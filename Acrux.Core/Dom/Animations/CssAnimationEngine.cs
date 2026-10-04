using Acrux.Core.Css.Resolver;
using Acrux.Core.Css.Rules;

namespace Acrux.Core.Dom.Animations;

/// <summary>What one animation tick changed, so the host can pick the cheapest valid path.</summary>
public readonly struct AnimationUpdateResult
{
    /// <summary>At least one animation or transition is currently in effect.</summary>
    public bool HasActiveAnimations { get; init; }

    /// <summary>A layout-affecting property moved, so boxes must be recomputed.</summary>
    public bool NeedsLayout { get; init; }

    /// <summary>Any painted value moved; the page must be repainted.</summary>
    public bool NeedsRepaint { get; init; }

    /// <summary>Number of elements with at least one running effect.</summary>
    public int AnimatedElements { get; init; }

    /// <summary>Boxes whose animated values were written back this tick. Empty when
    /// nothing was applied. Lets the caller decide whether a repaint can be seen.</summary>
    public IReadOnlyList<Element>? RepaintTargets { get; init; }

    /// <summary>One of this tick's animated properties paints outside the box by an amount
    /// the geometry cannot express (shadow spread, outline offset, blur), so no
    /// box-based culling of the repaint is safe at all.</summary>
    public bool RepaintBleeds { get; init; }

    public static readonly AnimationUpdateResult Idle = new();
}

/// <summary>
/// The CSS animation and transition engine: the "update animations and send
/// events" step of the rendering pipeline (Web Animations �?.8).
///
/// It runs once per frame, after style resolution and before layout, and writes
/// the sampled animated values into each element's
/// <see cref="ComputedStyle"/>. Because the cascade produces a fresh computed
/// style on every pass, writing into it is safe: the next style resolution
/// recomputes the underlying value and the engine re-applies the animation on
/// top, so an animated value can never leak into the base style.
///
/// Two properties of that contract drive the design. The engine must observe the
/// *cascaded* value before it writes anything, because that value is a
/// transition's after-change style. And the host must re-run style resolution
/// before every tick, otherwise the engine would keep re-applying onto its own
/// output.
/// </summary>
public sealed class CssAnimationEngine
{
    private readonly DocumentTimeline _timeline;
    private readonly Dictionary<Element, ElementAnimations> _elements = new();
    private readonly List<QueuedEvent> _pendingEvents = new();
    private readonly HashSet<Element> _visited = new();
    private readonly Dictionary<string, CssKeyframes> _keyframesCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _valueCache = new(StringComparer.Ordinal);
    private readonly List<StyleRuleKeyframes> _rulesScratch = new();

    private double _lastTimeMs = double.NaN;
    private List<Element>? _repaintTargets;
    private bool _repaintBleeds;
    private bool _suppressEvents;

    /// <summary>Receives events queued this tick, after the whole tree is sampled.</summary>
    public Action<Element, Event>? EventSink { get; set; }

    public CssAnimationEngine(DocumentTimeline? timeline = null) =>
        _timeline = timeline ?? new DocumentTimeline();

    public DocumentTimeline Timeline => _timeline;

    /// <summary>Timeline time of the last completed tick.</summary>
    public double CurrentTimeMs => _lastTimeMs;

    /// <summary>Whether the host re-resolved the cascade for the tick in flight.</summary>
    private bool _styleRecomputed = true;

    /// <summary>Number of elements with at least one live effect.</summary>
    public int ActiveElementCount
    {
        get
        {
            int n = 0;
            foreach (var kv in _elements)
                if (kv.Value.LiveEffectCount > 0) n++;
            return n;
        }
    }

    /// <summary>Total live animation + transition count, for diagnostics.</summary>
    public int ActiveEffectCount
    {
        get
        {
            int n = 0;
            foreach (var kv in _elements)
                n += kv.Value.LiveEffectCount;
            return n;
        }
    }

    /// <summary>Total keyframe sets indexed for the last tick.</summary>
    public int IndexedKeyframeSets => _keyframesCache.Count;

