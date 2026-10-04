namespace Acrux.Core.Dom.Html;

public class HTMLAnchorElement : HtmlElement
{
    public HTMLAnchorElement(Document? document, string? name = null)
        : base(name ?? "a") { }

    public string? Charset { get => GetAttribute("charset"); set => SetAttribute("charset", value); }
    public string? Coords { get => GetAttribute("coords"); set => SetAttribute("coords", value); }
    public string? Download { get => GetAttribute("download"); set => SetAttribute("download", value); }
    public string? Hash => new Uri(Href ?? "").Fragment;
    public string? Host => new Uri(Href ?? "").Host;
    public string? Hostname => new Uri(Href ?? "").Host;
    public string? Href { get => GetAttribute("href"); set => SetAttribute("href", value); }
    public string? Hreflang { get => GetAttribute("hreflang"); set => SetAttribute("hreflang", value); }
    public string? Origin => new Uri(Href ?? "").GetLeftPart(UriPartial.Authority);
    public string? Password { get; set; }
    public string? Pathname => new Uri(Href ?? "").AbsolutePath;
    public string? Ping { get => GetAttribute("ping"); set => SetAttribute("ping", value); }
    public string? Port => new Uri(Href ?? "").Port.ToString();
    public string? Protocol => new Uri(Href ?? "").Scheme + ":";
    public string? ReferrerPolicy { get => GetAttribute("referrerpolicy"); set => SetAttribute("referrerpolicy", value); }
    public string? Rel { get => GetAttribute("rel"); set => SetAttribute("rel", value); }
    public DOMTokenList RelList => new(Rel);
    public string? Search => new Uri(Href ?? "").Query;
    public string? Shape { get => GetAttribute("shape"); set => SetAttribute("shape", value); }
    public string? Target { get => GetAttribute("target"); set => SetAttribute("target", value); }
    public string? Text { get => TextContent; set => TextContent = value; }
    public string? Type { get => GetAttribute("type"); set => SetAttribute("type", value); }
    public string? Username { get; set; }

    public void Click() { }
}

public class HTMLButtonElement : HtmlElement
{
    public HTMLButtonElement(Document? document, string? name = null)
        : base(name ?? "button") { }

    public bool Autofocus { get => HasAttribute("autofocus"); set => SetBoolAttr("autofocus", value); }
    public bool Disabled { get => HasAttribute("disabled"); set => SetBoolAttr("disabled", value); }
    public HTMLFormElement? Form { get; set; }
    public string? FormAction { get => GetAttribute("formaction"); set => SetAttribute("formaction", value); }
    public string? FormEnctype { get => GetAttribute("formenctype"); set => SetAttribute("formenctype", value); }
    public string? FormMethod { get => GetAttribute("formmethod"); set => SetAttribute("formmethod", value); }
    public bool FormNoValidate { get => HasAttribute("formnovalidate"); set => SetBoolAttr("formnovalidate", value); }
    public string? FormTarget { get => GetAttribute("formtarget"); set => SetAttribute("formtarget", value); }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public string? Type { get => GetAttribute("type") ?? "submit"; set => SetAttribute("type", value); }
    public string? Value { get => GetAttribute("value"); set => SetAttribute("value", value); }
    public string? LabelText { get => TextContent; set => TextContent = value; }

    public void Click() { }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}

public class HTMLLabelElement : HtmlElement
{
    public HTMLLabelElement(Document? document, string? name = null)
        : base(name ?? "label") { }

    public string? HtmlFor { get => GetAttribute("for"); set => SetAttribute("for", value); }
    public HtmlElement? Control { get; set; }
    public HTMLFormElement? Form { get; set; }
}

public class HTMLLIElement : HtmlElement
{
    public HTMLLIElement(Document? document, string? name = null)
        : base(name ?? "li") { }

    public int Value { get => int.TryParse(GetAttribute("value"), out var v) ? v : 0; set => SetAttribute("value", value.ToString()); }
    public string? Type { get => GetAttribute("type"); set => SetAttribute("type", value); }
}

public class HTMLOptionElement : HtmlElement
{
    public HTMLOptionElement(Document? document, string? name = null)
        : base(name ?? "option") { }

