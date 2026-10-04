using System.Collections.Concurrent;

namespace Acrux.Core.EventLoop;

public class EventLoop
{
    private readonly ConcurrentQueue<Action> _taskQueue = new();
    private readonly List<Action> _timers = new();
    private readonly object _timersLock = new();
    private bool _running;

    public bool IsRunning => _running;

    public event Action? OnBeforeTask;
    public event Action? OnAfterTask;

    public void Start()
    {
        _running = true;
    }

    public void Stop()
    {
        _running = false;
    }

    public void PostTask(Action task)
    {
        _taskQueue.Enqueue(task);
    }

    public void SetTimeout(Action callback, int delayMs)
    {
        lock (_timersLock)
        {
            _timers.Add(() =>
            {
                Task.Delay(delayMs).ContinueWith(_ => PostTask(callback));
            });
        }
    }

    public void ProcessTasks()
    {
        OnBeforeTask?.Invoke();

        while (_taskQueue.TryDequeue(out var task))
        {
            try
            {
                task();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EventLoop error: {ex.Message}");
            }
        }

        lock (_timersLock)
        {
            foreach (var timer in _timers)
            {
                timer();
            }
            _timers.Clear();
        }

        OnAfterTask?.Invoke();
    }

    public async Task RunAsync()
    {
        Start();
        while (_running)
        {
            ProcessTasks();
            await Task.Delay(1);
        }
    }
}

public class TaskQueue
{
    private readonly ConcurrentQueue<Action> _queue = new();

    public void Enqueue(Action task)
    {
        _queue.Enqueue(task);
    }

    public bool TryDequeue(out Action? task)
    {
        return _queue.TryDequeue(out task);
    }

    public bool IsEmpty => _queue.IsEmpty;

    public int Count => _queue.Count;
}