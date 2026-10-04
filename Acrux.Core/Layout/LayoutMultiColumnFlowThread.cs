using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Geom = Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Flow thread implementation for CSS multicol. This will be inserted as an
/// anonymous child block of the actual multicol container (i.e. the
/// LayoutBlockFlow whose style computes to non-auto column-count and/or
/// column-width). LayoutMultiColumnFlowThread is the heart of the multicol
/// implementation, and there is only one instance per multicol container. Child
/// content of the multicol container is parented into the flow thread at the
/// time of layoutObject insertion.
///
/// Apart from this flow thread child, the multicol container will also have
/// LayoutMultiColumnSet children, which are used to position the columns
/// visually. The flow thread is in charge of layout, and, after having
/// calculated the column width, it lays out content as if everything were in one
/// tall single column, except that there will typically be some amount of blank
/// space (also known as pagination struts) at the offsets where the actual
/// column boundaries are. This way, content that needs to be preceded by a break
/// will appear at the top of the next column. Content needs to be preceded by a
/// break when there's a forced break or when the content is unbreakable and
/// cannot fully fit in the same column as the preceding piece of content.
/// Although a LayoutMultiColumnFlowThread is laid out, it does not take up any
/// space in its container. It's the LayoutMultiColumnSet objects that take up
/// the necessary amount of space, and make sure that the columns are painted and
/// hit-tested correctly.
///
/// If there is any column content inside the multicol container, we create a
/// LayoutMultiColumnSet. We only need to create multiple sets if there are
/// spanners (column-span:all) in the multicol container. When a spanner is
/// inserted, content preceding it gets its own set, and content succeeding it
/// will get another set. The spanner itself will also get its own placeholder
/// between the sets (LayoutMultiColumnSpannerPlaceholder), so that it gets
/// positioned and sized correctly. The column-span:all element is inside the
/// flow thread, but its containing block is the multicol container.
///
/// Some invariants for the layout tree structure for multicol:
/// - A multicol container is always a LayoutBlockFlow
/// - Every multicol container has one and only one LayoutMultiColumnFlowThread
/// - All multicol DOM children and pseudo-elements associated with the multicol
///   container are reparented into the flow thread.
/// - The LayoutMultiColumnFlowThread is the first child of the multicol
///   container.
/// - A multicol container may only have LayoutMultiColumnFlowThread,
///   LayoutMultiColumnSet and LayoutMultiColumnSpannerPlaceholder children.
/// - A LayoutMultiColumnSet may not be adjacent to another LayoutMultiColumnSet;
///   there are no use-cases for it, and there are also implementation
///   limitations behind this requirement.
/// - The flow thread is not in the containing block chain for children that are
///   not to be laid out in columns. This means column spanners and absolutely
///   positioned children whose containing block is outside column content
/// - Each spanner (column-span:all) establishes a
///   LayoutMultiColumnSpannerPlaceholder
///
/// The width of the flow thread is the same as the column width. The width of a
/// column set is the same as the content box width of the multicol container; in
/// other words exactly enough to hold the number of columns to be used, stacked
/// horizontally, plus column gaps between them.
///
/// Since it's the first child of the multicol container, the flow thread is laid
/// out first, albeit in a slightly special way, since it's not to take up any
/// space in its ancestors. Afterwards, the column sets are laid out. Column sets
/// get their height from the columns that they hold. In single column-row
/// constrained height non-balancing cases without spanners this will simply be
/// the same as the content height of the multicol container itself. In most
/// other cases we'll have to calculate optimal column heights ourselves, though.
/// This process is referred to as column balancing, and then we infer the column
/// height from the height of the flow thread portion occupied by each set.
///
/// More on column balancing: the columns' height is unknown in the first layout
/// pass when balancing. This means that we cannot insert any implicit (soft /
/// unforced) breaks (and pagination struts) when laying out the contents of the
/// flow thread. We'll just lay out everything in tall single strip. After the
/// initial flow thread layout pass we can determine a tentative / minimal /
/// initial column height. This is calculated by simply dividing the flow
/// thread's height by the number of specified columns. In the layout pass that
/// follows, we can insert breaks (and pagination struts) at column boundaries,
/// since we now have a column height.
/// It may very easily turn out that the calculated height wasn't enough, though.
/// We'll notice this at end of layout. If we end up with too many columns (i.e.
/// columns overflowing the multicol container), it wasn't enough. In this case
/// we need to increase the column heights. We'll increase them by the lowest
/// amount of space that could possibly affect where the breaks occur. We'll
/// relayout (to find new break points and the new lowest amount of space
/// increase that could affect where they occur, in case we need another round)
/// until we've reached an acceptable height (where everything fits perfectly in
/// the number of columns that we have specified). The rule of thumb is that we
/// shouldn't have to perform more of such iterations than the number of columns
/// that we have.
///
/// For each layout iteration done for column balancing, the flow thread will
/// need a deep layout if column heights changed in the previous pass, since
/// column height changes may affect break points and pagination struts anywhere
/// in the tree, and currently no way exists to do this in a more optimized
/// manner.
///
/// There's also some documentation online:
/// https://www.chromium.org/developers/design-documents/multi-column-layout
/// </summary>
public sealed class LayoutMultiColumnFlowThread : LayoutFlowThread
{
    private LayoutMultiColumnSet? _lastSetWorkedOn;
    private uint _columnCount = 1;
    private bool _isBeingEvacuated;

