namespace Acrux.Core.Dom;

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
        set => Data = value ?? string.Empty;
    }

    public override string? TextContent
    {
        get => _data;
        set => Data = value ?? string.Empty;
    }

    /// <summary>The one place text data changes: every CharacterData operation (and the nodeValue
    /// and textContent accessors) funnels through here, so a mutation observer sees the same
    /// record whatever spelling the page used.</summary>
    public string Data
    {
        get => _data;
        set
        {
            var old = _data;
            _data = value;
            DomMutationTracker.NoteCharacterData(this, old, value);
            OnDataChanged(old, value);
            DomMutationTracker.Notify();
        }
    }

    public int Length => _data.Length;

    /// <summary>DOM §3.6.3-§3.6.7: the offset is an index into the data and a place past the end is
    /// the IndexSizeError the text names — a negative one arrives as an enormous unsigned long and is
    /// the same error. A count that runs off the end is not an error: it is cut to what is left,
    /// which is why a page may ask for more of the string than it holds.</summary>
    private void CheckRange(int offset, int count)
    {
        if (count < 0 || offset < 0 || offset > _data.Length)
            throw new DOMException("Failed to execute on CharacterData: the offset is past the end "
                + "of the data, or the count is negative.", "IndexSizeError");
    }

    public string SubstringData(int offset, int count)
    {
        CheckRange(offset, count);
        count = Math.Min(count, _data.Length - offset);
        return _data.Substring(offset, count);
    }

    public void AppendData(string data)
    {
        Data = _data + data;
    }

    public void InsertData(int offset, string data)
    {
        CheckRange(offset, 0);
        Data = _data.Insert(offset, data);
    }

    public void DeleteData(int offset, int count)
    {
        CheckRange(offset, count);
        count = Math.Min(count, _data.Length - offset);
        Data = _data.Remove(offset, count);
    }

    public void ReplaceData(int offset, int count, string data)
    {
        CheckRange(offset, count);
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
