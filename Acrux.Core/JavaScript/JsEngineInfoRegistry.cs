namespace Acrux.Core.JavaScript;

/// <summary>
/// 管理 JS 引擎安装信息（从本地目录安装的引擎路径等元数据）。
/// 存储于 %APPDATA%\Acrux\engine_registry.txt（简单 key=value 格式，避免 JSON 序列化问题）。
/// </summary>
public static class JsEngineInfoRegistry
{
    private static string RegistryPath => Path.Combine(AppPaths.AppDataDir, "engine_registry.txt");

    private static EngineRegistry? _data;

    public static EngineRegistry Data
    {
        get
        {
            if (_data != null) return _data;
            if (File.Exists(RegistryPath))
            {
                try
                {
                    var data = new EngineRegistry();
                    foreach (var line in File.ReadAllLines(RegistryPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                            continue;
                        var parts = line.Split('|', 4);
                        if (parts.Length >= 4)
                        {
                            data.Engines[parts[0].Trim().ToLowerInvariant()] = new EngineInfo
                            {
                                AssemblyPath = parts[1].Trim(),
                                SourceFolder = parts[2].Trim(),
                                InstalledAt = parts[3].Trim()
                            };
                        }
                    }
                    _data = data;
                }
                catch { _data = new EngineRegistry(); }
            }
            _data ??= new EngineRegistry();
            return _data;
        }
    }

    public static void Register(JsEngineType type, string assemblyPath, string sourceFolder)
    {
        var data = Data;
        data.Engines[type.ToString().ToLowerInvariant()] = new EngineInfo
        {
            AssemblyPath = assemblyPath,
            SourceFolder = sourceFolder,
            InstalledAt = DateTime.UtcNow.ToString("O")
        };
        Save();
    }

    public static EngineInfo? GetInfo(JsEngineType type)
    {
        var data = Data;
        var key = type.ToString().ToLowerInvariant();
        return data.Engines.TryGetValue(key, out var info) ? info : null;
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        var lines = new List<string>();
        foreach (var kvp in Data.Engines)
        {
            lines.Add($"{kvp.Key}|{kvp.Value.AssemblyPath}|{kvp.Value.SourceFolder}|{kvp.Value.InstalledAt}");
        }
        File.WriteAllLines(RegistryPath, lines);
    }

    public class EngineRegistry
    {
        public Dictionary<string, EngineInfo> Engines = new();
    }

    public class EngineInfo
    {
        public string AssemblyPath { get; set; } = "";
        public string SourceFolder { get; set; } = "";
        public string InstalledAt { get; set; } = "";
    }
}
