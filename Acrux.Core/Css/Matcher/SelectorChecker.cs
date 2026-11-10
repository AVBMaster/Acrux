using System.Globalization;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;

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
                // ::selection and ::placeholder are routed the same way — their declarations
                // live in a side-car and are only reported through getComputedStyle, never
                // applied to the element's own box.
                return selector.PseudoType is CssPseudoType.Before or CssPseudoType.After or CssPseudoType.Marker
                    or CssPseudoType.FirstLine or CssPseudoType.FirstLetter
                    or CssPseudoType.Selection or CssPseudoType.Placeholder;

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
            CssPseudoType.Enabled => IsFormLike(element) && !IsActuallyDisabled(element),
            CssPseudoType.Disabled => IsActuallyDisabled(element),
            CssPseudoType.Checked => element.HasAttribute("checked") || element.HasAttribute("selected"),
            CssPseudoType.Required => element.HasAttribute("required"),
            CssPseudoType.Optional => !element.HasAttribute("required") && IsFormLike(element),
            CssPseudoType.Valid => IsValidControl(element),
            CssPseudoType.Invalid => IsInvalidControl(element),
            CssPseudoType.InRange => IsInRange(element),
            CssPseudoType.OutOfRange => IsOutOfRange(element),
            CssPseudoType.UserValid => true,
            CssPseudoType.UserInvalid => false,
            CssPseudoType.Default => IsDefaultControl(element),
            CssPseudoType.Indeterminate => IsIndeterminate(element),
            CssPseudoType.PlaceholderShown => element.HasAttribute("placeholder") && string.IsNullOrEmpty(element.Value),
            CssPseudoType.ReadOnly => !IsReadWriteControl(element),
            CssPseudoType.ReadWrite => IsReadWriteControl(element),
            // ':scope' is the element a query is anchored to, not a property of the element it is
            // tried against: 'box.querySelectorAll(":scope > p")' asks about box's children while
            // 'box.querySelectorAll(":scope")' finds nothing, because the anchor is never one of its
            // own descendants (all measured).
            CssPseudoType.Scope => ReferenceEquals(element, QueryScope),
            CssPseudoType.Lang => MatchLang(selector.Argument, element),
            CssPseudoType.Dir => MatchDir(selector.Argument, element),
            CssPseudoType.Defined => IsDefined(element),
            CssPseudoType.Target => false,
            CssPseudoType.Modal => element.IsModal,
            CssPseudoType.PopoverOpen => element.IsPopoverShowing,
            CssPseudoType.Fullscreen => false,
            CssPseudoType.PictureInPicture => false,
            CssPseudoType.Is => MatchSelectorList(selector.SelectorList, element, true),
            CssPseudoType.Where => MatchSelectorList(selector.SelectorList, element, true),
            CssPseudoType.Not => !MatchSelectorList(selector.SelectorList, element, true),
            CssPseudoType.Has => MatchHas(selector.SelectorList, element),
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
            // A pseudo-class the engine has no reading for matches nothing. It used to answer 'true',
            // which made every unknown ':xyz' a universal selector (measured: ':lang(en)' styled the
            // whole document before ':lang' was known here).
            _ => false
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

    /// <summary>HTML §4.10.4: whether the control is 'actually disabled' — it sets its own
    /// `disabled` attribute, or a `<fieldset disabled>` is an ancestor. The fieldset cascade is
    /// not bounded by a `<form>` (measured: an input inside a form inside a disabled fieldset is
    /// still ':disabled', though its own 'disabled' property answers false), and a `<optgroup
    /// disabled>` disables its options. An unrelated `disabled` attribute on a div or legend
    /// disables nothing (measured: those inputs stay ':enabled').</summary>
    private static bool IsActuallyDisabled(Element element)
    {
        if (element.HasAttribute("disabled")) return true;
        // An option is disabled by an enclosing disabled optgroup as well as by a fieldset.
        if (element.TagName == "OPTION")
        {
            var parent = element.ParentElement;
            if (parent is not null && parent.TagName.Equals("OPTGROUP", StringComparison.OrdinalIgnoreCase)
                && parent.HasAttribute("disabled"))
                return true;
        }
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor.TagName.Equals("FIELDSET", StringComparison.OrdinalIgnoreCase)
                && ancestor.HasAttribute("disabled"))
                return true;
        }
        return false;
    }

    /// <summary>Whether a control is editable, which is what ':read-write' tests (HTML §6.5).
    /// Only a text-entry input — or a textarea — that is neither disabled nor readonly counts;
    /// a checkbox, radio, button or select is always read-only, and so is a disabled control
    /// (measured: a text input with readonly is :read-only, a disabled text input is :read-only,
    /// and a checkbox is :read-only regardless).</summary>
    private static bool IsReadWriteControl(Element element)
    {
        if (element.HasAttribute("disabled") || element.HasAttribute("readonly")) return false;
        if (element.TagName.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase)) return true;
        if (!element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase)) return false;
        var type = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        return type is "text" or "search" or "url" or "tel" or "email" or "password" or "number"
            or "date" or "month" or "week" or "time" or "datetime-local" or "range";
    }

    /// <summary>HTML §4.10.4: whether this control takes part in constraint validation at all —
    /// a submittable control that is neither disabled nor read-only. The four :valid/:invalid/
    /// :in-range/:out-of-range pseudos only apply to such controls (measured: a disabled required
    /// field answers :valid because it is barred from validation).</summary>
    private static bool WillValidate(Element element)
    {
        if (!element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) &&
            !element.TagName.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase) &&
            !element.TagName.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            return false;
        if (element.HasAttribute("disabled") || element.HasAttribute("readonly")) return false;
        if (element.HasAttribute("formnovalidate")) return false;
        var inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        // A hidden input never validates (HTML §4.10.4).
        return inputType != "hidden";
    }

    /// <summary>Whether this INPUT/TEXTAREA/SELECT has a constraint that fires right now. Returns
    /// the two answers the range pseudos need in one pass.</summary>
    private static (bool valueMissing, bool badInput, bool typeMismatch, bool patternMismatch,
        bool rangeUnderflow, bool rangeOverflow) ComputeFlags(Element element)
    {
        bool valueMissing = false, badInput = false, typeMismatch = false,
             patternMismatch = false, rangeUnderflow = false, rangeOverflow = false;

        var text = element.Value ?? "";
        var tagName = element.TagName;
        var inputType = tagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase)
            ? (element.GetAttribute("type") ?? "text").ToLowerInvariant()
            : tagName.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase) ? "textarea" : "select";

        // valueMissing: a required field with no value.
        valueMissing = element.HasAttribute("required") && text.Length == 0;

        if (inputType == "number" || inputType == "range")
        {
            if (text.Length > 0 && !double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out _))
                badInput = true;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                if (double.TryParse(element.GetAttribute("min"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var min) && number < min)
                    rangeUnderflow = true;
                if (double.TryParse(element.GetAttribute("max"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var max) && number > max)
                    rangeOverflow = true;
            }
        }

        if (inputType == "email" && text.Length > 0 && !text.Contains('@'))
            typeMismatch = true;
        if (inputType == "url" && text.Length > 0 && !Uri.IsWellFormedUriString(text, UriKind.Absolute))
            typeMismatch = true;

        var pattern = element.GetAttribute("pattern");
        if (!string.IsNullOrEmpty(pattern) && text.Length > 0 && inputType != "number"
            && inputType != "range" && inputType != "email" && inputType != "url")
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, "^(?:" + pattern + ")\\z"))
                    patternMismatch = true;
            }
            catch { /* un-compilable pattern is not checked, matching the reference */ }
        }

        return (valueMissing, badInput, typeMismatch, patternMismatch, rangeUnderflow, rangeOverflow);
    }

    /// <summary>HTML §4.10.4: the control is valid when no constraint flag fires.</summary>
    private static bool IsValidControl(Element element)
    {
        if (!WillValidate(element)) return true;
        var f = ComputeFlags(element);
        return !(f.valueMissing || f.badInput || f.typeMismatch || f.patternMismatch
            || f.rangeUnderflow || f.rangeOverflow);
    }

    private static bool IsInvalidControl(Element element)
    {
        if (!WillValidate(element)) return false;
        var f = ComputeFlags(element);
        return f.valueMissing || f.badInput || f.typeMismatch || f.patternMismatch
            || f.rangeUnderflow || f.rangeOverflow;
    }

    /// <summary>HTML §4.10.4: the control has a range and its value is inside it — only
    /// number/range inputs can be :in-range or :out-of-range.</summary>
    private static bool IsInRange(Element element)
    {
        if (!WillValidate(element)) return false;
        var inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        if (inputType != "number" && inputType != "range") return false;
        var f = ComputeFlags(element);
        return !f.rangeUnderflow && !f.rangeOverflow;
    }

    private static bool IsOutOfRange(Element element)
    {
        if (!WillValidate(element)) return false;
        var inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        if (inputType != "number" && inputType != "range") return false;
        var f = ComputeFlags(element);
        return f.rangeUnderflow || f.rangeOverflow;
    }

    /// <summary>The element a running query is anchored to, for ':scope'. The matcher is re-entered
    /// by nested lists (':is()', ':has()'), so a query that sets it hands the previous anchor back
    /// when it finishes.</summary>
    [ThreadStatic] private static Element? s_scope;

    /// <summary>Run |match| with |scope| as the ':scope' anchor, restoring whatever was there before.</summary>
    public static T WithScope<T>(Element? scope, Func<T> match)
    {
        var previous = s_scope;
        s_scope = scope;
        try { return match(); }
        finally { s_scope = previous; }
    }

    private static Element? QueryScope => s_scope;

    /// <summary>
    /// <c>:lang(&lt;language-range&gt;)</c> (CSS Selectors 4 §6.7, HTML's element language). The
    /// element's language is its own non-empty 'lang', else the nearest ancestor's, else the
    /// document's; a range then matches when it is the whole tag or a leading subtag sequence —
    /// 'en' reaches 'en-GB', 'gb' reaches nothing, and the test ignores case (all measured).
    /// </summary>
    private static bool MatchLang(string? range, Element element)
    {
        if (string.IsNullOrEmpty(range)) return false;
        var language = LanguageOf(element);
        if (language == null) return false;
        return language.Equals(range, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith(range + "-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the element's effective direction — its own or its nearest ancestor's
    /// 'dir' attribute, defaulting to 'ltr' — is the one ':dir()' names (HTML §6.2, measured:
    /// ':dir(ltr)' matches every element of an ltr document and ':dir(rtl)' matches none).
    /// The direction inherits, so a 'dir' on an ancestor governs its descendants too.</summary>
    private static bool MatchDir(string? value, Element element)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var wanted = value.Trim().ToLowerInvariant();
        if (wanted is not ("ltr" or "rtl")) return false;
        var direction = "ltr";
        for (Element? el = element; el != null; el = el.ParentElement)
        {
            var dir = el.GetAttribute("dir");
            if (!string.IsNullOrEmpty(dir)) { direction = dir.ToLowerInvariant(); break; }
        }
        return direction == wanted;
    }

    /// <summary>HTML §4.13.6: ':indeterminate' matches a progress bar (an unspecified one always),
    /// and a radio in a group whose radios none of is checked. A checkbox that is not checked is not
    /// indeterminate (measured: '<input type=checkbox>' is false, a radio in an all-unchecked group
    /// is true).</summary>
    private static bool IsIndeterminate(Element element)
    {
        if (element is HTMLProgressElement) return true;
        if (element is not HTMLInputElement input) return false;
        var inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        if (inputType == "radio")
        {
            // HTML §4.10.4: the radio is indeterminate when no radio in its group carries a checked
            // state — walk its siblings for the first radio with the same name (the checked attribute
            // is the state the radio carries while nothing else has set it).
            if (input.Checked) return false;
            var name = element.GetAttribute("name");
            var parent = element.ParentElement;
            if (parent == null) return false;
            if (HasCheckedRadioInGroup(parent, name)) return false;
            return true;
        }
        return input.Indeterminate;
    }

    /// <summary>Whether any radio in the subtree under |parent| is checked in this group
    /// (HTML §4.10.4). The checked state is the element's own checked flag OR the checked
    /// content attribute — the attribute is the initial state the parser did not promote to
    /// the property (measured: a radio with checked in its markup is not :indeterminate).</summary>
    private static bool HasCheckedRadioInGroup(Element parent, string? name)
    {
        foreach (var child in parent.Children)
        {
            if (child is HTMLInputElement input
                && input.GetAttribute("type").Equals("radio", StringComparison.OrdinalIgnoreCase)
                && input.GetAttribute("name") == name
                && (input.Checked || input.HasAttribute("checked")))
                return true;
            if (child is Element childElement && HasCheckedRadioInGroup(childElement, name))
                return true;
        }
        return false;
    }

    /// <summary>HTML §4.13.2: ':defined' only matches an element whose tag names a real interface.
    /// A tag the parser built as an HTMLUnknownElement is not defined (measured).</summary>
    private static bool IsDefined(Element element)
        => element is not HTMLUnknownElement && HtmlElementFactory.IsKnown(element.TagName);

    /// <summary>HTML §4.13.7: ':default' matches a checkbox/radio with the checked content
    /// attribute, a selected option, or a submit button — one that is the default button of its
    /// form (HTML §4.10.22.2: the first submit control in tree order). Measured: a typeless or
    /// type=submit button matches ':default' without any attribute.</summary>
    private static bool IsDefaultControl(Element element)
    {
        if (element.HasAttribute("checked") || element.HasAttribute("selected")) return true;
        if (!IsSubmitControl(element)) return false;
        // The form default button is the first submit control in tree order among the form's
        // listed controls (HTML §4.10.22.2). The reference roots that walk at the button's
        // owning form when it has one, otherwise the element is not the default (measured).
        var root = FindOwningForm(element);
        if (root == null) return false;
        return FirstSubmitIn(root) == element;
    }

    /// <summary>The nearest ancestor 'form' element, which is what scopes a submit button's
    /// default-ness (the button has no form property of its own here).</summary>
    private static Element? FindOwningForm(Element element)
    {
        for (var parent = element.ParentElement; parent != null; parent = parent.ParentElement)
            if (parent.TagName.Equals("FORM", StringComparison.OrdinalIgnoreCase)) return parent;
        return null;
    }

    /// <summary>The first submit control in the subtree under |root|, in tree order, or null when
    /// the subtree holds none (HTML §4.10.22.2: the default button is the first one).</summary>
    private static Element? FirstSubmitIn(Element root)
    {
        foreach (var child in root.Children)
        {
            if (child is not Element childElement) continue;
            if (IsSubmitControl(childElement)) return childElement;
            var found = FirstSubmitIn(childElement);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsSubmitControl(Element element)
    {
        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
        {
            var type = (element.GetAttribute("type") ?? "submit").ToLowerInvariant();
            return type == "submit";
        }
        if (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
            return (element.GetAttribute("type") ?? "text").ToLowerInvariant() == "submit";
        return false;
    }

    /// <summary>The language declared for the element, following HTML: the nearest 'lang' attribute
    /// that is not empty, and failing that the one on the root — which is what makes a child of
    /// '<html lang="pt-BR">' answer ':lang(pt)' while a page that declares nothing gives its
    /// elements no language at all (measured). An empty 'lang' stops the search rather than
    /// inheriting past it.</summary>
    private static string? LanguageOf(Element element)
    {
        for (Node? node = element; node is not null; node = node.Parent)
        {
            if (node is not Element el || !el.HasAttribute("lang")) continue;
            var declared = el.GetAttribute("lang");
            return string.IsNullOrEmpty(declared) ? null : declared;
        }
        return element.OwnerDocument?.DocumentElement?.GetAttribute("lang");
    }

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

    /// <summary>
    /// <c>:has(S)</c> (CSS Selectors 4 §6.8.1): a member that was written relative looks in the
    /// place its combinator names — <c>&gt;</c> the children, <c>+</c> the element right after it,
    /// <c>~</c> any element after it — while a plain member searches every descendant.
    /// </summary>
    private static bool MatchHas(List<CssSelector>? list, Element element)
    {
        if (list == null || list.Count == 0) return false;
        foreach (var sel in list)
        {
            bool hit = sel.Relation switch
            {
                CssSelectorRelation.RelativeChild =>
                    element.Children.OfType<Element>().Any(child => MatchChain(sel, child)),
                CssSelectorRelation.RelativeDirectAdjacent =>
                    NextElementSibling(element) is Element next && MatchChain(sel, next),
                CssSelectorRelation.RelativeIndirectAdjacent =>
                    FollowingSiblings(element).Any(sibling => MatchChain(sel, sibling)),
                _ => MatchHasDescendant(sel, element),
            };
            if (hit) return true;
        }
        return false;
    }

    private static Element? NextElementSibling(Element element)
    {
        for (var node = element.NextSibling; node != null; node = node.NextSibling)
            if (node is Element el) return el;
        return null;
    }

    private static IEnumerable<Element> FollowingSiblings(Element element)
    {
        for (var node = element.NextSibling; node != null; node = node.NextSibling)
            if (node is Element el) yield return el;
    }

    private static bool MatchSelectorList(List<CssSelector>? list, Element element, bool matchSelf)
    {
        // An empty list matches nothing — that is what ':is()' with a list whose every member was
        // refused means. ':not()' and ':has()' never reach it with an empty list, because the parser
        // refuses those outright (measured).
        if (list == null || list.Count == 0) return false;
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