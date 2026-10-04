using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutMedia : LayoutReplaced
{
    private readonly List<LayoutObject> _children = new();

    public LayoutMedia(HTMLMediaElement? element) : base(element)
    {
    }

    public LayoutObject? FirstChild => _children.Count > 0 ? _children[0] : null;
    public LayoutObject? LastChild => _children.Count > 0 ? _children[^1] : null;

    public override List<LayoutObject> Children => _children;

    public HTMLMediaElement? MediaElement() => Node as HTMLMediaElement;

    public override string GetName() => "LayoutMedia";

    public float ComputePanelWidth(PhysicalRect mediaWidth) => 0;

    public override bool IsMedia => true;
    public override bool IsImage => false;
    public override bool CanHaveChildren => true;

    public override bool IsChildAllowed(LayoutObject? child, ComputedStyle style) => true;

    public override void PaintReplaced(PaintInfo? paintInfo, PhysicalOffset paintOffset)
    {
    }

    public override bool BackgroundShouldAlwaysBeClipped => false;

    public override void RecalcScrollableOverflow()
    {
    }
}