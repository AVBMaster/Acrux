#if !USE_MULTIPLE_JS_ENGINE
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Acrux.Core.JavaScript;

/// <summary>
/// In-process Jint adapter used when the browser runs without a separate
/// JsEngineHost process (<c>UseMultipleJsEngine=false</c>). CLR host objects
/// are exposed to JS through Jint's interop layer directly, so DOM/window
/// bindings are plain method calls instead of IPC round-trips.
/// </summary>
public class JintEngineAdapter : IJavaScriptEngineAdapter, IDisposable
{
    private readonly Engine _engine;
    private readonly Dictionary<string, object?> _hostGlobals = new();
    private readonly object _cbLock = new();
    private int _nextCbId = 1;
    private bool _disposed;

    public JsEngineType EngineType => JsEngineType.Jint;
    public bool SupportsHostObjects => true;
    public bool SupportsES6Proxy => true;
    public object? InnerEngine => _engine;

    public event Action<string, string>? OnConsoleLog;

    public JintEngineAdapter()
    {
        _engine = new Engine(options =>
        {
            options.Strict(false);
            // A host method that refuses — an index outside the rule list, a rule text the
            // grammar rejects, a replaceSync on a sheet the page did not construct — has to
            // reach the page as a JavaScript error it can catch, the way a DOMException does.
            // Jint's default bubbles the CLR exception to the host instead, which aborts the
            // whole script: one refused call silently took every later statement with it.
            options.Interop.ExceptionHandler = _ => true;
            // The host values whose platform interface is a sequence — 'object?[]', which is how
            // DocumentHost exposes document.adoptedStyleSheets — have to reach the page as an
            // Array: a plain CLR array arrives as an array-like, so 'Array.isArray' answers
            // false on a value the specification calls a sequence.
            options.Interop.ObjectConverters.Add(new SheetSequenceConverter());
            options.Interop.ClrExceptionErrorDecorator = (engine, error, ex) =>
            {
                // Host errors carry their DOMException name at the front of the message
                // ("IndexSizeError: …"); lift it onto error.name so a script that switches on
                // the name sees what a browser would give it.
                var message = ex.Message ?? "";
                int cut = message.IndexOf(": ", StringComparison.Ordinal);
                if (cut <= 1 || cut > 24) return;
                var name = message.Substring(0, cut);
                if (!IsDomExceptionName(name)) return;
                error.FastSetProperty("name", new PropertyDescriptor(
                    JsValue.FromObject(engine, name),
                    PropertyFlag.ConfigurableEnumerableWritable));
            };
        });
    }

    private static bool IsDomExceptionName(string name) => name switch
    {
        "IndexSizeError" or "HierarchyRequestError" or "NotFoundError" or "NotSupportedError"
        or "NotAllowedError" or "InvalidStateError" or "SyntaxError" or "TypeMismatchError"
        or "NetworkError" or "AbortError" or "SecurityError" or "InvalidAccessError" => true,
        _ => false,
    };

    /// <summary>Turns the host's sheet sequences into JavaScript arrays. Only a value that is
    /// an <c>object?[]</c> of sheet views is claimed, so the list interfaces the engine hands
    /// out as array-likes — a live collection, a rule list — keep the shape the page measures
    /// them with, and the identity of each sheet object is the one interop gives it.</summary>
    private sealed class SheetSequenceConverter : IObjectConverter
    {
        public bool TryConvert(Engine engine, object value, out JsValue? result)
        {
            result = null;
            if (value is not object?[] items) return false;
            foreach (var item in items)
                if (item is not null and not CssStyleSheetHost) return false;

            var values = new JsValue[items.Length];
            for (int i = 0; i < items.Length; i++)
                values[i] = JsValue.FromObject(engine, items[i]!);
            result = engine.Intrinsics.Array.ConstructFast(values);
            return true;
        }
    }

