//using SkiaSharp;
//using Acrux.Core.Css;
//using Acrux.Core.Fonts;
//using Acrux.Core.Dom;

//namespace Acrux.Core.Layout;

///// <summary>
///// Accurate text measurement using SkiaSharp.
///// Replaces the naive fontSize * 0.55 estimation with real glyph measurement.
///// </summary>
//public class SkiaTextMeasurer : ITextMeasurer
//{
//    private readonly Dictionary<string, float> _widthCache = new();
//    private readonly Dictionary<string, TextMetrics> _metricsCache = new();
//    private const int MaxCacheSize = 4096;

//    public float MeasureText(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal)
//    {
//        if (string.IsNullOrEmpty(text)) return 0;

//        var key = $"{text}:{fontSize}:{fontFamily}:{weight}";
//        if (_widthCache.TryGetValue(key, out var cached))
//            return cached;

//        var typeface = FontManager.GetOrCreateTypeface(fontFamily, weight);
//        using var skFont = new SKFont(typeface, fontSize);

//        var width = skFont.MeasureText(text);

//        CacheIfNeeded(_widthCache, key, width);
//        return width;
//    }

//    public float MeasureTextAdvanced(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal, FontStyleType style = FontStyleType.Normal)
//    {
//        return MeasureText(text, fontFamily, fontSize, weight);
//    }

//    public (float width, float height, float baseline) MeasureTextDetail(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal)
//    {
//        if (string.IsNullOrEmpty(text))
//            return (0, fontSize, fontSize * 0.8f);

//        var typeface = FontManager.GetOrCreateTypeface(fontFamily, weight);
//        using var skFont = new SKFont(typeface, fontSize);
//        var fontMetrics = skFont.Metrics;

//        var width = skFont.MeasureText(text);

//        return (width, fontSize, -fontMetrics.Ascent);
//    }

//    public float MeasureTextWidth(string text, float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
//    {
//        return MeasureText(text, fontFamily, fontSize, weight);
//    }

//    public TextMetrics MeasureTextMetrics(string text, float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
//    {
//        if (string.IsNullOrEmpty(text))
//            return new TextMetrics { Width = 0, Height = fontSize, Ascent = 0, Descent = 0, XHeight = fontSize * 0.5f };

//        var key = $"{text}:{fontSize}:{fontFamily}:{weight}";
//        if (_metricsCache.TryGetValue(key, out var cached))
//            return cached;

//        var typeface = FontManager.GetOrCreateTypeface(fontFamily, weight);
//        using var skFont = new SKFont(typeface, fontSize);
//        var fontMetrics = skFont.Metrics;

//        var width = skFont.MeasureText(text);

//        var result = new TextMetrics
//        {
//            Width = width,
//            Height = fontSize,
//            Ascent = -fontMetrics.Ascent,
//            Descent = fontMetrics.Descent,
//            Leading = fontMetrics.Leading,
//            XHeight = fontSize * 0.5f,
//            CapHeight = fontSize * 0.7f,
//            LineHeight = -fontMetrics.Ascent + fontMetrics.Descent + fontMetrics.Leading
//        };

//        CacheIfNeeded(_metricsCache, key, result);
//        return result;
//    }

//    public float MeasureTextHeight(float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
//    {
//        var typeface = FontManager.GetOrCreateTypeface(fontFamily, weight);
//        using var skFont = new SKFont(typeface, fontSize);
//        var fontMetrics = skFont.Metrics;
//        return -fontMetrics.Ascent + fontMetrics.Descent;
//    }

//    public float GetBaseline(float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
//    {
//        var typeface = FontManager.GetOrCreateTypeface(fontFamily, weight);
//        using var skFont = new SKFont(typeface, fontSize);
//        var fontMetrics = skFont.Metrics;
//        return -fontMetrics.Ascent;
//    }

//    public float GetXHeight(float fontSize, string fontFamily)
//    {
//        return MeasureTextMetrics("x", fontSize, fontFamily).XHeight;
//    }

//    public float GetCapHeight(float fontSize, string fontFamily)
//    {
//        return MeasureTextMetrics("H", fontSize, fontFamily).CapHeight;
//    }

//    public float MeasureWordWidth(string word, float fontSize, string fontFamily, FontWeight weight = FontWeight.Normal)
//    {
//        return MeasureTextWidth(word, fontSize, fontFamily, weight);
//    }

//    public List<WordBreak> BreakTextIntoWords(string text, float fontSize, string fontFamily, float availableWidth, FontWeight weight = FontWeight.Normal)
//    {
//        var breaks = new List<WordBreak>();
//        if (string.IsNullOrEmpty(text)) return breaks;

//        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
//        float currentWidth = 0;
//        var currentWords = new List<string>();

//        foreach (var word in words)
//        {
//            var wordWidth = MeasureWordWidth(word + " ", fontSize, fontFamily, weight);

//            if (currentWidth + wordWidth > availableWidth && currentWords.Count > 0)
//            {
//                breaks.Add(new WordBreak
//                {
//                    Words = currentWords.ToArray(),
//                    Width = currentWidth,
//                    LineHeight = fontSize * 1.2f
//                });
//                currentWords = new List<string>();
//                currentWidth = 0;
//            }

//            currentWords.Add(word);
//            currentWidth += wordWidth;
//        }

//        if (currentWords.Count > 0)
//        {
//            breaks.Add(new WordBreak
//            {
//                Words = currentWords.ToArray(),
//                Width = currentWidth,
//                LineHeight = fontSize * 1.2f
//            });
//        }

//        return breaks;
//    }

//    public bool ContainsCharacter(string fontFamily, int codePoint)
//    {
//        var typeface = FontManager.GetOrCreateTypeface(fontFamily);
//        return typeface.ContainsGlyph(codePoint);
//    }

//    private void CacheIfNeeded<T>(Dictionary<string, T> cache, string key, T value)
//    {
//        if (cache.Count >= MaxCacheSize)
//        {
//            var oldest = cache.Keys.First();
//            cache.Remove(oldest);
//        }
//        cache[key] = value;
//    }

//    public void ClearCache()
//    {
//        _widthCache.Clear();
//        _metricsCache.Clear();
//    }
//}

//public class TextMetrics
//{
//    public float Width { get; set; }
//    public float Height { get; set; }
//    public float Ascent { get; set; }
//    public float Descent { get; set; }
//    public float Leading { get; set; }
//    public float XHeight { get; set; }
//    public float CapHeight { get; set; }
//    public float LineHeight { get; set; }
//}

//public class WordBreak
//{
//    public string[] Words { get; set; } = Array.Empty<string>();
//    public float Width { get; set; }
//    public float LineHeight { get; set; }
//}