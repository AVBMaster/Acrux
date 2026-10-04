using System.Text;

namespace Acrux.Core.Dom.Parser;

/// <summary>
/// Port of Blink's HTMLTokenizer (html_tokenizer.h / html_tokenizer.cc).
/// </summary>
internal partial class HtmlTokenizer
{
    internal enum State
    {
        DataState,
        CharacterReferenceInDataState,
        RcdataState,
        CharacterReferenceInRcdataState,
        RawtextState,
        ScriptDataState,
        PlaintextState,
        TagOpenState,
        EndTagOpenState,
        TagNameState,
        RcdataLessThanSignState,
        RcdataEndTagOpenState,
        RcdataEndTagNameState,
        RawtextLessThanSignState,
        RawtextEndTagOpenState,
        RawtextEndTagNameState,
        ScriptDataLessThanSignState,
        ScriptDataEndTagOpenState,
        ScriptDataEndTagNameState,
        ScriptDataEscapeStartState,
        ScriptDataEscapeStartDashState,
        ScriptDataEscapedState,
        ScriptDataEscapedDashState,
        ScriptDataEscapedDashDashState,
        ScriptDataEscapedLessThanSignState,
        ScriptDataEscapedEndTagOpenState,
        ScriptDataEscapedEndTagNameState,
        ScriptDataDoubleEscapeStartState,
        ScriptDataDoubleEscapedState,
        ScriptDataDoubleEscapedDashState,
        ScriptDataDoubleEscapedDashDashState,
        ScriptDataDoubleEscapedLessThanSignState,
        ScriptDataDoubleEscapeEndState,
        BeforeAttributeNameState,
        AttributeNameState,
        AfterAttributeNameState,
        BeforeAttributeValueState,
        AttributeValueDoubleQuotedState,
        AttributeValueSingleQuotedState,
        AttributeValueUnquotedState,
        CharacterReferenceInAttributeValueState,
        AfterAttributeValueQuotedState,
        SelfClosingStartTagState,
        BogusCommentState,
        ContinueBogusCommentState,
        MarkupDeclarationOpenState,
        CommentStartState,
        CommentStartDashState,
        CommentState,
        CommentEndDashState,
        CommentEndState,
        CommentEndBangState,
        DoctypeState,
        BeforeDoctypeNameState,
        DoctypeNameState,
        AfterDoctypeNameState,
        AfterDoctypePublicKeywordState,
        BeforeDoctypePublicIdentifierState,
        DoctypePublicIdentifierDoubleQuotedState,
        DoctypePublicIdentifierSingleQuotedState,
        AfterDoctypePublicIdentifierState,
        BetweenDoctypePublicAndSystemIdentifiersState,
        AfterDoctypeSystemKeywordState,
        BeforeDoctypeSystemIdentifierState,
        DoctypeSystemIdentifierDoubleQuotedState,
        DoctypeSystemIdentifierSingleQuotedState,
        AfterDoctypeSystemIdentifierState,
        BogusDoctypeState,
        CdataSectionState,
        CdataSectionBracketState,
        CdataSectionEndState,
    }

    [System.Flags]
    private enum ScanFlags : ushort
    {
        NullCharacter = 1 << 0,
        NewlineOrCarriageReturn = 1 << 1,
        WhitespaceNotNewline = 1 << 2,
        Ampersand = 1 << 3,
        OpenTag = 1 << 4,
        SlashAndCloseTag = 1 << 5,
        Equal = 1 << 6,
        Quotes = 1 << 7,
        OpenBrace = 1 << 8,
        Whitespace = WhitespaceNotNewline | NewlineOrCarriageReturn,
        CharacterTokenSpecial = NullCharacter | NewlineOrCarriageReturn | Ampersand | OpenTag | OpenBrace,
        NullOrNewline = NullCharacter | NewlineOrCarriageReturn,
        RcdataSpecial = NullCharacter | Ampersand | OpenTag,
        TagNameSpecial = Whitespace | SlashAndCloseTag | NullCharacter,
        AttributeNameSpecial = Whitespace | SlashAndCloseTag | NullCharacter | Equal | OpenTag | Quotes,
    }

