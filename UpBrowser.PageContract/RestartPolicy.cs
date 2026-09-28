namespace UpBrowser.PageContract;

/// <summary>Why a page host stopped, as the shell judged it.</summary>
public enum CrashKind
{
    /// <summary>The shell asked it to close, or it exited cleanly after <c>Close</c>.</summary>
    CleanExit,
    /// <summary>It reported a fault before dying (managed exception, caught and reported).</summary>
    Faulted,
    /// <summary>It vanished without a report: access violation, OOM kill, external kill.</summary>
    KilledOrCrashedNative,
    /// <summary>The watchdog killed a host that had stopped beating. Deliberate, so it does
    /// not count against the crash budget — the page may simply be doing heavy work.</summary>
    HangKilled,
}

/// <summary>What the shell should do about one host death.</summary>
public readonly record struct RestartVerdict(bool ShouldRestart, int DelayMs, bool Exhausted);

/// <summary>
/// Sliding-window budget for relaunching a page host: a page that dies repeatedly gets
/// spaced-out retries and then stops, instead of spinning the browser in a relaunch loop.
/// A hang kill is not counted, and an explicit user reload refills the budget.
/// </summary>
public sealed class RestartPolicy
{
    public const int WindowMs = 30_000;
    public const int MaxFaultsPerWindow = 3;

    private static readonly int[] BackoffMs = [0, 1_000, 4_000];

    private readonly List<long> _faultTicks = new();
    private long _readyAtTick;

    /// <summary>True once the budget is spent; a reload resets it.</summary>
    public bool Exhausted { get; private set; }

    /// <summary>Count of faults currently inside the window.</summary>
    public int FaultCount => _faultTicks.Count;

    public RestartVerdict OnDeath(CrashKind kind, long nowTick)
    {
        if (kind == CrashKind.CleanExit)
            return new RestartVerdict(false, 0, Exhausted);

        Prune(nowTick);

        // A watchdog kill is our own doing: give the page another shot immediately and do
        // not charge the user's patience for it.
        if (kind == CrashKind.HangKilled)
            return new RestartVerdict(true, 0, Exhausted);

        _faultTicks.Add(nowTick);
        if (_faultTicks.Count > MaxFaultsPerWindow)
        {
            Exhausted = true;
            return new RestartVerdict(false, 0, true);
        }

        int delay = BackoffMs[Math.Min(_faultTicks.Count - 1, BackoffMs.Length - 1)];
        _readyAtTick = nowTick + delay;
        return new RestartVerdict(true, delay, false);
    }

    /// <summary>False while the backoff for the last crash has not elapsed.</summary>
    public bool ReadyToRelaunch(long nowTick) => nowTick >= _readyAtTick;

    /// <summary>The user asked for this page again: forget the history.</summary>
    public void Reset()
    {
        _faultTicks.Clear();
        Exhausted = false;
        _readyAtTick = 0;
    }

    private void Prune(long nowTick)
    {
        _faultTicks.RemoveAll(t => nowTick - t > WindowMs);
    }
}
