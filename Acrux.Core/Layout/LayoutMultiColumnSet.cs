using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Geom = Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// A set of columns in a multicol container. A column set is inserted as an
/// anonymous child of the actual multicol container (i.e. the layoutObject whose
/// style computes to non-auto column-count and/or column-width), next to the
/// flow thread. There'll be one column set for each contiguous run of column
/// content. The only thing that can interrupt a contiguous run of column content
/// is a column spanner, which means that if there are no spanners, there'll
/// only be one column set.
///
/// Since a spanner interrupts an otherwise contiguous run of column content,
/// inserting one may result in the creation of additional new column sets. A
/// placeholder for the spanning layoutObject has to be placed in between the
/// column sets that come before and after the spanner, if there's actually
/// column content both before and after the spanner.
///
/// A column set has no children on its own, but is merely used to slice a
/// portion of the tall "single-column" flow thread into actual columns visually,
/// to convert from flow thread coordinates to visual ones. It is in charge of
/// both positioning columns correctly relatively to the parent multicol
/// container, and to calculate the correct translation for each column's
/// contents, and to paint any rules between them. LayoutMultiColumnSet objects
/// are used for painting, hit testing, and any other type of operation that
/// requires mapping from flow thread coordinates to visual coordinates.
///
/// Columns are normally laid out in the inline progression direction, but if the
/// multicol container is inside another fragmentation context (e.g. paged media,
/// or an another multicol container), we may need to group the columns, so
/// that we get one MultiColumnFragmentainerGroup for each outer fragmentainer
/// (page / column) that the inner multicol container lives in. Each
/// fragmentainer group has its own column height, but the column height is
/// uniform within a group.
/// </summary>
public sealed class LayoutMultiColumnSet : LayoutBlockFlow
{
    private readonly MultiColumnFragmentainerGroupList _fragmentainerGroups;
    private LayoutFlowThread? _flowThread;

    public LayoutMultiColumnSet(LayoutFlowThread? flowThread)
        : base(null)
    {
        _fragmentainerGroups = new MultiColumnFragmentainerGroupList(this);
        _flowThread = flowThread;
    }

    public static LayoutMultiColumnSet CreateAnonymous(LayoutFlowThread flowThread, ComputedStyle parentStyle)
    {
        var layoutObject = new LayoutMultiColumnSet(flowThread);
        // Set document and style for anonymous
        return layoutObject;
    }

    public bool IsLayoutMultiColumnSet => true;
    public bool CanHaveChildren => false;

    public override string GetName() => "LayoutMultiColumnSet";

    public bool IsLayoutNGObject => false;

    public MultiColumnFragmentainerGroup FirstFragmentainerGroup()
    {
        UpdateGeometryIfNeeded();
        return _fragmentainerGroups.First();
    }

    public MultiColumnFragmentainerGroup LastFragmentainerGroup()
    {
        UpdateGeometryIfNeeded();
        return _fragmentainerGroups.Last();
    }

    public uint FragmentainerGroupIndexAtFlowThreadOffset(float flowThreadOffset, PageBoundaryRule rule)
    {
        UpdateGeometryIfNeeded();
        System.Diagnostics.Debug.Assert(_fragmentainerGroups.Size > 0);
        if (flowThreadOffset <= 0)
            return 0;
        for (uint index = 0; index < (uint)_fragmentainerGroups.Size; index++)
        {
            var row = _fragmentainerGroups[(int)index];
            if (rule == PageBoundaryRule.AssociateWithLatterPage)
            {
                if (row.LogicalTopInFlowThread <= flowThreadOffset && row.LogicalBottomInFlowThread > flowThreadOffset)
                    return index;
            }
            else if (row.LogicalTopInFlowThread < flowThreadOffset && row.LogicalBottomInFlowThread >= flowThreadOffset)
            {
                return index;
            }
        }
        return (uint)_fragmentainerGroups.Size - 1;
    }

    public MultiColumnFragmentainerGroup FragmentainerGroupAtFlowThreadOffset(float flowThreadOffset, PageBoundaryRule rule)
    {
        UpdateGeometryIfNeeded();
        return _fragmentainerGroups[(int)FragmentainerGroupIndexAtFlowThreadOffset(flowThreadOffset, rule)];
    }