    private readonly HtmlToken _token = new();
    private State _state = State.DataState;
    private bool _forceNullCharacterReplacement;
    private bool _shouldAllowCdata;
    private char _additionalAllowedCharacter;

    private readonly StringBuilder _appropriateEndTagName = new();
    private readonly StringBuilder _temporaryBuffer = new();
    private readonly StringBuilder _bufferedEndTagName = new();

    private readonly HtmlParserOptions _options;

    // Fix #1: the input stream preprocessor must be initialized.
    private readonly InputStreamPreprocessor _inputStreamPreprocessor;

    public HtmlTokenizer(HtmlParserOptions options)
    {
        _options = options;
        _inputStreamPreprocessor = new InputStreamPreprocessor(this);
        Reset();
    }

    public bool ShouldSkipNullCharacters => true;

    public State GetState() => _state;
    public void SetState(State state) => _state = state;

    public void Reset()
    {
        _token.Clear();
        _state = State.DataState;
        _forceNullCharacterReplacement = false;
        _shouldAllowCdata = false;
        _additionalAllowedCharacter = '\0';
    }

    public void ClearToken() => _token.Clear();

    public HtmlToken? NextToken(SegmentedString source)
    {
        bool completed = NextTokenImpl(source);
        return completed ? _token : null;
    }

    // ---------------------------------------------------------------------
    // Character classification and the scan-flag table.
    // ---------------------------------------------------------------------
    private static ushort CreateScanFlags(char cc)
    {
        ushort scanFlag = 0;
        if (cc == '\0') scanFlag = (ushort)ScanFlags.NullCharacter;
        else if (cc == '\n' || cc == '\r') scanFlag = (ushort)ScanFlags.NewlineOrCarriageReturn;
        else if (cc == ' ' || cc == '\x09' || cc == '\x0C') scanFlag = (ushort)ScanFlags.WhitespaceNotNewline;
        else if (cc == '&') scanFlag = (ushort)ScanFlags.Ampersand;
        else if (cc == '<') scanFlag = (ushort)ScanFlags.OpenTag;
        else if (cc == '/' || cc == '>') scanFlag = (ushort)ScanFlags.SlashAndCloseTag;
        else if (cc == '=') scanFlag = (ushort)ScanFlags.Equal;
        else if (cc == '"' || cc == '\'') scanFlag = (ushort)ScanFlags.Quotes;
        else if (cc == '{') scanFlag = (ushort)ScanFlags.OpenBrace;
        return scanFlag;
    }

    private static readonly ushort[] CharacterScanFlags = BuildScanFlags();

    private static ushort[] BuildScanFlags()
    {
        var table = new ushort[128];
        for (int i = 0; i < 128; i++)
            table[i] = CreateScanFlags((char)i);
        return table;
    }

    private static bool CheckScanFlag(char cc, ScanFlags flag)
    {
        if (cc >= 128) return false;
        return (CharacterScanFlags[cc] & (ushort)flag) != 0;
    }

    private static bool IsAsciiAlpha(char cc) => (cc >= 'a' && cc <= 'z') || (cc >= 'A' && cc <= 'Z');
    private static bool IsAsciiUpper(char cc) => cc >= 'A' && cc <= 'Z';
    private static bool IsAsciiDigit(char cc) => cc >= '0' && cc <= '9';
    private static bool IsAsciiHexDigit(char cc) => IsAsciiDigit(cc) || (cc >= 'a' && cc <= 'f') || (cc >= 'A' && cc <= 'F');
    private static bool IsAsciiAlphanumeric(char cc) => IsAsciiAlpha(cc) || IsAsciiDigit(cc);

    private static int ToAsciiHexValue(char cc)
    {
        if (cc >= '0' && cc <= '9') return cc - '0';
        if (cc >= 'a' && cc <= 'f') return cc - 'a' + 10;
        return cc - 'A' + 10;
    }

