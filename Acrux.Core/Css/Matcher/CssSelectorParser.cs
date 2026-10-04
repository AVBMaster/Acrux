using Acrux.Core.Css.Tokenizer;

namespace Acrux.Core.Css.Matcher;

/// <summary>
/// Parses CSS selector strings into CssSelector chains, mirroring Blink's CSSSelectorParser.
/// </summary>
public class CssSelectorParser
{
    public static List<CssSelector> ParseSelectorList(string selectorText)
    {
        var stream = new CssParserTokenStream(selectorText);
        var selectors = new List<CssSelector>();
        var current = ParseComplexSelector(stream);
        if (current != null)
        {
            selectors.Add(current);
            while (stream.Current.Type == CssTokenType.CommaToken)
            {
                stream.Next();
                var next = ParseComplexSelector(stream);
                if (next != null)
                    selectors.Add(next);
            }
        }
        return selectors;
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

            var nextCompound = ParseCompoundSelector(stream);
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

    private static CssSelector? ParseCompoundSelector(CssParserTokenStream stream)
    {
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
            var simple = ParseSimpleSelector(stream);
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

        return result;
    }

    private static CssSelector? ParseSimpleSelector(CssParserTokenStream stream)
    {
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
            return ParsePseudoSelector(stream);
        }

        if (token.Type == CssTokenType.LeftSquareBracketToken)
        {
            return ParseAttributeSelector(stream);
        }

        if (token.Type == CssTokenType.IdentToken || token.Type == CssTokenType.DelimiterToken)
        {
            string tagName = token.Value;
            string? ns = null;

            if (token.Type == CssTokenType.DelimiterToken && token.Value == "|")
            {
                ns = "*";
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
            else if (stream.LookAhead().Type == CssTokenType.ColumnToken)
            {
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
                else tagName = "*";
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

    private static CssSelector? ParsePseudoSelector(CssParserTokenStream stream)
    {
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

            if (stream.Current.Type == CssTokenType.FunctionToken)
            {
                stream.Next();
                // Parse arguments
                if (PseudoHasSelectorList(pseudoType))
                {
                    selectorList = new List<CssSelector>();
                    if (pseudoType is CssPseudoType.NthChild or CssPseudoType.NthLastChild)
                    {
                        // Parse An+B [of selector-list]
                        var argStream = new CssParserTokenStream(CollectTokensUntilParen(stream));
                        argument = ParseAnPlusB(argStream);
                        if (argStream.Current.Type == CssTokenType.IdentToken &&
                            argStream.Current.Value.Equals("of", StringComparison.OrdinalIgnoreCase))
                        {
                            // Parse selector list after "of"
                            argStream.Next();
                            var rest = new System.Text.StringBuilder();
                            while (argStream.Current.Type != CssTokenType.EofToken &&
                                   argStream.Current.Type != CssTokenType.RightParenthesisToken)
                            {
                                rest.Append(argStream.Current.ToCssText());
                                argStream.Next();
                            }
                            selectorList = ParseSelectorList(rest.ToString());
                        }
                    }
                    else
                    {
                        var argText = CollectTokensUntilParen(stream);
                        selectorList = ParseSelectorList(argText);
                    }
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

        return new CssSelector
        {
            MatchType = isPseudoElement ? CssSelectorMatchType.PseudoElement : CssSelectorMatchType.PseudoClass,
            PseudoType = pseudoType,
            Argument = argument,
            SelectorList = selectorList
        };
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

    private static string ParseAnPlusB(CssParserTokenStream stream)
    {
        var result = new System.Text.StringBuilder();
        while (stream.Current.Type != CssTokenType.EofToken && stream.Current.Type != CssTokenType.RightParenthesisToken)
        {
            if (stream.Current.Type == CssTokenType.WhitespaceToken)
                result.Append(' ');
            else
                result.Append(stream.Current.ToCssText());
            stream.Next();
        }
        return result.ToString().Trim();
    }

    private static CssSelector? ParseAttributeSelector(CssParserTokenStream stream)
    {
        stream.Next();
        SkipSelectorWhitespace(stream);
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
            AttributeCaseSensitive = caseSensitive
        };
    }

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

    public static CssPseudoType StringToPseudoType(string name, bool isPseudoElement)
    {
        if (isPseudoElement)
        {
            return name switch
            {
                "before" => CssPseudoType.Before,
                "after" => CssPseudoType.After,
                "backdrop" => CssPseudoType.Backdrop,
                "file-selector-button" => CssPseudoType.FileSelectorButton,
                "first-letter" => CssPseudoType.FirstLetter,
                "first-line" => CssPseudoType.FirstLine,
                "grammar-error" => CssPseudoType.GrammarError,
                "marker" => CssPseudoType.Marker,
                "placeholder" => CssPseudoType.Placeholder,
                "selection" => CssPseudoType.Selection,
                "spelling-error" => CssPseudoType.SpellingError,
                "target-text" => CssPseudoType.TargetText,
                "cue" => CssPseudoType.Cue,
                "cue-region" => CssPseudoType.CueRegion,
                "part" => CssPseudoType.Part,
                "slotted" => CssPseudoType.Slotted,
                "highlight" => CssPseudoType.Highlight,
                "view-transition" => CssPseudoType.ViewTransition,
                "view-transition-group" => CssPseudoType.ViewTransitionGroup,
                "view-transition-image-pair" => CssPseudoType.ViewTransitionImagePair,
                "view-transition-new" => CssPseudoType.ViewTransitionNew,
                "view-transition-old" => CssPseudoType.ViewTransitionOld,
                "scrollbar" => CssPseudoType.Scrollbar,
                "scrollbar-button" => CssPseudoType.ScrollbarButton,
                "scrollbar-corner" => CssPseudoType.ScrollbarCorner,
                "scrollbar-thumb" => CssPseudoType.ScrollbarThumb,
                "scrollbar-track" => CssPseudoType.ScrollbarTrack,
                "scrollbar-track-piece" => CssPseudoType.ScrollbarTrackPiece,
                "resizer" => CssPseudoType.Resizer,
                "scroll-next-button" => CssPseudoType.ScrollNextButton,
                "scroll-prev-button" => CssPseudoType.ScrollPrevButton,
                _ => CssPseudoType.Unknown
            };
        }

        return name switch
        {
            "active" => CssPseudoType.Active,
            "any-link" => CssPseudoType.AnyLink,
            "autofill" => CssPseudoType.Autofill,
            "blank" => CssPseudoType.Blank,
            "checked" => CssPseudoType.Checked,
            "corf" => CssPseudoType.CorF,
            "current" => CssPseudoType.Current,
            "default" => CssPseudoType.Default,
            "defined" => CssPseudoType.Defined,
            "disabled" => CssPseudoType.Disabled,
            "done" => CssPseudoType.Done,
            "drag" => CssPseudoType.Drag,
            "empty" => CssPseudoType.Empty,
            "enabled" => CssPseudoType.Enabled,
            "first-child" => CssPseudoType.FirstChild,
            "first-of-type" => CssPseudoType.FirstOfType,
            "focus" => CssPseudoType.Focus,
            "focus-visible" => CssPseudoType.FocusVisible,
            "focus-within" => CssPseudoType.FocusWithin,
            "fullscreen" => CssPseudoType.Fullscreen,
            "future" => CssPseudoType.Future,
            "has" => CssPseudoType.Has,
            "host" => CssPseudoType.Host,
            "host-context" => CssPseudoType.HostContext,
            "hover" => CssPseudoType.Hover,
            "in-range" => CssPseudoType.InRange,
            "indeterminate" => CssPseudoType.Indeterminate,
            "invalid" => CssPseudoType.Invalid,
            "is" => CssPseudoType.Is,
            "last-child" => CssPseudoType.LastChild,
            "last-of-type" => CssPseudoType.LastOfType,
            "left" => CssPseudoType.Left,
            "link" => CssPseudoType.Link,
            "modal" => CssPseudoType.Modal,
            "not" => CssPseudoType.Not,
            "nth-child" => CssPseudoType.NthChild,
            "nth-last-child" => CssPseudoType.NthLastChild,
            "nth-last-of-type" => CssPseudoType.NthLastOfType,
            "nth-of-type" => CssPseudoType.NthOfType,
            "only-child" => CssPseudoType.OnlyChild,
            "only-of-type" => CssPseudoType.OnlyOfType,
            "open" => CssPseudoType.Open,
            "optional" => CssPseudoType.Optional,
            "out-of-range" => CssPseudoType.OutOfRange,
            "past" => CssPseudoType.Past,
            "paused" => CssPseudoType.Paused,
            "picture-in-picture" => CssPseudoType.PictureInPicture,
            "placeholder-shown" => CssPseudoType.PlaceholderShown,
            "playing" => CssPseudoType.Playing,
            "popover-open" => CssPseudoType.PopoverOpen,
            "read-only" => CssPseudoType.ReadOnly,
            "read-write" => CssPseudoType.ReadWrite,
            "required" => CssPseudoType.Required,
            "right" => CssPseudoType.Right,
            "root" => CssPseudoType.Root,
            "scope" => CssPseudoType.Scope,
            "state" => CssPseudoType.State,
            "target" => CssPseudoType.Target,
            "unresolved" => CssPseudoType.Unresolved,
            "user-invalid" => CssPseudoType.UserInvalid,
            "user-valid" => CssPseudoType.UserValid,
            "valid" => CssPseudoType.Valid,
            "visited" => CssPseudoType.Visited,
            "where" => CssPseudoType.Where,
            "window-inactive" => CssPseudoType.WindowInactive,
            _ => CssPseudoType.Unknown
        };
    }
}