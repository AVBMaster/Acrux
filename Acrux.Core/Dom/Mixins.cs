namespace Acrux.Core.Dom;

// ===== ParentNode Mixin =====
public interface IParentNode
{
    IReadOnlyList<Node> Children { get; }
    Element? FirstElementChild { get; }
    Element? LastElementChild { get; }
    int ChildElementCount { get; }
    Element? QuerySelector(string selectors);
    IReadOnlyList<Element> QuerySelectorAll(string selectors);
    void Prepend(params Node[] nodes);
    void Append(params Node[] nodes);
    void ReplaceChildren(params Node[] nodes);
}

public static class ParentNodeExtensions
{
    public static Element? GetFirstElementChild(this IParentNode node)
    {
        foreach (var child in node.Children)
            if (child is Element el) return el;
        return null;
    }

    public static Element? GetLastElementChild(this IParentNode node)
    {
        Element? last = null;
        foreach (var child in node.Children)
            if (child is Element el) last = el;
        return last;
    }

    public static int GetChildElementCount(this IParentNode node)
    {
        int count = 0;
        foreach (var child in node.Children)
            if (child is Element) count++;
        return count;
    }
}

// ===== ChildNode Mixin =====
public interface IChildNode
{
    void Before(params Node[] nodes);
    void After(params Node[] nodes);
    void ReplaceWith(params Node[] nodes);
    void Remove();
}

// ===== NonElementParentNode Mixin =====
public interface INonElementParentNode
{
    Element? GetElementById(string elementId);
}

// ===== Slottable Mixin =====
public interface ISlottable
{
    Element? AssignedSlot { get; }
}

// ===== InnerHTML Mixin =====
public interface IInnerHTML
{
    string InnerHTML { get; set; }
}

// ===== ElementContentEditable Mixin =====
public interface IElementContentEditable
{
    string? ContentEditable { get; set; }
    bool IsContentEditable { get; }
    string? InputMode { get; set; }
    string? EnterKeyHint { get; set; }
}
