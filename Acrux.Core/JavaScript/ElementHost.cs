using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Acrux.Core.Css;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;

namespace Acrux.Core.JavaScript;

public class ElementHost : IDomPrototypeHost
{
    private readonly Element _element;
    private CssStyleDeclaration? _styleHost;
    private JsNodeList? _children;
    private JsNodeList? _childNodes;
    private NamedNodeMapHost? _attributes;
    private DomTokenListHost? _classList;
    private RelTokenListHost? _relList;
    private readonly Dictionary<string, List<int>> _eventCallbackIds = new();
    private readonly HashSet<int> _onceCallbacks = new();
    // Which phase of the dispatch walk each stored listener belongs to (DOM §2.7). The list is keyed by
    // type alone, so the flag has to sit beside the callback id it was registered with.
    private readonly Dictionary<int, bool> _listenerCapture = new();
    internal JavaScriptEngine? Engine { get; set; }

    public ElementHost(Element element)
    {
        _element = element;
        Engine = JavaScriptEngine.Current;
        JsIntegrationService.FixProto(this);
    }

    public Element NativeElement => _element;

    /// <summary>Style and layout have to be current before a synchronous geometry
    /// read answers, so each accessor below starts with this (see
    /// <see cref="JavaScriptEngine.FlushForRead"/>).</summary>
    private void FlushForRead() => (Engine ?? JavaScriptEngine.Current)?.FlushForRead();

    public string id
    {
        get => _element.Id ?? "";
        set => _element.Id = value;
    }

    public string className
    {
        get => _element.ClassName ?? "";
        set => _element.ClassName = value;
    }

    public string tagName => _element.TagName;

    public string nodeName => _element.NodeName;

    // The interface an element is read as belongs to its tag and its namespace (HTML §4.1): the page
    // asks 'input instanceof HTMLInputElement' and 'circle instanceof SVGElement', and the wrapper's
    // prototype is stamped from the name the chain ends with.
    public string __domType => InterfaceName;

    public string[] __domTypeChain
    {
        get
        {
            var name = InterfaceName;
            // The table stores the interfaces each one inherits from — the same line of descent the
            // constructor registration reads — and the chain a wrapper declares ends with the
            // interface itself, which is the level the prototype is taken from.
            if (Acrux.Core.Dom.Html.HtmlElementFactory.Interfaces.TryGetValue(name, out var bases))
            {
                var full = new string[bases.Length + 1];
                bases.CopyTo(full, 0);
                full[bases.Length] = name;
                return full;
            }
            return new[] { "Object", "EventTarget", "Node", "Element", name };
        }
    }

