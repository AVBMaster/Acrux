using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Acrux.Core.Layout.Inline;

namespace Acrux.Core.Layout;

using ChildrenVector = System.Collections.Generic.List<LogicalFragmentLink>;
using MulticolCollection = System.Collections.Generic.Dictionary<LayoutBox, MulticolWithPendingOofs<LogicalOffset>>;

public class EarlyBreak
{
    public enum BreakType
    {
        Line,
        Block,
    }

    public BreakType Type { get; set; }
    public LayoutInputNode? Target { get; set; }
}

public enum AdjoiningObjectTypes
{
    None = 0,
    FloatLeft = 1,
    FloatRight = 2,
    OutOfFlow = 4,
}

/// <summary>
/// Status of a layout result. Mirrors LayoutResult::EStatus in layout_result.h.
/// </summary>
[Flags]
public enum EStatus
{
    Success = 0,
    NeedsRelayout = 1,
    BfcBlockOffsetResolved = 2,
    NeedsEarlierBreak = 4,
    OutOfFragmentainerSpace = 8,
    NeedsLineClampRelayout = 16,
    DisableFragmentation = 32,
    NeedsRelayoutWithNoChildScrollbarChanges = 64,
    TextBoxTrimEndDidNotApply = 128,
    // Save bits by using the same value for mutually exclusive results.
    NeedsRelayoutWithRowCrossSizeChanges = 256,
    NeedsRelayoutAsLastTableBox = 256,
}

public static class AdjoiningObjectTypeUtils
{
    public static readonly AdjoiningObjectTypes AdjoiningNone = AdjoiningObjectTypes.None;
}

public class LogicalAnchorQuery
{
    public enum SetOptions
    {
        InFlow,
        OutOfFlow,
    }

    public bool IsEmpty() => _anchors.Count == 0;
    public void Set(ScopedCSSName? name, LayoutObject obj, LogicalRect rect, SetOptions options, Element? context = null) { }
    public void Set(LayoutObject obj, LayoutObject anchor, LogicalRect rect, SetOptions options, Element? context = null) { }
    public void SetFromPhysical(PhysicalAnchorQuery query, WritingModeConverter converter, LogicalOffset offset, SetOptions options, Element? context = null) { }
    public void SetFromLogical(LogicalAnchorQuery query, WritingModeConverter converter) { }

    private readonly List<LogicalAnchorEntry> _anchors = new();

    private class LogicalAnchorEntry
    {
        public ScopedCSSName? Name;
        public LayoutObject? Object;
        public LayoutObject? Anchor;
        public LogicalRect Rect;
        public SetOptions Options;
        public Element? Context;
    }
}

public class PhysicalAnchorQuery
{
    public bool IsEmpty() => _anchors.Count == 0;
    public void SetFromLogical(LogicalAnchorQuery query, WritingModeConverter converter) { }

    private readonly List<PhysicalAnchorEntry> _anchors = new();

    private class PhysicalAnchorEntry
    {
        public LayoutObject? Object;
        public PhysicalRect Rect;
    }
}

public class ScopedCSSName
{
    public string Name { get; }
    public ScopedCSSName(string name) => Name = name;
}

public class MulticolWithPendingOofs<T>
{
    public T MulticolOffset { get; set; }
    public OofContainingBlock<T> FixedposContainingBlock { get; set; }
    public OofInlineContainer<T> FixedposInlineContainer { get; set; }

    public MulticolWithPendingOofs(T multicolOffset, OofContainingBlock<T> fixedposContainingBlock, OofInlineContainer<T> fixedposInlineContainer)
    {
        MulticolOffset = multicolOffset;
        FixedposContainingBlock = fixedposContainingBlock;
        FixedposInlineContainer = fixedposInlineContainer;
    }

    public MulticolWithPendingOofs()
    {
        MulticolOffset = default!;
        FixedposContainingBlock = default;
        FixedposInlineContainer = default;
    }
}

public class LogicalOofNodeForFragmentation
{
    public LayoutBox Box { get; }
    public LogicalStaticPosition StaticPosition { get; }
    public bool RequiresContentBeforeBreaking { get; }
    public bool IsHiddenForPaint { get; }
    public OofInlineContainer<LogicalOffset> InlineContainer { get; }
    public OofContainingBlock<LogicalOffset> ContainingBlock { get; }
    public OofContainingBlock<LogicalOffset> FixedposContainingBlock { get; }
    public OofInlineContainer<LogicalOffset> FixedposInlineContainer { get; }

    public LogicalOofNodeForFragmentation(LayoutBox box, LogicalStaticPosition staticPosition, bool requiresContentBeforeBreaking, bool isHiddenForPaint,
        OofInlineContainer<LogicalOffset> inlineContainer, OofContainingBlock<LogicalOffset> containingBlock,
        OofContainingBlock<LogicalOffset> fixedposContainingBlock, OofInlineContainer<LogicalOffset> fixedposInlineContainer)
    {
        Box = box;
        StaticPosition = staticPosition;
        RequiresContentBeforeBreaking = requiresContentBeforeBreaking;
        IsHiddenForPaint = isHiddenForPaint;
        InlineContainer = inlineContainer;
        ContainingBlock = containingBlock;
        FixedposContainingBlock = fixedposContainingBlock;
        FixedposInlineContainer = fixedposInlineContainer;
    }

    public LogicalOofNodeForFragmentation(LogicalOofPositionedNode node)
    {
        Box = node.Box;
        StaticPosition = node.StaticPosition;
        RequiresContentBeforeBreaking = node.RequiresContentBeforeBreaking;
        IsHiddenForPaint = node.IsHiddenForPaint;
        InlineContainer = node.InlineContainer;
        ContainingBlock = new OofContainingBlock<LogicalOffset>(LogicalOffset.Zero, null, null, false);
        FixedposContainingBlock = new OofContainingBlock<LogicalOffset>(LogicalOffset.Zero, null, null, false);
        FixedposInlineContainer = OofInlineContainer<LogicalOffset>.Empty;
    }
}

public class PhysicalOofNodeForFragmentation
{
    public LayoutBox Box { get; }
    public PhysicalStaticPosition StaticPosition { get; }
    public bool RequiresContentBeforeBreaking { get; }
    public bool IsHiddenForPaint { get; }
    public OofInlineContainer<PhysicalOffset> InlineContainer { get; }
    public OofContainingBlock<PhysicalOffset> ContainingBlock { get; }
    public OofContainingBlock<PhysicalOffset> FixedposContainingBlock { get; }
    public OofInlineContainer<PhysicalOffset> FixedposInlineContainer { get; }

    public PhysicalOofNodeForFragmentation(LayoutBox box, PhysicalStaticPosition staticPosition, bool requiresContentBeforeBreaking, bool isHiddenForPaint,
        OofInlineContainer<PhysicalOffset> inlineContainer, OofContainingBlock<PhysicalOffset> containingBlock,
        OofContainingBlock<PhysicalOffset> fixedposContainingBlock, OofInlineContainer<PhysicalOffset> fixedposInlineContainer)
    {
        Box = box;
        StaticPosition = staticPosition;
        RequiresContentBeforeBreaking = requiresContentBeforeBreaking;
        IsHiddenForPaint = isHiddenForPaint;
        InlineContainer = inlineContainer;
        ContainingBlock = containingBlock;
        FixedposContainingBlock = fixedposContainingBlock;
        FixedposInlineContainer = fixedposInlineContainer;
    }
}

public readonly struct PhysicalStaticPosition
{
    public PhysicalOffset Offset { get; }
    public LogicalStaticPosition.StaticInlinePosition InlinePosition { get; }
    public LogicalStaticPosition.StaticBlockPosition BlockPosition { get; }

    public PhysicalStaticPosition(PhysicalOffset offset, LogicalStaticPosition.StaticInlinePosition inlinePosition, LogicalStaticPosition.StaticBlockPosition blockPosition)
    {
        Offset = offset;
        InlinePosition = inlinePosition;
        BlockPosition = blockPosition;
    }
}

