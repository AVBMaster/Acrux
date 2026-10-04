using System.Collections.Concurrent;

namespace Acrux.Core.Performance.Resources;

/// <summary>
/// Priority-aware request queue. <see cref="Enqueue"/> is thread-safe; <see cref="Dequeue"/>
/// returns the highest-priority pending request. FNV-1a hash of the URL is used to
/// provide stable secondary ordering, ensuring that an in-flight request is preferred
/// over a duplicate that arrives a moment later.
/// </summary>
public sealed class PriorityResourceQueue
{
    private readonly ConcurrentDictionary<string, Entry> _byUrl = new();
    private readonly SortedSet<Entry> _ordered = new();
    private long _enqueued;
    private long _dequeued;
    private long _duplicatesAvoided;
    private long _maxConcurrent = 6;

    public long Enqueued => Interlocked.Read(ref _enqueued);
    public long Dequeued => Interlocked.Read(ref _dequeued);
    public long DuplicatesAvoided => Interlocked.Read(ref _duplicatesAvoided);
    public int PendingCount => _byUrl.Count;

    public int MaxConcurrent
    {
        get => (int)Interlocked.Read(ref _maxConcurrent);
        set => Interlocked.Exchange(ref _maxConcurrent, value);
    }

    public bool Enqueue(ResourceRequest request)
    {
        if (_byUrl.ContainsKey(request.Url))
        {
            Interlocked.Increment(ref _duplicatesAvoided);
            return false;
        }
        var entry = new Entry
        {
            Request = request,
            Priority = request.Priority,
            Seq = Interlocked.Increment(ref _enqueued),
            UrlHash = FnvHash(request.Url),
        };
        if (!_byUrl.TryAdd(request.Url, entry)) return false;
        lock (_ordered) _ordered.Add(entry);
        return true;
    }

    public ResourceRequest? Dequeue()
    {
        Entry? entry = null;
        lock (_ordered)
        {
            if (_ordered.Count == 0) return null;
            entry = _ordered.Min;
            _ordered.Remove(entry);
        }
        if (entry is null) return null;
        if (!_byUrl.TryRemove(entry.Request.Url, out _)) return null;
        Interlocked.Increment(ref _dequeued);
        return entry.Request;
    }

    public bool Remove(string url) => _byUrl.TryRemove(url, out _);

    public void Clear()
    {
        _byUrl.Clear();
        lock (_ordered) _ordered.Clear();
    }

    private static ulong FnvHash(string s)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        ulong h = fnvOffset;
        for (int i = 0; i < s.Length; i++)
        {
            h ^= s[i];
            h *= fnvPrime;
        }
        return h;
    }

    private sealed class Entry : IComparable<Entry>
    {
        public required ResourceRequest Request { get; init; }
        public required ResourcePriority Priority { get; init; }
        public required long Seq { get; init; }
        public required ulong UrlHash { get; init; }

        public int CompareTo(Entry? other)
        {
            if (other is null) return -1;
            int p = ((byte)Priority).CompareTo((byte)other.Priority);
            if (p != 0) return p;
            int s = Seq.CompareTo(other.Seq);
            if (s != 0) return s;
            return UrlHash.CompareTo(other.UrlHash);
        }
    }
}
