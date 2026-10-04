using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Geom = Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Enum for page boundary association rules when mapping flow thread offsets
/// to columns.
/// </summary>
public enum PageBoundaryRule
{
    AssociateWithFormerPage,
    AssociateWithLatterPage,
}

/// <summary>
/// LayoutFlowThread is used to collect all the layout objects that participate
/// in a flow thread. It will also help in doing the layout. However, it will not
/// layout directly to screen. Instead, LayoutMultiColumnSet objects will
/// redirect their paint and nodeAtPoint methods to this object. Each
/// LayoutMultiColumnSet will actually be a viewPort of the LayoutFlowThread.
/// </summary>
public abstract class LayoutFlowThread : LayoutBlockFlow
{
    protected readonly List<LayoutMultiColumnSet> _multiColumnSetList = new();

    // Interval tree for column sets, implemented as a simple sorted list of
    // intervals, since the PODIntervalTree from WTF is not available.
    protected readonly List<MultiColumnSetInterval> _multiColumnSetIntervalTree = new();

    protected bool _columnSetsInvalidated;

    public LayoutFlowThread()
        : base(null)
    {
        _columnSetsInvalidated = false;
    }

    public bool IsLayoutFlowThread => true;
    public virtual bool IsLayoutMultiColumnFlowThread => false;

    public bool CreatesNewFormattingContext => true;

    // Search mode when looking for an enclosing fragmentation context.
    public enum AncestorSearchConstraint
    {
        // No constraints. When we're not laying out (but rather e.g. painting or
        // hit-testing), we just want to find all enclosing fragmentation contexts,
        // e.g. to calculate the accumulated visual translation.
        AnyAncestor,

        // Consider fragmentation contexts that are strictly unbreakable (seen from
        // the outside) to be isolated from the rest, so that such fragmentation
        // contexts don't participate in fragmentation of enclosing fragmentation
        // contexts, apart from taking up space and otherwise being completely
        // unbreakable. This is typically what we want to do during layout.
        IsolateUnbreakableContainers,
    }

    public static LayoutFlowThread? LocateFlowThreadContainingBlockOf(LayoutObject descendant, AncestorSearchConstraint constraint)
    {
        if (!descendant.IsInsideFlowThread)
            return null;
        LayoutObject curr = descendant;
        bool innerIsNgObject = curr.IsLayoutNGObject;
        while (curr != null)
        {
            if (curr.IsSVGChild)
                return null;
            // Always consider an in-flow legend child to be part of the flow
            // thread. The containing block of the rendered legend is actually the
            // multicol container itself (not its flow thread child), but since which
            // element is the rendered legend might change (if we insert another legend
            // in front of it, for instance), and such a change won't be detected by
            // this child, we'll just pretend that it's part of the flow thread.
            if (curr.Node is Element el && el.TagName == "LEGEND" && !curr.IsOutOfFlowPositioned && !curr.IsColumnSpanAll && curr.Parent is LayoutFlowThread)
                return (LayoutFlowThread)curr.Parent;
            if (curr is LayoutFlowThread flowThread)
                return flowThread;
            LayoutObject? container = curr.ContainingBlock();
            // If we're inside something strictly unbreakable (due to having scrollbars
            // or being writing mode roots, for instance), it's also strictly
            // unbreakable in any outer fragmentation context. As such, what goes on
            // inside any fragmentation context on the inside of this is completely
            // opaque to ancestor fragmentation contexts.
            if (constraint == AncestorSearchConstraint.IsolateUnbreakableContainers && container != null)
            {
                if (container is AuroraBox box)
                {
                    // We're walking up the tree without knowing which fragmentation engine
                    // is being used, so we have to detect any engine mismatch ourselves.
                    if (box.IsLayoutNGObject != innerIsNgObject)
                        return null;
                    if (box.IsMonolithic)
                        return null;
                }
            }
            curr = curr.Parent!;
            while (curr != container)
            {
                if (curr is LayoutFlowThread)
                {
                    // The nearest ancestor flow thread isn't in our containing block chain.
                    // Then we aren't really part of any flow thread, and we should stop
                    // looking. This happens when there are out-of-flow objects or column
                    // spanners.
                    return null;
                }
                curr = curr.Parent!;
            }
        }
        return null;
    }

    public virtual void FlowThreadDescendantWasInserted(LayoutObject descendant) { }
    public virtual void FlowThreadDescendantWillBeRemoved(LayoutObject descendant) { }
    public virtual void FlowThreadDescendantStyleWillChange(LayoutBoxModelObject descendant, object diff, ComputedStyle newStyle) { }
    public virtual void FlowThreadDescendantStyleDidChange(LayoutBoxModelObject descendant, object diff, ComputedStyle oldStyle) { }