    private string InterfaceName
    {
        get
        {
            var ns = _element.NamespaceUri;
            if (ns == "http://www.w3.org/2000/svg")
                return _element.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase)
                    ? "SVGSVGElement" : "SVGElement";
            if (ns == "http://www.w3.org/1998/Math/MathML") return "MathMLElement";
            if (ns != null && ns != "http://www.w3.org/1999/xhtml") return "Element";
            return Acrux.Core.Dom.Html.HtmlElementFactory.InterfaceNameFor(_element.LocalName);
        }
    }

    public string? textContent
    {
        get => GetTextContent();
        set
        {
            _element.Children.Clear();
            if (!string.IsNullOrEmpty(value))
                _element.AppendChild(new TextNode(value));
        }
    }

    public string? innerHTML
    {
        get => BuildInnerHtml();
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _element.Children.Clear();
                return;
            }
            var parsed = HtmlParser.ParseFragment(value, _element.TagName);
            _element.Children.Clear();
            foreach (var child in parsed)
                _element.AppendChild(child);
        }
    }

    public string? outerHTML => BuildOuterHtml();

    public CssStyleDeclaration style => _styleHost ??= new CssStyleDeclaration(_element);

    /// <summary>The CSSOM view of the sheet this element contributes (HTML §4.8.6). Only a
    /// <c>style</c>/<c>link</c> the style engine actually loaded owns one, and the list it
    /// exposes is the very one the cascade walks, so a rule inserted here is applied rather
    /// than merely remembered by the object the script is holding.</summary>
    public CssStyleSheetHost? sheet
    {
        get
        {
            // A style element only owns a sheet once the style engine has looked at it, so
            // the read forces the pending pass first — the same rule that makes
            // getComputedStyle and the geometry getters see the page's own changes. Without
            // it a script that appends a <style> and asks for its sheet in the next statement
            // gets nothing, because nothing has parsed it yet.
            FlushForRead();
            return _element.AssociatedStyleSheet == null
                ? null
                : CssStyleSheetHost.ViewOf(_element, WrapWithCache);
        }
    }

    /// <summary>The legacy 'media' IDL of a style and link element (HTML §4.8.6, §4.2.5): it is
    /// the attribute, so writing it changes which medium the sheet applies to on the next
    /// recompute. Any other element has no such member in the reference engine, and answers
    /// nothing here for it. The IDL type is a plain DOMString and not a nullable one, so a null
    /// written to it is the WORD 'null' and not the absence of a value — which is a media type
    /// nobody knows and therefore a sheet that never applies (measured:
    /// <c>style.media = null</c> leaves the attribute reading 'null' while
    /// <c>style.media = ''</c> leaves it present and empty, and an empty medium applies
    /// everywhere).</summary>
    public object? media
    {
        get => IsMediaElement(_element) ? _element.GetAttribute("media") ?? "" : null;
        set
        {
            if (!IsMediaElement(_element)) return;
            _element.SetAttribute("media", CssMediaListHost.DomString(value));
        }
    }

    private static bool IsMediaElement(Element element) =>
        string.Equals(element.TagName, "STYLE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(element.TagName, "LINK", StringComparison.OrdinalIgnoreCase);

    public string? value
    {
        get
        {
            // A select answers the value of its selected option (its text when that option carries
            // no value attribute); a textarea answers its text content; every other element answers
            // its own value.
            if (string.Equals(_element.TagName, "SELECT", StringComparison.OrdinalIgnoreCase))
                return SelectedOptionValue();
            if (string.Equals(_element.TagName, "TEXTAREA", StringComparison.OrdinalIgnoreCase))
                return _element.TextContent ?? "";
            return _element.Value;
        }
        set
        {
            if (string.Equals(_element.TagName, "TEXTAREA", StringComparison.OrdinalIgnoreCase))
                _element.TextContent = value ?? "";
            else _element.Value = value;
        }
    }

    private string? SelectedOptionValue()
    {
        var chosen = SelectedOptionElements().FirstOrDefault();
        if (chosen == null) return "";
        var v = chosen.GetAttribute("value");
        return v ?? chosen.TextContent?.Trim();
    }

    public int selectionStart
    {
        get => _element.SelectionStart;
        set => _element.SelectionStart = value;
    }

    public int selectionEnd
    {
        get => _element.SelectionEnd;
        set => _element.SelectionEnd = value;
    }

    private ElementHost? WrapWithCache(Element? element)
    {
        if (element == null) return null;
        var engine = JavaScriptEngine.Current ?? Engine;
        if (engine?.IntegrationService != null)
            return engine.IntegrationService.WrapDomNode(element) as ElementHost;
        var host = new ElementHost(element);
        if (host.Engine == null && Engine != null)
            host.Engine = Engine;
        return host;
    }

    public ElementHost? parentElement
    {
        get => WrapWithCache(_element.ParentElement);
    }

    /// <summary>DOM §4.1: a node's parent is whatever holds it — for the document element that is the
    /// Document itself and for a child of a fragment the fragment, and neither of those is an element,
    /// so the read cannot be borrowed from 'parentElement' the way it was.</summary>
    public object? parentNode => JsIntegrationService.WrapAny(_element.ParentNode);

    public object? previousElementSibling
    {
        get
        {
            var parent = _element.ParentElement;
            if (parent == null) return null;
            int idx = parent.Children.IndexOf(_element);
            for (int i = idx - 1; i >= 0; i--)
            {
                if (parent.Children[i] is Element e)
                    return WrapWithCache(e);
            }
            return null;
        }
    }

    public object? nextElementSibling
    {
        get
        {
            var parent = _element.ParentElement;
            if (parent == null) return null;
            int idx = parent.Children.IndexOf(_element);
            for (int i = idx + 1; i < parent.Children.Count; i++)
            {
                if (parent.Children[i] is Element e)
                    return WrapWithCache(e);
            }
            return null;
        }
    }

    public object? firstChild
    {
        get
        {
            var first = _element.Children.FirstOrDefault();
            return first != null ? WrapNode(first) : null;
        }
    }

    public object? lastChild
    {
        get
        {
            var last = _element.Children.LastOrDefault();
            return last != null ? WrapNode(last) : null;
        }
    }

    public object? previousSibling
    {
        get
        {
            var prev = _element.PreviousSibling;
            return prev != null ? WrapNode(prev) : null;
        }
    }

    public object? nextSibling
    {
        get
        {
            var next = _element.NextSibling;
            return next != null ? WrapNode(next) : null;
        }
    }

    // A live collection is one object per element for as long as the element lives (DOM §2.8);
    // the list re-reads the tree on every access, so caching the object costs no freshness.
    public JsNodeList children => _children ??= new JsNodeList(_element, Engine);

    public int childElementCount => _element.Children.OfType<Element>().Count();

    public JsNodeList childNodes => _childNodes ??= new JsNodeList(_element, Engine, childNodes: true);

    public NamedNodeMapHost attributes => _attributes ??= new NamedNodeMapHost(_element);

    public string? getAttribute(string name) => _element.GetAttribute(name);

    public void setAttribute(string name, string value)
    {
        DomNames.RequireSettableAttributeName(name, "setAttribute", "Element");
        _element.SetAttribute(name, value);
    }

    public void removeAttribute(string name) => _element.RemoveAttribute(name);

    public bool hasAttribute(string name) => _element.HasAttribute(name);

    public bool hasChildNodes() => _element.Children.Count > 0;

    public object? closest(string selector)
    {
        CssSelectorMatcher.ParseForQuery(selector, "closest", "Element");
        for (var el = _element; el != null; el = el.ParentElement)
            if (CssSelectorMatcher.Matches(selector, el, _element))
                return WrapWithCache(el);
        return null;
    }

    public object? querySelector(string selector)
    {
        CssSelectorMatcher.ParseForQuery(selector, "querySelector", "Element");
        var result = CssSelectorMatcher.FirstDescendant(_element, selector, _element);
        return result != null ? WrapWithCache(result) : null;
    }

    public JsNodeList querySelectorAll(string selector)
    {
        CssSelectorMatcher.ParseForQuery(selector, "querySelectorAll", "Element");
        return new JsNodeList(_element, Engine, root =>
        {
            var list = new List<Element>();
            CssSelectorMatcher.CollectDescendants(root, selector, list, root);
            return list;
        });
    }

    public JsHtmlCollection getElementsByTagName(string tagName) =>
        new JsHtmlCollection(_element, Engine, tagName);

    public JsHtmlCollection getElementsByClassName(string className) =>
        new JsHtmlCollection(_element, Engine, null, className);

    /// <summary>The DOM node a script-side wrapper stands for. A node reaches a mutation method
    /// as the host object the engine handed the page — an element, a text or comment node, or a
    /// document fragment — and each keeps its own CLR identity, so unwrapping here is what lets
    /// 'appendChild' accept a fragment or a text node rather than only an element.</summary>
    internal static Node? UnwrapNode(object? child) => child switch
    {
        ElementHost el => el.NativeElement,
        TextNodeWrapper t => t.NativeNode,
        CommentHost c => c.NativeNode,
        // A processing instruction and a doctype are nodes the page can make and put in the tree,
        // and a mutation method that met one had no case for it: the unwrap answered null and
        // 'appendChild' refused its own argument as not a node of this document.
        ProcessingInstructionHost pi => pi.NativeNode,
        DocumentTypeHost dt => dt.NativeNode,
        DocumentFragmentHost f => f.NativeFragment,
        // A document is a Node, and a page that hands one to a boundary or a traverser means the
        // document itself: 'range.selectNodeContents(document)', 'walker.root = document',
        // 'range.containsNode(document)' all answer it in the reference, and 'insertNode(document)'
        // is refused by its type, not by an argument that never arrived.
        DocumentHost d => d.NativeDocument,
        Node n => n,
        _ => null,
    };

    // Every mutation method answers the node the specification names: appendChild and
    // insertBefore give back the child that was inserted (the fragment, when a fragment was
    // handed in), removeChild and replaceChild give back the node that came away. The page's own
    // wrapper object is returned so identity holds ('x === node').
    public object? appendChild(object? child)
    {
        var node = UnwrapNode(child);
        if (node == null)
            throw new DOMException("The node to be appended is not a node of this document.", "NotFoundError");
        if (node is DocumentFragment frag)
        {
            foreach (var moved in frag.Children.ToList())
                _element.AppendChild(moved);
            return child;
        }
        _element.AppendChild(node);
        return child;
    }

    /// <summary>DOM §5.5 (ParentNode): the children are replaced in one go — everything the element
    /// held leaves first, then each argument is inserted in the order it was written, with strings
    /// becoming text nodes and a fragment contributing its own children.</summary>
    public void replaceChildren(params object?[] nodes)
    {
        for (var i = _element.Children.Count - 1; i >= 0; i--)
            _element.RemoveChild(_element.Children[i]);
        foreach (var n in nodes) InsertChild(n, atEnd: true);
    }

    /// <summary>Element.append(nodes…) inserts each argument at the end, accepting strings as text
    /// nodes and a fragment as its children (ParentNode mixin).</summary>
    public void append(params object?[] nodes)
    {
        foreach (var n in nodes) InsertChild(n, atEnd: true);
    }

    public void prepend(params object?[] nodes)
    {
        foreach (var n in nodes) InsertChild(n, atEnd: false);
    }

    private void InsertChild(object? child, bool atEnd)
    {
        Node? node = child is string s ? new TextNode(s) : UnwrapNode(child);
        if (node == null) return;
        if (node is DocumentFragment frag)
        {
            foreach (var moved in frag.Children.ToList())
            {
                if (atEnd) _element.AppendChild(moved);
                else _element.InsertBefore(moved, _element.Children.FirstOrDefault());
            }
            return;
        }
        if (atEnd) _element.AppendChild(node);
        else _element.InsertBefore(node, _element.Children.FirstOrDefault());
    }

    public object? insertBefore(object? newChild, object? refChild)
    {
        var node = UnwrapNode(newChild);
        if (node == null)
            throw new DOMException("The node to be inserted is not a node of this document.", "NotFoundError");
        Node? refNode = refChild == null ? null : UnwrapNode(refChild);
        if (node is DocumentFragment frag)
        {
            foreach (var moved in frag.Children.ToList())
                _element.InsertBefore(moved, refNode);
            return newChild;
        }
        _element.InsertBefore(node, refNode);
        return newChild;
    }

    public object? removeChild(object? child)
    {
        var node = UnwrapNode(child);
        if (node == null)
            throw new DOMException("The node to be removed is not a child of this node.", "NotFoundError");
        _element.RemoveChild(node);
        return child;
    }

    public object? replaceChild(object? newChild, object? oldChild)
    {
        var newNode = UnwrapNode(newChild);
        var oldNode = UnwrapNode(oldChild);
        if (newNode == null || oldNode == null)
            throw new DOMException("The node to be replaced is not a child of this node.", "NotFoundError");
        if (newNode is DocumentFragment frag)
        {
            int idx = _element.Children.IndexOf(oldNode);
            if (idx >= 0)
            {
                var moved = frag.Children.ToList();
                _element.RemoveChild(oldNode);
                for (int i = 0; i < moved.Count; i++)
                    _element.InsertBefore(moved[i], idx + i < _element.Children.Count ? _element.Children[idx + i] : null);
            }
            return oldChild;
        }
        _element.ReplaceChild(newNode, oldNode);
        return oldChild;
    }

    public object cloneNode(bool deep)
    {
        // The element clones itself: a second cloner here had its own list of what travels, and it
        // left the namespace, the prefix, the namespace an attribute was set under and every comment
        // or processing instruction behind (DOM §4.6).
        var cloned = (Element)_element.CloneNode(deep);
        return WrapWithCache(cloned) ?? new ElementHost(cloned);
    }

    public bool contains(object? other)
    {
        if (other is ElementHost host)
            return ContainsElement(_element, host.NativeElement);
        return false;
    }

    public void addEventListener(string type, object callback)
    {
        addEventListener(type, callback, null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "JS engine assemblies are rooted - reflection on options object is safe")]
    public void addEventListener(string type, object callback, object? options)
    {
        var engine = JavaScriptEngine.Current ?? Engine;
        if (engine == null) return;

        bool useCapture = false;
        bool once = false;
        if (options is bool b)
            useCapture = b;
        else if (options != null)
        {
            // A JS options object ({capture:true, once:true}) reaches the host as an interop
            // value with no CLR members, so its flags are read back through the engine rather
            // than reflection (DOM 4 §2.7).
            useCapture = ReadEngineOption(engine, options, "capture");
            once = ReadEngineOption(engine, options, "once");
        }

        int cbId;
        var integration = engine.IntegrationService;
        if (integration != null)
        {
            // Try __g_store first (works for ClearScript, returns same ID for same JS function).
            // If it fails, use StoreJsFunction fallback (works for Jint).
            try
            {
                var result = integration.Facade.CallFunction("__g_store", callback);
                if (result is int id) cbId = id;
                else if (result is double d) cbId = (int)d;
                else if (result is long l) cbId = (int)l;
                else
                    cbId = integration.Facade.StoreJsFunction(callback);
            }
            catch
            {
                cbId = integration.Facade.StoreJsFunction(callback);
            }
        }
        else
        {
            cbId = engine.StoreCallbackRef(callback);
        }

        if (once)
            _onceCallbacks.Add(cbId);

        _listenerCapture[cbId] = useCapture;

        if (!_eventCallbackIds.TryGetValue(type, out var list))
        {
            list = new List<int>();
            _eventCallbackIds[type] = list;
        }
        if (!list.Contains(cbId))
            list.Add(cbId);
    }

    public void removeEventListener(string type, object callback)
    {
        removeEventListener(type, callback, null);
    }

    public void removeEventListener(string type, object callback, object? options)
    {
        var engine = JavaScriptEngine.Current ?? Engine;
        if (engine == null) return;

        // Remove all listeners of this type on this element.
        // Jint creates different CLR wrappers for the same JS function each time
        // it crosses the boundary, so matching individual callbacks is unreliable.
        // Tracked callbackIds from addEventListener guarantee correct cleanup.
        if (_eventCallbackIds.TryGetValue(type, out var list))
        {
            foreach (var cbId in list)
            {
                engine.RemoveCallback(cbId);
                _listenerCapture.Remove(cbId);
                _onceCallbacks.Remove(cbId);
            }
            list.Clear();
            _eventCallbackIds.Remove(type);
        }
    }

    private static int _optProbeCounter;

    /// <summary>Reads one boolean flag off a JS options object handed to a host method. The
    /// object carries no CLR members, so it is bound to a temporary global and the property is
    /// evaluated through the engine (DOM 4 §2.7 — the capture/once/passive dictionary).</summary>
    private static bool ReadEngineOption(JavaScriptEngine? engine, object? options, string name)
    {
        if (engine == null || options == null) return false;
        var adapter = engine.Adapter;
        if (adapter == null) return false;
        var tmp = "__opt_probe_" + Interlocked.Increment(ref _optProbeCounter);
        try
        {
            adapter.EmbedHostObject(tmp, options);
            var v = adapter.Evaluate($"{tmp}.{name}");
            try { adapter.Execute($"delete {tmp};"); } catch { }
            return v switch
            {
                bool b => b,
                double d => d != 0,
                null => false,
                _ => !(v is string s ? s.Length == 0 || s == "false" : false)
            };
        }
        catch
        {
            try { adapter.Execute($"delete {tmp};"); } catch { }
            return false;
        }
    }

    public void click()
    {
        DispatchEvent(new ScriptEvent("click", this));
    }

    public void focus()
    {
        DispatchEvent(new ScriptEvent("focus", this));
    }

    public void blur()
    {
        DispatchEvent(new ScriptEvent("blur", this));
    }

    public void scrollIntoView(bool alignToTop = true)
    {
        _element.ScrollIntoView(alignToTop);
    }

    /// <summary>ScrollIntoView with options object (behavior/block/inline).</summary>
    public void scrollIntoViewOptions(ScrollIntoViewOptions? options)
    {
        _element.ScrollIntoView(options);
    }

    // ── CSSOM View §6.3: the scroll members ───────────────────────────────────
    // Each one takes either the (x, y) pair or the single dictionary with 'left'/'top' (and the
    // 'behavior' the engine has no use for), and 'scrollBy' is the same operation read as a distance
    // from where the box is scrolled to now.
    public void scrollTo(object? x = null, object? y = null) => ScrollCore(x, y, relative: false);
    public void scrollBy(object? x = null, object? y = null) => ScrollCore(x, y, relative: true);
    public void scroll(object? x = null, object? y = null) => ScrollCore(x, y, relative: false);

    private void ScrollCore(object? x, object? y, bool relative)
    {
        var engine = JavaScriptEngine.Current ?? Engine;
        double left = 0, top = 0;
        // The dictionary form is told by the 'left'/'top' it carries; a number never has them.
        if (x != null && TryReadScrollOption(engine, x, "left", out var optionLeft))
        {
            left = optionLeft;
            top = TryReadScrollOption(engine, x, "top", out var optionTop) ? optionTop : 0;
        }
        else
        {
            left = ToDouble(x, 0);
            top = ToDouble(y, 0);
        }
        if (relative) { left += scrollLeft; top += scrollTop; }
        scrollLeft = left;
        scrollTop = top;
    }

    private static bool TryReadScrollOption(JavaScriptEngine? engine, object? options, string name, out double value)
    {
        value = 0;
        if (engine == null || options == null) return false;
        var adapter = engine.Adapter;
        if (adapter == null) return false;
        var tmp = "__scroll_probe_" + Interlocked.Increment(ref _optProbeCounter);
        try
        {
            adapter.EmbedHostObject(tmp, options);
            var v = adapter.Evaluate($"{tmp}.{name}");
            if (v is double d) { value = d; return true; }
            if (v is int i) { value = i; return true; }
            if (v is string s && double.TryParse(s, out var parsed)) { value = parsed; return true; }
            return false;
        }
        catch { return false; }
        finally
        {
            try { adapter.Execute($"delete {tmp};"); } catch { }
        }
    }

    private static double ToDouble(object? value, double fallback)
    {
        try
        {
            if (value == null) return fallback;
            var text = value.ToString() ?? "";
            return double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        }
        catch { return fallback; }
    }

    /// <summary>DOM §4.2.1: the slot an element was assigned to. This engine has no shadow trees to
    /// assign anything into, so the answer is the one a page sees outside a shadow host.</summary>
    public object? assignedSlot => null;

    public DomRect getBoundingClientRect()
    {
        FlushForRead();
        var rect = LayoutRect();
        var (scrollX, scrollY) = AncestorScrollOf(ScrollBasisOf(_element));
        return new DomRect(rect.left - scrollX, rect.top - scrollY, rect.width, rect.height);
    }

    /// <summary>Where the element sits in the document, before any scroller above it has moved.
    /// CSSOM View 4: an inline element has no box of its own, and its rectangle is the union of
    /// the fragments it owns in the line boxes around it — a read that answered 0x0 made every
    /// script that measures text see an empty page.</summary>
    private (float left, float top, float width, float height) LayoutRect()
    {
        var box = _element.LayoutBox;
        if (box != null)
            return (box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Width, box.BorderBox.Height);
        return Acrux.Core.Layout.InlineGeometry.TryGetBoundingRect(_element, out var union)
            ? (union.Left, union.Top, union.Width, union.Height)
            : (0, 0, 0, 0);
    }

    /// <summary>How far the scrollers above a box have moved it. The layout tree keeps the
    /// unscrolled positions and the paint pass shifts them on screen, so a client rectangle has to
    /// make the same shift (measured: a 400x300 box inside a scroller taken to (40, 25) reports
    /// its rectangle at (-40, -25) while its offsetTop stays 0).</summary>
    /// <summary>The box whose scroll chain moves an element: its own, or for an inline element the
    /// block whose line boxes hold its fragments.</summary>
    private static LayoutBox? ScrollBasisOf(Element element) =>
        element.LayoutBox ?? Acrux.Core.Layout.InlineGeometry.ContainingBlockOf(element);

    private static (float X, float Y) AncestorScrollOf(LayoutBox? box)
    {
        float x = 0, y = 0;
        for (var b = box?.Parent; b != null; b = b.Parent)
        {
            x += b.ScrollX;
            y += b.ScrollY;
        }
        return (x, y);
    }

    /// <summary>One rectangle per fragment: a box of its own answers one, an inline box answers
    /// one per line it is broken across (plus one for each atomic inline it contains), and a box
    /// that is not rendered answers none (CSSOM View 4).</summary>
    public DomRect[] getClientRects()
    {
        FlushForRead();
        var box = _element.LayoutBox;
        var (scrollX, scrollY) = AncestorScrollOf(ScrollBasisOf(_element));
        if (box != null)
            return new[] { new DomRect(box.BorderBox.Left - scrollX, box.BorderBox.Top - scrollY,
                box.BorderBox.Width, box.BorderBox.Height) };
        var fragments = Acrux.Core.Layout.InlineGeometry.GetFragments(_element);
        var rects = new DomRect[fragments.Count];
        for (int i = 0; i < fragments.Count; i++)
            rects[i] = new DomRect(fragments[i].Left - scrollX, fragments[i].Top - scrollY,
                fragments[i].Width, fragments[i].Height);
        return rects;
    }

    // Layout dimension properties
    public float offsetWidth 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box != null) return Round(box.BorderBox.Width);
            // An inline element's offset box is the union of its fragments (measured: a run
            // 24.02px wide reports 24).
            return Acrux.Core.Layout.InlineGeometry.TryGetBoundingRect(_element, out var union)
                ? Round(union.Width) : 0;
        }
    }

    public float offsetHeight 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box != null) return Round(box.BorderBox.Height);
            return Acrux.Core.Layout.InlineGeometry.TryGetBoundingRect(_element, out var union)
                ? Round(union.Height) : 0;
        }
    }

    /// <summary>The reference engine reports these as integers, so a sub-pixel advance must not
    /// leak out of them. It rounds half upward rather than to even: a 102.5px border box reports
    /// 103, and a negative fraction moves toward positive infinity (measured both).</summary>
    private static float Round(float value) => (float)Math.Floor(value + 0.5f);

    public float clientWidth 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            // CSSOM View 1.5: the padding box, whatever of it the scrollbars take. The content box
            // alone is too small by the element's own padding (measured: a 118px box with a 2px
            // border and 7px padding reports 114, not 100).
            return Round(ClientSizeOf(box, widthAxis: true));
        }
    }

    public float clientHeight 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return Round(ClientSizeOf(box, widthAxis: false));
        }
    }

    public float clientLeft
    {
        get
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return Round(box.PaddingBox.Left - box.BorderBox.Left);
        }
    }

    public float clientTop
    {
        get
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return Round(box.PaddingBox.Top - box.BorderBox.Top);
        }
    }

    /// <summary>The padding a laid-out box was given, which is what turns its content box back
    /// into the padding box <c>clientWidth</c> reports.</summary>
    private static (float Left, float Top, float Right, float Bottom) PaddingOfStatic(
        LayoutBox box, Element? fallbackOwner)
    {
        // Resolved the way the box itself was laid out — against the element's own font — so the
        // number a page reads back is the padding the box actually took.
        var style = box.Dimensions?.Style ?? fallbackOwner?.ComputedStyle;
        if (style == null) return (0, 0, 0, 0);
        float font = style.FontSize > 0 ? style.FontSize : 16;
        float Edge(Acrux.Core.Dom.Length? length)
        {
            if (length == null) return 0;
            float px = length.ToPixels(font, font, 0, 0);
            return float.IsNaN(px) || px < 0 ? 0 : px;
        }
        return (Edge(style.PaddingLeft), Edge(style.PaddingTop),
            Edge(style.PaddingRight), Edge(style.PaddingBottom));
    }

    public ElementHost offsetParent
    {
        get
        {
            // The walk tests each ancestor's computed position, so it has to see a styled tree:
            // reading offsetParent straight after an innerHTML assignment would otherwise stop at
            // a static parent whose style has not been resolved yet.
            FlushForRead();
            var op = _element.OffsetParent;
            if (op != null)
                return WrapWithCache(op);
            var doc = _element.OwnerDocument;
            if (doc?.Body != null)
                return WrapWithCache(doc.Body) ?? new ElementHost(doc.Body);
            if (doc?.DocumentElement != null)
                return WrapWithCache(doc.DocumentElement) ?? new ElementHost(doc.DocumentElement);
            return new ElementHost(_element);
        }
    }

    public float scrollWidth 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            // CSSOM View 1.6: the scrollable width, which is the content that overflows or the
            // client box, whichever is larger — never the content box alone.
            return Round(Math.Max(ClientSizeOf(box, widthAxis: true), box.ScrollContentWidth));
        }
    }

    public float scrollHeight 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return Round(Math.Max(ClientSizeOf(box, widthAxis: false), box.ScrollContentHeight));
        }
    }

    private static float ClientSizeOf(LayoutBox box, bool widthAxis)
    {
        var pad = PaddingOfStatic(box, null);
        return widthAxis
            ? box.ContentBox.Width + pad.Left + pad.Right
            : box.ContentBox.Height + pad.Top + pad.Bottom;
    }

    public double scrollLeft
    {
        get { FlushForRead(); return _element.LayoutBox?.ScrollX ?? 0; }
        set
        {
            // CSSOM View §6.3.1: an element is only scrollable as far as its own overflow, so the
            // number the page asks for is clamped into [0, scrollWidth - clientWidth] and reads back
            // that way — a box with nothing to scroll stays at 0 (measured both).
            var box = _element.LayoutBox;
            if (box == null) return;
            var limit = Math.Max(0f, scrollWidth - clientWidth);
            box.ScrollX = (float)Math.Max(0, Math.Min(value, (double)limit));
        }
    }

    public double scrollTop
    {
        get { FlushForRead(); return _element.LayoutBox?.ScrollY ?? 0; }
        set
        {
            var box = _element.LayoutBox;
            if (box == null) return;
            var limit = Math.Max(0f, scrollHeight - clientHeight);
            box.ScrollY = (float)Math.Max(0, Math.Min(value, (double)limit));
        }
    }

    public float offsetTop 
    {
        get 
        {
            FlushForRead();
            return OffsetAxis(fromTop: true);
        }
    }

    public float offsetLeft 
    {
        get 
        {
            FlushForRead();
            return OffsetAxis(fromTop: false);
        }
    }

    /// <summary>CSSOM View 2.4: the offset is measured from the <b>padding edge</b> of the offset
    /// parent, not from the page origin — a box inside a positioned ancestor reports its place in
    /// that ancestor. The body and a missing offset parent stand for the initial containing block,
    /// which is not inset by the body's own border and padding (measured both: a span 20px into a
    /// body with a 5px border reports 20, while the same span inside a positioned div reports the
    /// distance from that div's padding edge).</summary>
    private float OffsetAxis(bool fromTop)
    {
        // The layout position, not the client one: scrolling an ancestor moves what is seen and
        // leaves the offset where it was (measured both).
        var rect = LayoutRect();
        var value = fromTop ? rect.top : rect.left;
        var parent = _element.OffsetParent;
        if (parent == null || parent.TagName is "BODY" or "HTML") return Round(value);
        var box = parent.LayoutBox;
        if (box == null) return Round(value);
        return Round(value - (fromTop ? box.PaddingBox.Top : box.PaddingBox.Left));
    }

    public object[] classListValues => _element.ClassList.Select(c => (object)c).ToArray();

    public DomTokenListHost classList => _classList ??= new(this);

    public bool classList_contains(string className) => _element.HasClass(className);

    public void classList_add(string className)
    {
        var current = _element.ClassName ?? "";
        var parts = current.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!parts.Contains(className))
        {
            parts.Add(className);
            _element.ClassName = string.Join(" ", parts);
        }
    }

    public void classList_remove(string className)
    {
        var current = _element.ClassName ?? "";
        var parts = current.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        parts.Remove(className);
        _element.ClassName = string.Join(" ", parts);
    }

    public bool classList_toggle(string className)
    {
        if (_element.HasClass(className))
        {
            classList_remove(className);
            return false;
        }
        classList_add(className);
        return true;
    }

    public string? classList_item(int index)
    {
        var classes = _element.ClassList.ToList();
        return index >= 0 && index < classes.Count ? classes[index] : null;
    }

    public string? getAttributeNS(string? ns, string name) => _element.GetAttributeNS(ns, name);

    public void setAttributeNS(string? ns, string name, string value)
    {
        DomNames.RequireQualifiedName(name, "setAttributeNS", "Element", element: false);
        _element.SetAttributeNS(ns, name, value);
    }

    public void removeAttributeNS(string? ns, string name) => _element.RemoveAttributeNS(ns, name);

    public object? dispatchEvent(object evt)
    {
        ScriptEvent? scriptEvt = evt as ScriptEvent;
        if (scriptEvt == null)
        {
            if (evt is Dom.Event domEvt)
            {
                scriptEvt = new ScriptEvent(domEvt.Type, this)
                {
                    bubbles = domEvt.Bubbles,
                    cancelable = domEvt.Cancelable,
                    detail = (domEvt is Dom.CustomEvent ce) ? ce.Detail : null,
                    composed = domEvt.Composed,
                };
            }
            else
            {
                // Handle JS event objects (e.g. from V8/ClearScript) that didn't unwrap
                try
                {
                    var engine = JavaScriptEngine.Current ?? Engine;
                    if (engine?.Adapter != null)
                    {
                        var adapter = engine.Adapter;
                        var tmp = $"__tmp_dispatch_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                        try
                        {
                            adapter.EmbedHostObject(tmp, evt);
                            var type = adapter.Evaluate($"{tmp}.type") as string;
                            if (!string.IsNullOrEmpty(type))
                            {
                                scriptEvt = new ScriptEvent(type, this);
                                var bubbles = adapter.Evaluate($"{tmp}.bubbles");
                                if (bubbles is bool b) scriptEvt.bubbles = b;
                                var cancelable = adapter.Evaluate($"{tmp}.cancelable");
                                if (cancelable is bool c) scriptEvt.cancelable = c;
                                var detail = adapter.Evaluate($"{tmp}.detail");
                                if (detail != null) scriptEvt.detail = detail;
                            }
                            adapter.Execute($"delete {tmp};");
                        }
                        catch
                        {
                            try { adapter.Execute($"delete {tmp};"); } catch { }
                        }
                    }
                }
                catch { }
            }
        }
        if (scriptEvt == null) return false;
        DispatchEvent(scriptEvt);
        return !scriptEvt.defaultPrevented;
    }

    /// <summary>DOM §2.5: a dispatch walks the path — the capture leg from the outermost ancestor in,
    /// the listeners on the target itself, then the bubble back out — and the document closes the chain.
    /// The node whose listeners are running is the event's 'currentTarget' while they run, and the leg
    /// the walk is on is what 'eventPhase' reports (§2.3.5). A listener registered for the capture phase
    /// is picked up only on the way in and one registered without it only on the way out, which is what
    /// makes a page write down 'body-cap, d-cap, s-at-target, d-bubble, body-bubble' (measured).</summary>
    internal bool DispatchEvent(ScriptEvent evt)
    {
        if (string.IsNullOrEmpty(evt.type)) return true;

        var engine = JavaScriptEngine.Current ?? Engine;

        var path = new List<ElementHost> { this };
        for (var p = _element.ParentElement; p != null; p = p.ParentElement)
            path.Add(WrapWithCache(p) ?? new ElementHost(p));

        evt.target = this;
        for (var i = path.Count - 1; i >= 1; i--)
        {
            if (evt.PropagationStopped || evt.ImmediateStopped) break;
            path[i].InvokeListeners(evt, 1, engine);
        }
        if (!evt.PropagationStopped && !evt.ImmediateStopped) InvokeListeners(evt, 2, engine);

        if (evt.bubbles && !evt.PropagationStopped && !evt.ImmediateStopped)
        {
            for (var i = 1; i < path.Count; i++)
            {
                if (evt.PropagationStopped || evt.ImmediateStopped) break;
                path[i].InvokeListeners(evt, 3, engine);
            }
            // The document is the last node on the path, and its listeners live in the bridge's bag
            // rather than in a list kept per element (DOM §2.5).
            if (!evt.PropagationStopped && !evt.ImmediateStopped && engine?.DocumentHost != null
                && path[path.Count - 1]._element.Parent is Core.Dom.Document)
            {
                try
                {
                    evt.eventPhase = 3;
                    evt.currentTarget = engine.DocumentHost;
                    engine.DocumentHost.dispatchEvent(evt);
                }
                catch { }
            }
        }

        // Measured: once the dispatch has returned, the event reports no phase, no target and no current
        // target any more — the walk it described is over.
        evt.eventPhase = 0;
        evt.target = null;
        evt.currentTarget = null;

        return !evt.defaultPrevented;
    }

    /// <summary>Runs the listeners this node holds for the event's type in one leg of the walk (DOM
    /// §2.9): leg 1 is the capture path, leg 2 the target — where both kinds of listener run, in the
    /// order they were registered — and leg 3 the bubble back out.</summary>
    private void InvokeListeners(ScriptEvent evt, int phase, JavaScriptEngine? engine)
    {
        if (engine == null) return;
        if (_eventCallbackIds.TryGetValue(evt.type, out var cbIds))
        {
            foreach (var cbId in cbIds.ToList())
            {
                if (evt.ImmediateStopped) break;
                var capture = _listenerCapture.TryGetValue(cbId, out var flag) && flag;
                if ((phase == 1 && !capture) || (phase == 3 && capture)) continue;
                try
                {
                    evt.currentTarget = this;
                    evt.eventPhase = phase;
                    var svc = engine.IntegrationService;
                    if (svc != null)
                        svc.Facade.InvokeJsFunction(cbId, evt);
                    else
                        engine.InvokeCallbackWith(cbId, evt);
                }
                catch { }
                // A 'once' listener removes itself after its first call, leaving the other
                // listeners on the same event untouched (DOM 4 §2.8).
                if (_onceCallbacks.Remove(cbId) && cbIds.Contains(cbId))
                {
                    cbIds.Remove(cbId);
                    engine.RemoveCallback(cbId);
                    _listenerCapture.Remove(cbId);
                }
            }
        }
        RunInlineHandler(evt, engine, phase);
    }

    /// <summary>The 'on-type' attribute the markup carries is a listener of the element it sits on, so
    /// it runs in the same legs a script's listener does and never on the way in (§2.7.3).</summary>
    private void RunInlineHandler(ScriptEvent evt, JavaScriptEngine engine, int phase)
    {
        if (phase == 1) return;
        var attr = _element.GetAttribute("on" + evt.type);
        if (string.IsNullOrEmpty(attr)) return;
        try { engine.Evaluate(attr); }
        catch { }
    }

    internal IEnumerable<ElementHost> GetChildHosts() =>
        _element.Children.OfType<Element>().Select(e => WrapWithCache(e) ?? new ElementHost(e));

    private string GetTextContent()
    {
        var sb = new System.Text.StringBuilder();
        CollectText(_element, sb);
        return sb.ToString();
    }

    private static void CollectText(Node node, System.Text.StringBuilder sb)
    {
        if (node is TextNode tn)
            sb.Append(tn.TextContent);
        foreach (var child in node.Children)
            CollectText(child, sb);
    }

    private string BuildInnerHtml()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in _element.Children)
            SerializeNode(child, sb);
        return sb.ToString();
    }

    private string BuildOuterHtml()
    {
        var sb = new System.Text.StringBuilder();
        SerializeNode(_element, sb);
        return sb.ToString();
    }

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area","base","br","col","embed","hr","img","input","link","meta","param","source","track","wbr"
    };

    private static void SerializeNode(Node node, System.Text.StringBuilder sb)
    {
        if (node is TextNode tn)
        {
            sb.Append(System.Net.WebUtility.HtmlEncode(tn.TextContent ?? ""));
        }
        else if (node is CommentNode cn)
        {
            sb.Append("<!--").Append(cn.Data).Append("-->");
        }
        else if (node is Element el)
        {
            sb.Append('<').Append(el.TagName.ToLowerInvariant());
            foreach (var attr in el.Attributes)
            {
                // HTML §13.4: every attribute is written as name="value" — an empty value still gets
                // its quotes, so 'hidden' serialises as hidden="" and not as the bare name (measured).
                sb.Append(' ').Append(attr.Key)
                    .Append("=\"").Append(System.Net.WebUtility.HtmlEncode(attr.Value ?? "")).Append('"');
            }
            // A void element has no content and closes at its start tag, so it must not grow a
            // spurious end tag the way 'br'/'img' did before.
            if (VoidElements.Contains(el.TagName))
            {
                sb.Append('>');
                return;
            }
            sb.Append('>');
            foreach (var child in el.Children)
                SerializeNode(child, sb);
            sb.Append("</").Append(el.TagName.ToLowerInvariant()).Append('>');
        }
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

    private static bool ContainsElement(Element parent, Element child)
    {
        if (parent == child) return true;
        foreach (var c in parent.Children.OfType<Element>())
        {
            if (ContainsElement(c, child))
                return true;
        }
        return false;
    }

    private object WrapNode(Node node) => JsIntegrationService.WrapAny(node)!;

    // 'getClientRects()' is the real list above; the name is kept off the stub that used to
    // answer 1 for every element, which made '.length' look right and '[0]' undefined.

    public bool hasAttributes() => _element.Attributes.Count > 0;

    // DOM 4 §5.3.1: the names of every attribute currently set, as a list.
    public string[] getAttributeNames()
    {
        var names = new string[_element.Attributes.Count];
        int i = 0;
        foreach (var a in _element.Attributes) names[i++] = a.Key;
        return names;
    }

    public ElementHost? firstElementChild
    {
        get
        {
            foreach (var child in _element.Children)
            {
                if (child is Element el) return WrapWithCache(el);
            }
            return null;
        }
    }

    public ElementHost? lastElementChild
    {
        get
        {
            Element? last = null;
            foreach (var child in _element.Children)
            {
                if (child is Element el) last = el;
            }
            return last != null ? WrapWithCache(last) : null;
        }
    }

    // An element made without a namespace has none to answer — only an element the parser or
    // createElement gave the HTML namespace carries one (DOM §4.2.1, measured:
    // 'createElementNS(null, "foo-bar").namespaceURI' is null, not the HTML one).
    public string? namespaceURI => _element.NamespaceUri;

    public string? prefix => _element.Prefix;

    // The two halves of the qualified name, as the element was made: 'svg:circle' is prefix 'svg' and
    // local name 'circle', while a name written with a colon but no namespace — 'x:y' from
    // 'setAttribute' — is one name, not two (§4.2.1).
    public string localName => _element.LocalName;

    // HTML §2.7.3: a node's base URL is the document's base URL — the 'href' of the first 'base'
    // element in the document, or the document URL itself when the page declared none.
    public string baseURI => NodeBaseUri.For(_element);

    public bool isConnected => _element.IsConnected;

    // Node.ownerDocument is the document the node lives in — the same object the page holds as
    // 'document', not a second wrapper for it. A node the parser never stamped with an owner
    // still belongs to the document the script is running in.
    public object? ownerDocument
    {
        get => JsIntegrationService.WrapAny(_element.OwnerDocument)
               ?? JavaScriptEngine.Current?.IntegrationService?.Document;
    }

    public bool matches(string selector)
    {
        // ':scope' inside an element's own query is that element, so 'el.matches(":scope")' is true
        // while 'el.querySelectorAll(":scope")' finds nothing (measured).
        CssSelectorMatcher.ParseForQuery(selector, "matches", "Element");
        return CssSelectorMatcher.Matches(selector, _element, _element);
    }

    public void insertAdjacentHTML(string position, string text)
    {
        var parsed = HtmlParser.ParseFragment(text, _element.TagName);
        switch (position.ToLowerInvariant())
        {
            case "beforebegin":
                var parent = _element.ParentElement;
                parent?.InsertBefore(parsed[0], _element);
                break;
            case "afterbegin":
                for (int i = parsed.Count - 1; i >= 0; i--)
                    _element.InsertBefore(parsed[i], _element.Children.FirstOrDefault());
                break;
            case "beforeend":
                foreach (var node in parsed)
                    _element.AppendChild(node);
                break;
            case "afterend":
                var parent2 = _element.ParentElement;
                var next = _element.NextSibling;
                foreach (var node in parsed)
                    parent2?.InsertBefore(node, next);
                break;
        }
    }

    public void insertAdjacentText(string position, string text)
    {
        InsertAt(position, new TextNode(text ?? ""), asText: true);
    }

    /// <summary>Element.insertAdjacentElement(position, element) (DOM 4 §5.5): puts the given node
    /// at one of the four positions and answers the node, or null when the node could not move.</summary>
    public object? insertAdjacentElement(string position, object? element)
    {
        var node = UnwrapNode(element);
        if (node == null) return null;
        InsertAt(position, node, asText: false);
        return element;
    }

    private void InsertAt(string position, Node node, bool asText)
    {
        switch ((position ?? "").ToLowerInvariant())
        {
            case "beforebegin":
                _element.Parent?.InsertBefore(node, _element);
                break;
            case "afterbegin":
                _element.InsertBefore(node, _element.Children.FirstOrDefault());
                break;
            case "beforeend":
                _element.AppendChild(node);
                break;
            case "afterend":
                _element.Parent?.InsertBefore(node, _element.NextSibling);
                break;
        }
    }

    /// <summary>ChildNode.remove (§5.5) takes this node out of whatever holds it — a parent that is a
    /// document or a fragment counts, not only a parent element. On a select the same name takes an
    /// index instead, and the option at that place is what leaves the list (HTML §4.10.9).</summary>
    public void remove(object? index = null)
    {
        if (index != null && _element.TagName.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            var options = _element.Children.OfType<Element>().ToList();
            var at = ToInt(index, -1);
            if (at >= 0 && at < options.Count) _element.RemoveChild(options[at]);
            return;
        }
        (_element.ParentNode as Node)?.RemoveChild(_element);
    }

    private static int ToInt(object? value, int fallback)
    {
        try
        {
            if (value == null) return fallback;
            var text = value.ToString() ?? "";
            return int.TryParse(text, out var parsed) ? parsed : fallback;
        }
        catch { return fallback; }
    }

    // ParentNode/ChildNode mixin (DOM 4 §5.5). 'replaceWith' takes the place of this node,
    // 'before'/'after' insert beside it, and each accepts strings (as text nodes), a fragment
    // (as its children), or plain nodes — the old ElementHost-only forms silently dropped text.
    public void replaceWith(params object?[] nodes)
    {
        var parent = _element.Parent;
        if (parent == null) return;
        var refNode = _element.NextSibling;
        parent.RemoveChild(_element);
        foreach (var n in nodes) InsertBeside(parent, n, refNode);
    }

    public void before(params object?[] nodes)
    {
        var parent = _element.Parent;
        if (parent == null) return;
        foreach (var n in nodes) InsertBeside(parent, n, _element);
    }

    public void after(params object?[] nodes)
    {
        var parent = _element.Parent;
        if (parent == null) return;
        var refNode = _element.NextSibling;
        foreach (var n in nodes) InsertBeside(parent, n, refNode);
    }

    private static void InsertBeside(Node parent, object? child, Node? refNode)
    {
        Node? node = child is string s ? new TextNode(s) : UnwrapNode(child);
        if (node == null) return;
        if (node is DocumentFragment frag)
        {
            foreach (var moved in frag.Children.ToList())
                parent.InsertBefore(moved, refNode);
            return;
        }
        parent.InsertBefore(node, refNode);
    }

    public bool hasAttributeNS(string? ns, string name)
    {
        return _element.HasAttributeNS(ns, name);
    }

    // 'dataset' (DOMStringMap) is answered by a JS accessor installed on the element prototypes in
    // the setup script, which returns a live Proxy over the 'data-*' attributes. The host carries
    // no 'dataset' member on purpose: a CLR property is resolved ahead of that prototype accessor
    // and would take 'delete el.dataset.x' back down to a no-op.

    public string? dir
    {
        get => _element.GetAttribute("dir");
        set => _element.SetAttribute("dir", value);
    }

    public string? lang
    {
        get => _element.GetAttribute("lang");
        set => _element.SetAttribute("lang", value);
    }

    public string? title
    {
        get => _element.GetAttribute("title");
        set => _element.SetAttribute("title", value);
    }

    public bool hidden
    {
        get => _element.GetAttribute("hidden") != null;
        set
        {
            if (value) _element.SetAttribute("hidden", "");
            else _element.RemoveAttribute("hidden");
        }
    }

    public string? contentEditable
    {
        get => _element.GetAttribute("contenteditable") ?? "inherit";
        set
        {
            if (value == "true" || value == "") _element.SetAttribute("contenteditable", "true");
            else if (value == "false") _element.SetAttribute("contenteditable", "false");
            else if (value == "inherit") _element.RemoveAttribute("contenteditable");
        }
    }

    public bool isContentEditable => _element.GetAttribute("contenteditable") == "true";

    public bool draggable
    {
        get => _element.GetAttribute("draggable") == "true";
        set => _element.SetAttribute("draggable", value ? "true" : "false");
    }

    public string? accessKey
    {
        get => _element.GetAttribute("accesskey");
        set => _element.SetAttribute("accesskey", value);
    }

    public int tabIndex
    {
        get => int.TryParse(_element.GetAttribute("tabindex"), out var i) ? i : 0;
        set => _element.SetAttribute("tabindex", value.ToString());
    }

    public void animate() { }

    /// <summary>HTML §4.12.5: the 'slot' attribute reflected. A reflected string reads as the empty
    /// string when the attribute is not there (measured), not as nothing at all.</summary>
    public string slot
    {
        get => _element.GetAttribute("slot") ?? "";
        set => _element.SetAttribute("slot", value ?? "");
    }

    public bool spellcheck
    {
        get => _element.GetAttribute("spellcheck") == "true";
        set => _element.SetAttribute("spellcheck", value ? "true" : "false");
    }

    public string? translate
    {
        get => _element.GetAttribute("translate");
        set => _element.SetAttribute("translate", value);
    }

    public void attachShadow(object? mode) { }

    public object? shadowRoot => null;

    public int nodeType => 1;

    public string? nodeValue { get; set; }

    /// <summary>DOM §4.2.4: the attribute is put in with the empty value and taken off again, and the
    /// answer is whether it is there now. The second argument takes the toggling out of it: a present
    /// value puts the attribute in or leaves it there, an absent one takes it out or leaves it off. A
    /// name that could not be an attribute is refused before anything changes (measured: the sentence
    /// names the value it was given).</summary>
    public bool toggleAttribute(string name)
    {
        DomNames.RequireSettableAttributeName(name, "toggleAttribute", "Element");
        if (_element.HasAttribute(name))
        {
            _element.RemoveAttribute(name);
            return false;
        }
        _element.SetAttribute(name, "");
        return true;
    }

    public bool toggleAttribute(string name, object? force)
    {
        DomNames.RequireSettableAttributeName(name, "toggleAttribute", "Element");
        if (RangeHost.Truthy(force))
        {
            if (!_element.HasAttribute(name)) _element.SetAttribute(name, "");
            return true;
        }
        if (_element.HasAttribute(name)) _element.RemoveAttribute(name);
        return false;
    }

    public object? getAttributeNode(string name)
    {
        // The Attr an element answers is live and one-per-name: reading it twice gives the same
        // object, and writing through 'attr.value' writes the attribute.
        return _element.HasAttribute(name) ? AttributeHost.For(_element, name) : null;
    }

    public object? getAttributeNodeNS(string? ns, string name)
    {
        // The attribute for a namespace and a local name is the very object the map answers for it,
        // so 'getAttributeNodeNS(ns, "y") === attributes.getNamedItem("x:y")' holds (§4.3.6).
        var stored = _element.FindAttributeName(ns, name);
        return stored == null ? null : getAttributeNode(stored);
    }

    public object? setAttributeNode(object attr)
        => SetAttributeOn(_element, attr);

    public object? setAttributeNodeNS(object attr) => setAttributeNode(attr);

    /// <summary>DOM §4.3.4: the attribute becomes the element's own and the one it replaces is
    /// handed back. Both the element and its NamedNodeMap run through here, so the two ways of
    /// putting an Attr on cannot drift apart.</summary>
    internal static object? SetAttributeOn(Element element, object attr)
    {
        if (attr is not AttributeHost host)
            throw new DOMException("parameter 1 is not of type 'Attr'.", "TypeError");
        // An attribute that belongs to another element has to be taken off that element first;
        // the spec refuses the call instead of moving it behind the page's back.
        if (host.ownerElement != null && !ReferenceEquals(host.NativeOwner, element))
            throw new DOMException("The attribute is in use on another element.", "InvalidModificationError");
        var previous = element.HasAttribute(host.name) ? AttributeHost.For(element, host.name) : null;
        if (ReferenceEquals(previous, host)) return host;
        // The attribute that is taken off has to be detached before the new value goes on: its own
        // 'value' is read off the element, so writing first would hand the page an old attribute
        // holding the new value (§4.3.4).
        if (previous != null) AttributeHost.Release(previous);
        // The namespace the attribute carries goes on with it, so the element keeps answering
        // 'getAttributeNS' by the local name (§4.3.6).
        element.SetAttributeNS(host.namespaceURI, host.name, host.value ?? "");
        AttributeHost.Attach(element, host);
        return previous;
    }

    public object? removeAttributeNode(object attr)
    {
        if (attr is not AttributeHost host)
            throw new DOMException("parameter 1 is not of type 'Attr'.", "TypeError");
        if (!ReferenceEquals(host.NativeOwner, _element) || !host.IsAttached)
            throw new DOMException("The attribute is not an attribute of the element.", "NotFoundError");
        // The value is read while the attribute is still on the element: the Attr the page gets
        // back keeps the value it had, which is what the removal hands over (§4.3.6).
        AttributeHost.Release(host);
        _element.RemoveAttribute(host.name);
        return host;
    }

    public void normalize() => NormalizeNode(_element);

    /// <summary>DOM 4 §4.5.1: walk the subtree, drop empty text nodes, and glue each run of
    /// adjacent text nodes into the first one. Removals go through RemoveChild so a detached
    /// node's parent pointer is cleared — the old Clear()+AppendChild path left a stale parent on
    /// every kept child and threw on the re-append.</summary>
    private static void NormalizeNode(Node node)
    {
        foreach (var child in node.Children.ToList())
            if (child is Element) NormalizeNode(child);

        var kids = node.Children;
        int i = 0;
        while (i < kids.Count)
        {
            if (kids[i] is TextNode tn)
            {
                if (string.IsNullOrEmpty(tn.Data)) { node.RemoveChild(tn); continue; }
                int j = i + 1;
                while (j < kids.Count && kids[j] is TextNode next)
                {
                    tn.Data += next.Data;
                    node.RemoveChild(next);
                }
                i++;
            }
            else i++;
        }
    }

    public bool isDefaultNamespace(string ns) => false;

    public string? lookupNamespaceURI(string prefix) => null;

    public string? lookupPrefix(string ns) => null;

    public bool isEqualNode(object? other)
    {
        if (other is ElementHost host)
            return _element.TagName == host.NativeElement.TagName;
        return false;
    }

    public bool isSameNode(object? other)
    {
        if (other is ElementHost host)
            return _element == host.NativeElement;
        return false;
    }

    public int compareDocumentPosition(object? other) => NodePositionOf(_element, UnwrapNode(other));

    /// <summary>DOM 4 §4.4.1: the bitmask describing the other node's position relative to this one
    /// (disconnected / preceding / following / contains / contained-by). The document-order half is
    /// found by walking both nodes' ancestor paths from the root and comparing the first position
    /// where they differ — the pair that never shared a tree reads as disconnected. A document answers
    /// it the same way an element does, so this is the one implementation both hosts use.</summary>
    internal static int NodePositionOf(Node self, Node? other)
    {
        if (other == null) return 0;
        if (ReferenceEquals(other, self)) return 0;
        const int Disconnected = 1, Preceding = 2, Following = 4, Contains = 8, ContainedBy = 16;
        var mine = PathToRoot(self);
        var theirs = PathToRoot(other);
        int result = 0;
        if (mine.Count > 0 && theirs.Count > 0 && !ReferenceEquals(mine[0], theirs[0]))
            result |= Disconnected;
        // DOM §4.4.1: containment carries its half of document order with it — a node that holds the
        // other comes before it, so the pair reads 20 (CONTAINED_BY|FOLLOWING) from the ancestor and
        // 10 (CONTAINS|PRECEDING) from the descendant (measured).
        if (theirs.Contains(self)) result |= ContainedBy | Following;
        if (mine.Contains(other)) result |= Contains | Preceding;
        if ((result & (Disconnected | Contains | ContainedBy)) == 0)
        {
            int n = Math.Min(mine.Count, theirs.Count);
            for (int i = 0; i < n; i++)
            {
                if (ReferenceEquals(mine[i], theirs[i])) continue;
                var parent = mine[i].Parent;
                if (parent != null)
                {
                    int ia = parent.Children.IndexOf(mine[i]);
                    int ib = parent.Children.IndexOf(theirs[i]);
                    result |= (ia < ib) ? Following : Preceding;
                }
                break;
            }
        }
        return result;
    }

    private static List<Node> PathToRoot(Node node)
    {
        var list = new List<Node>();
        for (var n = node; n != null; n = n.Parent) list.Add(n);
        list.Reverse();
        return list;
    }

    public object? getRootNode() => GetRootNodeCore(null);
    public object? getRootNode(object? options) => GetRootNodeCore(options);

    private object? GetRootNodeCore(object? options)
    {
        // DOM 4 §4.3: the root of the tree the node lives in — the Document for a mounted node, the
        // top-most ancestor for a detached one. A shadow root would stop here, but the engine has no
        // attached shadow trees yet, so the document is the root.
        var engine = JavaScriptEngine.Current ?? Engine;
        Node current = _element;
        while (current.Parent != null) current = current.Parent;
        if (current is Document doc)
            return engine?.DocumentHost ?? (object)new DocumentHost(doc);
        if (current is Element el)
            return engine?.IntegrationService?.WrapDomNode(el) as ElementHost ?? new ElementHost(el);
        return current;
    }

    public void setPointerCapture(int pointerId) { }

    public void releasePointerCapture(int pointerId) { }

    public bool hasPointerCapture(int pointerId) => false;

    /// <summary>The 'type' the interface answers is a different reflection per family (HTML §4.10.5,
    /// §4.10.6, §4.10.9): an input reads its attribute and defaults to 'text', a button defaults to
    /// 'submit', a select derives its word from 'multiple' and never reads an attribute, and a
    /// textarea or an option answers the family's own name. The other families — link, script, style,
    /// source, track — answer the attribute the page wrote, which is the MIME hint they carry.</summary>
    public string? type
    {
        get
        {
            var tag = _element.TagName;
            var written = _element.GetAttribute("type");
            if (tag.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
                return multiple ? "select-multiple" : "select-one";
            if (tag.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase)) return "textarea";
            if (tag.Equals("OPTION", StringComparison.OrdinalIgnoreCase)) return "option";
            if (tag.Equals("OPTGROUP", StringComparison.OrdinalIgnoreCase)) return "optgroup";
            if (tag.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
                return written is "reset" or "button" ? written : "submit";
            if (tag.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(written) ? "text" : written;
            return written;
        }
        set => inputType = value;
    }
    public string? inputType
    {
        get => _element.GetAttribute("type") ?? "text";
        set => _element.SetAttribute("type", value);
    }

    public bool inputChecked
    {
        get => _element.HasAttribute("checked");
        set
        {
            if (value) _element.SetAttribute("checked", "");
            else _element.RemoveAttribute("checked");
        }
    }

    public bool @checked
    {
        get => inputChecked;
        set => inputChecked = value;
    }

    public string? placeholder
    {
        get => _element.GetAttribute("placeholder");
        set => _element.SetAttribute("placeholder", value);
    }

    public string? inputName
    {
        get => _element.GetAttribute("name");
        set => _element.SetAttribute("name", value);
    }

    /// <summary>'disabled' on the elements that own a stylesheet is sheet state the engine
    /// reads, not a reflected attribute (measured: setting it leaves hasAttribute('disabled')
    /// false), so it goes through the style engine instead of the attribute store. Every other
    /// element keeps the attribute-backed form-control behaviour.</summary>
    public bool disabled
    {
        get => IsSheetOwner ? _element.SheetDisabled : _element.HasAttribute("disabled");
        set
        {
            if (IsSheetOwner)
            {
                _element.SheetDisabledState = value;
                Acrux.Core.Performance.DirtyState.AddSelf(_element,
                    Acrux.Core.Performance.DirtyFlags.Style | Acrux.Core.Performance.DirtyFlags.Layout |
                    Acrux.Core.Performance.DirtyFlags.Paint);
                DomMutationTracker.Notify();
            }
            else if (value) _element.SetAttribute("disabled", "");
            else _element.RemoveAttribute("disabled");
        }
    }

    private bool IsSheetOwner =>
        string.Equals(_element.TagName, "STYLE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_element.TagName, "LINK", StringComparison.OrdinalIgnoreCase);

    public bool readOnly
    {
        get => _element.HasAttribute("readonly");
        set
        {
            if (value) _element.SetAttribute("readonly", "");
            else _element.RemoveAttribute("readonly");
        }
    }

    public bool required
    {
        get => _element.HasAttribute("required");
        set
        {
            if (value) _element.SetAttribute("required", "");
            else _element.RemoveAttribute("required");
        }
    }

    public ulong inputWidth
    {
        get => ulong.TryParse(_element.GetAttribute("width"), out var v) ? v : 0;
        set => _element.SetAttribute("width", value.ToString());
    }

    public ulong inputHeight
    {
        get => ulong.TryParse(_element.GetAttribute("height"), out var v) ? v : 0;
        set => _element.SetAttribute("height", value.ToString());
    }

    public void select()
    {
        var evt = new ScriptEvent("select", this);
        DispatchEvent(evt);
    }

    public void setRangeText(string replacement, int? start = null, int? end = null, string? selectionMode = null) { }

    public void setSelectionRange(int start, int end, string? direction = null)
    {
        _element.SelectionStart = start;
        _element.SelectionEnd = end;
    }

    // ===== HTMLTextAreaElement specific =====
    public int rows
    {
        get => int.TryParse(_element.GetAttribute("rows"), out var v) ? v : 2;
        set => _element.SetAttribute("rows", value.ToString());
    }

    public int cols
    {
        get => int.TryParse(_element.GetAttribute("cols"), out var v) ? v : 20;
        set => _element.SetAttribute("cols", value.ToString());
    }

    // ===== HTMLSelectElement specific =====
    public int selectedIndex
    {
        get
        {
            var chosen = SelectedOptionElements().FirstOrDefault();
            return chosen == null ? -1 : OptionElements().IndexOf(chosen);
        }
        set
        {
            var options = OptionElements();
            for (int i = 0; i < options.Count; i++)
            {
                if (i == value) options[i].SetAttribute("selected", "");
                else options[i].RemoveAttribute("selected");
            }
        }
    }

    public object[] options => OptionElements()
        .Select(e => (object)(WrapWithCache(e) ?? new ElementHost(e))).ToArray();

    public int selectLength => OptionElements().Count;

    /// <summary>HTML §4.10.9: the select's list of options is every option it contains, the ones
    /// inside an option group included, in tree order.</summary>
    private List<Element> OptionElements()
    {
        var list = new List<Element>();
        CollectOptions(_element, list);
        return list;
    }

    private static void CollectOptions(Node root, List<Element> into)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            var tag = child.TagName;
            if (tag.Equals("OPTION", StringComparison.OrdinalIgnoreCase)) into.Add(child);
            else if (tag.Equals("OPTGROUP", StringComparison.OrdinalIgnoreCase)) CollectOptions(child, into);
        }
    }

    /// <summary>Which options the select answers as chosen: the ones the page marked, and — for a
    /// single-choice select with nothing marked — its first option, which is what the reference
    /// reports before a page touches the list at all (measured: 'selectedIndex' is 0 and
    /// 'selectedOptions' holds one option for a fresh select with an option in it).</summary>
    private List<Element> SelectedOptionElements()
    {
        var options = OptionElements();
        var marked = options.Where(o => o.HasAttribute("selected")).ToList();
        if (marked.Count > 0) return multiple ? marked : new List<Element> { marked[0] };
        if (!multiple && options.Count > 0) return new List<Element> { options[0] };
        return marked;
    }

    // ===== HTMLFormElement specific =====
    public object[] formElements
    {
        get
        {
            var elements = new List<object>();
            CollectFormElements(_element, elements);
            return elements.ToArray();
        }
    }

    public int formLength => formElements.Length;

    /// <summary>HTML §4.10.22 and §4.10.10.4: a form and a select are both read as a list — the form's
    /// listed elements, the select's options — and that list is what the index walks. Any other element
    /// holds no list at all, so the index answers nothing. 'length' is the same list counted, and it is
    /// an accessor on each interface's own level (the host member would be read by every element).</summary>
    private object[] ListedElements()
    {
        var tag = _element.TagName;
        if (tag.Equals("FORM", StringComparison.OrdinalIgnoreCase)) return formElements;
        if (tag.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            return OptionElements().Select(o => JsIntegrationService.WrapAny(o)!).ToArray();
        return Array.Empty<object>();
    }

    public object? this[int index]
    {
        get
        {
            var listed = ListedElements();
            return index >= 0 && index < listed.Length ? listed[index] : null;
        }
    }

    /// <summary>HTMLFormElement.elements — the live list of the form's controls, with the reads a
    /// collection owns: 'length', the indexed read, 'item()' and 'namedItem()' (HTML §4.10.22).
    /// A non-form element answers an empty list.</summary>
    public object elements
    {
        get => string.Equals(_element.TagName, "FORM", StringComparison.OrdinalIgnoreCase)
            ? new JsFormControlsCollection(_element, JavaScriptEngine.Current ?? Engine)
            : Array.Empty<object>();
    }

    public string? formName
    {
        get => _element.GetAttribute("name");
        set => _element.SetAttribute("name", value);
    }

    public string? action
    {
        get => _element.GetAttribute("action");
        set => _element.SetAttribute("action", value);
    }

    public string? method
    {
        get => _element.GetAttribute("method") ?? "get";
        set => _element.SetAttribute("method", value);
    }

    public string? enctype
    {
        get => _element.GetAttribute("enctype") ?? "application/x-www-form-urlencoded";
        set => _element.SetAttribute("enctype", value);
    }

    public void submit()
    {
        var evt = new ScriptEvent("submit", this);
        DispatchEvent(evt);
    }

    public void formReset()
    {
        var evt = new ScriptEvent("reset", this);
        DispatchEvent(evt);
    }

    /// <summary>HTMLFormElement.requestSubmit (HTML §4.10.22.6): the form asks to be submitted the way
    /// a button would — the 'submit' event runs first and a cancelled one stops the submission, which
    /// is the difference from 'submit()' that the interface defines. A submitter that is not a control
    /// of this form is refused.</summary>
    public void requestSubmit(object? submitter = null)
    {
        if (submitter != null && submitter is not ElementHost)
            throw new DOMException("Failed to execute 'requestSubmit' on 'HTMLFormElement': parameter 1"
                                   + " is not of type 'HTMLElement'.", "TypeError");
        if (!_element.TagName.Equals("FORM", StringComparison.OrdinalIgnoreCase)) return;
        // The submitter is clicked first, and the form's own 'submit' runs after it — both through
        // the host's dispatch, so a listener the page wrote sees them and can cancel the second
        // (§4.10.22.6). The engine has no form navigation to run for a request that stands.
        if (submitter is ElementHost button)
        {
            var click = new ScriptEvent("click", button);
            button.DispatchEvent(click);
        }
        var evt = new ScriptEvent("submit", this) { cancelable = true };
        DispatchEvent(evt);
    }

    /// <summary>HTMLFormElement.reset (HTML §4.10.22.5): restore every contained control to the value
    /// its content attributes declare — a text input to its 'value', a checkbox/radio to 'checked', a
    /// select to its 'selected' option, a textarea to its text — then fire 'reset'. 'formReset' is the
    /// host's older spelling of the same reset; this is the name the interface carries.</summary>
    public void reset()
    {
        foreach (var el in DescendantsFormControls(_element))
            RestoreFormControl(el);
        var evt = new ScriptEvent("reset", this);
        DispatchEvent(evt);
    }

    private static IEnumerable<Element> DescendantsFormControls(Element root)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            var tag = child.TagName.ToLowerInvariant();
            if (tag is "input" or "select" or "textarea" or "button") yield return child;
            foreach (var d in DescendantsFormControls(child)) yield return d;
        }
    }

    private static void RestoreFormControl(Element el)
    {
        switch (el.TagName.ToLowerInvariant())
        {
            case "input":
                var type = (el.GetAttribute("type") ?? "text").ToLowerInvariant();
                if (type is "checkbox" or "radio")
                {
                    if (el.HasAttribute("checked")) el.SetAttribute("checked", "");
                    else el.RemoveAttribute("checked");
                }
                else el.Value = el.GetAttribute("value") ?? "";
                break;
            case "textarea":
                el.TextContent = el.GetAttribute("value") ?? el.TextContent ?? "";
                break;
            case "select":
                foreach (var opt in el.Children.OfType<Element>())
                {
                    if (opt.HasAttribute("selected")) opt.SetAttribute("selected", "");
                    else opt.RemoveAttribute("selected");
                }
                break;
        }
    }

    public bool checkValidity() => true;

    // ===== HTMLCanvasElement specific =====
    public ulong canvasWidth
    {
        get => ulong.TryParse(_element.GetAttribute("width"), out var v) ? v : 150;
        set => _element.SetAttribute("width", value.ToString());
    }

    public ulong canvasHeight
    {
        get => ulong.TryParse(_element.GetAttribute("height"), out var v) ? v : 150;
        set => _element.SetAttribute("height", value.ToString());
    }

    public object? getContext(string type)
    {
        if (_element is HTMLCanvasElement canvas)
            return canvas.GetContext(type);
        return null;
    }

    public string? toDataURL(string? type = "image/png")
    {
        return "";
    }

    // ===== HTMLMediaElement specific =====
    public bool paused => true;
    public bool ended => false;
    public double currentTime { get; set; }
    public double duration => 0;
    public double volume { get; set; } = 1.0;
    public bool muted { get; set; }
    public string? mediaSrc
    {
        get => _element.GetAttribute("src");
        set => _element.SetAttribute("src", value);
    }

    public void play() { }
    public void pause() { }
    public void load() { }
    public string? canPlayType(string type) => "";

    // Generic attribute-backed src (works for script, img, iframe, media, etc.)
    public string? src
    {
        get => _element.GetAttribute("src");
        set => _element.SetAttribute("src", value ?? "");
    }

    // ===== HTMLAnchorElement specific =====
    public string? href
    {
        get => HrefResolved()?.href;
        set => _element.SetAttribute("href", value);
    }

    // A hyperlink reflects its URL through the 'URL' interface (HTML): the content attribute is
    // the (possibly relative) author value, while 'href', 'pathname', 'search' and the rest read
    // the same value resolved to an absolute URL against the document base.
    private URLHost? HrefResolved()
    {
        var raw = _element.GetAttribute("href");
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            var baseUrl = Engine?.LocationHost?.href
                ?? JavaScriptEngine.Current?.LocationHost?.href
                ?? _element.OwnerDocument?.Url;
            return string.IsNullOrEmpty(baseUrl) ? new URLHost(raw) : new URLHost(raw, baseUrl);
        }
        catch { return null; }
    }

    public string pathname => HrefResolved()?.pathname ?? "";
    public string search => HrefResolved()?.search ?? "";
    public string hash => HrefResolved()?.hash ?? "";
    public string protocol => HrefResolved()?.protocol ?? "";
    public string host => HrefResolved()?.host ?? "";
    public string hostname => HrefResolved()?.hostname ?? "";
    public string port => HrefResolved()?.port ?? "";
    public string origin => HrefResolved()?.origin ?? "";

    /// <summary>The 'name' content attribute, reflected on the elements that carry a name
    /// (form controls, hyperlinks, frames). It is the same map the 'name' attribute uses.</summary>
    public string? name
    {
        get => _element.GetAttribute("name");
        set => _element.SetAttribute("name", value);
    }

    public string? rel
    {
        get => _element.GetAttribute("rel");
        set => _element.SetAttribute("rel", value);
    }

    public RelTokenListHost relList => _relList ??= new(this);

    // Reflected content attributes the standard names directly on the element interface.
    public string? download
    {
        get => _element.GetAttribute("download");
        set => _element.SetAttribute("download", value);
    }

    public string? alt
    {
        get => _element.GetAttribute("alt");
        set => _element.SetAttribute("alt", value);
    }

    public bool multiple
    {
        get => _element.HasAttribute("multiple");
        set { if (value) _element.SetAttribute("multiple", ""); else _element.RemoveAttribute("multiple"); }
    }

    /// <summary>HTML §4.12.3: the template's content fragment. The parser fills a template's children
    /// the way it fills any other element, so the first time the fragment is asked for, those children
    /// move into it — which is where the specification keeps them, and why 'template.children' is read
    /// as empty while 'template.content.firstChild' holds the markup the page wrote. The fragment is
    /// the element's own, so 'tpl.content === tpl.content' holds.</summary>
    public object? __templateContent()
    {
        if (!_element.TagName.Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase)) return null;
        if (_element is not Acrux.Core.Dom.Html.HTMLTemplateElement template) return null;
        var fragment = template.Content;
        if (fragment.OwnerDocument == null) fragment.OwnerDocument = _element.OwnerDocument;
        foreach (var child in _element.Children.ToList()) fragment.AppendChild(child);
        return JsIntegrationService.WrapAny(fragment);
    }

    /// <summary>HTMLSelectElement.selectedOptions — the options the select answers as chosen (§4.10.9).</summary>
    public object[] selectedOptions
        => SelectedOptionElements().Select(opt => WrapWithCache(opt) ?? new ElementHost(opt)).ToArray();

    public string? htmlFor
    {
        get => _element.GetAttribute("for");
        set => _element.SetAttribute("for", value);
    }

    public string? target
    {
        get => _element.GetAttribute("target");
        set => _element.SetAttribute("target", value);
    }

    // ===== HTMLImageElement specific =====
    public string? imgSrc
    {
        get => _element.GetAttribute("src");
        set => _element.SetAttribute("src", value);
    }

    public string? imgAlt
    {
        get => _element.GetAttribute("alt");
        set => _element.SetAttribute("alt", value);
    }

    // HTML width/height content attributes. On the elements the spec gives a *number* (img, iframe,
    // embed, object) it answers the parsed integer — and the same element's offsetWidth reads back
    // the very number the attribute carried, the way the layout treats it. On every other element
    // 'width'/'height' carry no meaning, so the attribute text is returned.
    public object width
    {
        get => NumericSizeElement ? (object)ParseIntAttr("width") : (_element.GetAttribute("width") ?? "");
        set => _element.SetAttribute("width", Convert.ToString(value) ?? "");
    }

    public object height
    {
        get => NumericSizeElement ? (object)ParseIntAttr("height") : (_element.GetAttribute("height") ?? "");
        set => _element.SetAttribute("height", Convert.ToString(value) ?? "");
    }

    private bool NumericSizeElement => TagIs("IMG", "IFRAME", "EMBED", "OBJECT");

    private bool TagIs(params string[] names)
    {
        foreach (var n in names) if (string.Equals(_element.TagName, n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private ulong ParseIntAttr(string name)
    {
        var raw = _element.GetAttribute(name) ?? "";
        var digits = new string(raw.TakeWhile(char.IsDigit).ToArray());
        return ulong.TryParse(digits, out var v) ? v : 0;
    }

    public ulong imgWidth
    {
        get => ulong.TryParse(_element.GetAttribute("width"), out var v) ? v : 0;
        set => _element.SetAttribute("width", value.ToString());
    }

    public ulong imgHeight
    {
        get => ulong.TryParse(_element.GetAttribute("height"), out var v) ? v : 0;
        set => _element.SetAttribute("height", value.ToString());
    }

    public bool complete => true;
    public ulong naturalWidth => 0;
    public ulong naturalHeight => 0;

    // ===== HTMLIFrameElement specific =====
    public string? iframeSrc
    {
        get => _element.GetAttribute("src");
        set => _element.SetAttribute("src", value);
    }

    public string? srcdoc
    {
        get => _element.GetAttribute("srcdoc");
        set => _element.SetAttribute("srcdoc", value);
    }

    public object? contentDocument => null;
    public object? contentWindow => null;

    private static string KebabToCamel(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var parts = s.Split('-');
        for (int i = 1; i < parts.Length; i++)
            if (parts[i].Length > 0)
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
        return string.Join("", parts);
    }

    private void CollectFormElements(Element root, List<object> elements)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            var tag = child.TagName.ToLowerInvariant();
            if (tag is "input" or "select" or "textarea" or "button" or "output" or "datalist" or "progress" or "meter")
                elements.Add(WrapWithCache(child) ?? new ElementHost(child));
            CollectFormElements(child, elements);
        }
    }
}