public readonly struct OofContainingBlock<T>
{
    public T Offset { get; }
    public T RelativeOffset { get; }
    public PhysicalFragment? Fragment { get; }
    public LayoutUnit? ClippedContainerBlockOffset { get; }
    public bool IsInsideColumnSpanner { get; }

    public OofContainingBlock(T offset, T relativeOffset, PhysicalFragment? fragment, LayoutUnit? clippedContainerBlockOffset, bool isInsideColumnSpanner)
    {
        Offset = offset;
        RelativeOffset = relativeOffset;
        Fragment = fragment;
        ClippedContainerBlockOffset = clippedContainerBlockOffset;
        IsInsideColumnSpanner = isInsideColumnSpanner;
    }

    public OofContainingBlock(T offset, PhysicalFragment? fragment, LayoutUnit? clippedContainerBlockOffset, bool isInsideColumnSpanner)
        : this(offset, default!, fragment, clippedContainerBlockOffset, isInsideColumnSpanner)
    {
    }

    public static readonly OofContainingBlock<T> Empty = new(default!, default!, null, null, false);
}

public class FragmentedOofData
{
    public List<PhysicalOofNodeForFragmentation> OofPositionedFragmentainerDescendants { get; set; } = new();
    public Dictionary<LayoutBox, MulticolWithPendingOofs<PhysicalOffset>> MulticolsWithPendingOofs { get; set; } = new();
    public bool NeedsOOFPositionedInfoPropagation() => OofPositionedFragmentainerDescendants.Count > 0 || MulticolsWithPendingOofs.Count > 0;
}

public static class LayoutUnitUtils
{
    public static readonly LayoutUnit IndefiniteSize = LayoutUnit.FromValue(float.NaN);
    public static readonly LayoutUnit Min = LayoutUnit.FromValue(float.MinValue);
    public static readonly LayoutUnit Max = LayoutUnit.FromValue(float.MaxValue);

    public static void UpdateMinimalSpaceShortage(LayoutUnit? spaceShortage, ref LayoutUnit minimalSpaceShortage)
    {
        if (!spaceShortage.HasValue)
            return;
        if (minimalSpaceShortage == IndefiniteSize || spaceShortage.Value < minimalSpaceShortage)
            minimalSpaceShortage = spaceShortage.Value;
    }

    public static bool UsesFirstLineStyle(StyleVariant variant) => variant == StyleVariant.FirstLineInherited || variant == StyleVariant.FirstLineOwned;
}

public readonly struct LayoutUnit : IComparable<LayoutUnit>
{
    public float Value { get; }
    public LayoutUnit(float value) => Value = value;
    public static LayoutUnit FromValue(float value) => new(value);
    public static LayoutUnit FromFloat(float value) => new(value);
    public static LayoutUnit operator +(LayoutUnit a, LayoutUnit b) => new(a.Value + b.Value);
    public static LayoutUnit operator -(LayoutUnit a, LayoutUnit b) => new(a.Value - b.Value);
    public static LayoutUnit operator -(LayoutUnit a) => new(-a.Value);
    public static LayoutUnit operator *(LayoutUnit a, float b) => new(a.Value * b);
    public static LayoutUnit operator /(LayoutUnit a, float b) => new(a.Value / b);
    public static LayoutUnit operator *(LayoutUnit a, LayoutUnit b) => new(a.Value * b.Value);
    public static LayoutUnit operator /(LayoutUnit a, LayoutUnit b) => new(a.Value / b.Value);
    public static bool operator ==(LayoutUnit a, LayoutUnit b) => float.IsNaN(a.Value) && float.IsNaN(b.Value) || a.Value == b.Value;
    public static bool operator !=(LayoutUnit a, LayoutUnit b) => !(a == b);
    public static bool operator <(LayoutUnit a, LayoutUnit b) => a.Value < b.Value;
    public static bool operator >(LayoutUnit a, LayoutUnit b) => a.Value > b.Value;
    public static bool operator <=(LayoutUnit a, LayoutUnit b) => a.Value <= b.Value;
    public static bool operator >=(LayoutUnit a, LayoutUnit b) => a.Value >= b.Value;
    public override bool Equals(object? obj) => obj is LayoutUnit other && this == other;
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => float.IsNaN(Value) ? "indefinite" : Value.ToString("F2");
    public static LayoutUnit Min(LayoutUnit a, LayoutUnit b) => new(Math.Min(a.Value, b.Value));
    public static LayoutUnit Max(LayoutUnit a, LayoutUnit b) => new(Math.Max(a.Value, b.Value));
    public float ToFloat() => Value;
    public int ToInt() => (int)Value;
    public float Abs() => Math.Abs(Value);
    public float Round() => (float)Math.Round(Value);
    public float Ceil() => (float)Math.Ceiling(Value);
    public float Floor() => (float)Math.Floor(Value);
    public LayoutUnit ClampNegativeToZero() => new(Math.Max(0, Value));
    public static LayoutUnit FromFloatRound(float value) => new((float)Math.Round(value));
    public static readonly LayoutUnit MinSentinel = new(float.MinValue);
    public static readonly LayoutUnit MaxSentinel = new(float.MaxValue);
    public static implicit operator float(LayoutUnit u) => u.Value;
    public static explicit operator int(LayoutUnit u) => (int)u.Value;

    public int CompareTo(LayoutUnit other) => Value.CompareTo(other.Value);

    public LayoutUnit AddEpsilon() => new(Value + float.Epsilon);
    public static LayoutUnit FromFloatCeil(float v) => new((float)Math.Ceiling(v));
    public static LayoutUnit FromFloatFloor(float v) => new((float)Math.Floor(v));
}

public static class LayoutUnitExtensions
{
    public static LayoutUnit ClampSizeToMinAndMax(this MinMaxSizes minMax, LayoutUnit size)
    {
        return new LayoutUnit(Math.Clamp(size.Value, minMax.MinSize, minMax.MaxSize));
    }
    public static LayoutUnit ClampIndefiniteToZero(this LayoutUnit size)
    {
        return float.IsNaN(size.Value) ? LayoutUnit.FromValue(0) : size;
    }
}

public class PropagatedData
{
    public List<LayoutBoxModelObject>? StickyDescendants { get; set; }
    public List<Element>? SnapAreas { get; set; }
    public LayoutObject? ScrollStartTarget { get; set; }
}

public class FragmentBuilder
{

    public ComputedStyle Style
    {
        get
        {
            System.Diagnostics.Debug.Assert(_style != null);
            return _style;
        }
    }

    public void SetStyleVariant(StyleVariant styleVariant) => _styleVariant = styleVariant;

    public StyleVariant GetStyleVariant() => _styleVariant;

    public ConstraintSpace GetConstraintSpace() => _space;

    public WritingDirectionMode GetWritingDirection() => _writingDirection;
    public Geometry.WritingMode GetWritingMode() => _writingDirection.WritingMode;
    public TextDirection Direction() => _writingDirection.Direction;

    public bool IsRoot() => _node != null && _node.Box is LayoutView && !_space.IsAnonymous();

    public bool IsPaginatedRoot() => IsRoot() && _node.IsPaginatedRoot();

    public BreakToken? PreviousBreakToken => _previousBreakToken;

    public void SetIsNewFormattingContext(bool isNewFc) => _isNewFc = isNewFc;

