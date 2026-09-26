using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace UpBrowser.Core.JavaScript;

public class JsPropertyDescriptor
{
    public string JsName { get; set; } = "";
    public Func<object?, object?>? Getter { get; set; }
    public Action<object?, object?>? Setter { get; set; }
    public Type? ReturnType { get; set; }
    public bool Writable { get; set; } = true;
    public bool Configurable { get; set; } = true;
    public bool Enumerable { get; set; } = true;
}

public class JsMethodDescriptor
{
    public string JsName { get; set; } = "";
    public Delegate? Method { get; set; }
    public string[]? ArgumentNames { get; set; }
}

public class JsDomDescriptor
{
    public string JsInterfaceName { get; set; } = "";
    public List<JsPropertyDescriptor> Properties { get; set; } = new();
    public List<JsMethodDescriptor> Methods { get; set; } = new();
    public List<string> PrototypeChain { get; set; } = new();
}

public class BrowserJsEngineFacade : IDisposable
{
    private readonly IJavaScriptEngineAdapter _adapter;
    private bool _disposed;
    private readonly ConcurrentDictionary<string, object?> _lazyBindings = new();
    private readonly List<Action> _postExecutionActions = new();
    private readonly object _lock = new();

    public IJavaScriptEngineAdapter Adapter => _adapter;
    public JsEngineType EngineType => _adapter.EngineType;

    public event Action<BrowserJsException>? OnScriptError;
    public event Action<string, string>? OnConsoleLog;

    /// <summary>
    /// Invoked after any JS callback (timer / microtask / event listener) ran,
    /// so the host can mark the page dirty — DOM mutations from callbacks must
    /// trigger a re-render, not just from top-level script execution.
    /// </summary>
    public Action? OnCallbacksExecuted;

    public BrowserJsEngineFacade(IJavaScriptEngineAdapter adapter)
    {
        _adapter = adapter;
        _adapter.OnConsoleLog += OnConsoleLog;
    }

    public void SetGlobalObject(string name, object? hostObject)
    {
        if (hostObject == null) return;
        _adapter.SetGlobal(name, hostObject);
    }

    public T? GetGlobalObject<T>(string name) where T : class
    {
        return _adapter.GetGlobal<T>(name);
    }

    public void Execute(string code, string? sourceUrl = null, int lineOffset = 0)
    {
        if (_disposed || string.IsNullOrEmpty(code)) return;
        // 远程引擎未就绪时跳过执行，避免阻塞 UI
        if (JsEngineBridge.IsRemoteNotReady(_adapter))
            return;
        Current = _adapter;
        try
        {
            _adapter.Execute(code);
            FlushPostExecution();
        }
        catch (Exception ex)
        {
            var browserEx = new BrowserJsException(ex.Message, sourceUrl ?? "", lineOffset, 0, ex.StackTrace ?? "");
            OnScriptError?.Invoke(browserEx);
            Console.WriteLine($"[JS Error] {browserEx.Message} at {browserEx.SourceUrl}:{browserEx.LineNumber}:{browserEx.ColumnNumber}");
        }
        finally
        {
            Current = null;
        }
    }

    public object? Evaluate(string expression, string? sourceUrl = null)
    {
        if (_disposed || string.IsNullOrEmpty(expression)) return null;
        if (JsEngineBridge.IsRemoteNotReady(_adapter)) return null;
        Current = _adapter;
        try
        {
            var result = _adapter.Evaluate(expression);
            FlushPostExecution();
            return result;
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

    public object? CallFunction(string functionName, params object?[] args)
    {
        if (_disposed) return null;
        if (JsEngineBridge.IsRemoteNotReady(_adapter)) return null;
        Current = _adapter;
        try
        {
            var result = _adapter.CallFunction(functionName, args);
            FlushPostExecution();
            return result;
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

    public int StoreJsFunction(object callback)
    {
        return _adapter.StoreCallback(callback);
    }

    public void InvokeJsFunction(int callbackId, params object?[] args)
    {
        if (_disposed) return;
        if (JsEngineBridge.IsRemoteNotReady(_adapter)) return;
        Current = _adapter;
        var mutationBefore = UpBrowser.Core.Dom.DomMutationTracker.Version;
        try
        {
            if (args == null || args.Length == 0)
                _adapter.InvokeCallback(callbackId);
            else if (args.Length == 1)
                _adapter.InvokeCallbackWith(callbackId, args[0]);
            else
                {
                    var json = SerializeArgsToJson(args);
                    _adapter.Execute($"__g_invoke({callbackId}, JSON.parse('{EscapeJsString(json)}'))");
                }
            FlushPostExecution();
            // Only notify "page may need rebuild" when the callback actually
            // mutated the DOM — a pure-compute timer must not trigger a full
            // style/layout/paint cycle and a tile-cache wipe.
            if (UpBrowser.Core.Dom.DomMutationTracker.Version != mutationBefore)
                OnCallbacksExecuted?.Invoke();
        }
        catch { }
        finally
        {
            Current = null;
        }
    }

    public void RemoveJsFunction(int callbackId)
    {
        _adapter.RemoveCallback(callbackId);
    }

    public void RegisterLazyBinding(string name, Func<object?> factory)
    {
        _lazyBindings[name] = factory;
    }

    public object? ResolveLazyBinding(string name)
    {
        if (_lazyBindings.TryGetValue(name, out var value))
        {
            if (value is Func<object?> factory)
            {
                var resolved = factory();
                _lazyBindings[name] = resolved;
                return resolved;
            }
            return value;
        }
        return null;
    }

    public void RegisterPostExecution(Action action)
    {
        lock (_lock)
            _postExecutionActions.Add(action);
    }

    private void FlushPostExecution()
    {
        List<Action> actions;
        lock (_lock)
        {
            actions = new List<Action>(_postExecutionActions);
            _postExecutionActions.Clear();
        }
        foreach (var action in actions)
        {
            try { action(); }
            catch { }
        }
    }

    public void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void Reset()
    {
        _adapter.Reset();
        _lazyBindings.Clear();
        lock (_lock) _postExecutionActions.Clear();
    }

    public void ClearCallbacks()
    {
        _adapter.ClearCallbacks();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _adapter.Dispose();
        _lazyBindings.Clear();
        lock (_lock) _postExecutionActions.Clear();
        GC.SuppressFinalize(this);
    }

    [ThreadStatic]
    public static IJavaScriptEngineAdapter? Current;

    private static string SerializeArgsToJson(object?[] args)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(ToJsonLiteral(args[i]));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string ToJsonLiteral(object? value)
    {
        if (value is null) return "null";
        if (value is bool b) return b ? "true" : "false";
        if (value is string s) return $"'{EscapeJsString(s)}'";

        if (value is int i) return i.ToString();
        if (value is long l) return l.ToString();
        if (value is uint ui) return ui.ToString();
        if (value is ulong ul) return ul.ToString();
        if (value is short sh) return sh.ToString();
        if (value is ushort us) return us.ToString();
        if (value is byte bt) return bt.ToString();
        if (value is sbyte sb) return sb.ToString();

        if (value is double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
            return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (value is float f)
        {
            if (float.IsNaN(f)) return "NaN";
            if (float.IsInfinity(f)) return f > 0 ? "Infinity" : "-Infinity";
            return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Fallback for unknown types: use ToString() wrapped as string
        return $"'{EscapeJsString(value.ToString() ?? "")}'";
    }

    private static string EscapeJsString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}