/// <summary>DOMTokenList over the 'rel' content attribute (DOM 4 §3.3.5). A set of space-separated
/// tokens kept in the attribute; it is its own class because the element's 'classList' reads and
/// writes the 'class' attribute specifically and cannot stand in for 'rel'.</summary>
public class RelTokenListHost : IDomPrototypeHost
{
    private readonly ElementHost _element;
    public RelTokenListHost(ElementHost element) => _element = element;
    private Element El => _element.NativeElement;
    private List<string> Tokens() =>
        (El.GetAttribute("rel") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    private void SetTokens(List<string> t) => El.SetAttribute("rel", string.Join(" ", t));

    // 'relList' is the same interface as 'classList' — one level, which the page can see by comparing
    // the prototypes (measured: 'Object.getPrototypeOf(a.relList) === DOMTokenList.prototype').
    string[] IDomPrototypeHost.__domTypeChain => new[] { "DOMTokenList" };

    public int length => Tokens().Count;
    public string? this[int index] => item(index);
    public string? item(int index) { var t = Tokens(); return index >= 0 && index < t.Count ? t[index] : null; }
    public bool contains(string token) => Tokens().Contains(token);
    public void add(params string[] tokens)
    {
        foreach (var tok in tokens) DomTokens.Require(tok, "add");
        var t = Tokens();
        foreach (var tok in tokens) if (!string.IsNullOrEmpty(tok) && !t.Contains(tok)) t.Add(tok);
        SetTokens(t);
    }
    public void remove(params string[] tokens)
    {
        foreach (var tok in tokens) DomTokens.Require(tok, "remove");
        var t = Tokens();
        foreach (var tok in tokens) t.Remove(tok);
        SetTokens(t);
    }
    public bool toggle(string token)
    {
        DomTokens.Require(token, "toggle");
        var t = Tokens();
        if (t.Contains(token)) { t.Remove(token); SetTokens(t); return false; }
        t.Add(token); SetTokens(t); return true;
    }
    /// <summary>DOM §3.3.6: with the second argument the member stops toggling — the token is put in
    /// when it is present and taken out when it is absent, and the answer is whether the list holds it
    /// now.</summary>
    public bool toggle(string token, object? force)
    {
        DomTokens.Require(token, "toggle");
        var t = Tokens();
        if (RangeHost.Truthy(force))
        {
            if (!t.Contains(token)) { t.Add(token); SetTokens(t); }
            return true;
        }
        if (t.Contains(token)) { t.Remove(token); SetTokens(t); }
        return false;
    }
    public string value
    {
        get => El.GetAttribute("rel") ?? "";
        set => El.SetAttribute("rel", value ?? "");
    }
    public override string ToString() => value;
    public bool replace(string token, string newToken)
    {
        DomTokens.Require(token, "replace");
        DomTokens.Require(newToken, "replace");
        var t = Tokens();
        if (!t.Contains(token)) return false;
        t.Remove(token);
        if (!t.Contains(newToken)) t.Add(newToken);
        SetTokens(t);
        return true;
    }
    /// <summary>DOM §3.3.6: a 'rel' list is one of the lists that declares supported tokens, so the
    /// member answers here instead of refusing the way the class list does. Measured against the
    /// reference, which answers false for a link type it does hold and for one it does not.</summary>
    public bool supports(string token)
    {
        DomTokens.Require(token, "supports");
        return false;
    }
}

public class TextNodeWrapper : IDomPrototypeHost
{
    private readonly TextNode _node;
    public TextNodeWrapper(TextNode node) => _node = node;
    internal TextNode NativeNode => _node;