    public LayoutMultiColumnFlowThread()
        : base()
    {
        _columnCount = 1;
        _isBeingEvacuated = false;
    }

    public override bool IsLayoutMultiColumnFlowThread => true;

    public override string GetName() => "LayoutMultiColumnFlowThread";

    public static LayoutMultiColumnFlowThread CreateAnonymous(Document document, ComputedStyle parentStyle)
    {
        var layoutObject = new LayoutMultiColumnFlowThread();
        // Set document and style for anonymous
        return layoutObject;
    }

    public LayoutBlockFlow MultiColumnBlockFlow() => (LayoutBlockFlow)Parent!;

    public LayoutMultiColumnSet? FirstMultiColumnSet()
    {
        for (LayoutObject? sibling = NextSibling; sibling != null; sibling = sibling.NextSibling)
        {
            if (sibling.IsLayoutMultiColumnSet())
                return (LayoutMultiColumnSet)sibling;
        }
        return null;
    }

    public LayoutMultiColumnSet? LastMultiColumnSet()
    {
        for (LayoutObject? sibling = MultiColumnBlockFlow().LastChild(); sibling != null; sibling = sibling.PreviousSibling)
        {
            if (sibling.IsLayoutMultiColumnSet())
                return (LayoutMultiColumnSet)sibling;
        }
        return null;
    }

    // Return the first column set or spanner placeholder.
    public AuroraBox? FirstMultiColumnBox()
    {
        return (AuroraBox?)NextSibling;
    }

    // Return the last column set or spanner placeholder.
    public AuroraBox? LastMultiColumnBox()
    {
        AuroraBox? lastSiblingBox = MultiColumnBlockFlow().LastChildBox();
        // The flow thread is the first child of the multicol container. If the flow
        // thread is also the last child, it means that there are no siblings; i.e.
        // we have no column boxes.
        return lastSiblingBox != this && lastSiblingBox != null ? lastSiblingBox : null;
    }

    // Find the first set inside which the specified layoutObject (which is a
    // flowthread descendant) would be rendered.
    public LayoutMultiColumnSet? MapDescendantToColumnSet(LayoutObject layoutObject)
    {
        // Should not be used for spanners or content inside them.
        System.Diagnostics.Debug.Assert(ContainingColumnSpannerPlaceholder(layoutObject) == null);
        System.Diagnostics.Debug.Assert(layoutObject != this);
        System.Diagnostics.Debug.Assert(layoutObject.IsDescendantOf(this));
        System.Diagnostics.Debug.Assert(layoutObject.FlowThreadContainingBlock() == this);
        System.Diagnostics.Debug.Assert(!layoutObject.IsLayoutMultiColumnSet());
        System.Diagnostics.Debug.Assert(!layoutObject.IsLayoutMultiColumnSpannerPlaceholder());

        var multicolSet = FirstMultiColumnSet();
        if (multicolSet == null)
            return null;
        if (multicolSet.NextSiblingMultiColumnSet() == null)
            return multicolSet;

        // This is potentially SLOW! But luckily very uncommon. You would have to
        // dynamically insert a spanner into the middle of column contents to need
        // this.
        for (; multicolSet != null; multicolSet = multicolSet.NextSiblingMultiColumnSet())
        {
            LayoutObject? firstLayoutObject = FirstLayoutObjectInSet(multicolSet);
            LayoutObject? lastLayoutObject = LastLayoutObjectInSet(multicolSet);
            System.Diagnostics.Debug.Assert(firstLayoutObject != null);

            for (LayoutObject? walker = firstLayoutObject; walker != null; walker = walker.NextInPreOrder(this))
            {
                if (walker == layoutObject)
                    return multicolSet;
                if (walker == lastLayoutObject)
                    break;
            }
        }

        return null;
    }

    // Return the spanner placeholder that belongs to the spanner in the
    // containing block chain, if any. This includes the layoutObject for the
    // element that actually establishes the spanner too.
    public LayoutMultiColumnSpannerPlaceholder? ContainingColumnSpannerPlaceholder(LayoutObject descendant)
    {
        System.Diagnostics.Debug.Assert(descendant.IsDescendantOf(this));

        if (!HasAnyColumnSpanners())
            return null;

        // We have spanners. See if the layoutObject in question is one or inside of
        // one then.
        for (LayoutObject? ancestor = descendant; ancestor != null && ancestor != this; ancestor = ancestor.Parent)
        {
            var placeholder = ancestor.SpannerPlaceholder();
            if (placeholder != null)
                return placeholder;
        }
        return null;
    }

    // Populate the flow thread with what's currently its siblings. Called when a
    // regular block becomes a multicol container.
    public void Populate()
    {
        LayoutBlockFlow multicolContainer = MultiColumnBlockFlow();
        // Reparent children preceding the flow thread into the flow thread. It's
        // multicol content now. At this point there's obviously nothing after the
        // flow thread, but layoutObjects (column sets and spanners) will be inserted
        // there as we insert elements into the flow thread.
        // Move children from multicol container to this flow thread.
        while (multicolContainer.Children.Count > 0)
        {
            var child = multicolContainer.Children[0];
            multicolContainer.RemoveChild(child);
            AddChild(child);
        }
    }

