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

    private string? _title;

    /// <summary>HTML §4.2.5: the title IDL attribute reflects the text content of the first 'title'
    /// element in the document, stripped of leading and trailing ASCII whitespace. Reading it from
    /// the tree is what makes an edit to that element — a textContent write, an innerHTML rewrite,
    /// an appended node — the title the window and 'document.title' answer with; writing puts the
    /// text there, or into a new element at the end of the head when the document has none. The
    /// stored value is only what a document with no 'title' element falls back to.</summary>
    public string Title
    {
        get
        {
            var element = FirstTitleElement();
            if (element == null) return _title ?? string.Empty;
            return StripAsciiWhitespace(element.TextContent ?? string.Empty);
        }
        set
        {
            var element = FirstTitleElement();
            if (element != null)
            {
                element.TextContent = value;
                _title = value;
                return;
            }
            _title = value;
            var head = Head ?? HeadElementOf();
            if (head == null) return;
            var created = CreateElement("title");
            created.AppendChild(CreateTextNode(value));
            head.AppendChild(created);
        }
    }

    private Element? FirstTitleElement()
    {
        var titles = GetElementsByTagName("title");
        return titles.Count > 0 ? titles[0] : null;
    }

    private Element? HeadElementOf()
    {
        var root = DocumentElement;
        if (root == null) return null;
        foreach (var child in root.Children.OfType<Element>())
            if (child.TagName.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                return child;
        return null;
    }

    /// <summary>HTML's "strip leading and trailing whitespace" is the ASCII space, tab, line feed,
    /// carriage return and form feed — not the Unicode set, which would eat a no-break space the
    /// page put in the title on purpose.</summary>
    private static string StripAsciiWhitespace(string value)
    {
        int start = 0;
        while (start < value.Length && (value[start] == ' ' || value[start] == '\t' || value[start] == '\n'
            || value[start] == '\r' || value[start] == '\f'))
            start++;
        int end = value.Length;
        while (end > start && (value[end - 1] == ' ' || value[end - 1] == '\t' || value[end - 1] == '\n'
            || value[end - 1] == '\r' || value[end - 1] == '\f'))
            end--;
        return value.Substring(start, end - start);
    }

    public string Url { get; set; } = string.Empty;

    /// <summary>HTML §2.7.3: the first 'base' element with an 'href' wins over the document URL, and
    /// its value is resolved against that URL, so a page that names a relative base still hands its
    /// nodes an absolute 'baseURI'. A document with no base falls back to its own URL.</summary>
    public string? BaseUrl
    {
        get
        {
            foreach (var el in GetElementsByTagName("base"))
            {
                var href = el.GetAttribute("href");
                if (string.IsNullOrEmpty(href)) continue;
                return ResolveAgainstUrl(href);
            }
            return string.IsNullOrEmpty(Url) ? null : Url;
        }
    }

    private string? ResolveAgainstUrl(string href)
    {
        if (string.IsNullOrEmpty(Url)) return href;
        try
        {
            // 'System.Uri' understands 'file', 'http(s)' and the rest, and leaves an href that is
            // already absolute alone.
            return new Uri(new Uri(Url), href).ToString();
        }
        catch
        {
            return href;
        }
    }

    /// <summary>HTML §2.6.2: which compatibility mode the parser decided the document is in — written
    /// when the doctype is read, and forced on by the end of parsing when there was no doctype at all.
    /// 'document.compatMode' reads it, and the legacy layout rules that keys off quirks will find it
    /// here when they are wired up.</summary>
    public bool QuirksMode { get; set; }
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
    /// <summary>The document's adopted style sheets (CSSOM "adopted style sheets") — the
    /// constructed sheets a page installed with <c>document.adoptedStyleSheets = […]</c>.
    /// They hold the same rule list the sheet object exposes, so a rule inserted after
    /// adoption is applied rather than remembered, and they cascade after the sheets the
    /// document's own elements contribute, in adoption order.</summary>
    public List<Acrux.Core.Css.Rules.StyleSheetContents> AdoptedStyleSheets { get; set; } = new();
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
        var el = CreateHtmlElement(tagName);
        el.OwnerDocument = this;
        // DOM §4.2.1, step for a document that is not an HTML one: there is no case folding there at
        // all, so the spelling the page wrote is the one every name reads back — the tree an XML
        // parse of '<Child>' builds answers 'Child' from node name, tag name and local name alike
        // (measured), and an element the XML reader made without a namespace is the same case.
        if (!IsHtmlDocument)
        {
            el.TagName = tagName;
            el.LocalName = tagName;
            el.Prefix = null;
            // An element made by an XML document has no namespace until one is given to it — the HTML
            // namespace the element class carries from its constructor is not it (measured: a parse of
            // '<a>' as XML answers null from 'namespaceURI').
            el.NamespaceUri = null;
        }
        return el;
    }

    /// <summary>DOM §4.2.1: 'createElementNS' is handed a <em>qualified</em> name, so its prefix and
    /// its local name are taken apart here — an element made as 'svg:circle' answers 'svg' and
    /// 'circle'. The case it keeps is the document's business: only an HTML element of an HTML
    /// document is folded to the uppercase spelling tag name reads, and there the prefix is folded
    /// while the local name keeps the case it was written in (all four measured).</summary>
    public Element CreateElementNS(string? ns, string tagName)
    {
        var el = CreateHtmlElement(tagName);
        el.NamespaceUri = ns;
        el.OwnerDocument = this;
        var at = tagName.IndexOf(':');
        if (at > 0)
        {
            var html = ns == "http://www.w3.org/1999/xhtml" && IsHtmlDocument;
            el.Prefix = html ? tagName[..at].ToLowerInvariant() : tagName[..at];
            el.LocalName = tagName[(at + 1)..];
            el.TagName = html ? tagName.ToUpperInvariant() : tagName;
        }
        else if (!IsHtmlDocument)
        {
            // An XML document has no case folding at all: the name a page wrote is the name it reads.
            el.TagName = tagName;
            el.LocalName = tagName;
        }
        return el;
    }

    /// <summary>Whether this is an HTML document — the difference that decides the case of a name and
    /// which node kinds the document will make at all.</summary>
    public bool IsHtmlDocument => ContentType is null
        || ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

    private static HtmlElement CreateHtmlElement(string tagName) =>
        HtmlElementFactory.Create(tagName);

    public TextNode CreateTextNode(string text)
    {
        return new TextNode(text);
    }

    public CommentNode CreateComment(string data)
    {
        // A node made by a document belongs to that document from the moment it is made (§4.1), not
        // only once it reaches the tree — 'doc.createComment('x').ownerDocument' is read before any
        // insertion, and for a document the page built with the implementation it is not 'document'.
        return new CommentNode(data) { OwnerDocument = this };
    }

    public DocumentFragment CreateDocumentFragment()
    {
        return new DocumentFragment() { OwnerDocument = this };
    }

    public CDataSection CreateCDataSection(string data)
    {
        return new CDataSection(data) { OwnerDocument = this };
    }

    public ProcessingInstruction CreateProcessingInstruction(string target, string data)
    {
        return new ProcessingInstruction(target, data) { OwnerDocument = this };
    }

    public Attr CreateAttribute(string name)
    {
        return new Attr(name) { OwnerDocument = this };
    }

    public Attr CreateAttributeNS(string? ns, string name)
    {
        return new Attr(name, ns) { OwnerDocument = this };
    }

    public Element? GetElementById(string id)
    {
        return FindElementById(DocumentElement, id);
    }

    public List<Element> GetElementsByTagName(string tagName)
    {
        var result = new List<Element>();
        tagName = tagName.ToUpperInvariant();
        // HTML §4.5.4: on a document the search covers every element, the root included — measured,
        // 'document.getElementsByTagName("*")' counts the html element itself, while the same call on
        // an element covers only its descendants.
        // The wildcard reads every element, the root included (§4.5.4).
        var wildcard = tagName == "*";
        if (DocumentElement != null && (wildcard || DocumentElement.TagName == tagName))
            result.Add(DocumentElement);
        CollectElementsByTagName(DocumentElement, tagName, result);
        return result;
    }

    public List<Element> GetElementsByClassName(string className)
    {
        var result = new List<Element>();
        // The same reach as the tag search: a document collects its root element too (§4.5.4).
        if (DocumentElement != null && ClassTokensMatch(DocumentElement, className))
            result.Add(DocumentElement);
        CollectElementsByClassName(DocumentElement, className, result);
        return result;
    }

    /// <summary>HTML §4.5.4: the argument is a list of class names and an element answers only when it
    /// carries every one of them; a list with nothing in it matches nothing.</summary>
    private static bool ClassTokensMatch(Element element, string className)
    {
        var wanted = className.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (wanted.Length == 0) return false;
        var written = (element.GetAttribute("class") ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (written.Length == 0) return false;
        foreach (var token in wanted)
        {
            var found = false;
            foreach (var have in written)
                if (have == token) { found = true; break; }
            if (!found) return false;
        }
        return true;
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
            if (child.TagName == tagName || tagName == "*")
                result.Add(child);
            CollectElementsByTagName(child, tagName, result);
        }
    }

    private static void CollectElementsByClassName(Element? root, string className, List<Element> result)
    {
        if (root == null) return;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (ClassTokensMatch(child, className))
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

    // The engine's real matcher: a walk that recognizes only '#id', '.class' and a bare tag name
    // answers 'a.k', ':nth-child(2)' and '[type=text]' as nothing at all, and the same string
    // through the document's own querySelectorAll found the element.
    private static bool MatchesSimpleSelector(Element el, string selector) =>
        Acrux.Core.Css.CssSelectorMatcher.Matches(selector, el);
}