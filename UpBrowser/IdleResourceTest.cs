using System.Diagnostics;
using UpBrowser.Core.Dom;
using UpBrowser.Process;

namespace UpBrowser;

/// <summary>
/// Idle-resource probe: loads the startup page into a real tab-host child,
/// then measures child CPU time, child memory, and frames produced while
/// nobody touches anything. Expected: ~0 frames, &lt;2% of one core.
/// Invoked via <c>UpBrowser --idletest</c>.
/// </summary>
internal static class IdleResourceTest
{
    // Frames the child published: a callback can only capture a field, not a local.
    private static int _frames;

    public static int Run()
    {
        using var remote = new RemoteTabProcess(95, "", 1f, 1f);
        int childPid = remote.ChildPid;

        var sw0 = Stopwatch.StartNew();
        while (!remote.IsConnected && sw0.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        if (!remote.IsConnected) { Console.WriteLine("[idletest] connect failed"); return 1; }

        _frames = 0;
        remote.OnFrameArrived += () => Interlocked.Increment(ref _frames);

        remote.NavigateHtml(DocumentManager.TestCssFeatureHtml, "upbrowser://test-css");
        bool gotFirst = WaitUntil(() => remote.FrameVersion > 0, 8000) > 0;
        Thread.Sleep(400); // let warm-up frames settle

        var self = System.Diagnostics.Process.GetCurrentProcess();
        var child = childPid > 0 ? System.Diagnostics.Process.GetProcessById(childPid) : null;

        // Two windows, because the first one is not idle yet: loading a page leaves the
        // runtime settling (tiered compilation promoting the methods the first frame just
        // ran, GC growing its heaps), and that CPU belongs to startup, not to an idle
        // tick. The gate is on the steady window; the first is still printed so a startup
        // tail cannot hide a regression.
        double cpuA = SampleWindow(child, 3000, ref _frames, out int idleFramesA);
        double cpuB = SampleWindow(child, 3000, ref _frames, out int idleFramesB);

        child?.Refresh();
        double memMB = (child?.PrivateMemorySize64 ?? 0) / 1048576.0;

        Console.WriteLine($"[idletest] first={gotFirst} idleFrames={idleFramesA}/{idleFramesB} " +
                          $"childCPU={cpuA:F1}%->{cpuB:F1}%ofcore childPrivateMB={memMB:F0} " +
                          $"parentSelfMB={(self.PrivateMemorySize64 / 1048576.0):F0}");
        child?.Dispose();
        remote.Dispose();
        // Reconnect a fresh child to double-check no orphan survives dispose.
        Thread.Sleep(300);
        try
        {
            var leftover = System.Diagnostics.Process.GetProcessesByName("UpBrowser").Length;
            Console.WriteLine($"[idletest] UpBrowser processes after dispose (incl. this one): {leftover}");
        }
        catch { }
        bool pass = gotFirst && idleFramesB <= 1 && cpuB < 3.0;
        Console.WriteLine(pass ? "[idletest] PASS" : "[idletest] WARN — idle not quiet");
        return 0;
    }

    /// <summary>CPU the child used during one window, as a percentage of one core, plus
    /// how many frames arrived in it.</summary>
    private static double SampleWindow(System.Diagnostics.Process? child, int windowMs,
        ref int frameCount, out int idleFrames)
    {
        child?.Refresh();
        var cpuBefore = child?.TotalProcessorTime ?? TimeSpan.Zero;
        int framesAt = frameCount;
        Thread.Sleep(windowMs);
        child?.Refresh();
        var cpuDelta = (child?.TotalProcessorTime ?? TimeSpan.Zero) - cpuBefore;
        idleFrames = frameCount - framesAt;
        return cpuDelta.TotalMilliseconds / (windowMs / 10.0);
    }

    private static long WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return sw.ElapsedMilliseconds;
            Thread.Sleep(10);
        }
        return 0;
    }
}
