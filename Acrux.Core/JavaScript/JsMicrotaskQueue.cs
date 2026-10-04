using System.Collections.Concurrent;

namespace Acrux.Core.JavaScript;

public class JsMicrotaskQueue
{
    private readonly ConcurrentQueue<Func<Task>> _microtasks = new();
    private readonly ConcurrentQueue<Action> _syncMicrotasks = new();
    private readonly BrowserJsEngineFacade _facade;
    private bool _processing;

    public int PendingCount => _syncMicrotasks.Count + _microtasks.Count;

    public JsMicrotaskQueue(BrowserJsEngineFacade facade)
    {
        _facade = facade;
    }

    public void EnqueueMicrotask(Action task)
    {
        _syncMicrotasks.Enqueue(task);
    }

    public void EnqueueMicrotask(Func<Task> task)
    {
        _microtasks.Enqueue(task);
    }

    public void EnqueuePromiseMicrotask(int resolveCallbackId, object? value = null)
    {
        EnqueueMicrotask(() =>
        {
            try
            {
                _facade.InvokeJsFunction(resolveCallbackId, value);
            }
            catch { }
        });
    }

    public void DrainMicrotasks()
    {
        if (_processing) return;
        _processing = true;

        try
        {
            int iterations = 0;
            const int maxIterations = 1000;

            while (iterations < maxIterations)
            {
                bool executed = false;

                while (_syncMicrotasks.TryDequeue(out var task))
                {
                    try { task(); }
                    catch { }
                    executed = true;
                }

                while (_microtasks.TryDequeue(out var asyncTask))
                {
                    try
                    {
                        asyncTask().GetAwaiter().GetResult();
                    }
                    catch { }
                    executed = true;
                }

                if (!executed) break;
                iterations++;
            }

            if (iterations >= maxIterations)
                Console.WriteLine("[Microtask] Warning: exceeded max microtask iterations");
        }
        finally
        {
            _processing = false;
        }
    }

    public void Clear()
    {
        while (_syncMicrotasks.TryDequeue(out _)) { }
        while (_microtasks.TryDequeue(out _)) { }
    }
}
