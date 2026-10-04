namespace Acrux.Core.Dom;

public class DomPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double W { get; set; }

    public DomPoint() : this(0, 0, 0, 1)
    {
    }

    public DomPoint(double x, double y) : this(x, y, 0, 1)
    {
    }

    public DomPoint(double x, double y, double z) : this(x, y, z, 1)
    {
    }

    public DomPoint(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static DomPoint FromPoint(DomPoint other)
    {
        return new DomPoint(other.X, other.Y, other.Z, other.W);
    }

    public DomPoint MatrixTransform(DomMatrix matrix)
    {
        var m = matrix;
        double newX = m.M11 * X + m.M21 * Y + m.M31 * Z + m.M41 * W;
        double newY = m.M12 * X + m.M22 * Y + m.M32 * Z + m.M42 * W;
        double newZ = m.M13 * X + m.M23 * Y + m.M33 * Z + m.M43 * W;
        double newW = m.M14 * X + m.M24 * Y + m.M34 * Z + m.M44 * W;
        return new DomPoint(newX, newY, newZ, newW);
    }

    public override string ToString() => $"({X}, {Y}, {Z}, {W})";
}
