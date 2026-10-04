using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SkiaSharp;
using Acrux.Core.Performance;
using Acrux.Core.Performance.Compositor;
using Acrux.Core.Performance.Memory;
using Acrux.Core.Performance.Diagnostics;

namespace Acrux.Rendering;

/// <summary>
/// Browser tile compositor. Owns all coordinate transforms internally so the
/// caller only needs to provide the physical (device-pixel) viewport rectangle
/// and the combined DPR×resolution scale factor.
///
/// Key design decisions:
///   * <c>Render</c> saves, resets, and clips the canvas — it takes full control
///     of the transform stack.
///   * All tile positions are expressed in <em>physical device pixels</em>
///     (<c>tx * _tileSize * physicalScale</c>). The caller's canvas matrix is
///     irrelevant.
///   * Rasterization is budgeted: visible tiles are rasterized inline so the
///     viewport is always complete, while overscan/predictive preraster tiles
///     are capped per frame (in production they are deferred and drained under
///     a time budget via <see cref="RasterDeferred"/>).
///   * Stale-tile detection uses a <c>_displayListVersion</c> counter: when the
///     display list changes, all cached tiles are discarded on the next render.
///   * An LRU eviction pass runs periodically so the cache does not grow
///     unbounded.
/// </summary>
public sealed class TiledCompositor : IDisposable
{
    /// <summary>Tile priority. Lower numeric value = higher priority.</summary>
    public enum TilePriority : byte
    {
        Visible = 0,
        Overscan = 1,
        Predictive = 2,
        Distant = 3,
    }

    private sealed class Tile : IDisposable
    {
        public int X;
        public int Y;
        public SKImage? Image;
        public SKRect PageRect;
        public ulong Version;
        public ulong DisplayListVersion;
        public long LastAccessTick;
        public long EstimatedBytes;
        public TilePriority Priority;
        public CompositorDisplayList? Recorded;
        public bool Disposed;

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Image?.Dispose();
            Image = null;
            Recorded?.Reset();
            Recorded = null;
        }
    }

    public const int DefaultTileSize = 512;
    public const int MaxTiles = 1024;
    public const long DefaultMaxBytes = 96L * 1024 * 1024;
    public const int MaxBackgroundTilesPerFrame = 16;   // non-visible tiles per frame
    /// <summary>Max physical-pixel distance the predictive band may look ahead.</summary>
    public const float MaxLookaheadPx = 4096f;         // ~8 tiles at the default 512px size
    private const long CleanupIntervalMs = 5000;         // evict stale tiles every 5 s
    private const long StaleTileAgeMs = 30000;           // tile unused for 30 s is stale
    /// <summary>Highest texture dimension a tile may reach (max texture size in the reference).</summary>
    private const int MaxTextureSize = 4096;
    /// <summary>Round odd tile widths/heights up to increase re-use of the same size (kTileRoundUp).</summary>
    private const int TileRoundUp = 64;
    /// <summary>Tile width/height must be an even multiple of this for compositing (kTileMinimalAlignment).</summary>
    private const int TileMinimalAlignment = 4;

    /// <summary>
    /// Sampling used when blitting a rasterised tile (or a cached scroll layer)
    /// back onto the surface. Tiles are rasterised at
    /// <c>pageSize * physicalScale</c> device pixels and drawn 1:1, so the blit
    /// is a pure texel copy and Nearest is exact.
    ///
    /// Never switch this to Linear: bilinear would re-resample an already
    /// rasterised raster, smearing every glyph and thin rule by up to half a
    /// device pixel — the single biggest source of "the page looks soft". With
    /// Nearest a fractional scroll offset simply shifts the texel grid, so text
    /// stays hard-edged both at rest and mid-scroll, with no origin snapping and
    /// no visible jump when the scroll settles.
    /// </summary>
    private static readonly SKSamplingOptions TileSampling = new(SKFilterMode.Nearest);

    /// <summary>Content smaller than this in both dimensions is rasterized as a single tile.</summary>
    private const int MaxUntiledLayerWidth = 1024;
    private const int MaxUntiledLayerHeight = 768;

    private readonly int _tileSize;
    private readonly bool _adaptiveTileSize;
    /// <summary>Effective tile grid dimensions in page-space logical pixels; may differ per axis.</summary>
    private int _tileW;
    private int _tileH;
    private readonly int _overscanRings;
    private readonly bool _recordCommands;
    private DisplayList _displayList;
    private readonly CompositorReplayer _replayer = new();
    private ulong _displayListVersion;
    private ulong _lastRenderedDisplayListVersion;
    private ulong _nextTileVersion;

    private readonly Dictionary<long, Tile> _tiles = new();
    private readonly LinkedList<long> _lru = new();
    private long _currentBytes;
    private long _bytesHighWater;
    private int _drawCalls;
    private int _tilesReused;
    private int _tilesRasterized;
    private int _backgroundTilesCreatedThisFrame;
    private int _tilesSkippedThisFrame;
    private int _tilesEvictedLru;
    private int _tilesEvictedBudget;
    private int _overscanTilesRasterized;
    private int _predictiveTilesRasterized;
    private int _visibleTilesRasterized;
    private long _rasterNanos;
    private long _recordingsProduced;

    private float _lastPhysicalScale = 1f;
    private float _lastOriginX;
    private float _lastOriginY;
    private SKRect _predictedViewport;
    /// <summary>Physical (device-pixel) viewport of the last <see cref="Render"/>.</summary>
    private SKRect _lastPhysicalViewport;
    /// <summary>
    /// Union (op space) of the rects invalidated since the last render. Lets
    /// <see cref="EnsurePicture"/> cull the recorded picture to just what will
    /// be re-rasterized this frame instead of replaying the whole document.
    /// </summary>
    private SKRect _pendingInvalidateUnion;
    private long _lastCleanupTick;

    // Recorded picture used for per-tile raster. The reference model replays a
    // single recorded picture for every tile (clip + transform + DrawPicture)
    // instead of re-executing layout-level paint ops per tile; recording runs
    // once per display-list change.
    private SKPicture? _recordedPicture;
    private ulong _recordedPictureVersion;
    private DisplayList? _recordedPictureSource;
    private SKRect _pictureBounds;
    /// <summary>
    /// Union of the change-only regions declared via <see cref="SetChangeRegion"/>
    /// since the last render. When non-empty the recorded picture culls to exactly
    /// this region (+ one tile) so element-scroll repaints record and replay
    /// ~O(container) instead of ~O(viewport).
    /// </summary>
    private SKRect _pendingChangeRegion;
    /// <summary>Content cull the current recorded picture was produced from.</summary>
    private SKRect _recordedCullRect = SKRect.Empty;
    /// <summary>True when the current recorded picture contains only the change region.</summary>
    private bool _pictureIsChangeOnly;
    /// <summary>Incremented when an element scroll offset changed. Forces the
    /// recorded picture to be re-baked (DrawScrollLayerOp reads the live offset)
    /// without bumping the cache generation.</summary>
    private ulong _scrollBakeTick;
    private ulong _recordedScrollBakeTick;

    private readonly TileManager? _hubTiles;
    private readonly PredictiveTileScheduler? _hubPredictor;
    private readonly MemoryBudget? _memoryBudget;

    /// <summary>
    /// True when <see cref="InvalidateRect"/> was issued since the last display
    /// list swap. Lets <see cref="Render"/> adopt a rebuilt display list without
    /// dropping the surviving (unchanged) tiles.
    /// </summary>
    private bool _regionInvalidatedSinceLastSwap;

    /// <summary>
    /// True when production rendering defers per-tile rasterization: cache misses
    /// are queued by <see cref="Render"/> and drained by <see cref="RasterDeferred"/>
    /// under a per-frame time budget instead of rasterizing synchronously inline.
    /// Kept false for the PerfSmokeTest path, which relies on synchronous raster.
    /// </summary>
    public bool DeferRasterization { get; set; }

    /// <summary>Per-frame raster budget while the page is idle (no scroll velocity).</summary>
    public long IdleRasterBudgetNanos { get; set; } = 20_000_000;

    /// <summary>Per-frame raster budget while the page is scrolling (keeps motion responsive).</summary>
    public long ScrollingRasterBudgetNanos { get; set; } = 5_000_000;

