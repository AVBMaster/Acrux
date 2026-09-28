using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;
using System.Text;

namespace UpBrowser.Core.Layout.Inline;

// ==========================================================================
// Mirror of platform/fonts/shaping/shape_result.h + shaper hooks.
//
// The original uses HarfBuzz for complex shaping (ligatures, kerning, complex
// scripts). UpBrowser has no HarfBuzz integration in the layout pipeline yet,
// so the glyph advances here come from the SkiaSharp text measurer (via
// ITextMeasurer), measured per character. This reproduces the ShapeResult data
// model and the exact operations the line break algorithm performs on it
// (CachedWidth, PositionForOffset, safe-to-break offsets, etc.), at the cost
// of per-character shaping approximation (documented limitation).
// ==========================================================================

public enum ShapeOptions
{
    Default = 0,
    RunSegmenting = 1 << 0,
    AvoidRotation = 1 << 1,
    TextOrientationFallback = 1 << 2,
}

/// <summary>
/// A run of shaped glyphs for an inline text item. Mirrors the surface of
/// shape_result.h / shape_result_view.h used by the line breaker.
/// </summary>
public sealed class ShapeResult
{
    public int StartIndex { get; }
    public int EndIndex { get; }
    public bool IsRtl { get; }
    public string Text { get; }
    public float[] Advances { get; }

    public ShapeResult(int startIndex, int endIndex, string text, float[] advances, bool isRtl)
    {
        StartIndex = startIndex;
        EndIndex = endIndex;
        Text = text;
        Advances = advances;
        IsRtl = isRtl;
    }

    public int NumGlyphs => Advances.Length;
    public bool IsNull => Text == null;
    public int Length => EndIndex - StartIndex;

    public float InlineSize => SumWidth(0, Advances.Length);

    /// <summary>Compat: previous simplified ShapeResult exposed Width.</summary>
    public float Width => InlineSize;
    public float SnappedWidth() => InlineSize;

    public void EnsurePositionData() { }

    public void AccumulateUsedFonts() { }

    /// <summary>Width of the range [start, end) in absolute text offsets.</summary>
    public float CachedWidth(int start, int end)
    {
        int localStart = Math.Clamp(start - StartIndex, 0, Advances.Length);
        int localEnd = Math.Clamp(end - StartIndex, localStart, Advances.Length);
        return SumWidth(localStart, localEnd);
    }

    /// <summary>Position (advance) at the given absolute text offset.</summary>
    public float CachedPositionForOffset(int offset)
    {
        int local = Math.Clamp(offset - StartIndex, 0, Advances.Length);
        return SumWidth(0, local);
    }

    public float PositionForOffset(int offset) => CachedPositionForOffset(offset);

    public float GetWidth(int start, int end)
    {
        if (IsRtl)
        {
            // For RTL run, advances are stored in the reading (logical) order.
            return CachedWidth(start, end);
        }
        return CachedWidth(start, end);
    }

    /// <summary>
    /// Last safe-to-break offset not after |endOffset|. Mirrors
    /// ShapeResult::PreviousSafeToBreakOffset().
    /// </summary>
    public int PreviousSafeToBreakOffset(int endOffset)
    {
        int local = Math.Clamp(endOffset - StartIndex, 0, Advances.Length);
        if (local <= 0) return StartIndex;
        // Walk back to a safe-to-break position inside [StartIndex, endOffset).
        int i = local;
        while (i > 0)
        {
            if (IsSafeToBreak(i)) return StartIndex + i;
            i--;
        }
        return StartIndex;
    }

    public int NextSafeToBreakOffset(int startOffset)
    {
        int local = Math.Clamp(startOffset - StartIndex, 0, Advances.Length);
        for (int i = local; i <= Advances.Length; i++)
        {
            if (IsSafeToBreak(i)) return StartIndex + i;
        }
        return EndIndex;
    }

    private bool IsSafeToBreak(int localIndex)
    {
        if (localIndex <= 0 || localIndex > Advances.Length) return true;
        char c = Text[StartIndex + localIndex - 1];
        return c == ' ' || c == '\t' || c == '\u00A0' || c == '\u00AD' || Character.IsLineBreakBoundary(c);
    }

    private float SumWidth(int start, int end) => MonoSums.Compute(Advances, start, end);

