using System.Collections;

namespace Acrux.Core.Dom;

public class NamedNodeMap : IReadOnlyList<Attr>, IEnumerable<Attr>
{
    private readonly List<Attr> _items = new();

    public int Length => _items.Count;
    public int Count => _items.Count;

    public Attr? GetNamedItem(string name)
    {
        return _items.FirstOrDefault(a => a.Name == name);
    }

    public Attr? GetNamedItemNS(string? ns, string name)
    {
        return _items.FirstOrDefault(a => a.LocalName == name && a.NamespaceUri == ns);
    }

    public Attr SetNamedItem(Attr attr)
    {
        var existing = GetNamedItem(attr.Name);
        if (existing != null)
            _items.Remove(existing);
        _items.Add(attr);
        return existing;
    }

    public Attr SetNamedItemNS(Attr attr)
    {
        var existing = GetNamedItemNS(attr.NamespaceUri, attr.LocalName);
        if (existing != null)
            _items.Remove(existing);
        _items.Add(attr);
        return existing;
    }

    public Attr? RemoveNamedItem(string name)
    {
        var attr = GetNamedItem(name);
        if (attr != null)
            _items.Remove(attr);
        return attr;
    }

    public Attr? RemoveNamedItemNS(string? ns, string name)
    {
        var attr = GetNamedItemNS(ns, name);
        if (attr != null)
            _items.Remove(attr);
        return attr;
    }

    public Attr this[int index] => _items[index];

    public IEnumerator<Attr> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
