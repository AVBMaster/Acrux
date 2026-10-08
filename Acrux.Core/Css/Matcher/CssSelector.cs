namespace Acrux.Core.Css.Matcher;

/// <summary>Selector match type, mirroring Blink's CSSSelector::MatchType.</summary>
public enum CssSelectorMatchType
{
    Tag,
    Id,
    Class,
    PseudoClass,
    PseudoElement,
    PagePseudoClass,
    AttributeExact,
    AttributeSet,
    AttributeHyphen,
    AttributeList,
    AttributeContain,
    AttributeBegin,
    AttributeEnd
}

/// <summary>Combinator between simple selectors, mirroring Blink's CSSSelector::RelationType.</summary>
public enum CssSelectorRelation
{
    SubSelector,
    Descendant,
    Child,
    DirectAdjacent,
    IndirectAdjacent,
    UAShadow,
    ShadowSlot,
    ShadowPart,
    RelativeDescendant,
    RelativeChild,
    RelativeDirectAdjacent,
    RelativeIndirectAdjacent,
    ScopeActivation
}

/// <summary>Pseudo-class/ pseudo-element type, mirroring Blink's CSSSelector::PseudoType.</summary>
public enum CssPseudoType
{
    Unknown,
    Active, AnyLink, AnyLinkPseudo, Autofill, Blank, Bullet, 
    Checked, Closed, CorF, Current, Defined, Default, Disabled, Done, Drag, 
    Empty, Enabled, FirstChild, FirstOfType, FirstPage, Focus, FocusVisible, 
    FocusWithin, Fullscreen, Future, Has, Host, HostContext, Hover, 
    InRange, Indeterminate, Invalid, Is, LastChild, LastOfType, Left, 
    Link, Modal, MozAny, MozFocusRing, MozUIValid, MozUIInvalid, 
    NoOpen, Not, NthChild, NthLastChild, NthLastOfType, NthOfType, 
    OnlyChild, OnlyOfType, Open, Optional, OutOfRange, Past, 
    Paused, PictureInPicture, PlaceholderShown, Playing, PopoverOpen, 
    ReadOnly, ReadWrite, Required, Right, Root, Scope, 
    State, Target, Unresolved, UserInvalid, UserValid, Valid, 
    Visited, Where, WindowInactive,
    // Pseudo-elements
    After, Backdrop, Before, Cue, CueRegion, FileSelectorButton, 
    FirstLetter, FirstLine, GrammarError, Highlight, Marker, 
    Part, Placeholder, Selection, Slotted, SpellingError, 
    TargetText, ViewTransition, ViewTransitionGroup, 
    ViewTransitionImagePair, ViewTransitionNew, ViewTransitionOld,
    // Scrollbar pseudo-elements
    Scrollbar, ScrollbarButton, ScrollbarCorner, ScrollbarThumb, 
    ScrollbarTrack, ScrollbarTrackPiece, Resizer, ScrollNextButton, 
    ScrollPrevButton
}

/// <summary>How the namespace part of a simple selector is settled once the sheet's '@namespace'
/// declarations have been read. A selector written with no namespace part takes the sheet's default
/// address; written with <c>*|</c> it takes any address; written with <c>|</c> or a prefix it takes
/// the one it names — and a prefix the sheet never declared leaves the whole selector unable to
/// match anything at all.</summary>
public enum CssNamespaceRequirement
{
    /// <summary>No '@namespace' was read for the sheet, so nothing is decided about namespaces and
    /// the selector is matched by name alone.</summary>
    Unchecked,

    /// <summary>Any namespace, written as <c>*|E</c>.</summary>
    Any,

    /// <summary>No namespace at all, written as <c>|E</c>. No element of an HTML document has one,
    /// so such a selector matches nothing there (measured).</summary>
    None,

    /// <summary>Exactly the address in <see cref="CssSelector.NamespaceUri"/>: the one a declared
    /// prefix names, or the sheet's default for a selector written bare.</summary>
    Uri,

