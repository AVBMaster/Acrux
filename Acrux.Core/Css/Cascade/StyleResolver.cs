using Acrux.Core.Css.ElementStyles;
using Acrux.Core.Css.Matcher;
using Acrux.Core.Css.Properties;
using Acrux.Core.Css.Resolver;
using Acrux.Core.Css.Rules;
using Acrux.Core.Css.Tokenizer;
using Acrux.Core.Css.Values;
using Acrux.Core.Dom;

namespace Acrux.Core.Css.Cascade;

/// <summary>
/// Resolves ComputedStyle for elements based on the document's stylesheets.
/// Mirrors Blink's StyleResolver: collects matching rules, applies the cascade,
/// then adjusts the resulting style.
/// </summary>
public class StyleResolver
{
    private readonly List<StyleSheetContents> _authorSheets = new();
    private readonly StyleSheetContents? _uaSheet;
    private readonly SelectorChecker _checker = new();
    private readonly StyleAdjuster _adjuster = new();
    private int _treeOrderCounter;
    private float _viewportWidth = 1024;
    private float _viewportHeight = 768;
    private string _colorScheme = "light";
    /// <summary>The one environment every '@media' condition in this resolve is asked against.
    /// It is rebuilt by <see cref="SetViewport"/> rather than read per rule, so a rule and
    /// 'window.matchMedia' cannot drift apart mid-document, and so that the device scale the page
    /// is being rasterised at reaches 'resolution' (CSS Media Queries 4 §7.9).</summary>
    private MediaQueryEnvironment _mediaEnv = MediaQueryEnvironment.Default(1024, 768, "light");

    public StyleResolver(StyleSheetContents? authorSheet = null, StyleSheetContents? uaSheet = null)
    {
        if (authorSheet != null) _authorSheets.Add(authorSheet);
        _uaSheet = uaSheet;
    }

    /// <summary>Adds an author stylesheet (parsed via the Prism token pipeline).</summary>
    public void AddStyleSheet(StyleSheetContents sheet)
    {
        if (sheet != null) _authorSheets.Add(sheet);
    }

    public void AddStyleSheets(IEnumerable<StyleSheetContents> sheets)
    {
        foreach (var s in sheets)
            if (s != null) _authorSheets.Add(s);
    }

