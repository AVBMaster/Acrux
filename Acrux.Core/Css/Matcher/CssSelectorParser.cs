using Acrux.Core.Css.Tokenizer;

namespace Acrux.Core.Css.Matcher;

/// <summary>
/// Parses CSS selector strings into CssSelector chains, mirroring Blink's CSSSelectorParser.
/// </summary>
public class CssSelectorParser
{
    public static List<CssSelector> ParseSelectorList(string selectorText) =>
        ParseSelectorList(new CssParserTokenStream(selectorText));

    /// <summary>Parse a comma-separated selector list out of a stream the caller is already
    /// reading. A list in which any member is a selector the grammar refuses is refused as a whole:
    /// the reference engine does not keep the siblings of a bad one, it drops the rule (measured:
    /// '#p, bogus!!!' and '#p, *|' both leave a sheet with nothing in it).</summary>
    public static List<CssSelector> ParseSelectorList(CssParserTokenStream stream)
    {
        var selectors = new List<CssSelector>();
        if (!ReadSelectorList(stream, selectors))
            selectors.Clear();
        return selectors;
    }

    /// <summary>Read a selector list and say whether all of it was read. The answer has to include
    /// the tokens that were left behind and not just the members: a list read from inside an
    /// at-rule prelude cannot otherwise tell a trailing '!!!' from the '{' of the rule body,
    /// and both have to be distinguishable because one of them drops the rule.</summary>
    public static bool ReadSelectorList(CssParserTokenStream stream, List<CssSelector> selectors)
    {
        selectors.Clear();
        var first = ParseComplexSelector(stream);
        if (first == null) return false;
        selectors.Add(first);
        while (stream.Current.Type == CssTokenType.CommaToken)
        {
            stream.Next();
            var next = ParseComplexSelector(stream);
            if (next == null) return false;
            selectors.Add(next);
        }
        return IsSelectorListBoundary(stream.Current.Type);
    }

    private static bool IsSelectorListBoundary(CssTokenType type) => type switch
    {
        CssTokenType.EofToken or CssTokenType.CommaToken or CssTokenType.LeftBraceToken or
            CssTokenType.RightBraceToken or CssTokenType.SemicolonToken or
            CssTokenType.WhitespaceToken or CssTokenType.CommentToken => true,
        _ => false,
    };

    /// <summary>Settle the namespace part of every simple selector a sheet holds against that
    /// sheet's '@namespace' declarations, the way CSS Namespaces 3 asks and the reference engine
    /// was measured to do. One compound makes one decision: the prefix its type selector was
    /// written with governs the class, the id and the plain attribute beside it and the list inside
    /// its <c>:is()</c>, while a compound that writes nothing takes the sheet's default address —
    /// and a sheet with no default leaves its selectors settled by name alone, which is what an HTML
    /// document needs (measured: with a default of 'other', '<c>*|div.c</c>' styles an HTML element
    /// and 'div.c', '.c' and 'div:is(.c)' do not). Returns whether any selector names a prefix the
    /// sheet never declared — such a selector takes its whole rule down, the other selectors of its
    /// list with it (measured: 'zz|div, #x { ... }' leaves the sheet with nothing in it).</summary>
    public static bool ApplyNamespaces(IEnumerable<CssSelector> selectors, string? defaultUri,
        IReadOnlyDictionary<string, string>? prefixes)
    {
        bool invalid = false;
        foreach (var head in selectors) invalid |= ApplyChain(head, null, defaultUri, prefixes);
        return invalid;
    }

    /// <summary>Walk one complex selector from the compound the rule applies to toward its
    /// ancestors, settling each compound. 'inherited' is the prefix the enclosing compound was
    /// written with: it reaches the lists inside that compound's functional pseudos, and stops at
    /// the combinator, so 'div .c' asks the class for the default even after '*|div:is(...)'.
    /// A written prefix never carries over a combinator (measured: '*|div .c' matches nothing in a
    /// sheet whose default is another namespace).</summary>
    private static bool ApplyChain(CssSelector? node, string? inherited, string? defaultUri,
        IReadOnlyDictionary<string, string>? prefixes)
    {
        bool invalid = false;
        for (var head = node; head != null;)
        {
            var compound = new List<CssSelector>();
            var cursor = head;
            // The members of one compound are joined by 'SubSelector', and the link that leaves a
            // compound has had its own relation overwritten by the combinator that follows it.
            while (true)
            {
                compound.Add(cursor);
                if (cursor.Relation != CssSelectorRelation.SubSelector || cursor.Next == null) break;
                cursor = cursor.Next;
            }
            head = cursor.Next;
            invalid |= ApplyCompound(compound, inherited, defaultUri, prefixes);
        }
        return invalid;
    }

