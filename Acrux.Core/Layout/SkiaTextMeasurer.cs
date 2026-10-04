using SkiaSharp;
using Acrux.Core.Css;
using Acrux.Core.Fonts;
using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// Accurate text measurement using SkiaSharp.
/// Replaces the naive fontSize * 0.55 estimation with real glyph measurement.
/// </summary>
public class SkiaTextMeasurer : ITextMeasurer
{
    // Shared by the UI thread and background tab threads in threaded-tab mode.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, float> _widthCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TextMetrics> _metricsCache = new();
    private const int MaxCacheSize = 4096;

    public float MeasureText(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        // Glyph advances are resolved on the device pixel grid, so the scale is
        // part of the identity of a measured run.
        var key = $"{text}:{fontSize}:{fontFamily}:{weight}:{Fonts.FontMetricsProvider.DeviceScale}";
        if (_widthCache.TryGetValue(key, out var cached))
            return cached;

        var primaryTypeface = FontManager.GetOrCreateTypeface(fontFamily, weight);

        // Fast path: if primary typeface supports all glyphs, use single measurement
        // We optimistically measure and then verify: if the text is ASCII-only, we know it's fine
        bool hasNonAscii = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] > 127) { hasNonAscii = true; break; }
        }

        if (!hasNonAscii)
        {
            using var font = new SKFont(primaryTypeface, fontSize);
            float width = font.MeasureText(text);
            CacheIfNeeded(_widthCache, key, width);
            return width;
        }

        // Slow path: split text into runs by glyph support for CJK/emoji fallback
        float totalWidth = 0;
        int runStart = 0;
        SKTypeface currentTf = primaryTypeface;

        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                char c = text[i];
                SKTypeface neededTf = primaryTypeface.ContainsGlyph(c) ? primaryTypeface : FontManager.GetFallbackTypeface(c);

                if (neededTf != currentTf)
                {
                    if (i > runStart)
                    {
                        string run = text.Substring(runStart, i - runStart);
                        using var runFont = new SKFont(currentTf, fontSize);
                        totalWidth += runFont.MeasureText(run);
                    }
                    currentTf = neededTf;
                    runStart = i;
                }
            }
            else
            {
                if (i > runStart)
                {
                    string run = text.Substring(runStart);
                    using var runFont = new SKFont(currentTf, fontSize);
                    totalWidth += runFont.MeasureText(run);
                }
            }
        }

        CacheIfNeeded(_widthCache, key, totalWidth);
        return totalWidth;
    }

    public float MeasureTextAdvanced(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal, FontStyleType style = FontStyleType.Normal)
    {
        return MeasureText(text, fontFamily, fontSize, weight);
    }

    public (float width, float height, float baseline) MeasureTextDetail(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal)
    {
        var metrics = Fonts.FontMetricsProvider.Get(fontFamily, fontSize, weight);
        if (string.IsNullOrEmpty(text))
            return (0, metrics.FloatHeight, metrics.FloatAscent);

        var typeface = FontManager.GetOrCreateTypeface(fontFamily, weight);
        using var skFont = new SKFont(typeface, fontSize);
        var width = skFont.MeasureText(text);

        return (width, metrics.FloatHeight, metrics.FloatAscent);
    }

    public float MeasureTextWidth(string text, float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
    {
        return MeasureText(text, fontFamily, fontSize, weight);
    }

    public TextMetrics MeasureTextMetrics(string text, float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
    {
        var fm = Fonts.FontMetricsProvider.Get(fontFamily, fontSize, weight);

        if (string.IsNullOrEmpty(text))
        {
            return new TextMetrics
            {
                Width = 0,
                Height = fm.FloatHeight,
                Ascent = fm.FloatAscent,
                Descent = fm.FloatDescent,
                Leading = fm.LineGap,
                XHeight = fm.XHeight,
                CapHeight = fm.CapHeight,
                LineHeight = fm.LineSpacing,
            };
        }

        // Line spacing comes out of the device-grid quantization, so the scale is
        // part of the identity of a measured run.
        var key = $"{text}:{fontSize}:{fontFamily}:{weight}:{Fonts.FontMetricsProvider.DeviceScale}";
        if (_metricsCache.TryGetValue(key, out var cached))
            return cached;

        var typeface = FontManager.GetOrCreateTypeface(fontFamily, weight);
        using var skFont = new SKFont(typeface, fontSize);
        var width = skFont.MeasureText(text);

        var result = new TextMetrics
        {
            Width = width,
            Height = fm.FloatHeight,
            Ascent = fm.FloatAscent,
            Descent = fm.FloatDescent,
            Leading = fm.LineGap,
            XHeight = fm.XHeight,
            CapHeight = fm.CapHeight,
            LineHeight = fm.LineSpacing,
        };

        CacheIfNeeded(_metricsCache, key, result);
        return result;
    }

    public float MeasureTextHeight(float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
    {
        return Fonts.FontMetricsProvider.Get(fontFamily, fontSize, weight).FloatHeight;
    }

    public float GetBaseline(float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
    {
        return Fonts.FontMetricsProvider.Get(fontFamily, fontSize, weight).FloatAscent;
    }

    public float GetXHeight(float fontSize, string fontFamily)
    {
        return Fonts.FontMetricsProvider.Get(fontFamily, fontSize).XHeight;
    }

    public float GetCapHeight(float fontSize, string fontFamily)
    {
        return Fonts.FontMetricsProvider.Get(fontFamily, fontSize).CapHeight;
    }

    public float MeasureWordWidth(string word, float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
    {
        return MeasureTextWidth(word, fontSize, fontFamily, weight);
    }

    public List<WordBreak> BreakTextIntoWords(string text, float fontSize, string fontFamily, float availableWidth, FontWeight weight = FontWeight.Normal)
    {
        var breaks = new List<WordBreak>();
        if (string.IsNullOrEmpty(text)) return breaks;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float currentWidth = 0;
        var currentWords = new List<string>();

        // 'line-height: normal' for this font, i.e. its own line spacing.
        float normalLineHeight = Fonts.FontMetricsProvider.Get(fontFamily, fontSize, weight).LineSpacing;

        foreach (var word in words)
        {
            var wordWidth = MeasureWordWidth(word + " ", fontSize, fontFamily, weight);

            if (currentWidth + wordWidth > availableWidth && currentWords.Count > 0)
            {
                breaks.Add(new WordBreak
                {
                    Words = currentWords.ToArray(),
                    Width = currentWidth,
                    LineHeight = normalLineHeight
                });
                currentWords = new List<string>();
                currentWidth = 0;
            }

            currentWords.Add(word);
            currentWidth += wordWidth;
        }

        if (currentWords.Count > 0)
        {
            breaks.Add(new WordBreak
            {
                Words = currentWords.ToArray(),
                Width = currentWidth,
                LineHeight = normalLineHeight
            });
        }

        return breaks;
    }

    public bool ContainsCharacter(string fontFamily, int codePoint)
    {
        var typeface = FontManager.GetOrCreateTypeface(fontFamily);
        return typeface.ContainsGlyph(codePoint);
    }

    private void CacheIfNeeded<T>(System.Collections.Concurrent.ConcurrentDictionary<string, T> cache, string key, T value)
    {
        if (cache.Count >= MaxCacheSize)
        {
            foreach (var oldest in cache.Keys)
            {
                cache.TryRemove(oldest, out _);
                break;
            }
        }
        cache[key] = value;
    }

    public void ClearCache()
    {
        _widthCache.Clear();
        _metricsCache.Clear();
    }
}

public class TextMetrics
{
    public float Width { get; set; }
    public float Height { get; set; }
    public float Ascent { get; set; }
    public float Descent { get; set; }
    public float Leading { get; set; }
    public float XHeight { get; set; }
    public float CapHeight { get; set; }
    public float LineHeight { get; set; }
}

public class WordBreak
{
    public string[] Words { get; set; } = Array.Empty<string>();
    public float Width { get; set; }
    public float LineHeight { get; set; }
}
