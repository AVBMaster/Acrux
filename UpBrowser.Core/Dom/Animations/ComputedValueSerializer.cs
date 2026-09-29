using System.Globalization;
using System.Text;
using UpBrowser.Core.Css;

namespace UpBrowser.Core.Dom.Animations;

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
    {
        if (style == null || string.IsNullOrEmpty(property)) return null;

        switch (property)
        {
            // ------------------------------------------------------- colors
            case "color": return ColorText(style.Color);
            case "background-color": return style.BackgroundColor.HasValue ? ColorText(style.BackgroundColor.Value) : "transparent";
            case "border-top-color": return ColorText(style.BorderTopColor);
            case "border-right-color": return ColorText(style.BorderRightColor);
            case "border-bottom-color": return ColorText(style.BorderBottomColor);
            case "border-left-color": return ColorText(style.BorderLeftColor);
            case "outline-color": return ColorText(style.OutlineColor);
            case "caret-color": return style.CaretColor.HasValue ? ColorText(style.CaretColor.Value) : "auto";
            case "accent-color": return style.AccentColor.HasValue ? ColorText(style.AccentColor.Value) : "auto";
            case "column-rule-color": return style.ColumnRuleColor.HasValue ? ColorText(style.ColumnRuleColor.Value) : "transparent";
            case "text-decoration-color": return ColorText(style.TextDecorationColor);
            case "text-emphasis-color":
                return string.Equals(style.TextEmphasisColor, "currentcolor", StringComparison.OrdinalIgnoreCase)
                    ? "currentcolor"
                    : ResolveColorText(style.TextEmphasisColor);

            // -------------------------------------------------------- sizes
            case "width": return LengthText(style.Width);
            case "height": return LengthText(style.Height);
            case "min-width": return style.MinWidth != null ? LengthText(style.MinWidth) : "auto";
            case "max-width": return style.MaxWidth != null ? LengthText(style.MaxWidth) : "none";
            case "min-height": return style.MinHeight != null ? LengthText(style.MinHeight) : "auto";
            case "max-height": return style.MaxHeight != null ? LengthText(style.MaxHeight) : "none";

            case "top": return LengthText(style.Top);
            case "right": return LengthText(style.Right);
            case "bottom": return LengthText(style.Bottom);
            case "left": return LengthText(style.Left);

            case "margin-top": return LengthText(style.MarginTop);
            case "margin-right": return LengthText(style.MarginRight);
            case "margin-bottom": return LengthText(style.MarginBottom);
            case "margin-left": return LengthText(style.MarginLeft);
            case "padding-top": return LengthText(style.PaddingTop);
            case "padding-right": return LengthText(style.PaddingRight);
            case "padding-bottom": return LengthText(style.PaddingBottom);
            case "padding-left": return LengthText(style.PaddingLeft);

            case "border-top-width": return Px(style.BorderTopWidth);
            case "border-right-width": return Px(style.BorderRightWidth);
            case "border-bottom-width": return Px(style.BorderBottomWidth);
            case "border-left-width": return Px(style.BorderLeftWidth);
            case "outline-width": return Px(style.OutlineWidth);
            case "outline-offset": return Px(style.OutlineOffset);

            case "border-top-left-radius": return RadiusText(style.BorderTopLeftRadius, style.BorderTopLeftRadiusY);
            case "border-top-right-radius": return RadiusText(style.BorderTopRightRadius, style.BorderTopRightRadiusY);
            case "border-bottom-right-radius": return RadiusText(style.BorderBottomRightRadius, style.BorderBottomRightRadiusY);
            case "border-bottom-left-radius": return RadiusText(style.BorderBottomLeftRadius, style.BorderBottomLeftRadiusY);

            case "flex-basis": return LengthText(style.FlexBasis);

            // --------------------------------------------------------- text
            case "font-size": return Px(style.FontSize);
            case "line-height":
                return style.LineHeightIsNormal || Math.Abs(style.LineHeight - 1.2f) < 1e-6
                    ? "normal"
                    : CssValueTokenizer.Num(style.LineHeight);
            case "letter-spacing": return Px(style.LetterSpacing);
            case "word-spacing": return Px(style.WordSpacing);
            case "text-indent": return Px(style.TextIndent);
            case "font-weight": return ((int)style.FontWeight).ToString(CultureInfo.InvariantCulture);
            case "visibility": return style.Visibility switch
            {
                VisibilityType.Hidden => "hidden",
                VisibilityType.Collapse => "collapse",
                _ => "visible",
            };

            // ------------------------------------------------ discrete words
            case "display": return style.Display.ToCssString();
            case "position": return style.Position.ToCssString();
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
            case "box-sizing": return style.BoxSizing.ToCssString();
            case "list-style-type": return string.IsNullOrEmpty(style.ListStyleTypeString)
                ? style.ListStyleType.ToString().ToLowerInvariant()
                : style.ListStyleTypeString!;
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
            case "mix-blend-mode": return style.MixBlendMode.ToString().ToLowerInvariant();
            case "isolation": return style.Isolation == IsolationType.Isolate ? "isolate" : "auto";
            case "image-rendering": return style.ImageRendering switch
            {
                ImageRenderingType.CrispEdges => "crisp-edges",
                ImageRenderingType.Pixelated => "pixelated",
                _ => "auto",
            };
            case "pointer-events": return style.PointerEvents ?? "auto";
            case "cursor": return style.Cursor ?? "auto";
            case "resize": return style.Resize == ResizeType.Both ? "both"
                : style.Resize == ResizeType.Vertical ? "vertical"
                : style.Resize == ResizeType.Horizontal ? "horizontal" : "none";
            case "aspect-ratio": return style.AspectRatio > 0 ? CssValueTokenizer.Num(style.AspectRatio) : "auto";

            // -------------------------------------------------------- numbers
            case "opacity": return CssValueTokenizer.Num(style.Opacity);
            case "flex-grow": return CssValueTokenizer.Num(style.FlexGrow);
            case "flex-shrink": return CssValueTokenizer.Num(style.FlexShrink);
            case "column-count": return style.ColumnCount > 0 ? style.ColumnCount.ToString(CultureInfo.InvariantCulture) : "auto";
            case "row-gap": return LengthText(style.RowGap);
            case "column-gap": return LengthText(style.ColumnGap);
            case "orphans": return style.Orphans.ToString(CultureInfo.InvariantCulture);
            case "widows": return style.Widows.ToString(CultureInfo.InvariantCulture);

            // ---------------------------------------------------- transforms
            case "transform": return string.IsNullOrWhiteSpace(style.Transform) ? "none" : style.Transform!.Trim();
            case "translate": return string.IsNullOrWhiteSpace(style.Translate) ? "none" : style.Translate!.Trim();
            case "rotate": return string.IsNullOrWhiteSpace(style.Rotate) ? "none" : style.Rotate!.Trim();
            case "scale": return string.IsNullOrWhiteSpace(style.Scale) ? "none" : style.Scale!.Trim();
            case "transform-origin": return style.TransformOrigin ?? "50% 50% 0";

            // --------------------------------------------------- backgrounds
            case "background-position":
                return $"{BackgroundPosText(style.BackgroundPositionX)} {BackgroundPosText(style.BackgroundPositionY)}";
            case "background-size":
                return $"{BackgroundSizeText(style.BackgroundSizeWidth)} {BackgroundSizeText(style.BackgroundSizeHeight)}";
            case "background-repeat": return style.BackgroundRepeat.ToString().ToLowerInvariant();
            case "background-attachment": return style.BackgroundAttachment == BackgroundAttachment.Fixed ? "fixed"
                : style.BackgroundAttachment == BackgroundAttachment.Local ? "local" : "scroll";
            case "background-image": return style.BackgroundImage is { Count: > 0 } ? style.BackgroundImage[0] : "none";
            case "background-clip": return style.BackgroundClip;
            case "background-origin": return style.BackgroundOrigin;
            case "filter": return string.IsNullOrWhiteSpace(style.Filter) ? "none" : style.Filter!;
            case "backdrop-filter": return string.IsNullOrWhiteSpace(style.BackdropFilter) ? "none" : style.BackdropFilter!;
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
            case "text-decoration-style": return style.TextDecorationStyle switch
            {
                TextDecorationStyleType.Double => "double",
                TextDecorationStyleType.Dotted => "dotted",
                TextDecorationStyleType.Dashed => "dashed",
                TextDecorationStyleType.Wavy => "wavy",
                _ => "solid",
            };
            case "text-decoration-line": return style.TextDecorationLine.ToString().ToLowerInvariant();
            case "text-decoration-thickness":
                return style.TextDecorationThicknessFromFont || float.IsNaN(style.TextDecorationThickness)
                    ? "auto"
                    : Px(style.TextDecorationThickness);
            case "text-underline-offset":
                return style.TextUnderlineOffsetIsAuto ? "auto" : Px(style.TextUnderlineOffset);
            case "text-emphasis-style": return style.TextEmphasisStyle;
            case "text-emphasis-position": return style.TextEmphasisPosition;
            case "will-change": return style.WillChange ?? "auto";
            case "content": return style.Content ?? "normal";
        }

        return null;
    }

    private static string Px(float value) => CssValueTokenizer.Num(value) + "px";

    private static string ColorText(SkiaSharp.SKColor c)
    {
        if (c.Alpha == 255) return $"rgb({c.Red}, {c.Green}, {c.Blue})";
        if (c.Alpha == 0) return "transparent";
        return $"rgba({c.Red}, {c.Green}, {c.Blue}, {CssValueTokenizer.Num(c.Alpha / 255.0)})";
    }

    /// <summary>Text form of an optional color, resolving keywords through the parser.</summary>
    private static string ResolveColorText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "currentcolor";
        if (string.Equals(value, "currentcolor", StringComparison.OrdinalIgnoreCase)) return value;
        return ColorText(ColorParser.Parse(value));
    }

    private static string LengthText(Length? length)
    {
        if (length == null) return "auto";
        return length.ToString();
    }

    private static string RadiusText(float x, float y)
    {
        if (Math.Abs(x - y) < 1e-4) return Px(x);
        return $"{Px(x)} {Px(y)}";
    }

    private static string BackgroundPosText(Length? length)
    {
        if (length == null) return "0%";
        if (length is PercentLength p) return $"{CssValueTokenizer.Num(p.Value)}%";
        if (length is PixelLength px) return Px(px.Value);
        return length.ToString();
    }

    private static string BackgroundSizeText(Length? length)
    {
        if (length == null) return "auto";
        if (length is PercentLength p) return $"{CssValueTokenizer.Num(p.Value)}%";
        if (length is PixelLength px) return Px(px.Value);
        return length.ToString();
    }

    private static string BoxShadowText(ComputedStyle style)
    {
        if (style.BoxShadow == null || style.BoxShadow.Count == 0) return "none";
        var sb = new StringBuilder();
        foreach (var s in style.BoxShadow)
        {
            if (sb.Length > 0) sb.Append(", ");
            if (s.Inset) sb.Append("inset ");
            sb.Append(Px(s.OffsetX)).Append(' ').Append(Px(s.OffsetY)).Append(' ')
              .Append(Px(s.BlurRadius)).Append(' ').Append(Px(s.Spread)).Append(' ')
              .Append(ColorText(s.Color));
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
            sb.Append(Px(s.OffsetX)).Append(' ').Append(Px(s.OffsetY)).Append(' ')
              .Append(Px(s.BlurRadius)).Append(' ')
              .Append(ColorText(s.Color));
        }
        return sb.ToString();
    }
}