    public PhysicalFragment.BoxType GetBoxType()
    {
        if (_boxType != PhysicalFragment.BoxType.NormalBox)
            return _boxType;

        System.Diagnostics.Debug.Assert(_layoutObject != null);
        if (_layoutObject.IsFloating)
            return PhysicalFragment.BoxType.Floating;
        if (_layoutObject.IsOutOfFlowPositioned)
            return PhysicalFragment.BoxType.OutOfFlowPositioned;
        if (_layoutObject.IsRenderedLegend())
            return PhysicalFragment.BoxType.RenderedLegend;
        if (_layoutObject.StyleRef().IsPageMarginBox())
            return PhysicalFragment.BoxType.PageMargin;
        if (_layoutObject.IsInline)
        {
            if (_layoutObject.IsAtomicInlineLevel)
                return PhysicalFragment.BoxType.AtomicInline;
            return PhysicalFragment.BoxType.InlineBox;
        }
        System.Diagnostics.Debug.Assert(_node != null, "Must call SetBoxType if there is no node");
        System.Diagnostics.Debug.Assert(_isNewFc == _node.CreatesNewFormattingContext(), "Forgot to call builder.SetIsNewFormattingContext");
        if (_isNewFc)
            return PhysicalFragment.BoxType.BlockFlowRoot;
        return PhysicalFragment.BoxType.NormalBox;
    }

    public void SetBoxType(PhysicalFragment.BoxType boxType) => _boxType = boxType;

    public bool IsFragmentainerBoxType()
    {
        var boxType = GetBoxType();
        return boxType == PhysicalFragment.BoxType.ColumnBox || boxType == PhysicalFragment.BoxType.PageArea;
    }

    public LayoutUnit InlineSize => LayoutUnit.FromValue(_size.InlineSize);
    public LayoutUnit BlockSize
    {
        get
        {
            System.Diagnostics.Debug.Assert(_size.BlockSize != LayoutUnitUtils.IndefiniteSize.Value);
            return LayoutUnit.FromValue(_size.BlockSize);
        }
    }

    public LogicalSize Size
    {
        get
        {
            System.Diagnostics.Debug.Assert(_size.BlockSize != LayoutUnitUtils.IndefiniteSize.Value);
            return _size;
        }
    }

    public void SetBlockSize(LayoutUnit blockSize) => _size = new LogicalSize(_size.InlineSize, blockSize.Value);

    public bool HasBlockSize => _size.BlockSize != LayoutUnitUtils.IndefiniteSize.Value;

    public void SetIsHiddenForPaint(bool value) => _isHiddenForPaint = value;
    public void SetIsOpaque() => _isOpaque = true;
    public void SetHasCollapsedBorders(bool value) => _hasCollapsedBorders = value;

    public LayoutObject? GetLayoutObject() => _layoutObject;

    public LayoutUnit BfcLineOffset => _bfcLineOffset;
    public void SetBfcLineOffset(LayoutUnit offset) => _bfcLineOffset = offset;

    public LayoutUnit? BfcBlockOffset => _bfcBlockOffset;
    public void SetBfcBlockOffset(LayoutUnit offset) => _bfcBlockOffset = offset;
    public void ResetBfcBlockOffset() => _bfcBlockOffset = null;

    public void SetEndMarginStrut(MarginStrut strut) => _endMarginStrut = strut;

    public void SetMayHaveDescendantAboveBlockStart(bool b) => _mayHaveDescendantAboveBlockStart = b;

    public ExclusionSpace GetExclusionSpace() => _exclusionSpace;
    public void SetExclusionSpace(ExclusionSpace space) => _exclusionSpace = space;

    public void SetLinesUntilClamp(int? value) => _linesUntilClamp = value;

    public bool IsBlockEndTrimmableLine => _isBlockEndTrimmableLine;
    public void SetIsBlockEndTrimmableLine() => _isBlockEndTrimmableLine = true;

    public UnpositionedListMarker GetUnpositionedListMarker() => _unpositionedListMarker;
    public void SetUnpositionedListMarker(UnpositionedListMarker marker) => _unpositionedListMarker = marker;
    public void ClearUnpositionedListMarker() => _unpositionedListMarker = new UnpositionedListMarker(BlockNode.Null);

    public void ReplaceChild(int index, PhysicalFragment newChild, LogicalOffset offset)
    {
        System.Diagnostics.Debug.Assert(index < _children.Count);
        _children[index] = new LogicalFragmentLink(offset, newChild);
    }

    public ChildrenVector Children => _children;

    public bool HasItems => _itemsBuilder != null;
    public FragmentItemsBuilder? ItemsBuilder => _itemsBuilder;
    public void SetItemsBuilder(FragmentItemsBuilder? builder) => _itemsBuilder = builder;

    public void PropagateStickyDescendants(PhysicalFragment child)
    {
        if (child.HasStickyConstrainedPosition())
        {
            EnsureStickyDescendants().Insert(0, (LayoutBoxModelObject)child.GetMutableLayoutObject()!);
        }
        if (child.PropagatedStickyDescendants() is { } childStickyDescendants)
        {
            EnsureStickyDescendants().AddRange(childStickyDescendants);
        }
    }

    public void PropagateSnapAreas(PhysicalFragment child)
    {
        if (child.IsSnapArea())
        {
            if (child is PhysicalBoxFragment boxFrag && boxFrag.BreakToken == null)
            {
                var snapArea = (Element)child.GetLayoutObject()!.GetNode()!;
                var insertionPos = GetSnapInsertionPosition(snapArea);
                EnsureSnapAreas().Insert((int)insertionPos, snapArea);
            }
        }
        if (child.PropagatedSnapAreas() is { } childSnapAreas)
        {
            var insertionPos = GetSnapInsertionPosition(childSnapAreas[0]);
            EnsureSnapAreas().InsertRange((int)insertionPos, childSnapAreas);
        }
    }

    public void AddSnapAreaForColumn(Element columnPseudo)
    {
        EnsureSnapAreas().Add(columnPseudo);
    }

    public void PropagateChildAnchors(PhysicalFragment child, LogicalOffset childOffset)
    {
        if (child.IsBox && (child.Style().AnchorName() != null || child.IsImplicitAnchor()))
        {
            var rect = new LogicalRect(childOffset, child.Size.ToLogicalSize(GetWritingMode()));
            var options = AnchorQuerySetOptions(child, _node, IsBlockFragmentationContextRoot || HasItems);
            if (child.Style().AnchorName() != null)
            {
                foreach (var name in child.Style().AnchorName()!.GetNames())
                {
                    EnsureAnchorQuery().Set(name, child.GetLayoutObject()!, rect, options, null);
                }
            }
            if (child.IsImplicitAnchor())
            {
                EnsureAnchorQuery().Set(child.GetLayoutObject()!, child.GetLayoutObject()!, rect, options, null);
            }
        }

        if (child.AnchorQuery() is { } anchorQuery)
        {
            var options = AnchorQuerySetOptions(child, _node, IsBlockFragmentationContextRoot || HasItems);
            var converter = new WritingModeConverter(GetWritingDirection(), child.Size);
            EnsureAnchorQuery().SetFromPhysical(anchorQuery, converter, childOffset, options, null);
        }
    }

    public LogicalAnchorQuery? AnchorQuery => _anchorQuery;

    public void AddOutOfFlowChildCandidate(BlockNode child, LogicalOffset childOffset,
        LogicalStaticPosition.StaticInlinePosition inlineEdge = LogicalStaticPosition.StaticInlinePosition.Left,
        LogicalStaticPosition.StaticBlockPosition blockEdge = LogicalStaticPosition.StaticBlockPosition.Top,
        bool isHiddenForPaint = false, bool allowTopLayerNodes = false)
    {
        System.Diagnostics.Debug.Assert(child.Box != null);
        if ((child.Box != null && child.Box.IsInTopOrViewTransitionLayer()) && !allowTopLayerNodes)
            return;

        _oofCandidatesMayHaveAnchorQueries |= child.Box != null && child.Box.MayHaveAnchorQuery();
        _oofPositionedCandidates.Add(new LogicalOofPositionedNode(
            child.Box!,
            new LogicalStaticPosition(childOffset, inlineEdge, blockEdge, GetWritingDirection()),
            _requiresContentBeforeBreaking,
            isHiddenForPaint,
            OofInlineContainer<LogicalOffset>.Empty));
    }

