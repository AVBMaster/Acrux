using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Acrux.Core.Css;
using Acrux.Core.Dom;
using DomDocument = Acrux.Core.Dom.Document;
using DomElement = Acrux.Core.Dom.Element;

namespace Acrux.Core.JavaScript;

public class DocumentHost : IDomPrototypeHost
{
    private readonly DomDocument _document;
    private ElementHost? _documentElementHost;
    private ElementHost? _bodyHost;
    private SelectionHost? _selection;
    private readonly Dictionary<string, string> _cookies = new();
    internal JavaScriptEngine? Engine { get; set; }

    public string cookie
    {
        get => string.Join("; ", _cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            var parts = value.Split('=', 2);
            if (parts.Length == 2)
                _cookies[parts[0].Trim()] = parts[1].Split(';')[0].Trim();
        }
    }

    public DocumentHost(Document document)
    {
        _document = document;
        Engine = JavaScriptEngine.Current;
        JsIntegrationService.FixProto(this);
    }

    public Document NativeDocument => _document;

    public ElementHost? documentElement
    {
        get
        {
            if (_document.DocumentElement == null) return null;
            _documentElementHost ??= WrapElement(_document.DocumentElement);
            return _documentElementHost;
        }
    }

    public ElementHost? body
    {
        get
        {
            if (_document.Body == null) return null;
            if (_bodyHost == null || _bodyHost.NativeElement != _document.Body)
                _bodyHost = WrapElement(_document.Body);
            return _bodyHost;
        }
        set
        {
            if (value != null)
                _document.Body = value.NativeElement;
        }
    }

    public ElementHost? head
    {
        get
        {
            if (_document.Head == null) return null;
            return WrapElement(_document.Head);
        }
    }

    public string title
    {
        get => _document.Title;
        set => _document.Title = value;
    }

    public string URL => _document.Url;
    public string documentURI => _document.Url;
    public string? baseURI => _document.Url;

    public string? compatMode => "CSS1Compat";

    // A Document is a Node (DOM 4 §4), so it reports nodeType 9 and '#document' like any other,
    // and its documentElement is the root element under it.
    public int nodeType => 9;
    public string nodeName => "#document";
    public Node? parentNode => null;
    public Node? ownerDocument => null;

    // A Document is a Node, so it carries the child-node accessors too. Its only element child is
    // the document element; the rest (doctype, comments) are non-element children.
    public object[] children => _document.Children.OfType<DomElement>().Select(e => (object)WrapElement(e)!).ToArray();
    public object[] childNodes => _document.Children.Select(WrapAnyDocumentNode).Where(x => x != null).Select(x => x!).ToArray();
    // The tree reads a document answers as well as an element does (§4.1): 'document.firstChild' is
    // the doctype on a page the parser built, and 'previousSibling' of the root is how a page finds it.
    public object? firstChild => WrapAnyDocumentNode(_document.Children.FirstOrDefault());
    public object? lastChild => WrapAnyDocumentNode(_document.Children.LastOrDefault());
    public object? parentElement => null;
    public object? nextSibling => null;
    public object? previousSibling => null;
    public ElementHost? firstElementChild => _document.DocumentElement != null ? WrapElement(_document.DocumentElement) : null;
    public ElementHost? lastElementChild => firstElementChild;
    public int childElementCount => _document.Children.OfType<DomElement>().Count();

    private object? WrapAnyDocumentNode(Node n) => JsIntegrationService.WrapAny(n);
    public string? textContent
    {
        get => _document.TextContent;
        set => _document.TextContent = value;
    }

    public object? location => Engine?.LocationHost;

    public string __domType => "HTMLDocument";

    public string[] __domTypeChain => new[] { "EventTarget", "Node", "HTMLDocument" };
    public string? characterSet => "UTF-8";
    public string? contentType => "text/html";

    // ===== Document collections =====
    public object[] anchors
    {
        get
        {
            var elements = _document.GetElementsByTagName("a")
                .Where(e => e.HasAttribute("name") || e.HasAttribute("id"))
                .Select(e => (object)WrapElement(e)!).ToArray();
            return elements;
        }
    }

    public object[] forms
    {
        get
        {
            return _document.GetElementsByTagName("form")
                .Select(e => (object)WrapElement(e)!).ToArray();
        }
    }

    public object[] images
    {
        get
        {
            return _document.GetElementsByTagName("img")
                .Select(e => (object)WrapElement(e)!).ToArray();
        }
    }

    public object[] links
    {
        get
        {
            return _document.GetElementsByTagName("a")
                .Where(e => e.HasAttribute("href"))
                .Select(e => (object)WrapElement(e)!).ToArray();
        }
    }

    public object[] scripts
    {
        get
        {
            return _document.GetElementsByTagName("script")
                .Select(e => (object)WrapElement(e)!).ToArray();
        }
    }

    public object[] embeds
    {
        get
        {
            return _document.GetElementsByTagName("embed")
                .Select(e => (object)WrapElement(e)!).ToArray();
        }
    }

    public object[] plugins => embeds;

    public ElementHost? getElementById(string id)
    {
        if (_document.DocumentElement == null) return null;
        var el = FindElementById(_document.DocumentElement, id);
        return el != null ? WrapElement(el) : null;
    }

    public object? querySelector(string selector)
    {
        // The selector is checked before the document is even looked at: an empty document still
        // refuses a text the grammar does not read (measured).
        CssSelectorMatcher.ParseForQuery(selector, "querySelector", "Document");
        if (_document.DocumentElement == null) return null;
        // A document query is anchored at the root element and its candidate list starts with it:
        // 'document.querySelectorAll("html")' answers with one element, and ':scope' inside such a
        // query means that element (both measured).
        var el = CssSelectorMatcher.FirstDescendant(_document.DocumentElement, selector,
            _document.DocumentElement, includeRoot: true);
        return el != null ? WrapElement(el) : null;
    }

    public object querySelectorAll(string selector)
    {
        CssSelectorMatcher.ParseForQuery(selector, "querySelectorAll", "Document");
        if (_document.DocumentElement == null) return new object[0];
        return new JsNodeList(_document.DocumentElement, Engine, root =>
        {
            var list = new List<Element>();
            CssSelectorMatcher.CollectDescendants(root, selector, list, root, includeRoot: true);
            return list;
        });
    }

    public object getElementsByTagName(string tagName)
    {
        if (_document.DocumentElement == null) return new object[0];
        return new JsHtmlCollection(_document.DocumentElement, Engine, tagName);
    }

    public object getElementsByClassName(string className)
    {
        if (_document.DocumentElement == null) return new object[0];
        return new JsHtmlCollection(_document.DocumentElement, Engine, null, className);
    }

    public object getElementsByName(string name)
    {
        if (_document.DocumentElement == null) return new object[0];
        return new JsHtmlCollection(_document.DocumentElement, Engine, null, null, name);
    }

    public ElementHost createElement(string tagName)
    {
        DomNames.RequireTagName(tagName, "createElement", "Document");
        var el = _document.CreateElement(tagName);
        var host = WrapElement(el) ?? new ElementHost(el);
        // A name the page registered is constructed as it is made, while it is still outside the
        // tree — the constructor sees the element before any attribute does (Custom Elements §1).
        CustomElements.Constructed(el);
        return host;
    }

    public ElementHost createElementNS(string? ns, string tagName)
    {
        DomNames.RequireQualifiedName(tagName, "createElementNS", "Document", element: true);
        // The name is taken apart where the element is made: a qualified name carries a prefix and a
        // local name, the case an element keeps is the document's business, and an element in the
        // HTML namespace is the only one folded to the uppercase spelling (DOM §4.2.1, measured:
        // 'svg:circle' reads 'svg:circle' from tag name, 'x:Y' in the HTML namespace reads 'X:Y').
        var el = _document.CreateElementNS(ns, tagName);
        var host = WrapElement(el) ?? new ElementHost(el);
        CustomElements.Constructed(el);
        return host;
    }


    /// <summary>Whether this is an HTML document — the difference that decides case-folded names and
    /// which node kinds the document will make at all.</summary>
    private bool IsHtmlDocument =>
        _document.ContentType is null
        || _document.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

    // A node the page made belongs to the document that made it from the moment it is made, not
    // only once it reaches the tree (DOM §4.1), so 'doc.createComment('x').ownerDocument === doc'
    // holds for a document the page built with the implementation as much as for the page's own.
    private T WithOwner<T>(T node) where T : Node
    {
        node.OwnerDocument = _document;
        return node;
    }

    public TextNodeWrapper createTextNode(string text)
        => (TextNodeWrapper)JsIntegrationService.WrapAny(WithOwner(new TextNode(text)))!;

    public object? createComment(string data) => JsIntegrationService.WrapAny(WithOwner(new CommentNode(data)));

    public object? createDocumentFragment() => JsIntegrationService.WrapAny(WithOwner(new DocumentFragment()));

    /// <summary>DOM §4.3.8: a CDATA section carries text the parser was meant to read as-is, so an
    /// HTML document — where nothing is read that way — refuses to make one. An XML document gives a
    /// node that is a Text with its own name and type.</summary>
    public object? createCDATASection(string data)
    {
        // DOM §4.3.8: an HTML document has no CDATA content model, so it refuses to make one.
        if (IsHtmlDocument)
            throw new DOMException("Failed to execute 'createCDATASection' on 'Document': This "
                                   + "operation is not supported for HTML documents.", "NotSupportedError");
        // ']]>' is the delimiter the section ends at, so it cannot appear inside one (§4.3.8).
        if (data.Contains("]]>", StringComparison.Ordinal))
            throw new DOMException("Failed to execute 'createCDATASection' on 'Document': String "
                                   + "cannot contain ']]>' since that is the end delimiter of a CData section.",
                                   "InvalidCharacterError");
        return JsIntegrationService.WrapAny(WithOwner(new CDataSection(data)));
    }

    /// <summary>DOM §4.3.5: only the target is held to the XML name rule — the data after it is
    /// whatever the page wrote ('createProcessingInstruction('t','a<b')' is accepted, measured).</summary>
    public object? createProcessingInstruction(string target, string data)
    {
        DomNames.RequireXmlName(target, "createProcessingInstruction", "Document");
        return JsIntegrationService.WrapAny(WithOwner(new ProcessingInstruction(target, data)));
    }