    // A CDATA section is the same shape as a text node with its own name, type and interface: the
    // reference gives '#cdata-section', 4 and a CDATASection at the end of the chain (DOM §4.3.8).
    private bool IsCData => _node is CDataSection;
    public string nodeName => IsCData ? "#cdata-section" : "#text";
    public string? textContent { get => _node.TextContent; set => _node.Data = value ?? ""; }
    public string? data { get => _node.TextContent; set => _node.Data = value ?? ""; }
    public string? nodeValue { get => _node.TextContent; set => _node.Data = value ?? ""; }
    public int nodeType => IsCData ? 4 : 3;
    public string? wholeText => _node.WholeText;
    public object? ownerDocument
        => JsIntegrationService.WrapAny(_node.OwnerDocument) ?? JavaScriptEngine.Current?.IntegrationService?.Document;
    public string? __domType => IsCData ? "CDATASection" : "Text";
    public string baseURI => NodeBaseUri.For(_node);
    public string[] __domTypeChain => IsCData
        ? new[] { "EventTarget", "Node", "CharacterData", "Text", "CDATASection" }
        : new[] { "EventTarget", "Node", "Text" };
    public ElementHost? parentElement
    {
        get
        {
            var p = _node.ParentElement;
            if (p == null) return null;
            var engine = JavaScriptEngine.Current;
            return engine?.IntegrationService?.WrapDomNode(p) as ElementHost ?? new ElementHost(p);
        }
    }
    public object? parentNode
    {
        get => JsIntegrationService.WrapAny(_node.Parent);
    }
    public object? nextSibling
    {
        get => JsIntegrationService.WrapAny(_node.NextSibling);
    }
    public object? previousSibling
    {
        get => JsIntegrationService.WrapAny(_node.PreviousSibling);
    }
    public object? cloneNode(bool deep)
    {
        var cloned = _node.CloneNode(deep);
        var engine = JavaScriptEngine.Current;
        return engine?.IntegrationService != null
            ? engine.IntegrationService.WrapDomNode((Node)cloned)
            : new TextNodeWrapper((TextNode)cloned);
    }
    public void remove() => _node.Parent?.RemoveChild(_node);

