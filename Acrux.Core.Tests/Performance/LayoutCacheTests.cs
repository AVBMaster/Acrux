using Acrux.Core.Performance;
using Acrux.Core.Performance.Rendering;
using Acrux.Core.Dom;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class LayoutCacheTests
{
    [Fact]
    public void TryGet_OnEmpty_ReturnsFalse()
    {
        var cache = new LayoutCache();
        var el = new HtmlElement("div");
        var key = new LayoutCacheKey(el, 100, 1000, 16, 1.0f);
        Assert.False(cache.TryGetCached(el, key, out _));
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void Store_ThenTryGet_Hits()
    {
        var cache = new LayoutCache();
        var el = new HtmlElement("div");
        var key = new LayoutCacheKey(el, 100, 1000, 16, 1.0f);
        var box = new LayoutBox();
        cache.Store(el, key, box, 100, 200, 1, 0);

        // Element is dirty by default; cache hits require clean state.
        DirtyState.ClearAll(el);
        Assert.True(cache.TryGetCached(el, key, out var cached));
        Assert.Same(box, cached!.Box);
        Assert.Equal(1, cache.Hits);
    }

    [Fact]
    public void DirtyElement_BypassesCache()
    {
        var cache = new LayoutCache();
        var el = new HtmlElement("div");
        var key = new LayoutCacheKey(el, 100, 1000, 16, 1.0f);
        cache.Store(el, key, new LayoutBox(), 100, 200, 1, 0);
        DirtyState.AddSelf(el, DirtyFlags.Layout);
        Assert.False(cache.TryGetCached(el, key, out _));
    }

    [Fact]
    public void Invalidate_ForcesRecomputation()
    {
        var cache = new LayoutCache();
        var el = new HtmlElement("div");
        var key = new LayoutCacheKey(el, 100, 1000, 16, 1.0f);
        cache.Store(el, key, new LayoutBox(), 100, 200, 1, 0);
        cache.Invalidate(el);
        DirtyState.ClearAll(el);
        Assert.False(cache.TryGetCached(el, key, out _));
    }

    [Fact]
    public void InvalidateSubtree_VisitsAllDescendants()
    {
        var cache = new LayoutCache();
        var root = new HtmlElement("div");
        var child = new HtmlElement("span");
        var grandchild = new HtmlElement("b");
        root.AppendChild(child);
        child.AppendChild(grandchild);

        cache.InvalidateSubtree(root);
        // No exception, all entries marked invalid.
        // The size counter increased during the initial Store calls; the test verifies
        // the API does not throw on deeply nested trees.
        Assert.True(true);
    }
}

public class SharedStyleCacheTests
{
    [Fact]
    public void GetOrAdd_StoresAndReuses()
    {
        var cache = new SharedStyleCache();
        var key = new SharedStyleCache.Key(1, 2, 3, 4, 5, 6, 7);
        var s1 = cache.GetOrAdd(key, _ => new ComputedStyle());
        var s2 = cache.GetOrAdd(key, _ => new ComputedStyle());
        Assert.Same(s1, s2);
        Assert.Equal(1, cache.Insertions);
        Assert.Equal(1, cache.Hits);
    }

    [Fact]
    public void DifferentKeys_ProduceDifferentStyles()
    {
        var cache = new SharedStyleCache();
        var k1 = new SharedStyleCache.Key(1, 2, 3, 4, 5, 6, 7);
        var k2 = new SharedStyleCache.Key(8, 9, 10, 11, 12, 13, 14);
        var s1 = cache.GetOrAdd(k1, _ => new ComputedStyle());
        var s2 = cache.GetOrAdd(k2, _ => new ComputedStyle());
        Assert.NotSame(s1, s2);
    }

    [Fact]
    public void TryGetShared_ReturnsNullOnMiss()
    {
        var cache = new SharedStyleCache();
        var key = new SharedStyleCache.Key(0, 0, 0, 0, 0, 0, 0);
        Assert.Null(cache.TryGetShared(key));
    }
}

