using Acrux.Core.Dom;

namespace Acrux.Core.Dom.Parser;

public class DOMParser
{
    public Document ParseFromString(string text, string type)
    {
        var doc = new Document();
        if (string.IsNullOrEmpty(text)) return doc;
        return doc;
    }

    public Document ParseFromStringSync(string text, string type)
    {
        return ParseFromString(text, type);
    }
}

public class XMLSerializer
{
    public string SerializeToString(Node root)
    {
        return SerializeNode(root);
    }

    private static string SerializeNode(Node node)
    {
        if (node is TextNode text)
            return EscapeXml(text.TextContent ?? "");
        if (node is CommentNode comment)
            return $"<!--{EscapeXml(comment.TextContent ?? "")}-->";
        if (node is Document doc)
            return SerializeChildren(doc);
        if (node is DocumentFragment frag)
            return SerializeChildren(frag);
        if (node is Element el)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('<').Append(el.NodeName);
            foreach (var attr in el.Attributes)
                sb.Append(' ').Append(attr.Key).Append("=\"").Append(EscapeXml(attr.Value ?? "")).Append('"');
            if (el.ChildNodes.Count == 0)
                sb.Append(" />");
            else
            {
                sb.Append('>');
                sb.Append(SerializeChildren(el));
                sb.Append("</").Append(el.NodeName).Append('>');
            }
            return sb.ToString();
        }
        return "";
    }

    private static string SerializeChildren(Node node)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in node.ChildNodes)
            sb.Append(SerializeNode(child));
        return sb.ToString();
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

internal class AngleSharpDomConverter
{
    public HtmlElement? ConvertElement(AngleSharp.Dom.IElement angleElement)
    {
        if (angleElement == null) return null;
        var element = new HtmlElement(angleElement.LocalName ?? "unknown");
        foreach (var attr in angleElement.Attributes)
        {
            if (!string.IsNullOrEmpty(attr.Name))
                element.Attributes[attr.Name] = attr.Value ?? "";
        }
        foreach (var child in angleElement.ChildNodes)
        {
            if (child is AngleSharp.Dom.IElement childElement)
            {
                var converted = ConvertElement(childElement);
                if (converted != null) element.AppendChild(converted);
            }
            else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
            {
                element.AppendChild(new TextNode(child.TextContent ?? ""));
            }
        }
        return element;
    }
}

public static class StructuredClone
{
    public static object? Clone(object? value, StructuredCloneOptions? options = null)
    {
        return value switch
        {
            null => null,
            string s => s,
            int i => i,
            double d => d,
            bool b => b,
            Array arr => arr.Cast<object>().ToArray(),
            _ => value
        };
    }
}

public class StructuredCloneOptions
{
    public object[]? Transfer { get; set; }
}