    public void AddOutOfFlowInlineChildCandidate(BlockNode child, LogicalOffset childOffset, TextDirection inlineContainerDirection, bool isHiddenForPaint = false)
    {
        System.Diagnostics.Debug.Assert(_node.IsInline || _layoutObject is LayoutInline);
        AddOutOfFlowChildCandidate(child, childOffset,
            inlineContainerDirection == TextDirection.Ltr ? LogicalStaticPosition.StaticInlinePosition.Left : LogicalStaticPosition.StaticInlinePosition.Right,
            LogicalStaticPosition.StaticBlockPosition.Top, isHiddenForPaint);
    }

    public void AddOutOfFlowFragmentainerDescendant(LogicalOofNodeForFragmentation descendant)
    {
        _oofFragmentainerDescendantsMayHaveAnchorQueries |= descendant.Box.MayHaveAnchorQuery();
        _oofPositionedFragmentainerDescendants.Add(descendant);
    }

    public void AddOutOfFlowFragmentainerDescendantFromNode(LogicalOofPositionedNode descendant)
    {
        System.Diagnostics.Debug.Assert(!descendant.RequiresContentBeforeBreaking);
        var fragmentainerDescendant = new LogicalOofNodeForFragmentation(descendant);
        AddOutOfFlowFragmentainerDescendant(fragmentainerDescendant);
    }

    public void AddOutOfFlowDescendant(LogicalOofPositionedNode descendant)
    {
        _oofPositionedDescendants.Add(descendant);
    }

    public void SwapOutOfFlowPositionedCandidates(List<LogicalOofPositionedNode> candidates)
    {
        System.Diagnostics.Debug.Assert(candidates.Count == 0);
        if (_oofCandidatesMayHaveAnchorQueries)
        {
            _oofPositionedCandidates.Sort((a, b) => a.Box.IsBeforeInPreOrder(b.Box) ? -1 : 1);
            _oofCandidatesMayHaveAnchorQueries = false;
        }
        (_oofPositionedCandidates, candidates) = (candidates, _oofPositionedCandidates);
    }

    public void ClearOutOfFlowPositionedCandidates()
    {
        _oofCandidatesMayHaveAnchorQueries = false;
        _oofPositionedCandidates.Clear();
    }

    public void AddMulticolWithPendingOOFs(BlockNode multicol, MulticolWithPendingOofs<LogicalOffset>? multicolInfo = null)
    {
        System.Diagnostics.Debug.Assert(multicol.Box is LayoutBlockFlow);
        if (_multicolsWithPendingOofs.ContainsKey(multicol.Box!))
            return;
        _multicolsWithPendingOofs[multicol.Box!] = multicolInfo ?? new MulticolWithPendingOofs<LogicalOffset>();
    }

    public void SwapMulticolsWithPendingOOFs(MulticolCollection multicols)
    {
        System.Diagnostics.Debug.Assert(multicols.Count == 0);
        (_multicolsWithPendingOofs, multicols) = (multicols, _multicolsWithPendingOofs);
    }

    public void SwapOutOfFlowFragmentainerDescendants(List<LogicalOofNodeForFragmentation> descendants)
    {
        System.Diagnostics.Debug.Assert(descendants.Count == 0);
        if (_oofFragmentainerDescendantsMayHaveAnchorQueries)
        {
            _oofPositionedFragmentainerDescendants.Sort((a, b) => a.Box.IsBeforeInPreOrder(b.Box) ? -1 : 1);
            _oofFragmentainerDescendantsMayHaveAnchorQueries = false;
        }
        (_oofPositionedFragmentainerDescendants, descendants) = (descendants, _oofPositionedFragmentainerDescendants);
    }

    public void TransferOutOfFlowCandidates(FragmentBuilder destinationBuilder, LogicalOffset additionalOffset, MulticolWithPendingOofs<LogicalOffset>? multicol = null)
    {
        foreach (var candidate in _oofPositionedCandidates)
        {
            var node = candidate.Box;
            var staticPos = candidate.StaticPosition;
            staticPos = new LogicalStaticPosition(
                staticPos.Offset + additionalOffset,
                staticPos.InlinePosition, staticPos.BlockPosition,
                staticPos.WritingDirection);

            if (multicol != null && multicol.FixedposContainingBlock.Fragment != null && node.Dimensions?.Style?.Position == PositionType.Fixed)
            {
                destinationBuilder.AddOutOfFlowFragmentainerDescendant(new LogicalOofNodeForFragmentation(node,
                    staticPos, candidate.RequiresContentBeforeBreaking, candidate.IsHiddenForPaint,
                    multicol.FixedposInlineContainer, multicol.FixedposContainingBlock, multicol.FixedposContainingBlock,
                    multicol.FixedposInlineContainer));
                continue;
            }
            destinationBuilder._oofPositionedCandidates.Add(new LogicalOofPositionedNode(
                node, staticPos, candidate.RequiresContentBeforeBreaking, candidate.IsHiddenForPaint, candidate.InlineContainer));
        }
        destinationBuilder._oofCandidatesMayHaveAnchorQueries |= _oofCandidatesMayHaveAnchorQueries;
        ClearOutOfFlowPositionedCandidates();
    }

    public bool HasOutOfFlowPositionedCandidates => _oofPositionedCandidates.Count > 0;
    public bool HasOutOfFlowPositionedDescendants => _oofPositionedDescendants.Count > 0;
    public bool HasOutOfFlowFragmentainerDescendants => _oofPositionedFragmentainerDescendants.Count > 0;
    public bool HasMulticolsWithPendingOOFs => _multicolsWithPendingOofs.Count > 0;

    public void MoveOutOfFlowDescendantCandidatesToDescendants()
    {
        System.Diagnostics.Debug.Assert(_oofPositionedDescendants.Count == 0);
        (_oofPositionedCandidates, _oofPositionedDescendants) = (_oofPositionedDescendants, _oofPositionedCandidates);

        if (_layoutObject is not LayoutInline)
            return;

        foreach (var candidate in _oofPositionedDescendants)
        {
            if (candidate.InlineContainer.Container == null && IsInlineContainerForNode(candidate.Box, _layoutObject))
            {
                _oofPositionedDescendants[_oofPositionedDescendants.IndexOf(candidate)] = new LogicalOofPositionedNode(
                    candidate.Box, candidate.StaticPosition, candidate.RequiresContentBeforeBreaking, candidate.IsHiddenForPaint,
                    new OofInlineContainer<LogicalOffset>((LayoutInline)_layoutObject, LogicalOffset.Zero));
            }
        }
    }

    public LayoutUnit BlockOffsetAdjustmentForFragmentainer(LayoutUnit fragmentainerConsumedBlockSize = default)
    {
        if (IsFragmentainerBoxType() && PreviousBreakToken is BreakToken bt && bt is BlockBreakToken)
        {
            return fragmentainerConsumedBlockSize;
        }
        return fragmentainerConsumedBlockSize;
    }

    public bool HasOutOfFlowFragmentChild => _hasOutOfFlowFragmentChild;
    public void SetHasOutOfFlowFragmentChild(bool value) => _hasOutOfFlowFragmentChild = value;

    public bool HasOutOfFlowInFragmentainerSubtree => _hasOutOfFlowInFragmentainerSubtree;
    public void SetHasOutOfFlowInFragmentainerSubtree(bool value) => _hasOutOfFlowInFragmentainerSubtree = value;

