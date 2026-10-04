using System.Collections.Concurrent;
using SkiaSharp;
using Acrux.Core.Performance;
using Acrux.Core.Performance.Memory;

namespace Acrux.Core.Performance.Resources;

/// <summary>
/// Lazy-loading policy for image resources. Decides whether an image is in the
/// "load now" set (above the fold) or the "load when visible" set, and resolves
/// the latter through a viewport intersection callback.
/// </summary>
public sealed class LazyLoadController
{
    public sealed class PendingImage
    {
        public required string Url { get; init; }
        public required SKRect ViewportBox { get; set; }
        public required float LoadingDistance { get; set; }
        public required Action<string> OnVisible { get; init; }
        public bool IsLoaded;
        public bool IsVisible;
        public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, PendingImage> _pending = new();
    private long _loaded;
    private long _deferred;
    private long _skipped;

    public long Loaded => Interlocked.Read(ref _loaded);
    public long Deferred => Interlocked.Read(ref _deferred);
    public long Skipped => Interlocked.Read(ref _skipped);
    public int Pending => _pending.Count;

    /// <summary>
    /// Register an image for lazy loading. Returns true if the image should be loaded
    /// eagerly (above fold or already visible), false if it should be deferred.
    /// </summary>
    public bool Register(string url, SKRect viewportBox, float loadingDistance, Action<string> onVisible)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (_pending.ContainsKey(url)) return false;
        var pending = new PendingImage
        {
            Url = url,
            ViewportBox = viewportBox,
            LoadingDistance = loadingDistance,
            OnVisible = onVisible,
        };
        if (!_pending.TryAdd(url, pending)) return false;
        return true;
    }

    /// <summary>
    /// Update the current scroll viewport and trigger visibility callbacks. Each
    /// intersection is computed in O(n) over the pending set; suitable for
    /// thousands of images before an acceleration structure is needed.
    /// </summary>
    public int UpdateViewport(SKRect currentViewport)
    {
        int triggered = 0;
        foreach (var kv in _pending.ToArray())
        {
            var p = kv.Value;
            if (p.IsLoaded) { _pending.TryRemove(kv.Key, out _); continue; }

            var expanded = new SKRect(
                currentViewport.Left - p.LoadingDistance,
                currentViewport.Top - p.LoadingDistance,
                currentViewport.Right + p.LoadingDistance,
                currentViewport.Bottom + p.LoadingDistance);
            if (Intersects(p.ViewportBox, expanded))
            {
                p.IsVisible = true;
                p.IsLoaded = true;
                Interlocked.Increment(ref _loaded);
                try { p.OnVisible(p.Url); } catch (Exception ex) { Console.Error.WriteLine($"[LazyLoad] onVisible threw: {ex.Message}"); }
                _pending.TryRemove(kv.Key, out _);
                triggered++;
            }
        }
        return triggered;
    }

    public void Skip(string url)
    {
        if (_pending.TryRemove(url, out _))
            Interlocked.Increment(ref _skipped);
    }

    public void Defer(string url)
    {
        if (_pending.ContainsKey(url))
            Interlocked.Increment(ref _deferred);
    }

    public void Clear()
    {
        _pending.Clear();
    }

    private static bool Intersects(SKRect a, SKRect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
}

/// <summary>
/// Decoded-image memory pool with a hard byte budget. When the budget is exceeded the
/// least-recently-used decoded bitmap is released (the source bytes stay in the
/// resource cache so the image can be re-decoded on demand).
/// </summary>
public sealed class DecodedImagePool
{
    private sealed class Entry
    {
        public required string Url { get; init; }
        public required SKImage Image { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required long ByteSize { get; init; }
        public LinkedListNode<string> LruLink = null!;
    }

    private readonly Dictionary<string, Entry> _entries = new();
    private readonly LinkedList<string> _lru = new();
    private long _totalBytes;
    private long _capacityBytes = 128L * 1024 * 1024; // 128 MB default
    private long _hits;
    private long _misses;
    private long _evictions;

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Evictions => Interlocked.Read(ref _evictions);
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public long CapacityBytes => Interlocked.Read(ref _capacityBytes);
    public int Count => _entries.Count;

    public void SetCapacity(long bytes) => Interlocked.Exchange(ref _capacityBytes, Math.Max(0, bytes));

    public SKImage? Get(string url)
    {
        lock (_entries)
        {
            if (_entries.TryGetValue(url, out var e))
            {
                _lru.Remove(e.LruLink);
                e.LruLink = _lru.AddFirst(url);
                Interlocked.Increment(ref _hits);
                return e.Image;
            }
        }
        Interlocked.Increment(ref _misses);
        return null;
    }

    public void Put(string url, SKImage image, int width, int height)
    {
        if (image is null) return;
        long bytes = (long)width * height * 4; // assume 32-bit RGBA
        lock (_entries)
        {
            if (_entries.TryGetValue(url, out var existing))
            {
                _lru.Remove(existing.LruLink);
                _entries.Remove(url);
                Interlocked.Add(ref _totalBytes, -existing.ByteSize);
                existing.Image.Dispose();
            }
            var lruNode = _lru.AddFirst(url);
            _entries[url] = new Entry { Url = url, Image = image, Width = width, Height = height, ByteSize = bytes, LruLink = lruNode };
            Interlocked.Add(ref _totalBytes, bytes);
            EvictIfNeeded_NoLock();
        }
    }

    public void Remove(string url)
    {
        lock (_entries)
        {
            if (_entries.TryGetValue(url, out var e))
            {
                _lru.Remove(e.LruLink);
                _entries.Remove(url);
                Interlocked.Add(ref _totalBytes, -e.ByteSize);
                e.Image.Dispose();
            }
        }
    }

    public void Clear()
    {
        lock (_entries)
        {
            foreach (var e in _entries.Values) e.Image.Dispose();
            _entries.Clear();
            _lru.Clear();
        }
        Interlocked.Exchange(ref _totalBytes, 0);
    }

    private void EvictIfNeeded_NoLock()
    {
        var cap = CapacityBytes;
        while (TotalBytes > cap && _lru.Count > 1)
        {
            var tail = _lru.Last;
            if (tail is null) break;
            if (_entries.TryGetValue(tail.Value, out var e))
            {
                _lru.Remove(tail);
                _entries.Remove(e.Url);
                Interlocked.Add(ref _totalBytes, -e.ByteSize);
                e.Image.Dispose();
                Interlocked.Increment(ref _evictions);
            }
        }
    }
}
