using System.Collections;

namespace Acrux.Core.Dom;

public class DomStringList : IReadOnlyList<string>, IEnumerable<string>
{
    private readonly List<string> _items;

    public DomStringList() : this(new List<string>())
    {
    }

    public DomStringList(List<string> items)
    {
        _items = items;
    }

    public int Length => _items.Count;
    public int Count => _items.Count;

    public bool Contains(string value) => _items.Contains(value);

    public string this[int index] => _items[index];

    public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
