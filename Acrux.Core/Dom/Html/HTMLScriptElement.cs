namespace Acrux.Core.Dom.Html;

public class HTMLScriptElement : HtmlElement
{
    public HTMLScriptElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "script") { }

    public string? Src { get => GetAttribute("src"); set => SetAttribute("src", value); }
    public string? Type { get => GetAttribute("type") ?? "text/javascript"; set => SetAttribute("type", value); }
    public string? Charset { get => GetAttribute("charset"); set => SetAttribute("charset", value); }
    public string? Language { get => GetAttribute("language"); set => SetAttribute("language", value); }
    public bool Defer { get => HasAttribute("defer"); set => SetBoolAttr("defer", value); }
    public bool Async { get => HasAttribute("async"); set => SetBoolAttr("async", value); }
    public string? CrossOrigin { get => GetAttribute("crossorigin"); set => SetAttribute("crossorigin", value); }
    public string? Text
    {
        get => TextContent ?? "";
        set => TextContent = value;
    }
    public string? Integrity { get => GetAttribute("integrity"); set => SetAttribute("integrity", value); }
    public string? ReferrerPolicy { get => GetAttribute("referrerpolicy"); set => SetAttribute("referrerpolicy", value); }
    public string? FetchPriority { get => GetAttribute("fetchpriority"); set => SetAttribute("fetchpriority", value); }
    public string? NoModule { get => GetAttribute("nomodule"); set => SetAttribute("nomodule", value); }
    public string? Blocking { get => GetAttribute("blocking"); set => SetAttribute("blocking", value); }
    public bool? Event { get; set; }
    public bool? HtmlFor { get; set; }

    public bool IsUrlAttribute() => true;

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}


