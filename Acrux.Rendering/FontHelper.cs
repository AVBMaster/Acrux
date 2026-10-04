using SkiaSharp;

namespace Acrux.Rendering;

public static class FontHelper
{
    private static SKTypeface? _chineseTypeface;
    private static SKTypeface? _monoTypeface;
    private static SKTypeface? _defaultTypeface;
    private static SKTypeface? _emojiTypeface;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _chineseTypeface = FindFont(
            "Noto Sans SC", "Source Han Sans SC", "PingFang SC",
            "Microsoft YaHei", "Microsoft YaHei UI", "Microsoft JhengHei",
            "SimSun", "SimHei",
            "WenQuanYi Micro Hei", "Droid Sans Fallback",
            "Noto Sans CJK SC", "Source Han Sans");

        _monoTypeface = FindFont(
            "Consolas", "Cascadia Code", "Cascadia Mono", "Source Code Pro",
            "Courier New", "DejaVu Sans Mono", "Liberation Mono",
            "Fira Code", "Monaco", "Menlo");

        _defaultTypeface = FindFont(
            "Microsoft YaHei", "Microsoft YaHei UI",
            "PingFang SC", "Noto Sans SC", "Source Han Sans SC",
            "Segoe UI", "Arial", "Helvetica",
            "Liberation Sans", "DejaVu Sans", "Tahoma",
            "Verdana", "San Francisco", "Noto Sans");

        _emojiTypeface = FindFont(
            "Segoe UI Emoji", "Noto Color Emoji", "Apple Color Emoji",
            "Twitter Color Emoji", "EmojiOne Color", "Noto Emoji",
            "Segoe UI Symbol");
    }

    // Deliberately NOT wrapped in #if SUPPORT_WINXP: this project defines
    // SUPPORT_WINXP in DefineConstants, so anything guarded by
    // `#if !SUPPORT_WINXP` is excluded from the build. That is exactly how every
    // UI font here went unhinted — no stem snapping at all — and read as soft.
    private static void ConfigureText(SKFont font, bool linearMetrics = false)
    {
        font.Hinting = CrispHinting(font.Typeface);
        // LCD is requested; Skia falls back to grayscale automatically whenever
        // the backing surface is not opaque or the device matrix is scaled.
        font.Edging = SKFontEdging.SubpixelAntialias;
        font.Subpixel = true;
        font.LinearMetrics = linearMetrics;
    }

    /// <summary>
    /// Full hinting (= Normal + stem snapping) quantises stems to whole device
    /// pixels — that is what makes small text read as sharp instead of soft. But
    /// CJK ideographs pack many stems into one em box, so snapping them produces
    /// visibly uneven stroke widths; those faces stay at Normal, which still
    /// hints glyph origins to the grid without distorting the outline.
    /// </summary>
    public static SKFontHinting CrispHinting(SKTypeface? typeface) =>
        IsCjkTypeface(typeface) ? SKFontHinting.Normal : SKFontHinting.Full;

    private static bool IsCjkTypeface(SKTypeface? typeface)
    {
        if (typeface == null) return false;
        if (ReferenceEquals(typeface, GetChineseTypeface())) return true;
        var name = typeface.FamilyName ?? string.Empty;
        foreach (var tag in CjkFamilyTags)
            if (name.Contains(tag, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static readonly string[] CjkFamilyTags =
    {
        "YaHei", "JhengHei", "SimSun", "SimHei", "KaiTi", "FangSong",
        "Songti", "STSong", "STHei", "STKaiti", "STFangsong", "PingFang",
        "Hiragino", "Meiryo", "Yu Gothic", "MS Mincho", "MS Gothic",
        "Batang", "Gungsuh", "Noto Sans CJK", "Noto Serif CJK",
        "Noto Sans SC", "Noto Serif SC", "Source Han",
        "WenQuanYi", "Droid Sans Fallback",
    };

    public static SKFont CrispHintedFont(SKTypeface typeface, float textSize)
    {
        var font = new SKFont(typeface, textSize);
        ConfigureText(font);
        return font;
    }

    public static SKPaint CreateMonoPaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateMonoFont(float textSize = 12)
    {
        Initialize();
        var typeface = _monoTypeface ?? _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        var font = new SKFont(typeface, textSize);
        ConfigureText(font, linearMetrics: true);
        return font;
    }

    public static SKFont CreateDevToolsFont(float textSize = 12)
    {
        Initialize();
        var typeface = _chineseTypeface ?? _monoTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        var font = new SKFont(typeface, textSize);
        ConfigureText(font, linearMetrics: true);
        return font;
    }

    public static SKPaint CreatePaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateFont(float textSize = 12)
    {
        Initialize();
        var typeface = _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        var font = new SKFont(typeface, textSize);
        ConfigureText(font);
        return font;
    }

    public static SKPaint CreateChinesePaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateChineseFont(float textSize = 12)
    {
        Initialize();
        var typeface = _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        var font = new SKFont(typeface, textSize);
        ConfigureText(font);
        return font;
    }

    public static SKTypeface? GetMonoTypeface()
    {
        Initialize();
        return _monoTypeface ?? _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
    }

    public static SKTypeface? GetDefaultTypeface()
    {
        Initialize();
        return _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
    }

    public static SKTypeface? GetChineseTypeface()
    {
        Initialize();
        return _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
    }

    public static SKTypeface? GetEmojiTypeface()
    {
        Initialize();
        return _emojiTypeface;
    }

    public static SKPaint CreateEmojiPaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateEmojiFont(float textSize = 12)
    {
        Initialize();
        var typeface = _emojiTypeface ?? _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        var font = new SKFont(typeface, textSize);
        ConfigureText(font);
        return font;
    }

    private static SKTypeface? FindFont(params string[] candidates)
    {
        // Try SKTypeface.FromFamilyName first (supports system font lookup by any valid name)
        foreach (var name in candidates)
        {
            try
            {
                var tf = SKTypeface.FromFamilyName(name);
                if (tf != null && !string.IsNullOrEmpty(tf.FamilyName))
                    return tf;
            }
            catch { }
        }
        // Fallback: exact match via font families array
        try
        {
            var families = SKFontManager.Default.FontFamilies.ToArray();
            foreach (var name in candidates)
            {
                int idx = Array.IndexOf(families, name);
                if (idx >= 0)
                {
                    var tf = SKFontManager.Default.GetFontStyles(idx).CreateTypeface(0);
                    if (tf != null && !string.IsNullOrEmpty(tf.FamilyName))
                        return tf;
                }
            }
        }
        catch { }
        return null;
    }
}
