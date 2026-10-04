using SkiaSharp;
using System.Diagnostics;
using Acrux.Process;

namespace Acrux;

/// <summary>
/// Scroll-throughput stress probe (no window needed): loads a page into a real
/// tab-host child, drives a 60Hz burst of ScrollTo steps like an actively
/// scrolling user, then measures how long the child takes to catch up and what
/// frame economy it used. Regressions in child raster throughput (e.g. losing
/// op-culling) show up as catchUpMs blowup.
///
///   Acrux --scrollstress [htmlFile] [scrollPx]
/// </summary>
internal static class ScrollStressTest
{
    public static int Run(string[] args)
    {
        string? htmlFile = args.Length > 1 ? args[1] : null;
        int targetPx = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 6000;

        string html = htmlFile != null && File.Exists(htmlFile)
            ? File.ReadAllText(htmlFile)
            : MakeTallPage(6000);
        string url = htmlFile != null ? new Uri(Path.GetFullPath(htmlFile)).AbsoluteUri : "file:///stress/tall.html";

        using var remote = new RemoteTabProcess(94, "", 1f, 1f);
        var view = new RemoteTabView(remote);
        int full = 0, blit = 0, rows = 0, totalFrames = 0;
        long pixBytes = 0;
        remote.OnFrameArrived += () =>
        {
            view.UpdateFromFrame();
            if (remote.TryGetFrame(out var mode, out _, out _, out _, out _,
                    out _, out _, out _, out _, out long damageBytes, out _))
            {
                totalFrames++; pixBytes += damageBytes;
                switch (mode)
                {
                    case FrameMode.Full: full++; break;
                    case FrameMode.ScrollBlit: blit++; break;
                    case FrameMode.DamageRows: rows++; break;
                }
            }
        };

        var sw0 = Stopwatch.StartNew();
        while (!remote.IsConnected && sw0.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        if (!remote.IsConnected) { Console.WriteLine("[scrollstress] connect failed"); return 1; }

        remote.NavigateHtml(html, url);
        if (WaitUntil(() => remote.FrameVersion > 0, 60000) == 0)
        {
            Console.WriteLine("[scrollstress] initial frame timeout");
            return 1;
        }
        Thread.Sleep(300);
        int settleFrames = totalFrames;

        // 60Hz scroll burst, 150px per tick (like a fast wheel flick chain).
        const int steps = 40;
        float stepPx = targetPx / (float)steps;
        var swBurst = Stopwatch.StartNew();
        for (int i = 1; i <= steps; i++)
        {
            remote.ScrollTo(0, stepPx * i);
            Thread.Sleep(16);
        }
        float finalY = stepPx * steps;
        // Catch-up latency: time until the applied frame reaches the target scroll.
        long catchUp = WaitUntil(() => Math.Abs(view.FrameScrollY - finalY) < 2, 15000);
        long burstMs = swBurst.ElapsedMilliseconds;

        // F) Fractional 60Hz smooth-scroll replay (the real GUI scroll pattern):
        // many small non-integer deltas with pointer motion; the composited view
        // must equal a freshly rendered frame at the final scroll — any band
        // duplication/ghosting shows up as a pixel diff.
        remote.ScrollTo(0, 0);
        Thread.Sleep(200);
        for (int i = 1; i <= 120; i++)
        {
            remote.ScrollTo(0, i * 3.7f);          // fractional, like smooth wheel
            if (i % 17 == 0) remote.MouseMove(400, 300 + (i % 50));
            Thread.Sleep(16);
        }
        float finalFrac = 120 * 3.7f;
        bool fracConverged = WaitUntil(() => Math.Abs(view.FrameScrollY - finalFrac) < 1.5f, 4000) > 0;
        Thread.Sleep(400); // let hover settle + trailing bands land
        bool fracOk = fracConverged;
        var fbmp = view.Bitmap;
        if (fracOk && fbmp != null)
        {
            // b1 = state composited purely from incremental blits. Forcing the
            // child to resend a fresh FULL frame at the same scroll gives the
            // ground truth; the two must match pixel-for-pixel.
            long vBefore = remote.FrameVersion;
            SKBitmap b1;
            using (var snap = SKImage.FromBitmap(fbmp))
            using (var data = snap.Encode(SKEncodedImageFormat.Png, 100))
                b1 = SKBitmap.Decode(data);
            remote.SetActive(false);
            Thread.Sleep(120);
            remote.SetActive(true);
            fracOk = WaitUntil(() => remote.FrameVersion > vBefore, 4000) > 0;
            if (fracOk)
            {
                view.UpdateFromFrame(); // apply the fresh Full reference
                Thread.Sleep(150);
                // Tolerant compare: whole-row band shifts leave ≤1px sub-pixel AA
                // deltas at text edges vs a fresh fractional repaint. A real
                // misalignment moves whole glyphs (diff > 40 per channel).
                long diff = 0;
                for (int y = 0; y < b1.Height; y += 3)
                    for (int x = 0; x < b1.Width; x += 3)
                    {
                        var a = b1.GetPixel(x, y);
                        var b = fbmp.GetPixel(x, y);
                        if (Math.Abs(a.Red - b.Red) > 40 || Math.Abs(a.Green - b.Green) > 40 ||
                            Math.Abs(a.Blue - b.Blue) > 40) diff++;
                    }
                Console.WriteLine($"[scrollstress] fracDiffPixels={diff} (of ~{b1.Height / 3 * (b1.Width / 3)})");
                b1.Dispose();
                fracOk = diff < 200;
            }
        }

        Console.WriteLine($"[scrollstress] catchUpMs={(catchUp == 0 ? -1 : catchUp)} burstMs={burstMs} " +
                          $"frames={totalFrames - settleFrames} full={full} blit={blit} rows={rows} " +
                          $"MB={pixBytes / 1048576.0:F1} fracOk={fracOk} opsPage≈6000");
        bool pass = catchUp != 0 && catchUp < 1000 && fracOk;

        // G) Animation + scroll: on a page with an infinite CSS animation, every
        // frame changes content AND (during a burst) position. The composited
        // result must still match a fresh full frame — ghosting regressions in
        // the shifted-diff blit path only show up here.
        var animPage = Path.Combine(Path.GetTempPath(), "upb_anim_scroll.html");
        File.WriteAllText(animPage, "<!DOCTYPE html><html><head><style>" +
            "@keyframes pulse{0%{background:#f00}50%{background:#00f}100%{background:#f00}}" +
            ".a{animation:pulse 0.4s infinite;height:80px}</style></head>" +
            "<body style='margin:0'>" +
            "<div class=a>1</div><div style='height:80px;background:#0f0'>2</div><div class=a>3</div>" +
            "<div style='height:80px;background:#ff0'>4</div><div class=a>5</div>" +
            "<div style='height:6000px;background:linear-gradient(#123,#456)'>filler</div></body></html>");
        remote.NavigateHtml(File.ReadAllText(animPage), new Uri(animPage).AbsoluteUri);
        bool animFirst = WaitUntil(() => view.FrameScrollY < 1000 && Math.Abs(view.FrameScrollY) < 1, 30000) > 0;
        Thread.Sleep(250);
        for (int i = 1; i <= 50; i++)
        {
            remote.ScrollTo(0, i * 9.3f);   // scroll while the animation churns dlSerial
            Thread.Sleep(16);
        }
        float animY = 50 * 9.3f;
        bool animConverged = WaitUntil(() => Math.Abs(view.FrameScrollY - animY) < 1.5f, 4000) > 0;
        bool animOk = false;
        var abmp = view.Bitmap;
        if (animFirst && animConverged && abmp != null)
        {
            Thread.Sleep(250);
            long vRef = remote.FrameVersion;
            SKBitmap b2;
            using (var snap2 = SKImage.FromBitmap(abmp))
            using (var data2 = snap2.Encode(SKEncodedImageFormat.Png, 100))
                b2 = SKBitmap.Decode(data2);
            remote.SetActive(false);
            Thread.Sleep(120);
            remote.SetActive(true);
            // The reference full frame lands within a frame or two; animation
            // keeps content moving, so compare only rows outside the animated
            // boxes (the gradient filler) where state is scroll-deterministic.
            if (WaitUntil(() => remote.FrameVersion > vRef, 4000) > 0)
            {
                view.UpdateFromFrame();
                Thread.Sleep(60);
                view.UpdateFromFrame();
                long diff2 = 0;
                // Scroll 465 puts the viewport fully inside the static gradient
                // filler (animated boxes end at doc y 400) — every row is
                // scroll-deterministic, so the diff must be ~zero.
                int y0 = 4;
                for (int y = y0; y < b2.Height; y += 2)
                    for (int x = 0; x < b2.Width; x += 2)
                        if (b2.GetPixel(x, y) != abmp.GetPixel(x, y)) diff2++;
                long total2 = (long)(b2.Height - y0) / 2 * (b2.Width / 2);
                Console.WriteLine($"[scrollstress] animFillerDiff={diff2}/{total2}");
                animOk = diff2 * 100 <= total2; // ≤1%
            }
            b2.Dispose();
        }
        Console.WriteLine($"[scrollstress] anim: first={animFirst} conv={animConverged} ok={animOk}");
        pass &= animOk;
        Console.WriteLine(pass ? "[scrollstress] PASS" : "[scrollstress] WARN — child raster not keeping up");
        view.Dispose();
        return 0;
    }

    private static string MakeTallPage(int divs)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><title>big</title></head><body style='margin:0'>");
        string[] colors = { "#eef", "#fef", "#ffe", "#efe" };
        for (int i = 0; i < divs; i++)
            sb.Append($"<div style='height:20px;background:{colors[i % 4]}'>row {i} some text content here</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static long WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return Math.Max(1, sw.ElapsedMilliseconds);
            Thread.Sleep(10);
        }
        return 0;
    }
}
