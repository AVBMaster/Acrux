using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;
using Acrux.Core.Layout.Grid;
using SkiaSharp;
using System.Text;
using Acrux.Core.Css;

namespace Acrux.Core.Layout;

public static class LayoutMath
{
    public static float RoundToDevicePixel(float value, float dpiScale = 1.0f)
    {
        if (dpiScale <= 0) dpiScale = 1.0f;
        float physical = value * dpiScale;
        float roundedPhysical = MathF.Round(physical);
        return roundedPhysical / dpiScale;
    }

    public static SKRect RoundRect(SKRect rect, float dpiScale = 1.0f)
    {
        return new SKRect(
            RoundToDevicePixel(rect.Left, dpiScale),
            RoundToDevicePixel(rect.Top, dpiScale),
            RoundToDevicePixel(rect.Right, dpiScale),
            RoundToDevicePixel(rect.Bottom, dpiScale)
        );
    }
}

/// <summary>
/// LayoutEngine is the single layout engine of Acrux. It drives the
/// standards-conforming (CSS/W3C, aligned with modern engines) layout pipeline:
/// a tree of <see cref="BlockLayoutAlgorithm"/> / <see cref="InlineLayoutAlgorithm"/>
/// / <see cref="FlexLayoutAlgorithm"/> / grid / table algorithms built from a
/// <see cref="ConstraintSpace"/>, whose resulting fragment tree is converted
/// into the box model consumed by the painting layer.
///
/// Previously this class also hosted a second, older, hand-written box-building
/// recursion (<c>CreateLayoutBox</c>). That legacy path was non-conformant,
/// oversimplified and incomplete, so it has been removed. There is a single
/// layout path: the modern one below.
/// </summary>
public class LayoutEngine
{
    private float _viewportWidth;
    private float _viewportHeight;
    private float _contentHeight;
    private float _rootFontSize = 16;
    private float _dpiScale = 1.0f;

    public float ViewportWidth => _viewportWidth;
    public float ViewportHeight => _viewportHeight;
    public float RootFontSize => _rootFontSize;
    public float DpiScale => _dpiScale;
    public float ContentHeight => _contentHeight;

    /// <summary>
    /// Push viewport / device / root-font state onto this engine instance without
    /// running a layout pass. The unit-resolution context (vw/vh/rem/DPI) is
    /// established here; every layout entry calls this first.
    /// </summary>
    public void SyncPipelineState(float viewportWidth, float viewportHeight, float dpiScale, float rootFontSize)
    {
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0f;
        _rootFontSize = rootFontSize > 0 ? rootFontSize : 16f;
        Fonts.FontMetricsProvider.DeviceScale = _dpiScale;
    }

    /// <summary>
    /// Layout entry point. Runs the modern pipeline (<see cref="LayoutAurora"/>).
    /// </summary>
    public void Layout(Document document, float width, float height, float dpiScale = 1.0f)
    {
        LayoutAurora(document, width, height, dpiScale);
    }

    /// <summary>
    /// Runs the modern layout pipeline (BlockLayoutAlgorithm on the root) and
    /// converts the result into box-model values via
    /// <see cref="AuroraFragmentConverter"/>. This is the one and only box
    /// construction path in the engine.
    /// </summary>
    public void LayoutAurora(Document document, float width, float height, float dpiScale = 1.0f)
    {
        var sw = Acrux.Core.Performance.Clock.NowNanos();
        _viewportWidth = width;
        _viewportHeight = height;
        _dpiScale = dpiScale;
        _contentHeight = 0;

        // Font metrics resolve their line-box rounding on the device grid, so the
        // scale must be current before anything measures text.
        Fonts.FontMetricsProvider.DeviceScale = dpiScale > 0 ? dpiScale : 1.0f;

        var root = document.DocumentElement ?? document.Body;
        if (root == null)
        {
            Acrux.Core.Performance.PipelineTimings.Layout.AddSample(Acrux.Core.Performance.Clock.NowNanos() - sw);
            return;
        }

        LayoutDiagnostics.CountRootPass();

        // Save scroll state BEFORE ClearLayoutBoxes destroys the boxes.
        // Without this, every relayout resets ScrollY to 0 and inner scroll
        // containers can never hold a position.
        var savedScroll = new Dictionary<Element, Dom.LayoutBox>();
        SaveScrollContainers(root, savedScroll);

        ClearLayoutBoxes(root);

        // Generate ::before / ::after pseudo-element content for all elements
        // before layout runs, so the pseudo-elements are in the DOM tree when the
        // layout algorithm processes them.
        GeneratePseudoElementsForTree(root);

        // Unit-resolution context: rem resolves against the ROOT ELEMENT's
        // computed font-size (CSS spec), vw/vh against the viewport established
        // by SyncPipelineState/Layout entry above.
        float rootFontSize = root.ComputedStyle?.FontSize ?? _rootFontSize;
        if (rootFontSize <= 0) rootFontSize = ConstraintSpace.DefaultRootFontSize;
        var space = ConstraintSpace.Builder(width, height)
            .SetIsNewFormattingContext(true)
            .SetBfcBlockOffset(0)
            .SetForcedBfcBlockOffset(0)
            .SetRootFontSize(rootFontSize)
            .SetDpiScale(_dpiScale)
            .SetViewportSize(_viewportWidth, _viewportHeight)
            .ToConstraintSpace();
        var result = new BlockLayoutAlgorithm(root, space).Layout();
        var rootBox = AuroraFragmentConverter.ToLayoutBox(result.Fragment, root);
        if (rootBox != null)
        {
            root.LayoutBox = rootBox;
            AssignLayoutBox(root, rootBox);

            // Restore scroll state AFTER AssignLayoutBox has populated
            // element.LayoutBox for all children. Restoring before would write
            // to null references and silently lose the scroll position.
            foreach (var kv in savedScroll)
            {
                if (kv.Key.LayoutBox is { } nb)
                {
                    nb.ScrollX = kv.Value.ScrollX;
                    nb.ScrollY = kv.Value.ScrollY;
                    nb.TargetScrollX = kv.Value.TargetScrollX;
                    nb.TargetScrollY = kv.Value.TargetScrollY;
                    nb.IsSmoothScrollingX = kv.Value.IsSmoothScrollingX;
                    nb.IsSmoothScrollingY = kv.Value.IsSmoothScrollingY;
                    nb.ScrollVelX = kv.Value.ScrollVelX;
                    nb.ScrollVelY = kv.Value.ScrollVelY;
                }
            }
            savedScroll.Clear();

            CalculateContentHeight(rootBox);
        }
        Acrux.Core.Performance.PipelineTimings.Layout.AddSample(Acrux.Core.Performance.Clock.NowNanos() - sw);
    }

