using System.Globalization;
using System.Text;
using Acrux.Core.Css.Tokenizer;

namespace Acrux.Core.Css;

/// <summary>
/// The value of a media feature, read the way CSS Media Queries 4 §10 writes its grammar: a
/// feature value is either ONE &lt;dimension-token&gt;/&lt;number-token&gt; or a math function
/// over them — nothing else. <see cref="Acrux.Core.Dom.Length"/> is deliberately lenient about a
/// dimension (it reads a number out of the front of a token and calls the rest a unit), which is
/// right for a declaration and wrong here: '446px - 2px' has to be no match at all rather than
/// zero pixels, and a sum written outside a 'calc()' has to stay a parse failure.
/// <para>
/// Inside the math functions the types are checked the way CSS Values 4 §10.8 checks them, so a
/// length plus a number, a length times a length and a percentage among lengths are all
/// unparseable (measured: '(min-width: calc(445px + 1))', '(min-width: clamp(446px, 1px * 1px))'
/// and '(min-width: calc(50% + 10px))' match nothing whatever the viewport is). The leaves are
/// measured against the media environment — font-relative units against the initial 16px,
/// viewport units against the query viewport — and 'calc()', 'min()', 'max()', 'clamp()' and
/// 'round()' are the functions the grammar admits.
/// </para>
/// </summary>
public static class MediaFeatureValue
{
    public enum Kind { Invalid, Number, Length, Resolution, Percentage, Other }

    /// <summary>An evaluated feature value: a kind, a magnitude in that kind's own unit (pixels,
    /// dots per pixel, or bare), and whether every leaf of it was absolute.</summary>
    public readonly struct Reading
    {
        public readonly Kind Type;
        public readonly double Value;
        /// <summary>False when a font-, viewport- or container-relative unit took part, which is
        /// what tells the serialiser that the expression cannot be reduced to one absolute term
        /// yet (measured: 'calc(444px + 1vw)' reads back as written while 'calc(100px * 4 + 45px)'
        /// reads back as 'calc(445px)').</summary>
        public readonly bool Absolute;

        public Reading(Kind type, double value, bool absolute)
        {
            Type = type;
            Value = value;
            Absolute = absolute;
        }

        public static readonly Reading Invalid = new(Kind.Invalid, 0, false);
        public bool Valid => Type != Kind.Invalid;
    }

