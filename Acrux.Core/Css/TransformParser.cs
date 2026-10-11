using System.Text.RegularExpressions;
using SkiaSharp;

namespace Acrux.Core.Css;

public static class TransformParser
{
    private static readonly Regex TransformRegex = new(
        @"(translateX|translateY|translateZ|translate|translate3d|rotateX|rotateY|rotateZ|rotate3d|rotate|scaleX|scaleY|scaleZ|scale|scale3d|skewX|skewY|skew|matrix|matrix3d|perspective)\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase);

    /// <summary>A 4x4 matrix in the column-major order CSS prints it in: element
    /// <c>[column * 4 + row]</c>, so the translation lives at 12, 13 and 14 and a
    /// <c>matrix3d()</c> argument list is this array read straight through
    /// (CSS Transforms 2 §3, measured).</summary>
    public static float[] Identity4() => new float[]
    {
        1, 0, 0, 0,   0, 1, 0, 0,   0, 0, 1, 0,   0, 0, 0, 1,
    };

    /// <summary>The product of a transform list as a 4x4. CSS multiplies the functions in list
    /// order — 'transform: A B' is A·B, with B acting on the point first — so each operation is
    /// post-multiplied into the running product, exactly as <see cref="ToMatrix"/> does for the
    /// 3x3 the painter uses. The 3x3 cannot hold depth at all: 'rotate(45deg) translateZ(1px)'
    /// through it loses the z offset rather than saying it is a 4x4.</summary>
    public static float[] ToMatrix4(List<TransformOperation> operations)
    {
        var product = Identity4();
        foreach (var op in operations)
            product = Multiply4(product, OperationToMatrix4(op));
        return product;
    }

    /// <summary>True when a 4x4 says nothing a 3x3 could not: the third row and column are the
    /// ones CSS writes for a flat transform. The reference engine prints <c>matrix()</c> exactly
    /// then, even when the page spelled a 3D function — 'translate3d(1px, 2px, 0px)' and
    /// 'rotateZ(45deg)' read back as <c>matrix()</c> while 'scaleZ(2)' does not (all measured).</summary>
    public static bool IsFlat4(float[] m)
    {
        const float epsilon = 1e-6f;
        return MathF.Abs(m[2]) < epsilon && MathF.Abs(m[6]) < epsilon
            && MathF.Abs(m[8]) < epsilon && MathF.Abs(m[9]) < epsilon
            && MathF.Abs(m[10] - 1f) < epsilon && MathF.Abs(m[11]) < epsilon
            && MathF.Abs(m[14]) < epsilon && MathF.Abs(m[15] - 1f) < epsilon;
    }

    private static float[] Multiply4(float[] a, float[] b)
    {
        var result = new float[16];
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++)
            {
                float sum = 0;
                for (int k = 0; k < 4; k++)
                    sum += a[k * 4 + row] * b[column * 4 + k];
                result[column * 4 + row] = sum;
            }
        return result;
    }

