using System.Collections.Concurrent;
using SkiaSharp;
using Acrux.Core;
using Acrux.Core.Dom;
using Acrux.Core.EventLoop;
using Acrux.Core.JavaScript;
using Acrux.Core.Layout;
using Acrux.Core.Performance.Resources;
using Acrux.Core.Process;
using Acrux.Rendering;

namespace Acrux.Process;

/// <summary>
/// Everything the worker thread needs to run a tab while it is in the
/// background: the parsed document, its dedicated JS engine, the current
/// display list and scroll position. Ownership transfers atomically between
/// the UI thread (active tab) and the worker thread (background tab) —
/// exactly one side holds the object at any moment.
/// </summary>
public sealed class TabOwnership
{
    public required DocumentManager.DocumentLoadResult LoadResult { get; init; }
    public required JavaScriptEngine Engine { get; init; }
    public string Html { get; set; } = "";
    public string? BaseUrl { get; set; }
    public DisplayList DisplayList { get; set; } = new();
    public float ScrollX { get; set; }
    public float ScrollY { get; set; }
}

/// <summary>
/// TabProcess represents a tab running in its own background thread.
/// Each process has its own DocumentManager, LayoutEngine, JavaScriptEngine,
/// and caches — enabling true parallel page loading across tabs.
/// The main thread communicates via a command queue and receives DisplayList
/// results through lock-protected shared state.
/// In threaded-tab mode the UI thread hands the tab's ownership to the worker
/// when the tab goes to background (<see cref="HandOffToWorker"/>) and takes it
/// back on activation (<see cref="RequestReturnToUi"/>); while backgrounded the
/// worker keeps JS timers, layout and the display list warm.
/// </summary>
public class TabProcess : IDisposable
{
    private readonly int _tabIndex;
    private readonly string[] _fontFamilies;
    private readonly EventLoop _eventLoop;
    private readonly float _dpiScale;
    private readonly float _contentOffset;
    private readonly float _resolutionScale;

    private Thread? _workerThread;
    private CancellationTokenSource _cts = new();
    private BlockingCollection<Action> _commandQueue = new();

    private readonly object _sync = new();
    private DisplayList _displayList = new();
    private TabProcessMetrics _metrics;

    // Worker thread-local state (only accessed on the worker thread)
    private DocumentManager? _docManager;
    private LayoutEngine? _layoutEngine;
    private JavaScriptEngine? _jsEngine;
    private Dictionary<string, SKTypeface>? _typefaceCache;
    private ImageCache? _imageCache;

    // Background ownership (worker-thread only; handed across via commands)
    private TabOwnership? _owned;
    private Acrux.Core.Performance.Rendering.IncrementalLayoutEngine? _bgLayout;
    private long _lastBgRebuildTick;
    private string _lastPostedTitle = "";
    private volatile bool _forceBgRebuild;

    public volatile bool OwnershipOnWorker;

    private volatile float _viewportWidth = 1024;
    private volatile float _viewportHeight = 768;
    private volatile bool _isLoading;
    private volatile bool _hasNewContent;

    public int TabIndex => _tabIndex;
    public bool IsAlive => _workerThread?.IsAlive == true;
    public bool IsLoading => _isLoading;
    public bool HasNewContent
    {
        get => _hasNewContent;
        set => _hasNewContent = value;
    }

    public event Action<TabProcess>? OnUpdated;

    public TabProcess(int tabIndex, string initialUrl, string[] fontFamilies,
        EventLoop eventLoop, float dpiScale = 1f, float contentOffset = 0, float resolutionScale = 1f)
    {
        _tabIndex = tabIndex;
        _fontFamilies = fontFamilies;
        _eventLoop = eventLoop;
        _dpiScale = dpiScale;
        _contentOffset = contentOffset;
        _resolutionScale = resolutionScale;
        _metrics = new TabProcessMetrics { TabIndex = tabIndex, Url = initialUrl };
    }

    public void Start()
    {
        if (_workerThread?.IsAlive == true) return;
        _cts = new CancellationTokenSource();
        _commandQueue = new BlockingCollection<Action>();
        _workerThread = new Thread(WorkerMain)
        {
            Name = $"TabProc-{_tabIndex}",
            IsBackground = true
        };
        _workerThread.Start();
    }

