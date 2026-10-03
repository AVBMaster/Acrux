using SkiaSharp;
using System.Runtime.InteropServices;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Fonts;

/// <summary>
/// Cross-platform font manager with font fallback chain.
/// Inspired by Blink's FontFallback and HarfBuzz text shaping.
/// Supports Windows, Linux, and macOS font discovery.
/// </summary>
public static class FontManager
{
    private static Dictionary<string, SKTypeface> _typefaceCache = new();
    private static FontFallbackChain? _fallbackChain;
    private static readonly object _lock = new();

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_fallbackChain != null) return;
            _fallbackChain = new FontFallbackChain();
        }
    }

    public static SKTypeface GetOrCreateTypeface(string family, FontWeight weight = FontWeight.Normal, FontStyleType style = FontStyleType.Normal)
    {
        Initialize();

        var key = $"{family}:{weight}:{style}";
        // UI + tab worker threads resolve fonts concurrently in threaded-tab mode.
        lock (_lock)
        {
            if (_typefaceCache.TryGetValue(key, out var cached))
                return cached;

            var typeface = _fallbackChain!.Resolve(family, weight, style);
            _typefaceCache[key] = typeface;
            return typeface;
        }
    }

    public static SKTypeface GetDefaultTypeface()
    {
        Initialize();
        return _fallbackChain!.DefaultTypeface;
    }

    public static SKTypeface GetMonospaceTypeface()
    {
        Initialize();
        return _fallbackChain!.MonospaceTypeface;
    }

    public static SKTypeface GetSansSerifTypeface()
    {
        Initialize();
        return _fallbackChain!.SansSerifTypeface;
    }

    public static SKTypeface GetSerifTypeface()
    {
        Initialize();
        return _fallbackChain!.SerifTypeface;
    }

    public static SKTypeface GetEmojiTypeface()
    {
        Initialize();
        return _fallbackChain!.EmojiTypeface;
    }

    public static SKTypeface GetFallbackTypeface(int codePoint)
    {
        Initialize();
        return _fallbackChain!.GetFallbackForCodePoint(codePoint);
    }

    public static void ClearCache()
    {
        lock (_lock)
        {
            foreach (var tf in _typefaceCache.Values)
                tf?.Dispose();
            _typefaceCache.Clear();
        }
    }

    public static string[] GetAvailableFontFamilies()
    {
        Initialize();
        return _fallbackChain!.AvailableFamilies;
    }

    public static bool HasCharacter(SKTypeface typeface, int codePoint)
    {
        return typeface.ContainsGlyph(codePoint);
    }
}

/// <summary>
/// Font fallback chain - tries fonts in order until one contains the needed glyphs.
/// Similar to Blink's FontFallbackIterator.
/// </summary>
public class FontFallbackChain
{
    private readonly List<string> _genericFallbacks = new();
    private readonly Dictionary<string, List<string>> _familyFallbacks = new();
    private readonly SKTypeface _defaultTypeface;
    private readonly SKTypeface _monospaceTypeface;
    private readonly SKTypeface _sansSerifTypeface;
    private readonly SKTypeface _serifTypeface;
    private readonly SKTypeface _emojiTypeface;
    private readonly string[] _availableFamilies;

    public SKTypeface DefaultTypeface => _defaultTypeface;
    public SKTypeface MonospaceTypeface => _monospaceTypeface;
    public SKTypeface SansSerifTypeface => _sansSerifTypeface;
    public SKTypeface SerifTypeface => _serifTypeface;
    public SKTypeface EmojiTypeface => _emojiTypeface;
    public string[] AvailableFamilies => _availableFamilies;

