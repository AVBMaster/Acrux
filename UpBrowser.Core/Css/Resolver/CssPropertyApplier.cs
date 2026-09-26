using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Css.ElementStyles;
using CurrentColorSlot = UpBrowser.Core.Dom.ComputedStyle.CurrentColorSlot;

namespace UpBrowser.Core.Css.Resolver;

/// <summary>
/// Prism CSS engine: shared, stateless property-value application.
/// Parses a single CSS declaration value onto a ComputedStyle. Used by both the
/// legacy string cascade and the property-id cascade so all engines share one
/// implementation of every supported property.
/// </summary>
public static class CssPropertyApplier
{
    /// <summary>Scrollbar-color &lt;thumb&gt; &lt;track&gt; ("auto" resets a slot).</summary>
    public static void ApplyScrollbarColors(ComputedStyle style, string value)
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

    public static void Apply(ComputedStyle style, string name, string value)
    {
    try
    {
        switch (name)
        {
            case "width": style.Width = Length.Parse(value); break;
            case "height": style.Height = Length.Parse(value); break;
            case "min-width": style.MinWidth = Length.Parse(value); break;
            case "min-height": style.MinHeight = Length.Parse(value); break;
            case "max-width": style.MaxWidth = Length.Parse(value); break;
            case "max-height": style.MaxHeight = Length.Parse(value); break;
            // Logical sizing properties map to the physical ones for the
            // horizontal writing modes the engine supports (CSS Logical §1.2).
            case "inline-size": style.Width = Length.Parse(value); break;
            case "block-size": style.Height = Length.Parse(value); break;
            case "min-inline-size": style.MinWidth = Length.Parse(value); break;
            case "min-block-size": style.MinHeight = Length.Parse(value); break;
            case "max-inline-size": style.MaxWidth = Length.Parse(value); break;
            case "max-block-size": style.MaxHeight = Length.Parse(value); break;
            case "display": style.Display = ParseDisplay(value); break;
            case "position": style.Position = ParsePosition(value); break;
            case "float": style.Float = ParseFloat(value); break;
            case "clear": style.Clear = ParseClear(value); break;
            case "margin":
                ParseShorthand4(value, out var mt, out var mr, out var mb, out var ml);
                style.MarginTop = mt; style.MarginRight = mr; style.MarginBottom = mb; style.MarginLeft = ml;
                break;
            case "margin-top": style.MarginTop = Length.Parse(value); break;
            case "margin-bottom": style.MarginBottom = Length.Parse(value); break;
            case "margin-left": style.MarginLeft = Length.Parse(value); break;
            case "margin-right": style.MarginRight = Length.Parse(value); break;
            case "margin-block": ParseShorthand2(value, out var mbt, out var mbb); style.MarginTop = mbt; style.MarginBottom = mbb; break;
            case "margin-inline": ParseShorthand2(value, out var mil, out var mir); style.MarginLeft = mil; style.MarginRight = mir; break;
            case "margin-block-start": style.MarginTop = Length.Parse(value); break;
            case "margin-block-end": style.MarginBottom = Length.Parse(value); break;
            case "margin-inline-start": style.MarginLeft = Length.Parse(value); break;
            case "margin-inline-end": style.MarginRight = Length.Parse(value); break;
            case "padding":
                ParseShorthand4(value, out var pt, out var pr, out var pb, out var pl);
                style.PaddingTop = pt; style.PaddingRight = pr; style.PaddingBottom = pb; style.PaddingLeft = pl;
                break;
            case "padding-top": style.PaddingTop = Length.Parse(value); break;
            case "padding-bottom": style.PaddingBottom = Length.Parse(value); break;
            case "padding-left": style.PaddingLeft = Length.Parse(value); break;
            case "padding-right": style.PaddingRight = Length.Parse(value); break;
            case "padding-block": ParseShorthand2(value, out var pbt, out var pbb); style.PaddingTop = pbt; style.PaddingBottom = pbb; break;
            case "padding-inline": ParseShorthand2(value, out var pil, out var pir); style.PaddingLeft = pil; style.PaddingRight = pir; break;
            case "padding-block-start": style.PaddingTop = Length.Parse(value); break;
            case "padding-block-end": style.PaddingBottom = Length.Parse(value); break;
            case "padding-inline-start": style.PaddingLeft = Length.Parse(value); break;
            case "padding-inline-end": style.PaddingRight = Length.Parse(value); break;
            case "color": style.Color = ColorParser.Parse(value); break;
            case "accent-color": style.AccentColor = value == "auto" ? null : ColorParser.Parse(value); break;
            case "caret-color": style.CaretColor = value == "auto" ? null : ColorParser.Parse(value); MarkCurrentColor(style, CurrentColorSlot.Caret, value); break;

            // Standard scrollbar properties.
            case "scrollbar-width":
                style.ScrollbarWidth = value.Trim() switch
                {
                    "thin" => ScrollbarWidthType.Thin,
                    "none" => ScrollbarWidthType.None,
                    _ => ScrollbarWidthType.Auto,
                };
                break;
            case "scrollbar-color":
                ApplyScrollbarColors(style, value);
                break;
            case "color-scheme":
                var cs = value.ToLowerInvariant();
                style.ColorScheme = cs switch { "light" => "light", "dark" => "dark", "light dark" => "light dark", _ => "normal" };
                break;
            case "appearance": case "-webkit-appearance": style.Appearance = value.ToLowerInvariant(); break;
            case "forced-color-adjust": style.ForcedColorAdjust = value.ToLowerInvariant() == "none" ? ForcedColorAdjustType.None : ForcedColorAdjustType.Auto; break;
            case "background": ParseBackgroundShorthand(value, style); break;
            case "background-color": style.BackgroundColor = ColorParser.Parse(value); break;
            case "background-image":
                if (value == "none")
                    style.BackgroundImage = null;
                else
                    style.BackgroundImage = SplitCommaOutsideParens(value).Select(s => s.Trim()).ToList();
                break;
            case "background-repeat": style.BackgroundRepeat = ParseBackgroundRepeat(value); break;
            case "background-position": ParseBackgroundPosition(value, style); break;
            case "background-position-x": style.BackgroundPositionX = ParsePositionKeywordOrLength(value); break;
            case "background-position-y": style.BackgroundPositionY = ParsePositionKeywordOrLength(value); break;
            case "background-size": ParseBackgroundSize(value, style); break;
            case "background-attachment": style.BackgroundAttachment = ParseBackgroundAttachment(value); break;
            case "background-clip": style.BackgroundClip = value.ToLowerInvariant(); break;
            case "background-origin": style.BackgroundOrigin = value.ToLowerInvariant(); break;
            case "background-blend-mode": style.BackgroundBlendMode = ParseBackgroundBlendMode(value); break;
            case "text-align": style.TextAlign = ParseTextAlign(value); break;
            case "text-align-last": style.TextAlignLast = ParseTextAlignLast(value); break;
            case "text-decoration": ParseTextDecorationShorthand(value, style); break;
            case "text-decoration-line":
                style.TextDecorationLine = ParseTextDecorationLine(value);
                style.TextDecoration = style.TextDecorationLine switch
                {
                    TextDecorationLineType.Underline => TextDecorationType.Underline,
                    TextDecorationLineType.LineThrough => TextDecorationType.LineThrough,
                    TextDecorationLineType.Overline => TextDecorationType.Overline,
                    _ => TextDecorationType.None
                };
                break;
            case "text-decoration-style": style.TextDecorationStyle = ParseTextDecorationStyle(value); break;
            case "text-decoration-color": style.TextDecorationColor = ColorParser.Parse(value); MarkCurrentColor(style, CurrentColorSlot.TextDecoration, value); break;
            case "text-decoration-thickness":
                if (value == "auto") style.TextDecorationThickness = 0;
                else if (Length.TryParse(value, out var tdt)) style.TextDecorationThickness = tdt.ToPixels(0, 0, 0, 0);
                break;
            case "text-underline-offset":
                if (value == "auto") style.TextUnderlineOffset = 0;
                else if (Length.TryParse(value, out var tuo)) style.TextUnderlineOffset = tuo.ToPixels(0, 0, 0, 0);
                break;
            case "text-emphasis": style.TextEmphasis = value; break;
            case "text-emphasis-color": style.TextEmphasisColor = value; break;
            case "text-emphasis-style": style.TextEmphasisStyle = value; break;
            case "text-emphasis-position": style.TextEmphasisPosition = value; break;
            case "text-shadow": style.TextShadow = ParseTextShadow(value); break;
            case "text-overflow": style.TextOverflow = value.ToLowerInvariant() == "ellipsis" ? TextOverflowType.Ellipsis : TextOverflowType.Clip; break;
            case "vertical-align": ApplyVerticalAlign(style, value); break;
            case "white-space": style.WhiteSpace = ParseWhiteSpace(value); break;
            case "word-break": style.WordBreak = ParseWordBreak(value); break;
            case "overflow-wrap": case "word-wrap": style.OverflowWrap = ParseOverflowWrap(value); break;
            case "visibility": style.Visibility = ParseVisibility(value); break;
            case "overflow":
                var overflow = ParseOverflow(value);
                style.Overflow = overflow; style.OverflowX = overflow; style.OverflowY = overflow;
                break;
            case "overflow-x": style.OverflowX = ParseOverflow(value); break;
            case "overflow-y": style.OverflowY = ParseOverflow(value); break;
            case "overflow-anchor": style.OverflowAnchor = value.ToLowerInvariant() == "none" ? OverflowAnchorType.None : OverflowAnchorType.Auto; break;
            case "overscroll-behavior": style.OverscrollBehavior = ParseOverscrollBehavior(value); style.OverscrollBehaviorX = style.OverscrollBehavior; style.OverscrollBehaviorY = style.OverscrollBehavior; break;
            case "overscroll-behavior-x": style.OverscrollBehaviorX = ParseOverscrollBehavior(value); break;
            case "overscroll-behavior-y": style.OverscrollBehaviorY = ParseOverscrollBehavior(value); break;
            case "z-index": if (value != "auto") style.ZIndex = int.TryParse(value, out var z) ? z : null; break;
            case "border": ParseBorderShorthand(value, style); break;
            case "border-top": ParseBorderSide(style, "top", value); break;
            case "border-bottom": ParseBorderSide(style, "bottom", value); break;
            case "border-left": ParseBorderSide(style, "left", value); break;
            case "border-right": ParseBorderSide(style, "right", value); break;
            case "border-block-start": ParseBorderSide(style, "top", value); break;
            case "border-block-end": ParseBorderSide(style, "bottom", value); break;
            case "border-inline-start": ParseBorderSide(style, "left", value); break;
            case "border-inline-end": ParseBorderSide(style, "right", value); break;
            case "border-width": ParseBorderWidth(value, style); break;
            case "border-color": ParseBorderColor(value, style); break;
            case "border-style": ParseBorderStyle(value, style); break;
            case "border-top-width": style.BorderTopWidth = ParseSize(value) ?? 0; break;
            case "border-right-width": style.BorderRightWidth = ParseSize(value) ?? 0; break;
            case "border-bottom-width": style.BorderBottomWidth = ParseSize(value) ?? 0; break;
            case "border-left-width": style.BorderLeftWidth = ParseSize(value) ?? 0; break;
            case "border-top-style": style.BorderTopStyle = ParseBorderStyleValue(value); break;
            case "border-right-style": style.BorderRightStyle = ParseBorderStyleValue(value); break;
            case "border-bottom-style": style.BorderBottomStyle = ParseBorderStyleValue(value); break;
            case "border-left-style": style.BorderLeftStyle = ParseBorderStyleValue(value); break;
            case "border-top-color": style.BorderTopColor = ColorParser.Parse(value); MarkCurrentColor(style, CurrentColorSlot.BorderTop, value); break;
            case "border-right-color": style.BorderRightColor = ColorParser.Parse(value); MarkCurrentColor(style, CurrentColorSlot.BorderRight, value); break;
            case "border-bottom-color": style.BorderBottomColor = ColorParser.Parse(value); MarkCurrentColor(style, CurrentColorSlot.BorderBottom, value); break;
            case "border-left-color": style.BorderLeftColor = ColorParser.Parse(value); MarkCurrentColor(style, CurrentColorSlot.BorderLeft, value); break;
            case "border-radius": ParseBorderRadius(value, style); break;
            case "border-top-left-radius": style.BorderTopLeftRadius = ParseRadiusValue(value) ?? 0; break;
            case "border-top-right-radius": style.BorderTopRightRadius = ParseRadiusValue(value) ?? 0; break;
            case "border-bottom-left-radius": style.BorderBottomLeftRadius = ParseRadiusValue(value) ?? 0; break;
            case "border-bottom-right-radius": style.BorderBottomRightRadius = ParseRadiusValue(value) ?? 0; break;
            case "border-collapse": style.BorderCollapse = value.ToLowerInvariant() == "collapse"; break;
            case "border-spacing": style.BorderSpacing = ParseSize(value) ?? 0; break;
            case "border-image": ParseBorderImageShorthand(value, style); break;
            case "border-image-source": style.BorderImageSource = ParseUrl(value); break;
            case "border-image-slice": style.BorderImageSlice = value; break;
            case "border-image-width": style.BorderImageWidth = value; break;
            case "border-image-repeat": style.BorderImageRepeat = value; break;
            case "border-image-outset": style.BorderImageOutset = value; break;
            case "box-sizing": style.BoxSizing = value.Contains("border") ? BoxSizingType.BorderBox : BoxSizingType.ContentBox; style.BoxSizingIsAuthored = true; break;
            case "opacity": if (float.TryParse(value, out var o)) style.Opacity = Math.Clamp(o, 0, 1); break;
            case "box-shadow": style.BoxShadow = ParseBoxShadow(value); break;
            case "flex-direction": style.FlexDirection = ParseFlexDirection(value); break;
            case "flex-wrap": style.FlexWrap = ParseFlexWrap(value); break;
            case "flex-grow": if (float.TryParse(value, out var g)) style.FlexGrow = g; break;
            case "flex-shrink": if (float.TryParse(value, out var s)) style.FlexShrink = s; break;
            case "flex-basis": style.FlexBasis = Length.Parse(value); break;
            case "flex": ParseFlexShorthand(value, style); break;
            case "flex-flow": style.FlexFlow = value; ParseFlexFlow(value, style); break;
            case "order": if (int.TryParse(value, out var ord)) style.Order = ord; break;
            case "justify-content": style.JustifyContent = ParseJustifyContent(value); break;
            case "justify-items": style.JustifyItems = value.ToLowerInvariant(); break;
            case "justify-self": style.JustifySelf = value.ToLowerInvariant(); break;
            case "align-items": style.AlignItems = ParseAlignItems(value); break;
            case "align-self": style.AlignSelf = ParseAlignSelf(value); break;
            case "align-content": style.AlignContent = value.ToLowerInvariant(); break;
            case "place-content":
                style.PlaceContent = value.ToLowerInvariant();
                ApplyPlace(value, v => style.AlignContent = v, v => style.JustifyContent = ParseJustifyContent(v));
                break;
            case "place-items":
                style.PlaceItems = value.ToLowerInvariant();
                ApplyPlace(value, v => style.AlignItems = ParseAlignItems(v), v => style.JustifyItems = v);
                break;
            case "place-self":
                style.PlaceSelf = value.ToLowerInvariant();
                ApplyPlace(value, v => style.AlignSelf = ParseAlignSelf(v), v => style.JustifySelf = v);
                break;
            case "gap": ParseGap(value, style); break;
            case "row-gap": if (Length.TryParse(value, out var rg)) style.RowGap = rg; break;
            case "column-gap": if (Length.TryParse(value, out var cg)) style.ColumnGap = cg; break;
            case "column-count": if (int.TryParse(value, out var cc)) style.ColumnCount = cc; break;
            case "column-span":
                style.ColumnSpanAll = value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase);
                break;
            // CSS Multi-Column 1 §3.5: 'columns' is the shorthand of column-width
            // and column-count; either component may be omitted ('auto' resets it).
            case "columns": ParseColumns(style, value); break;
            case "column-width": if (Length.TryParse(value, out var cw)) style.ColumnWidth = cw; break;

            // A5: column-rule 鈥?the multicol separator line.
            case "column-rule": ParseColumnRule(value, style); break;
            case "column-rule-width":
                if (value.Trim() is "thin") style.ColumnRuleWidth = 1f;
                else if (value.Trim() is "medium" or "auto") style.ColumnRuleWidth = 3f;
                else if (value.Trim() is "thick") style.ColumnRuleWidth = 5f;
                else style.ColumnRuleWidth = ParseSize(value) ?? 3f;
                break;
            case "column-rule-style": style.ColumnRuleStyle = ParseBorderStyleValue(value); break;
            case "column-rule-color": style.ColumnRuleColor = ColorParser.Parse(value); MarkCurrentColor(style, CurrentColorSlot.ColumnRule, value); break;
            case "grid": style.Grid = value; break;
            case "grid-template": ParseGridTemplateShorthand(value, style); break;
            case "grid-template-columns": style.GridTemplateColumns = value == "none" ? null : value; break;
            case "grid-template-rows": style.GridTemplateRows = value == "none" ? null : value; break;
            case "grid-template-areas": style.GridTemplateAreas = value == "none" ? null : value; break;
            case "grid-auto-columns": style.GridAutoColumns = value; break;
            case "grid-auto-rows": style.GridAutoRows = value; break;
            case "grid-auto-flow": style.GridAutoFlow = ParseGridAutoFlow(value); break;
            case "grid-column": style.GridColumn = value; if (value.Contains("/")) { var parts = value.Split('/'); style.GridColumnStart = parts[0].Trim(); style.GridColumnEnd = parts.Length > 1 ? parts[1].Trim() : null; } break;
            case "grid-column-start": style.GridColumnStart = value; break;
            case "grid-column-end": style.GridColumnEnd = value; break;
            case "grid-row": style.GridRow = value; if (value.Contains("/")) { var parts = value.Split('/'); style.GridRowStart = parts[0].Trim(); style.GridRowEnd = parts.Length > 1 ? parts[1].Trim() : null; } break;
            case "grid-row-start": style.GridRowStart = value; break;
            case "grid-row-end": style.GridRowEnd = value; break;
            case "grid-area": style.GridArea = value; break;
            case "top": style.Top = Length.Parse(value); break;
            case "bottom": style.Bottom = Length.Parse(value); break;
            case "left": style.Left = Length.Parse(value); break;
            case "right": style.Right = Length.Parse(value); break;
            case "inset": ParseInsetShorthand(value, style); break;
            case "inset-block": ParseShorthand2(value, out var ibt, out var ibb); style.Top = ibt; style.Bottom = ibb; break;
            case "inset-inline": ParseShorthand2(value, out var iil, out var iir); style.Left = iil; style.Right = iir; break;
            case "inset-block-start": style.Top = Length.Parse(value); break;
            case "inset-block-end": style.Bottom = Length.Parse(value); break;
            case "inset-inline-start": style.Left = Length.Parse(value); break;
            case "inset-inline-end": style.Right = Length.Parse(value); break;
            case "list-style-type": style.ListStyleType = ParseListStyleType(value, style); break;
            case "list-style-position": style.ListStylePosition = value.Contains("inside") ? ListStylePosition.Inside : ListStylePosition.Outside; break;
            case "list-style-image": style.ListStyleImage = value == "none" ? null : ParseUrl(value); break;
            case "list-style": ParseListStyle(value, style); break;
            case "cursor": style.Cursor = value; break;
            case "transform": style.Transform = value; break;
            case "transform-origin": style.TransformOrigin = value; break;
            // Independent transform properties (CSS Transforms 2 §3): stored raw
            // and composed with 'transform' at paint time by ComputedStyle.
            case "translate": style.Translate = value; break;
            case "rotate": style.Rotate = value; break;
            case "scale": style.Scale = value; break;
            case "transition": style.Transition = value; break;
            case "transition-delay": style.TransitionDelay = value; break;
            case "transition-duration": style.TransitionDuration = value; break;
            case "transition-property": style.TransitionProperty = value; break;
            case "transition-timing-function": style.TransitionTimingFunction = value; break;
            case "animation": style.Animation = value; break;
            case "animation-name": style.AnimationName = value; break;
            case "animation-duration": style.AnimationDuration = value; break;
            case "animation-timing-function": style.AnimationTimingFunction = value; break;
            case "animation-delay": style.AnimationDelay = value; break;
            case "animation-iteration-count": style.AnimationIterationCount = value; break;
            case "animation-direction": style.AnimationDirection = value; break;
            case "animation-fill-mode": style.AnimationFillMode = value; break;
            case "animation-play-state": style.AnimationPlayState = value; break;
            case "pointer-events": style.PointerEvents = value; break;
            case "user-select": style.UserSelect = value; break;
            case "text-indent":
                {
                    // [ each-line || hanging ] <length>  (CSS Text 3 §5.2)
                    var indentTokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    style.TextIndentHanging = false;
                    var rest = new List<string>();
                    foreach (var token in indentTokens)
                    {
                        if (token.Equals("hanging", StringComparison.OrdinalIgnoreCase))
                            style.TextIndentHanging = true;
                        else if (token.Equals("each-line", StringComparison.OrdinalIgnoreCase))
                            continue;
                        else
                            rest.Add(token);
                    }
                    if (Length.TryParse(string.Join(" ", rest), out var ti))
                    {
                        if (ti is PercentLength tip)
                        {
                            style.TextIndent = 0;
                            style.TextIndentPercent = tip.Value;
                        }
                        else
                        {
                            style.TextIndent = ti.ToPixels(0, 0, 0, 0);
                            style.TextIndentPercent = 0;
                        }
                    }
                }
                break;
            case "letter-spacing":
                if (value == "normal") style.LetterSpacing = 0;
                else if (Length.TryParse(value, out var ls))
                    style.LetterSpacing = ls.ToPixels(0, 0, 0, 0);
                break;
            case "word-spacing":
                if (value == "normal") style.WordSpacing = 0;
                else if (Length.TryParse(value, out var ws))
                    style.WordSpacing = ws.ToPixels(0, 0, 0, 0);
                break;
            case "direction": style.Direction = value.ToLowerInvariant() == "rtl" ? "rtl" : "ltr"; break;
            case "unicode-bidi": style.UnicodeBidi = value.ToLowerInvariant(); break;
            case "writing-mode": style.WritingMode = ParseWritingMode(value); break;
            case "text-orientation": break; // recognized but minimal handling
            case "text-transform": style.TextTransform = value.ToLowerInvariant(); break;
            case "text-rendering": style.TextRendering = value.ToLowerInvariant(); break;
            case "font":
                ParseFontShorthand(value, style);
                break;
            case "font-family": style.FontFamily = ParseFontFamily(value); break;
            case "font-size": break; // handled in high-priority
            case "font-weight": break; // handled in high-priority
            case "font-style": break; // handled in high-priority
            case "line-height": break; // handled in high-priority
            case "font-variant": style.FontVariant = value.ToLowerInvariant(); break;
            case "font-stretch": style.FontStretch = value.ToLowerInvariant(); break;
            case "font-kerning": style.FontKerning = value.ToLowerInvariant(); break;
            case "font-synthesis": style.FontSynthesis = value.ToLowerInvariant(); break;
            case "font-optical-sizing": style.FontOpticalSizing = value.ToLowerInvariant(); break;
            case "font-variation-settings": style.FontVariationSettings = value; break;
            case "font-feature-settings": style.FontFeatureSettings = value; break;
            case "font-size-adjust": if (value != "none" && float.TryParse(value, out var fsa)) style.FontSizeAdjust = fsa; break;
            case "outline": ParseOutlineShorthand(value, style); break;
            case "outline-width":
                if (float.TryParse(value.Replace("px", ""), out var ow))
                    style.OutlineWidth = ow;
                break;
            case "outline-color": style.OutlineColor = ColorParser.Parse(value); MarkCurrentColor(style, CurrentColorSlot.Outline, value); break;
            case "outline-style": style.OutlineStyle = ParseBorderStyleValue(value); break;
            case "outline-offset": style.OutlineOffset = ParseSize(value) ?? 0; break;
            case "table-layout": style.TableLayout = value.ToLowerInvariant() == "fixed" ? "fixed" : "auto"; break;
            case "caption-side": style.CaptionSide = value.ToLowerInvariant() == "bottom" ? "bottom" : "top"; break;
            case "empty-cells": style.EmptyCells = value.ToLowerInvariant() == "hide" ? "hide" : "show"; break;
            case "content": style.Content = value; break;
            case "counter-increment": style.CounterIncrement = value; break;
            case "counter-reset": style.CounterReset = value; break;
            case "counter-set": style.CounterSet = value; break;
            case "quotes": style.Quotes = value; break;
            case "aspect-ratio":
                if (value == "auto") style.AspectRatio = 0;
                else if (value.Contains('/'))
                {
                    var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && float.TryParse(parts[0], out var aw) && float.TryParse(parts[1], out var ah) && ah > 0)
                        style.AspectRatio = aw / ah;
                }
                else if (float.TryParse(value, out var ar)) style.AspectRatio = ar;
                break;
            case "object-fit": style.ObjectFit = ParseObjectFit(value); break;
            case "object-position": ParsePosition(value, out var opx, out var opy); style.ObjectPositionX = opx; style.ObjectPositionY = opy; break;
            case "filter": style.Filter = value; break;
            case "backdrop-filter": style.BackdropFilter = value; break;
            case "clip-path": style.ClipPath = value; break;
            case "mask": style.Mask = value; break;
            case "mask-image": style.MaskImage = value; break;
            case "mask-clip": style.MaskClip = value; break;
            case "mask-composite": style.MaskComposite = value; break;
            case "mask-mode": style.MaskMode = value; break;
            case "mask-origin": style.MaskOrigin = value; break;
            case "mask-position": style.MaskPosition = value; break;
            case "mask-repeat": style.MaskRepeat = value; break;
            case "mask-size": style.MaskSize = value; break;
            case "isolation": style.Isolation = value.ToLowerInvariant() == "isolate" ? IsolationType.Isolate : IsolationType.Auto; break;
            case "mix-blend-mode": style.MixBlendMode = ParseMixBlendMode(value); break;
            case "image-rendering": style.ImageRendering = ParseImageRendering(value); break;
            case "contain": style.Contain = ParseContain(value); break;
            case "content-visibility": style.ContentVisibility = ParseContentVisibility(value); break;
            case "will-change": style.WillChange = value; break;
            case "scroll-behavior": style.ScrollBehavior = value.ToLowerInvariant() == "smooth" ? ScrollBehaviorType.Smooth : ScrollBehaviorType.Auto; break;
            case "tab-size": ParseTabSize(style, value); break;
            case "hyphens": style.Hyphens = ParseHyphens(value); break;
            case "line-break": style.LineBreak = ParseLineBreak(value); break;
            case "text-justify": style.TextJustify = ParseTextJustify(value); break;
            case "hanging-punctuation": style.HangingPunctuation = value.ToLowerInvariant(); break;
            case "resize": style.Resize = ParseResize(value); break;
            case "zoom": style.Zoom = ParseZoom(value); break;
            case "all": break; // all shorthand - handled via reset cascade
            case "initial-letter": break; // recognized, minimal handling
            case "box-decoration-break": break; // recognized, minimal handling
            case "page-break-after": break;
            case "page-break-before": break;
            case "page-break-inside": break;
            case "orphans": break;
            case "widows": break;
        }
    }
    catch (FormatException) { /* Gracefully skip malformed CSS values */ }
    catch (OverflowException) { /* Skip values that are too large/small */ }
    catch (Exception) { /* Catch any other parsing errors */ }
    }


    public static void ParseShorthand4(string value, out Length top, out Length right, out Length bottom, out Length left)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        top = Length.Parse(parts.Length > 0 ? parts[0] : "0");
        right = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
        bottom = Length.Parse(parts.Length > 2 ? parts[2] : parts[0]);
        left = Length.Parse(parts.Length > 3 ? parts[3] : (parts.Length > 1 ? parts[1] : parts[0]));
    }

    /// <summary>
    /// A5: `column-rule: &lt;width&gt; || &lt;style&gt; || &lt;color&gt;` — the multicol
    /// separator line. Same token classification family as border shorthands.
    /// </summary>
    public static void ParseColumnRule(string value, ComputedStyle style)
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
                MarkCurrentColor(style, CurrentColorSlot.ColumnRule, p);
                continue;
            }

            if (float.TryParse(p.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? p[..^2] : p,
                    out var wpx))
                style.ColumnRuleWidth = Math.Max(0, wpx);
        }
    }


    public static float ParseFontSize(string value, ComputedStyle? parentStyle)
    {
        float parentFontSize = parentStyle?.FontSize ?? 16;
        return Length.ParseFontSize(value, parentFontSize);
    }

    public static FontWeight ParseFontWeight(string value) => value.ToLowerInvariant() switch
    {
        "bold" or "bolder" or "500" or "600" or "700" or "800" or "900" => FontWeight.Bold,
        _ => FontWeight.Normal
    };

    public static FontStyleType ParseFontStyle(string value) => value.ToLowerInvariant() switch
    {
        "italic" or "oblique" => FontStyleType.Italic,
        _ => FontStyleType.Normal
    };

    public static string ParseFontFamily(string value)
    {
        // Keep the FULL font-family list (cleaned) so the renderer can fall back
        // through every specified family instead of only the first one. Consumers
        // that need a single family use the first entry (PrimaryFamily /
        // FontFamily.Split(',')[0]).
        var families = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (families.Length == 0) return "Arial, sans-serif";
        return string.Join(",", families.Select(f => f.Trim().Trim('"', '\'')));
    }

    public static float ParseLineHeight(string value, float fontSize)
    {
        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px / fontSize;
        if (float.TryParse(value, out var num)) return num;
        return 1.2f;
    }

    public static DisplayType ParseDisplay(string value) => value.ToLowerInvariant() switch
    {
        "block" => DisplayType.Block,
        "inline" => DisplayType.Inline,
        "inline-block" => DisplayType.InlineBlock,
        "flex" => DisplayType.Flex,
        "inline-flex" => DisplayType.InlineFlex,
        "grid" => DisplayType.Grid,
        "inline-grid" => DisplayType.InlineGrid,
        "list-item" => DisplayType.ListItem,
        "table" => DisplayType.Table,
        // inline-table is laid out as a table box (block-level approximation;
        // the engine has no separate inline-table display type).
        "inline-table" => DisplayType.Table,
        "table-row" => DisplayType.TableRow,
        "table-cell" => DisplayType.TableCell,
        "table-header-group" => DisplayType.TableHeaderGroup,
        "table-row-group" => DisplayType.TableRowGroup,
        "table-footer-group" => DisplayType.TableFooterGroup,
        "table-caption" => DisplayType.TableCaption,
        "table-column-group" => DisplayType.TableColumnGroup,
        "table-column" => DisplayType.TableColumn,
        "none" => DisplayType.None,
        "contents" => DisplayType.Contents,
        _ => DisplayType.Block
    };

    public static PositionType ParsePosition(string value) => value.ToLowerInvariant() switch
    {
        "relative" => PositionType.Relative,
        "absolute" => PositionType.Absolute,
        "fixed" => PositionType.Fixed,
        "sticky" => PositionType.Sticky,
        _ => PositionType.Static
    };

    public static FloatType ParseFloat(string value) => value.ToLowerInvariant() switch
    {
        "left" => FloatType.Left,
        "right" => FloatType.Right,
        _ => FloatType.None
    };

    public static ClearType ParseClear(string value) => value.ToLowerInvariant() switch
    {
        "left" => ClearType.Left,
        "right" => ClearType.Right,
        "both" => ClearType.Both,
        _ => ClearType.None
    };

    public static TextAlignType ParseTextAlign(string value) => value.ToLowerInvariant() switch
    {
        "left" => TextAlignType.Left,
        "right" => TextAlignType.Right,
        "center" => TextAlignType.Center,
        "justify" => TextAlignType.Justify,
        "start" => TextAlignType.Start,
        "end" => TextAlignType.End,
        _ => TextAlignType.Start
    };

    public static TextDecorationType ParseTextDecoration(string value) => value.ToLowerInvariant() switch
    {
        "underline" => TextDecorationType.Underline,
        "overline" => TextDecorationType.Overline,
        "line-through" => TextDecorationType.LineThrough,
        "none" => TextDecorationType.None,
        _ => TextDecorationType.None
    };

    /// <summary>
    /// 'vertical-align' takes the keyword set or a length/percentage (CSS 2.1
    /// §10.8.1). A percentage refers to the element's own line-height, which is
    /// only known at layout time, so it is kept as a fraction and flagged as
    /// Percentage; a length is resolved to pixels here.
    /// </summary>
    public static void ApplyVerticalAlign(ComputedStyle style, string value)
    {
        var token = value.Trim();
        style.VerticalAlignIsAuthored = true;
        var keyword = ParseVerticalAlign(token);
        if (keyword != VerticalAlignType.Baseline
            || token.Equals("baseline", StringComparison.OrdinalIgnoreCase))
        {
            style.VerticalAlign = keyword;
            style.VerticalAlignOffsetPx = null;
            return;
        }

        if (Length.TryParse(token, out var length))
        {
            if (length is PercentLength percent)
            {
                style.VerticalAlign = VerticalAlignType.Percentage;
                style.VerticalAlignOffsetPx = percent.Value;
            }
            else
            {
                style.VerticalAlign = VerticalAlignType.Length;
                style.VerticalAlignOffsetPx = length.ToPixels(style.FontSize, style.FontSize, 0, 0);
            }
        }
    }

    public static VerticalAlignType ParseVerticalAlign(string value) => value.ToLowerInvariant() switch
    {
        "top" => VerticalAlignType.Top,
        "bottom" => VerticalAlignType.Bottom,
        "middle" => VerticalAlignType.Middle,
        "sub" => VerticalAlignType.Sub,
        "super" => VerticalAlignType.Super,
        "text-top" => VerticalAlignType.TextTop,
        "text-bottom" => VerticalAlignType.TextBottom,
        _ => VerticalAlignType.Baseline
    };

    public static WhiteSpaceMode ParseWhiteSpace(string value) => value.ToLowerInvariant() switch
    {
        "nowrap" => WhiteSpaceMode.Nowrap,
        "pre" => WhiteSpaceMode.Pre,
        "pre-wrap" => WhiteSpaceMode.PreWrap,
        "pre-line" => WhiteSpaceMode.PreLine,
        "break-spaces" => WhiteSpaceMode.BreakSpaces,
        _ => WhiteSpaceMode.Normal
    };

    public static WordBreakMode ParseWordBreak(string value) => value.ToLowerInvariant() switch
    {
        "break-all" => WordBreakMode.BreakAll,
        "break-word" => WordBreakMode.BreakWord,
        _ => WordBreakMode.Normal
    };

    public static OverflowWrapMode ParseOverflowWrap(string value) => value.ToLowerInvariant() switch
    {
        "break-word" => OverflowWrapMode.BreakWord,
        "anywhere" => OverflowWrapMode.Anywhere,
        _ => OverflowWrapMode.Normal
    };

    public static VisibilityType ParseVisibility(string value) => value.ToLowerInvariant() switch
    {
        "hidden" => VisibilityType.Hidden,
        "collapse" => VisibilityType.Collapse,
        _ => VisibilityType.Visible
    };

    public static OverflowType ParseOverflow(string value) => value.ToLowerInvariant() switch
    {
        "hidden" => OverflowType.Hidden,
        "scroll" => OverflowType.Scroll,
        "auto" => OverflowType.Auto,
        _ => OverflowType.Visible
    };

    public static BackgroundRepeat ParseBackgroundRepeat(string value)
    {
        var parts = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return SingleRepeat(parts[0]);
        if (parts.Length >= 2)
        {
            var (x, y) = (parts[0], parts[1]);
            if (x == y)
                return SingleRepeat(x);
            if ((x, y) == ("repeat", "no-repeat")) return BackgroundRepeat.RepeatX;
            if ((x, y) == ("no-repeat", "repeat")) return BackgroundRepeat.RepeatY;
            // Mixed one-value axes (e.g. "round no-repeat") keep the x-axis mode
            // rather than dropping tiling entirely.
            return SingleRepeat(x);
        }
        return BackgroundRepeat.Repeat;
    }

    private static BackgroundRepeat SingleRepeat(string keyword) => keyword switch
    {
        "repeat-x" => BackgroundRepeat.RepeatX,
        "repeat-y" => BackgroundRepeat.RepeatY,
        "no-repeat" => BackgroundRepeat.NoRepeat,
        "round" => BackgroundRepeat.Round,
        "space" => BackgroundRepeat.Space,
        _ => BackgroundRepeat.Repeat
    };

    public static BackgroundAttachment ParseBackgroundAttachment(string value) => value.ToLowerInvariant() switch
    {
        "fixed" => BackgroundAttachment.Fixed,
        "local" => BackgroundAttachment.Local,
        _ => BackgroundAttachment.Scroll
    };

    public static void ParseBackgroundPosition(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            style.BackgroundPositionX = parts[0].ToLowerInvariant() switch
            {
                "left" => new PixelLength(0),
                "center" => new PercentLength(0.5f),
                "right" => new PercentLength(1),
                _ => Length.Parse(parts[0])
            };
            style.BackgroundPositionY = parts.Length > 1 ? Length.Parse(parts[1]) : new PixelLength(0);
        }
    }

    public static void ParseBackgroundSize(string value, ComputedStyle style)
    {
        if (value == "cover") { style.BackgroundSize = BackgroundSizeType.Cover; return; }
        if (value == "contain") { style.BackgroundSize = BackgroundSizeType.Contain; return; }
        if (value == "auto") { style.BackgroundSize = BackgroundSizeType.Auto; return; }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0] != "auto")
            style.BackgroundSizeWidth = Length.Parse(parts[0]);
        if (parts.Length > 1 && parts[1] != "auto")
            style.BackgroundSizeHeight = Length.Parse(parts[1]);
        style.BackgroundSize = BackgroundSizeType.Length;
    }

    public static void ParseBackgroundShorthand(string value, ComputedStyle style)
    {
        // Split by commas outside parentheses to get individual layers.
        var layers = SplitCommaOutsideParens(value);
        var images = new List<string>();

        foreach (var layer in layers)
        {
            var trimmed = layer.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Extract gradient/url from the layer before splitting by space.
            string? image = null;
            string remaining = trimmed;

            int gradIdx = FindGradientStart(trimmed);
            if (gradIdx >= 0)
            {
                int end = FindMatchingParenEnd(trimmed, gradIdx);
                if (end > gradIdx)
                {
                    image = trimmed[gradIdx..(end + 1)];
                    remaining = (trimmed[..gradIdx] + " " + trimmed[(end + 1)..]).Trim();
                }
            }
            else
            {
                int urlIdx = trimmed.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
                if (urlIdx >= 0)
                {
                    int end = FindMatchingParenEnd(trimmed, urlIdx + 4);
                    if (end > urlIdx)
                    {
                        image = ParseUrl(trimmed[urlIdx..(end + 1)]);
                        remaining = (trimmed[..urlIdx] + " " + trimmed[(end + 1)..]).Trim();
                    }
                }
            }

            if (image != null)
                images.Add(image);

            // Parse remaining tokens for color, repeat, position, size. The split
            // is parenthesis-aware so functional colors keep their inner spaces.
            var parts = ShorthandExpander.SplitShorthand(remaining);
            var positionTokens = new List<string>();
            foreach (var part in parts)
            {
                var lower = part.ToLowerInvariant();
                if (lower == "none" || lower == "transparent")
                {
                    style.BackgroundColor = SKColors.Transparent;
                }
                else if (ColorParser.LooksLikeColor(part))
                {
                    style.BackgroundColor = ColorParser.Parse(part);
                }
                else if (lower is "repeat" or "repeat-x" or "repeat-y" or "no-repeat" or "round" or "space")
                {
                    style.BackgroundRepeat = ParseBackgroundRepeat(part);
                }
                else if (lower is "scroll" or "fixed" or "local")
                {
                    style.BackgroundAttachment = ParseBackgroundAttachment(part);
                }
                else if (lower is "cover" or "contain")
                {
                    style.BackgroundSize = lower == "cover" ? BackgroundSizeType.Cover : BackgroundSizeType.Contain;
                }
                else if (lower is "left" or "right" or "center" or "top" or "bottom" ||
                         lower.EndsWith("%") || lower.EndsWith("px") || lower.StartsWith("calc("))
                {
                    positionTokens.Add(part);
                }
            }
            if (positionTokens.Count > 0)
                ParseBackgroundPosition(string.Join(" ", positionTokens), style);
        }

        if (images.Count > 0)
            style.BackgroundImage = images;
    }

    private static int FindGradientStart(string s)
    {
        string[] funcs = { "linear-gradient", "radial-gradient", "conic-gradient",
                           "repeating-linear-gradient", "repeating-radial-gradient", "repeating-conic-gradient" };
        int best = -1;
        foreach (var f in funcs)
        {
            int idx = s.IndexOf(f + "(", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (best < 0 || idx < best)) best = idx;
        }
        return best;
    }

    private static int FindMatchingParenEnd(string s, int openPos)
    {
        int depth = 0;
        for (int i = openPos; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    public static string? ParseUrl(string value)
    {
        if (value.StartsWith("url("))
            return value[4..].Trim(' ', '"', '\'', ')');
        return null;
    }

    public static void ParseBorderShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge"
                or "inset" or "outset" or "none" or "hidden")
            {
                var bs = ParseBorderStyleValue(part);
                style.BorderTopStyle = bs; style.BorderRightStyle = bs;
                style.BorderBottomStyle = bs; style.BorderLeftStyle = bs;
            }
            else if (part.EndsWith("px"))
            {
                var width = ParseSize(part);
                if (width.HasValue)
                {
                    style.BorderTopWidth = width.Value; style.BorderRightWidth = width.Value;
                    style.BorderBottomWidth = width.Value; style.BorderLeftWidth = width.Value;
                }
            }
            else
            {
                var color = ColorParser.Parse(part);
                style.BorderTopColor = color; style.BorderRightColor = color;
                style.BorderBottomColor = color; style.BorderLeftColor = color;
                MarkCurrentColor(style, CurrentColorSlot.AllBorders, part);
            }
        }
    }

    /// <summary>
    /// Parses the <c>border-image</c> shorthand. The value is
    /// <c>source slice? / width? / outset? repeat? fill?</c>, where slice has an
    /// optional trailing <c>fill</c>. Only an <c>url(...)</c> source is
    /// supported; other sources (gradients) are stored as nothing so the
    /// painter falls back to the ordinary border. The unresolved parts are kept
    /// as CSS string fragments so the painter's box-value expansion decides the
    /// final pixel semantics for each edge.
    /// </summary>
    public static void ParseBorderImageShorthand(string value, ComputedStyle style)
    {
        style.BorderImageSource = null;

        var tokens = new List<string>();
        var group = new List<string>();
        int depth = 0;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            group.Add(part);
            depth += part.Count(c => c == '(') - part.Count(c => c == ')');
            if (depth <= 0)
            {
                tokens.Add(string.Join(" ", group));
                group.Clear();
                depth = 0;
            }
        }
        if (group.Count > 0) tokens.Add(string.Join(" ", group));

        var canonical = new List<string>();
        foreach (var token in tokens)
        {
            if (token == "/")
            {
                canonical.Add("/");
            }
            else if (token.Contains('/') && !token.Contains('('))
            {
                var pieces = token.Split('/');
                for (int i = 0; i < pieces.Length; i++)
                {
                    if (pieces[i].Length > 0) canonical.Add(pieces[i]);
                    if (i < pieces.Length - 1) canonical.Add("/");
                }
            }
            else
            {
                canonical.Add(token);
            }
        }

        var slice = new List<string>();
        var width = new List<string>();
        var outset = new List<string>();
        var repeat = new List<string>();
        bool fill = false;
        int boxGroup = 0;
        foreach (var token in canonical)
        {
            if (token == "/") { boxGroup++; continue; }
            var lower = token.ToLowerInvariant();
            if (lower is "stretch" or "repeat" or "round" or "space")
            {
                repeat.Add(lower);
                continue;
            }
            if (lower == "fill") { fill = true; continue; }
            var source = ParseUrl(token);
            if (source != null)
            {
                style.BorderImageSource = source;
                continue;
            }
            if (boxGroup == 0) slice.Add(token);
            else if (boxGroup == 1) width.Add(token);
            else outset.Add(token);
        }

        style.BorderImageSlice = (slice.Count > 0 ? string.Join(" ", slice) : "100%") + (fill ? " fill" : "");
        style.BorderImageWidth = width.Count > 0 ? string.Join(" ", width) : "auto";
        style.BorderImageOutset = outset.Count > 0 ? string.Join(" ", outset) : "0";
        style.BorderImageRepeat = repeat.Count > 0 ? string.Join(" ", repeat) : "stretch";
    }

    /// <summary>Records that a color property was declared as the `currentcolor`
    /// keyword; StyleAdjuster substitutes the computed color after inheritance.</summary>
    internal static void MarkCurrentColor(ComputedStyle style, ComputedStyle.CurrentColorSlot slot, string token)
    {
        if (token.Trim().Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
            style.CurrentColorSlots |= (uint)slot;
        else
            style.CurrentColorSlots &= ~(uint)slot;
    }

    private static TextAlignLastType ParseTextAlignLast(string value) => value.Trim().ToLowerInvariant() switch
    {
        "start" => TextAlignLastType.Start,
        "end" => TextAlignLastType.End,
        "left" => TextAlignLastType.Left,
        "right" => TextAlignLastType.Right,
        "center" => TextAlignLastType.Center,
        "justify" => TextAlignLastType.Justify,
        _ => TextAlignLastType.Auto,
    };

    /// <summary>`place-*` shorthands take `<block-axis> <inline-axis>`, with the
    /// inline-axis value defaulting to the block-axis one (CSS Box Alignment §6).</summary>
    private static void ApplyPlace(string value, Action<string> setBlockAxis, Action<string> setInlineAxis)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        var block = parts[0].ToLowerInvariant();
        var inlineAxis = (parts.Length > 1 ? parts[1] : parts[0]).ToLowerInvariant();
        // `normal`/`stretch` are per-property keywords; only forward real values.
        if (block != "normal") setBlockAxis(block);
        if (inlineAxis != "normal") setInlineAxis(inlineAxis);
    }

    public static void ParseBorderSide(ComputedStyle style, string side, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge"
                or "inset" or "outset" or "none" or "hidden")
            {
                var bs = ParseBorderStyleValue(part);
                if (side == "top") style.BorderTopStyle = bs;
                else if (side == "bottom") style.BorderBottomStyle = bs;
                else if (side == "left") style.BorderLeftStyle = bs;
                else if (side == "right") style.BorderRightStyle = bs;
            }
            else if (part.EndsWith("px"))
            {
                var width = ParseSize(part);
                if (width.HasValue)
                {
                    if (side == "top") style.BorderTopWidth = width.Value;
                    else if (side == "bottom") style.BorderBottomWidth = width.Value;
                    else if (side == "left") style.BorderLeftWidth = width.Value;
                    else if (side == "right") style.BorderRightWidth = width.Value;
                }
            }
            else
            {
                var color = ColorParser.Parse(part);
                var slot = side switch
                {
                    "top" => CurrentColorSlot.BorderTop,
                    "bottom" => CurrentColorSlot.BorderBottom,
                    "left" => CurrentColorSlot.BorderLeft,
                    _ => CurrentColorSlot.BorderRight,
                };
                MarkCurrentColor(style, slot, part);
                if (side == "top") style.BorderTopColor = color;
                else if (side == "bottom") style.BorderBottomColor = color;
                else if (side == "left") style.BorderLeftColor = color;
                else if (side == "right") style.BorderRightColor = color;
            }
        }
    }

    public static void ParseBorderWidth(string value, ComputedStyle style)
    {
        var widths = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var w = widths.Select(v => v switch
        {
            "thin" => 1f,
            "medium" => 3f,
            "thick" => 5f,
            _ => ParseSize(v) ?? 0
        }).ToList();

        style.BorderTopWidth = w.Count > 0 ? w[0] : 0;
        style.BorderRightWidth = w.Count > 1 ? w[1] : w[0];
        style.BorderBottomWidth = w.Count > 2 ? w[2] : w[0];
        style.BorderLeftWidth = w.Count > 3 ? w[3] : (w.Count > 1 ? w[1] : w[0]);
    }

    public static void ParseBorderColor(string value, ComputedStyle style)
    {
        var colors = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var c = colors.Select(ColorParser.Parse).ToList();
        for (int ci = 0; ci < colors.Length && ci < 4; ci++)
        {
            var slot = ci switch
            {
                0 => CurrentColorSlot.BorderTop,
                1 => CurrentColorSlot.BorderRight,
                2 => CurrentColorSlot.BorderBottom,
                _ => CurrentColorSlot.BorderLeft,
            };
            MarkCurrentColor(style, slot, colors[ci]);
        }
        style.BorderTopColor = c.Count > 0 ? c[0] : SKColors.Black;
        style.BorderRightColor = c.Count > 1 ? c[1] : c[0];
        style.BorderBottomColor = c.Count > 2 ? c[2] : c[0];
        style.BorderLeftColor = c.Count > 3 ? c[3] : (c.Count > 1 ? c[1] : c[0]);
    }

    public static void ParseBorderStyle(string value, ComputedStyle style)
    {
        var styles = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = styles.Select(ParseBorderStyleValue).ToList();
        style.BorderTopStyle = s.Count > 0 ? s[0] : BorderStyle.None;
        style.BorderRightStyle = s.Count > 1 ? s[1] : (s.Count > 0 ? s[0] : BorderStyle.None);
        style.BorderBottomStyle = s.Count > 2 ? s[2] : (s.Count > 0 ? s[0] : BorderStyle.None);
        style.BorderLeftStyle = s.Count > 3 ? s[3] : (s.Count > 1 ? s[1] : (s.Count > 0 ? s[0] : BorderStyle.None));
    }

    public static void ParseBorderRadius(string value, ComputedStyle style)
    {
        // Elliptical radii use "horizontal / vertical"; only the horizontal set
        // is kept (percentages are encoded by ParseRadiusValue).
        int slash = value.IndexOf('/');
        if (slash >= 0) value = value[..slash];
        var radii = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var r = radii.Select(v => ParseRadiusValue(v) ?? 0).ToList();
        style.BorderTopLeftRadius = r.Count > 0 ? r[0] : 0;
        style.BorderTopRightRadius = r.Count > 1 ? r[1] : r[0];
        style.BorderBottomRightRadius = r.Count > 2 ? r[2] : r[0];
        style.BorderBottomLeftRadius = r.Count > 3 ? r[3] : (r.Count > 0 ? r[0] : 0);
    }

    public static BorderStyle ParseBorderStyleValue(string value) => value.ToLowerInvariant() switch
    {
        "solid" => BorderStyle.Solid,
        "dashed" => BorderStyle.Dashed,
        "dotted" => BorderStyle.Dotted,
        "double" => BorderStyle.Double,
        "groove" => BorderStyle.Groove,
        "ridge" => BorderStyle.Ridge,
        "inset" => BorderStyle.Inset,
        "outset" => BorderStyle.Outset,
        _ => BorderStyle.None
    };

    public static float? ParseSize(string value)
    {        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px;
        if (value.EndsWith("em") && float.TryParse(value[..^2], out var em)) return em * 16;
        if (value.EndsWith("rem") && float.TryParse(value[..^2], out var rem)) return rem * 16;
        if (value == "0") return 0;
        return null;
    }

    /// <summary>
    /// Parses one corner radius value, which may be a single length or the
    /// 'horizontal vertical' pair produced for elliptical border-radius.
    /// Uses the horizontal radius (the first value). Percentages are stored
    /// negated (e.g. 50% -> -50): they resolve against the box's own dimensions
    /// at paint time, where every existing consumer's "> 0" guard treats the
    /// unresolved value as unrounded.
    /// </summary>
    public static float? ParseRadiusValue(string value)
    {
        value = value.Trim();
        int sp = value.IndexOf(' ');
        if (sp > 0) value = value[..sp].Trim();
        if (value.EndsWith('%') && value.Length > 1 &&
            float.TryParse(value[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return -pct;
        return ParseSize(value);
    }

    public static void ParseFlexShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        if (parts[0] == "none" || parts[0] == "auto")
        {
            style.FlexGrow = 0; style.FlexShrink = 1;
            style.FlexBasis = AutoLength.Instance;
            return;
        }

        int i = 0;
        if (float.TryParse(parts[0], out var g))
        {
            style.FlexGrow = g; i++;
            if (i < parts.Length && float.TryParse(parts[i], out var s))
            { style.FlexShrink = s; i++; }
            // Per CSS spec, when flex-grow is specified as a number and no third value,
            // flex-basis defaults to 0%
            style.FlexBasis = new PercentLength(0);
        }
        else
        {
            style.FlexBasis = Length.Parse(parts[0]); i++;
            if (i < parts.Length && float.TryParse(parts[i], out var g2))
                style.FlexGrow = g2;
        }
    }

    public static FlexDirectionType ParseFlexDirection(string value) => value.ToLowerInvariant() switch
    {
        "row-reverse" => FlexDirectionType.RowReverse,
        "column" => FlexDirectionType.Column,
        "column-reverse" => FlexDirectionType.ColumnReverse,
        _ => FlexDirectionType.Row
    };

    public static FlexWrapType ParseFlexWrap(string value) => value.ToLowerInvariant() switch
    {
        "wrap" => FlexWrapType.Wrap,
        "wrap-reverse" => FlexWrapType.WrapReverse,
        _ => FlexWrapType.NoWrap
    };

    public static JustifyContentType ParseJustifyContent(string value) => value.ToLowerInvariant() switch
    {
        "flex-end" => JustifyContentType.FlexEnd,
        "center" => JustifyContentType.Center,
        "space-between" => JustifyContentType.SpaceBetween,
        "space-around" => JustifyContentType.SpaceAround,
        "space-evenly" => JustifyContentType.SpaceEvenly,
        _ => JustifyContentType.FlexStart
    };

    public static AlignItemsType ParseAlignItems(string value) => value.ToLowerInvariant() switch
    {
        "flex-start" or "start" or "left" => AlignItemsType.FlexStart,
        "flex-end" or "end" or "right" => AlignItemsType.FlexEnd,
        "center" => AlignItemsType.Center,
        "baseline" or "first baseline" => AlignItemsType.Baseline,
        _ => AlignItemsType.Stretch
    };

    public static AlignSelfType ParseAlignSelf(string value) => value.ToLowerInvariant() switch
    {
        "flex-start" or "start" => AlignSelfType.FlexStart,
        "flex-end" or "end" => AlignSelfType.FlexEnd,
        "center" => AlignSelfType.Center,
        "baseline" => AlignSelfType.Baseline,
        "stretch" => AlignSelfType.Stretch,
        _ => AlignSelfType.Auto
    };

    /// <summary>'list-style-type' keywords plus the quoted &lt;string&gt; form
    /// (CSS Lists 3 §5, §11).</summary>
    public static ListStyleType ParseListStyleType(string value, ComputedStyle? style = null)
    {
        string text = value.Trim();
        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
        {
            if (style != null)
                style.ListStyleTypeString = text[1..^1];
            return ListStyleType.String;
        }
        return text.ToLowerInvariant() switch
        {
            "disc" => ListStyleType.Disc,
            "circle" => ListStyleType.Circle,
            "square" => ListStyleType.Square,
            "decimal" => ListStyleType.Decimal,
            "decimal-leading-zero" => ListStyleType.DecimalLeadingZero,
            "lower-roman" => ListStyleType.LowerRoman,
            "upper-roman" => ListStyleType.UpperRoman,
            "lower-alpha" or "lower-latin" => ListStyleType.LowerLatin,
            "upper-alpha" or "upper-latin" => ListStyleType.UpperLatin,
            "lower-greek" => ListStyleType.LowerGreek,
            "upper-greek" => ListStyleType.UpperGreek,
            "armenian" => ListStyleType.Armenian,
            "georgian" => ListStyleType.Georgian,
            "hebrew" => ListStyleType.Hebrew,
            "hiragana" => ListStyleType.Hiragana,
            "katakana" => ListStyleType.Katakana,
            "hiragana-iroha" => ListStyleType.HiraganaIroha,
            "katakana-iroha" => ListStyleType.KatakanaIroha,
            "cjk-decimal" => ListStyleType.CjkDecimal,
            "cjk-ideographic" => ListStyleType.CjkIdeographic,
            "cjk-earthly-branch" => ListStyleType.CjkEarthlyBranch,
            "cjk-heavenly-stem" => ListStyleType.CjkHeavenlyStem,
            "thai" => ListStyleType.Thai,
            "lao" => ListStyleType.Lao,
            "khmer" => ListStyleType.Khmer,
            "myanmar" or "burmese" => ListStyleType.Myanmar,
            "mongolian" => ListStyleType.Mongolian,
            "arabic-indic" => ListStyleType.ArabicIndic,
            "persian" or "urdu" => ListStyleType.Persian,
            "devanagari" => ListStyleType.Devanagari,
            "bengali" => ListStyleType.Bengali,
            "tamil" => ListStyleType.Tamil,
            "telugu" => ListStyleType.Telugu,
            "canadian-aboriginal" => ListStyleType.CanadianAboriginal,
            "symbol" => ListStyleType.Symbol,
            "none" => ListStyleType.None,
            _ => ListStyleType.Disc,
        };
    }

    public static void ParseListStyle(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "inside" or "outside")
                style.ListStylePosition = lower == "inside" ? ListStylePosition.Inside : ListStylePosition.Outside;
            else if (lower == "none")
                style.ListStyleType = ListStyleType.None;
            else if (lower is "disc" or "circle" or "square" or "decimal" or "lower-roman" or "upper-roman")
                style.ListStyleType = ParseListStyleType(part, style);
            else if (lower.StartsWith("url("))
                style.ListStyleImage = ParseUrl(part);
        }
    }

    public static void ParseFontShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int i = 0;

        while (i < parts.Length)
        {
            var lower = parts[i].ToLowerInvariant();
            if (lower is "normal" or "italic" or "oblique")
            {
                if (lower == "italic" || lower == "oblique") style.FontStyle = FontStyleType.Italic;
                i++;
            }
            else if (lower is "bold" or "bolder" or "lighter" ||
                     lower is "100" or "200" or "300" or "400" or "500" or "600" or "700" or "800" or "900")
            {
                style.FontWeight = ParseFontWeight(parts[i]);
                i++;
            }
            else break;
        }

        if (i < parts.Length && (parts[i].EndsWith("px") || parts[i].EndsWith("em") || parts[i].EndsWith("rem") ||
            parts[i].EndsWith("%") || parts[i] is "xx-small" or "x-small" or "small" or "medium" or
            "large" or "x-large" or "xx-large"))
        {
            style.FontSize = Length.ParseFontSize(parts[i], style.FontSize);
            i++;
        }

        if (i < parts.Length && parts[i] == "/")
        {
            i++;
            if (i < parts.Length)
                Fonts.LineBoxMetrics.ApplyLineHeight(style, parts[i]);
            i++;
        }

        if (i < parts.Length)
        {
            var family = string.Join(" ", parts.Skip(i));
            style.FontFamily = family.Trim().Trim('"', '\'');
        }
    }

    public static void ParseOutlineShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.EndsWith("px"))
            {
                if (float.TryParse(part.Replace("px", ""), out var w))
                    style.OutlineWidth = w;
            }
            else if (part is "solid" or "dashed" or "dotted" or "double" or "none")
            {
                style.OutlineStyle = ParseBorderStyleValue(part);
            }
            else
            {
                style.OutlineColor = ColorParser.Parse(part);
                MarkCurrentColor(style, CurrentColorSlot.Outline, part);
            }
        }
    }

    public static void ParseShorthand2(string value, out Length a, out Length b)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        a = Length.Parse(parts.Length > 0 ? parts[0] : "0");
        b = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
    }

    public static Length? ParsePositionKeywordOrLength(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "left" or "top" => new PixelLength(0),
            "center" => new PercentLength(0.5f),
            "right" or "bottom" => new PercentLength(1),
            _ => Length.TryParse(value, out var l) ? l : null
        };
    }

    public static void ParsePosition(string value, out Length? x, out Length? y)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        x = parts.Length > 0 ? ParsePositionKeywordOrLength(parts[0]) : null;
        y = parts.Length > 1 ? ParsePositionKeywordOrLength(parts[1]) : null;
    }

    public static void ParseInsetShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        style.Top = Length.Parse(parts[0]);
        style.Right = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
        style.Bottom = Length.Parse(parts.Length > 2 ? parts[2] : parts[0]);
        style.Left = Length.Parse(parts.Length > 3 ? parts[3] : (parts.Length > 1 ? parts[1] : parts[0]));
    }

    public static TextDecorationLineType ParseTextDecorationLine(string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower == "none") return TextDecorationLineType.None;
        var result = TextDecorationLineType.None;
        if (lower.Contains("underline")) result |= TextDecorationLineType.Underline;
        if (lower.Contains("overline")) result |= TextDecorationLineType.Overline;
        if (lower.Contains("line-through")) result |= TextDecorationLineType.LineThrough;
        return result;
    }

    public static TextDecorationStyleType ParseTextDecorationStyle(string value) => value.ToLowerInvariant() switch
    {
        "double" => TextDecorationStyleType.Double,
        "dotted" => TextDecorationStyleType.Dotted,
        "dashed" => TextDecorationStyleType.Dashed,
        "wavy" => TextDecorationStyleType.Wavy,
        _ => TextDecorationStyleType.Solid
    };

    public static void ParseTextDecorationShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower == "none" || lower == "underline" || lower == "overline" || lower == "line-through")
            {
                style.TextDecorationLine = ParseTextDecorationLine(part);
                style.TextDecoration = style.TextDecorationLine switch
                {
                    TextDecorationLineType.Underline => TextDecorationType.Underline,
                    TextDecorationLineType.Overline => TextDecorationType.Overline,
                    TextDecorationLineType.LineThrough => TextDecorationType.LineThrough,
                    _ => TextDecorationType.None
                };
            }
            else if (lower == "solid" || lower == "double" || lower == "dotted" || lower == "dashed" || lower == "wavy")
                style.TextDecorationStyle = ParseTextDecorationStyle(part);
            else
                style.TextDecorationColor = ColorParser.Parse(part);
        }
    }

    public static List<TextShadowValue> ParseTextShadow(string value)
    {
        var shadows = new List<TextShadowValue>();
        if (string.IsNullOrEmpty(value) || value == "none") return shadows;

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return shadows;

        float offsetX = float.TryParse(parts[0].TrimEnd('p', 'x'), out var ox) ? ox : 0;
        float offsetY = float.TryParse(parts[1].TrimEnd('p', 'x'), out var oy) ? oy : 0;
        float blurRadius = 0;
        int index = 2;
        // Third length (blur) may be unitless ("0") or carry a unit.
        if (index < parts.Length &&
            (parts[index].Contains("px") || float.TryParse(parts[index], out _)))
        {
            float.TryParse(parts[index].TrimEnd('p', 'x'), out blurRadius);
            index++;
        }
        var color = index < parts.Length ? ColorParser.Parse(string.Join(" ", parts.Skip(index))) : new SKColor(0, 0, 0, 255);
        shadows.Add(new TextShadowValue(color, offsetX, offsetY, blurRadius));
        return shadows;
    }

    public static ObjectFitType ParseObjectFit(string value) => value.ToLowerInvariant() switch
    {
        "contain" => ObjectFitType.Contain,
        "cover" => ObjectFitType.Cover,
        "none" => ObjectFitType.None,
        "scale-down" => ObjectFitType.ScaleDown,
        _ => ObjectFitType.Fill
    };

    /// <summary>CSS Text 3 §3.4: a unitless 'tab-size' counts space advances of the
    /// element's primary font; a length value is used as-is.</summary>
    private static void ParseColumns(ComputedStyle style, string value)
    {
        style.ColumnCount = 0;
        style.ColumnWidth = null;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = token.Trim();
            if (part.Equals("auto", StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(part, out int count) && count > 0)
            {
                style.ColumnCount = count;
                continue;
            }
            var length = Length.Parse(part);
            if (length != null)
                style.ColumnWidth = length;
        }
    }

    private static void ParseTabSize(ComputedStyle style, string value)
    {
        string text = value.Trim();
        bool isLength = false;
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            isLength = true;
            text = text[..^2];
        }
        else if (text.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
        {
            isLength = true;
            text = text[..^3];
        }
        else if (text.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            isLength = true;
            text = text[..^2];
        }
        if (!float.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float size) || size < 0)
            return;
        if (isLength)
        {
            style.TabSizePx = size;
            style.TabSize = 8;
        }
        else
        {
            style.TabSize = size;
            style.TabSizePx = null;
        }
    }

    public static GridAutoFlowType ParseGridAutoFlow(string value)
    {
        var lower = value.ToLowerInvariant();
        bool column = lower.Contains("column");
        bool dense = lower.Contains("dense");
        if (column) return dense ? GridAutoFlowType.ColumnDense : GridAutoFlowType.Column;
        if (dense) return GridAutoFlowType.Dense;
        return GridAutoFlowType.Row;
    }

    public static void ParseGridTemplateShorthand(string value, ComputedStyle style)
    {
        if (value.Trim() == "none")
        {
            style.GridTemplateRows = null;
            style.GridTemplateColumns = null;
            style.GridTemplateAreas = null;
            return;
        }

        // Split on the top-level '/' — left = rows (+ named areas), right = columns.
        int slash = -1;
        int depth = 0;
        bool inQuote = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '"') inQuote = !inQuote;
            else if (!inQuote && c == '(') depth++;
            else if (!inQuote && c == ')') depth--;
            else if (!inQuote && depth == 0 && c == '/') { slash = i; break; }
        }
        string left = (slash >= 0 ? value[..slash] : value).Trim();
        string right = slash >= 0 ? value[(slash + 1)..].Trim() : "";

        // A leading string token means the row list is written as named-area rows:
        //   "head head" 20px "side main" 1fr
        // Each quoted string is one row; the bare tokens after it are that row's
        // track size. Also feed the area rows to grid-template-areas.
        if (left.Contains('"'))
        {
            var areaRows = new List<string>();
            var rowSizes = new List<string>();
            int j = 0;
            while (j < left.Length)
            {
                while (j < left.Length && char.IsWhiteSpace(left[j])) j++;
                if (j >= left.Length) break;
                if (left[j] == '"')
                {
                    int end = left.IndexOf('"', j + 1);
                    if (end < 0) break;
                    string row = left[(j + 1)..end].Trim();
                    areaRows.Add(row);
                    j = end + 1;
                    // Trailing size token for this row (optional).
                    int k = j;
                    while (k < left.Length && char.IsWhiteSpace(left[k])) k++;
                    int start = k;
                    while (k < left.Length && left[k] != '"') k++;
                    string size = left[start..k].Trim();
                    rowSizes.Add(string.IsNullOrEmpty(size) ? "auto" : size);
                    j = k;
                }
                else
                {
                    // Bare track tokens mixed in (rare); collect until next string.
                    int start = j;
                    while (j < left.Length && left[j] != '"') j++;
                    string extra = left[start..j].Trim();
                    if (extra.Length > 0)
                        rowSizes.Add(extra);
                }
            }
            style.GridTemplateAreas = string.Join(",", areaRows);
            style.GridTemplateRows = string.Join(" ", rowSizes);
        }
        else
        {
            style.GridTemplateRows = left.Length > 0 ? left : null;
        }

        if (slash >= 0)
            style.GridTemplateColumns = right.Length > 0 ? right : "none";
    }

    public static void ParseFlexFlow(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "row" or "row-reverse" or "column" or "column-reverse")
                style.FlexDirection = ParseFlexDirection(part);
            else if (lower is "nowrap" or "wrap" or "wrap-reverse")
                style.FlexWrap = ParseFlexWrap(part);
        }
    }

    public static WritingModeType ParseWritingMode(string value) => value.ToLowerInvariant() switch
    {
        "vertical-rl" => WritingModeType.VerticalRl,
        "vertical-lr" => WritingModeType.VerticalLr,
        _ => WritingModeType.HorizontalTb
    };

    public static HyphensType ParseHyphens(string value) => value.ToLowerInvariant() switch
    {
        "manual" => HyphensType.Manual,
        "auto" => HyphensType.Auto,
        _ => HyphensType.None
    };

    public static LineBreakType ParseLineBreak(string value) => value.ToLowerInvariant() switch
    {
        "loose" => LineBreakType.Loose,
        "normal" => LineBreakType.Normal,
        "strict" => LineBreakType.Strict,
        "anywhere" => LineBreakType.Anywhere,
        _ => LineBreakType.Auto
    };

    public static TextJustifyType ParseTextJustify(string value) => value.ToLowerInvariant() switch
    {
        "inter-word" => TextJustifyType.InterWord,
        "inter-character" => TextJustifyType.InterCharacter,
        "none" => TextJustifyType.None,
        _ => TextJustifyType.Auto
    };

    public static ResizeType ParseResize(string value) => value.ToLowerInvariant() switch
    {
        "both" => ResizeType.Both,
        "horizontal" => ResizeType.Horizontal,
        "vertical" => ResizeType.Vertical,
        _ => ResizeType.None
    };

    public static ContainType ParseContain(string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower == "none") return ContainType.None;
        if (lower == "strict") return ContainType.Strict;
        if (lower == "content") return ContainType.Content;
        if (lower == "layout") return ContainType.Layout;
        if (lower == "paint") return ContainType.Paint;
        if (lower == "size") return ContainType.Size;
        return ContainType.None;
    }

    public static ContentVisibilityType ParseContentVisibility(string value) => value.ToLowerInvariant() switch
    {
        "auto" => ContentVisibilityType.Auto,
        "hidden" => ContentVisibilityType.Hidden,
        _ => ContentVisibilityType.Visible
    };

    public static ImageRenderingType ParseImageRendering(string value) => value.ToLowerInvariant() switch
    {
        "crisp-edges" => ImageRenderingType.CrispEdges,
        "pixelated" => ImageRenderingType.Pixelated,
        _ => ImageRenderingType.Auto
    };

    public static MixBlendModeType ParseMixBlendMode(string value) => value.ToLowerInvariant() switch
    {
        "multiply" => MixBlendModeType.Multiply,
        "screen" => MixBlendModeType.Screen,
        "overlay" => MixBlendModeType.Overlay,
        "darken" => MixBlendModeType.Darken,
        "lighten" => MixBlendModeType.Lighten,
        "color-dodge" => MixBlendModeType.ColorDodge,
        "color-burn" => MixBlendModeType.ColorBurn,
        "hard-light" => MixBlendModeType.HardLight,
        "soft-light" => MixBlendModeType.SoftLight,
        "difference" => MixBlendModeType.Difference,
        "exclusion" => MixBlendModeType.Exclusion,
        "hue" => MixBlendModeType.Hue,
        "saturation" => MixBlendModeType.Saturation,
        "color" => MixBlendModeType.Color,
        "luminosity" => MixBlendModeType.Luminosity,
        _ => MixBlendModeType.Normal
    };

    public static BackgroundBlendModeType ParseBackgroundBlendMode(string value) => value.ToLowerInvariant() switch
    {
        "multiply" => BackgroundBlendModeType.Multiply,
        "screen" => BackgroundBlendModeType.Screen,
        "overlay" => BackgroundBlendModeType.Overlay,
        "darken" => BackgroundBlendModeType.Darken,
        "lighten" => BackgroundBlendModeType.Lighten,
        "color-dodge" => BackgroundBlendModeType.ColorDodge,
        "color-burn" => BackgroundBlendModeType.ColorBurn,
        "hard-light" => BackgroundBlendModeType.HardLight,
        "soft-light" => BackgroundBlendModeType.SoftLight,
        "difference" => BackgroundBlendModeType.Difference,
        "exclusion" => BackgroundBlendModeType.Exclusion,
        "hue" => BackgroundBlendModeType.Hue,
        "saturation" => BackgroundBlendModeType.Saturation,
        "color" => BackgroundBlendModeType.Color,
        "luminosity" => BackgroundBlendModeType.Luminosity,
        _ => BackgroundBlendModeType.Normal
    };

    public static OverscrollBehaviorType ParseOverscrollBehavior(string value) => value.ToLowerInvariant() switch
    {
        "contain" => OverscrollBehaviorType.Contain,
        "none" => OverscrollBehaviorType.None,
        _ => OverscrollBehaviorType.Auto
    };

    public static float ParseZoom(string value)
    {
        if (value.EndsWith("%") && float.TryParse(value[..^1], out var pct)) return pct / 100f;
        if (float.TryParse(value, out var num)) return num;
        return 1;
    }

    public static void ParseGap(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && Length.TryParse(parts[0], out var gap))
        {
            style.RowGap = gap;
            style.ColumnGap = parts.Length > 1 ? Length.Parse(parts[1]) : gap;
        }
    }

    /// <summary>
    /// Parses a 'box-shadow' value into a list of shadows. Supports the 'inset'
    /// keyword (anywhere before the lengths) and multiple comma-separated shadows.
    /// Mirrors the CSS box-shadow grammar: [inset? && <length>{2,4} && <color>?]# .
    /// </summary>
    public static List<BoxShadowValue>? ParseBoxShadow(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Trim() == "none")
            return null;

        var list = new List<BoxShadowValue>();
        foreach (var part in SplitCommaOutsideParens(value))
        {
            var shadow = ParseBoxShadowComponent(part);
            if (shadow != null)
                list.Add(shadow);
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>Splits a value on commas that are not inside parentheses, so that
    /// multiple box-shadows separate correctly while rgba()/rgb() color functions
    /// stay intact.</summary>
    public static IEnumerable<string> SplitCommaOutsideParens(string value)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == ',' && depth == 0)
            {
                yield return value[start..i];
                start = i + 1;
            }
        }
        yield return value[start..];
    }

    public static BoxShadowValue? ParseBoxShadowComponent(string component)
    {
        var parts = component.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        bool inset = false;
        int index = 0;
        if (parts[0].Equals("inset", StringComparison.OrdinalIgnoreCase))
        {
            inset = true;
            index++;
        }
        else if (parts.Length > 1 && parts[1].Equals("inset", StringComparison.OrdinalIgnoreCase))
        {
            // 'inset' may legally appear after the lengths.
            inset = true;
        }

        // The first two tokens are offsets.
        if (!TryParseLength(parts[index], out float offsetX) ||
            !TryParseLength(parts[index + 1], out float offsetY))
        {
            return null;
        }
        index += 2;

        float blurRadius = 0, spread = 0;
        if (index < parts.Length && TryParseLength(parts[index], out float br))
        {
            blurRadius = br;
            index++;
        }
        if (index < parts.Length && TryParseLength(parts[index], out float sp))
        {
            spread = sp;
            index++;
        }

        SKColor color;
        if (index < parts.Length)
        {
            color = ColorParser.Parse(string.Join(" ", parts.Skip(index)));
        }
        else
            color = new SKColor(0, 0, 0, 80); // default currentColor鈮坆lack with standard shadow alpha
        return new BoxShadowValue(color, offsetX, offsetY, blurRadius, spread, inset);
    }

    public static bool TryParseLength(string token, out float value)
    {
        value = 0;
        var t = token.Trim();
        if (t.EndsWith("px", StringComparison.OrdinalIgnoreCase) && float.TryParse(t[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
            return true;
        if (t.EndsWith("em", StringComparison.OrdinalIgnoreCase) && float.TryParse(t[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            value *= 16;
            return true;
        }
        if (float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
            return true;
        return false;
    }

    public static bool EvaluateSupportsCondition(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        // Handle 'not' prefix
        bool negate = false;
        var trimmed = condition.Trim();
        if (trimmed.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            negate = true;
            trimmed = trimmed[4..].Trim();
        }

        // Handle 'and' / 'or' combinators (simple version)
        if (trimmed.Contains(" and ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries);
            bool result = true;
            foreach (var part in parts)
                result = result && EvaluateSingleSupportsCondition(part.Trim());
            return negate ? !result : result;
        }
        if (trimmed.Contains(" or ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(new[] { " or " }, StringSplitOptions.RemoveEmptyEntries);
            bool result = false;
            foreach (var part in parts)
                result = result || EvaluateSingleSupportsCondition(part.Trim());
            return negate ? !result : result;
        }

        bool eval = EvaluateSingleSupportsCondition(trimmed);
        return negate ? !eval : eval;
    }

    public static bool EvaluateSingleSupportsCondition(string condition)
    {
        condition = condition.Trim();
        // Remove outer parentheses
        if (condition.StartsWith('(') && condition.EndsWith(')'))
            condition = condition[1..^1].Trim();

        // Parse property: value
        var colonIdx = condition.IndexOf(':');
        if (colonIdx < 0) return true;

        var propName = condition[..colonIdx].Trim().ToLowerInvariant();
        var propValue = condition[(colonIdx + 1)..].Trim().ToLowerInvariant();

        return propName switch
        {
            "display" => propValue is "flex" or "inline-flex" or "grid" or "inline-grid" or "block" or "inline-block" or "inline" or "list-item" or "none" or "table" or "table-cell" or "table-row",
            "position" => propValue is "static" or "relative" or "absolute" or "fixed" or "sticky",
            "transform" or "-webkit-transform" => propValue is not "none" || true,
            "transition" => true,
            "animation" => true,
            "overflow" or "overflow-x" or "overflow-y" => propValue is "visible" or "hidden" or "scroll" or "auto",
            "flex-wrap" => propValue is "nowrap" or "wrap" or "wrap-reverse",
            "justify-content" => propValue is "flex-start" or "flex-end" or "center" or "space-between" or "space-around" or "space-evenly",
            "align-items" => propValue is "flex-start" or "flex-end" or "center" or "baseline" or "stretch",
            "align-content" => propValue is "flex-start" or "flex-end" or "center" or "space-between" or "space-around" or "stretch",
            "gap" => true,
            "flex" or "flex-grow" or "flex-shrink" or "flex-basis" => true,
            "background" or "background-color" or "background-image" or "background-size" => true,
            "color" => true,
            "font-family" => true,
            "font-size" => true,
            "filter" or "-webkit-filter" => true,
            "clip-path" or "-webkit-clip-path" => true,
            "text-decoration" or "text-decoration-line" or "text-decoration-style" or "text-decoration-color" => true,
            "box-shadow" => true,
            "text-shadow" => true,
            "opacity" => true,
            "visibility" => propValue is "visible" or "hidden" or "collapse",
            "z-index" => true,
            "outline" or "outline-style" or "outline-width" or "outline-color" => true,
            "border" or "border-radius" => true,
            "margin" or "padding" => true,
            "width" or "height" or "min-width" or "max-width" or "min-height" or "max-height" => true,
            "top" or "right" or "bottom" or "left" => true,
            "float" => propValue is "none" or "left" or "right",
            "clear" => propValue is "none" or "left" or "right" or "both",
            "object-fit" => propValue is "fill" or "contain" or "cover" or "none" or "scale-down",
            "cursor" => true,
            "user-select" or "-webkit-user-select" => true,
            "pointer-events" => propValue is "auto" or "none",
            "white-space" => propValue is "normal" or "nowrap" or "pre" or "pre-wrap" or "pre-line",
            "word-break" => propValue is "normal" or "break-all" or "keep-all" or "break-word",
            "overflow-wrap" or "word-wrap" => propValue is "normal" or "break-word",
            "text-overflow" => propValue is "clip" or "ellipsis",
            "line-height" => true,
            "letter-spacing" => true,
            "list-style" or "list-style-type" or "list-style-position" or "list-style-image" => true,
            // An unknown property is NOT supported: @supports must report false
            // for declarations the engine has no handling for (CSS Conditional 3 §4).
            _ => UpBrowser.Core.Css.Properties.CssPropertyIdExtensions.FromString(propName) != UpBrowser.Core.Css.Properties.CssPropertyId.Invalid
        };
    }

    public static string GetOriginalShorthand(string longhand)
    {
        return longhand switch
        {
            var s when s.StartsWith("margin-") => "margin",
            var s when s.StartsWith("padding-") => "padding",
            var s when s.StartsWith("border-top-") || s.StartsWith("border-right-") || s.StartsWith("border-bottom-") || s.StartsWith("border-left-") => "border",
            var s when s.StartsWith("flex-") => "flex",
            var s when s.StartsWith("grid-") => "grid",
            _ => longhand
        };
    }
}

