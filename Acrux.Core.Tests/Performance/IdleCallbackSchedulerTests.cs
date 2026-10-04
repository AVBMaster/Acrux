using Acrux.Core.Performance.Scheduling;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class IdleCallbackSchedulerTests
{
    [Fact]
    public void Register_ThenRun_ExecutesCallback()
    {
        var idle = new IdleCallbackScheduler();
        bool ran = false;
        idle.Register(_ => ran = true, TimeSpan.FromSeconds(1));
        idle.Run(100);
        Assert.True(ran);
        Assert.Equal(1, idle.Fired);
    }

    [Fact]
    public void CancelledCallback_IsNotInvoked()
    {
        var idle = new IdleCallbackScheduler();
        bool ran = false;
        var reg = idle.Register(_ => ran = true, TimeSpan.FromSeconds(1));
        idle.Cancel(reg);
        idle.Run(100);
        Assert.False(ran);
        Assert.Equal(0, idle.Fired);
    }

    [Fact]
    public void Run_StopsAtAvailableTime()
    {
        var idle = new IdleCallbackScheduler();
        int ran = 0;
        for (int i = 0; i < 100; i++)
            idle.Register(_ => { ran++; Thread.Sleep(2); }, TimeSpan.FromSeconds(1));
        idle.Run(10);
        Assert.True(ran < 100, "should not run all callbacks with only 10ms budget");
    }

    [Fact]
    public void CallbackTimesOut_IfDeadlineExceeded()
    {
        var idle = new IdleCallbackScheduler();
        bool didTimeOut = false;
        idle.Register(d => didTimeOut = d.DidTimeout, TimeSpan.FromMilliseconds(1));
        Thread.Sleep(10);
        idle.Run(100);
        Assert.True(didTimeOut);
        Assert.Equal(1, idle.TimedOut);
    }
}