    public object? createAttribute(string name)
    {
        DomNames.RequireLocalName(name, "createAttribute", "Document");
        return new AttributeHost(name, document: _document);
    }

    public object? createAttributeNS(string? ns, string name)
    {
        DomNames.RequireQualifiedName(name, "createAttributeNS", "Document", element: false);
        return new AttributeHost(name, "", ns, _document);
    }

    public ElementHost? getElementByClassName(string className)
    {
        var coll = getElementsByClassName(className);
        if (coll is JsHtmlCollection htmlColl)
        {
            foreach (var item in htmlColl)
                return item as ElementHost;
        }
        return null;
    }

    public bool hasFocus() => true;

    public string? write(string text)
    {
        if (_document.Body != null && !string.IsNullOrEmpty(text))
        {
            var nodes = HtmlParser.ParseFragment(text, "body");
            foreach (var node in nodes)
            {
                _document.Body.AppendChild(node);
            }
            var engine = JavaScriptEngine.Current;
            engine?.MarkDirty();
        }
        return null;
    }

    public string? writeln(string text)
    {
        return write(text + "\n");
    }

    public ElementHost? elementFromPoint(double x, double y) => null;

    public object[] elementsFromPoint(double x, double y) => Array.Empty<object>();

    /// <summary>DOM §4.4 (ParentNode): the document builds trees the same way an element does, so the
    /// mixin's three methods are here rather than only on an element — a page that calls
    /// 'document.append(frag)' before the body exists expects the nodes to land on the document.</summary>
    public void append(params object?[] nodes)
    {
        foreach (var n in nodes) InsertOnDocument(n, atEnd: true);
    }

    public void prepend(params object?[] nodes)
    {
        foreach (var n in nodes) InsertOnDocument(n, atEnd: false);
    }

    public void replaceChildren(params object?[] nodes)
    {
        for (var i = _document.Children.Count - 1; i >= 0; i--)
            _document.RemoveChild(_document.Children[i]);
        foreach (var n in nodes) InsertOnDocument(n, atEnd: true);
    }

    private void InsertOnDocument(object? child, bool atEnd)
    {
        Node? node = child is string text ? new TextNode(text) : ElementHost.UnwrapNode(child);
        if (node == null) return;
        if (node is DocumentFragment frag)
        {
            foreach (var moved in frag.Children.ToList())
            {
                if (atEnd) _document.AppendChild(moved);
                else _document.InsertBefore(moved, _document.Children.FirstOrDefault());
            }
            return;
        }
        if (atEnd) _document.AppendChild(node);
        else _document.InsertBefore(node, _document.Children.FirstOrDefault());
    }

    /// <summary>DOM §4.1: 'document.contains(other)' is the ancestor question asked from the root of
    /// the tree — a node the document holds, including its own document element, answers true.</summary>
    public bool contains(object? other)
    {
        var node = ElementHost.UnwrapNode(other);
        if (node == null) return false;
        for (var current = node; current != null; current = current.ParentNode)
            if (ReferenceEquals(current, _document)) return true;
        return false;
    }

    public ElementHost? activeElement
    {
        get
        {
            var active = _document.ActiveElement;
            if (active != null) return WrapElement(active);
            if (_document.Body != null) return WrapElement(_document.Body);
            return null;
        }
    }

    public string readyState => "complete";

    public float scrollWidth
    {
        get
        {
            var el = _document.DocumentElement;
            if (el != null && el.LayoutBox != null)
                return Math.Max(el.ScrollWidth, 0f);
            var body = _document.Body;
            if (body != null && body.LayoutBox != null)
                return Math.Max(body.ScrollWidth, 0f);
            return 0f;
        }
    }

    public float scrollHeight
    {
        get
        {
            var el = _document.DocumentElement;
            if (el != null && el.LayoutBox != null)
                return Math.Max(el.ScrollHeight, 0f);
            var body = _document.Body;
            if (body != null && body.LayoutBox != null)
                return Math.Max(body.ScrollHeight, 0f);
            return 0f;
        }
    }

    public float scrollTop => 0f;
    public float scrollLeft => 0f;

    public string domain
    {
        get => _document.Url;
        set { }
    }

    public string referrer => "";

    public string lastModified => DateTime.Now.ToString();

    public string? inputEncoding => "UTF-8";

    public string? charset => "UTF-8";

    public string? defaultCharset => "UTF-8";

    public string? dir => "ltr";

    public string? visibilityState => "visible";

    public bool hidden => false;

    public object? doctype
    {
        get
        {
            // The doctype is a node, so it comes back as the one object the identity map keeps for
            // it — 'document.doctype === document.doctype' holds the way it does for any other node.
            var docType = _document.Children.OfType<DocumentTypeNode>().FirstOrDefault();
            return docType != null ? JsIntegrationService.WrapAny(docType) : null;
        }
    }

    public object implementation
        => _implementation ??= new DOMImplementationHost(JavaScriptEngine.Current ?? Engine);

    private object? _implementation;

    /// <summary>DOM 4 §4.7.7: a TreeWalker over the subtree, live — every step reads the tree as it
    /// stands, so a node the page inserted between two steps is walked through. The mask and the
    /// page's filter decide what a step answers; a rejected node prunes its subtree here.</summary>
    /// <remarks>
    /// The page reaches this through 'Document.prototype.createTreeWalker', which is script-side on
    /// purpose: it is the last place the filter is still the page's own object (a value handed to a
    /// host method arrives converted, and 'walker.filter' must answer the original), and what gets
    /// through here is the id that registration answered.
    /// </remarks>
    public object __makeTreeWalker(object? root, double whatToShow = 4294967295d, int filterId = 0)
    {
        var node = NodeTraverserHost.RequireRoot(root, "createTreeWalker");
        return new TreeWalkerHost(node, NodeTraverserHost.ToMask(whatToShow), filterId,
            JavaScriptEngine.Current ?? Engine);
    }

    /// <summary>DOM 4 §4.7.3: a NodeIterator over the subtree. It offers its root as the first
    /// candidate, where a walker starts one step after it, and a rejected node only steps over
    /// itself — an iterator does not prune.</summary>
    public object __makeNodeIterator(object? root, double whatToShow = 4294967295d, int filterId = 0)
    {
        var node = NodeTraverserHost.RequireRoot(root, "createNodeIterator");
        return new NodeIteratorHost(node, NodeTraverserHost.ToMask(whatToShow), filterId,
            JavaScriptEngine.Current ?? Engine);
    }

    /// <summary>DOM §3.4: a range is a pair of boundary points the page can move, and a fresh one
    /// starts collapsed at the document — before any node it could cover.</summary>
    public RangeHost createRange() => RangeHost.Adopt(new RangeHost(_document, 0, _document, 0));

    /// <summary>HTML §2.5.3.4: one selection per document, holding at most the range the page put
    /// in it. It answered null, so a script that reached for the cursor had nothing to hold. The
    /// document is given to it because a range whose content is not in this document is ignored.</summary>
    public object? getSelection()
    {
        if (_selection == null)
        {
            _selection = new SelectionHost(_document);
            JsIntegrationService.FixProto(_selection);
        }
        return _selection;
    }

    /// <summary>DOM §4.5: adoption takes the node out of the tree it was in and gives it to this
    /// document, and the object the page handed in is the object that comes back — now answering this
    /// document as its owner. Any kind of node can be adopted; a document itself cannot (§4.5).</summary>
    public object? adoptNode(object? node)
    {
        var native = ElementHost.UnwrapNode(node);
        if (native is Document)
            throw new DOMException("Failed to execute 'adoptNode' on 'Document': The node provided "
                                   + "is of type '#document', which may not be adopted.", "NotSupportedError");
        if (native == null) return node;
        native.Remove();
        native.OwnerDocument = _document;
        return node;
    }

    /// <summary>DOM §4.5: an import is a clone that belongs to this document — the node the page
    /// handed in keeps its own document, and the copy the call answers is a new object.</summary>
    public object? importNode(object? node, bool deep = false)
    {
        var native = ElementHost.UnwrapNode(node);
        if (native == null)
        {
            // An attribute is a node for every purpose but the tree, and it reaches the page as the
            // Attr wrapper rather than a CLR node — its import is a copy of what it holds.
            if (node is AttributeHost attr)
            {
                var copy = new AttributeHost(attr.name, attr.value, attr.namespaceURI, _document);
                JsIntegrationService.FixProto(copy);
                return copy;
            }
            throw new DOMException("Failed to execute 'importNode' on 'Document': parameter 1 is not "
                                   + "of type 'Node'.", "TypeError");
        }
        var imported = native.CloneNode(deep);
        imported.OwnerDocument = _document;
        return JsIntegrationService.WrapAny(imported);
    }

    public void normalizeDocument()
    {
        _document.Normalize();
    }

    public void open()
    {
    }

    public void close()
    {
    }

    public void addEventListener(string type, object callback)
    {
        addEventListener(type, callback, null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "JS engine assemblies are rooted - dynamic access to options is safe")]
    public void addEventListener(string type, object callback, object? options)
    {
        var engine = JavaScriptEngine.Current ?? Engine;
        if (engine == null) return;

        bool useCapture = false;
        if (options is bool b)
            useCapture = b;
        else if (options != null)
        {
            try { dynamic opts = options; useCapture = opts.capture ?? false; } catch { }
        }

        var integration = engine.IntegrationService;
        if (integration != null)
        {
            integration.EventBridge.AddListener(type, callback, useCapture);
        }
        else
        {
            var cbId = engine.StoreCallbackRef(callback);
        }
    }

    public void removeEventListener(string type, object callback)
    {
        removeEventListener(type, callback, null);
    }

    public void removeEventListener(string type, object callback, object? options)
    {
        var engine = JavaScriptEngine.Current ?? Engine;
        if (engine?.IntegrationService != null)
        {
            engine.IntegrationService.EventBridge.RemoveListener(type, callback);
        }
    }

    public void dispatchEvent(ScriptEvent evt)
    {
        var engine = JavaScriptEngine.Current ?? Engine;
        if (engine == null) return;

        var integration = engine.IntegrationService;
        if (integration != null)
        {
            integration.EventBridge.DispatchEvent(evt.type, null);
        }

        // Check for inline event handler on document
        if (_document.DocumentElement != null)
        {
            var attrName = "on" + evt.type;
            var attr = _document.DocumentElement.GetAttribute(attrName);
            if (!string.IsNullOrEmpty(attr))
            {
                try { engine.Evaluate(attr); }
                catch { }
            }
        }
    }

