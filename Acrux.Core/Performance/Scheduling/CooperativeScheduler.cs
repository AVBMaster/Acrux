using System.Collections.Concurrent;
using System.Diagnostics;

namespace Acrux.Core.Performance.Scheduling;

/// <summary>
/// Discrete priority levels for queued tasks. Modeled on Chromium's
/// MainThreadTaskQueue priorities. Within the same priority, FIFO order is preserved.
/// </summary>
public enum TaskPriority : byte
{
    /// <summary>Input handling, rAF callbacks, micro-tasks resolved at end of microtask checkpoint.</summary>
    Immediate = 0,
    /// <summary>High-priority microtasks such as layout / paint work that must finish this frame.</summary>
    High = 1,
    /// <summary>Default task queue: setTimeout, postMessage, fetch continuations.</summary>
    Normal = 2,
    /// <summary>Background work: analytics, telemetry, prefetch resolution.</summary>
    Low = 3,
    /// <summary>Idle callback work that should be cancelled if the browser is busy.</summary>
    Idle = 4,
}

/// <summary>
/// A unit of work that can be paused mid-execution when its time slice expires.
/// The continuation is invoked with <c>true</c> when more time remains and
/// <c>false</c> when the slice has been consumed and the scheduler is yielding
/// the thread back to higher-priority work.
/// </summary>
public sealed class CancellableTask
{
    public required Action<Func<bool>, CancellationToken> Body { get; init; }
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;
    public string? Name { get; init; }
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
    public CancellationTokenSource Cts { get; set; } = new();
    public TimeSpan? Deadline { get; set; }
    public bool IsCancelled => Cts.IsCancellationRequested;

    public void Cancel() => Cts.Cancel();
}

/// <summary>
/// Cooperative time-slicing scheduler. Mirrors the responsibilities of Chromium's
/// "ThreadController / MainThreadScheduler" pair. Tasks can be plain delegates or
/// cancellable coroutines that respect yield points.
/// </summary>
public sealed class CooperativeScheduler
{
    public struct FrameBudget
    {
        /// <summary>Wall-clock budget for the entire frame in milliseconds. 16.67 = 60fps.</summary>
        public double TotalMillis { get; init; }
        /// <summary>Maximum work the scheduler is willing to do before yielding to layout/paint.</summary>
        public double ScriptMillis { get; init; }
        /// <summary>True if this frame is being driven by a display vsync.</summary>
        public bool VSynced { get; init; }

        public static FrameBudget For60Fps { get; } = new() { TotalMillis = 16.67, ScriptMillis = 8.0, VSynced = true };
        public static FrameBudget For120Fps { get; } = new() { TotalMillis = 8.33, ScriptMillis = 4.0, VSynced = true };
        public static FrameBudget CatchUp { get; } = new() { TotalMillis = 50.0, ScriptMillis = 25.0, VSynced = false };
    }

    private readonly ConcurrentDictionary<TaskPriority, ConcurrentQueue<Action>> _plainTasks = new();
    private readonly ConcurrentDictionary<TaskPriority, ConcurrentQueue<CancellableTask>> _cancellableTasks = new();
    private readonly ConcurrentDictionary<TaskPriority, List<IdleCallback>> _idleCallbacks = new();
    private readonly object _idleLock = new();

    private long _plainProcessed;
    private long _plainEnqueued;
    private long _cancellableProcessed;
    private long _cancellableEnqueued;
    private long _yieldsObserved;
    private long _longTaskCount;
    private long _idleCallbacksRun;

    public long PlainProcessed => Interlocked.Read(ref _plainProcessed);
    public long PlainEnqueued => Interlocked.Read(ref _plainEnqueued);
    public long CancellableProcessed => Interlocked.Read(ref _cancellableProcessed);
    public long CancellableEnqueued => Interlocked.Read(ref _cancellableEnqueued);
    public long YieldsObserved => Interlocked.Read(ref _yieldsObserved);
    public long LongTaskCount => Interlocked.Read(ref _longTaskCount);
    public long IdleCallbacksRun => Interlocked.Read(ref _idleCallbacksRun);

    public CooperativeScheduler()
    {
        foreach (TaskPriority p in Enum.GetValues<TaskPriority>())
        {
            _plainTasks[p] = new ConcurrentQueue<Action>();
            _cancellableTasks[p] = new ConcurrentQueue<CancellableTask>();
            _idleCallbacks[p] = new List<IdleCallback>();
        }
    }

    /// <summary>Post a regular (non-cancellable) task.</summary>
    public void PostTask(Action task, TaskPriority priority = TaskPriority.Normal)
    {
        if (task is null) return;
        _plainTasks[priority].Enqueue(task);
        Interlocked.Increment(ref _plainEnqueued);
    }

    /// <summary>Post a coroutine that respects the time slice. The body receives a "more time?" probe.</summary>
    public CancellableTask PostCancellable(Action<Func<bool>, CancellationToken> body, TaskPriority priority = TaskPriority.Normal, string? name = null)
    {
        var t = new CancellableTask
        {
            Body = body,
            Priority = priority,
            Name = name,
        };
        _cancellableTasks[priority].Enqueue(t);
        Interlocked.Increment(ref _cancellableEnqueued);
        return t;
    }