    public bool DefaultSelected { get => HasAttribute("selected"); set => SetBoolAttr("selected", value); }
    public bool Disabled { get => HasAttribute("disabled"); set => SetBoolAttr("disabled", value); }
    public HTMLFormElement? Form { get; set; }
    public string? Label { get => GetAttribute("label"); set => SetAttribute("label", value); }
    public bool Selected { get; set; }
    public string? Text { get => TextContent; set => TextContent = value; }
    public string? Value { get => GetAttribute("value") ?? Text ?? ""; set => SetAttribute("value", value); }
    public int Index { get; set; } = -1;

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}

public class HTMLBRElement : HtmlElement
{
    public HTMLBRElement(Document? document, string? name = null) : base(name ?? "br") { }
}

public class HTMLHeadingElement : HtmlElement
{
    public HTMLHeadingElement(Document? document, string? name = null) : base(name ?? "h1") { }
}

public class HTMLParagraphElement : HtmlElement
{
    public HTMLParagraphElement(Document? document, string? name = null) : base(name ?? "p") { }
}

public class HTMLPreElement : HtmlElement
{
    public HTMLPreElement(Document? document, string? name = null) : base(name ?? "pre") { }
}

public class HTMLUListElement : HtmlElement
{
    public HTMLUListElement(Document? document, string? name = null) : base(name ?? "ul") { }
}

public class HTMLOListElement : HtmlElement
{
    public HTMLOListElement(Document? document, string? name = null) : base(name ?? "ol") { }
}

public class HTMLTableElement : HtmlElement
{
    public HTMLTableElement(Document? document, string? name = null) : base(name ?? "table") { }

    public HTMLTableCaptionElement? Caption { get; set; }
    public HTMLTableSectionElement? THead { get; set; }
    public HTMLTableSectionElement? TFoot { get; set; }
    public HtmlCollection Rows { get; set; } = new();
    public HtmlCollection TBodies { get; set; } = new();

    public HtmlElement? CreateTHead() => null;
    public void DeleteTHead() { }
    public HtmlElement? CreateTFoot() => null;
    public void DeleteTFoot() { }
    public HtmlElement? CreateTBody() => null;
    public HtmlElement? CreateCaption() => null;
    public void DeleteCaption() { }
    public HtmlElement? InsertRow(int index = -1) => null;
    public void DeleteRow(int index) { }
}

public class HTMLTableCaptionElement : HtmlElement
{
    public HTMLTableCaptionElement(Document? document, string? name = null) : base(name ?? "caption") { }
}

public class HTMLTableColElement : HtmlElement
{
    public HTMLTableColElement(Document? document, string? name = null) : base(name ?? "col") { }
    public int Span { get => int.TryParse(GetAttribute("span"), out var v) ? v : 1; set => SetAttribute("span", value.ToString()); }
}

public class HTMLTableSectionElement : HtmlElement
{
    public HTMLTableSectionElement(Document? document, string? name = null) : base(name ?? "tbody") { }
    public HtmlCollection Rows { get; set; } = new();
    public HtmlElement? InsertRow(int index = -1) => null;
    public void DeleteRow(int index) { }
}

public class HTMLTableRowElement : HtmlElement
{
    public HTMLTableRowElement(Document? document, string? name = null) : base(name ?? "tr") { }
    public int RowIndex { get; set; } = -1;
    public int SectionRowIndex { get; set; } = -1;
    public HtmlCollection Cells { get; set; } = new();
    public HtmlElement? InsertCell(int index = -1) => null;
    public void DeleteCell(int index) { }
}

public class HTMLTableCellElement : HtmlElement
{
    public HTMLTableCellElement(Document? document, string? name = null) : base(name ?? "td") { }
    public int ColSpan { get => int.TryParse(GetAttribute("colspan"), out var v) ? v : 1; set => SetAttribute("colspan", value.ToString()); }
    public int RowSpan { get => int.TryParse(GetAttribute("rowspan"), out var v) ? v : 1; set => SetAttribute("rowspan", value.ToString()); }
    public int CellIndex { get; set; } = -1;
    public string? Abbr { get => GetAttribute("abbr"); set => SetAttribute("abbr", value); }
    public string? Headers { get => GetAttribute("headers"); set => SetAttribute("headers", value); }
    public string? Scope { get => GetAttribute("scope"); set => SetAttribute("scope", value); }
}

public class HTMLModElement : HtmlElement
{
    public HTMLModElement(Document? document, string? name = null) : base(name ?? "ins") { }
    public string? Cite { get => GetAttribute("cite"); set => SetAttribute("cite", value); }
    public string? DateTime { get => GetAttribute("datetime"); set => SetAttribute("datetime", value); }
}

