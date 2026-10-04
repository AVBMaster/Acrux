namespace Acrux.Core.Dom.Html;

public class HTMLImageElement : HtmlElement
{
    public HTMLImageElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "img") { }

    public string? Alt { get => GetAttribute("alt"); set => SetAttribute("alt", value); }
    public string? Src { get => GetAttribute("src"); set => SetAttribute("src", value); }
    public string? Srcset { get => GetAttribute("srcset"); set => SetAttribute("srcset", value); }
    public string? Sizes { get => GetAttribute("sizes"); set => SetAttribute("sizes", value); }
    public string? CrossOrigin { get => GetAttribute("crossorigin"); set => SetAttribute("crossorigin", value); }
    public string? UseMap { get => GetAttribute("usemap"); set => SetAttribute("usemap", value); }
    public bool IsMap { get => HasAttribute("ismap"); set => SetBoolAttr("ismap", value); }
    public ulong Width { get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 0; set => SetAttribute("width", value.ToString()); }
    public ulong Height { get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 0; set => SetAttribute("height", value.ToString()); }
    public ulong NaturalWidth { get; set; }
    public ulong NaturalHeight { get; set; }
    public bool Complete { get; set; }
    public string? CurrentSrc { get; set; }
    public string? ReferrerPolicy { get => GetAttribute("referrerpolicy"); set => SetAttribute("referrerpolicy", value); }
    public string? Decoding { get => GetAttribute("decoding") ?? "auto"; set => SetAttribute("decoding", value); }
    public string? Loading { get => GetAttribute("loading") ?? "eager"; set => SetAttribute("loading", value); }
    public string? FetchPriority { get => GetAttribute("fetchpriority"); set => SetAttribute("fetchpriority", value); }

    public long X => 0;
    public long Y => 0;

    public object? Decode() => null;

    public static HTMLImageElement Create(Document document, ulong? width = null, ulong? height = null)
    {
        var img = new HTMLImageElement(document);
        if (width.HasValue) img.Width = width.Value;
        if (height.HasValue) img.Height = height.Value;
        return img;
    }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}