    /// <summary>Create a sub-range view as a new ShapeResult (mirrors ShapeResultView::Create).</summary>
    public static ShapeResult? Create(ShapeResult? source, int start, int end)
    {
        if (source == null) return null;
        int clampedStart = Math.Max(source.StartIndex, start);
        int clampedEnd = Math.Min(source.EndIndex, end);
        if (clampedEnd <= clampedStart) return new ShapeResult(clampedStart, clampedStart, source.Text, Array.Empty<float>(), source.IsRtl);
        int len = clampedEnd - clampedStart;
        var advances = new float[len];
        Array.Copy(source.Advances, clampedStart - source.StartIndex, advances, 0, len);
        return new ShapeResult(clampedStart, clampedEnd, source.Text, advances, source.IsRtl);
    }

    /// <summary>Create a copy spanning the whole |source|.</summary>
    public static ShapeResult Create(ShapeResult source)
    {
        var advances = new float[source.Advances.Length];
        Array.Copy(source.Advances, advances, advances.Length);
        return new ShapeResult(source.StartIndex, source.EndIndex, source.Text, advances, source.IsRtl);
    }

    internal static class MonoSums
    {
        public static float Compute(float[] advances, int start, int end)
        {
            float sum = 0;
            for (int i = start; i < end; i++) sum += advances[i];
            return sum;
        }
    }
}

/// <summary>
/// Thin view wrapper mirroring ShapeResultView. In this port it aliases
/// ShapeResult, since C# has no pointer views; ranges are copied.
/// </summary>
public static class ShapeResultView
{
    public static ShapeResult? Create(ShapeResult? source, int start, int end) => ShapeResult.Create(source, start, end);
    public static ShapeResult Create(ShapeResult source) => ShapeResult.Create(source);
}

/// <summary>Hooks for per-character glyph measurement (shaper proxy).</summary>
public sealed class HarfBuzzShaper
{
    // Keyed by the text AND the font properties that affect glyph advances. Keying
    // by text alone returned a stale width whenever the same string was shaped at
    // a different size/family/weight (e.g. a size ramp reusing one word), which
    // made runs advance by the wrong amount and overlap.
    public static readonly Dictionary<(string Text, float FontSize, string Family, int Weight, int Style, float LetterSpacing, float WordSpacing), ShapeResult> ShapeCache = new();
    private readonly string _text;

    public HarfBuzzShaper(string text)
    {
        _text = text;
    }

    public string Text => _text;

    private static (string, float, string, int, int, float, float) CacheKey(string text, ComputedStyle style) =>
        (text, style.FontSize, style.FontFamily, (int)style.FontWeight, (int)style.FontStyle, style.LetterSpacing, style.WordSpacing);

    /// <summary>
    /// Shape [start, end) of the text. Glyph advances come from the text
    /// measurer; per-character advances approximate complex shaping.
    /// </summary>
    public ShapeResult Shape(ComputedStyle style, TextDirection direction, int start, int end, object? segmentRange = null, ShapeOptions options = ShapeOptions.Default)
    {
        start = Math.Max(0, start);
        end = Math.Min(_text.Length, end);
        if (end <= start)
            return new ShapeResult(start, start, _text, Array.Empty<float>(), direction == TextDirection.Rtl);

        if (start == 0 && end == _text.Length && ShapeCache.TryGetValue(CacheKey(_text, style), out var whole))
            return whole;

        var advances = new float[end - start];
        float letterSpacing = style.LetterSpacing;
        float wordSpacing = style.WordSpacing;
        for (int i = start; i < end; i++)
        {
            char c = _text[i];
            float advance = ShapeCharacter(c, style);
            if (letterSpacing != 0) advance += letterSpacing;
            if (wordSpacing != 0 && (c == ' ' || c == '\t' || c == '\u00A0')) advance += wordSpacing;
            advances[i - start] = advance;
        }

        // Reconcile per-character advances with the SHAPED run
        // width. DrawTextOp renders the whole run through Skia's text pipeline,
        // which applies kerning/ligatures that per-char sums miss — so wrapping
        // measurements drifted from what users saw on screen. Distribute the
        // shaping delta proportionally over the clusters (HarfBuzz-grade
        // cluster positioning arrives with the SkiaSharp.HarfBuzz packaging
        // decision; this removes the drift today).
        string runText = _text[start..end];
        float charSum = 0;
        foreach (var a in advances) charSum += a;
        if (advances.Length > 1 && charSum > 0.01f)
        {
            float shapedWidth = TextMeasureProxy.MeasureText(runText, style)
                // letter-spacing is added after every character, the last one
                // included (CSS Text 3 §5.1) - Edge measures "abcde" + 6px as 73.6.
                + letterSpacing * advances.Length;
            if (!float.IsNaN(shapedWidth) && shapedWidth > 0)
            {
                float k = shapedWidth / charSum;
                if (MathF.Abs(k - 1f) > 0.001f)
                {
                    for (int i = 0; i < advances.Length; i++) advances[i] *= k;
                }
            }
        }

        var result = new ShapeResult(start, end, _text, advances, direction == TextDirection.Rtl);
        if (start == 0 && end == _text.Length)
        {
            if (ShapeCache.Count > 100000) ShapeCache.Clear();
            ShapeCache[CacheKey(_text, style)] = result;
        }
        return result;
    }

