using Acrux.Core.Performance.Diagnostics;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class PerformanceMetricsTests
{
    [Fact]
    public void FirstPaint_RecordedOnce()
    {
        var m = new PerformanceMetrics();
        m.RecordFirstPaint();
        var first = m.FirstPaintNanos;
        m.RecordFirstPaint();
        Assert.Equal(first, m.FirstPaintNanos);
    }

    [Fact]
    public void Lcp_TakesLatestValue()
    {
        var m = new PerformanceMetrics();
        m.RecordLargestContentfulPaint();
        Thread.Sleep(2);
        m.RecordLargestContentfulPaint();
        Assert.True(m.LargestContentfulPaintNanos > 0);
    }

    [Fact]
    public void Tbt_Accumulates()
    {
        var m = new PerformanceMetrics();
        m.AddBlockingTime(10_000_000);
        m.AddBlockingTime(20_000_000);
        Assert.Equal(30_000_000, m.TotalBlockingTimeNanos);
        Assert.Equal(30.0, m.TotalBlockingTimeMillis);
    }

    [Fact]
    public void Tbt_IgnoresNegative()
    {
        var m = new PerformanceMetrics();
        m.AddBlockingTime(-1);
        Assert.Equal(0, m.TotalBlockingTimeNanos);
    }

    [Fact]
    public void Cls_Accumulates()
    {
        var m = new PerformanceMetrics();
        m.AddLayoutShift(100);
        m.AddLayoutShift(50);
        Assert.Equal(150, m.CumulativeLayoutShiftMicros);
    }

    [Fact]
    public void Reset_ClearsAll()
    {
        var m = new PerformanceMetrics();
        m.RecordFirstPaint();
        m.RecordTimeToInteractive();
        m.AddBlockingTime(1000);
        m.AddLayoutShift(100);
        m.Reset();
        Assert.False(m.HasFirstPaint);
        Assert.False(m.HasTimeToInteractive);
        Assert.Equal(0, m.TotalBlockingTimeNanos);
        Assert.Equal(0, m.CumulativeLayoutShiftMicros);
    }

    [Fact]
    public void FirstInputDelay_TakesMax()
    {
        var m = new PerformanceMetrics();
        m.RecordFirstInputDelay(1_000_000);
        m.RecordFirstInputDelay(5_000_000);
        m.RecordFirstInputDelay(2_000_000);
        Assert.Equal(5_000_000, m.FirstInputDelayNanos);
    }
}

public class PerformanceApiTests
{
    [Fact]
    public void Mark_StoresValue()
    {
        var reg = new PerformanceRegistry();
        var api = new PerformanceApi(reg);
        api.Mark("a");
        api.Mark("b");
        Assert.Equal(2, api.Marks.Count);
    }

    [Fact]
    public void Measure_ComputesDuration()
    {
        var reg = new PerformanceRegistry();
        var api = new PerformanceApi(reg);
        api.Mark("start");
        Thread.Sleep(10);
        var m = api.Measure("duration", "start");
        Assert.NotNull(m);
        Assert.True(m!.DurationMillis >= 5);
    }

    [Fact]
    public void Measure_UnknownStartMark_Throws()
    {
        var reg = new PerformanceRegistry();
        var api = new PerformanceApi(reg);
        Assert.Throws<InvalidOperationException>(() => api.Measure("m", "missing"));
    }

    [Fact]
    public void ClearMarks_RemovesAll()
    {
        var reg = new PerformanceRegistry();
        var api = new PerformanceApi(reg);
        api.Mark("a"); api.Mark("b");
        api.ClearMarks();
        Assert.Empty(api.Marks);
    }

    [Fact]
    public void Snapshot_ContainsExpectedFields()
    {
        var reg = new PerformanceRegistry();
        var api = new PerformanceApi(reg);
        var s = api.Snapshot();
        Assert.Contains("metrics", s);
        Assert.Contains("longTasks", s);
        Assert.Contains("pipeline", s);
        Assert.Contains("memory", s);
    }
}
