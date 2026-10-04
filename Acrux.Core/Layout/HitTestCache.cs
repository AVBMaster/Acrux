namespace Acrux.Core.Layout;

/// <summary>
/// A cache entry storing a hit test result for a given location.
/// Mirrors HitTestCacheEntry in hit_test_cache.h.
/// </summary>
public class HitTestCacheEntry
{
    public HitTestLocation Location { get; set; } = new();
    public HitTestResult Result { get; set; } = new();
}

/// <summary>
/// A small cache for successful hit tests to DOM nodes in the visible viewport.
/// Cleared on DOM modifications, scrolling, CSS style changes.
/// Mirrors HitTestCache in hit_test_cache.h (simplified, class-based entries).
/// </summary>
public class HitTestCache
{
    private const int CacheSize = 2;

    private readonly HitTestCacheEntry[] _entries = new HitTestCacheEntry[CacheSize];
    private int _updateIndex;
    private ulong _domTreeVersion;

    public HitTestCache()
    {
        for (int i = 0; i < CacheSize; i++)
            _entries[i] = new HitTestCacheEntry();
    }

    public bool LookupCachedResult(HitTestLocation location, ref HitTestResult hitResult, ulong domTreeVersion)
    {
        if (domTreeVersion != _domTreeVersion || location.IsRectBased)
            return false;

        for (int i = 0; i < _entries.Length; i++)
        {
            var entry = _entries[i];
            if (entry.Location.Point.Left == location.Point.Left &&
                entry.Location.Point.Top == location.Point.Top)
            {
                var cachedReq = entry.Result.Request;
                var currentReq = hitResult.Request;
                if (cachedReq.IsMove == currentReq.IsMove &&
                    cachedReq.IsRelease == currentReq.IsRelease &&
                    cachedReq.IsActive == currentReq.IsActive &&
                    cachedReq.AllowChildFrameContent == currentReq.AllowChildFrameContent)
                {
                    hitResult = entry.Result;
                    return true;
                }
            }
        }
        return false;
    }

    public void AddCachedResult(HitTestLocation location, HitTestResult result, ulong domTreeVersion)
    {
        if (result.InnerNode == null)
            return;

        if (location.IsRectBased)
            return;

        if (domTreeVersion != _domTreeVersion)
            Clear();

        if (_updateIndex >= CacheSize)
            _updateIndex = 0;

        _entries[_updateIndex].Location = location;
        _entries[_updateIndex].Result = result;
        _domTreeVersion = domTreeVersion;
        _updateIndex++;
    }

    public void Clear()
    {
        _updateIndex = 0;
        for (int i = 0; i < CacheSize; i++)
            _entries[i] = new HitTestCacheEntry();
    }
}