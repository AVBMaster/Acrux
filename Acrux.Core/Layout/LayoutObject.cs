using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Controls whether a marking operation should also mark the container chain.
/// Mirrors MarkingBehavior in layout_object.h.
/// </summary>
public enum MarkingBehavior
{
    MarkOnlyThis,
    MarkContainerChain,
}

/// <summary>
/// Controls whether a relayout should be scheduled for the document.
/// Mirrors ScheduleRelayoutBehavior in layout_object.h.
/// </summary>
public enum ScheduleRelayoutBehavior
{
    ScheduleRelayout,
    DontScheduleRelayout,
}

/// <summary>
/// Positioning state of a layout object. Mirrors PositionedState in
/// layout_object.h.
/// </summary>
public enum PositionedState
{
    IsStaticallyPositioned = 0,
    IsRelativelyPositioned = 1,
    IsOutOfFlowPositioned = 2,
    IsStickyPositioned = 3,
}

/// <summary>
/// LayoutObject - base class for all layout tree objects.
/// Mirrors layout_object.cc. Forms a tree that closely maps the DOM tree.
/// </summary>
public abstract class LayoutObject
{
    private PositionedState _positionedState = PositionedState.IsStaticallyPositioned;
    private bool _isFloating;
    private bool _isInline;
    private bool _isAtomicInlineLevel;
    private bool _horizontalWritingMode = true;
    private bool _hasNonVisibleOverflow;
    private bool _hasLayer;
    private bool _hasBoxDecorationBackground;
    private bool _hasTransformRelatedProperty;
    private bool _everHadLayout;
    private bool _intrinsicLogicalWidthsDirty;
    private bool _needsOverflowRecalc;
    private bool _selfNeedsFullLayout;
    private bool _childNeedsFullLayout;
    private bool _needsSimplifiedLayout;
    private OverflowClipAxes _overflowClipAxes = OverflowClipAxes.None;

    public Node? Node { get; }
    public LayoutObject? Parent { get; set; }
    public LayoutObject? PreviousSibling { get; set; }
    public LayoutObject? NextSibling { get; set; }
    public bool IsAnonymous { get; }
    public SelectionState SelectionState { get; set; } = SelectionState.None;
    public bool IsSVGChild { get; }

    protected LayoutObject(Node? node, bool isAnonymous = false)
    {
        Node = node;
        IsAnonymous = isAnonymous || node == null;
    }

    public virtual string GetName() => "LayoutObject";

    /// <summary>Returns the name plus extra information about the object state (e.g. positioning).</summary>
    public string DecoratedName()
    {
        var name = GetName();
        if (IsPositioned) name += " (positioned)";
        if (IsFloating) name += " (floating)";
        return name;
    }

    public virtual List<LayoutObject> Children => new();

    public LayoutObject? SlowFirstChild() => Children.Count > 0 ? Children[0] : null;
    public LayoutObject? SlowLastChild() => Children.Count > 0 ? Children[^1] : null;

