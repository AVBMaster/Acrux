using System.Globalization;
using System.Text;

namespace Acrux.Core.Css;

/// <summary>
/// The one reader and printer of the CSS Color 4 functions whose arguments are NOT plain numbers:
/// <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c> and <c>color()</c>. Each of them has
/// channels with their own range and their own units, and the engine prints every one of them the
/// same three ways (all measured against the reference engine):
///
/// <list type="bullet">
/// <item>a lightness written as a percentage is the number it means — <c>lab(50% …)</c> prints
/// <c>lab(50 …)</c>, while <c>oklab()</c> and <c>oklch()</c> take their lightness on 0 to 1, so
/// <c>oklab(50% …)</c> prints <c>oklab(0.5 …)</c>;</item>
/// <item>an angle is printed in degrees without the unit — <c>lch(… 50grad)</c> is
/// <c>lch(… 45)</c>, and <c>120deg</c> is <c>120</c>;</item>
/// <item>an alpha is a fraction, so <c>/ 50%</c> prints <c>/ 0.5</c>, and the keyword
/// <c>none</c> is printed as itself in any position.</item>
/// </list>
///
/// <para><c>color()</c> has one more: the space <c>xyz</c> is an alias of <c>xyz-d65</c> and the
/// engine prints the long name (measured). The channels themselves are left exactly as written —
/// they are numbers in the colour space's own range, and nothing about them is the engine's to
/// change.</para>
///
/// <para>This is not only a spelling. The parsers used to meet <c>120deg</c> where they expected a
/// number, failed to read the hue, and painted <c>lch(50% 30 120deg)</c> BLACK — so the canonical
/// text is also what the parser is handed, and one value cannot be printed one way and painted
/// another.</para>
/// </summary>
public static class CssColorFunctions
{
    private static readonly string[] Names = { "lab", "lch", "oklab", "oklch", "color" };

    /// <summary>The canonical text of the colour function, or null when the token is not one of
    /// these functions or does not read as its grammar.</summary>
    public static string? Canonicalize(string token)
    {
        token = (token ?? string.Empty).Trim();
        int open = token.IndexOf('(');
        if (open <= 0 || !token.EndsWith(")", StringComparison.Ordinal)) return null;
        var name = token[..open].ToLowerInvariant();
        if (Array.IndexOf(Names, name) < 0) return null;
        var inner = token[(open + 1)..^1].Trim();
        if (inner.Length == 0) return null;

        // 'color()' is a space name followed by its channels; the others are three channels with an
        // optional alpha after a slash.
        var head = new List<string>();
        var rest = inner;
        if (name == "color")
        {
            int space = InnerIndexOfWhitespace(inner);
            if (space < 0) return null;
            head.Add(ExpandColorSpace(inner[..space].ToLowerInvariant()));
            rest = inner[(space + 1)..].Trim();
        }

        var (main, alpha) = SplitAlpha(rest);
        var args = SplitArgs(main);
        int channelCount = name == "color" ? 3 : 3;
        if (args.Count < (name == "color" ? 1 : channelCount) || args.Count > channelCount) return null;
        if (name != "color" && args.Count != channelCount) return null;

        var printed = new List<string>(head);
        for (int i = 0; i < args.Count; i++)
        {
            var word = args[i];
            if (word.Equals("none", StringComparison.OrdinalIgnoreCase)) { printed.Add("none"); continue; }
            bool isLightness = name != "color" && i == 0;
            bool isAngle = (name == "lch" || name == "oklch") && i == 2;
            if (isAngle)
            {
                if (!TryAngle(word, out double degrees)) return null;
                printed.Add(Num(degrees));
                continue;
            }
            if (isLightness)
            {
                if (!Number(word, out double lightness)) return null;
                bool percent = word.EndsWith("%", StringComparison.Ordinal);
                if (percent && (name == "oklab" || name == "oklch")) lightness /= 100d;
                printed.Add(Num(lightness));
                continue;
            }
            if (!Number(word, out double value)) return null;
            printed.Add(Num(value));
        }

        var body = new StringBuilder();
        if (name == "color")
        {
            // A colour space with no channels is no colour.
            if (printed.Count == 1) return null;
            body.Append(printed[0]).Append(' ');
            body.Append(string.Join(' ', printed.GetRange(1, printed.Count - 1)));
        }
        else body.Append(string.Join(' ', printed));

        if (alpha != null)
        {
            if (alpha.Equals("none", StringComparison.OrdinalIgnoreCase)) body.Append(" / none");
            else
            {
                if (!Fraction(alpha, out double a)) return null;
                if (a < 0) a = 0; else if (a > 1) a = 1;
                body.Append(" / ").Append(Num(a));
            }
        }
        return name + "(" + body + ")";
    }

