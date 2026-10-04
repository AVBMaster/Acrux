using System.Collections.Concurrent;

namespace Acrux.Core.Performance.Resources;

/// <summary>
/// In-memory and disk-aware cache for fetched resources. Provides bounded capacity with
/// an LRU eviction policy, weighted by the byte size of each entry. Smaller resources
/// are preferred for retention when memory is tight.
/// </summary>
public sealed class ResourceCache
{
    private sealed class Node
    {
        public required string Url { get; init; }
        public required ResourceResponse Response { get; init; }
        public LinkedListNode<string> LruLink = null!;
        public long Size;
    }

    private readonly Dictionary<string, Node> _entries = new();
    private readonly LinkedList<string> _lru = new();
    private long _totalBytes;
    private long _hits;
    private long _misses;
    private long _evictions;
    private long _currentCapacity = 64L * 1024 * 1024; // 64 MB default

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Evictions => Interlocked.Read(ref _evictions);
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public long CapacityBytes => Interlocked.Read(ref _currentCapacity);
    public int Count => _entries.Count;
    public double HitRate => (_hits + _misses) == 0 ? 0 : (double)_hits / (_hits + _misses);

    public void SetCapacity(long bytes) => Interlocked.Exchange(ref _currentCapacity, Math.Max(0, bytes));

    public bool TryGet(string url, out ResourceResponse? response)
    {
        lock (_entries)
        {
            if (_entries.TryGetValue(url, out var node))
            {
                _lru.Remove(node.LruLink);
                node.LruLink = _lru.AddFirst(node.Url);
                Interlocked.Increment(ref _hits);
                response = node.Response;
                return true;
            }
        }
        Interlocked.Increment(ref _misses);
        response = null;
        return false;
    }

    public void Put(string url, ResourceResponse response)
    {
        long size = response.Body.LongLength;
        if (size > CapacityBytes)
        {
            // Single resource exceeds total budget — store anyway, but evict everything else.
            lock (_entries) { _entries.Clear(); _lru.Clear(); }
            Interlocked.Exchange(ref _totalBytes, 0);
        }
        lock (_entries)
        {
            if (_entries.TryGetValue(url, out var existing))
            {
                _lru.Remove(existing.LruLink);
                _entries.Remove(url);
                Interlocked.Add(ref _totalBytes, -existing.Size);
            }
            var lruNode = _lru.AddFirst(url);
            var node = new Node { Url = url, Response = response, LruLink = lruNode, Size = size };
            _entries[url] = node;
            Interlocked.Add(ref _totalBytes, size);
            EvictIfNeeded_NoLock();
        }
    }

    public void Remove(string url)
    {
        lock (_entries)
        {
            if (_entries.TryGetValue(url, out var node))
            {
                _lru.Remove(node.LruLink);
                _entries.Remove(url);
                Interlocked.Add(ref _totalBytes, -node.Size);
            }
        }
    }

    public void Clear()
    {
        lock (_entries) { _entries.Clear(); _lru.Clear(); }
        Interlocked.Exchange(ref _totalBytes, 0);
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Interlocked.Exchange(ref _evictions, 0);
    }

    private void EvictIfNeeded_NoLock()
    {
        var cap = CapacityBytes;
        while (TotalBytes > cap && _lru.Count > 1)
        {
            var tail = _lru.Last;
            if (tail is null) break;
            if (_entries.TryGetValue(tail.Value, out var node))
            {
                _lru.Remove(tail);
                _entries.Remove(node.Url);
                Interlocked.Add(ref _totalBytes, -node.Size);
                Interlocked.Increment(ref _evictions);
            }
        }
    }
}
