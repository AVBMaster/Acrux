using SkiaSharp;
using Acrux.Core.Dom.Animations;
using Acrux.Core.Dom.Cssom;
using Acrux.Core.Dom.Html;

namespace Acrux.Core.Dom;

public class Document : Node
{
    private Element? _documentElement;
    public Element? DocumentElement
    {
        get => _documentElement;
        set
        {
            _documentElement = value;
            if (value != null && !Children.Contains(value))
                AppendChild(value);
            // Markers and generated content reach the document's @counter-style rules
            // through Node.OwnerDocument, so the whole tree has to carry the link.
            value?.SetOwnerDocumentRecursive(this);
        }
    }
    public Element? Body { get; set; }
    public Element? Head { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Charset { get; set; } = "UTF-8";
    public string ContentType { get; set; } = "text/html";
    public string ReadyState { get; set; } = "loading";
    public string CompatMode { get; set; } = "CSS1Compat";
    public string? Cookie { get; set; }
    public bool Hidden { get; set; }
    public string VisibilityState { get; set; } = "visible";

    public Element? ActiveElement { get; set; }
    public bool FullscreenEnabled { get; set; }
    public Element? FullscreenElement { get; set; }
    public Element? PointerLockElement { get; set; }
    public Element? ScrollingElement { get; set; }
    public WindowProxy? DefaultView { get; set; }
    public FontFaceSet Fonts { get; } = new();
    /// <summary>The document's @counter-style rules, rebuilt whenever its stylesheets are
    /// recomputed. Markers and counter() look custom names up here.</summary>
    public Acrux.Core.Css.CounterStyleRegistry CounterStyles { get; set; } = new();
    public DocumentTimeline Timeline { get; } = new();
    public StyleSheetList StyleSheets { get; } = new();
    public List<CSSStyleSheet> AdoptedStyleSheets { get; set; } = new();
    public HtmlCollection Images => new(GetElementsByTagName("img").Cast<Element>().ToList());
    public HtmlCollection Embeds => new(GetElementsByTagName("embed").Cast<Element>().ToList());
    public HtmlCollection Plugins => Embeds;
    public HtmlCollection Links => new(GetElementsByTagName("a").Where(e => e.HasAttribute("href")).Cast<Element>().ToList());
    public HtmlCollection Forms => new(GetElementsByTagName("form").Cast<Element>().ToList());
    public HtmlCollection Scripts => new(GetElementsByTagName("script").Cast<Element>().ToList());

    public Element? ElementFromPoint(double x, double y) => null;
    public List<Element> ElementsFromPoint(double x, double y) => new();
    public Selection? GetSelection() => null;

    public TouchList CreateTouchList(params Touch[] touches) => new(touches);

    public Document()
    {
        NodeName = "#document";
    }

    public override NodeType NodeType => NodeType.Document;
    public override string NodeName => "#document";

    // Spec methods
    public Element CreateElement(string tagName)
    {
        return CreateHtmlElement(tagName);
    }

    public Element CreateElementNS(string? ns, string tagName)
    {
        var el = CreateHtmlElement(tagName);
        el.NamespaceUri = ns;
        return el;
    }

    private static HtmlElement CreateHtmlElement(string tagName) =>
        HtmlElementFactory.Create(tagName);

    public TextNode CreateTextNode(string text)
    {
        return new TextNode(text);
    }

    public CommentNode CreateComment(string data)
    {
        return new CommentNode(data);
    }

    public DocumentFragment CreateDocumentFragment()
    {
        return new DocumentFragment();
    }

    public CDataSection CreateCDataSection(string data)
    {
        return new CDataSection(data);
    }

    public ProcessingInstruction CreateProcessingInstruction(string target, string data)
    {
        return new ProcessingInstruction(target, data);
    }

    public Attr CreateAttribute(string name)
    {
        return new Attr(name);
    }

    public Attr CreateAttributeNS(string? ns, string name)
    {
        return new Attr(name, ns);
    }

    public Element? GetElementById(string id)
    {
        return FindElementById(DocumentElement, id);
    }

    public List<Element> GetElementsByTagName(string tagName)
    {
        var result = new List<Element>();
        tagName = tagName.ToUpperInvariant();
        CollectElementsByTagName(DocumentElement, tagName, result);
        return result;
    }

    public List<Element> GetElementsByClassName(string className)
    {
        var result = new List<Element>();
        CollectElementsByClassName(DocumentElement, className, result);
        return result;
    }

    public Element? QuerySelector(string selector)
    {
        return QuerySelectorInternal(DocumentElement, selector);
    }

    public List<Element> QuerySelectorAll(string selector)
    {
        var result = new List<Element>();
        QuerySelectorAllInternal(DocumentElement, selector, result);
        return result;
    }

    public bool HasFocus() => true;

    public Node ImportNode(Node node, bool deep = false)
    {
        return node.CloneNode(deep);
    }

    public Node AdoptNode(Node node)
    {
        node.Remove();
        node.OwnerDocument = this;
        return node;
    }

    public Range CreateRange() => new();

    public Event CreateEvent(string eventType)
    {
        return eventType.ToLowerInvariant() switch
        {
            "uievent" or "uievents" => new UiEvent(""),
            "mouseevent" or "mouseevents" => new MouseEvent(""),
            "mutationevent" or "mutationevents" => new Event(""),
            "htmlevents" or "events" => new Event(""),
            "keyboardevent" or "keyboardevents" => new KeyboardEvent(""),
            "customevent" => new CustomEvent(""),
            "event" => new Event(""),
            _ => new Event(eventType)
        };
    }

    public void ParseHtml(string source)
    {
        ReadyState = "loading";
        ContentType = "text/html";
    }

    public void ParseXml(string source)
    {
        ReadyState = "loading";
        ContentType = "text/xml";
    }

    // Private helpers
    private static Element? FindElementById(Element? root, string id)
    {
        if (root == null) return null;
        if (root.Id == id) return root;
        foreach (var child in root.Children.OfType<Element>())
        {
            var found = FindElementById(child, id);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectElementsByTagName(Element? root, string tagName, List<Element> result)
    {
        if (root == null) return;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.TagName == tagName)
                result.Add(child);
            CollectElementsByTagName(child, tagName, result);
        }
    }

    private static void CollectElementsByClassName(Element? root, string className, List<Element> result)
    {
        if (root == null) return;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.HasClass(className))
                result.Add(child);
            CollectElementsByClassName(child, className, result);
        }
    }

    private static Element? QuerySelectorInternal(Element? root, string selector)
    {
        if (root == null) return null;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSimpleSelector(child, selector))
                return child;
            var found = QuerySelectorInternal(child, selector);
            if (found != null) return found;
        }
        return null;
    }

    private static void QuerySelectorAllInternal(Element? root, string selector, List<Element> result)
    {
        if (root == null) return;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSimpleSelector(child, selector))
                result.Add(child);
            QuerySelectorAllInternal(child, selector, result);
        }
    }

    private static bool MatchesSimpleSelector(Element el, string selector)
    {
        selector = selector.Trim();
        if (selector.StartsWith('#'))
            return el.Id == selector[1..];
        if (selector.StartsWith('.'))
            return el.HasClass(selector[1..]);
        return el.TagName.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }
}