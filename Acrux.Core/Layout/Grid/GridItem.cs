using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using WritingMode = Acrux.Core.Layout.Geometry.WritingMode;

namespace Acrux.Core.Layout.Grid;

public enum AxisEdge
{
    Start,
    Center,
    End,
    FirstBaseline,
    LastBaseline,
}

public class GridItemData
{
    public BlockNode Node { get; set; }
    public GridArea ResolvedPosition { get; set; }

    public bool HasSubgriddedColumns { get; set; }
    public bool HasSubgriddedRows { get; set; }
    public bool IsConsideredForColumnSizing { get; set; }
    public bool IsConsideredForRowSizing { get; set; }
    public bool IsOppositeDirectionInRootGridColumns { get; set; }
    public bool IsOppositeDirectionInRootGridRows { get; set; }
    public bool IsOverflowSafeForColumns { get; set; }
    public bool IsOverflowSafeForRows { get; set; }
    public bool IsParallelWithRootGrid { get; set; }
    public bool IsSizingDependentOnBlockSize { get; set; }
    public bool IsSubgriddedToParentGrid { get; set; }
    public bool MustConsiderGridItemsForColumnSizing { get; set; }
    public bool MustConsiderGridItemsForRowSizing { get; set; }

    public FontBaseline ParentGridFontBaseline { get; set; }

    public AxisEdge ColumnAlignment { get; set; }
    public AxisEdge RowAlignment { get; set; }

    public AxisEdge? ColumnFallbackAlignment { get; set; }
    public AxisEdge? RowFallbackAlignment { get; set; }

    public AutoSizeBehavior ColumnAutoBehavior { get; set; }
    public AutoSizeBehavior RowAutoBehavior { get; set; }

    public BaselineGroup ColumnBaselineGroup { get; set; }
    public BaselineGroup RowBaselineGroup { get; set; }

    public WritingMode ColumnBaselineWritingMode { get; set; }
    public WritingMode RowBaselineWritingMode { get; set; }

    public TrackSpanProperties ColumnSpanProperties { get; set; }
    public TrackSpanProperties RowSpanProperties { get; set; }

    public GridItemIndices ColumnSetIndices { get; set; }
    public GridItemIndices RowSetIndices { get; set; }

    public GridItemIndices ColumnRangeIndices { get; set; }
    public GridItemIndices RowRangeIndices { get; set; }

    public OutOfFlowItemPlacement ColumnPlacement { get; set; }
    public OutOfFlowItemPlacement RowPlacement { get; set; }

