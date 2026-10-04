using Acrux.Core.Css.Matcher;
using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// Static entry point for matching a CSS selector string against a DOM element.
/// Uses the token-based <see cref="CssSelectorParser"/> and <see cref="SelectorChecker"/>
/// so all pseudo-classes, combinators and attribute operators are supported.
/// Results are cached by selector text.
/// </summary>
public static class CssSelectorMatcher
{
    private static readonly Dictionary<string, List<CssSelector>> Cache = new(StringComparer.Ordinal);

    /// <summary>Parses a selector list, caching the parsed representation.</summary>
    public static List<CssSelector> Parse(string selectorText)
    {
        if (!Cache.TryGetValue(selectorText, out var parsed))
        {
            parsed = CssSelectorParser.ParseSelectorList(selectorText);
            Cache[selectorText] = parsed;
        }
        return parsed;
    }

    /// <summary>True if the element matches any selector in the list.</summary>
    public static bool Matches(string selectorText, Element element)
    {
        var parsed = Parse(selectorText);
        var checker = new SelectorChecker();
        foreach (var selector in parsed)
        {
            if (checker.Match(selector, element))
                return true;
        }
        return false;
    }

    public static void ClearCache() => Cache.Clear();
}
