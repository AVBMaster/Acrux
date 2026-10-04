using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Break status returned by layout algorithms during fragmentation.
/// </summary>
public enum BreakStatus
{
    Continue,
    NeedsEarlierBreak,
    BrokeBefore,
}

/// <summary>
/// Break appeal for column breaks.
/// </summary>
public enum BreakAppeal
{
    LastResort,
    Perfect,
}

/// <summary>
/// Column fill behavior.
/// </summary>
public enum EColumnFill
{
    Auto,
    Balance,
}

/// <summary>
/// Column spanner path - models a chain of column spanners found during layout.
/// </summary>
public class ColumnSpannerPath
{
    public ColumnSpannerPath? Child { get; set; }
    public BlockNode Node { get; set; }

    public ColumnSpannerPath(BlockNode node) { Node = node; }

    public BlockNode GetBlockNode() => Node;
}

/// <summary>
/// Unpositioned list marker (for list items inside multi-column).
/// </summary>
public class UnpositionedListMarker
{
    public BlockNode MarkerNode { get; }

    public UnpositionedListMarker(BlockNode markerNode) { MarkerNode = markerNode; }
}

/// <summary>
/// Column layout algorithm for CSS multi-column layout.
/// Mirrors the modern layout pipeline's column layout algorithm.
/// </summary>
public class ColumnLayoutAlgorithm : LayoutAlgorithm
{
    private readonly BlockBreakToken? _breakToken;

    private int _usedColumnCount;
    private float _columnInlineSize;
    private float _columnInlineProgression;
    private float _columnBlockSize;
    private float _intrinsicBlockSize;
    private float _tallestUnbreakableBlockSize;
    private bool _isConstrainedByOuterFragmentationContext;
    private bool _hasProcessedFirstChild;
    private ColumnSpannerPath? _spannerPath;

    // An itinerary of multicol container parts to walk separately for layout. A
    // part is either a chunk of regular column content, or a column spanner.
    private class MulticolPartWalker
    {
        public struct Entry
        {
            public BlockBreakToken? BreakToken;
            public BlockNode? Spanner;

            public Entry(BlockBreakToken? token, BlockNode? spanner)
            {
                BreakToken = token;
                Spanner = spanner;
            }
        }

        private Entry _current;
        private BlockNode? _spanner;
        private readonly Element _multicolContainer;
        private readonly BlockBreakToken? _parentBreakToken;
        private BlockBreakToken? _nextColumnToken;
        private int _childTokenIdx;
        private bool _isFinished;

        public MulticolPartWalker(Element multicolContainer, BlockBreakToken? breakToken)
        {
            _multicolContainer = multicolContainer;
            _parentBreakToken = breakToken;
            _childTokenIdx = 0;
            UpdateCurrent();
            if (IsBreakInside(_parentBreakToken) && _current.BreakToken == null && _parentBreakToken!.HasSeenAllChildren)
                _isFinished = true;
        }

        public Entry Current()
        {
            System.Diagnostics.Debug.Assert(!_isFinished);
            return _current;
        }

        public bool IsFinished() => _isFinished;

        public void Next()
        {
            if (_isFinished) return;
            MoveToNext();
            if (!_isFinished) UpdateCurrent();
        }

        public void MoveToSpanner(BlockNode spanner, BlockBreakToken? nextColumnToken)
        {
            _spanner = spanner;
            _nextColumnToken = nextColumnToken;
            UpdateCurrent();
        }

        public void AddNextColumnBreakToken(BlockBreakToken nextColumnToken)
        {
            _nextColumnToken = nextColumnToken;
            UpdateCurrent();
        }

        public void UpdateNextColumnBreakToken(System.Collections.Generic.List<BoxFragment> children)
        {
            if (children.Count == 0) return;
            var lastChild = children[^1];
            if (lastChild.BreakToken is BlockBreakToken childBreakToken)
            {
                if (childBreakToken != _nextColumnToken)
                    _nextColumnToken = childBreakToken;
            }
        }

        private void UpdateCurrent()
        {
            System.Diagnostics.Debug.Assert(!_isFinished);
            if (_parentBreakToken != null)
            {
                var childBreakTokens = _parentBreakToken.ChildBreakTokens;
                if (_childTokenIdx < childBreakTokens.Count)
                {
                    var childBreakToken = childBreakTokens[_childTokenIdx];
                    if (childBreakToken.Node == null)
                    {
                        _current.Spanner = null;
                    }
                    else
                    {
                        _current.Spanner = new BlockNode(childBreakToken.Node);
                    }
                    _current.BreakToken = new BlockBreakToken { Node = childBreakToken.Node };
                    return;
                }
            }

            if (_spanner != null)
            {
                _current = new Entry(null, _spanner);
                return;
            }

            if (_nextColumnToken != null)
            {
                _current = new Entry(_nextColumnToken, null);
                return;
            }

            // The current entry is empty. That's only the case when we're at the very
            // start of the multicol container, or if we're past all children.
            System.Diagnostics.Debug.Assert(!_isFinished);
            System.Diagnostics.Debug.Assert(_current.Spanner == null);
            System.Diagnostics.Debug.Assert(_current.BreakToken == null);
        }

        private void MoveToNext()
        {
            if (_parentBreakToken != null)
            {
                var childBreakTokens = _parentBreakToken.ChildBreakTokens;
                if (_childTokenIdx < childBreakTokens.Count)
                {
                    _childTokenIdx++;
                    if (_childTokenIdx < childBreakTokens.Count)
                        return;
                }
            }

            if (_spanner != null)
            {
                var next = _multicolContainer.NextSibling as Element;
                if (next != null && next.ComputedStyle != null && next.ComputedStyle.GetColumnSpanAll())
                {
                    _spanner = new BlockNode(next.LayoutBox);
                    return;
                }
                _spanner = null;
                if (_nextColumnToken != null)
                    return;
            }

            _isFinished = true;
        }

        private static bool IsBreakInside(BlockBreakToken? token)
        {
            return token != null && !token.IsBreakBefore && !token.IsRepeated;
        }
    }

    public ColumnLayoutAlgorithm(Element node, in ConstraintSpace space, BlockBreakToken? breakToken = null)
        : base(node, space)
    {
        _breakToken = breakToken;

        // When a list item has multicol, we need to keep track of the list marker.
        if (node.IsListItem())
        {
            // The list marker positioning is simplified; the full upstream
            // logic (UnpositionedListMarker) is not wired into the simplified builder.
        }
    }

    public override LayoutResult Layout()
    {
        var border = LengthUtils.ComputeBorders(Style);
        var padding = LengthUtils.ComputePadding(Space, Style);
        var bp = new BoxStrut(border.Top + padding.Top, border.Right + padding.Right,
            border.Bottom + padding.Bottom, border.Left + padding.Left);

        Builder.BorderLeft = border.Left; Builder.BorderTop = border.Top;
        Builder.BorderRight = border.Right; Builder.BorderBottom = border.Bottom;
        Builder.PaddingLeft = padding.Left; Builder.PaddingTop = padding.Top;
        Builder.PaddingRight = padding.Right; Builder.PaddingBottom = padding.Bottom;
        Builder.Element = Node;

        // The border-box size for the multicol container. Honour an
        // explicit style width (px/em/rem/vw/…) instead of always stretching to
        // the parent's available size; fall back to stretch for auto widths.
        float borderBoxInlineSize = Space.HasDefiniteInlineSize ? Space.AvailableInlineSize : ChildAvailableInlineSize;
        float borderBoxBlockSize = Space.HasDefiniteBlockSize ? Space.AvailableBlockSize : ChildAvailableBlockSize;

        var availMinMax = new MinMaxSizes(borderBoxInlineSize, borderBoxInlineSize);
        float specifiedInline = LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
            _ => new MinMaxSizesResult(availMinMax));
        if (!LengthUtils.IsIndefinite(specifiedInline))
            borderBoxInlineSize = Math.Max(0, specifiedInline);

