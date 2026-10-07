using System.Globalization;
using System.Text;

namespace Acrux.Core.Css;

/// <summary>
/// Evaluates CSS Media Queries (media queries level 4/5) against a viewport
/// and environment. Supports:
///   - media types: all, screen, print, speech, ...
///   - logical operators: and / or / not / only, and comma-separated lists
///   - parenthesized conditions with (feature), (feature: value),
///     (feature: min|max value) and range syntax (min &lt;= feature &lt;= max)
///   - standard features: width, height, aspect-ratio, orientation, resolution,
///     color, monochrome, grid, device-width, device-height, device-aspect-ratio,
///     color-index, color-gamut, hover, pointer, any-hover, any-pointer,
///     update, overflow-block, overflow-inline, prefers-color-scheme,
///     prefers-reduced-motion, prefers-reduced-transparency, prefers-contrast,
///     forced-colors, light-level, scripting, display-mode, and the vendor
///     '-webkit-*-device-pixel-ratio' aliases
/// A feature name and a keyword value are both ASCII case-insensitive (measured:
/// '(COLOR-GAMUT: SRGB)' and '(orientation: PORTRAIT)' match).
/// </summary>
public static class MediaQueryEvaluator
{
    private static readonly HashSet<string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "screen", "print", "speech", "tty", "tv", "projection",
        "handheld", "braille", "embossed", "aural"
    };

    public static bool Evaluate(string condition, float viewportWidth, float viewportHeight, string colorScheme = "light",
        MediaQueryEnvironment? env = null)
    {
        env ??= MediaQueryEnvironment.Default(viewportWidth, viewportHeight, colorScheme);

        if (string.IsNullOrWhiteSpace(condition)) return true;

        // A comma-separated list of media queries: true if ANY query matches.
        foreach (var query in SplitQueryList(condition))
        {
            if (EvaluateQuery(query.Trim(), env))
                return true;
        }
        return false;
    }

    /// <summary>Parses a comma-separated media query list, honoring parentheses.</summary>
    public static List<string> SplitQueryList(string condition)
    {
        var queries = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < condition.Length; i++)
        {
            char c = condition[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                var q = condition[start..i].Trim();
                if (q.Length > 0) queries.Add(q);
                start = i + 1;
            }
        }
        var last = condition[start..].Trim();
        if (last.Length > 0) queries.Add(last);
        return queries;
    }

    private static bool EvaluateQuery(string query, MediaQueryEnvironment env)
    {
        query = query.Trim();
        if (query.Length == 0) return true;

        // Strip outer parentheses only when the first '(' pairs with the last ')'. Inside one pair
        // the operands of 'and'/'or' have to be parenthesised conditions themselves, which the
        // outermost query does not require — 'screen and (min-width: 500px)' is a media type in
        // that position and is perfectly good.
        bool insideParens = false;
        while (query.StartsWith('(') && query.EndsWith(')') && OuterParensWrapWhole(query))
        {
            query = query[1..^1].Trim();
            insideParens = true;
        }

        // Handle leading "not"
        bool negate = false;
        if (query.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            negate = true;
            query = query[4..].Trim();
            // "not (cond)" is a negation of the whole condition.
            if (query.StartsWith('(') && query.EndsWith(')'))
            {
                var inner = EvaluateQuery(query, env);
                return !inner;
            }
        }

        // Handle leading "only"
        if (query.StartsWith("only ", StringComparison.OrdinalIgnoreCase))
            query = query[5..].Trim();

        // Split on top-level "or" (media queries level 4)
        int orIndex = FindTopLevelOperator(query, " or ");
        if (orIndex >= 0)
        {
            var leftText = query[..orIndex].Trim();
            var rightText = query[(orIndex + 4)..].Trim();
            if (!IsBooleanOperand(leftText, insideParens) || !IsBooleanOperand(rightText, insideParens)) return false;
            return EvaluateQuery(leftText, env) || EvaluateQuery(rightText, env);
        }

        // Split on top-level "and"
        int andIndex = FindTopLevelOperator(query, " and ");
        if (andIndex >= 0)
        {
            var leftText = query[..andIndex].Trim();
            var rightText = query[(andIndex + 5)..].Trim();
            if (!IsBooleanOperand(leftText, insideParens) || !IsBooleanOperand(rightText, insideParens)) return false;
            bool result = EvaluateQuery(leftText, env) && EvaluateQuery(rightText, env);
            return negate ? !result : result;
        }

        bool value = EvaluateSingle(query.Trim(), env);
        return negate ? !value : value;
    }

    /// <summary>Whether an operand of an 'and'/'or' is something the grammar allows in that
    /// position. Inside one pair of parentheses every operand has to be a parenthesised condition
    /// of its own, so both '(width &gt;= 400px and width &lt;= 900px)' and '(hover and pointer)' are
    /// not queries at all while '((width &gt;= 400px) and (width &lt;= 900px))' is — measured, the
    /// first two match whatever the viewport and the pointer are and the third matches. At the
    /// top level the operands are whole media queries, which may themselves be conjunctions.</summary>
    private static bool IsBooleanOperand(string operand, bool insideParens)
    {
        if (!insideParens) return true;
        var text = operand.Trim();
        return text.Length > 0 && text.StartsWith('(') && text.EndsWith(')') && OuterParensWrapWhole(text);
    }

    private static int FindTopLevelOperator(string query, string op)
    {
        int depth = 0;
        for (int i = 0; i + op.Length <= query.Length; i++)
        {
            char c = query[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && string.CompareOrdinal(query, i, op, 0, op.Length) == 0)
                return i;
        }
        return -1;
    }

    private static bool HasBalancedOuterParens(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')')
            {
                depth--;
                if (depth < 0) return false;
            }
        }
        return depth == 0;
    }

    /// <summary>True when the first '(' is closed by the final ')', i.e. the whole
    /// string is wrapped in one paren pair ("(a) and (b)" must NOT be stripped).</summary>
    private static bool OuterParensWrapWhole(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')')
            {
                depth--;
                if (depth == 0 && i < s.Length - 1)
                    return false; // the pair closes before the end -> not wrapping whole
            }
        }
        return true;
    }

    private static bool EvaluateSingle(string condition, MediaQueryEnvironment env)
    {
        condition = condition.Trim();

        // Media type alone, possibly with a trailing condition: "screen" or
        // "screen and (min-width: 500px)" was already split by 'and'. Handle
        // bare type / "not screen" here.
        if (MediaTypes.Contains(condition))
            return condition.ToLowerInvariant() switch
            {
                "all" => true,
                "screen" => env.MediaType == "screen" || env.MediaType == "all",
                "print" => env.MediaType == "print" || env.MediaType == "all",
                "speech" => env.MediaType == "speech" || env.MediaType == "all",
                _ => env.MediaType == condition.ToLowerInvariant()
            };

        // Bare feature without value: "(hover)" / "(color)"
        if (condition.StartsWith('(') && condition.EndsWith(')'))
            condition = condition[1..^1].Trim();

        if (!condition.Contains(':'))
        {
            // Chained range: "400px <= width < 500px" or "1/2 <= aspect-ratio <= 2/1".
            if (TrySplitChainedRange(condition, out var chainName, out var chainLeft, out var chainLeftOp,
                    out var chainRight, out var chainRightOp))
                return EvaluateNamedFeature(chainName, ReverseOp(chainLeftOp) + " " + chainLeft, env)
                    && EvaluateNamedFeature(chainName, chainRightOp + " " + chainRight, env);

            // Range syntax: "width >= 600px" or "400px <= width".
            int rangeIdx = FindRangeOperator(condition);
            if (rangeIdx >= 0)
            {
                var (featName, featOp, featValue) = SplitRange(condition, rangeIdx);
                return EvaluateNamedFeature(featName, featOp + " " + featValue, env);
            }
            return EvaluateBooleanFeature(condition, env);
        }

        // (feature: value) or range syntax.
        var colon = condition.IndexOf(':');
        var propName = condition[..colon].Trim().ToLowerInvariant();
        var propValue = condition[(colon + 1)..].Trim();

        return EvaluateNamedFeature(propName, propValue, env);
    }

    private static int FindRangeOperator(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<' || s[i] == '>')
                return i;
        }
        return -1;
    }

    private static (string name, string op, string value) SplitRange(string s, int idx)
    {
        string left = s[..idx].Trim();
        string right = s[(idx + 1)..].Trim();
        string op = s[idx].ToString();
        if (idx + 1 < s.Length && (s[idx + 1] == '='))
        {
            op += "=";
            right = s[(idx + 2)..].Trim();
        }
        else if (idx + 1 < s.Length && (s[idx + 1] == '<' || s[idx + 1] == '>'))
        {
            // Chained range "400px <= width <= 800px": not supported per query,
            // fall back to the rightmost comparison.
            op = s[idx].ToString();
            right = s[(idx + 1)..].Trim();
        }

        // "400px <= width" form: name is on the right.
        if (left.Contains('.') || left.Any(char.IsDigit))
        {
            // The numeric side is the value, the other side is the feature.
            return (right, ReverseOp(op), left);
        }
        return (left, op, right);
    }

    private static string ReverseOp(string op) => op switch
    {
        ">=" => "<=",
        "<=" => ">=",
        ">" => "<",
        "<" => ">",
        _ => op
    };

    /// <summary>Splits a chained range — 'value op name op value', as in '400px &lt;= width &lt; 500px'
    /// or '1/2 &lt;= aspect-ratio &lt;= 2/1'. The grammar only allows two comparisons that point the
    /// same way, both &lt;/&lt;= or both &gt;/&gt;=, which is why '900px &gt; width &lt; 1000px' is not a
    /// query at all and 'width &gt;= 400px &lt;= 900px' is not either (CSS Media Queries 4 §4.1;
    /// measured: the first two are false at a 445px viewport, the chained ranges are true).</summary>
    private static bool TrySplitChainedRange(string text, out string name, out string left, out string leftOp,
        out string right, out string rightOp)
    {
        name = left = leftOp = right = rightOp = "";
        var tokens = TokenizeRange(text);
        if (tokens.Count != 5) return false;
        if (!IsComparison(tokens[1]) || !IsComparison(tokens[3])) return false;
        if (!LooksLikeValue(tokens[0]) || !LooksLikeValue(tokens[4]) || LooksLikeValue(tokens[2])) return false;
        bool sameDirection = (IsLesser(tokens[1]) && IsLesser(tokens[3]))
            || (IsGreater(tokens[1]) && IsGreater(tokens[3]));
        if (!sameDirection) return false;

        name = tokens[2];
        leftOp = tokens[1];
        left = tokens[0];
        rightOp = tokens[3];
        right = tokens[4];
        return true;
    }

    /// <summary>Cuts a range expression into words and comparison operators. An operator is one
    /// token, '&lt;=' and '&gt;=' included: spacing the two characters apart would read '400px
    /// &lt;= width' as three comparisons and lose the query.</summary>
    private static List<string> TokenizeRange(string text)
    {
        var tokens = new List<string>();
        var word = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<' || c == '>')
            {
                if (word.Length > 0) { tokens.Add(word.ToString()); word.Clear(); }
                string op = c.ToString();
                if (i + 1 < text.Length && text[i + 1] == '=') { op += "="; i++; }
                tokens.Add(op);
            }
            else if (char.IsWhiteSpace(c))
            {
                if (word.Length > 0) { tokens.Add(word.ToString()); word.Clear(); }
            }
            else word.Append(c);
        }
        if (word.Length > 0) tokens.Add(word.ToString());
        return tokens;
    }

    private static bool IsComparison(string token) => token is "<" or "<=" or ">" or ">=";
    private static bool IsLesser(string op) => op is "<" or "<=";
    private static bool IsGreater(string op) => op is ">" or ">=";

    /// <summary>A media feature name never begins with a digit, and a value of a measurable
    /// feature always does — which is how the two sides of a chained range tell apart.</summary>
    private static bool LooksLikeValue(string token) =>
        token.Length > 0
        && (char.IsAsciiDigit(token[0])
            || (token.Length > 1 && (token[0] == '.' || token[0] == '-' || token[0] == '+') && char.IsAsciiDigit(token[1])));

    /// <summary>A feature with no value: '(hover)', '(color)', '(device-width)'. A keyword feature
    /// is false only when its value is the falsy keyword — the first one its definition lists,
    /// which is 'none' for nearly all of them — and an integer feature is false at zero
    /// (measured: '(color)' and '(update)' are true while '(monochrome)', '(grid)',
    /// '(color-index)' and '(prefers-reduced-motion)' are false).</summary>
    private static bool EvaluateBooleanFeature(string feature, MediaQueryEnvironment env)
    {
        return feature.ToLowerInvariant() switch
        {
            "color" => env.ColorBits > 0,
            "monochrome" => env.MonochromeBits > 0,
            "color-index" => env.ColorIndex > 0,
            "grid" => env.Grid > 0,
            "device-width" => env.DeviceWidth > 0,
            "device-height" => env.DeviceHeight > 0,
            "device-aspect-ratio" => true,
            "hover" => env.HoverCapability == MediaQueryEnvironment.HoverCapabilities.Hover,
            "any-hover" => env.HoverCapability is MediaQueryEnvironment.HoverCapabilities.Hover or MediaQueryEnvironment.HoverCapabilities.None,
            "pointer" => env.PointerCapability == MediaQueryEnvironment.PointerCapabilities.Fine,
            "any-pointer" => env.PointerCapability != MediaQueryEnvironment.PointerCapabilities.None,
            "update" => env.Update != "none",
            "overflow-block" => env.OverflowBlock != "none",
            "overflow-inline" => env.OverflowInline != "none",
            "color-gamut" => env.ColorGamut != "none",
            "scan" => false,
            "prefers-color-scheme" => env.ColorScheme != "none",
            "prefers-reduced-motion" => env.PrefersReducedMotion,
            "prefers-reduced-transparency" => env.PrefersReducedTransparency,
            "prefers-contrast" => false,
            "forced-colors" => env.ForcedColors,
            "scripting" => env.Scripting != "none",
            "display-mode" => env.DisplayMode != "none",
            "aspect-ratio" => true,
            "width" => true,
            "height" => true,
            "resolution" => true,
            "orientation" => true,
            // The legacy WebKit question — can this engine do 3D transforms at all — is still
            // answered by every Blink page, and a reference engine answers it here (measured
            // true). It is a boolean, so it only ever appears without a value.
            "-webkit-transform-3d" => true,
            _ => false
        };
    }

    private static bool EvaluateNamedFeature(string name, string value, MediaQueryEnvironment env)
    {
        // value may be "value", "min value", "max value", "<value" etc.
        // Detect min/max prefix.
        bool hasRange = value.StartsWith('<') || value.StartsWith('>');
        string op = "=";
        if (hasRange)
        {
            if (value.StartsWith("<=")) { op = "<="; value = value[2..]; }
            else if (value.StartsWith(">=")) { op = ">="; value = value[2..]; }
            else if (value.StartsWith('<')) { op = "<"; value = value[1..]; }
            else if (value.StartsWith('>')) { op = ">"; value = value[1..]; }
        }
        else if (value.StartsWith("min ", StringComparison.OrdinalIgnoreCase))
        {
            op = ">=";
            value = value[4..].Trim();
        }
        else if (value.StartsWith("max ", StringComparison.OrdinalIgnoreCase))
        {
            op = "<=";
            value = value[4..].Trim();
        }
        value = value.Trim();

        // Feature names are ASCII case-insensitive, whether they arrive from '(name: value)' or
        // from the range form, so they are canonicalised here rather than at both call sites.
        name = name.Trim().ToLowerInvariant();

        // Strip quotes around string values.
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            value = value[1..^1].Trim();

        // A keyword value is ASCII case-insensitive exactly as its feature name is (measured:
        // '(COLOR-GAMUT: SRGB)' and '(orientation: PORTRAIT)' both match; '(update: FAST)' too).
        value = value.ToLowerInvariant();

        switch (name)
        {
            case "width":
                return CompareLength(env.ViewportWidth, op, ParseLength(value, env));
            case "height":
                return CompareLength(env.ViewportHeight, op, ParseLength(value, env));
            case "min-width":
                return ComparePrefixedLength(env.ViewportWidth, op, ParseLength(value, env), atLeast: true);
            case "max-width":
                return ComparePrefixedLength(env.ViewportWidth, op, ParseLength(value, env), atLeast: false);
            case "min-height":
                return ComparePrefixedLength(env.ViewportHeight, op, ParseLength(value, env), atLeast: true);
            case "max-height":
                return ComparePrefixedLength(env.ViewportHeight, op, ParseLength(value, env), atLeast: false);
            // The device features measure the display, which is a different thing from the
            // viewport: '(width)' asks how wide the viewing area is, '(device-width)' how wide the
            // screen is (CSS Media Queries 4 §7.4). Both read the same parser, so a 'cm' or a 'q'
            // in either of them costs the same.
            case "device-width":
                return CompareLength(env.DeviceWidth, op, ParseLength(value, env));
            case "device-height":
                return CompareLength(env.DeviceHeight, op, ParseLength(value, env));
            case "min-device-width":
                return ComparePrefixedLength(env.DeviceWidth, op, ParseLength(value, env), atLeast: true);
            case "max-device-width":
                return ComparePrefixedLength(env.DeviceWidth, op, ParseLength(value, env), atLeast: false);
            case "min-device-height":
                return ComparePrefixedLength(env.DeviceHeight, op, ParseLength(value, env), atLeast: true);
            case "max-device-height":
                return ComparePrefixedLength(env.DeviceHeight, op, ParseLength(value, env), atLeast: false);
            case "aspect-ratio":
            case "device-aspect-ratio":
            {
                if (!TryParseRatio(value, out var ratio)) return false;
                return CompareDouble(ActualRatio(name == "device-aspect-ratio", env), op, ratio);
            }
            // 'min-'/'max-' fixes the comparison, so '(min-aspect-ratio: 1/2)' asks for a ratio of
            // at least one half rather than for one equal to it (measured true at a 445x481
            // viewport; the plain form answers the same question only with an operator of its own).
            case "min-aspect-ratio":
            case "max-aspect-ratio":
            case "min-device-aspect-ratio":
            case "max-device-aspect-ratio":
            {
                if (!TryParseRatio(value, out var ratio)) return false;
                bool device = name.EndsWith("device-aspect-ratio");
                bool atLeast = name.StartsWith("min-");
                return ComparePrefixedDouble(ActualRatio(device, env), op, ratio, atLeast);
            }
            case "orientation":
                if (op != "=") return false;
                if (value == "portrait") return env.ViewportHeight >= env.ViewportWidth;
                if (value == "landscape") return env.ViewportWidth > env.ViewportHeight;
                return false;
            case "resolution":
            {
                double res = ParseResolution(value);
                if (res < 0) return false;
                return CompareDouble(env.ResolutionDppx, op, res);
            }
            // 'min-resolution' / 'max-resolution' are the same measurement as 'resolution' with
            // the comparison fixed, and the plain form takes no operator of its own. A reference
            // engine answers all three (measured at a device pixel ratio of 1: '(min-resolution:
            // 37dpcm)' matches, '(min-resolution: 39dpcm)' does not — 37dpcm is 0.979dppx).
            case "min-resolution":
                return ComparePrefixedDouble(env.ResolutionDppx, op, ParseResolution(value), atLeast: true);
            case "max-resolution":
                return ComparePrefixedDouble(env.ResolutionDppx, op, ParseResolution(value), atLeast: false);
            // 'color' counts the bits per colour component, not per pixel: an ordinary 24-bit
            // display answers 8, so '(color: 8)' matches and '(color: 24)' does not (measured).
            case "color":
                if (string.IsNullOrEmpty(value)) return env.ColorBits > 0;
                return TryParseInt(value, out var colorBits) && CompareDouble(env.ColorBits, op, colorBits);
            case "min-color":
                return ComparePrefixedInt(env.ColorBits, op, value, atLeast: true);
            case "max-color":
                return ComparePrefixedInt(env.ColorBits, op, value, atLeast: false);
            case "monochrome":
                if (string.IsNullOrEmpty(value)) return env.MonochromeBits > 0;
                return TryParseInt(value, out var monoBits) && CompareDouble(env.MonochromeBits, op, monoBits);
            case "min-monochrome":
                return ComparePrefixedInt(env.MonochromeBits, op, value, atLeast: true);
            case "max-monochrome":
                return ComparePrefixedInt(env.MonochromeBits, op, value, atLeast: false);
            // 'color-index' is the number of entries in the colour lookup table. A display that
            // addresses colours directly has none, so '(color-index: 0)' matches while the bare
            // '(color-index)' does not (measured).
            case "color-index":
                if (string.IsNullOrEmpty(value)) return env.ColorIndex > 0;
                return TryParseInt(value, out var entries) && CompareDouble(env.ColorIndex, op, entries);
            case "min-color-index":
                return ComparePrefixedInt(env.ColorIndex, op, value, atLeast: true);
            case "max-color-index":
                return ComparePrefixedInt(env.ColorIndex, op, value, atLeast: false);
            case "grid":
                if (string.IsNullOrEmpty(value)) return env.Grid > 0;
                return TryParseInt(value, out var gridType) && CompareDouble(env.Grid, op, gridType);
            case "prefers-color-scheme":
                if (op != "=") return false;
                return value.Equals(env.ColorScheme, StringComparison.OrdinalIgnoreCase);
            case "prefers-reduced-motion":
                if (op != "=") return false;
                return value == "reduce" ? env.PrefersReducedMotion : value == "no-preference" && !env.PrefersReducedMotion;
            case "prefers-reduced-transparency":
                if (op != "=") return false;
                return value == "reduce" ? env.PrefersReducedTransparency : value == "no-preference" && !env.PrefersReducedTransparency;
            case "prefers-contrast":
                if (op != "=") return false;
                return value == "no-preference" || (value == "more" && env.PrefersContrastMore);
            case "forced-colors":
                if (op != "=") return false;
                return value == "none" ? !env.ForcedColors : value == "active" && env.ForcedColors;
            case "hover":
            case "any-hover":
                if (op != "=") return false;
                return value == "hover" ? env.HoverCapability == MediaQueryEnvironment.HoverCapabilities.Hover
                    : value == "none" && env.HoverCapability == MediaQueryEnvironment.HoverCapabilities.None;
            case "pointer":
            case "any-pointer":
                if (op != "=") return false;
                return value == "fine" ? env.PointerCapability == MediaQueryEnvironment.PointerCapabilities.Fine
                    : value == "coarse" ? env.PointerCapability == MediaQueryEnvironment.PointerCapabilities.Coarse
                    : value == "none" && env.PointerCapability == MediaQueryEnvironment.PointerCapabilities.None;
            case "light-level":
                if (op != "=") return false;
                return value switch
                {
                    "dim" => env.LightLevel is MediaQueryEnvironment.LightLevels.Dim or MediaQueryEnvironment.LightLevels.Normal,
                    "normal" => env.LightLevel == MediaQueryEnvironment.LightLevels.Normal,
                    "washed" => env.LightLevel == MediaQueryEnvironment.LightLevels.Washed,
                    _ => false
                };
            // 'update' says how often the rendering is expected to change. A desktop browser is
            // 'fast', which is also why the bare '(update)' is true (measured).
            case "update":
                if (op != "=") return false;
                return string.IsNullOrEmpty(value) ? env.Update != "none" : value == env.Update;
            case "overflow-block":
            case "overflow-inline":
            {
                if (op != "=") return false;
                string actual = name == "overflow-block" ? env.OverflowBlock : env.OverflowInline;
                return string.IsNullOrEmpty(value) ? actual != "none" : value == actual;
            }
            // A gamut query asks whether the display covers the colour space named. The engine
            // draws into sRGB, so that one matches and the wider spaces do not (measured: 'srgb'
            // true while 'p3', 'dci-p3' and 'rec2020' are false; the bare form true).
            case "color-gamut":
                if (op != "=") return false;
                return string.IsNullOrEmpty(value) ? env.ColorGamut != "none" : value == env.ColorGamut;
            // The vendor spellings of the device pixel ratio measure exactly what 'resolution'
            // does, and they are what most stylesheets in the wild ask for. The plain
            // 'device-pixel-ratio' and 'dpr' are not media features at all and stay unknown, which
            // is how a reference engine answers them (measured false).
            case "-webkit-device-pixel-ratio":
            {
                double webkitRes = ParseResolution(value);
                if (webkitRes < 0) return false;
                return CompareDouble(env.ResolutionDppx, op, webkitRes);
            }
            case "-webkit-min-device-pixel-ratio":
                return ComparePrefixedDouble(env.ResolutionDppx, op, ParseResolution(value), atLeast: true);
            case "-webkit-max-device-pixel-ratio":
                return ComparePrefixedDouble(env.ResolutionDppx, op, ParseResolution(value), atLeast: false);
            case "display-mode":
                if (op != "=") return false;
                return env.DisplayMode.Equals(value, StringComparison.OrdinalIgnoreCase);
            case "scripting":
                if (op != "=") return false;
                return value.Equals(env.Scripting, StringComparison.OrdinalIgnoreCase);
            default:
                // Unknown feature: per spec, unknown features make the query false
                // unless inside @supports. We conservatively return false.
                return false;
        }
    }

    private static bool CompareLength(float actual, string op, float target)
    {
        if (float.IsNaN(target)) return false;
        return op switch
        {
            ">=" => actual >= target,
            "<=" => actual <= target,
            ">" => actual > target,
            "<" => actual < target,
            _ => Math.Abs(actual - target) < 0.01f
        };
    }

    /// <summary>A 'min-'/'max-' prefixed length feature. The prefix IS the comparison, so the
    /// feature takes no operator of its own and '(min-width: &lt;= 400px)' is not a query at all
    /// — it matches nothing whatever the viewport is (CSS Media Queries 4 §4.4; measured).</summary>
    private static bool ComparePrefixedLength(float actual, string op, float threshold, bool atLeast)
    {
        if (float.IsNaN(threshold) || op != "=") return false;
        return atLeast ? actual >= threshold : actual <= threshold;
    }

    /// <summary>The same for a prefixed double-valued feature; a negative reading is
    /// <see cref="ParseResolution"/>'s signal that the value was not a resolution at all.</summary>
    private static bool ComparePrefixedDouble(double actual, string op, double value, bool atLeast)
    {
        if (value < 0 || op != "=") return false;
        return atLeast ? actual >= value : actual <= value;
    }

    /// <summary>A 'min-'/'max-' prefixed integer feature. An empty or unparseable value is not a
    /// threshold, and the prefix has already made the comparison.</summary>
    private static bool ComparePrefixedInt(int actual, string op, string value, bool atLeast)
    {
        if (op != "=" || !TryParseInt(value, out var threshold)) return false;
        return atLeast ? actual >= threshold : actual <= threshold;
    }

    /// <summary>The ratio a ratio feature measures: the viewport's aspect, or the display's.</summary>
    private static double ActualRatio(bool device, MediaQueryEnvironment env)
    {
        float w = device ? env.DeviceWidth : env.ViewportWidth;
        float h = device ? env.DeviceHeight : env.ViewportHeight;
        return h > 0 ? w / (double)h : 0;
    }

    private static bool CompareDouble(double actual, string op, double target) => op switch
    {
        ">=" => actual >= target,
        "<=" => actual <= target,
        ">" => actual > target,
        "<" => actual < target,
        _ => Math.Abs(actual - target) < 1e-9
    };

    /// <summary>A media feature length in CSS pixels. A query is not attached to a box, so every
    /// font-relative unit — 'em', 'rem', 'ch', 'ex', 'lh' — is measured against the initial value
    /// of 'font-size', 16px, and not against the document's root font size, which is what CSS
    /// Media Queries 4 §6 prescribes and what a reference engine does (measured at a 445px
    /// viewport: '(min-width: 27em)' matches and '(min-width: 28em)' does not whatever the page
    /// sets 'html { font-size: … }' to, so the basis is 16px and not 30px or 10px). The absolute
    /// and viewport units come from the same parser a property on the page uses, so a feature
    /// cannot price a centimetre, a 'q' or a 'PX' differently from a 'width'. A percentage is
    /// not a &lt;length&gt; here at all: '(max-width: 50%)' never matches (measured).</summary>
    private static float ParseLength(string value, MediaQueryEnvironment env)
    {
        const float initialFontSize = Acrux.Core.Dom.Length.FontSizeMedium;
        var text = value.Trim();
        if (text.EndsWith("%", StringComparison.Ordinal)) return float.NaN;
        // A comparison operator inside the 'length' means the range splitter handed over the
        // tail of a malformed query — 'width >= 400px <= 900px' is not a query, and the
        // property-side parser is lenient enough to read a number out of '400px <= 900px'.
        if (text.IndexOf('<') >= 0 || text.IndexOf('>') >= 0) return float.NaN;
        if (!Acrux.Core.Dom.Length.IsLength(text)) return float.NaN;
        // No element's font may leak in from the cascade pass that is evaluating the rule.
        using var _scope = Acrux.Core.Dom.FontUnitContext.Use(null);
        float px = Acrux.Core.Dom.Length.Parse(text)
            .ToPixels(initialFontSize, initialFontSize, env.ViewportWidth, env.ViewportHeight);
        return float.IsNaN(px) ? float.NaN : px;
    }

    private static float ParseFloat(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : float.NaN;

    private static bool TryParseInt(string s, out int v) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static bool TryParseRatio(string value, out double ratio)
    {
        ratio = 0;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Both sides are read as doubles. A ratio is compared for equality against a viewport
        // that is itself a quotient, and a float's 7 digits are not enough for that: '445/1600'
        // read as floats is 1.3e-8 away from the same quotient in doubles, which is a whole
        // 'false' on an exact-equality query.
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double single) && single > 0)
        {
            ratio = single;
            return true;
        }
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double h) &&
            h > 0 && w > 0)
        {
            ratio = w / h;
            return true;
        }
        return false;
    }

    private static double ParseResolution(string value)
    {
        value = value.Trim();
        if (value.EndsWith("dppx", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^4]);
        if (value.EndsWith("dpi", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^3]) / 96.0;
        // Dots per centimetre become dots per inch by the centimetre's own px length, taken
        // from the shared unit table rather than from a rounded copy of it (CSS Values 3 §6.6).
        if (value.EndsWith("dpcm", StringComparison.OrdinalIgnoreCase))
        {
            Acrux.Core.Dom.Length.TryAbsoluteUnitPixels("cm", out var pixelsPerCm);
            return ParseFloat(value[..^4]) / pixelsPerCm;
        }
        if (value.EndsWith("x", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
            return ParseFloat(value[..^1]);
        var f = ParseFloat(value);
        return float.IsNaN(f) ? -1 : f;
    }
}

/// <summary>
/// Environment snapshot used to evaluate media queries. All measurements are in
/// CSS pixels / 96dpi units.
/// </summary>
public class MediaQueryEnvironment
{
    public float ViewportWidth { get; set; } = 1024;
    public float ViewportHeight { get; set; } = 768;
    /// <summary>The display, which is a different measurement from the viewport: a page that is
    /// 445px wide on a 1920px screen answers '(width: 445px)' and '(device-width: 1920px)' both
    /// with 'true'. Both come from <see cref="Acrux.Core.Display.ScreenMetrics"/>, the same source
    /// 'window.screen' reads, so neither can contradict the other.</summary>
    public int DeviceWidth { get; set; } = Acrux.Core.Display.ScreenMetrics.Width;
    public int DeviceHeight { get; set; } = Acrux.Core.Display.ScreenMetrics.Height;
    public string ColorScheme { get; set; } = "light";
    public string MediaType { get; set; } = "screen";
    public string DisplayMode { get; set; } = "browser";
    public string Scripting { get; set; } = "enabled";
    public double ResolutionDppx { get; set; } = 1.0;
    /// <summary>Bits per colour component, which is what the 'color' feature counts — not the
    /// 24 bits per pixel of <see cref="Acrux.Core.Display.ScreenMetrics.ColorDepth"/> that
    /// 'screen.colorDepth' reports (measured: '(color: 8)' matches and '(color: 24)' does not).</summary>
    public int ColorBits { get; set; } = 8;
    public int MonochromeBits { get; set; } = 0;
    /// <summary>Entries in the display's colour lookup table. Colours are addressed directly, so
    /// there are none and the bare '(color-index)' is false (measured).</summary>
    public int ColorIndex { get; set; } = 0;
    /// <summary>Non-zero only on a device that draws as a grid of characters rather than as a
    /// bitmap; a window on a bitmap display answers '(grid: 0)' (measured).</summary>
    public int Grid { get; set; } = 0;
    /// <summary>How often the rendering is expected to change: 'fast' on a desktop browser, so a
    /// stylesheet's '(update)' and '(update: fast)' both match (measured).</summary>
    public string Update { get; set; } = "fast";
    /// <summary>How each axis scrolls. A window scrolls its content, so both are 'scroll'
    /// (measured), and only a paged medium would answer 'paged' on the block axis.</summary>
    public string OverflowBlock { get; set; } = "scroll";
    public string OverflowInline { get; set; } = "scroll";
    /// <summary>The widest colour space the display covers. The engine draws into sRGB; a query
    /// for 'p3' or 'rec2020' therefore does not match (measured).</summary>
    public string ColorGamut { get; set; } = "srgb";
    public bool PrefersReducedMotion { get; set; }
    public bool PrefersReducedTransparency { get; set; }
    public bool PrefersContrastMore { get; set; }
    public bool ForcedColors { get; set; }
    public LightLevels LightLevel { get; set; } = LightLevels.Normal;
    public HoverCapabilities HoverCapability { get; set; } = HoverCapabilities.Hover;
    public PointerCapabilities PointerCapability { get; set; } = PointerCapabilities.Fine;

    public enum LightLevels { Normal, Dim, Washed }
    public enum HoverCapabilities { None, Hover }
    public enum PointerCapabilities { None, Coarse, Fine }

    public static MediaQueryEnvironment Default(float viewportWidth, float viewportHeight, string colorScheme)
        => new() { ViewportWidth = viewportWidth, ViewportHeight = viewportHeight, ColorScheme = colorScheme };
}
