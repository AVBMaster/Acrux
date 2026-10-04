using static Acrux.Core.Dom.Parser.HtmlParserConstants;

namespace Acrux.Core.Dom.Parser;

internal class InputStreamPreprocessor
{
    private readonly HtmlTokenizer _tokenizer;
    private bool _skipNextNewLine;

    public InputStreamPreprocessor(HtmlTokenizer tokenizer)
    {
        _tokenizer = tokenizer;
    }

    public bool Peek(SegmentedString source, out char cc)
    {
        cc = source.CurrentChar();
        return ProcessNextInputCharacter(source, ref cc);
    }

    public bool Advance(SegmentedString source, out char cc)
    {
        cc = source.AdvanceAndUpdateLineNumber();
        return ProcessNextInputCharacter(source, ref cc);
    }

    public bool AdvancePastNonNewline(SegmentedString source, out char cc)
    {
        cc = source.AdvancePastNonNewline();
        return ProcessNextInputCharacter(source, ref cc);
    }

    public bool AdvancePastCarriageReturn(SegmentedString source, ref char cc)
    {
        cc = source.AdvancePastNonNewline();
        if (source.IsEmpty)
        {
            _skipNextNewLine = true;
            return false;
        }
        if (cc == '\n')
        {
            cc = source.AdvancePastNewlineAndUpdateLineNumber();
            if (source.IsEmpty)
                return false;
        }
        return true;
    }

    public bool ProcessNullCharacter(SegmentedString source, ref char cc)
    {
        if (source.IsEmpty)
            return false;
        if (ShouldTreatNullAsEndOfFileMarker(source))
            return true;
        if (!_tokenizer.ShouldSkipNullCharacters)
        {
            cc = ReplacementCharacter;
            return true;
        }
        cc = source.AdvancePastNonNewline();
        while (cc == '\0')
        {
            if (source.IsEmpty)
                return false;
            if (ShouldTreatNullAsEndOfFileMarker(source))
                return true;
            cc = source.AdvancePastNonNewline();
        }
        return true;
    }

    private bool ProcessNextInputCharacter(SegmentedString source, ref char cc)
    {
        if (cc != '\n' && cc != '\r' && cc != '\0')
        {
            _skipNextNewLine = false;
            return true;
        }
        if (source.IsEmpty)
            return false;
        return ProcessNextInputSpecialCharacter(source, ref cc);
    }

    private bool ProcessNextInputSpecialCharacter(SegmentedString source, ref char cc)
    {
    ProcessAgain:
        if (cc == '\n' && _skipNextNewLine)
        {
            _skipNextNewLine = false;
            cc = source.AdvancePastNewlineAndUpdateLineNumber();
            if (source.IsEmpty)
                return false;
        }
        if (cc == '\r')
        {
            cc = '\n';
            _skipNextNewLine = true;
        }
        else
        {
            _skipNextNewLine = false;
            if (cc == '\0' && !ShouldTreatNullAsEndOfFileMarker(source))
            {
                if (_tokenizer.ShouldSkipNullCharacters)
                {
                    cc = source.AdvancePastNonNewline();
                    if (source.IsEmpty)
                        return false;
                    goto ProcessAgain;
                }
                cc = ReplacementCharacter;
            }
        }
        return true;
    }

    private bool ShouldTreatNullAsEndOfFileMarker(SegmentedString source)
    {
        // blink: `return source.IsClosed() && source.length() == 1;` - the
        // "\0" end-of-file marker segment is closed by HtmlInputStream.
        // MarkEndOfFile() and is the only remaining length in the chain.
        return source.IsClosed && source.TotalLength == 1;
    }
}