using System.Text;
using Acrux.Core.Dom.Html;
using Node = Acrux.Core.Dom.Node;

namespace Acrux.Core.Dom.Parser;

internal class HtmlConstructionSite
{
    private readonly Document _document;
    private readonly Node _attachmentRoot;
    private readonly HtmlElementStack _openElements = new();
    private readonly HtmlFormattingElementList _activeFormattingElements = new();
    private HtmlStackItem? _head;
    private bool _isParsingFragment;
    private bool _redirectAttachToFosterParent;
    private bool _inQuirksMode;

    public HtmlConstructionSite(Document document, bool isFragment = false)
    {
        _document = document;
        _attachmentRoot = document;
        _isParsingFragment = isFragment;
        _inQuirksMode = false;
    }

    public HtmlElementStack OpenElements => _openElements;
    public HtmlFormattingElementList ActiveFormattingElements => _activeFormattingElements;
    public Element? CurrentElement => _openElements.Top;
    public Node? CurrentNode => _openElements.TopNode;
    public HtmlStackItem? CurrentStackItem => _openElements.TopStackItem;
    public Element? Head => _head?.GetElement();
    public HtmlStackItem? HeadStackItem => _head;
    public bool IsEmpty => _openElements.StackDepth == 0;
    public bool InQuirksMode => _inQuirksMode;

    public bool ShouldFosterParent => _redirectAttachToFosterParent;

    public void Flush() { }

    public void InsertDoctype(AtomicHtmlToken token)
    {
        string publicId = new string(token.PublicIdentifier.ToArray());
        string systemId = new string(token.SystemIdentifier.ToArray());
        var doctype = new DocumentTypeNode(
            string.IsNullOrEmpty(publicId) ? null : publicId,
            string.IsNullOrEmpty(systemId) ? null : systemId);
        _attachmentRoot.AppendChild(doctype);
        if (token.ForceQuirks)
            _inQuirksMode = true;
        else
            SetCompatibilityModeFromDoctype(token.GetName(), publicId, systemId);
    }

    public void InsertComment(AtomicHtmlToken token)
    {
        var comment = new CommentNode(token.Comment);
        CurrentNode?.AppendChild(comment);
    }

    public void InsertCommentOnDocument(AtomicHtmlToken token)
    {
        var comment = new CommentNode(token.Comment);
        _attachmentRoot.AppendChild(comment);
    }

    public void InsertCommentOnHtmlHtmlElement(AtomicHtmlToken token)
    {
        var comment = new CommentNode(token.Comment);
        _openElements.RootNode?.AppendChild(comment);
    }

    public void InsertHtmlHtmlStartTagBeforeHTML(AtomicHtmlToken token)
    {
        var element = CreateElement(token, "http://www.w3.org/1999/xhtml");
        SetAttributes(element, token);
        _attachmentRoot.AppendChild(element);
        _openElements.PushHtmlHtmlElement(new HtmlStackItem(element, token));
        _document.DocumentElement = element;
    }

    public void InsertHtmlHtmlStartTagInBody(AtomicHtmlToken token)
    {
        if (_isParsingFragment) return;
        var htmlElement = _openElements.HtmlElement;
        if (htmlElement != null)
            MergeAttributesFromTokenIntoElement(token, htmlElement);
    }

    public void InsertHtmlBodyStartTagInBody(AtomicHtmlToken token)
    {
        var bodyElement = _openElements.BodyElement;
        if (bodyElement != null)
            MergeAttributesFromTokenIntoElement(token, bodyElement);
    }

    public void InsertHtmlHeadElement(AtomicHtmlToken token)
    {
        var element = CreateElement(token, "http://www.w3.org/1999/xhtml");
        SetAttributes(element, token);
        CurrentNode?.AppendChild(element);
        _head = new HtmlStackItem(element, token);
        _openElements.PushHtmlHeadElement(_head);
        _document.Head = element;
    }

    public void InsertHtmlBodyElement(AtomicHtmlToken token)
    {
        var element = CreateElement(token, "http://www.w3.org/1999/xhtml");
        SetAttributes(element, token);
        CurrentNode?.AppendChild(element);
        _openElements.PushHtmlBodyElement(new HtmlStackItem(element, token));
        _document.Body = element;
    }

    public void InsertHtmlFormElement(AtomicHtmlToken token, bool isDemoted = false)
    {
        var element = CreateElement(token, "http://www.w3.org/1999/xhtml");
        SetAttributes(element, token);
        CurrentNode?.AppendChild(element);
        _openElements.Push(new HtmlStackItem(element, token));
    }

    public void InsertHtmlTemplateElement(AtomicHtmlToken token)
    {
        var element = CreateElement(token, "http://www.w3.org/1999/xhtml");
        SetAttributes(element, token);
        CurrentNode?.AppendChild(element);
        _openElements.Push(new HtmlStackItem(element, token));
    }

    public void InsertHtmlelement(AtomicHtmlToken token)
    {
        var element = CreateElement(token, "http://www.w3.org/1999/xhtml");
        SetAttributes(element, token);
        CurrentNode?.AppendChild(element);
        _openElements.Push(new HtmlStackItem(element, token));
    }

    public void InsertSelfClosingHtmlElementDestroyingToken(AtomicHtmlToken token)
    {
        var element = CreateElement(token, "http://www.w3.org/1999/xhtml");
        SetAttributes(element, token);
        CurrentNode?.AppendChild(element);
    }

