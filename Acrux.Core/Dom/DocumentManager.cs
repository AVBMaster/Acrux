using System.Reflection;
using System.Text;
using Acrux.Core.Css;
using Acrux.Core.Dom.Html;
using Acrux.Core.Dom.Parser;
using AngleSharp;
using AngleSharp.Css.Parser;

namespace Acrux.Core.Dom;

public class DocumentManager
{
    private static readonly Stylesheet _uaStylesheet;
    private static readonly string _defaultHtml;
    private static readonly CssParser _cssParser = new();

    /// <summary>When true, uses the custom HTML parser (default). When false, uses AngleSharp.</summary>
    public static bool UseCustomParser { get; set; } = true;

    /// <summary>Gets the user agent stylesheet.</summary>
    public Stylesheet GetUaStylesheet() => _uaStylesheet;

    /// <summary>Gets the CSS parser.</summary>
    public CssParser GetCssParser() => _cssParser;

    private static string LoadEmbeddedResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Acrux.Core.Resources.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static DocumentManager()
    {
        _uaStylesheet = _cssParser.Parse(LoadEmbeddedResource("Css.ua-stylesheet.css"));
        _defaultHtml = LoadEmbeddedResource("Html.default.html");
    }

    public async Task<DocumentLoadResult> LoadHtmlAsync(string html, string? baseUrl = null, float viewportWidth = 1024f, float viewportHeight = 768f, float dpiScale = 1.0f)
    {
        Document doc;
        if (UseCustomParser)
        {
            doc = HtmlDocumentParserIntegration.ParseHtml(html);
            doc.Url = baseUrl ?? "acrux://local";
        }
        else
        {
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var angleSharpDoc = await context.OpenAsync(req => req.Content(html));
            doc = new Document
            {
                Url = baseUrl ?? "acrux://local",
                Title = angleSharpDoc.Title ?? "Untitled"
            };
            doc.DocumentElement = ConvertHtmlToDom(angleSharpDoc.DocumentElement!, doc);
        }

        var styleComputer = new StyleComputer();
        styleComputer.AddStylesheet(_uaStylesheet, Css.Resolver.CascadeOrigin.UserAgent);

        try
        {
            await LoadStylesFromHtml(doc, styleComputer, baseUrl);
            styleComputer.ComputeStyles(doc, viewportWidth, viewportHeight);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CSS] Error computing styles: {ex}");
        }

