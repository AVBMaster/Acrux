using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutFrameSet : LayoutBlock
{
    public LayoutFrameSet(Element? element) : base(element)
    {
    }

    public override string GetName() => "LayoutFrameSet";

    public override bool IsFrameSet => true;

    public override bool IsChildAllowed(LayoutObject? child, ComputedStyle style) => true;

    public override void AddChild(LayoutObject child, LayoutObject? beforeChild = null)
    {
        AddChild(child);
    }

    public override void RemoveChild(LayoutObject child)
    {
        base.RemoveChild(child);
    }

    public CursorDirective GetCursor(PhysicalOffset point, Cursor? cursor) => CursorDirective.SetCursorBasedOnStyle;
}