    /// <summary>Recursively collect LayoutBoxes of scroll containers.</summary>
    private static void SaveScrollContainers(Element element, Dictionary<Element, Dom.LayoutBox> into)
    {
        if (element.LayoutBox is { IsScrollContainer: true } b)
            into[element] = b;
        foreach (var child in element.Children)
            if (child is Element ce)
                SaveScrollContainers(ce, into);
    }

    private void GeneratePseudoElementsForTree(Element element, CounterScope? counters = null,
        int quoteDepth = 0)
    {
        counters ??= new CounterScope();
        // One scope per element: what the element and its pseudo-elements reset stays
        // inside its subtree, while an increment of an inherited name still writes to the
        // scope that owns it (CSS GCP §4.2).
        // CSS Containment 3 §2.5: 'contain: style' seals that scope. Counters created inside
        // are invisible outside and vice versa, and so is the quote state — measured on the
        // reference engine, a 'contain: style' box whose child increments an outer counter by
        // 89 renders its own '[89]' and the next sibling still reads '[98]'.
        bool seals = element.ComputedStyle?.HasStyleContainment == true;
        counters.Push(seals);
        if (seals) quoteDepth = 0;

        if (element.ComputedStyle != null)
        {
            var tagName = element.TagName ?? "";
            if (!tagName.StartsWith("pseudo-", StringComparison.OrdinalIgnoreCase))
            {
                counters.ApplyStyle(element.ComputedStyle);
                // The 'list-item' counter belongs to the item's own scope, so an item's
                // generated content reads its own ordinal and a sibling list restarts —
                // unlike an author 'counter-reset', whose scope reaches the following
                // siblings (CSS Lists 4 §4.2, see CounterScope.Reset).
                if (element.ComputedStyle.Display == DisplayType.ListItem)
                    counters.ResetInOwnScope("list-item", List.ListItemNumbering.CounterValue(element));
            }

            var dummy = new LayoutBox();
            GeneratePseudoElementContent(element, dummy, element.ComputedStyle, counters, quoteDepth);
            GenerateFloatedFirstLetter(element, element.ComputedStyle);

            if (element.BeforeStyles != null && element.BeforeStyles.TryGetValue("content", out var before))
                quoteDepth += QuoteDepthDelta(before);
            if (element.AfterStyles != null && element.AfterStyles.TryGetValue("content", out var after))
                quoteDepth += QuoteDepthDelta(after);
            if (quoteDepth < 0) quoteDepth = 0;
        }
        foreach (var child in element.Children)
        {
            if (child is Element childEl)
                GeneratePseudoElementsForTree(childEl, counters, quoteDepth);
        }

        counters.Pop();
    }