    public bool fullscreenEnabled() => false;



    public string? currentScript => null;

    /// <summary>The sheets the document contributes, in document order (CSSOM §5.2).
    /// Only the elements whose sheet the style engine actually loaded own one, so a
    /// <c>style</c> a script appended appears here once the next recompute has collected
    /// it, and a disabled or removed one disappears with it.</summary>
    public object[] styleSheets
    {
        get
        {
            // A sheet a script just created only exists once the style engine has parsed the
            // element, so the list forces the pending pass first — otherwise the <style> that
            // is three statements old is missing from the very list that is supposed to hold it.
            Engine?.FlushForRead();
            var sheets = new List<object>();
            var stack = new Stack<DomElement>();
            var root = _document.DocumentElement;
            if (root != null) stack.Push(root);
            while (stack.Count > 0)
            {
                var element = stack.Pop();
                if (element.AssociatedStyleSheet != null)
                    sheets.Add(CssStyleSheetHost.ViewOf(element, WrapElement));
                var children = element.Children;
                for (int i = children.Count - 1; i >= 0; i--)
                    if (children[i] is DomElement child)
                        stack.Push(child);
            }
            return sheets.ToArray();
        }
    }

    public string? fonts => null;

    /// <summary>The document's adopted style sheets (CSSOM §5.2.2). A page installs the
    /// constructed sheets it made with <c>document.adoptedStyleSheets = [sheet]</c>, and they
    /// cascade after the sheets its own elements contribute. Measured in the reference engine:
    /// the very sheet object that went in comes back out, and the adopted list is <em>not</em>
    /// part of <c>document.styleSheets</c>.</summary>
    public object?[] adoptedStyleSheets
    {
        get
        {
            var contents = _document.AdoptedStyleSheets;
            var views = new object?[contents.Count];
            for (int i = 0; i < contents.Count; i++)
                views[i] = contents[i].CssomView ?? new CssStyleSheetHost(contents[i]);
            return views;
        }
        set
        {
            var adopted = new List<Acrux.Core.Css.Rules.StyleSheetContents>();
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    var host = value[i] as CssStyleSheetHost;
                    if (host == null)
                        // A member that is not a stylesheet at all is refused by the IDL before the
                        // list is read, so nothing is adopted (measured: '[null]' and '[{}]' both
                        // answer TypeError, and the previous list is still in place afterwards).
                        throw new Acrux.Core.Dom.DOMException(
                            "Failed to set the 'adoptedStyleSheets' property on 'Document': "
                            + "Failed to convert value to 'CSSStyleSheet'.", "TypeError");
                    // Only a sheet the script constructed itself can be adopted; a sheet an
                    // element owns is already in the cascade through that element, and the
                    // reference engine refuses the duplicate (measured: NotAllowedError).
                    if (!host.IsConstructed)
                        throw new Acrux.Core.Dom.DOMException(
                            "Failed to set the 'adoptedStyleSheets' property on 'Document': "
                            + "Can't adopt non-constructed stylesheets.", "NotAllowedError");
                    var sheet = host.NativeSheet;
                    if (sheet != null) adopted.Add(sheet);
                }
            }
            _document.AdoptedStyleSheets = adopted;
            // The adopted list is style input for the whole document, so the next read has to
            // recompute — including when the new list is empty, which un-applies the sheets the
            // old one held. Marking the root covers the live pipeline, where no element of the
            // page changed and nothing else would notice.
            var root = _document.DocumentElement;
            if (root != null)
                Acrux.Core.Performance.DirtyState.AddSelf(root,
                    Acrux.Core.Performance.DirtyFlags.Style | Acrux.Core.Performance.DirtyFlags.Layout |
                    Acrux.Core.Performance.DirtyFlags.Paint);
            DomMutationTracker.Notify();
        }
    }
    /// <summary>What the second argument of <c>getComputedStyle</c> named.</summary>
    private enum PseudoArgument { Element, Before, After, FirstLine, FirstLetter, Marker, Unknown }

    /// <summary>The pseudo-element a page asked for, or <see cref="PseudoArgument.Unknown"/> when
    /// the string names none. One or two colons both parse (measured), and a bare name parses too
    /// — <c>'before'</c> answers like the pseudo-element it stands for rather than like a name that
    /// matches nothing.</summary>
    private static PseudoArgument ResolvePseudoArgument(string? pseudoElt)
    {
        var text = (pseudoElt ?? "").Trim().TrimStart(':');
        if (text.Length == 0) return PseudoArgument.Element;
        return text.ToLowerInvariant() switch
        {
            "before" => PseudoArgument.Before,
            "after" => PseudoArgument.After,
            "first-line" or "firstline" => PseudoArgument.FirstLine,
            "first-letter" or "firstletter" => PseudoArgument.FirstLetter,
            "marker" => PseudoArgument.Marker,
            _ => PseudoArgument.Unknown,
        };
    }

    /// <summary>The declarations the cascade collected for that pseudo-element, or null when the
    /// page wrote none — a generated box with no rule of its own still has a style to report.</summary>
    private static Dictionary<string, string>? PseudoDeclarations(Acrux.Core.Dom.Element element,
        PseudoArgument pseudo) => pseudo switch
        {
            PseudoArgument.Before => element.BeforeStyles,
            PseudoArgument.After => element.AfterStyles,
            PseudoArgument.FirstLine => element.FirstLineStyles,
            PseudoArgument.FirstLetter => element.FirstLetterStyles,
            PseudoArgument.Marker => element.MarkerStyles,
            _ => null,
        };

    public object getComputedStyle(ElementHost? element, string? pseudoElt = null)
    {
        // The IDL takes an Element, so anything else is a wrong argument rather than an element
        // with no styles — the reference engine answers with a TypeError whose message starts at
        // the sentence and names the parameter (measured: e.name is 'TypeError' and e.message is
        // "Failed to execute 'getComputedStyle' on 'Window': parameter 1 is not of type 'Element'.").
        if (element == null)
            throw new Acrux.Core.Dom.DOMException(
                "Failed to execute 'getComputedStyle' on 'Window': parameter 1 is not of type 'Element'.",
                "TypeError");

        // A computed style read is a style change event in disguise: whatever the
        // script did to the DOM earlier in the same tick has to be resolved first,
        // or the page reads the previous frame's values.
        Engine?.FlushForRead();

        // The second argument asks for the style of a generated box instead. One colon or two
        // means the same pseudo-element (measured), and a name that is no pseudo-element at all
        // answers an empty block — every property reads the empty string, which is how
        // 'getComputedStyle(el, "::bogus").color' was measured.
        var pseudo = ResolvePseudoArgument(pseudoElt);
        if (pseudo == PseudoArgument.Unknown)
            return new ComputedStyleHost(new Dictionary<string, string>(), null, null);
        if (pseudo != PseudoArgument.Element
            && element.NativeElement.ComputedStyle is { } origin)
        {
            // The generated box inherits from the element it came from and takes the initial
            // value of everything else, including 'auto' for its own box sizes — which is also
            // why no layout object is handed to the view (measured: '::before' of a 30px-wide
            // element answers 'auto' where the element answers its used width).
            bool generatesBox = pseudo is PseudoArgument.Before or PseudoArgument.After;
            var generated = Acrux.Core.Css.PseudoStyleMerger.MergeForGeneratedBox(
                origin, PseudoDeclarations(element.NativeElement, pseudo), element.NativeElement,
                generatesBox);
            // Only ::before and ::after are generated boxes: a marker and a line fragment report
            // 'content' as the property's initial 'normal' (measured).
            return new ComputedStyleHost(new Dictionary<string, string>(), generated,
                element.NativeElement, generatedBox: generatesBox);
        }

        var computedStyle = element.NativeElement.ComputedStyle;
        Dictionary<string, string> props;
        if (computedStyle == null)
        {
            // Provide reasonable UA defaults so scripts inspecting computed styles do not get undefined
            props = CreateDefaultComputedStyleForElement(element);
        }
        else
        {
            props = new Dictionary<string, string>
            {
                    ["box-sizing"] = computedStyle.BoxSizing.ToCssString(),
                // A length is resolved at computed-value time (CSS Values 3 §7), so a box
                // authored 'width: 10em' answers in pixels against its own 12px font. A box
                // size additionally clamps: nothing is never narrower than nothing.
                ["width"] = computedStyle.ComputedSizeCss(computedStyle.Width),
                ["height"] = computedStyle.ComputedSizeCss(computedStyle.Height),
                ["display"] = computedStyle.DisplayCssText,
                ["position"] = computedStyle.Position.ToCssString(),
                ["float"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.Float.ToString()),
                ["clear"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.Clear.ToString()),
                ["color"] = CssColorText.FromColor(computedStyle.Color),
                ["font-family"] = computedStyle.FontFamily ?? "",
                ["font-size"] = $"{computedStyle.FontSize}px",
                ["font-weight"] = ((int)computedStyle.FontWeight).ToString(),
                ["font-style"] = computedStyle.FontStyleCssText,
                ["line-height"] = computedStyle.LineHeightCssText,
                ["text-align"] = computedStyle.TextAlignLegacyKeyword ?? Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.TextAlign.ToString()),
                ["text-decoration"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.TextDecoration.ToString()),
                ["white-space"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.WhiteSpace.ToString()),
                ["word-break"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.WordBreak.ToString()),
                ["overflow-wrap"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.OverflowWrap.ToString()),
                ["visibility"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.Visibility.ToString()),
                ["overflow"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.Overflow.ToString()),
                ["opacity"] = computedStyle.Opacity.ToString(),
                ["z-index"] = computedStyle.ZIndex?.ToString() ?? "auto",
                ["background-color"] = computedStyle.BackgroundColor.HasValue
                    ? CssColorText.FromColor(computedStyle.BackgroundColor.Value)
                    : CssColorText.Transparent,
                ["margin-top"] = computedStyle.ComputedLengthCss(computedStyle.MarginTop),
                ["margin-right"] = computedStyle.ComputedLengthCss(computedStyle.MarginRight),
                ["margin-bottom"] = computedStyle.ComputedLengthCss(computedStyle.MarginBottom),
                ["margin-left"] = computedStyle.ComputedLengthCss(computedStyle.MarginLeft),
                ["padding-top"] = computedStyle.ComputedLengthCss(computedStyle.PaddingTop),
                ["padding-right"] = computedStyle.ComputedLengthCss(computedStyle.PaddingRight),
                ["padding-bottom"] = computedStyle.ComputedLengthCss(computedStyle.PaddingBottom),
                ["padding-left"] = computedStyle.ComputedLengthCss(computedStyle.PaddingLeft),
                ["border-top-width"] = $"{computedStyle.BorderTopWidth}px",
                ["border-right-width"] = $"{computedStyle.BorderRightWidth}px",
                ["border-bottom-width"] = $"{computedStyle.BorderBottomWidth}px",
                ["border-left-width"] = $"{computedStyle.BorderLeftWidth}px",
                ["flex-direction"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.FlexDirection.ToString()),
                ["flex-wrap"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.FlexWrap.ToString()),
                ["justify-content"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.JustifyContent.ToString()),
                ["align-items"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.AlignItems.ToString())
            };
        }

        return new ComputedStyleHost(props, element.NativeElement.ComputedStyle, element.NativeElement);
    }

    private static Dictionary<string, string> CreateDefaultComputedStyleForElement(ElementHost element)
    {
        // Basic UA defaults to avoid undefined values when computed style is not available yet
        var tag = element.NativeElement.TagName?.ToLowerInvariant() ?? "";
        var blockElements = new HashSet<string>{"div","p","h1","h2","h3","h4","h5","h6","ul","ol","li","table","header","footer","section","article","nav","body"};
        var display = blockElements.Contains(tag) ? "block" : "inline";

        var d = new Dictionary<string, string>
        {
            ["display"] = display,
            ["position"] = "static",
            ["box-sizing"] = "content-box",
            ["width"] = "auto",
            ["height"] = "auto",
            ["color"] = "rgb(0, 0, 0)",
            ["font-family"] = Acrux.Core.Fonts.FontManager.StandardFontFamily,
            ["font-size"] = "16px",
            ["line-height"] = "normal",
            ["text-align"] = "start",
            ["visibility"] = "visible",
            ["overflow"] = "visible",
            ["opacity"] = "1",
            ["background-color"] = "transparent",
            ["margin-top"] = "0",
            ["margin-right"] = "0",
            ["margin-bottom"] = "0",
            ["margin-left"] = "0",
            ["padding-top"] = "0",
            ["padding-right"] = "0",
            ["padding-bottom"] = "0",
            ["padding-left"] = "0",
            ["border-top-width"] = "0px",
            ["border-right-width"] = "0px",
            ["border-bottom-width"] = "0px",
            ["border-left-width"] = "0px",
        };

        // UA defaults for specific elements to better match Chromium
        if (tag == "body")
        {
            d["padding-top"] = "20px";
            d["padding-right"] = "20px";
            d["padding-bottom"] = "20px";
            d["padding-left"] = "20px";
            d["background-color"] = "rgb(245, 245, 245)";
        }
        else if (tag == "button")
        {
            d["display"] = "inline-block";
            d["box-sizing"] = "border-box";
            d["font-size"] = "14px";
            d["padding-top"] = "10px";
            d["padding-bottom"] = "10px";
            d["padding-left"] = "20px";
            d["padding-right"] = "20px";
            d["background-color"] = "rgb(33, 150, 243)";
            d["color"] = "rgb(255, 255, 255)";
        }
        return d;
    }

    /// <summary>HTML §2.8.2: the legacy 'createEvent' answers by event family, and the same door is
    /// what a page-built event comes through — the family is recorded on the event so its wrapper can
    /// be stamped with the interface it names (DOM §4.5.2, UI Events §2).</summary>
    public object? createEvent(string type)
    {
        var given = type ?? "Event";
        var name = given.ToLowerInvariant() switch
        {
            "event" => "Event",
            "customevent" => "CustomEvent",
            "uievent" or "uievents" => "UIEvent",
            "mouseevent" or "mouseevents" => "MouseEvent",
            "keyevent" or "keyevents" or "keyboardevent" => "KeyboardEvent",
            "focusevent" => "FocusEvent",
            "pointerevent" or "pointerevents" => "PointerEvent",
            "wheelevent" => "WheelEvent",
            "inputevent" => "InputEvent",
            "compositionevent" => "CompositionEvent",
            "dragevent" => "DragEvent",
            "touchevent" => "TouchEvent",
            "clipboardevent" => "ClipboardEvent",
            "animationevent" or "animationevents" => "AnimationEvent",
            "transitionevent" or "transitionevents" => "TransitionEvent",
            "mutationevent" => "MutationEvent",
            "closeevent" => "CloseEvent",
            "storageevent" => "StorageEvent",
            "hashchangeevent" => "HashChangeEvent",
            "popstateevent" => "PopStateEvent",
            "pagetransitionevent" => "PageTransitionEvent",
            "beforeunloadevent" => "BeforeUnloadEvent",
            "promiserejectionevent" => "PromiseRejectionEvent",
            "progressevent" => "ProgressEvent",
            _ => given
        };
        return new ScriptEvent(name, null) { Kind = name };
    }

    private ElementHost? WrapElement(Element? element)
    {
        if (element == null) return null;
        var engine = JavaScriptEngine.Current ?? Engine;
        if (engine?.IntegrationService != null)
            return engine.IntegrationService.WrapDomNode(element) as ElementHost;
        var host = new ElementHost(element);
        if (host.Engine == null && engine != null)
            host.Engine = engine;
        return host;
    }

    // ===== Private helpers =====

    private static Element? FindElementById(Element root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children.OfType<Element>())
        {
            var found = FindElementById(child, id);
            if (found != null) return found;
        }
        return null;
    }

    private static List<Element> GetElementsByTagNameInternal(Element root, string tagName)
    {
        tagName = tagName.ToUpperInvariant();
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.TagName == tagName)
                result.Add(child);
            result.AddRange(GetElementsByTagNameInternal(child, tagName));
        }
        return result;
    }

    private static List<Element> GetElementsByClassNameInternal(Element root, string className)
    {
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.HasClass(className))
                result.Add(child);
            result.AddRange(GetElementsByClassNameInternal(child, className));
        }
        return result;
    }

    private static List<Element> GetElementsByNameInternal(Element root, string name)
    {
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.GetAttribute("name") == name)
                result.Add(child);
            result.AddRange(GetElementsByNameInternal(child, name));
        }
        return result;
    }

}

