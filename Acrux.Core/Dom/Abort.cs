namespace Acrux.Core.Dom;

public class AbortController
{
    public AbortSignal Signal { get; } = new();

    public void Abort(object? reason = null)
    {
        Signal.SignalAbort(reason);
    }
}

public class AbortSignal
{
    private readonly List<Action> _callbacks = new();
    private readonly object _lock = new();

    public bool Aborted { get; private set; }
    public object? Reason { get; private set; }
    public event Action? OnAbort;

    internal void SignalAbort(object? reason = null)
    {
        lock (_lock)
        {
            if (Aborted) return;
            Aborted = true;
            Reason = reason ?? new DOMException("The signal is aborted", "AbortError");
        }
        OnAbort?.Invoke();
        List<Action> callbacks;
        lock (_lock) callbacks = new List<Action>(_callbacks);
        foreach (var cb in callbacks)
        {
            try { cb(); } catch { }
        }
    }

    internal void AddCallback(Action callback)
    {
        if (Aborted) { callback(); return; }
        lock (_lock) _callbacks.Add(callback);
    }

    internal void RemoveCallback(Action callback)
    {
        lock (_lock) _callbacks.Remove(callback);
    }

    public void ThrowIfAborted()
    {
        if (Aborted) throw Reason as Exception ?? new DOMException("The signal is aborted", "AbortError");
    }

    public static AbortSignal Timeout(ulong ms)
    {
        var signal = new AbortSignal();
        _ = Task.Delay((int)ms).ContinueWith(_ => signal.SignalAbort(new DOMException("The operation timed out", "TimeoutError")));
        return signal;
    }

    public static AbortSignal Any(params AbortSignal[] signals)
    {
        var combined = new AbortSignal();
        foreach (var s in signals)
        {
            if (s.Aborted) { combined.SignalAbort(s.Reason); return combined; }
            s.OnAbort += () => combined.SignalAbort(s.Reason);
        }
        return combined;
    }

    public static AbortSignal CreateAborted(object? reason = null)
    {
        var signal = new AbortSignal();
        signal.SignalAbort(reason);
        return signal;
    }
}

public class DOMException : Exception
{
    public string Name { get; }

    public DOMException(string message, string name) : base(message)
    {
        Name = name;
    }

    public override string ToString() => $"{Name}: {Message}";
}
