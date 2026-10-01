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
            : ColorParser.Parse(parts[0].Trim(), style);

        style.ScrollbarTrackColor = parts.Count >= 2
            ? (parts[1].Trim() == "auto" ? null : ColorParser.Parse(parts[1].Trim(), style))
            : style.ScrollbarThumbColor;
    }

    public static void Apply(ComputedStyle style, string name, string value)
    {
    // Font-relative units inside these values resolve against this element's
    // own font (CSS Values 4 §6.3); the style argument carries it.
    using var _fontUnitScope = FontUnitContext.Use(style);
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
            case "color": style.Color = ColorParser.Parse(value, style); break;
            case "accent-color": style.AccentColor = value == "auto" ? null : ColorParser.Parse(value, style); break;
            case "caret-color": style.CaretColor = value == "auto" ? null : ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.Caret, value); break;

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
            case "background-color": style.BackgroundColor = ColorParser.Parse(value, style); break;
            case "background-image":
                style.BackgroundImage = ParseImageLayerList(value);
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
                style.TextDecoration = LegacyTextDecorationOf(style.TextDecorationLine);
                break;
            case "text-decoration-style": style.TextDecorationStyle = ParseTextDecorationStyle(value); break;
            case "text-decoration-color": style.TextDecorationColor = ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.TextDecoration, value); break;
            case "text-decoration-thickness": ApplyTextDecorationThickness(style, value); break;
            case "text-underline-offset": ApplyTextUnderlineOffset(style, value); break;
            case "text-underline-position": style.TextUnderlinePosition = ParseTextUnderlinePosition(value); break;
            case "text-decoration-skip-ink":
                style.TextDecorationSkipInk = !value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);
                break;
            case "text-emphasis": style.TextEmphasis = value; break;
            case "text-emphasis-color": style.TextEmphasisColor = value; break;
            case "text-emphasis-style": style.TextEmphasisStyle = value; break;
            case "text-emphasis-position": style.TextEmphasisPosition = value; break;
            case "text-shadow": style.TextShadow = ParseTextShadow(value); break;
            case "text-overflow": style.TextOverflow = value.ToLowerInvariant() == "ellipsis" ? TextOverflowType.Ellipsis : TextOverflowType.Clip; break;
            case "text-wrap":
                // Keep the whole (lowercased) value; 'balance'/'pretty' may combine with a
                // second keyword (e.g. "balance nowrap") in future, so we substring-match.
                style.TextWrap = value.Trim().ToLowerInvariant();
                break;
            case "-webkit-line-clamp":
            case "line-clamp":
                // 'none' (or a non-positive integer) disables clamping; otherwise the
                // value is the maximum number of visible lines.
                style.LineClamp = int.TryParse(value.Trim(), out var clampLines) && clampLines > 0 ? clampLines : 0;
                break;
            case "vertical-align": ApplyVerticalAlign(style, value); break;
            case "white-space": style.WhiteSpace = ParseWhiteSpace(value); break;
            case "word-break": style.WordBreak = ParseWordBreak(value); break;
            case "overflow-wrap": case "word-wrap": style.OverflowWrap = ParseOverflowWrap(value); break;
            case "visibility": style.Visibility = ParseVisibility(value); break;
            case "overflow":
                // Two-value shorthand: overflow: <x> <y>. Per CSS Overflow 3, if one
                // axis is 'visible' and the other is not, the 'visible' computes to
                // 'auto' (a box cannot be a scroll container on one axis only).
                {
                    var oparts = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    OverflowType oxs, oys;
                    if (oparts.Length >= 2) { oxs = ParseOverflow(oparts[0]); oys = ParseOverflow(oparts[1]); }
                    else { oxs = oys = ParseOverflow(value); }
                    if (oxs == OverflowType.Visible && oys != OverflowType.Visible) oxs = OverflowType.Auto;
                    else if (oys == OverflowType.Visible && oxs != OverflowType.Visible) oys = OverflowType.Auto;
                    style.OverflowX = oxs; style.OverflowY = oys;
                    style.Overflow = oxs == oys ? oxs : OverflowType.Auto;
                }
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
            case "border-top-width": style.BorderTopWidth = BorderWidthPx(value); break;
            case "border-right-width": style.BorderRightWidth = BorderWidthPx(value); break;
            case "border-bottom-width": style.BorderBottomWidth = BorderWidthPx(value); break;
            case "border-left-width": style.BorderLeftWidth = BorderWidthPx(value); break;
            case "border-top-style": style.BorderTopStyle = ParseBorderStyleValue(value); break;
            case "border-right-style": style.BorderRightStyle = ParseBorderStyleValue(value); break;
            case "border-bottom-style": style.BorderBottomStyle = ParseBorderStyleValue(value); break;
            case "border-left-style": style.BorderLeftStyle = ParseBorderStyleValue(value); break;
            case "border-top-color": style.BorderTopColor = ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.BorderTop, value); break;
            case "border-right-color": style.BorderRightColor = ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.BorderRight, value); break;
            case "border-bottom-color": style.BorderBottomColor = ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.BorderBottom, value); break;
            case "border-left-color": style.BorderLeftColor = ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.BorderLeft, value); break;
            case "border-radius": ParseBorderRadius(value, style); break;
            case "border-top-left-radius": ApplyRadiusPair(style, value, 0); break;
            case "border-top-right-radius": ApplyRadiusPair(style, value, 1); break;
            case "border-bottom-right-radius": ApplyRadiusPair(style, value, 2); break;
            case "border-bottom-left-radius": ApplyRadiusPair(style, value, 3); break;
            case "border-collapse": style.BorderCollapse = value.ToLowerInvariant() == "collapse"; break;
            case "border-spacing": ApplyBorderSpacing(style, value); break;
            case "border-image": ParseBorderImageShorthand(value, style); break;
            case "border-image-source": style.BorderImageSource = NormalizeImageSource(value); break;
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
            case "column-fill": { var cf = value.Trim().ToLowerInvariant(); if (cf == "auto" || cf == "balance") style.ColumnFill = cf; break; }
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
            case "column-rule-color": style.ColumnRuleColor = ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.ColumnRule, value); break;
            case "grid": style.Grid = value; break;
            case "grid-template": ParseGridTemplateShorthand(value, style); break;
            case "grid-template-columns": style.GridTemplateColumns = value == "none" ? null : value; break;
            case "grid-template-rows": style.GridTemplateRows = value == "none" ? null : value; break;
            case "grid-template-areas": style.GridTemplateAreas = value == "none" ? null : value; break;
            case "grid-auto-columns": style.GridAutoColumns = value; break;
            case "grid-auto-rows": style.GridAutoRows = value; break;
            case "grid-auto-flow": style.GridAutoFlow = ParseGridAutoFlow(value); break;
            case "grid-column":
                style.GridColumn = value;
                if (value.Contains('/')) { var parts = value.Split('/'); style.GridColumnStart = parts[0].Trim(); style.GridColumnEnd = parts.Length > 1 ? parts[1].Trim() : null; }
                else { style.GridColumnStart = value.Trim(); style.GridColumnEnd = null; }
                break;
            case "grid-column-start": style.GridColumnStart = value; break;
            case "grid-column-end": style.GridColumnEnd = value; break;
            case "grid-row":
                style.GridRow = value;
                if (value.Contains('/')) { var parts = value.Split('/'); style.GridRowStart = parts[0].Trim(); style.GridRowEnd = parts.Length > 1 ? parts[1].Trim() : null; }
                else { style.GridRowStart = value.Trim(); style.GridRowEnd = null; }
                break;
            case "grid-row-start": style.GridRowStart = value; break;
            case "grid-row-end": style.GridRowEnd = value; break;
            case "grid-area": style.GridArea = value; break;
            case "top": style.Top = Length.Parse(value); break;
            case "bottom": style.Bottom = Length.Parse(value); break;
            case "left": style.Left = Length.Parse(value); break;
            case "right": style.Right = Length.Parse(value); break;
            case "inset": ParseInsetShorthand(value, style); break;
            case "inset-block": ParseShorthand2(value, out var ibt, out var ibb); style.Top = ibt; style.Bottom = ibb; break;
            case "inset-inline": ParseShorthand2(value, out var iis, out var iie); ApplyInlineInset(style, iis, iie); break;
            case "inset-block-start": style.Top = Length.Parse(value); break;
            case "inset-block-end": style.Bottom = Length.Parse(value); break;
            case "inset-inline-start": ApplyInlineInset(style, Length.Parse(value), null); break;
            case "inset-inline-end": ApplyInlineInset(style, null, Length.Parse(value)); break;
            case "list-style-type": style.ListStyleType = ParseListStyleType(value, style); break;
            case "list-style-position": style.ListStylePosition = value.Contains("inside") ? ListStylePosition.Inside : ListStylePosition.Outside; break;
            case "list-style-image": style.ListStyleImage = NormalizeUrlValue(value); break;
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
                    style.TextIndentEachLine = false;
                    var rest = new List<string>();
                    foreach (var token in indentTokens)
                    {
                        if (token.Equals("hanging", StringComparison.OrdinalIgnoreCase))
                            style.TextIndentHanging = true;
                        else if (token.Equals("each-line", StringComparison.OrdinalIgnoreCase))
                            style.TextIndentEachLine = true;
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
                            // em/ex/ch/lh resolve against this element's own font, which is only
                            // known once the font properties have been applied.
                            style.TextIndent = ti.ToPixels(style.FontSize, style.FontSize, 0, 0);
                            style.TextIndentPercent = 0;
                        }
                    }
                }
                break;
            case "letter-spacing":
                if (value == "normal") style.LetterSpacing = 0;
                else if (Length.TryParse(value, out var ls))
                    style.LetterSpacing = ls.ToPixels(style.FontSize, style.FontSize, 0, 0);
                break;
            case "word-spacing":
                if (value == "normal") style.WordSpacing = 0;
                else if (Length.TryParse(value, out var ws))
                    style.WordSpacing = ws.ToPixels(style.FontSize, style.FontSize, 0, 0);
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
            case "font-variant":
                style.FontVariant = value.ToLowerInvariant();
                // Normalize the caps keyword onto the same cascade slot as
                // font-variant-caps (CSS Fonts 4 §3.5): 'font-variant: x' is
                // 'font-variant-caps: x' plus resetting the other variant
                // longhands, so a later 'font-variant-caps: normal' must be
                // able to override it.
                {
                    var tokens = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var token in tokens)
                        if (token is "small-caps" or "all-small-caps" or "petite-caps" or "all-petite-caps" or "unicase" or "titling-caps")
                        {
                            style.FontVariantCaps = token;
                            break;
                        }
                    if (Array.Exists(tokens, static t => t == "normal"))
                        style.FontVariantCaps = "normal";
                }
                break;
            case "font-variant-caps": style.FontVariantCaps = value.ToLowerInvariant(); break;
            case "font-stretch": style.FontStretch = value.ToLowerInvariant(); break;
            case "font-kerning": style.FontKerning = value.ToLowerInvariant(); break;
            case "font-synthesis": style.FontSynthesis = value.ToLowerInvariant(); break;
            case "font-optical-sizing": style.FontOpticalSizing = value.ToLowerInvariant(); break;
            case "font-variation-settings": style.FontVariationSettings = value; break;
            case "font-feature-settings": style.FontFeatureSettings = value; break;
            case "font-size-adjust": if (value != "none" && float.TryParse(value, out var fsa)) style.FontSizeAdjust = fsa; break;
            case "outline": ParseOutlineShorthand(value, style); break;
            case "outline-width":
                style.OutlineWidth = BorderWidthPx(value);
                break;
            case "outline-color": style.OutlineColor = ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.Outline, value); break;
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
            case "box-decoration-break":
                style.BoxDecorationBreak = value.Trim().StartsWith("clone", StringComparison.OrdinalIgnoreCase)
                    ? BoxDecorationBreakType.Clone : BoxDecorationBreakType.Slice;
                break;
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
                style.ColumnRuleColor = ColorParser.Parse(p, style);
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

    public static FontWeight ParseFontWeight(string value)
    {
        var v = value.ToLowerInvariant().Trim();
        switch (v)
        {
            case "normal": return FontWeight.Normal;
            case "bold": return FontWeight.Bold;
            // Relative keywords need the parent's used weight for the CSS Fonts 4
            // table; with our face set they land on the nearest concrete step.
            case "bolder": return FontWeight.Bold;
            case "lighter": return FontWeight.Normal;
        }
        if (int.TryParse(v, out var n) && n >= 1 && n <= 1000)
        {
            // Snap to the nearest 100-step the enum can carry (1..1000 are valid).
            int step = (int)Math.Clamp((long)Math.Round(n / 100.0) * 100, 100, 900);
            return (FontWeight)step;
        }
        return FontWeight.Normal;
    }

    public static FontStyleType ParseFontStyle(string value) => ParseFontStyle(value, null);

    /// <summary>
    /// 'font-style' is either a keyword or 'oblique' with an angle (CSS Fonts 4 §3.2.2).
    /// The angle is kept so the renderer can synthesize that exact slant; plain 'oblique'
    /// leaves it unset, which means "use the family's own slanted face".
    /// </summary>
    public static FontStyleType ParseFontStyle(string value, ComputedStyle? style)
    {
        var token = value.Trim();
        if (token.Equals("italic", StringComparison.OrdinalIgnoreCase))
        {
            if (style != null) style.FontStyleObliqueDegrees = null;
            return FontStyleType.Italic;
        }
        if (token.Equals("oblique", StringComparison.OrdinalIgnoreCase))
        {
            if (style != null) style.FontStyleObliqueDegrees = null;
            return FontStyleType.Oblique;
        }
        if (token.StartsWith("oblique", StringComparison.OrdinalIgnoreCase) &&
            TryParseAngleDegrees(token["oblique".Length..].Trim(), out float degrees))
        {
            if (style != null) style.FontStyleObliqueDegrees = Math.Clamp(degrees, -90f, 90f);
            return FontStyleType.Oblique;
        }
        if (style != null) style.FontStyleObliqueDegrees = null;
        return FontStyleType.Normal;
    }

    private static bool TryParseAngleDegrees(string token, out float degrees)
    {
        degrees = 0;
        token = token.Trim().ToLowerInvariant();
        float number;
        try
        {
            if (token.EndsWith("grad")) number = float.Parse(token[..^4], System.Globalization.CultureInfo.InvariantCulture) * 0.9f;
            else if (token.EndsWith("turn")) number = float.Parse(token[..^4], System.Globalization.CultureInfo.InvariantCulture) * 360f;
            else if (token.EndsWith("rad")) number = float.Parse(token[..^3], System.Globalization.CultureInfo.InvariantCulture) * (180f / MathF.PI);
            else if (token.EndsWith("deg")) number = float.Parse(token[..^3], System.Globalization.CultureInfo.InvariantCulture);
            else return false;
        }
        catch (FormatException) { return false; }
        if (float.IsNaN(number) || float.IsInfinity(number)) return false;
        degrees = number;
        return true;
    }

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
        "keep-all" => WordBreakMode.KeepAll,
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
        // Split respecting parentheses: a naive Split(' ') shreds 'calc(100% - 10px)'
        // into fragments that fail Length.Parse, silently dropping the offset.
        var parts = ShorthandExpander.SplitShorthand(value);
        if (parts.Count > 0)
        {
            style.BackgroundPositionX = parts[0].ToLowerInvariant() switch
            {
                "left" => new PixelLength(0),
                "center" => new PercentLength(0.5f),
                "right" => new PercentLength(1),
                _ => Length.Parse(parts[0])
            };
            style.BackgroundPositionY = parts.Count > 1 ? Length.Parse(parts[1]) : new PixelLength(0);
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

    /// <summary>
    /// Split a comma-separated image layer list, normalizing each layer. An
    /// empty url() / url("") is invalid at computed-value time, so the layer
    /// becomes 'none' (CSS Values 3 §10.8).
    /// </summary>
    private static List<string>? ParseImageLayerList(string value)
    {
        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        var layers = SplitCommaOutsideParens(value).Select(s => s.Trim()).ToList();
        for (int i = 0; i < layers.Count; i++)
        {
            if (IsUrlWithoutAddress(layers[i]))
                layers[i] = "none";
        }
        return layers;
    }

    /// <summary>True for 'url()', 'url("")', "url(' ')" — a url token with no address.</summary>
    public static bool IsUrlWithoutAddress(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return false;
        var close = trimmed.IndexOf(')');
        if (close < 0)
            return false;
        var inner = trimmed[4..close].Trim().Trim('"', '\'').Trim();
        return inner.Length == 0;
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
                        // Empty address: the layer is invalid at computed-value
                        // time, so it contributes no image.
                        if (string.IsNullOrEmpty(image))
                            image = null;
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
                    style.BackgroundColor = ColorParser.Parse(part, style);
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

    /// <summary>Image values that are generated by the engine rather than referenced by url.</summary>
    public static bool IsGeneratedImage(string value)
    {
        var v = value.TrimStart();
        return v.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("repeating-linear-gradient(", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("radial-gradient(", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("repeating-radial-gradient(", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("conic-gradient(", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("repeating-conic-gradient(", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Normalise an &lt;image&gt; longhand: url(), a generated gradient, or none.</summary>
    public static string? NormalizeImageSource(string value)
    {
        if (IsGeneratedImage(value)) return value.Trim();
        return NormalizeUrlValue(value);
    }

    /// <summary>
    /// Parse a single image value into a URL, or null for 'none' and for an
    /// empty url() (invalid at computed-value time).
    /// </summary>
    public static string? NormalizeUrlValue(string value)
    {
        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        var url = ParseUrl(value);
        return string.IsNullOrEmpty(url) ? null : url;
    }

    public static void ParseBorderShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool hasWidth = false;
        foreach (var part in parts)
        {
            if (part is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge"
                or "inset" or "outset" or "none" or "hidden")
            {
                var bs = ParseBorderStyleValue(part);
                style.BorderTopStyle = bs; style.BorderRightStyle = bs;
                style.BorderBottomStyle = bs; style.BorderLeftStyle = bs;
            }
            else if (IsBorderWidthToken(part))
            {
                // thin/medium/thick keywords and any length (px/em/rem) resolve to px.
                var width = BorderWidthPx(part);
                style.BorderTopWidth = width; style.BorderRightWidth = width;
                style.BorderBottomWidth = width; style.BorderLeftWidth = width;
                hasWidth = true;
            }
            else
            {
                var color = ColorParser.Parse(part, style);
                style.BorderTopColor = color; style.BorderRightColor = color;
                style.BorderBottomColor = color; style.BorderLeftColor = color;
                MarkCurrentColor(style, CurrentColorSlot.AllBorders, part);
            }
        }

        // The border shorthand resets border-width to its initial value (medium) when
        // no width token is present, so 'border: solid' paints a 3px border.
        if (!hasWidth)
        {
            style.BorderTopWidth = style.BorderRightWidth =
                style.BorderBottomWidth = style.BorderLeftWidth = 3f;
        }
    }

    /// <summary>A border-width token is one of the keywords or a length unit the
    /// width parser understands (not a bare percentage, which border-width rejects).</summary>
    private static bool IsBorderWidthToken(string part) =>
        part is "thin" or "medium" or "thick"
        || (part.Length > 2 && (part.EndsWith("px") || part.EndsWith("em") || part.EndsWith("rem")));

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
            if (IsGeneratedImage(token))
            {
                style.BorderImageSource = token;
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
                var color = ColorParser.Parse(part, style);
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

    /// <summary>
    /// 'border-spacing' takes one or two lengths: the first is the column (inline-axis)
    /// gap, the second - when present - the row (block-axis) gap (CSS 2.1 §17.5).
    /// </summary>
    public static void ApplyBorderSpacing(ComputedStyle style, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        float inlineSpacing = ParseSize(parts.Length > 0 ? parts[0] : value) ?? 0;
        style.BorderSpacing = Math.Max(0, inlineSpacing);
        style.BorderRowSpacing = parts.Length > 1 ? Math.Max(0, ParseSize(parts[1]) ?? inlineSpacing) : null;
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
        var c = colors.Select(v => ColorParser.Parse(v, style)).ToList();
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
        // 'border-radius: <horizontal-1..4> / <vertical-1..4>' (CSS Backgrounds 3 5.3).
        // A missing vertical half repeats the horizontal one; percentages stay
        // encoded by ParseRadiusValue and resolve against the box at paint time.
        int slash = value.IndexOf('/');
        var horizontal = slash >= 0 ? value[..slash] : value;
        var vertical = slash >= 0 ? value[(slash + 1)..] : horizontal;

        var rx = ToFourRadii(horizontal, style.BorderTopLeftRadius, style.BorderTopRightRadius,
            style.BorderBottomRightRadius, style.BorderBottomLeftRadius);
        var ry = ToFourRadii(vertical, rx[0], rx[1], rx[2], rx[3]);

        style.BorderTopLeftRadius = rx[0];
        style.BorderTopRightRadius = rx[1];
        style.BorderBottomRightRadius = rx[2];
        style.BorderBottomLeftRadius = rx[3];
        style.BorderTopLeftRadiusY = ry[0];
        style.BorderTopRightRadiusY = ry[1];
        style.BorderBottomRightRadiusY = ry[2];
        style.BorderBottomLeftRadiusY = ry[3];
    }

    /// <summary>Expand a 1-to-4 radius list into top-left, top-right, bottom-right,
    /// bottom-left, following the CSS corner repetition rules.</summary>
    private static float[] ToFourRadii(string value, float tl, float tr, float br, float bl)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parsed = new List<float>(4);
        foreach (var token in tokens)
            parsed.Add(ParseRadiusValue(token) ?? 0);
        return parsed.Count switch
        {
            0 => new[] { tl, tr, br, bl },
            1 => new[] { parsed[0], parsed[0], parsed[0], parsed[0] },
            2 => new[] { parsed[0], parsed[1], parsed[0], parsed[1] },
            3 => new[] { parsed[0], parsed[1], parsed[2], parsed[1] },
            _ => new[] { parsed[0], parsed[1], parsed[2], parsed[3] },
        };
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

    /// <summary>Resolve a border-width token: the CSS keywords thin/medium/thick map
    /// to 1/3/5px (CSS Backgrounds 3 §4), otherwise a length via <see cref="ParseSize"/>.
    /// The border shorthand emits per-side width longhands, so this must understand
    /// the keywords, not just lengths.</summary>
    public static float BorderWidthPx(string value) => value.Trim().ToLowerInvariant() switch
    {
        "thin" => 1f,
        "medium" => 3f,
        "thick" => 5f,
        _ => ParseSize(value) ?? 0f,
    };

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

    /// <summary>'border-*-radius: &lt;horizontal&gt; [&lt;vertical&gt;]' for one corner
    /// (0 = top-left, 1 = top-right, 2 = bottom-right, 3 = bottom-left).</summary>
    private static void ApplyRadiusPair(ComputedStyle style, string value, int corner)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float horizontal = ParseRadiusValue(tokens.Length > 0 ? tokens[0] : "0") ?? 0;
        // A single value makes the corner circular: the vertical radius mirrors it.
        float vertical = tokens.Length > 1 ? (ParseRadiusValue(tokens[1]) ?? 0) : horizontal;
        switch (corner)
        {
            case 0:
                style.BorderTopLeftRadius = horizontal; style.BorderTopLeftRadiusY = vertical; break;
            case 1:
                style.BorderTopRightRadius = horizontal; style.BorderTopRightRadiusY = vertical; break;
            case 2:
                style.BorderBottomRightRadius = horizontal; style.BorderBottomRightRadiusY = vertical; break;
            default:
                style.BorderBottomLeftRadius = horizontal; style.BorderBottomLeftRadiusY = vertical; break;
        }
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
            style.FlexGrow = g; style.FlexShrink = 1; i++;
            if (i < parts.Length && float.TryParse(parts[i], out var s))
            { style.FlexShrink = s; i++; }
            // A trailing non-number token is the explicit flex-basis; only when no
            // basis is given does a numeric 'flex' shorthand default it to 0%.
            if (i < parts.Length)
            {
                var b = parts[i].Trim().ToLowerInvariant();
                style.FlexBasis = b == "auto" ? AutoLength.Instance : Length.Parse(parts[i]);
            }
            else
            {
                style.FlexBasis = new PercentLength(0);
            }
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
                style.ListStyleImage = NormalizeUrlValue(part);
        }
    }

    public static void ParseFontShorthand(string value, ComputedStyle style)
    {
        // CSS Fonts 4 §5.3: the shorthand resets the variant slots it does not
        // carry, otherwise a previous font-variant(-caps) would leak through.
        style.FontVariant = "normal";
        style.FontVariantCaps = "normal";
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
            else if (lower == "small-caps")
            {
                style.FontVariant = "small-caps";
                style.FontVariantCaps = "small-caps";
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
                style.OutlineColor = ColorParser.Parse(part, style);
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

    /// <summary>
    /// Maps inline-axis insets (from inset-inline[-start|-end]) onto the
    /// physical left/right properties. With 'direction: rtl' the start and end
    /// edges flip (CSS Logical 1 §4.3). A null argument means "not specified".
    /// </summary>
    private static void ApplyInlineInset(ComputedStyle style, Length? start, Length? end)
    {
        bool rtl = style.Direction == "rtl";
        if (start != null)
        {
            if (rtl) style.Right = start; else style.Left = start;
        }
        if (end != null)
        {
            if (rtl) style.Left = end; else style.Right = end;
        }
    }

    /// <summary>'text-decoration-line' takes a space-separated list of keywords
    /// (CSS Text Decoration 4 §2.1), so 'underline overline' asks for two lines.</summary>
    public static TextDecorationLineType ParseTextDecorationLine(string value)
    {
        var result = TextDecorationLineType.None;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "none": return TextDecorationLineType.None;
                case "underline": result |= TextDecorationLineType.Underline; break;
                case "overline": result |= TextDecorationLineType.Overline; break;
                case "line-through": result |= TextDecorationLineType.LineThrough; break;
            }
        }
        return result;
    }

    /// <summary>The engine keeps a pre-flags copy of the line list in the legacy
    /// 'text-decoration' enum; it can only name one line, so it follows the first
    /// one the modern property asks for.</summary>
    public static TextDecorationType LegacyTextDecorationOf(TextDecorationLineType line) =>
        line.HasUnderline() ? TextDecorationType.Underline
        : line.HasOverline() ? TextDecorationType.Overline
        : line.HasLineThrough() ? TextDecorationType.LineThrough
        : TextDecorationType.None;

    public static TextDecorationStyleType ParseTextDecorationStyle(string value) => value.ToLowerInvariant() switch
    {
        "double" => TextDecorationStyleType.Double,
        "dotted" => TextDecorationStyleType.Dotted,
        "dashed" => TextDecorationStyleType.Dashed,
        "wavy" => TextDecorationStyleType.Wavy,
        _ => TextDecorationStyleType.Solid
    };

    /// <summary>'text-decoration-thickness: auto | from-font | &lt;length&gt; | &lt;percentage&gt;'
    /// (CSS Text Decoration 4 §3.4). Percentages resolve against the font size.</summary>
    private static void ApplyTextDecorationThickness(ComputedStyle style, string value)
    {
        var token = value.Trim().ToLowerInvariant();
        if (token == "from-font")
        {
            style.TextDecorationThicknessFromFont = true;
            style.TextDecorationThickness = float.NaN;
            return;
        }
        style.TextDecorationThicknessFromFont = false;
        if (token == "auto" || !Length.TryParse(token, out var length))
        {
            style.TextDecorationThickness = float.NaN;
            return;
        }
        style.TextDecorationThickness = Math.Max(0, length.ToPixels(style.FontSize, style.FontSize, 0, 0));
    }

    /// <summary>'text-underline-offset: auto | &lt;length&gt; | &lt;percentage&gt;'
    /// (CSS Text Decoration 4 §3.2); percentages resolve against the font size.</summary>
    private static void ApplyTextUnderlineOffset(ComputedStyle style, string value)
    {
        var token = value.Trim().ToLowerInvariant();
        if (token == "auto" || !Length.TryParse(token, out var length))
        {
            style.TextUnderlineOffsetIsAuto = true;
            style.TextUnderlineOffset = 0;
            return;
        }
        style.TextUnderlineOffsetIsAuto = false;
        style.TextUnderlineOffset = length.ToPixels(style.FontSize, style.FontSize, 0, 0);
    }

    /// <summary>'text-underline-position: auto | from-font | alphabetic | under | left | right'.</summary>
    private static TextUnderlinePositionType ParseTextUnderlinePosition(string value)
    {
        var token = value.Trim().ToLowerInvariant();
        // 'from-font' asks for the font's own underline position; 'auto' and
        // 'alphabetic' keep the line near the alphabetic baseline.
        if (token == "auto") return TextUnderlinePositionType.Auto;
        if (token.Contains("from-font")) return TextUnderlinePositionType.FromFont;
        if (token.Contains("left")) return TextUnderlinePositionType.Left;
        if (token.Contains("right")) return TextUnderlinePositionType.Right;
        if (token.Contains("under")) return TextUnderlinePositionType.Under;
        if (token.Contains("alphabetic")) return TextUnderlinePositionType.Alphabetic;
        return TextUnderlinePositionType.Auto;
    }

    /// <summary>'text-decoration' = &lt;line&gt;* || &lt;style&gt; || &lt;color&gt; || &lt;thickness&gt;.
    /// Tokens are classified rather than consumed positionally, and any longhand the
    /// shorthand leaves out returns to its initial value.</summary>
    public static void ParseTextDecorationShorthand(string value, ComputedStyle style)
    {
        style.TextDecorationLine = TextDecorationLineType.None;
        style.TextDecorationStyle = TextDecorationStyleType.Solid;
        style.TextDecorationColor = default;
        style.TextDecorationThickness = float.NaN;
        style.TextDecorationThicknessFromFont = false;

        foreach (var part in ShorthandExpander.SplitShorthand(value))
        {
            var lower = part.ToLowerInvariant();
            switch (lower)
            {
                case "none":
                    style.TextDecoration = TextDecorationType.None;
                    return;
                case "underline":
                    style.TextDecorationLine |= TextDecorationLineType.Underline;
                    continue;
                case "overline":
                    style.TextDecorationLine |= TextDecorationLineType.Overline;
                    continue;
                case "line-through":
                    style.TextDecorationLine |= TextDecorationLineType.LineThrough;
                    continue;
                case "solid":
                case "double":
                case "dotted":
                case "dashed":
                case "wavy":
                    style.TextDecorationStyle = ParseTextDecorationStyle(lower);
                    continue;
                case "auto":
                case "from-font":
                    ApplyTextDecorationThickness(style, lower);
                    continue;
            }
            if (Length.TryParse(lower, out _))
            {
                ApplyTextDecorationThickness(style, lower);
                continue;
            }
            style.TextDecorationColor = ColorParser.Parse(part, style);
            MarkCurrentColor(style, CurrentColorSlot.TextDecoration, part);
        }
        style.TextDecoration = LegacyTextDecorationOf(style.TextDecorationLine);
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

