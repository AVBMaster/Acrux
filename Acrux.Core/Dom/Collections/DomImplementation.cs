namespace Acrux.Core.Dom;

public class DomImplementation
{
    public bool HasFeature(string feature, string? version = null) => true;

    public DocumentTypeNode CreateDocumentType(string qualifiedName, string publicId, string systemId)
    {
        return new DocumentTypeNode(publicId, systemId);
    }

    public Document CreateDocument(string? ns, string qualifiedName, DocumentTypeNode? doctype = null)
    {
        var doc = new Document();
        if (!string.IsNullOrEmpty(qualifiedName))
        {
            var root = doc.CreateElementNS(ns, qualifiedName);
            doc.AppendChild(root);
            doc.DocumentElement = root;
        }
        return doc;
    }
}
