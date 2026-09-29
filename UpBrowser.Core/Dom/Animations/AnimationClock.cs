using System.Diagnostics;

namespace UpBrowser.Core.Dom.Animations;

/// <summary>
/// Single time source for every animation and transition in the process.
///
/// CSS animations and transitions are functions of a document timeline, so the
/// engine must never read the wall clock directly: a page rendered twice has to
/// produce the same pixels, and the visual-regression harness must be able to
/// ask for the frame at an exact instant. Freezing the clock here gives both.
/// </summary>
public static class AnimationClock
{
    private static readonly long OriginTicks = Stopwatch.GetTimestamp();
    private static double? _frozenMs;

    /// <summary>Milliseconds since process start, from a monotonic stopwatch.</summary>
    public static double WallClockMs => (Stopwatch.GetTimestamp() - OriginTicks) * 1000.0 / Stopwatch.Frequency;

    /// <summary>True while the clock is pinned to <see cref="FrozenMs"/>.</summary>
    public static bool IsFrozen => _frozenMs.HasValue;

    /// <summary>The pinned instant, or null when the clock is running free.</summary>
    public static double? FrozenMs => _frozenMs;

    /// <summary>The instant animation code should use.</summary>
    public static double NowMs => _frozenMs ?? WallClockMs;

    /// <summary>
    /// Pin the clock to <paramref name="milliseconds"/>. Every document timeline,
    /// animation and transition then observes exactly that instant, so a capture
    /// is reproducible regardless of how long the process actually took.
    /// </summary>
    public static void FreezeAt(double milliseconds) => _frozenMs = milliseconds;

    /// <summary>Return to the monotonic wall clock.</summary>
    public static void Unfreeze() => _frozenMs = null;

    /// <summary>
    /// Freeze for the lifetime of the returned scope. Used by the headless
    /// capture path so the freeze can never leak into a later live frame.
    /// </summary>
    public static IDisposable Scope(double milliseconds)
    {
        double? previous = _frozenMs;
        _frozenMs = milliseconds;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly double? _previous;
        private bool _disposed;

        internal RestoreScope(double? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _frozenMs = _previous;
        }
    }
}

/// <summary>
/// The timeline a document's animations and transitions are scheduled on
/// (Web Animations §DocumentTimeline). Its time starts at zero when the document
/// is created, matching the "animation start time = time the animation was
/// created" rule the Web Animations model is built on.
/// </summary>
public sealed class DocumentTimeline
{
    private double _originMs;

    public DocumentTimeline() => _originMs = AnimationClock.NowMs;

    /// <summary>Timeline time in milliseconds; 0 at document creation.</summary>
    public double CurrentTimeMs => AnimationClock.NowMs - _originMs;

    /// <summary>True while the shared clock is frozen (headless capture).</summary>
    public bool IsDeterministic => AnimationClock.IsFrozen;

    /// <summary>
    /// Re-base the timeline so its current time becomes <paramref name="timeMs"/>.
    /// Used when a host swaps in a new document and wants the animation clock to
    /// restart rather than inherit the previous page's elapsed time.
    /// </summary>
    public void SetCurrentTime(double timeMs) => _originMs = AnimationClock.NowMs - timeMs;
}
