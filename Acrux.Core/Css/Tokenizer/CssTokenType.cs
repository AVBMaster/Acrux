namespace Acrux.Core.Css.Tokenizer;

public enum CssTokenType
{
    IdentToken,
    FunctionToken,
    AtKeywordToken,
    HashToken,
    StringToken,
    BadStringToken,
    UrlToken,
    BadUrlToken,
    DelimiterToken,
    NumberToken,
    PercentageToken,
    DimensionToken,
    WhitespaceToken,
    CdoToken,
    CdcToken,
    ColonToken,
    SemicolonToken,
    CommaToken,
    LeftSquareBracketToken,
    RightSquareBracketToken,
    LeftParenthesisToken,
    RightParenthesisToken,
    LeftBraceToken,
    RightBraceToken,
    IncludeMatchToken,
    DashMatchToken,
    PrefixMatchToken,
    SuffixMatchToken,
    SubstringMatchToken,
    ColumnToken,
    UnicodeRangeToken,
    CommentToken,
    EofToken
}

public enum NumberType
{
    Integer,
    Number
}

public enum HashTokenType
{
    Id,
    Unrestricted
}