    // CharacterData mixin (DOM 4 §4.5): the mutating methods every text/comment node answers,
    // forwarded to the underlying node that already implements them.
    public int length => _node.Data?.Length ?? 0;
    public string? substringData(int offset, int count) => _node.SubstringData(offset, count);
    public void appendData(string data) => _node.AppendData(data);
    public void insertData(int offset, string data) => _node.InsertData(offset, data);
    public void deleteData(int offset, int count) => _node.DeleteData(offset, count);
    public void replaceData(int offset, int count, string data) => _node.ReplaceData(offset, count, data);

    /// <summary>DOM 4 §4.6: split this text node at an offset, returning the trailing node that
    /// is inserted as the next sibling (the original keeps the leading characters).</summary>
    public object? splitText(int offset)
    {
        var tail = _node.SplitText(offset);
        var engine = JavaScriptEngine.Current;
        return engine?.IntegrationService != null
            ? engine.IntegrationService.WrapDomNode((Node)tail)
            : new TextNodeWrapper(tail);
    }
}

public class CommentHost : IDomPrototypeHost
{
    private readonly CommentNode _node;
    public CommentHost(CommentNode node) => _node = node;
    internal CommentNode NativeNode => _node;
    public string nodeName => "#comment";
    public int nodeType => 8;
    public string? data { get => _node.Data; set => _node.Data = value ?? ""; }
    public string? nodeValue { get => _node.Data; set => _node.Data = value ?? ""; }
    public string? textContent { get => _node.Data; set => _node.Data = value ?? ""; }
    public int length => _node.Data?.Length ?? 0;
    public object? ownerDocument
        => JsIntegrationService.WrapAny(_node.OwnerDocument) ?? JavaScriptEngine.Current?.IntegrationService?.Document;
    public string? __domType => "Comment";
    public string baseURI => NodeBaseUri.For(_node);
    public string[] __domTypeChain => new[] { "EventTarget", "Node", "CharacterData", "Comment" };
    public object? parentNode
    {
        get => JsIntegrationService.WrapAny(_node.Parent);
    }
    public object? nextSibling
    {
        get => JsIntegrationService.WrapAny(_node.NextSibling);
    }
    public object? previousSibling
    {
        get => JsIntegrationService.WrapAny(_node.PreviousSibling);
    }
    public object? cloneNode(bool deep) => JsIntegrationService.WrapAny(_node.CloneNode(deep));
    public void remove() => _node.Parent?.RemoveChild(_node);