    public abstract void AddColumnSetToThread(LayoutMultiColumnSet columnSet);
    public virtual void RemoveColumnSetFromThread(LayoutMultiColumnSet columnSet)
    {
        _multiColumnSetList.Remove(columnSet);
        InvalidateColumnSets();
        // Clear the interval tree right away, instead of leaving it around with dead
        // objects.
        _multiColumnSetIntervalTree.Clear();
    }

    public bool HasColumnSets => _multiColumnSetList.Count > 0;

    public void ValidateColumnSets()
    {
        _columnSetsInvalidated = false;
        GenerateColumnSetIntervalTree();
    }

    public void InvalidateColumnSets() => _columnSetsInvalidated = true;

    public bool HasValidColumnSetInfo => !_columnSetsInvalidated && _multiColumnSetList.Count > 0;

    public virtual bool IsPageLogicalHeightKnown => true;

    // Return the visual bounding box based on the supplied flow-thread bounding
    // box. Both rectangles are completely physical in terms of writing mode.
    public PhysicalRect FragmentsBoundingBox(PhysicalRect layerBoundingBox)
    {
        System.Diagnostics.Debug.Assert(!_columnSetsInvalidated);
        PhysicalRect result = PhysicalRect.Zero;
        foreach (var columnSet in _multiColumnSetList)
            result = result.Union(columnSet.FragmentsBoundingBox(layerBoundingBox));
        return result;
    }

    public abstract PhysicalOffset VisualPointToFlowThreadPoint(PhysicalOffset visualPoint);

    public abstract LayoutMultiColumnSet? ColumnSetAtBlockOffset(float offset, PageBoundaryRule rule);

    public override string GetName() => "LayoutFlowThread";

    protected void GenerateColumnSetIntervalTree()
    {
        // FIXME: Optimize not to clear the interval all the time. This implies
        // manually managing the tree nodes lifecycle.
        _multiColumnSetIntervalTree.Clear();
        foreach (var columnSet in _multiColumnSetList)
        {
            _multiColumnSetIntervalTree.Add(new MultiColumnSetInterval(
                columnSet.LogicalTopInFlowThread(), columnSet.LogicalBottomInFlowThread(), columnSet));
        }
    }

    // Internal interval tree structure
    protected readonly struct MultiColumnSetInterval
    {
        public float Low { get; }
        public float High { get; }
        public LayoutMultiColumnSet Data { get; }

        public MultiColumnSetInterval(float low, float high, LayoutMultiColumnSet data)
        {
            Low = low; High = high; Data = data;
        }
    }

    protected class MultiColumnSetSearchAdapter
    {
        private readonly float _offset;
        private LayoutMultiColumnSet? _result;

        public MultiColumnSetSearchAdapter(float offset)
        {
            _offset = offset;
            _result = null;
        }

        public float LowValue => _offset;
        public float HighValue => _offset;

        public void CollectIfNeeded(MultiColumnSetInterval interval)
        {
            if (_result != null)
                return;
            if (interval.Low <= _offset && interval.High > _offset)
                _result = interval.Data;
        }

        public LayoutMultiColumnSet? Result => _result;
    }

    // Add missing properties used by multi-column code
    public new PhysicalOffset PhysicalLocation() => FrameLocation;
    public bool IsLayoutNGObject => false;
    public bool IsInsideFlowThread => true;
    public bool IsColumnSpanAll => false;
    public bool IsOutOfFlowPositioned => false;
    public float LogicalWidth => FrameSize.Width;

    public WritingModeConverter CreateWritingModeConverter()
    {
        var dir = new WritingDirectionMode(Geom.WritingMode.HorizontalTb, TextDirection.Ltr);
        return new WritingModeConverter(dir, FrameSize);
    }
}

// Extension to LayoutObject for flow thread methods
public static class LayoutObjectFlowThreadExtensions
{
    public static LayoutFlowThread? FlowThreadContainingBlock(this LayoutObject obj)
    {
        return LayoutFlowThread.LocateFlowThreadContainingBlockOf(obj, LayoutFlowThread.AncestorSearchConstraint.IsolateUnbreakableContainers);
    }
    public static bool IsLayoutNGObject(this LayoutObject obj) => false;
    public static LayoutMultiColumnSpannerPlaceholder? SpannerPlaceholder(this LayoutObject obj) => null;
    public static void SetSpannerPlaceholder(this LayoutObject obj, LayoutMultiColumnSpannerPlaceholder placeholder) { }
    public static void ClearSpannerPlaceholder(this LayoutObject obj) { }
}