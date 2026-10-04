using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.Core.Performance;

namespace Acrux.Core.Performance.Rendering;

/// <summary>
/// Layout cache key: identifies a unique (element, viewport-width, dpi, root-font-size)
/// layout context. A cached layout result is only reused when the key matches AND
/// the element is not marked dirty.
/// </summary>
public readonly struct LayoutCacheKey : IEquatable<LayoutCacheKey>
{
    public readonly int ElementId;
    public readonly float AvailableWidth;
    public readonly float ViewportWidth;
    public readonly float RootFontSize;
    public readonly float DpiScale;
    public readonly int StyleVersion;

    public LayoutCacheKey(Element element, float availableWidth, float viewportWidth, float rootFontSize, float dpiScale)
    {
        ElementId = element.GetHashCode();
        AvailableWidth = availableWidth;
        ViewportWidth = viewportWidth;
        RootFontSize = rootFontSize;
        DpiScale = dpiScale;
        StyleVersion = (int)Math.Min(int.MaxValue, DirtyState.GetStyleVersion(element));
    }

    public bool Equals(LayoutCacheKey other) =>
        ElementId == other.ElementId &&
        AvailableWidth.Equals(other.AvailableWidth) &&
        ViewportWidth.Equals(other.ViewportWidth) &&
        RootFontSize.Equals(other.RootFontSize) &&
        DpiScale.Equals(other.DpiScale) &&
        StyleVersion == other.StyleVersion;

    public override bool Equals(object? obj) => obj is LayoutCacheKey k && Equals(k);
    public override int GetHashCode() => HashCode.Combine(ElementId, AvailableWidth, ViewportWidth, RootFontSize, DpiScale, StyleVersion);
}

/// <summary>
/// Cached geometry for a single element. Stored on a sidecar so we do not have to mutate
/// the public LayoutBox type. Consumers should read <see cref="Box"/> when the cache is
/// valid for the given key.
/// </summary>
public sealed class CachedLayout
{
    public LayoutBox Box = null!;
    public LayoutCacheKey Key;
    public float ContentHeight;
    public float ContentWidth;
    public int SubtreeNodeCount;
    public int LineRunCount;
    public long CreatedAtNanos;
    public bool IsValid;
}

/// <summary>
/// Sidecar cache that stores per-element layout results. Keeps the existing
/// <see cref="LayoutBox"/> API intact; optimisation is opt-in by checking
/// <see cref="TryGetCached"/> before laying out.
/// </summary>
public sealed class LayoutCache
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Element, CachedLayout> _table = new();
    private long _hits;
    private long _misses;
    private long _evictions;
    private long _size;

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Evictions => Interlocked.Read(ref _evictions);
    public long Size => Interlocked.Read(ref _size);

    public double HitRate => (_hits + _misses) == 0 ? 0 : (double)_hits / (_hits + _misses);

    public bool TryGetCached(Element element, in LayoutCacheKey key, out CachedLayout? cached)
    {
        if (DirtyState.IsClean(element) && _table.TryGetValue(element, out cached!))
        {
            if (cached!.IsValid && cached.Key.Equals(key))
            {
                Interlocked.Increment(ref _hits);
                return true;
            }
        }
        Interlocked.Increment(ref _misses);
        cached = null;
        return false;
    }

    public CachedLayout Store(Element element, in LayoutCacheKey key, LayoutBox box, float contentWidth, float contentHeight, int subtreeNodes, int lineRuns)
    {
        var cached = _table.GetOrCreateValue(element);
        cached.Box = box;
        cached.Key = key;
        cached.ContentWidth = contentWidth;
        cached.ContentHeight = contentHeight;
        cached.SubtreeNodeCount = subtreeNodes;
        cached.LineRunCount = lineRuns;
        cached.CreatedAtNanos = Clock.NowNanos();
        cached.IsValid = true;
        Interlocked.Increment(ref _size);
        return cached;
    }

    public void Invalidate(Element element)
    {
        if (_table.TryGetValue(element, out var cached))
        {
            if (cached.IsValid) Interlocked.Increment(ref _evictions);
            cached.IsValid = false;
        }
    }

    public void InvalidateSubtree(Element root)
    {
        // Mark the element and all descendants as needing layout work by clearing dirty
        // state on cache. The dirty flag system will be used in the next layout pass.
        Invalidate(root);
        foreach (var child in root.Children)
        {
            if (child is Element ce) InvalidateSubtree(ce);
        }
    }

    public void Clear()
    {
        // ConditionalWeakTable cannot be cleared in bulk; the table is weakly keyed so
        // orphaned entries are reclaimed when the element is collected. We just reset
        // statistics here.
        Interlocked.Exchange(ref _size, 0);
    }
}