    // CharacterData mixin (DOM 4 §4.5) — a comment answers the same methods a text node does.
    public string substringData(int offset, int count) => _node.SubstringData(offset, count);
    public void appendData(string data) => _node.AppendData(data);
    public void insertData(int offset, string data) => _node.InsertData(offset, data);
    public void deleteData(int offset, int count) => _node.DeleteData(offset, count);
    public void replaceData(int offset, int count, string data) => _node.ReplaceData(offset, count, data);
    public ElementHost? parentElement
    {
        get
        {
            var p = _node.ParentElement;
            if (p == null) return null;
            var engine = JavaScriptEngine.Current;
            return engine?.IntegrationService?.WrapDomNode(p) as ElementHost ?? new ElementHost(p);
        }
    }
}

public class DomTokenListHost : IDomPrototypeHost
{
    private readonly ElementHost _element;
    public DomTokenListHost(ElementHost element) => _element = element;

    // The page reads the list as the interface it is: 'el.classList instanceof DOMTokenList' and the
    // iterable face the script puts on that level both follow from the stamp.
    string[] IDomPrototypeHost.__domTypeChain => new[] { "DOMTokenList" };

    public int length => _element.NativeElement.ClassList.Length;

    /// <summary>DOM §3.3.6: the named getter, so a page may read a token by its index as well as with
    /// 'item'.</summary>
    public string? this[int index] => _element.classList_item(index);