public class HTMLQuoteElement : HtmlElement
{
    public HTMLQuoteElement(Document? document, string? name = null) : base(name ?? "blockquote") { }
    public string? Cite { get => GetAttribute("cite"); set => SetAttribute("cite", value); }
}

public class HTMLDivElement : HtmlElement
{
    public HTMLDivElement(Document? document, string? name = null) : base(name ?? "div") { }
}

public class HTMLSpanElement : HtmlElement
{
    public HTMLSpanElement(Document? document, string? name = null) : base(name ?? "span") { }
}

public class HTMLHRElement : HtmlElement
{
    public HTMLHRElement(Document? document, string? name = null) : base(name ?? "hr") { }
}

public class HTMLEmbedElement : HtmlElement
{
    public HTMLEmbedElement(Document? document, string? name = null) : base(name ?? "embed") { }
    public string? Src { get => GetAttribute("src"); set => SetAttribute("src", value); }
    public string? Type { get => GetAttribute("type"); set => SetAttribute("type", value); }
    public ulong Width { get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 0; set => SetAttribute("width", value.ToString()); }
    public ulong Height { get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 0; set => SetAttribute("height", value.ToString()); }
}

public class HTMLObjectElement : HtmlElement
{
    public HTMLObjectElement(Document? document, string? name = null) : base(name ?? "object") { }
    public string? Data { get => GetAttribute("data"); set => SetAttribute("data", value); }
    public string? Type { get => GetAttribute("type"); set => SetAttribute("type", value); }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public string? ClassId { get => GetAttribute("classid"); set => SetAttribute("classid", value); }
    public ulong Width { get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 0; set => SetAttribute("width", value.ToString()); }
    public ulong Height { get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 0; set => SetAttribute("height", value.ToString()); }
}

public class HTMLParamElement : HtmlElement
{
    public HTMLParamElement(Document? document, string? name = null) : base(name ?? "param") { }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public string? Value { get => GetAttribute("value"); set => SetAttribute("value", value); }
}

public class HTMLSourceElement : HtmlElement
{
    public HTMLSourceElement(Document? document, string? name = null) : base(name ?? "source") { }
    public string? Src { get => GetAttribute("src"); set => SetAttribute("src", value); }
    public string? Type { get => GetAttribute("type"); set => SetAttribute("type", value); }
    public string? Srcset { get => GetAttribute("srcset"); set => SetAttribute("srcset", value); }
    public string? Sizes { get => GetAttribute("sizes"); set => SetAttribute("sizes", value); }
    public string? Media { get => GetAttribute("media"); set => SetAttribute("media", value); }
    public ulong Width { get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 0; set => SetAttribute("width", value.ToString()); }
    public ulong Height { get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 0; set => SetAttribute("height", value.ToString()); }
}

public class HTMLTrackElement : HtmlElement
{
    public HTMLTrackElement(Document? document, string? name = null) : base(name ?? "track") { }
    public string? Kind { get => GetAttribute("kind"); set => SetAttribute("kind", value); }
    public string? Src { get => GetAttribute("src"); set => SetAttribute("src", value); }
    public string? Srclang { get => GetAttribute("srclang"); set => SetAttribute("srclang", value); }
    public string? Label { get => GetAttribute("label"); set => SetAttribute("label", value); }
    public bool Default { get => HasAttribute("default"); set => SetBoolAttr("default", value); }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}

public class HTMLMapElement : HtmlElement
{
    public HTMLMapElement(Document? document, string? name = null) : base(name ?? "map") { }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public HtmlCollection Areas { get; set; } = new();
}

public class HTMLAreaElement : HtmlElement
{
    public HTMLAreaElement(Document? document, string? name = null) : base(name ?? "area") { }
    public string? Alt { get => GetAttribute("alt"); set => SetAttribute("alt", value); }
    public string? Coords { get => GetAttribute("coords"); set => SetAttribute("coords", value); }
    public string? Download { get => GetAttribute("download"); set => SetAttribute("download", value); }
    public string? Href { get => GetAttribute("href"); set => SetAttribute("href", value); }
    public string? Rel { get => GetAttribute("rel"); set => SetAttribute("rel", value); }
    public string? Shape { get => GetAttribute("shape"); set => SetAttribute("shape", value); }
    public string? Target { get => GetAttribute("target"); set => SetAttribute("target", value); }
}