    // Empty the flow thread by moving everything to the parent. Remove all
    // multicol specific layoutObjects. Then destroy the flow thread. Called when
    // a multicol container becomes a regular block.
    public void EvacuateAndDestroy()
    {
        LayoutBlockFlow multicolContainer = MultiColumnBlockFlow();
        _isBeingEvacuated = true;

        // Remove all sets and spanners.
        AuroraBox? columnBox;
        while ((columnBox = FirstMultiColumnBox()) != null)
        {
            // Destroy the column box
            if (columnBox is LayoutMultiColumnSet set)
                RemoveColumnSetFromThread(set);
            // Remove from tree
            multicolContainer.RemoveChild(columnBox);
        }

        // Finally we can promote all flow thread's children. Before we move them to
        // the flow thread's container, we need to unregister the flow thread, so that
        // they aren't just re-added again to the flow thread that we're trying to
        // empty.
        // Move all children including floats to the multicol container.
        while (Children.Count > 0)
        {
            var child = Children[0];
            RemoveChild(child);
            multicolContainer.AddChild(child);
        }
    }

    public uint ColumnCount => _columnCount;

    public PhysicalOffset ColumnOffset(PhysicalOffset point)
    {
        return FlowThreadTranslationAtPoint(point);
    }

    public override bool IsPageLogicalHeightKnown => _allColumnsHaveKnownHeight;

    public PhysicalOffset FlowThreadTranslationAtOffset(float offsetInFlowThread, PageBoundaryRule rule)
    {
        if (!HasValidColumnSetInfo)
            return PhysicalOffset.Zero;
        var columnSet = ColumnSetAtBlockOffset(offsetInFlowThread, rule);
        if (columnSet == null)
            return PhysicalOffset.Zero;
        return columnSet.FlowThreadTranslationAtOffset(offsetInFlowThread, rule);
    }

    public PhysicalOffset FlowThreadTranslationAtPoint(PhysicalOffset flowThreadPoint)
    {
        var converter = CreateWritingModeConverter();
        float blockOffset = converter.ToLogical(flowThreadPoint, PhysicalSize.Zero).BlockOffset;

        // If block direction is flipped, points at a column boundary belong in the
        // former column, not the latter.
        PageBoundaryRule rule = HasFlippedBlocksWritingMode() ? PageBoundaryRule.AssociateWithFormerPage : PageBoundaryRule.AssociateWithLatterPage;

        return FlowThreadTranslationAtOffset(blockOffset, rule);
    }

    public override PhysicalOffset VisualPointToFlowThreadPoint(PhysicalOffset visualPoint)
    {
        var converter = new WritingModeConverter(
            new WritingDirectionMode(Geom.WritingMode.HorizontalTb, TextDirection.Ltr), FrameSize);
        float blockOffset = converter.ToLogical(visualPoint, PhysicalSize.Zero).BlockOffset;
        LayoutMultiColumnSet? columnSet = null;
        for (var candidate = FirstMultiColumnSet(); candidate != null; candidate = candidate.NextSiblingMultiColumnSet())
        {
            columnSet = candidate;
            if (candidate.LogicalBottom() > blockOffset)
                break;
        }
        if (columnSet == null)
        {
            return visualPoint;
        }
        PhysicalOffset flowThreadOffset = PhysicalLocation();
        PhysicalOffset columnSetOffset = columnSet.PhysicalLocation();
        PhysicalOffset pointInSet = visualPoint + flowThreadOffset - columnSetOffset;
        return converter.ToPhysical(columnSet.VisualPointToFlowThreadPoint(pointInSet), PhysicalSize.Zero);
    }