    internal static float ShapeCharacter(char c, ComputedStyle style)
    {
        return TextMeasureProxy.MeasureCharacter(c, style);
    }
}

/// <summary>Static text-measurement entry points for shaping.</summary>
public static class TextMeasureProxy
{
    private static readonly Dictionary<(char, float, string, int), float> Cache = new();

    public static float MeasureCharacter(char c, ComputedStyle style)
    {
        var key = (c, style.FontSize, style.FontFamily, (int)style.FontWeight);
        if (Cache.TryGetValue(key, out var w)) return w;

        var measurer = TextMeasurer.Instance;
        w = measurer != null
            ? measurer.MeasureTextAdvanced(c.ToString(), style.FontFamily, style.FontSize, style.FontWeight, style.FontStyle)
            : style.FontSize * 0.5f;
        if (Cache.Count > 100000) Cache.Clear();
        Cache[key] = w;
        return w;
    }

    public static float MeasureText(string text, ComputedStyle style)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var measurer = TextMeasurer.Instance;
        return measurer != null
            ? measurer.MeasureTextAdvanced(text, style.FontFamily, style.FontSize, style.FontWeight, style.FontStyle)
            : text.Length * style.FontSize * 0.5f;
    }
}

/// <summary>Mirrors space_utils / character classification used by the line breaker.</summary>
public static class Character
{
    public const char kSpaceCharacter = ' ';
    public const char kTabulationCharacter = '\t';
    public const char kNewlineCharacter = '\n';
    public const char kCarriageReturnCharacter = '\r';
    public const char kFormFeedCharacter = '\f';
    public const char kZeroWidthSpaceCharacter = '\u200B';
    public const char kObjectReplacementCharacter = '\uFFFC';
    public const char kNoBreakSpaceCharacter = '\u00A0';
    public const char kPopDirectionalIsolateCharacter = '\u2069';
    public const char kPopDirectionalFormattingCharacter = '\u202C';
    public const char kSoftHyphenCharacter = '\u00AD';

    public static bool IsBreakableSpace(char c) => c == ' ' || c == '\t';

    public static bool IsOtherSpaceSeparator(char c)
    {
        // General_Category == Zs but not U+0020 (U+00A0 NBSP etc.).
        return c == '\u00A0' || c == '\u1680' || c == '\u2000' || c == '\u2001' || c == '\u2002' || c == '\u2003'
            || c == '\u2004' || c == '\u2005' || c == '\u2006' || c == '\u2007' || c == '\u2008' || c == '\u2009'
            || c == '\u200A' || c == '\u202F' || c == '\u205F' || c == '\u3000';
    }

    public static bool IsLineFeed(char c) => c == '\n';

