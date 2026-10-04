using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Paints the frame canvas behind the document. The frame owns the viewport
/// background while ViewPainter supplies CSS background propagation rules.
/// </summary>
internal sealed class FramePainter
{
    private readonly DisplayList _displayList;

    public FramePainter(DisplayList displayList)
    {
        _displayList = displayList;
    }

    public FrameBackground? PaintBackground(Document document, float viewportWidth,
        float viewportHeight, float contentOffsetY)
    {
        var source = BackgroundSource(document);
        var style = source?.ComputedStyle;
        if (source == null || style == null)
            return null;

        var canvasRect = CanvasRect(document, viewportWidth, viewportHeight, contentOffsetY);
        if (canvasRect.Width <= 0 || canvasRect.Height <= 0)
            return null;

        var baseColor = SKColors.White;
        var propagatedColor = style.BackgroundColor ?? SKColors.Transparent;
        var color = ViewPainter.Blend(baseColor, propagatedColor);
        if (color.Alpha > 0)
        {
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = canvasRect;
            op.FillColor = color;
            op.Bounds = canvasRect;
            op.ZIndex = int.MinValue;
            _displayList.Add(op);
        }

        return new FrameBackground(source, style, canvasRect);
    }

    private static Element? BackgroundSource(Document document)
    {
        var root = document.DocumentElement;
        if (root?.ComputedStyle == null)
            return null;
        if (ViewPainter.HasBackground(root.ComputedStyle))
            return root;

        var body = document.Body;
        if (body?.ComputedStyle != null && ViewPainter.BackgroundTransfersToView(body, document))
            return body;
        return root;
    }

    private static SKRect CanvasRect(Document document, float viewportWidth,
        float viewportHeight, float contentOffsetY)
    {
        var rootBox = document.DocumentElement?.LayoutBox;
        float documentRight = rootBox?.BorderBox.Right ?? 0;
        float documentBottom = rootBox?.BorderBox.Bottom ?? 0;
        float width = Math.Max(viewportWidth, documentRight);
        float height = Math.Max(viewportHeight, documentBottom);
        return new SKRect(0, contentOffsetY, width, contentOffsetY + height);
    }
}

internal sealed record FrameBackground(
    Element SourceElement,
    ComputedStyle Style,
    SKRect CanvasRect);
