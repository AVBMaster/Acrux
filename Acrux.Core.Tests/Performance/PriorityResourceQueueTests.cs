using Acrux.Core.Performance.Resources;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class PriorityResourceQueueTests
{
    [Fact]
    public void HighPriority_DequeuedFirst()
    {
        var q = new PriorityResourceQueue();
        q.Enqueue(Make("a", ResourcePriority.Low));
        q.Enqueue(Make("b", ResourcePriority.High));
        q.Enqueue(Make("c", ResourcePriority.Medium));
        Assert.Equal("b", q.Dequeue()!.Url);
        Assert.Equal("c", q.Dequeue()!.Url);
        Assert.Equal("a", q.Dequeue()!.Url);
    }

    [Fact]
    public void DuplicateEnqueue_IsAvoided()
    {
        var q = new PriorityResourceQueue();
        Assert.True(q.Enqueue(Make("dup", ResourcePriority.High)));
        Assert.False(q.Enqueue(Make("dup", ResourcePriority.High)));
        Assert.Equal(1, q.DuplicatesAvoided);
    }

    [Fact]
    public void Dequeue_OnEmpty_ReturnsNull()
    {
        var q = new PriorityResourceQueue();
        Assert.Null(q.Dequeue());
    }

    [Fact]
    public void FIFO_WithinSamePriority()
    {
        var q = new PriorityResourceQueue();
        q.Enqueue(Make("a", ResourcePriority.Medium));
        q.Enqueue(Make("b", ResourcePriority.Medium));
        q.Enqueue(Make("c", ResourcePriority.Medium));
        Assert.Equal("a", q.Dequeue()!.Url);
        Assert.Equal("b", q.Dequeue()!.Url);
        Assert.Equal("c", q.Dequeue()!.Url);
    }

    [Fact]
    public void MaxConcurrent_IsReadWrite()
    {
        var q = new PriorityResourceQueue();
        q.MaxConcurrent = 12;
        Assert.Equal(12, q.MaxConcurrent);
    }

    private static ResourceRequest Make(string url, ResourcePriority p) =>
        new() { Url = url, Kind = ResourceKind.Image, Priority = p };
}