    public void PropagateOOFPositionedInfo(PhysicalFragment fragment, LogicalOffset offset, LogicalOffset relativeOffset,
        LogicalOffset offsetAdjustment = default, OofInlineContainer<LogicalOffset>? inlineContainer = null,
        LayoutUnit containingBlockAdjustment = default, OofContainingBlock<LogicalOffset>? containingBlock = null,
        OofContainingBlock<LogicalOffset>? fixedposContainingBlock = null,
        OofInlineContainer<LogicalOffset>? fixedposInlineContainer = null,
        LogicalOffset additionalFixedposOffset = default)
    {
        System.Diagnostics.Debug.Assert(fragment.NeedsOOFPositionedInfoPropagation());

        var adjustedOffset = offset + offsetAdjustment + relativeOffset;
        var converter = new WritingModeConverter(GetWritingDirection(), fragment.Size);

        foreach (var descendant in fragment.OutOfFlowPositionedDescendants())
        {
            var node = descendant.Box;
            var staticPosition = new LogicalStaticPosition(
                new LogicalOffset(
                    LogicalOffset.ConvertToLogical(descendant.StaticPositionOffset, GetWritingDirection()).InlineOffset,
                    LogicalOffset.ConvertToLogical(descendant.StaticPositionOffset, GetWritingDirection()).BlockOffset),
                descendant.HorizontalEdge, descendant.VerticalEdge, GetWritingDirection());

            var newInlineContainer = new OofInlineContainer<LogicalOffset>(descendant.InlineContainer.Container,
                converter.ToLogical(descendant.InlineContainer.RelativeOffset, fragment.Size) + relativeOffset);

            if (descendant.InlineContainer.Container == null && inlineContainer?.Container != null && IsInlineContainerForNode(node, inlineContainer?.Container))
            {
                newInlineContainer = inlineContainer!.Value;
            }

            if ((fixedposContainingBlock?.Fragment != null || additionalFixedposOffset.InlineOffset != 0 || additionalFixedposOffset.BlockOffset != 0) && node.Dimensions?.Style?.Position == PositionType.Fixed)
            {
                staticPosition = new LogicalStaticPosition(
                    staticPosition.Offset + additionalFixedposOffset + relativeOffset - fixedposContainingBlock?.RelativeOffset ?? LogicalOffset.Zero -
                    (fixedposInlineContainer?.RelativeOffset ?? LogicalOffset.Zero),
                    staticPosition.InlinePosition, staticPosition.BlockPosition, staticPosition.WritingDirection);

                if (fixedposContainingBlock?.Fragment != null || _node.IsPaginatedRoot())
                {
                    var newFixedposInlineContainer = fixedposInlineContainer ?? OofInlineContainer<LogicalOffset>.Empty;
                    AddOutOfFlowFragmentainerDescendant(new LogicalOofNodeForFragmentation(node, staticPosition,
                        descendant.RequiresContentBeforeBreaking, descendant.IsHiddenForPaint,
                        newFixedposInlineContainer, fixedposContainingBlock!.Value, fixedposContainingBlock!.Value, newFixedposInlineContainer));
                    continue;
                }
            }

            staticPosition = new LogicalStaticPosition(staticPosition.Offset + adjustedOffset, staticPosition.InlinePosition, staticPosition.BlockPosition, staticPosition.WritingDirection);

            _oofCandidatesMayHaveAnchorQueries |= node.MayHaveAnchorQuery();
            _oofPositionedCandidates.Add(new LogicalOofPositionedNode(node, staticPosition,
                descendant.RequiresContentBeforeBreaking, descendant.IsHiddenForPaint, newInlineContainer));
        }

        var oofData = fragment.GetFragmentedOofData();
        if (oofData == null)
            return;

        var boxFragment = fragment as PhysicalBoxFragment;
        bool isColumnSpanner = boxFragment?.IsColumnSpanAll() ?? false;

        if (oofData.MulticolsWithPendingOofs.Count > 0)
        {
            foreach (var multicol in oofData.MulticolsWithPendingOofs)
            {
                var multicolInfo = multicol.Value;
                var multicolOffset = converter.ToLogical(multicolInfo.MulticolOffset, fragment.Size);

                var fixedposInlineRelOffset = converter.ToLogical(multicolInfo.FixedposInlineContainer.RelativeOffset, fragment.Size);
                var newFixedposInlineContainer = new OofInlineContainer<LogicalOffset>(
                    multicolInfo.FixedposInlineContainer.Container, fixedposInlineRelOffset);
                var fixedposContainingBlockFragment = multicolInfo.FixedposContainingBlock.Fragment;

                AdjustFixedposContainerInfo(boxFragment, relativeOffset, ref newFixedposInlineContainer, ref fixedposContainingBlockFragment);

                LogicalOffset fixedposContainingBlockOffset = LogicalOffset.Zero;
                LogicalOffset fixedposContainingBlockRelOffset = LogicalOffset.Zero;
                bool isInsideColumnSpanner = multicolInfo.FixedposContainingBlock.IsInsideColumnSpanner;

                if (fixedposContainingBlockFragment != null)
                {
                    fixedposContainingBlockOffset = converter.ToLogical(multicolInfo.FixedposContainingBlock.Offset, fixedposContainingBlockFragment.Size);
                    fixedposContainingBlockRelOffset = RelativeInsetToLogical(multicolInfo.FixedposContainingBlock.RelativeOffset, GetWritingDirection());
                    fixedposContainingBlockRelOffset += relativeOffset;
                    if (!fragment.IsFragmentainerBox())
                        fixedposContainingBlockOffset += offset;
                    fixedposContainingBlockOffset = new LogicalOffset(
                        fixedposContainingBlockOffset.InlineOffset,
                        fixedposContainingBlockOffset.BlockOffset + containingBlockAdjustment.Value);
                    if (isColumnSpanner)
                        isInsideColumnSpanner = true;
                }
                else
                {
                    multicolOffset += adjustedOffset;
                }

                AddMulticolWithPendingOOFs(new BlockNode(multicol.Key),
                    new MulticolWithPendingOofs<LogicalOffset>(multicolOffset,
                        new OofContainingBlock<LogicalOffset>(fixedposContainingBlockOffset, fixedposContainingBlockRelOffset,
                            fixedposContainingBlockFragment, null, isInsideColumnSpanner),
                        newFixedposInlineContainer));
            }
        }

        PropagateOOFFragmentainerDescendants(fragment, offset, relativeOffset, containingBlockAdjustment, containingBlock, fixedposContainingBlock);
    }

