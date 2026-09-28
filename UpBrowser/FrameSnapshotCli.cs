using SkiaSharp;
using UpBrowser.Process;

namespace UpBrowser;

/// <summary>
/// Captures what a page-host child process actually paints, so the out-of-process
/// pipeline can be diffed pixel-for-pixel against the in-process one:
///
///   UpBrowser --snapshot in.html ref.png                       (in-process render)
///   UpBrowser --frameshot in.html proc.png                     (page-host render)
///   UpBrowser --diff ref.png proc.png diff.png 0.001           (must be clean)
///
/// Runs headless: no window is created, only the child process and its pipe.
/// </summary>
internal static class FrameSnapshotCli
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: UpBrowser --frameshot <file.html|url> <out.png> [width] [height]");
            return 2;
        }

        string target = args[1];
        string outPath = args[2];
        int width = args.Length > 3 ? int.Parse(args[3]) : 1024;
        int height = args.Length > 4 ? int.Parse(args[4]) : 768;

        string url = target.Contains("://")
            ? target
            : new Uri(Path.GetFullPath(target)).AbsoluteUri;

        var proc = new RemoteTabProcess(0, url, 1f, 1f);
        // Disposing the view tears the child down with it; skipping it would orphan the host.
        var view = new RemoteTabView(proc);
        try
        {
            return Capture(view, proc, url, width, height, outPath);
        }
        finally
        {
            view.Dispose();
        }
    }

    private static int Capture(RemoteTabView view, RemoteTabProcess proc, string url,
        int width, int height, string outPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!proc.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
        if (!proc.IsConnected) return Fail("page host never connected");

        proc.Resize(width, height);
        proc.Navigate(url);

        // Let the host settle: the frame version holding still means the page stopped
        // repainting — scripts, fonts and lazy images have had their say.
        long last = -1, stableSince = -1;
        while (sw.ElapsedMilliseconds < 15000)
        {
            long v = proc.FrameVersion;
            if (v != last) { last = v; stableSince = sw.ElapsedMilliseconds; }
            else if (last > 0 && sw.ElapsedMilliseconds - stableSince > 700) break;
            Thread.Sleep(20);
        }
        if (last <= 0) return Fail("no frame produced");

        if (!view.UpdateFromFrame()) return Fail("frame could not be folded");
        var bmp = view.Bitmap;
        if (bmp == null) return Fail("empty frame bitmap");

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using (var fs = File.Create(outPath)) data.SaveTo(fs);

        Console.WriteLine($"[frameshot] {outPath} {bmp.Width}x{bmp.Height} dom={view.DomCount} " +
                          $"content={view.ContentW:0}x{view.ContentH:0} in {sw.ElapsedMilliseconds}ms");
        return 0;
    }

    private static int Fail(string why)
    {
        Console.Error.WriteLine($"[frameshot] {why}");
        return 1;
    }
}
