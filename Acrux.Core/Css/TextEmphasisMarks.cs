using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Fonts;

namespace Acrux.Core.Css;

/// <summary>
/// Resolves the 'text-emphasis' marks: which glyph paints them, and how much room
/// they need beside the text.
///
/// CSS Text 4 4.2.1 leaves the mark size to the font, but the marks must read as
/// punctuation rather than as a second line of text: the reference implementation
/// draws them from a font at half the text size, and the line box grows on the side
/// the marks sit on so they cannot collide with the neighbouring line. Both the
/// layout strut and the paint offset come from here so they stay consistent.
/// </summary>
public static class TextEmphasisMarks
{
    /// <summary>
    /// Mark font size as a fraction of the text font size. The reference implementation
    /// uses 0.5 with a face whose emphasis punctuation is about 0.4 of its own em, which
    /// lands the mark ink at 0.2em of the text. The faces installed here draw U+25CF at
    /// nearly 0.8em, so the factor is halved again to put the ink at the same 0.2em.
    /// </summary>
    public const float FontSizeFactor = 0.25f;

    /// <summary>Glyph that paints the style's mark, or empty when there is none.</summary>
    public static string GetMarkGlyph(ComputedStyle? style)
    {
        if (style == null) return string.Empty;
        var s = style.TextEmphasisStyle;
        if (string.IsNullOrEmpty(s) || s == "none") return string.Empty;
        var lower = s.ToLowerInvariant();
        bool open = lower.Contains("open");
        if (lower.Contains("double-circle")) return open ? "\u25CE" : "\u25C9";
        if (lower.Contains("circle")) return open ? "\u25CB" : "\u25CF";
        if (lower.Contains("triangle")) return open ? "\u25B3" : "\u25B2";
        if (lower.Contains("sesame")) return open ? "\uFE46" : "\uFE45";
        if (lower.Contains("dot")) return open ? "\u25E6" : "\u2022";
        if (lower == "auto" || lower == "filled" || lower == "open") return "\u2022";
        return s.Trim();
    }

    /// <summary>Gap kept between the text's own box and the mark.</summary>
    public static float Separation(float fontSize) => Math.Clamp(fontSize / 10f, 1f, 2f);

    /// <summary>
    /// How far the mark reaches beyond the edge of the text box. The reference
    /// implementation reserves the em box of a face at HALF the text size plus the
    /// separation, and that em box is far larger than the ink it draws (emphasis
    /// punctuation sits low and small inside it), so the line box grows by more than
    /// the mark looks like it needs. Measured on the reference: a 20px text reserves
    /// 12.4px above its own box, a 32px text 17.7px.
    /// The ratio is taken from the mark face at a probe size instead of the real one,
    /// because the metrics of a 5px font round to a fraction of themselves.
    /// </summary>
    public static float MarkBoxHeight(string markGlyph, float fontSize)
    {
        if (string.IsNullOrEmpty(markGlyph) || fontSize <= 0) return 0;

        // Resolved per text size, so cache by the tenth of a pixel it rounds to.
        int key = (int)MathF.Round(fontSize * 10f);
        var cacheKey = (markGlyph[0], key);
        if (_markBoxes.TryGetValue(cacheKey, out float cached)) return cached;

        float box = 0;
        var typeface = ResolveMarkTypeface(markGlyph[0]);
        if (typeface != null)
        {
            box = MarkEmBoxRatio(typeface) * fontSize * LayoutFontSizeFactor + Separation(fontSize);
        }

        _markBoxes[cacheKey] = box;
        return box;
    }

    /// <summary>The size the line box reserves room for, as a fraction of the text.</summary>
    public const float LayoutFontSizeFactor = 0.5f;

    private const float MetricsProbeSize = 100f;

    private static float MarkEmBoxRatio(SKTypeface typeface)
    {
        if (_emRatios.TryGetValue(typeface.FamilyName, out float cached)) return cached;
        float ratio = 1f;
        try
        {
            var font = new SKFont(typeface, MetricsProbeSize);
            var m = font.Metrics;
            float height = -m.Ascent + MathF.Max(0, m.Descent);
            if (height > 0) ratio = height / MetricsProbeSize;
        }
        catch
        {
            ratio = 1f;
        }
        _emRatios[typeface.FamilyName] = ratio;
        return ratio;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(char, int), float> _markBoxes = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, float> _emRatios = new();

    /// <summary>
    /// Typeface the marks are drawn with. The reference implementation deliberately
    /// substitutes a CJK font for emphasis punctuation because only those keep the
    /// mark glyphs at punctuation size; a symbol font such as Segoe UI Symbol draws
    /// U+25CF nearly as tall as the em box, which reads as a second line of text.
    /// The candidates are ordered by how close their U+25CF ink sits to the
    /// reference implementation's 0.2em of the text size (measured: MS Mincho 0.41
    /// of its own size, so 0.2em once the half-size mark font is applied).
    /// </summary>
    public static SKTypeface? ResolveMarkTypeface(char mark)
    {
        foreach (var family in MarkFontFamilies)
        {
            try
            {
                var typeface = FontManager.GetOrCreateTypeface(family);
                // GetOrCreateTypeface silently returns a substitute for a missing
                // family, so the request has to be confirmed by name.
                if (typeface != null && FontManager.HasCharacter(typeface, mark) &&
                    typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
                    return typeface;
            }
            catch
            {
                // Family not installed; try the next candidate.
            }
        }
        try
        {
            return FontManager.GetFallbackTypeface(mark);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>CJK fonts whose emphasis punctuation is sized like a mark, not like a box.</summary>
    private static readonly string[] MarkFontFamilies =
    {
        "MS Mincho", "Yu Mincho", "SimSun", "MS Gothic", "Microsoft YaHei", "Noto Serif CJK SC",
    };
}
