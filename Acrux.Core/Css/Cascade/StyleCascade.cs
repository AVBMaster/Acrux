using Acrux.Core.Css.Matcher;
using Acrux.Core.Css.Properties;
using Acrux.Core.Css.Resolver;
using Acrux.Core.Css.Rules;
using Acrux.Core.Css.Values;
using Acrux.Core.Dom;

namespace Acrux.Core.Css.Cascade;

/// <summary>
/// The CSS cascade engine, mirroring Blink's StyleCascade.
/// Analyzes matched rules into a CascadeMap, then applies them to build a ComputedStyle.
/// </summary>
public class StyleCascade
{
    private readonly CascadeResolverState _state;
    private readonly CascadeMap _map = new();
    private uint _generation;
    private bool _needsAnalyze = true;

    public StyleCascade(CascadeResolverState state) => _state = state;
    public CascadeMap Map => _map;
    public bool InlineStyleLost => _map.InlineStyleLost;

    public void AnalyzeIfNeeded()
    {
        if (!_needsAnalyze) return;
        _map.Reset();
        _generation = 0;

        foreach (var entry in _state.MatchedRules)
        {
            var priority = entry.Priority;
            foreach (var prop in entry.Properties.Properties)
            {
                // Per-declaration !important outranks every normal declaration
                // of the same property regardless of origin/specificity.
                var propPriority = prop.IsImportant ? priority.WithImportant() : priority;
                if (prop.Name.IsCustom)
                    _map.Add(prop.Name.CustomName!, propPriority);
                else
                    _map.Add(prop.Name.Id, propPriority);
            }
        }
        _needsAnalyze = false;
    }

    public void Apply(CascadeFilter? filter = null)
    {
        AnalyzeIfNeeded();

        ApplyCascadeAffecting();

        ApplyHighPriority();

        ApplyMatchResult(filter);

        ApplyInterpolations();
    }

    private void ApplyCascadeAffecting()
    {
        ApplyIfPresent(CssPropertyId.Direction);
        ApplyIfPresent(CssPropertyId.WritingMode);
        ApplyIfPresent(CssPropertyId.Zoom);
    }

    private void ApplyHighPriority()
    {
        foreach (var id in HighPriorityProperties)
            ApplyIfPresent(id);
    }

    private static readonly CssPropertyId[] HighPriorityProperties =
    {
        CssPropertyId.FontSize, CssPropertyId.FontWeight, CssPropertyId.FontStyle,
        CssPropertyId.FontFamily, CssPropertyId.FontVariant, CssPropertyId.FontVariantCaps, CssPropertyId.FontStretch,
        CssPropertyId.FontKerning, CssPropertyId.FontSizeAdjust, CssPropertyId.Font,
        CssPropertyId.LineHeight, CssPropertyId.Color, CssPropertyId.Visibility,
        CssPropertyId.TextAlign, CssPropertyId.TextTransform, CssPropertyId.TextIndent,
        CssPropertyId.LetterSpacing, CssPropertyId.WordSpacing, CssPropertyId.WhiteSpace,
        CssPropertyId.Direction, CssPropertyId.WritingMode,
    };

    private void ApplyMatchResult(CascadeFilter? filter)
    {
        // Apply custom properties FIRST so that var() references in regular
        // properties resolve against the current element's declarations.
        foreach (var customName in _map.CustomNames)
        {
            if (filter?.Rejects(new CssPropertyName(customName)) == true) continue;
            ApplyCustomIfPresent(customName);
        }
        foreach (var id in _map.NativeIds)
        {
            if (filter?.Rejects(new CssPropertyName(id)) == true) continue;
            if (HighPriorityProperties.Contains(id)) continue;
            if (id is CssPropertyId.Direction or CssPropertyId.WritingMode or CssPropertyId.Zoom) continue;
            ApplyIfPresent(id);
        }
    }

    private void ApplyInterpolations()
    {
        foreach (var entry in _state.Interpolations)
        {
            foreach (var prop in entry.Properties.Properties)
            {
                if (prop.Name.IsCustom)
                    _state.ApplyCustomProperty(prop.Name.CustomName!, prop.Value);
                else
                    ApplyProperty(prop.Name.Id, prop.Value);
            }
        }
    }

