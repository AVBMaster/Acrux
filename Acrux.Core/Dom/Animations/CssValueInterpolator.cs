using System.Globalization;
using System.Text;
using Acrux.Core.Css;

namespace Acrux.Core.Dom.Animations;

/// <summary>
/// Interpolates two CSS values for a property, following Web Animations §"interpolation".
///
/// Interpolation is a function of the property's *type* (CSS Values 4 §5), so each
/// supported property is routed to a handler that knows how to blend its two
/// endpoints. Properties whose value type cannot be blended fall back to the
/// discrete 50% step, which is exactly what the spec prescribes for
/// non-interpolable-but-animatable types.
/// </summary>
public static class CssValueInterpolator
{
    /// <summary>
    /// Blend <paramref name="from"/> and <paramref name="to"/> at
    /// <paramref name="progress"/> (already the specific progress, 0..1).
    /// Returns null when the property has no interpolable form, which the caller
    /// treats as "flip at 50%".
    /// </summary>
    public static string? Interpolate(string property, string from, string to, double progress)
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return null;
        if (progress <= 0) return from;
        if (progress >= 1) return to;

        property = property.ToLowerInvariant();

        // `visibility` is animatable but interpolates with a special discrete
        // rule (css-animations-1 §"Animating keyframe values"): if either endpoint
        // is `visible` the element stays visible for the whole run and only takes
        // the hidden value once the animation reaches its end. Otherwise it is an
        // ordinary 50% step.
        if (property == "visibility")
        {
            bool fromVisible = string.Equals(from.Trim(), "visible", StringComparison.OrdinalIgnoreCase);
            bool toVisible = string.Equals(to.Trim(), "visible", StringComparison.OrdinalIgnoreCase);
            return fromVisible || toVisible ? "visible" : (progress < 0.5 ? from : to);
        }

        // Composite values first: they own their own comma handling.
        if (TransformLikeProperties.Contains(property))
            return InterpolateTransform(property, from, to, progress);

        if (property is "box-shadow" or "text-shadow")
            return InterpolateShadow(property, from, to, progress);

        if (property is "filter" or "backdrop-filter")
            return InterpolateFilterList(property, from, to, progress);

        if (property is "background-position" or "background-size")
            return InterpolateComponentPair(property, from, to, progress);

        if (ColorProperties.Contains(property))
            return InterpolateColor(from, to, progress);

        if (AngleProperties.Contains(property))
            return InterpolateAngle(from, to, progress);

        if (NumberProperties.Contains(property))
            return InterpolateNumber(property, from, to, progress);

        if (LengthProperties.Contains(property) || property.EndsWith("-radius", StringComparison.Ordinal) ||
            property.EndsWith("-width", StringComparison.Ordinal) || property.EndsWith("-height", StringComparison.Ordinal) ||
            property is "top" or "right" or "bottom" or "left" or "inset")
        {
            return InterpolateLength(property, from, to, progress);
        }

