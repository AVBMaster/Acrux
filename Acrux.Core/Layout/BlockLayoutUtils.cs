using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Effective align-content value for a block container.
/// Mirrors BlockContentAlignment in block_layout_algorithm_utils.h.
/// </summary>
public enum BlockContentAlignment
{
    Start,
    SafeCenter,
    UnsafeCenter,
    SafeEnd,
    UnsafeEnd,
    Baseline,
}

/// <summary>
/// Shared helpers used by the block layout algorithm.
/// Mirrors block_layout_algorithm_utils.cc.
/// </summary>
public static class BlockLayoutUtils
{
    /// <summary>
    /// Resolve the effective align-content for a block container.
    /// Mirrors ComputeContentAlignmentForBlock().
    /// </summary>
    public static BlockContentAlignment ComputeContentAlignmentForBlock(string alignContent)
    {
        switch (alignContent)
        {
            case "center":
                return BlockContentAlignment.SafeCenter;
            case "end":
            case "flex-end":
                return BlockContentAlignment.SafeEnd;
            case "space-between":
                // Fallback alignment of <content-distribution> is flex-start.
                return BlockContentAlignment.Start;
            case "baseline":
                return BlockContentAlignment.Baseline;
            default:
                // "normal", "start", "flex-start", "stretch".
                return BlockContentAlignment.Start;
        }
    }

    /// <summary>
    /// Resolve the effective align-content for a table cell.
    /// Mirrors ComputeContentAlignmentForTableCell().
    /// </summary>
    public static BlockContentAlignment ComputeContentAlignmentForTableCell(string alignContent)
    {
        switch (alignContent)
        {
            case "center":
                return BlockContentAlignment.SafeCenter;
            case "end":
            case "flex-end":
                return BlockContentAlignment.UnsafeEnd;
            case "baseline":
                return BlockContentAlignment.Baseline;
            default:
                return BlockContentAlignment.Start;
        }
    }

    /// <summary>
    /// Shift children in the block direction when a block container has spare
    /// space and align-content is center/end. Mirrors AlignBlockContent().
    /// </summary>
    public static void AlignBlockContent(ComputedStyle style, float contentBlockSize, BoxFragmentBuilder builder)
    {
        // Only meaningful when the fragment already resolved its final block size
        // (border-box). Spare space only exists when the container has an
        // explicit block-size larger than its content.
        float freeSpace = builder.BlockSize - contentBlockSize;
        if (float.IsNaN(freeSpace) || freeSpace <= 0) return;

        switch (ComputeContentAlignmentForBlock(style.AlignContent))
        {
            case BlockContentAlignment.SafeCenter:
            case BlockContentAlignment.UnsafeCenter:
                builder.MoveChildrenInBlockDirection(freeSpace / 2f);
                break;
            case BlockContentAlignment.SafeEnd:
            case BlockContentAlignment.UnsafeEnd:
                builder.MoveChildrenInBlockDirection(freeSpace);
                break;
            // Start/Baseline: nothing to do.
        }
    }

    /// <summary>
    /// The block-direction offset a cleared box must sit below, given the
    /// current content edge and the relevant float extents.
    /// </summary>
    public static float ClearanceBottom(ClearType clear, float floatBottomLeft, float floatBottomRight) =>
        clear switch
        {
            ClearType.Left => floatBottomLeft,
            ClearType.Right => floatBottomRight,
            ClearType.Both => Math.Max(floatBottomLeft, floatBottomRight),
            _ => float.NegativeInfinity,
        };

    /// <summary>
    /// True when |clear| needs the content edge to be pushed down to at least
    /// |contentEdge|'s float exclusion bottom.
    /// </summary>
    public static bool ShouldClearFloat(ClearType clear, float contentEdge, float floatBottomLeft, float floatBottomRight)
    {
        if (clear == ClearType.None) return false;
        return ClearanceBottom(clear, floatBottomLeft, floatBottomRight) > contentEdge;
    }
}