    public void SetViewport(float width, float height, string colorScheme = "light", float resolutionDppx = 1f)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        _colorScheme = colorScheme;
        _mediaEnv = MediaQueryEnvironment.Default(width, height, colorScheme, resolutionDppx);
    }

    /// <summary>
    /// Resolve the computed style for a single element, given its parent's computed style.
    /// </summary>
    public ComputedStyle? ResolveStyle(Element element, ComputedStyle? parentStyle)
    {
        var style = new ComputedStyle();
        // The viewport belongs to the style, not to the property: a computed value that has to
        // print a 'vh' as pixels needs the number the cascade was given, and it is the same
        // number the layout will use.
        style.ComputedViewportWidth = _viewportWidth;
        style.ComputedViewportHeight = _viewportHeight;
        if (parentStyle != null)
            InheritProperties(style, parentStyle);

        var state = new CascadeResolverState
        {
            Element = element,
            ParentStyle = parentStyle,
            ViewportWidth = _viewportWidth,
            ViewportHeight = _viewportHeight,
            RootFontSize = parentStyle?.FontSize ?? 16,
            ColorScheme = _colorScheme,
        };
        element.ComputedStyle = style;

        var cascade = new StyleCascade(state);

        // 1. UA styles (parsed UA stylesheet at UserAgent origin, then registry fallback)
        MatchUAStyles(element, state);

        // 2. Presentational hints (HTML width/height/border/cellspacing attributes)
        MatchPresentationalHints(element, state);

        // 3. Author styles
        MatchAuthorStyles(element, state);

        // 4. Inline style
        MatchInlineStyle(element, state);

        // 5. JS-modified styles (element.style assignments) - highest origin
        MatchJsModifiedStyle(element, state);

        // 6. Apply the cascade
        cascade.Apply();

        // 7. Adjust the computed style
        // @keyframes are NOT folded in here: the cascade must yield the
        // element's *underlying* value so the animation engine has a clean base to
        // sample over, and so a transition's after-change style is the declared
        // value rather than an animated one.
        _adjuster.AdjustComputedStyle(style, element, parentStyle);

        return style;
    }

    public void ResolveDocument(Document document)
    {
        _treeOrderCounter = 0;
        var root = document.DocumentElement ?? document.Body;
        if (root == null) return;

        // The root element inherits from a base User-Agent style: the UA
        // defaults registry (sans-serif, line-height:normal, zero margins...)
        // applied on top of a fresh ComputedStyle. This mirrors the UA sheet's
        // :root defaults without needing an html rule to match.
        var rootStyle = new ComputedStyle();
        ElementStyleRegistry.ApplyUserAgentStyle(rootStyle, root.TagName, root);
        rootStyle.FontSize = 16;

        ResolveTree(root, rootStyle);
    }

    private void ResolveTree(Element element, ComputedStyle? parent)
    {
        // A generated box (::before / ::after / a floated ::first-letter) has no selector that
        // can match it: its style was built when the box was created, from the originating
        // element's clone plus the pseudo-element's own declarations. Re-resolving it here would
        // find no rule at all and quietly replace that style with plain inheritance, which
        // drops every declaration except the inherited ones (font-size, position, background).
        var style = element.IsGeneratedPseudoElement && element.ComputedStyle != null
            ? element.ComputedStyle
            : ResolveStyle(element, parent);
        foreach (var child in element.Children)
        {
            if (child is Element childEl)
                ResolveTree(childEl, style);
        }
    }

    private void MatchUAStyles(Element element, CascadeResolverState state)
    {
        // Parsed UA stylesheet (ua-stylesheet.css) at UserAgent origin.
        if (_uaSheet != null)
        {
            int uaTreeOrder = _treeOrderCounter++;
            int uaPosition = 0;
            foreach (var rule in _uaSheet.ChildRules)
            {
                if (rule is StyleRule styleRule)
                    MatchStyleRule(styleRule, element, state, CascadeOrigin.UserAgent, 0, uaTreeOrder, ref uaPosition);
                else if (rule is StyleRuleMedia media)
                {
                    if (MediaQueryEvaluator.Evaluate(media.ConditionText, _viewportWidth, _viewportHeight, _colorScheme, _mediaEnv))
                        MatchGroupRules(media.ChildRules, element, state, CascadeOrigin.UserAgent, 0, uaTreeOrder, ref uaPosition);
                }
                else if (rule is StyleRuleSupports supports)
                {
                    if (CssPropertyApplier.EvaluateSupportsCondition(supports.ConditionText))
                        MatchGroupRules(supports.ChildRules, element, state, CascadeOrigin.UserAgent, 0, uaTreeOrder, ref uaPosition);
                }
                else if (rule is StyleRuleLayerBlock layer)
                    MatchGroupRules(layer.ChildRules, element, state, CascadeOrigin.UserAgent, 0, uaTreeOrder, ref uaPosition);
            }
        }

        // Registry fallback for elements not covered by the UA stylesheet.
        var uaStyle = new ComputedStyle();
        ElementStyleRegistry.ApplyUserAgentStyle(uaStyle, element.TagName, element);

        var props = new CssPropertyValueSet(CssParserMode.UACSS);
        CollectNonDefaultProperties(uaStyle, props);

        // 'display' is emitted even when the registry agrees with the ComputedStyle
        // default, because that default is not a declaration: a div's block-ness has to
        // be something 'display: revert' can land on, and the CSS initial value ('inline')
        // is not it (CSS Cascading 4 6.4.2, measured: revert on a div computes to 'block').
        if (!props.HasProperty(CssPropertyId.Display))
            props.SetProperty(CssPropertyId.Display, CssIdentifierValue.Create(DisplayToId(uaStyle.Display)));

        if (props.PropertyCount > 0)
        {
            state.MatchedRules.Add(new MatchedRuleEntry
            {
                Properties = ExpandShorthands(props),
                Priority = new CascadePriority(CascadeOrigin.UserAgent, 0, _treeOrderCounter++, 0, false)
            });
        }
    }

    private void MatchPresentationalHints(Element element, CascadeResolverState state)
    {
        // Same priority treatment as legacy AnalyzePresentationalHints: UA origin.
        var priority = new CascadePriority(CascadeOrigin.UserAgent, 0, _treeOrderCounter++, 0, false);
        string tag = element.TagName.ToUpperInvariant();
        var props = new CssPropertyValueSet();

        if (tag is "VIDEO" or "IFRAME" or "EMBED" or "OBJECT" or "IMG")
        {
            if (uint.TryParse(element.GetAttribute("width"), out uint w) && w > 0)
                SetPx(props, "width", w);
            if (uint.TryParse(element.GetAttribute("height"), out uint h) && h > 0)
                SetPx(props, "height", h);
        }

        // <ol type="1|A|a|I|i"> is a presentational hint that maps to
        // list-style-type. The UA stylesheet carries 'ol{list-style-type:decimal}'
        // at element specificity (0,0,1), so this hint is given (0,1,0) to beat it
        // while staying in the UA origin — any author rule (higher origin) still
        // overrides it, matching how browsers treat the type attribute.
        if (tag == "OL")
        {
            var listType = element.GetAttribute("type") switch
            {
                "A" => CssValueId.UpperAlpha,
                "a" => CssValueId.LowerAlpha,
                "I" => CssValueId.UpperRoman,
                "i" => CssValueId.LowerRoman,
                "1" => CssValueId.Decimal,
                _ => CssValueId.Invalid,
            };
            if (listType != CssValueId.Invalid)
            {
                var typeProps = new CssPropertyValueSet();
                typeProps.SetProperty(CssPropertyId.ListStyleType, CssIdentifierValue.Create(listType));
                state.MatchedRules.Add(new MatchedRuleEntry
                {
                    Properties = typeProps,
                    Priority = new CascadePriority(CascadeOrigin.UserAgent, 0, _treeOrderCounter++, 0, false, 0, 0, 1, 0)
                });
            }
        }

        if (tag == "TABLE")
        {
            if (int.TryParse(element.GetAttribute("border"), out int b) && b > 0)
                ApplyBorderHints(props, b);
            if (int.TryParse(element.GetAttribute("cellspacing"), out int cs))
                props.SetProperty(CssPropertyId.BorderSpacing, CssNumericLiteralValue.Create(cs, CssUnitType.Pixels));
            // HTML §10.3.3.2.2: a table's width attribute is a used-width hint and
            // its height attribute a minimum-height hint.
            if (PresentationalLength(element, "width") is { } tableWidth)
                props.SetProperty(CssPropertyId.Width, tableWidth);
            if (PresentationalLength(element, "height") is { } tableHeight)
                props.SetProperty(CssPropertyId.MinHeight, tableHeight);
        }

        // Cells, rows and column groups carry the same width hint; a cell's height
        // attribute is its height, a row's the minimum height of the row.
        if (tag is "TD" or "TH" or "TR" or "COL" or "COLGROUP")
        {
            if (PresentationalLength(element, "width") is { } cellWidth)
                props.SetProperty(CssPropertyId.Width, cellWidth);
            if (PresentationalLength(element, "height") is { } cellHeight)
                props.SetProperty(tag == "TR" ? CssPropertyId.MinHeight : CssPropertyId.Height, cellHeight);
        }

        if (tag is "TD" or "TH")
        {
            var tableParent = element.ParentElement;
            while (tableParent != null && tableParent.TagName.ToUpperInvariant() != "TABLE")
                tableParent = tableParent.ParentElement;
            if (tableParent != null)
            {
                if (int.TryParse(tableParent.GetAttribute("cellpadding"), out int cp) && cp >= 0)
                    props.SetProperty(CssPropertyId.PaddingTop, CssNumericLiteralValue.Create(cp, CssUnitType.Pixels));
                if (int.TryParse(tableParent.GetAttribute("border"), out int b) && b > 0)
                    ApplyBorderHints(props, b);
            }
        }

        if (props.PropertyCount > 0)
        {
            state.MatchedRules.Add(new MatchedRuleEntry { Properties = props, Priority = priority });
        }
    }

    /// <summary>HTML presentational width/height attributes are a non-negative
    /// number, optionally suffixed with '%' (HTML §10.3.3.2.2). Anything else is
    /// ignored, exactly as the UA style rules do.</summary>
    private static CssNumericLiteralValue? PresentationalLength(Element element, string attribute)
    {
        var raw = element.GetAttribute(attribute)?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        bool isPercent = raw.EndsWith('%');
        var number = isPercent ? raw[..^1] : raw;
        if (!float.TryParse(number.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
            return null;
        return CssNumericLiteralValue.Create(value,
            isPercent ? CssUnitType.Percentage : CssUnitType.Pixels);
    }

    private static void SetPx(CssPropertyValueSet props, string name, float value)
    {
        var id = CssPropertyIdExtensions.FromString(name);
        if (id != CssPropertyId.Invalid)
            props.SetProperty(id, CssNumericLiteralValue.Create(value, CssUnitType.Pixels));
    }

    private static void ApplyBorderHints(CssPropertyValueSet props, int b)
    {
        props.SetProperty(CssPropertyId.BorderTopWidth, CssNumericLiteralValue.Create(b, CssUnitType.Pixels));
        props.SetProperty(CssPropertyId.BorderRightWidth, CssNumericLiteralValue.Create(b, CssUnitType.Pixels));
        props.SetProperty(CssPropertyId.BorderBottomWidth, CssNumericLiteralValue.Create(b, CssUnitType.Pixels));
        props.SetProperty(CssPropertyId.BorderLeftWidth, CssNumericLiteralValue.Create(b, CssUnitType.Pixels));
        props.SetProperty(CssPropertyId.BorderTopStyle, CssIdentifierValue.Create(CssValueId.Solid));
        props.SetProperty(CssPropertyId.BorderRightStyle, CssIdentifierValue.Create(CssValueId.Solid));
        props.SetProperty(CssPropertyId.BorderBottomStyle, CssIdentifierValue.Create(CssValueId.Solid));
        props.SetProperty(CssPropertyId.BorderLeftStyle, CssIdentifierValue.Create(CssValueId.Solid));
        props.SetProperty(CssPropertyId.BorderTopColor, new CssColorValue(SkiaSharp.SKColors.Black));
        props.SetProperty(CssPropertyId.BorderRightColor, new CssColorValue(SkiaSharp.SKColors.Black));
        props.SetProperty(CssPropertyId.BorderBottomColor, new CssColorValue(SkiaSharp.SKColors.Black));
        props.SetProperty(CssPropertyId.BorderLeftColor, new CssColorValue(SkiaSharp.SKColors.Black));
    }

    private void MatchJsModifiedStyle(Element element, CascadeResolverState state)
    {
        if (element.Style == null || element.Style.Count == 0) return;
        var props = new CssPropertyValueSet();
        foreach (var kv in element.Style)
        {
            if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
            var id = kv.Key.StartsWith("--") || kv.Key.Contains('-')
                ? CssPropertyIdExtensions.FromString(kv.Key.Replace("--", "x-"))
                : CssPropertyIdExtensions.FromString(kv.Key);
            if (id != CssPropertyId.Invalid)
            {
                props.SetProperty(id, new CssIdentifierValue(CssValueId.Inherit));
                // Reparse the JS value through the shared applier by carrying the raw
                // text via a CssStringValue-equivalent; reuse the tokenizer.
                var block = CssParserImpl.ParseDeclarationBlock($"{kv.Key}: {kv.Value}", CssParserContext.Default());
                foreach (var p in block.Properties)
                    props.SetProperty((CssPropertyId)p.Name.Id, p.Value);
            }
            else
            {
                var block = CssParserImpl.ParseDeclarationBlock($"{kv.Key}: {kv.Value}", CssParserContext.Default());
                foreach (var p in block.Properties)
                    props.SetLonghandProperty(p);
            }
        }
        if (props.PropertyCount > 0)
        {
            state.MatchedRules.Add(new MatchedRuleEntry
            {
                Properties = props,
                Priority = new CascadePriority(CascadeOrigin.JsModified, 0, _treeOrderCounter++, 0, false)
            });
        }
    }

    private void MatchAuthorStyles(Element element, CascadeResolverState state)
    {
        int treeOrder = _treeOrderCounter++;
        int position = 0;
        foreach (var sheet in _authorSheets)
        {
            foreach (var rule in sheet.ChildRules)
            {
                if (rule is StyleRule styleRule)
                {
                    MatchStyleRule(styleRule, element, state, CascadeOrigin.Author, 0, treeOrder, ref position);
                }
                else if (rule is StyleRuleMedia media)
                {
                    if (MediaQueryEvaluator.Evaluate(media.ConditionText, _viewportWidth, _viewportHeight, _colorScheme, _mediaEnv))
                    {
                        MatchGroupRules(media.ChildRules, element, state, CascadeOrigin.Author, 0, treeOrder, ref position);
                    }
                }
                else if (rule is StyleRuleSupports supports)
                {
                    if (CssPropertyApplier.EvaluateSupportsCondition(supports.ConditionText))
                    {
                        MatchGroupRules(supports.ChildRules, element, state, CascadeOrigin.Author, 0, treeOrder, ref position);
                    }
                }
                else if (rule is StyleRuleLayerBlock layer)
                {
                    MatchGroupRules(layer.ChildRules, element, state, CascadeOrigin.Author, 0, treeOrder, ref position);
                }
                else if (rule is StyleRuleStartingStyle starting)
                {
                    MatchGroupRules(starting.ChildRules, element, state, CascadeOrigin.Author, 0, treeOrder, ref position);
                }
            }
        }
    }

    private void MatchGroupRules(List<StyleRuleBase> rules, Element element, CascadeResolverState state,
        CascadeOrigin origin, int layerOrder, int treeOrder, ref int position)
    {
        foreach (var rule in rules)
        {
            if (rule is StyleRule styleRule)
            {
                MatchStyleRule(styleRule, element, state, origin, layerOrder, treeOrder, ref position);
            }
            else if (rule is StyleRuleMedia media)
            {
                if (MediaQueryEvaluator.Evaluate(media.ConditionText, _viewportWidth, _viewportHeight, _colorScheme, _mediaEnv))
                    MatchGroupRules(media.ChildRules, element, state, origin, layerOrder, treeOrder, ref position);
            }
            else if (rule is StyleRuleSupports supports)
            {
                if (CssPropertyApplier.EvaluateSupportsCondition(supports.ConditionText))
                    MatchGroupRules(supports.ChildRules, element, state, origin, layerOrder, treeOrder, ref position);
            }
            else if (rule is StyleRuleContainer container)
            {
                // Containers not yet resolvable; treat as always matching.
                MatchGroupRules(container.ChildRules, element, state, origin, layerOrder, treeOrder, ref position);
            }
            else if (rule is StyleRuleLayerBlock layer)
            {
                MatchGroupRules(layer.ChildRules, element, state, origin, layerOrder, treeOrder, ref position);
            }
            else if (rule is StyleRuleScope scope)
            {
                MatchGroupRules(scope.ChildRules, element, state, origin, layerOrder, treeOrder, ref position);
            }
            else if (rule is StyleRuleStartingStyle starting)
            {
                MatchGroupRules(starting.ChildRules, element, state, origin, layerOrder, treeOrder, ref position);
            }
        }
    }

    private static bool HasPseudoElement(string selectorText, string name)
    {
        string twoColons = "::" + name;
        string oneColon = ":" + name;
        int at = selectorText.IndexOf(twoColons, StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
            return true;
        at = selectorText.IndexOf(oneColon, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return false;
        // A single colon counts only when it is not part of a double colon of a
        // different pseudo-element (e.g. "::before" handled above).
        return at == 0 || selectorText[at - 1] != ':';
    }

    /// <summary>Matches a style rule and, when it wins, records it with the
    /// matching selector's specificity and a monotonically increasing position
    /// so equal-specificity rules resolve by source order (per CSS cascading).</summary>
    private void MatchStyleRule(StyleRule rule, Element element, CascadeResolverState state,
        CascadeOrigin origin, int layerOrder, int treeOrder, ref int position)
    {
        foreach (var selector in rule.Selectors)
        {
            if (_checker.Match(selector, element))
            {
                // ::-webkit-scrollbar-* rules style the scrollbar side-car, not the
                // element (mirrors TryCollectScrollbarStyle in the string cascade).
                var selectorText = rule.OriginalSelectorText ?? selector.ToComplexText();
                if (TryCollectScrollbarStyle(element, selectorText, rule.Properties))
                    return;

                // Pseudo-element rules (::before / ::after) style generated content,
                // not the element itself: route their declarations to the element's
                // side-car exactly like the string cascade does, and skip the cascade.
                // The single-colon legacy spelling is accepted for these three
                // (CSS Selectors 3 §6.1, for backwards compatibility).
                if (HasPseudoElement(selectorText, "before"))
                {
                    foreach (var p in rule.Properties.Properties)
                        (element.BeforeStyles ??= new Dictionary<string, string>())
                            [p.Name.ToCssString()] = p.Value.CssText();
                    return;
                }
                if (HasPseudoElement(selectorText, "after"))
                {
                    foreach (var p in rule.Properties.Properties)
                        (element.AfterStyles ??= new Dictionary<string, string>())
                            [p.Name.ToCssString()] = p.Value.CssText();
                    return;
                }
                if (HasPseudoElement(selectorText, "marker"))
                {
                    foreach (var p in rule.Properties.Properties)
                        (element.MarkerStyles ??= new Dictionary<string, string>())
                            [p.Name.ToCssString()] = p.Value.CssText();
                    return;
                }
                if (HasPseudoElement(selectorText, "first-line"))
                {
                    foreach (var p in rule.Properties.Properties)
                        (element.FirstLineStyles ??= new Dictionary<string, string>())
                            [p.Name.ToCssString()] = p.Value.CssText();
                    return;
                }
                if (HasPseudoElement(selectorText, "first-letter"))
                {
                    foreach (var p in rule.Properties.Properties)
                        (element.FirstLetterStyles ??= new Dictionary<string, string>())
                            [p.Name.ToCssString()] = p.Value.CssText();
                    return;
                }

                selector.ComputeSpecificity();
                state.MatchedRules.Add(new MatchedRuleEntry
                {
                    Properties = ExpandShorthands(rule.Properties),
                    Priority = new CascadePriority(
                        origin, layerOrder, treeOrder, position++, false,
                        specificityA: selector.SpecificityA,
                        specificityB: selector.SpecificityB,
                        specificityC: selector.SpecificityC)
                });
                return;
            }
        }
    }

    /// <summary>
    /// Expands shorthand declarations (margin, padding, border, font, ...) into
    /// their longhand parts with the same source order, so shorthands and the
    /// explicit longhands they imply compete on the same cascade slot. This is
    /// what the string cascade (ShorthandExpander.Expand) already does before
    /// matching; the Prism pipeline must mirror it or a shorthand would be
    /// applied as one property ID and lose to / overwrite its own longhands.
    /// </summary>
    internal static CssPropertyValueSet ExpandShorthands(CssPropertyValueSet props)
    {
        bool needsExpansion = false;
        foreach (var p in props.Properties)
        {
            if (!p.Name.IsCustom && p.Name.Id != CssPropertyId.Variable && IsExpandableShorthand(p.Name.ToCssString()))
            {
                needsExpansion = true;
                break;
            }
        }
        if (!needsExpansion) return props;

        var result = new CssPropertyValueSet(props.ParserMode);
        foreach (var p in props.Properties)
        {
            if (p.Name.IsCustom || !IsExpandableShorthand(p.Name.ToCssString()))
            {
                result.SetLonghandProperty(p);
                continue;
            }

            string name = p.Name.ToCssString();
            string text = p.Value.CssText();

            // Shorthand values containing var() cannot be expanded until the
            // variables are resolved (which happens at apply time). Pass the
            // shorthand through as-is so the evaluator substitutes first.
            if (text.Contains("var(", StringComparison.OrdinalIgnoreCase))
            {
                result.SetLonghandProperty(p);
                continue;
            }

            var expanded = ShorthandExpander.ExpandProperty(name, text);
            // An expansion with nothing in it is not a shorthand that described no longhand —
            // it is a value the grammar rejected ('background: url(a) text text'). A declaration
            // the grammar rejects might as well not have been written, so it resets nothing
            // either: wiping the controlled longhands below took a 'background-clip' the same
            // rule had put in front of the shorthand with it (measured).
            if (expanded.Count == 0) continue;
            // A shorthand resets every longhand it controls: declarations that
            // were already in the set (from earlier rules) must be removed before
            // the expansion is written, otherwise a later shorthand would merge
            // with an earlier one instead of resetting it.
            var reset = new HashSet<CssPropertyId>();
            foreach (var kv in expanded)
            {
                var one = CssParserImpl.ParseDeclarationBlock($"{kv.Key}: {kv.Value}", CssParserContext.Default());
                foreach (var sub in one.Properties)
                {
                    if (!sub.Name.IsCustom) reset.Add(sub.Name.Id);
                    result.SetLonghandProperty(p.IsImportant && !sub.IsImportant
                        ? new CssPropertyValue(sub.Name, sub.Value, true, sub.IsImplicit, sub.ShorthandId)
                        : sub);
                }
            }
            // Longhands the shorthand controls but did not emit (invalid/absent
            // tokens) still reset to their initial value: drop any stale entry.
            foreach (var id in ShorthandExpander.GetControlledLonghands(name))
                if (!reset.Contains(id) && id != CssPropertyId.Invalid)
                    result.RemoveProperty(id);
        }
        return result;
    }

    private static bool IsExpandableShorthand(string name) => name switch
    {
        "margin" or "padding" or "border-width" or "border-style" or "border-color" or "border-radius" or
        "border-top" or "border-right" or "border-bottom" or "border-left" or "border" or "margin-block" or
        "margin-inline" or "padding-block" or "padding-inline" or "inset" or "gap" or "background" or
        "background-position" or
        "font" or "font-variant" or "flex" or "flex-flow" or "outline" or "text-decoration" or "text-emphasis" or "columns" or
        "column-rule" or "animation" or "transition" or "grid-area" or "grid-column" or "grid-row" or "mask" or
        "white-space" or "text-wrap" or "overflow" or "contain-intrinsic-size" or
        "place-content" or "place-items" or "place-self" or "list-style" or
        "scroll-margin" or "scroll-padding" or
        "border-block" or "border-inline" or "border-block-start" or "border-block-end" or
        "border-inline-start" or "border-inline-end" or "border-block-width" or
        "border-block-style" or "border-block-color" or "border-inline-width" or
        "border-inline-style" or "border-inline-color" => true,
        _ => false
    };

    private enum ScrollbarSlot { Bar, Thumb, Track, Corner, Button }

    /// <summary>
    /// Routes ::-webkit-scrollbar-* pseudo-element declarations into the element's
    /// <see cref="ScrollbarStyles"/> side-car. Returns false when the rule is not a
    /// scrollbar rule so normal cascade continues. Mirrors the string cascade.
    /// </summary>
    private static bool TryCollectScrollbarStyle(Element element, string selector, CssPropertyValueSet properties)
    {
        ScrollbarSlot? slot = GetScrollbarSlot(selector);
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

        foreach (var prop in properties.Properties)
        {
            var name = prop.Name.ToCssString().ToLowerInvariant();
            var value = prop.Value?.CssText().Trim() ?? "";
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
            catch { /* malformed declaration — ignore like the cascade does */ }
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
                float.TryParse(v[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out px)) return true;
            if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out px)) return true;
            return false;
        }

        static string FirstToken(string v) { int i = v.IndexOf(' '); return i > 0 ? v[..i] : v; }
        static string LastToken(string v) { int i = v.LastIndexOf(' '); return i > 0 ? v[(i + 1)..] : v; }
    }

    private void MatchInlineStyle(Element element, CascadeResolverState state)
    {
        var inlineText = element.GetAttribute("style");
        if (string.IsNullOrWhiteSpace(inlineText)) return;

        var props = CssParserImpl.ParseDeclarationBlock(inlineText, CssParserContext.Default());

        if (props.PropertyCount > 0)
        {
            // CSS Cascading 4 6.4: a style attribute is a separate cascade origin that
            // beats every normal declaration, so it must win on specificity rather than
            // source order (otherwise any class selector would override it). It still
            // loses to '!important' author declarations, which Compare() decides earlier.
            state.MatchedRules.Add(new MatchedRuleEntry
            {
                Properties = ExpandShorthands(props),
                Priority = new CascadePriority(CascadeOrigin.Author, 0, int.MaxValue, 0, false,
                    specificityA: int.MaxValue, specificityB: int.MaxValue, specificityC: int.MaxValue)
            });
        }
    }

    private static void InheritProperties(ComputedStyle style, ComputedStyle parent)
    {
        // Single definition lives in CssInheritance; this used to be a second, drifting copy
        // that had lost twenty-four inherited properties.
        Acrux.Core.Css.CssInheritance.Apply(style, parent);
    }


    private static void CollectNonDefaultProperties(ComputedStyle source, CssPropertyValueSet target)
    {
        // Only emit properties that differ from the initial values, otherwise the
        // registry fallback would act as an author rule that pins defaults (e.g.
        // font-family: Arial,sans-serif) and block inheritance from html.
        var def = new ComputedStyle();
        DumpColorIfDiff(CssPropertyId.Color, source.Color, def.Color, target);
        DumpFloatIfDiff(CssPropertyId.FontSize, source.FontSize, def.FontSize, target);
        if (source.FontWeight != def.FontWeight)
            target.SetProperty(CssPropertyId.FontWeight, new CssStringValue(((int)source.FontWeight).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (source.FontFamily != def.FontFamily)
            target.SetProperty(CssPropertyId.FontFamily, new CssStringValue(source.FontFamily));
        if (source.FontStyle != def.FontStyle)
            target.SetProperty(CssPropertyId.FontStyle, CssIdentifierValue.Create(source.FontStyle switch
            {
                Dom.FontStyleType.Italic => CssValueId.Italic,
                Dom.FontStyleType.Oblique => CssValueId.Oblique,
                _ => CssValueId.Normal
            }));
        if (source.LineHeight != def.LineHeight)
            target.SetProperty(CssPropertyId.LineHeight, CssNumericLiteralValue.Create(source.LineHeight, CssUnitType.Number));
        if (source.Display != def.Display)
            target.SetProperty(CssPropertyId.Display, CssIdentifierValue.Create(DisplayToId(source.Display)));
        if (source.Position != def.Position)
            target.SetProperty(CssPropertyId.Position, CssIdentifierValue.Create(PositionToId(source.Position)));
        if (source.WhiteSpace != def.WhiteSpace)
            target.SetProperty(CssPropertyId.WhiteSpace, CssIdentifierValue.Create(WhiteSpaceToId(source.WhiteSpace)));
        if (source.TextAlign != def.TextAlign)
            target.SetProperty(CssPropertyId.TextAlign, CssIdentifierValue.Create(TextAlignToId(source.TextAlign)));
        if (source.BackgroundColor != def.BackgroundColor && source.BackgroundColor.HasValue)
            target.SetProperty(CssPropertyId.BackgroundColor, new CssColorValue(source.BackgroundColor.Value));
        DumpLengthIfDiff(CssPropertyId.MarginTop, source.MarginTop, def.MarginTop, target);
        DumpLengthIfDiff(CssPropertyId.MarginBottom, source.MarginBottom, def.MarginBottom, target);
        DumpLengthIfDiff(CssPropertyId.MarginLeft, source.MarginLeft, def.MarginLeft, target);
        DumpLengthIfDiff(CssPropertyId.MarginRight, source.MarginRight, def.MarginRight, target);
        DumpLengthIfDiff(CssPropertyId.PaddingTop, source.PaddingTop, def.PaddingTop, target);
        DumpLengthIfDiff(CssPropertyId.PaddingBottom, source.PaddingBottom, def.PaddingBottom, target);
        DumpLengthIfDiff(CssPropertyId.PaddingLeft, source.PaddingLeft, def.PaddingLeft, target);
        DumpLengthIfDiff(CssPropertyId.PaddingRight, source.PaddingRight, def.PaddingRight, target);
        if (source.BorderTopWidth != def.BorderTopWidth)
            target.SetProperty(CssPropertyId.BorderTopWidth, CssNumericLiteralValue.Create(source.BorderTopWidth, CssUnitType.Pixels));
        if (source.BorderRightWidth != def.BorderRightWidth)
            target.SetProperty(CssPropertyId.BorderRightWidth, CssNumericLiteralValue.Create(source.BorderRightWidth, CssUnitType.Pixels));
        if (source.BorderBottomWidth != def.BorderBottomWidth)
            target.SetProperty(CssPropertyId.BorderBottomWidth, CssNumericLiteralValue.Create(source.BorderBottomWidth, CssUnitType.Pixels));
        if (source.BorderLeftWidth != def.BorderLeftWidth)
            target.SetProperty(CssPropertyId.BorderLeftWidth, CssNumericLiteralValue.Create(source.BorderLeftWidth, CssUnitType.Pixels));
        if (source.Visibility != def.Visibility)
            target.SetProperty(CssPropertyId.Visibility, CssIdentifierValue.Create(
                source.Visibility == Dom.VisibilityType.Hidden ? CssValueId.Hidden :
                source.Visibility == Dom.VisibilityType.Collapse ? CssValueId.Collapse : CssValueId.Visible));
        if (source.Overflow != def.Overflow)
            target.SetProperty(CssPropertyId.Overflow, CssIdentifierValue.Create(OverflowToId(source.Overflow)));
        if (source.Float != def.Float)
            target.SetProperty(CssPropertyId.Float, CssIdentifierValue.Create(source.Float switch
            {
                Dom.FloatType.Left => CssValueId.Left,
                Dom.FloatType.Right => CssValueId.Right,
                Dom.FloatType.InlineStart => CssValueId.InlineStart,
                Dom.FloatType.InlineEnd => CssValueId.InlineEnd,
                _ => CssValueId.None
            }));
        if (source.TextTransform != def.TextTransform)
            target.SetProperty(CssPropertyId.TextTransform, new CssStringValue(source.TextTransform));
        if (source.TextDecoration != def.TextDecoration)
            target.SetProperty(CssPropertyId.TextDecoration, CssIdentifierValue.Create(source.TextDecoration switch
            {
                Dom.TextDecorationType.Underline => CssValueId.Underline,
                Dom.TextDecorationType.Overline => CssValueId.Overline,
                Dom.TextDecorationType.LineThrough => CssValueId.LineThrough,
                _ => CssValueId.None
            }));
        if (!string.IsNullOrEmpty(source.Cursor))
            target.SetProperty(CssPropertyId.Cursor, new CssStringValue(source.Cursor));
    }

    private static void DumpColorIfDiff(CssPropertyId id, SkiaSharp.SKColor value, SkiaSharp.SKColor def, CssPropertyValueSet target)
    {
        if (!Equals(value, def))
            target.SetProperty(id, new CssColorValue(value));
    }

    private static void DumpFloatIfDiff(CssPropertyId id, float value, float def, CssPropertyValueSet target)
    {
        if (value != def)
            target.SetProperty(id, CssNumericLiteralValue.Create(value, CssUnitType.Pixels));
    }

    private static void DumpLengthIfDiff(CssPropertyId id, Length value, Length def, CssPropertyValueSet target)
    {
        if (value == def) return;
        if (value is not PixelLength p) return;
        target.SetProperty(id, CssNumericLiteralValue.Create(p.Value, CssUnitType.Pixels));
    }

    private static CssValueId OverflowToId(Dom.OverflowType o) => o switch
    {
        Dom.OverflowType.Hidden => CssValueId.Hidden,
        Dom.OverflowType.Scroll => CssValueId.Scroll,
        Dom.OverflowType.Auto => CssValueId.Auto,
        _ => CssValueId.Visible
    };

    private static CssValueId DisplayToId(Dom.DisplayType d) => d switch
    {
        Dom.DisplayType.None => CssValueId.None,
        Dom.DisplayType.Block => CssValueId.Block,
        Dom.DisplayType.Inline => CssValueId.Inline,
        Dom.DisplayType.InlineBlock => CssValueId.InlineBlock,
        Dom.DisplayType.Flex => CssValueId.Flex,
        Dom.DisplayType.InlineFlex => CssValueId.InlineFlex,
        Dom.DisplayType.Grid => CssValueId.Grid,
        Dom.DisplayType.InlineGrid => CssValueId.InlineGrid,
        Dom.DisplayType.Table => CssValueId.Table,
        Dom.DisplayType.ListItem => CssValueId.Block,
        _ => CssValueId.Block
    };

    private static CssValueId PositionToId(Dom.PositionType p) => p switch
    {
        Dom.PositionType.Relative => CssValueId.Relative,
        Dom.PositionType.Absolute => CssValueId.Absolute,
        Dom.PositionType.Fixed => CssValueId.Fixed,
        Dom.PositionType.Sticky => CssValueId.Sticky,
        _ => CssValueId.Static
    };

    private static CssValueId WhiteSpaceToId(Dom.WhiteSpaceMode w) => w switch
    {
        Dom.WhiteSpaceMode.Pre => CssValueId.Pre,
        Dom.WhiteSpaceMode.PreWrap => CssValueId.PreWrap,
        Dom.WhiteSpaceMode.PreLine => CssValueId.PreLine,
        Dom.WhiteSpaceMode.Nowrap => CssValueId.Nowrap,
        _ => CssValueId.Normal
    };

    private static CssValueId TextAlignToId(Dom.TextAlignType t) => t switch
    {
        Dom.TextAlignType.Left => CssValueId.Left,
        Dom.TextAlignType.Right => CssValueId.Right,
        Dom.TextAlignType.Center => CssValueId.Center,
        Dom.TextAlignType.Justify => CssValueId.Justify,
        Dom.TextAlignType.End => CssValueId.End,
        _ => CssValueId.Start
    };
}