    /// <summary>The length units a feature value may carry. Everything on the page can be a length
    /// and the query has to price it the same way, so the pixels come from the engine's one length
    /// parser; this table only decides what counts as a length at all, which is what makes a '1s'
    /// or a '1deg' a different type rather than zero pixels.</summary>
    private static readonly HashSet<string> LengthUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "px", "em", "rem", "ex", "rex", "ch", "ric", "ic", "lh", "rlh", "cap", "rcap",
        "vi", "vb", "vw", "vh", "vmin", "vmax", "dvw", "dvh", "svw", "svh", "lvw", "lvh",
        "cqw", "cqh", "cqi", "cqb", "cqmin", "cqmax",
    };

    private static readonly string[] MathFunctions = ["calc", "min", "max", "clamp", "round"];

    /// <summary>Whether a name is one of the math functions a media feature value may be built
    /// from. The media query range splitter needs the same test to call 'calc(500px)' a value.</summary>
    public static bool IsMathFunctionName(string name)
    {
        foreach (var f in MathFunctions)
            if (string.Equals(f, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private enum TokKind { Number, Dimension, Percentage, Ident, Func, Plus, Minus, Times, Divides, Comma, Open, Close, Bad }

    private sealed class Tok
    {
        public TokKind Kind;
        public double Number;
        public string Text = "";
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c > 0x7F;
    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c > 0x7F;
    private static bool IsDigitish(char c) => char.IsAsciiDigit(c) || c == '.';

    /// <summary>Whether the text at <paramref name="i"/> begins an ident-style token. A dash does
    /// so only when a name follows it — '-webkit-unit' and '--custom' are names, while a dash that
    /// stands alone or is followed by a space is the subtraction operator, which is what makes
    /// 'calc(446px - 2px)' a sum and 'calc(446px -2px)' two operands with nothing between them.
    /// Reading the bare dash as a name is what a plain "letter, '_' or '-'" test does, and it
    /// quietly turns every subtraction into a parse failure.</summary>
    private static bool StartsIdent(string text, int i)
    {
        if (i >= text.Length) return false;
        char c = text[i];
        if (IsIdentStart(c)) return true;
        if (c != '-') return false;
        if (i + 1 >= text.Length) return false;
        char next = text[i + 1];
        return IsIdentStart(next) || (next == '-' && i + 2 < text.Length && IsIdentChar(text[i + 2]));
    }

    /// <summary>The CSS tokenizer's rule for a sign inside a math expression: '-2px' is one
    /// dimension token, so 'calc(446px -2px)' is two operands with no operator between them and is
    /// not a sum at all — while '446px-2px' is one dimension whose unit is 'px-2px' (measured: both
    /// match nothing, and both read back exactly as they were written).</summary>
    private static List<Tok>? Tokenize(string text)
    {
        var toks = new List<Tok>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
                || (c is '+' or '-' && i + 1 < text.Length
                    && (char.IsAsciiDigit(text[i + 1]) || (text[i + 1] == '.' && i + 2 < text.Length
                        && char.IsAsciiDigit(text[i + 2])))))
            {
                int start = i;
                if (text[i] is '+' or '-') i++;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                if (i < text.Length && text[i] == '.')
                {
                    i++;
                    while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                }
                // '1e3px' is a thousand pixels, but '2em' is two 'em's: the exponent only wins when
                // digits follow it, with their own optional sign.
                if (i < text.Length && (text[i] is 'e' or 'E'))
                {
                    int probe = i + 1;
                    if (probe < text.Length && text[probe] is '+' or '-') probe++;
                    if (probe < text.Length && char.IsAsciiDigit(text[probe]))
                    {
                        i = probe;
                        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                    }
                }
                if (!double.TryParse(text[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return null;

                if (i < text.Length && IsIdentStart(text[i]))
                {
                    int unitStart = i;
                    while (i < text.Length && IsIdentChar(text[i])) i++;
                    toks.Add(new Tok { Kind = TokKind.Dimension, Number = number, Text = text[unitStart..i].ToLowerInvariant() });
                }
                else if (i < text.Length && text[i] == '%')
                {
                    i++;
                    toks.Add(new Tok { Kind = TokKind.Percentage, Number = number });
                }
                else toks.Add(new Tok { Kind = TokKind.Number, Number = number });
                continue;
            }

            if (StartsIdent(text, i))
            {
                int start = i;
                while (i < text.Length && IsIdentChar(text[i])) i++;
                var ident = text[start..i].ToLowerInvariant();
                if (i < text.Length && text[i] == '(')
                    toks.Add(new Tok { Kind = TokKind.Func, Text = ident });
                else toks.Add(new Tok { Kind = TokKind.Ident, Text = ident });
                continue;
            }

            switch (c)
            {
                case '+': toks.Add(new Tok { Kind = TokKind.Plus }); i++; break;
                case '-': toks.Add(new Tok { Kind = TokKind.Minus }); i++; break;
                case '*': toks.Add(new Tok { Kind = TokKind.Times }); i++; break;
                case '/': toks.Add(new Tok { Kind = TokKind.Divides }); i++; break;
                case ',': toks.Add(new Tok { Kind = TokKind.Comma }); i++; break;
                case '(': toks.Add(new Tok { Kind = TokKind.Open }); i++; break;
                case ')': toks.Add(new Tok { Kind = TokKind.Close }); i++; break;
                default: return null;
            }
        }
        return toks;
    }

    /// <summary>Evaluates a whole feature value, which the grammar makes either one token or one
    /// math function. A bare sum or product outside a function is not a value: the reference engine
    /// leaves it exactly as the page wrote it and matches nothing at any viewport.</summary>
    public static bool TryEvaluate(string? text, MediaQueryEnvironment env, out Reading reading)
    {
        reading = Reading.Invalid;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var toks = Tokenize(text.Trim());
        if (toks is null || toks.Count == 0) return false;

        var parser = new Parser(toks, env);
        var value = parser.ParseValue();
        if (!value.Valid) return false;
        reading = value;
        return true;
    }

    /// <summary>A &lt;length&gt; feature value in CSS pixels. One number is accepted where a length
    /// is asked for and nowhere else: a zero has no unit to be wrong about (CSS Values 4 §6.0), so
    /// '(min-width: 0)' and '(min-width: calc(2 - 2))' are questions about a threshold of nothing
    /// while '(min-width: 12)' is not a query at all, and the exception is strictly about zero —
    /// a percentage does not get the same pass, so '(min-width: 0%)' stays as invalid as
    /// '(min-width: 50%)' (all four measured).</summary>
    public static bool TryLength(string? text, MediaQueryEnvironment env, out double pixels)
    {
        pixels = double.NaN;
        if (!TryEvaluate(text, env, out var r)) return false;
        if (r.Type == Kind.Length) { pixels = r.Value; return true; }
        if (r.Type == Kind.Number && r.Value == 0) { pixels = 0; return true; }
        return false;
    }

    /// <summary>A &lt;resolution&gt; feature value in dots per CSS pixel. The feature is defined on
    /// a resolution, so a bare number is not a value for it (measured: '(min-resolution: 1)' matches
    /// nothing at any device pixel ratio while '(min-resolution: 1dppx)' matches at one).</summary>
    public static bool TryResolution(string? text, MediaQueryEnvironment env, out double dppx)
    {
        dppx = -1;
        if (!TryEvaluate(text, env, out var r) || r.Type != Kind.Resolution) return false;
        dppx = r.Value;
        return true;
    }

    /// <summary>A &lt;number&gt; feature value, which is what the three vendor
    /// '-webkit-*-device-pixel-ratio' spellings take: a dimension is not a number for them
    /// (measured: '(-webkit-min-device-pixel-ratio: 1)' matches at a device pixel ratio of one and
    /// '…: 1dppx)' does not) even though 'min-resolution' takes the same measurement the other way
    /// round.</summary>
    public static bool TryNumber(string? text, MediaQueryEnvironment env, out double number)
    {
        number = double.NaN;
        if (!TryEvaluate(text, env, out var r) || r.Type != Kind.Number) return false;
        number = r.Value;
        return true;
    }

    private sealed class Parser
    {
        private readonly List<Tok> _t;
        private readonly MediaQueryEnvironment _env;
        private int _i;

        public Parser(List<Tok> toks, MediaQueryEnvironment env)
        {
            _t = toks;
            _env = env;
        }

        private bool AtEnd => _i >= _t.Count;
        private Tok Current => AtEnd ? new Tok { Kind = TokKind.Bad } : _t[_i];

        public Reading ParseValue()
        {
            var first = Current;
            if (first.Kind == TokKind.Func)
            {
                if (!IsMathFunctionName(first.Text)) return Reading.Invalid;
                var value = ParseFunction();
                return AtEnd ? value : Reading.Invalid;
            }
            // One token, and nothing after it.
            if (first.Kind is not (TokKind.Number or TokKind.Dimension or TokKind.Percentage))
                return Reading.Invalid;
            _i++;
            var leaf = Leaf(first);
            return AtEnd ? leaf : Reading.Invalid;
        }

        private Reading ParseSum()
        {
            var left = ParseProduct();
            if (!left.Valid) return Reading.Invalid;
            while (Current.Kind is TokKind.Plus or TokKind.Minus)
            {
                bool subtract = Current.Kind == TokKind.Minus;
                _i++;
                var right = ParseProduct();
                if (!right.Valid) return Reading.Invalid;
                left = Combine(left, right, subtract);
                if (!left.Valid) return Reading.Invalid;
            }
            return left;
        }

        private Reading ParseProduct()
        {
            var left = ParseUnary();
            if (!left.Valid) return Reading.Invalid;
            while (Current.Kind is TokKind.Times or TokKind.Divides)
            {
                bool divide = Current.Kind == TokKind.Divides;
                _i++;
                var right = ParseUnary();
                if (!right.Valid) return Reading.Invalid;
                left = divide ? Divide(left, right) : Multiply(left, right);
                if (!left.Valid) return Reading.Invalid;
            }
            return left;
        }

        private Reading ParseUnary()
        {
            bool negate = false;
            while (Current.Kind is TokKind.Plus or TokKind.Minus)
            {
                negate ^= Current.Kind == TokKind.Minus;
                _i++;
            }
            var value = ParsePrimary();
            if (!value.Valid) return Reading.Invalid;
            return negate ? new Reading(value.Type, -value.Value, value.Absolute) : value;
        }

        private Reading ParsePrimary()
        {
            var tok = Current;
            switch (tok.Kind)
            {
                case TokKind.Number:
                case TokKind.Dimension:
                case TokKind.Percentage:
                    _i++;
                    return Leaf(tok);
                case TokKind.Func:
                    if (!IsMathFunctionName(tok.Text)) return Reading.Invalid;
                    return ParseFunction();
                case TokKind.Open:
                    _i++;
                    var inner = ParseSum();
                    if (!inner.Valid) return Reading.Invalid;
                    return ExpectClose() ? inner : Reading.Invalid;
                default:
                    return Reading.Invalid;
            }
        }

        private bool ExpectClose()
        {
            // A query whose last parenthesis was never closed still reaches the end of the feature
            // value, and the reference engine reads it as if it had been closed here (measured:
            // '(min-width: calc(0.5px + 444px)' matches a 445px viewport and reads back as
            // '(min-width: calc(444.5px))').
            if (AtEnd) return true;
            if (Current.Kind != TokKind.Close) return false;
            _i++;
            return true;
        }

        private Reading ParseFunction()
        {
            string name = Current.Text;
            _i++;
            if (Current.Kind != TokKind.Open) return Reading.Invalid;
            _i++;

            string? strategy = null;
            if (name == "round" && Current.Kind == TokKind.Ident && _i + 1 < _t.Count && _t[_i + 1].Kind == TokKind.Comma)
            {
                strategy = Current.Text;
                _i += 2;
            }

            var args = new List<Reading>();
            while (true)
            {
                var arg = ParseSum();
                if (!arg.Valid) return Reading.Invalid;
                args.Add(arg);
                if (Current.Kind == TokKind.Comma) { _i++; continue; }
                break;
            }
            if (!ExpectClose()) return Reading.Invalid;

            switch (name)
            {
                case "calc":
                    return args.Count == 1 ? args[0] : Reading.Invalid;
                case "min":
                case "max":
                {
                    if (args.Count == 0 || !SameType(args)) return Reading.Invalid;
                    double best = args[0].Value;
                    foreach (var a in args)
                        best = name == "min" ? Math.Min(best, a.Value) : Math.Max(best, a.Value);
                    return new Reading(args[0].Type, best, AllAbsolute(args));
                }
                case "clamp":
                {
                    if (args.Count != 3 || !SameType(args)) return Reading.Invalid;
                    double value = Math.Min(args[1].Value, args[2].Value);
                    return new Reading(args[0].Type, Math.Max(value, args[0].Value), AllAbsolute(args));
                }
                case "round":
                {
                    if (args.Count is < 2 or > 3) return Reading.Invalid;
                    var value = args[^2];
                    var granularity = args[^1];
                    if (value.Type == Kind.Number && granularity.Type != Kind.Number) return Reading.Invalid;
                    if (granularity.Type != Kind.Number && granularity.Type != value.Type) return Reading.Invalid;
                    strategy ??= "nearest";
                    if (!TryRoundStrategy(strategy, out var up)) return Reading.Invalid;
                    return new Reading(value.Type, RoundToMultiple(value.Value, granularity.Value, up),
                                       value.Absolute && granularity.Absolute);
                }
                default:
                    return Reading.Invalid;
            }
        }

        private static bool SameType(List<Reading> args)
        {
            foreach (var a in args)
                if (a.Type != args[0].Type || a.Type is Kind.Invalid or Kind.Other) return false;
            return true;
        }

        private static bool AllAbsolute(List<Reading> args)
        {
            foreach (var a in args) if (!a.Absolute) return false;
            return true;
        }

        /// <summary>The four strategies CSS Values 4 §10.9 gives 'round()'. 'up' and 'down' point at
        /// positive and negative infinity whatever the sign of the value, which is what makes
        /// 'round(down, -1.5px, 1px)' two pixels away from zero rather than one.</summary>
        private enum RoundStrategy { Nearest, Up, Down, ToZero }

        private static bool TryRoundStrategy(string name, out RoundStrategy strategy)
        {
            switch (name)
            {
                case "nearest": strategy = RoundStrategy.Nearest; return true;
                case "up": strategy = RoundStrategy.Up; return true;
                case "down": strategy = RoundStrategy.Down; return true;
                case "to-zero": strategy = RoundStrategy.ToZero; return true;
                default: strategy = RoundStrategy.Nearest; return false;
            }
        }

        private static double RoundToMultiple(double value, double multiple, RoundStrategy strategy)
        {
            if (multiple == 0) return double.NaN;
            double quotient = value / multiple;
            double rounded = strategy switch
            {
                RoundStrategy.Up => Math.Ceiling(quotient),
                RoundStrategy.Down => Math.Floor(quotient),
                RoundStrategy.ToZero => Math.Truncate(quotient),
                _ => Math.Round(quotient, MidpointRounding.AwayFromZero),
            };
            return rounded * multiple;
        }

        private Reading Leaf(Tok tok) => tok.Kind switch
        {
            TokKind.Number => new Reading(Kind.Number, tok.Number, true),
            TokKind.Percentage => new Reading(Kind.Percentage, tok.Number, false),
            _ => ClassifyDimension(tok.Number, tok.Text),
        };

        private Reading ClassifyDimension(double number, string unit)
        {
            if (unit == "dppx" || unit == "x") return new Reading(Kind.Resolution, number, true);
            if (unit == "dpi") return new Reading(Kind.Resolution, number / 96.0, true);
            if (unit == "dpcm" && Acrux.Core.Dom.Length.TryAbsoluteUnitPixels("cm", out var pixelsPerCm))
                return new Reading(Kind.Resolution, number / pixelsPerCm, true);

            if (!LengthUnits.Contains(unit) && !Acrux.Core.Dom.Length.TryAbsoluteUnitPixels(unit, out _))
                return new Reading(Kind.Other, number, true);

            // A relative leaf has to be priced the way the query prices it: the initial font for a
            // font-relative unit and the query viewport for a viewport one, with no element's font
            // allowed in.
            var text = number.ToString("R", CultureInfo.InvariantCulture) + unit;
            float px;
            using (Acrux.Core.Dom.FontUnitContext.Use(null))
                px = Acrux.Core.Dom.Length.Parse(text)
                    .ToPixels(Acrux.Core.Dom.Length.FontSizeMedium, Acrux.Core.Dom.Length.FontSizeMedium,
                              _env.ViewportWidth, _env.ViewportHeight);
            if (float.IsNaN(px)) return Reading.Invalid;

            bool absolute = unit == "px" || Acrux.Core.Dom.Length.TryAbsoluteUnitPixels(unit, out _);
            return new Reading(Kind.Length, px, absolute);
        }

        /// <summary>'calc(445px + 1)' is not a length: an addition needs two of the same kind, and a
        /// percentage is never one in a media feature (CSS Values 4 §10.8; CSS Media Queries 4 §6).</summary>
        private static Reading Combine(Reading left, Reading right, bool subtract)
        {
            if (left.Type != right.Type || left.Type is Kind.Invalid or Kind.Other) return Reading.Invalid;
            double value = subtract ? left.Value - right.Value : left.Value + right.Value;
            return new Reading(left.Type, value, left.Absolute && right.Absolute);
        }

        private static Reading Multiply(Reading left, Reading right)
        {
            bool absolute = left.Absolute && right.Absolute;
            if (left.Type == Kind.Number && right.Type == Kind.Number)
                return new Reading(Kind.Number, left.Value * right.Value, absolute);
            if (left.Type == Kind.Number && right.Type is not (Kind.Invalid or Kind.Other))
                return new Reading(right.Type, left.Value * right.Value, absolute);
            if (right.Type == Kind.Number && left.Type is not (Kind.Invalid or Kind.Other))
                return new Reading(left.Type, left.Value * right.Value, absolute);
            return Reading.Invalid;   // a length times a length is an area, which no feature measures
        }

        private static Reading Divide(Reading left, Reading right)
        {
            if (right.Type != Kind.Number || left.Type is Kind.Invalid or Kind.Other) return Reading.Invalid;
            return new Reading(left.Type, left.Value / right.Value, left.Absolute && right.Absolute);
        }
    }

    /// <summary>The unit a folded value is printed in: the reference engine keeps the kind of the
    /// dimension it was given, so a length comes back in pixels and a resolution in 'dppx'
    /// (measured: 'calc(1cm)' is 'calc(37.7953px)' and 'calc(96dpi)' is 'calc(1dppx)').</summary>
    private static string FoldSuffix(Kind kind) => kind switch
    {
        Kind.Length => "px",
        Kind.Resolution => "dppx",
        _ => "",
    };

    /// <summary>The canonical spelling of a whole media query — the string a MediaQueryList reads
    /// as its 'media' and a '@media' rule reads as its 'conditionText' and 'media.mediaText'.
    /// A reference engine does not echo what the page typed: the grammar is case-insensitive, so
    /// the canonical form has no capitals in it; a list separates with ', '; a comparison operator
    /// is spaced on both sides; a feature colon is followed by exactly one space; a ratio is
    /// written '1 / 2'; and a leading 'all and ' is dropped (measured: '(MIN-WIDTH: 400PX)' reads as
    /// '(min-width: 400px)', 'SCREEN' as 'screen', '(width>=400px)' as '(width >= 400px)',
    /// '(min-aspect-ratio:1/2)' as '(min-aspect-ratio: 1 / 2)', 'all and (min-width: 1px)' as
    /// '(min-width: 1px)'). On top of that spelling, every math function that reduces to one
    /// absolute term comes back folded into a 'calc()' of that term, and the numbers of the terms
    /// that do not are re-printed to six significant digits — the same way a property serialises
    /// them, which is why '445.0px' reads '445px'.
    ///
    /// A list is printed query by query, and a query that is absent is not dropped but written as
    /// the query the grammar makes of an empty medium, 'not all' (measured: ',,screen,,' reads as
    /// 'not all, not all, screen, not all, not all' and 'print,,' as 'print, not all, not all').
    /// Only a list that is empty all the way through reads as no text at all, which is a rule that
    /// applies to every medium rather than to none.</summary>
    public static string Canonicalize(string? query, MediaQueryEnvironment? env = null)
    {
        if (string.IsNullOrEmpty(query)) return "";
        query = StripComments(query!);
        var items = MediaQueryEvaluator.SplitQueryList(query!, keepEmpty: true);
        var environment = env ?? new MediaQueryEnvironment();
        if (items.Count == 0 || (items.Count == 1 && items[0].Trim().Length == 0)) return "";
        return string.Join(", ", items.Select(item => CanonicalQuery(item, environment)));
    }

    /// <summary>The text with its CSS comments taken out. CSS Syntax 3 §4 throws comments away
    /// before anything is parsed, and a media list shows that in two ways at once: the grammar is
    /// read from the commentless sequence, and a medium that is ONLY a comment is an absent medium
    /// rather than a bad one (measured: <c>media='/*x*/'</c> reads back as no text and no items,
    /// <c>media='screen, /*c*/ print'</c> as two items, <c>media='screen /*c*/ and (min-width: 1px)'</c>
    /// as one query with the comment and the space it stood on both gone). This therefore runs
    /// before the list is split into queries.</summary>
    public static string StripComments(string text)
    {
        if (text.IndexOf("/*", StringComparison.Ordinal) < 0) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                // A string carries its own escapes and is not a place a comment starts.
                int j = i + 1;
                while (j < text.Length && text[j] != c)
                    j += text[j] == '\\' ? 2 : 1;
                int length = Math.Min(j - i + 1, text.Length - i);
                sb.Append(text, i, length);
                i += length - 1;
                continue;
            }
            if (c != '/' || i + 1 >= text.Length || text[i + 1] != '*') { sb.Append(c); continue; }
            int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
            // An unterminated comment runs to the end of the medium, which is the one thing the
            // tokenizer and the parser agree on here.
            i = close < 0 ? text.Length : close + 1;
        }
        return sb.ToString();
    }

    /// <summary>One item of the list above, on its own.</summary>
    private static string CanonicalQuery(string item, MediaQueryEnvironment env)
    {
        if (item.Trim().Length == 0) return "not all";
        var text = NormalizeSpacing(item);
        // A text the media-query grammar does not read as a query is not a syntax error at this
        // level: the engine holds the query that is always false instead, and prints THAT (measured
        // in all three channels — a media list, an '@media' condition and 'matchMedia().media':
        // '123', 'print screen', '#bad', 'print and', 'not', 'all and' and a top-level 'or' after a
        // media type all read back 'not all'). A feature or type the engine merely does not know
        // stays as it was written, because Media Queries 4 §11 makes an unknown a legal part of a
        // query rather than a parse failure ('(bogus: 1)', 'aural', 'unknown-feature(min-width:1)').
        if (!IsMediaQuery(text, out var unclosed)) return "not all";
        return FoldMath(text, env) + new string(')', unclosed);
    }

    /// <summary>Whether the text is one media query (CSS Media Queries 4 §4), and how many of its
    /// parentheses the reference engine has to close on the way out. The two productions are the
    /// parenthesised condition, whose groups are joined by 'and' or by 'or' but never by both at the
    /// same level, and the media type with its 'and' groups — a top-level 'or' after a type is not a
    /// query at all (measured: 'screen and (min-width: 1px) or (max-width: 2px)' is 'not all'), which
    /// is why the two paths answer the same question with different permission. A type written as a
    /// function call ('bogus((((', 'unknown-feature(min-width: 1px)') is a media type the engine does
    /// not understand, kept whole and never completed; anything else that cannot start a query — a
    /// number, a hash, a string, a bare 'not' — is refused.</summary>
    public static bool IsMediaQuery(string? query, out int unclosed)
    {
        unclosed = 0;
        if (string.IsNullOrWhiteSpace(query)) return false;
        var tokens = QueryTokens(query!);
        if (tokens.Count == 0) return false;

        int i = 0;
        bool not = IsKeyword(tokens, 0, "not");
        bool only = IsKeyword(tokens, 0, "only");
        if (not || only) i = 1;
        if (i >= tokens.Count) return false;

        if (tokens[i].Type == CssTokenType.LeftParenthesisToken)
        {
            // A group opened straight after 'not' is the whole query: the engine reads 'not (g)' as
            // the negation of one condition and stops it from joining on to more (measured: 'not
            // (min-width: 1px)' is kept while 'not (min-width: 1px) and (max-width: 2px)' and
            // 'not (min-width: 1px) or (max-width: 2px)' are 'not all'). 'only' has no place in
            // front of a group at all.
            if (only) return false;
            if (not) return SkipGroup(tokens, i, ref unclosed) >= tokens.Count;
            return GroupsFollow(tokens, i, allowOr: true, allowNot: false, ref unclosed);
        }

        // The media-type production. A type the engine cannot read as a name is a general-enclosed
        // thing it holds without understanding, and it is never joined to anything.
        if (tokens[i].Type == CssTokenType.FunctionToken) return true;
        if (tokens[i].Type != CssTokenType.IdentToken) return false;
        // The words the at-rule grammars own are not available as media types: 'layer' is one, so
        // 'matchMedia("layer")' is 'not all' while 'layer(b)' — a type the engine cannot read, which
        // it keeps — is not (measured through the media list, an '@media' condition and a sheet's
        // 'media' attribute, all three the same).
        if (IsKeyword(tokens, i, "and") || IsKeyword(tokens, i, "or") ||
            IsKeyword(tokens, i, "not") || IsKeyword(tokens, i, "only") ||
            IsKeyword(tokens, i, "layer"))
            return false;
        i++;
        if (i >= tokens.Count) return true;
        // A type is joined to what follows by 'and' and by nothing else, and the word comes before
        // the first group — which is what makes 'print (min-width: 100px)' a bad query while
        // 'screen and (min-width: 100px)' is one. Inside this production a group may be negated,
        // which the parenthesised production above does not allow (measured: 'screen and not
        // (min-width: 1px)' is kept, '(min-width: 1px) and not (max-width: 2px)' is 'not all').
        if (!IsKeyword(tokens, i, "and")) return false;
        return GroupsFollow(tokens, i + 1, allowOr: false, allowNot: true, ref unclosed);
    }

    public static bool IsMediaQuery(string? query) => IsMediaQuery(query, out _);

    /// <summary>The groups that make up a media condition, starting at <paramref name="i"/>: each
    /// one a parenthesis, the next joined by 'and' (and, in the parenthesised production, by 'or').
    /// Every group has to be joined by the same word, which is what makes
    /// '(a) and (b) or (c)' two operators at one level and therefore no query (measured). A group's
    /// own contents are not examined here — an unknown feature inside one is legal and a group the
    /// page left open is closed by the serialiser (measured: '(min-width: 100px' reads back
    /// '(min-width: 100px)') — so this walks groups and the words between them.</summary>
    private static bool GroupsFollow(List<CssParserToken> tokens, int i, bool allowOr, bool allowNot, ref int unclosed)
    {
        bool any = false;
        string? joiner = null;
        while (i < tokens.Count)
        {
            if (!any)
            {
                if (allowNot && IsKeyword(tokens, i, "not")) i++;
                if (i >= tokens.Count || tokens[i].Type != CssTokenType.LeftParenthesisToken) return false;
                i = SkipGroup(tokens, i, ref unclosed);
                any = true;
                continue;
            }
            bool and = IsKeyword(tokens, i, "and");
            bool or = IsKeyword(tokens, i, "or");
            if (!and && !or) return false;
            if (or && !allowOr) return false;
            var word = and ? "and" : "or";
            if (joiner != null && joiner != word) return false;
            joiner = word;
            i++;
            if (allowNot && IsKeyword(tokens, i, "not")) i++;
            if (i >= tokens.Count || tokens[i].Type != CssTokenType.LeftParenthesisToken) return false;
            i = SkipGroup(tokens, i, ref unclosed);
        }
        return any;
    }

    /// <summary>The index just past the parenthesis group that starts at <paramref name="i"/>, with
    /// the groups it never closed counted into <paramref name="unclosed"/>. A function token opens a
    /// group of its own — the tokenizer gives 'calc(' as one token and its ')' as the next, so a
    /// feature value that is a math function would otherwise end the outer group at the wrong
    /// parenthesis (measured: '(min-width: calc(444px))' is a query and
    /// '((min-width: 1px)' is one too, closed once on the way out).</summary>
    private static int SkipGroup(List<CssParserToken> tokens, int i, ref int unclosed)
    {
        int depth = 0;
        for (; i < tokens.Count; i++)
        {
            if (tokens[i].Type is CssTokenType.LeftParenthesisToken or CssTokenType.FunctionToken) depth++;
            else if (tokens[i].Type == CssTokenType.RightParenthesisToken && --depth == 0) return i + 1;
        }
        unclosed += depth;
        return tokens.Count;
    }

    private static bool IsKeyword(List<CssParserToken> tokens, int i, string keyword) =>
        i < tokens.Count && tokens[i].Type == CssTokenType.IdentToken &&
        string.Equals(tokens[i].Value, keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>The text as CSS tokens, with comments and whitespace already taken out — the
    /// sequence the media-query grammar is read from (CSS Media Queries 4 §4). The tokenizer hands
    /// comments back only to a caller that asks for them, and it hands whitespace back to every
    /// caller, which is why the separator between a group and the 'and' after it is dropped here:
    /// the grammar never sees it.</summary>
    private static List<CssParserToken> QueryTokens(string text)
    {
        var tokens = new List<CssParserToken>();
        var tokenizer = new Acrux.Core.Css.Tokenizer.CssTokenizer(text);
        while (true)
        {
            var token = tokenizer.TokenizeSingle();
            if (token.Type == CssTokenType.EofToken) break;
            if (token.Type == CssTokenType.WhitespaceToken) continue;
            tokens.Add(token);
        }
        return tokens;
    }

    /// <summary>The spacing rules above, applied in the order the reference engine's own output
    /// shows them. Each pass is deliberately separate — 'calc( 446px   -   2px )' has to be
    /// collapsed before the parentheses are tightened, and the operator pass can itself introduce
    /// the double space the final collapse removes.</summary>
    private static string NormalizeSpacing(string query)
    {
        var s = CollapseSpaces(query).Trim();
        s = DropSpaceAtParens(s);
        s = SpaceOperators(s);
        s = SpaceListSeparators(s);
        s = SpaceColons(s);
        s = SpaceRatios(s);
        s = CollapseSpaces(s).Trim();
        if (s.StartsWith("all and ", StringComparison.OrdinalIgnoreCase)) s = s["all and ".Length..];
        return s.ToLowerInvariant();
    }

    private static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool pending = false;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                // A run at the very start or the very end is dropped, which is the 'trim()' the
                // reference engine's own collapse pass ends with.
                if (sb.Length > 0) pending = true;
                continue;
            }
            if (pending) { sb.Append(' '); pending = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string DropSpaceAtParens(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            // A run of spaces is kept only when neither neighbour is a parenthesis; a parenthesis
            // swallows the run on its side of it ('( min-width: 1px )' → '(min-width: 1px)').
            if (s[i] != ' ') { sb.Append(s[i]); continue; }
            int j = i;
            while (j < s.Length && s[j] == ' ') j++;
            bool afterOpen = sb.Length > 0 && sb[^1] == '(';
            bool beforeClose = j < s.Length && s[j] == ')';
            if (!afterOpen && !beforeClose) sb.Append(' ');
            i = j - 1;
        }
        return sb.ToString();
    }

    private static string SpaceOperators(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '<' or '>')
            {
                bool paired = i + 1 < s.Length && s[i + 1] == '=';
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                sb.Append(c);
                if (paired) { sb.Append('='); i++; }
                sb.Append(' ');
                continue;
            }
            if (c == '=' && i + 1 < s.Length && (s[i + 1] == '=' || s[i + 1] == '<' || s[i + 1] == '>'))
            {
                // The second half of '<=' / '>=', already emitted.
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string SpaceListSeparators(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        foreach (char c in s)
        {
            if (c != ',') { sb.Append(c); continue; }
            while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
            sb.Append(", ");
        }
        return sb.ToString();
    }

    private static string SpaceColons(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != ':') { sb.Append(c); continue; }
            // The name in front of the colon is read back over any spaces the page put between it
            // and the ':', because those spaces go with the colon, not with the name.
            int tail = sb.Length - 1;
            while (tail >= 0 && sb[tail] == ' ') tail--;
            char before = tail >= 0 ? sb[tail] : '\0';
            if (!(char.IsLetterOrDigit(before) || before == '_' || before == '-')) { sb.Append(c); continue; }
            sb.Length = tail + 1;
            int next = i + 1;
            while (next < s.Length && char.IsWhiteSpace(s[next])) next++;
            // The space after the ':' belongs to a value, and a feature with no value at all is
            // printed with nothing after the colon (measured: '(min-width:)' reads back
            // '(min-width:)', not '(min-width: )').
            if (next >= s.Length || s[next] == ')') { sb.Append(':'); i = next - 1; continue; }
            sb.Append(": ");
            while (i + 1 < s.Length && char.IsWhiteSpace(s[i + 1])) i++;
        }
        return sb.ToString();
    }

    private static string SpaceRatios(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] != ':') { sb.Append(s[i]); i++; continue; }
            int probe = i + 1;
            while (probe < s.Length && s[probe] == ' ') probe++;
            int slash = FindRatioSlash(s, probe);
            if (slash < 0) { sb.Append(s[i]); i++; continue; }
            string left = s[probe..slash].Trim();
            int right = slash + 1;
            while (right < s.Length && s[right] == ' ') right++;
            int end = right;
            while (end < s.Length && (char.IsAsciiDigit(s[end]) || s[end] == '.')) end++;
            string rightSide = s[right..end];
            if (left.Length > 0 && rightSide.Length > 0)
            {
                sb.Append(": ").Append(left).Append(" / ").Append(rightSide);
                i = end;
                continue;
            }
            sb.Append(s[i]);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>The slash of a 'N / M' that begins at <paramref name="start"/>, or -1. A slash in
    /// a division ('calc(445px / 2)') is followed by a unit, so it is not a ratio either — but both
    /// spellings come out with the same spacing, which is why this only has to find the slash.</summary>
    private static int FindRatioSlash(string s, int start)
    {
        int i = start;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.')) i++;
        while (i < s.Length && s[i] == ' ') i++;
        return i < s.Length && s[i] == '/' ? i : -1;
    }

    /// <summary>The folding pass over an already-canically-spaced query: see
    /// <see cref="Canonicalize"/>.</summary>
    private static string FoldMath(string query, MediaQueryEnvironment env)
    {
        if (string.IsNullOrEmpty(query)) return query;
        var sb = new StringBuilder(query.Length + 8);
        int i = 0;
        while (i < query.Length)
        {
            char c = query[i];

            if (TryLexDimension(query, ref i, out var number, out var unit, out var isPercentage))
            {
                sb.Append(Acrux.Core.Dom.Animations.CssValueTokenizer.Num(number));
                if (isPercentage) sb.Append('%');
                else sb.Append(unit);
                continue;
            }

            int start = i;
            if (IsIdentStart(c))
            {
                while (i < query.Length && IsIdentChar(query[i])) i++;
                var ident = query[start..i];
                if (i < query.Length && query[i] == '(' && IsMathFunctionName(ident))
                {
                    int close = MatchingParen(query, i);
                    var call = close > 0 ? query[start..close] : query[start..];
                    sb.Append(Folded(call, env) ?? call);
                    i = close > 0 ? close : query.Length;
                    continue;
                }
                sb.Append(ident);
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>The folded spelling of one math function call, or null when it stays as written:
    /// a value that keeps a relative unit, a value whose type no feature can use and a value that
    /// does not parse are all printed the way the page wrote them (measured: 'calc(27.8em + 0px)',
    /// 'calc(445px + 1)' and 'calc(446px-2px)' all read back verbatim).</summary>
    private static string? Folded(string call, MediaQueryEnvironment env)
    {
        if (!TryEvaluate(call, env, out var r)) return null;
        if (!r.Absolute || r.Type is not (Kind.Length or Kind.Resolution or Kind.Number)) return null;
        var suffix = FoldSuffix(r.Type);
        if (double.IsPositiveInfinity(r.Value)) return $"calc(infinity{SuffixStar(suffix)})";
        if (double.IsNegativeInfinity(r.Value)) return $"calc(-infinity{SuffixStar(suffix)})";
        if (double.IsNaN(r.Value)) return null;
        return $"calc({Acrux.Core.Dom.Animations.CssValueTokenizer.Num(r.Value)}{suffix})";
    }

    /// <summary>An infinite value is not a number, so it is printed as the product the reference
    /// engine prints: 'infinity * 1px' (measured: 'calc(445px / 0)' reads back as
    /// '(min-width: calc(infinity * 1px))').</summary>
    private static string SuffixStar(string suffix) => suffix.Length == 0 ? "" : $" * 1{suffix}";

    /// <summary>Cuts the balanced parenthesis group that starts at <paramref name="open"/> out of
    /// the text, returning the index just past it, or -1 when it is never closed.</summary>
    private static int MatchingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0) return i + 1;
            }
        }
        return -1;
    }

    /// <summary>Reads a number, a dimension or a percentage at <paramref name="i"/>, leaving it
    /// just past the token. A '-webkit-…' feature name is not a number however it starts, so the
    /// sign only makes a number when a digit follows it.</summary>
    private static bool TryLexDimension(string text, ref int i, out double number, out string unit, out bool isPercentage)
    {
        number = 0;
        unit = "";
        isPercentage = false;
        int start = i;
        if (i < text.Length && text[i] is '+' or '-' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])) i++;
        if (i >= text.Length || !char.IsAsciiDigit(text[i])) { i = start; return false; }
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        }
        if (i < text.Length && text[i] is 'e' or 'E')
        {
            int probe = i + 1;
            if (probe < text.Length && text[probe] is '+' or '-') probe++;
            if (probe < text.Length && char.IsAsciiDigit(text[probe]))
            {
                i = probe;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            }
        }
        if (!double.TryParse(text[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            i = start;
            return false;
        }
        if (i < text.Length && IsIdentStart(text[i]))
        {
            int unitStart = i;
            while (i < text.Length && IsIdentChar(text[i])) i++;
            unit = text[unitStart..i];
            return true;
        }
        if (i < text.Length && text[i] == '%') { i++; isPercentage = true; return true; }
        return true;
    }
}
