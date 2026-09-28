using SkiaSharp;

namespace UpBrowser.Core.Layout.Geometry;

public readonly struct LogicalOffset
{
    public float InlineOffset { get; }
    public float BlockOffset { get; }
    public LogicalOffset(float inlineOffset, float blockOffset) { InlineOffset = inlineOffset; BlockOffset = blockOffset; }
    public static readonly LogicalOffset Zero = new(0, 0);
    public static LogicalOffset operator +(LogicalOffset a, LogicalOffset b) => new(a.InlineOffset + b.InlineOffset, a.BlockOffset + b.BlockOffset);
    public static LogicalOffset operator -(LogicalOffset a, LogicalOffset b) => new(a.InlineOffset - b.InlineOffset, a.BlockOffset - b.BlockOffset);

    /// <summary>Convert a logical offset to physical, using the writing direction.</summary>
    public PhysicalOffset ConvertToPhysical(WritingDirectionMode writingDirection, float containerInlineSize = 0)
    {
        if (writingDirection.IsHorizontal)
        {
            var p = new PhysicalOffset(
                writingDirection.Direction == TextDirection.Rtl ? containerInlineSize - InlineOffset : InlineOffset,
                BlockOffset);
            // RTL mirrors the inline axis about the container.
            return p;
        }
        // Vertical writing mode: inline axis maps to y, block axis maps to x.
        var q = new PhysicalOffset(BlockOffset, InlineOffset);
        return q;
    }

    /// <summary>Convert a physical offset to logical, using the writing direction.</summary>
    public static LogicalOffset ConvertToLogical(PhysicalOffset offset, WritingDirectionMode writingDirection, float containerInlineSize = 0)
    {
        if (writingDirection.IsHorizontal)
        {
            float inline = writingDirection.Direction == TextDirection.Rtl ? containerInlineSize - offset.Left : offset.Left;
            return new LogicalOffset(inline, offset.Top);
        }
        return new LogicalOffset(offset.Top, offset.Left);
    }
}

public readonly struct LogicalSize
{
    public float InlineSize { get; }
    public float BlockSize { get; }
    public LogicalSize(float inlineSize, float blockSize) { InlineSize = inlineSize; BlockSize = blockSize; }
    public bool IsEmpty => InlineSize <= 0 || BlockSize <= 0;
    public static readonly LogicalSize Zero = new(0, 0);
}

public readonly struct LogicalRect
{
    public float InlineStart { get; } public float BlockStart { get; }
    public float InlineSize { get; } public float BlockSize { get; }
    public LogicalRect(float inlineStart, float blockStart, float inlineSize, float blockSize)
    { InlineStart = inlineStart; BlockStart = blockStart; InlineSize = inlineSize; BlockSize = blockSize; }
    public LogicalRect(LogicalOffset offset, LogicalSize size) : this(offset.InlineOffset, offset.BlockOffset, size.InlineSize, size.BlockSize) { }
    public LogicalOffset Offset => new(InlineStart, BlockStart);
    public LogicalSize Size => new(InlineSize, BlockSize);
    public float InlineEnd => InlineStart + InlineSize;
    public float BlockEnd => BlockStart + BlockSize;
    public static readonly LogicalRect Zero = new(0, 0, 0, 0);
}

public readonly struct PhysicalSize
{
    public float Width { get; } public float Height { get; }
    public PhysicalSize(float width, float height) { Width = width; Height = height; }
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public static readonly PhysicalSize Zero = new(0, 0);
    public SKSize ToSKSize() => new(Width, Height);
}

public readonly struct PhysicalOffset
{
    public float Left { get; } public float Top { get; }
    public PhysicalOffset(float left, float top) { Left = left; Top = top; }
    public static readonly PhysicalOffset Zero = new(0, 0);
    public static PhysicalOffset operator +(PhysicalOffset a, PhysicalOffset b) => new(a.Left + b.Left, a.Top + b.Top);
    public static PhysicalOffset operator -(PhysicalOffset a, PhysicalOffset b) => new(a.Left - b.Left, a.Top - b.Top);
    public SKPoint ToSKPoint() => new(Left, Top);
}

