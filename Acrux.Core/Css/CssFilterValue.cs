using System.Globalization;
using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Animations;

namespace Acrux.Core.Css;

/// <summary>
/// The computed value of a <c>&lt;filter-value-list&gt;</c> (CSS Filter Effects 1 §29.1).
///
/// A filter list is not a token soup the engine may hand back the way the page typed it: at
/// computed-value time every length has lost the font it was relative to, every colour has
/// been resolved, <c>currentcolor</c> is gone, an angle is printed in degrees and an amount
/// that only ever made sense as a fraction is printed as one. And the list is all-or-nothing:
/// one function the grammar rejects — an unknown name, a fourth <c>drop-shadow()</c> length, a
/// comma where a space belongs — voids the whole declaration, so the property reads back as
/// its initial <c>none</c>.
///
/// Every rule recorded here was measured in a reference engine on 2026-10-07 and is exercised
/// by <c>snapshots/css-standard-verify239-filter-liststyle-invert.html</c> §A.
/// </summary>
public static class CssFilterValue
{
    /// <summary>Functions taking one <c>&lt;number&gt;|&lt;percentage&gt;</c> argument.</summary>
    private static readonly HashSet<string> AmountFunctions = new(StringComparer.Ordinal)
    {
        "brightness", "contrast", "saturate", "grayscale", "invert", "sepia", "opacity",
    };

    /// <summary>Of those, the ones whose argument is a fraction of an effect and therefore
    /// printed with its upper bound applied. <c>saturate()</c>, <c>brightness()</c> and
    /// <c>contrast()</c> are multipliers instead, and a multiplier above one is meaningful —
    /// <c>grayscale(2)</c> reads back as <c>grayscale(1)</c> while <c>saturate(3)</c> stays
    /// <c>saturate(3)</c> (measured).</summary>
    private static readonly HashSet<string> FractionFunctions = new(StringComparer.Ordinal)
    {
        "grayscale", "invert", "opacity", "sepia",
    };

    /// <summary>Absolute units and their exact px factor (CSS Values 3 §6.6), read from the one
    /// table the whole engine shares — <see cref="Length"/> owns it so that a bare '4pt', a
    /// <c>calc(4pt)</c> and a <c>blur(4pt)</c> cannot answer three numbers. Measured: <c>4pt</c>
    /// is <c>5.33333px</c> and <c>1q</c> is <c>0.944882px</c>.</summary>
    private static double? AbsoluteFactor(string unit)
        => unit == "px" ? 1 : Length.TryAbsoluteUnitPixels(unit, out var perUnit) ? perUnit : null;

    /// <summary>The computed value of the list, or <c>"none"</c> when it is empty or when any
    /// function in it is invalid.</summary>
    /// <param name="specified">The value the cascade holds for the property.</param>
    /// <param name="style">The element's style: a font-relative length resolves against its
    /// font, a viewport-relative one against the viewport the style was computed for, and a
    /// <c>drop-shadow()</c> written without a colour takes its
    /// <c>color</c> (measured: a box coloured <c>rgb(200, 0, 0)</c> answers
    /// <c>drop-shadow(rgb(200, 0, 0) 6px 6px 0px)</c>).</param>
    public static string ComputedText(string? specified, ComputedStyle? style)
    {
        var text = (specified ?? "").Trim();
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";

        // '<filter-value-list>' is a SPACE-separated list. A comma is not a separator here, and
        // the reference engine rejects the declaration rather than skipping the item it cannot
        // read: 'blur(2px), brightness(1.2)' computes as 'none' (measured). Splitting on
        // whitespace only leaves the comma glued to the function's closing parenthesis, which
        // no longer reads as a function call.
        var items = CssValueTokenizer.SplitTokens(text);
        if (items.Count == 0) return "none";

        var parts = new List<string>(items.Count);
        foreach (var item in items)
        {
            var computed = ComputeItem(item, style);
            // A single unrecognised word ('none' beside a function, a bare 'grayscale') is not
            // a function call at all, and voids the list just like a bad argument does.
            if (computed == null) return "none";
            parts.Add(computed);
        }
        return string.Join(" ", parts);
    }

