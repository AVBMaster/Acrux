using Acrux.Core.Performance.Memory;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class MemoryPressureTests
{
    private sealed class CountingResponder : MemoryResponder
    {
        public int PressureEvents;
        public int ReleaseEvents;
        public override string Name => "counting";
        public override void OnMemoryPressure(MemoryPressureLevel l) => PressureEvents++;
        public override void OnMemoryRelease(MemoryPressureLevel l) => ReleaseEvents++;
    }

    [Fact]
    public void ReportUsage_BelowThreshold_StaysNominal()
    {
        var m = new MemoryPressureMonitor();
        m.ReportUsage(100 * 1024 * 1024);
        Assert.Equal(MemoryPressureLevel.Nominal, m.Level);
    }

    [Fact]
    public void ReportUsage_AboveThreshold_Raises()
    {
        var m = new MemoryPressureMonitor();
        m.ReportUsage(3L * 1024 * 1024 * 1024);
        Assert.True(m.Level >= MemoryPressureLevel.High, $"expected High+, got {m.Level}");
        Assert.Equal(1, m.CriticalEvents);
    }

    [Fact]
    public void RegisteredResponder_ReceivesPressureCallback()
    {
        var m = new MemoryPressureMonitor();
        var r = new CountingResponder();
        m.Register(r);
        m.ReportUsage(3L * 1024 * 1024 * 1024);
        Assert.True(r.PressureEvents >= 1);
    }

    [Fact]
    public void UnregisteredResponder_ReceivesNoCallbacks()
    {
        var m = new MemoryPressureMonitor();
        var r = new CountingResponder();
        m.Register(r);
        m.Unregister(r);
        m.ReportUsage(3L * 1024 * 1024 * 1024);
        Assert.Equal(0, r.PressureEvents);
    }

    [Fact]
    public void ForceShrink_RunsAllResponders()
    {
        var m = new MemoryPressureMonitor();
        var r = new CountingResponder();
        m.Register(r);
        m.ForceShrink();
        Assert.Equal(1, r.PressureEvents);
    }

    [Fact]
    public void AggregateResponder_ShrinksCache()
    {
        var monitor = new MemoryPressureMonitor();
        var cache = new Acrux.Core.Performance.Resources.ResourceCache();
        cache.SetCapacity(100 * 1024 * 1024);
        var agg = new AggregateMemoryResponder { Resources = cache };
        monitor.Register(agg);

        monitor.ReportUsage(3L * 1024 * 1024 * 1024); // High pressure
        Assert.True(cache.CapacityBytes < 100 * 1024 * 1024, $"expected capacity reduced, got {cache.CapacityBytes}");
    }

    [Fact]
    public void MemoryBudget_CalculatesSoftLimits()
    {
        var b = new MemoryBudget(2L * 1024 * 1024 * 1024);
        Assert.Equal((long)(2L * 1024 * 1024 * 1024 * 0.30), b.ScriptHeapSoftLimit);
        Assert.Equal((long)(2L * 1024 * 1024 * 1024 * 0.40), b.ImagePoolSoftLimit);
    }
}