    /// <summary>The selector names a prefix the sheet never declares, or puts a namespace on an
    /// attribute — neither of which the reference engine lets match anything (measured both).</summary>
    CannotMatch,
}

/// <summary>
/// A single CSS simple selector, with optional chaining to form compound/complex selectors.
/// Mirrors Blink's CSSSelector class.
/// </summary>
public class CssSelector
{
    public CssSelectorMatchType MatchType { get; set; }
    public CssSelectorRelation Relation { get; set; }
    public CssPseudoType PseudoType { get; set; }

    public string? Value { get; set; }
    public string? AttributeName { get; set; }
    public string? AttributeValue { get; set; }
    public bool AttributeCaseSensitive { get; set; } = true;

    public string? Namespace { get; set; }
    public string? TagName { get; set; }

    /// <summary>Which namespace the element the selector names has to live in, as the sheet's
    /// '@namespace' declarations settle it. Left at <see cref="CssNamespaceRequirement.Unchecked"/>
    /// for a selector that was never read as part of a sheet.</summary>
    public CssNamespaceRequirement NamespaceRequirement { get; set; } = CssNamespaceRequirement.Unchecked;

    /// <summary>The address to compare an element's own against, meaningful only when
    /// <see cref="NamespaceRequirement"/> is <see cref="CssNamespaceRequirement.Uri"/>.</summary>
    public string? NamespaceUri { get; set; }

    /// <summary>Whether the any-namespace bar an element selector was written with has to be printed
    /// back. The sheet's settle pass raises it when that sheet gives its bare selectors a default to
    /// differ from, because dropping the bar there would name a different element (measured:
    /// '@namespace url(other); *|div' reads 'the div written with its bar', while the same rule in a
    /// sheet that declares nothing reads plain 'div').</summary>
    public bool PrintsAnyNamespacePrefix { get; set; }

    public string? Argument { get; set; }
    public List<CssSelector>? SelectorList { get; set; }

    public int A { get; set; }
    public int B { get; set; }

    public bool IsLastInComplexSelector { get; set; }
    public bool IsLastInSelectorList { get; set; }

    public CssSelector? Next { get; set; }

    public int SpecificityA { get; set; } // #id
    public int SpecificityB { get; set; } // .class, [attr], :pseudo
    public int SpecificityC { get; set; } // tag, ::pseudo

    public void ComputeSpecificity()
    {
        SpecificityA = 0;
        SpecificityB = 0;
        SpecificityC = 0;
        ComputeSpecificityRecursive(this, 0, out var a, out var b, out var c);
        SpecificityA = a;
        SpecificityB = b;
        SpecificityC = c;
    }

