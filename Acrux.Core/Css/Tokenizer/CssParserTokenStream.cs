namespace Acrux.Core.Css.Tokenizer;

/// <summary>
/// A token stream with one-token lookahead, mirroring Blink's CSSParserTokenStream.
/// Provides save/restore checkpointing for speculative parsing.
/// </summary>
public class CssParserTokenStream
{
    private readonly CssTokenizer _tokenizer;
    private CssParserToken _current;
    private CssParserToken _lookahead;
    private bool _hasLookahead;
    private int _currentOffset;
    private int _lookaheadOffset;
    private int _currentStart;
    private int _lookaheadStart;
    private int _currentLine;
    private int _currentColumn;

    public CssParserTokenStream(string input)
        : this(new CssTokenizer(input))
    {
    }

    public CssParserTokenStream(CssTokenizer tokenizer)
    {
        _tokenizer = tokenizer;
        _current = _tokenizer.TokenizeSingle();
        _currentOffset = _tokenizer.Offset;
        _currentStart = _tokenizer.PreviousOffset;
        _currentLine = _tokenizer.Line;
        _currentColumn = _tokenizer.Column;
    }

    public CssParserToken Current => _current;
    public int Offset => _currentOffset;

    /// <summary>Tell the tokenizer to read 'U+41' as one token. CSS Syntax 3 §4.3.14 allows a
    /// unicode range in the declarations of an '@font-face' block and nowhere else, so a stream
    /// that is not inside one leaves the same characters an ident and a number.</summary>
    public void SetUnicodeRangesAllowed(bool allowed) => _tokenizer.SetUnicodeRangesAllowed(allowed);

    /// <summary>Where the current token begins in the source. The stream keeps this for the token it
    /// is holding, so it stays right even while a looked-ahead token is waiting, which is what lets
    /// a caller that wants the text between two tokens can cut it out verbatim — the spelling a
    /// CSS '@import' 'supports()' keeps is the page's own, spaces and all.</summary>
    public int TokenStart => _currentStart;

    /// <summary>The source text between two offsets, exactly as the page wrote it.</summary>
    public string RawRange(int start, int end) =>
        end > start ? _tokenizer.StringRangeAt(start, end - start) : "";

    public int Line => _currentLine;
    public int Column => _currentColumn;
    public bool HasLookahead => _hasLookahead;

    public CssParserToken LookAhead()
    {
        if (!_hasLookahead)
        {
            _lookahead = _tokenizer.TokenizeSingle();
            _lookaheadOffset = _tokenizer.Offset;
            _lookaheadStart = _tokenizer.PreviousOffset;
            _hasLookahead = true;
        }
        return _lookahead;
    }

    public CssParserToken Next()
    {
        if (_hasLookahead)
        {
            _current = _lookahead;
            _currentOffset = _lookaheadOffset;
            _currentStart = _lookaheadStart;
            _currentLine = _lookahead.Line;
            _currentColumn = _lookahead.Column;
            _hasLookahead = false;
        }
        else
        {
            _current = _tokenizer.TokenizeSingle();
            _currentOffset = _tokenizer.Offset;
            _currentStart = _tokenizer.PreviousOffset;
            _currentLine = _tokenizer.Line;
            _currentColumn = _tokenizer.Column;
        }
        return _current;
    }

    /// <summary>Tokenizes the remaining tokens into a list (used for var()/unparsed values).</summary>
    public List<CssParserToken> TokenizeToEof()
    {
        var tokens = new List<CssParserToken>();
        if (_hasLookahead)
        {
            tokens.Add(_lookahead);
            _hasLookahead = false;
        }
        tokens.Add(_current);
        while (true)
        {
            var t = _tokenizer.TokenizeSingle();
            if (t.IsEof) break;
            tokens.Add(t);
        }
        return tokens;
    }

    /// <summary>Consumes a block (between a start and end token), returning the inner raw text.</summary>
    public string ConsumeRawBlock()
    {
        int start = _tokenizer.Offset;
        int depth = 0;
        while (true)
        {
            var t = _tokenizer.TokenizeSingle();
            if (t.IsEof) break;
            switch (t.Type)
            {
                case CssTokenType.LeftBraceToken:
                case CssTokenType.LeftParenthesisToken:
                case CssTokenType.LeftSquareBracketToken:
                    depth++;
                    break;
                case CssTokenType.RightBraceToken:
                case CssTokenType.RightParenthesisToken:
                case CssTokenType.RightSquareBracketToken:
                    depth--;
                    if (depth <= 0)
                    {
                        _current = _tokenizer.TokenizeSingle();
                        _currentOffset = _tokenizer.Offset;
                        _currentStart = _tokenizer.PreviousOffset;
                        return _tokenizer.StringRangeFrom(start);
                    }
                    break;
            }
        }
        _current = CssParserToken.Eof;
        return _tokenizer.StringRangeFrom(start);
    }
}