    public MultiColumnFragmentainerGroup FragmentainerGroupAtVisualPoint(LogicalOffset visualPoint)
    {
        UpdateGeometryIfNeeded();
        System.Diagnostics.Debug.Assert(_fragmentainerGroups.Size > 0);
        float blockOffset = visualPoint.BlockOffset;
        for (int index = 0; index < _fragmentainerGroups.Size; index++)
        {
            var row = _fragmentainerGroups[index];
            if (row.LogicalTop + row.GroupLogicalHeight > blockOffset)
                return row;
        }
        return _fragmentainerGroups.Last();
    }

    public MultiColumnFragmentainerGroupList FragmentainerGroups()
    {
        UpdateGeometryIfNeeded();
        return _fragmentainerGroups;
    }

    // Return the width and height of a single column or page in the set.
    public float PageLogicalWidth => FlowThread().LogicalWidth;

    public bool IsPageLogicalHeightKnown => FirstFragmentainerGroup().IsLogicalHeightKnown;

    public LayoutFlowThread FlowThread() => _flowThread!;

    public LayoutBlockFlow MultiColumnBlockFlow() => (LayoutBlockFlow)Parent!;

    public LayoutMultiColumnFlowThread MultiColumnFlowThread() => (LayoutMultiColumnFlowThread)FlowThread();

    public LayoutMultiColumnSet? NextSiblingMultiColumnSet()
    {
        for (LayoutObject? sibling = NextSibling; sibling != null; sibling = sibling.NextSibling)
        {
            if (sibling.IsLayoutMultiColumnSet())
                return (LayoutMultiColumnSet)sibling;
        }
        return null;
    }

    public LayoutMultiColumnSet? PreviousSiblingMultiColumnSet()
    {
        for (LayoutObject? sibling = PreviousSibling; sibling != null; sibling = sibling.PreviousSibling)
        {
            if (sibling.IsLayoutMultiColumnSet())
                return (LayoutMultiColumnSet)sibling;
        }
        return null;
    }

    public MultiColumnFragmentainerGroup AppendNewFragmentainerGroup()
    {
        MultiColumnFragmentainerGroup newGroup = new MultiColumnFragmentainerGroup(this);
        {
            MultiColumnFragmentainerGroup previousGroup = _fragmentainerGroups.Last();

            // This is the flow thread block offset where |previousGroup| ends and
            // |newGroup| takes over.
            float blockOffsetInFlowThread = previousGroup.LogicalTopInFlowThread + FragmentainerGroupCapacity(previousGroup);
            previousGroup.SetLogicalBottomInFlowThread(blockOffsetInFlowThread);
            newGroup.SetLogicalTopInFlowThread(blockOffsetInFlowThread);
            newGroup.SetLogicalTop(previousGroup.LogicalTop + previousGroup.GroupLogicalHeight);
            newGroup.ResetColumnHeight();
        }
        _fragmentainerGroups.Append(newGroup);
        return _fragmentainerGroups.Last();
    }

    public float LogicalTopInFlowThread() => FirstFragmentainerGroup().LogicalTopInFlowThread;

    public float LogicalBottomInFlowThread() => LastFragmentainerGroup().LogicalBottomInFlowThread;

    // Return the amount of flow thread contents that the specified fragmentainer
    // group can hold without overflowing.
    public float FragmentainerGroupCapacity(MultiColumnFragmentainerGroup group)
    {
        return group.ColumnLogicalHeight * UsedColumnCount;
    }

    // The used CSS value of column-count, i.e. how many columns there are room
    // for without overflowing.
    public uint UsedColumnCount => MultiColumnFlowThread().ColumnCount;

    // Find the column that contains the given block offset, and return the
    // translation needed to get from flow thread coordinates to visual
    // coordinates.
    public PhysicalOffset FlowThreadTranslationAtOffset(float blockOffset, PageBoundaryRule rule)
    {
        return FragmentainerGroupAtFlowThreadOffset(blockOffset, rule).FlowThreadTranslationAtOffset(blockOffset, rule);
    }

    public LogicalOffset VisualPointToFlowThreadPoint(PhysicalOffset visualPoint)
    {
        LogicalOffset logicalPoint = CreateWritingModeConverter().ToLogical(visualPoint, PhysicalSize.Zero);
        MultiColumnFragmentainerGroup row = FragmentainerGroupAtVisualPoint(logicalPoint);
        return row.VisualPointToFlowThreadPoint(logicalPoint - row.OffsetFromColumnSet());
    }

    // Reset previously calculated column height. Will mark for layout if needed.
    public void ResetColumnHeight()
    {
        _fragmentainerGroups.DeleteExtraGroups();
        _fragmentainerGroups.First().ResetColumnHeight();
    }