    private void WorkerMain()
    {
        try
        {
            _docManager = new DocumentManager();
            _layoutEngine = new LayoutEngine();
            _typefaceCache = new Dictionary<string, SKTypeface>();
            _imageCache = new ImageCache();

            lock (_sync) { _metrics.Status = "Running"; }

            long lastTitlePost = 0;
            while (!_cts.Token.IsCancellationRequested)
            {
                Action? cmd = null;
                try { _commandQueue.TryTake(out cmd, 16, _cts.Token); }
                catch (OperationCanceledException) { break; }

                if (cmd != null)
                {
                    try { cmd(); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TabProc-{_tabIndex}] Error: {ex.Message}");
                    }
                }
                else
                {
                    PumpBackground();

                    // Surface background title changes to the tab strip.
                    string? curTitle;
                    lock (_sync) curTitle = _metrics.Title;
                    if (!string.IsNullOrEmpty(curTitle) && curTitle != _lastPostedTitle &&
                        Environment.TickCount64 - lastTitlePost > 500)
                    {
                        _lastPostedTitle = curTitle;
                        lastTitlePost = Environment.TickCount64;
                        _eventLoop.PostTask(() => OnUpdated?.Invoke(this));
                    }
                }
            }

            lock (_sync) { _metrics.Status = "Terminated"; }
            _jsEngine?.Dispose();
            _owned?.Engine.Dispose();
            _owned = null;
        }
        catch (OperationCanceledException) { }
        catch (ThreadInterruptedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[TabProc-{_tabIndex}] Fatal: {ex.Message}");
        }
    }

    public void Post(Action action)
    {
        if (!_cts.IsCancellationRequested)
            _commandQueue.Add(action);
    }

    // ==================== Threaded-tab ownership ====================

    /// <summary>
    /// Give the tab's document/JS engine/display list to the worker thread.
    /// Called on the UI thread when the tab moves to the background. The worker
    /// keeps timers, layout and painting warm until <see cref="RequestReturnToUi"/>.
    /// </summary>
    public void HandOffToWorker(TabOwnership ownership)
    {
        OwnershipOnWorker = true;
        Post(() =>
        {
            _owned = ownership;
            _forceBgRebuild = false;
            _lastBgRebuildTick = 0;
            lock (_sync)
            {
                _displayList = ownership.DisplayList;
            }
        });
    }

    /// <summary>
    /// Ask the worker to stop pumping and hand the tab's ownership back.
    /// <paramref name="onReturned"/> runs on the UI thread (via the event loop)
    /// once the worker reaches a safe point. If the worker never took ownership
    /// the callback fires immediately with null.
    /// </summary>
    public void RequestReturnToUi(Action<TabOwnership?, DisplayList?> onReturned)
    {
        if (!OwnershipOnWorker)
        {
            onReturned(null, null);
            return;
        }
        Post(() =>
        {
            var owned = _owned;
            _owned = null;
            DisplayList? dl;
            lock (_sync) dl = _displayList;
            OwnershipOnWorker = false;
            _eventLoop.PostTask(() => onReturned(owned, dl));
        });
    }

    /// <summary>Mark the background tab dirty (e.g. after a viewport resize).</summary>
    public void InvalidateLayout()
    {
        _forceBgRebuild = true;
    }

    /// <summary>
    /// Pump JS timers/microtasks and rebuild the display list when the page is
    /// dirty. Runs on the worker thread between commands while the tab is in the
    /// background. The rebuilt list replaces the ownership one; the previous list
    /// is abandoned (never returned to the op pool) so the UI thread can still
    /// paint a snapshot it grabbed during a tab switch.
    /// </summary>
    private void PumpBackground()
    {
        var owned = _owned;
        if (owned == null) return;

        try
        {
            var eng = owned.Engine;
            eng.SetWindowSize((int)_viewportWidth, (int)_viewportHeight);

            var jsInt = eng.IntegrationService;
            jsInt?.ProcessTimers();
            jsInt?.MicrotaskQueue.DrainMicrotasks();
            jsInt?.IdentityMap.CleanupStaleEntries();
            if (eng.HasTimers)
                eng.TickTimers();

            bool dirty = eng.NeedsReLayout || _forceBgRebuild;
            if (!dirty) return;

            long now = Environment.TickCount64;
            if (now - _lastBgRebuildTick < 60) return; // coalesce bursts; dirty flags persist
            _lastBgRebuildTick = now;
            _forceBgRebuild = false;
            eng.ClearDirty();

            RebuildBackgroundDisplayList(owned);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TabProc-{_tabIndex}] bg pump: {ex.Message}");
        }
    }

    private void RebuildBackgroundDisplayList(TabOwnership owned)
    {
        if (_docManager == null || _typefaceCache == null || _imageCache == null) return;

        var doc = owned.LoadResult.Document;
        var styleComputer = owned.LoadResult.StyleComputer;
        if (styleComputer == null)
        {
            styleComputer = new Acrux.Core.Css.StyleComputer();
            styleComputer.AddStylesheet(_docManager.GetUaStylesheet(), Acrux.Core.Css.Resolver.CascadeOrigin.UserAgent);
        }
        styleComputer.ComputeStyles(doc, _viewportWidth, _viewportHeight);

        _bgLayout ??= new Acrux.Core.Performance.Rendering.IncrementalLayoutEngine(
            _layoutEngine ??= new LayoutEngine(), new Acrux.Core.Performance.Rendering.LayoutCache());
        _bgLayout.Layout(doc, _viewportWidth, _viewportHeight, _dpiScale, 16f);

        var visitor = new PaintVisitor(_contentOffset, _typefaceCache, _imageCache,
            _fontFamilies, owned.BaseUrl, _viewportWidth, _viewportHeight);
        visitor.PhysicalScale = _dpiScale * _resolutionScale;
        visitor.SetSkipInputTextOverlay(true);
        // The shared ScrollLayerCache belongs to the UI thread (it clears and
        // disposes those images); background lists use inline scroller painting.
        visitor.DisableScrollLayers = true;
        visitor.VisitDocumentStacking(doc);
        var newDl = visitor.GetDisplayList();
        newDl.SortByZIndex();
        newDl.BuildSpatialGrid();

        // Swap atomically; the old list may still be painted by the UI thread,
        // so abandon it instead of clearing (its ops never re-enter the pool).
        lock (_sync)
        {
            _displayList = newDl;
            owned.DisplayList = newDl;
            _metrics.Title = doc.Title ?? _metrics.Title;
            _metrics.DomNodeCount = CountDocNodes(doc);
            _metrics.LayoutBoxCount = CountLayoutBoxes(doc);
            _metrics.JsHeapSizeKB = owned.Engine.GetHeapSizeKB();
            _metrics.JsTimerCount = owned.Engine.TimerCount;
        }

        _hasNewContent = true;
        _eventLoop.PostTask(() => OnUpdated?.Invoke(this));
    }

    /// <summary>Navigate to HTML content on the worker thread.</summary>
    public void NavigateToHtml(string html, string? baseUrl = null)
    {
        Post(() => WorkerNavigate(html, baseUrl));
    }

    private void WorkerNavigate(string html, string? baseUrl)
    {
        if (_docManager == null || _layoutEngine == null) return;
        _jsEngine ??= new JavaScriptEngine(_tabIndex);

        _isLoading = true;
        _imageCache?.Clear();
        _typefaceCache?.Clear();
        // Layout needs intrinsic image sizes before the first paint exists.
        if (_imageCache != null)
            PaintVisitor.InstallReplacedIntrinsicSizes(_imageCache, baseUrl);

        try
        {
            var loadResult = _docManager.LoadHtmlAsync(html, baseUrl,
                _viewportWidth, _viewportHeight, _dpiScale).GetAwaiter().GetResult();

            _jsEngine.LoadDocument(loadResult.Document);

            RunPageScripts(loadResult, baseUrl);

            var visitor = new PaintVisitor(_contentOffset, _typefaceCache,
                _imageCache, _fontFamilies, baseUrl, _viewportWidth, _viewportHeight);
            visitor.PhysicalScale = _dpiScale * _resolutionScale;
            visitor.DisableScrollLayers = true;
            visitor.VisitDocumentStacking(loadResult.Document);
            var newDl = visitor.GetDisplayList();
            newDl.SortByZIndex();
            newDl.BuildSpatialGrid();

            var title = loadResult.Document.Title ?? "New Tab";
            int domCount = CountDocNodes(loadResult.Document);
            int boxCount = CountLayoutBoxes(loadResult.Document);

            long memBytes = GC.GetTotalMemory(false);

            lock (_sync)
            {
                _displayList = newDl;
                _metrics.Title = title;
                _metrics.Url = baseUrl ?? "";
                _metrics.DomNodeCount = domCount;
                _metrics.LayoutBoxCount = boxCount;
                _metrics.JsHeapSizeKB = _jsEngine.GetHeapSizeKB();
                _metrics.JsTimerCount = _jsEngine.TimerCount;
                _metrics.MemoryBytes = memBytes;
                _metrics.Status = "Running";
            }

            _hasNewContent = true;
            _eventLoop.PostTask(() => OnUpdated?.Invoke(this));
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>Resize the viewport (affects layout on next navigation).</summary>
    public void SetViewport(float width, float height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }

    /// <summary>Get the latest DisplayList (thread-safe, main thread consumer).</summary>
    public DisplayList GetDisplayList()
    {
        lock (_sync) return _displayList;
    }

    /// <summary>Get current metrics snapshot (thread-safe).</summary>
    public TabProcessMetrics GetMetrics()
    {
        lock (_sync) return _metrics;
    }

    /// <summary>Update URL metadata (thread-safe).</summary>
    public void UpdateUrl(string url)
    {
        lock (_sync) _metrics.Url = url;
    }

    /// <summary>Update title metadata (thread-safe).</summary>
    public void UpdateTitle(string title)
    {
        lock (_sync) _metrics.Title = title;
    }

    /// <summary>Update content metrics (thread-safe). Called from main thread after page load.</summary>
    public void UpdateContentMetrics(int domNodeCount, int layoutBoxCount, long memoryBytes, int jsHeapSizeKB, int jsTimerCount)
    {
        lock (_sync)
        {
            _metrics.DomNodeCount = domNodeCount;
            _metrics.LayoutBoxCount = layoutBoxCount;
            _metrics.MemoryBytes = memoryBytes;
            _metrics.JsHeapSizeKB = jsHeapSizeKB;
            _metrics.JsTimerCount = jsTimerCount;
            _metrics.Status = "Running";
        }
    }

    public void Dispose()
    {
        if (_cts?.IsCancellationRequested == true)
            return;

        // 1. 标记取消
        _cts?.Cancel();

        // 2. 停止添加新命令
        _commandQueue?.CompleteAdding();

        // 3. 发送哨兵命令唤醒阻塞在 GetConsumingEnumerable 的线程
        //    （NativeAOT 不支持 Thread.Interrupt，需用此方式唤醒）
        try { _commandQueue?.Add(() => { }); } catch { }

        if (_workerThread?.IsAlive == true)
        {
            if (!_workerThread.Join(3000))
            {
                Console.WriteLine($"[TabProc-{_tabIndex}] Worker thread did not exit in time");
            }
        }

        _cts?.Dispose();
        _commandQueue?.Dispose();
    }

    private void RunPageScripts(DocumentManager.DocumentLoadResult loadResult, string? baseUrl)
    {
        var doc = loadResult.Document;
        if (doc == null || _jsEngine == null) return;

        var allElements = new List<Element>();
        if (doc.DocumentElement != null)
        {
            var queue = new Queue<Element>();
            queue.Enqueue(doc.DocumentElement);
            while (queue.Count > 0)
            {
                var el = queue.Dequeue();
                allElements.Add(el);
                foreach (var child in el.Children)
                    if (child is Element childEl)
                        queue.Enqueue(childEl);
            }
        }

        var scripts = allElements.Where(e =>
            e.TagName.ToLowerInvariant() == "script").ToList();

        foreach (var el in scripts)
        {
            var src = el.GetAttribute("src");
            if (!string.IsNullOrEmpty(src))
            {
                try
                {
                    var url = ResolveUrl(src, baseUrl) ?? src;
                    var http = new StreamingHttpFetcher();
                    var resp = http.FetchAsync(new ResourceRequest
                    {
                        Url = url,
                        Kind = ResourceKind.Script,
                        Priority = ResourcePriority.High,
                        Timeout = TimeSpan.FromSeconds(10),
                    }).GetAwaiter().GetResult();
                    var code = System.Text.Encoding.UTF8.GetString(resp.Body);
                    _jsEngine.Execute(code);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TabProc] Script load failed: {ex.Message}");
                }
            }
            else
            {
                var code = el.TextContent;
                if (!string.IsNullOrWhiteSpace(code))
                    _jsEngine.Execute(code);
            }
        }
    }

    private static string? ResolveUrl(string url, string? baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("data:"))
            return url;
        if (url.StartsWith("//"))
        {
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://"))
                return "https:" + url;
            return "http:" + url;
        }
        if (string.IsNullOrEmpty(baseUrl)) return null;
        try
        {
            var baseUri = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/');
            return new Uri(baseUri, url).ToString();
        }
        catch { return null; }
    }

    private static int CountDocNodes(Core.Dom.Node node)
    {
        int count = node is Element ? 1 : 0;
        foreach (var child in node.Children)
            count += CountDocNodes(child);
        return count;
    }

    private static int CountLayoutBoxes(Core.Dom.Node node)
    {
        int count = (node is Element el && el.LayoutBox != null) ? 1 : 0;
        foreach (var child in node.Children)
            count += CountLayoutBoxes(child);
        return count;
    }
}