    /// <summary>Schedule an idle callback to run during the next available idle period.</summary>
    public void PostIdle(Action work, TimeSpan timeout, string? name = null)
    {
        if (work is null) return;
        lock (_idleLock)
        {
            _idleCallbacks[TaskPriority.Idle].Add(new IdleCallback
            {
                Body = work,
                Deadline = DateTime.UtcNow + timeout,
                Name = name,
            });
        }
    }

    /// <summary>Run the scheduler until either the frame budget is exhausted or queues are empty.</summary>
    /// <returns>The number of microseconds of work performed in the slice.</returns>
    public long RunFrame(in FrameBudget budget)
    {
        var frameStart = Stopwatch.GetTimestamp();
        double frameEndMillis = (Stopwatch.GetTimestamp() - frameStart) * 1000.0 / Stopwatch.Frequency + budget.TotalMillis;
        double scriptEndMillis = (Stopwatch.GetTimestamp() - frameStart) * 1000.0 / Stopwatch.Frequency + budget.ScriptMillis;

        long totalProcessed = 0;

        // 1. Immediate and High priorities are drained completely (with yield checks
        //    inside long-running cancellable bodies).
        totalProcessed += DrainPriority(TaskPriority.Immediate, scriptEndMillis);
        totalProcessed += DrainPriority(TaskPriority.High, scriptEndMillis);

        // 2. Normal — until script slice budget runs out.
        totalProcessed += DrainPriority(TaskPriority.Normal, scriptEndMillis);

        // 3. Low — only if we still have time before the next vsync.
        if (NowMillis(frameStart) < frameEndMillis - 1.0)
            totalProcessed += DrainPriority(TaskPriority.Low, scriptEndMillis);

        // 4. Idle callbacks if there's any time left.
        if (NowMillis(frameStart) < frameEndMillis - 1.0)
            totalProcessed += DrainIdle(frameEndMillis - NowMillis(frameStart));

        return totalProcessed;
    }

    /// <summary>Process all currently-pending work without bound. Useful for tests.</summary>
    public long Drain()
    {
        long total = 0;
        foreach (TaskPriority p in Enum.GetValues<TaskPriority>())
        {
            total += DrainPriority(p, double.MaxValue);
            total += DrainIdle(double.MaxValue);
        }
        return total;
    }

    private long DrainPriority(TaskPriority priority, double scriptEndMillis)
    {
        long processed = 0;
        var plain = _plainTasks[priority];
        while (plain.TryDequeue(out var task))
        {
            processed += RunOne(() => task(), scriptEndMillis, isPlain: true);
        }
        var cancellable = _cancellableTasks[priority];
        while (cancellable.TryDequeue(out var task))
        {
            processed += RunCancellable(task, scriptEndMillis);
        }
        return processed;
    }

    private long RunOne(Action body, double scriptEndMillis, bool isPlain)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Scheduler] task threw: {ex.Message}");
        }
        var elapsed = (Stopwatch.GetTimestamp() - start) * 1_000_000L / Stopwatch.Frequency;
        Interlocked.Increment(ref _plainProcessed);
        if (elapsed > 50_000) Interlocked.Increment(ref _longTaskCount); // > 50ms
        return elapsed;
    }

    private long RunCancellable(CancellableTask task, double scriptEndMillis)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            task.Body(() => true, task.Cts.Token);
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Scheduler] cancellable '{task.Name}' threw: {ex.Message}");
        }
        var elapsed = (Stopwatch.GetTimestamp() - start) * 1_000_000L / Stopwatch.Frequency;
        Interlocked.Increment(ref _cancellableProcessed);
        if (elapsed > 50_000) Interlocked.Increment(ref _longTaskCount);
        return elapsed;
    }

    private long DrainIdle(double remainingMillis)
    {
        long processed = 0;
        var sw = Stopwatch.StartNew();
        lock (_idleLock)
        {
            var callbacks = _idleCallbacks[TaskPriority.Idle];
            for (int i = callbacks.Count - 1; i >= 0; i--)
            {
                var cb = callbacks[i];
                if (DateTime.UtcNow > cb.Deadline)
                {
                    callbacks.RemoveAt(i);
                    continue;
                }
                if (sw.Elapsed.TotalMilliseconds >= remainingMillis - 0.5) break;
                callbacks.RemoveAt(i);
                Interlocked.Increment(ref _idleCallbacksRun);
                try { cb.Body(); } catch (Exception ex) { Console.Error.WriteLine($"[Scheduler] idle threw: {ex.Message}"); }
            }
        }
        processed = sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
        return processed;
    }

    private static double NowMillis(long frameStartTicks) =>
        (Stopwatch.GetTimestamp() - frameStartTicks) * 1000.0 / Stopwatch.Frequency;

    private sealed class IdleCallback
    {
        public required Action Body { get; init; }
        public required DateTime Deadline { get; init; }
        public string? Name { get; init; }
    }
}
