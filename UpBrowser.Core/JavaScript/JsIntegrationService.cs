using System.Text;
using System.Text.Json;
using DomDocument = UpBrowser.Core.Dom.Document;
using DomElement = UpBrowser.Core.Dom.Element;
using DomEvent = UpBrowser.Core.Dom.Event;

namespace UpBrowser.Core.JavaScript;

    public class JsIntegrationService : IDisposable
{
    private readonly BrowserJsEngineFacade _facade;
    private readonly ObjectIdentityMap _identityMap;
    private readonly JsEventListenerBridge _eventBridge;
    private readonly JsTimerQueue _timerQueue;
    private readonly JsMicrotaskQueue _microtaskQueue;
    private readonly ScriptExecutionQueue _scriptQueue;
    private readonly JsDomPrototypeSystem _prototypeSystem;
    private JavaScriptEngine? _jsEngine;
    private bool _disposed;
    private DomDocument? _currentDocument;

    public BrowserJsEngineFacade Facade => _facade;
    public ObjectIdentityMap IdentityMap => _identityMap;
    public JsEventListenerBridge EventBridge => _eventBridge;
    public JsTimerQueue TimerQueue => _timerQueue;
    public JsMicrotaskQueue MicrotaskQueue => _microtaskQueue;
    public ScriptExecutionQueue ScriptQueue => _scriptQueue;
    public JsDomPrototypeSystem PrototypeSystem => _prototypeSystem;

    public JsIntegrationService(IJavaScriptEngineAdapter adapter)
    {
        _facade = new BrowserJsEngineFacade(adapter);
        _identityMap = new ObjectIdentityMap();
        _eventBridge = new JsEventListenerBridge(_facade);
        _timerQueue = new JsTimerQueue(_facade);
        _microtaskQueue = new JsMicrotaskQueue(_facade);
        _scriptQueue = new ScriptExecutionQueue(_facade);
        _prototypeSystem = new JsDomPrototypeSystem(_facade);

        _facade.OnScriptError += OnScriptError;

        InitializeEnvironment();
    }

    public void SetJsEngine(JavaScriptEngine engine)
    {
        _jsEngine = engine;
        // Callbacks (timers/listeners/microtasks) that mutate the DOM must
        // funnel into the same dirty flag that top-level Execute() uses.
        _facade.OnCallbacksExecuted = () => engine.MarkDirty();
    }

    private void InitializeEnvironment()
    {
        _facade.Execute("var window = this; var globalThis = this; var self = this;");
        _prototypeSystem.RegisterBuiltinConstructors();
        SetupJsBridgeFunctions();
    }

    private void SetupJsBridgeFunctions()
    {
        var bridgeCode = @"
(function() {
    if (window.__bridgeInited) return;
    window.__bridgeInited = true;

    window.__identityMap = {};

    window.__createDomWrapper = function(handle, typeName) {
        if (window.__identityMap[handle]) return window.__identityMap[handle];
        var wrapper = { __handle: handle, __type: typeName || 'Node' };
        window.__identityMap[handle] = wrapper;
        return wrapper;
    };

    window.__getDomWrapper = function(handle) {
        return window.__identityMap[handle] || null;
    };

    window.__releaseDomWrapper = function(handle) {
        delete window.__identityMap[handle];
    };
})();
";
        try { _facade.Execute(bridgeCode); }
        catch { }
    }

    private void OnScriptError(BrowserJsException ex)
    {
        Console.WriteLine($"[JS Error] {ex.ErrorType}: {ex.Message} at {ex.SourceUrl}:{ex.LineNumber}:{ex.ColumnNumber}");
        if (!string.IsNullOrEmpty(ex.JsStackTrace))
            Console.WriteLine($"[JS Stack]\n{ex.JsStackTrace}");
    }

