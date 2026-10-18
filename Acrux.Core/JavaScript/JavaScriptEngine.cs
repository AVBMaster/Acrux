using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DomDocument = Acrux.Core.Dom.Document;
using DomElement = Acrux.Core.Dom.Element;
using DomMutationTracker = Acrux.Core.Dom.DomMutationTracker;

namespace Acrux.Core.JavaScript;

public class JavaScriptEngine : IDisposable
{
    private IJavaScriptEngineAdapter? _adapter;
    private bool _disposed;
    private DocumentHost? _documentHost;
    private DomDocument? _currentDocument;
    private int _nextTimerId = 1;
    private readonly Dictionary<int, TimerInfo> _timers = new();
    private readonly object _timersLock = new();

    private int _nextFetchId = 1;
    private readonly Dictionary<int, (int resolveId, int rejectId)> _fetchCallbacks = new();
    private readonly Dictionary<int, FetchResult> _fetchResults = new();
    private readonly object _fetchLock = new();

    private LocationHost? _locationHost;
    private AcruxBuiltins? _builtins;

    private JsIntegrationService? _integrationService;

    [ThreadStatic]
    public static JavaScriptEngine? Current;

    public JsEngineType EngineType => _adapter?.EngineType ?? JsEngineType.Jint;
    public LocationHost? LocationHost => _locationHost;
    public AcruxBuiltins? Builtins => _builtins;
    public DocumentHost? DocumentHost => _documentHost;
    public IJavaScriptEngineAdapter? Adapter => _adapter;
    public JsIntegrationService? IntegrationService => _integrationService;

    public event Action? OnDomChanged;
    public Func<string, string?, string?>? ShowDialog { get; set; }
    public bool HasTimers { get { lock (_timersLock) return _timers.Count > 0; } }
    public int TimerCount { get { lock (_timersLock) return _timers.Count; } }
    public void SetWindowSize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    public int GetHeapSizeKB()
    {
        return (int)(GC.GetTotalMemory(false) / 1024);
    }

    internal object? InnerEngine => _adapter?.InnerEngine;

    public JavaScriptEngine(int tabIndex = -1) : this(CreateDefaultAdapter(tabIndex))
    {
    }

    public JavaScriptEngine(IJavaScriptEngineAdapter adapter)
    {
        _adapter = adapter;
        _integrationService = new JsIntegrationService(adapter);
        _integrationService.SetJsEngine(this);

#if USE_MULTIPLE_JS_ENGINE
        if (adapter is RemoteJsEngineAdapter remote)
        {
            remote.OnAlert = msg => ShowDialog?.Invoke(msg, "alert");
            remote.OnConfirm = msg => ShowDialog?.Invoke(msg, "confirm") == "true";
            remote.OnPrompt = (msg, def) => ShowDialog?.Invoke(msg, "prompt:" + def);
            remote.OnGetInnerWidth = () => _windowWidth;
            remote.OnGetInnerHeight = () => _windowHeight;
            remote.OnScrollTo = (x, y) => { _pendingScrollX = x; _pendingScrollY = y; };
            remote.OnScrollBy = (x, y) => { _pendingScrollX += x; _pendingScrollY += y; };
            remote.OnEngineAction = (action, name) =>
            {
                return _builtins?.EngineAction?.Invoke(action, name)
                    ?? AcruxBuiltins.GlobalEngineAction?.Invoke(action, name)
                    ?? "{\"success\":false,\"error\":\"no handler\"}";
            };
        }
#endif

        SetupGlobals();
    }

    private int _windowWidth = 1024;
    private int _windowHeight = 768;
    private int _pendingScrollX;
    private int _pendingScrollY;

    private static IJavaScriptEngineAdapter CreateDefaultAdapter(int tabIndex)
    {
#if USE_MULTIPLE_JS_ENGINE
        var effectiveType = JsEngineConfig.EffectiveEngineType;

        // 尝试使用远程 JS 引擎（通过 IPC 与 JsEngineHost 进程通信）
        if (TryCreateRemoteAdapter(effectiveType, tabIndex, out var remoteAdapter))
            return remoteAdapter;

        // 远程引擎不可用时，使用 NullAdapter 兜底，避免 UI 进程因 JS 问题崩溃
        Console.WriteLine("[JS] Remote engine unavailable, using NullAdapter (graceful degradation)");
        return NullJsEngineAdapter.Instance;
#else
        // 单进程模式：直接使用浏览器内置的 Jint 引擎，无 IPC 开销
        return new JintEngineAdapter();
#endif
    }

#if USE_MULTIPLE_JS_ENGINE
    private static bool TryCreateRemoteAdapter(JsEngineType type, int tabIndex, out IJavaScriptEngineAdapter adapter)
    {
        adapter = null!;
        try
        {
            Console.WriteLine($"[JS] TryCreateRemoteAdapter tab={tabIndex}: BaseDirectory={AppContext.BaseDirectory}");
            var hostExe = FindHostExe();
            if (hostExe == null)
            {
                Console.WriteLine("[JS] Remote engine not available: FindHostExe returned null");
                // 列出当前目录中的文件用于调试
                if (Directory.Exists(AppContext.BaseDirectory))
                {
                    var files = Directory.EnumerateFiles(AppContext.BaseDirectory, "Acrux.JsEngineHost*").ToList();
                    Console.WriteLine($"[JS] Files in BaseDirectory: {string.Join(", ", files)}");
                }
                return false;
            }

            Console.WriteLine($"[JS] Starting remote engine for tab={tabIndex}: {hostExe}");
            var engineTypeStr = type.ToString();
            var remote = EngineProcessManager.GetOrCreate(tabIndex, engineTypeStr);
            adapter = remote;
            Console.WriteLine($"[JS] Remote engine started successfully for tab={tabIndex}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JS] Remote engine failed for tab={tabIndex}: {ex.Message}");
            return false;
        }
    }

    private static string? FindHostExe()
    {
        // 1. 当前目录（发布后）
        var exe = Path.Combine(AppContext.BaseDirectory, "Acrux.JsEngineHost.exe");
        if (File.Exists(exe)) return exe;
        var dll = Path.Combine(AppContext.BaseDirectory, "Acrux.JsEngineHost.dll");
        if (File.Exists(dll)) return dll;

        // 2. 开发环境：从项目输出目录查找
        var baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;

            // 检查 DLL（无 RuntimeIdentifier 的 build 输出）
            var devDll = Path.Combine(baseDir, "Acrux.JsEngineHost", "bin", "Debug", "net10.0", "Acrux.JsEngineHost.dll");
            if (File.Exists(devDll)) return devDll;
            var releaseDll = Path.Combine(baseDir, "Acrux.JsEngineHost", "bin", "Release", "net10.0", "Acrux.JsEngineHost.dll");
            if (File.Exists(releaseDll)) return releaseDll;

            // 检查 EXE（带 RuntimeIdentifier 的 build 输出）
            var ridExe = Path.Combine(baseDir, "Acrux.JsEngineHost", "bin", "Debug", "net10.0", "win-x64", "Acrux.JsEngineHost.exe");
            if (File.Exists(ridExe)) return ridExe;
            var ridReleaseExe = Path.Combine(baseDir, "Acrux.JsEngineHost", "bin", "Release", "net10.0", "win-x64", "Acrux.JsEngineHost.exe");
            if (File.Exists(ridReleaseExe)) return ridReleaseExe;
        }

        return null;
    }
#endif

    private void SetupGlobals()
    {
        if (_adapter == null) return;

        _adapter.Execute(JsCallbackStore.JsSetup);

        if (JsEngineBridge.IsRemote(_adapter))
        {
            _adapter.Execute(GetSetupScript());
            return;
        }

        var consoleHost = new ConsoleHost();
        consoleHost.DevToolsConsole = new JsDevToolsConsole();
        _adapter.SetGlobal("console", consoleHost);
        _builtins = new AcruxBuiltins(this);
        _adapter.SetGlobal("__acrux", _builtins);
        _adapter.SetGlobal("__win", new WindowHost(this));
        _adapter.SetGlobal("navigator", new NavigatorHost());
        _adapter.SetGlobal("CSS", new CssObjectHost());
        _locationHost = new LocationHost();
        _adapter.SetGlobal("location", _locationHost);
        _adapter.SetGlobal("history", new HistoryHost(_locationHost));
        _adapter.SetGlobal("screen", new ScreenHost());
        // Web Storage §5.1: the area belongs to the origin, not to the document, so the two hosts are
        // built once and handed to every document the engine runs — a page that wrote an item and was
        // navigated still reads it back.
        _adapter.SetGlobal("localStorage", StorageArea("localStorage"));
        _adapter.SetGlobal("sessionStorage", StorageArea("sessionStorage"));

        _adapter.SetGlobal("document", new DocumentHost(new Acrux.Core.Dom.Document()));

        _adapter.Execute(GetSetupScript());
    }

    public void LoadDocument(DomDocument document)
    {
        _currentDocument = document;
        _documentHost = new DocumentHost(document);
        _documentHost.Engine = this;
        // The address the page was navigated to is the document's own URL, and 'location' reads the
        // same one — a page that asks 'location.href' after a navigation must not be told about the
        // engine's default address instead.
        _locationHost?.SetDocumentUrl(document.Url);

        if (_adapter != null)
        {
            ClearState();
#if USE_MULTIPLE_JS_ENGINE
            var remoteAdapter = _adapter as RemoteJsEngineAdapter;
            if (remoteAdapter != null)
            {
                if (remoteAdapter.DomStore == null)
                    remoteAdapter.DomStore = new DomProxyStore();
                remoteAdapter.DomStore.Clear();
                remoteAdapter.DomStore.SetDocument(_documentHost);
            }
#endif
            _integrationService?.LoadDocument(document);
            ReapplyGlobals();
            if (!JsEngineBridge.IsRemote(_adapter))
                _adapter.SetGlobal("document", _documentHost);
        }

        MarkDirty();
    }

    private StorageHost? _localStorageHost;
    private StorageHost? _sessionStorageHost;

    /// <summary>Web Storage §5.1: the storage area belongs to the origin and the document set, not to
    /// one document, so the engine keeps the two areas and hands the same one to every page it runs.
    /// A page that wrote an item before a navigation reads it back afterwards.</summary>
    private StorageHost StorageArea(string name)
    {
        if (name == "sessionStorage") return _sessionStorageHost ??= new StorageHost(name);
        return _localStorageHost ??= new StorageHost(name);
    }

    private void ReapplyGlobals()
    {
        if (_adapter == null) return;


        // 清除 JS 端的元素缓存，避免旧页面的代理对象残留
        _adapter.Execute("if (typeof globalThis !== 'undefined') { globalThis.__elCache = {}; }");

        _adapter.Execute(JsCallbackStore.JsSetup);

        if (JsEngineBridge.IsRemote(_adapter))
        {
            _adapter.Execute(GetSetupScript());
            return;
        }

        if (_builtins != null)
            _adapter.SetGlobal("__acrux", _builtins);
        _adapter.SetGlobal("__win", new WindowHost(this));
        _adapter.SetGlobal("navigator", new NavigatorHost());
        _adapter.SetGlobal("CSS", new CssObjectHost());
        if (_locationHost != null)
            _adapter.SetGlobal("location", _locationHost);
        _adapter.SetGlobal("history", new HistoryHost(_locationHost ?? new LocationHost()));
        _adapter.SetGlobal("screen", new ScreenHost());
        _adapter.SetGlobal("localStorage", StorageArea("localStorage"));
        _adapter.SetGlobal("sessionStorage", StorageArea("sessionStorage"));
        _adapter.SetGlobal("console", new ConsoleHost { DevToolsConsole = new JsDevToolsConsole() });

        _adapter.Execute(GetSetupScript());
    }

    public void ClearState()
    {
        if (_adapter == null) return;

        _adapter.ClearCallbacks();

        lock (_timersLock)
        {
            foreach (var info in _timers.Values)
                _adapter.RemoveCallback(info.CallbackId);
            _timers.Clear();
        }

        lock (_fetchLock)
        {
            foreach (var cbs in _fetchCallbacks.Values)
            {
                _adapter.RemoveCallback(cbs.resolveId);
                _adapter.RemoveCallback(cbs.rejectId);
            }
            _fetchCallbacks.Clear();
            _fetchResults.Clear();
        }

        if (_adapter != null)
        {
            try
            {
                var ch = _adapter.GetGlobal<ConsoleHost>("console");
                ch?.Reset();
            }
            catch { }
        }
    }

    public void Execute(string code, string? sourceUrl = null, ScriptType type = ScriptType.Inline, int lineOffset = 0)
    {
        if (_adapter == null || string.IsNullOrEmpty(code)) return;
        // The ambient engine is put back, not cleared: a script run from inside a callback — a timer, a
        // listener, the 'on...' attribute the markup carries — finishes while the script that raised it
        // is still running, and leaving this null made every node read afterwards fall back to a fresh
        // wrapper, so the page held two objects for one node.
        var previous = Current;
        Current = this;
        try
        {
            _integrationService?.ExecuteScript(code, sourceUrl, type, lineOffset);
            MarkDirty();
        }
        finally
        {
            Current = previous;
        }
    }

    public object? Evaluate(string expression)
    {
        if (_adapter == null) return null;
        var previous = Current;
        Current = this;
        try
        {
            return _adapter.Evaluate(expression);
        }
        catch
        {
            return null;
        }
        finally
        {
            Current = previous;
        }
    }

    public object? CallJsFunction(string functionName, params object?[] args)
    {
        if (_adapter == null) return null;
        var previous = Current;
        Current = this;
        try
        {
            return _adapter.CallFunction(functionName, args);
        }
        catch
        {
            return null;
        }
        finally
        {
            Current = previous;
        }
    }

    public void SetGlobal(string name, object? value)
    {
        _adapter?.SetGlobal(name, value);
    }

    public ElementHost? GetElementHost(DomElement element)
    {
        return new ElementHost(element);
    }

    public bool DispatchEvent(DomElement element, string eventType)
    {
        var previous = Current;
        Current = this;
        try
        {
            var targetHost = GetDispatchHost(element);
            var evt = new ScriptEvent(eventType, targetHost);
            return targetHost.DispatchEvent(evt);
        }
        finally
        {
            Current = previous;
        }
    }

    public bool DispatchEvent(DomElement element, ScriptEvent evt)
    {
        var previous = Current;
        Current = this;
        try
        {
            // Must dispatch on the identity-mapped wrapper: addEventListener
            // stores its callbacks on that instance, a fresh host would drop them.
            var targetHost = GetDispatchHost(element);
            return targetHost.DispatchEvent(evt);
        }
        finally
        {
            Current = previous;
        }
    }

    /// <summary>
    /// The ElementHost that JS sees for this element (identity-mapped), creating
    /// it on first use. Callers building custom events (relatedTarget, ...) use
    /// this as the event target so listeners registered via addEventListener fire.
    /// </summary>
    public ElementHost GetDispatchHost(DomElement element)
    {
        if (_integrationService?.WrapDomNode(element) is ElementHost mapped)
        {
            mapped.Engine ??= this;
            return mapped;
        }
        var host = new ElementHost(element);
        host.Engine = this;
        return host;
    }

    internal int StoreCallbackRef(object callback)
    {
        return _adapter?.StoreCallback(callback) ?? 0;
    }

    internal void InvokeCallback(int cbId)
    {
        var v0 = DomMutationTracker.Version;
        _adapter?.InvokeCallback(cbId);
        if (DomMutationTracker.Version != v0) MarkDirty();
    }

    internal void InvokeCallbackWith(int cbId, object arg)
    {
        var v0 = DomMutationTracker.Version;
        _adapter?.InvokeCallbackWith(cbId, arg);
        if (DomMutationTracker.Version != v0) MarkDirty();
    }

    internal void RemoveCallback(int cbId)
    {
        _adapter?.RemoveCallback(cbId);
    }

    public void TickTimers()
    {
        List<int> due = new();
        lock (_timersLock)
        {
            var now = Environment.TickCount64;
            foreach (var kv in _timers.ToList())
            {
                if (now >= kv.Value.DueTime)
                {
                    due.Add(kv.Key);
                    if (kv.Value.Interval > 0)
                        kv.Value.DueTime = now + kv.Value.Interval;
                    else
                        _timers.Remove(kv.Key);
                }
            }
        }

        foreach (var id in due)
        {
            InvokeTimer(id);
        }

        var timersPrevious = Current;
        Current = this;
        try { ProcessCompletedFetches(); }
        finally { Current = timersPrevious; }
    }

    private void InvokeTimer(int timerId)
    {
        int? cbId;
        lock (_timersLock)
        {
            if (_timers.TryGetValue(timerId, out var info))
                cbId = info.CallbackId;
            else
                cbId = null;
        }
        if (cbId == null) return;
        var previous = Current;
        Current = this;
        var v0 = DomMutationTracker.Version;
        try
        {
            _adapter?.InvokeCallback(cbId.Value);
            if (DomMutationTracker.Version != v0) MarkDirty();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JS Timer] {ex.Message}");
        }
        finally
        {
            Current = previous;
        }
    }

    public bool NeedsReLayout { get; set; }

    // Diagnostics only (ACRUX_TAB_STATS=1): who marked the engine dirty.
    internal static readonly bool TrackDirtySource =
        Environment.GetEnvironmentVariable("ACRUX_TAB_STATS") == "1";
    public string? DirtyTrace { get; set; }

    public void MarkDirty()
    {
        NeedsReLayout = true;
        if (TrackDirtySource) DirtyTrace ??= Environment.StackTrace;
        OnDomChanged?.Invoke();
    }

    public void ClearDirty()
    {
        NeedsReLayout = false;
        DirtyTrace = null;
    }

    /// <summary>
    /// Runs the pending style and layout work. Installed by whoever owns the frame
    /// pipeline (the snapshot helper, the page engine) because only they can re-run
    /// those two passes.
    /// </summary>
    public Action? ForceUpdateStyleAndLayout { get; set; }

    private long _forcedUpdateVersion = -1;
    private bool _inForcedUpdate;

    /// <summary>
    /// Flushes style and layout before a synchronous DOM read. A page that changes a
    /// style and then measures the box in the same script has to see its own change:
    /// getComputedStyle, getBoundingClientRect and the offset, client and scroll
    /// accessors all force the pending work first (CSSOM "update the rendering",
    /// "getClientRects() for an element"). Without it every such read returns the
    /// numbers of the previous frame.
    /// </summary>
    public void FlushForRead()
    {
        var hook = ForceUpdateStyleAndLayout;
        if (hook == null || _inForcedUpdate) return;

        long version = DomMutationTracker.Version;
        if (version == _forcedUpdateVersion) return;

        _forcedUpdateVersion = version;
        _inForcedUpdate = true;
        try { hook(); }
        finally { _inForcedUpdate = false; }
    }

    /// <summary>HTML §5.6: the traversal to an entry the page pushed is answered with a 'popstate' on
    /// the window, carrying the state that entry holds. The event is built in script — the window's
    /// listeners live there, the interface's own constructor knows the shape, and the state is read
    /// from the history the page already holds — so the bridge carries nothing across.</summary>
    public void FirePopState()
    {
        // The state the event carries is the entry the history is standing on, and the script reads
        // it from there — a value that came out of the engine once and is handed back again is not
        // something the bridge should be moving across.
        try { IntegrationService?.CallGlobal("__acruxPopState"); }
        catch { /* a page that replaced the bridge keeps its own hearing */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_timersLock) _timers.Clear();
        _adapter?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    internal int SetTimer(int callbackId, int delayMs, bool recurring)
    {
        if (_integrationService != null)
        {
            if (recurring)
                return _integrationService.SetInterval(callbackId, delayMs);
            return _integrationService.SetTimeout(callbackId, delayMs);
        }

        lock (_timersLock)
        {
            var id = _nextTimerId++;
            _timers[id] = new TimerInfo
            {
                CallbackId = callbackId,
                DueTime = Environment.TickCount64 + Math.Max(1, delayMs),
                Interval = recurring ? Math.Max(1, delayMs) : 0
            };
            return id;
        }
    }

    internal void ClearTimer(int id)
    {
        _integrationService?.ClearTimer(id);

        lock (_timersLock)
        {
            if (_timers.TryGetValue(id, out var info))
            {
                _adapter?.RemoveCallback(info.CallbackId);
                _timers.Remove(id);
            }
        }
    }

    internal int AddFetch(int resolveId, int rejectId)
    {
        var id = Interlocked.Increment(ref _nextFetchId);
        lock (_fetchLock)
            _fetchCallbacks[id] = (resolveId, rejectId);
        return id;
    }

    /// <summary>Beacon (Beacon §2): a POST the page never waits on. The body is queued on the
    /// shared HTTP client as a fire-and-forget task — a send that fails or outlives the document is
    /// dropped silently, exactly as the specification permits, because the caller has no way to see
    /// the outcome.</summary>
    internal void QueueBeacon(string url, string body)
    {
        try
        {
            var client = new System.Net.Http.HttpClient();
            var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "text/plain");
            _ = client.PostAsync(url, content).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        catch { }
    }

    internal void CompleteFetch(int id, FetchResult result)
    {
        lock (_fetchLock)
            _fetchResults[id] = result;
    }

    private void ProcessCompletedFetches()
    {
        List<(int id, FetchResult result)> completed;
        lock (_fetchLock)
        {
            completed = _fetchResults.Select(kv => (kv.Key, kv.Value)).ToList();
            _fetchResults.Clear();
        }

        foreach (var (id, result) in completed)
        {
            lock (_fetchLock)
            {
                if (!_fetchCallbacks.TryGetValue(id, out var cbs)) continue;
                _fetchCallbacks.Remove(id);

                try
                {
                    if (result.Success)
                    {
                        var jsonObj = new JsonObject
                        {
                            ["ok"] = (JsonNode?)JsonValue.Create(result.Status >= 200 && result.Status < 300),
                            ["status"] = (JsonNode?)JsonValue.Create(result.Status),
                            ["statusText"] = (JsonNode?)JsonValue.Create(result.StatusText ?? ""),
                            ["data"] = (JsonNode?)JsonValue.Create(result.Data ?? ""),
                            ["headers"] = result.Headers != null
                                ? JsonSerializer.SerializeToNode(result.Headers, AcruxJsonContext.Default.DictionaryStringString)
                                : null
                        };
                        var json = jsonObj.ToJsonString();
                        _adapter?.Execute($"__g_invoke({cbs.resolveId}, JSON.parse('{EscapeJsString(json)}'))");
                    }
                    else
                    {
                        _adapter?.Execute($"__g_invoke({cbs.rejectId}, '{EscapeJsString(result.Error ?? "Unknown error")}')");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fetch] callback error: {ex.Message}");
                }
            }
        }
    }

    private static string GetSetupScript()
    {
        return @"
            var window = this;
            var globalThis = this;
            var self = this;

            // HTML §8.3: the delay is an optional argument, so the function's own length is one — the
            // rest of what the page passes arrives through 'arguments' the same way the extra
            // arguments to a timer callback do.
            function setTimeout(fn) {
                var id = __g_store(fn);
                return __acrux.setTimeout(id, arguments.length > 1 ? (arguments[1] || 0) : 0);
            }
            function setInterval(fn) {
                var id = __g_store(fn);
                return __acrux.setInterval(id, arguments.length > 1 ? (arguments[1] || 0) : 0);
            }
            function clearTimeout(id) { __acrux.clearTimeout(id); }
            function clearInterval(id) { __acrux.clearInterval(id); }
            function requestAnimationFrame(fn) { return setTimeout(fn, 16); }
            function cancelAnimationFrame(id) { return clearTimeout(id); }
            function alert(msg) { __win.alert(msg); }
            function confirm(msg) { return __win.confirm(msg); }
            function prompt(msg, def) { return __win.prompt(msg, def); }
            function atob(str) { return __acrux.atob(str); }
            function btoa(str) { return __acrux.btoa(str); }

            // The numeric and URI globals are ECMAScript's own, so the engine's version is the one a
            // page gets: 'parseInt' with the radix it was handed, 'parseFloat' reading the number that
            // is at the start of the text, and a value that is not a number answering NaN rather than
            // zero or a throw. The host has its own reading of each for an engine that brings nothing
            // with it — the out-of-process JavaScript host — so those are installed only where the name
            // is missing.
            (function () {
                var scope = typeof globalThis !== 'undefined' ? globalThis : window;
                var FALLBACKS = [
                    ['decodeURI', function (s) { return __acrux.decodeURI(s); }],
                    ['decodeURIComponent', function (s) { return __acrux.decodeURIComponent(s); }],
                    ['encodeURI', function (s) { return __acrux.encodeURI(s); }],
                    ['encodeURIComponent', function (s) { return __acrux.encodeURIComponent(s); }],
                    ['parseInt', function (s, r) { return __acrux.parseInt(s, r); }],
                    ['parseFloat', function (s) { return __acrux.parseFloat(s); }],
                    ['isNaN', function (v) { return __acrux.isNaN(v); }],
                    ['isFinite', function (v) { return __acrux.isFinite(v); }],
                    ['escape', function (s) { return __acrux.escape(s); }],
                    ['unescape', function (s) { return __acrux.unescape(s); }]
                ];
                for (var fi = 0; fi < FALLBACKS.length; fi++) {
                    if (typeof scope[FALLBACKS[fi][0]] === 'function') continue;
                    scope[FALLBACKS[fi][0]] = FALLBACKS[fi][1];
                }
            })();

            // Fetch §5.1: an aborted signal rejects with the reason it carries, and a reason the
            // page did not supply is the AbortError DOMException the spec names.
            function abortReason(signal) {
                var reason = signal ? signal.reason : undefined;
                if (reason !== undefined && reason !== null) return reason;
                return new DOMException('The operation was aborted.', 'AbortError');
            }

            function fetch(url, opts) {
                opts = opts || {};
                // Only what the request itself needs crosses to the host: an AbortSignal is a
                // page-side object whose members cannot be serialised into the options.
                var send = {};
                if (opts.method !== undefined && opts.method !== null) send.method = String(opts.method);
                if (opts.body !== undefined && opts.body !== null) send.body = String(opts.body);
                if (opts.headers !== undefined && opts.headers !== null) {
                    var headers = {};
                    if (typeof opts.headers.forEach === 'function') {
                        opts.headers.forEach(function (value, key) { headers[String(key)] = String(value); });
                    } else {
                        var names = Object.keys(opts.headers);
                        for (var i = 0; i < names.length; i++) headers[names[i]] = String(opts.headers[names[i]]);
                    }
                    send.headers = headers;
                }
                return new Promise(function (resolve, reject) {
                    var signal = opts.signal;
                    if (signal && signal.aborted) { reject(abortReason(signal)); return; }
                    var settled = false;
                    var handle = 0;
                    var onAbort = function () {
                        if (settled) return;
                        settled = true;
                        // The request is cancelled at the socket, not only at the promise: an
                        // abandoned download still costs bandwidth.
                        if (handle) __acrux.abortFetch(handle);
                        reject(abortReason(signal));
                    };
                    if (signal && typeof signal.addEventListener === 'function') {
                        signal.addEventListener('abort', onAbort);
                    }
                    var rid = __g_store(function (v) {
                        if (settled) return;
                        settled = true;
                        // The page gets a Response, not the host's payload: the status and the reason
                        // phrase, the headers as a Headers object and the body behind the read
                        // methods all belong to the interface (§6).
                        if (v && typeof v === 'object' && v.status !== undefined) {
                            resolve(new Response(v.data === undefined ? '' : String(v.data), {
                                status: v.status,
                                statusText: v.statusText === undefined ? '' : String(v.statusText),
                                headers: v.headers || null, url: String(url), type: 'basic'
                            }));
                        } else resolve(v);
                    });
                    var rjid = __g_store(function (e) { if (settled) return; settled = true; reject(e); });
                    handle = __acrux._fetch(url, JSON.stringify(send), rid, rjid);
                });
            }

            function XMLHttpRequest() {
                return __acrux.createXMLHttpRequest();
            }

            function URL(url, base) {
                return __acrux.createURL(url, base || '');
            }

            // URLSearchParams lives in script so its iteration (forEach, keys, values, entries and
            // the for..of protocol) reads like the standard's list-of-pairs; the host object it used
            // to wrap could not be made iterable across the interop boundary. Pairs keep insertion
            // order, which is what 'getAll' and 'toString' answer from.
            function URLSearchParams(init) {
                var self = this;
                var pairs = [];
                function dec(s) { try { return decodeURIComponent(String(s).replace(/\+/g, ' ')); } catch (e) { return String(s); } }
                function enc(s) { return encodeURIComponent(String(s)); }
                function find(k) { var out = []; for (var i = 0; i < pairs.length; i++) if (pairs[i][0] === k) out.push(pairs[i][1]); return out; }
                // A sequence of pairs initialises as the standard's sequence<sequence<USVString>>;
                // anything else (a query string) is parsed.
                if (init && typeof init !== 'string' && typeof init.forEach === 'function') {
                    var seed = [];
                    init.forEach(function (pair) { seed.push(pair); });
                    for (var si = 0; si < seed.length; si++) pairs.push([String(seed[si][0]), String(seed[si][1])]);
                } else {
                    var q = (init === null || init === undefined) ? '' : String(init);
                    if (q.charAt(0) === '?') q = q.slice(1);
                    if (q) q.split('&').forEach(function (kv) {
                        if (kv === '') return;
                        var i = kv.indexOf('=');
                        pairs.push([dec(i < 0 ? kv : kv.slice(0, i)), dec(i < 0 ? '' : kv.slice(i + 1))]);
                    });
                }
                this.append = function (k, v) { pairs.push([String(k), String(v)]); };
                this.delete = function (k) { k = String(k); for (var i = pairs.length - 1; i >= 0; i--) if (pairs[i][0] === k) pairs.splice(i, 1); };
                this.get = function (k) { var f = find(String(k)); return f.length ? f[0] : null; };
                this.getAll = function (k) { return find(String(k)); };
                this.has = function (k) { return find(String(k)).length > 0; };
                this.set = function (k, v) {
                    k = String(k); var done = false;
                    for (var i = 0; i < pairs.length; i++) if (pairs[i][0] === k) { if (!done) { pairs[i][1] = String(v); done = true; } else { pairs.splice(i, 1); i--; } }
                    if (!done) pairs.push([k, String(v)]);
                };
                this.sort = function () { pairs.sort(function (a, b) { return a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0; }); };
                this.toString = function () { return pairs.map(function (p) { return enc(p[0]) + '=' + enc(p[1]); }).join('&'); };
                this.forEach = function (cb, ts) { for (var i = 0; i < pairs.length; i++) cb.call(ts || null, pairs[i][1], pairs[i][0], self); };
                this.keys = function () { return pairs.map(function (p) { return p[0]; }); };
                this.values = function () { return pairs.map(function (p) { return p[1]; }); };
                this.entries = function () { return pairs.map(function (p) { return [p[0], p[1]]; }); };
                this[Symbol.iterator] = function () { return self.entries()[Symbol.iterator](); };
                Object.defineProperty(this, 'size', { get: function () { return pairs.length; } });
            }

            // CSSOM View §5.1 — DOMRectReadOnly, and the writable DOMRect over it. Four numbers in, the
            // rectangle and its derived edges out; a negative width or height flips the origin the way
            // the geometry model says it must. The reads live on the prototype so the object prints and
            // tests as the interface it is, and 'toJSON' answers the eight numbers the reference gives.
            function rectShape(x, y, w, h) {
                x = x === undefined ? 0 : Number(x);
                y = y === undefined ? 0 : Number(y);
                w = w === undefined ? 0 : Number(w);
                h = h === undefined ? 0 : Number(h);
                return {
                    x: Math.min(x, x + w), y: Math.min(y, y + h),
                    width: Math.abs(w), height: Math.abs(h)
                };
            }
            function DOMRectReadOnly(x, y, w, h) {
                var made = rectShape(x, y, w, h);
                Object.defineProperties(this, {
                    x: { value: made.x, enumerable: true, configurable: true, writable: true },
                    y: { value: made.y, enumerable: true, configurable: true, writable: true },
                    width: { value: made.width, enumerable: true, configurable: true, writable: true },
                    height: { value: made.height, enumerable: true, configurable: true, writable: true },
                    left: { value: made.x, enumerable: true, configurable: true, writable: true },
                    top: { value: made.y, enumerable: true, configurable: true, writable: true },
                    right: { value: made.x + made.width, enumerable: true, configurable: true, writable: true },
                    bottom: { value: made.y + made.height, enumerable: true, configurable: true, writable: true }
                });
            }
            DOMRectReadOnly.prototype.toJSON = function () {
                return { x: this.x, y: this.y, width: this.width, height: this.height,
                    top: this.top, right: this.right, bottom: this.bottom, left: this.left };
            };
            DOMRectReadOnly.fromRect = function (other) {
                var read = other || { };
                return new DOMRectReadOnly(read.x, read.y, read.width, read.height);
            };
            DOMRectReadOnly.fromPoint = function (other) {
                var read = other || { };
                return new DOMRectReadOnly(read.x, read.y, 0, 0);
            };
            function DOMRect(x, y, w, h) { DOMRectReadOnly.call(this, x, y, w, h); }
            DOMRect.prototype = Object.create(DOMRectReadOnly.prototype);
            DOMRect.prototype.constructor = DOMRect;
            DOMRect.fromRect = DOMRectReadOnly.fromRect;
            Object.defineProperty(DOMRectReadOnly.prototype, Symbol.toStringTag,
                { value: 'DOMRectReadOnly', configurable: true });
            Object.defineProperty(DOMRect.prototype, Symbol.toStringTag,
                { value: 'DOMRect', configurable: true });

            // ── Headers / FormData (Fetch §4, HTML §4.10.22.5) ─────────────────
            // A case-insensitive multimap of the HTTP kinds: a name is stored once, lower-cased, and
            // read back under any casing; combining a repeated name is a comma-space join while the
            // get-all view keeps the values apart. 'forEach' hands a Headers (and FormData) its own
            // collection as the third argument, as the interface defines.
            function makeNameMap(init) {
                var map = Object.create(null);
                function push(k, v) {
                    k = String(k).toLowerCase();
                    if (map[k] === undefined) map[k] = [String(v)]; else map[k].push(String(v));
                }
                if (init) {
                    if (Array.isArray(init)) init.forEach(function (pair) { push(pair[0], pair[1]); });
                    else if (typeof init.forEach === 'function') init.forEach(function (v, k) { push(k, v); });
                    else Object.keys(init).forEach(function (k) { push(k, init[k]); });
                }
                return map;
            }
            function Headers(init) {
                var self = this;
                var map = makeNameMap(
                    (init && typeof init.headers === 'object' && typeof init.headers.forEach === 'function')
                        ? init.headers : init);
                function key(k) { return String(k).toLowerCase(); }
                // A name written twice reads back as the pair the fetch spec joins — the same value
                // 'forEach' and 'values' hand out, and 'getAll' is the view that keeps them apart.
                this.get = function (k) { var a = map[key(k)]; return a ? a.join(', ') : null; };
                this.getAll = function (k) { var a = map[key(k)]; return a ? a.slice() : []; };
                // 'set-cookie' is the one name a response keeps its repetitions apart for — a
                // comma-joined read would fold the attributes of each cookie together — so the
                // interface adds a getter that hands the stored list back (Fetch §5.4).
                this.getSetCookie = function () {
                    var a = map['set-cookie'];
                    return a ? a.slice() : [];
                };
                this.has = function (k) { return map[key(k)] !== undefined; };
                this.set = function (k, v) { map[key(k)] = [String(v)]; };
                this.append = function (k, v) {
                    k = key(k); if (map[k] === undefined) map[k] = []; map[k].push(String(v));
                };
                this.delete = function (k) { delete map[key(k)]; };
                this.forEach = function (cb, ts) {
                    Object.keys(map).sort().forEach(function (k) { cb.call(ts || null, map[k].join(', '), k, self); });
                };
                this.keys = function () { var o = []; Object.keys(map).sort().forEach(function (k) { o.push(k); }); return o; };
                this.values = function () { var o = []; Object.keys(map).sort().forEach(function (k) { o.push(map[k].join(', ')); }); return o; };
                this.entries = function () { var o = []; Object.keys(map).sort().forEach(function (k) { o.push([k, map[k].join(', ')]); }); return o; };
                this[Symbol.iterator] = function () { return self.entries()[Symbol.iterator](); };
                // The guard the interface runs on a name (an illegal character refuses rather than
                // storing): every byte in the HTTP token set, no separators, no space, no control.
                this._check = function (name) {
                    return /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/.test(String(name));
                };
            }

            // ── FormData (XHR §4, HTML §17.10.4) ────────────────────────────────
            // A list of entries. Built from a form it starts with the entries that form would submit:
            // a control that is disabled or carries no name gives nothing, an unchecked checkbox or
            // radio is left out, a checked checkbox with no value answers 'on', a select answers the
            // option or options it shows as selected, a textarea its value, and a file control the file
            // it holds — or the empty file named 'blob' when nothing was picked (measured: a form with
            // 'a=1' and a checked box with no value gives 'a=1&b=on').
            function formDataQuote(value) { return String.fromCharCode(39) + String(value) + String.fromCharCode(39); }
            function formAttributeValue(el, name) {
                var value = el.getAttribute ? el.getAttribute(name) : null;
                return value === null || value === undefined ? null : String(value);
            }
            function collectFormEntries(form, entries) {
                var controls = form.elements;
                if (!controls) return;
                var count = typeof controls.length === 'number' ? controls.length : 0;
                for (var i = 0; i < count; i++) {
                    var el = controls.item ? controls.item(i) : controls[i];
                    if (!el) continue;
                    if (el.disabled === true) continue;
                    var name = formAttributeValue(el, 'name');
                    if (name === null || name === '') continue;
                    var tag = String(el.tagName || '').toUpperCase();
                    var type = (String(el.type || 'text') || 'text').toLowerCase();
                    if (tag === 'INPUT') {
                        if (type === 'submit' || type === 'reset' || type === 'button' || type === 'image')
                            continue;
                        if (type === 'checkbox' || type === 'radio') {
                            if (el.checked !== true) continue;
                            entries.push([name, formAttributeValue(el, 'value') || 'on']);
                            continue;
                        }
                        if (type === 'file') {
                            var held = el.files && el.files.length ? el.files.item(0) : null;
                            // The file an empty control contributes has no name and no bytes; its type
                            // is the default the platform gives a file rather than a named blob (§17.10.4).
                            entries.push([name, held || new File([], '', { type: 'application/octet-stream' })]);
                            continue;
                        }
                        entries.push([name, String(el.value === null || el.value === undefined ? '' : el.value)]);
                        continue;
                    }
                    if (tag === 'TEXTAREA') {
                        entries.push([name, String(el.value === null || el.value === undefined ? '' : el.value)]);
                        continue;
                    }
                    if (tag === 'SELECT') {
                        var options = el.options || [];
                        var picked = [];
                        for (var j = 0; j < options.length; j++) {
                            if (options[j] && options[j].selected === true)
                                picked.push(String(options[j].value));
                        }
                        if (!picked.length && el.multiple !== true && options.length)
                            picked.push(String(options[0] && options[0].value || ''));
                        for (var k = 0; k < picked.length; k++) entries.push([name, picked[k]]);
                        continue;
                    }
                    // Output, progress and meter carry no entry of their own; a button never does.
                }
            }
            function FormData(form) {
                if (!(this instanceof FormData)) {
                    throw new TypeError('Failed to construct ' + formDataQuote('FormData') + ': Please use '
                        + 'the ' + formDataQuote('new') + ' operator, this object constructor cannot be '
                        + 'called as a function.');
                }
                this.__entries = [];
                if (form === undefined || form === null) return;
                var isForm = !!form && String(form.tagName || '').toUpperCase() === 'FORM';
                if (!isForm) {
                    throw new TypeError('Failed to construct ' + formDataQuote('FormData') + ': parameter 1 '
                        + 'is not of type ' + formDataQuote('HTMLFormElement') + '.');
                }
                collectFormEntries(form, this.__entries);
            }
            function formDataEntryValue(value) {
                // A blob that is not a file is given a file name, which is what the entry the receiver
                // reads back needs (X §4.2).
                if (value && typeof Blob !== 'undefined' && value instanceof Blob
                    && !(typeof File === 'function' && value instanceof File)) {
                    return new File([value], 'blob');
                }
                return value;
            }
            FormData.prototype.append = function (name, value, filename) {
                if (arguments.length < 2) {
                    throw new TypeError('Failed to execute ' + formDataQuote('append') + ' on '
                        + formDataQuote('FormData') + ': 2 arguments required, but only '
                        + arguments.length + ' present.');
                }
                var entry = formDataEntryValue(value);
                this.__entries.push([String(name), filename !== undefined
                    ? new File([entry], String(filename)) : entry]);
            };
            FormData.prototype.set = function (name, value, filename) {
                if (arguments.length < 2) {
                    throw new TypeError('Failed to execute ' + formDataQuote('set') + ' on '
                        + formDataQuote('FormData') + ': 2 arguments required, but only '
                        + arguments.length + ' present.');
                }
                var key = String(name);
                var entry = filename !== undefined ? new File([formDataEntryValue(value)], String(filename))
                    : formDataEntryValue(value);
                var found = -1;
                for (var i = 0; i < this.__entries.length; i++) {
                    if (this.__entries[i][0] !== key) continue;
                    if (found < 0) { found = i; this.__entries[i][1] = entry; }
                    else { this.__entries.splice(i, 1); i--; }
                }
                if (found < 0) this.__entries.push([key, entry]);
            };
            FormData.prototype.get = function (name) {
                var key = String(name);
                for (var i = 0; i < this.__entries.length; i++)
                    if (this.__entries[i][0] === key) return this.__entries[i][1];
                return null;
            };
            FormData.prototype.getAll = function (name) {
                var key = String(name), out = [];
                for (var i = 0; i < this.__entries.length; i++)
                    if (this.__entries[i][0] === key) out.push(this.__entries[i][1]);
                return out;
            };
            FormData.prototype.has = function (name) {
                var key = String(name);
                for (var i = 0; i < this.__entries.length; i++)
                    if (this.__entries[i][0] === key) return true;
                return false;
            };
            FormData.prototype.delete = function (name) {
                var key = String(name);
                for (var i = this.__entries.length - 1; i >= 0; i--)
                    if (this.__entries[i][0] === key) this.__entries.splice(i, 1);
            };
            FormData.prototype.forEach = function (callback, thisArg) {
                var copy = this.__entries.slice();
                for (var i = 0; i < copy.length; i++) callback.call(thisArg, copy[i][1], copy[i][0], this);
            };
            function formDataPairs(self) {
                var out = [];
                for (var i = 0; i < self.__entries.length; i++)
                    out.push([self.__entries[i][0], self.__entries[i][1]]);
                return out;
            }
            FormData.prototype.entries = function () {
                var made = formDataPairs(this), at = 0;
                return { next: function () {
                    return at < made.length
                        ? { value: made[at++], done: false } : { value: undefined, done: true };
                }, [Symbol.iterator]: function () { return this; } };
            };
            FormData.prototype.keys = function () {
                var made = formDataPairs(this).map(function (p) { return p[0]; }), at = 0;
                return { next: function () {
                    return at < made.length
                        ? { value: made[at++], done: false } : { value: undefined, done: true };
                }, [Symbol.iterator]: function () { return this; } };
            };
            FormData.prototype.values = function () {
                var made = formDataPairs(this).map(function (p) { return p[1]; }), at = 0;
                return { next: function () {
                    return at < made.length
                        ? { value: made[at++], done: false } : { value: undefined, done: true };
                }, [Symbol.iterator]: function () { return this; } };
            };
            FormData.prototype[Symbol.iterator] = FormData.prototype.entries;
            // The historical read the engine already carried: the number of entries.
            Object.defineProperty(FormData.prototype, 'length', {
                get: function () { return this.__entries.length; },
                enumerable: false, configurable: true
            });
            Object.defineProperty(FormData.prototype, Symbol.toStringTag,
                { value: 'FormData', configurable: true });

            // ── AbortController / AbortSignal (DOM §4.6) ────────────────────────
            // The signal is an EventTarget once the controller aborts it — the engine's event bridge
            // runs the listeners, so the abort reaches every registered handler exactly once.
            // The signal is not a page-side constructor (§4.6): only a controller, a timeout, an
            // abort or a combination makes one, so the internal factory carries the sentinel the
            // constructor insists on.
            var __signalCraft = 0;
            function AbortSignal() {
                if (!__signalCraft) {
                    throw new TypeError('Failed to construct \'AbortSignal\': Illegal constructor');
                }
                var self = this;
                this._aborted = false;
                this._reason = undefined;
                this._listeners = [];
                Object.defineProperty(this, 'aborted', { get: function () { return self._aborted; } });
                Object.defineProperty(this, 'reason', { get: function () { return self._reason; } });
                this.onabort = null;
            }
            AbortSignal.prototype.addEventListener = function (t, l) {
                if (t === 'abort' && l && this._listeners.indexOf(l) < 0) this._listeners.push(l);
            };
            AbortSignal.prototype.removeEventListener = function (t, l) {
                if (t !== 'abort') return;
                var i = this._listeners.indexOf(l);
                if (i >= 0) this._listeners.splice(i, 1);
            };
            AbortSignal.prototype.throwIfAborted = function () {
                if (!this.aborted) return;
                throw abortReason(this);
            };
            AbortSignal.prototype._fire = function () {
                var self = this;
                var ev = new Event('abort');
                try { ev.target = self; } catch (e) { }
                var list = self._listeners.slice();
                for (var i = 0; i < list.length; i++) {
                    var l = list[i];
                    try { (typeof l === 'function' ? l : l.handleEvent).call(self, ev); } catch (e) { }
                }
                if (typeof self.onabort === 'function') { try { self.onabort(ev); } catch (e) { } }
            };
            AbortSignal._make = function () {
                __signalCraft++;
                try { return new AbortSignal(); } finally { __signalCraft--; }
            };
            // The one way a signal turns aborted: the reason it carries, then the listeners it has.
            AbortSignal._abort = function (signal, reason) {
                if (signal._aborted) return signal;
                signal._aborted = true;
                signal._reason = (reason === undefined)
                    ? new DOMException('The operation was aborted.', 'AbortError') : reason;
                signal._fire();
                return signal;
            };
            AbortSignal.abort = function (reason) {
                var s = AbortSignal._make();
                s._aborted = true;
                s._reason = (reason === undefined) ? new DOMException('The operation was aborted.', 'AbortError') : reason;
                return s;
            };
            // A timeout signal aborts on its own, with the TimeoutError the spec names, and the
            // timer is the engine's own so it runs at the same clock setTimeout does.
            AbortSignal.timeout = function (ms) {
                var s = AbortSignal._make();
                setTimeout(function () {
                    if (s._aborted) return;
                    s._aborted = true;
                    s._reason = new DOMException('Timing provided for the signal elapsed', 'TimeoutError');
                    s._fire();
                }, ms || 0);
                return s;
            };

            function AbortController() {
                var self = this;
                this.signal = AbortSignal._make();
                this.abort = function (reason) { AbortSignal._abort(self.signal, reason); };
            }

            // The Option and Image constructors the HTML defines as convenience wrappers over a
            // created element (HTMLOptionElement / HTMLImageElement).
            function Option(text, value, defaultSelected, selected) {
                var o = document.createElement('option');
                o.text = text === undefined ? '' : String(text);
                if (value !== undefined) o.value = String(value);
                if (defaultSelected) o.setAttribute('selected', '');
                if (selected) o.selected = true;
                return o;
            }

            // The interface names the platform exposes but that a page never constructs directly.
            // They exist as globals (typeof answers 'function'); constructing one refuses, exactly as
            // the 'Illegal constructor' the reference engines give (DOM 4 §3/§4.9).
            // TreeWalker and NodeIterator are declared properly further down, where their prototype
            // carries the accessors the host cannot answer.
            function DOMImplementation() { throw new TypeError('Illegal constructor'); }

            // ── Encoding / Crypto / DOM parsing / Blob ─────────────────────────
            // TextEncoder/TextDecoder carry the UTF-8 transformation the Web platform uses to move
            // text across the byte-oriented APIs; surrogate pairs are combined before encoding so an
            // astral character becomes its four bytes, exactly as the standard's algorithm does.
            function TextEncoder() { this.encoding = 'utf-8'; }
            TextEncoder.prototype.encode = function (input) {
                var str = (input === undefined || input === null) ? '' : String(input);
                var out = [];
                for (var i = 0; i < str.length; i++) {
                    var c = str.charCodeAt(i);
                    if (c >= 0xD800 && c <= 0xDBFF && i + 1 < str.length) {
                        var low = str.charCodeAt(i + 1);
                        if (low >= 0xDC00 && low <= 0xDFFF) { c = 0x10000 + ((c - 0xD800) << 10) + (low - 0xDC00); i++; }
                    }
                    if (c < 0x80) out.push(c);
                    else if (c < 0x800) out.push(0xC0 | (c >> 6), 0x80 | (c & 0x3F));
                    else if (c < 0x10000) out.push(0xE0 | (c >> 12), 0x80 | ((c >> 6) & 0x3F), 0x80 | (c & 0x3F));
                    else out.push(0xF0 | (c >> 18), 0x80 | ((c >> 12) & 0x3F), 0x80 | ((c >> 6) & 0x3F), 0x80 | (c & 0x3F));
                }
                return new Uint8Array(out);
            };
            TextEncoder.prototype.encodeInto = function (str, buf) {
                var b = this.encode(str);
                for (var i = 0; i < b.length && i < buf.length; i++) buf[i] = b[i];
                return { read: b.length, written: Math.min(b.length, buf.length) };
            };

            // Encoding Standard §3: the label is text, and a label the specification does not give a
            // decoder for is refused when the decoder is built — not quietly treated as UTF-8
            // (measured: 'new TextDecoder' of an object refuses with a RangeError quoting the label as
            // the conversion left it, '[object Object]'). The second argument is the options
            // dictionary, and the encoding it names is 'utf-8'.
            function TextDecoder(label, options) {
                var given = label === undefined || label === null ? '' : String(label);
                var name = given.toLowerCase().replace(/[^0-9a-z]/g, '');
                if (name === '' || name === 'utf8') name = 'utf-8';
                // Encoding Standard §2.4: the two endian forms are their own decoders, and 'utf-16'
                // takes the little-endian name of them. They are not refused, so a page that builds
                // one reads the bytes back in the order the standard says (measured: a decoder of
                // 'utf-16le' hands over the text without a refusal).
                if (name === 'utf16' || name === 'utf16le') name = 'utf-16le';
                else if (name === 'utf16be') name = 'utf-16be';
                if (name !== 'utf-8' && name !== 'utf-16le' && name !== 'utf-16be') {
                    // Encoding Standard §3: an unsupported label is refused when the decoder is built,
                    // with the text the conversion was given quoted back (measured).
                    var Q = String.fromCharCode(39);
                    throw new RangeError('Failed to construct ' + Q + 'TextDecoder' + Q
                        + ': The encoding label provided (' + Q + given + Q + ') is invalid.');
                }
                var init = options && typeof options === 'object' ? options : {};
                this.encoding = name;
                this.fatal = !!init.fatal;
                this.ignoreBOM = !!init.ignoreBOM;
            }
            function utf16Run(bytes, bigEndian, fatal, skipBom) {
                // Encoding Standard §2.4 UTF-16 decoder: two bytes at a time, a surrogate pair joined,
                // an unpaired surrogate or a leftover byte an error.
                var out = '', i = bigEndian ? 2 : 0, at = 0;
                if (bytes.length >= 2 && ((bigEndian && bytes[0] === 0xFE && bytes[1] === 0xFF)
                    || (!bigEndian && bytes[0] === 0xFF && bytes[1] === 0xFE)) && !skipBom) at = 2;
                for (; at + 1 < bytes.length; at += 2) {
                    var code = bigEndian ? (bytes[at] << 8) | bytes[at + 1] : (bytes[at + 1] << 8) | bytes[at];
                    if (code >= 0xD800 && code <= 0xDBFF && at + 3 < bytes.length) {
                        var next = bigEndian
                            ? (bytes[at + 2] << 8) | bytes[at + 3] : (bytes[at + 3] << 8) | bytes[at + 2];
                        if (next >= 0xDC00 && next <= 0xDFFF) {
                            out += String.fromCharCode(code, next);
                            at += 2;
                            continue;
                        }
                        if (fatal) {
                            throw new TypeError('Failed to execute ' + String.fromCharCode(39) + 'decode'
                                + String.fromCharCode(39) + ' on ' + String.fromCharCode(39) + 'TextDecoder'
                                + String.fromCharCode(39) + ': The encoded data was not valid.');
                        }
                        out += String.fromCharCode(0xFFFD);
                        continue;
                    }
                    if (code >= 0xD800 && code <= 0xDFFF) {
                        if (fatal) {
                            throw new TypeError('Failed to execute ' + String.fromCharCode(39) + 'decode'
                                + String.fromCharCode(39) + ' on ' + String.fromCharCode(39) + 'TextDecoder'
                                + String.fromCharCode(39) + ': The encoded data was not valid.');
                        }
                        out += String.fromCharCode(0xFFFD);
                        continue;
                    }
                    out += String.fromCharCode(code);
                }
                if (at < bytes.length) {
                    if (fatal) {
                        throw new TypeError('Failed to execute ' + String.fromCharCode(39) + 'decode'
                            + String.fromCharCode(39) + ' on ' + String.fromCharCode(39) + 'TextDecoder'
                            + String.fromCharCode(39) + ': The encoded data was not valid.');
                    }
                    out += String.fromCharCode(0xFFFD);
                }
                return out;
            }
            TextDecoder.prototype.decode = function (input) {
                if (input === undefined || input === null) return '';
                var bytes = (input instanceof Uint8Array) ? input
                    : (input.buffer ? new Uint8Array(input.buffer, input.byteOffset || 0, input.byteLength) : input);
                if (this.encoding === 'utf-16le' || this.encoding === 'utf-16be') {
                    return utf16Run(bytes, this.encoding === 'utf-16be', !!this.fatal, !!this.ignoreBOM);
                }
                // Encoding Standard §6.2 UTF-8 decoder: a byte sequence that is overlong, that runs
                // past its own length, that carries a continuation byte where none belongs, that
                // names a surrogate or a code point beyond U+10FFFF is an error — the fatal decoder
                // refuses the whole run (measured: a TypeError whose message is the whole sentence),
                // the non-fatal one answers one replacement character for each error run.
                var len = bytes.length, i = 0, res = '', seen = 0;
                if (len >= 3 && bytes[0] === 0xEF && bytes[1] === 0xBB && bytes[2] === 0xBF
                    && !this.ignoreBOM) { i = 3; }
                function bad() {
                    if (this.fatal) {
                        throw new TypeError('Failed to execute ' + String.fromCharCode(39) + 'decode'
                            + String.fromCharCode(39) + ' on ' + String.fromCharCode(39) + 'TextDecoder'
                            + String.fromCharCode(39) + ': The encoded data was not valid.');
                    }
                    if (!seen) res += String.fromCharCode(0xFFFD);
                    seen++;
                }
                while (i < len) {
                    var b0 = bytes[i], need = 0, cp = 0, low = 0;
                    if (b0 < 0x80) { cp = b0; need = 0; low = 0x00; }
                    else if ((b0 & 0xE0) === 0xC0) { cp = b0 & 0x1F; need = 1; low = 0x80; }
                    else if ((b0 & 0xF0) === 0xE0) { cp = b0 & 0x0F; need = 2; low = 0x800; }
                    else if ((b0 & 0xF8) === 0xF0) { cp = b0 & 0x07; need = 3; low = 0x10000; }
                    else { bad.call(this); i += 1; seen = 0; continue; }
                    var at = 0, ok = true;
                    for (at = 1; at <= need; at++) {
                        var next = i + at;
                        if (next >= len || (bytes[next] & 0xC0) !== 0x80) { ok = false; break; }
                        cp = (cp << 6) | (bytes[next] & 0x3F);
                    }
                    if (!ok) { i += 1; while (i < len && (bytes[i] & 0xC0) === 0x80) i++; bad.call(this); seen = 0; continue; }
                    // The value has to be worth the bytes it was written with, and it has to name a
                    // character at all: an overlong form, a surrogate and anything past the last
                    // code point are all errors the same way.
                    if (cp < low || (cp >= 0xD800 && cp <= 0xDFFF) || cp > 0x10FFFF) {
                        i += need + 1; bad.call(this); seen = 0; continue;
                    }
                    i += need + 1; seen = 0;
                    if (cp <= 0xFFFF) res += String.fromCharCode(cp);
                    else { cp -= 0x10000; res += String.fromCharCode(0xD800 + (cp >> 10), 0xDC00 + (cp & 0x3FF)); }
                }
                return res;
            };

            // Web Crypto — the parts that need no secure transport surface. randomUUID is minted by
            // the host (the only real entropy), getRandomValues fills a typed array in place and
            // returns the same array the page handed in, and 'subtle' holds the four digests §15.14
            // names. The bytes cross to the engine as hex and the digest comes back the same way, so
            // the platform's own crypto does the work while the promise, the conversions and the
            // refusals stay here — and both refusals are rejections rather than throws (measured: an
            // unknown algorithm name rejects NotSupportedError saying 'Algorithm: Unrecognized name',
            // a body that is not a buffer rejects TypeError naming '(ArrayBuffer or ArrayBufferView)').
            function bufferBytes(value) {
                if (value instanceof ArrayBuffer) return new Uint8Array(value);
                if (value && typeof value.byteLength === 'number'
                    && value.buffer instanceof ArrayBuffer)
                    return new Uint8Array(value.buffer, value.byteOffset || 0, value.byteLength);
                return null;
            }
            var HEX_DIGITS = '0123456789abcdef';
            function toHex(bytes) {
                var out = '';
                for (var i = 0; i < bytes.length; i++)
                    out += HEX_DIGITS.charAt((bytes[i] >> 4) & 15) + HEX_DIGITS.charAt(bytes[i] & 15);
                return out;
            }
            function fromHex(hex) {
                var made = new Uint8Array(hex.length >> 1);
                for (var j = 0; j < made.length; j++) made[j] = parseInt(hex.substr(j * 2, 2), 16);
                return made;
            }
            function SubtleCrypto() { }
            var SUBTLE_DIGESTS = { 'SHA-1': true, 'SHA-256': true, 'SHA-384': true, 'SHA-512': true };
            SubtleCrypto.prototype.digest = function (algorithm, data) {
                var name = algorithm && algorithm.name !== undefined
                    ? String(algorithm.name) : String(algorithm === undefined ? '' : algorithm);
                var bytes = bufferBytes(data);
                return new Promise(function (resolve, reject) {
                    if (bytes === null) {
                        reject(new TypeError('Failed to execute \'digest\' on \'SubtleCrypto\': The '
                            + 'provided value is not of type \'(ArrayBuffer or ArrayBufferView)\'.'));
                        return;
                    }
                    if (!SUBTLE_DIGESTS[name]) {
                        reject(new DOMException('Failed to execute \'digest\' on \'SubtleCrypto\': '
                            + 'Algorithm: Unrecognized name', 'NotSupportedError'));
                        return;
                    }
                    var hex = __acrux.digest(name, toHex(bytes));
                    if (typeof hex !== 'string') {
                        reject(new DOMException('The operation failed for an operation-specific '
                            + 'reason', 'OperationError'));
                        return;
                    }
                    resolve(fromHex(hex).buffer);
                });
            };
            // The other eleven members are the surface the page tests for with 'typeof'; this engine
            // holds no key material and runs no cipher, so each one rejects the reason it cannot.
            var SUBTLE_UNIMPLEMENTED = ['encrypt', 'decrypt', 'sign', 'verify', 'generateKey',
                'deriveKey', 'deriveBits', 'importKey', 'exportKey', 'wrapKey', 'unwrapKey'];
            for (var si = 0; si < SUBTLE_UNIMPLEMENTED.length; si++) {
                (function (member) {
                    SubtleCrypto.prototype[member] = function () {
                        return Promise.reject(new DOMException('Failed to execute \'' + member
                            + '\' on \'SubtleCrypto\': The operation is not supported.',
                            'NotSupportedError'));
                    };
                })(SUBTLE_UNIMPLEMENTED[si]);
            }
            Object.defineProperty(SubtleCrypto.prototype, Symbol.toStringTag,
                { value: 'SubtleCrypto', configurable: true });
            window.SubtleCrypto = SubtleCrypto;
            var crypto = {
                randomUUID: function () { return __acrux.randomUUID(); },
                getRandomValues: function (array) {
                    if (!array) return array;
                    for (var i = 0; i < array.length; i++)
                        array[i] = Math.floor(Math.random() * 256);
                    return array;
                },
                subtle: new SubtleCrypto()
            };

            // DOMParser builds a fresh Document out of markup (the host parses it through the same
            // HTML pipeline the live page went through), so the result answers getElementById and
            // querySelector just like 'document'. DOM Parsing §3 holds the type to a fixed list: the
            // HTML type builds the html/head/body skeleton, the XML ones build the tree the markup
            // says, and anything else is refused with the sentence the reference writes (measured).
            function DOMParser() {}
            var SUPPORTED_TYPES = {
                'text/html': 'html',
                'text/xml': 'xml',
                'application/xml': 'xml',
                'application/xhtml+xml': 'xml',
                'image/svg+xml': 'xml'
            };
            DOMParser.prototype.parseFromString = function (source, contentType) {
                var type = String(contentType === undefined ? '' : contentType);
                var kind = SUPPORTED_TYPES[type];
                if (!kind) {
                    var quote = String.fromCharCode(39);
                    throw new TypeError('Failed to execute \'parseFromString\' on \'DOMParser\': The '
                        + 'provided value ' + quote + type + quote
                        + ' is not a valid enum value of type SupportedType.');
                }
                var text = String(source === undefined ? '' : source);
                return kind === 'html' ? __acrux.parseHTML(text) : __acrux.parseXML(text, type);
            };

            // XMLSerializer re-serialises a node; the engine already owns the HTML serialisation that
            // outerHTML answers with, so that is what 'serializeToString' returns for an element, and
            // the document element for a whole document.
            function XMLSerializer() {}
            XMLSerializer.prototype.serializeToString = function (node) {
                if (node === null || node === undefined) return '';
                if (typeof node.outerHTML === 'string') return node.outerHTML;
                if (node.documentElement && typeof node.documentElement.outerHTML === 'string')
                    return node.documentElement.outerHTML;
                if (typeof node.textContent === 'string') return node.textContent;
                return String(node);
            };

            // ── Streams (Streams Spec §6.2-§6.8, §7, §9) ──────────────────────────
            // A readable stream is a queue with a controller that fills it, one reader at a time, and
            // the promises a page awaits; a writable one is the mirror of that. A read that finds the
            // queue empty parks until the controller enqueues, so a pull that has not run yet still
            // hands the chunk to the reader that asked first. Nothing in the engine chunks a network
            // body yet, so a body that answers a stream hands over its bytes as the one and only
            // chunk — which is what the reference gives for a small body too (measured: a four-byte
            // blob reads back a single Uint8Array of byteLength 4).
            var STREAM_STATE = new WeakMap();
            var STREAM_CONTROLLER = new WeakMap();
            var STREAM_READER = new WeakMap();
            var WRITABLE_STATE = new WeakMap();
            var WRITABLE_CONTROLLER = new WeakMap();
            var WRITABLE_WRITER = new WeakMap();

            function deferred() {
                var box = { promise: null, resolve: null, reject: null };
                box.promise = new Promise(function (res, rej) { box.resolve = res; box.reject = rej; });
                return box;
            }

            function newStreamQueue(highWaterMark) {
                var closeBox = deferred();
                closeBox.promise.catch(function () { });
                return { chunks: [], waiting: [], closed: false, finished: false, errored: undefined,
                    highWaterMark: highWaterMark === undefined ? 1 : Number(highWaterMark) || 0,
                    pull: null, cancel: null, controller: null, reader: null, locked: false,
                    closedPromise: closeBox.promise, closedBox: closeBox };
            }

            function streamQueueOf(stream) {
                if (!stream || typeof stream !== 'object') return null;
                var state = STREAM_STATE.get(stream);
                return state && state.chunks !== undefined ? state : null;
            }

            function queueFinished(state) {
                if (state.finished) return;
                if (state.errored !== undefined) {
                    state.finished = true;
                    state.closedBox.reject(state.errored);
                    return;
                }
                if (state.closed && !state.chunks.length) {
                    state.finished = true;
                    state.closedBox.resolve(undefined);
                }
            }

            function queueSettle(state) {
                while (state.waiting.length) {
                    var waiter = state.waiting[0];
                    if (state.errored !== undefined) {
                        state.waiting.shift(); waiter.reject(state.errored); continue;
                    }
                    if (state.chunks.length) {
                        state.waiting.shift(); waiter.resolve({ value: state.chunks.shift(), done: false });
                        continue;
                    }
                    if (state.closed) {
                        state.waiting.shift(); waiter.resolve({ value: undefined, done: true }); continue;
                    }
                    break;
                }
                queueFinished(state);
                queuePull(state);
            }

            function queuePull(state) {
                if (!state.pull || state.closed || state.errored !== undefined || state.pullRunning) return;
                if (state.chunks.length > state.highWaterMark) return;
                state.pullRunning = true;
                var made;
                try { made = state.pull(state.controller); }
                catch (e) { state.errored = e; queueFinished(state); return; }
                Promise.resolve(made).then(function () { state.pullRunning = false; queueSettle(state); },
                    function (err) { state.pullRunning = false; streamErrorController(state, err); });
            }

            function streamErrorController(state, error) {
                if (state.errored !== undefined) return;
                state.errored = error === undefined ? new Error('Uncaught (in promise) TypeError: Invalid reason') : error;
                var waits = state.waiting.splice(0);
                for (var i = 0; i < waits.length; i++) waits[i].reject(state.errored);
                queueFinished(state);
            }

            function ReadableStream(underlyingSource, queuingStrategy) {
                var source = underlyingSource || { };
                var mark = queuingStrategy && queuingStrategy.highWaterMark !== undefined
                    ? Number(queuingStrategy.highWaterMark) : 1;
                var state = newStreamQueue(mark);
                STREAM_STATE.set(this, state);
                var controller = Object.create(ReadableStreamDefaultController.prototype);
                STREAM_CONTROLLER.set(controller, state);
                state.controller = controller;
                if (typeof source.pull === 'function') state.pull = function (c) { return source.pull(c); };
                if (typeof source.cancel === 'function') state.cancel = function (r) { return source.cancel(r); };
                if (typeof source.start === 'function') {
                    try { var started = source.start(controller); Promise.resolve(started).catch(function (e) { streamErrorController(state, e); }); }
                    catch (e) { streamErrorController(state, e); }
                }
                queueSettle(state);
            }

            function readableState(stream, name) {
                var state = streamQueueOf(stream);
                if (!state) {
                    throw new TypeError('Failed to execute ' + formDataQuote(name) + ' on '
                        + formDataQuote('ReadableStream') + ': Illegal invocation');
                }
                return state;
            }

            ReadableStream.prototype.getReader = function (options) {
                var state = readableState(this, 'getReader');
                if (options && options.mode !== undefined && options.mode !== null && options.mode !== 'byob') {
                    throw new TypeError('Failed to execute ' + formDataQuote('getReader') + ' on '
                        + formDataQuote('ReadableStream') + ': The mode provided is not supported.');
                }
                if (state.locked) {
                    throw new TypeError('Failed to execute ' + formDataQuote('getReader') + ' on '
                        + formDataQuote('ReadableStream') + ': This stream is already locked.');
                }
                return new ReadableStreamDefaultReader(this);
            };

            function streamRead(state) {
                if (state.errored !== undefined) return Promise.reject(state.errored);
                if (state.chunks.length) {
                    var made = { value: state.chunks.shift(), done: false };
                    queueSettle(state);
                    return Promise.resolve(made);
                }
                if (state.closed) return Promise.resolve({ value: undefined, done: true });
                return new Promise(function (resolve, reject) {
                    state.waiting.push({ resolve: resolve, reject: reject });
                    queuePull(state);
                });
            }

            function streamCancel(state, reason) {
                var made = state.cancel ? Promise.resolve(state.cancel(reason)) : Promise.resolve(undefined);
                return made.then(function () {
                    if (state.chunks.length) state.chunks.length = 0;
                    state.closed = true;
                    queueSettle(state);
                    if (state.reader) releaseReader(state);
                });
            }

            ReadableStream.prototype.cancel = function (reason) {
                var state = readableState(this, 'cancel');
                return streamCancel(state, reason);
            };

            ReadableStream.prototype.pipeTo = function (destination) {
                var state = readableState(this, 'pipeTo');
                var writable = destination && WRITABLE_STATE ? WRITABLE_STATE.get(destination) : null;
                if (!writable) {
                    return Promise.reject(new TypeError('Failed to execute ' + formDataQuote('pipeTo')
                        + ' on ' + formDataQuote('ReadableStream') + ': parameter 1 is not of type '
                        + formDataQuote('WritableStream') + '.'));
                }
                if (state.locked) {
                    return Promise.reject(new TypeError('Invalid state: the source stream is already locked.'));
                }
                var reader = new ReadableStreamDefaultReader(this);
                var writer = new WritableStreamDefaultWriter(destination);
                var sourceErrored = false;
                function step() {
                    return reader.read().then(function (result) {
                        if (result.done) return writer.close();
                        return writer.write(result.value).then(step, function (err) {
                            sourceErrored = true;
                            reader.releaseLock();
                            return writer.abort(err);
                        });
                    });
                }
                return step().catch(function (err) {
                    if (!sourceErrored) { try { writer.abort(err); } catch (e) { } }
                    throw err;
                });
            };

            ReadableStream.prototype.pipeThrough = function (transformStream, options) {
                readableState(this, 'pipeThrough');
                var pair = transformStream || { };
                if (!pair.writable || !pair.readable) {
                    throw new TypeError('Failed to execute ' + formDataQuote('pipeThrough')
                        + ' on ' + formDataQuote('ReadableStream') + ': parameter 1 is not of type '
                        + formDataQuote('ReadableStreamAndWritableStream') + '.');
                }
                this.pipeTo(pair.writable);
                return pair.readable;
            };

            ReadableStream.prototype.tee = function () {
                readableState(this, 'tee');
                var left = new ReadableStream(), right = new ReadableStream();
                var leftState = STREAM_STATE.get(left), rightState = STREAM_STATE.get(right);
                var reader = new ReadableStreamDefaultReader(this);
                function step() {
                    return reader.read().then(function (result) {
                        if (result.done) {
                            leftState.controller.close(); rightState.controller.close();
                            return;
                        }
                        leftState.controller.enqueue(result.value);
                        rightState.controller.enqueue(result.value);
                        return step();
                    }, function (err) {
                        streamErrorController(leftState, err); streamErrorController(rightState, err);
                    });
                }
                step();
                return [left, right];
            };

            function streamAsyncIterator(stream) {
                var iterator = {
                    next: function () {
                        return streamRead(STREAM_STATE.get(stream)).then(function (result) {
                            if (result.done) iterator.__done = true;
                            return { value: result.value, done: !!result.done };
                        });
                    },
                    return: function () { return streamCancel(STREAM_STATE.get(stream)).then(function () {
                        return { value: undefined, done: true }; }); }
                };
                iterator[Symbol.asyncIterator] = function () { return iterator; };
                return iterator;
            }

            Object.defineProperty(ReadableStream.prototype, 'locked', {
                configurable: true, enumerable: true,
                get: function () { return readableState(this, 'locked').locked; }
            });
            // The iteration methods are accessors that hand back the function, the way the interface
            // defines them, so 'stream.values' reads as a function and 'stream.values()' as the
            // iterator (measured both).
            Object.defineProperty(ReadableStream.prototype, 'values', {
                configurable: true, enumerable: true,
                get: function () {
                    var self = this;
                    if (!streamQueueOf(self)) {
                        throw new TypeError('Failed to execute ' + formDataQuote('values') + ' on '
                            + formDataQuote('ReadableStream') + ': Illegal invocation');
                    }
                    return function () { return streamAsyncIterator(self); };
                }
            });
            Object.defineProperty(ReadableStream.prototype, Symbol.asyncIterator, {
                configurable: true, enumerable: false,
                get: function () {
                    var self = this;
                    if (!streamQueueOf(self)) {
                        throw new TypeError('Failed to execute ' + formDataQuote('Symbol.asyncIterator')
                            + ' on ' + formDataQuote('ReadableStream') + ': Illegal invocation');
                    }
                    return function () { return streamAsyncIterator(self); };
                }
            });

            function ReadableStreamDefaultController() {
                throw new TypeError('Failed to construct ' + formDataQuote('ReadableStreamDefaultController')
                    + ': Illegal constructor');
            }
            function controllerState(controller) {
                var state = STREAM_CONTROLLER.get(controller);
                if (!state) {
                    throw new TypeError('Illegal invocation');
                }
                return state;
            }
            ReadableStreamDefaultController.prototype.enqueue = function (chunk) {
                var state = controllerState(this);
                if (state.closed || state.errored !== undefined) throw new TypeError('Invalid state');
                state.chunks.push(chunk);
                queueSettle(state);
            };
            ReadableStreamDefaultController.prototype.close = function () {
                var state = controllerState(this);
                if (state.closed) throw new TypeError('Invalid state');
                state.closed = true;
                queueSettle(state);
            };
            ReadableStreamDefaultController.prototype.error = function (reason) {
                var state = controllerState(this);
                if (state.closed || state.errored !== undefined) return;
                streamErrorController(state, reason);
            };
            Object.defineProperty(ReadableStreamDefaultController.prototype, 'desiredSize', {
                configurable: true, enumerable: true,
                get: function () {
                    var state = controllerState(this);
                    if (state.closed || state.errored !== undefined) return null;
                    return state.highWaterMark - state.chunks.length;
                }
            });

            function ReadableStreamDefaultReader(stream) {
                if (arguments.length < 1) {
                    throw new TypeError('Failed to construct ' + formDataQuote('ReadableStreamDefaultReader')
                        + ': 1 argument required, but only ' + arguments.length + ' present.');
                }
                var state = streamQueueOf(stream);
                if (!state) {
                    throw new TypeError('Failed to construct ' + formDataQuote('ReadableStreamDefaultReader')
                        + ': parameter 1 is not of type ' + formDataQuote('ReadableStream') + '.');
                }
                if (state.locked) {
                    throw new TypeError('Failed to construct ' + formDataQuote('ReadableStreamDefaultReader')
                        + ': This stream is already locked.');
                }
                state.locked = true;
                state.reader = this;
                STREAM_READER.set(this, state);
            }
            function readerState(reader) {
                var state = STREAM_READER.get(reader);
                if (!state || state.reader !== reader) throw new TypeError('Reader was released');
                return state;
            }
            function releaseReader(state) {
                var reader = state.reader;
                state.reader = null;
                state.locked = false;
                if (reader) STREAM_READER.delete(reader);
            }
            ReadableStreamDefaultReader.prototype.read = function () {
                var state;
                try { state = readerState(this); }
                catch (e) { return Promise.reject(e); }
                return streamRead(state);
            };
            ReadableStreamDefaultReader.prototype.cancel = function (reason) {
                var state = readerState(this);
                return streamCancel(state, reason);
            };
            ReadableStreamDefaultReader.prototype.releaseLock = function () {
                var state = readerState(this);
                releaseReader(state);
            };
            Object.defineProperty(ReadableStreamDefaultReader.prototype, 'closed', {
                configurable: true, enumerable: true,
                get: function () { return readerState(this).closedPromise; }
            });
            Object.defineProperty(ReadableStreamDefaultReader.prototype, 'readable', {
                configurable: true, enumerable: true,
                get: function () {
                    var state = readerState(this);
                    return state.stream;
                }
            });

            function writableState(stream) {
                var state = stream && WRITABLE_STATE.get(stream);
                if (!state) {
                    throw new TypeError('Failed to execute on ' + formDataQuote('WritableStream')
                        + ': Illegal invocation');
                }
                return state;
            }

            function WritableStream(underlyingSink, queuingStrategy) {
                var sink = underlyingSink || { };
                var mark = queuingStrategy && queuingStrategy.highWaterMark !== undefined
                    ? Number(queuingStrategy.highWaterMark) : 1;
                var closeBox = deferred();
                closeBox.promise.catch(function () { });
                var state = { write: typeof sink.write === 'function' ? sink.write : null,
                    close: typeof sink.close === 'function' ? sink.close : null,
                    abort: typeof sink.abort === 'function' ? sink.abort : null,
                    queued: [], waiting: [], highWaterMark: mark || 0, closed: false,
                    finished: false, error: undefined, locked: false, writer: null,
                    closedPromise: closeBox.promise, closedBox: closeBox, draining: false,
                    signal: AbortSignal._make(), abortCalled: false };
                WRITABLE_STATE.set(this, state);
                var controller = Object.create(WritableStreamDefaultController.prototype);
                WRITABLE_CONTROLLER.set(controller, state);
                state.controller = controller;
                if (typeof sink.start === 'function') {
                    try { Promise.resolve(sink.start(controller)).catch(function (e) { writableAbort(state, e); }); }
                    catch (e) { writableAbort(state, e); }
                }
            }

            function writableFinish(state, error) {
                if (state.finished) return;
                state.finished = true;
                if (error !== undefined) { state.error = error; state.closedBox.reject(error); }
                else state.closedBox.resolve(undefined);
            }

            function writableDrain(state) {
                if (state.draining) return;
                state.draining = true;
                function next() {
                    var waiter = state.queued.shift();
                    if (!waiter) {
                        state.draining = false;
                        if (state.closed) writableFinish(state, state.error);
                        writableSettle(state);
                        return;
                    }
                    if (waiter.kind === 'write' && (state.error !== undefined || state.closed)) {
                        waiter.reject(state.error === undefined
                            ? new TypeError('Invalid state') : state.error);
                        next();
                        return;
                    }
                    if (waiter.kind === 'close') {
                        if (state.closed) { waiter.reject(new TypeError('Invalid state')); next(); return; }
                        var made = state.close ? state.close() : undefined;
                        Promise.resolve(made).then(function () {
                            state.closed = true;
                            waiter.resolve(undefined);
                            next();
                        }, function (e) { waiter.reject(e); writableAbort(state, e); });
                        return;
                    }
                    if (waiter.kind === 'abort') {
                        state.abortCalled = true;
                        var aborted = state.abort ? state.abort(waiter.reason) : undefined;
                        Promise.resolve(aborted).then(function () {
                            waiter.resolve(undefined);
                            writableAbort(state, waiter.reason);
                        }, function (e) { waiter.reject(e); writableAbort(state, e); });
                        return;
                    }
                    var written = state.write ? state.write(waiter.chunk, state.controller) : undefined;
                    Promise.resolve(written).then(function () {
                        waiter.resolve(undefined);
                        writableSettle(state);
                        next();
                    }, function (e) { waiter.reject(e); writableAbort(state, e); });
                }
                next();
            }

            // Aborting is the one path that ends a writable stream without closing it: everything still
            // queued is refused, the sink is told once, and the closed promise is the one that rejects
            // (§6.3.4).
            function writableAbort(state, reason) {
                if (state.error !== undefined || state.finished) return;
                state.error = reason === undefined ? new Error('The stream was aborted.') : reason;
                state.closed = true;
                var waits = state.queued.splice(0);
                for (var i = 0; i < waits.length; i++) {
                    if (waits[i].kind === 'abort') waits[i].resolve(undefined);
                    else waits[i].reject(state.error);
                }
                state.draining = false;
                if (!state.abortCalled && state.abort) {
                    state.abortCalled = true;
                    try { Promise.resolve(state.abort(state.error)).catch(function () { }); }
                    catch (e) { }
                }
                if (state.signal) { try { AbortSignal._abort(state.signal, state.error); } catch (e) { } }
                writableFinish(state, state.error);
                writableSettle(state);
            }

            function writableSettle(state) {
                while (state.waiting.length && state.error === undefined
                    && state.queued.length <= state.highWaterMark && !state.closed) {
                    var waiter = state.waiting.shift();
                    waiter.resolve(state.controller);
                }
                if (state.error !== undefined) {
                    var rejected = state.waiting.splice(0);
                    for (var i = 0; i < rejected.length; i++) rejected[i].reject(state.error);
                }
            }

            function writableQueue(state, kind, chunkOrReason) {
                return new Promise(function (resolve, reject) {
                    var waiter = { kind: kind, resolve: resolve, reject: reject };
                    if (kind === 'write') waiter.chunk = chunkOrReason;
                    else waiter.reason = chunkOrReason;
                    state.queued.push(waiter);
                    writableDrain(state);
                });
            }

            function WritableStreamDefaultWriter(stream) {
                var state = stream && WRITABLE_STATE.get(stream);
                if (!state) {
                    throw new TypeError('Failed to construct ' + formDataQuote('WritableStreamDefaultWriter')
                        + ': parameter 1 is not of type ' + formDataQuote('WritableStream') + '.');
                }
                if (state.locked) {
                    throw new TypeError('Failed to construct ' + formDataQuote('WritableStreamDefaultWriter')
                        + ': This stream is already locked.');
                }
                state.locked = true;
                state.writer = this;
                WRITABLE_WRITER.set(this, state);
            }

            function writerState(writer) {
                var state = WRITABLE_WRITER.get(writer);
                if (!state || state.writer !== writer) throw new TypeError('Writer was released');
                return state;
            }

            WritableStreamDefaultWriter.prototype.write = function (chunk) {
                var state;
                try { state = writerState(this); } catch (e) { return Promise.reject(e); }
                return writableQueue(state, 'write', chunk);
            };
            WritableStreamDefaultWriter.prototype.close = function () {
                return writableQueue(writerState(this), 'close');
            };
            WritableStreamDefaultWriter.prototype.abort = function (reason) {
                var state = writerState(this);
                if (state.error !== undefined || state.finished) return Promise.resolve(undefined);
                return writableQueue(state, 'abort', reason);
            };
            WritableStreamDefaultWriter.prototype.releaseLock = function () {
                var state = writerState(this);
                state.writer = null;
                state.locked = false;
                WRITABLE_WRITER.delete(this);
            };
            ['ready', 'closed', 'desiredSize'].forEach(function (name) {
                Object.defineProperty(WritableStreamDefaultWriter.prototype, name, {
                    configurable: true, enumerable: true,
                    get: function () {
                        var state = writerState(this);
                        if (name === 'closed') return state.closedPromise;
                        if (name === 'desiredSize') {
                            if (state.error !== undefined || state.finished) return null;
                            return state.highWaterMark - state.queued.length;
                        }
                        if (state.error !== undefined) return Promise.reject(state.error);
                        return new Promise(function (resolve, reject) {
                            if (state.closed) { reject(new TypeError('Invalid state')); return; }
                            state.waiting.push({ resolve: resolve, reject: reject });
                            writableSettle(state);
                        });
                    }
                });
            });

            function WritableStreamDefaultController() {
                throw new TypeError('Failed to construct ' + formDataQuote('WritableStreamDefaultController')
                    + ': Illegal constructor');
            }
            ['desiredSize', 'abortReason', 'signal'].forEach(function (name) {
                Object.defineProperty(WritableStreamDefaultController.prototype, name, {
                    configurable: true, enumerable: true,
                    get: function () {
                        var state = WRITABLE_CONTROLLER.get(this);
                        if (!state) throw new TypeError('Illegal invocation');
                        if (name === 'abortReason') return state.error;
                        if (name === 'desiredSize') {
                            if (state.error !== undefined || state.finished) return null;
                            return state.highWaterMark - state.queued.length;
                        }
                        return state.signal;
                    }
                });
            });
            WritableStreamDefaultController.prototype.error = function (reason) {
                var state = WRITABLE_CONTROLLER.get(this);
                if (!state) throw new TypeError('Illegal invocation');
                writableAbort(state, reason);
            };
            WritableStreamDefaultController.prototype.terminate = function () {
                var state = WRITABLE_CONTROLLER.get(this);
                if (!state) throw new TypeError('Illegal invocation');
                if (state.closed) return;
                state.closed = true;
                writableFinish(state);
            };

            Object.defineProperty(WritableStream.prototype, 'locked', {
                configurable: true, enumerable: true,
                get: function () { return writableState(this).locked; }
            });
            WritableStream.prototype.getWriter = function () {
                var state = WRITABLE_STATE.get(this);
                if (!state) {
                    throw new TypeError('Failed to execute ' + formDataQuote('getWriter') + ' on '
                        + formDataQuote('WritableStream') + ': Illegal invocation');
                }
                if (state.locked) {
                    throw new TypeError('Failed to execute ' + formDataQuote('getWriter') + ' on '
                        + formDataQuote('WritableStream') + ': This stream is already locked.');
                }
                return new WritableStreamDefaultWriter(this);
            };
            WritableStream.prototype.close = function () {
                var state = writableState(this);
                if (state.closed) return Promise.reject(new TypeError('Invalid state'));
                return writableQueue(state, 'close');
            };
            WritableStream.prototype.abort = function (reason) {
                var state = writableState(this);
                if (state.error !== undefined || state.finished) return Promise.resolve(undefined);
                return writableQueue(state, 'abort', reason);
            };

            // TransformStream §4.4: the default transform hands every chunk to the readable side
            // unchanged; a transformer may enqueue instead, and when it does the value it returns is
            // dropped so the chunk is not passed twice.
            function TransformStream(transformer, writableStrategy, readableStrategy) {
                var transform = transformer || { };
                var readable = new ReadableStream({ }, readableStrategy);
                var outState = STREAM_STATE.get(readable);
                var writable = new WritableStream({
                    write: function (chunk) {
                        if (typeof transform.transform !== 'function') {
                            outState.controller.enqueue(chunk);
                            return undefined;
                        }
                        var before = outState.chunks.length;
                        var made = transform.transform(chunk, outState.controller);
                        return Promise.resolve(made).then(function (value) {
                            if (outState.chunks.length === before && value !== undefined) {
                                outState.controller.enqueue(value);
                            }
                        });
                    },
                    close: function () {
                        if (typeof transform.flush === 'function') transform.flush(outState.controller);
                        outState.controller.close();
                    },
                    abort: function (reason) { streamErrorController(outState, reason); }
                }, writableStrategy);
                Object.defineProperties(this, {
                    readable: { value: readable, enumerable: true, configurable: true },
                    writable: { value: writable, enumerable: true, configurable: true }
                });
            }

            function singleChunkStream(bytes) {
                var made = new ReadableStream();
                var state = STREAM_STATE.get(made);
                state.controller.enqueue(bytes);
                state.controller.close();
                return made;
            }

            [ReadableStream, ReadableStreamDefaultReader, ReadableStreamDefaultController,
             WritableStream, WritableStreamDefaultWriter, WritableStreamDefaultController,
             TransformStream].forEach(function (ctor) {
                Object.defineProperty(ctor.prototype, Symbol.toStringTag,
                    { value: ctor.name, configurable: true });
            });
            window.ReadableStream = ReadableStream;
            window.ReadableStreamDefaultReader = ReadableStreamDefaultReader;
            window.ReadableStreamDefaultController = ReadableStreamDefaultController;
            window.WritableStream = WritableStream;
            window.WritableStreamDefaultWriter = WritableStreamDefaultWriter;
            window.WritableStreamDefaultController = WritableStreamDefaultController;
            window.TransformStream = TransformStream;

            // ── Blob and File (File API §4.1) ──────────────────────────────────
            // A blob holds bytes, not a string it happens to print: each part is normalised to a byte
            // run — a string through UTF-8, an ArrayBuffer or a view of one as it lies, another blob's
            // own bytes copied — and 'size' is the length of the joined run (measured: a blob built
            // from the two bytes 104,105 sizes 2, and one built from a three-byte buffer sizes 3).
            // 'text' is those bytes decoded, so a blob made from bytes reads back the text they mean.
            function partBytes(part) {
                if (part === null || part === undefined) return new Uint8Array(0);
                if (part instanceof Blob) return blobBytes(part);
                if (typeof part === 'string') return new TextEncoder().encode(part);
                if (typeof ArrayBuffer === 'function' && part instanceof ArrayBuffer) return new Uint8Array(part);
                if (part.buffer instanceof ArrayBuffer) {
                    var at = part.byteOffset || 0;
                    return new Uint8Array(part.buffer.slice(at, at + part.byteLength));
                }
                return new TextEncoder().encode(String(part));
            }
            function blobBytes(blob) {
                if (blob && blob.__bytes !== undefined) return blob.__bytes;
                return new TextEncoder().encode(blob && blob.__text !== undefined ? blob.__text : String(blob));
            }
            function blobToString(blob) { return blob.__text === undefined ? String(blob) : blob.__text; }
            function Blob(blobParts, options) {
                var parts = blobParts || [];
                var joined = [], total = 0, i, p;
                for (i = 0; i < parts.length; i++) {
                    p = partBytes(parts[i]);
                    joined.push(p);
                    total += p.length;
                }
                var bytes = new Uint8Array(total);
                var at = 0;
                for (i = 0; i < joined.length; i++) { bytes.set(joined[i], at); at += joined[i].length; }
                Object.defineProperty(this, '__bytes', { value: bytes, configurable: true });
                Object.defineProperty(this, '__text', { value: new TextDecoder().decode(bytes), configurable: true });
                this.type = (options && options.type) ? String(options.type) : '';
                this.size = bytes.length;
            }
            Blob.prototype.text = function () { return Promise.resolve(blobToString(this)); };
            Blob.prototype.arrayBuffer = function () {
                var copy = blobBytes(this).slice();
                return Promise.resolve(copy.buffer);
            };
            // A blob reads as one chunk: the whole byte range arrives in the first pull, which is what
            // the reference gives for a blob this small (measured: the first chunk of a four-byte blob
            // is a Uint8Array of byteLength 4).
            Blob.prototype.stream = function () { return singleChunkStream(blobBytes(this).slice()); };
            Blob.prototype.bytes = function () { return Promise.resolve(blobBytes(this).slice()); };
            Blob.prototype.slice = function (start, end, contentType) {
                // §4.1.1.8: the range is in bytes, a negative bound counts back from the end, and the
                // two bounds are clamped and swapped before the run is taken.
                var total = this.size;
                var s = start === undefined ? 0 : Number(start) || 0;
                var e = end === undefined ? total : Number(end) || 0;
                if (s < 0) s = Math.max(total + s, 0); else s = Math.min(s, total);
                if (e < 0) e = Math.max(total + e, 0); else e = Math.min(e, total);
                if (e < s) e = s;
                return new Blob([blobBytes(this).slice(s, e)], { type: contentType || '' });
            };
            Object.defineProperty(Blob.prototype, Symbol.toStringTag,
                { value: 'Blob', configurable: true });

            function File(fileBits, fileName, options) {
                if (arguments.length < 2) {
                    throw new TypeError('Failed to construct ' + formDataQuote('File') + ': '
                        + 2 + ' arguments required, but only ' + arguments.length + ' present.');
                }
                Blob.call(this, fileBits, options);
                this.name = String(fileName === undefined ? '' : fileName);
                var path = options && options.webkitPath ? String(options.webkitPath) : '';
                var mark = path.lastIndexOf('/');
                this.webkitPath = mark < 0 ? '' : path.slice(0, mark);
                this.lastModified = (options && options.lastModified !== undefined)
                    ? Number(options.lastModified) : Date.now();
            }
            File.prototype = Object.create(Blob.prototype);
            File.prototype.constructor = File;
            Object.defineProperty(File.prototype, 'webkitRelativePath', {
                get: function () {
                    // A file the script built has no directory beside it; one the user picked through a
                    // file input carries the path the picker knew.
                    return this.__relative === undefined ? '' : this.__relative;
                },
                enumerable: true, configurable: true
            });
            Object.defineProperty(File.prototype, Symbol.toStringTag,
                { value: 'File', configurable: true });

            // FileList (§4.10.5.1): the files a control holds. The engine never reads a disk, so a page
            // that builds one is refused, and the list a file control answers is the empty one.
            function FileList() {
                throw new TypeError('Failed to construct ' + formDataQuote('FileList')
                    + ': Illegal constructor');
            }
            FileList.prototype.length = 0;
            FileList.prototype.item = function () { return null; };
            Object.defineProperty(FileList.prototype, Symbol.toStringTag,
                { value: 'FileList', configurable: true });
            window.FileList = FileList;

            // ── FileReader (File API §4) ────────────────────────────────────────
            // A read runs as a task, and the reader is an event target the whole way: 'loadstart',
            // 'progress', then 'load' and 'loadend' around the result the page reads back. The three
            // states are constants on the interface, on its prototype and on every reader (measured:
            // EMPTY 0, LOADING 1, DONE 2), and a blob that is not a blob is refused for the parameter.
            var FILE_READERS = {
                readAsText: function (blob) { return blobToString(blob); },
                readAsDataURL: function (blob) {
                    var bytes = blobBytes(blob);
                    var bin = '';
                    for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
                    return 'data:' + (blob.type || '') + ';base64,' + __acrux.btoa(bin);
                },
                readAsArrayBuffer: function (blob) { return blobBytes(blob).buffer; },
                readAsBinaryString: function (blob) {
                    var bytes = blobBytes(blob), out = '';
                    for (var i = 0; i < bytes.length; i++) out += String.fromCharCode(bytes[i]);
                    return out;
                }
            };
            function FileReader() { }
            FileReader.prototype.result = null;
            FileReader.prototype.error = null;
            FileReader.prototype.readyState = 0;
            FileReader.prototype.onloadstart = null;
            FileReader.prototype.onprogress = null;
            FileReader.prototype.onload = null;
            FileReader.prototype.onerror = null;
            FileReader.prototype.onloadend = null;
            function readerRefused(method) {
                return new TypeError('Failed to execute ' + formDataQuote(method) + ' on '
                    + formDataQuote('FileReader') + ': parameter 1 is not of type '
                    + formDataQuote('Blob') + '.');
            }
            function readerRun(self, method, blob) {
                if (arguments.length < 3 || !(blob instanceof Blob)) throw readerRefused(method);
                if (self.readyState === 1) {
                    throw new DOMException('Failed to execute ' + formDataQuote(method) + ' on '
                        + formDataQuote('FileReader') + ': The object is in state '
                        + (self.readyState === 1 ? 'LOADING' : 'DONE') + ' not EMPTY.', 'InvalidStateError');
                }
                self.readyState = 1;
                self.result = null;
                self.error = null;
                readerFire(self, 'loadstart');
                Promise.resolve().then(function () {
                    var value;
                    try { value = FILE_READERS[method](blob); }
                    catch (error) {
                        self.readyState = 2;
                        self.error = error;
                        readerFire(self, 'error');
                        readerFire(self, 'loadend');
                        return;
                    }
                    readerFire(self, 'progress');
                    self.result = value;
                    self.readyState = 2;
                    readerFire(self, 'load');
                    readerFire(self, 'loadend');
                });
            }
            // The event a reader raises is built here rather than by the engine: a JS object owns its
            // members, so 'event.target' is the very reader the page holds — a host event object keeps
            // the target through a CLR field and hands back a different wrapper for a script object.
            function readerEvent(self, type, loaded) {
                var proto = typeof ProgressEvent === 'function' ? ProgressEvent.prototype : null;
                var event = proto ? Object.create(proto) : {};
                Object.defineProperties(event, {
                    type: { value: type, writable: true, enumerable: true, configurable: true },
                    target: { value: self, writable: true, enumerable: true, configurable: true },
                    currentTarget: { value: self, writable: true, enumerable: true, configurable: true },
                    srcElement: { value: self, writable: true, enumerable: true, configurable: true },
                    eventPhase: { value: 2, writable: true, enumerable: true, configurable: true },
                    bubbles: { value: false, writable: true, enumerable: true, configurable: true },
                    cancelable: { value: false, writable: true, enumerable: true, configurable: true },
                    composed: { value: false, writable: true, enumerable: true, configurable: true },
                    defaultPrevented: { value: false, writable: true, enumerable: true, configurable: true },
                    isTrusted: { value: false, writable: true, enumerable: true, configurable: true },
                    cancelBubble: { value: false, writable: true, enumerable: true, configurable: true },
                    returnValue: { value: true, writable: true, enumerable: true, configurable: true },
                    lengthComputable: { value: false, writable: true, enumerable: true, configurable: true },
                    loaded: { value: loaded || 0, writable: true, enumerable: true, configurable: true },
                    total: { value: 0, writable: true, enumerable: true, configurable: true },
                    timeStamp: { value: Date.now(), writable: true, enumerable: true, configurable: true },
                    preventDefault: { value: function () { }, writable: true, enumerable: true, configurable: true },
                    stopPropagation: { value: function () { }, writable: true, enumerable: true, configurable: true },
                    stopImmediatePropagation: { value: function () { }, writable: true, enumerable: true, configurable: true },
                    initEvent: { value: function () { }, writable: true, enumerable: true, configurable: true },
                    composedPath: { value: function () { return [self]; }, writable: true, enumerable: true, configurable: true }
                });
                return event;
            }
            function readerFire(self, type) {
                try { self.dispatchEvent(readerEvent(self, type, self.result ? 1 : 0)); } catch (e) { }
            }
            function makeReaderMethod(name) {
                return function (blob) { readerRun(this, name, blob); };
            }
            for (var readerName in FILE_READERS) {
                if (Object.prototype.hasOwnProperty.call(FILE_READERS, readerName))
                    FileReader.prototype[readerName] = makeReaderMethod(readerName);
            }
            FileReader.prototype.abort = function () {
                if (this.readyState === 0 || this.readyState === 2) return;
                this.readyState = 2;
                this.result = null;
                this.error = new DOMException('The user aborted a request.', 'AbortError');
                readerFire(this, 'error');
                readerFire(this, 'abort');
                readerFire(this, 'loadend');
            };
            var readerStates = [['EMPTY', 0], ['LOADING', 1], ['DONE', 2]];
            for (var rs = 0; rs < readerStates.length; rs++) {
                (function (readerState, value) {
                    var shape = { value: value, writable: false, enumerable: false, configurable: false };
                    try { Object.defineProperty(FileReader.prototype, readerState, shape); } catch (e) { }
                    try { Object.defineProperty(FileReader, readerState, shape); } catch (e) { }
                })(readerStates[rs][0], readerStates[rs][1]);
            }
            Object.defineProperty(FileReader.prototype, Symbol.toStringTag,
                { value: 'FileReader', configurable: true });

            // URL.createObjectURL hands out a blob: reference the host can later revoke; without a
            // backing store it is a stable label, which is all a page that only round-trips the URL needs.
            var objectUrls = {};
            URL.createObjectURL = function (blob) {
                var url = 'blob:acrux/' + __acrux.randomUUID();
                objectUrls[url] = blob;
                return url;
            };
            URL.revokeObjectURL = function (url) { delete objectUrls[url]; };
            window.URL = URL;

            // ── URL.canParse, reportError, AbortSignal.any (URL §2.5, HTML §8.8, DOM §4.6) ──
            // 'canParse' is the question the constructor answers without building anything, so it is a
            // static of the URL interface rather than a method of an instance.
            URL.canParse = function (url, base) {
                if (arguments.length < 1) {
                    throw new TypeError('Failed to execute \'canParse\' on \'URL\': 1 argument'
                        + ' required, but only 0 present.');
                }
                var text = String(url === undefined ? '' : url);
                if (!text) return false;
                // A url that is not absolute is only parseable against a base that is, and the host
                // constructor is lenient about what it makes — so the shape is asked here.
                var scheme = /^[a-zA-Z][a-zA-Z0-9+.\-]*:/;
                var root = (base === undefined || base === null) ? '' : String(base);
                if (!scheme.test(text) && !scheme.test(root)) return false;
                try {
                    var made = new URL(text, root);
                    return !!made && typeof made.href === 'string' && made.href.length > 0;
                } catch (e) { return false; }
            };

            // URL §2.4.1: 'parse' is the constructor asked as a question — the same parse, but a url the
            // base cannot resolve answers null instead of throwing (measured: 'URL.parse('not a url%')'
            // is null while 'URL.parse('https://example.com/a', base)' reads its pathname).
            URL.parse = function (url, base) {
                if (arguments.length < 1) {
                    throw new TypeError('Failed to execute \'parse\' on \'URL\': 1 argument'
                        + ' required, but only 0 present.');
                }
                // The host constructor is lenient about what it makes, so a run that is not absolute and
                // has no absolute base to lean on is refused here the way the constructor refuses it.
                if (!URL.canParse(url, base)) return null;
                try {
                    var made = new URL(String(url), (base === undefined || base === null) ? '' : String(base));
                    return made && typeof made.href === 'string' && made.href.length > 0 ? made : null;
                } catch (e) { return null; }
            };

            // 'reportError' takes a value the page raised without throwing it: it arrives as the
            // error event the window would have got, with the message the reference writes —
            // 'Uncaught ' plus the name and message of an error, or the value as text for anything
            // else, and the value itself still reachable from the event.
            // The event the error carries is the interface's own: the message the reference writes,
            // the value still reachable from 'error', and the position fields left empty.
            function ErrorEvent(type, init) {
                init = init || { };
                var evt = new Event(String(type === undefined ? '' : type),
                    { bubbles: !!init.bubbles, cancelable: !!init.cancelable });
                // The five members the interface declares belong to it, not to the event the page
                // happens to have been handed, so they are kept non-enumerably and read through the
                // prototype — which is also what makes the object print as an ErrorEvent rather than
                // as the Event its dispatch was built from (measured).
                function keep(key, value) {
                    try { evt[key] = value; } catch (e) { }
                    try {
                        Object.defineProperty(evt, key, { value: value, writable: true,
                            enumerable: false, configurable: true });
                    } catch (e) { }
                }
                keep('message', init.message === undefined ? '' : String(init.message));
                keep('error', init.error === undefined ? null : init.error);
                keep('filename', init.filename === undefined ? '' : String(init.filename));
                keep('lineno', Number(init.lineno) || 0);
                keep('colno', Number(init.colno) || 0);
                try {
                    var level = window.ErrorEvent && window.ErrorEvent.prototype;
                    if (level) Object.setPrototypeOf(evt, level);
                } catch (e) { }
                return evt;
            }
            ErrorEvent.prototype = Object.create(Object.prototype);
            Object.defineProperty(ErrorEvent.prototype, 'constructor',
                { value: ErrorEvent, writable: true, configurable: true });
            window.ErrorEvent = ErrorEvent;
            (window.__domProto || (window.__domProto = {})).ErrorEvent = ErrorEvent.prototype;
            window.reportError = function (value) {
                var message = 'Uncaught ';
                if (value && typeof value === 'object' && value.name && value.message)
                    message += String(value.name) + ': ' + String(value.message);
                else message += String(value);
                var event = null;
                try {
                    event = new ErrorEvent('error', { message: message, error: value,
                        filename: '', lineno: 0, colno: 0 });
                } catch (e) { return; }
                try { window.dispatchEvent(event); } catch (e) { }
                // The window's own listeners are what a page sets to hear about its errors, and the
                // handler written as a property is called first (HTML §8.8).
                var handler = typeof window.onerror === 'function' ? window.onerror : null;
                if (handler) { try { handler.call(window, message, '', 0, 0, value); } catch (e) { } }
                var bag = window.__winError || [];
                var copy = bag.slice();
                for (var i = 0; i < copy.length; i++) {
                    var fn = copy[i];
                    try { (typeof fn === 'function' ? fn : fn.handleEvent).call(window, event); }
                    catch (e) { }
                }
            };

            // The signals of a list, combined: the one the page gets is aborted as soon as any of them
            // is and carries that signal's reason; an input already aborted aborts it at once (§4.6).
            AbortSignal.any = function (signals) {
                var list;
                if (signals !== null && signals !== undefined
                    && typeof signals[Symbol.iterator] === 'function') {
                    list = Array.from(signals);
                } else {
                    // A sequence is asked for, and a value with nothing iterable about it is not one.
                    throw new TypeError('Failed to execute \'any\' on \'AbortSignal\': The provided'
                        + ' value cannot be converted to a sequence.');
                }
                for (var i = 0; i < list.length; i++) {
                    var item = list[i];
                    if (!item || item.aborted === undefined || typeof item.addEventListener !== 'function') {
                        throw new TypeError('Failed to execute \'any\' on \'AbortSignal\': Failed to'
                            + ' convert value to \'AbortSignal\'.');
                    }
                }
                var made = AbortSignal._make();
                for (var j = 0; j < list.length; j++) {
                    if (list[j].aborted) return AbortSignal._abort(made, list[j].reason);
                }
                for (var k = 0; k < list.length; k++) {
                    (function (source) {
                        source.addEventListener('abort', function () {
                            AbortSignal._abort(made, source.reason);
                        });
                    })(list[k]);
                }
                return made;
            };

            // ── DOMPoint, DOMRectList (Geometry Interfaces §3, §5) ─────────────
            function DOMPointReadOnly(x, y, z, w) {
                var self = this;
                this.x = x === undefined ? 0 : Number(x);
                this.y = y === undefined ? 0 : Number(y);
                this.z = z === undefined ? 0 : Number(z);
                this.w = w === undefined ? 1 : Number(w);
                this.toJSON = function () {
                    return { x: self.x, y: self.y, z: self.z, w: self.w };
                };
            }
            DOMPointReadOnly.fromPoint = function (other) {
                other = other || { };
                return new DOMPointReadOnly(other.x, other.y, other.z, other.w);
            };
            DOMPointReadOnly.prototype.matrixTransform = function () {
                return new DOMPointReadOnly(this.x, this.y, this.z, this.w);
            };
            function DOMPoint(x, y, z, w) {
                DOMPointReadOnly.call(this, x, y, z, w);
            }
            DOMPoint.prototype = Object.create(DOMPointReadOnly.prototype);
            DOMPoint.prototype.constructor = DOMPoint;
            DOMPoint.fromPoint = function (other) {
                other = other || { };
                return new DOMPoint(other.x, other.y, other.z, other.w);
            };

            // The list a geometry read answers is the array-like face of the interface: the items in
            // order, a length, and 'item' for the out-of-range answer of null.
            // The list a range or a box answers (CSSOM View §2.4.1): 'length', 'item' and the index,
            // with the tag the interface gives — the level is registered for the wrappers the engine
            // hands out, and those read their tag from here.
            function DOMRectList(items) {
                var list = items || [];
                var self = this;
                this.length = list.length;
                this.item = function (index) {
                    index = Number(index);
                    return index >= 0 && index < list.length ? list[index] : null;
                };
                for (var i = 0; i < list.length; i++) self[i] = list[i];
            }

            // ── Request and Response (Fetch §3, §6, §7) ────────────────────────
            // A body is held as text here, or as the stream the page handed over: the page-facing half
            // of the interface is the shape around it — the headers as a Headers object, 'ok', 'type'
            // and 'url' derived from the exchange, and the read methods each handing back a promise
            // that can only be taken once (§6.3). 'body' and 'bodyUsed' are accessors the interface
            // declares, so reading them off the prototype is refused the way the reference refuses it
            // (measured: 'Response.prototype.body' raises an illegal invocation rather than answering
            // undefined), and 'arrayBuffer' hands the buffer, not a view of it (§4.5.4).
            var BODY_SLOTS = new WeakMap();
            var RESPONSE_SLOTS = new WeakMap();
            var REQUEST_SLOTS = new WeakMap();
            function bodyOf(target, name) {
                var state = BODY_SLOTS.get(target);
                if (!state) {
                    // Reading a member off an object that is not one is refused with the reference's
                    // own sentence: the body read throws 'Illegal invocation' as the whole message,
                    // while a method names itself first (both measured).
                    if (name === 'body' || name === 'bodyUsed') throw new TypeError('Illegal invocation');
                    throw new TypeError('Failed to execute ' + formDataQuote(name) + ' on '
                        + formDataQuote('Body') + ': Illegal invocation');
                }
                return state;
            }
            function textBytes(text) {
                if (typeof Uint8Array === 'function' && typeof TextEncoder === 'function') {
                    return new TextEncoder().encode(text);
                }
                var bytes = [];
                for (var i = 0; i < text.length; i++) {
                    var code = text.charCodeAt(i);
                    if (code < 128) bytes.push(code);
                    else if (code < 2048) {
                        bytes.push(192 | (code >> 6));
                        bytes.push(128 | (code & 63));
                    } else {
                        bytes.push(224 | (code >> 12));
                        bytes.push(128 | ((code >> 6) & 63));
                        bytes.push(128 | (code & 63));
                    }
                }
                return typeof Uint8Array === 'function' ? new Uint8Array(bytes) : bytes;
            }
            function bytesText(bytes) {
                if (typeof TextDecoder === 'function') return new TextDecoder().decode(bytes);
                var out = '';
                for (var i = 0; i < bytes.length; i++) out += String.fromCharCode(bytes[i]);
                return out;
            }
            function bodyParts(body) {
                if (body === undefined || body === null) return { text: '', stream: null, bytes: null };
                if (typeof body === 'string') return { text: body, stream: null, bytes: null };
                if (streamQueueOf(body)) return { text: '', stream: body, bytes: null };
                if (typeof body === 'object') {
                    // A blob, a buffer or a view of one is taken as the bytes they are: the text is
                    // what those bytes decode to, and the reads that answer bytes hand the run back
                    // rather than a re-encoding of the text (§6.1).
                    if (body instanceof Blob || (typeof ArrayBuffer === 'function' && body instanceof ArrayBuffer)
                        || body.buffer instanceof ArrayBuffer) {
                        var run = partBytes(body);
                        return { text: bytesText(run), stream: null, bytes: run };
                    }
                    if (typeof body.__text === 'string') return { text: body.__text, stream: null, bytes: null };
                    if (typeof body.toString === 'function') {
                        var made = body.toString();
                        if (made !== '[object Object]') return { text: String(made), stream: null, bytes: null };
                    }
                }
                return { text: String(body), stream: null, bytes: null };
            }
            function drainBodyStream(state) {
                var iterator = streamAsyncIterator(state.stream);
                var chunks = [];
                function step() {
                    return iterator.next().then(function (result) {
                        if (result.done) {
                            var total = 0, i;
                            for (i = 0; i < chunks.length; i++) total += chunks[i].length || 0;
                            var joined = typeof Uint8Array === 'function'
                                ? new Uint8Array(total) : [];
                            var at = 0;
                            for (i = 0; i < chunks.length; i++) {
                                for (var j = 0; j < (chunks[i].length || 0); j++) joined[at++] = chunks[i][j];
                            }
                            return joined;
                        }
                        chunks.push(typeof result.value === 'string' ? textBytes(result.value) : result.value);
                        return step();
                    });
                }
                return step();
            }
            function readBody(state) {
                if (state.used) throw new TypeError('Body already read');
                state.used = true;
            }
            var BODY_MEMBERS = {
                text: function () {
                    var state = bodyOf(this, 'text');
                    readBody(state);
                    if (state.stream) {
                        return drainBodyStream(state).then(function (bytes) {
                            state.text = bytesText(bytes); return state.text; });
                    }
                    return Promise.resolve(state.text);
                },
                json: function () {
                    var state = bodyOf(this, 'json');
                    readBody(state);
                    if (state.stream) {
                        return drainBodyStream(state).then(function (bytes) {
                            return JSON.parse(bytesText(bytes)); });
                    }
                    try { return Promise.resolve(JSON.parse(state.text)); }
                    catch (e) { return Promise.reject(new SyntaxError(String(e.message))); }
                },
                arrayBuffer: function () {
                    var state = bodyOf(this, 'arrayBuffer');
                    readBody(state);
                    if (state.stream) {
                        return drainBodyStream(state).then(function (bytes) {
                            return bytes.buffer ? bytes.buffer : bytes; });
                    }
                    if (state.bytes) return Promise.resolve(state.bytes.slice().buffer);
                    var made = textBytes(state.text);
                    return Promise.resolve(made.buffer === undefined ? made : made.buffer);
                },
                blob: function () {
                    var state = bodyOf(this, 'blob');
                    readBody(state);
                    var type = state.headers ? (state.headers.get('content-type') || '') : '';
                    if (state.stream) {
                        return drainBodyStream(state).then(function (bytes) {
                            return new Blob([bytesText(bytes)], { type: type }); });
                    }
                    return Promise.resolve(new Blob([state.bytes || state.text], { type: type }));
                },
                bytes: function () {
                    var state = bodyOf(this, 'bytes');
                    readBody(state);
                    if (state.stream) return drainBodyStream(state);
                    if (state.bytes) return Promise.resolve(state.bytes.slice());
                    return Promise.resolve(textBytes(state.text));
                },
                formData: function () {
                    var state = bodyOf(this, 'formData');
                    readBody(state);
                    var made = new FormData();
                    var type = state.headers ? String(state.headers.get('content-type') || '') : '';
                    var text = state.text;
                    // A url-encoded body is the pair list the form interface would have handed over;
                    // anything else is the empty set, because this engine has no multipart reader yet.
                    if (type.indexOf('urlencoded') >= 0 || type === '') {
                        var pairs = new URLSearchParams(text);
                        pairs.forEach(function (value, key) { made.append(key, value); });
                    }
                    return Promise.resolve(made);
                }
            };
            function installBodyMembers(proto) {
                Object.keys(BODY_MEMBERS).forEach(function (member) {
                    Object.defineProperty(proto, member, {
                        value: BODY_MEMBERS[member], writable: true, configurable: true,
                        enumerable: false
                    });
                });
                Object.defineProperty(proto, 'bodyUsed', {
                    configurable: true, enumerable: false,
                    get: function () { return bodyOf(this, 'bodyUsed').used; }
                });
                Object.defineProperty(proto, 'body', {
                    configurable: true, enumerable: false,
                    get: function () {
                        var state = bodyOf(this, 'body');
                        if (state.stream) return state.stream;
                        if (!state.text.length) return null;
                        if (state.bodyStream === undefined) {
                            state.bodyStream = singleChunkStream(
                                state.bytes ? state.bytes.slice() : textBytes(state.text));
                        }
                        return state.bodyStream;
                    }
                });
            }
            // The members of an interface live on its prototype, so an instance owns nothing and a
            // page that lists its keys sees the empty list the reference gives (measured: a key list
            // of a fresh Response is empty).
            function installAccessors(proto, store, name, members) {
                Object.keys(members).forEach(function (member) {
                    Object.defineProperty(proto, member, {
                        configurable: true, enumerable: false,
                        get: function () {
                            var state = store.get(this);
                            if (!state) {
                                throw new TypeError('Failed to get ' + formDataQuote(member) + ' on '
                                    + formDataQuote(name) + ': Illegal invocation');
                            }
                            return members[member](state);
                        }
                    });
                });
            }

            function Response(body, init) {
                var options = init || { };
                var parts = bodyParts(body);
                var status = options.status === undefined ? 200 : Number(options.status);
                var state = { text: parts.text, used: false, stream: parts.stream,
                    headers: new Headers(options.headers || { }),
                    url: options.url === undefined ? '' : String(options.url),
                    redirected: !!options.redirected,
                    type: options.type === undefined ? 'default' : String(options.type),
                    status: status,
                    statusText: options.statusText === undefined ? '' : String(options.statusText),
                    trailer: new Headers(options.trailer || { }) };
                if (state.text.length && !state.headers.has('content-type'))
                    state.headers.set('content-type', 'text/plain;charset=UTF-8');
                BODY_SLOTS.set(this, state);
                RESPONSE_SLOTS.set(this, state);
            }
            Response.error = function () { return new Response('', { status: 0, type: 'error' }); };
            Response.redirect = function (url, status) {
                var made = new Response('', { status: status === undefined ? 302 : Number(status) });
                made.headers.set('location', String(url));
                return made;
            };
            Response.json = function (data, init) {
                var text;
                try { text = JSON.stringify(data); }
                catch (e) { text = undefined; }
                if (text === undefined) {
                    throw new TypeError('Failed to execute \'json\' on \'Response\':'
                        + ' The data is not JSON');
                }
                var made = new Response(text, init);
                made.headers.set('content-type', 'application/json');
                return made;
            };
            installBodyMembers(Response.prototype);
            installAccessors(Response.prototype, RESPONSE_SLOTS, 'Response', {
                type: function (s) { return s.type; },
                url: function (s) { return s.url; },
                redirected: function (s) { return s.redirected; },
                status: function (s) { return s.status; },
                ok: function (s) { return s.status >= 200 && s.status <= 299; },
                statusText: function (s) { return s.statusText; },
                headers: function (s) { return s.headers; },
                trailer: function (s) { return s.trailer; }
            });
            Object.defineProperty(Response.prototype, 'clone', {
                configurable: true, writable: true, enumerable: false,
                value: function () {
                    var state = RESPONSE_SLOTS.get(this);
                    if (!state) {
                        throw new TypeError('Failed to execute ' + formDataQuote('clone') + ' on '
                            + formDataQuote('Response') + ': Illegal invocation');
                    }
                    if (state.used) {
                        throw new TypeError('Failed to execute ' + formDataQuote('clone') + ' on '
                            + formDataQuote('Response') + ': Cannot clone a consumed response body.');
                    }
                    var made = new Response(state.stream ? state.stream : state.text,
                        { status: state.status, statusText: state.statusText, headers: state.headers,
                            url: state.url, type: state.type });
                    return made;
                }
            });

            function Request(input, init) {
                init = init || { };
                var copied = (input && typeof input === 'object'
                    && (input instanceof Request || input instanceof Response)) ? input : null;
                var target = copied ? copied.url : String(input === undefined ? '' : input);
                var base = '';
                try { base = window.location ? window.location.href : ''; } catch (e) { base = ''; }
                try { if (base) target = new URL(target, base).href; } catch (e) { }
                var copiedState = copied ? BODY_SLOTS.get(copied) : null;
                var body = init.body !== undefined ? init.body
                    : (copiedState ? (copiedState.stream || copiedState.text) : undefined);
                var parts = bodyParts(body);
                var state = { text: parts.text, used: false, stream: parts.stream,
                    url: target,
                    method: String(init.method !== undefined ? init.method
                        : (copied ? copied.method : 'GET')).toUpperCase(),
                    headers: new Headers(init.headers !== undefined ? init.headers
                        : (copied ? copied.headers : null)),
                    mode: init.mode !== undefined ? String(init.mode) : (copied ? copied.mode : 'cors'),
                    credentials: init.credentials !== undefined ? String(init.credentials)
                        : (copied ? copied.credentials : 'same-origin'),
                    cache: init.cache !== undefined ? String(init.cache) : 'default',
                    redirect: init.redirect !== undefined ? String(init.redirect) : 'follow',
                    referrer: init.referrer !== undefined ? String(init.referrer) : '',
                    referrerPolicy: init.referrerPolicy !== undefined
                        ? String(init.referrerPolicy) : '',
                    integrity: init.integrity !== undefined ? String(init.integrity) : '',
                    keepalive: !!init.keepalive,
                    destination: init.destination !== undefined ? String(init.destination) : '',
                    isHistoryDownload: false, isReloaded: false, isClientNavigate: false,
                    signal: (init.signal !== undefined && init.signal !== null)
                        ? init.signal : AbortSignal._make() };
                BODY_SLOTS.set(this, state);
                REQUEST_SLOTS.set(this, state);
            }
            installBodyMembers(Request.prototype);
            installAccessors(Request.prototype, REQUEST_SLOTS, 'Request', {
                url: function (s) { return s.url; },
                method: function (s) { return s.method; },
                headers: function (s) { return s.headers; },
                mode: function (s) { return s.mode; },
                credentials: function (s) { return s.credentials; },
                cache: function (s) { return s.cache; },
                redirect: function (s) { return s.redirect; },
                referrer: function (s) { return s.referrer; },
                referrerPolicy: function (s) { return s.referrerPolicy; },
                integrity: function (s) { return s.integrity; },
                keepalive: function (s) { return s.keepalive; },
                destination: function (s) { return s.destination; },
                signal: function (s) { return s.signal; },
                isHistoryDownload: function (s) { return s.isHistoryDownload; },
                isReloaded: function (s) { return s.isReloaded; },
                isClientNavigate: function (s) { return s.isClientNavigate; }
            });
            Object.defineProperty(Request.prototype, 'clone', {
                configurable: true, writable: true, enumerable: false,
                value: function () {
                    var state = REQUEST_SLOTS.get(this);
                    if (!state) {
                        throw new TypeError('Failed to execute ' + formDataQuote('clone') + ' on '
                            + formDataQuote('Request') + ': Illegal invocation');
                    }
                    return new Request(state.url, { method: state.method, headers: state.headers,
                        body: state.stream ? state.stream : state.text, mode: state.mode,
                        credentials: state.credentials, referrerPolicy: state.referrerPolicy,
                        integrity: state.integrity, keepalive: state.keepalive,
                        redirect: state.redirect, signal: state.signal });
                }
            });
            Object.defineProperty(Response.prototype, Symbol.toStringTag,
                { value: 'Response', configurable: true });
            Object.defineProperty(Request.prototype, Symbol.toStringTag,
                { value: 'Request', configurable: true });
            [DOMPointReadOnly, DOMPoint, DOMRectList].forEach(function (ctor) {
                Object.defineProperty(ctor.prototype, Symbol.toStringTag,
                    { value: ctor.name, configurable: true });
            });
            window.DOMPoint = DOMPoint;
            window.DOMPointReadOnly = DOMPointReadOnly;
            window.DOMRectList = DOMRectList;
            window.Response = Response;
            window.Request = Request;


            // 'new CSSStyleSheet()' — a sheet the script owns (CSSOM §5.2.2). The rules and the
            // disabled flag live in the engine; 'replace' is the async twin of 'replaceSync',
            // so it is built here out of a Promise rather than reaching into the host.
            function CSSStyleSheet() {
                var sheet = __acrux.createCSSStyleSheet();
                try {
                    sheet.replace = function (text) {
                        var self = this;
                        return new Promise(function (resolve, reject) {
                            try { self.replaceSync(text); resolve(self); }
                            catch (e) { reject(e); }
                        });
                    };
                } catch (e) { }
                return sheet;
            }
            // The constructor the page calls is this one — a sheet the script builds itself, empty
            // until 'replaceSync' or 'insertRule' fills it (CSSOM §5.2.2). It was written in the
            // script's own scope and never reached the window, so 'typeof CSSStyleSheet' answered
            // nothing at all.
            window.CSSStyleSheet = CSSStyleSheet;

            // ── The CSSOM interfaces (CSSOM §5.2-§5.6) ──────────────────────────
            // The rules a sheet hands out, the declarations they carry and the lists that hold them
            // are host objects, and the stamping that gives a wrapper its interface reads these
            // levels: the tag, 'rule instanceof CSSMediaRule' and the members the interface declares
            // all come from the level. A member read through a receiver the engine did not make is
            // refused the way the reference refuses it (measured: 'CSSRule.prototype.cssText' raises a
            // TypeError whose message is 'Illegal invocation', while a rule taken out of the sheet
            // answers its own text) — an instance never reaches these functions, because its own
            // member of the same name is read first.
            (function () {
                var levels = window.__domProto || (window.__domProto = {});
                function quoted(value) { return String.fromCharCode(39) + value + String.fromCharCode(39); }
                function level(name, baseName) {
                    var proto = levels[name] || Object.create(levels[baseName] || Object.prototype);
                    levels[name] = proto;
                    var earlier = window[name];
                    // A name the script already gave a working constructor keeps it: 'new
                    // CSSStyleSheet()' builds a sheet the page owns, so refusing it would take an
                    // interface away rather than describe one.
                    var Ctor = typeof earlier === 'function' ? earlier : function () {
                        throw new TypeError('Failed to construct ' + quoted(name)
                            + ': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = proto;
                    Object.defineProperty(proto, 'constructor',
                        { value: Ctor, writable: true, configurable: true });
                    Object.defineProperty(proto, Symbol.toStringTag,
                        { value: name, configurable: true, enumerable: false });
                    window[name] = Ctor;
                    return proto;
                }
                function refusal() { throw new TypeError('Illegal invocation'); }
                function readMember(iface, member) {
                    var proto = levels[iface];
                    if (!proto) return;
                    try {
                        Object.defineProperty(proto, member,
                            { configurable: true, enumerable: false, get: refusal, set: refusal });
                    } catch (e) { }
                }
                function callMember(iface, member) {
                    var proto = levels[iface];
                    if (!proto) return;
                    try {
                        Object.defineProperty(proto, member, { configurable: true, writable: true,
                            enumerable: false, value: function () { refusal(); } });
                    } catch (e) { }
                }
                var ruleLevel = level('CSSRule', null);
                // The rule-type constants the interface still names, with the values at-rules were
                // introduced in — not the order their own objects are read in (measured against the
                // host's own 'type': '@keyframes' 7, '@keyframe' 8, '@counter-style' 11, '@supports'
                // 12, '@namespace' 10, '@font-feature-values' 14).
                [['STYLE_RULE', 1], ['CHARSET_RULE', 2], ['IMPORT_RULE', 3],
                 ['MEDIA_RULE', 4], ['FONT_FACE_RULE', 5], ['PAGE_RULE', 6], ['KEYFRAMES_RULE', 7],
                 ['KEYFRAME_RULE', 8], ['MARGIN_RULE', 9], ['NAMESPACE_RULE', 10],
                 ['COUNTER_STYLE_RULE', 11], ['SUPPORTS_RULE', 12],
                 ['FONT_FEATURE_VALUES_RULE', 14]].forEach(function (pair) {
                    Object.defineProperty(ruleLevel, pair[0], { value: pair[1], writable: false,
                        enumerable: false, configurable: true });
                    Object.defineProperty(window.CSSRule, pair[0], { value: pair[1], writable: false,
                        enumerable: false, configurable: true });
                });
                ['type', 'cssText', 'parentRule', 'parentStyleSheet'].forEach(function (member) {
                    readMember('CSSRule', member);
                });
                level('CSSGroupingRule', 'CSSRule');
                readMember('CSSGroupingRule', 'cssRules');
                callMember('CSSGroupingRule', 'insertRule');
                callMember('CSSGroupingRule', 'deleteRule');
                level('CSSConditionRule', 'CSSGroupingRule');
                readMember('CSSConditionRule', 'conditionText');
                readMember('CSSConditionRule', 'name');
                level('CSSMediaRule', 'CSSConditionRule');
                readMember('CSSMediaRule', 'media');
                level('CSSSupportsRule', 'CSSConditionRule');
                readMember('CSSSupportsRule', 'supportsText');
                level('CSSContainerRule', 'CSSConditionRule');
                level('CSSStyleRule', 'CSSRule');
                readMember('CSSStyleRule', 'selectorText');
                readMember('CSSStyleRule', 'style');
                callMember('CSSStyleRule', 'insertRule');
                callMember('CSSStyleRule', 'deleteRule');
                level('CSSImportRule', 'CSSRule');
                readMember('CSSImportRule', 'href');
                readMember('CSSImportRule', 'media');
                readMember('CSSImportRule', 'supportsText');
                readMember('CSSImportRule', 'layerName');
                level('CSSKeyframesRule', 'CSSRule');
                readMember('CSSKeyframesRule', 'name');
                readMember('CSSKeyframesRule', 'cssRules');
                readMember('CSSKeyframesRule', 'length');
                callMember('CSSKeyframesRule', 'appendRule');
                callMember('CSSKeyframesRule', 'deleteRule');
                callMember('CSSKeyframesRule', 'getRuleByName');
                level('CSSKeyframeRule', 'CSSRule');
                readMember('CSSKeyframeRule', 'keyText');
                readMember('CSSKeyframeRule', 'style');
                level('CSSFontFaceRule', 'CSSRule');
                readMember('CSSFontFaceRule', 'style');
                level('CSSPageRule', 'CSSRule');
                readMember('CSSPageRule', 'selectorText');
                readMember('CSSPageRule', 'style');
                level('CSSNamespaceRule', 'CSSRule');
                readMember('CSSNamespaceRule', 'namespaceURI');
                readMember('CSSNamespaceRule', 'prefix');
                level('CSSMarginRule', 'CSSRule');
                readMember('CSSMarginRule', 'name');
                readMember('CSSMarginRule', 'style');
                level('CSSPropertyRule', 'CSSRule');
                readMember('CSSPropertyRule', 'name');
                readMember('CSSPropertyRule', 'syntax');
                readMember('CSSPropertyRule', 'inherits');
                readMember('CSSPropertyRule', 'initialValue');
                level('CSSCounterStyleRule', 'CSSRule');
                ['name', 'system', 'symbols', 'additiveSymbols', 'range', 'pad', 'suffix',
                 'negative', 'fallback', 'speakAs'].forEach(function (member) {
                    readMember('CSSCounterStyleRule', member);
                });
                level('CSSLayerBlockRule', 'CSSGroupingRule');
                readMember('CSSLayerBlockRule', 'name');
                level('CSSLayerStatementRule', 'CSSRule');
                readMember('CSSLayerStatementRule', 'nameList');
                level('CSSScopeRule', 'CSSGroupingRule');
                level('CSSStartingStyleRule', 'CSSRule');
                level('CSSFontFeatureValuesRule', 'CSSRule');
                level('CSSPositionTryRule', 'CSSRule');
                level('CSSViewTransitionRule', 'CSSRule');
                // The two lists (CSSOM §5.2.8, §6.1): 'length' with an 'item' read, and the index the
                // host answers for each position.
                level('CSSRuleList', null);
                readMember('CSSRuleList', 'length');
                callMember('CSSRuleList', 'item');
                level('StyleSheetList', null);
                readMember('StyleSheetList', 'length');
                callMember('StyleSheetList', 'item');
                level('MediaList', null);
                readMember('MediaList', 'mediaText');
                readMember('MediaList', 'length');
                callMember('MediaList', 'item');
                callMember('MediaList', 'appendMedium');
                callMember('MediaList', 'deleteMedium');
                // The declaration block (CSSOM §5.3.1): the seven members the interface names on its
                // own prototype, measured, with 'cssFloat' beside 'float' as the IDL has to.
                level('CSSStyleDeclaration', null);
                ['cssText', 'length', 'parentRule', 'cssFloat'].forEach(function (member) {
                    readMember('CSSStyleDeclaration', member);
                });
                ['getPropertyValue', 'getPropertyPriority', 'setProperty', 'removeProperty',
                 'item'].forEach(function (member) { callMember('CSSStyleDeclaration', member); });
                level('CSSStyleSheet', null);
                readMember('CSSStyleSheet', 'ownerRule');
                readMember('CSSStyleSheet', 'ownerNode');
                readMember('CSSStyleSheet', 'parentStyleSheet');
                readMember('CSSStyleSheet', 'cssRules');
                readMember('CSSStyleSheet', 'href');
                readMember('CSSStyleSheet', 'title');
                readMember('CSSStyleSheet', 'type');
                readMember('CSSStyleSheet', 'disabled');
                readMember('CSSStyleSheet', 'media');
                callMember('CSSStyleSheet', 'insertRule');
                callMember('CSSStyleSheet', 'deleteRule');
                callMember('CSSStyleSheet', 'replaceSync');
                // The geometry the CSSOM and the range reads through: a box's rectangle and a list of
                // them are stamped with these levels (Geometry Interfaces §4, §5, CSSOM View §2.4;
                // measured: [object DOMRect] for the box and [object DOMRectList] for the list).
                ['DOMPointReadOnly', 'DOMPoint', 'DOMRectReadOnly', 'DOMRect', 'DOMRectList']
                    .forEach(function (name) {
                        if (typeof window[name] !== 'function') return;
                        if (!levels[name]) levels[name] = window[name].prototype;
                    });
            })();

            function Image(width, height) {
                var img = document.createElement('img');
                if (width !== undefined) img.width = width;
                if (height !== undefined) img.height = height;
                return img;
            }

            window.addEventListener = function(type, listener, options) {
                if (document && document.addEventListener) {
                    document.addEventListener(type, listener, options);
                }
                // A 'message' event is fired on the Window rather than on the tree, so the window
                // keeps its own copy of these listeners for window.postMessage to reach (HTML §8.6).
                // The same holds for the error an 'reportError' raises: it belongs to the window,
                // and the tree root never sees it (HTML §8.8). The history's 'popstate' is a third —
                // the traversal is answered on the window, not on a node in the tree (HTML §5.6).
                var winBag = type === 'message' ? '__winMessage'
                    : (type === 'error' ? '__winError' : (type === 'popstate' ? '__winPopState' : null));
                if (winBag && listener) {
                    var ml = window[winBag] = window[winBag] || [];
                    if (ml.indexOf(listener) < 0) ml.push(listener);
                }
            };
            window.removeEventListener = function(type, listener, options) {
                if (document && document.removeEventListener) {
                    document.removeEventListener(type, listener, options);
                }
                var bag = type === 'message' ? window.__winMessage
                    : (type === 'error' ? window.__winError
                        : (type === 'popstate' ? window.__winPopState : null));
                if (bag) {
                    var i = bag.indexOf(listener);
                    if (i >= 0) bag.splice(i, 1);
                }
            };
            // A Window is an EventTarget; the listeners registered through window.addEventListener
            // live on the document (the host's single tree root), so a dispatch on the window walks
            // the same target. The return is 'not cancelled', matching EventTarget.dispatchEvent.
            window.dispatchEvent = function(evt) {
                if (document && document.dispatchEvent) return !!document.dispatchEvent(evt);
                return true;
            };

            Object.defineProperty(window, 'innerWidth', { configurable: true, get: function() { return __acrux.innerWidth(); } });
            Object.defineProperty(window, 'innerHeight', { configurable: true, get: function() { return __acrux.innerHeight(); } });
            Object.defineProperty(window, 'outerWidth', { configurable: true, get: function() { return __acrux.innerWidth(); } });
            Object.defineProperty(window, 'outerHeight', { configurable: true, get: function() { return __acrux.innerHeight(); } });
            Object.defineProperty(window, 'devicePixelRatio', { configurable: true, get: function() { return __acrux.devicePixelRatio(); } });
            Object.defineProperty(window, 'pageXOffset', { configurable: true, get: function() { return __acrux.scrollX(); } });
            Object.defineProperty(window, 'pageYOffset', { configurable: true, get: function() { return __acrux.scrollY(); } });
            Object.defineProperty(window, 'scrollX', { configurable: true, get: function() { return __acrux.scrollX(); } });
            Object.defineProperty(window, 'scrollY', { configurable: true, get: function() { return __acrux.scrollY(); } });

            window.scrollTo = function(x, y) { __acrux.scrollTo(x || 0, y || 0); };
            window.scrollBy = function(x, y) { __acrux.scrollBy(x || 0, y || 0); };
            window.scroll = window.scrollTo;
            // HTML §7.4.5: the six browser-UI bars the window names, each an object of the same
            // interface. This engine draws none of them and the interface has no way to hide one, so
            // every bar reads as visible — the state the reference gives (measured: all six answer
            // [object BarProp] with 'visible' true) — and writing it changes nothing.
            function BarProp() {
                throw new TypeError('Failed to construct ' + String.fromCharCode(39) + 'BarProp'
                    + String.fromCharCode(39) + ': Illegal constructor');
            }
            Object.defineProperty(BarProp.prototype, 'visible', {
                configurable: true, enumerable: true,
                get: function () { return true; }, set: function () { }
            });
            Object.defineProperty(BarProp.prototype, Symbol.toStringTag,
                { value: 'BarProp', configurable: true });
            window.BarProp = BarProp;
            var barNames = ['locationbar', 'menubar', 'personalbar', 'scrollbars', 'statusbar', 'toolbar'];
            for (var barAt = 0; barAt < barNames.length; barAt++) {
                (function (name) {
                    var bar = Object.create(BarProp.prototype);
                    Object.defineProperty(window, name, { configurable: true, enumerable: true,
                        get: function () { return bar; } });
                })(barNames[barAt]);
            }
            // HTML §8.9 (the legacy finder): this engine wires no search of its own, so the call
            // answers the way a search that found nothing does.
            window.find = function (searchString) { return false; };
            // CSSOM: every longhand of a computed style reads as a camelCase property as
            // well as through getPropertyValue('dashed-name'). The host object only carries
            // members for the properties the C# side wrote out by hand, so the rest are
            // answered from its __resolve; a name that is not a CSS property stays undefined.
            function __wrapComputedStyle(cs) {
                if (cs === null || cs === undefined || typeof Proxy !== 'function') return cs;
                return new Proxy(cs, {
                    get: function (target, name) {
                        // A computed style is an array-like of property names, so a numeric key
                        // reads the same name item() gives for it.
                        if (typeof name === 'string' && /^[0-9]+$/.test(name)) return target.item(+name);
                        if (typeof name !== 'string') {
                            if (name === Symbol.toStringTag) return 'CSSStyleDeclaration';
                            return target[name];
                        }
                        var own = target[name];
                        if (own !== undefined) return own;
                        var resolved = target.__resolve(name);
                        return resolved == null ? undefined : resolved;
                    },
                    has: function (target, name) {
                        if (typeof name === 'string' && /^[0-9]+$/.test(name)) return true;
                        if (typeof name === 'string' && target.__resolve(name) != null) return true;
                        return typeof target[name] !== 'undefined';
                    }
                });
            }
            // The second argument names a pseudo-element whose style the page wants instead; the
            // engine resolves it from the element's collected pseudo declarations (see
            // DocumentHost.getComputedStyle). A name that is no pseudo-element answers an empty
            // block, and null or omission leaves the element's own style.
            window.getComputedStyle = function (el, pseudo) {
                return __wrapComputedStyle(document.getComputedStyle(el, pseudo === undefined ? null : pseudo));
            };
            // 'MediaQueryList.media' is the query re-serialised rather than the text handed in, and
            // the engine does the whole of it (see mediaQuerySerialize): whitespace and casing are
            // canonical, comparison operators and feature colons get their spacing, a leading
            // 'all and ' goes away, and every math function that reduces to one absolute term comes
            // back folded — the query 'min-width: CALC(100PX * 4 + 45PX)' reads as
            // '(min-width: calc(445px))'. The same function answers 'CSSRule.conditionText' and
            // 'CSSRule.media.mediaText', so the three spellings of one query cannot drift apart.
            function __serializeMediaQuery(q) { return q ? __acrux.mediaQuerySerialize(q) : ''; }
            window.matchMedia = function (query) {
                if (arguments.length < 1) {
                    throw new TypeError('Failed to execute ' + formDataQuote('matchMedia') + ' on '
                        + formDataQuote('Window') + ': 1 argument required, but only 0 present.');
                }
                var q = String(query === undefined ? '' : query).trim();
                // The same evaluator an @media rule is filtered through, so a script and a
                // stylesheet cannot disagree about the viewport (CSSOM View §7.1).
                var list = Object.create(window.MediaQueryList.prototype);
                Object.defineProperties(list, {
                    __query: { value: q, enumerable: false, configurable: true },
                    media: { get: function () { return __serializeMediaQuery(q); },
                        enumerable: true, configurable: true },
                    matches: {
                        // Live: the list follows the viewport, and a listener registered against it is
                        // told when the answer it carries changes (MQ §3.1).
                        get: function () { return !!__acrux.mediaQueryMatches(q); },
                        enumerable: true, configurable: true
                    }
                });
                list.onchange = null;
                // The list is registered against the checkpoint that tells it when the viewport moves;
                // the checkpoint is built with the rest of the DOM surface below, so it is asked for on
                // the way out rather than assumed.
                if (typeof window.__mediaAdd === 'function') window.__mediaAdd(list);
                // The first read fixes the answer the change is measured against.
                list.__last = list.matches;
                return list;
            };
            window.open = function(url, name, features) {
                if (url) location.href = url;
                return window;
            };
            window.close = function() {};
            window.print = function() {};
            window.stop = function() {};
            window.focus = function() {};
            window.blur = function() {};
            window.moveBy = function(x, y) {};
            window.moveTo = function(x, y) {};
            window.resizeBy = function(x, y) {};
            window.resizeTo = function(x, y) {};
            // 'postMessage' and 'getSelection' are built with the rest of the DOM surface below,
            // where the message event and the selection range actually live.
            // HTML §8.3: the options dictionary is an optional argument, so the interface's own length
            // is one (measured), and the timeout it carries is the deadline the page asked to be run by.
            window.requestIdleCallback = function (cb) {
                var opts = arguments.length > 1 ? arguments[1] : null;
                var delay = opts && opts.timeout !== undefined ? Number(opts.timeout) || 0 : 50;
                return setTimeout(cb, delay);
            };
            window.cancelIdleCallback = function(id) { clearTimeout(id); };

            // HTML: 'queueMicrotask' schedules on the same job queue a resolved promise runs on, so
            // it is that promise spelled as a task — the callback lands before the next timer, after
            // the current script, exactly as the microtask checkpoint defines it.
            window.queueMicrotask = function(fn) {
                if (typeof fn !== 'function') throw new TypeError('queueMicrotask requires a function argument');
                Promise.resolve().then(function() { fn(); });
            };
            // HTML §2.9.4: a structured clone is a deep copy of the value graph. A reference the page
            // held twice stays one object in the copy, a cycle stays a cycle, and the kinds that carry
            // meaning keep their kind — a Date clones as a Date, a Map and a Set as themselves, an
            // ArrayBuffer and its views as the same bytes, a RegExp with its pattern and flags. What
            // cannot cross is refused with DataCloneError naming the value (measured: a function
            // answers 'function(){} could not be cloned.'), and undefined in, undefined out.
            function cloneUncloneable(value) {
                var label = typeof value === 'symbol' ? String(value) : String(value);
                return new DOMException('Failed to execute ' + String.fromCharCode(39)
                    + 'structuredClone' + String.fromCharCode(39) + ' on ' + String.fromCharCode(39)
                    + 'Window' + String.fromCharCode(39) + ': ' + label + ' could not be cloned.',
                    'DataCloneError');
            }
            function cloneInto(value, seen) {
                if (value === null) return null;
                var kind = typeof value;
                if (kind === 'function' || kind === 'symbol') throw cloneUncloneable(value);
                if (kind !== 'object') return value;
                if (seen.has(value)) return seen.get(value);
                var made;
                if (value instanceof Date) return copyInto(value, seen, new Date(value.getTime()));
                if (value instanceof RegExp) {
                    var re = new RegExp(value.source, value.flags === undefined ? '' : value.flags);
                    re.lastIndex = value.lastIndex || 0;
                    return copyInto(value, seen, re);
                }
                if (typeof ArrayBuffer !== 'undefined' && ArrayBuffer.isView && ArrayBuffer.isView(value)) {
                    var bytes = new value.constructor(value.length);
                    bytes.set(value);
                    return copyInto(value, seen, bytes);
                }
                if (typeof ArrayBuffer !== 'undefined' && value instanceof ArrayBuffer) {
                    return copyInto(value, seen, value.slice(0));
                }
                if (Array.isArray(value)) {
                    made = new Array(value.length);
                    seen.set(value, made);
                    for (var i = 0; i < value.length; i++) {
                        if (i in value) made[i] = cloneInto(value[i], seen);
                    }
                    return copyKeys(value, made, seen);
                }
                if (typeof Map === 'function' && value instanceof Map) {
                    made = new Map();
                    seen.set(value, made);
                    value.forEach(function (v, k) { made.set(cloneInto(k, seen), cloneInto(v, seen)); });
                    return copyKeys(value, made, seen);
                }
                if (typeof Set === 'function' && value instanceof Set) {
                    made = new Set();
                    seen.set(value, made);
                    value.forEach(function (v) { made.add(cloneInto(v, seen)); });
                    return copyKeys(value, made, seen);
                }
                // A blob is a value the page already holds bytes for, so the copy is the same object
                // the reference hands back for a kind its serializer knows (§2.9.4.16).
                if (typeof Blob !== 'undefined' && value instanceof Blob) return copyInto(value, seen, value);
                // The wrappers the engine hands out are not a plain graph the page built — the host
                // owns their state, and copying that state is not what a clone means. The reference
                // refuses them, and so does this (§2.9.4.18).
                // An object the engine handed out — a node, a collection, any host wrapper — is not a
                // graph the page owns, so it does not cross (measured: a function is refused with
                // DataCloneError, and the serializer has no rule for a host object either).
                var proto = Object.getPrototypeOf(value);
                if (proto && typeof proto.__domType === 'string') throw cloneUncloneable(value);
                made = proto === null ? Object.create(null) : {};
                seen.set(value, made);
                var keys = Object.keys(value);
                for (var j = 0; j < keys.length; j++) made[keys[j]] = cloneInto(value[keys[j]], seen);
                return copyKeys(value, made, seen);
            }
            function copyInto(original, seen, copy) { seen.set(original, copy); return copy; }
            function copyKeys(original, copy, seen) {
                // A clone carries the own properties the page wrote, keyed by string or by symbol — the
                // iterator and the well-known symbols belong to the type, not to the data, so they are
                // left where they are (§2.9.4.17).
                var names = Object.getOwnPropertyNames(original);
                for (var n = 0; n < names.length; n++) {
                    if (names[n] === 'length' && typeof original.length === 'number' && copy.length === original.length)
                        continue;
                    if (Object.prototype.propertyIsEnumerable.call(original, names[n]))
                        copy[names[n]] = cloneInto(original[names[n]], seen);
                }
                if (Object.getOwnPropertySymbols && Object.getOwnPropertySymbols(original).length) {
                    var symbols = Object.getOwnPropertySymbols(original);
                    for (var s = 0; s < symbols.length; s++) {
                        if (symbols[s] === Symbol.iterator) continue;
                        copy[symbols[s]] = cloneInto(original[symbols[s]], seen);
                    }
                }
                return copy;
            }
            window.structuredClone = function (value) {
                if (arguments.length < 1) {
                    throw new TypeError('Failed to execute ' + String.fromCharCode(39) + 'structuredClone'
                        + String.fromCharCode(39) + ' on ' + String.fromCharCode(39) + 'Window'
                        + String.fromCharCode(39) + ': 1 argument required, but only 0 present.');
                }
                if (value === undefined) return undefined;
                var seen = new Map();
                var options = arguments.length > 1 ? arguments[1] : null;
                if (options && (options.transfer !== undefined && options.transfer !== null
                    && options.transfer.length !== undefined && options.transfer.length > 0)) {
                    throw new DOMException('Failed to execute ' + String.fromCharCode(39) + 'structuredClone'
                        + String.fromCharCode(39) + ' on ' + String.fromCharCode(39) + 'Window'
                        + String.fromCharCode(39) + ': transfer is not supported by this engine.',
                        'NotSupportedError');
                }
                return cloneInto(value, seen);
            };

            // ── Performance (Performance API §3, High Resolution Time §4-§5,
            //    Navigation Timing §4, User Timing §3) ────────────────────────────
            // The clock a page measures with runs from the time origin on the platform's monotonic
            // tick, and a mark or a measure is an entry the buffer keeps until it is cleared:
            // 'getEntries' answers them in the order they started.
            // Every read of these interfaces lives on the prototype and the data behind it is a slot
            // only the interface's own accessors reach, so an entry owns no keys at all and the object
            // prints as the kind of entry it is (measured: a mark reads [object PerformanceMark], is an
            // instance of PerformanceEntry, lists no own keys, and a page that asks its JSON gets the
            // four numbers back). The same is true of the singleton: 'Object.keys(performance)' is
            // empty and 'toJSON' answers the time origin, the timing and the navigation.
            var perfWallOrigin = Date.now();
            var perfOriginClock = __acrux.monotonicNow();
            var perfBuffer = { mark: [], measure: [] };
            var PERF_ENTRY = new WeakMap();
            var PERFORMANCE_SLOTS = new WeakMap();
            function perfNow() { return __acrux.monotonicNow() - perfOriginClock; }

            function PerformanceEntry() {
                throw new TypeError('Failed to construct ' + formDataQuote('PerformanceEntry')
                    + ': Illegal constructor');
            }
            function entrySlots(entry, name) {
                var slots = PERF_ENTRY.get(entry);
                if (!slots) {
                    throw new TypeError('Failed to get ' + formDataQuote(name || 'name') + ' on '
                        + formDataQuote('PerformanceEntry') + ': Illegal invocation');
                }
                return slots;
            }
            ['name', 'entryType', 'startTime', 'duration'].forEach(function (field) {
                Object.defineProperty(PerformanceEntry.prototype, field, { configurable: true,
                    enumerable: false,
                    get: function () { return entrySlots(this, field)[field]; } });
            });
            Object.defineProperty(PerformanceEntry.prototype, 'toJSON', {
                configurable: true, writable: true, enumerable: false,
                value: function () {
                    var slots = entrySlots(this, 'toJSON');
                    return { name: slots.name, entryType: slots.entryType,
                        startTime: slots.startTime, duration: slots.duration };
                }
            });
            function newPerfEntry(proto, entryType, name, startTime, duration, detail) {
                var made = Object.create(proto);
                PERF_ENTRY.set(made, { name: String(name), entryType: entryType,
                    startTime: Number(startTime) || 0, duration: Number(duration) || 0,
                    detail: detail === undefined ? null : detail });
                return made;
            }
            // A mark the page constructs itself is the one entry type the interface lets it build; it
            // does not go into the buffer, which is why 'getEntriesByName' still answers nothing for it
            // (measured).
            function PerformanceMark(name, options) {
                if (arguments.length < 1) {
                    throw new TypeError('Failed to construct ' + formDataQuote('PerformanceMark')
                        + ': 1 argument required, but only ' + arguments.length + ' present.');
                }
                var read = options || { };
                return newPerfEntry(PerformanceMark.prototype, 'mark', name,
                    read.startTime === undefined ? perfNow() : Number(read.startTime) || 0, 0,
                    read.detail);
            }
            PerformanceMark.prototype = Object.create(PerformanceEntry.prototype);
            PerformanceMark.prototype.constructor = PerformanceMark;
            Object.defineProperty(PerformanceMark.prototype, 'detail', { configurable: true,
                enumerable: false,
                get: function () { return entrySlots(this, 'detail').detail; } });
            function PerformanceMeasure() {
                throw new TypeError('Failed to construct ' + formDataQuote('PerformanceMeasure')
                    + ': Illegal constructor');
            }
            PerformanceMeasure.prototype = Object.create(PerformanceEntry.prototype);
            PerformanceMeasure.prototype.constructor = PerformanceMeasure;

            // Navigation Timing §4: the legacy 'timing' and 'navigation' objects the reference still
            // answers. This engine stamps no navigation milestones, so every mark of the timing is the
            // same instant and the deprecated sentinel of 0 is what the interface reports; the
            // navigation is the one that brought the page in, with no redirects.
            var PERF_TIMING_FIELDS = ['navigationStart', 'unloadEventStart', 'unloadEventEnd',
                'redirectStart', 'redirectEnd', 'fetchStart', 'domainLookupStart', 'domainLookupEnd',
                'connectStart', 'connectEnd', 'secureConnectionStart', 'requestStart', 'responseStart',
                'responseEnd', 'domLoading', 'domInteractive', 'domContentLoadedEventStart',
                'domContentLoadedEventEnd', 'domComplete', 'loadEventStart', 'loadEventEnd'];
            function PerformanceTiming() {
                throw new TypeError('Failed to construct ' + formDataQuote('PerformanceTiming')
                    + ': Illegal constructor');
            }
            PERF_TIMING_FIELDS.forEach(function (field) {
                Object.defineProperty(PerformanceTiming.prototype, field, { configurable: true,
                    enumerable: false,
                    get: function () {
                        if (this !== performanceTiming) throw new TypeError('Illegal invocation');
                        return 0;
                    } });
            });
            Object.defineProperty(PerformanceTiming.prototype, 'toJSON', {
                configurable: true, writable: true, enumerable: false,
                value: function () {
                    var made = { };
                    PERF_TIMING_FIELDS.forEach(function (field) { made[field] = this[field]; });
                    return made;
                }
            });
            function PerformanceNavigation() {
                throw new TypeError('Failed to construct ' + formDataQuote('PerformanceNavigation')
                    + ': Illegal constructor');
            }
            Object.defineProperties(PerformanceNavigation.prototype, {
                type: { configurable: true, enumerable: false, get: function () { return 0; } },
                redirectCount: { configurable: true, enumerable: false, get: function () { return 0; } },
                toJSON: { configurable: true, writable: true, enumerable: false,
                    value: function () { return { type: this.type, redirectCount: this.redirectCount }; } }
            });
            [['TYPE_NAVIGATE', 0], ['TYPE_RELOAD', 1], ['TYPE_BACK_FORWARD', 2],
             ['TYPE_RESERVED', 255]].forEach(function (pair) {
                Object.defineProperty(PerformanceNavigation.prototype, pair[0],
                    { value: pair[1], configurable: true, enumerable: false });
                Object.defineProperty(PerformanceNavigation, pair[0],
                    { value: pair[1], configurable: true, enumerable: false });
            });
            // The heap read is the collector's own: the engine is the only side that knows how many
            // bytes it is holding.
            function MemoryInfo() {
                throw new TypeError('Failed to construct ' + formDataQuote('MemoryInfo')
                    + ': Illegal constructor');
            }
            ['totalJSHeapSize', 'usedJSHeapSize', 'jsHeapSizeLimit'].forEach(function (field, index) {
                Object.defineProperty(MemoryInfo.prototype, field, { configurable: true,
                    enumerable: false,
                    get: function () { return heapInfo()[index]; } });
            });
            var heapCache = { at: 0, values: [0, 0, 0] };
            function heapInfo() {
                var now = perfNow();
                if (now - heapCache.at > 50 || heapCache.at === 0) {
                    heapCache.at = now;
                    try { heapCache.values = __acrux.memoryInfo(); } catch (e) { heapCache.values = [0, 0, 0]; }
                }
                return heapCache.values;
            }
            function EventCounts() {
                throw new TypeError('Failed to construct ' + formDataQuote('EventCounts')
                    + ': Illegal constructor');
            }
            function performanceBrand(receiver, name) {
                if (!PERFORMANCE_SLOTS.get(receiver)) {
                    throw new TypeError('Failed to execute ' + formDataQuote(name) + ' on '
                        + formDataQuote('Performance') + ': Illegal invocation');
                }
            }
            function perfSorted(type) {
                var list = type === undefined ? perfBuffer.mark.concat(perfBuffer.measure)
                    : (perfBuffer[type] || []).slice();
                return list.sort(function (a, b) { return a.startTime - b.startTime; });
            }
            function perfMarkStart(name) {
                var found = perfBuffer.mark.concat(perfBuffer.measure);
                for (var i = 0; i < found.length; i++) if (found[i].name === String(name)) return found[i].startTime;
                return undefined;
            }
            var performanceTiming = Object.create(PerformanceTiming.prototype);
            var performanceNavigation = Object.create(PerformanceNavigation.prototype);
            var performanceMemory = Object.create(MemoryInfo.prototype);
            var performanceEventCounts = Object.create(EventCounts.prototype);
            var performanceResourceBuffer = { size: Infinity, full: null };

            function Performance() {
                throw new TypeError('Failed to construct ' + formDataQuote('Performance')
                    + ': Illegal constructor');
            }
            Object.defineProperties(Performance.prototype, {
                timeOrigin: { configurable: true, enumerable: false,
                    get: function () { performanceBrand(this, 'timeOrigin'); return perfWallOrigin; } },
                now: { configurable: true, writable: true, enumerable: false,
                    value: function () { performanceBrand(this, 'now'); return perfNow(); } },
                timing: { configurable: true, enumerable: false,
                    get: function () { performanceBrand(this, 'timing'); return performanceTiming; } },
                navigation: { configurable: true, enumerable: false,
                    get: function () { performanceBrand(this, 'navigation'); return performanceNavigation; } },
                memory: { configurable: true, enumerable: false,
                    get: function () { performanceBrand(this, 'memory'); return performanceMemory; } },
                eventCounts: { configurable: true, enumerable: false,
                    get: function () { performanceBrand(this, 'eventCounts'); return performanceEventCounts; } },
                interactionCount: { configurable: true, enumerable: false,
                    get: function () { performanceBrand(this, 'interactionCount'); return 0; } },
                onresourcetimingbufferfull: { configurable: true, enumerable: false,
                    get: function () { performanceBrand(this, 'onresourcetimingbufferfull');
                        return performanceResourceBuffer.full; },
                    set: function (handler) {
                        performanceBrand(this, 'onresourcetimingbufferfull');
                        performanceResourceBuffer.full = typeof handler === 'function' ? handler : null;
                    } },
                mark: { configurable: true, writable: true, enumerable: false,
                    value: function (name) {
                        performanceBrand(this, 'mark');
                        if (arguments.length < 1) {
                            throw new TypeError('Failed to execute ' + formDataQuote('mark') + ' on '
                                + formDataQuote('Performance') + ': 1 argument required, but only '
                                + arguments.length + ' present.');
                        }
                        var read = arguments.length > 1 ? (arguments[1] || { }) : { };
                        var made = newPerfEntry(PerformanceMark.prototype, 'mark', name, this.now(), 0,
                            read.detail);
                        perfBuffer.mark.push(made);
                        return made;
                    } },
                measure: { configurable: true, writable: true, enumerable: false,
                    value: function (name, startOrOptions, end) {
                        performanceBrand(this, 'measure');
                        var start = 0, duration = 0, detail = null;
                        if (startOrOptions && typeof startOrOptions === 'object') {
                            start = startOrOptions.start === undefined
                                ? (perfMarkStart(startOrOptions.startMark) || 0)
                                : Number(startOrOptions.start) || 0;
                            duration = startOrOptions.duration === undefined
                                ? (startOrOptions.end === undefined ? 0
                                    : (Number(startOrOptions.end) || 0) - start)
                                : Number(startOrOptions.duration) || 0;
                            detail = startOrOptions.detail === undefined ? null : startOrOptions.detail;
                        } else if (startOrOptions === undefined) {
                            start = 0;
                        } else {
                            var from = perfMarkStart(startOrOptions);
                            var to = perfMarkStart(end);
                            start = from === undefined ? this.now() : from;
                            duration = (to === undefined ? this.now() : to) - start;
                        }
                        var made = newPerfEntry(PerformanceMeasure.prototype, 'measure',
                            name === undefined ? '' : name, start, duration, detail);
                        perfBuffer.measure.push(made);
                        return made;
                    } },
                clearMarks: { configurable: true, writable: true, enumerable: false,
                    value: function (name) {
                        performanceBrand(this, 'clearMarks');
                        if (name === undefined) { perfBuffer.mark = []; return; }
                        perfBuffer.mark = perfBuffer.mark.filter(function (e) {
                            return e.name !== String(name); });
                    } },
                clearMeasures: { configurable: true, writable: true, enumerable: false,
                    value: function (name) {
                        performanceBrand(this, 'clearMeasures');
                        if (name === undefined) { perfBuffer.measure = []; return; }
                        perfBuffer.measure = perfBuffer.measure.filter(function (e) {
                            return e.name !== String(name); });
                    } },
                getEntries: { configurable: true, writable: true, enumerable: false,
                    value: function () { performanceBrand(this, 'getEntries'); return perfSorted(); } },
                getEntriesByType: { configurable: true, writable: true, enumerable: false,
                    value: function (type) {
                        performanceBrand(this, 'getEntriesByType'); return perfSorted(String(type));
                    } },
                getEntriesByName: { configurable: true, writable: true, enumerable: false,
                    value: function (name) {
                        performanceBrand(this, 'getEntriesByName');
                        return perfSorted().filter(function (e) { return e.name === String(name); });
                    } },
                clearResourceTimings: { configurable: true, writable: true, enumerable: false,
                    value: function () { performanceBrand(this, 'clearResourceTimings'); } },
                setResourceTimingBufferSize: { configurable: true, writable: true, enumerable: false,
                    value: function (size) {
                        performanceBrand(this, 'setResourceTimingBufferSize');
                        performanceResourceBuffer.size = Number(size) || 0;
                    } },
                toJSON: { configurable: true, writable: true, enumerable: false,
                    value: function () {
                        performanceBrand(this, 'toJSON');
                        // The three the reference names, in its own order: the entries are not part of
                        // this serialisation even though the buffer is read through other members.
                        return { timeOrigin: this.timeOrigin, timing: this.timing.toJSON(),
                            navigation: this.navigation.toJSON() };
                    } }
            });
            [Performance, PerformanceEntry, PerformanceMark, PerformanceMeasure, PerformanceTiming,
             PerformanceNavigation, MemoryInfo, EventCounts].forEach(function (ctor) {
                Object.defineProperty(ctor.prototype, Symbol.toStringTag,
                    { value: ctor.name, configurable: true, enumerable: false });
            });
            // Performance Timeline §3.6: the records handed to an observer callback are a list of the
            // same entry objects, read back through the three members the interface names — the page
            // gets the entries themselves, not a copy whose properties it cannot read (measured: an
            // observer watching marks answers their names and types).
            var PERF_LIST_SLOTS = new WeakMap();
            function PerformanceObserverEntryList() {
                throw new TypeError('Failed to construct ' + formDataQuote('PerformanceObserverEntryList')
                    + ': Illegal constructor');
            }
            function entryListRecords(list, name) {
                var records = PERF_LIST_SLOTS.get(list);
                if (records === undefined) {
                    throw new TypeError('Failed to execute ' + formDataQuote(name) + ' on '
                        + formDataQuote('PerformanceObserverEntryList') + ': Illegal invocation');
                }
                return records;
            }
            Object.defineProperties(PerformanceObserverEntryList.prototype, {
                getEntries: { configurable: true, writable: true, enumerable: false,
                    value: function () { return entryListRecords(this, 'getEntries').slice(); } },
                getEntriesByType: { configurable: true, writable: true, enumerable: false,
                    value: function (type) {
                        return entryListRecords(this, 'getEntriesByType').filter(function (entry) {
                            return entry.entryType === String(type); });
                    } },
                getEntriesByName: { configurable: true, writable: true, enumerable: false,
                    value: function (name) {
                        return entryListRecords(this, 'getEntriesByName').filter(function (entry) {
                            return entry.name === String(name); });
                    } }
            });
            Object.defineProperty(PerformanceObserverEntryList.prototype, Symbol.toStringTag,
                { value: 'PerformanceObserverEntryList', configurable: true, enumerable: false });
            function newPerfEntryList(records) {
                var made = Object.create(PerformanceObserverEntryList.prototype);
                PERF_LIST_SLOTS.set(made, records);
                return made;
            }
            window.PerformanceObserverEntryList = PerformanceObserverEntryList;
            var performance = Object.create(Performance.prototype);
            PERFORMANCE_SLOTS.set(performance, { });
            window.performance = performance;
            window.Performance = Performance;
            window.PerformanceEntry = PerformanceEntry;
            window.PerformanceMark = PerformanceMark;
            window.PerformanceMeasure = PerformanceMeasure;
            window.PerformanceTiming = PerformanceTiming;
            window.PerformanceNavigation = PerformanceNavigation;
            window.MemoryInfo = MemoryInfo;
            window.EventCounts = EventCounts;


            // HTML §6.1.5.2 — the 'on*' IDL event handler attributes. Assigning el.onclick = fn
            // is one slot, not a stacking listener, and the assignment has to reach dispatch. The
            // DOM wrappers are host objects whose prototype the engine already rewrote to a plain
            // JS object, so the handlers are installed as accessors on those prototypes: the setter
            // registers the function through addEventListener (the path dispatch actually walks)
            // and remembers it, the getter reads it back, and re-assignment releases the old one.
            (function () {
                if (typeof WeakMap !== 'function' || typeof Object.defineProperty !== 'function') return;
                var store = new WeakMap();
                var evs = ['click','dblclick','mousedown','mouseup','mousemove','mouseover',
                    'mouseout','mouseenter','mouseleave','keydown','keyup','keypress','input',
                    'change','submit','focus','blur','load','error','scroll','wheel',
                    'touchstart','touchend','touchmove','contextmenu'];
                function defOn(proto) {
                    if (!proto) return;
                    evs.forEach(function (ev) {
                        var prop = 'on' + ev;
                        if (Object.prototype.hasOwnProperty.call(proto, prop)) return;
                        try {
                            Object.defineProperty(proto, prop, {
                                configurable: true, enumerable: true,
                                get: function () { var m = store.get(this); return (m && m[prop]) || null; },
                                set: function (fn) {
                                    var m = store.get(this);
                                    if (!m) { m = {}; store.set(this, m); }
                                    var old = m[prop];
                                    if (typeof old === 'function') { try { this.removeEventListener(ev, old); } catch (e) {} }
                                    var next = (typeof fn === 'function') ? fn : null;
                                    m[prop] = next;
                                    if (next) { try { this.addEventListener(ev, next); } catch (e) {} }
                                }
                            });
                        } catch (e) {}
                    });
                }
                var P = window.__domProto;
                // Only the interfaces that can carry a handler: an element, a document, a fragment or
                // the window. The loop used to walk every level there is, which wrote 'onclick' onto a
                // '@media' rule as readily as onto a button — and the interface's own key list the
                // reference prints has no handler on it at all (measured).
                function handlerLevel(name) {
                    return /^(HTML|SVG|MathML)[A-Za-z]*Element$/.test(name) || name === 'Element'
                        || name === 'HTMLElement' || name === 'Document' || name === 'HTMLDocument'
                        || name === 'XMLDocument' || name === 'DocumentFragment'
                        || name === 'ShadowRoot' || name === 'Node' || name === 'Window';
                }
                if (P) { for (var k in P) if (handlerLevel(k)) defOn(P[k]); }
                defOn(window.HTMLElement && window.HTMLElement.prototype);
                defOn(window.Element && window.Element.prototype);
                // WindowEventHandlers (HTML §6.7.1): window.onload / onerror / onscroll and friends
                // are the same one-slot accessors, installed on the global itself.
                defOn(window);
            })();

            // HTML §6.1.4.3 — the 'dataset' DOMStringMap. It is a live view over the element's
            // 'data-*' attributes where a camelCase key stands for the hyphenated attribute, and a
            // Proxy is the only shape that makes read, write and 'delete el.dataset.x' all reach
            // the attribute store. The accessor lives on the element prototypes so it answers before
            // the host's own member, and the Proxy is cached per element (dataset is identity-stable).
            (function () {
                if (typeof Proxy !== 'function' || typeof WeakMap !== 'function' || typeof Object.defineProperty !== 'function') return;
                var store = new WeakMap();
                function dash(k) { return String(k).replace(/[A-Z]/g, function (c) { return '-' + c.toLowerCase(); }); }
                function camel(k) { return String(k).replace(/-([a-zA-Z])/g, function (m, c) { return c.toUpperCase(); }); }
                function attrName(k) { return 'data-' + dash(k); }
                // HTML §6.1.4.3: the map is only written through names that could be properties of
                // it, and a name with a hyphen in it is not one — the reference refuses the write
                // with a 'SyntaxError' that quotes the name (measured).
                function isValidPropName(k) {
                    return /^(?:[A-Za-z_$]|[^\x00-\x7F])(?:[A-Za-z0-9_$]|[^\x00-\x7F])*$/.test(String(k));
                }
                function refusedName(k) {
                    return new SyntaxError('Failed to set a named property ' + chr39(String(k))
                        + ' on ' + chr39('DOMStringMap') + ': ' + chr39(String(k))
                        + ' is not a valid property name.');
                }
                function chr39(text) { return chr(39) + String(text) + chr(39); }
                function chr(code) { return String.fromCharCode(code); }
                function dataKeys(el) {
                    // The attributes are the store, so the walk lists the ones that are data
                    // attributes and gives each its camelCase spelling.
                    var names = el.getAttributeNames ? el.getAttributeNames() : [];
                    var out = [];
                    for (var i = 0; i < names.length; i++) {
                        var name = String(names[i]);
                        if (name.length > 5 && name.slice(0, 5) === 'data-') out.push(camel(name.slice(5)));
                    }
                    return out;
                }
                function makeDataset(el) {
                    var existing = store.get(el);
                    if (existing) return existing;
                    var target = (window.__domProto && window.__domProto['DOMStringMap'])
                        ? Object.create(window.__domProto['DOMStringMap']) : {};
                    var proxy = new Proxy(target, {
                        get: function (t, p) {
                            if (typeof p !== 'string') return t[p];
                            if (p === 'toString') return function () { return ''; };
                            // A name that could not have been written on the map was never read from it
                            // either: the hyphenated spelling belongs to the attribute, not to the map
                            // (measured: 'dataset['a-b']' is undefined while 'dataset.aB' is the value).
                            if (!isValidPropName(p)) return undefined;
                            var v = el.getAttribute(attrName(p));
                            return v === null ? undefined : v;
                        },
                        set: function (t, p, v) {
                            if (typeof p !== 'string') return false;
                            if (!isValidPropName(p)) throw refusedName(p);
                            el.setAttribute(attrName(p), (v === undefined || v === null) ? '' : String(v));
                            return true;
                        },
                        deleteProperty: function (t, p) {
                            if (typeof p !== 'string') return false;
                            el.removeAttribute(attrName(p));
                            return true;
                        },
                        has: function (t, p) {
                            if (typeof p !== 'string') return p in t;
                            if (!isValidPropName(p)) return false;
                            return el.getAttribute(attrName(p)) !== null;
                        },
                        ownKeys: function () { return dataKeys(el); },
                        getOwnPropertyDescriptor: function (t, p) {
                            if (typeof p !== 'string') return Reflect.getOwnPropertyDescriptor(t, p);
                            var v = el.getAttribute(attrName(p));
                            if (v === null) return undefined;
                            // Measured: a data attribute reads as an own, enumerable, writable and
                            // configurable property of the map, which is what makes 'Object.keys',
                            // JSON.stringify and the spread operator all see it.
                            return { value: v, writable: true, enumerable: true, configurable: true };
                        }
                    });
                    store.set(el, proxy);
                    return proxy;
                }
                function defDataset(proto) {
                    if (!proto) return;
                    if (Object.prototype.hasOwnProperty.call(proto, 'dataset')) return;
                    try {
                        Object.defineProperty(proto, 'dataset', {
                            configurable: true, enumerable: false,
                            get: function () { return makeDataset(this); },
                            set: function () { }
                        });
                    } catch (e) { }
                }
                var P = window.__domProto;
                if (P) { defDataset(P['HTMLElement']); defDataset(P['Element']); defDataset(P['Node']); }
                defDataset(window.HTMLElement && window.HTMLElement.prototype);
            })();

            // HTML: document.defaultView is the Window that owns the document. An own-property write
            // onto the document host does not stick across the interop, so it is an accessor on the
            // document prototype instead — which the host's [[Get]] resolves — returning the live global
            // for the document the window is showing and nothing at all for one a script built (DOM
            // Parsing §3 and HTML §7.3.2: 'parseFromString' gives a document with no browsing context,
            // and the reference answers 'null' for its defaultView — measured).
            (function () {
                var proto = (window.__domProto && window.__domProto['HTMLDocument'])
                    || (window.HTMLDocument && window.HTMLDocument.prototype);
                if (!proto || typeof Object.defineProperty !== 'function') return;
                if (Object.prototype.hasOwnProperty.call(proto, 'defaultView')) return;
                try {
                    Object.defineProperty(proto, 'defaultView', {
                        configurable: true, enumerable: false,
                        get: function () { return this === document ? window : null; }
                    });
                } catch (e) { }
            })();


            // Descriptor factory for the handful of window attributes that are plain
            // writable strings (HTML: name, status, defaultStatus). The setter has to
            // keep the value: 'var name' at global scope does not create a variable,
            // it binds to window.name, so a no-op setter silently loses the value.
            function __domStringAttr(initial) {
                var stored = String(initial);
                return {
                    configurable: true,
                    get: function() { return stored; },
                    set: function(v) { stored = String(v); }
                };
            }

            Object.defineProperty(window, 'closed', { configurable: true, get: function() { return false; } });
            Object.defineProperty(window, 'name', __domStringAttr(''));
            Object.defineProperty(window, 'opener', { configurable: true, get: function() { return null; } });
            Object.defineProperty(window, 'parent', { configurable: true, get: function() { return window; } });
            window.self = window;
            Object.defineProperty(window, 'top', { configurable: true, get: function() { return window; } });
            Object.defineProperty(window, 'frames', { configurable: true, get: function() { return window; } });
            Object.defineProperty(window, 'length', { configurable: true, get: function() { return 0; } });
            Object.defineProperty(window, 'status', __domStringAttr(''));

            Object.defineProperty(window, 'screenLeft', { configurable: true, get: function() { return 0; } });
            Object.defineProperty(window, 'screenTop', { configurable: true, get: function() { return 0; } });
            Object.defineProperty(window, 'screenX', { configurable: true, get: function() { return 0; } });
            Object.defineProperty(window, 'screenY', { configurable: true, get: function() { return 0; } });

            // DOM 4 §5.1.1 — 'new Event(type, init)'. The host keeps the event object
            // (its type/bubbles/cancelable/composed and the preventDefault/stopPropagation
            // behaviour dispatch reads), so the constructor builds through createEvent and then
            // writes the init dictionary over the defaults. A dispatched event reports the same
            // fields a page switched on, and the flags default to false exactly as the interface
            // defines them (the older createEvent path left 'bubbles'/'cancelable' true).
            function Event(type, init) {
                var evt = document.createEvent('Event');
                evt.type = String(type === undefined ? '' : type);
                evt.bubbles = !!(init && init.bubbles);
                evt.cancelable = !!(init && init.cancelable);
                evt.composed = !!(init && init.composed);
                return evt;
            }

            function CustomEvent(type, init) {
                var evt = document.createEvent('customevent');
                evt.type = String(type === undefined ? '' : type);
                evt.bubbles = !!(init && init.bubbles);
                evt.cancelable = !!(init && init.cancelable);
                evt.composed = !!(init && init.composed);
                evt.detail = (init && init.detail !== undefined) ? init.detail : null;
                return evt;
            }

            function MouseEvent(type, init) {
                var evt = document.createEvent('mouseevent');
                evt.type = type;
                if (init) {
                    if (init.bubbles !== undefined) evt.bubbles = init.bubbles;
                    if (init.cancelable !== undefined) evt.cancelable = init.cancelable;
                    if (init.detail !== undefined) evt.detail = init.detail;
                    if (init.clientX !== undefined) evt.clientX = init.clientX;
                    if (init.clientY !== undefined) evt.clientY = init.clientY;
                    if (init.screenX !== undefined) evt.screenX = init.screenX;
                    if (init.screenY !== undefined) evt.screenY = init.screenY;
                    if (init.button !== undefined) evt.button = init.button;
                    if (init.ctrlKey !== undefined) evt.ctrlKey = init.ctrlKey;
                    if (init.shiftKey !== undefined) evt.shiftKey = init.shiftKey;
                    if (init.altKey !== undefined) evt.altKey = init.altKey;
                    if (init.metaKey !== undefined) evt.metaKey = init.metaKey;
                }
                return evt;
            }

            if (typeof Promise !== 'undefined') {
                if (!Promise.allSettled) {
                    Promise.allSettled = function(promises) {
                        return Promise.all(promises.map(function(p) {
                            return Promise.resolve(p).then(
                                function(v) { return { status: 'fulfilled', value: v }; },
                                function(e) { return { status: 'rejected', reason: e }; }
                            );
                        }));
                    };
                }
                if (!Promise.any) {
                    Promise.any = function(promises) {
                        return new Promise(function(resolve, reject) {
                            var errors = [];
                            var count = 0;
                            promises.forEach(function(p, i) {
                                Promise.resolve(p).then(resolve, function(e) {
                                    errors[i] = e;
                                    count++;
                                    if (count === promises.length) reject(new Error('All promises rejected'));
                                });
                            });
                        });
                    };
                }
            }

            // ── DOMException (WebIDL §2.9) ───────────────────────────────────────
            // The object a specification names for a refused call. A page tests the name, so the
            // exception is built on the one prototype every engine already unwinds through catch.
            function DOMException(message, name) {
                var err = new Error(message || '');
                err.name = name || 'Error';
                err.message = message || '';
                err.code = domExceptionLegacyCode(err.name);
                return err;
            }
            DOMException.prototype = Error.prototype;

            function domExceptionLegacyCode(name) {
                // The numeric codes legacy pages still read; 0 says none of the old ones.
                switch (name) {
                    case 'IndexSizeError': return 1;
                    case 'HierarchyRequestError': return 3;
                    case 'WrongDocumentError': return 4;
                    case 'InvalidCharacterError': return 5;
                    case 'NoModificationAllowedError': return 7;
                    case 'NotFoundError': return 8;
                    case 'NotSupportedError': return 9;
                    case 'TypeMismatchError': return 17;
                    case 'SecurityError': return 18;
                    case 'NetworkError': return 19;
                    case 'AbortError': return 20;
                    case 'TimeoutError': return 23;
                    case 'InvalidStateError': return 11;
                    case 'SyntaxError': return 12;
                    case 'InvalidModificationError': return 13;
                    default: return 0;
                }
            }

            // ── MutationObserver (DOM §4.5) ──────────────────────────────────────
            // The engine keeps no per-node mutation history, so records are made by comparing the
            // shape of each observed subtree against the shape it had at the last look: added and
            // removed nodes, the attribute that changed and the value it had, the text that was
            // retyped. takeRecords() answers them on the spot; the checkpoint delivers the same
            // list to the callback, in observer creation order, the way §4.5.6.1 puts it.
            var MutationObserver = function MutationObserver(callback) {
                if (typeof callback !== 'function') {
                    throw new TypeError('parameter 1 is not of type function');
                }
                this._callback = callback;
            };

            (function () {
                var observers = [];
                var flushing = false;

                // DOM §4.5.7: the old value a characterData record carries is the data the node held
                // before that one edit, and an edit that happens between two checkpoints leaves no
                // trace in a snapshot comparison — the node may not have been in the tree yet at
                // the last look. So the host records each text mutation as it happens and the
                // observer drains those entries here, one record per mutation, in order.
                var pendingCharData = new Map();
                var charGeneration = 0;

                function collectCharData() {
                    if (typeof __acrux === 'undefined'
                        || typeof __acrux.takeCharacterDataChanges !== 'function') return;
                    var list;
                    try { list = __acrux.takeCharacterDataChanges(); } catch (e) { return; }
                    if (!list || !list.length) return;
                    charGeneration++;
                    for (var i = 0; i < list.length; i++) {
                        var pair = list[i];
                        var node = pair && pair[0];
                        if (!node) continue;
                        var edits = pendingCharData.get(node);
                        if (!edits) { edits = []; pendingCharData.set(node, edits); }
                        edits.push({ generation: charGeneration, oldValue: String(pair[1]) });
                    }
                }

                // A registration only hears about edits made after it was put in place, and an
                // entry no live registration can still reach is dropped rather than kept alive.
                function pruneCharData() {
                    var lowest = charGeneration + 1;
                    for (var i = 0; i < observers.length; i++) {
                        if (observers[i].generation < lowest) lowest = observers[i].generation;
                    }
                    var keys = [];
                    pendingCharData.forEach(function (edits, node) { keys.push([node, edits]); });
                    for (var k = 0; k < keys.length; k++) {
                        var kept = [];
                        for (var e = 0; e < keys[k][1].length; e++) {
                            if (keys[k][1][e].generation >= lowest) kept.push(keys[k][1][e]);
                        }
                        if (kept.length) pendingCharData.set(keys[k][0], kept);
                        else pendingCharData.delete(keys[k][0]);
                    }
                }

                function syncCharDataTracking() {
                    if (typeof __acrux === 'undefined'
                        || typeof __acrux.setCharacterDataTracking !== 'function') return;
                    var wanted = false;
                    for (var i = 0; i < observers.length; i++) {
                        if (observers[i].options.characterData) { wanted = true; break; }
                    }
                    __acrux.setCharacterDataTracking(wanted);
                }

                function charDataRecords(reg, node, oldValue) {
                    var out = [];
                    var edits = pendingCharData.get(node);
                    if (edits) {
                        for (var i = 0; i < edits.length; i++) {
                            if (edits[i].generation < reg.generation) continue;
                            out.push(makeCharDataRecord(reg, node,
                                reg.options.characterDataOldValue ? edits[i].oldValue : null));
                        }
                    }
                    if (out.length === 0 && oldValue !== null) {
                        out.push(makeCharDataRecord(reg, node,
                            reg.options.characterDataOldValue ? oldValue : null));
                    }
                    return out;
                }

                function makeCharDataRecord(reg, node, oldValue) {
                    return {
                        type: 'characterData',
                        target: node,
                        addedNodes: [],
                        removedNodes: [],
                        previousSibling: null,
                        nextSibling: null,
                        attributeName: null,
                        namespaceURI: null,
                        oldValue: oldValue
                    };
                }


                function childNodesOf(node) {
                    var out = [];
                    if (!node || !node.childNodes) return out;
                    var list = node.childNodes;
                    for (var i = 0; i < list.length; i++) {
                        var child = list[i];
                        if (child) out.push(child);
                    }
                    return out;
                }

                function attributesOf(node) {
                    var out = [];
                    if (!node || typeof node.getAttributeNames !== 'function') return out;
                    var names = node.getAttributeNames();
                    for (var i = 0; i < names.length; i++) {
                        out.push([names[i], node.getAttribute(names[i])]);
                    }
                    return out;
                }

                function dataOf(node) {
                    if (!node) return null;
                    return (node.nodeType === 3 || node.nodeType === 8) ? String(node.data) : null;
                }

                function buildSnapshot(node, parentEntry, depth) {
                    var entry = {
                        node: node,
                        parent: parentEntry ? parentEntry.node : (node.parentNode || null),
                        attrs: attributesOf(node),
                        data: dataOf(node),
                        kids: []
                    };
                    if (parentEntry) parentEntry.kids.push(entry);
                    if (depth > 1) {
                        var children = childNodesOf(node);
                        for (var i = 0; i < children.length; i++) {
                            buildSnapshot(children[i], entry, depth - 1);
                        }
                    }
                    return entry;
                }

                function flatten(entry, into) {
                    into.push(entry);
                    for (var i = 0; i < entry.kids.length; i++) flatten(entry.kids[i], into);
                    return into;
                }

                function snapshot(reg) {
                    var depth = reg.options.subtree ? 1e6 : 1;
                    return flatten(buildSnapshot(reg.target, null, depth), []);
                }

                function entriesByNode(list) {
                    var map = new Map();
                    for (var i = 0; i < list.length; i++) map.set(list[i].node, list[i]);
                    return map;
                }

                function nodesOf(entries) {
                    var out = [];
                    for (var i = 0; i < entries.length; i++) out.push(entries[i].node);
                    return out;
                }

                function without(list, other) {
                    var out = [];
                    for (var i = 0; i < list.length; i++) {
                        if (other.indexOf(list[i]) < 0) out.push(list[i]);
                    }
                    return out;
                }

                function attrValue(pairs, name) {
                    for (var i = 0; i < pairs.length; i++) {
                        if (pairs[i][0] === name) return pairs[i][1];
                    }
                    return null;
                }

                function siblings(list, node) {
                    var index = list.indexOf(node);
                    return {
                        previous: index > 0 ? list[index - 1] : null,
                        next: index >= 0 && index < list.length - 1 ? list[index + 1] : null
                    };
                }

                function diff(reg) {
                    var fresh = snapshot(reg);
                    var oldByNode = entriesByNode(reg.snapshot);
                    var newByNode = entriesByNode(fresh);
                    var records = [];

                    var parents = [];
                    for (var i = 0; i < fresh.length; i++) {
                        if (parents.indexOf(fresh[i].node) < 0) parents.push(fresh[i].node);
                    }
                    for (var j = 0; j < reg.snapshot.length; j++) {
                        if (parents.indexOf(reg.snapshot[j].node) < 0) parents.push(reg.snapshot[j].node);
                    }

                    for (var p = 0; p < parents.length; p++) {
                        var node = parents[p];
                        var oldEntry = oldByNode.get(node) || { kids: [], attrs: [], data: null };
                        var newEntry = newByNode.get(node) || { kids: [], attrs: [], data: null };
                        var oldKids = nodesOf(oldEntry.kids);
                        var newKids = nodesOf(newEntry.kids);
                        var added = without(newKids, oldKids);
                        var removed = without(oldKids, newKids);

                        if (reg.options.childList && (added.length || removed.length)) {
                            var around = added.length ? siblings(newKids, added[0]) : siblings(oldKids, removed[0]);
                            records.push({
                                type: 'childList',
                                target: node,
                                addedNodes: added,
                                removedNodes: removed,
                                previousSibling: around.previous,
                                nextSibling: around.next,
                                attributeName: null,
                                namespaceURI: null,
                                oldValue: null
                            });
                        }

                        // Only a node that was in the last snapshot has an old side to compare;
                        // one that just appeared has no change of its own to report.
                        var comparable = oldByNode.has(node) && newByNode.has(node);

                        if (reg.options.attributes && node.nodeType === 1 && comparable) {
                            var names = [];
                            for (var a = 0; a < oldEntry.attrs.length; a++) {
                                if (names.indexOf(oldEntry.attrs[a][0]) < 0) names.push(oldEntry.attrs[a][0]);
                            }
                            for (var b = 0; b < newEntry.attrs.length; b++) {
                                if (names.indexOf(newEntry.attrs[b][0]) < 0) names.push(newEntry.attrs[b][0]);
                            }
                            for (var n = 0; n < names.length; n++) {
                                var name = names[n];
                                if (reg.options.attributeFilter && reg.options.attributeFilter.indexOf(name) < 0) continue;
                                var before = attrValue(oldEntry.attrs, name);
                                var after = attrValue(newEntry.attrs, name);
                                if (before === after) continue;
                                records.push({
                                    type: 'attributes',
                                    target: node,
                                    addedNodes: [],
                                    removedNodes: [],
                                    previousSibling: null,
                                    nextSibling: null,
                                    attributeName: name,
                                    namespaceURI: null,
                                    oldValue: reg.options.attributeOldValue ? before : null
                                });
                            }
                        }

                        if (reg.options.characterData && (node.nodeType === 3 || node.nodeType === 8)) {
                            var edits = charDataRecords(reg, node,
                                comparable && oldEntry.data !== newEntry.data ? oldEntry.data : null);
                            for (var c = 0; c < edits.length; c++) records.push(edits[c]);
                        }
                    }

                    reg.snapshot = fresh;
                    return records;
                }

                function deliver(registry) {
                    collectCharData();
                    var buckets = [];
                    for (var i = 0; i < observers.length; i++) {
                        var reg = observers[i];
                        if (registry && reg.observer !== registry) continue;
                        var made = diff(reg);
                        if (!made.length) continue;
                        var bucket = null;
                        for (var j = 0; j < buckets.length; j++) {
                            if (buckets[j].observer === reg.observer) bucket = buckets[j];
                        }
                        if (!bucket) {
                            bucket = { observer: reg.observer, records: [] };
                            buckets.push(bucket);
                        }
                        for (var k = 0; k < made.length; k++) bucket.records.push(made[k]);
                    }
                    for (var b = 0; b < buckets.length; b++) {
                        try { buckets[b].observer._callback(buckets[b].records, buckets[b].observer); }
                        catch (e) { }
                    }
                    return buckets.length > 0;
                }

                window.__mutationFlush = function () {
                    if (flushing) return;
                    flushing = true;
                    try {
                        // A callback that itself mutates the tree queues another round; the
                        // checkpoint runs until the tree settles, the way the spec loop does.
                        for (var pass = 0; pass < 8; pass++) {
                            if (!deliver(null)) break;
                        }
                        pruneCharData();
                    } finally {
                        flushing = false;
                    }
                };

                MutationObserver.prototype.observe = function (target, options) {
                    if (!target || !target.nodeType) {
                        throw new TypeError('parameter 1 is not of type Node');
                    }
                    options = options || {};
                    var hasFilter = options.attributeFilter !== undefined && options.attributeFilter !== null;
                    var childList = !!options.childList;
                    var attributes = !!options.attributes || hasFilter;
                    var characterData = !!options.characterData;
                    if (!childList && !attributes && !characterData) {
                        throw new TypeError('At least one of childList, attributes, characterData must be true');
                    }
                    var filter = hasFilter ? Array.prototype.slice.call(options.attributeFilter) : null;
                    // Everything the host recorded up to now belongs to the registrations that were
                    // already in place; this one starts from the tree as it stands.
                    collectCharData();
                    var reg = {
                        observer: this,
                        target: target,
                        generation: charGeneration + 1,
                        options: {
                            childList: childList,
                            attributes: attributes,
                            characterData: characterData,
                            subtree: !!options.subtree,
                            attributeOldValue: !!options.attributeOldValue,
                            characterDataOldValue: !!options.characterDataOldValue,
                            attributeFilter: filter
                        },
                        snapshot: null
                    };
                    reg.snapshot = snapshot(reg);
                    for (var i = 0; i < observers.length; i++) {
                        if (observers[i].observer === this && observers[i].target === target) {
                            observers.splice(i, 1);
                            break;
                        }
                    }
                    observers.push(reg);
                    syncCharDataTracking();
                };

                MutationObserver.prototype.disconnect = function () {
                    for (var i = observers.length - 1; i >= 0; i--) {
                        if (observers[i].observer === this) observers.splice(i, 1);
                    }
                    syncCharDataTracking();
                };

                // takeRecords empties the queue without running the callback, so the records are
                // collected and handed straight back.
                MutationObserver.prototype.takeRecords = function () {
                    collectCharData();
                    var out = [];
                    for (var i = 0; i < observers.length; i++) {
                        if (observers[i].observer !== this) continue;
                        var made = diff(observers[i]);
                        for (var j = 0; j < made.length; j++) out.push(made[j]);
                    }
                    pruneCharData();
                    return out;
                };

                window.MutationObserver = MutationObserver;
                window.MutationRecord = function MutationRecord() {
                    throw new TypeError('Illegal constructor');
                };
            })();

            // The microtask checkpoint is where a queued observer callback runs (DOM §4.5.6.1), and
            // the host calls this from it — after a script, after a timer, after an event.
            window.__domCheckpoint = function () {
                try { if (typeof window.__mutationFlush === 'function') window.__mutationFlush(); }
                catch (e) { }
                // The media lists are asked at the same checkpoint the mutation observers are, which
                // is where a page that changed the viewport without leaving the script learns that a
                // query it registered against flipped (MQ §3.1).
                try { if (typeof window.__mediaCheckpoint === 'function') window.__mediaCheckpoint(); }
                catch (e) { }
            };

            // ── Custom Elements (custom-elements §1) ───────────────────────────
            // The registry lives here because a definition is a script function — the constructor,
            // the attributes it observes and the callbacks are all script data. The engine keeps only
            // the set of names that carry one, and it names the moments a reaction is owed: an
            // element made for a registered name, a node put into the tree or taken out of it, an
            // attribute written. Those are the entry points at the bottom of this block. Every
            // reaction runs in the order the specification puts them — the constructor, then the
            // attributes the element already carries, then the connected callback.
            (function () {
                var Q = String.fromCharCode(34);
                var definitions = new Map();
                var namesByCtor = new Map();
                var constructedOf = new WeakMap();
                var connectedSet = new WeakSet();
                var waiters = new Map();
                var pendingElement = null;
                var makingOwnElement = false;
                var RESERVED = ['annotation-xml', 'color-profile', 'font-face', 'font-face-src',
                    'font-face-uri', 'font-face-format', 'font-face-name', 'missing-glyph'];

                function isNameStartChar(code) {
                    return (code >= 65 && code <= 90) || (code >= 97 && code <= 122)
                        || code === 95 || code === 58 || code >= 183;
                }
                function isNameChar(code) {
                    return isNameStartChar(code) || (code >= 48 && code <= 57)
                        || code === 45 || code === 46;
                }
                // The rule a name is held to, measured name by name: it begins with a lowercase ASCII
                // letter, no letter of it is uppercase, every character of it belongs to an XML name,
                // the part after its first colon carries a hyphen, and it is none of the eight names
                // the specification keeps back. 'x-' and 'svg:x-y' are names; '-x', 'xY-z', 'a:' and
                // 'font-face' are not.
                function validName(name) {
                    if (typeof name !== 'string' || name.length === 0) return false;
                    var first = name.charCodeAt(0);
                    if (first < 97 || first > 122) return false;
                    for (var i = 0; i < name.length; i++) {
                        var code = name.charCodeAt(i);
                        if (code >= 65 && code <= 90) return false;
                        if (!isNameChar(code)) return false;
                    }
                    if (RESERVED.indexOf(name) >= 0) return false;
                    // The hyphen may sit anywhere in the name — 'a-b:c' and 'a:-' are both names —
                    // so there is no rule about the colon beyond it being a name character.
                    return name.indexOf('-') >= 0;
                }
                function prefix(method) {
                    return 'Failed to execute ' + chr(39) + method + chr(39)
                        + ' on ' + chr(39) + 'CustomElementRegistry' + chr(39) + ': ';
                }
                function chr(code) { return String.fromCharCode(code); }
                function nameRefused(method, name) {
                    return new SyntaxError(prefix(method) + Q + String(name) + Q
                        + ' is not a valid custom element name');
                }
                function reportError(e) {
                    // A callback that throws is the page's error, not the engine's: the operation
                    // that asked for it still finishes (HTML §1 invoking custom element reactions).
                    try {
                        if (typeof window.__domError === 'function') window.__domError(e);
                    } catch (ignored) { }
                }
                function callReaction(fn, el, a, b, c, d) {
                    if (typeof fn !== 'function') return;
                    try { fn.call(el, a, b, c, d); } catch (e) { reportError(e); }
                }
                function localOf(node) {
                    try { return node && node.nodeType === 1 ? String(node.localName) : ''; }
                    catch (e) { return ''; }
                }
                function eachElementIn(node, out) {
                    if (!node) return out;
                    var isElement = false;
                    try { isElement = node.nodeType === 1; } catch (e) { }
                    if (isElement) out.push(node);
                    var kids = null;
                    try { kids = node.childNodes; } catch (e) { kids = null; }
                    if (!kids) return out;
                    for (var i = 0; i < kids.length; i++) eachElementIn(kids[i], out);
                    return out;
                }

                // ── the reactions themselves ───────────────────────────────────
                function attributeReaction(el, name, oldValue, newValue, ns) {
                    var registered = constructedOf.get(el);
                    if (registered === undefined) return;
                    var def = definitions.get(registered);
                    if (!def) return;
                    // The list a class observes is matched against the attribute's local name: a
                    // namespaced attribute written as 'x:foo' is observed as 'foo', and the callback
                    // is told the name the attribute is stored under (measured both halves).
                    var local = ns ? name.slice(name.indexOf(':') + 1) : name;
                    if (def.observed.indexOf(local) < 0) return;
                    var fn = null;
                    try { fn = el.attributeChangedCallback; } catch (e) { fn = null; }
                    callReaction(fn, el, name, oldValue === undefined ? null : oldValue,
                        newValue === undefined ? null : newValue, ns === undefined ? null : ns);
                }
                function connectReaction(el) {
                    if (connectedSet.has(el)) return;
                    connectedSet.add(el);
                    var fn = null;
                    try { fn = el.connectedCallback; } catch (e) { fn = null; }
                    callReaction(fn, el);
                }
                function disconnectReaction(el) {
                    if (!connectedSet.has(el)) return;
                    connectedSet.delete(el);
                    var fn = null;
                    try { fn = el.disconnectedCallback; } catch (e) { fn = null; }
                    callReaction(fn, el);
                }
                function construct(el, name) {
                    var def = definitions.get(name);
                    if (!def) return;
                    if (constructedOf.has(el)) return;
                    constructedOf.set(el, name);
                    pendingElement = el;
                    try { new def.ctor(); } catch (e) { reportError(e); }
                    pendingElement = null;
                    // The attributes that were already on the element are reported as though they had
                    // just arrived, in the order the element carries them (§1 upgrade).
                    var names = [];
                    try { names = el.getAttributeNames ? toJs(el.getAttributeNames()) : []; }
                    catch (e) { names = []; }
                    for (var i = 0; i < names.length; i++) {
                        var attributeName = String(names[i]);
                        var ns = null;
                        try {
                            var attrNode = el.getAttributeNode
                                ? el.getAttributeNode(attributeName) : null;
                            if (attrNode) ns = attrNode.namespaceURI;
                        } catch (e) { ns = null; }
                        var value = null;
                        try { value = el.getAttribute(attributeName); } catch (e) { }
                        attributeReaction(el, attributeName, null, value, ns);
                    }
                    if (isConnected(el)) connectReaction(el);
                }
                function isConnected(el) {
                    try { return !!el.isConnected; } catch (e) { return false; }
                }
                // Only a document with a browsing context has a registry, so only the page's own
                // document upgrades an element: a node the page made with 'document.implementation'
                // is never constructed, and an element that came from the page keeps whatever it was
                // built as when it travels out (measured both halves).
                function isPageDocument(el) {
                    try { return el.ownerDocument === document; } catch (e) { return false; }
                }
                function toJs(clrArray) {
                    var out = [];
                    if (!clrArray) return out;
                    try {
                        for (var i = 0; i < clrArray.length; i++) out.push(clrArray[i]);
                    } catch (e) { }
                    return out;
                }
                function insertionReaction(node) {
                    var list = eachElementIn(node, []);
                    for (var i = 0; i < list.length; i++) {
                        var el = list[i];
                        var name = localOf(el);
                        if (!definitions.has(name)) continue;
                        if (!constructedOf.has(el)) {
                            if (isPageDocument(el)) construct(el, name);
                            continue;
                        }
                        if (isConnected(el)) connectReaction(el);
                    }
                }
                function removalReaction(node) {
                    var list = eachElementIn(node, []);
                    for (var i = 0; i < list.length; i++) disconnectReaction(list[i]);
                }
                function adoptionReaction(node) {
                    var list = eachElementIn(node, []);
                    for (var i = 0; i < list.length; i++) {
                        var el = list[i];
                        if (!constructedOf.has(el)) continue;
                        var fn = null;
                        try { fn = el.adoptedCallback; } catch (e) { fn = null; }
                        callReaction(fn, el);
                    }
                }

                // ── the interface object ───────────────────────────────────────
                function CustomElementRegistry() { }
                if (typeof Symbol !== 'undefined' && Symbol.toStringTag) {
                    try {
                        Object.defineProperty(CustomElementRegistry.prototype, Symbol.toStringTag,
                            { value: 'CustomElementRegistry', configurable: true });
                    } catch (e) { }
                }

                // Two declared arguments, as the interface says; the options dictionary is read out of
                // 'arguments' so that it stays the third thing the page wrote without changing the
                // function's own length.
                CustomElementRegistry.prototype.define = function (name, ctor) {
                    var options = arguments.length > 2 ? arguments[2] : undefined;
                    if (arguments.length < 2) {
                        throw new TypeError(prefix('define') + '2 arguments required, but only '
                            + arguments.length + ' present.');
                    }
                    var key = String(name);
                    if (!validName(key)) throw nameRefused('define', key);
                    if (typeof ctor !== 'function') {
                        throw new TypeError(prefix('define')
                            + 'parameter 2 is not of type ' + chr(39) + 'Function' + chr(39) + '.');
                    }
                    // Two different refusals, measured apart: a function that cannot be constructed
                    // at all — the arrow a page writes — has no prototype property, while one that
                    // can be constructed but carries no prototype object (a plain function with its
                    // 'prototype' written away) is a constructor of the wrong shape.
                    if (!Object.prototype.hasOwnProperty.call(ctor, 'prototype')) {
                        throw new TypeError(prefix('define')
                            + 'constructor argument is not a constructor');
                    }
                    if (ctor.prototype === null || typeof ctor.prototype !== 'object') {
                        throw new TypeError(prefix('define')
                            + 'constructor prototype is not an object');
                    }
                    if (definitions.has(key)) {
                        throw new DOMException(prefix('define') + 'the name ' + Q + key + Q
                            + ' has already been used with this registry', 'NotSupportedError');
                    }
                    if (namesByCtor.has(ctor)) {
                        throw new DOMException(prefix('define')
                            + 'this constructor has already been used with this registry',
                            'NotSupportedError');
                    }
                    if (options !== undefined && options !== null) {
                        if (typeof options !== 'object') {
                            throw new TypeError(prefix('define') + 'The provided value is not of type '
                                + chr(39) + 'ElementDefinitionOptions' + chr(39) + '.');
                        }
                    }
                    var observed = [];
                    try {
                        // 'observedAttributes' is a static getter and the reference reads it once, at
                        // the definition (measured: one read for the definition, none per element).
                        var list = toJs(ctor.observedAttributes);
                        for (var i = 0; i < list.length; i++) observed.push(String(list[i]));
                    } catch (e) { observed = []; }
                    definitions.set(key, { ctor: ctor, observed: observed, extendsName: extendsOf(options) });
                    namesByCtor.set(ctor, key);
                    try {
                        if (typeof __acrux !== 'undefined' && typeof __acrux.ceDefine === 'function')
                            __acrux.ceDefine(key);
                    } catch (e) { }
                    var waiting = waiters.get(key);
                    if (waiting) {
                        waiters.delete(key);
                        for (var w = 0; w < waiting.length; w++) {
                            try { waiting[w](ctor); } catch (e) { reportError(e); }
                        }
                    }
                    // The elements of this document that already carry the name are upgraded (§1).
                    var tree = [];
                    try { tree = eachElementIn(document, []); } catch (e) { tree = []; }
                    for (var t = 0; t < tree.length; t++) {
                        if (localOf(tree[t]) === key && !constructedOf.has(tree[t]))
                            construct(tree[t], key);
                    }
                };

                function extendsOf(options) {
                    if (!options || typeof options !== 'object') return null;
                    var value = options.extends;
                    if (value === undefined || value === null) return null;
                    return String(value).toLowerCase();
                }

                CustomElementRegistry.prototype.get = function (name) {
                    var def = definitions.get(String(name));
                    return def ? def.ctor : undefined;
                };

                CustomElementRegistry.prototype.getName = function (ctor) {
                    // §1: the read is asked of the constructor, not of the name — a page that hands
                    // over the name is refused in the interface's own sentence (measured).
                    if (typeof ctor !== 'function') {
                        throw new TypeError(prefix('getName') + 'parameter 1 is not of type '
                            + chr(39) + 'Function' + chr(39) + '.');
                    }
                    var found = namesByCtor.get(ctor);
                    return found === undefined ? null : found;
                };

                CustomElementRegistry.prototype.whenDefined = function (name) {
                    var key = String(name);
                    if (!validName(key)) return Promise.reject(nameRefused('whenDefined', key));
                    var def = definitions.get(key);
                    if (def) return Promise.resolve(def.ctor);
                    var waiting = waiters.get(key);
                    if (!waiting) { waiting = []; waiters.set(key, waiting); }
                    return new Promise(function (resolve) { waiting.push(resolve); });
                };

                CustomElementRegistry.prototype.upgrade = function (node) {
                    var list = eachElementIn(node, []);
                    for (var i = 0; i < list.length; i++) {
                        var el = list[i];
                        var name = localOf(el);
                        if (!definitions.has(name) || constructedOf.has(el)) continue;
                        if (!isPageDocument(el)) continue;
                        construct(el, name);
                    }
                };

                var registry = new CustomElementRegistry();
                window.CustomElementRegistry = CustomElementRegistry;
                Object.defineProperty(window, 'customElements', {
                    configurable: true, enumerable: true,
                    get: function () { return registry; }
                });

                // ── the moments the engine names ───────────────────────────────
                // 'super()' inside a custom element constructor has to hand back the element: the one
                // the engine is building while it constructs, or — for a page that called
                // 'new MyEl()' itself — one made now for the name this constructor is registered
                // under (§1: a custom element constructor is also an element constructor).
                window.__ceTakeElement = function (ctor) {
                    if (pendingElement) {
                        var held = pendingElement;
                        if (ctor) { try { Object.setPrototypeOf(held, ctor.prototype); } catch (e) { } }
                        return held;
                    }
                    if (!ctor) return null;
                    var name = namesByCtor.get(ctor);
                    if (!name) return null;
                    // The element is made without consulting the registry: this very constructor is
                    // the one that is running, and a second construction of it would run the page's
                    // constructor body twice.
                    makingOwnElement = true;
                    var made = null;
                    try { made = document.createElement(name); } catch (e) { made = null; }
                    makingOwnElement = false;
                    if (!made) return null;
                    try { Object.setPrototypeOf(made, ctor.prototype); } catch (e) { }
                    constructedOf.set(made, name);
                    return made;
                };
                // The prototype a custom element carries is the definition's, not the interface's, so
                // the wrapper fix-up has to leave an element the registry owns alone.
                window.__ceOwnsPrototype = function (obj) {
                    try { return constructedOf.has(obj); } catch (e) { return false; }
                };
                window.__ceConstructed = function (el, name) {
                    if (makingOwnElement) return;
                    construct(el, String(name));
                };
                window.__ceInserted = function (node) { insertionReaction(node); };
                window.__ceRemoved = function (node) { removalReaction(node); };
                window.__ceAdopted = function (node) { adoptionReaction(node); };
                window.__ceAttribute = function (el, name, oldValue, newValue, ns) {
                    attributeReaction(el, String(name), oldValue, newValue,
                        ns === undefined || ns === null ? null : String(ns));
                };
            })();

            // ── The element interfaces a tag names (HTML §4.1, §1.9) ───────────
            // A wrapper is stamped with the interface its tag and namespace name (HTML §4.1), so
            // 'input instanceof HTMLInputElement' and '[object HTMLDivElement]' read from the same
            // table the element factory builds its classes from. Those interfaces are not page-side
            // constructors — 'new HTMLInputElement()' refuses — but a custom element class that
            // derives from one still gets its element through 'super()', so the refusal runs only
            // when nothing is being built (§1.9).
            (function () {
                var tagShape = /^(HTML|SVG|MathML)[A-Za-z0-9]*Element$/;
                var protoMap = window.__domProto || {};
                var names = Object.getOwnPropertyNames(window);
                function illegalise(name, proto) {
                    var Ctor = function () {
                        if (window.__ceTakeElement) {
                            var taken = window.__ceTakeElement(
                                typeof new.target === 'function' ? new.target : null);
                            if (taken) {
                                try { Object.setPrototypeOf(taken, proto); } catch (e) { }
                                return taken;
                            }
                        }
                        throw new TypeError('Failed to construct \'' + name
                            + '\': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = proto;
                    return Ctor;
                }
                for (var i = 0; i < names.length; i++) {
                    var name = names[i];
                    if (!tagShape.test(name) || name === 'HTMLElement') continue;
                    var level = protoMap[name];
                    var Ctor = window[name];
                    // Only the interfaces the DOM prototype system built: a page's own class that
                    // happens to be named like one of them is left alone.
                    if (!level || typeof Ctor !== 'function' || Ctor.prototype !== level) continue;
                    window[name] = illegalise(name, level);
                }
            })();

            // ── Window members a page reads (HTML §7.3, §9.2; CSSOM View §9) ──
            (function () {
                function constant(name, value) {
                    Object.defineProperty(window, name, { configurable: true, enumerable: true,
                        get: function () { return value; } });
                }
                Object.defineProperty(window, 'origin', {
                    configurable: true, enumerable: true,
                    get: function () {
                        try { return String(location.origin || location.href); } catch (e) { return ''; }
                    }
                });
                // A page the engine opens over a scheme the reference treats as trustworthy is a
                // secure context, and the cross-origin isolation the engine never asks for is off.
                constant('isSecureContext', true);
                constant('crossOriginIsolated', false);
                // A window that is not itself in a frame has no element that represents it (§7.3).
                constant('frameElement', null);
                constant('external', { AddSearchProvider: function () { },
                    IsSearchProviderInstalled: function () { return 0; } });
                // The visual viewport (CSSOM View §9) reports the area the host lays the page out in,
                // at scale one — the zoom interface has no separate state to read from.
                var viewport = { offsetLeft: 0, offsetTop: 0, scale: 1,
                    onresize: null, onscroll: null };
                viewport.addEventListener = function () { };
                viewport.removeEventListener = function () { };
                viewport.dispatchEventListener = undefined;
                Object.defineProperty(viewport, 'width', { configurable: true, enumerable: true,
                    get: function () { try { return window.innerWidth; } catch (e) { return 0; } } });
                Object.defineProperty(viewport, 'height', { configurable: true, enumerable: true,
                    get: function () { try { return window.innerHeight; } catch (e) { return 0; } } });
                Object.defineProperty(viewport, 'pageLeft', { configurable: true, enumerable: true,
                    get: function () { try { return window.scrollX; } catch (e) { return 0; } } });
                Object.defineProperty(viewport, 'pageTop', { configurable: true, enumerable: true,
                    get: function () { try { return window.scrollY; } catch (e) { return 0; } } });
                try {
                    Object.defineProperty(viewport, Symbol.toStringTag,
                        { value: 'VisualViewport', configurable: true });
                } catch (e) { }
                constant('visualViewport', viewport);
                // An image bitmap needs decoded bytes; the engine decodes an image inside its own
                // pipeline, so the promise is refused the way the interface refuses a source it
                // cannot read rather than throwing out of the call (HTML §9.2).
                window.createImageBitmap = function () {
                    return Promise.reject(new DOMException('The source given could not be decoded.',
                        'InvalidStateError'));
                };
                // Web Animations §4.6: the timeline the window and the document report. The engine
                // runs its animations from the style pass, and the document's animation list is the
                // host's answer (empty until 'element.animate' hands an animation back).
                var timeline = { currentTime: 0, phase: 'active', playbackRate: 1 };
                try {
                    Object.defineProperty(timeline, Symbol.toStringTag,
                        { value: 'DocumentTimeline', configurable: true });
                } catch (e) { }
                Object.defineProperty(document, 'timeline', { configurable: true, enumerable: true,
                    get: function () { return timeline; } });
                window.getAnimations = function () { return document.getAnimations(); };
                // Fullscreen §5: nothing in this engine takes a window over, so the element is null,
                // the flag is off and the exit is refused the way a document that is not in
                // fullscreen is.
                Object.defineProperty(document, 'fullscreenElement', { configurable: true,
                    enumerable: true, get: function () { return null; } });
                Object.defineProperty(document, 'fullscreenEnabled', { configurable: true,
                    enumerable: true, get: function () { return false; } });
                Object.defineProperty(document, 'pictureInPictureEnabled', { configurable: true,
                    enumerable: true, get: function () { return false; } });
                document.exitFullscreen = function () {
                    return Promise.reject(new DOMException(
                        'The document is not fullscreen.', 'NotFoundError'));
                };
                document.exitPictureInPicture = function () {
                    return Promise.reject(new DOMException(
                        'The document is not in picture-in-picture mode.', 'NotFoundError'));
                };
                // DOM String list (§2.7): the ordered set with an index face, a length and 'item'.
                function DOMStringList(items) {
                    var list = items || [];
                    var self = this;
                    this.length = list.length;
                    this.item = function (n) {
                        n = Number(n);
                        return n >= 0 && n < list.length ? list[n] : null;
                    };
                    this.contains = function (value) { return list.indexOf(String(value)) >= 0; };
                    for (var i = 0; i < list.length; i++) self[i] = list[i];
                }
                window.DOMStringList = DOMStringList;
                if (typeof __acrux !== 'undefined' && typeof __acrux.hardwareConcurrency === 'function') {
                    Object.defineProperty(navigator, 'hardwareConcurrency', { configurable: true,
                        enumerable: true, get: function () { return __acrux.hardwareConcurrency(); } });
                }
            })();

            // ── The event families a page builds (UI Events §3–§7, DOM §4.5.2) ───
            // 'new WheelEvent(type, init)' and its relatives: the engine's own event object carries
            // the members its dispatch needs, a member it does not carry is kept beside the event,
            // keyed by it, and read through the interface's prototype — the shape the element dataset
            // already uses. The interface the event was built for stamps the wrapper, so
            // 'wheel instanceof WheelEvent' and '[object WheelEvent]' both answer from that.
            (function () {
                var protoMap = window.__domProto || {};
                function defineInterface(name, baseName, construct) {
                    var base = baseName ? protoMap[baseName] : null;
                    var proto = protoMap[name] || Object.create(base || Object.prototype);
                    if (!protoMap[name]) protoMap[name] = proto;
                    // A name the script already gave a constructor of its own — 'Event', 'MouseEvent'
                    // — keeps whatever its prototype declares: the level inherits from it, so the
                    // 'initEvent' spellings written there stay reachable on the objects the host makes.
                    // A level is never hung below a script constructor's prototype: the stamping
                    // that gives a host object its interface uses the level itself, so the level's
                    // own place in the chain is the only one that matters. A level rebuilt for a new
                    // document (the setup script runs once per document) starts from this document's
                    // base level, which is why a page that has the same interface twice still walks
                    // from a pointer event down to 'Event'.
                    var Ctor = construct || function () {
                        throw new TypeError('Failed to construct \'' + name
                            + '\': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = proto;
                    Object.defineProperty(proto, 'constructor',
                        { value: Ctor, writable: true, configurable: true });
                    try {
                        Object.defineProperty(proto, Symbol.toStringTag,
                            { value: name, writable: false, enumerable: false, configurable: true });
                    } catch (e) { }
                    // The wrapper a host object gets is stamped with the interface's level, so the
                    // constructor the page sees has to carry that same object — otherwise
                    // 'e instanceof Event' compares against a prototype no event wears. A name the
                    // script already gave a constructor of its own keeps everything it declared: the
                    // level inherits from its prototype above.
                    Ctor.prototype = proto;
                    window[name] = Ctor;
                    return proto;
                }
                function eventCtor(name, members, initOnly) {
                    // A Touch is the one event the interfaces build from a dictionary alone: there
                    // is no type to name it, the identifier and the coordinates are the whole thing.
                    // Which interface each family hangs on (UI Events §3–§7): the pointer and drag
                    // events carry every mouse member, so they are mouse events; the focus, wheel,
                    // input, composition and touch events are user-interface events without a mouse
                    // shape; the rest extend Event directly.
                    var baseName = name === 'UIEvent' ? 'Event'
                        : (name === 'PointerEvent' || name === 'DragEvent') ? 'MouseEvent'
                            : (name === 'WheelEvent' || name === 'InputEvent'
                                || name === 'CompositionEvent' || name === 'TouchEvent'
                                || name === 'FocusEvent') ? 'UIEvent' : 'Event';
                    var proto = defineInterface(name, baseName,
                        function (type, init) {
                            var evt = document.createEvent(name);
                            if (initOnly) { init = type; }
                            else evt.type = String(type === undefined ? '' : type);
                            init = init || { };
                            // DOM §4.5.2: EventInit's three flags are all false, and every family built
                            // on it starts from there. The engine's own event object is made for the
                            // dispatch, where it does bubble and is often cancelable, so an event the
                            // page builds itself has to be told the dictionary's answer rather than keep
                            // the one the dispatcher would have worn (measured: 'new MouseEvent(...)'
                            // reads bubbles false and cancelable false unless the init says otherwise).
                            try { evt.bubbles = !!init.bubbles; } catch (e) { }
                            try { evt.cancelable = !!init.cancelable; } catch (e) { }
                            try { evt.composed = !!init.composed; } catch (e) { }
                            for (var k = 0; k < members.length; k++) {
                                var key = members[k][0];
                                var value;
                                if (init[key] !== undefined) value = init[key];
                                else if (typeof members[k][1] === 'function') continue;
                                else if (members[k][1] !== undefined) value = members[k][1];
                                else continue;
                                // The page's value goes through to the host when the engine carries
                                // that member — so a dispatch reads what the page wrote — and is kept
                                // on the event itself, non-enumerably, so the read answers exactly the
                                // value the dictionary held even where the host stores another shape.
                                try { evt[key] = value; } catch (e) { }
                                try {
                                    Object.defineProperty(evt, key, { value: value, writable: true,
                                        enumerable: false, configurable: true });
                                } catch (e) { }
                            }
                            try { Object.setPrototypeOf(evt, proto); } catch (e) { }
                            return evt;
                        });
                    // The methods an interface declares are shared, so they live on the prototype.
                    for (var i = 0; i < members.length; i++) {
                        if (typeof members[i][1] !== 'function') continue;
                        if (Object.prototype.hasOwnProperty.call(proto, members[i][0])) continue;
                        try {
                            Object.defineProperty(proto, members[i][0],
                                { value: members[i][1], writable: true, enumerable: false, configurable: true });
                        } catch (e) { }
                    }
                    return proto;
                }

                // The names the script already gave a constructor of its own — 'Event', 'MouseEvent'
                // and friends — must hand out the level the host stamps its objects with, or
                // 'event instanceof Event' compares against a prototype no event ever wears. The
                // level inherits whatever that prototype declared first, so nothing written there is
                // lost.
                // The methods every event shares live on the Event prototype, where a page looks for
                // them; a host event answers its own engine-driven version of the name first, so these
                // are what a plain object built like an event falls back to (§2.3).
                var sharedMethods = ['preventDefault', 'stopPropagation', 'stopImmediatePropagation'];
                var eventLevel = protoMap.Event || (protoMap.Event = Object.create(Object.prototype));
                for (var sm = 0; sm < sharedMethods.length; sm++) {
                    (function (key) {
                        if (Object.prototype.hasOwnProperty.call(eventLevel, key)) return;
                        Object.defineProperty(eventLevel, key, {
                            value: function () { }, writable: true, configurable: true, enumerable: false
                        });
                    })(sharedMethods[sm]);
                }
                if (!eventLevel.composedPath) {
                    Object.defineProperty(eventLevel, 'composedPath', {
                        value: function () { return []; }, writable: true, configurable: true });
                }
                var aligned = ['Event', 'CustomEvent', 'MouseEvent', 'KeyboardEvent', 'UIEvent'];
                for (var al = 0; al < aligned.length; al++) {
                    var level = protoMap[aligned[al]];
                    var earlier = window[aligned[al]];
                    if (!level || typeof earlier !== 'function' || earlier.prototype === level) continue;
                    try {
                        // The level keeps its place in the chain; the constructor is what is moved
                        // onto it, so 'x instanceof Event' tests the object the host stamps with.
                        earlier.prototype = level;
                        Object.defineProperty(level, 'constructor',
                            { value: earlier, writable: true, configurable: true });
                    } catch (e) { }
                }

                eventCtor('UIEvent', [['detail', 0], ['view', null], ['which', 0]]);
                eventCtor('MouseEvent', [['screenX', 0], ['screenY', 0], ['clientX', 0], ['clientY', 0],
                    ['pageX', function () { return this.clientX; }],
                    ['pageY', function () { return this.clientY; }],
                    ['movementX', 0], ['movementY', 0], ['button', 0], ['buttons', 0],
                    ['ctrlKey', false], ['shiftKey', false], ['altKey', false], ['metaKey', false],
                    ['relatedTarget', null], ['region', '']]);
                eventCtor('KeyboardEvent', [['key', ''], ['code', ''], ['location', 0],
                    ['repeat', false], ['isComposing', false], ['ctrlKey', false],
                    ['shiftKey', false], ['altKey', false], ['metaKey', false], ['altGraphKey', false],
                    ['getModifierState', function (name) {
                        var key = String(name);
                        if (key === 'Shift') return !!this.shiftKey;
                        if (key === 'Control') return !!this.ctrlKey;
                        if (key === 'Alt') return !!this.altKey;
                        if (key === 'Meta') return !!this.metaKey;
                        if (key === 'AltGraph') return !!this.altGraphKey;
                        return false;
                    }]]);
                eventCtor('FocusEvent', [['relatedTarget', null], ['detail', 0]]);
                eventCtor('WheelEvent', [['deltaX', 0], ['deltaY', 0], ['deltaZ', 0], ['deltaMode', 0],
                    ['wheelDelta', 0], ['wheelDeltaX', 0], ['wheelDeltaY', 0]]);
                eventCtor('PointerEvent', [['pointerId', 0], ['width', 1], ['height', 1],
                    ['pressure', 0], ['tangentialPressure', 0], ['tiltX', 0], ['tiltY', 0],
                    ['twist', 0], ['altitudeAngle', 0], ['azimuthAngle', 0],
                    // Measured: an uninitialised pointer event names no pointer type and is not the
                    // primary one — the values are the defaults the dictionary leaves alone.
                    ['pointerType', ''], ['isPrimary', false]]);
                eventCtor('InputEvent', [['inputType', ''], ['data', null], ['isComposing', false],
                    ['dataTransfer', null], ['targetRanges', []],
                    ['getTargetRanges', function () { return []; }]]);
                eventCtor('CompositionEvent', [['data', ''], ['locale', '']]);
                eventCtor('DragEvent', [['dataTransfer', null]]);
                var touchMembers = [['identifier', undefined], ['clientX', 0], ['clientY', 0],
                    ['screenX', 0], ['screenY', 0], ['pageX', 0], ['pageY', 0], ['radiusX', 0],
                    ['radiusY', 0], ['rotationAngle', 0], ['force', 0], ['target', undefined]];
                // A Touch is not an event — it is a plain record of one finger's state, carried inside
                // a TouchEvent — so it is built as an object of its own rather than as a host event,
                // and the dictionary's own values are what the reads answer (§7.1).
                var Touch = function Touch(init) {
                    var required = ['identifier', 'target'];
                    for (var r = 0; r < required.length; r++) {
                        if (!init || init[required[r]] === undefined) {
                            throw new TypeError('Failed to construct \'Touch\': Failed to read the'
                                + ' \'' + required[r] + '\' property from \'TouchInit\':'
                                + ' Required member is undefined.');
                        }
                    }
                    var self = this;
                    for (var m = 0; m < touchMembers.length; m++) {
                        var key = touchMembers[m][0];
                        var value = init[key] !== undefined ? init[key] : touchMembers[m][1];
                        if (value === undefined) continue;
                        try {
                            Object.defineProperty(self, key, { value: value, writable: true,
                                enumerable: false, configurable: true });
                        } catch (e) { }
                    }
                };
                var touchProto = defineInterface('Touch', null, Touch);
                window.Touch = Touch;
                eventCtor('TouchEvent', [['touches', []], ['targetTouches', []],
                    ['changedTouches', []], ['altKey', false], ['ctrlKey', false],
                    ['metaKey', false], ['shiftKey', false]]);
                eventCtor('ClipboardEvent', [['clipboardData', null]]);
                eventCtor('AnimationEvent', [['animationName', ''], ['elapsedTime', 0],
                    ['pseudoElement', '']]);
                eventCtor('TransitionEvent', [['propertyName', ''], ['elapsedTime', 0],
                    ['pseudoElement', '']]);
                eventCtor('MutationEvent', [['relatedNode', null], ['attrName', ''],
                    ['attrChange', 0], ['prevValue', ''], ['newValue', ''],
                    ['isCharacterData', false], ['modType', '']]);
                eventCtor('CloseEvent', [['wasClean', false], ['code', 0], ['reason', '']]);
                eventCtor('StorageEvent', [['key', null], ['oldValue', null], ['newValue', null],
                    ['url', ''], ['storageArea', null]]);
                eventCtor('HashChangeEvent', [['oldURL', ''], ['newURL', '']]);
                eventCtor('PopStateEvent', [['state', null]]);
                eventCtor('PageTransitionEvent', [['persisted', false]]);
                eventCtor('BeforeUnloadEvent', [['returnValue', '']]);
                eventCtor('PromiseRejectionEvent', [['reason', undefined], ['promise', undefined]]);
                eventCtor('ProgressEvent', [['lengthComputable', false], ['loaded', 0], ['total', 0]]);
                eventCtor('SubmitEvent', [['submitter', null]]);
                eventCtor('ToggleEvent', [['oldState', ''], ['newState', '']]);
                // A beforeunload event is the platform's to raise: a page that asks for one is refused,
                // the same as the reference (UI Events §5.9, measured: 'Illegal constructor').
                if (window.BeforeUnloadEvent) {
                    var beforeUnloadLevel = protoMap['BeforeUnloadEvent'];
                    var beforeUnloadRefused = function () {
                        throw new TypeError('Failed to construct ' + String.fromCharCode(39)
                            + 'BeforeUnloadEvent' + String.fromCharCode(39) + ': Illegal constructor');
                    };
                    if (beforeUnloadLevel) {
                        beforeUnloadRefused.prototype = beforeUnloadLevel;
                        window.BeforeUnloadEvent = beforeUnloadRefused;
                    }
                }
                defineInterface('EventSource', 'EventTarget');
                // The object 'document.implementation' answers (DOM §3.3): the level gives the host
                // wrapper its tag, its constructor name and the sentence the reference writes for a
                // page that tries to build one itself.
                defineInterface('DOMImplementation');
                // The interface a canvas 2D context measures with. This engine has no 2D context to
                // hand one out from, so the name exists and the constructor refuses, which is what a
                // page finds on a browser that never made one for it.
                var errorEventCtor = window.ErrorEvent;
                var errorEventLevel = defineInterface('ErrorEvent', 'Event');
                if (typeof errorEventCtor === 'function') {
                    // The script built this one itself, so the constructor the page calls stays its own
                    // and only the level moves under it: the tag, the 'instanceof' answer and the
                    // members the interface declares all read from there. The level the script made
                    // stands on its own until the event family is built, so it is hung below 'Event'
                    // here — otherwise 'errorEvent instanceof Event' would read false.
                    if (Object.getPrototypeOf(errorEventLevel) === Object.prototype) {
                        try {
                            Object.setPrototypeOf(errorEventLevel, protoMap['Event'] || Object.prototype);
                        } catch (e) { }
                    }
                    errorEventCtor.prototype = errorEventLevel;
                    window.ErrorEvent = errorEventCtor;
                }
                defineInterface('TextMetrics');
                defineInterface('ShadowRoot', 'DocumentFragment');
                defineInterface('ElementInternals');
                defineInterface('ValidityState');
                defineInterface('DocumentOrShadowRoot');
                defineInterface('DOMStringMap');
                defineInterface('DocumentTimeline', 'GlobalTimeline', function () { });
                defineInterface('Navigator', 'EventTarget');
                defineInterface('Location');
                defineInterface('History');
                defineInterface('Screen');
                defineInterface('Slot', 'Element');
                defineInterface('EventListener');

                // ── 'EventTarget' as a page builds one (DOM §2.3, §2.7) ──────────
                // A page that wants its own dispatch point calls 'new EventTarget()' and the listeners
                // live on the object the page made, so the walk is script's own: registration order,
                // the 'once' option that drops the listener after it has run, the capture flag that
                // makes a second listener of the same function, and the 'on' + type member that runs
                // after the bag. The names stay on the interface level, so every object the host
                // stamps with it — a document, an element, an event source — inherits the same face
                // while answering its own host member first.
                var eventTargetLevel = defineInterface('EventTarget', null, function () { });
                function listenerFlags(record) {
                    // The IDL takes either the capture boolean or the dictionary; both carry the same
                    // two flags this far.
                    if (record === true || record === false) return { capture: !!record, once: false };
                    record = record || {};
                    return { capture: !!record.capture, once: !!record.once };
                }
                function listenerBag(self) {
                    // The bag is hidden and non-enumerable, so a page that walks the object it built
                    // sees the interface, not the engine's bookkeeping.
                    if (!Object.prototype.hasOwnProperty.call(self, '__domListeners')) {
                        Object.defineProperty(self, '__domListeners', {
                            value: {}, writable: true, enumerable: false, configurable: true
                        });
                    }
                    return self.__domListeners;
                }
                function defineOnLevel(level, key, value) {
                    if (Object.prototype.hasOwnProperty.call(level, key)) return;
                    Object.defineProperty(level, key,
                        { value: value, writable: true, enumerable: false, configurable: true });
                }
                defineOnLevel(eventTargetLevel, 'addEventListener', function (type, listener, options) {
                    if (listener == null) return;
                    var bag = listenerBag(this);
                    var list = bag[String(type)] = bag[String(type)] || [];
                    var flags = listenerFlags(options);
                    for (var i = 0; i < list.length; i++) {
                        if (list[i].fn === listener && list[i].capture === flags.capture) return;
                    }
                    list.push({ fn: listener, capture: flags.capture, once: flags.once });
                });
                defineOnLevel(eventTargetLevel, 'removeEventListener', function (type, listener, options) {
                    if (listener == null) return;
                    var bag = listenerBag(this);
                    var list = bag[String(type)];
                    if (!list) return;
                    var flags = listenerFlags(options);
                    for (var i = 0; i < list.length; i++) {
                        if (list[i].fn === listener && list[i].capture === flags.capture) {
                            list.splice(i, 1);
                            return;
                        }
                    }
                });
                defineOnLevel(eventTargetLevel, 'dispatchEvent', function (event) {
                    if (event == null) {
                        throw new TypeError('Failed to execute \'dispatchEvent\' on \'EventTarget\': '
                            + 'parameter 1 is not of type \'Event\'.');
                    }
                    var type = String(event.type === undefined ? '' : event.type);
                    var list = (listenerBag(this)[type] || []).slice();
                    try { event.target = this; } catch (e) { }
                    try { event.currentTarget = this; } catch (e) { }
                    for (var i = 0; i < list.length; i++) {
                        var entry = list[i];
                        // A listener registered with 'once' is taken out before it runs, so a dispatch
                        // it raises from inside cannot hear it again.
                        if (entry.once) {
                            var live = listenerBag(this)[type] || [];
                            var at = live.indexOf(entry);
                            if (at >= 0) live.splice(at, 1);
                        }
                        try {
                            if (typeof entry.fn === 'function') entry.fn.call(this, event);
                            else if (entry.fn && typeof entry.fn.handleEvent === 'function') entry.fn.handleEvent(event);
                        } catch (e) { }
                    }
                    var on = 'on' + type;
                    if (typeof this[on] === 'function') { try { this[on](event); } catch (e) { } }
                    return event.defaultPrevented !== true;
                });

                // ── 'DOMTokenList' read as a collection (DOM §3.3.6) ─────────────
                // The tokens live in the host, so the walk reads them back through the two members that
                // interface already answers there — 'length' and 'item' — and hands each token to the
                // page with its index and the list itself, which is what a page treating the list as a
                // collection expects. 'values', 'keys' and 'entries' are the value, index and pair
                // walks of the same tokens, and the list is iterable, so 'Array.from(el.classList)' and
                // 'for (var token of list)' answer from the one place. 'supports' belongs to a list that
                // declares supported tokens; the class list has none, and the reference refuses the call
                // rather than answering false for a token it cannot know (measured) — the 'rel' list,
                // which does declare them, answers the name from its own host member.
                var tokenListLevel = defineInterface('DOMTokenList', null);
                function tokenCount(self) {
                    var n = Number(self.length);
                    return n > 0 ? Math.floor(n) : 0;
                }
                function tokenArray(self) {
                    var out = [];
                    for (var i = 0; i < tokenCount(self); i++) {
                        var token = self.item(i);
                        out.push(token === null || token === undefined ? '' : String(token));
                    }
                    return out;
                }
                function iterableOf(items) {
                    return items && typeof items[Symbol.iterator] === 'function'
                        ? items[Symbol.iterator]() : items;
                }
                defineOnLevel(tokenListLevel, 'forEach', function (callback, thisArg) {
                    if (typeof callback !== 'function') return;
                    var tokens = tokenArray(this);
                    for (var i = 0; i < tokens.length; i++) {
                        callback.call(thisArg, tokens[i], i, this);
                    }
                });
                defineOnLevel(tokenListLevel, 'values', function () { return iterableOf(tokenArray(this)); });
                defineOnLevel(tokenListLevel, 'keys', function () {
                    var out = [];
                    for (var i = 0; i < tokenCount(this); i++) out.push(i);
                    return iterableOf(out);
                });
                defineOnLevel(tokenListLevel, 'entries', function () {
                    var tokens = tokenArray(this);
                    var out = [];
                    for (var i = 0; i < tokens.length; i++) out.push([i, tokens[i]]);
                    return iterableOf(out);
                });
                defineOnLevel(tokenListLevel, Symbol.iterator, function () { return this.values(); });
                defineOnLevel(tokenListLevel, 'supports', function () {
                    throw new TypeError('Failed to execute \'supports\' on \'DOMTokenList\': '
                        + 'DOMTokenList has no supported tokens.');
                });

                // ── The constants the Node interface carries (DOM §4.2.1, §4.4.1) ──
                // Every node type and every position bit is a property of the interface and of its
                // prototype, which is how a page reads 'Node.TEXT_NODE' and 'el.ELEMENT_NODE' from the
                // same place, and they are readonly: a script that writes one leaves the value it was
                // given untouched (measured: the six position bits are 1, 2, 4, 8, 16 and 32 and answer
                // on the constructor, the prototype and every node through the chain).
                var nodeConstants = [
                    ['ELEMENT_NODE', 1], ['ATTRIBUTE_NODE', 2], ['TEXT_NODE', 3],
                    ['CDATA_SECTION_NODE', 4], ['ENTITY_REFERENCE_NODE', 5], ['ENTITY_NODE', 6],
                    ['PROCESSING_INSTRUCTION_NODE', 7], ['COMMENT_NODE', 8], ['DOCUMENT_NODE', 9],
                    ['DOCUMENT_TYPE_NODE', 10], ['DOCUMENT_FRAGMENT_NODE', 11], ['NOTATION_NODE', 12],
                    ['DOCUMENT_POSITION_DISCONNECTED', 1], ['DOCUMENT_POSITION_PRECEDING', 2],
                    ['DOCUMENT_POSITION_FOLLOWING', 4], ['DOCUMENT_POSITION_CONTAINS', 8],
                    ['DOCUMENT_POSITION_CONTAINED_BY', 16], ['DOCUMENT_POSITION_IMPLEMENTATION_SPECIFIC', 32]];
                var nodeLevel = protoMap['Node'] || (window.Node && window.Node.prototype);
                var constantShape = { writable: false, enumerable: false, configurable: false };
                for (var nc = 0; nc < nodeConstants.length; nc++) {
                    var cname = nodeConstants[nc][0];
                    var cvalue = nodeConstants[nc][1];
                    if (nodeLevel && !Object.prototype.hasOwnProperty.call(nodeLevel, cname)) {
                        constantShape.value = cvalue;
                        try { Object.defineProperty(nodeLevel, cname, constantShape); } catch (e) { }
                    }
                    if (window.Node && !Object.prototype.hasOwnProperty.call(window.Node, cname)) {
                        constantShape.value = cvalue;
                        try { Object.defineProperty(window.Node, cname, constantShape); } catch (e) { }
                    }
                }

                // The 'touchAction' style the interface reads, and the string list a name is a part of.
                function DOMStringList(items) {
                    var list = items || [];
                    var self = this;
                    this.length = list.length;
                    this.item = function (n) {
                        n = Number(n);
                        return n >= 0 && n < list.length ? list[n] : null;
                    };
                    this.contains = function (value) { return list.indexOf(String(value)) >= 0; };
                    for (var i = 0; i < list.length; i++) self[i] = list[i];
                }
                Object.defineProperty(DOMStringList.prototype, Symbol.toStringTag,
                    { value: 'DOMStringList', configurable: true });
                window.DOMStringList = DOMStringList;
                protoMap.DOMStringList = DOMStringList.prototype;

                // ── The request a page opens (XMLHttpRequest §2) ───────────────────
                // The host carries the state machine and the members the page reads; the level below it
                // carries what a host member cannot. The five state constants answer on the interface,
                // on its prototype and on every request through it (measured: 'XMLHttpRequest.DONE' and
                // 'new XMLHttpRequest().UNSENT' are 4 and 0), and 'EventTarget' one level above gives
                // the listener bag, so a request dispatched from the engine runs both the listeners and
                // the 'on' plus type member the page wrote.
                var xhrLevel = defineInterface('XMLHttpRequest', 'EventTarget', function () {
                    return __acrux.createXMLHttpRequest();
                });
                defineInterface('XMLHttpRequestUpload', 'EventTarget');
                var xhrStates = [['UNSENT', 0], ['OPENED', 1], ['HEADERS_RECEIVED', 2],
                    ['LOADING', 3], ['DONE', 4]];
                for (var xs = 0; xs < xhrStates.length; xs++) {
                    var stateShape = { value: xhrStates[xs][1], writable: false,
                        enumerable: false, configurable: false };
                    try {
                        Object.defineProperty(xhrLevel, xhrStates[xs][0], stateShape);
                        Object.defineProperty(window.XMLHttpRequest, xhrStates[xs][0], stateShape);
                    } catch (e) { }
                }
                // The engine's way of handing a request's event to the page: the same route a custom
                // element reaction takes, with the host itself as the target, so nothing is built in C#.
                window.__acruxXhrEvent = function (target, type) {
                    try {
                        if (!target || typeof target.dispatchEvent !== 'function') return;
                        target.dispatchEvent(new Event(String(type)));
                    } catch (e) { }
                };
            })();

            // ── The XPath the page evaluates (DOM Parsing and the XPath 1.0 a browser exposes) ──
            // 'document.evaluate' is a location-path expression read against a context node: the ten
            // axes a step can name, the node tests ('name', '*', 'text()', 'comment()',
            // 'processing-instruction()', 'node()', '@attr'), predicates by position, by attribute and by
            // a comparison, and the functions a predicate is usually written with. The result answers the
            // type the caller asked for, with the iterator and snapshot walks over the same node set in
            // document order. What the subset leaves out — unions, filters on a step's own result,
            // variables, the rest of the function library — is refused with the same sentence the
            // reference writes for a string it cannot parse.
            (function () {
                var ANY_TYPE = 0, NUMBER_TYPE = 1, STRING_TYPE = 2, BOOLEAN_TYPE = 3,
                    ORDERED_NODE_ITERATOR_TYPE = 4, UNORDERED_NODE_ITERATOR_TYPE = 5,
                    ORDERED_NODE_SNAPSHOT_TYPE = 6, UNORDERED_NODE_SNAPSHOT_TYPE = 7,
                    ANY_UNORDERED_NODE_TYPE = 8, FIRST_ORDERED_NODE_TYPE = 9;
                var RESULT_NAMES = ['ANY_TYPE', 'NUMBER_TYPE', 'STRING_TYPE', 'BOOLEAN_TYPE',
                    'ORDERED_NODE_ITERATOR_TYPE', 'UNORDERED_NODE_ITERATOR_TYPE',
                    'ORDERED_NODE_SNAPSHOT_TYPE', 'UNORDERED_NODE_SNAPSHOT_TYPE',
                    'ANY_UNORDERED_NODE_TYPE', 'FIRST_ORDERED_NODE_TYPE'];
                var protoMap = window.__domProto || {};

                function isNode(value) { return !!value && typeof value.nodeType === 'number'; }
                function quote(text) { return String.fromCharCode(39) + String(text) + String.fromCharCode(39); }
                function refused(expr) {
                    return new SyntaxError('Failed to execute ' + quote('evaluate') + ' on '
                        + quote('Document') + ': The string ' + quote(text_of(expr))
                        + ' is not a valid XPath expression.');
                }
                function text_of(expr) { return String(expr === undefined ? '' : expr); }

                function childNodes(node) {
                    var out = [];
                    if (!node) return out;
                    var kids = node.childNodes;
                    if (!kids) return out;
                    for (var i = 0; i < kids.length; i++) {
                        var kid = kids.item ? kids.item(i) : kids[i];
                        if (kid) out.push(kid);
                    }
                    return out;
                }
                function elementChildren(node) {
                    return childNodes(node).filter(function (n) { return n.nodeType === 1; });
                }
                function descendants(node, into) {
                    var list = into || [];
                    var kids = childNodes(node);
                    for (var i = 0; i < kids.length; i++) {
                        list.push(kids[i]);
                        if (kids[i].nodeType === 1 || kids[i].nodeType === 9 || kids[i].nodeType === 11)
                            descendants(kids[i], list);
                    }
                    return list;
                }
                function ancestors(node) {
                    var list = [];
                    for (var n = node.parentNode; n; n = n.parentNode) list.unshift(n);
                    return list;
                }
                function rootOf(node) {
                    var n = node;
                    while (n.parentNode) n = n.parentNode;
                    return n;
                }
                function inDocumentOrder(a, b) {
                    if (a === b) return 0;
                    var position = a.compareDocumentPosition ? a.compareDocumentPosition(b) : 0;
                    if (position & 4) return -1;
                    if (position & 2) return 1;
                    return 0;
                }
                function sortedUnique(nodes) {
                    var out = [];
                    for (var i = 0; i < nodes.length; i++) {
                        if (!nodes[i]) continue;
                        var seen = false;
                        for (var j = 0; j < out.length; j++) if (out[j] === nodes[i]) { seen = true; break; }
                        if (!seen) out.push(nodes[i]);
                    }
                    out.sort(inDocumentOrder);
                    return out;
                }
                function nameOf(node) {
                    if (node.nodeType === 2 || node.nodeType === 1) {
                        // An XPath name is the qualified name as the tree holds it — the local name of an
                        // HTML element, which the DOM reports upper case in nodeName (measured).
                        var prefix = node.prefix || '';
                        var local = node.localName || '';
                        return prefix ? prefix + ':' + local : local;
                    }
                    return node.nodeName || '';
                }
                function localNameOf(node) { return node.localName || String(node.nodeName || '').replace(/^[^:]*:/, ''); }
                function textOf(node) {
                    if (node.nodeType === 2) return node.value || '';
                    if (node.nodeType === 3 || node.nodeType === 4) return node.data || '';
                    if (node.nodeType === 7) return node.data || '';
                    var pieces = '';
                    var kids = childNodes(node);
                    for (var i = 0; i < kids.length; i++) {
                        if (kids[i].nodeType === 2 || kids[i].nodeType === 8) continue;
                        pieces += textOf(kids[i]);
                    }
                    return pieces;
                }
                function normalize(text) { return String(text).replace(/[\t\n\r ]+/g, ' ').replace(/^ | $/g, ''); }

                // ── the expression reader ────────────────────────────────────────────
                function Reader(text) { this.text = String(text); this.at = 0; }
                Reader.prototype.eof = function () { this.skip(); return this.at >= this.text.length; };
                Reader.prototype.peek = function () { this.skip(); return this.text.charAt(this.at); };
                Reader.prototype.skip = function () {
                    while (this.at < this.text.length && /[\t\n\r ]/.test(this.text.charAt(this.at))) this.at++;
                };
                Reader.prototype.word = function () {
                    this.skip();
                    var start = this.at;
                    while (this.at < this.text.length && /[A-Za-z0-9_.\-]/.test(this.text.charAt(this.at))) this.at++;
                    return this.text.slice(start, this.at);
                };
                Reader.prototype.take = function (token) {
                    this.skip();
                    if (this.text.slice(this.at, this.at + token.length) !== token) return false;
                    this.at += token.length;
                    return true;
                };
                Reader.prototype.error = function () { throw new Error('bad xpath'); };

                var AXES = ['ancestor', 'ancestor-or-self', 'attribute', 'child', 'descendant',
                    'descendant-or-self', 'following', 'following-sibling', 'namespace', 'parent',
                    'preceding', 'preceding-sibling', 'self'];

                function readNodeTest(r) {
                    // 'name()', 'text()', 'node()' and friends are a name followed immediately by the
                    // parenthesis, which is also how an axis is introduced — the caller has already taken
                    // the '::' or is reading a bare step, where the same word is a node test only when a
                    // '(' follows it.
                    var save = r.at;
                    var name = r.word();
                    if (name && r.text.charAt(r.at) === '(') {
                        if (name === 'node' || name === 'text' || name === 'comment'
                            || name === 'processing-instruction') {
                            r.at++;
                            if (name === 'processing-instruction' && r.peek() !== ')') r.word();
                            if (!r.take(')')) r.error();
                            return { kind: name };
                        }
                        r.at = save;
                        name = '';
                    }
                    if (!name) {
                        if (r.text.charAt(r.at) === '*') { r.at++; return { kind: 'any' }; }
                        r.error();
                    }
                    // A qualified name is read whole — the prefix, the colon and either the local name
                    // or the wildcard that matches every name in that namespace.
                    if (r.text.charAt(r.at) === ':' && r.text.charAt(r.at + 1) !== ':') {
                        r.at++;
                        if (r.text.charAt(r.at) === '*') {
                            r.at++;
                            return { kind: 'qname', prefix: name, local: '*' };
                        }
                        var local = r.word();
                        if (!local) r.error();
                        return { kind: 'qname', prefix: name, local: local };
                    }
                    return { kind: 'name', value: name };
                }

                function readStep(r) {
                    var save = r.at;
                    // The two abbreviations a step can be written as: '..' is the parent of the node and
                    // a lone '.' is the node itself, which is why 'string(./span)' reads the child of the
                    // context node rather than nothing at all.
                    if (r.text.charAt(r.at) === '.' && r.text.charAt(r.at + 1) === '.') {
                        r.at += 2;
                        return finishStep(r, 'parent', { kind: 'node' });
                    }
                    if (r.text.charAt(r.at) === '.' && !/[0-9]/.test(r.text.charAt(r.at + 1) || '')) {
                        var after = r.text.charAt(r.at + 1) || '';
                        if (after === '' || after === '/' || after === '[') {
                            r.at++;
                            return finishStep(r, 'self', { kind: 'node' });
                        }
                    }
                    var head = r.word();
                    if (r.text.charAt(r.at) === ':' && r.text.charAt(r.at + 1) === ':') {
                        if (AXES.indexOf(head) < 0) r.error();
                        r.at += 2;
                        return finishStep(r, head, readNodeTest(r));
                    }
                    r.at = save;
                    if (r.take('@')) return finishStep(r, 'attribute', readNodeTest(r));
                    return finishStep(r, 'child', readNodeTest(r));
                }
                function finishStep(r, axis, nodeTest) {
                    var predicates = [];
                    while (r.peek() === '[') {
                        r.at++;
                        var predicate = readExpr(r);
                        if (!r.take(']')) r.error();
                        predicates.push(predicate);
                    }
                    return { axis: axis, test: nodeTest, predicates: predicates };
                }

                function readPath(r, inPredicate) {
                    var path = { absolute: false, steps: [] };
                    r.skip();
                    if (r.take('//')) {
                        path.absolute = true;
                        path.steps.push({ axis: 'descendant-or-self', test: { kind: 'node' }, predicates: [] });
                    } else if (r.take('/')) {
                        path.absolute = true;
                        if (r.peek() === '(' || (!/[A-Za-z@*.\-]/.test(r.peek()) && r.peek() !== '')) {
                            return path;
                        }
                    }
                    var guard = 0;
                    for (;;) {
                        if (++guard > 40) r.error();
                        path.steps.push(readStep(r));
                        r.skip();
                        if (r.take('//')) { path.steps.push({ axis: 'descendant-or-self', test: { kind: 'node' }, predicates: [] }); continue; }
                        if (r.take('/')) {
                            if (r.eof() || r.peek() === ']' || r.peek() === ')') break;
                            continue;
                        }
                        break;
                    }
                    return path;
                }

                function readExpr(r) { return readOr(r); }
                function readOr(r) {
                    var left = readAnd(r);
                    for (;;) {
                        if (r.peek() === '|') {
                            r.skip(); r.at++;
                            left = { op: 'union', left: left, right: readAnd(r) };
                            continue;
                        }
                        // 'or' is a word, so it is eaten as a word — taking one character of it left the
                        // rest of the expression to be read as a name.
                        if (eatWord(r, 'or')) {
                            left = { op: 'or', left: left, right: readAnd(r) };
                            continue;
                        }
                        return left;
                    }
                }
                function isWordAt(r, word) {
                    r.skip();
                    var rest = r.text.slice(r.at);
                    return rest.length >= word.length && rest.slice(0, word.length) === word
                        && !/[A-Za-z0-9_.\-]/.test(rest.charAt(word.length) || '');
                }
                function eatWord(r, word) {
                    if (!isWordAt(r, word)) return false;
                    r.skip(); r.at += word.length; return true;
                }
                function readAnd(r) {
                    var left = readEquality(r);
                    while (eatWord(r, 'and')) left = { op: 'and', left: left, right: readEquality(r) };
                    return left;
                }
                function readEquality(r) {
                    var left = readRelational(r);
                    for (;;) {
                        r.skip();
                        if (r.take('=')) left = { op: 'eq', left: left, right: readRelational(r) };
                        else if (r.take('!')) { if (!r.take('=')) r.error(); left = { op: 'ne', left: left, right: readRelational(r) }; }
                        else return left;
                    }
                }
                function readRelational(r) {
                    var left = readAdditive(r);
                    for (;;) {
                        r.skip();
                        if (r.take('<=')) left = { op: 'le', left: left, right: readAdditive(r) };
                        else if (r.peek() === '<' && r.text.charAt(r.at + 1) !== '=') { r.at++; left = { op: 'lt', left: left, right: readAdditive(r) }; }
                        else if (r.take('>=')) left = { op: 'ge', left: left, right: readAdditive(r) };
                        else if (r.peek() === '>') { r.at++; left = { op: 'gt', left: left, right: readAdditive(r) }; }
                        else return left;
                    }
                }
                function readAdditive(r) {
                    var left = readMultiplicative(r);
                    for (;;) {
                        r.skip();
                        if (r.peek() === '+') { r.at++; left = { op: '+', left: left, right: readMultiplicative(r) }; }
                        else if (r.peek() === '-') { r.at++; left = { op: '-', left: left, right: readMultiplicative(r) }; }
                        else return left;
                    }
                }
                function readMultiplicative(r) {
                    var left = readUnary(r);
                    for (;;) {
                        r.skip();
                        if (r.peek() === '*' && !/^[\s)*\]]/.test(r.text.slice(r.at + 1, r.at + 2) + 'x'.slice(0, 0) === '' ? '' : '')) {
                            // 'a * b' is a multiplication; the parser only ever reaches this position
                            // where a '*' cannot be the start of a node test.
                            if (r.text.slice(r.at + 1, r.at + 2) === ':') return left;
                            r.at++; left = { op: '*', left: left, right: readUnary(r) };
                            continue;
                        }
                        if (eatWord(r, 'div')) { left = { op: 'div', left: left, right: readUnary(r) }; continue; }
                        if (eatWord(r, 'mod')) { left = { op: 'mod', left: left, right: readUnary(r) }; continue; }
                        return left;
                    }
                }
                function readUnary(r) {
                    r.skip();
                    if (r.peek() === '-') { r.at++; return { op: 'negate', right: readUnary(r) }; }
                    return readPrimary(r);
                }
                function readPrimary(r) {
                    r.skip();
                    if (r.take('(')) { var inner = readExpr(r); if (!r.take(')')) r.error(); return inner; }
                    var c = r.peek();
                    if (c === '$') r.error();
                    if (/[0-9]/.test(c) || (c === '.' && /[0-9]/.test(r.text.charAt(r.at + 1) || ''))) {
                        var start = r.at;
                        while (r.at < r.text.length && /[0-9.]/.test(r.text.charAt(r.at))) r.at++;
                        return { kind: 'number', value: parseFloat(r.text.slice(start, r.at)) };
                    }
                    if (c === String.fromCharCode(39) || c === String.fromCharCode(34)) {
                        var stop = c, from = r.at + 1;
                        var end = r.text.indexOf(stop, from);
                        if (end < 0) r.error();
                        r.at = end + 1;
                        return { kind: 'string', value: r.text.slice(from, end) };
                    }
                    if (c === '@' || c === '/'
                        || (c === '.' && !/[0-9]/.test(r.text.charAt(r.at + 1) || ''))) {
                        // A '.' or '..' is the start of a relative path, not a whole expression of its
                        // own: 'count(../em)' reads the parent and then the child of it.
                        return { kind: 'path', path: readPath(r, true) };
                    }
                    var save = r.at;
                    var name = r.word();
                    if (!name) r.error();
                    if (r.peek() === '(') {
                        // 'text()', 'comment()', 'processing-instruction()' and 'node()' are node tests
                        // even when no axis is written in front of them, which is how a page writes the
                        // predicate that keeps the element whose text is a given string (measured: it
                        // counts the element).
                        if (name === 'node' || name === 'text' || name === 'comment'
                            || name === 'processing-instruction') {
                            r.at++;
                            if (name === 'processing-instruction' && r.peek() !== ')') r.word();
                            if (!r.take(')')) r.error();
                            return { kind: 'path', path: { absolute: false, steps: [
                                { axis: 'child', test: { kind: name }, predicates: [] }] } };
                        }
                        r.at++;
                        var args = [];
                        if (!r.take(')')) {
                            for (;;) { args.push(readExpr(r)); if (r.take(',')) continue; if (!r.take(')')) r.error(); break; }
                        }
                        return { kind: 'call', name: name, args: args };
                    }
                    if (name === 'and' || name === 'or' || name === 'mod' || name === 'div') r.error();
                    r.at = save;
                    return { kind: 'path', path: readPath(r, true) };
                }

                function parse(expr) {
                    var r = new Reader(expr);
                    var value = readExpr(r);
                    r.skip();
                    if (r.at < r.text.length) throw new Error('trailing text');
                    return value;
                }

                function axisNodes(node, axis) {
                    switch (axis) {
                        case 'child': return childNodes(node);
                        case 'descendant': return descendants(node).filter(function (n) { return n.parentNode !== null; });
                        case 'descendant-or-self': return [node].concat(descendants(node));
                        case 'parent': return node.parentNode ? [node.parentNode] : [];
                        case 'ancestor': return ancestors(node);
                        case 'ancestor-or-self': return ancestors(node).concat([node]);
                        case 'self': return [node];
                        case 'attribute': return attributesOf(node);
                        case 'following-sibling': {
                            var out = [], n = node.nextSibling;
                            while (n) { out.push(n); n = n.nextSibling; }
                            return out;
                        }
                        case 'preceding-sibling': {
                            var back = [], p = node.previousSibling;
                            while (p) { back.unshift(p); p = p.previousSibling; }
                            return back;
                        }
                        case 'following': {
                            var after = [], chain = [node];
                            while (chain.length) {
                                var start = chain.shift();
                                for (var m = start.nextSibling; m; m = m.nextSibling) after.push(m);
                            }
                            var walk = node.parentNode;
                            while (walk) {
                                for (var q = walk.nextSibling; q; q = q.nextSibling) { after.push(q); }
                                walk = walk.parentNode;
                            }
                            return after;
                        }
                        case 'preceding': {
                            var before = [], walk2 = node.parentNode;
                            while (walk2) {
                                for (var k = walk2.previousSibling; k; k = k.previousSibling) before.push(k);
                                walk2 = walk2.parentNode;
                            }
                            return before;
                        }
                        default: return [];
                    }
                }
                function attributesOf(node) {
                    if (!node.attributes || node.nodeType !== 1) return [];
                    var out = [];
                    for (var i = 0; i < node.attributes.length; i++) {
                        var attr = node.attributes.item ? node.attributes.item(i) : node.attributes[i];
                        if (attr) out.push(attr);
                    }
                    return out;
                }
                var RESOLVER = null;
                function namespaceOf(node) { return node.namespaceURI || ''; }
                function testMatches(node, test) {
                    switch (test.kind) {
                        // The wildcard matches any element or any attribute; which of the two a step can
                        // reach is decided by its axis, so 'count(//span/@*)' counts the attributes and
                        // '//*' counts the elements (XPath 1.0 §2.3).
                        case 'any': return node.nodeType === 1 || node.nodeType === 2;
                        case 'name': {
                            if (node.nodeType === 1) return nameOf(node) === test.value;
                            if (node.nodeType === 2) return localNameOf(node) === test.value;
                            return false;
                        }
                        case 'qname': {
                            if (node.nodeType !== 1 && node.nodeType !== 2) return false;
                            var wanted = RESOLVER && typeof RESOLVER.lookupNamespaceURI === 'function'
                                ? RESOLVER.lookupNamespaceURI(test.prefix) : null;
                            // Without a resolver that names the prefix, the qualified name is matched the
                            // way the tree spells it — the prefix the page wrote is the prefix the node
                            // carries. With one, the namespace URI decides and the local name follows.
                            if (wanted === null || wanted === undefined) {
                                return nameOf(node) === test.prefix + ':' + test.local;
                            }
                            if (test.local === '*') return namespaceOf(node) === wanted;
                            return namespaceOf(node) === wanted && localNameOf(node) === test.local;
                        }
                        case 'text': return node.nodeType === 3 || node.nodeType === 4;
                        case 'comment': return node.nodeType === 8;
                        case 'processing-instruction': return node.nodeType === 7;
                        case 'node': return true;
                        default: return false;
                    }
                }
                function stepNodes(nodes, step) {
                    var reached = [];
                    for (var i = 0; i < nodes.length; i++) {
                        var found = axisNodes(nodes[i], step.axis);
                        for (var j = 0; j < found.length; j++)
                            if (testMatches(found[j], step.test)) reached.push(found[j]);
                    }
                    if (!step.predicates.length) return reached;
                    var kept = reached;
                    for (var p = 0; p < step.predicates.length; p++) {
                        var next = [];
                        for (var k = 0; k < kept.length; k++) {
                            var answer = evaluateValue(step.predicates[p], kept[k], k + 1, kept.length);
                            // A predicate whose value is a number keeps the node at that position — the
                            // index the page wrote, not whether the number is truthy (§2.3.5).
                            if (typeof answer === 'number') {
                                if (answer === k + 1) next.push(kept[k]);
                                continue;
                            }
                            if (toBoolean(answer, kept[k])) next.push(kept[k]);
                        }
                        kept = next;
                    }
                    return kept;
                }
                function pathNodes(path, context) {
                    var nodes = path.absolute ? [rootOf(context)] : [context];
                    for (var i = 0; i < path.steps.length; i++) nodes = stepNodes(nodes, path.steps[i]);
                    return nodes;
                }
                function firstOf(nodes) { return nodes.length ? nodes[0] : null; }
                function asNumber(text) {
                    // XPath 1.0 §4.1: a string converts to a number only when the whole of it is one, so
                    // the leading digits of '12abc' do not carry it (measured: 'number' of it is NaN).
                    var trimmed = String(text).replace(/^[\t\n\r ]+|[\t\n\r ]+$/g, '');
                    return /^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$/.test(trimmed)
                        ? parseFloat(trimmed) : NaN;
                }
                function toNumber(value) {
                    if (typeof value === 'number') return value;
                    if (typeof value === 'boolean') return value ? 1 : 0;
                    if (typeof value === 'string') return asNumber(value);
                    if (Array.isArray(value)) return value.length ? asNumber(textOf(value[0])) : NaN;
                    if (value && value.nodeType) return asNumber(textOf(value));
                    return NaN;
                }
                function toStringValue(value) {
                    if (typeof value === 'string') return value;
                    if (typeof value === 'number') return isNaN(value) ? 'NaN' : (value === Infinity ? 'Infinity' : (value === -Infinity ? '-Infinity' : String(value)));
                    if (typeof value === 'boolean') return value ? 'true' : 'false';
                    if (Array.isArray(value)) return value.length ? textOf(value[0]) : '';
                    if (value && value.nodeType) return textOf(value);
                    return '';
                }
                function toBoolean(value, node, context) {
                    if (typeof value === 'boolean') return value;
                    if (typeof value === 'number') return !isNaN(value) && value !== 0;
                    if (typeof value === 'string') return value.length > 0;
                    if (Array.isArray(value)) return value.length > 0;
                    if (value && value.nodeType) return true;
                    return false;
                }
                function callFunction(name, args, ctx, position, size) {
                    function arg(i) { return evaluateValue(args[i], ctx, position, size); }
                    // The one-argument converters take the node-set the page wrote, so their answer comes
                    // from its first node; with no argument at all the context node answers (§4.1).
                    function text(i) { return i < args.length ? toStringValue(arg(i)) : textOf(ctx); }
                    function nodeAt(i) {
                        if (i >= args.length) return ctx;
                        var value = arg(i);
                        if (Array.isArray(value)) return value.length ? value[0] : null;
                        return value && value.nodeType ? value : ctx;
                    }
                    switch (name) {
                        case 'count': {
                            var counted = args.length ? arg(0) : [];
                            return Array.isArray(counted) ? counted.length : 0;
                        }
                        case 'position': return position || 1;
                        case 'last': return size || 1;
                        case 'name': {
                            var named = nodeAt(0);
                            return named ? nameOf(named) : '';
                        }
                        case 'local-name': {
                            var local = nodeAt(0);
                            return local ? localNameOf(local) : '';
                        }
                        case 'string': return text(0);
                        case 'concat': {
                            var joined = '';
                            for (var i = 0; i < args.length; i++) joined += text(i);
                            return joined;
                        }
                        case 'contains': return text(0).indexOf(text(1)) >= 0;
                        case 'starts-with': return text(0).slice(0, text(1).length) === text(1);
                        case 'ends-with': {
                            var tail = text(1);
                            return text(0).slice(-tail.length) === tail;
                        }
                        case 'substring-before': {
                            var source = text(0);
                            var pivot = text(1);
                            var at = source.indexOf(pivot);
                            return at < 0 ? '' : source.slice(0, at);
                        }
                        case 'substring-after': {
                            var from = text(0);
                            var mark = text(1);
                            var found = from.indexOf(mark);
                            return found < 0 ? '' : from.slice(found + mark.length);
                        }
                        case 'substring': {
                            // Positions count from one, and the part asked for past the end of the
                            // string is simply not there (measured: 'substring' of hello from 4 for 5 is
                            // 'lo', not 'lo' padded with spaces).
                            var whole = text(0);
                            var first = Math.round(args.length > 1 ? toNumber(arg(1)) : NaN);
                            var span = args.length > 2 ? Math.round(toNumber(arg(2)))
                                : (isNaN(first) ? NaN : whole.length - first + 1);
                            if (isNaN(first) || isNaN(span)) return '';
                            var picked = '';
                            for (var ci = first; ci < first + span; ci++) {
                                if (ci >= 1 && ci <= whole.length) picked += whole.charAt(ci - 1);
                            }
                            return picked;
                        }
                        case 'string-length': return text(0).length;
                        case 'normalize-space': return normalize(text(0).replace(/[\t\n\r]/g, ' '));
                        case 'number': return 0 < args.length ? toNumber(arg(0)) : toNumber(textOf(ctx));
                        case 'boolean': return toBoolean(arg(0), ctx);
                        case 'not': return !toBoolean(arg(0), ctx);
                        case 'true': return true;
                        case 'false': return false;
                        case 'sum': {
                            var total = 0, list = args.length ? arg(0) : [];
                            if (!Array.isArray(list)) return toNumber(list);
                            for (var m = 0; m < list.length; m++) total += toNumber(textOf(list[m]));
                            return total;
                        }
                        case 'floor': return Math.floor(toNumber(arg(0)));
                        case 'ceiling': return Math.ceil(toNumber(arg(0)));
                        case 'round': return Math.round(toNumber(arg(0)));
                        case 'id': {
                            var document = rootOf(ctx);
                            var wanted = text(0).split(/\s+/);
                            var hits = [];
                            for (var w = 0; w < wanted.length; w++) {
                                if (!wanted[w]) continue;
                                var byId = (document.getElementById || function () { return null; }).call(document, wanted[w]);
                                if (byId) hits.push(byId);
                            }
                            return hits;
                        }
                        default: throw new Error('unknown function ' + name);
                    }
                }
                function evaluateValue(node, context, position, size) {
                    if (!node) return '';
                    switch (node.kind) {
                        case 'number': return node.value;
                        case 'string': return node.value;
                        case 'path': return pathNodes(node.path, context);
                        case 'call':
                            return callFunction(node.name, node.args, context, position, size);
                        default: break;
                    }
                    switch (node.op) {
                        case 'and': return toBoolean(evaluateValue(node.left, context, position, size), context) && toBoolean(evaluateValue(node.right, context, position, size), context);
                        case 'or': return toBoolean(evaluateValue(node.left, context, position, size), context) || toBoolean(evaluateValue(node.right, context, position, size), context);
                        case 'union': {
                            var left = evaluateValue(node.left, context, position, size);
                            var right = evaluateValue(node.right, context, position, size);
                            if (!Array.isArray(left) || !Array.isArray(right)) throw new Error('bad union');
                            return sortedUnique(left.concat(right));
                        }
                        case 'negate': return -toNumber(evaluateValue(node.right, context, position, size));
                        case '+': return toNumber(evaluateValue(node.left, context, position, size)) + toNumber(evaluateValue(node.right, context, position, size));
                        case '-': return toNumber(evaluateValue(node.left, context, position, size)) - toNumber(evaluateValue(node.right, context, position, size));
                        case '*': return toNumber(evaluateValue(node.left, context, position, size)) * toNumber(evaluateValue(node.right, context, position, size));
                        case 'div': return toNumber(evaluateValue(node.left, context, position, size)) / toNumber(evaluateValue(node.right, context, position, size));
                        case 'mod': return toNumber(evaluateValue(node.left, context, position, size)) % toNumber(evaluateValue(node.right, context, position, size));
                        case 'eq': case 'ne': case 'lt': case 'le': case 'gt': case 'ge': {
                            var a = evaluateValue(node.left, context, position, size);
                            var b = evaluateValue(node.right, context, position, size);
                            var bothNodes = Array.isArray(a) || Array.isArray(b);
                            if (Array.isArray(a) && Array.isArray(b)) {
                                // two node sets compare by string-value in XPath: any pair equal?
                                var matched = false;
                                for (var x = 0; x < a.length; x++) for (var y = 0; y < b.length; y++)
                                    if (textOf(a[x]) === textOf(b[y])) matched = true;
                                return node.op === 'eq' ? matched : node.op === 'ne' ? !matched : false;
                            }
                            if (node.op === 'eq' || node.op === 'ne') {
                                var leftText = Array.isArray(a) ? (a.length ? textOf(a[0]) : '') : a;
                                var rightText = Array.isArray(b) ? (b.length ? textOf(b[0]) : '') : b;
                                var equal;
                                if (typeof leftText === 'number' || typeof rightText === 'number')
                                    equal = toNumber(leftText) === toNumber(rightText);
                                else if (typeof leftText === 'boolean' || typeof rightText === 'boolean')
                                    equal = toBoolean(leftText, context) === toBoolean(rightText, context);
                                else equal = String(leftText) === String(rightText);
                                return node.op === 'eq' ? equal : !equal;
                            }
                            return compareNumbers(node.op,
                                Array.isArray(a) ? toNumber(a.length ? textOf(firstOf(a)) : NaN) : toNumber(a),
                                Array.isArray(b) ? toNumber(b.length ? textOf(firstOf(b)) : NaN) : toNumber(b));
                        }
                        default: throw new Error('cannot evaluate');
                    }
                }
                function compareNumbers(op, a, b) {
                    if (op === 'lt') return a < b;
                    if (op === 'le') return a <= b;
                    if (op === 'gt') return a > b;
                    return a >= b;
                }
                function evaluate(expr, node, position, size) {
                    return evaluateValue(expr, node, position, size);
                }

                function XPathResultImpl(type, value, context) {
                    var nodes = Array.isArray(value) ? sortedUnique(value) : null;
                    var iterator = nodes ? nodes.slice() : null;
                    var snapshot = nodes ? nodes.slice() : null;
                    var firstNode = nodes ? (nodes[0] || null) : (isNode(value) ? value : null);
                    this.resultType = type;
                    this.invalidIteratorState = false;
                    Object.defineProperties(this, {
                        iterateNext: { value: function () {
                            if (!iterator) return null;
                            return iterator.length ? iterator.shift() : null;
                        }, writable: true, enumerable: false, configurable: true },
                        snapshotItem: { value: function (index) {
                            if (!snapshot) return null;
                            var at = Number(index);
                            return at >= 0 && at < snapshot.length ? snapshot[at] : null;
                        }, writable: true, enumerable: false, configurable: true }
                    });
                    this.snapshotLength = snapshot ? snapshot.length : 0;
                    this.singleNodeValue = type === FIRST_ORDERED_NODE_TYPE || type === ANY_UNORDERED_NODE_TYPE
                        ? firstNode : null;
                    this.numberValue = type === NUMBER_TYPE
                        ? (nodes ? toNumber(nodes.length ? textOf(nodes[0]) : 'NaN') : toNumber(value)) : NaN;
                    this.stringValue = type === STRING_TYPE
                        ? (nodes ? (nodes.length ? textOf(nodes[0]) : '') : toStringValue(value)) : '';
                    this.booleanValue = type === BOOLEAN_TYPE ? toBoolean(nodes || value, context) : false;
                }
                // The page never builds a result of its own — 'document.evaluate' answers it (measured:
                // 'new XPathResult()' refuses with an illegal-constructor TypeError). The object the walk
                // returns is still an 'XPathResult', so it is made on this function's prototype.
                function XPathResult() {
                    throw new TypeError('Failed to construct ' + quote('XPathResult')
                        + ': Illegal constructor');
                }
                XPathResultImpl.prototype = XPathResult.prototype;
                for (var ri = 0; ri < RESULT_NAMES.length; ri++) {
                    (function (name, value) {
                        try {
                            Object.defineProperty(XPathResult, name,
                                { value: value, writable: false, enumerable: false, configurable: false });
                            Object.defineProperty(XPathResult.prototype, name,
                                { value: value, writable: false, enumerable: false, configurable: false });
                        } catch (e) { }
                    })(RESULT_NAMES[ri], ri);
                }
                // The result the page is handed reads as the interface it is (measured:
                // 'Object.prototype.toString.call(result)' names 'XPathResult').
                try {
                    Object.defineProperty(XPathResult.prototype, Symbol.toStringTag,
                        { value: 'XPathResult', configurable: true });
                } catch (e) { }

                function evaluateExpression(expr, contextNode, resolver, type) {
                    var parsed;
                    try { parsed = parse(expr); } catch (e) { throw refused(expr); }
                    // A name test two steps down still needs the resolver the caller handed in, and the
                    // walk is synchronous, so it is held for the duration of this one expression.
                    var previous = RESOLVER;
                    RESOLVER = resolver && typeof resolver.lookupNamespaceURI === 'function'
                        ? resolver : null;
                    var made;
                    try { made = evaluate(parsed, contextNode, 1, 1); }
                    finally { RESOLVER = previous; }
                    return new XPathResultImpl(type, made, contextNode);
                }
                function NSResolverFor(node) {
                    return {
                        lookupNamespaceURI: function (prefix) {
                            for (var n = node; n && n.nodeType === 1; n = n.parentNode) {
                                var declared = n.getAttribute && n.getAttribute('xmlns:' + prefix);
                                if (declared) return declared;
                            }
                            return null;
                        }
                    };
                }
                function XPathEvaluator() {
                    if (!(this instanceof XPathEvaluator)) {
                        throw new TypeError('Failed to construct ' + quote('XPathEvaluator')
                            + ': Please use the ' + quote('new') + ' operator, please.');
                    }
                }
                XPathEvaluator.prototype.createExpression = function (expr, resolver) {
                    var parsed;
                    try { parsed = parse(expr); } catch (e) {
                        throw new SyntaxError('Failed to execute ' + quote('createExpression') + ' on '
                            + quote('XPathEvaluator') + ': The string ' + quote(text_of(expr))
                            + ' is not a valid XPath expression.');
                    }
                    var owner = this;
                    return {
                        expression: String(expr),
                        resultType: 0,
                        nsResolver: resolver || null,
                        evaluate: function (context, type) {
                            var chosen = type === undefined ? ANY_TYPE : Number(type);
                            return evaluateFor(context, expr, resolver, chosen);
                        }
                    };
                };
                XPathEvaluator.prototype.createNSResolver = function (node) { return NSResolverFor(node); };
                XPathEvaluator.prototype.evaluate = function (expr, context, resolver, type, result) {
                    return evaluateFor(context, expr, resolver, type);
                };
                function evaluateFor(context, expr, resolver, type) {
                    if (!isNode(context)) {
                        throw new TypeError('Failed to execute ' + quote('evaluate') + ' on '
                            + quote('Document') + ': parameter 2 is not of type ' + quote('Node') + '.');
                    }
                    var chosen = type === undefined || type === null ? ANY_TYPE : Number(type);
                    return evaluateExpression(expr, context, resolver, chosen);
                }

                var documentLevel = protoMap['HTMLDocument'] || protoMap['Document'];
                if (documentLevel && !Object.prototype.hasOwnProperty.call(documentLevel, 'evaluate')) {
                    Object.defineProperty(documentLevel, 'evaluate', {
                        value: function (expr, context, resolver, type, result) {
                            return evaluateFor(context || this, expr, resolver, type);
                        },
                        writable: true, enumerable: false, configurable: true
                    });
                }
                if (documentLevel && !Object.prototype.hasOwnProperty.call(documentLevel, 'createNSResolver')) {
                    Object.defineProperty(documentLevel, 'createNSResolver', {
                        value: function (node) { return NSResolverFor(node); },
                        writable: true, enumerable: false, configurable: true
                    });
                }
                if (documentLevel && !Object.prototype.hasOwnProperty.call(documentLevel, 'createExpression')) {
                    Object.defineProperty(documentLevel, 'createExpression', {
                        value: XPathEvaluator.prototype.createExpression,
                        writable: true, enumerable: false, configurable: true
                    });
                }
                window.XPathResult = XPathResult;
                window.XPathEvaluator = XPathEvaluator;
                protoMap.XPathResult = XPathResult.prototype;
                protoMap.XPathEvaluator = XPathEvaluator.prototype;
            })();

            // ── The faces, commands, highlights and drag payload a page asks for ───
            // Four families that need nothing from the engine to answer well: the faces a page declares
            // with 'new FontFace' and the set the document keeps (CSS Font Loading §4.3, §4.4), the
            // editing commands it queries (§5.7), the highlights it registers (CSS Highlight 1) and the
            // payload a drag carries (HTML §5.3.4, Clipboard §3). Each answer here is what the reference
            // was read answering: an empty set is 'loaded' with size 0, a command the interface does not
            // know is false rather than a throw, and a registry member refuses a value that is not a
            // highlight with the sentence that names the parameter.
            (function () {
                var protoMap = window.__domProto || {};
                var eventTargetProto = protoMap['EventTarget']
                    || (window.EventTarget && window.EventTarget.prototype) || Object.prototype;
                var SQ = String.fromCharCode(39);
                var DQ = String.fromCharCode(34);
                function quoted(value) { return SQ + String(value) + SQ; }
                function argumentRefused(api, iface, needed, present) {
                    return new TypeError('Failed to execute ' + quoted(api) + ' on ' + quoted(iface)
                        + ': ' + needed + ' argument' + (needed === 1 ? '' : 's')
                        + ' required, but only ' + present + ' present.');
                }
                function wordOr(value, fallback) {
                    return value === undefined || value === null ? fallback : String(value);
                }
                function unquoted(text) {
                    var value = String(text).trim();
                    if (value.length > 1 && (value.charAt(0) === SQ || value.charAt(0) === DQ))
                        return value.slice(1, value.length - 1);
                    return value;
                }
                function setLike(proto, list) {
                    // The three spellings a Set-like interface answers with are the same function, and
                    // the iteration symbol names the one the reference prints for all of them (measured:
                    // 'fontSet[Symbol.iterator]' reads as the values member).
                    proto.values = function () {
                        var made = list(this);
                        var at = 0;
                        return {
                            next: function () {
                                return at < made.length
                                    ? { value: made[at++], done: false } : { value: undefined, done: true };
                            },
                            [Symbol.iterator]: function () { return this; }
                        };
                    };
                    proto.entries = function () {
                        var made = list(this);
                        var at = 0;
                        return {
                            next: function () {
                                if (at >= made.length) return { value: undefined, done: true };
                                var value = made[at];
                                at++;
                                return { value: [value, value], done: false };
                            },
                            [Symbol.iterator]: function () { return this; }
                        };
                    };
                    proto.keys = proto.values;
                    proto[Symbol.iterator] = proto.values;
                    proto.forEach = function (callback, thisArg) {
                        var made = list(this).slice();
                        for (var i = 0; i < made.length; i++) callback.call(thisArg, made[i], made[i], this);
                    };
                }
                function frozenCopy(items) {
                    // A page that writes to the list it read is refused the way a frozen array refuses
                    // it (measured: 'types.push' throws TypeError).
                    return Object.freeze(items.slice());
                }

                // ── CSS Font Loading §4.4: the face the page declares ───────────────
                function FontFace(family, source, descriptors) {
                    if (!(this instanceof FontFace)) {
                        throw new TypeError('Failed to construct ' + quoted('FontFace') + ': Please use '
                            + 'the ' + quoted('new') + ' operator, this object constructor cannot be '
                            + 'called as a function.');
                    }
                    var init = descriptors || {};
                    this.family = String(family === undefined ? '' : family);
                    this.style = wordOr(init.style, 'normal');
                    this.weight = wordOr(init.weight, 'normal');
                    this.stretch = wordOr(init.stretch, 'normal');
                    this.unicodeRange = wordOr(init.unicodeRange, 'U+0-10FFFF');
                    this.variant = wordOr(init.variant, 'normal');
                    this.featureSettings = wordOr(init.featureSettings, 'normal');
                    this.variationSettings = wordOr(init.variationSettings, 'normal');
                    this.display = wordOr(init.display, 'auto');
                    this.ascentOverride = wordOr(init.ascentOverride, 'normal');
                    this.descentOverride = wordOr(init.descentOverride, 'normal');
                    this.lineGapOverride = wordOr(init.lineGapOverride, 'normal');
                    this.sizeAdjust = wordOr(init.sizeAdjust, 'normal');
                    this.__source = String(source === undefined ? '' : source);
                    this.status = 'unloaded';
                    var settle;
                    this.loaded = new Promise(function (resolve) { settle = resolve; });
                    this.__settle = function (face) { settle(face); };
                }
                // §4.4.2: the face is not fetched — this engine matches a family against the faces the
                // platform already has — so the request answers as loaded at once, which is what a page
                // that waits on it and then reads 'check' needs to see.
                FontFace.prototype.load = function () {
                    var self = this;
                    if (self.status !== 'error') {
                        self.status = 'loaded';
                        self.__settle(self);
                    }
                    return Promise.resolve(self);
                };
                Object.defineProperty(FontFace.prototype, Symbol.toStringTag,
                    { value: 'FontFace', configurable: true });
                window.FontFace = FontFace;

                // ── CSS Font Loading §4.3: the set the document keeps ───────────────
                function FontFaceSet() {
                    this.__faces = [];
                    this.onloading = null;
                    this.onloadingdone = null;
                    this.onloadingerror = null;
                    this.__ready = Promise.resolve(this);
                }
                FontFaceSet.prototype = Object.create(eventTargetProto);
                Object.defineProperty(FontFaceSet.prototype, Symbol.toStringTag,
                    { value: 'FontFaceSet', configurable: true });
                // No 'constructor' of its own: 'FontFaceSet' is not a name the page reaches, so the read
                // walks on to the interface above it (measured: 'document.fonts.constructor.name' is
                // 'EventTarget' while the tag the same object prints is 'FontFaceSet').
                Object.defineProperty(FontFaceSet.prototype, 'status', {
                    get: function () { return 'loaded'; }, enumerable: true, configurable: true
                });
                Object.defineProperty(FontFaceSet.prototype, 'size', {
                    get: function () { return this.__faces.length; }, enumerable: true, configurable: true
                });
                Object.defineProperty(FontFaceSet.prototype, 'ready', {
                    get: function () { return this.__ready; }, enumerable: true, configurable: true
                });
                FontFaceSet.prototype.add = function (face) {
                    if (this.__faces.indexOf(face) < 0) this.__faces.push(face);
                };
                FontFaceSet.prototype.delete = function (face) {
                    var at = this.__faces.indexOf(face);
                    if (at < 0) return false;
                    this.__faces.splice(at, 1);
                    return true;
                };
                FontFaceSet.prototype.clear = function () { this.__faces.length = 0; };
                FontFaceSet.prototype.has = function (face) { return this.__faces.indexOf(face) >= 0; };
                function faceFamilies(text) {
                    // The shorthand the page writes is 'style size/line-height family, family'; the list
                    // is what comes after the last size word, so the names are read from the tail.
                    var parts = String(text === undefined ? '' : text).split(',');
                    var out = [];
                    for (var i = 0; i < parts.length; i++) {
                        var words = parts[i].trim().split(/\s+/);
                        var family = unquoted(words.length ? words[words.length - 1] : '');
                        if (family) out.push(family);
                    }
                    return out;
                }
                function matchingFaces(self, font) {
                    var families = faceFamilies(font).map(function (n) { return n.toLowerCase(); });
                    var found = [];
                    for (var i = 0; i < self.__faces.length; i++) {
                        var face = self.__faces[i];
                        if (families.indexOf(String(face.family).toLowerCase()) >= 0) found.push(face);
                    }
                    return found;
                }
                // §4.3.6: the check answers false while a face the shorthand names still needs loading;
                // a family the set does not hold is one the platform draws with, and that is true
                // (measured: false right after 'add', true again once the face is gone from the set).
                FontFaceSet.prototype.check = function (font, text) {
                    var found = matchingFaces(this, font);
                    for (var i = 0; i < found.length; i++) {
                        if (found[i].status !== 'loaded') return false;
                    }
                    return true;
                };
                FontFaceSet.prototype.load = function (font, text) {
                    if (arguments.length < 1) throw argumentRefused('load', 'FontFaceSet', 1, 0);
                    var found = matchingFaces(this, font);
                    var running = [];
                    for (var i = 0; i < found.length; i++) {
                        if (found[i].status === 'loaded') continue;
                        running.push(found[i].load());
                    }
                    return running.length ? Promise.all(running) : Promise.resolve([]);
                };
                setLike(FontFaceSet.prototype, function (self) { return self.__faces.slice(); });
                var documentLevel = protoMap['HTMLDocument'] || protoMap['Document'];
                if (documentLevel && !Object.prototype.hasOwnProperty.call(documentLevel, 'fonts')) {
                    Object.defineProperty(documentLevel, 'fonts', {
                        get: function () {
                            if (!this.__domFonts) this.__domFonts = new FontFaceSet();
                            return this.__domFonts;
                        },
                        enumerable: true, configurable: true
                    });
                }

                // ── The editing commands the page queries (HTML §5.7) ──────────────
                // The engine has no editing host, so no command does anything: 'execCommand' answers
                // false, the state is false and the value the empty string — which is exactly what the
                // reference answers when there is nothing selected in nothing editable (measured). What
                // the list carries is whether the name is one the interface knows: an unknown command is
                // false rather than a throw, in every one of the four queries.
                var COMMAND_NAMES = {};
                var KNOWN_COMMANDS = ['abort', 'backcolor', 'bold', 'createlink', 'copy', 'cut',
                    'defaultstyle', 'delete', 'deletetablecolumn', 'deletetablerow', 'execselect',
                    'fontname', 'fontsize', 'forecolor', 'formatblock', 'heading', 'indent',
                    'insertimage', 'insertlinebreak', 'insertorderedlist', 'insertparagraph',
                    'inserthorizontalrule', 'insertunorderedlist', 'italic', 'justifycenter',
                    'justifyfull', 'justifyleft', 'justifyright', 'nodeforecolor', 'outdent', 'paste',
                    'redo', 'removeformat', 'selectall', 'strikethrough', 'stylewithcss', 'subscript',
                    'superscript', 'underline', 'undo', 'unlink', 'usequestionnaireblock'];
                for (var ci = 0; ci < KNOWN_COMMANDS.length; ci++) COMMAND_NAMES[KNOWN_COMMANDS[ci]] = true;
                function commandKnown(name) {
                    return Object.prototype.hasOwnProperty.call(COMMAND_NAMES,
                        String(name === undefined ? '' : name).toLowerCase());
                }
                function defineDocumentMethod(level, name, body) {
                    if (!level) return;
                    // An operation on an interface is not enumerable, the same as the attributes beside it.
                    try {
                        Object.defineProperty(level, name, {
                            value: body, writable: true, enumerable: false, configurable: true
                        });
                    } catch (e) { level[name] = body; }
                }
                defineDocumentMethod(documentLevel, 'execCommand', function (commandId, showUI, value) {
                    if (arguments.length < 1) throw argumentRefused('execCommand', 'Document', 1, arguments.length);
                    if (!commandKnown(commandId)) return false;
                    return false;
                });
                defineDocumentMethod(documentLevel, 'queryCommandState', function (name) {
                    if (arguments.length < 1) throw argumentRefused('queryCommandState', 'Document', 1, arguments.length);
                    return false;
                });
                defineDocumentMethod(documentLevel, 'queryCommandSupported', function (name) {
                    if (arguments.length < 1) throw argumentRefused('queryCommandSupported', 'Document', 1, arguments.length);
                    return commandKnown(name);
                });
                defineDocumentMethod(documentLevel, 'queryCommandEnabled', function (name) {
                    if (arguments.length < 1) throw argumentRefused('queryCommandEnabled', 'Document', 1, arguments.length);
                    return commandKnown(name) && false;
                });
                defineDocumentMethod(documentLevel, 'queryCommandValue', function (name) {
                    if (arguments.length < 1) throw argumentRefused('queryCommandValue', 'Document', 1, arguments.length);
                    return '';
                });

                // ── The highlight a page registers (CSS Highlight API 1 §2, §3) ─────
                function Highlight() {
                    if (!(this instanceof Highlight)) {
                        throw new TypeError('Failed to construct ' + quoted('Highlight') + ': Please use '
                            + 'the ' + quoted('new') + ' operator, this object constructor cannot be '
                            + 'called as a function.');
                    }
                    this.__items = [];
                    this.priority = 0;
                    for (var i = 0; i < arguments.length; i++) this.add(arguments[i]);
                }
                Object.defineProperty(Highlight.prototype, 'type', {
                    get: function () { return 'highlight'; }, enumerable: true, configurable: true
                });
                Object.defineProperty(Highlight.prototype, 'size', {
                    get: function () { return this.__items.length; }, enumerable: true, configurable: true
                });
                function sameRange(a, b) {
                    // The set keeps a range once: two ranges drawn over the same span answer one item
                    // for the page (measured: adding the same collapsed range twice leaves 'size' 1).
                    return a === b || (a && b
                        && a.startContainer === b.startContainer && a.startOffset === b.startOffset
                        && a.endContainer === b.endContainer && a.endOffset === b.endOffset);
                }
                Highlight.prototype.add = function () {
                    for (var i = 0; i < arguments.length; i++) {
                        var range = arguments[i];
                        if (range == null) continue;
                        var found = false;
                        for (var k = 0; k < this.__items.length; k++) {
                            if (sameRange(this.__items[k], range)) { found = true; break; }
                        }
                        if (!found) this.__items.push(range);
                    }
                };
                Highlight.prototype.delete = function (range) {
                    for (var k = 0; k < this.__items.length; k++) {
                        if (sameRange(this.__items[k], range)) { this.__items.splice(k, 1); return true; }
                    }
                    return false;
                };
                Highlight.prototype.has = function (range) {
                    for (var k = 0; k < this.__items.length; k++) {
                        if (sameRange(this.__items[k], range)) return true;
                    }
                    return false;
                };
                Highlight.prototype.clear = function () { this.__items.length = 0; };
                setLike(Highlight.prototype, function (self) { return self.__items.slice(); });
                Object.defineProperty(Highlight.prototype, Symbol.toStringTag,
                    { value: 'Highlight', configurable: true });
                window.Highlight = Highlight;

                function HighlightRegistry() {
                    throw new TypeError('Failed to construct ' + quoted('HighlightRegistry')
                        + ': Illegal constructor');
                }
                function RegistryStore(registry) {
                    if (!Object.prototype.hasOwnProperty.call(registry, '__entries')) {
                        Object.defineProperty(registry, '__entries', {
                            value: { names: [], values: {} }, writable: true,
                            enumerable: false, configurable: true
                        });
                    }
                    return registry.__entries;
                }
                Object.defineProperty(HighlightRegistry.prototype, 'size', {
                    get: function () { return RegistryStore(this).names.length; },
                    enumerable: true, configurable: true
                });
                HighlightRegistry.prototype.set = function (name, highlight) {
                    if (arguments.length < 2) throw argumentRefused('set', 'HighlightRegistry', 2, arguments.length);
                    if (!(highlight instanceof Highlight)) {
                        throw new TypeError('Failed to execute ' + quoted('set') + ' on '
                            + quoted('HighlightRegistry') + ': parameter 2 is not of type '
                            + quoted('Highlight') + '.');
                    }
                    var store = RegistryStore(this);
                    var key = String(name);
                    if (store.values[key] === undefined) store.names.push(key);
                    store.values[key] = highlight;
                    return this;
                };
                HighlightRegistry.prototype.get = function (name) {
                    var store = RegistryStore(this);
                    var value = store.values[String(name)];
                    return value === undefined ? undefined : value;
                };
                HighlightRegistry.prototype.has = function (name) {
                    return RegistryStore(this).names.indexOf(String(name)) >= 0;
                };
                HighlightRegistry.prototype.delete = function (name) {
                    var store = RegistryStore(this);
                    var at = store.names.indexOf(String(name));
                    if (at < 0) return false;
                    store.names.splice(at, 1);
                    delete store.values[String(name)];
                    return true;
                };
                HighlightRegistry.prototype.clear = function () {
                    var store = RegistryStore(this);
                    store.names.length = 0;
                    store.values = {};
                };
                // The names walk in the order they were registered, and the pairs answer
                // [name, highlight] — the registry is a map, not the set the highlights themselves are.
                HighlightRegistry.prototype.values = function () {
                    var store = RegistryStore(this);
                    var made = [];
                    for (var i = 0; i < store.names.length; i++) made.push(store.values[store.names[i]]);
                    var at = 0;
                    return { next: function () {
                        return at < made.length
                            ? { value: made[at++], done: false } : { value: undefined, done: true };
                    }, [Symbol.iterator]: function () { return this; } };
                };
                HighlightRegistry.prototype.keys = function () {
                    var store = RegistryStore(this);
                    var made = store.names.slice();
                    var at = 0;
                    return { next: function () {
                        return at < made.length
                            ? { value: made[at++], done: false } : { value: undefined, done: true };
                    }, [Symbol.iterator]: function () { return this; } };
                };
                HighlightRegistry.prototype.entries = function () {
                    var store = RegistryStore(this);
                    var made = [];
                    for (var i = 0; i < store.names.length; i++)
                        made.push([store.names[i], store.values[store.names[i]]]);
                    var at = 0;
                    return { next: function () {
                        return at < made.length
                            ? { value: made[at++], done: false } : { value: undefined, done: true };
                    }, [Symbol.iterator]: function () { return this; } };
                };
                HighlightRegistry.prototype.forEach = function (callback, thisArg) {
                    var names = this.keys();
                    for (var step = names.next(); !step.done; step = names.next()) {
                        callback.call(thisArg, this.get(step.value), step.value, this);
                    }
                };
                HighlightRegistry.prototype[Symbol.iterator] = HighlightRegistry.prototype.entries;
                // §3.3: the highlights painted at a point are the ones whose ranges cover it. Nothing is
                // painted by point here, so the walk answers the empty set the interface returns when no
                // highlight covers the coordinate.
                HighlightRegistry.prototype.highlightsFromPoint = function (x, y) {
                    return new Highlight();
                };
                Object.defineProperty(HighlightRegistry.prototype, Symbol.toStringTag,
                    { value: 'HighlightRegistry', configurable: true });
                window.HighlightRegistry = HighlightRegistry;
                var highlightRegistry = Object.create(HighlightRegistry.prototype);
                if (window.CSS) {
                    try {
                        Object.defineProperty(window.CSS, 'highlights', {
                            value: highlightRegistry, writable: false, enumerable: true, configurable: true
                        });
                    } catch (e) { window.CSS.highlights = highlightRegistry; }
                }

                // ── The payload a drag carries (HTML §5.3.4, Clipboard §3) ──────────
                function DataTransferItem(type, read) {
                    this.__type = String(type);
                    this.__read = read;
                }
                Object.defineProperty(DataTransferItem.prototype, 'kind', {
                    get: function () { return 'string'; }, enumerable: true, configurable: true
                });
                Object.defineProperty(DataTransferItem.prototype, 'type', {
                    get: function () { return this.__type; }, enumerable: true, configurable: true
                });
                // §5.3.4.3: the string is handed to the callback on a task of its own, and a file — the
                // other kind there is — is not what a script-built transfer holds.
                DataTransferItem.prototype.getAsString = function (callback) {
                    if (typeof callback !== 'function') return;
                    var value = this.__read ? this.__read() : '';
                    Promise.resolve().then(function () { callback(value); });
                };
                DataTransferItem.prototype.getAsFile = function () { return null; };
                Object.defineProperty(DataTransferItem.prototype, Symbol.toStringTag,
                    { value: 'DataTransferItem', configurable: true });
                window.DataTransferItem = DataTransferItem;

                function DataTransfer() {
                    this.__order = [];
                    this.__data = {};
                    this.dropEffect = 'none';
                    this.effectAllowed = 'none';
                }
                DataTransfer.prototype.setData = function (type, data) {
                    if (arguments.length < 2) throw argumentRefused('setData', 'DataTransfer', 2, arguments.length);
                    var key = String(type).toLowerCase();
                    // A type that is not a MIME spelling is dropped without a word (§5.3.4.2).
                    if (!/^[a-z0-9!#$%&'*+.^_`|~-]+\/[a-z0-9!#$%&'*+.^_`|~-]+$/.test(key)) return;
                    if (this.__data[key] === undefined) this.__order.push(key);
                    this.__data[key] = String(data);
                };
                DataTransfer.prototype.getData = function (type) {
                    if (arguments.length < 1) throw argumentRefused('getData', 'DataTransfer', 1, arguments.length);
                    var value = this.__data[String(type).toLowerCase()];
                    return value === undefined ? '' : value;
                };
                DataTransfer.prototype.clearData = function (type) {
                    if (arguments.length < 1) {
                        this.__order.length = 0;
                        this.__data = {};
                        return;
                    }
                    var key = String(type).toLowerCase();
                    if (this.__data[key] === undefined) return;
                    delete this.__data[key];
                    var at = this.__order.indexOf(key);
                    if (at >= 0) this.__order.splice(at, 1);
                };
                // The image the drag shows beside the cursor is picked by the shell, not the script.
                DataTransfer.prototype.setDragImage = function () { };
                Object.defineProperty(DataTransfer.prototype, 'types', {
                    get: function () { return frozenCopy(this.__order); },
                    enumerable: true, configurable: true
                });
                Object.defineProperty(DataTransfer.prototype, 'items', {
                    get: function () {
                        var made = [];
                        for (var i = 0; i < this.__order.length; i++) {
                            (function (name, owner) {
                                made.push(new DataTransferItem(name, function () {
                                    var value = owner.__data[name];
                                    return value === undefined ? '' : value;
                                }));
                            })(this.__order[i], this);
                        }
                        return frozenCopy(made);
                    },
                    enumerable: true, configurable: true
                });
                Object.defineProperty(DataTransfer.prototype, 'files', {
                    // A script-built transfer holds no files; the list is the empty one the reference
                    // answers, and it is a FileList the page can read 'length' and 'item' from.
                    get: function () { return EmptyFileList(); },
                    enumerable: true, configurable: true
                });
                Object.defineProperty(DataTransfer.prototype, Symbol.toStringTag,
                    { value: 'DataTransfer', configurable: true });
                window.DataTransfer = DataTransfer;

                function EmptyFileList() {
                    if (!window.__domEmptyFiles) {
                        var list = [];
                        list.item = function () { return null; };
                        Object.defineProperty(list, Symbol.toStringTag,
                            { value: 'FileList', configurable: true });
                        window.__domEmptyFiles = Object.freeze(list);
                    }
                    return window.__domEmptyFiles;
                }

                // ── The clipboard item a page hands over (Clipboard §3.3) ───────────
                var CLIPBOARD_TYPES = { 'text/plain': true, 'text/html': true };
                function ClipboardItem(items) {
                    if (!(this instanceof ClipboardItem)) {
                        throw new TypeError('Failed to construct ' + quoted('ClipboardItem') + ': Please '
                            + 'use the ' + quoted('new') + ' operator, this object constructor cannot be '
                            + 'called as a function.');
                    }
                    if (items === undefined || items === null) {
                        throw new TypeError('Failed to construct ' + quoted('ClipboardItem') + ': 1 '
                            + 'argument required, but only 0 present.');
                    }
                    var dict = items;
                    var names = Object.keys(dict);
                    names.sort();
                    this.__types = names;
                    this.__data = dict;
                }
                Object.defineProperty(ClipboardItem.prototype, 'types', {
                    get: function () { return frozenCopy(this.__types); },
                    enumerable: true, configurable: true
                });
                ClipboardItem.prototype.getType = function (type) {
                    if (arguments.length < 1) {
                        throw argumentRefused('getType', 'ClipboardItem', 1, arguments.length);
                    }
                    var name = String(type);
                    var value = this.__data[name];
                    if (value === undefined) {
                        return Promise.reject(new DOMException('The data of the specified type was not '
                            + 'found.', 'NotFoundError'));
                    }
                    return Promise.resolve(typeof value.then === 'function' ? value : value);
                };
                // §3.3.1: the one spelling the engine can hand over is the one it can build a blob for.
                ClipboardItem.supports = function (type) {
                    if (arguments.length < 1) {
                        throw argumentRefused('supports', 'ClipboardItem', 1, arguments.length);
                    }
                    return CLIPBOARD_TYPES[String(type).toLowerCase()] === true;
                };
                Object.defineProperty(ClipboardItem.prototype, Symbol.toStringTag,
                    { value: 'ClipboardItem', configurable: true });
                window.ClipboardItem = ClipboardItem;

                // ── Web Storage §5: the named items the page writes ────────────────
                // A storage area is an exotic object: every name the page gives it is an item, so
                // 'localStorage.b = 2' writes one, 'Object.keys' lists the ones that are there in the
                // order they went in, and a name the interface itself owns is the member, not an item.
                // The host holds the items; this is the face the page reads them through.
                var STORAGE_MEMBERS = { getItem: 1, setItem: 1, removeItem: 1, clear: 1, key: 1, length: 1 };
                var storagePairs = [];
                function storageHostOf(obj) {
                    for (var p = 0; p < storagePairs.length; p++)
                        if (storagePairs[p].object === obj) return storagePairs[p].host;
                    return null;
                }
                function Storage() {
                    throw new TypeError('Failed to construct ' + quoted('Storage')
                        + ': Illegal constructor');
                }
                Storage.prototype.getItem = function (key) {
                    var host = storageHostOf(this);
                    return host === null ? null : host.getItem(String(key));
                };
                Storage.prototype.setItem = function (key, value) {
                    var host = storageHostOf(this);
                    if (host !== null) host.setItem(String(key), String(value));
                };
                Storage.prototype.removeItem = function (key) {
                    var host = storageHostOf(this);
                    if (host !== null) host.removeItem(String(key));
                };
                Storage.prototype.clear = function () {
                    var host = storageHostOf(this);
                    if (host !== null) host.clear();
                };
                Storage.prototype.key = function (index) {
                    var host = storageHostOf(this);
                    return host === null ? null : host.key(Number(index) | 0);
                };
                Object.defineProperty(Storage.prototype, 'length', {
                    get: function () {
                        var host = storageHostOf(this);
                        return host === null ? 0 : host.length;
                    },
                    enumerable: true, configurable: true
                });
                Object.defineProperty(Storage.prototype, Symbol.toStringTag,
                    { value: 'Storage', configurable: true });
                window.Storage = Storage;

                function storageNamed(host) {
                    if (typeof Proxy !== 'function') return host;
                    // An ordinary object stands in for the exotic one: the trap rules a proxy must keep
                    // are about the target it wraps, and the items are the only properties that should
                    // answer as its own. The host is held in the closure and read through the traps.
                    var target = { };
                    // The interface's methods are given as script functions over the host rather than the
                    // host's own members: a host method asked for through a proxy gets a receiver that is
                    // not the host, and the interop layer cannot make a CLR object out of that.
                    var members = {
                        getItem: function (key) {
                            var value = host.getItem(String(key));
                            return value === null || value === undefined ? null : value;
                        },
                        setItem: function (key, value) { host.setItem(String(key), String(value)); },
                        removeItem: function (key) { host.removeItem(String(key)); },
                        clear: function () { host.clear(); },
                        key: function (index) {
                            var value = host.key(Number(index) | 0);
                            return value === null || value === undefined ? null : value;
                        }
                    };
                    function readMember(key) {
                        if (key === 'length') return typeof host.length === 'number' ? host.length : 0;
                        return members[key];
                    }
                    var named = new Proxy(target, {
                        get: function (unused, key) {
                            if (key === Symbol.toStringTag) return 'Storage';
                            if (typeof key !== 'string') return host[key];
                            if (STORAGE_MEMBERS[key] === 1) return readMember(key);
                            var value = host.getItem(key);
                            // A name that is not an item reads as the missing property, not as the null
                            // the host's 'getItem' answers (measured: 'localStorage.a' after a delete).
                            return value === null || value === undefined ? undefined : value;
                        },
                        set: function (unused, key, value) {
                            if (typeof key !== 'string') { target[key] = value; return true; }
                            // A name the interface owns is not an item, so writing it stores nothing
                            // (measured: 'localStorage.length = 5' leaves the count where it was).
                            if (STORAGE_MEMBERS[key] === 1) return true;
                            host.setItem(key, String(value));
                            return true;
                        },
                        deleteProperty: function (unused, key) {
                            if (typeof key === 'string' && STORAGE_MEMBERS[key] !== 1) host.removeItem(key);
                            return true;
                        },
                        has: function (unused, key) {
                            if (typeof key !== 'string') return key in target;
                            if (STORAGE_MEMBERS[key] === 1) return true;
                            var value = host.getItem(key);
                            return value !== null && value !== undefined;
                        },
                        ownKeys: function () {
                            var out = [];
                            var count = typeof host.length === 'number' ? host.length : 0;
                            if (typeof host.key !== 'function') return out;
                            for (var i = 0; i < count; i++) {
                                var name = host.key(i);
                                if (name !== null && name !== undefined) out.push(String(name));
                            }
                            return out;
                        },
                        getOwnPropertyDescriptor: function (unused, key) {
                            if (typeof key !== 'string' || STORAGE_MEMBERS[key] === 1) return undefined;
                            var value = host.getItem(key);
                            if (value === null || value === undefined) return undefined;
                            return { value: value, writable: true, enumerable: true, configurable: true };
                        },
                        getPrototypeOf: function () { return Storage.prototype; }
                    });
                    storagePairs.push({ object: named, host: host });
                    // The pairs only have to answer for the areas this document hands out; a page that
                    // kept an object from a document it left behind has nowhere to read from anyway.
                    while (storagePairs.length > 4) storagePairs.shift();
                    return named;
                }
                // The areas were handed out as the host objects, so they are dressed here rather than
                // where they were set; the setup script runs once per document, and each document gets
                // one proxy per area that answers the same host every time it is read.
                if (window.localStorage) window.localStorage = storageNamed(window.localStorage);
                if (window.sessionStorage) window.sessionStorage = storageNamed(window.sessionStorage);
            })();

            // ── The collections, the media list and the legacy all-list (§2.8, MQ §3.1, HTML §4.10) ──
            // The interfaces the engine hands out as CLR objects still answer as the interface they
            // are: a level in the map the stamping reads gives a NodeList its 'instanceof', its
            // 'constructor' and the tag the page prints. Nothing here is constructible — a page builds
            // none of these itself (measured: 'new FileList()' and 'new HTMLCollection()' both refuse).
            (function () {
                var protoMap = window.__domProto || {};
                var SQ = String.fromCharCode(39);
                function quoted(value) { return SQ + String(value) + SQ; }
                function refuse(name) {
                    return function () {
                        throw new TypeError('Failed to construct ' + quoted(name)
                            + ': Illegal constructor');
                    };
                }
                // The level the interface owns in the map the stamping reads. A name the page cannot
                // build is registered with a constructor that refuses; a name the script builds itself
                // keeps its own function and only joins the map.
                function level(name, baseName, Ctor) {
                    var base = baseName ? protoMap[baseName] : null;
                    var proto = protoMap[name] || Object.create(base || Object.prototype);
                    if (!protoMap[name]) protoMap[name] = proto;
                    var Built = Ctor || refuse(name);
                    try { Object.defineProperty(Built, 'name', { value: name, configurable: true }); }
                    catch (e) { }
                    Built.prototype = proto;
                    try {
                        Object.defineProperty(proto, 'constructor',
                            { value: Built, writable: true, configurable: true });
                        Object.defineProperty(proto, Symbol.toStringTag,
                            { value: name, writable: false, enumerable: false, configurable: true });
                    } catch (e) { }
                    window[name] = Built;
                    return proto;
                }
                function adopt(name, baseName, Ctor) {
                    var base = baseName ? protoMap[baseName] : null;
                    if (base) { try { Object.setPrototypeOf(Ctor.prototype, base); } catch (e) { } }
                    try {
                        Object.defineProperty(Ctor.prototype, Symbol.toStringTag,
                            { value: name, writable: false, enumerable: false, configurable: true });
                    } catch (e) { }
                    protoMap[name] = Ctor.prototype;
                    if (!window[name]) window[name] = Ctor;
                    return Ctor.prototype;
                }
                function member(target, key, value) {
                    if (!target) return;
                    try {
                        Object.defineProperty(target, key,
                            { value: value, writable: true, enumerable: false, configurable: true });
                    } catch (e) { target[key] = value; }
                }

                // The collection family (DOM §4.5.2, §5.1). A NodeList and an HTMLCollection share the
                // reads a page uses — 'length', 'item', the index — and the HTMLCollection adds the
                // named read; the form's controls add it again with the id-or-name rule the collection
                // keeps, so it sits above the named item list rather than beside it.
                var nodeList = level('NodeList', null, refuse('NodeList'));
                member(nodeList, 'item', function (index) {
                    var at = Number(index) | 0;
                    return at >= 0 && at < this.length ? this[at] : null;
                });
                member(nodeList, 'forEach', function (callback, thisArg) {
                    for (var i = 0; i < this.length; i++) callback.call(thisArg, this[i], i, this);
                });
                member(nodeList, 'keys', function () {
                    var self = this, out = [], at = 0;
                    for (var i = 0; i < self.length; i++) out.push(i);
                    return { next: function () {
                        return at < out.length
                            ? { value: out[at++], done: false } : { value: undefined, done: true };
                    }, [Symbol.iterator]: function () { return this; } };
                });
                member(nodeList, 'values', function () {
                    var self = this, at = 0;
                    return { next: function () {
                        return at < self.length
                            ? { value: self[at++], done: false } : { value: undefined, done: true };
                    }, [Symbol.iterator]: function () { return this; } };
                });
                member(nodeList, 'entries', function () {
                    var self = this, at = 0;
                    return { next: function () {
                        if (at >= self.length) return { value: undefined, done: true };
                        var pair = [at, self[at]]; at++;
                        return { value: pair, done: false };
                    }, [Symbol.iterator]: function () { return this; } };
                });
                nodeList[Symbol.iterator] = nodeList.values;

                var collection = level('HTMLCollection', null, refuse('HTMLCollection'));
                Object.setPrototypeOf(collection, nodeList);
                member(collection, 'namedItem', function (name) {
                    var wanted = String(name === undefined ? '' : name);
                    for (var i = 0; i < this.length; i++) {
                        var el = this[i];
                        if (el && String(el.id === undefined ? '' : el.id) === wanted) return el;
                    }
                    for (var j = 0; j < this.length; j++) {
                        var item = this[j];
                        if (item && item.getAttribute && String(item.getAttribute('name') || '') === wanted)
                            return item;
                    }
                    return null;
                });

                var formControls = level('HTMLFormControlsCollection', 'HTMLCollection',
                    refuse('HTMLFormControlsCollection'));
                member(formControls, 'namedItem', function (name) {
                    var wanted = String(name === undefined ? '' : name);
                    var list = [];
                    for (var i = 0; i < this.length; i++) {
                        var el = this[i];
                        if (!el) continue;
                        if (String(el.id === undefined ? '' : el.id) === wanted) return el;
                        if (el.getAttribute && String(el.getAttribute('name') || '') === wanted) list.push(el);
                    }
                    if (!list.length) return null;
                    if (list.length === 1) return list[0];
                    // More than one control carries the name: the answer is the radio group, which is
                    // itself the list with a length (HTML §4.10.22).
                    var group = list.slice();
                    group.length = list.length;
                    group.item = function (at) { return group[Number(at) | 0] || null; };
                    group.value = list[0].value;
                    return group;
                });

                level('RadioNodeList', 'NodeList', refuse('RadioNodeList'));
                level('FileList', null);
                level('NamedNodeMap', null, refuse('NamedNodeMap'));
                level('HTMLAllCollection', null, refuse('HTMLAllCollection'));
                // The four the script builds keep the constructors the page already calls — the level
                // only joins the map so 'instanceof' and the tag read the interface.
                adopt('FormData', null, window.FormData);
                adopt('Blob', null, window.Blob);
                adopt('File', 'Blob', window.File);
                adopt('FileReader', 'EventTarget', window.FileReader);
                level('MediaQueryList', 'EventTarget', refuse('MediaQueryList'));
                level('MediaQueryListEvent', 'Event', function (type, init) {
                    var event = new Event(type, init);
                    init = init || { };
                    // The event reads as the interface it was built for, the same way every other
                    // family does, so the level is hung under it before the members are written.
                    var ownLevel = protoMap['MediaQueryListEvent'];
                    if (ownLevel) { try { Object.setPrototypeOf(event, ownLevel); } catch (e) { } }
                    try {
                        Object.defineProperty(event, 'media', {
                            value: String(init.media === undefined ? '' : init.media),
                            writable: true, enumerable: false, configurable: true
                        });
                        Object.defineProperty(event, 'matches', {
                            value: !!init.matches, writable: true, enumerable: false, configurable: true
                        });
                    } catch (e) { }
                    return event;
                });
                level('Clipboard', 'EventTarget', refuse('Clipboard'));

                // A page cannot build a media list of its own either, but it can hold one from
                // 'matchMedia', so the interface level carries the reads and the dispatch point.
                var mediaLevel = protoMap['MediaQueryList'];
                if (mediaLevel) {
                    member(mediaLevel, 'dispatchEvent', function (event) {
                        if (event == null) {
                            throw new TypeError('Failed to execute ' + quoted('dispatchEvent') + ' on '
                                + quoted('MediaQueryList') + ': parameter 1 is not of type '
                                + quoted('Event') + '.');
                        }
                        if (typeof EventTarget === 'function' && this instanceof EventTarget)
                            return EventTarget.prototype.dispatchEvent.call(this, event);
                        return true;
                    });
                }

                // ── The media checkpoint (CSSOM View §7.1, MQ §3.1) ────────────────
                // A list keeps the answer it gave the last time it was asked; at the checkpoint the
                // engine reads the viewport again and tells the lists whose answer moved. That is how
                // a page that resized the window without leaving the script learns about it.
                var mediaLists = [];
                function fireMediaChange(list, matches) {
                    var event;
                    try {
                        event = new MediaQueryListEvent('change', { media: list.media, matches: matches });
                    } catch (e) { return; }
                    try { event.target = list; } catch (e) { }
                    try { list.dispatchEvent(event); } catch (e) { }
                    if (typeof list.onchange === 'function') {
                        try { list.onchange(event); } catch (e) { }
                    }
                }
                window.__mediaLists = mediaLists;
                window.__mediaCheckpoint = function () {
                    for (var i = mediaLists.length - 1; i >= 0; i--) {
                        var list = mediaLists[i];
                        if (!list) { mediaLists.splice(i, 1); continue; }
                        var matches = false;
                        try { matches = !!list.matches; } catch (e) { continue; }
                        if (list.__last === undefined) { list.__last = matches; continue; }
                        if (list.__last === matches) continue;
                        list.__last = matches;
                        fireMediaChange(list, matches);
                    }
                };
                // A list made before this block ran still needs registering; 'matchMedia' pushes on the
                // way out, and the lists it made earlier are picked up here once.
                window.__mediaAdd = function (list) { mediaLists.push(list); };

                // ── 'document.all', the legacy collection every old page reads (HTML §4.10.21) ──
                // It is the one object in the platform that is both a collection and callable: an
                // argument is the name or the index and answers the element, a bare 'length' walks the
                // tree. The reference also makes 'typeof' answer 'undefined' and a boolean test answer
                // false, which is a rule the language carries inside the object and no script value can
                // be built to repeat — so those two read as the ordinary answer here, and everything
                // the page does with the collection works.
                function allElements() {
                    var out = [];
                    function walk(node) {
                        var kids = node.children;
                        var count = kids && typeof kids.length === 'number' ? kids.length : 0;
                        for (var i = 0; i < count; i++) {
                            var kid = kids.item ? kids.item(i) : kids[i];
                            if (!kid) continue;
                            out.push(kid);
                            walk(kid);
                        }
                    }
                    // The collection covers the document, and the document element is part of it
                    // (measured: 'document.all[0]' is the html element itself).
                    if (document.documentElement) out.push(document.documentElement);
                    walk(document.documentElement);
                    return out;
                }
                function allNamed(list, name) {
                    var wanted = String(name === undefined ? '' : name);
                    var hits = [];
                    for (var i = 0; i < list.length; i++) {
                        var el = list[i];
                        if (String(el.id === undefined ? '' : el.id) === wanted) hits.push(el);
                        else if (el.getAttribute && String(el.getAttribute('name') || '') === wanted)
                            hits.push(el);
                    }
                    if (!hits.length) return null;
                    return hits.length === 1 ? hits[0] : hits;
                }
                var allCollection = new Proxy(function () { }, {
                    apply: function (target, thisArg, args) {
                        var list = allElements();
                        if (!args.length) return list;
                        var first = args[0];
                        if (typeof first === 'number') return list[first] === undefined ? null : list[first];
                        var text = String(first);
                        if (/^[0-9]+$/.test(text)) return list[Number(text)] === undefined ? null : list[Number(text)];
                        return allNamed(list, text);
                    },
                    get: function (target, key) {
                        var list = allElements();
                        if (key === Symbol.toStringTag) return 'HTMLAllCollection';
                        if (key === 'length') return list.length;
                        if (key === 'item') return function (index) {
                            var at = Number(index) | 0;
                            return list[at] === undefined ? null : list[at];
                        };
                        if (key === 'namedItem') return function (name) { return allNamed(allElements(), name); };
                        if (key === 'call' || key === 'constructor' || key === 'apply')
                            return target[key];
                        if (typeof key === 'string' && /^[0-9]+$/.test(key))
                            return list[Number(key)] === undefined ? undefined : list[Number(key)];
                        return undefined;
                    },
                    has: function (target, key) {
                        if (key === 'length' || key === 'item' || key === 'namedItem') return true;
                        return typeof key === 'string' && /^[0-9]+$/.test(key)
                            && Number(key) < allElements().length;
                    },
                    ownKeys: function () {
                        var out = [];
                        var list = allElements();
                        for (var i = 0; i < list.length; i++) out.push(String(i));
                        out.push('length');
                        return out;
                    },
                    getOwnPropertyDescriptor: function (target, key) {
                        var list = allElements();
                        if (key === 'length')
                            return { value: list.length, writable: false, enumerable: false, configurable: true };
                        if (typeof key === 'string' && /^[0-9]+$/.test(key) && Number(key) < list.length)
                            return { value: list[Number(key)], writable: false, enumerable: true, configurable: true };
                        return Reflect.getOwnPropertyDescriptor(target, key);
                    }
                });
                if (window.HTMLAllCollection) {
                    try {
                        Object.setPrototypeOf(allCollection, window.HTMLAllCollection.prototype);
                    } catch (e) { }
                }
                // The media list keeps the two spellings it carried before the event-target pair
                // arrived, so a page written against either registers the same callback.
                var mediaListenerLevel = protoMap['MediaQueryList'];
                if (mediaListenerLevel) {
                    member(mediaListenerLevel, 'addListener', function (callback) {
                        if (typeof callback === 'function') this.addEventListener('change', callback);
                    });
                    member(mediaListenerLevel, 'removeListener', function (callback) {
                        this.removeEventListener('change', callback);
                    });
                }
                var documentLevel = protoMap['HTMLDocument'] || protoMap['Document'];
                if (documentLevel && !Object.prototype.hasOwnProperty.call(documentLevel, 'all')) {
                    // The member belongs to the interface, so it is installed where the document's own
                    // accessors live — 'document.defaultView' and 'document.fonts' are read the same way.
                    try {
                        Object.defineProperty(documentLevel, 'all', {
                            get: function () { return allCollection; },
                            enumerable: true, configurable: true
                        });
                    } catch (e) { }
                }

                // ── What the URL surface still owes (URL §2.5, §4.6) ──────────────
                // 'URL.fromJSON' parses a record the page read from somewhere and answers a url or
                // null — never a throw — and 'sort' puts a query's entries back in key order, keeping
                // the order the pairs came in when the key is the same (WHATWG sorts by key only).
                if (window.URL && !URL.fromJSON) {
                    URL.fromJSON = function (json) {
                        var record = json;
                        if (typeof record === 'string') {
                            try { record = JSON.parse(record); } catch (e) { return null; }
                        }
                        if (!record || typeof record !== 'object') return null;
                        var href = record.href === undefined ? '' : String(record.href);
                        var base = record.base === undefined ? undefined : String(record.base);
                        try {
                            return base ? new URL(href, base) : new URL(href);
                        } catch (e) { return null; }
                    };
                }

                // ── The image the page makes with 'new Image()' (HTML §4.8.11) ──────
                // The rest of what the interface carries: the lazy-loading and decoding keywords, the
                // source the loader settled on, and the promise that says the picture is ready. This
                // engine has no decoder to wait on, so the read answers the default the attribute does
                // not carry and 'decode' resolves at once (measured: a fresh image is already complete
                // with no natural size, and 'loading' and 'decoding' read 'auto').
                var imageLevel = protoMap['HTMLImageElement'];
                function imageKeyword(key, allowed, fallback) {
                    return {
                        get: function () {
                            var written = this.getAttribute(key);
                            var text = String(written === null || written === undefined ? '' : written).toLowerCase();
                            for (var i = 0; i < allowed.length; i++) if (allowed[i] === text) return text;
                            return fallback;
                        },
                        set: function (value) {
                            var text = String(value === undefined ? '' : value).toLowerCase();
                            var hit = fallback;
                            for (var i = 0; i < allowed.length; i++) if (allowed[i] === text) hit = text;
                            this.setAttribute(key, hit);
                        },
                        enumerable: true, configurable: true
                    };
                }
                function imageText(key) {
                    return {
                        get: function () {
                            var written = this.getAttribute(key);
                            return written === null || written === undefined ? '' : String(written);
                        },
                        set: function (value) { this.setAttribute(key, String(value === undefined ? '' : value)); },
                        enumerable: true, configurable: true
                    };
                }
                if (imageLevel) {
                    try {
                        Object.defineProperties(imageLevel, {
                            loading: imageKeyword('loading', ['lazy', 'eager'], 'auto'),
                            decoding: imageKeyword('decoding', ['sync', 'async', 'auto'], 'auto'),
                            referrerPolicy: imageKeyword('referrerPolicy',
                                ['', 'no-referrer', 'no-referrer-when-downgrade', 'origin',
                                    'origin-when-cross-origin', 'same-origin', 'strict-origin',
                                    'strict-origin-when-cross-origin', 'unsafe-url'], ''),
                            srcset: imageText('srcset'),
                            sizes: imageText('sizes'),
                            currentSrc: { get: function () { return ''; }, enumerable: true, configurable: true },
                            x: { get: function () { return 0; }, set: function () { },
                                enumerable: true, configurable: true }
                        });
                    } catch (e) { }
                    member(imageLevel, 'decode', function () { return Promise.resolve(undefined); });
                }

                // ── The window's own handler members (HTML §6.5, §8.4, §8.7) ──────
                // An 'on' member the page has not written is null, not missing — a page that reads
                // 'window.onstorage' before adding a listener has to see the handler that is not there
                // yet (measured), and the storage area never fires one at the window that wrote it
                // (Web Storage §5.2: the notification goes to the other documents).
                var windowHandlers = ['onabort', 'onauxclick', 'onbeforeinput', 'onbeforematch',
                    'onbeforetoggle', 'onblur', 'oncancel', 'oncanplay', 'oncanplaythrough', 'onchange',
                    'onclick', 'onclose', 'oncommand', 'oncontextlost', 'oncontextmenu', 'oncontextrestored',
                    'oncopy', 'oncuechange', 'oncut', 'ondblclick', 'ondrag', 'ondragend', 'ondragenter',
                    'ondragleave', 'ondragover', 'ondragstart', 'ondrop', 'ondurationchange', 'onemptied',
                    'onended', 'onerror', 'onfocus', 'onfocusin', 'onfocusout', 'onformdata', 'onhashchange',
                    'oninput', 'oninvalid', 'onkeydown', 'onkeypress', 'onkeyup', 'onlanguagechange',
                    'onload', 'onloadeddata', 'onloadedmetadata', 'onloadstart', 'onmessage',
                    'onmessageerror', 'onmousedown', 'onmouseenter', 'onmouseleave', 'onmousemove',
                    'onmouseout', 'onmouseover', 'onmouseup', 'onwheel', 'onoffline', 'ononline',
                    'onpagehide', 'onpageshow', 'onpaste', 'onpause', 'onplay', 'onplaying',
                    'onpopstate', 'onprogress', 'onratechange', 'onrejectionhandled', 'onreset',
                    'onresize', 'onscroll', 'onscrollend', 'onsecuritypolicyviolation', 'onseeked',
                    'onseeking', 'onselect', 'onslotchange', 'onstalled', 'onstorage', 'onsubmit',
                    'onsuspend', 'ontimeupdate', 'ontoggle', 'onunhandledrejection', 'onunload',
                    'onvolumechange', 'onwaiting'];
                for (var wh = 0; wh < windowHandlers.length; wh++) {
                    (function (handler) {
                        if (Object.prototype.hasOwnProperty.call(window, handler)) return;
                        try {
                            Object.defineProperty(window, handler, {
                                get: function () { return window['__' + handler] === undefined ? null : window['__' + handler]; },
                                set: function (value) { window['__' + handler] = value === undefined ? null : value; },
                                enumerable: true, configurable: true
                            });
                        } catch (e) { }
                    })(windowHandlers[wh]);
                }
            })();

            // ── ES2024 Array.prototype.group (Array grouping §2.3) ──────────────
            // The grouping the page asked for, in the order the elements came, with the same property
            // names and the null-prototype object Object.groupBy answers.
            if (!Array.prototype.group) {
                Object.defineProperty(Array.prototype, 'group', {
                    value: function (callbackfn, thisArg) {
                        if (typeof callbackfn !== 'function') {
                            throw new TypeError('callbackfn is not a function');
                        }
                        var out = Object.create(null);
                        var len = this.length >>> 0;
                        for (var i = 0; i < len; i++) {
                            if (!(i in this)) continue;
                            var key = String(callbackfn.call(thisArg, this[i], i, this));
                            if (!Object.prototype.hasOwnProperty.call(out, key)) out[key] = [];
                            out[key].push(this[i]);
                        }
                        return out;
                    },
                    writable: true, configurable: true
                });
            }

            // ── The accessors an interface declares that the host does not model ─
            // A host object answers its own members first, so a getter installed on the interface's
            // prototype is the place for what the engine reports as a constant or builds on the spot
            // (HTML §7.3, Fullscreen §5, CSSOM View §9, URL §2.5).
            (function () {
                var protoMap = window.__domProto || {};
                function accessor(iface, name, get, isAsync) {
                    var proto = protoMap[iface];
                    if (!proto) return;
                    try {
                        Object.defineProperty(proto, name, {
                            configurable: true, enumerable: true,
                            get: get,
                            set: isAsync ? undefined : function () { }
                        });
                    } catch (e) { }
                }
                accessor('Document', 'fullscreenElement', function () { return null; });
                accessor('Document', 'fullscreenEnabled', function () { return false; });
                accessor('Document', 'pictureInPictureElement', function () { return null; });
                accessor('Document', 'pictureInPictureEnabled', function () { return false; });
                accessor('Document', 'pointerLockElement', function () { return null; });
                // Web Animations §4.6: one timeline per document, and its time is the clock the host
                // runs its animations from.
                var timelines = new WeakMap();
                accessor('Document', 'timeline', function () {
                    var doc = this;
                    var made = timelines.get(doc);
                    if (!made) {
                        made = { currentTime: 0, phase: 'active', playbackRate: 1 };
                        try {
                            Object.defineProperty(made, Symbol.toStringTag,
                                { value: 'DocumentTimeline', configurable: true });
                        } catch (e) { }
                        timelines.set(doc, made);
                    }
                    return made;
                });
                if (protoMap.Document) {
                    // Web Animations §4.6: the document's animation set. The engine runs its
                    // animations from the style pass and hands none back as an Animation object yet,
                    // so the answer is the empty sequence the interface describes — an Array, which is
                    // what a host array would not have been.
                    protoMap.Document.getAnimations = function () { return []; };
                    protoMap.Document.exitFullscreen = function () {
                        return Promise.reject(new DOMException(
                            'The document is not fullscreen.', 'NotFoundError'));
                    };
                    protoMap.Document.exitPictureInPicture = function () {
                        return Promise.reject(new DOMException(
                            'The document is not in picture-in-picture mode.', 'NotFoundError'));
                    };
                    protoMap.Document.exitPointerLock = function () {
                        return Promise.resolve(undefined);
                    };
                }
                // The Clipboard API (§3.3) and the service-worker registration (SW §2.6): the engine
                // has neither a clipboard permission prompt nor a worker host, so both answer the
                // rejection the interface gives for a context that cannot do the work rather than
                // leaving the page to read undefined and throw.
                var clipboard = {
                    write: function () {
                        return Promise.reject(new DOMException('The Clipboard API is not available in '
                            + 'this context.', 'NotAllowedError'));
                    },
                    read: function () {
                        return Promise.reject(new DOMException('The Clipboard API is not available in '
                            + 'this context.', 'NotAllowedError'));
                    },
                    writeText: function () {
                        return Promise.reject(new DOMException('The Clipboard API is not available in '
                            + 'this context.', 'NotAllowedError'));
                    },
                    readText: function () {
                        return Promise.reject(new DOMException('The Clipboard API is not available in '
                            + 'this context.', 'NotAllowedError'));
                    }
                };
                // Clipboard is an event target (Clipboard §4.3), so the object hangs under the
                // interface's level: the tag, the constructor and the listener bag all read from there
                // rather than being faked one member at a time.
                try {
                    var clipboardLevel = protoMap['Clipboard'];
                    if (clipboardLevel) Object.setPrototypeOf(clipboard, clipboardLevel);
                    else Object.defineProperty(clipboard, Symbol.toStringTag,
                        { value: 'Clipboard', configurable: true });
                } catch (e) { }
                var registrations = Promise.reject(new DOMException('Service workers are not '
                    + 'supported in this context.', 'NotSupportedError'));
                var serviceWorker = {
                    ready: registrations,
                    controller: null,
                    register: function () { return registrations; },
                    getRegistration: function () { return Promise.resolve(undefined); },
                    getRegistrations: function () { return Promise.resolve([]); },
                    addEventListener: function () { },
                    removeEventListener: function () { },
                    onmessage: null
                };
                accessor('Navigator', 'clipboard', function () { return clipboard; });
                accessor('Navigator', 'serviceWorker', function () { return serviceWorker; });
                accessor('Navigator', 'mediaDevices', function () { return null; });
                accessor('Navigator', 'wakeLock', function () { return null; });
                accessor('Navigator', 'keyboard', function () { return null; });
                accessor('Navigator', 'managed', function () { return null; });
                if (typeof __acrux !== 'undefined' && typeof __acrux.hardwareConcurrency === 'function') {
                    accessor('Navigator', 'hardwareConcurrency',
                        function () { return __acrux.hardwareConcurrency(); });
                }
                // URL §2.5: the ancestors of a window this engine never nests are an empty list.
                accessor('Location', 'ancestorOrigins', function () {
                    return typeof DOMStringList === 'function' ? new DOMStringList([]) : [];
                });
                // Geolocation §5, Permissions §4.3, Storage §4.2, Network Information §3: the objects a
                // page reaches through the navigator. This engine has no position provider, no
                // permission database, no quota to measure and no link to sample, so each interface
                // answers the one state it can honestly report — a request that fails as unavailable, a
                // permission the page was never given, an estimate of no bytes — while the object still
                // carries the members and the tag the interface names (measured: each one reads as
                // [object Geolocation], [object Permissions], [object PermissionStatus], and the rest).
                function refusedInterface(name, baseProto) {
                    var ctor = function () {
                        throw new TypeError('Failed to construct ' + formDataQuote(name)
                            + ': Illegal constructor');
                    };
                    Object.defineProperty(ctor, 'name', { value: name, configurable: true });
                    var proto = Object.create(baseProto || Object.prototype);
                    Object.defineProperty(proto, 'constructor',
                        { value: ctor, writable: true, configurable: true });
                    Object.defineProperty(proto, Symbol.toStringTag, { value: name, configurable: true });
                    window[name] = ctor;
                    if (protoMap[name] === undefined) protoMap[name] = proto;
                    return proto;
                }
                function member(proto, name, value) {
                    Object.defineProperty(proto, name, { value: value, writable: true,
                        configurable: true, enumerable: false });
                }
                var eventTargetLevel = protoMap['EventTarget'] || Object.prototype;
                var GEO_SLOTS = new WeakMap();
                var geolocationProto = refusedInterface('Geolocation', null);
                var positionErrorProto = refusedInterface('GeolocationPositionError', null);
                ['code', 'message'].forEach(function (field) {
                    Object.defineProperty(positionErrorProto, field, { configurable: true, enumerable: false,
                        get: function () {
                            var slots = GEO_SLOTS.get(this);
                            if (!slots) throw new TypeError('Illegal invocation');
                            return slots[field];
                        } });
                });
                function positionError(code, message) {
                    var made = Object.create(positionErrorProto);
                    GEO_SLOTS.set(made, { code: code, message: message });
                    return made;
                }
                var geoWatchId = 0;
                member(geolocationProto, 'getCurrentPosition', function (success, error) {
                    // No provider is wired, so the request completes the way a device without a
                    // position source does: on the next task, as an error the page's own callback hears.
                    if (typeof error === 'function') {
                        setTimeout(function () {
                            try { error(positionError(2, 'Position information is unavailable in this engine.')); }
                            catch (e) { }
                        }, 0);
                    }
                });
                member(geolocationProto, 'watchPosition', function () { return ++geoWatchId; });
                member(geolocationProto, 'clearWatch', function () { });
                var geolocation = Object.create(geolocationProto);

                var PERMISSION_NAMES = ['accelerometer', 'accessibility-events', 'ambient-light-sensor',
                    'background-sync', 'camera', 'clipboard-read', 'clipboard-write',
                    'geolocation', 'gyroscope', 'magnetometer', 'microphone', 'midi', 'notifications',
                    'payment-handler', 'persistent-storage', 'push', 'screen-wake-lock', 'storage-access'];
                var permissionStatusProto = refusedInterface('PermissionStatus', eventTargetLevel);
                var PERM_SLOTS = new WeakMap();
                ['name', 'state', 'onchange'].forEach(function (field) {
                    Object.defineProperty(permissionStatusProto, field, { configurable: true, enumerable: false,
                        get: function () {
                            var slots = PERM_SLOTS.get(this);
                            if (!slots) throw new TypeError('Illegal invocation');
                            return slots[field];
                        } });
                });
                member(permissionStatusProto, 'toJSON', function () {
                    var slots = PERM_SLOTS.get(this);
                    if (!slots) throw new TypeError('Illegal invocation');
                    return { name: slots.name, state: slots.state };
                });
                var permissionsProto = refusedInterface('Permissions', null);
                member(permissionsProto, 'query', function (descriptor) {
                    var read = descriptor || { };
                    var name = read.name === undefined ? '' : String(read.name);
                    if (PERMISSION_NAMES.indexOf(name) < 0) {
                        var quote = String.fromCharCode(39);
                        throw new TypeError('Failed to execute ' + quote + 'query' + quote
                            + ' on ' + quote + 'Permissions' + quote + ': Failed to read the '
                            + quote + 'name' + quote + ' property from ' + quote + 'PermissionDescriptor'
                            + quote + ': The provided value ' + quote + name + quote
                            + ' is not a valid enum value of type PermissionName.');
                    }
                    // Nothing has been granted and nothing has been refused: the state a page hears
                    // back is the one the user has not answered yet.
                    var made = Object.create(permissionStatusProto);
                    PERM_SLOTS.set(made, { name: name, state: 'prompt', onchange: null });
                    return Promise.resolve(made);
                });
                var permissions = Object.create(permissionsProto);

                var storageManagerProto = refusedInterface('StorageManager', null);
                member(storageManagerProto, 'estimate', function () {
                    return Promise.resolve({ usage: 0, quota: 0, usageDetails: { } });
                });
                member(storageManagerProto, 'persist', function () { return Promise.resolve(false); });
                member(storageManagerProto, 'persisted', function () { return Promise.resolve(false); });
                member(storageManagerProto, 'getDirectory', function () {
                    return Promise.reject(new DOMException('The origin is not persisted.', 'NotAllowedError'));
                });
                var storageManager = Object.create(storageManagerProto);

                var networkInfoProto = refusedInterface('NetworkInformation', eventTargetLevel);
                ['effectiveType', 'rtt', 'downlink', 'saveData', 'type'].forEach(function (field) {
                    var values = { effectiveType: '4g', rtt: 0, downlink: 0, saveData: false, type: 'unknown' };
                    Object.defineProperty(networkInfoProto, field, { configurable: true, enumerable: false,
                        get: function () {
                            if (this !== networkInformation) throw new TypeError('Illegal invocation');
                            return values[field];
                        } });
                });
                var networkInformation = Object.create(networkInfoProto);

                accessor('Navigator', 'geolocation', function () { return geolocation; });
                accessor('Navigator', 'permissions', function () { return permissions; });
                accessor('Navigator', 'storage', function () { return storageManager; });
                accessor('Navigator', 'connection', function () { return networkInformation; });
                accessor('Navigator', 'webdriver', function () { return false; });
            })();

            // ── The observers (Intersection Observer §2, Resize Observer §2,
            //    Performance Timeline §3.6, Reporting API §5) ─────────────────────
            // Each one is a queue the page registers interest in and the platform drains at the next
            // opportunity. The engine has no rendering step to hang that on in a headless run, so the
            // queue is delivered on the timer the host already runs: an observation made now is
            // reported in a later task, never inside the call that made it, which is what a page
            // switching on the asynchronous shape depends on.
            (function () {
                function rectOf(node) {
                    var rect = null;
                    try { rect = node.getBoundingClientRect(); } catch (e) { return null; }
                    if (!rect) return null;
                    return { x: Number(rect.x) || 0, y: Number(rect.y) || 0,
                        width: Number(rect.width) || 0, height: Number(rect.height) || 0,
                        top: Number(rect.top) || 0, left: Number(rect.left) || 0,
                        right: Number(rect.right) || 0, bottom: Number(rect.bottom) || 0 };
                }
                function entryInterface(name, fields) {
                    var proto = (window.__domProto || {})[name] || Object.create(Object.prototype);
                    (window.__domProto || {})[name] = proto;
                    var Ctor = function () {
                        throw new TypeError('Failed to construct \'' + name
                            + '\': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = proto;
                    for (var i = 0; i < fields.length; i++) {
                        (function (key) {
                            Object.defineProperty(proto, key, { configurable: true, enumerable: true,
                                get: function () { return this['__' + key]; } });
                        })(fields[i]);
                    }
                    window[name] = Ctor;
                    return proto;
                }
                entryInterface('IntersectionObserverEntry', ['time', 'target', 'rootBounds',
                    'boundingClientRect', 'intersectionRect', 'intersectionRatio', 'isIntersecting']);
                entryInterface('ResizeObserverEntry', ['target', 'contentRect', 'contentBoxSize',
                    'borderBoxSize', 'devicePixelContentBoxSize']);
                // The performance entry interfaces are the ones the timing block above declares; an
                // observer hands out those very objects, so no second level is made for them here.
                function makeObserver(ctorName, compute) {
                    var states = new WeakMap();
                    var proto = (window.__domProto || {})[ctorName] || Object.create(Object.prototype);
                    (window.__domProto || {})[ctorName] = proto;
                    var Ctor = function (callback, options) {
                        if (typeof callback !== 'function') {
                            throw new TypeError('Failed to construct \'' + ctorName
                                + '\': parameter 1 is not of type \'Function\'.');
                        }
                        var self = this;
                        var targets = [];
                        var pending = [];
                        var scheduled = false;
                        var closed = false;
                        // The state an observer carries is kept beside it rather than on it, so the
                        // object a page holds has no own properties of its own (§2 of each interface).
                        var state = {
                            targets: targets, options: options || { }, record: record,
                            takeRecords: function () { return pending.splice(0, pending.length); },
                            disconnect: function () {
                                targets.length = 0; pending.length = 0; closed = true;
                            }
                        };
                        states.set(this, state);
                        function flush() {
                            scheduled = false;
                            if (closed) return;
                            var list = pending.splice(0, pending.length);
                            for (var i = 0; i < targets.length; i++) {
                                var made = compute(self, targets[i], { });
                                if (made) list.push(made);
                            }
                            if (!list.length) return;
                            try { callback.call(self, list, self); } catch (e) { }
                        }
                        function schedule() {
                            if (scheduled || closed) return;
                            scheduled = true;
                            setTimeout(flush, 0);
                        }
                        function record(target, opts) {
                            if (closed) return;
                            var isElement = false;
                            try { isElement = target && target.nodeType === 1; } catch (e) { }
                            if (!isElement) {
                                throw new TypeError('Failed to execute \'observe\' on \'' + ctorName
                                    + '\': parameter 1 is not of type \'Element\'.');
                            }
                            if (targets.indexOf(target) < 0) targets.push(target);
                            // The observation is not a record yet: the interface reports what the
                            // target looks like when the platform next looks at it, so the queue is
                            // filled at the flush and 'takeRecords' right after 'observe' is empty.
                            schedule();
                        }
                    };
                    Object.defineProperty(proto, 'observe', {
                        configurable: true, writable: true, enumerable: false,
                        value: function (target, opts) {
                            var st = states.get(this);
                            if (!st) return;
                            var isElement = false;
                            try { isElement = target && target.nodeType === 1; } catch (e) { }
                            if (!isElement) {
                                throw new TypeError('Failed to execute \'observe\' on \'' + ctorName
                                    + '\': parameter 1 is not of type \'Element\'.');
                            }
                            if (st.targets.indexOf(target) < 0) st.targets.push(target);
                            st.record(target, opts);
                        }
                    });
                    Object.defineProperty(proto, 'unobserve', {
                        configurable: true, writable: true, enumerable: false,
                        value: function (target) {
                            var st = states.get(this);
                            if (!st) return;
                            for (var i = 0; i < st.targets.length; i++) {
                                if (st.targets[i] === target) { st.targets.splice(i, 1); break; }
                            }
                        }
                    });
                    Object.defineProperty(proto, 'takeRecords', {
                        configurable: true, writable: true, enumerable: false,
                        value: function () {
                            var st = states.get(this);
                            return st ? st.takeRecords() : [];
                        }
                    });
                    Object.defineProperty(proto, 'disconnect', {
                        configurable: true, writable: true, enumerable: false,
                        value: function () {
                            var st = states.get(this);
                            if (!st) return;
                            st.disconnect();
                        }
                    });
                    Object.defineProperty(Ctor, 'name', { value: ctorName, configurable: true });
                    Ctor.prototype = proto;
                    Object.defineProperty(proto, 'constructor',
                        { value: Ctor, writable: true, configurable: true });
                    try {
                        Object.defineProperty(proto, Symbol.toStringTag,
                            { value: ctorName, configurable: true });
                    } catch (e) { }
                    window[ctorName] = Ctor;
                    return Ctor;
                }

                function intersectionEntry(observer, target)
                {
                    var rect = rectOf(target);
                    var made = Object.create((window.__domProto || {}).IntersectionObserverEntry
                        || Object.prototype);
                    var ratio = 0;
                    var box = rect || { x: 0, y: 0, width: 0, height: 0, top: 0, left: 0,
                        right: 0, bottom: 0 };
                    // The root is the viewport unless the page gave a root element; an area of zero
                    // never intersects, which is what a page reads in an engine that laid nothing out.
                    made.__time = (window.performance && performance.now) ? performance.now() : 0;
                    made.__target = target;
                    made.__boundingClientRect = box;
                    made.__rootBounds = box;
                    made.__intersectionRect = box;
                    made.__intersectionRatio = ratio;
                    made.__isIntersecting = false;
                    return made;
                }
                var IntersectionObserver = makeObserver('IntersectionObserver', intersectionEntry);

                function resizeEntry(observer, target) {
                    var rect = rectOf(target) || { x: 0, y: 0, width: 0, height: 0, top: 0,
                        left: 0, right: 0, bottom: 0 };
                    var made = Object.create((window.__domProto || {}).ResizeObserverEntry
                        || Object.prototype);
                    made.__target = target;
                    made.__contentRect = rect;
                    var size = { inlineSize: rect.width, blockSize: rect.height };
                    made.__contentBoxSize = [size];
                    made.__borderBoxSize = [size];
                    made.__devicePixelContentBoxSize = [size];
                    return made;
                }
                makeObserver('ResizeObserver', resizeEntry);
                makeObserver('ReportingObserver', function () { return null; });

                // Performance Timeline §3.6: the entries the buffer already holds, filtered to the
                // types the page asked for. 'buffered' replays what was recorded before the observer
                // was made, which is the reason the interface has the flag.
                // The types the reference names, in its own order (§3.7): the page switches on the
                // list rather than on any one entry.
                var supportedTypes = ['element', 'event', 'first-input', 'largest-contentful-paint',
                    'layout-shift', 'long-animation-frame', 'longtask', 'navigation', 'paint',
                    'resource'];
                function PerformanceObserver(callback) {
                    if (typeof callback !== 'function') {
                        throw new TypeError('Failed to construct \'PerformanceObserver\': parameter 1'
                            + ' is not of type \'Function\'.');
                    }
                    var self = this;
                    var wanted = [];
                    var scheduled = false;
                    var seen = [];
                    function entryList(records) { return newPerfEntryList(records); }
                    function collect() {
                        var out = [];
                        var all = [];
                        try { all = Array.from(window.performance.getEntries ? performance.getEntries() : []); }
                        catch (e) { all = []; }
                        for (var i = 0; i < all.length; i++) {
                            var raw = all[i];
                            if (!raw || wanted.indexOf(String(raw.entryType)) < 0) continue;
                            if (seen.indexOf(raw) >= 0) continue;
                            seen.push(raw);
                            out.push(raw);
                        }
                        return out;
                    }
                    function flush() {
                        scheduled = false;
                        var records = collect();
                        if (!records.length) return;
                        try { callback.call(self, entryList(records), self); } catch (e) { }
                    }
                    this.observe = function (opts) {
                        opts = opts || { };
                        var types = opts.entryTypes || (opts.type ? [opts.type] : []);
                        for (var i = 0; i < types.length; i++) {
                            if (wanted.indexOf(String(types[i])) < 0) wanted.push(String(types[i]));
                        }
                        if (!scheduled) { scheduled = true; setTimeout(flush, 0); }
                    };
                    this.takeRecords = function () {
                        var records = collect();
                        return entryList(records);
                    };
                    this.disconnect = function () { wanted.length = 0; seen.length = 0; };
                    Object.defineProperty(this, 'supportedEntryTypes', { configurable: true,
                        get: function () { return supportedTypes; } });
                }
                Object.defineProperty(PerformanceObserver, 'supportedEntryTypes', { configurable: true,
                    get: function () { return supportedTypes.slice(); } });
                (window.__domProto || {}).PerformanceObserver = PerformanceObserver.prototype;
                window.PerformanceObserver = PerformanceObserver;

                // The 'Audio' convenience the HTML defines over an audio element (HTML §4.8.10).
                window.Audio = function Audio(src) {
                    var el = document.createElement('audio');
                    if (src !== undefined && src !== null) el.src = String(src);
                    return el;
                };
                // The interface's prototype is the audio element's, so 'new Audio() instanceof Audio'
                // reads the way the element it builds does (HTML §4.8.10).
                window.Audio.prototype = (window.__domProto || {}).HTMLAudioElement
                    || Object.create(Object.prototype);
            })();

            // ── Members an element interface declares over the shared host (HTML §4.10.9,
            //    §4.12.3) ────────────────────────────────────────────────────────
            // The host answers every element with one object, so the members that belong to one
            // interface and not to the element in general are installed on that interface's
            // prototype: the host answers what it carries first, and the page reads the rest
            // through the level its element was stamped with.
            (function () {
                var protoMap = window.__domProto || {};
                var select = protoMap.HTMLSelectElement;
                if (select) {
                    Object.defineProperty(select, 'length', {
                        configurable: true, enumerable: true,
                        get: function () { var list = this.options; return list ? list.length : 0; },
                        set: function (wanted) {
                            wanted = Number(wanted) || 0;
                            var list = this.options ? Array.prototype.slice.call(this.options) : [];
                            for (var i = list.length - 1; i >= wanted; i--) this.remove(i);
                        }
                    });
                    Object.defineProperty(select, 'size', {
                        configurable: true, enumerable: true,
                        get: function () {
                            var written = parseInt(this.getAttribute('size'), 10);
                            return isNaN(written) || written < 0 ? 0 : written;
                        },
                        set: function (value) { this.setAttribute('size', String(Number(value) || 0)); }
                    });
                    // 'add' takes the option into the list before the anchor it is given — an index,
                    // or the option itself — or at the end when there is no anchor (§4.10.9).
                    select.add = function (option, before) {
                        var isElement = false;
                        try { isElement = !!option && option.nodeType === 1; } catch (e) { }
                        if (!isElement) {
                            throw new TypeError('Failed to execute \'add\' on \'HTMLSelectElement\':'
                                + ' parameter 1 is not of type \'HTMLOptionElement\'.');
                        }
                        var list = this.options ? Array.prototype.slice.call(this.options) : [];
                        var anchor = null;
                        if (before !== undefined && before !== null) {
                            anchor = typeof before === 'number'
                                ? (before >= 0 && before < list.length ? list[before] : null)
                                : (list.indexOf(before) >= 0 ? before : null);
                        }
                        if (anchor) this.insertBefore(option, anchor);
                        else this.appendChild(option);
                    };
                }
                var form = protoMap.HTMLFormElement;
                if (form) {
                    // The length a form answers is its element list's length (§4.10.22), read through
                    // the collection the host already maintains rather than kept beside it.
                    Object.defineProperty(form, 'length', { configurable: true, enumerable: true,
                        get: function () { var list = this.elements; return list ? list.length : 0; } });
                }
                var template = protoMap.HTMLTemplateElement;
                if (template) {
                    Object.defineProperty(template, 'content', {
                        configurable: true, enumerable: true,
                        get: function () {
                            return typeof this.__templateContent === 'function'
                                ? this.__templateContent() : null;
                        }
                    });
                }
                // The singletons the window hands out are host objects too: they are stamped with
                // their interface so the accessors declared above — and every accessor a family
                // declares later — are reachable on the object a page actually holds.
                var singletons = [['navigator', 'Navigator'], ['location', 'Location'],
                                  ['history', 'History'], ['screen', 'Screen']];
                for (var i = 0; i < singletons.length; i++) {
                    var object = window[singletons[i][0]];
                    var level = protoMap[singletons[i][1]];
                    if (!object || !level) continue;
                    try { Object.setPrototypeOf(object, level); } catch (e) { }
                }

                // ── Geometry Interfaces §3, §4, §5 ─────────────────────────────
                // A matrix is the 4x4 column list the interface names, held as sixteen numbers, with
                // the 2D view the page reads most (a,b,c,d,e,f) beside it. The operations are the
                // ones the interface defines; a matrix built from a transform list applies the list
                // in order, starting from the identity.
                function identity() {
                    return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
                }
                function multiply(left, right) {
                    var out = new Array(16);
                    for (var col = 0; col < 4; col++) {
                        for (var row = 0; row < 4; row++) {
                            var sum = 0;
                            for (var k = 0; k < 4; k++) sum += left[k * 4 + row] * right[col * 4 + k];
                            out[col * 4 + row] = sum;
                        }
                    }
                    return out;
                }
                // The string a matrix is built from is a transform list, and the list is held to the
                // grammar the CSS names: an angle carries a unit, a length carries one too, 'matrix'
                // takes exactly six numbers and 'matrix3d' sixteen. Anything else is refused with the
                // sentence the reference writes, naming the whole string it could not read.
                var number = /^-?(?:\d+\.?\d*|\.\d+)$/;
                var lengthArg = /^-?(?:\d+\.?\d*|\.\d+)(px)?$/;
                var angleArg = /^-?(?:\d+\.?\d*|\.\d+)(deg|grad|rad|turn)$/;
                function argsAre(parts, pattern, count) {
                    if (count && parts.length !== count) return false;
                    for (var i = 0; i < parts.length; i++) if (!pattern.test(parts[i])) return false;
                    return parts.length > 0;
                }
                function validTransformList(text) {
                    var re = /^\s*(?:(matrix|matrix3d|translate|translate3d|translateX|translateY|translateZ|scale|scale3d|scaleX|scaleY|scaleZ|rotate|rotate3d|rotateX|rotateY|rotateZ|skew|skewX|skewY)\s*\(([^()]*)\)\s*)+$/;
                    var whole = re.exec(String(text));
                    if (!whole) return false;
                    var each = /(matrix|matrix3d|translate|translate3d|translateX|translateY|translateZ|scale|scale3d|scaleX|scaleY|scaleZ|rotate|rotate3d|rotateX|rotateY|rotateZ|skew|skewX|skewY)\s*\(([^()]*)\)/g;
                    var match;
                    while ((match = each.exec(String(text))) !== null) {
                        var word = match[1];
                        var raw = match[2].split(/[\s,]+/).filter(function (p) { return p.length; });
                        if (word === 'matrix' && !argsAre(raw, number, 6)) return false;
                        else if (word === 'matrix3d' && !argsAre(raw, number, 16)) return false;
                        else if (word === 'scale' || word === 'scaleX' || word === 'scaleY'
                            || word === 'scaleZ') { if (!argsAre(raw, number)) return false; }
                        else if (word === 'rotate' || word === 'rotateX' || word === 'rotateY'
                            || word === 'rotateZ' || word === 'skew' || word === 'skewX'
                            || word === 'skewY') {
                            if (word === 'rotate' && raw.length !== 1) return false;
                            if (!argsAre(raw, angleArg)) return false;
                        } else if (word === 'translate' || word === 'translateX'
                            || word === 'translateY' || word === 'translateZ'
                            || word === 'translate3d') {
                            if (!argsAre(raw, lengthArg)) return false;
                        }
                    }
                    return true;
                }
                // The arguments of a transform function are numbers with the unit the grammar allows:
                // an angle arrives in degrees whatever it was written in (Geometry Interfaces §3.2
                // reads CSS angles), and a length in pixels or the font-relative units the host lays
                // out with. Reading the prefix by hand rather than with the number parser is what
                // keeps '90deg' from becoming nothing at all.
                function unitValue(raw, kind) {
                    var text = String(raw).trim();
                    var num = /^[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?/.exec(text);
                    if (!num) return NaN;
                    var value = Number(num[0]);
                    var unit = text.slice(num[0].length).toLowerCase();
                    if (kind === 'angle') {
                        if (unit === '' || unit === 'deg') return value;
                        if (unit === 'grad') return value * 0.9;
                        if (unit === 'rad') return value * 180 / Math.PI;
                        if (unit === 'turn') return value * 360;
                        return NaN;
                    }
                    if (unit === '' || unit === 'px') return value;
                    if (unit === 'em' || unit === 'rem') return value * 16;
                    if (unit === 'cm') return value * 96 / 2.54;
                    if (unit === 'mm') return value * 96 / 25.4;
                    if (unit === 'in') return value * 96;
                    if (unit === 'pt') return value * 96 / 72;
                    if (unit === 'pc') return value * 16;
                    return NaN;
                }
                function toNumbers(body, word) {
                    var kind = /rotate|skew/.test(word) ? 'angle' : 'length';
                    var raw = String(body).split(/[,\s]+/).filter(function (p) { return p.length; });
                    var out = [];
                    for (var i = 0; i < raw.length; i++) {
                        var value = unitValue(raw[i], kind);
                        if (isNaN(value)) return [];
                        out.push(value);
                    }
                    return out;
                }
                function parseTransformList(text) {
                    var current = identity();
                    var re = /(matrix|matrix3d|translate|translateX|translateY|translateZ|translate3d|scale|scaleX|scaleY|scaleZ|scale3d|rotate|rotateX|rotateY|rotateZ|rotateAxis3d|skew|skewX|skewY)\(([^)]*)\)/g;
                    var match;
                    while ((match = re.exec(String(text))) !== null) {
                        var word = match[1];
                        var parts = /matrix|matrix3d|scale/.test(word)
                            ? toNumbers(match[2], 'number') : toNumbers(match[2], word);
                        var step = identity();
                        var a = parts[0] || 0, b = parts[1] || 0;
                        if (word === 'matrix' && parts.length >= 6) {
                            // 'matrix(a, b, c, d, e, f)' is the 2D view of a column-major 4x4: a and
                            // b are the first column, c and d the second, and the pair (e, f) that
                            // translates lives in the fourth column.
                            step[0] = parts[0]; step[1] = parts[1];
                            step[4] = parts[2]; step[5] = parts[3];
                            step[12] = parts[4]; step[13] = parts[5];
                        } else if (word === 'translateX') step[12] = a;
                        else if (word === 'translateY') step[13] = a;
                        else if (word === 'translate' || word === 'translateZ' || word === 'translate3d') {
                            step[12] = parts[0] || 0;
                            step[13] = parts.length > 1 && word !== 'translateZ' ? (parts[1] || 0) : 0;
                            if (word === 'translateZ') step[14] = a;
                        } else if (word === 'scaleX') { step[0] = a; step[5] = 0 === a ? 0 : 1; }
                        else if (word === 'scaleY') { step[5] = a; }
                        else if (word === 'scale') {
                            step[0] = a; step[5] = parts.length > 1 ? parts[1] : a; step[10] = 1;
                        } else if (word === 'rotate') {
                            var rad = a * Math.PI / 180;
                            step[0] = Math.cos(rad); step[1] = Math.sin(rad);
                            step[4] = -Math.sin(rad); step[5] = Math.cos(rad);
                        } else if (word === 'skewX') step[2] = Math.tan(a * Math.PI / 180);
                        else if (word === 'skewY') step[1] = Math.tan(a * Math.PI / 180);
                        else if (word === 'skew') {
                            step[2] = Math.tan(a * Math.PI / 180);
                            step[1] = Math.tan((parts[1] || 0) * Math.PI / 180);
                        } else if (word === 'rotateZ') {
                            var rz = a * Math.PI / 180;
                            step[0] = Math.cos(rz); step[1] = Math.sin(rz);
                            step[4] = -Math.sin(rz); step[5] = Math.cos(rz);
                        }
                        current = multiply(current, step);
                    }
                    return current;
                }
                function DOMMatrixReadOnly(source) {
                    var numbers = identity();
                    if (typeof source === 'string' && source.length && !validTransformList(source)) {
                        throw new SyntaxError('Failed to construct \'' + (this && this.constructor
                            && this.constructor.name ? this.constructor.name : 'DOMMatrix')
                            + '\': Failed to parse \'' + source + '\'.');
                    }
                    if (typeof source === 'string') numbers = parseTransformList(source);
                    else if (source && typeof source === 'object' && source.m11 !== undefined) {
                        for (var i = 0; i < 16; i++) {
                            var key = 'm' + (((i % 4) + 1)) + (Math.floor(i / 4) + 1);
                            if (source[key] !== undefined) numbers[i] = Number(source[key]);
                        }
                    } else if (source && typeof source.length === 'number' && source.length >= 16) {
                        for (var j = 0; j < 16; j++) numbers[j] = Number(source[j]);
                    } else if (source && typeof source.length === 'number' && source.length === 6) {
                        numbers[0] = Number(source[0]); numbers[1] = Number(source[1]);
                        numbers[4] = Number(source[2]); numbers[5] = Number(source[3]);
                        numbers[12] = Number(source[4]); numbers[13] = Number(source[5]);
                    }
                    var self = this;
                    function at(col, row) { return numbers[col * 4 + row]; }
                    var twoD = numbers[2] === 0 && numbers[3] === 0 && numbers[6] === 0
                        && numbers[7] === 0 && numbers[8] === 0 && numbers[9] === 0
                        && numbers[11] === 0 && numbers[14] === 0 && numbers[15] === 1;
                    Object.defineProperty(this, 'is2D', { configurable: true, get: function () { return twoD; } });
                    Object.defineProperty(this, 'isIdentity', { configurable: true, get: function () {
                        var base = identity();
                        for (var k = 0; k < 16; k++) if (Math.abs(numbers[k] - base[k]) > 1e-10) return false;
                        return true; } });
                    var short2D = ['a', 'b', 'c', 'd', 'e', 'f'];
                    var shortIndex = [0, 1, 4, 5, 12, 13];
                    for (var s = 0; s < 6; s++) {
                        (function (index, position) {
                            Object.defineProperty(self, index, { configurable: true, enumerable: true,
                                get: function () { return numbers[position]; } });
                        })(short2D[s], shortIndex[s]);
                    }
                    for (var col = 1; col <= 4; col++) {
                        for (var row = 1; row <= 4; row++) {
                            (function (r, c) {
                                var key = 'm' + r + c;
                                Object.defineProperty(self, key, { configurable: true, enumerable: true,
                                    // The list is the rows laid end to end: m11, m12, m13, m14, m21,
                                    // … — so the row leads the index (Geometry Interfaces §3.1).
                                    get: function () { return numbers[(r - 1) * 4 + (c - 1)]; } });
                            })(row, col);
                        }
                    }
                    // The 'm21' spelling is the row/column read; the CSS naming (a..f) above is the
                    // same numbers seen as a 2D matrix.
                    this.inverse = function () { return new DOMMatrixReadOnly(invert(numbers)); };
                    this.multiply = function (other) {
                        return new DOMMatrixReadOnly(multiply(numbers, matrixOf(other)));
                    };
                    this.translate = function (tx, ty, tz) {
                        var step = identity();
                        step[12] = Number(tx) || 0;
                        step[13] = Number(ty) || 0;
                        step[14] = Number(tz) || 0;
                        return new DOMMatrixReadOnly(multiply(numbers, step));
                    };
                    this.scale = function (sx, sy, sz) {
                        var step = identity();
                        step[0] = Number(sx) || 0;
                        step[5] = (sy === undefined ? Number(sx) : Number(sy)) || 0;
                        step[10] = (sz === undefined ? 1 : Number(sz)) || 0;
                        return new DOMMatrixReadOnly(multiply(numbers, step));
                    };
                    this.rotate = function (alpha, beta, gamma) {
                        var rad = (Number(alpha) || 0) * Math.PI / 180;
                        var step = identity();
                        if (beta === undefined && gamma === undefined) {
                            step[0] = Math.cos(rad); step[1] = Math.sin(rad);
                            step[4] = -Math.sin(rad); step[5] = Math.cos(rad);
                        } else {
                            step[0] = Math.cos(rad); step[1] = Math.sin(rad);
                        }
                        return new DOMMatrixReadOnly(multiply(numbers, step));
                    };
                    this.transformPoint = function (point) {
                        var p = point && point.x !== undefined ? point : { x: 0, y: 0, z: 0, w: 1 };
                        var x = Number(p.x) || 0, y = Number(p.y) || 0, z = Number(p.z) || 0;
                        var w = p.w === undefined ? 1 : Number(p.w);
                        var out = {
                            x: at(0, 0) * x + at(1, 0) * y + at(2, 0) * z + at(3, 0) * w,
                            y: at(0, 1) * x + at(1, 1) * y + at(2, 1) * z + at(3, 1) * w,
                            z: at(0, 2) * x + at(1, 2) * y + at(2, 2) * z + at(3, 2) * w,
                            w: at(0, 3) * x + at(1, 3) * y + at(2, 3) * z + at(3, 3) * w
                        };
                        return new DOMPoint(out.x, out.y, out.z, out.w);
                    };
                    this.toFloat64Array = function () { return numbers.slice(0); };
                    this.toJSON = function () {
                        var out = { is2D: twoD, isIdentity: this.isIdentity };
                        for (var r = 1; r <= 4; r++) for (var c = 1; c <= 4; c++)
                            out['m' + r + c] = numbers[(r - 1) * 4 + (c - 1)];
                        return out;
                    };
                }
                function matrixOf(value) {
                    if (!value) return identity();
                    if (typeof value.toFloat64Array === 'function') return value.toFloat64Array();
                    if (typeof value === 'string') return parseTransformList(value);
                    if (value.m11 !== undefined) {
                        var numbers = identity();
                        for (var i = 0; i < 16; i++) {
                            var key = 'm' + ((i % 4) + 1) + (Math.floor(i / 4) + 1);
                            if (value[key] !== undefined) numbers[i] = Number(value[key]);
                        }
                        return numbers;
                    }
                    return identity();
                }
                function invert(source) {
                    // The 2D inverse is all a page that works in CSS transforms needs; a 3D matrix is
                    // inverted by its upper-left block the same way, which leaves the translation row
                    // for the caller to reapply.
                    var out = identity();
                    var a = source[0], b = source[1], c = source[4], d = source[5],
                        e = source[12], f = source[13];
                    var det = a * d - b * c;
                    if (Math.abs(det) < 1e-12) return out;
                    out[0] = d / det; out[1] = -b / det;
                    out[4] = -c / det; out[5] = a / det;
                    out[12] = (c * f - d * e) / det;
                    out[13] = (b * e - a * f) / det;
                    return out;
                }
                function DOMMatrix(source) { DOMMatrixReadOnly.call(this, source); }
                DOMMatrix.prototype = Object.create(DOMMatrixReadOnly.prototype);
                DOMMatrix.prototype.constructor = DOMMatrix;
                DOMMatrixReadOnly.fromMatrix = function (other) { return new DOMMatrixReadOnly(matrixOf(other)); };
                DOMMatrixReadOnly.fromTransform = DOMMatrixReadOnly.fromMatrix;
                DOMMatrixReadOnly.fromFloat64Array = function (list) { return new DOMMatrixReadOnly(list); };
                DOMMatrixReadOnly.fromSVGMatrix = DOMMatrixReadOnly.fromMatrix;
                DOMMatrix.fromMatrix = function (other) { return new DOMMatrix(matrixOf(other)); };
                DOMMatrix.fromTransform = DOMMatrix.fromMatrix;
                DOMMatrix.identity = function () { return new DOMMatrix(); };
                DOMMatrix.prototype.multiplyRight = DOMMatrix.prototype.multiply;
                var geometryProtos = { DOMMatrixReadOnly: DOMMatrixReadOnly, DOMMatrix: DOMMatrix };
                for (var key in geometryProtos) {
                    (window.__domProto || {})[key] = geometryProtos[key].prototype;
                }
                window.DOMMatrixReadOnly = DOMMatrixReadOnly;
                window.DOMMatrix = DOMMatrix;

                // DOMQuad (§5.3): the four corners of a rotated rectangle.
                function DOMQuad(p1, p2, p3, p4) {
                    this.p1 = p1 || new DOMPoint();
                    this.p2 = p2 || new DOMPoint();
                    this.p3 = p3 || new DOMPoint();
                    this.p4 = p4 || new DOMPoint();
                    this.toJSON = function () {
                        return { p1: this.p1.toJSON(), p2: this.p2.toJSON(),
                            p3: this.p3.toJSON(), p4: this.p4.toJSON() };
                    };
                }
                DOMQuad.fromRect = function (rect) {
                    return new DOMQuad(
                        new DOMPoint(rect.x, rect.y), new DOMPoint(rect.x + rect.width, rect.y),
                        new DOMPoint(rect.x + rect.width, rect.y + rect.height),
                        new DOMPoint(rect.x, rect.y + rect.height));
                    };
                DOMQuad.fromPoint = function (p1, p2, p3, p4) {
                    return new DOMQuad(p1, p2, p3, p4);
                };
                Object.defineProperty(DOMQuad.prototype, Symbol.toStringTag,
                    { value: 'DOMQuad', configurable: true });
                window.DOMQuad = DOMQuad;
                (window.__domProto || {}).DOMQuad = DOMQuad.prototype;

            })();

            // ── NodeFilter (DOM §4.6.2) ─────────────────────────────────────────
            // The whatToShow bitmask and the three answers a filter callback gives. SHOW_ALL is
            // the unsigned 0xffffffff the reference reports (measured: 4294967295, not -1), and the
            // entity bits run in the order the interfaces are named: a reference stands in for the
            // entity it points at, so it takes the lower bit.
            window.NodeFilter = {
                SHOW_ALL: 0xffffffff,
                SHOW_ELEMENT: 1,
                SHOW_ATTRIBUTE: 2,
                SHOW_TEXT: 4,
                SHOW_CDATA_SECTION: 8,
                SHOW_ENTITY_REFERENCE: 16,
                SHOW_ENTITY: 32,
                SHOW_PROCESSING_INSTRUCTION: 64,
                SHOW_COMMENT: 128,
                SHOW_DOCUMENT: 256,
                SHOW_DOCUMENT_TYPE: 512,
                SHOW_DOCUMENT_FRAGMENT: 1024,
                SHOW_NOTATION: 2048,
                FILTER_ACCEPT: 1,
                FILTER_REJECT: 2,
                FILTER_SKIP: 3
            };

            // ── Range and Selection (DOM §3.4–3.6, HTML §2.5.3.4) ─────────────────
            // The document makes both of them, and 'Range' is constructible as well: 'new Range()'
            // answers a range collapsed at the document, which is the range 'document.createRange()'
            // gives (measured). AbstractRange sits above it and StaticRange beside it, and the four
            // comparison modes the page passes to 'compareBoundaryPoints' are own properties of both the
            // interface object and its prototype (measured: enumerable, not writable, not configurable).
            // 'new.target' is what tells a constructor call from a plain one: the reference refuses the
            // second with a sentence of its own (measured for Range and StaticRange).
            (function () {
                var known = window.__domProto || {};
                // The two boundary points, for the objects the engine does not make: a StaticRange is a
                // JavaScript object that carries its points under this symbol, and the accessors on
                // AbstractRange.prototype read them back. A range the engine makes answers its own
                // members before the prototype is looked at, so these getters never see it.
                var points = Symbol('acruxRangePoints');
                var pointNames = ['startContainer', 'startOffset', 'endContainer', 'endOffset'];
                function buildInterface(name, base, construct) {
                    var Ctor = construct || function () {
                        throw new TypeError('Failed to construct \'' + name + '\': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = base ? Object.create(base.prototype) : Object.create(Object.prototype);
                    Object.defineProperty(Ctor.prototype, 'constructor', { value: Ctor, configurable: true, writable: true });
                    // What 'Object.prototype.toString.call(range)' answers (measured: '[object Range]').
                    Object.defineProperty(Ctor.prototype, Symbol.toStringTag,
                        { value: name, writable: false, enumerable: false, configurable: true });
                    window[name] = Ctor;
                    known[name] = Ctor.prototype;
                    return Ctor;
                }
                function defConstant(owner, key, value) {
                    Object.defineProperty(owner, key,
                        { value: value, writable: false, enumerable: true, configurable: false });
                }
                function abstractAccessors(proto) {
                    var keys = pointNames.concat(['collapsed']);
                    for (var k = 0; k < keys.length; k++) {
                        (function (key) {
                            Object.defineProperty(proto, key, {
                                configurable: true, enumerable: true,
                                get: function () {
                                    var p = this[points];
                                    if (!p) return undefined;
                                    if (key === 'collapsed') return p[0] === p[2] && p[1] === p[3];
                                    return p[pointNames.indexOf(key)];
                                }
                            });
                        })(keys[k]);
                    }
                }

                var AbstractRangeCtor = buildInterface('AbstractRange');
                abstractAccessors(AbstractRangeCtor.prototype);
                var RangeCtor = buildInterface('Range', AbstractRangeCtor, function () {
                    if (!new.target)
                        throw new TypeError('Failed to construct \'Range\': Please use the \'new\' operator,'
                            + ' this DOM object constructor cannot be called as a function.');
                    return document.createRange();
                });
                var modes = ['START_TO_START', 'START_TO_END', 'END_TO_END', 'END_TO_START'];
                for (var m = 0; m < modes.length; m++) {
                    defConstant(RangeCtor, modes[m], m);
                    defConstant(RangeCtor.prototype, modes[m], m);
                }
                var StaticRangeCtor = buildInterface('StaticRange', AbstractRangeCtor, function (init) {
                    var where = 'Failed to construct \'StaticRange\': ';
                    if (!new.target)
                        throw new TypeError(where + 'Please use the \'new\' operator, this DOM object'
                            + ' constructor cannot be called as a function.');
                    if (arguments.length < 1)
                        throw new TypeError(where + '1 argument required, but only 0 present.');
                    if (init === null || typeof init !== 'object')
                        throw new TypeError(where + 'The provided value is not of type \'StaticRangeInit\'.');
                    // A dictionary's members are read in the order the specification lists them, which is
                    // alphabetical, so the range that gives no offsets is refused for 'endOffset' first
                    // (measured), and an offset the page gave is coerced as an unsigned long after the
                    // required-member check ('null' is a value, so it becomes 0 rather than refusing).
                    var order = ['endContainer', 'endOffset', 'startContainer', 'startOffset'];
                    var read = {};
                    for (var i = 0; i < order.length; i++) {
                        var key = order[i];
                        var value = init[key];
                        if (value === undefined)
                            throw new TypeError(where + 'Failed to read the \'' + key + '\' property from'
                                + ' \'StaticRangeInit\': Required member is undefined.');
                        if (key === 'endContainer' || key === 'startContainer') {
                            if (typeof value !== 'object' || typeof value.nodeType !== 'number')
                                throw new TypeError(where + 'Failed to read the \'' + key + '\' property'
                                    + ' from \'StaticRangeInit\': Failed to convert value to \'Node\'.');
                        } else {
                            value = value >>> 0;
                        }
                        read[key] = value;
                    }
                    this[points] = [read.startContainer, read.startOffset, read.endContainer, read.endOffset];
                    return this;
                });

                var SelectionCtor = buildInterface('Selection');
                var SP = SelectionCtor.prototype;
                // What the host cannot see is settled here: the reference counts a method's arguments
                // before it looks at their types, so 'collapse()' refuses with the argument-count
                // sentence rather than with a type error about parameter 1 (measured for every one of
                // these). Each wrapper hands the call to the host member below its own name, which does
                // the type checks, the unsigned-long offsets and the range work — and quotes the name
                // the page called, which is why 'setPosition' and 'collapse' have separate members
                // although they do the same thing. The wrapper's 'length' is the number of required
                // arguments, exactly as the reference reports it (measured: 'setPosition.length' is 1,
                // 'setBaseAndExtent.length' is 4).
                function selectionMethod(name, host, required) {
                    function wrapped() {
                        if (arguments.length < required)
                            throw new TypeError('Failed to execute \'' + name + '\' on \'Selection\': '
                                + required
                                + (required === 1 ? ' argument required, but only ' : ' arguments required, but only ')
                                + arguments.length + ' present.');
                        return this[host].apply(this, arguments);
                    }
                    Object.defineProperty(wrapped, 'length', { value: required, configurable: true });
                    return wrapped;
                }
                var counted = ['addRange', 'removeRange', 'getRangeAt', 'collapse', 'setPosition',
                               'extend', 'selectAllChildren', 'containsNode'];
                for (var c = 0; c < counted.length; c++) {
                    Object.defineProperty(SP, counted[c], {
                        value: selectionMethod(counted[c], '__' + counted[c], 1),
                        writable: true, enumerable: true, configurable: true
                    });
                }
                Object.defineProperty(SP, 'setBaseAndExtent', {
                    value: selectionMethod('setBaseAndExtent', '__setBaseAndExtent', 4),
                    writable: true, enumerable: true, configurable: true
                });
                // The sequence of ranges has to reach the page as an Array, and a CLR array arrives as an
                // array-like, so the list is built here from the clones the selection answers with — fresh
                // objects every time, which is what the reference gives (measured: 'getComposedRanges()[0]
                // !== getRangeAt(0)'). 'empty' is the older name for 'removeAllRanges' (measured: it
                // clears, and the selection reports 'None' after it).
                Object.defineProperty(SP, 'getComposedRanges', {
                    writable: true, enumerable: true, configurable: true,
                    value: function () {
                        var list = [];
                        for (var i = 0; i < this.rangeCount; i++) list.push(this.__composedRange(i));
                        return list;
                    }
                });
                Object.defineProperty(SP, 'empty', {
                    writable: true, enumerable: true, configurable: true,
                    value: function () { return this.removeAllRanges(); }
                });
                window.getSelection = function () { return document.getSelection(); };
            })();

            // ── TreeWalker and NodeIterator (DOM 4 §4.7) ─────────────────────────
            // Neither is constructible either — the document makes them — and both the factory and
            // the 'filter' read live here rather than on the host because a JavaScript object handed
            // to a host method is converted on the way in, so no host can give the very object back.
            // The page's filter is therefore registered while it is still itself, the traverser is
            // made from the id that registration answers, and 'walker.filter' reads the original out
            // of that store — which is what makes 'walker.filter === f' hold (measured).
            (function () {
                var known = window.__domProto || {};
                function iface(name) {
                    var Ctor = function () {
                        throw new TypeError('Failed to construct \'' + name + '\': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = Object.create(Object.prototype);
                    Object.defineProperty(Ctor.prototype, 'constructor', { value: Ctor, configurable: true, writable: true });
                    Object.defineProperty(Ctor.prototype, Symbol.toStringTag,
                        { value: name, writable: false, enumerable: false, configurable: true });
                    window[name] = Ctor;
                    known[name] = Ctor.prototype;
                    return Ctor;
                }
                // The members of these interfaces are enumerable on the prototype, and 'filter' is a
                // getter with no setter (measured: 'Object.keys(TreeWalker.prototype)' answers them,
                // and a walker itself has no own property).
                function defFilter(proto) {
                    if (!proto) return;
                    Object.defineProperty(proto, 'filter', {
                        configurable: true, enumerable: true,
                        get: function () {
                            if (typeof __acruxFilterOf !== 'function') return null;
                            return __acruxFilterOf(this.__filterId);
                        }
                    });
                }
                defFilter(iface('TreeWalker').prototype);
                defFilter(iface('NodeIterator').prototype);

                // What the host cannot see is settled here: the argument count, the unsigned-long
                // coercion of whatToShow ('4' shows text, 'abc' and null show nothing, -1 shows
                // everything, 4294967296 wraps to 0 — measured), and that the filter is an object, a
                // function or nothing at all (a number is the TypeError the reference gives for
                // parameter 3). The traverser itself is made by the host, from the id.
                function traverserFactory(name, make) {
                    return function (root, whatToShow, filter) {
                        var where = 'Failed to execute \'' + name + '\' on \'Document\': ';
                        if (arguments.length < 1)
                            throw new TypeError(where + '1 argument required, but only 0 present.');
                        var mask = whatToShow === undefined ? 4294967295 : (Number(whatToShow) >>> 0);
                        if (filter !== undefined && filter !== null
                            && typeof filter !== 'object' && typeof filter !== 'function')
                            throw new TypeError(where + 'parameter 3 is not of type \'Object\'.');
                        var id = (typeof __acruxStoreFilter === 'function') ? __acruxStoreFilter(filter) : 0;
                        return this[make](root, mask, id);
                    };
                }
                var DP = known['Document'] || (window.Document && window.Document.prototype);
                if (DP) {
                    Object.defineProperty(DP, 'createTreeWalker', {
                        value: traverserFactory('createTreeWalker', '__makeTreeWalker'),
                        writable: true, enumerable: true, configurable: true
                    });
                    Object.defineProperty(DP, 'createNodeIterator', {
                        value: traverserFactory('createNodeIterator', '__makeNodeIterator'),
                        writable: true, enumerable: true, configurable: true
                    });
                }
            })();

            // ── Cross-document messaging (HTML §8.6) ─────────────────────────────
            // A posted message is a task, so it lands after the script that posted it — which is
            // what a page relying on the ordering sees in a browser. The channel registry is the
            // browsing context's: every channel with the same name in this document hears it.
            (function () {
                function fireEvent(target, listeners, onName, event) {
                    event.target = target;
                    var list = listeners || [];
                    for (var i = 0; i < list.length; i++) {
                        var listener = list[i];
                        if (!listener) continue;
                        try {
                            if (typeof listener === 'function') listener.call(target, event);
                            else if (typeof listener.handleEvent === 'function') listener.handleEvent(event);
                        } catch (e) { }
                    }
                    if (typeof target[onName] === 'function') {
                        try { target[onName](event); } catch (e) { }
                    }
                }

                function makeTarget(proto) {
                    proto.addEventListener = function (type, listener) {
                        if (!listener) return;
                        this._listeners = this._listeners || {};
                        var list = this._listeners[type] = this._listeners[type] || [];
                        if (list.indexOf(listener) < 0) list.push(listener);
                    };
                    proto.removeEventListener = function (type, listener) {
                        var list = this._listeners && this._listeners[type];
                        if (!list) return;
                        var index = list.indexOf(listener);
                        if (index >= 0) list.splice(index, 1);
                    };
                    proto.dispatchEvent = function (event) {
                        if (event && event.type) fireEvent(this, this._listeners && this._listeners[event.type], 'on' + event.type, event);
                        return true;
                    };
                    // An IDL event handler attribute sits on the prototype and stores the function
                    // beside the listener bag, so assigning one never gives the object a key of its own
                    // (measured: a port owns no keys at all, and reading a handler that was never
                    // installed answers null).
                    if (!Object.prototype.hasOwnProperty.call(proto, 'onmessage')) {
                        Object.defineProperty(proto, 'onmessage', {
                            configurable: true, enumerable: false,
                            get: function () { return handlerBag(this).message; },
                            set: function (fn) {
                                handlerBag(this).message = typeof fn === 'function' ? fn : null;
                            }
                        });
                    }
                    return proto;
                }
                function handlerBag(self) {
                    if (!Object.prototype.hasOwnProperty.call(self, '__handlers')) {
                        Object.defineProperty(self, '__handlers', { value: { message: null },
                            writable: true, enumerable: false, configurable: true });
                    }
                    var bag = self.__handlers;
                    if (bag.message === undefined) bag.message = null;
                    return bag;
                }

                // A message event carries data, which the engine's event host has no slot for, so
                // the whole event is built in script: the flags, the propagation methods and the
                // MessageEvent members all read the way the interface defines them.
                function MessageEvent(type, init) {
                    init = init || {};
                    var evt = {
                        type: String(type === undefined ? '' : type),
                        bubbles: !!init.bubbles,
                        cancelable: !!init.cancelable,
                        composed: !!init.composed,
                        isTrusted: false,
                        defaultPrevented: false,
                        data: init.data,
                        origin: init.origin === undefined ? '' : String(init.origin),
                        lastEventId: init.lastEventId === undefined ? '' : String(init.lastEventId),
                        source: init.source === undefined ? null : init.source,
                        ports: init.ports === undefined ? [] : init.ports,
                        target: null,
                        currentTarget: null,
                        eventPhase: 0,
                        preventDefault: function () { if (this.cancelable) this.defaultPrevented = true; },
                        stopPropagation: function () { this._stopped = true; },
                        stopImmediatePropagation: function () { this._stopped = true; this._immediateStopped = true; }
                    };
                    return evt;
                }

                // A port is one end of a channel: what goes in one end comes out of the other.
                // The bookkeeping an end keeps is hidden — a page that walks a port sees the interface,
                // not the engine's queue under it.
                function hide(self, key, value) {
                    Object.defineProperty(self, key, { value: value, writable: true,
                        enumerable: false, configurable: true });
                }
                function MessagePort() {
                    hide(this, '_queue', []);
                    hide(this, '_listeners', {});
                    hide(this, '_other', null);
                    hide(this, '_started', false);
                    hide(this, '_closed', false);
                }
                makeTarget(MessagePort.prototype);
                MessagePort.prototype.postMessage = function (data) {
                    if (this._closed) return;
                    var other = this._other;
                    if (!other) return;
                    other._queue.push(data);
                    var port = other;
                    setTimeout(function () { port._drain(); }, 0);
                };
                MessagePort.prototype.start = function () {
                    this._started = true;
                    this._drain();
                };
                MessagePort.prototype.close = function () {
                    this._closed = true;
                    this._queue.length = 0;
                };
                // A port that has neither been started nor given an 'onmessage' handler keeps its
                // queue, which is what lets a page install a listener before the first message
                // arrives without losing what was already posted (§8.6.8.4).
                MessagePort.prototype._drain = function () {
                    if (this._closed) return;
                    if (!this._started && typeof this.onmessage !== 'function') return;
                    while (this._queue.length) {
                        var data = this._queue.shift();
                        fireEvent(this, this._listeners.message, 'onmessage', new MessageEvent('message', { data: data }));
                    }
                };

                var CHANNEL_SLOTS = new WeakMap();
                function MessageChannel() {
                    var port1 = new MessagePort();
                    var port2 = new MessagePort();
                    port1._other = port2;
                    port2._other = port1;
                    CHANNEL_SLOTS.set(this, { port1: port1, port2: port2 });
                }
                function channelSlots(channel) {
                    var slots = CHANNEL_SLOTS.get(channel);
                    if (!slots) throw new TypeError('Illegal invocation');
                    return slots;
                }
                // The two ends read through the interface, so a channel owns nothing a page can list.
                ['port1', 'port2'].forEach(function (field) {
                    Object.defineProperty(MessageChannel.prototype, field, { configurable: true,
                        enumerable: false,
                        get: function () { return channelSlots(this)[field]; } });
                });


                var CHANNEL_NAMED = {};
                function BroadcastChannel(name) {
                    var self = this;
                    // The name is a read the interface declares, and the handler sits on the
                    // prototype, so a channel owns no key of its own (measured: a port lists none).
                    var made = String(name);
                    var list = CHANNEL_NAMED[made] = CHANNEL_NAMED[made] || [];
                    hide(this, 'name', made);
                    hide(this, '_listeners', {});
                    hide(this, '_closed', false);
                    list.push(self);
                }
                makeTarget(BroadcastChannel.prototype);
                BroadcastChannel.prototype.postMessage = function (data) {
                    if (this._closed) return;
                    var list = CHANNEL_NAMED[this.name] || [];
                    for (var i = 0; i < list.length; i++) {
                        var channel = list[i];
                        // The channel that posted does not hear its own message; the other ends of
                        // the same name do (§8.6.10 step 5).
                        if (channel === this || channel._closed) continue;
                        (function (target) {
                            setTimeout(function () {
                                if (target._closed) return;
                                fireEvent(target, target._listeners.message, 'onmessage',
                                    new MessageEvent('message', { data: data, origin: location.origin }));
                            }, 0);
                        })(channel);
                    }
                };
                BroadcastChannel.prototype.close = function () {
                    this._closed = true;
                    var list = CHANNEL_NAMED[this.name] || [];
                    var index = list.indexOf(this);
                    if (index >= 0) list.splice(index, 1);
                };

                window.MessageEvent = MessageEvent;
                window.MessagePort = MessagePort;
                window.MessageChannel = MessageChannel;
                window.BroadcastChannel = BroadcastChannel;
                // The tag each interface gives the objects it builds (measured: a channel reads
                // [object MessageChannel], a port [object MessagePort], a broadcast channel
                // [object BroadcastChannel]).
                var messageProtoMap = window.__domProto || (window.__domProto = {});
                [['MessageChannel', MessageChannel], ['MessagePort', MessagePort],
                 ['BroadcastChannel', BroadcastChannel]].forEach(function (pair) {
                    Object.defineProperty(pair[1].prototype, Symbol.toStringTag,
                        { value: pair[0], configurable: true, enumerable: false });
                    if (!messageProtoMap[pair[0]]) messageProtoMap[pair[0]] = pair[1].prototype;
                });

                // window.postMessage(message, targetOrigin) — the message comes back to this window
                // as a message event, which is what a page that shells out to itself sees. The
                // listeners live on the window (see window.addEventListener). The interface's length is
                // one: the origin and the transfer list are optional arguments (measured).
                window.postMessage = function (message) {
                    setTimeout(function () {
                        fireEvent(window, window.__winMessage || [], 'onmessage',
                            new MessageEvent('message', { data: message, origin: location.origin, source: window }));
                    }, 0);
                };
            })();

            // ── The members the element and document interfaces declare ────────
            // (DOM §4.2, CSSOM View §2.4, §3.10, HTML §7.3.4, §12.5.2, Web Animations §2-§4,
            //  Houdini CSS Typed OM §3-§6, Fullscreen §5, Form Data §4.15.5)
            // An element wrapper answers its own members first, so a member the host already
            // implements needs only its place on the interface: a page that reads
            // 'Element.prototype.matches' gets a function, and a call through a receiver that is
            // not an element is refused the way the reference refuses it. What the host does not
            // carry — the animation a page starts, the map a computed style reads through, the
            // internals of a custom element, the text an element shows — is built here for real.
            (function () {
                var levels = window.__domProto || (window.__domProto = {});
                function quoted(value) { return String.fromCharCode(39) + value + String.fromCharCode(39); }
                function refusal() { throw new TypeError('Illegal invocation'); }
                function isNode(value) { return !!value && typeof value.nodeType === 'number'; }
                function method(iface, name, body) {
                    var proto = levels[iface];
                    if (!proto) return;
                    try {
                        Object.defineProperty(proto, name, {
                            configurable: true, writable: true, enumerable: false, value: body
                        });
                    } catch (e) { }
                }
                // A member the host implements: the function is the interface's own, and only ever
                // runs for a receiver the engine did not make. The reference says the interface and
                // the operation, not just the invocation, and a page that reads 'err.message' out of
                // a log expects that wording (CSSOM View §2.2, DOM §4.2.13; both measured).
                function brandMethod(iface, name) {
                    method(iface, name, function () {
                        throw new TypeError('Failed to execute ' + quoted(name) + ' on ' + quoted(iface)
                            + ': Illegal invocation');
                    });
                }
                function brandRead(iface, name, value) {
                    var proto = levels[iface];
                    if (!proto) return;
                    try {
                        Object.defineProperty(proto, name, {
                            configurable: true, enumerable: false,
                            get: function () { if (!isNode(this)) refusal(); return typeof value === 'function' ? value(this) : value; },
                            set: refusal
                        });
                    } catch (e) { }
                }
                ['matches', 'closest', 'getClientRects', 'getBoundingClientRect', 'scrollBy',
                 'getAttributeNames', 'hasAttributes', 'isSameNode', 'insertAdjacentElement',
                 'insertAdjacentHTML', 'insertAdjacentText', 'remove', 'replaceChildren',
                 'attachShadow', 'setPointerCapture', 'releasePointerCapture', 'hasPointerCapture',
                 'scrollIntoView', 'matchesSelector'].forEach(function (name) {
                    brandMethod('Element', name);
                });
                ['append', 'prepend', 'before', 'after', 'replaceWith'].forEach(function (name) {
                    brandMethod('ChildNode' in levels ? 'ChildNode' : 'Element', name);
                    brandMethod('Element', name);
                });
                ['contains', 'normalize', 'cloneNode', 'insertBefore', 'appendChild',
                 'replaceChild', 'removeChild', 'isEqualNode', 'compareDocumentPosition',
                 'lookupPrefix', 'lookupNamespaceURI', 'getRootNode'].forEach(function (name) {
                    brandMethod('Node', name);
                });
                brandRead('Node', 'assignedSlot', null);
                brandRead('Node', 'baseURI', function (node) { return node.baseURI; });
                method('HTMLElement', 'click', function () {
                    if (!isNode(this)) refusal();
                    this.click();
                });
                brandMethod('HTMLElement', 'attachInternals');
                brandMethod('Document', 'querySelector');
                brandMethod('Document', 'querySelectorAll');

                // ── The animation a page starts (Web Animations §2, §4, §5) ──────
                // The engine has no compositor to drive, so an animation runs on the timer queue and
                // the page reads the state the interface names: it is playing until its own end,
                // 'finish' and 'cancel' move it, and the callbacks fire on the task the reference
                // would have run them on. Nothing is painted from it — that part stays a gap.
                var ANIMATIONS = new WeakMap();
                var animationSeq = 0;
                // The animation interfaces (Web Animations §2, §3, §4): the levels are registered
                // before the first animation is built, because an animation's own prototype is the
                // level — the tag, 'anim instanceof Animation' and the members the interface declares
                // all read from there (measured: [object Animation] with playState 'running').
                function namedLevel(name, baseName) {
                    var proto = levels[name] || Object.create(levels[baseName] || Object.prototype);
                    levels[name] = proto;
                    var earlier = window[name];
                    var Ctor = typeof earlier === 'function' ? earlier : function () {
                        throw new TypeError('Failed to construct ' + quoted(name)
                            + ': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = proto;
                    Object.defineProperty(proto, 'constructor',
                        { value: Ctor, writable: true, configurable: true });
                    Object.defineProperty(proto, Symbol.toStringTag, { value: name, configurable: true });
                    window[name] = Ctor;
                    return proto;
                }
                namedLevel('AnimationEffect', null);
                namedLevel('KeyframeEffect', 'AnimationEffect');
                namedLevel('Animation', null);
                function animProto() { return levels.Animation || Object.prototype; }
                function animSlots(anim) {
                    var slots = ANIMATIONS.get(anim);
                    if (!slots) refusal();
                    return slots;
                }
                function timingOf(options) {
                    var read = options;
                    if (read === undefined || read === null) return { duration: 0, delay: 0, endDelay: 0, iterations: 1, iterationStart: 0 };
                    if (typeof read !== 'object') read = { duration: read };
                    var duration = read.duration === undefined || read.duration === 'auto'
                        ? 0 : Number(read.duration) || 0;
                    return { duration: duration, delay: Number(read.delay) || 0,
                        endDelay: Number(read.endDelay) || 0,
                        iterations: read.iterations === undefined ? 1 : Number(read.iterations),
                        iterationStart: Number(read.iterationStart) || 0,
                        direction: read.direction === undefined ? 'normal' : String(read.direction),
                        fill: read.fill === undefined ? 'none' : String(read.fill),
                        easing: read.easing === undefined ? 'linear' : String(read.easing) };
                }
                function fireAnimation(anim, type) {
                    var slots = ANIMATIONS.get(anim);
                    if (!slots) return;
                    var event = null;
                    try {
                        event = new Event(type);
                        event.animation = anim;
                        event.timelineTime = slots.currentTime || 0;
                    } catch (e) { }
                    var handler = slots['on' + type];
                    if (typeof handler === 'function') { try { handler.call(anim, event); } catch (e) { } }
                    if (event) { try { anim.dispatchEvent(event); } catch (e) { } }
                }
                function Animation(target, keyframes, options) {
                    var self = this;
                    var timing = timingOf(options);
                    var shared = ANIMATIONS.get(self);
                    var slots = shared || {
                        playState: 'idle', currentTime: 0, startTime: null, playbackRate: 1,
                        pending: false, replaceState: 'active', id: '', timing: timing,
                        target: target || null, keyframes: keyframes, onfinish: null,
                        oncancel: null, onremove: null, timer: 0, holdTime: 0
                    };
                    if (!shared) ANIMATIONS.set(self, slots);
                    var made = Object.create(animProto());
                    ANIMATIONS.set(made, slots);
                    slots.animation = made;
                    made.play = function () {
                        var at = performance.now();
                        if (slots.playState === 'running') return;
                        slots.playState = 'running';
                        slots.startTime = at - slots.currentTime;
                        schedule();
                    };
                    made.pause = function () {
                        if (slots.timer) { clearTimeout(slots.timer); slots.timer = 0; }
                        slots.playState = 'paused';
                    };
                    made.finish = function () {
                        if (slots.timer) { clearTimeout(slots.timer); slots.timer = 0; }
                        slots.currentTime = total();
                        slots.playState = 'finished';
                    };
                    made.cancel = function () {
                        if (slots.timer) { clearTimeout(slots.timer); slots.timer = 0; }
                        slots.currentTime = 0;
                        slots.playState = 'idle';
                        fireAnimation(made, 'cancel');
                    };
                    made.reverse = function () {
                        slots.playbackRate = -slots.playbackRate;
                        made.play();
                    };
                    made.updatePlaybackRate = function (rate) {
                        slots.playbackRate = Number(rate) || 0;
                        return Promise.resolve(undefined);
                    };
                    made.commitStyles = function () { return undefined; };
                    made.persist = function () { };
                    made.addEventListener = function () { };
                    made.removeEventListener = function () { };
                    made.dispatchEvent = function () { return true; };
                    return made;
                }
                // The lists every animated object keeps, so 'getAnimations' answers what was started
                // and never finished or cancelled.
                function animationsOf(node) {
                    if (!isNode(node)) refusal();
                    var list = ANIMATIONS.get(node);
                    if (!list) { list = []; ANIMATIONS.set(node, list); }
                    var live = [];
                    for (var i = list.length - 1; i >= 0; i--) {
                        var slots = ANIMATIONS.get(list[i]);
                        if (!slots || slots.playState === 'idle') { list.splice(i, 1); continue; }
                        live.push(list[i]);
                    }
                    return live;
                }
                method('Element', 'getAnimations', function () { return animationsOf(this); });
                method('Document', 'getAnimations', function () { return animationsOf(this); });
                method('Element', 'animate', function (keyframes, options) {
                    if (!isNode(this)) refusal();
                    var made = new Animation(null, null, null);
                    var slots = ANIMATIONS.get(made);
                    if (!ANIMATIONS.get(this)) ANIMATIONS.set(this, []);
                    slots.target = this;
                    slots.keyframes = keyframes;
                    // The timing the page asked for belongs to this animation, not to the empty one
                    // built above: the constructor was handed nothing, so the dictionary is read here.
                    slots.timing = timingOf(options);
                    var span = slots.timing.duration * (slots.timing.iterations === Infinity ? 1
                        : Math.max(0, Number(slots.timing.iterations) || 0));
                    slots.span = span;
                    ANIMATIONS.get(this).push(made);
                    made.play = function () {
                        slots.playState = 'running';
                        slots.startTime = performance.now() - slots.currentTime;
                        if (!slots.span) { slots.playState = 'finished'; return; }
                        if (slots.timer) clearTimeout(slots.timer);
                        if (span > 0) {
                            slots.timer = setTimeout(function () {
                                slots.timer = 0;
                                if (slots.playState !== 'running') return;
                                slots.currentTime = span;
                                slots.playState = 'finished';
                                fireAnimation(made, 'finish');
                            }, span);
                        }
                    };
                    made.play();
                    return made;
                });
                // The clock an animation runs on is the engine's own: the state is recomputed when the
                // page reads it rather than only when a timer happens to come round (Web Animations
                // §3), and the finish callback is heard exactly once — the first read past the end,
                // or the timer that gets there first.
                function advance(slots) {
                    if (slots.playState !== 'running' || !slots.timer) return;
                    var elapsed = performance.now() - slots.startTime;
                    if (elapsed < slots.span) { slots.currentTime = elapsed > 0 ? elapsed : 0; return; }
                    slots.currentTime = slots.span;
                    slots.playState = 'finished';
                    if (slots.timer) { clearTimeout(slots.timer); slots.timer = 0; }
                    fireAnimation(slots.animation, 'finish');
                }
                ['effect', 'timeline', 'startTime', 'currentTime', 'playbackRate', 'contextTime',
                 'rangeStart', 'rangeEnd', 'playState', 'replaceState', 'pending', 'id', 'onfinish',
                 'oncancel', 'onremove', 'finished', 'ready'].forEach(function (field) {
                    Object.defineProperty(animProto(), field, {
                        configurable: true, enumerable: false,
                        get: function () {
                            var slots = ANIMATIONS.get(this);
                            if (!slots) refusal();
                            advance(slots);
                            if (field === 'finished' || field === 'ready') return Promise.resolve(this);
                            if (field === 'timeline') return document.timeline;
                            if (field === 'effect') return slots.keyframes === undefined ? null
                                : Object.create(levels.KeyframeEffect || Object.prototype);
                            if (field === 'currentTime') return slots.currentTime;
                            if (field === 'startTime') return slots.startTime;
                            return slots[field];
                        },
                        set: function (value) {
                            var slots = ANIMATIONS.get(this);
                            if (!slots) refusal();
                            if (field === 'currentTime') {
                                slots.currentTime = Number(value) || 0;
                                return;
                            }
                            if (field === 'playbackRate') { slots.playbackRate = Number(value) || 0; return; }
                            slots[field] = value === undefined ? null : value;
                        }
                    });
                });
                method('Animation', 'play', function () { refusal(); });
                method('Animation', 'pause', function () { refusal(); });
                method('Animation', 'finish', function () { refusal(); });
                method('Animation', 'cancel', function () { refusal(); });
                method('Animation', 'reverse', function () { refusal(); });
                method('Animation', 'commitStyles', function () { refusal(); });

                // Houdini CSS Typed OM §3, §5: the map a computed style is read through, and the
                // values it hands out. The engine has no parsed value objects to give yet, so every
                // property reads as the keyword the CSS text holds — the name, the read and the size
                // of the map are the interface's own.
                function StyleValue() { refusal(); }
                function CSSUnitValue(value, unit) {
                    if (!(this instanceof CSSUnitValue)) {
                        throw new TypeError('Failed to construct ' + quoted('CSSUnitValue')
                            + ': Please use the ' + quoted('new') + ' operator');
                    }
                    this.value = Number(value) || 0;
                    this.unit = String(unit === undefined ? '' : unit);
                }
                function CSSKeywordValue(value) {
                    if (!(this instanceof CSSKeywordValue)) {
                        throw new TypeError('Failed to construct ' + quoted('CSSKeywordValue')
                            + ': Please use the ' + quoted('new') + ' operator');
                    }
                    this.value = String(value === undefined ? '' : value);
                }
                function CSSMathValue(operator) {
                    throw new TypeError('Failed to construct ' + quoted('CSSMathValue')
                        + ': Illegal constructor');
                }
                function mapProto(name, baseName) {
                    // A map the page can build is hung below the read-only view it shares, so the
                    // name of the level above is looked up rather than used as the prototype itself.
                    var proto = Object.create(levels[baseName] || Object.prototype);
                    var Ctor = function () {
                        throw new TypeError('Failed to construct ' + quoted(name)
                            + ': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = proto;
                    Object.defineProperty(proto, 'constructor',
                        { value: Ctor, writable: true, configurable: true });
                    Object.defineProperty(proto, Symbol.toStringTag, { value: name, configurable: true });
                    window[name] = Ctor;
                    levels[name] = proto;
                    return proto;
                }
                var STYLE_MAP_SLOTS = new WeakMap();
                function mapEntries(receiver) {
                    var entries = STYLE_MAP_SLOTS.get(receiver);
                    if (!entries) refusal();
                    return entries;
                }
                var readOnlyProto = mapProto('StylePropertyMapReadOnly', null);
                var liveProto = mapProto('StylePropertyMap', 'StylePropertyMapReadOnly');
                // The value classes are the page's to build (CSS Typed OM §3): the keyword and the
                // unit are constructors, the mathematical value is not, and each name has to reach
                // the window — they were written in this block's own scope, so a page that asked for
                // 'CSSKeywordValue' heard nothing at all.
                // CSS Properties and Values API §4: a custom property the page registers is held by
                // name; the three fields the dictionary carries are kept beside it.
                var REGISTERED = { };
                if (!window.CSS) window.CSS = { };
                try {
                    Object.defineProperty(window.CSS, 'registerProperty', {
                        configurable: true, writable: true, enumerable: false,
                        value: function (definition) {
                            var read = definition || { };
                            var key = read.name === undefined ? '' : String(read.name);
                            if (key.length < 3 || key.substring(0, 2) !== '--') {
                                throw new DOMException('Failed to execute ' + quoted('registerProperty')
                                    + ' on ' + quoted('CSS') + ': The name provided must start with '
                                    + quoted('--') + '.', 'SyntaxError');
                            }
                            REGISTERED[key] = {
                                syntax: read.syntax === undefined ? '*' : String(read.syntax),
                                inherits: !!read.inherits,
                                initialValue: read.initialValue === undefined ? null : String(read.initialValue)
                            };
                        }
                    });
                } catch (e) { }
                window.CSSKeywordValue = CSSKeywordValue;
                window.CSSUnitValue = CSSUnitValue;
                window.CSSMathValue = CSSMathValue;
                Object.defineProperty(CSSKeywordValue.prototype, Symbol.toStringTag,
                    { value: 'CSSKeywordValue', configurable: true });
                Object.defineProperty(CSSUnitValue.prototype, Symbol.toStringTag,
                    { value: 'CSSUnitValue', configurable: true });
                Object.defineProperty(CSSMathValue.prototype, Symbol.toStringTag,
                    { value: 'CSSMathValue', configurable: true });
                levels.CSSKeywordValue = CSSKeywordValue.prototype;
                levels.CSSUnitValue = CSSUnitValue.prototype;
                levels.CSSMathValue = CSSMathValue.prototype;
                function asKeyword(text) {
                    return new CSSKeywordValue(text);
                }
                ['size', 'get', 'getAll', 'has', 'entries', 'forEach', 'keys', 'values', 'append',
                 'delete', 'set', 'clear'].forEach(function (name) {
                    if (name === 'size') {
                        Object.defineProperty(readOnlyProto, 'size', { configurable: true, enumerable: false,
                            get: function () { return Object.keys(mapEntries(this)).length; } });
                        return;
                    }
                    Object.defineProperty(readOnlyProto, name, {
                        configurable: true, writable: true, enumerable: false,
                        value: function (a, b) {
                            var entries = mapEntries(this);
                            if (name === 'get') return entries[a] === undefined ? null : asKeyword(entries[a]);
                            if (name === 'getAll') return entries[a] === undefined ? [] : [asKeyword(entries[a])];
                            if (name === 'has') return entries[a] !== undefined;
                            if (name === 'keys') return Object.keys(entries);
                            if (name === 'values') return Object.keys(entries).map(function (k) { return entries[k]; });
                            if (name === 'entries') return Object.keys(entries).map(function (k) { return [k, entries[k]]; });
                            if (name === 'forEach') {
                                Object.keys(entries).forEach(function (k) { a.call(b, entries[k], k); });
                                return undefined;
                            }
                            refusal();
                        }
                    });
                });
                method('Element', 'computedStyleMap', function () {
                    if (!isNode(this)) refusal();
                    var read = null;
                    try { read = window.getComputedStyle(this); } catch (e) { read = null; }
                    var entries = { };
                    // The declaration list names its own properties by index (CSSOM §5.3.2), which is
                    // the only list a computed style keeps — the camelCase members are a different set.
                    var count = read && read.length ? Number(read.length) : 0;
                    for (var i = 0; i < count; i++) {
                        var key = typeof read.item === 'function' ? read.item(i) : null;
                        if (key === null || key === undefined) continue;
                        entries[String(key)] = String(read.getPropertyValue(String(key)));
                    }
                    var made = Object.create(readOnlyProto);
                    STYLE_MAP_SLOTS.set(made, entries);
                    return made;
                });

                // Fullscreen §5: this engine draws its own window and has no full-screen surface to
                // ask for, so the request is refused with the error the specification names for an
                // element that cannot be made full screen.
                ['requestFullscreen', 'webkitRequestFullscreen'].forEach(function (name) {
                    method('Element', name, function () {
                        if (!isNode(this)) refusal();
                        return Promise.reject(new DOMException(
                            'Fullscreen is not available in this engine.', 'NotSupportedError'));
                    });
                });
                // Form Data §4.15.5: the internals a custom element holds. An element the page has
                // not defined is refused the way the reference refuses it (measured).
                var INTERNALS_SLOTS = new WeakMap();
                var internalsProto = levels.ElementInternals || (levels.ElementInternals =
                    Object.create(Object.prototype));
                if (!window.ElementInternals) {
                    var InternalsCtor = function () {
                        throw new TypeError('Failed to construct ' + quoted('ElementInternals')
                            + ': Illegal constructor');
                    };
                    Object.defineProperty(InternalsCtor, 'name',
                        { value: 'ElementInternals', configurable: true });
                    InternalsCtor.prototype = internalsProto;
                    Object.defineProperty(internalsProto, 'constructor',
                        { value: InternalsCtor, writable: true, configurable: true });
                    Object.defineProperty(internalsProto, Symbol.toStringTag,
                        { value: 'ElementInternals', configurable: true });
                    window.ElementInternals = InternalsCtor;
                }
                function internalsSlots(internals) {
                    var slots = INTERNALS_SLOTS.get(internals);
                    if (!slots) refusal();
                    return slots;
                }
                ['form', 'willValidate', 'validity', 'validationMessage', 'shadowRoot', 'states',
                 'role'].forEach(function (field) {
                    Object.defineProperty(internalsProto, field, { configurable: true, enumerable: false,
                        get: function () { return internalsSlots(this)[field]; } });
                });
                Object.defineProperty(internalsProto, 'labels', { configurable: true, enumerable: false,
                    get: function () {
                        var slots = internalsSlots(this);
                        if (!slots.formAssociated) {
                            // Form Data §4.15.5.11: the labels a form-associated custom element owns.
                            // An internals whose element never asked to be associated has none, and the
                            // reference reads that as a refusal naming the property (measured).
                            throw new DOMException('Failed to read the ' + quoted('labels') + ' property '
                                + 'from ' + quoted('ElementInternals') + ': The target element is not a '
                                + 'form-associated custom element.', 'NotSupportedError');
                        }
                        return slots.labels;
                    } });
                ['checkValidity', 'reportValidity', 'setFormValue', 'setValidity'].forEach(function (name) {
                    Object.defineProperty(internalsProto, name, {
                        configurable: true, writable: true, enumerable: false,
                        value: function () {
                            internalsSlots(this);
                            if (name === 'checkValidity' || name === 'reportValidity') return true;
                            return undefined;
                        }
                    });
                });
                // HTML §5.6: the bridge the history's own traversal calls. The event is built here,
                // through the interface's constructor, and sent down the tree the window's listeners
                // live on — the same path a dispatched event takes.
                window.__acruxPopState = function () {
                    var event = null;
                    try {
                        event = new PopStateEvent('popstate', {
                            state: history.state === undefined ? null : history.state });
                    } catch (e) { return; }
                    // The window's own listeners are held in the bag the window keeps (the same path
                    // a 'message' takes), because a 'popstate' belongs to the window and never walks
                    // the document tree; the tree is walked as well, so a page that registered on the
                    // document hears it too.
                    var bag = window.__winPopState || [];
                    for (var i = 0; i < bag.length; i++) {
                        try { bag[i].call(window, event); } catch (e) { }
                    }
                    try { document.dispatchEvent(event); } catch (e) { }
                    if (typeof window.onpopstate === 'function') {
                        try { window.onpopstate(event); } catch (e) { }
                    }
                };

                // Scheduler §2, §3: the task a page posts is queued on the timer queue the engine
                // already pumps, with the priority it asked for choosing how soon that pump reaches
                // it. The signal is the abort handle the task can be given; aborting it rejects the
                // promise with the reason the signal carries.
                function namedInterface(name, baseProto, constructible) {
                    var proto = levels[name] || Object.create(baseProto || Object.prototype);
                    levels[name] = proto;
                    var Ctor = constructible || function () {
                        throw new TypeError('Failed to construct ' + quoted(name)
                            + ': Illegal constructor');
                    };
                    Object.defineProperty(Ctor, 'name', { value: name, configurable: true });
                    Ctor.prototype = proto;
                    Object.defineProperty(proto, 'constructor',
                        { value: Ctor, writable: true, configurable: true });
                    Object.defineProperty(proto, Symbol.toStringTag, { value: name, configurable: true });
                    window[name] = Ctor;
                    return proto;
                }
                var TASK_SLOTS = new WeakMap();
                var taskSignalProto = namedInterface('TaskSignal', (levels.EventTarget || Object.prototype));
                ['priority', 'abortReason'].forEach(function (field) {
                    Object.defineProperty(taskSignalProto, field, { configurable: true, enumerable: false,
                        get: function () { var slots = TASK_SLOTS.get(this); if (!slots) refusal(); return slots[field]; } });
                });
                var schedulerProto = namedInterface('Scheduler', null);
                Object.defineProperty(schedulerProto, 'currentTask', { configurable: true, enumerable: false,
                    get: function () { return null; } });
                Object.defineProperty(schedulerProto, 'postTask', {
                    configurable: true, writable: true, enumerable: false,
                    value: function (callback, options) {
                        var self = this;
                        if (typeof callback !== 'function') {
                            throw new TypeError('Failed to execute ' + quoted('postTask') + ' on '
                                + quoted('Scheduler') + ': parameter 1 is not of type '
                                + quoted('Function') + '.');
                        }
                        var read = options || { };
                        var signal = read.signal === undefined ? null : read.signal;
                        var priority = read.priority === undefined ? 'user-visible' : String(read.priority);
                        if (signal && TASK_SLOTS.get(signal)) signal.__lastPriority = priority;
                        var delay = priority === 'background' ? 8 : (priority === 'user-blocking' ? 0 : 4);
                        return new Promise(function (resolve, reject) {
                            setTimeout(function () {
                                if (signal && signal.aborted) {
                                    reject(signal.reason === undefined
                                        ? new DOMException('The operation was aborted.', 'AbortError')
                                        : signal.reason);
                                    return;
                                }
                                try { resolve(callback()); } catch (e) { reject(e); }
                            }, delay);
                        });
                    }
                });
                Object.defineProperty(window, 'scheduler', { configurable: true, enumerable: true,
                    get: function () {
                        if (!window.__acruxScheduler) window.__acruxScheduler = Object.create(schedulerProto);
                        return window.__acruxScheduler;
                    } });

                method('HTMLElement', 'attachInternals', function () {
                    if (!isNode(this) || typeof this.tagName !== 'string') refusal();
                    if (this.tagName.indexOf('-') < 0) {
                        throw new DOMException('Failed to execute ' + quoted('attachInternals') + ' on '
                            + quoted('HTMLElement') + ': Unable to attach ElementInternals to '
                            + 'non-custom elements.', 'NotSupportedError');
                    }
                    var made = Object.create(internalsProto);
                    INTERNALS_SLOTS.set(made, { form: null, willValidate: false, validity: null,
                        validationMessage: '', shadowRoot: this.shadowRoot === undefined ? null
                            : this.shadowRoot, states: { }, role: null, labels: [],
                        formAssociated: false });
                    return made;
                });

                // HTML §12.5.2: the text an element shows. The engine has no rendered line boxes to
                // walk here, so the block runs of the markup stand in for them — each block child
                // contributes its own text and two breaks are joined into one — which gives the
                // reference's answers for the shapes pages actually read (measured: 'one\ntwo' for a
                // paragraph with a break, 'one\ntwo\n\nthree' for two paragraphs).
                var BLOCK_TEXT = {
                    ADDRESS: 1, ARTICLE: 1, ASIDE: 1, BLOCKQUOTE: 1, DD: 1, DETAILS: 1, DIV: 1,
                    DL: 1, DT: 1, FIELDSET: 1, FIGCAPTION: 1, FIGURE: 1, FOOTER: 1, FORM: 1, H1: 1,
                    H2: 1, H3: 1, H4: 1, H5: 1, H6: 1, HEADER: 1, HR: 1, LI: 1, MAIN: 1, NAV: 1,
                    OL: 1, P: 1, PRE: 1, SECTION: 1, TABLE: 1, TBODY: 1, TD: 1, TFOOT: 1, TH: 1,
                    THEAD: 1, TR: 1, UL: 1
                };
                function innerTextOf(node) {
                    if (node.nodeType === 3 || node.nodeType === 4) return node.data || '';
                    var out = '';
                    var children = node.childNodes || [];
                    for (var i = 0; i < children.length; i++) {
                        var child = children[i];
                        if (child.nodeType === 1) {
                            var tag = String(child.tagName || '').toUpperCase();
                            if (tag === 'BR') { out += '\n'; continue; }
                            if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'NOSCRIPT') continue;
                            var made = innerTextOf(child);
                            if (made === '') continue;
                            if (BLOCK_TEXT[tag]) {
                                // Each block is set off by a break of its own on either side, so two
                                // neighbouring blocks meet as two breaks — the reference's paragraph
                                // separator (measured: 'one\ntwo\n\nthree' for two paragraphs) — and
                                // three or more in a row fold back to two.
                                out += '\n' + made + '\n';
                            } else out += made;
                        } else if (child.nodeType === 3 || child.nodeType === 4) out += child.data || '';
                    }
                    return out;
                }
                var htmlProto = levels.HTMLElement || Object.prototype;
                if (!Object.prototype.hasOwnProperty.call(htmlProto, 'innerText')) {
                    try {
                        Object.defineProperty(htmlProto, 'innerText', {
                            configurable: true, enumerable: false,
                            get: function () {
                                if (!isNode(this)) refusal();
                                return innerTextOf(this).replace(/\n{3,}/g, '\n\n')
                                    .replace(/^\n+/, '').replace(/\s+$/, '');
                            },
                            set: function (text) {
                                if (!isNode(this)) refusal();
                                this.textContent = String(text === undefined ? '' : text);
                            }
                        });
                    } catch (e) { }
                }
                // HTML §7.3.4, DOM §3.2: the two reads the document and the history still leave out.
                brandRead('Document', 'scrollingElement', function (doc) { return doc.documentElement; });
            })();

            // Array.groupBy (ES2024 §23.1.10): sort an iterable into a null-prototype object keyed
            // by the string form of the grouper's result. Object.groupBy is the engine's own; the
            // static on Array is the same operation reached from the iterable side, and the
            // interpreter does not ship it, so it is filled in here over the iterator protocol.
            if (typeof Array.groupBy !== 'function') {
                Array.groupBy = function (iterable, grouper) {
                    var out = Object.create(null);
                    var it = iterable && (typeof iterable[Symbol.iterator] === 'function')
                        ? iterable[Symbol.iterator]()
                        : (function (arr) { var i = 0; return { next: function () { return i < arr.length ? { value: arr[i++], done: false } : { done: true }; } }; })(iterable || []);
                    for (var step = it.next(); !step.done; step = it.next()) {
                        var key = String(grouper(step.value));
                        if (out[key] === undefined) out[key] = [];
                        out[key].push(step.value);
                    }
                    return out;
                };
            }
        ";
    }

    private static string EscapeJsString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}

    public class FetchOptions
    {
        public string? Method { get; set; }
        public string? Body { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
    }

public class FetchResult
{
    public bool Success { get; set; }
    public string? Data { get; set; }
    public int Status { get; set; }
    public string? StatusText { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

internal class TimerInfo
{
    public int CallbackId { get; set; }
    public long DueTime { get; set; }
    public int Interval { get; set; }
}

public class WindowHost
{
    private readonly JavaScriptEngine _engine;

    public WindowHost(JavaScriptEngine engine) => _engine = engine;

    public void alert(object? message)
    {
        var msg = message?.ToString() ?? "";
        if (_engine.ShowDialog != null)
        {
            _engine.ShowDialog(msg, "alert");
        }
        else
        {
            Console.WriteLine($"[Alert] {msg}");
        }
    }

    public bool confirm(object? message)
    {
        var msg = message?.ToString() ?? "";
        if (_engine.ShowDialog != null)
        {
            var result = _engine.ShowDialog(msg, "confirm");
            return result == "true";
        }
        else
        {
            Console.Write($"[Confirm] {msg} (y/N): ");
            var key = Console.ReadLine()?.Trim().ToLowerInvariant();
            return key == "y" || key == "yes";
        }
    }

    public string? prompt(object? message, object? defaultValue)
    {
        var msg = message?.ToString() ?? "";
        var def = defaultValue?.ToString() ?? "";
        if (_engine.ShowDialog != null)
        {
            // The default is display input for the dialog, not a fallback answer: a
            // dismissed prompt must reach script as null.
            return _engine.ShowDialog(msg, "prompt:" + def);
        }
        else
        {
            Console.Write($"[Prompt] {msg} [{def}]: ");
            var input = Console.ReadLine();
            return string.IsNullOrEmpty(input) ? def : input;
        }
    }
}

public class AcruxBuiltins
{
    private readonly JavaScriptEngine _engine;

    public AcruxBuiltins(JavaScriptEngine engine) => _engine = engine;

    public int setTimeout(int cbId, int ms) => _engine.SetTimer(cbId, ms, false);
    public int setInterval(int cbId, int ms) => _engine.SetTimer(cbId, ms, true);
    public void clearTimeout(int id) => _engine.ClearTimer(id);
    public void clearInterval(int id) => _engine.ClearTimer(id);

    public string decodeURIComponent(string str) => Uri.UnescapeDataString(str ?? "null");
    public string encodeURIComponent(string str) => Uri.EscapeDataString(str ?? "null");
    public string escape(string str) => System.Net.WebUtility.UrlEncode(str ?? "null");
    public string unescape(string str) => System.Net.WebUtility.UrlDecode(str ?? "null");
    /// <summary>HTML §2.8.3: 'atob' reads base64 and answers a string whose code units are the bytes,
    /// so the range it lives in is Latin-1 rather than UTF-8. Padding a page may leave off, and the
    /// ASCII whitespace inside the run is dropped — anything else the base64 alphabet does not hold is
    /// refused with the sentence the reference writes (measured).</summary>
    public string atob(string str)
    {
        var raw = (str ?? "").Where(c => c is not (' ' or '\t' or '\n' or '\f' or '\r')).ToArray();
        var text = new string(raw);
        var pad = text.Length % 4 == 0 ? 0 : 4 - text.Length % 4;
        string decoded;
        try
        {
            decoded = System.Text.Encoding.Latin1.GetString(System.Convert.FromBase64String(text + new string('=', pad)));
        }
        catch
        {
            throw new Acrux.Core.Dom.DOMException("Failed to execute 'atob' on 'Window': The string to be "
                + "decoded is not correctly encoded.", "InvalidCharacterError");
        }
        return decoded;
    }

    /// <summary>HTML §2.8.3: 'btoa' writes base64 out of a string whose code units are bytes. The
    /// Latin-1 range is what the operation holds, and a code unit above it is refused — a code unit
    /// that is still inside it is taken as the byte it names (measured: 'ÿx' encodes).</summary>
    public string btoa(string str)
    {
        var text = str ?? "";
        foreach (var c in text)
        {
            if (c > 0xFF)
            {
                throw new Acrux.Core.Dom.DOMException("Failed to execute 'btoa' on 'Window': The string to "
                    + "be encoded contains characters outside of the Latin1 range.", "InvalidCharacterError");
            }
        }
        return System.Convert.ToBase64String(System.Text.Encoding.Latin1.GetBytes(text));
    }
    public Func<int>? GetInnerWidth { get; set; }
    public Func<int>? GetInnerHeight { get; set; }
    public Func<double>? GetDevicePixelRatio { get; set; }
    public Func<int>? GetScrollX { get; set; }
    public Func<int>? GetScrollY { get; set; }
    public Action<int, int>? OnScrollTo { get; set; }
    public Action<int, int>? OnScrollBy { get; set; }

    public int innerWidth() => GetInnerWidth?.Invoke() ?? 1024;
    public int innerHeight() => GetInnerHeight?.Invoke() ?? 768;
    public double devicePixelRatio() => GetDevicePixelRatio?.Invoke() ?? 1.0;

    /// <summary>Which preferred colour scheme the environment reports to
    /// 'prefers-color-scheme'. The shell has one; a page that never sets it stays 'light'.</summary>
    public Func<string>? GetColorScheme { get; set; }

    /// <summary>'window.matchMedia(query)': the query is evaluated by the same code that filters
    /// @media rules, which is the only way the two can be made to answer one question the same
    /// way. The viewport is the window's, the resolution the device pixel ratio, and the font
    /// units inside the query are the initial font's (CSS Media Queries 4 §6) — none of which a
    /// page can influence from a style sheet.</summary>
    public bool mediaQueryMatches(string query)
    {
        var env = MediaEnvironment();
        return Acrux.Core.Css.MediaQueryEvaluator.Evaluate(query ?? "", env.ViewportWidth, env.ViewportHeight,
                                                            env.ColorScheme, env);
    }

    /// <summary>The string a MediaQueryList's 'media' property reads as, and the same one a '@media'
    /// rule reads through 'conditionText' and 'media.mediaText'. A reference engine does not hand
    /// back what the page typed: it canonicalises the spelling of the grammar, resolves every math
    /// function that reduces to one absolute term and re-prints the numbers it keeps to the
    /// precision it uses (measured: 'matchMedia("(min-width: calc(100px * 4 + 45px)")' reads as
    /// '(min-width: calc(445px))' and '(min-width: 445.0px)' as '(min-width: 445px)'). See
    /// <see cref="Acrux.Core.Css.MediaFeatureValue.Canonicalize"/>.</summary>
    public string mediaQuerySerialize(string query)
        => Acrux.Core.Css.MediaFeatureValue.Canonicalize(query, MediaEnvironment());

    /// <summary>The viewport a query is answered against: the window's own size, its colour scheme
    /// and its device pixel ratio, with the font units measured from the initial font because a
    /// query is not attached to a box (CSS Media Queries 4 §6).</summary>
    private Acrux.Core.Css.MediaQueryEnvironment MediaEnvironment()
    {
        float w = innerWidth(), h = innerHeight();
        return new Acrux.Core.Css.MediaQueryEnvironment
        {
            ViewportWidth = w,
            ViewportHeight = h,
            ColorScheme = GetColorScheme?.Invoke() ?? "light",
            ResolutionDppx = devicePixelRatio(),
        };
    }
    public int scrollX() => GetScrollX?.Invoke() ?? 0;
    public int scrollY() => GetScrollY?.Invoke() ?? 0;
    public void scrollTo(int x, int y) { OnScrollTo?.Invoke(x, y); }
    public void scrollBy(int x, int y) { OnScrollBy?.Invoke(x, y); }
    public XMLHttpRequestHost createXMLHttpRequest() => new XMLHttpRequestHost(_engine);
    public URLHost createURL(string url, string? baseUrl) => new URLHost(url, baseUrl);
    public URLSearchParamsHost createURLSearchParams(string query) => new URLSearchParamsHost(query);

    /// <summary>'new CSSStyleSheet()' (CSSOM §5.2.2): a sheet the script owns. It applies to a
    /// document only once the page adopts it, and unlike a sheet an element owns it may be
    /// replaced wholesale through replaceSync/replace.</summary>
    public CssStyleSheetHost createCSSStyleSheet() => new CssStyleSheetHost(new Acrux.Core.Css.Rules.StyleSheetContents());

    // ── Web Crypto / DOMParser hosts ──────────────────────────

    /// <summary>High Resolution Time §5.1: the clock 'performance.now' counts on. It is the platform's
    /// monotonic tick rather than the wall clock, so a step in the system time cannot make a
    /// measurement negative or jump a page's own timing forward.</summary>
    public double monotonicNow()
        => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>'performance.memory' (the legacy heap read): the bytes the collector is holding, the
    /// heap it is holding them in and the ceiling the runtime would grow to. The engine is the only
    /// side that knows, so the three numbers come from the garbage collector rather than a constant.
    /// Order: total, used, limit.</summary>
    public double[] memoryInfo()
    {
        var info = GC.GetGCMemoryInfo();
        var used = (double)GC.GetTotalMemory(false);
        var total = info.HeapSizeBytes > 0
            ? (double)info.HeapSizeBytes : used;
        var limit = info.HighMemoryLoadThresholdBytes > 0
            ? (double)info.HighMemoryLoadThresholdBytes : total;
        return new[] { total, used, limit };
    }

    /// <summary>'crypto.randomUUID()' (Web Crypto §5.5): a version-4 identifier. The engine is
    /// the only place with a real random source, so the string is minted here.</summary>
    public string randomUUID() => Guid.NewGuid().ToString();

    /// <summary>'crypto.subtle.digest' (Web Crypto §15.14): the hash of the bytes the page handed in.
    /// The bytes arrive as hex and the digest leaves as hex, which keeps the boundary from having to
    /// marshal an ArrayBuffer and lets the platform's own crypto implementation do the work — the
    /// script side builds the promise and the refusals. A name the interface does not have answers
    /// null, and the caller has already checked the four digests it exposes.</summary>
    public string? digest(string algorithm, string hexData)
    {
        var name = (algorithm ?? "").Trim().ToUpperInvariant().Replace("-", "");
        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hexData ?? "");
        }
        catch
        {
            return null;
        }
        byte[] hash = name switch
        {
            "SHA1" => System.Security.Cryptography.SHA1.HashData(bytes),
            "SHA256" => System.Security.Cryptography.SHA256.HashData(bytes),
            "SHA384" => System.Security.Cryptography.SHA384.HashData(bytes),
            "SHA512" => System.Security.Cryptography.SHA512.HashData(bytes),
            _ => Array.Empty<byte>()
        };
        return hash.Length == 0 ? null : Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Custom Elements §1: the script-side registry tells the engine the name it put a
    /// definition under, and the first call switches the reaction hooks on. Until a page defines its
    /// first element the engine's tree operations never cross the boundary for a callback.</summary>
    public void ceDefine(string name) => CustomElements.NoteDefinition(name ?? "");

    /// <summary>'navigator.hardwareConcurrency' (Device Interface §3.1): the number of logical
    /// processors the engine can run work on, which only the host can answer.</summary>
    public int hardwareConcurrency() => System.Environment.ProcessorCount;

    /// <summary>'new DOMParser().parseFromString(markup, "text/html")' (DOM Parsing §3): a brand
    /// new Document built from the markup, handed to the page as a document host so it is queried
    /// and mutated like the live one.</summary>
    public object parseHTML(string? markup)
    {
        // An empty run still becomes a document with its html, head and body, the way the HTML parser
        // builds one from nothing (measured: 'parseFromString('', 'text/html')' has a body).
        var newDoc = Acrux.Core.Dom.Parser.HtmlDocumentParserIntegration.ParseHtml(markup ?? "");
        // Wrapped through the identity map rather than built here, so the document the page holds is the
        // one every node inside it names as its 'ownerDocument' (DOM §2.8, §3.2).
        return JsIntegrationService.WrapAny(newDoc) ?? new DocumentHost(newDoc);
    }

    /// <summary>DOM Parsing §3: the XML half of 'parseFromString'. The markup is read as an XML tree —
    /// elements with the namespace and prefix each name carries, attributes, text, CDATA, comments and
    /// the processing instructions between them — and the document is left with the content type the
    /// page asked for, which is what makes 'contentType', a CDATA section and a prefixed attribute all
    /// answer the way the interface says. Markup that is not well formed keeps the tree the reader
    /// reached and gains the 'parsererror' element the reference puts inside it, holding the sentence
    /// of the failure (measured).</summary>
    public object parseXML(string? markup, string? contentType)
    {
        var doc = new Acrux.Core.Dom.Document
        {
            Url = "about:blank",
            ContentType = string.IsNullOrEmpty(contentType) ? "text/xml" : contentType!,
        };
        var stack = new Stack<Acrux.Core.Dom.Element>();
        string? failure = null;
        int failureLine = 0, failureColumn = 0;
        try
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
            using var reader = System.Xml.XmlReader.Create(
                new System.IO.StringReader(markup ?? ""), settings);
            Acrux.Core.Dom.Element? root = null;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case System.Xml.XmlNodeType.Element:
                    {
                        var qualified = string.IsNullOrEmpty(reader.Prefix)
                            ? reader.LocalName : reader.Prefix + ":" + reader.LocalName;
                        var element = string.IsNullOrEmpty(reader.NamespaceURI)
                            ? doc.CreateElement(qualified)
                            : doc.CreateElementNS(reader.NamespaceURI, qualified);
                        for (var i = 0; i < reader.AttributeCount; i++)
                        {
                            reader.MoveToAttribute(i);
                            var name = string.IsNullOrEmpty(reader.Prefix)
                                ? reader.LocalName : reader.Prefix + ":" + reader.LocalName;
                            if (string.IsNullOrEmpty(reader.NamespaceURI))
                                element.SetAttribute(name, reader.Value ?? "");
                            else
                                element.SetAttributeNS(reader.NamespaceURI, name, reader.Value ?? "");
                        }
                        reader.MoveToElement();
                        if (root == null)
                        {
                            root = element;
                            doc.DocumentElement = element;
                            doc.AppendChild(element);
                        }
                        else stack.Peek().AppendChild(element);
                        if (!reader.IsEmptyElement) stack.Push(element);
                        break;
                    }
                    case System.Xml.XmlNodeType.EndElement:
                        if (stack.Count > 0) stack.Pop();
                        break;
                    case System.Xml.XmlNodeType.Text:
                    case System.Xml.XmlNodeType.CDATA:
                    case System.Xml.XmlNodeType.Whitespace:
                    case System.Xml.XmlNodeType.SignificantWhitespace:
                    {
                        if (stack.Count == 0) break;
                        Acrux.Core.Dom.Node text = reader.NodeType == System.Xml.XmlNodeType.CDATA
                            ? new Acrux.Core.Dom.CDataSection(reader.Value ?? "")
                            : new Acrux.Core.Dom.TextNode(reader.Value ?? "");
                        stack.Peek().AppendChild(text);
                        break;
                    }
                    case System.Xml.XmlNodeType.Comment:
                        if (stack.Count > 0) stack.Peek().AppendChild(new Acrux.Core.Dom.CommentNode(reader.Value ?? ""));
                        else doc.AppendChild(new Acrux.Core.Dom.CommentNode(reader.Value ?? ""));
                        break;
                    case System.Xml.XmlNodeType.ProcessingInstruction:
                    {
                        var pi = new Acrux.Core.Dom.ProcessingInstruction(reader.LocalName ?? "", reader.Value ?? "");
                        if (stack.Count > 0) stack.Peek().AppendChild(pi);
                        else doc.AppendChild(pi);
                        break;
                    }
                }
            }
        }
        catch (System.Xml.XmlException ex)
        {
            failure = ex.Message;
            failureLine = ex.LineNumber;
            failureColumn = ex.LinePosition;
        }

        if (failure != null)
        {
            var error = doc.CreateElementNS("http://www.w3.org/1999/xhtml", "parsererror");
            error.TextContent = "This page contains the following errors:"
                + $"error on line {failureLine} at column {failureColumn}: {failure}";
            if (doc.DocumentElement != null) doc.DocumentElement.AppendChild(error);
            else
            {
                doc.AppendChild(error);
                doc.DocumentElement = error;
            }
        }
        return JsIntegrationService.WrapAny(doc) ?? new DocumentHost(doc);
    }

    // ── JS Engine Management ──────────────────────────────────

    /// <summary>
    /// 实例回调，由 BrowserApp 主线程设置。
    /// </summary>
    public Func<string, string, string>? EngineAction { get; set; }

    /// <summary>
    /// 全局静态回调，由 BrowserApp 设置，供 TabProcess 等后台线程使用。
    /// </summary>
    public static Func<string, string, string>? GlobalEngineAction { get; set; }

    /// <summary>
    /// 获取所有 JS 引擎的状态（JSON 字符串）。
    /// 返回格式: [{"name":"Jint","type":"jint","active":true,"downloaded":true,"builtIn":true},...]
    /// </summary>
    public string engineGetStatus()
    {
        try
        {
            var effectiveType = JsEngineConfig.EffectiveEngineType;
            var activeName = effectiveType.ToString().ToLowerInvariant();

            var parts = new List<string>();
            foreach (var engName in new[] { "Jint", "V8", "Jurassic" })
            {
                var engType = JsEngineConfig.GetEngineTypeByName(engName);
                bool isBuiltIn = engType == JsEngineType.Jint;
                bool isDownloaded = isBuiltIn || JsEngineDownloader.IsEngineDownloaded(engType!.Value);
                bool isActive = engName.ToLowerInvariant() == activeName;
                // JSON requires lowercase true/false — convert bool to lowercase string
                string activeStr = isActive ? "true" : "false";
                string downloadedStr = isDownloaded ? "true" : "false";
                string builtInStr = isBuiltIn ? "true" : "false";
                string status = isBuiltIn ? "内置" : (isDownloaded ? "已就绪" : "未下载");
                parts.Add($"{{\"name\":\"{engName}\",\"type\":\"{engName.ToLowerInvariant()}\",\"active\":{activeStr},\"downloaded\":{downloadedStr},\"builtIn\":{builtInStr},\"status\":\"{status}\"}}");
            }

            return "[" + string.Join(",", parts) + "]";
        }
        catch (Exception ex)
        {
            return $"[error] {ex.Message}";
        }
    }

    /// <summary>
    /// 下载指定引擎。返回 JSON: {"success":true} 或 {"success":false,"error":"..."}
    /// </summary>
    public string engineDownload(string name)
    {
        var result = EngineAction?.Invoke("download", name) ?? GlobalEngineAction?.Invoke("download", name);
        return result ?? "[no handler]";
    }

    /// <summary>
    /// 从本地选择并安装指定引擎。返回 JSON: {"success":true} 或 {"success":false,"error":"..."}
    /// </summary>
    public string engineBrowse(string name)
    {
        var result = EngineAction?.Invoke("browse", name) ?? GlobalEngineAction?.Invoke("browse", name);
        return result ?? "[no handler]";
    }

    /// <summary>
    /// 应用（切换）到指定引擎。返回 JSON: {"success":true} 或 {"success":false,"error":"..."}
    /// </summary>
    public string engineApply(string name)
    {
        var result = EngineAction?.Invoke("apply", name) ?? GlobalEngineAction?.Invoke("apply", name);
        return result ?? "[no handler]";
    }

    private readonly Dictionary<int, CancellationTokenSource> _fetchTokens = new();
    private readonly object _fetchTokenLock = new();

    /// <summary>DOM §4.5.6: the JS MutationObserver turns text-change recording on while one of its
    /// registrations asks for characterData and off when none does, so a page that never observes
    /// pays nothing for every keystroke of a typewriter demo.</summary>
    public void setCharacterDataTracking(bool on)
        => Acrux.Core.Dom.DomMutationTracker.TrackCharacterData = on;

    /// <summary>The text edits queued since the last call, as [wrapped node, old value] pairs —
    /// the observer diff cannot reconstruct an old value a node has already lost.</summary>
    public object[][] takeCharacterDataChanges()
    {
        var changes = Acrux.Core.Dom.DomMutationTracker.TakeCharacterDataChanges();
        var outList = new List<object[]>(changes.Count);
        foreach (var change in changes)
        {
            object?[] pair = [JsIntegrationService.WrapAny(change.Node), change.OldValue];
            outList.Add(pair);
        }
        return outList.ToArray();
    }

    /// <summary>Fetch §5: the signal the page passed in aborts the request. The task is cancelled
    /// at the socket, not just at the promise — an abandoned download still costs bandwidth.</summary>
    public void abortFetch(int id)
    {
        CancellationTokenSource? source;
        lock (_fetchTokenLock) _fetchTokens.TryGetValue(id, out source);
        try { source?.Cancel(); }
        catch { }
    }

    public int _fetch(string url, string optionsJson, int resolveId, int rejectId)
    {
        var id = _engine.AddFetch(resolveId, rejectId);
        var cancellation = new CancellationTokenSource();
        lock (_fetchTokenLock) _fetchTokens[id] = cancellation;

        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

                var options = string.IsNullOrEmpty(optionsJson) ? null :
                    JsonSerializer.Deserialize(optionsJson, AcruxJsonContext.Default.FetchOptions);

                var method = options?.Method ?? "GET";
                var req = new HttpRequestMessage(new HttpMethod(method), url);

                string? contentType = null;
                if (options?.Headers != null)
                {
                    foreach (var h in options.Headers)
                    {
                        if (string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                            contentType = h.Value?.ToString();
                        else
                            req.Headers.TryAddWithoutValidation(h.Key, h.Value?.ToString() ?? "");
                    }
                }

                if (options?.Body != null && (method == "POST" || method == "PUT" || method == "PATCH"))
                {
                    req.Content = new StringContent(options.Body, Encoding.UTF8);
                    if (contentType != null)
                        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                }

                var response = await http.SendAsync(req, cancellation.Token);
                var text = await response.Content.ReadAsStringAsync(cancellation.Token);

                var headers = new Dictionary<string, string>();
                foreach (var h in response.Headers)
                    headers[h.Key] = string.Join(", ", h.Value);

                _engine.CompleteFetch(id, new FetchResult
                {
                    Success = true,
                    Data = text,
                    Status = (int)response.StatusCode,
                    StatusText = response.ReasonPhrase ?? "",
                    Headers = headers
                });
            }
            catch (Exception ex)
            {
                _engine.CompleteFetch(id, new FetchResult { Success = false, Error = ex.Message });
            }
            finally
            {
                lock (_fetchTokenLock) _fetchTokens.Remove(id);
                cancellation.Dispose();
            }
        });

        return id;
    }

    public string decodeURI(string str) => Uri.UnescapeDataString(str);
    public string encodeURI(string str) => Uri.EscapeDataString(str);
    /// <summary>'parseInt' for an engine that brings no built-ins of its own: the digits are read in the
    /// radix the page gave — sixteen for an '0x' spelling and ten when no radix came — and the first
    /// character that is not a digit in that radix ends the number rather than refusing the whole of it.
    /// Text with no digits at all answers NaN, which is what the ECMAScript operation answers, not zero.</summary>
    public double parseInt(string? s, object? radix = null)
    {
        var text = (s ?? "").TrimStart();
        int sign = 1;
        if (text.Length > 0 && (text[0] == '+' || text[0] == '-'))
        {
            sign = text[0] == '-' ? -1 : 1;
            text = text.Substring(1);
        }
        var given = radix switch
        {
            int ri => ri,
            long rl => (int)rl,
            double rd => (int)rd,
            string rs when double.TryParse(rs, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var rv) => (int)rv,
            _ => 0
        };
        int numberBase = given == 0
            ? (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10)
            : given;
        if (numberBase < 2 || numberBase > 36) return double.NaN;
        if (numberBase == 16 && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(2);
        double value = 0;
        int taken = 0;
        foreach (var c in text)
        {
            int digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'z' => c - 'a' + 10,
                >= 'A' and <= 'Z' => c - 'A' + 10,
                _ => -1
            };
            if (digit < 0 || digit >= numberBase) break;
            value = value * numberBase + digit;
            taken++;
        }
        return taken == 0 ? double.NaN : sign * value;
    }

    /// <summary>'parseFloat' for the same engine: the number at the start of the text is read and the
    /// rest of it is left alone, which is what '12px' answers twelve for rather than nothing.</summary>
    public double parseFloat(string? s)
    {
        const System.Globalization.NumberStyles Style = System.Globalization.NumberStyles.Float;
        var text = (s ?? "").TrimStart();
        for (var end = text.Length; end > 0; end--)
        {
            if (double.TryParse(text[..end], Style,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return double.NaN;
    }

    public bool isNaN(object? v)
    {
        if (v == null) return true;
        if (v is double dv) return double.IsNaN(dv);
        if (v is float fv) return double.IsNaN(fv);
        if (double.TryParse(v.ToString(), out var parsed)) return double.IsNaN(parsed);
        return true;
    }
    public bool isFinite(object? v)
    {
        if (v == null) return false;
        if (v is double dv) return !double.IsInfinity(dv) && !double.IsNaN(dv);
        if (v is float fv) return !float.IsInfinity(fv) && !float.IsNaN(fv);
        if (double.TryParse(v.ToString(), out var parsed))
            return !double.IsInfinity(parsed) && !double.IsNaN(parsed);
        return false;
    }
}

public class ConsoleHost
{
    private readonly Dictionary<string, int> _counters = new();
    private readonly Stack<(string label, long time)> _timers = new();
    private int _groupLevel = 0;
    private JsDevToolsConsole? _devToolsConsole;

    public JsDevToolsConsole? DevToolsConsole
    {
        get => _devToolsConsole;
        set => _devToolsConsole = value;
    }

    public void log(params object?[] args)
    {
        _devToolsConsole?.Log("log", args);
        WriteLine("[JS]", args);
    }
    public void error(params object?[] args)
    {
        _devToolsConsole?.Log("error", args);
        WriteLine("[JS Error]", args);
    }
    public void warn(params object?[] args)
    {
        _devToolsConsole?.Log("warn", args);
        WriteLine("[JS Warning]", args);
    }
    public void info(params object?[] args)
    {
        _devToolsConsole?.Log("info", args);
        WriteLine("[JS Info]", args);
    }
    public void debug(params object?[] args)
    {
        _devToolsConsole?.Log("debug", args);
        WriteLine("[JS Debug]", args);
    }
    public void trace(params object?[] args)
    {
        _devToolsConsole?.Log("trace", args);
        WriteLine("[JS Trace]", args);
        Console.WriteLine(new System.Diagnostics.StackTrace(true).ToString());
    }
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "dir is a debug console helper; object is user-provided")]
    public void dir(object? obj)
    {
        if (obj == null)
        {
            var msg = "null";
            _devToolsConsole?.Log("log", msg);
            Console.WriteLine(msg);
            return;
        }
        if (obj is System.Collections.IDictionary dict)
        {
            foreach (var key in dict.Keys)
                Console.WriteLine($"  {key}: {dict[key]}");
        }
        else
        {
            foreach (var prop in obj.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                try { Console.WriteLine($"  {prop.Name}: {prop.GetValue(obj)}"); }
                catch { }
            }
        }
    }
    public void table(params object?[] args)
    {
        if (args.Length == 0) return;
        if (args[0] is System.Collections.IEnumerable enumerable && args[0] is not string)
        {
            Console.WriteLine("[Table output]");
            foreach (var item in enumerable)
                Console.WriteLine($"  {item}");
        }
        else
        {
            WriteLine("[Table]", args);
        }
    }
    public void group(params object?[] args)
    {
        _groupLevel++;
        var prefix = new string(' ', _groupLevel * 2);
        var msg = $"{prefix}Group: {string.Join(" ", args.Select(a => a?.ToString() ?? "null"))}";
        Console.WriteLine(msg);
    }
    public void groupEnd()
    {
        if (_groupLevel > 0) _groupLevel--;
    }
    public void count(string? label = null)
    {
        var key = label ?? "default";
        if (!_counters.ContainsKey(key))
            _counters[key] = 0;
        _counters[key]++;
        var msg = $"{key}: {_counters[key]}";
        Console.WriteLine(msg);
    }
    public void countReset(string? label = null)
    {
        var key = label ?? "default";
        _counters[key] = 0;
        Console.WriteLine($"{key}: 0");
    }
    public void time(string? label = null)
    {
        var key = label ?? "default";
        _timers.Push((key, Environment.TickCount64));
        Console.WriteLine($"Timer '{key}' started");
    }
    public void timeLog(string? label = null, params object?[] args)
    {
        var key = label ?? "default";
        long startTime = 0;
        bool found = false;
        foreach (var (l, t) in _timers)
        {
            if (l == key) { startTime = t; found = true; break; }
        }
        if (found)
        {
            var elapsed = Environment.TickCount64 - startTime;
            var msg = $"{key}: {elapsed}ms";
            if (args.Length > 0) msg += " " + string.Join(" ", args.Select(a => a?.ToString() ?? "undefined"));
            Console.WriteLine(msg);
        }
        else
        {
            Console.WriteLine($"Timer '{key}' not found");
        }
    }
    public void timeEnd(string? label = null)
    {
        var key = label ?? "default";
        while (_timers.Count > 0)
        {
            var (timerLabel, startTime) = _timers.Pop();
            if (timerLabel == key)
            {
                var elapsed = Environment.TickCount64 - startTime;
                Console.WriteLine($"Timer '{key}': {elapsed}ms");
                return;
            }
        }
        Console.WriteLine($"Timer '{key}' not found");
    }
    public void timeStamp(string? label = null)
    {
        Console.WriteLine($"Timestamp: {label ?? "unnamed"} at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}ms");
    }
    public void assert(bool condition, params object?[] args)
    {
        if (!condition)
        {
            _devToolsConsole?.Log("error", args.Prepend("Assertion failed: ").ToArray());
            WriteLine("[JS Assertion Failed]", args);
        }
    }
    public void Reset()
    {
        _counters.Clear();
        _timers.Clear();
        _groupLevel = 0;
        _devToolsConsole?.Clear();
    }

    public void clear()
    {
        _devToolsConsole?.Clear();
        Console.Clear();
    }

    private void WriteLine(string prefix, object?[] args)
    {
        var indent = new string(' ', _groupLevel * 2);
        Console.WriteLine(indent + prefix + " " + string.Join(" ", args.Select(a => a?.ToString() ?? "undefined")));
    }
}

/// <summary>The <c>CSS</c> global (CSSOM §6.1.1 and CSS Conditional 5 §5.3): <c>supports()</c>
/// asked one way takes a whole condition as the page would write it inside an <c>@supports</c>
/// prelude, asked the other takes a property name and a value separately — and both spellings reach
/// the same predicate the sheet parser reads a condition with, so a script cannot be told one thing
/// and a rule another. The two-argument form keeps its own rule for a custom property, which is the
/// one place the IDL does not read a grammar at all: any value at all, including none, is supported
/// for a name that starts with two dashes (measured: <c>supports('--x', '1')</c> and
/// <c>supports('--x', '')</c> are both true while <c>supports('--x')</c> is not). The rest of the
/// interface — <c>escape</c>, <c>registerProperty</c>, <c>paintWorklet</c> — is not here, because a
/// name that answers nothing is worse than no name.</summary>
public class CssObjectHost
{
    public bool supports(string condition) =>
        Acrux.Core.Css.Tokenizer.CssParserImpl.SupportsConditionIsWellFormed(condition);

    public bool supports(string property, string value) =>
        // The two-argument form is asked about a VALUE, and an important flag is not part of one:
        // 'supports("color", "red !important")' is false while the condition form
        // 'supports("(color: red !important)")' is true (measured, both).
        !Acrux.Core.Css.CssValueGrammar.HasImportantFlag(value ?? "")
        && ((property ?? "").StartsWith("--", StringComparison.Ordinal)
            || Acrux.Core.Css.Tokenizer.CssParserImpl.SupportsConditionIsWellFormed(
                $"{property}: {value}"));

    /// <summary>CSSOM §4.2 — escape a string so it is safe to use as a CSS identifier. Control
    /// codes and a leading digit become hex escapes; every other character that is not an
    /// identifier code point (space, punctuation) is escaped with a single backslash; a bare
    /// NULL becomes the replacement character. This is what 'CSS.escape('foo[bar]')' answers.</summary>
    public string escape(string? input)
    {
        var value = input ?? "";
        if (value.Length == 0) return "￼";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            int cp = value[i];
            if (cp == 0x0000) { sb.Append('￼'); continue; }
            if ((cp >= 0x0001 && cp <= 0x001F) || cp == 0x007F) { sb.Append(HexEscape(cp)); continue; }
            if (i == 0 && cp >= 0x0030 && cp <= 0x0039) { sb.Append(HexEscape(cp)); continue; }
            // '-digit' as the first two code points, or a lone '-', must not read as a number, so
            // the leading dash of those forms is escaped while a dash anywhere else is kept.
            if (cp == 0x002D && i == 0 && value.Length >= 2 && value[1] >= '0' && value[1] <= '9')
            { sb.Append(HexEscape(cp)); continue; }
            if (cp >= 0x0080
                || (cp >= 0x0030 && cp <= 0x0039)
                || (cp >= 0x0041 && cp <= 0x005A)
                || (cp >= 0x0061 && cp <= 0x007A)
                || cp == 0x002D || cp == 0x005F)
            { sb.Append(value[i]); continue; }
            sb.Append('\\').Append(value[i]);
        }
        return sb.ToString();
    }

    private static string HexEscape(int codePoint)
    {
        var hex = codePoint.ToString("X");
        var space = hex.Length < 6 ? " " : "";
        return "\\" + hex + space;
    }
}

public class NavigatorHost : IDomPrototypeHost
{
    // The interface a page reads the navigator as (HTML §7.3), so an accessor the interface
    // declares — a clipboard, a service worker registration — is reachable on the host object.
    string[] IDomPrototypeHost.__domTypeChain => new[] { "EventTarget", "Navigator" };

    public string userAgent => "Acrux/1.0 (" + OperatingSystemComment() + ") AppleWebKit/537.36";
    public string appCodeName => "Mozilla";
    public string appName => "Acrux";
    public string appVersion => "1.0";

    /// <summary>HTML §7.3.4: 'platform' names the operating system the engine is running on, so one
    /// build answers 'Linux x86_64', 'Win32' or 'MacIntel' according to the host it is on rather than a
    /// string baked into the source.</summary>
    public string platform => NavigatorPlatform();

    /// <summary>HTML §7.3.4: 'vendor' is the string the browser ships with and the empty value is what
    /// an engine that is not one of the three that fill it reports (measured: the reference names its
    /// own maker here, the interface default is the empty string).</summary>
    public string vendor => "";

    /// <summary>HTML §7.3.4: the interface's own default, fixed by the specification rather than by
    /// the build date.</summary>
    public string productSub => "20030107";

    /// <summary>Do Not Track §3: the preference the user has not set reads back null, and this engine
    /// does not carry a setting for it.</summary>
    public string? doNotTrack => null;

    /// <summary>Pointer Events §5: the number of simultaneous touch contacts the device holds. A
    /// pointer the engine cannot read is not a touch, so the answer is the interface's zero.</summary>
    public ulong maxTouchPoints => 0;

    private static string NavigatorPlatform()
    {
        if (OperatingSystem.IsWindows()) return "Win32";
        if (OperatingSystem.IsMacOS())
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                System.Runtime.InteropServices.Architecture.Arm64 ? "MacArm" : "MacIntel";
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64";
        return "Linux " + arch;
    }

    private static string OperatingSystemComment()
    {
        if (OperatingSystem.IsWindows()) return "Windows NT 10.0; Win64; x64";
        if (OperatingSystem.IsMacOS()) return "Macintosh; Intel Mac OS X 10_15_7";
        return "X11; Linux " + (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64");
    }

    public string product => "Gecko";
    public string language => "zh-CN";
    public string[] languages => new[] { "zh-CN", "en" };
    public bool cookieEnabled => true;
    public bool onLine => true;
    public bool javaEnabled() => false;

    /// <summary>Beacon (Beacon §2): a fire-and-forget POST the page never waits on. The body — a
    /// string, or the text of a Blob/File — is queued as an async request and the call answers
    /// whether the queue took it, which is all the interface lets a page observe.</summary>
    public bool sendBeacon(string url, object? data = null)
    {
        if (string.IsNullOrEmpty(url)) return false;
        try
        {
            string body;
            if (data == null) body = "";
            else
            {
                var t = data.GetType();
                var textGetter = t.GetProperty("__text");
                body = textGetter != null ? Convert.ToString(textGetter.GetValue(data)) ?? "" : data.ToString() ?? "";
            }
            JavaScriptEngine.Current?.QueueBeacon(url, body);
            return true;
        }
        catch { return false; }
    }
}

public class LocationHost : IDomPrototypeHost
{
    string[] IDomPrototypeHost.__domTypeChain => new[] { "Location" };

    public Action<string>? OnNavigate { get; set; }
    public Action? OnReload { get; set; }

    // 'href' is the source of truth (HTML: it navigates when assigned); every other member is the
    // same URL broken into its components, and every setter edits 'href' at that component. The
    // older hand-set fields let 'location.hash = "#x"' leave 'location.href' unchanged, so a page
    // that read the URL back after setting a part saw a value it had never asked for.
    private string _href = "acrux://local/";

    public string href
    {
        get => _href;
        set { _href = string.IsNullOrEmpty(value) ? "about:blank" : value; OnNavigate?.Invoke(_href); }
    }

    private URLHost? Parts()
    {
        try { return new URLHost(_href); } catch { return null; }
    }

    public string protocol { get { var p = Parts()?.protocol ?? ""; return p; } set { _href = Rebuild(value, _href, scheme: true); } }
    public string hostname { get { var u = Parts(); return u?.hostname ?? ""; } set { var u = Parts(); _href = Rebuild(u == null ? _href : u.hostname, value, hostPart: true); } }
    public string port { get { var u = Parts(); return u?.port ?? ""; } set { } }
    public string host { get { var u = Parts(); return u?.host ?? ""; } set { hostname = value; } }
    public string pathname { get { var u = Parts(); return u?.pathname ?? "/"; } set { _href = RebuildTail(value, Search, Hash); } }
    public string search { get { var u = Parts(); return u?.search ?? ""; } set { _href = RebuildTail(Pathname, value, Hash); } }
    public string hash { get { var u = Parts(); return u?.hash ?? ""; } set { _href = RebuildTail(Pathname, Search, value); } }
    public string origin { get { var u = Parts(); return u?.origin ?? ""; } }

    /// <summary>HTML §5.2.5: the document's own URL is the address the browsing context is at, so a
    /// page reads the same URL through 'location' that the document carries. Setting it here does not
    /// ask for a navigation — the shell navigated to get here.</summary>
    internal void SetDocumentUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        _href = url!;
    }

    /// <summary>HTML §5.2.5: 'pushState' and 'replaceState' rewrite the address of the session history
    /// entry the page is on, and the new address is read relative to the one already there. No
    /// navigation happens (that is what separates the two from 'location.assign'), and the URL the
    /// entry ends up with is the absolute one the page meant.</summary>
    internal void SetStateUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        _href = new URLHost(url!, _href).href;
    }


    private string Hash { get { var u = Parts(); return u?.hash ?? ""; } }
    private string Search { get { var u = Parts(); return u?.search ?? ""; } }
    private string Pathname { get { var u = Parts(); return u?.pathname ?? "/"; } }

    // Compose scheme://host + path + ?search + #hash out of the pieces a caller has updated.
    private string Rebuild(string current, string replacement, bool scheme = false, bool hostPart = false)
    {
        var u = Parts();
        string proto = u?.protocol ?? "acrux:";
        string hostv = u?.host ?? "";
        string path = u?.pathname ?? "/";
        string search = u?.search ?? "";
        string hash = u?.hash ?? "";
        if (scheme) proto = replacement.EndsWith(":") ? replacement : replacement + ":";
        if (hostPart) hostv = replacement;
        return proto + "//" + hostv + path + search + hash;
    }

    private string RebuildTail(string path, string search = "", string hash = "")
    {
        var u = Parts();
        string proto = u?.protocol ?? "acrux:";
        string hostv = u?.host ?? "";
        if (!path.StartsWith("/")) path = "/" + path;
        if (!string.IsNullOrEmpty(search) && !search.StartsWith("?")) search = "?" + search;
        if (!string.IsNullOrEmpty(hash) && !hash.StartsWith("#")) hash = "#" + hash;
        return proto + "//" + hostv + path + search + hash;
    }

    public void assign(string url)
    {
        href = url;
    }
    public void replace(string url)
    {
        href = url;
    }
    public void reload()
    {
        OnReload?.Invoke();
    }
}

public class HistoryHost : IDomPrototypeHost
{
    string[] IDomPrototypeHost.__domTypeChain => new[] { "History" };

    private readonly List<Entry> _stack = new();
    private int _currentIndex = -1;
    private readonly LocationHost _location;
    private string _scrollRestoration = "auto";

    /// <summary>One entry of the session history: the state the page handed to 'pushState' and the
    /// address it was written with, which a traversal has to put back (HTML §5.6).</summary>
    private sealed record Entry(object? State, string? Url);

    public HistoryHost(LocationHost location) => _location = location;

    // HTML §5.2.5: the entry the page arrived on is the first one in the list, so the length counts
    // it even before a 'pushState' has added anything.
    public int length => _stack.Count + 1;
    public object? state => _currentIndex >= 0 && _currentIndex < _stack.Count
        ? _stack[_currentIndex].State : null;

    /// <summary>HTML §7.4.7: the two spellings the interface accepts. A value the enumeration does not
    /// name is dropped, which is what the reference does when a page writes anything else (measured:
    /// writing 'nonsense' leaves the value it already held).</summary>
    public string scrollRestoration
    {
        get => _scrollRestoration;
        set { if (value is "auto" or "manual") _scrollRestoration = value; }
    }

    public void back() => Traverse(-1);
    public void forward() => Traverse(1);

    /// <summary>HTML §5.6: the entry the history moves to, and the 'popstate' event the window hears
    /// when it is a different one. The address moves with it, because the entry remembers the one that
    /// was written when it was pushed.</summary>
    public void go(int delta)
    {
        if (delta == 0) { _location.OnReload?.Invoke(); return; }
        Traverse(delta);
    }

    private void Traverse(int delta)
    {
        var next = _currentIndex + delta;
        if (next < 0 || next >= _stack.Count) return;
        _currentIndex = next;
        var entry = _stack[next];
        if (entry.Url != null) _location.SetStateUrl(entry.Url);
        JavaScriptEngine.Current?.FirePopState();
    }

    public void pushState(object? state, string title, string? url)
    {
        while (_stack.Count > _currentIndex + 1) _stack.RemoveAt(_stack.Count - 1);
        _stack.Add(new Entry(state, url));
        _currentIndex = _stack.Count - 1;
        _location.SetStateUrl(url);
    }

    public void replaceState(object? state, string title, string? url)
    {
        if (_currentIndex >= 0 && _currentIndex < _stack.Count)
            _stack[_currentIndex] = new Entry(state, url ?? _stack[_currentIndex].Url);
        else
        {
            _stack.Add(new Entry(state, url));
            _currentIndex = _stack.Count - 1;
        }
        _location.SetStateUrl(url);
    }
}

public class ScreenHost : IDomPrototypeHost
{
    string[] IDomPrototypeHost.__domTypeChain => new[] { "Screen" };

    // The device media features read the same numbers this object hands to a page, so
    // '(device-width: screen.width + "px")' cannot be false (CSS Media Queries 4 §7.4).
    public int width => Acrux.Core.Display.ScreenMetrics.Width;
    public int height => Acrux.Core.Display.ScreenMetrics.Height;
    public int availWidth => Acrux.Core.Display.ScreenMetrics.AvailableWidth;
    public int availHeight => Acrux.Core.Display.ScreenMetrics.AvailableHeight;
    public int availTop => 0;
    public int availLeft => 0;
    public int colorDepth => Acrux.Core.Display.ScreenMetrics.ColorDepth;
    public int pixelDepth => Acrux.Core.Display.ScreenMetrics.ColorDepth;
    public int top => Acrux.Core.Display.ScreenMetrics.Top;
    public int left => Acrux.Core.Display.ScreenMetrics.Left;
    public object? orientation => new ScreenOrientationHost();
}

public class ScreenOrientationHost
{
    public string type => "landscape-primary";
    public string angle => "0";
    public void lock_(string orientation) { }
    public void unlock() { }
}

public class StorageHost : System.Dynamic.DynamicObject
{
    private readonly Dictionary<string, string> _data = new();
    private readonly List<string> _order = new();
    private readonly string _name;

    public StorageHost(string name = "storage")
    {
        _name = name;
    }

    public string? getItem(string key) => _data.GetValueOrDefault(key);

    /// <summary>Web Storage §5.2: an item that is already there keeps its place and a new one is added
    /// at the end, which is the order 'key(index)' and a page that enumerates the object read.</summary>
    public void setItem(string key, string value)
    {
        if (!_data.ContainsKey(key)) _order.Add(key);
        _data[key] = value;
    }

    public void removeItem(string key)
    {
        if (_data.Remove(key)) _order.Remove(key);
    }

    public void clear()
    {
        _data.Clear();
        _order.Clear();
    }

    public string? key(int index) => index >= 0 && index < _order.Count ? _order[index] : null;
    public int length => _data.Count;

    // Web Storage is defined with a named getter/setter, so 'localStorage.foo' reads and writes the
    // item called 'foo' (Web Storage §5). The named methods always answer first; anything else is an
    // item name. Setting a key that collides with a method name stores nothing, matching a browser.
    public override bool TryGetMember(System.Dynamic.GetMemberBinder binder, out object? result)
    {
        result = _data.TryGetValue(binder.Name, out var v) ? v : null;
        return true;
    }

    public override bool TrySetMember(System.Dynamic.SetMemberBinder binder, object? value)
    {
        if (IsMethodName(binder.Name)) return false;
        if (!_data.ContainsKey(binder.Name)) _order.Add(binder.Name);
        _data[binder.Name] = value?.ToString() ?? "undefined";
        return true;
    }

    public override bool TryDeleteMember(System.Dynamic.DeleteMemberBinder binder)
    {
        if (_data.Remove(binder.Name)) _order.Remove(binder.Name);
        return true;
    }

    private static bool IsMethodName(string name) => name switch
    {
        "getItem" or "setItem" or "removeItem" or "clear" or "key" or "length" => true,
        _ => false,
    };
}