using System.Collections.Concurrent;
using Acrux.Core.Css;

namespace Acrux.Core.Performance.Rendering;

/// <summary>
/// Fast selector-matching index. CSS rules are bucketed by their rightmost compound
/// (tag, class, id) which is what the browser uses as the entry point for matching.
/// At match time we only run the (relatively expensive) full selector test for rules
/// in the candidate buckets, dramatically reducing the work when matching against a
/// given element.
/// </summary>
public sealed class SelectorIndex
{
    // tag -> bucket
    private readonly ConcurrentDictionary<string, Bucket> _byTag = new(StringComparer.OrdinalIgnoreCase);
    // "tag.class" or "tag#id" -> bucket
    private readonly ConcurrentDictionary<string, Bucket> _byCompound = new(StringComparer.OrdinalIgnoreCase);
    // class-only bucket
    private readonly ConcurrentDictionary<string, Bucket> _byClass = new(StringComparer.OrdinalIgnoreCase);
    // id-only bucket
    private readonly ConcurrentDictionary<string, Bucket> _byId = new(StringComparer.OrdinalIgnoreCase);
    // rules that can't be classified (e.g. *, :root) — must always be tested
    private readonly ConcurrentBag<CssRule> _universal = new();

    private long _ruleCount;
    private long _indexedRuleCount;

    public long RuleCount => Interlocked.Read(ref _ruleCount);
    public long IndexedRuleCount => Interlocked.Read(ref _indexedRuleCount);
    public int UniversalRuleCount => _universal.Count;

    public void Add(CssRule rule)
    {
        Interlocked.Increment(ref _ruleCount);
        var key = ClassifyRule(rule);
        if (key is null)
        {
            _universal.Add(rule);
            return;
        }
        Interlocked.Increment(ref _indexedRuleCount);
        var bucket = _byCompound.GetOrAdd(key, _ => new Bucket());
        bucket.Add(rule);
    }

    public IEnumerable<CssRule> GetCandidates(string? tag, IReadOnlyList<string> classes, string? id, bool includeUniversal = true)
    {
        // 1. Highest specificity: compound selectors like "div.foo"
        if (tag is not null)
        {
            foreach (var cls in classes)
            {
                var key = tag + "." + cls;
                if (_byCompound.TryGetValue(key, out var b))
                    foreach (var r in b.Snapshot()) yield return r;
            }
            if (id is not null)
            {
                var key = tag + "#" + id;
                if (_byCompound.TryGetValue(key, out var b))
                    foreach (var r in b.Snapshot()) yield return r;
            }
        }

        // 2. Class-only
        foreach (var cls in classes)
        {
            if (_byClass.TryGetValue(cls, out var b))
                foreach (var r in b.Snapshot()) yield return r;
        }

        // 3. Id-only
        if (id is not null && _byId.TryGetValue(id, out var bid))
            foreach (var r in bid.Snapshot()) yield return r;

        // 4. Tag-only
        if (tag is not null && _byTag.TryGetValue(tag, out var bt))
            foreach (var r in bt.Snapshot()) yield return r;

        // 5. Universal
        if (includeUniversal)
            foreach (var r in _universal) yield return r;
    }

    public void Clear()
    {
        _byTag.Clear();
        _byCompound.Clear();
        _byClass.Clear();
        _byId.Clear();
        _universal.Clear();
        Interlocked.Exchange(ref _ruleCount, 0);
        Interlocked.Exchange(ref _indexedRuleCount, 0);
    }

    private static string? ClassifyRule(CssRule rule)
    {
        var selector = rule.Selector;
        if (string.IsNullOrEmpty(selector)) return null;
        if (selector.Contains('*')) return null;          // universal — index on tag only
        if (selector.Contains(':')) return null;          // pseudo-class — defer
        if (selector.Contains(' ')) return null;          // descendant — composite, defer
        if (selector.Contains('>')) return null;
        if (selector.Contains('+')) return null;
        if (selector.Contains('~')) return null;
        if (selector.Contains('[')) return null;
        return selector.ToLowerInvariant();
    }

    private sealed class Bucket
    {
        private readonly List<CssRule> _rules = new();
        private readonly object _lock = new();
        public void Add(CssRule r) { lock (_lock) _rules.Add(r); }
        public IReadOnlyList<CssRule> Snapshot() { lock (_lock) return _rules.ToArray(); }
    }
}
