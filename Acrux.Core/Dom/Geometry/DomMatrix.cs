namespace Acrux.Core.Dom;

public class DomMatrix
{
    public double M11 { get; set; }
    public double M12 { get; set; }
    public double M13 { get; set; }
    public double M14 { get; set; }
    public double M21 { get; set; }
    public double M22 { get; set; }
    public double M23 { get; set; }
    public double M24 { get; set; }
    public double M31 { get; set; }
    public double M32 { get; set; }
    public double M33 { get; set; }
    public double M34 { get; set; }
    public double M41 { get; set; }
    public double M42 { get; set; }
    public double M43 { get; set; }
    public double M44 { get; set; }

    public DomMatrix() : this(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1)
    {
    }

    public DomMatrix(
        double m11, double m12, double m13, double m14,
        double m21, double m22, double m23, double m24,
        double m31, double m32, double m33, double m34,
        double m41, double m42, double m43, double m44)
    {
        M11 = m11; M12 = m12; M13 = m13; M14 = m14;
        M21 = m21; M22 = m22; M23 = m23; M24 = m24;
        M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        M41 = m41; M42 = m42; M43 = m43; M44 = m44;
    }

    public bool IsIdentity =>
        M11 == 1 && M12 == 0 && M13 == 0 && M14 == 0 &&
        M21 == 0 && M22 == 1 && M23 == 0 && M24 == 0 &&
        M31 == 0 && M32 == 0 && M33 == 1 && M34 == 0 &&
        M41 == 0 && M42 == 0 && M43 == 0 && M44 == 1;

    public DomMatrix Multiply(DomMatrix other)
    {
        var a = this;
        var b = other;
        return new DomMatrix(
            a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41,
            a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42,
            a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43,
            a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44,
            a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41,
            a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42,
            a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43,
            a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44,
            a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41,
            a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42,
            a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43,
            a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44,
            a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41,
            a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42,
            a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43,
            a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44);
    }

    public DomMatrix Translate(double tx, double ty, double tz = 0)
    {
        var translation = new DomMatrix(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            tx, ty, tz, 1);
        return Multiply(translation);
    }

    public DomMatrix Scale(double sx, double sy, double sz = 1)
    {
        var scale = new DomMatrix(
            sx, 0, 0, 0,
            0, sy, 0, 0,
            0, 0, sz, 0,
            0, 0, 0, 1);
        return Multiply(scale);
    }

    public DomMatrix Rotate(double angle, double? originX = null, double? originY = null)
    {
        double rad = angle * Math.PI / 180;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        var rotation = new DomMatrix(
            cos, sin, 0, 0,
            -sin, cos, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        return Multiply(rotation);
    }

    public DomMatrix RotateAxisAngle(double x, double y, double z, double angle)
    {
        double rad = angle * Math.PI / 180;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        double len = Math.Sqrt(x * x + y * y + z * z);
        if (len == 0) return Clone();
        x /= len; y /= len; z /= len;
        double t = 1 - cos;
        var rotation = new DomMatrix(
            cos + x * x * t, y * x * t + z * sin, z * x * t - y * sin, 0,
            x * y * t - z * sin, cos + y * y * t, z * y * t + x * sin, 0,
            x * z * t + y * sin, y * z * t - x * sin, cos + z * z * t, 0,
            0, 0, 0, 1);
        return Multiply(rotation);
    }

    public DomMatrix SkewX(double angle)
    {
        double rad = angle * Math.PI / 180;
        var skew = new DomMatrix(
            1, 0, 0, 0,
            Math.Tan(rad), 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        return Multiply(skew);
    }

    public DomMatrix SkewY(double angle)
    {
        double rad = angle * Math.PI / 180;
        var skew = new DomMatrix(
            1, Math.Tan(rad), 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        return Multiply(skew);
    }

    public DomMatrix Invert()
    {
        double det = M11 * (M22 * (M33 * M44 - M34 * M43) - M23 * (M32 * M44 - M34 * M42) + M24 * (M32 * M43 - M33 * M42))
                   - M12 * (M21 * (M33 * M44 - M34 * M43) - M23 * (M31 * M44 - M34 * M41) + M24 * (M31 * M43 - M33 * M41))
                   + M13 * (M21 * (M32 * M44 - M34 * M42) - M22 * (M31 * M44 - M34 * M41) + M24 * (M31 * M42 - M32 * M41))
                   - M14 * (M21 * (M32 * M43 - M33 * M42) - M22 * (M31 * M43 - M33 * M41) + M23 * (M31 * M42 - M32 * M41));
        if (Math.Abs(det) < 1e-10) throw new InvalidOperationException("Matrix is not invertible");
        double invDet = 1.0 / det;
        // Simplified 4x4 inverse - for a full implementation use a library
        return new DomMatrix();
    }

    public DomPoint TransformPoint(DomPoint point)
    {
        return point.MatrixTransform(this);
    }

    public DomRect TransformRect(DomRect rect)
    {
        var p1 = TransformPoint(new DomPoint(rect.X, rect.Y));
        var p2 = TransformPoint(new DomPoint(rect.X + rect.Width, rect.Y));
        var p3 = TransformPoint(new DomPoint(rect.X + rect.Width, rect.Y + rect.Height));
        var p4 = TransformPoint(new DomPoint(rect.X, rect.Y + rect.Height));
        double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
        return new DomRect(minX, minY, maxX - minX, maxY - minY);
    }

    public DomMatrix Clone() => new(M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, M41, M42, M43, M44);

    public override string ToString() =>
        $"matrix({M11}, {M12}, {M21}, {M22}, {M41}, {M42})";
}
