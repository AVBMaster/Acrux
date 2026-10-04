using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Paints a scrollbar that is styled via the <c>::-webkit-scrollbar-*</c> pseudo
/// element family. Each part (<c>::-webkit-scrollbar</c> bar,
/// <c>::-webkit-scrollbar-track</c>, <c>::-webkit-scrollbar-thumb</c>,
/// <c>::-webkit-scrollbar-corner</c>) is painted independently, honoring the
/// background, border, border-radius and (for the bar) thickness collected in
/// <see cref="ScrollbarStyles"/>. A part is only painted when it actually has
/// styling (mirrors the reference behaviour where unstyled pseudo parts do not
/// exist and are therefore skipped).
///
/// Only the parts that carry explicit styles are drawn; no default skin is
/// synthesized here. The caller (<see cref="ScrollableAreaPainter"/>) routes to
/// this theme only when the element has at least one styled custom part, and
/// otherwise keeps the classic painter intact.
///
/// Geometry follows the reference algorithm: the thumb length is
/// <c>round(proportion * trackLength)</c> clamped to the track and to a minimum
/// thumb length, and the thumb position travels along the remaining track range
/// proportionally to the scroll offset.
/// </summary>
internal sealed class CustomScrollbarTheme
{
    private readonly DisplayList _displayList;

    /// <summary>
    /// Creates a custom scrollbar theme that emits display-list operations into
    /// <paramref name="displayList"/>.
    /// </summary>
    public CustomScrollbarTheme(DisplayList displayList)
    {
        _displayList = displayList;
    }

    /// <summary>
    /// Paints the custom scrollbar (bar, track, thumb and corner) for the given
    /// scroll container from its collected <see cref="ScrollbarStyles"/>. Assumes
    /// the element has at least one styled part (checked by the caller).
    /// </summary>
    public void Paint(LayoutBox box, ComputedStyle style, float contentOffsetY, bool canResize)
    {
        var custom = style.ScrollbarCustom;
        if (custom == null)
            return;

        // scrollbar-width: none — scrolling stays functional, bars disappear.
        float thickness = ScrollbarMetrics.ThicknessFor(style);
        if (thickness <= 0)
            return;

        var paddingBox = box.PaddingBox;
        bool hasVerticalOverflow = box.ScrollContentHeight > box.ContentBox.Height;
        bool hasHorizontalOverflow = box.ScrollContentWidth > box.ContentBox.Width;
        bool paintVertical = hasVerticalOverflow || ForcesVerticalScrollbar(style);
        bool paintHorizontal = hasHorizontalOverflow || ForcesHorizontalScrollbar(style);

        if (paintVertical)
            PaintVertical(box, custom, paddingBox, contentOffsetY, thickness, paintHorizontal);
        if (paintHorizontal)
            PaintHorizontal(box, custom, paddingBox, contentOffsetY, thickness, paintVertical);
        if (paintVertical && paintHorizontal)
            PaintCorner(custom, paddingBox, contentOffsetY, thickness);
        if (canResize && (paintVertical || paintHorizontal))
            PaintResizer(box, paddingBox, contentOffsetY, thickness);
    }

    private void PaintVertical(LayoutBox box, ScrollbarStyles custom, SKRect paddingBox,
        float contentOffsetY, float thickness, bool hasHorizontal)
    {
        float trackHeight = Math.Max(0, paddingBox.Height - (hasHorizontal ? thickness : 0));
        float x = paddingBox.Right - thickness;
        float y = paddingBox.Top + contentOffsetY;
        var barRect = new SKRect(x, y, x + thickness, y + trackHeight);

        if (custom.Bar.HasAny)
            AddPart(barRect, custom.Bar);

        if (custom.Track.HasAny)
            AddPart(InsetPx(barRect, custom.Track.BorderWidth), custom.Track);

        bool hasOverflow = box.ScrollContentHeight > box.ContentBox.Height;
        if (!hasOverflow || trackHeight <= 0 || !custom.Thumb.HasAny)
            return;

        float trackLen = trackHeight;
        float length = ThumbLength(box.ContentBox.Height, box.ScrollContentHeight, trackLen);
        float thumbPos = ThumbPosition(box.ScrollY, box.ContentBox.Height, box.ScrollContentHeight,
            trackLen, length);

        var thumbRect = new SKRect(x, y + thumbPos, x + thickness, y + thumbPos + length);
        AddPart(InsetPx(thumbRect, custom.Thumb.BorderWidth), custom.Thumb);
    }

    private void PaintHorizontal(LayoutBox box, ScrollbarStyles custom, SKRect paddingBox,
        float contentOffsetY, float thickness, bool hasVertical)
    {
        float trackWidth = Math.Max(0, paddingBox.Width - (hasVertical ? thickness : 0));
        float x = paddingBox.Left;
        float y = paddingBox.Bottom - thickness + contentOffsetY;
        var barRect = new SKRect(x, y, x + trackWidth, y + thickness);

        if (custom.Bar.HasAny)
            AddPart(barRect, custom.Bar);

        if (custom.Track.HasAny)
            AddPart(InsetPx(barRect, custom.Track.BorderWidth), custom.Track);

        bool hasOverflow = box.ScrollContentWidth > box.ContentBox.Width;
        if (!hasOverflow || trackWidth <= 0 || !custom.Thumb.HasAny)
            return;

        float trackLen = trackWidth;
        float length = ThumbLength(box.ContentBox.Width, box.ScrollContentWidth, trackLen);
        float thumbPos = ThumbPosition(box.ScrollX, box.ContentBox.Width, box.ScrollContentWidth,
            trackLen, length);

        var thumbRect = new SKRect(x + thumbPos, y, x + thumbPos + length, y + thickness);
        AddPart(InsetPx(thumbRect, custom.Thumb.BorderWidth), custom.Thumb);
    }

