namespace Acrux.Core.Layout.Grid;

public class GridBreakTokenData : BlockBreakTokenData
{
    public GridBreakTokenData(BlockBreakTokenData? breakTokenData = null)
        : base(BreakTokenDataType.kGridBreakTokenData, breakTokenData)
    {
    }
}