    /// <summary>CJK ideographs, kana and full-width forms (UAX #14 classes ID/CJ/BA territory).</summary>
    public static bool IsCJKIdeographOrSymbol(char c) =>
        (c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xAC00 && c <= 0xD7AF)
        || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x20000 && c <= 0x2A6DF)
        || (c >= 0xFF00 && c <= 0xFF6F) || (c >= 0xFF9F && c <= 0xFFDC);

    /// <summary>Approximate line-break boundary used for greedy wrapping (CJK + punctuation).</summary>
    public static bool IsLineBreakBoundary(char c)
    {
        if (IsCJKIdeographOrSymbol(c)) return true;
        return c == '-' || c == '/' || c == '\\' || c == '.' || c == '_' || c == '@' || c == '#' || c == '?' || c == '!' || c == ';' || c == ':';
    }

    /// <summary>
    /// UAX #14 LB13/LB19/LB21: a line may not start with these. The set mirrors Chrome
    /// measurements for both CJK and Latin context: closing brackets and quotation marks,
    /// full-width and ASCII stops/separators, iteration marks, combining marks, word
    /// joiners and the hyphen/dashes that glue to what precedes them. Notably Chrome does
    /// allow a line to start with ー, 〆, 〜 or small kana (ぁ).
    /// </summary>
    public static bool IsProhibitedLineStart(char c)
    {
        switch (c)
        {
            case '\u00A0': // NBSP
            case '\u2060': // word joiner
            case '\u2011': // non-breaking hyphen
            case '\u2010': // hyphen
            case '\u2013': // en dash
            case '\u2019': case '\u201D': // closing quotes
            case '\u2044': // fraction slash
            case '\u3000': // ideographic space
            case '\u3001': case '\u3002': case '\u3005': case '\u303B': // 、 。 々 〻
            case '\u309D': case '\u309E': case '\u30FD': case '\u30FE': // ゝ ゞ ヽ ヾ
            case '\u3099': case '\u309A': case '\u309B': case '\u309C': // combining/marks
            case '\u30FB': // ・
            case '\uFF01': case '\uFF05': case '\uFF09': case '\uFF0C': // ！ ％ ） ，
            case '\uFF0E': case '\uFF1A': case '\uFF1B': case '\uFF1F': // ． ： ； ？
            case '\uFF3D': case '\uFF5D': case '\uFF60': case '\uFF61': // ］ ｝ ｠ ｡
            case '\uFF63': case '\uFF9F': // ｣ ﾟ
            case '\u0027': case '\u0022': // ' "
            case '-': case ';': case ':': case '!': case '?': case ',': case '.':
            case ')': case ']': case '}': case '/': case '%':
                return true;
        }
        if (c >= 0x300 && c <= 0x036F) return true; // combining diacritical marks
        return false;
    }

    /// <summary>
    /// UAX #14 LB16/LB21/LB25: a line may not end with these (opening brackets and
    /// quotation marks, backslash, non-breaking separators, prefixes such as « and −).
    /// </summary>
    public static bool IsProhibitedLineEnd(char c)
    {
        switch (c)
        {
            case '\u00A0': case '\u2060': case '\u2011': case '\u2044':
            case '\u00AB': case '\u00BF': // « ¿
            case '\u2018': case '\u201C': // opening quotes
            case '\u2212': // minus sign
            case '\u3008': case '\u300A': case '\u300C': case '\u300E': // 〈 《 「 『
            case '\u3010': case '\u3014': case '\uFF08': case '\uFF3B': // 【 〔 （ ［
            case '\uFF5B': case '\uFF5F': case '\uFF62': // ｛ ｟ ｢
            case '(': case '[': case '{': case '\\': case '\'': case '"':
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when a break between |previous| and |next| is allowed. Mirrors the pair rules
    /// the single-character table cannot express.
    /// </summary>
    public static bool IsBreakAllowed(char previous, char next)
    {
        if (previous == '\u200B' || next == '\u200B') return true; // ZWSP is an explicit opportunity
        if (IsProhibitedLineStart(next)) return false;
        if (IsProhibitedLineEnd(previous)) return false;
        if (previous == '\u00A0' || next == '\u00A0') return false; // nothing breaks across NBSP
        if (previous == '\u2011' || next == '\u2011') return false;
        if (previous == '\u2060' || next == '\u2060') return false;
        return true;
    }
}

public enum TextItemType
{
    kNormal,
    kForcedLineBreak,
    kFlowControl,
    kSymbolMarker,
}

public enum LineBreakType
{
    kNormal,
    kBreakAll,
    kBreakCharacter,
    kKeepAll,
    kPhrase,
}

public enum LineBreakStrictness
{
    kDefault,
    kNormal,
    kStrict,
    kLoose,
}

public enum BreakSpaceType
{
    kAfterSpaceRun,
    kAfterEverySpace,
}

/// <summary>CSS white-space handling style flags, mirroring ComputedStyle queries.</summary>
public static class WhiteSpaceStyle
{
    public static bool ShouldCollapseWhiteSpaces(ComputedStyle style) =>
        style.WhiteSpace == WhiteSpaceMode.Normal || style.WhiteSpace == WhiteSpaceMode.Nowrap || style.WhiteSpace == WhiteSpaceMode.PreLine;

    public static bool ShouldPreserveWhiteSpaces(ComputedStyle style) =>
        style.WhiteSpace is WhiteSpaceMode.Pre or WhiteSpaceMode.PreWrap or WhiteSpaceMode.BreakSpaces;

    public static bool ShouldBreakSpaces(ComputedStyle style) => style.WhiteSpace == WhiteSpaceMode.Pre;

    /// <summary>
    /// 'pre-wrap' keeps a line's trailing spaces but lets them hang past the end of
    /// the line box, while 'break-spaces' preserves them and therefore counts them
    /// against the available width. CSS Text 3 4.1.2.7.
    /// </summary>
    public static bool HangsTrailingSpaces(ComputedStyle style) =>
        style.WhiteSpace == WhiteSpaceMode.PreWrap;

    public static bool ShouldWrapLine(ComputedStyle style) => style.WhiteSpace is WhiteSpaceMode.Normal or WhiteSpaceMode.PreWrap or WhiteSpaceMode.PreLine or WhiteSpaceMode.BreakSpaces;

    public static bool ShouldBreakOnlyAfterWhiteSpace(ComputedStyle style) => style.WhiteSpace == WhiteSpaceMode.PreLine;

    public static bool ShouldPreserveNewline(ComputedStyle style) => style.WhiteSpace != WhiteSpaceMode.Normal && style.WhiteSpace != WhiteSpaceMode.Nowrap;
}

/// <summary>
/// Lazy break iterator over the inline text. Mirrors the parts of
/// LazyLineBreakIterator (platform/text/) used by LineBreaker.
/// </summary>
public sealed class LazyLineBreakIterator
{
    private readonly string _text;
    private int _startOffset;
    private string? _locale;
    private LineBreakStrictness _strictness = LineBreakStrictness.kDefault;
    private LineBreakType _breakType = LineBreakType.kNormal;
    private BreakSpaceType _breakSpace = BreakSpaceType.kAfterSpaceRun;
    private bool _softHyphenEnabled;

    public LazyLineBreakIterator(string text)
    {
        _text = text;
    }

    public LazyLineBreakIterator(LazyLineBreakIterator other, string text)
    {
        _text = text;
        _startOffset = other._startOffset;
        _locale = other._locale;
        _strictness = other._strictness;
        _breakType = other._breakType;
        _breakSpace = other._breakSpace;
        _softHyphenEnabled = other._softHyphenEnabled;
    }

    public string GetString() => _text;

    public int StartOffset => _startOffset;
    public void SetStartOffset(int offset) => _startOffset = offset;
    public void SetLocale(string? locale) => _locale = locale;
    public void SetStrictness(LineBreakStrictness strictness) => _strictness = strictness;
    public void SetBreakType(LineBreakType breakType) => _breakType = breakType;
    public LineBreakType BreakType => _breakType;
    public void SetBreakSpace(BreakSpaceType breakSpace) => _breakSpace = breakSpace;
    public void EnableSoftHyphen(bool enabled) => _softHyphenEnabled = enabled;
    public bool IsSoftHyphenEnabled => _softHyphenEnabled;

    private static bool IsBidiTrailingSpace(char c) => char.IsWhiteSpace(c);

    /// <summary>True if a line can break before offset (i.e., after character offset-1).</summary>
    public bool IsBreakable(int offset)
    {
        if (offset <= 0 || offset > _text.Length) return false;
        char c = _text[offset - 1];
        if (c == '\n') return false; // newlines are handled as forced breaks
        char next = offset < _text.Length ? _text[offset] : '\0';
        if (c == '\u200B' || next == '\u200B') return true; // ZWSP
        if (c == '\u00AD') return _softHyphenEnabled;

        // LB13/LB16/LB19: the pair decides whether this position may break at all.
        // Chrome keeps these restrictions under 'word-break: break-all' but
        // 'overflow-wrap: anywhere' breaks through them.
        if (next != '\0' && _breakType != LineBreakType.kBreakCharacter &&
            !Character.IsBreakAllowed(c, next))
            return false;

        if (IsBreakableSpaceStyle(c)) return true;

        switch (_breakType)
        {
            case LineBreakType.kBreakCharacter:
                return true;
            case LineBreakType.kKeepAll:
                // 'word-break: keep-all' only removes the opportunities CJK would
                // create; Latin rules (hyphen, slash, space) still apply, so a
                // boundary is dropped only when a CJK character touches it.
                if (Character.IsCJKIdeographOrSymbol(c) || Character.IsCJKIdeographOrSymbol(next)) return false;
                break;
        }
        return Character.IsLineBreakBoundary(c);
    }

    private bool IsBreakableSpaceStyle(char c)
    {
        if (_breakSpace == BreakSpaceType.kAfterEverySpace)
            return Character.IsBreakableSpace(c) || Character.IsOtherSpaceSeparator(c);
        return Character.IsBreakableSpace(c) || Character.IsOtherSpaceSeparator(c);
    }

    /// <summary>
    /// True when the UAX #14 pair restrictions allow a break between offset-1 and offset,
    /// independent of which characters carry an opportunity by themselves. Used by
    /// 'word-break: break-all', where any pair is breakable except these.
    /// </summary>
    public bool IsPairBreakAllowed(int offset)
    {
        if (offset <= 0 || offset >= _text.Length) return true;
        return Character.IsBreakAllowed(_text[offset - 1], _text[offset]);
    }

    /// <summary>Next break opportunity at or after |fromOffset| but before |limit|.</summary>
    public int NextBreakOpportunity(int fromOffset, int limit = int.MaxValue)
    {
        if (limit == int.MaxValue) limit = _text.Length;
        limit = Math.Min(limit, _text.Length + 1);
        for (int i = Math.Max(fromOffset, _startOffset); i < limit; i++)
        {
            if (IsBreakable(i)) return i;
        }
        return Math.Min(limit, _text.Length + 1) == Math.Min(limit, _text.Length + 1) ? limit : limit;
    }

    /// <summary>Previous break opportunity at or before |fromOffset|, not below |minOffset|.</summary>
    public int PreviousBreakOpportunity(int fromOffset, int minOffset = -1)
    {
        if (minOffset < 0) minOffset = 0;
        for (int i = Math.Min(fromOffset, _text.Length); i > minOffset; i--)
        {
            if (IsBreakable(i)) return i;
        }
        if (minOffset == 0 && IsBreakable(minOffset + 1)) return minOffset + 1;
        return minOffset;
    }
}

/// <summary>
/// Shape-result spacing (word-spacing / letter-spacing). Mirrors
/// shape_result_spacing.h. Only letter/word spacing is applied.
/// </summary>
public sealed class ShapeResultSpacing
{
    private readonly string _text;
    private readonly bool _isSvgText;
    private float _letterSpacing;
    private float _wordSpacing;

    public ShapeResultSpacing(string text, bool isSvgText)
    {
        _text = text;
        _isSvgText = isSvgText;
    }

    public bool HasSpacing => _letterSpacing != 0 || _wordSpacing != 0;
    public float LetterSpacing => _letterSpacing;
    public float WordSpacing => _wordSpacing;

    public void SetSpacing(ComputedStyle style)
    {
        _letterSpacing = style.LetterSpacing;
        _wordSpacing = style.WordSpacing;
    }
}

/// <summary>
/// Paragraph-level bidi resolution helpers (platform/text/bidi_paragraph.h).
/// Implemented with the UAX#9 run-level reversal on the item's resolved levels.
/// </summary>
public static class BidiParagraph
{
    /// <summary>Base direction for a string or LTR if no strong chars.</summary>
    public static TextDirection BaseDirectionForStringOrLtr(string text, Func<char, bool>? segmentBreak = null)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (segmentBreak != null && segmentBreak(c)) continue;
            if (IsStrongRtl(c)) return TextDirection.Rtl;
            if (IsStrongLtr(c)) return TextDirection.Ltr;
        }
        return TextDirection.Ltr;
    }

    public static bool IsStrongLtr(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

    public static bool IsStrongRtl(char c) =>
        (c >= 0x05BE && c <= 0x05EA) || (c >= 0x0620 && c <= 0x064A) || (c >= 0x0671 && c <= 0x06D3) || (c >= 0x06FA && c <= 0x06FF)
        || (c >= 0x07C0 && c <= 0x07EA) || (c >= 0x0800 && c <= 0x0815);

    /// <summary>
    /// Computes the visual order of |levels| (UAX#9 L2). |indicesInVisualOrder| is
    /// a permutation of 0..size-1. The returned mapping maps logical index → index
    /// in the visual (reordered) sequence.
    /// </summary>
    public static int[] IndicesInVisualOrder(int[] levels)
    {
        int n = levels.Length;
        var visual = new int[n];
        for (int i = 0; i < n; i++) visual[i] = i;

        int maxLevel = 0;
        foreach (int l in levels) maxLevel = Math.Max(maxLevel, l);

        // L2: from the highest level down to 1 (including intermediate levels
        // not present in the text), reverse contiguous runs at or above each
        // level. Stepping by one is required once runs carry levels above
        // base+1, e.g. LTR words at level 2 inside an RTL paragraph.
        for (int level = maxLevel; level >= 1; level--)
        {
            int i = 0;
            while (i < n)
            {
                if (levels[visual[i]] >= level)
                {
                    int j = i + 1;
                    while (j < n && levels[visual[j]] >= level) j++;
                    Reverse(visual, i, j);
                    i = j;
                }
                else
                {
                    i++;
                }
            }
        }

        // Build logical→visual mapping (position in the reordered sequence).
        var logicalToVisual = new int[n];
        for (int i = 0; i < n; i++) logicalToVisual[visual[i]] = i;
        return logicalToVisual;
    }

    private static void Reverse(int[] array, int start, int end)
    {
        for (int i = start, j = end - 1; i < j; i++, j--)
        {
            int t = array[i]; array[i] = array[j]; array[j] = t;
        }
    }
}

