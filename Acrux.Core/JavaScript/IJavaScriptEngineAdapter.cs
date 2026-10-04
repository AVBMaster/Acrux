namespace Acrux.Core.JavaScript;

public interface IJavaScriptEngineAdapter : IDisposable
{
    JsEngineType EngineType { get; }

    void Execute(string code);
    object? Evaluate(string expression);
    object? CallFunction(string functionName, params object?[] args);

    void SetGlobal(string name, object? value);
    void EmbedHostObject(string name, object? value);
    T? GetGlobal<T>(string name) where T : class;

    int StoreCallback(object callback);
    void InvokeCallback(int id);
    void InvokeCallbackWith(int id, object? arg);
    void RemoveCallback(int id);
    int CaptureFunction(string globalName);
    void ClearCallbacks();
    void Reset();

    bool SupportsHostObjects { get; }
    bool SupportsES6Proxy { get; }
    object? InnerEngine { get; }

    /// <summary>JS console output: (method like "console.log", message)</summary>
    event Action<string, string>? OnConsoleLog;
}

/// <summary>
/// Central place for adapter-kind checks so call sites stay compilable in
/// both single-process (no RemoteJsEngineAdapter) and multi-process builds.
/// </summary>
internal static class JsEngineBridge
{
    public static bool IsRemote(IJavaScriptEngineAdapter? adapter)
    {
#if USE_MULTIPLE_JS_ENGINE
        return adapter is RemoteJsEngineAdapter;
#else
        return false;
#endif
    }

    public static bool IsRemoteNotReady(IJavaScriptEngineAdapter? adapter)
    {
#if USE_MULTIPLE_JS_ENGINE
        return adapter is RemoteJsEngineAdapter remote && !remote.IsReady;
#else
        return false;
#endif
    }
}