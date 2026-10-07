using System.Text;
using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Css.ElementStyles;
using CurrentColorSlot = Acrux.Core.Dom.ComputedStyle.CurrentColorSlot;

namespace Acrux.Core.Css.Resolver;

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
    // Logical box properties are also queued so StyleAdjuster can re-map them once
    // 'direction' is final; the first mapping below still gives the LTR answer.
    if (IsLogicalBoxProperty(name))
        QueueLogicalProperty(style, name, value);
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
            case "display": ApplyDisplay(style, value); break;
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
            case "margin-inline": ParseShorthand2(value, out var mil, out var mir); SetMarginSide(style, InlineStartSide(style), mil); SetMarginSide(style, InlineEndSide(style), mir); break;
            case "margin-block-start": style.MarginTop = Length.Parse(value); break;
            case "margin-block-end": style.MarginBottom = Length.Parse(value); break;
            case "margin-inline-start": SetMarginSide(style, InlineStartSide(style), Length.Parse(value)); break;
            case "margin-inline-end": SetMarginSide(style, InlineEndSide(style), Length.Parse(value)); break;
            case "padding":
                ParseShorthand4(value, out var pt, out var pr, out var pb, out var pl);
                style.PaddingTop = pt; style.PaddingRight = pr; style.PaddingBottom = pb; style.PaddingLeft = pl;
                break;
            case "padding-top": style.PaddingTop = Length.Parse(value); break;
            case "padding-bottom": style.PaddingBottom = Length.Parse(value); break;
            case "padding-left": style.PaddingLeft = Length.Parse(value); break;
            case "padding-right": style.PaddingRight = Length.Parse(value); break;
            case "padding-block": ParseShorthand2(value, out var pbt, out var pbb); style.PaddingTop = pbt; style.PaddingBottom = pbb; break;
            case "padding-inline": ParseShorthand2(value, out var pil, out var pir); SetPaddingSide(style, InlineStartSide(style), pil); SetPaddingSide(style, InlineEndSide(style), pir); break;
            case "padding-block-start": style.PaddingTop = Length.Parse(value); break;
            case "padding-block-end": style.PaddingBottom = Length.Parse(value); break;
            case "padding-inline-start": SetPaddingSide(style, InlineStartSide(style), Length.Parse(value)); break;
            case "padding-inline-end": SetPaddingSide(style, InlineEndSide(style), Length.Parse(value)); break;
            case "color": style.Color = ColorParser.Parse(value, style); break;
            case "accent-color": style.AccentColor = value == "auto" ? null : ColorParser.Parse(value, style); break;
            case "caret-color": style.CaretColor = value == "auto" ? null : ColorParser.Parse(value, style); MarkCurrentColor(style, CurrentColorSlot.Caret, value); break;
            // CSS UI 4 §4.3: 'caret-shape' is the keyword pair, and a platform that draws one
            // caret still keeps the authored one for the computed surface and for scripts.
            case "caret-shape": { var caretShape = value.Trim().ToLowerInvariant();
                    if (caretShape is "auto" or "bar" or "block") style.CaretShape = caretShape; break; }
            // CSS Scroll Snap 1 §6.1: the two four-sided scroll insets, whose initial is zero and
            // which take negative lengths but no percentages.
            case "scroll-margin-top": SetScrollInset(style, value, 0); break;
            case "scroll-margin-right": SetScrollInset(style, value, 1); break;
            case "scroll-margin-bottom": SetScrollInset(style, value, 2); break;
            case "scroll-margin-left": SetScrollInset(style, value, 3); break;
            case "scroll-padding-top": SetScrollInset(style, value, 4); break;
            case "scroll-padding-right": SetScrollInset(style, value, 5); break;
            case "scroll-padding-bottom": SetScrollInset(style, value, 6); break;
            case "scroll-padding-left": SetScrollInset(style, value, 7); break;

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
            // '-webkit-appearance' is an alias, not a second property: measured in the reference
            // engine, writing it changes the computed 'appearance' and the CSSOM normalises
            // cssText to the unprefixed name. What a control paints as is not in CSS at all —
            // it comes from ComputedStyle.NativeThemeFamily, which the element itself carries.
            case "appearance": case "-webkit-appearance":
                style.Appearance = value.ToLowerInvariant();
                break;
            case "forced-color-adjust": style.ForcedColorAdjust = value.ToLowerInvariant() == "none" ? ForcedColorAdjustType.None : ForcedColorAdjustType.Auto; break;
            case "background": ParseBackgroundShorthand(value, style); break;
            case "background-color": style.BackgroundColor = ColorParser.Parse(value, style); break;
            case "background-image":
                style.BackgroundImage = ParseImageLayerList(value);
                break;
            case "background-repeat":
                // The grammars of the background longhands are checked before anything is
                // written: a value the grammar does not read is a parse error, and a parse
                // error leaves the element on the value the cascade gave it before (measured:
                // 'background-repeat: repeat-x no-repeat' keeps the earlier 'round').
                if (!ShorthandExpander.IsBackgroundRepeatValue(value)) break;
                // Always a list, even for one layer: the two axes of 'space round' survive only in
                // a pair, and the scalar field has room for one axis (measured: the reference
                // engine reads that value back as 'space round', not as 'space').
                style.BackgroundRepeatLayers = ParseBackgroundLayerList(value, ParseOneBackgroundRepeatPair, always: true);
                style.BackgroundRepeat = FoldRepeatPair(ParseOneBackgroundRepeatPair(FirstBackgroundLayer(value)));
                break;
            case "background-position":
                if (!ShorthandExpander.IsBackgroundPositionValue(value)) break;
                ApplyBackgroundPositionLonghand(value, style);
                break;
            case "background-position-x":
                if (!ShorthandExpander.IsBackgroundPositionAxisValue(value, horizontal: true)) break;
                ApplyBackgroundPositionAxisLonghand(value, style, horizontal: true);
                break;
            case "background-position-y":
                if (!ShorthandExpander.IsBackgroundPositionAxisValue(value, horizontal: false)) break;
                ApplyBackgroundPositionAxisLonghand(value, style, horizontal: false);
                break;
            case "background-size":
                if (!ShorthandExpander.IsBackgroundSizeValue(value)) break;
                ParseBackgroundSize(FirstBackgroundLayer(value), style);
                style.BackgroundSizeLayers = ParseBackgroundLayerList(value, ParseOneBackgroundSize);
                break;
            case "background-attachment":
                if (!ShorthandExpander.IsBackgroundAttachmentValue(value)) break;
                style.BackgroundAttachment = ParseBackgroundAttachment(FirstBackgroundLayer(value));
                style.BackgroundAttachmentLayers = ParseBackgroundLayerList(value, ParseBackgroundAttachment);
                break;
            case "background-clip":
                if (!ShorthandExpander.IsBackgroundBoxValue(value, allowText: true)) break;
                style.BackgroundClip = value.ToLowerInvariant();
                style.BackgroundClipLayers = ParseBackgroundLayerList(value, s => s.ToLowerInvariant());
                break;
            case "background-origin":
                if (!ShorthandExpander.IsBackgroundBoxValue(value, allowText: false)) break;
                style.BackgroundOrigin = value.ToLowerInvariant();
                style.BackgroundOriginLayers = ParseBackgroundLayerList(value, s => s.ToLowerInvariant());
                break;
            case "background-blend-mode": style.BackgroundBlendMode = ParseBackgroundBlendMode(value); break;
            case "text-align": style.TextAlign = ParseTextAlign(value); break;
            case "text-align-last": style.TextAlignLast = ParseTextAlignLast(value); break;
            case "text-decoration": ParseTextDecorationShorthand(value, style); break;
            case "text-decoration-line":
                style.TextDecorationLine = ParseTextDecorationLine(value);
                style.TextDecoration = LegacyTextDecorationOf(style.TextDecorationLine);
                break;
            case "text-decoration-style": style.TextDecorationStyle = ParseTextDecorationStyle(value); break;
            case "text-decoration-color": ApplyTextDecorationColor(style, value); break;
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
            case "text-overflow": ParseTextOverflow(value, style); break;
            case "text-wrap":
                // 'text-wrap' is the shorthand of text-wrap-mode + text-wrap-style
                // (CSS Text 4 §4). Tokens are classified rather than positional, and the
                // legacy string is kept in sync for the balancing code.
                {
                    string tw = value.Trim().ToLowerInvariant();
                    style.TextWrapMode = tw.Contains("nowrap") ? TextWrapModeType.NoWrap : TextWrapModeType.Wrap;
                    style.TextWrapStyle = tw.Contains("balance") ? TextWrapStyleType.Balanced
                        : tw.Contains("pretty") ? TextWrapStyleType.Pretty
                        : tw.Contains("stable") ? TextWrapStyleType.Stable : TextWrapStyleType.Auto;
                    RecomputeEffectiveWhiteSpace(style);
                }
                break;
            case "-webkit-line-clamp":
            case "line-clamp":
                // 'none' (or a non-positive integer) disables clamping; otherwise the
                // value is the maximum number of visible lines.
                style.LineClamp = int.TryParse(value.Trim(), out var clampLines) && clampLines > 0 ? clampLines : 0;
                break;
            case "vertical-align": ApplyVerticalAlign(style, value); break;
            case "white-space": ApplyWhiteSpaceShorthand(style, value); break;
            case "white-space-collapse":
                // CSS Text 4 §3.2 longhand of 'white-space'.
                style.WhiteSpaceCollapse = value.Trim().ToLowerInvariant() switch
                {
                    "preserve" => WhiteSpaceCollapseType.Preserve,
                    "break-spaces" => WhiteSpaceCollapseType.BreakSpaces,
                    "preserve-breaks" => WhiteSpaceCollapseType.PreserveBreaks,
                    "discard" => WhiteSpaceCollapseType.Discard,
                    _ => WhiteSpaceCollapseType.Collapse,
                };
                RecomputeEffectiveWhiteSpace(style);
                break;
            case "text-wrap-mode":
                // CSS Text 4 §4.1: the wrapping half of 'white-space'.
                style.TextWrapMode = value.Trim().ToLowerInvariant() == "nowrap"
                    ? TextWrapModeType.NoWrap : TextWrapModeType.Wrap;
                RecomputeEffectiveWhiteSpace(style);
                break;
            case "text-wrap-style":
                // CSS Text 4 §4.3. 'auto' is the initial; 'pretty' is browser-specific.
                style.TextWrapStyle = value.Trim().ToLowerInvariant() switch
                {
                    "stable" => TextWrapStyleType.Stable,
                    "balanced" => TextWrapStyleType.Balanced,
                    "pretty" => TextWrapStyleType.Pretty,
                    _ => TextWrapStyleType.Auto,
                };
                RecomputeEffectiveWhiteSpace(style);
                break;
            case "word-break": style.WordBreak = ParseWordBreak(value); break;
            case "overflow-wrap": case "word-wrap": style.OverflowWrap = ParseOverflowWrap(value); break;
            case "visibility": style.Visibility = ParseVisibility(value); break;
            case "overflow":
                // Two-value shorthand: overflow: <x> <y>. The §3.3.1 pair constraints are NOT
                // applied here — StyleAdjuster.AdjustOverflow decides them on the computed
                // style, so that a longhand-written pair gets the same answer.
                {
                    var oparts = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    OverflowType oxs, oys;
                    if (oparts.Length >= 2) { oxs = ParseOverflow(oparts[0]); oys = ParseOverflow(oparts[1]); }
                    else { oxs = oys = ParseOverflow(value); }
                    style.OverflowX = oxs; style.OverflowY = oys;
                }
                break;
            case "overflow-x": style.OverflowX = ParseOverflow(value); break;
            case "overflow-clip-margin":
                // CSS Overflow 3 §4.1: 'content-box' | 'border-box' | 'padding-box' | <length>.
                // A negative length is out of range and drops the declaration.
                {
                    string cm = value.Trim().ToLowerInvariant();
                    if (cm == "content-box") style.OverflowClipMarginBox = OverflowClipMarginBox.ContentBox;
                    else if (cm == "border-box") style.OverflowClipMarginBox = OverflowClipMarginBox.BorderBox;
                    else if (cm == "padding-box") style.OverflowClipMarginBox = OverflowClipMarginBox.PaddingBox;
                    else
                    {
                        var len = Length.Parse(cm);
                        // A negative length is out of range: drop the declaration.
                        if (len is PixelLength neg && neg.Value < 0) break;
                        style.OverflowClipMargin = len;
                    }
                }
                break;
            case "overflow-y": style.OverflowY = ParseOverflow(value); break;
            case "overflow-anchor": style.OverflowAnchor = value.ToLowerInvariant() == "none" ? OverflowAnchorType.None : OverflowAnchorType.Auto; break;
            case "overscroll-behavior": ApplyOverscrollBehavior(style, value); break;
            case "overscroll-behavior-x": style.OverscrollBehaviorX = ParseOverscrollBehavior(value); break;
            // Logical axes (CSS Overscroll Behavior 1 §3): for the horizontal writing
            // modes the engine supports, block maps to the y axis and inline to x.
            case "overscroll-behavior-block": style.OverscrollBehaviorY = ParseOverscrollBehavior(value); break;
            case "overscroll-behavior-inline": style.OverscrollBehaviorX = ParseOverscrollBehavior(value); break;
            // Legacy grid aliases (CSS Grid 1 §2.1.1, renamed by Grid 2): 'grid-gap'
            // takes <row-gap> <column-gap>, the same order as today's 'gap'.
            case "grid-gap": ParseGap(value, style); break;
            case "grid-row-gap": SetGap(style, value, row: true); break;
            case "grid-column-gap": SetGap(style, value, row: false); break;
            case "overscroll-behavior-y": style.OverscrollBehaviorY = ParseOverscrollBehavior(value); break;
            case "z-index": if (value != "auto") style.ZIndex = int.TryParse(value, out var z) ? z : null; break;
            case "border": ParseBorderShorthand(value, style); break;
            case "border-top": ParseBorderSide(style, "top", value); break;
            case "border-bottom": ParseBorderSide(style, "bottom", value); break;
            case "border-left": ParseBorderSide(style, "left", value); break;
            case "border-right": ParseBorderSide(style, "right", value); break;
            case "border-block-start": ParseBorderSide(style, "top", value); break;
            case "border-block-end": ParseBorderSide(style, "bottom", value); break;
            // The inline sides resolve against 'direction' (CSS Logical Properties 1 §2),
            // with a second mapping pass in StyleAdjuster.
            case "border-inline-start": ParseBorderSide(style, InlineStartSide(style), value); break;
            case "border-inline-end": ParseBorderSide(style, InlineEndSide(style), value); break;
            case "border-inline": ParseBorderSide(style, InlineStartSide(style), value); ParseBorderSide(style, InlineEndSide(style), value); break;
            case "border-block": ParseBorderSide(style, "top", value); ParseBorderSide(style, "bottom", value); break;
            case "border-inline-start-width": SetBorderWidthSide(style, InlineStartSide(style), value); break;
            case "border-inline-end-width": SetBorderWidthSide(style, InlineEndSide(style), value); break;
            case "border-inline-start-style": SetBorderStyleSide(style, InlineStartSide(style), value); break;
            case "border-inline-end-style": SetBorderStyleSide(style, InlineEndSide(style), value); break;
            case "border-inline-start-color": SetBorderColorSide(style, InlineStartSide(style), value); break;
            case "border-inline-end-color": SetBorderColorSide(style, InlineEndSide(style), value); break;
            case "border-inline-width": SetBorderWidthSide(style, InlineStartSide(style), value); SetBorderWidthSide(style, InlineEndSide(style), value); break;
            case "border-inline-style": SetBorderStyleSide(style, InlineStartSide(style), value); SetBorderStyleSide(style, InlineEndSide(style), value); break;
            case "border-inline-color": SetBorderColorSide(style, InlineStartSide(style), value); SetBorderColorSide(style, InlineEndSide(style), value); break;
            case "border-block-start-width": SetBorderWidthSide(style, "top", value); break;
            case "border-block-end-width": SetBorderWidthSide(style, "bottom", value); break;
            case "border-block-start-style": SetBorderStyleSide(style, "top", value); break;
            case "border-block-end-style": SetBorderStyleSide(style, "bottom", value); break;
            case "border-block-start-color": SetBorderColorSide(style, "top", value); break;
            case "border-block-end-color": SetBorderColorSide(style, "bottom", value); break;
            case "border-block-width": SetBorderWidthSide(style, "top", value); SetBorderWidthSide(style, "bottom", value); break;
            case "border-block-style": SetBorderStyleSide(style, "top", value); SetBorderStyleSide(style, "bottom", value); break;
            case "border-block-color": ParseBorderColorBothAxes(style, value); break;
            case "border-width": ParseBorderWidth(value, style); break;
            case "border-color": ParseBorderColor(value, style); break;
            case "border-style": ParseBorderStyle(value, style); break;
            case "border-top-width": style.BorderTopWidth = BorderWidthPx(value); style.AuthoredWidthSlots |= (uint)CurrentColorSlot.BorderTop; break;
            case "border-right-width": style.BorderRightWidth = BorderWidthPx(value); style.AuthoredWidthSlots |= (uint)CurrentColorSlot.BorderRight; break;
            case "border-bottom-width": style.BorderBottomWidth = BorderWidthPx(value); style.AuthoredWidthSlots |= (uint)CurrentColorSlot.BorderBottom; break;
            case "border-left-width": style.BorderLeftWidth = BorderWidthPx(value); style.AuthoredWidthSlots |= (uint)CurrentColorSlot.BorderLeft; break;
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
            // Logical corner radii (CSS Backgrounds 3 §5.3): 'start'/'end' name the
            // inline edges, so with 'direction: rtl' start-start is the TOP-RIGHT
            // corner. Queued with the other logical box properties so the mapping runs
            // once 'direction' is final (a 'direction' authored later still flips it).
            case "border-start-start-radius": ApplyRadiusPair(style, value, LogicalCorner(style, 0, 1)); break;
            case "border-start-end-radius": ApplyRadiusPair(style, value, LogicalCorner(style, 1, 0)); break;
            case "border-end-start-radius": ApplyRadiusPair(style, value, LogicalCorner(style, 3, 2)); break;
            case "border-end-end-radius": ApplyRadiusPair(style, value, LogicalCorner(style, 2, 3)); break;
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
            case "justify-content":
                {
                    var jc = CanonicalAlignmentValue(value);
                    if (jc == null) break;
                    style.JustifyContent = ParseJustifyContent(jc);
                    style.JustifyContentCssText = jc;
                }
                break;
            case "justify-items":
                {
                    var ji = CanonicalAlignmentValue(value);
                    if (ji != null) style.JustifyItems = ji;
                }
                break;
            case "justify-self":
                {
                    var js = CanonicalAlignmentValue(value);
                    if (js != null) style.JustifySelf = js;
                }
                break;
            case "align-items":
                {
                    var ai = CanonicalAlignmentValue(value);
                    if (ai == null) break;
                    style.AlignItems = ParseAlignItems(ai);
                    style.AlignItemsCssText = ai;
                }
                break;
            case "align-self":
                {
                    var asf = CanonicalAlignmentValue(value);
                    if (asf == null) break;
                    style.AlignSelf = ParseAlignSelf(asf);
                    style.AlignSelfCssText = asf;
                }
                break;
            case "align-content":
                {
                    var ac = CanonicalAlignmentValue(value);
                    if (ac != null) style.AlignContent = ac;
                }
                break;
            case "place-content":
                {
                    style.PlaceContent = value.ToLowerInvariant();
                    var (block, inline) = PlacePair(value);
                    ApplyPlace(v => { style.AlignContent = v; },
                        v => { style.JustifyContent = ParseJustifyContent(v); style.JustifyContentCssText = v; },
                        block, inline);
                }
                break;
            case "place-items":
                {
                    style.PlaceItems = value.ToLowerInvariant();
                    var (block, inline) = PlacePair(value);
                    ApplyPlace(v => { style.AlignItems = ParseAlignItems(v); style.AlignItemsCssText = v; },
                        v => style.JustifyItems = v, block, inline);
                }
                break;
            case "place-self":
                {
                    style.PlaceSelf = value.ToLowerInvariant();
                    var (block, inline) = PlacePair(value);
                    ApplyPlace(v => style.AlignSelf = ParseAlignSelf(v), v => style.JustifySelf = v, block, inline);
                }
                break;
            case "gap": ParseGap(value, style); break;
            case "row-gap": SetGap(style, value, row: true); break;
            case "column-gap": SetGap(style, value, row: false); break;
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
            case "grid-template-areas": style.GridTemplateAreas = CanonicalGridTemplateAreas(value); break;
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
            // Both origins are canonicalised to the two- (or three-)token form the reference
            // engine reports: each keyword is filed on its own axis as the percentage it stands
            // for, lengths become pixels, an unnamed axis keeps '50%', and a zero depth is
            // dropped. A percentage stays a percentage here — the box it resolves against
            // belongs to layout, which is what the CSSOM reports as the used value.
            case "transform-origin":
                if (IndividualTransforms.CanonicalOrigin(value, style, allowDepth: true) is { } originText)
                    style.TransformOrigin = originText;
                break;
            case "perspective-origin":
                if (IndividualTransforms.CanonicalOrigin(value, style, allowDepth: false) is { } perspectiveOriginText)
                    style.PerspectiveOrigin = perspectiveOriginText;
                break;
            // Individual transform properties (CSS Transforms 2 §4). Canonicalised once, here,
            // so that the paint path and the CSSOM read the same text rather than each re-reading
            // what the page typed; and a declaration the grammar rejects stores nothing, which is
            // how the reference engine treats it — it resets nothing.
            case "translate":
                if (IndividualTransforms.CanonicalTranslate(value, style) is { } translateText)
                    style.Translate = translateText;
                break;
            case "rotate":
                if (IndividualTransforms.CanonicalRotate(value) is { } rotateText)
                    style.Rotate = rotateText;
                break;
            case "scale":
                if (IndividualTransforms.CanonicalScale(value) is { } scaleText)
                    style.Scale = scaleText;
                break;
            case "perspective":
                if (IndividualTransforms.CanonicalPerspective(value, style) is { } perspectiveText)
                    style.Perspective = perspectiveText;
                break;
            case "backface-visibility":
                if (IndividualTransforms.TryBackfaceVisibility(value, out var backfaceKeyword))
                    style.BackfaceVisibility = backfaceKeyword;
                break;
            case "transform-style":
                if (IndividualTransforms.TryTransformStyle(value, out var styleKeyword))
                    style.TransformStyle = styleKeyword;
                break;
            case "transform-box":
                if (IndividualTransforms.TryTransformBox(value, out var boxKeyword))
                    style.TransformBox = boxKeyword;
                break;
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
            case "pointer-events":
                // CSS UI 4 §11. The whole keyword family is valid on any element (the SVG-only
                // members just behave like 'auto' outside SVG), and anything else is an invalid
                // declaration: the reference engine leaves the property at its initial 'auto'
                // rather than storing the typo.
                if (IsPointerEventsKeyword(value)) style.PointerEvents = value.Trim().ToLowerInvariant();
                break;
            case "user-select": style.UserSelect = value; break;
            case "text-indent":
                {
                    // [ each-line || hanging ] <length>  (CSS Text 3 §5.2)
                    var indentTokens = ShorthandExpander.SplitShorthand(value);
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
                        style.TextIndentMath = null;
                        if (ti is PercentLength tip)
                        {
                            style.TextIndent = 0;
                            style.TextIndentPercent = tip.Value;
                        }
                        else if (ti is MathLength mathLen)
                        {
                            // A calc() with a percentage must resolve against the
                            // containing block inline size, which is unknown here;
                            // defer the whole expression to line-break time.
                            style.TextIndent = 0;
                            style.TextIndentPercent = 0;
                            style.TextIndentMath = string.Join(" ", rest);
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
                // 'normal' is a value of its own, not a zero length: the two shape the text the
                // same but read back differently (CSS Text 4 §5.1).
                if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                {
                    style.LetterSpacing = 0;
                    style.LetterSpacingIsNormal = true;
                }
                else if (Length.TryParse(value, out var ls))
                {
                    style.LetterSpacing = ls.ToPixels(style.FontSize, style.FontSize, 0, 0);
                    style.LetterSpacingIsNormal = false;
                }
                break;
            case "word-spacing":
                if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                {
                    style.WordSpacing = 0;
                    style.WordSpacingIsNormal = true;
                }
                else if (Length.TryParse(value, out var ws))
                {
                    style.WordSpacing = ws.ToPixels(style.FontSize, style.FontSize, 0, 0);
                    style.WordSpacingIsNormal = false;
                }
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
            case "font-synthesis":
                if (TryParseFontSynthesis(value, out var synthesis)) style.FontSynthesis = synthesis;
                break;
            case "font-synthesis-weight":
                if (TryParseFontSynthesisLonghand(value, out var fsWeight))
                    style.FontSynthesis = SetFlag(style.FontSynthesis, FontSynthesisType.Weight, fsWeight);
                break;
            case "font-synthesis-style":
                if (TryParseFontSynthesisLonghand(value, out var fsStyle))
                    style.FontSynthesis = SetFlag(style.FontSynthesis, FontSynthesisType.Style, fsStyle);
                break;
            case "font-synthesis-small-caps":
                if (TryParseFontSynthesisLonghand(value, out var fsCaps))
                    style.FontSynthesis = SetFlag(style.FontSynthesis, FontSynthesisType.SmallCaps, fsCaps);
                break;
            case "font-optical-sizing": style.FontOpticalSizing = value.ToLowerInvariant(); break;
            case "font-variation-settings": style.FontVariationSettings = value; break;
            case "font-feature-settings": style.FontFeatureSettings = value; break;
            case "font-size-adjust": if (value != "none" && float.TryParse(value, out var fsa)) style.FontSizeAdjust = fsa; break;
            case "outline": ParseOutlineShorthand(value, style); break;
            case "outline-width":
                style.OutlineWidth = BorderWidthPx(value);
                style.AuthoredWidthSlots |= (uint)CurrentColorSlot.Outline;
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
                // The value is a ratio of two numbers, and both are kept: a box sized from
                // '1 / 2' and one sized from '0.5' lay out the same, but a script reading the
                // computed value gets the pair back, in the author's own numbers.
                if (value == "auto") { style.AspectRatio = 0; style.AspectRatioPair = null; }
                else if (value.Contains('/'))
                {
                    var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && float.TryParse(parts[0], out var aw) && float.TryParse(parts[1], out var ah) && ah > 0)
                    {
                        style.AspectRatio = aw / ah;
                        style.AspectRatioPair = $"{Acrux.Core.Dom.Animations.CssValueTokenizer.Num(aw)} / {Acrux.Core.Dom.Animations.CssValueTokenizer.Num(ah)}";
                    }
                }
                else if (float.TryParse(value, out var ar))
                {
                    style.AspectRatio = ar;
                    style.AspectRatioPair = ar > 0 ? $"{Acrux.Core.Dom.Animations.CssValueTokenizer.Num(ar)} / 1" : null;
                }
                break;
            case "object-fit": style.ObjectFit = ParseObjectFit(value); break;
            case "object-position": ParsePosition(value, out var opx, out var opy); style.ObjectPositionX = opx; style.ObjectPositionY = opy; break;
            case "filter": style.Filter = value; break;
            case "backdrop-filter": style.BackdropFilter = value; break;
            case "clip-path": style.ClipPath = value; break;
            case "mask": style.Mask = value; break;
            case "mask-image": style.MaskImage = value; break;
            // Each of these is a comma list of layers ('<layer>#'), so one bad layer
            // invalidates the WHOLE declaration — and the list is kept per layer so a mask with
            // three images and two positions can cycle them independently.
            case "mask-clip":
                ApplyMaskLayers(style, value, allowNoClip: true,
                    (v, one) => { v.MaskClip = one; }, (v, list) => { v.MaskClipLayers = list; },
                    entry => IsMaskBoxList(entry, allowNoClip: true));
                break;
            case "mask-origin":
                ApplyMaskLayers(style, value, allowNoClip: false,
                    (v, one) => { v.MaskOrigin = one; }, (v, list) => { v.MaskOriginLayers = list; },
                    entry => IsMaskBoxList(entry, allowNoClip: false));
                break;
            case "mask-composite":
                ApplyMaskLayers(style, value, allowNoClip: false,
                    (v, one) => { v.MaskComposite = one; }, (v, list) => { v.MaskCompositeLayers = list; },
                    entry => IsKeywordList(entry, "add", "subtract", "intersect", "exclude"));
                break;
            case "mask-mode":
                ApplyMaskLayers(style, value, allowNoClip: false,
                    (v, one) => { v.MaskMode = one; }, (v, list) => { v.MaskModeLayers = list; },
                    entry => IsKeywordList(entry, "match-source", "alpha", "luminance"));
                break;
            case "mask-position":
                // Position entries are validated loosely (the grammar is the background one);
                // what matters here is that the list survives, and the serializer resolves the
                // keywords to the percentages the reference engine reports.
                ApplyMaskLayers(style, value, allowNoClip: false,
                    (v, one) => { v.MaskPosition = one; }, (v, list) => { v.MaskPositionLayers = list; },
                    entry => entry.Length > 0 && !entry.Contains(","));
                break;
            case "mask-repeat":
                ApplyMaskLayers(style, value, allowNoClip: false,
                    (v, one) => { v.MaskRepeat = one; }, (v, list) => { v.MaskRepeatLayers = list; },
                    entry => IsMaskRepeat(entry));
                break;
            case "mask-size":
                ApplyMaskLayers(style, value, allowNoClip: false,
                    (v, one) => { v.MaskSize = one; }, (v, list) => { v.MaskSizeLayers = list; },
                    entry => IsMaskSize(entry));
                break;
            case "isolation": style.Isolation = value.ToLowerInvariant() == "isolate" ? IsolationType.Isolate : IsolationType.Auto; break;
            case "mix-blend-mode": style.MixBlendMode = ParseMixBlendMode(value); break;
            case "image-rendering": style.ImageRendering = ParseImageRendering(value); break;
            case "contain": style.Contain = ParseContain(value); break;
            case "content-visibility": style.ContentVisibility = ParseContentVisibility(value); break;
            case "contain-intrinsic-size": ApplyContainIntrinsicSize(style, value); break;
            case "contain-intrinsic-width": ApplyContainIntrinsicAxis(style, "width", value); break;
            case "contain-intrinsic-height": ApplyContainIntrinsicAxis(style, "height", value); break;
            case "contain-intrinsic-inline-size": ApplyContainIntrinsicAxis(style, "inline", value); break;
            case "contain-intrinsic-block-size": ApplyContainIntrinsicAxis(style, "block", value); break;
            case "will-change": style.WillChange = value; break;
            case "scroll-behavior": style.ScrollBehavior = value.ToLowerInvariant() == "smooth" ? ScrollBehaviorType.Smooth : ScrollBehaviorType.Auto; break;
            case "tab-size": ParseTabSize(style, value); break;
            case "hyphenate-character":
                // CSS Text 4 §5.1: 'auto' or a <string>. 'none' is not a value here, so
                // an invalid declaration must leave the property untouched (measured: Edge
                // reports 'auto' for a rule that says 'none').
                {
                    string hc = value.Trim();
                    if (hc.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        style.HyphenateCharacter = "auto";
                    else if (hc.Length >= 2 && (hc[0] == '"' || hc[0] == '\'')
                             && hc[^1] == hc[0])
                        style.HyphenateCharacter = UnescapeCssString(hc[1..^1]);
                }
                break;
            case "hyphens": style.Hyphens = ParseHyphens(value); break;
            case "ruby-position":
                // CSS Ruby 1 §4.3: the annotation sits over, under or between base and text;
                // 'left'/'right' are the vertical-writing-mode spellings. Any other keyword is
                // not a value here, so the declaration is dropped and the inherited position
                // stays.
                {
                    string rp = value.Trim().ToLowerInvariant();
                    if (rp is "over" or "under" or "inter-character" or "left" or "right")
                        style.RubyPosition = rp;
                }
                break;
            case "line-break": style.LineBreak = ParseLineBreak(value); break;
            case "text-justify": style.TextJustify = ParseTextJustify(value); break;
            case "hanging-punctuation": style.HangingPunctuation = value.ToLowerInvariant(); break;
            case "resize": style.Resize = ParseResize(value); break;
            case "field-sizing":
                // 'field-sizing: normal | content'. An unknown keyword (the draft's 'size'
                // among them) invalidates the declaration rather than resetting the axis, so
                // it must not be folded into the 'normal' branch.
                switch (value.Trim().ToLowerInvariant())
                {
                    case "normal": style.FieldSizing = FieldSizingType.Normal; break;
                    case "content": style.FieldSizing = FieldSizingType.Content; break;
                }
                break;
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
            // Both were empty stubs: the declaration was accepted (so nothing warned)
            // and then thrown away, which also silently disabled the multicol
            // 'orphans' floor that ColumnLayoutAlgorithm reads from this style.
            // CSS Text 3 §5.5.1/GCP: <integer> with a minimum of 1; anything below is
            // out of range and the declaration is dropped, keeping the inherited value.
            case "orphans":
                if (TryFragmentationCount(value, out var orphans)) style.Orphans = orphans;
                break;
            case "widows":
                if (TryFragmentationCount(value, out var widows)) style.Widows = widows;
                break;
        }
    }
    catch (FormatException) { /* Gracefully skip malformed CSS values */ }
    catch (OverflowException) { /* Skip values that are too large/small */ }
    catch (Exception) { /* Catch any other parsing errors */ }
    }


    public static void ParseShorthand4(string value, out Length top, out Length right, out Length bottom, out Length left)
    {
        var parts = ShorthandExpander.SplitShorthand(value);
        top = Length.Parse(parts.Count > 0 ? parts[0] : "0");
        right = Length.Parse(parts.Count > 1 ? parts[1] : parts[0]);
        bottom = Length.Parse(parts.Count > 2 ? parts[2] : parts[0]);
        left = Length.Parse(parts.Count > 3 ? parts[3] : (parts.Count > 1 ? parts[1] : parts[0]));
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


    /// <summary>Record 'font-size' together with whether it is still the default.
    /// Every path that assigns a font size (the longhand, the 'font' shorthand, the
    /// typed cascade, pseudo-element styles) must go through here, because the
    /// generic 'monospace' substitutes its own size only while the value has never
    /// been authored — and 'medium' is the initial keyword, so it does not count as
    /// authored (measured in Edge, see ComputedStyle.FontSizeIsDefault).</summary>
    public static void SetFontSize(ComputedStyle style, string? specified, float size)
    {
        style.FontSize = size;
        string s = specified?.Trim().ToLowerInvariant() ?? "";
        style.FontSizeIsDefault = s.Length == 0 || s == "medium" || s == "auto";
    }

    public static float ParseFontSize(string value, ComputedStyle? parentStyle,
        float rootFontSize = 16f, float viewportWidth = 0f, float viewportHeight = 0f)
    {
        float parentFontSize = parentStyle?.FontSize ?? 16;
        string v = value?.Trim() ?? "";
        // Font-relative units (cap/ex/ch/ic/lh and root variants) resolve against
        // the parent font's real metrics; viewport units (vw/vh/svh/lvh/dvh/…)
        // against the viewport; and a deferred math expression against the parent's
        // font size, which is the percentage base a 'font-size' has (CSS Fonts 4 §7.1).
        // Both go through the general Length path, which the string-slicing
        // ParseFontSize does not cover. The element's own font-size is not known yet,
        // so the unit context is the parent style.
        if ((IsMetricOrViewportLength(v) || IsMathFunction(v)) && parentStyle != null)
        {
            using var _scope = FontUnitContext.Use(parentStyle);
            float px = Length.Parse(v).ToPixels(parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            if (!float.IsNaN(px) && px > 0)
                return px;
        }
        return Length.ParseFontSize(v, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
    }

    /// <summary>True for a value whose arithmetic has to be evaluated before the unit is
    /// known — 'calc()', 'min()', 'max()', 'clamp()'. The cascade folds most of these itself;
    /// one that still carries a percentage reaches here, where the parent font size is the base.</summary>
    private static bool IsMathFunction(string v)
        => v.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
        || v.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
        || v.StartsWith("max(", StringComparison.OrdinalIgnoreCase)
        || v.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase);

    private static bool IsMetricOrViewportLength(string v)
    {
        foreach (var u in new[]
                 {
                     "cap", "ex", "ch", "ic", "lh", "rcap", "rex", "rch", "ric", "rlh",
                     "svw", "svh", "lvw", "lvh", "dvw", "dvh", "vi", "vb", "vmin", "vmax", "vw", "vh",
                 })
            if (v.EndsWith(u, StringComparison.OrdinalIgnoreCase) && v.Length > u.Length
                && (char.IsDigit(v[0]) || v[0] == '.' || v[0] == '+' || v[0] == '-'))
                return true;
        return false;
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
        // Keep the FULL font-family list so the renderer can fall back through every specified
        // family instead of only the first one. Consumers that need a single family take the
        // first entry and strip the quotes themselves.
        //
        // The list is kept in the author's own spelling — case and quotes included, the
        // families separated by ', ' — because that is what a reference engine prints back out
        // of getComputedStyle (measured: font-family: "Times New Roman", Georgia) and because a
        // family name is a name: lowering it is not a harmless normalisation when the platform
        // matcher is looking for the string a face was installed under.
        var list = ShorthandExpander.JoinFamilyList(value);
        return list.Length > 0 ? list : Acrux.Core.Fonts.FontManager.StandardFontFamily;
    }

    public static float ParseLineHeight(string value, float fontSize)
    {
        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px / fontSize;
        if (float.TryParse(value, out var num)) return num;
        return 1.2f;
    }

    /// <summary>
    /// The one place 'display' is written, so the flow-root marker can never drift from the
    /// display type it was parsed with (three call sites used to assign style.Display
    /// straight from the keyword list). A value the grammar rejects drops the whole
    /// declaration (CSS Values 3 §3.1): 'display: garbage' has to leave a span inline instead
    /// of silently blockifying it, which is what the old catch-all 'block' fallback did.
    /// </summary>
    public static void ApplyDisplay(ComputedStyle style, string value)
    {
        if (!TryParseDisplay(value, out var display, out var flowRoot)) return;
        style.Display = display;
        style.DisplayIsFlowRoot = flowRoot;
    }

    /// <summary>
    /// The 'display' grammar (CSS Display 3 §3). A single legacy keyword is still accepted;
    /// the modern form is an external keyword ('block' / 'inline'), one internal keyword
    /// ('flow', 'flow-root', 'table', 'flex', 'grid', 'ruby') and the 'list-item' addition,
    /// written in any order and with either the external or the internal one left out —
    /// 'block list-item', 'list-item block' and 'flow list-item' are the same box, and a bare
    /// 'flow' is 'block' (measured against the reference engine).
    /// </summary>
    /// <param name="flowRoot">
    /// 'flow-root' is reported as a marker next to the DisplayType instead of as a member of
    /// its own: ~70 layout sites test for DisplayType.Block, and 'display: inline flow-root'
    /// is an inline-block box in the reference engine anyway. The marker carries the distinct
    /// computed value and the new-formatting-context behaviour (CSS Display 3 §4).
    /// </param>
    public static bool TryParseDisplay(string value, out DisplayType display, out bool flowRoot)
    {
        if (TryParseDisplayParts(value, out var parts))
        {
            DisplayFromParts(parts, out display, out flowRoot);
            return true;
        }
        display = default;
        flowRoot = false;
        var tokens = SplitDisplayTokens(value);
        // The hyphenated legacy keywords ('none', 'contents', 'inline-block', 'table-row',
        // ...) have no modern spelling, so they are the only single-token values left.
        return tokens.Length == 1 && DisplayKeywords.TryGetValue(tokens[0], out display);
    }

    /// <summary>The three axes the modern grammar (CSS Display 3 §3) is written on: an
    /// external keyword, an internal keyword and the 'list-item' addition. The layout type
    /// and the canonical text are both derived from this, so they cannot drift apart.</summary>
    public readonly struct DisplayParts
    {
        public readonly bool InlineLevel;
        /// <summary>1 flow, 2 flow-root, 3 table, 4 flex, 5 grid, 6 ruby; never 0.</summary>
        public readonly int Internal;
        public readonly bool ListItem;

        public DisplayParts(bool inlineLevel, int @internal, bool listItem)
        {
            InlineLevel = inlineLevel;
            Internal = @internal;
            ListItem = listItem;
        }
    }

    /// <summary>Decodes the modern multi-keyword form. A single legacy keyword is not a
    /// parts value and is left to <see cref="TryParseDisplay"/>.</summary>
    public static bool TryParseDisplayParts(string value, out DisplayParts parts)
    {
        parts = default;
        var tokens = SplitDisplayTokens(value);
        if (tokens.Length == 0) return false;

        // 'run-in' has no box model in this engine, so it is not accepted.
        int external = 0;    // 1 = block, 2 = inline
        int internal_ = 0;   // 0 = absent, fills in 'flow' below
        bool listItem = false;
        foreach (var raw in tokens)
        {
            switch (raw.ToLowerInvariant())
            {
                case "block": if (external != 0) return false; external = 1; break;
                case "inline": if (external != 0) return false; external = 2; break;
                case "flow": if (internal_ != 0) return false; internal_ = 1; break;
                case "flow-root": if (internal_ != 0) return false; internal_ = 2; break;
                case "table": if (internal_ != 0) return false; internal_ = 3; break;
                case "flex": if (internal_ != 0) return false; internal_ = 4; break;
                case "grid": if (internal_ != 0) return false; internal_ = 5; break;
                case "ruby": if (internal_ != 0) return false; internal_ = 6; break;
                case "list-item": if (listItem) return false; listItem = true; break;
                default: return false;
            }
        }
        // An external keyword on its own is a legacy value, and two of anything the grammar
        // only allows once ('block block', 'flow flow-root') never reaches here. 'list-item'
        // needs an outer display type of 'flow' (CSS Display 3 §3.2), so it pairs with
        // 'flow'/'flow-root' only — 'flex list-item' is dropped, as the reference engine does.
        if (listItem && internal_ != 0 && internal_ != 1 && internal_ != 2) return false;
        if (internal_ == 0 && !listItem) return false;
        // Without an internal keyword the grammar fills in 'flow'; without an external one the
        // box is block-level.
        parts = new DisplayParts(external == 2, internal_ == 0 ? 1 : internal_, listItem);
        return true;
    }

    private static void DisplayFromParts(DisplayParts parts, out DisplayType display, out bool flowRoot)
    {
        flowRoot = parts.Internal == 2;
        // 'inline flow-root' is an inline-level atomic box that establishes a formatting
        // context, which is exactly 'inline-block'; the reference engine reports it as such.
        if (parts.InlineLevel && parts.Internal == 2)
        {
            display = DisplayType.InlineBlock;
            flowRoot = false;
            return;
        }
        display = parts.Internal switch
        {
            1 => parts.ListItem ? DisplayType.ListItem
                : parts.InlineLevel ? DisplayType.Inline : DisplayType.Block,
            2 => parts.ListItem ? DisplayType.ListItem : DisplayType.Block,
            3 => DisplayType.Table,
            4 => parts.InlineLevel ? DisplayType.InlineFlex : DisplayType.Flex,
            5 => parts.InlineLevel ? DisplayType.InlineGrid : DisplayType.Grid,
            6 => DisplayType.Ruby,
            _ => DisplayType.Block
        };
    }

    /// <summary>
    /// The text the reference engine serialises a 'display' declaration back as. It stores
    /// the parsed value rather than the author's characters, so keywords that were left out
    /// or written in another order come back in their canonical place, and a value a legacy
    /// keyword already spells stays spelled that one word (measured: 'display: block flow
    /// list-item' reads back as 'list-item', 'display: list-item flow-root' as
    /// 'flow-root list-item', 'display: FLOW' as 'block').
    /// </summary>
    public static bool TryCanonicalDisplayText(string value, out string text)
    {
        if (TryParseDisplayParts(value, out var parts))
        {
            text = CanonicalDisplayText(parts);
            return true;
        }
        var tokens = SplitDisplayTokens(value);
        if (tokens.Length == 1 && DisplayKeywords.TryGetValue(tokens[0], out _))
        {
            text = tokens[0].ToLowerInvariant();
            return true;
        }
        text = value;
        return false;
    }

    private static string CanonicalDisplayText(DisplayParts parts)
    {
        // 'block' is the default external type, so it never appears in the canonical text.
        bool inline = parts.InlineLevel;
        return parts.Internal switch
        {
            1 => parts.ListItem
                ? (inline ? "inline list-item" : "list-item")
                : (inline ? "inline" : "block"),
            2 => parts.ListItem
                ? (inline ? "inline flow-root list-item" : "flow-root list-item")
                : (inline ? "inline-block" : "flow-root"),
            3 => inline ? "inline-table" : "table",
            4 => inline ? "inline-flex" : "flex",
            5 => inline ? "inline-grid" : "grid",
            // 'ruby' has no inline-level spelling (measured: 'display: inline ruby' reads
            // back as 'ruby'), and 'list-item' cannot pair with it.
            _ => "ruby"
        };
    }

    private static string[] SplitDisplayTokens(string value) =>
        value.Split(new[] { ' ', '\t', '\r', '\n', '\f' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Single-keyword 'display' values, the set the legacy grammar accepts.</summary>
    private static readonly Dictionary<string, DisplayType> DisplayKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["block"] = DisplayType.Block,
            ["inline"] = DisplayType.Inline,
            ["inline-block"] = DisplayType.InlineBlock,
            ["flex"] = DisplayType.Flex,
            ["inline-flex"] = DisplayType.InlineFlex,
            ["grid"] = DisplayType.Grid,
            ["inline-grid"] = DisplayType.InlineGrid,
            ["list-item"] = DisplayType.ListItem,
            ["table"] = DisplayType.Table,
            // inline-table is laid out as a table box (block-level approximation;
            // the engine has no separate inline-table display type).
            ["inline-table"] = DisplayType.Table,
            ["table-row"] = DisplayType.TableRow,
            ["table-cell"] = DisplayType.TableCell,
            ["table-header-group"] = DisplayType.TableHeaderGroup,
            ["table-row-group"] = DisplayType.TableRowGroup,
            ["table-footer-group"] = DisplayType.TableFooterGroup,
            ["table-caption"] = DisplayType.TableCaption,
            ["table-column-group"] = DisplayType.TableColumnGroup,
            ["table-column"] = DisplayType.TableColumn,
            ["flow-root"] = DisplayType.Block,
            ["ruby"] = DisplayType.Ruby,
            ["none"] = DisplayType.None,
            ["contents"] = DisplayType.Contents,
        };

    public static PositionType ParsePosition(string value) => value.ToLowerInvariant() switch
    {
        "relative" => PositionType.Relative,
        "absolute" => PositionType.Absolute,
        "fixed" => PositionType.Fixed,
        "sticky" => PositionType.Sticky,
        _ => PositionType.Static
    };

    public static FloatType ParseFloat(string value) =>
        Acrux.Core.Dom.CssFloatKeywords.TryParseFloat(value, out var f) ? f : FloatType.None;

    public static ClearType ParseClear(string value) =>
        Acrux.Core.Dom.CssFloatKeywords.TryParseClear(value, out var c) ? c : ClearType.None;

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

    /// <summary>'white-space' is a shorthand of the collapse/mode/style triple
    /// (CSS Text 4 §1). The shorthand writes all three plus the effective mode layout
    /// consumes; a longhand writes its own field and re-derives the mode.</summary>
    private static void ApplyWhiteSpaceShorthand(ComputedStyle style, string value)
    {
        string v = value.Trim().ToLowerInvariant();
        string head = v.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "normal";
        switch (head)
        {
            case "nowrap":
                style.WhiteSpaceCollapse = WhiteSpaceCollapseType.Collapse;
                style.TextWrapMode = TextWrapModeType.NoWrap;
                style.WhiteSpace = WhiteSpaceMode.Nowrap;
                break;
            case "pre":
                style.WhiteSpaceCollapse = WhiteSpaceCollapseType.Preserve;
                style.TextWrapMode = TextWrapModeType.NoWrap;
                style.WhiteSpace = WhiteSpaceMode.Pre;
                break;
            case "pre-wrap":
                style.WhiteSpaceCollapse = WhiteSpaceCollapseType.Preserve;
                style.TextWrapMode = TextWrapModeType.Wrap;
                style.WhiteSpace = WhiteSpaceMode.PreWrap;
                break;
            case "pre-line":
                style.WhiteSpaceCollapse = WhiteSpaceCollapseType.PreserveBreaks;
                style.TextWrapMode = TextWrapModeType.Wrap;
                style.WhiteSpace = WhiteSpaceMode.PreLine;
                break;
            case "break-spaces":
                // Modelled as its own collapse value (see WhiteSpaceCollapseType): with
                // plain 'preserve' it would be indistinguishable from 'pre-wrap'.
                style.WhiteSpaceCollapse = WhiteSpaceCollapseType.BreakSpaces;
                style.TextWrapMode = TextWrapModeType.Wrap;
                style.WhiteSpace = WhiteSpaceMode.BreakSpaces;
                break;
            default: // 'normal'
                style.WhiteSpaceCollapse = WhiteSpaceCollapseType.Collapse;
                style.TextWrapMode = TextWrapModeType.Wrap;
                style.WhiteSpace = WhiteSpaceMode.Normal;
                break;
        }
        // 'white-space' also accepts the wrapping-style keywords of 'text-wrap'.
        if (v.Contains("balance")) style.TextWrapStyle = TextWrapStyleType.Balanced;
        else if (v.Contains("stable")) style.TextWrapStyle = TextWrapStyleType.Stable;
        else if (v.Contains("pretty")) style.TextWrapStyle = TextWrapStyleType.Pretty;
        else style.TextWrapStyle = TextWrapStyleType.Auto;
        SyncTextWrapString(style);
    }

    /// <summary>Re-derive the effective 'white-space' mode from the triple after a
    /// longhand changed. 'preserve' + 'wrap' is ambiguous by spec (it is both 'pre-wrap'
    /// and 'break-spaces'), so an already-effective 'break-spaces' is kept rather than
    /// silently downgraded — the shorthand is the only way to ask for it.</summary>
    private static void RecomputeEffectiveWhiteSpace(ComputedStyle style)
    {
        style.WhiteSpace = (style.WhiteSpaceCollapse, style.TextWrapMode) switch
        {
            (WhiteSpaceCollapseType.Collapse, TextWrapModeType.NoWrap) => WhiteSpaceMode.Nowrap,
            (WhiteSpaceCollapseType.Collapse, _) => WhiteSpaceMode.Normal,
            (WhiteSpaceCollapseType.Preserve, TextWrapModeType.NoWrap) => WhiteSpaceMode.Pre,
            (WhiteSpaceCollapseType.Preserve, _) => WhiteSpaceMode.PreWrap,
            (WhiteSpaceCollapseType.BreakSpaces, TextWrapModeType.NoWrap) => WhiteSpaceMode.Pre,
            (WhiteSpaceCollapseType.BreakSpaces, _) => WhiteSpaceMode.BreakSpaces,
            (WhiteSpaceCollapseType.PreserveBreaks, TextWrapModeType.NoWrap) => WhiteSpaceMode.Pre,
            (WhiteSpaceCollapseType.PreserveBreaks, _) => WhiteSpaceMode.PreLine,
            (WhiteSpaceCollapseType.Discard, TextWrapModeType.NoWrap) => WhiteSpaceMode.Nowrap,
            _ => WhiteSpaceMode.Normal,
        };
        SyncTextWrapString(style);
    }

    /// <summary>Layout reads the legacy 'text-wrap' string for 'balance'/'pretty'; keep
    /// it consistent with the modular fields so both spellings drive the same code.</summary>
    private static void SyncTextWrapString(ComputedStyle style) =>
        style.TextWrap = style.TextWrapStyle switch
        {
            TextWrapStyleType.Balanced => "balance",
            TextWrapStyleType.Pretty => "pretty",
            TextWrapStyleType.Stable => "stable",
            _ => style.TextWrapMode == TextWrapModeType.NoWrap ? "nowrap" : "normal",
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

    /// <summary>
    /// Store one of the comma-list mask longhands: validate every layer, keep the first layer in
    /// the scalar field (so single-layer masks keep their existing path) and the whole list when
    /// there is more than one. A single invalid layer rejects the entire declaration, which is
    /// what the '&lt;layer&gt;#' grammar means.
    /// </summary>
    private static void ApplyMaskLayers(ComputedStyle style, string value, bool allowNoClip,
        Action<ComputedStyle, string> setScalar, Action<ComputedStyle, List<string>?>? setList,
        Func<string, bool> validEntry)
    {
        var entries = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(value, ',');
        if (entries.Count == 0) return;
        foreach (var entry in entries)
            if (!validEntry(entry)) return;

        var normalized = entries.Select(e => e.Trim().ToLowerInvariant()).ToList();
        setScalar(style, normalized[0]);
        if (setList != null) setList(style, normalized.Count > 1 ? normalized : null);
    }

    /// <summary>'mask-size' (CSS Masking 1 §9): 'auto', one of the two fit keywords, or one or
    /// two lengths/percentages. Anything else is an invalid declaration and leaves the property
    /// alone — the reference engine reports 'auto' for 'mask-size: bogus', not the typo.</summary>
    public static bool IsMaskSize(string value)
    {
        var tokens = (value ?? string.Empty).Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length > 2) return false;
        foreach (var t in tokens)
        {
            var lower = t.ToLowerInvariant();
            if (lower is "auto" or "contain" or "cover") continue;
            // Length.Parse never answers null — an unknown word comes back as a length of zero —
            // so validity has to be decided on the token's shape, not by parsing it.
            if (!IsLengthOrPercentageToken(t)) return false;
        }
        // 'contain'/'cover' are single-keyword values and cannot stand beside anything.
        if (tokens.Length == 2 && tokens.Any(t => t.Equals("contain", StringComparison.OrdinalIgnoreCase)
                                               || t.Equals("cover", StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    /// <summary>'&lt;length&gt; | &lt;percentage&gt;' by shape: a sign, a number, then a known unit or '%'.
    /// Deliberately stricter than the length parser, which is forgiving by design and would read
    /// any bare word as zero.</summary>
    public static bool IsLengthOrPercentageToken(string token)
    {
        token = (token ?? string.Empty).Trim();
        if (token.Length == 0) return false;
        if (token.EndsWith("%", StringComparison.Ordinal))
            return double.TryParse(token[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        int i = 0;
        if (token[0] == '+' || token[0] == '-') i = 1;
        int digits = i;
        bool dot = false;
        for (; i < token.Length; i++)
        {
            if (char.IsAsciiDigit(token[i])) continue;
            if (token[i] == '.' && !dot) { dot = true; continue; }
            break;
        }
        if (i == digits) return false;                       // no number at all
        var number = token[..i];
        var unit = token[i..].ToLowerInvariant();
        if (double.TryParse(number, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _) is false) return false;
        return unit.Length == 0 ? double.TryParse(number, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var zero) && zero == 0   // CSS '0' without a unit
               : unit is "px" or "em" or "rem" or "ex" or "ch" or "vw" or "vh" or "vmin" or "vmax"
                   or "cm" or "mm" or "q" or "in" or "pt" or "pc" or "lh" or "rlh"
                   or "svw" or "svh" or "lvw" or "lvh" or "dvw" or "dvh"
                   or "vi" or "vb" or "cqw" or "cqh" or "cqi" or "cqb" or "cqmin" or "cqmax";
    }

    /// <summary>'mask-repeat' (CSS Masking 1 §9.3): per layer, one or two repeat keywords —
    /// the two-token form names the horizontal and vertical axis separately, and the reference
    /// engine collapses identical axes back to one keyword on output.</summary>
    public static bool IsMaskRepeat(string value)
    {
        var layers = SplitCommaTokens(value);
        if (layers.Count == 0) return false;
        var allowed = new[] { "repeat-x", "repeat-y", "repeat", "no-repeat", "space", "round" };
        foreach (var layer in layers)
        {
            var axes = layer.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (axes.Length is 0 or > 2) return false;
            // 'repeat-x'/'repeat-y' are single-axis values and take no second token.
            if (axes.Length == 2
                && (axes[0].Equals("repeat-x", StringComparison.OrdinalIgnoreCase)
                    || axes[0].Equals("repeat-y", StringComparison.OrdinalIgnoreCase)))
                return false;
            if (!axes.All(a => allowed.Any(k => a.Equals(k, StringComparison.OrdinalIgnoreCase)))) return false;
        }
        return true;
    }

    /// <summary>A comma list drawn from one keyword set (each layer may name its own value).</summary>
    public static bool IsKeywordList(string value, params string[] allowed) =>
        SplitCommaTokens(value).Count > 0
        && SplitCommaTokens(value).All(t => allowed.Any(a => t.Equals(a, StringComparison.OrdinalIgnoreCase)));

    /// <summary>'mask-origin' / 'mask-clip': a comma list of geometry boxes. Only 'mask-clip'
    /// also accepts 'no-clip' (CSS Masking 1 §9.1/§11).</summary>
    public static bool IsMaskBoxList(string value, bool allowNoClip)
    {
        var boxes = new[] { "border-box", "padding-box", "content-box", "fill-box", "stroke-box", "view-box" };
        var tokens = SplitCommaTokens(value);
        return tokens.Count > 0 && tokens.All(t =>
            (allowNoClip && t.Equals("no-clip", StringComparison.OrdinalIgnoreCase))
            || boxes.Any(b => t.Equals(b, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> SplitCommaTokens(string value) =>
        (value ?? string.Empty).Split(',')
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();

    /// <summary>The keyword set of 'pointer-events' (CSS UI 4 §11 plus SVG 2's
    /// 'bounding-box'). Anything else is an invalid declaration rather than a value, so the
    /// property stays at its initial 'auto' — the reference engine drops the typo.</summary>
    public static bool IsPointerEventsKeyword(string value) =>
        value.Trim().ToLowerInvariant() is "auto" or "none" or "visiblepainted" or "visiblefill"
            or "visiblestroke" or "visible" or "painted" or "fill" or "stroke" or "all"
            or "bounding-box";

    /// <summary>Which values make the box a scroll container (CSS Overflow 3 §3.3):
    /// 'visible' and 'clip' do not; 'hidden', 'auto' and 'scroll' do.</summary>
    internal static bool IsScrollContainerOverflow(OverflowType o) =>
        o is OverflowType.Hidden or OverflowType.Auto or OverflowType.Scroll;

    /// <summary>Ordering used to collapse a mixed pair into the single 'Overflow'
    /// field. 'clip' sits between 'visible' and 'hidden': it clips, but scrolls on
    /// neither axis.</summary>
    internal static int OverflowSeverity(OverflowType o) => o switch
    {
        OverflowType.Visible => 0,
        OverflowType.Clip => 1,
        OverflowType.Hidden => 2,
        _ => 3,
    };

    public static OverflowType ParseOverflow(string value) => value.ToLowerInvariant() switch
    {
        "hidden" => OverflowType.Hidden,
        "scroll" => OverflowType.Scroll,
        "auto" => OverflowType.Auto,
        // CSS Overflow 3 §3.3. Before this case existed, 'clip' fell through to
        // 'Visible' — so the declaration parsed, matched, and the box stopped clipping
        // at all, which is the opposite of what the keyword asks for.
        "clip" => OverflowType.Clip,
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
        // CSS Position 3 §5.2 through the one matcher the shorthand's <bg-position> uses as
        // well, so the two cannot drift: 'left 10px' is the pair (0%, 10px) while
        // 'left 10px top 20px' is an edge with the offset measured from it. A token list the
        // grammar does not read leaves the box on the position it already had.
        if (!ShorthandExpander.TryMatchBackgroundPosition(
                ShorthandExpander.SplitShorthand(value), out var x, out var y))
            return;
        style.BackgroundPositionX = ResolvePositionAxis(x, horizontal: true);
        style.BackgroundPositionY = ResolvePositionAxis(y, horizontal: false);
    }

    /// <summary>The offset one matched axis of a &lt;position&gt; carries. An axis the value never
    /// mentions is the middle of the box, a near edge without an offset is its start, a far edge
    /// without one is 100%, and a far edge with an offset is the distance measured inwards from
    /// it — which the reference engine prints as the arithmetic it is ('right 10px' is
    /// 'calc(100% - 10px)') except when the offset is a percentage, which folds onto the near
    /// edge while it is still a fraction ('right 10%' is '90%', measured).</summary>
    private static Length? ResolvePositionAxis(ShorthandExpander.PositionAxis axis, bool horizontal)
    {
        if (!axis.Present) return new PercentLength(0.5f);
        if (axis.Edge == null) return ParseLengthToken(axis.Offset!);
        if (IsPositionKeyword(axis.Edge, "center")) return new PercentLength(0.5f);
        var far = horizontal
            ? IsPositionKeyword(axis.Edge, "right")
            : IsPositionKeyword(axis.Edge, "bottom");
        if (!far) return axis.Offset == null ? new PercentLength(0) : ParseLengthToken(axis.Offset);
        if (axis.Offset == null) return new PercentLength(1);
        var offset = ParseLengthToken(axis.Offset);
        return offset is PercentLength percent
            ? new PercentLength(1f - percent.Value)
            : new FarEdgeLength(offset);
    }

    private static bool IsPositionKeyword(string token, string keyword) =>
        token.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>A &lt;length-percentage&gt; the position grammar has already accepted. CSS units are
    /// case-insensitive — '10PX' is the 10px the reference engine reports — and this engine's
    /// length parser reads only the lowercase spellings, so the token is put into that shape
    /// first; a math function keeps its own spelling, which is what it reads back as.</summary>
    private static Length ParseLengthToken(string token) => Length.Parse(token.ToLowerInvariant());

    /// <summary>'background-position' as the longhand it is: a list with one entry per layer,
    /// and it writes BOTH axis lists, because the two axes are longhands of their own that the
    /// position shorthand fills (CSS Backgrounds 3 §4.1.1.1).</summary>
    private static void ApplyBackgroundPositionLonghand(string value, ComputedStyle style)
    {
        var xs = new List<Length?>();
        var ys = new List<Length?>();
        foreach (var layer in SplitCommaOutsideParens(value))
        {
            var scratch = new ComputedStyle();
            ParseBackgroundPosition(layer.Trim(), scratch);
            xs.Add(scratch.BackgroundPositionX);
            ys.Add(scratch.BackgroundPositionY);
        }
        if (xs.Count == 0) return;
        style.BackgroundPositionXLayers = xs.Count > 1 ? xs : null;
        style.BackgroundPositionYLayers = ys.Count > 1 ? ys : null;
        style.BackgroundPositionX = xs[0];
        style.BackgroundPositionY = ys[0];
        SyncBackgroundPositionLayers(style);
    }

    /// <summary>'background-position-x' / '-y': one axis's own list, leaving the other axis of
    /// every layer exactly as the box had it (measured: a box positioned '7px 9px' that is then
    /// given 'background-position-x: 90%' reads back as '90% 9px').</summary>
    private static void ApplyBackgroundPositionAxisLonghand(string value, ComputedStyle style, bool horizontal)
    {
        var axisValues = new List<Length?>();
        foreach (var layer in SplitCommaOutsideParens(value))
        {
            if (!ShorthandExpander.TryMatchBackgroundPositionAxis(layer.Trim(), horizontal, out var axis))
                return;
            axisValues.Add(ResolvePositionAxis(axis, horizontal));
        }
        if (axisValues.Count == 0) return;
        var list = axisValues.Count > 1 ? axisValues : null;
        if (horizontal)
        {
            style.BackgroundPositionXLayers = list;
            style.BackgroundPositionX = axisValues[0];
        }
        else
        {
            style.BackgroundPositionYLayers = list;
            style.BackgroundPositionY = axisValues[0];
        }
        SyncBackgroundPositionLayers(style);
    }

    /// <summary>The per-layer pairs the painter indexes, rebuilt from the two axis lists: each
    /// axis cycles against the other on its own (CSS Backgrounds 3 §2), so a box with
    /// 'background-position-x: 10px, 20px' and 'background-position-y: 5px' positions its layers
    /// '10px 5px, 20px 5px'.</summary>
    private static void SyncBackgroundPositionLayers(ComputedStyle style)
    {
        var xs = style.BackgroundPositionXLayers;
        var ys = style.BackgroundPositionYLayers;
        int count = Math.Max(xs?.Count ?? 1, ys?.Count ?? 1);
        if (count < 2)
        {
            style.BackgroundPositionLayers = null;
            return;
        }
        var layers = new List<BackgroundPositionLayer>(count);
        for (int i = 0; i < count; i++)
            layers.Add(new BackgroundPositionLayer
            {
                X = xs == null ? style.BackgroundPositionX : xs[i % xs.Count],
                Y = ys == null ? style.BackgroundPositionY : ys[i % ys.Count],
            });
        style.BackgroundPositionLayers = layers;
    }

    public static void ParseBackgroundSize(string value, ComputedStyle style)
    {
        if (IsPositionKeyword(value, "cover")) { style.BackgroundSize = BackgroundSizeType.Cover; return; }
        if (IsPositionKeyword(value, "contain")) { style.BackgroundSize = BackgroundSizeType.Contain; return; }
        if (IsPositionKeyword(value, "auto")) { style.BackgroundSize = BackgroundSizeType.Auto; return; }

        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
        if (parts.Length > 0 && !IsPositionKeyword(parts[0], "auto"))
            style.BackgroundSizeWidth = ParseLengthToken(parts[0]);
        if (parts.Length > 1 && !IsPositionKeyword(parts[1], "auto"))
            style.BackgroundSizeHeight = ParseLengthToken(parts[1]);
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

    /// <summary>
    /// 'background' as the cascade applies it: the longhands the shorthand expands to, written
    /// through the same applier a longhand declaration goes through. The layer model — one
    /// position, size, repeat and box per image layer, each cycling against the others — lives
    /// in those appliers, so a shorthand parsed by a second grammar of its own can only drift
    /// from it. Measured: 'background: right 20px bottom 10px / 30px 30px …' used to paint the
    /// layer at the position its size had been read into, with the size itself lost.
    /// </summary>
    public static void ParseBackgroundShorthand(string value, ComputedStyle style)
    {
        var expanded = ShorthandExpander.ExpandProperty("background", value);
        // Nothing came out because the value's grammar rejected it, and a declaration the
        // grammar rejects never existed: it resets none of the longhands either.
        if (expanded.Count == 0) return;
        for (int i = 0; i < BackgroundLonghands.Length; i++)
            Apply(style, BackgroundLonghands[i],
                expanded.TryGetValue(BackgroundLonghands[i], out var text) ? text : BackgroundInitials[i]);
    }

    /// <summary>The longhands 'background' owns (CSS Backgrounds 3 §4), in the order they are
    /// applied — the two position axes before the box properties, which do not read them. The
    /// axes are what the shorthand writes, never the pair: 'background-position' is itself a
    /// shorthand of them (§4.1.1.1) and applying the pair here would let a
    /// 'background-position-x' from anywhere in the same block be overwritten by it.</summary>
    private static readonly string[] BackgroundLonghands =
    {
        "background-image", "background-color", "background-repeat", "background-attachment",
        "background-position-x", "background-position-y", "background-size",
        "background-origin", "background-clip",
    };

    /// <summary>The initial value of each of them, which is what the shorthand leaves behind for
    /// a layer it does not describe (CSS 2.1 §14.3).</summary>
    private static readonly string[] BackgroundInitials =
    {
        "none", "transparent", "repeat", "scroll", "0%", "0%", "auto", "padding-box", "border-box",
    };

    /// <summary>
    /// Take the first layer of a comma-separated background longhand. The scalar
    /// position/size/repeat/attachment fields can only hold one, and handing them the
    /// whole list produced a degenerate parse (nothing painted) rather than a
    /// first-layer approximation.
    /// </summary>
    private static string FirstBackgroundLayer(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf(',') < 0)
            return value;
        var layers = System.Linq.Enumerable.ToArray(SplitCommaOutsideParens(value));
        for (int i = 0; i < layers.Length; i++)
        {
            string layer = layers[i].Trim();
            if (layer.Length > 0)
                return layer;
        }
        return value;
    }

    /// <summary>
    /// Parse a comma-separated background longhand into one entry per layer.
    /// Returns null for a single-value list on purpose: the scalar fields already hold
    /// that value, so one-layer backgrounds (nearly all of them) keep using the exact
    /// same code path and allocate nothing. A property whose single value the scalar cannot
    /// hold — 'background-repeat' keeps two axes — asks for the list whatever its length.
    /// </summary>
    private static List<T>? ParseBackgroundLayerList<T>(string value, Func<string, T> parseOne,
        bool always = false)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var layers = SplitCommaOutsideParens(value).ToArray();
        if (!always && layers.Length < 2 && value.IndexOf(',') < 0) return null;
        List<T>? list = null;
        foreach (var layer in layers)
        {
            string entry = layer.Trim();
            if (entry.Length == 0) continue;
            list ??= new List<T>();
            list.Add(parseOne(entry));
        }
        return list is { Count: > 1 } || always ? list : null;
    }

    private static BackgroundSizeLayer ParseOneBackgroundSize(string value)
    {
        var scratch = new ComputedStyle();
        ParseBackgroundSize(value, scratch);
        return new BackgroundSizeLayer
        {
            Type = scratch.BackgroundSize,
            Width = scratch.BackgroundSizeWidth,
            Height = scratch.BackgroundSizeHeight,
        };
    }

    private static BackgroundRepeatPair ParseOneBackgroundRepeatPair(string value)
    {
        var parts = ShorthandExpander.SplitShorthand(value);
        if (parts.Count >= 2)
            return new BackgroundRepeatPair
            {
                X = ParseBackgroundRepeat(parts[0]),
                Y = ParseBackgroundRepeat(parts[1]),
            };
        // 'repeat-x' and 'repeat-y' name the PAIR they stand for rather than a mode each axis
        // repeats, so a pair kept as (RepeatX, RepeatX) could not be told apart from 'repeat'.
        var single = ParseBackgroundRepeat(value);
        return single switch
        {
            BackgroundRepeat.RepeatX => new BackgroundRepeatPair
            { X = BackgroundRepeat.Repeat, Y = BackgroundRepeat.NoRepeat },
            BackgroundRepeat.RepeatY => new BackgroundRepeatPair
            { X = BackgroundRepeat.NoRepeat, Y = BackgroundRepeat.Repeat },
            _ => new BackgroundRepeatPair { X = single, Y = single },
        };
    }

    /// <summary>The one keyword the scalar field carries for a pair of axes: two axes that agree
    /// collapse to that mode, and the pair 'repeat no-repeat' is what 'repeat-x' means (CSS
    /// Backgrounds 3 §4.1.1); a mixed pair that no single word describes keeps the horizontal
    /// mode, which is the tiling the painter can still act on.</summary>
    private static BackgroundRepeat FoldRepeatPair(BackgroundRepeatPair pair) => (pair.X, pair.Y) switch
    {
        (var x, var y) when x == y => x,
        (BackgroundRepeat.Repeat, BackgroundRepeat.NoRepeat) => BackgroundRepeat.RepeatX,
        (BackgroundRepeat.NoRepeat, BackgroundRepeat.Repeat) => BackgroundRepeat.RepeatY,
        _ => pair.X,
    };

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
        var parts = ShorthandExpander.SplitShorthand(value);
        bool hasWidth = false;
        bool hasColor = false;
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
                style.AuthoredWidthSlots |= (uint)CurrentColorSlot.AllBorders;
                hasWidth = true;
            }
            else
            {
                var color = ColorParser.Parse(part, style);
                style.BorderTopColor = color; style.BorderRightColor = color;
                style.BorderBottomColor = color; style.BorderLeftColor = color;
                MarkCurrentColor(style, CurrentColorSlot.AllBorders, part);
                hasColor = true;
            }
        }

        // The border shorthand resets border-width to its initial value (medium) when
        // no width token is present, so 'border: solid' paints a 3px border.
        if (!hasWidth)
        {
            style.BorderTopWidth = style.BorderRightWidth =
                style.BorderBottomWidth = style.BorderLeftWidth = MediumBorderWidth;
            style.AuthoredWidthSlots |= (uint)CurrentColorSlot.AllBorders;
        }

        // The initial border-color is currentcolor, so a shorthand that carries no
        // colour paints the element's text colour rather than a stored black.
        if (!hasColor)
            MarkCurrentColor(style, CurrentColorSlot.AllBorders, "currentcolor");
    }

    /// <summary>A border-width token is one of the keywords or a length the width
    /// parser understands. A leading sign or digit covers the unitless and bare
    /// numbers ('0', '.5', '2px') that 'border: 0' relies on; treating '0' as
    /// neither a width nor a style let the shorthand's medium reset re-inflate it.
    /// A math function is a length as well, so 'border: calc(2px + 1px) solid' is
    /// not mistaken for a colour.</summary>
    internal static bool IsBorderWidthToken(string part) =>
        part is "thin" or "medium" or "thick"
        || (part.Length > 0 && (char.IsAsciiDigit(part[0]) || part[0] is '.' or '+' or '-'))
        || IsLengthFunction(part);

    /// <summary>The width slot a per-side border property addresses. The same bit set
    /// records both currentcolour and authored widths because both are per side.</summary>
    private static CurrentColorSlot SideWidthSlot(string side) => side switch
    {
        "top" => CurrentColorSlot.BorderTop,
        "bottom" => CurrentColorSlot.BorderBottom,
        "left" => CurrentColorSlot.BorderLeft,
        _ => CurrentColorSlot.BorderRight,
    };

    private static bool IsLengthFunction(string part)
    {
        int open = part.IndexOf('(');
        if (open <= 0) return false;
        var name = part.AsSpan(0, open);
        return name.Equals("calc", StringComparison.OrdinalIgnoreCase)
            || name.Equals("min", StringComparison.OrdinalIgnoreCase)
            || name.Equals("max", StringComparison.OrdinalIgnoreCase)
            || name.Equals("clamp", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>The alignment keywords the six alignment properties share (CSS Box Alignment 3
    /// §4, §6, §7), with the <c>safe</c>/<c>unsafe</c> overflow qualifier a position keyword may
    /// carry. Anything else — a length, a typo — is not a value these properties have, and the
    /// declaration must then be dropped rather than stored as the box's alignment.</summary>
    private static readonly HashSet<string> AlignmentKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "normal", "auto", "stretch", "start", "end", "flex-start", "flex-end", "center",
        "left", "right", "baseline", "first baseline", "last baseline",
        "space-between", "space-around", "space-evenly",
        // The legacy family is 'justify-items' only (CSS Box Alignment 3 §8), but the six
        // properties share one keyword table here.
        "legacy", "legacy-left", "legacy-right", "legacy-center",
    };

    /// <summary>The canonical spelling of an alignment value: lower-case, whitespace collapsed,
    /// and null when the token is not an alignment keyword. The property stores this text because
    /// its computed value is the keyword the page used — 'end' answers 'end' and an authored
    /// 'flex-end' answers 'flex-end' (measured), which a single enum could not tell apart.</summary>
    private static string? CanonicalAlignmentValue(string value)
    {
        // Collapse the runs of space a multi-word keyword may be written with, so that
        // 'first  baseline' and 'first baseline' are the same value.
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            }
            else sb.Append(c);
        }
        if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        var text = sb.ToString();
        if (text.Length == 0) return null;
        // The qualifier is only legal in front of a position keyword, so it is split off and the
        // remainder checked on its own.
        foreach (var qualifier in new[] { "safe ", "unsafe " })
        {
            if (text.StartsWith(qualifier, StringComparison.OrdinalIgnoreCase))
            {
                var rest = text[qualifier.Length..];
                return AlignmentKeywords.Contains(rest) && rest is not ("normal" or "auto" or "stretch")
                    ? text.ToLowerInvariant()
                    : null;
            }
        }
        return AlignmentKeywords.Contains(text) ? text.ToLowerInvariant() : null;
    }

    /// <summary>Splits a 'place-*' value into its block and inline side, the inline side
    /// defaulting to the block one (CSS Box Alignment 3 §6). A side that is not an alignment
    /// keyword makes the whole declaration invalid.</summary>
    private static (string? block, string? inlineAxis) PlacePair(string value)
    {
        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
        if (parts.Length == 0) return (null, null);
        var block = CanonicalAlignmentValue(parts[0]);
        var inlineAxis = parts.Length > 1 ? CanonicalAlignmentValue(parts[1]) : block;
        return block == null || inlineAxis == null ? (null, null) : (block, inlineAxis);
    }

    /// <summary>Writes the two sides of a 'place-*' value onto the pair of longhands it stands
    /// for. Both sides are forwarded as written, because 'normal' means something different to
    /// each of them: it is the initial of the block axis, while the inline axis of 'place-items'
    /// reads it as 'legacy' (CSS Box Alignment 3 §6).</summary>
    private static void ApplyPlace(Action<string> setBlockAxis, Action<string> setInlineAxis,
        string? block, string? inlineAxis)
    {
        if (block == null || inlineAxis == null) return;
        setBlockAxis(block);
        setInlineAxis(inlineAxis);
    }

    public static void ParseBorderSide(ComputedStyle style, string side, string value)
    {
        // A per-side shorthand resets the components it leaves out, so it classifies
        // its tokens exactly like the 'border' shorthand does and writes all three
        // aspects onto the side the property name selected.
        bool hasWidth = false;
        bool hasColor = false;
        foreach (var part in ShorthandExpander.SplitShorthand(value))
        {
            if (part is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge"
                or "inset" or "outset" or "none" or "hidden")
            {
                var bs = ParseBorderStyleValue(part);
                if (side == "top") style.BorderTopStyle = bs;
                else if (side == "bottom") style.BorderBottomStyle = bs;
                else if (side == "left") style.BorderLeftStyle = bs;
                else style.BorderRightStyle = bs;
            }
            else if (IsBorderWidthToken(part))
            {
                // Any length unit (em, %, calc) and the thin/medium/thick keywords are
                // widths; 'EndsWith("px")' used to drop all but pixel widths.
                var width = BorderWidthPx(part);
                if (side == "top") style.BorderTopWidth = width;
                else if (side == "bottom") style.BorderBottomWidth = width;
                else if (side == "left") style.BorderLeftWidth = width;
                else style.BorderRightWidth = width;
                style.AuthoredWidthSlots |= (uint)SideWidthSlot(side);
                hasWidth = true;
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
                else style.BorderRightColor = color;
                hasColor = true;
            }
        }

        if (!hasWidth)
        {
            if (side == "top") style.BorderTopWidth = MediumBorderWidth;
            else if (side == "bottom") style.BorderBottomWidth = MediumBorderWidth;
            else if (side == "left") style.BorderLeftWidth = MediumBorderWidth;
            else style.BorderRightWidth = MediumBorderWidth;
            style.AuthoredWidthSlots |= (uint)SideWidthSlot(side);
        }

        if (!hasColor)
        {
            var resetSlot = side switch
            {
                "top" => CurrentColorSlot.BorderTop,
                "bottom" => CurrentColorSlot.BorderBottom,
                "left" => CurrentColorSlot.BorderLeft,
                _ => CurrentColorSlot.BorderRight,
            };
            MarkCurrentColor(style, resetSlot, "currentcolor");
        }
    }

    /// <summary>
    /// 'border-spacing' takes one or two lengths: the first is the column (inline-axis)
    /// gap, the second - when present - the row (block-axis) gap (CSS 2.1 §17.5).
    /// </summary>
    public static void ApplyBorderSpacing(ComputedStyle style, string value)
    {
        var parts = ShorthandExpander.SplitShorthand(value);
        float inlineSpacing = ParseSize(parts.Count > 0 ? parts[0] : value) ?? 0;
        style.BorderSpacing = Math.Max(0, inlineSpacing);
        style.BorderRowSpacing = parts.Count > 1 ? Math.Max(0, ParseSize(parts[1]) ?? inlineSpacing) : null;
    }

    public static void ParseBorderWidth(string value, ComputedStyle style)
    {
        var widths = ShorthandExpander.SplitShorthand(value);
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
        style.AuthoredWidthSlots |= (uint)CurrentColorSlot.AllBorders;
    }

    public static void ParseBorderColor(string value, ComputedStyle style)
    {
        var colors = ShorthandExpander.SplitShorthand(value).ToArray();
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
        var tokens = ShorthandExpander.SplitShorthand(value);
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

    /// <summary>
    /// Resolve a &lt;length&gt; that a consumer stores in pixels. Every unit the length
    /// parser understands works (calc, viewport, ch/ex/...), and font-relative units
    /// take the element's own font through the ambient FontUnitContext that
    /// <see cref="Apply"/> installs. Percentages are not lengths, so they stay
    /// unresolved and the caller keeps its fallback.
    /// </summary>
    public static float? ParseSize(string value)
    {
        string text = value.Trim();
        if (text.Length == 0) return null;
        if (text.EndsWith('%')) return null;

        if (Length.TryParse(text, out var length) && length != null)
        {
            float font = FontUnitContext.Current?.FontSize ?? 16f;
            float px = length.ToPixels(font, font, 0, 0);
            if (!float.IsNaN(px)) return px;
        }
        return null;
    }

    /// <summary>Resolve a border-width token: the CSS keywords thin/medium/thick map
    /// to 1/3/5px (CSS Backgrounds 3 §4), otherwise a length via <see cref="ParseSize"/>.
    /// The border shorthand emits per-side width longhands, so this must understand
    /// the keywords, not just lengths.
    /// The result is quantised on the device pixel grid because that is the width both
    /// layout and painting use: 2px at a scale of 1.25 becomes 1.6 CSS px (two device
    /// pixels), while a non-zero width never collapses below one device pixel.</summary>
    public static float BorderWidthPx(string value)
    {
        float width = value.Trim().ToLowerInvariant() switch
        {
            "thin" => 1f,
            "medium" => 3f,
            "thick" => 5f,
            _ => ParseSize(value) ?? 0f,
        };
        return QuantizeToDevices(width);
    }

    /// <summary>'medium' is both a keyword and the border/outline shorthand's initial
    /// width, so the reset paths take the quantised value too.</summary>
    public static float MediumBorderWidth => QuantizeToDevices(3f);

    /// <summary>A width of zero stays zero; anything else takes at least one device
    /// pixel, floored on the grid (Chrome's border computation).</summary>
    private static float QuantizeToDevices(float width)
    {
        if (width <= 0f) return 0f;
        float scale = Fonts.FontMetricsProvider.DeviceScale;
        if (scale <= 0f || float.IsNaN(scale)) return width;
        float device = MathF.Floor(width * scale);
        if (device < 1f) device = 1f;
        return device / scale;
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
        // The pair is 'horizontal vertical'; only a top-level space separates them,
        // so 'calc(10px + 2px)' survives as one token.
        var tokens = ShorthandExpander.SplitShorthand(value.Trim());
        if (tokens.Count == 0) return null;
        string first = tokens[0];
        if (first.EndsWith('%') && first.Length > 1 &&
            float.TryParse(first[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return -pct;
        return ParseSize(first);
    }

    /// <summary>'border-*-radius: &lt;horizontal&gt; [&lt;vertical&gt;]' for one corner
    /// (0 = top-left, 1 = top-right, 2 = bottom-right, 3 = bottom-left).</summary>
    private static void ApplyRadiusPair(ComputedStyle style, string value, int corner)
    {
        var tokens = ShorthandExpander.SplitShorthand(value).ToArray();
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
        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
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
            {
                style.ListStyleTypeString = text[1..^1];
                style.ListStyleTypeName = null;
            }
            return ListStyleType.String;
        }
        // A keyword form supersedes a string or a custom name the element picked up
        // earlier; the marker generator reads whichever is set, so they have to go.
        if (style != null)
        {
            style.ListStyleTypeString = null;
            style.ListStyleTypeName = null;
        }
        string lower = text.ToLowerInvariant();
        if (MatchListStyleType(lower) is { } preset)
            return preset;

        // Every other ident names a @counter-style rule (CSS Counter Styles §3.2). The
        // name is kept rather than resolved here: the rule can live in a sheet the
        // cascade has already folded into this value, and an unknown name still has to
        // render as decimal rather than as the initial 'disc'.
        if (style != null && IsCounterStyleName(text))
        {
            style.ListStyleTypeName = text;
            return ListStyleType.Custom;
        }
        return ListStyleType.Disc;
    }

    /// <summary>A &lt;custom-ident&gt; that can name a counter style: an identifier that is
    /// not a CSS-wide keyword and does not collide with a predefined style name.</summary>
    public static bool IsCounterStyleName(string text)
    {
        if (text.Length == 0 || char.IsAsciiDigit(text[0]) || text is "-" or "_")
            return false;
        if (text is "auto" or "inherit" or "initial" or "unset" or "revert" or "none")
            return false;
        foreach (char c in text)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '\u00B7' || c > '\u007F')
                continue;
            return false;
        }
        return true;
    }

    /// <summary>The 'list-style-type' keyword table (CSS Lists 3 §5, §A). Null for a
    /// token that is not a type at all, which lets the 'list-style' shorthand tell a
    /// missing part from an unsupported keyword.</summary>
    public static ListStyleType? MatchListStyleType(string keyword) => keyword.ToLowerInvariant() switch
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
        "armenian" or "upper-armenian" => ListStyleType.Armenian,
        "lower-armenian" => ListStyleType.LowerArmenian,
        "georgian" => ListStyleType.Georgian,
        "hebrew" => ListStyleType.Hebrew,
        "ethiopic-numeric" => ListStyleType.EthiopicNumeric,
        "disclosure-open" => ListStyleType.DisclosureOpen,
        "disclosure-closed" => ListStyleType.DisclosureClosed,
        "hiragana" => ListStyleType.Hiragana,
        "katakana" => ListStyleType.Katakana,
        "hiragana-iroha" => ListStyleType.HiraganaIroha,
        "katakana-iroha" => ListStyleType.KatakanaIroha,
        "cjk-decimal" => ListStyleType.CjkDecimal,
        "cjk-ideographic" or "trad-chinese-informal" => ListStyleType.TradChineseInformal,
        "trad-chinese-formal" => ListStyleType.TradChineseFormal,
        "simp-chinese-informal" => ListStyleType.SimpChineseInformal,
        "simp-chinese-formal" => ListStyleType.SimpChineseFormal,
        "japanese-informal" => ListStyleType.JapaneseInformal,
        "japanese-formal" => ListStyleType.JapaneseFormal,
        "korean-hangul-formal" => ListStyleType.KoreanHangulFormal,
        "korean-hanja-informal" => ListStyleType.KoreanHanjaInformal,
        "korean-hanja-formal" => ListStyleType.KoreanHanjaFormal,
        "cjk-earthly-branch" => ListStyleType.CjkEarthlyBranch,
        "cjk-heavenly-stem" => ListStyleType.CjkHeavenlyStem,
        "thai" => ListStyleType.Thai,
        "lao" => ListStyleType.Lao,
        "khmer" or "cambodian" => ListStyleType.Khmer,
        "myanmar" or "burmese" => ListStyleType.Myanmar,
        "mongolian" => ListStyleType.Mongolian,
        "arabic-indic" => ListStyleType.ArabicIndic,
        "persian" or "urdu" => ListStyleType.Persian,
        "devanagari" => ListStyleType.Devanagari,
        "bengali" => ListStyleType.Bengali,
        "tamil" => ListStyleType.Tamil,
        "telugu" => ListStyleType.Telugu,
        "gujarati" => ListStyleType.Gujarati,
        "gurmukhi" => ListStyleType.Gurmukhi,
        "kannada" => ListStyleType.Kannada,
        "malayalam" => ListStyleType.Malayalam,
        "oriya" => ListStyleType.Oriya,
        "tibetan" => ListStyleType.Tibetan,
        "canadian-aboriginal" => ListStyleType.CanadianAboriginal,
        "symbol" => ListStyleType.Symbol,
        "none" => ListStyleType.None,
        _ => null,
    };

    /// <summary>'list-style' is a shorthand for position, image and type
    /// (CSS Lists 3 §4.5); parts it does not carry are reset to their initial value.</summary>
    public static void ParseListStyle(string value, ComputedStyle style)
    {
        var parts = ShorthandExpander.SplitShorthand(value);
        bool sawType = false;
        style.ListStylePosition = ListStylePosition.Outside;
        style.ListStyleImage = "none";
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "inside" or "outside")
                style.ListStylePosition = lower == "inside" ? ListStylePosition.Inside : ListStylePosition.Outside;
            else if (lower.StartsWith("url("))
                style.ListStyleImage = NormalizeUrlValue(part);
            else if (!sawType && (MatchListStyleType(lower) is { } type || IsQuotedString(part)
                                  || IsCounterStyleName(part)))
            {
                style.ListStyleType = ParseListStyleType(part, style);
                sawType = true;
            }
        }

        // The shorthand resets every component it does not carry, so 'list-style: inside'
        // computes the marker type back to its initial 'disc' and forgets a custom string.
        if (!sawType)
        {
            style.ListStyleType = ListStyleType.Disc;
            style.ListStyleTypeString = null;
            style.ListStyleTypeName = null;
        }
    }

    /// <summary>'text-overflow' accepts the two keywords plus a &lt;string&gt; that replaces
    /// the ellipsis (CSS UI 4 &#167;4.4). The two-value form addresses the block axis with
    /// its second value, which this engine does not clip separately, so the first value
    /// wins. 'no-ellipsis' asks for plain clipping.</summary>
    public static void ParseTextOverflow(string value, ComputedStyle style)
    {
        style.TextOverflowString = null;
        style.TextOverflow = TextOverflowType.Clip;

        string text = value.Trim();
        if (text.Length == 0) return;

        if (text[0] == '"' || text[0] == '\'')
        {
            int close = IndexOfClosingQuote(text, text[0]);
            if (close < 0) return;
            string marker = UnescapeCssString(text[1..close]);
            if (marker.Length == 0)
                return; // An empty replacement asks for plain clipping.
            style.TextOverflowString = marker;
            style.TextOverflow = TextOverflowType.Ellipsis;
            return;
        }

        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0].Equals("ellipsis", StringComparison.OrdinalIgnoreCase))
            style.TextOverflow = TextOverflowType.Ellipsis;
        // 'clip' and 'no-ellipsis' both clip without a marker.
    }

    private static int IndexOfClosingQuote(string text, char quote)
    {
        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == quote) return i;
        }
        return -1;
    }

    /// <summary>Resolve the escapes a quoted CSS &lt;string&gt; may carry: a backslash
    /// before a literal character, and up to six hex digits for a codepoint optionally
    /// followed by one space that belongs to the escape rather than to the text.</summary>
    internal static string UnescapeCssString(string text)
    {
        if (text.IndexOf('\\') < 0) return text;
        var outText = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                outText.Append(c);
                continue;
            }
            i++;
            char next = text[i];
            if (!IsHexDigit(next))
            {
                outText.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    _ => next,
                });
                continue;
            }
            int code = 0, taken = 0;
            while (i < text.Length && taken < 6 && IsHexDigit(text[i]))
            {
                code = code * 16 + HexValue(text[i]);
                i++;
                taken++;
            }
            // A space right after the digits terminates the escape; it is not content.
            if (taken == 6 && i < text.Length && text[i] == ' ') i++;
            if (code is > 0 and <= 0x10FFFF)
                outText.Append(char.ConvertFromUtf32(code));
        }
        return outText.ToString();
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static int HexValue(char c) =>
        c <= '9' ? c - '0' : char.ToLowerInvariant(c) - 'a' + 10;

    private static bool IsQuotedString(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length >= 2 && ((trimmed[0] == '"' && trimmed[^1] == '"')
            || (trimmed[0] == '\'' && trimmed[^1] == '\''));
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
            SetFontSize(style, parts[i], Length.ParseFontSize(parts[i], style.FontSize));
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
            style.FontFamily = ParseFontFamily(string.Join(" ", parts.Skip(i)));
    }

    public static void ParseOutlineShorthand(string value, ComputedStyle style)
    {
        bool hasWidth = false;
        bool hasStyle = false;
        foreach (var part in ShorthandExpander.SplitShorthand(value))
        {
            if (part is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge"
                or "inset" or "outset" or "none" or "hidden")
            {
                style.OutlineStyle = ParseBorderStyleValue(part);
                hasStyle = true;
            }
            else if (IsBorderWidthToken(part))
            {
                // Keywords, any length unit and calc() are all widths.
                style.OutlineWidth = BorderWidthPx(part);
                style.AuthoredWidthSlots |= (uint)CurrentColorSlot.Outline;
                hasWidth = true;
            }
            else
            {
                style.OutlineColor = ColorParser.Parse(part, style);
                MarkCurrentColor(style, CurrentColorSlot.Outline, part);
            }
        }

        // 'outline' resets the components it does not carry: width back to medium and
        // style back to none (CSS UI 4 §5), so 'outline: 3px' draws nothing.
        if (!hasWidth)
        {
            style.OutlineWidth = MediumBorderWidth;
            style.AuthoredWidthSlots |= (uint)CurrentColorSlot.Outline;
        }
        if (!hasStyle) style.OutlineStyle = BorderStyle.None;
    }

    public static void ParseShorthand2(string value, out Length a, out Length b)
    {
        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
        a = Length.Parse(parts.Length > 0 ? parts[0] : "0");
        b = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
    }

    public static Length? ParsePositionKeywordOrLength(string value)
    {
        // The edge keywords are the percentages they lay out as (CSS Backgrounds 3 §4.1.1), not
        // pixel offsets: 'left' reads back as '0%' on the computed surface (measured), and a pixel
        // zero would print as '0px' instead.
        return value.ToLowerInvariant() switch
        {
            "left" or "top" => new PercentLength(0),
            "center" => new PercentLength(0.5f),
            "right" or "bottom" => new PercentLength(1),
            _ => Length.TryParse(value, out var l) ? l : null
        };
    }

    public static void ParsePosition(string value, out Length? x, out Length? y)
    {
        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
        x = parts.Length > 0 ? ParsePositionKeywordOrLength(parts[0]) : null;
        y = parts.Length > 1 ? ParsePositionKeywordOrLength(parts[1]) : null;
    }

    public static void ParseInsetShorthand(string value, ComputedStyle style)
    {
        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
        if (parts.Length == 0) return;
        style.Top = Length.Parse(parts[0]);
        style.Right = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
        style.Bottom = Length.Parse(parts.Length > 2 ? parts[2] : parts[0]);
        style.Left = Length.Parse(parts.Length > 3 ? parts[3] : (parts.Length > 1 ? parts[1] : parts[0]));
    }

    /// <summary>The logical box properties whose physical target depends on
    /// 'direction' (CSS Logical Properties 1 §2). They are applied immediately (so a
    /// style that never reaches the adjuster still gets the LTR answer) and queued for
    /// a second pass once 'direction' is final. The physical longhands they alias are
    /// deliberately not queued: they need no re-mapping, and the cascade resolves each
    /// property id independently (StyleCascade.ApplyIfPresent → TryGetWinner) before
    /// walking ids in enum order, so authoring order between an alias pair is already
    /// lost by the time Apply runs — queueing the physical side could not restore it
    /// (residual gap #205).</summary>
    private static readonly System.Collections.Generic.HashSet<string> BoxEdgeProperties =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            "margin-inline", "margin-inline-start", "margin-inline-end",
            "padding-inline", "padding-inline-start", "padding-inline-end",
            "border-inline", "border-inline-start", "border-inline-end",
            "border-inline-width", "border-inline-style", "border-inline-color",
            "border-inline-start-width", "border-inline-start-style", "border-inline-start-color",
            "border-inline-end-width", "border-inline-end-style", "border-inline-end-color",
            "inset-inline", "inset-inline-start", "inset-inline-end",
            "border-start-start-radius", "border-start-end-radius",
            "border-end-start-radius", "border-end-end-radius",
        };

    /// <summary>'orphans' / 'widows' take an &lt;integer&gt; of at least 1; a decimal or a
    /// value below 1 is out of range, so the caller keeps the inherited value.</summary>
    private static bool TryFragmentationCount(string value, out int result)
    {
        result = 0;
        string v = value.Trim();
        if (!int.TryParse(v, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return false;
        if (n < 1) return false;
        result = n;
        return true;
    }

    private static bool IsLogicalBoxProperty(string name) => BoxEdgeProperties.Contains(name);

    private static void QueueLogicalProperty(ComputedStyle style, string name, string value)
    {
        var list = style.PendingBoxEdgeProperties ??= new();
        string lower = name.ToLowerInvariant();
        for (int i = list.Count - 1; i >= 0; i--)
        {
            // One entry per property name, the last-authored value winning.
            if (list[i].Key.Equals(lower, StringComparison.OrdinalIgnoreCase))
                list.RemoveAt(i);
        }
        list.Add(new System.Collections.Generic.KeyValuePair<string, string>(lower, value));
    }

    /// <summary>Physical side of the inline-start edge: 'direction: rtl' puts it on
    /// the right (CSS Logical Properties 1 §2). 'writing-mode' is not supported yet
    /// (gap #131), so the block axis keeps its physical top/bottom mapping.</summary>
    /// <summary>Physical corner index for a logical corner name. Corners are numbered
    /// 0=top-left, 1=top-right, 2=bottom-right, 3=bottom-left; 'direction: rtl' swaps
    /// the inline pair. The block axis keeps its physical meaning because 'writing-mode'
    /// is not supported yet (gap #131).</summary>
    private static int LogicalCorner(ComputedStyle style, int ltrCorner, int rtlCorner) =>
        style.Direction == "rtl" ? rtlCorner : ltrCorner;

    private static string InlineStartSide(ComputedStyle style) => style.Direction == "rtl" ? "right" : "left";
    private static string InlineEndSide(ComputedStyle style) => style.Direction == "rtl" ? "left" : "right";

    /// <summary>Set one margin side by physical name.</summary>
    private static void SetMarginSide(ComputedStyle style, string side, Length value)
    {
        switch (side)
        {
            case "top": style.MarginTop = value; break;
            case "bottom": style.MarginBottom = value; break;
            case "left": style.MarginLeft = value; break;
            default: style.MarginRight = value; break;
        }
    }

    private static void SetPaddingSide(ComputedStyle style, string side, Length value)
    {
        switch (side)
        {
            case "top": style.PaddingTop = value; break;
            case "bottom": style.PaddingBottom = value; break;
            case "left": style.PaddingLeft = value; break;
            default: style.PaddingRight = value; break;
        }
    }

    /// <summary>One border width on one physical side, mirroring the
    /// 'border-&lt;side&gt;-width' longhand (the authored slot is recorded so
    /// 'ApplyInitialBorderWidths' can turn a style without a width into 'medium').</summary>
    private static void SetBorderWidthSide(ComputedStyle style, string side, string value)
    {
        var w = BorderWidthPx(value);
        switch (side)
        {
            case "top": style.BorderTopWidth = w; break;
            case "bottom": style.BorderBottomWidth = w; break;
            case "left": style.BorderLeftWidth = w; break;
            default: style.BorderRightWidth = w; break;
        }
        style.AuthoredWidthSlots |= (uint)SideWidthSlot(side);
    }

    private static void SetBorderStyleSide(ComputedStyle style, string side, string value)
    {
        var bs = ParseBorderStyleValue(value);
        switch (side)
        {
            case "top": style.BorderTopStyle = bs; break;
            case "bottom": style.BorderBottomStyle = bs; break;
            case "left": style.BorderLeftStyle = bs; break;
            default: style.BorderRightStyle = bs; break;
        }
    }

    private static void SetBorderColorSide(ComputedStyle style, string side, string value)
    {
        var color = ColorParser.Parse(value, style);
        var slot = SideWidthSlot(side);
        MarkCurrentColor(style, slot, value);
        switch (side)
        {
            case "top": style.BorderTopColor = color; break;
            case "bottom": style.BorderBottomColor = color; break;
            case "left": style.BorderLeftColor = color; break;
            default: style.BorderRightColor = color; break;
        }
    }

    /// <summary>'border-block-color: &lt;a&gt; &lt;b&gt;' addresses block-start then
    /// block-end, which for the supported horizontal writing modes are top/bottom.
    /// Split on the raw text: these are colors, so they never go through Length.</summary>
    private static void ParseBorderColorBothAxes(ComputedStyle style, string value)
    {
        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
        if (parts.Length == 0) return;
        string start = parts[0];
        string end = parts.Length > 1 ? parts[1] : parts[0];
        SetBorderColorSide(style, "top", start);
        SetBorderColorSide(style, "bottom", end);
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

    /// <summary>'text-decoration-color: auto | &lt;color&gt;' (CSS Text Decoration 4
    /// §3.2). 'auto' is a distinct state, not a color: it paints with the element's
    /// own 'color', while an authored 'transparent' is explicit ink that must draw
    /// nothing — keying that distinction on the alpha channel conflated the two and
    /// made every zero-alpha spelling paint the text color.</summary>
    private static void ApplyTextDecorationColor(ComputedStyle style, string value)
    {
        var token = value.Trim();
        if (token.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            style.TextDecorationColorIsAuto = true;
            style.TextDecorationColor = default;
            // Clear any stale currentColor slot: 'auto' is resolved at use time, and
            // leaving the slot set would re-make the color explicit in the adjuster.
            MarkCurrentColor(style, CurrentColorSlot.TextDecoration, token);
            return;
        }
        style.TextDecorationColorIsAuto = false;
        style.TextDecorationColor = ColorParser.Parse(token, style);
        MarkCurrentColor(style, CurrentColorSlot.TextDecoration, token);
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
        style.TextDecorationColorIsAuto = true;
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
            ApplyTextDecorationColor(style, part);
        }
        style.TextDecoration = LegacyTextDecorationOf(style.TextDecorationLine);
    }

    /// <summary>'text-shadow' is none | &lt;shadow&gt;# with
    /// <c>[ &lt;color&gt;? &amp;&amp; &lt;length&gt;{2,3} ]</c> (CSS Text Decoration 4 §4.1). Each
    /// comma-separated shadow is classified on its own, so a colour may lead or trail the two
    /// or three lengths and a list of shadows keeps all of its members.</summary>
    public static List<TextShadowValue> ParseTextShadow(string value)
    {
        var shadows = new List<TextShadowValue>();
        if (string.IsNullOrEmpty(value) || value.Trim() == "none") return shadows;

        foreach (var component in SplitCommaOutsideParens(value))
        {
            var parts = ShorthandExpander.SplitShorthand(component.Trim()).ToArray();
            var lengths = new List<float>(3);
            var color = new List<string>(4);
            foreach (var raw in parts)
            {
                var token = raw.Trim();
                if (!ColorParser.LooksLikeColor(token) && ParseSize(token) is float px)
                {
                    if (lengths.Count < 3) lengths.Add(px);
                    continue;
                }
                color.Add(token);
            }
            if (lengths.Count is < 2 or > 3 || color.Count > 1) continue;
            var shadowColor = color.Count > 0
                ? ColorParser.Parse(string.Join(" ", color))
                : new SKColor(0, 0, 0, 255);
            shadows.Add(new TextShadowValue(shadowColor, lengths[0], lengths[1],
                lengths.Count > 2 ? lengths[2] : 0f));
        }
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
        // columns: <column-width> || <column-count>. Each component takes at most
        // one token, so a second length ("92px 8px") or a second integer is a
        // syntax error and the whole declaration is dropped: the longhands keep
        // whatever an earlier cascade origin gave them. Parsing into locals first
        // is what makes that possible - writing as we go would already have reset
        // the pair by the time the bad token shows up.
        int count = 0;
        Length? width = null;
        bool hasCount = false;
        bool hasWidth = false;
        foreach (var token in ShorthandExpander.SplitShorthand(value))
        {
            string part = token.Trim();
            if (part.Length == 0)
                continue;
            if (part.Equals("auto", StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(part, out int parsed))
            {
                if (hasCount || parsed <= 0)
                    return;
                hasCount = true;
                count = parsed;
                continue;
            }
            var length = Length.Parse(part);
            if (hasWidth || length == null)
                return;
            hasWidth = true;
            width = length;
        }

        style.ColumnCount = count;
        style.ColumnWidth = width;
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
            style.GridTemplateAreas = CanonicalGridTemplateAreas(string.Join(",", areaRows));
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

    /// <summary>
    /// CSS Containment 3 §2: 'contain' is a keyword list, so 'size layout' is the union of
    /// two bits rather than a sixth state. The whole declaration is invalid when a keyword
    /// repeats or is unknown (reference engine: 'contain: size size' and 'contain: auto'
    /// both compute to 'none'), and the two set keywords 'strict'/'content' only stand alone.
    /// </summary>
    public static ContainType ParseContain(string value)
    {
        var tokens = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return ContainType.None;
        if (tokens.Length == 1)
        {
            if (tokens[0] == "none") return ContainType.None;
            if (tokens[0] == "strict") return ContainType.Strict;
            if (tokens[0] == "content") return ContainType.Content;
        }
        ContainType result = ContainType.None;
        foreach (var token in tokens)
        {
            ContainType bit = token switch
            {
                "size" => ContainType.Size,
                "layout" => ContainType.Layout,
                "style" => ContainType.Style,
                "paint" => ContainType.Paint,
                _ => ContainType.None,
            };
            if (bit == ContainType.None || (result & bit) != 0) return ContainType.None;
            result |= bit;
        }
        return result;
    }

    /// <summary>The computed value in the reference engine's canonical order — size, layout,
    /// style, paint — collapsing the full sets back to the 'strict'/'content' keywords.</summary>
    public static string FormatContain(ContainType contain)
    {
        if (contain == ContainType.None) return "none";
        if (contain == ContainType.Strict) return "strict";
        if (contain == ContainType.Content) return "content";
        var parts = new List<string>(4);
        if ((contain & ContainType.Size) != 0) parts.Add("size");
        if ((contain & ContainType.Layout) != 0) parts.Add("layout");
        if ((contain & ContainType.Style) != 0) parts.Add("style");
        if ((contain & ContainType.Paint) != 0) parts.Add("paint");
        return string.Join(' ', parts);
    }

    /// <summary>
    /// 'font-synthesis' (CSS Fonts 4 §6.1) as the reference engine implements it: 'none' alone,
    /// or a duplicate-free list of the three category keywords. Measured value by value: the
    /// 'auto' keyword and the 'bold'/'italic'/'oblique' compatibility spellings are rejected in
    /// the shorthand (they never reach the computed value), 'none' may not appear beside a
    /// category, and a repeated category invalidates the whole declaration. Case-insensitive,
    /// any run of spaces separates the list.
    /// </summary>
    public static bool TryParseFontSynthesis(string value, out FontSynthesisType flags)
    {
        flags = FontSynthesisType.None;
        var tokens = (value ?? string.Empty).Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        if (tokens.Length == 1 && tokens[0].Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var token in tokens)
        {
            FontSynthesisType bit = token.ToLowerInvariant() switch
            {
                "weight" => FontSynthesisType.Weight,
                "style" => FontSynthesisType.Style,
                "small-caps" => FontSynthesisType.SmallCaps,
                _ => (FontSynthesisType)0,
            };
            if (bit == 0 || (flags & bit) != 0) { flags = FontSynthesisType.Auto; return false; }
            flags |= bit;
        }
        return true;
    }

    /// <summary>A 'font-synthesis-*' longhand: 'auto' turns that one synthesis on, 'none' off.</summary>
    public static bool TryParseFontSynthesisLonghand(string value, out bool on)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "auto": on = true; return true;
            case "none": on = false; return true;
            default: on = false; return false;
        }
    }

    private static FontSynthesisType SetFlag(FontSynthesisType flags, FontSynthesisType bit, bool on) =>
        on ? flags | bit : flags & ~bit;

    /// <summary>The computed value in the reference engine's canonical order. 'auto' is the
    /// initial state but never the serialization — the engine lists the three categories.</summary>
    public static string FormatFontSynthesis(FontSynthesisType synthesis)
    {
        if (synthesis == FontSynthesisType.None) return "none";
        var parts = new List<string>(3);
        if ((synthesis & FontSynthesisType.Weight) != 0) parts.Add("weight");
        if ((synthesis & FontSynthesisType.Style) != 0) parts.Add("style");
        if ((synthesis & FontSynthesisType.SmallCaps) != 0) parts.Add("small-caps");
        return string.Join(' ', parts);
    }

    /// <summary>'contain-intrinsic-size': one or two per-axis sizes, optionally prefixed by
    /// the remembered-size 'auto' that applies to both axes. A bare 'auto' is invalid (the
    /// reference engine computes it to 'none'), as is any percentage.</summary>
    private static void ApplyContainIntrinsicSize(ComputedStyle style, string value)
    {
        var tokens = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return;
        int index = 0;
        bool bothAuto = false;
        if (tokens[0] == "auto")
        {
            bothAuto = true;
            index = 1;
            if (tokens.Length == 1) return;
        }
        int remaining = tokens.Length - index;
        if (remaining is < 1 or > 2) return;
        if (tokens.Any(t => t != "none" && t != "auto" && t.Contains('%'))) return;

        var inlineAxis = ParseContainIntrinsicToken(tokens[index]);
        var blockAxis = remaining == 2 ? ParseContainIntrinsicToken(tokens[index + 1]) : inlineAxis;
        if (inlineAxis == null || blockAxis == null) return;
        if (bothAuto)
        {
            inlineAxis = (inlineAxis.Value.Size, true);
            blockAxis = (blockAxis.Value.Size, true);
        }
        SetContainIntrinsic(style, inlineAxis, blockAxis);
    }

    /// <summary>The four longhands. 'inline'/'block' are the logical pair and land on the
    /// physical axis the writing mode selects; 'width'/'height' are unambiguous.</summary>
    private static void ApplyContainIntrinsicAxis(ComputedStyle style, string axis, string value)
    {
        var tokens = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return;
        int index = 0;
        bool auto = false;
        if (tokens[0] == "auto")
        {
            auto = true;
            index = 1;
            if (tokens.Length == 1) return;
        }
        if (tokens.Length - index != 1) return;
        if (tokens[^1].Contains('%')) return;
        var parsed = ParseContainIntrinsicToken(tokens[^1]);
        if (parsed == null) return;
        parsed = (parsed.Value.Size, parsed.Value.Auto || auto);
        bool vertical = style.WritingMode is WritingModeType.VerticalRl or WritingModeType.VerticalLr;
        switch (axis)
        {
            case "width": SetContainIntrinsic(style, parsed, null); break;
            case "height": SetContainIntrinsic(style, null, parsed); break;
            case "inline": SetContainIntrinsic(style, vertical ? null : parsed, vertical ? parsed : null); break;
            default: SetContainIntrinsic(style, vertical ? parsed : null, vertical ? null : parsed); break;
        }
    }

    /// <summary>One axis value: 'none', or a single non-percentage length. Anything else
    /// (a garbage token, which <see cref="Length.Parse"/> reports as 'auto') is invalid and
    /// makes the caller drop the whole declaration.</summary>
    private static (Length? Size, bool Auto)? ParseContainIntrinsicToken(string token)
    {
        if (token == "none") return (null, false);
        var length = Length.Parse(token);
        return length is AutoLength or IntrinsicLength ? null : (length, false);
    }

    /// <summary>Null for an axis the caller does not touch; a non-null tuple with a null
    /// <c>Size</c> is the explicit 'none', which resets the axis to no fallback.</summary>
    private static void SetContainIntrinsic(ComputedStyle style,
        (Length? Size, bool Auto)? inlineAxis, (Length? Size, bool Auto)? blockAxis)
    {
        if (inlineAxis != null)
        {
            style.ContainIntrinsicWidth = inlineAxis.Value.Size;
            style.ContainIntrinsicWidthIsAuto = inlineAxis.Value.Auto;
        }
        if (blockAxis != null)
        {
            style.ContainIntrinsicHeight = blockAxis.Value.Size;
            style.ContainIntrinsicHeightIsAuto = blockAxis.Value.Auto;
        }
    }

    /// <summary>The computed text of the contain-intrinsic pair, e.g. 'auto 30px'.</summary>
    public static string FormatContainIntrinsic(ComputedStyle style)
    {
        string Axis(Length? size, bool auto)
        {
            if (size == null) return auto ? "auto none" : "none";
            return (auto ? "auto " : "") + size.ToCssString();
        }
        var inlineText = Axis(style.ContainIntrinsicWidth, style.ContainIntrinsicWidthIsAuto);
        var blockText = Axis(style.ContainIntrinsicHeight, style.ContainIntrinsicHeightIsAuto);
        return inlineText == blockText ? inlineText : $"{inlineText} {blockText}";
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

    /// <summary>'overscroll-behavior' is the two-axis shorthand (CSS Overscroll Behavior 1 §3):
    /// one keyword sets both axes, two set the x axis and then the y axis. Reading the whole
    /// value as a single keyword left 'contain none' at the initial 'auto' on both.</summary>
    public static void ApplyOverscrollBehavior(ComputedStyle style, string value)
    {
        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
        var x = parts.Length > 0 ? ParseOverscrollBehavior(parts[0]) : OverscrollBehaviorType.Auto;
        var y = parts.Length > 1 ? ParseOverscrollBehavior(parts[1]) : x;
        style.OverscrollBehaviorX = x;
        style.OverscrollBehaviorY = y;
        // The scalar the engine carried before the axes were modelled stays the x axis, which
        // is what one keyword always meant for it.
        style.OverscrollBehavior = x;
    }

    public static float ParseZoom(string value)
    {
        if (value.EndsWith("%") && float.TryParse(value[..^1], out var pct)) return pct / 100f;
        if (float.TryParse(value, out var num)) return num;
        return 1;
    }

    public static void ParseGap(string value, ComputedStyle style)
    {
        var parts = ShorthandExpander.SplitShorthand(value).ToArray();
        if (parts.Length == 0) return;
        // 'gap' is <'row-gap'> <'column-gap'>?, and each side is 'normal' | <length-percentage>
        // (CSS Box Alignment 3 §3.1). One bad side discards the whole declaration, so both sides
        // are checked before either is written.
        if (parts.Length > 1 && !IsGapValue(parts[1])) return;
        if (!IsGapValue(parts[0])) return;
        SetGap(style, parts[0], row: true);
        SetGap(style, parts.Length > 1 ? parts[1] : parts[0], row: false);
    }

    private static bool IsGapValue(string token) =>
        IsNormalGapKeyword(token) || Length.TryParse(token, out _);

    /// <summary>Writes one gap side, keeping the 'normal' spelling next to the zero it lays out
    /// as so that the computed value can report the keyword back.</summary>
    private static void SetGap(ComputedStyle style, string token, bool row)
    {
        if (IsNormalGapKeyword(token))
        {
            if (row) { style.RowGap = new PixelLength(0); style.RowGapIsNormal = true; }
            else { style.ColumnGap = new PixelLength(0); style.ColumnGapIsNormal = true; }
            return;
        }
        if (!Length.TryParse(token, out _)) return;
        var length = Length.Parse(token);
        if (row) { style.RowGap = length; style.RowGapIsNormal = false; }
        else { style.ColumnGap = length; style.ColumnGapIsNormal = false; }
    }

    /// <summary>Writes one side of the two scroll insets (CSS Scroll Snap 1 §6.1). The sides are
    /// numbered in the order the box shorthands fill them — top, right, bottom, left of
    /// 'scroll-margin', then the same four of 'scroll-padding'. 'auto' is the zero the property
    /// starts at, a percentage is not a value of it, and anything the length parser rejects
    /// leaves the side on its initial value.</summary>
    private static void SetScrollInset(ComputedStyle style, string value, int side)
    {
        var token = value.Trim().ToLowerInvariant();
        Length? length;
        if (token == "auto") length = new PixelLength(0);
        else if (!Length.TryParse(token, out _)) return;
        else
        {
            var parsed = Length.Parse(token);
            // A percentage would have to resolve against the scrollport, which the inset is
            // measured from, and the grammar does not allow one at all.
            if (parsed is PercentLength) return;
            length = parsed;
        }
        switch (side)
        {
            case 0: style.ScrollMarginTop = length; break;
            case 1: style.ScrollMarginRight = length; break;
            case 2: style.ScrollMarginBottom = length; break;
            case 3: style.ScrollMarginLeft = length; break;
            case 4: style.ScrollPaddingTop = length; break;
            case 5: style.ScrollPaddingRight = length; break;
            case 6: style.ScrollPaddingBottom = length; break;
            case 7: style.ScrollPaddingLeft = length; break;
        }
    }

    /// <summary>'normal' is a legal value of 'row-gap', 'column-gap' and 'gap' and is also
    /// their initial value (CSS Box Alignment 3 §3.1). It lays out as zero, which is why the
    /// engine can keep the length it parsed and only remember the spelling — but the computed
    /// value of a box that was told 'normal' is 'normal', not '0px' (measured).</summary>
    private static bool IsNormalGapKeyword(string token) =>
        token.Trim().Equals("normal", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Canonical form of 'grid-template-areas' (CSS Grid 1 §7.2): one row per quoted
    /// string, cells separated by a single space, rows separated by a single space — the shape
    /// the computed value has. It is stored like this because both spellings reach the same
    /// property: the authored '"a a" "b c"', and the bare rows the 'grid-template' parser hands
    /// over as 'a a,b c'.</summary>
    public static string? CanonicalGridTemplateAreas(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

        var rows = new List<string>();
        if (text.Contains('"'))
        {
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] != '"') { i++; continue; }
                int end = text.IndexOf('"', i + 1);
                if (end < 0) break;
                rows.Add(CanonicalAreaRow(text[(i + 1)..end]));
                i = end + 1;
            }
        }
        else
        {
            foreach (var row in text.Split(',')) rows.Add(CanonicalAreaRow(row));
        }
        return rows.Count == 0 ? null : string.Join(" ", rows.Select(r => $"\"{r}\""));
    }

    /// <summary>The cells of one area row, collapsed to a single space between them.</summary>
    private static string CanonicalAreaRow(string row) =>
        string.Join(' ', row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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

    /// <summary>One layer of 'box-shadow': <c>[inset? &amp;&amp; &lt;length&gt;{2,4} &amp;&amp; &lt;color&gt;?]</c>
    /// (CSS Backgrounds 3 §4.3). The grammar is unordered — the keyword, the lengths and the
    /// colour may come in any order — so each token is classified first and the lengths are
    /// then assigned in their grammatical order offset-x, offset-y, blur, spread. A layer with
    /// fewer than two or more than four lengths is not a shadow and is dropped.</summary>
    public static BoxShadowValue? ParseBoxShadowComponent(string component)
    {
        var parts = ShorthandExpander.SplitShorthand(component.Trim()).ToArray();
        if (parts.Length == 0) return null;

        bool inset = false;
        var lengths = new List<float>(4);
        var color = new List<string>(4);
        foreach (var raw in parts)
        {
            var token = raw.Trim();
            if (token.Equals("inset", StringComparison.OrdinalIgnoreCase)) { inset = true; continue; }
            if (!ColorParser.LooksLikeColor(token) && ParseSize(token) is float px)
            {
                if (lengths.Count < 4) lengths.Add(px);
                continue;
            }
            color.Add(token);
        }

        if (lengths.Count is < 2 or > 4 || color.Count > 1) return null;
        var shadowColor = color.Count > 0
            ? ColorParser.Parse(string.Join(" ", color))
            : new SKColor(0, 0, 0, 80); // default currentColor≈black with standard shadow alpha
        return new BoxShadowValue(shadowColor, lengths[0], lengths[1],
            lengths.Count > 2 ? lengths[2] : 0f, lengths.Count > 3 ? lengths[3] : 0f, inset);
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
            "display" => TryParseDisplay(propValue, out _, out _),
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
            // Validity comes from the same keyword table the parsers use; the fifth
            // hand-written copy of this list is what silently dropped 'inline-start'.
            "float" => Acrux.Core.Dom.CssFloatKeywords.TryParseFloat(propValue, out _),
            "clear" => Acrux.Core.Dom.CssFloatKeywords.TryParseClear(propValue, out _),
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
            _ => Acrux.Core.Css.Properties.CssPropertyIdExtensions.FromString(propName) != Acrux.Core.Css.Properties.CssPropertyId.Invalid
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

