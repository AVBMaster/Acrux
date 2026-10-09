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
                    if (host == null) continue;
                    // Only a sheet the script constructed itself can be adopted; a sheet an
                    // element owns is already in the cascade through that element, and the
                    // reference engine refuses the duplicate (measured: NotAllowedError).
                    if (!host.IsConstructed)
                        throw new InvalidOperationException(
                            "NotAllowedError: Can't adopt a non-constructed stylesheet.");
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
    public object getComputedStyle(ElementHost element)
    {
        // A computed style read is a style change event in disguise: whatever the
        // script did to the DOM earlier in the same tick has to be resolved first,
        // or the page reads the previous frame's values.
        Engine?.FlushForRead();

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
    private readonly Acrux.Core.Dom.Element? _owner;

    public ComputedStyleHost(Dictionary<string, string> properties, ComputedStyle? style = null,
        Acrux.Core.Dom.Element? owner = null)
    {
        _properties = properties ?? new Dictionary<string, string>();
        _style = style;
        _owner = owner;
    }

    public string? getProperty(string name) => getPropertyValue(name);

    /// <summary>CSSOM: a property the declaration block does not carry reads back as the
    /// empty string, never as null.</summary>
    public string getPropertyValue(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
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
            if (serialized != null) return UsedValue(lower, serialized) ?? serialized;
        }
        return _properties.TryGetValue(lower, out var value) ? value : "";
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
            case "width": return SizeUsed(text, box.ContentBox.Width);
            case "height": return SizeUsed(text, box.ContentBox.Height);
            case "transform-origin":
            case "perspective-origin":
                return TransformOriginUsed(text, LayoutUnit(box.BorderBox.Width),
                    LayoutUnit(box.BorderBox.Height)) ?? withAddresses;
            default:
                return withAddresses == text ? null : withAddresses;
        }
    }

    /// <summary>One box size on the computed surface: the used value where the authored value was
    /// 'auto', the authored pixel value snapped to a layout unit otherwise.</summary>
    private static string? SizeUsed(string text, float used)
    {
        if (text == "auto") return Px(LayoutUnit(used));
        if (text.EndsWith("px") && TryNumber(text.Substring(0, text.Length - 2), out var px))
            return Px(LayoutUnit((float)px));
        return null;
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
        var dashed = Acrux.Core.Css.Properties.CssPropertyIdExtensions
            .CanonicalName(Hyphenate(name));
        // The same refusal the dashed read makes: a name the reference engine does not declare is
        // not a member of a computed style at all, so 'getComputedStyle(el).textSecurity' and
        // 'getComputedStyle(el)["text-security"]' are undefined rather than the value the prefixed
        // property carries (measured both).
        if (Acrux.Core.Css.Resolver.CssPropertyTraits.IsEngineOnlyName(dashed)) return null;
        // Same precedence as getPropertyValue: the serialiser is the surface that knows the
        // computed-value model, and a camelCase read must not fall back onto the older
        // spellings the hand-built map still carries.
        if (_style != null)
        {
            var serialized = Acrux.Core.Dom.Animations.ComputedValueSerializer.Get(_style, dashed, includeShorthands: true);
            if (serialized != null) return UsedValue(dashed, serialized) ?? serialized;
        }
        return _properties.TryGetValue(dashed, out var value) ? value : null;
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