    private void ApplyIfPresent(CssPropertyId id)
    {
        if (!_map.TryGetWinner(id, out var priority)) return;
        if (!priority.IsRelevant) return;
        var matchedEntry = FindMatchedEntry(priority);
        if (matchedEntry == null) return;
        // Within one declaration block the later declaration wins (a shorthand
        // expansion's default must not shadow an explicit longhand that follows it).
        var prop = matchedEntry.Properties.Properties
            .LastOrDefault(p => p.Name.Id == id && !p.Name.IsCustom);
        if (prop.Value != null)
            ApplyProperty(id, prop.Value);
    }

    private void ApplyCustomIfPresent(string customName)
    {
        if (!_map.TryGetWinner(customName, out var priority)) return;
        if (!priority.IsRelevant) return;
        var matchedEntry = FindMatchedEntry(priority);
        if (matchedEntry == null) return;
        var prop = matchedEntry.Properties.Properties
            .LastOrDefault(p => p.Name.IsCustom && p.Name.CustomName == customName);
        if (prop.Value != null)
            _state.ApplyCustomProperty(customName, prop.Value);
    }

    private void ApplyProperty(CssPropertyId id, CssValue value)
    {
        _state.ApplyProperty(id, value);
    }

    private MatchedRuleEntry? FindMatchedEntry(CascadePriority priority)
    {
        // The map may carry a per-declaration !important variant of the rule's
        // priority; match on the rule identity (origin/layer/tree/position/spec)
        // and ignore the important bit.
        foreach (var entry in _state.MatchedRules)
        {
            var ep = entry.Priority;
            if (ep.Origin == priority.Origin && ep.LayerOrder == priority.LayerOrder &&
                ep.TreeOrder == priority.TreeOrder && ep.Position == priority.Position &&
                ep.SpecificityA == priority.SpecificityA && ep.SpecificityB == priority.SpecificityB &&
                ep.SpecificityC == priority.SpecificityC)
                return entry;
        }
        return null;
    }

    public void Reset()
    {
        _map.Reset();
        _needsAnalyze = true;
        _generation = 0;
    }
}

/// <summary>
/// State accumulated during cascade resolution, bridging to the existing ComputedStyle.
/// </summary>
public class CascadeResolverState
{
    public List<MatchedRuleEntry> MatchedRules { get; } = new();
    public List<MatchedRuleEntry> Interpolations { get; } = new();
    public Element? Element { get; set; }
    public ComputedStyle? ParentStyle { get; set; }
    public float ViewportWidth { get; set; } = 1024;
    public float ViewportHeight { get; set; } = 768;
    public float RootFontSize { get; set; } = 16;
    public float FontSize { get; set; } = 16;
    public string ColorScheme { get; set; } = "light";

    public void ApplyProperty(CssPropertyId id, CssValue value)
    {
        var style = Element?.ComputedStyle;
        if (style == null) return;
        // Font-relative units inside the raw value (calc/min/max) resolve
        // against this element's own font.
        using var _fontUnitScope = FontUnitContext.Use(style);
        // Delegate to the shared Prism engine property applier so every
        // property supported by the string cascade is available here too.
        var name = CssPropertyIdExtensions.ToString(id);
        var text = value.CssText();
        text = CssFunctionEvaluator.Evaluate(text, Element,
            FontSize, RootFontSize, ViewportWidth, ViewportHeight);
        if (CssPropertyTraits.TryApplyCssWideKeyword(style, name, text, ParentStyle))
            return;
        // Font properties are deliberately not in CssPropertyApplier.Apply's
        // switch (they are "high-priority": em/ch units depend on font-size).
        // Resolve them here where the parent style context is available.
        switch (id)
        {
            case CssPropertyId.FontSize:
                style.FontSize = CssPropertyApplier.ParseFontSize(text, ParentStyle, RootFontSize, ViewportWidth, ViewportHeight);
                FontSize = style.FontSize;
                return;
            case CssPropertyId.FontWeight:
                style.FontWeight = CssPropertyApplier.ParseFontWeight(text);
                return;
            case CssPropertyId.FontStyle:
                style.FontStyle = CssPropertyApplier.ParseFontStyle(text, style);
                return;
            case CssPropertyId.FontFamily:
                style.FontFamily = CssPropertyApplier.ParseFontFamily(text);
                return;
            case CssPropertyId.LineHeight:
                Acrux.Core.Fonts.LineBoxMetrics.ApplyLineHeight(style, text);
                return;
        }
        CssPropertyApplier.Apply(style, name, text);
    }

