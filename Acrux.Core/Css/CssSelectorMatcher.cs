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

    /// <summary>True if the element matches any selector in the list. |scope| is what a ':scope'
    /// inside the list is measured against; left out, ':scope' has no anchor and matches nothing.</summary>
    public static bool Matches(string selectorText, Element element, Element? scope = null)
    {
        var parsed = Parse(selectorText);
        if (scope == null) return MatchesParsed(parsed, element);
        return SelectorChecker.WithScope(scope, () => MatchesParsed(parsed, element));
    }

    private static bool MatchesParsed(List<CssSelector> parsed, Element element)
    {
        var checker = new SelectorChecker();
        foreach (var selector in parsed)
        {
            if (checker.Match(selector, element))
                return true;
        }
        return false;
    }

    /// <summary>Parse a selector list for a DOM query, refusing a text the grammar does not read.
    /// 'no element matches' and 'there is no selector here' are different answers, and the reference
    /// engine keeps them apart: querySelector, querySelectorAll, matches and closest each throw a
    /// SyntaxError for the second (measured for 'p:bogus', 'p:nth-child(3 n)' and a blank text).
    /// The sentence names the call that failed — the method and the interface it was asked on — and
    /// then the selector (measured for all six: "Failed to execute 'querySelectorAll' on 'Document':
    /// 'p:bogus' is not a valid selector."). A named script error, so the page catches
    /// 'e.name == "SyntaxError"' and reads the sentence in 'e.message'; the name is not repeated
    /// inside it.</summary>
    public static List<CssSelector> ParseForQuery(string selectorText, string method, string owner)
    {
        var parsed = Parse(selectorText ?? string.Empty);
        if (parsed.Count == 0)
            // A text that is empty has its own sentence, while a text that is only spaces is quoted
            // back as the invalid selector it is (both measured).
            throw new Acrux.Core.Dom.DOMException(
                "Failed to execute '" + method + "' on '" + owner + "': "
                + ((selectorText ?? string.Empty).Length == 0
                    ? "The provided selector is empty."
                    : "'" + selectorText + "' is not a valid selector."), "SyntaxError");
        return parsed;
    }

    /// <summary>The descendants of |root| that match |selectorText|, in document order — the walk
    /// every querySelectorAll in the engine is built on. It lives here so that the DOM, the element
    /// host and the document host cannot answer one query three ways: a selector that begins with a
    /// type name ('a.k', 'b:nth-of-type(1)') used to match under document.querySelectorAll and not
    /// under an element's, because two of those walks carried their own idea of what a selector is.</summary>
    public static void CollectDescendants(Element root, string selectorText, List<Element> result,
        Element? scope = null, bool includeRoot = false)
    {
        SelectorChecker.WithScope(scope ?? root, () =>
        {
            // A query from the document takes the root element itself: 'document.querySelectorAll("html")'
            // answers with it, while the same query from an element never includes that element
            // (measured — and ':scope' alone then finds nothing, because the anchor is not one of its
            // own descendants).
            if (includeRoot && Matches(selectorText, root)) result.Add(root);
            CollectWithin(root, selectorText, result);
            return true;
        });
    }

    private static void CollectWithin(Element root, string selectorText, List<Element> result)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            if (Matches(selectorText, child)) result.Add(child);
            CollectWithin(child, selectorText, result);
        }
    }

    /// <summary>The first element in |root|'s query list that matches |selectorText|.</summary>
    public static Element? FirstDescendant(Element root, string selectorText,
        Element? scope = null, bool includeRoot = false) =>
        SelectorChecker.WithScope(scope ?? root, () =>
            includeRoot && Matches(selectorText, root) ? root : FirstWithin(root, selectorText));

    private static Element? FirstWithin(Element root, string selectorText)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            if (Matches(selectorText, child)) return child;
            var found = FirstWithin(child, selectorText);
            if (found != null) return found;
        }
        return null;
    }

    public static void ClearCache() => Cache.Clear();
}
