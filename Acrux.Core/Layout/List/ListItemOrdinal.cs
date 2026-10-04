using Acrux.Core.Dom;

namespace Acrux.Core.Layout.List;

public class ListItemOrdinal
{
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

    public void Reset()
    {
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
        _cachedValue = node as Element switch
        {
            null => 1,
            var self => ListItemNumbering.Ordinal(self),
        };
    }
}