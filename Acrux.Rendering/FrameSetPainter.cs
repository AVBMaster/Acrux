using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Paints the frameset chrome: child frames plus the resize borders between
/// them. Mirrors FrameSetPainter.
///
/// DrawingRecorder / GraphicsContext are mapped to the existing paint-op
/// layer: the caller provides the collection the painter appends DrawRectOps
/// to, and the SKCanvas against which pixel snapping is performed.
/// </summary>
public sealed class FrameSetPainter
{
    private const uint kBorderStartEdgeColor = 0xFFAAAAAA;
    private const uint kBorderEndEdgeColor = 0xFF000000;
    private const uint kBorderFillColor = 0xFFD0D0D0;

    private readonly LayoutBox _layoutBox;
    private readonly FrameSetLayoutData _layoutData;

    public FrameSetPainter(LayoutBox layoutBox, FrameSetLayoutData layoutData)
    {
        _layoutBox = layoutBox;
        _layoutData = layoutData;
    }

    /// <summary>Paints the frameset. Appends fill ops to <paramref name="sink"/>.</summary>
    public void PaintObject(List<DrawRectOp> sink, SKCanvas canvas, float paintOffsetX, float paintOffsetY, int childCount = -1)
    {
        int actualChildCount = childCount >= 0 ? childCount : _layoutData.ColSizes.Sum(_ => _ <= 0 ? 0 : 1) * _layoutData.RowSizes.Sum(_ => _ <= 0 ? 0 : 1);
        if (actualChildCount == 0)
            return;

        if (_layoutBox.Dimensions?.Style?.Visibility != VisibilityType.Visible)
            return;

        PaintBorders(sink, canvas, paintOffsetX, paintOffsetY);
    }

    /// <summary>
    /// Appends the resize-border rects to <paramref name="sink"/> following the
    /// row/column grid. Mirrors PaintBorders().
    /// </summary>
    public void PaintBorders(List<DrawRectOp> sink, SKCanvas canvas, float paintOffsetX, float paintOffsetY)
    {
        float borderThickness = _layoutData.BorderThickness;
        if (borderThickness <= 0)
            return;

        var style = _layoutBox.Dimensions?.Style;
        uint borderFillColor = _layoutData.HasBorderColor
            ? (uint)(((uint)style?.BorderLeftColor.Red << 16) | ((uint)style?.BorderLeftColor.Green << 8) | (uint)style?.BorderLeftColor.Blue)
            : kBorderFillColor;

        var rowSizes = _layoutData.RowSizes;
        var colSizes = _layoutData.ColSizes;
        var boxSize = _layoutBox.BorderBox;
        float width = boxSize.Width;
        float height = boxSize.Height;

        float y = 0;
        int childrenCount = rowSizes.Count * colSizes.Count;
        for (int row = 0; row < rowSizes.Count; row++)
        {
            float x = 0;
            for (int col = 0; col < colSizes.Count; col++)
            {
                x += colSizes[col];
                if (ShouldPaintBorderAfter(_layoutData.ColAllowBorder, col))
                {
                    var rect = ToPixelSnappedRect(new SKRect(
                        paintOffsetX + x, paintOffsetY + y,
                        paintOffsetX + x + borderThickness, paintOffsetY + y + height - y));
                    PaintColumnBorder(sink, canvas, rect, borderFillColor);
                    x += borderThickness;
                }
                if (--childrenCount == 0)
                    return;
            }
            y += rowSizes[row];
            if (ShouldPaintBorderAfter(_layoutData.RowAllowBorder, row))
            {
                var rect = ToPixelSnappedRect(new SKRect(
                    paintOffsetX, paintOffsetY + y,
                    paintOffsetX + width, paintOffsetY + y + borderThickness));
                PaintRowBorder(sink, canvas, rect, borderFillColor);
                y += borderThickness;
            }
        }
    }

    private static bool ShouldPaintBorderAfter(List<bool> allowBorder, int index)
    {
        // Should not paint a border after the last frame along the axis.
        return index + 1 < allowBorder.Count - 1 && allowBorder[index + 1];
    }

    private void PaintRowBorder(List<DrawRectOp> sink, SKCanvas canvas, SKRect borderRect, uint fillColor)
    {
        // Fill first.
        PushFill(sink, borderRect, fillColor);

        // Stroke the edges but only if there is room to paint both with a little
        // of the fill color showing through.
        if (borderRect.Height < 3)
            return;
        PushFill(sink, new SKRect(borderRect.Left, borderRect.Top, borderRect.Right, borderRect.Top + 1), kBorderStartEdgeColor);
        PushFill(sink, new SKRect(borderRect.Left, borderRect.Bottom - 1, borderRect.Right, borderRect.Bottom), kBorderEndEdgeColor);
    }

    private void PaintColumnBorder(List<DrawRectOp> sink, SKCanvas canvas, SKRect borderRect, uint fillColor)
    {
        // Fill first.
        PushFill(sink, borderRect, fillColor);

        // Stroke the edges but only if there is room to paint both with a little
        // of the fill color showing through.
        if (borderRect.Width < 3)
            return;
        PushFill(sink, new SKRect(borderRect.Left, borderRect.Top, borderRect.Left + 1, borderRect.Bottom), kBorderStartEdgeColor);
        PushFill(sink, new SKRect(borderRect.Right - 1, borderRect.Top, borderRect.Right, borderRect.Bottom), kBorderEndEdgeColor);
    }

    private static void PushFill(List<DrawRectOp> sink, SKRect rect, uint rgb)
    {
        sink.Add(new DrawRectOp
        {
            Rect = rect,
            FillColor = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255),
        });
    }

    private static SKRect ToPixelSnappedRect(SKRect r) =>
        new(MathF.Floor(r.Left), MathF.Floor(r.Top), MathF.Ceiling(r.Right), MathF.Ceiling(r.Bottom));
}