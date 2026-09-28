using System.Diagnostics;
using UpBrowser.Process;

namespace UpBrowser;

/// <summary>
/// Headless end-to-end test of the multi-process tab stack (parent
/// RemoteTabProcess ↔ child `--tab-host`): frame delivery, JS execution in
/// the child, scroll offsets and link-click navigation.
/// Invoked via <c>UpBrowser --proctest</c>. Returns 0 on pass, 1 on fail.
/// </summary>
internal static class ProcessTabSelfTest
{
    public static int Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest");
        Directory.CreateDirectory(dir);
        var page1 = Path.Combine(dir, "page1.html");
        var page2 = Path.Combine(dir, "page2.html");
        var url1 = new Uri(page1).AbsoluteUri;
        var url2 = new Uri(page2).AbsoluteUri;

        File.WriteAllText(page1, $$"""
            <!DOCTYPE html><html><head><title>start</title></head>
            <body style="margin:0">
            <a id="lnk" href="{{url2}}" style="display:block;width:300px;height:60px;background:red">go</a>
            <script>
              var n = 0;
              setInterval(function() { n++; document.title = 'tick' + n; }, 80);
            </script>
            </body></html>
            """);
        File.WriteAllText(page2, "<!DOCTYPE html><html><head><title>PAGE2</title></head><body>second page</body></html>");

        using var remote = new RemoteTabProcess(99, url1, 1f, 1f);

        var sw = Stopwatch.StartNew();
        while (!remote.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        bool connected = remote.IsConnected;
        Console.WriteLine($"[proctest] connected={connected}");
        if (!connected) return 1;

        remote.NavigateHtml(File.ReadAllText(page1), url1);

        long v0 = WaitUntil(() => remote.FrameVersion > 0, 8000);
        bool gotFrame = v0 > 0;
        // With pixels on shared memory the "packet" is metadata + a damage-byte count:
        // a frame carrying damage over a real viewport is the equivalent of the old
        // inline packet having pixels.
        bool hasPixels = remote.TryGetFrame(out _, out int w, out int h, out _, out _,
            out _, out _, out int domCount, out _, out long damageBytes, out _) && damageBytes > 0;
        Console.WriteLine($"[proctest] gotFrame={gotFrame} frame={w}x{h} pixels={damageBytes > 0} dom={domCount}");

        // JS must be running in the child: title advances to tickN.
        WaitUntil(() => remote.Title.StartsWith("tick"), 4000);
        bool jsRan = remote.Title.StartsWith("tick");
        Console.WriteLine($"[proctest] jsTitle='{remote.Title}' jsRan={jsRan}");

        // Scroll offset must round-trip into the next frame.
        long before = remote.FrameVersion;
        remote.ScrollTo(0, 120);
        bool scrolled = WaitUntil(() =>
            remote.TryGetFrame(out _, out _, out _, out _, out float sy, out _, out _, out _, out _, out _, out long ver2)
            && ver2 != before && Math.Abs(sy - 120) < 1, 6000) > 0;
        Console.WriteLine($"[proctest] scrolled={scrolled}");

        // Click on the link must navigate inside the child and report the URL.
        remote.ScrollTo(0, 0);
        Thread.Sleep(150);
        remote.MouseDown(10, 10);
        remote.MouseUp(10, 10);
        bool navigated = WaitUntil(() => remote.Url == url2, 6000) > 0;
        Console.WriteLine($"[proctest] linkNav={navigated} url='{remote.Url}'");

        bool pass = connected && gotFrame && hasPixels && w > 100 && h > 100 && domCount > 3 && jsRan && scrolled && navigated;
        Console.WriteLine($"[proctest] base features pass={pass}");
        bool features = FeatureTests().GetAwaiter().GetResult();
        bool selection = SelectionTest();
        bool ime = ImeTest();
        bool dialogs = DialogTest();
        bool watchdog = WatchdogTest();
        bool crashes = CrashTest();
        bool channel = FrameChannelTest();
        bool resize = ResizeRenegotiationTest();
        bool nonblocking = SendUnderWedgeTest();
        bool budget = RestartBudgetTest();
        bool perf = PerformanceTests().GetAwaiter().GetResult();
        pass = pass && features && selection && ime && dialogs && watchdog && crashes && channel && resize && nonblocking && budget && perf;
        Console.WriteLine(pass ? "[proctest] PASS" : "[proctest] FAIL");

        try { Directory.Delete(dir, true); } catch { }
        return pass ? 0 : 1;
    }

    /// <summary>A sampled grid of the folded frame — enough to expose a stride or offset
    /// mismatch (which displaces everything) without depending on pixel-buffer APIs.</summary>
    private static string SampleGrid(RemoteTabProcess proc, RemoteTabView view)
    {
        long until = Environment.TickCount64 + 400;
        while (Environment.TickCount64 < until)
        {
            view.UpdateFromFrame();
            Thread.Sleep(20);
        }
        var bmp = view.Bitmap;
        if (bmp == null) return "<none>";
        var sb = new System.Text.StringBuilder();
        sb.Append(bmp.Width).Append('x').Append(bmp.Height).Append('|');
        for (int gy = 0; gy < 16; gy++)
            for (int gx = 0; gx < 24; gx++)
            {
                var c = bmp.GetPixel((bmp.Width - 1) * gx / 23, (bmp.Height - 1) * gy / 15);
                sb.Append(c.Red).Append(',').Append(c.Green).Append(',').Append(c.Blue).Append(';');
            }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static bool WaitForFrame(RemoteTabProcess proc, long afterVersion, int ms = 8000) =>
        WaitUntil(() => proc.FrameVersion > afterVersion, ms) > 0;

    /// <summary>
    /// Resizing swaps the shared mapping for a new generation. The frame the shell folds
    /// afterwards must be identical to what a host that started at that size produces — a
    /// stale stride, pitch or slot geometry shows up here and nowhere else.
    /// </summary>
    /// <summary>
    /// Sending to a wedged host must not stall the caller: the child's reader thread keeps
    /// draining the pipe even while its own loop is stuck, so with pixels in shared memory the
    /// shell's writes are bounded by a buffer nobody is filling. This measurement is what
    /// decides whether the shell needs an outbound writer thread at all.
    /// </summary>
    private static bool SendUnderWedgeTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest_wedge");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "w.html");
        File.WriteAllText(page, "<!DOCTYPE html><html><head><title>w</title></head><body>x</body></html>");
        var url = new Uri(page).AbsoluteUri;

        long worstMs = 0;
        int sent = 0;
        long totalMs;
        using (var wedged = new RemoteTabProcess(85, url, 1f, 1f, hang: true))
        {
            var sw = Stopwatch.StartNew();
            while (!wedged.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            wedged.Navigate(url);
            Thread.Sleep(600);   // let it wedge for real, then hammer input
            var bench = Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++)
            {
                var one = Stopwatch.StartNew();
                wedged.MouseMove(i % 500 + 0.5f, i % 300 + 0.5f);
                wedged.Resize(1000 + i % 3, 700);
                one.Stop();
                sent += 2;
                if (one.ElapsedMilliseconds > worstMs) worstMs = one.ElapsedMilliseconds;
            }
            totalMs = bench.ElapsedMilliseconds;
            Console.WriteLine($"[proctest] wedge-send msgs={sent} totalMs={totalMs} " +
                $"worstOne={worstMs}ms dead={wedged.IsDead} responsive={wedged.Responsiveness}");
        }
        try { Directory.Delete(dir, true); } catch { }
        return worstMs <= 50 && sent == 4000 && totalMs < 1000;
    }