        // Mirror the inline axis for the block axis: honour an explicit height on
        // the multicol box itself. Without this the column height fell back to the
        // viewport's available block size, so a short fixed-height multicol put all
        // content in the first column instead of filling/balancing the fragmentainer.
        if (Style.Height is not AutoLength)
        {
            float specifiedBlock = LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp,
                borderBoxBlockSize, borderBoxInlineSize);
            if (!LengthUtils.IsIndefinite(specifiedBlock))
                borderBoxBlockSize = Math.Max(0, specifiedBlock);
        }

        // |columnBlockSize_| isn't the content-box size, as |BorderScrollbarPadding()|
        // has been adjusted for fragmentation. Preserve the original semantics: the
        // column block size is the content-box block size.
        _columnBlockSize = Math.Max(0, borderBoxBlockSize - bp.Top - bp.Bottom);

        float childAvailableInlineSize = Math.Max(0, borderBoxInlineSize - bp.HorizontalSum);
        System.Diagnostics.Debug.Assert(childAvailableInlineSize >= 0);
        _columnInlineSize = ResolveUsedColumnInlineSize(childAvailableInlineSize, Style);
        _columnInlineProgression = _columnInlineSize + ResolveUsedColumnGap(childAvailableInlineSize, Style);
        _usedColumnCount = ResolveUsedColumnCount(childAvailableInlineSize, Style);

        // Write the column inline-size and count back to the flow thread if
        // we're at the first fragment.
        if (!IsBreakInside(_breakToken))
        {
            // Store column size and count (TextAutosizer / legacy machinery).
        }

        // If we know the block-size of the fragmentainers in an outer fragmentation
        // context (if any), our columns may be constrained by that.
        _isConstrainedByOuterFragmentationContext = Space.HasBlockFragmentation;

        _intrinsicBlockSize = bp.Top;

        // Self-contained column layout for the common (spanner-free, inline or
        // block content) case. The full flow-thread/fragmentainer machinery in
        // this file is ported but not wired; rather than depend on it, lay the
        // flow out once at the column inline-size, then distribute its line boxes
        // across balanced column fragmentainers. Each column becomes an anonymous
        // child box positioned side by side. The line-breaker overflow fix makes
        // wrapping at the narrow column width correct.
        // A spanning element breaks the flow into column sets stacked vertically
        // (CSS Multi-Column 1 §3): the content before and after it fills its own
        // set of columns, and the spanner takes the full content width.
        var columnFlow = CollectColumnFlowChildren();
        foreach (var flowChild in columnFlow)
        {
            if (flowChild.ComputedStyle!.GetColumnSpanAll())
                return LayoutWithSpanners(columnFlow, bp, borderBoxInlineSize, childAvailableInlineSize);
        }

        float colInlineSize = _columnInlineSize;
        float colProgression = _columnInlineProgression;
        int colCount = Math.Max(1, _usedColumnCount);

        var contentSpace = Space.InheritBuilder(colInlineSize, float.NaN)
            .SetIsNewFormattingContext(true)
            .SetAvailableSize(colInlineSize, float.NaN)
            .SetIsFixedInlineSize(true)
            .SetPercentageResolution(colInlineSize, ChildAvailableBlockSize)
            .SetBfcBlockOffset(0)
            .SetForcedBfcBlockOffset(0)
            .SetDirection(Style.Direction == "rtl" ? TextDirection.Rtl : TextDirection.Ltr)
            .ToConstraintSpace();

        List<BoxLine> allLines;
        float flowBlock = 0;
        float flowContent = 0;
        List<float> leadingMargins = new();
        try
        {
            if (Node.IsInlineFormattingContextRoot())
            {
                var inlineResult = new InlineLayoutAlgorithm(Node, contentSpace, this).Layout();
                allLines = new List<BoxLine>(inlineResult.Fragment.Lines);
            }
            else
            {
                // The flow thread sizes to the COLUMN, never to the container:
                // re-running the block algorithm on Node would let the container's
                // own specified width win over the fragmentainer space (a block
                // hands its children its content inline size, not the space it was
                // given), which puts every line at the container width and makes
                // the columns paint on top of each other.
                allLines = LayoutFlowRun(columnFlow, 0, columnFlow.Count, colInlineSize,
                    out flowContent, out flowBlock, out leadingMargins);
            }
        }
        catch
        {
            allLines = new List<BoxLine>();
        }

        // Balance columns unless the container has an explicit block-size. Note:
        // a definite *available* block size (from the viewport) must not disable
        // balancing 鈥?only an explicit height on the multicol box itself fixes
        // the column height.
        bool definiteHeight = Style.ColumnFill() == EColumnFill.Auto && Style.Height is not AutoLength && _columnBlockSize > 0;
        float columnBlockSize = definiteHeight
            ? _columnBlockSize
            : BalancedColumnBlockSize(allLines, colCount);

        var columnFragments = DistributeLinesToColumns(allLines, columnBlockSize, colInlineSize, colProgression,
            colCount, bp, blockOffsetBase: 0, flowTotalBlock: flowBlock);

        float usedColumnBlockSize = 0;
        foreach (var col in columnFragments)
            usedColumnBlockSize = Math.Max(usedColumnBlockSize, col.BlockSize);

        _intrinsicBlockSize = bp.Top + usedColumnBlockSize + bp.Bottom;

        float finalBlockSize = LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, _intrinsicBlockSize, borderBoxInlineSize);

        Builder.InlineSize = borderBoxInlineSize;
        Builder.BlockSize = finalBlockSize;
        Builder.IntrinsicBlockSize = _intrinsicBlockSize;
        // The parent positions this box by the BFC line offset it handed us; a
        // multicol that never reports one back lands at inline offset 0 and loses
        // its ancestors' padding/margin shift.
        Builder.BfcLineOffset = Space.GetBfcOffset().LineOffset;

        // A5: export resolved column geometry for column-rule painting.
        Builder.IsMultiColumn = colCount > 1;
        Builder.UsedColumnCount = colCount;
        Builder.ColumnInlineSize = colInlineSize;
        Builder.ColumnProgression = colProgression;

        var fragment = Builder.ToBoxFragment();

        // Unit-resolution context: flatten the column fragmentainers into the container's own line
        // list. The legacy painting pipeline walks Dom.LayoutBox.Lines of REAL
        // elements only — anonymous child boxes would never be visited — so each
        // column's lines are shifted by the column's inline offset and merged
        // into the container. Children stay attached for structural fidelity.
        foreach (var col in columnFragments)
        {
            foreach (var line in col.Lines)
            {
                ShiftLineToColumn(line, col);
                fragment.Lines.Add(line);
            }
        }
        fragment.Children.AddRange(columnFragments);
        var result = LayoutResult.FromFragment(fragment);
        result.IntrinsicBlockSize = _intrinsicBlockSize;
        result.BfcLineOffset = Space.GetBfcOffset().LineOffset;
        result.BfcBlockOffsetValue = Space.ForcedBfcBlockOffset ?? Space.GetBfcOffset().BlockOffset;
        return result;
    }

    private static void ShiftLineToColumn(BoxLine line, BoxFragment column) =>
        ShiftLineToColumn(line, column.InlineOffset, column.BlockOffset);

    /// <summary>Move one line box from a column fragmentainer's own coordinates
    /// into the multicol container's border-box space. The converter anchors a
    /// box's lines on its border box, so the fragmentainer offset has to be
    /// folded in. Runs keep their line-relative inline offset.</summary>
    private static void ShiftLineToColumn(BoxLine line, float inlineDelta, float blockDelta)
    {
        line.InlineOffset += inlineDelta;
        line.BlockOffset += blockDelta;
        line.BaselineOffset += blockDelta;
        foreach (var run in line.Runs)
        {
            run.BlockOffset += blockDelta;
            run.BaselineOffset += blockDelta;
        }
    }

    /// <summary>
    /// Lay a run of in-flow multicol children out at the fragmentainer's column
    /// inline-size and return their line boxes stacked into one absolute flow.
    /// Each child is laid out on its own rather than by re-running the block
    /// algorithm on the container: a block sizes its children from its own
    /// resolved width, so the container's specified width would otherwise reach
    /// the flow thread and the columns would overlap (CSS Multi-Column 1 §3 -
    /// the container's width sizes the whole multicol, the columns divide its
    /// content box). <paramref name="flowContentEnd"/> is where the run's content
    /// stops - the block-end margin of its last child is excluded, because a
    /// fragmentainer that is followed by another one (or by a spanner) drops the
    /// margin at the break (CSS Fragmentation 1 §3.1). <paramref name="flowBlockSize"/>
    /// keeps that margin: it is the size of a run that ends the flow inside the
    /// fragmentainer that holds its last line.
    /// </summary>
    private List<BoxLine> LayoutFlowRun(List<Element> children, int start, int count,
        float colInlineSize, out float flowContentEnd, out float flowBlockSize,
        out List<float> leadingMargins)
    {
        var lines = new List<BoxLine>();
        leadingMargins = new List<float>();
        flowContentEnd = 0;
        flowBlockSize = 0;
        float flowBlock = 0;
        float trailingMargin = 0;
        int end = start + count;
        for (int i = start; i < end; i++)
        {
            var child = children[i];
            // Adjacent siblings in the column flow collapse exactly as they do in a
            // block container - the multicol is a new formatting context only for its
            // OWN margins (CSS 2.1 §10.5.3). Lay the flow out at the column width, not
            // through the container's block algorithm, so the rule has to be applied
            // here; adding the two margins instead produced lines a whole margin apart.
            float marginBefore = i > start
                ? CollapseBlockMargins(trailingMargin, ResolveBlockMargin(child, blockStart: true))
                : ResolveBlockMargin(child, blockStart: true);
            flowBlock += marginBefore;
            var childResult = LayoutColumnChild(child, MakeColumnChildSpace(colInlineSize));
            var childFragment = childResult.Fragment;
            var childLines = new List<BoxLine>(childFragment.Lines);
            CollectChildLines(childFragment, childLines);
            foreach (var line in childLines)
            {
                line.BlockOffset += flowBlock;
                line.BaselineOffset += flowBlock;
                foreach (var run in line.Runs)
                {
                    run.BlockOffset += flowBlock;
                    run.BaselineOffset += flowBlock;
                }
            }
            // Every line records the block-start margin of the block it came from: a
            // fragmentainer that continues that block is measured as if the margin were
            // applied again at its top, even though the placed lines sit flush with the
            // column top. That is what makes Chrome's balanced height taller than a
            // plain line-boundary division.
            for (int l = 0; l < childLines.Count; l++)
                leadingMargins.Add(marginBefore);
            lines.AddRange(childLines);
            flowBlock += childFragment.BlockSize;
            flowContentEnd = flowBlock;
            trailingMargin = ResolveBlockMargin(child, blockStart: false);
            flowBlockSize = flowBlock + trailingMargin;
        }
        return lines;
    }

    /// <summary>CSS 2.1 §10.5.3 for one pair of adjacent block-level margins.</summary>
    private static float CollapseBlockMargins(float first, float second)
    {
        if (first >= 0 && second >= 0) return MathF.Max(first, second);
        if (first <= 0 && second <= 0) return MathF.Min(first, second);
        return MathF.Max(first, second) + MathF.Min(first, second);
    }

    /// <summary>In-flow children of the multicol container, in document order.</summary>
    private List<Element> CollectColumnFlowChildren()
    {
        var children = new List<Element>();
        foreach (var node in Node.Children)
        {
            if (node is not Element element || element.ComputedStyle == null
                || element.ComputedStyle.Display == DisplayType.None)
                continue;
            children.Add(element);
        }
        return children;
    }

    /// <summary>Constraint space for content flowing inside one column, or across
    /// all of them for a spanner.</summary>
    private ConstraintSpace MakeColumnChildSpace(float inlineSize)
    {
        return Space.InheritBuilder(inlineSize, float.NaN)
            .SetIsNewFormattingContext(true)
            .SetAvailableSize(inlineSize, float.NaN)
            .SetIsFixedInlineSize(true)
            .SetPercentageResolution(inlineSize, ChildAvailableBlockSize)
            .SetBfcBlockOffset(0)
            .SetForcedBfcBlockOffset(0)
            .SetDirection(Style.Direction == "rtl" ? TextDirection.Rtl : TextDirection.Ltr)
            .ToConstraintSpace();
    }

    private LayoutResult LayoutColumnChild(Element child, ConstraintSpace space)
    {
        var style = child.ComputedStyle!;
        var display = style.Display;
        if (display is DisplayType.Flex or DisplayType.InlineFlex)
            return new FlexLayoutAlgorithm(child, space).Layout();
        if (display is DisplayType.Grid or DisplayType.InlineGrid)
            return new GridLayoutAdapter(child, space).Layout();
        if (display == DisplayType.Table)
            return new Table.TableLayoutAlgorithm(child, space).Layout();
        if (child.IsInlineFormattingContextRoot())
            return new InlineLayoutAlgorithm(child, space, this).Layout();
        return new BlockLayoutAlgorithm(child, space).Layout();
    }

    private static float ResolveBlockMargin(Element child, bool blockStart)
    {
        var style = child.ComputedStyle!;
        var length = blockStart ? style.MarginTop : style.MarginBottom;
        return length?.ToPixels(style.FontSize, 16, 0, 0) ?? 0;
    }

    /// <summary>
    /// Lay the flow out as column sets separated by full-width spanners. Each set
    /// balances its own content across the columns exactly like the spanner-free
    /// path; the spanner box is placed between the sets at the container's content
    /// width (CSS Multi-Column 1 §3).
    /// </summary>
    private LayoutResult LayoutWithSpanners(List<Element> flowChildren, BoxStrut bp,
        float borderBoxInlineSize, float childAvailableInlineSize)
    {
        float colInlineSize = _columnInlineSize;
        float colProgression = _columnInlineProgression;
        int colCount = Math.Max(1, _usedColumnCount);
        bool definiteHeight = Style.ColumnFill() == EColumnFill.Auto && Style.Height is not AutoLength && _columnBlockSize > 0;

        var columnFragments = new List<BoxFragment>();
        var spannerFragments = new List<BoxFragment>();
        float currentBlock = 0;
        int index = 0;
        while (index < flowChildren.Count)
        {
            var child = flowChildren[index];
            if (child.ComputedStyle!.GetColumnSpanAll())
            {
                float marginBefore = ResolveBlockMargin(child, blockStart: true);
                var spanResult = LayoutColumnChild(child, MakeColumnChildSpace(childAvailableInlineSize));
                var spanFragment = spanResult.Fragment;
                float marginAfter = ResolveBlockMargin(child, blockStart: false);
                // The spanner is a REAL element, so the converter anchors it on the
                // container's content box and adds the border/padding strut itself.
                // Folding |bp| in here as well painted it one strut too far right and
                // down - far enough to cover the first line of the set below it.
                spanFragment.InlineOffset = 0;
                spanFragment.BlockOffset = currentBlock + marginBefore;
                spannerFragments.Add(spanFragment);
                currentBlock += marginBefore + spanFragment.BlockSize + marginAfter;
                index++;
                continue;
            }

            // One run of non-spanning children: lay them out at the column width,
            // stack their line boxes, then balance them across the columns.
            int runStart = index;
            while (index < flowChildren.Count && !flowChildren[index].ComputedStyle!.GetColumnSpanAll())
                index++;
            var lines = LayoutFlowRun(flowChildren, runStart, index - runStart, colInlineSize,
                out float flowBlock, out _, out List<float> runLeadingMargins);

            float columnBlockSize = definiteHeight
                ? _columnBlockSize
                : BalancedColumnBlockSize(lines, colCount, runLeadingMargins);
            var columns = DistributeLinesToColumns(lines, columnBlockSize, colInlineSize, colProgression,
                colCount, bp, currentBlock, flowBlock, runLeadingMargins);
            float setHeight = 0;
            foreach (var column in columns)
            {
                foreach (var line in column.Lines)
                {
                    // DistributeLinesToColumns already rebases the lines onto
                    // |currentBlock|, so only the container's own border/padding
                    // offset is still missing here.
                    ShiftLineToColumn(line, column.InlineOffset, bp.Top);
                    Builder.Lines.Add(line);
                }
                setHeight = Math.Max(setHeight, column.BlockSize);
                columnFragments.Add(column);
            }
            currentBlock += setHeight;
        }

        _intrinsicBlockSize = bp.Top + currentBlock + bp.Bottom;
        float blockSize = LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, _intrinsicBlockSize,
            borderBoxInlineSize);
        if (LengthUtils.IsIndefinite(blockSize))
            blockSize = _intrinsicBlockSize;

        Builder.InlineSize = borderBoxInlineSize;
        Builder.BlockSize = blockSize;
        Builder.IntrinsicBlockSize = _intrinsicBlockSize;
        Builder.BfcLineOffset = Space.GetBfcOffset().LineOffset;
        Builder.IsMultiColumn = colCount > 1;
        Builder.UsedColumnCount = colCount;
        Builder.ColumnInlineSize = colInlineSize;
        Builder.ColumnProgression = colProgression;

        var fragment = Builder.ToBoxFragment();
        fragment.Lines.AddRange(Builder.Lines);
        fragment.Children.AddRange(columnFragments);
        // BoxFragmentBuilder does not carry children over, so the spanner boxes
        // (which are real elements and have to paint) are attached explicitly.
        fragment.Children.AddRange(spannerFragments);
        var result = LayoutResult.FromFragment(fragment);
        result.IntrinsicBlockSize = _intrinsicBlockSize;
        result.BfcLineOffset = Space.GetBfcOffset().LineOffset;
        result.BfcBlockOffsetValue = Space.ForcedBfcBlockOffset ?? Space.GetBfcOffset().BlockOffset;
        return result;
    }

    /// <summary>
    /// Partition the flow's line boxes into column fragmentainers. Each column
    /// keeps lines until it reaches <paramref name="columnBlockSize"/> (the last
    /// column absorbs the remainder), producing an anonymous, side-by-side child
    /// box whose lines are shifted to start at the column's top.
    /// </summary>
    private static void CollectChildLines(BoxFragment fragment, List<BoxLine> into)
    {
        foreach (var child in fragment.Children)
        {
            foreach (var line in child.Lines)
            {
                // Child lines are in the child's local coordinates; shift every
                // vertical field by the child's block offset so the column
                // distributor sees one consistent absolute flow. Shifting only
                // |line.BlockOffset| left the runs/baseline behind, so the first
                // column (whose rebasing delta is 0) rendered overlapping lines.
                line.BlockOffset += child.BlockOffset;
                line.BaselineOffset += child.BlockOffset;
                foreach (var run in line.Runs)
                {
                    run.BlockOffset += child.BlockOffset;
                    run.BaselineOffset += child.BlockOffset;
                }
                into.Add(line);
            }
            CollectChildLines(child, into);
        }
    }

    /// <summary>
    /// Greedy fragmentainer fill, without touching the line geometry: assign lines
    /// to fragmentainers of <paramref name="fragmentainerBlockSize"/> until the flow
    /// runs out or the columns are used up, always taking at least one line per
    /// column. Each range is written to <paramref name="fills"/>; the return value
    /// says whether lines were left over, i.e. whether the height was too small.
    /// </summary>
    private static bool PlanColumnFills(List<BoxLine> lines, float fragmentainerBlockSize, int colCount,
        List<(int Start, int End)> fills, List<float>? leadingMargins = null)
    {
        fills.Clear();
        float flowOrigin = 0;
        int idx = 0;
        while (idx < lines.Count && fills.Count < colCount)
        {
            int start = idx;
            float colOrigin = flowOrigin;
            // A fragmentainer always takes the line it broke on, then keeps going
            // while the *next* line still fits - testing after consuming would let
            // every column swallow one line too many and report a flow that never
            // overflowed.
            float columnExtra = leadingMargins is { } extras && start > 0 && start < extras.Count
                ? extras[start]
                : 0f;
            idx++;
            while (idx < lines.Count &&
                   lines[idx].BlockEnd - colOrigin + columnExtra <= fragmentainerBlockSize + 0.5f)
                idx++;
            flowOrigin = lines[idx - 1].BlockEnd;
            fills.Add((start, idx));
        }
        return idx < lines.Count;
    }

    /// <summary>
    /// Balanced column height for an auto-height multicol: the content divided by
    /// the column count is a lower bound, but it only holds when every break lands
    /// on a line boundary. A break that cannot be taken (a line taller than the
    /// remainder) pushes content into the next fragmentainer, so the bound is grown
    /// until the flow really fits the columns - the smallest such height, which is
    /// what "balance the columns" means (CSS Multi-Column 1 §3.2). Without this the
    /// leftover piled up in the last column and the box came out a line or two
    /// taller than its neighbours.
    /// </summary>
    private static float BalancedColumnBlockSize(List<BoxLine> lines, int colCount,
        List<float>? leadingMargins = null)
    {
        if (lines.Count == 0 || colCount <= 1)
            return lines.Count > 0 ? lines[^1].BlockEnd : 0;

        float total = lines[^1].BlockEnd;
        float low = MathF.Ceiling(total / colCount);
        if (low <= 0)
            return 0;

        var fills = new List<(int Start, int End)>();
        if (!PlanColumnFills(lines, low, colCount, fills, leadingMargins))
            return low;

        // Monotone in the height: a taller fragmentainer never needs more columns,
        // so binary search the first height that fits.
        float high = total;
        while (high - low > 1f)
        {
            float mid = MathF.Ceiling((low + high) / 2f);
            if (mid <= low || mid >= high)
                break;
            if (PlanColumnFills(lines, mid, colCount, fills, leadingMargins))
                low = mid;
            else
                high = mid;
        }
        return high;
    }

    private List<BoxFragment> DistributeLinesToColumns(List<BoxLine> allLines, float columnBlockSize,
        float colInlineSize, float colProgression, int colCount, BoxStrut bp, float blockOffsetBase = 0,
        float flowTotalBlock = 0, List<float>? leadingMargins = null)
    {
        var columns = new List<BoxFragment>();
        if (allLines.Count == 0)
            return columns;

        const float epsilon = 0.5f;
        int idx = 0;
        // Where the previous fragmentainer stopped consuming the flow. Measuring a
        // column's fill from the first line placed in it (the old behaviour) hid the
        // flow's leading margin, so a fixed-height multicol over-filled its first
        // column by exactly that margin (CSS Multi-Column 1 §3.2: a fragmentainer
        // holds at most its block-size worth of flow). The set's own flow starts at
        // zero; |blockOffsetBase| is only where the rebased column lands, so it must
        // not enter the measurement.
        float flowOrigin = 0;
        for (int c = 0; c < colCount && idx < allLines.Count; c++)
        {
            bool lastColumn = c == colCount - 1;
            float colOrigin = flowOrigin;
            var colFragment = new BoxFragment
            {
                InlineSize = colInlineSize,
                BlockSize = columnBlockSize,
                InlineOffset = bp.Left + c * colProgression,
                BlockOffset = bp.Top + blockOffsetBase,
                Element = null,
            };

            float maxBottom = 0;
            // The margin the line that opens this fragmentainer brings with it.
            float columnExtra = idx > 0 && leadingMargins is { Count: > 0 } ? leadingMargins[idx] : 0f;
            while (idx < allLines.Count)
            {
                var line = allLines[idx];
                // Read the flow position before the line is rebased onto this
                // fragmentainer: measuring afterwards hands the next column a
                // column-local origin, and every column after it drifts down by
                // everything already consumed.
                float flowEnd = line.BlockEnd;
                // The fill is measured with the block-start margin re-applied for a
                // continuation column; the column's own height keeps counting placed
                // geometry only, because the margin is not painted a second time.
                float rel = flowEnd - colOrigin;
                if (!lastColumn && colFragment.Lines.Count > 0 &&
                    rel + columnExtra > columnBlockSize + epsilon)
                    break;

                float delta = blockOffsetBase - colOrigin;
                line.BlockOffset += delta;
                line.BaselineOffset += delta;
                foreach (var run in line.Runs)
                    run.BlockOffset += delta;

                colFragment.Lines.Add(line);
                // Measured from the fragmentainer's own top, which sits at
                // |blockOffsetBase| when a column set follows a spanner.
                maxBottom = Math.Max(maxBottom, rel);
                flowOrigin = flowEnd;
                idx++;
            }

            // The balanced columnBlockSize is the target for every fragmentainer;
            // the LAST column absorbs the remainder, so grow it to its real content
            // height. Non-last columns keep the balance (they overflowed by design).
            // The flow's trailing block margin occupies the box only when the content
            // really ends in this, the last fragmentainer: a multicol is its own
            // formatting context so the margin does not collapse out of the box
            // (CSS 2.1 §10.5.3), but a break at a fragmentainer edge drops it
            // (CSS Fragmentation 1 §3.1), and unused columns after the flow's end are
            // never created, so |lastColumn| is exactly that test.
            if (lastColumn && flowTotalBlock > colOrigin)
                maxBottom = Math.Max(maxBottom, flowTotalBlock - colOrigin);
            if (maxBottom > colFragment.BlockSize)
                colFragment.BlockSize = maxBottom;
            columns.Add(colFragment);
        }

        return columns;
    }

    public MinMaxSizesResult ComputeMinMaxSizes(MinMaxSizesFloatInput input)
    {
        float overrideIntrinsicInlineSize = Style.Width is PixelLength pl ? pl.Value : float.NaN;
        if (!float.IsNaN(overrideIntrinsicInlineSize))
        {
            float borderPaddingInline = BorderLeftRight + PaddingLeft + PaddingRight;
            float size = borderPaddingInline + overrideIntrinsicInlineSize;
            return new MinMaxSizesResult(new MinMaxSizes(size, size));
        }

        // First calculate the min/max sizes of columns using block layout.
        var space = CreateConstraintSpaceForMinMax();
        var algorithm = new BlockLayoutAlgorithm(Node, space);
        var layoutResult = algorithm.Layout();
        var result = new MinMaxSizesResult(new MinMaxSizes(layoutResult.Fragment.InlineSize, layoutResult.Fragment.InlineSize));

        // How column-width affects min/max sizes. (The old and only spec.)
        float minSize = result.Sizes.MinSize;
        float maxSize = result.Sizes.MaxSize;
        if (!Style.HasAutoColumnWidth())
        {
            float columnWidth = Style.ColumnWidth is PixelLength colPl ? colPl.Value : minSize;
            minSize = Math.Min(minSize, columnWidth);
            maxSize = Math.Max(maxSize, columnWidth);
            maxSize = Math.Max(maxSize, minSize);
        }

        // Now convert those column min/max values to multicol container min/max
        // values. We typically have multiple columns and also gaps between them.
        int columnCount = Style.ColumnCount;
        System.Diagnostics.Debug.Assert(columnCount >= 1);
        float columnGap = ResolveUsedColumnGap(0, Style);
        float gapExtra = columnGap * (columnCount - 1);

        // column-count (and therefore also column-gap) is ignored in intrinsic min
        // inline-size calculation, if column-width is specified.
        if (Style.HasAutoColumnWidth())
        {
            minSize = minSize * columnCount + gapExtra;
        }
        maxSize = maxSize * columnCount + gapExtra;

        // The block layout algorithm skips spanners for min/max calculation.
        if (Style.Contain == ContainType.None)
        {
            var spannerSizes = ComputeSpannersMinMaxSizes(Node).Sizes;
            minSize = Math.Max(minSize, spannerSizes.MinSize);
            maxSize = Math.Max(maxSize, spannerSizes.MaxSize);
        }

        float bpInlineSum = BorderLeft + BorderRight + PaddingLeft + PaddingRight;
        return new MinMaxSizesResult(new MinMaxSizes(minSize + bpInlineSum, maxSize + bpInlineSum));
    }

    // Create an empty column fragment, modeled after an existing column. The
    // resulting column may then be used and mutated by the out-of-flow layout
    // code, to add out-of-flow descendants.
    public static PhysicalBoxFragment CreateEmptyColumn(Element node, ConstraintSpace parentSpace, PhysicalBoxFragment previousColumn)
    {
        // Simplified: create an empty column fragment matching the previous column
        // geometry.
        var result = new PhysicalBoxFragment
        {
            Size = previousColumn.Size,
        };
        result.Box = PhysicalFragment.BoxType.ColumnBox;
        return result;
    }

    private MinMaxSizesResult ComputeSpannersMinMaxSizes(Element searchParent)
    {
        MinMaxSizesResult result = new MinMaxSizesResult();
        foreach (var child in searchParent.Children)
        {
            if (child is not Element el) continue;
            if (el.ComputedStyle == null || !el.ComputedStyle.GetColumnSpanAll()) continue;
            // A spanner contributes its full min/max inline size.
            var childResult = ComputeSpannersMinMaxSizesHelper(el);
            result.Sizes = ColumnLayoutAlgorithmExtensions.Encompass(result.Sizes, childResult);
        }
        return result;
    }

    private MinMaxSizes ComputeSpannersMinMaxSizesHelper(Element spanner)
    {
        if (spanner.LayoutBox != null)
        {
            return new MinMaxSizes(spanner.LayoutBox.Width, spanner.LayoutBox.Width);
        }
        return new MinMaxSizes(0, float.MaxValue);
    }

    private BreakStatus LayoutChildren(BoxStrut bp)
    {
        MarginStrut marginStrut = MarginStrut.Zero;
        var walker = new MulticolPartWalker(Node, _breakToken);

        while (!walker.IsFinished())
        {
            var entry = walker.Current();

            // If this is regular column content (i.e. not a spanner), or we're at the
            // very start, perform column layout.
            if (entry.Spanner == null)
            {
                var result = LayoutRow(entry.BreakToken, 0, bp, ref marginStrut);
                if (result == null)
                {
                    // An outer fragmentainer break was inserted before this row.
                    System.Diagnostics.Debug.Assert(Space.HasBlockFragmentation);
                    break;
                }

                walker.Next();

                var nextColumnToken = result.Fragment.BreakToken;

                if (result.SpannerNode() != null)
                {
                    // We found a spanner. Move the walker to the spanner.
                    walker.MoveToSpanner(result.SpannerNode()!, nextColumnToken);
                    continue;
                }

                if (nextColumnToken != null)
                    walker.AddNextColumnBreakToken(nextColumnToken);

                break;
            }

            // Attempt to lay out one column spanner.
            BlockNode spannerNode = entry.Spanner!;

            // Handle any OOF fragmentainer descendants that were found before the spanner.
            walker.UpdateNextColumnBreakToken(Builder.Children);

            BreakStatus breakStatus = LayoutSpanner(spannerNode, entry.BreakToken, bp, ref marginStrut);

            walker.Next();

            if (breakStatus == BreakStatus.NeedsEarlierBreak)
                return breakStatus;
            if (breakStatus == BreakStatus.BrokeBefore || Builder.HasInflowChildBreakInside())
                break;
        }

        if (!walker.IsFinished() || Builder.HasInflowChildBreakInside())
        {
            // We broke in the main flow. Let this multicol container take up any
            // remaining space.
            _intrinsicBlockSize = Math.Max(_intrinsicBlockSize, FragmentainerSpaceLeftForChildren());

            // Go through any remaining parts that we didn't get to, and push them as
            // break tokens for the next (outer) fragmentainer to handle.
            for (; !walker.IsFinished(); walker.Next())
            {
                var entry = walker.Current();
                if (entry.BreakToken != null)
                {
                    Builder.AddBreakToken(entry.BreakToken);
                }
                else if (entry.Spanner != null)
                {
                    Builder.AddBreakBeforeChild(entry.Spanner!, BreakAppeal.Perfect, false);
                }
            }
        }
        else
        {
            // We've gone through all the content.
            Builder.HasSeenAllChildren = true;
            _intrinsicBlockSize += marginStrut.Sum;
        }

        return BreakStatus.Continue;
    }

    private LayoutResult? LayoutRow(BlockBreakToken? nextColumnToken, float minimumColumnBlockSize, BoxStrut bp, ref MarginStrut marginStrut)
    {
        LogicalSize columnSize = new LogicalSize(_columnInlineSize, _columnBlockSize);

        // Calculate the block-offset by including any trailing margin from a previous
        // adjacent column spanner.
        float rowOffset = _intrinsicBlockSize + marginStrut.Sum;

        // If block-size is non-auto, subtract the space for content we've consumed in
        // previous fragments. This is necessary when we're nested inside another
        // fragmentation context.
        if (!float.IsNaN(columnSize.BlockSize) && columnSize.BlockSize != 0)
        {
            if (_breakToken != null && _isConstrainedByOuterFragmentationContext)
                columnSize = new LogicalSize(columnSize.InlineSize, columnSize.BlockSize - _breakToken.ConsumedBlockSize);

            // Subtract the space already taken in the current fragment (spanners and
            // earlier column rows).
            columnSize = new LogicalSize(columnSize.InlineSize, Math.Max(0, columnSize.BlockSize - CurrentContentBlockOffset(rowOffset)));
        }

        bool mayResumeInNextOuterFragmentainer = false;
        float availableOuterSpace = float.NaN;
        if (_isConstrainedByOuterFragmentationContext)
        {
            availableOuterSpace = Math.Max(minimumColumnBlockSize, FragmentainerSpaceLeftForChildren() - rowOffset);
            System.Diagnostics.Debug.Assert(availableOuterSpace >= 0);

            // Determine if we should resume layout in the next outer fragmentation
            // context if we run out of space in the current one.
            if (float.IsNaN(columnSize.BlockSize) || columnSize.BlockSize > availableOuterSpace)
                mayResumeInNextOuterFragmentainer = true;
        }

        bool shrinkToFitColumnBlockSize = false;

        // If column-fill is 'balance', we should of course balance. Additionally, we
        // need to do it if we're *inside* another multicol container that's
        // performing its initial column balancing pass.
        bool balanceColumns = Style.ColumnFill() == EColumnFill.Balance
            || (Space.HasBlockFragmentation && !Space.HasDefiniteBlockSize);

        // If columns are to be balanced, we need to examine the contents of the
        // multicol container to figure out a good initial column block-size.
        bool hasContentBasedBlockSize = balanceColumns || (float.IsNaN(columnSize.BlockSize) && !_isConstrainedByOuterFragmentationContext);

        if (hasContentBasedBlockSize)
        {
            columnSize = new LogicalSize(columnSize.InlineSize, ResolveColumnAutoBlockSize(columnSize, rowOffset, availableOuterSpace, nextColumnToken, balanceColumns));
        }
        else if (!float.IsNaN(availableOuterSpace))
        {
            // Finally, resolve any remaining auto block-size, and make sure that we
            // don't take up more space than there's room for in the outer fragmentation
            // context.
            if (float.IsNaN(columnSize.BlockSize) || columnSize.BlockSize > availableOuterSpace)
            {
                if (float.IsNaN(columnSize.BlockSize))
                    shrinkToFitColumnBlockSize = true;
                columnSize = new LogicalSize(columnSize.InlineSize, availableOuterSpace);
            }
        }

        System.Diagnostics.Debug.Assert(columnSize.BlockSize >= 0);

        // New column fragments won't be added to the fragment builder right away,
        // since we may need to delete them and try again with a different block-size
        // (column balancing). Keep them in this list.
        var newColumns = new System.Collections.Generic.List<LayoutResult>();
        bool isEmptySpannerParent = false;

        // Avoid suboptimal breaks inside a nested multicol if we can.
        bool mayHaveMoreSpaceInNextOuterFragmentainer = false;
        if (mayResumeInNextOuterFragmentainer && !IsBreakInside(_breakToken))
        {
            if (_intrinsicBlockSize > 0)
                mayHaveMoreSpaceInNextOuterFragmentainer = true;
        }

        LayoutResult? result = null;
        BreakAppeal? minBreakAppeal = null;
        float intrinsicBlockSizeContribution = 0;

        do
        {
            BlockBreakToken? columnBreakToken = nextColumnToken;
            bool hasViolatingBreak = false;

            float columnInlineOffset = BorderLeft + PaddingLeft;
            int actualColumnCount = 0;
            int forcedBreakCount = 0;

            // Each column should calculate their own minimal space shortage.
            float minimalSpaceShortage = float.NaN;

            minBreakAppeal = null;
            intrinsicBlockSizeContribution = 0;

            do
            {
                // Lay out one column. Each column will become a fragment.
                var childSpace = CreateConstraintSpaceForFragmentainer(columnSize, balanceColumns, minBreakAppeal ?? BreakAppeal.LastResort);

                var childAlgorithm = new BlockLayoutAlgorithm(Node, childSpace);
                childAlgorithm.SetBoxType(PhysicalFragment.BoxType.ColumnBox);
                result = childAlgorithm.Layout();
                var column = result.Fragment;
                intrinsicBlockSizeContribution = columnSize.BlockSize;

                if (shrinkToFitColumnBlockSize)
                {
                    // Shrink-to-fit the row block-size contribution from the first column
                    // if we're nested inside another fragmentation context.
                    intrinsicBlockSizeContribution = Math.Min(intrinsicBlockSizeContribution, result.IntrinsicBlockSize);
                    shrinkToFitColumnBlockSize = false;
                }

                // Add the new column fragment to the list, positioned at its logical
                // offset within the row.
                column.InlineOffset = columnInlineOffset;
                column.BlockOffset = rowOffset;
                newColumns.Add(result);

                UpdateMinimalSpaceShortage(result, ref minimalSpaceShortage);
                actualColumnCount++;

                if (result.SpannerNode != null)
                {
                    isEmptySpannerParent = result.IntrinsicBlockSize == 0;
                    break;
                }

                hasViolatingBreak |= result.GetBreakAppeal() != BreakAppeal.Perfect;
                columnInlineOffset += _columnInlineProgression;

                if (result.HasForcedBreak())
                    forcedBreakCount++;

                columnBreakToken = column.BreakToken;

                // If we're participating in an outer fragmentation context, we'll only
                // allow as many columns as the used value of column-count.
                if (mayResumeInNextOuterFragmentainer && columnBreakToken != null && actualColumnCount >= _usedColumnCount)
                    break;

                if (mayHaveMoreSpaceInNextOuterFragmentainer)
                {
                    minBreakAppeal = (BreakAppeal)Math.Min((int)(minBreakAppeal ?? BreakAppeal.Perfect), (int)result.GetBreakAppeal());

                    float blockEndOverflow = column.BlockOffset + column.BlockSize;
                    if (rowOffset + blockEndOverflow > FragmentainerSpaceLeftForChildren())
                    {
                        if (minimumColumnBlockSize == 0 && blockEndOverflow > columnSize.BlockSize)
                        {
                            System.Diagnostics.Debug.Assert(blockEndOverflow > 0);
                            minimumColumnBlockSize = blockEndOverflow;
                            return LayoutRow(nextColumnToken, minimumColumnBlockSize, bp, ref marginStrut);
                        }
                    }
                }
            } while (columnBreakToken != null);

            if (!balanceColumns)
            {
                if (result != null && result.SpannerNode != null)
                {
                    // We always have to balance columns preceding a spanner.
                    balanceColumns = true;
                    newColumns.Clear();
                    columnSize = new LogicalSize(columnSize.InlineSize, ResolveColumnAutoBlockSize(columnSize, rowOffset, availableOuterSpace, nextColumnToken, balanceColumns));
                    continue;
                }
                break;
            }

            // We're balancing columns. Check if the column block-size that we laid out
            // with was satisfactory.
            if (!hasViolatingBreak && actualColumnCount <= _usedColumnCount && (columnBreakToken == null || (result != null && result.SpannerNode != null)))
                break;

            // Attempt to stretch the columns.
            float newColumnBlockSize;
            if (_usedColumnCount <= forcedBreakCount + 1)
            {
                // If we have no soft break opportunities (because forced breaks cause too
                // many breaks already), there's no stretch amount that could prevent the
                // columns from overflowing. Give up, unless we're nested inside another
                // fragmentation context.
                if (!_isConstrainedByOuterFragmentationContext)
                    break;
                newColumnBlockSize = float.MaxValue;
            }
            else
            {
                newColumnBlockSize = columnSize.BlockSize;
                if (minimalSpaceShortage > 0)
                    newColumnBlockSize += minimalSpaceShortage;
            }
            newColumnBlockSize = ConstrainColumnBlockSize(newColumnBlockSize, rowOffset, availableOuterSpace);

            // Give up if we cannot get taller columns.
            System.Diagnostics.Debug.Assert(newColumnBlockSize >= columnSize.BlockSize);
            if (newColumnBlockSize <= columnSize.BlockSize)
                break;

            // Remove column fragments and re-attempt layout with taller columns.
            newColumns.Clear();
            columnSize = new LogicalSize(columnSize.InlineSize, newColumnBlockSize);
        } while (true);

        if (Space.HasBlockFragmentation && rowOffset > 0)
        {
            // If we have container separation, breaking before this row is fine.
            float fragmentainerBlockOffset = FragmentainerOffsetForChildren() + rowOffset;
            if (!MovePastBreakpoint(result!.Fragment, fragmentainerBlockOffset, BreakAppeal.Perfect))
            {
                // This row didn't fit nicely in the outer fragmentation context. Breaking
                // before is better.
                if (nextColumnToken == null)
                {
                    Builder.AddBreakBeforeChild(new BlockNode(Node.LayoutBox), BreakAppeal.LastResort, false);
                }
                return null;
            }
        }

        // If we just have one empty fragmentainer, we need to keep the trailing
        // margin from any previous column spanner.
        bool isEmpty = columnSize.BlockSize == 0 && newColumns.Count == 1
            && (newColumns[0].Fragment.Children.Count == 0 || isEmptySpannerParent);

        if (!isEmpty)
        {
            _hasProcessedFirstChild = true;
            Builder.PreviousBreakAfter = EBreakBetween.Auto;

            if (newColumns.Count > 0)
            {
                var firstColumn = newColumns[0].Fragment;
                AttemptToPositionListMarker(firstColumn, rowOffset);
            }

            // We're adding a row with content. We can update the intrinsic block-size
            // (which will also be used as layout position for subsequent content).
            _intrinsicBlockSize = rowOffset + intrinsicBlockSizeContribution;
            marginStrut = MarginStrut.Zero;
        }

        // Commit all column fragments to the fragment builder.
        foreach (var colResult in newColumns)
        {
            var column = colResult.Fragment;
            Builder.AddChild(column);
            PropagateBaselineFromChild(column, rowOffset);
        }

        if (minBreakAppeal.HasValue)
            Builder.ClampBreakAppeal(minBreakAppeal.Value);

        return result;
    }

    private BreakStatus LayoutSpanner(BlockNode spannerNode, BlockBreakToken? breakToken, BoxStrut bp, ref MarginStrut marginStrut)
    {
        _spannerPath = null;

        // Compute margins for the spanner.
        float childAvailableInlineSize = ChildAvailableInlineSize;
        BoxStrut margins = ComputeMarginsFor(spannerNode.Style!, childAvailableInlineSize, Space);
        AdjustMarginsForFragmentation(breakToken, ref margins);

        // Collapse the block-start margin of this spanner with the block-end margin
        // of an immediately preceding spanner, if any.
        marginStrut = marginStrut.Append(margins.Top);

        float blockOffset = _intrinsicBlockSize + marginStrut.Sum;
        var spannerSpace = CreateConstraintSpaceForSpanner(spannerNode, blockOffset);

        var result = spannerNode.Layout(spannerSpace, breakToken);

        if (Space.HasBlockFragmentation)
        {
            float fragmentainerBlockOffset = FragmentainerOffsetForChildren() + blockOffset;
            bool movePast = MovePastBreakpoint(result.Fragment, fragmentainerBlockOffset, BreakAppeal.Perfect);
            if (!movePast)
            {
                // We need to break before the spanner.
                Builder.AddBreakBeforeChild(spannerNode, BreakAppeal.Perfect, true);
                return BreakStatus.BrokeBefore;
            }
        }

        // Add the spanner to the container builder.
        Builder.AddChild(result.Fragment);
        PropagateBaselineFromChild(result.Fragment, blockOffset);

        // Update the intrinsic block size and reset the margin strut.
        _intrinsicBlockSize = blockOffset + result.Fragment.BlockSize;
        marginStrut = MarginStrut.Zero;

        return BreakStatus.Continue;
    }

    // Attempt to position the list-item marker (if any) beside the child fragment.
    private void AttemptToPositionListMarker(BoxFragment childFragment, float blockOffset)
    {
        // Simplified: the full list-marker positioning logic is not wired into the
        // simplified builder.
    }

    // At the end of layout, if no column or spanner were able to position the
    // list-item marker, position the marker at the beginning of the multicol
    // container.
    private void PositionAnyUnclaimedListMarker()
    {
    }

    // Propagate the baseline from the given child if needed.
    private void PropagateBaselineFromChild(BoxFragment child, float blockOffset)
    {
        // Baseline propagation is simplified in this port.
    }

    // Calculate the smallest possible block-size for columns, based on the content.
    private float ResolveColumnAutoBlockSize(LogicalSize columnSize, float rowOffset, float availableOuterSpace, BlockBreakToken? childBreakToken, bool balanceColumns)
    {
        // Simplified placeholder for the content-based column sizing. In the full
        // implementation this performs a balancing pass over the content.
        if (balanceColumns)
            return 0;
        return columnSize.BlockSize;
    }

    private float ConstrainColumnBlockSize(float size, float rowOffset, float availableOuterSpace)
    {
        return size;
    }

    private float CurrentContentBlockOffset(float borderBoxRowOffset)
    {
        return borderBoxRowOffset - (BorderTop + PaddingTop);
    }

    private LogicalSize ColumnPercentageResolutionSize()
    {
        return new LogicalSize(_columnInlineSize, ChildAvailableBlockSize);
    }

    private ConstraintSpace CreateConstraintSpaceForBalancing(LogicalSize columnSize)
    {
        return new ConstraintSpace(columnSize.InlineSize, columnSize.BlockSize);
    }

    private ConstraintSpace CreateConstraintSpaceForSpanner(BlockNode spanner, float blockOffset)
    {
        return new ConstraintSpace(Space.AvailableInlineSize, Space.AvailableBlockSize);
    }

    private ConstraintSpace CreateConstraintSpaceForMinMax()
    {
        return new ConstraintSpace(Space.AvailableInlineSize, Space.AvailableBlockSize);
    }

    // The sum of all the current column children's block-sizes, as if they were
    // stacked.
    private float TotalColumnBlockSize()
    {
        float total = 0;
        foreach (var child in Builder.Children)
            total += child.BlockSize;
        return total;
    }

    // ---- Static helpers mirroring shared layout utility functions ----

    private static float ResolveUsedColumnInlineSize(float availableInlineSize, ComputedStyle style)
    {
        // Use the real column-gap (not a fixed 16) when distributing the container
        // width across N columns: W = (available − (N−1)·gap) / N (CSS Multi-column §5).
        // The same formula stretches a definite 'column-width': that value is a
        // minimum, and the used columns always fill the fragmentainer - so
        // 'columns: 92px' in a 300px box with an 8px gap gives 94.67px, and in a
        // 150px box (one column fits) it gives the full 150px.
        float gap = ResolveUsedColumnGap(availableInlineSize, style);
        int count = Math.Max(1, ResolveUsedColumnCount(availableInlineSize, style));
        return Math.Max(0, (availableInlineSize - (count - 1) * gap) / count);
    }

    private static float ResolveUsedColumnGap(float availableInlineSize, ComputedStyle style)
    {
        if (style.ColumnGap is PixelLength pl)
            return pl.Value;
        if (style.ColumnGap is PercentLength pcl)
            return pcl.Value * availableInlineSize;
        return 16; // Default 1em
    }

    private static int ResolveUsedColumnCount(float availableInlineSize, ComputedStyle style)
    {
        if (style.ColumnCount > 0)
            return style.ColumnCount;
        float columnWidth = style.ColumnWidth is PixelLength pl ? pl.Value : 0;
        if (columnWidth > 0)
        {
            // N = floor((available + gap) / (columnWidth + gap)) — the standard
            // column-width packing, using the real gap rather than a fixed 16.
            float gap = ResolveUsedColumnGap(availableInlineSize, style);
            return Math.Max(1, (int)((availableInlineSize + gap) / (columnWidth + gap)));
        }
        return 1;
    }

    private static ConstraintSpace CreateConstraintSpaceForFragmentainer(LogicalSize columnSize, bool balanceColumns, BreakAppeal minBreakAppeal)
    {
        return new ConstraintSpace(columnSize.InlineSize, columnSize.BlockSize);
    }

    private static bool IsBreakInside(BlockBreakToken? token)
    {
        return token != null && !token.IsBreakBefore && !token.IsRepeated;
    }

    private static bool InvolvedInBlockFragmentation(ConstraintSpace space, BlockBreakToken? breakToken)
    {
        return space.HasDefiniteBlockSize || IsBreakInside(breakToken);
    }

    private float ClampIntrinsicBlockSize(float intrinsicSize, float previouslyConsumedBlockSize)
    {
        float minH = Style.MinHeight is PixelLength mh ? mh.Value : 0;
        float maxH = Style.MaxHeight is PixelLength mx ? mx.Value : float.MaxValue;
        return Math.Clamp(intrinsicSize, minH, maxH);
    }

    private void FinishFragmentation()
    {
        // Simplified: mark the builder as having block fragmentation.
    }

    private static bool MovePastBreakpoint(BoxFragment fragment, float fragmentainerBlockOffset, BreakAppeal appeal)
    {
        // Simplified: always move past until the full breakpoint machinery exists.
        return true;
    }

    private static float FragmentainerOffsetForChildren() => 0;
    private static float FragmentainerSpaceLeftForChildren() => float.MaxValue;

    private void AlignBlockContent(float unconstrainedIntrinsicBlockSize)
    {
        // Simplified: no align-content handling in the multicol port.
    }

    private void FinalizeTableCellLayout(float unconstrainedIntrinsicBlockSize)
    {
    }

    private static BoxStrut ComputeMarginsFor(ComputedStyle style, float availableInlineSize, ConstraintSpace space)
    {
        // 'auto' contributes zero; the auto-margin rules distribute the leftover.
        float M(Length l) => l is AutoLength ? 0
            : l.ToPixels(style.FontSize, space.RootFontSize, space.ViewportWidth, space.ViewportHeight);
        return new BoxStrut(M(style.MarginTop), M(style.MarginRight), M(style.MarginBottom), M(style.MarginLeft));
    }

    private static void AdjustMarginsForFragmentation(BlockBreakToken? breakToken, ref BoxStrut margins)
    {
        // The block-start margin is adjusted for fragmentation.
        if (breakToken != null && breakToken.IsBreakBefore)
            margins = new BoxStrut(0, margins.Right, margins.Bottom, margins.Left);
    }

    private static void UpdateMinimalSpaceShortage(LayoutResult result, ref float minimalSpaceShortage)
    {
        float shortage = result.MinimalSpaceShortage();
        if (shortage > 0 && (float.IsNaN(minimalSpaceShortage) || shortage < minimalSpaceShortage))
            minimalSpaceShortage = shortage;
    }

    private static LayoutResult RelayoutAndBreakEarlier()
    {
        return new LayoutResult();
    }
}