    public void InsertFormattingElement(AtomicHtmlToken token)
    {
        InsertHtmlelement(token);
        _activeFormattingElements.Append(CurrentStackItem!);
    }

    public void InsertScriptElement(AtomicHtmlToken token)
    {
        InsertHtmlelement(token);
    }

    public void InsertForeignElement(AtomicHtmlToken token, string namespaceUri)
    {
        var element = CreateElement(token, namespaceUri);
        SetAttributes(element, token);
        CurrentNode?.AppendChild(element);
        if (!token.SelfClosing)
            _openElements.Push(new HtmlStackItem(element, token, namespaceUri));
    }

    public void InsertTextNode(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var textNode = new TextNode(text);
        CurrentNode?.AppendChild(textNode);
    }

    public void ReconstructTheActiveFormattingElements()
    {
        if (_activeFormattingElements.IsEmpty) return;
        var entry = _activeFormattingElements.Count - 1;
        if (_activeFormattingElements.Count > 0)
        {
            int last = _activeFormattingElements.Count - 1;
            if (!_activeFormattingElements[last].IsMarker)
            {
                var item = _activeFormattingElements[last].StackItem;
                if (item != null && _openElements.Contains(item.GetElement()!))
                    return;
            }
        }

        int firstUnopenElement = _activeFormattingElements.Count;
        for (int i = _activeFormattingElements.Count - 1; i >= 0; i--)
        {
            if (_activeFormattingElements[i].IsMarker) break;
            var item = _activeFormattingElements[i].StackItem;
            if (item != null && _openElements.Contains(item.GetElement()!))
            {
                firstUnopenElement = i + 1;
                break;
            }
        }

        if (firstUnopenElement == _activeFormattingElements.Count) return;

        for (int i = firstUnopenElement; i < _activeFormattingElements.Count; i++)
        {
            var item = _activeFormattingElements[i].StackItem;
            if (item != null)
            {
                var newElement = HtmlElementFactory.Create(item.Name);
                foreach (var attr in item.Attributes)
                    newElement.SetAttribute(attr.GetName(), attr.GetValue());
                CurrentNode?.AppendChild(newElement);
                var newStackItem = new HtmlStackItem(newElement, new AtomicHtmlToken(HtmlToken.TokenType.StartTag, item.Name));
                _openElements.Push(newStackItem);
                _activeFormattingElements.SetEntry(i, new HtmlFormattingElementList.Entry { StackItem = newStackItem });
            }
        }
    }

    public void GenerateImpliedEndTags()
    {
        while (_openElements.TopStackItem != null)
        {
            string tag = _openElements.TopStackItem.Name.ToLowerInvariant();
            if (tag == "dd" || tag == "dt" || tag == "li" || tag == "option" ||
                tag == "optgroup" || tag == "p" || tag == "rb" || tag == "rp" ||
                tag == "rt" || tag == "rtc")
                _openElements.Pop();
            else
                break;
        }
    }

    public void GenerateImpliedEndTagsWithExclusion(string exclusion)
    {
        while (_openElements.TopStackItem != null)
        {
            string tag = _openElements.TopStackItem.Name.ToLowerInvariant();
            if (tag == exclusion) break;
            if (tag == "dd" || tag == "dt" || tag == "li" || tag == "option" ||
                tag == "optgroup" || tag == "p" || tag == "rb" || tag == "rp" ||
                tag == "rt" || tag == "rtc")
                _openElements.Pop();
            else
                break;
        }
    }

    public void ProcessEndOfFile()
    {
        Flush();
        _openElements.PopAll();
    }

    public void FinishedParsing()
    {
        Flush();
    }

    private static Element CreateElement(AtomicHtmlToken token, string namespaceUri)
    {
        var tagName = token.GetName();
        if (namespaceUri == "http://www.w3.org/1999/xhtml")
            return HtmlElementFactory.Create(tagName);
        return new HtmlElement(tagName) { NamespaceUri = namespaceUri };
    }

    private static void SetAttributes(Element element, AtomicHtmlToken token)
    {
        foreach (var attr in token.Attributes)
            element.SetAttribute(attr.GetName(), attr.GetValue());
    }

    private static void MergeAttributesFromTokenIntoElement(AtomicHtmlToken token, Element element)
    {
        foreach (var attr in token.Attributes)
        {
            if (!element.HasAttribute(attr.GetName()))
                element.SetAttribute(attr.GetName(), attr.GetValue());
        }
    }

    private void SetCompatibilityModeFromDoctype(string name, string publicId, string systemId)
    {
        if (name != "html")
        {
            _inQuirksMode = true;
            return;
        }
        if (publicId.Contains("+//Silmaril//") || publicId.Contains("-//W3C//DTD HTML 4.01 Frameset//") ||
            publicId.Contains("-//W3C//DTD HTML 4.01 Transitional//"))
        {
            if (string.IsNullOrEmpty(systemId))
            {
                _inQuirksMode = true;
                return;
            }
        }
        if (publicId.Contains("-//W3C//DTD XHTML 1.0 Frameset//") ||
            publicId.Contains("-//W3C//DTD XHTML 1.0 Transitional//"))
        {
            _inQuirksMode = false;
            return;
        }
        _inQuirksMode = false;
    }
}