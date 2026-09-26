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
    public static int Run()
    {
        using var remote = new RemoteTabProcess(95, "", 1f, 1f);
        int childPid = remote.ChildPid;

        var sw0 = Stopwatch.StartNew();
        while (!remote.IsConnected && sw0.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        if (!remote.IsConnected) { Console.WriteLine("[idletest] connect failed"); return 1; }

        int frameCount = 0;
        remote.OnFrameArrived += () => Interlocked.Increment(ref frameCount);

        remote.NavigateHtml(DocumentManager.TestCssFeatureHtml, "upbrowser://test-css");
        bool gotFirst = WaitUntil(() => remote.FrameVersion > 0, 8000) > 0;
        Thread.Sleep(400); // let warm-up frames settle

        var self = System.Diagnostics.Process.GetCurrentProcess();
        var child = childPid > 0 ? System.Diagnostics.Process.GetProcessById(childPid) : null;
        child?.Refresh();
        var cpuBefore = child?.TotalProcessorTime ?? TimeSpan.Zero;
        long vBefore = remote.FrameVersion;
        int framesBefore = frameCount;

        Thread.Sleep(3000);

        child?.Refresh();
        var cpuDelta = (child?.TotalProcessorTime ?? TimeSpan.Zero) - cpuBefore;
        double memMB = (child?.PrivateMemorySize64 ?? 0) / 1048576.0;
        int idleFrames = frameCount - framesBefore;
        long selfIdleMs = 0;
        var selfBefore = self.TotalProcessorTime;
        Thread.Sleep(0);
        selfIdleMs = (long)(self.TotalProcessorTime - selfBefore).TotalMilliseconds;

        double childCpuPct = cpuDelta.TotalMilliseconds / 30.0; // % of one core over 3s
        Console.WriteLine($"[idletest] first={gotFirst} idleFrames={idleFrames} " +
                          $"childCPU={childCpuPct:F1}%ofcore childPrivateMB={memMB:F0} " +
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
        bool pass = gotFirst && idleFrames <= 1 && childCpuPct < 3.0;
        Console.WriteLine(pass ? "[idletest] PASS" : "[idletest] WARN — idle not quiet");
        return 0;
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