    public void ApplyCustomProperty(string name, CssValue value)
    {
        var style = Element?.ComputedStyle;
        if (style == null) return;
        var text = value.CssText();
        text = CssFunctionEvaluator.Evaluate(text, Element,
            FontSize, RootFontSize, ViewportWidth, ViewportHeight);
        // Custom property names come from the parser with the leading "--" intact;
        // ComputedStyle stores them without the prefix (so var(--x) lookups by the
        // evaluator, which strips "--", resolve correctly).
        style.SetCustomProperty(name.StartsWith("--") ? name[2..] : name, text);
    }

    private static float ParseFloat(string text, float defaultVal) =>
        float.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : defaultVal;

    private static SkiaSharp.SKColor ParseColor(string value)
    {
        try { return ColorParser.Parse(value); } catch { return SkiaSharp.SKColors.Transparent; }
    }

    private static Dom.FontWeight ParseFontWeight(string value) => CssPropertyApplier.ParseFontWeight(value);

    private static Dom.FontStyleType ParseFontStyle(string value) => value.ToLowerInvariant() switch
    {
        "italic" => Dom.FontStyleType.Italic,
        "oblique" => Dom.FontStyleType.Oblique,
        _ => Dom.FontStyleType.Normal
    };

    private static Dom.TextDecorationType ParseTextDecoration(string value) => value.ToLowerInvariant() switch
    {
        "underline" => Dom.TextDecorationType.Underline,
        "overline" => Dom.TextDecorationType.Overline,
        "line-through" => Dom.TextDecorationType.LineThrough,
        _ => Dom.TextDecorationType.None
    };

    private static Dom.GridAutoFlowType ParseGridAutoFlow(string value) => value.ToLowerInvariant() switch
    {
        "column" => Dom.GridAutoFlowType.Column,
        "dense" => Dom.GridAutoFlowType.Dense,
        "row dense" => Dom.GridAutoFlowType.Dense,
        "column dense" => Dom.GridAutoFlowType.Dense,
        _ => Dom.GridAutoFlowType.Row
    };

    private static Dom.ResizeType ParseResizeType(string value) => value.ToLowerInvariant() switch
    {
        "both" => Dom.ResizeType.Both,
        "horizontal" => Dom.ResizeType.Horizontal,
        "vertical" => Dom.ResizeType.Vertical,
        _ => Dom.ResizeType.None
    };

    private static Dom.MixBlendModeType ParseMixBlendMode(string value) => value.ToLowerInvariant() switch
    {
        "multiply" => Dom.MixBlendModeType.Multiply,
        "screen" => Dom.MixBlendModeType.Screen,
        "overlay" => Dom.MixBlendModeType.Overlay,
        "darken" => Dom.MixBlendModeType.Darken,
        "lighten" => Dom.MixBlendModeType.Lighten,
        "color-dodge" => Dom.MixBlendModeType.ColorDodge,
        "color-burn" => Dom.MixBlendModeType.ColorBurn,
        "hard-light" => Dom.MixBlendModeType.HardLight,
        "soft-light" => Dom.MixBlendModeType.SoftLight,
        "difference" => Dom.MixBlendModeType.Difference,
        "exclusion" => Dom.MixBlendModeType.Exclusion,
        "hue" => Dom.MixBlendModeType.Hue,
        "saturation" => Dom.MixBlendModeType.Saturation,
        "color" => Dom.MixBlendModeType.Color,
        "luminosity" => Dom.MixBlendModeType.Luminosity,
        _ => Dom.MixBlendModeType.Normal
    };

    private static Dom.IsolationType ParseIsolationType(string value) =>
        value.Equals("isolate", StringComparison.OrdinalIgnoreCase) ? Dom.IsolationType.Isolate : Dom.IsolationType.Auto;

