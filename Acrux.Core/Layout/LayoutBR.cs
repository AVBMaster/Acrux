using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutBR : LayoutText
{
    public LayoutBR(HTMLBRElement node) : base(node, "\n")
    {
    }

    public override string GetName() => "LayoutBR";

    public override bool IsBR => true;

    public override int CaretMinOffset() => 0;
    public override int CaretMaxOffset() => 1;

    public PositionWithAffinity? PositionForPoint(PhysicalOffset point) => null;

    public Position? PositionForCaretOffset(uint offset) => null;

    public uint? CaretOffsetForPosition(Position? position) => null;

    protected override uint NonCollapsedCaretMaxOffset() => 1;
}