namespace Acrux.Core.Layout.Grid;

public class GridTrackCollection
{
    public class Set
    {
        public int TrackCount { get; set; }
        public int TrackSize { get; set; }
    }

    public List<Set> Sets { get; } = new();
}

public class GridSizingSubtree
{
}

public class GridSubtree
{
}