public class ComputedStyleHost
{
    private readonly Dictionary<string, string> _properties;
    private readonly ComputedStyle? _style;
    private readonly Acrux.Core.Dom.Element? _owner;
    /// <summary>True when this view reads a generated box rather than the element. A generated
    /// box has no used geometry of its own to report (measured: '::before' of a 30px-wide element
    /// answers 'auto' where the element answers its used width), and its 'content' says 'none'
    /// where an element's says 'normal'.</summary>
    private readonly bool _generatedBox;

    public ComputedStyleHost(Dictionary<string, string> properties, ComputedStyle? style = null,
        Acrux.Core.Dom.Element? owner = null, bool generatedBox = false)
    {
        _properties = properties ?? new Dictionary<string, string>();
        _style = style;
        _owner = owner;
        _generatedBox = generatedBox;
    }

    /// <summary>The value as this surface reports it. Only 'content' differs: a regular element
    /// that says 'none' is reported as 'normal', because on an element the property generates
    /// nothing and that is the word the reference engine uses for it (measured), while a generated
    /// box keeps the 'none' that says it draws no box at all.</summary>
    private string? ForSurface(string dashed, string? value)
    {
        if (_style != null && !_generatedBox && dashed == "content"
            && (value == null || value == "none"))
            return "normal";
        return value;
    }

    public string? getProperty(string name) => getPropertyValue(name);

    /// <summary>The names a computed style lists, in the order the reference engine lists them:
    /// every property this engine declares under its dashed name, unprefixed first and then the
    /// prefixed spellings, each group in its own order (measured: <c>item(0)</c> is
    /// <c>'accent-color'</c> and the last entry is <c>'-webkit-writing-mode'</c>, so a plain
    /// alphabetical sort over both groups is not it). A computed style is an array-like of these
    /// names rather than of the declarations a page wrote: it answers every property, and
    /// <c>length</c> is the size of this list, not of the element's style attribute.</summary>
    public static readonly string[] PropertyNames = BuildPropertyNames();

    private static string[] BuildPropertyNames()
    {
        var plain = new List<string>();
        var prefixed = new List<string>();
        foreach (var id in System.Enum.GetValues<Acrux.Core.Css.Properties.CssPropertyId>())
        {
            // The enum carries its own bookkeeping members (MaxProperties and the like) at the
            // end of the list; they are counts, not properties, and must not be listed.
            if ((int)id <= 0
                || id >= Acrux.Core.Css.Properties.CssPropertyId.MaxProperties) continue;
            var name = Acrux.Core.Css.Properties.CssPropertyIdExtensions.ToString(id);
            if (string.IsNullOrEmpty(name)) continue;
            (name[0] == '-' ? prefixed : plain).Add(name);
        }
        plain.Sort(System.StringComparer.Ordinal);
        prefixed.Sort(System.StringComparer.Ordinal);
        // 'float' is a reserved word in JavaScript, and the IDL names the member 'cssFloat'.
        plain.Add("float");
        plain.AddRange(prefixed);
        return System.Linq.Enumerable.Distinct(plain).ToArray();
    }

    /// <summary>How many properties a computed style reports.</summary>
    public int length => PropertyNames.Length;

    /// <summary>The name at an index; past either end the empty string, which is what the IDL's
    /// DOMString return gives (measured for an inline block, and the same rule the declaration
    /// object follows).</summary>
    public string item(int index) =>
        index < 0 || index >= PropertyNames.Length ? "" : PropertyNames[index];

    /// <summary>Numeric indexing of a computed style: <c>getComputedStyle(el)[0]</c> is
    /// <c>item(0)</c>. An integer indexer is what makes that work through the interop; a string
    /// indexer would have swallowed every other member read, which is a mistake this engine has
    /// made before.</summary>
    public string this[int index] => item(index);

    /// <summary>A computed style has no serialisation of its own (CSSOM §7): the reference engine
    /// answers the empty string, whatever the element's style attribute holds (measured).</summary>
    public string cssText => "";


