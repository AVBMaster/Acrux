using DOMException = Acrux.Core.Dom.DOMException;

namespace Acrux.Core.JavaScript;

/// <summary>
/// The name rules the DOM puts on the names a page hands the document, with the sentences the
/// reference writes when a name breaks one. Measured against the reference: an attribute name may
/// not contain a character that would end one while markup is being read (the five whitespace
/// characters, '/', '=' and '>'), a tag name swaps '=' for the rule that a name may not start with
/// a digit, and both are allowed to start with a letter, '_', ':' or a non-ASCII letter. The XML
/// name production a processing instruction target is held to is stricter still: letters, digits,
/// '.', '-', '_' and ':' only, and a name may not begin with a digit, '-' or '.'.
/// </summary>
internal static class DomNames
{
    /// <summary>The whitespace an HTML attribute or tag name ends at. Vertical tab is deliberately
    /// not one of them: the reference takes 'a\x0Bb' as a name (measured).</summary>
    private static bool IsMarkupSpace(char c)
        => c is '\t' or '\n' or '\f' or '\r' or ' ';

    private static bool BadInTagName(char c)
        => c == '\0' || IsMarkupSpace(c) || c is '/' or '>';

    private static bool BadInAttributeName(char c)
        => c == '\0' || IsMarkupSpace(c) || c is '/' or '=' or '>';

    private static bool IsNameStart(char c)
        => char.IsLetter(c) || c is '_' or ':' || c >= '\u00B7';

    private static bool IsNameChar(char c)
        => IsNameStart(c) || char.IsDigit(c) || c is '.' or '-';

    /// <summary>DOM §4.3.6 'createAttribute': the local name, refused as a whole.</summary>
    internal static void RequireLocalName(string? name, string method, string owner)
    {
        if (name != null && name.Length > 0 && !name.Any(BadInAttributeName)) return;
        throw new DOMException($"Failed to execute '{method}' on '{owner}': The localName provided "
                               + $"('{name}') contains an invalid character.", "InvalidCharacterError");
    }

    /// <summary>DOM §4.3.2 'createElement': a tag name, refused as a whole.</summary>
    internal static void RequireTagName(string? name, string method, string owner)
    {
        if (name != null && name.Length > 0 && !char.IsDigit(name[0]) && !name.Any(BadInTagName)) return;
        throw new DOMException($"Failed to execute '{method}' on '{owner}': The tag name provided "
                               + $"('{name}') is not a valid name.", "InvalidCharacterError");
    }

    /// <summary>DOM §4.2.1 'setAttribute': the same name rule, told in the element's own sentence.</summary>
    internal static void RequireSettableAttributeName(string? name, string method, string owner)
    {
        if (name != null && name.Length > 0 && !name.Any(BadInAttributeName)) return;
        throw new DOMException($"Failed to execute '{method}' on '{owner}': '{name}' is not a valid "
                               + "attribute name.", "InvalidCharacterError");
    }

    /// <summary>DOM §4.3.1: a qualified name is checked as written — prefix, ':' and local name — and
    /// refused by the first character that is not allowed in it.</summary>
    internal static void RequireQualifiedName(string? name, string method, string owner, bool element)
    {
        if (string.IsNullOrEmpty(name))
            throw new DOMException($"Failed to execute '{method}' on '{owner}': The qualified name "
                                   + "provided is empty.", "InvalidCharacterError");
        var bad = BadInTagName;
        if (element)
        {
            if (char.IsDigit(name![0]))
                throw Invalid(name!, name[0], method, owner);
        }
        else
            bad = BadInAttributeName;
        foreach (var c in name!)
            if (bad(c))
                throw Invalid(name!, c, method, owner);
    }

    /// <summary>DOM §4.3.4 'createDocumentType': the doctype's name, in its own sentence.</summary>
    internal static void RequireDocumentTypeName(string? name, string method, string owner)
    {
        if (name != null && name.Length > 0 && !char.IsDigit(name[0]) && !name.Any(BadInTagName)) return;
        throw new DOMException($"Failed to execute '{method}' on '{owner}': The provided doctype name "
                               + $"('{name}') contains an invalid character.", "InvalidCharacterError");
    }

    /// <summary>DOM §4.3.5: a processing instruction's target is an XML name.</summary>
    internal static void RequireXmlName(string? name, string method, string owner)
    {
        if (name is { Length: > 0 } && IsNameStart(name[0]))
        {
            for (var i = 1; i < name.Length; i++)
                if (!IsNameChar(name[i]))
                    goto refused;
            return;
        }
    refused:
        throw new DOMException($"Failed to execute '{method}' on '{owner}': The target provided "
                               + $"('{name}') is not a valid name.", "InvalidCharacterError");
    }

    private static DOMException Invalid(string name, char c, string method, string owner)
        => new($"Failed to execute '{method}' on '{owner}': The qualified name provided ('{name}') "
               + $"contains the invalid character '{c}'.", "InvalidCharacterError");
}