/// <summary>
/// Breaks a shaped line into a sub-range fitting an available width.
/// Mirrors ShapingLineBreaker (platform/fonts/shaping/shaping_line_breaker.h).
/// </summary>
public sealed class ShapingLineBreaker
{
    public sealed class Result
    {
        public int BreakOffset;
        public bool IsHyphenated;
        public bool HasTrailingSpaces;
        public bool IsOverflow;
    }

    private readonly ShapeResult _result;
    private readonly LazyLineBreakIterator _breakIterator;
    private readonly ComputedStyle _style;
    private bool _noResultIfOverflow;
    private bool _dontReshapeEndIfAtSpace;
    private int _lineStart;
    private bool _isAfterForcedBreak;

    public ShapingLineBreaker(ShapeResult result, LazyLineBreakIterator breakIterator, ComputedStyle style)
    {
        _result = result;
        _breakIterator = breakIterator;
        _style = style;
    }

    public void SetTextSpacingTrim(object textSpacingTrim) { }
    public void SetLineStart(int lineStart) => _lineStart = lineStart;
    public void SetIsAfterForcedBreak(bool value) => _isAfterForcedBreak = value;
    public void SetDontReshapeEndIfAtSpace() => _dontReshapeEndIfAtSpace = true;
    public void SetNoResultIfOverflow() => _noResultIfOverflow = true;
    public bool NoResultIfOverflow => _noResultIfOverflow;

