namespace Acrux.Core.Dom.Parser;

internal partial class HtmlTokenizer
{
    // ------------------------------------------------------------------------
    // NextTokenImpl -- direct port of the state machine in html_tokenizer.cc.
    // Fix #2: every "advance past non-newline" stores the returned char into cc.
    // Fix #3: the value returned after the switch is based on the token type.
    // Fix #4: TagOpenState re-reads the current character on entry.
    // ------------------------------------------------------------------------
    private bool NextTokenImpl(SegmentedString source)
    {
        if (_bufferedEndTagName.Length != 0 && !IsEndTagBufferingState(_state))
        {
            // FIXME: Should call FlushBufferedEndTag().
            // We started an end tag during our last iteration.
            _token.BeginEndTag(_bufferedEndTagName.ToString());
            _bufferedEndTagName.Clear();
            _appropriateEndTagName.Clear();
            _temporaryBuffer.Clear();
            if (_state == State.DataState)
            {
                // We're back in the data state, so we must be done with the tag.
                return true;
            }
        }

        char cc;
        if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
            return HaveBufferedCharacterToken();

        switch (_state)
        {
            case State.DataState: goto DataState;
            case State.CharacterReferenceInDataState: goto CharacterReferenceInDataState;
            case State.RcdataState: goto RcdataState;
            case State.CharacterReferenceInRcdataState: goto CharacterReferenceInRcdataState;
            case State.RawtextState: goto RawtextState;
            case State.ScriptDataState: goto ScriptDataState;
            case State.PlaintextState: goto PlaintextState;
            case State.TagOpenState: goto TagOpenState;
            case State.EndTagOpenState: goto EndTagOpenState;
            case State.TagNameState: goto TagNameState;
            case State.RcdataLessThanSignState: goto RcdataLessThanSignState;
            case State.RcdataEndTagOpenState: goto RcdataEndTagOpenState;
            case State.RcdataEndTagNameState: goto RcdataEndTagNameState;
            case State.RawtextLessThanSignState: goto RawtextLessThanSignState;
            case State.RawtextEndTagOpenState: goto RawtextEndTagOpenState;
            case State.RawtextEndTagNameState: goto RawtextEndTagNameState;
            case State.ScriptDataLessThanSignState: goto ScriptDataLessThanSignState;
            case State.ScriptDataEndTagOpenState: goto ScriptDataEndTagOpenState;
            case State.ScriptDataEndTagNameState: goto ScriptDataEndTagNameState;
            case State.ScriptDataEscapeStartState: goto ScriptDataEscapeStartState;
            case State.ScriptDataEscapeStartDashState: goto ScriptDataEscapeStartDashState;
            case State.ScriptDataEscapedState: goto ScriptDataEscapedState;
            case State.ScriptDataEscapedDashState: goto ScriptDataEscapedDashState;
            case State.ScriptDataEscapedDashDashState: goto ScriptDataEscapedDashDashState;
            case State.ScriptDataEscapedLessThanSignState: goto ScriptDataEscapedLessThanSignState;
            case State.ScriptDataEscapedEndTagOpenState: goto ScriptDataEscapedEndTagOpenState;
            case State.ScriptDataEscapedEndTagNameState: goto ScriptDataEscapedEndTagNameState;
            case State.ScriptDataDoubleEscapeStartState: goto ScriptDataDoubleEscapeStartState;
            case State.ScriptDataDoubleEscapedState: goto ScriptDataDoubleEscapedState;
            case State.ScriptDataDoubleEscapedDashState: goto ScriptDataDoubleEscapedDashState;
            case State.ScriptDataDoubleEscapedDashDashState: goto ScriptDataDoubleEscapedDashDashState;
            case State.ScriptDataDoubleEscapedLessThanSignState: goto ScriptDataDoubleEscapedLessThanSignState;
            case State.ScriptDataDoubleEscapeEndState: goto ScriptDataDoubleEscapeEndState;
            case State.BeforeAttributeNameState: goto BeforeAttributeNameState;
            case State.AttributeNameState: goto AttributeNameState;
            case State.AfterAttributeNameState: goto AfterAttributeNameState;
            case State.BeforeAttributeValueState: goto BeforeAttributeValueState;
            case State.AttributeValueDoubleQuotedState: goto AttributeValueDoubleQuotedState;
            case State.AttributeValueSingleQuotedState: goto AttributeValueSingleQuotedState;
            case State.AttributeValueUnquotedState: goto AttributeValueUnquotedState;
            case State.CharacterReferenceInAttributeValueState: goto CharacterReferenceInAttributeValueState;
            case State.AfterAttributeValueQuotedState: goto AfterAttributeValueQuotedState;
            case State.SelfClosingStartTagState: goto SelfClosingStartTagState;
            case State.BogusCommentState: goto BogusCommentState;
            case State.ContinueBogusCommentState: goto ContinueBogusCommentState;
            case State.MarkupDeclarationOpenState: goto MarkupDeclarationOpenState;
            case State.CommentStartState: goto CommentStartState;
            case State.CommentStartDashState: goto CommentStartDashState;
            case State.CommentState: goto CommentState;
            case State.CommentEndDashState: goto CommentEndDashState;
            case State.CommentEndState: goto CommentEndState;
            case State.CommentEndBangState: goto CommentEndBangState;
            case State.DoctypeState: goto DoctypeState;
            case State.BeforeDoctypeNameState: goto BeforeDoctypeNameState;
            case State.DoctypeNameState: goto DoctypeNameState;
            case State.AfterDoctypeNameState: goto AfterDoctypeNameState;
            case State.AfterDoctypePublicKeywordState: goto AfterDoctypePublicKeywordState;
            case State.BeforeDoctypePublicIdentifierState: goto BeforeDoctypePublicIdentifierState;
            case State.DoctypePublicIdentifierDoubleQuotedState: goto DoctypePublicIdentifierDoubleQuotedState;
            case State.DoctypePublicIdentifierSingleQuotedState: goto DoctypePublicIdentifierSingleQuotedState;
            case State.AfterDoctypePublicIdentifierState: goto AfterDoctypePublicIdentifierState;
            case State.BetweenDoctypePublicAndSystemIdentifiersState: goto BetweenDoctypePublicAndSystemIdentifiersState;
            case State.AfterDoctypeSystemKeywordState: goto AfterDoctypeSystemKeywordState;
            case State.BeforeDoctypeSystemIdentifierState: goto BeforeDoctypeSystemIdentifierState;
            case State.DoctypeSystemIdentifierDoubleQuotedState: goto DoctypeSystemIdentifierDoubleQuotedState;
            case State.DoctypeSystemIdentifierSingleQuotedState: goto DoctypeSystemIdentifierSingleQuotedState;
            case State.AfterDoctypeSystemIdentifierState: goto AfterDoctypeSystemIdentifierState;
            case State.BogusDoctypeState: goto BogusDoctypeState;
            case State.CdataSectionState: goto CdataSectionState;
            case State.CdataSectionBracketState: goto CdataSectionBracketState;
            case State.CdataSectionEndState: goto CdataSectionEndState;
            default: goto EndImpl;
        }

        goto EndImpl;

    // http://www.whatwg.org/specs/web-apps/current-work/#data-state
    DataState:
        {
            if (cc == '&')
            {
                _state = State.CharacterReferenceInDataState;
                cc = source.AdvancePastNonNewline();
                goto CharacterReferenceInDataState;
            }
            if (cc == '<')
            {
                // We have a bunch of character tokens queued up that we emit lazily.
                if (HaveBufferedCharacterToken())
                    return true;
                _state = State.TagOpenState;
                cc = source.AdvancePastNonNewline();
                goto TagOpenState;
            }
            if (cc == '\0')
                return EmitEndOfFile(source);
            return EmitData(source, cc);
        }

    CharacterReferenceInDataState:
        {
            if (!ProcessEntity(source))
                return HaveBufferedCharacterToken();
            _state = State.DataState;
            cc = source.CurrentChar();
            goto DataState;
        }

    RcdataState:
        {
            while (!CheckScanFlag(cc, ScanFlags.RcdataSpecial))
            {
                BufferCharacter(cc);
                if (!_inputStreamPreprocessor.Advance(source, out cc))
                    return HaveBufferedCharacterToken();
            }
            if (cc == '&')
            {
                _state = State.CharacterReferenceInRcdataState;
                cc = source.AdvancePastNonNewline();
                goto CharacterReferenceInRcdataState;
            }
            if (cc == '<')
            {
                _state = State.RcdataLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto RcdataLessThanSignState;
            }
            if (cc == '\0')
                return EmitEndOfFile(source);
            return HaveBufferedCharacterToken();
        }

    CharacterReferenceInRcdataState:
        {
            if (!ProcessEntity(source))
                return HaveBufferedCharacterToken();
            _state = State.RcdataState;
            cc = source.CurrentChar();
            goto RcdataState;
        }

    RawtextState:
        {
            if (cc == '<')
            {
                _state = State.RawtextLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto RawtextLessThanSignState;
            }
            if (cc == '\0')
                return EmitEndOfFile(source);
            BufferCharacter(cc);
            _state = State.RawtextState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto RawtextState;
        }

    ScriptDataState:
        {
            if (cc == '<')
            {
                _state = State.ScriptDataLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataLessThanSignState;
            }
            if (cc == '\0')
                return EmitEndOfFile(source);
            BufferCharacter(cc);
            _state = State.ScriptDataState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto ScriptDataState;
        }

    PlaintextState:
        {
            if (cc == '\0')
                return EmitEndOfFile(source);
            return EmitPlaintext(source, cc);
        }

    // Fix #4: re-read the current character after the transition.
    TagOpenState:
        {
            cc = source.CurrentChar();
            if (IsAsciiAlpha(cc))
            {
                _token.BeginStartTag(ToLowerCase(cc));
                _state = State.TagNameState;
                cc = source.AdvancePastNonNewline();
                goto TagNameState;
            }
            if (cc == '!')
            {
                _state = State.MarkupDeclarationOpenState;
                cc = source.AdvancePastNonNewline();
                goto MarkupDeclarationOpenState;
            }
            if (cc == '/')
            {
                _state = State.EndTagOpenState;
                cc = source.AdvancePastNonNewline();
                goto EndTagOpenState;
            }
            if (cc == '?')
            {
                // The spec consumes the current character before switching to the
                // bogus comment state, but it's easier to implement if we reconsume it.
                _state = State.BogusCommentState;
                goto BogusCommentState;
            }
            BufferCharacter('<');
            _state = State.DataState;
            goto DataState;
        }

    EndTagOpenState:
        {
            if (IsAsciiAlpha(cc))
            {
                _token.BeginEndTag(ToLowerCase(cc));
                _appropriateEndTagName.Clear();
                _state = State.TagNameState;
                cc = source.AdvancePastNonNewline();
                goto TagNameState;
            }
            if (cc == '>')
            {
                _state = State.DataState;
                cc = source.AdvancePastNonNewline();
                goto DataState;
            }
            if (cc == '\0')
            {
                BufferCharacter('<');
                BufferCharacter('/');
                _state = State.DataState;
                goto DataState;
            }
            _state = State.BogusCommentState;
            goto BogusCommentState;
        }

    TagNameState:
        {
            while (!CheckScanFlag(cc, ScanFlags.TagNameSpecial))
            {
                _token.AppendToName(ToLowerCaseIfAlpha(cc));
                if (!_inputStreamPreprocessor.AdvancePastNonNewline(source, out cc))
                    return HaveBufferedCharacterToken();
            }
            if (cc == '/')
            {
                _state = State.SelfClosingStartTagState;
                cc = source.AdvancePastNonNewline();
                goto SelfClosingStartTagState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            _state = State.BeforeAttributeNameState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto BeforeAttributeNameState;
        }

    RcdataLessThanSignState:
        {
            if (cc == '/')
            {
                _temporaryBuffer.Clear();
                _state = State.RcdataEndTagOpenState;
                cc = source.AdvancePastNonNewline();
                goto RcdataEndTagOpenState;
            }
            BufferCharacter('<');
            _state = State.RcdataState;
            goto RcdataState;
        }

    RcdataEndTagOpenState:
        {
            if (IsAsciiAlpha(cc))
            {
                _temporaryBuffer.Append(cc);
                AddToPossibleEndTag(ToLowerCase(cc));
                _state = State.RcdataEndTagNameState;
                cc = source.AdvancePastNonNewline();
                goto RcdataEndTagNameState;
            }
            BufferCharacter('<');
            BufferCharacter('/');
            _state = State.RcdataState;
            goto RcdataState;
        }

    RcdataEndTagNameState:
        {
            if (IsAsciiAlpha(cc))
            {
                _temporaryBuffer.Append(cc);
                AddToPossibleEndTag(ToLowerCase(cc));
                _state = State.RcdataEndTagNameState;
                cc = source.AdvancePastNonNewline();
                goto RcdataEndTagNameState;
            }
            if (IsTokenizerWhitespace(cc))
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    _state = State.BeforeAttributeNameState;
                    if (FlushBufferedEndTag(source, currentCharMayBeNewline: true))
                        return true;
                    if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
                        return _token.Type != HtmlToken.TokenType.Uninitialized;
                    goto BeforeAttributeNameState;
                }
            }
            else if (cc == '/')
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    _state = State.SelfClosingStartTagState;
                    if (FlushBufferedEndTag(source, currentCharMayBeNewline: false))
                        return true;
                    if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
                        return _token.Type != HtmlToken.TokenType.Uninitialized;
                    goto SelfClosingStartTagState;
                }
            }
            else if (cc == '>')
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    return FlushEmitAndResumeInDataState(source);
                }
            }
            BufferCharacter('<');
            BufferCharacter('/');
            _token.AppendToCharacter(_temporaryBuffer.ToString());
            _bufferedEndTagName.Clear();
            _temporaryBuffer.Clear();
            _state = State.RcdataState;
            goto RcdataState;
        }

    RawtextLessThanSignState:
        {
            if (cc == '/')
            {
                _temporaryBuffer.Clear();
                _state = State.RawtextEndTagOpenState;
                cc = source.AdvancePastNonNewline();
                goto RawtextEndTagOpenState;
            }
            BufferCharacter('<');
            _state = State.RawtextState;
            goto RawtextState;
        }

    RawtextEndTagOpenState:
        {
            if (IsAsciiAlpha(cc))
            {
                _temporaryBuffer.Append(cc);
                AddToPossibleEndTag(ToLowerCase(cc));
                _state = State.RawtextEndTagNameState;
                cc = source.AdvancePastNonNewline();
                goto RawtextEndTagNameState;
            }
            BufferCharacter('<');
            BufferCharacter('/');
            _state = State.RawtextState;
            goto RawtextState;
        }

    RawtextEndTagNameState:
        {
            if (IsAsciiAlpha(cc))
            {
                _temporaryBuffer.Append(cc);
                AddToPossibleEndTag(ToLowerCase(cc));
                _state = State.RawtextEndTagNameState;
                cc = source.AdvancePastNonNewline();
                goto RawtextEndTagNameState;
            }
            if (IsTokenizerWhitespace(cc))
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    _state = State.BeforeAttributeNameState;
                    if (FlushBufferedEndTag(source, currentCharMayBeNewline: true))
                        return true;
                    if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
                        return _token.Type != HtmlToken.TokenType.Uninitialized;
                    goto BeforeAttributeNameState;
                }
            }
            else if (cc == '/')
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    _state = State.SelfClosingStartTagState;
                    if (FlushBufferedEndTag(source, currentCharMayBeNewline: false))
                        return true;
                    if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
                        return _token.Type != HtmlToken.TokenType.Uninitialized;
                    goto SelfClosingStartTagState;
                }
            }
            else if (cc == '>')
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    return FlushEmitAndResumeInDataState(source);
                }
            }
            BufferCharacter('<');
            BufferCharacter('/');
            _token.AppendToCharacter(_temporaryBuffer.ToString());
            _bufferedEndTagName.Clear();
            _temporaryBuffer.Clear();
            _state = State.RawtextState;
            goto RawtextState;
        }

    ScriptDataLessThanSignState:
        {
            if (cc == '/')
            {
                _temporaryBuffer.Clear();
                _state = State.ScriptDataEndTagOpenState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEndTagOpenState;
            }
            if (cc == '!')
            {
                BufferCharacter('<');
                BufferCharacter('!');
                _state = State.ScriptDataEscapeStartState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapeStartState;
            }
            BufferCharacter('<');
            _state = State.ScriptDataState;
            goto ScriptDataState;
        }

    ScriptDataEndTagOpenState:
        {
            if (IsAsciiAlpha(cc))
            {
                _temporaryBuffer.Append(cc);
                AddToPossibleEndTag(ToLowerCase(cc));
                _state = State.ScriptDataEndTagNameState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEndTagNameState;
            }
            BufferCharacter('<');
            BufferCharacter('/');
            _state = State.ScriptDataState;
            goto ScriptDataState;
        }

    ScriptDataEndTagNameState:
        {
            if (IsAsciiAlpha(cc))
            {
                _temporaryBuffer.Append(cc);
                AddToPossibleEndTag(ToLowerCase(cc));
                _state = State.ScriptDataEndTagNameState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEndTagNameState;
            }
            if (IsTokenizerWhitespace(cc))
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    _state = State.BeforeAttributeNameState;
                    if (FlushBufferedEndTag(source, currentCharMayBeNewline: true))
                        return true;
                    if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
                        return _token.Type != HtmlToken.TokenType.Uninitialized;
                    goto BeforeAttributeNameState;
                }
            }
            else if (cc == '/')
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    _state = State.SelfClosingStartTagState;
                    if (FlushBufferedEndTag(source, currentCharMayBeNewline: false))
                        return true;
                    if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
                        return _token.Type != HtmlToken.TokenType.Uninitialized;
                    goto SelfClosingStartTagState;
                }
            }
            else if (cc == '>')
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    return FlushEmitAndResumeInDataState(source);
                }
            }
            BufferCharacter('<');
            BufferCharacter('/');
            _token.AppendToCharacter(_temporaryBuffer.ToString());
            _bufferedEndTagName.Clear();
            _temporaryBuffer.Clear();
            _state = State.ScriptDataState;
            goto ScriptDataState;
        }

    ScriptDataEscapeStartState:
        {
            if (cc == '-')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataEscapeStartDashState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapeStartDashState;
            }
            _state = State.ScriptDataState;
            goto ScriptDataState;
        }

    ScriptDataEscapeStartDashState:
        {
            if (cc == '-')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataEscapedDashDashState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedDashDashState;
            }
            _state = State.ScriptDataState;
            goto ScriptDataState;
        }

    ScriptDataEscapedState:
        {
            if (cc == '-')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataEscapedDashState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedDashState;
            }
            if (cc == '<')
            {
                _state = State.ScriptDataEscapedLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedLessThanSignState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            BufferCharacter(cc);
            _state = State.ScriptDataEscapedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto ScriptDataEscapedState;
        }

    ScriptDataEscapedDashState:
        {
            if (cc == '-')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataEscapedDashDashState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedDashDashState;
            }
            if (cc == '<')
            {
                _state = State.ScriptDataEscapedLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedLessThanSignState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            BufferCharacter(cc);
            _state = State.ScriptDataEscapedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto ScriptDataEscapedState;
        }

    ScriptDataEscapedDashDashState:
        {
            if (cc == '-')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataEscapedDashDashState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedDashDashState;
            }
            if (cc == '<')
            {
                _state = State.ScriptDataEscapedLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedLessThanSignState;
            }
            if (cc == '>')
            {
                BufferCharacter(cc);
                _state = State.DataState;
                cc = source.AdvancePastNonNewline();
                goto DataState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            BufferCharacter(cc);
            _state = State.ScriptDataEscapedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto ScriptDataEscapedState;
        }

    ScriptDataEscapedLessThanSignState:
        {
            if (cc == '/')
            {
                _temporaryBuffer.Clear();
                _state = State.ScriptDataEscapedEndTagOpenState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedEndTagOpenState;
            }
            if (IsAsciiAlpha(cc))
            {
                BufferCharacter('<');
                BufferCharacter(cc);
                _temporaryBuffer.Clear();
                _temporaryBuffer.Append(ToLowerCase(cc));
                _state = State.ScriptDataDoubleEscapeStartState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapeStartState;
            }
            BufferCharacter('<');
            _state = State.ScriptDataEscapedState;
            goto ScriptDataEscapedState;
        }

    ScriptDataEscapedEndTagOpenState:
        {
            if (IsAsciiAlpha(cc))
            {
                _temporaryBuffer.Append(cc);
                AddToPossibleEndTag(ToLowerCase(cc));
                _state = State.ScriptDataEscapedEndTagNameState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedEndTagNameState;
            }
            BufferCharacter('<');
            BufferCharacter('/');
            _state = State.ScriptDataEscapedState;
            goto ScriptDataEscapedState;
        }

    ScriptDataEscapedEndTagNameState:
        {
            if (IsAsciiAlpha(cc))
            {
                _temporaryBuffer.Append(cc);
                AddToPossibleEndTag(ToLowerCase(cc));
                _state = State.ScriptDataEscapedEndTagNameState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataEscapedEndTagNameState;
            }
            if (IsTokenizerWhitespace(cc))
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    _state = State.BeforeAttributeNameState;
                    if (FlushBufferedEndTag(source, currentCharMayBeNewline: true))
                        return true;
                    if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
                        return _token.Type != HtmlToken.TokenType.Uninitialized;
                    goto BeforeAttributeNameState;
                }
            }
            else if (cc == '/')
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    _state = State.SelfClosingStartTagState;
                    if (FlushBufferedEndTag(source, currentCharMayBeNewline: false))
                        return true;
                    if (source.IsEmpty || !_inputStreamPreprocessor.Peek(source, out cc))
                        return _token.Type != HtmlToken.TokenType.Uninitialized;
                    goto SelfClosingStartTagState;
                }
            }
            else if (cc == '>')
            {
                if (IsAppropriateEndTag())
                {
                    _temporaryBuffer.Append(cc);
                    return FlushEmitAndResumeInDataState(source);
                }
            }
            BufferCharacter('<');
            BufferCharacter('/');
            _token.AppendToCharacter(_temporaryBuffer.ToString());
            _bufferedEndTagName.Clear();
            _temporaryBuffer.Clear();
            _state = State.ScriptDataEscapedState;
            goto ScriptDataEscapedState;
        }

    ScriptDataDoubleEscapeStartState:
        {
            if (IsTokenizerWhitespace(cc) || cc == '/' || cc == '>')
            {
                BufferCharacter(cc);
                if (TemporaryBufferIs("script"))
                    _state = State.ScriptDataDoubleEscapedState;
                else
                    _state = State.ScriptDataEscapedState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto ScriptDataEscapedState;
            }
            if (IsAsciiAlpha(cc))
            {
                BufferCharacter(cc);
                _temporaryBuffer.Append(ToLowerCase(cc));
                _state = State.ScriptDataDoubleEscapeStartState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapeStartState;
            }
            _state = State.ScriptDataEscapedState;
            goto ScriptDataEscapedState;
        }

    ScriptDataDoubleEscapedState:
        {
            if (cc == '-')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataDoubleEscapedDashState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapedDashState;
            }
            if (cc == '<')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataDoubleEscapedLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapedLessThanSignState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            BufferCharacter(cc);
            _state = State.ScriptDataDoubleEscapedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto ScriptDataDoubleEscapedState;
        }

    ScriptDataDoubleEscapedDashState:
        {
            if (cc == '-')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataDoubleEscapedDashDashState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapedDashDashState;
            }
            if (cc == '<')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataDoubleEscapedLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapedLessThanSignState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            BufferCharacter(cc);
            _state = State.ScriptDataDoubleEscapedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto ScriptDataDoubleEscapedState;
        }

    ScriptDataDoubleEscapedDashDashState:
        {
            if (cc == '-')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataDoubleEscapedDashDashState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapedDashDashState;
            }
            if (cc == '<')
            {
                BufferCharacter(cc);
                _state = State.ScriptDataDoubleEscapedLessThanSignState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapedLessThanSignState;
            }
            if (cc == '>')
            {
                BufferCharacter(cc);
                _state = State.DataState;
                cc = source.AdvancePastNonNewline();
                goto DataState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            BufferCharacter(cc);
            _state = State.ScriptDataDoubleEscapedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto ScriptDataDoubleEscapedState;
        }

    ScriptDataDoubleEscapedLessThanSignState:
        {
            if (cc == '/')
            {
                BufferCharacter(cc);
                _temporaryBuffer.Clear();
                _state = State.ScriptDataDoubleEscapeEndState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapeEndState;
            }
            _state = State.ScriptDataDoubleEscapedState;
            goto ScriptDataDoubleEscapedState;
        }

    ScriptDataDoubleEscapeEndState:
        {
            if (IsTokenizerWhitespace(cc) || cc == '/' || cc == '>')
            {
                BufferCharacter(cc);
                if (TemporaryBufferIs("script"))
                    _state = State.ScriptDataEscapedState;
                else
                    _state = State.ScriptDataDoubleEscapedState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto ScriptDataEscapedState;
            }
            if (IsAsciiAlpha(cc))
            {
                BufferCharacter(cc);
                _temporaryBuffer.Append(ToLowerCase(cc));
                _state = State.ScriptDataDoubleEscapeEndState;
                cc = source.AdvancePastNonNewline();
                goto ScriptDataDoubleEscapeEndState;
            }
            _state = State.ScriptDataDoubleEscapedState;
            goto ScriptDataDoubleEscapedState;
        }

    BeforeAttributeNameState:
        {
            if (!SkipWhitespaces(source, ref cc))
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (cc == '/')
            {
                _state = State.SelfClosingStartTagState;
                cc = source.AdvancePastNonNewline();
                goto SelfClosingStartTagState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            if (cc == '"' || cc == '\'' || cc == '<' || cc == '=')
            {
                // parse error
            }
            _token.AddNewAttribute(ToLowerCaseIfAlpha(cc));
            _state = State.AttributeNameState;
            cc = source.AdvancePastNonNewline();
            goto AttributeNameState;
        }

    AttributeNameState:
        {
            while (!CheckScanFlag(cc, ScanFlags.AttributeNameSpecial))
            {
                _token.AppendToAttributeName(ToLowerCaseIfAlpha(cc));
                if (!_inputStreamPreprocessor.AdvancePastNonNewline(source, out cc))
                    return HaveBufferedCharacterToken();
            }
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.AfterAttributeNameState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto AfterAttributeNameState;
            }
            if (cc == '/')
            {
                _state = State.SelfClosingStartTagState;
                cc = source.AdvancePastNonNewline();
                goto SelfClosingStartTagState;
            }
            if (cc == '=')
            {
                _state = State.BeforeAttributeValueState;
                cc = source.AdvancePastNonNewline();
                goto BeforeAttributeValueState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            // parse error: cc is '"', '\'', '<' or '='.
            _token.AppendToAttributeName(ToLowerCaseIfAlpha(cc));
            _state = State.AttributeNameState;
            cc = source.AdvancePastNonNewline();
            goto AttributeNameState;
        }

    AfterAttributeNameState:
        {
            if (!SkipWhitespaces(source, ref cc))
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (cc == '/')
            {
                _state = State.SelfClosingStartTagState;
                cc = source.AdvancePastNonNewline();
                goto SelfClosingStartTagState;
            }
            if (cc == '=')
            {
                _state = State.BeforeAttributeValueState;
                cc = source.AdvancePastNonNewline();
                goto BeforeAttributeValueState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            if (cc == '"' || cc == '\'' || cc == '<')
            {
                // parse error
            }
            _token.AddNewAttribute(ToLowerCaseIfAlpha(cc));
            _state = State.AttributeNameState;
            cc = source.AdvancePastNonNewline();
            goto AttributeNameState;
        }

    BeforeAttributeValueState:
        {
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.BeforeAttributeValueState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto BeforeAttributeValueState;
            }
            if (cc == '"')
            {
                _state = State.AttributeValueDoubleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto AttributeValueDoubleQuotedState;
            }
            if (cc == '&')
            {
                _state = State.AttributeValueUnquotedState;
                goto AttributeValueUnquotedState;
            }
            if (cc == '\'')
            {
                _state = State.AttributeValueSingleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto AttributeValueSingleQuotedState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            if (cc == '<' || cc == '=' || cc == '`')
            {
                // parse error
            }
            _token.AppendToAttributeValue(cc);
            _state = State.AttributeValueUnquotedState;
            cc = source.AdvancePastNonNewline();
            goto AttributeValueUnquotedState;
        }

    AttributeValueDoubleQuotedState:
        {
            if (cc == '"')
            {
                _state = State.AfterAttributeValueQuotedState;
                cc = source.AdvancePastNonNewline();
                goto AfterAttributeValueQuotedState;
            }
            if (cc == '&')
            {
                _additionalAllowedCharacter = '"';
                _state = State.CharacterReferenceInAttributeValueState;
                cc = source.AdvancePastNonNewline();
                goto CharacterReferenceInAttributeValueState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            _token.AppendToAttributeValue(cc);
            _state = State.AttributeValueDoubleQuotedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto AttributeValueDoubleQuotedState;
        }

    AttributeValueSingleQuotedState:
        {
            if (cc == '\'')
            {
                _state = State.AfterAttributeValueQuotedState;
                cc = source.AdvancePastNonNewline();
                goto AfterAttributeValueQuotedState;
            }
            if (cc == '&')
            {
                _additionalAllowedCharacter = '\'';
                _state = State.CharacterReferenceInAttributeValueState;
                cc = source.AdvancePastNonNewline();
                goto CharacterReferenceInAttributeValueState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            _token.AppendToAttributeValue(cc);
            _state = State.AttributeValueSingleQuotedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto AttributeValueSingleQuotedState;
        }

    AttributeValueUnquotedState:
        {
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.BeforeAttributeNameState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto BeforeAttributeNameState;
            }
            if (cc == '&')
            {
                _additionalAllowedCharacter = '>';
                _state = State.CharacterReferenceInAttributeValueState;
                cc = source.AdvancePastNonNewline();
                goto CharacterReferenceInAttributeValueState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            if (cc == '"' || cc == '\'' || cc == '<' || cc == '=' || cc == '`')
            {
                // parse error
            }
            _token.AppendToAttributeValue(cc);
            _state = State.AttributeValueUnquotedState;
            cc = source.AdvancePastNonNewline();
            goto AttributeValueUnquotedState;
        }

    CharacterReferenceInAttributeValueState:
        {
            bool notEnoughCharacters = false;
            var decodedEntity = new DecodedHtmlEntity();
            bool success = HtmlEntityParser.ConsumeHtmlEntity(source, ref decodedEntity, out notEnoughCharacters, _additionalAllowedCharacter);
            if (notEnoughCharacters)
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (!success)
            {
                _token.AppendToAttributeValue('&');
            }
            else
            {
                for (int i = 0; i < decodedEntity.Length; i++)
                    _token.AppendToAttributeValue(decodedEntity.Data[i]);
            }
            // We're supposed to switch back to the attribute value state that we
            // were in when we were switched into this state, determined by
            // additional_allowed_character_.
            if (_additionalAllowedCharacter == '"')
            {
                _state = State.AttributeValueDoubleQuotedState;
                cc = source.CurrentChar();
                goto AttributeValueDoubleQuotedState;
            }
            if (_additionalAllowedCharacter == '\'')
            {
                _state = State.AttributeValueSingleQuotedState;
                cc = source.CurrentChar();
                goto AttributeValueSingleQuotedState;
            }
            if (_additionalAllowedCharacter == '>')
            {
                _state = State.AttributeValueUnquotedState;
                cc = source.CurrentChar();
                goto AttributeValueUnquotedState;
            }
            goto EndImpl;
        }

    AfterAttributeValueQuotedState:
        {
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.BeforeAttributeNameState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto BeforeAttributeNameState;
            }
            if (cc == '/')
            {
                _state = State.SelfClosingStartTagState;
                cc = source.AdvancePastNonNewline();
                goto SelfClosingStartTagState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            _state = State.BeforeAttributeNameState;
            goto BeforeAttributeNameState;
        }

    SelfClosingStartTagState:
        {
            if (cc == '>')
            {
                _token.SetSelfClosing();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            _state = State.BeforeAttributeNameState;
            goto BeforeAttributeNameState;
        }

    BogusCommentState:
        {
            _token.BeginComment();
            _state = State.ContinueBogusCommentState;
            goto ContinueBogusCommentState;
        }

    ContinueBogusCommentState:
        {
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
                return EmitAndReconsumeInDataState();
            _token.AppendToComment(cc);
            _state = State.ContinueBogusCommentState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto ContinueBogusCommentState;
        }

    MarkupDeclarationOpenState:
        {
            if (cc == '-')
            {
                SegmentedString.LookAheadResult result = source.LookAhead("--");
                if (result == SegmentedString.LookAheadResult.DidMatch)
                {
                    source.AdvanceAndAssert('-');
                    source.AdvanceAndAssert('-');
                    _token.BeginComment();
                    _state = State.CommentStartState;
                    cc = source.CurrentChar();
                    goto CommentStartState;
                }
                if (result == SegmentedString.LookAheadResult.NotEnoughCharacters)
                    return HaveBufferedCharacterToken();
            }
            else if (cc == 'D' || cc == 'd')
            {
                SegmentedString.LookAheadResult result = source.LookAheadIgnoringCase("doctype");
                if (result == SegmentedString.LookAheadResult.DidMatch)
                {
                    AdvanceString(source, "doctype");
                    _state = State.DoctypeState;
                    cc = source.CurrentChar();
                    goto DoctypeState;
                }
                if (result == SegmentedString.LookAheadResult.NotEnoughCharacters)
                    return HaveBufferedCharacterToken();
            }
            else if (cc == '[' && _shouldAllowCdata)
            {
                SegmentedString.LookAheadResult result = source.LookAhead("[CDATA[");
                if (result == SegmentedString.LookAheadResult.DidMatch)
                {
                    AdvanceString(source, "[CDATA[");
                    _state = State.CdataSectionState;
                    cc = source.CurrentChar();
                    goto CdataSectionState;
                }
                if (result == SegmentedString.LookAheadResult.NotEnoughCharacters)
                    return HaveBufferedCharacterToken();
            }
            _state = State.BogusCommentState;
            goto BogusCommentState;
        }

    CommentStartState:
        {
            if (cc == '-')
            {
                _state = State.CommentStartDashState;
                cc = source.AdvancePastNonNewline();
                goto CommentStartDashState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
                return EmitAndReconsumeInDataState();
            _token.AppendToComment(cc);
            _state = State.CommentState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto CommentState;
        }

    CommentStartDashState:
        {
            if (cc == '-')
            {
                _state = State.CommentEndState;
                cc = source.AdvancePastNonNewline();
                goto CommentEndState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
                return EmitAndReconsumeInDataState();
            _token.AppendToComment('-');
            _token.AppendToComment(cc);
            _state = State.CommentState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto CommentState;
        }

    CommentState:
        {
            if (cc == '-')
            {
                _state = State.CommentEndDashState;
                cc = source.AdvancePastNonNewline();
                goto CommentEndDashState;
            }
            if (cc == '\0')
                return EmitAndReconsumeInDataState();
            _token.AppendToComment(cc);
            _state = State.CommentState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto CommentState;
        }

    CommentEndDashState:
        {
            if (cc == '-')
            {
                _state = State.CommentEndState;
                cc = source.AdvancePastNonNewline();
                goto CommentEndState;
            }
            if (cc == '\0')
                return EmitAndReconsumeInDataState();
            _token.AppendToComment('-');
            _token.AppendToComment(cc);
            _state = State.CommentState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto CommentState;
        }

    CommentEndState:
        {
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '!')
            {
                _state = State.CommentEndBangState;
                cc = source.AdvancePastNonNewline();
                goto CommentEndBangState;
            }
            if (cc == '-')
            {
                _token.AppendToComment('-');
                _state = State.CommentEndState;
                cc = source.AdvancePastNonNewline();
                goto CommentEndState;
            }
            if (cc == '\0')
                return EmitAndReconsumeInDataState();
            _token.AppendToComment('-');
            _token.AppendToComment('-');
            _token.AppendToComment(cc);
            _state = State.CommentState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto CommentState;
        }

    CommentEndBangState:
        {
            if (cc == '-')
            {
                _token.AppendToComment('-');
                _token.AppendToComment('-');
                _token.AppendToComment('!');
                _state = State.CommentEndDashState;
                cc = source.AdvancePastNonNewline();
                goto CommentEndDashState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
                return EmitAndReconsumeInDataState();
            _token.AppendToComment('-');
            _token.AppendToComment('-');
            _token.AppendToComment('!');
            _token.AppendToComment(cc);
            _state = State.CommentState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto CommentState;
        }

    DoctypeState:
        {
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.BeforeDoctypeNameState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto BeforeDoctypeNameState;
            }
            if (cc == '\0')
            {
                _token.BeginDoctype();
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _state = State.BeforeDoctypeNameState;
            goto BeforeDoctypeNameState;
        }

    BeforeDoctypeNameState:
        {
            if (!SkipWhitespaces(source, ref cc))
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (cc == '>')
            {
                _token.BeginDoctype();
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.BeginDoctype();
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.BeginDoctype(ToLowerCaseIfAlpha(cc));
            _state = State.DoctypeNameState;
            cc = source.AdvancePastNonNewline();
            goto DoctypeNameState;
        }

    DoctypeNameState:
        {
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.AfterDoctypeNameState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto AfterDoctypeNameState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.AppendToName(ToLowerCaseIfAlpha(cc));
            _state = State.DoctypeNameState;
            cc = source.AdvancePastNonNewline();
            goto DoctypeNameState;
        }

    AfterDoctypeNameState:
        {
            if (!SkipWhitespaces(source, ref cc))
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            if (cc == 'P' || cc == 'p')
            {
                SegmentedString.LookAheadResult result = source.LookAheadIgnoringCase("public");
                if (result == SegmentedString.LookAheadResult.DidMatch)
                {
                    AdvanceString(source, "public");
                    _state = State.AfterDoctypePublicKeywordState;
                    cc = source.CurrentChar();
                    goto AfterDoctypePublicKeywordState;
                }
                if (result == SegmentedString.LookAheadResult.NotEnoughCharacters)
                    return HaveBufferedCharacterToken();
            }
            else if (cc == 'S' || cc == 's')
            {
                SegmentedString.LookAheadResult result = source.LookAheadIgnoringCase("system");
                if (result == SegmentedString.LookAheadResult.DidMatch)
                {
                    AdvanceString(source, "system");
                    _state = State.AfterDoctypeSystemKeywordState;
                    cc = source.CurrentChar();
                    goto AfterDoctypeSystemKeywordState;
                }
                if (result == SegmentedString.LookAheadResult.NotEnoughCharacters)
                    return HaveBufferedCharacterToken();
            }
            _token.SetForceQuirks();
            _state = State.BogusDoctypeState;
            cc = source.AdvancePastNonNewline();
            goto BogusDoctypeState;
        }

    AfterDoctypePublicKeywordState:
        {
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.BeforeDoctypePublicIdentifierState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto BeforeDoctypePublicIdentifierState;
            }
            if (cc == '"')
            {
                _token.SetPublicIdentifierToEmptyString();
                _state = State.DoctypePublicIdentifierDoubleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypePublicIdentifierDoubleQuotedState;
            }
            if (cc == '\'')
            {
                _token.SetPublicIdentifierToEmptyString();
                _state = State.DoctypePublicIdentifierSingleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypePublicIdentifierSingleQuotedState;
            }
            if (cc == '>')
            {
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.SetForceQuirks();
            _state = State.BogusDoctypeState;
            cc = source.AdvancePastNonNewline();
            goto BogusDoctypeState;
        }

    BeforeDoctypePublicIdentifierState:
        {
            if (!SkipWhitespaces(source, ref cc))
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (cc == '"')
            {
                _token.SetPublicIdentifierToEmptyString();
                _state = State.DoctypePublicIdentifierDoubleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypePublicIdentifierDoubleQuotedState;
            }
            if (cc == '\'')
            {
                _token.SetPublicIdentifierToEmptyString();
                _state = State.DoctypePublicIdentifierSingleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypePublicIdentifierSingleQuotedState;
            }
            if (cc == '>')
            {
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.SetForceQuirks();
            _state = State.BogusDoctypeState;
            cc = source.AdvancePastNonNewline();
            goto BogusDoctypeState;
        }

    DoctypePublicIdentifierDoubleQuotedState:
        {
            if (cc == '"')
            {
                _state = State.AfterDoctypePublicIdentifierState;
                cc = source.AdvancePastNonNewline();
                goto AfterDoctypePublicIdentifierState;
            }
            if (cc == '>')
            {
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.AppendToPublicIdentifier(cc);
            _state = State.DoctypePublicIdentifierDoubleQuotedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto DoctypePublicIdentifierDoubleQuotedState;
        }

    DoctypePublicIdentifierSingleQuotedState:
        {
            if (cc == '\'')
            {
                _state = State.AfterDoctypePublicIdentifierState;
                cc = source.AdvancePastNonNewline();
                goto AfterDoctypePublicIdentifierState;
            }
            if (cc == '>')
            {
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.AppendToPublicIdentifier(cc);
            _state = State.DoctypePublicIdentifierSingleQuotedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto DoctypePublicIdentifierSingleQuotedState;
        }

    AfterDoctypePublicIdentifierState:
        {
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.BetweenDoctypePublicAndSystemIdentifiersState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto BetweenDoctypePublicAndSystemIdentifiersState;
            }
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '"')
            {
                _token.SetSystemIdentifierToEmptyString();
                _state = State.DoctypeSystemIdentifierDoubleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypeSystemIdentifierDoubleQuotedState;
            }
            if (cc == '\'')
            {
                _token.SetSystemIdentifierToEmptyString();
                _state = State.DoctypeSystemIdentifierSingleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypeSystemIdentifierSingleQuotedState;
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.SetForceQuirks();
            _state = State.BogusDoctypeState;
            cc = source.AdvancePastNonNewline();
            goto BogusDoctypeState;
        }

    BetweenDoctypePublicAndSystemIdentifiersState:
        {
            if (!SkipWhitespaces(source, ref cc))
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '"')
            {
                _token.SetSystemIdentifierToEmptyString();
                _state = State.DoctypeSystemIdentifierDoubleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypeSystemIdentifierDoubleQuotedState;
            }
            if (cc == '\'')
            {
                _token.SetSystemIdentifierToEmptyString();
                _state = State.DoctypeSystemIdentifierSingleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypeSystemIdentifierSingleQuotedState;
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.SetForceQuirks();
            _state = State.BogusDoctypeState;
            cc = source.AdvancePastNonNewline();
            goto BogusDoctypeState;
        }

    AfterDoctypeSystemKeywordState:
        {
            if (IsTokenizerWhitespace(cc))
            {
                _state = State.BeforeDoctypeSystemIdentifierState;
                cc = source.AdvanceAndUpdateLineNumber();
                goto BeforeDoctypeSystemIdentifierState;
            }
            if (cc == '"')
            {
                _token.SetSystemIdentifierToEmptyString();
                _state = State.DoctypeSystemIdentifierDoubleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypeSystemIdentifierDoubleQuotedState;
            }
            if (cc == '\'')
            {
                _token.SetSystemIdentifierToEmptyString();
                _state = State.DoctypeSystemIdentifierSingleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypeSystemIdentifierSingleQuotedState;
            }
            if (cc == '>')
            {
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.SetForceQuirks();
            _state = State.BogusDoctypeState;
            cc = source.AdvancePastNonNewline();
            goto BogusDoctypeState;
        }

    BeforeDoctypeSystemIdentifierState:
        {
            if (!SkipWhitespaces(source, ref cc))
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (cc == '"')
            {
                _token.SetSystemIdentifierToEmptyString();
                _state = State.DoctypeSystemIdentifierDoubleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypeSystemIdentifierDoubleQuotedState;
            }
            if (cc == '\'')
            {
                _token.SetSystemIdentifierToEmptyString();
                _state = State.DoctypeSystemIdentifierSingleQuotedState;
                cc = source.AdvancePastNonNewline();
                goto DoctypeSystemIdentifierSingleQuotedState;
            }
            if (cc == '>')
            {
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.SetForceQuirks();
            _state = State.BogusDoctypeState;
            cc = source.AdvancePastNonNewline();
            goto BogusDoctypeState;
        }

    DoctypeSystemIdentifierDoubleQuotedState:
        {
            if (cc == '"')
            {
                _state = State.AfterDoctypeSystemIdentifierState;
                cc = source.AdvancePastNonNewline();
                goto AfterDoctypeSystemIdentifierState;
            }
            if (cc == '>')
            {
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.AppendToSystemIdentifier(cc);
            _state = State.DoctypeSystemIdentifierDoubleQuotedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto DoctypeSystemIdentifierDoubleQuotedState;
        }

    DoctypeSystemIdentifierSingleQuotedState:
        {
            if (cc == '\'')
            {
                _state = State.AfterDoctypeSystemIdentifierState;
                cc = source.AdvancePastNonNewline();
                goto AfterDoctypeSystemIdentifierState;
            }
            if (cc == '>')
            {
                _token.SetForceQuirks();
                return EmitAndResumeInDataState(source);
            }
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _token.AppendToSystemIdentifier(cc);
            _state = State.DoctypeSystemIdentifierSingleQuotedState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto DoctypeSystemIdentifierSingleQuotedState;
        }

    AfterDoctypeSystemIdentifierState:
        {
            if (!SkipWhitespaces(source, ref cc))
                return _token.Type != HtmlToken.TokenType.Uninitialized;
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
            {
                _token.SetForceQuirks();
                return EmitAndReconsumeInDataState();
            }
            _state = State.BogusDoctypeState;
            cc = source.AdvancePastNonNewline();
            goto BogusDoctypeState;
        }

    BogusDoctypeState:
        {
            if (cc == '>')
                return EmitAndResumeInDataState(source);
            if (cc == '\0')
                return EmitAndReconsumeInDataState();
            _state = State.BogusDoctypeState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto BogusDoctypeState;
        }

    CdataSectionState:
        {
            if (cc == ']')
            {
                _state = State.CdataSectionBracketState;
                cc = source.AdvancePastNonNewline();
                goto CdataSectionBracketState;
            }
            if (cc == '\0')
            {
                _state = State.DataState;
                goto DataState;
            }
            BufferCharacter(cc);
            _state = State.CdataSectionState;
            cc = source.AdvanceAndUpdateLineNumber();
            goto CdataSectionState;
        }

    CdataSectionBracketState:
        {
            if (cc == ']')
            {
                _state = State.CdataSectionEndState;
                cc = source.AdvancePastNonNewline();
                goto CdataSectionEndState;
            }
            BufferCharacter(']');
            _state = State.CdataSectionState;
            goto CdataSectionState;
        }

    CdataSectionEndState:
        {
            if (cc == ']')
            {
                BufferCharacter(']');
                _state = State.CdataSectionEndState;
                cc = source.AdvancePastNonNewline();
                goto CdataSectionEndState;
            }
            if (cc == '>')
            {
                _state = State.DataState;
                cc = source.AdvancePastNonNewline();
                goto DataState;
            }
            BufferCharacter(']');
            BufferCharacter(']');
            _state = State.CdataSectionState;
            goto CdataSectionState;
        }

    // Fix #3: report whether the token was put into a non-uninitialized state.
    EndImpl:
        return _token.Type != HtmlToken.TokenType.Uninitialized;
    }
}