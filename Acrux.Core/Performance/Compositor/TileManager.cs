using System.Collections.Concurrent;
using SkiaSharp;
using Acrux.Core.Performance;

namespace Acrux.Core.Performance.Compositor;

/// <summary>
/// Coordinate of a tile in the page-level tiling grid. The grid is anchored at the
/// page origin; coordinates are in tile units (not pixels).
/// </summary>
public readonly struct TileKey : IEquatable<TileKey>
{
    public readonly int X;
    public readonly int Y;
    public readonly int LayerId;

    public TileKey(int x, int y, int layerId)
    {
        X = x;
        Y = y;
        LayerId = layerId;
    }

    public bool Equals(TileKey other) => X == other.X && Y == other.Y && LayerId == other.LayerId;
    public override bool Equals(object? obj) => obj is TileKey k && Equals(k);
    public override int GetHashCode() => HashCode.Combine(X, Y, LayerId);
    public override string ToString() => $"tile(l={LayerId},x={X},y={Y})";
}

/// <summary>
/// Lifecycle phases of a tile. <see cref="Rasterizing"/> tiles are owned by the
/// rasterizer pool; only tiles in <see cref="Ready"/> state are valid for compositing.
/// </summary>
public enum TileState : byte
{
    Empty = 0,
    Dirty = 1,
    Rasterizing = 2,
    Ready = 3,
    Evicted = 4,
    Failed = 5,
}

/// <summary>
/// A rasterised page tile. Owns the SKImage texture. The texture is released when
/// the tile moves out of the active set; the producer (rasterizer) re-creates it on
/// demand.
/// </summary>
public sealed class Tile
{
    public required TileKey Key { get; init; }
    public int SizePixels { get; init; }
    public SKImage? Image { get; set; }
    public TileState State { get; set; } = TileState.Empty;
    public SKRect PageRectPx { get; set; }
    public long LastTouchedNanos { get; set; }
    public long RasterizedAtNanos { get; set; }
    public int RasterAttempts { get; set; }
    public string? FailureReason { get; set; }

    public void Touch() => LastTouchedNanos = Clock.NowNanos();
    public long AgeNanos() => Clock.NowNanos() - LastTouchedNanos;
    public bool HasImage => Image is not null;
    public void ReleaseImage()
    {
        if (Image is not null) { Image.Dispose(); Image = null; }
        State = TileState.Evicted;
    }
}

/// <summary>
/// Tiling strategy: a fixed grid of square tiles, defaulting to 256×256 device pixels.
/// Large pages are sliced into rows×cols tiles per layer; tiles outside the active
/// viewport are evicted to bound GPU memory.
/// </summary>
public sealed class TileManager
{
    public sealed class Config
    {
        public int TileSizePixels { get; init; } = 256;
        public int MaxTilesInMemory { get; init; } = 512;
        public int MaxConcurrentRasterizations { get; init; } = 4;
        public float OverscanFactor { get; init; } = 1.5f;
    }

    private readonly Config _config;
    private readonly ConcurrentDictionary<TileKey, Tile> _tiles = new();
    private long _allocated;
    private long _evicted;
    private long _rasterized;
    private long _failed;
    private long _hits;
    private long _misses;

    public Config Settings => _config;
    public long Allocated => Interlocked.Read(ref _allocated);
    public long Evicted => Interlocked.Read(ref _evicted);
    public long Rasterized => Interlocked.Read(ref _rasterized);
    public long Failed => Interlocked.Read(ref _failed);
    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public int ActiveCount => _tiles.Count;

    public TileManager(Config? config = null)
    {
        _config = config ?? new Config();
    }

    /// <summary>Convert a page-space pixel rectangle to a set of tile keys that cover it.</summary>
    public IEnumerable<TileKey> TilesForRect(SKRect pageRect, int layerId)
    {
        int size = _config.TileSizePixels;
        int x0 = (int)Math.Floor(pageRect.Left / size);
        int y0 = (int)Math.Floor(pageRect.Top / size);
        int x1 = (int)Math.Floor((pageRect.Right - 1) / size);
        int y1 = (int)Math.Floor((pageRect.Bottom - 1) / size);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return new TileKey(x, y, layerId);
    }

