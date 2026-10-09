using System.Globalization;
using Acrux.Core.Dom;

namespace Acrux.Core.Css.Matcher;

/// <summary>
/// Core selector matching engine, mirroring Blink's SelectorChecker.
/// Matches CSSSelector chains against DOM elements.
/// </summary>
public class SelectorChecker
{
    private readonly struct CheckOneContext
    {
        public readonly Element Element;
        public readonly CssSelector Selector;
        public readonly bool IsSubSelector;

        public CheckOneContext(Element element, CssSelector selector, bool isSubSelector)
        {
            Element = element;
            Selector = selector;
            IsSubSelector = isSubSelector;
        }
    }

    public bool Match(CssSelector selector, Element element)
    {
        if (selector == null) return false;
        // rule.Selectors holds one head per comma-group of a selector list, so a
        // single head is matched directly against the element.
        return MatchSelector(selector, element);
    }

    private bool MatchSelector(CssSelector selector, Element element)
    {
        if (!CheckOne(selector, element))
            return false;

        if (selector.IsLastInComplexSelector)
            return true;

        return MatchForRelation(selector, element);
    }

    private bool MatchForRelation(CssSelector selector, Element element)
    {
        var next = selector.Next;
        if (next == null) return true;

        switch (selector.Relation)
        {
            case CssSelectorRelation.SubSelector:
                // Continue matching the next simple selector in the compound
                return MatchSelector(next, element);

            case CssSelectorRelation.Descendant:
            case CssSelectorRelation.RelativeDescendant:
            {
                var ancestor = element.ParentElement;
                while (ancestor != null)
                {
                    if (MatchSelector(next, ancestor))
                        return true;
                    ancestor = ancestor.ParentElement;
                }
                return false;
            }

            case CssSelectorRelation.Child:
            case CssSelectorRelation.RelativeChild:
            {
                var parent = element.ParentElement;
                return parent != null && MatchSelector(next, parent);
            }

            case CssSelectorRelation.DirectAdjacent:
            case CssSelectorRelation.RelativeDirectAdjacent:
            {
                var prev = element.PreviousSibling;
                while (prev != null)
                {
                    if (prev is Element prevEl)
                    {
                        if (MatchSelector(next, prevEl))
                            return true;
                        return false;
                    }
                    prev = prev.PreviousSibling;
                }
                return false;
            }

            case CssSelectorRelation.IndirectAdjacent:
            case CssSelectorRelation.RelativeIndirectAdjacent:
            {
                var sibling = element.PreviousSibling;
                while (sibling != null)
                {
                    if (sibling is Element siblingEl && MatchSelector(next, siblingEl))
                        return true;
                    sibling = sibling.PreviousSibling;
                }
                return false;
            }

            case CssSelectorRelation.ScopeActivation:
                // @scope activation - for now, match as descendant
            {
                var ancestor = element.ParentElement;
                while (ancestor != null)
                {
                    if (MatchSelector(next, ancestor))
                        return true;
                    ancestor = ancestor.ParentElement;
                }
                return false;
            }

            default:
                return false;
        }
    }

