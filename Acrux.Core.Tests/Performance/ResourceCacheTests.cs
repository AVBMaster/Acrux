using Acrux.Core.Performance.Resources;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class ResourceCacheTests
{
    private static ResourceResponse MakeResponse(string body, string contentType = "image/png")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        return new ResourceResponse
        {
            StatusCode = 200,
            Body = bytes,
            ContentType = contentType,
            ContentLength = bytes.LongLength,
        };
    }

    [Fact]
    public void Put_ThenGet_ReturnsCached()
    {
        var cache = new ResourceCache();
        cache.Put("u1", MakeResponse("hello"));
        Assert.True(cache.TryGet("u1", out var r));
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(r!.Body));
        Assert.Equal(1, cache.Hits);
    }

    [Fact]
    public void MissingKey_IsMiss()
    {
        var cache = new ResourceCache();
        Assert.False(cache.TryGet("missing", out _));
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void Eviction_WhenOverCapacity()
    {
        var cache = new ResourceCache();
        cache.SetCapacity(1024);
        cache.Put("big", new ResourceResponse { Body = new byte[2048], ContentLength = 2048 });
        // Oversized entry should evict everything else
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void LRU_EvictsLeastRecentlyUsed()
    {
        var cache = new ResourceCache();
        cache.SetCapacity(100);
        for (int i = 0; i < 10; i++) cache.Put("k" + i, new ResourceResponse { Body = new byte[20], ContentLength = 20 });

        // Access k0 to make it most-recent
        Assert.True(cache.TryGet("k0", out _));
        // Add one more to trigger eviction
        cache.Put("k10", new ResourceResponse { Body = new byte[20], ContentLength = 20 });
        // k1 should be evicted (it was the oldest unreferenced)
        Assert.False(cache.TryGet("k1", out _));
        Assert.True(cache.TryGet("k0", out _));
    }

    [Fact]
    public void Remove_ClearsEntry()
    {
        var cache = new ResourceCache();
        cache.Put("u", MakeResponse("data"));
        cache.Remove("u");
        Assert.False(cache.TryGet("u", out _));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new ResourceCache();
        cache.Put("a", MakeResponse("x"));
        cache.Put("b", MakeResponse("y"));
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task Put_OverwritesExisting()
    {
        var cache = new ResourceCache();
        cache.Put("u", MakeResponse("v1"));
        cache.Put("u", MakeResponse("v2"));
        Assert.True(cache.TryGet("u", out var r));
        Assert.Equal("v2", System.Text.Encoding.UTF8.GetString(r!.Body));
        Assert.Equal(1, cache.Count);
        await Task.CompletedTask;
    }
}