    private static Dom.ContainType ParseContainType(string value) => value.ToLowerInvariant() switch
    {
        "strict" => Dom.ContainType.Strict,
        "content" => Dom.ContainType.Content,
        "layout" => Dom.ContainType.Layout,
        "paint" => Dom.ContainType.Paint,
        "size" => Dom.ContainType.Size,
        _ => Dom.ContainType.None
    };

    private static Dom.ContentVisibilityType ParseContentVisibility(string value) => value.ToLowerInvariant() switch
    {
        "auto" => Dom.ContentVisibilityType.Auto,
        "hidden" => Dom.ContentVisibilityType.Hidden,
        _ => Dom.ContentVisibilityType.Visible
    };

    private static Dom.ScrollBehaviorType ParseScrollBehavior(string value) =>
        value.Equals("smooth", StringComparison.OrdinalIgnoreCase) ? Dom.ScrollBehaviorType.Smooth : Dom.ScrollBehaviorType.Auto;

    private static Dom.OverscrollBehaviorType ParseOverscrollBehavior(string value) => value.ToLowerInvariant() switch
    {
        "contain" => Dom.OverscrollBehaviorType.Contain,
        "none" => Dom.OverscrollBehaviorType.None,
        _ => Dom.OverscrollBehaviorType.Auto
    };

    private static Dom.VerticalAlignType ParseVerticalAlign(string value) => value.ToLowerInvariant() switch
    {
        "top" => Dom.VerticalAlignType.Top,
        "middle" => Dom.VerticalAlignType.Middle,
        "bottom" => Dom.VerticalAlignType.Bottom,
        "sub" => Dom.VerticalAlignType.Sub,
        "super" => Dom.VerticalAlignType.Super,
        "text-top" => Dom.VerticalAlignType.TextTop,
        "text-bottom" => Dom.VerticalAlignType.TextBottom,
        "inherit" => Dom.VerticalAlignType.Inherit,
        _ => Dom.VerticalAlignType.Baseline
    };

    private static Dom.WritingModeType ParseWritingMode(string value) => value.ToLowerInvariant() switch
    {
        "vertical-rl" => Dom.WritingModeType.VerticalRl,
        "vertical-lr" => Dom.WritingModeType.VerticalLr,
        _ => Dom.WritingModeType.HorizontalTb
    };

    private static Dom.BackgroundRepeat ParseBackgroundRepeat(string value)
        => CssPropertyApplier.ParseBackgroundRepeat(value);

    private static Dom.BackgroundAttachment ParseBackgroundAttachment(string value) => value.ToLowerInvariant() switch
    {
        "fixed" => Dom.BackgroundAttachment.Fixed,
        "local" => Dom.BackgroundAttachment.Local,
        _ => Dom.BackgroundAttachment.Scroll
    };

    private static Dom.BackgroundSizeType ParseBackgroundSize(string value) => value.ToLowerInvariant() switch
    {
        "cover" => Dom.BackgroundSizeType.Cover,
        "contain" => Dom.BackgroundSizeType.Contain,
        _ => Dom.BackgroundSizeType.Auto
    };

    private static Dom.TextOverflowType ParseTextOverflow(string value) =>
        value.Equals("ellipsis", StringComparison.OrdinalIgnoreCase) ? Dom.TextOverflowType.Ellipsis : Dom.TextOverflowType.Clip;

    /// <summary>'text-decoration-line' is a list; the shared parser in
    /// <see cref="Resolver.CssPropertyApplier"/> produces the flag set.</summary>
    private static Dom.TextDecorationLineType ParseTextDecorationLine(string value) =>
        Resolver.CssPropertyApplier.ParseTextDecorationLine(value);

    private static Dom.TextDecorationStyleType ParseTextDecorationStyle(string value) => value.ToLowerInvariant() switch
    {
        "double" => Dom.TextDecorationStyleType.Double,
        "dotted" => Dom.TextDecorationStyleType.Dotted,
        "dashed" => Dom.TextDecorationStyleType.Dashed,
        "wavy" => Dom.TextDecorationStyleType.Wavy,
        _ => Dom.TextDecorationStyleType.Solid
    };

