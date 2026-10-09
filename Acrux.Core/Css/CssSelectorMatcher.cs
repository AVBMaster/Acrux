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

    /// <summary>The descendants of |root| that match |selectorText|, in document order — the walk
    /// every querySelectorAll in the engine is built on. It lives here so that the DOM, the element
    /// host and the document host cannot answer one query three ways: a selector that begins with a
    /// type name ('a.k', 'b:nth-of-type(1)') used to match under document.querySelectorAll and not
    /// under an element's, because two of those walks carried their own idea of what a selector is.</summary>
    public static void CollectDescendants(Element root, string selectorText, List<Element> result)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            if (Matches(selectorText, child)) result.Add(child);
            CollectDescendants(child, selectorText, result);
        }
    }

    /// <summary>The first descendant of |root| that matches |selectorText|, in document order.</summary>
    public static Element? FirstDescendant(Element root, string selectorText)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            if (Matches(selectorText, child)) return child;
            var found = FirstDescendant(child, selectorText);
            if (found != null) return found;
        }
        return null;
    }

    public static void ClearCache() => Cache.Clear();
}
