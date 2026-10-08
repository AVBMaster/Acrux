using System.Text;
using System.Globalization;

namespace Acrux.Core.Css.Tokenizer;

public class CssTokenizer
{
    private readonly string _input;
    private int _pos;
    private int _prevPos;
    private int _tokenCount;
    private readonly List<CssTokenType> _blockStack = new();
    private readonly List<string> _stringPool = new();
    private bool _unicodeRangesAllowed;
    private int _line;
    private int _column;

    public CssTokenizer(string input, int offset = 0)
    {
        _input = Preprocess(input);
        _pos = offset;
        _line = 1;
        _column = 1;
    }

    public int Offset => _pos;
    public int PreviousOffset => _prevPos;
    public int TokenCount => _tokenCount;
    public int Line => _line;
    public int Column => _column;

    public void SetUnicodeRangesAllowed(bool allowed) => _unicodeRangesAllowed = allowed;

    public CssParserToken TokenizeSingle()
    {
        return NextToken(skipComments: false);
    }

    public CssParserToken TokenizeSingleWithComments()
    {
        return NextToken(skipComments: true);
    }

    public void SkipToEndOfBlock(int offset)
    {
        _pos = offset;
        _blockStack.Clear();
    }

    public CssParserToken Restore(CssParserToken next, int offset)
    {
        _pos = offset;
        _blockStack.Clear();
        return next;
    }

    public string StringRangeFrom(int start)
    {
        return _input[start.._pos];
    }

    public string StringRangeAt(int start, int length)
    {
        return _input.Substring(start, length);
    }

    private static string Preprocess(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '\r' && i + 1 < input.Length && input[i + 1] == '\n')
                continue;
            if (c == '\r' || c == '\f')
            {
                sb.Append('\n');
                continue;
            }
            if (c == '\0')
            {
                sb.Append('\uFFFD');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private char Peek()
    {
        return _pos < _input.Length ? _input[_pos] : '\0';
    }

    private char Peek(int ahead)
    {
        int idx = _pos + ahead;
        return idx < _input.Length ? _input[idx] : '\0';
    }

    private char Consume()
    {
        if (_pos >= _input.Length) return '\0';
        char c = _input[_pos];
        _pos++;
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        return c;
    }

    private void Reconsume()
    {
        _pos--;
        _column--;
        if (_pos >= 0 && _input[_pos] == '\n')
        {
            _line--;
        }
    }

    private bool IsWhitespace(char c) =>
        c == ' ' || c == '\t' || c == '\n';

    private bool IsDigit(char c) => c >= '0' && c <= '9';

    private bool IsLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private bool IsIdentStart(char c) =>
        IsLetter(c) || c == '_' || c == '-' || c == '\uFFFD';

    private bool IsIdentChar(char c) =>
        IsIdentStart(c) || IsDigit(c) || c == '-';

    private bool IsNonAscii(char c) => c > '\u007F';

    private bool IsNameStart(char c) =>
        IsLetter(c) || c == '_' || IsNonAscii(c) || c == '\uFFFD';

    private bool IsNameChar(char c) =>
        IsNameStart(c) || IsDigit(c) || c == '-';

    private bool IsNonPrintable(char c) =>
        c < '\u0020' && c != '\n' && c != '\t';

    private bool IsHexDigit(char c) =>
        IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0
    };

    private bool NextTwoCharsAreValidEscape()
    {
        if (Peek() != '\\') return false;
        char next = Peek(1);
        return next != '\0' && !IsWhitespace(next) && next != '\n';
    }

    private bool NextCharsAreNumber()
    {
        char c = Peek();
        if (c == '+' || c == '-')
        {
            char c2 = Peek(1);
            if (IsDigit(c2)) return true;
            if (c2 == '.' && IsDigit(Peek(2))) return true;
            return false;
        }
        if (c == '.')
        {
            return IsDigit(Peek(1));
        }
        return IsDigit(c);
    }

    private bool NextCharsAreNumber(char first)
    {
        if (first == '+' || first == '-')
        {
            char c2 = Peek(1);
            if (IsDigit(c2)) return true;
            if (c2 == '.' && IsDigit(Peek(2))) return true;
            return false;
        }
        if (first == '.')
        {
            return IsDigit(Peek(1));
        }
        return IsDigit(first);
    }

    private bool NextCharsAreIdentifier()
    {
        char c = Peek();
        if (c == '-')
        {
            char c2 = Peek(1);
            if (IsNameStart(c2) || c2 == '-' || c2 == '\uFFFD') return true;
            if (c2 == '\\' && NextTwoCharsAreValidEscape()) return true;
            return false;
        }
        if (IsNameStart(c)) return true;
        if (c == '\\' && NextTwoCharsAreValidEscape()) return true;
        return false;
    }

