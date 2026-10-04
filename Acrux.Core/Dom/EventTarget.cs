namespace Acrux.Core.Dom;

public abstract class EventTarget
{
    private readonly Dictionary<string, List<RegisteredEventListener>> _listeners = new();

    public virtual void AddEventListener(string type, object? callback = null, EventListenerOptions? options = null)
    {
        if (callback == null) return;
        var capture = options?.Capture ?? false;
        var once = options?.Once ?? false;
        var passive = options?.Passive ?? false;
        AbortSignal? signal = options?.Signal;

        if (signal?.Aborted == true) return;

        var listener = new RegisteredEventListener
        {
            Type = type,
            Callback = callback,
            Capture = capture,
            Once = once,
            Passive = passive,
            Signal = signal
        };

        if (!_listeners.TryGetValue(type, out var list))
        {
            list = new List<RegisteredEventListener>();
            _listeners[type] = list;
        }
        list.Add(listener);

        if (signal != null)
        {
            signal.AddCallback(() => RemoveEventListener(type, callback, new EventListenerOptions { Capture = capture }));
        }
    }

    public virtual void RemoveEventListener(string type, object? callback = null, EventListenerOptions? options = null)
    {
        if (callback == null) return;
        var capture = options?.Capture ?? false;

        if (_listeners.TryGetValue(type, out var list))
        {
            list.RemoveAll(l => l.Callback == callback && l.Capture == capture);
            if (list.Count == 0) _listeners.Remove(type);
        }
    }

    public virtual bool DispatchEvent(Event evt)
    {
        if (evt.Dispatched)
            throw new InvalidOperationException("Event already being dispatched.");
        if (evt.Initialized == false)
            throw new InvalidOperationException("Event not initialized.");

        evt.Dispatched = true;
        evt.Target = this;

        var eventPath = BuildEventPath(evt);

        DispatchEventToPath(evt, eventPath);

        evt.Dispatched = false;
        evt.EventPhase = EventPhase.None;

        return !evt.DefaultPrevented;
    }

    protected virtual List<EventTarget> BuildEventPath(Event evt)
    {
        var path = new List<EventTarget>();
        if (this is Node node)
        {
            var current = node.ParentNode;
            while (current != null)
            {
                path.Add(current);
                current = current.ParentNode;
            }
        }
        path.Reverse();
        return path;
    }

    private void DispatchEventToPath(Event evt, List<EventTarget> path)
    {
        evt.EventPhase = EventPhase.Capturing;
        for (int i = 0; i < path.Count; i++)
        {
            if (evt.StopPropagationFlag) return;
            evt.CurrentTarget = path[i];
            InvokeListeners(evt, path[i], phase: EventPhase.Capturing);
            if (evt.StopImmediatePropagationFlag) return;
        }

        evt.EventPhase = EventPhase.AtTarget;
        evt.CurrentTarget = this;
        InvokeListeners(evt, this, phase: EventPhase.AtTarget);
        if (evt.StopImmediatePropagationFlag) return;

        if (evt.Bubbles)
        {
            evt.EventPhase = EventPhase.Bubbling;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (evt.StopPropagationFlag) return;
                evt.CurrentTarget = path[i];
                InvokeListeners(evt, path[i], phase: EventPhase.Bubbling);
                if (evt.StopImmediatePropagationFlag) return;
            }
        }
    }

    private void InvokeListeners(Event evt, EventTarget target, EventPhase phase)
    {
        if (target is not EventTarget et) return;
        var listeners = et._listeners;

        if (!listeners.TryGetValue(evt.Type, out var typeListeners))
            return;

        bool isAtTarget = phase == EventPhase.AtTarget;

        foreach (var listener in typeListeners.ToList())
        {
            if (listener.Signal?.Aborted == true)
            {
                typeListeners.Remove(listener);
                continue;
            }

            bool captureMatch = listener.Capture == (phase == EventPhase.Capturing);
            if (!isAtTarget && !captureMatch)
                continue;

            if (listener.Once)
                typeListeners.Remove(listener);

            try
            {
                if (listener.Callback is Action<Event> action)
                    action(evt);
                else if (listener.Callback is EventListener el)
                    el.HandleEvent(evt);
            }
            catch
            {
            }

            if (evt.StopImmediatePropagationFlag) return;
        }
    }

    internal void InvokeEventListeners(Event evt)
    {
        if (_listeners.TryGetValue(evt.Type, out var typeListeners))
        {
            foreach (var listener in typeListeners.ToList())
            {
                if (listener.Signal?.Aborted == true)
                {
                    typeListeners.Remove(listener);
                    continue;
                }

                if (listener.Once)
                    typeListeners.Remove(listener);

                try
                {
                    if (listener.Callback is Action<Event> action)
                        action(evt);
                    else if (listener.Callback is EventListener el)
                        el.HandleEvent(evt);
                }
                catch { }
            }
        }
    }
}

public class EventListenerOptions
{
    public bool Capture { get; set; }
    public bool Once { get; set; }
    public bool Passive { get; set; }
    public AbortSignal? Signal { get; set; }
}

public class AddEventListenerOptions : EventListenerOptions
{
    public bool Abortable { get; set; }
}

public interface EventListener
{
    void HandleEvent(Event evt);
}

internal class RegisteredEventListener
{
    public string Type { get; set; } = string.Empty;
    public object? Callback { get; set; }
    public bool Capture { get; set; }
    public bool Once { get; set; }
    public bool Passive { get; set; }
    public AbortSignal? Signal { get; set; }
}
