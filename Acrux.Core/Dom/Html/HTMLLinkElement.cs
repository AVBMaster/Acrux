using Acrux.Core.Dom.Cssom;

namespace Acrux.Core.Dom.Html;

public class HTMLLinkElement : HtmlElement
{
    public HTMLLinkElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "link") { }

    public string? Href { get => GetAttribute("href"); set => SetAttribute("href", value); }
    public string? Rel { get => GetAttribute("rel"); set => SetAttribute("rel", value); }
    public string? As { get => GetAttribute("as"); set => SetAttribute("as", value); }
    public string? CrossOrigin { get => GetAttribute("crossorigin"); set => SetAttribute("crossorigin", value); }
    public string? Type { get => GetAttribute("type"); set => SetAttribute("type", value); }
    public string? Media { get => GetAttribute("media"); set => SetAttribute("media", value); }
    public string? Hreflang { get => GetAttribute("hreflang"); set => SetAttribute("hreflang", value); }
    public string? Title { get => GetAttribute("title"); set => SetAttribute("title", value); }
    public string? Sizes { get => GetAttribute("sizes"); set => SetAttribute("sizes", value); }
    public string? ReferrerPolicy { get => GetAttribute("referrerpolicy"); set => SetAttribute("referrerpolicy", value); }
    public string? FetchPriority { get => GetAttribute("fetchpriority"); set => SetAttribute("fetchpriority", value); }
    public string? ImageSrcset { get => GetAttribute("imagesrcset"); set => SetAttribute("imagesrcset", value); }
    public string? ImageSizes { get => GetAttribute("imagesizes"); set => SetAttribute("imagesizes", value); }
    public string? Blocking { get => GetAttribute("blocking"); set => SetAttribute("blocking", value); }
    public string? Importance { get => GetAttribute("importance"); set => SetAttribute("importance", value); }
    public string? ColorScheme { get => GetAttribute("colorscheme"); set => SetAttribute("colorscheme", value); }
    public string? Integrity { get => GetAttribute("integrity"); set => SetAttribute("integrity", value); }

    public StyleSheet? Sheet { get; set; }
    public DOMTokenList RelList => new(Rel);
}


