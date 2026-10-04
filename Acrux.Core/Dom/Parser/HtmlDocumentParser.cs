namespace Acrux.Core.Dom.Parser;

internal class HtmlDocumentParser
{
    private readonly HtmlInputStream _input = new();
    private readonly HtmlParserOptions _options;
    private readonly HtmlTokenizer _tokenizer;
    private readonly HtmlTreeBuilder _treeBuilder;
    private readonly Document _document;

    public HtmlDocumentParser(Document document)
    {
        _document = document;
        _options = new HtmlParserOptions();
        _tokenizer = new HtmlTokenizer(_options);
        _treeBuilder = new HtmlTreeBuilder(this, document, _options);
    }

    public HtmlTokenizer Tokenizer => _tokenizer;

    public void Append(string source)
    {
        if (string.IsNullOrEmpty(source))
            return;

        var segmented = new SegmentedString(source);
        _input.AppendToEnd(segmented);
        PumpTokenizer();
    }

    public void Finish()
    {
        if (!_input.HaveSeenEndOfFile)
            _input.MarkEndOfFile();

        PumpTokenizer();
        _treeBuilder.Finished();
    }

    private void PumpTokenizer()
    {
        // Fix #11: simple loop with a sane token limit.
        const int maxTokens = 1000000;
        int tokens = 0;
        while (true)
        {
            if (tokens >= maxTokens)
                break;

            var token = _tokenizer.NextToken(_input.Current);
            if (token == null || token.Type == HtmlToken.TokenType.Uninitialized)
                break;

            var atomicToken = new AtomicHtmlToken(token);
            _tokenizer.ClearToken();
            _treeBuilder.ConstructTree(atomicToken);
            tokens++;
        }
    }
}