    /// <summary>
    /// A ::first-letter with 'float' becomes its own floating box (CSS Pseudo-Elements
    /// 4 §3 and CSS 2.1 §9.4.1), so the rest of the block's text wraps around the
    /// drop cap. Materialize that box as a generated child element and take the
    /// letter out of the text it belongs to.
    /// </summary>
    private void GenerateFloatedFirstLetter(Element element, ComputedStyle style)
    {
        if (element.HasGeneratedFirstLetter || element.FirstLetterStyles is not { Count: > 0 } props)
            return;
        if (!props.TryGetValue("float", out var floatValue))
            return;
        floatValue = floatValue.Trim();
        bool isLeft = floatValue.Equals("left", StringComparison.OrdinalIgnoreCase);
        bool isRight = floatValue.Equals("right", StringComparison.OrdinalIgnoreCase);
        if (!isLeft && !isRight)
            return;

        // The first letter is the first non-whitespace character of the block's own
        // text; ::before content (already generated as a child element) comes first.
        TextNode? host = null;
        int letterIndex = -1;
        foreach (var child in element.Children)
        {
            if (child is not TextNode text || string.IsNullOrEmpty(text.Data))
                continue;
            for (int i = 0; i < text.Data.Length; i++)
            {
                if (!char.IsWhiteSpace(text.Data[i]))
                {
                    host = text;
                    letterIndex = i;
                    break;
                }
            }
            if (host != null)
                break;
        }
        if (host == null || letterIndex < 0)
            return;

        string letter = host.Data[letterIndex].ToString();
        host.Data = host.Data.Remove(letterIndex, 1);

        // A floating box is blockified, and the pseudo-element declarations win over
        // the block's own inherited values. The reset has to come first: it writes the
        // non-inherited properties back to their initial values, 'float' among them.
        var letterStyle = style.Clone();
        ResetNonInheritedBoxProperties(letterStyle);
        letterStyle.Float = isLeft ? FloatType.Left : FloatType.Right;
        letterStyle.Display = DisplayType.Block;
        foreach (var kv in props)
        {
            if (kv.Key.Equals("float", StringComparison.OrdinalIgnoreCase))
                continue;
            ApplyPseudoProperty(letterStyle, kv.Key, kv.Value);
        }

        var letterElement = new HtmlElement("pseudo-first-letter")
        {
            ComputedStyle = letterStyle,
            Parent = element,
            IsGeneratedPseudoElement = true,
        };
        letterElement.Children.Add(new TextNode(letter) { Parent = letterElement });

        int insertAt = 0;
        for (int i = 0; i < element.Children.Count; i++)
        {
            if (element.Children[i] is Element generated
                && generated.TagName.Equals("pseudo-before", StringComparison.OrdinalIgnoreCase))
                insertAt = i + 1;
        }
        element.Children.Insert(insertAt, letterElement);
        element.HasGeneratedFirstLetter = true;
    }

    /// <summary>
    /// A generated pseudo-element box takes the initial value of the non-inherited
    /// box properties it does not declare itself (CSS Pseudo-Elements 4 §3.1). The
    /// style is built by cloning the originating element, so without this reset a
    /// ::before or a floated ::first-letter would inherit the block's own width and
    /// stop shrinking to fit its content.
    /// </summary>
    private static void ResetNonInheritedBoxProperties(ComputedStyle style)
    {
        // CSS Pseudo-Elements 4 §3.1: a generated box takes the *initial* value of every
        // non-inherited property its own rule does not declare. Measured against the reference
        // engine, a ::before inside 'margin:20px; border:5px solid; background:red;
        // position:relative; top:3px; z-index:5; opacity:.5' computes every one of those to
        // its initial value — but the style is built by cloning the originating element, so
        // each has to be written back by hand or it leaks into the generated box.
        //
        // 'auto' is the initial value of width/height, and the rest of the engine tests for
        // AutoLength (not for null) to decide whether a size is definite — writing null made an
        // auto-sized generated box look definite, so it stretched to its containing block
        // instead of shrinking to fit.
        style.Width = AutoLength.Instance;
        style.Height = AutoLength.Instance;
        style.MinWidth = null;
        style.MaxWidth = null;
        style.MinHeight = null;
        style.MaxHeight = null;

        style.MarginTop = style.MarginRight = style.MarginBottom = style.MarginLeft = new PixelLength(0);
        style.PaddingTop = style.PaddingRight = style.PaddingBottom = style.PaddingLeft = new PixelLength(0);

        style.BorderTopWidth = style.BorderRightWidth = style.BorderBottomWidth = style.BorderLeftWidth = 0;
        style.BorderTopStyle = style.BorderRightStyle = style.BorderBottomStyle = style.BorderLeftStyle = BorderStyle.None;

        style.Position = PositionType.Static;
        style.Top = style.Right = style.Bottom = style.Left = AutoLength.Instance;
        style.Float = FloatType.None;
        style.Clear = ClearType.None;
        // The flow-root marker belongs to the same property as 'display' and has to be
        // reset with it, or a generated box inside a flow-root parent keeps the parent's
        // formatting context.
        style.DisplayIsFlowRoot = false;
        style.ZIndex = null;
        style.Opacity = 1f;
        style.Overflow = style.OverflowX = style.OverflowY = OverflowType.Visible;
        style.BackgroundColor = null;
        style.BackgroundImage = null;
        style.OutlineWidth = 0;
        style.OutlineStyle = BorderStyle.None;
        style.Transform = null;
        style.BoxShadow = null;
        style.TextShadow = new();
        style.Contain = ContainType.None;
    }

