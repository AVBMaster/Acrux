using Acrux.Core.Performance;
using Acrux.Core.Performance.Rendering;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class PerformanceHubTests
{
    [Fact]
    public void Hub_InitializesAndShutsDownCleanly()
    {
        var hub = new PerformanceHub();
        Assert.False(hub.Enabled);
        hub.Initialize();
        Assert.True(hub.Enabled);
        hub.Shutdown();
        Assert.False(hub.Enabled);
    }

    [Fact]
    public void Hub_DefaultsSubSystemsAreWired()
    {
        var hub = new PerformanceHub();
        Assert.NotNull(hub.Scheduler);
        Assert.NotNull(hub.Registry);
        Assert.NotNull(hub.Api);
        Assert.NotNull(hub.StyleCache);
        Assert.NotNull(hub.SelectorIndex);
        Assert.NotNull(hub.LayoutCache);
        Assert.NotNull(hub.ResourceCache);
        Assert.NotNull(hub.ImagePool);
        Assert.NotNull(hub.LazyLoad);
        Assert.NotNull(hub.Tiles);
        Assert.NotNull(hub.Rasterizer);
        Assert.NotNull(hub.PredictiveScheduler);
        Assert.NotNull(hub.CompositorReplayer);
    }

    [Fact]
    public void Hub_RunFrame_AdvancesScheduler()
    {
        var hub = new PerformanceHub();
        hub.Initialize();
        int counter = 0;
        hub.Scheduler.PostTask(() => counter++);
        hub.Scheduler.PostTask(() => counter++);
        long done = hub.RunFrame(Acrux.Core.Performance.Scheduling.CooperativeScheduler.FrameBudget.For60Fps);
        Assert.Equal(2, counter);
        Assert.True(done > 0);
    }

    [Fact]
    public void Hub_LongTaskDetection_UpdatesMetrics()
    {
        var hub = new PerformanceHub();
        hub.Initialize();
        hub.LongTasks.Observe("test", Acrux.Core.Performance.Scheduling.TaskPriority.Normal, () => Thread.Sleep(60));
        Assert.True(hub.Registry.Metrics.TotalBlockingTimeNanos > 0);
    }

    [Fact]
    public void Hub_AggregateResponder_IsRegistered()
    {
        var hub = new PerformanceHub();
        Assert.Equal(1, hub.Registry.MemoryPressure.ResponderCount);
    }

    [Fact]
    public void Hub_Shared_IsSingleton()
    {
        Assert.Same(PerformanceHub.Shared, PerformanceHub.Shared);
    }

    [Fact]
    public void Hub_ShutdownClearsCaches()
    {
        var hub = new PerformanceHub();
        hub.Initialize();
        hub.ResourceCache.Put("k", new Acrux.Core.Performance.Resources.ResourceResponse { Body = new byte[100], ContentLength = 100 });
        Assert.Equal(1, hub.ResourceCache.Count);
        hub.Shutdown();
        Assert.Equal(0, hub.ResourceCache.Count);
    }
}
