using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// LayoutInline - inline box layout object (display: inline). Mirrors
/// layout_inline.cc. Called an "inline box" in CSS 2.1.
/// http://www.w3.org/TR/CSS2/visuren.html#inline-boxes
/// </summary>
public partial class LayoutInline : LayoutBoxModelObject
{
    private readonly List<LayoutObject> _children = new();
    private bool _alwaysCreateLineBoxesForLayoutInline;
    private int _firstInlineFragmentItemIndex;
    private PhysicalRect _lineBoxesBoundingBox;

    public LayoutInline(Node? node) : base(node)
    {
    }

    public override string GetName() => "LayoutInline";
    public override bool IsLayoutInline => true;
    public override bool IsInline => true;
    public override bool CanHaveChildren => true;

    public static LayoutInline CreateAnonymous(Document? document) => new(null);

    public override List<LayoutObject> Children => _children;

    public override LayoutObject? FirstChild => _children.Count > 0 ? _children[0] : null;
    public override LayoutObject? LastChild => _children.Count > 0 ? _children[^1] : null;
    public override int ChildrenCount => _children.Count;

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

        if (insertIndex > 0)
        {
            _children[insertIndex - 1].NextSibling = child;
            child.PreviousSibling = _children[insertIndex - 1];
        }
        if (insertIndex < _children.Count)
        {
            _children[insertIndex].PreviousSibling = child;
            child.NextSibling = _children[insertIndex];
        }
        _children.Insert(insertIndex, child);
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
    }

    // ===== Line boxes / inline fragments =====

    /// <summary>True if this inline should always generate its own line box(es).</summary>
    public bool AlwaysCreateLineBoxes
    {
        get => AlwaysCreateLineBoxesForLayoutInline();
        set => SetAlwaysCreateLineBoxesForLayoutInline(value);
    }

    /// <summary>Raw line-box-creation flag shared with LayoutObject tracking.</summary>
    public bool AlwaysCreateLineBoxesForLayoutInline() => _alwaysCreateLineBoxesForLayoutInline;
    public void SetAlwaysCreateLineBoxesForLayoutInline(bool value) => _alwaysCreateLineBoxesForLayoutInline = value;

    /// <summary>True if this inline box should force creation of a PhysicalBoxFragment.</summary>
    public bool ShouldCreateBoxFragment => _alwaysCreateLineBoxesForLayoutInline && IsInLayoutNGInlineFormattingContextValue;

    public void SetShouldCreateBoxFragment(bool value = true) => SetAlwaysCreateLineBoxesForLayoutInline(value);
    public void UpdateShouldCreateBoxFragment() { }

    /// <summary>The index of the first fragment item associated with this object (0 = none).</summary>
    public int FirstInlineFragmentItemIndex
    {
        get => IsInLayoutNGInlineFormattingContextValue ? _firstInlineFragmentItemIndex : 0;
        set => _firstInlineFragmentItemIndex = value;
    }

    public void ClearFirstInlineFragmentItemIndex() => _firstInlineFragmentItemIndex = 0;

    public bool HasInlineFragments() => IsInLayoutNGInlineFormattingContextValue;

    /// <summary>Bounding box of all line boxes associated with this inline, in physical coordinates.</summary>
    public PhysicalRect PhysicalLinesBoundingBox() => _lineBoxesBoundingBox;

    /// <summary>Bounding box of the line boxes plus their visual overflow.</summary>
    public PhysicalRect LinesVisualOverflowBoundingBox() => _lineBoxesBoundingBox;

    public override PhysicalRect VisualOverflowRect() => _lineBoxesBoundingBox;

    /// <summary>The top-left corner of the first line box (or zero if none).</summary>
    public PhysicalOffset FirstLineBoxTopLeft() => new(_lineBoxesBoundingBox.X, _lineBoxesBoundingBox.Y);

    /// <summary>Directive to create or merge into a line box for this inline.</summary>
    public void SetLineBoxesBoundingBox(PhysicalRect rect) => _lineBoxesBoundingBox = rect;

    public override float FirstLineHeight() => 0;

    // ===== Margins =====

    public override float MarginLeft => ResolveLength(StyleRef().MarginLeft);
    public override float MarginRight => ResolveLength(StyleRef().MarginRight);
    public override float MarginTop => ResolveLength(StyleRef().MarginTop);
    public override float MarginBottom => ResolveLength(StyleRef().MarginBottom);

    // ===== Hit testing =====

    /// <summary>Hit test the culled (line box-less) inline by regenerating line box rects.</summary>
    public bool HitTestCulledInline(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        if (result == null)
            return false;
        var localPoint = hitTestLocation.Point - accumulatedOffset;
        var rect = _lineBoxesBoundingBox;
        if (rect.Width == 0 && rect.Height == 0)
            rect = new PhysicalRect(localPoint, new PhysicalSize(1, 1));
        if (hitTestLocation.BoundingBox.Intersects(rect))
        {
            result.SetNodeAndPosition(Node, hitTestLocation.Point);
            return true;
        }
        return false;
    }

    public override bool NodeAtPoint(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        if (result == null)
            return false;
        if (HitTestCulledInline(result, hitTestLocation, accumulatedOffset))
            return true;
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i].HitTestAllPhases(result, hitTestLocation, accumulatedOffset))
                return true;
        }
        return false;
    }

    public override bool HitTestAllPhases(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        return base.HitTestAllPhases(result, hitTestLocation, accumulatedOffset) ||
               NodeAtPoint(result, hitTestLocation, accumulatedOffset);
    }

    // ===== Offsets =====

    public override float OffsetWidth() => _lineBoxesBoundingBox.Width;
    public override float OffsetHeight() => _lineBoxesBoundingBox.Height;

    public PhysicalRect AbsoluteBoundingBoxRectHandlingEmptyInline()
    {
        var rect = _lineBoxesBoundingBox;
        if (rect.Width == 0 && rect.Height == 0)
            rect = new PhysicalRect(LocalToAbsolutePoint(PhysicalOffset.Zero), PhysicalSize.Zero);
        return LocalToAbsoluteRect(rect);
    }

    public override void Paint(PaintInfo? paintInfo)
    {
        if (paintInfo == null)
            return;
        foreach (var child in _children)
            child.Paint(paintInfo);
    }
}

/// <summary>Compatibility storage backer for the inline formatting-context state.</summary>
public partial class LayoutInline
{
    internal bool IsInLayoutNGInlineFormattingContextValue { get; set; }
    internal bool AlwaysCreateLineBoxesForLayoutInlineValue => _alwaysCreateLineBoxesForLayoutInline;
}