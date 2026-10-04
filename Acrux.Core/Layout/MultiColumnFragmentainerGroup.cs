using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Geom = Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

// Extension for ComputedStyle to support IsLeftToRightDirection
public static class ComputedStyleMulticolExtensions
{
    public static bool IsLeftToRightDirection(this ComputedStyle style) => true;
}

/// <summary>
/// A group of columns, that are laid out in the inline progression direction,
/// all with the same column height.
///
/// When a multicol container is inside another fragmentation context, and said
/// multicol container lives in multiple outer fragmentainers (pages / columns),
/// we need to put these inner columns into separate groups, with one group per
/// outer fragmentainer. Such a group of columns is what comprises a "row of
/// column boxes" in spec lingo.
///
/// Column balancing, when enabled, takes place within a column fragmentainer
/// group.
///
/// Each fragmentainer group may have its own actual column count (if there are
/// unused columns because of forced breaks, for example). If there are multiple
/// fragmentainer groups, the actual column count must not exceed the used column
/// count (the one calculated based on column-count and column-width from CSS),
/// or they'd overflow the outer fragmentainer in the inline direction. If we
/// need more columns than what a group has room for, we'll create another group
/// and put them there (and make them appear in the next outer fragmentainer).
/// </summary>
public sealed class MultiColumnFragmentainerGroup
{
    // Limit the maximum column count, to prevent potential performance problems.
    private const uint ColumnCountClampMax = 10000;

    // Clamp "infinite" clips to a number of pixels that can be losslessly
    // converted to and from floating point, to avoid loss of precision.
    // Note that tables have something similar, see TableLayoutUtils::kTableMaxWidth.
    private const float MulticolMaxClipPixels = 1000000;

    private readonly LayoutMultiColumnSet _columnSet;

    private float _logicalTop;
    private float _logicalTopInFlowThread;
    private float _logicalBottomInFlowThread;

    // Logical height of the group. This will also be the height of each column
    // in this group, with the difference that, while the logical height can be
    // 0, the height of a column must be >= 1px.
    private float _logicalHeight;

    private bool _isLogicalHeightKnown;

    public MultiColumnFragmentainerGroup(LayoutMultiColumnSet columnSet)
    {
        _columnSet = columnSet;
    }

    // Position within the LayoutMultiColumnSet.
    public float LogicalTop => _logicalTop;
    public void SetLogicalTop(float logicalTop) => _logicalTop = logicalTop;

    // Return the amount of block space that this fragmentainer group takes up in
    // its containing LayoutMultiColumnSet.
    public float GroupLogicalHeight
    {
        get
        {
            System.Diagnostics.Debug.Assert(IsLogicalHeightKnown);
            return _logicalHeight;
        }
    }

    // Return the block size of a column (or fragmentainer) in this fragmentainer
    // group. The spec says that this value must always be >= 1px, to ensure
    // progress.
    public float ColumnLogicalHeight
    {
        get
        {
            System.Diagnostics.Debug.Assert(IsLogicalHeightKnown);
            return Math.Max(1, _logicalHeight);
        }
    }

    // Return whether we have some column height to work with. This doesn't have
    // to be the final height. It will only return false in the first layout pass,
    // and even then only if column height is auto and there's no way to even make
    // a guess (i.e. when there are no usable constraints).
    public bool IsLogicalHeightKnown => _isLogicalHeightKnown;

    public LogicalOffset OffsetFromColumnSet()
    {
        return new LogicalOffset(0, LogicalTop);
    }

    // The top of our flow thread portion
    public float LogicalTopInFlowThread => _logicalTopInFlowThread;
    public void SetLogicalTopInFlowThread(float logicalTopInFlowThread) => _logicalTopInFlowThread = logicalTopInFlowThread;

    // The bottom of our flow thread portion
    public float LogicalBottomInFlowThread => _logicalBottomInFlowThread;
    public void SetLogicalBottomInFlowThread(float logicalBottomInFlowThread) => _logicalBottomInFlowThread = logicalBottomInFlowThread;
    public void ExtendLogicalBottomInFlowThread(float blockSize) => _logicalBottomInFlowThread += blockSize;

