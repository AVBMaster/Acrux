using Acrux.Core.Dom;

namespace Acrux.Core.Layout.List;

/// <summary>
/// A LayoutObject subclass for outside-positioned list markers in the modern layout pipeline.
/// Mirrors layout_outside_list_marker.h/.cc.
/// </summary>
public class LayoutOutsideListMarker : LayoutBlockFlow
{
    private readonly ListMarker _listMarker = new();

    public LayoutOutsideListMarker(Element? element) : base(element)
    {
    }

    public override string GetName() => "LayoutOutsideListMarker";
    public override bool IsLayoutOutsideListMarker => true;
    public override bool IsMonolithic => true;

    public ListMarker Marker() => _listMarker;

    public void WillCollectInlines()
    {
        _listMarker.UpdateMarkerTextIfNeeded(this);
    }

    public bool NeedsOccupyWholeLine()
    {
        var doc = GetDocument();
        if (doc == null || !doc.InQuirksMode())
            return false;

        LayoutObject? nextSibling = NextSibling;
        if (nextSibling != null && !nextSibling.IsInline && !nextSibling.IsFloatingOrOutOfFlowPositioned()
            && nextSibling.Node is Element nextEl
            && (nextEl.TagName == "UL" || nextEl.TagName == "OL"))
            return true;

        return false;
    }

    private Document? GetDocument()
    {
        return (Node as Element)?.OwnerDocument;
    }
}

/// <summary>
/// Extensions for LayoutObject quirks mode checks. Mirrors
/// Document::InQuirksMode() and LayoutObject helpers.
/// </summary>
public static class LayoutObjectQuirksExtensions
{
    public static bool IsFloatingOrOutOfFlowPositioned(this LayoutObject obj)
    {
        return obj.IsFloating || obj.IsOutOfFlowPositioned;
    }

    public static bool InQuirksMode(this Document? doc)
    {
        return false;
    }
}