        try
        {
            var layoutEngine = new Acrux.Core.Layout.LayoutEngine();
            layoutEngine.Layout(doc, viewportWidth, viewportHeight, dpiScale);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Layout] Error during layout: {ex}");
        }

        return new DocumentLoadResult(doc, styleComputer);
    }

    public static string DefaultHtml => _defaultHtml;

    private static string? _testCssFeatureHtml;
    public static string TestCssFeatureHtml => _testCssFeatureHtml ??= LoadEmbeddedResource("Html.test-css-features.html");

    private static string? _jsTestHtml;
    public static string JsTestHtml => _jsTestHtml ??= LoadEmbeddedResource("Html.js-test.html");

    private static string? _elementTestHtml;
    public static string ElementTestHtml => _elementTestHtml ??= LoadEmbeddedResource("Html.element-test.html");

    private static string? _debugHtml;
    public static string DebugHtml => _debugHtml ??= LoadEmbeddedResource("Html.debug.html");

    private static string? _jsEngineHtml;
    public static string JsEngineHtml => _jsEngineHtml ??= LoadEmbeddedResource("Html.js-engine.html");

    private static List<Element> GetAllElements(Element root)
    {
        var result = new List<Element>();
        var queue = new Queue<Element>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var el = queue.Dequeue();
            result.Add(el);
            foreach (var child in el.Children)
                if (child is Element childEl)
                    queue.Enqueue(childEl);
        }
        return result;
    }

    private async Task LoadStylesFromHtml(Document doc, StyleComputer styleComputer, string? baseUrl)
    {
        var allElements = doc.DocumentElement != null ? GetAllElements(doc.DocumentElement) : new List<Element>();
        var styleElements = allElements.Where(e => e.TagName.ToLowerInvariant() == "style");
        foreach (var styleElement in styleElements)
        {
            var cssText = styleElement.TextContent;
            if (!string.IsNullOrEmpty(cssText))
            {
                try
                {
                    var stylesheet = _cssParser.Parse(cssText);
                    ProcessImports(stylesheet, styleComputer, baseUrl);
                    styleComputer.AddStylesheet(stylesheet);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CSS] Failed to parse <style>: {ex}");
                }
            }
        }

        var linkElements = allElements.Where(e =>
            e.TagName.ToLowerInvariant() == "link" &&
            e.GetAttribute("rel")?.ToLowerInvariant() == "stylesheet");

        foreach (var link in linkElements)
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrEmpty(href)) continue;
            var url = ResolveUrl(baseUrl, href);
            if (url == null) continue;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var cssText = await client.GetStringAsync(url);
                var stylesheet = _cssParser.Parse(cssText);
                await ProcessImports(stylesheet, styleComputer, url);
                styleComputer.AddStylesheet(stylesheet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSS] Failed to load stylesheet '{url}': {ex}");
            }
        }
    }

    private async Task ProcessImports(Stylesheet stylesheet, StyleComputer styleComputer, string? baseUrl)
    {
        foreach (var importRule in stylesheet.ImportRules)
        {
            if (string.IsNullOrEmpty(importRule.Url)) continue;
            var url = ResolveUrl(baseUrl, importRule.Url);
            if (url == null) continue;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var cssText = await client.GetStringAsync(url);
                var importedStylesheet = _cssParser.Parse(cssText);
                await ProcessImports(importedStylesheet, styleComputer, url);
                styleComputer.AddStylesheet(importedStylesheet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSS] Failed to process @import '{importRule.Url}': {ex}");
            }
        }
    }

    private static string? ResolveUrl(string? baseUrl, string href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        if (href.StartsWith("http://") || href.StartsWith("https://") || href.StartsWith("data:") || href.StartsWith("blob:"))
            return href;
        if (href.StartsWith("//"))
        {
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://"))
                return "https:" + href;
            return "http:" + href;
        }
        if (baseUrl != null && (baseUrl.StartsWith("http://") || baseUrl.StartsWith("https://")))
        {
            try
            {
                var baseUri = new Uri(baseUrl);
                var resolved = new Uri(baseUri, href);
                return resolved.ToString();
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static Element ConvertHtmlToDom(AngleSharp.Dom.IElement source, Document target)
    {
        var element = HtmlElementFactory.Create(source.LocalName ?? "unknown");
        if (element == null) return null!;
        foreach (var attr in source.Attributes)
        {
            if (!string.IsNullOrEmpty(attr.Name))
                element.Attributes[attr.Name] = attr.Value ?? "";
        }
        target.AppendChild(element);
        ConvertElementChildren(source, element);
        return element;
    }

    private static void ConvertElementChildren(AngleSharp.Dom.INode source, Element target)
    {
        foreach (var child in source.ChildNodes)
        {
            if (child is AngleSharp.Dom.IElement childElement)
            {
                var el = HtmlElementFactory.Create(childElement.LocalName ?? "unknown");
                if (el != null)
                {
                    foreach (var attr in childElement.Attributes)
                    {
                        if (!string.IsNullOrEmpty(attr.Name))
                            el.Attributes[attr.Name] = attr.Value ?? "";
                    }
                    target.AppendChild(el);
                    ConvertElementChildren(childElement, el);
                }
            }
            else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
            {
                target.AppendChild(new TextNode(child.TextContent ?? ""));
            }
        }
    }

    private static Element? ConvertHtmlToDomElement(AngleSharp.Dom.IElement source)
    {
        var element = HtmlElementFactory.Create(source.LocalName ?? "unknown");
        if (element == null) return null;
        foreach (var attr in source.Attributes)
        {
            if (!string.IsNullOrEmpty(attr.Name))
                element.Attributes[attr.Name] = attr.Value ?? "";
        }
        foreach (var child in source.ChildNodes)
        {
            if (child is AngleSharp.Dom.IElement childElement)
            {
                var childEl = ConvertHtmlToDomElement(childElement);
                if (childEl != null)
                    element.AppendChild(childEl);
            }
            else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
            {
                element.AppendChild(new TextNode(child.TextContent ?? ""));
            }
        }
        return element;
    }

    public record DocumentLoadResult(Document Document, StyleComputer? StyleComputer = null);
}

public class HtmlElement : Element
{
    public HtmlElement(string tagName) : base(tagName) { }

    // Global HTML attributes (HTMLElement spec)
    public string? Title
    {
        get => GetAttribute("title");
        set { if (value != null) SetAttribute("title", value); else RemoveAttribute("title"); }
    }
    public string? Lang
    {
        get => GetAttribute("lang");
        set { if (value != null) SetAttribute("lang", value); else RemoveAttribute("lang"); }
    }
    public bool Translate
    {
        get => !string.Equals(GetAttribute("translate"), "no", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("translate", value ? "yes" : "no");
    }
    public string? Dir
    {
        get => GetAttribute("dir");
        set { if (value != null) SetAttribute("dir", value); else RemoveAttribute("dir"); }
    }
    public bool Hidden
    {
        get => HasAttribute("hidden");
        set { if (value) SetAttribute("hidden", ""); else RemoveAttribute("hidden"); }
    }
    public bool Inert
    {
        get => HasAttribute("inert");
        set { if (value) SetAttribute("inert", ""); else RemoveAttribute("inert"); }
    }
    public bool Draggable
    {
        get => string.Equals(GetAttribute("draggable"), "true", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("draggable", value ? "true" : "false");
    }
    public bool Spellcheck
    {
        get => !string.Equals(GetAttribute("spellcheck"), "false", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("spellcheck", value ? "true" : "false");
    }
    public string? ContentEditable
    {
        get => GetAttribute("contenteditable") ?? "inherit";
        set { if (value != null) SetAttribute("contenteditable", value); else RemoveAttribute("contenteditable"); }
    }
    public bool IsContentEditable => ContentEditable == "true";
    public string? InputMode
    {
        get => GetAttribute("inputmode");
        set { if (value != null) SetAttribute("inputmode", value); else RemoveAttribute("inputmode"); }
    }
    public string? EnterKeyHint
    {
        get => GetAttribute("enterkeyhint");
        set { if (value != null) SetAttribute("enterkeyhint", value); else RemoveAttribute("enterkeyhint"); }
    }
    public string? Autocapitalize
    {
        get => GetAttribute("autocapitalize") ?? "";
        set { if (value != null) SetAttribute("autocapitalize", value); else RemoveAttribute("autocapitalize"); }
    }
    public string? Nonce
    {
        get => GetAttribute("nonce");
        set { if (value != null) SetAttribute("nonce", value); else RemoveAttribute("nonce"); }
    }
    public string? Popover
    {
        get => GetAttribute("popover");
        set { if (value != null) SetAttribute("popover", value); else RemoveAttribute("popover"); }
    }
    public int TabIndex
    {
        get => int.TryParse(GetAttribute("tabindex"), out var v) ? v : 0;
        set => SetAttribute("tabindex", value.ToString());
    }
    public string? AccessKey
    {
        get => GetAttribute("accesskey");
        set { if (value != null) SetAttribute("accesskey", value); else RemoveAttribute("accesskey"); }
    }
    public string? AccessKeyLabel => AccessKey;
    public DOMStringMap Dataset => new(x => GetAttribute(x), (x, v) => SetAttribute(x, v));
    public ElementInternals AttachInternals() => new(this);
}