    private static bool ResizeRenegotiationTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest_rs");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "rs.html");
        File.WriteAllText(page, """
            <!DOCTYPE html><html><head><title>rs</title></head>
            <body style="margin:0">
            <div style="width:100%;height:40px;background:#3a6">top</div>
            <p style="font-size:17px">The quick brown fox jumps over the lazy dog again and again so that text reflows at every width we test here.</p>
            <div style="width:60%;height:120px;background:#e93"></div>
            </body></html>
            """);
        var url = new Uri(page).AbsoluteUri;

        string grown = "", direct = "";
        using (var a = new RemoteTabProcess(87, url, 1f, 1f))
        using (var viewA = new RemoteTabView(a))
        {
            var sw = Stopwatch.StartNew();
            while (!a.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            a.Resize(700, 500);
            a.Navigate(url);
            WaitForFrame(a, 0);
            // Grow to the reference size: new mapping generation, resync frame expected.
            long before = a.FrameVersion;
            a.Resize(1024, 768);
            bool resized = WaitForFrame(a, before);
            viewA.UpdateFromFrame();
            grown = resized ? SampleGrid(a, viewA) : "<no-frame>";
        }
        using (var b = new RemoteTabProcess(86, url, 1f, 1f))
        using (var viewB = new RemoteTabView(b))
        {
            var sw = Stopwatch.StartNew();
            while (!b.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            b.Resize(1024, 768);
            b.Navigate(url);
            WaitForFrame(b, 0);
            direct = SampleGrid(b, viewB);
        }

        bool same = grown.Length > 0 && grown == direct;
        Console.WriteLine($"[proctest] resize renegotiated={same} grown='{grown[..Math.Min(14, grown.Length)]}' " +
            $"direct='{direct[..Math.Min(14, direct.Length)]}'");
        try { Directory.Delete(dir, true); } catch { }
        return same;
    }

    /// <summary>
    /// The shared-memory frame channel, exercised through two endpoints over one mapping:
    /// layout offsets, the slot state machine, metadata/rect fidelity and sequence ordering.
    /// A wrong offset here is a silently misplaced pixel there, so the probe pattern is written
    /// into the last byte of the last slot.
    /// </summary>
    private static unsafe bool FrameChannelTest()
    {
        string name = $"UpBrowser_TestFrames_{Environment.ProcessId:x8}";
        int w = 37, h = 13;   // deliberately odd sizes: catch row-padding and alignment math
        using var host = UpBrowser.PageContract.FrameChannel.Create(name, w, h);
        using var shell = UpBrowser.PageContract.FrameChannel.Open(name);
        bool opened = shell != null;

        bool rejectsGarbage =
            UpBrowser.PageContract.FrameChannel.Open($"UpBrowser_Nope_{Environment.ProcessId:x8}") == null;

        // Nothing published yet.
        bool empty = shell != null &&
            !shell.TryAcquireNewest(0, out _, out _, out _, out _);

        // Fill every slot so the reader must pick the newest, not the first.
        var rects = new UpBrowser.PageContract.DamageRect[] { new(1, 2, 3, 4), new(5, 6, 7, 8) };
        int lastSlot = -1;
        for (int i = 0; i < UpBrowser.PageContract.FrameChannel.SlotCount; i++)
        {
            int slot = host.AcquireForWriting();
            if (slot < 0) break;
            byte* px = host.PixelPointer(slot);
            for (int b = 0; b < host.Stride * host.Height; b++) px[b] = (byte)(slot * 16 + 1);
            var meta = new UpBrowser.PageContract.FrameMeta
            {
                Mode = UpBrowser.PageContract.FrameMode.DamageRows,
                ScrollX = 1.5f + slot, ScrollY = 2.5f,
                ContentW = 1000 + slot, ContentH = 2000,
                PageBgRgba = 0x11223344u,
                DomCount = 11 + slot, BoxCount = 22,
                ScrollDy = 3,
                CaretX = 33.5f, CaretY = 44.25f, CaretH = 18f,
                HasEditableFocus = true, IsPassword = true,
            };
            host.Publish(slot, in meta, rects);
            lastSlot = slot;
        }

        bool got = shell!.TryAcquireNewest(0, out int rslot, out ulong seq,
            out var rmeta, out var rrects);
        // Newest sequence must win, and its bytes must match the slot that produced them.
        byte probe = shell.PixelPointer(rslot)[host.Stride * (host.Height - 1) + (host.Stride - 1)];
        bool newest = got && seq == UpBrowser.PageContract.FrameChannel.SlotCount;
        bool metaOk = got && rmeta.ScrollY == 2.5f && rmeta.ContentW >= 1000 && rmeta.PageBgRgba == 0x11223344u
            && rmeta.BoxCount == 22 && rmeta.ScrollDy == 3
            && rmeta.CaretX == 33.5f && rmeta.CaretY == 44.25f && rmeta.CaretH == 18f
            && rmeta.HasEditableFocus && rmeta.IsPassword
            && rmeta.Mode == UpBrowser.PageContract.FrameMode.DamageRows;
        bool pixelsOk = got && probe == (byte)(rslot * 16 + 1);
        bool rectsOk = got && rrects.Length == 2 && rrects[1].X == 5 && rrects[1].Height == 8;

        // A slot under lease must not be handed back out for writing twice.
        int dup = host.AcquireForWriting();
        bool leaseHeld = dup != rslot;
        if (dup >= 0) host.ReleaseForWriting(dup);
        shell.ReleaseRead(rslot);

        // Same sequence again yields nothing: the shell must not redraw an unchanged frame.
        bool noReplay = !shell.TryAcquireNewest(seq, out _, out _, out _, out _);

        // Every slot released → the host can claim all four again.
        int claimed = 0;
        for (int i = 0; i < UpBrowser.PageContract.FrameChannel.SlotCount; i++)
        {
            int s2 = host.AcquireForWriting();
            if (s2 < 0) break;
            claimed++;
            host.ReleaseForWriting(s2);
        }

        Console.WriteLine($"[proctest] frames open={opened} junk={rejectsGarbage} empty={empty} " +
            $"newest={newest} meta={metaOk} pixels={pixelsOk} rects={rectsOk} lease={leaseHeld} " +
            $"noReplay={noReplay} recyclable={claimed == UpBrowser.PageContract.FrameChannel.SlotCount} lastSlot={lastSlot}");
        return opened && rejectsGarbage && empty && newest && metaOk && pixelsOk && rectsOk
            && leaseHeld && noReplay && claimed == UpBrowser.PageContract.FrameChannel.SlotCount;
    }

    /// <summary>Spawn a host that dies one particular way and report how the shell judged it.</summary>
    private static CrashKind DeathOf(string url, bool hang, bool fault, out string detail, bool closePolitely = false)
    {
        var proc = new RemoteTabProcess(91, url, 1f, 1f, hang, fault);
        detail = "";
        try
        {
            var sw = Stopwatch.StartNew();
            while (!proc.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            proc.Navigate(url);
            if (closePolitely)
            {
                proc.Dispose();
                detail = "closed";
                return proc.DeathKind;
            }
            WaitUntil(() => proc.IsDead, 8000);
            // Give the Exited backstop a moment in case the pipe closed before the code was read.
            for (int i = 0; i < 20 && proc.DeathKind == CrashKind.CleanExit && proc.DeathDetail == ""; i++) Thread.Sleep(50);
            detail = proc.DeathDetail;
            return proc.DeathKind;
        }
        finally
        {
            try { proc.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// The three ways a host can stop must not be confused: an injected fault reports itself,
    /// a killed process vanishes without a word, and a polite close is not a crash at all.
    /// </summary>
    private static bool CrashTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest_crash");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "c.html");
        File.WriteAllText(page, "<!DOCTYPE html><html><head><title>c</title></head><body>x</body></html>");
        var url = new Uri(page).AbsoluteUri;

        bool faulted = DeathOf(url, false, true, out var fDetail) == CrashKind.Faulted && fDetail.Contains("injected fault");
        Console.WriteLine($"[proctest] crash faulted={faulted} detail='{fDetail}'");

        // A live host, then pulled out from under us mid-flight.
        string kDetail;
        CrashKind killed;
        var live = new RemoteTabProcess(90, url, 1f, 1f);
        try
        {
            var sw = Stopwatch.StartNew();
            while (!live.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            live.Navigate(url);
            WaitUntil(() => live.FrameVersion > 0, 8000);
            try { System.Diagnostics.Process.GetProcessById(live.ChildPid).Kill(entireProcessTree: true); } catch { }
            WaitUntil(() => live.IsDead, 8000);
            killed = live.DeathKind;
            kDetail = live.DeathDetail;
        }
        finally { live.Dispose(); }
        bool killCaught = killed == CrashKind.KilledOrCrashedNative;
        Console.WriteLine($"[proctest] crash killed={killCaught} kind={killed} detail='{kDetail}'");

        var proc = new RemoteTabProcess(89, url, 1f, 1f);
        proc.Navigate(url);
        WaitUntil(() => proc.IsConnected, 8000);
        proc.Dispose();
        bool closeIsClean = proc.DeathKind == CrashKind.CleanExit;
        Console.WriteLine($"[proctest] crash closeClean={closeIsClean} kind={proc.DeathKind}");

        try { Directory.Delete(dir, true); } catch { }
        return faulted && killCaught && closeIsClean;
    }

    /// <summary>
    /// The restart budget, driven deterministically: escalating spacing, hang kills free,
    /// a spent budget, window sliding letting the tab try again, and a user reload refilling it.
    /// </summary>
    private static bool RestartBudgetTest()
    {
        var p = new RestartPolicy();
        long t = 0;
        var v1 = p.OnDeath(CrashKind.Faulted, t);
        var v2 = p.OnDeath(CrashKind.Faulted, t + 100);
        var v3 = p.OnDeath(CrashKind.Faulted, t + 200);
        bool escalates = v1.ShouldRestart && v1.DelayMs == 0 && v2.DelayMs == 1000 && v3.DelayMs == 4000;

        // Backoff must actually gate: at +200ms the third restart is not due until +4200.
        bool gated = !p.ReadyToRelaunch(t + 200) && p.ReadyToRelaunch(t + 4200);

        var v4 = p.OnDeath(CrashKind.Faulted, t + 5000);
        bool exhausted = v4.Exhausted && !v4.ShouldRestart;

        var q = new RestartPolicy();
        bool hangFree = true;
        for (int i = 0; i < 10; i++)
        {
            var hv = q.OnDeath(CrashKind.HangKilled, i * 100L);
            hangFree &= hv.ShouldRestart && !hv.Exhausted;
        }
        bool hangLeavesBudget = q.OnDeath(CrashKind.Faulted, 1000).ShouldRestart;

        var r = new RestartPolicy();
        r.OnDeath(CrashKind.Faulted, 0);
        r.OnDeath(CrashKind.Faulted, 10);
        r.OnDeath(CrashKind.Faulted, 20);
        r.OnDeath(CrashKind.Faulted, 30);
        bool spent = r.Exhausted;
        r.Reset();
        bool refilled = !r.Exhausted && r.OnDeath(CrashKind.Faulted, 40).ShouldRestart;

        // Faults spread beyond the window must not add up to exhaustion.
        var s = new RestartPolicy();
        s.OnDeath(CrashKind.Faulted, 0);
        s.OnDeath(CrashKind.Faulted, 40_000);
        s.OnDeath(CrashKind.Faulted, 80_000);
        var last = s.OnDeath(CrashKind.Faulted, 120_000);
        bool sliding = !s.Exhausted && last.ShouldRestart;

        Console.WriteLine($"[proctest] budget escalate={escalates} gated={gated} exhausted={exhausted} " +
            $"hangFree={hangFree} hangLeavesBudget={hangLeavesBudget} spent={spent} refilled={refilled} sliding={sliding}");
        return escalates && gated && exhausted && hangFree && hangLeavesBudget && spent && refilled && sliding;
    }

    /// <summary>
    /// The watchdog has to catch a wedged host and, just as importantly, leave a live one
    /// alone — including one parked on a dialog the user has not answered yet.
    /// </summary>
    private static bool WatchdogTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest_wd");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "wd.html");
        File.WriteAllText(page, """
            <!DOCTYPE html><html><head><title>wd</title></head><body>dead</body></html>
            """);
        var url = new Uri(page).AbsoluteUri;

        bool caughtWedged = false, suspectFirst = false;
        using (var wedged = new RemoteTabProcess(93, url, 1f, 1f, hang: true))
        {
            var sw = Stopwatch.StartNew();
            while (!wedged.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            wedged.Navigate(url);

            // Suspect before Unresponsive, and unresponsive within the advertised budget.
            suspectFirst = WaitUntil(() => wedged.Responsiveness == PageResponsiveness.Suspect, 3000) > 0;
            caughtWedged = WaitUntil(() => wedged.Responsiveness == PageResponsiveness.Unresponsive, 6000) > 0;
            Console.WriteLine($"[proctest] watchdog wedged={caughtWedged} staged={suspectFirst} " +
                $"silent={wedged.SilentMs}ms awaiting={wedged.AwaitingPage}");
        }

        // A live host that is merely waiting on the user must never look wedged.
        bool dialogWaitStaysHealthy = false;
        var alertPage = Path.Combine(dir, "alert.html");
        File.WriteAllText(alertPage, """
            <!DOCTYPE html><html><head><title>al</title></head><body>
            <script>__win.alert('waiting for you');</script>
            </body></html>
            """);
        using (var idle = new RemoteTabProcess(92, new Uri(alertPage).AbsoluteUri, 1f, 1f))
        {
            var sw = Stopwatch.StartNew();
            while (!idle.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            idle.Navigate(new Uri(alertPage).AbsoluteUri);
            if (WaitUntil(() => !idle.AwaitingPage, 6000) > 0)
            {
                // Leave the dialog unanswered for longer than the unresponsive budget.
                long until = Environment.TickCount64 + 4000;
                bool everBad = false;
                while (Environment.TickCount64 < until)
                {
                    if (idle.Responsiveness != PageResponsiveness.Healthy) { everBad = true; break; }
                    Thread.Sleep(50);
                }
                dialogWaitStaysHealthy = !everBad && !idle.IsDead;
            }
            Console.WriteLine($"[proctest] watchdog dialogWaitHealthy={dialogWaitStaysHealthy} " +
                $"beats={idle.HeartbeatSeq} silent={idle.SilentMs}ms");
        }

        try { Directory.Delete(dir, true); } catch { }
        return caughtWedged && suspectFirst && dialogWaitStaysHealthy;
    }

    /// <summary>
    /// alert/confirm/prompt must round-trip: the page's script thread blocks on the
    /// answer, and the answer must be what the page finally sees.
    /// </summary>
    private static bool DialogTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest_dlg");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "dlg.html");
        File.WriteAllText(page, """
            <!DOCTYPE html><html><head><title>start</title></head><body>
            <script>
              var ok = confirm('sure?');
              var n = prompt('q', 'd');
              document.title = 'r:' + ok + ':N[' + n + ']';
            </script>
            </body></html>
            """);
        var url = new Uri(page).AbsoluteUri;

        using var remote = new RemoteTabProcess(95, url, 1f, 1f);
        var sw = Stopwatch.StartNew();
        while (!remote.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);

        // Answer dialogs in arrival order — the ids are the page's, not ours to assume.
        var arrived = new System.Collections.Concurrent.ConcurrentQueue<(int Id, string Message, string Type)>();
        remote.OnDialogRequest += (id, message, type) => arrived.Enqueue((id, message, type));
        remote.Navigate(url);

        (int Id, string Message, string Type)? NextRequest(int timeoutMs)
        {
            if (WaitUntil(() => !arrived.IsEmpty, timeoutMs) == 0) return null;
            arrived.TryDequeue(out var r);
            return r;
        }

        const string finalTitle = "r:true:N[hello]";
        var first = NextRequest(6000);
        bool confirmShape = first != null && first.Value.Message == "sure?" && first.Value.Type == "confirm";
        // Still unanswered, so the script must not have reached its title assignment.
        bool blockedWhileOpen = remote.Title != finalTitle;
        if (first != null) remote.RespondDialog(first.Value.Id, accepted: true, text: null);

        var second = NextRequest(6000);
        bool promptShape = second != null && second.Value.Message == "q" && second.Value.Type == "prompt:d";
        if (second != null) remote.RespondDialog(second.Value.Id, accepted: true, text: "hello");

        // The script resumed, and it sees exactly the answers the user gave.
        bool resumed = WaitUntil(() => remote.Title == finalTitle, 6000) > 0;

        // A rejected dialog must still be answered, and must read back as false/null —
        // the script may never stay blocked on an answer the user already gave.
        bool rejectWorks = false;
        var page2 = Path.Combine(dir, "dlg2.html");
        File.WriteAllText(page2, """
            <!DOCTYPE html><html><head><title>start</title></head><body>
            <script>
              var c = confirm('again');
              var p = prompt('dismiss me', 'anon');
              document.title = 'c:' + c + ' p:' + p;
            </script>
            </body></html>
            """);
        using (var dismissed = new RemoteTabProcess(94, new Uri(page2).AbsoluteUri, 1f, 1f))
        {
            var seen = new System.Collections.Concurrent.ConcurrentQueue<(int Id, string Message, string Type)>();
            dismissed.OnDialogRequest += (id, message, type) => seen.Enqueue((id, message, type));
            var sw2 = Stopwatch.StartNew();
            while (!dismissed.IsConnected && sw2.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            dismissed.Navigate(new Uri(page2).AbsoluteUri);

            bool answeredBoth = false;
            if (WaitUntil(() => !seen.IsEmpty, 6000) > 0 && seen.TryDequeue(out var r1))
            {
                dismissed.RespondDialog(r1.Id, accepted: false, text: null);
                if (WaitUntil(() => !seen.IsEmpty, 6000) > 0 && seen.TryDequeue(out var r2))
                {
                    dismissed.RespondDialog(r2.Id, accepted: false, text: null);
                    answeredBoth = true;
                }
            }
            rejectWorks = answeredBoth &&
                WaitUntil(() => dismissed.Title == "c:false p:null", 6000) > 0;
        }

        Console.WriteLine($"[proctest] dialogs confirm={confirmShape} prompt={promptShape} " +
            $"blockedWhileOpen={blockedWhileOpen} resumed={resumed} reject={rejectWorks} title='{remote.Title}'");
        try { Directory.Delete(dir, true); } catch { }
        return confirmShape && promptShape && blockedWhileOpen && resumed && rejectWorks;
    }

    /// <summary>Hover events, form editing, element-scroller wheel, scrollTo sync, background frame suppression.</summary>
    private static Task<bool> FeatureTests() => Task.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest2");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "feat.html");
        File.WriteAllText(page, """
            <!DOCTYPE html><html><head><title>start</title></head>
            <body style="margin:0">
            <input id="in" type="text" style="width:200px;height:30px">
            <div id="box" style="overflow:scroll;width:100px;height:80px"><div style="height:600px">tall</div></div>
            <div id="hov" style="width:300px;height:40px;background:#eee">hover me</div>
            <div style="height:3000px">filler</div>
            <script>
              var inp = document.getElementById('in');
              inp.addEventListener('input', function(){ document.title = 'val:' + inp.value; });
              document.getElementById('hov').addEventListener('mouseover', function(){ document.title = 'hovered'; });
              window.scrollTo(0, 60);
            </script>
            </body></html>
            """);

        using var remote = new RemoteTabProcess(98, "", 1f, 1f);
        bool? wheelConsumed = null;
        remote.OnWheelResult += c => wheelConsumed = c;
        var sw0 = Stopwatch.StartNew();
        while (!remote.IsConnected && sw0.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        if (!remote.IsConnected) return false;

        remote.NavigateHtml(File.ReadAllText(page), new Uri(page).AbsoluteUri);

        // scrollTo(0,60) must round-trip into frame metadata (ScrollChanged sync).
        bool scrollSync = WaitUntil(() =>
            remote.TryGetFrame(out _, out _, out _, out _, out float sy, out _, out _, out _, out _, out _, out _) && Math.Abs(sy - 60) < 1, 6000) > 0;

        // Hover: mouseover on #hov (doc y 110..150 → viewport y 50..90 after 60px scroll).
        remote.MouseMove(50, 70);
        bool hovered = WaitUntil(() => remote.Title == "hovered", 4000) > 0;

        // Form editing: scroll to top, click the input, type 'h','i' → title updates.
        remote.ScrollTo(0, 0);
        Thread.Sleep(250);
        remote.MouseDown(10, 15);
        remote.MouseUp(10, 15);
        remote.Char((ushort)'h');
        remote.Char((ushort)'i');
        bool edited = WaitUntil(() => remote.Title == "val:hi", 4000) > 0;

        // Element scroller: wheel over #box (doc y 30..110) must be consumed by the child.
        wheelConsumed = null;
        remote.Wheel(0, 30, 50, 60);
        bool wheelAnswered = WaitUntil(() => wheelConsumed != null, 3000) > 0;
        bool wheelConsumedYes = wheelConsumed == true;

        // Root scroller regression: wheel over ordinary page content must NOT be
        // consumed by body/html (that invisible element-scroll froze the page —
        // the wheel-arbitration ghost bug). Expect an answer of consumed=false.
        wheelConsumed = null;
        remote.Wheel(0, 120, 400, 400);
        bool wheelAnswered2 = WaitUntil(() => wheelConsumed != null, 3000) > 0;
        bool rootWheelPasses = wheelConsumed == false;

        // Background tabs: no frames while inactive; one fresh frame on activation.
        remote.SetActive(false);
        Thread.Sleep(150);
        long vIdle = remote.FrameVersion;
        remote.ScrollTo(0, 300);
        Thread.Sleep(300);
        bool suppressed = remote.FrameVersion == vIdle;
        remote.SetActive(true);
        bool resumed = WaitUntil(() =>
            remote.TryGetFrame(out _, out _, out _, out _, out float sy2, out _, out _, out _, out _, out _, out _) && sy2 > 250, 4000) > 0;

        Console.WriteLine($"[proctest] scrollSync={scrollSync} hovered={hovered} edited={edited} " +
                          $"wheelAnswered={wheelAnswered} wheelConsumed={wheelConsumedYes} bgSuppressed={suppressed} bgResume={resumed} rootWheelPasses={rootWheelPasses}");

        try { Directory.Delete(dir, true); } catch { }
        return scrollSync && hovered && edited && wheelConsumedYes && wheelAnswered2 && rootWheelPasses && suppressed && resumed;
    });

    /// <summary>
    /// Page-text selection lives in the child now: pointer events drive the engine's
    /// hit-test and drag, and the shell reads the result back over the SelectedText RPC.
    /// Exact substrings are asserted — a hit test off by one character fails here.
    /// </summary>
    private static bool SelectionTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest_sel");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "sel.html");
        File.WriteAllText(page, """
            <!DOCTYPE html><html><head><title>sel</title></head>
            <body style="margin:0">
            <div style="font-family:Arial;font-size:20px">The quick brown fox</div>
            <div style="font-family:Arial;font-size:20px">Lazy dog sleeps</div>
            </body></html>
            """);
        var url = new Uri(page).AbsoluteUri;

        // Replicate the engine's per-character measurement so every click lands at the
        // centre of its target character cell: the resolved caret offset is then
        // unambiguous, and any hit-test error shifts the extracted substring.
        UpBrowser.PageHost.PageEnvironment.Initialize();
        var measurer = UpBrowser.Core.Layout.TextMeasurer.Instance;
        if (measurer == null)
        {
            Console.WriteLine("[proctest] selection measurer=missing");
            return false;
        }
        const string line = "The quick brown fox";   // one text node, offsets 0..19
        const float fs = 20f;
        var cum = new float[line.Length];
        float acc = 0;
        for (int i = 0; i < line.Length; i++)
        {
            acc += measurer.MeasureText(line[i].ToString(), "Arial", fs, UpBrowser.Core.Dom.FontWeight.Normal);
            cum[i] = acc;
        }
        // Viewport x that resolves to caret offset i in the first line's text node.
        float OffsetX(int i) => i <= 0 ? 1f : i >= line.Length ? cum[^1] - 0.5f : (cum[i - 1] + cum[i]) / 2f;
        const float lineY = 10f;   // mid-line: 20px text starting at document y 0

        using var remote = new RemoteTabProcess(83, url, 1f, 1f);
        var sw = Stopwatch.StartNew();
        while (!remote.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        remote.NavigateHtml(File.ReadAllText(page), url);
        bool framed = WaitUntil(() => remote.FrameVersion > 0, 8000) > 0;

        // One outstanding request at a time; Ask returns the child's answer.
        int hits = 0;
        string Ask()
        {
            string answer = "<<no-answer>>";
            using var done = new ManualResetEventSlim(false);
            remote.RequestSelectedText(t => { Interlocked.Increment(ref hits); answer = t; done.Set(); });
            done.Wait(4000);
            return answer;
        }

        bool noSelEmpty = framed && Ask() == "";

        // Drag-select "quick brown": press at offset 4, extend to offset 15, release.
        remote.MouseDown(OffsetX(4), lineY);
        remote.MouseMove(OffsetX(10), lineY);
        remote.MouseMove(OffsetX(15), lineY);
        remote.MouseUp(OffsetX(15), lineY);
        bool dragOk = Ask() == "quick brown";

        // A plain click with no movement clears the selection (the shell's rule).
        remote.MouseDown(OffsetX(0), lineY);
        remote.MouseUp(OffsetX(0), lineY);
        bool clickClears = Ask() == "";

        // Double-click takes the word under the pointer, triple-click the whole line.
        remote.MouseDown(OffsetX(12), lineY);
        remote.MouseUp(OffsetX(12), lineY);
        remote.MouseDown(OffsetX(12), lineY);
        remote.MouseUp(OffsetX(12), lineY);
        bool wordOk = Ask() == "brown";

        remote.MouseDown(OffsetX(12), lineY);
        remote.MouseUp(OffsetX(12), lineY);
        bool lineOk = Ask() == line;

        // A response whose correlation id nobody asked for fires no callback and does not
        // wedge the RPC: the next real request still answers, exactly once.
        hits = 0;
        remote.ProbeSelectedTextRequest(987654);
        string afterBogus = Ask();
        bool staleSafe = afterBogus == line && Volatile.Read(ref hits) == 1;

        Console.WriteLine($"[proctest] selection none={noSelEmpty} drag={dragOk} clickClear={clickClears} " +
                          $"word={wordOk} line={lineOk} staleId={staleSafe}");
        try { Directory.Delete(dir, true); } catch { }
        return noSelEmpty && dragOk && clickClears && wordOk && lineOk && staleSafe;
    }

    private static long WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return Math.Max(1, sw.ElapsedMilliseconds);
            Thread.Sleep(20);
        }
        return 0;
    }

    /// <summary>
    /// CJK composition works out of process: the shell forwards ImeUpdate/Commit/Cancel,
    /// the engine owns the composition (drawn into the frame at the caret), and every
    /// published frame carries the document-space caret + editable/password flags so the
    /// candidate window follows without a round trip. The commit assertion goes through
    /// real DOM + JS (the page's input listener writes the title), not an internal getter.
    /// </summary>
    private static bool ImeTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest_ime");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "ime.html");
        File.WriteAllText(page, """
            <!DOCTYPE html><html><head><title>start</title></head>
            <body style="margin:0">
            <input id="in" type="text" style="width:200px;height:30px">
            <div style="height:40px"></div>
            <input id="pw" type="password" style="width:160px;height:30px">
            <script>
              var inp = document.getElementById('in');
              inp.addEventListener('input', function(){ document.title = 'val:' + inp.value; });
            </script>
            </body></html>
            """);
        var url = new Uri(page).AbsoluteUri;

        using var remote = new RemoteTabProcess(84, url, 1f, 1f);
        var sw = Stopwatch.StartNew();
        while (!remote.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        remote.NavigateHtml(File.ReadAllText(page), url);
        bool framed = WaitUntil(() => remote.FrameVersion > 0, 8000) > 0;

        // Focus the text input by click: the frame reports editable focus and no
        // password flag — exactly what the shell's UpdateImeTarget consults in process mode.
        remote.MouseDown(10, 15);
        remote.MouseUp(10, 15);
        bool textFocus = WaitUntil(() =>
            remote.TryGetImeState(out _, out _, out _, out bool f, out bool p) && f && !p, 4000) > 0;

        // The composition is laid out, not just stored: the published caret x advances
        // with the pending composition's measured width.
        remote.ImeUpdate("nihao", 5, 5);
        bool caretUp = WaitUntil(() =>
            remote.TryGetImeState(out float cx, out float _, out float _, out bool f, out _) && f && cx > 10, 4000) > 0;
        remote.TryGetImeState(out float firstX, out _, out float caretH, out _, out _);
        remote.ImeUpdate("nihao shijie", 12, 12);
        bool caretGrew = WaitUntil(() =>
            remote.TryGetImeState(out float cx2, out _, out _, out bool f, out _) && f && cx2 > firstX + 8, 4000) > 0;
        bool caretSized = caretH > 8;
        remote.ImeCancel();

        // Commit inserts through the engine's typed-character path and fires 'input':
        // the page's own listener writes the title — an end-to-end IPC proof.
        remote.ImeUpdate("ni hao", 6, 6);
        remote.ImeCommit("你好");
        bool committed = WaitUntil(() => remote.Title == "val:你好", 6000) > 0;

        // The password block crosses the boundary: focusing the password input reports
        // IsPassword, which is what stops the shell from ever offering it as IME target.
        remote.MouseDown(10, 98);
        remote.MouseUp(10, 98);
        bool passwordFlag = WaitUntil(() =>
            remote.TryGetImeState(out _, out _, out _, out bool f, out bool p) && f && p, 4000) > 0;

        // Update-then-cancel inserts nothing: the value (and thus the title) is untouched.
        // (The child may batch both commands into one pass, so assert on the settled state,
        // not on a new frame.)
        remote.MouseDown(10, 15);
        remote.MouseUp(10, 15);
        remote.ImeUpdate("x", 1, 1);
        remote.ImeCancel();
        bool cancelSilent = WaitUntil(() => remote.Title != "val:你好", 500) == 0 && remote.Title == "val:你好";

        Console.WriteLine($"[proctest] ime focus={textFocus} caret={caretUp} grew={caretGrew} " +
            $"h={caretSized} password={passwordFlag} commit={committed} cancel={cancelSilent} framed={framed}");
        try { Directory.Delete(dir, true); } catch { }
        return framed && textFocus && caretUp && caretGrew && caretSized && passwordFlag && committed && cancelSilent;
    }

    /// <summary>Performance invariants: idle pages send zero frames, small DOM
    /// writes send thin row bands, pure scrolls send shift+band only.</summary>
    private static Task<bool> PerformanceTests() => Task.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "upbrowser_proctest3");
        Directory.CreateDirectory(dir);
        var staticPage = Path.Combine(dir, "static.html");
        var tickPage = Path.Combine(dir, "tick.html");
        File.WriteAllText(staticPage, "<!DOCTYPE html><html><head><title>s</title></head><body style='margin:0'><h1>static</h1><div style='height:3000px;background:linear-gradient(#f00,#0f0)'>f</div></body></html>");
        File.WriteAllText(tickPage, """
            <!DOCTYPE html><html><head><title>t</title></head>
            <body style="margin:0">
            <div id="tick" style="font-size:12px">0</div>
            <div style="height:3000px;background:linear-gradient(#00f,#ff0)">filler</div>
            <script>var n=0;setInterval(function(){n++;document.getElementById('tick').textContent='tick '+n;},100);</script>
            </body></html>
            """);

        using var remote = new RemoteTabProcess(97, "", 1f, 1f);
        var view = new RemoteTabView(remote);
        // Frames now commit through shared memory; the per-frame tuple records the
        // mode and the damage-byte volume the commit carried (what the shell folds).
        var frames = new System.Collections.Concurrent.ConcurrentQueue<(int mode, int pixLen, int w, int h)>();
        remote.OnFrameArrived += () =>
        {
            view.UpdateFromFrame();
            if (remote.TryGetFrame(out var mode, out int w, out int h, out _, out _, out _, out _,
                    out _, out _, out long damageBytes, out _))
                frames.Enqueue(((int)mode, (int)damageBytes, w, h));
        };
        var sw0 = Stopwatch.StartNew();
        while (!remote.IsConnected && sw0.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        if (!remote.IsConnected) return false;

        // A) Idle page: after the first frame nothing more may be sent.
        remote.NavigateHtml(File.ReadAllText(staticPage), new Uri(staticPage).AbsoluteUri);
        bool first = WaitUntil(() => remote.FrameVersion > 0, 6000) > 0;
        Thread.Sleep(200);
        long vStable = remote.FrameVersion;
        Thread.Sleep(500);
        bool idleSilent = remote.FrameVersion == vStable;

        // B) 12px text tick: frames arrive as thin damage bands.
        while (frames.TryDequeue(out _)) { }
        remote.NavigateHtml(File.ReadAllText(tickPage), new Uri(tickPage).AbsoluteUri);
        Thread.Sleep(700);
        int total = 0; long bandMax = 0; int bandFrames = 0;
        while (frames.TryDequeue(out var f))
        {
            total = f.w * f.h * 4;
            if (f.mode != (int)FrameMode.Full) { bandFrames++; bandMax = Math.Max(bandMax, f.pixLen); }
        }
        bool damageSmall = bandFrames >= 2 && total > 0 && bandMax < total / 4;

        // C) Scroll on a three-band color page. A hidden bar at document
        // y≈1100 turns red when hovered; the mouse is parked so it only hovers
        // the bar AFTER the second scroll — that scroll changes content AND
        // moves the viewport in one frame, exercising the shifted-diff
        // ScrollBlit path. The changed rows stay contiguous (bar inside the
        // newly exposed bottom strip), so frames remain band-sized.
        var bandPage = Path.Combine(dir, "bands.html");
        File.WriteAllText(bandPage, "<!DOCTYPE html><html><head><title>b</title><style>#bar{background:rgb(0,0,255)}#bar:hover{background:rgb(255,0,0)}</style></head><body style='margin:0'>" +
            "<div style='height:600px;background:rgb(0,255,0)'>f</div>" +
            "<div style='height:600px;padding-top:500px;background:rgb(0,0,255)'><div id=bar style='height:40px;width:900px'>f</div></div>" +
            "<div style='height:600px;background:rgb(255,255,0)'>f</div></body></html>");
        remote.NavigateHtml(File.ReadAllText(bandPage), new Uri(bandPage).AbsoluteUri);
        Thread.Sleep(300);
        while (frames.TryDequeue(out _)) { }
        remote.MouseMove(700, 700);          // doc 700: plain blue, no hover
        Thread.Sleep(80);
        remote.ScrollTo(0, 200);             // pure scroll (bar enters view bottom, no hover)
        Thread.Sleep(150);
        remote.ScrollTo(0, 420);             // bar now at rows 680..720, hovered red + scroll
        Thread.Sleep(300);
        int scrollFrames = 0;
        while (frames.TryDequeue(out var f))
        {
            if (f.mode != (int)FrameMode.DamageRows) scrollFrames++;
        }
        // Scroll policy: a pure scroll ships one shift+strip (ScrollBlit) frame, a
        // content-changing scroll falls back to Full — bounded count (no runaway)
        // and exact alignment; bandwidth is not asserted.
        bool scrollBounded = scrollFrames >= 1 && scrollFrames <= 6;

        // Alignment: bitmap row y must show document row 420+y ⇒
        // y<180 band0 GREEN; bar rows 680..720 RED (hover); other band1 rows BLUE.
        bool blitAligned = false;
        var abmp = view.Bitmap;
        if (abmp != null && Math.Abs(view.FrameScrollY - 420) < 2)
        {
            bool All(int[] ys, byte r, byte g, byte b)
            {
                foreach (int y in ys)
                {
                    if (y >= abmp.Height) continue;
                    var c = abmp.GetPixel(700, y);
                    if (Math.Abs(c.Red - r) > 40 || Math.Abs(c.Green - g) > 40 || Math.Abs(c.Blue - b) > 40)
                        return false;
                }
                return true;
            }
            blitAligned = All(new[] { 60, 150 }, 0, 255, 0) &&
                          All(new[] { 260, 500 }, 0, 0, 255) &&
                          All(new[] { 690, 710 }, 255, 0, 0) &&
                          All(new[] { 740 }, 0, 0, 255);
        }
        Console.WriteLine($"[proctest] blitAligned={blitAligned}");

        bool viewOk = view.HasFrame;
        // Scroll BACK UP (negative delta) must land aligned too: at scroll 0 the
        // bar is off-screen → rows 0..599 green, rows 600..767 plain blue.
        bool contentPainted = false;
        remote.ScrollTo(0, 0);
        Thread.Sleep(250);
        var bmp = view.Bitmap;
        if (bmp != null && Math.Abs(view.FrameScrollY) < 2)
        {
            contentPainted = true;
            foreach (int y in new[] { 60, 150, 400 })
            {
                var c = bmp.GetPixel(700, y);
                if (c.Red > 60 || c.Green < 200 || c.Blue > 60) { contentPainted = false; break; }
            }
            foreach (int y in new[] { 620, 700 })
            {
                if (!contentPainted || y >= bmp.Height) continue;
                var c = bmp.GetPixel(700, y);
                if (c.Red > 60 || c.Green > 60 || c.Blue < 200) { contentPainted = false; break; }
            }
        }

        // E) Fast flick: a burst of scroll steps must converge fast and land
        // aligned. Frames are shift+strip (or Full when the shell lags); only the
        // COUNT is bounded (each coalesced scroll position may ship at most one
        // frame, plus settle).
        while (frames.TryDequeue(out _)) { }
        for (int i = 1; i <= 8; i++)
        {
            remote.ScrollTo(0, i * 170);
            Thread.Sleep(15);
        }
        bool flickConverged = WaitUntil(() => Math.Abs(view.FrameScrollY - 1360) < 2, 1200) > 0;
        int framesDuringFlick = 0;
        while (frames.TryDequeue(out var f)) framesDuringFlick++;
        bool flickClean = framesDuringFlick >= 4 && framesDuringFlick <= 16;
        bool flickAligned = false;
        var fb = view.Bitmap;
        if (flickConverged && fb != null)
        {
            // doc 1360+y: y=100 → 1460 inside band1 (blue); y=500 → 1860 band2 (yellow).
            var c1 = fb.GetPixel(700, 100);
            var c2 = fb.GetPixel(700, 500);
            flickAligned = c1.Blue > 200 && c1.Red < 60 && c1.Green < 60 &&
                           c2.Red > 200 && c2.Green > 200 && c2.Blue < 60;
        }

        // D) A pure-compute 20ms timer (no DOM writes) must produce NO rebuild
        // frames at all — mutation-gated dirty marking in the child.
        var calcPage = Path.Combine(dir, "calc.html");
        File.WriteAllText(calcPage, "<!DOCTYPE html><html><head><title>c</title></head><body style='margin:0'><h1>calc</h1><script>var x=0;setInterval(function(){x+=1;},20);</script></body></html>");
        long vPreCalc = remote.FrameVersion;
        remote.NavigateHtml(File.ReadAllText(calcPage), new Uri(calcPage).AbsoluteUri);
        bool calcFirstFrame = WaitUntil(() => remote.FrameVersion > vPreCalc, 6000) > 0;
        long vAfterCalcFirst = remote.FrameVersion;
        // Allow a couple of legitimate warm-up frames (DOMContentLoaded/scripts);
        // a 20ms no-op timer would produce ~30 in this window if ungated.
        bool calcSilent = WaitUntil(() => remote.FrameVersion > vAfterCalcFirst + 3, 600) == 0;

        Console.WriteLine($"[proctest] idleSilent={idleSilent} bandFrames={bandFrames} bandMaxKB={bandMax / 1024} " +
                          $"damageSmall={damageSmall} scrollFrames={scrollFrames} scrollBounded={scrollBounded} " +
                          $"viewOk={viewOk} contentPainted={contentPainted} calcFirst={calcFirstFrame} calcSilent={calcSilent} " +
                          $"flick={flickConverged}/{flickClean}/{flickAligned} framesInFlick={framesDuringFlick}");
        try { Directory.Delete(dir, true); } catch { }
        view.Dispose();
        return first && idleSilent && damageSmall && scrollBounded && blitAligned && viewOk && contentPainted
               && flickConverged && flickClean && flickAligned && calcFirstFrame && calcSilent;
    });
}
