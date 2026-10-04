using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;

namespace Acrux.Core.Layout;

public class LayoutWordBreak : LayoutText
{
    public LayoutWordBreak(HtmlElement node) : base(node, "\u200B")
    {
    }

    public Position? PositionForCaretOffset(uint offset) => null;

    public uint? CaretOffsetForPosition(Position? position) => null;

    public override string GetName() => "LayoutWordBreak";

    public override bool IsWordBreak => true;
}