namespace Acrux.Core.Dom.Animations;

public enum AnimationFillMode
{
    /// <summary>No effect outside the active phase.</summary>
    None,
    /// <summary>Retain the last computed value after the animation ends.</summary>
    Forwards,
    /// <summary>Apply the first value during the delay phase.</summary>
    Backwards,
    /// <summary>Apply first/last values outside the active phase.</summary>
    Both,
}

public enum AnimationPlayDirection
{
    Normal,
    Reverse,
    Alternate,
    AlternateReverse,
}

public enum AnimationPlayState
{
    Running,
    Paused,
}

/// <summary>Where a given local time falls in an effect's lifetime (Web Animations §4.4.4).</summary>
public enum AnimationPhase
{
    /// <summary>Before the effect's start (or no effect at all).</summary>
    Before,
    /// <summary>Inside the delay, with fill applying the start value.</summary>
    Delay,
    /// <summary>Inside one or more iterations.</summary>
    Active,
    /// <summary>After the last iteration, with fill applying the end value.</summary>
    After,
}

/// <summary>
/// The timing model of one animation or transition (Web Animations §4.5,
/// CSS Animations 1 §3). Everything the engine needs to turn a local time into
/// an iteration progress and a phase lives here, so animations and transitions
/// share exactly the same timing behaviour.
/// </summary>
public readonly struct AnimationTiming : IEquatable<AnimationTiming>
{
    /// <summary>Time before the active phase, in ms. May be negative (see Easing 1 §3.1).</summary>
    public readonly double Delay;

    /// <summary>Time after the active phase, in ms.</summary>
    public readonly double EndDelay;

    /// <summary>Length of a single iteration, in ms.</summary>
    public readonly double Duration;

    /// <summary>Offset into the first iteration, in iterations. Defaults to 0.</summary>
    public readonly double IterationStart;

    /// <summary>Number of iterations; may be <see cref="double.PositiveInfinity"/>.</summary>
    public readonly double IterationCount;

    public readonly AnimationFillMode FillMode;
    public readonly AnimationPlayDirection Direction;
    public readonly AnimationPlayState PlayState;
    public readonly AnimationTimingFunction Easing;

    public AnimationTiming(
        double delay,
        double duration,
        double iterationCount,
        AnimationFillMode fillMode,
        AnimationPlayDirection direction,
        AnimationPlayState playState,
        AnimationTimingFunction easing,
        double endDelay = 0,
        double iterationStart = 0)
    {
        Delay = delay;
        Duration = duration;
        IterationCount = iterationCount;
        FillMode = fillMode;
        Direction = direction;
        PlayState = playState;
        Easing = easing;
        EndDelay = endDelay;
        IterationStart = iterationStart;
    }

    public static AnimationTiming Default(double duration, double delay = 0) => new(
        delay, duration, 1,
        AnimationFillMode.None, AnimationPlayDirection.Normal,
        AnimationPlayState.Running, AnimationTimingFunction.Ease);

    /// <summary>Total time spent iterating, or infinity for <c>infinite</c>.</summary>
    public double ActiveDuration =>
        double.IsPositiveInfinity(IterationCount) || double.IsNaN(IterationCount)
            ? double.PositiveInfinity
            : Duration * IterationCount;

    /// <summary>Time from the effect's start to the end of the active phase.</summary>
    public double EndTime => double.IsPositiveInfinity(ActiveDuration)
        ? double.PositiveInfinity
        : Delay + ActiveDuration + EndDelay;

    /// <summary>
    /// An effect with no iterations never reaches its active phase. A
    /// <em>zero-duration</em> effect still does have one — it is simply over the
    /// instant it starts, which is why a <c>0s</c> animation with
    /// <c>fill: forwards</c> correctly presents its last keyframe.
    /// </summary>
    public bool HasIterations => IterationCount > 0 && !double.IsNaN(IterationCount);

    /// <summary>True once the effect can no longer produce a value (play-state is ignored here).</summary>
    public bool IsFinished(double localTime) =>
        HasIterations && double.IsFinite(ActiveDuration) && localTime >= EndTime;

    /// <summary>Classify a local time into a phase, applying fill-mode semantics.</summary>
    public AnimationPhase PhaseAt(double localTime)
    {
        if (!HasIterations)
            return localTime >= 0 ? AnimationPhase.After : AnimationPhase.Before;

        // A negative delay puts the start of the active phase before the effect's
        // own start time, so the animation is already active when it begins.
        if (localTime < Delay)
            return FillMode is AnimationFillMode.Backwards or AnimationFillMode.Both
                ? AnimationPhase.Delay
                : AnimationPhase.Before;

        if (localTime < Delay + ActiveDuration)
            return AnimationPhase.Active;

        // Past the active phase. A zero-duration effect lands here immediately,
        // and a forwards/both fill then presents the last keyframe.
        return AnimationPhase.After;
    }

    /// <summary>
    /// The zero-based index of the iteration containing <paramref name="localTime"/>,
    /// clamped to the last iteration once the effect is past its end.
    /// </summary>
    public double CurrentIteration(double localTime)
    {
        if (!HasIterations) return 0;
        double activeTime = localTime - Delay;
        if (activeTime <= 0) return 0;
        if (double.IsPositiveInfinity(ActiveDuration) || activeTime >= ActiveDuration)
            return double.IsPositiveInfinity(IterationCount)
                ? Math.Floor(activeTime / Duration)
                : Math.Max(0, IterationCount - 1);
        return Math.Floor(activeTime / Duration);
    }

    /// <summary>Progress through the current iteration, before easing. 0 &lt;= p &lt; 1.</summary>
    public double IterationProgress(double localTime)
    {
        if (!HasIterations) return 0;
        double activeTime = localTime - Delay;
        if (activeTime <= 0) return 0;

        // An infinite effect never runs out, so the iteration is simply
        // activeTime modulo the duration; only a finite effect can be past its
        // end and hold the last iteration's final value.
        if (double.IsPositiveInfinity(ActiveDuration))
            return Duration > 0 ? (activeTime % Duration) / Duration : 0;

        if (activeTime >= ActiveDuration) return 1;
        if (Duration <= 0) return 1;
        return (activeTime % Duration) / Duration;
    }

    /// <summary>Apply <c>animation-direction</c> to an in-iteration progress.</summary>
    public double TransformedProgress(double localTime, double iterationProgress)
    {
        bool reverse = Direction switch
        {
            AnimationPlayDirection.Reverse => true,
            AnimationPlayDirection.Alternate => IsOddIteration(localTime),
            AnimationPlayDirection.AlternateReverse => !IsOddIteration(localTime),
            _ => false,
        };
        return reverse ? 1.0 - iterationProgress : iterationProgress;
    }

    private bool IsOddIteration(double localTime)
    {
        double iteration = Math.Floor(Math.Max(0, localTime - Delay) / Duration);
        return iteration % 2 == 1;
    }

    /// <summary>
    /// The iteration progress with <c>animation-direction</c> already applied.
    ///
    /// The direction belongs to the progress value, not to the keyframe list:
    /// a <c>reverse</c> animation runs the easing curve backwards, so the shaping
    /// happens on the reversed progress rather than on a mirrored keyframe set.
    /// </summary>
    public double DirectedIterationProgress(double localTime) =>
        TransformedProgress(localTime, IterationProgress(localTime));

    /// <summary>Iteration progress after direction and the per-iteration easing.</summary>
    public double EasedIterationProgress(double localTime)
    {
        if (!HasIterations) return 0;
        if (localTime < Delay)
        {
            // Backwards fill has not started: report the value the animation
            // will present before its delay, which for alternate directions is
            // the far end of the first iteration.
            if (FillMode is not (AnimationFillMode.Backwards or AnimationFillMode.Both))
                return 0;
            return ApplyEasing(DirectionReversesFirst() ? 1.0 : 0.0);
        }
        double p = IterationProgress(localTime);
        if (double.IsPositiveInfinity(ActiveDuration) || localTime >= Delay + ActiveDuration)
        {
            // At/after the end, hold the final iteration's end value.
            p = 1.0;
            if (Direction is AnimationPlayDirection.Reverse or AnimationPlayDirection.AlternateReverse)
            {
                double lastIteration = double.IsPositiveInfinity(IterationCount)
                    ? Math.Floor(Math.Max(0, localTime - Delay) / Duration)
                    : Math.Max(0, IterationCount - 1);
                bool lastIsOdd = lastIteration % 2 == 1;
                bool reverse = Direction == AnimationPlayDirection.Reverse || lastIsOdd;
                p = reverse ? 0.0 : 1.0;
            }
            return ApplyEasing(p);
        }
        return ApplyEasing(TransformedProgress(localTime, p));
    }

    /// <summary>
    /// The overall (transformed, un-eased) progress across the whole effect,
    /// used for <c>elapsedTime</c> in animation events and for transition
    /// percentages.
    /// </summary>
    public double OverallProgress(double localTime)
    {
        if (!HasIterations) return 1;
        if (localTime <= Delay) return 0;
        if (double.IsPositiveInfinity(ActiveDuration) || localTime >= Delay + ActiveDuration) return 1;
        return (localTime - Delay) / ActiveDuration;
    }

    private bool DirectionReversesFirst() =>
        Direction is AnimationPlayDirection.Reverse or AnimationPlayDirection.AlternateReverse;

    /// <summary>
    /// Shape an iteration progress with the effect's easing function.
    ///
    /// The value is clamped, not short-circuited: an easing function is free to
    /// define its own endpoints, and <c>step-start</c> deliberately maps 0 to 1.
    /// A backwards fill samples at iteration progress 0, so a shortcut here would
    /// visibly disagree with the spec.
    /// </summary>
    public double ApplyEasing(double p)
    {
        if (double.IsNaN(p)) return 0;
        if (p < 0) p = 0;
        else if (p > 1) p = 1;
        return Easing.Apply(p);
    }

    /// <summary>
    /// Timing equality drives animation replacement: a CSS animation whose
    /// timing inputs are unchanged keeps running, and one whose inputs changed is
    /// replaced by a fresh occurrence (CSS Animations 1 §4).
    /// </summary>
    public bool Equals(AnimationTiming other) => Equals(other, includePlayState: true);

    /// <summary>
    /// Equality that can ignore <c>animation-play-state</c>. Pausing and resuming
    /// a running animation must not replace it — the occurrence survives and only
    /// its hold time changes — so the engine compares without the play state and
    /// handles that transition separately.
    /// </summary>
    public bool Equals(AnimationTiming other, bool includePlayState) =>
        Delay.Equals(other.Delay)
        && EndDelay.Equals(other.EndDelay)
        && Duration.Equals(other.Duration)
        && IterationStart.Equals(other.IterationStart)
        && IterationCount.Equals(other.IterationCount)
        && FillMode == other.FillMode
        && Direction == other.Direction
        && (includePlayState ? PlayState == other.PlayState : true)
        && Equals(Easing, other.Easing);

    public override bool Equals(object? obj) => obj is AnimationTiming t && Equals(t);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Delay);
        hash.Add(EndDelay);
        hash.Add(Duration);
        hash.Add(IterationStart);
        hash.Add(IterationCount);
        hash.Add((int)FillMode);
        hash.Add((int)Direction);
        hash.Add((int)PlayState);
        hash.Add(Easing?.CssText);
        return hash.ToHashCode();
    }

    public static bool operator ==(AnimationTiming a, AnimationTiming b) => a.Equals(b);

    public static bool operator !=(AnimationTiming a, AnimationTiming b) => !a.Equals(b);

    public override string ToString() =>
        $"{Duration}ms {Delay}ms {IterationCount}x {Easing.CssText} {FillMode} {Direction} {PlayState}";
}