    /// <summary>Drops the boxes of the previous pass. The converter rebuilds the whole box
    /// tree every layout, so an element that keeps a stale box would be painted at its old
    /// geometry until the new mapping reaches it.</summary>
    private static void ClearLayoutBoxes(Element element)
    {
        element.LayoutBox = null;
        foreach (var child in element.Children)
            if (child is Element childElement)
                ClearLayoutBoxes(childElement);
    }

    /// <summary>Hands every element the box the converter built for it, so paint,
    /// hit-testing and scrolling can go from an element to its geometry.</summary>
    private static void AssignLayoutBox(Element element, Dom.LayoutBox box)
    {
        foreach (var childBox in box.Children)
        {
            if (childBox.Dimensions?.Element is not { } childElement ||
                ReferenceEquals(childElement, element))
                continue;
            childElement.LayoutBox = childBox;
            AssignLayoutBox(childElement, childBox);
        }
    }

    private void CalculateContentHeight(Dom.LayoutBox box)
    {
        foreach (var child in box.Children)
            CalculateContentHeight(child);
        if (box.MarginBox.Bottom > _contentHeight)
            _contentHeight = box.MarginBox.Bottom;
    }

    private void GeneratePseudoElementContent(Element element, LayoutBox box, ComputedStyle style,
        CounterScope counters, int quoteDepth)
    {
        // Counter properties declared on a pseudo-element apply to (and are
        // visible in) that pseudo-element's own content (CSS 2.1 §10.4).
        if (element.BeforeStyles != null)
            ApplyCounterDeclarations(element.BeforeStyles, counters);

        if (element.BeforeStyles != null && element.BeforeStyles.TryGetValue("content", out var beforeContent) && !element.HasGeneratedBefore)
        {
            var result = BuildPseudoElement(element, style, beforeContent, isBefore: true, counters, quoteDepth);
            if (result is Element el)
            {
                element.Children.Insert(0, el);
                element.HasGeneratedBefore = true;
            }
        }

        if (element.AfterStyles != null)
            ApplyCounterDeclarations(element.AfterStyles, counters);

        if (element.AfterStyles != null && element.AfterStyles.TryGetValue("content", out var afterContent) && !element.HasGeneratedAfter)
        {
            var result = BuildPseudoElement(element, style, afterContent, isBefore: false, counters, quoteDepth);
            if (result is Element el)
            {
                element.Children.Add(el);
                element.HasGeneratedAfter = true;
            }
        }
    }