public class HTMLProgressElement : HtmlElement
{
    public HTMLProgressElement(Document? document, string? name = null) : base(name ?? "progress") { }
    public double Value { get => double.TryParse(GetAttribute("value"), out var v) ? v : 0; set => SetAttribute("value", value.ToString("0.0")); }
    public double Max { get => double.TryParse(GetAttribute("max"), out var v) ? v : 1; set => SetAttribute("max", value.ToString("0.0")); }
    public double Position => Max > 0 ? Value / Max : 0;
    public NodeList? Labels { get; set; }
}

public class HTMLMeterElement : HtmlElement
{
    public HTMLMeterElement(Document? document, string? name = null) : base(name ?? "meter") { }
    public double Value { get => double.TryParse(GetAttribute("value"), out var v) ? v : 0; set => SetAttribute("value", value.ToString("0.0")); }
    public double Min { get => double.TryParse(GetAttribute("min"), out var v) ? v : 0; set => SetAttribute("min", value.ToString("0.0")); }
    public double Max { get => double.TryParse(GetAttribute("max"), out var v) ? v : 1; set => SetAttribute("max", value.ToString("0.0")); }
    public double Low { get => double.TryParse(GetAttribute("low"), out var v) ? v : Min; set => SetAttribute("low", value.ToString("0.0")); }
    public double High { get => double.TryParse(GetAttribute("high"), out var v) ? v : Max; set => SetAttribute("high", value.ToString("0.0")); }
    public double Optimum { get => double.TryParse(GetAttribute("optimum"), out var v) ? v : (Max - Min) / 2; set => SetAttribute("optimum", value.ToString("0.0")); }
    public NodeList? Labels { get; set; }
}

public class HTMLFieldSetElement : HtmlElement
{
    public HTMLFieldSetElement(Document? document, string? name = null) : base(name ?? "fieldset") { }
    public bool Disabled { get => HasAttribute("disabled"); set => SetBoolAttr("disabled", value); }
    public HTMLFormElement? Form { get; set; }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public HtmlCollection Elements => new(ChildNodes.OfType<Element>().ToList());
    public string? ValidationMessage => "";
    public bool WillValidate => false;
    public ValidityState Validity => new();
    public bool CheckValidity() => true;
    public bool ReportValidity() => true;
    public void SetCustomValidity(string error) { }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}

public class HTMLLegendElement : HtmlElement
{
    public HTMLLegendElement(Document? document, string? name = null) : base(name ?? "legend") { }
    public HTMLFormElement? Form { get; set; }
}

public class HTMLOutputElement : HtmlElement
{
    public HTMLOutputElement(Document? document, string? name = null) : base(name ?? "output") { }
    public string? HtmlFor { get => GetAttribute("for"); set => SetAttribute("for", value); }
    public HTMLFormElement? Form { get; set; }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public string? DefaultValue { get => GetAttribute("value") ?? TextContent ?? ""; set { SetAttribute("value", value); TextContent = value; } }
    public string? Value { get => TextContent ?? ""; set => TextContent = value; }
    public string? Type => "output";
    public NodeList? Labels { get; set; }
    public ValidityState Validity => new();
    public bool CheckValidity() => true;
    public bool ReportValidity() => true;
    public void SetCustomValidity(string error) { }
}

public class HTMLBaseElement : HtmlElement
{
    public HTMLBaseElement(Document? document, string? name = null) : base(name ?? "base") { }
    public string? Href { get => GetAttribute("href"); set => SetAttribute("href", value); }
    public string? Target { get => GetAttribute("target"); set => SetAttribute("target", value); }
}

public class HTMLMetaElement : HtmlElement
{
    public HTMLMetaElement(Document? document, string? name = null) : base(name ?? "meta") { }
    public string? Name { get => GetAttribute("name"); set => SetAttribute("name", value); }
    public string? HttpEquiv { get => GetAttribute("http-equiv"); set => SetAttribute("http-equiv", value); }
    public string? Content { get => GetAttribute("content"); set => SetAttribute("content", value); }
    public string? Scheme { get => GetAttribute("scheme"); set => SetAttribute("scheme", value); }
    public string? Media { get => GetAttribute("media"); set => SetAttribute("media", value); }
    public string? Charset { get => GetAttribute("charset"); set => SetAttribute("charset", value); }
}

