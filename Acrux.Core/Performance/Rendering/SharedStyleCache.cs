using System.Collections.Concurrent;
using Acrux.Core.Dom;

namespace Acrux.Core.Performance.Rendering;

/// <summary>
/// Cache for resolved <see cref="ComputedStyle"/> instances, keyed by the structural
/// signature of an element (tag + id + class-list + important attributes) and the
/// relevant viewport state. Two elements that share the same signature reuse the same
/// <see cref="ComputedStyle"/> instance, drastically reducing allocations on pages with
/// many similar list items / table rows.
/// </summary>
public sealed class SharedStyleCache
{
    public readonly struct Key : IEquatable<Key>
    {
        public readonly int TagHash;
        public readonly int IdHash;
        public readonly int ClassHash;
        public readonly int AttributeHash;
        public readonly int ViewportBucket;
        public readonly int StylesheetCount;
        public readonly int StateHash;

        public Key(int tagHash, int idHash, int classHash, int attributeHash,
                   int viewportBucket, int stylesheetCount, int stateHash)
        {
            TagHash = tagHash;
            IdHash = idHash;
            ClassHash = classHash;
            AttributeHash = attributeHash;
            ViewportBucket = viewportBucket;
            StylesheetCount = stylesheetCount;
            StateHash = stateHash;
        }

        public bool Equals(Key other) =>
            TagHash == other.TagHash &&
            IdHash == other.IdHash &&
            ClassHash == other.ClassHash &&
            AttributeHash == other.AttributeHash &&
            ViewportBucket == other.ViewportBucket &&
            StylesheetCount == other.StylesheetCount &&
            StateHash == other.StateHash;

        public override bool Equals(object? obj) => obj is Key k && Equals(k);

        public override int GetHashCode() =>
            HashCode.Combine(TagHash, IdHash, ClassHash, AttributeHash, ViewportBucket, StylesheetCount, StateHash);
    }

    private readonly ConcurrentDictionary<Key, Entry> _cache = new();
    private long _hits;
    private long _misses;
    private long _insertions;

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Insertions => Interlocked.Read(ref _insertions);
    public int Size => _cache.Count;
    public double HitRate => (_hits + _misses) == 0 ? 0 : (double)_hits / (_hits + _misses);

    private sealed class Entry
    {
        public ComputedStyle Style = null!;
        public int RefCount;
    }

    public ComputedStyle? TryGetShared(in Key key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            Interlocked.Increment(ref entry.RefCount);
            Interlocked.Increment(ref _hits);
            return entry.Style;
        }
        Interlocked.Increment(ref _misses);
        return null;
    }

    public ComputedStyle GetOrAdd(in Key key, Func<Key, ComputedStyle> factory)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            Interlocked.Increment(ref entry.RefCount);
            Interlocked.Increment(ref _hits);
            return entry.Style;
        }
        Interlocked.Increment(ref _misses);
        var style = factory(key);
        var newEntry = new Entry { Style = style, RefCount = 1 };
        // The factory could have inserted concurrently, in which case we adopt the
        // existing entry to avoid creating duplicate styles for the same key.
        var existing = _cache.GetOrAdd(key, newEntry);
        if (!ReferenceEquals(existing, newEntry))
        {
            Interlocked.Increment(ref existing.RefCount);
            return existing.Style;
        }
        Interlocked.Increment(ref _insertions);
        return style;
    }

    public void Clear()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Interlocked.Exchange(ref _insertions, 0);
    }
}

/// <summary>
/// Computes the structural signature of an element for shared-style lookup.
/// Pure function — no mutation of the element.
/// </summary>
public static class StyleSignature
{
    /// <summary>
    /// Build a signature from element identity (tag, id, classes, attributes that affect
    /// style) and viewport state. Two elements with the same signature can usually
    /// share a resolved style, but only if the surrounding context (ancestor style) also
    /// matches — the caller is responsible for the inheritance check.
    /// </summary>
    public static SharedStyleCache.Key ComputeSignature(Element element, int stylesheetCount, float viewportWidth, string colorScheme)
    {
        int tagHash = StringComparer.OrdinalIgnoreCase.GetHashCode(element.TagName ?? "");
        int idHash = element.Id is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(element.Id);
        int classHash = HashClasses(element.ClassList);

        // Attributes that affect style: srcset, type, href fragment, disabled, checked.
        int attrHash = 0;
        if (element.HasAttribute("disabled")) attrHash = HashCode.Combine(attrHash, "disabled");
        if (element.HasAttribute("checked")) attrHash = HashCode.Combine(attrHash, "checked");
        if (element.HasAttribute("type")) attrHash = HashCode.Combine(attrHash, "type", element.GetAttribute("type") ?? "");
        if (element.HasAttribute("href")) attrHash = HashCode.Combine(attrHash, "href-frag", GetFragment(element.GetAttribute("href") ?? ""));

        int viewportBucket = (int)Math.Round(viewportWidth / 16f);
        int stateHash = StringComparer.OrdinalIgnoreCase.GetHashCode(colorScheme ?? "light");
        return new SharedStyleCache.Key(tagHash, idHash, classHash, attrHash, viewportBucket, stylesheetCount, stateHash);
    }

    private static int HashClasses(string[] classes)
    {
        if (classes.Length == 0) return 0;
        // Order-independent hash so element.classList order changes do not bust the cache.
        int h = 0;
        for (int i = 0; i < classes.Length; i++)
            h ^= StringComparer.Ordinal.GetHashCode(classes[i]);
        return h;
    }

    private static string GetFragment(string href)
    {
        int idx = href.IndexOf('#');
        return idx < 0 ? "" : href[idx..];
    }
}