public readonly struct PhysicalRect
{
    public float X { get; } public float Y { get; }
    public float Width { get; } public float Height { get; }
    public PhysicalRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    public PhysicalRect(PhysicalOffset offset, PhysicalSize size) : this(offset.Left, offset.Top, size.Width, size.Height) { }
    public PhysicalOffset Offset => new(X, Y);
    public PhysicalSize Size => new(Width, Height);
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public static readonly PhysicalRect Zero = new(0, 0, 0, 0);
    public bool Intersects(PhysicalRect other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
    public PhysicalRect Intersect(PhysicalRect other)
    {
        float x = Math.Max(X, other.X), y = Math.Max(Y, other.Y);
        float r = Math.Min(Right, other.Right), b = Math.Min(Bottom, other.Bottom);
        return x >= r || y >= b ? Zero : new PhysicalRect(x, y, r - x, b - y);
    }
    public PhysicalRect Union(PhysicalRect other)
    {
        float x = Math.Min(X, other.X), y = Math.Min(Y, other.Y);
        return new PhysicalRect(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }
    public SKRect ToSKRect() => new(X, Y, X + Width, Y + Height);
}

public readonly struct BfcOffset
{
    public float LineOffset { get; } public float BlockOffset { get; }
    public BfcOffset(float lineOffset, float blockOffset) { LineOffset = lineOffset; BlockOffset = blockOffset; }
    public static readonly BfcOffset Zero = new(0, 0);

    public static BfcOffset operator +(BfcOffset a, BfcDelta d) => new(a.LineOffset + d.LineOffsetDelta, a.BlockOffset + d.BlockOffsetDelta);
    public static bool operator ==(BfcOffset a, BfcOffset b) => a.LineOffset == b.LineOffset && a.BlockOffset == b.BlockOffset;
    public static bool operator !=(BfcOffset a, BfcOffset b) => !(a == b);
    public override bool Equals(object? obj) => obj is BfcOffset o && this == o;
    public override int GetHashCode() => HashCode.Combine(LineOffset, BlockOffset);
}

/// <summary>A delta applied to a <see cref="BfcOffset"/>.</summary>
public readonly struct BfcDelta
{
    public float LineOffsetDelta { get; } public float BlockOffsetDelta { get; }
    public BfcDelta(float lineOffsetDelta, float blockOffsetDelta) { LineOffsetDelta = lineOffsetDelta; BlockOffsetDelta = blockOffsetDelta; }
    public static readonly BfcDelta Zero = new(0, 0);
}

/// <summary>
/// Position and size of a rect (typically a fragment) relative to a block
/// formatting context. Mirrors BfcRect in bfc_rect.h.
/// </summary>
public readonly struct BfcRect
{
    public BfcOffset StartOffset { get; }
    public BfcOffset EndOffset { get; }

    public BfcRect(BfcOffset startOffset, BfcOffset endOffset)
    {
        StartOffset = startOffset;
        EndOffset = endOffset;
    }

    public float LineStartOffset => StartOffset.LineOffset;
    public float LineEndOffset => EndOffset.LineOffset;
    public float BlockStartOffset => StartOffset.BlockOffset;
    public float BlockEndOffset => EndOffset.BlockOffset;

    public float BlockSize =>
        EndOffset.BlockOffset == float.MaxValue ? float.MaxValue : EndOffset.BlockOffset - StartOffset.BlockOffset;

    public float InlineSize =>
        EndOffset.LineOffset == float.MaxValue
            ? (StartOffset.LineOffset == float.MaxValue ? 0 : float.MaxValue)
            : EndOffset.LineOffset - StartOffset.LineOffset;

    public static BfcRect operator +(BfcRect r, BfcDelta d) =>
        new(r.StartOffset + d, r.EndOffset + d);

    public static bool operator ==(BfcRect a, BfcRect b) => a.StartOffset == b.StartOffset && a.EndOffset == b.EndOffset;
    public static bool operator !=(BfcRect a, BfcRect b) => !(a == b);
    public override bool Equals(object? obj) => obj is BfcRect r && this == r;
    public override int GetHashCode() => HashCode.Combine(StartOffset, EndOffset);
}

/// <summary>
/// Position of a flex item relative to its flex container in the main/cross
/// axis. Mirrors FlexOffset in flex_offset.h.
/// </summary>
public readonly struct FlexOffset
{
    public float MainAxisOffset { get; }
    public float CrossAxisOffset { get; }

    public FlexOffset(float mainAxisOffset, float crossAxisOffset)
    {
        MainAxisOffset = mainAxisOffset;
        CrossAxisOffset = crossAxisOffset;
    }

    public static readonly FlexOffset Zero = new(0, 0);

    public static FlexOffset operator +(FlexOffset a, FlexOffset b) =>
        new(a.MainAxisOffset + b.MainAxisOffset, a.CrossAxisOffset + b.CrossAxisOffset);
    public static FlexOffset operator -(FlexOffset a, FlexOffset b) =>
        new(a.MainAxisOffset - b.MainAxisOffset, a.CrossAxisOffset - b.CrossAxisOffset);
    public static bool operator ==(FlexOffset a, FlexOffset b) =>
        a.MainAxisOffset == b.MainAxisOffset && a.CrossAxisOffset == b.CrossAxisOffset;
    public static bool operator !=(FlexOffset a, FlexOffset b) => !(a == b);

    public override bool Equals(object? obj) => obj is FlexOffset o && this == o;
    public override int GetHashCode() => HashCode.Combine(MainAxisOffset, CrossAxisOffset);

    public FlexOffset TransposedOffset() => new(CrossAxisOffset, MainAxisOffset);
}

/// <summary>
/// Represents a range of scroll translation offsets in physical axes.
/// A missing value means an unbounded range in that direction.
/// Mirrors PhysicalScrollRange in scroll_offset_range.h.
/// </summary>
public readonly struct PhysicalScrollRange
{
    public float? XMin { get; }
    public float? XMax { get; }
    public float? YMin { get; }
    public float? YMax { get; }

    public PhysicalScrollRange(float? xMin, float? xMax, float? yMin, float? yMax)
    {
        XMin = xMin; XMax = xMax; YMin = yMin; YMax = yMax;
    }

    public bool Contains(float x, float y) =>
        (!XMin.HasValue || x >= XMin.Value) && (!XMax.HasValue || x <= XMax.Value) &&
        (!YMin.HasValue || y >= YMin.Value) && (!YMax.HasValue || y <= YMax.Value);
}

/// <summary>
/// Represents a range of scroll translation offsets in logical axes.
/// Mirrors LogicalScrollRange in scroll_offset_range.h.
/// </summary>
public readonly struct LogicalScrollRange
{
    public float? InlineMin { get; }
    public float? InlineMax { get; }
    public float? BlockMin { get; }
    public float? BlockMax { get; }

    public LogicalScrollRange(float? inlineMin, float? inlineMax, float? blockMin, float? blockMax)
    {
        InlineMin = inlineMin; InlineMax = inlineMax; BlockMin = blockMin; BlockMax = blockMax;
    }

    public PhysicalScrollRange ToPhysical(WritingDirectionMode mode)
    {
        if (mode.Direction == TextDirection.Ltr && mode.WritingMode == WritingMode.HorizontalTb)
            return new PhysicalScrollRange(InlineMin, InlineMax, BlockMin, BlockMax);
        return SlowToPhysical(mode);
    }

    private PhysicalScrollRange SlowToPhysical(WritingDirectionMode mode)
    {
        // Simplified: horizontal-tb RTL swaps inline min/max.
        var p = new PhysicalScrollRange(
            mode.WritingMode == WritingMode.HorizontalTb ? InlineMin : BlockMin,
            mode.WritingMode == WritingMode.HorizontalTb ? InlineMax : BlockMax,
            mode.WritingMode == WritingMode.HorizontalTb ? BlockMin : InlineMin,
            mode.WritingMode == WritingMode.HorizontalTb ? BlockMax : InlineMax);
        // RTL: reverse x-axis bounds.
        if (mode.Direction == TextDirection.Rtl && mode.WritingMode == WritingMode.HorizontalTb)
            return new PhysicalScrollRange(
                p.XMax.HasValue ? -p.XMax.Value : null,
                p.XMin.HasValue ? -p.XMin.Value : null,
                p.YMin, p.YMax);
        return p;
    }
}

/// <summary>
/// Margin collapsing accumulator. Mirrors MarginStrut in
/// geometry/margin_strut.h. Mutable struct (margin struts accumulate during
/// layout).
/// </summary>
public struct MarginStrut
{
    public float positive_margin;
    public float negative_margin;

    // Store quirky margins separately. Quirky containers need to ignore quirky
    // end margins. Quirky margins are always default margins, which are always
    // positive.
    public float quirky_positive_margin;

    // If this flag is set, we only Append non-quirky margins to this strut.
    // See the comment inside BlockLayoutAlgorithm for when this occurs.
    public bool is_quirky_container_start;

    // If set, we will discard all adjoining margins.
    public bool discard_margins;

    public float PositiveMargin => positive_margin;
    public float NegativeMargin => negative_margin;

    public MarginStrut(float positive = 0, float negative = 0)
    {
        positive_margin = positive;
        negative_margin = negative;
        quirky_positive_margin = 0;
        is_quirky_container_start = false;
        discard_margins = false;
    }

    /// <summary>
    /// Appends a margin value using the collapsed (max) semantics of
    /// MarginStrut::Append. Mirrors the two-argument Append in margin_strut.cc.
    /// </summary>
    public void Append(float value, bool is_quirky)
    {
        if (value < 0)
        {
            // CSS 2.1 §8.3.1: adjoining negative margins collapse to the most
            // negative one, they do not add up.
            negative_margin = Math.Min(negative_margin, value);
        }
        else if (is_quirky && is_quirky_container_start)
        {
            // Quirky margins for a container are ignored.
        }
        else if (is_quirky)
        {
            quirky_positive_margin = Math.Max(quirky_positive_margin, value);
        }
        else
        {
            positive_margin = Math.Max(positive_margin, value);
        }
    }

    /// <summary>
    /// Legacy additive append, kept for callers that relied on the previous
    /// simple implementation (e.g. ColumnLayoutAlgorithm).
    /// </summary>
    public MarginStrut Append(float margin)
    {
        if (margin >= 0)
            positive_margin += margin;
        else
            negative_margin += margin;
        return this;
    }

    /// <summary>Sum up negative and positive margins of this strut.</summary>
    public float Sum
    {
        get
        {
            if (discard_margins)
                return 0;
            return Math.Max(quirky_positive_margin, positive_margin) + negative_margin;
        }
    }

    /// <summary>Sum up the margins of this strut without quirky handling.</summary>
    public float QuirkyContainerSum()
    {
        if (discard_margins)
            return 0;
        return positive_margin + negative_margin;
    }

    /// <summary>Whether there have been no margins appended to this margin strut.</summary>
    public bool IsEmpty()
    {
        return positive_margin == 0 && negative_margin == 0 && quirky_positive_margin == 0;
    }

    public bool Equals(MarginStrut other) =>
        positive_margin == other.positive_margin && negative_margin == other.negative_margin &&
        quirky_positive_margin == other.quirky_positive_margin &&
        is_quirky_container_start == other.is_quirky_container_start && discard_margins == other.discard_margins;

    public static bool operator ==(MarginStrut a, MarginStrut b) => a.Equals(b);
    public static bool operator !=(MarginStrut a, MarginStrut b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is MarginStrut other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = positive_margin.GetHashCode();
            hash = (hash * 397) ^ negative_margin.GetHashCode();
            hash = (hash * 397) ^ quirky_positive_margin.GetHashCode();
            hash = (hash * 397) ^ is_quirky_container_start.GetHashCode();
            hash = (hash * 397) ^ discard_margins.GetHashCode();
            return hash;
        }
    }

    public static readonly MarginStrut Zero = new(0, 0);
}

public readonly struct MinMaxSizes
{
    public float MinSize { get; } public float MaxSize { get; }
    public MinMaxSizes(float minSize = 0, float maxSize = float.MaxValue) { MinSize = minSize; MaxSize = maxSize; }
    public bool IsEmpty => MaxSize < MinSize;
    public float ShrinkToFit(float availableSize) => Math.Clamp(availableSize, MinSize, MaxSize);
    public static readonly MinMaxSizes Zero = new(0, 0);
    public static readonly MinMaxSizes Unlimited = new(0, float.MaxValue);
}

public readonly struct BoxStrut
{
    public float Top { get; } public float Right { get; } public float Bottom { get; } public float Left { get; }
    public BoxStrut(float top, float right, float bottom, float left) { Top = top; Right = right; Bottom = bottom; Left = left; }
    public float HorizontalSum => Left + Right;
    public float VerticalSum => Top + Bottom;
    public static readonly BoxStrut Zero = new(0, 0, 0, 0);
}

public readonly struct PhysicalBoxStrut
{
    public float Top { get; } public float Right { get; } public float Bottom { get; } public float Left { get; }
    public PhysicalBoxStrut(float top, float right, float bottom, float left) { Top = top; Right = right; Bottom = bottom; Left = left; }
    public float HorizontalSum => Left + Right;
    public float VerticalSum => Top + Bottom;
    public static readonly PhysicalBoxStrut Zero = new(0, 0, 0, 0);
}

public enum WritingMode { HorizontalTb, VerticalRl, VerticalLr, SidewaysRl, SidewaysLr }
public enum TextDirection { Ltr, Rtl }

public readonly struct WritingDirectionMode
{
    public WritingMode WritingMode { get; } public TextDirection Direction { get; }
    public WritingDirectionMode(WritingMode writingMode, TextDirection direction) { WritingMode = writingMode; Direction = direction; }
    public bool IsHorizontal => WritingMode == WritingMode.HorizontalTb;
    public WritingMode GetWritingMode() => WritingMode;

    public static readonly WritingDirectionMode HorizontalLtr = new(WritingMode.HorizontalTb, TextDirection.Ltr);
    public static readonly WritingDirectionMode HorizontalRtl = new(WritingMode.HorizontalTb, TextDirection.Rtl);
    public static readonly WritingDirectionMode VerticalLtr = new(WritingMode.VerticalRl, TextDirection.Ltr);
    public static readonly WritingDirectionMode VerticalRtl = new(WritingMode.VerticalRl, TextDirection.Rtl);
}