/// <summary>
/// Extension methods for ColumnLayoutAlgorithm support types.
/// </summary>
public static class ColumnLayoutAlgorithmExtensions
{
    public static bool IsListItem(this Element element)
    {
        return element.ComputedStyle != null && element.ComputedStyle.Display == DisplayType.ListItem;
    }

    public static bool HasAutoColumnWidth(this ComputedStyle style)
    {
        return !(style.ColumnWidth is PixelLength);
    }

    public static EColumnFill ColumnFill(this ComputedStyle style) =>
        style.ColumnFill == "auto" ? EColumnFill.Auto : EColumnFill.Balance;

    public static bool GetColumnSpanAll(this ComputedStyle style) => style.ColumnSpanAll;

    public static bool HasInflowChildBreakInside(this BoxFragmentBuilder builder) => false;

    public static void AddBreakToken(this BoxFragmentBuilder builder, BlockBreakToken token) { }

    public static void AddBreakBeforeChild(this BoxFragmentBuilder builder, BlockNode node, BreakAppeal appeal, bool isForcedBreak) { }

    public static void ClampBreakAppeal(this BoxFragmentBuilder builder, BreakAppeal appeal) { }

    public static void SetBoxType(this BlockLayoutAlgorithm algorithm, PhysicalFragment.BoxType type) { }