    private static bool ApplyCompound(List<CssSelector> compound, string? inherited,
        string? defaultUri, IReadOnlyDictionary<string, string>? prefixes)
    {
        bool invalid = false;
        var typePart = compound.Find(m => m.MatchType == CssSelectorMatchType.Tag);
        string? written = typePart?.Namespace ?? inherited;
        var (requirement, uri, bad) = ResolveNamespace(written, false, defaultUri, prefixes);
        invalid |= bad;
        // An any-prefix only has to be printed when the sheet gives its bare selectors a default to
        // differ from: '*|div' reads 'div' from a sheet that declares no default and ' *|div' — with
        // the bar — from one that does, because dropping it there would name another element
        // (measured both, parsed and inserted alike).
        if (typePart != null && written == "*" && defaultUri != null)
            typePart.PrintsAnyNamespacePrefix = true;

        foreach (var member in compound)
        {
            if (member.SelectorList != null)
                foreach (var inner in member.SelectorList)
                    invalid |= ApplyChain(inner, written, defaultUri, prefixes);

            if (!IsNamespaceGated(member.MatchType)) continue;
            if (member.MatchType == CssSelectorMatchType.Tag)
            {
                member.NamespaceRequirement = requirement;
                member.NamespaceUri = uri;
                continue;
            }

            // An attribute written with a prefix of its own answers for itself; every other part of
            // the compound takes the answer the type selector wrote.
            if (IsAttribute(member.MatchType) && member.Namespace != null)
            {
                var (attrReq, attrUri, attrBad) =
                    ResolveNamespace(member.Namespace, true, defaultUri, prefixes);
                member.NamespaceRequirement = attrReq;
                member.NamespaceUri = attrUri;
                invalid |= attrBad;
                continue;
            }
            member.NamespaceRequirement = requirement;
            member.NamespaceUri = uri;
        }
        return invalid;
    }

    /// <summary>Which simple selectors a namespace decision is recorded on. Every part of a
    /// compound takes it, and a compound whose only part is a pseudo-class or a pseudo-element is
    /// gated just as one that starts with a type selector is: in a sheet that gives its selectors a
    /// default, ':not(.c)' matches nothing at all in an HTML document (measured), because the
    /// element the compound names has to live in that default.</summary>
    private static bool IsNamespaceGated(CssSelectorMatchType matchType) => true;

    private static bool IsAttribute(CssSelectorMatchType matchType) =>
        matchType is >= CssSelectorMatchType.AttributeExact and <= CssSelectorMatchType.AttributeEnd;

    /// <summary>Settle one written namespace part: null for nothing written (the sheet's default,
    /// or nothing at all when the sheet declares none), <c>"*"</c> for the any-namespace bar,
    /// <c>""</c> for a bare bar and a name for a prefix. Say, as well, whether the answer is one
    /// the sheet cannot support — a prefix it never declares — which takes the rule down.</summary>
    private static (CssNamespaceRequirement Requirement, string? Uri, bool Invalid) ResolveNamespace(
        string? written, bool attribute, string? defaultUri,
        IReadOnlyDictionary<string, string>? prefixes)
    {
        if (written == null)
            return defaultUri == null
                ? (CssNamespaceRequirement.Unchecked, null, false)
                : (CssNamespaceRequirement.Uri, defaultUri, false);

        if (written == "*") return (CssNamespaceRequirement.Any, null, false);

        if (written.Length == 0)
            // The bar with nothing in front of it says 'no namespace'. An HTML element always has
            // one, so a type selector written that way matches nothing; an HTML attribute never has
            // one, so the same part in front of an attribute is the address the attribute has
            // (measured both, in a document whose elements are in the XHTML namespace).
            return attribute ? (CssNamespaceRequirement.Any, null, false)
                             : (CssNamespaceRequirement.None, null, false);

        bool declared = prefixes != null && prefixes.ContainsKey(written);
        if (attribute)
            // An attribute has no namespace of its own, so a name in front of one says something no
            // attribute can be: the selector survives only when the sheet declares the name, and
            // then it matches nothing (measured: '[q|a]' in a sheet declaring q is kept, prints as
            // written and leaves the element unstyled; '[zz|a]' takes its rule down).
            return (CssNamespaceRequirement.CannotMatch, null, !declared);

        if (prefixes != null && prefixes.TryGetValue(written, out var uri))
            return (CssNamespaceRequirement.Uri, uri, false);

        return (CssNamespaceRequirement.CannotMatch, null, true);
    }

    public static CssSelector? ParseComplexSelector(CssParserTokenStream stream)
    {
        var compounds = new List<CssSelector?>();
        var combinators = new List<CssSelectorRelation>();

        var first = ParseCompoundSelector(stream);
        if (first == null) return null;
        compounds.Add(first);

        while (true)
        {
            var combinator = ParseCombinator(stream);
            if (combinator == CssSelectorRelation.SubSelector)
                break;

            var nextCompound = ParseCompoundSelector(stream, out bool broke);
            // A compound that read tokens and produced nothing is a selector the grammar does not
            // have: '* | div' reads the bar of '| div', finds a space after it, and stops — and the
            // rule goes, rather than surviving as the descendant selector '*' (measured).
            if (broke) return null;
            if (nextCompound == null) break;

            compounds.Add(nextCompound);
            combinators.Add(combinator);
        }

        if (compounds.Count == 1)
        {
            // The chain walks head (leftmost simple) → tail via SubSelector links,
            // so the terminator must be the compound's LAST simple selector;
            // marking the head would make 'table.it' match every table.
            TailOf(first).IsLastInComplexSelector = true;
            return first;
        }

        // A complex selector is matched right-to-left: the RIGHTMOST compound's
        // head simple selector is the entry point (the element the rule applies
        // to). Within a compound, simple selectors are linked by SubSelector; the
        // last simple selector of a compound carries the combinator Relation and
        // its Next points at the LEFT (ancestor) compound's head. The chain ends
        // (IsLastInComplexSelector) on the leftmost compound's tail. The previous
        // code instead made the LEFTMOST compound the head and pointed Next
        // rightward, so MatchSelector checked the ancestor's type/class against
        // the child element and complex selectors like 'A > B' never matched.
        CssSelector? entry = null;
        CssSelector? leftmostTail = null;

        for (int i = compounds.Count - 1; i >= 0; i--)
        {
            var head = compounds[i]!;
            var tail = head;
            var cursor = head;
            while (cursor.Next != null && cursor.Relation == CssSelectorRelation.SubSelector)
            {
                tail = cursor.Next;
                cursor = cursor.Next;
            }

            if (entry == null)
            {
                entry = head;
            }
            else
            {
                // Attach this compound to the previous (rightwards) compound's tail
                // via the combinator that separated them.
                var rightCompoundHead = compounds[i + 1]!;
                var rightTail = TailOf(rightCompoundHead);
                rightTail.Next = head;
                rightTail.Relation = combinators[i];
            }

            if (i == 0)
                leftmostTail = tail;
        }

        entry!.IsLastInComplexSelector = false;
        leftmostTail!.IsLastInComplexSelector = true;

        return entry;
    }

