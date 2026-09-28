using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;
using UpBrowser.Core.Layout.Inline;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Base class for layout algorithms. Encapsulates the constraint space and produces
/// a BoxFragment result. Mirrors the architecture of the engine's layout algorithms.
/// </summary>
public abstract class LayoutAlgorithm
{
    protected Element Node { get; }
    protected ComputedStyle Style => Node.ComputedStyle ?? DefaultStyle;
    protected ConstraintSpace Space { get; }
    protected BoxFragmentBuilder Builder { get; }

    private static readonly ComputedStyle DefaultStyle = new();

    /// <summary>
    /// Root element computed font-size — the rem basis. Flows from the root
    /// ConstraintSpace established by <see cref="LayoutEngine.LayoutAurora"/>.
    /// </summary>
    protected float RootFontSize => Space.RootFontSize;

    /// <summary>
    /// Initial containing block width — the vw/vmin/vmax basis. NOTE: this is the
    /// real viewport, not the element's available inline size; percentages keep
    /// resolving against Space percentage/available sizes as before.
    /// </summary>
    protected float ViewportWidth => Space.ViewportWidth;
    protected float ViewportHeight => Space.ViewportHeight;

    protected LayoutAlgorithm(Element node, in ConstraintSpace space)
    {
        Node = node;
        Space = space;
        // An element's own computed 'direction' steers its inline formatting; the
        // space handed down from the parent only carries the ancestor's value.
        if (node.ComputedStyle is { } cs)
        {
            var styleDir = string.Equals(cs.Direction, "rtl", StringComparison.OrdinalIgnoreCase)
                ? TextDirection.Rtl : TextDirection.Ltr;
            if (styleDir != space.Direction)
                Space = space.WithDirection(styleDir);
        }
        Builder = new BoxFragmentBuilder();
    }

    /// <summary>
    /// Border widths this box's own geometry is built from. A table cell inside a
    /// border-collapse: collapse table owns only half of every shared grid line
    /// (CSS 2.1 §17.6.2); the table algorithm resolves those halves and hands them
    /// down through the constraint space, so they - not the style's border-widths -
    /// size the box.
    /// </summary>
    protected BoxStrut OwnBorders => Space.CollapsedCellBorders ?? LengthUtils.ComputeBorders(Style);

    public float BorderLeft => OwnBorders.Left;
    public float BorderRight => OwnBorders.Right;
    public float BorderTop => OwnBorders.Top;
    public float BorderBottom => OwnBorders.Bottom;

    public float PaddingLeft => Style.PaddingLeft.ToPixels(Style.FontSize, RootFontSize, ViewportWidth, ViewportHeight);
    public float PaddingRight => Style.PaddingRight.ToPixels(Style.FontSize, RootFontSize, ViewportWidth, ViewportHeight);
    public float PaddingTop => Style.PaddingTop.ToPixels(Style.FontSize, RootFontSize, ViewportWidth, ViewportHeight);
    public float PaddingBottom => Style.PaddingBottom.ToPixels(Style.FontSize, RootFontSize, ViewportWidth, ViewportHeight);

    public float BorderLeftRight => BorderLeft + BorderRight;
    public float BorderTopBottom => BorderTop + BorderBottom;
    public float BorderPaddingInline => BorderLeftRight + PaddingLeft + PaddingRight;
    public float BorderPaddingBlock => BorderTopBottom + PaddingTop + PaddingBottom;

    /// <summary>Available inline size for children (content box width, minus this box's own reserved scrollbar).</summary>
    public float ChildAvailableInlineSize =>
        Math.Max(0, (Space.HasDefiniteInlineSize ? Space.AvailableInlineSize : 0) - BorderPaddingInline - Space.ScrollbarInline);

    /// <summary>Available block size for children (content box height, or infinite).</summary>
    public float ChildAvailableBlockSize =>
        Space.HasDefiniteBlockSize ? Math.Max(0, Space.AvailableBlockSize - BorderPaddingBlock) : float.PositiveInfinity;

    public abstract LayoutResult Layout();
}