    public bool IsDescendantOf(LayoutObject? ancestor)
    {
        var current = Parent;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.Parent;
        }
        return false;
    }

    // ===== Tree navigation =====

    /// <summary>Returns the next object in pre-order traversal, or null if there is none.</summary>
    public LayoutObject? NextInPreOrder() => NextInPreOrder(null);

    /// <summary>Returns the next object in pre-order traversal, staying within |stayWithin|.</summary>
    public LayoutObject? NextInPreOrder(LayoutObject? stayWithin)
    {
        // If the object has children, return the first child.
        if (Children.Count > 0)
            return Children[0];
        return NextInPreOrderAfterChildren(stayWithin);
    }

    /// <summary>Returns the next object after this one's children, staying within |stayWithin|.</summary>
    public LayoutObject? NextInPreOrderAfterChildren(LayoutObject? stayWithin)
    {
        if (NextSibling != null)
            return NextSibling;
        LayoutObject? parent = Parent;
        while (parent != null && parent != stayWithin)
        {
            if (parent.NextSibling != null)
                return parent.NextSibling;
            parent = parent.Parent;
        }
        return null;
    }

    /// <summary>Returns the previous object in pre-order traversal, staying within |stayWithin|.</summary>
    public LayoutObject? PreviousInPreOrder(LayoutObject? stayWithin)
    {
        if (PreviousSibling != null)
        {
            LayoutObject? lastChild = PreviousSibling;
            while (lastChild is LayoutBlock or LayoutBlockFlow)
            {
                var block = (LayoutBlock)lastChild;
                if (block.Children.Count > 0)
                    lastChild = block.Children[^1];
                else
                    break;
            }
            return lastChild;
        }
        return Parent != stayWithin ? Parent : null;
    }

    /// <summary>The depth of this object in the tree (root has depth 0).</summary>
    public int Depth()
    {
        int depth = 0;
        for (LayoutObject? obj = Parent; obj != null; obj = obj.Parent)
            depth++;
        return depth;
    }

    // ===== Type checking =====

    public virtual bool IsBoxModelObject => false;
    public virtual bool IsInline => false;
    public virtual bool IsBox => false;
    public virtual bool IsLayoutBlock => false;
    public virtual bool IsLayoutBlockFlow => false;
    public virtual bool IsLayoutFlowThread => false;
    public virtual bool IsLayoutReplaced => false;
    public virtual bool IsFragmentLessBox => false;
    public virtual bool IsText => false;
    public virtual bool IsReplaced => false;
    public virtual bool IsAtomicInlineLevel => false;
    public virtual bool IsScrollContainer => false;
    public virtual bool CreatesNewFormattingContext => false;
    public virtual bool IsLayoutNGObject => false;
    public virtual bool IsMonolithic => false;
    public virtual bool IsInsideFlowThread => false;
    public virtual bool IsColumnSpanAll => false;
    public virtual bool IsLayoutListItem => false;
    public virtual bool IsLayoutInsideListMarker => false;
    public virtual bool IsLayoutOutsideListMarker => false;
    public virtual bool IsListMarkerImage => false;
    public virtual bool IsInlineListItem => false;
    public virtual bool IsAnonymousBlock => false;
    public virtual bool IsMedia => false;
    public virtual bool IsImage => false;
    public virtual bool IsVideo => false;
    public virtual bool IsCanvas => false;
    public virtual bool IsLayoutEmbeddedContent => false;
    public virtual bool IsEmbeddedObject => false;
    public virtual bool IsFrame => false;
    public virtual bool IsLayoutIFrame => false;
    public virtual bool IsFrameSet => false;
    public virtual bool IsBR => false;
    public virtual bool IsWordBreak => false;
    public virtual bool IsProgress => false;
    public virtual bool IsRuby => false;
    public virtual bool IsLayoutTextCombine => false;
    public virtual bool IsTextFragment => false;
    public virtual bool IsLayoutInline => false;
    public virtual bool CanHaveChildren => false;
    public virtual bool BackgroundShouldAlwaysBeClipped => false;
    public virtual bool CanBeSelectionLeafInternal => false;
    public virtual bool CanHaveAdditionalCompositingReasons => false;
    public virtual bool ValidNgItems { get; set; } = true;

    public virtual bool IsBlock => false;
    public virtual bool IsEmbeddedContent => false;
    public virtual bool IsIFrame => false;
    public virtual bool IsTextCombine => false;
    public virtual bool IsListMarker => false;
    public virtual bool IsListItem => false;
    public virtual bool HasLayer => false;

    /// <summary>True if this object is absolute or fixed positioned.</summary>
    public bool IsOutOfFlowPositioned => _positionedState == PositionedState.IsOutOfFlowPositioned;
    public bool IsRelPositioned => _positionedState == PositionedState.IsRelativelyPositioned;
    public bool IsStickyPositioned => _positionedState == PositionedState.IsStickyPositioned;
    public bool IsFixedPositioned => IsOutOfFlowPositioned && StyleRef().Position == PositionType.Fixed;
    public bool IsAbsolutePositioned => IsOutOfFlowPositioned && StyleRef().Position == PositionType.Absolute;
    public bool IsPositioned => _positionedState != PositionedState.IsStaticallyPositioned;

    /// <summary>True if this object is floating.</summary>
    public bool IsFloating => _isFloating;

    public bool IsFloatingOrOutOfFlowPositioned => IsFloating || IsOutOfFlowPositioned;

    public bool IsTablePart => IsTableCell || IsLayoutTableCol || IsTableCaption || IsTableRow || IsTableSection;

    // Table-related checks (default to false; overridden by table layout objects).
    public virtual bool IsTable => false;
    public virtual bool IsTableCaption => false;
    public virtual bool IsTableCell => false;
    public virtual bool IsTableRow => false;
    public virtual bool IsTableSection => false;
    public virtual bool IsLayoutTableCol => false;
    public virtual bool IsFieldset => false;
    public virtual bool IsFlexibleBox => false;
    public virtual bool IsLayoutGrid => false;
    public virtual bool IsQuote => false;
    public virtual bool IsHR => false;

    /// <summary>
    /// True if this anonymous object is a non-inline child of a LayoutInline.
    /// Mirrors IsBlockInInline().
    /// </summary>
    public bool IsBlockInInline => IsAnonymous && !IsInline && !IsFloatingOrOutOfFlowPositioned && Parent is { IsLayoutInline: true };

    // ===== Style access =====

    public virtual ComputedStyle? Style => (Node as Element)?.ComputedStyle;

    /// <summary>The computed style of this object. Never returns null (falls back to a default style).</summary>
    public ComputedStyle StyleRef()
    {
        return Style ?? new ComputedStyle();
    }

    /// <summary>The style of the first line, or this object's style if no first-line style applies.</summary>
    public ComputedStyle FirstLineStyle() => StyleRef();

    /// <summary>
    /// The containing block for this object. For most objects this is the
    /// nearest block ancestor. Mirrors LayoutObject::ContainingBlock().
    /// </summary>
    public virtual LayoutObject? ContainingBlock()
    {
        var current = Parent;
        while (current != null)
        {
            if (current.IsLayoutBlock) return current;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// Returns the containing block of the object, using the CSS containing
    /// block rules (inlines can be containers for positioned descendants).
    /// Mirrors LayoutObject::Container().
    /// </summary>
    public virtual LayoutObject? Container()
    {
        if (IsOutOfFlowPositioned)
            return ContainingBlock();
        return Parent;
    }

    /// <summary>Convenience function for getting to the nearest enclosing box of this object.</summary>
    public AuroraBox? EnclosingBox()
    {
        LayoutObject? current = this;
        while (current != null)
        {
            if (current.IsBox) return (AuroraBox)current;
            current = current.Parent;
        }
        return null;
    }

    public LayoutView? View() => this is LayoutView v ? v : Parent?.View();

    // ===== Layout flags =====

    /// <summary>True if this object (or a descendant) needs (re)layout.</summary>
    public bool NeedsLayout
    {
        get => _selfNeedsFullLayout || _childNeedsFullLayout || _needsSimplifiedLayout;
        set
        {
            if (value) SetNeedsLayout();
            else ClearNeedsLayout();
        }
    }

    public bool SelfNeedsFullLayout => _selfNeedsFullLayout;
    public bool ChildNeedsFullLayout => _childNeedsFullLayout;
    public bool NeedsSimplifiedLayout => _needsSimplifiedLayout;
    public bool EverHadLayout => _everHadLayout;
    public bool IntrinsicLogicalWidthsDirty => _intrinsicLogicalWidthsDirty;
    public bool NeedsOverflowRecalc => _needsOverflowRecalc;

    public void SetNeedsLayout(MarkingBehavior markParents = MarkingBehavior.MarkContainerChain)
    {
        if (_selfNeedsFullLayout)
            return;
        _selfNeedsFullLayout = true;
        SetNeedsOverflowRecalc();
        if (markParents == MarkingBehavior.MarkContainerChain)
            MarkContainerChainForLayout();
    }

    public void SetNeedsLayoutAndFullPaintInvalidation(MarkingBehavior markParents = MarkingBehavior.MarkContainerChain)
    {
        SetNeedsLayout(markParents);
        SetShouldDoFullPaintInvalidation();
    }

    public void SetChildNeedsLayout(MarkingBehavior markParents = MarkingBehavior.MarkContainerChain)
    {
        if (_childNeedsFullLayout)
            return;
        _childNeedsFullLayout = true;
        SetNeedsOverflowRecalc();
        if (markParents == MarkingBehavior.MarkContainerChain)
            MarkContainerChainForLayout();
    }

    public void SetNeedsSimplifiedLayout()
    {
        if (_needsSimplifiedLayout)
            return;
        _needsSimplifiedLayout = true;
        MarkContainerChainForLayout();
    }

    /// <summary>
    /// Marks the ancestors of this object as needing a child layout. Mirrors
    /// MarkContainerChainForLayout().
    /// </summary>
    public void MarkContainerChainForLayout()
    {
        for (LayoutObject? obj = Parent; obj != null; obj = obj.Parent)
        {
            if (obj._childNeedsFullLayout)
                return;
            obj._childNeedsFullLayout = true;
            if (obj.IsRelayoutBoundary())
                return;
        }
    }

    /// <summary>
    /// True if this object is a relayout boundary. Mirrors
    /// ObjectIsRelayoutBoundary() in layout_object.cc. Boxes that establish a
    /// formatting context are treated as relayout boundaries by the legacy
    /// engine.
    /// </summary>
    public bool IsRelayoutBoundary() => IsLayoutBlock && CreatesNewFormattingContext;

    public void ClearNeedsLayoutWithoutPaintInvalidation()
    {
        _everHadLayout = true;
        _selfNeedsFullLayout = false;
        _childNeedsFullLayout = false;
        _needsSimplifiedLayout = false;
    }

    public void ClearNeedsLayout()
    {
        ClearNeedsLayoutWithoutPaintInvalidation();
        SetShouldCheckForPaintInvalidation();
    }

    public void ClearNeedsLayoutWithFullPaintInvalidation()
    {
        ClearNeedsLayoutWithoutPaintInvalidation();
        SetShouldDoFullPaintInvalidation();
    }

    public void SetIntrinsicLogicalWidthsDirty(MarkingBehavior markParents = MarkingBehavior.MarkContainerChain)
    {
        if (_intrinsicLogicalWidthsDirty)
            return;
        _intrinsicLogicalWidthsDirty = true;
        if (markParents == MarkingBehavior.MarkContainerChain)
            MarkContainerChainForLayout();
    }

    public void ClearIntrinsicLogicalWidthsDirty() => _intrinsicLogicalWidthsDirty = false;

    public void SetNeedsOverflowRecalc() => _needsOverflowRecalc = true;
    public void ClearNeedsOverflowRecalc() => _needsOverflowRecalc = false;

    public void SetShouldDoFullPaintInvalidation(PaintInvalidationReason reason = PaintInvalidationReason.Layout) => InvalidatePaint(SharedPaintInvalidatorContext.Value);
    public void SetShouldCheckForPaintInvalidation() => InvalidatePaint(SharedPaintInvalidatorContext.Value);

    private static readonly System.Threading.ThreadLocal<PaintInvalidatorContext> SharedPaintInvalidatorContext =
        new(() => new PaintInvalidatorContext());

    // ===== Positioning / formatting flags =====

    public void SetPositionState(PositionType position)
    {
        switch (position)
        {
            case PositionType.Static:
                _positionedState = PositionedState.IsStaticallyPositioned;
                break;
            case PositionType.Relative:
                _positionedState = PositionedState.IsRelativelyPositioned;
                break;
            case PositionType.Absolute:
            case PositionType.Fixed:
                _positionedState = PositionedState.IsOutOfFlowPositioned;
                break;
            case PositionType.Sticky:
                _positionedState = PositionedState.IsStickyPositioned;
                break;
            default:
                _positionedState = PositionedState.IsStaticallyPositioned;
                break;
        }
    }

    public void ClearPositionedState() => _positionedState = PositionedState.IsStaticallyPositioned;

    public void SetFloating(bool isFloating) => _isFloating = isFloating;
    public void SetInline(bool isInline) => _isInline = isInline;
    public void SetIsAtomicInlineLevel(bool value) => _isAtomicInlineLevel = value;
    public void SetHorizontalWritingMode(bool value) => _horizontalWritingMode = value;
    public void SetHasLayer(bool value) => _hasLayer = value;
    public void SetHasNonVisibleOverflow(bool value) => _hasNonVisibleOverflow = value;
    public bool HasNonVisibleOverflow => _hasNonVisibleOverflow;
    public void SetHasBoxDecorationBackground(bool value) => _hasBoxDecorationBackground = value;
    public bool HasBoxDecorationBackground => _hasBoxDecorationBackground;
    public void SetHasTransformRelatedProperty(bool value) => _hasTransformRelatedProperty = value;

    public void SetOverflowClipAxes(OverflowClipAxes axes) => _overflowClipAxes = axes;
    public OverflowClipAxes GetOverflowClipAxes() => _overflowClipAxes;
    public bool ShouldClipOverflowAlongEitherAxis() => _overflowClipAxes != OverflowClipAxes.None;
    public bool ShouldClipOverflowAlongBothAxis() => _overflowClipAxes == OverflowClipAxes.BothAxis;

    public bool IsHorizontalWritingMode => _horizontalWritingMode;

    // ===== Children management hooks =====

    public virtual bool IsChildAllowed(LayoutObject? child, ComputedStyle style) => true;
    public virtual void AddChild(LayoutObject? newChild, LayoutObject? beforeChild = null)
    {
        if (newChild == null) return;
        Children.Add(newChild);
        newChild.Parent = this;
    }

    public virtual void AddChildIgnoringContinuation(LayoutObject? newChild, LayoutObject? beforeChild = null) =>
        AddChild(newChild, beforeChild);

    public virtual void RemoveChild(LayoutObject child)
    {
        Children.Remove(child);
        child.Parent = null;
    }

    public virtual void WillBeDestroyed() { }
    public virtual void StyleDidChange(StyleDifference difference, ComputedStyle? oldStyle) { }
    public virtual void InsertedIntoTree() { }
    public virtual void TextDidChange() { }
    public virtual void IntrinsicSizeChanged() { }
    public virtual void ImageChanged(WrappedImagePtr? image, CanDeferInvalidation defer) { }
    public virtual void UpdateFromElement() { }
    public virtual void UpdateAfterLayout() { }
    public virtual void PaintReplaced(PaintInfo? paintInfo, PhysicalOffset paintOffset) { }
    public virtual void RecalcScrollableOverflow() { }
    public virtual void ComputeIntrinsicSizingInfo(IntrinsicSizingInfo? info) { }
    public virtual void UpdateHitTestResult(HitTestResult? result, PhysicalOffset offset) { }
    public virtual void TransformAndSecureOriginalText() { }
    public virtual string OriginalText() => this is LayoutText t ? t.Text : "";
    public virtual string PlainText() => this is LayoutText t ? t.Text : "";
    public virtual LayoutText? GetFirstLetterPart() => null;
    public virtual uint TextStartOffset => 0;
    public virtual uint OwnerNodeId => 0;
    public virtual int CaretMinOffset() => 0;
    public virtual int CaretMaxOffset() => 0;
    public virtual uint? CaretOffsetForPosition(Position? position) => null;
    public virtual Position? PositionForCaretOffset(uint offset) => null;
    public virtual OverflowClipAxes ComputeOverflowClipAxes() => OverflowClipAxes.BothAxis;
    public virtual PaintLayerType LayerTypeRequired() => PaintLayerType.NoPaintLayer;
    public virtual CompositingReasons AdditionalCompositingReasons() => CompositingReasons.None;
    public virtual void RemoveLeftoverAnonymousBlock(LayoutBlock? block) { }
    public virtual PositionWithAffinity? PositionForPoint(PhysicalOffset point) => null;
    protected virtual uint NonCollapsedCaretMaxOffset() => (uint)(this is LayoutText t ? t.Text.Length : 0);
    protected virtual char PreviousCharacter() => '\0';
    public virtual bool RespectsCSSOverflow() => true;

    public virtual bool BackgroundIsKnownToBeOpaqueInRect(PhysicalRect rect) => false;
    public virtual bool ForegroundIsKnownToBeOpaqueInRect(PhysicalRect rect, uint depth) => false;

    public virtual void WillBeRemovedFromTree() { }
    public virtual void InvalidatePaint() { }
    public virtual void InvalidatePaint(PaintInvalidatorContext? context) { }

    public virtual LayoutObject? FirstChild => null;
    public virtual LayoutObject? LastChild => null;
    public virtual int ChildrenCount => 0;

    // ===== Paint =====

    /// <summary>Paints this object. Mirrors LayoutObject::Paint().</summary>
    public virtual void Paint(PaintInfo? paintInfo) { }

    // ===== Hit testing =====

    public virtual bool VisibleToHitTesting() => StyleRef().Visibility == VisibilityType.Visible;

    /// <summary>Hit test this object and its descendants in all phases. Mirrors HitTestAllPhases().</summary>
    public virtual bool HitTestAllPhases(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        if (result == null)
            return false;
        if (!VisibleToHitTesting())
            return false;
        var localPoint = hitTestLocation.Point - accumulatedOffset;
        bool hit = NodeAtPoint(result, hitTestLocation, accumulatedOffset);
        if (hit)
            UpdateHitTestResult(result, localPoint);
        return hit;
    }

    /// <summary>Hit test this object (self phase). Mirrors NodeAtPoint().</summary>
    public virtual bool NodeAtPoint(HitTestResult? result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        if (result == null)
            return false;
        if (Node == null)
            return false;
        var rect = new PhysicalRect(accumulatedOffset, new PhysicalSize(0, 0));
        if (!hitTestLocation.Intersects(rect))
            return false;
        result.SetNodeAndPosition(Node, hitTestLocation.Point);
        return true;
    }

    // ===== Coordinate mapping =====

    /// <summary>Convert a point in this object's local space to ancestor space. Mirrors LocalToAncestorPoint().</summary>
    public virtual PhysicalOffset LocalToAncestorPoint(PhysicalOffset point, LayoutObject? ancestor)
    {
        PhysicalOffset result = point;
        LayoutObject? current = this;
        while (current != null && current != ancestor)
        {
            var container = current.Container();
            if (container == null)
                break;
            result += current.OffsetFromContainer(container);
            current = container;
        }
        return result;
    }

    /// <summary>Shorthand of LocalToAncestorPoint with a null ancestor.</summary>
    public PhysicalOffset LocalToAbsolutePoint(PhysicalOffset point) => LocalToAncestorPoint(point, null);

    /// <summary>Convert a rect in this object's local space to ancestor space. Mirrors LocalToAncestorRect().</summary>
    public PhysicalRect LocalToAncestorRect(PhysicalRect rect, LayoutObject? ancestor)
    {
        var offset = LocalToAncestorPoint(rect.Offset, ancestor);
        return new PhysicalRect(offset, rect.Size);
    }

    public PhysicalRect LocalToAbsoluteRect(PhysicalRect rect) => LocalToAncestorRect(rect, null);

    /// <summary>Convert a rect in ancestor coordinates to this object's local space.</summary>
    public PhysicalRect AncestorToLocalRect(PhysicalRect rect, LayoutObject? ancestor)
    {
        PhysicalOffset result = rect.Offset;
        LayoutObject? current = this;
        while (current != null && current != ancestor)
        {
            var container = current.Container();
            if (container == null)
                break;
            result -= current.OffsetFromContainer(container);
            current = container;
        }
        return new PhysicalRect(result, rect.Size);
    }

    public PhysicalRect AbsoluteToLocalRect(PhysicalRect rect) => AncestorToLocalRect(rect, null);
    public PhysicalOffset AbsoluteToLocalPoint(PhysicalOffset point) => AncestorToLocalRect(new PhysicalRect(point, PhysicalSize.Zero), null).Offset;

    /// <summary>Return the offset from |container| to this object. Mirrors OffsetFromContainer().</summary>
    public virtual PhysicalOffset OffsetFromContainer(LayoutObject? container, int mode = 0)
    {
        if (container == null || this is not AuroraBox box)
            return PhysicalOffset.Zero;
        if (box.ContainingBlock() != container && box.Container() != container)
            return PhysicalOffset.Zero;
        return box.FrameLocation;
    }

    /// <summary>Returns the bounding box of this object in absolute coordinates.</summary>
    public PhysicalRect AbsoluteBoundingBoxRect()
    {
        if (this is not AuroraBox box)
            return new PhysicalRect(LocalToAbsolutePoint(PhysicalOffset.Zero), PhysicalSize.Zero);
        return LocalToAbsoluteRect(box.BorderBoxRect);
    }

    public override string ToString()
    {
        var nodeInfo = Node is Element el ? $"<{el.TagName}>" : Node?.NodeName ?? "anonymous";
        return $"{GetName()} {nodeInfo}";
    }

    // ===== Factory =====

    /// <summary>
    /// Creates the appropriate LayoutObject based on the style, in particular
    /// 'display' and 'content'. "display: none" and "display: contents" are the
    /// only times this function returns null. Mirrors LayoutObject::CreateObject().
    /// </summary>
    public static LayoutObject? CreateObject(Element? element, ComputedStyle? style)
    {
        if (element == null || style == null)
            return null;
        if (style.Display == DisplayType.None || style.Display == DisplayType.Contents)
            return null;

        // Special-cased elements with a default association to a specific
        // LayoutObject (mirrors the engine's tag->renderer defaults).
        switch (element.TagName)
        {
            case "IMG":
                return new LayoutImage(element);
            case "CANVAS":
                return new LayoutHTMLCanvas(element as HTMLCanvasElement);
            case "VIDEO":
                return new LayoutVideo(element as HTMLVideoElement);
            case "AUDIO":
                return new LayoutMedia(element as HTMLMediaElement);
            case "IFRAME":
                return new LayoutIFrame(element as HTMLIFrameElement);
            case "EMBED":
            case "OBJECT":
                return new LayoutEmbeddedContent(element as HtmlElement);
            case "FRAME":
                return new LayoutFrame(element as HtmlElement);
            case "FRAMESET":
                return new LayoutFrameSet(element);
            case "PROGRESS":
                return new LayoutProgress(element as HTMLProgressElement);
        }

        if (element is HTMLBRElement)
            return new LayoutBR(element as HTMLBRElement);

        return CreateBlockFlowOrListItem(element, style);
    }

    /// <summary>
    /// Creates a block-flow (or list-item / inline) layout object for the given
    /// element. Mirrors LayoutObject::CreateBlockFlowOrListItem().
    /// </summary>
    public static LayoutObject CreateBlockFlowOrListItem(Element? element, ComputedStyle? style)
    {
        if (style != null && style.Display == DisplayType.ListItem)
            return new LayoutBlockFlow(element);
        if (style != null && (style.Display == DisplayType.Inline || style.Display == DisplayType.InlineFlex ||
                              style.Display == DisplayType.InlineGrid || style.Display == DisplayType.InlineBlock ||
                              style.Display == DisplayType.TableCell))
            return new LayoutInline(element);
        return new LayoutBlockFlow(element);
    }
}

/// <summary>
/// LayoutText - text node layout object.
/// Mirrors layout_text.cc.
/// </summary>
public class LayoutText : LayoutObject
{
    private string _text;

    public LayoutText(Node? node, string text) : base(node)
    {
        _text = text;
    }

    public override string GetName() => "LayoutText";
    public override bool IsText => true;
    public override bool IsInline => true;

    public string Text => _text;
    public void SetText(string text) => _text = text;

    public float MinPreferredLogicalWidth { get; set; }
    public float MaxPreferredLogicalWidth { get; set; }

    public override List<LayoutObject> Children => new();

    public static LayoutText CreateEmptyAnonymous(Document document) =>
        new(null, "");
}

/// <summary>
/// LayoutView - root layout view.
/// Mirrors layout_view.cc.
/// </summary>
public class LayoutView : LayoutBlockFlow
{
    public LayoutView(Document document) : base(document)
    {
    }

    public override string GetName() => "LayoutView";
}

// PaintInvalidationReason is declared in PaintInvalidator.cs.