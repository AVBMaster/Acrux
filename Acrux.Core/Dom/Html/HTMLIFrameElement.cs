namespace Acrux.Core.Dom.Html;

public class HTMLIFrameElement : HtmlElement
{
    public HTMLIFrameElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "iframe") { }

    public string? Src { get => GetAttribute("src"); set => SetAttribute("src", value); }
    public string? Srcdoc { get => GetAttribute("srcdoc"); set => SetAttribute("srcdoc", value); }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public string? Sandbox { get => GetAttribute("sandbox"); set => SetAttribute("sandbox", value); }
    public bool AllowFullscreen { get => HasAttribute("allowfullscreen"); set => SetBoolAttr("allowfullscreen", value); }
    public bool AllowPaymentRequest { get => HasAttribute("allowpaymentrequest"); set => SetBoolAttr("allowpaymentrequest", value); }
    public string? Allow { get => GetAttribute("allow"); set => SetAttribute("allow", value); }
    public string? Loading { get => GetAttribute("loading") ?? "eager"; set => SetAttribute("loading", value); }
    public string? ReferrerPolicy { get => GetAttribute("referrerpolicy"); set => SetAttribute("referrerpolicy", value); }
    public string? Importance { get => GetAttribute("importance"); set => SetAttribute("importance", value); }
    public string? Csp { get => GetAttribute("csp"); set => SetAttribute("csp", value); }
    public string? Credentialless { get => GetAttribute("credentialless"); set => SetAttribute("credentialless", value); }
    public ulong Width { get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 300; set => SetAttribute("width", value.ToString()); }
    public ulong Height { get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 150; set => SetAttribute("height", value.ToString()); }

    public Document? ContentDocument { get; set; }
    public WindowProxy? ContentWindow { get; set; }

    public Document? GetSVGDocument() => ContentDocument;

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}

public class WindowProxy { }


