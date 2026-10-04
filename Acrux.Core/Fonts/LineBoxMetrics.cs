using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Fonts;

/// <summary>
/// Resolves line box geometry from real font metrics.
///
/// Every baseline and line-height decision in layout and paint must come from
/// here. Approximating the baseline as a fixed fraction of the font size (the
/// classic 'fontSize * 0.85' shortcut) is wrong for every real font: the
/// alphabetic ascent ranges from roughly 0.89em for a serif face to over 1.06em
/// for a CJK face, so a fixed ratio misplaces every line of text vertically and,
/// because line box heights feed back into block heights, shifts the whole page.
///
/// The construction follows CSS 2.1 10.8 'Line height calculations':
///   1. take the font's rounded ascent/descent as the text's own height,
///   2. resolve the used value of 'line-height',
///   3. distribute the difference as half-leading above and below.
/// </summary>
public static class LineBoxMetrics
{
    /// <summary>Fallback font size when a style is missing entirely.</summary>
    private const float DefaultFontSize = 16f;

    /// <summary>Metrics of the style's primary font at the style's font size.</summary>
    public static FontMetrics GetFontMetrics(ComputedStyle? style)
    {
        if (style == null)
            return FontMetricsProvider.Get((SkiaSharp.SKTypeface?)null, DefaultFontSize);

        float size = style.FontSize > 0 ? style.FontSize : DefaultFontSize;
        return FontMetricsProvider.Get(style.FontFamily, size, style.FontWeight, style.FontStyle);
    }

    /// <summary>
    /// Used value of 'line-height' in pixels. 'normal' resolves to the font's own
    /// line spacing (rounded ascent + descent + line gap), which is what makes
    /// the default leading match the reference implementation instead of an
    /// invented multiplier such as 1.2 or 1.5.
    /// </summary>
    public static float ResolveLineHeight(ComputedStyle? style) =>
        ResolveLineHeight(style, GetFontMetrics(style));

    public static float ResolveLineHeight(ComputedStyle? style, in FontMetrics metrics)
    {
        if (style == null)
            return metrics.LineSpacing;

        if (style.LineHeightIsNormal)
            return metrics.LineSpacing;

        if (style.LineHeightPx is { } px)
            return MathF.Round(px, MidpointRounding.AwayFromZero);

        float size = style.FontSize > 0 ? style.FontSize : DefaultFontSize;
        float multiplier = style.LineHeight;

        // Guard against a multiplier that was never initialised.
        if (multiplier <= 0)
            return metrics.LineSpacing;

        return multiplier * size;
    }

    /// <summary>
    /// The text's own height: the rounded font ascent/descent, before leading.
    /// This is the extent that 'text-top' and 'text-bottom' refer to.
    /// </summary>
    public static FontHeight GetTextHeight(ComputedStyle? style, FontBaseline baseline = FontBaseline.Alphabetic)
    {
        var metrics = GetFontMetrics(style);
        var (ascent, descent) = metrics.GetFontHeight(baseline);
        return new FontHeight(ascent, descent);
    }

    /// <summary>
    /// The line box strut: the text height with the half-leading applied. Its
    /// ascent is the baseline position measured from the top of the line box.
    /// </summary>
    public static FontHeight GetStrut(ComputedStyle? style, FontBaseline baseline = FontBaseline.Alphabetic)
    {
        var metrics = GetFontMetrics(style);
        var (ascent, descent) = metrics.GetFontHeight(baseline);
        var textHeight = new FontHeight(ascent, descent);

        float lineHeight = ResolveLineHeight(style, metrics);
        var leading = FontHeight.CalculateLeadingSpace(lineHeight, textHeight);
        return textHeight.AddLeading(leading);
    }

    /// <summary>
    /// Distance from the top of the line box down to the alphabetic baseline.
    /// This is the replacement for a hard-coded baseline ratio.
    /// </summary>
    public static float GetBaseline(ComputedStyle? style, FontBaseline baseline = FontBaseline.Alphabetic) =>
        GetStrut(style, baseline).Ascent;

    /// <summary>Total height of the line box produced by a single run of this style.</summary>
    public static float GetLineHeight(ComputedStyle? style) => GetStrut(style).LineHeight;

    /// <summary>
    /// Distance from the top of the glyph box (not the line box) down to the
    /// baseline. Use this when a caller already positions the text at the top of
    /// the font's own box rather than at the top of a line box.
    /// </summary>
    public static float GetTextAscent(ComputedStyle? style) => GetFontMetrics(style).FloatAscent;

    /// <summary>Rounded font ascent for an explicit family/size/weight triple.</summary>
    public static float GetTextAscent(float fontSize, string? fontFamily,
        FontWeight weight = FontWeight.Normal, FontStyleType fontStyle = FontStyleType.Normal)
    {
        if (fontSize <= 0) fontSize = DefaultFontSize;
        return FontMetricsProvider.Get(fontFamily ?? "sans-serif", fontSize, weight, fontStyle).FloatAscent;
    }

