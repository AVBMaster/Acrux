using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Css.ElementStyles;
using UpBrowser.Core.Performance;

namespace UpBrowser.Core.Css.Resolver;

/// <summary>
/// CSS Cascade resolution system - inspired by Blink's StyleCascade.
/// Two-phase design: Analyze (build CascadeMap) 锟?Apply (build ComputedStyle).
/// Priority encoding: Importance 锟?Origin 锟?TreeOrder 锟?LayerOrder 锟?Position
/// </summary>
public class CascadeResolver
{
    private readonly List<Stylesheet> _stylesheets = new();
    private readonly List<CascadeOrigin> _stylesheetOrigins = new();
    private readonly CssParser _inlineParser = new();
    private readonly SelectorMatcher _matcher = new();
    private readonly CascadeMap _cascadeMap = new();
    private readonly MatchedPropertiesCache _cache = new();

    private ComputedStyle? _rootStyle;
    private int _treeOrderCounter;
    private float _viewportWidth = 1024f;
    private float _viewportHeight = 768f;
    private string _colorScheme = "light";

    public void SetViewport(float width, float height, string colorScheme = "light")
    {
        _viewportWidth = width;
        _viewportHeight = height;
        _colorScheme = colorScheme;
    }

    public void AddStylesheet(Stylesheet stylesheet, CascadeOrigin origin = CascadeOrigin.Author)
    {
        _stylesheets.Add(stylesheet);
        _stylesheetOrigins.Add(origin);
    }

    public void ResolveStyles(Document document, float viewportWidth = 1024f, float viewportHeight = 768f, string colorScheme = "light")
    {
        var sw = UpBrowser.Core.Performance.Clock.NowNanos();
        _treeOrderCounter = 0;
        _cache.Clear();
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _colorScheme = colorScheme;

        _rootStyle = CreateUserAgentStyle("html");
        _rootStyle.FontSize = 16;

        var root = document.DocumentElement ?? document.Body;
        if (root != null)
            ResolveElement(root, _rootStyle);
        UpBrowser.Core.Performance.PipelineTimings.Style.AddSample(UpBrowser.Core.Performance.Clock.NowNanos() - sw);
    }

    /// <summary>
    /// PerformanceHub-aware resolve path. Skips the cascade for elements whose
    /// inherited style context is identical to a previously computed element with
    /// the same structural signature. The first call to <see cref="ResolveStyles"/>
    /// remains untouched so existing callers are not affected.
    /// </summary>
    public void ResolveStylesIncremental(Document document,
        UpBrowser.Core.Performance.Rendering.SharedStyleCache sharedCache,
        float viewportWidth = 1024f, float viewportHeight = 768f, string colorScheme = "light")
    {
        var sw = UpBrowser.Core.Performance.Clock.NowNanos();
        _treeOrderCounter = 0;
        _cache.Clear();
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _colorScheme = colorScheme;

        _rootStyle = CreateUserAgentStyle("html");
        _rootStyle.FontSize = 16;

        var root = document.DocumentElement ?? document.Body;
        if (root != null)
            ResolveElementIncremental(root, _rootStyle, sharedCache, document);
        UpBrowser.Core.Performance.PipelineTimings.Style.AddSample(UpBrowser.Core.Performance.Clock.NowNanos() - sw);
    }

    private void ResolveElementIncremental(Element element, ComputedStyle? parentStyle,
        UpBrowser.Core.Performance.Rendering.SharedStyleCache sharedCache, Document document)
    {
        int stylesheetCount = _stylesheets.Count;
        var signature = UpBrowser.Core.Performance.Rendering.StyleSignature.ComputeSignature(
            element, stylesheetCount, _viewportWidth, _colorScheme);

        if (parentStyle is not null && element.ComputedStyle is not null
            && element.ComputedStyle.GetType() == typeof(ComputedStyle))
        {
            // Element already has a freshly computed style that matches the parent
            // and signature 锟?skip the entire cascade for this element.
            if (UpBrowser.Core.Performance.DirtyState.IsClean(element)
                && sharedCache.TryGetShared(signature) is not null)
            {
                return;
            }
        }

        ResolveElement(element, parentStyle);
    }

    private void ResolveElement(Element element, ComputedStyle? parentStyle)
    {
        int treeOrder = _treeOrderCounter++;

        var style = new ComputedStyle();

        if (parentStyle != null)
            InheritProperties(style, parentStyle);

        var cacheKey = new CacheKey(element, _stylesheets.Count);
        if (_cache.TryGet(cacheKey, out var cached))
        {
            CopyStyle(style, cached);
            element.ComputedStyle = style;
            ResolveChildren(element, style);
            return;
        }

        _cascadeMap.Clear();

        AnalyzeUAStyles(element, treeOrder);
        AnalyzePresentationalHints(element, treeOrder);
        Analyze(element, treeOrder);
        AnalyzeInlineStyle(element, treeOrder);
        AnalyzeJsModifiedStyle(element, treeOrder);

        ApplyCustomProperties(style, element);
        ApplyCascadeAffecting(style, element);
        ApplyHighPriority(style, parentStyle, element);
        ApplyMatchResult(style, element, parentStyle);

        // Expose collected ::-webkit-scrollbar-* side-car to painting.
        style.ScrollbarCustom = element.ScrollbarCustom;

        // Apply @keyframes final state for animated elements
        if (!string.IsNullOrEmpty(style.AnimationName) && style.AnimationName != "none")
        {
            ApplyKeyframeAnimation(element, style, parentStyle);
        }

        _cache.Set(cacheKey, style.Clone());

        element.ComputedStyle = style;
        ResolveChildren(element, style);
    }

