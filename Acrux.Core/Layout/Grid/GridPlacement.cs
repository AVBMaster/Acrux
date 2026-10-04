using Acrux.Core.Dom;

namespace Acrux.Core.Layout.Grid;

public class GridPlacement
{
    public enum PackingBehavior { Sparse, Dense }

    private GridPlacementData _placementData;
    private PackingBehavior _packingBehavior;
    private GridTrackSizingDirection _majorDirection;
    private GridTrackSizingDirection _minorDirection;
    private int _minorMaxEndLine;

    public GridPlacement(ComputedStyle gridStyle, GridLineResolver lineResolver)
    {
        _placementData = new GridPlacementData(lineResolver);
        _packingBehavior = PackingBehavior.Sparse;
        _majorDirection = GridTrackSizingDirection.kForRows;
        _minorDirection = GridTrackSizingDirection.kForColumns;
    }

    public GridPlacementData RunAutoPlacementAlgorithm(GridItems gridItems) => _placementData;

    public static void ResolveOutOfFlowItemGridLines(
        GridLayoutTrackCollection trackCollection, GridLineResolver lineResolver,
        ComputedStyle gridStyle, ComputedStyle itemStyle,
        int startOffset, out int startLine, out int endLine)
    {
        startLine = 0;
        endLine = 0;
    }
}