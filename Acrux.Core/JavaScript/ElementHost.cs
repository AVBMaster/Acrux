using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Acrux.Core.Css;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;

namespace Acrux.Core.JavaScript;

public class ElementHost
{
    private readonly Element _element;
    private CssStyleDeclaration? _styleHost;
    private readonly Dictionary<string, List<int>> _eventCallbackIds = new();
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

    public string __domType => "HTMLElement";

    public string[] __domTypeChain => new[] { "EventTarget", "Node", "Element", "HTMLElement" };

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
        get => _element.Value;
        set => _element.Value = value;
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

    public ElementHost? parentNode => parentElement;

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

    public JsNodeList children => new JsNodeList(_element, Engine);

    public int childElementCount => _element.Children.OfType<Element>().Count();

    public JsNodeList childNodes => new JsNodeList(_element, Engine);

    public NamedNodeMapHost attributes => new(_element);

    public string? getAttribute(string name) => _element.GetAttribute(name);

    public void setAttribute(string name, string value) => _element.SetAttribute(name, value);

    public void removeAttribute(string name) => _element.RemoveAttribute(name);

    public bool hasAttribute(string name) => _element.HasAttribute(name);

    public bool hasChildNodes() => _element.Children.Count > 0;

    public object? closest(string selector)
    {
        var el = _element;
        while (el != null)
        {
            if (MatchesSelector(el, selector))
                return WrapWithCache(el);
            el = el.ParentElement;
        }
        return null;
    }

    public object? querySelector(string selector)
    {
        var result = QuerySelectorAllInternal(_element, selector).FirstOrDefault();
        return result != null ? WrapWithCache(result) : null;
    }

    public JsNodeList querySelectorAll(string selector) =>
        new JsNodeList(_element, Engine, root => QuerySelectorAllInternal(root, selector));

    public JsHtmlCollection getElementsByTagName(string tagName) =>
        new JsHtmlCollection(_element, Engine, tagName);

    public JsHtmlCollection getElementsByClassName(string className) =>
        new JsHtmlCollection(_element, Engine, null, className);

    public void appendChild(object child)
    {
        var host = child as ElementHost;
        if (host == null) return;
        _element.AppendChild(host.NativeElement);
    }

    public void insertBefore(object newChild, object? refChild)
    {
        var newHost = newChild as ElementHost;
        if (newHost == null) return;
        Element? refEl = null;
        if (refChild is ElementHost refHost)
            refEl = refHost.NativeElement;
        _element.InsertBefore(newHost.NativeElement, refEl);
    }

    public void removeChild(object child)
    {
        var host = child as ElementHost;
        if (host == null) return;
        _element.RemoveChild(host.NativeElement);
    }

    public void replaceChild(object newChild, object oldChild)
    {
        var newHost = newChild as ElementHost;
        var oldHost = oldChild as ElementHost;
        if (newHost == null || oldHost == null) return;
        int idx = _element.Children.IndexOf(oldHost.NativeElement);
        if (idx >= 0)
        {
            _element.Children[idx] = newHost.NativeElement;
            newHost.NativeElement.Parent = _element;
            oldHost.NativeElement.Parent = null;
        }
    }

    public object cloneNode(bool deep)
    {
        var cloned = CloneElement(_element, deep);
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
        if (options is bool b)
            useCapture = b;
        else if (options != null)
        {
            // Try to extract capture/once/passive from options object
            try { useCapture = Convert.ToBoolean(options.GetType().GetProperty("capture")?.GetValue(options) ?? false); } catch { }
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
                engine.RemoveCallback(cbId);
            list.Clear();
            _eventCallbackIds.Remove(type);
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

    public DomRect getBoundingClientRect()
    {
        FlushForRead();
        var box = _element.LayoutBox;
        if (box == null)
            return new DomRect(0, 0, 0, 0);

        return new DomRect(
            box.BorderBox.Left,
            box.BorderBox.Top,
            box.BorderBox.Width,
            box.BorderBox.Height
        );
    }

    // Layout dimension properties
    public float offsetWidth 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.BorderBox.Width;
        }
    }