    public override LayoutMultiColumnSet? ColumnSetAtBlockOffset(float offset, PageBoundaryRule pageBoundaryRule)
    {
        LayoutMultiColumnSet? columnSet = _lastSetWorkedOn;
        if (columnSet != null)
        {
            // Layout in progress. We are calculating the set heights as we speak, so
            // the column set range information is not up to date.
            while (columnSet.LogicalTopInFlowThread() > offset)
            {
                // Sometimes we have to use a previous set. This happens when we're
                // working with a block that contains a spanner (so that there's a column
                // set both before and after the spanner, and both sets contain said
                // block).
                LayoutMultiColumnSet? previousSet = columnSet.PreviousSiblingMultiColumnSet();
                if (previousSet == null)
                    break;
                columnSet = previousSet;
            }
        }
        else
        {
            System.Diagnostics.Debug.Assert(!_columnSetsInvalidated);
            if (_multiColumnSetList.Count == 0)
                return null;
            if (offset < 0)
            {
                columnSet = _multiColumnSetList[0];
            }
            else
            {
                MultiColumnSetSearchAdapter adapter = new MultiColumnSetSearchAdapter(offset);
                foreach (var interval in _multiColumnSetIntervalTree)
                    adapter.CollectIfNeeded(interval);

                // If no set was found, the offset is in the flow thread overflow.
                if (adapter.Result == null && _multiColumnSetList.Count > 0)
                    columnSet = _multiColumnSetList[_multiColumnSetList.Count - 1];
                else
                    columnSet = adapter.Result;
            }
        }
        if (pageBoundaryRule == PageBoundaryRule.AssociateWithFormerPage && columnSet != null && offset == columnSet.LogicalTopInFlowThread())
        {
            // The column set that we found starts at the exact same flow thread offset
            // as we specified. Since we are to associate offsets at boundaries with the
            // former fragmentainer, the fragmentainer we're looking for is in the
            // previous column set.
            LayoutMultiColumnSet? previousSet = columnSet.PreviousSiblingMultiColumnSet();
            if (previousSet != null)
                columnSet = previousSet;
        }
        // Avoid returning zero-height column sets, if possible. We found a column set
        // based on a flow thread coordinate. If multiple column sets share that
        // coordinate (because we have zero-height column sets between column
        // spanners, for instance), look for one that has a height. Also look ahead to
        // find a set that actually contains the coordinate. Note that when we do this
        // during layout, it means that we might return a column set that hasn't got
        // its flow thread boundaries updated yet (and thus using those from the
        // previous layout), but that's the best we can do when our engine doesn't
        // actually understand fragmentation. This may happen when there's a float
        // that's split into multiple fragments because of column spanners, and we
        // still perform all its layout at the position before the first spanner in
        // question (i.e. where only the first fragment is supposed to be laid out).
        for (LayoutMultiColumnSet? walker = columnSet; walker != null; walker = walker.NextSiblingMultiColumnSet())
        {
            if (!walker.IsPageLogicalHeightKnown)
                continue;
            if (pageBoundaryRule == PageBoundaryRule.AssociateWithFormerPage)
            {
                if (walker.LogicalTopInFlowThread() < offset && walker.LogicalBottomInFlowThread() >= offset)
                    return walker;
            }
            else if (walker.LogicalTopInFlowThread() <= offset && walker.LogicalBottomInFlowThread() > offset)
            {
                return walker;
            }
        }
        return columnSet;
    }

    public void ColumnRuleStyleDidChange()
    {
        for (var columnSet = FirstMultiColumnSet(); columnSet != null; columnSet = columnSet.NextSiblingMultiColumnSet())
        {
            columnSet.SetShouldDoFullPaintInvalidation();
        }
    }

    // Remove the spanner placeholder and return true if the specified object is
    // no longer a valid spanner.
    public bool RemoveSpannerPlaceholderIfNoLongerValid(AuroraBox spannerObjectInFlowThread)
    {
        System.Diagnostics.Debug.Assert(spannerObjectInFlowThread.SpannerPlaceholder() != null);
        if (DescendantIsValidColumnSpanner(spannerObjectInFlowThread))
            return false; // Still a valid spanner.

        // No longer a valid spanner. Get rid of the placeholder.
        var placeholder = spannerObjectInFlowThread.SpannerPlaceholder();
        if (placeholder != null)
            DestroySpannerPlaceholder(placeholder);
        spannerObjectInFlowThread.ClearSpannerPlaceholder();

        // We may have a new containing block, since we're no longer a spanner. Mark
        // it for relayout.
        if (spannerObjectInFlowThread.ContainingBlock() != null)
            spannerObjectInFlowThread.ContainingBlock()!.NeedsLayout = true;

        // Now generate a column set for this ex-spanner, if needed and none is there
        // for us already.
        FlowThreadDescendantWasInserted(spannerObjectInFlowThread);

        return true;
    }

    public LayoutMultiColumnFlowThread? EnclosingFlowThread(AncestorSearchConstraint constraint = AncestorSearchConstraint.IsolateUnbreakableContainers)
    {
        if (!MultiColumnBlockFlow().IsInsideFlowThread)
            return null;
        return (LayoutMultiColumnFlowThread?)LocateFlowThreadContainingBlockOf(MultiColumnBlockFlow(), constraint);
    }

    public void SetColumnCountFromNG(uint columnCount)
    {
        _columnCount = columnCount;
    }

    public void FinishLayoutFromNG(float flowThreadOffset)
    {
        _allColumnsHaveKnownHeight = true;
        for (AuroraBox? columnBox = FirstMultiColumnBox(); columnBox != null; columnBox = (AuroraBox?)columnBox.NextSibling)
        {
            columnBox.NeedsLayout = false;
        }

        ValidateColumnSets();
        NeedsLayout = false;
        _lastSetWorkedOn = null;
    }

    public override void AddColumnSetToThread(LayoutMultiColumnSet columnSet)
    {
        var nextSet = columnSet.NextSiblingMultiColumnSet();
        if (nextSet != null)
        {
            int it = _multiColumnSetList.IndexOf(nextSet);
            if (it >= 0)
                _multiColumnSetList.Insert(it, columnSet);
            else
                _multiColumnSetList.Add(columnSet);
        }
        else
        {
            _multiColumnSetList.Add(columnSet);
        }
    }

    public override void RemoveColumnSetFromThread(LayoutMultiColumnSet columnSet)
    {
        _multiColumnSetList.Remove(columnSet);
        InvalidateColumnSets();
        _multiColumnSetIntervalTree.Clear();
    }

    protected override void ComputeIntrinsicLogicalWidths() { }