    public void LoadDocument(DomDocument document)
    {
        var prevEngine = JavaScriptEngine.Current;
        JavaScriptEngine.Current = _jsEngine;
        try
        {
            _currentDocument = document;
            _identityMap.Clear();
            _scriptQueue.Clear();
            _timerQueue.ClearAll();
            _eventBridge.ClearListeners();

            _facade.Reset();
            _facade.Execute("var window = this; var self = this; var globalThis = this;");
            SetupJsBridgeFunctions();
            _prototypeSystem.Reset();
            _prototypeSystem.RegisterBuiltinConstructors();

            var docHost = new DocumentHost(document);
            // 远程引擎已在 DomSetup.js 中定义 document，无需通过 SetGlobal 发送 .NET 对象
            if (!JsEngineBridge.IsRemote(_facade.Adapter))
            {
                _facade.SetGlobalObject("document", docHost);
            }

            // 远程引擎的 document 已在 JsEngineHost 的 DomSetup.js 中定义，
            // 无需执行 window.document = document 和原型修复
            if (!JsEngineBridge.IsRemote(_facade.Adapter))
            {
                _facade.Execute("window.document = document;");
                _facade.Execute("window.__fixProto(document);");

                // Post-load fixup: fix prototypes on all elements reachable from document
                _facade.Execute(@"
(function() {
    function fixTree(el) {
        window.__fixProto(el);
        if (el.children) {
            for (var i = 0; i < el.children.length; i++) {
                var child = el.children[i];
                if (child && child.tagName) fixTree(child);
            }
        }
    }
    if (document.body) fixTree(document.body);
    if (document.documentElement) window.__fixProto(document.documentElement);
    if (document.head) window.__fixProto(document.head);
})();
");
            }
        }
        finally
        {
            JavaScriptEngine.Current = prevEngine;
        }
    }

    public object? WrapDomNode(DomElement element)
    {
        if (element == null) return null;
        var tempHost = new ElementHost(element);
        if (tempHost.Engine == null)
            tempHost.Engine = _jsEngine;
        var handle = _identityMap.GetOrCreateHandle(element, () => tempHost);
        var cached = _identityMap.GetWrapper(handle);
        if (cached != null)
        {
            if (cached is ElementHost cachedHost && cachedHost.Engine == null)
                cachedHost.Engine = _jsEngine;
            FixHostProto(cached);
            return cached;
        }
        FixHostProto(tempHost);
        return tempHost;
    }

    public void FixHostProto(object host)
    {
        try
        {
            if (JsEngineBridge.IsRemote(_facade.Adapter)) return;
            var id = Interlocked.Increment(ref _fixProtoCounter);
            var tmpName = $"__tmp_fp_{id}";
            _facade.SetGlobalObject(tmpName, host);
            _facade.Execute($"window.__fixProto({tmpName}); delete {tmpName};");
        }
        catch { }
    }

    public static void FixProto(object host)
    {
        var engine = JavaScriptEngine.Current;
        engine?.IntegrationService?.FixHostProto(host);
    }

    public object? WrapDomNode(Dom.Node node)
    {
        if (node is DomElement el) return WrapDomNode(el);
        if (node is Dom.TextNode tn) return new TextNodeWrapper(tn);
        if (node is Dom.DocumentFragment df) return new DocumentFragmentHost(df);
        return null;
    }

    public void ExecuteScript(string code, string? sourceUrl = null, ScriptType type = ScriptType.Inline, int lineOffset = 0)
    {
        var prevEngine = JavaScriptEngine.Current;
        JavaScriptEngine.Current = _jsEngine;
        try
        {
            switch (type)
            {
                case ScriptType.Inline:
                case ScriptType.External:
                    _facade.Execute(code, sourceUrl, lineOffset);
                    _microtaskQueue.DrainMicrotasks();
                    break;
                case ScriptType.Defer:
                    _scriptQueue.EnqueueScript(code, type, sourceUrl);
                    break;
                case ScriptType.Async:
                case ScriptType.Module:
                    _scriptQueue.EnqueueScript(code, type, sourceUrl);
                    break;
            }
        }
        finally
        {
            JavaScriptEngine.Current = prevEngine;
        }
    }

    public void FireDOMContentLoaded()
    {
        var prevEngine = JavaScriptEngine.Current;
        JavaScriptEngine.Current = _jsEngine;
        try
        {
            _scriptQueue.FireDOMContentLoaded();
            _microtaskQueue.DrainMicrotasks();

            var engine = JavaScriptEngine.Current;
            if (engine?.DocumentHost != null)
            {
                try
                {
                    engine.DocumentHost.dispatchEvent(new ScriptEvent("DOMContentLoaded", null));
                }
                catch { }
            }
        }
        finally
        {
            JavaScriptEngine.Current = prevEngine;
        }
    }

    public void DispatchEvent(DomElement target, DomEvent evt)
    {
        var domEvent = MapDomEvent(evt);
        _eventBridge.DispatchEvent(evt.Type, evt, target != null ? WrapDomNode(target) : null);
        _microtaskQueue.DrainMicrotasks();
    }

    public int SetTimeout(int callbackId, int delayMs, int nestingLevel = 0)
    {
        return _timerQueue.SetTimeout(callbackId, delayMs, nestingLevel);
    }

    public int SetInterval(int callbackId, int intervalMs, int nestingLevel = 0)
    {
        return _timerQueue.SetInterval(callbackId, intervalMs, nestingLevel);
    }

    public void ClearTimer(int timerId)
    {
        _timerQueue.ClearTimer(timerId);
    }

    public void ProcessTimers()
    {
        _timerQueue.ProcessTimers();
        _microtaskQueue.DrainMicrotasks();
    }

    /// <summary>Ms until the next JS timer fires (int.MaxValue = none).</summary>
    public int NextTimerDelayMs() => _timerQueue.NextTimerDelayMs();

    public void CollectGarbage()
    {
        _facade.CollectGarbage();
        _identityMap.CleanupStaleEntries();
    }

    public void ThrottleInactiveTabs()
    {
        _timerQueue.ThrottleInactiveTabs();
    }

    public void Reset()
    {
        _facade.Reset();
        _identityMap.Clear();
        _timerQueue.ClearAll();
        _eventBridge.ClearListeners();
        _scriptQueue.Clear();
        _microtaskQueue.Clear();
    }

    public BrowserJsException? GetLastException()
    {
        return _lastException;
    }

    private BrowserJsException? _lastException;
    private int _fixProtoCounter;

    private static DomEvent MapDomEvent(DomEvent evt)
    {
        return evt;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timerQueue.ClearAll();
        _eventBridge.ClearListeners();
        _scriptQueue.Clear();
        _microtaskQueue.Clear();
        _identityMap.Dispose();
        _facade.Dispose();
        GC.SuppressFinalize(this);
    }
}
