using SkiaSharp;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Rendering;

/// <summary>
/// 2D offset (point/vector) in line-relative space, i.e. the physical space
/// rotated for vertical 'writing-mode'. Mirrors LineRelativeOffset. Uses float coordinates
/// (Acrux has no LayoutUnit).
/// </summary>
public readonly struct LineRelativeOffset
{
    public float LineLeft { get; }
    public float LineOver { get; }

    public LineRelativeOffset(float lineLeft, float lineOver)
    {
        LineLeft = lineLeft;
        LineOver = lineOver;
    }

    /// <summary>
    /// Map a physical offset of a line box to line-relative space by reusing the
    /// offset coordinates (physical top-left). The line box origin is the same in
    /// both spaces regardless of the writing flow.
    /// </summary>
    public static LineRelativeOffset CreateFromBoxOrigin(PhysicalOffset origin) => new(origin.Left, origin.Top);

    public static implicit operator SKPoint(LineRelativeOffset o) => new(o.LineLeft, o.LineOver);
    public LineRelativeOffset Add(LineRelativeOffset other) => new(LineLeft + other.LineLeft, LineOver + other.LineOver);
    public static LineRelativeOffset operator +(LineRelativeOffset a, LineRelativeOffset b) => a.Add(b);

    public SKPoint ToRoundedPoint() => new(MathF.Round(LineLeft), MathF.Round(LineOver));
}

