using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutEmbeddedContent : LayoutReplaced
{
    public LayoutEmbeddedContent(HtmlElement? element) : base(element)
    {
    }

    public bool NodeAtPoint(HitTestResult? result, HitTestLocation? location, PhysicalOffset accumulatedOffset, HitTestPhase phase) => false;

    public LayoutView? ChildLayoutView() => null;

    public WebPluginContainerImpl? Plugin() => null;

    public EmbeddedContentView? GetEmbeddedContentView() => null;

    public PhysicalOffset EmbeddedContentFromBorderBox(PhysicalOffset offset) => offset;

    public PhysicalOffset BorderBoxFromEmbeddedContent(PhysicalOffset offset) => offset;

    public PhysicalRect ReplacedContentRectFrom(PhysicalRect baseContentRect) => baseContentRect;

    public void UpdateOnEmbeddedContentViewChange()
    {
    }

    public void UpdateGeometry(EmbeddedContentView? view)
    {
    }

    public override bool IsLayoutEmbeddedContent => true;

    public bool IsThrottledFrameView() => false;

    public virtual PhysicalSize? FrozenFrameSize() => null;

    public AffineTransform EmbeddedContentTransform() => new();

    public override PaintLayerType LayerTypeRequired() => PaintLayerType.NoPaintLayer;

    public override void StyleDidChange(StyleDifference diff, ComputedStyle? oldStyle)
    {
    }

    public override void PaintReplaced(PaintInfo? paintInfo, PhysicalOffset paintOffset)
    {
    }

    public CursorDirective GetCursor(PhysicalOffset offset, Cursor? cursor) => CursorDirective.SetCursorBasedOnStyle;

    public override bool CanBeSelectionLeafInternal => true;

    public HtmlElement? GetFrameOwnerElement() => Node as HtmlElement;

    public override void WillBeDestroyed()
    {
    }

    private bool NodeAtPointOverEmbeddedContentView(HitTestResult? result, HitTestLocation? location, PhysicalOffset accumulatedOffset, HitTestPhase phase) => false;

    private bool PointOverResizer(HitTestResult? result, HitTestLocation? location, PhysicalOffset accumulatedOffset) => false;

    private void PropagateZoomFactor(double zoomFactor)
    {
    }
}