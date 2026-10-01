using System.Globalization;
using System.Text;
using SkiaSharp;
using UpBrowser.Process;

namespace UpBrowser;

/// <summary>
/// A tab, from the point of view of a test that wants to drive it: the same handful of
/// input calls and the same handful of observable answers, whether the engine runs in a
/// child process or in this one.
/// </summary>
internal interface ITabDriver : IDisposable
{
    string Name { get; }
    void Resize(float w, float h);
    void Navigate(string url);
    void PointerDown(float x, float y);
    void PointerUp(float x, float y);
    void PointerMove(float x, float y);
    void Wheel(float dx, float dy, float x, float y);
    void Key(ushort charCode, ushort key, bool repeat);
    void Char(ushort charCode);
    void SetScroll(float x, float y);
    void SelectAll();
    string SelectedText();
    bool SelectOpen { get; }
    float ScrollX { get; }
    float ScrollY { get; }
    float ContentHeight { get; }
    string Title { get; }
    string Url { get; }
    SKBitmap? Bitmap { get; }
    /// <summary>Let the tab settle: commands are queued, and a remote one round-trips.</summary>
    void Settle(int ms);
}

internal sealed class LocalTabDriver : ITabDriver
{
    private readonly InProcessTabHost _host;
    public LocalTabDriver() => _host = new InProcessTabHost(0, 1f, 1f);
    public string Name => "local";
    public void Resize(float w, float h) => _host.Resize(w, h);
    public void Navigate(string url) => _host.Navigate(url);
    public void PointerDown(float x, float y) => _host.PointerDown(x, y);
    public void PointerUp(float x, float y) => _host.PointerUp(x, y);
    public void PointerMove(float x, float y) => _host.PointerMove(x, y);
    public void Wheel(float dx, float dy, float x, float y) => _host.Wheel(dx, dy, x, y);
    public void Key(ushort c, ushort k, bool r) => _host.Key(c, k, r);
    public void Char(ushort c) => _host.Char(c);
    public void SetScroll(float x, float y) => _host.SetScroll(x, y);
    public void SelectAll() => _host.SelectAll();
    public string SelectedText() => _host.Query(e => e.SelectedText);
    public bool SelectOpen => _host.Query(e => e.HasOpenSelect);
    public float ScrollX => _host.ScrollX;
    public float ScrollY => _host.ScrollY;
    public float ContentHeight => _host.Query(e => e.ContentHeight);
    public string Title => _host.Title;
    public string Url => _host.Url;
    public SKBitmap? Bitmap => _host.Bitmap;
    public void Settle(int ms) => Thread.Sleep(ms);
    public void Dispose() => _host.Dispose();
}

internal sealed class RemoteTabDriver : ITabDriver
{
    private readonly RemoteTabProcess _proc;
    private readonly RemoteTabView _view;
    private long _frames;
    private float _wheelX, _wheelY, _vw = 1024, _vh = 768;

    public RemoteTabDriver()
    {
        _proc = new RemoteTabProcess(0, "", 1f, 1f);
        _view = new RemoteTabView(_proc);
        _proc.OnFrameArrived += () => Interlocked.Increment(ref _frames);
        // The child decides element scrolling and answers; the root scroll belongs to the
        // host, which here is this driver — the same job BrowserApp does for a real tab.
        _proc.OnWheelResult += consumed =>
        {
            if (consumed) return;
            float dx = _wheelX, dy = _wheelY;
            _wheelX = _wheelY = 0;
            _proc.ScrollTo(Math.Clamp(_view.FrameScrollX + dx, 0, Math.Max(0, _view.ContentW - _vw)),
                Math.Clamp(_view.FrameScrollY + dy, 0, Math.Max(0, _view.ContentH - _vh)));
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!_proc.IsConnected && sw.ElapsedMilliseconds < 8000) Thread.Sleep(20);
    }