    public static bool HasForcedBreak(this LayoutResult result) => false;

    public static BreakAppeal GetBreakAppeal(this LayoutResult result) => BreakAppeal.Perfect;

    public static float MinimalSpaceShortage(this LayoutResult result) => 0;

    public static BlockNode? SpannerNode(this LayoutResult result) => null;

    public static void SetHasSeenAllChildren(this BoxFragmentBuilder builder) { }

    public static void SetPreviousBreakAfter(this BoxFragmentBuilder builder, EBreakBetween value) { }

    public static MinMaxSizes Encompass(MinMaxSizes sizes, MinMaxSizes other)
    {
        return new MinMaxSizes(Math.Max(sizes.MinSize, other.MinSize), Math.Max(sizes.MaxSize, other.MaxSize));
    }

    public static bool IsAtFragmentainerStart(this ConstraintSpace space) => false;

    public static bool HasKnownFragmentainerBlockSize(this ConstraintSpace space) => space.HasDefiniteBlockSize;

    public static bool IsColumnBox(this PhysicalFragment fragment) => fragment.Box == PhysicalFragment.BoxType.ColumnBox;

    public static bool IsFragmentainerBox(this PhysicalFragment fragment)
        => fragment.Box == PhysicalFragment.BoxType.ColumnBox || fragment.Box == PhysicalFragment.BoxType.PageArea;

    public static bool IsCSSBox(this PhysicalFragment fragment) => !fragment.IsLineBox && !fragment.IsFragmentainerBox();

    public static float BlockEndScrollableOverflow(this LogicalBoxFragment fragment) => fragment.BlockSize;

    public static bool IsInitialColumnBalancingPass(this BoxFragmentBuilder builder) => false;
}