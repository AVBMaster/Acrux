namespace Acrux.Core.JavaScript;

public enum JsEngineType
{
    V8,
    Jint,
    Jurassic
}

public static class JsEngineConfig
{
    private static JsEngineType _defaultEngineType = JsEngineType.Jint;

    public static JsEngineType DefaultEngineType
    {
        get => _defaultEngineType;
        set => _defaultEngineType = value;
    }

    public static JsEngineType EffectiveEngineType
    {
        get
        {
#if !USE_MULTIPLE_JS_ENGINE
            // Single-process mode always runs the built-in Jint engine
            return JsEngineType.Jint;
#else
            if (_defaultEngineType != JsEngineType.Jint && !JsEngineDownloader.IsEngineDownloaded(_defaultEngineType))
                return JsEngineType.Jint;
            return _defaultEngineType;
#endif
        }
    }

    public static JsEngineType? GetEngineTypeByName(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            "v8" => JsEngineType.V8,
            "jint" => JsEngineType.Jint,
            "jurassic" => JsEngineType.Jurassic,
            _ => null
        };
    }

    public static string GetEngineName(JsEngineType type) => type switch
    {
        JsEngineType.V8 => "V8JsEngine",
        JsEngineType.Jint => "JintJsEngine",
        JsEngineType.Jurassic => "JurassicJsEngine",
        _ => "JintJsEngine"
    };

    public static bool IsEngineAvailable(JsEngineType type)
    {
        if (type == JsEngineType.Jint) return true;
        return JsEngineDownloader.IsEngineDownloaded(type);
    }
}