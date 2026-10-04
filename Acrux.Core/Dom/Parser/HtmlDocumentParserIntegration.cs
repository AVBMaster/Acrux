namespace Acrux.Core.Dom.Parser;

public static class HtmlDocumentParserIntegration
{
    public static Document ParseHtml(string html)
    {
        var document = new Document();
        if (string.IsNullOrEmpty(html))
            return document;

        // Strip BOM (U+FEFF) which can appear at the start of UTF-8 encoded content.
        if (html.Length > 0 && html[0] == '\uFEFF')
            html = html.Substring(1);

        if (string.IsNullOrEmpty(html))
            return document;

        var parser = new HtmlDocumentParser(document);
        parser.Append(html);
        parser.Finish();

        // Set DocumentElement, Head, Body from the parsed tree.
        foreach (var child in document.Children)
        {
            if (child is Element el)
            {
                if (el.TagName == "HTML" && document.DocumentElement == null)
                    document.DocumentElement = el;
                if (el.TagName == "HEAD" && document.Head == null)
                    document.Head = el;
                if (el.TagName == "BODY" && document.Body == null)
                    document.Body = el;
            }
        }

        // Extract title from the <title> element.
        if (document.Head != null)
        {
            foreach (var child in document.Head.Children)
            {
                if (child is Element titleEl && titleEl.TagName == "TITLE")
                {
                    document.Title = titleEl.TextContent ?? "";
                    break;
                }
            }
        }

        // If DocumentElement wasn't found among direct children, search recursively.
        if (document.DocumentElement == null)
        {
            var queue = new Queue<Element>();
            foreach (var child in document.Children)
                if (child is Element c)
                    queue.Enqueue(c);
            while (queue.Count > 0)
            {
                var el = queue.Dequeue();
                if (el.TagName == "HTML")
                {
                    document.DocumentElement = el;
                    break;
                }
                foreach (var child in el.Children)
                    if (child is Element c)
                        queue.Enqueue(c);
            }
        }

        return document;
    }

    /// <summary>Debug: parse HTML and return the DOM structure as a string tree.</summary>
    public static string ParseHtmlDebug(string html)
    {
        var doc = ParseHtml(html);
        return 
            DumpNode(doc, 0);
    }

    private static string DumpNode(Node node, int indent)
    {
        var sb = new System.Text.StringBuilder();
        var prefix = new string(' ', indent * 2);
        if (node is Element el)
        {
            var attrs = string.Join(" ", el.Attributes.Select(a => $"{a.Key}='{a.Value}'"));
            sb.AppendLine($"{prefix}<{el.TagName}{(attrs.Length > 0 ? " " + attrs : "")}>");
            foreach (var child in el.Children)
                sb.Append(DumpNode(child, indent + 1));
            sb.AppendLine($"{prefix}</{el.TagName}>");
        }
        else if (node is TextNode text)
        {
            sb.AppendLine($"{prefix}\"{text.TextContent}\"");
        }
        else if (node is Document doc)
        {
            sb.AppendLine($"{prefix}#document");
            foreach (var child in doc.Children)
                sb.Append(DumpNode(child, indent + 1));
        }
        else
        {
            sb.AppendLine($"{prefix}{node.NodeType}");
        }
        return sb.ToString();
    }
}