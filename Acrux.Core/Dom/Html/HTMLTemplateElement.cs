namespace Acrux.Core.Dom.Html;

public class HTMLTemplateElement : HtmlElement
{
    public HTMLTemplateElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "template") { }

    public DocumentFragment Content { get; } = new DocumentFragment();
    public ShadowMode ShadowRootMode { get; set; } = ShadowMode.Open;
    public bool ShadowRootDelegatesFocus { get; set; }
    public bool ShadowRootClonable { get; set; }
    public bool ShadowRootSerializable { get; set; }
}


