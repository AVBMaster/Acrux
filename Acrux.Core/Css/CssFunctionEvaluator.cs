using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Acrux.Core.Dom;

namespace Acrux.Core.Css;

public static class CssFunctionEvaluator
{
    private static readonly Regex CalcFuncRegex = new(@"calc\s*\(", RegexOptions.IgnoreCase);
    private static readonly Regex VarRegex = new(@"var\s*\(--([^,)]+)(?:,\s*([^)]+))?\)", RegexOptions.IgnoreCase);
    private static readonly Regex AttrRegex = new(@"attr\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex FitContentRegex = new(@"fit-content\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex CounterRegex = new(@"counter\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex CountersRegex = new(@"counters\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex LinearGradRegex = new(@"linear-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RadialGradRegex = new(@"radial-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex ConicGradRegex = new(@"conic-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingLinearGradRegex = new(@"repeating-linear-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingRadialGradRegex = new(@"repeating-radial-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingConicGradRegex = new(@"repeating-conic-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex NumberRegex = new(@"^[+-]?\d+(\.\d+)?");
    private static readonly Regex UnitRegex = new(@"^[a-z%]+", RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> _customProperties = new(StringComparer.OrdinalIgnoreCase);

    public static void SetCustomProperty(string name, string value)
    {
        _customProperties[name] = value;
    }

    public static string? GetCustomProperty(string name)
    {
        return _customProperties.GetValueOrDefault(name);
    }

    public static void ClearCustomProperties()
    {
        _customProperties.Clear();
    }

    /// <param name="property">The name the value is being applied under. CSS Grid 1 §7.2.3: a track
    /// list keeps its <c>fit-content()</c> function through the cascade — the layout is what prices
    /// it (measured: 'grid-auto-rows: fit-content(10px) 20px' computes exactly what was written) —
    /// and the rule is asked here rather than at each call site, because the cascade has two of
    /// those and a rule a caller can forget is a rule that gets forgotten.</param>
    /// <summary>Whether a whole value is one call of a math function. The opacity and length
    /// properties of CSS Fill and Stroke 3 accept a math expression where a number would go, and
    /// they print it as written rather than folding it, so the first question is what kind of text
    /// the value is at all.</summary>
    public static bool IsMathFunctionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (!trimmed.EndsWith(")", StringComparison.Ordinal)) return false;
        int open = trimmed.IndexOf('(');
        if (open <= 0) return false;
        return trimmed[..open].Trim().ToLowerInvariant() switch
        {
            "calc" or "min" or "max" or "clamp" or "round" or "mod" => true,
            _ => false,
        };
    }

    /// <summary>A math expression whose value is known without a box: the expression is evaluated
    /// and printed as the number it means, and anything with a percentage or a unit in it comes
    /// back unchanged because no engine can price it without one (measured: 'fill-opacity:
    /// calc(0.5)' computes '0.5').</summary>
    public static string EvaluateConstantNumber(string text)
    {
        if (!IsMathFunctionText(text)) return text;
        var inner = text.Trim();
        int open = inner.IndexOf('(');
        var body = inner[(open + 1)..^1].Trim();
        if (inner[..open].Trim().ToLowerInvariant() != "calc") return text;
        if (body.IndexOf('%') >= 0 || body.IndexOfAny("emexnpx".ToCharArray()) < 0)
        {
            // Only a plain arithmetic expression of bare numbers is a constant here.
            if (body.IndexOfAny("emexnpx".ToCharArray()) >= 0) return text;
            double total = 0; int sign = 1; bool any = false; var current = "";
            void Flush()
            {
                if (current.Length == 0) return;
                if (double.TryParse(current.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value))
                { total += sign * value; any = true; }
                else { any = false; }
                current = "";
            }
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c is '+' or '-') { Flush(); sign = c == '-' ? -1 : 1; }
                else current += c;
            }
            Flush();
            if (any) return Acrux.Core.Dom.Animations.CssValueTokenizer.Num(total);
        }
        return text;
    }

    /// <summary>A math expression over bare numbers, printed with the unit the property will give
    /// them: 'stroke-width' is a length, so 'calc(2)' computes 'calc(2px)' (measured) — the
    /// expression stays an expression, only its numbers gain the unit.</summary>
    public static string NumberlessMathToPixels(string text)
    {
        if (!IsMathFunctionText(text)) return text;
        var trimmed = text.Trim();
        int open = trimmed.IndexOf('(');
        var head = trimmed[..(open + 1)];
        var body = trimmed[(open + 1)..^1];
        if (body.IndexOf('%') >= 0 || body.IndexOfAny("emexnpx".ToCharArray()) >= 0) return text;
        var printed = System.Text.RegularExpressions.Regex.Replace(body,
            @"(\d+(?:\.\d+)?)(?![\w.])", "$1px");
        return head + printed + ")";
    }

    public static string Evaluate(string value, Element? context = null, float parentFontSize = 16, float rootFontSize = 16, float viewportWidth = 0, float viewportHeight = 0, int maxRecursion = 10, bool forceMath = false, string? property = null)
    {
        if (string.IsNullOrEmpty(value) || maxRecursion <= 0) return value;

        var result = value;

        result = EvaluateVar(result, context, parentFontSize, rootFontSize, viewportWidth, viewportHeight, maxRecursion);
        result = EvaluateAttr(result, context);
        result = EvaluateCalcAndMath(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        // A percentage inside a math expression is a share of the box, which is not known here, so
        // the passes that would price one at the font go the way of the expression they read: the
        // layout owns them.
        if (forceMath || !HasMathFunctionWithPercent(result))
        {
            if (!IsTrackListProperty(property))
                result = EvaluateFitContent(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            result = EvaluateCounter(result, context);
            result = EvaluateCounters(result, context);
        }

        return result;
    }

    /// <summary>The cascade's substitutions with no arithmetic: what a <c>var()</c> stands for and
    /// what an <c>attr()</c> carries are resolved, while a math expression is left exactly as the
    /// page wrote it. A property whose computed value keeps the expression — 'stroke-width' is a
    /// length, so <c>calc(2)</c> reads back <c>calc(2px)</c> rather than <c>2px</c> (measured) — has
    /// to be handed the expression itself, because a number priced here cannot be turned back into
    /// one.</summary>
    public static string EvaluateSubstitutionsOnly(string value, Element? context = null,
        float parentFontSize = 16, float rootFontSize = 16, float viewportWidth = 0,
        float viewportHeight = 0, int maxRecursion = 10)
    {
        if (string.IsNullOrEmpty(value) || maxRecursion <= 0) return value;
        var result = EvaluateVar(value, context, parentFontSize, rootFontSize,
            viewportWidth, viewportHeight, maxRecursion);
        return EvaluateAttr(result, context);
    }

    /// <summary>The cascade's read of <c>content</c>: substitutions that the property's own value
    /// owns are resolved — <c>var()</c> and <c>attr()</c>, the second because a page that asks for
    /// an attribute gets the attribute's value as the string it becomes (measured:
    /// <c>content: attr(data-a)</c> on an element with <c>data-a="V"</c> computes <c>"V"</c>, and
    /// with no such attribute its fallback, <c>""</c> when there is none) — while a counter stays
    /// the function the page wrote. The reference engine's computed value echoes
    /// <c>counter()</c>/<c>counters()</c> unevaluated (measured: <c>counter(x)</c> reads
    /// <c>counter(x)</c>, not <c>0</c>), and the text a generated box actually paints is built from
    /// the same declaration by the layout, which owns the counter scope
    /// (<see cref="Layout.LayoutEngine.BuildPseudoElement"/>). Evaluating a counter here would
    /// answer a script with a number the page never wrote and would do it before the counters had
    /// been counted.</summary>
    public static string EvaluateForContent(string value, Element? context,
        float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var result = EvaluateVar(value, context, parentFontSize, rootFontSize,
            viewportWidth, viewportHeight);
        return EvaluateContentAttr(result, context);
    }

    /// <summary><c>attr()</c> inside <c>content</c>: the attribute's value becomes a STRING of the
    /// content list, not bare text, and a missing attribute becomes the fallback the page wrote or
    /// the empty string (measured: <c>attr(data-a)</c> with <c>data-a="V"</c> reads
    /// <c>"V"</c>, and with no such attribute <c>""</c>). The generic attr pass substitutes the
    /// value itself, which is right for <c>width: attr(w px)</c> and wrong here.</summary>
    private static string EvaluateContentAttr(string value, Element? context)
    {
        if (string.IsNullOrEmpty(value)
            || value.IndexOf("attr(", StringComparison.OrdinalIgnoreCase) < 0) return value;
        var sb = new System.Text.StringBuilder(value.Length + 16);
        int i = 0;
        while (i < value.Length)
        {
            int at = value.IndexOf("attr(", i, StringComparison.OrdinalIgnoreCase);
            if (at < 0) { sb.Append(value, i, value.Length - i); break; }
            sb.Append(value, i, at - i);
            int close = MatchingParen(value, at + 4);
            if (close < 0) { sb.Append(value, at, value.Length - at); break; }
            var inner = value[(at + 5)..close];
            // 'attr( <name> [ <attr-type> ]? , <fallback> )' (CSS Values 5 §2.2.2): the name is the
            // first word and a type written beside it is nobody's name, while the fallback after the
            // comma is what a missing attribute becomes (measured: 'attr(data-x type(string), "y")'
            // on an element with no such attribute reads '"y"').
            int comma = inner.IndexOf(',');
            var head = (comma < 0 ? inner : inner[..comma]).Trim();
            var space = head.IndexOfAny(new[] { ' ', '\t' });
            var name = space > 0 ? head[..space] : head;
            var attribute = context?.GetAttribute(name);
            if (!string.IsNullOrEmpty(attribute))
                sb.Append('"').Append(attribute).Append('"');
            else if (comma >= 0)
                sb.Append(AttrFallbackText(inner[(comma + 1)..]));
            else
                sb.Append("\"\"");
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>The computed form of an <c>attr()</c> fallback: a run of quoted strings joined into
    /// the one string they say (measured: 'attr(x, "y" "z")' reads '"yz"').</summary>
    private static string AttrFallbackText(string fallback)
    {
        var words = (fallback ?? string.Empty).Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "\"\"";
        var text = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            var quote = word[0];
            if (word.Length < 2 || quote is not ('"' or '\'') || word[^1] != quote) return "\"\"";
            text.Append(Acrux.Core.Css.Resolver.CssPropertyApplier.UnescapeCssString(word[1..^1]));
        }
        return "\"" + text + "\"";
    }

    /// <summary>The index of the ')' closing the parenthesis that starts at
    /// <paramref name="open"/>, or -1. Nested calls — 'attr(x, attr(y))' — belong to their own
    /// argument list.</summary>
    private static int MatchingParen(string text, int open)
    {
        int depth = 0;
        for (int j = open; j < text.Length; j++)
        {
            if (text[j] == '(') depth++;
            else if (text[j] == ')' && --depth == 0) return j;
        }
        return -1;
    }

    /// <summary>True when a calc/min/max/clamp expression in the value contains a
    /// '%' token before its closing parenthesis.</summary>
    public static bool HasMathFunctionWithPercent(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('%')) return false;
        string[] funcs = { "calc(", "min(", "max(", "clamp(" };
        for (int i = 0; i < value.Length; i++)
        {
            foreach (var f in funcs)
            {
                if (i + f.Length <= value.Length &&
                    value.AsSpan(i, f.Length).Equals(f.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    int depth = 1;
                    int j = i + f.Length;
                    for (; j < value.Length && depth > 0; j++)
                    {
                        if (value[j] == '(') depth++;
                        else if (value[j] == ')') depth--;
                        else if (value[j] == '%' && depth > 0) return true;
                    }
                    i = j;
                    break;
                }
            }
        }
        return false;
    }

    /// <summary>Substitute every var() in the text. The call is found by a parenthesis walk rather
    /// than by a pattern because a fallback is a value in its own right and may be another call:
    /// 'var(--red, var(--undef))' has one closing parenthesis more than a flat pattern counts, and
    /// reading the fallback up to the FIRST ')' hands the property 'var(--undef' — a text no property
    /// has a value for (measured: the reference engine answers the fallback only when the name is
    /// the one that is unanswered; rows 9 and 10 of _probe_b265_p.html are both green).</summary>
    private static string EvaluateVar(string value, Element? context, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight, int maxRecursion = 10)
    {
        if (string.IsNullOrEmpty(value)
            || value.IndexOf("var", StringComparison.OrdinalIgnoreCase) < 0
            || maxRecursion <= 0)
            return value;

        var sb = new System.Text.StringBuilder(value.Length + 16);
        int i = 0;
        while (i < value.Length)
        {
            int at = FindVarCall(value, i, out int open);
            if (at < 0)
            {
                sb.Append(value, i, value.Length - i);
                break;
            }
            int close = MatchingClose(value, open);
            if (close < 0)
            {
                // A call whose parenthesis never closes is not a value of anything; leaving the text
                // as written lets the property's own parse refuse it, which is what the reference
                // engine ends up doing too.
                sb.Append(value, i, value.Length - i);
                break;
            }
            sb.Append(value, i, at - i);
            var argument = value[(open + 1)..close];
            int comma = TopLevelComma(argument);
            var name = (comma < 0 ? argument : argument[..comma]).Trim();
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                // var() takes a custom property name and nothing else; 'var(color)' is not a value,
                // and the text stays as written so the property that got it says so.
                sb.Append(value, at, close + 1 - at);
                i = close + 1;
                continue;
            }
            var resolved = ResolveVar(name[2..], context);
            bool answered = resolved != null && resolved.Trim().Length > 0;
            // Only the branch that is taken is read: a fallback beside an answered name never was.
            var substitution = answered ? resolved!
                : comma < 0 ? string.Empty
                : argument[(comma + 1)..].Trim();
            sb.Append(substitution.Contains('(')
                ? Evaluate(substitution, context, parentFontSize, rootFontSize,
                    viewportWidth, viewportHeight, maxRecursion - 1)
                : substitution);
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>The start of the next 'var(' at or after <paramref name="from"/>, with the index of
    /// its opening parenthesis, or -1 when there is none. 'var' has to be a word of its own: a
    /// property or function whose name ends in those three letters is not a call.</summary>
    private static int FindVarCall(string text, int from, out int open)
    {
        open = -1;
        for (int i = from; i + 3 <= text.Length; i++)
        {
            if (!text.AsSpan(i, 3).Equals("var".AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;
            if (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '-' || text[i - 1] == '_'))
                continue;
            int j = i + 3;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;
            if (j >= text.Length || text[j] != '(') continue;
            open = j;
            return i;
        }
        return -1;
    }

    private static string? ResolveVar(string name, Element? context)
    {
        // Walk the element and its ancestor chain so custom properties inherit
        // like normal properties (per spec all custom properties inherit by
        // default). Checks the element's own computed style (set by the cascade)
        // before the inline style / global registry.
        for (Element? el = context; el != null; el = el.ParentElement)
        {
            if (el.ComputedStyle != null)
            {
                var computed = el.ComputedStyle.GetCustomProperty(name);
                if (computed != null) return computed;
            }

            var inlineVal = el.GetAttribute("style");
            if (inlineVal != null)
            {
                var props = inlineVal.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var prop in props)
                {
                    var colon = prop.IndexOf(':');
                    if (colon > 0)
                    {
                        var pname = prop[..colon].Trim();
                        var pval = prop[(colon + 1)..].Trim();
                        if (pname.Equals("--" + name, StringComparison.OrdinalIgnoreCase))
                            return pval;
                    }
                }
            }
        }

        var registered = GetCustomProperty(name);
        if (registered != null) return registered;

        if (context?.ComputedStyle != null)
        {
            var cp = context.ComputedStyle.GetCustomProperty(name);
            if (cp != null) return cp;
        }

        return null;
    }

    /// <summary>Whether the text asks for a custom property that nothing in the element's chain
    /// answers — or answers with nothing — while no fallback is there to answer it instead. CSS
    /// Variables 1 §3 makes the whole declaration invalid at computed-value time when that happens:
    /// the CSSOM echo keeps what the page wrote while the computed value is the property's inherited
    /// value or its initial (measured: 'opacity: calc(var(--x))' computes the initial '1', 'color:
    /// var(--x)' under a green parent computes that green, and 'width: calc(10px + var(--x))' computes
    /// the width a box with no 'width' was given). Printing the substitution instead would say
    /// something no engine says — and the text it prints, 'calc(10px + )', is not a value of
    /// anything.</summary>
    public static bool HasUnresolvedVar(string value, Element? context)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (i + 4 > value.Length
                || !value.AsSpan(i, 4).Equals("var(".AsSpan(), StringComparison.OrdinalIgnoreCase))
                continue;
            int close = MatchingClose(value, i + 3);
            if (close < 0) return false;
            var argument = value[(i + 4)..close];
            int comma = TopLevelComma(argument);
            var name = (comma < 0 ? argument : argument[..comma]).Trim();
            if (!name.StartsWith("--", StringComparison.Ordinal)) { i = close; continue; }
            var substituted = ResolveVar(name[2..], context);
            // An empty value substitutes to nothing, which leaves the property with no value to
            // parse — the same failure, and the same answer ('--x: ;' with 'opacity: var(--x)').
            bool answered = substituted != null && substituted.Trim().Length > 0;
            if (!answered && comma < 0) return true;
            // Only the branch that is taken is a problem: 'var(--x, var(--y))' with --x defined never
            // reads --y, so an undefined --y in the fallback says nothing here (measured).
            if (!answered && HasUnresolvedVar(argument[(comma + 1)..], context)) return true;
            i = close;
        }
        return false;
    }

    /// <summary>The index of the parenthesis that closes the one at <paramref name="open"/>, or -1
    /// when the list never closes.</summary>
    private static int MatchingClose(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>The comma that separates a var()'s name from its fallback — the first one with no
    /// parenthesis open in front of it — or -1 when the argument carries no fallback.</summary>
    private static int TopLevelComma(string argument)
    {
        int depth = 0;
        for (int i = 0; i < argument.Length; i++)
        {
            if (argument[i] == '(') depth++;
            else if (argument[i] == ')' && depth > 0) depth--;
            else if (argument[i] == ',' && depth == 0) return i;
        }
        return -1;
    }

    private static string EvaluateAttr(string value, Element? context)
    {
        return AttrRegex.Replace(value, match =>
        {
            var attrName = match.Groups[1].Value.Trim().Trim('"', '\'');
            if (context == null) return "";
            return context.GetAttribute(attrName) ?? "";
        });
    }

    private static string EvaluateCalcAndMath(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        // A color value owns its own calc(): a relative color syntax slot such as
        // 'calc(l + 20)' refers to channel keywords this evaluator cannot resolve.
        if (ColorParser.IsFunctionalColor(value)) return value;

        var calls = CssMathValue.MathCalls(value);
        if (calls.Count == 0) return value;

        var sb = new StringBuilder(value.Length + 16);
        int at = 0;
        foreach (var call in calls)
        {
            sb.Append(value, at, call.Start - at);
            at = call.Start + call.Length;
            var text = value.Substring(call.Start, call.Length);
            var category = CssMathValue.TypeOf(text);
            // A share of the box in one expression with a length is not a number yet — the base is
            // the containing block, which the layout owns — while an expression that is nothing but
            // shares folds to the share it adds up to (measured: 'background-position:
            // calc(10px + 2px) calc(20% + 3%)' computes '12px 23%', and 'calc(300px - 100%)' is
            // still a calc() at computed-value time). A caller that has published a priced base owns
            // that box, and then the expression is worked out.
            var unit = category == null ? null : PrintedUnit(category);
            if (unit == null || (category == "length" && call.Argument.Contains('%')
                    && !_percentagesArePriced))
            {
                sb.Append(text);
                continue;
            }
            float result;
            try
            {
                result = category == "percentage"
                    ? EvalInPercentageSpace(call, parentFontSize, rootFontSize, viewportWidth, viewportHeight)
                    : EvalMathExpression(call.Argument, call.Name, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            }
            catch
            {
                result = 0;
            }
            sb.Append(Acrux.Core.Dom.Animations.CssValueTokenizer.Num(result) + unit);
        }
        sb.Append(value, at, value.Length - at);
        return sb.ToString();
    }

    /// <summary>The unit a math expression of this type is printed with once it has been worked out,
    /// or null for a type the engine has no number for yet. The engine measures a length in pixels,
    /// an angle in degrees and a time in seconds, which is what the reference engine prints too
    /// (measured: 'calc(1grad + 1rad)' is '58.1958deg', 'calc(1s + 1ms)' is '1.001s' and
    /// 'calc(1 + 2)' is the number '3'); a fraction of a grid track, a frequency and a resolution
    /// are left as written because nothing here prices them.</summary>
    private static string? PrintedUnit(string category) => category switch
    {
        "length" => "px",
        "angle" => "deg",
        "time" => "s",
        "number" => "",
        "percentage" => "%",
        _ => null,
    };

    private static float EvalMathExpression(string expr, string funcName, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        expr = expr.Trim();

        if (funcName == "min")
        {
            var parts = SplitArgs(expr);
            if (parts.Count == 0) return 0;
            float minVal = float.MaxValue;
            foreach (var part in parts)
            {
                float val = EvalArithmetic(part.Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                if (val < minVal) minVal = val;
            }
            return minVal;
        }

        if (funcName == "max")
        {
            var parts = SplitArgs(expr);
            if (parts.Count == 0) return 0;
            float maxVal = float.MinValue;
            foreach (var part in parts)
            {
                float val = EvalArithmetic(part.Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                if (val > maxVal) maxVal = val;
            }
            return maxVal;
        }

        if (funcName == "clamp")
        {
            var parts = SplitArgs(expr);
            if (parts.Count < 3) return 0;
            float min = EvalArithmetic(parts[0].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            float mid = EvalArithmetic(parts[1].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            float max = EvalArithmetic(parts[2].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            // clamp(MIN, VAL, MAX) is max(MIN, min(VAL, MAX)), so a MIN above the MAX
            // still yields MIN (CSS Values 4 §10.3.3) rather than an invalid range.
            return MathF.Max(min, MathF.Min(mid, max));
        }

        if (funcName == "round")
        {
            var parts = SplitArgs(expr);
            int first = 0;
            RoundStrategy strategy = RoundStrategy.Nearest;
            if (parts.Count >= 3 && TryRoundStrategy(parts[0], out strategy))
                first = 1;
            if (parts.Count - first < 2) return 0;
            float dividend = EvalArithmetic(parts[first].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            float divisor = EvalArithmetic(parts[first + 1].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            return RoundToMultiple(dividend, divisor, strategy);
        }

        if (funcName is "mod" or "rem" or "abs" or "sign")
        {
            var parts = SplitArgs(expr);
            if (funcName == "abs" || funcName == "sign")
            {
                if (parts.Count < 1) return 0;
                float v = EvalArithmetic(parts[0].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                return funcName == "abs" ? MathF.Abs(v) : MathF.Sign(v);
            }
            if (parts.Count < 2) return 0;
            float a = EvalArithmetic(parts[0].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            float b = EvalArithmetic(parts[1].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            if (b == 0) return 0;
            float quotient = a / b;
            // mod() takes the sign of the divisor, rem() that of the dividend
            // (CSS Values 4 §10.3.7, matching Euclidean vs truncated division).
            return funcName == "mod"
                ? a - b * MathF.Floor(quotient)
                : a - b * MathF.Truncate(quotient);
        }

        if (funcName == "hypot")
        {
            // The length of a vector out of the arguments, in their own unit (measured:
            // 'hypot(3px, 4px)' is 'calc(5px)').
            var parts = SplitArgs(expr);
            double sum = 0;
            foreach (var part in parts)
            {
                double value = EvalArithmetic(part.Trim(), parentFontSize, rootFontSize,
                    viewportWidth, viewportHeight);
                sum += value * value;
            }
            return (float)System.Math.Sqrt(sum);
        }

        if (funcName is "sqrt" or "exp" or "log" or "pow"
            or "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "atan2")
            return EvalNamedFunction(funcName, expr, parentFontSize, rootFontSize,
                viewportWidth, viewportHeight);

        return EvalArithmetic(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// The arithmetic and circular functions, worked out in the unit the surrounding expression is
    /// measured in. The first group answers with a count and takes nothing but counts; sin(), cos()
    /// and tan() take an angle but a bare number given to them is radians, which a page would not
    /// guess (measured: 'cos(0.5)' is 'calc(0.877583)', the cosine of half a radian); and the
    /// inverse functions answer with an angle, printed in degrees like every other angle here.
    /// </summary>
    private static float EvalNamedFunction(string funcName, string expr, float parentFontSize,
        float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var parts = SplitArgs(expr);
        float Value(int index) => index < parts.Count
            ? EvalArithmetic(parts[index].Trim(), parentFontSize, rootFontSize,
                viewportWidth, viewportHeight)
            : 0f;

        switch (funcName)
        {
            case "sqrt": return parts.Count < 1 || Value(0) < 0 ? 0f : MathF.Sqrt(Value(0));
            case "exp": return parts.Count < 1 ? 0f : MathF.Exp(Value(0));
            case "log": return parts.Count < 1 || Value(0) <= 0 ? 0f : MathF.Log(Value(0));
            case "pow": return parts.Count < 2 ? 0f : MathF.Pow(Value(0), Value(1));
            case "sin": return parts.Count < 1 ? 0f : MathF.Sin(RadiansOf(Value(0), parts[0]));
            case "cos": return parts.Count < 1 ? 0f : MathF.Cos(RadiansOf(Value(0), parts[0]));
            case "tan": return parts.Count < 1 ? 0f : MathF.Tan(RadiansOf(Value(0), parts[0]));
            case "asin": return parts.Count < 1 ? 0f : MathF.Asin(Math.Clamp(Value(0), -1f, 1f)) * DegreesPerRadian;
            case "acos": return parts.Count < 1 ? 0f : MathF.Acos(Math.Clamp(Value(0), -1f, 1f)) * DegreesPerRadian;
            case "atan": return parts.Count < 1 ? 0f : MathF.Atan(Value(0)) * DegreesPerRadian;
            default: return parts.Count < 2 ? 0f : MathF.Atan2(Value(0), Value(1)) * DegreesPerRadian;
        }
    }

    private const float DegreesPerRadian = 180f / MathF.PI;

    /// <summary>An angle in radians: the expression has already been resolved to degrees by the
    /// unit conversion, but a count written into sin(), cos() or tan() is radians as it is, so the
    /// text the page wrote decides which of the two it was (measured, both spellings).</summary>
    private static float RadiansOf(float value, string argument)
        => CssMathValue.TypeOf(argument) == "number" ? value : value / DegreesPerRadian;

    private enum RoundStrategy { Nearest, Up, Down, ToZero }

    private static bool TryRoundStrategy(string token, out RoundStrategy strategy)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "nearest": strategy = RoundStrategy.Nearest; return true;
            case "up": strategy = RoundStrategy.Up; return true;
            case "down": strategy = RoundStrategy.Down; return true;
            case "to-zero": strategy = RoundStrategy.ToZero; return true;
            default: strategy = RoundStrategy.Nearest; return false;
        }
    }

    /// <summary>
    /// Round |dividend| to a multiple of |divisor|. 'up' rounds toward positive
    /// infinity and 'down' toward negative infinity (not away from / toward zero),
    /// which is observable only for negative dividends; verified against the
    /// reference engine with negative margins.
    /// </summary>
    private static float RoundToMultiple(float dividend, float divisor, RoundStrategy strategy)
    {
        if (divisor == 0) return 0;
        float quotient = dividend / divisor;
        float rounded = strategy switch
        {
            RoundStrategy.Up => MathF.Ceiling(quotient),
            RoundStrategy.Down => MathF.Floor(quotient),
            RoundStrategy.ToZero => MathF.Truncate(quotient),
            _ => MathF.Round(quotient, MidpointRounding.AwayFromZero),
        };
        return rounded * divisor;
    }

    private static List<string> SplitArgs(string args)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '(') depth++;
            else if (args[i] == ')') depth--;
            else if (args[i] == ',' && depth == 0)
            {
                result.Add(args[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < args.Length)
            result.Add(args[start..].Trim());
        return result;
    }

    private static float EvalArithmetic(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        expr = expr.Trim();

        // Resolve any nested function calls first
        expr = ResolveNestedFunctions(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);

        // Tokenize into numbers/units and operators
        var tokens = TokenizeWithPrecedence(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        if (tokens.Count == 0) return 0;

        // First pass: evaluate * and /
        var pass1 = new List<float>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is OpToken op && (op.Value == '*' || op.Value == '/'))
            {
                float left = pass1[^1];
                pass1.RemoveAt(pass1.Count - 1);
                i++;
                float right = ((NumToken)tokens[i]).Value;
                float result = op.Value == '*' ? left * right : (right != 0 ? left / right : 0);
                pass1.Add(result);
            }
            else if (tokens[i] is NumToken n)
            {
                pass1.Add(n.Value);
            }
        }

        // Second pass: evaluate + and -
        float result2 = pass1[0];
        int opIdx = 1;
        foreach (var token in tokens)
        {
            if (token is OpToken op)
            {
                if (opIdx < pass1.Count)
                {
                    if (op.Value == '+') result2 += pass1[opIdx];
                    else if (op.Value == '-') result2 -= pass1[opIdx];
                    opIdx++;
                }
            }
        }

        return result2;
    }

    /// <summary>Work every math function that stands on its own inside an expression out to the
    /// number it is worth, printed in the unit its own type prints in. The old version printed 'px'
    /// for all of them, which turned a nested angle into a length and a nested time into a
    /// length.</summary>
    private static string ResolveNestedFunctions(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var calls = CssMathValue.MathCalls(expr);
        if (calls.Count == 0) return expr;

        var sb = new StringBuilder(expr.Length + 16);
        int at = 0;
        foreach (var call in calls)
        {
            sb.Append(expr, at, call.Start - at);
            at = call.Start + call.Length;
            var text = expr.Substring(call.Start, call.Length);
            var category = CssMathValue.TypeOf(text);
            var unit = category == null ? null : PrintedUnit(category);
            // A share of the box is priced by the layout, not here, so a percentage expression keeps
            // its own text and the caller decides what to do with it — unless the caller has
            // published the box it is a share of.
            if (unit == null || (category == "length" && call.Argument.Contains('%')
                    && !_percentagesArePriced))
            {
                sb.Append(text);
                continue;
            }
            float result;
            try
            {
                result = category == "percentage"
                    ? EvalInPercentageSpace(call, parentFontSize, rootFontSize, viewportWidth, viewportHeight)
                    : EvalMathExpression(call.Argument, call.Name, parentFontSize, rootFontSize,
                        viewportWidth, viewportHeight);
            }
            catch
            {
                result = 0;
            }
            sb.Append(Acrux.Core.Dom.Animations.CssValueTokenizer.Num(result) + unit);
        }
        sb.Append(expr, at, expr.Length - at);
        return sb.ToString();
    }

    /// <summary>Work out an expression that is nothing but shares of a box. The box is not known at
    /// computed-value time, so the arithmetic runs with 100 as the base and the answer keeps the
    /// percent sign it was priced with (measured: 'background-position: calc(20% + 3%)' computes
    /// '23%', not the fraction of a font size the plain conversion would give).</summary>
    private static float EvalInPercentageSpace(CssMathValue.MathCall call, float parentFontSize,
        float rootFontSize, float viewportWidth, float viewportHeight)
    {
        using (UsePercentageBase(100f))
            return EvalMathExpression(call.Argument, call.Name, parentFontSize, rootFontSize,
                viewportWidth, viewportHeight);
    }

    private abstract class Token { }
    private class NumToken : Token { public float Value; }
    private class OpToken : Token { public char Value; }

    private static List<Token> TokenizeWithPrecedence(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < expr.Length)
        {
            if (char.IsWhiteSpace(expr[i]))
            {
                i++;
                continue;
            }

            // Check for unary + or - (at start of expression or after another operator)
            bool isUnary = (expr[i] == '+' || expr[i] == '-') &&
                (tokens.Count == 0 || tokens[^1] is OpToken);

            if (!isUnary && (expr[i] == '+' || expr[i] == '-' || expr[i] == '*' || expr[i] == '/'))
            {
                tokens.Add(new OpToken { Value = expr[i] });
                i++;
                continue;
            }

            if (isUnary && (expr[i] == '+' || expr[i] == '-'))
            {
                char sign = expr[i];
                i++;

                // Skip whitespace after sign
                while (i < expr.Length && char.IsWhiteSpace(expr[i])) i++;

                if (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.'))
                {
                    var numMatch = NumberRegex.Match(expr[i..]);
                    if (numMatch.Success)
                    {
                        float num = float.Parse(numMatch.Value, CultureInfo.InvariantCulture);
                        if (sign == '-') num = -num;
                        i += numMatch.Length;

                        var unitMatch = UnitRegex.Match(i < expr.Length ? expr[i..] : "");
                        if (unitMatch.Success)
                        {
                            string unit = unitMatch.Value.ToLowerInvariant();
                            i += unitMatch.Length;
                            num = ConvertUnit(num, unit, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                        }

                        tokens.Add(new NumToken { Value = num });
                        continue;
                    }
                }

                // If no number follows, treat as binary operator
                tokens.Add(new OpToken { Value = sign });
                continue;
            }

            if (char.IsDigit(expr[i]) || expr[i] == '.')
            {
                var numMatch = NumberRegex.Match(expr[i..]);
                if (numMatch.Success)
                {
                    float num = float.Parse(numMatch.Value, CultureInfo.InvariantCulture);
                    int consumed = numMatch.Length;
                    i += consumed;

                    var unitMatch = UnitRegex.Match(i < expr.Length ? expr[i..] : "");
                    if (unitMatch.Success)
                    {
                        string unit = unitMatch.Value.ToLowerInvariant();
                        i += unitMatch.Length;
                        num = ConvertUnit(num, unit, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                    }

                    tokens.Add(new NumToken { Value = num });
                    continue;
                }
            }

            i++;
        }

        return tokens;
    }

    /// <summary>
    /// Percentage base of the math expression being evaluated. A percentage inside
    /// calc() depends on the containing block, which only layout knows, so the
    /// layout-time caller publishes it here instead of overloading the font size
    /// argument (CSS Values 4 10.7).
    ///
    /// <c>priced</c> says that the base published is a BOX — that the number in front of
    /// the engine is what the percentage is a share of, and not a font size handed over for
    /// the 'em's in the same expression. Only then may a length expression that mixes a
    /// percentage with a length be worked out: the computed surface must leave
    /// 'calc(300px - 100%)' standing because nothing has measured the box yet, while the
    /// layout of a 400px block has to be told that its 'width: calc(100% - 10px)' is 390px.
    /// </summary>
    [ThreadStatic] private static float _percentageBase;
    [ThreadStatic] private static bool _percentagesArePriced;

    public sealed class PercentageBaseScope : IDisposable
    {
        private readonly float _previousBase;
        private readonly bool _previousPriced;
        internal PercentageBaseScope(float value, bool priced)
        {
            _previousBase = _percentageBase; _previousPriced = _percentagesArePriced;
            _percentageBase = value; _percentagesArePriced = priced;
        }
        public void Dispose() { _percentageBase = _previousBase; _percentagesArePriced = _previousPriced; }
    }

    public static IDisposable UsePercentageBase(float value, bool priced = false)
        => new PercentageBaseScope(value, priced);

    private static float ConvertUnit(float value, string unit, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        // Font-relative units inside math functions use the element's real
        // glyph metrics when a style context is active (CSS Values 4 §6.3);
        // the ratios below are only the no-context fallback.
        var fontStyle = Acrux.Core.Dom.FontUnitContext.Current;
        // 'rem' is a share of the ROOT's font (CSS Values 4 §6.3), not of the font of the element
        // whose metrics are in scope: the style carries the number the document started with, and a
        // resolution with no style at all falls back to the caller's argument. Measured on a 14px
        // box inside a 16px root: 'calc(1rem)' is 16px, '1em' is 14px and 'calc(1rem + 1em)' is 30px.
        if (unit == "rem")
            return value * (fontStyle is { RootFontSize: > 0 } ? fontStyle.RootFontSize : rootFontSize);
        if (fontStyle != null && fontStyle.FontSize > 0)
        {
            var fm = Acrux.Core.Fonts.FontMetricsProvider.GetForStyle(fontStyle);
            float? resolved = unit switch
            {
                "ex" or "rex" => fm.XHeight,
                "ch" => fm.ZeroWidth,
                "ic" or "ric" => fm.IdeographicWidth > 0 ? fm.IdeographicWidth : fontStyle.FontSize,
                "cap" or "rcap" => fm.CapHeight,
                "lh" or "rlh" => Acrux.Core.Fonts.LineBoxMetrics.GetLineHeight(fontStyle),
                _ => null
            };
            if (resolved is float r) return value * r;
            // 'em' is the element's own font size. A deferred calc() may have been
            // handed the percentage base as parentFontSize, so the context wins.
            if (unit == "em") return value * fontStyle.FontSize;
        }
        // The absolute units come from Length's table rather than from factors written here, so
        // that 'calc(1cm)' and a bare '1cm' cannot answer two different numbers — and so that
        // 'q', which this switch never had and which therefore resolved as a pixel, resolves.
        if (Acrux.Core.Dom.Length.TryAbsoluteUnitPixels(unit, out var pixelsPerUnit))
            return (float)(value * pixelsPerUnit);
        return unit switch
        {
            "em" => value * parentFontSize,            "rem" => value * rootFontSize,
            "%" => value / 100f * (_percentageBase > 0 ? _percentageBase : parentFontSize),
            "vw" => value * viewportWidth / 100f,
            "vh" => value * viewportHeight / 100f,
            "vmin" => value * Math.Min(viewportWidth, viewportHeight) / 100f,
            "vmax" => value * Math.Max(viewportWidth, viewportHeight) / 100f,
            "vi" => value * viewportWidth / 100f,
            "vb" => value * viewportHeight / 100f,
            "svw" => value * viewportWidth / 100f,
            "svh" => value * viewportHeight / 100f,
            "lvw" => value * viewportWidth / 100f,
            "lvh" => value * viewportHeight / 100f,
            "dvw" => value * viewportWidth / 100f,
            "dvh" => value * viewportHeight / 100f,
            "cqw" => value * viewportWidth / 100f,
            "cqh" => value * viewportHeight / 100f,
            "cqi" => value * viewportWidth / 100f,
            "cqb" => value * viewportHeight / 100f,
            "cqmin" => value * Math.Min(viewportWidth, viewportHeight) / 100f,
            "cqmax" => value * Math.Max(viewportWidth, viewportHeight) / 100f,
            "ex" => value * parentFontSize * 0.5f,
            "ch" => value * parentFontSize * 0.5f,
            "ic" => value * parentFontSize,
            "rex" => value * rootFontSize * 0.5f,
            "ric" => value * rootFontSize,
            "lh" => value * parentFontSize,
            "rlh" => value * rootFontSize,
            "cap" => value * parentFontSize * 0.7f,
            "rcap" => value * rootFontSize * 0.7f,
            // An angle is measured in degrees and a time in seconds, whichever way the page spelled
            // it, because those are the units they are printed in — the same table the fold of a
            // specified value reads from (measured: 'calc(1grad + 1rad)' is '58.1958deg' and
            // 'calc(1s + 1ms)' is '1.001s').
            "grad" => value * 0.9f,
            "rad" => value * DegreesPerRadian,
            "turn" => value * 360f,
            "ms" => value * 0.001f,
            _ => value
        };
    }

    /// <summary>The four properties whose value is a grid track list (CSS Grid 1 §7.2): each keeps
    /// 'fit-content()' and 'minmax()' as written, since it is the track layout that prices them.</summary>
    private static bool IsTrackListProperty(string? name) =>
        name is not null && name.ToLowerInvariant() switch
        {
            "grid-template-rows" or "grid-template-columns" or "grid-auto-rows" or "grid-auto-columns"
                => true,
            _ => false,
        };

    private static string EvaluateFitContent(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        return FitContentRegex.Replace(value, match =>
        {
            var arg = match.Groups[1].Value.Trim();
            var evaluated = Evaluate(arg, null, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            var px = ResolveToPixels(evaluated, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            return $"{px:F1}px";
        });
    }

    private static string EvaluateCounter(string value, Element? context)
    {
        return CounterRegex.Replace(value, match => "0");
    }

    private static string EvaluateCounters(string value, Element? context)
    {
        return CountersRegex.Replace(value, match => "");
    }

    private static float ResolveToPixels(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        value = value.Trim();

        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px;
        // 'rem' has to be asked before 'em': the one ends with the other's characters, and reading
        // '1rem' as an 'em' priced a share of the root's font at the element's own (measured: a 14px
        // box inside a 16px root resolves 'calc(1rem)' as 16px and 'calc(1rem + 1em)' as 30px).
        if (value.EndsWith("rem") && float.TryParse(value[..^2], out var rem)) return rem * rootFontSize;
        if (value.EndsWith("em") && float.TryParse(value[..^2], out var em)) return em * parentFontSize;
        if (value.EndsWith("%") && float.TryParse(value[..^1], out var pct)) return pct / 100f * parentFontSize;
        if (value.EndsWith("vw") && float.TryParse(value[..^2], out var vw)) return vw * viewportWidth / 100f;
        if (value.EndsWith("vh") && float.TryParse(value[..^2], out var vh)) return vh * viewportHeight / 100f;

        if (float.TryParse(value, out var num)) return num;
        // Remaining units (pt/pc/in/cm/mm/q/ch/ex/cap/lh/vmin/vmax/container
        // units…) go through the shared Length conversion, which also picks up
        // the ambient font context for the glyph-based units.
        if (Acrux.Core.Dom.Length.TryParse(value, out var length) && length is not Acrux.Core.Dom.AutoLength)
            return length.ToPixels(parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        return 0;
    }

    public static string? ParseGradient(string value)
    {
        if (LinearGradRegex.IsMatch(value)) return value;
        if (RadialGradRegex.IsMatch(value)) return value;
        if (ConicGradRegex.IsMatch(value)) return value;
        if (RepeatingLinearGradRegex.IsMatch(value)) return value;
        if (RepeatingRadialGradRegex.IsMatch(value)) return value;
        if (RepeatingConicGradRegex.IsMatch(value)) return value;
        return null;
    }

    public static bool IsGradient(string value) => ParseGradient(value) != null;

    public static string? ParseTransform(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "none") return null;
        return value;
    }

    public static bool IsCalc(string value) => CalcFuncRegex.IsMatch(value);
    public static bool IsVar(string value) => VarRegex.IsMatch(value);
    public static bool IsMinMax(string value) => CssMathValue.MathCalls(value).Count > 0;
    public static bool IsClamp(string value)
    {
        foreach (var call in CssMathValue.MathCalls(value))
            if (call.Name == "clamp") return true;
        return false;
    }
    public static bool IsFitContent(string value) => FitContentRegex.IsMatch(value);
}
