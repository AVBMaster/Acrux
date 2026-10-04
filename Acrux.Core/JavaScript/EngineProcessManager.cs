#if USE_MULTIPLE_JS_ENGINE
namespace Acrux.Core.JavaScript;

/// <summary>
/// 管理 JsEngineHost 进程的生命周期和连接池。
/// </summary>
public static class EngineProcessManager
{
    private static readonly Dictionary<int, RemoteJsEngineAdapter> _engines = new();
    private static readonly object _lock = new();
    private static int _nextChannelId;
    private static readonly string _pidSuffix = System.Diagnostics.Process.GetCurrentProcess().Id.ToString("x8");

    /// <summary>
    /// 获取或创建指定标签页的远程 JS 引擎适配器。
    /// 非阻塞：JS 引擎在后台启动，返回适配器可能尚未就绪，调用方应使用 IsReady 检查。
    /// </summary>
    public static RemoteJsEngineAdapter GetOrCreate(int tabIndex, string engineType)
    {
        lock (_lock)
        {
            if (_engines.TryGetValue(tabIndex, out var existing))
            {
                if (existing.IsReady && existing.EngineType == (JsEngineConfig.GetEngineTypeByName(engineType) ?? JsEngineType.Jint))
                    return existing;
                if (existing.IsReady)
                {
                    // Engine type changed — dispose old engine so a new one is created
                    Console.WriteLine($"[EngineProcessManager] Tab {tabIndex} engine type changed from {existing.EngineType} to {engineType}, disposing old engine");
                    existing.Dispose();
                    _engines.Remove(tabIndex);
                }
                else
                {
                    // 已存在但尚未就绪，返回等待（已启动后台任务）
                    return existing;
                }
            }

            var channelId = Interlocked.Increment(ref _nextChannelId);
            var channelName = $"Acrux_JS_{_pidSuffix}_{tabIndex}_{channelId}";
            var adapter = new RemoteJsEngineAdapter(channelName, engineType, tabIndex);
            // 在后台启动，不阻塞 UI 线程
            _ = Task.Run(async () =>
            {
                try
                {
                    await adapter.StartAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EngineProcessManager] Failed to start JS engine for tab={tabIndex}: {ex.Message}");
                }
            });
            _engines[tabIndex] = adapter;
            return adapter;
        }
    }

    /// <summary>
    /// 释放指定标签页的 JS 引擎。
    /// </summary>
    public static void Release(int tabIndex)
    {
        lock (_lock)
        {
            if (_engines.TryGetValue(tabIndex, out var adapter))
            {
                adapter.Dispose();
                _engines.Remove(tabIndex);
            }
        }
    }

    /// <summary>
    /// 释放所有 JS 引擎。
    /// </summary>
    public static void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (var adapter in _engines.Values)
                adapter.Dispose();
            _engines.Clear();
        }
    }
}
#endif
