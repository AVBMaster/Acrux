using Acrux.Core.Performance;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class ClockTests
{
    [Fact]
    public void NowNanos_IsMonotonicallyIncreasing()
    {
        long a = Clock.NowNanos();
        long b = Clock.NowNanos();
        long c = Clock.NowNanos();
        Assert.True(b >= a);
        Assert.True(c >= b);
    }

    [Fact]
    public void NanosToMillis_RoundTrips()
    {
        long nanos = 5_000_000L;
        Assert.Equal(5.0, Clock.NanosToMillis(nanos));
    }

    [Fact]
    public void TimingScope_RecordsElapsedTime()
    {
        var acc = new TimingAccumulator();
        using (TimingScope.Measure(acc))
        {
            // Spin briefly
            long total = 0;
            for (int i = 0; i < 1000; i++) total += i;
            GC.KeepAlive(total);
        }
        Assert.True(acc.Count == 1, "exactly one sample should be recorded");
        Assert.True(acc.MeanNanos > 0, "mean should be positive");
    }

    [Fact]
    public async Task TimingScope_AcrossAwaitingTask_StillMeasuresAsyncWork()
    {
        var acc = new TimingAccumulator();
        using (TimingScope.Measure(acc))
        {
            await Task.Delay(20);
        }
        Assert.True(acc.MeanMillis >= 15, $"expected ~20ms; got {acc.MeanMillis}ms");
    }

    [Fact]
    public void TimingAccumulator_TracksMinMaxAccurately()
    {
        var acc = new TimingAccumulator();
        acc.AddSample(100);
        acc.AddSample(50);
        acc.AddSample(200);
        Assert.Equal(50, acc.MinNanos);
        Assert.Equal(200, acc.MaxNanos);
        Assert.Equal(3, acc.Count);
        Assert.Equal(350, acc.SumNanos);
    }

    [Fact]
    public void TimingAccumulator_IsThreadSafe()
    {
        var acc = new TimingAccumulator();
        Parallel.For(0, 10_000, _ => acc.AddSample(42));
        Assert.Equal(10_000, acc.Count);
        Assert.Equal(42 * 10_000, acc.SumNanos);
    }
}
