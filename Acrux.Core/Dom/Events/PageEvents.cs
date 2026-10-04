namespace Acrux.Core.Dom;

public class PageTransitionEvent : Event
{
    public bool Persisted { get; }

    public PageTransitionEvent(string type, PageTransitionEventInit? init = null) : base(type, init)
    {
        Persisted = init?.Persisted ?? false;
    }
}

public class PageTransitionEventInit : EventInit
{
    public bool Persisted { get; set; }
}

public class HashChangeEvent : Event
{
    public string OldUrl { get; }
    public string NewUrl { get; }

    public HashChangeEvent(string type, HashChangeEventInit? init = null) : base(type, init)
    {
        OldUrl = init?.OldUrl ?? "";
        NewUrl = init?.NewUrl ?? "";
    }
}

public class HashChangeEventInit : EventInit
{
    public string OldUrl { get; set; } = "";
    public string NewUrl { get; set; } = "";
}

public class PopStateEvent : Event
{
    public object? State { get; }

    public PopStateEvent(string type, PopStateEventInit? init = null) : base(type, init)
    {
        State = init?.State;
    }
}

public class PopStateEventInit : EventInit
{
    public object? State { get; set; }
}

public class BeforeUnloadEvent : Event
{
    public string? ReturnValue { get; set; }

    public BeforeUnloadEvent(string type, EventInit? init = null) : base(type, init)
    {
    }
}

public class StorageEvent : Event
{
    public string? Key { get; }
    public string? OldValue { get; }
    public string? NewValue { get; }
    public string Url { get; }
    public Storage? StorageArea { get; }

    public StorageEvent(string type, StorageEventInit? init = null) : base(type, init)
    {
        Key = init?.Key;
        OldValue = init?.OldValue;
        NewValue = init?.NewValue;
        Url = init?.Url ?? "";
        StorageArea = init?.StorageArea;
    }
}

public class StorageEventInit : EventInit
{
    public string? Key { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Url { get; set; } = "";
    public Storage? StorageArea { get; set; }
}

public class Storage
{
    private readonly Dictionary<string, string> _items = new();

    public int Length => _items.Count;
    public string? Key(int index) => index >= 0 && index < _items.Count ? _items.ElementAt(index).Key : null;
    public string? GetItem(string key) => _items.TryGetValue(key, out var v) ? v : null;
    public void SetItem(string key, string value) => _items[key] = value;
    public void RemoveItem(string key) => _items.Remove(key);
    public void Clear() => _items.Clear();
}