/// <summary>
    /// Hard cap on tiles synchronously re-rasterized per element-scroll frame.
    /// The app flushes only the scrolled container's on-screen slice, which at
    /// the default 512px tile size is far below this for any real-world layout;
    /// the cap merely guards against a pathological viewport-sized container.
    /// </summary>
    public const int MaxSynchronousRegionTiles = 32;

    private readonly record struct DeferredTile(int Tx, int Ty, float PhysicalScale, TilePriority Priority);
    private readonly List<DeferredTile> _deferred = new();
    private readonly HashSet<long> _deferredKeys = new();

    /// <summary>
    /// A deferred tile that survived the main-thread cache/bounds checks and is
    /// ready to rasterize. Everything a worker needs is precomputed here so the
    /// parallel phase touches only immutable inputs.
    /// </summary>
    private readonly record struct PreparedTile(long Key, int Tx, int Ty, TilePriority Priority,
        float PhysicalScale, SKRect PageRect, int PixelW, int PixelH);

    // Workers per raster batch. Tile raster is pure CPU (private SKBitmap +
    // SKPicture replay), so extra cores translate almost linearly into tiles
    // per frame. Capped at 4: beyond that the batch wall time is dominated by
    // the slowest tile and memory churn grows.
    private static readonly int RasterParallelism = Math.Clamp(Environment.ProcessorCount - 1, 2, 4);

    /// <summary>True when tiles are queued for background rasterization.</summary>
    public bool HasPendingRasterWork => _deferred.Count > 0;

    public int TileSize => _tileSize;
    public int DrawCalls => _drawCalls;
    public int TilesReused => _tilesReused;
    public int TilesRasterized => _tilesRasterized;
    public int TilesSkippedThisFrame => _tilesSkippedThisFrame;
    public int TilesEvictedLru => _tilesEvictedLru;
    public int TilesEvictedBudget => _tilesEvictedBudget;
    public int VisibleTilesRasterized => _visibleTilesRasterized;
    public int OverscanTilesRasterized => _overscanTilesRasterized;
    public int PredictiveTilesRasterized => _predictiveTilesRasterized;
    public int CachedTileCount => _tiles.Count;
    public long CachedBytes => _currentBytes;
    public long BytesHighWater => _bytesHighWater;
    public ulong Version => _displayListVersion;
    public long RasterNanos => _rasterNanos;
    public long RecordingsProduced => _recordingsProduced;
    public SKRect PredictedViewport => _predictedViewport;
    public CompositorReplayer Replayer => _replayer;

    public TiledCompositor(
        int tileSize = DefaultTileSize,
        int overscanRings = 1,
        bool adaptiveTileSize = false,
        int minTileSize = 128,
        int maxTileSize = 1024,
        bool recordCommands = false,
        DisplayList? displayList = null,
        Func<SKRect, IEnumerable<PaintOp>>? opsProvider = null,
        TileManager? hubTiles = null,
        PredictiveTileScheduler? hubPredictor = null,
        MemoryBudget? memoryBudget = null)
    {
        if (tileSize < 64) tileSize = 64;
        if (tileSize > 2048) tileSize = 2048;
        _tileSize = tileSize;
        _adaptiveTileSize = adaptiveTileSize;
        _tileW = tileSize;
        _tileH = tileSize;
        _overscanRings = Math.Clamp(overscanRings, 0, 4);
        _recordCommands = recordCommands;
        _displayList = displayList ?? new DisplayList();
        _displayListVersion = 1;
        _nextTileVersion = 1;
        _hubTiles = hubTiles;
        _hubPredictor = hubPredictor;
        _memoryBudget = memoryBudget;
        _lastCleanupTick = Environment.TickCount64;
    }

    public void UpdateScrollVelocity(float vx, float vy)
    {
        _hubPredictor?.UpdateVelocity(vx, vy);
    }

    /// <summary>
    /// Compute the physical-pixel band that should be pre-rasterized to keep up
    /// with the current scroll velocity: the viewport extended by the distance
    /// travelled during <paramref name="lookaheadMillis"/> in the direction of
    /// travel. Returns the viewport unchanged when the page is not moving.
    /// </summary>
    public SKRect ComputePredictiveBand(SKRect physicalViewport, float lookaheadMillis = 200f)
    {
        if (_hubPredictor == null) return physicalViewport;
        var vel = _hubPredictor.Velocity;
        if (MathF.Abs(vel.Vx) < 1f && MathF.Abs(vel.Vy) < 1f) return physicalViewport;
        float dy = vel.Vy * (lookaheadMillis / 1000f);
        // Clamp the look-ahead so a velocity spike (e.g. a frame with a tiny dt)
        // cannot blow the preraster band up and stall the per-frame raster loop.
        dy = Math.Clamp(dy, -MaxLookaheadPx, MaxLookaheadPx);
        return new SKRect(
            physicalViewport.Left,
            Math.Min(physicalViewport.Top, physicalViewport.Top + Math.Min(0, dy)),
            physicalViewport.Right,
            Math.Max(physicalViewport.Bottom, physicalViewport.Bottom + Math.Max(0, dy)));
    }

    /// <summary>Report the viewport that the compositor should aim at (in physical device pixels).</summary>
    public void SetPredictedViewport(SKRect physicalViewport)
    {
        _predictedViewport = physicalViewport;
    }

    /// <summary>
    /// Replace the display list. When the list identity changes we bump
    /// <c>_displayListVersion</c> so the next render pass discards stale tiles
    /// before rasterising.
    /// </summary>
    public void SetDisplayList(DisplayList displayList)
    {
        if (ReferenceEquals(_displayList, displayList)) return;
        _displayList = displayList ?? new DisplayList();
        _displayListVersion++;
        _regionInvalidatedSinceLastSwap = false;
    }

    /// <summary>
    /// Adopt a rebuilt display list whose change is confined to the rects that
    /// were region-invalidated via <see cref="InvalidateRect"/>. Unlike
    /// <see cref="SetDisplayList"/> the cache generation is preserved (surviving
    /// tiles stay valid), but the recorded picture guard is broken so the next
    /// per-tile rasterization re-records from the new list. This must only be
    /// used when every changed pixel lies inside a region-invalidated rect —
    /// otherwise a surviving tile would keep displaying stale content.
    /// </summary>
    public void AdoptRebuiltDisplayList(DisplayList displayList)
    {
        _displayList = displayList ?? new DisplayList();
        if (_recordedPicture != null)
        {
            _recordedPicture.Dispose();
            _recordedPicture = null;
        }
        _recordedPictureVersion = 0;
        _recordedPictureSource = null;
        _pictureBounds = SKRect.Empty;
    }

    public void InvalidateAll()
    {
        _nextTileVersion++;
        foreach (var t in _tiles.Values) t.Dispose();
        _tiles.Clear();
        _lru.Clear();
        _currentBytes = 0;
        _hubTiles?.Clear();
        _hubPredictor?.ClearPending();
        _regionInvalidatedSinceLastSwap = false;
    }