    /// <summary>The last simple selector in a compound (whose Next is null).</summary>
    private static CssSelector TailOf(CssSelector head)
    {
        var cursor = head;
        while (cursor.Next != null && cursor.Relation == CssSelectorRelation.SubSelector)
            cursor = cursor.Next;
        return cursor;
    }

    private static CssSelector? ParseCompoundSelector(CssParserTokenStream stream) =>
        ParseCompoundSelector(stream, out _);

    /// <summary>Read one compound selector. 'broke' says the compound was not merely absent but
    /// unreadable: tokens were consumed before the parse stopped, which is the difference between
    /// the end of a complex selector and a selector the grammar refuses.</summary>
    private static CssSelector? ParseCompoundSelector(CssParserTokenStream stream, out bool broke)
    {
        int start = stream.Offset;
        broke = false;
        SkipWhitespace(stream);

        if (stream.Current.Type == CssTokenType.CommaToken ||
            stream.Current.Type == CssTokenType.RightBraceToken ||
            stream.Current.Type == CssTokenType.EofToken ||
            (stream.Current.Type == CssTokenType.DelimiterToken && stream.Current.Value == ")"))
            return null;

        CssSelector? result = null;
        CssSelector? last = null;

        while (true)
        {
            // A compound starts with its type selector (CSS Selectors 4 §9): '[a]div' and '[a]*' are
            // not compounds the grammar has, and the rule goes with them (measured).
            if (result != null && StartsTypeSelector(stream)) break;

            var simple = ParseSimpleSelector(stream, out bool refused);
            // A simple selector that read its tokens and then found the grammar has no room for them
            // refuses the whole compound: ':nth-child(abc)' is not 'q' with something unparseable
            // beside it, and keeping the 'q' would style every q on the page (measured: the
            // reference engine leaves the rule out of the sheet entirely).
            if (refused) { result = null; break; }
            if (simple == null) break;

            if (result == null)
            {
                result = simple;
                last = simple;
            }
            else
            {
                last!.Next = simple;
                simple.Relation = CssSelectorRelation.SubSelector;
                last = simple;
            }
        }

        broke = result == null && stream.Offset > start;
        return result;
    }

