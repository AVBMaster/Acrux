using SkiaSharp;

namespace Acrux.Core.Css;

/// <summary>
/// Color interpolation and the relative color syntax (CSS Color 4 §10, Color 5 §3-§5).
///
/// 'color-mix()' must interpolate in the *named* space, not in sRGB, and the
/// relative color syntax substitutes channels after converting the origin color
/// into the target space, so both need a common representation of a color as four
/// channels in a known space. This file owns those conversions; everything enters
/// and leaves through 8-bit sRGB, which is the engine's color currency.
/// </summary>
internal readonly struct ColorChannels
{
    /// <summary>Space the channels live in: srgb, srgb-linear, hsl, hwb, lab, lch, oklab, oklch, xyz, xyz-d50.</summary>
    public readonly string Space;

    /// <summary>First three channels in the space's own units; for hue-bearing
    /// spaces channel 0 is the hue in degrees. NaN means 'none'.</summary>
    public readonly float C0;
    public readonly float C1;
    public readonly float C2;
    public readonly float Alpha;

    public ColorChannels(string space, float c0, float c1, float c2, float alpha)
    {
        Space = space;
        C0 = c0; C1 = c1; C2 = c2; Alpha = alpha;
    }

    public ColorChannels With(string space, float c0, float c1, float c2) =>
        new(space, c0, c1, c2, Alpha);
}

internal static class ColorInterpolation
{
    // ------------------------------------------------------------------
    // Entry / exit through sRGB bytes
    // ------------------------------------------------------------------

