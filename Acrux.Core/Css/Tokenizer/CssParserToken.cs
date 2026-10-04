using System.Globalization;

namespace Acrux.Core.Css.Tokenizer;

public struct CssParserToken
{
    public CssTokenType Type { get; set; }
    public string Value { get; set; }
    public double NumericValue { get; set; }
    public NumberType NumericType { get; set; }
    public HashTokenType HashType { get; set; }
    public string Unit { get; set; }
    public string FunctionName { get; set; }
    public uint UnicodeRangeStart { get; set; }
    public uint UnicodeRangeEnd { get; set; }
    public bool HasStringVal { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }

    public static CssParserToken CreateIdent(string value) =>
        new() { Type = CssTokenType.IdentToken, Value = value, HasStringVal = true };

    public static CssParserToken CreateFunction(string name) =>
        new() { Type = CssTokenType.FunctionToken, FunctionName = name, HasStringVal = true };

    public static CssParserToken CreateAtKeyword(string value) =>
        new() { Type = CssTokenType.AtKeywordToken, Value = value, HasStringVal = true };

    public static CssParserToken CreateHash(string value, HashTokenType hashType) =>
        new() { Type = CssTokenType.HashToken, Value = value, HashType = hashType, HasStringVal = true };

    public static CssParserToken CreateString(string value) =>
        new() { Type = CssTokenType.StringToken, Value = value, HasStringVal = true };

    public static CssParserToken CreateBadString() =>
        new() { Type = CssTokenType.BadStringToken };

    public static CssParserToken CreateUrl(string value) =>
        new() { Type = CssTokenType.UrlToken, Value = value };

    public static CssParserToken CreateBadUrl() =>
        new() { Type = CssTokenType.BadUrlToken };

    public static CssParserToken CreateDelimiter(char c) =>
        new() { Type = CssTokenType.DelimiterToken, Value = c.ToString() };

    public static CssParserToken CreateNumber(double value, NumberType type) =>
        new() { Type = CssTokenType.NumberToken, NumericValue = value, NumericType = type };

    public static CssParserToken CreatePercentage(double value) =>
        new() { Type = CssTokenType.PercentageToken, NumericValue = value };

    public static CssParserToken CreateDimension(double value, string unit, NumberType type) =>
        new() { Type = CssTokenType.DimensionToken, NumericValue = value, Unit = unit, NumericType = type };

    public static CssParserToken CreateWhitespace() =>
        new() { Type = CssTokenType.WhitespaceToken };

    public static CssParserToken CreateCdo() =>
        new() { Type = CssTokenType.CdoToken };

    public static CssParserToken CreateCdc() =>
        new() { Type = CssTokenType.CdcToken };

    public static CssParserToken CreateColon() =>
        new() { Type = CssTokenType.ColonToken };

    public static CssParserToken CreateSemicolon() =>
        new() { Type = CssTokenType.SemicolonToken };

    public static CssParserToken CreateComma() =>
        new() { Type = CssTokenType.CommaToken };

    public static CssParserToken CreateLeftSquare() =>
        new() { Type = CssTokenType.LeftSquareBracketToken };

    public static CssParserToken CreateRightSquare() =>
        new() { Type = CssTokenType.RightSquareBracketToken };

    public static CssParserToken CreateLeftParen() =>
        new() { Type = CssTokenType.LeftParenthesisToken };

    public static CssParserToken CreateRightParen() =>
        new() { Type = CssTokenType.RightParenthesisToken };

    public static CssParserToken CreateLeftBrace() =>
        new() { Type = CssTokenType.LeftBraceToken };

    public static CssParserToken CreateRightBrace() =>
        new() { Type = CssTokenType.RightBraceToken };

    public static CssParserToken CreateIncludeMatch() =>
        new() { Type = CssTokenType.IncludeMatchToken };

    public static CssParserToken CreateDashMatch() =>
        new() { Type = CssTokenType.DashMatchToken };

    public static CssParserToken CreatePrefixMatch() =>
        new() { Type = CssTokenType.PrefixMatchToken };

    public static CssParserToken CreateSuffixMatch() =>
        new() { Type = CssTokenType.SuffixMatchToken };

    public static CssParserToken CreateSubstringMatch() =>
        new() { Type = CssTokenType.SubstringMatchToken };

    public static CssParserToken CreateColumn() =>
        new() { Type = CssTokenType.ColumnToken };

