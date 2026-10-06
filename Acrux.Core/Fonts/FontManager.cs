using SkiaSharp;
using System.Runtime.InteropServices;
using Acrux.Core.Dom;

namespace Acrux.Core.Fonts;

/// <summary>
/// Cross-platform font manager with font fallback chain.
/// Inspired by Blink's FontFallback and HarfBuzz text shaping.
/// Supports Windows, Linux, and macOS font discovery.
/// </summary>
public static class FontManager
{
    private static Dictionary<string, SKTypeface> _typefaceCache = new();
    private static Dictionary<int, SKTypeface> _fallbackByCodePoint = new();
    private static FontFallbackChain? _fallbackChain;
    private static readonly object _lock = new();

    /// <summary>
    /// The browser's <b>standard font</b> — what an element with no 'font-family'
    /// declaration computes to. Chrome's is a SERIF face, and on this box its setting
    /// resolves to "Times New Roman" (measured: getComputedStyle on an unstyled
    /// &lt;p&gt; reports font-family: "Times New Roman", and the reference string
    /// "The quick brown fox jumps over the lazy dog" is 292.38px at 16px).
    /// Hardcoding a sans default made every page that does not declare a font render in
    /// the wrong face AND measure ~5% wide, which shows up as odd word spacing.
    /// The name is deliberately a concrete family rather than 'serif': the platform
    /// alias table (Times New Roman -> Liberation Serif) resolves it to a
    /// metric-compatible face, which is what the reference engine does here.
    /// </summary>
    public const string StandardFontFamily = "Times New Roman";

    /// <summary>The reference engine's "Fixed-width" font size setting, which the
    /// generic 'monospace' carries with it: an element whose family list is exactly
    /// 'monospace' and whose size was never authored computes to 13px, not 16px
    /// (measured in Edge — 43 'a' glyphs are 279.50px wide there, 6.5px each, versus
    /// 8.0px each when the size stays 16px).</summary>
    public const float StandardFixedFontSize = 13f;