    /// <summary>Read one simple selector. 'refused' says a pseudo-class consumed its argument and the
    /// argument is not one the grammar accepts — which is a different answer from 'there is no simple
    /// selector here', because only the first one takes the rule down.</summary>
    private static CssSelector? ParseSimpleSelector(CssParserTokenStream stream, out bool refused)
    {
        refused = false;
        if (stream.Current.Type == CssTokenType.EofToken ||
            stream.Current.Type == CssTokenType.CommaToken ||
            stream.Current.Type == CssTokenType.RightBraceToken)
            return null;

        // Whitespace or a combinator terminates the current compound. Peek without
        // consuming so ParseComplexSelector can still read the combinator from the
        // stream (a simple selector list never contains whitespace).
        if (stream.Current.Type == CssTokenType.WhitespaceToken ||
            stream.Current.Type == CssTokenType.CommentToken ||
            IsCombinatorNext(stream))
            return null;

        var token = stream.Current;

        if (token.Type == CssTokenType.DelimiterToken && token.Value == "*")
        {
            // A star in front of a bar is not a universal selector but the namespace prefix that
            // means 'any' (CSS Namespaces 3 §4), so the pair has to be read together: '*|div' is
            // a type of any namespace and '*|*' a universal of any namespace, which is what lets
            // '*|*' paint an HTML element from a sheet that declares another default (measured).
            if (IsBar(stream.LookAhead()))
            {
                stream.Next();
                stream.Next();
                string localName = "*";
                if (stream.Current.Type == CssTokenType.IdentToken)
                {
                    localName = stream.Current.Value;
                    stream.Next();
                }
                else if (stream.Current.Type == CssTokenType.DelimiterToken && stream.Current.Value == "*")
                    stream.Next();
                else return null;
                return new CssSelector
                {
                    MatchType = CssSelectorMatchType.Tag,
                    TagName = localName.ToLowerInvariant(),
                    Namespace = "*"
                };
            }
            stream.Next();
            return new CssSelector { MatchType = CssSelectorMatchType.Tag, TagName = "*" };
        }

        if (token.Type == CssTokenType.HashToken)
        {
            stream.Next();
            return new CssSelector
            {
                MatchType = CssSelectorMatchType.Id,
                Value = token.Value
            };
        }

        if (token.Type == CssTokenType.DelimiterToken && token.Value == ".")
        {
            stream.Next();
            if (stream.Current.Type == CssTokenType.IdentToken)
            {
                var cls = stream.Current.Value;
                stream.Next();
                return new CssSelector
                {
                    MatchType = CssSelectorMatchType.Class,
                    Value = cls
                };
            }
            return null;
        }

        if (token.Type == CssTokenType.ColonToken)
        {
            return ParsePseudoSelector(stream, out refused);
        }

        if (token.Type == CssTokenType.LeftSquareBracketToken)
        {
            return ParseAttributeSelector(stream);
        }

        // Only an ident, or a bar that opens the 'no namespace' spelling, can start a type selector.
        // Reading any other delimiter as a name made 'bogus!!!' a selector of four parts and left a
        // rule like '#p, bogus!!!' in the sheet, where the reference engine drops the whole rule.
        if (token.Type == CssTokenType.IdentToken ||
            (token.Type == CssTokenType.DelimiterToken && token.Value == "|"))
        {
            string tagName = token.Value;
            string? ns = null;

            if (token.Type == CssTokenType.DelimiterToken && token.Value == "|")
            {
                // The bar on its own says 'no namespace', which is not the same thing as any
                // namespace: an HTML element is always in one, so '|div' matches nothing here
                // (measured, with and without an '@namespace' in front of it).
                ns = "";
                stream.Next();
                if (stream.Current.Type == CssTokenType.IdentToken)
                {
                    tagName = stream.Current.Value;
                    stream.Next();
                }
                else if (stream.Current.Type == CssTokenType.DelimiterToken && stream.Current.Value == "*")
                {
                    tagName = "*";
                    stream.Next();
                }
                else return null;
            }
            else if (stream.LookAhead().Type == CssTokenType.DelimiterToken &&
                     stream.LookAhead().Value == "|")
            {
                // A single bar is a delimiter token; the column token is the '||' of a failed
                // selector, so this is the only shape a written prefix can arrive in. Nothing after
                // the bar is not a universal selector but a selector the grammar refuses: 'q|' and
                // 'q| div' take their whole rule down (measured — and the space matters, because
                // 'q |div' is the type 'q' followed by the compound '|div', which parses).
                ns = token.Value;
                stream.Next();
                stream.Next();
                if (stream.Current.Type == CssTokenType.IdentToken)
                {
                    tagName = stream.Current.Value;
                    stream.Next();
                }
                else if (stream.Current.Type == CssTokenType.DelimiterToken && stream.Current.Value == "*")
                {
                    tagName = "*";
                    stream.Next();
                }
                else return null;
            }
            else
            {
                stream.Next();
            }

            return new CssSelector
            {
                MatchType = CssSelectorMatchType.Tag,
                TagName = tagName.ToLowerInvariant(),
                Namespace = ns
            };
        }

        return null;
    }

    /// <summary>Read a pseudo-class or pseudo-element. 'refused' says the name was read, its argument
    /// was read, and the argument is one the grammar does not have — the case that takes the whole
    /// rule down rather than leaving the selector beside it.</summary>
    private static CssSelector? ParsePseudoSelector(CssParserTokenStream stream, out bool refused)
    {
        refused = false;
        stream.Next();
        bool isPseudoElement = false;

        if (stream.Current.Type == CssTokenType.ColonToken)
        {
            isPseudoElement = true;
            stream.Next();
        }

        if (stream.Current.Type != CssTokenType.IdentToken &&
            stream.Current.Type != CssTokenType.FunctionToken)
            return null;

        string name = stream.Current.Type == CssTokenType.IdentToken
            ? stream.Current.Value.ToLowerInvariant()
            : stream.Current.FunctionName.ToLowerInvariant();

        var pseudoType = StringToPseudoType(name, isPseudoElement);
        string? argument = null;
        List<CssSelector>? selectorList = null;
        bool nthOfPresent = false;

            if (stream.Current.Type == CssTokenType.FunctionToken)
            {
                stream.Next();
                // Parse arguments
                if (PseudoHasSelectorList(pseudoType))
                {
                    if (pseudoType is CssPseudoType.NthChild or CssPseudoType.NthLastChild)
                    {
                        // '<an-plus-b> [ of <complex-selector-list> ]?' (CSS Selectors 4 §6.6.2).
                        // The pattern keeps the page's own spelling because that spelling is what
                        // the grammar reads: see CollectRawUntilParen.
                        var argText = CollectRawUntilParen(stream);
                        var ofAt = IndexOfNthOfKeyword(argText);
                        argument = CanonicalNthArgument((ofAt >= 0 ? argText[..ofAt] : argText).Trim());
                        if (ofAt >= 0)
                        {
                            nthOfPresent = true;
                            selectorList = ParseSelectorList(argText[(ofAt + "of".Length)..]);
                        }
                    }
                    else
                    {
                        selectorList = new List<CssSelector>();
                        var argText = CollectTokensUntilParen(stream);
                        selectorList = ParseSelectorList(argText);
                    }
                }
            else if (pseudoType is CssPseudoType.NthOfType or CssPseudoType.NthLastOfType
                     && stream.Current.Type != CssTokenType.RightParenthesisToken)
            {
                argument = CanonicalNthArgument(CollectRawUntilParen(stream).Trim());
            }
            else
            {
                if (stream.Current.Type == CssTokenType.RightParenthesisToken)
                {
                    argument = "";
                }
                else
                {
                    argument = CollectTokensUntilParen(stream);
                }
            }
        }
        else
        {
            stream.Next();
        }

        // An 'An+B' the integer grammar refuses is not a pattern, and a pseudo-class with no pattern
        // in it is not a selector: the reference engine takes the whole rule down with it (measured,
        // through insertRule, where each of ':nth-child(5n+)', ':nth-child(+ 3)', ':nth-child(3 n)',
        // ':nth-child(2.5n)', ':nth-child()' and ':nth-child(2n+1 of (b))' is refused outright).
        if (pseudoType is CssPseudoType.NthChild or CssPseudoType.NthLastChild
            or CssPseudoType.NthOfType or CssPseudoType.NthLastOfType)
        {
            if (!SelectorChecker.TryParseAnPlusB(argument, out _, out _))
            {
                refused = true;
                return null;
            }
            // An 'of' whose list reads as nothing is the same refusal (measured: '2n+1 of ' and
            // ' of b' are both invalid, so the keyword is only kept with a list behind it).
            if (nthOfPresent && selectorList is not { Count: > 0 })
            {
                refused = true;
                return null;
            }
        }

        return new CssSelector
        {
            MatchType = isPseudoElement ? CssSelectorMatchType.PseudoElement : CssSelectorMatchType.PseudoClass,
            PseudoType = pseudoType,
            Argument = argument,
            SelectorList = selectorList,
            NthOfPresent = nthOfPresent
        };
    }