    private static string? ComputeItem(string item, ComputedStyle? style)
    {
        if (!CssValueTokenizer.TryReadFunction(item, out var rawName, out var argsText)) return null;
        var name = rawName.Trim().ToLowerInvariant();

        // A <url> in a filter list names a <filter> element in the document. It is printed
        // quoted, and a fragment-only address is left alone rather than made absolute
        // (measured: 'url(#ff)' reads back as 'url("#ff")'). Checked before the comma rule
        // because a comma is ordinary content inside an address.
        if (name == "url")
        {
            var urlArguments = SplitArgumentTokens(argsText);
            return urlArguments.Count == 1 ? $"url(\"{Unquote(urlArguments[0])}\")" : null;
        }

        // No filter function takes a comma-separated argument list, and a comma inside the
        // parentheses is not a separator the grammar allows either (measured:
        // 'drop-shadow(6px, 6px, green)' computes as 'none').
        if (HasTopLevelComma(argsText)) return null;
        var args = SplitArgumentTokens(argsText);

        if (name == "blur")
        {
            if (args.Count > 1) return null;
            // An omitted radius is the zero length, not a parse error (measured: 'blur()' →
            // 'blur(0px)').
            var radius = args.Count == 0 ? "0px" : LengthText(args[0], style, nonNegative: true);
            return radius == null ? null : $"blur({radius})";
        }

        if (name == "hue-rotate")
        {
            if (args.Count > 1) return null;
            var angle = args.Count == 0 ? "0deg" : AngleText(args[0]);
            return angle == null ? null : $"hue-rotate({angle})";
        }

        if (name == "drop-shadow") return DropShadowText(args, style);

        if (AmountFunctions.Contains(name))
        {
            if (args.Count > 1) return null;
            var amount = Amount(args.Count == 0 ? null : args[0]);
            if (amount == null) return null;
            if (FractionFunctions.Contains(name)) amount = Math.Min(amount.Value, 1);
            return $"{name}({Num(amount.Value)})";
        }

        // An unknown function voids the whole list (measured: 'foo(2px)' → 'none'), which is
        // also the answer for a name spelled with the wrong case handled above.
        return null;
    }

    /// <summary>
    /// <c>drop-shadow()</c> is <c>&lt;length&gt;{2,3} &lt;color&gt;?</c>, and the colour is
    /// printed FIRST with the blur always present, so every accepted spelling reads back the
    /// same way (measured): 'drop-shadow(green 6px 6px)', 'drop-shadow(6px 6px green)' and
    /// 'drop-shadow(6px 6px 0 rgb(0,128,0))' all compute as
    /// 'drop-shadow(rgb(0, 128, 0) 6px 6px 0px)'. A second colour, one length or four of them
    /// reject the list; so does a percentage, which is not a <c>&lt;length&gt;</c> here.
    ///
    /// The two offsets carry a direction and may be negative — 'drop-shadow(-2px -3px 4px
    /// green)' reads back unchanged — while the third length is a radius, and a negative one is
    /// out of range and voids the list (measured: 'drop-shadow(2px 3px -1px)' → 'none'). The
    /// sign is therefore checked per position, not per token, which needs the lengths kept in
    /// the order they were written in.
    /// </summary>
    private static string? DropShadowText(List<string> args, ComputedStyle? style)
    {
        SKColor? color = null;
        var lengths = new List<string>();
        foreach (var token in args)
        {
            if (ColorParser.IsColorToken(token))
            {
                if (color != null) return null;
                // 'currentcolor' names the element's own text colour, and the computed value
                // never keeps the keyword (measured: 'drop-shadow(6px 6px 0 currentcolor)' on a
                // box coloured rgb(17, 17, 17) prints that colour). Read from the style directly:
                // ColorParser has no element to ask.
                color = token.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)
                    ? style?.Color ?? new SKColor(0, 0, 0)
                    : ColorParser.Parse(token, style);
                continue;
            }
            // A length is an offset while fewer than three have been seen, and the blur after them.
            var length = LengthText(token, style, nonNegative: lengths.Count >= 2);
            if (length == null) return null;
            lengths.Add(length);
        }

