using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// A 1D-area where a line can fit (inline size only).
/// Mirrors LineLayoutOpportunity in line_layout_opportunity.h.
/// </summary>
public readonly struct LineLayoutOpportunity
{
    /// <summary>Available inline-size of the line, taking shapes into account.</summary>
    public float LineLeftOffset { get; }
    public float LineRightOffset { get; }

    /// <summary>Available inline-size for floats (same as the layout opportunity).</summary>
    public float FloatLineLeftOffset { get; }
    public float FloatLineRightOffset { get; }

    public float BfcBlockOffset { get; }
    public float LineBlockSize { get; }

    public LineLayoutOpportunity(float lineLeftOffset, float lineRightOffset,
        float floatLineLeftOffset, float floatLineRightOffset,
        float bfcBlockOffset, float lineBlockSize)
    {
        LineLeftOffset = lineLeftOffset;
        LineRightOffset = lineRightOffset;
        FloatLineLeftOffset = floatLineLeftOffset;
        FloatLineRightOffset = floatLineRightOffset;
        BfcBlockOffset = bfcBlockOffset;
        LineBlockSize = lineBlockSize;
    }

    public LineLayoutOpportunity(float inlineSize) : this(0, inlineSize, 0, inlineSize, 0, 0) { }

    public float AvailableInlineSize => LineRightOffset - LineLeftOffset;
    public float AvailableFloatInlineSize => FloatLineRightOffset - FloatLineLeftOffset;

    public bool IsEqualToAvailableFloatInlineSize(float inlineSize) =>
        FloatLineLeftOffset + inlineSize == FloatLineRightOffset;
}