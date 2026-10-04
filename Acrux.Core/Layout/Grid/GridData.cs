using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Grid;

public struct GridItemIndices
{
    public int Begin { get; set; }
    public int End { get; set; }

    public GridItemIndices(int begin = -1, int end = -1)
    {
        Begin = begin;
        End = end;
    }
}

public struct OutOfFlowItemPlacement
{
    public GridItemIndices RangeIndex { get; set; }
    public GridItemIndices OffsetInRange { get; set; }
}

public struct GridPlacementData
{
    public GridLineResolver LineResolver { get; set; }
    public List<GridArea> GridItemPositions { get; set; }
    public int ColumnStartOffset { get; set; }
    public int RowStartOffset { get; set; }

    public GridPlacementData(GridLineResolver lineResolver)
    {
        LineResolver = lineResolver;
        GridItemPositions = new List<GridArea>();
        ColumnStartOffset = 0;
        RowStartOffset = 0;
    }

    public int AutoRepeatTrackCount(GridTrackSizingDirection trackDirection) =>
        LineResolver.AutoRepeatTrackCount(trackDirection);

    public int ExplicitGridTrackCount(GridTrackSizingDirection trackDirection) =>
        LineResolver.ExplicitGridTrackCount(trackDirection);

    public bool HasStandaloneAxis(GridTrackSizingDirection trackDirection) =>
        LineResolver.HasStandaloneAxis(trackDirection);

    public int StartOffset(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns ? ColumnStartOffset : RowStartOffset;

    public int SubgridSpanSize(GridTrackSizingDirection trackDirection) =>
        LineResolver.SubgridSpanSize(trackDirection);
}

public class GridLayoutData
{
    public GridLayoutTrackCollection? Columns { get; private set; }
    public GridLayoutTrackCollection? Rows { get; private set; }

    public bool HasSubgriddedAxis(GridTrackSizingDirection trackDirection) =>
        trackDirection == GridTrackSizingDirection.kForColumns
            ? !(Columns != null && Columns.IsForSizing())
            : !(Rows != null && Rows.IsForSizing());

    public bool IsSubgridWithStandaloneAxis(GridTrackSizingDirection trackDirection) =>
        Columns != null && Rows != null &&
        ((trackDirection == GridTrackSizingDirection.kForColumns)
            ? Columns.IsForSizing() && !Rows.IsForSizing()
            : Rows.IsForSizing() && !Columns.IsForSizing());

    public GridSizingTrackCollection SizingCollection(GridTrackSizingDirection trackDirection) =>
        (GridSizingTrackCollection)(trackDirection == GridTrackSizingDirection.kForColumns ? Columns! : Rows!);

    public GridLayoutTrackCollection OnlySubgriddedCollection =>
        Columns!.IsForSizing() ? Rows! : Columns!;

    public void SetTrackCollection(GridLayoutTrackCollection trackCollection)
    {
        if (trackCollection.Direction == GridTrackSizingDirection.kForColumns)
            Columns = trackCollection;
        else
            Rows = trackCollection;
    }
}

public class GridLayoutTreeNode
{
    public GridLayoutData LayoutData { get; }
    public int SubtreeSize { get; }
    public bool HasUnresolvedGeometry { get; }

    public GridLayoutTreeNode(GridLayoutData layoutData, int subtreeSize)
    {
        LayoutData = layoutData;
        SubtreeSize = subtreeSize;
        HasUnresolvedGeometry = layoutData.Columns!.HasIndefiniteSet() || layoutData.Rows!.HasIndefiniteSet();
    }
}

public class GridLayoutTree
{
    private readonly List<GridLayoutTreeNode> _treeData;

    public GridLayoutTree(List<GridLayoutTreeNode> treeData)
    {
        _treeData = treeData;
    }

    public bool AreSubtreesEqual(int subtreeRoot, GridLayoutTree other, int otherSubtreeRoot)
    {
        int subtreeSize = SubtreeSize(subtreeRoot);
        if (subtreeSize != other.SubtreeSize(otherSubtreeRoot))
            return false;
        for (int i = 0; i < subtreeSize; i++)
        {
            if (!Equals(other.LayoutData(otherSubtreeRoot + i), LayoutData(subtreeRoot + i)))
                return false;
        }
        return true;
    }

    public bool HasUnresolvedGeometry(int index) => _treeData[index].HasUnresolvedGeometry;
    public GridLayoutData LayoutData(int index) => _treeData[index].LayoutData;
    public int Size => _treeData.Count;
    public int SubtreeSize(int index) => _treeData[index].SubtreeSize;
}

public class GridLayoutSubtree
{
    private readonly GridLayoutTree? _gridTree;
    private readonly int _subtreeRoot;

    public GridLayoutSubtree() { _subtreeRoot = 0; }

    public GridLayoutSubtree(GridLayoutTree layoutTree, int subtreeRoot = 0)
    {
        _gridTree = layoutTree;
        _subtreeRoot = subtreeRoot;
    }

    public bool HasUnresolvedGeometry() => _gridTree!.HasUnresolvedGeometry(_subtreeRoot);
    public GridLayoutData LayoutData() => _gridTree!.LayoutData(_subtreeRoot);

    public override bool Equals(object? obj)
    {
        if (obj is not GridLayoutSubtree otherSubtree)
            return false;
        return _gridTree != null && otherSubtree._gridTree != null
            ? _gridTree.AreSubtreesEqual(_subtreeRoot, otherSubtree._gridTree, otherSubtree._subtreeRoot)
            : _gridTree == null && otherSubtree._gridTree == null;
    }

    public override int GetHashCode() => _gridTree?.GetHashCode() ?? 0;
}