    private bool CheckOne(CssSelector selector, Element element)
    {
        // The namespace part is settled when the sheet its '@namespace' rules are read, and it
        // decides whether this element can be the one the selector names before any name is
        // compared: an id selector in a sheet whose default namespace is a foreign one matches
        // nothing at all (measured, and the same for a class, an attribute, a type and the '*').
        if (!MatchesNamespace(selector, element)) return false;

        switch (selector.MatchType)
        {
            case CssSelectorMatchType.Tag:
                return MatchTag(selector, element);

            case CssSelectorMatchType.Id:
                return selector.Value != null &&
                       string.Equals(element.Id, selector.Value, StringComparison.OrdinalIgnoreCase);

            case CssSelectorMatchType.Class:
                return selector.Value != null && element.HasClass(selector.Value);

            case CssSelectorMatchType.AttributeSet:
                return selector.AttributeName != null && element.HasAttribute(selector.AttributeName);

            case CssSelectorMatchType.AttributeExact:
                if (selector.AttributeName == null || selector.AttributeValue == null) return false;
                var exactVal = element.GetAttribute(selector.AttributeName);
                return exactVal != null &&
                       string.Equals(exactVal, selector.AttributeValue,
                           AttributeComparison(selector.AttributeName, selector.AttributeCaseSensitive));

            case CssSelectorMatchType.AttributeHyphen:
                if (selector.AttributeName == null) return false;
                var hyphenVal = element.GetAttribute(selector.AttributeName);
                var hyphenCmp = AttributeComparison(selector.AttributeName, selector.AttributeCaseSensitive);
                return hyphenVal == selector.AttributeValue ||
                       (hyphenVal != null && selector.AttributeValue != null &&
                        hyphenVal.StartsWith(selector.AttributeValue + "-", hyphenCmp));

            case CssSelectorMatchType.AttributeList:
                if (selector.AttributeName == null) return false;
                var listVal = element.GetAttribute(selector.AttributeName);
                if (listVal == null || selector.AttributeValue == null) return false;
                var listCmp = AttributeComparison(selector.AttributeName, selector.AttributeCaseSensitive);
                return listVal.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Any(part => string.Equals(part, selector.AttributeValue, listCmp));

            case CssSelectorMatchType.AttributeContain:
                if (selector.AttributeName == null) return false;
                var containVal = element.GetAttribute(selector.AttributeName);
                return containVal != null && selector.AttributeValue != null &&
                       containVal.Contains(selector.AttributeValue, selector.AttributeCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

            case CssSelectorMatchType.AttributeBegin:
                if (selector.AttributeName == null) return false;
                var beginVal = element.GetAttribute(selector.AttributeName);
                return beginVal != null && selector.AttributeValue != null &&
                       beginVal.StartsWith(selector.AttributeValue, selector.AttributeCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

            case CssSelectorMatchType.AttributeEnd:
                if (selector.AttributeName == null) return false;
                var endVal = element.GetAttribute(selector.AttributeName);
                return endVal != null && selector.AttributeValue != null &&
                       endVal.EndsWith(selector.AttributeValue, selector.AttributeCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

            case CssSelectorMatchType.PseudoClass:
                return CheckPseudoClass(selector, element);

            case CssSelectorMatchType.PseudoElement:
                // ::before/::after/::marker/::first-line/::first-letter match their
                // originating element so their rules can be routed to the element side-cars.
                // Every other pseudo-element (::selection, ::placeholder, ...) creates a
                // separate style context and must not apply to the element itself.
                return selector.PseudoType is CssPseudoType.Before or CssPseudoType.After or CssPseudoType.Marker
                    or CssPseudoType.FirstLine or CssPseudoType.FirstLetter;

            default:
                return true;
        }
    }

    private static bool MatchTag(CssSelector selector, Element element)
    {
        if (selector.TagName == null || selector.TagName == "*")
            return true;

        // The namespace part is already settled by the time a selector reaches the checker; what is
        // left is the name, which an HTML document compares without regard to case.
        return element.TagName.Equals(selector.TagName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the element the selector is tried against is in the namespace the selector
    /// asks for, as the sheet's declarations settled it. A selector that was never part of a sheet
    /// with any '@namespace' rule in it is matched by name alone, which is what an HTML document
    /// needs of a plain 'div': its elements are in the XHTML namespace, and a default of 'no
    /// namespace' would leave every type selector in the document matching nothing.</summary>
    /// <summary>Whether the element the compound names lives where the compound was written to live.
    /// The decision belongs to the compound, so an attribute part asks it of the element as much as a
    /// type selector does: '[data-x]' matches nothing in a sheet whose default is another namespace
    /// (measured), while 'q|*[data-x]' styles an element that carries that prefix.</summary>
    private static bool MatchesNamespace(CssSelector selector, Element element) =>
        selector.NamespaceRequirement switch
        {
            CssNamespaceRequirement.CannotMatch => false,
            CssNamespaceRequirement.Any => true,
            CssNamespaceRequirement.None => string.IsNullOrEmpty(element.NamespaceUri),
            CssNamespaceRequirement.Uri => element.NamespaceUri == selector.NamespaceUri,
            _ => true,
        };

    private bool CheckPseudoClass(CssSelector selector, Element element)
    {
        return selector.PseudoType switch
        {
            CssPseudoType.FirstChild => IsFirstChild(element),
            CssPseudoType.LastChild => IsLastChild(element),
            CssPseudoType.FirstOfType => IsFirstOfType(element),
            CssPseudoType.LastOfType => IsLastOfType(element),
            CssPseudoType.OnlyChild => element.ParentElement?.Children.OfType<Element>().Count() == 1,
            CssPseudoType.OnlyOfType => element.ParentElement?.Children.OfType<Element>()
                .Count(e => e.TagName == element.TagName) == 1,
            CssPseudoType.NthChild => MatchNth(selector, element, false),
            CssPseudoType.NthLastChild => MatchNth(selector, element, true),
            CssPseudoType.NthOfType => MatchNthOfType(selector.Argument, element, false),
            CssPseudoType.NthLastOfType => MatchNthOfType(selector.Argument, element, true),
            CssPseudoType.Root => element.Parent is Document,
            CssPseudoType.Empty => element.Children.Count == 0 && string.IsNullOrEmpty(element.TextContent),
            CssPseudoType.Link => element.TagName == "A" && element.HasAttribute("href"),
            CssPseudoType.Visited => element.TagName == "A" && element.HasAttribute("href"),
            CssPseudoType.AnyLink => element.TagName == "A" && element.HasAttribute("href"),
            CssPseudoType.Active => element.IsFocused,
            CssPseudoType.Hover => element.IsHovered,
            CssPseudoType.Focus => element.IsFocused,
            CssPseudoType.FocusVisible => element.IsFocused,
            CssPseudoType.FocusWithin => IsFocusWithin(element),
            CssPseudoType.Enabled => !element.HasAttribute("disabled") && IsFormLike(element),
            CssPseudoType.Disabled => element.HasAttribute("disabled"),
            CssPseudoType.Checked => element.HasAttribute("checked") || element.HasAttribute("selected"),
            CssPseudoType.Required => element.HasAttribute("required"),
            CssPseudoType.Optional => !element.HasAttribute("required") && IsFormLike(element),
            CssPseudoType.Valid => true,
            CssPseudoType.Invalid => false,
            CssPseudoType.InRange => true,
            CssPseudoType.OutOfRange => false,
            CssPseudoType.UserValid => true,
            CssPseudoType.UserInvalid => false,
            CssPseudoType.Default => element.HasAttribute("checked") || element.HasAttribute("selected"),
            CssPseudoType.Indeterminate => false,
            CssPseudoType.PlaceholderShown => element.HasAttribute("placeholder") && string.IsNullOrEmpty(element.Value),
            CssPseudoType.ReadOnly => element.HasAttribute("readonly"),
            CssPseudoType.ReadWrite => !element.HasAttribute("readonly") && IsFormLike(element),
            CssPseudoType.Scope => true,
            CssPseudoType.Defined => true,
            CssPseudoType.Target => false,
            CssPseudoType.Modal => false,
            CssPseudoType.PopoverOpen => false,
            CssPseudoType.Fullscreen => false,
            CssPseudoType.PictureInPicture => false,
            CssPseudoType.Is => MatchSelectorList(selector.SelectorList, element, true),
            CssPseudoType.Where => MatchSelectorList(selector.SelectorList, element, true),
            CssPseudoType.Not => !MatchSelectorList(selector.SelectorList, element, true),
            CssPseudoType.Has => MatchSelectorList(selector.SelectorList, element, false),
            CssPseudoType.Host => element.ShadowRoot != null,
            CssPseudoType.HostContext => element.ShadowRoot != null,
            CssPseudoType.Slotted => element.AssignedSlot != null,
            CssPseudoType.Part => element.HasAttribute("part"),
            CssPseudoType.Before => true,
            CssPseudoType.After => true,
            CssPseudoType.FirstLetter => false,
            CssPseudoType.FirstLine => false,
            CssPseudoType.Marker => false,
            CssPseudoType.Placeholder => false,
            CssPseudoType.Selection => false,
            CssPseudoType.Backdrop => false,
            CssPseudoType.FileSelectorButton => false,
            CssPseudoType.SpellingError => false,
            CssPseudoType.GrammarError => false,
            _ => true
        };
    }

    private static bool IsFirstChild(Element element)
    {
        var parent = element.ParentElement;
        if (parent == null) return false;
        return parent.Children.OfType<Element>().FirstOrDefault() == element;
    }

    private static bool IsLastChild(Element element)
    {
        var parent = element.ParentElement;
        if (parent == null) return false;
        return parent.Children.OfType<Element>().LastOrDefault() == element;
    }

    private static bool IsFirstOfType(Element element)
    {
        var parent = element.ParentElement;
        if (parent == null) return false;
        return parent.Children.OfType<Element>()
            .FirstOrDefault(e => e.TagName == element.TagName) == element;
    }

    private static bool IsLastOfType(Element element)
    {
        var parent = element.ParentElement;
        if (parent == null) return false;
        return parent.Children.OfType<Element>()
            .LastOrDefault(e => e.TagName == element.TagName) == element;
    }

    private static bool IsFocusWithin(Element element)
    {
        if (element.IsFocused) return true;
        foreach (var child in element.Children.OfType<Element>())
        {
            if (IsFocusWithin(child)) return true;
        }
        return false;
    }

    private static bool IsFormLike(Element element) => element.TagName switch
    {
        "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON" or "OPTION" or "OPTGROUP" => true,
        _ => false
    };

    private static bool MatchNth(CssSelector selector, Element element, bool fromLast)
    {
        if (!TryParseAnPlusB(selector.Argument, out int a, out int b)) return false;
        // ':nth-child(An+B of S)' (CSS Selectors 4 §6.6.2) numbers the element among the siblings
        // that match S, and an element the list leaves out is not numbered at all. An 'of' whose
        // list the grammar refused is a selector the engine has no reading for, so it matches
        // nothing — measured: '... of ' and ' of .c' each throw out of querySelectorAll and take
        // their rule down in a sheet.
        if (selector.NthOfPresent)
        {
            var parent = element.ParentElement;
            var list = selector.SelectorList;
            if (parent == null || list == null || list.Count == 0) return false;
            var matched = parent.Children.OfType<Element>()
                .Where(e => list.Any(sel => MatchChain(sel, e)))
                .ToList();
            return MatchAnPlusB(a, b, element, fromLast, matched);
        }
        return MatchAnPlusB(a, b, element, fromLast, false);
    }

    private static bool MatchNthOfType(string? argument, Element element, bool fromLast)
    {
        if (!TryParseAnPlusB(argument, out int a, out int b)) return false;
        return MatchAnPlusB(a, b, element, fromLast, true);
    }

    /// <summary>
    /// The An+B microsyntax of CSS Pseudo-classes 4 §6.6.2. A malformed argument is not an
    /// error and must never reach the caller as an exception — one bad selector in a sheet
    /// would otherwise take the matcher down for every element — so it says so instead, and
    /// the caller treats a selector it cannot read as one that matches nothing.
    /// </summary>
    internal static bool TryParseAnPlusB(string? argument, out int a, out int b)
    {
        a = 0; b = 0;
        argument = argument?.Trim();
        if (string.IsNullOrEmpty(argument)) return false;
        if (argument.Equals("odd", StringComparison.OrdinalIgnoreCase)) { a = 2; b = 1; return true; }
        if (argument.Equals("even", StringComparison.OrdinalIgnoreCase)) { a = 2; b = 0; return true; }

        int i = 0;
        bool negative = false;

        if (argument[i] == '-') { negative = true; i++; }
        else if (argument[i] == '+') i++;

        int numStart = i;
        while (i < argument.Length && char.IsDigit(argument[i])) i++;
        string numStr = argument[numStart..i];
        if (numStr.Length > 0 && !TryInteger(numStr, out a)) return false;
        if (negative) a = -a;

        if (i < argument.Length && (argument[i] == 'n' || argument[i] == 'N'))
        {
            i++;
            // '0n+5' really does mean a = 0; only an absent coefficient stands for one.
            if (numStr.Length == 0) a = negative ? -1 : 1;
            SkipWhitespace(argument, ref i);
            if (i < argument.Length && (argument[i] == '+' || argument[i] == '-'))
            {
                bool bNeg = argument[i] == '-';
                i++;
                SkipWhitespace(argument, ref i);
                int bStart = i;
                while (i < argument.Length && char.IsDigit(argument[i])) i++;
                if (!TryInteger(argument[bStart..i], out b)) return false;
                if (bNeg) b = -b;
            }
        }
        else
        {
            // No 'n' at all: the whole argument has to read as one signed integer, and the sign
            // stays on it — ':nth-child(-3)' numbers a 1-based list, so it reaches no element
            // (measured: the reference engine matches nothing for '-3' and the third child for
            // '+3'). The sign the prologue above consumed belongs to this same number, which is
            // why it is not applied twice here.
            a = 0;
            if (!TryInteger(argument, out b)) return false;
        }

        if (i != argument.Length) return false;
        return true;
    }

    /// <summary>An integer the pattern can hold. A selector written as
    /// ':nth-child(99999999999999n)' is not a crash and, in the reference engine, not a refusal
    /// either: it is read as far as an integer goes and saturates there (measured — it prints back
    /// as <c>2147483647n</c>). Anything that is not a signed integer at all is malformed.</summary>
    private static bool TryInteger(string text, out int value)
    {
        value = 0;
        if (text.Length == 0) return false;
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long wide))
            return false;
        value = wide > int.MaxValue ? int.MaxValue : wide < int.MinValue ? int.MinValue : (int)wide;
        return true;
    }

    /// <summary>What a pattern reads back as. The reference engine keeps the two numbers rather than
    /// the words they were written with, so <c>even</c> prints <c>2n</c>, <c>1n</c> prints <c>n</c>,
    /// <c>2n+0</c> prints <c>2n</c> and a pattern with no <c>n</c> prints its constant alone — each
    /// measured. It is the one spelling a selector's serialiser and a page's round-trip can share,
    /// which is why the parser canonicalises its argument through here.</summary>
    internal static string CanonicalAnPlusB(int a, int b)
    {
        if (a == 0) return b.ToString(CultureInfo.InvariantCulture);
        var text = a switch
        {
            1 => "n",
            -1 => "-n",
            _ => a.ToString(CultureInfo.InvariantCulture) + "n",
        };
        if (b == 0) return text;
        return text + (b > 0 ? "+" : "-") +
            Math.Abs(b).ToString(CultureInfo.InvariantCulture);
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static bool MatchAnPlusB(int a, int b, Element element, bool fromLast, bool ofType)
    {
        var parent = element.ParentElement;
        if (parent == null) return false;

        var siblings = parent.Children.OfType<Element>().ToList();
        if (ofType)
            siblings = siblings.Where(e => e.TagName == element.TagName).ToList();
        return MatchAnPlusB(a, b, element, fromLast, siblings);
    }

    private static bool MatchAnPlusB(int a, int b, Element element, bool fromLast, List<Element> siblings)
    {
        if (fromLast) siblings.Reverse();

        int index = siblings.IndexOf(element);
        if (index < 0) return false;

        int n = index + 1;
        if (a == 0) return n == b;
        if ((n - b) % a != 0) return false;
        return (n - b) / a >= 0;
    }

    private static bool MatchSelectorList(List<CssSelector>? list, Element element, bool matchSelf)
    {
        if (list == null || list.Count == 0) return true;
        foreach (var sel in list)
        {
            if (matchSelf)
            {
                if (MatchChain(sel, element))
                    return true;
            }
            else
            {
                // :has() - test against descendants
                if (MatchHasDescendant(sel, element))
                    return true;
            }
        }
        return false;
    }

    private static bool MatchChain(CssSelector selector, Element element)
    {
        var checker = new SelectorChecker();
        return checker.Match(selector, element);
    }

    private static bool MatchHasDescendant(CssSelector selector, Element element)
    {
        foreach (var child in element.Children.OfType<Element>())
        {
            if (MatchChain(selector, child))
                return true;
            if (MatchHasDescendant(selector, child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// How a selector's attribute value compares with the element's. The 'i' flag
    /// makes the test ASCII case-insensitive for any attribute; independently of
    /// that, HTML's enumerated attributes have a case-insensitive value space, so
    /// [type="checkbox"] matches type=CHECKBOX (verified against Chrome, which
    /// resolves both to its 13x13 checkbox).
    /// </summary>
    private static StringComparison AttributeComparison(string attributeName, bool caseSensitive) =>
        !caseSensitive || CaseInsensitiveHtmlAttributes.Contains(attributeName.ToLowerInvariant())
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static readonly HashSet<string> CaseInsensitiveHtmlAttributes = new(StringComparer.Ordinal)
    {
        "type", "size", "align", "valign", "clear", "compact", "nowrap", "shape", "scope",
        "charset", "coords", "headers", "method", "span", "target", "declare", "defer",
        "ismap", "nohref", "noshade", "rev", "link", "vlink", "alink", "frame", "rules",
    };
}