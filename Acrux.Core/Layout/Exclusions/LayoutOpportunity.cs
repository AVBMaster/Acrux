using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// A 2D-area where a LogicalFragment can fit within the exclusion space.
/// Coordinates are relative to the BFC.
/// Mirrors LayoutOpportunity in layout_opportunity.h (simplified, no shape
/// exclusions).
/// </summary>
public readonly struct LayoutOpportunity
{
    public BfcRect Rect { get; }

    /// <summary>Always false in this simplified version (no shape-outside support).</summary>
    public bool HasShapeExclusions => false;

    public LayoutOpportunity(BfcRect rect)
    {
        Rect = rect;
    }

    public static LayoutOpportunity Default =>
        new(new BfcRect(
            new BfcOffset(float.MinValue, float.MinValue),
            new BfcOffset(float.MaxValue, float.MaxValue)));

    public bool IsBlockDeltaBelowShapes(float blockDelta) => true;

    public LineLayoutOpportunity ComputeLineLayoutOpportunity(float lineBlockSize, float blockDelta) =>
        new(Rect.LineStartOffset, Rect.LineEndOffset,
            Rect.LineStartOffset, Rect.LineEndOffset,
            Rect.BlockStartOffset + blockDelta, lineBlockSize);

    public static bool operator ==(LayoutOpportunity a, LayoutOpportunity b) => a.Rect == b.Rect;
    public static bool operator !=(LayoutOpportunity a, LayoutOpportunity b) => !(a == b);
    public override bool Equals(object? obj) => obj is LayoutOpportunity o && this == o;
    public override int GetHashCode() => Rect.GetHashCode();
}