    // The height of the flow thread portion for the entire fragmentainer group.
    public float LogicalHeightInFlowThread
    {
        get
        {
            // Due to negative margins, logical bottom may actually end up above logical
            // top, but we never want to return negative logical heights.
            return Math.Max(0, _logicalBottomInFlowThread - _logicalTopInFlowThread);
        }
    }

    // The height of the flow thread portion for the specified fragmentainer.
    // The last fragmentainer may not be using all available space.
    public float LogicalHeightInFlowThreadAt(uint columnIndex)
    {
        System.Diagnostics.Debug.Assert(IsLogicalHeightKnown);
        float columnHeight = ColumnLogicalHeight;
        float logicalTop = LogicalTopInFlowThreadAt(columnIndex);
        float logicalBottom = logicalTop + columnHeight;
        uint actualCount = ActualColumnCount;
        if (columnIndex + 1 >= actualCount)
        {
            // The last column may contain overflow content, if the actual column count
            // was clamped, so using the column height won't do. This is also a way to
            // stay within the bounds of the flow thread, if the last column happens to
            // contain LESS than the other columns. We also need this clamping if we're
            // given a column index *after* the last column. Height should obviously be
            // 0 then. We may be called with a column index that's one entry past the
            // end if we're dealing with zero-height content at the very end of the flow
            // thread, and this location is at a column boundary.
            if (columnIndex + 1 == actualCount)
                logicalBottom = LogicalBottomInFlowThread;
            else
                logicalBottom = logicalTop;
        }
        return Math.Max(0, logicalBottom - logicalTop);
    }

    public void ResetColumnHeight()
    {
        _isLogicalHeightKnown = false;
        _logicalHeight = 0;
    }

    public PhysicalOffset FlowThreadTranslationAtOffset(float offsetInFlowThread, PageBoundaryRule rule)
    {
        var flowThread = _columnSet.MultiColumnFlowThread();

        // A column out of range doesn't have a flow thread portion, so we need to
        // clamp to make sure that we stay within the actual columns. This means that
        // content in the overflow area will be mapped to the last actual column,
        // instead of being mapped to an imaginary column further ahead.
        uint columnIndex = offsetInFlowThread >= LogicalBottomInFlowThread ? ActualColumnCount - 1 : ColumnIndexAtOffset(offsetInFlowThread, rule);

        PhysicalRect portionRect = FlowThreadPortionRectAt(columnIndex);
        portionRect = new PhysicalRect(portionRect.Offset + flowThread.PhysicalLocation(), portionRect.Size);

        LogicalRect columnRect = ColumnRectAt(columnIndex);
        columnRect = new LogicalRect(columnRect.Offset + OffsetFromColumnSet(), columnRect.Size);
        PhysicalRect physicalColumnRect = _columnSet.CreateWritingModeConverter().ToPhysical(columnRect);
        physicalColumnRect = new PhysicalRect(physicalColumnRect.Offset + _columnSet.PhysicalLocation(), physicalColumnRect.Size);

        return physicalColumnRect.Offset - portionRect.Offset;
    }

    public LogicalOffset VisualPointToFlowThreadPoint(LogicalOffset visualPoint)
    {
        uint columnIndex = ColumnIndexAtVisualPoint(visualPoint);
        LogicalRect columnRect = ColumnRectAt(columnIndex);
        LogicalOffset localPoint = visualPoint;
        localPoint -= columnRect.Offset;
        return new LogicalOffset(localPoint.InlineOffset, localPoint.BlockOffset + LogicalTopInFlowThreadAt(columnIndex));
    }

