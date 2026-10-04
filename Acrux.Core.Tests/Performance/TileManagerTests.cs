using SkiaSharp;
using Acrux.Core.Performance.Compositor;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class TileManagerTests
{
    [Fact]
    public void TilesForRect_GeneratesExpectedGrid()
    {
        var m = new TileManager(new TileManager.Config { TileSizePixels = 100 });
        var rect = new SKRect(0, 0, 250, 250);
        var keys = m.TilesForRect(rect, 1).ToList();
        // 3x3 = 9 tiles for 0..250 at 100px tile size
        Assert.Equal(9, keys.Count);
        Assert.Contains(new TileKey(0, 0, 1), keys);
        Assert.Contains(new TileKey(2, 2, 1), keys);
    }

    [Fact]
    public void GetOrCreate_ReusesSameTile()
    {
        var m = new TileManager();
        var a = m.GetOrCreate(new TileKey(0, 0, 1));
        var b = m.GetOrCreate(new TileKey(0, 0, 1));
        Assert.Same(a, b);
    }

    [Fact]
    public void MarkDirty_ReleasesReadyImage()
    {
        var m = new TileManager();
        var key = new TileKey(0, 0, 1);
        var tile = m.GetOrCreate(key);
        tile.State = TileState.Ready;
        // Use a real SKImage is complex; just verify state transition
        m.MarkDirty(new[] { key });
        Assert.Equal(TileState.Dirty, tile.State);
    }

    [Fact]
    public void EvictOutOfBounds_RemovesFarTiles()
    {
        var m = new TileManager();
        var near = m.GetOrCreate(new TileKey(0, 0, 1));
        var far = m.GetOrCreate(new TileKey(1000, 1000, 1));
        var active = new SKRect(0, 0, 1000, 1000);
        int n = m.EvictOutOfBounds(active);
        Assert.True(n >= 1);
        Assert.Single(m.Snapshot());
    }

    [Fact]
    public void EnforceMemoryBudget_EvictsLRU()
    {
        var m = new TileManager(new TileManager.Config { MaxTilesInMemory = 3 });
        for (int i = 0; i < 10; i++) m.GetOrCreate(new TileKey(i, 0, 1));
        // Touch the most recent so they are "recent"
        m.GetOrCreate(new TileKey(9, 0, 1)).Touch();
        m.EnforceMemoryBudget();
        Assert.True(m.ActiveCount <= 3);
    }

    [Fact]
    public void TileKey_Equality()
    {
        var a = new TileKey(1, 2, 3);
        var b = new TileKey(1, 2, 3);
        var c = new TileKey(1, 2, 4);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
