using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// A flex item in the engine's flex layout algorithm. Mirrors NGFlexItem.
/// </summary>
public class FlexItem
{
    public BlockNode InputNode { get; }
    public float MainAxisFinalSize { get; set; }
    public float MarginBlockEnd { get; set; }
    public float TotalRemainingBlockSize { get; set; }
    public FlexOffset Offset { get; set; }
    public bool IsInitialBlockSizeIndefinite { get; set; }
    public bool IsUsedFlexBasisIndefinite { get; set; }
    public bool HasDescendantThatDependsOnPercentageBlockSize { get; set; }

    public FlexItem(BlockNode inputNode)
    {
        InputNode = inputNode;
    }

    public ComputedStyle? Style => InputNode.Style;
}

/// <summary>
/// A flex line containing a collection of flex items. Mirrors NGFlexLine.
/// </summary>
public class FlexLine
{
    public List<FlexItem> Items { get; }
    public float MainAxisFreeSpace { get; set; }
    public float LineCrossSize { get; set; }
    public float CrossAxisOffset { get; set; }
    public float MajorBaseline { get; set; }
    public float MinorBaseline { get; set; }
    public float ItemOffsetAdjustment { get; set; }
    public bool HasSeenAllChildren { get; set; }

    public FlexLine(int itemCount)
    {
        Items = new List<FlexItem>(itemCount);
    }

    public float LineCrossEnd => LineCrossSize + CrossAxisOffset + ItemOffsetAdjustment;
}