    /// <summary>
    /// Baseline offset from the top of a line box for an explicit
    /// family/size/weight triple, assuming 'line-height: normal'.
    /// </summary>
    public static float GetBaseline(float fontSize, string? fontFamily,
        FontWeight weight = FontWeight.Normal, FontStyleType fontStyle = FontStyleType.Normal)
    {
        if (fontSize <= 0) fontSize = DefaultFontSize;
        var metrics = FontMetricsProvider.Get(fontFamily ?? "sans-serif", fontSize, weight, fontStyle);
        var textHeight = new FontHeight(metrics.LayoutAscent, metrics.LayoutDescent);
        var leading = FontHeight.CalculateLeadingSpace(metrics.LineSpacing, textHeight);
        return textHeight.AddLeading(leading).Ascent;
    }

    /// <summary>
    /// Line box height for an explicit family/size/weight triple with
    /// 'line-height: normal'.
    /// </summary>
    public static float GetNormalLineHeight(float fontSize, string? fontFamily,
        FontWeight weight = FontWeight.Normal, FontStyleType fontStyle = FontStyleType.Normal)
    {
        if (fontSize <= 0) fontSize = DefaultFontSize;
        return FontMetricsProvider.Get(fontFamily ?? "sans-serif", fontSize, weight, fontStyle).LineSpacing;
    }

    /// <summary>
    /// Baseline offset when a caller has already decided the line box height,
    /// for example because the line box was united from several inline boxes.
    /// The leading implied by <paramref name="lineHeight"/> is distributed
    /// around the font's own height.
    /// </summary>
    public static float GetBaselineForLineHeight(ComputedStyle? style, float lineHeight)
    {
        var metrics = GetFontMetrics(style);
        var textHeight = new FontHeight(metrics.LayoutAscent, metrics.LayoutDescent);
        if (lineHeight <= 0)
            lineHeight = metrics.LineSpacing;
        var leading = FontHeight.CalculateLeadingSpace(lineHeight, textHeight);
        return textHeight.AddLeading(leading).Ascent;
    }

    /// <summary>
    /// Apply a specified 'line-height' value to a style, keeping the three
    /// representations consistent: 'normal', a length in pixels, or a unitless
    /// number / percentage multiplier. All CSS entry points must go through here
    /// so that <see cref="ComputedStyle.LineHeightIsNormal"/> and
    /// <see cref="ComputedStyle.LineHeightPx"/> never disagree with
    /// <see cref="ComputedStyle.LineHeight"/>.
    /// </summary>
    public static void ApplyLineHeight(ComputedStyle style, string? value)
    {
        if (style == null) return;

        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        if (text.Equals("normal", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("initial", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("unset", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            SetNormal(style);
            return;
        }

        if (text.Equals("inherit", StringComparison.OrdinalIgnoreCase))
            return;

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var numberStyles = System.Globalization.NumberStyles.Float;

        // <percentage>: relative to this element's own font size, and it
        // inherits as the computed length rather than as the percentage.
        if (text.EndsWith("%", StringComparison.Ordinal) &&
            float.TryParse(text[..^1], numberStyles, invariant, out var percent))
        {
            float refSize = style.FontSize > 0 ? style.FontSize : DefaultFontSize;
            SetPixels(style, percent / 100f * refSize);
            return;
        }

        // <number>: a multiplier of the element's font size.
        if (float.TryParse(text, numberStyles, invariant, out var number))
        {
            SetMultiplier(style, number);
            return;
        }

        // <length>: resolve against the element's font size for font-relative
        // units, then store the absolute pixel value.
        var length = Length.Parse(text);
        if (length != null)
        {
            float reference = style.FontSize > 0 ? style.FontSize : DefaultFontSize;
            float px = length.ToPixels(reference, reference, 0, 0);
            if (!float.IsNaN(px) && px >= 0)
            {
                SetPixels(style, px);
                return;
            }
        }

        SetNormal(style);
    }

    /// <summary>Reset 'line-height' to its initial value of 'normal'.</summary>
    public static void SetNormal(ComputedStyle style)
    {
        style.LineHeightIsNormal = true;
        style.LineHeightPx = null;
    }

    /// <summary>Set 'line-height' to a multiple of the element's font size.</summary>
    public static void SetMultiplier(ComputedStyle style, float multiplier)
    {
        if (multiplier <= 0)
        {
            style.LineHeightIsNormal = false;
            style.LineHeightPx = 0f;
            style.LineHeight = 0f;
            return;
        }

        style.LineHeightIsNormal = false;
        style.LineHeightPx = null;
        style.LineHeight = multiplier;
    }

    /// <summary>Set 'line-height' to an absolute pixel length.</summary>
    public static void SetPixels(ComputedStyle style, float pixels)
    {
        style.LineHeightIsNormal = false;
        style.LineHeightPx = pixels;

        // Keep the multiplier roughly in sync for any consumer that still reads
        // it directly, so a stale reader degrades gracefully instead of treating
        // a pixel length as a multiplier.
        float reference = style.FontSize > 0 ? style.FontSize : DefaultFontSize;
        style.LineHeight = pixels / reference;
    }
}