/// <summary>
    /// Invalidate only the tiles that intersect <paramref name="pageRect"/>.
    /// </summary>
    public void InvalidateRect(SKRect pageRect)
    {
        if (pageRect.Width <= 0 || pageRect.Height <= 0)
        {
            InvalidateAll();
            return;
        }
        _pendingInvalidateUnion = _pendingInvalidateUnion.IsEmpty
            ? pageRect
            : SKRect.Union(_pendingInvalidateUnion, pageRect);
        int minX = PageXToTile(pageRect.Left);
        int maxX = PageXToTile(pageRect.Right - 0.01f);
        int minY = PageYToTile(pageRect.Top);
        int maxY = PageYToTile(pageRect.Bottom - 0.01f);
        _nextTileVersion++;
        for (int ty = minY; ty <= maxY; ty++)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                long key = TileKey(tx, ty);
                if (_tiles.TryGetValue(key, out var t))
                {
                    t.Dispose();
                    _tiles.Remove(key);
                    _lru.Remove(key);
                    _currentBytes -= t.EstimatedBytes;
                }
            }
        }
        // Region-only invalidation: the remaining tiles encode unchanged content,
        // so flag the next display-list swap to reuse them. The next Render adopts
        // the rebuilt list without bumping the cache generation.
        _regionInvalidatedSinceLastSwap = true;
    }

    /// <summary>
    /// Declare the rect that will actually be re-rasterized this frame (the flushed
    /// on-screen slice of a scrolled container). Into this rect the recorded picture
    /// culls to the change instead of the whole viewport, so the per-frame re-record
    /// and the container tile raster replay ~O(container) ops rather than ~O(viewport).
    /// Non-declared regions are guaranteed to keep their cached tiles; a re-raster
    /// that ever lands outside this cull forces a full re-record (see
    /// <see cref="ForceFullRecord"/>).
    /// </summary>
    public void SetChangeRegion(SKRect pageRect)
    {
        if (pageRect.Width <= 0 || pageRect.Height <= 0) return;
        _pendingChangeRegion = _pendingChangeRegion.IsEmpty
            ? pageRect
            : SKRect.Union(_pendingChangeRegion, pageRect);
    }

    /// <summary>
    /// Composite the viewport. Fully owns the canvas transform:
    ///   <c>canvas.Save() → ResetMatrix() → Translate(-originX, -originY) → ClipRect(physicalViewport)</c>
    /// Tiles are stored in page-space logical coordinates and drawn at
    /// <c>tx*tileSize*physicalScale - origin</c>, so <paramref name="originX"/>/
    /// <paramref name="originY"/> must describe the physical (device-pixel)
    /// position of page-space origin (0,0) — normally the scroll offset, DPR
    /// and content offset. All clipping happens in the same physical space.
    /// </summary>
    public void Render(SKCanvas canvas, SKRect physicalViewport, float physicalScale, float originX = 0f, float originY = 0f, DisplayList? displayList = null, SKColor? backgroundFill = null)
    {
        if (displayList != null && !ReferenceEquals(_displayList, displayList))
        {
            _displayList = displayList;
            // When the app refreshed only the invalidated region (via
            // InvalidateRect) before handing us a rebuilt display list that is
            // identical everywhere else, keep the surviving tiles instead of
            // dropping the whole cache. Any other swap means new content.
            if (!_regionInvalidatedSinceLastSwap)
                _displayListVersion++;
        }
        _regionInvalidatedSinceLastSwap = false;
        if (physicalScale <= 0.01f) physicalScale = 1f;

        _lastPhysicalScale = physicalScale;
        _lastPhysicalViewport = physicalViewport;

        // Settle the picture bounds and the (possibly adaptive) tile grid before
        // any index arithmetic so one frame always sees a single grid. The
        // pending-invalidate union must still be live here so EnsurePicture can
        // fold the invalidated rect into its recorded-picture cull — otherwise a
        // region-invalidated scroll container that extends past the viewport
        // edge would be culled out of the recording and rasterize transparent.
        EnsurePicture();
        _pendingInvalidateUnion = SKRect.Empty;
        _pendingChangeRegion = SKRect.Empty;

        // ---- version check: discard cache when display list changed ----
        PeriodicallyCleanup();
        if (_lastRenderedDisplayListVersion < _displayListVersion)
        {
            InvalidateAll();
            _lastRenderedDisplayListVersion = _displayListVersion;
        }

        _drawCalls = 0;
        _tilesReused = 0;
        _tilesRasterized = 0;
        _backgroundTilesCreatedThisFrame = 0;
        _tilesSkippedThisFrame = 0;
        _overscanTilesRasterized = 0;
        _predictiveTilesRasterized = 0;
        _visibleTilesRasterized = 0;

        _deferred.Clear();
        _deferredKeys.Clear();

        _lastOriginX = originX;
        _lastOriginY = originY;

        // The compositor takes full control of the canvas transform. Tiles are
        // drawn in physical device pixels relative to the page origin, so any
        // scroll/content offset the caller wants must arrive here as `origin`.
        canvas.Save();
        canvas.ResetMatrix();
        canvas.Translate(-originX, -originY);
        canvas.ClipRect(physicalViewport);

        // Tile indices from the physical viewport. Grid cells may differ per
        // axis and the last row/column clamps to the picture bounds.
        float tilePhysicalX = _tileW * physicalScale;
        float tilePhysicalY = _tileH * physicalScale;
        int visMinX = (int)Math.Floor(physicalViewport.Left / tilePhysicalX);
        int visMaxX = (int)Math.Floor((physicalViewport.Right - 0.01f) / tilePhysicalX);
        int visMinY = (int)Math.Floor(physicalViewport.Top / tilePhysicalY);
        int visMaxY = (int)Math.Floor((physicalViewport.Bottom - 0.01f) / tilePhysicalY);

        // 1. Visible tiles.
        for (int ty = visMinY; ty <= visMaxY; ty++)
        {
            for (int tx = visMinX; tx <= visMaxX; tx++)
            {
                if (DrawOrBuildTile(canvas, tx, ty, physicalScale, TilePriority.Visible))
                    _visibleTilesRasterized++;
            }
        }

        // 2. Overscan tiles.
        if (_overscanRings > 0)
        {
            // TODO(audit): the overscan ring is not clamped to the picture/content
            // bounds, so off-picture tiles are rasterized (as transparent) and
            // cached, wasting raster work and memory. The reference clamps the
            // skewport/soon/prefetch rects to the picture bounds so no work is
            // scheduled outside the picture. Clamping needs a picture-bounds API on
            // the display list (currently absent), so it is deferred.
            int expand = _overscanRings;
            for (int ring = 1; ring <= expand; ring++)
            {
                int top = visMinY - ring;
                int bottom = visMaxY + ring;
                int left = visMinX - ring;
                int right = visMaxX + ring;
                for (int tx = left; tx <= right; tx++)
                {
                    if (DrawOrBuildTile(canvas, tx, top, physicalScale, TilePriority.Overscan))
                        _overscanTilesRasterized++;
                    if (DrawOrBuildTile(canvas, tx, bottom, physicalScale, TilePriority.Overscan))
                        _overscanTilesRasterized++;
                }
                for (int ty = top + 1; ty <= bottom - 1; ty++)
                {
                    if (DrawOrBuildTile(canvas, left, ty, physicalScale, TilePriority.Overscan))
                        _overscanTilesRasterized++;
                    if (DrawOrBuildTile(canvas, right, ty, physicalScale, TilePriority.Overscan))
                        _overscanTilesRasterized++;
                }
            }
        }

        // The retained-surface composite keeps the previous frame's pixels where
        // no tile exists yet. The viewport regions that have no page content —
        // beyond the picture bounds: below a short page, right of a narrow
        // column (the scrollbar strip), or above/left of content when scrolled
        // to a negative offset — would otherwise retain stale pixels forever, so
        // fill them with the page background. physicalViewport is already in
        // device coords (page position x physicalScale), so dividing by the
        // scale yields the page-space viewport.
        var pageViewport = new SKRect(
            physicalViewport.Left / physicalScale,
            physicalViewport.Top / physicalScale,
            physicalViewport.Right / physicalScale,
            physicalViewport.Bottom / physicalScale);
        bool fillRight = pageViewport.Right > _pictureBounds.Right;
        bool fillBottom = pageViewport.Bottom > _pictureBounds.Bottom;
        bool fillLeft = pageViewport.Left < _pictureBounds.Left;
        bool fillTop = pageViewport.Top < _pictureBounds.Top;
        if (fillRight || fillBottom || fillLeft || fillTop)
        {
            var fillColor = backgroundFill ?? SKColors.White;
            using var bg = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill };
            if (fillRight)
            {
                canvas.DrawRect(
                    new SKRect(_pictureBounds.Right * physicalScale, pageViewport.Top * physicalScale,
                        pageViewport.Right * physicalScale, pageViewport.Bottom * physicalScale), bg);
            }
            if (fillBottom)
            {
                canvas.DrawRect(
                    new SKRect(pageViewport.Left * physicalScale, _pictureBounds.Bottom * physicalScale,
                        pageViewport.Right * physicalScale, pageViewport.Bottom * physicalScale), bg);
            }
            if (fillLeft)
            {
                canvas.DrawRect(
                    new SKRect(pageViewport.Left * physicalScale, pageViewport.Top * physicalScale,
                        _pictureBounds.Left * physicalScale, pageViewport.Bottom * physicalScale), bg);
            }
            if (fillTop)
            {
                canvas.DrawRect(
                    new SKRect(pageViewport.Left * physicalScale, pageViewport.Top * physicalScale,
                        pageViewport.Right * physicalScale, _pictureBounds.Top * physicalScale), bg);
            }
        }

        // Composite cached scroll layers (blink-style "moved scrollers"): a layered
        // container is drawn here at its CURRENT scroll offset on every frame, so
        // scrolling it never re-rasterizes tiles or re-records the picture — it is
        // pure composition. The container's own tiles carry a hole (the paint emits
        // no content for it); this pass fills that hole from the cached layer.
        DrawLiveScrollLayers(canvas, physicalScale, originX, originY);

        EnforceBudget();
        canvas.Restore();