    public PhysicalRect FragmentsBoundingBox(PhysicalRect boundingBoxInFlowThread)
    {
        // Find the start and end column intersected by the bounding box.
        LogicalRect logicalBoundingBox = _columnSet.FlowThread().CreateWritingModeConverter().ToLogical(boundingBoxInFlowThread);
        float boundingBoxLogicalTop = logicalBoundingBox.Offset.BlockOffset;
        float boundingBoxLogicalBottom = logicalBoundingBox.BlockEnd;
        if (boundingBoxLogicalBottom <= LogicalTopInFlowThread || boundingBoxLogicalTop >= LogicalBottomInFlowThread)
        {
            // The bounding box doesn't intersect this fragmentainer group.
            return PhysicalRect.Zero;
        }
        uint startColumn;
        uint endColumn;
        ColumnIntervalForBlockRangeInFlowThread(boundingBoxLogicalTop, boundingBoxLogicalBottom, out startColumn, out endColumn);

        PhysicalRect startColumnRect = boundingBoxInFlowThread;
        startColumnRect = startColumnRect.Intersect(FlowThreadPortionOverflowRectAt(startColumn));
        startColumnRect = new PhysicalRect(startColumnRect.Offset + FlowThreadTranslationAtOffset(LogicalTopInFlowThreadAt(startColumn), PageBoundaryRule.AssociateWithLatterPage), startColumnRect.Size);
        if (startColumn == endColumn)
            return startColumnRect; // It all takes place in one column. We're done.

        PhysicalRect endColumnRect = boundingBoxInFlowThread;
        endColumnRect = endColumnRect.Intersect(FlowThreadPortionOverflowRectAt(endColumn));
        endColumnRect = new PhysicalRect(endColumnRect.Offset + FlowThreadTranslationAtOffset(LogicalTopInFlowThreadAt(endColumn), PageBoundaryRule.AssociateWithLatterPage), endColumnRect.Size);
        return UnionRect(startColumnRect, endColumnRect);
    }

    public PhysicalRect FlowThreadPortionRectAt(uint columnIndex)
    {
        return _columnSet.FlowThread().CreateWritingModeConverter().ToPhysical(LogicalFlowThreadPortionRectAt(columnIndex));
    }

    public PhysicalRect FlowThreadPortionOverflowRectAt(uint columnIndex)
    {
        // This function determines the portion of the flow thread that paints for the
        // column.
        //
        // In the block direction, we will not clip overflow out of the top of the
        // first column, or out of the bottom of the last column. This applies only to
        // the true first column and last column across all column sets.
        bool isFirstColumnInRow = columnIndex == 0;
        bool isLastColumnInRow = columnIndex == ActualColumnCount - 1;

        LogicalRect portionRect = LogicalFlowThreadPortionRectAt(columnIndex);
        bool isFirstColumnInMulticolContainer
            = isFirstColumnInRow && this == _columnSet.FirstFragmentainerGroup() && _columnSet.PreviousSiblingMultiColumnSet() == null;
        bool isLastColumnInMulticolContainer
            = isLastColumnInRow && this == _columnSet.LastFragmentainerGroup() && _columnSet.NextSiblingMultiColumnSet() == null;
        // Calculate the overflow rectangle. It will be clipped at the logical top
        // and bottom of the column box, unless it's the first or last column in the
        // multicol container, in which case it should allow overflow. It will also
        // be clipped in the middle of adjacent column gaps. Care is taken here to
        // avoid rounding errors.
        LogicalRect overflowRect = new(-MulticolMaxClipPixels, -MulticolMaxClipPixels, 2 * MulticolMaxClipPixels, 2 * MulticolMaxClipPixels);
        if (!isFirstColumnInMulticolContainer)
        {
            overflowRect = ShiftBlockStartEdgeTo(overflowRect, portionRect.Offset.BlockOffset);
        }
        if (!isLastColumnInMulticolContainer)
        {
            overflowRect = ShiftBlockEndEdgeTo(overflowRect, portionRect.BlockEnd);
        }
        return _columnSet.FlowThread().CreateWritingModeConverter().ToPhysical(overflowRect);
    }

    // Get the first and the last column intersecting the specified block range.
    // Note that |logicalBottomInFlowThread| is an exclusive endpoint.
    public void ColumnIntervalForBlockRangeInFlowThread(
        float logicalTopInFlowThread, float logicalBottomInFlowThread, out uint firstColumn, out uint lastColumn)
    {
        logicalTopInFlowThread = Math.Max(logicalTopInFlowThread, LogicalTopInFlowThread);
        logicalBottomInFlowThread = Math.Min(logicalBottomInFlowThread, LogicalBottomInFlowThread);
        firstColumn = ConstrainedColumnIndexAtOffset(logicalTopInFlowThread, PageBoundaryRule.AssociateWithLatterPage);
        if (logicalBottomInFlowThread <= logicalTopInFlowThread)
        {
            // Zero-height block range. There'll be one column in the interval. Set it
            // right away. This is important if we're at a column boundary, since
            // calling ConstrainedColumnIndexAtOffset() with the end-exclusive bottom
            // offset would actually give us the *previous* column.
            lastColumn = firstColumn;
        }
        else
        {
            lastColumn = ConstrainedColumnIndexAtOffset(logicalBottomInFlowThread, PageBoundaryRule.AssociateWithFormerPage);
        }
    }

