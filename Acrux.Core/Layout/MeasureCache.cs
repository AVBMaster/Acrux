namespace Acrux.Core.Layout;

/// <summary>
/// An N-way LRU cache for "measure" layout results. Some layout algorithms
/// (grid in particular) measure an element multiple times with different
/// constraint spaces. Mirrors MeasureCache in measure_cache.h.
/// </summary>
public class MeasureCache
{
    public const int MaxCacheEntries = 8;

    private readonly List<LayoutResult> _cache = new();

    /// <summary>
    /// Find a cached layout result matching the given constraint space.
    /// Returns null if no match is found.
    /// </summary>
    public LayoutResult? Find(BlockNode node, ConstraintSpace newSpace)
    {
        for (int i = _cache.Count - 1; i >= 0; i--)
        {
            var result = _cache[i];
            if (CalculateSizeBasedLayoutCacheStatus(node, result, newSpace) == LayoutCacheStatus.Hit)
            {
                // Move to the back (most recently used).
                if (i != _cache.Count - 1)
                {
                    _cache.RemoveAt(i);
                    _cache.Add(result);
                }
                return result;
            }
        }
        return null;
    }

    public void Add(LayoutResult result)
    {
        if (_cache.Count >= MaxCacheEntries)
            _cache.RemoveAt(0);
        _cache.Add(result);
    }

    public void Clear()
    {
        _cache.Clear();
    }

    public LayoutResult? GetLastForTesting() =>
        _cache.Count > 0 ? _cache[^1] : null;

    private static LayoutCacheStatus CalculateSizeBasedLayoutCacheStatus(
        BlockNode node, LayoutResult cachedResult, ConstraintSpace newSpace)
    {
        // Simplified: match on available inline/block size + BFC offset.
        var cached = cachedResult.ConstraintSpaceForCaching;
        if (cached == null)
            return LayoutCacheStatus.NeedsLayout;

        var cs = cached.Value;
        if (cs.AvailableInlineSize == newSpace.AvailableInlineSize &&
            cs.AvailableBlockSize == newSpace.AvailableBlockSize &&
            cs.BfcBlockOffset == newSpace.BfcBlockOffset &&
            cs.IsNewFormattingContext == newSpace.IsNewFormattingContext)
        {
            return LayoutCacheStatus.Hit;
        }

        // Simplified layout may still be possible if only the BFC offset changed.
        if (cs.AvailableInlineSize == newSpace.AvailableInlineSize &&
            cs.AvailableBlockSize == newSpace.AvailableBlockSize)
        {
            return LayoutCacheStatus.NeedsSimplifiedLayout;
        }

        return LayoutCacheStatus.NeedsLayout;
    }
}