    /// <summary>Quote nesting change contributed by one generated-content value:
    /// an element that opens a quote places its descendants one level deeper
    /// (CSS GCP §4.1); its own close-quote belongs to the current level.</summary>
    private static int QuoteDepthDelta(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;
        return content.Contains("open-quote", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static void ApplyCounterDeclarations(Dictionary<string, string> declarations, CounterScope counters)
    {
        if (declarations.TryGetValue("counter-reset", out var reset))
            counters.ApplyReset(reset);
        if (declarations.TryGetValue("counter-increment", out var inc))
            counters.ApplyIncrement(inc);
        if (declarations.TryGetValue("counter-set", out var set))
            counters.ApplySet(set);
    }

    private Node? BuildPseudoElement(Element parent, ComputedStyle parentStyle, string rawContent, bool isBefore,
        CounterScope counters, int quoteDepth)
    {
        var props = isBefore ? parent.BeforeStyles : parent.AfterStyles;
        if (props == null) return null;
        // A value the grammar rejects leaves 'content' at its initial 'normal', which for
        // a pseudo-element generates no box at all.
        if (!CounterStyleArgsValid(rawContent)) return null;

        var content = DecodeCssContent(rawContent, counters, parent, quoteDepth);
        if (content == "none" || content == null) return null;

        // Build a ComputedStyle by cloning the parent and applying ::before/::after props.
        var pseudoStyle = parentStyle.Clone();

        // The parent's used box properties are not inherited by the generated box.
        ResetNonInheritedBoxProperties(pseudoStyle);

        // Apply display (the initial value for ::before/::after is 'inline'). A value the
        // grammar rejects is dropped, which leaves that initial 'inline' — the shared
        // 'display' parser is the only keyword list here, as for float and clear.
        string displayStr = props.TryGetValue("display", out var d) ? d : "inline";
        if (Acrux.Core.Css.Resolver.CssPropertyApplier.TryParseDisplay(displayStr, out var displayType, out var displayFlowRoot))
        {
            pseudoStyle.Display = displayType;
            pseudoStyle.DisplayIsFlowRoot = displayFlowRoot;
        }

        // Apply the remaining pseudo-element properties.
        foreach (var kv in props)
        {
            if (kv.Key == "content" || kv.Key == "display") continue;
            ApplyPseudoProperty(pseudoStyle, kv.Key, kv.Value);
        }

        // The generated box never goes through the cascade, so the adjustments it would
        // have received have to be applied here: 'position: absolute' on a ::before blockifies
        // its initial 'inline' display (CSS Display 3 §3.2), and without that the box has
        // geometry but is painted as if it were still inline content.
        Acrux.Core.Css.Cascade.StyleAdjuster.BlockifyFloatAndAbsolute(pseudoStyle);

        // Create the Element. Even for inline content we need a real Element
        // so that the pseudo-element's own styles (color, font-weight, etc.) are
        // applied — a bare TextNode would inherit the parent's style and ignore
        // the ::before/::after declarations.
        // A lone url() makes the generated box a replaced element (CSS GCP §4.2),
        // which reuses the image layout/paint pipeline and its intrinsic size.
        bool isImageContent = TryExtractSingleUrl(content, out string imageUrl);
        var pseudoEl = new HtmlElement(isImageContent ? "img" : "pseudo-" + (isBefore ? "before" : "after"))
        {
            ComputedStyle = pseudoStyle,
            Parent = parent,
            // The generated box takes pointer events for its originating element, never
            // for itself — see Element.IsGeneratedPseudoElement.
            IsGeneratedPseudoElement = true,
        };
        if (isImageContent)
            pseudoEl.SetAttribute("src", imageUrl);

        // Add the text content as a child text node.
        if (!isImageContent && !string.IsNullOrEmpty(content))
        {
            var textNode = new TextNode(content);
            textNode.Parent = pseudoEl;
            pseudoEl.Children.Add(textNode);
        }

        return pseudoEl;
    }

    /// <summary>True when the whole content value is a single url() token.</summary>
    private static bool TryExtractSingleUrl(string? content, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(content))
            return false;
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return false;
        int close = trimmed.IndexOf(')');
        if (close < 0 || trimmed[(close + 1)..].Trim().Length > 0)
            return false;
        url = trimmed[(trimmed.IndexOf('(') + 1)..close].Trim().Trim('"', '\'');
        return url.Length > 0;
    }

    private static void ApplyPseudoProperty(ComputedStyle style, string name, string value)
    {
        var lower = name.ToLowerInvariant();
        try
        {
            switch (lower)
            {
                case "width": style.Width = Length.Parse(value); break;
                case "height": style.Height = Length.Parse(value); break;
                case "min-width": style.MinWidth = Length.Parse(value); break;
                case "min-height": style.MinHeight = Length.Parse(value); break;
                case "max-width": style.MaxWidth = Length.Parse(value); break;
                case "max-height": style.MaxHeight = Length.Parse(value); break;
                case "margin": ParseShorthand4(value, out var mt, out var mr, out var mb, out var ml);
                    style.MarginTop = mt; style.MarginRight = mr; style.MarginBottom = mb; style.MarginLeft = ml; break;
                case "margin-top": style.MarginTop = Length.Parse(value); break;
                case "margin-right": style.MarginRight = Length.Parse(value); break;
                case "margin-bottom": style.MarginBottom = Length.Parse(value); break;
                case "margin-left": style.MarginLeft = Length.Parse(value); break;
                case "padding": ParseShorthand4(value, out var pt, out var pr, out var pb, out var pl);
                    style.PaddingTop = pt; style.PaddingRight = pr; style.PaddingBottom = pb; style.PaddingLeft = pl; break;
                case "padding-top": style.PaddingTop = Length.Parse(value); break;
                case "padding-right": style.PaddingRight = Length.Parse(value); break;
                case "padding-bottom": style.PaddingBottom = Length.Parse(value); break;
                case "padding-left": style.PaddingLeft = Length.Parse(value); break;
                case "color": style.Color = ColorParser.Parse(value); break;
                case "background-color": style.BackgroundColor = ColorParser.Parse(value); break;
                case "background-image": style.BackgroundImage = new List<string> { value }; break;
                case "position": style.Position = ParsePseudoPosition(value); break;
                case "top": style.Top = Length.Parse(value); break;
                case "right": style.Right = Length.Parse(value); break;
                case "bottom": style.Bottom = Length.Parse(value); break;
                case "left": style.Left = Length.Parse(value); break;
                case "float": style.Float = ParsePseudoFloat(value); break;
                case "clear": style.Clear = ParsePseudoClear(value); break;
                case "z-index": style.ZIndex = int.TryParse(value, out var zi) ? zi : 0; break;
                case "opacity": style.Opacity = float.TryParse(value, out var op) ? op : 1; break;
                case "overflow": style.Overflow = ParsePseudoOverflow(value); break;
                case "text-align": style.TextAlign = ParsePseudoTextAlign(value); break;
                case "font-size": Css.Resolver.CssPropertyApplier.SetFontSize(style, value, ParsePseudoFontSize(value)); break;
                case "line-height": Acrux.Core.Fonts.LineBoxMetrics.ApplyLineHeight(style, value); break;
                case "font-family": style.FontFamily = value; break;
                case "font-weight": style.FontWeight = (FontWeight)(int.TryParse(value, out var fw) ? fw : 400); break;
                case "border": ParsePseudoBorder(style, value); break;
                case "border-radius": ParsePseudoBorderRadius(style, value); break;
                case "box-shadow": ParsePseudoBoxShadow(style, value); break;
                case "background": style.BackgroundColor = ColorParser.Parse(value); break;
            }
        }
        catch { /* ignore invalid property values */ }
    }

    private static void ParseShorthand4(string value, out Length? v1, out Length? v2, out Length? v3, out Length? v4)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var p = parts.Select(Length.Parse).ToList();
        v1 = p.Count > 0 ? p[0] : null;
        v2 = p.Count > 1 ? p[1] : v1;
        v3 = p.Count > 2 ? p[2] : v1;
        v4 = p.Count > 3 ? p[3] : (p.Count > 1 ? p[1] : v1);
    }

