namespace Acrux.Core.Dom;

public class NodeInternalState
{
    public Node? ParentNode { get; set; }
    public Node? FirstChild { get; set; }
    public Node? LastChild { get; set; }
    public Node? PreviousSibling { get; set; }
    public Node? NextSibling { get; set; }
    public Document? OwnerDocument { get; set; }
    public bool IsConnected { get; set; }
    public string? NamespaceUri { get; set; }
    public string? Prefix { get; set; }
    public string LocalName { get; set; } = string.Empty;
    public ShadowRoot? ShadowRoot { get; set; }
    public Dictionary<string, object?> Slots { get; } = new();
}

public class ElementInternalState : NodeInternalState
{
    public List<Attr> Attributes { get; } = new();
    public Dictionary<string, Attr> AttributeMap { get; } = new();
    public CustomElementState CustomElementState { get; set; } = CustomElementState.Undefined;
    public string? CustomElementDefinition { get; set; }
}

public enum CustomElementState
{
    Undefined,
    Defined,
    Failed
}