    public void StyleDidChange(object diff, ComputedStyle? oldStyle)
    {
        // column-rule is specified on the parent (the multicol container) of this
        // object, but it's the column sets that are in charge of painting them.
        // A column rule is pretty much like any other box decoration, like borders.
        // We need to say that we have box decorations here, so that the columnn set
        // is invalidated when it gets laid out. We cannot check here whether the
        // multicol container actually has a visible column rule or not, because we
        // may not have been inserted into the tree yet. Painting a column set is
        // cheap anyway, because the only thing it can paint is the column rule, while
        // actual multicol content is handled by the flow thread.
        HasBoxDecorationBackground = true;
    }

    public float ColumnGap()
    {
        LayoutBlockFlow parentBlock = MultiColumnBlockFlow();

        if (parentBlock.StyleRef().ColumnGap is Length columnGap)
        {
            return columnGap.ToPixels(parentBlock.StyleRef().FontSize, 16, parentBlock.AvailableLogicalWidth(), 0);
        }

        // "1em" is recommended as the normal gap setting. Matches <p> margins.
        return parentBlock.StyleRef().GetFontDescription();
    }

    public uint ActualColumnCount
    {
        get
        {
            // FIXME: remove this method. It's a meaningless question to ask the set "how
            // many columns do you actually have?", since that may vary for each row.
            return FirstFragmentainerGroup().ActualColumnCount;
        }
    }

    public PhysicalRect FragmentsBoundingBox(PhysicalRect boundingBoxInFlowThread)
    {
        UpdateGeometryIfNeeded();
        PhysicalRect result = PhysicalRect.Zero;
        foreach (var group in _fragmentainerGroups)
            result = result.Union(group.FragmentsBoundingBox(boundingBoxInFlowThread));
        return result;
    }

    public void AttachToFlowThread()
    {
        if (_flowThread == null)
            return;
        _flowThread.AddColumnSetToThread(this);
    }

    public void DetachFromFlowThread()
    {
        if (_flowThread != null)
        {
            _flowThread.RemoveColumnSetFromThread(this);
            _flowThread = null;
        }
    }

    public void SetIsIgnoredByNG()
    {
        _fragmentainerGroups.First().SetColumnBlockSizeFromNG(0);
    }

    public new PhysicalOffset PhysicalLocation() => FrameLocation;

    public new WritingModeConverter CreateWritingModeConverter()
    {
        var dir = new WritingDirectionMode(Geom.WritingMode.HorizontalTb, TextDirection.Ltr);
        return new WritingModeConverter(dir, FrameSize);
    }

    public float LogicalWidth()
    {
        LogicalSize size = ToLogicalSize(FrameSize, Geom.WritingMode.HorizontalTb);
        return size.InlineSize;
    }
    public float ContentLogicalWidth() => LogicalWidth() - BorderLeft - BorderRight - PaddingLeft - PaddingRight;
    public float AvailableLogicalWidth() => ContentLogicalWidth();

    private static LogicalSize ToLogicalSize(PhysicalSize size, Geom.WritingMode mode)
    {
        return mode == Geom.WritingMode.HorizontalTb
            ? new LogicalSize(size.Width, size.Height)
            : new LogicalSize(size.Height, size.Width);
    }

    internal bool HasBoxDecorationBackground { get; set; }

    private void UpdateGeometry()
    {
        // Simplified geometry update. In the full implementation, this would
        // iterate over physical fragments to compute the actual geometry.
        _fragmentainerGroups.DeleteExtraGroups();
        _fragmentainerGroups.First().ResetColumnHeight();
    }

    private void UpdateGeometryIfNeeded()
    {
        if (!HasValidCachedGeometry)
        {
            UpdateGeometry();
            HasValidCachedGeometry = true;
        }
    }

    internal bool HasValidCachedGeometry { get; set; }
}

// Extensions for LayoutObject to detect multi-column set type
public static class LayoutObjectMultiColumnExtensions
{
    public static bool IsLayoutMultiColumnSet(this LayoutObject obj) => obj is LayoutMultiColumnSet;
    public static float LogicalWidth(this PhysicalSize size, Geom.WritingMode mode)
    {
        return mode == Geom.WritingMode.HorizontalTb ? size.Width : size.Height;
    }
    public static PhysicalSize ConvertToLogical(this PhysicalSize size, Geom.WritingMode mode)
    {
        return mode == Geom.WritingMode.HorizontalTb ? size : new PhysicalSize(size.Height, size.Width);
    }
}