    public uint ColumnIndexAtOffset(float offsetInFlowThread, PageBoundaryRule rule)
    {
        // Handle the offset being out of range.
        if (offsetInFlowThread < _logicalTopInFlowThread)
            return 0;

        if (!IsLogicalHeightKnown)
            return 0;
        float columnHeight = ColumnLogicalHeight;
        uint columnIndex = (uint)((offsetInFlowThread - _logicalTopInFlowThread) / columnHeight);
        if (rule == PageBoundaryRule.AssociateWithFormerPage && columnIndex > 0 && LogicalTopInFlowThreadAt(columnIndex) == offsetInFlowThread)
        {
            // We are exactly at a column boundary, and we've been told to associate
            // offsets at column boundaries with the former column, not the latter.
            columnIndex--;
        }
        return columnIndex;
    }

    // Like ColumnIndexAtOffset(), but with the return value clamped to actual
    // column count. While there are legitimate reasons for dealing with columns
    // out of bounds during layout, this should not happen when performing read
    // operations on the tree (like painting and hit-testing).
    public uint ConstrainedColumnIndexAtOffset(float offsetInFlowThread, PageBoundaryRule rule)
    {
        uint index = ColumnIndexAtOffset(offsetInFlowThread, rule);
        return Math.Min(index, ActualColumnCount - 1);
    }

    // The "CSS actual" value of column-count. This includes overflowing columns,
    // if any.
    // Returns 1 or greater, never 0.
    public uint ActualColumnCount
    {
        get
        {
            uint count = UnclampedActualColumnCount();
            count = Math.Min(count, ColumnCountClampMax);
            System.Diagnostics.Debug.Assert(count >= 1);
            return count;
        }
    }

    public void SetColumnBlockSizeFromNG(float blockSize)
    {
        // We clamp the fragmentainer block size up to 1 for legacy write-back if
        // there is content that overflows the less-than-1px-height (or even
        // zero-height) fragmentainer. However, if one fragmentainer contains no
        // overflow, while others fragmentainers do, the known height may be different
        // than the |blockSize| passed in. Don't override the stored height if this
        // is the case.
        System.Diagnostics.Debug.Assert(!_isLogicalHeightKnown || _logicalHeight == blockSize || blockSize <= 1);
        if (_isLogicalHeightKnown)
            return;
        _logicalHeight = blockSize;
        _isLogicalHeightKnown = true;
    }

    public void ExtendColumnBlockSizeFromNG(float blockSize)
    {
        System.Diagnostics.Debug.Assert(_isLogicalHeightKnown);
        _logicalHeight += blockSize;
    }

    private LogicalRect ColumnRectAt(uint columnIndex)
    {
        float columnLogicalWidth = _columnSet.PageLogicalWidth;
        float columnLogicalHeight = LogicalHeightInFlowThreadAt(columnIndex);
        float columnLogicalTop = 0;
        float columnLogicalLeft = 0;
        float columnGap = _columnSet.ColumnGap();

        if (_columnSet.StyleRef().IsLeftToRightDirection())
        {
            columnLogicalLeft += columnIndex * (columnLogicalWidth + columnGap);
        }
        else
        {
            columnLogicalLeft += _columnSet.ContentLogicalWidth() - columnLogicalWidth - columnIndex * (columnLogicalWidth + columnGap);
        }

        return new LogicalRect(columnLogicalLeft, columnLogicalTop, columnLogicalWidth, columnLogicalHeight);
    }

    private float LogicalTopInFlowThreadAt(uint columnIndex)
    {
        return _logicalTopInFlowThread + columnIndex * ColumnLogicalHeight;
    }

    private LogicalRect LogicalFlowThreadPortionRectAt(uint columnIndex)
    {
        float logicalTop = LogicalTopInFlowThreadAt(columnIndex);
        float portionLogicalHeight = LogicalHeightInFlowThreadAt(columnIndex);
        return new LogicalRect(0, logicalTop, _columnSet.PageLogicalWidth, portionLogicalHeight);
    }