    /// <summary>Where the 'of' of an ':nth-child(An+B of S)' begins in the argument, or -1 when the
    /// argument is a pattern on its own. The keyword is a token, so '2n+1of' — which the tokenizer
    /// reads as the single dimension '1of' — is not a pattern followed by a keyword, and a prefix
    /// the integer grammar refuses is not a pattern either; both leave the whole argument to be
    /// rejected together (measured: '2n+1of .c' and '+ 3' each take their rule down in the
    /// reference engine).</summary>
    private static int IndexOfNthOfKeyword(string argument)
    {
        var stream = new CssParserTokenStream(argument);
        while (!stream.Current.IsEof)
        {
            var token = stream.Current;
            if (token.Type == CssTokenType.WhitespaceToken)
            {
                stream.Next();
                continue;
            }
            if (token.Type == CssTokenType.IdentToken)
            {
                // The keyword itself is only read in lower case. That is the reference engine's
                // answer and not the spec's — measured: 'ODD of b' is a pattern with a list while
                // 'odd OF b' is refused outright, so the pattern's own words stay
                // case-insensitive and the separator does not.
                if (token.Value == "of")
                    return SelectorChecker.TryParseAnPlusB(argument[..stream.TokenStart], out _, out _)
                        ? stream.TokenStart : -1;
                // 'n', 'N', 'even' and 'odd' are the pattern's own words; any other name means this
                // argument has no pattern in it at all.
                var name = token.Value.ToLowerInvariant();
                if (name is not ("n" or "even" or "odd")) return -1;
                stream.Next();
                continue;
            }
            if (token.Type is CssTokenType.NumberToken)
            {
                stream.Next();
                continue;
            }
            if (token.Type == CssTokenType.DimensionToken
                && token.Unit.Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                stream.Next();
                continue;
            }
            if (token.Type == CssTokenType.DelimiterToken && token.Value is "+" or "-")
            {
                stream.Next();
                continue;
            }
            return -1;
        }
        return -1;
    }

    /// <summary>The argument of a functional pseudo-class, cut out of the source and kept exactly as
    /// the page wrote it. Rebuilding it from tokens loses what a token cannot hold: the reference
    /// engine reads ':nth-child(0n+3)' as a pattern with b=3, while the tokenizer hands that over as
    /// the dimension '0n' followed by a *signed number*, and a number serialized from its value is
    /// '3' — glued to '0n' it is '0n3', an argument that means nothing (measured: the spaced form
    /// '0n + 3' was the one that worked, because there the sign arrives as its own token).</summary>
    private static string CollectRawUntilParen(CssParserTokenStream stream)
    {
        int start = stream.TokenStart;
        int depth = 1;
        while (depth > 0)
        {
            if (stream.Current.IsEof) break;
            if (stream.Current.Type == CssTokenType.RightParenthesisToken)
            {
                depth--;
                if (depth == 0) break;
            }
            else if (stream.Current.Type == CssTokenType.LeftParenthesisToken) depth++;
            stream.Next();
        }
        var text = stream.RawRange(start, stream.TokenStart);
        stream.Next();
        return text;
    }

    private static string CollectTokensUntilParen(CssParserTokenStream stream)
    {
        var result = new System.Text.StringBuilder();
        int depth = 1;
        while (depth > 0)
        {
            if (stream.Current.Type == CssTokenType.RightParenthesisToken)
            {
                depth--;
                if (depth == 0) break;
                result.Append(')');
            }
            else if (stream.Current.Type == CssTokenType.LeftParenthesisToken)
            {
                depth++;
                result.Append('(');
            }
            else if (stream.Current.IsEof)
            {
                break;
            }
            else
            {
                result.Append(stream.Current.ToCssText());
            }
            stream.Next();
        }
        stream.Next();
        return result.ToString();
    }

