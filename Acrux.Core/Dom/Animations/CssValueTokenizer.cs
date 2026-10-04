using System.Globalization;
using System.Text;

namespace Acrux.Core.Dom.Animations;

/// <summary>
/// Minimal top-level-aware CSS value splitter shared by the animation code.
///
/// Interpolating a value means walking structures like
/// <c>box-shadow: 1px 2px red, inset 3px 4px blue</c> or
/// <c>transform: translate(1px) rotate(30deg)</c>, so splitting has to respect
/// parentheses, brackets and quotes instead of using plain <c>String.Split</c>.
/// </summary>
internal static class CssValueTokenizer
{
    /// <summary>Split on commas that are not nested inside () / [] / quotes.</summary>
    public static List<string> SplitTopLevel(string value)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(value)) return result;

        int depth = 0;
        var sb = new StringBuilder();
        char quote = '\0';

        foreach (char c in value)
        {
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'')
            {
                quote = c;
                sb.Append(c);
                continue;
            }
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;

            if (c == ',' && depth == 0)
            {
                var item = sb.ToString().Trim();
                if (item.Length > 0) result.Add(item);
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }

        var tail = sb.ToString().Trim();
        if (tail.Length > 0) result.Add(tail);
        return result;
    }

    /// <summary>
    /// Split a chain of functions into its parts, e.g.
    /// <c>brightness(0.8) grayscale(0.4)</c>. Needed because a filter chain is not
    /// a transform list: the transform scanner only knows transform functions.
    /// </summary>
    public static List<(string Name, List<string> Args)> SplitFunctions(string value)
    {
        var result = new List<(string, List<string>)>();
        if (string.IsNullOrWhiteSpace(value)) return result;

        int i = 0;
        while (i < value.Length)
        {
            if (char.IsWhiteSpace(value[i])) { i++; continue; }

            int nameStart = i;
            while (i < value.Length && value[i] != '(' && !char.IsWhiteSpace(value[i])) i++;
            string name = value[nameStart..i].Trim().ToLowerInvariant();

            if (i >= value.Length || value[i] != '(')
            {
                // Not a function (a bare keyword such as `none`): keep it as an
                // opaque part so the caller can recognise it.
                if (name.Length > 0) result.Add((name, new List<string>()));
                continue;
            }

            int depth = 0;
            int argStart = i + 1;
            while (i < value.Length)
            {
                if (value[i] == '(') depth++;
                else if (value[i] == ')')
                {
                    depth--;
                    if (depth == 0) break;
                }
                i++;
            }
            string argText = value[argStart..Math.Min(i, value.Length)];
            result.Add((name, SplitArguments(argText)));
            i++;   // past the ')'
        }
        return result;
    }

    /// <summary>Split on whitespace that is not nested inside () / [] / quotes.</summary>
    public static List<string> SplitTokens(string value)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(value)) return result;

        int depth = 0;
        var sb = new StringBuilder();
        char quote = '\0';

        foreach (char c in value)
        {
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'')
            {
                quote = c;
                sb.Append(c);
                continue;
            }
            if (c == '(' || c == '[') { depth++; sb.Append(c); continue; }
            if (c == ')' || c == ']') { depth--; sb.Append(c); continue; }

            if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    /// <summary>
    /// Read a <c>name(args)</c> function. Returns false when the text is not a
    /// single function call (e.g. <c>none</c> or a bare keyword).
    /// </summary>
    public static bool TryReadFunction(string text, out string name, out string argsText)
    {
        name = "";
        argsText = "";
        int open = text.IndexOf('(');
        if (open <= 0 || !text.EndsWith(')')) return false;
        name = text[..open].Trim();
        if (name.Length == 0 || name.Contains(' ')) return false;
        argsText = text[(open + 1)..^1];
        return true;
    }

    public static List<string> SplitArguments(string argsText) => SplitTopLevel(argsText);

    /// <summary>Split a comma list further on whitespace, e.g. "1px 2px, 3px 4px".</summary>
    public static List<List<string>> SplitItemsAndTokens(string value)
    {
        var items = new List<List<string>>();
        foreach (var item in SplitTopLevel(value))
            items.Add(SplitTokens(item));
        return items;
    }

    /// <summary>Format a number the way CSS serialization does (no trailing zeros).</summary>
    public static string Num(double value)
    {
        if (Math.Abs(value) < 1e-7) return "0";
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    public static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Split a <c>&lt;number&gt;&lt;unit&gt;</c> token. Percentages and the
    /// dimensionless zero are handled by the caller, which knows the property's
    /// type; this only peels off a trailing alphabetic unit.
    /// </summary>
    public static bool TrySplitUnit(string token, out double number, out string unit)
    {
        number = 0;
        unit = "";
        var t = token.Trim();
        if (t.Length == 0) return false;

        int i = t.Length;
        if (t[i - 1] == '%') { unit = "%"; i--; }
        else
        {
            while (i > 0 && (char.IsLetter(t[i - 1]) || t[i - 1] == '%')) i--;
            unit = t[i..];
        }

        var numText = t[..i];
        if (numText.Length == 0) return false;
        if (!double.TryParse(numText, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return false;
        return true;
    }
}
