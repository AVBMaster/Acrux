using Acrux.Core.Dom;
using Acrux.Core.Dom.Animations;

namespace Acrux.Core.Css;

/// <summary>
/// The computed value of the CSS Transforms 2 individual transform properties
/// (<c>translate</c>, <c>rotate</c>, <c>scale</c>) and of the properties that surround them
/// (<c>perspective</c>, <c>perspective-origin</c>, <c>transform-origin</c>,
/// <c>backface-visibility</c>, <c>transform-box</c>).
///
/// Each method returns the value in the spelling a reference engine reports from
/// <c>getComputedStyle</c>, or null when the value does not match the grammar — which is what the
/// cascade needs, because a declaration the grammar rejects is dropped without touching the
/// property. Everything is canonicalised once, at computed-value time, so the paint path and the
/// CSSOM read the same text instead of each re-reading what the page typed:
///   · absolute and font-relative lengths become pixels (a <c>calc()</c> that carries a percentage
///     cannot be, and stays the expression the page wrote);
///   · percentages stay percentages, because the box they resolve against is not known yet;
///   · numbers and angles are printed with six significant digits and in the canonical unit.
/// </summary>
public static class IndividualTransforms
{
    /// <summary>'translate: none | &lt;length-percentage&gt; [ &lt;length-percentage&gt; &lt;length&gt;? ]?'
    /// (CSS Transforms 2 §4.1). The computed value is the shortest list that still says the same
    /// thing (measured): a zero depth is dropped, and only then a zero y — a two-component list is
    /// x and y, so a surviving depth keeps its zero y. A bare zero is a length and prints '0px';
    /// '0% 0%' keeps both, a percentage not being a length.</summary>
    public static string? CanonicalTranslate(string? value, ComputedStyle style)
    {
        if (!TryTokens(value, out var tokens)) return null;
        if (IsNone(tokens)) return "none";
        if (tokens.Length > 3) return null;

        var parts = new List<string>(3);
        for (int i = 0; i < tokens.Length; i++)
        {
            // The depth axis has no box to be a percentage of, so only the first two accept one.
            if (!TryLengthText(tokens[i], style, allowPercent: i < 2, out var text)) return null;
            parts.Add(text);
        }
        if (parts.Count == 3 && IsZeroPixels(parts[2])) parts.RemoveAt(2);
        if (parts.Count == 2 && IsZeroPixels(parts[1])) parts.RemoveAt(1);
        return string.Join(" ", parts);
    }

    /// <summary>'rotate: none | &lt;angle&gt; | [x|y|z] &lt;angle&gt; | &lt;number&gt;{3} &lt;angle&gt;'
    /// (CSS Transforms 2 §4.2). Angles compute to 'deg'; a vector along one coordinate axis becomes
    /// that axis's keyword with its sign folded into the angle, and 'z' — the axis a plain
    /// <c>rotate()</c> already turns around — disappears. A bare <c>&lt;number&gt;</c> is not an
    /// angle, which is what makes 'rotate: 45' as invalid as 'rotate: 0'.</summary>
    public static string? CanonicalRotate(string? value)
    {
        if (!TryTokens(value, out var tokens)) return null;
        if (IsNone(tokens)) return "none";
        if (tokens.Length > 4) return null;

        // The angle and the axis it turns about are an '&&' combination (CSS Transforms 2 §4.2),
        // so either may come first — '90deg x' and 'x 90deg' are the same declaration, and so are
        // '90deg 1 0 0' and '1 0 0 90deg' (measured). The list is therefore split by what each
        // token is rather than by where it sits: exactly one angle, and then nothing, one axis
        // keyword, or three numbers.
        int angleAt = -1;
        double angle = 0;
        var rest = new List<string>(3);
        for (int i = 0; i < tokens.Length; i++)
        {
            if (angleAt < 0 && TryAngleDegrees(tokens[i], out var candidate))
            {
                angleAt = i;
                angle = candidate;
                continue;
            }
            rest.Add(tokens[i]);
        }
        if (angleAt < 0) return null;
        if (rest.Count == 0) return AngleText(angle);

        if (rest.Count == 1)
            switch (rest[0].ToLowerInvariant())
            {
                case "x": return "x " + AngleText(angle);
                case "y": return "y " + AngleText(angle);
                // 'z 45deg' and '45deg' are the same rotation, and the engine reports the shorter.
                case "z": return AngleText(angle);
                default: return null;
            }

        if (rest.Count == 3
            && TryNumber(rest[0], out var x) && TryNumber(rest[1], out var y) && TryNumber(rest[2], out var z))
        {
            var axis = AxisOf(x, y, z);
            if (axis == null)
                return $"{CssValueTokenizer.Num(x)} {CssValueTokenizer.Num(y)} {CssValueTokenizer.Num(z)} {AngleText(angle)}";
            double signed = axis.Value.Sign < 0 ? -angle : angle;
            return axis.Value.Name switch
            {
                "x" => "x " + AngleText(signed),
                "y" => "y " + AngleText(signed),
                _ => AngleText(signed),
            };
        }
        return null;
    }

