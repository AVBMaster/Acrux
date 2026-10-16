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
            const int maxIterations = 1000;

            // DOM §4.5.6.1 puts the queued mutation-observer callbacks at the same checkpoint the
            // promise jobs run at, so the queue is drained, the observers are given their records,
            // and a callback that queued more work is followed by another round of the same.
            for (int round = 0; round < 4; round++)
            {
                var iterations = DrainOnce(maxIterations);
                if (iterations >= maxIterations)
                {
                    Console.WriteLine("[Microtask] Warning: exceeded max microtask iterations");
                    break;
                }
                RunDomCheckpoint();
                if (PendingCount == 0) break;
            }
        }
        finally
        {
            _processing = false;
        }
    }

    private void RunDomCheckpoint()
    {
        try { _facade.CallFunction("__domCheckpoint"); }
        catch { }
    }

    private int DrainOnce(int maxIterations)
    {
        int iterations = 0;

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

        return iterations;
    }

    public void Clear()
    {
        while (_syncMicrotasks.TryDequeue(out _)) { }
        while (_microtasks.TryDequeue(out _)) { }
    }
}
