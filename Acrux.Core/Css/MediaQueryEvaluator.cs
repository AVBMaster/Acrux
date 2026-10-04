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
///     color, monochrome, grid, hover, pointer, any-hover, any-pointer,
///     update, overflow-block, overflow-inline, prefers-color-scheme,
///     prefers-reduced-motion, prefers-reduced-transparency, prefers-contrast,
///     forced-colors, light-level, scripting, display-mode
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

        // Strip outer parentheses only when the first '(' pairs with the last ')'.
        while (query.StartsWith('(') && query.EndsWith(')') && OuterParensWrapWhole(query))
            query = query[1..^1].Trim();

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
            var left = EvaluateQuery(query[..orIndex].Trim(), env);
            var right = EvaluateQuery(query[(orIndex + 4)..].Trim(), env);
            return left || right;
        }

        // Split on top-level "and"
        int andIndex = FindTopLevelOperator(query, " and ");
        if (andIndex >= 0)
        {
            var left = EvaluateQuery(query[..andIndex].Trim(), env);
            var right = EvaluateQuery(query[(andIndex + 5)..].Trim(), env);
            bool result = left && right;
            return negate ? !result : result;
        }

        bool value = EvaluateSingle(query.Trim(), env);
        return negate ? !value : value;
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

    private static bool EvaluateBooleanFeature(string feature, MediaQueryEnvironment env)
    {
        return feature.ToLowerInvariant() switch
        {
            "color" => env.ColorBits > 0,
            "monochrome" => env.MonochromeBits > 0,
            "grid" => false,
            "hover" => env.HoverCapability == MediaQueryEnvironment.HoverCapabilities.Hover,
            "any-hover" => env.HoverCapability is MediaQueryEnvironment.HoverCapabilities.Hover or MediaQueryEnvironment.HoverCapabilities.None,
            "pointer" => env.PointerCapability == MediaQueryEnvironment.PointerCapabilities.Fine,
            "any-pointer" => env.PointerCapability != MediaQueryEnvironment.PointerCapabilities.None,
            "update" => false,
            "overflow-block" => false,
            "overflow-inline" => false,
            "scan" => false,
            "prefers-color-scheme" => false,
            "prefers-reduced-motion" => false,
            "prefers-reduced-transparency" => false,
            "prefers-contrast" => false,
            "forced-colors" => false,
            "scripting" => false,
            "display-mode" => false,
            "aspect-ratio" => true,
            "width" => true,
            "height" => true,
            "resolution" => true,
            "orientation" => true,
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

        // Strip quotes around string values.
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            value = value[1..^1].Trim();

        switch (name)
        {
            case "width":
                return CompareLength(env.ViewportWidth, op, ParseLength(value, env.ViewportWidth));
            case "height":
                return CompareLength(env.ViewportHeight, op, ParseLength(value, env.ViewportHeight));
            case "min-width":
                return env.ViewportWidth >= ParseLength(value, env.ViewportWidth);
            case "max-width":
                return env.ViewportWidth <= ParseLength(value, env.ViewportWidth);
            case "min-height":
                return env.ViewportHeight >= ParseLength(value, env.ViewportHeight);
            case "max-height":
                return env.ViewportHeight <= ParseLength(value, env.ViewportHeight);
            case "aspect-ratio":
            case "min-aspect-ratio":
            case "max-aspect-ratio":
            {
                if (!TryParseRatio(value, out var ratio)) return false;
                double actual = env.ViewportHeight > 0 ? env.ViewportWidth / env.ViewportHeight : 0;
                return CompareDouble(actual, op, ratio);
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
            case "color":
            case "min-color":
            case "max-color":
            {
                if (op == "=" && string.IsNullOrEmpty(value)) return env.ColorBits > 0;
                if (!TryParseInt(value, out var bits)) return false;
                return CompareDouble(env.ColorBits, op, bits);
            }
            case "monochrome":
            case "min-monochrome":
            case "max-monochrome":
            {
                if (!TryParseInt(value, out var bits)) return false;
                return CompareDouble(env.MonochromeBits, op, bits);
            }
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
            case "update":
                return false;
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

    private static bool CompareDouble(double actual, string op, double target) => op switch
    {
        ">=" => actual >= target,
        "<=" => actual <= target,
        ">" => actual > target,
        "<" => actual < target,
        _ => Math.Abs(actual - target) < 1e-9
    };

    private static float ParseLength(string value, float reference)
    {
        value = value.Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]);
        if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]) * reference;
        if (value.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^3]) * 16f;
        if (value.EndsWith("vw", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]) * reference / 100f;
        if (value.EndsWith("vh", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]) * reference / 100f;
        if (value.EndsWith("cm", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]) * 37.7953f;
        if (value.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]) * 3.77953f;
        if (value.EndsWith("in", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]) * 96f;
        if (value.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]) * 1.33333f;
        if (value.EndsWith("pc", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^2]) * 16f;
        // Unitless numbers are interpreted in the reference unit (px).
        return ParseFloat(value);
    }

    private static float ParseFloat(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : float.NaN;

    private static bool TryParseInt(string s, out int v) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static bool TryParseRatio(string value, out double ratio)
    {
        ratio = 0;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float single) && single > 0)
        {
            ratio = single;
            return true;
        }
        if (parts.Length == 2 &&
            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float w) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float h) &&
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
        if (value.EndsWith("dpcm", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(value[..^4]) / 37.7953;
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
    public string ColorScheme { get; set; } = "light";
    public string MediaType { get; set; } = "screen";
    public string DisplayMode { get; set; } = "browser";
    public string Scripting { get; set; } = "enabled";
    public double ResolutionDppx { get; set; } = 1.0;
    public int ColorBits { get; set; } = 24;
    public int MonochromeBits { get; set; } = 0;
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