    public FontFallbackChain()
    {
        _availableFamilies = SKFontManager.Default.FontFamilies.ToArray();

        _defaultTypeface = FindBestFont(
            GetPlatformCandidates("sans-serif"),
            SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        _monospaceTypeface = FindBestFont(
            GetPlatformCandidates("monospace"),
            SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        _sansSerifTypeface = FindBestFont(
            GetPlatformCandidates("sans-serif"),
            SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        _serifTypeface = FindBestFont(
            GetPlatformCandidates("serif"),
            SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        _emojiTypeface = FindBestFont(
            GetPlatformCandidates("emoji"),
            SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        SetupGenericFallbacks();
        SetupFamilyFallbacks();
    }

    public SKTypeface Resolve(string family, FontWeight weight, FontStyleType style)
    {
        var skWeight = ConvertWeight(weight);
        var skSlant = style == FontStyleType.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

        // Walk the FULL CSS font-family list so a missing first family falls back
        // to the next specified one (e.g. "MyFont, Arial, sans-serif") instead of
        // jumping straight to the generic/default. Generic names map through the
        // per-generic candidates.
        foreach (var raw in family.Split(','))
        {
            string fam = raw.Trim().Trim('"', '\'').Trim();
            if (fam.Length == 0) continue;

            if (_familyFallbacks.TryGetValue(fam.ToLowerInvariant(), out var fallbacks))
            {
                foreach (var candidate in fallbacks)
                {
                    var tf = TryCreateTypeface(candidate, skWeight, skSlant);
                    if (tf != null) return tf;
                }
            }

            var direct = TryCreateTypeface(fam, skWeight, skSlant);
            if (direct != null) return direct;
        }

        foreach (var candidate in _genericFallbacks)
        {
            var tf = TryCreateTypeface(candidate, skWeight, skSlant);
            if (tf != null) return tf;
        }

        return _defaultTypeface;
    }

    public SKTypeface GetFallbackForCodePoint(int codePoint)
    {
        foreach (var family in _genericFallbacks)
        {
            var tf = TryCreateTypeface(family, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);
            if (tf != null && tf.ContainsGlyph(codePoint))
                return tf;
        }

        return _defaultTypeface;
    }

    private SKTypeface? TryCreateTypeface(string family, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        try
        {
            var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
            var tf = SKTypeface.FromFamilyName(family, style);
            if (tf != null && !string.IsNullOrEmpty(tf.FamilyName))
                return tf;
        }
        catch { }
        return null;
    }

    private SKTypeface FindBestFont(IEnumerable<string> candidates, SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant)
    {
        foreach (var name in candidates)
        {
            var tf = TryCreateTypeface(name, weight, slant);
            if (tf != null) return tf;
        }
        return SKTypeface.Default;
    }

    public static SKFontStyleWeight ConvertWeight(FontWeight weight) => weight switch
    {
        FontWeight.Thin => SKFontStyleWeight.Thin,
        FontWeight.ExtraLight => SKFontStyleWeight.ExtraLight,
        FontWeight.Light => SKFontStyleWeight.Light,
        FontWeight.Medium => SKFontStyleWeight.Medium,
        FontWeight.SemiBold => SKFontStyleWeight.SemiBold,
        FontWeight.Bold => SKFontStyleWeight.Bold,
        FontWeight.ExtraBold => SKFontStyleWeight.ExtraBold,
        FontWeight.Black => SKFontStyleWeight.Black,
        _ => SKFontStyleWeight.Normal,
    };

    private void SetupGenericFallbacks()
    {
        // Symbol and emoji faces must come LAST in the per-code-point chain: they
        // carry legacy coverage of Thai, Indic and other scripts, and matching them
        // first silently replaced the script's own face (Segoe UI Symbol's Thai is
        // ~9% narrower than Leelawadee UI's, its Devanagari ~13% wider).
        string[] lastResort = { "Segoe UI Emoji", "Segoe UI Symbol", "Apple Color Emoji",
                                "Noto Color Emoji", "Twemoji Mozilla", "EmojiOne" };

        foreach (var family in GetPlatformCandidates("generic-fallback"))
        {
            if (!lastResort.Contains(family) && !_genericFallbacks.Contains(family))
                _genericFallbacks.Add(family);
        }

        // The generic chain has to reach a script-specific face for every script the
        // preset counter styles and CJK text use, or the code point falls through to
        // SKTypeface.Default, whose Hangul advance is proportional (~0.58em) instead
        // of the full 1em a Korean face gives. Appended after the Latin faces so
        // Latin text keeps resolving to Segoe UI exactly as before; only glyphs none
        // of the Latin faces own reach these.
        foreach (var script in new[] { "korean", "japanese", "thai", "indic" })
        {
            foreach (var candidate in GetPlatformCandidates(script))
            {
                if (!_genericFallbacks.Contains(candidate))
                    _genericFallbacks.Add(candidate);
            }
        }

        foreach (var family in lastResort)
        {
            if (!_genericFallbacks.Contains(family))
                _genericFallbacks.Add(family);
        }
    }

    private void SetupFamilyFallbacks()
    {
        // CSS Fonts 4 §5.4: the generic font keywords must resolve to the platform
        // face of that class. Without these entries "monospace" fell through to the
        // default (proportional) family, so <pre>/<code> text was laid out with the
        // wrong advances.
        _familyFallbacks["monospace"] = GetPlatformCandidates("monospace");
        _familyFallbacks["ui-monospace"] = GetPlatformCandidates("monospace");
        _familyFallbacks["fixed"] = GetPlatformCandidates("monospace");
        _familyFallbacks["serif"] = GetPlatformCandidates("serif");
        _familyFallbacks["ui-serif"] = GetPlatformCandidates("serif");
        _familyFallbacks["sans-serif"] = GetPlatformCandidates("sans-serif");
        _familyFallbacks["ui-sans-serif"] = GetPlatformCandidates("sans-serif");
        _familyFallbacks["system-ui"] = GetPlatformCandidates("sans-serif");
        _familyFallbacks["rounded"] = GetPlatformCandidates("sans-serif");
        _familyFallbacks["ui-rounded"] = GetPlatformCandidates("sans-serif");
        _familyFallbacks["cursive"] = GetPlatformCandidates("comic-sans");
        _familyFallbacks["fantasy"] = GetPlatformCandidates("impact");
        _familyFallbacks["math"] = new List<string> { "Cambria Math", "STIX Two Math", "DejaVu Math TeX Gyre" };
        _familyFallbacks["emoji"] = GetPlatformCandidates("emoji");

        _familyFallbacks["arial"] = GetPlatformCandidates("arial");
        _familyFallbacks["helvetica"] = GetPlatformCandidates("helvetica");
        _familyFallbacks["times new roman"] = GetPlatformCandidates("times");
        _familyFallbacks["courier new"] = GetPlatformCandidates("courier");
        _familyFallbacks["verdana"] = GetPlatformCandidates("verdana");
        _familyFallbacks["georgia"] = GetPlatformCandidates("georgia");
        _familyFallbacks["palatino"] = GetPlatformCandidates("palatino");
        _familyFallbacks["garamond"] = GetPlatformCandidates("garamond");
        _familyFallbacks["bookman"] = GetPlatformCandidates("bookman");
        _familyFallbacks["comic sans ms"] = GetPlatformCandidates("comic-sans");
        _familyFallbacks["trebuchet ms"] = GetPlatformCandidates("trebuchet");
        _familyFallbacks["arial black"] = GetPlatformCandidates("arial-black");
        _familyFallbacks["impact"] = GetPlatformCandidates("impact");

        _familyFallbacks["microsoft yahei"] = GetPlatformCandidates("chinese");
        _familyFallbacks["simhei"] = GetPlatformCandidates("chinese");
        _familyFallbacks["simsun"] = GetPlatformCandidates("chinese");
        _familyFallbacks["source han sans sc"] = GetPlatformCandidates("chinese");
        _familyFallbacks["noto sans sc"] = GetPlatformCandidates("chinese");
        _familyFallbacks["pingfang sc"] = GetPlatformCandidates("chinese");
        _familyFallbacks["hiragino sans gb"] = GetPlatformCandidates("chinese");

        _familyFallbacks["meiryo"] = GetPlatformCandidates("japanese");
        _familyFallbacks["yu gothic"] = GetPlatformCandidates("japanese");
        _familyFallbacks["noto sans jp"] = GetPlatformCandidates("japanese");
        _familyFallbacks["hiragino kaku gothic"] = GetPlatformCandidates("japanese");

        _familyFallbacks["malgun gothic"] = GetPlatformCandidates("korean");
        _familyFallbacks["noto sans kr"] = GetPlatformCandidates("korean");
        _familyFallbacks["apple gothic"] = GetPlatformCandidates("korean");
    }

    private static List<string> GetPlatformCandidates(string category)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return category switch
            {
                "sans-serif" => new() { "Segoe UI", "Arial", "Tahoma", "Verdana", "Calibri" },
                "serif" => new() { "Times New Roman", "Georgia", "Palatino Linotype", "Book Antiqua" },
                "monospace" => new() { "Consolas", "Cascadia Code", "Cascadia Mono", "Courier New", "Lucida Console" },
                "emoji" => new() { "Segoe UI Emoji", "Segoe UI Symbol", "Arial Unicode MS" },
                "chinese" => new() { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "NSimSun", "FangSong", "KaiTi" },
                "japanese" => new() { "Meiryo", "Yu Gothic", "MS Gothic", "MS Mincho" },
                "korean" => new() { "Malgun Gothic", "Gulim", "Dotum" },
                // Neither Segoe UI nor YaHei covers Thai or the Indic scripts, so these
                // fell through to the default face and painted .notdef boxes.
                "thai" => new() { "Leelawadee UI", "Tahoma", "Thonburi" },
                "indic" => new() { "Nirmala UI", "Mangal", "Kohinoor Devanagari" },
                "arial" => new() { "Arial", "Arial Unicode MS", "Microsoft Sans Serif" },
                "helvetica" => new() { "Arial", "Microsoft Sans Serif" },
                "times" => new() { "Times New Roman", "Times" },
                "courier" => new() { "Courier New", "Consolas" },
                "verdana" => new() { "Verdana", "Tahoma" },
                "georgia" => new() { "Georgia", "Times New Roman" },
                "palatino" => new() { "Palatino Linotype", "Book Antiqua", "Palatino" },
                "garamond" => new() { "Garamond", "Book Antiqua" },
                "bookman" => new() { "Book Antiqua", "Bookman Old Style" },
                "comic-sans" => new() { "Comic Sans MS" },
                "trebuchet" => new() { "Trebuchet MS" },
                "arial-black" => new() { "Arial Black", "Impact" },
                "impact" => new() { "Impact", "Arial Black" },
                "generic-fallback" => new() { "Segoe UI", "Arial", "Times New Roman", "Microsoft YaHei", "Segoe UI Emoji", "Segoe UI Symbol" },
                _ => new() { "Segoe UI" }
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return category switch
            {
                "sans-serif" => new() { "SF Pro Text", "SF Pro Display", "Helvetica Neue", "Arial", "Lucida Grande" },
                "serif" => new() { "Times New Roman", "Georgia", "Palatino", "Book Antiqua" },
                "monospace" => new() { "SF Mono", "Menlo", "Monaco", "Courier New" },
                "emoji" => new() { "Apple Color Emoji" },
                "chinese" => new() { "PingFang SC", "PingFang TC", "Heiti SC", "STHeiti", "Songti SC" },
                "japanese" => new() { "Hiragino Kaku Gothic Pro", "Hiragino Sans", "YuGothic" },
                "korean" => new() { "AppleGothic", "Apple SD Gothic Neo" },
                "thai" => new() { "Thonburi", "Noto Sans Thai" },
                "indic" => new() { "Kohinoor Devanagari", "Devanagari MT", "Noto Sans Devanagari" },
                "arial" => new() { "Arial", "Arial Unicode MS" },
                "helvetica" => new() { "Helvetica Neue", "Helvetica", "Arial" },
                "times" => new() { "Times New Roman", "Times" },
                "courier" => new() { "Courier New", "Courier" },
                "verdana" => new() { "Verdana" },
                "georgia" => new() { "Georgia" },
                "palatino" => new() { "Palatino" },
                "garamond" => new() { "Garamond" },
                "bookman" => new() { "Bookman" },
                "comic-sans" => new() { "Comic Sans MS" },
                "trebuchet" => new() { "Trebuchet MS" },
                "arial-black" => new() { "Arial Black" },
                "impact" => new() { "Impact" },
                "generic-fallback" => new() { "SF Pro Text", "Helvetica Neue", "Times New Roman", "PingFang SC", "Apple Color Emoji" },
                _ => new() { "SF Pro Text" }
            };
        }
        else
        {
            return category switch
            {
                "sans-serif" => new() { "Noto Sans", "DejaVu Sans", "Liberation Sans", "Ubuntu", "Cantarell", "FreeSans" },
                "serif" => new() { "Noto Serif", "DejaVu Serif", "Liberation Serif", "FreeSerif" },
                "monospace" => new() { "Noto Sans Mono", "DejaVu Sans Mono", "Liberation Mono", "Ubuntu Mono", "FreeMono" },
                "emoji" => new() { "Noto Color Emoji", "EmojiOne", "Twemoji Mozilla" },
                "chinese" => new() { "Noto Sans CJK SC", "Noto Sans SC", "WenQuanYi Micro Hei", "WenQuanYi Zen Hei", "AR PL UMing CN" },
                "japanese" => new() { "Noto Sans CJK JP", "Noto Sans JP" },
                "korean" => new() { "Noto Sans CJK KR", "Noto Sans KR" },
                "thai" => new() { "Noto Sans Thai", "Loma", "Garuda" },
                "indic" => new() { "Noto Sans Devanagari", "Mangal", "Lohit Devanagari" },
                "arial" => new() { "Noto Sans", "DejaVu Sans", "Liberation Sans" },
                "helvetica" => new() { "Noto Sans", "DejaVu Sans" },
                "times" => new() { "Noto Serif", "DejaVu Serif", "Liberation Serif" },
                "courier" => new() { "Noto Sans Mono", "DejaVu Sans Mono", "Liberation Mono" },
                "verdana" => new() { "DejaVu Sans" },
                "georgia" => new() { "DejaVu Serif" },
                "palatino" => new() { "DejaVu Serif" },
                "garamond" => new() { "DejaVu Serif" },
                "bookman" => new() { "DejaVu Serif" },
                "comic-sans" => new() { "DejaVu Sans" },
                "trebuchet" => new() { "DejaVu Sans" },
                "arial-black" => new() { "DejaVu Sans" },
                "impact" => new() { "DejaVu Sans" },
                "generic-fallback" => new() { "Noto Sans", "DejaVu Sans", "Noto Serif", "Noto Sans CJK SC", "Noto Color Emoji" },
                _ => new() { "Noto Sans" }
            };
        }
    }
}