    public string Name => "remote";
    public void Resize(float w, float h) { _vw = w; _vh = h; _proc.Resize(w, h); }
    public void Navigate(string url) => _proc.Navigate(url);
    public void PointerDown(float x, float y) => _proc.MouseDown(x, y);
    public void PointerUp(float x, float y) => _proc.MouseUp(x, y);
    public void PointerMove(float x, float y) => _proc.MouseMove(x, y);
    public void Wheel(float dx, float dy, float x, float y)
    {
        _wheelX = dx;
        _wheelY = dy;
        _proc.Wheel(dx, dy, x, y);
    }
    public void Key(ushort c, ushort k, bool r) => _proc.KeyDown(c, k, r);
    public void Char(ushort c) => _proc.Char(c);
    public void SetScroll(float x, float y) => _proc.ScrollTo(x, y);
    public bool SelectOpen { get { _view.UpdateFromFrame(); return _view.FrameSelectOpen; } }
    public void SelectAll() => _proc.SelectAll();

    public string SelectedText()
    {
        string text = "";
        using var done = new ManualResetEventSlim(false);
        _proc.RequestSelectedText(t => { text = t; done.Set(); });
        return done.Wait(TimeSpan.FromSeconds(5)) ? text : "<timeout>";
    }

    public float ScrollX { get { _view.UpdateFromFrame(); return _view.FrameScrollX; } }
    public float ScrollY { get { _view.UpdateFromFrame(); return _view.FrameScrollY; } }
    public float ContentHeight { get { _view.UpdateFromFrame(); return _view.ContentH; } }
    public string Title => _proc.Title;
    public string Url => _proc.Url;
    public SKBitmap? Bitmap { get { _view.UpdateFromFrame(); return _view.Bitmap; } }
    public void Settle(int ms) => Thread.Sleep(ms);
    public void Dispose() => _view.Dispose();
}

