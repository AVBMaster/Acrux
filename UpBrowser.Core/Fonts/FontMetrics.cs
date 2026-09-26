using SkiaSharp;

namespace UpBrowser.Core.Fonts;

/// <summary>
/// Baseline kinds used when resolving 'dominant-baseline' and 'vertical-align'.
/// Mirrors font_baseline.h.
/// </summary>
public enum FontBaseline
{
    Alphabetic,
    Central,
    TextUnder,
    IdeographicUnder,
    XMiddle,
    Math,
    Hanging,
    TextOver,
}

/// <summary>
/// Resolved metrics of one font at one size. Mirrors FontMetrics in
/// font_metrics.h, including the distinction between the float metrics (used by
/// SVG text and canvas) and the integer metrics (used by HTML line boxes).
///
/// The integer variants matter for output fidelity: line box struts are built
/// from the rounded ascent/descent, so reproducing the rounding is what makes a
/// line box land on the same device pixel as the reference implementation.
/// </summary>
public readonly struct FontMetrics
{
    /// <summary>Distance from the baseline to the top of the em box, unrounded.</summary>
    public float FloatAscent { get; }

    /// <summary>Distance from the baseline to the bottom of the em box, unrounded.</summary>
    public float FloatDescent { get; }

    /// <summary>Rounded ascent. Line box layout uses this, not <see cref="FloatAscent"/>.</summary>
    public int IntAscent { get; }

    /// <summary>Rounded descent. Line box layout uses this, not <see cref="FloatDescent"/>.</summary>
    public int IntDescent { get; }

    public float CapHeight { get; }
    public float XHeight { get; }

    /// <summary>False when the font supplied no x-height and it had to be synthesized.</summary>
    public bool HasXHeight { get; }

    /// <summary>Rounded line gap ('leading') reported by the font.</summary>
    public int LineGap { get; }

    /// <summary>
    /// Rounded sum of ascent, descent and line gap. This is the used value of
    /// 'line-height: normal'.
    /// </summary>
    public int LineSpacing { get; }

    public float UnderlinePosition { get; }
    public float UnderlineThickness { get; }

    /// <summary>Advance of the '0' glyph, i.e. the CSS 'ch' unit.</summary>
    public float ZeroWidth { get; }

    /// <summary>Advance of the 'x' glyph, used for text field sizing heuristics.</summary>
    public float AverageCharWidth { get; }

    public float FloatHeight => FloatAscent + FloatDescent;

    /// <summary>Rounded ascent + rounded descent.</summary>
    public int Height => IntAscent + IntDescent;

    public FontMetrics(
        float floatAscent,
        float floatDescent,
        float capHeight,
        float xHeight,
        bool hasXHeight,
        float lineGap,
        float underlinePosition,
        float underlineThickness,
        float zeroWidth,
        float averageCharWidth)
    {
        FloatAscent = floatAscent;
        FloatDescent = floatDescent;
        IntAscent = LRound(floatAscent);
        IntDescent = LRound(floatDescent);
        CapHeight = capHeight;
        XHeight = xHeight;
        HasXHeight = hasXHeight;
        LineGap = LRound(lineGap);
        LineSpacing = LRound(floatAscent) + LRound(floatDescent) + LRound(lineGap);
        UnderlinePosition = underlinePosition;
        UnderlineThickness = underlineThickness;
        ZeroWidth = zeroWidth;
        AverageCharWidth = averageCharWidth;
    }

    /// <summary>Unrounded ascent for a given baseline.</summary>
    public float GetFloatAscent(FontBaseline baseline = FontBaseline.Alphabetic) => baseline switch
    {
        FontBaseline.Alphabetic => FloatAscent,
        FontBaseline.Central => FloatHeight / 2f,
        FontBaseline.TextUnder => FloatHeight,
        FontBaseline.IdeographicUnder => FloatHeight,
        FontBaseline.XMiddle => FloatAscent - XHeight / 2f,
        FontBaseline.Math => FloatAscent * 0.5f,
        FontBaseline.Hanging => FloatAscent * 0.2f,
        FontBaseline.TextOver => 0f,
        _ => FloatAscent,
    };

    /// <summary>Unrounded descent for a given baseline.</summary>
    public float GetFloatDescent(FontBaseline baseline = FontBaseline.Alphabetic) =>
        baseline == FontBaseline.Alphabetic ? FloatDescent : FloatHeight - GetFloatAscent(baseline);

    /// <summary>Rounded ascent for a given baseline. Line box layout uses this.</summary>
    public int GetAscent(FontBaseline baseline = FontBaseline.Alphabetic) => baseline switch
    {
        FontBaseline.Alphabetic => IntAscent,
        FontBaseline.Central => Height - Height / 2,
        FontBaseline.TextUnder => Height,
        FontBaseline.IdeographicUnder => Height,
        FontBaseline.XMiddle => IntAscent - (int)(XHeight / 2f),
        FontBaseline.Math => IntAscent / 2,
        FontBaseline.Hanging => IntAscent * 2 / 10,
        FontBaseline.TextOver => 0,
        _ => IntAscent,
    };

    /// <summary>Rounded descent for a given baseline.</summary>
    public int GetDescent(FontBaseline baseline = FontBaseline.Alphabetic) =>
        baseline == FontBaseline.Alphabetic ? IntDescent : Height - GetAscent(baseline);

    /// <summary>Shift a value expressed against the alphabetic baseline to another baseline.</summary>
    public float ConvertBaseline(float value, FontBaseline to, FontBaseline from = FontBaseline.Alphabetic) =>
        from == to ? value : GetFloatAscent(to) - GetFloatAscent(from) + value;

    /// <summary>Offset of the alphabetic baseline relative to <paramref name="baseline"/>.</summary>
    public float Alphabetic(FontBaseline baseline) => ConvertBaseline(0f, baseline);

    /// <summary>
    /// The rounded ascent/descent pair that a line box strut is built from,
    /// before leading is added. Mirrors FontMetrics::GetFontHeight().
    /// </summary>
    public (float Ascent, float Descent) GetFontHeight(FontBaseline baseline = FontBaseline.Alphabetic) =>
        (GetAscent(baseline), GetDescent(baseline));

    /// <summary>Unrounded ascent/descent pair, used for SVG text.</summary>
    public (float Ascent, float Descent) GetFloatFontHeight(FontBaseline baseline = FontBaseline.Alphabetic) =>
        (GetFloatAscent(baseline), GetFloatDescent(baseline));

    /// <summary>Round half away from zero, matching lroundf().</summary>
    internal static int LRound(float value) =>
        (int)MathF.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Round to the nearest whole scalar, matching SkScalarRoundToScalar().</summary>
    internal static float RoundToScalar(float value) => MathF.Floor(value + 0.5f);
}