    public void add(params string[] classNames)
    {
        // Every token is checked before the first one reaches the attribute, so a list is never left
        // holding part of a refused call.
        foreach (var cn in classNames) DomTokens.Require(cn, "add");
        foreach (var cn in classNames) _element.classList_add(cn);
    }
    public void remove(params string[] classNames)
    {
        foreach (var cn in classNames) DomTokens.Require(cn, "remove");
        foreach (var cn in classNames) _element.classList_remove(cn);
    }
    public bool contains(string className) => _element.classList_contains(className);
    public bool toggle(string className)
    {
        DomTokens.Require(className, "toggle");
        return _element.classList_toggle(className);
    }
    /// <summary>DOM §3.3.6: with the second argument the member stops toggling — the token is put in
    /// when it is present and taken out when it is absent, and what comes back is whether the list
    /// holds it now.</summary>
    public bool toggle(string className, object? force)
    {
        DomTokens.Require(className, "toggle");
        if (RangeHost.Truthy(force))
        {
            if (!_element.classList_contains(className)) _element.classList_add(className);
            return true;
        }
        if (_element.classList_contains(className)) _element.classList_remove(className);
        return false;
    }
    public string? item(int index) => _element.classList_item(index);
    // DOMTokenList is a set of tokens that serialises as the space-separated class attribute;
    // 'value' is that string and 'toString' is what a page gets from String(el.classList).
    public string value
    {
        get => string.Join(" ", _element.NativeElement.ClassList);
        set => _element.className = value ?? "";
    }
    public override string ToString() => value;
    public bool replace(string token, string newToken)
    {
        DomTokens.Require(token, "replace");
        DomTokens.Require(newToken, "replace");
        if (!_element.classList_contains(token)) return false;
        _element.classList_remove(token);
        _element.classList_add(newToken);
        return true;
    }
}

public class ScriptEvent : IDomPrototypeHost
{
    /// <summary>The interface this event reads as (DOM §4.5.2, UI Events §2 and the families built on
    /// top of them). A page that calls 'new WheelEvent(...)' asks the document for an event of that
    /// kind, and the kind is what stamps the wrapper with that interface's prototype — so
    /// 'wheel instanceof WheelEvent' and '[object WheelEvent]' both follow from it.</summary>
    internal string Kind = "Event";

