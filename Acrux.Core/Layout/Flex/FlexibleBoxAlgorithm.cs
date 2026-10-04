using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Flex;

public class FlexibleBoxAlgorithm
{
    public FlexibleBoxAlgorithm(ComputedStyle style, LayoutUnit lineBreakLength, LogicalSize percentResolutionSizes)
    {
        Style = style;
        LineBreakLength = lineBreakLength;
    }

    public ComputedStyle Style { get; }
    public LayoutUnit LineBreakLength { get; }
    public List<FlexItem> AllItems { get; } = new();
    public List<FlexLine> FlexLines { get; } = new();
    public int NextItemIndex { get; set; }

    public bool IsHorizontalFlow => true;
    public bool IsColumnFlow => false;
    public bool IsMultiline => false;

    public FlexLine? ComputeNextFlexLine() => null;
    public LayoutUnit IntrinsicContentBlockSize() => LayoutUnit.FromValue(0);
}

public class FlexItem
{
    public ComputedStyle Style { get; set; }
    public LayoutUnit FlexBaseContentSize { get; set; }
    public MinMaxSizes MinMaxMainSizes { get; set; }
    public LayoutUnit HypotheticalMainContentSize { get; set; }
    public LayoutUnit MainAxisBorderPadding { get; set; }
    public BoxStrut Scrollbars { get; set; }
    public LayoutUnit FlexedContentSize { get; set; }
    public LayoutUnit CrossAxisSize { get; set; }
    public bool IsInitialBlockSizeIndefinite { get; set; }
    public bool IsUsedFlexBasisIndefinite { get; set; }
    public bool DependsOnMinMaxSizes { get; set; }
    public bool Frozen { get; set; }
    public BlockNode NgInputNode { get; set; }
    public LayoutResult? LayoutResult { get; set; }
    public LayoutUnit? MaxContentContribution { get; set; }
    public FlexOffset Offset { get; set; }

    public LayoutUnit HypotheticalMainAxisMarginBoxSize =>
        HypotheticalMainContentSize + MainAxisBorderPadding + MainAxisMarginExtent();

    public LayoutUnit FlexBaseMarginBoxSize =>
        FlexBaseContentSize + MainAxisBorderPadding + MainAxisMarginExtent();

    public LayoutUnit FlexedBorderBoxSize =>
        FlexedContentSize + MainAxisBorderPadding;

    public LayoutUnit FlexedMarginBoxSize =>
        FlexedContentSize + MainAxisBorderPadding + MainAxisMarginExtent();

    public LayoutUnit ClampSizeToMinAndMax(LayoutUnit size) =>
        MinMaxMainSizes.ClampSizeToMinAndMax(size);

    public LayoutUnit MainAxisMarginExtent() => LayoutUnit.FromValue(0);
    public LayoutUnit CrossAxisMarginExtent() => LayoutUnit.FromValue(0);
}

public class FlexLine
{
    public FlexibleBoxAlgorithm Algorithm { get; }
    public List<FlexItem> LineItems { get; }
    public LayoutUnit SumFlexBaseSize { get; }
    public double TotalFlexGrow { get; }
    public double TotalFlexShrink { get; }
    public double TotalWeightedFlexShrink { get; }
    public LayoutUnit SumHypotheticalMainSize { get; }
    public LayoutUnit ContainerMainInnerSize { get; set; }
    public LayoutUnit InitialFreeSpace { get; set; }
    public LayoutUnit RemainingFreeSpace { get; set; }
    public LayoutUnit CrossAxisExtent { get; set; }

    public FlexLine(FlexibleBoxAlgorithm algorithm, List<FlexItem> lineItems, LayoutUnit sumFlexBaseSize,
        double totalFlexGrow, double totalFlexShrink, double totalWeightedFlexShrink, LayoutUnit sumHypotheticalMainSize)
    {
        Algorithm = algorithm;
        LineItems = lineItems;
        SumFlexBaseSize = sumFlexBaseSize;
        TotalFlexGrow = totalFlexGrow;
        TotalFlexShrink = totalFlexShrink;
        TotalWeightedFlexShrink = totalWeightedFlexShrink;
        SumHypotheticalMainSize = sumHypotheticalMainSize;
    }

    public FlexSign Sign() =>
        SumHypotheticalMainSize < ContainerMainInnerSize ? FlexSign.PositiveFlexibility : FlexSign.NegativeFlexibility;

    public void SetContainerMainInnerSize(LayoutUnit size) { ContainerMainInnerSize = size; }
    public void FreezeInflexibleItems() { }
    public void FreezeViolations(List<FlexItem> violations) { }
    public bool ResolveFlexibleLengths() => true;
    public LayoutUnit ApplyMainAxisAutoMarginAdjustment() => LayoutUnit.FromValue(0);
    public void ComputeLineItemsPosition() { }
}

public class LayoutFlexibleBox
{
    public void Layout() { }
}