    public void PropagateOOFFragmentainerDescendants(PhysicalFragment fragment, LogicalOffset offset, LogicalOffset relativeOffset,
        LayoutUnit containingBlockAdjustment, OofContainingBlock<LogicalOffset>? containingBlock,
        OofContainingBlock<LogicalOffset>? fixedposContainingBlock, List<LogicalOofNodeForFragmentation>? outList = null)
    {
        var oofData = fragment.GetFragmentedOofData();
        if (oofData == null || oofData.OofPositionedFragmentainerDescendants.Count == 0)
            return;

        var converter = new WritingModeConverter(GetWritingDirection(), fragment.Size);
        var boxFragment = fragment as PhysicalBoxFragment;
        bool isColumnSpanner = boxFragment?.IsColumnSpanAll() ?? false;

        foreach (var descendant in oofData.OofPositionedFragmentainerDescendants)
        {
            var containingBlockFragment = descendant.ContainingBlock.Fragment;
            bool containerInsideColumnSpanner = descendant.ContainingBlock.IsInsideColumnSpanner;
            bool fixedposContainerInsideColumnSpanner = descendant.FixedposContainingBlock.IsInsideColumnSpanner;

            if (containingBlockFragment == null)
            {
                containingBlockFragment = boxFragment;
            }
            else if (boxFragment != null && false)
            {
                if (containerInsideColumnSpanner)
                {
                    containerInsideColumnSpanner = false;
                    fixedposContainerInsideColumnSpanner = false;
                }
                else
                {
                    continue;
                }
            }

            if (isColumnSpanner)
                containerInsideColumnSpanner = true;

            var containingBlockOffset = converter.ToLogical(descendant.ContainingBlock.Offset, containingBlockFragment.Size);
            var containingBlockRelOffset = RelativeInsetToLogical(descendant.ContainingBlock.RelativeOffset, GetWritingDirection());
            containingBlockRelOffset += relativeOffset;
            if (!fragment.IsFragmentainerBox())
                containingBlockOffset += offset;
            containingBlockOffset = new LogicalOffset(containingBlockOffset.InlineOffset,
                containingBlockOffset.BlockOffset + containingBlockAdjustment.Value);

            var clippedContainerBlockOffset = descendant.ContainingBlock.ClippedContainerBlockOffset;
            if (!clippedContainerBlockOffset.HasValue && fragment.HasNonVisibleBlockOverflow())
            {
                clippedContainerBlockOffset = LayoutUnit.FromValue(0);
            }
            if (clippedContainerBlockOffset.HasValue)
            {
                if (!fragment.IsFragmentainerBox())
                    clippedContainerBlockOffset = LayoutUnit.FromValue(clippedContainerBlockOffset.Value.Value + offset.BlockOffset);
                clippedContainerBlockOffset = LayoutUnit.FromValue(clippedContainerBlockOffset.Value.Value + containingBlockAdjustment.Value);
            }
            if (!clippedContainerBlockOffset.HasValue && containingBlock?.ClippedContainerBlockOffset != null)
            {
                clippedContainerBlockOffset = containingBlock.Value.ClippedContainerBlockOffset;
            }

            var inlineRelOffset = converter.ToLogical(descendant.InlineContainer.RelativeOffset, fragment.Size);
            var newInlineContainer = new OofInlineContainer<LogicalOffset>(descendant.InlineContainer.Container, inlineRelOffset);

            var containingBlockSize = descendant.ContainingBlock.Fragment != null
                ? descendant.ContainingBlock.Fragment.Size
                : fragment.Size;
            var containingBlockConverter = new WritingModeConverter(GetWritingDirection(), containingBlockSize);
            var staticPosition = new LogicalStaticPosition(
                new LogicalOffset(
                    LogicalOffset.ConvertToLogical(descendant.StaticPosition.Offset, GetWritingDirection()).InlineOffset,
                    LogicalOffset.ConvertToLogical(descendant.StaticPosition.Offset, GetWritingDirection()).BlockOffset),
                descendant.StaticPosition.InlinePosition, descendant.StaticPosition.BlockPosition, GetWritingDirection());

            if (newInlineContainer.Container != null && boxFragment != null && containingBlockFragment == boxFragment)
                staticPosition = new LogicalStaticPosition(staticPosition.Offset - inlineRelOffset,
                    staticPosition.InlinePosition, staticPosition.BlockPosition, staticPosition.WritingDirection);

            var fixedposInlineRelOffset = converter.ToLogical(descendant.FixedposInlineContainer.RelativeOffset, fragment.Size);
            var newFixedposInlineContainer = new OofInlineContainer<LogicalOffset>(descendant.FixedposInlineContainer.Container, fixedposInlineRelOffset);
            var fixedposContainingBlockFragment = descendant.FixedposContainingBlock.Fragment;

            AdjustFixedposContainerInfo(boxFragment, relativeOffset, ref newFixedposInlineContainer, ref fixedposContainingBlockFragment, newInlineContainer);

            LogicalOffset fixedposContainingBlockOffset = default;
            LogicalOffset fixedposContainingBlockRelOffset = default;
            LayoutUnit? fixedposClippedContainerBlockOffset = null;

            if (fixedposContainingBlockFragment != null)
            {
                fixedposContainingBlockOffset = converter.ToLogical(descendant.FixedposContainingBlock.Offset, fixedposContainingBlockFragment.Size);
                fixedposContainingBlockRelOffset = RelativeInsetToLogical(descendant.FixedposContainingBlock.RelativeOffset, GetWritingDirection());
                fixedposContainingBlockRelOffset += relativeOffset;
                if (!fragment.IsFragmentainerBox())
                    fixedposContainingBlockOffset += offset;
                fixedposContainingBlockOffset = new LogicalOffset(fixedposContainingBlockOffset.InlineOffset,
                    fixedposContainingBlockOffset.BlockOffset + containingBlockAdjustment.Value);

                if (descendant.FixedposContainingBlock.ClippedContainerBlockOffset.HasValue)
                {
                    fixedposClippedContainerBlockOffset = descendant.FixedposContainingBlock.ClippedContainerBlockOffset;
                    if (!fragment.IsFragmentainerBox())
                        fixedposClippedContainerBlockOffset = LayoutUnit.FromValue(fixedposClippedContainerBlockOffset.Value.Value + offset.BlockOffset);
                    fixedposClippedContainerBlockOffset = LayoutUnit.FromValue(fixedposClippedContainerBlockOffset.Value.Value + containingBlockAdjustment.Value);
                }
                else if (fragment.HasNonVisibleBlockOverflow())
                {
                    fixedposClippedContainerBlockOffset = LayoutUnit.FromValue(0);
                }
                else if (containingBlock?.ClippedContainerBlockOffset != null)
                {
                    fixedposClippedContainerBlockOffset = containingBlock.Value.ClippedContainerBlockOffset;
                }

                if (isColumnSpanner)
                    fixedposContainerInsideColumnSpanner = true;
            }

            if (fixedposContainingBlockFragment == null && fixedposContainingBlock?.Fragment != null)
            {
                fixedposContainingBlockFragment = fixedposContainingBlock.Value.Fragment;
                fixedposContainingBlockOffset = fixedposContainingBlock.Value.Offset;
                fixedposContainingBlockRelOffset = fixedposContainingBlock.Value.RelativeOffset;
            }

            var oofNode = new LogicalOofNodeForFragmentation(descendant.Box, staticPosition,
                descendant.RequiresContentBeforeBreaking, descendant.IsHiddenForPaint, newInlineContainer,
                new OofContainingBlock<LogicalOffset>(containingBlockOffset, containingBlockRelOffset,
                    containingBlockFragment, clippedContainerBlockOffset, containerInsideColumnSpanner),
                new OofContainingBlock<LogicalOffset>(fixedposContainingBlockOffset, fixedposContainingBlockRelOffset,
                    fixedposContainingBlockFragment, fixedposClippedContainerBlockOffset, fixedposContainerInsideColumnSpanner),
                newFixedposInlineContainer);

            if (outList != null)
                outList.Add(oofNode);
            else
                AddOutOfFlowFragmentainerDescendant(oofNode);
        }
    }

    public void SetIsSelfCollapsing() => _isSelfCollapsing = true;
    public void SetIsPushedByFloats() => _isPushedByFloats = true;
    public bool IsPushedByFloats => _isPushedByFloats;

    public void SetSubtreeModifiedMarginStrut()
    {
        _subtreeModifiedMarginStrut = true;
    }

    public void ResetAdjoiningObjectTypes()
    {
        _adjoiningObjectTypes = AdjoiningObjectTypes.None;
        _hasAdjoiningObjectDescendants = false;
    }

    public void AddAdjoiningObjectTypes(AdjoiningObjectTypes types)
    {
        _adjoiningObjectTypes |= types;
        _hasAdjoiningObjectDescendants |= types != AdjoiningObjectTypes.None;
    }

    public void SetAdjoiningObjectTypes(AdjoiningObjectTypes types) => _adjoiningObjectTypes = types;
    public void SetHasAdjoiningObjectDescendants(bool value) => _hasAdjoiningObjectDescendants = value;
    public AdjoiningObjectTypes GetAdjoiningObjectTypes() => _adjoiningObjectTypes;

    public void SetIsBlockInInline() => _isBlockInInline = true;
    public void SetIsLineForParallelFlow() => _isLineForParallelFlow = true;

    public void SetHasBlockFragmentation() => _hasBlockFragmentation = true;
    public void SetIsBlockFragmentationContextRoot() => _isFragmentationContextRoot = true;
    public bool IsBlockFragmentationContextRoot => _isFragmentationContextRoot;

