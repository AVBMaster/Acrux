using System.Collections;

namespace Acrux.Core.Dom;

public class HtmlCollection : IReadOnlyList<Element>, IEnumerable<Element>
{
    private readonly List<Element> _items;

    public HtmlCollection() : this(new List<Element>())
    {
    }

    public HtmlCollection(List<Element> items)
    {
        _items = items;
    }

    public int Length => _items.Count;
    public int Count => _items.Count;

    public Element? NamedItem(string name)
    {
        return _items.FirstOrDefault(e =>
            e.Id == name ||
            (e.GetAttribute("name") == name));
    }

    public Element this[int index] => _items[index];
    public Element? this[string name] => NamedItem(name);

    public IEnumerator<Element> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
