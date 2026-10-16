using DOMException = Acrux.Core.Dom.DOMException;

namespace Acrux.Core.JavaScript;

/// <summary>DOM §3.3.6: the tokens a page hands a DOMTokenList are checked before the list changes,
/// and each refusal has its own sentence. Measured against the reference: an empty token is refused as
/// a 'SyntaxError' naming the method, and a token that holds an HTML space character is refused as an
/// 'InvalidCharacterError' that quotes the token it was given.</summary>
internal static class DomTokens
{
    private static bool IsHtmlSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    public static void Require(string? token, string method)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new DOMException($"Failed to execute '{method}' on 'DOMTokenList': The token "
                                   + "provided must not be empty.", "SyntaxError");
        }
        if (token!.Any(IsHtmlSpace))
        {
            throw new DOMException($"Failed to execute '{method}' on 'DOMTokenList': The token "
                                   + $"provided ('{token}') contains HTML space characters, which are "
                                   + "not valid in tokens.", "InvalidCharacterError");
        }
    }
}