    private static void ComputeSpecificityRecursive(CssSelector? sel, int depth, out int a, out int b, out int c)
    {
        a = 0; b = 0; c = 0;
        if (sel == null) return;

        if (sel.PseudoType == CssPseudoType.Where)
        {
            // :where() has zero specificity
        }
        else if (sel.PseudoType is CssPseudoType.Is or CssPseudoType.Not or CssPseudoType.Has)
        {
            if (sel.SelectorList != null)
            {
                int maxA = 0, maxB = 0, maxC = 0;
                foreach (var child in sel.SelectorList)
                {
                    ComputeSpecificityRecursive(child, depth + 1, out int ca, out int cb, out int cc);
                    if (ca > maxA || (ca == maxA && cb > maxB) || (ca == maxA && cb == maxB && cc > maxC))
                    { maxA = ca; maxB = cb; maxC = cc; }
                }
                a += maxA; b += maxB; c += maxC;
            }
        }
        else
        {
            switch (sel.MatchType)
            {
                case CssSelectorMatchType.Id:
                    a++;
                    break;
                case CssSelectorMatchType.Class:
                case CssSelectorMatchType.AttributeExact:
                case CssSelectorMatchType.AttributeSet:
                case CssSelectorMatchType.AttributeHyphen:
                case CssSelectorMatchType.AttributeList:
                case CssSelectorMatchType.AttributeContain:
                case CssSelectorMatchType.AttributeBegin:
                case CssSelectorMatchType.AttributeEnd:
                    b++;
                    break;
                case CssSelectorMatchType.PseudoClass:
                    if (sel.PseudoType != CssPseudoType.Where)
                        b++;
                    break;
                case CssSelectorMatchType.PseudoElement:
                    c++;
                    break;
                case CssSelectorMatchType.Tag:
                    if (sel.TagName != null && sel.TagName != "*")
                        c++;
                    break;
            }
        }

        // Recurse into selector list for :is(), :not(), :has(), :nth-child(An+B of selector-list)
        if (sel.SelectorList != null && sel.PseudoType is not (CssPseudoType.Is or CssPseudoType.Not or CssPseudoType.Has))
        {
            // For :nth-child(An+B of S), the specificity of S is added
            if (sel.PseudoType is CssPseudoType.NthChild or CssPseudoType.NthLastChild)
            {
                int maxA = 0, maxB = 0, maxC = 0;
                foreach (var child in sel.SelectorList)
                {
                    ComputeSpecificityRecursive(child, depth + 1, out int ca, out int cb, out int cc);
                    if (ca > maxA || (ca == maxA && cb > maxB) || (ca == maxA && cb == maxB && cc > maxC))
                    { maxA = ca; maxB = cb; maxC = cc; }
                }
                a += maxA; b += maxB; c += maxC;
            }
        }

        // Add next chain selector's specificity. Compound members are linked by
        // SubSelector (tr → .x) and complex parts by combinators — every link in
        // the chain contributes its simple selector's specificity.
        if (sel.Next != null)
        {
            ComputeSpecificityRecursive(sel.Next, depth, out int na, out int nb, out int nc);
            a += na; b += nb; c += nc;
        }
    }

    public override string ToString() => SimpleText(hasCompanions: false);

    /// <summary>The text this one simple selector prints, told whether the compound it sits in says
    /// anything else as well. Two of the answers are inversions of each other (measured): a universal
    /// that carries no namespace decision disappears from a compound that has another part —
    /// <c>*[a]</c> reads <c>[a]</c> — while a prefix written in front of a type selector is kept for
    /// <c>|*</c> and <c>q|*</c> and dropped for the any-namespace one, which reads as a bare <c>*</c>.
    /// In front of an attribute it is the empty prefix that disappears: <c>[|a]</c> reads <c>[a]</c>,
    /// <c>[*|a]</c> and <c>[q|a]</c> read as written.</summary>
    public string SimpleText(bool hasCompanions)
    {
        if (MatchType == CssSelectorMatchType.PseudoClass && PseudoType == CssPseudoType.Not && SelectorList != null)
            return $":not({string.Join(", ", SelectorList.Select(s => s.ToComplexText()))})";
        if (MatchType == CssSelectorMatchType.PseudoClass && PseudoType == CssPseudoType.Is && SelectorList != null)
            return $":is({string.Join(", ", SelectorList.Select(s => s.ToComplexText()))})";
        if (MatchType == CssSelectorMatchType.PseudoClass && PseudoType == CssPseudoType.Where && SelectorList != null)
            return $":where({string.Join(", ", SelectorList.Select(s => s.ToComplexText()))})";
        if (MatchType == CssSelectorMatchType.PseudoClass && PseudoType == CssPseudoType.Has && SelectorList != null)
            return $":has({string.Join(", ", SelectorList.Select(s => s.ToComplexText()))})";
        if (MatchType == CssSelectorMatchType.Id && Value != null)
            return $"#{Value}";
        if (MatchType == CssSelectorMatchType.Class && Value != null)
            return $".{Value}";
        if (MatchType == CssSelectorMatchType.Tag)
        {
            // An any-namespace prefix is never spelled on the way out, and a plain universal is
            // spelled only when the compound has nothing else to say.
            string prefix = Namespace switch
            {
                null => "",
                "*" => PrintsAnyNamespacePrefix ? "*|" : "",
                "" => "|",
                var name => name + "|",
            };
            if (prefix.Length == 0 && TagName == "*" && hasCompanions) return "";
            return prefix + (TagName ?? "*");
        }
        if (MatchType == CssSelectorMatchType.PseudoClass)
            return $":{PseudoType.ToString().ToLowerInvariant()}({Argument})" is var s && Argument != null ? s : $":{PseudoType.ToString().ToLowerInvariant()}";
        if (MatchType == CssSelectorMatchType.PseudoElement)
            return $"::{PseudoType.ToString().ToLowerInvariant()}";
        if (MatchType is CssSelectorMatchType.AttributeExact or CssSelectorMatchType.AttributeSet or
            CssSelectorMatchType.AttributeHyphen or CssSelectorMatchType.AttributeList or
            CssSelectorMatchType.AttributeContain or CssSelectorMatchType.AttributeBegin or
            CssSelectorMatchType.AttributeEnd)
        {
            string op = MatchType switch
            {
                CssSelectorMatchType.AttributeExact => "=",
                CssSelectorMatchType.AttributeSet => "",
                CssSelectorMatchType.AttributeHyphen => "|=",
                CssSelectorMatchType.AttributeList => "~=",
                CssSelectorMatchType.AttributeContain => "*=",
                CssSelectorMatchType.AttributeBegin => "^=",
                CssSelectorMatchType.AttributeEnd => "$=",
                _ => ""
            };
            string val = AttributeValue != null ? $"\"{AttributeValue}\"" : "";
            string prefix = Namespace switch
            {
                null or "" => "",
                var name => name + "|",
            };
            return $"[{prefix}{AttributeName}{op}{val}]";
        }
        return "?";
    }

