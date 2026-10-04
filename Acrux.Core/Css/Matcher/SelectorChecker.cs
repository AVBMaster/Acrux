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

        if (!element.TagName.Equals(selector.TagName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (selector.Namespace != null)
        {
            var elNs = element.NamespaceUri ?? "";
            if (selector.Namespace == "*")
                return true;
            if (selector.Namespace == "ns")
                return elNs == "http://www.w3.org/2000/svg" ||
                       elNs == "http://www.w3.org/1998/Math/MathML";
            return elNs.EndsWith(selector.Namespace, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

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
            CssPseudoType.NthChild => MatchNth(selector.Argument, element, false),
            CssPseudoType.NthLastChild => MatchNth(selector.Argument, element, true),
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

    private static bool MatchNth(string? argument, Element element, bool fromLast)
    {
        if (string.IsNullOrEmpty(argument)) return false;
        var (a, b) = ParseAnPlusB(argument);
        return MatchAnPlusB(a, b, element, fromLast, false);
    }

    private static bool MatchNthOfType(string? argument, Element element, bool fromLast)
    {
        if (string.IsNullOrEmpty(argument)) return false;
        var (a, b) = ParseAnPlusB(argument);
        return MatchAnPlusB(a, b, element, fromLast, true);
    }

    private static (int a, int b) ParseAnPlusB(string argument)
    {
        argument = argument.Trim();
        if (argument == "odd") return (2, 1);
        if (argument == "even") return (2, 0);

        int a = 0, b = 0;
        int i = 0;
        bool negative = false;

        if (argument[i] == '-') { negative = true; i++; }
        else if (argument[i] == '+') i++;

        int numStart = i;
        while (i < argument.Length && char.IsDigit(argument[i])) i++;
        string numStr = argument[numStart..i];
        if (numStr.Length > 0)
            a = int.Parse(numStr) * (negative ? -1 : 1);

        if (i < argument.Length && (argument[i] == 'n' || argument[i] == 'N'))
        {
            i++;
            if (a == 0) a = negative ? -1 : 1;
            SkipWhitespace(argument, ref i);
            if (i < argument.Length && (argument[i] == '+' || argument[i] == '-'))
            {
                bool bNeg = argument[i] == '-';
                i++;
                SkipWhitespace(argument, ref i);
                int bStart = i;
                while (i < argument.Length && char.IsDigit(argument[i])) i++;
                b = int.Parse(argument[bStart..i]) * (bNeg ? -1 : 1);
            }
        }
        else
        {
            a = 0;
            b = int.Parse(argument) * (negative ? -1 : 1);
        }

        return (a, b);
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
        if (fromLast)
            siblings.Reverse();

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