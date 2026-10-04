using SkiaSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using Acrux.Core;
using Acrux.Core.Css;
using Acrux.Core.Dom;
using Acrux.Core.Input;
using Acrux.Core.EventLoop;
using FormElement = Acrux.Core.Dom.Html.HTMLFormElement;
using Acrux.Core.JavaScript;
using Acrux.Core.Layout;
using Acrux.Process;
using Acrux.Core.Performance;
using Acrux.Core.Performance.Diagnostics;
using Acrux.Core.Performance.Memory;
using Acrux.Core.Performance.Rendering;
using Acrux.Core.Performance.Resources;
using Acrux.Core.Performance.Scheduling;
using Acrux.PageHost;
using Acrux.Platform;
using Acrux.Rendering;
using Acrux.Rendering.DevTools;

namespace Acrux;

    public class BrowserApp : IDisposable
{
    private readonly IWindow _window;
    private readonly SkiaRenderer _skiaRenderer;
    private readonly ChromeRenderer _chrome;
    private readonly ScrollManager _scroll;
    private readonly ScrollInteraction _scrollInteraction = new();
    private bool _scrollDirty;
    private bool _compositorBacklogLast;
    /// <summary>
    /// True when the previous frame finished with deferred tile raster work still
    /// pending or just completed (PendingTileRepaint). Drives the retained-surface
    /// decision before the frame renders.
    /// </summary>
    private bool _pendingTileWorkLast;
    /// <summary>
    /// True when the pending relayout was triggered by a HOVER change (mouse
    /// over a new element / select-option highlight) — a small, localized change.
    /// When the frame rebuilds for this alone, the tile cache is preserved and
    /// only the hovered region is re-rasterized, instead of dropping and re-tiling
    /// the whole page over several frames (the "pixelated flicker" seen while the
    /// mouse crosses scroll containers).
    /// </summary>
    private bool _hoverRelayoutPending;
    /// <summary>Page-space rect (chrome offset applied) that changed on hover; the
    /// union of the old and new hovered element boxes.</summary>
    private SKRect _hoverDirtyRect;
    /// <summary>Document for which <see cref="_documentHasHoverRules"/> was computed
    /// (invalidated when a new document loads).</summary>
    private Core.Dom.Document? _hoverRulesDoc;
    /// <summary>Cached: does the current document contain any :hover CSS rule?
    /// When false, hover changes can't affect computed styles, so they need no
    /// relayout (only checkbox/radio paint-time hover feedback does).</summary>
    private bool _documentHasHoverRules;
    private readonly DocumentManager _docManager;
    private readonly InputHandler _input;
    private readonly LayoutEngine _layout = new();
    private JavaScriptEngine _jsEngine;
    private readonly EventLoop _eventLoop;
    private readonly Acrux.Core.Performance.Resources.StreamingHttpFetcher _httpFetcher = new();
    private readonly float _dpiScale;
    private readonly float _contentOffset;

    private DocumentManager.DocumentLoadResult? _currentLoad;
    private PaintVisitor? _cachedPaintVisitor;
    private DisplayList _displayList = new();
    private float _lastLayoutWidth;
    private string _currentHtml = "";
    /// <summary>True once the page area has been composited at least once; retained
    /// frames before that must paint the page background instead of holding onto
    /// an uninitialized surface.</summary>
    private bool _everRenderedPage;

    // Performance-integrated layout engine (wraps the regular LayoutEngine with
    // LayoutCache + DirtyFlags so clean subtrees can skip work).
    private IncrementalLayoutEngine? _incrementalLayout;

    /// <summary>
    /// CSS animations and transitions. Advanced once per frame between style
    /// resolution and layout, exactly as the headless capture path does, so the
    /// on-screen result and a snapshot of the same markup agree.
    /// </summary>
    private readonly Acrux.Core.Dom.Animations.CssAnimationEngine _animations = new();
    private bool _animationsRunning;

    /// <summary>
    /// Wall-clock instant the document timeline was last re-based. The timeline's
    /// own time is the distance from the engine's construction, so this is what
    /// keeps a page's animations measured from its navigation instead.
    /// </summary>
    private double _animationTimelineBaseMs = Acrux.Core.Dom.Animations.AnimationClock.WallClockMs;

    /// <summary>Per-frame animation diagnostics (ACRUX_ANIM_LIVE=1).</summary>
    private readonly bool _animTrace = Environment.GetEnvironmentVariable("ACRUX_ANIM_LIVE") == "1";

    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private float _lastScrollX;
    private float _lastScrollY;
    private float _lastDevToolsHeight;
    private bool _lastDevToolsVisible;
    private long _lastTaskManagerRefresh;
    private readonly DevToolsPanel _devTools;

    private readonly ImageCache _sharedImageCache = new();
    private readonly Dictionary<string, SKTypeface> _sharedTypefaceCache = new();
    private static string[]? _fontFamilies;
    private bool _pendingRelayout;

    private readonly RenderingSettings _renderingSettings = new();
    private readonly RenderingSettingsPage _renderingSettingsPage;
    private readonly TaskManagerPage _taskManagerPage;
    private readonly ProcessManager _processManager;

    // ── Threaded-tab mode (settings: TabMode) ─────────────────────
    // When true each tab owns a JavaScriptEngine; background tabs are handed
    // to their TabProcess worker thread (JS keeps running, display list stays
    // warm) and adopted back by the UI thread on activation.
    private readonly bool _threadedTabs;
    private JavaScriptEngine? _parkEngine;
    private volatile bool _adoptionPending;
    private int _pendingAdoptTab = -1;
    private int _adoptionGen;

    // ── Multi-process tab mode (settings: TabMode = "process") ─────
    // Each tab lives in its own `--tab-host` child process; the browser only
    // composites the child's rendered frames and forwards input + scroll.
    private readonly bool _processTabs;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, RemoteTabView> _remoteTabs = new();
    private long _lastRemoteMoveTick;
    // Wheel arbitration: the child gets first refusal for element scrollers;
    // accumulated deltas fall back to page scroll on WheelResult/timeout.
    private double _pendingWheelX, _pendingWheelY;
    private bool _wheelAwaiting;
    private long _wheelSentTick;
    private float _lastRemoteExtentW = -1, _lastRemoteExtentH = -1;

    // Performance optimization layer. Lazily initialized in RunAsync so the
    // existing Chrome/loader flows are not perturbed on cold-start.
    private PerformanceHub? _perfHub;
    private SharedStyleCache? _sharedStyleCache;
    private LayoutCache? _layoutCache;

    private long _lastInputTimeTick = Environment.TickCount64;
    private const long InputCooldownMs = 80;
    private long _lastGcTick = Environment.TickCount64;

    private string? _dialogResult;
    private string? _dialogInput;
    private bool _dialogActive;
    private string _dialogMessage = "";
    private string _dialogType = "";
    private SKRect _dialogOkRect;
    private SKRect _dialogCancelRect;
    private SKRect _dialogInputRect;
    // A page-host dialog is the same overlay, but answered over the pipe instead of a
    // blocking loop: the shell's own thread must never stall waiting for a click.
    // One modal shows at a time; the rest wait per tab, still blocking their page.
    private int _remoteDialogId = -1;
    private int _remoteDialogTab = -1;
    private readonly Dictionary<int, (int RequestId, string Message, string Type)> _pendingDialogs = new();

    // ---- page-unresponsive watchdog ----
    // The child proves liveness with heartbeats; this side only decides what to do about
    // their absence. The timer runs off the frame loop on purpose: a wedged page stops
    // requesting repaints, so a frame-loop check alone could never notice the change.
    private System.Threading.Timer? _watchdog;
    private volatile int _watchdogTarget = -1;
    private PageResponsiveness _reportedResp = PageResponsiveness.Healthy;
    private bool _bubbleMuted;
    private SKRect _bubbleReloadRect, _bubbleWaitRect, _bubbleKillRect;
    private SKRect _crashReloadRect;

    // IME support: track focused input element
    private Acrux.Core.Dom.Element? _focusedElement;
    private bool _devToolsFocused;
    private readonly PageInputImeHost _pageInputImeHost;
    private const float ScrollbarDragThreshold = 5;

    // Form input editing state
    private int _inputCursorPos;
    private int _inputSelStart = -1;
    private bool _inputShowCursor = true;
    private long _inputLastCursorBlinkTick = Environment.TickCount64;
    private bool _inputDragging;
    private float _inputScrollOffset;
    // Form input IME state
    private bool _inputImeComposing;
    private string _inputImeCompositionStr = "";
    private int _inputImeCursorPos;
    // Process-mode IME: last target state read from the newest remote frame (change
    // detector so the IME target is only re-set when the child's focus actually moved).
    private bool _remoteImeEditable;

    // Element scrollbar drag state
    private LayoutBox? _elemScrollDragBox;
    private bool _elemScrollDragVertical;
    private float _elemScrollDragStart;
    private float _elemScrollDragStartScroll;
    /// <summary>
    /// Set when an element scroll offset changed without any layout/style change.
    /// The render loop rebuilds the display list via paint only (skipping the
    /// style+layout pass) and region-invalidates the scrolled containers so their
    /// tiles re-raster while every other tile survives — avoiding the all-tile
    /// drop that made element scrollbars lag and flicker.
    /// </summary>
    private bool _elementScrollDirty;
    private readonly List<SKRect> _pendingElementScrollRects = new();
    private readonly List<Acrux.Core.Dom.LayoutBox> _pendingElementScrollBoxes = new();

    // Textarea scroll state (user wheel/thumb scroll vs caret-following viewport)
    private bool _textareaUserScroll;
    private bool _textareaScrollDragging;
    private float _textareaScrollDragStartY;
    private float _textareaScrollDragStartScroll;

    // Text selection support (node+offset based for character-level precision)
    private struct SelPoint
    {
        public Core.Dom.TextNode? Node;
        public int Offset;
    }
    private bool _isSelecting;
    private bool _hasSelection;
    private SelPoint _selAnchor;
    private SelPoint _selFocus;
    private readonly string? _startupUrl;
    // Idle diagnostics (see TabHost stats): ACRUX_TAB_STATS=1
    private readonly bool _renderStats = Environment.GetEnvironmentVariable("ACRUX_TAB_STATS") == "1";
    private long _rsNextLog, _rsTotal, _rsRedraw, _rsLoading, _rsScroll, _rsRelayout, _rsInput, _rsDevTools, _rsCursor;
    private long _rsPreMs, _rsPumpMs, _rsBodyMs, _rsPresentMs, _rsRendered;