    private void ApplyKeyframeAnimation(Element element, ComputedStyle style, ComputedStyle? parentStyle = null)
    {
        var name = style.AnimationName;
        foreach (var stylesheet in _stylesheets)
        {
            foreach (var kfRule in stylesheet.KeyframesRules)
            {
                if (!string.Equals(kfRule.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Find the 100% keyframe (or the last keyframe)
                KeyframeBlock? finalBlock = null;
                float maxPct = -1;
                foreach (var block in kfRule.Keyframes)
                {
                    if (float.TryParse(block.Selector.TrimEnd('%'), out var pct) && pct >= maxPct)
                    {
                        maxPct = pct;
                        finalBlock = block;
                    }
                }

                if (finalBlock != null)
                {
                    var expanded = ShorthandExpander.Expand(finalBlock.Properties);
                    foreach (var prop in expanded)
                    {
                        var animPriority = new LegacyCascadePriority(
                            importance: false,
                            origin: CascadeOrigin.Animation,
                            treeOrder: int.MaxValue
                        );
                        _cascadeMap.Insert(prop.Key, prop.Value, animPriority);
                    }
                    ApplyCascadeAffecting(style, element);
                    ApplyHighPriority(style, parentStyle ?? style, element);
                    ApplyMatchResult(style, element, parentStyle);
                }
                break;
            }
        }
    }

    private void ResolveChildren(Element element, ComputedStyle parentStyle)
    {
        foreach (var child in element.Children)
        {
            if (child is Element childElement)
                ResolveElement(childElement, parentStyle);
        }
    }

    /// <summary>
    /// Routes ::-webkit-scrollbar-* pseudo-element declarations into the
    /// element's <see cref="Dom.ScrollbarStyles"/> side-car. Returns false when
    /// the rule is not a scrollbar rule so normal cascade continues.
    /// </summary>
    private static bool TryCollectScrollbarStyle(Element element, string selector, Dictionary<string, string> properties)
    {
        var slot = GetScrollbarSlot(selector);
        if (slot == null)
            return false;

        element.ScrollbarCustom ??= new ScrollbarStyles();
        var target = slot.Value switch
        {
            ScrollbarSlot.Thumb => element.ScrollbarCustom.Thumb,
            ScrollbarSlot.Track => element.ScrollbarCustom.Track,
            ScrollbarSlot.Corner => element.ScrollbarCustom.Corner,
            _ => element.ScrollbarCustom.Bar,
        };

        foreach (var prop in properties)
        {
            var name = prop.Key.ToLowerInvariant();
            var value = prop.Value?.Trim() ?? "";
            try
            {
                switch (name)
                {
                    case "background":
                    case "background-color":
                        target.Background = ColorParser.Parse(value);
                        break;
                    case "width" when slot == ScrollbarSlot.Bar:
                        if (TryParsePx(value, out var w)) { target.Thickness = (int)w; target.HasThickness = true; }
                        break;
                    case "height" when slot == ScrollbarSlot.Bar:
                        // Vertical bars use width; horizontal use height 鈥?keep max
                        // of both so one declaration per axis still works.
                        if (TryParsePx(value, out var hgt) && hgt > target.Thickness)
                        { target.Thickness = (int)hgt; target.HasThickness = true; }
                        break;
                    case "border-radius":
                        if (TryParsePx(value, out var r)) target.BorderRadius = Math.Max(0, r);
                        break;
                    case "border":
                    case "border-width":
                        if (TryParsePx(FirstToken(value), out var bw)) target.BorderWidth = bw;
                        break;
                    case "border-color":
                        target.BorderColor = ColorParser.Parse(LastToken(value));
                        break;
                }
            }
            catch { /* malformed declaration 鈥?ignore like the cascade does */ }
        }
        return true;

        static ScrollbarSlot? GetScrollbarSlot(string sel)
        {
            if (!sel.Contains("::-webkit-scrollbar", StringComparison.OrdinalIgnoreCase))
                return null;
            if (sel.Contains("scrollbar-thumb", StringComparison.OrdinalIgnoreCase)) return ScrollbarSlot.Thumb;
            if (sel.Contains("scrollbar-track-piece", StringComparison.OrdinalIgnoreCase)) return ScrollbarSlot.Track;
            if (sel.Contains("scrollbar-track", StringComparison.OrdinalIgnoreCase)) return ScrollbarSlot.Track;
            if (sel.Contains("scrollbar-corner", StringComparison.OrdinalIgnoreCase)) return ScrollbarSlot.Corner;
            if (sel.Contains("scrollbar-button", StringComparison.OrdinalIgnoreCase)) return ScrollbarSlot.Button;
            return ScrollbarSlot.Bar;
        }

        static bool TryParsePx(string v, out float px)
        {
            px = 0;
            v = v.Trim();
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(v[..^2], out px)) return true;
            if (float.TryParse(v, out px)) return true; // unitless zero etc.
            return false;
        }

        static string FirstToken(string v) { int i = v.IndexOf(' '); return i > 0 ? v[..i] : v; }
        static string LastToken(string v) { int i = v.LastIndexOf(' '); return i > 0 ? v[(i + 1)..] : v; }
    }

    private enum ScrollbarSlot { Bar, Thumb, Track, Corner, Button }

    /// <summary>Scrollbar-color &lt;thumb&gt; &lt;track&gt; ("auto" resets a slot).</summary>
    private static void ApplyScrollbarColors(ComputedStyle style, string value)
    {
        var parts = ShorthandExpander.SplitShorthand(value);
        if (parts.Count == 0) return;

        style.ScrollbarThumbColor = parts[0].Trim() == "auto"
            ? null
            : ColorParser.Parse(parts[0].Trim());

        style.ScrollbarTrackColor = parts.Count >= 2
            ? (parts[1].Trim() == "auto" ? null : ColorParser.Parse(parts[1].Trim()))
            : style.ScrollbarThumbColor;
    }

    /// <summary>
    /// A5: `column-rule: &lt;width&gt; || &lt;style&gt; || &lt;color&gt;` 鈥?same token
    /// classification family as border shorthands.
    /// </summary>
    private void ParseColumnRule(string value, ComputedStyle style)
    {
        foreach (var raw in ShorthandExpander.SplitShorthand(value))
        {
            var p = raw.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            switch (p)
            {
                case "thin": style.ColumnRuleWidth = 1f; continue;
                case "medium" or "auto": style.ColumnRuleWidth = 3f; continue;
                case "thick": style.ColumnRuleWidth = 5f; continue;
            }

            if (p is "none" or "hidden" or "solid" or "dashed" or "dotted"
                or "double" or "groove" or "ridge" or "inset" or "outset")
            {
                style.ColumnRuleStyle = ParseBorderStyleValue(p);
                continue;
            }

            if (p.StartsWith('#') || p.StartsWith("rgb") || p.StartsWith("hsl")
                || ColorParser.IsColorName(p))
            {
                style.ColumnRuleColor = ColorParser.Parse(p);
                continue;
            }

            if (float.TryParse(p.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? p[..^2] : p,
                    out var wpx))
                style.ColumnRuleWidth = Math.Max(0, wpx);
        }
    }

    /// <summary>
    /// Analyze phase: traverse all author stylesheet declarations into CascadeMap.
    /// Source order is preserved: rules are visited in document order and a
    /// monotonically increasing <see cref="LegacyCascadePriority.SourceOrder"/> plus
    /// selector specificity decide the winner, so equal-specificity rules resolve
    /// by source order exactly like the CSS spec requires.
    /// </summary>
    private void Analyze(Element element, int treeOrder)
    {
        int sourceOrder = 0;

        for (int i = 0; i < _stylesheets.Count; i++)
        {
            var stylesheet = _stylesheets[i];
            var origin = i < _stylesheetOrigins.Count ? _stylesheetOrigins[i] : CascadeOrigin.Author;

            foreach (var rule in stylesheet.Rules)
                ProcessRule(element, rule, origin, treeOrder, ref sourceOrder);

            foreach (var mediaRule in stylesheet.MediaRules)
            {
                if (MediaQueryEvaluator.Evaluate(mediaRule.Condition, _viewportWidth, _viewportHeight, _colorScheme))
                    ProcessGroup(element, mediaRule, origin, treeOrder, ref sourceOrder);
            }

            foreach (var supportsRule in stylesheet.SupportsRules)
            {
                if (EvaluateSupportsCondition(supportsRule.Condition))
                    ProcessGroup(element, supportsRule, origin, treeOrder, ref sourceOrder);
            }

            foreach (var layerRule in stylesheet.LayerRules)
                ProcessGroup(element, layerRule, origin, treeOrder, ref sourceOrder);

            foreach (var containerRule in stylesheet.ContainerRules)
            {
                if (EvaluateContainerCondition(containerRule.Condition))
                    ProcessGroup(element, containerRule, origin, treeOrder, ref sourceOrder);
            }

            foreach (var scopeRule in stylesheet.ScopeRules)
            {
                if (ScopeMatches(scopeRule, element))
                    ProcessGroup(element, scopeRule, origin, treeOrder, ref sourceOrder);
            }

            foreach (var startingRule in stylesheet.StartingStyleRules)
                ProcessGroup(element, startingRule, origin, treeOrder, ref sourceOrder);
        }
    }

    /// <summary>Recursively analyzes a group rule's direct and nested rules.</summary>
    private void ProcessGroup(Element element, CssAtRuleGroup group, CascadeOrigin origin, int treeOrder, ref int sourceOrder)
    {
        foreach (var rule in group.Rules)
            ProcessRule(element, rule, origin, treeOrder, ref sourceOrder);

        foreach (var media in group.SubMediaRules)
        {
            if (MediaQueryEvaluator.Evaluate(media.Condition, _viewportWidth, _viewportHeight, _colorScheme))
                ProcessGroup(element, media, origin, treeOrder, ref sourceOrder);
        }
        foreach (var supports in group.SubSupportsRules)
        {
            if (EvaluateSupportsCondition(supports.Condition))
                ProcessGroup(element, supports, origin, treeOrder, ref sourceOrder);
        }
        foreach (var layer in group.SubLayerRules)
            ProcessGroup(element, layer, origin, treeOrder, ref sourceOrder);
        foreach (var container in group.SubContainerRules)
        {
            if (EvaluateContainerCondition(container.Condition))
                ProcessGroup(element, container, origin, treeOrder, ref sourceOrder);
        }
        foreach (var scope in group.SubScopeRules)
        {
            if (ScopeMatches(scope, element))
                ProcessGroup(element, scope, origin, treeOrder, ref sourceOrder);
        }
        foreach (var starting in group.SubStartingStyleRules)
            ProcessGroup(element, starting, origin, treeOrder, ref sourceOrder);
    }

    /// <summary>Whether an @scope boundary matches the element (best-effort).</summary>
    private static bool ScopeMatches(ScopeRule scope, Element element)
    {
        if (string.IsNullOrEmpty(scope.ScopeRoot)) return true;
        var sel = scope.ScopeRoot.Trim();
        if (sel.StartsWith("(") && sel.EndsWith(")")) sel = sel[1..^1].Trim();
        if (sel.Length == 0) return true;
        return CssSelectorMatcher.Matches(sel, element);
    }

    private bool EvaluateContainerCondition(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        // Container conditions are evaluated against named containers which the
        // layout engine does not yet expose; resolve against the viewport size as
        // a fallback so content is not spuriously dropped.
        return MediaQueryEvaluator.Evaluate(condition, _viewportWidth, _viewportHeight, _colorScheme);
    }

    /// <summary>Analyzes a single matched rule into the cascade map.</summary>
    private void ProcessRule(Element element, CssRule rule, CascadeOrigin origin, int treeOrder, ref int sourceOrder)
    {
        if (!_matcher.Matches(rule, element))
            return;

        string selectorText = rule.OriginalSelectorText.Length > 0 ? rule.OriginalSelectorText : rule.Selector;

        // Scrollbar pseudo rules go to the side-car, not the cascade.
        if (TryCollectScrollbarStyle(element, selectorText, rule.Properties))
            return;

        bool isBefore = selectorText.Contains("::before", StringComparison.OrdinalIgnoreCase);
        bool isAfter = selectorText.Contains("::after", StringComparison.OrdinalIgnoreCase);

        if (isBefore)
        {
            element.BeforeStyles ??= new Dictionary<string, string>();
            foreach (var prop in rule.Properties)
                element.BeforeStyles[prop.Key] = prop.Value;
            return;
        }
        if (isAfter)
        {
            element.AfterStyles ??= new Dictionary<string, string>();
            foreach (var prop in rule.Properties)
                element.AfterStyles[prop.Key] = prop.Value;
            return;
        }

        var (specA, specB, specC, specD) = rule.Specificity;
        var expandedProps = ShorthandExpander.Expand(rule.Properties);
        foreach (var prop in expandedProps)
        {
            bool isImportant = rule.IsPropertyImportant(prop.Key) || rule.IsPropertyImportant(GetOriginalShorthand(prop.Key));
            var priority = new LegacyCascadePriority(
                importance: isImportant,
                origin: origin,
                treeOrder: treeOrder,
                specA: specA,
                specB: specB,
                specC: specC,
                specD: specD,
                sourceOrder: sourceOrder
            );
            sourceOrder++;
            _cascadeMap.Insert(prop.Key, prop.Value, priority);
        }
    }

    private void AnalyzeUAStyles(Element element, int treeOrder)
    {
        var style = new ComputedStyle();
        ElementStyleRegistry.ApplyUserAgentStyle(style, element.TagName, element);

        var props = new Dictionary<string, string>();
        CollectNonDefaultProperties(style, props);

        foreach (var kv in props)
        {
            var priority = new LegacyCascadePriority(false, CascadeOrigin.UserAgent, treeOrder);
            _cascadeMap.Insert(kv.Key, kv.Value, priority);
        }
    }

    /// <summary>Insert one HTML presentational size attribute (a non-negative
    /// integer, optionally followed by '%') as the given CSS property.</summary>
    private void InsertSizeHint(Element element, string attribute, string property,
        LegacyCascadePriority priority)
    {
        var raw = element.GetAttribute(attribute)?.Trim();
        if (string.IsNullOrEmpty(raw)) return;
        bool isPercent = raw.EndsWith('%');
        var number = isPercent ? raw[..^1] : raw;
        if (!float.TryParse(number.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
            return;
        _cascadeMap.Insert(property, isPercent ? $"{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}%" : $"{value}px", priority);
    }

    private void AnalyzePresentationalHints(Element element, int treeOrder)
    {
        string tag = element.TagName.ToUpperInvariant();
        var phPriority = new LegacyCascadePriority(false, CascadeOrigin.UserAgent, treeOrder);

        if (tag == "VIDEO" || tag == "IFRAME" || tag == "EMBED" || tag == "OBJECT")
        {
            string? wAttr = element.GetAttribute("width");
            string? hAttr = element.GetAttribute("height");
            if (uint.TryParse(wAttr, out uint w) && w > 0)
                _cascadeMap.Insert("width", $"{w}px", phPriority);
            if (uint.TryParse(hAttr, out uint h) && h > 0)
                _cascadeMap.Insert("height", $"{h}px", phPriority);
        }

        if (tag == "IMG")
        {
            string? wAttr = element.GetAttribute("width");
            string? hAttr = element.GetAttribute("height");
            if (uint.TryParse(wAttr, out uint w) && w > 0)
                _cascadeMap.Insert("width", $"{w}px", phPriority);
            if (uint.TryParse(hAttr, out uint h) && h > 0)
                _cascadeMap.Insert("height", $"{h}px", phPriority);
        }

        if (tag == "TABLE")
        {
            string? border = element.GetAttribute("border");
            string? cellspacing = element.GetAttribute("cellspacing");
            if (int.TryParse(border, out int b) && b > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    string side = i switch { 0 => "top", 1 => "right", 2 => "bottom", 3 => "left" };
                    _cascadeMap.Insert($"border-{side}-width", $"{b}px", phPriority);
                    _cascadeMap.Insert($"border-{side}-style", "solid", phPriority);
                    _cascadeMap.Insert($"border-{side}-color", "#000000", phPriority);
                }
            }
            if (int.TryParse(cellspacing, out int cs))
                _cascadeMap.Insert("border-spacing", $"{cs}px", phPriority);
            // HTML §10.3.3.2.2 presentational hints: width is a used-width hint,
            // height the minimum height of the table.
            InsertSizeHint(element, "width", "width", phPriority);
            InsertSizeHint(element, "height", "min-height", phPriority);
        }

        if (tag is "TD" or "TH" or "TR" or "COL" or "COLGROUP")
        {
            InsertSizeHint(element, "width", "width", phPriority);
            // A cell's height attribute is its height; a row's is a minimum.
            InsertSizeHint(element, "height", tag == "TR" ? "min-height" : "height", phPriority);
        }

        if (tag == "TD" || tag == "TH")
        {
            var tableParent = element.ParentElement;
            while (tableParent != null && tableParent.TagName.ToUpperInvariant() != "TABLE")
                tableParent = tableParent.ParentElement;
            if (tableParent != null)
            {
                string? cellpadding = tableParent.GetAttribute("cellpadding");
                if (int.TryParse(cellpadding, out int cp) && cp >= 0)
                {
                    _cascadeMap.Insert("padding", $"{cp}px", phPriority);
                }
                string? border = tableParent.GetAttribute("border");
                if (int.TryParse(border, out int b) && b > 0)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        string side = i switch { 0 => "top", 1 => "right", 2 => "bottom", 3 => "left" };
                        _cascadeMap.Insert($"border-{side}-width", $"{b}px", phPriority);
                        _cascadeMap.Insert($"border-{side}-style", "solid", phPriority);
                        _cascadeMap.Insert($"border-{side}-color", "#000000", phPriority);
                    }
                }
            }
        }
    }