    public float offsetHeight 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.BorderBox.Height;
        }
    }

    public float clientWidth 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.ContentBox.Width;
        }
    }

    public float clientHeight 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.ContentBox.Height;
        }
    }

    public float clientLeft => 0;
    public float clientTop => 0;

    public ElementHost offsetParent
    {
        get
        {
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
            return box.ContentBox.Width;
        }
    }

    public float scrollHeight 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.ContentBox.Height;
        }
    }

    public double scrollLeft
    {
        get { FlushForRead(); return _element.LayoutBox?.ScrollX ?? 0; }
        set { if (_element.LayoutBox != null) _element.LayoutBox.ScrollX = (float)value; }
    }

    public double scrollTop
    {
        get { FlushForRead(); return _element.LayoutBox?.ScrollY ?? 0; }
        set { if (_element.LayoutBox != null) _element.LayoutBox.ScrollY = (float)value; }
    }

    public float offsetTop 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.BorderBox.Top;
        }
    }

    public float offsetLeft 
    {
        get 
        {
            FlushForRead();
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.BorderBox.Left;
        }
    }

    public object[] classListValues => _element.ClassList.Select(c => (object)c).ToArray();

    public DomTokenListHost classList => new(this);

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

    public string? getAttributeNS(string ns, string name) => _element.GetAttribute(name);

    public void setAttributeNS(string ns, string name, string value) => _element.SetAttribute(name, value);
    public void removeAttributeNS(string ns, string name) => _element.RemoveAttribute(name);

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
        return !scriptEvt.DefaultPrevented;
    }

    internal bool DispatchEvent(ScriptEvent evt)
    {
        if (string.IsNullOrEmpty(evt.type)) return true;

        var engine = JavaScriptEngine.Current ?? Engine;

        // Target phase - dispatch element-level listeners
        if (_eventCallbackIds.TryGetValue(evt.type, out var cbIds))
        {
            if (engine != null)
            {
                foreach (var cbId in cbIds.ToList())
                {
                    try
                    {
                        evt.currentTarget = this;
                        evt.eventPhase = 2; // AT_TARGET
                        var svc = engine.IntegrationService;
                        if (svc != null)
                            svc.Facade.InvokeJsFunction(cbId, evt);
                        else
                            engine.InvokeCallbackWith(cbId, evt);
                    }
                    catch { }
                }
            }
        }

        // Bubbling phase
        if (evt.bubbles)
        {
            var parent = _element.ParentElement;
            if (parent != null)
            {
                var parentHost = WrapWithCache(parent);
                if (parentHost != null)
                {
                    evt.eventPhase = 3; // BUBBLING_PHASE
                    parentHost.DispatchEvent(evt);
                }
            }
            else if (_element.Parent is Core.Dom.Document)
            {
                // Dispatch to document when bubbling reaches root element
                if (engine?.DocumentHost != null)
                {
                    try
                    {
                        evt.eventPhase = 3;
                        engine.DocumentHost.dispatchEvent(evt);
                    }
                    catch { }
                }
            }
        }

        // Check for inline event handler
        var attrName = "on" + evt.type;
        var attr = _element.GetAttribute(attrName);
        if (!string.IsNullOrEmpty(attr))
        {
            if (engine != null)
            {
                try { engine.Evaluate(attr); }
                catch { }
            }
        }

        return !evt.DefaultPrevented;
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

    private static void SerializeNode(Node node, System.Text.StringBuilder sb)
    {
        if (node is TextNode tn)
        {
            sb.Append(System.Net.WebUtility.HtmlEncode(tn.TextContent ?? ""));
        }
        else if (node is Element el)
        {
            sb.Append('<').Append(el.TagName.ToLowerInvariant());
            foreach (var attr in el.Attributes)
            {
                sb.Append(' ').Append(attr.Key);
                if (!string.IsNullOrEmpty(attr.Value))
                    sb.Append("=\"").Append(System.Net.WebUtility.HtmlEncode(attr.Value)).Append('"');
            }
            sb.Append('>');
            foreach (var child in el.Children)
                SerializeNode(child, sb);
            sb.Append("</").Append(el.TagName.ToLowerInvariant()).Append('>');
        }
    }

    private static bool MatchesSelector(Element el, string selector)
    {
        selector = selector.Trim();
        if (selector == "*")
            return true;
        if (selector.StartsWith('#'))
            return el.Id == selector[1..];
        if (selector.StartsWith('.'))
            return el.HasClass(selector[1..]);
        return el.TagName.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }

    private static List<Element> QuerySelectorAllInternal(Element root, string selector)
    {
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSelector(child, selector))
                result.Add(child);
            result.AddRange(QuerySelectorAllInternal(child, selector));
        }
        return result;
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

    private static Element CloneElement(Element source, bool deep)
    {
        var clone = new HtmlElement(source.TagName);
        foreach (var attr in source.Attributes)
            clone.Attributes[attr.Key] = attr.Value;
        foreach (var kv in source.Style)
            clone.Style[kv.Key] = kv.Value;
        if (deep)
        {
            foreach (var child in source.Children)
            {
                if (child is Element childEl)
                    clone.AppendChild(CloneElement(childEl, true));
                else if (child is TextNode tn)
                    clone.AppendChild(new TextNode(tn.TextContent ?? ""));
            }
        }
        return clone;
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

    private object WrapNode(Node node)
    {
        if (node is Element e) return WrapWithCache(e) ?? new ElementHost(e);
        return new TextNodeWrapper((TextNode)node);
    }

    public int getClientRects() => 1;

    public bool hasAttributes() => _element.Attributes.Count > 0;

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

    public string? namespaceURI => "http://www.w3.org/1999/xhtml";

    public string? prefix => null;

    public string localName => _element.TagName.ToLowerInvariant();

    public string baseURI => "";

    public bool isConnected => _element.ParentElement != null || _element.TagName == "HTML";

    public object? ownerDocument => null;

    public bool matches(string selector)
    {
        return CssSelectorMatcher.Matches(selector, _element);
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
        insertAdjacentHTML(position, text);
    }

    public void remove()
    {
        _element.ParentElement?.RemoveChild(_element);
    }

    public object? replaceWith(object newElement)
    {
        if (newElement is ElementHost host)
        {
            var parent = _element.ParentElement;
            if (parent != null)
            {
                parent.RemoveChild(_element);
                parent.AppendChild(host._element);
            }
        }
        return null;
    }

    public object? before(object newElement)
    {
        if (newElement is ElementHost host)
        {
            _element.ParentElement?.InsertBefore(host._element, _element);
        }
        return null;
    }

    public object? after(object newElement)
    {
        if (newElement is ElementHost host)
        {
            _element.ParentElement?.InsertBefore(host._element, _element.NextSibling);
        }
        return null;
    }

    public bool hasAttributeNS(string ns, string name)
    {
        return _element.HasAttribute(name);
    }

    private static int _datasetProxyCounter;
    private object? _datasetProxyCache;

    public object? dataset
    {
        get
        {
            var engine = JavaScriptEngine.Current ?? Engine;
            if (engine == null)
                return new DatasetHost(_element);

            if (engine.EngineType == JsEngineType.V8)
            {
                if (_datasetProxyCache != null)
                    return _datasetProxyCache;

                try
                {
                    var adapter = engine.Adapter;
                    if (adapter != null)
                    {
                        var tmp = $"__tmp_ds_{Interlocked.Increment(ref _datasetProxyCounter)}";
                        adapter.EmbedHostObject(tmp, this);
                        var result = adapter.Evaluate(@"
                            (function() {
                                var el = " + tmp + @";
                                return new Proxy({}, {
                                    get: function(t, p) {
                                        if (typeof p === 'string') {
                                            var v = el.getAttribute('data-' + p);
                                            return v !== null ? v : undefined;
                                        }
                                    },
                                    set: function(t, p, v) {
                                        el.setAttribute('data-' + p, v);
                                        return true;
                                    },
                                    deleteProperty: function(t, p) {
                                        el.removeAttribute('data-' + p);
                                        return true;
                                    },
                                    has: function(t, p) {
                                        return el.getAttribute('data-' + p) !== null;
                                    },
                                    ownKeys: function(t) { return []; },
                                    getOwnPropertyDescriptor: function(t, p) {
                                        return { configurable: true, enumerable: true };
                                    }
                                });
                            })()
                        ");
                        try { adapter.Evaluate($"delete {tmp};"); } catch { }
                        _datasetProxyCache = result;
                        return _datasetProxyCache;
                    }
                }
                catch { }
            }

            return new DatasetHost(_element);
        }
    }

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

    public string? slot
    {
        get => _element.GetAttribute("slot");
        set => _element.SetAttribute("slot", value);
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

    public bool assignedSlot => false;

    public int nodeType => 1;

    public string? nodeValue { get; set; }

    public void toggleAttribute(string name)
    {
        if (_element.HasAttribute(name))
            _element.RemoveAttribute(name);
        else
            _element.SetAttribute(name, "");
    }

    public object? getAttributeNode(string name)
    {
        var val = _element.GetAttribute(name);
        return val != null ? new AttributeHost(name, val) : null;
    }

    public object? getAttributeNodeNS(string ns, string name)
    {
        return getAttributeNode(name);
    }

    public object? setAttributeNode(object attr)
    {
        if (attr is AttributeHost host)
        {
            _element.SetAttribute(host.name, host.value ?? "");
            return host;
        }
        return null;
    }

    public object? setAttributeNodeNS(object attr) => setAttributeNode(attr);

    public object? removeAttributeNode(object attr)
    {
        if (attr is AttributeHost host)
        {
            _element.RemoveAttribute(host.name);
            return host;
        }
        return null;
    }

    public void normalize()
    {
        // Merge adjacent text nodes
        var newChildren = new List<Node>();
        TextNode? lastText = null;
        foreach (var child in _element.Children)
        {
            if (child is TextNode tn)
            {
                if (lastText != null)
                {
                    lastText.Data += tn.Data;
                }
                else
                {
                    lastText = tn;
                    newChildren.Add(tn);
                }
            }
            else
            {
                lastText = null;
                newChildren.Add(child);
            }
        }
        _element.Children.Clear();
        foreach (var child in newChildren)
            _element.AppendChild(child);
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

    public int compareDocumentPosition(object? other) => 0;

    public object? getRootNode(object? options) => null;

    public void setPointerCapture(int pointerId) { }

    public void releasePointerCapture(int pointerId) { }

    public bool hasPointerCapture(int pointerId) => false;

    // ===== HTMLInputElement / HTMLTextAreaElement specific =====
    public string? type
    {
        get => inputType;
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
        get => _element.Children.OfType<Element>().ToList().FindIndex(e => e.HasAttribute("selected"));
        set
        {
            var options = _element.Children.OfType<Element>().ToList();
            for (int i = 0; i < options.Count; i++)
            {
                if (i == value) options[i].SetAttribute("selected", "");
                else options[i].RemoveAttribute("selected");
            }
        }
    }

    public object[] options
    {
        get => _element.Children.OfType<Element>().Select(e => (object)(WrapWithCache(e) ?? new ElementHost(e))).ToArray();
    }

    public int selectLength => _element.Children.OfType<Element>().Count();

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
        get => _element.GetAttribute("href");
        set => _element.SetAttribute("href", value);
    }

    public string? rel
    {
        get => _element.GetAttribute("rel");
        set => _element.SetAttribute("rel", value);
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

public class TextNodeWrapper
{
    private readonly TextNode _node;
    public TextNodeWrapper(TextNode node) => _node = node;
    public string nodeName => "#text";
    public string? textContent { get => _node.TextContent; set => _node.Data = value ?? ""; }
    public string? data { get => _node.TextContent; set => _node.Data = value ?? ""; }
    public int nodeType => 3;
    public string? wholeText => _node.TextContent;
    public string? __domType => "Text";
    public string[] __domTypeChain => new[] { "EventTarget", "Node", "Text" };
}

public class DomTokenListHost
{
    private readonly ElementHost _element;
    public DomTokenListHost(ElementHost element) => _element = element;
    public int length => _element.NativeElement.ClassList.Length;
    public void add(params string[] classNames)
    {
        foreach (var cn in classNames)
            _element.classList_add(cn);
    }
    public void remove(string className) => _element.classList_remove(className);
    public bool contains(string className) => _element.classList_contains(className);
    public bool toggle(string className) => _element.classList_toggle(className);
    public string? item(int index) => _element.classList_item(index);
}

public class ScriptEvent
{
    public string type { get; set; }
    public ElementHost? target { get; }
    public ElementHost? currentTarget { get; set; }
    public bool bubbles { get; set; } = true;
    public bool cancelable { get; set; } = true;
    public bool DefaultPrevented { get; private set; }
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
    public string? code { get; set; }
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

    public void preventDefault() => DefaultPrevented = true;
    public void stopPropagation() => bubbles = false;
    public void stopImmediatePropagation() => bubbles = false;
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