    public void Execute(string code)
    {
        if (_disposed || string.IsNullOrEmpty(code)) return;
        try
        {
            _engine.Execute(code);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JS Error] {ex.Message}");
        }
    }

    public object? Evaluate(string expression)
    {
        if (_disposed || string.IsNullOrEmpty(expression)) return null;
        try
        {
            return FromJsValue(_engine.Evaluate(expression));
        }
        catch
        {
            return null;
        }
    }

    public object? CallFunction(string functionName, params object?[] args)
    {
        if (_disposed) return null;
        try
        {
            return FromJsValue(_engine.Invoke(functionName, args));
        }
        catch
        {
            return null;
        }
    }

    public void SetGlobal(string name, object? value)
    {
        if (_disposed || value == null) return;
        _engine.SetValue(name, ToJsValue(value));
        _hostGlobals[name] = value;
    }

    public void EmbedHostObject(string name, object? value) => SetGlobal(name, value);

    public T? GetGlobal<T>(string name) where T : class
    {
        if (_disposed) return null;
        try
        {
            var value = _engine.GetValue(name);
            if (value is ObjectWrapper ow && ow.Target is T typed)
                return typed;
            var clr = FromJsValue(value);
            if (clr is T fromValue)
                return fromValue;
        }
        catch
        {
        }
        if (_hostGlobals.TryGetValue(name, out var host) && host is T hostTyped)
            return hostTyped;
        return null;
    }

    public int StoreCallback(object callback)
    {
        var id = AllocCbId();
        if (_disposed || callback == null) return id;
        try
        {
            StoreInCallbackMap(id, ToJsValue(callback));
        }
        catch
        {
            try { _engine.Execute($"__g_cbs[{id}] = function(){{}};"); } catch { }
        }
        return id;
    }

    public void InvokeCallback(int id)
    {
        if (_disposed) return;
        try { _engine.Execute($"__g_invoke({id})"); } catch { }
    }

    public void InvokeCallbackWith(int id, object? arg)
    {
        if (_disposed) return;
        var tmp = $"__g_cbarg_{id}";
        try
        {
            if (arg == null)
            {
                _engine.Execute($"__g_invoke({id})");
                return;
            }

            // Host objects (ScriptEvent, ElementHost, ...) must stay wrapped so JS
            // keeps access to their methods; primitives go through JSON.
            if (!(arg is string || arg.GetType().IsPrimitive || arg is decimal))
            {
                _engine.SetValue(tmp, ToJsValue(arg));
                _engine.Execute($"__g_invoke({id}, {tmp}); delete {tmp};");
                return;
            }

            var node = JsonAotHelper.ToJsonNode(arg);
            if (node != null)
                _engine.Execute($"__g_invoke({id}, JSON.parse('{EscapeJs(node.ToJsonString())}'))");
        }
        catch
        {
            try { _engine.Execute($"delete {tmp};"); } catch { }
        }
    }

    public void RemoveCallback(int id)
    {
        if (_disposed) return;
        try { _engine.Execute($"__g_remove({id})"); } catch { }
    }

    public int CaptureFunction(string globalName)
    {
        var id = AllocCbId();
        if (_disposed) return id;
        try
        {
            var fn = _engine.GetValue(globalName);
            if (fn.Type == Types.Undefined)
                _engine.Execute($"__g_cbs[{id}] = {globalName};");
            else
                StoreInCallbackMap(id, fn);
        }
        catch { }
        return id;
    }

    public void ClearCallbacks()
    {
        if (_disposed) return;
        try { _engine.Execute("__g_cbs = {}; __g_cbid = 0;"); } catch { }
        _hostGlobals.Clear();
    }

    public void Reset()
    {
        if (_disposed) return;
        try
        {
            ClearCallbacks();
            _engine.Execute("for (var k in this) { if (typeof this[k] != 'function') delete this[k]; }");
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
        GC.SuppressFinalize(this);
    }

    private void StoreInCallbackMap(int id, JsValue value)
    {
        var tmp = $"__g_tmp_{id}";
        _engine.SetValue(tmp, value);
        _engine.Execute($"__g_cbs[{id}] = {tmp}; delete {tmp};");
    }

    private JsValue ToJsValue(object value)
    {
        if (value is JsValue js) return js;
        return JsValue.FromObject(_engine, value);
    }

    private static object? FromJsValue(JsValue value)
    {
        if (value == null) return null;
        switch (value.Type)
        {
            case Types.Undefined:
            case Types.Null:
                return null;
            case Types.String:
                return value.AsString();
            case Types.Boolean:
                return value.AsBoolean();
            case Types.Number:
                return value.AsNumber();
            default:
                return value.ToObject();
        }
    }

    private int AllocCbId()
    {
        lock (_cbLock) return _nextCbId++;
    }

    private static string EscapeJs(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
#endif