    // Return the column that the specified visual point belongs to. Only the
    // coordinate on the column progression axis is relevant. Every point belongs
    // to a column, even if said point is not inside any of the columns.
    private uint ColumnIndexAtVisualPoint(LogicalOffset visualPoint)
    {
        float columnLength = _columnSet.PageLogicalWidth;
        float offsetInColumnProgressionDirection = visualPoint.InlineOffset;
        if (!_columnSet.StyleRef().IsLeftToRightDirection())
        {
            offsetInColumnProgressionDirection = _columnSet.LogicalWidth() - offsetInColumnProgressionDirection;
        }
        float columnGap = _columnSet.ColumnGap();
        if (columnLength + columnGap <= 0)
            return 0;
        // Column boundaries are in the middle of the column gap.
        int index = (int)((offsetInColumnProgressionDirection + columnGap / 2) / (columnLength + columnGap));
        if (index < 0)
            return 0;
        return Math.Min((uint)index, ActualColumnCount - 1);
    }

    private uint UnclampedActualColumnCount()
    {
        // We must always return a value of 1 or greater. Column count = 0 is a
        // meaningless situation, and will confuse and cause problems in other parts
        // of the code.
        if (!IsLogicalHeightKnown)
            return 1;
        // Our flow thread portion determines our column count. We have as many
        // columns as needed to fit all the content.
        float flowThreadPortionHeight = LogicalHeightInFlowThread;
        if (flowThreadPortionHeight == 0)
            return 1;

        float columnHeight = ColumnLogicalHeight;
        uint count = (uint)(flowThreadPortionHeight / columnHeight);
        // flowThreadPortionHeight may be saturated, so detect the remainder manually.
        if (count * columnHeight < flowThreadPortionHeight)
            count++;

        System.Diagnostics.Debug.Assert(count >= 1);
        return count;
    }

    internal static LogicalRect ShiftBlockStartEdgeTo(LogicalRect rect, float edgeOffset)
    {
        float delta = edgeOffset - rect.BlockStart;
        return new LogicalRect(rect.InlineStart, edgeOffset, rect.InlineSize, rect.BlockSize - delta);
    }

    internal static LogicalRect ShiftBlockEndEdgeTo(LogicalRect rect, float edgeOffset)
    {
        return new LogicalRect(rect.InlineStart, rect.BlockStart, rect.InlineSize, edgeOffset - rect.BlockStart);
    }

    internal static PhysicalRect UnionRect(PhysicalRect a, PhysicalRect b) => a.Union(b);
}

/// <summary>
/// List of all fragmentainer groups within a column set. There will always be at
/// least one group. Deleting the one group is not allowed (or possible). There
/// will be more than one group if the owning column set lives in multiple outer
/// fragmentainers (e.g. multicol inside paged media).
/// </summary>
public sealed class MultiColumnFragmentainerGroupList
{
    private readonly LayoutMultiColumnSet _columnSet;
    private readonly List<MultiColumnFragmentainerGroup> _groups = new();

    public MultiColumnFragmentainerGroupList(LayoutMultiColumnSet columnSet)
    {
        _columnSet = columnSet;
        Append(new MultiColumnFragmentainerGroup(_columnSet));
    }

    // Add an additional fragmentainer group to the end of the list, and return it.
    public MultiColumnFragmentainerGroup AddExtraGroup()
    {
        Append(new MultiColumnFragmentainerGroup(_columnSet));
        return Last();
    }

    // Remove all fragmentainer groups but the first one.
    public void DeleteExtraGroups()
    {
        Shrink(1);
    }

    public MultiColumnFragmentainerGroup First() => _groups[0];
    public MultiColumnFragmentainerGroup Last() => _groups[_groups.Count - 1];

    public int Size => _groups.Count;
    public MultiColumnFragmentainerGroup this[int i] => _groups[i];

    public void Append(MultiColumnFragmentainerGroup group) => _groups.Add(group);
    public void Shrink(int size)
    {
        if (_groups.Count > size)
            _groups.RemoveRange(size, _groups.Count - size);
    }

    public List<MultiColumnFragmentainerGroup>.Enumerator GetEnumerator() => _groups.GetEnumerator();
}