using System.Diagnostics.CodeAnalysis;
using Acrux.Core.Css;
using Acrux.Core.Dom;
using DomDocument = Acrux.Core.Dom.Document;
using DomElement = Acrux.Core.Dom.Element;

namespace Acrux.Core.JavaScript;

public class DocumentHost
{
    private readonly DomDocument _document;
    private ElementHost? _documentElementHost;
    private ElementHost? _bodyHost;
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
        if (_document.DocumentElement == null) return null;
        var el = QuerySelectorInternal(_document.DocumentElement, selector);
        return el != null ? WrapElement(el) : null;
    }

    public object querySelectorAll(string selector)
    {
        if (_document.DocumentElement == null) return new object[0];
        return new JsNodeList(_document.DocumentElement, Engine,
            root => QuerySelectorAllInternal(root, selector));
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
        var el = new HtmlElement(tagName);
        var engine = JavaScriptEngine.Current ?? Engine;
        var integration = engine?.IntegrationService;
        if (integration != null)
            return (integration.WrapDomNode(el) as ElementHost)!;
        var host = new ElementHost(el);
        if (host.Engine == null && engine != null)
            host.Engine = engine;
        return host;
    }

    public ElementHost createElementNS(string? ns, string tagName)
    {
        var el = new HtmlElement(tagName) { NamespaceUri = ns };
        return WrapElement(el) ?? new ElementHost(el);
    }

    public TextNodeWrapper createTextNode(string text)
    {
        return new TextNodeWrapper(new TextNode(text));
    }

    public object? createComment(string data) =>
        new TextNodeWrapper(new TextNode(data));

    public object? createDocumentFragment()
    {
        return new DocumentFragmentHost(new DocumentFragment());
    }

    public object? createCDATASection(string data) => null;

    public object? createProcessingInstruction(string target, string data) => null;

    public object? createAttribute(string name) => new AttributeHost(name);

    public object? createAttributeNS(string? ns, string name) => new AttributeHost(name, ns);

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

    public string? getSelection() => null;

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
            var docType = _document.Children.OfType<DocumentTypeNode>().FirstOrDefault();
            return docType != null ? new DocumentTypeHost(docType) : null;
        }
    }

    public object implementation => new DomImplementation();

    public object? adoptNode(object node)
    {
        if (node is ElementHost elHost)
        {
            elHost.NativeElement.Remove();
            return elHost;
        }
        return node;
    }

    public object? importNode(object node, bool deep = false)
    {
        if (node is ElementHost elHost)
        {
            var cloned = elHost.NativeElement.CloneNode(deep);
            return WrapElement((DomElement)cloned)!;
        }
        return node;
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

    public bool exitFullscreen() => false;

    public string? currentScript => null;

    public string? styleSheets => null;

    public string? fonts => null;

    public object getComputedStyle(ElementHost element)
    {
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
                ["width"] = computedStyle.Width.ToString(),
                ["height"] = computedStyle.Height.ToString(),
                ["display"] = computedStyle.Display.ToCssString(),
                ["position"] = computedStyle.Position.ToCssString(),
                ["float"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.Float.ToString()),
                ["clear"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.Clear.ToString()),
                ["color"] = $"rgb({computedStyle.Color.Red}, {computedStyle.Color.Green}, {computedStyle.Color.Blue})",
                ["font-family"] = computedStyle.FontFamily ?? "",
                ["font-size"] = $"{computedStyle.FontSize}px",
                ["font-weight"] = ((int)computedStyle.FontWeight).ToString(),
                ["font-style"] = computedStyle.FontStyleCssText,
                ["line-height"] = computedStyle.LineHeight == 1.2f ? "normal" : computedStyle.LineHeight.ToString(),
                ["text-align"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.TextAlign.ToString()),
                ["text-decoration"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.TextDecoration.ToString()),
                ["white-space"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.WhiteSpace.ToString()),
                ["word-break"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.WordBreak.ToString()),
                ["overflow-wrap"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.OverflowWrap.ToString()),
                ["visibility"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.Visibility.ToString()),
                ["overflow"] = Dom.CssEnumFormatter.CssKeywordFromEnum(computedStyle.Overflow.ToString()),
                ["opacity"] = computedStyle.Opacity.ToString(),
                ["z-index"] = computedStyle.ZIndex?.ToString() ?? "auto",
                ["background-color"] = computedStyle.BackgroundColor.HasValue
                    ? $"rgba({computedStyle.BackgroundColor.Value.Red}, {computedStyle.BackgroundColor.Value.Green}, {computedStyle.BackgroundColor.Value.Blue}, {computedStyle.BackgroundColor.Value.Alpha / 255f})"
                    : "transparent",
                ["margin-top"] = computedStyle.MarginTop.ToCssString(),
                ["margin-right"] = computedStyle.MarginRight.ToCssString(),
                ["margin-bottom"] = computedStyle.MarginBottom.ToCssString(),
                ["margin-left"] = computedStyle.MarginLeft.ToCssString(),
                ["padding-top"] = computedStyle.PaddingTop.ToCssString(),
                ["padding-right"] = computedStyle.PaddingRight.ToCssString(),
                ["padding-bottom"] = computedStyle.PaddingBottom.ToCssString(),
                ["padding-left"] = computedStyle.PaddingLeft.ToCssString(),
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

        return new ComputedStyleHost(props, element.NativeElement.ComputedStyle);
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

    public object? createEvent(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "customevent" => new ScriptEvent("Custom", null),
            "mouseevent" => new ScriptEvent("Mouse", null),
            "keyevent" => new ScriptEvent("Key", null),
            _ => new ScriptEvent(type ?? "Event", null)
        };
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

    private static Element? QuerySelectorInternal(Element root, string selector)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSelector(child, selector))
                return child;
            var found = QuerySelectorInternal(child, selector);
            if (found != null) return found;
        }
        return null;
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

    private static bool MatchesSelector(Element el, string selector)
    {
        try
        {
            return CssSelectorMatcher.Matches(selector, el);
        }
        catch
        {
            // Fallback to simple matching on parse failure
            selector = selector.Trim();
            if (selector.StartsWith('#'))
                return el.Id == selector[1..];
            if (selector.StartsWith('.'))
                return el.HasClass(selector[1..]);
            return el.TagName.Equals(selector, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class ComputedStyleHost
{
    private readonly Dictionary<string, string> _properties;
    private readonly ComputedStyle? _style;

    public ComputedStyleHost(Dictionary<string, string> properties, ComputedStyle? style = null)
    {
        _properties = properties ?? new Dictionary<string, string>();
        _style = style;
    }

    public string? getProperty(string name) => getPropertyValue(name);

    /// <summary>The hand-written list above answers the properties it covers; everything else
    /// falls through to the serializer the animation engine already uses, so a property does
    /// not have to be added to two places before scripts can read it. Without this, a
    /// property that is fully modelled still reports the empty string to JS.</summary>
    public string? getPropertyValue(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var lower = name.ToLowerInvariant();
        return _properties.TryGetValue(lower, out var value)
            ? value
            : _style != null ? Acrux.Core.Dom.Animations.ComputedValueSerializer.Get(_style, lower) : null;
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

public class DocumentFragmentHost
{
    private readonly DocumentFragment _fragment;

    public DocumentFragmentHost(DocumentFragment fragment) => _fragment = fragment;

    public object? querySelector(string selector) => null;
    public object[] querySelectorAll(string selector) => Array.Empty<object>();
    public object[] children => Array.Empty<object>();
    public int childElementCount => 0;
}

public class DocumentTypeHost
{
    private readonly DocumentTypeNode _docType;

    public DocumentTypeHost(DocumentTypeNode docType)
    {
        _docType = docType;
    }

    public string name => _docType.Name ?? "html";
    public string publicId => _docType.PublicId ?? "";
    public string systemId => _docType.SystemId ?? "";
}

public class NamedNodeMapHost
{
    private readonly DomElement _element;

    public NamedNodeMapHost(DomElement element)
    {
        _element = element;
    }

    public int length => _element.Attributes.Count;

    public object? item(int index)
    {
        if (index < 0 || index >= _element.Attributes.Count) return null;
        var kv = _element.Attributes.ElementAt(index);
        return new AttributeHost(kv.Key, kv.Value);
    }

    public string? getNamedItem(string name)
    {
        return _element.GetAttribute(name);
    }

    public object? setNamedItem(object attr)
    {
        if (attr is AttributeHost host)
            _element.SetAttribute(host.name, host.value ?? "");
        return null;
    }

    public string? removeNamedItem(string name)
    {
        var val = _element.GetAttribute(name);
        _element.RemoveAttribute(name);
        return val;
    }
}

public class AttributeHost
{
    public string name { get; }
    public string? value { get; set; }
    public string? namespaceUri { get; }
    public bool specified => true;
    public bool isId => name.Equals("id", StringComparison.OrdinalIgnoreCase);

    public AttributeHost(string name, string? value = null, string? ns = null)
    {
        this.name = name;
        this.value = value;
        this.namespaceUri = ns;
    }
}