    /// <summary>CSSOM: a property the declaration block does not carry reads back as the
    /// empty string, never as null.</summary>
    public string getPropertyValue(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        // CSS Variables 1 §4: a custom property is a property of the computed style, and what it
        // reads back is the value the cascade substituted — '14pt' for '--a: var(--b)' written beside
        // '--b: 14pt' (measured). A name nothing on the element or its ancestors answers reads back
        // as the empty string, like any property with no value.
        if (name.StartsWith("--", StringComparison.Ordinal) && _style != null)
            return _style.GetCustomProperty(name[2..]) ?? "";
        // A name the reference engine does not declare has no value to read, whatever this engine
        // stores under it: the prefix is part of the property's identity here, and folding it away
        // (as the canonicaliser does for the cascade) would make 'text-security' answer for a page
        // that wrote '-webkit-text-security'. Measured: the unprefixed read is the empty string.
        if (Acrux.Core.Css.Resolver.CssPropertyTraits.IsEngineOnlyName(name)) return "";
        var lower = Acrux.Core.Css.Properties.CssPropertyIdExtensions.CanonicalName(name.ToLowerInvariant());
        // The serialiser is the measured surface for every property it knows, so it answers
        // first; the hand-written map is only a fallback for a name it leaves out. Reading the
        // map first would keep its older spellings ('align-items: flex-end' for an authored
        // 'end', a border width that ignores 'border-style: none') alive.
        if (_style != null)
        {
            var serialized = Acrux.Core.Dom.Animations.ComputedValueSerializer.Get(_style, lower, includeShorthands: true);
            if (serialized != null)
                return ForSurface(lower, UsedValue(lower, serialized) ?? serialized) ?? "";
        }
        return ForSurface(lower, _properties.TryGetValue(lower, out var value) ? value : null) ?? "";
    }

    /// <summary>The value as the reference engine would print it: the serialiser's text, put onto
    /// the box it belongs to. Two kinds of change happen here. Addresses are absolute (see
    /// <see cref="ResolveUrls"/>), which needs the document but no layout. And a few values are
    /// reported as the box's <c>used</c> value rather than the value the page wrote (measured): a
    /// box size left 'auto' answers with the size layout gave it — the content box, so a padded
    /// box still reports its authored content width — and 'transform-origin' answers in pixels
    /// from the border box's top-left corner, the only box a percentage of the origin resolves
    /// against. Those need a layout object, which an inline, a detached element or a read before
    /// the first layout does not have; they keep the serialiser's text.</summary>
    private string? UsedValue(string name, string text)
    {
        // Addresses are still absolute for a generated box, so the url pass runs; what it has no
        // part in is the geometry — a generated box reports no used value, and its sizes are what
        // the page wrote, which for 'width'/'height' is the initial 'auto' (measured).
        if (_generatedBox) return ResolveUrls(text);
        var withAddresses = ResolveUrls(text);
        var box = _owner?.LayoutBox;
        if (box == null) return withAddresses == text ? null : withAddresses;
        switch (name)
        {
            // A box size is the one number the computed surface reports in layout units, and in
            // pixels even when the page wrote a percentage (measured: 'width: 33.3px' reads back
            // '33.2969px', 'width: 25%' of a 400px box reads '100px', and 'width: auto' reads the
            // content box the layout produced). A percentage we cannot resolve yet — the box the
            // reference engine measures it against is the containing block's, which this surface
            // does not look at — stays the authored percentage.
            // The logical pair reports the used value of the physical side it selects, exactly as
            // its twin does (CSS Logical 1 §4.2; measured: 'inline-size: 50%' and 'width: 50%'
            // answer the same number, where the name alone decided whether the layout was asked).
            case "width": case "inline-size": return SizeUsed(text, box.ContentBox.Width);
            case "height": case "block-size": return SizeUsed(text, box.ContentBox.Height);
            case "margin-top": case "margin-right": case "margin-bottom": case "margin-left":
            case "padding-top": case "padding-right": case "padding-bottom": case "padding-left":
            case "margin-block-start": case "margin-block-end":
            case "margin-inline-start": case "margin-inline-end":
            case "padding-block-start": case "padding-block-end":
            case "padding-inline-start": case "padding-inline-end":
                return SideUsed(name, text, box);
            // A positioned box's inset reports the offset layout will apply, so its percentage is
            // priced against the containing block (CSS 2.1 §10.5/§10.6: the vertical pair against
            // its height, the horizontal pair against its width). A static box moves nothing, so its
            // inset keeps the serialiser's text with the '%' left in it (measured).
            case "top": case "bottom":
            case "inset-block-start": case "inset-block-end":
                return InsetUsed(name, text, box, vertical: true);
            case "left": case "right":
            case "inset-inline-start": case "inset-inline-end":
                return InsetUsed(name, text, box, vertical: false);

            case "transform-origin":
            case "perspective-origin":
                return TransformOriginUsed(text, LayoutUnit(box.BorderBox.Width),
                    LayoutUnit(box.BorderBox.Height)) ?? withAddresses;

            case "transform":
            {
                // A translate() percentage is measured against the element's own border box, so the
                // computed matrix is only right once the box has been laid out (CSS Transforms 2 §4;
                // measured: 'translate(10%, 20%)' on a 100x50 box reads
                // 'matrix(1, 0, 0, 1, 10, 10)'). With no percentage in the list the serialiser's own
                // answer needs nothing from here.
                var authored = _style?.Transform;
                if (string.IsNullOrEmpty(authored)
                    || !authored.Contains('%', StringComparison.Ordinal)) return withAddresses;
                return Acrux.Core.Dom.Animations.ComputedValueSerializer.TransformText(
                    Acrux.Core.Css.TransformParser.ResolveTranslatePercentages(authored,
                        box.BorderBox.Width, box.BorderBox.Height));
            }
            default:
                return withAddresses == text ? null : withAddresses;
        }
    }

    /// <summary>One box size on the computed surface: the used value where the authored value was
    /// 'auto' or carried a percentage, the authored pixel value snapped to a layout unit
    /// otherwise.</summary>
    private static string? SizeUsed(string text, float used)
    {
        if (text == "auto") return Px(LayoutUnit(used));
        if (text.EndsWith("px") && TryNumber(text.Substring(0, text.Length - 2), out var px))
            return Px(LayoutUnit((float)px));
        // A box size is reported as the number the layout produced whatever the page wrote, so a
        // percentage needs no base of its own once the box exists (measured: 'width: calc(1px + 1%)'
        // answers one number, and 'width: 25%' of a 400px containing block answers '100px').
        if (text.IndexOf('%') >= 0) return Px(LayoutUnit(used));
        return null;
    }

    /// <summary>A margin or padding the page wrote as a percentage: CSS 2.1 §8.3 and §8.5 resolve it
    /// against the containing block's inline size, and the computed surface reports the product
    /// (measured: 'margin-top: calc(10px + 1%)' of a 365.625px containing block is '13.6562px', never
    /// the sum with a '%' left in it). A length with no percentage in it is the serialiser's own
    /// answer and stays untouched.</summary>
    private string? SideUsed(string name, string text, Acrux.Core.Dom.LayoutBox box)
    {
        if (_style == null || text.IndexOf('%') < 0) return null;
        bool rtl = string.Equals(_style.Direction, "rtl", StringComparison.OrdinalIgnoreCase);
        var length = name switch
        {
            "margin-top" or "margin-block-start" => _style.MarginTop,
            "margin-right" => _style.MarginRight,
            "margin-bottom" or "margin-block-end" => _style.MarginBottom,
            "margin-left" => _style.MarginLeft,
            "margin-inline-start" => rtl ? _style.MarginRight : _style.MarginLeft,
            "margin-inline-end" => rtl ? _style.MarginLeft : _style.MarginRight,
            "padding-top" or "padding-block-start" => _style.PaddingTop,
            "padding-right" => _style.PaddingRight,
            "padding-bottom" or "padding-block-end" => _style.PaddingBottom,
            "padding-left" => _style.PaddingLeft,
            "padding-inline-start" => rtl ? _style.PaddingRight : _style.PaddingLeft,
            "padding-inline-end" => rtl ? _style.PaddingLeft : _style.PaddingRight,
            _ => null,
        };
        if (length == null) return null;
        // The box the percentage belongs to: the containing block the layout resolved the box's own
        // size against, its parent when that link is not filled in, and the viewport for a box whose
        // containing block is the initial one.
        var inlineSize = box.ContainingBlock?.ContentBox.Width
            ?? box.Parent?.ContentBox.Width
            ?? _style.ComputedViewportWidth;
        if (inlineSize <= 0) return null;
        return _style.ComputedLengthCss(length, percentageBase: inlineSize);
    }

    /// <summary>An inset the page wrote with a percentage on a box that is actually positioned.
    /// cssom-view reports the <em>used</em> offset there, so the percentage is priced against the
    /// containing block — its height for the vertical pair, its width for the horizontal one — while
    /// a static box, which the offset does not move, keeps the authored text (measured:
    /// 'top: calc(10px + 1%)' of an absolutely positioned box in a 200px-tall block is '12px', of a
    /// static one 'calc(1% + 10px)').</summary>
    private string? InsetUsed(string name, string text, Acrux.Core.Dom.LayoutBox box, bool vertical)
    {
        if (_style == null || text.IndexOf('%') < 0) return null;
        if (_style.Position == Acrux.Core.Dom.PositionType.Static) return null;
        float baseSize = vertical
            ? box.ContainingBlock?.ContentBox.Height ?? box.Parent?.ContentBox.Height
              ?? _style.ComputedViewportHeight
            : box.ContainingBlock?.ContentBox.Width ?? box.Parent?.ContentBox.Width
              ?? _style.ComputedViewportWidth;
        if (baseSize <= 0) return null;
        bool rtl = string.Equals(_style.Direction, "rtl", StringComparison.OrdinalIgnoreCase);
        var length = name switch
        {
            "top" or "inset-block-start" => _style.Top,
            "bottom" or "inset-block-end" => _style.Bottom,
            "left" => _style.Left,
            "right" => _style.Right,
            "inset-inline-start" => rtl ? _style.Right : _style.Left,
            "inset-inline-end" => rtl ? _style.Left : _style.Right,
            _ => null,
        };
        if (length == null) return null;
        return _style.ComputedLengthCss(length, percentageBase: baseSize);
    }