    private static CssSelector? ParseAttributeSelector(CssParserTokenStream stream)
    {
        stream.Next();
        SkipSelectorWhitespace(stream);

        // The namespace part an attribute selector may open with: '[*|att]' and '[|att]' are the two
        // that mean anything in an HTML document, and the second loses its empty prefix on the way
        // back out (measured: 'style[|type]' reads as 'style[type]'). A named prefix in front of an
        // attribute is kept here and settled against the sheet's declarations by the caller, which
        // makes the whole rule invalid when the name was never declared.
        string? attributeNamespace = ReadAttributeNamespace(stream);
        if (stream.Current.Type != CssTokenType.IdentToken)
            return null;

        string attrName = stream.Current.Value;
        stream.Next();
        SkipSelectorWhitespace(stream);

        CssSelectorMatchType matchType = CssSelectorMatchType.AttributeSet;
        string? attrValue = null;
        bool caseSensitive = true;

        if (stream.Current.Type != CssTokenType.RightSquareBracketToken)
        {
            matchType = stream.Current.Type switch
            {
                CssTokenType.IncludeMatchToken => CssSelectorMatchType.AttributeList,
                CssTokenType.DashMatchToken => CssSelectorMatchType.AttributeHyphen,
                CssTokenType.PrefixMatchToken => CssSelectorMatchType.AttributeBegin,
                CssTokenType.SuffixMatchToken => CssSelectorMatchType.AttributeEnd,
                CssTokenType.SubstringMatchToken => CssSelectorMatchType.AttributeContain,
                CssTokenType.DelimiterToken when stream.Current.Value == "=" => CssSelectorMatchType.AttributeExact,
                _ => CssSelectorMatchType.AttributeSet
            };

            if (matchType != CssSelectorMatchType.AttributeSet)
            {
                stream.Next();
                SkipSelectorWhitespace(stream);
                // Parse value
                if (stream.Current.Type == CssTokenType.IdentToken ||
                    stream.Current.Type == CssTokenType.StringToken)
                {
                    attrValue = stream.Current.Value;
                    stream.Next();
                }
                // Whitespace is legal between the value and the case-sensitivity
                // flag ("[type=\"hidden\" i]"). Not skipping it made the flag
                // unreadable, the selector never closed, and the whole rule was
                // dropped - which silently disabled every UA rule that spells the
                // flag with a space, hidden inputs included.
                SkipSelectorWhitespace(stream);
                // Case sensitivity flag
                if (stream.Current.Type == CssTokenType.IdentToken &&
                    string.Equals(stream.Current.Value, "i", StringComparison.OrdinalIgnoreCase))
                {
                    caseSensitive = false;
                    stream.Next();
                }
                else if (stream.Current.Type == CssTokenType.IdentToken &&
                    string.Equals(stream.Current.Value, "s", StringComparison.OrdinalIgnoreCase))
                {
                    caseSensitive = true;
                    stream.Next();
                }
            }
        }

        if (stream.Current.Type == CssTokenType.RightSquareBracketToken)
            stream.Next();

        return new CssSelector
        {
            MatchType = matchType,
            AttributeName = attrName,
            AttributeValue = attrValue,
            AttributeCaseSensitive = caseSensitive,
            Namespace = attributeNamespace
        };
    }

    /// <summary>Read the <c>[ns|…]</c>, <c>[*|…]</c> or <c>[|…]</c> opening of an attribute
    /// selector, if it has one, and give back the part as it was written: the empty string for a bar
    /// with nothing in front of it, <c>"*"</c> for the any-namespace bar, a name for a prefix, and
    /// null when the bracket holds a plain name.</summary>
    private static string? ReadAttributeNamespace(CssParserTokenStream stream)
    {
        if (IsBar(stream.Current))
        {
            // A bar with nothing in front of it says 'no namespace', and an attribute has none, so
            // the part says nothing: '[|att]' reads as a plain '[att]' — which is what the printer
            // shows — and it then follows the same rule as a bare one, taking the namespace its
            // compound was written with or the sheet's default (measured: '[|data-x]' styles an HTML
            // element in a sheet that declares no default and stops styling it once the sheet gives
            // its selectors another one).
            stream.Next();
            SkipSelectorWhitespace(stream);
            return null;
        }

        if (stream.Current.Type != CssTokenType.IdentToken && !IsStar(stream.Current)) return null;
        var next = stream.LookAhead();
        if (!IsBar(next)) return null;
        string prefix = stream.Current.Value;
        stream.Next();
        stream.Next();
        SkipSelectorWhitespace(stream);
        return prefix;
    }

    /// <summary>Whether the next token opens a type selector — an ident, the bar that says 'no
    /// namespace', or the universal star. A compound may hold only one, and only at its front.</summary>
    private static bool StartsTypeSelector(CssParserTokenStream stream) => stream.Current switch
    {
        { Type: CssTokenType.IdentToken } => true,
        { Type: CssTokenType.DelimiterToken, Value: "*" or "|" } => true,
        _ => false,
    };

    private static bool IsBar(CssParserToken token) =>
        token.Type == CssTokenType.DelimiterToken && token.Value == "|";

    private static bool IsStar(CssParserToken token) =>
        token.Type == CssTokenType.DelimiterToken && token.Value == "*";