    private void CreateAndInsertMultiColumnSet(AuroraBox? insertBefore = null)
    {
        LayoutBlockFlow multicolContainer = MultiColumnBlockFlow();
        var newSet = LayoutMultiColumnSet.CreateAnonymous(this, multicolContainer.StyleRef());
        if (insertBefore != null)
        {
            // Insert before the given node
            int idx = multicolContainer.Children.IndexOf(insertBefore);
            if (idx >= 0)
                multicolContainer.AddChild(newSet);
        }
        else
        {
            multicolContainer.AddChild(newSet);
        }
        InvalidateColumnSets();

        // We cannot handle immediate column set siblings (and there's no need for it,
        // either). There has to be at least one spanner separating them.
        System.Diagnostics.Debug.Assert(newSet.PreviousSiblingMultiColumnSet() == null || !newSet.PreviousSiblingMultiColumnSet().IsLayoutMultiColumnSet);
        System.Diagnostics.Debug.Assert(newSet.NextSiblingMultiColumnSet() == null || !newSet.NextSiblingMultiColumnSet().IsLayoutMultiColumnSet);
    }

    private void CreateAndInsertSpannerPlaceholder(AuroraBox spannerObjectInFlowThread, LayoutObject? insertedBeforeInFlowThread)
    {
        AuroraBox? insertBeforeColumnBox = null;
        LayoutMultiColumnSet? setToSplit = null;
        if (insertedBeforeInFlowThread != null)
        {
            // The spanner is inserted before something. Figure out what this entails.
            // If the next object is a spanner too, it means that we can simply insert a
            // new spanner placeholder in front of its placeholder.
            insertBeforeColumnBox = (AuroraBox?)insertedBeforeInFlowThread.SpannerPlaceholder();
            if (insertBeforeColumnBox == null)
            {
                // The next object isn't a spanner; it's regular column content. Examine
                // what comes right before us in the flow thread, then.
                LayoutObject? previousLayoutObject = PreviousInPreOrderSkippingOutOfFlow(spannerObjectInFlowThread);
                if (previousLayoutObject == null || previousLayoutObject == this)
                {
                    // The spanner is inserted as the first child of the multicol container,
                    // which means that we simply insert a new spanner placeholder at the
                    // beginning.
                    insertBeforeColumnBox = FirstMultiColumnBox();
                }
                else
                {
                    var previousPlaceholder = ContainingColumnSpannerPlaceholder(previousLayoutObject);
                    if (previousPlaceholder != null)
                    {
                        // Before us is another spanner. We belong right after it then.
                        insertBeforeColumnBox = (AuroraBox?)previousPlaceholder.NextSibling;
                    }
                    else
                    {
                        // We're inside regular column content with both feet. Find out which
                        // column set this is. It needs to be split it into two sets, so that we
                        // can insert a new spanner placeholder between them.
                        setToSplit = MapDescendantToColumnSet(previousLayoutObject);
                        System.Diagnostics.Debug.Assert(setToSplit == MapDescendantToColumnSet(insertedBeforeInFlowThread));
                        insertBeforeColumnBox = (AuroraBox?)setToSplit.NextSiblingMultiColumnSet();
                        // We've found out which set that needs to be split. Now proceed to
                        // inserting the spanner placeholder, and then insert a second column
                        // set.
                    }
                }
            }
            System.Diagnostics.Debug.Assert(setToSplit != null || insertBeforeColumnBox != null);
        }

        LayoutBlockFlow multicolContainer = MultiColumnBlockFlow();
        var newPlaceholder = LayoutMultiColumnSpannerPlaceholder.CreateAnonymous(multicolContainer.StyleRef(), spannerObjectInFlowThread);
        // Insert the placeholder
        multicolContainer.AddChild(newPlaceholder);
        spannerObjectInFlowThread.SetSpannerPlaceholder(newPlaceholder);

        if (setToSplit != null)
            CreateAndInsertMultiColumnSet(insertBeforeColumnBox);
    }

    private void DestroySpannerPlaceholder(LayoutMultiColumnSpannerPlaceholder placeholder)
    {
        var nextColumnBox = (AuroraBox?)placeholder.NextSibling;
        var previousColumnBox = (AuroraBox?)placeholder.PreviousSibling;
        if (nextColumnBox != null && nextColumnBox.IsLayoutMultiColumnSet() && previousColumnBox != null && previousColumnBox.IsLayoutMultiColumnSet())
        {
            // Need to merge two column sets.
            // Remove next column set
            if (nextColumnBox is LayoutMultiColumnSet nextSet)
            {
                _multiColumnSetList.Remove(nextSet);
                InvalidateColumnSets();
            }
        }
        // Destroy the placeholder
        if (placeholder.Parent != null)
            ((LayoutBlock)placeholder.Parent).RemoveChild(placeholder);
    }

