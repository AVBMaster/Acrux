namespace Acrux.Core.Dom.Html;

/// <summary>
/// HTML element factory, mirroring Blink's HTMLElementFactory.
/// Maps each HTML tag name to its specialized element class per the HTML spec.
/// Unknown/deprecated tags fall back to HtmlElement or HTMLUnknownElement.
/// </summary>
public static class HtmlElementFactory
{
    public delegate HtmlElement Constructor(string tagName);

    private static readonly Dictionary<string, Constructor> _constructors = new(StringComparer.OrdinalIgnoreCase);

    static HtmlElementFactory()
    {
        Register("a", t => new HTMLAnchorElement(null, t));
        Register("abbr", t => new HtmlElement(t));
        Register("acronym", t => new HtmlElement(t));
        Register("address", t => new HtmlElement(t));
        Register("area", t => new HTMLAreaElement(null, t));
        Register("article", t => new HtmlElement(t));
        Register("aside", t => new HtmlElement(t));
        Register("audio", t => new HTMLAudioElement(null, t));
        Register("b", t => new HtmlElement(t));
        Register("base", t => new HTMLBaseElement(null, t));
        Register("basefont", t => new HtmlElement(t));
        Register("bdi", t => new HtmlElement(t));
        Register("bdo", t => new HtmlElement(t));
        Register("big", t => new HtmlElement(t));
        Register("blockquote", t => new HTMLQuoteElement(null, t));
        Register("body", t => new HTMLBodyElement(null, t));
        Register("br", t => new HTMLBRElement(null, t));
        Register("button", t => new HTMLButtonElement(null, t));
        Register("canvas", t => new HTMLCanvasElement(null, t));
        Register("caption", t => new HTMLTableCaptionElement(null, t));
        Register("center", t => new HtmlElement(t));
        Register("cite", t => new HtmlElement(t));
        Register("code", t => new HtmlElement(t));
        Register("col", t => new HTMLTableColElement(null, t));
        Register("colgroup", t => new HTMLTableColElement(null, t));
        Register("data", t => new HTMLDataElement(null, t));
        Register("datalist", t => new HTMLDataListElement(null, t));
        Register("dd", t => new HtmlElement(t));
        Register("del", t => new HTMLModElement(null, t));
        Register("details", t => new HTMLDetailsElement(null, t));
        Register("dfn", t => new HtmlElement(t));
        Register("dialog", t => new HTMLDialogElement(null, t));
        Register("dir", t => new HtmlElement(t));
        Register("div", t => new HTMLDivElement(null, t));
        Register("dl", t => new HTMLDListElement(null, t));
        Register("dt", t => new HtmlElement(t));
        Register("em", t => new HtmlElement(t));
        Register("embed", t => new HTMLEmbedElement(null, t));
        Register("fieldset", t => new HTMLFieldSetElement(null, t));
        Register("figcaption", t => new HtmlElement(t));
        Register("figure", t => new HtmlElement(t));
        Register("font", t => new HtmlElement(t));
        Register("footer", t => new HtmlElement(t));
        Register("form", t => new HTMLFormElement(null, t));
        Register("frame", t => new HtmlElement(t));
        Register("frameset", t => new HtmlElement(t));
        Register("h1", t => new HTMLHeadingElement(null, t));
        Register("h2", t => new HTMLHeadingElement(null, t));
        Register("h3", t => new HTMLHeadingElement(null, t));
        Register("h4", t => new HTMLHeadingElement(null, t));
        Register("h5", t => new HTMLHeadingElement(null, t));
        Register("h6", t => new HTMLHeadingElement(null, t));
        Register("head", t => new HTMLHeadElement(null, t));
        Register("header", t => new HtmlElement(t));
        Register("hgroup", t => new HtmlElement(t));
        Register("hr", t => new HTMLHRElement(null, t));
        Register("html", t => new HTMLHtmlElement(null, t));
        Register("i", t => new HtmlElement(t));
        Register("iframe", t => new HTMLIFrameElement(null, t));
        Register("img", t => new HTMLImageElement(null, t));
        Register("input", t => new HTMLInputElement(null, t));
        Register("ins", t => new HTMLModElement(null, t));
        Register("kbd", t => new HtmlElement(t));
        Register("label", t => new HTMLLabelElement(null, t));
        Register("legend", t => new HTMLLegendElement(null, t));
        Register("li", t => new HTMLLIElement(null, t));
        Register("link", t => new HTMLLinkElement(null, t));
        Register("listing", t => new HTMLPreElement(null, t));
        Register("main", t => new HtmlElement(t));
        Register("map", t => new HTMLMapElement(null, t));
        Register("mark", t => new HtmlElement(t));
        Register("marquee", t => new HTMLMarqueeElement(null, t));
        Register("menu", t => new HTMLMenuElement(null, t));
        Register("meta", t => new HTMLMetaElement(null, t));
        Register("meter", t => new HTMLMeterElement(null, t));
        Register("nav", t => new HtmlElement(t));
        Register("nobr", t => new HtmlElement(t));
        Register("noembed", t => new HtmlElement(t));
        Register("noframes", t => new HtmlElement(t));
        Register("noscript", t => new HtmlElement(t));
        Register("object", t => new HTMLObjectElement(null, t));
        Register("ol", t => new HTMLOListElement(null, t));
        Register("optgroup", t => new HTMLOptGroupElement(null, t));
        Register("option", t => new HTMLOptionElement(null, t));
        Register("output", t => new HTMLOutputElement(null, t));
        Register("p", t => new HTMLParagraphElement(null, t));
        Register("param", t => new HTMLParamElement(null, t));
        Register("picture", t => new HTMLPictureElement(null, t));
        Register("plaintext", t => new HtmlElement(t));
        Register("pre", t => new HTMLPreElement(null, t));
        Register("progress", t => new HTMLProgressElement(null, t));
        Register("q", t => new HTMLQuoteElement(null, t));
        Register("rp", t => new HtmlElement(t));
        Register("rt", t => new HtmlElement(t));
        Register("ruby", t => new HtmlElement(t));
        Register("s", t => new HtmlElement(t));
        Register("samp", t => new HtmlElement(t));
        Register("script", t => new HTMLScriptElement(null, t));
        Register("search", t => new HtmlElement(t));
        Register("section", t => new HtmlElement(t));
        Register("select", t => new HTMLSelectElement(null, t));
        Register("slot", t => new HTMLSlotElement(null, t));
        Register("small", t => new HtmlElement(t));
        Register("source", t => new HTMLSourceElement(null, t));
        Register("span", t => new HTMLSpanElement(null, t));
        Register("strike", t => new HtmlElement(t));
        Register("strong", t => new HtmlElement(t));
        Register("style", t => new HTMLStyleElement(null, t));
        Register("sub", t => new HtmlElement(t));
        Register("summary", t => new HtmlElement(t));
        Register("sup", t => new HtmlElement(t));
        Register("table", t => new HTMLTableElement(null, t));
        Register("tbody", t => new HTMLTableSectionElement(null, t));
        Register("td", t => new HTMLTableCellElement(null, t));
        Register("template", t => new HTMLTemplateElement(null, t));
        Register("textarea", t => new HTMLTextAreaElement(null, t));
        Register("tfoot", t => new HTMLTableSectionElement(null, t));
        Register("th", t => new HTMLTableCellElement(null, t));
        Register("thead", t => new HTMLTableSectionElement(null, t));
        Register("time", t => new HTMLTimeElement(null, t));
        Register("title", t => new HTMLTitleElement(null, t));
        Register("tr", t => new HTMLTableRowElement(null, t));
        Register("track", t => new HTMLTrackElement(null, t));
        Register("tt", t => new HtmlElement(t));
        Register("u", t => new HtmlElement(t));
        Register("ul", t => new HTMLUListElement(null, t));
        Register("var", t => new HtmlElement(t));
        Register("video", t => new HTMLVideoElement(null, t));
        Register("wbr", t => new HtmlElement(t));
        Register("xmp", t => new HTMLPreElement(null, t));
    }

    private static void Register(string tag, Constructor ctor) => _constructors[tag] = ctor;

    /// <summary>Creates the appropriate element class for the given tag name.</summary>
    public static HtmlElement Create(string tagName)
    {
        var lower = tagName.ToLowerInvariant();
        if (_constructors.TryGetValue(lower, out var ctor))
            return ctor(lower);
        return new HTMLUnknownElement(null, lower);
    }
}