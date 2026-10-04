using Acrux.Core.Performance.Scheduling;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class CooperativeSchedulerTests
{
    [Fact]
    public void PostTask_RunsThroughPlainQueue()
    {
        var s = new CooperativeScheduler();
        int counter = 0;
        s.PostTask(() => counter++);
        s.PostTask(() => counter++);
        var done = s.Drain();
        Assert.Equal(2, counter);
        Assert.True(s.PlainProcessed == 2);
        Assert.True(done > 0);
    }

    [Fact]
    public void PriorityOrdering_ImmediateRunsBeforeNormal()
    {
        var s = new CooperativeScheduler();
        var log = new List<string>();
        s.PostTask(() => log.Add("normal"), TaskPriority.Normal);
        s.PostTask(() => log.Add("immediate"), TaskPriority.Immediate);
        s.PostTask(() => log.Add("normal-2"), TaskPriority.Normal);
        s.Drain();
        Assert.Equal("immediate", log[0]);
        Assert.NotEqual("normal-2", log[0]);
    }

    [Fact]
    public void CancellableTask_GetsYieldProbe()
    {
        var s = new CooperativeScheduler();
        bool gotProbe = false;
        s.PostCancellable((moreTime, ct) =>
        {
            gotProbe = true;
            var _ = moreTime();
        }, TaskPriority.Normal, name: "test");
        s.Drain();
        Assert.True(gotProbe);
        Assert.True(s.CancellableProcessed == 1);
    }

    [Fact]
    public void CancellableTask_CanBeCancelled()
    {
        var s = new CooperativeScheduler();
        var task = s.PostCancellable((moreTime, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var _ = moreTime();
        }, TaskPriority.Normal, name: "cancel-test");
        task.Cancel();
        s.Drain();
        // Cancellation prevents the body from completing; we just verify no exception bubbles.
        Assert.Equal(1, s.CancellableProcessed);
    }

    [Fact]
    public void LongTaskCounter_IncrementsAfterLongWork()
    {
        var s = new CooperativeScheduler();
        s.PostTask(() => Thread.Sleep(60), TaskPriority.Low);
        s.Drain();
        Assert.True(s.LongTaskCount >= 1, $"expected >= 1 long tasks, got {s.LongTaskCount}");
    }

    [Fact]
    public void RunFrame_RespectsBudget()
    {
        var s = new CooperativeScheduler();
        for (int i = 0; i < 1000; i++) s.PostTask(() => Thread.SpinWait(1000), TaskPriority.Normal);
        var done = s.RunFrame(CooperativeScheduler.FrameBudget.For60Fps);
        Assert.True(done > 0);
    }

    [Fact]
    public void PostIdle_ExecutesWhenThereIsIdleTime()
    {
        var s = new CooperativeScheduler();
        bool ran = false;
        s.PostIdle(() => ran = true, TimeSpan.FromSeconds(1));
        s.Drain();
        Assert.True(ran);
    }
}
