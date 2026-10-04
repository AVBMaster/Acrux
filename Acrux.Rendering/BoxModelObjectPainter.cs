using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Rendering;

/// <summary>
/// Paints either a LayoutBox or a LayoutInline, allowing code sharing between
/// block and inline-block painting. Mirrors BoxModelObjectPainter, backing
/// onto the existing <see cref="BoxPainterBase"/>.
/// </summary>
public sealed class BoxModelObjectPainter
{
    private readonly BoxPainterBase _boxPainter;

    public BoxModelObjectPainter(DisplayList displayList)
    {
        _boxPainter = new BoxPainterBase(displayList);
    }

    /// <summary>The enclosed shared box painter.</summary>
    public BoxPainterBase BoxPainter => _boxPainter;

    /// <summary>
    /// Adjusts a paint rect to reflect a scrolled content box with borders at the
    /// ends, and clips to the overflow area. Mirrors AdjustRectForScrolledContent().
    /// </summary>
    public PhysicalRect AdjustRectForScrolledContent(LayoutBox box, PhysicalBoxStrut border, PhysicalRect rect)
    {
        var scrolled = new PhysicalRect(
            rect.Offset.Left - box.ScrollX,
            rect.Offset.Top - box.ScrollY,
            border.Left + border.Right + box.ScrollContentWidth,
            border.Top + box.ScrollContentHeight + border.Bottom);
        return scrolled;
    }

    /// <summary>Same as <see cref="AdjustRectForScrolledContent"/> but also records an overflow clip.</summary>
    public PhysicalRect AdjustRectForScrolledContent(LayoutBox box, PhysicalBoxStrut border, PhysicalRect rect,
        List<SKRect> clipSink)
    {
        if (clipSink != null)
        {
            clipSink.Add(new SKRect(rect.Offset.Left, rect.Offset.Top,
                rect.Offset.Left + box.ScrollContentWidth + border.Left + border.Right,
                rect.Offset.Top + box.ScrollContentHeight + border.Top + border.Bottom));
        }
        return AdjustRectForScrolledContent(box, border, rect);
    }
}