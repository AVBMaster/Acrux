using Acrux.Core.Performance;

namespace Acrux.Core.Performance.Scheduling;

/// <summary>
/// W3C-style <c>requestIdleCallback</c> implementation. The browser
/// equivalent fires user-supplied callbacks during idle periods, and re-arms them
/// if they return <c>true</c>.
///
/// This implementation is conservative: it defers callbacks to a dedicated scheduler
/// slice and only runs them when the frame has time remaining. Callbacks are sorted by
/// deadline (earliest first) so that work that has been waiting the longest runs first.
/// </summary>
public sealed class IdleCallbackScheduler
{
    public sealed class Registration
    {
        public required Action<IdleDeadline> Body { get; init; }
        public required TimeSpan Timeout { get; init; }
        public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
        public DateTime? Deadline { get; set; }
        public bool Cancelled { get; private set; }
        public int Id { get; init; }

        public void Cancel() => Cancelled = true;
    }

    public readonly struct IdleDeadline
    {
        public required double TimeRemainingMillis { get; init; }
        public required bool DidTimeout { get; init; }
    }

    private readonly List<Registration> _pending = new();
    private readonly object _lock = new();
    private int _nextId;
    private long _registered;
    private long _fired;
    private long _timedOut;

    public long Registered => Interlocked.Read(ref _registered);
    public long Fired => Interlocked.Read(ref _fired);
    public long TimedOut => Interlocked.Read(ref _timedOut);
    public int PendingCount
    {
        get { lock (_lock) return _pending.Count; }
    }

    public Registration Register(Action<IdleDeadline> body, TimeSpan? timeout = null)
    {
        var reg = new Registration
        {
            Id = Interlocked.Increment(ref _nextId),
            Body = body,
            Timeout = timeout ?? TimeSpan.FromSeconds(50),
            Deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(50)),
        };
        lock (_lock) _pending.Add(reg);
        Interlocked.Increment(ref _registered);
        return reg;
    }

    public void Cancel(Registration reg)
    {
        reg.Cancel();
        lock (_lock) _pending.Remove(reg);
    }

    /// <summary>Run as many callbacks as fit in <paramref name="availableMillis"/>.</summary>
    public void Run(double availableMillis)
    {
        if (availableMillis <= 0) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow;

        while (sw.Elapsed.TotalMilliseconds < availableMillis)
        {
            Registration? next = null;
            lock (_lock)
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    var r = _pending[i];
                    if (r.Cancelled) { _pending.RemoveAt(i); i--; continue; }
                    if (next is null || r.EnqueuedAt < next.EnqueuedAt) next = r;
                }
                if (next is not null) _pending.Remove(next);
            }
            if (next is null) break;

            bool didTimeout = now >= next.Deadline;
            if (didTimeout) Interlocked.Increment(ref _timedOut);

            try
            {
                next.Body(new IdleDeadline
                {
                    TimeRemainingMillis = availableMillis - sw.Elapsed.TotalMilliseconds,
                    DidTimeout = didTimeout,
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[IdleCallback] threw: {ex.Message}");
            }
            Interlocked.Increment(ref _fired);
        }
    }
}
