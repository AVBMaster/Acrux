namespace Acrux.Core.Dom;

public class DomImplementation
{
    public bool HasFeature(string feature, string? version = null) => true;

    public DocumentTypeNode CreateDocumentType(string qualifiedName, string publicId, string systemId)
    {
        return new DocumentTypeNode(string.IsNullOrEmpty(publicId) ? null : publicId,
            string.IsNullOrEmpty(systemId) ? null : systemId, qualifiedName);
    }

    public Document CreateDocument(string? ns, string qualifiedName, DocumentTypeNode? doctype = null)
    {
        var doc = new Document();
        // The doctype the caller hands over goes on before the root element, where the reference
        // puts it; 'createDocument(null, null, dt)' still gives a document with just the doctype.
        if (doctype != null) doc.AppendChild(doctype);
        if (!string.IsNullOrEmpty(qualifiedName))
        {
            var root = doc.CreateElementNS(ns, qualifiedName);
            doc.AppendChild(root);
            doc.DocumentElement = root;
        }
        return doc;
    }
}
