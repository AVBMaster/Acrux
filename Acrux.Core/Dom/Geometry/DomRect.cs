namespace Acrux.Core.Dom;

public class DomRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public DomRect()
    {
    }

    public DomRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Left => X;

    public static DomRect FromRect(DomRect other)
    {
        return new DomRect(other.X, other.Y, other.Width, other.Height);
    }

    public override string ToString() => $"({X}, {Y}) {Width} x {Height}";
}

public class DomRectReadOnly
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public DomRectReadOnly(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Left => X;

    public override string ToString() => $"({X}, {Y}) {Width} x {Height}";
}