    /// <summary>
    /// Advance every effect to the timeline's current time and write the
    /// resulting animated values into the elements' computed styles.
    /// </summary>
    /// <param name="suppressEvents">
    /// Sample without producing events or consuming the one-shot "already
    /// reported" flags. Used by the priming pass, which exists only to record an
    /// element's before-change style before the page's scripts run: a browser
    /// creates CSS animations during the first style resolution but delivers
    /// their events in the same frame as the paint, by which time a listener
    /// registered by a script exists.
    /// </param>
    /// <param name="styleRecomputed">
    /// Whether the host re-resolved the cascade for this frame.
    ///
    /// It decides whether a transition may <em>start</em>. A transition begins
    /// when a computed value differs from the before-change style, so it can only
    /// be discovered on a frame whose styles came from the cascade; comparing
    /// against the previous frame's styles would compare this engine's own output
    /// and invent transitions out of its own animation values. Running effects
    /// are time-driven and advance either way.
    ///
    /// Passing false lets a host skip style resolution on frames where the only
    /// thing that changed is an animation, which on a real page is the single
    /// most expensive thing in the frame.
    /// </param>
    public AnimationUpdateResult Update(
        Element root,
        IReadOnlyList<StyleRuleKeyframes>? keyframeRules,
        bool suppressEvents = false,
        bool styleRecomputed = true)
    {
        if (root == null) return AnimationUpdateResult.Idle;

        double time = _timeline.CurrentTimeMs;
        _lastTimeMs = time;
        _styleRecomputed = styleRecomputed;
        _keyframesCache.Clear();
        _valueCache.Clear();
        _pendingEvents.Clear();
        _suppressEvents = suppressEvents;
        _visited.Clear();

        bool anyActive = false;
        bool needsLayout = false;
        bool needsRepaint = false;
        int animatedElements = 0;
        _repaintTargets = null;
        _repaintBleeds = false;

        Walk(root, keyframeRules, ref anyActive, ref needsLayout, ref needsRepaint, ref animatedElements, time);

        // Forget elements that left the document, then deliver the events.
        PruneDetached();
        FlushEvents();

        return new AnimationUpdateResult
        {
            HasActiveAnimations = anyActive,
            NeedsLayout = needsLayout,
            NeedsRepaint = needsRepaint,
            AnimatedElements = animatedElements,
            RepaintTargets = _repaintTargets ?? (IReadOnlyList<Element>)Array.Empty<Element>(),
            RepaintBleeds = _repaintBleeds,
        };
    }