/// <summary>
/// Builds and caches <see cref="FontMetrics"/> for a typeface at a given size.
///
/// The ascent/descent derivation reproduces the platform behaviour of the
/// reference implementation: the raw values reported by the rasterizer are
/// rounded to whole pixels, except for very small sizes where rounding would
/// collapse distinct baselines onto the same position.
/// </summary>
public static class FontMetricsProvider
{
    private static readonly Dictionary<(SKTypeface, float), FontMetrics> Cache = new();
    private static readonly object CacheLock = new();
    private const int MaxCacheEntries = 4096;

    /// <summary>
    /// Below this ascent (in pixels) the ascent/descent are kept unrounded so
    /// that different baseline kinds do not collapse onto the same value.
    /// </summary>
    private const float SubpixelAscentThreshold = 3f;

    private const float SubpixelHeightThreshold = 2f;

    /// <summary>
    /// Fraction of the ascent used as the x-height when the font does not
    /// report one. Matches the reference implementation's Windows heuristic.
    /// </summary>
    /// <summary>Fallback x-height ratio when the font does not report one.</summary>
    public const float SynthesizedXHeightRatio = 0.56f;

    public static FontMetrics Get(SKTypeface? typeface, float size)
    {
        if (typeface == null || size <= 0)
            return CreateFallback(size);

        var key = (typeface, size);
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var hit))
                return hit;
        }

        var computed = Compute(typeface, size);

        lock (CacheLock)
        {
            if (Cache.Count >= MaxCacheEntries)
                Cache.Clear();
            Cache[key] = computed;
        }

        return computed;
    }

    /// <summary>Resolve a family/weight/style triple through the font manager, then measure it.</summary>
    public static FontMetrics Get(string fontFamily, float size, Dom.FontWeight weight = Dom.FontWeight.Normal,
        Dom.FontStyleType style = Dom.FontStyleType.Normal)
    {
        if (size <= 0) return CreateFallback(size);
        var typeface = FontManager.GetOrCreateTypeface(PrimaryFamily(fontFamily), weight, style);
        return Get(typeface, size);
    }

    /// <summary>
    /// Take the first entry of a CSS font-family list. Full fallback-chain
    /// resolution happens in the font manager; the primary font determines the
    /// line box strut, which is what these metrics are used for.
    /// </summary>
    public static string PrimaryFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)) return "sans-serif";

        int comma = fontFamily.IndexOf(',');
        var first = comma >= 0 ? fontFamily[..comma] : fontFamily;
        first = first.Trim().Trim('"', '\'').Trim();
        return first.Length == 0 ? "sans-serif" : first;
    }

    private static FontMetrics Compute(SKTypeface typeface, float size)
    {
        using var font = new SKFont(typeface, size);
        var raw = font.Metrics;

        float rawAscent = -raw.Ascent;
        float rawDescent = raw.Descent;

        float ascent, descent;
        if (rawAscent < SubpixelAscentThreshold || rawAscent + rawDescent < SubpixelHeightThreshold)
        {
            // Tiny sizes: rounding here would make several baseline kinds resolve
            // to the same position, so keep the exact values.
            ascent = rawAscent;
            descent = rawDescent;
        }
        else
        {
            ascent = FontMetrics.RoundToScalar(rawAscent);
            descent = FontMetrics.RoundToScalar(rawDescent);
        }

        bool hasXHeight = raw.XHeight != 0;
        float xHeight = hasXHeight ? raw.XHeight : ascent * SynthesizedXHeightRatio;

        float capHeight = raw.CapHeight != 0 ? raw.CapHeight : ascent;

        float underlineThickness = raw.UnderlineThickness ?? MathF.Max(1f, size / 16f);
        float underlinePosition = raw.UnderlinePosition ?? underlineThickness;

        float zeroWidth = font.MeasureText("0");
        if (zeroWidth <= 0) zeroWidth = size * 0.5f;

        float averageCharWidth = raw.AverageCharacterWidth;
        if (averageCharWidth <= 0)
        {
            averageCharWidth = font.MeasureText("x");
            if (averageCharWidth <= 0) averageCharWidth = xHeight;
        }

        return new FontMetrics(
            floatAscent: ascent,
            floatDescent: descent,
            capHeight: capHeight,
            xHeight: xHeight,
            hasXHeight: hasXHeight,
            lineGap: raw.Leading,
            underlinePosition: underlinePosition,
            underlineThickness: underlineThickness,
            zeroWidth: zeroWidth,
            averageCharWidth: averageCharWidth);
    }

    /// <summary>
    /// Last-resort metrics for when no typeface is available at all. These are
    /// deliberately close to a typical sans-serif so that a missing font does
    /// not shift layout dramatically.
    /// </summary>
    private static FontMetrics CreateFallback(float size)
    {
        float s = size > 0 ? size : 16f;
        float ascent = FontMetrics.RoundToScalar(s * 0.905f);
        float descent = FontMetrics.RoundToScalar(s * 0.212f);
        return new FontMetrics(
            floatAscent: ascent,
            floatDescent: descent,
            capHeight: s * 0.716f,
            xHeight: s * 0.519f,
            hasXHeight: false,
            lineGap: 0f,
            underlinePosition: s * 0.1f,
            underlineThickness: MathF.Max(1f, s / 16f),
            zeroWidth: s * 0.556f,
            averageCharWidth: s * 0.5f);
    }

    public static void ClearCache()
    {
        lock (CacheLock) Cache.Clear();
    }
}
