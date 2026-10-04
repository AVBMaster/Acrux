using SkiaSharp;
using Acrux.PageHost;

namespace Acrux.Process;

/// <summary>
/// A page engine running inside the shell's own process: the in-process twin of
/// <see cref="TabHost"/>, with the same loop, the same frame production and none of the
/// transport. It exists so that <c>single</c> and <c>threaded</c> tabs are served by the
/// very same pipeline a child process runs, instead of by a second copy of it that the
/// shell keeps for itself — one pipeline, three ways to host it.
/// </summary>
/// <remarks>
/// The frame it produces is a whole viewport, not a damage list: the dirty-rect and
/// scroll-blit machinery in <see cref="TabHost"/> exists to keep bytes off a pipe, and an
/// in-process host has no pipe to keep them off. The raster itself is identical, so a
/// frame captured here can be diffed pixel for pixel against one captured over there.
/// </remarks>
internal sealed class InProcessTabHost : IPageEngineSink, IDisposable
{
    private readonly PageEngine _engine;
    private readonly float _scale;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _loop;
    private readonly AutoResetEvent _signal = new(false);
    private readonly object _gate = new();
    private readonly Queue<Action> _queued = new();

    private SKBitmap? _frame;
    private long _frameVersion;
    private bool _renderDirty = true;
    private float _sentScrollX = float.NaN, _sentScrollY;
    private long _sentDlSerial = -1;
    private bool _sentValid;
    private string _title = "", _url = "";
    private bool _loading;
    private float _pendingWheelX, _pendingWheelY;

    public InProcessTabHost(int id, float dpi, float res)
    {
        _scale = dpi * res;
        _engine = new PageEngine(id, dpi, res, this);
        _loop = new Thread(Run) { IsBackground = true, Name = $"PageLoop-{id}" };
        _loop.Start();
    }

    // ---- state the shell reads (snapshots: the engine loop owns the document) ----

    public string Title => _title;
    public string Url => _url;
    public bool IsLoading => _loading;
    public long FrameVersion => Interlocked.Read(ref _frameVersion);
    public bool IsActive => _engine.IsActive;
    public float ScrollX => _engine.ScrollX;
    public float ScrollY => _engine.ScrollY;
    public float ContentWidth => _engine.ContentWidth;
    public float ContentHeight => _engine.ContentHeight;
    public int DomCount => _engine.DomCount;
    public bool HasTextSelection => _engine.HasTextSelection;

    /// <summary>The last published frame. Owned by this class; copy before handing it out
    /// to anything that keeps it across a turn of the loop.</summary>
    public SKBitmap? Bitmap => _frame;

    /// <summary>Page-state changes worth repainting chrome for. Raised on the engine loop.</summary>
    public event Action? OnStateChanged;

    /// <summary>Answers <c>alert/confirm/prompt</c>. Must not return until the user has
    /// answered, because script semantics are synchronous.</summary>
    public Func<string, string, string?>? DialogHandler { get; set; }

    // ---- commands: all of them run on the engine loop ----

    public void Navigate(string url) => Post(() => _engine.Navigate(url));
    public void LoadHtml(string html, string baseUrl) => Post(() => _engine.LoadHtml(html, baseUrl));
    public void Resize(float w, float h) => Post(() => _engine.SetViewport(w, h));
    public void SetActive(bool active) => Post(() => _engine.SetActive(active));
    public void SetScroll(float x, float y) => Post(() => _engine.SetScrollFromHost(x, y));
    public void PointerDown(float x, float y) => Post(() => _engine.HandlePointerDown(x, y));
    public void PointerUp(float x, float y) => Post(() => _engine.HandlePointerUp(x, y));
    public void PointerMove(float x, float y) => Post(() => _engine.HandlePointerMove(x, y));
    public void Wheel(float dx, float dy, float x, float y)
    {
        // The engine only decides whether an element scroller took the wheel; whoever
        // hosts it owns the root scroll, so the delta has to survive the answer.
        _pendingWheelX = dx;
        _pendingWheelY = dy;
        Post(() => _engine.HandleWheel(dx, dy, x, y));
    }
    public void Key(ushort ch, ushort key, bool repeat) => Post(() => _engine.HandleKey(ch, key, repeat));
    public void Char(ushort ch) => Post(() => _engine.HandleChar(ch));
    public void SelectAll() => Post(() => _engine.SelectAllText());

    /// <summary>Runs <paramref name="work"/> on the engine loop and waits for it: page
    /// queries need live document state, which that thread owns.</summary>
    public T Query<T>(Func<PageEngine, T> work)
    {
        if (Thread.CurrentThread == _loop) return work(_engine);
        T result = default!;
        using var done = new ManualResetEventSlim(false);
        Post(() => { result = work(_engine); done.Set(); });
        return done.Wait(TimeSpan.FromSeconds(5)) ? result : default!;
    }