    /// <summary>An alpha: a number on 0 to 1 or the percentage of it, which is the fraction it
    /// means (measured: 'lab(50% 20 30 / 50%)' prints 'lab(50 20 30 / 0.5)').</summary>
    private static bool Fraction(string token, out double value)
    {
        token = (token ?? string.Empty).Trim();
        if (token.EndsWith("%", StringComparison.Ordinal))
        {
            if (!double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;
            value /= 100d;
            return !double.IsNaN(value);
        }
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !double.IsNaN(value);
    }

    /// <summary>The canonical text with every <c>none</c> channel replaced by the 0 it computes as
    /// (CSS Color 4 §3), for the callers that read numbers rather than print them.</summary>
    public static string ForParsing(string canonical)
        => canonical.Contains("none", StringComparison.OrdinalIgnoreCase)
            ? System.Text.RegularExpressions.Regex.Replace(canonical, @"\bnone\b", "0")
            : canonical;

    /// <summary>The reference engine's own name for the space: 'xyz' is the alias of 'xyz-d65' and
    /// the long name is what comes back out (measured). Every other space is printed as written.</summary>
    private static string ExpandColorSpace(string space) => space == "xyz" ? "xyz-d65" : space;

    private static int InnerIndexOfWhitespace(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && char.IsWhiteSpace(c)) return i;
        }
        return -1;
    }

    /// <summary>The alpha, which CSS Color 4 introduces with a slash and the legacy syntax with a
    /// comma — and a slash inside a nested call belongs to that call, not to this one.</summary>
    private static (string Main, string? Alpha) SplitAlpha(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && (c == '/' || (c == ',' && i > 0 && CountTopLevel(text, i) >= 3)))
            {
                return (text[..i].Trim(), text[(i + 1)..].Trim());
            }
        }
        return (text.Trim(), null);
    }

    private static int CountTopLevel(string text, int upTo)
    {
        int count = 1, depth = 0;
        for (int i = 0; i < upTo; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && (c == ',' || char.IsWhiteSpace(c))
                     && i + 1 < upTo && !(c == ',' && char.IsWhiteSpace(text[i + 1]))) count++;
        }
        return count;
    }

    /// <summary>The channels, split on whitespace or on commas — both spellings are the same list.</summary>
    private static List<string> SplitArgs(string text)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        int depth = 0;
        foreach (var c in text)
        {
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            if (depth == 0 && (char.IsWhiteSpace(c) || c == ','))
            {
                if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }

    private static bool Number(string token, out double value)
    {
        token = (token ?? string.Empty).Trim();
        if (token.EndsWith("%", StringComparison.Ordinal))
            token = token[..^1];
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static readonly (string Unit, double Per)[] AngleUnits =
    { ("turn", 360d), ("rad", 180d / Math.PI), ("grad", 0.9d), ("deg", 1d) };

    private static bool TryAngle(string token, out double degrees)
    {
        token = (token ?? string.Empty).Trim();
        foreach (var (unit, per) in AngleUnits)
        {
            if (token.EndsWith(unit, StringComparison.OrdinalIgnoreCase)
                && Number(token[..^unit.Length], out double n))
            {
                degrees = n * per;
                return true;
            }
        }
        if (Number(token, out degrees)) return true;
        return false;
    }

    /// <summary>The shortest decimal that says the number, without an exponent for the ordinary
    /// range — which is how the reference engine prints a colour channel (measured: 45, 0.5, 57.29578).</summary>
    private static string Num(double value)
    {
        if (value == 0) return "0";
        var text = value.ToString("G7", CultureInfo.InvariantCulture);
        if (text.Contains('E', StringComparison.Ordinal) || text.Contains('e', StringComparison.Ordinal))
            text = value.ToString("0.#######", CultureInfo.InvariantCulture);
        return text;
    }
}