/// <summary>
/// 2D rect in line-relative space (physical space rotated for 'writing-mode').
/// Mirrors LineRelativeRect. Uses float coordinates.
/// </summary>
public readonly struct LineRelativeRect
{
    public LineRelativeOffset Offset { get; }
    public LogicalSize Size { get; }

    public LineRelativeRect(LineRelativeOffset offset, LogicalSize size)
    {
        Offset = offset;
        Size = size;
    }

    /// <summary>
    /// Build a rect from a bounding SKRectF, flooring the offset and ceilling the
    /// opposite edges (LayoutUnit::FromFloatFloor/Ceil).
    /// </summary>
    public static LineRelativeRect EnclosingRect(SKRect rect)
    {
        float left = MathF.Floor(rect.Left);
        float top = MathF.Floor(rect.Top);
        float inline = MathF.Ceiling(rect.Right) - left;
        float block = MathF.Ceiling(rect.Bottom) - top;
        return new LineRelativeRect(new LineRelativeOffset(left, top), new LogicalSize(inline, block));
    }

    /// <summary>
    /// Map the physical rect of a line box to line-relative space by reusing the
    /// offset coordinates and (if not horizontal) swapping width and height.
    /// </summary>
    public static LineRelativeRect CreateFromLineBox(PhysicalRect rect, bool isHorizontal) =>
        new(LineRelativeOffset.CreateFromBoxOrigin(rect.Offset),
            new LogicalSize(isHorizontal ? rect.Width : rect.Height, isHorizontal ? rect.Height : rect.Width));

    /// <summary>
    /// Map a physical rect that may be a line box or a contained text fragment to
    /// line-relative space, by mapping it through the inverse of the given
    /// rotation matrix. A null/identity rotation means no-op.
    /// </summary>
    public static LineRelativeRect Create(PhysicalRect rect, SKMatrix? rotation)
    {
        if (!rotation.HasValue || rotation.Value == SKMatrix.Identity)
            return new LineRelativeRect(new LineRelativeOffset(rect.X, rect.Y), new LogicalSize(rect.Width, rect.Height));
        if (!rotation.Value.TryInvert(out var inverse))
            inverse = SKMatrix.Identity;
        return EnclosingRect(inverse.MapRect(rect.ToSKRect()));
    }

    public static implicit operator SKRect(LineRelativeRect r) =>
        new(r.Offset.LineLeft, r.Offset.LineOver, r.Offset.LineLeft + r.Size.InlineSize, r.Offset.LineOver + r.Size.BlockSize);

    public LineRelativeRect Add(LineRelativeOffset other) => new(Offset + other, Size);
    public static LineRelativeRect operator +(LineRelativeRect r, LineRelativeOffset o) => r.Add(o);

    public float LineLeft => Offset.LineLeft;
    public float LineOver => Offset.LineOver;
    public float InlineSize => Size.InlineSize;
    public float BlockSize => Size.BlockSize;

    public void Deconstruct(out LineRelativeOffset offset, out LogicalSize size)
    {
        offset = Offset;
        size = Size;
    }

    public LineRelativeRect Move(LineRelativeOffset other) => new(Offset + other, Size);

    public SKPoint PixelSnappedOffset => Offset.ToRoundedPoint();
    public int PixelSnappedInlineSize => SnapSizeToPixel(Size.InlineSize, Offset.LineLeft);
    public int PixelSnappedBlockSize => SnapSizeToPixel(Size.BlockSize, Offset.LineOver);
    public SKSize PixelSnappedSize => new(PixelSnappedInlineSize, PixelSnappedBlockSize);
    public SKRect ToPixelSnappedRect => new(PixelSnappedOffset.X, PixelSnappedOffset.Y, PixelSnappedOffset.X + PixelSnappedSize.Width, PixelSnappedOffset.Y + PixelSnappedSize.Height);

    private static int SnapSizeToPixel(float size, float offset)
    {
        float snappedOffset = MathF.Round(offset);
        return (int)(MathF.Round(offset + size) - snappedOffset);
    }

    /// <summary>
    /// Transform that rotates the canvas in the appropriate direction for a
    /// vertical writing mode while keeping the physical top-left corner of the
    /// line box at the same place. Mirrors ComputeRelativeToPhysicalTransform.
    /// Returns identity for horizontal writing modes.
    /// </summary>
    public SKMatrix ComputeRelativeToPhysicalTransform(WritingMode writingMode)
    {
        if (writingMode == WritingMode.HorizontalTb)
            return SKMatrix.Identity;

        // VerticalRl / VerticalLr / (SidewaysRl): rotation [0 -1; 1 0]
        //   scaleX=0 skewX=-1 skewY=1 scaleY=0 tx=... ty=...
        // SidewaysLr: rotation [0 1; -1 0]
        bool sidewaysLr = writingMode == WritingMode.SidewaysLr;
        float a = 0, b = 1, c = -1, d = 0, e, f;
        if (sidewaysLr)
        {
            a = 0; b = -1; c = 1; d = 0;
            e = LineLeft - LineOver;
            f = LineLeft + LineOver + InlineSize;
        }
        else
        {
            e = LineLeft + LineOver + BlockSize;
            f = LineOver - LineLeft;
        }
        return new SKMatrix
        {
            ScaleX = a, SkewY = b, SkewX = c, ScaleY = d,
            Persp0 = 0, Persp1 = 0, Persp2 = 1,
            TransX = e, TransY = f,
        };
    }

    public LineRelativeRect EnclosingLineRelativeRect()
    {
        int left = (int)MathF.Floor(Offset.LineLeft);
        int top = (int)MathF.Floor(Offset.LineOver);
        int right = (int)MathF.Ceiling(Offset.LineLeft + Size.InlineSize);
        int bottom = (int)MathF.Ceiling(Offset.LineOver + Size.BlockSize);
        return new LineRelativeRect(new LineRelativeOffset(left, top), new LogicalSize(right - left, bottom - top));
    }

    /// <summary>Shift start edges by -d and end edges by +d in both axes.</summary>
    public LineRelativeRect Inflate(float d) => new(
        new LineRelativeOffset(Offset.LineLeft - d, Offset.LineOver - d),
        new LogicalSize(Size.InlineSize + d * 2, Size.BlockSize + d * 2));

    /// <summary>Union-ish: even if either rect is empty, spans both (PhysicalRect::UniteEvenIfEmpty).</summary>
    public LineRelativeRect Unite(LineRelativeRect other)
    {
        float left = MathF.Min(Offset.LineLeft, other.Offset.LineLeft);
        float top = MathF.Min(Offset.LineOver, other.Offset.LineOver);
        float right = MathF.Max(Offset.LineLeft + Size.InlineSize, other.Offset.LineLeft + other.Size.InlineSize);
        float bottom = MathF.Max(Offset.LineOver + Size.BlockSize, other.Offset.LineOver + other.Size.BlockSize);
        return new LineRelativeRect(new LineRelativeOffset(left, top), new LogicalSize(right - left, bottom - top));
    }

    public override string ToString() =>
        $"({LineLeft:F1},{LineOver:F1} {InlineSize:F1}x{BlockSize:F1})";
}

/// <summary>
/// Extension helpers on WritingMode, mirroring the subset used by
/// line_relative_rect.cc.
/// </summary>
public static class LineRelativeRectExtensions
{
    public static float LineStartInkOverflow(this PhysicalRect inkOverflow, WritingMode writingMode) =>
        writingMode switch
        {
            WritingMode.HorizontalTb => inkOverflow.X,
            WritingMode.SidewaysLr => -inkOverflow.Bottom,
            _ => inkOverflow.Y,
        };

    public static float LineEndInkOverflow(this PhysicalRect inkOverflow, float fragmentHeight, WritingMode writingMode) =>
        writingMode switch
        {
            WritingMode.HorizontalTb => inkOverflow.Right,
            WritingMode.SidewaysLr => fragmentHeight - inkOverflow.Y,
            _ => inkOverflow.Bottom,
        };
}