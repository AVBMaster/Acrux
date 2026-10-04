namespace Acrux.Core.Dom;

public enum EventPhase : ushort
{
    None = 0,
    Capturing = 1,
    AtTarget = 2,
    Bubbling = 3
}

public class Event
{
    public string Type { get; protected set; } = string.Empty;
    public EventTarget? Target { get; internal set; }
    public EventTarget? CurrentTarget { get; internal set; }
    public EventPhase EventPhase { get; internal set; } = EventPhase.None;
    public bool Bubbles { get; protected set; }
    public bool Cancelable { get; protected set; }
    public bool Composed { get; protected set; }
    public long TimeStamp { get; }
    public bool DefaultPrevented { get; private set; }
    public bool IsTrusted { get; internal set; }

    // Internal state
    internal bool StopPropagationFlag { get; private set; }
    internal bool StopImmediatePropagationFlag { get; private set; }
    internal bool CanceledFlag { get; private set; }
    internal bool Initialized { get; set; }
    internal bool Dispatched { get; set; }

    public Event(string type, EventInit? init = null)
    {
        Type = type;
        Bubbles = init?.Bubbles ?? false;
        Cancelable = init?.Cancelable ?? false;
        Composed = init?.Composed ?? false;
        TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Initialized = true;
    }

    public void StopPropagation()
    {
        StopPropagationFlag = true;
    }

    public void StopImmediatePropagation()
    {
        StopPropagationFlag = true;
        StopImmediatePropagationFlag = true;
    }

    public void PreventDefault()
    {
        if (Cancelable)
            DefaultPrevented = true;
    }

    public void InitEvent(string type, bool bubbles, bool cancelable)
    {
        if (Dispatched) return;
        Type = type;
        Bubbles = bubbles;
        Cancelable = cancelable;
        Initialized = true;
    }

    public List<EventTarget> ComposedPath()
    {
        var path = new List<EventTarget>();
        if (Target is Node node)
        {
            var current = node.ParentNode;
            while (current != null)
            {
                path.Add(current);
                current = current.ParentNode;
            }
            path.Reverse();
        }
        path.Insert(0, Target!);
        return path;
    }

    // Legacy properties
    public string? ReturnValue
    {
        get => DefaultPrevented ? null : string.Empty;
        set { if (value == null) PreventDefault(); }
    }

    public bool CancelBubble
    {
        get => StopPropagationFlag;
        set { if (value) StopPropagation(); }
    }

    public EventTarget? SrcElement => Target;
}

public class EventInit
{
    public bool Bubbles { get; set; }
    public bool Cancelable { get; set; }
    public bool Composed { get; set; }
}

public class CustomEvent : Event
{
    public object? Detail { get; }

    public CustomEvent(string type, CustomEventInit? init = null)
        : base(type, init)
    {
        Detail = init?.Detail;
    }

    public void InitCustomEvent(string type, bool bubbles, bool cancelable, object? detail)
    {
        InitEvent(type, bubbles, cancelable);
    }
}

public class CustomEventInit : EventInit
{
    public object? Detail { get; set; }
}

public class ErrorEvent : Event
{
    public string? Message { get; }
    public string? Filename { get; }
    public int Lineno { get; }
    public int Colno { get; }
    public object? Error { get; }

    public ErrorEvent(string type, ErrorEventInit? init = null) : base(type, init)
    {
        Message = init?.Message;
        Filename = init?.Filename;
        Lineno = init?.Lineno ?? 0;
        Colno = init?.Colno ?? 0;
        Error = init?.Error;
    }
}

public class ErrorEventInit : EventInit
{
    public string? Message { get; set; }
    public string? Filename { get; set; }
    public int Lineno { get; set; }
    public int Colno { get; set; }
    public object? Error { get; set; }
}

public class PromiseRejectionEvent : Event
{
    public object? Promise { get; }
    public object? Reason { get; }

    public PromiseRejectionEvent(string type, PromiseRejectionEventInit? init = null) : base(type, init)
    {
        Promise = init?.Promise;
        Reason = init?.Reason;
    }
}

public class PromiseRejectionEventInit : EventInit
{
    public object? Promise { get; set; }
    public object? Reason { get; set; }
}