    public void SetHasColumnSpanner(bool value) => _hasColumnSpanner = value;
    public void SetColumnSpannerPath(ColumnSpannerPath? path)
    {
        _columnSpannerPath = path;
        SetHasColumnSpanner(path != null);
    }
    public bool FoundColumnSpanner => _hasColumnSpanner;

    public void SetIsEmptySpannerParent(bool value) => _isEmptySpannerParent = value;
    public bool IsEmptySpannerParent => _isEmptySpannerParent;

    public void SetShouldForceSameFragmentationFlow() => _shouldForceSameFragmentationFlow = true;
    public bool ShouldForceSameFragmentationFlow => _shouldForceSameFragmentationFlow;

    public void SetRequiresContentBeforeBreaking(bool value) => _requiresContentBeforeBreaking = value;
    public bool RequiresContentBeforeBreaking => _requiresContentBeforeBreaking;

    public void ClampBreakAppeal(BreakAppeal appeal)
    {
        if (appeal < _breakAppeal)
            _breakAppeal = appeal;
    }

    public void SetHasDescendantThatDependsOnPercentageBlockSize(bool value = true) => _hasDescendantThatDependsOnPercentageBlockSize = value;

    public void SetAnnotationOverflow(LayoutUnit overflow) => _annotationOverflow = overflow;
    public LayoutUnit AnnotationOverflow => _annotationOverflow;

    public void SetBlockEndAnnotationSpace(LayoutUnit space) => _blockEndAnnotationSpace = space;

    public void PropagateSpaceShortage(LayoutUnit? spaceShortage)
    {
        LayoutUnitUtils.UpdateMinimalSpaceShortage(spaceShortage, ref _minimalSpaceShortage);
    }

    public LayoutUnit? MinimalSpaceShortage
    {
        get
        {
            if (_minimalSpaceShortage == LayoutUnitUtils.IndefiniteSize)
                return null;
            return _minimalSpaceShortage;
        }
    }

    public void PropagateTallestUnbreakableBlockSize(LayoutUnit unbreakableBlockSize)
    {
        _tallestUnbreakableBlockSize = LayoutUnit.Max(_tallestUnbreakableBlockSize, unbreakableBlockSize);
    }

    public void SetIsInitialColumnBalancingPass()
    {
        _tallestUnbreakableBlockSize = LayoutUnit.FromValue(0);
    }

    public bool IsInitialColumnBalancingPass => _tallestUnbreakableBlockSize >= LayoutUnit.FromValue(0);

    protected FragmentBuilder(LayoutInputNode node, ComputedStyle style, ConstraintSpace space, WritingDirectionMode writingDirection,
        BreakToken? previousBreakToken)
    {
        _node = node;
        _space = space;
        _style = style;
        _writingDirection = writingDirection;
        _styleVariant = StyleVariant.Standard;
        _previousBreakToken = previousBreakToken;
        _isHiddenForPaint = space.IsHiddenForPaint;
        _layoutObject = node.LayoutObject;
    }

    protected List<LayoutBoxModelObject> EnsureStickyDescendants()
    {
        _stickyDescendants ??= new List<LayoutBoxModelObject>();
        return _stickyDescendants;
    }

    protected List<Element> EnsureSnapAreas()
    {
        _snapAreas ??= new List<Element>();
        return _snapAreas;
    }

    protected LogicalAnchorQuery EnsureAnchorQuery()
    {
        _anchorQuery ??= new LogicalAnchorQuery();
        return _anchorQuery;
    }

    protected void PropagateFromLayoutResultAndFragment(LayoutResult childResult, LogicalOffset childOffset, LogicalOffset relativeOffset,
        OofInlineContainer<LogicalOffset>? inlineContainer = null)
    {
        PropagateFromLayoutResult(childResult);
        PropagateFromFragment(childResult.GetPhysicalFragment(), childOffset, relativeOffset, inlineContainer);
    }

    protected void PropagateFromLayoutResult(LayoutResult childResult)
    {
        _hasOrthogonalFallbackSizeDescendant |= childResult.HasOrthogonalFallbackInlineSize() || childResult.HasOrthogonalFallbackSizeDescendant();
    }

    protected void UpdateScrollStartTarget(LayoutObject newTarget)
    {
        if (newTarget != _scrollStartTarget && (_scrollStartTarget == null || newTarget.IsBeforeInPreOrder(_scrollStartTarget)))
        {
            _scrollStartTarget = newTarget;
        }
    }

    protected void PropagateScrollStartTarget(PhysicalFragment child)
    {
        if (child.Style().ScrollStartTarget() != EScrollStartTarget.None)
        {
            if (child.GetMutableLayoutObject() is { } childObj)
                UpdateScrollStartTarget(childObj);
        }
        if (child.PropagatedScrollStartTarget() is { } target)
            UpdateScrollStartTarget(target);
    }

    protected void PropagateFromFragment(PhysicalFragment child, LogicalOffset childOffset, LogicalOffset relativeOffset,
        OofInlineContainer<LogicalOffset>? inlineContainer = null)
    {
        if (GetBoxType() == PhysicalFragment.BoxType.PageBorderBox)
        {
            System.Diagnostics.Debug.Assert(child.GetBoxType() == PhysicalFragment.BoxType.PageArea);
            return;
        }

        PropagateChildAnchors(child, childOffset + relativeOffset);
        PropagateStickyDescendants(child);
        PropagateSnapAreas(child);
        PropagateScrollStartTarget(child);

        if (child.NeedsOOFPositionedInfoPropagation() && (!IsFragmentainerBoxType() || !child.IsOutOfFlowPositioned))
        {
            var adjustmentForOofPropagation = BlockOffsetAdjustmentForFragmentainer();
            PropagateOOFPositionedInfo(child, childOffset, relativeOffset, default, inlineContainer, adjustmentForOofPropagation);
        }

        if (!_hasDescendantThatDependsOnPercentageBlockSize)
        {
            if (child.DependsOnPercentageBlockSize && !child.IsOutOfFlowPositioned)
                _hasDescendantThatDependsOnPercentageBlockSize = true;

            var childStyle = child.Style();
            if (child.IsCSSBox() && childStyle.Position == PositionType.Relative)
            {
                if (Style.IsHorizontalWritingMode())
                {
                    if (childStyle.Top.HasPercent() || childStyle.Bottom.HasPercent())
                        _hasDescendantThatDependsOnPercentageBlockSize = true;
                }
                else
                {
                    if (childStyle.Left.HasPercent() || childStyle.Right.HasPercent())
                        _hasDescendantThatDependsOnPercentageBlockSize = true;
                }
            }
        }

        if (!_hasFloatingDescendantsForPaint)
        {
            if (child.IsFloating || (child.HasFloatingDescendantsForPaint && !child.IsPaintedAtomically))
                _hasFloatingDescendantsForPaint = true;
        }

        if (!_hasAdjoiningObjectDescendants)
        {
            if (!child.IsFormattingContextRoot() && child.HasAdjoiningObjectDescendants)
                _hasAdjoiningObjectDescendants = true;
        }

        if (_hasBlockFragmentation && !child.IsFragmentainerBox() && _breakToken == null)
        {
            var childBreakToken = child.BreakToken;
            switch (child.Type)
            {
                case PhysicalFragment.FragmentType.FragmentBox:
                    if (childBreakToken != null)
                        _childBreakTokens.Add(childBreakToken);
                    break;
                case PhysicalFragment.FragmentType.FragmentLineBox:
                    if (child.IsLineForParallelFlow)
                        break;
                    if (childBreakToken is InlineBreakToken inlineBreakToken)
                        _lastInlineBreakToken = inlineBreakToken;
                    _lineCount++;
                    break;
            }
        }
    }

    protected void AddChildInternal(PhysicalFragment? child, LogicalOffset childOffset)
    {
        if (child == null) return;
        if (child.IsListMarker())
        {
            _children.Insert(0, new LogicalFragmentLink(childOffset, child));
            return;
        }
        if (child.IsTextControlPlaceholder)
        {
            int size = _children.Count;
            if (size > 0)
            {
                _children.Insert(size - 1, new LogicalFragmentLink(childOffset, child));
                return;
            }
        }
        _children.Add(new LogicalFragmentLink(childOffset, child));
    }

