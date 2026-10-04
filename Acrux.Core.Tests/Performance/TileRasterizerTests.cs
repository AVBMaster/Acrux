using SkiaSharp;
using Acrux.Core.Performance.Compositor;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class TileRasterizerTests
{
    [Fact]
    public void Enqueue_TryStart_Complete_FullCycle()
    {
        var tm = new TileManager();
        var ras = new TileRasterizer(tm);
        var key = new TileKey(0, 0, 1);
        ras.Enqueue(key);
        Assert.Equal(1, ras.Pending);

        var started = ras.TryStartNext();
        Assert.Equal(key, started);
        Assert.Equal(0, ras.Pending);
        Assert.Equal(1, ras.InFlight);

        // No raster delegate set; mark with null image (failure) for the test
        Assert.True(ras.Complete(key, null, "no raster"));
        Assert.Equal(0, ras.InFlight);
    }

    [Fact]
    public void DuplicateEnqueue_IsAvoided()
    {
        var tm = new TileManager();
        var ras = new TileRasterizer(tm);
        var key = new TileKey(1, 1, 1);
        ras.Enqueue(key);
        ras.Enqueue(key);
        Assert.Equal(1, ras.Pending);
    }

    [Fact]
    public void PumpAll_DrainsQueue()
    {
        var tm = new TileManager();
        var ras = new TileRasterizer(tm, (k, r, s) => null); // returns null, marks failed
        for (int i = 0; i < 5; i++) ras.Enqueue(new TileKey(i, 0, 1));
        int pumped = ras.PumpAll(10);
        Assert.Equal(5, pumped);
        Assert.Equal(0, ras.Pending);
    }

    [Fact]
    public void CancelOutOfBounds_DropsFarTiles()
    {
        var tm = new TileManager();
        var ras = new TileRasterizer(tm);
        var near = new TileKey(0, 0, 1);
        var far = new TileKey(100, 100, 1);
        tm.GetOrCreate(near);
        tm.GetOrCreate(far);
        ras.Enqueue(near);
        ras.Enqueue(far);
        int cancelled = ras.CancelOutOfBounds(new SKRect(0, 0, 256, 256));
        Assert.True(cancelled >= 1);
        Assert.Equal(1, ras.Pending);
    }
}
