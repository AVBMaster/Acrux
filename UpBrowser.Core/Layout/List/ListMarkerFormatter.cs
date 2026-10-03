using System.Globalization;
using System.Text;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout.List;

/// <summary>
/// Marker text for the counter styles of CSS Lists 3 (§5 symbols, §6.2 numeric,
/// §6.3 alphabetic, §6.4 additive, §6.5 fixed and §7 complex styles). Engines share one
/// table so layout (marker width) and painting (marker glyphs) agree.
/// </summary>
public static class ListMarkerFormatter
{
    /// <summary>Complete marker label, separator included. An ordinal outside a style's
    /// range renders with that style's fallback while keeping the style's own separator
    /// (CSS Lists 3 §2.2 fallback description).</summary>
    public static string MarkerLabel(ListStyleType type, int ordinal, string? customString)
    {
        if (customString != null)
            return customString;

        switch (type)
        {
            case ListStyleType.None:
                return string.Empty;
            case ListStyleType.String:
                // A quoted &lt;string&gt; type has no counter and no separator (§11).
                return string.Empty;
            case ListStyleType.Disc:
                return "\u2022";
            case ListStyleType.Circle:
                return "\u25E6";
            case ListStyleType.Square:
                return "\u25A0";
            case ListStyleType.DisclosureOpen:
                return "\u25BE";
            case ListStyleType.DisclosureClosed:
                return "\u25B8";

            case ListStyleType.Decimal:
                return Decimal(ordinal) + ".";
            case ListStyleType.DecimalLeadingZero:
                return DecimalLeadingZero(ordinal);

            case ListStyleType.LowerRoman:
                return Roman(ordinal, upper: false);
            case ListStyleType.UpperRoman:
                return Roman(ordinal, upper: true);

            case ListStyleType.LowerLatin:
            case ListStyleType.LowerAlpha:
                return Alphabetic(ordinal, "abcdefghijklmnopqrstuvwxyz", ".");
            case ListStyleType.UpperLatin:
            case ListStyleType.UpperAlpha:
                return Alphabetic(ordinal, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", ".");
            case ListStyleType.LowerGreek:
                return Alphabetic(ordinal, "αβγδεζηθικλμνξοπρστυφχψω", ".");
            case ListStyleType.UpperGreek:
                return Alphabetic(ordinal, "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩ", ".");
            case ListStyleType.Hiragana:
                return Alphabetic(ordinal, "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわゐゑをん", "\u3001");
            case ListStyleType.Katakana:
                return Alphabetic(ordinal, "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヰヱヲン", "\u3001");
            case ListStyleType.HiraganaIroha:
                return Alphabetic(ordinal, "いろはにほへとちりぬるをわかよたれそつねならむうゐのおくやまけふこえてあさきゆめみしゑひもせす", "\u3001");
            case ListStyleType.KatakanaIroha:
                return Alphabetic(ordinal, "イロハニホヘトチリヌルヲワカヨタレソツネナラムウヰノオクヤマケフコエテアサキユメミシヱヒモセス", "\u3001");
            case ListStyleType.Symbol:
                return Alphabetic(ordinal, "☐☑☒☓☔☕☖☗☘☙", string.Empty);

            case ListStyleType.Armenian:
            case ListStyleType.UpperArmenian:
                return AdditiveLabel(ordinal, ArmenianUpperUnits, min: 1, max: 9999, separator: ".");
            case ListStyleType.LowerArmenian:
                return AdditiveLabel(ordinal, ArmenianLowerUnits, min: 1, max: 9999, separator: ".");
            case ListStyleType.Georgian:
                return AdditiveLabel(ordinal, GeorgianUnits, min: 1, max: 19999, separator: ".");
            case ListStyleType.Hebrew:
                return HebrewLabel(ordinal);
            case ListStyleType.EthiopicNumeric:
                return EthiopicLabel(ordinal);

            case ListStyleType.CjkEarthlyBranch:
                return Fixed(ordinal, "子丑寅卯辰巳午未申酉戌亥", "\u3001", CjkDecimalLabel);
            case ListStyleType.CjkHeavenlyStem:
                return Fixed(ordinal, "甲乙丙丁戊己庚辛壬癸", "\u3001", CjkDecimalLabel);
            case ListStyleType.CjkDecimal:
                return CjkDecimalLabel(ordinal);
            case ListStyleType.CjkIdeographic:
            case ListStyleType.TradChineseInformal:
                return LonghandLabel(ordinal, TradChineseInformalTable, informal: true, negative: "負", separator: "\u3001");
            case ListStyleType.TradChineseFormal:
                return LonghandLabel(ordinal, TradChineseFormalTable, informal: false, negative: "負", separator: "\u3001");
            case ListStyleType.SimpChineseInformal:
                return LonghandLabel(ordinal, SimpChineseInformalTable, informal: true, negative: "负", separator: "\u3001");
            case ListStyleType.SimpChineseFormal:
                return LonghandLabel(ordinal, SimpChineseFormalTable, informal: false, negative: "负", separator: "\u3001");
            case ListStyleType.JapaneseInformal:
                return AdditiveLabel(ordinal, JapaneseInformalUnits, min: -9999, max: 9999,
                    separator: "\u3001", negative: JapaneseNegative, fallback: CjkDecimalLabel);
            case ListStyleType.JapaneseFormal:
                return AdditiveLabel(ordinal, JapaneseFormalUnits, min: -9999, max: 9999,
                    separator: "\u3001", negative: JapaneseNegative, fallback: CjkDecimalLabel);
            case ListStyleType.KoreanHangulFormal:
                return LonghandLabel(ordinal, KoreanHangulFormalTable, informal: false, negative: KoreanNegative, separator: ",");
            case ListStyleType.KoreanHanjaInformal:
                return LonghandLabel(ordinal, KoreanHanjaInformalTable, informal: true, negative: KoreanNegative, separator: ",");
            case ListStyleType.KoreanHanjaFormal:
                return LonghandLabel(ordinal, KoreanHanjaFormalTable, informal: false, negative: KoreanNegative, separator: ",");

            case ListStyleType.Thai:
                return Numeric(ordinal, "๐๑๒๓๔๕๖๗๘๙", ".");
            case ListStyleType.Lao:
                return Numeric(ordinal, "໐໑໒໓໔໕໖໗໘໙", ".");
            case ListStyleType.Khmer:
                return Numeric(ordinal, "០១២៣៤៥៦៧៨៩", ".");
            case ListStyleType.Myanmar:
                return Numeric(ordinal, "၀၁၂၃၄၅၆၇၈၉", ".");
            case ListStyleType.Mongolian:
                return Numeric(ordinal, "᠐᠑᠒᠓᠔᠕᠖᠗᠘᠙", ".");
            case ListStyleType.Gujarati:
                return Numeric(ordinal, "૦૧૨૩૪૫૬૭૮૯", ".");
            case ListStyleType.Gurmukhi:
                return Numeric(ordinal, "੦੧੨੩੪੫੬੭੮੯", ".");
            case ListStyleType.Kannada:
                return Numeric(ordinal, "೦೧೨೩೪೫೬೭೮೯", ".");
            case ListStyleType.Malayalam:
                return Numeric(ordinal, "൦൧൨൩൪൫൬൭൮൯", ".");
            case ListStyleType.Oriya:
                return Numeric(ordinal, "୦୧୨୩୪୫୬୭୮୯", ".");
            case ListStyleType.Tibetan:
                return Numeric(ordinal, "༠༡༢༣༤༥༦༧༨༩", ".");
            case ListStyleType.ArabicIndic:
                return Numeric(ordinal, "٠١٢٣٤٥٦٧٨٩", ".");
            case ListStyleType.Persian:
                return Numeric(ordinal, "۰۱۲۳۴۵۶۷۸۹", ".");
            case ListStyleType.Devanagari:
                return Numeric(ordinal, "०१२३४५६७८९", ".");
            case ListStyleType.Bengali:
                return Numeric(ordinal, "০১২৩৪৫৬৭৮৯", ".");
            case ListStyleType.Tamil:
                return Numeric(ordinal, "௦௧௨௩௪௫௬௭௮௯", ".");
            case ListStyleType.Telugu:
                return Numeric(ordinal, "౦౧౨౩౪౫౬౭౮౯", ".");
            case ListStyleType.CanadianAboriginal:
                return Numeric(ordinal, "ᐳᐴᐵᐶᐷᐸᐹᐺᐻᐼ", ".");

            default:
                // Unknown keyword: invalid at computed-value time, so the style
                // falls back to its initial value, 'disc'.
                return "\u2022";
        }
    }

    /// <summary>True when the style paints a fixed glyph instead of a counter.</summary>
    public static bool IsSymbolic(ListStyleType type) => type is ListStyleType.Disc or ListStyleType.Circle
        or ListStyleType.Square or ListStyleType.DisclosureOpen or ListStyleType.DisclosureClosed
        or ListStyleType.None;

    private static string Decimal(int ordinal) =>
        ordinal.ToString(CultureInfo.InvariantCulture);

    /// <summary>'decimal-leading-zero' is 'decimal' with pad: 2 '0' (§6.1). The pad counts
    /// grapheme clusters of the representation, and the minus sign is already one of them,
    /// so a single negative digit is not padded — measured against Edge: -1 gives "-1.".</summary>
    private static string DecimalLeadingZero(int ordinal)
    {
        if (ordinal < 0) return Decimal(ordinal) + ".";
        return ordinal.ToString("D2", CultureInfo.InvariantCulture) + ".";
    }

    private static bool Within(int ordinal, int min, int max) => ordinal >= min && ordinal <= max;

    /// <summary>The absolute value as a 32-bit-magnitude counter value; engines clamp
    /// the ordinal of a list item to the integer range, so int.MinValue is the top step.</summary>
    private static uint Magnitude(int ordinal) =>
        ordinal == int.MinValue ? 2147483648u : (uint)Math.Abs(ordinal);

    private static string Roman(int ordinal, bool upper)
    {
        if (!Within(ordinal, 1, 3999)) return Decimal(ordinal) + ".";
        string text = ToRoman(ordinal);
        return (upper ? text.ToUpperInvariant() : text) + ".";
    }

    private static string Alphabetic(int ordinal, string symbols, string separator)
    {
        // Alphabetic systems have no zero and no negative values (§6.3).
        if (ordinal < 1) return Decimal(ordinal) + separator;
        return ToAlphabetic(ordinal, symbols) + separator;
    }

    private static string Numeric(int ordinal, string digits, string separator) =>
        ToNumeric(ordinal, digits) + separator;

    /// <summary>Fixed system (§6.5): one symbol per ordinal inside the symbol list, the
    /// style's own fallback outside it.</summary>
    private static string Fixed(int ordinal, string symbols, string separator, Func<int, string> fallback) =>
        Within(ordinal, 1, symbols.Length) ? symbols[ordinal - 1] + separator : fallback(ordinal);

    /// <summary>Additive system (§6.4) with the style's range. A value the table cannot
    /// decompose exactly, or one outside the range, takes the style's own fallback while
    /// keeping the style's separator.</summary>
    private static string AdditiveLabel(int ordinal, (int Value, string Symbols)[] units, int min, int max,
        string separator, string? negative = null, Func<int, string>? fallback = null)
    {
        if (!Within(ordinal, min, max))
            return fallback != null ? fallback(ordinal) : Decimal(ordinal) + separator;
        string text = ToAdditive(Magnitude(ordinal), units);
        if (text.Length == 0)
            return fallback != null ? fallback(ordinal) : Decimal(ordinal) + separator;
        string sign = ordinal < 0 ? negative ?? "-" : string.Empty;
        return sign + text + separator;
    }

    /// <summary>'cjk-decimal' (§6.2): numeric, so it is purely positional and does carry
    /// a zero glyph. Negative values are outside its range and fall back to decimal.</summary>
    private static string CjkDecimalLabel(int ordinal) =>
        ordinal >= 0 ? ToNumeric(ordinal, "〇一二三四五六七八九") + "\u3001" : Decimal(ordinal) + "\u3001";

    /// <summary>Alphabetic (base-N without a zero) system, CSS Lists 3 §6.3.</summary>
    public static string ToAlphabetic(int value, string symbols)
    {
        int len = symbols.Length;
        if (len == 0) return Decimal(value);
        var chars = new List<char>();
        while (value > 0)
        {
            value--;
            chars.Add(symbols[value % len]);
            value /= len;
        }
        chars.Reverse();
        return new string(chars.ToArray());
    }

    /// <summary>Decimal digits rendered with the style's own glyph run (§6.2).
    /// Numeric systems do carry a sign, so negatives keep the '-'.</summary>
    public static string ToNumeric(int value, string digits)
    {
        if (digits.Length != 10) return Decimal(value);
        string text = Decimal(value);
        var buffer = new char[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            buffer[i] = char.IsDigit(c) ? digits[c - '0'] : c;
        }
        return new string(buffer);
    }

    public static string ToRoman(int value)
    {
        var sb = new StringBuilder();
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var numerals = new[] { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
        for (int i = 0; i < values.Length; i++)
        {
            while (value >= values[i])
            {
                sb.Append(numerals[i]);
                value -= values[i];
            }
        }
        return sb.ToString();
    }

    /// <summary>Additive systems are greedy and need an exact decomposition; the table
    /// is ordered from the largest weight down (§6.4).</summary>
    private static string ToAdditive(uint value, (int Value, string Symbols)[] units)
    {
        if (value == 0)
        {
            // Only tables that carry a symbol for zero can represent it.
            foreach (var unit in units)
                if (unit.Value == 0)
                    return unit.Symbols;
            return string.Empty;
        }

        var text = new StringBuilder();
        uint remaining = value;
        foreach (var unit in units)
        {
            if (unit.Value == 0) continue;
            while (remaining >= (uint)unit.Value)
            {
                text.Append(unit.Symbols);
                remaining -= (uint)unit.Value;
            }
            if (remaining == 0) break;
        }
        return remaining == 0 ? text.ToString() : string.Empty;
    }

    /// <summary>Negative signs of the styles that spell them out (§7.1). The Korean one
    /// keeps its trailing space, which is part of the descriptor value.</summary>
    private const string JapaneseNegative = "\u30DE\u30A4\u30CA\u30B9";

    private const string KoreanNegative = "\uB9C8\uC774\uB108\uC2A4 ";

    private static readonly (int Value, string Symbols)[] GeorgianUnits =
    {
        (10000, "ჵ"), (9000, "ჰ"), (8000, "ჯ"), (7000, "ჴ"), (6000, "ხ"), (5000, "ჭ"),
        (4000, "წ"), (3000, "ძ"), (2000, "ც"), (1000, "ჩ"), (900, "შ"), (800, "ყ"),
        (700, "ღ"), (600, "ქ"), (500, "ფ"), (400, "ჳ"), (300, "ტ"), (200, "ს"), (100, "რ"),
        (90, "ჟ"), (80, "პ"), (70, "ო"), (60, "ჲ"), (50, "ნ"), (40, "მ"), (30, "ლ"),
        (20, "კ"), (10, "ი"), (9, "თ"), (8, "ჱ"), (7, "ზ"), (6, "ვ"), (5, "ე"), (4, "დ"),
        (3, "გ"), (2, "ბ"), (1, "ა"),
    };

    /// <summary>Japanese informal/formal (§7.1.1): additive over ±9999, and outside that
    /// range the style falls back to cjk-decimal rather than to decimal digits.</summary>
    private static readonly (int Value, string Symbols)[] JapaneseInformalUnits =
    {
        (9000, "九千"), (8000, "八千"), (7000, "七千"), (6000, "六千"), (5000, "五千"), (4000, "四千"),
        (3000, "三千"), (2000, "二千"), (1000, "千"), (900, "九百"), (800, "八百"), (700, "七百"),
        (600, "六百"), (500, "五百"), (400, "四百"), (300, "三百"), (200, "二百"), (100, "百"),
        (90, "九十"), (80, "八十"), (70, "七十"), (60, "六十"), (50, "五十"), (40, "四十"),
        (30, "三十"), (20, "二十"), (10, "十"), (9, "九"), (8, "八"), (7, "七"), (6, "六"),
        (5, "五"), (4, "四"), (3, "三"), (2, "二"), (1, "一"), (0, "〇"),
    };

    private static readonly (int Value, string Symbols)[] JapaneseFormalUnits =
    {
        (9000, "九阡"), (8000, "八阡"), (7000, "七阡"), (6000, "六阡"), (5000, "伍阡"), (4000, "四阡"),
        (3000, "参阡"), (2000, "弐阡"), (1000, "壱阡"), (900, "九百"), (800, "八百"), (700, "七百"),
        (600, "六百"), (500, "伍百"), (400, "四百"), (300, "参百"), (200, "弐百"), (100, "壱百"),
        (90, "九拾"), (80, "八拾"), (70, "七拾"), (60, "六拾"), (50, "伍拾"), (40, "四拾"),
        (30, "参拾"), (20, "弐拾"), (10, "壱拾"), (9, "九"), (8, "八"), (7, "七"), (6, "六"),
        (5, "伍"), (4, "四"), (3, "参"), (2, "弐"), (1, "壱"), (0, "零"),
    };

    /// <summary>Hebrew numbering (§7.1.1 defines it up to 10999, engines may extend it).
    /// Every thousands block is written as the letters of its count followed by geresh,
    /// which reproduces the spec table's 1000..10000 tuples exactly, so the same
    /// construction can carry on above 10999 instead of dropping to decimal.</summary>
    private static string HebrewLabel(int ordinal)
    {
        const string separator = ".";
        const char Geresh = '׳';
        if (!Within(ordinal, 1, 999999)) return Decimal(ordinal) + separator;
        uint value = (uint)ordinal;
        string text = ToAdditive(value % 1000, HebrewUnits);
        if (value >= 1000)
            text = ToAdditive(value / 1000, HebrewUnits) + Geresh + text;
        return text + separator;
    }

    /// <summary>Hebrew numbering (§7.1.1 table): the thousands are the leading letters
    /// with a geresh, and 19..15 are spelled out so that 15 and 16 do not write the
    /// Tetragrammaton. Greedy over that table, so 900 is two 400s plus 100.</summary>
    private static readonly (int Value, string Symbols)[] HebrewUnits =
    {
        (10000, "\u05D9\u05F3"), (9000, "\u05D8\u05F3"), (8000, "\u05D7\u05F3"), (7000, "\u05D6\u05F3"),
        (6000, "\u05D5\u05F3"), (5000, "\u05D4\u05F3"), (4000, "\u05D3\u05F3"), (3000, "\u05D2\u05F3"),
        (2000, "\u05D1\u05F3"), (1000, "\u05D0\u05F3"),
        (400, "\u05EA"), (300, "\u05E9"), (200, "\u05E8"), (100, "\u05E7"), (90, "\u05E6"),
        (80, "\u05E4"), (70, "\u05E2"), (60, "\u05E1"), (50, "\u05E0"), (40, "\u05DE"), (30, "\u05DC"),
        (20, "\u05DB"),
        (19, "\u05D9\u05D8"), (18, "\u05D9\u05D7"), (17, "\u05D9\u05D6"), (16, "\u05D8\u05D6"),
        (15, "\u05D8\u05D5"),
        (10, "\u05D9"), (9, "\u05D8"), (8, "\u05D7"), (7, "\u05D6"), (6, "\u05D5"), (5, "\u05D4"),
        (4, "\u05D3"), (3, "\u05D2"), (2, "\u05D1"), (1, "\u05D0"),
    };

    /// <summary>Armenian numbering (§7.1.1): 36 letters for 1..9, 10..90, 100..900 and
    /// 1000..9000. Both cases are contiguous in Unicode, so the table is generated from
    /// the first letter instead of transcribing 36 escapes.</summary>
    private static (int Value, string Symbols)[] ArmenianUnits(char first)
    {
        var units = new (int Value, string Symbols)[36];
        int[] place = [1, 10, 100, 1000];
        for (int group = 0; group < place.Length; group++)
        {
            for (int digit = 1; digit <= 9; digit++)
            {
                int index = group * 9 + digit - 1;
                units[index] = (digit * place[group], ((char)(first + index)).ToString());
            }
        }
        Array.Reverse(units);
        return units;
    }

    private static readonly (int Value, string Symbols)[] ArmenianUpperUnits = ArmenianUnits('\u0531');
    private static readonly (int Value, string Symbols)[] ArmenianLowerUnits = ArmenianUnits('\u0561');

    /// <summary>Ethiopic numeric (§7.4): two decimal digits per group, the groups
    /// alternating between the hundred and the ten-thousand separator.</summary>
    private static string EthiopicLabel(int ordinal)
    {
        const string separator = "/";
        if (!Within(ordinal, 1, int.MaxValue)) return Decimal(ordinal) + separator;
        uint value = Magnitude(ordinal);
        if (value < 10) return EthiopicUnits[(int)value - 1] + separator;

        var text = new List<char>();
        bool oddGroup = false;
        while (value != 0)
        {
            uint group = value % 100;
            value /= 100;
            // Group zero gets the ten-thousand marker up front and drops it again at
            // the end, so every lower pair is separated from the next one.
            if (!oddGroup) text.Add('\u137C');
            else if (group != 0) text.Add('\u137B');

            bool mostSignificant = value == 0;
            bool skipDigits = group == 0 || (group == 1 && mostSignificant) || (group == 1 && oddGroup);
            if (!skipDigits)
            {
                if (group % 10 != 0) text.Add(EthiopicUnits[group % 10 - 1]);
                if (group / 10 != 0) text.Add(EthiopicTens[group / 10 - 1]);
            }
            oddGroup = !oddGroup;
        }
        text.Reverse();
        text.RemoveAt(text.Count - 1);
        return new string(text.ToArray()) + separator;
    }

    private static readonly char[] EthiopicUnits =
        { '\u1369', '\u136A', '\u136B', '\u136C', '\u136D', '\u136E', '\u136F', '\u1370', '\u1371' };

    private static readonly char[] EthiopicTens =
        { '\u1372', '\u1373', '\u1374', '\u1375', '\u1376', '\u1377', '\u1378', '\u1379', '\u137A' };

    private const int LonghandLangChinese = 1;
    private const int LonghandLangKorean = 2;
    private const int LonghandZeroIndex = 10;
    private const int LonghandGroupLength = 9;

    /// <summary>Longhand East Asian numbering (§7.1): the value is split into groups of
    /// four decimal digits, each digit gets its order marker, trailing zeros are dropped
    /// and runs of zeros collapse into one. The informal styles write 10..19 without the
    /// tens digit; the Korean ones separate each group marker with a space.</summary>
    private static string LonghandLabel(int ordinal, char[] table, bool informal, string negative, string separator)
    {
        string sign = ordinal < 0 ? negative : string.Empty;
        return sign + Longhand(Magnitude(ordinal), table, informal) + separator;
    }

    private static string Longhand(uint number, char[] table, bool informal)
    {
        bool chinese = table[0] == LonghandLangChinese;
        bool korean = table[0] == LonghandLangKorean;
        if (number == 0)
            return table[LonghandZeroIndex].ToString();

        // Slots per group: digit, marker, digit, marker, digit, marker, digit, marker, marker.
        // The abstract indexes are 1..6 for the three two-codepoint group markers,
        // 7..9 for the digit markers and 10..19 for the digits themselves.
        var buffer = new int[4 * LonghandGroupLength];
        for (int i = 0; i < 4; i++)
        {
            uint group = number % 10000;
            number /= 10000;
            int o = (3 - i) * LonghandGroupLength;

            if (group != 0 && i != 0)
            {
                buffer[o + 8] = 1 + i;
                buffer[o + 7] = i;
            }

            uint thousands = group / 1000;
            uint hundreds = group / 100 % 10;
            uint tens = group / 10 % 10;
            uint ones = group % 10;
            // The Korean informal styles omit the 1 in front of *every* marker — digit
            // marker or group marker — so 1111 reads 千百十一 and 100000 reads 十萬.
            bool dropOneBeforeMarker = informal && korean;
            bool groupMarked = group != 0 && i != 0;

            bool trailingZero = chinese && ones == 0;
            if (ones != 0 && !(dropOneBeforeMarker && ones == 1 && groupMarked))
                buffer[o + 6] = LonghandZeroIndex + (int)ones;

            if (number != 0 || group > 9)
            {
                if (tens != 0)
                {
                    if (!(dropOneBeforeMarker && tens == 1))
                        buffer[o + 4] = LonghandZeroIndex + (int)tens;
                    buffer[o + 5] = 7;
                }
                else if (chinese && !trailingZero)
                    buffer[o + 4] = LonghandZeroIndex;
                trailingZero &= tens == 0;
            }
            if (number != 0 || group > 99)
            {
                if (hundreds != 0)
                {
                    if (!(dropOneBeforeMarker && hundreds == 1))
                        buffer[o + 2] = LonghandZeroIndex + (int)hundreds;
                    buffer[o + 3] = 8;
                }
                else if (chinese && !trailingZero)
                    buffer[o + 2] = LonghandZeroIndex;
                trailingZero &= hundreds == 0;
            }
            if (number != 0 || group > 999)
            {
                if (thousands != 0)
                {
                    if (!(dropOneBeforeMarker && thousands == 1))
                        buffer[o] = LonghandZeroIndex + (int)thousands;
                    buffer[o + 1] = 9;
                }
                else if (chinese && !trailingZero)
                    buffer[o] = LonghandZeroIndex;
                trailingZero &= thousands == 0;
            }

            // A group that ends in zeros puts the zero glyph after its group marker
            // instead of dropping it, so the next lower group can collapse into it.
            if (trailingZero && i > 0)
            {
                buffer[o + 6] = buffer[o + 7];
                buffer[o + 7] = buffer[o + 8];
                buffer[o + 8] = LonghandZeroIndex;
            }

            if (chinese && informal && group < 20)
                buffer[o + 4] = 0;

            if (number == 0) break;
        }

        var text = new StringBuilder();
        int last = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            int slot = buffer[i];
            if (slot == 0) continue;
            // Runs of the zero glyph collapse into one; the Korean tables have no zero at all.
            if (slot != LonghandZeroIndex || (chinese && last != LonghandZeroIndex))
            {
                char glyph = table[slot];
                if (glyph != 0)
                {
                    text.Append(glyph);
                    if (korean && (slot == 1 || slot == 3 || slot == 5))
                        text.Append(' ');
                }
            }
            last = slot;
        }
        if ((chinese && last == LonghandZeroIndex) || (text.Length > 0 && text[^1] == ' '))
            text.Length--;
        return text.ToString();
    }

    /// <summary>Table layout: language, three two-codepoint group markers, the three
    /// digit markers (ten, hundred, thousand), then the ten digit glyphs.</summary>
    private static char[] LonghandTable(int lang, string[] groupMarkers, string tens, string hundreds,
        string thousands, string digits)
    {
        var table = new char[21];
        table[0] = (char)lang;
        for (int i = 0; i < groupMarkers.Length; i++)
        {
            string marker = groupMarkers[i];
            table[1 + (i * 2)] = marker.Length > 0 ? marker[0] : '\0';
            table[2 + (i * 2)] = marker.Length > 1 ? marker[1] : '\0';
        }
        table[7] = tens[0];
        table[8] = hundreds[0];
        table[9] = thousands[0];
        for (int i = 0; i < 10; i++)
            table[LonghandZeroIndex + i] = digits[i];
        return table;
    }

    private static readonly char[] TradChineseInformalTable = LonghandTable(LonghandLangChinese,
        ["萬", "億", "兆"], "十", "百", "千", "零一二三四五六七八九");

    private static readonly char[] TradChineseFormalTable = LonghandTable(LonghandLangChinese,
        ["萬", "億", "兆"], "拾", "佰", "仟", "零壹貳參肆伍陸柒捌玖");

    private static readonly char[] SimpChineseInformalTable = LonghandTable(LonghandLangChinese,
        ["万", "亿", "万亿"], "十", "百", "千", "零一二三四五六七八九");

    private static readonly char[] SimpChineseFormalTable = LonghandTable(LonghandLangChinese,
        ["万", "亿", "万亿"], "拾", "佰", "仟", "零壹贰叁肆伍陆柒捌玖");

    private static readonly char[] KoreanHangulFormalTable = LonghandTable(LonghandLangKorean,
        ["만", "억", "조"], "십", "백", "천", "영일이삼사오육칠팔구");

    private static readonly char[] KoreanHanjaInformalTable = LonghandTable(LonghandLangKorean,
        ["萬", "億", "兆"], "十", "百", "千", "零一二三四五六七八九");

    private static readonly char[] KoreanHanjaFormalTable = LonghandTable(LonghandLangKorean,
        ["萬", "億", "兆"], "拾", "百", "仟", "零壹貳參四五六七八九");
}
