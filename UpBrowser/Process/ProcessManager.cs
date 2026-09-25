using System.Collections.Concurrent;
using UpBrowser.Core.EventLoop;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.Process;
using UpBrowser.Rendering;

namespace UpBrowser.Process;

public class ProcessManager : IDisposable
{
    private readonly string[] _fontFamilies;
    private readonly EventLoop _eventLoop;
    private readonly float _dpiScale;
    private readonly float _contentOffset;
    private readonly float _resolutionScale;

    private readonly ConcurrentDictionary<int, TabProcess> _processes = new();
    private readonly object _lock = new();

    public int ActiveProcessCount => _processes.Count;

    public event Action<TabProcess>? OnProcessUpdated;

    public ProcessManager(string[] fontFamilies, EventLoop eventLoop,
        float dpiScale = 1f, float contentOffset = 0, float resolutionScale = 1f)
    {
        _fontFamilies = fontFamilies;
        _eventLoop = eventLoop;
        _dpiScale = dpiScale;
        _contentOffset = contentOffset;
        _resolutionScale = resolutionScale;
    }

    public TabProcess CreateProcess(int tabIndex, string url = "upbrowser://newtab")
    {
        lock (_lock)
        {
            if (_processes.TryGetValue(tabIndex, out var existing))
            {
                if (existing.IsAlive)
                    return existing;
                existing.Dispose();
                _processes.TryRemove(tabIndex, out _);
            }

            var proc = new TabProcess(tabIndex, url, _fontFamilies,
                _eventLoop, _dpiScale, _contentOffset, _resolutionScale);
            proc.OnUpdated += OnProcessUpdated;
            _processes[tabIndex] = proc;
            proc.Start();
            return proc;
        }
    }

    public TabProcess? GetProcess(int tabIndex)
    {
        _processes.TryGetValue(tabIndex, out var proc);
        return proc;
    }

    public void DestroyProcess(int tabIndex)
    {
        if (_processes.TryRemove(tabIndex, out var proc))
        {
            proc.OnUpdated -= OnProcessUpdated;
            proc.Dispose();
#if USE_MULTIPLE_JS_ENGINE
            EngineProcessManager.Release(tabIndex);
#endif
        }
    }

    public TabProcess? GetOrCreate(int tabIndex, string url = "upbrowser://newtab")
    {
        var proc = GetProcess(tabIndex);
        if (proc != null && proc.IsAlive)
            return proc;
        return CreateProcess(tabIndex, url);
    }

    public void DestroyAll()
    {
        foreach (var kv in _processes.ToArray())
        {
            DestroyProcess(kv.Key);
        }
    }

    public DisplayList? GetDisplayList(int tabIndex)
    {
        var proc = GetProcess(tabIndex);
        return proc?.GetDisplayList();
    }

    public TabProcessMetrics? GetMetrics(int tabIndex)
    {
        var proc = GetProcess(tabIndex);
        return proc?.GetMetrics();
    }

    public List<TabProcess> GetAllProcesses()
    {
        return _processes.Values.Where(p => p.IsAlive).ToList();
    }

    public List<TabProcessMetrics> GetAllMetrics()
    {
        return _processes.Values
            .Where(p => p.IsAlive)
            .Select(p => p.GetMetrics())
            .ToList();
    }

    public void Navigate(int tabIndex, string html, string? baseUrl = null)
    {
        var proc = GetOrCreate(tabIndex);
        if (proc == null) return;
        proc.UpdateUrl(baseUrl ?? html);
        proc.NavigateToHtml(html, baseUrl);
    }

    /// <summary>Hand a tab's ownership to its worker thread (creates the worker if needed).</summary>
    public void HandOffToWorker(int tabIndex, TabOwnership ownership, string url = "")
    {
        var proc = GetOrCreate(tabIndex, url);
        proc?.HandOffToWorker(ownership);
    }

    /// <summary>Ask a tab's worker to return ownership; callback runs on the UI thread.</summary>
    public void RequestReturnToUi(int tabIndex, Action<TabOwnership?, DisplayList?> onReturned)
    {
        var proc = GetProcess(tabIndex);
        if (proc == null || !proc.IsAlive || !proc.OwnershipOnWorker)
        {
            onReturned(null, null);
            return;
        }
        proc.RequestReturnToUi(onReturned);
    }

    /// <summary>Update viewport for all background tabs (active tab is laid out by the UI thread).</summary>
    public void SetViewportAll(float width, float height)
    {
        foreach (var kv in _processes)
        {
            kv.Value.SetViewport(width, height);
            if (kv.Value.OwnershipOnWorker)
                kv.Value.InvalidateLayout();
        }
    }

    public void Dispose()
    {
        DestroyAll();
    }
}