    /// <summary>Advance past the whitespace and comments that are legal inside a
    /// bracketed attribute selector.</summary>
    private static void SkipSelectorWhitespace(CssParserTokenStream stream)
    {
        while (stream.Current.Type == CssTokenType.WhitespaceToken ||
               stream.Current.Type == CssTokenType.CommentToken)
            stream.Next();
    }

    private static CssSelectorRelation ParseCombinator(CssParserTokenStream stream)
    {
        bool hadWhitespace = false;
        while (stream.Current.Type == CssTokenType.WhitespaceToken ||
               stream.Current.Type == CssTokenType.CommentToken)
        {
            hadWhitespace = true;
            stream.Next();
        }

        if (stream.Current.Type == CssTokenType.DelimiterToken)
        {
            if (stream.Current.Value == ">")
            {
                stream.Next();
                return CssSelectorRelation.Child;
            }
            if (stream.Current.Value == "+")
            {
                stream.Next();
                return CssSelectorRelation.DirectAdjacent;
            }
            if (stream.Current.Value == "~")
            {
                stream.Next();
                return CssSelectorRelation.IndirectAdjacent;
            }
        }

        // Whitespace before another compound is a descendant combinator. Trailing
        // whitespace before EOF / comma / brace is not a combinator.
        if (hadWhitespace)
        {
            if (stream.Current.IsEof || stream.Current.Type == CssTokenType.CommaToken ||
                stream.Current.Type == CssTokenType.RightBraceToken ||
                stream.Current.Type == CssTokenType.LeftBraceToken)
                return CssSelectorRelation.SubSelector;
            return CssSelectorRelation.Descendant;
        }

        return CssSelectorRelation.SubSelector;
    }

    private static void SkipWhitespace(CssParserTokenStream stream)
    {
        while (stream.Current.Type == CssTokenType.WhitespaceToken ||
               stream.Current.Type == CssTokenType.CommentToken)
            stream.Next();
    }

    /// <summary>True if the next meaningful token is a combinator (&gt;, +, ~), without consuming it.</summary>
    private static bool IsCombinatorNext(CssParserTokenStream stream)
    {
        if (stream.Current.Type == CssTokenType.DelimiterToken &&
            (stream.Current.Value == ">" || stream.Current.Value == "+" || stream.Current.Value == "~"))
            return true;

        if (stream.Current.Type == CssTokenType.WhitespaceToken ||
            stream.Current.Type == CssTokenType.CommentToken)
        {
            var la = stream.LookAhead();
            return la.Type == CssTokenType.DelimiterToken &&
                   (la.Value == ">" || la.Value == "+" || la.Value == "~");
        }
        return false;
    }

    private static bool PseudoHasSelectorList(CssPseudoType type) => type switch
    {
        CssPseudoType.Is or CssPseudoType.Where or CssPseudoType.Not or CssPseudoType.Has => true,
        CssPseudoType.NthChild or CssPseudoType.NthLastChild => true,
        CssPseudoType.HostContext => true,
        _ => false
    };

    /// <summary>The pseudo-class names the selector grammar reads, in the spelling a page uses. One
    /// table answers both directions — a name for a type and a type for a name — because the two
    /// halves of that question drifting apart is a selector that parses and then prints as something
    /// else, which is exactly what a hand-maintained second list produces.</summary>
    private static readonly (string Name, CssPseudoType Type)[] PseudoClasses =
    {
        ("active", CssPseudoType.Active),
        ("any-link", CssPseudoType.AnyLink),
        ("autofill", CssPseudoType.Autofill),
        ("blank", CssPseudoType.Blank),
        ("checked", CssPseudoType.Checked),
        ("corf", CssPseudoType.CorF),
        ("current", CssPseudoType.Current),
        ("default", CssPseudoType.Default),
        ("defined", CssPseudoType.Defined),
        ("disabled", CssPseudoType.Disabled),
        ("done", CssPseudoType.Done),
        ("drag", CssPseudoType.Drag),
        ("empty", CssPseudoType.Empty),
        ("enabled", CssPseudoType.Enabled),
        ("first-child", CssPseudoType.FirstChild),
        ("first-of-type", CssPseudoType.FirstOfType),
        ("focus", CssPseudoType.Focus),
        ("focus-visible", CssPseudoType.FocusVisible),
        ("focus-within", CssPseudoType.FocusWithin),
        ("fullscreen", CssPseudoType.Fullscreen),
        ("future", CssPseudoType.Future),
        ("has", CssPseudoType.Has),
        ("host", CssPseudoType.Host),
        ("host-context", CssPseudoType.HostContext),
        ("hover", CssPseudoType.Hover),
        ("in-range", CssPseudoType.InRange),
        ("indeterminate", CssPseudoType.Indeterminate),
        ("invalid", CssPseudoType.Invalid),
        ("is", CssPseudoType.Is),
        ("last-child", CssPseudoType.LastChild),
        ("last-of-type", CssPseudoType.LastOfType),
        ("left", CssPseudoType.Left),
        ("link", CssPseudoType.Link),
        ("modal", CssPseudoType.Modal),
        ("not", CssPseudoType.Not),
        ("nth-child", CssPseudoType.NthChild),
        ("nth-last-child", CssPseudoType.NthLastChild),
        ("nth-last-of-type", CssPseudoType.NthLastOfType),
        ("nth-of-type", CssPseudoType.NthOfType),
        ("only-child", CssPseudoType.OnlyChild),
        ("only-of-type", CssPseudoType.OnlyOfType),
        ("open", CssPseudoType.Open),
        ("optional", CssPseudoType.Optional),
        ("out-of-range", CssPseudoType.OutOfRange),
        ("past", CssPseudoType.Past),
        ("paused", CssPseudoType.Paused),
        ("picture-in-picture", CssPseudoType.PictureInPicture),
        ("placeholder-shown", CssPseudoType.PlaceholderShown),
        ("playing", CssPseudoType.Playing),
        ("popover-open", CssPseudoType.PopoverOpen),
        ("read-only", CssPseudoType.ReadOnly),
        ("read-write", CssPseudoType.ReadWrite),
        ("required", CssPseudoType.Required),
        ("right", CssPseudoType.Right),
        ("root", CssPseudoType.Root),
        ("scope", CssPseudoType.Scope),
        ("state", CssPseudoType.State),
        ("target", CssPseudoType.Target),
        ("unresolved", CssPseudoType.Unresolved),
        ("user-invalid", CssPseudoType.UserInvalid),
        ("user-valid", CssPseudoType.UserValid),
        ("valid", CssPseudoType.Valid),
        ("visited", CssPseudoType.Visited),
        ("where", CssPseudoType.Where),
        ("window-inactive", CssPseudoType.WindowInactive),
    };