    private static PositionType ParsePseudoPosition(string v) => v.ToLowerInvariant() switch
    {
        "absolute" => PositionType.Absolute, "fixed" => PositionType.Fixed,
        "relative" => PositionType.Relative, "sticky" => PositionType.Sticky,
        _ => PositionType.Static
    };
    private static FloatType ParsePseudoFloat(string v) =>
        CssFloatKeywords.TryParseFloat(v, out var f) ? f : FloatType.None;
    private static ClearType ParsePseudoClear(string v) =>
        CssFloatKeywords.TryParseClear(v, out var c) ? c : ClearType.None;
    private static OverflowType ParsePseudoOverflow(string v) => v.ToLowerInvariant() switch
    {
        "hidden" => OverflowType.Hidden, "scroll" => OverflowType.Scroll, "auto" => OverflowType.Auto, _ => OverflowType.Visible
    };
    private static TextAlignType ParsePseudoTextAlign(string v) => v.ToLowerInvariant() switch
    {
        "left" => TextAlignType.Left, "right" => TextAlignType.Right, "center" => TextAlignType.Center, "justify" => TextAlignType.Justify, _ => TextAlignType.Start
    };
    private static float ParsePseudoFontSize(string v)
    {
        if (v.EndsWith("px") && float.TryParse(v[..^2], out var px)) return px;
        if (v.EndsWith("em") && float.TryParse(v[..^2], out var em)) return em * 16;
        if (v.EndsWith("rem") && float.TryParse(v[..^2], out var rem)) return rem * 16;
        if (float.TryParse(v, out var f)) return f;
        return 16;
    }