    private static float[] OperationToMatrix4(TransformOperation op)
    {
        float[] args = op.Args.Select(ParseFloat).ToArray();
        float At(int index, float otherwise = 0f) => args.Length > index ? args[index] : otherwise;
        var matrix = Identity4();
        switch (op.Function)
        {
            case "translate":
                matrix[12] = At(0); matrix[13] = At(1);
                break;
            case "translate3d":
                matrix[12] = At(0); matrix[13] = At(1); matrix[14] = At(2);
                break;
            case "translatex": matrix[12] = At(0); break;
            case "translatey": matrix[13] = At(0); break;
            case "translatez": matrix[14] = At(0); break;
            case "rotate":
            case "rotatez":
            {
                float radians = At(0) * MathF.PI / 180f;
                float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
                matrix[0] = cos; matrix[1] = sin; matrix[4] = -sin; matrix[5] = cos;
                break;
            }
            case "rotatex":
            {
                float radians = At(0) * MathF.PI / 180f;
                float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
                matrix[5] = cos; matrix[6] = sin; matrix[9] = -sin; matrix[10] = cos;
                break;
            }
            case "rotatey":
            {
                float radians = At(0) * MathF.PI / 180f;
                float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
                matrix[0] = cos; matrix[2] = -sin; matrix[8] = sin; matrix[10] = cos;
                break;
            }
            case "rotate3d":
            {
                // The axis is normalized first, and a zero-length axis is the identity — the
                // reference engine prints 'matrix(1, 0, 0, 1, 0, 0)' for 'rotate3d(0,0,0,90deg)'.
                float x = At(0), y = At(1), z = At(2);
                float length = MathF.Sqrt(x * x + y * y + z * z);
                if (length < 1e-7f) break;
                x /= length; y /= length; z /= length;
                float angle = At(3) * MathF.PI / 180f;
                float c = MathF.Cos(angle), s = MathF.Sin(angle), t = 1f - c;
                matrix[0] = t * x * x + c;   matrix[1] = t * x * y + z * s; matrix[2] = t * x * z - y * s;
                matrix[4] = t * x * y - z * s; matrix[5] = t * y * y + c;   matrix[6] = t * y * z + x * s;
                matrix[8] = t * x * z + y * s; matrix[9] = t * y * z - x * s; matrix[10] = t * z * z + c;
                break;
            }
            case "scale":
                matrix[0] = At(0, 1f); matrix[5] = At(1, matrix[0]);
                break;
            case "scale3d":
                matrix[0] = At(0, 1f); matrix[5] = At(1, 1f); matrix[10] = At(2, 1f);
                break;
            case "scalex": matrix[0] = At(0, 1f); break;
            case "scaley": matrix[5] = At(0, 1f); break;
            case "scalez": matrix[10] = At(0, 1f); break;
            case "skew":
                // The first argument leans the vertical axis (m21) and the second the horizontal
                // one (m12) — 'skew(10deg, 20deg)' is matrix(1, tan 20°, tan 10°, 1, 0, 0)
                // (measured), the same reading skewX() and skewY() already have on their own.
                matrix[1] = MathF.Tan(At(1) * MathF.PI / 180f);
                matrix[4] = MathF.Tan(At(0) * MathF.PI / 180f);
                break;
            case "skewx": matrix[4] = MathF.Tan(At(0) * MathF.PI / 180f); break;
            case "skewy": matrix[1] = MathF.Tan(At(0) * MathF.PI / 180f); break;
            case "matrix":
                if (args.Length >= 6)
                {
                    matrix[0] = args[0]; matrix[1] = args[1];
                    matrix[4] = args[2]; matrix[5] = args[3];
                    matrix[12] = args[4]; matrix[13] = args[5];
                }
                break;
            case "matrix3d":
                if (args.Length >= 16) Array.Copy(args, matrix, 16);
                break;
            case "perspective":
                if (At(0) != 0f) matrix[11] = -1f / At(0);
                break;
        }
        return matrix;
    }

    public static List<TransformOperation> Parse(string? transformString)
    {
        var result = new List<TransformOperation>();
        if (string.IsNullOrWhiteSpace(transformString) || transformString == "none")
            return result;

        foreach (Match match in TransformRegex.Matches(transformString))
        {
            var op = new TransformOperation
            {
                Function = match.Groups[1].Value.ToLowerInvariant(),
                Args = match.Groups[2].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .ToArray()
            };
            result.Add(op);
        }
        return result;
    }