    /// <summary>The pseudo-element names, in the same table-for-both-directions shape as
    /// <see cref="PseudoClasses"/>.</summary>
    private static readonly (string Name, CssPseudoType Type)[] PseudoElements =
    {
        ("before", CssPseudoType.Before),
        ("after", CssPseudoType.After),
        ("backdrop", CssPseudoType.Backdrop),
        ("file-selector-button", CssPseudoType.FileSelectorButton),
        ("first-letter", CssPseudoType.FirstLetter),
        ("first-line", CssPseudoType.FirstLine),
        ("grammar-error", CssPseudoType.GrammarError),
        ("marker", CssPseudoType.Marker),
        ("placeholder", CssPseudoType.Placeholder),
        ("selection", CssPseudoType.Selection),
        ("spelling-error", CssPseudoType.SpellingError),
        ("target-text", CssPseudoType.TargetText),
        ("cue", CssPseudoType.Cue),
        ("cue-region", CssPseudoType.CueRegion),
        ("part", CssPseudoType.Part),
        ("slotted", CssPseudoType.Slotted),
        ("highlight", CssPseudoType.Highlight),
        ("view-transition", CssPseudoType.ViewTransition),
        ("view-transition-group", CssPseudoType.ViewTransitionGroup),
        ("view-transition-image-pair", CssPseudoType.ViewTransitionImagePair),
        ("view-transition-new", CssPseudoType.ViewTransitionNew),
        ("view-transition-old", CssPseudoType.ViewTransitionOld),
        ("scrollbar", CssPseudoType.Scrollbar),
        ("scrollbar-button", CssPseudoType.ScrollbarButton),
        ("scrollbar-corner", CssPseudoType.ScrollbarCorner),
        ("scrollbar-thumb", CssPseudoType.ScrollbarThumb),
        ("scrollbar-track", CssPseudoType.ScrollbarTrack),
        ("scrollbar-track-piece", CssPseudoType.ScrollbarTrackPiece),
        ("resizer", CssPseudoType.Resizer),
        ("scroll-next-button", CssPseudoType.ScrollNextButton),
        ("scroll-prev-button", CssPseudoType.ScrollPrevButton),
    };

    private static readonly Dictionary<string, CssPseudoType> PseudoClassByName =
        PseudoClasses.ToDictionary(e => e.Name, e => e.Type, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CssPseudoType> PseudoElementByName =
        PseudoElements.ToDictionary(e => e.Name, e => e.Type, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<CssPseudoType, string> PseudoClassNameByType =
        PseudoClasses.ToDictionary(e => e.Type, e => e.Name);
    private static readonly Dictionary<CssPseudoType, string> PseudoElementNameByType =
        PseudoElements.ToDictionary(e => e.Type, e => e.Name);

    /// <summary>The name a pseudo-class or pseudo-element type is written with on the way out. A type
    /// the grammar has no name for — an engine-internal one — keeps the old answer, which is its
    /// enum's own lower-cased spelling.</summary>
    public static string PseudoTypeToName(CssPseudoType type, bool isPseudoElement)
    {
        var table = isPseudoElement ? PseudoElementNameByType : PseudoClassNameByType;
        return table.TryGetValue(type, out var name)
            ? name : type.ToString().ToLowerInvariant();
    }

    /// <summary>The argument of an <c>An+B</c> pseudo-class in the one spelling the engine keeps: the
    /// two numbers the pattern stands for. An argument the integer grammar refuses is left as the
    /// page wrote it, because there is no pattern to print for it.</summary>
    private static string CanonicalNthArgument(string argument) =>
        SelectorChecker.TryParseAnPlusB(argument, out int a, out int b)
            ? SelectorChecker.CanonicalAnPlusB(a, b) : argument;

    public static CssPseudoType StringToPseudoType(string name, bool isPseudoElement)
    {
        var table = isPseudoElement ? PseudoElementByName : PseudoClassByName;
        return table.TryGetValue(name, out var type) ? type : CssPseudoType.Unknown;
    }
}