    protected void AdjustFixedposContainerInfo(PhysicalFragment? boxFragment, LogicalOffset relativeOffset,
        ref OofInlineContainer<LogicalOffset> fixedposInlineContainer, ref PhysicalFragment? fixedposContainingBlockFragment,
        OofInlineContainer<LogicalOffset>? currentInlineContainer = null)
    {
        if (boxFragment == null) return;

        if (fixedposContainingBlockFragment == null && boxFragment.GetLayoutObject() is { } layoutObj)
        {
            if (currentInlineContainer?.Container != null && currentInlineContainer.Value.Container.CanContainFixedPositionObjects())
            {
                fixedposInlineContainer = currentInlineContainer.Value;
                fixedposContainingBlockFragment = boxFragment;
            }
            else if (layoutObj.CanContainFixedPositionObjects())
            {
                if (fixedposInlineContainer.Container == null && layoutObj is LayoutInline layoutInline)
                {
                    fixedposInlineContainer = new OofInlineContainer<LogicalOffset>(layoutInline, relativeOffset);
                }
                else
                {
                    fixedposContainingBlockFragment = boxFragment;
                }
            }
            else if (fixedposInlineContainer.Container != null)
            {
                if (layoutObj == fixedposInlineContainer.Container.ContainingBlock())
                    fixedposContainingBlockFragment = boxFragment;
            }
        }
    }

    protected void PropagateFromLayoutResultAndFragment(LayoutResult childResult, LogicalOffset relativeOffset)
    {
        PropagateFromLayoutResult(childResult);
        PropagateFromFragment(childResult.GetPhysicalFragment(), relativeOffset, LogicalOffset.Zero);
    }

    private uint GetSnapInsertionPosition(Element snapArea)
    {
        var snapAreas = EnsureSnapAreas();
        var newBox = snapArea.GetLayoutBox();
        if (newBox == null)
            return (uint)snapAreas.Count;
        for (int i = snapAreas.Count; i >= 1; i--)
        {
            var existingBox = snapAreas[i - 1].GetLayoutBox();
            if (existingBox != null && existingBox.IsBeforeInPreOrder(newBox))
                return (uint)i;
        }
        return 0;
    }

    private static bool IsInlineContainerForNode(LayoutBox node, LayoutObject? inlineContainer)
    {
        return inlineContainer is LayoutInline layoutInline && layoutInline.CanContainOutOfFlowPositionedElement(node.Dimensions?.Style?.Position ?? PositionType.Static);
    }

    private static LogicalAnchorQuery.SetOptions AnchorQuerySetOptions(PhysicalFragment fragment, LayoutInputNode container, bool maybeOutOfOrderIfOof)
    {
        if (!fragment.IsOutOfFlowPositioned)
            return LogicalAnchorQuery.SetOptions.InFlow;

        if (!maybeOutOfOrderIfOof)
            return LogicalAnchorQuery.SetOptions.OutOfFlow;

        if (container.GetLayoutBox() == null)
            return LogicalAnchorQuery.SetOptions.OutOfFlow;

        var layoutObject = fragment.GetLayoutObject();
        if (layoutObject == null)
            return LogicalAnchorQuery.SetOptions.OutOfFlow;

        var containingBlock = layoutObject.ContainingBlock();
        if (containingBlock?.Node != null && container.GetLayoutBox()?.Dimensions?.Element != null && containingBlock.Node == container.GetLayoutBox()!.Dimensions!.Element)
            return LogicalAnchorQuery.SetOptions.OutOfFlow;

        return LogicalAnchorQuery.SetOptions.InFlow;
    }

    private static LogicalOffset RelativeInsetToLogical(PhysicalOffset physicalOffset, WritingDirectionMode writingDirection)
    {
        if (writingDirection.IsHorizontal)
            return new LogicalOffset(physicalOffset.Left, physicalOffset.Top);
        return new LogicalOffset(physicalOffset.Top, physicalOffset.Left);
    }

    private static LogicalOffset RelativeInsetToLogical(LogicalOffset offset, WritingDirectionMode writingDirection)
    {
        return offset;
    }

    protected LayoutInputNode _node;
    protected ConstraintSpace _space;
    protected ComputedStyle _style;
    protected WritingDirectionMode _writingDirection;
    protected StyleVariant _styleVariant;
    protected PhysicalFragment.BoxType _boxType = PhysicalFragment.BoxType.NormalBox;
    protected LogicalSize _size;
    protected LayoutObject? _layoutObject;

    protected BreakToken? _previousBreakToken;
    protected BreakToken? _breakToken;

    protected List<LayoutBoxModelObject>? _stickyDescendants;
    protected List<Element>? _snapAreas;
    protected LayoutObject? _scrollStartTarget;
    protected LogicalAnchorQuery? _anchorQuery;
    protected LayoutUnit _bfcLineOffset;
    protected LayoutUnit? _bfcBlockOffset;
    protected MarginStrut _endMarginStrut;
    protected ExclusionSpace _exclusionSpace;
    protected int? _linesUntilClamp;

    protected ChildrenVector _children = new();

    protected FragmentItemsBuilder? _itemsBuilder;

    protected List<BreakToken> _childBreakTokens = new();
    protected InlineBreakToken? _lastInlineBreakToken;

    protected List<LogicalOofPositionedNode> _oofPositionedCandidates = new();
    protected List<LogicalOofNodeForFragmentation> _oofPositionedFragmentainerDescendants = new();
    protected List<LogicalOofPositionedNode> _oofPositionedDescendants = new();
    protected MulticolCollection _multicolsWithPendingOofs = new();

    protected UnpositionedListMarker _unpositionedListMarker = new UnpositionedListMarker(BlockNode.Null);

    protected ColumnSpannerPath? _columnSpannerPath;
    protected EarlyBreak? _earlyBreak;
    protected BreakAppeal _breakAppeal = BreakAppeal.Perfect;

    protected LayoutUnit _annotationOverflow;
    protected LayoutUnit _blockEndAnnotationSpace;

    protected LayoutUnit _minimalSpaceShortage = LayoutUnitUtils.IndefiniteSize;
    protected LayoutUnit _tallestUnbreakableBlockSize = LayoutUnitUtils.Min;

    protected int _lineCount;

    protected AdjoiningObjectTypes _adjoiningObjectTypes = AdjoiningObjectTypes.None;
    protected bool _hasAdjoiningObjectDescendants;
    protected bool _isSelfCollapsing;
    protected bool _isPushedByFloats;
    protected bool _subtreeModifiedMarginStrut;
    protected bool _isNewFc;
    protected bool _isBlockInInline;
    protected bool _isLineForParallelFlow;
    protected bool _hasFloatingDescendantsForPaint;
    protected bool _hasDescendantThatDependsOnPercentageBlockSize;
    protected bool _hasOrthogonalFallbackSizeDescendant;
    protected bool _mayHaveDescendantAboveBlockStart;
    protected bool _hasBlockFragmentation;
    protected bool _isFragmentationContextRoot;
    protected bool _isHiddenForPaint;
    protected bool _isOpaque;
    protected bool _hasCollapsedBorders;
    protected bool _hasColumnSpanner;
    protected bool _isEmptySpannerParent;
    protected bool _shouldForceSameFragmentationFlow;
    protected bool _requiresContentBeforeBreaking;
    protected bool _hasOutOfFlowFragmentChild;
    protected bool _hasOutOfFlowInFragmentainerSubtree;
    protected bool _isBlockEndTrimmableLine;

    protected bool _oofCandidatesMayHaveAnchorQueries;
    protected bool _oofFragmentainerDescendantsMayHaveAnchorQueries;
}