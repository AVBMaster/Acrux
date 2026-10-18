using System.Collections.Concurrent;
using Acrux.Core.Dom;
using DomEvent = Acrux.Core.Dom.Event;

namespace Acrux.Core.JavaScript;

public class JsEventListenerEntry
{
    public int CallbackId { get; set; }
    public object? JsCallback { get; set; }
    public bool UseCapture { get; set; }
    public bool Once { get; set; }
    public bool Passive { get; set; }
    public AbortSignal? Signal { get; set; }
    /// <summary>The token the script side made for an <c>{signal:}</c> listener (DOM §2.7.7). The
    /// signal itself is a script object and does not survive the call, so the token is asked, once per
    /// dispatch, whether the controller behind it has aborted — the same rule an element's listener
    /// keeps.</summary>
    public int SignalToken { get; set; }
}

public class JsEventListenerBridge
{
    private readonly BrowserJsEngineFacade _facade;
    private readonly ConcurrentDictionary<string, List<JsEventListenerEntry>> _listeners = new();
    private readonly object _lock = new();

    public JsEventListenerBridge(BrowserJsEngineFacade facade)
    {
        _facade = facade;
    }

    public int AddListener(string type, object jsCallback, bool useCapture = false, bool once = false, bool passive = false, AbortSignal? signal = null, int signalToken = 0)
    {
        // Use __g_store from JS side to properly store the callback function reference.
        // This avoids StoreJsFunction which uses EmbedHostObject and fails for
        // ClearScript/V8 ScriptObject types, silently replacing the callback with a no-op.
        int callbackId;
        try
        {
            var result = _facade.CallFunction("__g_store", jsCallback);
            if (result is int id)
                callbackId = id;
            else if (result is double d)
                callbackId = (int)d;
            else if (result is long l)
                callbackId = (int)l;
            else
                callbackId = _facade.StoreJsFunction(jsCallback);
        }
        catch
        {
            callbackId = _facade.StoreJsFunction(jsCallback);
        }

        lock (_lock)
        {
            // Check if this callbackId is already registered for this type to avoid duplicates
            if (_listeners.TryGetValue(type, out var existing))
            {
                if (existing.Any(l => l.CallbackId == callbackId))
                    return callbackId;
            }

            var entry = new JsEventListenerEntry
            {
                CallbackId = callbackId,
                JsCallback = jsCallback,
                UseCapture = useCapture,
                Once = once,
                Passive = passive,
                Signal = signal,
                SignalToken = signalToken
            };

            var list = existing ?? new List<JsEventListenerEntry>();
            list.Add(entry);
            _listeners[type] = list;
        }

        if (signal != null && !signal.Aborted)
            signal.AddCallback(() => RemoveListener(type, callbackId));

        return callbackId;
    }

    public void RemoveListener(string type, int callbackId)
    {
        lock (_lock)
        {
            if (_listeners.TryGetValue(type, out var list))
            {
                list.RemoveAll(l => l.CallbackId == callbackId);
                if (list.Count == 0)
                    _listeners.TryRemove(type, out _);
            }
        }
        _facade.RemoveJsFunction(callbackId);
    }

    public void RemoveListener(string type, object jsCallback)
    {
        // Use __g_store to resolve the callback to an ID (reuses existing ID if already stored)
        int? callbackId = null;
        try
        {
            var result = _facade.CallFunction("__g_store", jsCallback);
            if (result is int id) callbackId = id;
            else if (result is double d) callbackId = (int)d;
            else if (result is long l) callbackId = (int)l;
        }
        catch { }

        lock (_lock)
        {
            if (_listeners.TryGetValue(type, out var list))
            {
                if (callbackId.HasValue)
                    list.RemoveAll(l => l.CallbackId == callbackId.Value);
                else
                    list.RemoveAll(l => ReferenceEquals(l.JsCallback, jsCallback));
                if (list.Count == 0)
                    _listeners.TryRemove(type, out _);
            }
        }
    }

    public List<JsEventListenerEntry> GetListeners(string type, bool capture = false)
    {
        lock (_lock)
        {
            if (!_listeners.TryGetValue(type, out var list))
                return new List<JsEventListenerEntry>();

            return list.Where(l => l.UseCapture == capture).ToList();
        }
    }

    public bool DispatchEvent(string type, object? evt, object? target = null)
    {
        // The document is the target of its own dispatch, so both kinds of listener run there in the
        // order they were put in place (DOM §2.9).
        Invoke(type, capture: null, evt, target);
        return true;
    }

    /// <summary>One leg of a walk that started on a node below the document: the capture listeners run
    /// before the path is walked in and the rest after the bubble has come out (DOM §2.5). The bag is
    /// the document's, so a page that put a listener on the window — which shares it — is heard on the
    /// same leg.</summary>
    public void InvokeCapturePhase(string type, object eventObject)
        => Invoke(type, capture: true, eventObject, null);

    public void InvokeBubblePhase(string type, object eventObject)
        => Invoke(type, capture: false, eventObject, null);

    private void Invoke(string type, bool? capture, object? eventObject, object? target)
    {
        List<JsEventListenerEntry> listeners;
        lock (_lock)
        {
            if (!_listeners.TryGetValue(type, out var all))
                return;
            listeners = all.Where(l => capture == null || l.UseCapture == capture).ToList();
        }

        var engine = JavaScriptEngine.Current;
        var drop = new List<int>();
        foreach (var listener in listeners)
        {
            if (listener.Signal?.Aborted == true
                || (listener.SignalToken != 0 && engine != null
                    && ElementHost.SignalAborted(engine, listener.SignalToken)))
            {
                drop.Add(listener.CallbackId);
                continue;
            }

            try
            {
                // The listener is handed the event it is there for (DOM §2.9): a page that reads
                // 'e.target' off a document or window listener is reading the same object a node
                // listener is given.
                if (target != null)
                    _facade.InvokeJsFunction(listener.CallbackId, target);
                else if (eventObject != null)
                    _facade.InvokeJsFunction(listener.CallbackId, eventObject);
                else
                    _facade.InvokeJsFunction(listener.CallbackId);
            }
            catch { }

            // 'once' takes the registration away after it has run — taking it away first also frees
            // the callback the run is about to make (DOM §2.9, step 6).
            if (listener.Once)
                drop.Add(listener.CallbackId);
        }

        foreach (var callbackId in drop)
            RemoveListener(type, callbackId);
    }

    public void ClearListeners()
    {
        lock (_lock)
        {
            foreach (var kvp in _listeners)
            {
                foreach (var entry in kvp.Value)
                    _facade.RemoveJsFunction(entry.CallbackId);
            }
            _listeners.Clear();
        }
    }

    public int ListenerCount
    {
        get
        {
            lock (_lock)
                return _listeners.Sum(kvp => kvp.Value.Count);
        }
    }
}
