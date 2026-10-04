using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

public class PaginationUtils2
{
    public static void SetupPagination(ConstraintSpace space, BoxFragmentBuilder builder) { }
    public static bool IsBreakInside(BlockBreakToken? breakToken) => false;
    public static LayoutUnit FragmentainerSpaceAvailable(LayoutUnit blockOffset) => LayoutUnit.FromValue(0);
    public static void ConsumeRemainingFragmentainerSpace(LayoutUnit offsetInStitchedContainer) { }
    public static BreakStatus BreakBeforeChildIfNeeded(LayoutInputNode child, LayoutResult layoutResult,
        LayoutUnit fragmentainerBlockOffset, bool hasContainerSeparation) => BreakStatus.Continue;
    public static void FinishFragmentation(BoxFragmentBuilder builder) { }
}

public class PaginatedRootLayoutAlgorithm : LayoutAlgorithm
{
    public PaginatedRootLayoutAlgorithm(Element node, in ConstraintSpace space) : base(node, space) { }
    public override LayoutResult Layout() => LayoutResult.FromFragment(Builder.ToBoxFragment());
}

public class PageContainerLayoutAlgorithm : LayoutAlgorithm
{
    public PageContainerLayoutAlgorithm(Element node, in ConstraintSpace space) : base(node, space) { }
    public override LayoutResult Layout() => LayoutResult.FromFragment(Builder.ToBoxFragment());
}

public class PageBorderBoxLayoutAlgorithm : LayoutAlgorithm
{
    public PageBorderBoxLayoutAlgorithm(Element node, in ConstraintSpace space) : base(node, space) { }
    public override LayoutResult Layout() => LayoutResult.FromFragment(Builder.ToBoxFragment());
}