    /// <summary>
    /// Result shape for [startOffset, breakOffset) that fits within
    /// |availableWidth|. Returns null (with overflow hint) if the first break
    /// opportunity does not fit and NoResultIfOverflow is set.
    /// </summary>
    public ShapeResult? ShapeLine(int startOffset, float availableWidth, out Result result)
    {
        result = new Result();
        int start = Math.Max(startOffset, _result.StartIndex);
        int end = _result.EndIndex;

        if (start >= end)
        {
            result.BreakOffset = end;
            return ShapeResultView.Create(_result, start, end);
        }

        // Accumulate width until the available width is exceeded.
        float width = 0;
        int breakAt = start;
        bool foundSpaceFit = false;
        for (int i = start; i < end; i++)
        {
            float w = _result.Advances[i - _result.StartIndex];
            if (width + w > availableWidth && width > 0)
            {
                breakAt = i;
                foundSpaceFit = true;
                break;
            }
            width += w;
            breakAt = i + 1;
        }

        LineBreakType breakType = _breakIterator.BreakType;
        bool breakCharacter = breakType == LineBreakType.kBreakCharacter || breakType == LineBreakType.kBreakAll;

        // If everything fits, break at the end (or earlier break opportunity for
        // normal breaks so trailing items can attach).
        if (breakAt >= end)
        {
            result.BreakOffset = end;
            result.IsOverflow = false;
            result.HasTrailingSpaces = end > start && (Character.IsBreakableSpace(_result.Text[end - 1]));
            return ShapeResultView.Create(_result, start, end);
        }

        if (foundSpaceFit && !breakCharacter)
        {
            // 'pre-wrap' lets a run of spaces that starts at the point of overflow
            // hang past the end of the line box, so the word in front of them stays
            // on this line. 'break-spaces' preserves those spaces instead and must
            // wrap them (CSS Text 3 4.1.2.7).
            if (breakAt < end && WhiteSpaceStyle.HangsTrailingSpaces(_style) &&
                Character.IsBreakableSpace(_result.Text[breakAt]))
            {
                int afterSpaces = breakAt;
                while (afterSpaces < end && Character.IsBreakableSpace(_result.Text[afterSpaces]))
                    afterSpaces++;
                result.BreakOffset = afterSpaces;
                result.IsOverflow = false;
                result.HasTrailingSpaces = true;
                return ShapeResultView.Create(_result, start, afterSpaces);
            }

            // Find the last safe-to-break at or before breakAt. The shaped run only
            // knows the character classes, so 'word-break: keep-all' and the pair rules
            // (LB13/LB16/LB19) have to be resolved through the break iterator.
            int safe = breakType == LineBreakType.kKeepAll
                ? _breakIterator.PreviousBreakOpportunity(breakAt, start)
                : _result.PreviousSafeToBreakOffset(breakAt);
            while (safe > start && !_breakIterator.IsBreakable(safe))
            {
                safe = breakType == LineBreakType.kKeepAll
                    ? _breakIterator.PreviousBreakOpportunity(safe - 1, start)
                    : _result.PreviousSafeToBreakOffset(safe - 1);
            }
            if (safe > start)
            {
                result.BreakOffset = safe;
                result.IsOverflow = false;
                result.HasTrailingSpaces = _result.Text[safe - 1] == ' ' && safe > start;
                return ShapeResultView.Create(_result, start, safe);
            }
            // First opportunity doesn't fit.
            if (_noResultIfOverflow)
            {
                result.IsOverflow = true;
                return null;
            }
            // We can't fit any break, so break character-wise.
            bool perChar = false;
            int perCharEnd = start + 1;
            if (perChar)
            {
                result.BreakOffset = perCharEnd;
                return ShapeResultView.Create(_result, start, perCharEnd);
            }
            // Continue to the first break opportunity after overflow (the item
            // result will be rewound by HandleOverflow).
            int next = _breakIterator.NextBreakOpportunity(start + 1, end + 1);
            result.BreakOffset = Math.Min(next, end);
            result.IsOverflow = true;
            return ShapeResultView.Create(_result, start, Math.Min(next, end));
        }

        if (breakCharacter)
        {
            int at = Math.Max(start + 1, breakAt);
            // 'word-break: break-all' splits between any two characters but Chrome still
            // refuses a line that starts with (or ends with) restricted punctuation, so
            // slide forward to the first allowed position.
            if (breakType == LineBreakType.kBreakAll)
            {
                while (at < end && !_breakIterator.IsPairBreakAllowed(at)) at++;
            }
            result.BreakOffset = at;
            result.IsOverflow = false;
            return ShapeResultView.Create(_result, start, at);
        }

        result.BreakOffset = breakAt;
        result.IsOverflow = true;
        return ShapeResultView.Create(_result, start, breakAt);
    }