    private bool DescendantIsValidColumnSpanner(LayoutObject descendant)
    {
        // This method needs to behave correctly in the following situations:
        // - When the descendant doesn't have a spanner placeholder but should have
        //   one (return true).
        // - When the descendant doesn't have a spanner placeholder and still should
        //   not have one (return false).
        // - When the descendant has a spanner placeholder but should no longer have
        //   one (return false).
        // - When the descendant has a spanner placeholder and should still have one
        //   (return true).

        // We assume that we're inside the flow thread. This function is not to be
        // called otherwise.
        System.Diagnostics.Debug.Assert(descendant.IsDescendantOf(this));

        // The spec says that column-span only applies to in-flow block-level
        // elements.
        // Simplified check: we check if the descendant is a box and not inline.
        if (!descendant.IsColumnSpanAll || !descendant.IsBox || descendant.IsInline || descendant.IsFloating || descendant.IsOutOfFlowPositioned)
            return false;

        if (descendant.ContainingBlock() is not LayoutBlockFlow)
        {
            // Needs to be in a block-flow container, and not e.g. a table.
            return false;
        }

        // This looks like a spanner, but if we're inside something unbreakable or
        // something that establishes a new formatting context, it's not to be treated
        // as one.
        for (AuroraBox? ancestor = descendant.Parent as AuroraBox; ancestor != null; ancestor = ancestor.ContainingBlock() as AuroraBox)
        {
            if (ancestor is LayoutFlowThread)
            {
                System.Diagnostics.Debug.Assert(ancestor == this);
                return true;
            }
            if (!CanContainSpannerInParentFragmentationContext(ancestor))
                return false;
        }
        return false;
    }

    private bool CanContainSpannerInParentFragmentationContext(LayoutObject obj)
    {
        if (obj is not LayoutBlockFlow blockFlow)
            return false;
        return !blockFlow.CreatesNewFormattingContext && !blockFlow.IsMonolithic;
    }

    public void FlowThreadDescendantWasInserted(LayoutObject descendant)
    {
        System.Diagnostics.Debug.Assert(!_isBeingEvacuated);

        if (ShouldSkipInsertedOrRemovedChild(descendant))
            return;
        LayoutObject? objectAfterSubtree = NextInPreOrderAfterChildrenSkippingOutOfFlow(descendant);
        LayoutObject? next;
        for (LayoutObject? layoutObject = descendant; layoutObject != null; layoutObject = next)
        {
            if (layoutObject != descendant && ShouldSkipInsertedOrRemovedChild(layoutObject))
            {
                next = layoutObject.NextInPreOrderAfterChildren(descendant);
                continue;
            }
            next = layoutObject.NextInPreOrder(descendant);
            if (ContainingColumnSpannerPlaceholder(layoutObject) != null)
                continue; // Inside a column spanner. Nothing to do, then.
            if (DescendantIsValidColumnSpanner(layoutObject) && layoutObject is AuroraBox box)
            {
                // This layoutObject is a spanner, so it needs to establish a spanner
                // placeholder.
                CreateAndInsertSpannerPlaceholder(box, objectAfterSubtree);
                continue;
            }
            // This layoutObject is regular column content (i.e. not a spanner). Create
            // a set if necessary.
            if (objectAfterSubtree != null)
            {
                var placeholder = objectAfterSubtree.SpannerPlaceholder();
                if (placeholder != null)
                {
                    // If inserted right before a spanner, we need to make sure that there's
                    // a set for us there.
                    AuroraBox? previous = (AuroraBox?)placeholder.PreviousSibling;
                    if (previous == null || !previous.IsLayoutMultiColumnSet())
                        CreateAndInsertMultiColumnSet(placeholder);
                }
                else
                {
                    // Otherwise, since |objectAfterSubtree| isn't a spanner, it has to mean
                    // that there's already a set for that content. We can use it for this
                    // layoutObject too.
                    System.Diagnostics.Debug.Assert(MapDescendantToColumnSet(objectAfterSubtree) != null);
                    System.Diagnostics.Debug.Assert(MapDescendantToColumnSet(layoutObject) == MapDescendantToColumnSet(objectAfterSubtree));
                }
            }
            else
            {
                // Inserting at the end. Then we just need to make sure that there's a
                // column set at the end.
                AuroraBox? lastColumnBox = LastMultiColumnBox();
                if (lastColumnBox == null || !lastColumnBox.IsLayoutMultiColumnSet())
                    CreateAndInsertMultiColumnSet();
            }
        }
    }

