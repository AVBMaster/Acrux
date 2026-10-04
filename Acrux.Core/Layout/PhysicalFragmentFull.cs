using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Acrux.Core.Layout.Inline;
using Geom = Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// OofData storage for out-of-flow positioned descendants and anchor queries.
/// Mirrors PhysicalFragment::OofData in physical_fragment.h.
/// </summary>
public class OofData
{
    public List<PhysicalOofPositionedNode> OofPositionedDescendants { get; set; } = new();
    public PhysicalAnchorQuery AnchorQuery { get; set; } = new();

    public bool HasOutOfFlowPositionedDescendants() => OofPositionedDescendants.Count > 0;
    public bool HasAnchorQuery() => !AnchorQuery.IsEmpty();
}

/// <summary>
/// Full physical fragment extension methods.
/// </summary>
public static class PhysicalFragmentExtensions
{
    public static bool IsContainer(this PhysicalFragment f) => f.Type == PhysicalFragment.FragmentType.FragmentBox || f.Type == PhysicalFragment.FragmentType.FragmentLineBox;

    public static PhysicalFragment.BoxType GetBoxType(this PhysicalFragment f)
    {
        System.Diagnostics.Debug.Assert(f.IsBox);
        return f.Box;
    }

    public static bool IsInlineBox(this PhysicalFragment f) => f.IsBox && f.Box == PhysicalFragment.BoxType.InlineBox;
    public static bool IsColumnBox(this PhysicalFragment f) => f.IsBox && f.Box == PhysicalFragment.BoxType.ColumnBox;
    public static bool IsFragmentainerBoxType(this PhysicalFragment f, PhysicalFragment.BoxType type) => type == PhysicalFragment.BoxType.ColumnBox || type == PhysicalFragment.BoxType.PageArea;

    public static bool IsColumnSpanAll(this PhysicalFragment f)
    {
        if (f.GetLayoutObject() is AuroraBox box)
            return box.IsColumnSpanAll;
        return false;
    }

