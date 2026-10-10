using System.Globalization;
using System.Text;
using Acrux.Core.Css;
using Acrux.Core.Css.Properties;

namespace Acrux.Core.Dom.Animations;

/// <summary>
/// Reads a property's current computed value off a <see cref="ComputedStyle"/> as
/// canonical CSS text.
///
/// Two consumers need this and nothing else provides it: transitions need a
/// before-change value to animate from, and <c>@keyframes</c> need the element's
/// underlying value to fill in the implicit 0% / 100% endpoints. Emitting text
/// (rather than typed values) keeps the engine aligned with the rest of the
/// style system, which is declaration-shaped throughout.
/// </summary>
public static class ComputedValueSerializer
{
    /// <summary>
    /// The computed value of <paramref name="property"/>, or null when the
    /// property is not tracked for animation.
    /// </summary>
    public static string? Get(ComputedStyle style, string property)
        => Get(style, property, includeShorthands: false);

    /// <summary>
    /// The computed value of <paramref name="property"/>.
    /// </summary>
    /// <param name="includeShorthands">Whether a shorthand may answer. A computed style is a
    /// snapshot of every property that applies, so <c>getComputedStyle(el).margin</c> has to
    /// read back the four margins assembled into the shorthand (CSSOM §6, measured); the
    /// animation code, which asks only for the longhand it is about to interpolate, must not
    /// have a list of text handed to it where it expects one value.</param>
    public static string? Get(ComputedStyle style, string property, bool includeShorthands)
    {
        if (style == null || string.IsNullOrEmpty(property)) return null;
        // The echo-only properties answer from their one table: the canonical text the page wrote,
        // or the value the engine says when nothing was written. A property that gains a real field
        // and consumer is taken out of that table, so it is never answered twice.
        if (Acrux.Core.Css.Resolver.EchoedProperties.Find(property) is { } echo)
        {
            var written = style.GetEchoed(echo.Name) ?? echo.Initial;
            return echo.Name == "math-depth"
                ? Acrux.Core.Css.Resolver.EchoedProperties.MathDepthComputed(written)
                : written;
        }
        var value = ValueOf(style, property);
        if (value != null) return value;
        if (includeShorthands)
        {
            var shorthand = ShorthandValueOf(style, property);
            if (shorthand != null) return shorthand;
        }
        // A legacy '-webkit-' property that no layout step reads still has a computed value: the
        // one the page wrote, or the one measured for an element that wrote nothing (both in
        // snapshots/out/_b257_edge_legacy_grammar.txt). It is answered before the alias fold below
        // because for these names the prefixed spelling IS the property's name.
        if (Acrux.Core.Css.Resolver.CssPropertyTraits.IsLegacyDeclaredOnly(property))
        {
            var written = style.GetDeclaredOnlyValue(property);
            if (written == null || Acrux.Core.Css.Resolver.CssPropertyTraits.IsLegacyComputedInitial(property))
                return Acrux.Core.Css.Resolver.CssPropertyTraits.LegacyDeclaredOnlyInitial(property);
            return Acrux.Core.Css.Resolver.CssPropertyTraits.LegacyComputedText(property, written);
        }

        // A prefixed name is an alias of the property it is spelled without the prefix for, and
        // a reference engine answers it: 'getComputedStyle(el).getPropertyValue("-webkit-transform")'
        // gives the transform. Only a name this serializer has no case of its own for falls
        // through, so a prefix that means something else keeps its own spelling.
        var canonical = CssPropertyName.FromString(property);
        if (canonical.Id == CssPropertyId.Invalid || canonical.Id == CssPropertyId.Variable) return null;
        var dashed = Acrux.Core.Css.Properties.CssPropertyIdExtensions.BehaviourName(
            canonical.ToCssString());
        return ValueOf(style, dashed)
            ?? (includeShorthands ? ShorthandValueOf(style, dashed) : null);
    }

