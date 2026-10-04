using System.Collections.Concurrent;
using SkiaSharp;
using Acrux.Core.Performance;
using Acrux.Core.Performance.Memory;

namespace Acrux.Core.Performance.Compositor;

/// <summary>
/// Asynchronous tile rasterizer. The <see cref="TryStartNext"/> method is called by
/// the producer loop (cooperative scheduler, worker thread, etc.) and dispatches the
/// next pending tile to a delegate that performs the actual drawing.
///
/// In this single-process browser we don't have a separate raster thread, so the
/// drawing delegate runs synchronously on the calling thread. The async semantics
/// remain: the manager owns the queue, the producer never blocks on drawing.
/// </summary>
public sealed class TileRasterizer
{
    public delegate SKImage RasterDelegate(TileKey key, SKRect pageRect, int sizePixels);

    private readonly TileManager _tiles;
    private readonly ConcurrentQueue<TileKey> _pending = new();
    private readonly ConcurrentDictionary<TileKey, byte> _inFlight = new();
    private readonly RasterDelegate? _raster;
    private long _queued;
    private long _started;
    private long _completed;
    private long _cancelled;

    public long Queued => Interlocked.Read(ref _queued);
    public long Started => Interlocked.Read(ref _started);
    public long Completed => Interlocked.Read(ref _completed);
    public long Cancelled => Interlocked.Read(ref _cancelled);
    public int Pending => _pending.Count;
    public int InFlight => _inFlight.Count;

    public TileRasterizer(TileManager tiles, RasterDelegate? raster = null)
    {
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _raster = raster;
    }

    public void Enqueue(TileKey key)
    {
        if (_inFlight.ContainsKey(key)) return;
        if (_pending.Contains(key)) return;
        _pending.Enqueue(key);
        Interlocked.Increment(ref _queued);
    }

    public void EnqueueRange(IEnumerable<TileKey> keys)
    {
        foreach (var k in keys) Enqueue(k);
    }

    /// <summary>
    /// Cancel in-flight or pending raster work for tiles that are no longer needed
    /// (e.g. scrolled out of view). Tiles that have already been drawn are unaffected.
    /// </summary>
    public int CancelOutOfBounds(SKRect activeRegion)
    {
        int cancelled = 0;
        // Drain queue and re-enqueue those still in bounds.
        var keep = new List<TileKey>();
        while (_pending.TryDequeue(out var k))
        {
            if (_tiles.TryGet(k, out var t) && t is not null && Intersects(t.PageRectPx, activeRegion))
                keep.Add(k);
            else
                cancelled++;
        }
        foreach (var k in keep) _pending.Enqueue(k);
        Interlocked.Add(ref _cancelled, cancelled);
        return cancelled;
    }

    /// <summary>
    /// Try to start the next pending tile. Returns the tile key that is now in
    /// flight, or null when there is nothing to do. The caller is responsible for
    /// invoking <see cref="Complete"/> after the rasterization finishes.
    /// </summary>
    public TileKey? TryStartNext()
    {
        if (_pending.TryDequeue(out var key))
        {
            _inFlight[key] = 0;
            // Mark the tile as being rasterized so the TileManager eviction /
            // memory-budget passes (which skip Rasterizing tiles) do not free or
            // evict it while it is in flight. This is the only place the state is
            // entered; it is cleared by Complete -> MarkReady/MarkFailed. Mirrors
            // the reference behaviour of never evicting a tile with active work.
            var tile = _tiles.GetOrCreate(key);
            tile.State = TileState.Rasterizing;
            Interlocked.Increment(ref _started);
            return key;
        }
        return null;
    }

    /// <summary>Complete an in-flight tile. Records success or failure on the tile manager.</summary>
    public bool Complete(TileKey key, SKImage? image, string? failureReason = null)
    {
        if (!_inFlight.TryRemove(key, out _)) return false;
        if (image is not null)
        {
            _tiles.MarkReady(key, image);
            Interlocked.Increment(ref _completed);
        }
        else
        {
            _tiles.MarkFailed(key, failureReason ?? "rasterizer returned null");
        }
        return true;
    }

