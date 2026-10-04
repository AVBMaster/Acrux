namespace Acrux.Core.Dom;

public class DocumentTypeNode : Node
{
    public string? PublicId { get; }
    public string? SystemId { get; }

    public DocumentTypeNode(string? publicId = null, string? systemId = null) : base()
    {
        PublicId = publicId;
        SystemId = systemId;
        NodeName = "DOCTYPE";
    }

    public override NodeType NodeType => NodeType.DocumentType;

    public string Name => "html";

    protected override Node CloneNodeInternal(bool deep) => new DocumentTypeNode(PublicId, SystemId);
}
