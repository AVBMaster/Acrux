using DomDocument = Acrux.Core.Dom.Document;
using DomElement = Acrux.Core.Dom.Element;
using DomNode = Acrux.Core.Dom.Node;

namespace Acrux.Core.JavaScript;

/// <summary>
/// Custom Elements §1: the registry itself is a script-side object — a definition is a constructor,
/// and its observed attributes and its callbacks are script data. What belongs to the engine is the
/// set of moments a reaction is owed: an element made for a name the registry knows, a node put into
/// or taken out of the tree, an attribute written. This class is the line that names those moments to
/// the script, and the only thing it keeps on this side is the set of names that carry a definition,
/// so every hook costs a static bool and a hash lookup — a page with no custom elements pays nothing
/// for the plumbing, and a tree built by 'document.implementation' never runs a reaction because it
/// has no browsing context and so no registry to run one from.
/// </summary>
internal static class CustomElements
{
    private static readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>Whether the page holds any definition at all. Every entry point reads this first.</summary>
    internal static bool Active;

    internal static void Reset()
    {
        Active = false;
        _names.Clear();
    }

    /// <summary>The script's registry calls in for each name it defines, and the first call is what
    /// turns the hooks on.</summary>
    internal static void NoteDefinition(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        _names.Add(name);
        Active = true;
    }

    internal static bool IsDefined(string? localName)
        => localName != null && _names.Contains(localName);

    /// <summary>The document the page runs in — the only one with a registry behind it.</summary>
    private static DomDocument? PageDocument
        => JavaScriptEngine.Current?.IntegrationService?.Document?.NativeDocument;

    private static bool BelongsToPage(DomNode node)
    {
        var page = PageDocument;
        if (page == null) return false;
        return ReferenceEquals(node is DomDocument document ? document : node.OwnerDocument, page);
    }

    /// <summary>An element the engine made for a name the registry knows. Constructing it is the
    /// script's business — the constructor is a script function — so the element is handed over
    /// (§1: "create an element for a tag" consults the registry while the element is being made).</summary>
    internal static void Constructed(DomElement element)
    {
        if (!Active || !IsDefined(element.LocalName) || !BelongsToPage(element)) return;
        Fire("__ceConstructed", JsIntegrationService.WrapAny(element), element.LocalName);
    }

    /// <summary>A node that went into the tree. The script walks the subtree from here, because an
    /// insertion brings every descendant with it and each of those may be one of theirs (DOM §4.1
    /// insertion steps, HTML §1 "connected callback").</summary>
    internal static void Inserted(DomNode node, DomDocument? previousOwner)
    {
        if (!Active || !ContainsDefinition(node)) return;
        var wrapper = JsIntegrationService.WrapAny(node);
        if (wrapper == null) return;
        // A node that came from another document is adopted on the way in, between being taken out
        // of the tree it was in and being put into this one (§4.5, HTML §1).
        if (previousOwner != null && !ReferenceEquals(previousOwner, node.OwnerDocument))
            Fire("__ceAdopted", wrapper);
        Fire("__ceInserted", wrapper);
    }

    /// <summary>A node that came out of the tree — the disconnected callback it owes runs for the
    /// whole subtree it took with it.</summary>
    internal static void Removed(DomNode node)
    {
        // Not filtered by document: a node that was connected in the page's document and is being
        // moved into another one owes its disconnected callback on the way out (§1), and by the time
        // this is reached its owner document has already been retargeted.
        if (!Active || !ContainsDefinition(node)) return;
        var wrapper = JsIntegrationService.WrapAny(node);
        if (wrapper == null) return;
        Fire("__ceRemoved", wrapper);
    }

    /// <summary>An attribute written on an element the registry knows. Whether the name is one the
    /// class observes is script data, so the whole element is handed over with both values (§1:
    /// "attribute changed reaction").</summary>
    internal static void AttributeChanged(DomElement element, string name, string? oldValue,
        string? newValue, string? ns)
    {
        if (!Active || !IsDefined(element.LocalName) || !BelongsToPage(element)) return;
        Fire("__ceAttribute", JsIntegrationService.WrapAny(element), name, oldValue, newValue, ns);
    }

    /// <summary>Whether anything under this node has a definition — the walk that keeps a tree
    /// operation from crossing the boundary at all for the ordinary node.</summary>
    private static bool ContainsDefinition(DomNode node)
    {
        if (node is DomElement element && IsDefined(element.LocalName)) return true;
        foreach (var child in node.Children)
            if (ContainsDefinition(child)) return true;
        return false;
    }

    private static void Fire(string functionName, params object?[] args)
    {
        // A callback the page wrote is run as the reference runs it: an error inside it is reported
        // and the DOM operation that asked for it still finishes.
        JavaScriptEngine.Current?.IntegrationService?.CallGlobal(functionName, args);
    }
}
