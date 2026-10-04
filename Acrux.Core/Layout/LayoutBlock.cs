using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// LayoutBlock - the class used by any LayoutObject that is a containing block.
/// Mirrors layout_block.cc. Handles child management and out-of-flow positioned
/// descendants (which are tracked in a separate list and laid out by this
/// block).
/// http://www.w3.org/TR/CSS2/visuren.html#containing-block
/// </summary>
public class LayoutBlock : AuroraBox
{
    private readonly List<LayoutObject> _children = new();
    private readonly HashSet<LayoutObject> _positionedDescendants = new();
    private bool _hasLineIfEmpty;
    private bool _isAnonymousBlock;

    public LayoutBlock(Node? node) : base(node)
    {
    }

    public override string GetName() => "LayoutBlock";
    public override bool IsLayoutBlock => true;
    public override bool IsBlock => true;
    public override bool CreatesNewFormattingContext => true;
    public override bool IsLayoutNGObject => true;

    public override List<LayoutObject> Children => _children;

    public override LayoutObject? FirstChild => _children.Count > 0 ? _children[0] : null;
    public override LayoutObject? LastChild => _children.Count > 0 ? _children[^1] : null;
    public override int ChildrenCount => _children.Count;

    public bool ChildrenInline { get; set; }

    // ===== Children management =====

    public override void AddChild(LayoutObject? child, LayoutObject? beforeChild = null)
    {
        if (child == null)
            return;
        if (child.Parent != null && !ReferenceEquals(child.Parent, this))
            child.Parent.RemoveChild(child);

        child.Parent = this;
        int insertIndex = beforeChild != null ? _children.IndexOf(beforeChild) : -1;
        if (insertIndex < 0)
            insertIndex = _children.Count;

        var next = insertIndex < _children.Count ? _children[insertIndex] : null;
        var prev = insertIndex > 0 ? _children[insertIndex - 1] : null;
        if (prev != null) prev.NextSibling = child;
        if (next != null) next.PreviousSibling = child;
        child.PreviousSibling = prev;
        child.NextSibling = next;
        _children.Insert(insertIndex, child);

        if (child.IsOutOfFlowPositioned)
            _positionedDescendants.Add(child);
        child.InsertedIntoTree();
    }

    public override void RemoveChild(LayoutObject child)
    {
        int idx = _children.IndexOf(child);
        if (idx < 0)
            return;
        if (idx > 0) _children[idx - 1].NextSibling = child.NextSibling;
        if (idx + 1 < _children.Count) _children[idx + 1].PreviousSibling = child.PreviousSibling;
        _children.RemoveAt(idx);
        child.Parent = null;
        child.PreviousSibling = null;
        child.NextSibling = null;
        _positionedDescendants.Remove(child);
        child.WillBeRemovedFromTree();
    }

    public void ClearChildren() => _children.Clear();

    /// <summary>Add a child, surfacing any block-in-inline wrappers. Mirrors LayoutBlock::AddChild().</summary>
    public void AddChildBeforeDescendant(LayoutObject? newChild, LayoutObject? beforeDescendant)
    {
        if (beforeDescendant != null && beforeDescendant.Parent != null && !ReferenceEquals(beforeDescendant.Parent, this))
        {
            if (beforeDescendant.Parent is LayoutBlockFlow wrapper)
            {
                wrapper.AddChild(newChild, beforeDescendant);
                return;
            }
        }
        AddChild(newChild, beforeDescendant);
    }

    public override void AddChildIgnoringContinuation(LayoutObject? newChild, LayoutObject? beforeChild = null) =>
        AddChild(newChild, beforeChild);

    // ===== Anonymous blocks =====

    /// <summary>Create an anonymous block sharing this block's style.</summary>
    public LayoutBlock CreateAnonymousBlock()
    {
        return CreateAnonymousWithParentAndDisplay(this, DisplayType.Block);
    }

    /// <summary>Create an anonymous block parented under |parent| with the given display.</summary>
    public static LayoutBlock CreateAnonymousWithParentAndDisplay(LayoutObject parent, DisplayType display = DisplayType.Block)
    {
        var block = new LayoutBlock(null);
        block._isAnonymousBlock = display is DisplayType.Block or DisplayType.Flex or DisplayType.Grid;
        return block;
    }

    public override bool IsAnonymousBlock => _isAnonymousBlock;

    // ===== Text / first line =====

    public virtual bool HasLineIfEmpty() => _hasLineIfEmpty;
    public void SetHasLineIfEmpty(bool value) => _hasLineIfEmpty = value;

    public virtual float FirstLineHeight() => 0;

    public float TextIndentOffset() => StyleRef().TextIndent;

    /// <summary>Returns baseline offset for an empty line, or null if no font data.</summary>
    public float? BaselineForEmptyLine() => HasLineIfEmpty() ? FirstLineHeight() : null;

    public virtual LayoutBlockFlow? NearestInnerBlockWithFirstLine() => this as LayoutBlockFlow;

    // ===== Positioned descendants =====

    /// <summary>Out-of-flow positioned descendants, mirroring PositionedObjects() in LayoutBlock.</summary>
    public IEnumerable<LayoutObject> PositionedObjects() => _positionedDescendants;

    public void RemovePositionedObjects(LayoutObject? root)
    {
        if (root == null)
        {
            _positionedDescendants.Clear();
            return;
        }
        _positionedDescendants.RemoveWhere(o => o == root || o.IsDescendantOf(root));
    }

    public bool HasPositionedObjects() => _positionedDescendants.Count > 0;

    // ===== Overflow =====

    public override void RecalcScrollableOverflow()
    {
        base.RecalcScrollableOverflow();
        if (!HasVisualOverflow())
            return;
        foreach (var child in _children)
        {
            if (child.IsBox)
            {
                var childBox = (AuroraBox)child;
                AddContentsVisualOverflow(childBox.BorderBoxRect);
            }
        }
        ClearNeedsOverflowRecalc();
    }

    // ===== Hit testing =====

    protected virtual bool HitTestChildren(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        if (result == null)
            return false;
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i].HitTestAllPhases(result, hitTestLocation, accumulatedOffset))
                return true;
        }
        return false;
    }

    public override bool NodeAtPoint(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        if (result == null)
            return false;
        return HitTestChildren(result, hitTestLocation, accumulatedOffset) || base.NodeAtPoint(result, hitTestLocation, accumulatedOffset);
    }

    public override bool HitTestAllPhases(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        return base.HitTestAllPhases(result, hitTestLocation, accumulatedOffset) ||
               HitTestChildren(result, hitTestLocation, accumulatedOffset);
    }

    // ===== Painting =====

    public override void Paint(PaintInfo? paintInfo)
    {
        if (paintInfo == null)
            return;
        foreach (var child in _children)
            child.Paint(paintInfo);
    }
}