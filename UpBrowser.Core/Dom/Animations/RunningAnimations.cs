namespace UpBrowser.Core.Dom.Animations;

/// <summary>
/// A running CSS animation (CSS Animations 1 §3).
///
/// One instance corresponds to one <c>animation-name</c> entry on one element.
/// The engine keeps it alive across frames; its <see cref="StartTimeMs"/> is the
/// timeline time at which the animation was created, so a negative
/// <c>animation-delay</c> puts it straight into its active phase — which is also
/// what makes a paused, negatively-delayed animation a deterministic frame.
/// </summary>
public sealed class CssAnimation
{
    /// <summary>Stable identity, exposed to script as <c>animationName</c>.</summary>
    public string Name { get; }

    /// <summary>Identity of this animation occurrence (index in the name list).</summary>
    public string Id { get; }

    public CssKeyframes Keyframes { get; internal set; }

    public AnimationTiming Timing { get; internal set; }

    /// <summary>Timeline time the animation was created at.</summary>
    public double StartTimeMs { get; internal set; }

    /// <summary>
    /// Local time the effect is held at while <c>animation-play-state: paused</c>.
    /// Null while running. A paused effect freezes the timeline rather than
    /// following it, so a <c>paused</c> animation paired with a negative delay
    /// presents a fixed frame - the standard way to inspect an exact instant.
    /// </summary>
    public double? HoldTimeMs { get; internal set; }

    /// <summary>Index in the element's <c>animation-name</c> list.</summary>
    public int Index { get; internal set; }

    /// <summary>Set while the effect should hold a fill value; false when finished.</summary>
    public bool IsFinished { get; internal set; }

    /// <summary>Set when the animation has been removed by a cascade change.</summary>
    public bool IsCancelled { get; internal set; }

    /// <summary>Iteration index reached on the previous frame, for animationiteration.</summary>
    public double LastIteration { get; internal set; } = -1;

    /// <summary>True once the start event has been queued for this frame.</summary>
    public bool StartEventSent { get; internal set; }

    /// <summary>Iteration indices already reported through animationiteration.</summary>
    public HashSet<double> ReportedIterations { get; } = new();

    public CssAnimation(string name, CssKeyframes keyframes, int index)
    {
        Name = name;
        Keyframes = keyframes;
        Index = index;
        Id = name;
    }

    /// <summary>Timeline time relative to this animation's start.</summary>
    public double LocalTime(double timelineTimeMs) =>
        HoldTimeMs ?? (timelineTimeMs - StartTimeMs);

    public bool IsRunning =>
        !IsFinished && !IsCancelled &&
        HoldTimeMs == null && Timing.PlayState == AnimationPlayState.Running;

    public override string ToString() =>
        $"animation {Name} start={StartTimeMs:F1} {Timing}";
}

/// <summary>
/// A running CSS transition (CSS Transitions 1 §2).
///
/// A transition is created when a property's computed value changes, capturing
/// the before-change and after-change values, and is removed when it reaches its
/// end time.
/// </summary>
public sealed class CssTransition
{
    public string Property { get; }

    public string FromValue { get; }

    public string ToValue { get; }

    /// <summary>Timeline time the transition started (may precede the current time).</summary>
    public double StartTimeMs { get; }

    public double DurationMs { get; }

    public AnimationTimingFunction Easing { get; }

    public bool IsFinished { get; set; }

    public bool IsCancelled { get; set; }

    /// <summary>True once transitionrun has been queued.</summary>
    public bool RunEventSent { get; set; }

    /// <summary>True once transitionstart has been queued.</summary>
    public bool StartEventSent { get; set; }

    public CssTransition(string property, string fromValue, string toValue, double startTimeMs,
        double durationMs, AnimationTimingFunction easing)
    {
        Property = property;
        FromValue = fromValue;
        ToValue = toValue;
        StartTimeMs = startTimeMs;
        DurationMs = durationMs;
        Easing = easing;
    }

    /// <summary>Progress through the transition, 0..1, easing already applied.</summary>
    public double ProgressAt(double timelineTimeMs)
    {
        if (DurationMs <= 0) return 1;
        double elapsed = timelineTimeMs - StartTimeMs;
        if (elapsed <= 0) return 0;
        if (elapsed >= DurationMs) return 1;
        return Easing.Apply(elapsed / DurationMs);
    }

    public bool HasStarted(double timelineTimeMs) => timelineTimeMs >= StartTimeMs;

    public double ElapsedTimeAt(double timelineTimeMs) => Math.Max(0, timelineTimeMs - StartTimeMs);

    public override string ToString() =>
        $"transition {Property} {FromValue} -> {ToValue} in {DurationMs:F1}ms";
}