    private void Post(Action work)
    {
        lock (_gate) _queued.Enqueue(work);
        _signal.Set();
    }

    // ==================== the loop ====================

    private void Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            int pending;
            lock (_gate) pending = _queued.Count;
            for (int i = 0; i < pending && !_cts.IsCancellationRequested; i++)
            {
                Action work;
                lock (_gate) work = _queued.Dequeue();
                try { work(); }
                catch (Exception ex) { Console.Error.WriteLine($"[LocalTab] cmd: {ex.Message}"); }
            }

            try
            {
                _engine.PumpJs();
                _engine.SettleDeferredHover(true);

                // Same cadence rule as the child: the engine folds JS timers and animation
                // frames into one deadline, and trusting it is what lets a parked animation
                // actually sleep.
                bool wanted = _engine.PipelineWanted || _renderDirty;
                if (wanted)
                {
                    _engine.UpdatePipeline();
                    if (_renderDirty) RenderFrame();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LocalTab] loop: {ex.Message}");
            }

            if (!_cts.IsCancellationRequested) _signal.WaitOne(Math.Max(1, _engine.NextWorkDelayMs));
        }
    }

    private void RenderFrame()
    {
        _renderDirty = false;
        var dl = _engine.DisplayList;
        if (dl == null) return;

        float scale = _scale;
        int wPx = Math.Max(1, (int)(_engine.ViewportWidth * scale));
        int hPx = Math.Max(1, (int)(_engine.ViewportHeight * scale));

        var info = new SKImageInfo(wPx, hPx, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = _frame is { } old && old.Width == wPx && old.Height == hPx ? old : new SKBitmap(info);
        if (!ReferenceEquals(bmp, _frame)) _frame = bmp;

        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(_engine.ViewBackgroundColor);
            canvas.Scale(scale);
            canvas.Translate(-_engine.ScrollX, -_engine.ScrollY);
            float invS = 1f / scale;
            dl.ExecuteCulled(canvas, new SKRect(_engine.ScrollX - 8, _engine.ScrollY - 512,
                _engine.ScrollX + wPx * invS + 8, _engine.ScrollY + hPx * invS + 512));
            canvas.Flush();
        }

        // Publish only on a real change, so an idle tab costs nothing and a frame counter
        // means what it says: same rule the child applies before it commits a slot.
        bool changed = !_sentValid || _sentDlSerial != _engine.DisplayListSerial ||
                       Math.Abs(_sentScrollX - _engine.ScrollX) > 0.01f ||
                       Math.Abs(_sentScrollY - _engine.ScrollY) > 0.01f;
        _sentValid = true;
        _sentDlSerial = _engine.DisplayListSerial;
        _sentScrollX = _engine.ScrollX;
        _sentScrollY = _engine.ScrollY;
        if (changed) Interlocked.Increment(ref _frameVersion);
    }

    // ==================== IPageEngineSink ====================

    public void RequestNavigate(string url) => Post(() => _engine.Navigate(url));
    public void RequestLoadHtml(string html, string baseUrl) => Post(() => _engine.LoadHtml(html, baseUrl));
    public void ReportTitle(string title) { _title = title; OnStateChanged?.Invoke(); }
    public void ReportUrl(string url) { _url = url; OnStateChanged?.Invoke(); }
    public void ReportLoading(bool loading) { _loading = loading; OnStateChanged?.Invoke(); }
    public void ReportScrollChanged(float x, float y) => OnStateChanged?.Invoke();
    public void ReportWheelConsumed(bool consumed)
    {
        if (consumed) return;
        float dx = _pendingWheelX, dy = _pendingWheelY;
        _pendingWheelX = _pendingWheelY = 0;
        Post(() => ScrollRootBy(dx, dy));
    }

    /// <summary>Root scroll: the same clamp the shell applies before it pushes a scroll
    /// into a child, kept here so a host with no chrome still scrolls.</summary>
    private void ScrollRootBy(float dx, float dy)
    {
        float maxX = Math.Max(0, _engine.ContentWidth - _engine.ViewportWidth);
        float maxY = Math.Max(0, _engine.ContentHeight - _engine.ViewportHeight);
        _engine.SetScroll(Math.Clamp(_engine.ScrollX + dx, 0, maxX),
            Math.Clamp(_engine.ScrollY + dy, 0, maxY));
    }
    public void ReportDirtyTrace(string trace) { }
    public string? RequestDialog(string message, string type) => DialogHandler?.Invoke(message, type);
    public void RequestFrame() => _renderDirty = true;

    public void InvalidateFrameBaseline()
    {
        _sentValid = false;
        _sentDlSerial = -1;
        _renderDirty = true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _signal.Set();
        try { _loop.Join(1500); } catch { }
        _engine.Dispose();
        _frame?.Dispose();
        _cts.Dispose();
        _signal.Dispose();
    }
}
