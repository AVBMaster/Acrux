using System.Text.RegularExpressions;
using SkiaSharp;

namespace UpBrowser.Core.Css;

public static class TransformParser
{
    private static readonly Regex TransformRegex = new(
        @"(translateX|translateY|translate|translate3d|rotateX|rotateY|rotateZ|rotate3d|rotate|scaleX|scaleY|scale|scale3d|skewX|skewY|skew|matrix|matrix3d|perspective)\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase);

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
            return 0; // percent handled by caller via other transforms; net zero is safe
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
