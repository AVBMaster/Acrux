namespace Acrux.Core.JavaScript;

/// <summary>DOM §4.2.1 with HTML §2.7.3: every node answers 'baseURI' with the base URL of its node
/// document, which is the 'href' of the first 'base' element that has one and the document URL when
/// the page declared no base. A node that belongs to no document — a detached element, a fragment
/// built by 'createDocumentFragment' — answers the empty string.</summary>
internal static class NodeBaseUri
{
    public static string For(Dom.Node? node)
    {
        if (node == null) return "";
        var document = node as Dom.Document ?? node.OwnerDocument
            // A node the fragment parser built below the directly-inserted child carries no owner
            // of its own (AppendChild stamps only that child), but it still lives in the running
            // document — the same fallback Node.ownerDocument reads through.
            ?? JavaScriptEngine.Current?.IntegrationService?.Document?.NativeDocument;
        return document?.BaseUrl ?? "";
    }
}
