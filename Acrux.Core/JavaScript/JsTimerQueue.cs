using System.Collections.Concurrent;

namespace Acrux.Core.JavaScript;

public class JsTimerTask
{
    public int TimerId { get; set; }
    public int CallbackId { get; set; }
    public long DueTime { get; set; }
    public int Interval { get; set; }
    public int NestingLevel { get; set; }
    public bool IsInterval => Interval > 0;
}

public class JsTimerQueue
{
    private readonly BrowserJsEngineFacade _facade;
    private readonly ConcurrentDictionary<int, JsTimerTask> _timers = new();
    private readonly object _lock = new();
    private int _nextTimerId = 1;
    private int _maxNestingLevel;

    private const int MinDelay = 1;
    private const int MinNestedDelay = 4;
    private const int MaxNestedDelay = 4;
    private const int InactiveTabThrottleMs = 1000;

    public int TimerCount => _timers.Count;
    public int MaxNestingLevel => _maxNestingLevel;

    public JsTimerQueue(BrowserJsEngineFacade facade)
    {
        _facade = facade;
    }

    public int SetTimeout(int callbackId, int delayMs, int nestingLevel = 0)
    {
        delayMs = Math.Max(GetEffectiveMinDelay(nestingLevel), delayMs);
        var task = new JsTimerTask
        {
            TimerId = Interlocked.Increment(ref _nextTimerId),
            CallbackId = callbackId,
            DueTime = Environment.TickCount64 + delayMs,
            Interval = 0,
            NestingLevel = nestingLevel
        };
        _timers[task.TimerId] = task;
        return task.TimerId;
    }

    public int SetInterval(int callbackId, int intervalMs, int nestingLevel = 0)
    {
        intervalMs = Math.Max(GetEffectiveMinDelay(nestingLevel), intervalMs);
        var task = new JsTimerTask
        {
            TimerId = Interlocked.Increment(ref _nextTimerId),
            CallbackId = callbackId,
            DueTime = Environment.TickCount64 + intervalMs,
            Interval = intervalMs,
            NestingLevel = nestingLevel
        };
        _timers[task.TimerId] = task;
        return task.TimerId;
    }

    public void ClearTimer(int timerId)
    {
        if (_timers.TryRemove(timerId, out var task))
        {
            _facade.RemoveJsFunction(task.CallbackId);
        }
    }

    public void ProcessTimers()
    {
        var now = Environment.TickCount64;
        var dueTimers = new List<JsTimerTask>();

        foreach (var kvp in _timers)
        {
            if (now >= kvp.Value.DueTime)
                dueTimers.Add(kvp.Value);
        }

        foreach (var task in dueTimers)
        {
            if (!_timers.TryGetValue(task.TimerId, out _))
                continue;

            if (task.IsInterval)
            {
                task.DueTime = now + Math.Max(GetEffectiveMinDelay(task.NestingLevel), task.Interval);
            }
            else
            {
                _timers.TryRemove(task.TimerId, out _);
            }

            var newNesting = Math.Min(task.NestingLevel + 1, 10);
            if (newNesting > _maxNestingLevel)
                _maxNestingLevel = newNesting;

            try
            {
                _facade.InvokeJsFunction(task.CallbackId);
            }
            catch { }
        }
    }

    public void ThrottleInactiveTabs()
    {
        var now = Environment.TickCount64;
        foreach (var kvp in _timers)
        {
            if (kvp.Value.DueTime - now < InactiveTabThrottleMs)
                kvp.Value.DueTime = now + InactiveTabThrottleMs;
        }
    }

    /// <summary>
    /// Milliseconds until the earliest pending timer fires (0 = already due).
    /// Returns int.MaxValue when no timers exist, so hosts can sleep until
    /// the next real wake-up instead of polling.
    /// </summary>
    public int NextTimerDelayMs()
    {
        var now = Environment.TickCount64;
        long best = long.MaxValue;
        foreach (var kvp in _timers)
        {
            long due = kvp.Value.DueTime - now;
            if (due < best) best = due;
        }
        return best == long.MaxValue ? int.MaxValue : (int)Math.Max(0, best);
    }

    public void ClearAll()
    {
        foreach (var kvp in _timers)
        {
            _facade.RemoveJsFunction(kvp.Value.CallbackId);
        }
        _timers.Clear();
        _maxNestingLevel = 0;
    }

    private int GetEffectiveMinDelay(int nestingLevel)
    {
        return nestingLevel >= 5 ? MinNestedDelay : MinDelay;
    }
}