    /// <summary>The reference engine's layout works in units of 1/64 of a CSS pixel and every
    /// used value it prints is one of those, truncated (measured: a box authored 'width: 33.3px'
    /// answers '33.2969px' — 2131/64 — and 'height: 9.7px' answers '9.6875px' — 620/64). Putting
    /// our float sizes through the same quantisation makes the same box answer the same number.</summary>
    private static double LayoutUnit(float value) => Math.Floor(value * 64) / 64;

    private static string Px(double value) =>
        Acrux.Core.Dom.Animations.CssValueTokenizer.Num(value) + "px";

    /// <summary>Rewrites every url() in a value onto the address it resolves to, in the quoted
    /// spelling the reference engine prints (measured: a layer written 'url(a.png)' reads back
    /// as 'url("file:///…/a.png")' — the address the fetch will use, not the token the page
    /// typed). A value that mentions no url() is returned untouched, so the scan is free for
    /// everything else.
    ///
    /// An address that is only a fragment is the one exception: it names something inside this
    /// document — a '&lt;filter&gt;' or a '&lt;mask&gt;' element — and resolving it against the
    /// page's own URL would describe a different resource to a script that compares the two
    /// (measured, and the same for background-image, mask-image and filter: 'url(#ff)' reads
    /// back as 'url("#ff")', while 'url(a.svg#ff)' is made absolute like any other).</summary>
    private string ResolveUrls(string text)
    {
        if (text.IndexOf("url(", StringComparison.OrdinalIgnoreCase) < 0) return text;
        string? baseText = _owner?.OwnerDocument?.Url;
        if (string.IsNullOrEmpty(baseText)) return text;
        if (!System.Uri.TryCreate(baseText, UriKind.Absolute, out var baseUri)) return text;
        var sb = new System.Text.StringBuilder(text.Length + 32);
        int i = 0;
        while (i < text.Length)
        {
            int at = text.IndexOf("url(", i, StringComparison.OrdinalIgnoreCase);
            if (at < 0) { sb.Append(text, i, text.Length - i); break; }
            sb.Append(text, i, at - i);
            int close = IndexMatchingParen(text, at + 3);
            if (close < 0) { sb.Append(text, at, text.Length - at); break; }
            var inner = text.Substring(at + 4, close - at - 4).Trim();
            if (inner.Length >= 2 && (inner[0] == '"' || inner[0] == '\'') && inner[^1] == inner[0])
                inner = inner.Substring(1, inner.Length - 2);
            sb.Append("url(\"")
              .Append(inner.StartsWith('#') ? inner : Absolute(inner, baseUri))
              .Append("\")");
            i = close + 1;
        }
        return sb.ToString();
    }

    private static string Absolute(string address, System.Uri baseUri)
    {
        if (string.IsNullOrEmpty(address)) return address;
        return System.Uri.TryCreate(baseUri, address, out var resolved)
            ? resolved.AbsoluteUri : address;
    }