    public static CssParserToken CreateUnicodeRange(uint start, uint end) =>
        new() { Type = CssTokenType.UnicodeRangeToken, UnicodeRangeStart = start, UnicodeRangeEnd = end };

    public static CssParserToken CreateComment(string value) =>
        new() { Type = CssTokenType.CommentToken, Value = value };

    public static readonly CssParserToken Eof = new() { Type = CssTokenType.EofToken };

    public bool IsEof => Type == CssTokenType.EofToken;

    /// <summary>
    /// Serializes this token back to its CSS source text. Unlike <see cref="Value"/>,
    /// this reproduces numbers with their units, quoted strings, function names and
    /// operators so a token stream can be faithfully turned back into CSS text.
    /// </summary>
    public string ToCssText()
    {
        switch (Type)
        {
            case CssTokenType.IdentToken: return Value;
            case CssTokenType.FunctionToken: return FunctionName + "(";
            case CssTokenType.AtKeywordToken: return "@" + Value;
            case CssTokenType.HashToken: return "#" + Value;
            case CssTokenType.StringToken: return "\"" + EscapeString(Value) + "\"";
            case CssTokenType.BadStringToken: return "\"";
            case CssTokenType.UrlToken: return "url(" + Value + ")";
            case CssTokenType.BadUrlToken: return "url(";
            case CssTokenType.DelimiterToken: return Value;
            case CssTokenType.NumberToken: return FormatNumber(NumericValue);
            case CssTokenType.PercentageToken: return FormatNumber(NumericValue) + "%";
            case CssTokenType.DimensionToken: return FormatNumber(NumericValue) + Unit;
            case CssTokenType.WhitespaceToken: return " ";
            case CssTokenType.CdoToken: return "<!--";
            case CssTokenType.CdcToken: return "-->";
            case CssTokenType.ColonToken: return ":";
            case CssTokenType.SemicolonToken: return ";";
            case CssTokenType.CommaToken: return ",";
            case CssTokenType.LeftSquareBracketToken: return "[";
            case CssTokenType.RightSquareBracketToken: return "]";
            case CssTokenType.LeftParenthesisToken: return "(";
            case CssTokenType.RightParenthesisToken: return ")";
            case CssTokenType.LeftBraceToken: return "{";
            case CssTokenType.RightBraceToken: return "}";
            case CssTokenType.IncludeMatchToken: return "~=";
            case CssTokenType.DashMatchToken: return "|=";
            case CssTokenType.PrefixMatchToken: return "^=";
            case CssTokenType.SuffixMatchToken: return "$=";
            case CssTokenType.SubstringMatchToken: return "*=";
            case CssTokenType.ColumnToken: return "||";
            case CssTokenType.UnicodeRangeToken:
                return "U+" + UnicodeRangeStart.ToString("X") + "-" + UnicodeRangeEnd.ToString("X");
            case CssTokenType.CommentToken: return "/*" + Value + "*/";
            case CssTokenType.EofToken: return "";
            default: return Value;
        }
    }

    private static string FormatNumber(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? value.ToString("0.###############", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string EscapeString(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\a "); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    public override string ToString() => Type switch
    {
        CssTokenType.IdentToken => $"IDENT({Value})",
        CssTokenType.FunctionToken => $"FUNCTION({FunctionName})",
        CssTokenType.AtKeywordToken => $"AT({Value})",
        CssTokenType.HashToken => $"HASH({Value})",
        CssTokenType.StringToken => $"STRING({Value})",
        CssTokenType.UrlToken => $"URL({Value})",
        CssTokenType.NumberToken => $"NUMBER({NumericValue})",
        CssTokenType.PercentageToken => $"PERCENTAGE({NumericValue})",
        CssTokenType.DimensionToken => $"DIMENSION({NumericValue}{Unit})",
        CssTokenType.WhitespaceToken => "WS",
        CssTokenType.ColonToken => "COLON",
        CssTokenType.SemicolonToken => "SEMICOLON",
        CssTokenType.CommaToken => "COMMA",
        CssTokenType.LeftBraceToken => "LBRACE",
        CssTokenType.RightBraceToken => "RBRACE",
        CssTokenType.LeftParenthesisToken => "LPAREN",
        CssTokenType.RightParenthesisToken => "RPAREN",
        CssTokenType.LeftSquareBracketToken => "LSQUARE",
        CssTokenType.RightSquareBracketToken => "RSQUARE",
        CssTokenType.DelimiterToken => $"DELIM({Value})",
        CssTokenType.EofToken => "EOF",
        _ => Type.ToString()
    };
}