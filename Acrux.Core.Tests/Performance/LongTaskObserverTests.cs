using Acrux.Core.Performance.Scheduling;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class LongTaskObserverTests
{
    [Fact]
    public void LongTask_DetectedAboveThreshold()
    {
        var obs = new LongTaskObserver(new LongTaskObserver.Options { ThresholdMicros = 10_000 });
        var entries = new List<LongTaskObserver.LongTaskEntry>();
        obs.OnLongTask += e => entries.Add(e);
        obs.Observe("slow", TaskPriority.Normal, () => Thread.Sleep(20));
        Assert.Single(entries);
        Assert.Equal("slow", entries[0].Name);
        Assert.True(entries[0].DurationNanos > 10_000_000);
    }

    [Fact]
    public void ShortTask_NotReported()
    {
        var obs = new LongTaskObserver(new LongTaskObserver.Options { ThresholdMicros = 100_000 });
        var entries = new List<LongTaskObserver.LongTaskEntry>();
        obs.OnLongTask += e => entries.Add(e);
        obs.Observe("fast", TaskPriority.Normal, () => { });
        Assert.Empty(entries);
    }

    [Fact]
    public void MaxEntries_IsRespected()
    {
        var obs = new LongTaskObserver(new LongTaskObserver.Options
        {
            ThresholdMicros = 100,
            MaxEntries = 3,
        });
        for (int i = 0; i < 10; i++)
            obs.Observe("task-" + i, TaskPriority.Normal, () => Thread.Sleep(2));
        Assert.Equal(3, obs.Snapshot().Count);
        Assert.Equal(10, obs.Observed);
    }

    [Fact]
    public void ObserverDoesNotLoseBodyExceptions()
    {
        var obs = new LongTaskObserver();
        Assert.Throws<InvalidOperationException>(() =>
            obs.Observe("bad", TaskPriority.Normal, () => throw new InvalidOperationException("boom")));
    }
}