        return null;
    }

    // ---------------------------------------------------------------- transforms

    private static readonly HashSet<string> TransformLikeProperties = new(StringComparer.Ordinal)
    {
        "transform", "translate", "rotate", "scale", "perspective", "transform-origin",
    };

    private static readonly HashSet<string> ColorProperties = new(StringComparer.Ordinal)
    {
        "color", "background-color", "border-color", "border-top-color", "border-right-color",
        "border-bottom-color", "border-left-color", "outline-color", "text-decoration-color",
        "caret-color", "accent-color", "column-rule-color", "fill", "stroke", "text-emphasis-color",
    };

    private static readonly HashSet<string> AngleProperties = new(StringComparer.Ordinal)
    {
        "rotate", "perspective", "hue-rotate",
    };

    private static readonly HashSet<string> NumberProperties = new(StringComparer.Ordinal)
    {
        "opacity", "line-height", "font-weight", "font-size", "letter-spacing", "word-spacing",
        "text-indent", "flex-grow", "flex-shrink", "aspect-ratio", "order", "z-index",
        "tab-size", "column-count", "orphans", "widows", "stroke-width", "fill-opacity",
        "stroke-opacity", "flood-opacity", "stop-opacity", "stroke-miterlimit",
    };

    private static readonly HashSet<string> LengthProperties = new(StringComparer.Ordinal)
    {
        "width", "height", "min-width", "max-width", "min-height", "max-height",
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "top", "right", "bottom", "left", "flex-basis",
        "row-gap", "column-gap", "gap", "outline-offset", "text-underline-offset",
        "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius",
        "border-bottom-left-radius", "border-spacing",
    };

    /// <summary>
    /// Interpolate a transform-function list.
    ///
    /// When both lists have the same functions in the same order the arguments
    /// are blended pairwise, which keeps a pure <c>rotate()</c> animation in
    /// <c>rotate()</c> form and exact. Otherwise both sides are flattened to a
    /// 2x3 matrix and the matrices are blended — the same fallback the engine
    /// spec allows, and exact for the 2D transforms this renderer supports.
    /// </summary>
    private static string? InterpolateTransform(string property, string from, string to, double progress)
    {
        // transform-origin is a position, not a function list.
        if (property == "transform-origin")
            return InterpolateComponentTriple(property, from, to, progress);

        if (property == "scale")
        {
            // scale/translate/rotate accept a single shorthand number.
            var a = TransformParser.Parse(from);
            var b = TransformParser.Parse(to);
            if (a.Count == 1 && b.Count == 1 &&
                a[0].Function == b[0].Function && InterpolationUnit(a[0]) == InterpolationUnit(b[0]))
            {
                return BlendFunction(a[0], b[0], progress);
            }
            if (a.Count == 0 || b.Count == 0)
            {
                var other = a.Count == 0 ? b : a;
                if (other.Count == 1)
                {
                    return a.Count == 0
                        ? BlendFunction(IdentityOf(other[0]), other[0], progress)
                        : BlendFunction(other[0], IdentityOf(other[0]), progress);
                }
            }
            return InterpolateMatrices(from, to, progress);
        }

        var fromOps = TransformParser.Parse(from);
        var toOps = TransformParser.Parse(to);

        if (fromOps.Count == 0 && toOps.Count == 0) return "none";
        if (fromOps.Count == 0 || toOps.Count == 0)
        {
            // 'none' is the identity. A single function can be paired against it
            // directly, anything longer needs the matrix path.
            var present = fromOps.Count == 0 ? toOps : fromOps;
            if (present.Count == 1)
            {
                var real = present[0];
                var identity = IdentityOf(real);
                return fromOps.Count == 0
                    ? BlendFunction(identity, real, progress)
                    : BlendFunction(real, identity, progress);
            }
            return InterpolateMatrices(from, to, progress);
        }

        if (CanPairwise(fromOps, toOps))
        {
            var parts = new List<string>(fromOps.Count);
            for (int i = 0; i < fromOps.Count; i++)
                parts.Add(BlendFunction(fromOps[i], toOps[i], progress));
            return string.Join(" ", parts);
        }

        return InterpolateMatrices(from, to, progress);
    }

    /// <summary>
    /// Two lists pair pairwise when they have the same length, the same function
    /// names in the same order, and the same argument units.
    /// </summary>
    private static bool CanPairwise(List<TransformOperation> a, List<TransformOperation> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Function != b[i].Function) return false;
            if (InterpolationUnit(a[i]) != InterpolationUnit(b[i])) return false;
        }
        return true;
    }

    /// <summary>A coarse unit signature so <c>translate(10px)</c> never pairs with <c>translate(5%)</c>.</summary>
    private static string InterpolationUnit(TransformOperation op)
    {
        if (op.Args.Length == 0) return "";
        var parts = new List<string>();
        foreach (var arg in op.Args)
        {
            if (CssValueTokenizer.TrySplitUnit(arg, out _, out var unit)) parts.Add(unit.ToLowerInvariant());
            else parts.Add("num");
        }
        return string.Join("/", parts);
    }

    /// <summary>
    /// The identity counterpart of <paramref name="op"/>, built by replacing its
    /// arguments with the neutral value for that function.
    ///
    /// Deriving it from the real operation rather than from a table keeps the
    /// argument count and units identical, which is what lets the pair blend
    /// instead of falling back to a matrix.
    /// </summary>
    private static TransformOperation IdentityOf(TransformOperation op)
    {
        string function = op.Function;
        bool isScale = function.StartsWith("scale", StringComparison.Ordinal);
        bool isAngle = function.StartsWith("rotate", StringComparison.Ordinal) ||
                       function.StartsWith("skew", StringComparison.Ordinal);

        var args = new string[op.Args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            if (isScale) { args[i] = "1"; continue; }
            if (isAngle) { args[i] = "0deg"; continue; }
            if (function.StartsWith("matrix", StringComparison.Ordinal))
            {
                // matrix(a,b,c,d,e,f) identity: scale 1, no skew/rotation, no translation.
                args[i] = i is 0 or 3 ? "1" : "0";
                continue;
            }
            args[i] = "0px";
        }
        return new TransformOperation { Function = function, Args = args };
    }

    private static string BlendFunction(TransformOperation from, TransformOperation to, double progress)
    {
        if (from.Function != to.Function || from.Args.Length != to.Args.Length)
            return progress < 0.5 ? Serialize(from) : Serialize(to);

        bool angular = from.Function.StartsWith("rotate", StringComparison.Ordinal) ||
                        from.Function.StartsWith("skew", StringComparison.Ordinal);

        var args = new string[from.Args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            double a = NumericValue(from.Args[i], angular);
            double b = NumericValue(to.Args[i], angular);
            string unit = OutputUnit(from.Args[i], i, from.Function);
            args[i] = CssValueTokenizer.Num(a + (b - a) * progress) + unit;
        }
        return $"{from.Function}({string.Join(",", args)})";
    }

    private static string Serialize(TransformOperation op) =>
        op.Args.Length == 0 ? op.Function : $"{op.Function}({string.Join(",", op.Args)})";

    /// <summary>Numeric part of an argument, normalizing angles to degrees.</summary>
    private static double NumericValue(string token, bool angular)
    {
        if (!CssValueTokenizer.TrySplitUnit(token, out double value, out string unit)) return 0;
        unit = unit.ToLowerInvariant();
        if (unit == "rad") return value * 180.0 / Math.PI;
        if (unit == "grad") return value * 0.9;
        if (unit == "turn") return value * 360.0;
        return value;
    }

    /// <summary>Unit of the output argument, defaulting sensibly for bare numbers.</summary>
    private static string OutputUnit(string token, int index, string function)
    {
        if (CssValueTokenizer.TrySplitUnit(token, out _, out string unit) && unit.Length > 0)
        {
            return unit.ToLowerInvariant() switch
            {
                "rad" => "deg",
                "grad" => "deg",
                "turn" => "deg",
                var u => u,
            };
        }
        // Bare numbers: rotation angles default to degrees, scales to 1.
        if (function.StartsWith("rotate", StringComparison.Ordinal) ||
            function.StartsWith("skew", StringComparison.Ordinal)) return "deg";
        if (function.StartsWith("scale", StringComparison.Ordinal)) return "";
        if (function.StartsWith("translate", StringComparison.Ordinal)) return "px";
        return "";
    }

    /// <summary>
    /// Matrix fallback: flatten both lists to 2x3 matrices about the origin and
    /// blend the six entries. Exact for the affine 2D transforms supported here.
    /// </summary>
    private static string? InterpolateMatrices(string from, string to, double progress)
    {
        var a = TransformParser.ToMatrix(TransformParser.Parse(from), 0, 0);
        var b = TransformParser.ToMatrix(TransformParser.Parse(to), 0, 0);

        double s = a.ScaleX + (b.ScaleX - a.ScaleX) * progress;
        double kx = a.SkewX + (b.SkewX - a.SkewX) * progress;
        double ky = a.SkewY + (b.SkewY - a.SkewY) * progress;
        double sy = a.ScaleY + (b.ScaleY - a.ScaleY) * progress;
        double tx = a.TransX + (b.TransX - a.TransX) * progress;
        double ty = a.TransY + (b.TransY - a.TransY) * progress;

        return $"matrix({CssValueTokenizer.Num(s)},{CssValueTokenizer.Num(ky)}," +
               $"{CssValueTokenizer.Num(kx)},{CssValueTokenizer.Num(sy)}," +
               $"{CssValueTokenizer.Num(tx)},{CssValueTokenizer.Num(ty)})";
    }

    // ------------------------------------------------------------------- shadows

    private static string? InterpolateShadow(string property, string from, string to, double progress)
    {
        var a = ParseShadowList(from, property == "text-shadow");
        var b = ParseShadowList(to, property == "text-shadow");
        if (a == null || b == null) return null;
        if (a.Count == 1 && a[0].Lengths.Count == 0) return progress < 0.5 ? from : to;   // `none`
        if (b.Count == 1 && b[0].Lengths.Count == 0) return progress < 0.5 ? from : to;
        if (a.Count != b.Count) return null;   // lists of different length: discrete flip

        var items = new List<string>(a.Count);
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            // Both sides must have the same shape: same spread, same inset flag.
            if (x.Inset != y.Inset || x.Lengths.Count != y.Lengths.Count) return null;

            var sb = new StringBuilder();
            if (x.Inset) sb.Append("inset ");
            for (int k = 0; k < x.Lengths.Count; k++)
            {
                if (k > 0) sb.Append(' ');
                sb.Append(CssValueTokenizer.Num(x.Lengths[k] + (y.Lengths[k] - x.Lengths[k]) * progress));
                sb.Append("px");
            }
            if (x.Color != null && y.Color != null)
            {
                sb.Append(' ').Append(ColorToCss(BlendColor(
                    ColorParser.Parse(x.Color), ColorParser.Parse(y.Color), progress)));
            }
            else if (x.Color != null || y.Color != null)
            {
                sb.Append(' ').Append(progress < 0.5 ? (x.Color ?? y.Color!) : (y.Color ?? x.Color!));
            }
            items.Add(sb.ToString());
        }
        return string.Join(", ", items);
    }

    private readonly struct ShadowPart
    {
        public bool Inset { get; }
        public string? Color { get; }
        public List<double> Lengths { get; }

        public ShadowPart(bool inset, string? color, List<double> lengths)
        {
            Inset = inset;
            Color = color;
            Lengths = lengths;
        }
    }

    /// <summary>
    /// Split a shadow list into its parts. A shadow is
    /// <c>inset? color? &lt;length&gt;{2,4}</c> — two lengths for the offsets, plus
    /// an optional blur and an optional spread — so the arity is discovered
    /// rather than assumed. Returns null for anything that does not parse.
    /// </summary>
    private static List<ShadowPart>? ParseShadowList(string value, bool isTextShadow)
    {
        var result = new List<ShadowPart>();
        foreach (var tokens in CssValueTokenizer.SplitItemsAndTokens(value))
        {
            bool inset = false;
            string? color = null;
            var lengths = new List<double>();
            foreach (var token in tokens)
            {
                var t = token.Trim();
                if (t.Equals("inset", StringComparison.OrdinalIgnoreCase)) { inset = true; continue; }
                if (LooksLikeColor(t)) { color ??= t; continue; }
                if (CssValueTokenizer.TrySplitUnit(t, out double v, out string u) && u.Length > 0)
                {
                    lengths.Add(NormalizeLengthToken(v, u));
                    continue;
                }
                if (CssValueTokenizer.TryParseNumber(t, out double n)) { lengths.Add(n); continue; }
                return null;   // unknown token: not a shadow
            }
            // Two offsets, plus an optional blur and an optional spread.
            if (lengths.Count < 2) return null;
            if (isTextShadow && lengths.Count > 3) return null;
            if (lengths.Count > 4) return null;
            result.Add(new ShadowPart(inset, color, lengths));
        }
        return result.Count == 0 ? null : result;
    }

    private static double NormalizeLengthToken(double value, string unit) => unit.ToLowerInvariant() switch
    {
        "rad" => value * 180.0 / Math.PI,
        "turn" => value * 360.0,
        _ => value,
    };

    // ------------------------------------------------------------------- filters

    private static string? InterpolateFilterList(string property, string from, string to, double progress)
    {
        var a = CssValueTokenizer.SplitFunctions(from);
        var b = CssValueTokenizer.SplitFunctions(to);

        // `none` is the identity: pair it against a single function's identity
        // rather than giving up and flipping at 50%.
        if (IsNoneFilter(a) || IsNoneFilter(b))
        {
            var present = IsNoneFilter(a) ? b : a;
            if (present.Count != 1) return progress < 0.5 ? from : to;
            var real = present[0];
            var identity = FilterIdentityOf(real);
            return IsNoneFilter(a)
                ? BlendFilterFunction(identity, real, progress)
                : BlendFilterFunction(real, identity, progress);
        }

        if (a.Count != b.Count) return null;

        var parts = new List<string>(a.Count);
        for (int i = 0; i < a.Count; i++)
            parts.Add(BlendFilterFunction(a[i], b[i], progress));
        return string.Join(" ", parts);
    }

    private static bool IsNoneFilter(List<(string Name, List<string> Args)> parts) =>
        parts.Count == 0 || (parts.Count == 1 && parts[0].Name == "none");

    private static string SerializeFilter((string Name, List<string> Args) op) =>
        op.Args.Count == 0 ? op.Name : $"{op.Name}({string.Join(",", op.Args)})";

    /// <summary>The identity of a filter function, derived from its own arguments.</summary>
    private static (string, List<string>) FilterIdentityOf((string Name, List<string> Args) op)
    {
        var args = new List<string>(op.Args.Count);
        foreach (var arg in op.Args)
        {
            if (LooksLikeColor(arg)) { args.Add("transparent"); continue; }
            // A <length> argument (blur, drop-shadow) keeps its unit; everything
            // else is a unitless factor.
            args.Add(CssValueTokenizer.TrySplitUnit(arg, out _, out string u) && u.Length > 0 ? "0" + u : "0");
        }
        return (op.Name, args);
    }

    private static string BlendFilterFunction(
        (string Name, List<string> Args) from,
        (string Name, List<string> Args) to,
        double progress)
    {
        if (from.Name != to.Name) return progress < 0.5 ? SerializeFilter(from) : SerializeFilter(to);
        if (from.Args.Count != to.Args.Count) return progress < 0.5 ? SerializeFilter(from) : SerializeFilter(to);
        if (from.Args.Count == 0) return SerializeFilter(from);

        var args = new List<string>(from.Args.Count);
        for (int i = 0; i < from.Args.Count; i++)
        {
            if (LooksLikeColor(from.Args[i]) && LooksLikeColor(to.Args[i]))
            {
                args.Add(ColorToCss(BlendColor(
                    ColorParser.Parse(from.Args[i]), ColorParser.Parse(to.Args[i]), progress)));
                continue;
            }
            // Filter arguments are <length>, <angle>, <number> or <percentage>. The
            // unit is carried over verbatim from the "from" side, so a unitless
            // brightness() factor stays unitless and a blur() keeps its px.
            if (!CssValueTokenizer.TrySplitUnit(from.Args[i], out double va, out string ua) ||
                !CssValueTokenizer.TrySplitUnit(to.Args[i], out double vb, out string ub))
            {
                return progress < 0.5 ? SerializeFilter(from) : SerializeFilter(to);
            }
            ua = ua.ToLowerInvariant();
            ub = ub.ToLowerInvariant();
            if (ua != ub) return progress < 0.5 ? SerializeFilter(from) : SerializeFilter(to);

            double na = NormalizeLengthToken(va, ua);
            double nb = NormalizeLengthToken(vb, ub);
            args.Add(CssValueTokenizer.Num(na + (nb - na) * progress) + ua);
        }
        return $"{from.Name}({string.Join(",", args)})";
    }

    // ------------------------------------------------------------ component pairs

    private static string? InterpolateComponentPair(string property, string from, string to, double progress)
    {
        var a = CssValueTokenizer.SplitTopLevel(from);
        var b = CssValueTokenizer.SplitTopLevel(to);
        if (a.Count == 1) a.Add(a[0]);
        if (b.Count == 1) b.Add(b[0]);
        if (a.Count != 2 || b.Count != 2) return null;

        string x = BlendComponent(property, a[0], b[0], progress);
        string y = BlendComponent(property, a[1], b[1], progress);
        return $"{x} {y}";
    }

    private static string? InterpolateComponentTriple(string property, string from, string to, double progress)
    {
        var a = CssValueTokenizer.SplitTokens(from);
        var b = CssValueTokenizer.SplitTokens(to);
        // x y [z]; a single token means "x y" with y = center (50% for origin).
        if (a.Count == 1) { a.Add("50%"); a.Add("0px"); }
        if (b.Count == 1) { b.Add("50%"); b.Add("0px"); }
        if (a.Count == 2) a.Add("0px");
        if (b.Count == 2) b.Add("0px");
        if (a.Count != 3 || b.Count != 3) return null;

        var parts = new List<string>(3);
        for (int i = 0; i < 3; i++)
        {
            var blended = BlendComponent(property, a[i], b[i], progress);
            if (blended == null) return null;
            parts.Add(blended);
        }
        return string.Join(" ", parts);
    }

    private static string BlendComponent(string property, string from, string to, double progress)
    {
        bool fromKeyword = IsComponentKeyword(from);
        bool toKeyword = IsComponentKeyword(to);
        if (fromKeyword || toKeyword)
        {
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return from;
            return progress < 0.5 ? from : to;
        }
        if (LooksLikeColor(from) && LooksLikeColor(to))
            return ColorToCss(BlendColor(ColorParser.Parse(from), ColorParser.Parse(to), progress));

        double a = NumericValue(from, false);
        double b = NumericValue(to, false);
        string unit = from.EndsWith('%') ? "%" : (CssValueTokenizer.TrySplitUnit(from, out _, out var u) && u.Length > 0 ? u : "px");
        return CssValueTokenizer.Num(a + (b - a) * progress) + unit;
    }

    private static bool IsComponentKeyword(string token) => token.ToLowerInvariant() switch
    {
        "left" or "right" or "top" or "bottom" or "center" => true,
        _ => false,
    };

    // -------------------------------------------------------------------- colors

    private static string InterpolateColor(string from, string to, double progress)
    {
        return ColorToCss(BlendColor(ColorParser.Parse(from), ColorParser.Parse(to), progress));
    }

    /// <summary>
    /// Blend two colors in non-premultiplied sRGB. CSS Color 4 §12: the
    /// interpolation happens on the unpremultiplied components, so fading
    /// <c>red</c> to <c>transparent blue</c> passes through purple, not grey.
    /// </summary>
    private static SkiaSharp.SKColor BlendColor(SkiaSharp.SKColor a, SkiaSharp.SKColor b, double progress)
    {
        // Premultiply on the 0..255 alpha scale, blend the channels, then
        // un-premultiply: interpolating in premultiplied space would darken
        // every transition that fades alpha.
        double af = a.Alpha / 255.0, bf = b.Alpha / 255.0;
        double ap = af * a.Red, bp = bf * b.Red;
        double aq = af * a.Green, bq = bf * b.Green;
        double ar = af * a.Blue, br = bf * b.Blue;

        double alpha = af + (bf - af) * progress;
        if (alpha <= 0) return new SkiaSharp.SKColor(0, 0, 0, 0);

        double red = (ap + (bp - ap) * progress) / alpha;
        double green = (aq + (bq - aq) * progress) / alpha;
        double blue = (ar + (br - ar) * progress) / alpha;

        return new SkiaSharp.SKColor(
            ClampByte(red), ClampByte(green), ClampByte(blue), ClampByte(alpha * 255.0));
    }

    private static byte ClampByte(double v)
    {
        int i = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        if (i < 0) return 0;
        if (i > 255) return 255;
        return (byte)i;
    }

    private static string ColorToCss(SkiaSharp.SKColor c)
    {
        if (c.Alpha == 255)
            return $"rgb({c.Red}, {c.Green}, {c.Blue})";
        return $"rgba({c.Red}, {c.Green}, {c.Blue}, {CssValueTokenizer.Num(c.Alpha / 255.0)})";
    }

    // ------------------------------------------------------- numbers, angles, lengths

    private static string InterpolateAngle(string from, string to, double progress)
    {
        double a = NumericValue(from, true);
        double b = NumericValue(to, true);
        return CssValueTokenizer.Num(a + (b - a) * progress) + "deg";
    }

    private static string InterpolateNumber(string property, string from, string to, double progress)
    {
        // Keywords (normal, bold, auto, none) are discrete.
        if (IsKeyword(from) || IsKeyword(to))
            return progress < 0.5 ? from : to;

        double a = NumericValue(from, false);
        double b = NumericValue(to, false);
        double v = a + (b - a) * progress;

        // line-height and font-size stay unitless when both sides are unitless.
        string unit = CssValueTokenizer.TrySplitUnit(from, out _, out string fu) ? fu.ToLowerInvariant() : "";
        if (unit.Length == 0 && CssValueTokenizer.TrySplitUnit(to, out _, out string tu))
            unit = tu.ToLowerInvariant();
        if (unit.Length == 0 && property is "line-height" or "aspect-ratio")
            return CssValueTokenizer.Num(v);
        return CssValueTokenizer.Num(v) + (unit.Length == 0 ? "" : unit);
    }

    private static string? InterpolateLength(string property, string from, string to, double progress)
    {
        // A length cannot be interpolated with a percentage (different reference
        // boxes), nor with an intrinsic keyword; those flip at 50%.
        if (from.EndsWith('%') != to.EndsWith('%')) return null;
        if (IsKeyword(from) || IsKeyword(to)) return null;

        if (!CssValueTokenizer.TrySplitUnit(from, out double a, out string ua) ||
            !CssValueTokenizer.TrySplitUnit(to, out double b, out string ub))
        {
            return null;
        }

        ua = ua.ToLowerInvariant();
        ub = ub.ToLowerInvariant();

        // Different relative units need a resolution context this layer does not
        // have, so only identical unit families blend.
        if (ua != ub && !IsAbsoluteLengthUnit(ua) || ua != ub && !IsAbsoluteLengthUnit(ub))
            return null;
        if (ua != ub) ua = ub = "px";

        // A bare 0 is unitless in CSS but equivalent to 0px.
        if (a == 0 && ua.Length == 0) ua = "px";
        if (b == 0 && ub.Length == 0) ub = "px";

        double v = a + (b - a) * progress;
        string unit = ua.Length == 0 && ub.Length == 0 ? "" : "px";
        return CssValueTokenizer.Num(v) + unit;
    }

    private static bool IsAbsoluteLengthUnit(string unit) => unit is "px" or "" or "in" or "cm" or "mm" or "q" or "pt" or "pc";

    private static bool IsKeyword(string token)
    {
        if (token.Length == 0) return true;
        if (CssValueTokenizer.TryParseNumber(token, out _)) return false;
        if (token.EndsWith('%')) return false;
        if (token[0] == '#' || token[0] == '$') return false;
        if (token[0] == '-' && token.Length > 1 && (char.IsDigit(token[1]) || token[1] == '.')) return false;
        if (char.IsDigit(token[0]) || token[0] == '.') return false;
        if (token[0] == '(' && CssValueTokenizer.TryReadFunction(token.ToLowerInvariant(), out var fn, out _) &&
            IsFunctionalComponent(fn))
        {
            return false;
        }
        return true;
    }

    private static bool IsFunctionalComponent(string name) => name is
        "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or
        "color" or "color-mix" or "calc" or "min" or "max" or "clamp" or "var" or "url" or
        "linear-gradient" or "radial-gradient" or "conic-gradient" or "repeating-linear-gradient" or
        "repeating-radial-gradient" or "attr" or "env";

    private static bool LooksLikeColor(string token)
    {
        if (token.Length == 0) return false;
        if (token[0] == '#') return true;

        var lower = token.ToLowerInvariant();
        if (lower == "transparent" || lower == "currentcolor") return true;
        if (CssValueTokenizer.TryReadFunction(lower, out var name, out _))
            return IsColorFunction(name) || IsFunctionalComponent(name);
        return NamedColorNames.Contains(lower);
    }

    private static bool IsColorFunction(string name) => name is
        "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "color";

    /// <summary>
    /// The CSS named colors (CSS Color 4 §6.1). A list rather than a parser
    /// round-trip: this is a hot path (one call per shadow / filter component
    /// per frame) and the set is closed.
    /// </summary>
    private static readonly HashSet<string> NamedColorNames = new(StringComparer.Ordinal)
    {
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque",
        "black", "blanchedalmond", "blue", "blueviolet", "brown", "burlywood",
        "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue", "cornsilk",
        "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod", "darkgray",
        "darkgreen", "darkgrey", "darkkhaki", "darkmagenta", "darkolivegreen",
        "darkorange", "darkorchid", "darkred", "darksalmon", "darkseagreen",
        "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise",
        "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey", "dodgerblue",
        "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite",
        "gold", "goldenrod", "gray", "green", "greenyellow", "grey", "honeydew",
        "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender",
        "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral",
        "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey",
        "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray",
        "lightslategrey", "lightsteelblue", "lightyellow", "lime", "limegreen", "linen",
        "magenta", "maroon", "mediumaquamarine", "mediumblue", "mediumorchid",
        "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen",
        "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream",
        "mistyrose", "moccasin", "navajowhite", "navy", "oldlace", "olive",
        "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen",
        "paleturquoise", "palevioletred", "papayawhip", "peachpuff", "peru", "pink",
        "plum", "powderblue", "purple", "rebeccapurple", "red", "rosybrown", "royalblue",
        "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna",
        "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen",
        "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat",
        "white", "whitesmoke", "yellow", "yellowgreen",
    };
}
