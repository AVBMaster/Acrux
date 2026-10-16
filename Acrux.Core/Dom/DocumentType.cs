namespace Acrux.Core.Dom;

public class DocumentTypeNode : Node
{
    public string? PublicId { get; }
    public string? SystemId { get; }

    public DocumentTypeNode(string? publicId = null, string? systemId = null, string? name = null) : base()
    {
        PublicId = publicId;
        SystemId = systemId;
        Name = name ?? "html";
        // DOM §4.3.4: a doctype's nodeName is its name, not a fixed label.
        NodeName = Name;
    }

    public override NodeType NodeType => NodeType.DocumentType;

    public string Name { get; }

    protected override Node CloneNodeInternal(bool deep) => new DocumentTypeNode(PublicId, SystemId, Name);
}
