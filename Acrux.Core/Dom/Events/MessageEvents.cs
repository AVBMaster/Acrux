namespace Acrux.Core.Dom;

public class MessageEvent : Event
{
    public object? Data { get; }
    public string Origin { get; }
    public string LastEventId { get; }
    public object? Source { get; }
    public MessagePort[] Ports { get; }
    public bool UserActivation { get; }

    public MessageEvent(string type, MessageEventInit? init = null) : base(type, init)
    {
        Data = init?.Data;
        Origin = init?.Origin ?? "";
        LastEventId = init?.LastEventId ?? "";
        Source = init?.Source;
        Ports = init?.Ports ?? Array.Empty<MessagePort>();
        UserActivation = init?.UserActivation ?? false;
    }
}

public class MessageEventInit : EventInit
{
    public object? Data { get; set; }
    public string Origin { get; set; } = "";
    public string LastEventId { get; set; } = "";
    public object? Source { get; set; }
    public MessagePort[] Ports { get; set; } = Array.Empty<MessagePort>();
    public bool UserActivation { get; set; }
}

public class MessagePort
{
    public void PostMessage(object? message) { }
    public void Close() { }
    public void Start() { }
}

public class CloseEvent : Event
{
    public bool WasClean { get; }
    public ushort Code { get; }
    public string Reason { get; }

    public CloseEvent(string type, CloseEventInit? init = null) : base(type, init)
    {
        WasClean = init?.WasClean ?? false;
        Code = init?.Code ?? 0;
        Reason = init?.Reason ?? "";
    }
}

public class CloseEventInit : EventInit
{
    public bool WasClean { get; set; }
    public ushort Code { get; set; }
    public string Reason { get; set; } = "";
}
