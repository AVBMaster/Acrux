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
        _adapter.SetGlobal("localStorage", new StorageHost("localStorage"));
        _adapter.SetGlobal("sessionStorage", new StorageHost("sessionStorage"));

        _adapter.SetGlobal("document", new DocumentHost(new Acrux.Core.Dom.Document()));

        _adapter.Execute(GetSetupScript());
    }

    public void LoadDocument(DomDocument document)
    {
        _currentDocument = document;
        _documentHost = new DocumentHost(document);
        _documentHost.Engine = this;

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
        _adapter.SetGlobal("localStorage", new StorageHost("localStorage"));
        _adapter.SetGlobal("sessionStorage", new StorageHost("sessionStorage"));
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
        Current = this;
        try
        {
            _integrationService?.ExecuteScript(code, sourceUrl, type, lineOffset);
            MarkDirty();
        }
        finally
        {
            Current = null;
        }
    }

    public object? Evaluate(string expression)
    {
        if (_adapter == null) return null;
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
            Current = null;
        }
    }

    public object? CallJsFunction(string functionName, params object?[] args)
    {
        if (_adapter == null) return null;
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
            Current = null;
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

        Current = this;
        try { ProcessCompletedFetches(); }
        finally { Current = null; }
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
            Current = null;
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

            function setTimeout(fn, ms) {
                var id = __g_store(fn);
                return __acrux.setTimeout(id, ms || 0);
            }
            function setInterval(fn, ms) {
                var id = __g_store(fn);
                return __acrux.setInterval(id, ms || 0);
            }
            function clearTimeout(id) { __acrux.clearTimeout(id); }
            function clearInterval(id) { __acrux.clearInterval(id); }
            function requestAnimationFrame(fn) { return setTimeout(fn, 16); }
            function cancelAnimationFrame(id) { return clearTimeout(id); }
            function alert(msg) { __win.alert(msg); }
            function confirm(msg) { return __win.confirm(msg); }
            function prompt(msg, def) { return __win.prompt(msg, def); }
            function decodeURI(str) { return __acrux.decodeURI(str); }
            function decodeURIComponent(str) { return __acrux.decodeURIComponent(str); }
            function encodeURI(str) { return __acrux.encodeURI(str); }
            function encodeURIComponent(str) { return __acrux.encodeURIComponent(str); }
            function parseInt(s, r) { return __acrux.parseInt(s, r); }
            function parseFloat(s) { return __acrux.parseFloat(s); }
            function isNaN(v) { return __acrux.isNaN(v); }
            function isFinite(v) { return __acrux.isFinite(v); }
            function escape(str) { return __acrux.escape(str); }
            function unescape(str) { return __acrux.unescape(str); }
            function atob(str) { return __acrux.atob(str); }
            function btoa(str) { return __acrux.btoa(str); }

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

            // CSSOM View §5.1 — DOMRect. Four numbers in, the rectangle and its derived edges out;
            // a negative width/height flips the origin the way the geometry model says it must.
            function DOMRect(x, y, w, h) {
                x = x === undefined ? 0 : Number(x);
                y = y === undefined ? 0 : Number(y);
                w = w === undefined ? 0 : Number(w);
                h = h === undefined ? 0 : Number(h);
                this.x = this.left = Math.min(x, x + w);
                this.y = this.top = Math.min(y, y + h);
                this.width = Math.abs(w);
                this.height = Math.abs(h);
                this.right = this.left + this.width;
                this.bottom = this.top + this.height;
                this.toJSON = function () {
                    return { x: this.x, y: this.y, width: this.width, height: this.height,
                        top: this.top, right: this.right, bottom: this.bottom, left: this.left };
                };
            }
            DOMRect.fromRect = function (o) { return new DOMRect(o.x, o.y, o.width, o.height); };

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

            function FormData(init) {
                var self = this;
                var pairs = [];
                if (init && typeof init.forEach === 'function' && typeof init.append !== 'function')
                    init.forEach(function (v, k) { pairs.push([String(k), v]); });
                else if (Array.isArray(init)) init.forEach(function (p) { pairs.push([String(p[0]), p[1]]); });
                function find(k) { var o = []; for (var i = 0; i < pairs.length; i++) if (pairs[i][0] === k) o.push(pairs[i][1]); return o; }
                this.append = function (k, v) { pairs.push([String(k), v]); };
                this.set = function (k, v) {
                    k = String(k); var done = false;
                    for (var i = 0; i < pairs.length; i++) if (pairs[i][0] === k) { if (!done) { pairs[i][1] = v; done = true; } else { pairs.splice(i, 1); i--; } }
                    if (!done) pairs.push([k, v]);
                };
                this.get = function (k) { var f = find(String(k)); return f.length ? f[0] : null; };
                this.getAll = function (k) { return find(String(k)); };
                this.has = function (k) { return find(String(k)).length > 0; };
                this.delete = function (k) { k = String(k); for (var i = pairs.length - 1; i >= 0; i--) if (pairs[i][0] === k) pairs.splice(i, 1); };
                this.forEach = function (cb, ts) { for (var i = 0; i < pairs.length; i++) cb.call(ts || null, pairs[i][1], pairs[i][0], self); };
                this.keys = function () { return pairs.map(function (p) { return p[0]; }); };
                this.values = function () { return pairs.map(function (p) { return p[1]; }); };
                this.entries = function () { return pairs.map(function (p) { return [p[0], p[1]]; }); };
                this[Symbol.iterator] = function () { return pairs.map(function (p) { return [p[0], p[1]]; })[Symbol.iterator](); };
                Object.defineProperty(this, 'length', { get: function () { return pairs.length; } });
            }

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

            function TextDecoder(label, options) { this.encoding = String(label || 'utf-8').toLowerCase(); this.fatal = false; }
            TextDecoder.prototype.decode = function (input) {
                if (input === undefined || input === null) return '';
                var bytes = (input instanceof Uint8Array) ? input
                    : (input.buffer ? new Uint8Array(input.buffer, input.byteOffset || 0, input.byteLength) : input);
                var res = '', i = 0, len = bytes.length;
                while (i < len) {
                    var b0 = bytes[i], cp;
                    if (b0 < 0x80) { cp = b0; i += 1; }
                    else if ((b0 & 0xE0) === 0xC0) { cp = ((b0 & 0x1F) << 6) | (bytes[i + 1] & 0x3F); i += 2; }
                    else if ((b0 & 0xF0) === 0xE0) { cp = ((b0 & 0x0F) << 12) | ((bytes[i + 1] & 0x3F) << 6) | (bytes[i + 2] & 0x3F); i += 3; }
                    else if ((b0 & 0xF8) === 0xF0) { cp = ((b0 & 0x07) << 18) | ((bytes[i + 1] & 0x3F) << 12) | ((bytes[i + 2] & 0x3F) << 6) | (bytes[i + 3] & 0x3F); i += 4; }
                    else { cp = b0; i += 1; }
                    if (cp <= 0xFFFF) res += String.fromCharCode(cp);
                    else { cp -= 0x10000; res += String.fromCharCode(0xD800 + (cp >> 10), 0xDC00 + (cp & 0x3FF)); }
                }
                return res;
            };

            // Web Crypto — the parts that need no secure transport surface. randomUUID is minted by
            // the host (the only real entropy), getRandomValues fills a typed array in place and
            // returns the same array the page handed in.
            var crypto = {
                randomUUID: function () { return __acrux.randomUUID(); },
                getRandomValues: function (array) {
                    if (!array) return array;
                    for (var i = 0; i < array.length; i++)
                        array[i] = Math.floor(Math.random() * 256);
                    return array;
                }
            };

            // DOMParser builds a fresh Document out of markup (the host parses it through the same
            // HTML pipeline the live page went through), so the result answers getElementById and
            // querySelector just like 'document'.
            function DOMParser() {}
            DOMParser.prototype.parseFromString = function (source, contentType) {
                return __acrux.parseHTML(String(source === undefined ? '' : source));
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

            // Blob holds concatenated string parts; 'size' is the UTF-8 byte length and 'text'/'
            // arrayBuffer' are the async reads a page awaits on. File is a Blob with a name.
            function Blob(blobParts, options) {
                var parts = blobParts || [];
                var pieces = [];
                for (var i = 0; i < parts.length; i++) {
                    var p = parts[i];
                    pieces.push(p === null || p === undefined ? '' : (p instanceof Blob ? p.__text : String(p)));
                }
                var text = pieces.join('');
                Object.defineProperty(this, '__text', { value: text, configurable: true });
                this.type = (options && options.type) ? String(options.type) : '';
                this.size = new TextEncoder().encode(text).length;
            }
            Blob.prototype.text = function () { var t = this.__text; return Promise.resolve(t); };
            Blob.prototype.arrayBuffer = function () { return Promise.resolve(new TextEncoder().encode(this.__text).buffer); };
            Blob.prototype.slice = function (start, end, contentType) {
                var t = this.__text;
                var s = start < 0 ? Math.max(t.length + start, 0) : (start || 0);
                var e = end === undefined ? t.length : (end < 0 ? t.length + end : end);
                var sliceText = t.substring(Math.min(s, e), Math.max(s, e));
                return new Blob([sliceText], { type: contentType || '' });
            };
            function File(fileBits, fileName, options) {
                Blob.call(this, fileBits, options);
                this.name = String(fileName === undefined ? '' : fileName);
                this.lastModified = (options && options.lastModified) ? options.lastModified : Date.now();
            }
            File.prototype = Object.create(Blob.prototype);
            File.prototype.constructor = File;

            // FileReader runs its read as a task, then fires 'load' with the result on the reader —
            // the shape every script expects from the callback API.
            function FileReader() {
                this.result = null; this.error = null; this.readyState = 0;
                this.onload = null; this.onerror = null; this.onloadend = null; this.onprogress = null;
            }
            FileReader.prototype.readAsText = function (blob) {
                var self = this;
                self.readyState = 1;
                Promise.resolve().then(function () {
                    self.result = blob.__text !== undefined ? blob.__text : String(blob);
                    self.readyState = 2;
                    if (typeof self.onload === 'function') self.onload({ target: self });
                    if (typeof self.onloadend === 'function') self.onloadend({ target: self });
                });
            };
            FileReader.prototype.readAsDataURL = function (blob) {
                var self = this;
                self.readyState = 1;
                Promise.resolve().then(function () {
                    var bytes = new TextEncoder().encode(blob.__text || '');
                    var bin = '';
                    for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
                    self.result = 'data:' + (blob.type || '') + ';base64,' + __acrux.btoa(bin);
                    self.readyState = 2;
                    if (typeof self.onload === 'function') self.onload({ target: self });
                    if (typeof self.onloadend === 'function') self.onloadend({ target: self });
                });
            };
            FileReader.prototype.abort = function () { this.readyState = 2; };

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
                evt.message = init.message === undefined ? '' : String(init.message);
                evt.error = init.error;
                evt.filename = init.filename === undefined ? '' : String(init.filename);
                evt.lineno = Number(init.lineno) || 0;
                evt.colno = Number(init.colno) || 0;
                return evt;
            }
            window.ErrorEvent = ErrorEvent;
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
            // A body is held as text here: the page-facing half of the interface is the shape around
            // it — the headers as a Headers object, 'ok', 'type' and 'url' derived from the exchange,
            // and the read methods each handing back a promise that can only be taken once (§6.3).
            function bodyToText(body) {
                if (body === undefined || body === null) return '';
                if (typeof body === 'string') return body;
                if (typeof body === 'object' && typeof body._text === 'string') return body._text;
                return String(body);
            }
            function readBody(state) {
                if (state.used) throw new TypeError('Body already read');
                state.used = true;
            }
            function bodyMethods(state, headers) {
                var self = this;
                this.text = function () {
                    readBody(state);
                    return Promise.resolve(state.text);
                };
                this.json = function () {
                    readBody(state);
                    try { return Promise.resolve(JSON.parse(state.text)); }
                    catch (e) { return Promise.reject(new SyntaxError(String(e.message))); }
                };
                this.arrayBuffer = function () {
                    readBody(state);
                    var bytes = [];
                    var source = state.text;
                    for (var i = 0; i < source.length; i++) {
                        var code = source.charCodeAt(i);
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
                    return Promise.resolve(typeof Uint8Array === 'function' ? new Uint8Array(bytes) : bytes);
                };
                this.blob = function () {
                    readBody(state);
                    return Promise.resolve(new Blob([state.text],
                        { type: headers.get('content-type') || '' }));
                };
                Object.defineProperty(this, 'bodyUsed', {
                    configurable: true,
                    get: function () { return state.used; }
                });
            }

            function Response(body, init) {
                var self = this;
                init = init || { };
                var state = { text: bodyToText(body), used: false };
                var status = init.status === undefined ? 200 : Number(init.status);
                this.headers = new Headers(init.headers || { });
                if (state.text.length && !this.headers.has('content-type'))
                    this.headers.set('content-type', 'text/plain;charset=UTF-8');
                this.url = init.url === undefined ? '' : String(init.url);
                this.redirected = !!init.redirected;
                this.type = init.type === undefined ? 'default' : String(init.type);
                this.status = status;
                this.statusText = init.statusText === undefined ? '' : String(init.statusText);
                this.ok = status >= 200 && status <= 299;
                this.body = state.text.length ? state.text : null;
                bodyMethods.call(this, state, this.headers);
                this.clone = function () {
                    return new Response(state.text, { status: self.status, statusText: self.statusText,
                        headers: self.headers, url: self.url, type: self.type });
                };
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

            function Request(input, init) {
                var self = this;
                init = init || { };
                var copied = (input && typeof input === 'object' && input.url !== undefined) ? input : null;
                var target = copied ? copied.url : String(input === undefined ? '' : input);
                var base = '';
                try { base = window.location ? window.location.href : ''; } catch (e) { base = ''; }
                try { if (base) target = new URL(target, base).href; } catch (e) { }
                var body = init.body !== undefined ? init.body : (copied ? copied.body : undefined);
                var state = { text: bodyToText(body), used: false };
                this.url = target;
                this.method = String(init.method !== undefined ? init.method
                    : (copied ? copied.method : 'GET')).toUpperCase();
                this.headers = new Headers(init.headers !== undefined ? init.headers
                    : (copied ? copied.headers : null));
                this.mode = init.mode !== undefined ? String(init.mode) : (copied ? copied.mode : 'cors');
                this.credentials = init.credentials !== undefined ? String(init.credentials)
                    : (copied ? copied.credentials : 'same-origin');
                this.cache = init.cache !== undefined ? String(init.cache) : 'default';
                this.redirect = init.redirect !== undefined ? String(init.redirect) : 'follow';
                this.referrer = init.referrer !== undefined ? String(init.referrer) : '';
                this.referrerPolicy = init.referrerPolicy !== undefined
                    ? String(init.referrerPolicy) : '';
                this.integrity = init.integrity !== undefined ? String(init.integrity) : '';
                this.keepalive = !!init.keepalive;
                this.destination = init.destination !== undefined ? String(init.destination) : '';
                this.isHistoryDownload = false;
                this.isReloaded = false;
                this.isClientNavigate = false;
                this.signal = (init.signal !== undefined && init.signal !== null)
                    ? init.signal : AbortSignal._make();
                this.body = state.text.length ? state.text : null;
                bodyMethods.call(this, state, this.headers);
                this.clone = function () {
                    return new Request(self.url, { method: self.method, headers: self.headers,
                        body: state.text, mode: self.mode, credentials: self.credentials,
                        referrerPolicy: self.referrerPolicy });
                };
            }
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
                // and the tree root never sees it (HTML §8.8).
                if ((type === 'message' || type === 'error') && listener) {
                    var ml = window[type === 'message' ? '__winMessage' : '__winError']
                        = window[type === 'message' ? '__winMessage' : '__winError'] || [];
                    if (ml.indexOf(listener) < 0) ml.push(listener);
                }
            };
            window.removeEventListener = function(type, listener, options) {
                if (document && document.removeEventListener) {
                    document.removeEventListener(type, listener, options);
                }
                var bag = type === 'message' ? window.__winMessage : (type === 'error' ? window.__winError : null);
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
            window.matchMedia = function(query) {
                var q = String(query === undefined ? '' : query).trim();
                // The same evaluator an @media rule is filtered through, so a script and a
                // stylesheet cannot disagree about the viewport (CSSOM View §7.1).
                var matches = function() { return !!__acrux.mediaQueryMatches(q); };
                var listeners = [];
                var list = {};
                // A MediaQueryList reads its own properties off the prototype and keeps them
                // non-enumerable, so 'Object.keys(mql)' is empty and a spread of it is too.
                Object.defineProperties(list, {
                    media: { get: function() { return __serializeMediaQuery(q); } },
                    matches: { get: matches },   // live: the list follows the viewport
                    onchange: { value: null, writable: true },
                    addListener: { value: function(cb) { if (typeof cb === 'function') listeners.push(cb); } },
                    removeListener: { value: function(cb) { var i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); } },
                    addEventListener: { value: function(t, cb) { if (t === 'change' && typeof cb === 'function') listeners.push(cb); } },
                    removeEventListener: { value: function(t, cb) { var i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); } },
                    dispatchEvent: { value: function() { return true; } }
                });
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
            window.requestIdleCallback = function(cb, opts) { return setTimeout(cb, 50); };
            window.cancelIdleCallback = function(id) { clearTimeout(id); };

            // HTML: 'queueMicrotask' schedules on the same job queue a resolved promise runs on, so
            // it is that promise spelled as a task — the callback lands before the next timer, after
            // the current script, exactly as the microtask checkpoint defines it.
            window.queueMicrotask = function(fn) {
                if (typeof fn !== 'function') throw new TypeError('queueMicrotask requires a function argument');
                Promise.resolve().then(function() { fn(); });
            };
            // A structural clone is a deep copy that keeps plain data (object/array graph) and
            // drops what cannot cross — functions and non-serialisable values. The full set of
            // types (Date, Map, Set, ArrayBuffer, transferables) is out of scope here, but the
            // everyday JSON-safe graph clones the way the page expects.
            window.structuredClone = function(value) {
                if (value === undefined) return undefined;
                return JSON.parse(JSON.stringify(value));
            };

            // User Timing (Performance API §3): the clock the page measures against. 'now' is the
            // milliseconds since the page began, 'mark'/'measure' keep the named timestamps a
            // profiler reads back. The high-resolution origin is the host's monotonic clock; a
            // wall-clock fallback is fine when one is not wired.
            var performance = {
                timeOrigin: Date.now(),
                now: function () { return Date.now(); },
                _marks: {},
                mark: function (name) { this._marks[String(name)] = { name: String(name), startTime: Date.now(), duration: 0 }; },
                clearMarks: function (name) { if (name === undefined) this._marks = {}; else delete this._marks[String(name)]; },
                getEntriesByName: function (name) { var m = this._marks[String(name)]; return m ? [m] : []; },
                getEntriesByType: function () { return []; },
                getEntries: function () { return []; },
                measure: function () {},
                clearMeasures: function () {},
                memory: {}
            };
            window.performance = performance;

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
                if (P) { for (var k in P) defOn(P[k]); }
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
                function makeDataset(el) {
                    var existing = store.get(el);
                    if (existing) return existing;
                    var proxy = new Proxy({}, {
                        get: function (t, p) {
                            if (typeof p !== 'string') return undefined;
                            var v = el.getAttribute('data-' + dash(p));
                            return v === null ? undefined : v;
                        },
                        set: function (t, p, v) { el.setAttribute('data-' + dash(p), (v === undefined || v === null) ? '' : String(v)); return true; },
                        deleteProperty: function (t, p) { el.removeAttribute('data-' + dash(p)); return true; },
                        has: function (t, p) { return el.getAttribute('data-' + dash(p)) !== null; },
                        ownKeys: function () { return []; },
                        getOwnPropertyDescriptor: function () { return { configurable: true, enumerable: true }; }
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
            // document prototype instead — which the host's [[Get]] resolves — returning the live global.
            (function () {
                var proto = (window.__domProto && window.__domProto['HTMLDocument'])
                    || (window.HTMLDocument && window.HTMLDocument.prototype);
                if (!proto || typeof Object.defineProperty !== 'function') return;
                if (Object.prototype.hasOwnProperty.call(proto, 'defaultView')) return;
                try {
                    Object.defineProperty(proto, 'defaultView', {
                        configurable: true, enumerable: false,
                        get: function () { return window; }
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
            Object.defineProperty(window, 'defaultStatus', __domStringAttr(''));
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
                    return proto;
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
                function MessagePort() {
                    this._queue = [];
                    this._listeners = {};
                    this.onmessage = null;
                    this._other = null;
                    this._started = false;
                    this._closed = false;
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

                function MessageChannel() {
                    var port1 = new MessagePort();
                    var port2 = new MessagePort();
                    port1._other = port2;
                    port2._other = port1;
                    this.port1 = port1;
                    this.port2 = port2;
                }

                var channelsByName = {};

                function BroadcastChannel(name) {
                    var self = this;
                    this.name = String(name);
                    this.onmessage = null;
                    this._listeners = {};
                    this._closed = false;
                    var list = channelsByName[this.name] = channelsByName[this.name] || [];
                    list.push(self);
                }
                makeTarget(BroadcastChannel.prototype);
                BroadcastChannel.prototype.postMessage = function (data) {
                    if (this._closed) return;
                    var list = channelsByName[this.name] || [];
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
                    var list = channelsByName[this.name] || [];
                    var index = list.indexOf(this);
                    if (index >= 0) list.splice(index, 1);
                };

                window.MessageEvent = MessageEvent;
                window.MessagePort = MessagePort;
                window.MessageChannel = MessageChannel;
                window.BroadcastChannel = BroadcastChannel;

                // window.postMessage(message, targetOrigin) — the message comes back to this window
                // as a message event, which is what a page that shells out to itself sees. The
                // listeners live on the window (see window.addEventListener).
                window.postMessage = function (message, targetOrigin, transfer) {
                    setTimeout(function () {
                        fireEvent(window, window.__winMessage || [], 'onmessage',
                            new MessageEvent('message', { data: message, origin: location.origin, source: window }));
                    }, 0);
                };
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
    public string atob(string str) => System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(str ?? "null"));
    public string btoa(string str) => System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(str ?? "null"));
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

    /// <summary>'crypto.randomUUID()' (Web Crypto §5.5): a version-4 identifier. The engine is
    /// the only place with a real random source, so the string is minted here.</summary>
    public string randomUUID() => Guid.NewGuid().ToString();

    /// <summary>Custom Elements §1: the script-side registry tells the engine the name it put a
    /// definition under, and the first call switches the reaction hooks on. Until a page defines its
    /// first element the engine's tree operations never cross the boundary for a callback.</summary>
    public void ceDefine(string name) => CustomElements.NoteDefinition(name ?? "");

    /// <summary>'new DOMParser().parseFromString(markup, "text/html")' (DOM Parsing §3): a brand
    /// new Document built from the markup, handed to the page as a document host so it is queried
    /// and mutated like the live one.</summary>
    public DocumentHost parseHTML(string? markup)
    {
        var html = markup ?? "";
        var newDoc = string.IsNullOrEmpty(html.Trim())
            ? new Acrux.Core.Dom.Document()
            : Acrux.Core.Dom.Parser.HtmlDocumentParserIntegration.ParseHtml(html);
        return new DocumentHost(newDoc);
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
    public int parseInt(string s, object? radix = null)
    {
        var r = radix is int ri ? ri : 10;
        try { return Convert.ToInt32(s, r); }
        catch { return int.TryParse(s, out var res) ? res : 0; }
    }
    public double parseFloat(string s) => double.TryParse(s, out var r) ? r : double.NaN;
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

public class NavigatorHost
{
    public string userAgent => "Acrux/1.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    public string appCodeName => "Mozilla";
    public string appName => "Acrux";
    public string appVersion => "1.0";
    public string platform => "Win32";
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

public class LocationHost
{
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

public class HistoryHost
{
    private readonly List<object?> _stack = new();
    private int _currentIndex = -1;
    private readonly LocationHost _location;

    public HistoryHost(LocationHost location) => _location = location;

    public int length => Math.Max(0, _stack.Count);
    public object? state => _currentIndex >= 0 && _currentIndex < _stack.Count ? _stack[_currentIndex] : null;

    public void back() { if (_currentIndex > 0) _currentIndex--; }
    public void forward() { if (_currentIndex < _stack.Count - 1) _currentIndex++; }
    public void go(int delta) { _currentIndex = Math.Max(0, Math.Min(_stack.Count - 1, _currentIndex + delta)); }
    public void pushState(object? state, string title, string? url)
    {
        while (_stack.Count > _currentIndex + 1) _stack.RemoveAt(_stack.Count - 1);
        _stack.Add(state);
        _currentIndex = _stack.Count - 1;
        if (!string.IsNullOrEmpty(url))
        {
            _location.assign(url);
        }
    }
    public void replaceState(object? state, string title, string? url)
    {
        if (_currentIndex >= 0 && _currentIndex < _stack.Count)
            _stack[_currentIndex] = state;
        if (!string.IsNullOrEmpty(url))
        {
            _location.replace(url);
        }
    }
}

public class ScreenHost
{
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
    private readonly string _name;

    public StorageHost(string name = "storage")
    {
        _name = name;
    }

    public string? getItem(string key) => _data.GetValueOrDefault(key);
    public void setItem(string key, string value) => _data[key] = value;
    public void removeItem(string key) => _data.Remove(key);
    public void clear() => _data.Clear();
    public string? key(int index) => index >= 0 && index < _data.Count ? _data.ElementAt(index).Key : null;
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
        _data[binder.Name] = value?.ToString() ?? "undefined";
        return true;
    }

    public override bool TryDeleteMember(System.Dynamic.DeleteMemberBinder binder)
    {
        _data.Remove(binder.Name);
        return true;
    }

    private static bool IsMethodName(string name) => name switch
    {
        "getItem" or "setItem" or "removeItem" or "clear" or "key" or "length" => true,
        _ => false,
    };
}