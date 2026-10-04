using Acrux.Core.Dom.Cssom;

namespace Acrux.Core.Dom.Html;

public class HTMLStyleElement : HtmlElement
{
    public HTMLStyleElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "style") { }

    public string? Media { get => GetAttribute("media"); set => SetAttribute("media", value); }
    public string? Type { get => GetAttribute("type") ?? "text/css"; set => SetAttribute("type", value); }
    public string? Blocking { get => GetAttribute("blocking"); set => SetAttribute("blocking", value); }
    public string? Nonce { get => GetAttribute("nonce"); set => SetAttribute("nonce", value); }
    public string? Title { get => GetAttribute("title"); set => SetAttribute("title", value); }
    public bool Scoped { get => HasAttribute("scoped"); set => SetBoolAttr("scoped", value); }

    public CSSStyleSheet? Sheet { get; set; }

    public bool IsDisabled { get; set; }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}


