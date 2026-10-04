using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;

namespace Acrux.Core.Layout;

public class LayoutIFrame : LayoutEmbeddedContent
{
    public LayoutIFrame(HTMLIFrameElement? element) : base(element)
    {
    }

    public override string GetName() => "LayoutIFrame";

    public override bool IsLayoutIFrame => true;
}