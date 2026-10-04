using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

public class LayoutFrame : LayoutEmbeddedContent
{
    public LayoutFrame(HtmlElement? element) : base(element)
    {
    }

    public override void ImageChanged(WrappedImagePtr? image, CanDeferInvalidation defer)
    {
    }

    public override string GetName() => "LayoutFrame";

    public override bool IsFrame => true;
}