        if (lengths.Count is < 2 or > 3) return null;
        var used = color ?? style?.Color ?? new SKColor(0, 0, 0);
        var blur = lengths.Count > 2 ? lengths[2] : "0px";
        return $"drop-shadow({CssColorText.FromColor(used)} {lengths[0]} {lengths[1]} {blur})";
    }

    /// <summary>An effect amount: a bare number, a percentage of one, or <c>null</c> when the
    /// token is neither. An omitted argument is the default <c>1</c> (measured:
    /// 'brightness()' → 'brightness(1)'), and a negative one is out of the value range — the
    /// reference engine rejects it rather than clamping to zero (measured: 'grayscale(-1)',
    /// 'opacity(-1)', 'contrast(-2)' and 'saturate(-1)' all read back as 'none').</summary>
    private static double? Amount(string? token)
    {
        if (string.IsNullOrEmpty(token)) return 1;
        if (!CssValueTokenizer.TrySplitUnit(token, out var number, out var unit)) return null;
        if (unit.Length == 0) return number < 0 ? null : number;
        if (char.ToLowerInvariant(unit[0]) != '%') return null;
        number /= 100;
        return number < 0 ? null : number;
    }

    /// <summary>A <c>&lt;length&gt;</c> resolved to pixels, or <c>null</c> when the token is
    /// not one. Percentages are excluded (they are not valid in a filter function), a unitless
    /// number only survives as the zero it may stand for, and a font- or viewport-relative unit
    /// is resolved against the element the property belongs to.</summary>
    /// <param name="nonNegative">The value range of a radius: <c>blur()</c> and the third
    /// <c>drop-shadow()</c> length spread light, which has no direction, so a negative one is
    /// rejected out of hand rather than clamped (measured: 'blur(-2px)' and
    /// 'drop-shadow(2px 3px -1px)' both compute as 'none'). A zero written negative ('-0.0px')
    /// is still a zero and survives.</param>
    private static string? LengthText(string token, ComputedStyle? style, bool nonNegative = false)
    {
        if (!CssValueTokenizer.TrySplitUnit(token, out var number, out var unit)) return null;
        unit = unit.ToLowerInvariant();
        if (nonNegative && number < 0) return null;
        if (unit.Length == 0) return number == 0 ? "0px" : null;
        if (unit == "%") return null;

        var factor = AbsoluteFactor(unit);
        if (factor != null) return Num(number * factor.Value) + "px";

        // Rebuild the token without the spelling the page chose ('+2.50PX' → '2.5px'), so the
        // length parser sees a canonical dimension and the printed number is the value, not
        // the text.
        var length = Length.Parse(Num(number) + unit);
        if (length is AutoLength or PercentLength or IntrinsicLength) return null;
        var text = style?.ComputedLengthCss(length);
        return text is { Length: > 0 } && text.EndsWith("px", StringComparison.Ordinal) ? text : null;
    }

    /// <summary>An <c>&lt;angle&gt;</c> printed in degrees: the reference engine converts every
    /// unit to deg rather than keeping the authored one (measured: '0.5turn' → '180deg',
    /// '100grad' → '90deg', '1.5rad' → '85.9437deg'). A negative angle is allowed — only the
    /// effect amounts have a lower bound.</summary>
    private static string? AngleText(string token)
    {
        if (!CssValueTokenizer.TrySplitUnit(token, out var number, out var unit)) return null;
        var scale = unit.ToLowerInvariant() switch
        {
            "deg" => 1.0,
            "grad" => 0.9,
            "rad" => 180.0 / Math.PI,
            "turn" => 360.0,
            // A unitless angle is invalid except for the zero it may stand for.
            "" => number == 0 ? 1.0 : double.NaN,
            _ => double.NaN,
        };
        return double.IsNaN(scale) ? null : Num(number * scale) + "deg";
    }

    /// <summary>The arguments of one function as whitespace-separated tokens, with nested
    /// parentheses kept in one piece: <c>drop-shadow(6px 6px 0 rgb(0, 128, 0))</c> is four.</summary>
    private static List<string> SplitArgumentTokens(string argsText)
    {
        var tokens = new List<string>();
        foreach (var token in CssValueTokenizer.SplitTokens(argsText))
            if (token.Length > 0) tokens.Add(token);
        return tokens;
    }

    /// <summary>True when a comma sits at the top level of the argument text — that is, outside
    /// every nested function — where no filter grammar puts one.</summary>
    private static bool HasTopLevelComma(string argsText)
    {
        int depth = 0;
        foreach (var c in argsText)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0) return true;
        }
        return false;
    }

    private static string Unquote(string token)
    {
        var text = token.Trim();
        if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
            return text[1..^1];
        return text;
    }

    /// <summary>A CSS number as the reference engine prints it: six significant digits, plain
    /// notation for the range a stylesheet lives in and exponential outside it (measured:
    /// 'brightness(0.333333333)' → '0.333333', 'opacity(0.0000001)' → '1e-07',
    /// 'brightness(1234567)' → '1.23457e+06').</summary>
    private static string Num(double value)
    {
        var absolute = Math.Abs(value);
        if (absolute != 0 && (absolute >= 1e6 || absolute < 1e-4))
        {
            var exponent = (int)Math.Floor(Math.Log10(absolute));
            var mantissa = Math.Round(value / Math.Pow(10, exponent), 5, MidpointRounding.ToEven);
            // Rounding can carry the mantissa up to ten (9.999996 → 10), which belongs in the
            // exponent instead: 10e+5 is written 1e+6.
            if (Math.Abs(mantissa) >= 10) { mantissa /= 10; exponent++; }
            var digits = mantissa.ToString("0.#####", CultureInfo.InvariantCulture);
            return $"{digits}e{(exponent < 0 ? "-" : "+")}{Math.Abs(exponent):D2}";
        }
        return CssValueTokenizer.Num(value);
    }
}