    /// <summary>
    /// Convenience: pull a tile, rasterize it synchronously through the configured
    /// delegate, and mark it complete. Returns the in-flight key when work was done.
    /// </summary>
    public TileKey? Pump()
    {
        if (_raster is null) return null;
        var key = TryStartNext();
        if (key is null) return null;
        if (!_tiles.TryGet(key.Value, out var tile) || tile is null)
        {
            Complete(key.Value, null, "tile missing");
            return key;
        }
        var sw = Clock.NowNanos();
        try
        {
            var image = _raster(key.Value, tile.PageRectPx, tile.SizePixels);
            Complete(key.Value, image);
        }
        catch (Exception ex)
        {
            Complete(key.Value, null, ex.Message);
        }
        PipelineTimings.TileRaster.AddSample(Clock.NowNanos() - sw);
        return key;
    }

    public int PumpAll(int maxTiles)
    {
        int n = 0;
        while (n < maxTiles && Pump() is not null) n++;
        return n;
    }

    public void Clear()
    {
        // Drop any still-in-flight work and release the Rasterizing marker on
        // any tiles still held by the manager, so a subsequent InvalidateAll /
        // Clear does not leave them permanently protected from eviction or
        // resurrect stale tiles from a previously cleared generation.
        foreach (var key in _inFlight.Keys)
        {
            if (_tiles.TryGet(key, out var t) && t is not null && t.State == TileState.Rasterizing)
                t.State = TileState.Dirty;
        }
        _pending.Clear();
        _inFlight.Clear();
    }

    private static bool Intersects(SKRect a, SKRect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
}

/// <summary>
/// Predictive tile scheduler. Given a current viewport and a scroll velocity vector,
/// computes the set of tile keys that should be pre-rasterized to keep up with the
/// scroll without checkerboarding.
/// </summary>
public sealed class PredictiveTileScheduler
{
    public struct ScrollVelocity
    {
        public float Vx;
        public float Vy;
        public DateTime SampledAt;
    }

    private readonly TileManager _tiles;
    private readonly TileRasterizer _raster;
    private ScrollVelocity _velocity;
    private DateTime _lastUpdate;
    private long _predictions;
    private long _predictionsCancelled;

    public long Predictions => Interlocked.Read(ref _predictions);
    public long PredictionsCancelled => Interlocked.Read(ref _predictionsCancelled);
    public ScrollVelocity Velocity => _velocity;

    public PredictiveTileScheduler(TileManager tiles, TileRasterizer raster)
    {
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _raster = raster ?? throw new ArgumentNullException(nameof(raster));
    }

    public void UpdateVelocity(float vx, float vy)
    {
        _velocity = new ScrollVelocity { Vx = vx, Vy = vy, SampledAt = DateTime.UtcNow };
        _lastUpdate = DateTime.UtcNow;
    }

    /// <summary>
    /// Drop all pending/in-flight predictive raster work and reset the velocity
    /// sample. Used when the underlying tile cache is invalidated so stale keys
    /// from a previous content generation are not rasterized back in.
    /// </summary>
    public void ClearPending()
    {
        _raster.Clear();
        _velocity = default;
    }

    /// <summary>
    /// Enqueue tiles for pre-rasterization in the direction of travel. The "lookahead"
    /// is in pixels and scaled by the current speed (faster scroll → more lookahead).
    /// </summary>
    public int SchedulePreRasters(SKRect currentViewport, int layerId, double lookaheadFactor = 1.0)
    {
        var speed = MathF.Sqrt(_velocity.Vx * _velocity.Vx + _velocity.Vy * _velocity.Vy);
        if (speed < 1f) return 0;

        // Predict where the viewport will be 200 ms in the future (or factor-adjusted).
        var dtMillis = 200.0 * lookaheadFactor;
        var dx = _velocity.Vx * (float)(dtMillis / 1000.0);
        var dy = _velocity.Vy * (float)(dtMillis / 1000.0);

        var predicted = new SKRect(
            currentViewport.Left + Math.Min(0, dx),
            currentViewport.Top + Math.Min(0, dy),
            currentViewport.Right + Math.Max(0, dx),
            currentViewport.Bottom + Math.Max(0, dy));

        // Expand horizontally to handle the entire width regardless of horizontal speed.
        predicted = new SKRect(
            currentViewport.Left,
            Math.Min(currentViewport.Top, predicted.Top),
            currentViewport.Right,
            Math.Max(currentViewport.Bottom, predicted.Bottom));

        int scheduled = 0;
        foreach (var key in _tiles.TilesForRect(predicted, layerId))
        {
            if (_tiles.TryGet(key, out var t) && t is not null && t.State == TileState.Ready) continue;
            _raster.Enqueue(key);
            scheduled++;
        }
        Interlocked.Add(ref _predictions, scheduled);
        return scheduled;
    }
}
