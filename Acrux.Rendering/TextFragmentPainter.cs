using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Transliteration of the list-marker symbol painting logic.
/// Paints disc / circle / square / disclosure markers as geometry
/// (FillEllipse / StrokeEllipse / FillRect / FillPath) instead of glyphs.
/// </summary>
public sealed class TextFragmentPainter
{
    private readonly DisplayList _displayList;

    public TextFragmentPainter(DisplayList displayList)
    {
        _displayList = displayList;
    }

    /// <summary>
    /// Physical writing direction of a disclosure marker, based on writing
    /// direction and open/closed state.
    /// </summary>
    private enum PhysicalDirection { Left, Right, Up, Down }

    /// <summary>Build the canonical disclosure triangle path in marker-space (0..1).</summary>
    private static SKPath GetCanonicalDisclosurePath(ComputedStyle style, bool isOpen)
    {
        Span<SKPoint> points = stackalloc SKPoint[4];
        switch (GetDisclosureOrientation(style, isOpen))
        {
            case PhysicalDirection.Left:
                points = new[] { new SKPoint(1.0f, 0.0f), new SKPoint(0.14f, 0.5f), new SKPoint(1.0f, 1.0f), new SKPoint(1.0f, 0.0f) };
                break;
            case PhysicalDirection.Right:
                points = new[] { new SKPoint(0.0f, 0.0f), new SKPoint(0.86f, 0.5f), new SKPoint(0.0f, 1.0f), new SKPoint(0.0f, 0.0f) };
                break;
            case PhysicalDirection.Up:
                points = new[] { new SKPoint(0.0f, 0.93f), new SKPoint(0.5f, 0.07f), new SKPoint(1.0f, 0.93f), new SKPoint(0.0f, 0.93f) };
                break;
            case PhysicalDirection.Down:
                points = new[] { new SKPoint(0.0f, 0.07f), new SKPoint(0.5f, 0.93f), new SKPoint(1.0f, 0.07f), new SKPoint(0.0f, 0.07f) };
                break;
        }

        var path = new SKPath();
        path.MoveTo(points[0]);
        for (int i = 1; i < 4; i++)
            path.LineTo(points[i]);
        path.Close();
        return path;
    }

    private static PhysicalDirection GetDisclosureOrientation(ComputedStyle style, bool isOpen)
    {
        bool rtl = false;
        if (style.Direction != null)
            rtl = style.Direction.Equals("rtl", StringComparison.OrdinalIgnoreCase) || style.Direction.Equals("ltr", StringComparison.OrdinalIgnoreCase) == false;
        return isOpen ? PhysicalDirection.Down : (rtl ? PhysicalDirection.Left : PhysicalDirection.Right);
    }

    /// <summary>
    /// Paint a symbol (disc / circle / square / disclosure) list marker.
    /// Mirrors TextFragmentPainter::PaintSymbol.
    /// </summary>
    public void PaintSymbol(ComputedStyle style, SKRect markerRect, SKColor color, string type, bool isOpen = false)
    {
        var snappedRect = markerRect;

        switch (type)
        {
            case "disc":
            {
                var op = PaintOpPool.GetDrawPathOp();
                var path = new SKPath();
                path.AddOval(snappedRect);
                op.Path.Dispose();
                op.Path = path;
                op.FillPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
                op.StrokePaint = null;
                op.Bounds = snappedRect;
                _displayList.Add(op);
                break;
            }
            case "circle":
            {
                var op = PaintOpPool.GetDrawPathOp();
                var path = new SKPath();
                path.AddOval(snappedRect);
                op.Path.Dispose();
                op.Path = path;
                op.StrokePaint = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f, IsAntialias = true };
                op.FillPaint = null;
                op.Bounds = snappedRect;
                _displayList.Add(op);
                break;
            }
            case "square":
            {
                var op = PaintOpPool.GetDrawRectOp();
                op.Rect = snappedRect;
                op.FillColor = color;
                op.Bounds = snappedRect;
                _displayList.Add(op);
                break;
            }
            case "disclosure-open":
            case "disclosure-closed":
            {
                var path = GetCanonicalDisclosurePath(style, type == "disclosure-open");
                var scaled = new SKPath();
                var matrix = SKMatrix.CreateScale(snappedRect.Width, snappedRect.Height);
                scaled.AddPath(path, ref matrix);
                scaled.Transform(SKMatrix.CreateTranslation(snappedRect.Left, snappedRect.Top));
                path.Dispose();

                var op = PaintOpPool.GetDrawPathOp();
                op.Path.Dispose();
                op.Path = scaled;
                op.FillPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
                op.StrokePaint = null;
                op.Bounds = snappedRect;
                _displayList.Add(op);
                break;
            }
        }
    }
}
