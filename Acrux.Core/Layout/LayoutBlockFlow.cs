using Acrux.Core.Dom;
using Acrux.Core.Layout.Inline;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// LayoutBlockFlow - block flow (block container) layout object that implements
/// a CSS 2.1 block container and owns floats and the inline formatting context.
/// Mirrors layout_block_flow.cc.
/// http://www.w3.org/TR/CSS21/visuren.html#block-boxes
/// </summary>
public partial class LayoutBlockFlow : LayoutBlock
{
    private LayoutMultiColumnFlowThread? _multiColumnFlowThread;
    private InlineNodeData? _inlineNodeData;
    protected FragmentItems? _fragmentItems;
    private bool _mayBeNonContiguousIfc;

    public LayoutBlockFlow(Node? node) : base(node)
    {
    }

    public override bool IsLayoutBlockFlow => true;
    public override bool IsInline => false;
    public override bool CreatesNewFormattingContext => true;
    public override string GetName() => "LayoutBlockFlow";

    /// <summary>Create an anonymous block flow with the given style.</summary>
    public static LayoutBlockFlow CreateAnonymous(Document? document, ComputedStyle? style)
    {
        var blockFlow = new LayoutBlockFlow(null);
        if (style != null)
            blockFlow.SetAnonymousStyle(style);
        return blockFlow;
    }

    private void SetAnonymousStyle(ComputedStyle style)
    {
        // In the simplified model we just reference the style via the layout
        // object; no copy is needed.
        AnonymousStyle = style;
    }

    public ComputedStyle? AnonymousStyle { get; private set; }

    public bool CanContainFirstFormattedLine() => true;

    // ===== Multi-column =====

    public LayoutMultiColumnFlowThread? MultiColumnFlowThread() => _multiColumnFlowThread;

    public void ResetMultiColumnFlowThread() => _multiColumnFlowThread = null;

    public bool IsFragmentationContextRoot => _multiColumnFlowThread != null;

    public void SetMultiColumnFlowThread(LayoutMultiColumnFlowThread? flowThread) => _multiColumnFlowThread = flowThread;

    // ===== Inline formatting context =====

    /// <summary>Returns the associated InlineNodeData, or null if not an inline formatting context root.</summary>
    public InlineNodeData? GetInlineNodeData() => _inlineNodeData;

    public void ResetInlineNodeData()
    {
        _inlineNodeData = new InlineNodeData();
        ResetFragmentItems();
    }

    public void ClearInlineNodeData()
    {
        _inlineNodeData = null;
        _fragmentItems = null;
    }

    public void WillCollectInlines() { }

    /// <summary>True if children should be laid out as inline content.</summary>
    public bool ChildrenInline() => ChildrenInlineValue;

    public void SetChildrenInline(bool value) => ChildrenInlineValue = value;

    public bool MayBeNonContiguousIfc() => _mayBeNonContiguousIfc;
    public void SetMayBeNonContiguousIfc(bool value) => _mayBeNonContiguousIfc = value;

    public FragmentItems? FragmentItems() => _fragmentItems;
    public void SetFragmentItems(FragmentItems items) => _fragmentItems = items;
    public void ResetFragmentItems() => _fragmentItems = null;

    /// <summary>The block flow that will have fragment items for |this| inline-level object.</summary>
    public LayoutBlockFlow? FragmentItemsContainer()
    {
        LayoutObject? container = Container();
        while (container != null)
        {
            if (container is LayoutBlockFlow blockFlow && blockFlow.IsInlineFormattingContextRoot())
                return blockFlow;
            container = container.Container();
        }
        return null;
    }

    public bool IsInlineFormattingContextRoot() => _inlineNodeData != null || ChildrenInline();

    // ===== Children management overrides =====

    public override void AddChild(LayoutObject? child, LayoutObject? beforeChild = null)
    {
        base.AddChild(child, beforeChild);
    }

    public void MoveAllChildrenIncludingFloatsTo(LayoutBlock toBlock, bool fullRemoveInsert)
    {
        var children = new List<LayoutObject>(Children);
        foreach (var child in children)
            RemoveChild(child);
        foreach (var child in children)
            toBlock.AddChild(child);
    }

    /// <summary>Called when a child becomes floating or out-of-flow positioned.</summary>
    public void ChildBecameFloatingOrOutOfFlow(AuroraBox child) { }

    public void CollapseAnonymousBlockChild(LayoutBlockFlow child) { }

    // ===== Hit testing =====

    protected override bool HitTestChildren(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        if (result == null)
            return false;
        if (ChildrenInline() && _fragmentItems != null)
        {
            var localPoint = hitTestLocation.Point - accumulatedOffset;
            foreach (var item in _fragmentItems.Items)
            {
                if (!item.IsBox && !item.IsText)
                    continue;
                if (item.Rect.Intersects(new PhysicalRect(localPoint, new PhysicalSize(1, 1))))
                {
                    result.SetNodeAndPosition(item.LayoutObject?.Node ?? Node, hitTestLocation.Point);
                    return true;
                }
            }
        }
        return base.HitTestChildren(result, hitTestLocation, accumulatedOffset);
    }
}

/// <summary>Compatibility storage backer for the inline children flag.</summary>
public partial class LayoutBlockFlow
{
    internal bool ChildrenInlineValue { get; set; } = true;
}