    string[] IDomPrototypeHost.__domTypeChain =>
        Kind == "Event" ? new[] { "Event" } : new[] { "Event", Kind };

    public string type { get; set; }
    // DOM §2.3.5: 'target' is where the event was dispatched and 'currentTarget' the node whose
    // listeners are running right now — both are set by the walk, not by whoever built the event, and
    // both are nil again once it has finished (measured: 'eventPhase' is 0 and the two are null after a
    // dispatch returns). The current target is the document for a listener registered on it, so the
    // pair is held as 'object'.
    public object? target { get; set; }
    public object? currentTarget { get; set; }
    public bool bubbles { get; set; } = true;
    public bool cancelable { get; set; } = true;
    // DOM §2.3.5: the page reads this as 'defaultPrevented' — the name the interface member carries.
    public bool defaultPrevented { get; private set; }
    public long timeStamp { get; }
    public int eventPhase { get; set; }
    public object? detail { get; set; }
    public bool composed { get; set; } = false;
    public bool isTrusted { get; } = true;
    public string? returnValue { get; set; }
    public string? srcElement => target?.ToString();
    public bool cancelBubble { get => !bubbles; set => bubbles = !value; }

    // Keyboard event properties
    public string? key { get; set; }
    public bool ctrlKey { get; set; }
    public bool shiftKey { get; set; }
    public bool altKey { get; set; }
    public bool metaKey { get; set; }
    public bool repeat { get; set; }
    public bool isComposing { get; set; }
    public int keyCode { get; set; }
    public int which { get; set; }
    public bool charCode { get; set; }

    // Mouse event properties
    public double clientX { get; set; }
    public double clientY { get; set; }
    public double screenX { get; set; }
    public double screenY { get; set; }
    public int button { get; set; }
    public int buttons { get; set; }
    public ElementHost? relatedTarget { get; set; }

    // Wheel event properties
    public double deltaX { get; set; }
    public double deltaY { get; set; }
    public double deltaZ { get; set; }
    public int deltaMode { get; set; }

    // Animation event properties (CSS Animations 1 §4.1)
    public string animationName { get; set; } = "";
    public double elapsedTime { get; set; }
    public string pseudoElement { get; set; } = "";

    // Transition event properties (CSS Transitions 1 §6)
    public string propertyName { get; set; } = "";

    // TimeEvent.currentTime, shared by both families (Web Animations §4.7)
    public double currentTime { get; set; }

    public ScriptEvent(string type, ElementHost? target)
    {
        this.type = type;
        this.target = target;
        timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>DOM §2.3.6: the flag is only set for an event that can be cancelled, and it is the
    /// event's own 'cancelable' that says whether it can be.</summary>
    public void preventDefault()
    {
        if (cancelable) defaultPrevented = true;
    }

    internal bool PropagationStopped;
    internal bool ImmediateStopped;

    /// <summary>DOM 4 §2.5: the event's path — the target and every ancestor up to the document,
    /// innermost first. A listener reads this to know the chain a capture/bubble walk will follow.</summary>
    public object[] composedPath()
    {
        var engine = JavaScriptEngine.Current;
        var start = target ?? currentTarget;
        var path = new List<object>();
        if (start == null) return path.ToArray();
        if (start is not ElementHost startHost)
        {
            // The document is the only other node a dispatch can be pointed at, and the path from it
            // is just itself (DOM §2.5).
            path.Add(start);
            return path.ToArray();
        }
        var el = startHost.NativeElement;
        if (el == null) { path.Add(start); return path.ToArray(); }
        for (var n = (Node)el; n != null; n = n.Parent)
        {
            if (n is Acrux.Core.Dom.Document doc)
                path.Add((object?)engine?.DocumentHost ?? new DocumentHost(doc));
            else if (n is Element e)
                path.Add((object?)engine?.IntegrationService?.WrapDomNode(e) ?? new ElementHost(e));
        }
        return path.ToArray();
    }

    public void stopPropagation() { PropagationStopped = true; bubbles = false; }
    public void stopImmediatePropagation() { PropagationStopped = true; ImmediateStopped = true; bubbles = false; }
    public void initEvent(string type, bool bubbles = true, bool cancelable = true)
    {
        this.type = type;
        this.bubbles = bubbles;
        this.cancelable = cancelable;
    }
    public void initUIEvent(string type, bool bubbles = true, bool cancelable = true, object? view = null, int detail = 0)
    {
        this.type = type;
        this.bubbles = bubbles;
        this.cancelable = cancelable;
        this.detail = detail;
    }
    public void initCustomEvent(string type, bool bubbles = true, bool cancelable = true, object? detail = null)
    {
        this.type = type;
        this.bubbles = bubbles;
        this.cancelable = cancelable;
        this.detail = detail;
    }
    public void initMouseEvent(string type, bool bubbles = true, bool cancelable = true, object? view = null, int detail = 0, int screenX = 0, int screenY = 0, int clientX = 0, int clientY = 0, bool ctrlKey = false, bool altKey = false, bool shiftKey = false, bool metaKey = false, int button = 0, object? relatedTarget = null)
    {
        this.type = type;
        this.bubbles = bubbles;
        this.cancelable = cancelable;
        this.detail = detail;
        this.screenX = screenX;
        this.screenY = screenY;
        this.clientX = clientX;
        this.clientY = clientY;
        this.ctrlKey = ctrlKey;
        this.altKey = altKey;
        this.shiftKey = shiftKey;
        this.metaKey = metaKey;
        this.button = button;
    }
}

/// <summary>DOM §4.3.5: a processing instruction is a node a page can make and read, with the
/// target it was made from and the data after it. In an HTML document it is only ever something
/// the script builds — the parser never gives one — but the object behaves the same either way.</summary>
public class ProcessingInstructionHost : IDomPrototypeHost
{
    private readonly ProcessingInstruction _node;

    public ProcessingInstructionHost(ProcessingInstruction node) => _node = node;

    internal ProcessingInstruction NativeNode => _node;

    public string target => _node.Target;
    public string nodeName => _node.Target;
    public int nodeType => 7;
    public string? data { get => _node.Data; set => _node.Data = value ?? ""; }
    public string? nodeValue { get => _node.Data; set => _node.Data = value ?? ""; }
    public string? textContent { get => _node.Data; set => _node.Data = value ?? ""; }
    public int length => _node.Data?.Length ?? 0;
    public string[] __domTypeChain => new[] { "EventTarget", "Node", "CharacterData", "ProcessingInstruction" };
    public string baseURI => NodeBaseUri.For(_node);
    public object? ownerDocument
        => JsIntegrationService.WrapAny(_node.OwnerDocument) ?? JavaScriptEngine.Current?.IntegrationService?.Document;
    public object? parentNode => JsIntegrationService.WrapAny(_node.Parent);
    public ElementHost? parentElement
    {
        get
        {
            var p = _node.ParentElement;
            if (p == null) return null;
            var engine = JavaScriptEngine.Current;
            return engine?.IntegrationService?.WrapDomNode(p) as ElementHost ?? new ElementHost(p);
        }
    }
    public object? previousSibling => JsIntegrationService.WrapAny(_node.PreviousSibling);
    public object? nextSibling => JsIntegrationService.WrapAny(_node.NextSibling);
    // A processing instruction is a leaf: it has a child list, and it is always empty (§4.3.5).
    public object[] childNodes => Array.Empty<object>();
    public object? firstChild => null;
    public object? lastChild => null;
    public bool hasChildNodes() => false;
    public object? cloneNode(bool deep) => JsIntegrationService.WrapAny(_node.CloneNode(deep));
    public void remove() => _node.Parent?.RemoveChild(_node);

    // CharacterData mixin (DOM 4 §4.5) — the same methods a text and a comment node answer.
    public string substringData(int offset, int count) => _node.SubstringData(offset, count);
    public void appendData(string data) => _node.AppendData(data);
    public void insertData(int offset, string data) => _node.InsertData(offset, data);
    public void deleteData(int offset, int count) => _node.DeleteData(offset, count);
    public void replaceData(int offset, int count, string data) => _node.ReplaceData(offset, count, data);
}

public class DatasetHost : System.Dynamic.DynamicObject
{
    private readonly Element _element;

    public DatasetHost(Element element) => _element = element;

    public override bool TryGetMember(System.Dynamic.GetMemberBinder binder, out object? result)
    {
        var attrName = ToDataDash(binder.Name);
        result = _element.GetAttribute(attrName);
        return true;
    }

    public override bool TrySetMember(System.Dynamic.SetMemberBinder binder, object? value)
    {
        var attrName = ToDataDash(binder.Name);
        if (value == null)
            _element.RemoveAttribute(attrName);
        else
            _element.SetAttribute(attrName, value.ToString() ?? "");
        return true;
    }

    public override bool TryDeleteMember(System.Dynamic.DeleteMemberBinder binder)
    {
        var attrName = ToDataDash(binder.Name);
        _element.RemoveAttribute(attrName);
        return true;
    }

    public bool Has(string name) => _element.GetAttribute(ToDataDash(name)) != null;

    public string? Get(string name) => _element.GetAttribute(ToDataDash(name));

    public void Set(string name, string? value)
    {
        if (value == null)
            _element.RemoveAttribute(ToDataDash(name));
        else
            _element.SetAttribute(ToDataDash(name), value);
    }

    private static string ToDataDash(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return "data-";
        var sb = new System.Text.StringBuilder("data-");
        foreach (var ch in camelCase)
        {
            if (char.IsUpper(ch))
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}

public class DomRect
{
    public float x { get; }
    public float y { get; }
    public float width { get; }
    public float height { get; }
    public float top => y;
    public float right => x + width;
    public float bottom => y + height;
    public float left => x;

    public DomRect(float x, float y, float width, float height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }
}