    public void FlowThreadDescendantWillBeRemoved(LayoutObject descendant)
    {
        if (_isBeingEvacuated)
            return;
        if (ShouldSkipInsertedOrRemovedChild(descendant))
            return;
        bool hadContainingPlaceholder = ContainingColumnSpannerPlaceholder(descendant) != null;
        bool processedSomething = false;
        LayoutObject? next;
        // Remove spanner placeholders that are no longer needed, and merge column
        // sets around them.
        for (LayoutObject? layoutObject = descendant; layoutObject != null; layoutObject = next)
        {
            if (layoutObject != descendant && ShouldSkipInsertedOrRemovedChild(layoutObject))
            {
                next = layoutObject.NextInPreOrderAfterChildren(descendant);
                continue;
            }
            processedSomething = true;
            var placeholder = layoutObject.SpannerPlaceholder();
            if (placeholder == null)
            {
                next = layoutObject.NextInPreOrder(descendant);
                continue;
            }
            next = layoutObject.NextInPreOrderAfterChildren(descendant); // It's a spanner. Its children are of no interest to us.
            DestroySpannerPlaceholder(placeholder);
        }
        if (hadContainingPlaceholder || !processedSomething)
            return; // No column content will be removed, so we can stop here.

        // Column content will be removed. Does this mean that we should destroy a
        // column set?
        LayoutMultiColumnSpannerPlaceholder? adjacentPreviousSpannerPlaceholder = null;
        LayoutObject? previousLayoutObject = PreviousInPreOrderSkippingOutOfFlow(descendant);
        if (previousLayoutObject != null && previousLayoutObject != this)
        {
            adjacentPreviousSpannerPlaceholder = ContainingColumnSpannerPlaceholder(previousLayoutObject);
            if (adjacentPreviousSpannerPlaceholder == null)
                return; // Preceded by column content. Set still needed.
        }
        LayoutMultiColumnSpannerPlaceholder? adjacentNextSpannerPlaceholder = null;
        LayoutObject? nextLayoutObject = NextInPreOrderAfterChildrenSkippingOutOfFlow(descendant);
        if (nextLayoutObject != null)
        {
            adjacentNextSpannerPlaceholder = ContainingColumnSpannerPlaceholder(nextLayoutObject);
            if (adjacentNextSpannerPlaceholder == null)
                return; // Followed by column content. Set still needed.
        }
        // We have now determined that, with the removal of |descendant|, we should
        // remove a column set. Locate it and remove it.
        LayoutMultiColumnSet? columnSetToRemove;
        if (adjacentNextSpannerPlaceholder != null)
        {
            var sibling = (AuroraBox?)adjacentNextSpannerPlaceholder.PreviousSibling;
            System.Diagnostics.Debug.Assert(sibling != null && sibling.IsLayoutMultiColumnSet());
            columnSetToRemove = (LayoutMultiColumnSet)sibling;
        }
        else if (adjacentPreviousSpannerPlaceholder != null)
        {
            var sibling = (AuroraBox?)adjacentPreviousSpannerPlaceholder.NextSibling;
            System.Diagnostics.Debug.Assert(sibling != null && sibling.IsLayoutMultiColumnSet());
            columnSetToRemove = (LayoutMultiColumnSet)sibling;
        }
        else
        {
            // If there were no adjacent spanners, it has to mean that there's only one
            // column set, since it's only spanners that may cause creation of
            // multiple sets.
            columnSetToRemove = FirstMultiColumnSet();
            System.Diagnostics.Debug.Assert(columnSetToRemove != null);
            System.Diagnostics.Debug.Assert(columnSetToRemove.NextSiblingMultiColumnSet() == null);
        }
        System.Diagnostics.Debug.Assert(columnSetToRemove != null);
        if (columnSetToRemove != null)
        {
            _multiColumnSetList.Remove(columnSetToRemove);
            if (columnSetToRemove.Parent != null)
                ((LayoutBlock)columnSetToRemove.Parent).RemoveChild(columnSetToRemove);
        }
    }

    private bool HasAnyColumnSpanners()
    {
        var firstBox = FirstMultiColumnBox();
        return firstBox != null && (firstBox != LastMultiColumnBox() || firstBox.IsLayoutMultiColumnSpannerPlaceholder());
    }

    private static bool ShouldSkipInsertedOrRemovedChild(LayoutObject child)
    {
        if (child.IsSVGChild)
            return true;
        if (child is LayoutFlowThread)
            return true;
        if (child.IsLayoutMultiColumnSet() || child.IsLayoutMultiColumnSpannerPlaceholder())
            return true;
        if (child.IsOutOfFlowPositioned && !child.IsInsideFlowThread)
            return true;
        return false;
    }

    private LayoutObject? NextInPreOrderAfterChildrenSkippingOutOfFlow(LayoutObject descendant)
    {
        System.Diagnostics.Debug.Assert(descendant.IsDescendantOf(this));
        LayoutObject? obj = descendant.NextInPreOrderAfterChildren(this);
        while (obj != null)
        {
            // Walk through the siblings and find the first one which is either in-flow
            // or has this flow thread as its containing block flow thread.
            if (!obj.IsOutOfFlowPositioned)
                break;
            if (obj.FlowThreadContainingBlock() == this)
                break;
            obj = obj.NextInPreOrderAfterChildren(this);
        }
        return obj;
    }

    private LayoutObject? PreviousInPreOrderSkippingOutOfFlow(LayoutObject descendant)
    {
        System.Diagnostics.Debug.Assert(descendant.IsDescendantOf(this));
        LayoutObject? obj = descendant.PreviousInPreOrder(this);
        while (obj != null && obj != this)
        {
            if (obj.IsColumnSpanAll)
            {
                var placeholderFlowThread = obj.SpannerPlaceholder()?.FlowThread();
                if (placeholderFlowThread == this)
                    break;
                // We're inside an inner multicol container. We have no business there.
                // Continue on the outside.
                obj = placeholderFlowThread?.MultiColumnBlockFlow();
                System.Diagnostics.Debug.Assert(obj == null || obj.IsDescendantOf(this));
                if (obj == null)
                    break;
                continue;
            }
            if (obj.FlowThreadContainingBlock() == this)
            {
                LayoutObject? ancestor;
                for (ancestor = obj.Parent; ; ancestor = ancestor.Parent)
                {
                    if (ancestor == this)
                        return obj;
                    if (ancestor is LayoutFlowThread)
                    {
                        // We're inside an inner multicol container. We have no business
                        // there.
                        break;
                    }
                }
                obj = ancestor;
                System.Diagnostics.Debug.Assert(obj == null || obj.IsDescendantOf(this));
                if (obj == null)
                    break;
                continue; // Continue on the outside of the inner flow thread.
            }
            // We're inside something that's out-of-flow. Keep looking upwards and
            // backwards in the tree.
            obj = obj.PreviousInPreOrder(this);
        }
        if (obj == null || obj == this)
            return null;
        return obj;
    }