    /// <summary>Read a color's channels in <paramref name="space"/>. Gamma-encoded
    /// sRGB components are used for 'srgb' because that space interpolates there.</summary>
    public static ColorChannels FromSrgb(SKColor color, string space)
    {
        float r = color.Red / 255f, g = color.Green / 255f, b = color.Blue / 255f;
        float a = color.Alpha / 255f;

        switch (Normalize(space))
        {
            case "srgb":
                return new ColorChannels("srgb", r, g, b, a);
            case "srgb-linear":
                return new ColorChannels("srgb-linear", SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b), a);
            case "hsl":
            {
                var (h, s, l) = RgbToHsl(r, g, b);
                return new ColorChannels("hsl", h, s, l, a);
            }
            case "hwb":
            {
                var (h, s, _) = RgbToHsl(r, g, b);
                float w = MathF.Min(r, MathF.Min(g, b));
                float k = 1 - MathF.Max(r, MathF.Max(g, b));
                return new ColorChannels("hwb", h, w, k, a);
            }
            case "lab":
            {
                var (l, aa, bb) = LinearRgbToLab(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
                return new ColorChannels("lab", l, aa, bb, a);
            }
            case "lch":
            {
                var (l, aa, bb) = LinearRgbToLab(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
                var (lc, hs, hue) = LabToLch(l, aa, bb);
                return new ColorChannels("lch", lc, hs, hue, a);
            }
            case "oklab":
            {
                var (l, aa, bb) = LinearRgbToOklab(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
                return new ColorChannels("oklab", l, aa, bb, a);
            }
            case "oklch":
            {
                var (l, aa, bb) = LinearRgbToOklab(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
                var (lc, hs, hue) = LabToLch(l, aa, bb);
                return new ColorChannels("oklch", lc, hs, hue, a);
            }
            case "xyz":
            case "xyz-d65":
            {
                var (x, y, z) = LinearRgbToXyzD65(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
                return new ColorChannels("xyz-d65", x, y, z, a);
            }
            case "xyz-d50":
            {
                var (x, y, z) = LinearRgbToXyzD50(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
                return new ColorChannels("xyz-d50", x, y, z, a);
            }
            default:
                return new ColorChannels("srgb", r, g, b, a);
        }
    }

    public static SKColor ToSrgb(in ColorChannels c)
    {
        float r, g, b;
        switch (c.Space)
        {
            case "srgb":
                (r, g, b) = (c.C0, c.C1, c.C2);
                break;
            case "srgb-linear":
                (r, g, b) = (LinearToSrgb(c.C0), LinearToSrgb(c.C1), LinearToSrgb(c.C2));
                break;
            case "hsl":
            {
                var (rr, gg, bb) = HslToRgb(c.C0, c.C1, c.C2);
                (r, g, b) = (rr, gg, bb);
                break;
            }
            case "hwb":
            {
                var (rr, gg, bb) = HwbToRgb(c.C0, c.C1, c.C2);
                (r, g, b) = (rr, gg, bb);
                break;
            }
            case "lab":
            {
                var (lr, lg, lb) = LabToLinearRgb(c.C0, c.C1, c.C2);
                (r, g, b) = (LinearToSrgb(lr), LinearToSrgb(lg), LinearToSrgb(lb));
                break;
            }
            case "lch":
            {
                var (ll, la, lb2) = LchToLab(c.C0, c.C1, c.C2);
                var (lr, lg, lb) = LabToLinearRgb(ll, la, lb2);
                (r, g, b) = (LinearToSrgb(lr), LinearToSrgb(lg), LinearToSrgb(lb));
                break;
            }
            case "oklab":
            {
                var (lr, lg, lb) = OklabToLinearRgb(c.C0, c.C1, c.C2);
                (r, g, b) = (LinearToSrgb(lr), LinearToSrgb(lg), LinearToSrgb(lb));
                break;
            }
            case "oklch":
            {
                var (ol, oa, ob) = LchToLab(c.C0, c.C1, c.C2);
                var (lr, lg, lb) = OklabToLinearRgb(ol, oa, ob);
                (r, g, b) = (LinearToSrgb(lr), LinearToSrgb(lg), LinearToSrgb(lb));
                break;
            }
            case "xyz-d65":
            {
                var (lr, lg, lb) = XyzD65ToLinearRgb(c.C0, c.C1, c.C2);
                (r, g, b) = (LinearToSrgb(lr), LinearToSrgb(lg), LinearToSrgb(lb));
                break;
            }
            case "xyz-d50":
            {
                var (lr, lg, lb) = XyzD50ToLinearRgb(c.C0, c.C1, c.C2);
                (r, g, b) = (LinearToSrgb(lr), LinearToSrgb(lg), LinearToSrgb(lb));
                break;
            }
            default:
                (r, g, b) = (c.C0, c.C1, c.C2);
                break;
        }

        return new SKColor(ToByte(r), ToByte(g), ToByte(b), ToByteAlpha(c.Alpha));
    }

    // ------------------------------------------------------------------
    // Mixing
    // ------------------------------------------------------------------

    /// <summary>
    /// Interpolate two colors already expressed in the same space. Alpha is
    /// premultiplied in that space (except for a hue channel), per CSS Color 5 §3.
    /// </summary>
    public static ColorChannels Mix(in ColorChannels x, float weightX, in ColorChannels y, float weightY,
        string hueMethod)
    {
        float total = weightX + weightY;
        if (total <= 0) { weightX = weightY = 0.5f; total = 1f; }
        float p = weightX / total;
        float q = weightY / total;

        float alpha = x.Alpha * p + y.Alpha * q;

        // Hue is not premultiplied and interpolates on the shortest arc; the
        // cylindrical spaces keep it last (lch/oklch) or first (hsl/hwb).
        int hueIndex = HueIndex(x.Space);
        float c0 = hueIndex == 0 ? InterpolateHue(x.C0, y.C0, p, hueMethod)
                                 : MixChannel(x.C0, x.Alpha, y.C0, y.Alpha, p, q, alpha);
        float c1 = hueIndex == 1 ? InterpolateHue(x.C1, y.C1, p, hueMethod)
                                 : MixChannel(x.C1, x.Alpha, y.C1, y.Alpha, p, q, alpha);
        float c2 = hueIndex == 2 ? InterpolateHue(x.C2, y.C2, p, hueMethod)
                                 : MixChannel(x.C2, x.Alpha, y.C2, y.Alpha, p, q, alpha);

        return new ColorChannels(x.Space, c0, c1, c2, alpha);
    }

    private static float MixChannel(float cx, float ax, float cy, float ay, float p, float q, float alpha)
    {
        if (float.IsNaN(cx)) return cy;
        if (float.IsNaN(cy)) return cx;
        if (alpha > 0)
            return (cx * ax * p + cy * ay * q) / alpha;
        return cx * p + cy * q;
    }

    /// <summary>Index of the hue channel in a cylindrical space, or -1.</summary>
    public static int HueIndex(string space) => space switch
    {
        "hsl" or "hwb" => 0,
        "lch" or "oklch" => 2,
        _ => -1,
    };

    /// <summary>Hue interpolation with the shorter-arc default (CSS Color 4 §11).</summary>
    public static float InterpolateHue(float h1, float h2, float t, string method)
    {
        if (float.IsNaN(h1)) return h2;
        if (float.IsNaN(h2)) return h1;
        float d = h2 - h1;
        switch (method)
        {
            case "longer":
                if (d > 0) h1 += 360; else h2 += 360;
                break;
            case "specified":
                break;
            default: // shorter
                if (MathF.Abs(d) > 180)
                {
                    if (d > 0) h1 += 360; else h2 += 360;
                }
                break;
        }
        return h1 + (h2 - h1) * t;
    }

    // ------------------------------------------------------------------
    // Channel conversion maths
    // ------------------------------------------------------------------

    public static string Normalize(string space) => space.Trim().ToLowerInvariant();

    public static float SrgbToLinear(float v) =>
        v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);

    public static float LinearToSrgb(float v) =>
        v <= 0.0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;

    public static (float h, float s, float l) RgbToHsl(float r, float g, float b)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float l = (max + min) / 2f;
        float delta = max - min;
        if (delta == 0) return (0, 0, l);

        float s = l > 0.5f ? delta / (2 - max - min) : delta / (max + min);
        float h = max == r ? ((g - b) / delta + (g < b ? 6 : 0))
              : max == g ? (b - r) / delta + 2
              : (r - g) / delta + 4;
        return (h * 60, s, l);
    }

    public static (float r, float g, float b) HslToRgb(float h, float s, float l)
    {
        h = ((h % 360) + 360) % 360;
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
        float m = l - c / 2;
        float r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return (r + m, g + m, b + m);
    }

    public static (float r, float g, float b) HwbToRgb(float h, float w, float bl)
    {
        w = Math.Clamp(w, 0, 1);
        bl = Math.Clamp(bl, 0, 1);
        float sum = w + bl;
        if (sum > 1f) { w /= sum; bl /= sum; }
        var (r, g, b) = HslToRgb(h, 1f, 0.5f);
        return (r * (1 - w - bl) + w, g * (1 - w - bl) + w, b * (1 - w - bl) + w);
    }

    private static (float x, float y, float z) LinearRgbToXyzD65(float r, float g, float b) =>
        (0.4123908f * r + 0.3575843f * g + 0.1804808f * b,
         0.2126390f * r + 0.7151687f * g + 0.0721923f * b,
         0.0193308f * r + 0.1191948f * g + 0.9505322f * b);

    private static (float r, float g, float b) XyzD65ToLinearRgb(float x, float y, float z) =>
        (3.2409699f * x - 1.5373832f * y - 0.4986108f * z,
         -0.9692436f * x + 1.8759675f * y + 0.0415551f * z,
         0.0556301f * x - 0.2039970f * y + 1.0569716f * z);

    // lab()/lch() are defined against the D50 white point, so the Bradford
    // adaptation is folded into these two matrices (CSS Color 4 §10.3, §10.5)
    // rather than approximated by scaling the D65 result.
    private static (float x, float y, float z) LinearRgbToXyzD50(float r, float g, float b) =>
        (0.4360747f * r + 0.3850649f * g + 0.1430804f * b,
         0.2225045f * r + 0.7168786f * g + 0.0606169f * b,
         0.0139322f * r + 0.0971045f * g + 0.7141733f * b);

    private static (float r, float g, float b) XyzD50ToLinearRgb(float x, float y, float z) =>
        (3.1338561f * x - 1.6168667f * y - 0.4975667f * z,
         -0.9787684f * x + 1.9161415f * y + 0.0334542f * z,
         0.0719453f * x - 0.2283120f * y + 1.4057661f * z);

    private const float WhiteXn = 0.96422f, WhiteYn = 1f, WhiteZn = 0.82521f;

    public static (float l, float a, float b) LinearRgbToLab(float r, float g, float b)
    {
        var (x, y, z) = LinearRgbToXyzD50(r, g, b);
        float fx = LabF(x / WhiteXn), fy = LabF(y / WhiteYn), fz = LabF(z / WhiteZn);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    public static (float r, float g, float b) LabToLinearRgb(float l, float a, float bb)
    {
        float fy = (l + 16) / 116f;
        float fx = a / 500f + fy;
        float fz = fy - bb / 200f;
        float x = LabInverse(fx) * WhiteXn;
        float y = LabInverse(fy) * WhiteYn;
        float z = LabInverse(fz) * WhiteZn;
        return XyzD50ToLinearRgb(x, y, z);
    }

    private static float LabF(float t) =>
        t > 216f / 24389f ? MathF.Cbrt(t) : (24389f / 27f * t + 16f) / 116f;

    private static float LabInverse(float t)
    {
        float t3 = t * t * t;
        return t3 > 216f / 24389f ? t3 : (t - 4f / 29f) * 3 * (6f / 29f) * (6f / 29f);
    }

    public static (float l, float c, float h) LabToLch(float l, float a, float b)
    {
        float c = MathF.Sqrt(a * a + b * b);
        float h = c == 0 ? 0 : MathF.Atan2(b, a) * 180f / MathF.PI;
        if (h < 0) h += 360;
        return (l, c, h);
    }

    public static (float l, float a, float b) LchToLab(float l, float c, float h)
    {
        float rad = h * MathF.PI / 180f;
        return (l, c * MathF.Cos(rad), c * MathF.Sin(rad));
    }

    // ------------------------------------------------------------------
    // Named RGB color spaces of color() (CSS Color 4 §11)
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolve color(&lt;space&gt; c1 c2 c3 [/ a]). Each space has its own transfer
    /// function and its matrix into XYZ D65 (ProPhoto is specified against D50), so
    /// the value is decoded, converted, and re-encoded through sRGB.
    /// </summary>
    public static SKColor FromColorSpace(string space, float c0, float c1, float c2, float alpha)
    {
        string key = Normalize(space);

        switch (key)
        {
            case "xyz":
            case "xyz-d65":
                return XyzToSrgbColor(c0, c1, c2, alpha);
            case "xyz-d50":
            {
                var (x, y, z) = AdaptWhite(c0, c1, c2, WhiteD50, WhiteD65);
                return XyzToSrgbColor(x, y, z, alpha);
            }
        }

        float[] encoded = key switch
        {
            "srgb-linear" => new[] { c0, c1, c2 },
            "a98-rgb" => new[] { DecodeA98(c0), DecodeA98(c1), DecodeA98(c2) },
            "prophoto-rgb" => new[] { DecodeProPhoto(c0), DecodeProPhoto(c1), DecodeProPhoto(c2) },
            "rec2020" => new[] { DecodeRec2020(c0), DecodeRec2020(c1), DecodeRec2020(c2) },
            _ => new[] { SrgbToLinear(c0), SrgbToLinear(c1), SrgbToLinear(c2) },
        };

        float[] xyz = key switch
        {
            "a98-rgb" => Mat(new[] {
                0.5767309f, 0.1855545f, 0.1882373f,
                0.29737697f, 0.6273491f, 0.07527413f,
                0.02703436f, 0.07068746f, 0.9913375f }, encoded),
            // ProPhoto RGB is specified against the D50 white point.
            "prophoto-rgb" => ProPhotoToXyzD65(encoded),
            "rec2020" => Mat(new[] {
                0.63695804f, 0.1446169f, 0.16888097f,
                0.26270391f, 0.67799807f, 0.05929801f,
                0f, 0.02807269f, 1.06098505f }, encoded),
            "display-p3" => Mat(new[] {
                0.48657094f, 0.26566769f, 0.19821728f,
                0.22897456f, 0.69173852f, 0.07928691f,
                0f, 0.04511338f, 0.80842133f }, encoded),
            _ => Mat(new[] {
                0.4123908f, 0.3575843f, 0.1804808f,
                0.2126390f, 0.7151687f, 0.0721923f,
                0.0193308f, 0.1191948f, 0.9505322f }, encoded),
        };

        var (lr, lg, lb) = XyzD65ToLinearRgb(xyz[0], xyz[1], xyz[2]);
        return new SKColor(ToByte(LinearToSrgb(lr)), ToByte(LinearToSrgb(lg)),
            ToByte(LinearToSrgb(lb)), ToByteAlpha(alpha));
    }

    private static float[] ProPhotoToXyzD65(float[] encoded)
    {
        var xyz = Mat(new[] {
            0.7976749f, 0.1351917f, 0.0313534f,
            0.2880402f, 0.7118741f, 0.0000857f,
            0f, 0f, 0.8228567f }, encoded);
        var (x, y, z) = AdaptWhite(xyz[0], xyz[1], xyz[2], WhiteD50, WhiteD65);
        return new[] { x, y, z };
    }

    private static readonly (float X, float Y, float Z) WhiteD65 = (0.95047f, 1f, 1.08883f);
    private static readonly (float X, float Y, float Z) WhiteD50 = (0.96422f, 1f, 0.82521f);

    /// <summary>Von Kries chromatic adaptation in the Bradford cone space.</summary>
    private static (float x, float y, float z) AdaptWhite(
        float x, float y, float z,
        (float X, float Y, float Z) source, (float X, float Y, float Z) dest)
    {
        static (float l, float m, float s) ToLms(float X, float Y, float Z) =>
            (0.8951f * X + 0.2664f * Y - 0.1614f * Z,
             -0.7502f * X + 1.7135f * Y + 0.0367f * Z,
             0.0382f * X + 0.0685f * Y + 1.0159f * Z);
        static (float X, float Y, float Z) FromLms(float l, float m, float s) =>
            (0.986993f * l - 0.147054f * m + 0.159962f * s,
             -0.432350f * l + 1.518360f * m + 0.049291f * s,
             0.008528f * l - 0.020102f * m + 1.537275f * s);

        var (ls, ms, ss) = ToLms(source.X, source.Y, source.Z);
        var (ld, md, sd) = ToLms(dest.X, dest.Y, dest.Z);
        float kr = ls == 0 ? 1f : ld / ls;
        float kg = ms == 0 ? 1f : md / ms;
        float kb = ss == 0 ? 1f : sd / ss;
        var (l, m, s) = ToLms(x, y, z);
        return FromLms(l * kr, m * kg, s * kb);
    }

    private static SKColor XyzToSrgbColor(float x, float y, float z, float alpha)
    {
        var (r, g, b) = XyzD65ToLinearRgb(x, y, z);
        return new SKColor(ToByte(LinearToSrgb(r)), ToByte(LinearToSrgb(g)),
            ToByte(LinearToSrgb(b)), ToByteAlpha(alpha));
    }

    /// <summary>Quantize a 0..1 channel to a byte in double precision: the float
    /// product of e.g. 0.9 * 255 lands just below .5 and would round down a step.</summary>
    private static byte ToByte(float v)
    {
        double d = Math.Clamp((double)v, 0d, 1d) * 255d;
        return (byte)(d >= 254.5d ? 255 : (int)(d + 0.5d));
    }

    private static byte ToByteAlpha(float a)
    {
        double d = Math.Clamp((double)a, 0d, 1d) * 255d;
        return (byte)(d >= 254.5d ? 255 : (int)(d + 0.5d));
    }

    private static float[] Mat(float[] m, float[] v) =>
        [m[0] * v[0] + m[1] * v[1] + m[2] * v[2],
         m[3] * v[0] + m[4] * v[1] + m[5] * v[2],
         m[6] * v[0] + m[7] * v[1] + m[8] * v[2]];

    private static float DecodeA98(float c)
    {
        float sign = c < 0 ? -1f : 1f;
        float a = MathF.Abs(c);
        float v = a <= 1f / 64f ? a / 16f : MathF.Pow((a + 0.01953f) / 1.01953f, 563f / 256f);
        return sign * v;
    }

    private static float DecodeProPhoto(float c)
    {
        float sign = c < 0 ? -1f : 1f;
        float e = MathF.Abs(c);
        float v = e < 16f / 512f ? e / 16f : ((8f * MathF.Sqrt(e + 0.041f) + 0.0956f) *
                                              (8f * MathF.Sqrt(e + 0.041f) + 0.0956f)) / 64f;
        return sign * v;
    }

    private static float DecodeRec2020(float c)
    {
        float sign = c < 0 ? -1f : 1f;
        float e = MathF.Abs(c);
        const float alpha = 1.09929682680944f, beta = 0.018053968510807f;
        float v = e < beta * 4.5f ? e / 4.5f : MathF.Pow((e + beta) / alpha, 1f / 0.45f);
        return sign * v;
    }

    public static (float l, float a, float b) LinearRgbToOklab(float r, float g, float b)
    {
        float l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
        float m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
        float s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;
        float l_ = MathF.Cbrt(l), m_ = MathF.Cbrt(m), s_ = MathF.Cbrt(s);
        return (0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
                1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
                0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_);
    }

    public static (float r, float g, float b) OklabToLinearRgb(float l, float a, float b)
    {
        float l_ = l + 0.3963377774f * a + 0.2158037573f * b;
        float m_ = l - 0.1055613458f * a - 0.0638541728f * b;
        float s_ = l - 0.0894841775f * a - 1.2914855480f * b;
        float l3 = l_ * l_ * l_, m3 = m_ * m_ * m_, s3 = s_ * s_ * s_;
        return (4.0767416621f * l3 - 3.3077115913f * m3 + 0.2309699292f * s3,
                -1.2684380046f * l3 + 2.6097574011f * m3 - 0.3413193965f * s3,
                -0.0041960863f * l3 - 0.7034186147f * m3 + 1.7076147010f * s3);
    }
}