    public static bool IsAtomicInline(this PhysicalFragment f) => f.IsBox && f.Box == PhysicalFragment.BoxType.AtomicInline;
    public static bool IsFloating(this PhysicalFragment f) => f.IsBox && f.Box == PhysicalFragment.BoxType.Floating;
    public static bool IsOutOfFlowPositioned(this PhysicalFragment f) => f.IsBox && f.Box == PhysicalFragment.BoxType.OutOfFlowPositioned;
    public static bool IsFloatingOrOutOfFlowPositioned(this PhysicalFragment f) => f.IsFloating() || f.IsOutOfFlowPositioned();
    public static bool IsFixedPositioned(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject is AuroraBox ngBox && ngBox.IsFixedPositioned();
    public static bool IsPositioned(this PhysicalFragment f) => f.LayoutObject?.IsPositioned ?? false;
    public static bool IsLineForParallelFlow(this PhysicalFragment f) => f.IsLineForParallelFlow;
    public static bool IsInline(this PhysicalFragment f) => f.IsInlineBox() || f.IsAtomicInline();

    public static bool IsBlockFlow(this PhysicalFragment f)
    {
        if (f.IsLineBox) return false;
        return f.LayoutObject is LayoutBlockFlow;
    }

    public static bool IsAnonymousBlock(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject?.IsAnonymous == true;
    public static bool IsFrameSet(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject.IsFrameSet();
    public static bool IsListMarker(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject.IsLayoutOutsideListMarker();
    public static bool IsSvg(this PhysicalFragment f) => f.LayoutObject.IsSVG();
    public static bool IsSvgText(this PhysicalFragment f) => f.LayoutObject.IsSVGText();
    public static bool IsRenderedLegend(this PhysicalFragment f) => f.IsBox && f.Box == PhysicalFragment.BoxType.RenderedLegend;
    public static bool IsMathML(this PhysicalFragment f) => f.IsBox && f.GetSelfOrContainerLayoutObject()?.IsMathML() == true;
    public static bool IsTablePart(this PhysicalFragment f) => f.IsTablePart;
    public static bool IsTable(this PhysicalFragment f) => f.IsTablePart && f.LayoutObject.IsTable();
    public static bool IsTableRow(this PhysicalFragment f) => f.IsTablePart && f.LayoutObject.IsTableRow();
    public static bool IsTableSection(this PhysicalFragment f) => f.IsTablePart && f.LayoutObject.IsTableSection();
    public static bool IsTableCell(this PhysicalFragment f) => f.IsTablePart && f.LayoutObject.IsTableCell();
    public static bool IsGrid(this PhysicalFragment f) => f.LayoutObject.IsLayoutGrid();
    public static bool IsFieldsetContainer(this PhysicalFragment f) => f.IsFieldsetContainer;
    public static bool IsFormattingContextRoot(this PhysicalFragment f) => f.IsBox && f.Box >= PhysicalFragment.BoxType.AtomicInline;
    public static bool IsPaintedAtomically(this PhysicalFragment f) => f.IsPaintedAtomically;
    public static bool HasCollapsedBorders(this PhysicalFragment f) => f.HasCollapsedBorders;
    public static bool IsHiddenForPaint(this PhysicalFragment f) => f.IsHiddenForPaint || (f.LayoutObject != null && f.LayoutObject.IsTruncated());
    public static bool IsOpaque(this PhysicalFragment f) => f.IsOpaque;
    public static bool IsFirstForNode(this PhysicalFragment f) => f.IsFirstForNode;
    public static bool IsLastForNode(this PhysicalFragment f) => f.IsLastForNode;
    public static bool IsImplicitAnchor(this PhysicalFragment f) => f.GetNode() is Element element && element.HasImplicitlyAnchoredElement();

    public static bool HasStickyConstrainedPosition(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.StyleRef().HasStickyConstrainedPosition();
    public static bool IsSnapArea(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject is AuroraBox && f.LayoutObject.StyleRef().GetScrollSnapAlign() != default;

    public static ComputedStyle Style(this PhysicalFragment f) => f.LayoutObject?.EffectiveStyle(f.GetStyleVariant()) ?? ComputedStyle.CreateDefault();
    public static StyleVariant GetStyleVariant(this PhysicalFragment f) => f.StyleVariant;
    public static bool UsesFirstLineStyle(this PhysicalFragment f) => LayoutUnitUtils.UsesFirstLineStyle(f.GetStyleVariant());

    public static PhysicalSize Size(this PhysicalFragment f) => f.Size;
    public static PhysicalRect LocalRect(this PhysicalFragment f) => new PhysicalRect(PhysicalOffset.Zero, f.Size);
    public static Document GetDocument(this PhysicalFragment f) => f.LayoutObject?.GetDocument() ?? new Document();
    public static Node? GetNode(this PhysicalFragment f) => f.IsCSSBox() ? f.LayoutObject?.GetNode() : null;
    public static Node? GeneratingNode(this PhysicalFragment f) => f.IsCSSBox() ? f.LayoutObject?.GeneratingNode() : null;
    public static Node? NodeForHitTest(this PhysicalFragment f) => f.IsFragmentainerBox() ? null : f.LayoutObject?.NodeForHitTest();
    public static Node? NonPseudoNode(this PhysicalFragment f) => f.IsCSSBox() ? f.LayoutObject?.NonPseudoNode() : null;

    public static bool HasLayer(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject is LayoutBoxModelObject;
    public static bool HasSelfPaintingLayer(this PhysicalFragment f) => f.HasLayer() && f.LayoutObject != null && f.LayoutObject.HasSelfPaintingLayer();
    public static bool HasNonVisibleOverflow(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.HasNonVisibleOverflow();

    public static OverflowClipAxes GetOverflowClipAxes(this PhysicalFragment f)
    {
        if (!f.IsCSSBox()) return OverflowClipAxes.None;
        return f.LayoutObject?.GetOverflowClipAxes() ?? OverflowClipAxes.None;
    }

    public static bool HasNonVisibleBlockOverflow(this PhysicalFragment f)
    {
        var clipAxes = f.GetOverflowClipAxes();
        if (f.Style().IsHorizontalWritingMode())
            return (clipAxes & OverflowClipAxes.Y) != 0;
        return (clipAxes & OverflowClipAxes.X) != 0;
    }

    public static bool IsScrollContainer(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.IsScrollContainer;
    public static bool IsEffectiveRootScroller(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.IsEffectiveRootScroller();
    public static bool ShouldApplyLayoutContainment(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.ShouldApplyLayoutContainment();
    public static bool ShouldClipOverflowAlongEitherAxis(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.ShouldClipOverflowAlongEitherAxis();
    public static bool ShouldClipOverflowAlongBothAxis(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.ShouldClipOverflowAlongBothAxis();
    public static bool ShouldApplyOverflowClipMargin(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.ShouldApplyOverflowClipMargin();
    public static bool CanTraverse(this PhysicalFragment f) => f.LayoutObject != null && f.LayoutObject.CanTraversePhysicalFragments();

    public static LayoutObject? GetLayoutObject(this PhysicalFragment f) => f.IsCSSBox() ? f.LayoutObject : null;
    public static LayoutObject? GetMutableLayoutObject(this PhysicalFragment f) => f.IsCSSBox() ? f.LayoutObject : null;
    public static LayoutObject? GetSelfOrContainerLayoutObject(this PhysicalFragment f) => f.LayoutObject;
    public static bool IsLayoutObjectDestroyedOrMoved(this PhysicalFragment f) => f.LayoutObject == null;
    public static void LayoutObjectWillBeDestroyed(this PhysicalFragment f) => f.LayoutObject = null;

    public static PhysicalFragment? PostLayout(this PhysicalFragment f)
    {
        if (f is PhysicalBoxFragment boxFrag)
            return boxFrag.PostLayout();
        return f;
    }

    public static LogicalRect ConvertChildToLogical(this PhysicalFragment f, PhysicalRect rect)
    {
        return new WritingModeConverter(new WritingDirectionMode(f.Style().GetWritingMode(), f.Style().GetDirection()), f.Size).ToLogical(rect);
    }

    public static bool HasFloatingDescendantsForPaint(this PhysicalFragment f) => f.HasFloatingDescendantsForPaint;
    public static bool HasAdjoiningObjectDescendants(this PhysicalFragment f) => f.HasAdjoiningObjectDescendants;
    public static bool DependsOnPercentageBlockSize(this PhysicalFragment f) => f.DependsOnPercentageBlockSize;
    public static bool HasOutOfFlowFragmentChild(this PhysicalFragment f) => f.HasOutOfFlowFragmentChildFlag;
    public static bool HasOutOfFlowInFragmentainerSubtree(this PhysicalFragment f) => f.HasOutOfFlowInFragmentainerSubtree;

    public static bool HasOutOfFlowPositionedDescendants(this PhysicalFragment f) => f.OofDataInternal != null && f.OofDataInternal.HasOutOfFlowPositionedDescendants();
    public static List<PhysicalOofPositionedNode> OutOfFlowPositionedDescendants(this PhysicalFragment f)
    {
        if (!f.HasOutOfFlowPositionedDescendants())
            return new List<PhysicalOofPositionedNode>();
        return f.OofDataInternal!.OofPositionedDescendants;
    }

    public static bool HasAnchorQuery(this PhysicalFragment f) => f.OofDataInternal != null && f.OofDataInternal.HasAnchorQuery();
    public static bool HasAnchorQueryToPropagate(this PhysicalFragment f) => f.HasAnchorQuery() || f.Style().AnchorName() != null || f.IsImplicitAnchor();
    public static PhysicalAnchorQuery? AnchorQuery(this PhysicalFragment f)
    {
        if (!f.HasAnchorQuery()) return null;
        return f.OofDataInternal!.AnchorQuery;
    }

    public static FragmentedOofData? GetFragmentedOofData(this PhysicalFragment f) => f.FragmentedOofDataInternal;

    public static bool HasNestedMulticolsWithOOFs(this PhysicalFragment f)
    {
        var oofData = f.GetFragmentedOofData();
        return oofData != null && oofData.MulticolsWithPendingOofs.Count > 0;
    }

    public static bool NeedsOOFPositionedInfoPropagation(this PhysicalFragment f)
    {
        return f.OofDataInternal != null
            || (f.FragmentedOofDataInternal != null && f.FragmentedOofDataInternal.NeedsOOFPositionedInfoPropagation());
    }

    public static bool HasPropagatedLayoutObjects(this PhysicalFragment f)
        => f.PropagatedStickyDescendants() != null || f.PropagatedScrollStartTarget() != null || f.PropagatedSnapAreas() != null;

    public static List<LayoutBoxModelObject>? StickyDescendants(this PhysicalFragment f) => f.PropagatedDataInternal?.StickyDescendants;
    public static List<LayoutBoxModelObject>? PropagatedStickyDescendants(this PhysicalFragment f) => f.IsScrollContainer() ? null : f.StickyDescendants();
    public static LayoutObject? ScrollStartTarget(this PhysicalFragment f) => f.PropagatedDataInternal?.ScrollStartTarget;
    public static LayoutObject? PropagatedScrollStartTarget(this PhysicalFragment f) => f.IsScrollContainer() ? null : f.ScrollStartTarget();
    public static List<Element>? SnapAreas(this PhysicalFragment f) => f.PropagatedDataInternal?.SnapAreas;
    public static List<Element>? PropagatedSnapAreas(this PhysicalFragment f) => f.IsScrollContainer() ? null : f.SnapAreas();

    public static bool ChildrenValid(this PhysicalFragment f) => f.ChildrenValid;
    public static void SetChildrenInvalid(this PhysicalFragment f) => f.ChildrenValid = false;
    public static BreakToken? GetBreakToken(this PhysicalFragment f) => f.BreakToken;
    public static bool IsInitialLetterBox(this PhysicalFragment f) => f.IsCSSBox() && f.LayoutObject != null && f.LayoutObject.IsInitialLetterBox();
    public static bool IsMathMLFraction(this PhysicalFragment f) => f.IsMathMLFraction;
    public static bool IsMathMLOperator(this PhysicalFragment f) => f.IsMathMLOperator;
    public static bool MayHaveDescendantAboveBlockStart(this PhysicalFragment f) => f.MayHaveDescendantAboveBlockStart;

    public static bool IsInlineFormattingContext(this PhysicalFragment f)
    {
        return false;
    }

    public static FragmentItems? Items(this PhysicalFragment f)
    {
        return null;
    }
}

[Flags]
public enum OverflowClipAxes
{
    None = 0,
    X = 1,
    Y = 2,
    BothAxis = X | Y,
}

/// <summary>
/// PhysicalBoxFragmentFull - box fragment with all fields.
/// </summary>
public class PhysicalBoxFragmentFull : PhysicalBoxFragment
{
    public PhysicalBoxFragmentFull()
    {
        Type = PhysicalFragment.FragmentType.FragmentBox;
    }

    public bool IsFragmentainerBox { get; set; }
    public bool IsFieldsetContainer { get; set; }
    public bool IsTablePart { get; set; }
    public bool IsPaintedAtomically { get; set; }
    public bool HasCollapsedBorders { get; set; }
    public bool HasFirstBaseline { get; set; }
    public bool HasLastBaseline { get; set; }
    public bool UseLastBaselineForInlineBaseline { get; set; }
    public bool HasFragmentedOutOfFlowData { get; set; }
    public bool IsContentInline { get; set; }
    public int LineCount { get; set; }
    public bool IsMonolithic { get; set; }
    public bool IsInlineFormattingContext { get; set; }
    public bool IsTextControlContainer { get; set; }
    public bool IsTextControlPlaceholder { get; set; }
    public bool IsFirstForNode { get; set; }
    public bool IsLastForNode { get; set; }
    public FragmentItems? Items { get; set; }
    public PhysicalRect? ScrollableOverflow { get; set; }
    public PhysicalBoxStrut? Borders { get; set; }
    public PhysicalBoxStrut? Scrollbar { get; set; }
    public PhysicalBoxStrut? Padding { get; set; }
    public PhysicalRect? InflowBounds { get; set; }
    public PhysicalBoxStrut? Margins { get; set; }

    public PhysicalFragment? PostLayout()
    {
        return this;
    }
}

/// <summary>
/// PhysicalTextFragment for text content.
/// </summary>
public class PhysicalTextFragment : PhysicalFragment
{
    public PhysicalTextFragment()
    {
        Type = FragmentType.FragmentBox;
        Box = BoxType.AtomicInline;
    }

    public string Text { get; set; } = "";
    public ShapeResult? ShapeResult { get; set; }
}

/// <summary>
/// Extension methods for LayoutObject, ComputedStyle, etc. to support the fragment system.
/// </summary>
public static class LayoutObjectSupportExtensions
{
    public static bool IsFrameSet(this LayoutObject obj) => false;
    public static bool IsRenderedLegend(this LayoutObject obj) => obj.Node is Element el && el.TagName == "LEGEND";
    public static bool IsLayoutOutsideListMarker(this LayoutObject obj) => false;
    public static bool IsSVG(this LayoutObject obj) => false;
    public static bool IsSVGText(this LayoutObject obj) => false;
    public static bool IsMathML(this LayoutObject obj) => false;
    public static bool IsTable(this LayoutObject obj) => false;
    public static bool IsTableRow(this LayoutObject obj) => false;
    public static bool IsTableSection(this LayoutObject obj) => false;
    public static bool IsTableCell(this LayoutObject obj) => false;
    public static bool IsLayoutGrid(this LayoutObject obj) => false;
    public static bool IsTruncated(this LayoutObject obj) => false;
    public static bool IsInitialLetterBox(this LayoutObject obj) => false;
    public static bool IsEffectiveRootScroller(this LayoutObject obj) => false;
    public static bool ShouldApplyLayoutContainment(this LayoutObject obj) => false;
    public static bool ShouldClipOverflowAlongEitherAxis(this LayoutObject obj) => false;
    public static bool ShouldClipOverflowAlongBothAxis(this LayoutObject obj) => false;
    public static bool ShouldApplyOverflowClipMargin(this LayoutObject obj) => false;
    public static bool CanTraversePhysicalFragments(this LayoutObject obj) => true;
    public static bool HasSelfPaintingLayer(this LayoutObject obj) => false;
    public static bool HasNonVisibleOverflow(this LayoutObject obj) => false;
    public static OverflowClipAxes GetOverflowClipAxes(this LayoutObject obj) => OverflowClipAxes.None;
    public static Document GetDocument(this LayoutObject obj) => new Document();
    public static Node? GetNode(this LayoutObject obj) => obj.Node;
    public static Node? GeneratingNode(this LayoutObject obj) => obj.Node;
    public static Node? NodeForHitTest(this LayoutObject obj) => obj.Node;
    public static Node? NonPseudoNode(this LayoutObject obj) => obj.Node;
    public static bool IsBeforeInPreOrder(this LayoutObject a, LayoutObject b) => false;
    public static bool IsBeforeInPreOrder(this LayoutBox a, LayoutBox b) => false;
    public static bool MayHaveAnchorQuery(this LayoutBox box) => false;
    public static bool IsInTopOrViewTransitionLayer(this LayoutBox box) => false;

    public static ComputedStyle EffectiveStyle(this LayoutObject obj, StyleVariant variant) => obj.StyleRef();
    public static bool HasStickyConstrainedPosition(this ComputedStyle style) => false;
    public static object? GetScrollSnapAlign(this ComputedStyle style) => null;
    public static List<ScopedCSSName>? GetNames(this List<ScopedCSSName> names) => names;
    public static List<ScopedCSSName>? AnchorName(this ComputedStyle style) => null;
    public static EScrollStartTarget ScrollStartTarget(this ComputedStyle style) => EScrollStartTarget.None;
    public static bool IsHorizontalWritingMode(this ComputedStyle style) => true;
    public static TextDirection GetDirection(this ComputedStyle style) => TextDirection.Ltr;
    public static Geom.WritingMode GetWritingMode(this ComputedStyle style) => Geom.WritingMode.HorizontalTb;
    public static bool HasPercent(this Length length) => length is PercentLength;
    public static bool IsPageMarginBox(this ComputedStyle style) => false;
    public static bool IsPaginatedRoot(this LayoutInputNode node) => false;
    public static bool CreatesNewFormattingContext(this LayoutInputNode node) => node.Box?.IsFloating == true || node.Box?.IsScrollContainer == true;
    public static LayoutBox? GetLayoutBox(this LayoutInputNode node) => node.Box;
    public static bool IsAnonymous(this ConstraintSpace space) => false;
    public static bool IsFixedPositioned(this AuroraBox box) => box.IsPositioned && box.StyleRef().Position == PositionType.Fixed;
    public static bool IsVerticalWritingMode(this ComputedStyle style) => !style.IsHorizontalWritingMode();
    public static bool IsHorizontalWritingMode(this Geom.WritingMode mode) => mode == Geom.WritingMode.HorizontalTb;
    public static bool IsVerticalWritingMode(this Geom.WritingMode mode) => !mode.IsHorizontalWritingMode();
    public static bool IsFlippedLinesWritingMode(this Geom.WritingMode mode) => mode == Geom.WritingMode.VerticalRl;
    public static bool IsFlippedBlocksWritingMode(this Geom.WritingMode mode) => false;
    public static bool GetIsLineBreakInside(this InlineBreakToken token) => false;
    public static bool HasImplicitlyAnchoredElement(this Element element) => false;
    public static LayoutBox? GetLayoutBox(this Element element) => null;
    public static bool CanContainOutOfFlowPositionedElement(this LayoutInline inline, PositionType position) => false;
    public static bool CanContainFixedPositionObjects(this LayoutObject obj) => false;
    public static bool CanContainFixedPositionObjects(this LayoutInline inline) => false;
    public static LayoutObject? ContainingBlock(this LayoutInline inline) => inline.Parent;
    public static bool IsColumnSpanAll(this AuroraBox box) => false;
    public static bool HasOrthogonalFallbackInlineSize(this LayoutResult result) => false;
    public static bool HasOrthogonalFallbackSizeDescendant(this LayoutResult result) => false;
    public static PhysicalFragment? GetPhysicalFragment(this LayoutResult result)
    {
        return null;
    }
}

public enum EScrollStartTarget
{
    None,
    Top,
    Bottom,
    Left,
    Right,
}

public static class LogicalSizeExtensions
{
    public static LogicalSize ToLogicalSize(this PhysicalSize size, Geom.WritingMode mode)
    {
        if (mode == Geom.WritingMode.HorizontalTb)
            return new LogicalSize(size.Width, size.Height);
        return new LogicalSize(size.Height, size.Width);
    }
}