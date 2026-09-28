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
/// SVG text and canvas) and the device-grid metrics (used by HTML line boxes).
///
/// The device-grid variants matter for output fidelity: a line box strut is
/// built from an ascent/descent/leading that each land on a whole device pixel,
/// so 'line-height: normal' becomes a whole number of *device* pixels. At a
/// device scale above 1 that is a fractional CSS-pixel value — reproducing the
/// quantization is what makes a line box land on the same device pixel as the
/// reference implementation.
/// </summary>
public readonly struct FontMetrics
{
    /// <summary>Distance from the baseline to the top of the em box, unrounded.</summary>
    public float FloatAscent { get; }

    /// <summary>Distance from the baseline to the bottom of the em box, unrounded.</summary>
    public float FloatDescent { get; }

    /// <summary>Ascent quantized to the device pixel grid. Line box layout uses this.</summary>
    public float LayoutAscent => DevAscent / DeviceScale;

    /// <summary>Descent quantized to the device pixel grid. Line box layout uses this.</summary>
    public float LayoutDescent => DevDescent / DeviceScale;

    public float CapHeight { get; }
    public float XHeight { get; }

    /// <summary>False when the font supplied no x-height and it had to be synthesized.</summary>
    public bool HasXHeight { get; }

    /// <summary>Line gap ('leading') reported by the font, quantized to the device grid.</summary>
    public float LineGap => DevLineGap / DeviceScale;

    /// <summary>
    /// Sum of the device-quantized ascent, descent and line gap. This is the used
    /// value of 'line-height: normal'.
    /// </summary>
    public float LineSpacing => (DevAscent + DevDescent + DevLineGap) / DeviceScale;

    public float UnderlinePosition { get; }
    public float UnderlineThickness { get; }

    /// <summary>Advance of the '0' glyph, i.e. the CSS 'ch' unit.</summary>
    public float ZeroWidth { get; }

    /// <summary>Advance of the '水' glyph, i.e. the CSS 'ic' unit.</summary>
    public float IdeographicWidth { get; }

    /// <summary>Advance of the 'x' glyph, used for text field sizing heuristics.</summary>
    public float AverageCharWidth { get; }

    public float FloatHeight => FloatAscent + FloatDescent;

    /// <summary>Device-quantized ascent + descent, in CSS pixels.</summary>
    public float Height => (DevAscent + DevDescent) / DeviceScale;

    /// <summary>Scale these metrics were quantized at; 1 means CSS pixels are device pixels.</summary>
    private readonly float DeviceScale;

    // Ascent / descent / leading as whole device pixels. The platform rasterizer
    // resolves font metrics on the device grid, so every line-box decision is
    // taken here and converted back to CSS pixels on read. At a scale of 1 this
    // is exactly the historic whole-CSS-pixel rounding.
    private readonly int DevAscent;
    private readonly int DevDescent;
    private readonly int DevLineGap;

    /// <param name="rawAscent">Unrounded ascent in CSS pixels, before device quantization.</param>
    /// <param name="rawDescent">Unrounded descent in CSS pixels.</param>
    /// <param name="rawLineGap">Unrounded leading in CSS pixels.</param>
    /// <param name="deviceScale">Device pixels per CSS pixel these metrics are resolved at.</param>
    public FontMetrics(
        float floatAscent,
        float floatDescent,
        float capHeight,
        float xHeight,
        bool hasXHeight,
        float underlinePosition,
        float underlineThickness,
        float zeroWidth,
        float ideographicWidth,
        float averageCharWidth,
        float rawAscent,
        float rawDescent,
        float rawLineGap,
        float deviceScale)
    {
        DeviceScale = deviceScale > 0 ? deviceScale : 1f;
        DevAscent = LRound(rawAscent * DeviceScale);
        DevDescent = LRound(rawDescent * DeviceScale);
        DevLineGap = LRound(rawLineGap * DeviceScale);

        FloatAscent = floatAscent;
        FloatDescent = floatDescent;
        CapHeight = capHeight;
        XHeight = xHeight;
        HasXHeight = hasXHeight;
        UnderlinePosition = underlinePosition;
        UnderlineThickness = underlineThickness;
        ZeroWidth = zeroWidth;
        IdeographicWidth = ideographicWidth;
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

    /// <summary>
    /// Ascent for a given baseline, on the device pixel grid. Line box layout
    /// uses this. The per-baseline arithmetic is done in whole device pixels so
    /// that a fractional CSS-pixel value never breaks the integer relationships
    /// the reference grid guarantees.
    /// </summary>
    public float GetAscent(FontBaseline baseline = FontBaseline.Alphabetic)
    {
        int dev = baseline switch
        {
            FontBaseline.Alphabetic => DevAscent,
            FontBaseline.Central => DevHeight - DevHeight / 2,
            FontBaseline.TextUnder => DevHeight,
            FontBaseline.IdeographicUnder => DevHeight,
            FontBaseline.XMiddle => DevAscent - (int)(XHeight * DeviceScale / 2f),
            FontBaseline.Math => DevAscent / 2,
            FontBaseline.Hanging => DevAscent * 2 / 10,
            FontBaseline.TextOver => 0,
            _ => DevAscent,
        };
        return dev / DeviceScale;
    }

    private int DevHeight => DevAscent + DevDescent;

    /// <summary>Descent for a given baseline, on the device pixel grid.</summary>
    public float GetDescent(FontBaseline baseline = FontBaseline.Alphabetic) =>
        baseline == FontBaseline.Alphabetic ? LayoutDescent : Height - GetAscent(baseline);

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
    private static readonly Dictionary<(SKTypeface, float, float), FontMetrics> Cache = new();
    private static readonly object CacheLock = new();
    private const int MaxCacheEntries = 4096;

    /// <summary>
    /// Device pixels per CSS pixel that line-box metrics are quantized at. Layout
    /// sets this from the viewport DPI before measuring; it changes only when the
    /// window moves to a display with a different scale.
    /// </summary>
    public static float DeviceScale = 1f;

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
        float scale = DeviceScale > 0 ? DeviceScale : 1f;
        if (typeface == null || size <= 0)
            return CreateFallback(size, scale);

        var key = (typeface, size, scale);
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var hit))
                return hit;
        }

        var computed = Compute(typeface, size, scale);

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
        if (size <= 0) return CreateFallback(size, DeviceScale > 0 ? DeviceScale : 1f);
        var typeface = FontManager.GetOrCreateTypeface(PrimaryFamily(fontFamily), weight, style);
        return Get(typeface, size);
    }

    /// <summary>Metrics of a computed style's own font (family × weight × style at its size).</summary>
    public static FontMetrics GetForStyle(Dom.ComputedStyle style) =>
        Get(style.FontFamily, style.FontSize, style.FontWeight, style.FontStyle);

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

    private static FontMetrics Compute(SKTypeface typeface, float size, float deviceScale)
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

        // CSS 'ic' is the advance of U+6C34 (水). Fonts without the glyph fall
        // back through the platform chain; an ideographic advance is 1em there.
        float ideographicWidth = MeasureIdeographicAdvance(typeface, size);

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
            underlinePosition: underlinePosition,
            underlineThickness: underlineThickness,
            zeroWidth: zeroWidth,
            ideographicWidth: ideographicWidth,
            averageCharWidth: averageCharWidth,
            rawAscent: rawAscent,
            rawDescent: rawDescent,
            rawLineGap: raw.Leading,
            deviceScale: deviceScale);
    }

    /// <summary>Advance of U+6C34 (水) used by the CSS 'ic' unit; 1em when unavailable.</summary>
    private static float MeasureIdeographicAdvance(SKTypeface typeface, float size)
    {
        const int IdeographicCodePoint = 0x6C34;
        var used = typeface;
        if (typeface.GetGlyph(IdeographicCodePoint) == 0)
            used = FontManager.GetFallbackTypeface(IdeographicCodePoint) ?? typeface;

        using var font = new SKFont(used, size);
        float width = font.MeasureText("\u6C34");
        return width > 0 ? width : size;
    }

    /// <summary>
    /// Last-resort metrics for when no typeface is available at all. These are
    /// deliberately close to a typical sans-serif so that a missing font does
    /// not shift layout dramatically.
    /// </summary>
    private static FontMetrics CreateFallback(float size, float deviceScale)
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
            underlinePosition: s * 0.1f,
            underlineThickness: MathF.Max(1f, s / 16f),
            zeroWidth: s * 0.556f,
            ideographicWidth: s,
            averageCharWidth: s * 0.5f,
            rawAscent: s * 0.905f,
            rawDescent: s * 0.212f,
            rawLineGap: 0f,
            deviceScale: deviceScale);
    }

    public static void ClearCache()
    {
        lock (CacheLock) Cache.Clear();
    }
}
