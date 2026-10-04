namespace Acrux.Core.JavaScript;

/// <summary>
/// 空 JS 引擎适配器：当远程 JS 引擎不可用时作为兜底。
/// 所有操作均为空操作，保证 UI 进程不因 JS 问题崩溃。
/// </summary>
public class NullJsEngineAdapter : IJavaScriptEngineAdapter, IDisposable
{
    private static readonly NullJsEngineAdapter _instance = new();

    public static NullJsEngineAdapter Instance => _instance;

    public JsEngineType EngineType => JsEngineType.Jint;
    public object? InnerEngine => null;
    public bool SupportsHostObjects => false;
    public bool SupportsES6Proxy => false;

    public event Action<string, string>? OnConsoleLog;

    public NullJsEngineAdapter()
    {
    }

    public void Execute(string code)
    {
        // 空操作：JS 引擎不可用
    }

    public object? Evaluate(string expression)
    {
        return null;
    }

    public object? CallFunction(string functionName, params object?[] args)
    {
        return null;
    }

    public void SetGlobal(string name, object? value)
    {
    }

    public void EmbedHostObject(string name, object? value)
    {
    }

    public T? GetGlobal<T>(string name) where T : class
    {
        return null;
    }

    public int StoreCallback(object callback)
    {
        return 0;
    }

    public void InvokeCallback(int id)
    {
    }

    public void InvokeCallbackWith(int id, object? arg)
    {
    }

    public void RemoveCallback(int id)
    {
    }

    public int CaptureFunction(string globalName)
    {
        return 0;
    }

    public void ClearCallbacks()
    {
    }

    public void Reset()
    {
    }

    public void Dispose()
    {
    }
}