    private void PaintCorner(ScrollbarStyles custom, SKRect paddingBox, float contentOffsetY, float thickness)
    {
        var corner = new SKRect(
            paddingBox.Right - thickness,
            paddingBox.Bottom - thickness + contentOffsetY,
            paddingBox.Right,
            paddingBox.Bottom + contentOffsetY);

        if (custom.Corner.HasAny)
        {
            AddPart(corner, custom.Corner);
            return;
        }

        // Corner not styled but both axes present: fill it with the track (or bar)
        // background so no hole shows between the two scrollbars.
        var bg = custom.Track?.Background ?? custom.Bar?.Background;
        if (bg.HasValue)
            AddFill(corner, bg.Value);
    }

    private void PaintResizer(LayoutBox box, SKRect paddingBox, float contentOffsetY, float thickness)
    {
        // Mirror DrawPlatformResizerImage: two dark diagonal grip lines with
        // light counterparts offset one pixel below.
        float cornerSize = thickness;
        float cornerX = paddingBox.Right - cornerSize;
        float cornerY = paddingBox.Bottom - cornerSize + contentOffsetY;

        float edge = 3;
        float midX = cornerX + cornerSize / 2;
        float midY = cornerY + cornerSize / 2;
        float bottomRightX = cornerX + cornerSize - edge;
        float bottomRightY = cornerY + cornerSize - edge;

        AddLine(midX, cornerY + edge, bottomRightX, midY, new SKColor(0x00, 0x00, 0x00, 0x99));
        AddLine(cornerX + edge, bottomRightY, bottomRightX, cornerY + cornerSize - edge, new SKColor(0x00, 0x00, 0x00, 0x99));
    }

    /// <summary>
    /// Reference thumb-length math: <c>round(visible/total * trackLength)</c>
    /// clamped to the configured minimum thumb length and capped at the track
    /// length so the thumb never overflows the track.
    /// </summary>
    private static float ThumbLength(float visible, float total, float trackLen)
    {
        float proportion = visible / Math.Max(1f, total);
        float length = MathF.Round(proportion * trackLen);
        length = Math.Max(length, ScrollbarMetrics.MinThumbLength);
        if (length > trackLen)
            length = trackLen;
        return length;
    }

    /// <summary>
    /// Reference thumb-position math: the thumb travels over the leftover track
    /// range <c>(trackLen - length)</c> proportionally to the scroll offset over
    /// the scrollable range.
    /// </summary>
    private static float ThumbPosition(float scrollOffset, float visible, float total, float trackLen, float length)
    {
        float scrollRange = Math.Max(1f, total - visible);
        float position = Math.Clamp(scrollOffset, 0f, scrollRange);
        return (trackLen - length) * (position / scrollRange);
    }

    /// <summary>Emits a part that carries a nontrivial background / border / radius.</summary>
    private void AddPart(SKRect rect, ScrollbarPartStyle part)
    {
        bool hasBackground = part.Background.HasValue && part.Background.Value.Alpha > 0;
        bool hasBorder = part.BorderWidth > 0 && part.BorderColor.HasValue && part.BorderColor.Value.Alpha > 0;
        if (!hasBackground && !hasBorder && part.BorderRadius <= 0)
            return;

        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = rect;
        op.BorderRadius = Math.Max(0f, part.BorderRadius);
        if (hasBackground)
            op.FillColor = part.Background!.Value;
        if (hasBorder)
        {
            var color = part.BorderColor!.Value;
            op.BorderTopWidth = op.BorderRightWidth = op.BorderBottomWidth = op.BorderLeftWidth = part.BorderWidth;
            op.BorderTopColor = op.BorderRightColor = op.BorderBottomColor = op.BorderLeftColor = color;
            op.BorderTopStyle = op.BorderRightStyle = op.BorderBottomStyle = op.BorderLeftStyle = BorderStyle.Solid;
        }
        op.Bounds = rect;
        _displayList.Add(op);
    }

    private void AddFill(SKRect rect, SKColor color)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = rect;
        op.FillColor = color;
        op.Bounds = rect;
        _displayList.Add(op);
    }

    private void AddLine(float x1, float y1, float x2, float y2, SKColor color)
    {
        var op = PaintOpPool.GetDrawLineOp();
        op.X1 = x1;
        op.Y1 = y1;
        op.X2 = x2;
        op.Y2 = y2;
        op.StrokeWidth = 1;
        op.Color = color;
        op.Bounds = new SKRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(x1, x2));
        _displayList.Add(op);
    }

    private static SKRect InsetPx(SKRect rect, float px) =>
        px <= 0 ? rect : new SKRect(rect.Left + px, rect.Top + px, rect.Right - px, rect.Bottom - px);

    private static bool ForcesVerticalScrollbar(ComputedStyle style) =>
        style.OverflowY == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;

    private static bool ForcesHorizontalScrollbar(ComputedStyle style) =>
        style.OverflowX == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
}