    /// <summary>The value of a property spelled by its canonical dashed name.</summary>
    private static string? ValueOf(ComputedStyle style, string property)
    {
        switch (property)
        {
            // ------------------------------------------------------- colors
            case "color": return ColorText(style.Color);
            case "background-color": return style.BackgroundColor.HasValue
                    ? ColorText(style.BackgroundColor.Value)
                    // The initial value is 'transparent', which the reference engine prints in
                    // its rgba() form (measured), never as the keyword.
                    : ColorText(new SkiaSharp.SKColor(0, 0, 0, 0));
            case "border-top-color": return ColorText(style.BorderTopColor);
            case "border-right-color": return ColorText(style.BorderRightColor);
            case "border-bottom-color": return ColorText(style.BorderBottomColor);
            case "border-left-color": return ColorText(style.BorderLeftColor);
            case "outline-color": return ColorText(style.OutlineColor);
            // 'caret-color: auto' has no colour of its own: the caret is drawn in the text
            // colour, and that is what the computed value reports (measured: a box whose
            // 'color' is rgb(1, 2, 3) answers 'rgb(1, 2, 3)' for 'caret-color' while the page
            // never mentioned the caret).
            case "caret-color": return ColorText(style.CaretColor ?? style.Color);
            case "accent-color": return style.AccentColor.HasValue ? ColorText(style.AccentColor.Value) : "auto";
            // 'currentcolor' is a specified value, not a computed one: every color property that
            // defaults to the text color reports the color it resolved to (measured), and
            // 'column-rule-color' is one of them.
            case "column-rule-color": return style.ColumnRuleColor.HasValue
                    ? ColorText(style.ColumnRuleColor.Value) : ColorText(style.Color);
            // 'auto' has no computed color of its own; a reference engine reports it
            // as the element's resolved 'color', and that is also what an animation
            // must interpolate from.
            case "text-decoration-color": return ColorText(style.ResolvedTextDecorationColor);
            case "text-emphasis-color":
                return string.Equals(style.TextEmphasisColor, "currentcolor", StringComparison.OrdinalIgnoreCase)
                    ? ColorText(style.Color)
                    : ResolveColorText(style.TextEmphasisColor, style.Color);

            // -------------------------------------------------------- sizes
            // A box size is clamped at computed-value time: nothing narrower than nothing is
            // still zero wide, and a negative used value prints as one (CSS 2.1 §10.4).
            case "width": return style.ComputedSizeCss(style.Width);
            case "height": return style.ComputedSizeCss(style.Height);
            case "min-width": return style.MinWidth != null ? LengthText(style, style.MinWidth) : "auto";
            case "max-width": return style.MaxWidth != null ? LengthText(style, style.MaxWidth) : "none";
            case "min-height": return style.MinHeight != null ? LengthText(style, style.MinHeight) : "auto";
            case "max-height": return style.MaxHeight != null ? LengthText(style, style.MaxHeight) : "none";

            case "top": return LengthText(style, style.Top);
            case "right": return LengthText(style, style.Right);
            case "bottom": return LengthText(style, style.Bottom);
            case "left": return LengthText(style, style.Left);

            case "margin-top": return LengthText(style, style.MarginTop);
            case "margin-right": return LengthText(style, style.MarginRight);
            case "margin-bottom": return LengthText(style, style.MarginBottom);
            case "margin-left": return LengthText(style, style.MarginLeft);
            case "padding-top": return LengthText(style, style.PaddingTop);
            case "padding-right": return LengthText(style, style.PaddingRight);
            case "padding-bottom": return LengthText(style, style.PaddingBottom);
            case "padding-left": return LengthText(style, style.PaddingLeft);

            // A border whose style is 'none' or 'hidden' has no width, whatever the page asked
            // for (CSS 2.1 §10.3.2 — the width is zeroed at computed-value time, which is why
            // 'border-width: 1px' on a box with no border style reads back as '0px').
            case "border-top-width": return BorderWidthText(style.BorderTopWidth, style.BorderTopStyle);
            case "border-right-width": return BorderWidthText(style.BorderRightWidth, style.BorderRightStyle);
            case "border-bottom-width": return BorderWidthText(style.BorderBottomWidth, style.BorderBottomStyle);
            case "border-left-width": return BorderWidthText(style.BorderLeftWidth, style.BorderLeftStyle);
            case "outline-width": return Px(style.OutlineWidth);
            case "outline-offset": return Px(style.OutlineOffset);

            // The logical longhands are the physical side they were applied to: this engine maps
            // them onto the sides during the cascade (CSS Logical Properties 1 §2), so a
            // 'margin-block-start' read answers with the top margin it became, and an
            // 'margin-inline-start' with the side 'direction' put it on.
            case "margin-block-start": return LengthText(style, style.MarginTop);
            case "margin-block-end": return LengthText(style, style.MarginBottom);
            case "margin-inline-start": return InlineSideMargin(style, start: true);
            case "margin-inline-end": return InlineSideMargin(style, start: false);
            case "padding-block-start": return LengthText(style, style.PaddingTop);
            case "padding-block-end": return LengthText(style, style.PaddingBottom);
            case "padding-inline-start": return InlineSidePadding(style, start: true);
            case "padding-inline-end": return InlineSidePadding(style, start: false);
            case "border-block-start-width": return BorderWidthText(style.BorderTopWidth, style.BorderTopStyle);
            case "border-block-end-width": return BorderWidthText(style.BorderBottomWidth, style.BorderBottomStyle);
            case "border-block-start-style": return style.BorderTopStyle.ToCssString();
            case "border-block-end-style": return style.BorderBottomStyle.ToCssString();
            case "border-block-start-color": return ColorText(style.BorderTopColor);
            case "border-block-end-color": return ColorText(style.BorderBottomColor);
            case "border-inline-start-width":
                return IsRtl(style) ? BorderWidthText(style.BorderRightWidth, style.BorderRightStyle)
                                    : BorderWidthText(style.BorderLeftWidth, style.BorderLeftStyle);
            case "border-inline-end-width":
                return IsRtl(style) ? BorderWidthText(style.BorderLeftWidth, style.BorderLeftStyle)
                                    : BorderWidthText(style.BorderRightWidth, style.BorderRightStyle);
            case "border-inline-start-style":
                return (IsRtl(style) ? style.BorderRightStyle : style.BorderLeftStyle).ToCssString();
            case "border-inline-end-style":
                return (IsRtl(style) ? style.BorderLeftStyle : style.BorderRightStyle).ToCssString();
            case "border-inline-start-color":
                return ColorText(IsRtl(style) ? style.BorderRightColor : style.BorderLeftColor);
            case "border-inline-end-color":
                return ColorText(IsRtl(style) ? style.BorderLeftColor : style.BorderRightColor);

            case "border-top-left-radius": return RadiusText(style.BorderTopLeftRadius, style.BorderTopLeftRadiusY);
            case "border-top-right-radius": return RadiusText(style.BorderTopRightRadius, style.BorderTopRightRadiusY);
            case "border-bottom-right-radius": return RadiusText(style.BorderBottomRightRadius, style.BorderBottomRightRadiusY);
            case "border-bottom-left-radius": return RadiusText(style.BorderBottomLeftRadius, style.BorderBottomLeftRadiusY);

            case "flex-basis": return LengthText(style, style.FlexBasis);

            // --------------------------------------------------------- text
            case "font-size": return Px(style.FontSize);
            case "font": return FontShorthandText(style);
            case "line-height":
                // 'normal' is the only keyword the computed value keeps. A multiplier is not:
                // CSS 2.1 §10.6 makes a unitless 'line-height' relative to the element's font,
                // and the reference engine reports the pixels that produces (measured, on an 11px
                // box: '1.2' reads back '13.2px', '120%' '13.2px', '2em' '22px', '0' '0px').
                return style.LineHeightIsNormal
                    ? "normal"
                    : Px(style.LineHeightPx ?? style.LineHeight * style.FontSize);
            case "letter-spacing": return style.LetterSpacingIsNormal ? "normal" : Px(style.LetterSpacing);
            // 'word-spacing' has no keyword in its computed value (CSS Text 4 §5.1, measured):
            // 'normal' is the zero of the property and reads back as '0px'. 'letter-spacing'
            // keeps its keyword, which is why the two do not share a line here.
            case "word-spacing": return style.WordSpacingIsNormal ? "0px" : Px(style.WordSpacing);
            // The computed value of 'text-indent' is the resolved length with the two flags still
            // attached, in the engine's own order (measured: '3em hanging' reads '48px hanging').
            case "text-indent": return CssValueText.CanonicalizeTextIndent(
                Px(style.TextIndent)
                + (style.TextIndentHanging ? " hanging" : "")
                + (style.TextIndentEachLine ? " each-line" : ""), "text-indent") ?? Px(style.TextIndent);
            case "font-weight": return ((int)style.FontWeight).ToString(CultureInfo.InvariantCulture);
            case "visibility": return style.Visibility switch
            {
                VisibilityType.Hidden => "hidden",
                VisibilityType.Collapse => "collapse",
                _ => "visible",
            };

            // ------------------------------------------------ discrete words
            case "display": return style.DisplayCssText;
            case "position": return style.Position.ToCssString();
            // The logical keywords must survive the round trip: 'float: inline-start'
            // computes as 'inline-start', not as the physical side it will be used as.
            case "float": return FloatTypeText(style.Float);
            case "clear": return ClearTypeText(style.Clear);
            case "z-index": return style.ZIndex?.ToString(CultureInfo.InvariantCulture) ?? "auto";
            case "order": return style.Order.ToString(CultureInfo.InvariantCulture);
            case "overflow-x": return style.OverflowX switch
            {
                OverflowType.Hidden => "hidden",
                OverflowType.Scroll => "scroll",
                OverflowType.Auto => "auto",
                _ => "visible",
            };
            case "overflow-y": return style.OverflowY switch
            {
                OverflowType.Hidden => "hidden",
                OverflowType.Scroll => "scroll",
                OverflowType.Auto => "auto",
                _ => "visible",
            };
            // -------------------------------------------------- box alignment
            // CSS Box Alignment 3 §3: the initial value of the distribution properties is the
            // keyword 'normal', and a keyword the page wrote is what reads back — 'end' is not
            // the legacy 'flex-end' (measured). The engine's own enums fold the two together for
            // layout, so the spelling is carried beside them.
            case "align-items": return string.IsNullOrEmpty(style.AlignItemsCssText)
                ? "normal" : style.AlignItemsCssText;
            case "justify-content": return string.IsNullOrEmpty(style.JustifyContentCssText)
                ? "normal" : style.JustifyContentCssText;
            case "align-content": return string.IsNullOrWhiteSpace(style.AlignContent)
                ? "normal" : style.AlignContent;
            case "justify-items": return string.IsNullOrWhiteSpace(style.JustifyItems)
                    || style.JustifyItems == "legacy"
                ? "normal" : style.JustifyItems;
            case "align-self": return string.IsNullOrEmpty(style.AlignSelfCssText)
                ? "auto" : style.AlignSelfCssText;
            case "justify-self": return string.IsNullOrWhiteSpace(style.JustifySelf)
                ? "auto" : style.JustifySelf;
            // 'overflow-clip-margin' is a length plus the box it measures from; the length form
            // is what the reference engine prints for a plain value.
            case "overflow-clip-margin":
                return style.OverflowClipMargin == null ? "0px" : LengthText(style, style.OverflowClipMargin);
            // The scroll insets (CSS Scroll Snap 1 §6.1): a side the page never mentioned is the
            // initial zero, not 'auto' — 'auto' is only another spelling of that zero.
            case "scroll-margin-top": return ScrollInsetText(style, style.ScrollMarginTop);
            case "scroll-margin-right": return ScrollInsetText(style, style.ScrollMarginRight);
            case "scroll-margin-bottom": return ScrollInsetText(style, style.ScrollMarginBottom);
            case "scroll-margin-left": return ScrollInsetText(style, style.ScrollMarginLeft);
            case "scroll-padding-top": return ScrollInsetText(style, style.ScrollPaddingTop);
            case "scroll-padding-right": return ScrollInsetText(style, style.ScrollPaddingRight);
            case "scroll-padding-bottom": return ScrollInsetText(style, style.ScrollPaddingBottom);
            case "scroll-padding-left": return ScrollInsetText(style, style.ScrollPaddingLeft);
            case "caret-shape": return string.IsNullOrWhiteSpace(style.CaretShape)
                ? "auto" : style.CaretShape;
            case "box-sizing": return style.BoxSizing.ToCssString();
            // A string marker is a value of 'list-style-type', and the engine says it as a string —
            // with its quotes, whichever way the page wrote them (measured: "'x'" reads '"x"').
            case "list-style-type": return string.IsNullOrEmpty(style.ListStyleTypeString)
                ? string.IsNullOrEmpty(style.ListStyleTypeName)
                    ? CssEnumFormatter.CssKeywordFromEnum(style.ListStyleType.ToString())
                    : style.ListStyleTypeName!
                : "\"" + style.ListStyleTypeString! + "\"";
            case "list-style-position": return style.ListStylePosition == ListStylePosition.Inside ? "inside" : "outside";
            case "table-layout": return style.TableLayout;
            case "caption-side": return style.CaptionSide;
            case "empty-cells": return style.EmptyCells;
            case "border-collapse": return style.BorderCollapse ? "collapse" : "separate";
            case "object-fit": return style.ObjectFit switch
            {
                ObjectFitType.Contain => "contain",
                ObjectFitType.Cover => "cover",
                ObjectFitType.None => "none",
                ObjectFitType.ScaleDown => "scale-down",
                _ => "fill",
            };
            case "mix-blend-mode": return CssEnumFormatter.CssKeywordFromEnum(style.MixBlendMode.ToString());
            case "isolation": return style.Isolation == IsolationType.Isolate ? "isolate" : "auto";
            case "image-rendering": return style.ImageRendering switch
            {
                ImageRenderingType.CrispEdges => "crisp-edges",
                ImageRenderingType.Pixelated => "pixelated",
                _ => "auto",
            };
            case "pointer-events": return style.PointerEvents ?? "auto";
            case "font-synthesis": return Css.Resolver.CssPropertyApplier.FormatFontSynthesis(style.FontSynthesis);
            case "font-synthesis-weight":
                return (style.FontSynthesis & FontSynthesisType.Weight) != 0 ? "auto" : "none";
            case "font-synthesis-style":
                return (style.FontSynthesis & FontSynthesisType.Style) != 0 ? "auto" : "none";
            case "font-synthesis-small-caps":
                return (style.FontSynthesis & FontSynthesisType.SmallCaps) != 0 ? "auto" : "none";

            // ------------------------------------------------- modelled but unserialised
            // These all have a real field on ComputedStyle; they used to answer nothing at
            // all, which made a supported property look unsupported to a script. Formats
            // below follow the reference engine's serialization (measured, b225 §E).
            case "font-variant-caps": return style.FontVariantCaps;
            case "font-kerning": return style.FontKerning;
            case "font-optical-sizing": return style.FontOpticalSizing;
            // 'normal' is reported as the percentage it means.
            case "font-stretch": return style.FontStretch is "" or "normal" ? "100%" : style.FontStretch;
            case "text-rendering": return style.TextRendering;
            case "contain": return Css.Resolver.CssPropertyApplier.FormatContain(style.Contain);
            case "contain-intrinsic-size": return ContainIntrinsicSizeText(style);
            case "field-sizing": return style.FieldSizing == FieldSizingType.Content ? "content" : "normal";
            case "text-overflow": return style.TextOverflowString is { Length: > 0 } tos ? tos
                : style.TextOverflow == TextOverflowType.Ellipsis ? "ellipsis" : "clip";
            case "line-break": return style.LineBreak.ToString().ToLowerInvariant();
            case "hyphens": return style.Hyphens.ToString().ToLowerInvariant();
            // 'tab-size' keeps the form the page used: a number counts space advances and reads as
            // a number, a length is a length and reads with its unit (both measured; the initial is
            // the number 8).
            case "tab-size": return style.TabSizePx is { } tabPx
                ? Px(tabPx) : CssValueTokenizer.Num(style.TabSize);
            case "scroll-behavior": return style.ScrollBehavior.ToString().ToLowerInvariant();
            case "overscroll-behavior-x": return style.OverscrollBehaviorX.ToString().ToLowerInvariant();
            case "overscroll-behavior-y": return style.OverscrollBehaviorY.ToString().ToLowerInvariant();
            // 'text-wrap' prints as the shorthand the page wrote: a wrapping mode is what a style
            // word alone means, so 'wrap balance' reads 'balance' while 'nowrap stable' says both
            // (measured).
            case "text-wrap": {
                var mode = style.TextWrapMode.ToString().ToLowerInvariant();
                // The enum member is 'Balanced' while the keyword is 'balance', so the words are
                // named here rather than derived from the member (see CssEnumFormatter's rule).
                var wrapStyle = style.TextWrapStyle switch {
                    TextWrapStyleType.Stable => "stable",
                    TextWrapStyleType.Balanced => "balance",
                    TextWrapStyleType.Pretty => "pretty",
                    _ => "auto",
                };
                if (wrapStyle == "auto") return mode;
                return mode == "wrap" ? wrapStyle : mode + " " + wrapStyle;
            }
            case "text-wrap-mode": return style.TextWrapMode.ToString().ToLowerInvariant();
            case "text-wrap-style": return style.TextWrapStyle switch {
                TextWrapStyleType.Stable => "stable",
                TextWrapStyleType.Balanced => "balance",
                TextWrapStyleType.Pretty => "pretty",
                _ => "auto",
            };
            case "writing-mode": return style.WritingMode switch
            {
                WritingModeType.VerticalRl => "vertical-rl",
                WritingModeType.VerticalLr => "vertical-lr",
                _ => "horizontal-tb",
            };
            case "direction": return style.Direction;
            case "unicode-bidi": return style.UnicodeBidi;
            case "user-select": return style.UserSelect ?? "auto";
            case "counter-reset": return string.IsNullOrEmpty(style.CounterReset) ? "none" : style.CounterReset;
            case "counter-increment": return string.IsNullOrEmpty(style.CounterIncrement) ? "none" : style.CounterIncrement;
            case "quotes": return style.Quotes;
            case "mask-image": return Css.ImageValueCanonicalizer.Canonicalize(
                string.IsNullOrEmpty(style.MaskImage) ? (style.Mask ?? "none") : style.MaskImage);
            case "mask-size": return MaskLayerList(style, style.MaskSizeLayers, style.MaskSize, "auto", null);
            // 'mask-position' is stored as authored; the reference engine resolves the
            // keywords to their percentage of the positioning area (measured: 'right bottom'
            // serializes as '100% 100%', 'center' as '50% 50%'), and an omitted y is 'center'.
                        case "mask-position": return MaskLayerList(style, style.MaskPositionLayers, style.MaskPosition, "0% 0%", Css.MaskKeywordSerializer.Position);
            // 'mask-repeat' collapses two identical axes to one keyword ('round round' → 'round').
                        case "mask-repeat": return MaskLayerList(style, style.MaskRepeatLayers, style.MaskRepeat, "repeat", Css.MaskKeywordSerializer.Repeat);
                        case "mask-clip": return MaskLayerList(style, style.MaskClipLayers, style.MaskClip, "border-box", null);
                        case "mask-origin": return MaskLayerList(style, style.MaskOriginLayers, style.MaskOrigin, "border-box", null);
                        case "mask-mode": return MaskLayerList(style, style.MaskModeLayers, style.MaskMode, "match-source", null);
                        case "mask-composite": return MaskLayerList(style, style.MaskCompositeLayers, style.MaskComposite, "add", null);
            case "cursor": return style.Cursor ?? "auto";
            case "resize": return style.Resize switch
            {
                ResizeType.Both => "both",
                ResizeType.Vertical => "vertical",
                ResizeType.Horizontal => "horizontal",
                ResizeType.Block => "block",
                ResizeType.Inline => "inline",
                _ => "none",
            };
            // The ratio of two numbers, printed as the pair it was authored with (measured:
            // '1/2' reads back as '1 / 2', '0.5' as '0.5 / 1') — not as their quotient.
            case "aspect-ratio":
                return style.AspectRatio > 0
                    ? style.AspectRatioPair ?? $"{CssValueTokenizer.Num(style.AspectRatio)} / 1"
                    : "auto";

            // -------------------------------------------------------- numbers
            case "opacity": return CssValueTokenizer.Num(style.Opacity);
            case "flex-grow": return CssValueTokenizer.Num(style.FlexGrow);
            case "flex-shrink": return CssValueTokenizer.Num(style.FlexShrink);
            case "column-count": return style.ColumnCount > 0 ? style.ColumnCount.ToString(CultureInfo.InvariantCulture) : "auto";
            // 'normal' is the initial value of both gaps and is a keyword of its own, not a
            // zero (CSS Box Alignment 3 §3.1, measured).
            case "row-gap": return style.RowGapIsNormal ? "normal" : LengthText(style, style.RowGap);
            case "column-gap": return style.ColumnGapIsNormal ? "normal" : LengthText(style, style.ColumnGap);
            case "orphans": return style.Orphans.ToString(CultureInfo.InvariantCulture);
            case "widows": return style.Widows.ToString(CultureInfo.InvariantCulture);

            // ---------------------------------------------------- transforms
            // A transform's computed value is the matrix it applies, not the list of functions
            // (CSS Transforms 1 §6, measured: 'translate(4px,5px) rotate(30deg)' reads back as
            // 'matrix(0.866025, 0.5, -0.5, 0.866025, 4, 5)'). The origin is not part of it:
            // 'transform-origin' is a separate property, and the reference engine reports the
            // un-origin-shifted matrix here too. A list that carries a 3D function projects to
            // matrix3d() on the platform, which this engine has no text form for, so those
            // values stay as authored rather than printing a wrong 2D projection.
            case "transform": return TransformText(style.Transform);
            case "translate": return string.IsNullOrWhiteSpace(style.Translate) ? "none" : style.Translate!.Trim();
            case "rotate": return string.IsNullOrWhiteSpace(style.Rotate) ? "none" : style.Rotate!.Trim();
            case "scale": return string.IsNullOrWhiteSpace(style.Scale) ? "none" : style.Scale!.Trim();
            case "perspective": return string.IsNullOrWhiteSpace(style.Perspective) ? "none" : style.Perspective!.Trim();
            case "transform-origin": return TransformOriginText(style.TransformOrigin);
            case "perspective-origin": return TransformOriginText(style.PerspectiveOrigin);
            case "backface-visibility": return style.BackfaceVisibility ?? "visible";
            case "transform-style": return style.TransformStyle ?? "flat";
            // 'view-box' is the initial value the spec gives the property (CSS Transforms 1 §3),
            // and the box a non-SVG element's origin percentages resolve against.
            case "transform-box": return style.TransformBox ?? "view-box";

            // The grid placement properties hold the line the author named ('1', 'span 2',
            // a line name); 'auto' is what a box that was not placed sits on.
            case "grid-row-start": return style.GridRowStart ?? "auto";
            case "grid-row-end": return style.GridRowEnd ?? "auto";
            case "grid-column-start": return style.GridColumnStart ?? "auto";
            case "grid-column-end": return style.GridColumnEnd ?? "auto";

            // --------------------------------------------------- backgrounds
            // Every geometry longhand takes one entry per background layer, and the reference
            // engine prints the whole list (measured: 'background: url(a) 10px 20px, url(b)'
            // reads back as '10px 20px, 0% 0%' — the second layer keeps the property's initial
            // value rather than cycling the first one).
            case "background-position":
                return BackgroundPositionText(style);
            // The two axes have longhands of their own (CSS Backgrounds 3 §4.1.1.1); each reports
            // its own list, which the position shorthand fills from its two halves.
            case "background-position-x": return BackgroundAxisText(style, horizontal: true);
            case "background-position-y": return BackgroundAxisText(style, horizontal: false);
            case "background-size":
                return CycleListText(style.BackgroundSizeLayers, BackgroundSizeText(style), SizeLayerText,
                    BackgroundLayerCount(style));
            case "background-repeat":
                return CycleListText(style.BackgroundRepeatLayers,
                    CssEnumFormatter.CssKeywordFromEnum(style.BackgroundRepeat.ToString()), RepeatLayerText,
                    BackgroundLayerCount(style));
            case "background-attachment":
                return CycleListText(style.BackgroundAttachmentLayers,
                    AttachmentText(style.BackgroundAttachment), AttachmentText,
                    BackgroundLayerCount(style));
            case "background-clip":
                return CycleListText(style.BackgroundClipLayers, style.BackgroundClip, one => one,
                    BackgroundLayerCount(style));
            case "background-origin":
                return CycleListText(style.BackgroundOriginLayers, style.BackgroundOrigin, one => one,
                    BackgroundLayerCount(style));
            // The whole list, colours canonicalised: reporting only the first layer made a
            // multi-layer background look like a single-image one to any script reading it.
            case "background-image": return Css.ImageValueCanonicalizer.Canonicalize(
                style.BackgroundImage is { Count: > 0 } ? string.Join(", ", style.BackgroundImage) : "none");
            // A filter list is printed as the grammar reads it, not as the page typed it: one
            // invalid function leaves the property at its initial 'none', a length has lost the
            // font it was relative to, and a drop-shadow carries its colour first (CSS Filters 1
            // §29.1, measured — see CssFilterValue).
            case "filter": return CssFilterValue.ComputedText(style.Filter, style);
            case "backdrop-filter": return CssFilterValue.ComputedText(style.BackdropFilter, style);
            case "clip-path": return string.IsNullOrWhiteSpace(style.ClipPath) ? "none" : style.ClipPath!;
            case "box-shadow": return BoxShadowText(style);
            case "text-shadow": return TextShadowText(style);

            // ------------------------------------------------------ borders
            case "outline-style": return style.OutlineStyle.ToCssString();
            case "border-top-style": return style.BorderTopStyle.ToCssString();
            case "border-right-style": return style.BorderRightStyle.ToCssString();
            case "border-bottom-style": return style.BorderBottomStyle.ToCssString();
            case "border-left-style": return style.BorderLeftStyle.ToCssString();
            case "column-rule-style": return style.ColumnRuleStyle.ToCssString();
            case "column-rule-width": return Px(style.ColumnRuleWidth);
            // The line painter reads 'text-decoration-skip-ink', so it is not one of the echoed
            // properties: the engine keeps the flag and prints the two answers it can give.
            case "text-decoration-skip-ink": return style.TextDecorationSkipInk ? "auto" : "none";
            case "text-decoration-style": return style.TextDecorationStyle switch
            {
                TextDecorationStyleType.Double => "double",
                TextDecorationStyleType.Dotted => "dotted",
                TextDecorationStyleType.Dashed => "dashed",
                TextDecorationStyleType.Wavy => "wavy",
                _ => "solid",
            };
            case "text-decoration-line": return TextDecorationLineText(style.TextDecorationLine);
            case "text-decoration-thickness":
                return style.TextDecorationThicknessFromFont || float.IsNaN(style.TextDecorationThickness)
                    ? "auto"
                    : Px(style.TextDecorationThickness);
            case "text-underline-offset":
                return style.TextUnderlineOffsetIsAuto ? "auto" : Px(style.TextUnderlineOffset);
            // 'filled' is the initial shape style, and the reference engine leaves it out of the
            // computed value (measured: 'text-emphasis: filled circle' reads back as 'circle').
            case "text-emphasis-style": return TextEmphasisStyleText(style.TextEmphasisStyle);
            case "text-emphasis-position": return style.TextEmphasisPosition;
            case "will-change": return style.WillChange ?? "auto";
            case "content": return style.Content ?? "normal";

            // ------------------------------------------------ time-controlled
            // The transition and animation longhands are carried as the declaration text
            // they were written with, so the initial values have to be spelled out here:
            // a computed style is a full snapshot and reports every longhand (CSSOM).
            case "transition-property": return ListOr(style.TransitionProperty, "all");
            case "transition-duration": return ListOr(style.TransitionDuration, "0s");
            case "transition-delay": return ListOr(style.TransitionDelay, "0s");
            case "transition-timing-function": return ListOr(style.TransitionTimingFunction, "ease");
            case "animation-name": return ListOr(style.AnimationName, "none");
            case "animation-duration": return ListOr(style.AnimationDuration, "0s");
            case "animation-timing-function": return ListOr(style.AnimationTimingFunction, "ease");
            case "animation-delay": return ListOr(style.AnimationDelay, "0s");
            case "animation-iteration-count": return ListOr(style.AnimationIterationCount, "1");
            case "animation-direction": return ListOr(style.AnimationDirection, "normal");
            case "animation-fill-mode": return ListOr(style.AnimationFillMode, "none");
            case "animation-play-state": return ListOr(style.AnimationPlayState, "running");

            // ---------------------------------------------------- the rest of the
            // longhands the cascade models but this list had not reached. Each one reads
            // a real field, so a script asking for it gets the value the engine actually
            // uses rather than the empty string.
            case "text-transform": return style.TextTransform;
            case "text-align-last": return CssEnumFormatter.CssKeywordFromEnum(style.TextAlignLast.ToString());
            case "vertical-align":
                // A length resolves at computed-value time, a percentage stays a percentage
                // (measured: 'vertical-align: 1em' at a 12px font reads back '12px').
                return style.VerticalAlign switch
                {
                    VerticalAlignType.Length => Px(style.VerticalAlignOffsetPx ?? 0f),
                    VerticalAlignType.Percentage => $"{CssValueTokenizer.Num(style.VerticalAlignOffsetPx ?? 0f)}%",
                    _ => CssEnumFormatter.CssKeywordFromEnum(style.VerticalAlign.ToString()),
                };
            case "white-space-collapse": return CssEnumFormatter.CssKeywordFromEnum(style.WhiteSpaceCollapse.ToString());
            case "box-decoration-break": return CssEnumFormatter.CssKeywordFromEnum(style.BoxDecorationBreak.ToString());
            case "text-justify": return CssEnumFormatter.CssKeywordFromEnum(style.TextJustify.ToString());
            case "hanging-punctuation": return style.HangingPunctuation ?? "none";
            case "overscroll-behavior":
                // The two axes are one shorthand value: equal keywords collapse to one, and
                // 'overscroll-behavior-x' alone leaves the other axis at its initial 'auto'
                // (measured: 'contain auto').
                return AxisPair(
                    style.OverscrollBehaviorX.ToString().ToLowerInvariant(),
                    style.OverscrollBehaviorY.ToString().ToLowerInvariant());
            case "content-visibility": return CssEnumFormatter.CssKeywordFromEnum(style.ContentVisibility.ToString());
            case "scrollbar-width": return CssEnumFormatter.CssKeywordFromEnum(style.ScrollbarWidth.ToString());
            // The keyword family: the stored text is what the reference engine prints, and the
            // fallback is its measured initial value. 'line-clamp' and 'text-security' without a
            // prefix are not properties the reference engine declares at all, so they answer
            // nothing — the name the page used is what decides, not the value it wrote.
            case "touch-action": return style.TouchAction ?? "auto";
            case "paint-order": return style.PaintOrder ?? "normal";
            case "vector-effect": return style.VectorEffect ?? "none";
            case "shape-rendering": return style.ShapeRendering ?? "auto";
            case "color-rendering": return style.ColorRendering ?? "auto";
            case "color-interpolation": return style.ColorInterpolation ?? "srgb";
            case "color-interpolation-filters":
                return style.ColorInterpolationFilters ?? "linearrgb";
            case "image-orientation": return style.ImageOrientation ?? "from-image";
            case "text-security": return style.TextSecurity ?? "none";
            case "line-clamp":
                // The reference engine has no unprefixed 'line-clamp'; the id this engine keeps for
                // the prefixed spelling answers under the name the page wrote, which the CSSOM
                // reader has already filtered by then.
                return style.LineClamp > 0 ? style.LineClamp.ToString() : "none";
            // The one value of 'both-edges' is the pair, printed in the order the property
            // introduces them (measured: 'stable both-edges' reads back as written).
            case "scrollbar-gutter": return style.ScrollbarGutter switch
            {
                ScrollbarGutterType.Stable => "stable",
                ScrollbarGutterType.BothEdges => "stable both-edges",
                _ => "auto",
            };
            case "scrollbar-color":
                return style.ScrollbarThumbColor == null && style.ScrollbarTrackColor == null
                    ? "auto"
                    : $"{ColorOrAuto(style.ScrollbarThumbColor)} {ColorOrAuto(style.ScrollbarTrackColor)}";
            case "color-scheme": return style.ColorScheme;
            // CSS UI 4 §4.3: 'none' is the initial value, so an element with no widget reports
            // 'none'; 'auto' is the user-agent's own declaration on a control, which paints the
            // widget named next to the value (ComputedStyle.NativeThemeFamily), not a theme.
            case "appearance": return string.IsNullOrWhiteSpace(style.Appearance)
                ? "none"
                : style.Appearance;
            case "forced-color-adjust": return CssEnumFormatter.CssKeywordFromEnum(style.ForcedColorAdjust.ToString());
            case "ruby-position": return style.RubyPosition;
            case "ruby-align":
                // The two deprecated 'distribute-*-letter' spellings both compute as
                // 'space-around' (measured), which is also the initial value.
                return style.RubyAlign switch
                {
                    null or "" => "space-around",
                    "distribute-letter" or "distribute-all-letter" => "space-around",
                    _ => style.RubyAlign,
                };
            case "font-variant": return style.FontVariant;
            case "font-variation-settings": return style.FontVariationSettings;
            case "font-feature-settings": return FontFeatureSettingsText(style.FontFeatureSettings);
            // 'hyphenate-character' is 'auto' or a <string>, and a string is printed as one:
            // the engine keeps the characters, the CSSOM has to show the quotes (CSS Text 4 §5.1).
            case "hyphenate-character": return style.HyphenateCharacter == "auto"
                ? "auto"
                : QuotedText(style.HyphenateCharacter);
            case "counter-set": return style.CounterSet;
            case "column-width": return LengthText(style, style.ColumnWidth);
            case "column-fill": return style.ColumnFill;
            case "column-span": return style.ColumnSpanAll ? "all" : "none";
            case "list-style-image": return string.IsNullOrWhiteSpace(style.ListStyleImage) ? "none" : style.ListStyleImage!;
            case "object-position":
                return $"{ObjectPositionText(style.ObjectPositionX)} {ObjectPositionText(style.ObjectPositionY)}";
            case "grid-auto-flow":
                // 'row' is the initial row direction, so 'dense' alone already says
                // 'row dense' (measured: 'grid-auto-flow: row dense' reads back 'dense').
                return style.GridAutoFlow switch
                {
                    GridAutoFlowType.Column => "column",
                    GridAutoFlowType.Dense => "dense",
                    GridAutoFlowType.ColumnDense => "column dense",
                    _ => "row",
                };
            case "grid-auto-columns": return style.GridAutoColumns ?? "auto";
            case "grid-auto-rows": return style.GridAutoRows ?? "auto";
            case "grid-template-areas": return style.GridTemplateAreas ?? "none";
            // A track list reads back the sizes LAYOUT gave the tracks, not the list the author
            // wrote: 'grid-template-columns: 1fr 2fr' in a 100px grid is '33.3281px 66.6719px'
            // (measured). Only where there are no tracks to report — a box that is not a grid, or
            // one the algorithm never sized — does the authored list stand, and 'none' when that
            // list is empty.
            case "grid-template-columns": return GridTrackListText(style.GridTemplateColumns, style.GridUsedColumnSizes);
            case "grid-template-rows": return GridTrackListText(style.GridTemplateRows, style.GridUsedRowSizes);
            case "border-spacing":
                // Two equal axes collapse to one keyword, and the UA default is the CSS 2.1
                // 2px (measured: 'border-spacing: 5px 5px' reads back '5px').
                return style.BorderRowSpacing is null || Almost(style.BorderSpacing, style.BorderRowSpacing.Value)
                    ? Px(style.BorderSpacing)
                    : $"{Px(style.BorderSpacing)} {Px(style.BorderRowSpacing.Value)}";
            case "zoom": return CssValueTokenizer.Num(style.Zoom);
        }

        return null;
    }

