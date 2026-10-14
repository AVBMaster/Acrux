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

            function fetch(url, opts) {
                return new Promise(function(resolve, reject) {
                    var rid = __g_store(resolve);
                    var rjid = __g_store(reject);
                    __acrux._fetch(url, JSON.stringify(opts || {}), rid, rjid);
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
                var q = (init === null || init === undefined) ? '' : String(init);
                if (q.charAt(0) === '?') q = q.slice(1);
                if (q) q.split('&').forEach(function (kv) {
                    if (kv === '') return;
                    var i = kv.indexOf('=');
                    pairs.push([dec(i < 0 ? kv : kv.slice(0, i)), dec(i < 0 ? '' : kv.slice(i + 1))]);
                });
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
                Object.defineProperty(this, 'size', { get: function () { return pairs.length; } });
            }

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
            };
            window.removeEventListener = function(type, listener, options) {
                if (document && document.removeEventListener) {
                    document.removeEventListener(type, listener, options);
                }
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
            window.postMessage = function(message, targetOrigin, transfer) {};
            window.getSelection = function() { return null; };
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

    public int _fetch(string url, string optionsJson, int resolveId, int rejectId)
    {
        var id = _engine.AddFetch(resolveId, rejectId);

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

                var response = await http.SendAsync(req);
                var text = await response.Content.ReadAsStringAsync();

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

public class StorageHost
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
}