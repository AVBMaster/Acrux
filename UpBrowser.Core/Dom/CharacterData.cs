namespace UpBrowser.Core.Dom;

public abstract class CharacterData : Node
{
    private string _data;

    protected CharacterData(string data, string nodeName) : base()
    {
        _data = data;
        NodeName = nodeName;
    }

    public override string? NodeValue
    {
        get => _data;
        set { _data = value ?? string.Empty; DomMutationTracker.Notify(); }
    }

    public override string? TextContent
    {
        get => _data;
        set { _data = value ?? string.Empty; DomMutationTracker.Notify(); }
    }

    public string Data
    {
        get => _data;
        set
        {
            var old = _data;
            _data = value;
            OnDataChanged(old, value);
            DomMutationTracker.Notify();
        }
    }

    public int Length => _data.Length;

    public string SubstringData(int offset, int count)
    {
        if (offset < 0 || offset >= _data.Length) return string.Empty;
        count = Math.Min(count, _data.Length - offset);
        return _data.Substring(offset, count);
    }

    public void AppendData(string data)
    {
        Data = _data + data;
    }

    public void InsertData(int offset, string data)
    {
        if (offset < 0 || offset > _data.Length) return;
        Data = _data.Insert(offset, data);
    }

    public void DeleteData(int offset, int count)
    {
        if (offset < 0 || offset >= _data.Length) return;
        count = Math.Min(count, _data.Length - offset);
        Data = _data.Remove(offset, count);
    }

    public void ReplaceData(int offset, int count, string data)
    {
        if (offset < 0 || offset > _data.Length) return;
        count = Math.Min(count, _data.Length - offset);
        Data = _data.Remove(offset, count).Insert(offset, data);
    }

    protected virtual void OnDataChanged(string oldValue, string newValue) { }

    public Element? NextElementSibling
    {
        get
        {
            var next = NextSibling;
            while (next != null && next is not Element)
                next = next.NextSibling;
            return next as Element;
        }
    }

    public Element? PreviousElementSibling
    {
        get
        {
            var prev = PreviousSibling;
            while (prev != null && prev is not Element)
                prev = prev.PreviousSibling;
            return prev as Element;
        }
    }

    public void Before(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        foreach (var node in nodes)
            parent.InsertBefore(node, this);
    }

    public void After(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        var next = NextSibling;
        foreach (var node in nodes)
            parent.InsertBefore(node, next);
    }

    public void ReplaceWith(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        var next = NextSibling;
        Remove();
        foreach (var node in nodes)
            parent.InsertBefore(node, next);
    }

    public new void Remove()
    {
        ParentNode?.RemoveChild(this);
    }
}