    private static void ParsePseudoBorder(ComputedStyle style, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.EndsWith("px") && float.TryParse(p[..^2], out var w))
            { style.BorderTopWidth = style.BorderRightWidth = style.BorderBottomWidth = style.BorderLeftWidth = w; }
            else if (p is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge" or "inset" or "outset")
            { var bs = p switch { "solid" => BorderStyle.Solid, "dashed" => BorderStyle.Dashed, "dotted" => BorderStyle.Dotted, "double" => BorderStyle.Double, "groove" => BorderStyle.Groove, "ridge" => BorderStyle.Ridge, "inset" => BorderStyle.Inset, "outset" => BorderStyle.Outset, _ => BorderStyle.Solid };
                style.BorderTopStyle = style.BorderRightStyle = style.BorderBottomStyle = style.BorderLeftStyle = bs; }
            else
            { var c = ColorParser.Parse(p); style.BorderTopColor = style.BorderRightColor = style.BorderBottomColor = style.BorderLeftColor = c; }
        }
    }

    private static void ParsePseudoBorderRadius(ComputedStyle style, string value)
    {
        var radii = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var r = radii.Select(v =>
        {
            if (v.EndsWith("px") && float.TryParse(v[..^2], out var px)) return px;
            if (float.TryParse(v, out var f)) return f;
            return 0f;
        }).ToList();
        style.BorderTopLeftRadius = r.Count > 0 ? r[0] : 0;
        style.BorderTopRightRadius = r.Count > 1 ? r[1] : r[0];
        style.BorderBottomRightRadius = r.Count > 2 ? r[2] : r[0];
        style.BorderBottomLeftRadius = r.Count > 3 ? r[3] : (r.Count > 0 ? r[0] : 0);
    }

    private static void ParsePseudoBoxShadow(ComputedStyle style, string value)
    {
        // Simplified: single shadow only.
        if (value == "none") return;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool inset = false;
        int idx = 0;
        if (parts[0] == "inset") { inset = true; idx++; }
        if (idx + 1 >= parts.Length) return;
        float.TryParse(parts[idx].TrimEnd('p', 'x'), out var ox);
        float.TryParse(parts[idx + 1].TrimEnd('p', 'x'), out var oy);
        idx += 2;
        float br = 0, sp = 0;
        if (idx < parts.Length && parts[idx].Contains('x')) { float.TryParse(parts[idx].TrimEnd('p', 'x'), out br); idx++; }
        if (idx < parts.Length && parts[idx].Contains('x')) { float.TryParse(parts[idx].TrimEnd('p', 'x'), out sp); idx++; }
        var color = idx < parts.Length ? ColorParser.Parse(string.Join(" ", parts.Skip(idx))) : new SKColor(0, 0, 0, 80);
        style.BoxShadow = new List<BoxShadowValue> { new BoxShadowValue(color, ox, oy, br, sp, inset) };
    }

    private string DecodeCssContent(string content, CounterScope? counters = null, Element? owner = null,
        int quoteDepth = 0)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
        {
            return trimmed;
        }

        var sb = new StringBuilder();
        int i = 0;
        while (i < trimmed.Length)
        {
            if (trimmed[i] == '"')
            {
                i++;
                while (i < trimmed.Length && trimmed[i] != '"')
                {
                    if (trimmed[i] == '\\' && i + 1 < trimmed.Length)
                    {
                        i++;
                        sb.Append(trimmed[i]);
                    }
                    else
                    {
                        sb.Append(trimmed[i]);
                    }
                    i++;
                }
                if (i < trimmed.Length) i++;
            }
            else if (trimmed[i] == '\'')
            {
                i++;
                while (i < trimmed.Length && trimmed[i] != '\'')
                {
                    if (trimmed[i] == '\\' && i + 1 < trimmed.Length)
                    {
                        i++;
                        sb.Append(trimmed[i]);
                    }
                    else
                    {
                        sb.Append(trimmed[i]);
                    }
                    i++;
                }
                if (i < trimmed.Length) i++;
            }
            else if (i + 4 <= trimmed.Length && trimmed.Substring(i, 4).Equals("attr", StringComparison.OrdinalIgnoreCase)
                     && i + 4 < trimmed.Length && trimmed[i + 4] == '(')
            {
                // attr(name) resolves against the originating element; a missing
                // attribute contributes an empty string (CSS Values 4 §11.1) unless the
                // two-argument form supplies a fallback: attr(name "FB").
                int parenStart = i + 4;
                int parenEnd = FindMatchingParen(trimmed, parenStart);
                if (parenEnd > parenStart)
                {
                    var argument = trimmed.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim();
                    int space = argument.IndexOfAny(new[] { ' ', '\t' });
                    var attrName = space > 0 ? argument[..space] : argument;
                    var fallback = space > 0 ? UnquoteAttrFallback(argument[(space + 1)..]) : null;
                    if (owner != null && owner.HasAttribute(attrName))
                        sb.Append(owner.GetAttribute(attrName));
                    else if (fallback != null)
                        sb.Append(fallback);
                    i = parenEnd + 1;
                }
                else
                {
                    sb.Append(trimmed[i]);
                    i++;
                }
            }
            else if (IsCounterCall(trimmed, i, "counters"))
            {
                int parenStart = i + 8;
                int parenEnd = FindMatchingParen(trimmed, parenStart);
                if (parenEnd > parenStart)
                {
                    var args = trimmed.Substring(parenStart + 1, parenEnd - parenStart - 1);
                    var argParts = SplitCounterArgs(args);
                    var counterName = argParts.Count > 0 ? argParts[0].Trim() : "";
                    var styleName = argParts.Count > 2 ? argParts[2].Trim() : "decimal";
                    var separator = argParts.Count > 1 ? UnquoteCounterArg(argParts[1]) : ".";
                    // One value per open scope of the name, outermost first, joined by the
                    // separator (CSS Lists 4 §4.2).
                    var values = counters?.Values(counterName) ?? new List<int>();
                    if (values.Count == 0) values.Add(0);
                    sb.Append(string.Join(separator, values.Select(
                        v => List.ListMarkerFormatter.CounterRepresentation(
                            styleName, v, owner?.OwnerDocument))));
                    i = parenEnd + 1;
                }
                else
                {
                    sb.Append(trimmed[i]);
                    i++;
                }
            }
            else if (IsCounterCall(trimmed, i, "counter"))
            {
                int parenStart = i + 7;
                int parenEnd = FindMatchingParen(trimmed, parenStart);
                if (parenEnd > parenStart)
                {
                    var args = trimmed.Substring(parenStart + 1, parenEnd - parenStart - 1);
                    var argParts = SplitCounterArgs(args);
                    var counterName = argParts.Count > 0 ? argParts[0].Trim() : "";
                    var styleName = argParts.Count > 1 ? argParts[1].Trim() : "decimal";
                    int val = counters?.Value(counterName) ?? 0;
                    sb.Append(List.ListMarkerFormatter.CounterRepresentation(
                        styleName, val, owner?.OwnerDocument));
                    i = parenEnd + 1;
                }
                else
                {
                    sb.Append(trimmed[i]);
                    i++;
                }
            }
            else if (TryQuoteKeyword(trimmed, i, out QuoteType quoteType, out int quoteEnd))
            {
                // An element's own open and close marks use the pair at its
                // nesting level; its descendants are one level deeper (CSS GCP §4.1).
                string? quotesData = owner?.ComputedStyle?.Quotes;
                sb.Append(LayoutQuote.ResolveQuote(quoteType, quoteDepth, quotesData));
                i = quoteEnd;
            }
            else
            {
                sb.Append(trimmed[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>Match one of the four quote keywords at |index|, honouring the
    /// longest token first and refusing to split a longer identifier.</summary>
    private static bool TryQuoteKeyword(string s, int index, out QuoteType type, out int end)
    {
        type = QuoteType.OpenQuote;
        end = index;
        if (index > 0 && (char.IsLetterOrDigit(s[index - 1]) || s[index - 1] == '-' || s[index - 1] == '_'))
            return false;

        ReadOnlySpan<char> rest = s.AsSpan(index);
        string? matched = null;
        foreach (var (keyword, quoteType) in QuoteKeywords)
        {
            if (!rest.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                continue;
            int after = index + keyword.Length;
            if (after < s.Length && (char.IsLetterOrDigit(s[after]) || s[after] == '-' || s[after] == '_'))
                continue;
            matched = keyword;
            type = quoteType;
            end = after;
            break;
        }
        return matched != null;
    }

    private static readonly (string Keyword, QuoteType Type)[] QuoteKeywords =
    {
        ("no-open-quote", QuoteType.NoOpenQuote),
        ("no-close-quote", QuoteType.NoCloseQuote),
        ("open-quote", QuoteType.OpenQuote),
        ("close-quote", QuoteType.CloseQuote),
    };

    /// <summary>The style argument of counter() and counters() is a counter-style name, not
    /// a string. A quoted one is a parse error, and the whole 'content' value is dropped
    /// with it rather than just that function (measured against Edge: the pseudo-element
    /// then generates nothing).</summary>
    private static bool CounterStyleArgsValid(string content)
    {
        if (string.IsNullOrEmpty(content)) return true;

        int i = 0;
        while (i < content.Length)
        {
            char c = content[i];
            if (c is '"' or '\'')
            {
                i = SkipQuotedLiteral(content, i);
                continue;
            }

            bool isCounters = IsCounterCall(content, i, "counters");
            int keywordLength = isCounters ? "counters".Length : "counter".Length;
            if (!isCounters && !IsCounterCall(content, i, "counter"))
            {
                i++;
                continue;
            }

            int open = i + keywordLength;
            int close = FindMatchingParen(content, open);
            if (close < 0) return false;
            var args = SplitCounterArgs(content[(open + 1)..close]);
            // counters() carries the style third, counter() second.
            int styleIndex = isCounters ? 2 : 1;
            if (args.Count > styleIndex && args[styleIndex].Trim().Length > 0
                && args[styleIndex].Trim()[0] is '"' or '\'')
                return false;
            i = close + 1;
        }
        return true;
    }

    /// <summary>Index just past the string literal that starts at |index|.</summary>
    private static int SkipQuotedLiteral(string text, int index)
    {
        char quote = text[index];
        for (int i = index + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == quote) return i + 1;
        }
        return text.Length;
    }

    /// <summary>Match a counter() or counters() call at |index|: the keyword, an opening
    /// paren right after it, and no identifier character before it, so 'mycounter(' is not
    /// read as a call.</summary>
    private static bool IsCounterCall(string s, int index, string keyword)
    {
        if (index > 0 && (char.IsLetterOrDigit(s[index - 1]) || s[index - 1] is '-' or '_'))
            return false;
        if (index + keyword.Length > s.Length) return false;
        if (!s.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
            return false;
        return index + keyword.Length < s.Length && s[index + keyword.Length] == '(';
    }

    private static int FindMatchingParen(string s, int openPos)    {
        int depth = 0;
        for (int i = openPos; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    /// <summary>A counter() separator is a &lt;string&gt;; an unquoted argument is not a
    /// valid value and contributes nothing.</summary>
    /// <summary>The fallback argument of <c>attr(name "FB")</c>: only a quoted string is
    /// a valid fallback here, so an unquoted remainder yields no fallback at all.</summary>
    private static string? UnquoteAttrFallback(string text)
    {
        text = text.Trim();
        if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
            return Css.Resolver.CssPropertyApplier.UnescapeCssString(text[1..^1]);
        return null;
    }

    private static string UnquoteCounterArg(string text)
    {
        text = text.Trim();
        if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
            return Css.Resolver.CssPropertyApplier.UnescapeCssString(text[1..^1]);
        return string.Empty;
    }

    private static List<string> SplitCounterArgs(string args)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        char quoteChar = '"';
        for (int i = 0; i < args.Length; i++)
        {
            if (inQuote)
            {
                if (args[i] == quoteChar) inQuote = false;
                sb.Append(args[i]);
            }
            else if (args[i] == '"' || args[i] == '\'')
            {
                inQuote = true;
                quoteChar = args[i];
                sb.Append(args[i]);
            }
            else if (args[i] == ',')
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(args[i]);
            }
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
