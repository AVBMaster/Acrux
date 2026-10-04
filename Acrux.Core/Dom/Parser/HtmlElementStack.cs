namespace Acrux.Core.Dom.Parser;

internal class HtmlElementStack
{
    private HtmlStackItem? _top;
    private Node? _rootNode;
    private Element? _headElement;
    private Element? _bodyElement;
    private int _stackDepth;

    public int StackDepth => _stackDepth;
    public Element? Top => _top?.GetElement();
    public Node? TopNode => _top?.GetNode();
    public HtmlStackItem? TopStackItem => _top;
    public HtmlStackItem? OneBelowTop
    {
        get
        {
            if (_top?.NextItemInStack == null) return null;
            return _top.NextItemInStack.IsElementNode ? _top.NextItemInStack : null;
        }
    }

    public Element? HtmlElement => _rootNode as Element;
    public Element? HeadElement => _headElement;
    public Element? BodyElement => _bodyElement;
    public Node? RootNode => _rootNode;

    public bool HasOnlyOneElement => _top?.NextItemInStack == null;
    public bool SecondElementIsHtmlBodyElement => _bodyElement != null;

    public void PushRootNode(HtmlStackItem item)
    {
        _rootNode = item.GetNode();
        PushCommon(item);
    }

    public void PushHtmlHtmlElement(HtmlStackItem item)
    {
        _rootNode = item.GetNode();
        PushCommon(item);
    }

    public void PushHtmlHeadElement(HtmlStackItem item)
    {
        _headElement = item.GetElement();
        PushCommon(item);
    }

    public void PushHtmlBodyElement(HtmlStackItem item)
    {
        _bodyElement = item.GetElement();
        PushCommon(item);
    }

    public void Push(HtmlStackItem item)
    {
        PushCommon(item);
    }

    private void PushCommon(HtmlStackItem item)
    {
        _stackDepth++;
        item.NextItemInStack = _top;
        _top = item;
    }

    public void Pop()
    {
        if (_top == null) return;
        _top = _top.NextItemInStack;
        _stackDepth--;
    }

    public void PopHtmlHeadElement()
    {
        _headElement = null;
        Pop();
    }

    public void PopHtmlBodyElement()
    {
        _bodyElement = null;
        Pop();
    }

    public void PopAll()
    {
        _rootNode = null;
        _headElement = null;
        _bodyElement = null;
        _stackDepth = 0;
        _top = null;
    }

    public void PopUntil(string tagName)
    {
        while (_top != null && !string.Equals(_top.Name, tagName, StringComparison.OrdinalIgnoreCase))
            Pop();
    }

    public void PopUntilPopped(string tagName)
    {
        PopUntil(tagName);
        Pop();
    }

    public void PopUntilTableScopeMarker()
    {
        while (_top != null && !IsTableScopeMarker(_top))
            Pop();
    }

    public void PopUntilTableBodyScopeMarker()
    {
        while (_top != null && !IsTableBodyScopeMarker(_top))
            Pop();
    }

    public void PopUntilTableRowScopeMarker()
    {
        while (_top != null && !IsTableRowScopeMarker(_top))
            Pop();
    }

    public void Remove(Element element)
    {
        if (_top?.GetElement() == element)
        {
            Pop();
            return;
        }
        RemoveNonTopCommon(element);
    }

    public void RemoveHtmlHeadElement(Element element)
    {
        _headElement = null;
        if (_top?.GetElement() == element)
        {
            PopHtmlHeadElement();
            return;
        }
        RemoveNonTopCommon(element);
    }

    private void RemoveNonTopCommon(Element element)
    {
        for (HtmlStackItem? item = _top; item?.NextItemInStack != null; item = item.NextItemInStack)
        {
            if (item.NextItemInStack.GetElement() == element)
            {
                item.NextItemInStack = item.NextItemInStack.NextItemInStack;
                _stackDepth--;
                return;
            }
        }
    }

    public bool Contains(Element element)
    {
        return Find(element) != null;
    }

    public HtmlStackItem? Find(Element element)
    {
        for (HtmlStackItem? item = _top; item != null; item = item.NextItemInStack)
        {
            if (item.GetElement() == element)
                return item;
        }
        return null;
    }

    public bool InScope(string tagName)
    {
        for (HtmlStackItem? item = _top; item != null; item = item.NextItemInStack)
        {
            if (string.Equals(item.Name, tagName, StringComparison.OrdinalIgnoreCase) && item.IsHtmlNamespace)
                return true;
            if (IsScopeMarker(item))
                return false;
        }
        return false;
    }

    public bool InButtonScope(string tagName)
    {
        for (HtmlStackItem? item = _top; item != null; item = item.NextItemInStack)
        {
            if (string.Equals(item.Name, tagName, StringComparison.OrdinalIgnoreCase) && item.IsHtmlNamespace)
                return true;
            if (IsButtonScopeMarker(item))
                return false;
        }
        return false;
    }