    /// <summary>'scale: none | ( &lt;number&gt; | &lt;percentage&gt; ){1,3}' (CSS Transforms 2 §4.3).
    /// A percentage is a number divided by 100, and the computed value is again the shortest list:
    /// a depth of one is dropped, then a y that equals x.</summary>
    public static string? CanonicalScale(string? value)
    {
        if (!TryTokens(value, out var tokens)) return null;
        if (IsNone(tokens)) return "none";
        if (tokens.Length > 3) return null;

        var parts = new List<double>(3);
        foreach (var token in tokens)
        {
            if (TryNumber(token, out var number))
            {
                parts.Add(number);
            }
            else if (TryPercentage(token, out var percent))
            {
                parts.Add(percent / 100);
            }
            else return null;
        }
        if (parts.Count == 3 && parts[2] == 1) parts.RemoveAt(2);
        if (parts.Count == 2 && parts[1] == parts[0]) parts.RemoveAt(1);
        return string.Join(" ", parts.Select(CssValueTokenizer.Num));
    }

    /// <summary>'perspective: none | &lt;length&gt;' with a non-negative length (CSS Transforms 1 §5).
    /// Neither a percentage nor the keyword 'auto' is in the grammar, and a metre is not a CSS unit
    /// at all (CSS Values 3 §6.7 lists cm, mm, Q, in, pc, pt and px).</summary>
    public static string? CanonicalPerspective(string? value, ComputedStyle style)
    {
        if (!TryTokens(value, out var tokens)) return null;
        if (IsNone(tokens)) return "none";
        if (tokens.Length != 1) return null;
        if (!TryLengthText(tokens[0], style, allowPercent: false, out var text)) return null;
        return IsNegativePixels(text) ? null : text;
    }

    private static readonly string[] TransformBoxKeywords =
        ["content-box", "border-box", "fill-box", "stroke-box", "view-box"];

    private static readonly string[] TransformStyleKeywords = ["flat", "preserve-3d"];

    /// <summary>'transform-style: flat | preserve-3d' (CSS Transforms 2 §5), case-insensitive,
    /// initial 'flat'. 'preserve-flat' and 'auto' are the spellings a page reaches for and the
    /// grammar has neither.</summary>
    public static bool TryTransformStyle(string? value, out string keyword)
    {
        keyword = "";
        if (!TryTokens(value, out var tokens) || tokens.Length != 1) return false;
        var lower = tokens[0].ToLowerInvariant();
        if (Array.IndexOf(TransformStyleKeywords, lower) < 0) return false;
        keyword = lower;
        return true;
    }

    /// <summary>'transform-box' (CSS Transforms 1 §3): five boxes, case-insensitive, initial
    /// 'view-box'. 'auto' and 'margin-box' belong to no grammar and are invalid.</summary>
    public static bool TryTransformBox(string? value, out string keyword)
    {
        keyword = "";
        if (!TryTokens(value, out var tokens) || tokens.Length != 1) return false;
        var lower = tokens[0].ToLowerInvariant();
        if (Array.IndexOf(TransformBoxKeywords, lower) < 0) return false;
        keyword = lower;
        return true;
    }

    /// <summary>'backface-visibility: visible | hidden' (CSS Transforms 1 §4), case-insensitive.</summary>
    public static bool TryBackfaceVisibility(string? value, out string keyword)
    {
        keyword = "";
        if (!TryTokens(value, out var tokens) || tokens.Length != 1) return false;
        var lower = tokens[0].ToLowerInvariant();
        if (lower is not ("visible" or "hidden")) return false;
        keyword = lower;
        return true;
    }