    private static char ToLowerCase(char cc) => (char)(cc | 0x20);
    private static char ToLowerCaseIfAlpha(char cc) => IsAsciiUpper(cc) ? (char)(cc | 0x20) : cc;

    private static bool IsTokenizerWhitespace(char cc) => cc == ' ' || cc == '\t' || cc == '\n' || cc == '\r' || cc == '\f';

    private static bool IsEndTagBufferingState(State state)
    {
        switch (state)
        {
            case State.RcdataEndTagOpenState:
            case State.RcdataEndTagNameState:
            case State.RawtextEndTagOpenState:
            case State.RawtextEndTagNameState:
            case State.ScriptDataEndTagOpenState:
            case State.ScriptDataEndTagNameState:
            case State.ScriptDataEscapedEndTagOpenState:
            case State.ScriptDataEscapedEndTagNameState:
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------------
    // Helpers ported from html_tokenizer.cc.
    // ---------------------------------------------------------------------
    private bool HaveBufferedCharacterToken() => _token.Type == HtmlToken.TokenType.Character;

    private void BufferCharacter(char character)
    {
        _token.EnsureIsCharacterToken();
        _token.AppendToCharacter(character);
    }

    private void SaveEndTagNameIfNeeded()
    {
        if (_token.Type == HtmlToken.TokenType.StartTag)
        {
            _appropriateEndTagName.Clear();
            _appropriateEndTagName.Append(_token.GetName());
        }
    }

    private bool ProcessEntity(SegmentedString source)
    {
        bool notEnoughCharacters = false;
        var decodedEntity = new DecodedHtmlEntity();
        bool success = HtmlEntityParser.ConsumeHtmlEntity(source, ref decodedEntity, out notEnoughCharacters);
        if (notEnoughCharacters)
            return false;
        if (!success)
        {
            BufferCharacter('&');
        }
        else
        {
            for (int i = 0; i < decodedEntity.Length; i++)
                BufferCharacter(decodedEntity.Data[i]);
        }
        return true;
    }

    private bool FlushBufferedEndTag(SegmentedString source, bool currentCharMayBeNewline)
    {
        if (currentCharMayBeNewline)
            source.AdvanceAndUpdateLineNumber();
        else
            source.AdvancePastNonNewline();
        if (_token.Type == HtmlToken.TokenType.Character)
            return true;
        _token.BeginEndTag(_bufferedEndTagName.ToString());
        _bufferedEndTagName.Clear();
        _appropriateEndTagName.Clear();
        _temporaryBuffer.Clear();
        return false;
    }

    private bool FlushEmitAndResumeInDataState(SegmentedString source)
    {
        _state = State.DataState;
        FlushBufferedEndTag(source, currentCharMayBeNewline: false);
        return true;
    }

    private bool EmitAndResumeInDataState(SegmentedString source)
    {
        SaveEndTagNameIfNeeded();
        _state = State.DataState;
        source.AdvancePastNonNewline();
        return true;
    }

    private bool EmitAndReconsumeInDataState()
    {
        SaveEndTagNameIfNeeded();
        _state = State.DataState;
        return true;
    }

    private bool EmitEndOfFile(SegmentedString source)
    {
        if (HaveBufferedCharacterToken())
            return true;
        _state = State.DataState;
        source.AdvanceAndUpdateLineNumber();
        _token.Clear();
        _token.MakeEndOfFile();
        return true;
    }

    private void AddToPossibleEndTag(char cc) => _bufferedEndTagName.Append(cc);

    private bool IsAppropriateEndTag()
    {
        if (_bufferedEndTagName.Length != _appropriateEndTagName.Length)
            return false;
        return _bufferedEndTagName.ToString().Equals(_appropriateEndTagName.ToString(), StringComparison.Ordinal);
    }

    private bool TemporaryBufferIs(string expectedString)
    {
        return _temporaryBuffer.Length == expectedString.Length &&
            _temporaryBuffer.ToString().Equals(expectedString, StringComparison.Ordinal);
    }

    private void AdvanceString(SegmentedString source, string text)
    {
        foreach (char c in text)
            source.AdvanceAndAssert(c);
    }

    private bool SkipWhitespaces(SegmentedString source, ref char cc)
    {
        if (!CheckScanFlag(cc, ScanFlags.Whitespace))
            return true;
        return SkipWhitespacesHelper(source, ref cc);
    }

    private bool SkipWhitespacesHelper(SegmentedString source, ref char cc)
    {
        cc = source.CurrentChar();
        while (true)
        {
            while (CheckScanFlag(cc, ScanFlags.WhitespaceNotNewline))
                cc = source.AdvancePastNonNewline();
            switch (cc)
            {
                case '\n':
                    cc = source.AdvancePastNewlineAndUpdateLineNumber();
                    break;
                case '\r':
                    if (!_inputStreamPreprocessor.AdvancePastCarriageReturn(source, ref cc))
                        return false;
                    break;
                case '\0':
                    if (!_inputStreamPreprocessor.ProcessNullCharacter(source, ref cc))
                        return false;
                    if (cc == '\0')
                        return true;
                    break;
                default:
                    return true;
            }
        }
    }

    private bool EmitData(SegmentedString source, char cc)
    {
        _token.EnsureIsCharacterToken();
        if (cc == '\n')
            cc = source.CurrentChar();
        while (true)
        {
            while (!CheckScanFlag(cc, ScanFlags.CharacterTokenSpecial))
            {
                _token.AppendToCharacter(cc);
                cc = source.AdvancePastNonNewline();
            }
            switch (cc)
            {
                case '&':
                    _state = State.CharacterReferenceInDataState;
                    source.AdvanceAndAssert('&');
                    if (!ProcessEntity(source))
                        return true;
                    _state = State.DataState;
                    if (source.IsEmpty)
                        return true;
                    cc = source.CurrentChar();
                    break;
                case '\n':
                    _token.AppendToCharacter(cc);
                    cc = source.AdvancePastNewlineAndUpdateLineNumber();
                    break;
                case '\r':
                    _token.AppendToCharacter('\n');
                    if (!_inputStreamPreprocessor.AdvancePastCarriageReturn(source, ref cc))
                        return true;
                    break;
                case '<':
                    return true;
                case '\0':
                    if (!_inputStreamPreprocessor.ProcessNullCharacter(source, ref cc))
                        return true;
                    if (cc == '\0')
                        return EmitEndOfFile(source);
                    break;
                case '{':
                    _token.AppendToCharacter(cc);
                    cc = source.AdvancePastNonNewline();
                    break;
                default:
                    return true;
            }
        }
    }

    private bool EmitPlaintext(SegmentedString source, char cc)
    {
        _token.EnsureIsCharacterToken();
        if (cc == '\n')
            cc = source.CurrentChar();
        while (true)
        {
            while (!CheckScanFlag(cc, ScanFlags.NullOrNewline))
            {
                _token.AppendToCharacter(cc);
                cc = source.AdvancePastNonNewline();
            }
            switch (cc)
            {
                case '\n':
                    _token.AppendToCharacter(cc);
                    cc = source.AdvancePastNewlineAndUpdateLineNumber();
                    break;
                case '\r':
                    _token.AppendToCharacter('\n');
                    if (!_inputStreamPreprocessor.AdvancePastCarriageReturn(source, ref cc))
                        return true;
                    break;
                case '\0':
                    if (!_inputStreamPreprocessor.ProcessNullCharacter(source, ref cc))
                        return true;
                    if (cc == '\0')
                        return EmitEndOfFile(source);
                    break;
                default:
                    return true;
            }
        }
    }

    public string BufferedCharacters()
    {
        if (NumberOfBufferedCharacters == 0)
            return string.Empty;
        return "</" + _temporaryBuffer;
    }

    public int NumberOfBufferedCharacters => _temporaryBuffer.Length != 0 ? _temporaryBuffer.Length + 2 : 0;
}