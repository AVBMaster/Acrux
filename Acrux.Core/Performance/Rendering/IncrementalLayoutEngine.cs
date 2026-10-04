using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.Core.Performance;

namespace Acrux.Core.Performance.Rendering;

/// <summary>
/// Statistics exposed by <see cref="IncrementalLayoutEngine"/>: how many nodes were
/// actually re-laid-out, how many were skipped thanks to the cache, etc. The diagnostics
/// UI consumes these to show "Layout work avoided" panels.
/// </summary>
public sealed class LayoutStats
{
    private int _nodesVisited;
    private int _nodesSkipped;
    private int _nodesRelaid;
    private int _cacheHits;
    private int _cacheMisses;
    private long _elapsedNanos;
    private int _dirtyRoots;

    public int NodesVisited => _nodesVisited;
    public int NodesSkipped => _nodesSkipped;
    public int NodesReLaid => _nodesRelaid;
    public int CacheHits => _cacheHits;
    public int CacheMisses => _cacheMisses;
    public long ElapsedNanos => _elapsedNanos;
    public double ElapsedMillis => _elapsedNanos / 1_000_000.0;
    public int DirtyRoots => _dirtyRoots;

    public double SkipRatio =>
        _nodesVisited == 0 ? 0 : (double)_nodesSkipped / _nodesVisited;

    public void Reset()
    {
        _nodesVisited = _nodesSkipped = _nodesRelaid = 0;
        _cacheHits = _cacheMisses = 0;
        _elapsedNanos = 0;
        _dirtyRoots = 0;
    }

    internal void IncVisited() => _nodesVisited++;
    internal void IncSkipped() => _nodesSkipped++;
    internal void IncRelaid() => _nodesRelaid++;
    internal void IncHit() => _cacheHits++;
    internal void IncMiss() => _cacheMisses++;
    internal void IncDirtyRoot() => _dirtyRoots++;
    internal void AddElapsed(long ns) => _elapsedNanos += ns;
}

/// <summary>
/// The runtime layout orchestrator used by the live browser. It wraps a
/// <see cref="LayoutEngine"/> and reports layout statistics for the diagnostics
/// UI. There is exactly one layout path — the modern pipeline of the wrapped
/// engine — so live frames and headless captures always produce identical boxes.
/// </summary>
public sealed class IncrementalLayoutEngine
{
    private readonly LayoutEngine _base;
    public LayoutCache Cache { get; }
    public LayoutStats Stats { get; } = new();

    public IncrementalLayoutEngine(LayoutEngine baseEngine, LayoutCache? cache = null)
    {
        _base = baseEngine ?? throw new ArgumentNullException(nameof(baseEngine));
        Cache = cache ?? new LayoutCache();
    }

    /// <summary>
    /// Run a full layout pass. Delegates to the wrapped engine's modern pipeline
    /// (the same entry headless snapshots use).
    /// </summary>
    public void Layout(Document document, float width, float height, float dpiScale = 1.0f, float rootFontSize = 16f)
    {
        Stats.Reset();
        var sw = Clock.NowNanos();
        var root = document.DocumentElement ?? document.Body;
        if (root == null) { Stats.AddElapsed(Clock.NowNanos() - sw); return; }

        // Unified pipeline: delegate to the wrapped engine's own full pass.
        // The viewport state is established inside the engine itself.
        _base.SyncPipelineState(width, height, dpiScale, rootFontSize);
        _base.Layout(document, width, height, dpiScale);

        // The modern pass walks the whole tree fresh; report that honestly instead
        // of pretending cache skips happened.
        int subtree = CountSubtree(root);
        Stats.IncDirtyRoot();
        Stats.IncVisited();
        Stats.IncRelaid();
        Stats.IncMiss();
        Stats.AddElapsed(Clock.NowNanos() - sw);
        _ = subtree; // subtree size kept for future partial-relayout reporting
    }

    /// <summary>
    /// Mark a subtree as needing relayout. Use this after DOM mutations or style updates.
    /// </summary>
    public void InvalidateSubtree(Element root)
    {
        Cache.InvalidateSubtree(root);
        foreach (var child in root.Children)
        {
            if (child is Element ce)
            {
                DirtyState.AddSelf(ce, DirtyFlags.AllLayout);
                DirtyState.AddChildren(ce, DirtyFlags.AllLayout);
            }
        }
        DirtyState.AddSelf(root, DirtyFlags.AllLayout);
        DirtyState.AddChildren(root, DirtyFlags.AllLayout);
    }

    /// <summary>
    /// Mark a single element as needing style recomputation.
    /// </summary>
    public void InvalidateStyle(Element element)
    {
        DirtyState.AddSelf(element, DirtyFlags.Style);
        DirtyState.AddChildren(element, DirtyFlags.ChildrenStyle);
        DirtyState.BumpStyleVersion(element);
        Cache.Invalidate(element);
    }

    private static int CountSubtree(Element element)
    {
        int n = 1;
        foreach (var child in element.Children)
            if (child is Element ce) n += CountSubtree(ce);
        return n;
    }
}