    private void Walk(Element element, IReadOnlyList<StyleRuleKeyframes>? rules,
        ref bool anyActive, ref bool needsLayout, ref bool needsRepaint, ref int animatedElements, double time)
    {
        var style = element.ComputedStyle;

        if (style != null)
        {
            // The value cache is per element: the same property name means a
            // different value on every box, and a transition's before-change
            // style must be that box's own.
            _valueCache.Clear();

            if (!_elements.TryGetValue(element, out var state))
            {
                state = new ElementAnimations();
                _elements[element] = state;
            }
            _visited.Add(element);

            // 1. Transitions react to a *change* in computed value, so the
            //    before/after comparison has to run on the cascaded style before
            //    any animated value is written back.
            ProcessTransitions(element, style, state, time, ref needsLayout, ref needsRepaint);

            // 2. Reconcile the CSS animation list against what is running. That list is
            //    read from the cascade, so it can only have changed on a pass whose styles
            //    came from the cascade; on any other pass an element with nothing running
            //    has nothing to reconcile. Without the gate every element re-parses seven
            //    animation longhands per frame, which costs more than everything else in
            //    an animation frame put together.
            if (_styleRecomputed || state.Animations.Count > 0 || state.SeenAnimations.Count > 0)
                ProcessAnimations(element, style, state, rules, time);

            // 3. Apply, animations first so transitions win on shared properties.
            if (state.LiveEffectCount > 0)
            {
                animatedElements++;
                anyActive = true;
                needsRepaint = true;
                // Which boxes this tick actually touched, so a host can decide whether
                // the change can reach its visible area at all.
                (_repaintTargets ??= new List<Element>()).Add(element);
                ApplyEffects(element, style, state, time, ref needsLayout);
            }
        }

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
                Walk(childElement, rules, ref anyActive, ref needsLayout, ref needsRepaint, ref animatedElements, time);
        }
    }

    // ------------------------------------------------------------------ animations

    private void ProcessAnimations(Element element, ComputedStyle style, ElementAnimations state,
        IReadOnlyList<StyleRuleKeyframes>? rules, double time)
    {
        var entries = ParseAnimationList(style, rules);

        // A name that has left the list can be scheduled again. The removal
        // happens after the scan: mutating the set while enumerating it throws.
        if (state.SeenAnimations.Count > 0)
        {
            List<string>? stale = null;
            foreach (var completed in state.SeenAnimations)
            {
                bool stillDeclared = false;
                foreach (var e in entries)
                {
                    if (string.Equals(e.Name, completed, StringComparison.Ordinal)) { stillDeclared = true; break; }
                }
                if (!stillDeclared) (stale ??= new List<string>()).Add(completed);
            }
            if (stale != null)
            {
                foreach (var name in stale) state.SeenAnimations.Remove(name);
            }
        }

        // Running occurrences are matched to the new list in order. Matching
        // greedily (rather than looking each name up) is what makes `a, b, a` three
        // distinct effects with the composite order preserved.
        foreach (var entry in entries) entry.Matched = null;
        foreach (var running in state.Animations)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Matched != null) continue;
                if (!string.Equals(entries[i].Name, running.Name, StringComparison.Ordinal)) continue;
                entries[i].Matched = running;
                break;
            }
        }

        for (int i = state.Animations.Count - 1; i >= 0; i--)
        {
            var running = state.Animations[i];

            var match = (AnimationEntry?)null;
            foreach (var e in entries)
            {
                if (ReferenceEquals(e.Matched, running)) { match = e; break; }
            }

            if (match == null)
            {
                state.Animations.RemoveAt(i);
                running.IsCancelled = true;
                QueueAnimationEvent(element, "animationcancel", running, time);
                continue;
            }

            // The spec replaces an animation whenever any of its inputs change;
            // the running occurrence does not survive that change. Pausing is the
            // exception: it holds the occurrence and freezes its local time.
            if (!match.Timing.Equals(running.Timing, includePlayState: false))
            {
                state.Animations.RemoveAt(i);
                running.IsCancelled = true;
                match.Matched = null;
                QueueAnimationEvent(element, "animationcancel", running, time);
            }
            else if (match.Timing.PlayState != running.Timing.PlayState)
            {
                if (match.Timing.PlayState == AnimationPlayState.Paused)
                {
                    // Pausing freezes the occurrence at its current local time.
                    running.HoldTimeMs = time - running.StartTimeMs;
                }
                else
                {
                    // Resuming continues from the held instant: the start time moves
                    // so that timeline - start is still the local time it was held at.
                    running.StartTimeMs = time - (running.HoldTimeMs ?? 0);
                    running.HoldTimeMs = null;
                }
                running.Timing = match.Timing;
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Matched != null) continue;
            if (entry.Name.Length == 0 || entry.Name == "none") continue;
            if (entry.Keyframes == null || !entry.Keyframes.IsUsable) continue;
            if (!entry.Timing.HasIterations) continue;
            // A name that already ran to completion under the current list is not
            // started again; the guard is cleared above if the name leaves the list.
            if (state.SeenAnimations.Contains(entry.Name)) continue;

            var created = new CssAnimation(entry.Name, entry.Keyframes, entry.Index)
            {
                Timing = entry.Timing,
                // The effect starts now, so a negative delay lands inside the
                // active phase immediately. That is the deterministic "freeze
                // frame" idiom every engine shares.
                StartTimeMs = time,
            };
            // An animation whose first style is already paused never plays: it is
            // held at the instant it was created, so a negative delay pins one
            // exact frame instead of following the timeline.
            if (entry.Timing.PlayState == AnimationPlayState.Paused)
            {
                created.HoldTimeMs = 0;
            }
            state.Animations.Add(created);
            entry.Matched = created;
        }
    }

    private List<AnimationEntry> ParseAnimationList(ComputedStyle style, IReadOnlyList<StyleRuleKeyframes>? rules)
    {
        var entries = new List<AnimationEntry>();

        var nameList = AnimationTimingFunction.SplitTopLevel(style.AnimationName ?? "none");
        if (nameList.Count == 0) nameList.Add("none");

        var durations = ParseTimeList(style.AnimationDuration);
        var delays = ParseTimeList(style.AnimationDelay);
        var iterations = ParseIterationList(style.AnimationIterationCount);
        var directions = ParseDirectionList(style.AnimationDirection);
        var fills = ParseFillList(style.AnimationFillMode);
        var plays = ParsePlayStateList(style.AnimationPlayState);
        var easings = ParseEasingList(style.AnimationTimingFunction);

        for (int i = 0; i < nameList.Count; i++)
        {
            var entry = new AnimationEntry
            {
                Name = nameList[i],
                Index = i,
                Duration = Pick(durations, i, 0),
                Delay = Pick(delays, i, 0),
                Iterations = Pick(iterations, i, 1),
                Direction = Pick(directions, i, AnimationPlayDirection.Normal),
                Fill = Pick(fills, i, AnimationFillMode.None),
                PlayState = Pick(plays, i, AnimationPlayState.Running),
                Easing = easings == null || easings.Count == 0
                    ? AnimationTimingFunction.Ease
                    : easings[i % easings.Count],
            };
            if (entry.Name.Length > 0 && entry.Name != "none")
                entry.Keyframes = ResolveKeyframes(entry.Name, rules);
            entries.Add(entry);
        }
        return entries;
    }

    private static T Pick<T>(List<T>? list, int index, T fallback) =>
        list == null || list.Count == 0 ? fallback : list[index % list.Count];

    private CssKeyframes? ResolveKeyframes(string name, IReadOnlyList<StyleRuleKeyframes>? rules)
    {
        if (_keyframesCache.TryGetValue(name, out var cached)) return cached;
        if (rules == null) { _keyframesCache[name] = null!; return null; }

        _rulesScratch.Clear();
        foreach (var rule in rules)
        {
            if (string.Equals(rule.Name, name, StringComparison.Ordinal))
                _rulesScratch.Add(rule);
        }
        var built = _rulesScratch.Count == 0 ? null : CssKeyframes.Build(name, _rulesScratch);
        _keyframesCache[name] = built!;
        return built;
    }

    private static List<double>? ParseTimeList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var items = AnimationTimingFunction.SplitTopLevel(value);
        if (items.Count == 0) return null;
        var result = new List<double>(items.Count);
        foreach (var item in items) result.Add(ParseTime(item));
        return result;
    }

    private static List<double>? ParseIterationList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var items = AnimationTimingFunction.SplitTopLevel(value);
        if (items.Count == 0) return null;
        var result = new List<double>(items.Count);
        foreach (var item in items)
        {
            var t = item.Trim().ToLowerInvariant();
            if (t == "infinite") { result.Add(double.PositiveInfinity); continue; }
            result.Add(AnimationTimingFunction.TryNumber(t, out var n) ? Math.Max(0, n) : 1);
        }
        return result;
    }

    private static List<AnimationPlayDirection>? ParseDirectionList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var items = AnimationTimingFunction.SplitTopLevel(value);
        if (items.Count == 0) return null;
        var result = new List<AnimationPlayDirection>(items.Count);
        foreach (var item in items)
        {
            result.Add(item.Trim().ToLowerInvariant() switch
            {
                "reverse" => AnimationPlayDirection.Reverse,
                "alternate" => AnimationPlayDirection.Alternate,
                "alternate-reverse" => AnimationPlayDirection.AlternateReverse,
                _ => AnimationPlayDirection.Normal,
            });
        }
        return result;
    }

    private static List<AnimationFillMode>? ParseFillList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var items = AnimationTimingFunction.SplitTopLevel(value);
        if (items.Count == 0) return null;
        var result = new List<AnimationFillMode>(items.Count);
        foreach (var item in items)
        {
            result.Add(item.Trim().ToLowerInvariant() switch
            {
                "forwards" => AnimationFillMode.Forwards,
                "backwards" => AnimationFillMode.Backwards,
                "both" => AnimationFillMode.Both,
                _ => AnimationFillMode.None,
            });
        }
        return result;
    }

    private static List<AnimationPlayState>? ParsePlayStateList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var items = AnimationTimingFunction.SplitTopLevel(value);
        if (items.Count == 0) return null;
        var result = new List<AnimationPlayState>(items.Count);
        foreach (var item in items)
            result.Add(item.Trim().ToLowerInvariant() == "paused" ? AnimationPlayState.Paused : AnimationPlayState.Running);
        return result;
    }

    private static List<AnimationTimingFunction>? ParseEasingList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var items = AnimationTimingFunction.SplitTopLevel(value);
        if (items.Count == 0) return null;
        var result = new List<AnimationTimingFunction>(items.Count);
        foreach (var item in items) result.Add(AnimationTimingFunction.Parse(item));
        return result;
    }

    /// <summary>Parse a CSS &lt;time&gt; into milliseconds. A bare 0 is a valid time.</summary>
    public static double ParseTime(string? value, double fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var t = value.Trim().ToLowerInvariant();
        if (t.EndsWith("ms", StringComparison.Ordinal))
            return AnimationTimingFunction.TryNumber(t[..^2], out var ms) ? ms : fallback;
        if (t.EndsWith("s", StringComparison.Ordinal))
            return AnimationTimingFunction.TryNumber(t[..^1], out var s) ? s * 1000 : fallback;
        if (t == "0") return 0;
        if (AnimationTimingFunction.TryNumber(t, out var bare)) return bare * 1000;
        return fallback;
    }

    // ----------------------------------------------------------------- transitions

    private void ProcessTransitions(Element element, ComputedStyle style, ElementAnimations state,
        double time, ref bool needsLayout, ref bool needsRepaint)
    {
        // 1. Track the properties this element could transition, and detect the
        //    value changes that start new transitions. Skipped when the host did
        //    not re-resolve the cascade: the styles on hand are then this engine's
        //    own output from last frame, and a "change" against them is not a
        //    change in the document. Transitions already running still advance
        //    below, because their progress is a function of time alone.
        var candidates = _styleRecomputed ? TransitionCandidates(style) : null;
        if (candidates != null && (candidates.Count > 0 || state.HasSnapshot))
        {
            if (state.HasSnapshot)
            {
                foreach (var property in state.Snapshot.Keys.ToList())
                {
                    string? current = GetValue(style, property);
                    if (current == null) continue;
                    if (state.Snapshot[property] == current) continue;

                    // Value changed: cancel the running transition (if any) and
                    // start a new one from the before-change style.
                    var running = FindTransition(state, property);
                    if (running != null)
                    {
                        state.Transitions.Remove(running);
                        running.IsCancelled = true;
                        QueueTransitionEvent(element, "transitioncancel", running, time);
                    }

                    var spec = GetTransitionSpec(style, property);
                    if (spec.HasValue)
                    {
                        // The transition starts at (change time + delay), so a
                        // negative delay puts the start in the past: that is how a
                        // transition is pinned part-way through from a static page.
                        var transition = new CssTransition(property, state.Snapshot[property], current,
                            time + spec.Value.Delay, spec.Value.Duration, spec.Value.Easing);
                        state.Transitions.Add(transition);
                        // transitionrun fires as soon as the transition is created,
                        // even when a positive delay postpones the active phase
                        // (css-transitions-1 §"Starting of transition": run precedes
                        // start, and both precede any change to the animated value).
                        transition.RunEventSent = true;
                        QueueTransitionEvent(element, "transitionrun", transition, time);
                    }
                    state.Snapshot[property] = current;
                }

                // Newly-tracked properties, first time they are candidates.
                if (candidates.Count > 0)
                    TrackNewCandidates(style, state, candidates, time);
            }
            else if (candidates.Count > 0)
            {
                // First style for this element: record the before-change style.
                // No transition may start on the initial value (Transitions 1 �?).
                TrackNewCandidates(style, state, candidates, time);
                state.HasSnapshot = true;
            }
        }

        // 2. Advance the running transitions and retire the finished ones.
        for (int i = state.Transitions.Count - 1; i >= 0; i--)
        {
            var transition = state.Transitions[i];
            if (time < transition.StartTimeMs) continue;

            if (!transition.StartEventSent)
            {
                transition.StartEventSent = true;
                QueueTransitionEvent(element, "transitionstart", transition, time);
            }

            if (AnimatableProperties.IsLayoutAffecting(transition.Property)) needsLayout = true;

            if (time >= transition.StartTimeMs + transition.DurationMs)
            {
                state.Transitions.RemoveAt(i);
                transition.IsFinished = true;
                QueueTransitionEvent(element, "transitionend", transition, time);
            }
        }
    }

    private void TrackNewCandidates(ComputedStyle style, ElementAnimations state, List<string> candidates, double time)
    {
        foreach (var property in candidates)
        {
            if (state.Snapshot.ContainsKey(property)) continue;
            var current = GetValue(style, property);
            if (current != null) state.Snapshot[property] = current;
        }
    }

    private static CssTransition? FindTransition(ElementAnimations state, string property)
    {
        foreach (var t in state.Transitions)
            if (string.Equals(t.Property, property, StringComparison.Ordinal)) return t;
        return null;
    }

    /// <summary>
    /// The properties this element may transition, or null when it declares none.
    /// <c>transition-property: all</c> expands to every animatable property;
    /// the expanded set is narrowed on first use to the properties the style
    /// engine can actually read, which keeps the per-frame cost proportional to
    /// the page's real style surface rather than to the whole property list.
    /// </summary>
    private static List<string>? TransitionCandidates(ComputedStyle style)
    {
        var list = AnimationTimingFunction.SplitTopLevel(style.TransitionProperty ?? "all");
        if (list.Count == 0) return null;

        bool anyDuration = false;
        foreach (var item in AnimationTimingFunction.SplitTopLevel(style.TransitionDuration ?? "0s"))
        {
            if (ParseTime(item) > 0) { anyDuration = true; break; }
        }
        if (!anyDuration) return null;

        if (!list.Any(p => p.Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            var explicitList = new List<string>(list.Count);
            foreach (var item in list)
            {
                var property = item.Trim().ToLowerInvariant();
                if (property == "none") continue;
                if (AnimatableProperties.IsAnimatable(property)) explicitList.Add(property);
            }
            return explicitList.Count == 0 ? null : explicitList;
        }

        return AllTransitionableList;
    }

    private static readonly List<string> AllTransitionableList = new(AnimatableProperties.AllTransitionable);

    private readonly struct TransitionSpec
    {
        public readonly double Duration;
        public readonly double Delay;
        public readonly AnimationTimingFunction Easing;

        public TransitionSpec(double duration, double delay, AnimationTimingFunction easing)
        {
            Duration = duration;
            Delay = delay;
            Easing = easing;
        }
    }

    /// <summary>
    /// Pick the duration / delay / easing for <paramref name="property"/> by
    /// cycling the comma lists and indexing by the property's position in
    /// <c>transition-property</c> (CSS Transitions 1 �?.2).
    /// </summary>
    private static TransitionSpec? GetTransitionSpec(ComputedStyle style, string property)
    {
        var properties = AnimationTimingFunction.SplitTopLevel(style.TransitionProperty ?? "all");
        if (properties.Count == 0) return null;

        int index;
        if (properties.Any(p => p.Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            index = AllTransitionableList.IndexOf(property);
            if (index < 0) return null;
        }
        else
        {
            index = -1;
            for (int i = 0; i < properties.Count; i++)
            {
                if (properties[i].Trim().Equals(property, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
            }
            if (index < 0) return null;
        }

        var durations = AnimationTimingFunction.SplitTopLevel(style.TransitionDuration ?? "0s");
        if (durations.Count == 0) return null;
        double duration = ParseTime(durations[index % durations.Count]);
        if (duration <= 0) return null;

        var delays = AnimationTimingFunction.SplitTopLevel(style.TransitionDelay ?? "0s");
        double delay = delays.Count > 0 ? ParseTime(delays[index % delays.Count]) : 0;

        var easings = AnimationTimingFunction.SplitTopLevel(style.TransitionTimingFunction ?? "ease");
        var easing = easings.Count switch
        {
            0 => AnimationTimingFunction.Ease,
            1 => AnimationTimingFunction.Parse(easings[0]),
            _ => AnimationTimingFunction.ParseList(string.Join(",", easings), easings.Count, index)[0],
        };

        return new TransitionSpec(duration, delay, easing);
    }

    // --------------------------------------------------------------------- apply

    private void ApplyEffects(Element element, ComputedStyle style, ElementAnimations state,
        double time, ref bool needsLayout)
    {
        var values = state.Scratch;
        var retire = state.RetireScratch;
        retire.Clear();

        // CSS animations first, in list order, so a later animation overrides an
        // earlier one for any property they share. A transition on the same
        // property still wins, because it is later in the effect composite order
        // (Transitions 1 �?.3).
        for (int i = 0; i < state.Animations.Count; i++)
        {
            var animation = state.Animations[i];
            var timing = animation.Timing;
            if (!timing.HasIterations) continue;

            double localTime = animation.LocalTime(time);
            var phase = timing.PhaseAt(localTime);

            bool finished = double.IsFinite(timing.ActiveDuration) && localTime >= timing.EndTime;
            if (finished && !animation.IsFinished)
            {
                animation.IsFinished = true;
                QueueAnimationEvent(element, "animationend", animation, time);
            }

            bool fillAfterwards = timing.FillMode is AnimationFillMode.Forwards or AnimationFillMode.Both;
            bool fillBeforewards = timing.FillMode is AnimationFillMode.Backwards or AnimationFillMode.Both;

            if (phase == AnimationPhase.Active)
            {
                // Order matters: the direction belongs to the iteration progress,
                // and the easing belongs to the keyframe interval. Applying the
                // easing first and reversing afterwards would make `ease` run
                // backwards for a `reverse` animation.
                double iterationProgress = timing.DirectedIterationProgress(localTime);
                SampleAndApply(element, style, animation, iterationProgress, timing.Easing, values, ref needsLayout);
                QueueIterationEvent(element, animation, localTime, timing, time);
            }
            else if (phase == AnimationPhase.After && fillAfterwards)
            {
                // The last iteration's end value, already resolved: no easing.
                double endProgress = EndProgress(timing, timing.EndTime) ? 1.0 : 0.0;
                SampleAndApply(element, style, animation, endProgress, timing.Easing, values, ref needsLayout);
            }
            else if (localTime < timing.Delay && fillBeforewards)
            {
                // Backwards fill: present the value the first iteration starts at.
                double startProgress = EndProgress(timing, timing.Delay) ? 0.0 : 1.0;
                SampleAndApply(element, style, animation, startProgress, timing.Easing, values, ref needsLayout);
            }
            else if (finished && !fillAfterwards)
            {
                // Nothing to contribute any more. Remember that this name has run
                // to completion under the current list, so the next frame does not
                // start it again; the occurrence itself is dropped in a separate
                // pass so the sample order above stays in list order.
                state.SeenAnimations.Add(animation.Name);
                retire.Add(animation);
            }
        }

        foreach (var dead in retire)
        {
            int index = state.Animations.IndexOf(dead);
            if (index >= 0) state.Animations.RemoveAt(index);
        }

        // Then transitions, which override the same properties.
        foreach (var transition in state.Transitions)
        {
            if (time < transition.StartTimeMs) continue;

            double progress = transition.ProgressAt(time);
            string value = CssValueInterpolator.Interpolate(
                transition.Property, transition.FromValue, transition.ToValue, progress)
                ?? (progress < 0.5 ? transition.FromValue : transition.ToValue);

            if (ApplyAnimatedValue(style, transition.Property, value))
            {
                if (AnimatableProperties.IsLayoutAffecting(transition.Property)) needsLayout = true;
                if (AnimatableProperties.BleedsOutsideBox(transition.Property)) _repaintBleeds = true;
            }
        }
    }

    private void SampleAndApply(Element element, ComputedStyle style, CssAnimation animation,
        double iterationProgress, AnimationTimingFunction effectEasing,
        Dictionary<string, string> values, ref bool needsLayout)
    {
        _valueCache.Clear();
        _underlyingElement = element;
        animation.Keyframes.Sample(iterationProgress, effectEasing, UnderlyingValue, values);

        foreach (var (property, value) in values)
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (ApplyAnimatedValue(style, property, value))
            {
                if (AnimatableProperties.IsLayoutAffecting(property)) needsLayout = true;
                if (AnimatableProperties.BleedsOutsideBox(property)) _repaintBleeds = true;
            }
        }
    }

    /// <summary>
    /// True when the direction leaves the effect running "forwards" at
    /// <paramref name="localTime"/>: normal, or an even iteration of alternate.
    /// </summary>
    private static bool EndProgress(AnimationTiming timing, double localTime) => !IsReversed(timing, localTime);

    private static bool IsReversed(AnimationTiming timing, double localTime) => timing.Direction switch
    {
        AnimationPlayDirection.Reverse => true,
        AnimationPlayDirection.Alternate =>
            Math.Floor(Math.Max(0, localTime - timing.Delay) / Math.Max(1e-6, timing.Duration)) % 2 == 1,
        AnimationPlayDirection.AlternateReverse =>
            Math.Floor(Math.Max(0, localTime - timing.Delay) / Math.Max(1e-6, timing.Duration)) % 2 == 0,
        _ => false,
    };

    private Element? _underlyingElement;

    /// <summary>
    /// The element's own cascaded value for a property the keyframes do not
    /// mention �?the "underlying value" the implicit 0% / 100% endpoints use.
    /// </summary>
    private string? UnderlyingValue(string property)
    {
        if (_underlyingElement?.ComputedStyle == null) return null;
        return ComputedValueSerializer.Get(_underlyingElement.ComputedStyle, property);
    }

    private string? GetValue(ComputedStyle style, string property)
    {
        if (_valueCache.TryGetValue(property, out var cached)) return cached;
        var value = ComputedValueSerializer.Get(style, property);
        _valueCache[property] = value;
        return value;
    }

    /// <summary>
    /// Write an animated value onto the computed style through the ordinary
    /// declaration applier, so an animated property is interpreted by exactly
    /// the same code as the declaration that authored it.
    ///
    /// A handful of properties are deferred by the applier because they need
    /// context the plain (style, name, value) signature does not carry �?the
    /// parent's font size for <c>font-size</c>, and the shared line-box slot
    /// bookkeeping for <c>line-height</c>. They are handled here so an animated
    /// value is never silently dropped.
    /// </summary>
    private static bool ApplyAnimatedValue(ComputedStyle style, string property, string value)
    {
        if (style == null || string.IsNullOrEmpty(property) || string.IsNullOrEmpty(value)) return false;
        try
        {
            switch (property)
            {
                case "font-size":
                {
                    // An absolute px/rem target needs no context; a relative unit
                    // would need the parent's size, and the keyframe values the
                    // engine produces are already resolved lengths.
                    float px = Length.Parse(value) is { } l && !float.IsNaN(l.ToPixels(style.FontSize, style.FontSize, 0, 0))
                        ? l.ToPixels(style.FontSize, style.FontSize, 0, 0)
                        : float.NaN;
                    if (!float.IsNaN(px) && px > 0) { style.FontSize = px; return true; }
                    return false;
                }
                case "font-weight":
                    style.FontWeight = CssPropertyApplier.ParseFontWeight(value);
                    return true;
                case "font-style":
                    style.FontStyle = CssPropertyApplier.ParseFontStyle(value, style);
                    return true;
                case "line-height":
                    Acrux.Core.Fonts.LineBoxMetrics.ApplyLineHeight(style, value);
                    return true;
            }

            CssPropertyApplier.Apply(style, property, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------- events

    private readonly record struct QueuedEvent(Element Element, Event Event);

    private void QueueAnimationEvent(Element element, string type, CssAnimation animation, double time)
    {
        if (_suppressEvents) return;
        _pendingEvents.Add(new QueuedEvent(element, new AnimationEvent(type)
        {
            AnimationName = animation.Name,
            ElapsedTime = Math.Max(0, time - animation.StartTimeMs),
            CurrentTime = time,
        }));
    }

    private void QueueIterationEvent(Element element, CssAnimation animation, double localTime,
        AnimationTiming timing, double time)
    {
        if (_suppressEvents) return;
        if (!animation.StartEventSent)
        {
            animation.StartEventSent = true;
            _pendingEvents.Add(new QueuedEvent(element, new AnimationEvent("animationstart")
            {
                AnimationName = animation.Name,
                // With a negative delay the active phase began before the effect
                // started, so the reported elapsed time is already positive.
                ElapsedTime = Math.Max(0, -timing.Delay),
                CurrentTime = time,
            }));
        }

        double iteration = timing.CurrentIteration(localTime);
        if (iteration <= 0) return;
        if (!animation.ReportedIterations.Add(iteration)) return;

        _pendingEvents.Add(new QueuedEvent(element, new AnimationEvent("animationiteration")
        {
            AnimationName = animation.Name,
            ElapsedTime = iteration * timing.Duration,
            CurrentTime = time,
        }));
    }

    private void QueueTransitionEvent(Element element, string type, CssTransition transition, double time)
    {
        if (_suppressEvents) return;
        _pendingEvents.Add(new QueuedEvent(element, new TransitionEvent(type)
        {
            PropertyName = transition.Property,
            ElapsedTime = transition.ElapsedTimeAt(time),
            CurrentTime = time,
        }));
    }

    private void FlushEvents()
    {
        if (_pendingEvents.Count == 0)
        {
            if (EventSink != null) return;
        }
        var sink = EventSink;
        foreach (var (element, evt) in _pendingEvents)
        {
            try
            {
                // Dom-level listeners first, then the script bridge.
                element.InvokeEventListeners(evt);
                sink?.Invoke(element, evt);
            }
            catch
            {
                // A throwing listener must not abort the frame.
            }
        }
        _pendingEvents.Clear();
    }

    // -------------------------------------------------------------------- cleanup

    private void PruneDetached()
    {
        if (_elements.Count == 0) return;
        List<Element>? dead = null;
        foreach (var kv in _elements)
        {
            if (_visited.Contains(kv.Key)) continue;
            (dead ??= new List<Element>()).Add(kv.Key);
        }
        if (dead == null) return;
        foreach (var element in dead)
            _elements.Remove(element);
    }

    /// <summary>Forget every effect, e.g. after a navigation.</summary>
    public void Reset()
    {
        _elements.Clear();
        _pendingEvents.Clear();
        _visited.Clear();
        _keyframesCache.Clear();
        _valueCache.Clear();
        _lastTimeMs = double.NaN;

        // A new document gets a new document timeline. Without this the timeline
        // keeps counting from whenever the engine was constructed, so a page
        // loaded a second into the session samples its animations at t=1000ms and
        // anything longer is already over before the first frame is drawn.
        _timeline.SetCurrentTime(0);
    }

    /// <summary>Per-element animation state.</summary>
    private sealed class ElementAnimations
    {
        public readonly List<CssAnimation> Animations = new();
        public readonly List<CssTransition> Transitions = new();
        public readonly Dictionary<string, string> Scratch = new(StringComparer.Ordinal);

        /// <summary>Occurrences to drop after this tick, collected during sampling.</summary>
        public readonly List<CssAnimation> RetireScratch = new();

        /// <summary>Last frame's cascaded values: the before-change style.</summary>
        public readonly Dictionary<string, string> Snapshot = new(StringComparer.Ordinal);

        /// <summary>Names that have run to completion under the current name list.</summary>
        public readonly HashSet<string> SeenAnimations = new(StringComparer.Ordinal);

        public bool HasSnapshot { get; set; }

        public int LiveEffectCount => Animations.Count + Transitions.Count;
    }

    private sealed class AnimationEntry
    {
        public string Name { get; init; } = "none";
        public int Index { get; init; }
        public double Duration { get; init; }
        public double Delay { get; init; }
        public double Iterations { get; init; } = 1;
        public AnimationPlayDirection Direction { get; init; }
        public AnimationFillMode Fill { get; init; }
        public AnimationPlayState PlayState { get; init; }
        public AnimationTimingFunction Easing { get; init; } = AnimationTimingFunction.Ease;
        public CssKeyframes? Keyframes { get; set; }

        /// <summary>The running occurrence this entry was matched to, or null once created.</summary>
        public CssAnimation? Matched { get; set; }

        public AnimationTiming Timing => new(Delay, Duration, Iterations, Fill, Direction, PlayState, Easing);
    }
}