    /// <summary>
    /// Serializes the full complex selector chain (the whole comma-group) into CSS
    /// selector text. Unlike <see cref="ToString"/>, which only serializes a single
    /// simple selector, this walks the subselector chain and the ancestor links so
    /// consumers see e.g. ".a &gt; .b:hover" rather than just ".b".
    /// </summary>
    public string ToComplexText()
    {
        // Walk from the subject (rightmost compound) toward the root (leftmost).
        var compounds = new List<string>();
        var combinators = new List<string>();
        CssSelector? current = this;

        while (current != null)
        {
            // Collect the whole compound: head ... tail via SubSelector links. The members are
            // counted before any of them is printed because a universal disappears when the
            // compound has something else to say.
            var members = new List<CssSelector>();
            CssSelector cursor = current!;
            members.Add(cursor);
            // A simple selector carries the relation it is joined to the chain by, so the link from
            // a member to the next one of the same compound is the one whose PARENT is still a plain
            // sub-selector; the tail of a compound has had its own relation overwritten by the
            // combinator that leads to the ancestor.
            while (cursor.Relation == CssSelectorRelation.SubSelector && cursor.Next != null)
            {
                cursor = cursor.Next;
                members.Add(cursor);
            }
            var compound = new System.Text.StringBuilder();
            foreach (var member in members)
                compound.Append(member.SimpleText(members.Count > 1));

            // The tail carries the combinator to the ancestor compound.
            combinators.Add(RelationToText(cursor.Relation));
            current = cursor.Next;
            compounds.Add(compound.ToString());
        }

        // compounds = subject -> root; combinators[i] links compounds[i] to the left.
        var result = new System.Text.StringBuilder();
        for (int i = compounds.Count - 1; i >= 0; i--)
        {
            result.Append(compounds[i]);
            if (i > 0)
                result.Append(combinators[i - 1]);
        }
        return result.ToString();
    }

    private static string RelationToText(CssSelectorRelation r) => r switch
    {
        CssSelectorRelation.Descendant => " ",
        CssSelectorRelation.Child => " > ",
        CssSelectorRelation.DirectAdjacent => " + ",
        CssSelectorRelation.IndirectAdjacent => " ~ ",
        _ => " "
    };
}