    /// <summary>True when the specified list is the single generic keyword 'monospace'
    /// (any of the aliases the platform accepts for it). A list that merely *ends* in
    /// the generic — <c>'Nope Not Here', monospace</c> — does not get the substituted
    /// size, and neither does <c>monospace, sans-serif</c>: measured in Edge both stay
    /// at 16px, so the substitution keys on the specified list, not on which face the
    /// glyphs finally came from.</summary>
    public static bool IsSoleMonospaceGeneric(string? familyList)
    {
        if (string.IsNullOrWhiteSpace(familyList)) return false;
        var parts = familyList.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1) return false;
        string one = parts[0].Trim().Trim('"', '\'').ToLowerInvariant();
        // Only the exact keyword: measured in Edge, 'ui-monospace' and 'fixed' do NOT
        // get the substituted size — they stay at the inherited 16px.
        return one == "monospace";
    }

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
        // Every text run with a glyph the primary face lacks lands here — measurement,
        // line breaking AND paint all call it per character. Without a cache each miss
        // walks the whole generic family list through SKTypeface.FromFamilyName
        // (a fontconfig query), which is millisecond-scale; on CJK pages that turned a
        // five-line paragraph into seconds of raster time. SKTypeface is immutable and
        // shared by reference everywhere else, so caching the instance is safe.
        lock (_lock)
        {
            if (_fallbackByCodePoint.TryGetValue(codePoint, out var cached))
                return cached;
            var tf = _fallbackChain!.GetFallbackForCodePoint(codePoint);
            _fallbackByCodePoint[codePoint] = tf;
            return tf;
        }
    }

    public static void ClearCache()
    {
        lock (_lock)
        {
            foreach (var tf in _typefaceCache.Values)
                tf?.Dispose();
            _typefaceCache.Clear();
            // Fallback faces are handed out by GetFallbackTypeface and may still be
            // referenced by live paint state — drop the map but not the objects.
            _fallbackByCodePoint.Clear();
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

            // 1. A face installed under exactly this name wins outright.
            var exact = TryCreateExactTypeface(fam, skWeight, skSlant);
            if (exact != null) return exact;

            // 2. Otherwise ask the platform matcher. On Linux that is fontconfig,
            //    which carries the distro's alias table: "Arial" -> Liberation Sans,
            //    "Times New Roman" -> Liberation Serif, "Courier New" -> Liberation
            //    Mono. Those substitutes ship as metric-compatible clones, so every
            //    other application on the machine lays Arial out with them. Our own
            //    candidate lists were assembled around Windows/macOS faces and used to
            //    shadow the aliases with Noto Sans, whose advances are ~9% wider than
            //    Arial's and whose line box is 22px instead of 18px at 16px — which
            //    threw off every text measurement, line break and intrinsic-width
            //    comparison against a reference browser.
            if (!IsGenericFontKeyword(fam))
            {
                var aliased = TryMatchAliasedFamily(fam, skWeight, skSlant);
                if (aliased != null) return aliased;
            }

            // 3. Curated candidates: the seeds behind the generic keywords (CSS Fonts
            //    4 §5.4) and the script families fontconfig has no alias for.
            if (_familyFallbacks.TryGetValue(fam.ToLowerInvariant(), out var candidates))
            {
                foreach (var candidate in candidates)
                {
                    var tf = TryCreateExactTypeface(candidate, skWeight, skSlant)
                             ?? TryMatchAliasedFamily(candidate, skWeight, skSlant, IsMonospaceKeyword(candidate));
                    if (tf != null) return tf;
                }
            }
        }

        // Nothing in the list matched. CSS Fonts 4 §5.3.1 then hands the text to the
        // "first available font", i.e. the browser's standard font — a serif face in
        // every mainstream engine — rather than to the platform's no-match default,
        // which on a CJK-configured desktop is a huge multi-script sans (and ~28%
        // taller than Arial). Windows and macOS keep the previous tail of the chain:
        // their font linkers already resolve unknown names for us.
        if (UsesPlatformFontMatching)
        {
            foreach (var candidate in GetPlatformCandidates("serif"))
            {
                var tf = TryCreateExactTypeface(candidate, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright)
                         ?? TryMatchAliasedFamily(candidate, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);
                if (tf != null) return tf;
            }
        }

        foreach (var candidate in _genericFallbacks)
        {
            var tf = TryCreateTypeface(candidate, skWeight, skSlant);
            if (tf != null) return tf;
        }

        return _defaultTypeface;
    }

    /// <summary>The generic classes of CSS Fonts 4 §5.5. They are not family names:
    /// the user agent resolves them through its own font settings, so the platform
    /// matcher must not answer them with whatever "sans-serif" happens to alias to
    /// in this desktop's fontconfig configuration (a CJK face on many images).</summary>
    private static bool IsGenericFontKeyword(string family) => family.ToLowerInvariant() switch
    {
        "serif" or "sans-serif" or "monospace" or "cursive" or "fantasy"
        or "system-ui" or "ui-serif" or "ui-sans-serif" or "ui-monospace" or "ui-rounded"
        or "math" or "emoji" or "fixed" => true,
        _ => false,
    };

    /// <summary>Installed under exactly this name (case-insensitive).</summary>
    private SKTypeface? TryCreateExactTypeface(string family, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        var tf = TryCreateTypeface(family, weight, slant);
        if (tf == null) return null;
        return string.Equals(tf.FamilyName, family, StringComparison.OrdinalIgnoreCase) ? tf : null;
    }

    /// <summary>True where the OS font matcher (fontconfig) owns family resolution and
    /// carries the distro's metric-compatible alias table — Linux and the other Unix
    /// flavours. Windows and macOS resolve links through their own font linkers.</summary>
    private static bool UsesPlatformFontMatching =>
        !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>Ask the platform matcher for the family and accept its answer only
    /// when it is a real substitution: a face whose name differs both from the
    /// request and from the desktop's own class defaults. The second test is what
    /// separates "fontconfig aliased Arial to Liberation Sans" from "fontconfig gave
    /// up and returned its serif/sans default" — a request for "Georgia" on a box
    /// without Georgia must fall through to the first-available font (CSS Fonts 4
    /// §5.3.1), not to the CJK face the desktop uses to render generic serif.</summary>
    private SKTypeface? TryMatchAliasedFamily(string family, SKFontStyleWeight weight, SKFontStyleSlant slant,
        bool acceptClassDefault = false)
    {
        if (!UsesPlatformFontMatching) return null;

        var tf = TryCreateTypeface(family, weight, slant);
        if (tf == null) return null;
        string resolved = tf.FamilyName ?? "";
        if (resolved.Length == 0) return null;
        if (string.Equals(resolved, family, StringComparison.OrdinalIgnoreCase)) return null;
        if (!acceptClassDefault && PlatformClassDefaultNames.Contains(resolved)) return null;
        return tf;
    }

    /// <summary>The fixed-width generic classes. Unlike serif and sans-serif — which the
    /// reference browser resolves through its own font preferences, so the platform
    /// matcher must not be allowed to answer them with the desktop's CJK default — the
    /// monospace class goes straight to fontconfig, and its answer is accepted even when
    /// it is a class default. Measured here: <c>font-family: monospace</c> sets ASCII at
    /// 0.5em in a reference browser because fontconfig's "monospace" alias is
    /// "Noto Sans Mono CJK SC", while every other generic stays Latin.</summary>
    private static bool IsMonospaceKeyword(string family) => family.ToLowerInvariant() switch
    {
        "monospace" or "fixed" or "ui-monospace" => true,
        _ => false,
    };

    /// <summary>Face names the matcher hands out when a request carries no family it
    /// knows: the answers to nonsense names and to the generic classes themselves.
    /// Collected once, on first use, because they describe the installed system.</summary>
    private HashSet<string>? _platformClassDefaultNames;

    private HashSet<string> PlatformClassDefaultNames => _platformClassDefaultNames ??= BuildPlatformClassDefaults();

    private HashSet<string> BuildPlatformClassDefaults()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var probe in new[]
                 {
                     "acrux-not-a-real-family \u2400", "\u2400\u2400", "sans-serif", "serif",
                     "monospace", "cursive", "fantasy",
                 })
        {
            var tf = TryCreateTypeface(probe, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);
            if (!string.IsNullOrEmpty(tf?.FamilyName)) names.Add(tf!.FamilyName!);
        }
        return names;
    }


    public SKTypeface GetFallbackForCodePoint(int codePoint)
    {
        // The colour face is the one case that must come BEFORE the generic chain
        // rather than last: it is the only face with the right advance (a 1em strike),
        // and the paint side already draws those characters from it — measuring them
        // against DejaVu made an emoji run a third narrower than what was drawn, so
        // the text after it overlapped. The face stays last for everything else,
        // because it carries legacy coverage of Thai and Indic and would silently
        // replace those scripts' own face (see SetupGenericFallbacks).
        //
        // Which characters qualify is Unicode's Emoji_Presentation property, not
        // "everything above U+1F000": ✈ U+2708 is an emoji but text-presentation, and
        // an open-ended astral test also claimed CJK extension B (U+20000+), which no
        // emoji font contains and which then painted as tofu. ContainsGlyph still gates
        // the decision so a machine without a colour face keeps its outline metrics.
        if (EmojiRanges.IsDefaultEmojiPresentation(codePoint)
            && _emojiTypeface != null && _emojiTypeface.ContainsGlyph(codePoint))
            return _emojiTypeface;

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
                // Blink's "fixed font" preference on Windows is Courier New, and
                // that is what the UA sheet's form-control fonts (textarea, and the
                // cols x "0" default width built from it) resolve to - measured at
                // 0.597em, i.e. Courier New's 0.6em, not Consolas' 0.55em.
                "monospace" => new() { "Courier New", "Consolas", "Cascadia Mono", "Lucida Console" },
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
                // Chrome resolves the generic classes through its own font settings,
                // whose defaults are the CSS named families: sans-serif -> Arial,
                // serif -> Times New Roman. fontconfig then aliases those to the
                // metric-compatible faces installed here, which is why "sans-serif"
                // must NOT be handed to the matcher as the literal string — on a
                // CJK-configured desktop that answers with Noto Sans CJK (22px line
                // box at 16px, ~9% wider than Arial).
                "sans-serif" => new() { "Arial", "Liberation Sans", "DejaVu Sans", "Noto Sans", "Roboto", "Cantarell", "FreeSans" },
                "serif" => new() { "Times New Roman", "Liberation Serif", "DejaVu Serif", "Noto Serif", "FreeSerif" },
                // The fixed font is the one place Chrome does consult the platform
                // generic, so "monospace" is matched literally first.
                "monospace" => new() { "monospace", "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Ubuntu Mono", "FreeMono" },
                "emoji" => new() { "Noto Color Emoji", "EmojiOne", "Twemoji Mozilla" },
                "chinese" => new() { "Noto Sans CJK SC", "Noto Sans SC", "WenQuanYi Micro Hei", "WenQuanYi Zen Hei", "AR PL UMing CN" },
                "japanese" => new() { "Noto Sans CJK JP", "Noto Sans JP" },
                "korean" => new() { "Noto Sans CJK KR", "Noto Sans KR" },
                "thai" => new() { "Noto Sans Thai", "Loma", "Garuda" },
                "indic" => new() { "Noto Sans Devanagari", "Mangal", "Lohit Devanagari" },
                // Named families are NOT listed here on purpose: the fontconfig alias
                // table installed with the distro is authoritative and already covers
                // Arial/Helvetica/Times/Courier. A family it does not know resolves
                // through the "first available font" rule above, exactly as Chrome's
                // does — hardcoding a substitute here would only drift from it.
                "arial" => new() { "Arial" },
                "helvetica" => new() { "Helvetica", "Arial" },
                "times" => new() { "Times New Roman" },
                "courier" => new() { "Courier New" },
                "verdana" => new() { "Verdana" },
                "georgia" => new() { "Georgia" },
                "palatino" => new() { "Palatino Linotype", "Palatino" },
                "garamond" => new() { "Garamond" },
                "bookman" => new() { "Bookman Old Style" },
                "comic-sans" => new() { "Comic Sans MS" },
                "trebuchet" => new() { "Trebuchet MS" },
                "arial-black" => new() { "Arial Black" },
                "impact" => new() { "Impact" },
                "generic-fallback" => new() { "Arial", "Noto Sans", "DejaVu Sans", "Times New Roman", "Noto Sans CJK SC", "Noto Color Emoji" },
                _ => new() { "Arial" }
            };
        }
    }
}
