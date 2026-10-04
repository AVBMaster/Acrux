using Acrux.Core.Layout.Geometry;
using Geom = Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Used to find the fragmentainers that intersect with a given portion of the
/// flow thread. The portion typically corresponds to the bounds of some
/// descendant layout object. The iterator walks in block direction order.
/// </summary>
public sealed class FragmentainerIterator
{
    private LayoutMultiColumnSet? _currentColumnSet;
    private uint _currentFragmentainerGroupIndex;
    private uint _currentFragmentainerIndex;
    private uint _endFragmentainerIndex;

    private float _logicalTopInFlowThread;
    private float _logicalBottomInFlowThread;

    private bool _boundingBoxIsEmpty;

    // Initialize the iterator, and move to the first fragmentainer of interest.
    // Only thing that can limit the set of fragmentainers to visit is
    // |physicalBoundingBoxInFlowThread|.
    public FragmentainerIterator(LayoutFlowThread flowThread, PhysicalRect physicalBoundingBoxInFlowThread)
        : this(flowThread, physicalBoundingBoxInFlowThread, 0)
    {
    }

    private FragmentainerIterator(LayoutFlowThread flowThread, PhysicalRect physicalBoundingBoxInFlowThread, uint unused)
    {
        LogicalRect boundsInFlowThread = flowThread.CreateWritingModeConverter().ToLogical(physicalBoundingBoxInFlowThread);

        _logicalTopInFlowThread = boundsInFlowThread.Offset.BlockOffset;
        _logicalBottomInFlowThread = boundsInFlowThread.BlockEnd;
        _boundingBoxIsEmpty = boundsInFlowThread.InlineSize <= 0 || boundsInFlowThread.BlockSize <= 0;

        // Jump to the first interesting column set.
        _currentColumnSet = flowThread.ColumnSetAtBlockOffset(_logicalTopInFlowThread, PageBoundaryRule.AssociateWithLatterPage);
        if (_currentColumnSet == null)
        {
            SetAtEnd();
            return;
        }
        // Then find the first interesting fragmentainer group.
        _currentFragmentainerGroupIndex
            = _currentColumnSet.FragmentainerGroupIndexAtFlowThreadOffset(_logicalTopInFlowThread, PageBoundaryRule.AssociateWithLatterPage);

        // Now find the first and last fragmentainer we're interested in.
        SetFragmentainersOfInterest();
    }

    // Advance to the next fragmentainer. Not allowed to call this if AtEnd() is true.
    public void Advance()
    {
        System.Diagnostics.Debug.Assert(!AtEnd());

        if (_currentFragmentainerIndex < _endFragmentainerIndex)
        {
            _currentFragmentainerIndex++;
        }
        else
        {
            // That was the last fragmentainer to visit in this fragmentainer group.
            // Advance to the next group.
            MoveToNextFragmentainerGroup();
            if (AtEnd())
                return;
        }
    }

    // Return true if we have walked through all relevant fragmentainers.
    public bool AtEnd() => _currentColumnSet == null;

    // Return the physical clip rectangle of the current fragmentainer, relative
    // to the flow thread.
    public PhysicalRect ClipRectInFlowThread()
    {
        System.Diagnostics.Debug.Assert(!AtEnd());
        PhysicalRect clipRect;
        // An empty bounding box rect would typically be 0,0 0x0, so it would be
        // placed in the first column always. However, the first column might not have
        // a top edge clip (see FlowThreadPortionOverflowRectAt()). This might cause
        // artifacts to paint outside of the column container. To avoid this
        // situation, and since the logical bounding box is empty anyway, use the
        // portion rect instead which is bounded on all sides. Note that we don't
        // return an empty clip here, because an empty clip indicates that we have an
        // empty column which may be treated differently by the calling code.
        if (_boundingBoxIsEmpty)
        {
            clipRect = CurrentGroup().FlowThreadPortionRectAt(_currentFragmentainerIndex);
        }
        else
        {
            clipRect = CurrentGroup().FlowThreadPortionOverflowRectAt(_currentFragmentainerIndex);
        }
        return clipRect;
    }

    private MultiColumnFragmentainerGroup CurrentGroup()
    {
        System.Diagnostics.Debug.Assert(!AtEnd());
        return _currentColumnSet!.FragmentainerGroups()[(int)_currentFragmentainerGroupIndex];
    }

    private void MoveToNextFragmentainerGroup()
    {
        _currentFragmentainerGroupIndex++;
        if (_currentFragmentainerGroupIndex >= _currentColumnSet!.FragmentainerGroups().Size)
        {
            // That was the last fragmentainer group in this set. Advance to the next.
            _currentColumnSet = _currentColumnSet.NextSiblingMultiColumnSet();
            _currentFragmentainerGroupIndex = 0;
            if (_currentColumnSet == null || _currentColumnSet.LogicalTopInFlowThread() >= _logicalBottomInFlowThread)
            {
                SetAtEnd();
                return; // No more sets or next set out of range. We're done.
            }
        }
        if (CurrentGroup().LogicalTopInFlowThread >= _logicalBottomInFlowThread)
        {
            // This fragmentainer group doesn't intersect with the range we're
            // interested in. We're done.
            SetAtEnd();
            return;
        }
        SetFragmentainersOfInterest();
    }

    private void SetFragmentainersOfInterest()
    {
        MultiColumnFragmentainerGroup group = CurrentGroup();

        // Figure out the start and end fragmentainers for the block range we're
        // interested in. We might not have to walk the entire fragmentainer group.
        group.ColumnIntervalForBlockRangeInFlowThread(
            _logicalTopInFlowThread, _logicalBottomInFlowThread, out _currentFragmentainerIndex, out _endFragmentainerIndex);
        System.Diagnostics.Debug.Assert(_endFragmentainerIndex >= _currentFragmentainerIndex);
    }

    private void SetAtEnd() => _currentColumnSet = null;
}