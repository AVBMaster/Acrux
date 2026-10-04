using Acrux.Core.Dom.Html;

namespace Acrux.Core.Dom.Parser;

internal class HtmlStackItem
{
    public enum ItemType { ForContextElement, ForDocumentFragmentNode }

    public Node Node { get; }
    public string Name { get; }
    public string NamespaceUri { get; }
    public bool IsDocumentFragmentNode { get; }
    public List<HtmlToken.Attribute> Attributes { get; }
    public HtmlStackItem? NextItemInStack { get; set; }

    public HtmlStackItem(Node node, ItemType type)
    {
        Node = node;
        switch (type)
        {
            case ItemType.ForDocumentFragmentNode:
                IsDocumentFragmentNode = true;
                Name = string.Empty;
                NamespaceUri = "http://www.w3.org/1999/xhtml";
                break;
            case ItemType.ForContextElement:
                Name = (node as Element)?.LocalName ?? string.Empty;
                NamespaceUri = (node as Element)?.NamespaceUri ?? "http://www.w3.org/1999/xhtml";
                IsDocumentFragmentNode = false;
                break;
        }
        Attributes = new List<HtmlToken.Attribute>();
    }

    public HtmlStackItem(Node node, AtomicHtmlToken token, string namespaceUri = "http://www.w3.org/1999/xhtml")
    {
        Node = node;
        Name = token.GetName();
        NamespaceUri = namespaceUri;
        IsDocumentFragmentNode = false;
        Attributes = new List<HtmlToken.Attribute>(token.Attributes);
    }

    public Element? GetElement() => Node as Element;
    public Node? GetNode() => Node;
    public bool IsElementNode => !IsDocumentFragmentNode;
    public string LocalName => Name;
    public string NamespaceURI => NamespaceUri;

    public bool IsInHtmlNamespace =>
        NamespaceUri == "http://www.w3.org/1999/xhtml" || IsDocumentFragmentNode;

    public bool IsHtmlNamespace =>
        NamespaceUri == "http://www.w3.org/1999/xhtml";

    public bool MatchesHtmlTag(string tagName)
    {
        return string.Equals(Name, tagName, StringComparison.OrdinalIgnoreCase) && IsHtmlNamespace;
    }

    public bool IsNumberedHeaderElement
    {
        get
        {
            if (NamespaceUri != "http://www.w3.org/1999/xhtml")
                return false;
            switch (Name.ToLowerInvariant())
            {
                case "h1": case "h2": case "h3":
                case "h4": case "h5": case "h6":
                    return true;
                default:
                    return false;
            }
        }
    }

    public bool CausesFosterParenting
    {
        get
        {
            if (NamespaceUri != "http://www.w3.org/1999/xhtml")
                return false;
            switch (Name.ToLowerInvariant())
            {
                case "table": case "tbody": case "tfoot": case "thead": case "tr":
                    return true;
                default:
                    return false;
            }
        }
    }

    public bool IsTableBodyContextElement
    {
        get
        {
            if (NamespaceUri != "http://www.w3.org/1999/xhtml")
                return false;
            switch (Name.ToLowerInvariant())
            {
                case "tbody": case "tfoot": case "thead":
                    return true;
                default:
                    return false;
            }
        }
    }

    public bool IsSpecialNode
    {
        get
        {
            if (IsDocumentFragmentNode)
                return true;
            if (IsInHtmlNamespace)
            {
                switch (Name.ToLowerInvariant())
                {
                    case "address": case "area": case "applet": case "article":
                    case "aside": case "base": case "basefont": case "bgsound":
                    case "blockquote": case "body": case "br": case "button":
                    case "caption": case "center": case "col": case "colgroup":
                    case "command": case "dd": case "details": case "dir":
                    case "div": case "dl": case "dt": case "embed":
                    case "fieldset": case "figcaption": case "figure": case "footer":
                    case "form": case "frame": case "frameset":
                    case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    case "head": case "header": case "hgroup": case "hr":
                    case "html": case "iframe": case "img": case "input":
                    case "li": case "link": case "listing": case "main":
                    case "marquee": case "menu": case "meta": case "nav":
                    case "noembed": case "noframes": case "noscript": case "object":
                    case "ol": case "p": case "param": case "plaintext":
                    case "pre": case "script": case "section": case "select":
                    case "style": case "summary": case "table":
                    case "tbody": case "td": case "template": case "textarea":
                    case "tfoot": case "th": case "thead": case "title": case "tr":
                    case "ul": case "wbr": case "xmp":
                        return true;
                    default:
                        return false;
                }
            }
            return false;
        }
    }
}