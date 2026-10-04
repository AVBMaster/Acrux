namespace Acrux.Core.Dom;

public class MutationRecord
{
    public string Type { get; }
    public Node Target { get; }
    public NodeList AddedNodes { get; }
    public NodeList RemovedNodes { get; }
    public Node? PreviousSibling { get; }
    public Node? NextSibling { get; }
    public string? AttributeName { get; }
    public string? AttributeNamespace { get; }
    public string? OldValue { get; }

    internal MutationRecord(
        string type,
        Node target,
        NodeList? addedNodes = null,
        NodeList? removedNodes = null,
        Node? previousSibling = null,
        Node? nextSibling = null,
        string? attributeName = null,
        string? attributeNamespace = null,
        string? oldValue = null)
    {
        Type = type;
        Target = target;
        AddedNodes = addedNodes ?? NodeList.Empty;
        RemovedNodes = removedNodes ?? NodeList.Empty;
        PreviousSibling = previousSibling;
        NextSibling = nextSibling;
        AttributeName = attributeName;
        AttributeNamespace = attributeNamespace;
        OldValue = oldValue;
    }
}