    /// <summary>'transform-origin' and 'perspective-origin' share one grammar (CSS Transforms 1 §3,
    /// §5; CSS Transforms 2 §6): one or two positional values — a keyword for its own axis, a length
    /// or percentage for either — and, for 'transform-origin' only, a third bare <c>&lt;length&gt;</c>
    /// that is the depth. Measured consequences: 'right 10px' is x=right, y=10px and NOT an offset
    /// from the right edge, a keyword cannot be used twice, and a percentage in the depth position
    /// takes the whole declaration down with it.</summary>
    public static string? CanonicalOrigin(string? value, ComputedStyle style, bool allowDepth)
    {
        if (!TryTokens(value, out var tokens)) return null;
        if (tokens.Length == 0 || tokens.Length > 3) return null;

        string? depth = null;
        if (tokens.Length == 3)
        {
            if (!allowDepth) return null;
            if (!TryLengthText(tokens[2], style, allowPercent: false, out depth)) return null;
            tokens = [tokens[0], tokens[1]];
        }

        string? x = null, y = null;
        foreach (var token in tokens)
        {
            var keyword = token.ToLowerInvariant();
            switch (keyword)
            {
                case "left":
                    if (x != null) return null;
                    x = "0%";
                    continue;
                case "right":
                    if (x != null) return null;
                    x = "100%";
                    continue;
                case "top":
                    if (y != null) return null;
                    y = "0%";
                    continue;
                case "bottom":
                    if (y != null) return null;
                    y = "100%";
                    continue;
                case "center":
                    if (x == null) x = "50%";
                    else if (y == null) y = "50%";
                    else return null;
                    continue;
            }
            if (!TryLengthText(token, style, allowPercent: true, out var text)) return null;
            if (x == null) x = text;
            else if (y == null) y = text;
            else return null;
        }

        // An axis nobody named stays at its initial 'center' (measured: 'transform-origin: 10px'
        // reads back '10px 50%', and 'bottom' reads back '50% 100%').
        x ??= "50%";
        y ??= "50%";
        if (depth != null && !IsZeroPixels(depth)) return $"{x} {y} {depth}";
        return $"{x} {y}";
    }

    /// <summary>The axis a rotation vector names: one of the three coordinate axes, or null for a
    /// vector that is not along one (including the zero vector, which the engine reports verbatim).</summary>
    private static (string Name, int Sign)? AxisOf(double x, double y, double z)
    {
        int nonZero = (x != 0 ? 1 : 0) + (y != 0 ? 1 : 0) + (z != 0 ? 1 : 0);
        if (nonZero != 1) return null;
        if (x != 0) return ("x", x < 0 ? -1 : 1);
        if (y != 0) return ("y", y < 0 ? -1 : 1);
        return ("z", z < 0 ? -1 : 1);
    }

    private static string AngleText(double degrees) => CssValueTokenizer.Num(degrees) + "deg";