    public AxisEdge Alignment(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns
            ? ColumnFallbackAlignment ?? ColumnAlignment
            : RowFallbackAlignment ?? RowAlignment;

    public bool IsOverflowSafe(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns
            ? ColumnFallbackAlignment.HasValue || IsOverflowSafeForColumns
            : RowFallbackAlignment.HasValue || IsOverflowSafeForRows;

    public bool IsBaselineAligned(GridTrackSizingDirection trackDirection)
    {
        var axisAlignment = Alignment(trackDirection);
        return axisAlignment == AxisEdge.FirstBaseline || axisAlignment == AxisEdge.LastBaseline;
    }

    public bool IsBaselineSpecified(GridTrackSizingDirection trackDirection)
    {
        var axisAlignment = trackDirection == GridTrackSizingDirection.kForColumns ? ColumnAlignment : RowAlignment;
        return axisAlignment == AxisEdge.FirstBaseline || axisAlignment == AxisEdge.LastBaseline;
    }

    public bool IsLastBaselineSpecified(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns
            ? ColumnAlignment == AxisEdge.LastBaseline
            : RowAlignment == AxisEdge.LastBaseline;

    public void ComputeSetIndices(GridLayoutTrackCollection trackCollection) { }

    public void ComputeOutOfFlowItemPlacement(
        GridLayoutTrackCollection trackCollection, GridPlacementData placementData, ComputedStyle gridStyle) { }

    public BaselineGroup GetBaselineGroup(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns ? ColumnBaselineGroup : RowBaselineGroup;

    public WritingDirectionMode BaselineWritingDirection(GridTrackSizingDirection trackDirection) =>
        new WritingDirectionMode(
            trackDirection == GridTrackSizingDirection.kForColumns ? ColumnBaselineWritingMode : RowBaselineWritingMode,
            Geometry.TextDirection.Ltr);

    public GridItemIndices SetIndices(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns ? ColumnSetIndices : RowSetIndices;

    public GridItemIndices RangeIndices(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns ? ColumnRangeIndices : RowRangeIndices;

    public void ResetPlacementIndices()
    {
        ColumnRangeIndices = RowRangeIndices = new GridItemIndices();
        ColumnSetIndices = RowSetIndices = new GridItemIndices();
    }

    public GridSpan Span(GridTrackSizingDirection trackDirection) => ResolvedPosition.Span(trackDirection);
    public int StartLine(GridTrackSizingDirection trackDirection) => ResolvedPosition.StartLine(trackDirection);
    public int EndLine(GridTrackSizingDirection trackDirection) => ResolvedPosition.EndLine(trackDirection);
    public int SpanSize(GridTrackSizingDirection trackDirection) => ResolvedPosition.SpanSize(trackDirection);

    public bool IsSubgrid => HasSubgriddedColumns || HasSubgriddedRows;

    public bool IsConsideredForSizing(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns ? IsConsideredForColumnSizing : IsConsideredForRowSizing;

    public bool IsOppositeDirectionInRootGrid(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns
            ? IsOppositeDirectionInRootGridColumns
            : IsOppositeDirectionInRootGridRows;

    public bool MustCachePlacementIndices(GridTrackSizingDirection trackDirection) =>
        !IsSubgriddedToParentGrid || IsConsideredForSizing(trackDirection) || MustConsiderGridItemsForSizing(trackDirection);

    public bool MustConsiderGridItemsForSizing(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns ? MustConsiderGridItemsForColumnSizing : MustConsiderGridItemsForRowSizing;

    public bool IsOutOfFlow => Node.IsOutOfFlowPositioned;

    public TrackSpanProperties GetTrackSpanProperties(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns ? ColumnSpanProperties : RowSpanProperties;

    public void SetTrackSpanProperty(TrackSpanProperties.PropertyId property, GridTrackSizingDirection trackDirection)
    {
        if (trackDirection == GridTrackSizingDirection.kForColumns)
            ColumnSpanProperties.SetProperty(property);
        else
            RowSpanProperties.SetProperty(property);
    }

    public bool IsSpanningFlexibleTrack(GridTrackSizingDirection trackDirection) =>
        GetTrackSpanProperties(trackDirection).HasProperty(TrackSpanProperties.PropertyId.kHasFlexibleTrack);
    public bool IsSpanningIntrinsicTrack(GridTrackSizingDirection trackDirection) =>
        GetTrackSpanProperties(trackDirection).HasProperty(TrackSpanProperties.PropertyId.kHasIntrinsicTrack);
    public bool IsSpanningAutoMinimumTrack(GridTrackSizingDirection trackDirection) =>
        GetTrackSpanProperties(trackDirection).HasProperty(TrackSpanProperties.PropertyId.kHasAutoMinimumTrack);
    public bool IsSpanningFixedMinimumTrack(GridTrackSizingDirection trackDirection) =>
        GetTrackSpanProperties(trackDirection).HasProperty(TrackSpanProperties.PropertyId.kHasFixedMinimumTrack);
    public bool IsSpanningFixedMaximumTrack(GridTrackSizingDirection trackDirection) =>
        GetTrackSpanProperties(trackDirection).HasProperty(TrackSpanProperties.PropertyId.kHasFixedMaximumTrack);
}

public class GridItems
{
    private readonly List<GridItemData> _itemData = new();
    private int _firstSubgriddedItemIndex;

    public bool IsEmpty => _itemData.Count == 0;
    public int Size => _itemData.Count;

    public void Append(GridItemData newItemData)
    {
        if (!newItemData.IsSubgriddedToParentGrid)
        {
            _firstSubgriddedItemIndex = _itemData.Count;
        }
        _itemData.Add(newItemData);
    }

    public void Append(GridItems other)
    {
        foreach (var item in other._itemData)
            Append(item);
    }

    public void SortByOrderProperty() { }

    public GridItemData At(int index) => _itemData[index];

    public IEnumerable<GridItemData> Items => _itemData.Take(_firstSubgriddedItemIndex > 0 ? _firstSubgriddedItemIndex : _itemData.Count);
    public IEnumerable<GridItemData> IncludeSubgriddedItems => _itemData;

    public void ReserveInitialCapacity(int capacity) { if (_itemData.Capacity < capacity) _itemData.Capacity = capacity; }
}