public class HTMLTitleElement : HtmlElement
{
    public HTMLTitleElement(Document? document, string? name = null) : base(name ?? "title") { }
    public new string? Text { get => TextContent; set => TextContent = value; }
}

public class HTMLUnknownElement : HtmlElement
{
    public HTMLUnknownElement(Document? document, string? name = null) : base(name ?? "unknown") { }
}

public class HTMLDListElement : HtmlElement
{
    public HTMLDListElement(Document? document, string? name = null) : base(name ?? "dl") { }
}

public class HTMLDataElement : HtmlElement
{
    public HTMLDataElement(Document? document, string? name = null) : base(name ?? "data") { }
    public string? Value { get => GetAttribute("value"); set => SetAttribute("value", value); }
}

public class HTMLTimeElement : HtmlElement
{
    public HTMLTimeElement(Document? document, string? name = null) : base(name ?? "time") { }
    public string? DateTime { get => GetAttribute("datetime"); set => SetAttribute("datetime", value); }
}

public class HTMLPictureElement : HtmlElement
{
    public HTMLPictureElement(Document? document, string? name = null) : base(name ?? "picture") { }
}

public class HTMLMenuElement : HtmlElement
{
    public HTMLMenuElement(Document? document, string? name = null) : base(name ?? "menu") { }
    public bool Compact { get => HasAttribute("compact"); set { if (value) SetAttribute("compact", ""); else RemoveAttribute("compact"); } }
}

public class HTMLOptGroupElement : HtmlElement
{
    public HTMLOptGroupElement(Document? document, string? name = null) : base(name ?? "optgroup") { }
    public bool Disabled { get => HasAttribute("disabled"); set { if (value) SetAttribute("disabled", ""); else RemoveAttribute("disabled"); } }
    public string? Label { get => GetAttribute("label"); set => SetAttribute("label", value); }
}

public class HTMLMarqueeElement : HtmlElement
{
    public HTMLMarqueeElement(Document? document, string? name = null) : base(name ?? "marquee") { }
    public string? Behavior { get => GetAttribute("behavior"); set => SetAttribute("behavior", value); }
    public string? Direction { get => GetAttribute("direction"); set => SetAttribute("direction", value); }
    public int ScrollAmount { get => int.TryParse(GetAttribute("scrollamount"), out var v) ? v : 6; set => SetAttribute("scrollamount", value.ToString()); }
    public int ScrollDelay { get => int.TryParse(GetAttribute("scrolldelay"), out var v) ? v : 85; set => SetAttribute("scrolldelay", value.ToString()); }
    public bool Loop { get => int.TryParse(GetAttribute("loop"), out var v) ? v != 0 : true; set => SetAttribute("loop", value ? "-1" : "0"); }
    public ulong Width { get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 0; set => SetAttribute("width", value.ToString()); }
    public ulong Height { get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 0; set => SetAttribute("height", value.ToString()); }
    public int HSpace { get => int.TryParse(GetAttribute("hspace"), out var v) ? v : 0; set => SetAttribute("hspace", value.ToString()); }
    public int VSpace { get => int.TryParse(GetAttribute("vspace"), out var v) ? v : 0; set => SetAttribute("vspace", value.ToString()); }
}

public class HTMLBodyElement : HtmlElement
{
    public HTMLBodyElement(Document? document, string? name = null) : base(name ?? "body") { }
    public EventHandler? OnAfterPrint { get; set; }
    public EventHandler? OnBeforePrint { get; set; }
    public EventHandler? OnBeforeUnload { get; set; }
    public EventHandler? OnHashChange { get; set; }
    public EventHandler? OnLanguageChange { get; set; }
    public EventHandler? OnMessage { get; set; }
    public EventHandler? OnOffline { get; set; }
    public EventHandler? OnOnline { get; set; }
    public EventHandler? OnPageHide { get; set; }
    public EventHandler? OnPageShow { get; set; }
    public EventHandler? OnPopState { get; set; }
    public EventHandler? OnResize { get; set; }
    public EventHandler? OnStorage { get; set; }
    public EventHandler? OnUnload { get; set; }
}

public class HTMLHtmlElement : HtmlElement
{
    public HTMLHtmlElement(Document? document, string? name = null) : base(name ?? "html") { }
    public string? Version { get => GetAttribute("version"); set => SetAttribute("version", value); }
}

public class HTMLHeadElement : HtmlElement
{
    public HTMLHeadElement(Document? document, string? name = null) : base(name ?? "head") { }
}
