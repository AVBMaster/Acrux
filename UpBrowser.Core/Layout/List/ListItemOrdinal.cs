using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout.List;

public class ListItemOrdinal
{
    private int? _explicitValue;
    private int _cachedValue = 1;
    private bool _dirty = true;

    public int Value(Node node)
    {
        if (_dirty)
        {
            RecalcValue(node);
            _dirty = false;
        }
        return _cachedValue;
    }

    public void SetExplicit(int value)
    {
        _explicitValue = value;
        _dirty = true;
    }

    public void Reset()
    {
        _explicitValue = null;
        _dirty = true;
    }

    public static void ItemInsertedOrRemoved(LayoutObject item)
    {
        var parent = item.Parent;
        if (parent == null) return;
        MarkDirty(parent);
        foreach (var child in parent.Children)
            MarkDirty(child);
    }

    private static void MarkDirty(LayoutObject obj)
    {
        if (obj is LayoutListItem li)
            li.OrdinalDirty = true;
        else if (obj is LayoutInlineListItem inlineLi)
            inlineLi.OrdinalDirty = true;
    }

    internal void SetDirty() => _dirty = true;

    private void RecalcValue(Node node)
    {
        if (_explicitValue.HasValue)
        {
            _cachedValue = _explicitValue.Value;
            return;
        }

        var self = node as Element;
        var parent = self?.ParentElement;
        if (parent == null) { _cachedValue = 1; return; }

        // Collect the list-item siblings in document order.
        var items = new System.Collections.Generic.List<Element>();
        foreach (var child in parent.Children)
            if (child is Element el && el.ComputedStyle?.Display == DisplayType.ListItem)
                items.Add(el);

        int selfIndex = items.IndexOf(self!);
        if (selfIndex < 0) { _cachedValue = 1; return; }

        // HTML list numbering: <ol start> sets the first ordinal, <ol reversed>
        // counts down (default start = number of items), and a per-item 'value'
        // attribute overrides that item's ordinal and reseeds the running count.
        bool reversed = parent.HasAttribute("reversed");
        int increment = reversed ? -1 : 1;
        int running = ParseInt(parent.GetAttribute("start"))
                      ?? (reversed ? items.Count : 1);

        for (int i = 0; i < items.Count; i++)
        {
            int v = ParseInt(items[i].GetAttribute("value")) ?? running;
            if (i == selfIndex) { _cachedValue = v; return; }
            running = v + increment;
        }
        _cachedValue = 1;
    }

    private static int? ParseInt(string? s) =>
        int.TryParse(s?.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
}