    private bool NextCharsAreIdentifier(char first)
    {
        if (first == '-')
        {
            char c2 = Peek(1);
            if (IsNameStart(c2) || c2 == '-' || c2 == '\uFFFD') return true;
            if (c2 == '\\' && NextTwoCharsAreValidEscape()) return true;
            return false;
        }
        if (IsNameStart(first)) return true;
        if (first == '\\' && NextTwoCharsAreValidEscape()) return true;
        return false;
    }

    private string ConsumeEscape()
    {
        if (Peek() != '\\') return string.Empty;
        Consume();
        return ConsumeEscapeBody();
    }

    /// <summary>Consumes the body of an escape sequence whose leading
    /// backslash has already been consumed by the caller.</summary>
    private string ConsumeEscapeBody()
    {
        if (Peek() == '\0') return string.Empty;
        _prevPos = _pos;

        char c = Consume();
        if (IsHexDigit(c))
        {
            string hex = c.ToString();
            int count = 1;
            while (count < 6 && IsHexDigit(Peek()))
            {
                hex += Consume();
                count++;
            }
            if (IsWhitespace(Peek()))
                Consume();

            int code = int.Parse(hex, NumberStyles.HexNumber);
            if (code == 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
                return "\uFFFD";
            return char.ConvertFromUtf32(code);
        }
        return c.ToString();
    }

    private string ConsumeName()
    {
        var sb = new StringBuilder();
        while (_pos < _input.Length)
        {
            char c = Peek();
            if (IsNameChar(c))
            {
                sb.Append(Consume());
            }
            else if (NextTwoCharsAreValidEscape())
            {
                sb.Append(ConsumeEscape());
            }
            else
            {
                break;
            }
        }
        return sb.ToString();
    }

    private double ConsumeNumber()
    {
        var sb = new StringBuilder();
        NumberType type = NumberType.Integer;

        char c = Peek();
        if (c == '+' || c == '-')
        {
            sb.Append(Consume());
        }

        while (IsDigit(Peek()))
        {
            sb.Append(Consume());
        }

        if (Peek() == '.' && IsDigit(Peek(1)))
        {
            sb.Append(Consume());
            while (IsDigit(Peek()))
                sb.Append(Consume());
            type = NumberType.Number;
        }

        c = Peek();
        if (c == 'e' || c == 'E')
        {
            char next = Peek(1);
            if (IsDigit(next) || ((next == '+' || next == '-') && IsDigit(Peek(2))))
            {
                sb.Append(Consume());
                if (Peek() == '+' || Peek() == '-')
                    sb.Append(Consume());
                while (IsDigit(Peek()))
                    sb.Append(Consume());
                type = NumberType.Number;
            }
        }

        if (double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return result;
        return 0;
    }

    private CssParserToken ConsumeNumericToken()
    {
        _prevPos = _pos;
        double num = ConsumeNumber();
        NumberType numType = NumberType.Integer;

        string numStr = _input[_prevPos.._pos];
        if (numStr.Contains('.') || numStr.Contains('e') || numStr.Contains('E'))
            numType = NumberType.Number;

        if (Peek() == '%')
        {
            Consume();
            return CssParserToken.CreatePercentage(num);
        }

        if (NextCharsAreIdentifier())
        {
            string unit = ConsumeName();
            return CssParserToken.CreateDimension(num, unit, numType);
        }

        return CssParserToken.CreateNumber(num, numType);
    }

    private CssParserToken ConsumeIdentLikeToken()
    {
        string name = ConsumeName();
        if (Peek() == '(')
        {
            if (string.Equals(name, "url", StringComparison.OrdinalIgnoreCase))
            {
                char next = Peek(1);
                if (next != '\'' && next != '"')
                    return ConsumeUrlToken();
                // A quoted address is still one url token, so the string is only the first half of
                // it: 'url("a.png")' is an address, 'url('a'b.png)' is a bad-url token and the
                // declaration that carried it (CSS Syntax 3 §4.3.3).
                if (!QuotedUrlIsClosed())
                    return ConsumeUrlToken();
            }
            Consume();
            _blockStack.Add(CssTokenType.RightParenthesisToken);
            return CssParserToken.CreateFunction(name);
        }
        return CssParserToken.CreateIdent(name);
    }

    /// <summary>Whether the '(' the cursor is on opens a url token whose quoted address closes
    /// before the ')'. Nothing but whitespace may stand between the string and the parenthesis;
    /// anything else is swept by the url token as a bad one.</summary>
    private bool QuotedUrlIsClosed()
    {
        char quote = Peek(1);
        int p = _pos + 2;
        while (p < _input.Length)
        {
            char c = _input[p];
            if (c == '\n') return false;
            if (c == '\\')
            {
                p += 2;
                continue;
            }
            if (c == quote) { p++; break; }
            p++;
        }
        while (p < _input.Length && IsWhitespace(_input[p])) p++;
        return p < _input.Length && _input[p] == ')';
    }

    private CssParserToken ConsumeStringTokenUntil(char endChar)
    {
        var sb = new StringBuilder();
        while (_pos < _input.Length)
        {
            char c = Consume();
            if (c == endChar)
                return CssParserToken.CreateString(sb.ToString());
            if (c == '\n')
            {
                _pos--;
                return CssParserToken.CreateBadString();
            }
            if (c == '\\')
            {
                if (Peek() == '\n')
                {
                    Consume();
                }
                else if (Peek() != '\0')
                {
                    sb.Append(ConsumeEscapeBody());
                }
                else
                {
                    sb.Append('\\');
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return CssParserToken.CreateString(sb.ToString());
    }

    private CssParserToken ConsumeUrlToken()
    {
        Consume();
        while (IsWhitespace(Peek())) Consume();
        if (Peek() == '\'' || Peek() == '"')
        {
            var strToken = ConsumeStringTokenUntil(Peek() == '\'' ? '\'' : '"');
            if (strToken.Type == CssTokenType.BadStringToken)
            {
                ConsumeBadUrlRemnants();
                return CssParserToken.CreateBadUrl();
            }
            while (IsWhitespace(Peek())) Consume();
            if (Peek() == ')')
            {
                Consume();
                return CssParserToken.CreateUrl(strToken.Value);
            }
            ConsumeBadUrlRemnants();
            return CssParserToken.CreateBadUrl();
        }

        var sb = new StringBuilder();
        while (_pos < _input.Length)
        {
            char c = Peek();
            if (c == ')')
            {
                Consume();
                return CssParserToken.CreateUrl(sb.ToString());
            }
            if (IsWhitespace(c))
            {
                Consume();
                while (IsWhitespace(Peek())) Consume();
                if (Peek() == ')')
                {
                    Consume();
                    return CssParserToken.CreateUrl(sb.ToString());
                }
                ConsumeBadUrlRemnants();
                return CssParserToken.CreateBadUrl();
            }
            if (c == '\'' || c == '"' || c == '(' || IsNonPrintable(c))
            {
                ConsumeBadUrlRemnants();
                return CssParserToken.CreateBadUrl();
            }
            if (c == '\\' && NextTwoCharsAreValidEscape())
            {
                sb.Append(ConsumeEscape());
            }
            else
            {
                sb.Append(Consume());
            }
        }
        return CssParserToken.CreateBadUrl();
    }

    private void ConsumeBadUrlRemnants()
    {
        while (_pos < _input.Length)
        {
            char c = Consume();
            if (c == ')') return;
            if (c == '\\' && Peek() != '\0' && !IsWhitespace(Peek()))
                ConsumeEscapeBody();
        }
    }

    private void ConsumeSingleWhitespaceIfNext()
    {
        if (IsWhitespace(Peek()))
            Consume();
    }

    private void ConsumeUntilCommentEndFound()
    {
        while (_pos < _input.Length)
        {
            if (Consume() == '*' && Peek() == '/')
            {
                Consume();
                return;
            }
        }
    }

    /// <summary>CSS Syntax 3 §4.3.15: 'U+' and one to six hexadecimal digits, a run of '?' that
    /// stands for the digits it does not name, or a '-' and the last digits of a second number. The
    /// two numbers are what the range means and what a reference engine prints, so a wildcard is
    /// read as the span it covers — 'U+??' is the range 0x00 to 0xFF and prints 'U+0-FF' (measured)
    /// — and a range that names one codepoint prints that codepoint alone.</summary>
    private CssParserToken ConsumeUnicodeRange()
    {
        Consume();
        Consume();

        string hex = "";
        while (hex.Length < 6 && IsHexDigit(Peek()))
            hex += Consume();

        uint from = hex.Length > 0 ? uint.Parse(hex, NumberStyles.HexNumber) : 0;
        uint to = from;

        if (Peek() == '?')
        {
            int wildcards = 0;
            while (wildcards < 6 - hex.Length && Peek() == '?')
            {
                Consume();
                wildcards++;
            }
            from = from & ~((1u << (wildcards * 4)) - 1);
            to = from | ((1u << (wildcards * 4)) - 1);
        }
        else if (Peek() == '-' && IsHexDigit(Peek(1)))
        {
            Consume();
            string endHex = "";
            while (endHex.Length < 6 && IsHexDigit(Peek()))
                endHex += Consume();
            if (endHex.Length > 0)
                to = uint.Parse(endHex, NumberStyles.HexNumber);
        }

        return CssParserToken.CreateUnicodeRange(from, to);
    }

    private CssParserToken HyphenMinus()
    {
        if (NextCharsAreNumber('-'))
        {
            return ConsumeNumericToken();
        }
        if (Peek(1) == '-' && Peek(2) == '>')
        {
            Consume(); Consume(); Consume();
            return CssParserToken.CreateCdc();
        }
        if (NextCharsAreIdentifier('-'))
        {
            return ConsumeIdentLikeToken();
        }
        Consume();
        return CssParserToken.CreateDelimiter('-');
    }

    private CssParserToken Hash()
    {
        Consume();
        char c = Peek();
        if (IsNameChar(c) || (c == '\\' && NextTwoCharsAreValidEscape()))
        {
            bool isId = NextCharsAreIdentifier();
            string name = ConsumeName();
            return CssParserToken.CreateHash(name, isId ? HashTokenType.Id : HashTokenType.Unrestricted);
        }
        return CssParserToken.CreateDelimiter('#');
    }

    private CssParserToken LetterU()
    {
        // CSS Syntax 3 §4.3.14: only a stream that was told unicode ranges are allowed — which is
        // a '@font-face' block and nothing else — reads 'U+41' as one token. Elsewhere the same
        // characters are an ident and a number, and the '+' of a range that starts with a wildcard
        // ('U+??') still makes it a range rather than an ident.
        if (_unicodeRangesAllowed && (Peek() is 'u' or 'U') && Peek(1) == '+' &&
            (IsHexDigit(Peek(2)) || Peek(2) == '?'))
            return ConsumeUnicodeRange();
        return ConsumeIdentLikeToken();
    }

    private CssParserToken BlockStart(CssTokenType startType)
    {
        _blockStack.Add(EndFor(startType));
        Consume();
        return startType switch
        {
            CssTokenType.LeftParenthesisToken => CssParserToken.CreateLeftParen(),
            CssTokenType.LeftSquareBracketToken => CssParserToken.CreateLeftSquare(),
            CssTokenType.LeftBraceToken => CssParserToken.CreateLeftBrace(),
            _ => CssParserToken.CreateDelimiter('?')
        };
    }

    private CssParserToken BlockEnd(CssTokenType endType, CssTokenType startType)
    {
        if (_blockStack.Count > 0 && _blockStack[^1] == endType)
            _blockStack.RemoveAt(_blockStack.Count - 1);
        Consume();
        return endType switch
        {
            CssTokenType.RightParenthesisToken => CssParserToken.CreateRightParen(),
            CssTokenType.RightSquareBracketToken => CssParserToken.CreateRightSquare(),
            CssTokenType.RightBraceToken => CssParserToken.CreateRightBrace(),
            _ => CssParserToken.CreateDelimiter('?')
        };
    }

    private static CssTokenType EndFor(CssTokenType startType) => startType switch
    {
        CssTokenType.LeftParenthesisToken => CssTokenType.RightParenthesisToken,
        CssTokenType.LeftSquareBracketToken => CssTokenType.RightSquareBracketToken,
        CssTokenType.LeftBraceToken => CssTokenType.RightBraceToken,
        _ => CssTokenType.EofToken
    };

    private CssParserToken NextToken(bool skipComments)
    {
        _tokenCount++;
        _prevPos = _pos;
        int tokenLine = _line;
        int tokenColumn = _column;

        CssParserToken result = NextTokenCore(skipComments);
        result.Line = tokenLine;
        result.Column = tokenColumn;
        return result;
    }

    private CssParserToken NextTokenCore(bool skipComments)
    {
        while (_pos < _input.Length)
        {
            char c = Peek();

            switch (c)
            {
                case '\0':
                    return CssParserToken.Eof;

                case ' ':
                case '\t':
                case '\n':
                    Consume();
                    while (IsWhitespace(Peek())) Consume();
                    return CssParserToken.CreateWhitespace();

                case '"':
                case '\'':
                    Consume();
                    return ConsumeStringTokenUntil(c);

                case '#':
                    return Hash();

                case '$':
                    Consume();
                    if (Peek() == '=') { Consume(); return CssParserToken.CreateSuffixMatch(); }
                    return CssParserToken.CreateDelimiter('$');

                case '(':
                    return BlockStart(CssTokenType.LeftParenthesisToken);

                case ')':
                    return BlockEnd(CssTokenType.RightParenthesisToken, CssTokenType.LeftParenthesisToken);

                case '*':
                    Consume();
                    if (Peek() == '=') { Consume(); return CssParserToken.CreateSubstringMatch(); }
                    return CssParserToken.CreateDelimiter('*');

                case '+':
                    if (NextCharsAreNumber('+'))
                    {
                        return ConsumeNumericToken();
                    }
                    Consume();
                    return CssParserToken.CreateDelimiter('+');

                case ',':
                    Consume();
                    return CssParserToken.CreateComma();

                case '-':
                    if (NextCharsAreNumber('-'))
                    {
                        return ConsumeNumericToken();
                    }
                    if (Peek(1) == '-' && Peek(2) == '>')
                    {
                        Consume(); Consume(); Consume();
                        return CssParserToken.CreateCdc();
                    }
                    if (NextCharsAreIdentifier('-'))
                    {
                        return ConsumeIdentLikeToken();
                    }
                    Consume();
                    return CssParserToken.CreateDelimiter('-');

                case '.':
                    if (NextCharsAreNumber('.'))
                    {
                        return ConsumeNumericToken();
                    }
                    Consume();
                    return CssParserToken.CreateDelimiter('.');

                case '/':
                    Consume();
                    if (Peek() == '*')
                    {
                        Consume();
                        int commentStart = _pos;
                        string comment = "";
                        while (_pos < _input.Length)
                        {
                            if (Peek() == '*' && Peek(1) == '/')
                            {
                                comment = _input[commentStart.._pos];
                                Consume(); Consume();
                                break;
                            }
                            Consume();
                        }
                        if (skipComments)
                        {
                            return CssParserToken.CreateComment(comment);
                        }
                        continue;
                    }
                    return CssParserToken.CreateDelimiter('/');

                case ':':
                    Consume();
                    return CssParserToken.CreateColon();

                case ';':
                    Consume();
                    return CssParserToken.CreateSemicolon();

                case '<':
                    Consume();
                    if (Peek() == '!' && Peek(1) == '-' && Peek(2) == '-')
                    {
                        Consume(); Consume(); Consume();
                        return CssParserToken.CreateCdo();
                    }
                    return CssParserToken.CreateDelimiter('<');

                case '@':
                    Consume();
                    if (NextCharsAreIdentifier())
                    {
                        string name = ConsumeName();
                        return CssParserToken.CreateAtKeyword(name);
                    }
                    return CssParserToken.CreateDelimiter('@');

                case '[':
                    return BlockStart(CssTokenType.LeftSquareBracketToken);

                case '\\':
                    if (NextTwoCharsAreValidEscape())
                    {
                        return ConsumeIdentLikeToken();
                    }
                    Consume();
                    return CssParserToken.CreateDelimiter('\\');

                case ']':
                    return BlockEnd(CssTokenType.RightSquareBracketToken, CssTokenType.LeftSquareBracketToken);

                case '^':
                    Consume();
                    if (Peek() == '=') { Consume(); return CssParserToken.CreatePrefixMatch(); }
                    return CssParserToken.CreateDelimiter('^');

                case '{':
                    return BlockStart(CssTokenType.LeftBraceToken);

                case '|':
                    Consume();
                    if (Peek() == '=') { Consume(); return CssParserToken.CreateDashMatch(); }
                    if (Peek() == '|') { Consume(); return CssParserToken.CreateColumn(); }
                    return CssParserToken.CreateDelimiter('|');

                case '}':
                    return BlockEnd(CssTokenType.RightBraceToken, CssTokenType.LeftBraceToken);

                case '~':
                    Consume();
                    if (Peek() == '=') { Consume(); return CssParserToken.CreateIncludeMatch(); }
                    return CssParserToken.CreateDelimiter('~');

                case 'u':
                case 'U':
                    return LetterU();

                default:
                    if (IsDigit(c))
                    {
                        return ConsumeNumericToken();
                    }
                    if (IsNameStart(c))
                    {
                        return ConsumeIdentLikeToken();
                    }
                    if (c == '\uFFFD')
                    {
                        return ConsumeIdentLikeToken();
                    }
                    Consume();
                    return CssParserToken.CreateDelimiter(c);
            }
        }

        return CssParserToken.Eof;
    }
}