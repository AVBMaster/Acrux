using SkiaSharp;

namespace Acrux.Core;

public interface IImeSupport
{
    Point GetImeCaretPosition();
    void OnImeCompositionStart();
    void OnImeCompositionUpdate(string compositionString, int cursorPosition);
    void OnImeCompositionEnd(string? resultString);
}

public struct Point
{
    public float X { get; set; }
    public float Y { get; set; }

    public Point(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static implicit operator SKPoint(Point p) => new(p.X, p.Y);
    public static implicit operator Point(SKPoint p) => new(p.X, p.Y);
}