    private static bool Almost(float a, float b) => Math.Abs(a - b) < 0.001f;

    /// <summary>Two axis keywords sharing one shorthand value: equal axes are written once
    /// (CSS Overscroll Behavior 1 §3, measured: 'contain' for both, 'contain auto' when only
    /// the x axis was declared).</summary>
    private static string AxisPair(string x, string y) => x == y ? x : $"{x} {y}";

    /// <summary>'contain-intrinsic-size' as a computed value: every axis carries its own
    /// 'auto', and 'content-visibility: auto' gives that keyword to a bare size too (CSS
    /// Containment 2 §3.1, measured: the same '33px 44px' reads back 'auto 33px auto 44px' once
    /// content-visibility is auto, and '33px 44px' while it is visible).</summary>
    private static string ContainIntrinsicSizeText(ComputedStyle style)
    {
        var forceAuto = style.ContentVisibility == ContentVisibilityType.Auto;
        string Axis(Length? size, bool hasAuto)
        {
            if (size == null) return hasAuto ? "auto none" : "none";
            return (hasAuto || forceAuto ? "auto " : "") + style.ComputedLengthCss(size);
        }
        var inlineText = Axis(style.ContainIntrinsicWidth, style.ContainIntrinsicWidthIsAuto);
        var blockText = Axis(style.ContainIntrinsicHeight, style.ContainIntrinsicHeightIsAuto);
        return inlineText == blockText ? inlineText : $"{inlineText} {blockText}";
    }

