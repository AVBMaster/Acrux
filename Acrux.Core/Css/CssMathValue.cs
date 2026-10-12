using System.Globalization;
using System.Text;
using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// The text a reference engine prints for a math function. A <c>calc()</c> is not kept as the
/// tokens the page typed but reduced to a sum of terms it can still evaluate: absolute units are
/// converted to the unit they print as, terms of one unit are merged, the terms are printed in the
/// order of their unit names with the percentages in front, and the numbers they carry are printed
/// to six significant digits. A math function other than <c>calc()</c> is answered outright — and
/// so becomes a <c>calc()</c> — when every one of its arguments is a value the engine knows without
/// being told the box or the font, and is left standing as written when one of them is not.
/// </summary>
/// <remarks>
/// Measured, all on the specified value of a declaration (snapshots/_probe_b265_l.html,
/// snapshots/_probe_b265_m.html):
/// <c>calc(10px + 2px)</c> reads <c>calc(12px)</c> and <c>calc(1in)</c> reads <c>calc(96px)</c>,
/// while <c>calc(1em + 10px)</c> keeps both terms whichever end the page wrote the em on.
/// <c>calc(500ms)</c> reads <c>calc(0.5s)</c>, <c>calc(0.5turn)</c> reads <c>calc(180deg)</c>,
/// <c>calc(10px - 1em)</c> reads <c>calc(-1em + 10px)</c>, <c>calc(300px - 100%)</c> reads
/// <c>calc(-100% + 300px)</c>, and <c>calc(1em - 1em)</c> reads <c>calc(0em)</c> — a term of
/// nothing is still a term. <c>min(1px, 2px)</c> reads <c>calc(1px)</c> but <c>min(1em, 2em)</c>
/// is printed as written, and <c>calc(min(1em, 2em) + 10px)</c> carries it behind the pixels as
/// <c>calc(10px + min(1em, 2em))</c>. The other functions answer in the type they were given:
/// <c>abs(-1px)</c> and <c>mod(10px, 3px)</c> read <c>calc(1px)</c>, <c>round(up, 10.2px, 1px)</c>
/// reads <c>calc(11px)</c>, <c>hypot(3px, 4px)</c> reads <c>calc(5px)</c>, while <c>sign(1px)</c>
/// and <c>sqrt(4)</c> are counts and read <c>calc(1)</c> and <c>calc(2)</c>, and an angle comes back
/// from the inverse functions — <c>atan2(-1, -1)</c> reads <c>calc(-135deg)</c>. A number given to
/// <c>sin()</c>, <c>cos()</c> or <c>tan()</c> is radians, not degrees, so <c>cos(0.5)</c> reads
/// <c>calc(0.877583)</c> where <c>cos(0.5turn)</c> would read <c>calc(-1)</c>. The reduction reaches
/// into a function argument and through a compound value alike: <c>offset-path: inset(calc(10px +
/// 2px))</c> reads <c>inset(calc(12px))</c> and <c>margin: calc(10px + 2px) auto</c> reads
/// <c>calc(12px) auto</c>. A value that carries
/// somebody else's tokens is not reduced at all, because its terms are not known yet:
/// <c>calc(10px + var(--a))</c> keeps both, unmerged and unordered.
/// </remarks>
public static class CssMathValue
{
    /// <summary>The functions reduced here. A legacy <c>-webkit-calc()</c> is the same value to the
    /// engine, so it folds like one and prints under the name the engine gives it (measured:
    /// <c>-webkit-calc(1px + 2px)</c> reads <c>calc(3px)</c>).</summary>
    private static readonly HashSet<string> FoldableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "calc", "min", "max", "clamp", "-webkit-calc",
        "abs", "sign", "round", "mod", "rem", "hypot",
        "sqrt", "pow", "exp", "log",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
    };

    /// <summary>The value text with every math function in it reduced.</summary>
    public static string Fold(string text)
    {
        if (text.Length == 0 || text.IndexOf('(') < 0) return text;
        var sb = new StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                i = CopyQuoted(text, i, sb);
                continue;
            }
            if (IsIdentStart(text, i))
            {
                int start = i;
                while (i < text.Length && IsIdentPart(text[i])) i++;
                var word = text[start..i];
                if (i < text.Length && text[i] == '(')
                {
                    int close = MatchingParen(text, i);
                    if (close < 0)
                    {
                        // The argument list never closes, so nothing after it can be read as an
                        // expression and the rest of the text is left exactly as it came.
                        sb.Append(text, start, text.Length - start);
                        return sb.ToString();
                    }
                    var argument = text[(i + 1)..close];
                    // An address is not an expression, whatever characters it happens to hold.
                    if (word.Equals("url", StringComparison.OrdinalIgnoreCase))
                        sb.Append(word).Append('(').Append(argument).Append(')');
                    else
                    {
                        var inner = Fold(argument);
                        sb.Append(FoldableNames.Contains(word) ? Reduce(word, inner) : word + "(" + inner + ")");
                    }
                    i = close + 1;
                    continue;
                }
                sb.Append(word);
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>The engine's six-significant-digit spelling of a number, which is what a reference
    /// engine prints for the numbers it folds and for the ones a page typed:
    /// <c>calc(100% / 3)</c> reads <c>calc(33.3333%)</c>, a <c>1234567px</c> reads
    /// <c>1.23457e+06px</c> and a <c>0.123456789px</c> reads <c>0.123457px</c> (measured).</summary>
    public static string Number(double value)
    {
        if (value == 0) value = 0;   // folds the negative zero that '-0px' carries
        return value.ToString("G6", CultureInfo.InvariantCulture).Replace('E', 'e');
    }

    // ------------------------------------------------------------------ reducing one function

    private static string Reduce(string name, string inner)
    {
        // A term whose value belongs to a custom property, an attribute or the environment is not
        // the engine's to combine, so the whole function is printed as written.
        if (HasForeignTokens(inner)) return NameOf(name) + "(" + inner + ")";

        if (name.Equals("calc", StringComparison.OrdinalIgnoreCase)
            || name.Equals("-webkit-calc", StringComparison.OrdinalIgnoreCase))
        {
            // A calc() whose whole body is one nested function says nothing that function does not
            // already say, and the reference engine prints only the function:
            // 'calc(clamp(1px, 2%, 3px))' reads back 'clamp(1px, 2%, 3px)' while the same call that
            // does fold keeps its wrapper — 'calc(min(1px, 2px))' is 'calc(1px)', not '1px'
            // (measured, both).
            if (IsSingleCall(inner)) return inner;
            return TryReadSum(inner, out var sum) && sum!.Printable
                ? "calc(" + sum.Print() + ")" : "calc(" + inner + ")";
        }

        var arguments = SplitArguments(inner);
        var lower = name.ToLowerInvariant();
        if (!FoldableNames.Contains(lower) || !ArityMatches(lower, arguments.Count))
            return name + "(" + inner + ")";

        // 'round()' opens with a rounding strategy rather than with a value, so its operands are
        // the last two arguments and not the first.
        if (lower == "round")
        {
            bool hasStrategy = RoundStrategies.Contains(arguments[0]);
            if (arguments.Count == 3 && !hasStrategy) return name + "(" + inner + ")";
            if (arguments.Count != (hasStrategy ? 3 : 2)) return name + "(" + inner + ")";
            var operands = ReadValues(arguments.GetRange(hasStrategy ? 1 : 0, 2));
            if (operands == null) return name + "(" + inner + ")";
            return Finitish(RoundTo(operands[0].Value, operands[1].Value, arguments[0]),
                operands[0].Unit, name, inner);
        }

        var values = ReadValues(arguments);
        if (values == null) return name + "(" + inner + ")";
        // The count-only functions take a number and nothing else: a 'sqrt(4px)' is left standing
        // because the expression is no value at all (measured through 'supports()').
        if (NumberOnlyArguments(lower) && values[0].Unit.Length > 0) return name + "(" + inner + ")";

        var unit = ResultUnit(lower, values[0].Unit);
        double result = lower switch
        {
            "min" => Smallest(values),
            "max" => Largest(values),
            "clamp" => ClampValue(values),
            "abs" => System.Math.Abs(values[0].Value),
            "sign" => System.Math.Sign(values[0].Value),
            "mod" => Modulo(values[0].Value, values[1].Value, false),
            "rem" => Modulo(values[0].Value, values[1].Value, true),
            "hypot" => System.Math.Sqrt(values.Sum(value => value.Value * value.Value)),
            "sqrt" => values[0].Value < 0 ? double.NaN : System.Math.Sqrt(values[0].Value),
            "exp" => System.Math.Exp(values[0].Value),
            "log" => values[0].Value <= 0 ? double.NaN : System.Math.Log(values[0].Value),
            "pow" => System.Math.Pow(values[0].Value, values[1].Value),
            "sin" => System.Math.Sin(Radians(values[0])),
            "cos" => System.Math.Cos(Radians(values[0])),
            "tan" => System.Math.Tan(Radians(values[0])),
            "asin" => System.Math.Abs(values[0].Value) > 1 ? double.NaN
                : System.Math.Asin(values[0].Value) * DegreesPerRadian,
            "acos" => System.Math.Abs(values[0].Value) > 1 ? double.NaN
                : System.Math.Acos(values[0].Value) * DegreesPerRadian,
            "atan" => System.Math.Atan(values[0].Value) * DegreesPerRadian,
            "atan2" => System.Math.Atan2(values[0].Value, values[1].Value) * DegreesPerRadian,
            _ => double.NaN,
        };
        return Finitish(result, unit, name, inner);
    }

    /// <summary>The reduced function, or the author's text when the arithmetic came out of the
    /// numbers — a division by zero, a logarithm of nothing — that no value can carry.</summary>
    private static string Finitish(double result, string unit, string name, string inner)
        => double.IsNaN(result) || double.IsInfinity(result)
            ? name + "(" + inner + ")" : "calc(" + PrintTerm(result, unit) + ")";

    /// <summary>The number of arguments a math function takes. min(), max() and hypot() take a list
    /// of them, clamp() always three, round() a value and a resolution with an optional strategy,
    /// and the arithmetic and trigonometric functions one or two.</summary>
    private static bool ArityMatches(string lower, int count) => lower switch
    {
        "min" or "max" or "hypot" => count >= 1,
        "clamp" => count == 3,
        "round" => count == 2 || count == 3,
        "mod" or "rem" or "pow" or "atan2" => count == 2,
        _ => count == 1,
    };

    /// <summary>The arguments of one function, read as the values it works on: a single term each,
    /// of a unit whose worth is known without being told the box or the font, and all of the same
    /// unit. Null when an argument is a sum of two terms, an 'em' or a percentage, which is the case
    /// a min() cannot settle.</summary>
    private static List<(double Value, string Unit)>? ReadValues(List<string> arguments)
    {
        var values = new List<(double Value, string Unit)>(arguments.Count);
        foreach (var argument in arguments)
        {
            if (!TryReadSum(argument, out var arg) || !arg!.Printable
                || arg.Opaque.Count > 0 || arg.Terms.Count != 1)
                return null;
            var only = arg.Terms.First();
            if (!AbsolutePrintedUnits.Contains(only.Key)) return null;
            values.Add((only.Value, only.Key));
            if (values[0].Unit != values[^1].Unit) return null;
        }
        return values;
    }

    private static double Smallest(List<(double Value, string Unit)> values)
    {
        double chosen = values[0].Value;
        foreach (var value in values) if (value.Value < chosen) chosen = value.Value;
        return chosen;
    }

    private static double Largest(List<(double Value, string Unit)> values)
    {
        double chosen = values[0].Value;
        foreach (var value in values) if (value.Value > chosen) chosen = value.Value;
        return chosen;
    }

    /// <summary>mod() takes the sign of the divisor and rem() that of the dividend (CSS Values 4
    /// §10.3.7), which is Euclidean against truncated division and only tells them apart on a
    /// negative argument.</summary>
    private static double Modulo(double value, double divisor, bool remainder)
    {
        if (divisor == 0) return double.NaN;
        double quotient = value / divisor;
        return value - divisor * (remainder ? System.Math.Truncate(quotient)
            : System.Math.Floor(quotient));
    }

    /// <summary>Round a value to a multiple of a resolution. 'up' rounds toward positive infinity
    /// and 'down' toward negative infinity, not away from and toward zero, so the two only differ
    /// from 'nearest' on a negative value (measured against the reference engine).</summary>
    private static double RoundTo(double value, double resolution, string strategy)
    {
        if (resolution == 0) return double.NaN;
        double quotient = value / resolution;
        double rounded = strategy.ToLowerInvariant() switch
        {
            "up" => System.Math.Ceiling(quotient),
            "down" => System.Math.Floor(quotient),
            "to-zero" => System.Math.Truncate(quotient),
            _ => System.Math.Round(quotient, MidpointRounding.AwayFromZero),
        };
        return rounded * resolution;
    }

    /// <summary>The functions that work on counts and on nothing else.</summary>
    private static bool NumberOnlyArguments(string lower) => lower switch
    {
        "sqrt" or "exp" or "log" or "pow"
            or "asin" or "acos" or "atan" or "atan2" => true,
        _ => false,
    };

    /// <summary>The unit a function's answer is printed in. The sign of anything, the arithmetic and
    /// the circular functions are counts, the inverse circular functions are angles, and everything
    /// else answers in the unit it was given (measured: 'sign(1px)' prints 'calc(1)', 'atan2(-1,-1)'
    /// prints 'calc(-135deg)' and 'abs(-10px)' would print 'calc(10px)').</summary>
    private static string ResultUnit(string lower, string argumentUnit) => lower switch
    {
        "sign" or "sqrt" or "exp" or "log" or "pow"
            or "sin" or "cos" or "tan" => "",
        "asin" or "acos" or "atan" or "atan2" => "deg",
        _ => argumentUnit,
    };

    private const double DegreesPerRadian = 180.0 / System.Math.PI;

    /// <summary>The angle a trigonometric argument carries, in radians. A bare number is read as
    /// radians while an angle is converted (measured: 'cos(0.5)' is 'calc(0.877583)', which is the
    /// cosine of half a radian and not of half a degree, while 'sin(100grad)' is 'calc(1)').</summary>
    private static double Radians((double Value, string Unit) argument)
        => argument.Unit.Length == 0 ? argument.Value : argument.Value / DegreesPerRadian;

    /// <summary>The units whose value a comparison can be settled without being told the box, the
    /// font or the viewport: the pixels an absolute length is printed as, the degree an angle is
    /// printed as, the second a time is printed as and a plain number.</summary>
    private static readonly HashSet<string> AbsolutePrintedUnits = new(StringComparer.Ordinal)
    {
        "", "px", "deg", "s",
    };

    /// <summary><c>clamp(MIN, VAL, MAX)</c> is <c>max(min(VAL, MAX), MIN)</c> (CSS Values 4 §10.3),
    /// so the middle argument is the value and the other two only bound it.</summary>
    private static double ClampValue(List<(double Value, string Unit)> values)
        => System.Math.Max(System.Math.Min(values[1].Value, values[2].Value), values[0].Value);

    /// <summary>The name the engine prints for one of its math functions: the legacy spelling is
    /// the same value as the unprefixed one and so loses the prefix on the way out.</summary>
    private static string NameOf(string name)
        => name.Equals("-webkit-calc", StringComparison.OrdinalIgnoreCase) ? "calc" : NameLower(name);

    private static string NameLower(string name)
    {
        for (int i = 0; i < name.Length; i++)
            if (char.IsUpper(name[i])) return name.ToLowerInvariant();
        return name;
    }

    private static bool HasForeignTokens(string text)
        => text.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0
         || text.IndexOf("env(", StringComparison.OrdinalIgnoreCase) >= 0
         || text.IndexOf("attr(", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Whether the whole text is one foldable function call and nothing around it.
    /// 'clamp(1px, 2%, 3px)' is, '1px + clamp(2px, 3%)' is not — the name has to start the text and
    /// the closing paren has to end it.</summary>
    private static bool IsSingleCall(string text)
    {
        int open = text.IndexOf('(');
        if (open <= 0 || text.Length == 0 || text[text.Length - 1] != ')') return false;
        if (!IsIdentStart(text, 0)) return false;
        for (int i = 0; i < open; i++)
            if (!IsIdentPart(text[i])) return false;
        if (MatchingParen(text, open) != text.Length - 1) return false;
        return FoldableNames.Contains(text[..open].ToLowerInvariant());
    }

    private static string PrintTerm(double value, string unit)
        => (value < 0 ? "-" : "") + Number(System.Math.Abs(value)) + unit;

    // ------------------------------------------------------------------ the sum of an expression

    /// <summary>What an expression reduced to: the terms it could merge, keyed by the unit each is
    /// printed as, and the text of any function it had to carry along. A sum that runs into
    /// something it cannot read is broken, and a broken sum is not printed at all — the caller
    /// leaves the author's text standing.</summary>
    private sealed class Sum
    {
        public readonly Dictionary<string, double> Terms = new(StringComparer.Ordinal);
        public readonly List<(double Sign, string Text)> Opaque = new();
        public bool Broken;
        private string? _category;

        /// <summary>True when the sum says something: it has terms and nothing in it was
        /// unreadable.</summary>
        public bool Printable => !Broken && (Terms.Count > 0 || Opaque.Count > 0);

        public void Add(double coefficient, string unit)
        {
            var canonical = Canonical(unit);
            if (canonical == null) { Broken = true; return; }
            var (category, canonicalUnit, factor) = canonical.Value;
            // A percentage is a share of whatever the property measures, so it takes the category
            // of its neighbours and never gives one. Two real categories in one sum is no value.
            if (category != "percentage")
            {
                if (_category == null) _category = category;
                else if (_category != category) { Broken = true; return; }
            }
            Terms.TryGetValue(canonicalUnit, out var existing);
            Terms[canonicalUnit] = existing + coefficient * factor;
        }

        public void AddScaled(Sum other, double scale)
        {
            if (other.Broken) { Broken = true; return; }
            foreach (var (unit, value) in other.Terms) Add(scale * value, unit);
            foreach (var (sign, text) in other.Opaque) Opaque.Add((sign * scale, text));
        }

        public string Print()
        {
            var keys = Terms.Keys.ToList();
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var unit in keys)
            {
                var value = Terms[unit];
                if (double.IsNaN(value) || double.IsInfinity(value)) return "";
                Append(sb, value < 0, Number(System.Math.Abs(value)) + unit);
            }
            foreach (var (sign, text) in Opaque) Append(sb, sign < 0, text);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, bool negative, string term)
        {
            if (sb.Length == 0) sb.Append(negative ? "-" : "").Append(term);
            else sb.Append(negative ? " - " : " + ").Append(term);
        }
    }

    /// <summary>The category a unit belongs to, the unit it is printed as and the size of one of
    /// them in that unit. An angle is always printed in degrees and a time in seconds, whichever
    /// way the page spelled it, while a length that depends on a font or on the viewport keeps its
    /// own unit because its value is not a number yet. Null for a unit nothing here knows, which
    /// leaves the expression unfolded rather than guessed at.</summary>
    private static (string Category, string CanonicalUnit, double Factor)? Canonical(string unit)
        => unit switch
        {
            "" => ("number", "", 1),
            "%" => ("percentage", "%", 1),
            "px" => ("length", "px", 1),
            "em" => ("length", "em", 1),
            "rem" => ("length", "rem", 1),
            "ex" => ("length", "ex", 1),
            "ch" => ("length", "ch", 1),
            "ic" => ("length", "ic", 1),
            "lh" => ("length", "lh", 1),
            "rlh" => ("length", "rlh", 1),
            "cap" => ("length", "cap", 1),
            "vi" => ("length", "vi", 1),
            "svi" => ("length", "svi", 1),
            "lvi" => ("length", "lvi", 1),
            "dvi" => ("length", "dvi", 1),
            "vb" => ("length", "vb", 1),
            "svb" => ("length", "svb", 1),
            "lvb" => ("length", "lvb", 1),
            "dvb" => ("length", "dvb", 1),
            "vw" => ("length", "vw", 1),
            "svw" => ("length", "svw", 1),
            "lvw" => ("length", "lvw", 1),
            "dvw" => ("length", "dvw", 1),
            "vh" => ("length", "vh", 1),
            "svh" => ("length", "svh", 1),
            "lvh" => ("length", "lvh", 1),
            "dvh" => ("length", "dvh", 1),
            "vmin" => ("length", "vmin", 1),
            "svmin" => ("length", "svmin", 1),
            "lvmin" => ("length", "lvmin", 1),
            "dvmin" => ("length", "dvmin", 1),
            "vmax" => ("length", "vmax", 1),
            "svmax" => ("length", "svmax", 1),
            "lvmax" => ("length", "lvmax", 1),
            "dvmax" => ("length", "dvmax", 1),
            "fr" => ("flex", "fr", 1),
            "deg" => ("angle", "deg", 1),
            "grad" => ("angle", "deg", 0.9),
            "rad" => ("angle", "deg", 180.0 / System.Math.PI),
            "turn" => ("angle", "deg", 360.0),
            "s" => ("time", "s", 1),
            "ms" => ("time", "s", 0.001),
            // The absolute lengths all go through the inch and the inch is 96px, so they are
            // merged into the pixels they are worth — read from the one table every other length in
            // the engine is resolved against, so a fold and a layout can never disagree about it.
            _ when Length.TryAbsoluteUnitPixels(unit, out var pixelsPerUnit)
                => ("length", "px", pixelsPerUnit),
            _ => null,
        };

    // ------------------------------------------------------------------ typing one expression

    /// <summary>Whether every <c>+</c> and <c>-</c> that adds or subtracts in this expression text is
    /// written with whitespace on both sides of it (CSS Values 4 §10.2.1). The rule is not typography:
    /// a tokenizer reading <c>1px+1px</c> makes one signed dimension out of <c>+1px</c>, so what the
    /// page wrote is two values standing side by side with no operator between them, which is no
    /// expression and no value either (measured: the reference engine drops
    /// <c>width: calc(1px+1px)</c>, <c>calc(1px+ 1px)</c> and <c>calc(1px +1px)</c> alike and answers
    /// <c>false</c> for each through <c>supports()</c>). A sign that has nothing to take from — the
    /// front of an operand, after <c>*</c> or <c>/</c>, after a parenthesis or a comma — belongs to the
    /// number it stands in front of and is not an operator; <c>calc(1px * -2)</c> is a value.
    /// The scan skips strings, keeps an exponent inside its number, and treats a closing parenthesis
    /// as the end of the value it closes, so a nested call or a <c>var()</c> neither hides a sum nor
    /// asks for one.</summary>
    public static bool OperatorsAreSpaced(string text)
    {
        bool afterValue = false;
        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            if (c is '"' or '\'') { i = SkipQuoted(text, i); afterValue = true; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c is '+' or '-')
            {
                if (afterValue)
                {
                    bool spaceBefore = i > 0 && char.IsWhiteSpace(text[i - 1]);
                    bool spaceAfter = i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]);
                    if (!spaceBefore || !spaceAfter) return false;
                }
                i++;
                afterValue = false;
                continue;
            }
            if (c is '*' or '/' or '(' or ',')
            {
                i++;
                afterValue = false;
                continue;
            }
            if (c == ')') { i++; afterValue = true; continue; }
            if (char.IsAsciiDigit(c) || c == '.')
            {
                // The number, its exponent and its unit — and the unit scan stops at a sign, which
                // is what lets the operator test above see '1px+1px' as a sum written without spaces.
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.')) i++;
                if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
                {
                    int probe = i + 1;
                    if (probe < text.Length && text[probe] is '+' or '-') probe++;
                    int digits = probe;
                    while (probe < text.Length && char.IsAsciiDigit(text[probe])) probe++;
                    if (probe > digits) i = probe;
                }
                if (i < text.Length && text[i] == '%') i++;
                else while (i < text.Length && char.IsLetter(text[i])) i++;
                afterValue = true;
                continue;
            }
            if (IsIdentStart(text, i))
            {
                while (i < text.Length && IsIdentPart(text[i])) i++;
                afterValue = true;
                continue;
            }
            i++;
            afterValue = false;
        }
        return true;
    }

    /// <summary>The type an expression resolves to when one of its terms is somebody else's value —
    /// a <c>var()</c>, an <c>attr()</c>, a function this checker does not own. The declaration is
    /// accepted on the word of the page, which is what the reference engine does with
    /// <c>calc(1px + var(--x))</c> (measured).</summary>
    private const string TypedUnknown = "*";

    /// <summary>No type at all: the expression asks for something CSS does not define, such as a
    /// number added to a length.</summary>
    private const string TypedBad = "?";

    /// <summary>The type a math function or an arithmetic expression resolves to, decided by the
    /// rules CSS Values 4 §10.3 puts on an expression: the terms of a sum have to be values of one
    /// type, at most one factor of a product may carry a type, only a number may divide, and a
    /// percentage is a share of a length — never a share of a number. A function answers with the
    /// type of what it was given, so <c>abs(-1px)</c> is a length while <c>sign(1px)</c> is a number
    /// and <c>min(1px, 2)</c> is nothing at all (all measured through <c>supports()</c>).
    /// <paramref name="category"/> is null when the type cannot be named, which is the case that
    /// carries a variable; false is never one.</summary>
    public static bool WellTyped(string text, out string? category)
    {
        var typed = TypeOfExpression(text);
        category = typed == TypedBad || typed == TypedUnknown ? null : typed;
        return typed != TypedBad;
    }

    /// <summary>The type a math call or expression resolves to, or null when it names no type the
    /// engine can print — an ill-typed expression, or one that carries a variable.</summary>
    public static string? TypeOf(string text) => WellTyped(text, out var category) ? category : null;

    /// <summary>One math call, with where it stands in the text it was found in.</summary>
    public readonly record struct MathCall(int Start, int Length, string Name, string Argument);

    /// <summary>The math calls a value text carries, in the order they are written and only as far as
    /// they stand on their own: a <c>calc()</c> inside another math function belongs to that
    /// expression and is not reported separately, while one written under <c>inset()</c> or
    /// <c>minmax()</c> is a call of its own. A <c>url()</c> is skipped whole, because its characters
    /// are an address and not an expression.</summary>
    public static List<MathCall> MathCalls(string text)
    {
        var calls = new List<MathCall>();
        if (text.Length == 0 || text.IndexOf('(') < 0) return calls;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is '"' or '\'') { i = SkipQuoted(text, i); continue; }
            if (!IsIdentStart(text, i)) { i++; continue; }
            int start = i;
            while (i < text.Length && IsIdentPart(text[i])) i++;
            if (i >= text.Length || text[i] != '(') continue;
            int close = MatchingParen(text, i);
            if (close < 0) break;
            var name = text[start..i];
            if (name.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                i = close + 1;
                continue;
            }
            if (FoldableNames.Contains(name) || GrammarMathNames.Contains(name))
            {
                calls.Add(new MathCall(start, close + 1 - start, name.ToLowerInvariant(),
                    text[(i + 1)..close]));
                i = close + 1;
                continue;
            }
            i++;   // some other function: its arguments may still hold a math call
        }
        return calls;
    }

    /// <summary>The math names the value grammar validates, which the type checker reads as well so
    /// that a <c>round()</c> or a <c>hypot()</c> in a declaration is typed before it is stored.</summary>
    private static readonly HashSet<string> GrammarMathNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "calc", "-webkit-calc", "min", "max", "clamp", "round", "mod", "rem", "abs", "sign",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "pow", "sqrt", "hypot", "log", "exp",
    };

    private static int SkipQuoted(string text, int at)
    {
        char quote = text[at];
        for (int i = at + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == quote) return i + 1;
        }
        return text.Length;
    }

    /// <summary>The category name of one expression or one whole call, as <see cref="Canonical"/>
    /// names it: <c>number</c>, <c>percentage</c>, <c>length</c>, <c>angle</c>, <c>time</c>,
    /// <c>frequency</c>, <c>resolution</c> or <c>flex</c>, or one of the two markers above.</summary>
    private static string TypeOfExpression(string text) => new ExpressionType(text).Read();

    private static string TypeOfCall(string name, string argument)
    {
        switch (name)
        {
            case "calc":
            case "-webkit-calc":
                return TypeOfExpression(argument);
            case "min":
            case "max":
            case "hypot":
                {
                    // Every argument has to be of one type: 'min(1px, 2)' is no length and
                    // 'hypot(1px, 2)' is no length either (measured, both refused for 'width').
                    var args = SplitArguments(argument);
                    if (args.Count < 1) return TypedBad;
                    var category = TypeOfExpression(args[0]);
                    for (int i = 1; i < args.Count; i++)
                        category = Combine(category, TypeOfExpression(args[i]));
                    return category;
                }
            case "clamp":
                {
                    var args = SplitArguments(argument);
                    if (args.Count != 3) return TypedBad;
                    return Combine(TypeOfExpression(args[0]),
                        Combine(TypeOfExpression(args[1]), TypeOfExpression(args[2])));
                }
            case "round":
                {
                    // 'round( [strategy,] <value>, <resolution> )', and the resolution has to be of
                    // the value's own type: 'round(1px, 2px)' rounds a length while 'round(1px, 2)'
                    // multiplies a length by a number and is no value (measured, both).
                    var args = SplitArguments(argument);
                    int first = 0;
                    if (args.Count == 3)
                    {
                        if (!RoundStrategies.Contains(args[0].Trim().ToLowerInvariant())) return TypedBad;
                        first = 1;
                    }
                    else if (args.Count != 2) return TypedBad;
                    return Combine(TypeOfExpression(args[first]), TypeOfExpression(args[first + 1]));
                }
            case "mod":
            case "rem":
                {
                    var args = SplitArguments(argument);
                    if (args.Count != 2) return TypedBad;
                    // Both operands of a remainder share one type, so 'mod(10px, 3)' asks for a
                    // length over a number and is refused while 'rem(10px, 3px)' is a length
                    // (measured, both).
                    return Combine(TypeOfExpression(args[0]), TypeOfExpression(args[1]));
                }
            case "abs":
                {
                    var args = SplitArguments(argument);
                    return args.Count == 1 ? TypeOfExpression(args[0]) : TypedBad;
                }
            case "sign":
                {
                    // The sign of any value is a count, which is why 'calc(sign(1px))' is no length
                    // (measured).
                    var args = SplitArguments(argument);
                    if (args.Count != 1) return TypedBad;
                    var category = TypeOfExpression(args[0]);
                    return category == TypedBad ? TypedBad
                        : category == TypedUnknown ? TypedUnknown : "number";
                }
            case "sqrt":
            case "exp":
            case "log":
            case "pow":
                {
                    // Arithmetic on counts only: an argument of any other type is refused, and what
                    // comes back is a number — which a length-valued property then turns down
                    // (measured: 'sqrt(4)', 'sqrt(4px)', 'pow(2px, 2)', 'exp(1)' and 'log(1px)' all
                    // leave 'width' with no declaration).
                    var args = SplitArguments(argument);
                    if (args.Count != (name == "pow" ? 2 : 1)) return TypedBad;
                    foreach (var arg in args)
                    {
                        var category = TypeOfExpression(arg);
                        if (category == TypedBad) return TypedBad;
                        if (category == TypedUnknown) return TypedUnknown;
                        if (category != "number") return TypedBad;
                    }
                    return "number";
                }
            case "sin":
            case "cos":
            case "tan":
                {
                    // The circular functions take an angle — or a count, which is read as radians —
                    // and answer with a count, so 'calc(sin(30deg) * 100px)' is a length while
                    // 'calc(sin(30deg) + 1px)' is no value (measured, both).
                    var args = SplitArguments(argument);
                    if (args.Count != 1) return TypedBad;
                    var category = TypeOfExpression(args[0]);
                    return category is TypedBad or "length" or "percentage" or "time" ? TypedBad
                        : category == TypedUnknown ? TypedUnknown : "number";
                }
            case "asin":
            case "acos":
            case "atan":
            case "atan2":
                {
                    // The inverse functions work on counts and answer with an angle, which is why
                    // 'asin(0.5)' belongs to 'rotate' and 'asin(1deg)' to nothing (measured).
                    var args = SplitArguments(argument);
                    if (args.Count != (name == "atan2" ? 2 : 1)) return TypedBad;
                    bool unknown = false;
                    foreach (var arg in args)
                    {
                        var category = TypeOfExpression(arg);
                        if (category == TypedBad) return TypedBad;
                        if (category == TypedUnknown) unknown = true;
                        else if (category != "number") return TypedBad;
                    }
                    return unknown ? TypedUnknown : "angle";
                }
            default:
                return TypedUnknown;
        }
    }

    private static readonly HashSet<string> RoundStrategies = new(StringComparer.OrdinalIgnoreCase)
    { "nearest", "up", "down", "to-zero" };

    /// <summary>The type of a sum of two typed values. A percentage is a share of the value the
    /// property measures, so it joins a length and becomes one while it joins nothing else:
    /// <c>calc(1px + 1%)</c> is a length, <c>calc(1s + 100%)</c> and <c>calc(50% + 0)</c> are no
    /// value (measured, all three).</summary>
    private static string Combine(string left, string right)
    {
        if (left == TypedBad || right == TypedBad) return TypedBad;
        if (left == TypedUnknown || right == TypedUnknown) return TypedUnknown;
        if (left == right) return left;
        if (left == "length") return right == "percentage" ? "length" : TypedBad;
        if (left == "percentage") return right == "length" ? "length" : TypedBad;
        return TypedBad;
    }

    /// <summary>The recursive-descent type reader over one expression. It walks the same grammar as
    /// <see cref="Reader"/> but carries types instead of values, because the values here may be
    /// unknown: <c>calc(1px + var(--x))</c> has a type to give (a length, or nothing) and no number
    /// to give it with.</summary>
    private sealed class ExpressionType
    {
        private readonly string _text;
        private int _i;

        public ExpressionType(string text) { _text = text ?? ""; }

        public string Read()
        {
            var category = Sum();
            SkipSpace();
            // Anything left over is a token that no operator connects, which is no expression.
            return _i < _text.Length ? TypedBad : category;
        }

        private string Sum()
        {
            var left = Product();
            while (true)
            {
                SkipSpace();
                char operatorCharacter = Peek();
                if (operatorCharacter is not ('+' or '-')) return left;
                // The same whitespace rule the value reader applies, read off the text because the
                // product before the operator has already skipped over the space in front of it.
                if (!IsSpaceOrEnd(_i - 1) || !IsSpaceOrEnd(_i + 1)) return TypedBad;
                _i++;
                SkipSpace();
                left = Combine(left, Product());
            }
        }

        /// <summary>Whether this offset is whitespace or one of the two ends of the text.</summary>
        private bool IsSpaceOrEnd(int offset)
            => offset < 0 || offset >= _text.Length || char.IsWhiteSpace(_text[offset]);

        private string Product()
        {
            var category = Primary();
            bool typed = category != "number";
            while (true)
            {
                SkipSpace();
                char c = Peek();
                if (c is not ('*' or '/')) return category;
                bool divide = c == '/';
                _i++;
                SkipSpace();
                var next = Primary();
                if (next == TypedBad) return TypedBad;
                if (divide && next != "number" && next != TypedUnknown) return TypedBad;
                if (next == TypedUnknown) { category = TypedUnknown; typed = true; continue; }
                if (next == "number") continue;
                // Two factors of a type are no product: 'calc(10px * 20px)' and 'calc(10px * 20%)'
                // both go down (measured), and a typed value cannot divide a length either.
                if (typed || divide) return TypedBad;
                category = next;
                typed = true;
            }
        }

        private string Primary()
        {
            SkipSpace();
            char c = Peek();
            if (c == '\0') return TypedBad;
            if (c == '(')
            {
                int close = MatchingParen(_text, _i);
                if (close < 0) return TypedBad;
                var inner = TypeOfExpression(_text[(_i + 1)..close]);
                _i = close + 1;
                return inner;
            }
            if (IsIdentStart(_text, _i))
            {
                int start = _i;
                while (_i < _text.Length && IsIdentPart(_text[_i])) _i++;
                var word = _text[start.._i].ToLowerInvariant();
                if (Peek() != '(') return TypedBad;   // a bare identifier names no value
                int close = MatchingParen(_text, _i);
                if (close < 0) return TypedBad;
                var argument = _text[(_i + 1)..close];
                _i = close + 1;
                return TypeOfCall(word, argument);
            }
            if (c is '"' or '\'') return TypedUnknown;
            return Dimension();
        }

        private string Dimension()
        {
            int start = _i;
            if (Peek() is '+' or '-') _i++;
            int digits = _i;
            while (_i < _text.Length && char.IsAsciiDigit(_text[_i])) _i++;
            if (_i < _text.Length && _text[_i] == '.')
            {
                _i++;
                while (_i < _text.Length && char.IsAsciiDigit(_text[_i])) _i++;
            }
            if (_i == digits) { _i = start; return TypedBad; }
            if (_i < _text.Length && (_text[_i] == 'e' || _text[_i] == 'E'))
            {
                int probe = _i + 1;
                if (probe < _text.Length && _text[probe] is '+' or '-') probe++;
                int exponent = probe;
                while (probe < _text.Length && char.IsAsciiDigit(_text[probe])) probe++;
                if (probe > exponent) _i = probe;
            }
            string unit;
            if (Peek() == '%') { unit = "%"; _i++; }
            else
            {
                int unitStart = _i;
                while (_i < _text.Length && char.IsLetter(_text[_i])) _i++;
                unit = _text[unitStart.._i].ToLowerInvariant();
            }
            var canonical = Canonical(unit);
            return canonical == null ? TypedBad : canonical.Value.Category;
        }

        private char Peek() => _i < _text.Length ? _text[_i] : '\0';
        private void SkipSpace() { while (_i < _text.Length && char.IsWhiteSpace(_text[_i])) _i++; }
    }

    // ------------------------------------------------------------------ reading an expression

    private static bool TryReadSum(string text, out Sum? sum)
    {
        var reader = new Reader(text);
        sum = reader.ReadSum();
        return sum is not null && !sum.Broken && !reader.HasRest;
    }

    /// <summary>The arguments of a function, split on the commas that are not inside another
    /// function or a quote.</summary>
    private static List<string> SplitArguments(string inner)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (quote != 0)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < inner.Length) { sb.Append(inner[++i]); continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; sb.Append(c); continue; }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        parts.Add(sb.ToString().Trim());
        return parts;
    }

    /// <summary>One value read out of an expression: a plain number, a dimension, a percentage, a
    /// sum in its own right from inside parentheses or from a function that reduced to one, or the
    /// text of a function that did not.</summary>
    private readonly struct Primary
    {
        private readonly double _value;
        private readonly string _unit;
        private readonly Sum? _nested;
        private readonly string? _opaque;

        private Primary(double value, string unit, Sum? nested, string? opaque)
        {
            _value = value; _unit = unit; _nested = nested; _opaque = opaque;
        }

        public static Primary OfNumber(double value) => new(value, "", null, null);
        public static Primary OfDimension(double value, string unit) => new(value, unit, null, null);
        public static Primary OfSum(Sum sum) => new(0, "", sum, null);
        public static Primary OfFunction(string text) => new(0, "", null, text);

        public double Value => _value;
        public string Unit => _unit;
        public Sum? Nested => _nested;
        public string? Opaque => _opaque;
        public bool IsNumber => _nested == null && _opaque == null && _unit.Length == 0;

        /// <summary>True when the value is a count and so may scale or divide the typed factor of a
        /// product: a plain number, or a function that reduced to nothing but one — which is what
        /// lets 'calc(sin(30deg) * 100px)' be worked out as 'calc(50px)' (measured) rather than be
        /// carried as the product of two types.</summary>
        public bool IsCount => IsNumber
            || (_nested is { } nested && nested.Opaque.Count == 0
                && nested.Terms.Count == 1 && nested.Terms.ContainsKey(""));

        /// <summary>The count itself, valid when <see cref="IsCount"/> said it was one.</summary>
        public double CountValue => IsNumber ? _value : _nested!.Terms[""];
    }

    /// <summary>The recursive-descent reader over one expression. It is a small machine because a
    /// CSS math expression is a small grammar: a sum of products, a product being one typed value
    /// taken with any number of plain numbers, and a value being a number, a dimension, a
    /// percentage, a parenthesised sum or a function (CSS Values 4 §10.2).</summary>
    private sealed class Reader
    {
        private readonly string _text;
        private int _i;

        public Reader(string text) { _text = text; }

        public bool HasRest { get { SkipSpace(); return _i < _text.Length; } }

        public Sum? ReadSum()
        {
            var sum = new Sum();
            SkipSpace();
            double sign = 1;
            if (Peek() is '+' or '-') { sign = Peek() == '-' ? -1 : 1; _i++; SkipSpace(); }
            var first = ReadProduct();
            if (first == null) return null;
            sum.AddScaled(first, sign);
            while (true)
            {
                SkipSpace();
                char c = Peek();
                if (c is not ('+' or '-')) break;
                // An operator the page did not put whitespace around is not an operator: the
                // tokenizer reads '1px+1px' as two values side by side, so the expression — and with
                // it the declaration — is nothing, and a folder that quietly added the two together
                // would print a value the property never accepted (measured, and CSS Values 4 §10.2.1).
                // The space in front is read off the text rather than off the scanner's move, because
                // the product before the operator has already skipped over that space.
                if (!IsSpaceOrEnd(_i - 1) || !IsSpaceOrEnd(_i + 1)) return null;
                _i++;
                SkipSpace();
                var next = ReadProduct();
                if (next == null) return null;
                sum.AddScaled(next, c == '-' ? -1 : 1);
            }
            return sum;
        }

        /// <summary>Whether this offset is whitespace or one of the two ends of the text — the boundary
        /// a sum's operator has to have on each side of it.</summary>
        private bool IsSpaceOrEnd(int offset)
            => offset < 0 || offset >= _text.Length || char.IsWhiteSpace(_text[offset]);

        private Sum? ReadProduct()
        {
            var factors = new List<(Primary Primary, bool Divide)>();
            var head = ReadPrimary();
            if (head == null) return null;
            factors.Add((head.Value, false));
            while (true)
            {
                SkipSpace();
                char c = Peek();
                if (c is not ('*' or '/')) break;
                _i++;
                SkipSpace();
                var next = ReadPrimary();
                if (next == null) return null;
                factors.Add((next.Value, c == '/'));
            }

            var product = new Sum();
            double number = 1;
            Primary? typed = null;
            string? opaque = null;
            foreach (var (factor, divide) in factors)
            {
                if (factor.IsCount)
                {
                    double value = factor.CountValue;
                    // Only a number divides, and division by zero is not a value.
                    if (divide && value == 0) return null;
                    if (divide) number /= value;
                    else number *= value;
                    continue;
                }
                // A typed value cannot be a divisor, and two of them multiplied together are no
                // CSS value either, so an expression that asks for one is left as written.
                if (divide || typed != null || opaque != null) return null;
                if (factor.Opaque != null) opaque = factor.Opaque;
                else typed = factor;
            }

            if (opaque != null)
            {
                // A function the sum carries whole keeps its own text, so only a sign may be put
                // in front of it — a scale would have to be printed inside an expression this does
                // not own.
                if (number != 1 && number != -1) return null;
                product.Opaque.Add((number, opaque));
                return product;
            }
            if (typed == null) { product.Add(number, ""); return product; }
            if (typed.Value.Nested != null)
            {
                product.AddScaled(typed.Value.Nested!, number);
                return product;
            }
            product.Add(number * typed.Value.Value, typed.Value.Unit);
            return product;
        }

        private Primary? ReadPrimary()
        {
            SkipSpace();
            char c = Peek();
            if (c == '\0') return null;
            if (c is '+' or '-' && (PeekAt(1) == '(' || IsIdentStart(_text, _i + 1)))
            {
                _i++;
                var negated = ReadPrimary();
                if (negated == null) return null;
                if (negated.Value.Opaque != null)
                    return Primary.OfFunction((c == '-' ? "-" : "") + negated.Value.Opaque);
                if (negated.Value.Nested != null)
                {
                    var flipped = new Sum();
                    flipped.AddScaled(negated.Value.Nested, c == '-' ? -1 : 1);
                    return Primary.OfSum(flipped);
                }
                return Primary.OfDimension(c == '-' ? -negated.Value.Value : negated.Value.Value,
                    negated.Value.Unit);
            }
            if (c == '(')
            {
                _i++;
                var inner = ReadSum();
                SkipSpace();
                if (inner == null || Peek() != ')') return null;
                _i++;
                return Primary.OfSum(inner);
            }
            if (IsIdentStart(_text, _i))
            {
                int start = _i;
                while (_i < _text.Length && IsIdentPart(_text[_i])) _i++;
                var word = _text[start.._i];
                if (Peek() != '(') return null;
                int close = MatchingParen(_text, _i);
                if (close < 0) return null;
                var argument = Fold(_text[(_i + 1)..close]);
                _i = close + 1;
                if (!FoldableNames.Contains(word)) return null;
                var reduced = Reduce(word, argument);
                // What comes back is either the function still standing, which the sum carries as
                // a term of its own, or a calc() of it, whose terms the sum can merge with its own.
                if (reduced.StartsWith("calc(", StringComparison.Ordinal) && reduced.Length > 6
                    && TryReadSum(reduced[5..^1], out var calcSum))
                    return Primary.OfSum(calcSum!);
                return Primary.OfFunction(reduced);
            }
            return ReadDimension();
        }

        private Primary? ReadDimension()
        {
            int start = _i;
            if (Peek() is '+' or '-') _i++;
            int digits = _i;
            while (_i < _text.Length && char.IsAsciiDigit(_text[_i])) _i++;
            if (_i < _text.Length && _text[_i] == '.')
            {
                _i++;
                while (_i < _text.Length && char.IsAsciiDigit(_text[_i])) _i++;
            }
            if (_i == digits) { _i = start; return null; }
            // An exponent belongs to the number, so '1e2px' is read whole and not as '1e' plus '2px'.
            if (_i < _text.Length && (_text[_i] == 'e' || _text[_i] == 'E'))
            {
                int probe = _i + 1;
                if (probe < _text.Length && _text[probe] is '+' or '-') probe++;
                int exponentDigits = probe;
                while (probe < _text.Length && char.IsAsciiDigit(_text[probe])) probe++;
                if (probe > exponentDigits) _i = probe;
            }
            var number = _text[start.._i];
            string unit = "";
            if (_i < _text.Length && _text[_i] == '%') { unit = "%"; _i++; }
            else
            {
                int unitStart = _i;
                while (_i < _text.Length && char.IsLetter(_text[_i])) _i++;
                unit = _text[unitStart.._i].ToLowerInvariant();
            }
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return null;
            return unit.Length == 0 ? Primary.OfNumber(value) : Primary.OfDimension(value, unit);
        }

        private char Peek() => _i < _text.Length ? _text[_i] : '\0';
        private char PeekAt(int offset) => _i + offset < _text.Length ? _text[_i + offset] : '\0';
        private void SkipSpace() { while (_i < _text.Length && char.IsWhiteSpace(_text[_i])) _i++; }
    }

    // ------------------------------------------------------------------ scanning helpers

    private static int CopyQuoted(string text, int i, StringBuilder sb)
    {
        char quote = text[i];
        sb.Append(quote);
        i++;
        while (i < text.Length)
        {
            char c = text[i];
            sb.Append(c);
            i++;
            if (c == '\\' && i < text.Length) { sb.Append(text[i]); i++; continue; }
            if (c == quote) break;
        }
        return i;
    }

    private static int MatchingParen(string text, int open)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != 0)
            {
                if (c == '\\' && i + 1 < text.Length) i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') quote = c;
            else if (c == '(') depth++;
            else if (c == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    /// <summary>A CSS identifier starts at a letter or an underscore, and at a hyphen only when a
    /// letter, another hyphen or an escape follows it (CSS Syntax 4 §4.2) — which is what keeps a
    /// subtraction sign out of the name branch and lets '-webkit-calc' be read as one name.</summary>
    private static bool IsIdentStart(string text, int i)
    {
        char c = text[i];
        if (char.IsLetter(c) || c == '_') return true;
        return c == '-' && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] is '-' or '\\');
    }

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '\\';
}