    /// <summary>A value is a list of space-separated components; a comma is not part of any of these
    /// grammars, so it makes the whole declaration invalid — outside a math function, where
    /// 'min(10px, 20px)' is one component and not two.</summary>
    private static bool TryTokens(string? value, out string[] tokens)
    {
        tokens = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        int depth = 0;
        foreach (var c in value)
        {
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (c == ',' && depth == 0) return false;
        }
        // Split at top-level spaces only: 'min(10px, 20px)' is one component, not two.
        var parts = new List<string>();
        int start = -1;
        depth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (start >= 0) { parts.Add(value[start..i]); start = -1; }
                continue;
            }
            if (start < 0 && !char.IsWhiteSpace(c)) start = i;
        }
        if (start >= 0) parts.Add(value[start..]);
        // A component that opens a parenthesis has to open a math function: a url(), a var()
        // the substitution never resolved, or an unmatched ')' is not a transform value.
        foreach (var part in parts)
            if (part.Contains('(') && !StartsWithMathFunction(part)) return false;
        tokens = parts.ToArray();
        return tokens.Length > 0;
    }

    private static bool StartsWithMathFunction(string value)
        => value.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("max(", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase);

    private static bool IsNone(string[] tokens)
        => tokens.Length == 1 && tokens[0].Equals("none", StringComparison.OrdinalIgnoreCase);

    /// <summary>One &lt;length&gt; or &lt;length-percentage&gt; in the spelling the reference engine
    /// uses. Returns null when the token is not a length at all, or is one the property's grammar
    /// does not allow (a percentage where <paramref name="allowPercent"/> is false, 'auto',
    /// 'max-content').</summary>
    private static bool TryLengthText(string token, ComputedStyle style, bool allowPercent, out string text)
    {
        text = token;
        if (!Length.IsLength(token))
            return allowPercent && TryPercentage(token, out var percent)
                && (text = CssValueTokenizer.Num(percent) + "%") != null;

        var length = Length.Parse(token);
        switch (length)
        {
            // Parse is lenient: an unknown word and 'auto' both come back as AutoLength, and
            // 'max-content' as an intrinsic. None of them is a length these properties accept.
            case AutoLength:
            case IntrinsicLength:
                return false;
            case PercentLength percentLength:
                if (!allowPercent) return false;
                text = CssValueTokenizer.Num(percentLength.Value * 100) + "%";
                return true;
        }

        // A math expression that still carries a percentage resolves against a box nobody has
        // measured yet, so the engine hands the expression back — as does ours.
        if (length is MathLength && token.Contains('%'))
        {
            text = token;
            return true;
        }
        // Viewport- and container-relative units are resolved here against nothing, so they keep
        // their own spelling rather than collapsing to 0px.
        if (IsRelativeUnitOutsideTheBox(token))
        {
            text = token;
            return true;
        }

        float reference = style?.FontSize is > 0 ? style.FontSize : Length.FontSizeMedium;
        float px = length.ToPixels(reference, Length.FontSizeMedium, 0f, 0f);
        if (float.IsNaN(px) || float.IsInfinity(px)) return false;
        text = CssValueTokenizer.Num(px) + "px";
        return true;
    }

    private static bool IsRelativeUnitOutsideTheBox(string token)
    {
        string suffix = UnitSuffix(token);
        return suffix is "vw" or "vh" or "vmin" or "vmax" or "vi" or "vb"
            or "svw" or "svh" or "lvw" or "lvh" or "dvw" or "dvh"
            or "cqw" or "cqh" or "cqi" or "cqb" or "cqmin" or "cqmax";
    }

    /// <summary>The letters a numeric dimension ends with, lowercase — the unit of '10px' is 'px'.</summary>
    private static string UnitSuffix(string token)
    {
        int i = token.Length;
        while (i > 0 && char.IsLetter(token[i - 1])) i--;
        return token[i..].ToLowerInvariant();
    }

    private static bool TryPercentage(string token, out double percent)
    {
        percent = 0;
        if (token.Length < 2 || !token.EndsWith("%", StringComparison.Ordinal)) return false;
        if (!TryNumber(token[..^1], out percent)) return false;
        return true;
    }

    /// <summary>A CSS &lt;number&gt;: no unit, no 'auto', no infinity and not 'none'. Scientific
    /// notation is a number, and the engine prints it back in positional notation ('1e2px' is
    /// '100px'), which is what <see cref="CssValueTokenizer.Num"/> does.</summary>
    private static bool TryNumber(string token, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(token)) return false;
        int i = 0;
        if (token[0] is '+' or '-') i++;
        bool digits = false, dot = false, exponent = false;
        while (i < token.Length)
        {
            char c = token[i];
            if (char.IsAsciiDigit(c)) { digits = true; }
            else if (c == '.' && !dot && !exponent) dot = true;
            else if ((c == 'e' || c == 'E') && digits && !exponent) { exponent = true; if (i + 1 < token.Length && (token[i + 1] is '+' or '-')) i++; }
            else return false;
            i++;
        }
        if (!digits) return false;
        if (!double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>'deg' is the canonical angle unit, so turn (360°), grad (0.9°) and rad (180/π°)
    /// are multiplied into it; a bare number is not an angle.</summary>
    private static bool TryAngleDegrees(string token, out double degrees)
    {
        degrees = 0;
        var suffix = UnitSuffix(token);
        double factor = suffix switch
        {
            "deg" => 1,
            "grad" => 0.9,
            "turn" => 360,
            "rad" => 180 / System.Math.PI,
            _ => 0,
        };
        if (factor == 0) return false;
        if (!TryNumber(token[..^suffix.Length], out var value)) return false;
        degrees = value * factor;
        return true;
    }

    private static bool IsZeroPixels(string text)
        => text.EndsWith("px", StringComparison.Ordinal)
        && TryNumber(text[..^2], out var value) && value == 0;

    private static bool IsNegativePixels(string text)
        => text.EndsWith("px", StringComparison.Ordinal)
        && TryNumber(text[..^2], out var value) && value < 0;
}
