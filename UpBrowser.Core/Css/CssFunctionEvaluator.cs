using System.Text.RegularExpressions;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css;

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

    public static string Evaluate(string value, Element? context = null, float parentFontSize = 16, float rootFontSize = 16, float viewportWidth = 0, float viewportHeight = 0, int maxRecursion = 10, bool forceMath = false)
    {
        if (string.IsNullOrEmpty(value) || maxRecursion <= 0) return value;

        // Percentages inside calc()/min()/max()/clamp() depend on the containing
        // block, which is unknown at computed-value time: substitute var()/attr()
        // only and keep the math expression intact (MathLength) so the layout
        // pass can resolve it against the correct percentage base. Layout-time
        // callers pass forceMath: true to get the numeric result.
        if (!forceMath && HasMathFunctionWithPercent(value))
        {
            var varsOnly = EvaluateVar(value, context, parentFontSize, rootFontSize, viewportWidth, viewportHeight, maxRecursion);
            return EvaluateAttr(varsOnly, context);
        }

        var result = value;

        result = EvaluateVar(result, context, parentFontSize, rootFontSize, viewportWidth, viewportHeight, maxRecursion);
        result = EvaluateAttr(result, context);
        result = EvaluateCalcAndMath(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        result = EvaluateFitContent(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        result = EvaluateCounter(result, context);
        result = EvaluateCounters(result, context);

        return result;
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

    private static string EvaluateVar(string value, Element? context, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight, int maxRecursion = 10)
    {
        return VarRegex.Replace(value, match =>
        {
            var name = match.Groups[1].Value.Trim();
            var fallback = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";

            var resolved = ResolveVar(name, context);

            if (resolved == null)
                resolved = !string.IsNullOrEmpty(fallback) ? fallback : "";

            if (resolved != null && (VarRegex.IsMatch(resolved) || CalcFuncRegex.IsMatch(resolved)))
                resolved = Evaluate(resolved, context, parentFontSize, rootFontSize, viewportWidth, viewportHeight, maxRecursion - 1);

            return resolved ?? "";
        });
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

        int maxIter = 20;
        while (maxIter-- > 0)
        {
            var match = FindOuterMathFunction(value);
            if (match == null) break;

            var (funcName, innerExpr, _, _) = match.Value;
            if (HasUnresolvableIdentifier(innerExpr))
            {
                // The expression carries a bare identifier that is neither a unit nor
                // a nested call — a relative color channel keyword inside calc(), for
                // instance. Folding it to a number here would silently lose meaning,
                // so leave the expression for the parser that owns it.
                break;
            }

            float result;
            try
            {
                result = EvalMathExpression(innerExpr, funcName, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            }
            catch
            {
                result = 0;
            }
            value = value[..match.Value.StartIndex] + $"{result:F1}px" + value[(match.Value.StartIndex + match.Value.Length)..];
        }
        return value;
    }

    /// <summary>True when the text contains an identifier that this evaluator cannot
    /// resolve: not a unit glued to a number, and not one of the math functions.</summary>
    private static bool HasUnresolvableIdentifier(string expr)
    {
        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (!(char.IsLetter(c) || c == '_')) continue;

            int start = i;
            while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '-' || expr[i] == '_')) i++;
            string word = expr[start..i];

            if (i < expr.Length && expr[i] == '(') continue; // nested call: resolvable
            char before = start > 0 ? expr[start - 1] : ' ';
            if (char.IsDigit(before) || before == '.') continue; // unit suffix: resolvable

            bool known = false;
            foreach (var name in MathFunctions)
            {
                if (string.Equals(word, name, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
            }
            if (!known)
            {
                foreach (var name in MathKeywordArguments)
                {
                    if (string.Equals(word, name, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                }
            }
            if (!known) return true;
            i--;
        }
        return false;
    }

    /// <summary>Identifiers that are arguments rather than unknown names: the
    /// rounding strategies of round(), plus the special values it accepts.</summary>
    private static readonly string[] MathKeywordArguments =
        { "nearest", "up", "down", "to-zero", "inf", "-inf", "nan" };

    /// <summary>
    /// CSS Values 4 &amp;10.3 math functions. Order only matters for readability: the
    /// match requires a '(' right after the name, so 'min' cannot shadow 'minmax'.
    /// </summary>
    private static readonly string[] MathFunctions =
        { "calc", "clamp", "round", "sign", "min", "max", "mod", "rem", "abs" };

    private static (string funcName, string innerExpr, int StartIndex, int Length)? FindOuterMathFunction(string value)
    {
        int i = 0;
        while (i < value.Length)
        {
            string? func = null;
            // A function name must start at a token boundary: 'max(' inside
            // 'minmax(' is not a call to max().
            bool boundary = i == 0 || !(char.IsLetterOrDigit(value[i - 1]) || value[i - 1] == '-' || value[i - 1] == '_');
            if (boundary)
            {
                foreach (var name in MathFunctions)
                {
                    if (i + name.Length < value.Length &&
                        value[i + name.Length] == '(' &&
                        value[i..(i + name.Length)].Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        func = name;
                        break;
                    }
                }
            }

            if (func != null)
            {
                int parenStart = i + func.Length; // index of the '(' character
                int depth = 1;
                int j = parenStart + 1;           // start scanning AFTER '('
                while (j < value.Length && depth > 0)
                {
                    if (value[j] == '(') depth++;
                    else if (value[j] == ')') depth--;
                    if (depth > 0) j++;
                }
                if (depth == 0)
                {
                    var inner = value[(parenStart + 1)..j];
                    return (func, inner, i, j - i + 1);
                }
                i = j + 1;
            }
            else
            {
                i++;
            }
        }
        return null;
    }

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

        return EvalArithmetic(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
    }

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

    private static string ResolveNestedFunctions(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        int maxIter = 10;
        while (maxIter-- > 0)
        {
            var match = FindOuterMathFunction(expr);
            if (match == null) break;
            var (funcName, innerExpr, start, len) = match.Value;
            float val = EvalMathExpression(innerExpr, funcName, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            expr = expr[..start] + $"{val:F1}px" + expr[(start + len)..];
        }
        return expr;
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
                        float num = float.Parse(numMatch.Value);
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
                    float num = float.Parse(numMatch.Value);
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
    /// </summary>
    [ThreadStatic] private static float _percentageBase;

    public sealed class PercentageBaseScope : IDisposable
    {
        private readonly float _previous;
        internal PercentageBaseScope(float value) { _previous = _percentageBase; _percentageBase = value; }
        public void Dispose() => _percentageBase = _previous;
    }

    public static IDisposable UsePercentageBase(float value) => new PercentageBaseScope(value);

    private static float ConvertUnit(float value, string unit, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        // Font-relative units inside math functions use the element's real
        // glyph metrics when a style context is active (CSS Values 4 §6.3);
        // the ratios below are only the no-context fallback.
        var fontStyle = UpBrowser.Core.Dom.FontUnitContext.Current;
        if (fontStyle != null && fontStyle.FontSize > 0)
        {
            var fm = UpBrowser.Core.Fonts.FontMetricsProvider.GetForStyle(fontStyle);
            float? resolved = unit switch
            {
                "ex" or "rex" => fm.XHeight,
                "ch" => fm.ZeroWidth,
                "ic" or "ric" => fm.IdeographicWidth > 0 ? fm.IdeographicWidth : fontStyle.FontSize,
                "cap" or "rcap" => fm.CapHeight,
                "lh" or "rlh" => UpBrowser.Core.Fonts.LineBoxMetrics.GetLineHeight(fontStyle),
                _ => null
            };
            if (resolved is float r) return value * r;
            // 'em' is the element's own font size. A deferred calc() may have been
            // handed the percentage base as parentFontSize, so the context wins.
            if (unit == "em") return value * fontStyle.FontSize;
        }
        return unit switch
        {
            "px" => value,
            "em" => value * parentFontSize,
            "rem" => value * rootFontSize,
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
            "pt" => value * 1.33333f,
            "pc" => value * 16f,
            "in" => value * 96f,
            "cm" => value * 37.7953f,
            "mm" => value * 3.77953f,
            "ex" => value * parentFontSize * 0.5f,
            "ch" => value * parentFontSize * 0.5f,
            "ic" => value * parentFontSize,
            "rex" => value * rootFontSize * 0.5f,
            "ric" => value * rootFontSize,
            "lh" => value * parentFontSize,
            "rlh" => value * rootFontSize,
            "cap" => value * parentFontSize * 0.7f,
            "rcap" => value * rootFontSize * 0.7f,
            _ => value
        };
    }

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
        if (value.EndsWith("em") && float.TryParse(value[..^2], out var em)) return em * parentFontSize;
        if (value.EndsWith("rem") && float.TryParse(value[..^2], out var rem)) return rem * rootFontSize;
        if (value.EndsWith("%") && float.TryParse(value[..^1], out var pct)) return pct / 100f * parentFontSize;
        if (value.EndsWith("vw") && float.TryParse(value[..^2], out var vw)) return vw * viewportWidth / 100f;
        if (value.EndsWith("vh") && float.TryParse(value[..^2], out var vh)) return vh * viewportHeight / 100f;

        if (float.TryParse(value, out var num)) return num;
        // Remaining units (pt/pc/in/cm/mm/q/ch/ex/cap/lh/vmin/vmax/container
        // units…) go through the shared Length conversion, which also picks up
        // the ambient font context for the glyph-based units.
        if (UpBrowser.Core.Dom.Length.TryParse(value, out var length) && length is not UpBrowser.Core.Dom.AutoLength)
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
    public static bool IsMinMax(string value) => FindOuterMathFunction(value) != null;
    public static bool IsClamp(string value) => FindOuterMathFunction(value)?.funcName == "clamp";
    public static bool IsFitContent(string value) => FitContentRegex.IsMatch(value);
}
