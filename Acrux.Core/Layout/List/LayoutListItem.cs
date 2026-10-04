using Acrux.Core.Dom;

namespace Acrux.Core.Layout.List;

/// <summary>
/// A LayoutObject subclass for 'display: list-item' in the modern layout pipeline.
/// Mirrors layout_list_item.h/.cc.
/// </summary>
public class LayoutListItem : LayoutBlockFlow
{
    private readonly ListItemOrdinal _ordinal = new();

    public LayoutListItem(Element? element) : base(element)
    {
    }

    public override string GetName() => "LayoutListItem";
    public override bool IsLayoutListItem => true;

    public ListItemOrdinal Ordinal() => _ordinal;

    internal bool OrdinalDirty
    {
        get => false;
        set { if (value) _ordinal.SetDirty(); }
    }

    public int Value()
    {
        return _ordinal.Value(Node!);
    }

    public LayoutObject? Marker()
    {
        return (Node as Element)?.MarkerLayoutObject as LayoutObject;
    }

    public void UpdateMarkerTextIfNeeded()
    {
        var marker = Marker();
        if (ListMarker.Get(marker) is { } listMarker)
            listMarker.UpdateMarkerTextIfNeeded(marker!);
    }

    public void UpdateCounterStyle()
    {
        var style = Style;
        if (style == null || !style.GeneratesCounterStyle()) return;

        var marker = Marker();
        if (ListMarker.Get(marker) is { } listMarker)
            listMarker.CounterStyleChanged(marker!);
    }

    public void OrdinalValueChanged()
    {
        var marker = Marker();
        if (ListMarker.Get(marker) is { } listMarker)
            listMarker.OrdinalValueChanged(marker!);
    }

    public void WillCollectInlines()
    {
        UpdateMarkerTextIfNeeded();
    }

    public void StyleDidChange(ComputedStyle? oldStyle)
    {
        var marker = Marker();
        var listMarker = ListMarker.Get(marker);
        if (listMarker == null) return;

        listMarker.UpdateMarkerContentIfNeeded(marker!);

        if (oldStyle != null)
        {
            var oldListStyleType = oldStyle.ListStyleType;
            var newListStyleType = Style?.ListStyleType;
            if (oldListStyleType != newListStyleType)
                listMarker.ListStyleTypeChanged(marker!);
        }
    }

    public void SubtreeDidChange()
    {
        var marker = Marker();
        if (ListMarker.Get(marker) is { } listMarker)
            listMarker.UpdateMarkerContentIfNeeded(marker!);
    }

    public static LayoutObject? FindSymbolMarkerLayoutText(LayoutObject? obj)
    {
        if (obj == null) return null;

        if (ListMarker.Get(obj) is { } listMarker)
            return listMarker.SymbolMarkerLayoutText(obj);

        if (obj.IsLayoutListItem)
            return FindSymbolMarkerLayoutText(((LayoutListItem)obj).Marker());

        if (obj is LayoutInlineListItem inlineListItem)
            return FindSymbolMarkerLayoutText(inlineListItem.Marker());

        if (obj.IsAnonymousBlock)
            return FindSymbolMarkerLayoutText(GetLayoutObjectForParentNode(obj));

        return null;
    }

    private static LayoutObject? GetLayoutObjectForParentNode(LayoutObject obj)
    {
        var parent = obj.Parent;
        return parent;
    }
}