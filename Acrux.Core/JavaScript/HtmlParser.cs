using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;
using AngleSharp;

namespace Acrux.Core.JavaScript;

/// <summary>Parses a markup string into engine nodes for the DOM-mutating JS APIs
/// ('innerHTML', 'insertAdjacentHTML', 'DOMParser').</summary>
public static class HtmlParser
{
    public static List<Node> ParseFragment(string html, string parentTagName)
    {
        var result = new List<Node>();
        if (string.IsNullOrWhiteSpace(html)) return result;

        try
        {
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var doc = context.OpenAsync(req => req.Content($"<html><body>{html}</body></html>"))
                .GetAwaiter().GetResult();
            if (doc.Body == null) return result;

            foreach (var child in doc.Body.ChildNodes)
            {
                var converted = Convert(child);
                if (converted != null)
                    result.Add(converted);
            }
        }
        catch
        {
            result.Add(new TextNode(html));
        }

        return result;
    }

    private static Node? Convert(AngleSharp.Dom.INode node)
    {
        if (node is AngleSharp.Dom.IText textNode)
            return new TextNode(textNode.TextContent ?? "");
        if (node is not AngleSharp.Dom.IElement element)
            return null;

        // The same factory the document parser uses, so a fragment gets the element
        // class its tag names for.
        var el = HtmlElementFactory.Create(element.LocalName);
        foreach (var attr in element.Attributes)
            el.Attributes[attr.Name] = attr.Value;
        // A template's markup is not its children: the parser keeps it in the element's content
        // fragment, and AngleSharp has already put it there — copying 'ChildNodes' would drop it
        // (HTML §13.2.6.5, §4.12.3).
        var source = element is AngleSharp.Html.Dom.IHtmlTemplateElement templateElement
            ? templateElement.Content.ChildNodes
            : element.ChildNodes;
        var into = element is AngleSharp.Html.Dom.IHtmlTemplateElement && el is Acrux.Core.Dom.Html.HTMLTemplateElement contented
            ? (Node)contented.Content
            : el;
        // AppendChild rather than the child list: the parent link is part of the
        // tree, and numbering, style inheritance and 'closest()' all walk it.
        foreach (var child in source)
        {
            var converted = Convert(child);
            if (converted != null)
                into.AppendChild(converted);
        }
        return el;
    }
}
