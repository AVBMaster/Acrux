using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using SkiaSharp;
using UpBrowser.Core;
using UpBrowser.Core.Css;
using UpBrowser.Core.Dom;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Performance.Rendering;
using UpBrowser.Core.Performance.Resources;
using UpBrowser.Rendering;

namespace UpBrowser.Process;

/// <summary>
/// Child-process entry for a multi-process tab (`UpBrowser --tab-host`).
/// Owns the full page stack for one tab — document, layout, JS engine and
/// painting — rasterizes the viewport to BGRA frames and streams them over
/// the named pipe. All DOM/JS work stays on the host's main loop thread;
/// only network fetches run on worker tasks.
/// </summary>
internal static class TabHostApp
{
    public static int Run(string[] args)
    {
        string channel = "";
        int tab = 0;
        float dpi = 1f, res = 1f;
        foreach (var a in args)
        {
            if (a.StartsWith("--channel=")) channel = a[10..];
            else if (a.StartsWith("--tab=")) int.TryParse(a[6..], out tab);
            else if (a.StartsWith("--dpi=")) float.TryParse(a[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out dpi);
            else if (a.StartsWith("--res=")) float.TryParse(a[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out res);
        }
        if (string.IsNullOrEmpty(channel)) return 1;

        using var pipe = new NamedPipeClientStream(".", channel, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { pipe.Connect(8000); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TabHost {tab}] connect failed: {ex.Message}");
            return 1;
        }

        var host = new TabHost(tab, pipe, dpi, res);
        return host.Run();
    }
}

internal sealed class TabHost : IDisposable
{
    private readonly int _tab;
    private readonly Stream _pipe;
    private readonly object _pipeWrite = new();
    private readonly float _dpi, _res;
    private readonly ConcurrentQueue<TabMessage> _commands = new();
    private readonly Thread _readerThread;
    private readonly CancellationTokenSource _cts = new();

    // Main-loop-owned page state
    private readonly DocumentManager _docManager = new();
    private readonly JavaScriptEngine _engine;
    private readonly LayoutEngine _layoutEngine = new();
    private IncrementalLayoutEngine? _incremental;
    private readonly LayoutCache _layoutCache = new();
    private readonly Dictionary<string, SKTypeface> _typefaceCache = new();
    private readonly ImageCache _imageCache = new();
    private StyleComputer? _styleComputer;

    private Document? _document;
    private string _baseUrl = "";
    private string _title = "";
    private float _viewportW = 1024, _viewportH = 768;
    private float _scrollX, _scrollY;
    private float _contentW, _contentH;
    // Three-level dirty chain: layout ⇒ dl ⇒ raster; scroll/hover-free changes take cheaper paths.
    private bool _layoutDirty = true;
    private bool _dlDirty = true;
    private bool _renderDirty = true;
    private long _lastRenderTick;
    private DisplayList? _cachedDl;
    private SKColor _cachedBg = SKColors.White;
    private long _dlSerial;
    // Persistent raster + frame-diff state
    private SKBitmap? _work;
    private byte[]? _shadow;
    private bool _sentValid;
    private float _sentScrollX, _sentScrollY;
    // Device row _work's content actually stands for (child-internal band raster).
    private float _workDevY;
    private int _bandRun;
    private long _sentDlSerial;
    // Frame sender mailbox (latest-wins) + double-buffered packets
    private readonly byte[][] _pktScratch = new byte[2][];
    private readonly bool[] _pktFree = { true, true };
    private byte[]? _mboxPacket;
    private int _mboxLen, _mboxSlot = -1;
    private readonly AutoResetEvent _mboxSignal = new(false);
    private readonly AutoResetEvent _cmdSignal = new(false);
    private Thread? _senderThread;
    private int _navSeq;
    private bool _active = true;
    // Idle diagnostics: set UPBROWSER_TAB_STATS=1 to print a 2s heartbeat of
    // which pipeline stages are firing (commands/layout/dl/raster/frames).
    private readonly bool _stats = Environment.GetEnvironmentVariable("UPBROWSER_TAB_STATS") == "1";
    private long _stCmds, _stLayout, _stDl, _stRaster, _stFull, _stRows, _stBlit, _stSilent, _stRelayout, _stBytes, _stNextLog;
    private long _stReplayMs, _stDiffMs, _stRepBand, _stNBand, _stRepFull, _stNFull;
    private readonly long[] _stCmdHist = new long[32];
    private bool _docHasHoverRules;
    private Document? _hoverRulesDoc;
    private Element? _hoveredElement;

    // Form editing state (child owns focus/caret like the parent does in-proc)
    private Element? _focused;
    private int _caret;
    private int _selStart = -1;
    private bool _showCursor = true;
    private long _blinkTick = Environment.TickCount64;
    private Element? _pressedButton;

    public TabHost(int tab, Stream pipe, float dpi, float res)
    {
        _tab = tab;
        _pipe = pipe;
        _dpi = dpi;
        _res = res;
        _engine = new JavaScriptEngine(tab);
        _engine.ShowDialog = (msg, type) =>
        {
            // v1: report dialogs without blocking the JS thread.
            Send(TabMsg.Dialog, w => { TabFraming.WriteString(w, msg ?? ""); TabFraming.WriteString(w, type ?? ""); });
            return null;
        };

        // Page-compat: window metrics + JS-driven scrolling live in the child.
        if (_engine.Builtins != null)
        {
            var b = _engine.Builtins;
            b.GetInnerWidth = () => (int)_viewportW;
            b.GetInnerHeight = () => (int)_viewportH;
            b.GetDevicePixelRatio = () => _dpi * _res;
            b.GetScrollX = () => (int)_scrollX;
            b.GetScrollY = () => (int)_scrollY;
            b.OnScrollTo = (x, y) => SetScroll(x, y);
            b.OnScrollBy = (x, y) => SetScroll(_scrollX + x, _scrollY + y);
        }

        if (_engine.LocationHost != null)
        {
            _engine.LocationHost.OnNavigate = url =>
            {
                if (!string.IsNullOrWhiteSpace(url))
                    _commands.Enqueue(new TabMessage(TabMsg.Navigate,
                        TabFraming.BuildPayload(w => TabFraming.WriteString(w, url))));
            };
            _engine.LocationHost.OnReload = () =>
            {
                _commands.Enqueue(new TabMessage(TabMsg.Navigate,
                    TabFraming.BuildPayload(w => TabFraming.WriteString(w, _baseUrl))));
            };
        }
        _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = $"TabHostRead-{tab}" };
        _readerThread.Start();
        _senderThread = new Thread(SenderLoop) { IsBackground = true, Name = $"TabHostSend-{tab}" };
        _senderThread.Start();
    }

    private void SenderLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _mboxSignal.WaitOne(50);
                byte[] pkt; int len; int slot;
                lock (_mboxLock)
                {
                    if (_mboxPacket == null) continue;
                    pkt = _mboxPacket; len = _mboxLen; slot = _mboxSlot;
                    _mboxPacket = null; _mboxSlot = -1;
                }
                try
                {
                    lock (_pipeWrite) _pipe.Write(pkt, 0, len);
                }
                catch { _cts.Cancel(); return; }
                if (slot >= 0) lock (_mboxLock) _pktFree[slot] = true;
            }
        }
        catch { }
    }

    private readonly object _mboxLock = new();

    /// <summary>
    /// Hand a serialized frame packet to the sender thread. NEVER drop a queued
    /// frame: the diff baseline and the parent's bitmap advance exactly once per
    /// SENT packet — silently replacing an unsent one desynchronized them and
    /// made every later blit band land at the wrong offset (scroll ghosting).
    /// Callers only invoke this after AcquireFreeSlot confirmed a free slot.
    /// </summary>
    private void QueueFramePacket(byte[] packet, int len)
    {
        int slot = packet == _pktScratch[0] ? 0 : 1;
        lock (_mboxLock)
        {
            _mboxPacket = packet; _mboxLen = len; _mboxSlot = slot;
            _pktFree[slot] = false;
        }
        _mboxSignal.Set();
    }

    private int AcquireFreeSlot()
    {
        lock (_mboxLock)
        {
            for (int i = 0; i < 2; i++)
                if (_pktFree[i]) return i;
        }
        return -1;
    }

    private void ReaderLoop()
    {
        while (TabFraming.TryRead(_pipe, out var msg))
        {
            _commands.Enqueue(msg);
            _cmdSignal.Set();
            if (msg.Type == TabMsg.Close) return;
        }
        _commands.Enqueue(new TabMessage(TabMsg.Close, Array.Empty<byte>()));
        _cmdSignal.Set();
    }

    public int Run()
    {
        Send(TabMsg.Ready, _ => { });

        while (!_cts.IsCancellationRequested)
        {
            while (_commands.TryDequeue(out var msg))
            {
                if (_stats)
                {
                    _stCmds++;
                    _stCmdHist[(int)msg.Type % 32]++;
                }
                if (!Handle(msg)) return 0;
            }

            // Hover recompute deferred from scroll settles once the wheel stops:
            // running it per scroll tick fires mouseout/mouseover JS + a full
            // style/layout rebuild every frame, saturating the child so stale
            // offset frames linger on screen for hundreds of ms.
            if (_hoverScrollPending && _commands.Count == 0 &&
                Environment.TickCount64 - _lastScrollMoveTick >= 110)
            {
                _hoverScrollPending = false;
                if (_mouseVX >= 0) UpdateHoverAt(_mouseVX, _mouseVY);
            }

            PumpJs();

            bool wantRender = _layoutDirty || _dlDirty || _renderDirty;
            // Hidden tabs rebuild at most every 500ms (JS keeps running); the
            // active tab rebuilds at ~125fps max.
            long minInterval = _active ? 8 : 500;
            if (wantRender && Environment.TickCount64 - _lastRenderTick >= minInterval)
                RenderPass();

            if (_stats && Environment.TickCount64 >= _stNextLog)
            {
                _stNextLog = Environment.TickCount64 + 2000;
                var line = $"[TabHost {_tab} stats] cmds={_stCmds} relayout={_stRelayout} " +
                    $"layout={_stLayout} dl={_stDl} raster={_stRaster} " +
                    $"full={_stFull} rows={_stRows} blit={_stBlit} silent={_stSilent} KB={_stBytes / 1024} " +
                    $"rep={_stReplayMs / Math.Max(1, _stRaster)}ms bandRep={_stRepBand / Math.Max(1, _stNBand)}ms(n={_stNBand}) fullRep={_stRepFull / Math.Max(1, _stNFull)}ms(n={_stNFull}) diff={_stDiffMs / Math.Max(1, _stRaster)}ms " +
                    $"heapMB={GC.GetTotalMemory(false) / 1048576} wsMB={Environment.WorkingSet / 1048576}";
                for (int i = 0; i < _stCmdHist.Length; i++)
                    if (_stCmdHist[i] > 0) line += $" {((TabMsg)i)}:{_stCmdHist[i]}";
                Console.Error.WriteLine(line);
                try { File.AppendAllText($"upbrowser_tabstats_{_tab}.log", line + "\n"); } catch { }
            }

            // Event-driven idle: sleep until the next JS timer is due instead of
            // polling — an idle tab wakes a handful of times per minute.
            int waitMs;
            if (_commands.Count > 0) waitMs = 0;
            else
            {
                int due = _engine.IntegrationService?.NextTimerDelayMs() ?? int.MaxValue;
                if (_active)
                    waitMs = Math.Clamp(due == int.MaxValue ? 60 : due, 2, 60);
                else
                    waitMs = Math.Clamp(due == int.MaxValue ? 500 : due, 50, 500);
            }
            if (waitMs > 0) _cmdSignal.WaitOne(waitMs);
        }
        return 0;
    }

    /// <summary>Handle one command. Returns false when the host should exit.</summary>
    private bool Handle(in TabMessage msg)
    {
        switch (msg.Type)
        {
            case TabMsg.Navigate:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                NavigateTo(TabFraming.ReadString(r));
                break;
            }
            case TabMsg.NavigateHtml:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                var html = TabFraming.ReadString(r);
                var baseUrl = TabFraming.ReadString(r);
                ApplyHtml(html, baseUrl);
                break;
            }
            case TabMsg.MouseDown:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                HandleClick(r.ReadSingle(), r.ReadSingle(), true);
                break;
            }
            case TabMsg.MouseUp:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                HandleClick(r.ReadSingle(), r.ReadSingle(), false);
                break;
            }
            case TabMsg.MouseMove:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                HandleMove(r.ReadSingle(), r.ReadSingle());
                break;
            }
            case TabMsg.ScrollTo:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                float x = r.ReadSingle(), y = r.ReadSingle();
                if (Math.Abs(x - _scrollX) > 0.5f || Math.Abs(y - _scrollY) > 0.5f)
                {
                    _scrollX = x; _scrollY = y;
                    _renderDirty = true; // cull band follows the viewport
                    NoteScrollMoved();
                }
                break;
            }
            case TabMsg.Resize:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                float w = r.ReadSingle(), h = r.ReadSingle();
                if (w > 50 && h > 50 && (Math.Abs(w - _viewportW) > 0.5f || Math.Abs(h - _viewportH) > 0.5f))
                {
                    _viewportW = w; _viewportH = h;
                    MarkLayout();
                }
                break;
            }
            case TabMsg.KeyDown:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                HandleKey(r.ReadUInt16(), r.ReadUInt16(), r.ReadByte() != 0);
                break;
            }
            case TabMsg.Char:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                HandleChar(r.ReadUInt16());
                break;
            }
            case TabMsg.Wheel:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                HandleWheel(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                break;
            }
            case TabMsg.SetActive:
                bool active = msg.Payload.Length > 0 && msg.Payload[0] != 0;
                if (active != _active)
                {
                    _active = active;
                    // Coming back to the foreground: push a fresh full frame
                    // built from the display lists we kept warm while hidden.
                    if (_active) { _sentValid = false; _renderDirty = true; }
                    else _engine.IntegrationService?.ThrottleInactiveTabs();
                }
                break;
            case TabMsg.Close:
                return false;
        }
        return true;
    }

    private void PumpJs()
    {
        try
        {
            _engine.SetWindowSize((int)_viewportW, (int)_viewportH);
            var jsInt = _engine.IntegrationService;
            jsInt?.ProcessTimers();
            jsInt?.MicrotaskQueue.DrainMicrotasks();
            if (_engine.HasTimers)
                _engine.TickTimers();

            if (_engine.NeedsReLayout)
            {
                if (_stats)
                {
                    _stRelayout++;
                    if (_engine.DirtyTrace != null)
                    {
                        try { File.AppendAllText($"upbrowser_tabstats_{_tab}.log", $"[TabHost {_tab}] MarkDirty from:\n{_engine.DirtyTrace}\n"); } catch { }
                    }
                }
                _engine.ClearDirty();
                MarkLayout();
            }

            var t = _document?.Title ?? "";
            if (t != _title)
            {
                _title = t;
                Send(TabMsg.Title, w => TabFraming.WriteString(w, t));
            }

            // Caret blink for the focused field.
            if (_focused != null && Environment.TickCount64 - _blinkTick >= 530)
            {
                _blinkTick = Environment.TickCount64;
                _showCursor = !_showCursor;
                MarkPaint();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TabHost {_tab}] pump: {ex.Message}");
        }
    }

    // ==================== navigation ====================

    private void NavigateTo(string url)
    {
        // Internal browser pages are owned by the parent: report the URL and
        // wait for the parent to push the page content back via NavigateHtml.
        if (url.StartsWith("upbrowser://", StringComparison.OrdinalIgnoreCase))
        {
            Send(TabMsg.UrlChanged, w => TabFraming.WriteString(w, url));
            return;
        }

        int seq = ++_navSeq;
        Send(TabMsg.Loading, w => w.Write((byte)1));
        Task.Run(() =>
        {
            string html;
            try
            {
                html = FetchHtml(url);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TabHost {_tab}] fetch {url}: {ex.Message}");
                html = ErrorHtml(url, ex.Message);
            }
            if (seq != _navSeq) return; // superseded
            // Engine/DOM work must land on the host main loop thread.
            _commands.Enqueue(new TabMessage(TabMsg.NavigateHtml,
                TabFraming.BuildPayload(w => { TabFraming.WriteString(w, html); TabFraming.WriteString(w, url); })));
        });
    }

    private static string FetchHtml(string url)
    {
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var path = new Uri(url).LocalPath;
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        }
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var fetcher = new StreamingHttpFetcher();
            var resp = fetcher.FetchAsync(new ResourceRequest
            {
                Url = url,
                Kind = ResourceKind.Document,
                Priority = ResourcePriority.High,
                Timeout = TimeSpan.FromSeconds(15),
            }).GetAwaiter().GetResult();
            return System.Text.Encoding.UTF8.GetString(resp.Body);
        }
        return DocumentManager.DefaultHtml;
    }

    private static string ErrorHtml(string url, string error) =>
        $"<!DOCTYPE html><html><head><title>Error</title></head><body style='font-family:Arial;padding:40px'>" +
        $"<h1 style='color:#d32f2f'>Unable to connect</h1><p>{System.Net.WebUtility.HtmlEncode(url)}</p>" +
        $"<p style='color:#666'>{System.Net.WebUtility.HtmlEncode(error)}</p></body></html>";

    private void ApplyHtml(string html, string baseUrl)
    {
        try
        {
            _imageCache.Clear();
            _typefaceCache.Clear();
            PaintOpPool.Clear(); // recycle the previous page's pooled ops
            PaintVisitor.InstallReplacedIntrinsicSizes(_imageCache, baseUrl);

            var load = _docManager.LoadHtmlAsync(html, baseUrl, _viewportW, _viewportH, _dpi)
                .GetAwaiter().GetResult();

            _document = load.Document;
            _baseUrl = baseUrl;
            _styleComputer = load.StyleComputer;
            _incremental = null; // fresh node tree — drop the layout cache
            _layoutCache.Clear();
            _scrollX = _scrollY = 0;
            _hoveredElement = null;
            _hoverRulesDoc = null;
            _focused = null;
            _pressedButton = null;
            _selStart = -1;

            _engine.LoadDocument(load.Document);
            RunPageScripts(load.Document, baseUrl);
            _engine.IntegrationService?.FireDOMContentLoaded();

            MarkLayout();
            _sentValid = false;
            _title = load.Document.Title ?? "";
            Send(TabMsg.Title, w => TabFraming.WriteString(w, _title));
            Send(TabMsg.UrlChanged, w => TabFraming.WriteString(w, baseUrl));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TabHost {_tab}] load: {ex.Message}");
        }
        finally
        {
            Send(TabMsg.Loading, w => w.Write((byte)0));
        }
    }

    private void RunPageScripts(Document doc, string baseUrl)
    {
        var scripts = new List<Element>();
        var queue = new Queue<Element>();
        if (doc.DocumentElement != null) queue.Enqueue(doc.DocumentElement);
        while (queue.Count > 0)
        {
            var el = queue.Dequeue();
            if (string.Equals(el.TagName, "SCRIPT", StringComparison.OrdinalIgnoreCase)) scripts.Add(el);
            foreach (var c in el.Children)
                if (c is Element ce) queue.Enqueue(ce);
        }

        foreach (var s in scripts)
        {
            var src = s.GetAttribute("src");
            try
            {
                if (!string.IsNullOrEmpty(src))
                {
                    var url = ResolveUrl(src, baseUrl);
                    if (url == null) continue;
                    var code = FetchHtml(url);
                    _engine.Execute(code, url);
                }
                else
                {
                    var code = s.TextContent;
                    if (!string.IsNullOrWhiteSpace(code))
                        _engine.Execute(code);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TabHost {_tab}] script: {ex.Message}");
            }
        }
    }

    private static string? ResolveUrl(string url, string baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://") || url.StartsWith("data:"))
            return url;
        if (string.IsNullOrEmpty(baseUrl)) return null;
        try { return new Uri(new Uri(baseUrl), url).AbsoluteUri; }
        catch { return null; }
    }

    // ==================== input ====================

    /// <summary>
    /// Dispatch through the identity-mapped ElementHost — addEventListener
    /// listeners live on the same wrapper instance JS originally received
    /// (Core's DispatchEvent resolves it; GetDispatchHost builds custom events).
    /// </summary>
    private void DispatchDomEvent(Element? el, Func<ElementHost, ScriptEvent> make)
    {
        if (el == null) return;
        try { _engine.DispatchEvent(el, make(_engine.GetDispatchHost(el))); }
        catch { }
    }

    private void DispatchSimple(Element? el, string type)
    {
        try { DispatchDomEvent(el, h => new ScriptEvent(type, h)); } catch { }
    }

    private ElementHost WrapHost(Element el) => _engine.GetDispatchHost(el);

    private void HandleClick(float x, float y, bool down)
    {
        if (_document == null) return;
        var el = BrowserApp.HitTest(_document, x + _scrollX, y + _scrollY);

        try { DispatchSimple(el, down ? "mousedown" : "mouseup"); } catch { }

        var itype = el?.InputType?.ToLowerInvariant();
        bool isControl = itype == "checkbox" || itype == "radio" || el?.TagName == "BUTTON";

        if (down)
        {
            try { DispatchSimple(el, "click"); } catch { }

            if (el != null && el.IsTextEditable)
            {
                SetFocus(el);
                _caret = (el.Value ?? "").Length;
                _selStart = -1;
            }
            else if (el != null && !isControl)
            {
                SetFocus(null);
            }

            if (el != null && (itype == "checkbox" || itype == "radio"))
            {
                SetFocus(el);
                if (itype == "checkbox")
                {
                    if (el.HasAttribute("checked")) el.RemoveAttribute("checked");
                    else el.SetAttribute("checked", "");
                    try { DispatchSimple(el, "change"); } catch { }
                }
                else if (!el.HasAttribute("checked"))
                {
                    UncheckRadioGroup(el);
                    el.SetAttribute("checked", "");
                    try { DispatchSimple(el, "change"); } catch { }
                }
            }

            if (el?.TagName == "BUTTON")
            {
                _pressedButton = el;
                SetFocus(el);
            }

            // Link navigation
            for (var a = el; a != null; a = a.ParentElement)
            {
                if (string.Equals(a.TagName, "A", StringComparison.OrdinalIgnoreCase))
                {
                    var href = a.GetAttribute("href");
                    var target = ResolveUrl(href, _baseUrl);
                    if (!string.IsNullOrEmpty(target))
                        NavigateTo(target);
                    break;
                }
            }
        }
        else if (_pressedButton != null)
        {
            var btn = _pressedButton;
            _pressedButton = null;
            var btype = (btn.GetAttribute("type") ?? "submit").ToLowerInvariant();
            var form = FindForm(btn) as UpBrowser.Core.Dom.Html.HTMLFormElement;
            if (form != null)
            {
                if (btype == "submit") form.Submit();
                else if (btype == "reset") form.Reset();
            }
        }
        MarkPaint();
    }

    private void UncheckRadioGroup(Element radio)
    {
        if (_document == null) return;
        var name = radio.GetAttribute("name");
        var queue = new Queue<Node>();
        if (_document.DocumentElement != null) queue.Enqueue(_document.DocumentElement);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var c in n.Children)
            {
                if (c is Element ce)
                {
                    if (string.Equals(ce.TagName, "INPUT", StringComparison.OrdinalIgnoreCase) &&
                        ce.GetAttribute("type") == "radio" &&
                        ce.GetAttribute("name") == name &&
                        !ReferenceEquals(ce, radio))
                        ce.RemoveAttribute("checked");
                    queue.Enqueue(ce);
                }
            }
        }
    }

    private static Node? FindForm(Element el)
    {
        for (var p = el.ParentElement; p != null; p = (p as Element)?.ParentElement)
            if (string.Equals(p.TagName, "FORM", StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }

    private void SetFocus(Element? next)
    {
        if (ReferenceEquals(_focused, next)) return;
        var old = _focused;
        _focused = next;
        _caret = 0;
        _selStart = -1;
        _showCursor = true;
        _blinkTick = Environment.TickCount64;
        try { if (old != null) DispatchSimple(old, "blur"); } catch { }
        try { if (next != null) DispatchSimple(next, "focus"); } catch { }
        MarkPaint();
    }

    private bool TryEditable(out string value, out bool readOnly)
    {
        value = "";
        readOnly = false;
        var el = _focused;
        if (el == null || !el.IsTextEditable) return false;
        var t = el.InputType?.ToLowerInvariant();
        bool isText = t == null || t == "text" || t == "password" || t == "email" ||
                      t == "search" || t == "tel" || t == "url" || t == "number";
        if (!isText) return false;
        readOnly = el.HasAttribute("readonly");
        value = el.Value ?? "";
        return true;
    }

    private void HandleChar(ushort charCode)
    {
        var c = (char)charCode;
        if (c < 32 || c == 127) return;
        if (!TryEditable(out var value, out var readOnly))
        {
            // Not editing: pages may still want keypress events on document.
            try { if (_document?.Body != null) DispatchSimple(_document.Body, "keypress"); } catch { }
            return;
        }
        if (readOnly || _focused == null) return;
        _caret = Math.Clamp(_caret, 0, value.Length);
        if (_selStart >= 0 && _selStart != _caret)
        {
            int a = Math.Min(_selStart, _caret), b = Math.Max(_selStart, _caret);
            value = value[..a] + c + value[b..];
            _caret = a + 1;
            _selStart = -1;
        }
        else
        {
            value = value[.._caret] + c + value[_caret..];
            _caret++;
        }
        _focused.Value = value;
        _showCursor = true;
        _blinkTick = Environment.TickCount64;
        try { DispatchSimple(_focused, "input"); } catch { }
        MarkPaint();
    }

    private void HandleKey(ushort charCode, ushort key, bool repeat)
    {
        var k = (UpBrowser.Platform.Key)key;

        if (_focused != null && TryEditable(out var value, out var readOnly))
        {
            bool edited = false;
            switch (k)
            {
                case UpBrowser.Platform.Key.Backspace:
                    if (!readOnly && _caret > 0)
                    {
                        if (_selStart >= 0 && _selStart != _caret)
                        {
                            int a = Math.Min(_selStart, _caret), b = Math.Max(_selStart, _caret);
                            value = value[..a] + value[b..];
                            _caret = a; _selStart = -1;
                        }
                        else
                        {
                            value = value[..(_caret - 1)] + value[_caret..];
                            _caret--;
                        }
                        edited = true;
                    }
                    break;
                case UpBrowser.Platform.Key.Delete:
                    if (!readOnly && _caret < value.Length)
                    {
                        value = value[.._caret] + value[(_caret + 1)..];
                        edited = true;
                    }
                    break;
                case UpBrowser.Platform.Key.Left:
                    if (_caret > 0) _caret--;
                    _selStart = -1; edited = false; break;
                case UpBrowser.Platform.Key.Right:
                    if (_caret < value.Length) _caret++;
                    _selStart = -1; edited = false; break;
                case UpBrowser.Platform.Key.Home:
                    _caret = 0; _selStart = -1; break;
                case UpBrowser.Platform.Key.End:
                    _caret = value.Length; _selStart = -1; break;
                case UpBrowser.Platform.Key.Escape:
                case UpBrowser.Platform.Key.Tab:
                    SetFocus(null);
                    return;
                case UpBrowser.Platform.Key.Enter:
                    if (_focused.TagName == "TEXTAREA" && !readOnly)
                    {
                        value = value[.._caret] + '\n' + value[_caret..];
                        _caret++;
                        _focused.Value = value;
                        MarkPaint();
                    }
                    else
                    {
                        var form = FindForm(_focused) as UpBrowser.Core.Dom.Html.HTMLFormElement;
                        try { DispatchSimple(_focused, "keydown"); } catch { }
                        form?.Submit();
                    }
                    return;
            }
            if (edited && _focused != null)
            {
                _focused.Value = value;
                try { DispatchSimple(_focused, "input"); } catch { }
            }
            if (edited || k is UpBrowser.Platform.Key.Left or UpBrowser.Platform.Key.Right
                or UpBrowser.Platform.Key.Home or UpBrowser.Platform.Key.End)
            {
                _showCursor = true;
                _blinkTick = Environment.TickCount64;
                MarkPaint();
                return;
            }
        }

        // Non-editable: surface keydown to the page (games/listeners).
        try
        {
            var target = _focused ?? _document?.Body;
            if (target != null)
                DispatchSimple(target, "keydown");
        }
        catch { }
    }

    private void HandleWheel(float dx, float dy, float x, float y)
    {
        bool consumed = false;
        if (_document != null)
        {
            var el = BrowserApp.HitTest(_document, x + _scrollX, y + _scrollY);
            bool vertical = Math.Abs(dy) >= Math.Abs(dx);
            float delta = vertical ? dy : dx;
            var root = _document.Body;
            for (var p = el; p != null && !consumed; p = p.ParentElement)
            {
                // body/html is the ROOT scroller: its overflow is the window
                // scroll, which the parent owns. Passing it as "consumed" would
                // move an invisible box and leave the page frozen.
                if (p == root || string.Equals(p.TagName, "HTML", StringComparison.OrdinalIgnoreCase))
                    continue;
                var box = p.LayoutBox;
                if (box == null || !box.IsScrollContainer) continue;
                float max = vertical
                    ? box.ScrollContentHeight - box.ContentBox.Height
                    : box.ScrollContentWidth - box.ContentBox.Width;
                if (max <= 0.5f) continue;
                float cur = vertical ? box.ScrollY : box.ScrollX;
                float next = Math.Clamp(cur + delta, 0, max);
                if (Math.Abs(next - cur) < 0.01f) continue; // at edge → page scrolls
                if (vertical) box.ScrollY = next; else box.ScrollX = next;
                consumed = true;
                MarkPaint();
            }
        }
        Send(TabMsg.WheelResult, w => w.Write((byte)(consumed ? 1 : 0)));
    }

    private float _mouseVX = -1, _mouseVY = -1; // last known pointer in viewport coords
    // Hover refresh deferred while the viewport scrolls; settled in the main loop.
    private bool _hoverScrollPending;
    private long _lastScrollMoveTick;

    private void NoteScrollMoved()
    {
        _lastScrollMoveTick = Environment.TickCount64;
        if (_mouseVX >= 0) _hoverScrollPending = true;
    }

    private void HandleMove(float x, float y)
    {
        if (_document == null) return;
        var el = BrowserApp.HitTest(_document, x + _scrollX, y + _scrollY);

        // mousemove fires on every move regardless of hover transitions.
        try
        {
            if (el != null)
                DispatchDomEvent(el, h => new ScriptEvent("mousemove", h) { clientX = x, clientY = y });
        }
        catch { }

        _mouseVX = x; _mouseVY = y;
        UpdateHoverAt(x, y);
    }

    /// <summary>
    /// Recompute :hover for the viewport point (x, y). Also called after scroll:
    /// content moves under a stationary pointer, so hover must follow the new
    /// document position (real browsers fire mouseout/mouseover there too).
    /// </summary>
    private void UpdateHoverAt(float x, float y)
    {
        if (_document == null) return;
        var el = BrowserApp.HitTest(_document, x + _scrollX, y + _scrollY);
        if (ReferenceEquals(el, _hoveredElement)) return;

        // mouseout / mouseover with relatedTarget, mirroring the UI-thread model.
        try
        {
            if (_hoveredElement != null)
                DispatchDomEvent(_hoveredElement, h => new ScriptEvent("mouseout", h)
                {
                    clientX = x, clientY = y,
                    relatedTarget = el != null ? WrapHost(el) : null,
                });
            if (el != null)
                DispatchDomEvent(el, h => new ScriptEvent("mouseover", h)
                {
                    clientX = x, clientY = y,
                    relatedTarget = _hoveredElement != null ? WrapHost(_hoveredElement) : null,
                });
        }
        catch { }

        // CSS :hover — toggle the ancestor chains, then only relayout when the
        // document actually carries :hover rules.
        for (var p = _hoveredElement; p != null; p = p.ParentElement) p.IsHovered = false;
        _hoveredElement = el;
        for (var p = el; p != null; p = p.ParentElement) p.IsHovered = true;

        if (!ReferenceEquals(_hoverRulesDoc, _document))
        {
            _docHasHoverRules = _styleComputer?.HasHoverRules() ?? false;
            _hoverRulesDoc = _document;
        }
        if (_docHasHoverRules)
            MarkLayout();
        else
            MarkPaint(); // form-control hover visuals live in the paint state
    }

    // ==================== render ====================

    private void MarkPaint() { _dlDirty = true; _renderDirty = true; }
    private void MarkLayout() { _layoutDirty = true; _dlDirty = true; _renderDirty = true; }

    private void RenderPass()
    {
        if (_document == null) return;
        try
        {
            if (_layoutDirty)
            {
                if (_stats) _stLayout++;
                if (_styleComputer == null)
                {
                    _styleComputer = new StyleComputer();
                    _styleComputer.AddStylesheet(_docManager.GetUaStylesheet(), UpBrowser.Core.Css.Resolver.CascadeOrigin.UserAgent);
                }
                _styleComputer.ComputeStyles(_document, _viewportW, _viewportH);
                _incremental ??= new IncrementalLayoutEngine(_layoutEngine, _layoutCache);
                _incremental.Layout(_document, _viewportW, _viewportH, _dpi, 16f);
                _layoutDirty = false;

                var bodyBox = _document.Body?.LayoutBox;
                _contentW = bodyBox?.BorderBox.Width ?? _viewportW;
                _contentH = bodyBox?.BorderBox.Height ?? _viewportH;
                // DOM metrics feed tooltips/task manager only — recompute at 1s,
                // not on every animation-frame layout.
                if (_cachedDomCount == 0 || Environment.TickCount64 - _lastCountTick >= 1000)
                {
                    _lastCountTick = Environment.TickCount64;
                    _cachedDomCount = CountDocNodes(_document);
                    _cachedBoxCount = CountLayoutBoxes(_document);
                }
                _dlDirty = true;
            }

            if (_dlDirty)
            {
                if (_stats) _stDl++;
                var visitor = new PaintVisitor(0, _typefaceCache, _imageCache,
                    GetFontFamilies(), _baseUrl, _viewportW, _viewportH);
                visitor.PhysicalScale = _dpi * _res;
                // No culling: the display list is scroll-invariant, so pure
                // scroll frames skip the paint walk entirely (raster only).
                visitor.SetFocusedElement(_focused);
                visitor.SetPressedButton(_pressedButton);
                if (_focused != null && _focused.IsTextEditable)
                    visitor.SetInputState(_caret, _selStart, _showCursor, false, "", 0);
                visitor.VisitDocumentStacking(_document);
                var dl = visitor.GetDisplayList();
                dl.SortByZIndex();
                _cachedDl = dl;
                _cachedBg = visitor.ViewBackgroundColor;
                _dlSerial++;
                _dlDirty = false;
                _renderDirty = true;
            }

            if (_renderDirty)
            {
                if (_stats) _stRaster++;
                RasterizeAndSend();
                _renderDirty = false;
            }
            _lastRenderTick = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TabHost {_tab}] render: {ex.Message}");
        }
    }

    private int _cachedDomCount, _cachedBoxCount;
    private long _lastCountTick;

    private unsafe void RasterizeAndSend()
    {
        var dl = _cachedDl;
        if (dl == null || _document == null) return;

        // Hidden tabs keep layout + display list warm but skip raster +
        // pixel transfer entirely; activation forces one fresh full frame.
        if (!_active) { _sentValid = false; return; }

        float scale = _dpi * _res;
        int wPx = Math.Max(1, (int)(_viewportW * scale));
        int hPx = Math.Max(1, (int)(_viewportH * scale));
        int rowBytes = wPx * 4;
        int total = rowBytes * hPx;

        if (_work == null || _work.Width != wPx || _work.Height != hPx)
        {
            _work?.Dispose();
            _work = new SKBitmap(wPx, hPx, SKColorType.Bgra8888, SKAlphaType.Premul);
            _shadow = null;
            _sentValid = false;
        }
        if (_shadow == null || _shadow.Length != total) { _shadow = new byte[total]; _sentValid = false; }

        // Wire policy is stateless (scroll ships FULL frames — no parent-side
        // pixel baseline can desync). Speed comes from a CHILD-INTERNAL band
        // raster: _work holds the previous raster at _workScrollY, so a scroll
        // shifts it by whole rows and repaints only the exposed strip. Any
        // hypothetical shift error is confined to this process and self-heals
        // at the next full repaint, which is forced every 16 band frames, on
        // content changes, and on large or horizontal jumps.
        int dyPx = (int)Math.Round(_scrollY - _sentScrollY);
        bool xStable = Math.Abs(_scrollX - _sentScrollX) < 0.5f;
        bool scrolled = dyPx != 0 || !xStable;
        // Device-space bookkeeping: _workDevY is the device row the _work content
        // actually stands for (whole-row shifts only), so per-frame rounding
        // residuals never accumulate — the error stays under one pixel until the
        // periodic full resync instead of wobbling text.
        float scrollDevY = _scrollY * scale;
        int dyW = (int)Math.Round(scrollDevY - _workDevY);
        bool bandScroll = _sentValid && _dlSerial == _sentDlSerial && _bandRun < 64 &&
                          xStable && dyW != 0 && dyW > -hPx && dyW < hPx;

        int preSlot = -1;
        if (bandScroll)
        {
            // The shift mutates _work: only take it once the send is certain.
            preSlot = AcquireFreeSlot();
            if (preSlot < 0) { _renderDirty = true; return; }
        }

        byte* cur = (byte*)_work.GetPixels(out _);
        long rsT = _stats ? Stopwatch.GetTimestamp() : 0;
        // Cull in document space: the canvas maps doc→device with scale and the
        // scroll translate, so only ops near the visible viewport can paint.
        float invS = 1f / scale;
        using (var canvas = new SKCanvas(_work))
        {
            if (bandScroll)
            {
                ShiftRasterRows(cur, hPx, rowBytes, dyW);
                // Repaint the exposed strip plus one guard row each side of the
                // sub-pixel residual left by the whole-row shift.
                int by = dyW > 0 ? Math.Max(0, hPx - dyW - 1) : 0;
                int bh = Math.Min(hPx, Math.Abs(dyW) + 2);
                canvas.Save();
                canvas.ClipRect(new SKRect(0, by, wPx, by + bh));
                canvas.Scale(scale);
                canvas.Translate(-_scrollX, -_scrollY);
                dl.ExecuteCulled(canvas, new SKRect(
                    _scrollX - 8, _scrollY + by * invS - 8,
                    _scrollX + wPx * invS + 8, _scrollY + (by + bh) * invS + 8));
                canvas.Restore();
                _workDevY += dyW;
                _bandRun++;
                if (_stats) { _stBlit++; _stRepBand += (Stopwatch.GetTimestamp() - rsT) * 1000 / Stopwatch.Frequency; _stNBand++; }
            }
            else
            {
                var docWindow = new SKRect(_scrollX - 8, _scrollY - 512, _scrollX + wPx * invS + 8, _scrollY + hPx * invS + 512);
                canvas.Clear(_cachedBg);
                canvas.Scale(scale);
                canvas.Translate(-_scrollX, -_scrollY);
                dl.ExecuteCulled(canvas, docWindow);
                canvas.Flush();
                _workDevY = scrollDevY;
                _bandRun = 0;
                if (_stats) { _stRepFull += (Stopwatch.GetTimestamp() - rsT) * 1000 / Stopwatch.Frequency; _stNFull++; }
            }
        }
        if (_stats) _stReplayMs += (Stopwatch.GetTimestamp() - rsT) * 1000 / Stopwatch.Frequency;

        byte mode;
        int bandY = 0, bandH = hPx;
        if (!_sentValid || scrolled)
        {
            // Scroll (or first frame): ship the whole viewport, statelessly.
            mode = (byte)FrameMode.Full;
        }
        else
        {
            // Stable scroll: ship only changed rows if the change is localized.
            long rsD = _stats ? Stopwatch.GetTimestamp() : 0;
            int first = -1, last = -1;
            var curSpan = new ReadOnlySpan<byte>(cur, total);
            for (int y = 0; y < hPx; y++)
            {
                int off = y * rowBytes;
                if (!curSpan.Slice(off, rowBytes).SequenceEqual(new ReadOnlySpan<byte>(_shadow, off, rowBytes)))
                {
                    if (first < 0) first = y;
                    last = y;
                }
            }
            if (_stats) _stDiffMs += (Stopwatch.GetTimestamp() - rsD) * 1000 / Stopwatch.Frequency;
            if (first < 0)
            {
                if (_stats) _stSilent++;
                _sentDlSerial = _dlSerial;
                return; // nothing actually changed — send nothing
            }
            if (last - first + 1 > hPx * 3 / 5)
                mode = (byte)FrameMode.Full;
            else
            {
                mode = (byte)FrameMode.DamageRows; bandY = first; bandH = last - first + 1;
            }
        }

        const int headerSize = 4 + 4 + 16 + 8 + 1 + 16 + 4; // meta+mode+rect+len = 53
        int payloadLen = headerSize + bandH * rowBytes;
        int slot = preSlot >= 0 ? preSlot : AcquireFreeSlot();
        if (slot < 0) { _renderDirty = true; return; } // both packets in flight — retry next pass
        var pkt = _pktScratch[slot];
        int need = 5 + payloadLen;
        if (pkt == null || pkt.Length < need)
        {
            pkt = new byte[need];
            _pktScratch[slot] = pkt;
        }

        var span = new Span<byte>(pkt, 0, need);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span, payloadLen);
        span[4] = (byte)TabMsg.Frame;
        var h = span.Slice(5, headerSize);
        int ho = 0;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.Slice(ho, 4), wPx); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.Slice(ho, 4), hPx); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(h.Slice(ho, 4), _scrollX); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(h.Slice(ho, 4), _scrollY); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(h.Slice(ho, 4), _contentW); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(h.Slice(ho, 4), _contentH); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.Slice(ho, 4), _cachedDomCount); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.Slice(ho, 4), _cachedBoxCount); ho += 4;
        h[ho++] = mode;
        // Spare field = page background (RGBA), so the parent can fill freshly
        // exposed scroll strips with the page's own color instead of smearing
        // pixels (edge stretch) or flashing white.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(ho, 4), (uint)_cachedBg); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.Slice(ho, 4), bandY); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.Slice(ho, 4), wPx); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.Slice(ho, 4), bandH); ho += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.Slice(ho, 4), bandH * rowBytes);

        // Pixels: copy the band from the raster into the packet, then fold the
        // same update into the shadow so the next diff starts from this frame.
        var dst = new Span<byte>(pkt, 5 + headerSize, bandH * rowBytes);
        var src = new ReadOnlySpan<byte>(cur, total);
        src.Slice(bandY * rowBytes, bandH * rowBytes).CopyTo(dst);
        dst.CopyTo(new Span<byte>(_shadow, bandY * rowBytes, bandH * rowBytes));

        QueueFramePacket(pkt, need);
        if (_stats)
        {
            _stBytes += need;
            if (mode == (byte)FrameMode.Full) _stFull++;
            else if (mode == (byte)FrameMode.ScrollBlit) _stBlit++;
            else _stRows++;
        }

        _sentValid = true;
        _sentScrollX = _scrollX;
        _sentScrollY = _scrollY;
        _sentDlSerial = _dlSerial;
    }

    /// <summary>
    /// In-place whole-row shift of the raster so row y becomes old row y+d
    /// (scrolling down by d rows moves content up in the buffer). Copy order
    /// guarantees no read-after-write corruption for overlapping ranges.
    /// </summary>
    private static unsafe void ShiftRasterRows(byte* buf, int hPx, int rowBytes, int d)
    {
        if (d > 0)
        {
            for (int y = 0; y + d < hPx; y++)
                Buffer.MemoryCopy(buf + (long)(y + d) * rowBytes, buf + (long)y * rowBytes, rowBytes, rowBytes);
        }
        else if (d < 0)
        {
            for (int y = hPx - 1; y + d >= 0; y--)
                Buffer.MemoryCopy(buf + (long)(y + d) * rowBytes, buf + (long)y * rowBytes, rowBytes, rowBytes);
        }
    }

    private void SetScroll(float x, float y)
    {
        float nx = Math.Max(0, x);
        float ny = Math.Max(0, y);
        if (Math.Abs(nx - _scrollX) < 0.5f && Math.Abs(ny - _scrollY) < 0.5f) return;
        _scrollX = nx; _scrollY = ny;
        _renderDirty = true;
        NoteScrollMoved();
        // Keep the parent's scrollbar in sync with child-initiated scrolls.
        Send(TabMsg.ScrollChanged, wr => { wr.Write(_scrollX); wr.Write(_scrollY); });
    }

    private static int CountDocNodes(Node node)
    {
        int count = node is Element ? 1 : 0;
        foreach (var child in node.Children)
            count += CountDocNodes(child);
        return count;
    }

    private static int CountLayoutBoxes(Node node)
    {
        int count = (node is Element el && el.LayoutBox != null) ? 1 : 0;
        foreach (var child in node.Children)
            count += CountLayoutBoxes(child);
        return count;
    }

    private static string[]? _fontFamiliesCache;
    private static string[] GetFontFamilies()
    {
        // SKFontManager enumeration is expensive; the system font list is
        // static for the process lifetime.
        return _fontFamiliesCache ??= SkiaSharp.SKFontManager.Default.FontFamilies.ToArray();
    }

    private void Send(TabMsg type, Action<BinaryWriter> body)
    {
        var payload = TabFraming.BuildPayload(body);
        try
        {
            lock (_pipeWrite)
                TabFraming.Write(_pipe, type, payload);
        }
        catch (IOException)
        {
            _cts.Cancel(); // parent is gone
        }
        catch (ObjectDisposedException)
        {
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _engine.Dispose();
        _work?.Dispose();
    }
}