    private static Dom.BackgroundBlendModeType ParseBackgroundBlendMode(string value) => value.ToLowerInvariant() switch
    {
        "multiply" => Dom.BackgroundBlendModeType.Multiply,
        "screen" => Dom.BackgroundBlendModeType.Screen,
        "overlay" => Dom.BackgroundBlendModeType.Overlay,
        "darken" => Dom.BackgroundBlendModeType.Darken,
        "lighten" => Dom.BackgroundBlendModeType.Lighten,
        "color-dodge" => Dom.BackgroundBlendModeType.ColorDodge,
        "color-burn" => Dom.BackgroundBlendModeType.ColorBurn,
        "hard-light" => Dom.BackgroundBlendModeType.HardLight,
        "soft-light" => Dom.BackgroundBlendModeType.SoftLight,
        "difference" => Dom.BackgroundBlendModeType.Difference,
        "exclusion" => Dom.BackgroundBlendModeType.Exclusion,
        "hue" => Dom.BackgroundBlendModeType.Hue,
        "saturation" => Dom.BackgroundBlendModeType.Saturation,
        "color" => Dom.BackgroundBlendModeType.Color,
        "luminosity" => Dom.BackgroundBlendModeType.Luminosity,
        _ => Dom.BackgroundBlendModeType.Normal
    };

    private static Dom.ImageRenderingType ParseImageRendering(string value) => value.ToLowerInvariant() switch
    {
        "crisp-edges" => Dom.ImageRenderingType.CrispEdges,
        "pixelated" => Dom.ImageRenderingType.Pixelated,
        _ => Dom.ImageRenderingType.Auto
    };

    private static Dom.ForcedColorAdjustType ParseForcedColorAdjust(string value) =>
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ? Dom.ForcedColorAdjustType.None : Dom.ForcedColorAdjustType.Auto;

    private static Dom.HyphensType ParseHyphens(string value) => value.ToLowerInvariant() switch
    {
        "manual" => Dom.HyphensType.Manual,
        "auto" => Dom.HyphensType.Auto,
        _ => Dom.HyphensType.None
    };

    private static Dom.LineBreakType ParseLineBreak(string value) => value.ToLowerInvariant() switch
    {
        "loose" => Dom.LineBreakType.Loose,
        "normal" => Dom.LineBreakType.Normal,
        "strict" => Dom.LineBreakType.Strict,
        "anywhere" => Dom.LineBreakType.Anywhere,
        _ => Dom.LineBreakType.Auto
    };

    private static Dom.TextJustifyType ParseTextJustify(string value) => value.ToLowerInvariant() switch
    {
        "inter-word" => Dom.TextJustifyType.InterWord,
        "inter-character" => Dom.TextJustifyType.InterCharacter,
        "none" => Dom.TextJustifyType.None,
        _ => Dom.TextJustifyType.Auto
    };

    private static Dom.OverflowAnchorType ParseOverflowAnchor(string value) =>
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ? Dom.OverflowAnchorType.None : Dom.OverflowAnchorType.Auto;

