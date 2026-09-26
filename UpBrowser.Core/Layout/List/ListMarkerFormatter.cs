using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout.List;

/// <summary>
/// Marker text for the counter styles of CSS Lists 3 (§5 symbols, §6.2 numeric,
/// §6.3 alphabetic and §6.4 additive systems). Engines share one table so layout
/// (marker width) and painting (marker glyphs) agree.
/// </summary>
public static class ListMarkerFormatter
{
    /// <summary>Formatted marker label without the trailing separator, e.g. "iv"
    /// or "ᔨ". Returns null when the style needs a symbol rather than a counter.</summary>
    public static string? Format(ListStyleType type, int oneBasedIndex, string? customString)
    {
        if (customString != null)
            return customString;

        int index = Math.Max(1, oneBasedIndex);
        switch (type)
        {
            case ListStyleType.LowerLatin:
                return Alphabetic(index, "abcdefghijklmnopqrstuvwxyz") + ".";
            case ListStyleType.UpperLatin:
                return Alphabetic(index, "ABCDEFGHIJKLMNOPQRSTUVWXYZ") + ".";
            case ListStyleType.LowerAlpha:
                return Alphabetic(index, "abcdefghijklmnopqrstuvwxyz") + ".";
            case ListStyleType.UpperAlpha:
                return Alphabetic(index, "ABCDEFGHIJKLMNOPQRSTUVWXYZ") + ".";
            case ListStyleType.LowerGreek:
                return Alphabetic(index, "αβγδεζηθικλμνξοπρστυφχψω") + ".";
            case ListStyleType.UpperGreek:
                return Alphabetic(index, "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩ") + ".";
            case ListStyleType.Armenian:
                return Alphabetic(index, "աբգդեզէըթժիլխծկհձղճմյնշոչպջռսվտրցւփքօֆ") + ".";
            case ListStyleType.Georgian:
                return Alphabetic(index, "ႠႢႣႤႥႦႧႨႩႪႫႬႭႮႯႰႲႳႴႶႷႹႺႻႼႽႾႿ") + ".";
            case ListStyleType.Hebrew:
                return Additive(index, HebrewUnits) + ".";
            case ListStyleType.Hiragana:
                return Alphabetic(index, "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん");
            case ListStyleType.Katakana:
                return Alphabetic(index, "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン");
            case ListStyleType.HiraganaIroha:
                return Alphabetic(index, "いろはにほへとちりぬるをわかよたれそつねならむうゐのおくやまけふこえてあさきゆめみしゑひもせす");
            case ListStyleType.KatakanaIroha:
                return Alphabetic(index, "イロハニホヘトチリヌルヲワカヨタレソツネナラムウヰノオクヤマケフコエテアサキユメミシヱヒモセス");
            case ListStyleType.CjkDecimal:
                return CjkDecimal(index) + "、";
            case ListStyleType.CjkIdeographic:
                return CjkDecimal(index) + "、";
            case ListStyleType.CjkEarthlyBranch:
                return Alphabetic(index, "子丑寅卯辰巳午未申酉戌亥") + ".";
            case ListStyleType.CjkHeavenlyStem:
                return Alphabetic(index, "甲乙丙丁戊己庚辛壬癸") + ".";
            case ListStyleType.Thai:
                return Numeric(index, "๐๑๒๓๔๕๖๗๘๙") + ".";
            case ListStyleType.Lao:
                return Numeric(index, "໐໑໒໓໔໕໖໗໘໙") + ".";
            case ListStyleType.Khmer:
                return Numeric(index, "០១២៣៤៥៦៧៨៩") + ".";
            case ListStyleType.Myanmar:
                return Numeric(index, "၀၁၂၃၄၅၆၇၈၉") + ".";
            case ListStyleType.Mongolian:
                return Numeric(index, "᠐᠑᠒᠓᠔᠕᠖᠗᠘᠙") + ".";
            case ListStyleType.ArabicIndic:
                return Numeric(index, "٠١٢٣٤٥٦٧٨٩") + ".";
            case ListStyleType.Persian:
                return Numeric(index, "۰۱۲۳۴۵۶۷۸۹") + ".";
            case ListStyleType.Devanagari:
                return Numeric(index, "०१२३४५६७८९") + ".";
            case ListStyleType.Bengali:
                return Numeric(index, "০১২৩৪৫৬৭৮৯") + ".";
            case ListStyleType.Tamil:
                return Numeric(index, "௦௧௨௩௪௫௬௭௮௯") + ".";
            case ListStyleType.Telugu:
                return Numeric(index, "౦౧౨౩౪౫౬౭౮౯") + ".";
            case ListStyleType.CanadianAboriginal:
                return Numeric(index, "ᐳᐴᐵᐶᐷᐸᐹᐺᐻᐼ") + ".";
            case ListStyleType.Symbol:
                return Alphabetic(index, "☐☑☒☓☔☕☖☗☘☙") ;
            default:
                return null;
        }
    }

