namespace Acrux.Core.Layout.Grid;

public class GridLineResolver
{
    public int AutoRepeatTrackCount(GridTrackSizingDirection trackDirection) => 0;
    public int ExplicitGridTrackCount(GridTrackSizingDirection trackDirection) => 0;
    public bool HasStandaloneAxis(GridTrackSizingDirection trackDirection) => false;
    public int SubgridSpanSize(GridTrackSizingDirection trackDirection) => 0;
}

public enum GridTrackSizingDirection
{
    kForColumns,
    kForRows,
}

public enum AutoSizeBehavior
{
    kAutoSize,
    kContain,
}

public struct GridSpan
{
    public int StartLine { get; }
    public int EndLine { get; }
    public int SpanSize => EndLine - StartLine;

    public GridSpan(int startLine, int endLine)
    {
        StartLine = startLine;
        EndLine = endLine;
    }
}

public struct GridArea
{
    public GridSpan Columns { get; }
    public GridSpan Rows { get; }

    public GridArea(GridSpan columns, GridSpan rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public GridSpan Span(GridTrackSizingDirection direction) =>
        direction == GridTrackSizingDirection.kForColumns ? Columns : Rows;

    public int StartLine(GridTrackSizingDirection direction) => Span(direction).StartLine;
    public int EndLine(GridTrackSizingDirection direction) => Span(direction).EndLine;
    public int SpanSize(GridTrackSizingDirection direction) => Span(direction).SpanSize;
}

public class TrackSpanProperties
{
    public enum PropertyId
    {
        kHasFlexibleTrack,
        kHasIntrinsicTrack,
        kHasAutoMinimumTrack,
        kHasFixedMinimumTrack,
        kHasFixedMaximumTrack,
    }

    private int _properties;

    public void SetProperty(PropertyId property) => _properties |= (1 << (int)property);
    public bool HasProperty(PropertyId property) => (_properties & (1 << (int)property)) != 0;
}

public class GridLayoutTrackCollection
{
    public GridTrackSizingDirection Direction { get; }
    public virtual bool IsForSizing() => false;
    public bool HasIndefiniteSet() => false;

    public GridLayoutTrackCollection(GridTrackSizingDirection direction)
    {
        Direction = direction;
    }
}

public class GridSizingTrackCollection : GridLayoutTrackCollection
{
    public override bool IsForSizing() => true;

    public GridSizingTrackCollection(GridTrackSizingDirection direction) : base(direction) { }
}