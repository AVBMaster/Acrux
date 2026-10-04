using Acrux.Core.Performance.Compositor;
using Acrux.Core.Performance.Diagnostics;
using Acrux.Core.Performance.Memory;
using Acrux.Core.Performance.Rendering;
using Acrux.Core.Performance.Resources;
using Acrux.Core.Performance.Scheduling;

namespace Acrux.Core.Performance;

/// <summary>
/// Single entry point that owns the optional performance sub-systems. Constructed
/// by the application host and passed down to the layout/style/render code. All
/// members are safe to access before <see cref="Initialize"/> has been called:
/// they are no-ops until then, so existing call sites do not need to branch.
/// </summary>
public sealed class PerformanceHub
{
    public static PerformanceHub Shared { get; } = new();

    public bool Enabled { get; private set; }
    public DateTime? InitializedAt { get; private set; }

    public CooperativeScheduler Scheduler { get; } = new();
    public LongTaskObserver LongTasks { get; } = new();
    public IdleCallbackScheduler Idle { get; } = new();
    public PerformanceRegistry Registry { get; }
    public PerformanceApi Api { get; }
    public SharedStyleCache StyleCache { get; } = new();
    public SelectorIndex SelectorIndex { get; } = new();
    public LayoutCache LayoutCache { get; } = new();
    public ResourceCache ResourceCache { get; } = new();
    public DecodedImagePool ImagePool { get; } = new();
    public LazyLoadController LazyLoad { get; } = new();
    public TileManager Tiles { get; } = new();
    public TileRasterizer Rasterizer { get; }
    public PredictiveTileScheduler PredictiveScheduler { get; }
    public CompositorReplayer CompositorReplayer { get; } = new();
    public MemoryBudget Budget { get; private set; } = new(2L * 1024 * 1024 * 1024);
    public AggregateMemoryResponder AggregateResponder { get; }

    public PerformanceHub()
    {
        Registry = new PerformanceRegistry(LongTasks);
        Api = new PerformanceApi(Registry);
        Rasterizer = new TileRasterizer(Tiles);
        PredictiveScheduler = new PredictiveTileScheduler(Tiles, Rasterizer);
        AggregateResponder = new AggregateMemoryResponder
        {
            Resources = ResourceCache,
            Images = ImagePool,
            Tiles = Tiles,
        };
        Registry.MemoryPressure.Register(AggregateResponder, priority: 100);
    }

    public void Initialize(MemoryBudget? budget = null)
    {
        if (Enabled) return;
        Enabled = true;
        InitializedAt = DateTime.UtcNow;
        if (budget is not null) Budget = budget;
        // Reasonable defaults for a 2GB-capable process.
        ResourceCache.SetCapacity(Budget.ResourceCacheSoftLimit);
        ImagePool.SetCapacity(Budget.ImagePoolSoftLimit);
        LongTasks.OnLongTask += OnLongTaskDetected;
    }

    public void Shutdown()
    {
        if (!Enabled) return;
        LongTasks.OnLongTask -= OnLongTaskDetected;
        Tiles.Clear();
        ImagePool.Clear();
        ResourceCache.Clear();
        StyleCache.Clear();
        LayoutCache.Clear();
        PipelineTimings.ResetAll();
        Enabled = false;
    }

    /// <summary>Drives the scheduler through one frame. Returns the work done in microseconds.</summary>
    public long RunFrame(CooperativeScheduler.FrameBudget budget)
    {
        if (!Enabled) return 0;
        var done = Scheduler.RunFrame(budget);
        var remaining = budget.TotalMillis - done / 1_000.0;
        if (remaining > 0) Idle.Run(remaining);
        return done;
    }

    private void OnLongTaskDetected(LongTaskObserver.LongTaskEntry entry)
    {
        // Update TBT per the W3C longtask definition: only the portion above 50 ms counts.
        var blockingMicros = (entry.DurationNanos / 1_000L) - 50_000L;
        if (blockingMicros > 0) Registry.Metrics.AddBlockingTime(blockingMicros * 1_000L);
        Registry.Feed.Append("longtask", $"{entry.Name} took {entry.DurationNanos / 1_000_000.0:F1} ms");
    }
}
