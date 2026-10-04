namespace Acrux.Core.Layout.Grid;

public class LayoutGrid
{
    public GridPlacementData CachedPlacementData => new();
    public void SetSubgridMinMaxSizesCacheDirty(bool dirty) { }
    public bool ShouldInvalidateSubgridMinMaxSizesCacheFor(GridLayoutData layoutData) => false;
}