    private static readonly Regex TranslateRegex = new(
        @"(translate3d|translateX|translateY|translate)\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Rewrite percentage arguments of translate()/translateX()/translateY()/translate3d()
    /// into absolute pixels against the element's own border box (CSS Transforms §4:
    /// X% is a fraction of the border-box width, Y% of the height). ParseFloat cannot do
    /// this on its own because it has no geometry, so callers that only have the transform
    /// string would otherwise silently drop translate percentages. translate3d's Z% has no
    /// 2D reference, so it flattens to 0 like the Z length does.
    /// </summary>
    public static string ResolveTranslatePercentages(string? transform, float borderWidth, float borderHeight)
    {
        if (string.IsNullOrEmpty(transform) || !transform.Contains('%'))
            return transform ?? string.Empty;

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return TranslateRegex.Replace(transform, m =>
        {
            string fn = m.Groups[1].Value.ToLowerInvariant();
            var args = m.Groups[2].Value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].EndsWith("%", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!float.TryParse(args[i][..^1], System.Globalization.NumberStyles.Float, ci, out float pct))
                    continue;
                // translateX's lone argument is horizontal; translateY's is vertical.
                float reference = fn switch
                {
                    "translatex" => borderWidth,
                    "translatey" => borderHeight,
                    _ => i == 0 ? borderWidth : borderHeight,
                };
                if (fn == "translate3d" && i == 2) reference = 0f; // Z% has no 2D reference
                args[i] = (pct / 100f * reference).ToString(ci) + "px";
            }
            return $"{m.Groups[1].Value}({string.Join(",", args)})";
        });
    }

    public static SKMatrix ToMatrix(List<TransformOperation> operations, float originX, float originY)
    {
        // CSS multiplies the functions in list order: 'transform: A B' is the matrix A·B,
        // so B acts on the point first. Skia uses column vectors and Concat(a, b) = a·b,
        // which is the same convention, so the product is built by post-multiplying.
        var product = SKMatrix.Identity;
        foreach (var op in operations)
            product = SKMatrix.Concat(product, OperationToMatrix(op));

        // The whole list turns about the transform-origin exactly once. Wrapping every
        // function separately also drags the earlier functions' translations through the
        // later scales, which multiplies the origin offsets and throws the element off
        // the page.
        var toOrigin = SKMatrix.CreateTranslation(-originX, -originY);
        var fromOrigin = SKMatrix.CreateTranslation(originX, originY);
        return SKMatrix.Concat(SKMatrix.Concat(fromOrigin, product), toOrigin);
    }

    private static SKMatrix OperationToMatrix(TransformOperation op)
    {
        float[] args = op.Args.Select(ParseFloat).ToArray();

        return op.Function switch
        {
            "translate" or "translate3d" => SKMatrix.CreateTranslation(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)),
            "translatex" => SKMatrix.CreateTranslation(args.ElementAtOrDefault(0), 0),
            "translatey" => SKMatrix.CreateTranslation(0, args.ElementAtOrDefault(0)),
            "rotate" => SKMatrix.CreateRotationDegrees(args.ElementAtOrDefault(0)),
            "rotatex" => CreateRotateX(args.ElementAtOrDefault(0)),
            "rotatey" => CreateRotateY(args.ElementAtOrDefault(0)),
            "rotatez" => SKMatrix.CreateRotationDegrees(args.ElementAtOrDefault(0)),
            "rotate3d" => CreateRotate3d(args),
            // scale(s) with one argument applies to BOTH axes (CSS Transforms §6).
            "scale" or "scale3d" => SKMatrix.CreateScale(args.Length > 0 ? args[0] : 1f, args.Length > 1 ? args[1] : (args.Length > 0 ? args[0] : 1f)),
            "scalex" => SKMatrix.CreateScale(args.Length > 0 ? args[0] : 1f, 1),
            "scaley" => SKMatrix.CreateScale(1, args.Length > 0 ? args[0] : 1f),
            "skew" => CreateSkew(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)),
            "skewx" => CreateSkew(args.ElementAtOrDefault(0), 0),
            "skewy" => CreateSkew(0, args.ElementAtOrDefault(0)),
            "matrix" => CreateMatrix(args),
            "matrix3d" => CreateMatrix3d(args),
            _ => SKMatrix.Identity
        };
    }

    private static SKMatrix CreateRotateX(float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        return SKMatrix.CreateScale(1, cos);
    }

    private static SKMatrix CreateRotateY(float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        return SKMatrix.CreateScale(cos, 1);
    }

    private static SKMatrix CreateSkew(float x, float y)
    {
        float tanX = MathF.Tan(x * MathF.PI / 180f);
        float tanY = MathF.Tan(y * MathF.PI / 180f);
        return new SKMatrix
        {
            ScaleX = 1, SkewX = tanX, TransX = 0,
            SkewY = tanY, ScaleY = 1, TransY = 0,
            Persp0 = 0, Persp1 = 0, Persp2 = 1
        };
    }

    private static SKMatrix CreateMatrix(float[] args)
    {
        if (args.Length < 6) return SKMatrix.Identity;
        return new SKMatrix
        {
            ScaleX = args[0], SkewX = args[2], TransX = args[4],
            SkewY = args[1], ScaleY = args[3], TransY = args[5],
            Persp0 = 0, Persp1 = 0, Persp2 = 1
        };
    }

    /// <summary>
    /// matrix3d() is column-major, so the 2D-relevant entries are the first two
    /// columns plus the translation in the fourth. Dropping the rest is the same
    /// orthographic flattening rotateX/rotateY already use.
    /// </summary>
    private static SKMatrix CreateMatrix3d(float[] args)
    {
        if (args.Length < 16) return SKMatrix.Identity;
        return new SKMatrix
        {
            ScaleX = args[0], SkewX = args[4], TransX = args[12],
            SkewY = args[1], ScaleY = args[5], TransY = args[13],
            Persp0 = 0, Persp1 = 0, Persp2 = 1
        };
    }

    /// <summary>
    /// rotate3d() about an arbitrary axis, flattened to the plane: the rotation
    /// matrix' top-left 2x2 is its action on z = 0, which reduces to rotate() for a
    /// z axis and to the rotateX/rotateY squashes for the other two.
    /// </summary>
    private static SKMatrix CreateRotate3d(float[] args)
    {
        if (args.Length < 4) return SKMatrix.Identity;
        float x = args[0], y = args[1], z = args[2];
        float length = MathF.Sqrt(x * x + y * y + z * z);
        if (length == 0) return SKMatrix.Identity;
        x /= length; y /= length; z /= length;

        float angle = args[3] * MathF.PI / 180f;
        float c = MathF.Cos(angle);
        float s = MathF.Sin(angle);
        float t = 1 - c;

        return new SKMatrix
        {
            // Row 1 and 2 of the Rodrigues rotation matrix.
            ScaleX = c + x * x * t, SkewX = x * y * t - z * s, TransX = 0,
            SkewY = y * x * t + z * s, ScaleY = c + y * y * t, TransY = 0,
            Persp0 = 0, Persp1 = 0, Persp2 = 1
        };
    }

    private static float ParseFloat(string s)
    {
        s = s.Trim();
        // 'grad' is tested before 'rad' because it ends in it, and an unrecognised angle unit
        // must not fall through to the bare-number branch and read as a pixel length.
        if (s.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s[..^4], out var turn)) return turn * 360f;
        }
        if (s.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s[..^4], out var grad)) return grad * 0.9f;
        }
        if (s.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s[..^3], out var deg)) return deg;
        }
        if (s.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s[..^3], out var rad)) return rad * 180f / MathF.PI;
        }
        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s[..^2], out var px)) return px;
        }
        if (s.EndsWith("%", StringComparison.OrdinalIgnoreCase))
        {
            return 0; // translate percentages are rewritten to px before parsing; any
                      // remaining percentage has no geometry here, so contribute nothing.
        }
        if (float.TryParse(s, out var val)) return val;
        return 0;
    }
}

public class TransformOperation
{
    public string Function { get; set; } = "";
    public string[] Args { get; set; } = Array.Empty<string>();
}