    public bool InTableScope(string tagName)
    {
        for (HtmlStackItem? item = _top; item != null; item = item.NextItemInStack)
        {
            if (string.Equals(item.Name, tagName, StringComparison.OrdinalIgnoreCase) && item.IsHtmlNamespace)
                return true;
            if (IsTableScopeMarker(item))
                return false;
        }
        return false;
    }

    public bool InListItemScope(string tagName)
    {
        for (HtmlStackItem? item = _top; item != null; item = item.NextItemInStack)
        {
            if (string.Equals(item.Name, tagName, StringComparison.OrdinalIgnoreCase) && item.IsHtmlNamespace)
                return true;
            if (IsListItemScopeMarker(item))
                return false;
        }
        return false;
    }

    public bool HasTemplateInHtmlScope => InScope("template");

    public bool HasNumberedHeaderElementInScope
    {
        get
        {
            for (HtmlStackItem? item = _top; item != null; item = item.NextItemInStack)
            {
                if (item.IsNumberedHeaderElement)
                    return true;
                if (IsScopeMarker(item))
                    return false;
            }
            return false;
        }
    }

    public HtmlStackItem? FurthestBlockForFormattingElement(Element formattingElement)
    {
        HtmlStackItem? furthestBlock = null;
        for (HtmlStackItem? item = _top; item != null; item = item.NextItemInStack)
        {
            if (item.GetElement() == formattingElement)
                return furthestBlock;
            if (item.IsSpecialNode)
                furthestBlock = item;
        }
        return null;
    }

    public void Replace(HtmlStackItem oldItem, HtmlStackItem newItem)
    {
        HtmlStackItem? previous = null;
        for (HtmlStackItem? item = _top; item != null; item = item.NextItemInStack)
        {
            if (item == oldItem)
            {
                if (previous != null)
                    previous.NextItemInStack = newItem;
                newItem.NextItemInStack = oldItem.NextItemInStack;
                return;
            }
            previous = item;
        }
    }

    public void InsertAbove(HtmlStackItem item, HtmlStackItem itemBelow)
    {
        if (itemBelow == _top)
        {
            Push(item);
            return;
        }
        for (HtmlStackItem? above = _top; above != null; above = above.NextItemInStack)
        {
            if (above.NextItemInStack == itemBelow)
            {
                _stackDepth++;
                item.NextItemInStack = above.NextItemInStack;
                above.NextItemInStack = item;
                return;
            }
        }
    }

    private static bool IsScopeMarker(HtmlStackItem item)
    {
        if (item.IsDocumentFragmentNode) return true;
        if (!item.IsHtmlNamespace) return false;
        switch (item.Name.ToLowerInvariant())
        {
            case "caption": case "applet": case "html": case "marquee":
            case "object": case "table": case "td": case "template": case "th":
                return true;
            default:
                return false;
        }
    }

    private static bool IsListItemScopeMarker(HtmlStackItem item)
    {
        if (item.IsDocumentFragmentNode) return true;
        if (!item.IsHtmlNamespace) return false;
        switch (item.Name.ToLowerInvariant())
        {
            case "caption": case "applet": case "html": case "marquee":
            case "object": case "table": case "td": case "template": case "th":
            case "ol": case "ul":
                return true;
            default:
                return false;
        }
    }

    private static bool IsTableScopeMarker(HtmlStackItem item)
    {
        if (item.IsDocumentFragmentNode) return true;
        if (!item.IsHtmlNamespace) return false;
        switch (item.Name.ToLowerInvariant())
        {
            case "html": case "table": case "template":
                return true;
            default:
                return false;
        }
    }

    private static bool IsTableBodyScopeMarker(HtmlStackItem item)
    {
        if (item.IsDocumentFragmentNode) return true;
        if (!item.IsHtmlNamespace) return false;
        switch (item.Name.ToLowerInvariant())
        {
            case "html": case "tbody": case "tfoot": case "thead": case "template":
                return true;
            default:
                return false;
        }
    }

    private static bool IsTableRowScopeMarker(HtmlStackItem item)
    {
        if (item.IsDocumentFragmentNode) return true;
        if (!item.IsHtmlNamespace) return false;
        switch (item.Name.ToLowerInvariant())
        {
            case "html": case "tr": case "template":
                return true;
            default:
                return false;
        }
    }

    private static bool IsButtonScopeMarker(HtmlStackItem item)
    {
        if (item.IsDocumentFragmentNode) return true;
        if (!item.IsHtmlNamespace) return false;
        switch (item.Name.ToLowerInvariant())
        {
            case "caption": case "applet": case "html": case "marquee":
            case "object": case "table": case "td": case "template": case "th":
            case "button":
                return true;
            default:
                return false;
        }
    }
}