using Acrux.Core.Performance;

namespace Acrux.Core.Performance.Scheduling;

/// <summary>
/// Detects long-running synchronous tasks by observing their wall-clock duration.
/// "Long task" follows the W3C PerformanceObserver longtask definition: any task
/// whose duration exceeds 50 ms is reported so that the UI can shed weight.
/// </summary>
public sealed class LongTaskObserver
{
    public const long DefaultThresholdMicros = 50_000; // 50 ms

    public readonly struct LongTaskEntry
    {
        public required string Name { get; init; }
        public required long StartNanos { get; init; }
        public required long DurationNanos { get; init; }
        public required TaskPriority Priority { get; init; }
    }

    public sealed class Options
    {
        public long ThresholdMicros { get; init; } = DefaultThresholdMicros;
        public int MaxEntries { get; init; } = 128;
        public bool NotifyOnEveryTask { get; init; } = false;
    }

    private readonly Options _options;
    private readonly LinkedList<LongTaskEntry> _entries = new();
    private readonly object _lock = new();
    private long _observed;

    public long Observed => Interlocked.Read(ref _observed);
    public int Capacity => _options.MaxEntries;
    public long ThresholdMicros => _options.ThresholdMicros;

    public event Action<LongTaskEntry>? OnLongTask;

    public LongTaskObserver(Options? options = null)
    {
        _options = options ?? new Options();
    }

    /// <summary>
    /// Run <paramref name="body"/> and report the task if it exceeds the threshold.
    /// The wall clock is measured even when no long task is reported, so callers can
    /// use this in tight loops without branching.
    /// </summary>
    public void Observe(string name, TaskPriority priority, Action body)
    {
        var start = Clock.NowNanos();
        try { body(); }
        finally
        {
            var duration = Clock.NowNanos() - start;
            if (duration >= _options.ThresholdMicros * 1_000L)
            {
                Interlocked.Increment(ref _observed);
                var entry = new LongTaskEntry
                {
                    Name = name,
                    StartNanos = start,
                    DurationNanos = duration,
                    Priority = priority,
                };
                Record(entry);
                OnLongTask?.Invoke(entry);
            }
        }
    }

    /// <summary>Snapshot of currently retained long-task entries.</summary>
    public IReadOnlyList<LongTaskEntry> Snapshot()
    {
        lock (_lock)
        {
            var result = new LongTaskEntry[_entries.Count];
            int i = 0;
            foreach (var e in _entries) result[i++] = e;
            return result;
        }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        Interlocked.Exchange(ref _observed, 0);
    }

    private void Record(LongTaskEntry entry)
    {
        lock (_lock)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > _options.MaxEntries)
                _entries.RemoveLast();
        }
    }
}