    public Tile GetOrCreate(TileKey key)
    {
        return _tiles.GetOrAdd(key, k => new Tile
        {
            Key = k,
            SizePixels = _config.TileSizePixels,
            PageRectPx = new SKRect(
                k.X * _config.TileSizePixels,
                k.Y * _config.TileSizePixels,
                (k.X + 1) * _config.TileSizePixels,
                (k.Y + 1) * _config.TileSizePixels),
        });
    }

    public bool TryGet(TileKey key, out Tile? tile)
    {
        if (_tiles.TryGetValue(key, out var t))
        {
            Interlocked.Increment(ref _hits);
            t.Touch();
            tile = t;
            return true;
        }
        Interlocked.Increment(ref _misses);
        tile = null;
        return false;
    }

    public void MarkDirty(IEnumerable<TileKey> keys)
    {
        foreach (var k in keys)
        {
            var t = GetOrCreate(k);
            if (t.State == TileState.Ready)
            {
                t.ReleaseImage();
            }
            t.State = TileState.Dirty;
        }
    }

    public void MarkReady(TileKey key, SKImage image)
    {
        var tile = GetOrCreate(key);
        if (tile.Image is not null) tile.Image.Dispose();
        tile.Image = image;
        tile.State = TileState.Ready;
        tile.RasterizedAtNanos = Clock.NowNanos();
        tile.RasterAttempts++;
        Interlocked.Increment(ref _rasterized);
        Interlocked.Increment(ref _allocated);
    }

    public void MarkFailed(TileKey key, string reason)
    {
        var tile = GetOrCreate(key);
        tile.State = TileState.Failed;
        tile.FailureReason = reason;
        Interlocked.Increment(ref _failed);
    }

    /// <summary>Evict tiles that are outside the active region to bound memory use.</summary>
    public int EvictOutOfBounds(SKRect activeRegion)
    {
        int evicted = 0;
        foreach (var kv in _tiles.ToArray())
        {
            var t = kv.Value;
            if (t.State == TileState.Rasterizing) continue;
            if (!Intersects(t.PageRectPx, activeRegion))
            {
                if (_tiles.TryRemove(kv.Key, out var removed))
                {
                    removed.ReleaseImage();
                    evicted++;
                    Interlocked.Increment(ref _evicted);
                    Interlocked.Decrement(ref _allocated);
                }
            }
        }
        EnforceMemoryBudget();
        return evicted;
    }

    public int EnforceMemoryBudget()
    {
        int evicted = 0;
        // TODO(audit): this budget is enforced on tile count only, not on actual
        // bytes of the backing SKImage. The reference tracks a byte-based memory
        // usage (MemoryUsage::memory_bytes) and frees tiles until the byte limit
        // (and resource count) is satisfied. Tiles here are uniform in size
        // (SizePixels from config), so count == bytes for steady state; this is an
        // accepted adaptation, but a per-tile byte budget should be added if
        // non-uniform tile sizes are ever introduced.
        var cap = _config.MaxTilesInMemory;
        while (_tiles.Count > cap)
        {
            // Evict the least-recently-touched tile that is not currently rasterizing.
            Tile? lru = null;
            long oldest = long.MaxValue;
            foreach (var t in _tiles.Values)
            {
                if (t.State == TileState.Rasterizing) continue;
                if (t.LastTouchedNanos < oldest) { oldest = t.LastTouchedNanos; lru = t; }
            }
            if (lru is null) break;
            if (_tiles.TryRemove(lru.Key, out var removed))
            {
                removed.ReleaseImage();
                evicted++;
                Interlocked.Increment(ref _evicted);
                Interlocked.Decrement(ref _allocated);
            }
        }
        return evicted;
    }

    public void Clear()
    {
        foreach (var t in _tiles.Values) t.ReleaseImage();
        _tiles.Clear();
        Interlocked.Exchange(ref _allocated, 0);
    }

    public IEnumerable<Tile> Snapshot() => _tiles.Values.ToArray();

    private static bool Intersects(SKRect a, SKRect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
}