    /// <summary>Shape an explicit range (used by BreakTextAt).</summary>
    public ShapeResult? ShapeLineAt(int startOffset, int endOffset)
    {
        return ShapeResultView.Create(_result, startOffset, endOffset);
    }
}

/// <summary>Hyphenation hook. Mirrors Hyphenation; auto-hyphenation is not available.</summary>
public sealed class Hyphenation
{
    public static readonly Hyphenation None = new();

    public float MinWordLength => 3;

    /// <summary>List of hyphenation points in descending order.</summary>
    public List<int> HyphenLocations(ReadOnlySpan<char> word)
    {
        var locations = new List<int>();
        if (word.Length >= 8)
        {
            // Coarse fallback: hyphenate long compound words at existing hyphens.
            for (int i = word.Length - 1; i > 2; i--)
            {
                if (word[i] == '-') locations.Add(i);
            }
        }
        return locations;
    }

    public int FirstHyphenLocation(ReadOnlySpan<char> word, int beforeIndex)
    {
        var list = HyphenLocations(word);
        return list.Count > 0 ? list[^1] : 0;
    }
}

/// <summary>A cached hyphen string and its shape.</summary>
public sealed class HyphenResult
{
    public string Text { get; }
    public ShapeResult ShapeResult { get; }

    public HyphenResult()
    {
        Text = "-";
        var text = Text;
        ShapeResult = new ShapeResult(0, text.Length, text,
            new[] { TextShapeFallback('-') }, false);
    }