    /// <summary>True when the style paints a fixed glyph instead of a counter.</summary>
    public static bool IsSymbolic(ListStyleType type) => type is ListStyleType.Disc or ListStyleType.Circle
        or ListStyleType.Square or ListStyleType.None;

    /// <summary>Alphabetic (base-N without a zero) system, CSS Lists 3 §6.3.</summary>
    private static string Alphabetic(int value, string symbols)
    {
        int len = symbols.Length;
        if (len == 0) return value.ToString();
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

    /// <summary>Decimal digits rendered with the style's own glyph run (§6.2).</summary>
    private static string Numeric(int value, string digits)
    {
        if (digits.Length != 10) return value.ToString();
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var buffer = new char[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            buffer[i] = char.IsDigit(c) ? digits[c - '0'] : c;
        }
        return new string(buffer);
    }

    private static readonly (int Value, string Symbols)[] HebrewUnits =
    {
        (400, "ת"), (300, "ש"), (200, "ר"), (100, "ק"), (90, "צ"), (80, "פ"), (70, "ע"),
        (60, "ס"), (50, "נ"), (40, "מ"), (30, "ל"), (20, "כ"),
        (19, "יט"), (18, "יח"), (17, "יז"), (16, "טז"), (15, "טו"), (14, "יד"), (13, "יג"),
        (12, "יב"), (11, "יא"), (10, "י"), (9, "ט"), (8, "ח"), (7, "ז"), (6, "ו"), (5, "ה"),
        (4, "ד"), (3, "ג"), (2, "ב"), (1, "א"),
    };

    /// <summary>Additive system (§6.4): the largest value wins, repeated as needed.</summary>
    private static string Additive(int value, (int Value, string Symbols)[] units)
    {
        if (value <= 0) return value.ToString();
        var text = new System.Text.StringBuilder();
        int remaining = value;
        foreach (var unit in units)
        {
            while (remaining >= unit.Value)
            {
                text.Append(unit.Symbols);
                remaining -= unit.Value;
                // Hebrew repeats the largest glyph only for the thousands; the
                // 15/16 substitutions above keep the rest well formed.
                if (unit.Value >= 400)
                    break;
            }
            if (remaining == 0)
                break;
        }
        if (remaining > 0)
            text.Append(remaining.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }

    private static readonly string[] CjkDigits = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

    /// <summary>'cjk-decimal' (§A.2): 一…十, then 十一, 二十, 二十一 …</summary>
    private static string CjkDecimal(int value)
    {
        if (value < 10)
            return CjkDigits[value];
        if (value < 20)
            return "十" + (value % 10 == 0 ? "" : CjkDigits[value % 10]);
        if (value < 100)
        {
            int tens = value / 10;
            return CjkDigits[tens] + "十" + (value % 10 == 0 ? "" : CjkDigits[value % 10]);
        }
        // Beyond two digits the positional form is used.
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var buffer = new System.Text.StringBuilder();
        foreach (char c in text)
            buffer.Append(CjkDigits[c - '0']);
        return buffer.ToString();
    }
}
