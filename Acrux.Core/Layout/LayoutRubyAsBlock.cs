using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

public class LayoutRubyAsBlock : LayoutBlockFlow
{
    public LayoutRubyAsBlock(Element? element) : base(element)
    {
    }

    public override string GetName() => "LayoutRubyAsBlock";

    public override bool IsRuby => true;

    public override void AddChild(LayoutObject child, LayoutObject? beforeChild = null)
    {
        AddChild(child);
    }

    public override void StyleDidChange(StyleDifference diff, ComputedStyle? oldStyle)
    {
    }

    public override void RemoveLeftoverAnonymousBlock(LayoutBlock? block)
    {
    }
}