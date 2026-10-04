using Acrux.Core.Dom;

namespace Acrux.Core.Layout.List;

/// <summary>
/// A LayoutObject subclass for inside-positioned list markers in the modern layout pipeline.
/// Mirrors layout_inside_list_marker.h/.cc.
/// </summary>
public class LayoutInsideListMarker : LayoutInline
{
    private readonly ListMarker _listMarker = new();

    public LayoutInsideListMarker(Element? element) : base(element)
    {
    }

    public override string GetName() => "LayoutInsideListMarker";
    public override bool IsLayoutInsideListMarker => true;

    public ListMarker Marker() => _listMarker;

    public DocumentLifecycle GetDocument() => new();
}

/// <summary>
/// Minimal lifecycle state enum. Mirrors DocumentLifecycle in document_lifecycle.h.
/// </summary>
public enum DocumentLifecycle
{
    Uninitialized,
    InStyleRecalc,
    StyleClean,
    InPerformLayout,
    Clean,
    InPreLayout,
    PreLayoutClean,
    InPostLayout,
    AfterPerformLayout,
    InPaintInvalidation,
    InPrePaint,
    PrePaintClean,
    InPaint,
    PaintClean,
    InCompositingUpdate,
    CompositingClean,
    InVisualUpdate,
    VisualUpdateClean,
    InDisposal,
    Stopped,
}