    public HyphenResult(ComputedStyle style) : this()
    {
    }

    private static float TextShapeFallback(char c) => c == '-' ? 0.5f : 16;
    public float InlineSize() => ShapeResult.SnappedWidth();
    public bool IsValid => true;

    public static implicit operator bool(HyphenResult? result) => result != null;
    public static explicit operator bool?(HyphenResult? result) => result != null;
}

/// <summary>
/// Convert a ComputedStyle into the FontHeightMetrics used by inline box
/// metrics. Values come from the resolved font metrics of the style's primary
/// font, so the ascent/descent are the font's own rounded values rather than a
/// fraction of the font size.
/// </summary>
public static class FontHelper
{
    private static readonly Dictionary<(float, string, int, int, float), FontHeightMetrics> Cache = new();

    public static FontHeightMetrics GetFontMetrics(ComputedStyle style)
    {
        // The strut depends on the device pixel grid, so the scale is part of the key.
        var key = (style.FontSize, style.FontFamily, (int)style.FontWeight, (int)style.FontStyle,
            Fonts.FontMetricsProvider.DeviceScale);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var metrics = Fonts.LineBoxMetrics.GetFontMetrics(style);
        cached = new FontHeightMetrics(metrics.LayoutAscent, metrics.LayoutDescent, metrics.CapHeight);

        if (Cache.Count > 10000) Cache.Clear();
        Cache[key] = cached;
        return cached;
    }

    /// <summary>Line box strut for the style, including the half-leading.</summary>
    public static FontHeightMetrics GetFont(ComputedStyle style)
    {
        var strut = Fonts.LineBoxMetrics.GetStrut(style);
        return new FontHeightMetrics(strut.Ascent, strut.Descent,
            Fonts.LineBoxMetrics.GetFontMetrics(style).CapHeight);
    }

    /// <summary>Clear the per-style metrics cache (used when fonts are reloaded).</summary>
    public static void ClearCache() => Cache.Clear();
}