    /// <summary>The index of the ')' that closes the '(' at <paramref name="open"/>, or -1.</summary>
    private static int IndexMatchingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>The origin as pixels: '50%' of the border box's width, a length unchanged. The
    /// serialiser has already put the value into its positional form with the keywords folded onto
    /// percentages. A third value is the depth, which belongs to no axis and therefore to no box —
    /// it is only re-spelled, never resolved. Each endpoint is a layout unit of its own, so a
    /// percentage of a quantised width is quantised again on the way out (measured: '11.0879px' =
    /// 33.296875 × 33.3%).</summary>
    private static string? TransformOriginUsed(string text, double width, double height)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4) return PositionedOriginUsed(parts, width, height);
        if (parts.Length != 2 && parts.Length != 3) return null;
        var x = OriginEndpointUsed(parts[0], width);
        var y = OriginEndpointUsed(parts[1], height);
        if (x == null || y == null) return null;
        if (parts.Length == 3)
        {
            // The depth is a <length> by grammar, so a percentage here is somebody else's value
            // and the whole used value falls back to what the serialiser printed.
            var z = OriginEndpointUsed(parts[2], 0);
            return z == null ? null : $"{x} {y} {z}";
        }
        return $"{x} {y}";
    }

    /// <summary>The four-component &lt;position&gt;: each axis is an edge keyword with a length
    /// measured from it, in either order (CSS Position 3 §5.2). Measured against a 200×0 box:
    /// 'right 10px bottom 20px' resolves to '190px -20px', 'left 10px top 20px' to '10px 20px' and
    /// 'left 10% top 20%' to '20px 0px' — so the offset is priced against the axis it sits on, the
    /// same box the keyword names rather than nothing.</summary>
    private static string? PositionedOriginUsed(string[] parts, double width, double height)
    {
        string? x = null, y = null;
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var keyword = parts[i].ToLowerInvariant();
            bool horizontal = keyword is "left" or "right";
            double size = horizontal ? width : height;
            var length = OriginEndpointUsed(parts[i + 1], size);
            if (length == null || !TryNumber(length[..^2], out var px)) return null;
            double offset = keyword switch
            {
                "left" or "top" => px,
                "right" or "bottom" => size - px,
                "center" => size / 2 + px,
                _ => double.NaN,
            };
            if (double.IsNaN(offset)) return null;
            var resolved = Px(LayoutUnit((float)offset));
            if (horizontal) x = resolved; else y = resolved;
        }
        return x == null || y == null ? null : $"{x} {y}";
    }

    private static string? OriginEndpointUsed(string token, double size)
    {
        // The box it resolves against is a layout unit; the product of the percentage is not
        // snapped again (measured: 33.296875 × 33.3% reads back '11.0879px', not '11.0781px').
        if (token.EndsWith("%"))
        {
            if (!TryNumber(token.Substring(0, token.Length - 1), out var percent)) return null;
            return Px(percent / 100.0 * size);
        }
        if (token.EndsWith("px"))
        {
            return TryNumber(token.Substring(0, token.Length - 2), out var px)
                ? Px(LayoutUnit((float)px)) : null;
        }
        return null;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>Backs the wrapper the shim installs over a computed style: every longhand is
    /// readable as a camelCase property too, and this object only has hand-written members
    /// for the ones the C# side spelled out. Null means the name is not a CSS property at
    /// all, which the wrapper reports as undefined — as distinct from the empty string a
    /// real property with no value gives.</summary>
    public string? __resolve(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // A custom property is reached the same way as any other member of the computed style, and
        // answers the substituted value (CSS Variables 1 §4, measured). Unlike the dashed read it
        // has no empty string to give: a name nothing answers is no member at all.
        if (name.StartsWith("--", StringComparison.Ordinal))
            return _style?.GetCustomProperty(name[2..]);
        var dashed = Acrux.Core.Css.Properties.CssPropertyIdExtensions
            .CanonicalName(Hyphenate(name));
        // The same refusal the dashed read makes: a name the reference engine does not declare is
        // not a member of a computed style at all, so 'getComputedStyle(el).textSecurity' and
        // 'getComputedStyle(el)["text-security"]' are undefined rather than the value the prefixed
        // property carries (measured both).
        if (Acrux.Core.Css.Resolver.CssPropertyTraits.IsEngineOnlyName(dashed)) return null;
        // The empty block a pseudo-element read that matched nothing answers the empty string for
        // every real property, exactly as its dashed twin does (measured); a name that is no
        // property at all stays undefined.
        if (_style == null && _properties.Count == 0)
            return Acrux.Core.Css.Resolver.CssPropertyTraits.IsDeclaredProperty(dashed) ? "" : null;
        // Same precedence as getPropertyValue: the serialiser is the surface that knows the
        // computed-value model, and a camelCase read must not fall back onto the older
        // spellings the hand-built map still carries.
        if (_style != null)
        {
            var serialized = Acrux.Core.Dom.Animations.ComputedValueSerializer.Get(_style, dashed, includeShorthands: true);
            if (serialized != null) return ForSurface(dashed, UsedValue(dashed, serialized) ?? serialized);
        }
        return ForSurface(dashed, _properties.TryGetValue(dashed, out var value) ? value : null);
    }

    /// <summary>'borderTopColor' to 'border-top-color'. A name that already contains a dash
    /// is either dashed or prefixed, and for both the dash is meaningful, so only its case
    /// needs fixing.</summary>
    private static string Hyphenate(string name)
    {
        if (name.IndexOf('-') >= 0) return name.ToLowerInvariant();
        // Two spellings the IDL gives a property that the dashed form cannot express. 'float'
        // is a reserved word, so the declaration object calls it 'cssFloat'; and a legacy
        // prefixed property is reached as 'webkitAppearance', which hyphenating would put on
        // the wrong side of the dash entirely (measured: both answer the property's value).
        if (name.Equals("cssFloat", StringComparison.OrdinalIgnoreCase)) return "float";
        foreach (var prefix in new[] { "webkit", "moz", "ms", "o" })
        {
            if (name.Length > prefix.Length + 1
                && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && char.IsUpper(name[prefix.Length]))
                // The rest of the member name is camelCase like any other, so it hyphenates the
                // same way: 'webkitTextSecurity' is '-webkit-text-security' and not
                // '-webkit-textsecurity' (measured: the member answers the property's value).
                return "-" + prefix + "-" + Hyphenate(name[prefix.Length..]);
        }
        var sb = new System.Text.StringBuilder(name.Length + 8);
        foreach (var ch in name)
        {
            if (char.IsUpper(ch))
            {
                if (sb.Length > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    // Common camelCase shortcuts for JS consumers
    public string? width => getPropertyValue("width");
    public string? height => getPropertyValue("height");
    public string? display => getPropertyValue("display");
    public string? position => getPropertyValue("position");
    public string? boxSizing => getPropertyValue("box-sizing");
    public string? color => getPropertyValue("color");
    public string? fontFamily => getPropertyValue("font-family");
    public string? fontSize => getPropertyValue("font-size");
    public string? fontWeight => getPropertyValue("font-weight");
    public string? fontStyle => getPropertyValue("font-style");
    public string? lineHeight => getPropertyValue("line-height");
    public string? textAlign => getPropertyValue("text-align");
    public string? visibility => getPropertyValue("visibility");
    public string? overflow => getPropertyValue("overflow");
    public string? opacity => getPropertyValue("opacity");
    public string? backgroundColor => getPropertyValue("background-color");
    public string? marginTop => getPropertyValue("margin-top");
    public string? marginRight => getPropertyValue("margin-right");
    public string? marginBottom => getPropertyValue("margin-bottom");
    public string? marginLeft => getPropertyValue("margin-left");
    public string? paddingTop => getPropertyValue("padding-top");
    public string? paddingRight => getPropertyValue("padding-right");
    public string? paddingBottom => getPropertyValue("padding-bottom");
    public string? paddingLeft => getPropertyValue("padding-left");
    public string? borderTopWidth => getPropertyValue("border-top-width");
    public string? borderRightWidth => getPropertyValue("border-right-width");
    public string? borderBottomWidth => getPropertyValue("border-bottom-width");
    public string? borderLeftWidth => getPropertyValue("border-left-width");
}

public class DocumentFragmentHost : IDomPrototypeHost
{
    private readonly DocumentFragment _fragment;

    public DocumentFragmentHost(DocumentFragment fragment) => _fragment = fragment;

    internal DocumentFragment NativeFragment => _fragment;

    public string nodeName => "#document-fragment";
    public int nodeType => 11;
    public string? __domType => "DocumentFragment";
    public string[] __domTypeChain => new[] { "EventTarget", "Node", "DocumentFragment" };

    public object? querySelector(string selector)
    {
        foreach (var el in EnumerateElements(_fragment))
        {
            var m = CssSelectorMatcher.FirstDescendant(el, selector, el, includeRoot: true);
            if (m != null) return Wrap(m);
        }
        return null;
    }

    public object querySelectorAll(string selector)
    {
        var list = new List<DomElement>();
        foreach (var el in EnumerateElements(_fragment))
            CssSelectorMatcher.CollectDescendants(el, selector, list, el, includeRoot: true);
        return list.Select(e => (object)Wrap(e)!).ToArray();
    }

    public object append(object? child)
    {
        var node = ElementHost.UnwrapNode(child);
        if (node == null) return child!;
        if (node is Acrux.Core.Dom.DocumentFragment f)
            foreach (var moved in f.Children.ToList()) _fragment.AppendChild(moved);
        else _fragment.AppendChild(node);
        return child!;
    }

    public object prepend(object? child)
    {
        var node = ElementHost.UnwrapNode(child);
        if (node == null) return child!;
        var first = _fragment.Children.FirstOrDefault();
        if (node is Acrux.Core.Dom.DocumentFragment f)
            foreach (var moved in f.Children.ToList()) _fragment.InsertBefore(moved, first);
        else _fragment.InsertBefore(node, first);
        return child!;
    }

    // A document fragment is a Node, so the whole mutation surface it is used through —
    // building up a subtree before inserting it — has to answer, not just 'append'.
    public object appendChild(object? child)
    {
        var node = ElementHost.UnwrapNode(child);
        if (node == null)
            throw new DOMException("The node to be appended is not a node of this document.", "NotFoundError");
        if (node is Acrux.Core.Dom.DocumentFragment f)
            foreach (var moved in f.Children.ToList()) _fragment.AppendChild(moved);
        else _fragment.AppendChild(node);
        return child!;
    }

    public object insertBefore(object? newChild, object? refChild)
    {
        var node = ElementHost.UnwrapNode(newChild);
        if (node == null)
            throw new DOMException("The node to be inserted is not a node of this document.", "NotFoundError");
        Node? refNode = refChild == null ? null : ElementHost.UnwrapNode(refChild);
        if (node is Acrux.Core.Dom.DocumentFragment f)
            foreach (var moved in f.Children.ToList()) _fragment.InsertBefore(moved, refNode);
        else _fragment.InsertBefore(node, refNode);
        return newChild!;
    }

    public object removeChild(object? child)
    {
        var node = ElementHost.UnwrapNode(child);
        if (node == null)
            throw new DOMException("The node to be removed is not a child of this node.", "NotFoundError");
        _fragment.RemoveChild(node);
        return child!;
    }

    public object replaceChild(object? newChild, object? oldChild)
    {
        var newNode = ElementHost.UnwrapNode(newChild);
        var oldNode = ElementHost.UnwrapNode(oldChild);
        if (newNode == null || oldNode == null)
            throw new DOMException("The node to be replaced is not a child of this node.", "NotFoundError");
        _fragment.ReplaceChild(newNode, oldNode);
        return oldChild!;
    }

    public object? getElementById(string id)
    {
        foreach (var el in EnumerateElements(_fragment))
            if (el.Id == id) return Wrap(el);
        return null;
    }

    public object[] children => EnumerateElements(_fragment).Select(e => (object)Wrap(e)!).ToArray();

    public object[] childNodes
    {
        get
        {
            var r = new List<object>();
            foreach (var n in _fragment.Children)
                r.Add(WrapAnyNode(n)!);
            return r.ToArray();
        }
    }

    public int childElementCount => EnumerateElements(_fragment).Count();

    // A fragment is a Node, so the tree reads a page makes after 'extractContents' or while it
    // builds a subtree up answer the same as an element's do (DOM §3.7).
    public object? ownerDocument => WrapAnyNode(_fragment.OwnerDocument);
    public object? parentElement => _fragment.ParentElement == null ? null : WrapAnyNode(_fragment.ParentElement);
    // ParentNode's element reads, which a template's content is used through more than anywhere
    // else in the platform (§4.12.3): 'content.firstElementChild' is the idiom a page clones by.
    public object? firstElementChild
    {
        get
        {
            foreach (var child in _fragment.Children)
                if (child is DomElement) return WrapAnyNode(child);
            return null;
        }
    }
    public object? lastElementChild
    {
        get
        {
            for (var i = _fragment.Children.Count - 1; i >= 0; i--)
                if (_fragment.Children[i] is DomElement) return WrapAnyNode(_fragment.Children[i]);
            return null;
        }
    }
    public object? firstChild => WrapAnyNode(_fragment.FirstChild);
    public object? lastChild => WrapAnyNode(_fragment.LastChild);
    public object? parentNode => WrapAnyNode(_fragment.ParentNode);
    public object? nextSibling => WrapAnyNode(_fragment.NextSibling);
    public object? previousSibling => WrapAnyNode(_fragment.PreviousSibling);
    public bool hasChildNodes() => _fragment.Children.Count > 0;
    public string? nodeValue { get => null; set { } }
    public void remove() => _fragment.Remove();

    public object cloneNode(bool deep = true)
        => JsIntegrationService.WrapAny(_fragment.CloneNode(deep)) ?? this;

    public string? textContent
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            CollectText(_fragment, sb);
            return sb.ToString();
        }
        set
        {
            _fragment.Children.Clear();
            if (!string.IsNullOrEmpty(value))
                _fragment.AppendChild(new Acrux.Core.Dom.TextNode(value));
        }
    }

    private static void CollectText(Acrux.Core.Dom.Node node, System.Text.StringBuilder sb)
    {
        if (node is Acrux.Core.Dom.TextNode tn) sb.Append(tn.TextContent);
        foreach (var child in node.Children) CollectText(child, sb);
    }

    private static IEnumerable<DomElement> EnumerateElements(Acrux.Core.Dom.Node root)
    {
        foreach (var child in root.Children)
        {
            if (child is DomElement el)
            {
                yield return el;
                foreach (var d in EnumerateElements(el)) yield return d;
            }
        }
    }

    private object? Wrap(DomElement? el)
    {
        if (el == null) return null;
        var engine = JavaScriptEngine.Current ?? Acrux.Core.JavaScript.JavaScriptEngine.Current;
        if (engine?.IntegrationService != null)
            return engine.IntegrationService.WrapDomNode(el);
        return new ElementHost(el);
    }

    private object? WrapAnyNode(Acrux.Core.Dom.Node n) => JsIntegrationService.WrapAny(n);
}

/// <summary>DOM §4.3.4: the doctype is a node of the document, not a bundle of strings — it reads
/// its name, public and system identifiers, and it sits in the document's child list with the rest.
/// Its 'nodeValue' and 'textContent' answer null and cannot be written: a doctype has no data.</summary>
public class DocumentTypeHost : IDomPrototypeHost
{
    private readonly DocumentTypeNode _docType;

    public DocumentTypeHost(DocumentTypeNode docType)
    {
        _docType = docType;
    }

    internal DocumentTypeNode NativeNode => _docType;

    public string name => _docType.Name;
    public string publicId => _docType.PublicId ?? "";
    public string systemId => _docType.SystemId ?? "";

    public string nodeName => _docType.Name;
    public int nodeType => 10;
    public string? nodeValue => null;
    public string? textContent => null;
    public string[] __domTypeChain => new[] { "EventTarget", "Node", "DocumentType" };
    public object? ownerDocument
        => JsIntegrationService.WrapAny(_docType.OwnerDocument) ?? JavaScriptEngine.Current?.IntegrationService?.Document;
    public object? parentNode => JsIntegrationService.WrapAny(_docType.Parent);
    public object? parentElement => null;
    public object? previousSibling => JsIntegrationService.WrapAny(_docType.PreviousSibling);
    public object? nextSibling => JsIntegrationService.WrapAny(_docType.NextSibling);
    public object[] childNodes => Array.Empty<object>();
    public int length => 0;
    public object? firstChild => null;
    public object? lastChild => null;
    public bool hasChildNodes() => false;
    // The two legacy collections a doctype used to hold its entities and notations in are gone from
    // the interface; the reference still answers null for them, so a page that looks finds nothing
    // rather than an undefined member.
    public object? entities => null;
    public object? notations => null;
    public object? cloneNode(bool deep) => JsIntegrationService.WrapAny(_docType.CloneNode(deep));
    public void remove() => _docType.Parent?.RemoveChild(_docType);
}

/// <summary>DOM §4.3.3: the attribute list of an element is live and array-like — 'el.attributes[0]'
/// and 'el.attributes.length' read the current set, and every position answers the same Attr object
/// 'getNamedItem' gives for that name.</summary>
public class NamedNodeMapHost : IList
{
    private readonly DomElement _element;

    public NamedNodeMapHost(DomElement element)
    {
        _element = element;
    }

    public int length => Names().Count;
    public int Length => Names().Count;

    public object? this[int index]
    {
        get => item(index);
        set => throw new NotSupportedException();
    }

    public object? item(int index)
    {
        var names = Names();
        return index >= 0 && index < names.Count ? AttributeHost.For(_element, names[index]) : null;
    }

    public object? getNamedItem(string name)
    {
        // NamedNodeMap answers Attr nodes, not the attribute's string value — a page that reads
        // getNamedItem('id').value would otherwise find 'value' on a plain string.
        if (!_element.HasAttribute(name)) return null;
        return AttributeHost.For(_element, name);
    }

    /// <summary>DOM §4.2.1: the namespaced getter is looked up by the namespace and the local name,
    /// not by the spelling the page wrote the attribute in — an attribute put on as 'x:y' under a
    /// namespace is found as 'y' with that namespace, and the same Attr object comes back.</summary>
    public object? getNamedItemNS(string? ns, string name)
    {
        var stored = _element.FindAttributeName(ns, name);
        return stored == null ? null : AttributeHost.For(_element, stored);
    }

    public object? setNamedItem(object attr) => ElementHost.SetAttributeOn(_element, attr);

    /// <summary>DOM §4.2.1: the namespaced setter is the same operation — a name brought without a
    /// prefix is put on under the namespace it carries, and the attribute it replaces is handed back.</summary>
    public object? setNamedItemNS(object attr) => ElementHost.SetAttributeOn(_element, attr);

    public object? removeNamedItem(string name)
    {
        if (!_element.HasAttribute(name))
            throw new DOMException("The named item does not exist.", "NotFoundError");
        var removed = AttributeHost.For(_element, name);
        AttributeHost.Release(removed);
        _element.RemoveAttribute(name);
        return removed;
    }

    public object? removeNamedItemNS(string? ns, string name)
    {
        var stored = _element.FindAttributeName(ns, name);
        if (stored == null)
            throw new DOMException("The named item does not exist.", "NotFoundError");
        var removed = AttributeHost.For(_element, stored);
        AttributeHost.Release(removed);
        _element.RemoveAttribute(stored);
        return removed;
    }

    public IEnumerator GetEnumerator() => Names().Select(n => (object?)AttributeHost.For(_element, n)).ToList().GetEnumerator();

    public int Count => Names().Count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => Names().Any(n => ReferenceEquals(AttributeHost.For(_element, n), value));
    public int IndexOf(object? value)
    {
        var names = Names();
        for (var i = 0; i < names.Count; i++)
            if (ReferenceEquals(AttributeHost.For(_element, names[i]), value)) return i;
        return -1;
    }
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) => ToArray().CopyTo(array, index);

    public object?[] ToArray() => Names().Select(n => (object?)AttributeHost.For(_element, n)).ToArray();

    private List<string> Names()
    {
        // The element keeps its attributes in insertion order, which is the order the list reads.
        return _element.Attributes.Keys.ToList();
    }
}

/// <summary>DOM §4.3: an attribute the page holds is one object per (element, name) for as long
/// as it is attached, and reading or writing 'value' goes to the element's attribute — not to a
/// copy taken when the page looked at it. A removal hands back the attribute it took off, with
/// the value it held, and that one is detached: it answers its own value from then on.</summary>
public class AttributeHost : IDomPrototypeHost
{
    private static readonly ConditionalWeakTable<DomElement, Dictionary<string, AttributeHost>> _views = new();

    private DomElement? _owner;
    private DomDocument? _document;
    private string? _detached;
    private bool _released;

    public string name { get; }
    public bool specified => true;
    public bool isId => name.Equals("id", StringComparison.OrdinalIgnoreCase);
    public string? namespaceURI { get; internal set; }
    public string nodeName => name;

    // DOM §4.3.3: the two halves of an attribute's name. An attribute that carries a namespace is
    // split at its first ':' — 'x:y' is prefix 'x', local name 'y'; one the parser made in an HTML
    // document is not split at all, so 'xml:lang' keeps its whole local name and has no prefix
    // (measured).
    public string localName => namespaceURI == null ? name : LocalPart(name);
    public string? prefix => namespaceURI == null ? null : PrefixPart(name);
    public int nodeType => 2;
    public string? textContent { get => value; set => this.value = value; }
    public string[] __domTypeChain => new[] { "EventTarget", "Node", "Attr" };
    public object? ownerElement => _owner != null && !_released ? JsIntegrationService.WrapAny(_owner) : null;

    /// <summary>DOM §4.3.3: an attribute belongs to a document whether or not an element holds it,
    /// and taking it off the element does not take it off the document — 'createAttribute' gives it
    /// the document that made it. It is the page's own 'document', not a second wrapper of it.</summary>
    public object? ownerDocument
        => JsIntegrationService.WrapAny(_owner?.OwnerDocument ?? _document)
           ?? JavaScriptEngine.Current?.IntegrationService?.Document;

    public override string ToString() => value ?? "";

    internal AttributeHost(DomElement? owner, string name, string? ns = null)
    {
        _owner = owner;
        this.name = name;
        namespaceURI = ns;
    }

    /// <summary>A detached attribute: 'document.createAttribute' makes one, and it answers its
    /// own value until an element takes it.</summary>
    public AttributeHost(string name, string? value = null, string? ns = null, DomDocument? document = null)
        : this((DomElement?)null, name, ns)
    {
        _detached = value;
        _document = document;
    }

    public string? value
    {
        get => _released ? _detached : (_owner != null ? _owner.GetAttribute(name) : _detached);
        set
        {
            if (_released || _owner == null) _detached = value;
            else _owner.SetAttribute(name, value ?? "");
        }
    }

    internal DomElement? NativeOwner => _owner;
    internal bool IsAttached => _owner != null && !_released && _owner.HasAttribute(name);

    /// <summary>The attribute of an element, the same object every time while it is attached.</summary>
    internal static AttributeHost For(DomElement element, string name)
    {
        var map = _views.GetValue(element, _ => new Dictionary<string, AttributeHost>(StringComparer.Ordinal));
        if (!map.TryGetValue(name, out var view) || view._released)
        {
            // The element remembers which namespace each attribute was set under, and an attribute
            // read back out of the element is the same object whether the page came at it by its
            // qualified name or by its namespace and local name.
            view = new AttributeHost(element, name,
                element.AttributeNamespaces.TryGetValue(name, out var ns) ? ns : null);
            map[name] = view;
        }
        return view;
    }

    private static string LocalPart(string name)
    {
        var at = name.IndexOf(':');
        return at < 0 ? name : name[(at + 1)..];
    }

    private static string? PrefixPart(string name)
    {
        var at = name.IndexOf(':');
        return at <= 0 ? null : name[..at];
    }

    /// <summary>Takes a detached attribute onto an element, so the element answers the very
    /// object the page passed in.</summary>
    internal static void Attach(DomElement element, AttributeHost attr)
    {
        var map = _views.GetValue(element, _ => new Dictionary<string, AttributeHost>(StringComparer.Ordinal));
        attr._owner = element;
        attr._document = element.OwnerDocument;
        attr._detached = null;
        attr._released = false;
        map[attr.name] = attr;
    }

    /// <summary>Takes the attribute off the element that owned it: the page that held the object
    /// keeps reading the value it had.</summary>
    internal static void Release(object? attr)
    {
        if (attr is not AttributeHost host || host._owner == null) return;
        host._detached = host._owner.GetAttribute(host.name);
        host._released = true;
        if (_views.TryGetValue(host._owner, out var map)
            && map.TryGetValue(host.name, out var current) && ReferenceEquals(current, host))
            map.Remove(host.name);
    }
}

/// <summary>The object 'document.implementation' answers (DOM §3.3, HTML). It builds fresh,
/// detached documents — an HTML one with the root/head/body/title a page expects to find.</summary>
public class DOMImplementationHost
{
    private readonly JavaScriptEngine? _engine;
    public DOMImplementationHost(JavaScriptEngine? engine) => _engine = engine;

    public bool hasFeature(string? feature, object? version = null) => true;

    public DocumentTypeHost createDocumentType(string qualifiedName, string? publicId = null, string? systemId = null)
    {
        DomNames.RequireDocumentTypeName(qualifiedName, "createDocumentType", "DOMImplementation");
        // Wrapped, not built here: the doctype a page hands to 'createDocument' has to be the same
        // object it reads back from 'doc.doctype' and from the child list (DOM §2.8).
        return (DocumentTypeHost)JsIntegrationService.WrapAny(
            new DocumentTypeNode(publicId, systemId, qualifiedName))!;
    }

    public object createDocument(string? ns, string? qualifiedName, object? doctype = null)
    {
        if (!string.IsNullOrEmpty(qualifiedName))
            DomNames.RequireQualifiedName(qualifiedName, "createDocument", "DOMImplementation", element: true);
        var doc = new Document();
        // A document made through 'createDocument' is an XML one, and the interface the page reads
        // back ('contentType', and what a CDATA section costs it) follows from that (§3.3).
        doc.ContentType = ns == "http://www.w3.org/1999/xhtml"
            ? "application/xhtml+xml" : "application/xml";
        // The doctype the page hands over is put on before the root element (§3.3).
        if (UnwrapDocumentType(doctype) is DocumentTypeNode dt) doc.AppendChild(dt);
        if (!string.IsNullOrEmpty(qualifiedName))
        {
            var root = doc.CreateElementNS(ns, qualifiedName);
            doc.AppendChild(root);
            doc.DocumentElement = root;
        }
        // Wrapped, not built here, so the document the page holds is the one every node it contains
        // names as its own (DOM §2.8).
        return JsIntegrationService.WrapAny(doc) ?? new DocumentHost(doc) { Engine = _engine };
    }

    private static DocumentTypeNode? UnwrapDocumentType(object? value) => value switch
    {
        DocumentTypeHost host => host.NativeNode,
        DocumentTypeNode node => node,
        _ => null
    };

    public object createHTMLDocument(string? title = null)
    {
        var doc = new Document();
        // An HTML document made by the implementation carries its doctype, the way the parser's
        // does — a page that reads 'other.doctype.name' after creating one expects 'html'.
        doc.AppendChild(new DocumentTypeNode(null, null, "html"));
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        doc.DocumentElement = html;
        var head = doc.CreateElement("head");
        html.AppendChild(head);
        if (!string.IsNullOrEmpty(title))
        {
            var titleEl = doc.CreateElement("title");
            titleEl.TextContent = title;
            head.AppendChild(titleEl);
            doc.Title = title;
        }
        var body = doc.CreateElement("body");
        html.AppendChild(body);
        // The document's body is a property of the document, not something read back out of the tree,
        // so a document built here had no body until it was written down: 'other.body' answered null
        // and every range the page then tried to put in the new document was refused for it.
        doc.Body = body;
        return JsIntegrationService.WrapAny(doc) ?? new DocumentHost(doc) { Engine = _engine };
    }
}