    /// <summary>'font-feature-settings' the way the reference engine serialises it: items
    /// sorted by tag and a value of 1 left out (measured: '\"liga\" 0, \"dlig\" 1' reads back
    /// '\"dlig\", \"liga\" 0'). Text that is not that shape is returned as authored.</summary>
    private static string FontFeatureSettingsText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
        var items = Css.MaskLayerParser.SplitTopLevel(text, ',');
        var features = new List<(string Tag, string Number)>(items.Count);
        foreach (var item in items)
        {
            var entry = item.Trim();
            var space = entry.LastIndexOf(' ');
            var tag = space < 0 ? entry : entry[..space].Trim();
            var number = space < 0 ? "1" : entry[(space + 1)..].Trim();
            if (tag.Length < 2 || tag[0] != '\"' || tag[tag.Length - 1] != '\"') return text;
            features.Add((tag, number));
        }
        features.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));
        var parts = new List<string>(features.Count);
        foreach (var (tag, number) in features) parts.Add(number == "1" ? tag : $"{tag} {number}");
        return string.Join(", ", parts);
    }

    /// <summary>A declaration list the way the computed value spells it: every item, separated
    /// by ', ' (measured: two transitions answer 'color, width' and '1s, 3s'), or |fallback|
    /// when the cascade never reached the property. Splitting at top level keeps a function
    /// like 'cubic-bezier(.1,.2,.3,.4)' in one piece.</summary>
    private static string ListOr(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var items = Css.MaskLayerParser.SplitTopLevel(value, ',');
        var kept = new List<string>(items.Count);
        foreach (var item in items)
        {
            var text = item.Trim();
            if (text.Length > 0) kept.Add(text);
        }
        return kept.Count > 0 ? string.Join(", ", kept) : fallback;
    }

    private static string ColorOrAuto(SkiaSharp.SKColor? color) => color.HasValue ? ColorText(color.Value) : "auto";

    private static string ObjectPositionText(Length? length)
    {
        if (length == null) return "50%";
        // A percentage is kept as a fraction inside the engine, so the printed form has to
        // multiply it back out: 'object-position: 25%' reads as '25%', not as '0.25%'.
        if (length is PercentLength p) return $"{CssValueTokenizer.Num(p.Value * 100f)}%";
        if (length is PixelLength px) return Px(px.Value);
        return length.ToString();
    }

    /// <summary>A &lt;string&gt; computed value, printed the way the CSSOM prints it: in double
    /// quotes, with the quotes and backslashes inside it escaped.</summary>
    private static string QuotedText(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            if (c is '"' or '\\') sb.Append('\\');
            sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string FloatTypeText(FloatType value) => CssFloatKeywords.ToCssString(value);

    private static string ClearTypeText(ClearType value) => CssFloatKeywords.ToCssString(value);

    private static string Px(float value) => CssValueTokenizer.Num(value) + "px";

    /// <summary>A border width's computed value: zero while its style is 'none' or 'hidden'
    /// (CSS 2.1 §10.3.2 — the width is suppressed at computed-value time, so an authored
    /// 'border-width: 1px' on a styleless box reads back as '0px').</summary>
    private static string BorderWidthText(float width, BorderStyle style) =>
        style == BorderStyle.None ? Px(0) : Px(width);

    /// <summary>The margin of the inline side 'direction' put a logical longhand on.</summary>
    private static string InlineSideMargin(ComputedStyle style, bool start) =>
        LengthText(style, IsRtl(style) ? (start ? style.MarginRight : style.MarginLeft)
                                       : (start ? style.MarginLeft : style.MarginRight));

    private static string InlineSidePadding(ComputedStyle style, bool start) =>
        LengthText(style, IsRtl(style) ? (start ? style.PaddingRight : style.PaddingLeft)
                                       : (start ? style.PaddingLeft : style.PaddingRight));

    private static bool IsRtl(ComputedStyle style) =>
        style.Direction.Equals("rtl", StringComparison.OrdinalIgnoreCase);

    /// <summary>A transform's computed value as the matrix it applies.</summary>
    private static string TransformText(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform)) return "none";
        var operations = Acrux.Core.Css.TransformParser.Parse(transform);
        if (operations.Count == 0) return transform.Trim();
        // A 3D function computes to a 4x4 and prints as matrix3d(); this engine has no text
        // form for that yet, and a 2D projection of it would be a wrong answer rather than a
        // different spelling of the right one, so those lists stay as authored.
        foreach (var operation in operations)
            if (operation.Function is "translate3d" or "rotate3d" or "rotatex" or "rotatey"
                or "matrix3d" or "perspective")
                return transform.Trim();

        var m = Acrux.Core.Css.TransformParser.ToMatrix(operations, 0, 0);
        return $"matrix({CssValueTokenizer.Num(m.ScaleX)}, {CssValueTokenizer.Num(m.SkewY)}, "
             + $"{CssValueTokenizer.Num(m.SkewX)}, {CssValueTokenizer.Num(m.ScaleY)}, "
             + $"{CssValueTokenizer.Num(m.TransX)}, {CssValueTokenizer.Num(m.TransY)})";
    }

    /// <summary>The 'font' shorthand's computed value: its longhands in the shorthand's own
    /// order, with the ones that sit at their initial left out (CSS Fonts 4 §5.3, measured:
    /// 'font: bold 12px/150% "Times New Roman", Georgia' reads back as
    /// '700 12px / 18px "Times New Roman", Georgia', and a plain 'font: 12px Arial' carries no
    /// slash at all because the line-height it did not set is still 'normal').</summary>
    private static string FontShorthandText(ComputedStyle style)
    {
        var parts = new System.Collections.Generic.List<string>(8);
        if (style.FontStyleCssText != "normal") parts.Add(style.FontStyleCssText);
        if (style.FontVariantCaps != "normal") parts.Add(style.FontVariantCaps);
        if ((int)style.FontWeight != 400) parts.Add(((int)style.FontWeight).ToString(CultureInfo.InvariantCulture));
        if (style.FontStretch is not ("" or "normal")) parts.Add(style.FontStretch);
        parts.Add(Px(style.FontSize));
        if (!style.LineHeightIsNormal)
        {
            parts.Add("/");
            parts.Add(style.LineHeightCssText);
        }
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
            parts.Add(style.FontFamily);
        // The size, its slash and the family are printed tight to what they belong to: the
        // reference engine puts one space between the components of the list.
        return string.Join(" ", parts);
    }

    /// <summary>One mask layer list, cycled to the number of mask images (see
    /// Css.MaskKeywordSerializer.LayerList). Shared by all seven geometry longhands so the
    /// image count is derived in exactly one place.</summary>
    private static string MaskLayerList(ComputedStyle style, System.Collections.Generic.IReadOnlyList<string>? layers,
        string? scalar, string initial, System.Func<string, string>? normalize)
    {
        var images = Css.MaskLayerParser.SplitTopLevel(
            string.IsNullOrEmpty(style.MaskImage) ? (style.Mask ?? string.Empty) : style.MaskImage, ',');
        var text = Css.MaskKeywordSerializer.LayerList(layers, scalar, images.Count, normalize);
        return text.Length > 0 ? text : initial;
    }

    private static string ColorText(SkiaSharp.SKColor c)
    {
        if (c.Alpha == 255) return $"rgb({c.Red}, {c.Green}, {c.Blue})";
        // A fully transparent color is not spelled 'transparent' on the computed surface: the
        // reference engine prints the rgba() form for it here and everywhere else (measured:
        // 'color: transparent' reads back as 'rgba(0, 0, 0, 0)'), so the keyword never appears.
        // The channels are kept — a transparent red still reads as a transparent red.
        if (c.Alpha == 0) return $"rgba({c.Red}, {c.Green}, {c.Blue}, 0)";
        return $"rgba({c.Red}, {c.Green}, {c.Blue}, {CssValueTokenizer.AlphaText(c.Alpha)})";
    }

    /// <summary>Text form of an optional color. Both the keyword 'currentcolor' and a value the
    /// cascade has not filled in resolve to the element's own text color: the computed value of
    /// a color property never contains the keyword (measured).</summary>
    private static string ResolveColorText(string? value, SkiaSharp.SKColor current)
    {
        if (string.IsNullOrWhiteSpace(value)) return ColorText(current);
        if (string.Equals(value, "currentcolor", StringComparison.OrdinalIgnoreCase)) return ColorText(current);
        return ColorText(ColorParser.Parse(value));
    }

    private static string LengthText(ComputedStyle owner, Length? length)
    {
        if (length == null) return "auto";
        // A length resolves against the element's own font at computed-value time (CSS Values
        // 3 §7), so 'width: 10em' on a box whose font is 12px reads back as '120px'; a
        // percentage stays a percentage and 'auto' stays a keyword.
        return owner.ComputedLengthCss(length);
    }

    /// <summary>One side of a scroll inset (CSS Scroll Snap 1 §6.1). The initial value is the
    /// length zero, so an untouched side reads back as '0px' — never as 'auto', which is only
    /// another spelling the page may use for that same zero (measured).</summary>
    private static string ScrollInsetText(ComputedStyle style, Length? length) =>
        length == null ? "0px" : LengthText(style, length);

    private static string RadiusText(float x, float y)
    {
        var horizontal = RadiusEndpoint(x);
        var vertical = RadiusEndpoint(y);
        if (horizontal == vertical) return horizontal;
        return $"{horizontal} {vertical}";
    }

    /// <summary>One radius endpoint. A percentage is kept as a percentage — that is what the
    /// computed value of 'border-radius' is (CSS Backgrounds 3 §5.3, measured: a 50%/25% corner
    /// reads back as '50% 25%', not as the pixels it will resolve to) — and the engine marks it
    /// by storing the negative of the number, because one float per axis has no room for a
    /// unit.</summary>
    private static string RadiusEndpoint(float radius) =>
        radius < 0 ? $"{CssValueTokenizer.Num(-radius)}%" : Px(radius);

    private static string BackgroundPosText(Length? length, bool cycled = false)
    {
        if (length == null) return "0%";
        // PercentLength carries the fraction, so 0.5 is the 50% the platform prints.
        if (length is PercentLength p) return $"{CssValueTokenizer.Num((double)p.Value * 100)}%";
        if (length is PixelLength px) return Px(px.Value);
        // An offset measured from the far edge is reported as the arithmetic it stands for
        // (measured: 'background-position: right 10px bottom 20px' reads back as
        // 'calc(100% - 10px) calc(100% - 20px)') — but only in a layer that was written down:
        // the position shorthand forgets the edge as soon as it repeats its list over the
        // leftover image layers, while the two axis longhands do not (measured: that same
        // position on a two-layer background reads back as 'calc(100% - 10px) calc(100% - 20px),
        // 10px 20px', with 'background-position-x' still saying 'calc(100% - 10px), calc(100% -
        // 10px)'). A quirk of how the reference engine stores the two, not of the grammar.
        if (length is FarEdgeLength far)
            return cycled
                ? BackgroundPosText(far.Offset)
                : $"calc(100% - {BackgroundPosText(far.Offset)})";
        return length.ToString();
    }

    /// <summary>The origin as the reference engine prints it: two positional values plus, when it
    /// says something, a depth. The cascade has already filed every keyword on its own axis and
    /// put the lengths in pixels, so all that is left here is the shape of the list — an axis
    /// nobody named keeps the initial '50%' (measured: 'transform-origin: 10px' reads back
    /// '10px 50%'), a lone keyword stands for the axis it names ('bottom' is '50% 100%'), and the
    /// depth is dropped when it is the initial zero (measured: the engine prints two values, never
    /// three, for '50% 50% 0'). Pixels come later, from the host that knows the box.</summary>
    private static string TransformOriginText(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return "50% 50%";
        var parts = origin.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "50% 50%";
        static string Fold(string token) => token.ToLowerInvariant() switch
        {
            "left" => "0%",
            "right" => "100%",
            "top" => "0%",
            "bottom" => "100%",
            "center" => "50%",
            _ => token,
        };
        string x, y;
        if (parts.Length == 1)
        {
            // A single value is a keyword of the vertical axis or a length of the horizontal one
            // (CSS Transforms 1 §3); 'transform-origin: top' is the y axis moving, not the x.
            var only = parts[0].ToLowerInvariant();
            if (only is "top" or "bottom") { x = "50%"; y = Fold(only); }
            else { x = Fold(only); y = "50%"; }
        }
        else
        {
            x = Fold(parts[0]);
            y = Fold(parts[1]);
        }
        if (parts.Length >= 3 && parts[2] != "0" && parts[2] != "0px")
            return $"{x} {y} {parts[2]}";
        return $"{x} {y}";
    }

    private static string BackgroundSizeText(ComputedStyle style)
    {
        // 'cover' and 'contain' are computed values in their own right (CSS Backgrounds 3 §4.8):
        // the reference engine prints the keyword a layer was sized with rather than the box it
        // resolves against, which it does not know at computed-value time.
        if (style.BackgroundSize == BackgroundSizeType.Cover) return "cover";
        if (style.BackgroundSize == BackgroundSizeType.Contain) return "contain";
        var width = SizeEndpointText(style.BackgroundSizeWidth);
        var height = SizeEndpointText(style.BackgroundSizeHeight);
        return SizePairText(width, height);
    }

    /// <summary>One layer of a 'background-size' list, spelled the way the scalar property is.</summary>
    private static string SizeLayerText(BackgroundSizeLayer layer)
    {
        if (layer.Type == BackgroundSizeType.Cover) return "cover";
        if (layer.Type == BackgroundSizeType.Contain) return "contain";
        return SizePairText(SizeEndpointText(layer.Width), SizeEndpointText(layer.Height));
    }

    /// <summary>One layer of a 'background-repeat' list. Two axes that agree are one word, and
    /// the pair that is one axis repeating while the other does not is the 'repeat-x' / 'repeat-y'
    /// the grammar spells with a single word (CSS Backgrounds 3 §4.1.1, measured).</summary>
    private static string RepeatLayerText(BackgroundRepeatPair pair)
    {
        if (pair.X == pair.Y) return RepeatAxisText(pair.X);
        if (pair.X == BackgroundRepeat.Repeat && pair.Y == BackgroundRepeat.NoRepeat) return "repeat-x";
        if (pair.X == BackgroundRepeat.NoRepeat && pair.Y == BackgroundRepeat.Repeat) return "repeat-y";
        return $"{RepeatAxisText(pair.X)} {RepeatAxisText(pair.Y)}";
    }

    private static string RepeatAxisText(BackgroundRepeat axis) =>
        CssEnumFormatter.CssKeywordFromEnum(axis.ToString());

    /// <summary>'background-position' with one entry per image layer: the two axis lists cycle
    /// against each other independently, and a box that has only the position scalars repeats that
    /// one position for every layer (CSS Backgrounds 3 §2, measured against
    /// 'background-image: url(a), url(b); background-position: 10px 20px' reading back twice).</summary>
    private static string BackgroundPositionText(ComputedStyle style)
    {
        var count = BackgroundLayerCount(style);
        var xLayers = style.BackgroundPositionXLayers;
        var yLayers = style.BackgroundPositionYLayers;
        var parts = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) parts.Append(", ");
            parts.Append(BackgroundPosText(PickAxis(xLayers, style.BackgroundPositionX, i), IsCycledLayer(xLayers, i)))
                 .Append(' ')
                 .Append(BackgroundPosText(PickAxis(yLayers, style.BackgroundPositionY, i), IsCycledLayer(yLayers, i)));
        }
        return parts.ToString();
    }

    /// <summary>True for a layer the axis list had to be repeated into rather than one it wrote
    /// down. A list the box never carried is one position, so everything after the first layer
    /// is a repeat of it.</summary>
    private static bool IsCycledLayer(List<Length?>? layers, int index) => index >= (layers?.Count ?? 1);

    private static string BackgroundAxisText(ComputedStyle style, bool horizontal)
    {
        var layers = horizontal ? style.BackgroundPositionXLayers : style.BackgroundPositionYLayers;
        var scalar = horizontal ? style.BackgroundPositionX : style.BackgroundPositionY;
        var count = BackgroundLayerCount(style);
        var parts = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) parts.Append(", ");
            parts.Append(BackgroundPosText(PickAxis(layers, scalar, i)));
        }
        return parts.ToString();
    }

    private static Length? PickAxis(List<Length?>? layers, Length? scalar, int index) =>
        layers is { Count: > 0 } ? layers[index % layers.Count] : scalar;

    /// <summary>How many layers the background of this box has. An absent or empty image list is
    /// the one layer of 'none' the initial value describes.</summary>
    private static int BackgroundLayerCount(ComputedStyle style) =>
        style.BackgroundImage is { Count: > 0 } images ? images.Count : 1;

    private static string AttachmentText(BackgroundAttachment attachment) => attachment switch
    {
        BackgroundAttachment.Fixed => "fixed",
        BackgroundAttachment.Local => "local",
        _ => "scroll",
    };

    /// <summary>A per-layer background value as the reference engine reports it: exactly one entry
    /// per image layer, the specified list cycled round and its extras truncated (CSS Backgrounds 3
    /// §2, measured: 'background-repeat: repeat, no-repeat' on a one-layer background reads back as
    /// 'repeat', and 'background-origin: border-box, padding-box, content-box' as 'border-box').
    /// With no list at all the scalar the box carries is the entry every layer gets.</summary>
    private static string CycleListText<T>(List<T>? layers, string first, Func<T, string> entry, int count)
    {
        var parts = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) parts.Append(", ");
            parts.Append(layers is { Count: > 0 } ? entry(layers[i % layers.Count]) : first);
        }
        return parts.ToString();
    }

    /// <summary>The two sides of a layer's size. A pair that ends in 'auto' is printed without
    /// it (measured: 'background-size: 30px auto' reads back as '30px', while 'auto 30px' keeps
    /// both words); a pair that says 'auto' twice is one word (measured: the initial size of a
    /// layer reads back as 'auto', not as 'auto auto').</summary>
    private static string SizePairText(string width, string height)
    {
        if (width == "auto" && height == "auto") return "auto";
        if (height == "auto") return width;
        return $"{width} {height}";
    }

    private static string SizeEndpointText(Length? length)
    {
        if (length == null) return "auto";
        if (length is PercentLength p) return $"{CssValueTokenizer.Num((double)p.Value * 100)}%";
        if (length is PixelLength px) return Px(px.Value);
        return length.ToString();
    }

    /// <summary>One shadow layer, printed the way the platform prints it: the colour first,
    /// then the offsets, and the 'inset' keyword last (CSS Backgrounds 3 §4.3, measured:
    /// 'rgb(9, 8, 7) 1px 2px 3px 4px inset'). The grammar accepts the components in any order,
    /// so the serialisation has to pick one.</summary>
    private static string ShadowText(SkiaSharp.SKColor color, float offsetX, float offsetY,
        float blur, float spread, bool inset, bool withSpread)
    {
        var sb = new StringBuilder();
        sb.Append(ColorText(color)).Append(' ')
          .Append(Px(offsetX)).Append(' ').Append(Px(offsetY)).Append(' ').Append(Px(blur));
        if (withSpread) sb.Append(' ').Append(Px(spread));
        if (inset) sb.Append(" inset");
        return sb.ToString();
    }

    private static string BoxShadowText(ComputedStyle style)
    {
        if (style.BoxShadow == null || style.BoxShadow.Count == 0) return "none";
        var sb = new StringBuilder();
        foreach (var s in style.BoxShadow)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(ShadowText(s.Color, s.OffsetX, s.OffsetY, s.BlurRadius, s.Spread, s.Inset,
                withSpread: true));
        }
        return sb.ToString();
    }

    private static string TextShadowText(ComputedStyle style)
    {
        if (style.TextShadow == null || style.TextShadow.Count == 0) return "none";
        var sb = new StringBuilder();
        foreach (var s in style.TextShadow)
        {
            if (sb.Length > 0) sb.Append(", ");
            // 'text-shadow' has no spread, so its fourth length would be a grammar error; the
            // reference engine still prints the blur and stops (measured: '1px 2px 0px').
            sb.Append(ShadowText(s.Color, s.OffsetX, s.OffsetY, s.BlurRadius, 0, false,
                withSpread: false));
        }
        return sb.ToString();
    }

    // ============================================================== shorthands
    // A computed style is a snapshot of every property that applies, so a shorthand asked of
    // one answers with the longhands it stands for (CSSOM §6). Two things follow from that and
    // both are measured against the reference engine below: the components come out in the
    // grammar's order, which is not the author's, and a list of four sides collapses to the
    // shortest spelling that says the same thing.

    /// <summary>The computed value of a shorthand, or null when this engine has no model for
    /// the name at all. A shorthand whose parts disagree prints as the empty string, which is
    /// what the platform hands back for e.g. 'border' when the four sides differ.</summary>
    private static string? ShorthandValueOf(ComputedStyle style, string property)
    {
        switch (property)
        {
            // ------------------------------------------------- four-sided lists
            case "margin": return BoxList(style, "margin-top", "margin-right", "margin-bottom", "margin-left");
            case "padding": return BoxList(style, "padding-top", "padding-right", "padding-bottom", "padding-left");
            case "inset": return BoxList(style, "top", "right", "bottom", "left");
            case "border-width": return BoxList(style, "border-top-width", "border-right-width", "border-bottom-width", "border-left-width");
            case "border-style": return BoxList(style, "border-top-style", "border-right-style", "border-bottom-style", "border-left-style");
            case "border-color": return BoxList(style, "border-top-color", "border-right-color", "border-bottom-color", "border-left-color");
            case "scroll-margin":
                return BoxList(style, "scroll-margin-top", "scroll-margin-right",
                    "scroll-margin-bottom", "scroll-margin-left");
            case "scroll-padding":
                return BoxList(style, "scroll-padding-top", "scroll-padding-right",
                    "scroll-padding-bottom", "scroll-padding-left");

            // ------------------------------------------------ logical box pairs
            case "margin-block": return Pair(Longhand(style, "margin-block-start"), Longhand(style, "margin-block-end"));
            case "margin-inline": return Pair(Longhand(style, "margin-inline-start"), Longhand(style, "margin-inline-end"));
            case "padding-block": return Pair(Longhand(style, "padding-block-start"), Longhand(style, "padding-block-end"));
            case "padding-inline": return Pair(Longhand(style, "padding-inline-start"), Longhand(style, "padding-inline-end"));

            // ---------------------------------------------------------- borders
            case "border-top": return BorderSideText(style, "border-top");
            case "border-right": return BorderSideText(style, "border-right");
            case "border-bottom": return BorderSideText(style, "border-bottom");
            case "border-left": return BorderSideText(style, "border-left");
            case "border-block-start": return BorderSideText(style, "border-block-start");
            case "border-block-end": return BorderSideText(style, "border-block-end");
            case "border-inline-start": return BorderSideText(style, "border-inline-start");
            case "border-inline-end": return BorderSideText(style, "border-inline-end");
            case "border": return AllBorders(style);
            case "border-block": return Pair(BorderSideText(style, "border-block-start"),
                                             BorderSideText(style, "border-block-end"));
            case "border-inline": return Pair(BorderSideText(style, "border-inline-start"),
                                              BorderSideText(style, "border-inline-end"));
            case "border-radius": return BorderRadiusText(style);
            case "outline":
                // Measured order: the colour, the style, the width.
                return $"{Longhand(style, "outline-color")} {Longhand(style, "outline-style")} "
                     + $"{Longhand(style, "outline-width")}";

            // ----------------------------------------------------------- boxes
            case "flex":
                return $"{Longhand(style, "flex-grow")} {Longhand(style, "flex-shrink")} "
                     + $"{Longhand(style, "flex-basis")}";
            case "columns": return ColumnsText(style);
            // Measured: 'column-rule' prints its width, then the style only when it is not the
            // initial 'none', then the colour — so a '3px' rule reads back as '3px rgb(...)'.
            case "column-rule":
            {
                var ruleStyle = Longhand(style, "column-rule-style");
                return ruleStyle == "none"
                    ? $"{Longhand(style, "column-rule-width")} {Longhand(style, "column-rule-color")}"
                    : $"{Longhand(style, "column-rule-width")} {ruleStyle} "
                      + $"{Longhand(style, "column-rule-color")}";
            }
            case "overflow": return Pair(Longhand(style, "overflow-x"), Longhand(style, "overflow-y"));
            case "gap": return Pair(Longhand(style, "row-gap"), Longhand(style, "column-gap"));
            case "place-content":
                return Pair(Longhand(style, "align-content"), Longhand(style, "justify-content"));
            case "place-items":
                return Pair(Longhand(style, "align-items"), Longhand(style, "justify-items"));
            case "place-self":
                return Pair(Longhand(style, "align-self"), Longhand(style, "justify-self"));

            // ----------------------------------------------------------- text
            case "text-decoration": return TextDecorationText(style);
            case "text-emphasis":
                return $"{TextEmphasisStyleText(style.TextEmphasisStyle)} {ColorText(EmphasisColor(style))}";
            case "list-style":
                // Measured order: position, image, type.
                return $"{Longhand(style, "list-style-position")} {Longhand(style, "list-style-image")} "
                     + $"{Longhand(style, "list-style-type")}";

            // ----------------------------------------------------------- grid
            case "grid-row": return GridLine(Longhand(style, "grid-row-start"), Longhand(style, "grid-row-end"));
            case "grid-column":
                return GridLine(Longhand(style, "grid-column-start"), Longhand(style, "grid-column-end"));
            case "grid-area": return GridAreaText(style);
            case "grid-template": return GridTemplateText(style);

            // -------------------------------------------------------- time lists
            case "animation": return AnimationText(style);
            case "transition": return TransitionText(style);

            // ------------------------------------------------------ backgrounds
            case "background": return BackgroundText(style);
            case "mask": return MaskText(style);
            default: return null;
        }
    }

    /// <summary>The line list in the engine's own order — the three that draw, then the three that
    /// only say themselves (measured: 'blink underline' computes 'underline blink', and
    /// 'underline overline' keeps that order).</summary>
    private static string TextDecorationLineText(TextDecorationLineType line)
    {
        var parts = new List<string>();
        if (line.HasUnderline()) parts.Add("underline");
        if (line.HasOverline()) parts.Add("overline");
        if (line.HasLineThrough()) parts.Add("line-through");
        if ((line & TextDecorationLineType.Blink) != 0) parts.Add("blink");
        if ((line & TextDecorationLineType.SpellingError) != 0) parts.Add("spelling-error");
        if ((line & TextDecorationLineType.GrammarError) != 0) parts.Add("grammar-error");
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    /// <summary>One longhand's computed text. An empty fallback rather than null: a shorthand
    /// that is missing a part still has to say something about the parts it has.</summary>
    private static string Longhand(ComputedStyle style, string name) => ValueOf(style, name) ?? "";

    /// <summary>The four-value collapse of a box shorthand (CSS 2.1 §5.10.1, measured): a list
    /// that repeats itself once prints as one value, one that mirrors top onto bottom and left
    /// onto right prints as two, and only a list whose left edge differs from its right keeps
    /// all four.</summary>
    private static string BoxList(ComputedStyle style, string top, string right, string bottom,
        string left)
        => BoxListText(Longhand(style, top), Longhand(style, right), Longhand(style, bottom),
            Longhand(style, left));

    private static string BoxListText(string top, string right, string bottom, string left)
    {
        if (left != right) return $"{top} {right} {bottom} {left}";
        if (bottom == top) return top == right ? top : $"{top} {right}";
        return $"{top} {right} {bottom}";
    }

    /// <summary>Two values that a shorthand may say at once. The pair merges into one value when
    /// both halves agree: measured across 'gap', 'overflow', 'margin-block'/'-inline',
    /// 'padding-block'/'-inline', 'border-block'/'-inline' and the 'place-*' properties, every
    /// two-sided shorthand a reference engine prints this way — none keeps the repeat.</summary>
    private static string Pair(string first, string second)
        => first == second ? first : $"{first} {second}";

    private static string BorderSideText(ComputedStyle style, string side)
        => $"{Longhand(style, side + "-width")} {Longhand(style, side + "-style")} "
         + $"{Longhand(style, side + "-color")}";

    /// <summary>The 'border' shorthand: one triple if every side agrees, and nothing at all if
    /// they do not (measured: a box with a dotted left edge and a solid top reads back an empty
    /// 'border', because no single border declaration could have produced the other three).</summary>
    private static string AllBorders(ComputedStyle style)
    {
        var top = BorderSideText(style, "border-top");
        if (top != BorderSideText(style, "border-right")
            || top != BorderSideText(style, "border-bottom")
            || top != BorderSideText(style, "border-left")) return "";
        return top;
    }

    private static string BorderRadiusText(ComputedStyle style)
    {
        var horizontal = BoxListText(
            RadiusEndpoint(style.BorderTopLeftRadius), RadiusEndpoint(style.BorderTopRightRadius),
            RadiusEndpoint(style.BorderBottomRightRadius), RadiusEndpoint(style.BorderBottomLeftRadius));
        var vertical = BoxListText(
            RadiusEndpoint(style.BorderTopLeftRadiusY), RadiusEndpoint(style.BorderTopRightRadiusY),
            RadiusEndpoint(style.BorderBottomRightRadiusY), RadiusEndpoint(style.BorderBottomLeftRadiusY));
        // The two ellipses share one spelling when they are the same shape (CSS Backgrounds 3
        // §5.3, measured: '10px 20% / 30px 5%' and a plain '0px').
        return horizontal == vertical ? horizontal : $"{horizontal} / {vertical}";
    }

    private static string ColumnsText(ComputedStyle style)
    {
        var width = Longhand(style, "column-width");
        var count = Longhand(style, "column-count");
        if (width == "auto") return count;
        if (count == "auto") return width;
        return $"{width} {count}";
    }

    private static string TextDecorationText(ComputedStyle style)
    {
        // Measured order: line, thickness, style, colour — and a part that is still at its
        // initial is left out, so a plain underline says 'underline' and nothing more.
        var parts = new List<string> { Longhand(style, "text-decoration-line") };
        var thickness = Longhand(style, "text-decoration-thickness");
        if (thickness != "auto") parts.Add(thickness);
        var decorationStyle = Longhand(style, "text-decoration-style");
        if (decorationStyle != "solid") parts.Add(decorationStyle);
        if (!style.TextDecorationColorIsAuto) parts.Add(ColorText(style.TextDecorationColor));
        return string.Join(" ", parts);
    }

    /// <summary>The 'text-emphasis' shape without its initial 'filled' (measured: a page that
    /// writes 'filled circle' reads back 'circle').</summary>
    private static string TextEmphasisStyleText(string authored)
    {
        var text = (authored ?? "").Trim();
        return text.StartsWith("filled ", StringComparison.OrdinalIgnoreCase)
            ? text.Substring("filled ".Length).Trim()
            : (text.Length == 0 ? "none" : text);
    }

    private static SkiaSharp.SKColor EmphasisColor(ComputedStyle style) =>
        string.IsNullOrWhiteSpace(style.TextEmphasisColor)
        || style.TextEmphasisColor.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)
            ? style.Color
            : ColorParser.Parse(style.TextEmphasisColor);

    private static string GridLine(string start, string end) =>
        start == "auto" && end == "auto" ? "auto" : $"{start} / {end}";

    private static string GridAreaText(ComputedStyle style)
    {
        // row-start / column-start / row-end / column-end, with the trailing 'auto's dropped
        // (measured: 'grid-area: 1 / 2 / span 2 / auto' reads back as '1 / 2 / span 2').
        var lines = new[]
        {
            Longhand(style, "grid-row-start"), Longhand(style, "grid-column-start"),
            Longhand(style, "grid-row-end"), Longhand(style, "grid-column-end"),
        };
        int last = lines.Length - 1;
        while (last > 0 && lines[last] == "auto") last--;
        if (last == 0 && lines[0] == "auto") return "auto";
        return string.Join(" / ", lines, 0, last + 1);
    }

    private static string GridTemplateText(ComputedStyle style)
    {
        var rows = GridTemplateRowsText(style);
        var columns = GridTemplateColumnsText(style);
        if (rows == "none" && columns == "none") return "none";
        // The template is the row list, then the column list on the other side of a slash.
        return $"{rows} / {columns}";
    }

    /// <summary>The column side of 'grid-template': the list as authored (measured keeps the
    /// '1fr' and the 'repeat()', where the longhand has already been replaced by the used
    /// sizes), or the used sizes when nothing was authored — that is the one case the shorthand
    /// reports a layout result.</summary>
    private static string GridTemplateColumnsText(ComputedStyle style)
    {
        var authored = string.IsNullOrWhiteSpace(style.GridTemplateColumns)
            ? null : style.GridTemplateColumns!.Trim();
        if (authored != null && authored != "none") return authored;
        return style.GridUsedColumnSizes is { Length: > 0 } used
            ? string.Join(" ", used.Select(v => Px(v))) : "none";
    }

    /// <summary>The row side of 'grid-template', with the named areas written in front of the
    /// tracks they occupy (measured: 'grid-template-areas: "a" "b"' over two rows reads back
    /// '"a" 10px "b" 20px'). An engine that has not been asked to lay the grid out reports the
    /// list without them.</summary>
    private static string GridTemplateRowsText(ComputedStyle style)
    {
        var authored = string.IsNullOrWhiteSpace(style.GridTemplateRows)
            ? null : style.GridTemplateRows!.Trim();
        var list = authored != null && authored != "none"
            ? authored
            : style.GridUsedRowSizes is { Length: > 0 } used
                ? string.Join(" ", used.Select(v => Px(v))) : "none";
        if (list == "none") return list;
        var areas = AreaRowStrings(style.GridTemplateAreas);
        return areas.Count > 0 ? InterleaveAreas(areas, list) : list;
    }

    /// <summary>One quoted string per row of 'grid-template-areas', in the author's own
    /// spelling of the names.</summary>
    private static List<string> AreaRowStrings(string? areas)
    {
        var rows = new List<string>();
        if (string.IsNullOrWhiteSpace(areas)) return rows;
        int pos = 0;
        while (pos < areas.Length)
        {
            int open = areas.IndexOf('"', pos);
            if (open < 0) break;
            int close = areas.IndexOf('"', open + 1);
            if (close < 0) break;
            rows.Add($"\"{areas.Substring(open + 1, close - open - 1)}\"");
            pos = close + 1;
        }
        return rows;
    }

    /// <summary>Put each area string in front of the track of the same index; tracks beyond the
    /// last area keep their own size and the extra area strings are dropped.</summary>
    private static string InterleaveAreas(List<string> areas, string list)
    {
        var tracks = SplitTrackTokens(list);
        if (tracks.Count == 0) return list;
        var parts = new List<string>(tracks.Count);
        for (int i = 0; i < tracks.Count; i++)
            parts.Add(i < areas.Count ? $"{areas[i]} {tracks[i]}" : tracks[i]);
        return string.Join(" ", parts);
    }

    /// <summary>The track list as the CSSOM reports it: the used sizes, with the author's line
    /// names kept between them while they still fit (measured: '[a] 1fr [b] 1fr [c]' in a 100px
    /// grid reads back '[a] 50px [b] 50px [c]'). A list that does not divide into as many
    /// tracks as the layout resolved — a 'repeat()' is the usual case — keeps only the sizes.</summary>
    private static string GridTrackListText(string? specified, float[]? used)
    {
        var authored = string.IsNullOrWhiteSpace(specified) ? null : specified!.Trim();
        if (used == null || used.Length == 0) return authored ?? "none";
        var sizes = used.Select(v => Px(v)).ToList();
        if (authored == null || authored == "none") return string.Join(" ", sizes);
        return InterleaveLineNames(authored, sizes) ?? string.Join(" ", sizes);
    }

    /// <summary>Rebuild a track list from the author's line-name groups and a new size per
    /// track, or null when the authored text does not describe that many tracks.</summary>
    private static string? InterleaveLineNames(string authored, IReadOnlyList<string> sizes)
    {
        var groups = new List<List<string>>();     // names before each track, plus a trailing group
        var tracks = new List<string>();
        var pending = new List<string>();
        foreach (var token in SplitTrackTokensWithNames(authored))
        {
            if (token.StartsWith("[")) pending.Add(token);
            else { groups.Add(pending); pending = new List<string>(); tracks.Add(token); }
        }
        groups.Add(pending);
        if (tracks.Count != sizes.Count) return null;
        var parts = new List<string>(sizes.Count * 2);
        for (int i = 0; i < sizes.Count; i++)
        {
            if (groups[i].Count > 0) parts.Add(string.Join(" ", groups[i]));
            parts.Add(sizes[i]);
        }
        if (groups.Count > sizes.Count && groups[^1].Count > 0) parts.Add(string.Join(" ", groups[^1]));
        return string.Join(" ", parts);
    }

    /// <summary>Split a track list into its top-level tokens, keeping '[…]' groups, quoted
    /// strings and functions in one piece.</summary>
    private static List<string> SplitTrackTokens(string list)
        => SplitTrackTokensWithNames(list).Where(t => !t.StartsWith("[")).ToList();

    private static List<string> SplitTrackTokensWithNames(string list)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < list.Length)
        {
            while (i < list.Length && (char.IsWhiteSpace(list[i]) || list[i] == ',')) i++;
            if (i >= list.Length) break;
            int start = i;
            int depth = 0;
            bool quoted = false;
            while (i < list.Length)
            {
                char c = list[i];
                if (quoted) { if (c == '"') quoted = false; }
                else if (c == '"') quoted = true;
                else if (c == '[' || c == '(') depth++;
                else if (c == ']' || c == ')') depth--;
                else if (depth == 0 && (char.IsWhiteSpace(c) || c == ',')) break;
                i++;
            }
            tokens.Add(list.Substring(start, i - start));
        }
        return tokens;
    }

    private static string AnimationText(ComputedStyle style)
    {
        // One item per layer, in the grammar's order, and a part still at its initial is left
        // out (measured: '1s a, 2s linear b' for two animations whose other parts are default).
        var names = List(Longhand(style, "animation-name"));
        var durations = List(Longhand(style, "animation-duration"));
        var timings = List(Longhand(style, "animation-timing-function"));
        var delays = List(Longhand(style, "animation-delay"));
        var counts = List(Longhand(style, "animation-iteration-count"));
        var directions = List(Longhand(style, "animation-direction"));
        var fills = List(Longhand(style, "animation-fill-mode"));
        var states = List(Longhand(style, "animation-play-state"));
        int layers = System.Math.Max(names.Count, durations.Count);
        var items = new List<string>(System.Math.Max(1, layers));
        for (int i = 0; i < System.Math.Max(1, layers); i++)
        {
            var parts = new List<string>(8);
            AddUnlessAt(parts, At(durations, i), "0s");
            AddUnlessAt(parts, At(timings, i), "ease");
            AddUnlessAt(parts, At(delays, i), "0s");
            AddUnlessAt(parts, At(counts, i), "1");
            AddUnlessAt(parts, At(directions, i), "normal");
            AddUnlessAt(parts, At(fills, i), "none");
            AddUnlessAt(parts, At(states, i), "running");
            var name = At(names, i);
            if (parts.Count == 0) items.Add(name.Length == 0 ? "none" : name);
            else if (name.Length > 0 && name != "none") { parts.Add(name); items.Add(string.Join(" ", parts)); }
            else items.Add(string.Join(" ", parts));
        }
        return items.Count == 1 && items[0] == "none" ? "none" : string.Join(", ", items);
    }

    private static string TransitionText(ComputedStyle style)
    {
        var properties = List(Longhand(style, "transition-property"));
        var durations = List(Longhand(style, "transition-duration"));
        var timings = List(Longhand(style, "transition-timing-function"));
        var delays = List(Longhand(style, "transition-delay"));
        int layers = System.Math.Max(properties.Count, durations.Count);
        var items = new List<string>(System.Math.Max(1, layers));
        for (int i = 0; i < System.Math.Max(1, layers); i++)
        {
            var parts = new List<string>(4);
            AddUnlessAt(parts, At(properties, i), "all");
            AddUnlessAt(parts, At(durations, i), "0s");
            AddUnlessAt(parts, At(timings, i), "ease");
            AddUnlessAt(parts, At(delays, i), "0s");
            items.Add(parts.Count == 0 ? "all" : string.Join(" ", parts));
        }
        return string.Join(", ", items);
    }

    private static List<string> List(string text)
    {
        var items = Css.MaskLayerParser.SplitTopLevel(text ?? "", ',');
        var kept = new List<string>(items.Count);
        foreach (var item in items)
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0) kept.Add(trimmed);
        }
        return kept;
    }

    private static string At(List<string> list, int index) =>
        list.Count == 0 ? "" : list[index % list.Count];

    private static void AddUnlessAt(List<string> into, string value, string initial)
    {
        if (value.Length == 0 || value == initial) return;
        into.Add(value);
    }

    private static string BackgroundText(ComputedStyle style)
    {
        var images = style.BackgroundImage is { Count: > 0 }
            ? new List<string>(style.BackgroundImage)
            : new List<string> { "none" };
        var color = Longhand(style, "background-color");
        var repeat = Longhand(style, "background-repeat");
        var attachment = Longhand(style, "background-attachment");
        var position = Longhand(style, "background-position");
        var size = Longhand(style, "background-size");
        var origin = Longhand(style, "background-origin");
        var clip = Longhand(style, "background-clip");
        var items = new List<string>(images.Count);
        for (int i = 0; i < images.Count; i++)
        {
            var parts = new List<string>(8)
            {
                // The colour belongs to the last layer only (CSS Backgrounds 3 §4.3), and the
                // layers before it say the initial colour — in its rgba() spelling, because a
                // computed color is never the keyword 'transparent' (measured).
                i == images.Count - 1 ? color : ColorText(new SkiaSharp.SKColor(0, 0, 0, 0)),
                // The image is read back the way the 'background-image' longhand prints it: an
                // absolute url in quotes, not the relative token the page wrote.
                Css.ImageValueCanonicalizer.Canonicalize(images[i]), repeat, attachment,
                // The size is part of the position slot even when it is the initial 'auto'
                // (measured: '0% 0% / auto' is what a plain layer reads back as).
                $"{position} / {size}",
                origin, clip,
            };
            items.Add(string.Join(" ", parts));
        }
        return string.Join(", ", items);
    }

    private static string MaskText(ComputedStyle style)
    {
        var images = List(string.IsNullOrEmpty(style.MaskImage) ? (style.Mask ?? "none") : style.MaskImage!);
        if (images.Count == 0) images.Add("none");
        var position = MaskLayerList(style, style.MaskPositionLayers, style.MaskPosition, "0% 0%",
            Css.MaskKeywordSerializer.Position);
        var size = MaskLayerList(style, style.MaskSizeLayers, style.MaskSize, "auto", null);
        var repeat = MaskLayerList(style, style.MaskRepeatLayers, style.MaskRepeat, "repeat",
            Css.MaskKeywordSerializer.Repeat);
        var clip = MaskLayerList(style, style.MaskClipLayers, style.MaskClip, "border-box", null);
        var items = new List<string>(images.Count);
        for (int i = 0; i < images.Count; i++)
        {
            var parts = new List<string>(5) { At(images, i) };
            var p = At(List(position), i);
            var s = At(List(size), i);
            if (p.Length > 0 && p != "0% 0%") parts.Add(p);
            if (s.Length > 0 && s != "auto") { parts.Add("/"); parts.Add(s); }
            var r = At(List(repeat), i);
            if (r.Length > 0 && r != "repeat") parts.Add(r);
            var c = At(List(clip), i);
            if (c.Length > 0 && c != "border-box") parts.Add(c);
            items.Add(string.Join(" ", parts));
        }
        return string.Join(", ", items);
    }
}
