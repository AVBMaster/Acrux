using System.Collections.Concurrent;

namespace Acrux.Core.JavaScript;

public class ScriptLoadTask
{
    public int Id { get; set; }
    public string? Src { get; set; }
    public string Code { get; set; } = "";
    public ScriptType Type { get; set; }
    public int Order { get; set; }
    public string? SourceUrl { get; set; }
    public bool Loaded { get; set; }
    public bool Executed { get; set; }
}

public class ScriptExecutionQueue
{
    private readonly BrowserJsEngineFacade _facade;
    private readonly List<ScriptLoadTask> _deferQueue = new();
    private readonly List<ScriptLoadTask> _asyncQueue = new();
    private readonly List<ScriptLoadTask> _moduleQueue = new();
    private readonly ConcurrentDictionary<int, ScriptLoadTask> _loadingScripts = new();
    private readonly object _lock = new();
    private int _nextScriptId = 1;
    private int _deferOrderCounter;
    private bool _domContentLoadedFired;

    public bool AllScriptsLoaded
    {
        get
        {
            lock (_lock)
                return _loadingScripts.IsEmpty;
        }
    }

    public ScriptExecutionQueue(BrowserJsEngineFacade facade)
    {
        _facade = facade;
    }

    public int EnqueueScript(string code, ScriptType type, string? sourceUrl = null)
    {
        var task = new ScriptLoadTask
        {
            Id = Interlocked.Increment(ref _nextScriptId),
            Code = code,
            Type = type,
            SourceUrl = sourceUrl,
            Order = type == ScriptType.Defer ? Interlocked.Increment(ref _deferOrderCounter) : 0
        };

        lock (_lock)
        {
            switch (type)
            {
                case ScriptType.Defer:
                    _deferQueue.Add(task);
                    break;
                case ScriptType.Async:
                case ScriptType.Module:
                    _asyncQueue.Add(task);
                    break;
                default:
                    ExecuteImmediate(task);
                    break;
            }
        }

        return task.Id;
    }

    public int EnqueueExternalScript(string src, ScriptType type, string? sourceUrl = null)
    {
        var task = new ScriptLoadTask
        {
            Id = Interlocked.Increment(ref _nextScriptId),
            Src = src,
            Type = type,
            SourceUrl = sourceUrl,
            Order = type == ScriptType.Defer ? Interlocked.Increment(ref _deferOrderCounter) : 0
        };

        _loadingScripts[task.Id] = task;

        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                task.Code = await http.GetStringAsync(src);
                task.Loaded = true;
                _loadingScripts.TryRemove(task.Id, out _);

                lock (_lock)
                {
                    switch (type)
                    {
                        case ScriptType.Defer:
                            _deferQueue.Add(task);
                            CheckDeferQueue();
                            break;
                        case ScriptType.Module:
                            _moduleQueue.Add(task);
                            CheckModuleQueue();
                            break;
                        case ScriptType.Async:
                            ExecuteImmediate(task);
                            break;
                        default:
                            ExecuteImmediate(task);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScriptLoader] Failed to load '{src}': {ex.Message}");
                _loadingScripts.TryRemove(task.Id, out _);
            }
        });

        return task.Id;
    }

    public void FireDOMContentLoaded()
    {
        lock (_lock)
        {
            _domContentLoadedFired = true;
            ExecuteDeferQueue();
        }
    }

    public void WaitForPendingScripts()
    {
        int maxWait = 0;
        while (!_loadingScripts.IsEmpty && maxWait < 100)
        {
            Thread.Sleep(10);
            maxWait++;
        }
    }

    private void ExecuteImmediate(ScriptLoadTask task)
    {
        try
        {
            if (!string.IsNullOrEmpty(task.Code))
                _facade.Execute(task.Code, task.SourceUrl);
            task.Executed = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScriptLoader] Execute error: {ex.Message}");
        }
    }

    private void ExecuteDeferQueue()
    {
        if (!_domContentLoadedFired) return;

        var ordered = _deferQueue.OrderBy(t => t.Order).ToList();
        foreach (var task in ordered)
        {
            if (!task.Executed)
                ExecuteImmediate(task);
        }
        _deferQueue.Clear();
    }

    private void CheckDeferQueue()
    {
        if (_domContentLoadedFired)
            ExecuteDeferQueue();
    }

    private void CheckModuleQueue()
    {
        foreach (var task in _moduleQueue.ToList())
        {
            if (task.Loaded && !task.Executed)
            {
                ExecuteImmediate(task);
                _moduleQueue.Remove(task);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _deferQueue.Clear();
            _asyncQueue.Clear();
            _moduleQueue.Clear();
            _loadingScripts.Clear();
            _domContentLoadedFired = false;
            _deferOrderCounter = 0;
        }
    }
}