    private static float ParseLineHeight(string text, float fontSize)
    {
        if (text == "normal") return fontSize * 1.2f;
        if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num))
            return num > 10 ? num : num * fontSize;
        return Length.ParseFontSize(text, fontSize);
    }

    private static Length? ParseLength(string text, float defaultVal = 0)
    {
        if (text == "auto" || text == "inherit" || text == "initial") return Dom.AutoLength.Instance;
        if (text == "0") return new Dom.PixelLength(0);
        return Length.Parse(text);
    }

    private static float ParseBorderWidth(string text)
    {
        if (text == "thin") return 1; if (text == "medium") return 3; if (text == "thick") return 5;
        var len = Length.Parse(text);
        return len.ToPixels(16, 16, 1024, 768);
    }

    private static Dom.BorderStyle ParseBorderStyle(string text) => text.ToLowerInvariant() switch
    {
        "solid" => Dom.BorderStyle.Solid, "dashed" => Dom.BorderStyle.Dashed,
        "dotted" => Dom.BorderStyle.Dotted, "double" => Dom.BorderStyle.Double,
        "groove" => Dom.BorderStyle.Groove, "ridge" => Dom.BorderStyle.Ridge,
        "inset" => Dom.BorderStyle.Inset, "outset" => Dom.BorderStyle.Outset,
        _ => Dom.BorderStyle.None
    };

    private static Dom.DisplayType ParseDisplay(string text) => text.ToLowerInvariant() switch
    {
        "none" => Dom.DisplayType.None, "block" => Dom.DisplayType.Block,
        "inline" => Dom.DisplayType.Inline, "inline-block" => Dom.DisplayType.InlineBlock,
        "flex" => Dom.DisplayType.Flex, "inline-flex" => Dom.DisplayType.InlineFlex,
        "grid" => Dom.DisplayType.Grid, "inline-grid" => Dom.DisplayType.InlineGrid,
        "table" => Dom.DisplayType.Table,
        "table-row" => Dom.DisplayType.TableRow, "table-cell" => Dom.DisplayType.TableCell,
        "table-column" => Dom.DisplayType.TableColumn, "table-caption" => Dom.DisplayType.TableCaption,
        "list-item" => Dom.DisplayType.ListItem,
        "contents" => Dom.DisplayType.Contents, _ => Dom.DisplayType.Inline
    };

    private static Dom.PositionType ParsePosition(string text) => text.ToLowerInvariant() switch
    {
        "static" => Dom.PositionType.Static, "relative" => Dom.PositionType.Relative,
        "absolute" => Dom.PositionType.Absolute, "fixed" => Dom.PositionType.Fixed,
        "sticky" => Dom.PositionType.Sticky, _ => Dom.PositionType.Static
    };

    private static Dom.FloatType ParseFloatType(string text) => text.ToLowerInvariant() switch
    {
        "left" => Dom.FloatType.Left, "right" => Dom.FloatType.Right, _ => Dom.FloatType.None
    };

    private static Dom.ClearType ParseClearType(string text) => text.ToLowerInvariant() switch
    {
        "left" => Dom.ClearType.Left, "right" => Dom.ClearType.Right,
        "both" => Dom.ClearType.Both, _ => Dom.ClearType.None
    };

    private static Dom.OverflowType ParseOverflow(string text) => text.ToLowerInvariant() switch
    {
        "visible" => Dom.OverflowType.Visible, "hidden" => Dom.OverflowType.Hidden,
        "scroll" => Dom.OverflowType.Scroll, "auto" => Dom.OverflowType.Auto,
        _ => Dom.OverflowType.Visible
    };

    private static Dom.VisibilityType ParseVisibility(string text) => text.ToLowerInvariant() switch
    {
        "hidden" => Dom.VisibilityType.Hidden, "collapse" => Dom.VisibilityType.Collapse,
        _ => Dom.VisibilityType.Visible
    };

    private static Dom.TextAlignType ParseTextAlign(string text) => text.ToLowerInvariant() switch
    {
        "left" => Dom.TextAlignType.Left, "right" => Dom.TextAlignType.Right,
        "center" => Dom.TextAlignType.Center, "justify" => Dom.TextAlignType.Justify,
        "start" => Dom.TextAlignType.Start, "end" => Dom.TextAlignType.End, _ => Dom.TextAlignType.Left
    };

    private static Dom.WhiteSpaceMode ParseWhiteSpace(string text) => text.ToLowerInvariant() switch
    {
        "normal" => Dom.WhiteSpaceMode.Normal, "pre" => Dom.WhiteSpaceMode.Pre,
        "nowrap" => Dom.WhiteSpaceMode.Nowrap, "pre-wrap" => Dom.WhiteSpaceMode.PreWrap,
        "pre-line" => Dom.WhiteSpaceMode.PreLine,
        "break-spaces" => Dom.WhiteSpaceMode.BreakSpaces,
        _ => Dom.WhiteSpaceMode.Normal
    };

    private static Dom.WordBreakMode ParseWordBreak(string text) => text.ToLowerInvariant() switch
    {
        "break-all" => Dom.WordBreakMode.BreakAll,
        "break-word" => Dom.WordBreakMode.BreakWord,
        "keep-all" => Dom.WordBreakMode.KeepAll, _ => Dom.WordBreakMode.Normal
    };

    private static Dom.OverflowWrapMode ParseOverflowWrap(string text) => text.ToLowerInvariant() switch
    {
        "break-word" => Dom.OverflowWrapMode.BreakWord, "anywhere" => Dom.OverflowWrapMode.Anywhere,
        _ => Dom.OverflowWrapMode.Normal
    };

    private static Dom.ListStyleType ParseListStyleType(string text) => text.ToLowerInvariant() switch
    {
        "disc" => Dom.ListStyleType.Disc, "circle" => Dom.ListStyleType.Circle,
        "square" => Dom.ListStyleType.Square, "decimal" => Dom.ListStyleType.Decimal,
        "lower-roman" => Dom.ListStyleType.LowerRoman, "upper-roman" => Dom.ListStyleType.UpperRoman,
        "lower-alpha" => Dom.ListStyleType.LowerAlpha, "upper-alpha" => Dom.ListStyleType.UpperAlpha,
        "none" => Dom.ListStyleType.None, _ => Dom.ListStyleType.Disc
    };

    private static Dom.FlexDirectionType ParseFlexDirection(string text) => text.ToLowerInvariant() switch
    {
        "row" => Dom.FlexDirectionType.Row, "row-reverse" => Dom.FlexDirectionType.RowReverse,
        "column" => Dom.FlexDirectionType.Column, "column-reverse" => Dom.FlexDirectionType.ColumnReverse,
        _ => Dom.FlexDirectionType.Row
    };

    private static Dom.FlexWrapType ParseFlexWrap(string text) => text.ToLowerInvariant() switch
    {
        "nowrap" => Dom.FlexWrapType.NoWrap, "wrap" => Dom.FlexWrapType.Wrap,
        "wrap-reverse" => Dom.FlexWrapType.WrapReverse, _ => Dom.FlexWrapType.NoWrap
    };

    private static Dom.JustifyContentType ParseJustifyContent(string text) => text.ToLowerInvariant() switch
    {
        "flex-start" => Dom.JustifyContentType.FlexStart, "flex-end" => Dom.JustifyContentType.FlexEnd,
        "center" => Dom.JustifyContentType.Center, "space-between" => Dom.JustifyContentType.SpaceBetween,
        "space-around" => Dom.JustifyContentType.SpaceAround,
        "space-evenly" => Dom.JustifyContentType.SpaceEvenly, _ => Dom.JustifyContentType.FlexStart
    };

    private static Dom.AlignItemsType ParseAlignItems(string text) => text.ToLowerInvariant() switch
    {
        "flex-start" => Dom.AlignItemsType.FlexStart, "flex-end" => Dom.AlignItemsType.FlexEnd,
        "center" => Dom.AlignItemsType.Center, "baseline" => Dom.AlignItemsType.Baseline,
        "stretch" => Dom.AlignItemsType.Stretch, _ => Dom.AlignItemsType.Stretch
    };

    private static Dom.AlignSelfType ParseAlignSelf(string text) => text.ToLowerInvariant() switch
    {
        "auto" => Dom.AlignSelfType.Auto, "flex-start" => Dom.AlignSelfType.FlexStart,
        "flex-end" => Dom.AlignSelfType.FlexEnd, "center" => Dom.AlignSelfType.Center,
        "baseline" => Dom.AlignSelfType.Baseline, "stretch" => Dom.AlignSelfType.Stretch,
        _ => Dom.AlignSelfType.Auto
    };

    private static Dom.ObjectFitType ParseObjectFit(string text) => text.ToLowerInvariant() switch
    {
        "fill" => Dom.ObjectFitType.Fill, "contain" => Dom.ObjectFitType.Contain,
        "cover" => Dom.ObjectFitType.Cover, "none" => Dom.ObjectFitType.None,
        "scale-down" => Dom.ObjectFitType.ScaleDown, _ => Dom.ObjectFitType.Fill
    };
}

/// <summary>A matched rule entry with its cascade priority.</summary>
public class MatchedRuleEntry
{
    public CssPropertyValueSet Properties { get; set; } = new();
    public CascadePriority Priority { get; set; }
    public RuleSet.RuleData? RuleData { get; set; }
}

/// <summary>Filter for cascade application, used to skip certain properties.</summary>
public class CascadeFilter
{
    private readonly HashSet<CssPropertyId> _rejected = new();
    public CascadeFilter Add(CssPropertyId id, bool enabled) { if (enabled) _rejected.Add(id); return this; }
    public bool Rejects(CssPropertyName name) => !name.IsCustom && _rejected.Contains(name.Id);
}
