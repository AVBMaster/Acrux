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

    public int AddListener(string type, object jsCallback, bool useCapture = false, bool once = false, bool passive = false, AbortSignal? signal = null)
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
                Signal = signal
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

    public bool DispatchEvent(string type, DomEvent evt, object? target = null)
    {
        List<JsEventListenerEntry> listeners;
        lock (_lock)
        {
            if (!_listeners.TryGetValue(type, out var all))
                return true;
            listeners = all.ToList();
        }

        foreach (var listener in listeners)
        {
            if (listener.Signal?.Aborted == true)
            {
                RemoveListener(type, listener.CallbackId);
                continue;
            }

            if (listener.Once)
                RemoveListener(type, listener.CallbackId);

            try
            {
                if (target != null)
                    _facade.InvokeJsFunction(listener.CallbackId, target);
                else
                    _facade.InvokeJsFunction(listener.CallbackId);
            }
            catch { }
        }

        return true;
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