    private static LayoutObject? FirstLayoutObjectInSet(LayoutMultiColumnSet multicolSet)
    {
        var sibling = (AuroraBox?)multicolSet.PreviousSiblingMultiColumnBox();
        if (sibling == null)
            return multicolSet.FlowThread()?.FirstChild();
        // Adjacent column content sets should not occur. We would have no way of
        // figuring out what each of them contains then.
        System.Diagnostics.Debug.Assert(sibling.IsLayoutMultiColumnSpannerPlaceholder());
        var spanner = ((LayoutMultiColumnSpannerPlaceholder)sibling).LayoutObjectInFlowThread();
        if (spanner == null)
            return null;
        return NextInPreOrderAfterChildrenSkippingOutOfFlow(multicolSet.MultiColumnFlowThread(), spanner);
    }

    private static LayoutObject? LastLayoutObjectInSet(LayoutMultiColumnSet multicolSet)
    {
        var sibling = (AuroraBox?)multicolSet.NextSiblingMultiColumnBox();
        // By right we should return lastLeafChild() here, but the caller doesn't
        // care, so just return nullptr.
        if (sibling == null)
            return null;
        // Adjacent column content sets should not occur. We would have no way of
        // figuring out what each of them contains then.
        System.Diagnostics.Debug.Assert(sibling.IsLayoutMultiColumnSpannerPlaceholder());
        var spanner = ((LayoutMultiColumnSpannerPlaceholder)sibling).LayoutObjectInFlowThread();
        if (spanner == null)
            return null;
        return PreviousInPreOrderSkippingOutOfFlow(multicolSet.MultiColumnFlowThread(), spanner);
    }

    private static bool HasFlippedBlocksWritingMode() => false;

    private bool _allColumnsHaveKnownHeight;

    private static LayoutObject? PreviousInPreOrderSkippingOutOfFlow(LayoutMultiColumnFlowThread flowThread, LayoutObject descendant)
    {
        return flowThread.PreviousInPreOrderSkippingOutOfFlow(descendant);
    }

    private static LayoutObject? NextInPreOrderAfterChildrenSkippingOutOfFlow(LayoutMultiColumnFlowThread flowThread, LayoutObject descendant)
    {
        return flowThread.NextInPreOrderAfterChildrenSkippingOutOfFlow(descendant);
    }

    private static LayoutObject? FirstLayoutObjectInSet(LayoutMultiColumnFlowThread flowThread, LayoutMultiColumnSet multicolSet)
    {
        return FirstLayoutObjectInSet(multicolSet);
    }
}

// Extension methods for LayoutMultiColumnFlowThread
public static class LayoutMultiColumnFlowThreadExtensions
{
    public static LayoutMultiColumnFlowThread? MultiColumnFlowThread(this LayoutBlockFlow blockFlow)
    {
        foreach (var child in blockFlow.Children)
        {
            if (child is LayoutMultiColumnFlowThread flowThread)
                return flowThread;
        }
        return null;
    }

    public static AuroraBox? LastChildBox(this LayoutBlockFlow blockFlow)
    {
        if (blockFlow.Children.Count == 0) return null;
        return blockFlow.Children[^1] as AuroraBox;
    }

    public static AuroraBox? NextSiblingMultiColumnBox(this LayoutMultiColumnSet set)
    {
        return set.NextSibling as AuroraBox;
    }

    public static AuroraBox? PreviousSiblingMultiColumnBox(this LayoutMultiColumnSet set)
    {
        return set.PreviousSibling as AuroraBox;
    }

    public static float LogicalBottom(this LayoutMultiColumnSet set)
    {
        return set.LogicalBottomInFlowThread();
    }

    public static void SetShouldDoFullPaintInvalidation(this LayoutMultiColumnSet set) { }

    public static LayoutObject? NextInPreOrderAfterChildren(this LayoutObject obj, LayoutObject? stayWithin)
    {
        if (obj.NextSibling != null)
            return obj.NextSibling;
        LayoutObject? parent = obj.Parent;
        while (parent != null && parent != stayWithin)
        {
            if (parent.NextSibling != null)
                return parent.NextSibling;
            parent = parent.Parent;
        }
        return null;
    }

    public static LayoutObject? PreviousInPreOrder(this LayoutObject obj, LayoutObject? stayWithin)
    {
        if (obj.PreviousSibling != null)
        {
            LayoutObject? lastChild = obj.PreviousSibling;
            while (lastChild is LayoutBlock or LayoutBlockFlow)
            {
                if ((lastChild as LayoutBlock)?.Children.Count > 0)
                    lastChild = ((LayoutBlock)lastChild).Children[^1];
                else
                    break;
            }
            return lastChild;
        }
        return obj.Parent != stayWithin ? obj.Parent : null;
    }

    public static LayoutObject? NextInPreOrder(this LayoutObject obj, LayoutObject? stayWithin)
    {
        // If the object has children, return the first child.
        if (obj.Children.Count > 0)
            return obj.Children[0];
        // Otherwise, walk up to find the next sibling.
        return obj.NextInPreOrderAfterChildren(stayWithin);
    }
}