if (PipelineTimings.TilesRasterized != null)
        {
            PipelineTimings.TilesRasterized.AddSample(_tilesRasterized);
            PipelineTimings.TilesReused.AddSample(_tilesReused);
        }
    }

    /// <summary>
    /// Composited scroll layers (see <see cref="ScrollLayerCache"/>): draw each
    /// cached scroll container at its live scroll offset — content image + live
    /// scrollbar — directly in compositor space. Pure per-frame composition, no
    /// re-raster / re-record. The canvas here is in physical device pixels with the
    /// page origin already applied, matching the tile draw below.
    /// </summary>
    private void DrawLiveScrollLayers(SKCanvas canvas, float physicalScale, float originX, float originY)
    {
        if (!ScrollLayerCache.HasAny) return;
        ScrollLayerCache.Enumerate(info =>
        {
            var cb = info.ContentBox;
            // Visibility cull in CANVAS coordinates. The canvas is already
            // translated by (-originX,-originY) into page×scale space (the same
            // space the layer is drawn in and the space _lastPhysicalViewport
            // is expressed in) — subtracting origin here would compare device
            // space against canvas space and cull visible layers once the page
            // scrolls (leaving the layered container empty).
            float vx0 = cb.Left * physicalScale;
            float vy0 = cb.Top * physicalScale;
            float vx1 = cb.Right * physicalScale;
            float vy1 = cb.Bottom * physicalScale;
            if (!_lastPhysicalViewport.IntersectsWith(new SKRect(vx0, vy0, vx1, vy1)))
                return;

            var box = info.Box;
            float sx = info.IsBaked ? 0f : box.ScrollX;
            float sy = info.IsBaked ? 0f : box.ScrollY;
            // Scroll offset is snapped to the device grid only when the container is
            // NOT smooth-scrolling: at rest this keeps the baked bitmap texel-for-
            // texel crisp, while mid-smooth-scroll the fractional offset moves the
            // layer continuously (no 1px judder). Wheel scrolls are integer anyway.
            float devSx = info.IsBaked || box.IsSmoothScrollingX ? sx * physicalScale : MathF.Round(sx * physicalScale);
            float devSy = info.IsBaked || box.IsSmoothScrollingY ? sy * physicalScale : MathF.Round(sy * physicalScale);
            // The layer was baked with its translate SNAPPED to the device grid
            // (BuildScrollLayer), so image pixel u holds content at page
            // round(cb.Left*scale)/scale + u/scale; drawing pixel 0 at
            // round(cb.Left*scale) maps every baked texel to a whole device pixel.
            float cx0 = MathF.Round(cb.Left * physicalScale);
            float cy0 = MathF.Round(cb.Top * physicalScale);
            canvas.Save();
            canvas.ClipRect(new SKRect(
                cb.Left * physicalScale, cb.Top * physicalScale,
                cb.Right * physicalScale, cb.Bottom * physicalScale));
            canvas.Translate(-devSx, -devSy);
            var layerImage = info.Image;
            canvas.DrawImage(layerImage,
                new SKRect(cx0, cy0,
                    cx0 + layerImage.Width,
                    cy0 + layerImage.Height),
                TileSampling, null);
            canvas.Restore();
            DrawLiveScrollbar(canvas, info, physicalScale);
        });
    }

    /// <summary>Draw one scroll container's live scrollbar (track / thumb / corner).</summary>
    private static void DrawLiveScrollbar(SKCanvas canvas, ScrollLayerInfo info, float scale)
    {
        var box = info.Box;
        if (!info.ShowVertical && !info.ShowHorizontal) return;

        var pb = info.PaddingBox;
        // This canvas is in DEVICE pixels (page × scale), so the logical
        // scrollbar thickness/radius must be scaled to match the classic painter
        // (otherwise the layered bar renders thinner/sharper at DPI ≠ 1).
        float t = info.ScrollbarThickness * scale;
        float radius = info.ThumbRadius * scale;
        float X0 = pb.Left * scale;
        float Y0 = pb.Top * scale;
        float X1 = pb.Right * scale;
        float Y1 = pb.Bottom * scale;

        using var trackPaint = new SKPaint { Color = info.TrackColor, Style = SKPaintStyle.Fill };
        using var thumbPaint = new SKPaint { Color = info.ThumbColor, Style = SKPaintStyle.Fill, IsAntialias = true };

        float vRange = Math.Max(1f, box.ScrollContentHeight - box.ContentBox.Height);
        float hRange = Math.Max(1f, box.ScrollContentWidth - box.ContentBox.Width);

        if (info.ShowVertical)
        {
            float trackX = X1 - t;
            float trackH = Math.Max(0f, (Y1 - Y0) - (info.ShowHorizontal ? t : 0f));
            canvas.DrawRect(new SKRect(trackX, Y0, X1, Y0 + trackH), trackPaint);
            if (trackH > 0)
            {
                float ratio = box.ContentBox.Height / Math.Max(1f, box.ScrollContentHeight);
                float thumbH = Math.Min(trackH, Math.Max(20f * scale, trackH * Math.Min(1f, ratio)));
                float pos = Math.Clamp(box.ScrollY, 0f, vRange);
                float thumbY = Y0 + (trackH - thumbH) * (pos / vRange);
                canvas.DrawRoundRect(new SKRect(trackX + 2f * scale, thumbY + 1f * scale, X1 - 2f * scale, thumbY + thumbH - 1f * scale), radius, radius, thumbPaint);
            }
        }

        if (info.ShowHorizontal)
        {
            float trackW = Math.Max(0f, (X1 - X0) - (info.ShowVertical ? t : 0f));
            float trackY = Y1 - t;
            canvas.DrawRect(new SKRect(X0, trackY, X0 + trackW, Y1), trackPaint);
            if (trackW > 0)
            {
                float ratio = box.ContentBox.Width / Math.Max(1f, box.ScrollContentWidth);
                float thumbW = Math.Min(trackW, Math.Max(20f * scale, trackW * Math.Min(1f, ratio)));
                float pos = Math.Clamp(box.ScrollX, 0f, hRange);
                float thumbX = X0 + (trackW - thumbW) * (pos / hRange);
                canvas.DrawRoundRect(new SKRect(thumbX + 1f * scale, trackY + 2f * scale, thumbX + thumbW - 1f * scale, Y1 - 2f * scale), radius, radius, thumbPaint);
            }
        }

        if (info.ShowVertical && info.ShowHorizontal)
            canvas.DrawRect(new SKRect(X1 - t, Y1 - t, X1, Y1), trackPaint);
    }

    /// <summary>
    /// Read-back step for the predictive scheduler.
    /// </summary>
    public int PumpPredictiveTiles(SKCanvas canvas, int maxTiles)
    {
        if (_hubPredictor == null) return 0;
        if (_predictedViewport.IsEmpty) return 0;
        int pumped = 0;
        EnsurePicture();
        float tilePhysicalX = _tileW * _lastPhysicalScale;
        float tilePhysicalY = _tileH * _lastPhysicalScale;

        // Predictive tiles are drawn in the same physical space as Render().
        canvas.Save();
        canvas.ResetMatrix();
        canvas.Translate(-_lastOriginX, -_lastOriginY);

        // The caller already computed the band (ComputePredictiveBand) from the
        // scheduler's velocity. Walk the band and queue missing tiles through
        // DrawOrBuildTile (deferred in production); the picture-bounds check and
        // per-frame caps inside it prevent redundant work. The hub raster queue
        // itself is not driven by the app, so nothing is scheduled through it.
        int predMinX = (int)Math.Floor(_predictedViewport.Left / tilePhysicalX);
        int predMaxX = (int)Math.Floor((_predictedViewport.Right - 0.01f) / tilePhysicalX);
        int predMinY = (int)Math.Floor(_predictedViewport.Top / tilePhysicalY);
        int predMaxY = (int)Math.Floor((_predictedViewport.Bottom - 0.01f) / tilePhysicalY);
        int added = 0;
        for (int ty = predMinY; ty <= predMaxY && added < maxTiles; ty++)
        {
            for (int tx = predMinX; tx <= predMaxX && added < maxTiles; tx++)
            {
                if (DrawOrBuildTile(canvas, tx, ty, _lastPhysicalScale, TilePriority.Predictive))
                {
                    _predictiveTilesRasterized++;
                    added++;
                }
            }
        }
        pumped = added;

        canvas.Restore();
        return pumped;
    }

    private IEnumerable<PaintOp> OpsFor(SKRect tilePageRect)
    {
        return _displayList.GetOpsInRect(tilePageRect);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool DrawOrBuildTile(SKCanvas canvas, int tx, int ty, float physicalScale, TilePriority priority)
    {
        long key = TileKey(tx, ty);
        bool isBackground = priority is TilePriority.Overscan or TilePriority.Predictive;
        if (_tiles.TryGetValue(key, out var tile)
            && tile.Image != null
            && !tile.Disposed
            && tile.DisplayListVersion == _displayListVersion)
        {
            Touch(tile);
            // Background tiles (overscan / predictive band) sit outside the
            // viewport clip, so blitting them to the surface is pure clip-test
            // waste in deferred mode. Visible tiles are always composited.
            if (!isBackground || !DeferRasterization)
            {
                canvas.DrawImage(tile.Image, tx * _tileW * physicalScale, ty * _tileH * physicalScale, TileSampling, null);
                _drawCalls++;
            }
            _tilesReused++;
            return false;
        }

        // Skip tiles outside the recorded picture bounds (matches the reference:
        // tiles are only created for rects intersecting the picture), so off-page
        // transparent tiles are never queued or rasterized.
        EnsurePicture();
        var tilePageRect = CellRect(tx, ty);
        if (!tilePageRect.IntersectsWith(_pictureBounds))
            return false;

        // A change-only picture cannot satisfy a tile that reaches outside it —
        // treat any such miss as a request for the full recording.
        if (_pictureIsChangeOnly && !tilePageRect.IntersectsWith(_recordedCullRect))
            ForceFullRecord();

        // Per-frame cap for non-visible tiles so a coherent scroll does not flood
        // the deferred queue.
        if (isBackground && _backgroundTilesCreatedThisFrame >= MaxBackgroundTilesPerFrame)
        {
            _tilesSkippedThisFrame++;
            return false;
        }

        if (DeferRasterization)
        {
            // Deferred mode (production): EVERY tile, the visible strip included,
            // is queued and drained by RasterDeferred() under a per-frame time
            // budget. The frame composites only already-cached tiles; where a tile
            // is not ready the retained surface keeps the previous frame's pixels
            // (the caller skips the page-area clear on scroll frames), so there is
            // no white gap and no in-frame raster at all. A tile completed by the
            // drain requests a repaint and is composited on the next frame.
            long deferredKey = TileKey(tx, ty);
            if (_deferredKeys.Add(deferredKey))
            {
                _deferred.Add(new DeferredTile(tx, ty, physicalScale, priority));
                _backgroundTilesCreatedThisFrame++;
            }
            return false;
        }

        var (pixelW, pixelH) = CellPixels(tilePageRect, physicalScale);
        var sw = Clock.NowNanos();
        SKImage? img = RasterizeTile(tilePageRect, pixelW, pixelH, physicalScale, out var recorded);
        _rasterNanos += Clock.NowNanos() - sw;
        if (img == null) return false;

        if (isBackground) _backgroundTilesCreatedThisFrame++;
        tile = new Tile
        {
            X = tx,
            Y = ty,
            Image = img,
            PageRect = tilePageRect,
            Version = _nextTileVersion,
            DisplayListVersion = _displayListVersion,
            LastAccessTick = Environment.TickCount,
            EstimatedBytes = (long)pixelW * pixelH * 4,
            Priority = priority,
            Recorded = recorded,
        };
        if (recorded != null) _recordingsProduced++;
        _tiles[key] = tile;
        _lru.AddFirst(key);
        _currentBytes += tile.EstimatedBytes;
        if (_currentBytes > _bytesHighWater) _bytesHighWater = _currentBytes;
        _tilesRasterized++;

        canvas.DrawImage(img, tx * _tileW * physicalScale, ty * _tileH * physicalScale, TileSampling, null);
        _drawCalls++;
        return true;
    }

    /// <summary>
    /// Drain the deferred-raster backlog under a per-frame time budget. Completed
    /// tiles are stored in the cache; the next <see cref="Render"/> (requested via
    /// the caller checking <see cref="HasPendingRasterWork"/> plus the returned
    /// count) composites them through the normal viewport-clipped draw path.
    /// Returns the number of tiles stored this call.
    /// </summary>
    /// <param name="compositeInto">
    /// When supplied, each finished batch is composited into this canvas
    /// immediately, so a tile that lands during this drain is visible on the
    /// CURRENT frame instead of waiting one frame for the next Render.
    /// </param>
    public int RasterDeferred(SKCanvas? compositeInto = null)
    {
        if (_deferred.Count == 0) return 0;

        float speed = 0f;
        if (_hubPredictor != null)
        {
            var vel = _hubPredictor.Velocity;
            speed = MathF.Sqrt(vel.Vx * vel.Vx + vel.Vy * vel.Vy);
        }
        long budget = speed > 1f ? ScrollingRasterBudgetNanos : IdleRasterBudgetNanos;
        long deadline = Clock.NowNanos() + budget;

        // The debug command-recording path touches main-thread-only state
        // (op bounds scans, recording lists); keep it serial.
        if (_recordCommands)
        {
            int serialStored = 0;
            int consumed = 0;
            while (consumed < _deferred.Count && Clock.NowNanos() < deadline)
            {
                var item = _deferred[consumed];
                if (RasterizeAndStore(item.Tx, item.Ty, item.PhysicalScale, item.Priority))
                    serialStored++;
                consumed++;
            }
            if (consumed > 0) _deferred.RemoveRange(0, consumed);
            _tilesRasterized += serialStored;
            return serialStored;
        }

        // 1. Prepare on the main thread: drop tiles that got cached or fell off
        //    the picture, and settle the recorded picture. ForceFullRecord may
        //    re-record, so it must complete before any worker replays the picture.
        EnsurePicture();
        List<PreparedTile>? works = null;
        for (int i = 0; i < _deferred.Count; i++)
        {
            var item = _deferred[i];
            long key = TileKey(item.Tx, item.Ty);
            if (_tiles.TryGetValue(key, out var existing)
                && existing.Image != null
                && !existing.Disposed
                && existing.DisplayListVersion == _displayListVersion)
                continue;

            var tilePageRect = CellRect(item.Tx, item.Ty);
            if (!tilePageRect.IntersectsWith(_pictureBounds))
                continue;
            if (_pictureIsChangeOnly && !tilePageRect.IntersectsWith(_recordedCullRect))
                ForceFullRecord();

            var (pixelW, pixelH) = CellPixels(tilePageRect, item.PhysicalScale);
            (works ??= new List<PreparedTile>(_deferred.Count - i))
                .Add(new PreparedTile(key, item.Tx, item.Ty, item.Priority, item.PhysicalScale, tilePageRect, pixelW, pixelH));
        }

        var picture = _recordedPicture;
        if (works == null || picture == null) return 0;

        // 2. Rasterize in parallel batches and store back on the main thread.
        //    A batch costs about one tile of wall time, so the frame budget stays
        //    authoritative; the drain stops at the deadline and the next frame
        //    re-queues whatever is still missing.
        int stored = 0;
        long rasterNanos = 0;
        for (int idx = 0; idx < works.Count; idx += RasterParallelism)
        {
            int batch = Math.Min(RasterParallelism, works.Count - idx);
            var images = new SKImage?[batch];
            int start = idx;
            var sw = Clock.NowNanos();
            Parallel.For(0, batch, k => images[k] = RasterizePrepared(picture, works[start + k]));
            rasterNanos += Clock.NowNanos() - sw;

            for (int k = 0; k < batch; k++)
            {
                var w = works[start + k];
                var img = images[k];
                if (img == null) continue;
                StoreTile(w.Key, w.Tx, w.Ty, w.Priority, w.PageRect, w.PixelW, w.PixelH, img, recorded: null);
                stored++;
            }

            // Same-frame composite: draw the batch that just finished directly into
            // the surface being presented this frame. The compositor owns its own
            // transform (physical space relative to the page origin), exactly like
            // Render does.
            if (compositeInto != null && stored > 0)
            {
                compositeInto.Save();
                compositeInto.ResetMatrix();
                compositeInto.Translate(-_lastOriginX, -_lastOriginY);
                compositeInto.ClipRect(_lastPhysicalViewport);
                for (int k = 0; k < batch; k++)
                {
                    var img = images[k];
                    if (img == null) continue;
                    var w = works[start + k];
                    compositeInto.DrawImage(img, w.Tx * _tileW * w.PhysicalScale, w.Ty * _tileH * w.PhysicalScale, TileSampling, null);
                    _drawCalls++;
                }
                compositeInto.Restore();
            }

            if (Clock.NowNanos() >= deadline && start + batch < works.Count)
                break;
        }

        _rasterNanos += rasterNanos;
        _tilesRasterized += stored;
        return stored;
    }

    /// <summary>
    /// Rasterize one prepared tile by replaying the shared picture onto a private
    /// CPU surface. Thread-safe: <see cref="SKPicture"/> is immutable and every
    /// worker owns its bitmap, canvas, and resulting image.
    /// </summary>
    private static SKImage? RasterizePrepared(SKPicture picture, in PreparedTile w)
    {
        var info = new SKImageInfo(w.PixelW, w.PixelH, SKColorType.Rgba8888, SKAlphaType.Premul);
        try
        {
            using var bmp = new SKBitmap(info);
            using var tc = new SKCanvas(bmp);
            tc.Clear(SKColors.Transparent);
            tc.Scale(w.PhysicalScale, w.PhysicalScale);
            tc.Translate(-w.PageRect.Left, -w.PageRect.Top);
            tc.DrawPicture(picture);
            return SKImage.FromBitmap(bmp);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Synchronously rasterize every tile intersecting <paramref name="pageRect"/>
    /// so the region is cached before the enclosing <see cref="Render"/> composites
    /// it on the same frame. This is the region-scoped complement of
    /// <see cref="RasterDeferred"/>: for an element scroll the scrolled container's
    /// ON-SCREEN slice rarely exceeds a dozen tiles, so the region completes within
    /// this single call and the container updates in place with no late pop-in or
    /// tile-seam tearing. <paramref name="maxTiles"/> is a hard cap that only trips
    /// on pathological (viewport-sized) regions; overflow stays dropped and is
    /// re-queued by the normal visible pass once it enters the viewport. Returns
    /// the number of tiles rasterized.
    /// </summary>
    public int RasterizeRectSynchronous(SKRect pageRect, float physicalScale, int maxTiles = MaxSynchronousRegionTiles)
    {
        if (maxTiles <= 0) return 0;
        if (pageRect.Width <= 0 || pageRect.Height <= 0) return 0;

        EnsurePicture();
        int minX = PageXToTile(pageRect.Left);
        int minY = PageYToTile(pageRect.Top);
        int maxX = PageXToTile(pageRect.Right - 0.01f);
        int maxY = PageYToTile(pageRect.Bottom - 0.01f);

        int flushes = 0;
        for (int ty = minY; ty <= maxY && flushes < maxTiles; ty++)
        {
            for (int tx = minX; tx <= maxX && flushes < maxTiles; tx++)
            {
                if (RasterizeAndStore(tx, ty, physicalScale, TilePriority.Visible))
                    flushes++;
            }
        }
        return flushes;
    }

    /// <summary>Deferred rasterization: cache check, raster, insert. No drawing — Render composites.</summary>
    private bool RasterizeAndStore(int tx, int ty, float physicalScale, TilePriority priority)
    {
        long key = TileKey(tx, ty);
        if (_tiles.TryGetValue(key, out var existing)
            && existing.Image != null
            && !existing.Disposed
            && existing.DisplayListVersion == _displayListVersion)
            return false;

        // Skip tiles outside the recorded picture bounds (same rule as inline).
        EnsurePicture();
        var tilePageRect = CellRect(tx, ty);
        if (!tilePageRect.IntersectsWith(_pictureBounds))
            return false;

        // A change-only picture cannot satisfy a tile that reaches outside it —
        // treat any such miss as a request for the full recording.
        if (_pictureIsChangeOnly && !tilePageRect.IntersectsWith(_recordedCullRect))
            ForceFullRecord();

        var (pixelW, pixelH) = CellPixels(tilePageRect, physicalScale);
        var sw = Clock.NowNanos();
        SKImage? img = RasterizeTile(tilePageRect, pixelW, pixelH, physicalScale, out var recorded);
        _rasterNanos += Clock.NowNanos() - sw;
        if (img == null) return false;

        StoreTile(key, tx, ty, priority, tilePageRect, pixelW, pixelH, img, recorded);
        return true;
    }

    /// <summary>
    /// Insert an already-rasterized tile into the cache and update statistics.
    /// Main-thread only: touches the tile map, LRU list, and byte counters.
    /// </summary>
    private void StoreTile(long key, int tx, int ty, TilePriority priority, SKRect tilePageRect,
        int pixelW, int pixelH, SKImage img, CompositorDisplayList? recorded)
    {
        var tile = new Tile
        {
            X = tx,
            Y = ty,
            Image = img,
            PageRect = tilePageRect,
            Version = _nextTileVersion,
            DisplayListVersion = _displayListVersion,
            LastAccessTick = Environment.TickCount,
            EstimatedBytes = (long)pixelW * pixelH * 4,
            Priority = priority,
            Recorded = recorded,
        };
        if (recorded != null) _recordingsProduced++;
        _tiles[key] = tile;
        _lru.AddFirst(key);
        _currentBytes += tile.EstimatedBytes;
        if (_currentBytes > _bytesHighWater) _bytesHighWater = _currentBytes;
        switch (priority)
        {
            case TilePriority.Visible: _visibleTilesRasterized++; break;
            case TilePriority.Overscan: _overscanTilesRasterized++; break;
            case TilePriority.Predictive: _predictiveTilesRasterized++; break;
        }
    }

    private SKImage? RasterizeTile(SKRect tilePageRect, int pixelW, int pixelH, float physicalScale, out CompositorDisplayList? recorded)
    {
        recorded = null;
        var info = new SKImageInfo(pixelW, pixelH, SKColorType.Rgba8888, SKAlphaType.Premul);
        try
        {
            using var bmp = new SKBitmap(info);
            using var tc = new SKCanvas(bmp);
            tc.Clear(SKColors.Transparent);
            tc.Scale(physicalScale, physicalScale);
            tc.Translate(-tilePageRect.Left, -tilePageRect.Top);

            if (_recordCommands)
            {
                // Debug command recording still scans the ops intersecting the tile
                // (cheap bounds-only); rendering itself replays the recorded
                // picture, so the expensive layout-level work runs once per
                // display-list change instead of once per tile.
                var rec = new CompositorDisplayList();
                rec.Add(CompositorCommand.Save());
                rec.Add(CompositorCommand.Translate(-tilePageRect.Left, -tilePageRect.Top));
                rec.Add(CompositorCommand.ClipRect(tilePageRect));
                foreach (var op in OpsFor(tilePageRect))
                    rec.Add(CompositorCommand.DrawRect(op.Bounds, SKColors.Transparent));
                rec.Add(CompositorCommand.Restore());
                recorded = rec;
            }

            EnsurePicture();
            if (_recordedPicture != null)
                tc.DrawPicture(_recordedPicture);

            return SKImage.FromBitmap(bmp);
        }
        catch
        {
            recorded?.Reset();
            return null;
        }
    }

    /// <summary>
    /// Ensure the display list is recorded into an <see cref="SKPicture"/> once.
    /// Every tile replays the same picture with a clip + transform — the model
    /// used by the reference compositor, which rasterizes tiles by playing back a
    /// pre-recorded picture instead of re-running paint ops per tile.
    /// </summary>
    /// <summary>
    /// Declare that an element scroll offset changed. The recorded picture is then
    /// re-baked on the next raster so live scroll-layer ops capture the new offset;
    /// the cache generation (and all surviving tiles) is untouched.
    /// </summary>
    public void MarkScrollBakeDirty() => _scrollBakeTick++;

    private void EnsurePicture()
    {
        if (_recordedPicture != null
            && ReferenceEquals(_recordedPictureSource, _displayList)
            && _recordedPictureVersion == _displayListVersion
            && _recordedScrollBakeTick == _scrollBakeTick)
            return;

        _recordedPicture?.Dispose();
        _recordedPicture = null;

        _pictureBounds = SKRect.Empty;
        _displayList.ForEachOp(op =>
        {
            if (!op.Bounds.IsEmpty)
                _pictureBounds = _pictureBounds.IsEmpty ? op.Bounds : SKRect.Union(_pictureBounds, op.Bounds);
        });
        // The grid responds to the content bounds (adaptive mode), so it must be
        // settled before any tile-index arithmetic against the new picture bounds.
        ComputeAdaptiveTileSize();
        if (_pictureBounds.IsEmpty)
        {
            _pictureBounds = new SKRect(0, 0, 1, 1);
            _recordedPictureVersion = _displayListVersion;
            _recordedPictureSource = _displayList;
            _recordedScrollBakeTick = _scrollBakeTick;
            _recordedCullRect = _pictureBounds;
            _pictureIsChangeOnly = false;
            return;
        }

        var contentCull = new SKRect(
            _pictureBounds.Left - 64f,
            _pictureBounds.Top - 64f,
            _pictureBounds.Right + 64f,
            _pictureBounds.Bottom + 64f);

        float margin = _tileW > _tileH ? _tileW : _tileH;
        bool changeOnly = !_pendingChangeRegion.IsEmpty;
        if (changeOnly)
        {
            // Region-scoped repaint (element scroll): the only pixels scheduled for
            // re-raster this frame lie inside the flushed change region (plus one
            // tile so edge-straddling cells stay complete), so record exactly that.
            // The full document stays fixed; odds/evicted tiles outside this cull
            // force a full re-record via ForceFullRecord before they raster.
            contentCull = Inflate(_pendingChangeRegion, margin);
        }
        else
        {
            // General path: cull to the recorded picture, the visible viewport, the
            // predictive band and the invalidated rects so a per-frame re-record
            // replays just the visible/changed ops instead of the whole document.
            // The full picture bounds stay authoritative for the grid geometry.
            if (!_lastPhysicalViewport.IsEmpty)
            {
                var vp = new SKRect(
                    _lastPhysicalViewport.Left / _lastPhysicalScale,
                    _lastPhysicalViewport.Top / _lastPhysicalScale,
                    _lastPhysicalViewport.Right / _lastPhysicalScale,
                    _lastPhysicalViewport.Bottom / _lastPhysicalScale);
                contentCull = SKRect.Union(contentCull, Inflate(vp, margin));
            }
            if (!_predictedViewport.IsEmpty)
            {
                var pv = new SKRect(
                    _predictedViewport.Left / _lastPhysicalScale,
                    _predictedViewport.Top / _lastPhysicalScale,
                    _predictedViewport.Right / _lastPhysicalScale,
                    _predictedViewport.Bottom / _lastPhysicalScale);
                contentCull = SKRect.Union(contentCull, Inflate(pv, margin));
            }
            if (!_pendingInvalidateUnion.IsEmpty)
                contentCull = SKRect.Union(contentCull, Inflate(_pendingInvalidateUnion, margin));
        }

        var recorder = new SKPictureRecorder();
        var recordCanvas = recorder.BeginRecording(contentCull);
        // Layered scroll containers are composited LIVE every frame (see
        // DrawLiveScrollLayers), so the tile picture leaves a hole at their
        // position — replaying the DrawScrollLayerOp into tiles would bake a
        // stale offset that ghosts over the live layer while scrolling.
        _displayList.Execute(recordCanvas, contentCull, skipScrollLayerOps: true);
        _recordedPicture = recorder.EndRecording();
        _recordedPictureVersion = _displayListVersion;
        _recordedPictureSource = _displayList;
        _recordedScrollBakeTick = _scrollBakeTick;
        _recordedCullRect = contentCull;
        _pictureIsChangeOnly = changeOnly;
    }

    /// <summary>
    /// Re-record the picture at full cull (viewport + picture bounds). Called when a
    /// cache miss lands outside the change-only cull, so the missing cell always
    /// rasters with the complete document behind it.
    /// </summary>
    private void ForceFullRecord()
    {
        if (_recordedPicture != null)
        {
            _recordedPicture.Dispose();
            _recordedPicture = null;
        }
        // Forget the change-only intent so the next EnsurePicture takes the full
        // general cull branch while keeping the same cache generation.
        _recordedPictureVersion = 0;
        _recordedPictureSource = null;
        _pictureIsChangeOnly = false;
        _pendingChangeRegion = SKRect.Empty;
        EnsurePicture();
    }

    /// <summary>Return <paramref name="rect"/> padded by <paramref name="margin"/> on all sides.</summary>
    private static SKRect Inflate(SKRect rect, float margin)
    {
        return new SKRect(rect.Left - margin, rect.Top - margin, rect.Right + margin, rect.Bottom + margin);
    }

    /// <summary>
    /// Compute the per-axis tile grid dimensions from the picture bounds. When
    /// adaptive tiling is off the grid is uniform (<c>_tileSize</c>). Adaptive
    /// mode follows the reference CPU-raster rule set:
    ///   * narrow content widens the tiles vertically;
    ///   * short content widens the tiles horizontally;
    ///   * content under the "untiled" size is rasterized as one tile;
    ///   * tile sizes round up to multiples of 64 then 4, and never exceed the
    ///     max texture size.
    /// The last row/column still clamps each cell to the content bounds.
    /// </summary>
    private void ComputeAdaptiveTileSize()
    {
        if (!_adaptiveTileSize)
        {
            _tileW = _tileSize;
            _tileH = _tileSize;
            return;
        }

        float contentW = _pictureBounds.Width;
        float contentH = _pictureBounds.Height;
        int defaultW = _tileSize;
        int defaultH = _tileSize;
        if (contentW < defaultW)
            defaultH = MaxUntiledLayerHeight;
        if (contentH < defaultH)
            defaultW = MaxUntiledLayerWidth;
        if (contentW < MaxUntiledLayerWidth && contentH < MaxUntiledLayerHeight)
        {
            defaultW = MaxUntiledLayerWidth;
            defaultH = MaxUntiledLayerHeight;
        }

        int tileW = defaultW;
        int tileH = defaultH;
        if (contentW < defaultW)
        {
            tileW = (int)MathF.Ceiling(contentW / TileRoundUp) * TileRoundUp;
            tileW = Math.Min(tileW, defaultW);
        }
        if (contentH < defaultH)
        {
            tileH = (int)MathF.Ceiling(contentH / TileRoundUp) * TileRoundUp;
            tileH = Math.Min(tileH, defaultH);
        }
        tileW = (int)MathF.Ceiling(tileW / (float)TileMinimalAlignment) * TileMinimalAlignment;
        tileH = (int)MathF.Ceiling(tileH / (float)TileMinimalAlignment) * TileMinimalAlignment;
        tileW = Math.Min(tileW, MaxTextureSize);
        tileH = Math.Min(tileH, MaxTextureSize);

        _tileW = Math.Max(tileW, TileMinimalAlignment);
        _tileH = Math.Max(tileH, TileMinimalAlignment);
    }

    /// <summary>
    /// Page-space rect of the grid cell (tx, ty). Interior cells span a full
    /// grid dimension; the last row/column clamps to the picture bounds so the
    /// raster never covers space the page does not.
    /// </summary>
    private SKRect CellRect(int tx, int ty)
    {
        float x = tx * _tileW;
        float y = ty * _tileH;
        float w = MathF.Min(_tileW, MathF.Max(0f, _pictureBounds.Right - x));
        float h = MathF.Min(_tileH, MathF.Max(0f, _pictureBounds.Bottom - y));
        return new SKRect(x, y, x + w, y + h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int W, int H) CellPixels(SKRect cell, float scale)
    {
        return (
            Math.Max(1, (int)MathF.Ceiling(cell.Width * scale)),
            Math.Max(1, (int)MathF.Ceiling(cell.Height * scale)));
    }

    private void Touch(Tile t)
    {
        long key = TileKey(t.X, t.Y);
        _lru.Remove(key);
        _lru.AddFirst(key);
        t.LastAccessTick = Environment.TickCount;
    }

    private void EnforceBudget()
    {
        long cap = GetEffectiveByteCap();
        int maxTiles = GetEffectiveTileCap();
        while ((_tiles.Count > maxTiles || _currentBytes > cap) && _lru.Count > 0)
        {
            long evictKey = _lru.Last!.Value;
            if (_tiles.TryGetValue(evictKey, out var t))
            {
                _currentBytes -= t.EstimatedBytes;
                t.Dispose();
                _tiles.Remove(evictKey);
                _tilesEvictedBudget++;
            }
            _lru.RemoveLast();
        }
    }

    /// <summary>Evict tiles untouched for more than <see cref="StaleTileAgeMs"/>.</summary>
    private void PeriodicallyCleanup()
    {
        long now = Environment.TickCount64;
        if (now - _lastCleanupTick < CleanupIntervalMs) return;
        _lastCleanupTick = now;

        int evicted = 0;
        var keys = _tiles.Keys.ToArray();
        foreach (var key in keys)
        {
            if (_tiles.TryGetValue(key, out var t)
                && (now - t.LastAccessTick) > StaleTileAgeMs)
            {
                _currentBytes -= t.EstimatedBytes;
                t.Dispose();
                _tiles.Remove(key);
                _lru.Remove(key);
                evicted++;
            }
        }
        if (evicted > 0) _tilesEvictedLru += evicted;
    }

    private long GetEffectiveByteCap()
    {
        if (_memoryBudget == null) return DefaultMaxBytes;
        return _memoryBudget.TileMemorySoftLimit;
    }

    private int GetEffectiveTileCap()
    {
        if (_memoryBudget == null) return MaxTiles;
        long bytesPerTile = (long)_tileW * _tileH * 4;
        long capFromBytes = _memoryBudget.TileMemorySoftLimit / Math.Max(1, bytesPerTile);
        return (int)Math.Min(MaxTiles, capFromBytes);
    }

    public void EvictLru(int count)
    {
        for (int i = 0; i < count && _lru.Count > 0; i++)
        {
            long evictKey = _lru.Last!.Value;
            if (_tiles.TryGetValue(evictKey, out var t))
            {
                _currentBytes -= t.EstimatedBytes;
                t.Dispose();
                _tiles.Remove(evictKey);
                _tilesEvictedLru++;
            }
            _lru.RemoveLast();
        }
    }

    public void Dispose()
    {
        foreach (var t in _tiles.Values) t.Dispose();
        _tiles.Clear();
        _lru.Clear();
        _currentBytes = 0;
        _recordedPicture?.Dispose();
        _recordedPicture = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long TileKey(int tx, int ty) => ((long)tx << 32) | (uint)ty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PageXToTile(float x) => (int)Math.Floor(x / _tileW);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PageYToTile(float y) => (int)Math.Floor(y / _tileH);
}