    private void CollectNonDefaultProperties(ComputedStyle style, Dictionary<string, string> props)
    {
        var def = new ComputedStyle();
        if (style.Display != def.Display) props["display"] = style.Display.ToCssString();
        if (style.FontSize != def.FontSize) props["font-size"] = $"{style.FontSize}px";
        if (style.FontWeight != def.FontWeight) props["font-weight"] = ((int)style.FontWeight).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (style.FontStyle != def.FontStyle) props["font-style"] = style.FontStyle == FontStyleType.Italic ? "italic" : "normal";
        if (style.FontFamily != def.FontFamily) props["font-family"] = style.FontFamily;
        if (style.LineHeight != def.LineHeight) props["line-height"] = style.LineHeight.ToString("0.000");
        if (style.Color != def.Color) props["color"] = $"rgba({style.Color.Red},{style.Color.Green},{style.Color.Blue},{style.Color.Alpha / 255f})";
        if (style.BackgroundColor != def.BackgroundColor && style.BackgroundColor.HasValue) props["background-color"] = $"rgba({style.BackgroundColor.Value.Red},{style.BackgroundColor.Value.Green},{style.BackgroundColor.Value.Blue},{style.BackgroundColor.Value.Alpha / 255f})";
        AddLengthProp(props, style.MarginTop, "margin-top", def.MarginTop);
        AddLengthProp(props, style.MarginBottom, "margin-bottom", def.MarginBottom);
        AddLengthProp(props, style.MarginLeft, "margin-left", def.MarginLeft);
        AddLengthProp(props, style.MarginRight, "margin-right", def.MarginRight);
        AddLengthProp(props, style.PaddingTop, "padding-top", def.PaddingTop);
        AddLengthProp(props, style.PaddingBottom, "padding-bottom", def.PaddingBottom);
        AddLengthProp(props, style.PaddingLeft, "padding-left", def.PaddingLeft);
        AddLengthProp(props, style.PaddingRight, "padding-right", def.PaddingRight);
        if (style.WhiteSpace != def.WhiteSpace) props["white-space"] = style.WhiteSpace.ToString().ToLowerInvariant();
        if (style.ListStyleType != def.ListStyleType) props["list-style-type"] = style.ListStyleType.ToString().ToLowerInvariant();
        if (style.BorderCollapse != def.BorderCollapse) props["border-collapse"] = style.BorderCollapse ? "collapse" : "separate";
        if (style.BackgroundImage is { Count: > 0 }) props["background-image"] = string.Join(", ", style.BackgroundImage);
        if (style.TextAlign != def.TextAlign) props["text-align"] = style.TextAlign.ToString().ToLowerInvariant();
        if (style.TextDecorationLine != TextDecorationLineType.None || def.TextDecorationLine != TextDecorationLineType.None)
        {
            // 'text-decoration-line' is a list, so the flags are written back out in
            // their canonical order.
            var lines = new List<string>();
            if (style.TextDecorationLine.HasUnderline()) lines.Add("underline");
            if (style.TextDecorationLine.HasOverline()) lines.Add("overline");
            if (style.TextDecorationLine.HasLineThrough()) lines.Add("line-through");
            props["text-decoration"] = lines.Count > 0 ? string.Join(' ', lines) : "none";
            if (style.TextDecorationStyle != def.TextDecorationStyle)
                props["text-decoration-style"] = style.TextDecorationStyle.ToString().ToLowerInvariant();
            if (!float.IsNaN(style.TextDecorationThickness))
                props["text-decoration-thickness"] = style.TextDecorationThickness + "px";
            if (!style.TextUnderlineOffsetIsAuto)
                props["text-underline-offset"] = style.TextUnderlineOffset + "px";
        }
        if (style.VerticalAlign != def.VerticalAlign) props["vertical-align"] = style.VerticalAlign.ToString().ToLowerInvariant();
        if (style.Overflow != def.Overflow) props["overflow"] = style.Overflow.ToString().ToLowerInvariant();
        if (style.Position != def.Position) props["position"] = style.Position.ToString().ToLowerInvariant();
        if (style.Float != def.Float) props["float"] = style.Float.ToString().ToLowerInvariant();
        if (style.Clear != def.Clear) props["clear"] = style.Clear.ToString().ToLowerInvariant();
        if (style.BoxSizing != def.BoxSizing) props["box-sizing"] = style.BoxSizing == BoxSizingType.BorderBox ? "border-box" : "content-box";
        if (style.Cursor != def.Cursor) props["cursor"] = style.Cursor ?? "auto";
        if (style.Resize != def.Resize) props["resize"] = style.Resize == ResizeType.None ? "none" : style.Resize.ToString().ToLowerInvariant();

        AddLengthProp(props, style.Width, "width", def.Width);
        AddLengthProp(props, style.Height, "height", def.Height);
        if (style.MinWidth != def.MinWidth && style.MinWidth != null) AddLengthProp(props, style.MinWidth, "min-width", def.MinWidth!);
        if (style.MinHeight != def.MinHeight && style.MinHeight != null) AddLengthProp(props, style.MinHeight, "min-height", def.MinHeight!);
        if (style.MaxWidth != def.MaxWidth && style.MaxWidth != null) AddLengthProp(props, style.MaxWidth, "max-width", def.MaxWidth!);
        if (style.MaxHeight != def.MaxHeight && style.MaxHeight != null) AddLengthProp(props, style.MaxHeight, "max-height", def.MaxHeight!);

        if (style.BorderTopWidth != def.BorderTopWidth) props["border-top-width"] = $"{style.BorderTopWidth}px";
        if (style.BorderRightWidth != def.BorderRightWidth) props["border-right-width"] = $"{style.BorderRightWidth}px";
        if (style.BorderBottomWidth != def.BorderBottomWidth) props["border-bottom-width"] = $"{style.BorderBottomWidth}px";
        if (style.BorderLeftWidth != def.BorderLeftWidth) props["border-left-width"] = $"{style.BorderLeftWidth}px";
        if (style.BorderTopStyle != def.BorderTopStyle) props["border-top-style"] = style.BorderTopStyle.ToString().ToLowerInvariant();
        if (style.BorderRightStyle != def.BorderRightStyle) props["border-right-style"] = style.BorderRightStyle.ToString().ToLowerInvariant();
        if (style.BorderBottomStyle != def.BorderBottomStyle) props["border-bottom-style"] = style.BorderBottomStyle.ToString().ToLowerInvariant();
        if (style.BorderLeftStyle != def.BorderLeftStyle) props["border-left-style"] = style.BorderLeftStyle.ToString().ToLowerInvariant();
        if (style.BorderTopColor != def.BorderTopColor) props["border-top-color"] = $"#{style.BorderTopColor.Red:X2}{style.BorderTopColor.Green:X2}{style.BorderTopColor.Blue:X2}";
        if (style.BorderRightColor != def.BorderRightColor) props["border-right-color"] = $"#{style.BorderRightColor.Red:X2}{style.BorderRightColor.Green:X2}{style.BorderRightColor.Blue:X2}";
        if (style.BorderBottomColor != def.BorderBottomColor) props["border-bottom-color"] = $"#{style.BorderBottomColor.Red:X2}{style.BorderBottomColor.Green:X2}{style.BorderBottomColor.Blue:X2}";
        if (style.BorderLeftColor != def.BorderLeftColor) props["border-left-color"] = $"#{style.BorderLeftColor.Red:X2}{style.BorderLeftColor.Green:X2}{style.BorderLeftColor.Blue:X2}";
        if (style.BorderTopLeftRadius != def.BorderTopLeftRadius) props["border-top-left-radius"] = $"{style.BorderTopLeftRadius}px";
        if (style.BorderTopRightRadius != def.BorderTopRightRadius) props["border-top-right-radius"] = $"{style.BorderTopRightRadius}px";
        if (style.BorderBottomLeftRadius != def.BorderBottomLeftRadius) props["border-bottom-left-radius"] = $"{style.BorderBottomLeftRadius}px";
        if (style.BorderBottomRightRadius != def.BorderBottomRightRadius) props["border-bottom-right-radius"] = $"{style.BorderBottomRightRadius}px";

        if (style.BorderRowSpacing is { } rowSpacing && rowSpacing != style.BorderSpacing)
            props["border-spacing"] = $"{style.BorderSpacing}px {rowSpacing}px";
        else if (style.BorderSpacing != def.BorderSpacing) props["border-spacing"] = $"{style.BorderSpacing}px";

        if (style.Visibility != def.Visibility) props["visibility"] = style.Visibility.ToString().ToLowerInvariant();
        if (style.ZIndex != def.ZIndex) props["z-index"] = style.ZIndex?.ToString() ?? "auto";
        if (style.Opacity != def.Opacity) props["opacity"] = style.Opacity.ToString("0.000");
        if (style.TextIndent != def.TextIndent) props["text-indent"] = $"{style.TextIndent}px";
        if (style.LetterSpacing != def.LetterSpacing) props["letter-spacing"] = $"{style.LetterSpacing}px";
        if (style.WordSpacing != def.WordSpacing) props["word-spacing"] = $"{style.WordSpacing}px";
        if (style.TextTransform != def.TextTransform) props["text-transform"] = style.TextTransform;
        if (style.TextOverflow != def.TextOverflow) props["text-overflow"] = style.TextOverflow.ToString().ToLowerInvariant();
        if (!string.IsNullOrEmpty(style.Transform)) props["transform"] = style.Transform;
        if (style.ObjectFit != def.ObjectFit) props["object-fit"] = style.ObjectFit.ToString().ToLowerInvariant();

        if (style.FlexDirection != def.FlexDirection) props["flex-direction"] = style.FlexDirection.ToString().ToLowerInvariant();
        if (style.FlexWrap != def.FlexWrap) props["flex-wrap"] = style.FlexWrap.ToString().ToLowerInvariant();
        if (style.FlexGrow != def.FlexGrow) props["flex-grow"] = style.FlexGrow.ToString("0");
        if (style.FlexShrink != def.FlexShrink) props["flex-shrink"] = style.FlexShrink.ToString("0");
        AddLengthProp(props, style.FlexBasis, "flex-basis", def.FlexBasis);
        if (style.JustifyContent != def.JustifyContent) props["justify-content"] = style.JustifyContent.ToString().ToLowerInvariant();
        if (style.AlignItems != def.AlignItems) props["align-items"] = style.AlignItems.ToString().ToLowerInvariant();
        if (style.AlignSelf != def.AlignSelf) props["align-self"] = style.AlignSelf.ToString().ToLowerInvariant();
        if (style.Order != def.Order) props["order"] = style.Order.ToString();

        if (style.OutlineWidth != def.OutlineWidth) props["outline-width"] = $"{style.OutlineWidth}px";
        if (style.OutlineStyle != def.OutlineStyle) props["outline-style"] = style.OutlineStyle.ToString().ToLowerInvariant();
        if (style.OutlineColor != def.OutlineColor) props["outline-color"] = $"#{style.OutlineColor.Red:X2}{style.OutlineColor.Green:X2}{style.OutlineColor.Blue:X2}";
        if (style.OutlineOffset != def.OutlineOffset) props["outline-offset"] = $"{style.OutlineOffset}px";

        if (style.BoxShadow != null && style.BoxShadow.Count > 0)
            props["box-shadow"] = string.Join(", ", style.BoxShadow.Select(b =>
                (b.Inset ? "inset " : "") + $"{b.OffsetX:0.##}px {b.OffsetY:0.##}px {b.BlurRadius:0.##}px{(b.Spread != 0 ? $" {b.Spread:0.##}px" : "")} #{b.Color.Red:X2}{b.Color.Green:X2}{b.Color.Blue:X2}"));
        if (!string.IsNullOrEmpty(style.Filter)) props["filter"] = style.Filter!;
    }

    private static void AddLengthProp(Dictionary<string, string> props, Length length, string name, Length defaultLength)
    {
        if (length == null || length == defaultLength || length is AutoLength) return;
        if (length is PixelLength px) { if (px.Value != 0) props[name] = $"{px.Value}px"; }
        else { props[name] = length.ToString(); }
    }

    private void AnalyzeInlineStyle(Element element, int treeOrder)
    {
        var inlineStyleAttr = element.GetAttribute("style");
        if (string.IsNullOrEmpty(inlineStyleAttr)) return;

        var inlineProps = _inlineParser.ParseInlineStyle(inlineStyleAttr);
        var expandedProps = ShorthandExpander.Expand(inlineProps);
        foreach (var prop in expandedProps)
        {
            var priority = new LegacyCascadePriority(false, CascadeOrigin.Inline, treeOrder);
            _cascadeMap.Insert(prop.Key, prop.Value, priority);
        }
    }

    private void AnalyzeJsModifiedStyle(Element element, int treeOrder)
    {
        foreach (var kv in element.Style)
        {
            if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
            {
                var priority = new LegacyCascadePriority(false, CascadeOrigin.JsModified, treeOrder);
                _cascadeMap.Insert(kv.Key, kv.Value, priority);
            }
        }
    }

    /// <summary>
    /// Apply cascade-affecting properties first (direction, writing-mode, zoom).
    /// These affect how other properties are resolved.
    /// </summary>
    private void ApplyCascadeAffecting(ComputedStyle style, Element element)
    {
        if (_cascadeMap.TryGetValue("direction", out var dirVal))
        {
            var v = CssFunctionEvaluator.Evaluate(dirVal, element, style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (!TryApplyCssWideKeyword(style, "direction", v, null))
                style.Direction = v.ToLowerInvariant() == "rtl" ? "rtl" : "ltr";
        }
        if (_cascadeMap.TryGetValue("writing-mode", out var wmVal))
        {
            var v = CssFunctionEvaluator.Evaluate(wmVal, element, style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (!TryApplyCssWideKeyword(style, "writing-mode", v, null))
                style.WritingMode = ParseWritingMode(v);
        }
        if (_cascadeMap.TryGetValue("zoom", out var zoomVal))
        {
            var v = CssFunctionEvaluator.Evaluate(zoomVal, element, style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (!TryApplyCssWideKeyword(style, "zoom", v, null))
                style.Zoom = ParseZoom(v);
        }
    }

    /// <summary>
    /// Apply high-priority properties first (font properties).
    /// em/ch units depend on font-size, so font must be resolved first.
    /// </summary>
    private void ApplyHighPriority(ComputedStyle style, ComputedStyle? parentStyle, Element element)
    {
        if (_cascadeMap.TryGetValue("font-size", out var fontSizeVal))
        {
            var resolved = CssFunctionEvaluator.Evaluate(fontSizeVal, element, style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (!TryApplyCssWideKeyword(style, "font-size", resolved, parentStyle))
                style.FontSize = ParseFontSize(resolved, parentStyle);
        }

        if (_cascadeMap.TryGetValue("font-weight", out var fontWeightVal))
        {
            var v = CssFunctionEvaluator.Evaluate(fontWeightVal, element, style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (!TryApplyCssWideKeyword(style, "font-weight", v, parentStyle))
                style.FontWeight = ParseFontWeight(v);
        }

        if (_cascadeMap.TryGetValue("font-style", out var fontStyleVal))
        {
            var v = CssFunctionEvaluator.Evaluate(fontStyleVal, element, style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (!TryApplyCssWideKeyword(style, "font-style", v, parentStyle))
                style.FontStyle = ParseFontStyle(v, style);
        }

        if (_cascadeMap.TryGetValue("font-family", out var fontFamilyVal))
        {
            var v = CssFunctionEvaluator.Evaluate(fontFamilyVal, element, style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (!TryApplyCssWideKeyword(style, "font-family", v, parentStyle))
                style.FontFamily = ParseFontFamily(v);
        }

        if (_cascadeMap.TryGetValue("line-height", out var lineHeightVal))
        {
            var v = CssFunctionEvaluator.Evaluate(lineHeightVal, element, style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (!TryApplyCssWideKeyword(style, "line-height", v, parentStyle))
                Fonts.LineBoxMetrics.ApplyLineHeight(style, v);
        }
    }

    /// <summary>
    /// Store cascade-winning custom properties (--x) on the element's computed
    /// style. Runs before other property application so var() references resolve.
    /// Iterates to a fixed point so a custom property may reference another one
    /// declared on the same element (--a: var(--b); --b: red).
    /// </summary>
    private void ApplyCustomProperties(ComputedStyle style, Element element)
    {
        var pending = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in _cascadeMap.GetAll())
        {
            if (name.StartsWith("--"))
                pending.Add(new KeyValuePair<string, string>(name[2..], value));
        }

        // Resolve in a loop: once the value no longer references unresolved vars
        // (or is stable), store it. A handful of passes is enough for any chain.
        var unresolved = pending;
        for (int pass = 0; pass < pending.Count + 1 && unresolved.Count > 0; pass++)
        {
            var next = new List<KeyValuePair<string, string>>();
            foreach (var (name, value) in unresolved)
            {
                var resolved = CssFunctionEvaluator.Evaluate(value, element,
                    style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
                var stillUnresolved = value.Contains("var(") && resolved == value;
                if (stillUnresolved)
                    next.Add(new KeyValuePair<string, string>(name, value));
                else
                    style.SetCustomProperty(name, resolved);
            }
            unresolved = next;
        }
        // Anything still unresolved (cyclic / missing) keeps its raw value.
        foreach (var (name, value) in unresolved)
            style.SetCustomProperty(name, value);
    }

    /// <summary>
    /// Apply all remaining cascade-winning declarations. Custom properties (--x)
    /// are stored on the element's computed style first so that var() references
    /// in later declarations resolve against them.
    /// </summary>
    private void ApplyMatchResult(ComputedStyle style, Element element, ComputedStyle? parentStyle)
    {
        // Apply regular properties, resolving var() references.
        // The element's own style is the context for font-relative units
        // (ch/ex/cap/lh) inside calc()/min()/max() during evaluation.
        using var _fontUnitScope = FontUnitContext.Use(style);
        foreach (var (name, value) in _cascadeMap.GetAll())
        {
            if (name.StartsWith("--")) continue;
            if (IsHighPriorityProperty(name)) continue;

            var evaluated = CssFunctionEvaluator.Evaluate(value, element,
                style.FontSize, RootFontSize(), _viewportWidth, _viewportHeight);
            if (TryApplyCssWideKeyword(style, name, evaluated, parentStyle))
                continue;
            ApplyProperty(style, name, evaluated);
        }
    }

    /// <summary>
    /// Handles the CSS-wide keywords inherit/initial/unset/revert/revert-layer.
    /// Returns true when the value is a global keyword (already applied), false
    /// when the value is a normal declaration that the caller must apply.
    /// </summary>
    private static bool TryApplyCssWideKeyword(ComputedStyle style, string name, string value, ComputedStyle? parentStyle)
    {
        switch (value.Trim())
        {
            case "inherit":
                if (CssPropertyTraits.IsKnown(name))
                    CssPropertyTraits.Copy(style, parentStyle ?? new ComputedStyle(), name);
                return true;
            case "initial":
                if (CssPropertyTraits.IsKnown(name))
                    CssPropertyTraits.SetInitial(style, name);
                return true;
            case "unset":
                if (CssPropertyTraits.IsInherited(name))
                    CssPropertyTraits.Copy(style, parentStyle ?? new ComputedStyle(), name);
                else if (CssPropertyTraits.IsKnown(name))
                    CssPropertyTraits.SetInitial(style, name);
                return true;
            case "revert":
            case "revert-layer":
                // Best effort: treat as unset (author layer history is not tracked).
                if (CssPropertyTraits.IsInherited(name))
                    CssPropertyTraits.Copy(style, parentStyle ?? new ComputedStyle(), name);
                else if (CssPropertyTraits.IsKnown(name))
                    CssPropertyTraits.SetInitial(style, name);
                return true;
            default:
                return false;
        }
    }

    private float RootFontSize()
    {
        return _rootStyle?.FontSize ?? 16f;
    }

    /// <summary>Apply a single CSS property value to a style.  Public so that
    /// pseudo-element styles can be resolved outside the full cascade walk.</summary>
    /// <summary>Apply a single CSS property value to a style.  Public so that
    /// pseudo-element styles can be resolved outside the full cascade walk.
    /// Delegates to the shared Prism engine <see cref="CssPropertyApplier"/>.</summary>
    public void ApplyProperty(ComputedStyle style, string name, string value)
    {
        CssPropertyApplier.Apply(style, name, value);
    }

    private bool IsHighPriorityProperty(string name) => name switch
    {
        "font-size" or "font-weight" or "font-style" or "font-family" or "line-height" => true,
        _ => false
    };
    private void InheritProperties(ComputedStyle child, ComputedStyle parent)
    {
        child.Color = parent.Color;
        child.FontFamily = parent.FontFamily;
        child.FontSize = parent.FontSize;
        child.FontWeight = parent.FontWeight;
        child.FontStyle = parent.FontStyle;
        child.LineHeight = parent.LineHeight;
        // 'line-height' inherits its computed value, so the 'normal' flag and any
        // absolute length must travel with the multiplier.
        child.LineHeightIsNormal = parent.LineHeightIsNormal;
        child.LineHeightPx = parent.LineHeightPx;
        child.TextAlign = parent.TextAlign;
        child.TextAlignLast = parent.TextAlignLast;
        child.WhiteSpace = parent.WhiteSpace;
        child.WordBreak = parent.WordBreak;
        child.OverflowWrap = parent.OverflowWrap;
        child.Visibility = parent.Visibility;
        child.Cursor = parent.Cursor;
        child.Direction = parent.Direction;
        child.LetterSpacing = parent.LetterSpacing;
        child.WordSpacing = parent.WordSpacing;
        child.TextIndent = parent.TextIndent;
        child.TextTransform = parent.TextTransform;
        child.CaptionSide = parent.CaptionSide;
        child.EmptyCells = parent.EmptyCells;
        child.WritingMode = parent.WritingMode;
        child.Orphans = parent.Orphans;
        child.Widows = parent.Widows;
        child.Hyphens = parent.Hyphens;
        child.LineBreak = parent.LineBreak;
        child.TextJustify = parent.TextJustify;
        child.TextRendering = parent.TextRendering;
        child.TextShadow = new List<TextShadowValue>(parent.TextShadow);
        // 'text-decoration-*' is NOT inherited (CSS Text Decoration 4 §5.1): a
        // decoration on an ancestor still reaches its text, but by propagation at
        // paint time, where every originating box keeps its own line list, style,
        // color and metrics. Inheriting it here would merge nested decorations
        // into a single line and let an inner 'text-decoration: none' erase the
        // outer one.
        child.TextEmphasis = parent.TextEmphasis;
        child.TextEmphasisColor = parent.TextEmphasisColor;
        child.TextEmphasisStyle = parent.TextEmphasisStyle;
        child.TextEmphasisPosition = parent.TextEmphasisPosition;
        child.FontVariant = parent.FontVariant;
        child.FontVariantCaps = parent.FontVariantCaps;
        child.FontKerning = parent.FontKerning;
        child.FontStretch = parent.FontStretch;
        child.FontSynthesis = parent.FontSynthesis;
        child.FontOpticalSizing = parent.FontOpticalSizing;
        child.FontVariationSettings = parent.FontVariationSettings;
        child.FontFeatureSettings = parent.FontFeatureSettings;
        child.FontSizeAdjust = parent.FontSizeAdjust;
        child.Quotes = parent.Quotes;
        child.ImageRendering = parent.ImageRendering;
        child.AccentColor = parent.AccentColor;
        child.CaretColor = parent.CaretColor;
        child.ColorScheme = parent.ColorScheme;
        child.ForcedColorAdjust = parent.ForcedColorAdjust;
        child.ListStyleType = parent.ListStyleType;
        child.ListStylePosition = parent.ListStylePosition;
        child.TabSize = parent.TabSize;
        child.TabSizePx = parent.TabSizePx;
        child.BorderSpacing = parent.BorderSpacing;
        child.BorderRowSpacing = parent.BorderRowSpacing;
        child.RubyPosition = parent.RubyPosition;
        child.PointerEvents = parent.PointerEvents;
        child.UserSelect = parent.UserSelect;
        child.Zoom = parent.Zoom;
    }

    private ComputedStyle CreateUserAgentStyle(string tagName)
    {
        var style = new ComputedStyle();
        ElementStyleRegistry.ApplyUserAgentStyle(style, tagName);
        return style;
    }

    private void CopyStyle(ComputedStyle dest, ComputedStyle src)
    {
        dest.Width = src.Width; dest.Height = src.Height;
        dest.MinWidth = src.MinWidth; dest.MinHeight = src.MinHeight;
        dest.MaxWidth = src.MaxWidth; dest.MaxHeight = src.MaxHeight;
        dest.Display = src.Display; dest.Position = src.Position;
        dest.Float = src.Float; dest.Clear = src.Clear;
        dest.MarginTop = src.MarginTop; dest.MarginRight = src.MarginRight;
        dest.MarginBottom = src.MarginBottom; dest.MarginLeft = src.MarginLeft;
        dest.PaddingTop = src.PaddingTop; dest.PaddingRight = src.PaddingRight;
        dest.PaddingBottom = src.PaddingBottom; dest.PaddingLeft = src.PaddingLeft;
        dest.Color = src.Color; dest.BackgroundColor = src.BackgroundColor;
        dest.BackgroundImage = src.BackgroundImage;
        dest.BackgroundRepeat = src.BackgroundRepeat;
        dest.BackgroundPositionX = src.BackgroundPositionX;
        dest.BackgroundPositionY = src.BackgroundPositionY;
        dest.BackgroundSize = src.BackgroundSize;
        dest.BackgroundSizeWidth = src.BackgroundSizeWidth;
        dest.BackgroundSizeHeight = src.BackgroundSizeHeight;
        dest.BackgroundAttachment = src.BackgroundAttachment;
        dest.BackgroundClip = src.BackgroundClip;
        dest.BackgroundOrigin = src.BackgroundOrigin;
        dest.BackgroundBlendMode = src.BackgroundBlendMode;
        dest.FontFamily = src.FontFamily; dest.FontSize = src.FontSize;
        dest.FontWeight = src.FontWeight; dest.FontStyle = src.FontStyle;
        dest.LineHeight = src.LineHeight;
        dest.LineHeightIsNormal = src.LineHeightIsNormal;
        dest.LineHeightPx = src.LineHeightPx;
        dest.TextAlign = src.TextAlign;
        dest.TextDecoration = src.TextDecoration;
        dest.TextDecorationLine = src.TextDecorationLine;
        dest.TextDecorationStyle = src.TextDecorationStyle;
        dest.TextDecorationColor = src.TextDecorationColor;
        dest.TextDecorationThickness = src.TextDecorationThickness;
        dest.TextDecorationThicknessFromFont = src.TextDecorationThicknessFromFont;
        dest.TextUnderlineOffset = src.TextUnderlineOffset;
        dest.TextUnderlineOffsetIsAuto = src.TextUnderlineOffsetIsAuto;
        dest.TextUnderlinePosition = src.TextUnderlinePosition;
        dest.TextDecorationSkipInk = src.TextDecorationSkipInk;
        dest.BoxDecorationBreak = src.BoxDecorationBreak;
        dest.VerticalAlign = src.VerticalAlign;
        dest.WhiteSpace = src.WhiteSpace;
        dest.WordBreak = src.WordBreak;
        dest.OverflowWrap = src.OverflowWrap;
        dest.Visibility = src.Visibility;
        dest.Overflow = src.Overflow;
        dest.OverflowX = src.OverflowX; dest.OverflowY = src.OverflowY;
        dest.OverflowAnchor = src.OverflowAnchor;
        dest.OverscrollBehavior = src.OverscrollBehavior;
        dest.OverscrollBehaviorX = src.OverscrollBehaviorX;
        dest.OverscrollBehaviorY = src.OverscrollBehaviorY;
        dest.ZIndex = src.ZIndex;
        dest.BorderTopWidth = src.BorderTopWidth;
        dest.BorderRightWidth = src.BorderRightWidth;
        dest.BorderBottomWidth = src.BorderBottomWidth;
        dest.BorderLeftWidth = src.BorderLeftWidth;
        dest.BorderTopColor = src.BorderTopColor;
        dest.BorderRightColor = src.BorderRightColor;
        dest.BorderBottomColor = src.BorderBottomColor;
        dest.BorderLeftColor = src.BorderLeftColor;
        dest.BorderTopStyle = src.BorderTopStyle;
        dest.BorderRightStyle = src.BorderRightStyle;
        dest.BorderBottomStyle = src.BorderBottomStyle;
        dest.BorderLeftStyle = src.BorderLeftStyle;
        dest.BorderTopLeftRadius = src.BorderTopLeftRadius;
        dest.BorderTopRightRadius = src.BorderTopRightRadius;
        dest.BorderBottomRightRadius = src.BorderBottomRightRadius;
        dest.BorderBottomLeftRadius = src.BorderBottomLeftRadius;
        dest.BorderCollapse = src.BorderCollapse;
        dest.BorderSpacing = src.BorderSpacing;
        dest.BorderRowSpacing = src.BorderRowSpacing;
        dest.BoxSizing = src.BoxSizing;
        dest.Opacity = src.Opacity;
        dest.BoxShadow = src.BoxShadow;
        dest.FlexDirection = src.FlexDirection;
        dest.FlexWrap = src.FlexWrap;
        dest.FlexGrow = src.FlexGrow;
        dest.FlexShrink = src.FlexShrink;
        dest.FlexBasis = src.FlexBasis;
        dest.FlexFlow = src.FlexFlow;
        dest.JustifyContent = src.JustifyContent;
        dest.AlignItems = src.AlignItems;
        dest.AlignSelf = src.AlignSelf;
        dest.AlignContent = src.AlignContent;
        dest.JustifyItems = src.JustifyItems;
        dest.JustifySelf = src.JustifySelf;
        dest.PlaceContent = src.PlaceContent;
        dest.PlaceItems = src.PlaceItems;
        dest.PlaceSelf = src.PlaceSelf;
        dest.Order = src.Order;
        dest.Top = src.Top; dest.Bottom = src.Bottom;
        dest.Left = src.Left; dest.Right = src.Right;
        dest.ListStyleType = src.ListStyleType;
        dest.ListStylePosition = src.ListStylePosition;
        dest.ListStyleImage = src.ListStyleImage;
        dest.Cursor = src.Cursor;
        dest.Transform = src.Transform; dest.TransformOrigin = src.TransformOrigin;
        dest.Transition = src.Transition; dest.TransitionDelay = src.TransitionDelay;
        dest.TransitionDuration = src.TransitionDuration; dest.TransitionProperty = src.TransitionProperty;
        dest.TransitionTimingFunction = src.TransitionTimingFunction;
        dest.Animation = src.Animation; dest.AnimationName = src.AnimationName;
        dest.AnimationDuration = src.AnimationDuration; dest.AnimationTimingFunction = src.AnimationTimingFunction;
        dest.AnimationDelay = src.AnimationDelay; dest.AnimationIterationCount = src.AnimationIterationCount;
        dest.AnimationDirection = src.AnimationDirection; dest.AnimationFillMode = src.AnimationFillMode;
        dest.AnimationPlayState = src.AnimationPlayState;
        dest.PointerEvents = src.PointerEvents; dest.UserSelect = src.UserSelect;
        dest.Direction = src.Direction;
        dest.UnicodeBidi = src.UnicodeBidi;
        dest.WritingMode = src.WritingMode;
        dest.LetterSpacing = src.LetterSpacing;
        dest.WordSpacing = src.WordSpacing;
        dest.TextIndent = src.TextIndent;
        dest.TextTransform = src.TextTransform;
        dest.TextRendering = src.TextRendering;
        dest.TextOverflow = src.TextOverflow;
        dest.TextShadow = src.TextShadow;
        dest.TextEmphasis = src.TextEmphasis;
        dest.TextEmphasisColor = src.TextEmphasisColor;
        dest.TextEmphasisStyle = src.TextEmphasisStyle;
        dest.TextEmphasisPosition = src.TextEmphasisPosition;
        dest.OutlineWidth = src.OutlineWidth;
        dest.OutlineColor = src.OutlineColor;
        dest.OutlineStyle = src.OutlineStyle;
        dest.OutlineOffset = src.OutlineOffset;
        dest.TableLayout = src.TableLayout;
        dest.CaptionSide = src.CaptionSide;
        dest.EmptyCells = src.EmptyCells;
        dest.Content = src.Content;
        dest.CounterIncrement = src.CounterIncrement;
        dest.CounterReset = src.CounterReset;
        dest.CounterSet = src.CounterSet;
        dest.Quotes = src.Quotes;
        dest.AspectRatio = src.AspectRatio;
        dest.ObjectFit = src.ObjectFit;
        dest.ObjectPositionX = src.ObjectPositionX;
        dest.ObjectPositionY = src.ObjectPositionY;
        dest.Filter = src.Filter;
        dest.BackdropFilter = src.BackdropFilter;
        dest.ClipPath = src.ClipPath;
        dest.Mask = src.Mask; dest.MaskImage = src.MaskImage;
        dest.MaskClip = src.MaskClip; dest.MaskComposite = src.MaskComposite;
        dest.MaskMode = src.MaskMode; dest.MaskOrigin = src.MaskOrigin;
        dest.MaskPosition = src.MaskPosition; dest.MaskRepeat = src.MaskRepeat;
        dest.MaskSize = src.MaskSize;
        dest.Isolation = src.Isolation;
        dest.MixBlendMode = src.MixBlendMode;
        dest.ImageRendering = src.ImageRendering;
        dest.Contain = src.Contain;
        dest.ContentVisibility = src.ContentVisibility;
        dest.WillChange = src.WillChange;
        dest.ScrollBehavior = src.ScrollBehavior;
        dest.TabSize = src.TabSize; dest.TabSizePx = src.TabSizePx;
        dest.Hyphens = src.Hyphens;
        dest.LineBreak = src.LineBreak;
        dest.TextJustify = src.TextJustify;
        dest.HangingPunctuation = src.HangingPunctuation;
        dest.Resize = src.Resize;
        dest.Zoom = src.Zoom;
        dest.FontVariant = src.FontVariant;
        dest.FontVariantCaps = src.FontVariantCaps;
        dest.FontKerning = src.FontKerning;
        dest.FontStretch = src.FontStretch;
        dest.FontSynthesis = src.FontSynthesis;
        dest.FontOpticalSizing = src.FontOpticalSizing;
        dest.FontVariationSettings = src.FontVariationSettings;
        dest.FontFeatureSettings = src.FontFeatureSettings;
        dest.FontSizeAdjust = src.FontSizeAdjust;
        dest.AccentColor = src.AccentColor;
        dest.CaretColor = src.CaretColor;
        dest.ColorScheme = src.ColorScheme;
        dest.ForcedColorAdjust = src.ForcedColorAdjust;
        dest.Orphans = src.Orphans;
        dest.Widows = src.Widows;
        dest.RubyPosition = src.RubyPosition;
        dest.BorderImageSource = src.BorderImageSource;
        dest.BorderImageSlice = src.BorderImageSlice;
        dest.BorderImageWidth = src.BorderImageWidth;
        dest.BorderImageRepeat = src.BorderImageRepeat;
        dest.BorderImageOutset = src.BorderImageOutset;
        dest.RowGap = src.RowGap;
        dest.ColumnGap = src.ColumnGap;
        dest.ColumnCount = src.ColumnCount;
        dest.ColumnWidth = src.ColumnWidth;
        dest.GridTemplateColumns = src.GridTemplateColumns;
        dest.GridTemplateRows = src.GridTemplateRows;
        dest.GridTemplateAreas = src.GridTemplateAreas;
        dest.GridAutoColumns = src.GridAutoColumns;
        dest.GridAutoRows = src.GridAutoRows;
        dest.GridAutoFlow = src.GridAutoFlow;
        dest.GridColumnStart = src.GridColumnStart;
        dest.GridColumnEnd = src.GridColumnEnd;
        dest.GridRowStart = src.GridRowStart;
        dest.GridRowEnd = src.GridRowEnd;
        dest.GridColumn = src.GridColumn;
        dest.GridRow = src.GridRow;
        dest.GridArea = src.GridArea;
        dest.Grid = src.Grid;
    }

    // 鈹€鈹€ Parsing helpers (delegated to specialized parsers) 鈹€鈹€



    // ----- Delegating wrappers for parsers shared with CssPropertyApplier -----

    private void ParseShorthand4(string value, out Length top, out Length right, out Length bottom, out Length left)
        => CssPropertyApplier.ParseShorthand4(value, out top, out right, out bottom, out left);
    private BorderStyle ParseBorderStyleValue(string value)
        => CssPropertyApplier.ParseBorderStyleValue(value);
    private WritingModeType ParseWritingMode(string value)
        => CssPropertyApplier.ParseWritingMode(value);
    private FontWeight ParseFontWeight(string value)
        => CssPropertyApplier.ParseFontWeight(value);
    private FontStyleType ParseFontStyle(string value, ComputedStyle style)
        => CssPropertyApplier.ParseFontStyle(value, style);
    private float ParseFontSize(string value, ComputedStyle? parentStyle)
        => CssPropertyApplier.ParseFontSize(value, parentStyle);
    private string ParseFontFamily(string value)
        => CssPropertyApplier.ParseFontFamily(value);
    private float ParseLineHeight(string value, float fontSize)
        => CssPropertyApplier.ParseLineHeight(value, fontSize);
    private void ParseBackgroundPosition(string value, ComputedStyle style)
        => CssPropertyApplier.ParseBackgroundPosition(value, style);
    private void ParseBackgroundSize(string value, ComputedStyle style)
        => CssPropertyApplier.ParseBackgroundSize(value, style);
    private void ParseBackgroundShorthand(string value, ComputedStyle style)
        => CssPropertyApplier.ParseBackgroundShorthand(value, style);
    private string? ParseUrl(string value)
        => CssPropertyApplier.ParseUrl(value);
    private void ParseBorderShorthand(string value, ComputedStyle style)
        => CssPropertyApplier.ParseBorderShorthand(value, style);
    private void ParseBorderSide(ComputedStyle style, string side, string value)
        => CssPropertyApplier.ParseBorderSide(style, side, value);
    private void ParseBorderWidth(string value, ComputedStyle style)
        => CssPropertyApplier.ParseBorderWidth(value, style);
    private void ParseBorderColor(string value, ComputedStyle style)
        => CssPropertyApplier.ParseBorderColor(value, style);
    private void ParseBorderStyle(string value, ComputedStyle style)
        => CssPropertyApplier.ParseBorderStyle(value, style);
    private void ParseBorderRadius(string value, ComputedStyle style)
        => CssPropertyApplier.ParseBorderRadius(value, style);
    private float? ParseSize(string value)
        => CssPropertyApplier.ParseSize(value);
    private float? ParseRadiusValue(string value)
        => CssPropertyApplier.ParseRadiusValue(value);
    private void ParseFlexShorthand(string value, ComputedStyle style)
        => CssPropertyApplier.ParseFlexShorthand(value, style);
    private void ParseListStyle(string value, ComputedStyle style)
        => CssPropertyApplier.ParseListStyle(value, style);
    private void ParseFontShorthand(string value, ComputedStyle style)
        => CssPropertyApplier.ParseFontShorthand(value, style);
    private void ParseOutlineShorthand(string value, ComputedStyle style)
        => CssPropertyApplier.ParseOutlineShorthand(value, style);
    private void ParseShorthand2(string value, out Length a, out Length b)
        => CssPropertyApplier.ParseShorthand2(value, out a, out b);
    private Length? ParsePositionKeywordOrLength(string value)
        => CssPropertyApplier.ParsePositionKeywordOrLength(value);
    private void ParsePosition(string value, out Length? x, out Length? y)
        => CssPropertyApplier.ParsePosition(value, out x, out y);
    private void ParseInsetShorthand(string value, ComputedStyle style)
        => CssPropertyApplier.ParseInsetShorthand(value, style);
    private TextDecorationLineType ParseTextDecorationLine(string value)
        => CssPropertyApplier.ParseTextDecorationLine(value);
    private void ParseTextDecorationShorthand(string value, ComputedStyle style)
        => CssPropertyApplier.ParseTextDecorationShorthand(value, style);
    private List<TextShadowValue> ParseTextShadow(string value)
        => CssPropertyApplier.ParseTextShadow(value);
    private GridAutoFlowType ParseGridAutoFlow(string value)
        => CssPropertyApplier.ParseGridAutoFlow(value);
    private void ParseGridTemplateShorthand(string value, ComputedStyle style)
        => CssPropertyApplier.ParseGridTemplateShorthand(value, style);
    private void ParseFlexFlow(string value, ComputedStyle style)
        => CssPropertyApplier.ParseFlexFlow(value, style);
    private ContainType ParseContain(string value)
        => CssPropertyApplier.ParseContain(value);
    private float ParseZoom(string value)
        => CssPropertyApplier.ParseZoom(value);
    private void ParseGap(string value, ComputedStyle style)
        => CssPropertyApplier.ParseGap(value, style);
    private static List<BoxShadowValue>? ParseBoxShadow(string value)
        => CssPropertyApplier.ParseBoxShadow(value);
    private static IEnumerable<string> SplitCommaOutsideParens(string value)
        => CssPropertyApplier.SplitCommaOutsideParens(value);
    private static BoxShadowValue? ParseBoxShadowComponent(string component)
        => CssPropertyApplier.ParseBoxShadowComponent(component);
    private static bool TryParseLength(string token, out float value)
        => CssPropertyApplier.TryParseLength(token, out value);
    private bool EvaluateSupportsCondition(string condition)
        => CssPropertyApplier.EvaluateSupportsCondition(condition);
    private bool EvaluateSingleSupportsCondition(string condition)
        => CssPropertyApplier.EvaluateSingleSupportsCondition(condition);
    private static string GetOriginalShorthand(string longhand)
        => CssPropertyApplier.GetOriginalShorthand(longhand);
}

    // ----- Shared property parsers (implemented in CssPropertyApplier) -----

/// <summary>
/// Cascade priority encoding - similar to Blink's 96-bit priority integer.
/// Order: Importance 锟?Origin 锟?Specificity 锟?SourceOrder
/// Per CSS Cascading 4 spec:
///   Normal: Inline(5) > Author(3) > User(2) > UA(1)
///   Important: UA(5) > User(4) > Author(3) > Inline(2)
///   (JS-modified same as Inline)
/// Specificity is folded into the priority so the cascade no longer relies on the
/// (removed) parser-side specificity pre-sort.
/// </summary>
public readonly struct LegacyCascadePriority : IComparable<LegacyCascadePriority>
{
    public readonly bool Importance;
    public readonly CascadeOrigin Origin;
    public readonly int TreeOrder;
    public readonly int SpecificityA;
    public readonly int SpecificityB;
    public readonly int SpecificityC;
    public readonly int SpecificityD;
    public readonly int SourceOrder;

    public LegacyCascadePriority(bool importance, CascadeOrigin origin, int treeOrder)
        : this(importance, origin, treeOrder, 0, 0, 0, 0, 0)
    {
    }

    public LegacyCascadePriority(bool importance, CascadeOrigin origin, int treeOrder,
        int specA, int specB, int specC, int specD, int sourceOrder)
    {
        Importance = importance;
        Origin = origin;
        TreeOrder = treeOrder;
        SpecificityA = specA;
        SpecificityB = specB;
        SpecificityC = specC;
        SpecificityD = specD;
        SourceOrder = sourceOrder;
    }

    public int CompareTo(LegacyCascadePriority other)
    {
        if (Importance != other.Importance)
            return Importance.CompareTo(other.Importance);

        int thisWeight = GetOriginWeight(Origin, Importance);
        int otherWeight = GetOriginWeight(other.Origin, other.Importance);
        if (thisWeight != otherWeight)
            return thisWeight.CompareTo(otherWeight);

        int specCmp = CompareSpecificity(this, other);
        if (specCmp != 0)
            return specCmp;

        if (SourceOrder != other.SourceOrder)
            return SourceOrder.CompareTo(other.SourceOrder);

        return TreeOrder.CompareTo(other.TreeOrder);
    }

    private static int CompareSpecificity(LegacyCascadePriority a, LegacyCascadePriority b)
    {
        if (a.SpecificityA != b.SpecificityA) return a.SpecificityA.CompareTo(b.SpecificityA);
        if (a.SpecificityB != b.SpecificityB) return a.SpecificityB.CompareTo(b.SpecificityB);
        if (a.SpecificityC != b.SpecificityC) return a.SpecificityC.CompareTo(b.SpecificityC);
        return a.SpecificityD.CompareTo(b.SpecificityD);
    }

    private static int GetOriginWeight(CascadeOrigin origin, bool isImportant)
    {
        if (isImportant)
        {
            return origin switch
            {
                CascadeOrigin.JsModified => 1,
                CascadeOrigin.Inline => 2,
                CascadeOrigin.Animation => 3,
                CascadeOrigin.Author => 4,
                CascadeOrigin.User => 5,
                CascadeOrigin.UserAgent => 6,
                _ => 0
            };
        }
        else
        {
            return origin switch
            {
                CascadeOrigin.UserAgent => 1,
                CascadeOrigin.User => 2,
                CascadeOrigin.Author => 3,
                CascadeOrigin.Animation => 4,
                CascadeOrigin.Inline => 5,
                CascadeOrigin.JsModified => 6,
                _ => 0
            };
        }
    }
}

public enum CascadeOrigin { UserAgent, User, Author, Inline, Animation, JsModified }

/// <summary>
/// CascadeMap stores property declarations and keeps only the highest-priority one per property.
/// Similar to Blink's CascadeMap in style_cascade.cc.
/// </summary>
public class CascadeMap
{
    private readonly Dictionary<string, (string value, LegacyCascadePriority priority)> _map = new();

    public void Clear() => _map.Clear();

    public void Insert(string property, string value, LegacyCascadePriority priority)
    {
        if (_map.TryGetValue(property, out var existing))
        {
            if (priority.CompareTo(existing.priority) >= 0)
                _map[property] = (value, priority);
        }
        else
        {
            _map[property] = (value, priority);
        }
    }

    public bool TryGetValue(string property, out string value)
    {
        if (_map.TryGetValue(property, out var entry))
        {
            value = entry.value;
            return true;
        }
        value = string.Empty;
        return false;
    }

    public IEnumerable<KeyValuePair<string, string>> GetAll()
    {
        foreach (var kv in _map)
            yield return new KeyValuePair<string, string>(kv.Key, kv.Value.value);
    }
}

/// <summary>
/// SelectorMatcher - pre-compiles selectors for faster matching.
/// Uses the token-based CssSelectorParser and SelectorChecker.
/// </summary>
public class SelectorMatcher
{
    public bool Matches(CssRule rule, Element element)
    {
        return CssSelectorMatcher.Matches(rule.OriginalSelectorText.Length > 0
            ? rule.OriginalSelectorText
            : rule.Selector, element);
    }

    public void ClearCache() => CssSelectorMatcher.ClearCache();
}

/// <summary>
/// MatchedPropertiesCache - caches resolved styles for elements to avoid re-computation.
/// Similar to Blink's MatchedPropertiesCache.
/// </summary>
public class MatchedPropertiesCache
{
    private readonly Dictionary<CacheKey, ComputedStyle> _cache = new();
    private const int MaxSize = 1024;

    public bool TryGet(CacheKey key, out ComputedStyle? style)
    {
        return _cache.TryGetValue(key, out style);
    }

    public void Set(CacheKey key, ComputedStyle style)
    {
        if (_cache.Count >= MaxSize)
        {
            var oldest = _cache.Keys.First();
            _cache.Remove(oldest);
        }
        _cache[key] = style;
    }

    public void Clear() => _cache.Clear();
}

public readonly struct CacheKey : IEquatable<CacheKey>
{
    public readonly int ElementId;
    public readonly int StylesheetCount;
    public readonly int StyleAttrHash;
    public readonly int ClassHash;

    public CacheKey(Element element, int stylesheetCount)
    {
        ElementId = element.GetHashCode();
        StylesheetCount = stylesheetCount;
        StyleAttrHash = element.GetAttribute("style")?.GetHashCode() ?? 0;
        ClassHash = element.ClassName?.GetHashCode() ?? 0;
    }

    public bool Equals(CacheKey other) =>
        ElementId == other.ElementId &&
        StylesheetCount == other.StylesheetCount &&
        StyleAttrHash == other.StyleAttrHash &&
        ClassHash == other.ClassHash;

    public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ElementId, StylesheetCount, StyleAttrHash, ClassHash);
}