/// <summary>
/// Runs a scripted interaction against a tab and records what the page answered.
///
/// The point is the diff between two drivers: the child process and the in-process host
/// execute the same script over the same engine, so their traces must match. That is the
/// only thing standing between "the shell consumes the page engine" and "the shell's
/// interaction behaviour changed", because the static pixel gates cannot see interaction.
/// </summary>
internal static class InteractionScript
{
    public static int Run(string[] args)
    {
        // --interact <page> <script.txt> [outPrefix]
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: UpBrowser --interact <file.html|url> <script.txt> [outPrefix]");
            return 2;
        }
        string target = args[1];
        string scriptPath = args[2];
        string prefix = args.Length > 3 ? args[3] : "";
        string url = ToUrl(target);
        var baseUri = new Uri(url);
        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"[interact] script not found: {scriptPath}");
            return 1;
        }

        var lines = File.ReadAllLines(scriptPath);
        var local = RunOne(new LocalTabDriver(), baseUri, lines, prefix + ".local");
        var remote = RunOne(new RemoteTabDriver(), baseUri, lines, prefix + ".remote");

        bool same = local.Trace == remote.Trace;
        Console.WriteLine($"[interact] traceMatch={same} steps={local.Steps}");
        if (prefix.Length > 0)
        {
            try
            {
                File.WriteAllText(prefix + ".local.trace.txt", local.Trace);
                File.WriteAllText(prefix + ".remote.trace.txt", remote.Trace);
            }
            catch { }
        }
        foreach (var step in local.Trace.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Console.WriteLine($"[interact]   {step}");
        if (!same)
        {
            var l = local.Trace.Split('\n');
            var r = remote.Trace.Split('\n');
            for (int i = 0; i < Math.Max(l.Length, r.Length); i++)
            {
                string a = i < l.Length ? l[i] : "<missing>", b = i < r.Length ? r[i] : "<missing>";
                if (a != b) Console.WriteLine($"[interact] step {i} LOCAL {a}\n[interact] step {i} REMOTE {b}");
            }
        }
        bool pixels = true;
        string pixelWhy = "";
        if (prefix.Length > 0 && File.Exists(prefix + ".local.png") && File.Exists(prefix + ".remote.png"))
            pixels = FrameSnapshotCli.SamePixels(prefix + ".local.png", prefix + ".remote.png", out pixelWhy);
        Console.WriteLine(pixels ? "[interact] finalFrame=match" : $"[interact] finalFrame={pixelWhy}");
        return same && pixels ? 0 : 1;
    }

    private static (string Trace, int Steps) RunOne(ITabDriver d, Uri baseUri, string[] lines, string pngPrefix)
    {
        var trace = new StringBuilder();
        int steps = 0, mark = 0;
        try
        {
            d.Navigate(baseUri.AbsoluteUri);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (p[0])
                {
                    case "resize" when p.Length >= 3:
                        d.Resize(F(p[1]), F(p[2])); break;
                    case "goto":
                        // Relative to the page, resolved the way a browser resolves it: the
                        // URL is the base, not a path on disk.
                        d.Navigate(new Uri(baseUri, p[1]).AbsoluteUri);
                        break;
                    case "settle" when p.Length >= 2:
                        d.Settle((int)F(p[1])); continue;
                    case "down" when p.Length >= 3: d.PointerDown(F(p[1]), F(p[2])); break;
                    case "up" when p.Length >= 3: d.PointerUp(F(p[1]), F(p[2])); break;
                    case "move" when p.Length >= 3: d.PointerMove(F(p[1]), F(p[2])); break;
                    case "click" when p.Length >= 3:
                        d.PointerDown(F(p[1]), F(p[2])); d.Settle(60); d.PointerUp(F(p[1]), F(p[2])); break;
                    case "dblclick" when p.Length >= 3:
                        d.PointerDown(F(p[1]), F(p[2])); d.PointerUp(F(p[1]), F(p[2])); d.Settle(30);
                        d.PointerDown(F(p[1]), F(p[2])); d.PointerUp(F(p[1]), F(p[2])); break;
                    case "wheel" when p.Length >= 5: d.Wheel(F(p[1]), F(p[2]), F(p[3]), F(p[4])); break;
                    case "scrollto" when p.Length >= 3: d.SetScroll(F(p[1]), F(p[2])); break;
                    case "key" when p.Length >= 2: d.Key(0, Keycode(p[1]), false); break;
                    case "type" when p.Length >= 2:
                        foreach (var ch in p[1]) d.Char((ushort)ch);
                        break;
                    case "selectall": d.SelectAll(); break;
                    case "mark":
                        steps++;
                        trace.Append(p.Length > 1 ? p[1] : $"m{mark++}").Append('|')
                            .Append(Num(d.ScrollX)).Append(',').Append(Num(d.ScrollY))
                            .Append("/h").Append(Num(d.ContentHeight)).Append('|')
                            .Append(d.Title).Append('|').Append(d.Url.Split('/').Last()).Append('|')
                            .Append("open=").Append(d.SelectOpen ? '1' : '0').Append('|')
                            .Append(Sanitize(d.SelectedText())).Append('\n');
                        continue;
                    default:
                        Console.Error.WriteLine($"[interact] unknown step: {line}");
                        break;
                }
                d.Settle(120);
            }
            d.Settle(400);
            if (pngPrefix.Length > 0 && d.Bitmap is { } bmp)
            {
                using var image = SKImage.FromBitmap(bmp);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var fs = File.Create(pngPrefix + ".png");
                data.SaveTo(fs);
            }
        }
        catch (Exception ex)
        {
            trace.Append("ERROR|").Append(d.Name).Append('|').Append(ex.GetType().Name).Append('|').Append(ex.Message).Append('\n');
        }
        finally { d.Dispose(); }
        return (trace.ToString().TrimEnd('\n'), steps);
    }

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Accepts a URL, a Windows path, or the <c>file:/C:/...</c> form a POSIX
    /// shell rewrites a <c>/tmp/...</c> argument into — the last of those is not a URL by
    /// the <c>"://"</c> test, and treating it as a relative path invents a bogus one.</summary>
    public static string ToUrl(string target)
    {
        if (target.Contains("://")) return target;
        if (target.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return new Uri(target).AbsoluteUri;
        return new Uri(Path.GetFullPath(target)).AbsoluteUri;
    }
    private static string Num(float v) => (Math.Round(v * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);
    private static string Sanitize(string s) => s.Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>Named keys the script can press; anything else is taken as a code point.</summary>
    private static ushort Keycode(string name) => name.ToLowerInvariant() switch
    {
        "enter" => 13, "esc" or "escape" => 27, "tab" => 9, "backspace" => 8, "del" or "delete" => 46,
        "up" => 38, "down" => 40, "left" => 37, "right" => 39, "home" => 36, "end" => 35,
        "a" => 65, "c" => 67, "v" => 86, "f" => 70, _ => ushort.Parse(name),
    };
}
