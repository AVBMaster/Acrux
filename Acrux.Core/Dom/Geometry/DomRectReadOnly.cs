namespace Acrux.Core.Dom;

public class DOMRectReadOnly
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public DOMRectReadOnly(double x = 0, double y = 0, double width = 0, double height = 0)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    public double Top => Math.Min(Y, Y + Height);
    public double Right => Math.Max(X, X + Width);
    public double Bottom => Math.Max(Y, Y + Height);
    public double Left => Math.Min(X, X + Width);

    public static DOMRectReadOnly FromRect(DOMRectReadOnly other) =>
        new(other.X, other.Y, other.Width, other.Height);
}