/// <summary>
/// Accumulates geometry while a layout algorithm runs, then produces a BoxFragment.
/// </summary>
public class BoxFragmentBuilder
{
    public float InlineSize { get; set; }
    public float BlockSize { get; set; }
    public float IntrinsicBlockSize { get; set; }
    public float MarginLeft { get; set; }
    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }
    public float BorderLeft { get; set; }
    public float BorderTop { get; set; }
    public float BorderRight { get; set; }
    public float BorderBottom { get; set; }
    public float PaddingLeft { get; set; }
    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }
    public Element? Element { get; set; }

    // A5: resolved multicol geometry (copied into the BoxFragment).
    public bool IsMultiColumn { get; set; }
    public int UsedColumnCount { get; set; }
    public float ColumnInlineSize { get; set; }
    public float ColumnProgression { get; set; }
    public List<BoxFragment> Children { get; } = new();
    public List<BoxLine> Lines { get; } = new();
    public float BfcLineOffset { get; set; }
    public float BfcBlockOffset { get; set; }
    public bool IsSelfCollapsing { get; set; }
    public float EndMarginStrut { get; set; }
    public bool HasSeenAllChildren { get; set; }
    public EBreakBetween PreviousBreakAfter { get; set; }
    public float Baseline { get; set; }

    public void AddChild(BoxFragment child)
    {
        child.InlineOffset += child.MarginLeft;
        child.BlockOffset += child.MarginTop;
        Children.Add(child);
        IntrinsicBlockSize = Math.Max(IntrinsicBlockSize, child.MarginBoxBlockEnd);
    }

    public void AddLine(BoxLine line) => Lines.Add(line);

    /// <summary>
    /// Shift all current children in the block direction.
    /// Mirrors BoxFragmentBuilder::MoveChildrenInBlockDirection() (used by
    /// AlignBlockContent / align-content).
    /// </summary>
    public void MoveChildrenInBlockDirection(float delta)
    {
        if (delta == 0) return;
        foreach (var child in Children)
            child.BlockOffset += delta;
        IntrinsicBlockSize += delta;
        BfcBlockOffset += delta;
    }

    public BoxFragment ToBoxFragment() => new()
    {
        InlineSize = InlineSize,
        BlockSize = BlockSize,
        MarginLeft = MarginLeft,
        MarginTop = MarginTop,
        MarginRight = MarginRight,
        MarginBottom = MarginBottom,
        BorderLeft = BorderLeft,
        BorderTop = BorderTop,
        BorderRight = BorderRight,
        BorderBottom = BorderBottom,
        PaddingLeft = PaddingLeft,
        PaddingTop = PaddingTop,
        PaddingRight = PaddingRight,
        PaddingBottom = PaddingBottom,
        Element = Element,
        IsMultiColumn = IsMultiColumn,
        UsedColumnCount = UsedColumnCount,
        ColumnInlineSize = ColumnInlineSize,
        ColumnProgression = ColumnProgression,
    };

    // ============================================================
    // OOF (Out-of-Flow) infrastructure
    // ============================================================

    // ── OOF State ─────────────────────────────────────────────────────────
    public List<LogicalOofPositionedNode> OofPositionedCandidates { get; } = new();
    public List<LogicalOofNodeForFragmentation> OofPositionedFragmentainerDescendants { get; } = new();
    public List<LogicalOofPositionedNode> OofPositionedDescendants { get; } = new();
    public Dictionary<LayoutBox, MulticolWithPendingOofs<LogicalOffset>> MulticolsWithPendingOofs { get; } = new();
    public bool HasOutOfFlowFragmentChild { get; set; }
    public bool HasOutOfFlowInFragmentainerSubtree { get; set; }
    public bool IsFragmentationContextRoot { get; set; }
    public float TallestUnbreakableBlockSize { get; set; } = float.MinValue;

    public bool HasOutOfFlowPositionedCandidates => OofPositionedCandidates.Count > 0;
    public bool HasOutOfFlowFragmentainerDescendants => OofPositionedFragmentainerDescendants.Count > 0;
    public bool HasMulticolsWithPendingOOFs => MulticolsWithPendingOofs.Count > 0;
    public bool IsBlockFragmentationContextRoot => IsFragmentationContextRoot;
    public bool IsInitialColumnBalancingPass => TallestUnbreakableBlockSize >= 0;

    public LogicalSize Size => new(InlineSize, BlockSize);
    public bool HasBlockSize => BlockSize != 0 && !float.IsNaN(BlockSize) && !float.IsInfinity(BlockSize);
    public BoxStrut Borders => new(BorderTop, BorderRight, BorderBottom, BorderLeft);
    public BoxStrut Scrollbar => BoxStrut.Zero;
    public LayoutInputNode Node => _node ?? new BlockNode(null);
    public float FragmentBlockSize => BlockSize;
    public LogicalSize ChildAvailableSize => new(InlineSize, BlockSize);

    public LayoutInputNode? _node;

    public ConstraintSpace GetConstraintSpace() => _space ?? ConstraintSpace.Infinite();
    public WritingDirectionMode GetWritingDirection() => new(WritingMode.HorizontalTb, TextDirection.Ltr);
    public LayoutObject? GetLayoutObject() => _layoutObject;
    public BreakToken? PreviousBreakToken => _previousBreakToken;

    private ConstraintSpace? _space;
    private LayoutObject? _layoutObject;
    private BreakToken? _previousBreakToken;

    public void SetConstraintSpace(ConstraintSpace space) => _space = space;
    public void SetLayoutObject(LayoutObject? obj) => _layoutObject = obj;
    public void SetPreviousBreakToken(BreakToken? token) => _previousBreakToken = token;
    public void SetNode(LayoutInputNode node) => _node = node;

    public bool IsAbsoluteContainer() => true;
    public bool IsFixedContainer() => true;
    public bool IsScrollContainer() => false;
    public bool IsRoot() => false;
    public bool IsFragmentainerBoxType() => false;
    public bool IsPaginatedRoot() => IsRoot() && Node.IsPaginatedRoot();

    public void SwapOutOfFlowPositionedCandidates(List<LogicalOofPositionedNode> candidates)
    {
        var temp = OofPositionedCandidates;
        OofPositionedCandidates.Clear();
        OofPositionedCandidates.AddRange(candidates);
        candidates.Clear();
        candidates.AddRange(temp);
    }

    public void SwapOutOfFlowFragmentainerDescendants(List<LogicalOofNodeForFragmentation> descendants)
    {
        var temp = OofPositionedFragmentainerDescendants;
        OofPositionedFragmentainerDescendants.Clear();
        OofPositionedFragmentainerDescendants.AddRange(descendants);
        descendants.Clear();
        descendants.AddRange(temp);
    }

    public void AddOutOfFlowChildCandidate(BlockNode child, LogicalOffset offset,
        LogicalStaticPosition.StaticInlinePosition inlineEdge = LogicalStaticPosition.StaticInlinePosition.Left,
        LogicalStaticPosition.StaticBlockPosition blockEdge = LogicalStaticPosition.StaticBlockPosition.Top,
        bool isHiddenForPaint = false, bool allowTopLayerNodes = false)
    {
        var box = child.Box;
        if (box != null && box.IsInTopOrViewTransitionLayer() && !allowTopLayerNodes)
            return;
        OofPositionedCandidates.Add(new LogicalOofPositionedNode(
            box ?? child.GetLayoutBox(),
            new LogicalStaticPosition(offset, inlineEdge, blockEdge, GetWritingDirection()),
            false, isHiddenForPaint,
            OofInlineContainer<LogicalOffset>.Empty));
    }

    public void AddOutOfFlowFragmentainerDescendant(LogicalOofNodeForFragmentation descendant)
    {
        OofPositionedFragmentainerDescendants.Add(descendant);
    }

    public void AddOutOfFlowDescendant(LogicalOofPositionedNode descendant)
    {
        OofPositionedDescendants.Add(descendant);
    }

    public void AddResult(LayoutResult result, LogicalOffset offset, BoxStrut margins,
        LayoutUnit? relativeOffset, OofInlineContainer<LogicalOffset> inlineContainer)
    {
        var fragment = new BoxFragment
        {
            InlineSize = result.Fragment.InlineSize,
            BlockSize = result.Fragment.BlockSize,
            InlineOffset = offset.InlineOffset,
            BlockOffset = offset.BlockOffset,
            Element = result.Fragment.Element,
            IsOutOfFlowPositioned = true,
            MarginLeft = margins.Left,
            MarginTop = margins.Top,
            MarginRight = margins.Right,
            MarginBottom = margins.Bottom,
        };
        Children.Add(fragment);
    }

    public void SetHasOutOfFlowFragmentChild(bool value) => HasOutOfFlowFragmentChild = value;
    public void SetHasOutOfFlowInFragmentainerSubtree(bool value) => HasOutOfFlowInFragmentainerSubtree = value;
    public void PropagateTallestUnbreakableBlockSize(float size)
    {
        TallestUnbreakableBlockSize = Math.Max(TallestUnbreakableBlockSize, size);
    }

    public void AdjustFixedposContainingBlockForFragmentainerDescendants() { }
    public void AdjustFixedposContainingBlockForInnerMulticols() { }
    public void AdjustFragmentainerDescendant(LogicalOofNodeForFragmentation descendant) { }

    public void PropagateOOFPositionedInfo(PhysicalFragment fragment, LogicalOffset offset, LogicalOffset relativeOffset,
        LogicalOffset offsetAdjustment, OofInlineContainer<LogicalOffset>? inlineContainer = null,
        LayoutUnit containingBlockAdjustment = default, OofContainingBlock<LogicalOffset>? containingBlock = null,
        OofContainingBlock<LogicalOffset>? fixedposContainingBlock = null,
        OofInlineContainer<LogicalOffset>? fixedposInlineContainer = null,
        LogicalOffset additionalFixedposOffset = default)
    {
    }
}