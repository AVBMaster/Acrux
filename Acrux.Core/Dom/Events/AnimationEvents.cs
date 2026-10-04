namespace Acrux.Core.Dom;

/// <summary>
/// Base for the events that carry a timeline position (Web Animations §4.7).
/// Both the animation and the transition event families expose
/// <c>currentTime</c>, measured on the same timeline the effect runs on.
/// </summary>
public class TimeEvent : Event
{
    /// <summary>Timeline time at which the event was generated, in milliseconds.</summary>
    public double CurrentTime { get; internal set; }

    public TimeEvent(string type, EventInit? init = null) : base(type, init) { }

    public double currentTime => CurrentTime;
}

/// <summary>
/// <c>animationstart</c> / <c>animationend</c> / <c>animationiteration</c> /
/// <c>animationcancel</c> (CSS Animations 1 §4.1).
/// </summary>
public class AnimationEvent : TimeEvent
{
    public string AnimationName { get; internal set; }

    /// <summary>
    /// Time the animation has been running, in milliseconds. Positive at
    /// <c>animationstart</c> when the animation has a negative delay, because its
    /// active phase began before the effect started.
    /// </summary>
    public double ElapsedTime { get; internal set; }

    /// <summary>Pseudo-element the animation targets, or null for the element itself.</summary>
    public string? PseudoElement { get; internal set; }

    public AnimationEvent(string type, EventInit? init = null) : base(type, init)
    {
        if (init is AnimationEventInit a)
        {
            AnimationName = a.AnimationName;
            ElapsedTime = a.ElapsedTime;
            PseudoElement = string.IsNullOrEmpty(a.PseudoElement) ? null : a.PseudoElement;
        }
        else
        {
            AnimationName = "";
            ElapsedTime = 0;
            PseudoElement = null;
        }
    }

    public string animationName => AnimationName;
    public double elapsedTime => ElapsedTime;
    public string? pseudoElement => PseudoElement;
}

public class AnimationEventInit : EventInit
{
    public string AnimationName { get; set; } = "";
    public double ElapsedTime { get; set; }
    public string PseudoElement { get; set; } = "";
}

/// <summary>
/// <c>transitionrun</c> / <c>transitionstart</c> / <c>transitionend</c> /
/// <c>transitioncancel</c> (CSS Transitions 1 §6).
/// </summary>
public class TransitionEvent : TimeEvent
{
    public string PropertyName { get; internal set; }

    /// <summary>Time the transition has been running, in milliseconds.</summary>
    public double ElapsedTime { get; internal set; }

    public string? PseudoElement { get; internal set; }

    public TransitionEvent(string type, EventInit? init = null) : base(type, init)
    {
        if (init is TransitionEventInit t)
        {
            PropertyName = t.PropertyName;
            ElapsedTime = t.ElapsedTime;
            PseudoElement = string.IsNullOrEmpty(t.PseudoElement) ? null : t.PseudoElement;
        }
        else
        {
            PropertyName = "";
            ElapsedTime = 0;
            PseudoElement = null;
        }
    }

    public string propertyName => PropertyName;
    public double elapsedTime => ElapsedTime;
    public string? pseudoElement => PseudoElement;
}

public class TransitionEventInit : EventInit
{
    public string PropertyName { get; set; } = "";
    public double ElapsedTime { get; set; }
    public string PseudoElement { get; set; } = "";
}
