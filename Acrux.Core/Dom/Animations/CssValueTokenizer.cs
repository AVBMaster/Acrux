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

    /// <summary>Format a number the way CSS serialisation does: at most six significant digits,
    /// rounded off the number ITSELF, and the exponent form outside 1e-4 to 1e6 (measured:
    /// '0.000123457px' from '0.000123456789', '12345.7px' from '12345.6789', '1e+06px',
    /// '9.99e-05px', '3.35544e+07px'). The rounding is the one the binary value deserves, not the one
    /// its shortest decimal suggests, and that is what separates the two surfaces a number is read
    /// on: the page's '0.01234565px' sits below its own six-digit tie in double precision, so the
    /// style attribute echoes '0.0123456px', while the same digits held in the single precision the
    /// layout works with sit above it, so the computed value reads '0.0123457px' (both measured, on
    /// 'letter-spacing', which cuts nothing). Six digits is also what hides a number's travel
    /// through a float: the '33.3' a page wrote arrives here as 33.299999237 and must not print all
    /// nine of those digits. A zero keeps no sign of its own — the arithmetic that cancelled itself
    /// out prints '0', which is what 'transition-duration: -0s' computes to (measured).</summary>
    public static string Num(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) || value == 0
            ? "0" : PrintSix(value.ToString("G6", CultureInfo.InvariantCulture));

    /// <summary>The same print for a number the engine holds in single precision, rounded off the
    /// float: '0.000001px' is stored one step under the decimal and still reads '1e-06px', and
    /// '0.1234565px' reads '0.0123457px'-side of its tie rather than the double's side (measured).</summary>
    public static string Num(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) || value == 0
            ? "0" : PrintSix(((double)value).ToString("G6", CultureInfo.InvariantCulture));

    /// <summary>One six-digit print to the spelling the reference engine uses: a lower-case 'e', a
    /// signed exponent of at least two digits, and nothing at all for a number under a
    /// ten-millionth of a unit. That last rule is what hides the arithmetic noise under a value the
    /// engine just worked out — 'rotate(0.5turn)' is 'matrix(-1, 0, 0, -1, 0, 0)', not
    /// 'matrix(-1, -8.74228e-08, …)' (measured) — and it is the same line the layout's own fixed
    /// point draws.</summary>
    private static string PrintSix(string printed)
    {
        int at = printed.IndexOfAny(['e', 'E']);
        if (at < 0) return printed;
        if (!int.TryParse(printed[(at + 1)..], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out int exponent))
            return printed;
        if (exponent < -7) return "0";
        return printed[..at] + "e" + (exponent < 0 ? "-" : "+")
             + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>The box's own numbers, which the reference engine cuts at seven digits after the
    /// point before it prints them: 'margin-left: 0.000123456789px' reads back '0.0001234px' while
    /// 'line-height: 0.000123456789px' reads '0.000123457px' (measured, both, and the same split on
    /// the style attribute's echo). A number already inside seven digits keeps every one of them and
    /// prints its own six-digit rounding — 'margin-left: 9.999995px' reads '10px', not the
    /// seven-digit floor of it (measured) — and the cut takes digits away without rounding up:
    /// 'margin-left: 0.001234555px' keeps '0.0012345', where a rounding would say '0.0012346'.
    ///
    /// The family is the box: a margin side, a padding side, a size, a minimum size, a physical
    /// inset, a font size and a shape's margin. The measured list is exactly that and no more —
    /// 'max-width', the 'inset' shorthand, 'inset-inline-start', 'scroll-margin-left',
    /// 'line-height', 'letter-spacing', 'word-spacing', 'text-indent', 'border-radius',
    /// 'border-width', 'outline-width', 'translate', 'background-position-x', 'column-width',
    /// 'gap', 'flex-basis', 'text-underline-offset', 'clip' and 'stroke-width' all print the
    /// number as written.</summary>
    public static bool CutsAtSevenDecimals(string? property)
    {
        if (string.IsNullOrEmpty(property)) return false;
        var name = property!.ToLowerInvariant();
        return name is "width" or "height" or "min-width" or "min-height"
            or "inline-size" or "block-size" or "min-inline-size" or "min-block-size"
            or "top" or "right" or "bottom" or "left"
            or "font-size" or "shape-margin"
            || name.StartsWith("margin", StringComparison.Ordinal)
            || name.StartsWith("padding", StringComparison.Ordinal);
    }

    /// <summary>The cut taken on the digits a number is written as: everything past the seventh
    /// place after the point goes and what is left is printed by the rule above. This is the one
    /// home of the family's cut, and the surfaces differ only in whose digits they hand it — the
    /// style attribute's echo gives it the text the page wrote, the computed surface gives it the
    /// shortest decimal of the float the value was stored in.</summary>
    public static string NumCutText(string number) => NumCutText(number, asFloat: false);

    public static string NumCutText(string number, bool asFloat)
    {
        var cut = CutToSevenDigits(number);
        return asFloat
            ? float.TryParse(cut, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? Num(f) : "0"
            : double.TryParse(cut, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? Num(d) : "0";
    }

    /// <summary>The same cut for a number the engine holds in single precision, taken off the
    /// shortest decimal that reads back as that float. 'margin-left: 0.01234565px' is stored above
    /// its own six-digit tie and would print '0.0123457px', but eight decimals is one more than the
    /// family keeps, so the eighth goes first and the computed value is '0.0123456px' (measured) —
    /// and the '0.0123456px' a page wrote is already inside the grid, so it prints as written and
    /// never shows the single-precision step under it.</summary>
    public static string NumCut(float value) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? "0" : NumCutText(value.ToString(CultureInfo.InvariantCulture), asFloat: true);

    /// <summary>Drop everything a decimal says past the seventh place after the point. A number with
    /// fewer digits, a whole number and an exponent print come back as they went in: the cut is only
    /// ever a cut of digits, never a change of value.</summary>
    private static string CutToSevenDigits(string text)
    {
        int point = text.IndexOf('.');
        if (point < 0 || text.IndexOfAny(['e', 'E']) >= 0) return text;
        return text.Length - point - 1 <= 7 ? text : text[..(point + 8)];
    }


    /// <summary>The largest number of pixels a computed length is allowed to say. A page can write
    /// a number the property holds and no box can be: the reference engine prints the author's
    /// digits in the CSSOM and its own ceiling in the computed value, because the layout works in
    /// a fixed-point number that cannot carry more than 2^25 pixels (measured: '99999951px' echoes
    /// as '1e+08px' and computes as '3.35544e+07px').</summary>
    public const double MaxComputedPixels = 33554432;

    /// <summary>Fold a computed length into the range the layout can represent. This is a print
    /// rule, not a layout rule: the value the engine lays out with stays the one it parsed.</summary>
    public static double ClampComputedPixels(double pixels) =>
        pixels > MaxComputedPixels ? MaxComputedPixels
        : pixels < -MaxComputedPixels ? -MaxComputedPixels
        : pixels;

    /// <summary>The same ceiling for a length the engine holds in single precision.</summary>
    public static float ClampComputedPixels(float pixels) =>
        pixels > MaxComputedPixels ? (float)MaxComputedPixels
        : pixels < -MaxComputedPixels ? (float)-MaxComputedPixels
        : pixels;

    /// <summary>
    /// An 8-bit alpha as the shortest decimal that maps back to the same byte. The reference
    /// engine keeps the authored fraction, so 'rgba(0 0 0 / 50%)' reads back as '0.5'; a naive
    /// 128/255 division prints '0.501961' instead. Trying 1, 2 then 3 decimals and taking the
    /// first that round-trips reproduces '0.5', '0.333' and '0' the way the browser does.
    /// </summary>
    public static string AlphaText(int alphaByte)
    {
        if (alphaByte <= 0) return "0";
        if (alphaByte >= 255) return "1";
        for (int decimals = 1; decimals <= 3; decimals++)
        {
            double rounded = Math.Round(alphaByte / 255.0, decimals, MidpointRounding.AwayFromZero);
            if ((int)Math.Round(rounded * 255.0, MidpointRounding.AwayFromZero) == alphaByte)
                return rounded.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture)
                    .TrimEnd('0').TrimEnd('.').Length == 0 ? "0"
                    : rounded.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture)
                        .TrimEnd('0').TrimEnd('.');
        }
        return (alphaByte / 255.0).ToString("0.###", CultureInfo.InvariantCulture);
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
