namespace Acrux.Core.Dom.Parser;

internal class HtmlFormattingElementList
{
    internal class Entry
    {
        public HtmlStackItem? StackItem { get; set; }
        public bool IsMarker => StackItem == null;
        public Element? GetElement() => StackItem?.GetElement();
    }

    private readonly List<Entry> _entries = new();

    public bool IsEmpty => _entries.Count == 0;
    public int Count => _entries.Count;

    public Entry this[int index] => _entries[index];

    public Element? ClosestElementInScopeWithName(string targetName)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.IsMarker) return null;
            if (string.Equals(entry.StackItem?.Name, targetName, StringComparison.OrdinalIgnoreCase))
                return entry.GetElement();
        }
        return null;
    }

    public bool Contains(Element element) => Find(element) >= 0;

    public int Find(Element element)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].GetElement() == element)
                return i;
        }
        return -1;
    }

    public void Append(HtmlStackItem item)
    {
        _entries.Add(new Entry { StackItem = item });
    }

    public void Remove(Element element)
    {
        int idx = Find(element);
        if (idx >= 0)
            _entries.RemoveAt(idx);
    }

    public void AppendMarker()
    {
        _entries.Add(new Entry { StackItem = null });
    }

    public void SetEntry(int index, Entry entry) => _entries[index] = entry;

    public void ClearToLastMarker()
    {
        while (_entries.Count > 0)
        {
            bool shouldStop = _entries[_entries.Count - 1].IsMarker;
            _entries.RemoveAt(_entries.Count - 1);
            if (shouldStop) break;
        }
    }
}