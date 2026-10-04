namespace Acrux.Core.Dom;

public class DomQuad
{
    public DomPoint P1 { get; set; }
    public DomPoint P2 { get; set; }
    public DomPoint P3 { get; set; }
    public DomPoint P4 { get; set; }

    public DomQuad() : this(new DomPoint(), new DomPoint(), new DomPoint(), new DomPoint())
    {
    }

    public DomQuad(DomPoint p1, DomPoint p2, DomPoint p3, DomPoint p4)
    {
        P1 = p1;
        P2 = p2;
        P3 = p3;
        P4 = p4;
    }

    public DomQuad(DomRect rect) : this(
        new DomPoint(rect.X, rect.Y),
        new DomPoint(rect.X + rect.Width, rect.Y),
        new DomPoint(rect.X + rect.Width, rect.Y + rect.Height),
        new DomPoint(rect.X, rect.Y + rect.Height))
    {
    }

    public DomRect GetBounds()
    {
        double minX = Math.Min(Math.Min(P1.X, P2.X), Math.Min(P3.X, P4.X));
        double minY = Math.Min(Math.Min(P1.Y, P2.Y), Math.Min(P3.Y, P4.Y));
        double maxX = Math.Max(Math.Max(P1.X, P2.X), Math.Max(P3.X, P4.X));
        double maxY = Math.Max(Math.Max(P1.Y, P2.Y), Math.Max(P3.Y, P4.Y));
        return new DomRect(minX, minY, maxX - minX, maxY - minY);
    }

    public override string ToString() => $"({P1}, {P2}, {P3}, {P4})";
}