    public BrowserApp(int logicalWidth, int logicalHeight, string? startupUrl = null)
    {
        _startupUrl = startupUrl;
        if (_renderStats)
            InputHandler.RedrawTraceGate = stack =>
            {
                try { File.AppendAllText("acrux_tabstats_redraw.log", stack + "\n----\n"); } catch { }
            };
        LogCtor("SkiaTextMeasurer");
        PageEnvironment.Initialize();
        LogCtorDone("SkiaTextMeasurer");

        LogCtor("SKFontManager");
        _fontFamilies = PageEnvironment.FontFamilies;
        LogCtorDone("SKFontManager");

        LogCtor("GetDpiScale");
        _dpiScale = PlatformFactory.GetDpiScale();
        Console.WriteLine($"DPI Scale: {_dpiScale:F2} ({_dpiScale * 100}%)");
        LogCtorDone("GetDpiScale");

        int physicalWidth = (int)(logicalWidth * _dpiScale);
        int physicalHeight = (int)(logicalHeight * _dpiScale);

        LogCtor("CreateWindow");
        _window = PlatformFactory.CreateWindow(physicalWidth, physicalHeight, "Acrux");
        LogCtorDone("CreateWindow");

        LogCtor("DocumentManager");
        _docManager = new DocumentManager();
        LogCtorDone("DocumentManager");

        LogCtor("ChromeRenderer");
        _chrome = new ChromeRenderer();
        LogCtorDone("ChromeRenderer");

        LogCtor("ScrollManager");
        _scroll = new ScrollManager();
        LogCtorDone("ScrollManager");

        LogCtor("SkiaRenderer");
        _skiaRenderer = new SkiaRenderer();
        LogCtorDone("SkiaRenderer");

        LogCtor("LoadSettingsConfig");
        RenderingSettingsConfig.Load(_renderingSettings);
        DocumentManager.UseCustomParser = _renderingSettings.UseCustomHtmlParser;
        // Apply frame pacing at startup too — previously only OnChanged set it,
        // so the configured FPS silently differed from the actual loop cadence.
        _window.TargetFrameTimeMs = _renderingSettings.TargetFps > 0
            ? 1000f / _renderingSettings.TargetFps
            : 16.67f; // "unlimited" → vsync-class 60Hz; a 1ms loop burns CPU for nothing
        LogCtorDone("LoadSettingsConfig");

        var engineType = JsEngineConfig.GetEngineTypeByName(_renderingSettings.JsEngine) ?? JsEngineType.Jint;
        if (engineType != JsEngineType.Jint && !JsEngineDownloader.IsEngineDownloaded(engineType))
        {
            Console.WriteLine($"[Startup] Engine '{engineType}' is configured but not downloaded. Using Jint (built-in).");
            Console.WriteLine($"[Startup] To use {engineType}, go to Settings → JavaScript Engine and download it first.");
            engineType = JsEngineType.Jint;
            _renderingSettings.JsEngine = "Jint";
        }
        JsEngineConfig.DefaultEngineType = engineType;

        _processTabs = _renderingSettings.TabMode == "process";
        _threadedTabs = _renderingSettings.TabMode == "threaded";
        if (_processTabs)
            Console.WriteLine("[Startup] Multi-process tab mode enabled");
        else if (_threadedTabs)
            Console.WriteLine("[Startup] Threaded-tab mode enabled");

        LogCtor("JavaScriptEngine");
        _jsEngine = new JavaScriptEngine(-1);
        LogCtorDone("JavaScriptEngine");

        LogCtor("EventLoop");
        _eventLoop = new EventLoop();
        LogCtorDone("EventLoop");

        LogCtor("DevToolsPanel");
        _devTools = new DevToolsPanel();
        LogCtorDone("DevToolsPanel");

        LogCtor("PageInputImeHost");
        _pageInputImeHost = new PageInputImeHost(this);
        LogCtorDone("PageInputImeHost");

        LogCtor("TrySetGpu");
        if (!_skiaRenderer.TrySetGpu(_renderingSettings.GpuAcceleration))
            Console.WriteLine("[Startup] GPU init failed, using CPU");
        LogCtorDone("TrySetGpu");

        LogCtor("RenderingSettingsPage");
        _renderingSettingsPage = new RenderingSettingsPage(_renderingSettings, _dpiScale);
        LogCtorDone("RenderingSettingsPage");

        LogCtor("TaskManagerPage");
        _taskManagerPage = new TaskManagerPage();
        LogCtorDone("TaskManagerPage");

        LogCtor("ProcessManager");
        _processManager = new ProcessManager(_fontFamilies!, _eventLoop, _dpiScale, _chrome.GetContentOffset(), _renderingSettings.ResolutionScale);
        LogCtorDone("ProcessManager");

        _contentOffset = _chrome.GetContentOffset();

        LogCtor("InputHandler");
        _input = new InputHandler(_chrome, _scroll, _window, _dpiScale);
        LogCtorDone("InputHandler");
        _input.OnDomClick = HandleDomClick;
        _input.OnDevToolsKey = () =>
        {
            _devTools.Toggle();
            _devToolsFocused = _devTools.Visible;
            UpdateImeTarget();
            _input.NeedsRedraw = true;
        };

        _processManager.OnProcessUpdated += (proc) =>
        {
            if (proc.TabIndex == _chrome.ActiveTabIndex && proc.HasNewContent)
            {
                proc.HasNewContent = false;
                _input.NeedsRedraw = true;
            }
            else if (_threadedTabs)
            {
                // Background tab: surface its JS-driven title change in the strip.
                var m = proc.GetMetrics();
                if (!string.IsNullOrEmpty(m.Title))
                    _chrome.SetTabTitle(proc.TabIndex, m.Title);
                proc.HasNewContent = false;
            }
        };

        _chrome.OnSettingsClick = () =>
        {
            _renderingSettingsPage.Toggle();
            _input.NeedsRedraw = true;
        };

        _chrome.OnTaskManagerClick = () =>
        {
            _taskManagerPage.Toggle();
            _input.NeedsRedraw = true;
        };

        _taskManagerPage.OnChanged += () =>
        {
            _input.NeedsRedraw = true;
        };

        _renderingSettingsPage.OnChanged += () =>
        {
            _input.NeedsRedraw = true;
            // 不在这里使页面缓存失效，避免每次鼠标悬停都重绘网页
        };

        _renderingSettingsPage.OnEngineApplied += type =>
        {
            JsEngineConfig.DefaultEngineType = type;
            Console.WriteLine($"[JS] Engine applied for new tabs: {type}");
        };

        _renderingSettingsPage.OnBrowseEngine += engineName =>
        {
            var type = JsEngineConfig.GetEngineTypeByName(engineName);
            if (type == null || type == JsEngineType.Jint) return;

            // 弹出模态文件夹选择对话框，附属到主窗口
            var hwnd = _window.GetNativeHandle();
            string? folder = PickFolder("选择引擎目录", hwnd);
            if (folder == null) return;

            var dest = JsEngineDownloader.EngineDir(type.Value);
            try
            {
                // 清空旧文件，确保完全更新
                if (Directory.Exists(dest))
                    Directory.Delete(dest, true);
                Directory.CreateDirectory(dest);
                var result = ScanAndCopyEngineDlls(folder, dest, type.Value);
                if (!result.Success)
                {
                    ShowDialog(result.ErrorMessage ?? "引擎文件验证失败", "引擎验证失败");
                    _renderingSettingsPage.Invalidate();
                    return;
                }

                Console.WriteLine($"[Engine] {engineName} installed from {folder}");
                JsEngineInfoRegistry.Register(type.Value, result.AssemblyPath!, folder);
                _renderingSettingsPage.Invalidate();
            }
            catch (Exception ex)
            {
                ShowDialog($"从本地安装 {engineName} 时出错:\n{ex.Message}",
                    "引擎安装失败");
            }
        };

        // ── JS engine action handler for acrux://js page ──
        string JsEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        string JsResult(bool ok, string msg) => $"{{\"success\":{(ok ? "true" : "false")},\"error\":\"{JsEscape(msg)}\"}}";

        string HandleEngineAction(string action, string engineName)
        {
            try
            {
                return action switch
                {
                    "download" => HandleEngineDownload(engineName),
                    "browse" => HandleEngineBrowse(engineName),
                    "apply" => HandleEngineApply(engineName),
                    _ => "{\"success\":false,\"error\":\"unknown action\"}"
                };
            }
            catch (Exception ex)
            {
                return $"{{\"success\":false,\"error\":\"{ex.Message}\"}}";
            }
        }

        string HandleEngineDownload(string engineName)
        {
            var type = JsEngineConfig.GetEngineTypeByName(engineName);
            if (type == null || type == JsEngineType.Jint)
                return "{\"success\":false,\"error\":\"不支持的引擎\"}";

            if (JsEngineDownloader.IsEngineDownloaded(type.Value))
                return "{\"success\":false,\"error\":\"引擎已下载\"}";

            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await JsEngineDownloader.DownloadEngineAsync(type.Value);
                        Console.WriteLine($"[JS] {engineName} downloaded via HTML page");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[JS] Download failed: {ex.Message}");
                    }
                });
                return "{\"success\":true,\"message\":\"下载已开始，请稍候刷新页面\"}";
            }
            catch (Exception ex)
            {
                return JsResult(false, ex.Message);
            }
        }

        string HandleEngineBrowse(string engineName)
        {
            var type = JsEngineConfig.GetEngineTypeByName(engineName);
            if (type == null || type == JsEngineType.Jint)
                return "{\"success\":false,\"error\":\"不支持的引擎\"}";

            var hwnd = _window.GetNativeHandle();
            string? folder = PickFolder("选择引擎目录", hwnd);
            if (folder == null)
                return "{\"success\":false,\"error\":\"用户取消选择\"}";

            var dest = JsEngineDownloader.EngineDir(type.Value);
            try
            {
                if (Directory.Exists(dest))
                    Directory.Delete(dest, true);
                Directory.CreateDirectory(dest);
                var result = ScanAndCopyEngineDlls(folder, dest, type.Value);
                if (result.Success)
                {
                    Console.WriteLine($"[Engine] {engineName} installed from {folder}");
                    JsEngineInfoRegistry.Register(type.Value, result.AssemblyPath!, folder);
                    return "{\"success\":true,\"message\":\"引擎安装成功\"}";
                }
                return JsResult(false, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                return JsResult(false, ex.Message);
            }
        }

        string HandleEngineApply(string engineName)
        {
            var type = JsEngineConfig.GetEngineTypeByName(engineName);
            if (type == null)
                return "{\"success\":false,\"error\":\"不支持的引擎\"}";

            if (type == JsEngineType.Jint)
            {
                _renderingSettings.JsEngine = "Jint";
                JsEngineConfig.DefaultEngineType = JsEngineType.Jint;
                return "{\"success\":true,\"message\":\"已切换到内置 Jint 引擎\"}";
            }

            if (!JsEngineDownloader.IsEngineDownloaded(type.Value))
                return "{\"success\":false,\"error\":\"引擎未下载，请先下载或从本地安装\"}";

            _renderingSettings.JsEngine = engineName;
            JsEngineConfig.DefaultEngineType = type.Value;

            Console.WriteLine($"[JS] Engine applied for new tabs: {type.Value}");
            return $"{{\"success\":true,\"message\":\"已切换到 {JsEscape(engineName)} 引擎，新标签页将使用新引擎\"}}";
        }

        _renderingSettings.OnChanged += () =>
        {
            RenderingSettingsConfig.Save(_renderingSettings);
            DocumentManager.UseCustomParser = _renderingSettings.UseCustomHtmlParser;
            _window.TargetFrameTimeMs = _renderingSettings.TargetFps > 0
                ? (float)(1000.0 / _renderingSettings.TargetFps)
                : 16.67f;
            // Smooth scrolling toggle: gate the page and element scroll physics so
            // the setting takes effect immediately (OFF = instant wheel/keys).
            _scroll.SmoothEnabled = _renderingSettings.SmoothScrolling;
            _scrollInteraction.SmoothEnabled = _renderingSettings.SmoothScrolling;
            // 设置变更时使页面缓存失效，确保新设置生效
            _skiaRenderer.InvalidatePageCache();
        };

        // Apply the loaded smooth-scrolling preference up front (the OnChanged
        // handler above runs for later changes only).
        _scroll.SmoothEnabled = _renderingSettings.SmoothScrolling;
        _scrollInteraction.SmoothEnabled = _renderingSettings.SmoothScrolling;

        _renderingSettings.OnGpuChanged += (enable) =>
        {
            _skiaRenderer.TrySetGpu(enable);
        };

        _chrome.OnChanged += () => _input.NeedsRedraw = true;
        _devTools.SetSourceChangeHandler(async (html) =>
        {
            _currentHtml = html;

            if (_currentLoad != null)
            {
                // Dispose old document
            }

            _currentLoad = await _docManager.LoadHtmlAsync(html);
            var (pw, ph) = _window.GetClientSize();
            _scrollInteraction.SetDocument(_currentLoad.Document);
            _jsEngine.LoadDocument(_currentLoad.Document);
            SetDevToolsDocument(_currentLoad.Document, html);
            _input.NeedsRedraw = true;
        });
        _devTools.OnChanged += () =>
        {
            UpdateImeTarget();
            _input.NeedsRedraw = true;
        };
        _input.OnFormInputKey = (c, key, shift) => HandleFormInputKey(c, key, shift);
        _input.OnDevToolsInput = (c, key, shift) => DevToolsHandleInput(c, key, shift);
        _input.OnDevToolsClick = (x, y, isDown) => HandleDevToolsClick(x, y, isDown);
        _input.OnTaskManagerKey = () =>
        {
            _taskManagerPage.Toggle();
            _input.NeedsRedraw = true;
        };

        _input.OnDevToolsWheel = (delta, mx, my) => _devTools.HandleWheel(delta, mx, my);
        _input.OnDevToolsMouseMove = (x, y) =>
        {
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            int wh = (int)(ph / _dpiScale);
            _devTools.HandleMouseMove(x, y, ww, wh);
        };
        _input.OnScrollContainerWheel = (dx, dy, mx, my) =>
        {
            // Shift+wheel scrolls horizontally, matching the page scroller.
            if (_input.IsShiftDown)
                (dx, dy) = (dy, 0);
            float pageX = mx + _scroll.ScrollX;
            float pageY = my - _contentOffset + _scroll.ScrollY;
            return _scrollInteraction.HandleWheel(dx, dy, pageX, pageY);
        };
        _scrollInteraction.OnScrollChanged = (box) =>
        {
            // Element scrollbar wheel/drag: route through the element-scroll path
            // (layered fast path or paint-only rebuild). Only the document root /
            // body keeps the page-scroll path.
            var doc = _currentLoad?.Document;
            if (ReferenceEquals(box, doc?.DocumentElement?.LayoutBox) ||
                ReferenceEquals(box, doc?.Body?.LayoutBox))
            {
                _scrollDirty = true;
                _input.NeedsRedraw = true;
            }
            else
            {
                MarkElementScrollDirty(box);
            }
        };
        _input.OnImeChar = HandleImeChar;
        _input.OnImeTargetChanged = UpdateImeTarget;
        _input.OnCopy = PerformCopy;
        _input.OnPaste = PerformPaste;
        _input.OnCut = PerformCut;
        _input.OnSelectAll = PerformSelectAll;

        WireTabEngine(_jsEngine, -1);
        AcruxBuiltins.GlobalEngineAction = (action, engineName) => HandleEngineAction(action, engineName);

        // Wire DOM keyboard events
        _input.OnDomKeyDown = (charCode, key, repeat) => HandleDomKeyDown(charCode, key, repeat);
        _input.OnDomKeyUp = (charCode, key, repeat) => HandleDomKeyUp(charCode, key, repeat);
        _input.OnDomChar = (charCode) => HandleDomChar(charCode);

        // Wire DOM mouse events
        _input.OnDomMouseMove = (x, y) => HandleDomMouseMove(x, y);
        _input.OnDomMouseDown = (x, y, isDown) => { /* handled via OnDomClick */ };
        _input.OnDomMouseUp = (x, y, isDown) => HandleDomMouseUp(x, y);

        _input.OnSettingsPageClick = (x, y, isUp) =>
        {
            if (isUp)
            {
                _renderingSettingsPage.HandleMouseUp();
                return false;
            }
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            return _renderingSettingsPage.HandleClick(x, y, ww, _contentOffset);
        };

        _input.OnSettingsPageMove = (x, y) =>
        {
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            return _renderingSettingsPage.HandleMouseMove(x, y, ww, _contentOffset);
        };

        _input.OnSettingsPageWheel = (delta) =>
        {
            if (!_renderingSettingsPage.Visible) return false;
            var (pw, ph) = _window.GetClientSize();
            int wh = (int)(ph / _dpiScale);
            _renderingSettingsPage.HandleWheel(delta, wh, _contentOffset);
            return true;
        };

        _input.OnTaskManagerPageClick = (x, y, isUp) =>
        {
            if (isUp)
            {
                _taskManagerPage.HandleMouseUp();
                return false;
            }
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            int wh = (int)(ph / _dpiScale);
            return _taskManagerPage.HandleClick(x, y, ww, wh);
        };

        _input.OnTaskManagerPageMove = (x, y) =>
        {
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            int wh = (int)(ph / _dpiScale);
            return _taskManagerPage.HandleMouseMove(x, y, ww, wh);
        };

        _input.OnTaskManagerPageWheel = (delta) =>
        {
            if (!_taskManagerPage.Visible) return false;
            var (pw, ph) = _window.GetClientSize();
            int wh = (int)(ph / _dpiScale);
            _taskManagerPage.HandleWheel(delta, wh);
            return true;
        };

        _taskManagerPage.OnEndProcess += (tabIndex) =>
        {
            _chrome.CloseTab(tabIndex);
            _input.NeedsRedraw = true;
        };

        _input.OnDialogClick = (x, y) =>
        {
            if (!_dialogActive) return false;

            bool isPrompt = _dialogType.StartsWith("prompt:");

            if (_dialogOkRect.Contains(x, y))
            {
                CompleteDialog(accepted: true, isPrompt ? _dialogInput : null);
                return true;
            }

            if (_dialogCancelRect.Contains(x, y) && (isPrompt || _dialogType == "confirm"))
            {
                CompleteDialog(accepted: false, null);
                return true;
            }

            if (_dialogInputRect.Contains(x, y) && isPrompt)
                return true;

            return true;
        };

        _input.OnPageBubbleClick = (x, y) =>
        {
            // Only the buttons are claimed; the rest of the page area stays usable
            // (a wedged page simply will not answer, which is the bubble's whole point).
            if (!_crashReloadRect.IsEmpty && _crashReloadRect.Contains(x, y))
            {
                ReloadRemoteTab(_chrome.ActiveTabIndex);
                return true;
            }
            if (_bubbleWaitRect.Contains(x, y) || _bubbleReloadRect.Contains(x, y) || _bubbleKillRect.Contains(x, y))
            {
                HandlePageBubbleClick(x, y);
                return true;
            }
            return false;
        };

        Initialize();
    }

    private void Initialize()
    {
        // Wire the new JsIntegrationService for enhanced JS support
        if (_jsEngine.IntegrationService != null)
        {
            var jsInt = _jsEngine.IntegrationService;
            _eventLoop.OnAfterTask += () =>
            {
                jsInt.ProcessTimers();
                jsInt.MicrotaskQueue.DrainMicrotasks();
            };
        }

        // Wire JS console output from the remote engine
        _jsEngine.Adapter?.OnConsoleLog += (method, message) =>
            Console.WriteLine($"[JS {method}] {message}");

        // Performance hub must be live before anything else uses the heavy
        // subsystems, so the first layout/style pass is already instrumented.
        InitializePerformanceHub();

        _chrome.Initialize();
        StartWatchdog();

        _skiaRenderer.Initialize(1024, 768, enableDirtyRegions: true);
        _skiaRenderer.DpiScale = _dpiScale;

        // Attempt GPU acceleration (OpenGL via SkiaSharp GRContext)
        if (_skiaRenderer.TryEnableGpu())
        {
            _skiaRenderer.Initialize(1024, 768, enableDirtyRegions: false);
            _skiaRenderer.DpiScale = _dpiScale;
        }
        else
        {
            Console.WriteLine("GPU acceleration unavailable, using CPU rendering");
        }

        _skiaRenderer.Settings = _renderingSettings;

        _eventLoop.Start();

        _input.WireEvents();

        // Clear selection state when window loses focus
        _window.OnKillFocus = () =>
        {
            _isSelecting = false;
        };
    }

    private string? ShowDialog(string message, string? type)
    {
        _dialogMessage = message;
        _dialogType = type ?? "";
        _dialogInput = (type ?? "").StartsWith("prompt:") ? (type ?? "")[7..] : "";
        _dialogResult = null;
        _dialogActive = true;

        var (pw, ph) = _window.GetClientSize();
        int ww = (int)(pw / _dpiScale);
        int wh = (int)(ph / _dpiScale);

        while (_dialogActive)
        {
            if (!_window.PumpPendingMessage())
                break;

            RenderDialogFrame(ww, wh);

            Thread.Sleep(10);
        }

        _dialogActive = false;
        return _dialogResult;
    }

    /// <summary>
    /// Resolve the modal the user just answered. An in-process page is waiting on
    /// <see cref="ShowDialog"/>'s loop; a page-host page is blocked in its own process,
    /// so its answer travels back over the pipe and the shell keeps painting.
    /// </summary>
    private void CompleteDialog(bool accepted, string? text)
    {
        if (_remoteDialogId >= 0)
        {
            if (_remoteTabs.TryGetValue(_remoteDialogTab, out var view) && !view.Proc.IsDead)
                view.Proc.RespondDialog(_remoteDialogId, accepted, accepted ? text : null);
            _remoteDialogId = -1;
            _remoteDialogTab = -1;
            _dialogActive = false;
            _input.NeedsRedraw = true;
            PresentRemoteDialog();   // another tab may have been waiting behind this one
            return;
        }

        _dialogResult = _dialogType switch
        {
            "confirm" => accepted ? "true" : "false",
            _ when _dialogType.StartsWith("prompt:") => accepted ? (text ?? "") : null,
            _ => "",
        };
        _dialogActive = false;
    }

    /// <summary>A page-host modal arrived. It shows only while its tab is frontmost;
    /// otherwise it waits — the page's script thread is blocked either way, like any
    /// browser handling a dialog on a background tab.</summary>
    private void ShowRemoteDialog(int tab, int requestId, string message, string type)
    {
        _pendingDialogs[tab] = (requestId, message, type);
        PresentRemoteDialog();
    }

    private void PresentRemoteDialog()
    {
        if (_remoteDialogId >= 0) return;   // one modal on screen at a time
        if (!_pendingDialogs.TryGetValue(_chrome.ActiveTabIndex, out var pending)) return;

        _pendingDialogs.Remove(_chrome.ActiveTabIndex);
        _remoteDialogId = pending.RequestId;
        _remoteDialogTab = _chrome.ActiveTabIndex;

        _dialogMessage = pending.Message;
        _dialogType = pending.Type;
        _dialogInput = pending.Type.StartsWith("prompt:") ? pending.Type[7..] : "";
        _dialogResult = null;
        _dialogActive = true;
        _input.NeedsRedraw = true;
    }

    /// <summary>Put the on-screen modal back to waiting, because another tab came to the
    /// front. The page stays blocked on it until its tab is shown again.</summary>
    private void ParkRemoteDialog()
    {
        if (_remoteDialogId < 0) return;
        _pendingDialogs[_remoteDialogTab] = (_remoteDialogId, _dialogMessage, _dialogType);
        _remoteDialogId = -1;
        _remoteDialogTab = -1;
        _dialogActive = false;
        _input.NeedsRedraw = true;
    }

    /// <summary>Forget a modal that can never be answered — its tab died or closed.</summary>
    private void DropRemoteDialogs(int tab)
    {
        _pendingDialogs.Remove(tab);
        if (_remoteDialogTab != tab) return;
        _remoteDialogId = -1;
        _remoteDialogTab = -1;
        _dialogActive = false;
        _input.NeedsRedraw = true;
    }

    private void RenderDialogFrame(int windowWidth, int windowHeight)
    {
        _skiaRenderer.Canvas.Clear(SKColors.White);

        var title = _currentLoad?.Document.Title ?? "Acrux";
        if (_processTabs)
        {
            var rvT = ActiveRemote();
            if (rvT != null && !string.IsNullOrEmpty(rvT.Proc.Title))
                title = rvT.Proc.Title;
        }
        var currentUrl = _chrome.GetCurrentUrl();
        _chrome.RenderChrome(_skiaRenderer.Canvas, windowWidth, windowHeight, currentUrl ?? "acrux://local", title);

        float devToolsHeight = _devTools.Visible ? _devTools.PanelHeight : 0;
        float contentViewportHeight = windowHeight - _contentOffset - _chrome.GetStatusBarHeight() - devToolsHeight;
        if (_processTabs)
            RenderRemotePage(windowWidth, contentViewportHeight);
        else
            _skiaRenderer.RenderWithScroll(_displayList, _contentOffset,
                _scroll.ScrollX, _scroll.ScrollY,
                windowWidth, contentViewportHeight);

        RenderDialogOverlay(_skiaRenderer.Canvas, windowWidth, windowHeight);

        var pixels = _skiaRenderer.GetPixelData();
        _window.Render(pixels, _skiaRenderer.PhysicalWidth, _skiaRenderer.PhysicalHeight);
    }

    private void RenderDialogOverlay(SKCanvas canvas, float windowWidth, float windowHeight)
    {
        using var overlay = new SKPaint { Color = new SKColor(0, 0, 0, 128), Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, 0, windowWidth, windowHeight, overlay);

        float dlgW = 360, dlgH = 160;
        float dlgX = (windowWidth - dlgW) / 2;
        float dlgY = (windowHeight - dlgH) / 2;

        using var bg = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(dlgX, dlgY, dlgW, dlgH, 8, 8, bg);

        using var border = new SKPaint { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(dlgX, dlgY, dlgW, dlgH, 8, 8, border);

        using var titlePaint = FontHelper.CreatePaint(14);
        using var titleFont = FontHelper.CreateFont(14);
        titlePaint.Color = SKColor.Parse("#333333");
        string titleText = _dialogType == "alert" ? "Alert" : _dialogType == "confirm" ? "Confirm" : "Prompt";
        canvas.DrawText(titleText, dlgX + 16, dlgY + 24, SKTextAlign.Left, titleFont, titlePaint);

        using var msgPaint = FontHelper.CreatePaint(13);
        using var msgFont = FontHelper.CreateFont(13);
        msgPaint.Color = SKColor.Parse("#666666");
        float msgY = dlgY + 55;
        float maxMsgW = dlgW - 32;
        string msg = _dialogMessage;
        if (msgFont.MeasureText(msg) > maxMsgW)
        {
            while (msgFont.MeasureText(msg + "…") > maxMsgW && msg.Length > 0)
                msg = msg[..^1];
            msg += "…";
        }
        canvas.DrawText(msg, dlgX + 16, msgY, SKTextAlign.Left, msgFont, msgPaint);

        bool isPrompt = _dialogType.StartsWith("prompt:");
        float btnY = dlgY + dlgH - 40;

        if (isPrompt)
        {
            using var inputPaint = FontHelper.CreatePaint(13);
            using var inputFont = FontHelper.CreateFont(13);
            inputPaint.Color = SKColor.Parse("#333333");
            using var inputBg = new SKPaint { Color = SKColor.Parse("#F5F5F5"), Style = SKPaintStyle.Fill };
            float inpX = dlgX + 16, inpY = dlgY + 80, inpW = dlgW - 32, inpH = 28;
            canvas.DrawRoundRect(inpX, inpY, inpW, inpH, 4, 4, inputBg);
            canvas.DrawText(_dialogInput ?? "", inpX + 8, inpY + inpH * 0.7f, SKTextAlign.Left, inputFont, inputPaint);
            _dialogInputRect = new SKRect(inpX, inpY, inpX + inpW, inpY + inpH);
        }

        float btnW = 70, btnH = 28;
        float btnSpacing = 10;
        float totalBtnW = (isPrompt || _dialogType == "confirm") ? btnW * 2 + btnSpacing : btnW;
        float btnStartX = dlgX + (dlgW - totalBtnW) / 2;

        if (isPrompt || _dialogType == "confirm")
        {
            _dialogCancelRect = new SKRect(btnStartX, btnY, btnStartX + btnW, btnY + btnH);
            using var cancelPaint = new SKPaint { Color = SKColor.Parse("#E0E0E0"), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(_dialogCancelRect, 4, 4, cancelPaint);
            using var cancelFontPaint = FontHelper.CreatePaint(12);
            using var cancelFont = FontHelper.CreateFont(12);
            cancelFontPaint.Color = SKColor.Parse("#333333");
            string cancelLabel = isPrompt ? "Cancel" : "Cancel";
            float cw = cancelFont.MeasureText(cancelLabel);
            canvas.DrawText(cancelLabel, _dialogCancelRect.Left + (btnW - cw) / 2, _dialogCancelRect.Top + btnH * 0.7f, SKTextAlign.Left, cancelFont, cancelFontPaint);
            btnStartX += btnW + btnSpacing;
        }

        _dialogOkRect = new SKRect(btnStartX, btnY, btnStartX + btnW, btnY + btnH);
        using var okPaint = new SKPaint { Color = SKColor.Parse("#1A73E8"), Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(_dialogOkRect, 4, 4, okPaint);
        using var okFontPaint = FontHelper.CreatePaint(12);
        using var okFont = FontHelper.CreateFont(12);
        okFontPaint.Color = SKColors.White;
        string okLabel = isPrompt ? "OK" : "OK";
        float ow = okFont.MeasureText(okLabel);
        canvas.DrawText(okLabel, _dialogOkRect.Left + (btnW - ow) / 2, _dialogOkRect.Top + btnH * 0.7f, SKTextAlign.Left, okFont, okFontPaint);
    }

    public async Task RunAsync()
    {
        Console.WriteLine("Acrux - Starting...");
        LogStart("RunAsync");

        // ---- Initialize the performance layer ----
        LogStart("InitializePerformanceHub");
        InitializePerformanceHub();
        LogDone("InitializePerformanceHub");

        //Load test_css_feature.html in Acrux.Core.Resources
        LogStart("LoadHtmlAsync");
        if (_processTabs)
        {
            // Multi-process mode: the initial page is owned and rendered by the
            // tab-host child; the parent only composites its frames.
            _lastActiveTabIndex = _chrome.ActiveTabIndex;
            var initialView = EnsureRemoteTab(_lastActiveTabIndex, "acrux://test-css");
            initialView.PendingInitial = "acrux://test-css";
        }
        else
        {
        _currentHtml = DocumentManager.TestCssFeatureHtml;
        var initialLoad = await _docManager.LoadHtmlAsync(_currentHtml);
        _currentLoad = initialLoad;
        _scrollInteraction.SetDocument(_currentLoad.Document);
        LogDone("LoadHtmlAsync");

        var devTool = new LayoutDevTool();
        var debugReport = devTool.GenerateReport(_currentLoad!.Document, 1024, 768);
        File.WriteAllText("layout_debug.txt", debugReport);
        Console.WriteLine($"[Debug] Initial report saved ({debugReport.Length} chars)");
        Console.WriteLine(devTool.GenerateQuickReport(_currentLoad.Document));

        LogStart("LoadDocument");
        _jsEngine.LoadDocument(_currentLoad.Document);
        LogDone("LoadDocument");

        LogStart("DevTools.SetDocument");
        SetDevToolsDocument(_currentLoad.Document, _currentHtml);
        LogDone("DevTools.SetDocument");

        LogStart("RunPageScripts");
        RunPageScripts(null);
        LogDone("RunPageScripts");

        LogStart("BuildDisplayList");
        BuildDisplayList(1024, 768);
        LogDone("BuildDisplayList");
        _lastLayoutWidth = 1024;

        _lastActiveTabIndex = _chrome.ActiveTabIndex;
        _tabStates[_lastActiveTabIndex] = new TabState
        {
            Html = _currentHtml,
            LoadResult = _currentLoad,
            ScrollX = 0,
            ScrollY = 0
        };

        // Create process for the initial tab
        LogStart("CreateProcess");
        var initialProc = _processManager.CreateProcess(0, "acrux://local");
        LogDone("CreateProcess");
        initialProc.UpdateTitle(_currentLoad.Document.Title ?? "");

        var bodyBox = _currentLoad.Document.Body?.LayoutBox;
        var lastContentHeight = bodyBox?.BorderBox.Height ?? 0;
        }

        LogStart("WireNavigation");
        WireNavigation();
        LogDone("WireNavigation");

        // Command line startup page: Acrux <url | file.html>
        if (!string.IsNullOrWhiteSpace(_startupUrl))
        {
            if (_startupUrl.StartsWith("http://") || _startupUrl.StartsWith("https://"))
                NavigateToHttp(_startupUrl);
            // A file:// URL is already absolute. Running it through Path.GetFullPath
            // would treat "file:///C:/x" as a relative path and produce a path that
            // does not exist, so the URL form has to be recognised before the
            // local-path form.
            else if (_startupUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                NavigateToFile(_startupUrl);
            else if (_startupUrl.StartsWith("acrux://", StringComparison.Ordinal))
                _chrome.NavigateToUrl(_startupUrl); // routes through OnNavigate per tab mode
            else
                NavigateToFile(new Uri(Path.GetFullPath(_startupUrl)).AbsoluteUri);
        }

        LogStart("_window.Run(RenderFrame)");
        try
        {
            _window.Run(RenderFrame);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRASH] In _window.Run(RenderFrame): {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            try { File.WriteAllText("acrux_crash.log", ex.ToString()); } catch { }
            throw;
        }
        LogDone("_window.Run(RenderFrame)");

        Console.WriteLine("Acrux closed.");
    }

    private static void LogStart(string name)
    {
        Console.WriteLine($"[Startup] > {name}");
    }

    private static void LogDone(string name)
    {
        Console.WriteLine($"[Startup] < {name} OK");
    }

    private static void LogCtor(string name)
    {
        Console.WriteLine($"[Ctor] > {name}");
        try { File.AppendAllText("acrux_ctor.log", $"[Ctor] > {name}\n"); } catch { }
    }

    private static void LogCtorDone(string name)
    {
        Console.WriteLine($"[Ctor] < {name} OK");
        try { File.AppendAllText("acrux_ctor.log", $"[Ctor] < {name} OK\n"); } catch { }
    }

    private void WireNavigation()
    {
        _chrome.OnNavigate = (url) =>
        {
            Console.WriteLine($"Navigating to: {url}");
            _input.NeedsRedraw = true;

            if (_processTabs)
            {
                ProcessNavigateTo(_chrome.ActiveTabIndex, url);
                return;
            }

            if (url.StartsWith("acrux://"))
            {
                if (url == "acrux://newtab" || url == "acrux://local")
                {
                    _currentHtml = DocumentManager.DefaultHtml;
                    LoadAndRenderHtml(_currentHtml);
                    _scroll.ScrollTo(0, 0);
                }
                else if (url == "acrux://js-test")
                {
                    _currentHtml = DocumentManager.JsTestHtml;
                    LoadAndRenderHtml(_currentHtml);
                    _scroll.ScrollTo(0, 0);
                }
                else if (url == "acrux://element-test")
                {
                    _currentHtml = DocumentManager.ElementTestHtml;
                    LoadAndRenderHtml(_currentHtml);
                    _scroll.ScrollTo(0, 0);
                }
                else if (url == "acrux://debug")
                {
                    _currentHtml = DocumentManager.DebugHtml;
                    LoadAndRenderHtml(_currentHtml);
                    _scroll.ScrollTo(0, 0);
                }
                else if (url == "acrux://js")
                {
                    _currentHtml = DocumentManager.JsEngineHtml;
                    LoadAndRenderHtml(_currentHtml);
                    _scroll.ScrollTo(0, 0);
                }
            }
            else if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                NavigateToHttp(url);
            }
            // file:// 协议支持
            else if (url.StartsWith("file://"))
            {
                NavigateToFile(url);
            }
            else
            {
                NavigateToSearch(url);
            }
        };

        _chrome.OnRefresh = () =>
        {
            Console.WriteLine("Refreshing page...");
            _input.NeedsRedraw = true;
            if (_processTabs)
            {
                ProcessNavigateTo(_chrome.ActiveTabIndex, _chrome.GetCurrentUrl());
                return;
            }
            if (!string.IsNullOrEmpty(_currentHtml))
            {
                LoadAndRenderHtml(_currentHtml);
            }
        };

        _chrome.OnHome = () =>
        {
            Console.WriteLine("Going home...");
            _chrome.NavigateToUrl("acrux://local");
        };

        _chrome.OnTabChanged = (url) =>
        {
            Console.WriteLine($"Tab changed to: {url}");
            _input.NeedsRedraw = true;

            if (_processTabs)
            {
                OnTabChangedProcess(url);
                return;
            }

            if (_threadedTabs)
            {
                OnTabChangedThreaded(url);
                return;
            }

            int currentTabIndex = _chrome.ActiveTabIndex;

            // 保存当前标签页状态
            if (_lastActiveTabIndex >= 0 && _currentLoad != null)
            {
                _tabStates[_lastActiveTabIndex] = new TabState
                {
                    Html = _currentHtml,
                    LoadResult = _currentLoad,
                    ScrollX = _scroll.ScrollX,
                    ScrollY = _scroll.ScrollY,
                    DomNodeCount = CountDomNodes(_currentLoad.Document),
                    LayoutBoxCount = CountLayoutBoxes(_currentLoad.Document)
                };
                var oldProc = _processManager.GetProcess(_lastActiveTabIndex);
                if (oldProc != null)
                {
                    oldProc.UpdateTitle(_currentLoad.Document.Title ?? "");
                    oldProc.UpdateUrl(url);
                }
            }

            _lastActiveTabIndex = currentTabIndex;

            // 确保目标标签页有进程
            _processManager.GetOrCreate(currentTabIndex, url);

            // 尝试从进程获取最新的 DisplayList
            var procDl = _processManager.GetDisplayList(currentTabIndex);
            if (procDl != null && procDl.Count > 0)
            {
                _displayList = procDl;
            }

            // 检查目标标签页是否有已保存的状态
            if (_tabStates.TryGetValue(currentTabIndex, out var savedState) && !string.IsNullOrEmpty(savedState.Html))
            {
                _currentHtml = savedState.Html;
                _currentLoad = savedState.LoadResult;
                _scrollInteraction.SetDocument(_currentLoad?.Document);
                _scroll.ScrollTo(savedState.ScrollX, savedState.ScrollY);

                if (_currentLoad != null)
                {
                    // JS 引擎初始化和文档关联放到后台线程，避免阻塞 UI
                    // 渲染立即进行：从进程获取 DisplayList，若不存在则重建
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            _jsEngine.LoadDocument(_currentLoad.Document);
                            _eventLoop.PostTask(() =>
                            {
                                SetDevToolsDocument(_currentLoad?.Document, _currentHtml);
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TabSwitch] LoadDocument error: {ex.Message}");
                        }
                    });

                    // 优先使用进程缓存的 DisplayList；若已存在则不重复设置
                    if (_displayList == null || _displayList.Count == 0)
                    {
                        var tabDl = _processManager.GetDisplayList(currentTabIndex);
                        if (tabDl != null && tabDl.Count > 0)
                        {
                            _displayList = tabDl;
                        }
                        else
                        {
                            BuildDisplayList(_lastWindowWidth, _lastWindowHeight);
                        }
                    }
                }
            }
            else
            {
                _isNavigating = false; // 清除旧导航，允许新导航
                if (url.StartsWith("http://") || url.StartsWith("https://"))
                {
                    NavigateToHttp(url);
                }
                else if (url == "acrux://newtab" || url == "acrux://local")
                {
                    _currentHtml = DocumentManager.DefaultHtml;
                    LoadAndRenderHtml(_currentHtml);
                }
                else if (url == "acrux://js-test")
                {
                    _currentHtml = DocumentManager.JsTestHtml;
                    LoadAndRenderHtml(_currentHtml);
                }
                else if (url == "acrux://element-test")
                {
                    _currentHtml = DocumentManager.ElementTestHtml;
                    LoadAndRenderHtml(_currentHtml);
                }
                else if (url == "acrux://debug")
                {
                    _currentHtml = DocumentManager.DebugHtml;
                    LoadAndRenderHtml(_currentHtml);
                }
                else if (url == "acrux://js")
                {
                    _currentHtml = DocumentManager.JsEngineHtml;
                    LoadAndRenderHtml(_currentHtml);
                }
            }
        };

        _chrome.OnNewTab = () =>
        {
            Console.WriteLine("New tab requested");
            int newIdx = _chrome.TabCount;
            _processManager.CreateProcess(newIdx, "acrux://newtab");
            _input.NeedsRedraw = true;
        };

        _chrome.OnCloseTab = (index) =>
        {
            Console.WriteLine($"Close tab {index} requested");

            // A modal of a closing tab is unanswerable; drop it on the UI thread.
            _eventLoop.PostTask(() => DropRemoteDialogs(index));

            // 所有重清理操作放到后台线程，绝不阻塞 UI 线程
            _ = Task.Run(() =>
            {
                if (_processTabs && _remoteTabs.TryRemove(index, out var remoteView))
                {
                    try { remoteView.Dispose(); }
                    catch (Exception ex) { Console.WriteLine($"[CloseTab] remote dispose error: {ex.Message}"); }
                }
                try
                {
                    _processManager.DestroyProcess(index);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CloseTab] DestroyProcess error: {ex.Message}");
                }

                // 清除标签页状态
                if (_tabStates.TryRemove(index, out var oldState) && oldState.LoadResult != null)
                {
                    try
                    {
                        // Threaded mode: the tab's dedicated engine dies with the tab.
                        // DestroyProcess above already joined the worker thread, so the
                        // engine is no longer in use there.
                        oldState.Engine?.Dispose();
                    }
                    catch (Exception ex) { Console.WriteLine($"[Dispose] Tab state doc error: {ex.Message}"); }
                }

                // 如果关闭的是当前活动标签页，清除当前加载
                _eventLoop.PostTask(() =>
                {
                    if (_pendingAdoptTab == index)
                    {
                        _pendingAdoptTab = -1;
                        _adoptionPending = false;
                    }
                    if (_currentLoad != null && _chrome.ActiveTabIndex == index)
                    {
                        try { }
                        catch (Exception ex) { Console.WriteLine($"[Dispose] Current doc error: {ex.Message}"); }
                        _currentLoad = null;
                        _scrollInteraction.SetDocument(null);
                    }
                });

                // 后台 GC，不阻塞 UI
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
            });
        };

        if (_processTabs)
        {
            // The parent holds no DOM: park the shared engine and forward all
            // page input to the active tab's child process instead.
            _jsEngine = _parkEngine ??= new JavaScriptEngine(NullJsEngineAdapter.Instance);

            _input.OnDomClick = (x, y) =>
            {
                var v = ActiveRemote();
                // Press only: the release arrives through OnDomMouseUp. The child needs
                // the gap between them — page-text selection extends while the button is
                // held, so a synthetic down+up pair per click would kill every drag.
                v?.Proc.MouseDown(x, y - _contentOffset);
            };
            _input.OnDomMouseUp = (x, y, isUp) =>
            {
                ActiveRemote()?.Proc.MouseUp(x, y - _contentOffset);
            };
            _input.OnDomMouseMove = (x, y) =>
            {
                long now = Environment.TickCount64;
                if (now - _lastRemoteMoveTick < 24) return;
                _lastRemoteMoveTick = now;
                ActiveRemote()?.Proc.MouseMove(x, y - _contentOffset);
            };
            _input.OnDomKeyDown = (charCode, key, repeat) =>
                ActiveRemote()?.Proc.KeyDown((ushort)charCode, (ushort)key, repeat);
            _input.OnDomChar = (charCode) => ActiveRemote()?.Proc.Char((ushort)charCode);
            // Wheel goes to the child first (element scrollers); page scroll
            // happens only when the child passes or the reply times out.
            _input.OnScrollContainerWheel = (dx, dy, mx, my) =>
            {
                // Speculative first: the page scrolls with the wheel IMMEDIATELY.
                // The child still adjudicates element scrollers; if it claims the
                // wheel, FlushPendingWheel rolls the page scroll back. Waiting for
                // the child's reply before scrolling added a full pipe round-trip
                // to every notch — the "white strip lingers" latency users felt.
                ApplyRemotePageScroll(dx, dy);
                var v = ActiveRemote();
                if (v != null && v.Proc.IsConnected)
                {
                    _pendingWheelX += dx;
                    _pendingWheelY += dy;
                    if (!_wheelAwaiting)
                    {
                        _wheelAwaiting = true;
                        _wheelSentTick = Environment.TickCount64;
                    }
                    v.Proc.Wheel((float)dx, (float)dy, mx, my - _contentOffset);
                }
                return true;
            };
        }
    }

    private bool _isNavigating;
    private int _navigationSeq;
    private int _lastActiveTabIndex = -1;
    private string? _currentBaseUrl;
    private int _navigatingTabIndex = -1; // which tab initiated the current navigation
    private readonly ConcurrentDictionary<int, TabState> _tabStates = new();

    private class TabState
    {
        public string Html { get; set; } = "";
        public DocumentManager.DocumentLoadResult? LoadResult { get; set; }
        public float ScrollX { get; set; }
        public float ScrollY { get; set; }
        public int DomNodeCount { get; set; }
        public int LayoutBoxCount { get; set; }
        public JavaScriptEngine? Engine { get; set; }
        public string? BaseUrl { get; set; }
        public string? PendingNavigate { get; set; }
    }

    /// <summary>
    /// Wire a JS engine's host callbacks (dialogs, location, window props) for a
    /// specific tab. In threaded-tab mode the callbacks must route to the owning
    /// tab — the engine may be pumping on its worker thread while the tab is in
    /// the background.
    /// </summary>
    private void WireTabEngine(JavaScriptEngine eng, int tabIndex)
    {
        eng.ShowDialog = (msg, type) =>
        {
            if (!_threadedTabs || tabIndex < 0 || _chrome.ActiveTabIndex == tabIndex)
                return ShowDialog(msg, type);
            // Background tab: do not steal the modal from the active tab.
            Console.WriteLine($"[Tab {tabIndex}] {type}: {msg}");
            return null;
        };

        if (eng.LocationHost != null)
        {
            eng.LocationHost.OnNavigate = (url) =>
            {
                Console.WriteLine($"Location navigate to: {url}");
                _eventLoop.PostTask(() =>
                {
                    if (string.IsNullOrWhiteSpace(url)) return;
                    if (!_threadedTabs || tabIndex < 0 || _chrome.ActiveTabIndex == tabIndex)
                    {
                        _chrome.NavigateToUrl(url);
                    }
                    else if (_tabStates.TryGetValue(tabIndex, out var st))
                    {
                        // Remember it; the tab navigates when the user returns to it.
                        st.PendingNavigate = url;
                        _chrome.SetTabUrl(tabIndex, url);
                        _input.NeedsRedraw = true;
                    }
                });
            };
            eng.LocationHost.OnReload = () =>
            {
                _eventLoop.PostTask(() =>
                {
                    if (_chrome.ActiveTabIndex == tabIndex) _chrome.OnRefresh?.Invoke();
                });
            };
        }

        if (eng.Builtins != null)
        {
            var b = eng.Builtins;
            b.GetInnerWidth = () => (int)(_window.GetClientSize().width / _dpiScale);
            b.GetInnerHeight = () => (int)(_window.GetClientSize().height / _dpiScale);
            b.GetDevicePixelRatio = () => _dpiScale;
            b.GetScrollX = () => (!_threadedTabs || tabIndex < 0 || _chrome.ActiveTabIndex == tabIndex)
                ? (int)_scroll.ScrollX
                : (_tabStates.TryGetValue(tabIndex, out var s) ? (int)s.ScrollX : 0);
            b.GetScrollY = () => (!_threadedTabs || tabIndex < 0 || _chrome.ActiveTabIndex == tabIndex)
                ? (int)_scroll.ScrollY
                : (_tabStates.TryGetValue(tabIndex, out var s) ? (int)s.ScrollY : 0);
            b.OnScrollTo = (x, y) =>
            {
                if (!_threadedTabs || tabIndex < 0 || _chrome.ActiveTabIndex == tabIndex)
                    _scroll.ScrollTo(x, y);
                else if (_tabStates.TryGetValue(tabIndex, out var s))
                {
                    s.ScrollX = x; s.ScrollY = y;
                }
            };
            b.OnScrollBy = (x, y) =>
            {
                if (!_threadedTabs || tabIndex < 0 || _chrome.ActiveTabIndex == tabIndex)
                    _scroll.ScrollBy(x, y);
                else if (_tabStates.TryGetValue(tabIndex, out var s))
                {
                    s.ScrollX += x; s.ScrollY += y;
                }
            };
            // Engine management callbacks resolve via AcruxBuiltins.GlobalEngineAction.
        }
    }

    /// <summary>
    /// Threaded-tab mode: every tab gets its own JavaScriptEngine so background
    /// tabs keep running independently on their worker threads.
    /// </summary>
    private JavaScriptEngine GetOrCreateTabEngine(int tabIndex)
    {
        if (!_threadedTabs || tabIndex < 0)
            return _jsEngine;

        var state = _tabStates.GetOrAdd(tabIndex, _ => new TabState());
        if (state.Engine == null)
        {
            state.Engine = new JavaScriptEngine(tabIndex);
            WireTabEngine(state.Engine, tabIndex);
        }
        return state.Engine;
    }

    /// <summary>Drop all UI interaction state bound to the previous tab's document.</summary>
    private void ClearTabInteractionState()
    {
        _focusedElement = null;
        _hasSelection = false;
        _isSelecting = false;
        _hoveredElement = null;
        _activeSelect = null;
        _pressedButton = null;
        _selectOptionRects.Clear();
        _selectHoverIndex = -1;
        _inputCursorPos = 0;
        _inputSelStart = -1;
        _inputScrollOffset = 0;
        _textareaUserScroll = false;
        _textareaScrollY = 0;
    }

    /// <summary>
    /// Tab switch in threaded mode: hand the departing tab to its worker thread
    /// and adopt the target tab's warm display list — no re-parse, no full
    /// re-layout, no tile-cache-wide invalidation of page content beyond raster.
    /// </summary>
    private void OnTabChangedThreaded(string url)
    {
        int to = _chrome.ActiveTabIndex;
        int from = _lastActiveTabIndex;

        if (from == to)
        {
            _lastActiveTabIndex = to;
            return;
        }

        // 1. Departing tab → its worker thread (JS keeps running in background).
        if (from >= 0 && from != to && _currentLoad != null && !ReferenceEquals(_jsEngine, _parkEngine))
        {
            var st = _tabStates.GetOrAdd(from, _ => new TabState());
            st.Html = _currentHtml;
            st.LoadResult = _currentLoad;
            st.Engine = _jsEngine;
            st.BaseUrl = _currentBaseUrl;
            st.ScrollX = _scroll.ScrollX;
            st.ScrollY = _scroll.ScrollY;
            st.DomNodeCount = CountDomNodes(_currentLoad.Document);
            st.LayoutBoxCount = CountLayoutBoxes(_currentLoad.Document);

            var ownedDl = _displayList;
            _displayList = new DisplayList(); // UI releases the handed-off list
            _processManager.HandOffToWorker(from, new TabOwnership
            {
                LoadResult = _currentLoad,
                Engine = _jsEngine,
                Html = _currentHtml,
                BaseUrl = _currentBaseUrl,
                DisplayList = ownedDl,
                ScrollX = _scroll.ScrollX,
                ScrollY = _scroll.ScrollY
            });

            _currentLoad = null;
            _jsEngine = _parkEngine ??= new JavaScriptEngine(NullJsEngineAdapter.Instance);
            _scrollInteraction.SetDocument(null);
            SetDevToolsDocument(null, "");
        }

        ClearTabInteractionState();
        _lastActiveTabIndex = to;

        // 2. Target tab: adopt its warm state, or navigate it if never loaded.
        if (_tabStates.TryGetValue(to, out var saved) && saved.LoadResult != null)
        {
            _adoptionPending = true;
            _pendingAdoptTab = to;
            int gen = ++_adoptionGen;
            _currentHtml = saved.Html;

            // Instant paint from the worker's published snapshot (immutable once
            // published; the worker swaps in fresh lists, never mutates these).
            var snap = _processManager.GetDisplayList(to);
            if (snap != null && snap.Count > 0)
            {
                _displayList = snap;
                _scroll.ScrollTo(saved.ScrollX, saved.ScrollY);
            }
            _input.NeedsRedraw = true;

            _processManager.RequestReturnToUi(to, (own, dl) => AdoptTab(to, own, dl, gen));
            return;
        }

        // Never-loaded tab: cancel any pending adoption and navigate.
        _adoptionPending = false;
        _pendingAdoptTab = -1;
        _isNavigating = false;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://"))
        {
            NavigateToHttp(url);
        }
        else if (url == "acrux://newtab" || url == "acrux://local")
        {
            _currentHtml = DocumentManager.DefaultHtml;
            LoadAndRenderHtml(_currentHtml);
        }
        else if (url == "acrux://js-test")
        {
            _currentHtml = DocumentManager.JsTestHtml;
            LoadAndRenderHtml(_currentHtml);
        }
        else if (url == "acrux://element-test")
        {
            _currentHtml = DocumentManager.ElementTestHtml;
            LoadAndRenderHtml(_currentHtml);
        }
        else if (url == "acrux://debug")
        {
            _currentHtml = DocumentManager.DebugHtml;
            LoadAndRenderHtml(_currentHtml);
        }
        else if (url == "acrux://js")
        {
            _currentHtml = DocumentManager.JsEngineHtml;
            LoadAndRenderHtml(_currentHtml);
        }
    }

    /// <summary>
    /// Runs on the UI thread (via the event loop) once the worker confirmed it
    /// stopped touching the tab. If the user already switched again, ownership
    /// is simply re-handed to the worker.
    /// </summary>
    private void AdoptTab(int tabIndex, TabOwnership? own, DisplayList? dl, int gen)
    {
        // A newer switch/navigation superseded this adoption — hand ownership
        // back to the worker (if we even got it) so the tab stays warm.
        if (gen != _adoptionGen)
        {
            if (own != null)
                _processManager.HandOffToWorker(tabIndex, own);
            return;
        }

        if (own != null && _chrome.ActiveTabIndex != tabIndex)
        {
            _processManager.HandOffToWorker(tabIndex, own);
            if (_pendingAdoptTab == tabIndex)
            {
                _pendingAdoptTab = -1;
                _adoptionPending = false;
            }
            return;
        }

        if (!_tabStates.TryGetValue(tabIndex, out var state))
        {
            _adoptionPending = false;
            _pendingAdoptTab = -1;
            return;
        }

        if (own != null)
        {
            state.Html = own.Html;
            state.LoadResult = own.LoadResult;
            state.Engine = own.Engine;
            state.BaseUrl = own.BaseUrl;
            state.ScrollX = own.ScrollX;
            state.ScrollY = own.ScrollY;
        }

        if (state.LoadResult == null)
        {
            _adoptionPending = false;
            _pendingAdoptTab = -1;
            return;
        }

        _adoptionPending = false;
        _pendingAdoptTab = -1;

        _currentHtml = state.Html;
        _currentBaseUrl = state.BaseUrl;
        _currentLoad = state.LoadResult;
        _jsEngine = state.Engine ?? GetOrCreateTabEngine(tabIndex);
        if (dl != null && dl.Count > 0)
            _displayList = dl;

        // Scroll extents come from the worker's latest layout boxes.
        var bodyBox = _currentLoad.Document.Body?.LayoutBox;
        float contentW = bodyBox?.BorderBox.Width ?? Math.Max(100, _lastWindowWidth);
        float contentH = bodyBox?.BorderBox.Height ?? 0;
        float viewportH = Math.Max(100, _lastWindowHeight - _contentOffset - _chrome.GetStatusBarHeight());
        _scroll.UpdateScroll(contentW, contentH, Math.Max(100, _lastWindowWidth), viewportH);
        _scroll.ScrollTo(state.ScrollX, state.ScrollY);

        // Degenerate case: nothing warm to show — fall back to one full rebuild.
        if (_displayList.Count == 0)
        {
            _pendingRelayout = true;
            _lastLayoutWidth = 0;
        }

        _scrollInteraction.SetDocument(_currentLoad.Document);
        SetDevToolsDocument(_currentLoad.Document, _currentHtml);

        // Content changed: the raster tiles must be re-generated from the
        // adopted display list (cheap raster work, no layout/paint-walk).
        _skiaRenderer.InvalidatePageCache();
        _input.NeedsRedraw = true;

        if (state.PendingNavigate != null)
        {
            var pending = state.PendingNavigate;
            state.PendingNavigate = null;
            if (_chrome.ActiveTabIndex == tabIndex)
                _chrome.NavigateToUrl(pending);
        }
    }

    // ==================== Multi-process tabs (TabMode = "process") ====================

    private RemoteTabView? ActiveRemote() =>
        _remoteTabs.TryGetValue(_chrome.ActiveTabIndex, out var v) ? v : null;

    // DevTools/find over the pipe: the panel talks to one IDevToolsChannel, rebuilt when
    // the active tab's host process changes (launch, relaunch after a crash, tab switch).
    private RemoteTabProcess? _dtChannelProc;
    private RemoteDevToolsChannel? _remoteDtChannel;

    private void SyncRemoteDevTools()
    {
        var proc = ActiveRemote()?.Proc;
        if (ReferenceEquals(proc, _dtChannelProc)) return;   // per-frame cheap identity check
        _dtChannelProc = proc;
        _remoteDtChannel = proc == null
            ? null
            : new RemoteDevToolsChannel(proc, act => _eventLoop.PostTask(act));
        _devTools.SetInspection(_remoteDtChannel, "");
    }

    /// <summary>Point DevTools at the page the shell itself owns (single/threaded modes).
    /// The in-process channel answers the same DTOs the page host streams, so the panel
    /// code is identical in both modes; null document keeps the console's engine alive
    /// while the Elements tab shows "(no document loaded)" exactly as before.</summary>
    private void SetDevToolsDocument(Core.Dom.Document? doc, string htmlSource)
    {
        Acrux.PageContract.IDevToolsChannel? channel = _processTabs
            ? _remoteDtChannel
            : new Acrux.PageHost.Inspection.InProcessDevToolsChannel(doc, _jsEngine, _currentLoad?.StyleComputer);
        _devTools.SetInspection(channel, htmlSource);
    }

    private RemoteTabView EnsureRemoteTab(int tabIndex, string url)
    {
        return _remoteTabs.GetOrAdd(tabIndex, idx =>
        {
            var view = new RemoteTabView(new RemoteTabProcess(idx, url, _dpiScale, _renderingSettings.ResolutionScale));
            view.Proc.OnFrameArrived += () => _eventLoop.PostTask(() => _input.NeedsRedraw = true);
            // The child connects asynchronously: a startup navigation queued while
            // the pipe was down only goes out from PumpRemoteTabs, which runs on a
            // redraw pass. Wake one on Ready or PendingInitial could stall forever.
            view.Proc.OnSignal += (type, _, _) =>
            {
                if (type == TabMsg.Ready) _eventLoop.PostTask(() => _input.NeedsRedraw = true);
            };
            view.Proc.OnSignal += (type, text, extra) => _eventLoop.PostTask(() => HandleRemoteSignal(idx, type, text, extra));
            view.Proc.OnScrollChanged += (x, y) => _eventLoop.PostTask(() =>
            {
                // Child-initiated scroll (window.scrollTo / anchors): mirror it
                // onto the parent scrollbar without triggering a send-back.
                if (_remoteTabs.TryGetValue(idx, out var v))
                {
                    v.SentScrollX = x; v.SentScrollY = y;
                }
                if (_chrome.ActiveTabIndex == idx)
                {
                    _scroll.ScrollTo(x, y);
                    _input.NeedsRedraw = true;
                }
            });
            view.Proc.OnWheelResult += consumed => _eventLoop.PostTask(() => FlushPendingWheel(consumed));
            view.Proc.OnDialogRequest += (id, message, type) =>
                _eventLoop.PostTask(() => ShowRemoteDialog(idx, id, message, type));
            return view;
        });
    }

    /// <summary>Page-level scroll from wheel, mirroring InputHandler's axis rules.</summary>
    private void ApplyRemotePageScroll(double dx, double dy)
    {
        if (_input.IsShiftDown) _scroll.ScrollBy((float)dy, 0);
        else if (Math.Abs(dx) > 0) _scroll.ScrollBy((float)dx, 0);
        else _scroll.ScrollBy((float)dy);
        _input.NeedsRedraw = true;
    }

    private void FlushPendingWheel(bool consumed)
    {
        if (!_wheelAwaiting) return;
        _wheelAwaiting = false;
        double px = _pendingWheelX, py = _pendingWheelY;
        _pendingWheelX = _pendingWheelY = 0;
        if (consumed && (px != 0 || py != 0))
        {
            // An element scroller claimed the wheel: undo the speculative page
            // scroll; the child's own element offset arrives with its next frame.
            if (_input.IsShiftDown) _scroll.ScrollBy((float)-py, 0);
            else if (Math.Abs(px) > 0) _scroll.ScrollBy((float)-px, 0);
            else _scroll.ScrollBy((float)-py);
            _input.NeedsRedraw = true;
        }
    }

    private static string SpecialPageHtml(string url) => url switch
    {
        "acrux://js-test" => DocumentManager.JsTestHtml,
        "acrux://element-test" => DocumentManager.ElementTestHtml,
        "acrux://debug" => DocumentManager.DebugHtml,
        "acrux://js" => DocumentManager.JsEngineHtml,
        "acrux://test-css" => DocumentManager.TestCssFeatureHtml,
        _ => DocumentManager.DefaultHtml,
    };

    private void ProcessNavigateTo(int tab, string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        var view = EnsureRemoteTab(tab, url);
        view.PendingInitial = url;
        view.InitialSent = false;
        view.LastUrl = url;
        _chrome.SetTabUrl(tab, url);
        _input.NeedsRedraw = true;
    }

    private void HandleRemoteSignal(int tab, TabMsg type, string text, string extra)
    {
        switch (type)
        {
            case TabMsg.Title:
                _chrome.SetTabTitle(tab, text);
                _input.NeedsRedraw = true;
                break;
            case TabMsg.UrlChanged:
                if (text.StartsWith("acrux://", StringComparison.Ordinal))
                {
                    // Internal pages are parent-owned: push the mapped content back —
                    // but only for a genuine navigation request. ApplyHtml finishes by
                    // echoing the loaded URL, and re-navigating on that echo would loop
                    // the tab through full reloads forever.
                    if (!_remoteTabs.TryGetValue(tab, out var rv) ||
                        !string.Equals(rv.LastUrl, text, StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessNavigateTo(tab, text);
                    }
                }
                else
                {
                    _chrome.SetTabUrl(tab, text);
                    if (_remoteTabs.TryGetValue(tab, out var rv2)) rv2.LastUrl = text;
                    if (_chrome.ActiveTabIndex == tab) _input.NeedsRedraw = true;
                }
                break;
        }
    }

    private void OnTabChangedProcess(string url)
    {
        int to = _chrome.ActiveTabIndex;
        int from = _lastActiveTabIndex;
        _lastActiveTabIndex = to;
        ClearTabInteractionState();

        var view = EnsureRemoteTab(to, url);
        // A tab that crashed while it was in the background comes back now that it is seen.
        if (view.Proc.IsDead && view.NeedsRelaunch && Environment.TickCount64 >= view.RelaunchAtTick)
        {
            RelaunchRemoteTab(to, view.LastUrl, view.Restart);
            return;
        }
        if (!view.HasFrame && !view.InitialSent && view.PendingInitial == null)
            ProcessNavigateTo(to, string.IsNullOrEmpty(url) ? "acrux://newtab" : url);

        view.Proc.SetActive(true);
        if (from >= 0 && from != to && _remoteTabs.TryGetValue(from, out var old))
            old.Proc.SetActive(false);

        _scroll.ScrollTo(view.SentScrollX, view.SentScrollY);
        // A modal belongs to its tab: park the one on screen if we are leaving it, and
        // surface whatever the new tab was blocked on.
        if (_remoteDialogTab >= 0 && _remoteDialogTab != to) ParkRemoteDialog();
        PresentRemoteDialog();
        _input.NeedsRedraw = true;
    }

    /// <summary>Per-frame pump: deferred initial navigations, scroll sync, loading state.</summary>
    private void PumpRemoteTabs(int windowWidth, float contentViewportHeight)
    {
        // DevTools follows the active host without a round trip: swap the inspection
        // channel when a new child takes over the tab, keep it while one is running.
        SyncRemoteDevTools();

        foreach (var kv in _remoteTabs)
        {
            var v = kv.Value;

            if (v.Proc.IsDead)
            {
                // Classify each death exactly once; the budget then decides. Distinguishing
                // who ended the host matters: a watchdog kill is our doing, a page fault is
                // the page's, and a clean close is neither.
                if (!v.DeathHandled && v.InitialSent)
                {
                    v.DeathHandled = true;
                    DropRemoteDialogs(kv.Key);
                    var verdict = v.Restart.OnDeath(v.Proc.DeathKind, Environment.TickCount64);
                    if (verdict.Exhausted)
                    {
                        v.Crashed = true;
                        v.CrashDetail = v.Proc.DeathDetail;
                        Console.WriteLine($"[RemoteTab {kv.Key}] restart budget spent; showing crash card");
                        _input.NeedsRedraw = true;
                    }
                    else
                    {
                        v.NeedsRelaunch = verdict.ShouldRestart;
                        v.RelaunchAtTick = Environment.TickCount64 + verdict.DelayMs;
                        Console.WriteLine($"[RemoteTab {kv.Key}] {v.Proc.DeathKind}; " +
                            $"relaunch in {verdict.DelayMs}ms ({v.Restart.FaultCount} fault(s) in window)");
                    }
                }

                // A background tab's crash is nobody's emergency. Relaunching it eagerly would
                // spend a process and its whole working set on a page the user is not looking at,
                // so it waits until the tab is shown again.
                if (v.NeedsRelaunch && kv.Key == _chrome.ActiveTabIndex &&
                    Environment.TickCount64 >= v.RelaunchAtTick)
                    RelaunchRemoteTab(kv.Key, v.LastUrl, carry: v.Restart);
                continue;
            }

            if (v.Proc.IsDead || v.InitialSent) continue;
            if (v.PendingInitial != null && v.Proc.IsConnected)
            {
                var url = v.PendingInitial;
                v.PendingInitial = null;
                v.InitialSent = true;
                if (url.StartsWith("acrux://", StringComparison.Ordinal))
                    v.Proc.NavigateHtml(SpecialPageHtml(url), url);
                else
                    v.Proc.Navigate(url);
            }
        }

        var active = ActiveRemote();
        if (active == null) return;

        // IME follows the child without a round trip: the target when its focus moved,
        // the candidate window when the newest frame published a new caret. All reads
        // hit the shell's cached frame metadata.
        bool imeEditable = active.Proc.TryGetImeState(out _, out _, out _, out bool imeFocus, out bool imePassword)
            && imeFocus && !imePassword;
        if (imeEditable != _remoteImeEditable)
        {
            _remoteImeEditable = imeEditable;
            UpdateImeTarget();
        }
        if (imeEditable)
            _window.UpdateImeCompositionWindow();

        // Wheel arbitration safety net: if the child never answers, scroll the page.
        if (_wheelAwaiting && Environment.TickCount64 - _wheelSentTick > 80)
            FlushPendingWheel(consumed: false);

        if (Math.Abs(_scroll.ScrollX - active.SentScrollX) > 0.5f ||
            Math.Abs(_scroll.ScrollY - active.SentScrollY) > 0.5f)
        {
            active.SentScrollX = _scroll.ScrollX;
            active.SentScrollY = _scroll.ScrollY;
            active.Proc.ScrollTo(_scroll.ScrollX, _scroll.ScrollY);
            _input.NeedsRedraw = true;
        }

        if (active.HasFrame && active.ContentH > 0 &&
            (Math.Abs(active.ContentW - _lastRemoteExtentW) > 1f || Math.Abs(active.ContentH - _lastRemoteExtentH) > 1f))
        {
            // Only re-derive scroll extents when the page size actually moved;
            // calling UpdateScroll every frame fights smooth scrolling.
            _lastRemoteExtentW = active.ContentW;
            _lastRemoteExtentH = active.ContentH;
            _scroll.UpdateScroll(active.ContentW, active.ContentH, windowWidth, contentViewportHeight);
        }
        _chrome.SetLoadingState(active.Proc.Loading);

        // Follow the tab we are asked to keep an eye on, and let a recovered page show a
        // fresh bubble the next time it wedges.
        _watchdogTarget = _chrome.ActiveTabIndex;
        if (active.Proc.Responsiveness == PageResponsiveness.Healthy) _bubbleMuted = false;
    }

    /// <summary>Starts the liveness probe. Only page-host tabs can be unresponsive —
    /// in-process tabs wedge the shell itself, which is the thing this whole split removes.</summary>
    private void StartWatchdog()
    {
        if (!_processTabs) return;
        _watchdog = new System.Threading.Timer(_ =>
        {
            // A pending relaunch has to be woken by the timer: once the host is dead nothing
            // comes back over its pipe, so Responsiveness stops changing and the frame loop
            // would never run again to fire the delayed relaunch. Only for the tab on screen
            // and only once its backoff is due — otherwise a crashed background tab would
            // force repaints forever.
            long nowTick = Environment.TickCount64;
            int target = _watchdogTarget;
            bool wakeForRelaunch = target >= 0 &&
                _remoteTabs.TryGetValue(target, out var pending) &&
                pending.NeedsRelaunch && nowTick >= pending.RelaunchAtTick;
            if (wakeForRelaunch)
            {
                _input.NeedsRedraw = true;
                return;
            }

            int idx = target;
            if (idx < 0 || !_remoteTabs.TryGetValue(idx, out var view)) return;
            var now = view.Proc.Responsiveness;
            // Wake the frame loop only on a transition: an idle, healthy browser must keep
            // painting nothing at all.
            if (now != _reportedResp)
            {
                _reportedResp = now;
                _input.NeedsRedraw = true;
            }
        }, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    private void HandlePageBubbleClick(float x, float y)
    {
        var view = ActiveRemote();
        if (view == null) return;
        int tab = _chrome.ActiveTabIndex;

        if (_bubbleWaitRect.Contains(x, y))
        {
            _bubbleMuted = true;
            return;
        }
        if (_bubbleReloadRect.Contains(x, y))
        {
            ReloadRemoteTab(tab);
            return;
        }
        if (_bubbleKillRect.Contains(x, y))
            _chrome.CloseTab(tab);
    }

    /// <summary>Tear a host down and load its page again in a fresh one.</summary>
    private void RelaunchRemoteTab(int tabIndex, string url, RestartPolicy carry)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (_remoteTabs.TryGetValue(tabIndex, out var old))
        {
            _remoteTabs.TryRemove(tabIndex, out _);
            try { old.Dispose(); } catch { }
        }
        var nv = EnsureRemoteTab(tabIndex, url);
        nv.Restart = carry;
        nv.LastUrl = url;
        nv.PendingInitial = url;
        nv.InitialSent = false;
        _input.NeedsRedraw = true;
    }

    /// <summary>Tear a wedged host down and load its page again in a new one. A user asking
    /// for this resets the crash budget — they are not watching an automated restart loop.</summary>
    private void ReloadRemoteTab(int tabIndex)
    {
        if (!_remoteTabs.TryGetValue(tabIndex, out var view)) return;
        string url = !string.IsNullOrEmpty(view.LastUrl) ? view.LastUrl : view.PendingInitial ?? "";
        var policy = view.Restart;
        policy.Reset();
        // A wedged host does not answer a polite Close; killing it first keeps Dispose from
        // spending its exit-wait on the UI thread.
        if (!view.Proc.IsDead) view.Proc.KillForHang();
        _bubbleMuted = false;
        _reportedResp = PageResponsiveness.Healthy;
        RelaunchRemoteTab(tabIndex, url, policy);
    }

    /// <summary>
    /// The shell's answer to a page host that stopped beating: a small card over the page
    /// area offering wait / reload / close. It is chrome, not page content, so it draws and
    /// takes clicks while the page host is completely stuck.
    /// </summary>
    private void RenderUnresponsiveBubble(SKCanvas canvas, float windowWidth, float contentOffset)
    {
        float w = Math.Min(420, windowWidth - 32), h = 96;
        float x = (windowWidth - w) / 2;
        float y = contentOffset + 16;

        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 40), Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(x + 2, y + 3, w, h, 8, 8, shadow);
        using var bg = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(x, y, w, h, 8, 8, bg);
        using var border = new SKPaint
        {
            Color = new SKColor(210, 210, 210),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawRoundRect(x, y, w, h, 8, 8, border);

        using var titleFont = FontHelper.CreateFont(14);
        using var titlePaint = FontHelper.CreatePaint(14);
        titlePaint.Color = SKColor.Parse("#333333");
        canvas.DrawText("页面没有响应", x + 16, y + 26, SKTextAlign.Left, titleFont, titlePaint);

        using var msgFont = FontHelper.CreateFont(12);
        using var msgPaint = FontHelper.CreatePaint(12);
        msgPaint.Color = SKColor.Parse("#666666");
        canvas.DrawText("该标签的程序仍在运行，但已停止响应输入。", x + 16, y + 45, SKTextAlign.Left, msgFont, msgPaint);

        const float btnW = 92, btnH = 28, gap = 8;
        float by = y + h - btnH - 14;
        float bx = x + w - btnW * 3 - gap * 2 - 16;

        _bubbleWaitRect = new SKRect(bx, by, bx + btnW, by + btnH);
        _bubbleReloadRect = new SKRect(bx + btnW + gap, by, bx + btnW * 2 + gap, by + btnH);
        _bubbleKillRect = new SKRect(bx + (btnW + gap) * 2, by, bx + btnW * 3 + gap * 2, by + btnH);

        using var font = FontHelper.CreateFont(13);
        DrawBubbleButton(canvas, font, _bubbleWaitRect, "等待", new SKColor(240, 240, 240), SKColor.Parse("#333333"));
        DrawBubbleButton(canvas, font, _bubbleReloadRect, "重新加载", new SKColor(225, 236, 250), SKColor.Parse("#1a5fb4"));
        DrawBubbleButton(canvas, font, _bubbleKillRect, "关闭页面", new SKColor(250, 228, 228), SKColor.Parse("#a51d1d"));
    }

    /// <summary>
    /// Shown once a tab has spent its restart budget: the shell stops relaunching a page that
    /// only crashes again, says so, and leaves one button that refills the budget.
    /// </summary>
    private void RenderCrashCard(SKCanvas canvas, float windowWidth, float contentOffset, string detail)
    {
        float w = Math.Min(460, windowWidth - 48), h = 132;
        float x = (windowWidth - w) / 2;
        float y = contentOffset + 48;

        using var bg = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(x, y, w, h, 8, 8, bg);
        using var border = new SKPaint
        {
            Color = new SKColor(210, 210, 210),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawRoundRect(x, y, w, h, 8, 8, border);

        using var titleFont = FontHelper.CreateFont(15);
        using var titlePaint = FontHelper.CreatePaint(15);
        titlePaint.Color = SKColor.Parse("#333333");
        canvas.DrawText("此标签的页面已停止工作", x + 16, y + 28, SKTextAlign.Left, titleFont, titlePaint);

        using var msgFont = FontHelper.CreateFont(12);
        using var msgPaint = FontHelper.CreatePaint(12);
        msgPaint.Color = SKColor.Parse("#777777");
        canvas.DrawText("它连续崩溃太多次，浏览器已停止自动重新加载。", x + 16, y + 50, SKTextAlign.Left, msgFont, msgPaint);
        if (!string.IsNullOrEmpty(detail))
            canvas.DrawText(Truncate(detail, msgFont, w - 32), x + 16, y + 68, SKTextAlign.Left, msgFont, msgPaint);

        const float btnW = 104, btnH = 30;
        _crashReloadRect = new SKRect(x + w - btnW - 16, y + h - btnH - 14, x + w - 16, y + h - 14);
        using var font = FontHelper.CreateFont(13);
        DrawBubbleButton(canvas, font, _crashReloadRect, "重新加载", new SKColor(225, 236, 250), SKColor.Parse("#1a5fb4"));
    }

    private static string Truncate(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (font.MeasureText(text[..mid] + "…") <= maxWidth) lo = mid; else hi = mid - 1;
        }
        return text[..lo] + "…";
    }

    private static void DrawBubbleButton(SKCanvas canvas, SKFont font, SKRect rect, string text,
        SKColor fill, SKColor textColor)
    {
        using var bg = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(rect, 6, 6, bg);
        using var paint = FontHelper.CreatePaint(13);
        paint.Color = textColor;
        canvas.DrawText(text, rect.MidX, rect.Top + rect.Height * 0.68f, SKTextAlign.Center, font, paint);
    }

    private void RenderRemotePage(int windowWidth, float contentViewportHeight)
    {
        var canvas = _skiaRenderer.Canvas;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, _contentOffset, windowWidth, _contentOffset + contentViewportHeight));
        canvas.Translate(0, _contentOffset);

        var view = ActiveRemote();
        if (view != null)
        {
            view.UpdateFromFrame();
            var bmp = view.Bitmap;
            if (bmp != null)
            {
                float inv = 1f / Math.Max(0.01f, _dpiScale * _renderingSettings.ResolutionScale);
                float lw = bmp.Width * inv, lh = bmp.Height * inv;
                // Offset the (already-scrolled) frame by the delta between what the
                // child baked and the parent's current scroll: instant scroll feedback
                // while the child re-renders at the new offset.
                float dx = view.FrameScrollX - _scroll.ScrollX;
                float dy = view.FrameScrollY - _scroll.ScrollY;
                // Freshly exposed strips take the PAGE background color (like a
                // compositor outrunning its rasterizer) — never smeared pixels.
                if (Math.Abs(dy) > 0.5f || Math.Abs(dx) > 0.5f)
                {
                    using var bgPaint = new SKPaint { Color = view.PageBg };
                    canvas.DrawRect(new SKRect(0, 0, windowWidth, contentViewportHeight), bgPaint);
                }
                canvas.DrawBitmap(bmp, new SKRect(dx, dy, dx + lw, dy + lh));
            }
        }
        canvas.Restore();
    }

    private void RunPageScripts(string? baseUrl)
    {
        if (_currentLoad == null) return;

        

        var scriptElements = new List<Element>();
        if (_currentLoad.Document.DocumentElement != null)
        {
            var queue = new Queue<Element>();
            queue.Enqueue(_currentLoad.Document.DocumentElement);
            while (queue.Count > 0)
            {
                var el = queue.Dequeue();
                if (el.TagName.ToLowerInvariant() == "script")
                    scriptElements.Add(el);
                foreach (var child in el.Children)
                    if (child is Element childEl)
                        queue.Enqueue(childEl);
            }
        }

        var integration = _jsEngine.IntegrationService;

        foreach (var scriptEl in scriptElements)
        {
            var type = scriptEl.GetAttribute("type");
            if (!string.IsNullOrEmpty(type) && type != "text/javascript" && type != "application/javascript" && type != "module")
                continue;

            var isAsync = scriptEl.HasAttribute("async");
            var isDefer = scriptEl.HasAttribute("defer");
            var isModule = type == "module";
            var src = scriptEl.GetAttribute("src");

            ScriptType scriptType;
            if (isModule) scriptType = ScriptType.Module;
            else if (isAsync) scriptType = ScriptType.Async;
            else if (isDefer) scriptType = ScriptType.Defer;
            else scriptType = string.IsNullOrEmpty(src) ? ScriptType.Inline : ScriptType.External;

            if (!string.IsNullOrEmpty(src))
            {
                var absoluteUrl = ResolveUrl(src, baseUrl);
                if (absoluteUrl == null)
                {
                    Console.WriteLine($"[JS] Invalid script URL: {src}");
                    continue;
                }

                if (integration != null)
                {
                    integration.ScriptQueue.EnqueueExternalScript(absoluteUrl, scriptType, absoluteUrl);
                }
                else
                {
                    ExecuteExternalScriptFallback(absoluteUrl, scriptType);
                }
            }
            else
            {
                var code = scriptEl.TextContent;
                if (!string.IsNullOrWhiteSpace(code))
                {
                    if (integration != null)
                    {
                        integration.ExecuteScript(code, null, scriptType);
                    }
                    else
                    {
                        _jsEngine.Execute(code);
                    }
                }
            }
        }

        if (integration != null)
        {
            integration.FireDOMContentLoaded();
        }
    }

    private void ExecuteExternalScriptFallback(string url, ScriptType type)
    {
        try
        {
            var resp = _httpFetcher.FetchAsync(new ResourceRequest
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
            Console.WriteLine($"[JS] Failed to load script '{url}': {ex.Message}");
        }
    }

    private static string? ResolveUrl(string url, string? baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("data:") || url.StartsWith("blob:"))
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

    private void NavigateToHttp(string url)
    {
        if (_processTabs)
        {
            ProcessNavigateTo(_chrome.ActiveTabIndex, url);
            return;
        }
        _isNavigating = false; // allow new navigation to interrupt previous one
        int seq = Interlocked.Increment(ref _navigationSeq);
        int tabIdx = _chrome.ActiveTabIndex;
        _navigatingTabIndex = tabIdx;
        _isNavigating = true;
        _chrome.SetLoadingState(true);
        _input.NeedsRedraw = true;

        Task.Run(async () =>
        {
            try
            {
                var request = new ResourceRequest
                {
                    Url = url,
                    Kind = ResourceKind.Document,
                    Priority = ResourcePriority.VeryHigh,
                    Timeout = TimeSpan.FromSeconds(30),
                    Headers = new Dictionary<string, string>
                    {
                        ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36",
                        ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                        ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8",
                    },
                };
                var response = await _httpFetcher.FetchAsync(request);

                if (response.StatusCode < 200 || response.StatusCode >= 300)
                    throw new HttpRequestException($"HTTP {response.StatusCode}");

                var webHtml = System.Text.Encoding.UTF8.GetString(response.Body);
                var finalUrl = response.FinalUrl ?? url;

                _eventLoop.PostTask(() =>
                {
                    if (seq != _navigationSeq) return; // stale navigation
                    // 如果导航期间切换了标签页，保存到对应标签页的状态中
                    if (_chrome.ActiveTabIndex != tabIdx)
                    {
                        _tabStates[tabIdx] = new TabState { Html = webHtml };
                        _chrome.UpdateUrl(finalUrl);
                        return;
                    }
                    var savedUrl = finalUrl;
                    _chrome.UpdateUrl(savedUrl);
                    LoadAndRenderHtml(webHtml, savedUrl);
                });
            }
            catch (TaskCanceledException)
            {
                _eventLoop.PostTask(() =>
                {
                    if (seq != _navigationSeq) return;
                    if (_chrome.ActiveTabIndex != tabIdx) return;
                    ShowErrorPage(url, "Request timed out");
                });
            }
            catch (Exception ex)
            {
                _eventLoop.PostTask(() =>
                {
                    if (seq != _navigationSeq) return;
                    if (_chrome.ActiveTabIndex != tabIdx) return;
                    ShowErrorPage(url, ex.Message);
                });
            }
        });
    }

    private void NavigateToFile(string url)
    {
        if (_processTabs)
        {
            ProcessNavigateTo(_chrome.ActiveTabIndex, url);
            return;
        }
        if (_isNavigating)
            _isNavigating = false; // 允许新导航中断之前的请求

        _isNavigating = true;
        _chrome.SetLoadingState(true);
        _input.NeedsRedraw = true;

        Task.Run(async () =>
        {
            string filePath;
            try
            {
                // 将 file:///C:/path/file.html 转换为本地路径
                var uri = new Uri(url);
                filePath = uri.LocalPath; // Windows: "C:\\path\\file.html"
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");
            }
            catch (Exception ex)
            {
                _eventLoop.PostTask(() => ShowErrorPage(url, ex.Message));
                return;
            }

            try
            {
                string html = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                // 使用文件所在目录作为 baseUrl，用于解析相对路径的资源
                string baseDir = Path.GetDirectoryName(filePath)?.Replace('\\', '/');
                string baseUrl = baseDir != null ? $"file://{baseDir}/" : null;

                _eventLoop.PostTask(() => LoadAndRenderHtml(html, baseUrl));
            }
            catch (Exception ex)
            {
                _eventLoop.PostTask(() => ShowErrorPage(url, ex.Message));
            }
        });
    }

    private void LoadAndRenderHtml(string html, string? baseUrl = null)
    {
        if (_processTabs)
        {
            var view = EnsureRemoteTab(_chrome.ActiveTabIndex, baseUrl ?? "acrux://local");
            view.Proc.NavigateHtml(html, baseUrl ?? "acrux://local");
            return;
        }
        _currentHtml = html;
        _currentBaseUrl = baseUrl;
        _sharedImageCache.Clear();
        _sharedTypefaceCache.Clear();
        _hasSelection = false;
        _isSelecting = false;
        _hoveredElement = null;
        _activeSelect = null;
        _selectOptionRects.Clear();
        _selectHoverIndex = -1;

        // 显示加载进度条（仅当尚未加载时，避免覆盖 HTTP 加载的进度）
        if (!_chrome.IsLoading)
        {
            _chrome.SetLoadingState(true);
            _input.NeedsRedraw = true;
        }

        // Parse HTML on background thread
        // Capture current viewport and dpi so layout during load uses correct CSS pixel size
        var (pw_cap, ph_cap) = _window.GetClientSize();
        float viewportWidthCss_cap = pw_cap / _dpiScale;
        float viewportHeightCss_cap = ph_cap / _dpiScale;

        Task.Run(async () =>
        {
            DocumentManager.DocumentLoadResult? loadResult = null;
            try
            {
                var docManager = new DocumentManager();
                // Layout needs image intrinsic sizes during (not after) the load.
                PaintVisitor.InstallReplacedIntrinsicSizes(_sharedImageCache, baseUrl);
                loadResult = await docManager.LoadHtmlAsync(html, baseUrl, viewportWidthCss_cap, viewportHeightCss_cap, _dpiScale);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Load] Error loading HTML: {ex.Message}");
                var errorHtml = $@"<!DOCTYPE html>
<html><head><title>Error</title></head>
<body style='font-family: Arial; padding: 40px;'>
    <h1 style='color: #d32f2f;'>Rendering Error</h1>
    <p style='color: #666;'>Error: {ex.Message}</p>
</body></html>";
                try
                {
                    var docManager = new DocumentManager();
                    loadResult = await docManager.LoadHtmlAsync(errorHtml, baseUrl, viewportWidthCss_cap, viewportHeightCss_cap, _dpiScale);
                }
                catch { return; }
            }

            if (loadResult != null)
            {
                var capturedHtml = html;
                _eventLoop.PostTask(() => ApplyLoadedHtml(loadResult, capturedHtml));
            }
        });
    }

    private void ApplyLoadedHtml(DocumentManager.DocumentLoadResult loadResult, string html)
    {
        int tabIdx = _chrome.ActiveTabIndex;

        if (_threadedTabs && _adoptionPending && _pendingAdoptTab == tabIdx)
        {
            // User navigated while the worker still holds this tab — retry once
            // the in-flight adoption completes.
            _eventLoop.PostTask(() => ApplyLoadedHtml(loadResult, html));
            return;
        }

        // Dispose old document safely
        if (_currentLoad != null)
        {
            try { }
            catch (Exception ex) { Console.WriteLine($"[Dispose] Error: {ex.Message}"); }
        }

        // Release accumulated paint ops and blur cache on navigation
        PaintOpPool.Clear();

        _currentLoad = loadResult;
        _currentHtml = html;
        _scrollInteraction.SetDocument(_currentLoad.Document);

        // A new document gets a new document timeline: the animation clock
        // restarts at zero so the page's delays and negative delays are measured
        // from this navigation, not from the previous page's lifetime.
        _animations.Reset();
        _animationTimelineBaseMs = Acrux.Core.Dom.Animations.AnimationClock.WallClockMs;
        _animationsRunning = false;

        var (pw, ph) = _window.GetClientSize();
        int ww = (int)(pw / _dpiScale);
        int wh = (int)(ph / _dpiScale);

        if (_threadedTabs)
        {
            // Fresh navigation always targets the active tab's own engine.
            _adoptionGen++;
            _jsEngine = GetOrCreateTabEngine(tabIdx);
            _adoptionPending = false;
            _pendingAdoptTab = -1;
            if (_tabStates.TryGetValue(tabIdx, out var stNav)) stNav.PendingNavigate = null;
        }

        _jsEngine.LoadDocument(_currentLoad.Document);
        SetDevToolsDocument(_currentLoad.Document, html);
        // Events flow out of the engine through this forwarder, which is why the
        // sink is re-installed on every navigation: the script engine changes with
        // the tab, and a stale sink would deliver to a dead engine.
        _animations.EventSink = DispatchAnimationEvents;
        BuildDisplayList(ww, wh);
        _scroll.ScrollTo(0, 0);
        // P3-2: W3C paint-timing semantics — FP is the first rendered frame of
        // any kind; FCP is the first frame carrying real content (a non-empty
        // page display list). Both are recorded exactly once by the metrics API.
        _perfHub.Registry.Metrics.RecordFirstPaint();
        if (_displayList.Count > 0)
            _perfHub.Registry.Metrics.RecordFirstContentfulPaint();

        RunPageScripts(_currentBaseUrl);

        // Update layout debug report on every page load
        try
        {
            var devTool = new LayoutDevTool();
            var debugReport = devTool.GenerateReport(_currentLoad.Document, ww, wh);
            File.WriteAllText("layout_debug.txt", debugReport);
            Console.WriteLine($"[Debug] Report updated ({debugReport.Length} chars)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Debug] Failed to generate report: {ex.Message}");
        }

        _lastActiveTabIndex = tabIdx;
        _tabStates[tabIdx] = new TabState
        {
            Html = _currentHtml,
            LoadResult = _currentLoad,
            ScrollX = 0,
            ScrollY = 0,
            Engine = _threadedTabs ? _jsEngine : null,
            BaseUrl = _currentBaseUrl
        };

        // Update process manager for the active tab
        var activeProc = _processManager.GetProcess(tabIdx);
        if (activeProc != null)
        {
            activeProc.UpdateTitle(_currentLoad?.Document.Title ?? "");
            activeProc.UpdateUrl(_currentBaseUrl ?? "");
            int nDom = _currentLoad != null ? CountDomNodes(_currentLoad.Document) : 0;
            int nBox = _currentLoad != null ? CountLayoutBoxes(_currentLoad.Document) : 0;
            long memBytes = System.GC.GetTotalMemory(false);
            activeProc.UpdateContentMetrics(nDom, nBox, memBytes,
                _jsEngine.GetHeapSizeKB(), _jsEngine.TimerCount);
        }

        _isNavigating = false;
        _chrome.SetLoadingState(false);
        _input.NeedsRedraw = true;

        // Prompt GC to free old page's managed memory
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
    }

    private void ShowErrorPage(string url, string errorMessage)
    {
        var errorHtml = $@"<!DOCTYPE html>
<html><head><title>Error</title>
<style>
    body {{ font-family: 'Microsoft YaHei', Arial, sans-serif; padding: 40px; background: #f5f5f5; }}
    .error-container {{ max-width: 600px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
    h1 {{ color: #d32f2f; margin-top: 0; }}
    .url {{ color: #666; word-break: break-all; }}
    .error {{ color: #999; margin-top: 20px; }}
    .retry-btn {{ background: #1a73e8; color: white; border: none; padding: 10px 20px; border-radius: 4px; cursor: pointer; margin-top: 20px; }}
</style>
</head>
<body>
    <div class='error-container'>
        <h1>Unable to connect</h1>
        <p>Failed to load: <span class='url'>{url}</span></p>
        <p class='error'>Error: {errorMessage}</p>
        <button class='retry-btn' onclick='location.reload()'>Retry</button>
    </div>
</body></html>";
        _isNavigating = false;
        _chrome.SetLoadingState(false);
        _input.NeedsRedraw = true;
        LoadAndRenderHtml(errorHtml, url);
    }

    private void NavigateToSearch(string query)
    {
        var searchHtml = $@"<!DOCTYPE html>
<html><head><title>Search: {query}</title></head>
<body style='font-family: Arial; padding: 40px;'>
    <h1 style='color: #1a73e8;'>Search</h1>
    <p>Searching for: <strong>{query}</strong></p>
    <p style='color: #666;'>Search functionality requires network access.</p>
</body></html>";
        LoadAndRenderHtml(searchHtml);
    }

    private void BuildDisplayList(float windowWidth, float windowHeight, bool styleRecomputed = true)
    {
        if (_currentLoad == null) return;

        // Wrap the heavy work in the long-task observer so we get metrics
        // for free. When the hub is not initialised this is a no-op.
        if (_perfHub is { Enabled: true })
        {
            _perfHub.LongTasks.Observe("BuildDisplayList", TaskPriority.High, () =>
            {
                BuildDisplayListImpl(windowWidth, windowHeight, styleRecomputed);
            });
        }
        else
        {
            BuildDisplayListImpl(windowWidth, windowHeight, styleRecomputed);
        }
    }

    /// <summary>
    /// Sample every CSS animation and transition for this frame and write the
    /// results into the elements' computed styles.
    ///
    /// Must be called after style resolution and before layout. Returns true when
    /// something is animating, which the frame loop uses to keep repainting: a
    /// running animation is the only reason a page repaints with nothing else
    /// having changed.
    /// </summary>
    private bool AdvanceAnimations(bool styleRecomputed = true)
    {
        if (_currentLoad == null) return false;
        var root = _currentLoad.Document.DocumentElement ?? _currentLoad.Document.Body;
        if (root == null) return false;

        try
        {
            // Keep the timeline pinned to this document's lifetime. The engine's
            // timeline counts from the moment the engine was constructed, so a
            // page loaded a minute into the session would sample its animations at
            // t=60000ms - long past every one of them - and nothing would ever
            // move. The navigation in ApplyLoadedHtml calls Reset, which re-bases
            // the origin; assert it here too, since one missed reset is
            // indistinguishable from "animations do not work".
            double now = Acrux.Core.Dom.Animations.AnimationClock.WallClockMs;
            if (now - _animationTimelineBaseMs > 1000)
            {
                _animations.Timeline.SetCurrentTime(0);
                _animationTimelineBaseMs = now;
            }

            var result = _animations.Update(root, _currentLoad.StyleComputer?.CollectKeyframeRules(),
                styleRecomputed: styleRecomputed);
            _animationsRunning = result.HasActiveAnimations;
            if (_animTrace)
                Console.WriteLine($"[anim] t={_animations.Timeline.CurrentTimeMs:F1}ms " +
                                  $"kf={_currentLoad.StyleComputer?.CollectKeyframeRules().Count ?? -1} " +
                                  $"active={result.HasActiveAnimations} effects={_animations.ActiveEffectCount} " +
                                  $"animated={result.AnimatedElements}");
            return result.HasActiveAnimations;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[anim] update failed: {ex.Message}");
            _animationsRunning = false;
            return false;
        }
    }

    /// <summary>
    /// Deliver animation and transition events to script. The engine queues
    /// them so a listener that mutates the DOM cannot perturb the sampling that
    /// produced them; the bridge converts them into the objects listeners see.
    /// </summary>
    private void DispatchAnimationEvents(Acrux.Core.Dom.Element element, Acrux.Core.Dom.Event evt)
    {
        try
        {
            var engine = _jsEngine;
            if (engine?.Adapter == null) return;

            var host = engine.GetDispatchHost(element);
            if (evt is Acrux.Core.Dom.AnimationEvent anim)
            {
                engine.DispatchEvent(element, new ScriptEvent(anim.Type, host)
                {
                    bubbles = false,
                    cancelable = false,
                    animationName = anim.AnimationName,
                    elapsedTime = anim.ElapsedTime,
                    pseudoElement = anim.PseudoElement ?? "",
                    currentTime = anim.CurrentTime,
                });
            }
            else if (evt is Acrux.Core.Dom.TransitionEvent trans)
            {
                engine.DispatchEvent(element, new ScriptEvent(trans.Type, host)
                {
                    bubbles = false,
                    cancelable = false,
                    propertyName = trans.PropertyName,
                    elapsedTime = trans.ElapsedTime,
                    pseudoElement = trans.PseudoElement ?? "",
                    currentTime = trans.CurrentTime,
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[anim] event dispatch failed: {ex.Message}");
        }
    }

    private void BuildDisplayListImpl(float windowWidth, float windowHeight, bool styleRecomputed = true)
    {
        // "Update animations and send events" runs here, between style resolution
        // and layout, so animated values are in the styles layout reads. When style
        // resolution has just run, the engine compares the cascaded values to find
        // transition start conditions, which is why it needs that ordering.
        AdvanceAnimations(styleRecomputed);
        // Route layout through the incremental engine when available.
        // It consults LayoutCache + DirtyFlags and skips clean subtrees,
        // which is the main win on small JS-driven DOM updates and typing.
        if (_incrementalLayout is { } incLayout)
        {
            // When the viewport size changes we need a full re-layout. Bumping
            // the layout version invalidates the cache for all nodes that don't
            // already carry a dirty flag.
            DirtyState.BumpLayoutVersion(_currentLoad.Document.DocumentElement ?? _currentLoad.Document.Body!);
            incLayout.Layout(_currentLoad.Document, windowWidth, windowHeight, _dpiScale, 16f);

            // Surface incremental stats to the dev tools feed.
            _perfHub.Registry.Feed.Append("layout",
                $"skipped={incLayout.Stats.NodesSkipped} relaid={incLayout.Stats.NodesReLaid} " +
                $"hit={incLayout.Stats.CacheHits} miss={incLayout.Stats.CacheMisses} " +
                $"{incLayout.Stats.ElapsedMillis:F2}ms");
        }
        else
        {
            _layout.Layout(_currentLoad.Document, windowWidth, windowHeight, _dpiScale);
        }

        // Refresh the shared scroll state so the painter and the click->caret mapping
        // agree on the same viewport (boxes are fresh after the layout above).
        UpdateInputScrollOffset(_focusedElement);
        UpdateTextAreaScrollY();

        // Return old display list ops to pool before creating new one (fixes memory leak)
        _displayList.Clear();

        _cachedPaintVisitor = new PaintVisitor(_contentOffset, _sharedTypefaceCache, _sharedImageCache, _fontFamilies, _currentBaseUrl, windowWidth, windowHeight);
        _cachedPaintVisitor.PhysicalScale = _dpiScale * _renderingSettings.ResolutionScale;
        // P2-2b: viewport culling — only layers intersecting the visible page
        // rect (+300px bleed) emit paint ops. Refreshed on every rebuild.
        // With the tile compositor active the display list must cover the whole
        // page so any tile (visible or lazily filled below the viewport) finds
        // its ops; viewport culling would make off-screen tiles rasterize empty
        // and stay cached blank.
        if (!_skiaRenderer.TileCompositorActive)
        {
            float cullTop = _scroll.ScrollY - 300f;
            float cullLeft = _scroll.ScrollX - 300f;
            _cachedPaintVisitor.SetCullRect(new SKRect(
                cullLeft, cullTop,
                cullLeft + windowWidth + 600f,
                cullTop + windowHeight + _contentOffset + 600f));
        }
        _cachedPaintVisitor.SetFocusedElement(_focusedElement);
        _cachedPaintVisitor.SetSkipInputTextOverlay(true);
        _cachedPaintVisitor.SetPasswordRevealed(_passwordRevealed);
        _cachedPaintVisitor.SetInputScrollOffset(_inputScrollOffset);
        _cachedPaintVisitor.SetTextAreaScrollY(_textareaScrollY);
        _cachedPaintVisitor.SetTextAreaUserScroll(_textareaUserScroll);
        var (mx, my) = _input.GetMousePosition();
        _cachedPaintVisitor.SetMouseState(mx + _scroll.ScrollX, my - _contentOffset + _scroll.ScrollY,
            _input.IsMouseDown(), _pressedInputControl);
        _cachedPaintVisitor.SetPressedButton(_pressedButton);
        if (_activeSelect != null)
        {
            ComputeSelectDropdownGeometry();
            _cachedPaintVisitor.SetSelectDropdown(_activeSelect, _selectDropdownRect, _selectOptionRects, _selectHoverIndex);
        }
        else
        {
            _cachedPaintVisitor.SetSelectDropdown(null, default, null, -1);
        }
        if (_focusedElement != null && _focusedElement.IsTextEditable)
        {
            _cachedPaintVisitor.SetInputState(_inputCursorPos, _inputSelStart, _inputShowCursor,
                _inputImeComposing, _inputImeCompositionStr, _inputImeCursorPos);
        }
        if (_hasSelection && _selAnchor.Node != null && _selFocus.Node != null)
        {
            _cachedPaintVisitor.SetSelectionRange(_selAnchor.Node, _selAnchor.Offset, _selFocus.Node, _selFocus.Offset);
        }
        _cachedPaintVisitor.VisitDocumentStacking(_currentLoad.Document);
        _displayList = _cachedPaintVisitor.GetDisplayList();
        _displayList.SortByZIndex();
        _displayList.BuildSpatialGrid();

        // Only invalidate the focused input region when possible, full page otherwise.
        // This avoids destroying ALL tiles on every keystroke — only the tiles covering
        // the input element are re-rasterized.
        if (!_pendingRelayout && _focusedElement is { IsFormElement: true, LayoutBox: not null })
        {
            var box = _focusedElement.LayoutBox.PaddingBox;
            var invalidRect = new SKRect(box.Left, box.Top, box.Right + 2, box.Bottom + 2);
            _skiaRenderer.Invalidate(invalidRect);
        }
        else
        {
            _skiaRenderer.InvalidatePageCache();
            // A JS-driven rebuild drops every tile; leaving it to the deferred
            // rasterizer blanks the viewport mid-scroll for several frames.
            // Re-raster the visible region synchronously so the next presented
            // frame is already complete.
            if (_skiaRenderer.TileCompositorActive && !_window.IsInSizeMove)
                _skiaRenderer.PrerasterizePageRect(new SKRect(
                    _scroll.ScrollX, _scroll.ScrollY,
                    _scroll.ScrollX + windowWidth,
                    _scroll.ScrollY + windowHeight + _contentOffset));
        }
    }

    private void RenderFrame(double dt)
    {
        long rsMark = 0;
        if (_renderStats) rsMark = Stopwatch.GetTimestamp();

        // Drive the cooperative scheduler for the frame and observe memory pressure.
        RunPerfFrame(dt);

        _eventLoop.ProcessTasks();

        var jsInt = _jsEngine.IntegrationService;
        if (jsInt != null)
        {
            jsInt.ProcessTimers();
            jsInt.MicrotaskQueue.DrainMicrotasks();
            jsInt.IdentityMap.CleanupStaleEntries();
        }

        if (_jsEngine.HasTimers && jsInt == null)
        {
            if (_perfHub is { Enabled: true })
            {
                _perfHub.Scheduler.PostTask(() => _jsEngine.TickTimers(), TaskPriority.Normal);
            }
            else
            {
                _jsEngine.TickTimers();
            }
        }

        _chrome.UpdateLoadingProgress();

        if (_input.IsMouseDown())
        {
            _input.UpdatePageThumbDrag();
        }

        float currentDevToolsHeight = _devTools.Visible ? _devTools.PanelHeight : 0;
        bool devToolsChanged = _devTools.Visible != _lastDevToolsVisible ||
                               Math.Abs(currentDevToolsHeight - _lastDevToolsHeight) > 0.5f;

        if (_devTools.Visible)
        {
            var (mx, my) = _input.GetMousePosition();
            if (_input.IsMouseDown())
            {
                bool dragMoved = _devTools.HandleDragMove(mx, my);
                if (dragMoved) _input.NeedsRedraw = true;
            }
            else
            {
                _devTools.HandleDragEnd();
            }
        }

        bool cursorChanged = _chrome.UpdateCursorBlink();
        UpdateFormInputCursorBlink();
        bool devToolsCursorChanged = _devTools.Visible && _devTools.TickCursorBlink();
        bool cursorNeedsRedraw = (cursorChanged && _chrome.IsUrlBarFocused()) || devToolsCursorChanged;

        var (pw, ph) = _window.GetClientSize();
        int windowWidth = (int)(pw / _dpiScale);
        int windowHeight = (int)(ph / _dpiScale);

        bool sizeChanged = windowWidth != _lastWindowWidth || windowHeight != _lastWindowHeight;

        // Update smooth scrolling FIRST so scrollChanged below reflects THIS frame's
        // motion — otherwise the first smooth-scroll frame renders one frame late,
        // which is perceived as input/scroll delay.
        if (dt > 0)
        {
            _scroll.UpdateSmoothScroll((float)dt);
            UpdateElementSmoothScrolls((float)dt);
        }

        bool scrollChanged = Math.Abs(_scroll.ScrollX - _lastScrollX) > 0.5f ||
                             Math.Abs(_scroll.ScrollY - _lastScrollY) > 0.5f;

        // Scroll velocity = (Δscroll / Δt) in pixels/second. We feed the result
        // into the predictive tile scheduler so the compositor can pre-rasterise
        // tiles in the direction of travel. The velocity is decayed each frame
        // so the prediction window naturally narrows when the user stops
        // scrolling.
        if (scrollChanged && dt > 0.0001)
        {
            float vx = (_scroll.ScrollX - _lastScrollX) / (float)dt * 1000f;
            float vy = (_scroll.ScrollY - _lastScrollY) / (float)dt * 1000f;
            _skiaRenderer.ReportScrollVelocity(vx, vy);
        }
        else
        {
            // Apply a gentle decay so the predictor stops firing when the user
            // is idle. We multiply by 0.5 per frame, which means the velocity
            // signal is effectively zero after ~5 frames (~80 ms at 60 fps).
            _skiaRenderer.ReportScrollVelocity(0, 0);
        }

        bool inputRecently = Environment.TickCount64 - _lastInputTimeTick < InputCooldownMs;

        // Accumulate pending relayout flag. In process mode the child owns page
        // layout and the parent never runs a layout pass, so the flag must not
        // latch here — a stuck flag forces a full repaint every frame.
        if (_jsEngine.NeedsReLayout)
        {
            if (_processTabs) _jsEngine.ClearDirty();
            else _pendingRelayout = true;
        }

        if (_taskManagerPage.Visible)
        {
            long now = Environment.TickCount64;
            if (now - _lastTaskManagerRefresh > 1000)
            {
                _lastTaskManagerRefresh = now;
                _input.NeedsRedraw = true;
            }
        }

        bool remoteNavPending = false;
        if (_processTabs)
        {
            // Keep pumping while any deferred initial navigation awaits a
            // connected child — otherwise the queue could idle-block itself.
            foreach (var kv in _remoteTabs)
                if (!kv.Value.InitialSent && kv.Value.PendingInitial != null)
                {
                    remoteNavPending = true;
                    break;
                }
        }

        bool needsRedraw = _input.NeedsRedraw || _pendingRelayout || devToolsChanged ||
                           (cursorNeedsRedraw && !inputRecently) || scrollChanged || remoteNavPending ||
                           // A running animation or transition is itself a reason
                           // to produce a frame: nothing else about the page has
                           // changed, but its style has.
                           _animationsRunning;

        if (_renderStats)
        {
            long nowTs = Stopwatch.GetTimestamp();
            _rsPreMs += (nowTs - rsMark) * 1000 / Stopwatch.Frequency;
            rsMark = nowTs;
            _rsTotal++;
            if (needsRedraw) _rsRedraw++;
            if (_chrome.IsLoading) _rsLoading++;
            if (scrollChanged) _rsScroll++;
            if (_pendingRelayout) _rsRelayout++;
            if (_input.NeedsRedraw) _rsInput++;
            if (devToolsChanged) _rsDevTools++;
            if (cursorNeedsRedraw && !inputRecently) _rsCursor++;

            if (Environment.TickCount64 >= _rsNextLog)
            {
                _rsNextLog = Environment.TickCount64 + 2000;
                int n = Math.Max(1, (int)_rsRendered);
                var line = $"[parent stats] frames={_rsTotal} redraw={_rsRedraw} loading={_rsLoading} " +
                           $"scroll={_rsScroll} relayout={_rsRelayout} input={_rsInput} rendered={_rsRendered} " +
                           $"ms pre={_rsPreMs / n} pump={_rsPumpMs / n} body={_rsBodyMs / n} present={_rsPresentMs / n} " +
                           $"heapMB={GC.GetTotalMemory(false) / 1048576} wsMB={Environment.WorkingSet / 1048576}";
                Console.WriteLine(line);
                try { File.AppendAllText("acrux_tabstats_parent.log", line + "\n"); } catch { }
                _rsTotal = _rsRedraw = _rsLoading = _rsScroll = _rsRelayout = _rsInput = _rsDevTools = _rsCursor = 0;
                _rsPreMs = _rsPumpMs = _rsBodyMs = _rsPresentMs = 0; _rsRendered = 0;
            }
        }

        // 加载中时强制全帧渲染，确保进度条可见
        if (_chrome.IsLoading)
            _input.NeedsRedraw = true;

        if (!sizeChanged && !needsRedraw && !_chrome.IsLoading)
            return;

        if (windowWidth <= 0 || windowHeight <= 0 || (_currentLoad == null && !_adoptionPending && !_processTabs))
            return;

        if (sizeChanged)
        {
            _skiaRenderer.Resize(windowWidth, windowHeight);
            _lastWindowWidth = windowWidth;
            _lastWindowHeight = windowHeight;
            _skiaRenderer.InvalidatePageCache();
            // The measurer's cache is keyed by text, font, size, weight and device scale, so
            // a window resize cannot invalidate an entry — and clearing it here threw away the
            // cache every other tab in the process shares, once per resize event.
            _pendingRelayout = true;
        }

        float contentViewportHeight = windowHeight - _contentOffset - _chrome.GetStatusBarHeight() - currentDevToolsHeight;

        if (_processTabs)
        {
            PumpRemoteTabs(windowWidth, contentViewportHeight);
            if (_renderStats) { long t = Stopwatch.GetTimestamp(); _rsPumpMs += (t - rsMark) * 1000 / Stopwatch.Frequency; rsMark = t; }
            if (sizeChanged)
            {
                foreach (var kv in _remoteTabs)
                    kv.Value.Proc.Resize(windowWidth, contentViewportHeight);
            }
        }
        else if (sizeChanged && _threadedTabs)
            _processManager.SetViewportAll(windowWidth, Math.Max(100, contentViewportHeight));

        bool needsFullRebuild = (sizeChanged || windowWidth != _lastLayoutWidth || _pendingRelayout || devToolsChanged
                                 || _animationsRunning)
                                && !_adoptionPending && !_processTabs;
        if (_processTabs) _pendingRelayout = false;

        // While a resize/move drag is in flight, bypass the tile compositor and
        // draw the whole page directly: every resize tick would otherwise discard
        // and re-raster the entire tile cache (~100 ms per tick), which reads as
        // a blank/black window that lags the mouse. A single recorded-picture draw
        // keeps each frame complete and cheap.
        bool directDraw = _window.IsInSizeMove;

        // Interactive element-scroll frames skip the deferred background raster
        // drain (the scrolled container is flushed synchronously below), so the
        // frame stays bounded and the scroll feels immediate.
        bool interactiveScrollFrame = false;

        // Retained compositing: keep the previous frame's page pixels wherever the
        // tile compositor cannot yet fill this frame — a scroll exposes regions with
        // no ready tile, and dropping/clearing there shows a white flash. Retain on
        // every frame that changes the page or still has pending/just-completed tile
        // work; tiles that complete land on the next repaint. Frames with nothing
        // pending clear so overlay/caret changes stay crisp.
        bool tileBacklogRetained =
            _skiaRenderer.TileCompositorActive && !devToolsChanged && !directDraw && !_processTabs &&
            (needsFullRebuild || scrollChanged || _scrollDirty || _elementScrollDirty ||
             _compositorBacklogLast || _pendingTileWorkLast);
        if (needsFullRebuild)
        {
            // Layout/content changed: cached scroll layers are stale, rebuild them
            // (the paint walk below re-registers every layered container).
            ScrollLayerCache.ClearAll();
            _lastLayoutWidth = windowWidth;

            // A relayout triggered ONLY by a hover change is a small, localized
            // update: the rest of the page is pixel-identical, so keep the tile
            // cache and re-raster just the hovered region synchronously. Dropping
            // every tile (InvalidateAll) here makes the whole page re-tile over
            // several deferred frames — visible as "pixelated flicker" while the
            // mouse crosses scroll containers.
            bool hoverOnlyRebuild = _hoverRelayoutPending
                && !sizeChanged && !devToolsChanged && !_jsEngine.NeedsReLayout;
            _hoverRelayoutPending = false;

            // Style re-resolves when the document changed. It is NOT re-resolved for
            // an animation frame where nothing else is dirty: the animation rewrites
            // the same properties every frame, so a full style pass per frame is pure
            // cost, and on a real page it dominates the frame. AdvanceAnimations is
            // told which case this is, so a transition cannot start off the engine's
            // own output.
            bool styleRecomputed = _pendingRelayout;
            if (styleRecomputed)
            {
                var styleComputer = _currentLoad.StyleComputer;
                if (styleComputer == null)
                {
                    styleComputer = new StyleComputer();
                    styleComputer.AddStylesheet(_docManager.GetUaStylesheet(), Acrux.Core.Css.Resolver.CascadeOrigin.UserAgent);
                }
                // Style compute: timing is already recorded inside CascadeResolver
                // via PipelineTimings.Style. We additionally wrap in a long-task
                // observer so anything > 50 ms is reported.
                if (_perfHub is { Enabled: true })
                {
                    _perfHub.LongTasks.Observe("StyleCompute", TaskPriority.High,
                        () => styleComputer.ComputeStyles(_currentLoad.Document, windowWidth, contentViewportHeight));
                }
                else
                {
                    styleComputer.ComputeStyles(_currentLoad.Document, windowWidth, contentViewportHeight);
                }
                _jsEngine.ClearDirty();
                _pendingRelayout = false;
            }

            // #3: page scrollbar takes layout space — reduce content width so
            // text doesn't go under the scrollbar.
            float sbWidth = _scroll.CanScrollY ? 12f : 0f;
            float layoutWidth = Math.Max(100, windowWidth - sbWidth);
            BuildDisplayList(layoutWidth, Math.Max(100, (int)contentViewportHeight), styleRecomputed);
            UpdateInputScrollOffset(_focusedElement);

            var bodyBox = _currentLoad.Document.Body?.LayoutBox;
            float contentWidth = bodyBox?.BorderBox.Width ?? layoutWidth;
            float contentHeight = bodyBox?.BorderBox.Height ?? 0;

            _scroll.UpdateScroll(contentWidth, contentHeight, windowWidth, contentViewportHeight);

            _window.UpdateImeCompositionWindow();

            if (hoverOnlyRebuild && _skiaRenderer.TileCompositorActive
                && _hoverDirtyRect.Width > 0 && _hoverDirtyRect.Height > 0)
            {
                // Hover-only rebuild: adopt the new display list without bumping the
                // cache generation, drop just the tiles over the hovered region and
                // re-raster them synchronously. Everything else survives the frame,
                // so the page updates atomically instead of re-tiling over frames.
                _skiaRenderer.AdoptRebuiltDisplayList(_displayList);
                _skiaRenderer.InvalidatePageRect(_hoverDirtyRect);
                _skiaRenderer.SetChangeRegion(_hoverDirtyRect);
                _skiaRenderer.PrerasterizePageRect(_hoverDirtyRect);
            }
        }
        else if (_elementScrollDirty && _currentLoad != null)
        {
            // Element scroll offsets changed but layout/styles didn't. When the
            // scrolled containers were rasterized as scroll LAYERS (cached content
            // + live scroll offset), the display list and recorded picture are
            // already scroll-invariant: the only work is re-baking the picture at
            // the new offset and re-rastering the container's tiles — NO document
            // repaint. Containers that could not be layered fall back to the
            // paint-only rebuild (scroll baked into ops), which is still correct.
            _elementScrollDirty = false;

            bool allLayered = _skiaRenderer.TileCompositorActive
                && _pendingElementScrollBoxes.Count > 0
                && _pendingElementScrollBoxes.All(ScrollLayerCache.IsLayered);

            if (allLayered)
            {
                // Layered containers are composited LIVE by the compositor at their
                // current scroll offset (ScrollLayerCache) — scrolling them needs no
                // tile invalidation, no re-record, no flush. Sticky / z-index layers
                // (IsBaked) are rebuilt from their subtree at the current scroll,
                // but only when the device-quantized offset actually moved — the
                // baked image is already within one device pixel otherwise.
                interactiveScrollFrame = true;
                float bakedScale = _dpiScale * _renderingSettings.ResolutionScale;
                foreach (var bb in _pendingElementScrollBoxes)
                {
                    if (ScrollLayerCache.TryGetInfo(bb, out var info)
                        && info.IsBaked
                        && ScrollLayerCache.BakedOffsetChanged(bb, bakedScale))
                    {
                        _cachedPaintVisitor?.RebuildScrollLayer(bb);
                    }
                }
            }
            else
            {
            // Only the paint walk is needed to rebuild the display list (scroll
            // offsets are applied as paint-time transforms); style+layout would be
            // wasted work. The scrolled containers are region-invalidated so only
            // their tiles re-raster and the rest of the cache survives.
            float sbW = _scroll.CanScrollY ? 12f : 0f;
            int lw = Math.Max(100, windowWidth - (int)sbW);
            int vh = Math.Max(100, (int)contentViewportHeight);

            // ── Fast path: single non-layered scroller with an opaque background ──
            // Only that container's subtree changed; re-paint it in isolation
            // (O(container), no O(document) layer-tree rebuild per frame) and
            // swap its ops into the existing display list. The opaque background
            // guarantees the freshly painted subtree fully covers the container's
            // previous pixels, so nothing ghosts through tile transparency. Any
            // failure falls back to the full-document rebuild below.
            bool subtreeFastPath = false;
            if (_pendingElementScrollBoxes.Count == 1)
            {
                try
                {
                    subtreeFastPath = TrySubtreeRepaintScrollContainer(_pendingElementScrollBoxes[0], lw, vh);
                }
                catch
                {
                    subtreeFastPath = false;
                }
            }

            if (!subtreeFastPath)
            {
            PaintOpPool.Clear();
            _displayList.Clear();
            _cachedPaintVisitor = new PaintVisitor(_contentOffset, _sharedTypefaceCache,
                _sharedImageCache, _fontFamilies, _currentBaseUrl, lw, vh);
            _cachedPaintVisitor.PhysicalScale = _dpiScale * _renderingSettings.ResolutionScale;
            _cachedPaintVisitor.SetSkipInputTextOverlay(true);
            _cachedPaintVisitor.SetCullRect(new SKRect(
                _scroll.ScrollX - 300, _scroll.ScrollY - 300,
                _scroll.ScrollX + windowWidth + 300, _scroll.ScrollY + contentViewportHeight + 600));

            _cachedPaintVisitor.VisitDocumentStacking(_currentLoad.Document);
            _displayList = _cachedPaintVisitor.GetDisplayList();
            _displayList.SortByZIndex();
            }

            interactiveScrollFrame = true;
            foreach (var rect in _pendingElementScrollRects)
            {
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                _skiaRenderer.InvalidatePageRect(rect);
            }
            if (_skiaRenderer.TileCompositorActive)
            {
                // Publish the rebuilt display list to the compositor BEFORE the
                // flush, and force its recorded picture to be re-created. The
                // synchronous re-raster below must replay the NEW content; if the
                // old picture were reused the flushed tiles would cache stale
                // (pre-scroll) pixels and the container would appear frozen.
                _skiaRenderer.AdoptRebuiltDisplayList(_displayList);
                // Re-raster synchronously only the ON-SCREEN slice of each scrolled
                // container (plus one tile of margin so the emerging edge strip is
                // covered). Off-screen container tiles were dropped above and are
                // lazily re-rastered by the visible pass when they enter the viewport,
                // so a tall container cannot stall the frame.
                float tileMargin = _skiaRenderer.Compositor?.TileSize ?? TiledCompositor.DefaultTileSize;
                var flushCull2 = new SKRect(
                    -tileMargin, _contentOffset - tileMargin,
                    windowWidth + tileMargin, _contentOffset + contentViewportHeight + tileMargin);
                for (int i = 0; i < _pendingElementScrollRects.Count; i++)
                {
                    var rect = _pendingElementScrollRects[i];
                    if (rect.Width <= 0 || rect.Height <= 0) continue;
                    var onScreen = SKRect.Intersect(rect, flushCull2);
                    if (onScreen.Width <= 0 || onScreen.Height <= 0) continue;
                    // The change-only recorded picture culls to this region, but a
                    // scrolled container's content ops carry UNTRANSLATED bounds
                    // (the scroll is applied as a paint-time transform). Content that
                    // draws into the on-screen slice lives at positions shifted by
                    // the box's scroll offset, so include that shifted band too —
                    // otherwise a container scrolled beyond one tile renders empty/
                    // stale below the fold (ghosted through the retained surface).
                    LayoutBox? sb = i < _pendingElementScrollBoxes.Count ? _pendingElementScrollBoxes[i] : null;
                    var cullRegion = onScreen;
                    if (sb != null && (sb.ScrollX != 0 || sb.ScrollY != 0))
                    {
                        cullRegion = SKRect.Union(onScreen, new SKRect(
                            onScreen.Left + sb.ScrollX,
                            onScreen.Top + sb.ScrollY,
                            onScreen.Right + sb.ScrollX,
                            onScreen.Bottom + sb.ScrollY));
                    }
                    _skiaRenderer.SetChangeRegion(cullRegion);
                    _skiaRenderer.PrerasterizePageRect(onScreen);
                }
            }
            }
            _pendingElementScrollRects.Clear();
            _pendingElementScrollBoxes.Clear();
        }
        else if (_scrollDirty && _currentLoad != null)
        {
            if (_skiaRenderer.TileCompositorActive)
            {
                // Tiles are cached in page space — a scroll only changes the page
                // origin, so skip the display-list rebuild and tile-cache
                // invalidation. Reusing the cached tiles makes scroll instant and
                // avoids the re-raster blank flash; the rebuilt cull-limited
                // display list is only needed for the direct picture path.
                _scrollDirty = false;
            }
            else
            {
            // ── Lightweight scroll-only repaint ──
            // Scroll offset changed but layout/styles haven't. Just regenerate
            // the display list from existing LayoutBoxes with current ScrollY
            // transforms. Much faster than a full rebuild.
            _scrollDirty = false;

            float sbW = _scroll.CanScrollY ? 12f : 0f;
            int lw = Math.Max(100, windowWidth - (int)sbW);
            int vh = Math.Max(100, (int)contentViewportHeight);

            PaintOpPool.Clear();
            _displayList.Clear();
            _cachedPaintVisitor = new PaintVisitor(_contentOffset, _sharedTypefaceCache,
                _sharedImageCache, _fontFamilies, _currentBaseUrl, lw, vh);
            _cachedPaintVisitor.PhysicalScale = _dpiScale * _renderingSettings.ResolutionScale;
            _cachedPaintVisitor.SetSkipInputTextOverlay(true);
            _cachedPaintVisitor.SetCullRect(new SKRect(
                _scroll.ScrollX - 300, _scroll.ScrollY - 300,
                _scroll.ScrollX + windowWidth + 300, _scroll.ScrollY + contentViewportHeight + 600));

            _cachedPaintVisitor.VisitDocumentStacking(_currentLoad.Document);
            _displayList = _cachedPaintVisitor.GetDisplayList();
            _displayList.SortByZIndex();

            _skiaRenderer.InvalidatePageCache();
            }
        }
        else if (_input.NeedsRedraw && _cachedPaintVisitor != null)
        {
            // Input-only change: avoid O(n) DOM walk + display list rebuild.
            // Only rebuild the overlay (input text/cursor/selection — ~O(1)).
            UpdateInputScrollOffset(_focusedElement);
            UpdateTextAreaScrollY();
            _cachedPaintVisitor.SetFocusedElement(_focusedElement);
            _cachedPaintVisitor.SetPasswordRevealed(_passwordRevealed);
            _cachedPaintVisitor.SetInputScrollOffset(_inputScrollOffset);
            _cachedPaintVisitor.SetTextAreaScrollY(_textareaScrollY);
            _cachedPaintVisitor.SetTextAreaUserScroll(_textareaUserScroll);
            var (hmx, hmy) = _input.GetMousePosition();
            _cachedPaintVisitor.SetMouseState(hmx + _scroll.ScrollX, hmy - _contentOffset + _scroll.ScrollY,
                _input.IsMouseDown(), _pressedInputControl);
            _cachedPaintVisitor.SetPressedButton(_pressedButton);
            if (_activeSelect != null)
            {
                ComputeSelectDropdownGeometry();
                _cachedPaintVisitor.SetSelectDropdown(_activeSelect, _selectDropdownRect, _selectOptionRects, _selectHoverIndex);
            }
            else
            {
                _cachedPaintVisitor.SetSelectDropdown(null, default, null, -1);
            }
            if (_focusedElement != null && _focusedElement.IsTextEditable)
            {
                _cachedPaintVisitor.SetInputState(_inputCursorPos, _inputSelStart, _inputShowCursor,
                    _inputImeComposing, _inputImeCompositionStr, _inputImeCursorPos);
                // Update stored scroll offset from PaintVisitor's overlay build
                UpdateInputScrollOffset(_focusedElement);
            }
            _cachedPaintVisitor.RebuildOverlay();
        }

        if (_focusedElement != null && _focusedElement.IsTextEditable)
        {
            _window.UpdateImeCompositionWindow();
        }

        _lastScrollX = _scroll.ScrollX;
        _lastScrollY = _scroll.ScrollY;
        _lastDevToolsHeight = currentDevToolsHeight;
        _lastDevToolsVisible = _devTools.Visible;

        if (!tileBacklogRetained)
            _skiaRenderer.Canvas.Clear(SKColors.White);
        else if (!_everRenderedPage)
        {
            // First-ever frame: there is no previous surface to retain, so seed
            // the page area with its background before the tiles composite over it.
            using var seedBg = new SKPaint
            {
                Color = _cachedPaintVisitor?.ViewBackgroundColor ?? SKColors.White,
                Style = SKPaintStyle.Fill,
            };
            _skiaRenderer.Canvas.DrawRect(
                0, _contentOffset, windowWidth, _contentOffset + contentViewportHeight, seedBg);
        }

        var title = _currentLoad?.Document.Title ?? "Acrux";
        if (_processTabs)
        {
            var rvT = ActiveRemote();
            if (rvT != null && !string.IsNullOrEmpty(rvT.Proc.Title))
                title = rvT.Proc.Title;
        }
        var currentUrl = _chrome.GetCurrentUrl();
        if (string.IsNullOrEmpty(currentUrl))
            currentUrl = "acrux://local";

        // Update tab tooltips
        int activeTabIdx = _chrome.ActiveTabIndex;
        for (int i = 0; i < _chrome.Tabs.Count; i++)
        {
            var tab = _chrome.Tabs[i];
            int nDom = 0, nBox = 0;
            if (i == activeTabIdx && _currentLoad != null)
            {
                nDom = CountDomNodes(_currentLoad.Document);
                nBox = CountLayoutBoxes(_currentLoad.Document);
            }
            else if (_processTabs && _remoteTabs.TryGetValue(i, out var rv))
            {
                nDom = rv.DomCount;
                nBox = rv.BoxCount;
            }
            else if (_tabStates.TryGetValue(i, out var st))
            {
                nDom = st.DomNodeCount;
                nBox = st.LayoutBoxCount;
            }
            tab.TooltipText = $"Title: {tab.Title}\nURL: {(string.IsNullOrEmpty(tab.Url) ? "acrux://newtab" : tab.Url)}\nDOM Nodes: {nDom}\nLayout Boxes: {nBox}\nStatus: {(tab.IsLoading ? "Loading" : (i == activeTabIdx ? "Running" : "Complete"))}";
        }

        _chrome.RenderChrome(_skiaRenderer.Canvas, windowWidth, windowHeight, currentUrl, title);

        if (_processTabs)
            RenderRemotePage(windowWidth, contentViewportHeight);
        else
            _skiaRenderer.RenderWithScroll(_displayList, _contentOffset,
                _scroll.ScrollX, _scroll.ScrollY,
                windowWidth, contentViewportHeight,
                _cachedPaintVisitor?.OverlayList,
                _cachedPaintVisitor?.ViewBackgroundColor ?? SKColors.White,
                interactiveScrollFrame, directDraw);

        // Remember whether the deferred tile rasterizer still has backlog, so the
        // next frame knows to retain the page pixels instead of clearing them.
        _compositorBacklogLast = _skiaRenderer.Compositor?.HasPendingRasterWork ?? false;
        _pendingTileWorkLast = _skiaRenderer.PendingTileRepaint;
        _everRenderedPage = true;

        _chrome.RenderScrollbars(_skiaRenderer.Canvas, windowWidth, windowHeight, _scroll);

        _chrome.RenderTabTooltip(_skiaRenderer.Canvas, windowWidth, windowHeight);

        _devTools.Render(_skiaRenderer.Canvas, windowWidth, windowHeight, _contentOffset);

        _renderingSettingsPage.Render(_skiaRenderer.Canvas, windowWidth, windowHeight, _contentOffset);

        // Build rich data for task manager with multi-process metrics
        var tmRows = new System.Collections.Generic.List<TmRowData>();
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        proc.Refresh();

        // Collect per-tab process metrics
        var allProcMetrics = _processManager.GetAllMetrics();
        double wsmb = proc.WorkingSet64 / (1024.0 * 1024.0);
        double heapMB = System.GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        // Performance pipeline timings (read accumulators for latest values)
        double styleMs = PipelineTimings.Style.MeanMillis;
        double layoutMs = PipelineTimings.Layout.MeanMillis;
        double paintMs = PipelineTimings.Paint.MeanMillis;
        double scriptMs = PipelineTimings.Script.MeanMillis;
        double compositeMs = PipelineTimings.Composite.MeanMillis;
        double imageDecodeMs = PipelineTimings.ImageDecode.MeanMillis;
        double tileRasterMs = PipelineTimings.TileRaster.MeanMillis;
        double networkMs = PipelineTimings.NetworkWait.MeanMillis;

        // Memory breakdown from performance subsystems
        double imagePoolMB = _perfHub?.ImagePool.CapacityBytes / (1024.0 * 1024.0) ?? 0;
        double tileMemoryMB = (_perfHub?.Tiles.Settings?.MaxTilesInMemory ?? 0) * (256.0 * 256.0 * 4.0) / (1024.0 * 1024.0);

        // Rendering counters
        int tilesRast = (int)PipelineTimings.TilesRasterized.Value;
        int tilesReused = (int)PipelineTimings.TilesReused.Value;
        int imagesDecoded = (int)PipelineTimings.ImagesDecoded.Value;
        int imageHits = (int)PipelineTimings.ImageCacheHits.Value;
        int cacheHits = (int)PipelineTimings.ResourceCacheHits.Value;

        // JS stats
        int jsHeapSize = _jsEngine.GetHeapSizeKB();
        int jsCallbacks = _jsEngine.TimerCount;

        // Frame timing from the window
        double frameTimeMs = Math.Max(dt, 1.0 / 1000.0);
        double fps = 1000.0 / frameTimeMs;

        tmRows.Add(new TmRowData
        {
            Name = "Browser",
            Detail = $"PID: {proc.Id}",
            Memory = $"{wsmb:F1} MB",
            Cpu = "",
            Status = "Running",
            Pid = proc.Id,
            TabIndex = -1,
            StyleTimingMs = styleMs,
            LayoutTimingMs = layoutMs,
            PaintTimingMs = paintMs,
            ScriptTimingMs = scriptMs,
            CompositeTimingMs = compositeMs,
            ImageDecodeTimingMs = imageDecodeMs,
            TileRasterTimingMs = tileRasterMs,
            NetworkWaitTimingMs = networkMs,
            WorkingSetMB = wsmb,
            ManagedHeapMB = heapMB,
            ImageCacheMB = imagePoolMB,
            TileMemoryMB = tileMemoryMB,
            TilesRasterized = tilesRast,
            TilesReused = tilesReused,
            ImagesDecoded = imagesDecoded,
            ImageCacheHits = imageHits,
            ResourceCacheHits = cacheHits,
            JsHeapSizeKB = jsHeapSize,
            JsCallbackCount = jsCallbacks,
            FrameTimeMs = frameTimeMs,
            Fps = fps,
        });
        tmRows.Add(new TmRowData
        {
            Name = "  Working Set",
            Detail = "",
            Memory = $"{wsmb:F1} MB",
            Status = "",
            TabIndex = -1,
            WorkingSetMB = wsmb,
        });
        tmRows.Add(new TmRowData
        {
            Name = "  Managed Heap",
            Detail = "",
            Memory = $"{heapMB:F1} MB",
            Status = "",
            TabIndex = -1,
            ManagedHeapMB = heapMB,
        });
        tmRows.Add(new TmRowData
        {
            Name = "  Image Cache",
            Detail = "",
            Memory = $"{imagePoolMB:F1} MB",
            Status = "",
            TabIndex = -1,
            ImageCacheMB = imagePoolMB,
        });

        var tabSnapshot = _chrome.SnapshotTabs();
        for (int i = 0; i < tabSnapshot.Length; i++)
        {
            var tab = tabSnapshot[i];
            int domNodes = 0, layoutBoxes = 0;
            double memMB = 0;
            if (i == activeTabIdx && _currentLoad != null)
            {
                domNodes = CountDomNodes(_currentLoad.Document);
                layoutBoxes = CountLayoutBoxes(_currentLoad.Document);
            }
            else if (_processTabs && _remoteTabs.TryGetValue(i, out var rvTm))
            {
                domNodes = rvTm.DomCount;
                layoutBoxes = rvTm.BoxCount;
            }
            else if (_tabStates.TryGetValue(i, out var st))
            {
                domNodes = st.DomNodeCount;
                layoutBoxes = st.LayoutBoxCount;
            }

            // Use per-process metrics when available
            var tcMetrics = _processManager.GetMetrics(i);
            if (tcMetrics != null)
            {
                if (domNodes == 0) domNodes = tcMetrics.DomNodeCount;
                if (layoutBoxes == 0) layoutBoxes = tcMetrics.LayoutBoxCount;
                memMB = tcMetrics.MemoryBytes / (1024.0 * 1024.0);
            }

            string detail = string.IsNullOrEmpty(tab.Url) || tab.Url == "acrux://newtab" ? "" : tab.Url;
            string status = tab.IsLoading ? "Loading" : (i == activeTabIdx ? "Running" : "Complete");
            // 每个标签页只显示自己独立的内存数据（来自 TabProcess），
            // 不显示整个进程的内存占用，避免误导
            tmRows.Add(new TmRowData
            {
                Name = string.IsNullOrEmpty(tab.Title) ? "New Tab" : tab.Title,
                Detail = detail,
                Memory = memMB > 0 ? $"{memMB:F1} MB" : "-",
                Cpu = i == activeTabIdx ? "" : "-",
                DomNodes = domNodes,
                LayoutBoxes = layoutBoxes,
                Status = status,
                TabIndex = i,
                StyleTimingMs = i == activeTabIdx ? styleMs : 0,
                LayoutTimingMs = i == activeTabIdx ? layoutMs : 0,
                PaintTimingMs = i == activeTabIdx ? paintMs : 0,
                ScriptTimingMs = i == activeTabIdx ? scriptMs : 0,
                CompositeTimingMs = i == activeTabIdx ? compositeMs : 0,
                ImageDecodeTimingMs = i == activeTabIdx ? imageDecodeMs : 0,
                TileRasterTimingMs = i == activeTabIdx ? tileRasterMs : 0,
                NetworkWaitTimingMs = i == activeTabIdx ? networkMs : 0,
                WorkingSetMB = memMB,
                ManagedHeapMB = i == activeTabIdx ? heapMB : 0,
                ImageCacheMB = i == activeTabIdx ? imagePoolMB : 0,
                TileMemoryMB = i == activeTabIdx ? tileMemoryMB : 0,
                TilesRasterized = i == activeTabIdx ? tilesRast : 0,
                TilesReused = i == activeTabIdx ? tilesReused : 0,
                ImagesDecoded = i == activeTabIdx ? imagesDecoded : 0,
                ImageCacheHits = i == activeTabIdx ? imageHits : 0,
                ResourceCacheHits = i == activeTabIdx ? cacheHits : 0,
                JsHeapSizeKB = (i == activeTabIdx ? jsHeapSize : (tcMetrics?.JsHeapSizeKB ?? 0)),
                JsCallbackCount = (i == activeTabIdx ? jsCallbacks : (tcMetrics?.JsTimerCount ?? 0)),
                FrameTimeMs = i == activeTabIdx ? frameTimeMs : 0,
                Fps = i == activeTabIdx ? fps : 0,
            });
        }

        _taskManagerPage.Render(_skiaRenderer.Canvas, windowWidth, windowHeight, _contentOffset, tmRows);

        // A page-host modal is drawn by the ordinary frame loop — the shell keeps
        // painting and animating while the page's script thread waits for the answer.
        if (_dialogActive)
            RenderDialogOverlay(_skiaRenderer.Canvas, windowWidth, windowHeight);

        var activeView = ActiveRemote();
        if (activeView != null && activeView.Crashed)
        {
            RenderCrashCard(_skiaRenderer.Canvas, windowWidth, _contentOffset, activeView.CrashDetail);
        }
        else
        {
            _crashReloadRect = SKRect.Empty;
            if (activeView != null && activeView.Proc.Responsiveness == PageResponsiveness.Unresponsive && !_bubbleMuted)
                RenderUnresponsiveBubble(_skiaRenderer.Canvas, windowWidth, _contentOffset);
            else
                _bubbleKillRect = _bubbleReloadRect = _bubbleWaitRect = SKRect.Empty;
        }

        _skiaRenderer.TickFrame();
        _skiaRenderer.RenderFpsCounter(_skiaRenderer.Canvas, windowWidth, windowHeight);

        var pixels = _skiaRenderer.GetPixelData();
        if (_renderStats) { long t = Stopwatch.GetTimestamp(); _rsBodyMs += (t - rsMark) * 1000 / Stopwatch.Frequency; rsMark = t; }
        _window.Render(pixels, _skiaRenderer.PhysicalWidth, _skiaRenderer.PhysicalHeight);
        if (_renderStats)
        {
            _rsPresentMs += (Stopwatch.GetTimestamp() - rsMark) * 1000 / Stopwatch.Frequency;
            _rsRendered++;
        }

        _input.NeedsRedraw = false;
        if (_input.IsMouseDown()) _input.NeedsRedraw = true;
        // While dragging, keep requesting frames: the first frame after the drag
        // ends (directDraw flips back to false) hands over to the tile path and
        // starts warming the cache at the settled size.
        if (directDraw) _input.NeedsRedraw = true;
        // 瓦片合成器可能还有延迟栅格的瓦片未画完：请求再渲染一帧将其合成上屏
        if (_skiaRenderer.PendingTileRepaint)
        {
            _skiaRenderer.PendingTileRepaint = false;
            _input.NeedsRedraw = true;
        }
        // 进度条刚被清除时，强制再渲染一帧来清除残留的进度条图像
        if (_chrome.IsProgressJustCleared)
            _input.NeedsRedraw = true;
    }

    private void UpdateImeTarget()
    {
        if (_chrome.IsUrlBarFocused())
        {
            _devToolsFocused = false;
            _focusedElement = null;
            _window.SetImeTarget(_chrome);
        }
        else if (_devToolsFocused && _devTools.Visible)
        {
            _focusedElement = null;
            var ime = _devTools.GetActiveImeSupport();
            _window.SetImeTarget(ime);
        }
        else if (_processTabs)
        {
            _devToolsFocused = false;
            // Out-of-process page: the child owns the DOM focus, so the IME target follows
            // the newest frame's metadata instead of _focusedElement (which the shell never
            // holds in process mode). Password fields block IME here too, and a tab without
            // a frame yet behaves exactly like a page without an editable focus.
            var proc = ActiveRemote()?.Proc;
            if (proc != null && proc.TryGetImeState(out _, out _, out _, out bool hasFocus, out bool password)
                && hasFocus && !password)
                _window.SetImeTarget(_pageInputImeHost);
            else
                _window.SetImeTarget(null);
        }
        else if (_focusedElement != null && _focusedElement.IsTextEditable)
        {
            _devToolsFocused = false;
            // Block IME for password fields to prevent pinyin composition
            string? inputType = _focusedElement.InputType?.ToLowerInvariant();
            if (inputType == "password")
                _window.SetImeTarget(null);
            else
                _window.SetImeTarget(_pageInputImeHost);
        }
        else
        {
            _devToolsFocused = false;
            if (_activeSelect == null)
                _focusedElement = null;
            _window.SetImeTarget(null);
        }
    }

    private void OnKeyDown(Key key)
    {
        _lastInputTimeTick = Environment.TickCount64;
        if (key == Key.F12)
        {
            _devTools.Toggle();
            _input.NeedsRedraw = true;
            return;
        }
        _chrome.HandleKeyPress('\0', key switch
        {
            Key.Escape => SKKey.Escape,
            Key.F5 => SKKey.None,
            _ => SKKey.None
        });
    }

    private bool HandleDevToolsClick(float x, float y, bool isDown)
    {
        if (!_devTools.Visible) return false;

        if (isDown)
        {
            _devTools.HandleMouseUp(x, y);
            return true;
        }

        if (_devTools.HandleDragStart(x, y))
        {
            _devToolsFocused = true;
            _focusedElement = null;
            UpdateImeTarget();
            return true;
        }
        bool handled = _devTools.HandleClick(x, y);
        if (handled)
        {
            _devToolsFocused = true;
            if (_focusedElement != null)
            {
                _focusedElement = null;
                _pendingRelayout = true;
            }
            _window.UpdateImeCompositionWindow();
            _input.NeedsRedraw = true;
        }
        else
        {
            _devToolsFocused = false;
        }
        UpdateImeTarget();
        return handled;
    }

    private bool HitTestElementScrollbar(float x, float y, out LayoutBox? hitBox, out bool isVertical)    {
        hitBox = null;
        isVertical = false;
        if (_currentLoad == null) return false;
        float docX = x + _scroll.ScrollX;
        float docY = y - _contentOffset + _scroll.ScrollY;

        // Find the deepest element at this point, then walk up to find a scroll container
        var element = PageHitTest.HitTest(_currentLoad.Document, docX, docY);
        if (element == null) return false;
        var el = element;
        while (el != null)
        {
            var box = el.LayoutBox;
            if (box != null && box.IsScrollContainer &&
                box.ContentBox.Height > 0 && box.ContentBox.Width > 0 &&
                (box.ScrollContentHeight > box.ContentBox.Height || box.ScrollContentWidth > box.ContentBox.Width))
            {
                var pb = box.PaddingBox;
                float scrollBarW = 12f;
                bool vert = box.ScrollContentHeight > box.ContentBox.Height;
                bool horz = box.ScrollContentWidth > box.ContentBox.Width;
                if (vert && docX >= pb.Right - scrollBarW && docX <= pb.Right &&
                    docY >= pb.Top && docY <= pb.Bottom)
                {
                    hitBox = box;
                    isVertical = true;
                    return true;
                }
                if (horz && docY >= pb.Bottom - scrollBarW && docY <= pb.Bottom &&
                    docX >= pb.Left && docX <= pb.Right)
                {
                    hitBox = box;
                    isVertical = false;
                    return true;
                }
            }
            el = el.ParentElement;
        }
        return false;
    }

    // Textarea vertical scrollbar (overlay, drawn by DrawTextAreaElement). Handles
    // thumb drag start and track page-up/down; only active while the textarea is
    // focused and its content overflows.
    private bool HandleTextAreaScrollbarClick(float x, float y)
    {
        if (_focusedElement == null || _focusedElement.TagName != "TEXTAREA" ||
            _focusedElement.ComputedStyle == null || _focusedElement.LayoutBox == null)
            return false;
        float docX = x + _scroll.ScrollX;
        float docY = y - _contentOffset + _scroll.ScrollY;
        var cb = _focusedElement.LayoutBox.ContentBox;
        const float scrollBarW = 12f;
        if (docX < cb.Right - scrollBarW || docX > cb.Right || docY < cb.Top || docY > cb.Bottom)
            return false;
        // A resize grip owns the bottom-right corner; leave that square to it.
        if (_focusedElement.ComputedStyle.Resize != ResizeType.None && docY >= cb.Bottom - 16)
            return false;
        var (usableH, totalH, _, maxScrollY, _) = GetTextAreaMetrics(_focusedElement);
        if (maxScrollY <= 0) return false;

        float trackHeight = cb.Height;
        float thumbHeight = Math.Max(20, trackHeight * Math.Min(1, usableH / Math.Max(1, totalH)));
        float thumbTop = (trackHeight - thumbHeight) * (_textareaScrollY / maxScrollY);
        float localY = docY - cb.Top;
        if (localY >= thumbTop && localY <= thumbTop + thumbHeight)
        {
            _textareaScrollDragging = true;
            _textareaScrollDragStartY = localY;
            _textareaScrollDragStartScroll = _textareaScrollY;
            _input.NeedsRedraw = true;
        }
        else
        {
            _textareaUserScroll = true;
            float page = trackHeight * 0.9f;
            _textareaScrollY = Math.Clamp(_textareaScrollY + (localY < thumbTop ? -page : page), 0, maxScrollY);
            _input.NeedsRedraw = true;
        }
        return true;
    }

    private void ComputeSelectDropdownGeometry()
    {
        _selectOptionRects.Clear();
        if (_activeSelect == null || _activeSelect.LayoutBox == null)
        {
            _selectDropdownRect = default;
            return;
        }
        var cb = _activeSelect.LayoutBox.ContentBox;
        float fontSize = _activeSelect.ComputedStyle?.FontSize > 0 ? _activeSelect.ComputedStyle.FontSize : 14;
        float rowHeight = Math.Max(22, fontSize + 8);

        int optionCount = _activeSelect.Children.Count(o => o is Core.Dom.Element ce && ce.TagName == "OPTION");
        if (optionCount == 0)
        {
            _selectDropdownRect = default;
            return;
        }

        float maxTextWidth = cb.Width - 8;
        foreach (var child in _activeSelect.Children)
        {
            if (child is Core.Dom.Element ce && ce.TagName == "OPTION")
            {
                string optText = ce.TextContent?.Trim() ?? "";
                float tw = Core.Layout.TextMeasurer.Instance?.MeasureText(optText, "Segoe UI, Arial, sans-serif", fontSize)
                           ?? optText.Length * fontSize * 0.55f;
                if (tw > maxTextWidth) maxTextWidth = tw;
            }
        }
        float dropW = maxTextWidth + 24;
        float dropH = optionCount * rowHeight;

        float dropX = cb.Left;
        float dropY = cb.Bottom;
        float viewportH = _lastWindowHeight - _contentOffset - _chrome.GetStatusBarHeight() -
                          (_devTools.Visible ? _devTools.PanelHeight : 0);
        float viewportBottom = _contentOffset + viewportH;
        if (dropY + dropH > viewportBottom)
            dropY = Math.Max(cb.Top - dropH, 0);
        if (dropX + dropW > _lastWindowWidth)
            dropX = Math.Max(0, _lastWindowWidth - dropW);

        _selectDropdownRect = new SKRect(dropX, dropY, dropX + dropW, dropY + dropH);

        int i = 0;
        foreach (var child in _activeSelect.Children)
        {
            if (child is Core.Dom.Element ce && ce.TagName == "OPTION")
            {
                float rowTop = dropY + i * rowHeight;
                _selectOptionRects.Add((ce, new SKRect(dropX, rowTop, dropX + dropW, rowTop + rowHeight)));
                i++;
            }
        }
        _selectHoverIndex = -1;
    }

    private void CloseSelectDropdown()
    {
        _activeSelect = null;
        _selectOptionRects.Clear();
        _selectHoverIndex = -1;
        _pendingRelayout = true;
    }

    private bool HandleSelectDropdownClick(float x, float y)
    {
        if (_selectDropdownRect.Width <= 0)
        {
            CloseSelectDropdown();
            return false;
        }
        float docX = x + _scroll.ScrollX;
        float docY = y - _contentOffset + _scroll.ScrollY;

        // Clicking the select element itself while open closes it
        if (_activeSelect?.LayoutBox != null)
        {
            var sb = _activeSelect.LayoutBox.BorderBox;
            if (docX >= sb.Left && docX <= sb.Right && docY >= sb.Top && docY <= sb.Bottom)
            {
                CloseSelectDropdown();
                return true;
            }
        }

        if (docX >= _selectDropdownRect.Left && docX <= _selectDropdownRect.Right &&
            docY >= _selectDropdownRect.Top && docY <= _selectDropdownRect.Bottom)
        {
            foreach (var (opt, rect) in _selectOptionRects)
            {
                if (docX >= rect.Left && docX <= rect.Right && docY >= rect.Top && docY <= rect.Bottom)
                {
                    foreach (var child in _activeSelect!.Children)
                    {
                        if (child is Core.Dom.Element ce && ce.TagName == "OPTION")
                        {
                            if (ce == opt) ce.SetAttribute("selected", "");
                            else ce.RemoveAttribute("selected");
                        }
                    }
                    _jsEngine.DispatchEvent(opt, "change");
                    _jsEngine.DispatchEvent(_activeSelect, "change");
                    _jsEngine.DispatchEvent(_activeSelect, "input");
                    CloseSelectDropdown();
                    _input.NeedsRedraw = true;
                    return true;
                }
            }
            // Click in dropdown padding: keep open
            return true;
        }

        // Click outside: close and continue with normal click handling
        CloseSelectDropdown();
        return false;
    }

    private void HandleDomClick(float x, float y)
    {
        if (_currentLoad == null) return;

        // Delegate to unified scroll interaction (inner scrollbar thumb/track).
        float pageX = x + _scroll.ScrollX;
        float pageY = y - _contentOffset + _scroll.ScrollY;
        if (_scrollInteraction.HandleMouseDown(pageX, pageY))
            return;

        // If a select dropdown is open, clicks either pick an option or close it.
        if (_activeSelect != null)
        {
            if (HandleSelectDropdownClick(x, y))
                return;
        }

        // Check the focused textarea's scrollbar before generic scroll containers
        if (HandleTextAreaScrollbarClick(x, y))
            return;

        // Check element scrollbar first
        if (HitTestElementScrollbar(x, y, out var sbBox, out bool isVert))
        {
            HandleElementScrollbarClick(sbBox!, isVert, x, y);
            return;
        }

        float docX = x + _scroll.ScrollX;
        float adjustedY = y - _contentOffset + _scroll.ScrollY;
        var element = PageHitTest.HitTest(_currentLoad.Document, docX, adjustedY);
        Console.WriteLine($"[Click] HitTest found: {element?.TagName} at ({docX:F1},{adjustedY:F1})");
        if (element != null)
        {
            // Handle <summary> click to toggle parent <details>
            if (element.TagName == "SUMMARY")
            {
                var detailsParent = element.ParentElement;
                while (detailsParent != null && detailsParent.TagName != "DETAILS")
                    detailsParent = detailsParent.ParentElement;
                if (detailsParent != null)
                {
                    if (detailsParent.HasAttribute("open"))
                        detailsParent.RemoveAttribute("open");
                    else
                        detailsParent.SetAttribute("open", "");
                    MarkSubtreeDirty((Acrux.Core.Dom.Element)detailsParent, DirtyFlags.AllLayout);
                    _pendingRelayout = true;
                    return;
                }
            }

            bool shouldProceed = _jsEngine.DispatchEvent(element, "click");

            // Toggle <select> dropdown before generic form-element handling
            if (element.TagName == "SELECT" && !element.HasAttribute("disabled"))
            {
                if (_activeSelect == element)
                {
                    CloseSelectDropdown();
                }
                else
                {
                    if (_focusedElement != null && _focusedElement != element)
                        _jsEngine.DispatchEvent(_focusedElement, "blur");
                    _focusedElement = element;
                    _jsEngine.DispatchEvent(element, "focus");
                    _pendingRelayout = true;
                    _activeSelect = element;
                    ComputeSelectDropdownGeometry();
                }
                return;
            }

            // Dispatch focus/blur when focused element changes
            if (element.IsFormElement)
            {
                // Disabled form controls are not focusable and ignore all mouse
                // interaction (no caret, no checkbox/radio toggle, no button press).
                if (element.HasAttribute("disabled"))
                {
                    _isSelecting = false;
                    return;
                }
                _isSelecting = false;
                if (_focusedElement != element)
                {
                    if (_focusedElement != null)
                        _jsEngine.DispatchEvent(_focusedElement, "blur");
                    _focusedElement = element;
                    // Full rebuild so the previously focused input's text returns to the
                    // main display list (it was in the overlay) and the new one goes to overlay.
                    _pendingRelayout = true;
                    _jsEngine.DispatchEvent(element, "focus");
                    _window.UpdateImeCompositionWindow();
                    _hasSelection = false;
                    _inputImeComposing = false;
                    _inputImeCompositionStr = "";
                    _inputImeCursorPos = 0;
                }
                // Update cursor position on every click (even re-click on same input)
                string val = element.Value ?? "";
                string? inputType = element.InputType?.ToLowerInvariant();
                bool isTextInput = inputType == null || inputType == "text" || inputType == "password" ||
                                   inputType == "email" || inputType == "search" || inputType == "tel" ||
                                   inputType == "url" || inputType == "number";

                // Handle internal control buttons (search clear, number spin, password reveal)
                // before cursor placement. Hit areas mirror the drawing in PaintVisitor.DrawInputElement.
                if (inputType == "search" && !string.IsNullOrEmpty(val) &&
                    !element.HasAttribute("disabled") && element.LayoutBox != null)
                {
                    var cb = element.LayoutBox.ContentBox;
                    float clearX = cb.Right - 14;
                    float clearY = cb.Top + cb.Height / 2;
                    if (Math.Abs(docX - clearX) <= 7 && Math.Abs(adjustedY - clearY) <= 7)
                    {
                        _pressedInputControl = "search-clear";
                        element.Value = "";
                        _inputCursorPos = 0;
                        _inputSelStart = -1;
                        _inputShowCursor = true;
                        _inputLastCursorBlinkTick = Environment.TickCount64;
                        _inputDragging = false;
                        _jsEngine.DispatchEvent(element, "input");
                        _pendingRelayout = true;
                        _input.NeedsRedraw = true;
                        return;
                    }
                }
                else if (inputType == "number" && !element.HasAttribute("disabled") && element.LayoutBox != null)
                {
                    var cb = element.LayoutBox.ContentBox;
                    float spinLeft = cb.Right - 20;
                    float spinRight = cb.Right - 1;
                    float spinTop = cb.Top + 1;
                    float spinBottom = cb.Bottom - 1;
                    if (docX >= spinLeft && docX <= spinRight && adjustedY >= spinTop && adjustedY <= spinBottom)
                    {
                        bool up = adjustedY < (spinTop + spinBottom) / 2;
                        _pressedInputControl = up ? "number-up" : "number-down";
                        double cur = 0;
                        double.TryParse(element.Value, out cur);
                        string? stepStr = element.GetAttribute("step");
                        double step = stepStr != null && double.TryParse(stepStr, out double sp) && sp > 0 ? sp : 1;
                        double newVal = up ? cur + step : cur - step;
                        if (double.TryParse(element.GetAttribute("min"), out double mn)) newVal = Math.Max(mn, newVal);
                        if (double.TryParse(element.GetAttribute("max"), out double mx)) newVal = Math.Min(mx, newVal);
                        element.Value = newVal.ToString("0.############");
                        _inputCursorPos = element.Value?.Length ?? 0;
                        _inputSelStart = -1;
                        _inputShowCursor = true;
                        _inputLastCursorBlinkTick = Environment.TickCount64;
                        _inputDragging = false;
                        _jsEngine.DispatchEvent(element, "input");
                        _pendingRelayout = true;
                        _input.NeedsRedraw = true;
                        return;
                    }
                }
                else if (inputType == "password" && !element.HasAttribute("disabled") && element.LayoutBox != null)
                {
                    var cb = element.LayoutBox.ContentBox;
                    float eyeX = cb.Right - 14;
                    float eyeY = cb.Top + cb.Height / 2;
                    if (Math.Abs(docX - eyeX) <= 10 && Math.Abs(adjustedY - eyeY) <= 10)
                    {
                        _pressedInputControl = "password-reveal";
                        _passwordRevealed = !_passwordRevealed;
                        _inputDragging = false;
                        _pendingRelayout = true;
                        _input.NeedsRedraw = true;
                        return;
                    }
                }

                if (element.TagName == "TEXTAREA" && element.ComputedStyle != null && element.LayoutBox != null)
                {
                    // Bottom-right corner is a resize grip (matches the grip drawn by
                    // DrawTextAreaElement). Starting a drag there resizes the textarea.
                    var tbb = element.LayoutBox.BorderBox;
                    if (element.ComputedStyle.Resize != ResizeType.None &&
                        docX >= tbb.Right - 16 && adjustedY >= tbb.Bottom - 16)
                    {
                        _textareaResizeElement = element;
                        _textareaResizeStartX = docX;
                        _textareaResizeStartY = adjustedY;
                        // The inline style width/height is interpreted as content-box
                        // size, but a border-box textarea reports border-box dims; pick
                        // whichever matches the element's box-sizing.
                        bool boxSizing = element.ComputedStyle.BoxSizing == BoxSizingType.BorderBox;
                        _textareaResizeStartW = boxSizing ? element.LayoutBox.BorderBox.Width : element.LayoutBox.ContentBox.Width;
                        _textareaResizeStartH = boxSizing ? element.LayoutBox.BorderBox.Height : element.LayoutBox.ContentBox.Height;
                        _pressedInputControl = "textarea-resize";
                        _inputDragging = false;
                        return;
                    }
                    // Multi-line textarea: map the click to a visual line/column and
                    // convert back to a flat character index.
                    _inputCursorPos = GetTextAreaCharIndex(element, docX, adjustedY);
                    _textareaUserScroll = false;
                }
                else if (isTextInput && element.TagName == "INPUT" && !string.IsNullOrEmpty(val) && element.ComputedStyle != null && element.LayoutBox != null)
                {
                    float cbLeft = element.LayoutBox.ContentBox.Left;
                    float clickX = docX - cbLeft - 2;
                    float fontSize = element.ComputedStyle.FontSize > 0 ? element.ComputedStyle.FontSize : 14;
                    string fontFamily = element.ComputedStyle.FontFamily ?? "Arial";
                    // Password dots are wider than the real characters, so the caret
                    // mapping must measure the same masked text the painter draws,
                    // otherwise clicks drift horizontally.
                    string displayVal = val;
                    if (inputType == "password" && !_passwordRevealed)
                        displayVal = new string('●', val.Length);
                    // Account for horizontal scroll offset so click targeting works
                    // when text inside the input has been scrolled.
                    UpdateInputScrollOffset(element);
                    _inputCursorPos = GetFormInputCharIndex(displayVal, clickX + _inputScrollOffset, fontSize, fontFamily);
                    UpdateInputScrollOffset(element);
                }
                else
                {
                    _inputCursorPos = val.Length;
                }
                _inputSelStart = -1;
                _inputShowCursor = true;
                _inputLastCursorBlinkTick = Environment.TickCount64;
                _inputDragging = isTextInput && element.TagName == "INPUT";

                // Checkbox/radio toggle
                if (inputType == "checkbox")
                {
                    if (element.HasAttribute("checked"))
                        element.RemoveAttribute("checked");
                    else
                        element.SetAttribute("checked", "");
                    _jsEngine.DispatchEvent(element, "change");
                    _input.NeedsRedraw = true;
                }
                else if (inputType == "radio")
                {
                    if (!element.HasAttribute("checked"))
                    {
                        // A radio group is all same-name radios sharing the same
                        // form owner (radios outside any <form> share the "no form"
                        // group). Uncheck the whole group so only one can be selected.
                        UncheckRadioGroup(element);
                        element.SetAttribute("checked", "");
                        _jsEngine.DispatchEvent(element, "change");
                        _input.NeedsRedraw = true;
                    }
                }

                // BUTTON element: press feedback + submit/reset based on the type attribute.
                // Buttons are form elements, so they enter this branch instead of the
                // generic else-branch below; without this block a <button> (type defaults
                // to "submit" in HTML, but the attribute may be null) would get no feedback.
                if (element.TagName == "BUTTON")
                {
                    _pressedButton = element;
                    _pendingRelayout = true;
                    string? btnType = element.GetAttribute("type")?.ToLowerInvariant();
                    if (btnType == "submit" || btnType == null)
                    {
                        var form = FindParentForm(element);
                        if (form != null)
                            form.Submit();
                    }
                    else if (btnType == "reset")
                    {
                        var form = FindParentForm(element);
                        if (form != null)
                            form.Reset();
                    }
                }
                else if (inputType == "submit" || inputType == "image")
                {
                    _pressedButton = element;
                    _pendingRelayout = true;
                    var form = FindParentForm(element);
                    if (form != null)
                        form.Submit();
                }
                else if (inputType == "reset")
                {
                    _pressedButton = element;
                    _pendingRelayout = true;
                    var form = FindParentForm(element);
                    if (form != null)
                        form.Reset();
                }
            }
            else
            {
                // Check if clicking a BUTTON element (for submit type)
                if (element.TagName == "BUTTON")
                {
                    _pressedButton = element;
                    _pendingRelayout = true;
                    string? btnType = element.GetAttribute("type")?.ToLowerInvariant();
                    if (btnType == "submit" || btnType == null)
                    {
                        var form = FindParentForm(element);
                        if (form != null)
                            form.Submit();
                    }
                    else if (btnType == "reset")
                    {
                        var form = FindParentForm(element);
                        if (form != null)
                            form.Reset();
                    }
                }

                _isSelecting = false;
                if (_focusedElement != null)
                {
                    _jsEngine.DispatchEvent(_focusedElement, "blur");
                    _focusedElement = null;
                    _pendingRelayout = true;
                }
                _inputSelStart = -1;
                _inputCursorPos = 0;
                _inputImeComposing = false;
                _inputImeCompositionStr = "";
                _inputImeCursorPos = 0;
                _inputDragging = false;

                // Start text selection on non-interactive elements
                if (shouldProceed && !IsInteractiveElement(element))
                {
                    _isSelecting = true;
                    _hasSelection = false;
                    float dlX = x + _scroll.ScrollX;
                    float dlY = y + _scroll.ScrollY;
                    var pt = HitTestTextPosition(_currentLoad.Document, dlX, dlY);
                    _selAnchor = pt;
                    _selFocus = pt;
                }
                else
                {
                    _hasSelection = false;
                }
            }

            if (shouldProceed)
                NavigateForLinkClick(element);
        }
        else
        {
            if (_focusedElement != null)
            {
                _jsEngine.DispatchEvent(_focusedElement, "blur");
                _focusedElement = null;
                _pendingRelayout = true;
            }
            _inputSelStart = -1;
            _inputCursorPos = 0;
            _inputImeComposing = false;
            _inputImeCompositionStr = "";
            _inputImeCursorPos = 0;
            _hasSelection = false;
        }
        UpdateImeTarget();
    }

    private static bool IsInteractiveElement(Core.Dom.Element element)
    {
        return element.TagName is "A" or "BUTTON" or "INPUT" or "TEXTAREA" or "SELECT"
            || element.GetAttribute("onclick") != null
            || element.GetAttribute("role") == "button";
    }

    private void NavigateForLinkClick(Core.Dom.Element element)
    {
        // Walk up the element tree to find an <a> tag
        var current = element;
        while (current != null)
        {
            if (current.TagName == "A")
            {
                var href = current.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    var url = href;
                    // Resolve relative URLs against current base URL
                    if (!url.Contains("://") && !url.StartsWith("//") && !string.IsNullOrEmpty(_currentBaseUrl))
                    {
                        try
                        {
                            var baseUri = new Uri(_currentBaseUrl.EndsWith('/') ? _currentBaseUrl : _currentBaseUrl + '/');
                            url = new Uri(baseUri, url).ToString();
                        }
                        catch { }
                    }
                    else if (url.StartsWith("//"))
                    {
                        // Protocol-relative URL
                        try
                        {
                            var baseUri = new Uri(_currentBaseUrl ?? "https://example.com");
                            url = baseUri.Scheme + ":" + url;
                        }
                        catch { }
                    }

                    Console.WriteLine($"Link click navigating to: {url}");
                    _chrome.NavigateToUrl(url);
                    return;
                }
            }

            // Check target="_blank" on anchor or area
            var target = current.GetAttribute("target");
            if (target == "_blank")
            {
                var href = current.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    Console.WriteLine($"New tab link: {href}");
                    // For now, navigate in same tab
                    var url = href;
                    if (!url.Contains("://") && !string.IsNullOrEmpty(_currentBaseUrl))
                    {
                        try
                        {
                            var baseUri = new Uri(_currentBaseUrl.EndsWith('/') ? _currentBaseUrl : _currentBaseUrl + '/');
                            url = new Uri(baseUri, url).ToString();
                        }
                        catch { }
                    }
                    _chrome.NavigateToUrl(url);
                    return;
                }
            }

            current = current.ParentElement;
        }
    }

    #region DOM Event Handlers

    private Core.Dom.Element? _hoveredElement;
    private bool _passwordRevealed;
    private string? _pressedInputControl;
    private Core.Dom.Element? _pressedButton;
    private Core.Dom.Element? _textareaResizeElement;
    private float _textareaResizeStartX, _textareaResizeStartY;
    private float _textareaResizeStartW, _textareaResizeStartH;
    private float _textareaScrollY;
    private Core.Dom.Element? _activeSelect;
    private SKRect _selectDropdownRect;
    private readonly List<(Core.Dom.Element Option, SKRect Rect)> _selectOptionRects = new();
    private int _selectHoverIndex = -1;

    private void HandleDomKeyDown(char charCode, Key key, bool repeat)
    {
        if (_currentLoad == null) return;

        var target = _focusedElement ?? _currentLoad.Document.Body ?? _currentLoad.Document.DocumentElement;
        if (target == null) return;

        var keyStr = KeyToJsKey(charCode, key);
        var codeStr = KeyToJsCode(key);

        var host = _jsEngine.GetElementHost(target);
        var evt = new ScriptEvent("keydown", host)
        {
            key = keyStr,
            code = codeStr,
            ctrlKey = IsCtrlPressed(),
            shiftKey = IsShiftPressed(),
            altKey = IsAltPressed(),
            repeat = repeat,
            keyCode = (int)key,
            which = (int)key,
            bubbles = true,
            cancelable = true
        };
        _jsEngine.DispatchEvent(target, evt);
    }

    private void HandleDomKeyUp(char charCode, Key key, bool repeat)
    {
        if (_currentLoad == null) return;

        var target = _focusedElement ?? _currentLoad.Document.Body ?? _currentLoad.Document.DocumentElement;
        if (target == null) return;

        var keyStr = KeyToJsKey(charCode, key);
        var codeStr = KeyToJsCode(key);

        var host = _jsEngine.GetElementHost(target);
        var evt = new ScriptEvent("keyup", host)
        {
            key = keyStr,
            code = codeStr,
            ctrlKey = IsCtrlPressed(),
            shiftKey = IsShiftPressed(),
            altKey = IsAltPressed(),
            repeat = repeat,
            keyCode = (int)key,
            which = (int)key,
            bubbles = true,
            cancelable = true
        };
        _jsEngine.DispatchEvent(target, evt);
    }

    private void HandleDomChar(char charCode)
    {
        if (_currentLoad == null || charCode == '\0' || charCode < 32) return;

        var target = _focusedElement ?? _currentLoad.Document.Body ?? _currentLoad.Document.DocumentElement;
        if (target == null) return;

        var keyStr = new string(charCode, 1);

        var host = _jsEngine.GetElementHost(target);
        var evt = new ScriptEvent("keypress", host)
        {
            key = keyStr,
            code = keyStr,
            ctrlKey = IsCtrlPressed(),
            shiftKey = IsShiftPressed(),
            altKey = IsAltPressed(),
            keyCode = charCode,
            which = charCode,
            bubbles = true,
            cancelable = true
        };
        _jsEngine.DispatchEvent(target, evt);
    }

    private bool HandleFormInputKey(char charCode, Key key, bool shift)
    {
        if (_focusedElement == null || !_focusedElement.IsTextEditable)
            return false;

        // Any keystroke re-enables caret-following scrolling for textareas,
        // overriding a previous wheel/thumb scroll of the viewport.
        if (_focusedElement.TagName == "TEXTAREA")
            _textareaUserScroll = false;

        string? inputType = _focusedElement.InputType?.ToLowerInvariant();
        bool isTextInput = inputType == null || inputType == "text" || inputType == "password" ||
                           inputType == "email" || inputType == "search" || inputType == "tel" ||
                           inputType == "url" || inputType == "number";
        if (!isTextInput)
            return false;

        bool isReadOnly = _focusedElement.HasAttribute("readonly");
        bool isDisabled = _focusedElement.HasAttribute("disabled");

        string value = _focusedElement.Value ?? "";
        int cursorPos = _inputCursorPos;
        int selStart = _inputSelStart;

        // Ctrl+A: select all
        if (IsCtrlPressed() && charCode == 1)
        {
            _inputSelStart = 0;
            _inputCursorPos = value.Length;
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.Left)
        {
            if (cursorPos <= 0) return true;
            if (shift)
            {
                if (selStart < 0) _inputSelStart = cursorPos;
                _inputCursorPos--;
            }
            else
            {
                if (selStart >= 0)
                    _inputCursorPos = Math.Min(selStart, cursorPos);
                else
                    _inputCursorPos--;
                _inputSelStart = -1;
            }
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.Right)
        {
            if (cursorPos >= value.Length) return true;
            if (shift)
            {
                if (selStart < 0) _inputSelStart = cursorPos;
                _inputCursorPos++;
            }
            else
            {
                if (selStart >= 0)
                    _inputCursorPos = Math.Max(selStart, cursorPos);
                else
                    _inputCursorPos++;
                _inputSelStart = -1;
            }
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.Home)
        {
            if (shift)
            {
                if (selStart < 0) _inputSelStart = cursorPos;
                _inputCursorPos = 0;
            }
            else
            {
                _inputCursorPos = 0;
                _inputSelStart = -1;
            }
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.End)
        {
            if (shift)
            {
                if (selStart < 0) _inputSelStart = cursorPos;
                _inputCursorPos = value.Length;
            }
            else
            {
                _inputCursorPos = value.Length;
                _inputSelStart = -1;
            }
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.Escape)
        {
            BlurFocusedElement();
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.Up || key == Key.Down)
        {
            if (_focusedElement.TagName != "TEXTAREA")
                return false;
            string taVal = _focusedElement.Value ?? "";
            var taStyle = _focusedElement.ComputedStyle;
            var taBox = _focusedElement.LayoutBox;
            if (taStyle == null || taBox == null) return true;
            float fs = taStyle.FontSize > 0 ? taStyle.FontSize : 14;
            float usableW = Math.Max(1, taBox.ContentBox.Width - 4);
            string ff = taStyle.FontFamily ?? "Arial";
            var lines = Core.Layout.TextWrapHelper.WrapToLines(taVal, ff, fs, usableW);
            if (lines.Count == 0) return true;
            var (curLine, curCol) = Core.Layout.TextWrapHelper.GetLineColumn(lines, cursorPos);
            int targetLine = key == Key.Up ? curLine - 1 : curLine + 1;
            if (targetLine < 0 || targetLine >= lines.Count) return true;
            var curLn = lines[curLine];
            var tgtLn = lines[targetLine];
            string curText = taVal.Substring(curLn.Start, curLn.Length);
            string tgtText = taVal.Substring(tgtLn.Start, tgtLn.Length);
            int curColClamped = Math.Min(curCol, curText.Length);
            float curX = TextMeasurer.Instance?.MeasureText(curText[..curColClamped], ff, fs)
                         ?? curColClamped * fs * 0.55f;
            int targetCol = GetFormInputCharIndex(tgtText, curX, fs, ff);
            int newPos = Core.Layout.TextWrapHelper.GetFlatIndex(lines, targetLine, targetCol);
            if (shift)
            {
                if (selStart < 0) _inputSelStart = cursorPos;
                _inputCursorPos = newPos;
            }
            else
            {
                _inputCursorPos = newPos;
                _inputSelStart = -1;
            }
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.Enter)
        {
            if (_focusedElement.TagName == "TEXTAREA")
            {
                // Insert newline in textarea
                string val = _focusedElement.Value ?? "";
                int curPos = _inputCursorPos;
                if (_inputSelStart >= 0 && _inputSelStart != curPos)
                {
                    int a = Math.Min(_inputSelStart, curPos);
                    int b = Math.Max(_inputSelStart, curPos);
                    val = val[..a] + '\n' + val[b..];
                    _inputCursorPos = a + 1;
                    _inputSelStart = -1;
                }
                else
                {
                    val = val[..curPos] + '\n' + val[curPos..];
                    _inputCursorPos = curPos + 1;
                }
                _focusedElement.Value = val;
                _inputShowCursor = true;
                _inputLastCursorBlinkTick = Environment.TickCount64;
                _input.NeedsRedraw = true;
                return true;
            }
            // Submit form on Enter in text input
            var form = FindParentForm(_focusedElement);
            if (form != null)
                form.Submit();
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.Tab)
        {
            BlurFocusedElement();
            _input.NeedsRedraw = true;
            return true;
        }

        if (isReadOnly || isDisabled)
            return true;

        if (key == Key.Backspace)
        {
            if (selStart >= 0 && selStart != cursorPos)
            {
                int a = Math.Min(selStart, cursorPos);
                int b = Math.Max(selStart, cursorPos);
                value = value[..a] + value[b..];
                _inputCursorPos = a;
                _inputSelStart = -1;
            }
            else if (cursorPos > 0)
            {
                value = value[..(cursorPos - 1)] + value[cursorPos..];
                _inputCursorPos--;
            }
            _focusedElement.Value = value;
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            return true;
        }

        if (key == Key.Delete)
        {
            if (selStart >= 0 && selStart != cursorPos)
            {
                int a = Math.Min(selStart, cursorPos);
                int b = Math.Max(selStart, cursorPos);
                value = value[..a] + value[b..];
                _inputCursorPos = a;
                _inputSelStart = -1;
            }
            else if (cursorPos < value.Length)
            {
                value = value[..cursorPos] + value[(cursorPos + 1)..];
            }
            _focusedElement.Value = value;
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            return true;
        }

        // Printable character (OnDomChar already dispatched this to JS)
        if (key == Key.Unknown && charCode >= 32)
        {
            // Tel input: only allow digits (0-9)
            if (inputType == "tel" && (charCode < '0' || charCode > '9'))
                return true;
            // maxlength enforcement
            string? maxlenStr = _focusedElement.GetAttribute("maxlength");
            if (int.TryParse(maxlenStr, out int maxlen) && maxlen >= 0)
            {
                int lenAfter = value.Length;
                if (selStart >= 0 && selStart != cursorPos)
                    lenAfter -= Math.Abs(cursorPos - selStart);
                if (lenAfter >= maxlen)
                    return true;
            }
            if (selStart >= 0 && selStart != cursorPos)
            {
                int a = Math.Min(selStart, cursorPos);
                int b = Math.Max(selStart, cursorPos);
                value = value[..a] + charCode + value[b..];
                _inputCursorPos = a + 1;
                _inputSelStart = -1;
            }
            else
            {
                value = value[..cursorPos] + charCode + value[cursorPos..];
                _inputCursorPos++;
            }
            _focusedElement.Value = value;
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
            _jsEngine.DispatchEvent(_focusedElement, "input");
            return true;
        }

        return false;
    }

    private static FormElement? FindParentForm(Element element)
    {
        var el = element.ParentElement;
        while (el != null)
        {
            if (el is FormElement form)
                return form;
            el = el.ParentElement;
        }
        return null;
    }

    // Unchecks every radio that shares the same name AND the same form owner
    // (radios outside any <form> form a group of their own), leaving the clicked
    // one as the only selected member of the group.
    private void UncheckRadioGroup(Core.Dom.Element element)
    {
        string? name = element.GetAttribute("name");
        if (string.IsNullOrEmpty(name)) return;
        var doc = _currentLoad?.Document;
        if (doc == null) return;
        var form = FindParentForm(element);
        foreach (var el in doc.GetElementsByTagName("input"))
        {
            if (ReferenceEquals(el, element)) continue;
            if (el.GetAttribute("type") == "radio" && el.GetAttribute("name") == name &&
                ReferenceEquals(FindParentForm(el), form))
                el.RemoveAttribute("checked");
        }
    }

    // Writes width/height in px into the element's inline style, preserving any
    // other declarations. The engine treats inline style width/height as the
    // content-box size, so the rendered border-box grows exactly by the drag delta.
    private static void SetElementInlineSize(Core.Dom.Element element, float width, float height)
    {
        string? existing = element.GetAttribute("style");
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(existing))
        {
            foreach (var decl in existing.Split(';'))
            {
                var trimmed = decl.Trim();
                if (trimmed.Length == 0) continue;
                int colon = trimmed.IndexOf(':');
                if (colon > 0)
                {
                    string prop = trimmed[..colon].Trim().ToLowerInvariant();
                    if (prop == "width" || prop == "height") continue;
                }
                sb.Append(trimmed).Append("; ");
            }
        }
        sb.Append("width: ").Append(width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("px; ")
          .Append("height: ").Append(height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("px;");
        element.SetAttribute("style", sb.ToString());
        DirtyState.AddSelf(element, DirtyFlags.AllLayout);
        DirtyState.AddChildren(element, DirtyFlags.AllLayout);
    }

    private void BlurFocusedElement()
    {
        if (_focusedElement != null)
        {
            _jsEngine.DispatchEvent(_focusedElement, "blur");
            _focusedElement = null;
            // Full rebuild so the blurred input's text (which lived in the overlay)
            // is painted back into the main display list.
            _pendingRelayout = true;
        }
        _inputSelStart = -1;
        _inputCursorPos = 0;
        _inputImeComposing = false;
        _inputImeCompositionStr = "";
        _inputImeCursorPos = 0;
        UpdateImeTarget();
        _input.NeedsRedraw = true;
    }

    private bool HandleScrollContainerWheel(double deltaX, double deltaY, float mouseX, float mouseY)
    {
        if (_currentLoad == null) return false;
        _lastInputTimeTick = Environment.TickCount64;

        // Convert screen coords to page coords
        float docX = mouseX + _scroll.ScrollX;
        float docY = mouseY - _contentOffset + _scroll.ScrollY;

        // Focused textarea: wheel scrolls its own content
        if (deltaY != 0 && _focusedElement != null && _focusedElement.TagName == "TEXTAREA" &&
            _focusedElement.LayoutBox != null)
        {
            var tcb = _focusedElement.LayoutBox.ContentBox;
            if (docX >= tcb.Left && docX <= tcb.Right && docY >= tcb.Top && docY <= tcb.Bottom)
            {
                _textareaUserScroll = true;
                _textareaScrollY += (float)(-deltaY / 120.0 * 40.0);
                _input.NeedsRedraw = true;
                return true;
            }
        }

        // Find deepest element at cursor position
        var element = PageHitTest.HitTest(_currentLoad.Document, docX, docY);

        // Walk up from hit element to find nearest scroll container with overflow
        var el = element;
        while (el != null)
        {
            var box = el.LayoutBox;
            if (box != null && box.IsScrollContainer &&
                box.ContentBox.Height > 0 && box.ContentBox.Width > 0 &&
                (box.ScrollContentHeight > box.ContentBox.Height || box.ScrollContentWidth > box.ContentBox.Width))
            {
                // Direct scroll update — immediate, no animation layer
                float maxScrollY = Math.Max(0, box.ScrollContentHeight - box.ContentBox.Height);
                float maxScrollX = Math.Max(0, box.ScrollContentWidth - box.ContentBox.Width);

                if (deltaY != 0)
                {
                    float dy = (float)(-deltaY / 120.0 * 60.0);
                    box.ScrollY = Math.Clamp(box.ScrollY + dy, 0, maxScrollY);
                    box.IsSmoothScrollingY = false;
                    box.ScrollVelY = 0;
                }
                if (deltaX != 0)
                {
                    float dx = (float)(-deltaX / 120.0 * 60.0);
                    box.ScrollX = Math.Clamp(box.ScrollX + dx, 0, maxScrollX);
                    box.IsSmoothScrollingX = false;
                    box.ScrollVelX = 0;
                }
                // Element scroll without layout — paint-only rebuild suffices.
                MarkElementScrollDirty(box);
                _input.NeedsRedraw = true;
                return true;
            }
            el = el.ParentElement;
        }
        return false;
    }

    /// <summary>
    /// Mark that element scrolling changed (see <see cref="_elementScrollDirty"/>):
    /// accumulate the scrolled container's op-space padding box and request a frame.
    /// </summary>
    private void MarkElementScrollDirty(LayoutBox box)
    {
        var pb = box.PaddingBox;
        _pendingElementScrollRects.Add(new SKRect(pb.Left, pb.Top + _contentOffset, pb.Right, pb.Bottom + _contentOffset));
        _pendingElementScrollBoxes.Add(box);
        _elementScrollDirty = true;
        _input.NeedsRedraw = true;
    }

    /// <summary>
    /// Element-scroll fast path for a single NON-layered scroll container: re-paints
    /// only the container's subtree (O(container), no O(document) layer-tree rebuild)
    /// and swaps its ops into the existing page display list, replacing the stale
    /// ones. Requires an OPAQUE container background so the freshly painted subtree
    /// fully covers the container's previous pixels — re-rasterized tiles are
    /// transparent where ops are missing and the retained surface would otherwise
    /// ghost the old content through the gaps. Nested eligible scrollers keep their
    /// own live layers (never double-painted). Returns false when the fast path
    /// cannot apply and the caller must fall back to the full-document rebuild.
    /// </summary>
    private bool TrySubtreeRepaintScrollContainer(LayoutBox box, int lw, int vh)
    {
        if (_currentLoad == null || _displayList == null) return false;
        var element = box.Dimensions?.Element;
        var style = element?.ComputedStyle;
        if (element == null || style == null) return false;
        if (style.BackgroundColor is not { Alpha: >= 255 }) return false;

        float scale = _dpiScale * _renderingSettings.ResolutionScale;
        var cb = box.ContentBox;
        // The removal region is the container's COLUMN STRIP: its X extent spans the
        // content box (narrow, so neighboring / full-width elements at other columns
        // are NOT swallowed), while both extents cover the ENTIRE scrollable content
        // (ops carry UNTRANSLATED layout positions, so scrolled-out content still
        // lives inside the box's scroll bounds). Removing all of them — content
        // scrolled above/below/left/right of the visible box — is what stops stale
        // content from rasterizing unclipped ("floating out" over the page).
        const float yMargin = 512f;
        const float xBleed = 8f;
        float regionLeft = cb.Left - xBleed;
        float regionTop = cb.Top + _contentOffset - yMargin;
        float regionRight = Math.Max(cb.Right, cb.Left + box.ScrollContentWidth) + xBleed;
        float regionBottom = Math.Max(cb.Bottom, cb.Top + box.ScrollContentHeight) + _contentOffset + yMargin;
        var region = new SKRect(regionLeft, regionTop, regionRight, regionBottom);

        // Drop the container's stale ops (background, borders, content, scrollbar)
        // so the re-painted subtree replaces them instead of stacking on top.
        _displayList.RemoveOpsContainedIn(region);

        var sub = new PaintVisitor(_contentOffset, _sharedTypefaceCache,
            _sharedImageCache, _fontFamilies, _currentBaseUrl, lw, vh)
        {
            PhysicalScale = scale,
        };
        sub.SetSkipInputTextOverlay(true);
        sub.PaintElementSubtree(element, _currentLoad.Document, region);
        var subList = sub.GetDisplayList();
        if (subList.Count == 0)
        {
            // The subtree produced nothing (shouldn't happen for a painted
            // container) — do not commit a blank region; let the caller rebuild.
            return false;
        }
        subList.SortByZIndex();
        _displayList.AddRange(subList.EnumerateOps());
        _displayList.SortByZIndex();
        return true;
    }

    private void UpdateElementSmoothScrolls(float dt)
    {
        if (_currentLoad == null) return;
        // Clamp to ~15fps so low frame rates don't slow the physics; the spring
        // stays stable (K·dt² < 1) within this bound and the velocity decay is exact.
        dt = Math.Min(dt, 0.067f);
        bool anyChanged = false;
        // Decay lambda matches the element wheel velocity scale (10), so a wheel
        // notch glides ~one notch over ~0.3 s with a clearly visible ease-out.
        // The scrollIntoView smooth spring is a separate branch and is unaffected.
        const float decayLambda = 10f;
        const float minVel = 1f;
        const float bounceK = 150f, bounceDamp = 25f;
        const float snapK = 60f, snapDamp = 16f;

        var pending = new Queue<LayoutBox>();
        pending.Enqueue(_currentLoad.Document.DocumentElement?.LayoutBox);
        while (pending.Count > 0)
        {
            var box = pending.Dequeue();
            if (box == null) continue;
            if (box.IsScrollContainer)
            {
                bool changed = false;
                float maxY = Math.Max(0, box.ScrollContentHeight - box.ContentBox.Height);
                float maxX = Math.Max(0, box.ScrollContentWidth - box.ContentBox.Width);

                // ── Vertical ──
                if (box.IsSmoothScrollingY)
                {
                    // Target-seeking spring (scrollTo/scrollIntoView smooth).
                    // Takes priority over bounce/decay when a programmatic target is set.
                    if (!float.IsNaN(box.TargetScrollY) && box.TargetScrollY >= 0 && !box.IsBouncingY)
                    {
                        const float springK = 120f, springDamp = 22f;
                        float diff = box.TargetScrollY - box.ScrollY;
                        box.ScrollVelY += diff * springK * dt;
                        box.ScrollVelY *= MathF.Exp(-springDamp * dt);
                        box.ScrollY += box.ScrollVelY * dt;
                        if (MathF.Abs(diff) < 0.5f && MathF.Abs(box.ScrollVelY) < 2f)
                        {
                            box.ScrollY = box.TargetScrollY;
                            box.TargetScrollY = float.NaN;
                            box.ScrollVelY = 0;
                            box.IsSmoothScrollingY = false;
                        }
                        else changed = true;
                    }
                    else if (box.IsBouncingY)
                    {
                        float boundary = box.ScrollY < 0 ? 0 : maxY;
                        float diff = boundary - box.ScrollY;
                        float force = diff * bounceK - box.ScrollVelY * bounceDamp;
                        box.ScrollVelY += force * dt;
                        box.ScrollVelY *= 0.97f;
                        box.ScrollY += box.ScrollVelY * dt;
                        if (Math.Abs(diff) < 0.5f && Math.Abs(box.ScrollVelY) < 5f)
                        {
                            box.ScrollY = boundary;
                            box.ScrollVelY = 0;
                            box.IsBouncingY = false;
                            box.IsSmoothScrollingY = false;
                        }
                        else changed = true;
                    }
                    else if (Math.Abs(box.ScrollVelY) > minVel)
                    {
                        box.ScrollVelY *= MathF.Exp(-decayLambda * dt);
                        box.ScrollY += box.ScrollVelY * dt;
                        if (box.ScrollY < 0) { box.IsBouncingY = true; box.ScrollVelY *= 0.5f; }
                        else if (box.ScrollY > maxY) { box.IsBouncingY = true; box.ScrollVelY *= 0.5f; }
                        else if (Math.Abs(box.ScrollVelY) < minVel) { box.ScrollVelY = 0; box.IsSmoothScrollingY = false; }
                        else changed = true;
                    }
                    else
                    {
                        box.IsSmoothScrollingY = false;
                    }
                }
                else
                {
                    box.ScrollVelY *= 0.8f;
                }

                // ── Horizontal ──
                if (box.IsSmoothScrollingX)
                {
                    // X target-seeking spring
                    if (!float.IsNaN(box.TargetScrollX) && box.TargetScrollX >= 0 && !box.IsBouncingX)
                    {
                        const float springK = 120f, springDamp = 22f;
                        float diff = box.TargetScrollX - box.ScrollX;
                        box.ScrollVelX += diff * springK * dt;
                        box.ScrollVelX *= MathF.Exp(-springDamp * dt);
                        box.ScrollX += box.ScrollVelX * dt;
                        if (MathF.Abs(diff) < 0.5f && MathF.Abs(box.ScrollVelX) < 2f)
                        {
                            box.ScrollX = box.TargetScrollX;
                            box.TargetScrollX = float.NaN;
                            box.ScrollVelX = 0;
                            box.IsSmoothScrollingX = false;
                        }
                        else changed = true;
                    }
                    else if (box.IsBouncingX)
                    {
                        float boundary = box.ScrollX < 0 ? 0 : maxX;
                        float diff = boundary - box.ScrollX;
                        float force = diff * bounceK - box.ScrollVelX * bounceDamp;
                        box.ScrollVelX += force * dt;
                        box.ScrollVelX *= 0.97f;
                        box.ScrollX += box.ScrollVelX * dt;
                        if (Math.Abs(diff) < 0.5f && Math.Abs(box.ScrollVelX) < 5f)
                        {
                            box.ScrollX = boundary;
                            box.ScrollVelX = 0;
                            box.IsBouncingX = false;
                            box.IsSmoothScrollingX = false;
                        }
                        else changed = true;
                    }
                    else if (Math.Abs(box.ScrollVelX) > minVel)
                    {
                        box.ScrollVelX *= MathF.Exp(-decayLambda * dt);
                        box.ScrollX += box.ScrollVelX * dt;
                        if (box.ScrollX < 0) { box.IsBouncingX = true; box.ScrollVelX *= 0.5f; }
                        else if (box.ScrollX > maxX) { box.IsBouncingX = true; box.ScrollVelX *= 0.5f; }
                        else if (Math.Abs(box.ScrollVelX) < minVel) { box.ScrollVelX = 0; box.IsSmoothScrollingX = false; }
                        else changed = true;
                    }
                    else
                    {
                        box.IsSmoothScrollingX = false;
                    }
                }
                else
                {
                    box.ScrollVelX *= 0.8f;
                }

                if (changed)
            {
                anyChanged = true;
                MarkElementScrollDirty(box);
            }
            }
            foreach (var child in box.Children)
                pending.Enqueue(child);
        }
        if (anyChanged) _elementScrollDirty = true;
    }

    private void HandleElementScrollbarClick(LayoutBox box, bool isVertical, float x, float y)
    {
        float docX = x + _scroll.ScrollX;
        float docY = y - _contentOffset + _scroll.ScrollY;
        var pb = box.PaddingBox;

        if (isVertical)
        {
            // Compute thumb position (same as DrawScrollbar). The vertical track
            // shrinks when a horizontal bar is present, matching the painter.
            bool hasHorz = box.ScrollContentWidth > box.ContentBox.Width;
            float trackHeight = Math.Max(0, pb.Height - (hasHorz ? 12f : 0));
            float thumbRatio = box.ContentBox.Height / Math.Max(1, box.ScrollContentHeight);
            float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
            float scrollRange = Math.Max(1, box.ScrollContentHeight - box.ContentBox.Height);
            float thumbTop = scrollRange > 0 ? (trackHeight - thumbHeight) * (box.ScrollY / scrollRange) : 0;
            float trackY = pb.Top;

            float localY = docY - trackY;
            if (localY >= thumbTop && localY <= thumbTop + thumbHeight)
            {
                // Start thumb drag
                _elemScrollDragBox = box;
                _elemScrollDragVertical = true;
                _elemScrollDragStart = localY;
                _elemScrollDragStartScroll = box.ScrollY;
            }
            else
            {
                // Page up/down
                float pageSize = box.ContentBox.Height * 0.9f;
                float delta = localY < thumbTop ? -pageSize : pageSize;
                box.ScrollVelY += delta * 1.5f;
                box.IsSmoothScrollingY = true;
                _pendingRelayout = true;
            }
        }
        else
        {
            // The horizontal track shrinks when a vertical bar is present,
            // matching the painter.
            bool hasVert = box.ScrollContentHeight > box.ContentBox.Height;
            float trackWidth = Math.Max(0, pb.Width - (hasVert ? 12f : 0));
            float thumbRatio = box.ContentBox.Width / Math.Max(1, box.ScrollContentWidth);
            float thumbWidth = Math.Max(20, trackWidth * thumbRatio);
            float scrollRange = Math.Max(1, box.ScrollContentWidth - box.ContentBox.Width);
            float thumbLeft = scrollRange > 0 ? (trackWidth - thumbWidth) * (box.ScrollX / scrollRange) : 0;

            float localX = docX - pb.Left;
            if (localX >= thumbLeft && localX <= thumbLeft + thumbWidth)
            {
                _elemScrollDragBox = box;
                _elemScrollDragVertical = false;
                _elemScrollDragStart = localX;
                _elemScrollDragStartScroll = box.ScrollX;
            }
            else
            {
                float pageSize = box.ContentBox.Width * 0.9f;
                float delta = localX < thumbLeft ? -pageSize : pageSize;
                box.ScrollVelX += delta * 1.5f;
                box.IsSmoothScrollingX = true;
                _pendingRelayout = true;
            }
        }
    }

    private void HandleDomMouseMove(float x, float y)
    {
        _lastInputTimeTick = Environment.TickCount64;
        if (_currentLoad == null) return;

        // Delegate to unified scroll interaction (thumb drag tracking).
        {
            float pageX = x + _scroll.ScrollX;
            float pageY = y - _contentOffset + _scroll.ScrollY;
            _scrollInteraction.HandleMouseMove(pageX, pageY);
        }

        // Textarea scrollbar thumb drag update
        if (_textareaScrollDragging && _focusedElement != null && _focusedElement.TagName == "TEXTAREA" &&
            _focusedElement.LayoutBox != null)
        {
            float dragDocY = y - _contentOffset + _scroll.ScrollY;
            var cb = _focusedElement.LayoutBox.ContentBox;
            var (usableH, totalH, _, maxScrollY, _) = GetTextAreaMetrics(_focusedElement);
            if (maxScrollY > 0)
            {
                float trackHeight = cb.Height;
                float thumbHeight = Math.Max(20, trackHeight * Math.Min(1, usableH / Math.Max(1, totalH)));
                float localY = dragDocY - cb.Top;
                float delta = (localY - _textareaScrollDragStartY) / Math.Max(1, trackHeight - thumbHeight) * maxScrollY;
                _textareaUserScroll = true;
                _textareaScrollY = Math.Clamp(_textareaScrollDragStartScroll + delta, 0, maxScrollY);
            }
            _input.NeedsRedraw = true;
            return;
        }

        // Element scrollbar drag update
        if (_elemScrollDragBox != null)
        {
            float dragDocX = x + _scroll.ScrollX;
            float dragDocY = y - _contentOffset + _scroll.ScrollY;
            var pb = _elemScrollDragBox.PaddingBox;
            if (_elemScrollDragVertical)
            {
                float trackHeight = pb.Height;
                float thumbHeight = Math.Max(20, trackHeight * (_elemScrollDragBox.ContentBox.Height / Math.Max(1, _elemScrollDragBox.ScrollContentHeight)));
                float scrollRange = Math.Max(1, _elemScrollDragBox.ScrollContentHeight - _elemScrollDragBox.ContentBox.Height);
                float localY = dragDocY - pb.Top;
                float delta = (localY - _elemScrollDragStart) / (trackHeight - thumbHeight) * scrollRange;
                float target = Math.Clamp(_elemScrollDragStartScroll + delta, 0, scrollRange);
                _elemScrollDragBox.IsSmoothScrollingY = false;
                _elemScrollDragBox.ScrollVelY = 0;
                _elemScrollDragBox.ScrollY = target;
                MarkElementScrollDirty(_elemScrollDragBox);
            }
            else
            {
                float trackWidth = pb.Width;
                float thumbWidth = Math.Max(20, trackWidth * (_elemScrollDragBox.ContentBox.Width / Math.Max(1, _elemScrollDragBox.ScrollContentWidth)));
                float scrollRange = Math.Max(1, _elemScrollDragBox.ScrollContentWidth - _elemScrollDragBox.ContentBox.Width);
                float localX = dragDocX - pb.Left;
                float delta = (localX - _elemScrollDragStart) / (trackWidth - thumbWidth) * scrollRange;
                float target = Math.Clamp(_elemScrollDragStartScroll + delta, 0, scrollRange);
                _elemScrollDragBox.IsSmoothScrollingX = false;
                _elemScrollDragBox.ScrollVelX = 0;
                _elemScrollDragBox.ScrollX = target;
                MarkElementScrollDirty(_elemScrollDragBox);
            }
            return;
        }

        float docX = x + _scroll.ScrollX;
        float adjustedY = y - _contentOffset + _scroll.ScrollY;
        var element = PageHitTest.HitTest(_currentLoad.Document, docX, adjustedY);

        // Textarea resize drag: grow/shrink the element via its inline style.
        if (_textareaResizeElement != null)
        {
            float newW = Math.Max(50, _textareaResizeStartW + (docX - _textareaResizeStartX));
            float newH = Math.Max(30, _textareaResizeStartH + (adjustedY - _textareaResizeStartY));
            SetElementInlineSize(_textareaResizeElement, newW, newH);
            _pendingRelayout = true;
            _input.NeedsRedraw = true;
            return;
        }

        // Update text selection during drag
        if (_isSelecting)
        {
            float dlX = x + _scroll.ScrollX;
            float dlY = y + _scroll.ScrollY;
            var pt = HitTestTextPosition(_currentLoad.Document, dlX, dlY);
            if (pt.Node != null)
            {
                if (pt.Node != _selFocus.Node || pt.Offset != _selFocus.Offset)
                {
                    _selFocus = pt;
                    _hasSelection = true;
                    _input.NeedsRedraw = true;
                }
            }
        }

        // Form input drag selection
        if (_inputDragging && _focusedElement != null && _focusedElement.IsTextEditable)
        {
            string val = _focusedElement.Value ?? "";
            string? inputType = _focusedElement.InputType?.ToLowerInvariant();
            bool isTextInput = inputType == null || inputType == "text" || inputType == "password" ||
                               inputType == "email" || inputType == "search" || inputType == "tel" ||
                               inputType == "url" || inputType == "number";
            if (isTextInput && _focusedElement.ComputedStyle != null && _focusedElement.LayoutBox != null)
            {
                float cbLeft = _focusedElement.LayoutBox.ContentBox.Left;
                float clickX = docX - cbLeft - 2;
                float fontSize = _focusedElement.ComputedStyle.FontSize > 0 ? _focusedElement.ComputedStyle.FontSize : 14;
                string fontFamily = _focusedElement.ComputedStyle.FontFamily ?? "Arial";
                string displayVal = val;
                if (inputType == "password" && !_passwordRevealed)
                    displayVal = new string('●', val.Length);
                UpdateInputScrollOffset(_focusedElement);
                int newPos = GetFormInputCharIndex(displayVal, clickX + _inputScrollOffset, fontSize, fontFamily);
                if (newPos != _inputCursorPos)
                {
                    if (_inputSelStart < 0) _inputSelStart = _inputCursorPos;
                    _inputCursorPos = newPos;
                    _inputShowCursor = true;
                    _inputLastCursorBlinkTick = Environment.TickCount64;
                    _input.NeedsRedraw = true;
                    UpdateInputScrollOffset(_focusedElement);
                }
            }
        }

        // Track hovered element for mouseover/mouseout
        if (element != _hoveredElement)
        {
            if (_hoveredElement != null)
            {
                var outHost = _jsEngine.GetElementHost(_hoveredElement);
                var outEvt = new ScriptEvent("mouseout", outHost)
                {
                    clientX = x,
                    clientY = y,
                    relatedTarget = element != null ? _jsEngine.GetElementHost(element) : null,
                    bubbles = true,
                    cancelable = true
                };
                _jsEngine.DispatchEvent(_hoveredElement, outEvt);
            }
            if (element != null)
            {
                var overHost = _jsEngine.GetElementHost(element);
                var overEvt = new ScriptEvent("mouseover", overHost)
                {
                    clientX = x,
                    clientY = y,
                    relatedTarget = _hoveredElement != null ? _jsEngine.GetElementHost(_hoveredElement) : null,
                    bubbles = true,
                    cancelable = true
                };
                _jsEngine.DispatchEvent(element, overEvt);
            }
            // Update CSS :hover pseudo-class state
            // Clear hover on old element's ancestor chain
            var oldHover = _hoveredElement;
            var ptr = oldHover;
            while (ptr != null)
            {
                ptr.IsHovered = false;
                ptr = ptr.ParentElement;
            }
            // Set hover on new element's ancestor chain
            _hoveredElement = element;
            ptr = _hoveredElement;
            while (ptr != null)
            {
                ptr.IsHovered = true;
                ptr = ptr.ParentElement;
            }

            // A hover change only needs a style/relayout round-trip when it can
            // actually affect rendering: the document has :hover rules, or the
            // hovered chain contains a checkbox/radio (their visuals read
            // IsHovered at paint time). Otherwise nothing changes visually, so
            // skip the relayout entirely — every mouse move over a hover-free
            // page previously rebuilt + re-tiled the whole document.
            if (!ReferenceEquals(_hoverRulesDoc, _currentLoad?.Document))
            {
                _documentHasHoverRules = _currentLoad?.StyleComputer?.HasHoverRules() ?? false;
                _hoverRulesDoc = _currentLoad?.Document;
            }
            bool hoverAffectsPaint = _documentHasHoverRules
                || ChainContainsHoverFormControl(element);

            if (hoverAffectsPaint)
            {
                // Record the changed region (old + new hovered element ancestor
                // chains) so the frame can re-raster only that area instead of
                // re-tiling the page. :hover applies to the whole chain, so all
                // their boxes are dirty.
                _hoverDirtyRect = default;
                for (var p = oldHover; p != null; p = p.ParentElement)
                    _hoverDirtyRect = UnionHoverRect(_hoverDirtyRect, p.LayoutBox?.BorderBox);
                for (var p = element; p != null; p = p.ParentElement)
                    _hoverDirtyRect = UnionHoverRect(_hoverDirtyRect, p.LayoutBox?.BorderBox);
                _hoverRelayoutPending = true;
                _pendingRelayout = true;
            }
        }

        // Update select dropdown option hover highlight
        if (_activeSelect != null)
        {
            float ddX = x + _scroll.ScrollX;
            float ddY = y - _contentOffset + _scroll.ScrollY;
            int newHover = -1;
            for (int i = 0; i < _selectOptionRects.Count; i++)
            {
                var r = _selectOptionRects[i].Rect;
                if (ddX >= r.Left && ddX <= r.Right && ddY >= r.Top && ddY <= r.Bottom)
                {
                    newHover = i;
                    break;
                }
            }
            if (newHover != _selectHoverIndex)
            {
                _selectHoverIndex = newHover;
                _hoverDirtyRect = _selectDropdownRect;
                _hoverRelayoutPending = true;
                _pendingRelayout = true;
            }
        }

        if (element != null)
        {
            var moveHost = _jsEngine.GetElementHost(element);
            var moveEvt = new ScriptEvent("mousemove", moveHost)
            {
                clientX = x,
                clientY = y,
                bubbles = true,
                cancelable = true
            };
            _jsEngine.DispatchEvent(element, moveEvt);
        }
    }

    /// <summary>Grow <paramref name="acc"/> to include <paramref name="box"/> (page
    /// space, chrome offset applied). Empty/absent inputs leave it unchanged.</summary>
    private SKRect UnionHoverRect(SKRect acc, SKRect? box)
    {
        if (!box.HasValue || box.Value.Width <= 0 || box.Value.Height <= 0)
            return acc;
        var r = new SKRect(box.Value.Left, box.Value.Top + _contentOffset,
            box.Value.Right, box.Value.Bottom + _contentOffset);
        if (acc.Width <= 0 || acc.Height <= 0)
            return r;
        return SKRect.Union(acc, r);
    }

    /// <summary>True when <paramref name="element"/> or any ancestor is a checkbox /
    /// radio input — the only page paint that reads IsHovered directly (hover
    /// feedback ring), so a hover over them needs a repaint even without :hover rules.</summary>
    private static bool ChainContainsHoverFormControl(Core.Dom.Element? element)
    {
        for (var p = element; p != null; p = p.ParentElement)
        {
            if (!p.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
                continue;
            var t = p.GetAttribute("type");
            if (t != null && (t.Equals("checkbox", StringComparison.OrdinalIgnoreCase)
                || t.Equals("radio", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }

    private void HandleDomMouseUp(float x, float y)
    {
        // End scrollbar drag.
        _scrollInteraction.HandleMouseUp();

        // Element scrollbar drag release
        _elemScrollDragBox = null;
        _textareaResizeElement = null;
        _textareaScrollDragging = false;
        bool hadControlPress = _pressedInputControl != null || _pressedButton != null;
        _pressedInputControl = null;
        _pressedButton = null;
        if (hadControlPress)
            _pendingRelayout = true;

        if (_isSelecting)
        {
            _isSelecting = false;
            if (_hasSelection)
            {
                _input.NeedsRedraw = true;
            }
        }
        if (_inputDragging)
        {
            _inputDragging = false;
            _input.NeedsRedraw = true;
        }
    }

    public string GetSelectedText()
    {
        if (!_hasSelection || _currentLoad == null) return "";
        if (_selAnchor.Node == null || _selFocus.Node == null) return "";

        var sb = new System.Text.StringBuilder();
        CollectSelectedTextRange(_selAnchor.Node, _selAnchor.Offset, _selFocus.Node, _selFocus.Offset, sb);
        return sb.ToString();
    }

    private static void CollectSelectedTextRange(Core.Dom.TextNode startNode, int startOff,
        Core.Dom.TextNode endNode, int endOff, System.Text.StringBuilder sb)
    {
        // Normalize: ensure start is before end in DOM order
        int cmp = CompareDomPosition(startNode, endNode);
        if (cmp > 0)
        {
            (startNode, endNode) = (endNode, startNode);
            (startOff, endOff) = (endOff, startOff);
        }
        else if (cmp == 0)
        {
            // Same node
            int lo = Math.Min(startOff, endOff);
            int hi = Math.Max(startOff, endOff);
            var text = startNode.TextContent ?? "";
            if (lo < 0) lo = 0;
            if (hi > text.Length) hi = text.Length;
            if (lo < hi)
                sb.Append(text.AsSpan(lo, hi - lo));
            return;
        }

        // Different nodes: collect from start node to its end,
        // then all text nodes between them in DOM order, then from beginning of end node
        var startText = startNode.TextContent ?? "";
        if (startOff < startText.Length)
            sb.Append(startText.AsSpan(startOff));

        CollectTextBetween(startNode, endNode, sb);

        var endText = endNode.TextContent ?? "";
        if (endOff > 0 && endOff <= endText.Length)
            sb.Append(endText.AsSpan(0, endOff));
    }

    private static void CollectTextBetween(Core.Dom.Node startNode, Core.Dom.Node endNode, System.Text.StringBuilder sb)
    {
        // Find common ancestor by building ancestor paths
        var startPath = new List<Core.Dom.Node>();
        var n = startNode;
        while (n != null) { startPath.Add(n); n = n.ParentNode; }

        var endPath = new List<Core.Dom.Node>();
        n = endNode;
        while (n != null) { endPath.Add(n); n = n.ParentNode; }

        startPath.Reverse();
        endPath.Reverse();

        int depth = Math.Min(startPath.Count, endPath.Count);
        Core.Dom.Node? commonAncestor = null;
        for (int i = 0; i < depth; i++)
        {
            if (startPath[i] == endPath[i])
                commonAncestor = startPath[i];
            else
                break;
        }

        if (commonAncestor == null) return;

        // DFS from common ancestor, collecting text between startNode and endNode
        bool collecting = false;
        CollectTextBetweenRecursive(commonAncestor, startNode, endNode, ref collecting, sb);
    }

    private static bool CollectTextBetweenRecursive(Core.Dom.Node current, Core.Dom.Node startNode, Core.Dom.Node endNode,
        ref bool collecting, System.Text.StringBuilder sb)
    {
        foreach (var child in current.Children)
        {
            if (child == endNode)
                return true;

            if (!collecting)
            {
                if (child == startNode)
                {
                    collecting = true;
                    continue;
                }
                if (child is Core.Dom.Element el)
                {
                    if (CollectTextBetweenRecursive(el, startNode, endNode, ref collecting, sb))
                        return true;
                }
                continue;
            }

            if (child is Core.Dom.TextNode tn)
            {
                var text = tn.TextContent;
                if (!string.IsNullOrEmpty(text))
                    sb.Append(text);
            }
            else if (child is Core.Dom.Element el)
            {
                if (CollectTextBetweenRecursive(el, startNode, endNode, ref collecting, sb))
                    return true;
            }
        }
        return false;
    }

    private static int CompareDomPosition(Core.Dom.Node a, Core.Dom.Node b)
    {
        if (a == b) return 0;
        // Walk ancestors to find common ancestor and compare position
        var aPath = new List<Core.Dom.Node>();
        var bPath = new List<Core.Dom.Node>();
        var cur = a;
        while (cur != null) { aPath.Add(cur); cur = cur.ParentNode; }
        cur = b;
        while (cur != null) { bPath.Add(cur); cur = cur.ParentNode; }
        aPath.Reverse();
        bPath.Reverse();
        int depth = Math.Min(aPath.Count, bPath.Count);
        for (int i = 0; i < depth; i++)
        {
            if (aPath[i] != bPath[i])
            {
                // Find sibling index
                var parent = aPath[i].ParentNode;
                if (parent != null)
                {
                    int ai = parent.Children.IndexOf((Core.Dom.Node)aPath[i]);
                    int bi = parent.Children.IndexOf((Core.Dom.Node)bPath[i]);
                    return ai.CompareTo(bi);
                }
                return 0;
            }
        }
        return aPath.Count.CompareTo(bPath.Count);
    }

    private static string KeyToJsKey(char charCode, Key key)
    {
        if (charCode != '\0' && charCode >= 32) return new string(charCode, 1);
        return key switch
        {
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Backspace => "Backspace",
            Key.Delete => "Delete",
            Key.Left => "ArrowLeft",
            Key.Up => "ArrowUp",
            Key.Right => "ArrowRight",
            Key.Down => "ArrowDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Space => " ",
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            >= Key.A and <= Key.Z => ((char)(int)key).ToString(),
            _ => "Unidentified"
        };
    }

    private static string KeyToJsCode(Key key)
    {
        return key switch
        {
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Backspace => "Backspace",
            Key.Delete => "Delete",
            Key.Left => "ArrowLeft",
            Key.Up => "ArrowUp",
            Key.Right => "ArrowRight",
            Key.Down => "ArrowDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Space => "Space",
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            >= Key.A and <= Key.Z => "Key" + (char)(int)key,
            _ => ""
        };
    }

    private bool IsCtrlPressed() => _input.IsCtrlDown;
    private bool IsShiftPressed() => _input.IsShiftDown;
    private bool IsAltPressed() => _input.IsAltDown;

    private void UpdateInputScrollOffset(Core.Dom.Element? element)
    {
        // Seed from the previous scroll so a caret sitting mid-text keeps the
        // current viewport instead of snapping back to 0; only overflow re-scrolls.
        float currentScroll = _inputScrollOffset;
        _inputScrollOffset = 0;
        if (element == null || element.LayoutBox == null) return;
        var style = element.ComputedStyle;
        if (style == null) return;
        string? value = element.Value;
        if (string.IsNullOrEmpty(value)) return;
        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        string fontFamily = style.FontFamily ?? "Arial";
        // Password dots are wider than the real characters; measure the same masked
        // text the painter draws so scroll/click mapping stay aligned.
        string? inputType = element.InputType?.ToLowerInvariant();
        string measuredValue = inputType == "password" && !_passwordRevealed
            ? new string('●', value.Length) : value;
        float fullTextWidth = TextMeasurer.Instance?.MeasureText(measuredValue, fontFamily, fontSize) ?? measuredValue.Length * fontSize * 0.55f;
        // Keep the scroll range consistent with the painter: the right-side internal
        // controls (clear button / spin buttons / password reveal) reserve space, so
        // usable width must match, otherwise click->caret mapping drifts horizontally.
        float usableWidth = element.LayoutBox.ContentBox.Width - 4 - GetInputReservedRight(element);
        if (fullTextWidth > usableWidth)
        {
            float cursorWidth = TextMeasurer.Instance?.MeasureText(measuredValue[..Math.Min(_inputCursorPos, measuredValue.Length)], fontFamily, fontSize)
                ?? _inputCursorPos * fontSize * 0.55f;
            float maxScroll = Math.Max(0, fullTextWidth - usableWidth);
            _inputScrollOffset = KeepCaretVisibleOffset(cursorWidth, currentScroll, usableWidth, maxScroll);
        }
    }

    // Mirrors PaintVisitor.DrawTextAreaElement's keep-visible rule, but persists the
    // vertical scroll as state (seeded from the previous value) so the viewport is
    // stable while typing/clicking instead of re-deriving from the caret each draw.
    // Shared textarea geometry: wrapped line count, scroll range and caret line.
    // Used by the caret-following viewport, the scrollbar hit-test and the thumb
    // drag so they all agree on the same numbers.
    private (float usableH, float totalH, float lineH, float maxScrollY, int caretLine) GetTextAreaMetrics(Core.Dom.Element el)
    {
        var style = el.ComputedStyle!;
        var box = el.LayoutBox!;
        float fs = style.FontSize > 0 ? style.FontSize : 14;
        float lineH = fs * (style.LineHeight > 0 ? style.LineHeight : 1.2f);
        float usableW = Math.Max(1, box.ContentBox.Width - 4);
        float usableH = Math.Max(1, box.ContentBox.Height - 4);
        var lines = Core.Layout.TextWrapHelper.WrapToLines(el.Value ?? "", style.FontFamily ?? "Arial", fs, usableW);
        float totalH = lines.Count * lineH;
        float maxScrollY = Math.Max(0, totalH - usableH);
        int caretLine = lines.Count == 0 ? 0
            : Math.Min(Core.Layout.TextWrapHelper.GetLineColumn(lines, _inputCursorPos).line, lines.Count - 1);
        return (usableH, totalH, lineH, maxScrollY, caretLine);
    }

    private void UpdateTextAreaScrollY()
    {
        // Seed from the previous scroll so typing/clicking below the fold keeps the
        // viewport stable instead of resetting the textarea back to the top.
        float prevScroll = _textareaScrollY;
        _textareaScrollY = 0;
        var el = _focusedElement;
        if (el == null || el.TagName != "TEXTAREA" || el.ComputedStyle == null || el.LayoutBox == null) return;
        var (usableH, totalH, lineH, maxScrollY, caretLine) = GetTextAreaMetrics(el);
        if (maxScrollY <= 0) { _textareaScrollY = 0; return; }
        float scroll = prevScroll;
        if (_textareaUserScroll)
        {
            // User scrolled with the wheel/thumb: keep the viewport, just clamp.
            _textareaScrollY = Math.Clamp(scroll, 0, maxScrollY);
            return;
        }
        const float margin = 4;
        float caretLineY = caretLine * lineH;
        if (caretLineY < scroll + margin)
            scroll = Math.Max(0, caretLineY - margin);
        else if (caretLineY + lineH > scroll + usableH - margin)
            scroll = Math.Min(maxScrollY, caretLineY + lineH - (usableH - margin));
        _textareaScrollY = scroll;
    }

    private float GetInputReservedRight(Core.Dom.Element element)
    {
        if (element.TagName != "INPUT") return 0;
        string? inputType = element.InputType?.ToLowerInvariant();
        bool isFocused = _focusedElement == element;
        bool isDisabled = element.HasAttribute("disabled");
        if (inputType == "search" && isFocused && !isDisabled && !string.IsNullOrEmpty(element.Value)) return 24;
        if (inputType == "number" && isFocused && !isDisabled) return 22;
        if (inputType == "password" && isFocused && !isDisabled) return 22;
        return 0;
    }

    // Scrolls horizontally only far enough to keep the caret visible (with a small
    // margin) instead of snapping it to a fixed fraction of the width. This keeps the
    // caret exactly where the user clicked instead of drifting ~33% toward the left.
    private static float KeepCaretVisibleOffset(float caretWidth, float currentScroll, float usableWidth, float maxScroll)
    {
        const float margin = 8;
        float caretX = caretWidth - currentScroll;
        if (caretX < margin)
            return Math.Clamp(caretWidth - margin, 0, maxScroll);
        if (caretX > usableWidth - margin)
            return Math.Clamp(caretWidth - (usableWidth - margin), 0, maxScroll);
        return Math.Clamp(currentScroll, 0, maxScroll);
    }

    // Maps a click inside a <textarea> (doc coordinates) to a flat character index,
    // using the same visual-line / vertical-scroll model as PaintVisitor.DrawTextAreaElement.
    private int GetTextAreaCharIndex(Core.Dom.Element element, float docX, float docY)
    {
        var box = element.LayoutBox!;
        var style = element.ComputedStyle!;
        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        float lineH = fontSize * (style.LineHeight > 0 ? style.LineHeight : 1.2f);
        var contentBox = box.ContentBox;
        float textX = contentBox.Left + 2;
        float textTop = contentBox.Top + 2;
        float usableW = Math.Max(1, contentBox.Width - 4);
        float usableH = Math.Max(1, contentBox.Height - 4);
        string fontFamily = style.FontFamily ?? "Arial";
        string val = element.Value ?? "";

        var lines = Core.Layout.TextWrapHelper.WrapToLines(val, fontFamily, fontSize, usableW);
        if (lines.Count == 0) return 0;

        // Use the shared vertical scroll state (the viewport as currently shown) so
        // the clicked line is resolved against what the user actually sees, matching
        // the painter exactly.
        float maxScrollY = Math.Max(0, lines.Count * lineH - usableH);
        float scrollY = maxScrollY > 0 ? _textareaScrollY : 0;

        int lineIdx = (int)Math.Floor((docY - textTop + scrollY) / lineH);
        lineIdx = Math.Clamp(lineIdx, 0, lines.Count - 1);
        var ln = lines[lineIdx];
        string lineText = val.Substring(ln.Start, ln.Length);
        int col = GetFormInputCharIndex(lineText, docX - textX, fontSize, fontFamily);
        return Core.Layout.TextWrapHelper.GetFlatIndex(lines, lineIdx, col);
    }

    private int GetFormInputCharIndex(string text, float clickX, float fontSize, string fontFamily)
    {
        if (string.IsNullOrEmpty(text) || clickX <= 0) return 0;
        if (fontSize <= 0) fontSize = 14;
        if (string.IsNullOrEmpty(fontFamily)) fontFamily = "Arial";

        float totalWidth = Core.Layout.TextMeasurer.Instance?.MeasureText(text, fontFamily, fontSize)
            ?? text.Length * fontSize * 0.55f;
        if (clickX >= totalWidth) return text.Length;

        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            string prefix = text[..mid];
            float w = Core.Layout.TextMeasurer.Instance?.MeasureText(prefix, fontFamily, fontSize)
                ?? mid * fontSize * 0.55f;
            if (clickX <= w)
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }

    private void UpdateFormInputCursorBlink()
    {
        if (_focusedElement == null || !_focusedElement.IsTextEditable)
            return;
        long now = Environment.TickCount64;
        if (now - _inputLastCursorBlinkTick >= 500)
        {
            _inputLastCursorBlinkTick = now;
            _inputShowCursor = !_inputShowCursor;
            // Don't force redraw if user recently interacted (typing, scroll, mouse)
            if (now - _lastInputTimeTick >= InputCooldownMs)
                _input.NeedsRedraw = true;
        }
    }

    private bool DevToolsHandleInput(char c, Key key, bool shift)
    {
        if (key == Key.Unknown && c == 1) return false;
        if (key == Key.Unknown && c == 3) return false;
        if (key == Key.Unknown && c == 22) return false;
        if (key == Key.Unknown && c == 24) return false;
        if (key == Key.Unknown && c == 26) return false;

        return _devTools.HandleKeyPress(c, key, shift);
    }

    private void HandleImeChar(char charCode)
    {
        if (charCode == '\0') return;

        if (_devTools.IsInputField(_input.GetMousePosition().x, _input.GetMousePosition().y))
        {
            if (_devTools.HandleImeChar(charCode))
            {
                _input.NeedsRedraw = true;
                return;
            }
        }

        if (_focusedElement != null && _focusedElement.IsTextEditable)
        {
            // Insert IME character into the form input value
            string value = _focusedElement.Value ?? "";
            int cursorPos = _inputCursorPos;
            value = value[..Math.Min(cursorPos, value.Length)] + charCode +
                    value[Math.Min(cursorPos, value.Length)..];
            _focusedElement.Value = value;
            _inputCursorPos = cursorPos + 1;
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _jsEngine.DispatchEvent(_focusedElement, "input");
            _input.NeedsRedraw = true;
        }
        else if (_chrome.IsUrlBarFocused())
        {
            _chrome.HandleKeyPress(charCode, SKKey.None);
            _input.NeedsRedraw = true;
        }
    }

    private void PerformCopy()
    {
        var (mx, my) = _input.GetMousePosition();
        if (_devTools.Visible && _devTools.IsInputField(mx, my))
        {
            string sel = _devTools.GetActiveTabSelectedText();
            if (!string.IsNullOrEmpty(sel))
                Clipboard.SetText(sel);
        }
        else if (_chrome.IsUrlBarFocused())
        {
            string? sel = _chrome.UrlBarSelectedText;
            if (sel != null)
                Clipboard.SetText(sel);
            else
            {
                string url = _chrome.GetCurrentUrl() ?? "";
                if (!string.IsNullOrEmpty(url))
                    Clipboard.SetText(url);
            }
        }
        else if (_focusedElement != null && _focusedElement.IsTextEditable)
        {
            string value = _focusedElement.Value ?? "";
            if (_inputSelStart >= 0 && _inputSelStart != _inputCursorPos)
            {
                int a = Math.Min(_inputSelStart, _inputCursorPos);
                int b = Math.Max(_inputSelStart, _inputCursorPos);
                a = Math.Min(a, value.Length);
                b = Math.Min(b, value.Length);
                string sel = value[a..b];
                if (!string.IsNullOrEmpty(sel))
                    Clipboard.SetText(sel);
            }
        }
        else
        {
            if (_processTabs)
            {
                // Remote page: the child owns the selection. Ask it, then land the text
                // on the clipboard on the UI thread — this callback is on the pipe reader.
                ActiveRemote()?.Proc.RequestSelectedText(sel => _eventLoop.PostTask(() =>
                {
                    if (!string.IsNullOrEmpty(sel))
                        Clipboard.SetText(sel);
                }));
                return;
            }
            string sel = GetSelectedText();
            if (!string.IsNullOrEmpty(sel))
                Clipboard.SetText(sel);
        }
    }

    private void PerformPaste()
    {
        string? text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;

        if (_chrome.IsUrlBarFocused())
        {
            if (_chrome.UrlBarSelectedText != null)
                _chrome.HandleKeyPress('\0', SKKey.Backspace);
            foreach (char c in text)
                HandleImeChar(c);
        }
        else if (_focusedElement != null && _focusedElement.IsTextEditable)
        {
            string value = _focusedElement.Value ?? "";
            string? inputType = _focusedElement.InputType?.ToLowerInvariant();
            // Tel input: strip non-digits on paste
            if (inputType == "tel")
                text = new string(text.Where(char.IsDigit).ToArray());
            if (text.Length == 0) return;
            // maxlength enforcement on paste
            string? maxlenStr = _focusedElement.GetAttribute("maxlength");
            if (int.TryParse(maxlenStr, out int maxlen) && maxlen >= 0)
            {
                int curLen = value.Length;
                if (_inputSelStart >= 0 && _inputSelStart != _inputCursorPos)
                    curLen -= Math.Abs(_inputCursorPos - _inputSelStart);
                int avail = maxlen - curLen;
                if (avail <= 0) return;
                if (text.Length > avail)
                    text = text[..avail];
            }
            int cursorPos = _inputCursorPos;
            if (_inputSelStart >= 0 && _inputSelStart != cursorPos)
            {
                int a = Math.Min(_inputSelStart, cursorPos);
                int b = Math.Max(_inputSelStart, cursorPos);
                value = value[..a] + text + value[b..];
                _inputCursorPos = a + text.Length;
                _inputSelStart = -1;
            }
            else
            {
                value = value[..cursorPos] + text + value[cursorPos..];
                _inputCursorPos = cursorPos + text.Length;
            }
            _focusedElement.Value = value;
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _jsEngine.DispatchEvent(_focusedElement, "input");
            _input.NeedsRedraw = true;
        }
        else
        {
            foreach (char c in text)
                HandleImeChar(c);
        }
    }

    private void PerformCut()
    {
        PerformCopy();
        if (_devTools.Visible)
        {
            if (_devTools.GetActiveTab() == 0)
                _devTools.HandleKeyPress('\0', Key.Backspace);
            else if (_devTools.GetActiveTab() == 2)
                _devTools.HandleKeyPress('\0', Key.Backspace);
        }
        else if (_chrome.IsUrlBarFocused() && _chrome.UrlBarSelectedText != null)
            _chrome.HandleKeyPress('\0', SKKey.Backspace);
        else if (_focusedElement != null && _focusedElement.IsTextEditable &&
                 _inputSelStart >= 0 && _inputSelStart != _inputCursorPos)
        {
            string value = _focusedElement.Value ?? "";
            int a = Math.Min(_inputSelStart, _inputCursorPos);
            int b = Math.Max(_inputSelStart, _inputCursorPos);
            value = value[..a] + value[b..];
            _focusedElement.Value = value;
            _inputCursorPos = a;
            _inputSelStart = -1;
            _jsEngine.DispatchEvent(_focusedElement, "input");
            _input.NeedsRedraw = true;
        }
    }

    private void PerformSelectAll()
    {
        if (_chrome.IsUrlBarFocused())
        {
            _chrome.SelectAllInUrlBar();
        }
        else if (_devTools.Visible)
        {
            _devTools.SelectAllInActiveTab();
        }
        else if (_focusedElement != null && _focusedElement.IsTextEditable)
        {
            string value = _focusedElement.Value ?? "";
            _inputSelStart = 0;
            _inputCursorPos = value.Length;
            _inputShowCursor = true;
            _inputLastCursorBlinkTick = Environment.TickCount64;
            _input.NeedsRedraw = true;
        }
        else if (_processTabs)
        {
            // The selection lives with the document in the child process, so the parent
            // cannot build it: ask, and the next frame arrives already highlighted.
            ActiveRemote()?.Proc.SelectAll();
        }
        else if (_currentLoad?.Document != null)
        {
            // Select all text on the page: find first and last text nodes
            Core.Dom.TextNode? firstText = null;
            Core.Dom.TextNode? lastText = null;
            Acrux.PageHost.PageEngine.FindFirstLastTextNodes(
                _currentLoad.Document.DocumentElement ?? _currentLoad.Document.Body, ref firstText, ref lastText);
            if (firstText != null && lastText != null)
            {
                _selAnchor = new SelPoint { Node = firstText, Offset = 0 };
                _selFocus = new SelPoint { Node = lastText, Offset = (lastText.TextContent ?? "").Length };
                _hasSelection = true;
                _isSelecting = false;
                _input.NeedsRedraw = true;
            }
        }
    }

    public void InjectImeChar(char c)
    {
        HandleImeChar(c);
    }

    private class PageInputImeHost : IImeSupport
    {
        private readonly BrowserApp _app;

        public PageInputImeHost(BrowserApp app) { _app = app; }

        public Point GetImeCaretPosition()
        {
            if (_app._processTabs)
            {
                // Out-of-process page: the newest frame carries the caret in DOCUMENT
                // space; the shell applies its own (speculative) scroll and the chrome
                // offset here, so the candidate window stays glued to the caret while
                // the child is still catching up.
                var proc = _app.ActiveRemote()?.Proc;
                if (proc != null && proc.TryGetImeState(out float docX, out float docY, out _, out _, out _))
                    return new Point(docX - _app._scroll.ScrollX,
                        docY - _app._scroll.ScrollY + _app._contentOffset);
                return new Point(0, _app._contentOffset);
            }

            var el = _app._focusedElement;
            if (el?.LayoutBox == null)
                return new Point(0, _app._contentOffset);

            float fontSize = el.ComputedStyle?.FontSize > 0 ? el.ComputedStyle.FontSize : 14;
            string fontFamily = el.ComputedStyle?.FontFamily ?? "Arial";
            string value = el.Value ?? "";
            int cursorPos = _app._inputCursorPos;
            float textBeforeWidth = Core.Layout.TextMeasurer.Instance?.MeasureText(
                value[..Math.Min(cursorPos, value.Length)], fontFamily, fontSize) ?? cursorPos * fontSize * 0.55f;
            float caretX = el.LayoutBox.ContentBox.Left + 2 + textBeforeWidth;
            float caretY = el.LayoutBox.BorderBox.Top - _app._scroll.ScrollY + _app._contentOffset;
            return new Point(caretX, (int)caretY);
        }

        public void OnImeCompositionStart()
        {
            if (_app._processTabs)
            {
                // The engine owns composition state out of process: forward and let the
                // next frame bring back the drawn composition + caret.
                _app.ActiveRemote()?.Proc.ImeUpdate("", 0, 0);
                return;
            }
            _app._inputImeComposing = true;
            _app._inputImeCompositionStr = "";
            _app._inputImeCursorPos = 0;
        }

        public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
        {
            if (_app._processTabs)
            {
                compositionString ??= "";
                _app.ActiveRemote()?.Proc.ImeUpdate(compositionString, cursorPosition, compositionString.Length);
                return;
            }
            _app._inputImeCompositionStr = compositionString;
            _app._inputImeCursorPos = cursorPosition;
            _app._input.NeedsRedraw = true;
        }

        public void OnImeCompositionEnd(string? resultString)
        {
            if (_app._processTabs)
            {
                var proc = _app.ActiveRemote()?.Proc;
                if (proc == null) return;
                if (string.IsNullOrEmpty(resultString))
                    proc.ImeCancel();
                else
                    proc.ImeCommit(resultString);
                return;
            }

            _app._inputImeComposing = false;
            _app._inputImeCompositionStr = "";
            _app._inputImeCursorPos = 0;

            if (string.IsNullOrEmpty(resultString) || _app._focusedElement == null || !_app._focusedElement.IsTextEditable)
                return;

            string value = _app._focusedElement.Value ?? "";
            int cursorPos = _app._inputCursorPos;
            value = value[..Math.Min(cursorPos, value.Length)] + resultString +
                    value[Math.Min(cursorPos, value.Length)..];
            _app._focusedElement.Value = value;
            _app._inputCursorPos = cursorPos + resultString.Length;
            _app._inputSelStart = -1;
            _app._inputShowCursor = true;
            _app._inputLastCursorBlinkTick = Environment.TickCount64;
            _app._jsEngine.DispatchEvent(_app._focusedElement, "input");
            _app._input.NeedsRedraw = true;
        }
    }

    private SelPoint HitTestTextPosition(Core.Dom.Document doc, float dlX, float dlY)
    {
        var result = new SelPoint { Node = null, Offset = 0 };
        HitTestTextPositionRecursive(doc.DocumentElement ?? doc.Body, dlX, dlY, ref result);
        if (result.Node == null)
            HitTestTextPositionRecursive(doc.Body, dlX, dlY, ref result);
        return result;
    }

    private const float HitToleranceY = 8f;      // hit area tolerance above/below line
    private const float HitToleranceX = 4f;      // hit area tolerance left/right of run

    private void HitTestTextPositionRecursive(Core.Dom.Element? element, float dlX, float dlY, ref SelPoint result)
    {
        if (element == null) return;
        var box = element.LayoutBox;
        if (box != null)
        {
            // Viewport culling: skip element if its content box is far from hit point
            float boxTop = box.ContentBox.Top + _contentOffset;
            float boxBottom = boxTop + box.ContentBox.Height;

            // Only check Lines/LineRuns if hit point is within element's Y range (with large tolerance)
            bool boxInRange = dlY >= boxTop - HitToleranceY * 4 && dlY < boxBottom + HitToleranceY * 4;

            if (boxInRange && box.Lines != null && box.Lines.Count > 0)
            {
                float boxLeft = box.ContentBox.Left;
                Core.Dom.TextNode? lastTextNode = null;
                int runCharOffset = 0;
                foreach (var line in box.Lines)
                {
                    float lineTop = line.Y + _contentOffset;
                    float lineBottom = lineTop + line.Height;
                    bool lineMatches = dlY >= lineTop - HitToleranceY && dlY < lineBottom + HitToleranceY;
                    if (!lineMatches) continue;
                    float runX = boxLeft + line.TextAlignOffsetX;
                    foreach (var run in line.Runs)
                    {
                        if (!run.IsText || run.Node is not Core.Dom.TextNode tn)
                        {
                            lastTextNode = null;
                            runCharOffset = 0;
                            runX += run.Width;
                            continue;
                        }

                        if (tn != lastTextNode)
                        {
                            runCharOffset = 0;
                            lastTextNode = tn;
                        }

                        if (dlX < runX + run.Width + HitToleranceX || run == line.Runs[^1])
                        {
                            float localX = Math.Max(0, dlX - runX);
                            int localOffset = GetCharOffsetAtX(run, run.Text ?? "", localX);
                            result = new SelPoint { Node = tn, Offset = runCharOffset + localOffset };
                            return;
                        }

                        runCharOffset += (run.Text ?? "").Length;
                        runX += run.Width;
                    }
                    // Not found in this line's runs, continue to next line
                }
            }

            if (boxInRange && box.LineRuns != null && box.LineRuns.Count > 0)
            {
                float boxTopLR = box.ContentBox.Top + _contentOffset;
                float x = box.ContentBox.Left;
                float lineHeight = 0;
                foreach (var run in box.LineRuns) lineHeight = Math.Max(lineHeight, run.Height);
                if (lineHeight <= 0) lineHeight = box.ContentBox.Height;
                if (dlY >= boxTopLR - HitToleranceY && dlY < boxTopLR + lineHeight + HitToleranceY)
                {
                    Core.Dom.TextNode? lastTextNode = null;
                    int runCharOffset = 0;
                    foreach (var run in box.LineRuns)
                    {
                        if (!run.IsText || run.Node is not Core.Dom.TextNode tn)
                        {
                            lastTextNode = null;
                            runCharOffset = 0;
                            x += run.Width;
                            continue;
                        }

                        if (tn != lastTextNode)
                        {
                            runCharOffset = 0;
                            lastTextNode = tn;
                        }

                        if (dlX < x + run.Width + HitToleranceX || run == box.LineRuns[^1])
                        {
                            float localX = Math.Max(0, dlX - x);
                            int localOffset = GetCharOffsetAtX(run, run.Text ?? "", localX);
                            result = new SelPoint { Node = tn, Offset = runCharOffset + localOffset };
                            return;
                        }

                        runCharOffset += (run.Text ?? "").Length;
                        x += run.Width;
                    }
                }
            }
            // Fallback for elements with text but no Lines/LineRuns (e.g. table cells)
            if (boxInRange && (box.Lines == null || box.Lines.Count == 0) && (box.LineRuns == null || box.LineRuns.Count == 0))
            {
                var style = element.ComputedStyle;
                if (style != null)
                {
                    float fontSize = style.FontSize;
                    string fontFamily = style.FontFamily ?? "Arial";
                    var fontWeight = style.FontWeight;
                    var textAlign = style.TextAlign;
                    float boxLeft = box.ContentBox.Left;
                    float boxRight = box.ContentBox.Right;
                    float boxWidth = box.ContentBox.Width;
                    float boxTop2 = box.ContentBox.Top;

                    // Tight Y bounds: text occupies [boxTop2, boxTop2 + fontSize]
                    float textTop2 = boxTop2 - HitToleranceY;
                    float textBottom2 = boxTop2 + fontSize + HitToleranceY;
                    if (dlY >= textTop2 && dlY <= textBottom2)
                    {
                        // Quick X rejection before any text measurement
                        if (dlX >= boxLeft - HitToleranceX && dlX <= boxRight + HitToleranceX)
                        {
                            foreach (var child in element.Children)
                            {
                                if (child is TextNode tn)
                                {
                                    string text = tn.TextContent ?? "";
                                    if (string.IsNullOrEmpty(text)) continue;

                                    // Lightweight width approximation for fast rejection
                                    float approxWidth = text.Length * fontSize * 0.45f;
                                    float startX = boxLeft;
                                    if (textAlign == TextAlignType.Center)
                                        startX = boxLeft + (boxWidth - approxWidth) / 2;
                                    else if (textAlign == TextAlignType.Right || textAlign == TextAlignType.End)
                                        startX = boxLeft + boxWidth - approxWidth;

                                    if (dlX >= startX - HitToleranceX && dlX <= startX + approxWidth + HitToleranceX)
                                    {
                                        // Accurate measurement
                                        float textWidth;
                                        if (TextMeasurer.Instance != null)
                                            textWidth = TextMeasurer.Instance.MeasureText(text, fontFamily, fontSize, fontWeight);
                                        else
                                            textWidth = approxWidth;

                                        if (textAlign == TextAlignType.Center)
                                            startX = boxLeft + (boxWidth - textWidth) / 2;
                                        else if (textAlign == TextAlignType.Right || textAlign == TextAlignType.End)
                                            startX = boxLeft + boxWidth - textWidth;
                                        else
                                            startX = boxLeft;

                                        if (dlX >= startX - HitToleranceX && dlX < startX + textWidth + HitToleranceX)
                                        {
                                            float localX = Math.Max(0, dlX - startX);
                                            int offset = GetCharOffsetAtX(text, fontSize, fontFamily, fontWeight, textWidth, localX);
                                            result = new SelPoint { Node = tn, Offset = offset };
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Only recurse into children if the hit point could be within this element's area
        // (avoid walking entire DOM tree for every mouse move)
        foreach (var child in element.Children.OfType<Core.Dom.Element>())
        {
            HitTestTextPositionRecursive(child, dlX, dlY, ref result);
            if (result.Node != null) return;
        }
    }

    private static int GetCharOffsetAtX(string text, float fontSize, string fontFamily, Core.Dom.FontWeight weight, float textWidth, float localX)
    {
        if (string.IsNullOrEmpty(text) || localX <= 0) return 0;
        if (localX >= textWidth) return text.Length;

        int len = text.Length;
        float[] cumWidths = new float[len];
        float acc = 0;
        for (int i = 0; i < len; i++)
        {
            string chStr = text[i].ToString();
            float cw;
            if (TextMeasurer.Instance != null)
                cw = TextMeasurer.Instance.MeasureText(chStr, fontFamily, fontSize, weight);
            else
                cw = fontSize * 0.45f;
            acc += cw;
            cumWidths[i] = acc;
        }

        int lo = 0, hi = len;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (cumWidths[mid - 1] <= localX) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private static int GetCharOffsetAtX(Core.Dom.InlineRun run, string runText, float localX)
    {
        float fontSize = run.FontSize ?? 16;
        string fontFamily = run.FontFamily ?? "Arial";
        var weight = run.FontWeight;
        return GetCharOffsetAtX(runText, fontSize, fontFamily, weight, run.Width, localX);
    }

    public void Dispose()
    {
        _watchdog?.Dispose();
        _watchdog = null;
        ShutdownPerformanceHub();
        _processManager.Dispose();
        if (_currentLoad != null)
        {
            // Dispose handled elsewhere
        }
        _chrome.Dispose();
        _skiaRenderer.Dispose();
        _window.Dispose();
        _jsEngine.Dispose();
        if (_threadedTabs)
        {
            foreach (var st in _tabStates.Values)
                st.Engine?.Dispose();
            _parkEngine?.Dispose();
        }
        foreach (var kv in _remoteTabs)
        {
            try { kv.Value.Dispose(); } catch { }
        }
        _remoteTabs.Clear();
#if USE_MULTIPLE_JS_ENGINE
        EngineProcessManager.Release(-1);
#endif
        _eventLoop.Stop();
        if (_renderingSettings != null)
            _skiaRenderer.Settings = null;
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Performance integration

    private void InitializePerformanceHub()
    {
        if (_perfHub is not null) return;

        _perfHub = PerformanceHub.Shared;
        _sharedStyleCache = _perfHub.StyleCache;
        _layoutCache = _perfHub.LayoutCache;

        // Wrap the regular LayoutEngine with the incremental engine so clean
        // subtrees can be skipped on subsequent passes.
        _incrementalLayout = new IncrementalLayoutEngine(_layout, _layoutCache);

        // 2GB soft budget for the process; can be tuned via config later.
        var memoryBudget = new MemoryBudget(2L * 1024 * 1024 * 1024);
        _perfHub.Initialize(memoryBudget);

        // Wire the long-task observer so we record any operation that exceeds
        // 50 ms in the central metrics. The hub will automatically push the
        // long-task duration into Total Blocking Time.
        _perfHub.LongTasks.OnLongTask += entry =>
        {
            _perfHub?.Registry.Feed.Append("longtask",
                $"{entry.Name} {entry.DurationNanos / 1_000_000.0:F1}ms");
        };

        // Connect the resource cache used by the rendering layer to memory pressure.
        // When the monitor reports a High/Critical level, the aggregate responder
        // shrinks caches to free pages.
        _perfHub.Registry.MemoryPressure.Register(_perfHub.AggregateResponder);

        // Make the image cache's decoded pool obey memory pressure
        if (_sharedImageCache is { } img)
        {
            _perfHub.Registry.MemoryPressure.Register(new ImagePoolPressureAdapter(img.DecodedPool));
        }

        // Route the tile compositor through the performance hub so its cache
        // uses the shared tile manager, the predictive scheduler pre-rasterises
        // tiles in the direction of scroll, and the memory budget caps the
        // tile byte total. The compositor is rebuilt inside AttachPerformanceHub
        // so it picks up these references on the next frame.
        try
        {
            _skiaRenderer.AttachPerformanceHub(_perfHub, memoryBudget);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PerfHub] Compositor attach failed: {ex.Message}");
        }

        Console.WriteLine("[PerfHub] Initialized. Style cache, layout cache, scheduler, long-task observer, tile compositor active.");
    }

    private void ShutdownPerformanceHub()
    {
        if (_perfHub is null) return;
        _perfHub.Shutdown();
        _perfHub = null;
    }

    /// <summary>
    /// Runs at the start of each render frame: drives the cooperative scheduler
    /// through one budget slice, observes the time spent in the main pipeline
    /// phases, and reports memory usage to the pressure monitor.
    /// </summary>
    private void RunPerfFrame(double dtMillis)
    {
        if (_perfHub is not null)
        {
            // Choose a frame budget based on the current target FPS (capped at 60 Hz
            // for the C#/Skia renderer). When the page is "behind" (dt > target),
            // use a larger catch-up budget to drain pending tasks.
            var budget = dtMillis > 50
                ? CooperativeScheduler.FrameBudget.CatchUp
                : CooperativeScheduler.FrameBudget.For60Fps;
            _perfHub.RunFrame(budget);

            // Coarse memory accounting: bytes used by managed heap is not directly
            // observable, but we can poke the GC heap and feed it to the pressure
            // monitor. This is a hint — the real policy is in MemoryPressureMonitor.
            long managedBytes = GC.GetTotalMemory(forceFullCollection: false);
            _perfHub.Registry.MemoryPressure.ReportUsage(managedBytes);
        }

        // Periodic JS GC to release V8/native heap memory every 30s
        if (dtMillis > 0)
        {
            long now = Environment.TickCount64;
            if (now - _lastGcTick >= 30000)
            {
                _lastGcTick = now;
                _jsEngine.IntegrationService?.CollectGarbage();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
            }
        }
    }

    /// <summary>
    /// Convenience used by JS engine / input handlers to mark a subtree as
    /// needing relayout. Replaces the previous boolean flag with a per-element
    /// dirty bit that the incremental layout engine can use to skip work.
    /// </summary>
    private void MarkSubtreeDirty(Acrux.Core.Dom.Element root, DirtyFlags flags = DirtyFlags.AllLayout)
    {
        if (root is null) return;
        DirtyState.AddSelf(root, flags);
        foreach (var child in root.Children)
        {
            if (child is Acrux.Core.Dom.Element ce)
            {
                DirtyState.AddChildren(ce, flags);
                MarkSubtreeDirty(ce, flags);
            }
        }
    }

    private static int CountDomNodes(Core.Dom.Node node)
    {
        int count = node is Core.Dom.Element ? 1 : 0;
        foreach (var child in node.Children)
            count += CountDomNodes(child);
        return count;
    }

    private static int CountLayoutBoxes(Core.Dom.Node node)
    {
        int count = (node is Core.Dom.Element el && el.LayoutBox != null) ? 1 : 0;
        foreach (var child in node.Children)
            count += CountLayoutBoxes(child);
        return count;
    }

    /// <summary>
    /// Public escape hatch: a debugging snapshot of the current performance
    /// state. Exposed so the dev tools panel can show a single JSON view of
    /// style/layout/paint timings, long tasks, and memory pressure level.
    /// </summary>
    public string GetPerformanceSnapshot() => _perfHub?.Api.Snapshot() ?? "{}";

    #endregion

    private static string? PickFolder(string title, IntPtr? hwndOwner = null)
    {
        if (OperatingSystem.IsWindows())
            return PickFolderWindows(title, hwndOwner);
        if (OperatingSystem.IsMacOS())
            return PickFolderMac(title);
        if (OperatingSystem.IsLinux())
            return PickFolderLinux(title);
        return null;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint SHBrowseForFolderW(ref BROWSEINFOW bi);

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListW(nint pidl, System.Text.StringBuilder path);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct BROWSEINFOW
    {
        public nint hwndOwner;
        public nint pidlRoot;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string? pszDisplayName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string? lpszTitle;
        public uint ulFlags;
        public nint lpfn;
        public nint lParam;
        public int iImage;
    }

    private static string? PickFolderWindows(string title, IntPtr? hwndOwner)
    {
        var bi = new BROWSEINFOW
        {
            lpszTitle = title,
            ulFlags = 0x0001, // BIF_RETURNONLYFSDIRS
            hwndOwner = hwndOwner ?? IntPtr.Zero  // 附属到主窗口
        };
        nint pidl = SHBrowseForFolderW(ref bi);
        if (pidl == 0) return null;
        var sb = new System.Text.StringBuilder(260);
        if (!SHGetPathFromIDListW(pidl, sb)) return null;
        try { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pidl); } catch { }
        var path = sb.ToString();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static string? PickFolderLinux(string title)
    {
        // Linux: shell out to zenity if available (GNOME, KDE etc.)
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "zenity",
                Arguments = $"--file-selection --directory --title=\"{title}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p != null)
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    return output;
            }
        }
        catch { /* zenity not available */ }
        return null;
    }

    private static string? PickFolderMac(string title)
    {
        // macOS: use AppleScript to open a native folder picker
        try
        {
            var escaped = title.Replace("\"", "\\\"");
            var script = $"set f to choose folder with prompt \"{escaped}\"\nreturn POSIX path of f";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e \"{script}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p != null)
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    return output;
            }
        }
        catch { }
        return null;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    /// <summary>
    /// 智能扫描引擎目录，自动匹配当前运行时的 TFM 子文件夹。
    /// 返回包含复制结果的结构体。
    /// </summary>
    private static (bool Success, string? AssemblyPath, string? ErrorMessage) ScanAndCopyEngineDlls(
        string sourceDir, string destDir, JsEngineType type)
    {
        var expectedDll = type switch
        {
            JsEngineType.V8 => "JavaScriptEngineSwitcher.V8.dll",
            JsEngineType.Jurassic => "JavaScriptEngineSwitcher.Jurassic.dll",
            _ => null
        };

        if (expectedDll == null)
            return (false, null, "不支持的引擎类型");

        var targetAssemblyPath = Path.Combine(destDir, expectedDll);
        bool found = false;

        // 收集所有 TFM 子文件夹
        var tfmFolders = Directory.EnumerateDirectories(sourceDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && (n.StartsWith("net") || n.StartsWith("netstandard")))
            .ToList();

        // 排序：优先匹配当前运行时
        tfmFolders.Sort((a, b) =>
        {
            int Score(string name) => name switch
            {
                _ when name.Contains("net8") => 100,
                _ when name.Contains("net7") => 90,
                _ when name.Contains("net6") => 80,
                _ when name.Contains("netstandard2.1") => 70,
                _ when name.Contains("netstandard2.0") => 60,
                _ when name.Contains("netstandard") => 50,
                _ when name.Contains("net4") => 40,
                _ => 30
            };
            return Score(b) - Score(a);
        });

        // 策略1：在根目录直接查找
        var rootDll = Directory.EnumerateFiles(sourceDir, expectedDll).FirstOrDefault();
        if (!string.IsNullOrEmpty(rootDll))
        {
            File.Copy(rootDll, targetAssemblyPath, true);
            found = true;
            Console.WriteLine($"[Engine] Found {expectedDll} in root directory");
        }

        // 策略2：在 TFM 子文件夹中查找
        if (!found)
        {
            foreach (var tfm in tfmFolders)
            {
                var dllInTfm = Path.Combine(sourceDir, tfm, expectedDll);
                if (File.Exists(dllInTfm))
                {
                    File.Copy(dllInTfm, targetAssemblyPath, true);
                    found = true;
                    Console.WriteLine($"[Engine] Found {expectedDll} in {tfm} folder");
                    break;
                }
            }
        }

        // 策略3：在整个目录树中搜索（兜底）
        if (!found)
        {
            var allMatches = Directory.EnumerateFiles(sourceDir, expectedDll, SearchOption.AllDirectories).ToList();
            if (allMatches.Count > 0)
            {
                File.Copy(allMatches[0], targetAssemblyPath, true);
                found = true;
                Console.WriteLine($"[Engine] Found {expectedDll} in subdirectory: {allMatches[0]}");
            }
        }

        // 如果还是没找到，给出详细错误信息
        if (!found)
        {
            var allDlls = Directory.EnumerateFiles(sourceDir, "*.dll", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            var dllList = string.Join(", ", allDlls);
            return (false, null,
                $"未找到 {expectedDll}\n\n该目录中包含的 DLL 文件:\n{dllList}\n\n提示:\n- V8 引擎需要 JavaScriptEngineSwitcher.V8.dll 和 ClearScript.V8.dll\n- V8 原生库位于 runtimes/{JsEngineDownloader.GetRuntimeIdentifier()}/native/ 目录中\n- Jurassic 引擎需要 JavaScriptEngineSwitcher.Jurassic.dll\n- 请从 NuGet 包中复制对应的文件，或选择正确的目录");
        }

        // 同时复制所有依赖 DLL（从根目录和匹配的 TFM 目录）
        var copiedCount = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*.dll"))
        {
            var name = Path.GetFileName(file);
            if (name != expectedDll)
            {
                var destFile = Path.Combine(destDir, name);
                File.Copy(file, destFile, true);
                copiedCount++;
            }
        }

        // 也复制 TFM 子文件夹中的依赖 DLL
        foreach (var tfm in tfmFolders)
        {
            var tfmPath = Path.Combine(sourceDir, tfm);
            if (Directory.Exists(tfmPath))
            {
                foreach (var file in Directory.EnumerateFiles(tfmPath, "*.dll"))
                {
                    var name = Path.GetFileName(file);
                    if (name != expectedDll && !File.Exists(Path.Combine(destDir, name)))
                    {
                        File.Copy(file, Path.Combine(destDir, name), true);
                        copiedCount++;
                    }
                }
            }
        }

        Console.WriteLine($"[Engine] Copied {copiedCount} additional DLLs");

        // 策略4：复制原生运行时 DLL
        // V8 需要 ClearScript 原生库（如 ClearScriptV8.win-x64.dll），托管 DLL 跨平台通用
        if (type == JsEngineType.V8)
        {
            var rid = JsEngineDownloader.GetRuntimeIdentifier();
            var osPrefix = rid.Split('-')[0];

            // 路径A：NuGet 包结构 runtimes/{rid}/native/
            // 路径B：用户直接整理的平台目录结构 {win-x64}/
            bool nativeCopied = false;

            // 先尝试 NuGet 包结构
            var runtimesDir = Path.Combine(sourceDir, "runtimes");
            if (Directory.Exists(runtimesDir))
            {
                var nativeDir = Path.Combine(runtimesDir, rid, "native");
                if (!Directory.Exists(nativeDir))
                {
                    var matchingDirs = Directory.EnumerateDirectories(runtimesDir)
                        .Where(d => Path.GetFileName(d).StartsWith(osPrefix))
                        .ToList();
                    if (matchingDirs.Count > 0)
                        nativeDir = Path.Combine(matchingDirs[0], "native");
                }

                if (Directory.Exists(nativeDir))
                {
                    int n = 0;
                    foreach (var file in Directory.EnumerateFiles(nativeDir))
                    {
                        File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
                        n++;
                    }
                    Console.WriteLine($"[Engine] Copied {n} native runtime files from {nativeDir}");
                    nativeCopied = true;
                }
            }

            // 再尝试用户整理的平台目录结构（如 win-x64/ 直接放在源目录下）
            if (!nativeCopied)
            {
                // 先找精确匹配当前 RID 的目录
                var platformDir = Path.Combine(sourceDir, rid);
                if (!Directory.Exists(platformDir))
                {
                    // 没找到精确匹配，尝试前缀匹配（如 win-*）
                    var matchingDirs = Directory.EnumerateDirectories(sourceDir)
                        .Where(d => Path.GetFileName(d).StartsWith(osPrefix))
                        .ToList();
                    if (matchingDirs.Count > 0)
                        platformDir = matchingDirs[0];
                }

                if (Directory.Exists(platformDir))
                {
                    int n = 0;
                    foreach (var file in Directory.EnumerateFiles(platformDir))
                    {
                        var name = Path.GetFileName(file);
                        // 原生 DLL 有平台后缀（如 ClearScriptV8.win-x64.dll），托管 DLL 没有
                        if (name.Contains(osPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(file, Path.Combine(destDir, name), true);
                            n++;
                            Console.WriteLine($"[Engine] Copied native: {name}");
                        }
                        else
                        {
                            // 托管 DLL（如 ClearScript.Core.dll, Newtonsoft.Json.dll）跨平台通用
                            // 只复制一次，避免重复覆盖
                            var destFile = Path.Combine(destDir, name);
                            if (!File.Exists(destFile))
                            {
                                File.Copy(file, destFile, true);
                                n++;
                                Console.WriteLine($"[Engine] Copied managed: {name}");
                            }
                        }
                    }
                    Console.WriteLine($"[Engine] Copied {n} files from platform directory {platformDir}");
                    nativeCopied = true;
                }
            }

            if (!nativeCopied)
            {
                Console.WriteLine($"[Engine] No native runtime directory found for {rid}. " +
                    "V8 引擎需要 ClearScript 原生库（ClearScriptV8.win-x64.dll 等），请确保选择包含该文件的目录。");
            }
        }

        // 验证关键文件是否存在
        if (type == JsEngineType.V8)
        {
            // 列出目标目录中所有文件用于调试
            var destFiles = Directory.EnumerateFiles(destDir).Select(Path.GetFileName).ToList();
            Console.WriteLine($"[Engine] Files in {destDir}: {string.Join(", ", destFiles)}");

            // V8 需要 ClearScript.V8.dll
            var clearScriptPath = Path.Combine(destDir, "ClearScript.V8.dll");
            if (!File.Exists(clearScriptPath))
            {
                Console.WriteLine($"[Engine] Warning: ClearScript.V8.dll not found in {destDir}, V8 may not work");
                Console.WriteLine($"[Engine] Please ensure the selected directory contains ClearScript.V8.dll from the NuGet package.");
            }
            // 检查原生运行时 DLL（如 ClearScriptV8.win-x64.dll）
            var nativeCount = Directory.EnumerateFiles(destDir, "ClearScriptV8*.*").Count();
            if (nativeCount == 0)
            {
                Console.WriteLine($"[Engine] Warning: No ClearScriptV8 native DLL found in {destDir}");
                var rid = JsEngineDownloader.GetRuntimeIdentifier();
                Console.WriteLine($"[Engine] The ClearScript native library (ClearScriptV8.{rid}.dll) should be in the runtimes/{rid}/native/ folder of the NuGet package.");
            }
        }

        return (true, targetAssemblyPath, null);
    }
}

/// <summary>
/// Memory responder that shrinks the decoded-image pool in proportion to the
/// reported pressure level. Wired in <see cref="BrowserApp.InitializePerformanceHub"/>.
/// </summary>
internal sealed class ImagePoolPressureAdapter : Acrux.Core.Performance.Memory.MemoryResponder
{
    private readonly Acrux.Core.Performance.Resources.DecodedImagePool _pool;
    private long _originalCapacity;

    public ImagePoolPressureAdapter(Acrux.Core.Performance.Resources.DecodedImagePool pool)
    {
        _pool = pool;
        _originalCapacity = pool.CapacityBytes;
    }

    public override string Name => "image-pool";

    public override void OnMemoryPressure(Acrux.Core.Performance.Memory.MemoryPressureLevel level)
    {
        long factor = level switch
        {
            Acrux.Core.Performance.Memory.MemoryPressureLevel.Critical => 4,
            Acrux.Core.Performance.Memory.MemoryPressureLevel.High => 2,
            Acrux.Core.Performance.Memory.MemoryPressureLevel.Moderate => 1,
            _ => 0,
        };
        if (factor == 0) return;
        long target = Math.Max(1L * 1024 * 1024, _originalCapacity / factor);
        _pool.SetCapacity(target);
    }

    public override void OnMemoryRelease(Acrux.Core.Performance.Memory.MemoryPressureLevel level)
    {
        if (level <= Acrux.Core.Performance.Memory.MemoryPressureLevel.Moderate)
        {
            _pool.SetCapacity(_originalCapacity);
        }
    }
}



