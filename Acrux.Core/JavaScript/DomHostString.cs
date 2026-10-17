namespace Acrux.Core.JavaScript;

/// <summary>
/// The string a host object answers when the page turns it into one. A DOM object has no string of its
/// own, so the reference prints the interface it is an instance of — and the chain every host declares
/// ends with exactly that name, which is why one helper serves them all.
/// <para>
/// A host whose interface does carry a string — a Range and a Selection answer their content, a URL
/// answers its href, a DOMTokenList answers its tokens — keeps its own override of 'ToString' and does
/// not come here.</para>
/// </summary>
internal static class DomHostString
{
    public static string Tagged(object host)
    {
        var chain = host is IDomPrototypeHost declared ? declared.__domTypeChain : null;
        var name = chain is { Length: > 0 } ? chain![chain!.Length - 1] : null;
        return "[object " + (string.IsNullOrEmpty(name) ? host.GetType().Name : name) + "]";
    }

    /// <summary>The tag for a host that declares its interface name as a plain string instead of a
    /// chain — the collections, which answer the interface the API that made them belongs to.</summary>
    public static string Tagged(string interfaceName) => "[object " + interfaceName + "]";
}
