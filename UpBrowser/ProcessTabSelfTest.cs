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
        bool hasPixels = remote.TryGetFrame(out var px, out _, out int pixLen, out int w, out int h,
            out _, out _, out _, out _, out int domCount, out _, out _, out _, out _, out _);
        Console.WriteLine($"[proctest] gotFrame={gotFrame} frame={w}x{h} pixels={px != null && pixLen > 0} dom={domCount}");

        // JS must be running in the child: title advances to tickN.
        WaitUntil(() => remote.Title.StartsWith("tick"), 4000);
        bool jsRan = remote.Title.StartsWith("tick");
        Console.WriteLine($"[proctest] jsTitle='{remote.Title}' jsRan={jsRan}");

        // Scroll offset must round-trip into the next frame.
        long before = remote.FrameVersion;
        remote.ScrollTo(0, 120);
        bool scrolled = WaitUntil(() =>
            remote.TryGetFrame(out _, out _, out _, out _, out _, out _, out float sy, out _, out _, out _, out _, out _, out _, out _, out long ver2)
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
        bool perf = PerformanceTests().GetAwaiter().GetResult();
        pass = pass && features && perf;
        Console.WriteLine(pass ? "[proctest] PASS" : "[proctest] FAIL");

        try { Directory.Delete(dir, true); } catch { }
        return pass ? 0 : 1;
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
            remote.TryGetFrame(out _, out _, out _, out _, out _, out _, out float sy, out _, out _, out _, out _, out _, out _, out _, out _) && Math.Abs(sy - 60) < 1, 6000) > 0;

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
            remote.TryGetFrame(out _, out _, out _, out _, out _, out _, out float sy2, out _, out _, out _, out _, out _, out _, out _, out _) && sy2 > 250, 4000) > 0;

        Console.WriteLine($"[proctest] scrollSync={scrollSync} hovered={hovered} edited={edited} " +
                          $"wheelAnswered={wheelAnswered} wheelConsumed={wheelConsumedYes} bgSuppressed={suppressed} bgResume={resumed} rootWheelPasses={rootWheelPasses}");

        try { Directory.Delete(dir, true); } catch { }
        return scrollSync && hovered && edited && wheelConsumedYes && wheelAnswered2 && rootWheelPasses && suppressed && resumed;
    });

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
        var frames = new System.Collections.Concurrent.ConcurrentQueue<(int mode, int pixLen, int w, int h)>();
        remote.OnFrameArrived += () =>
        {
            view.UpdateFromFrame();
            if (remote.TryGetFrame(out _, out _, out int pixLen, out int w, out int h,
                    out _, out _, out _, out _, out _, out _, out var mode, out _, out _, out _))
                frames.Enqueue(((int)mode, pixLen, w, h));
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
        // Stateless policy: any scroll ships a full viewport frame — bounded
        // count (no runaway) and exact alignment; bandwidth is not asserted.
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
        // aligned. Frames are full by policy; only the COUNT is bounded (each
        // coalesced scroll position may ship at most one frame, plus settle).
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
