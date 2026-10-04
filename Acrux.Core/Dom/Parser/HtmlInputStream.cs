using System.Text;

namespace Acrux.Core.Dom.Parser;

internal static class HtmlParserConstants
{
    internal const char EndOfFileMarker = '\0';
    internal const char ReplacementCharacter = '\uFFFD';
}

internal class SegmentedString
{
    private string _string;
    private int _offset;
    private int _lineNumber;
    private int _columnNumber;
    private SegmentedString? _next;
    private bool _closed;

    public SegmentedString()
    {
        _string = string.Empty;
        _offset = 0;
        _lineNumber = 1;
        _columnNumber = 0;
    }

    public SegmentedString(string source) : this()
    {
        _string = source;
    }

    // Mirrors SegmentedString::empty_ in blink: the stream only appears empty
    // when the current segment is exhausted AND no further segment is linked.
    // This lets the tokenizer cross segment boundaries instead of bailing out,
    // which is what allows the "\0" end-of-file marker to ever be reached.
    public bool IsEmpty
    {
        get
        {
            if (_offset < _string.Length)
                return false;
            return _next == null;
        }
    }

    public bool IsClosed => _closed;

    // Remaining length of just this segment (node-local).
    public int Length => _string.Length - _offset;

    // Remaining length across the whole linked chain. Equivalent to the total
    // of SegmentedString::length() over all substrings (used to recognize the
    // trailing "\0" end-of-file marker).
    public int TotalLength
    {
        get
        {
            int len = _string.Length - _offset;
            for (var next = _next; next != null; next = next._next)
                len += next._string.Length - next._offset;
            return len;
        }
    }

    public char CurrentChar()
    {
        if (_offset < _string.Length)
            return _string[_offset];
        if (_next != null)
            return _next.CurrentChar();
        return HtmlParserConstants.EndOfFileMarker;
    }

    public int NumberOfCharactersConsumed => _offset;

    public SegmentedString? NextSegmentedString => _next;

    public void SetNextSegmentedString(SegmentedString? next)
    {
        _next = next;
    }

    public void Append(SegmentedString other)
    {
        if (_next != null)
        {
            _next.Append(other);
            return;
        }
        _next = other;
    }

    public void Append(string other)
    {
        Append(new SegmentedString(other));
    }

    public char AdvancePastNonNewline()
    {
        _offset++;
        _columnNumber++;
        if (_offset < _string.Length)
            return _string[_offset];
        if (_next != null)
        {
            var oldNext = _next;
            _string = oldNext._string;
            _offset = oldNext._offset;
            _lineNumber = oldNext._lineNumber;
            _columnNumber = oldNext._columnNumber;
            _next = oldNext._next;
            _closed = oldNext._closed;
            if (_offset < _string.Length)
                return _string[_offset];
        }
        return HtmlParserConstants.EndOfFileMarker;
    }

    public char AdvanceAndUpdateLineNumber()
    {
        char c = CurrentChar();
        if (c == '\n')
            _lineNumber++;
        _offset++;
        _columnNumber = (c == '\n') ? 0 : _columnNumber + 1;
        if (_offset >= _string.Length && _next != null)
        {
            var oldNext = _next;
            _string = oldNext._string;
            _offset = oldNext._offset;
            _lineNumber = oldNext._lineNumber;
            _columnNumber = oldNext._columnNumber;
            _next = oldNext._next;
            _closed = oldNext._closed;
        }
        return CurrentChar();
    }

    public char AdvancePastNewlineAndUpdateLineNumber()
    {
        _lineNumber++;
        _offset++;
        _columnNumber = 0;
        if (_offset >= _string.Length && _next != null)
        {
            var oldNext = _next;
            _string = oldNext._string;
            _offset = oldNext._offset;
            _lineNumber = oldNext._lineNumber;
            _columnNumber = oldNext._columnNumber;
            _next = oldNext._next;
            _closed = oldNext._closed;
        }
        return CurrentChar();
    }

    public void AdvanceAndAssert(char expected)
    {
        char c = CurrentChar();
        _offset++;
        _columnNumber++;
        if (_offset >= _string.Length && _next != null)
        {
            var oldNext = _next;
            _string = oldNext._string;
            _offset = oldNext._offset;
            _lineNumber = oldNext._lineNumber;
            _columnNumber = oldNext._columnNumber;
            _next = oldNext._next;
            _closed = oldNext._closed;
        }
    }

    public void Push(char c)
    {
        _string = c + _string.Substring(_offset);
        _offset = 0;
    }

    public void Prepend(SegmentedString other, PrependType type)
    {
        // Chain `other -> this`, then adopt other's data; must capture other's
        // original link first, otherwise this ends up pointing at itself.
        var originalNext = other._next;
        other._next = this;
        _string = other._string;
        _offset = other._offset;
        _lineNumber = other._lineNumber;
        _columnNumber = other._columnNumber;
        _next = originalNext;
        _closed = other._closed;
    }

    public enum PrependType { Unconsume }

    public string GetRemainingString()
    {
        if (_offset >= _string.Length)
        {
            if (_next != null)
                return _next.GetRemainingString();
            return "";
        }
        var remaining = _string.Substring(_offset);
        if (_next != null)
            remaining += _next.GetRemainingString();
        return remaining;
    }

    public void Close()
    {
        _closed = true;
    }

    public int CurrentLine => _lineNumber;
    public int CurrentColumn => _columnNumber;

    public void SetCurrentPosition(int line, int column, int offset)
    {
        _lineNumber = line;
        _columnNumber = column;
        _offset = offset;
    }

    public LookAheadResult LookAhead(string pattern)
    {
        int savedOffset = _offset;
        int savedLine = _lineNumber;
        int savedColumn = _columnNumber;
        var savedNext = _next;
        string savedString = _string;
        bool savedClosed = _closed;

        foreach (char c in pattern)
        {
            if (CurrentChar() != c)
            {
                _offset = savedOffset;
                _lineNumber = savedLine;
                _columnNumber = savedColumn;
                _next = savedNext;
                _string = savedString;
                _closed = savedClosed;
                return LookAheadResult.DidNotMatch;
            }
            AdvanceAndUpdateLineNumber();
            if (IsEmpty && pattern.Length > 1)
            {
                _offset = savedOffset;
                _lineNumber = savedLine;
                _columnNumber = savedColumn;
                _next = savedNext;
                _string = savedString;
                _closed = savedClosed;
                return LookAheadResult.NotEnoughCharacters;
            }
        }

        _offset = savedOffset;
        _lineNumber = savedLine;
        _columnNumber = savedColumn;
        _next = savedNext;
        _string = savedString;
        _closed = savedClosed;
        return LookAheadResult.DidMatch;
    }

    public LookAheadResult LookAheadIgnoringCase(string pattern)
    {
        int savedOffset = _offset;
        int savedLine = _lineNumber;
        int savedColumn = _columnNumber;
        var savedNext = _next;
        string savedString = _string;
        bool savedClosed = _closed;

        foreach (char c in pattern)
        {
            char cur = CurrentChar();
            if (char.ToLowerInvariant(cur) != char.ToLowerInvariant(c))
            {
                _offset = savedOffset;
                _lineNumber = savedLine;
                _columnNumber = savedColumn;
                _next = savedNext;
                _string = savedString;
                _closed = savedClosed;
                return LookAheadResult.DidNotMatch;
            }
            AdvanceAndUpdateLineNumber();
            if (IsEmpty && pattern.Length > 1)
            {
                _offset = savedOffset;
                _lineNumber = savedLine;
                _columnNumber = savedColumn;
                _next = savedNext;
                _string = savedString;
                _closed = savedClosed;
                return LookAheadResult.NotEnoughCharacters;
            }
        }

        _offset = savedOffset;
        _lineNumber = savedLine;
        _columnNumber = savedColumn;
        _next = savedNext;
        _string = savedString;
        _closed = savedClosed;
        return LookAheadResult.DidMatch;
    }

    public enum LookAheadResult
    {
        DidMatch,
        DidNotMatch,
        NotEnoughCharacters,
    }
}

internal class HtmlInputStream
{
    private SegmentedString? _first;
    private SegmentedString? _last;

    public HtmlInputStream()
    {
        _first = null;
        _last = null;
    }

    public void AppendToEnd(SegmentedString str)
    {
        if (_first == null)
        {
            _first = str;
            _last = str;
        }
        else
        {
            _last!.Append(str);
        }
    }

    public void InsertAtCurrentInsertionPoint(SegmentedString str)
    {
        if (_first == null)
        {
            _first = str;
            _last = str;
        }
        else
        {
            _first.Append(str);
        }
    }

    public bool HasInsertionPoint => _first != null && _first != _last;

    public void MarkEndOfFile()
    {
        // Blink: last_->Append(SegmentedString(kEndOfFileMarker)); last_->Close();
        // In the node-chain model the marker is its own segment, so it must be
        // closed as well: ShouldTreatNullAsEndOfFileMarker() requires
        // `IsClosed && length() == 1`, and the tokenizer reaches that segment
        // through the advance/switch code paths that copy its closed state.
        // Without it the "\0" is treated as a literal null and replaced with
        // U+FFFD, rendering a box character at the end of the page.
        var eof = new SegmentedString("\0");
        eof.Close();
        if (_last != null)
        {
            _last.Append(eof);
            _last.Close();
        }
        else
        {
            _first = eof;
            _last = eof;
        }
    }

    public bool HaveSeenEndOfFile => _last?.IsClosed ?? false;

    public SegmentedString Current => _first ?? new SegmentedString();

    public void SplitInto(ref SegmentedString next)
    {
        next = _first!;
        _first = new SegmentedString();
        _first.SetNextSegmentedString(next);
        // Mirrors `last_ == &first_`: when first_ was the only segment, it is
        // no longer the last once the split happens - |next| is now the last.
        // In the node-chain model _last already references |next| in that case,
        // so this restores the invariant explicitly.
        if (_last == next)
            _last = next;
    }

    public void MergeFrom(SegmentedString next)
    {
        // Blink: first_.Append(next) copies next's data into first_, then
        // first_.SetNextSegmentedString(next.NextSegmentedString()) restores
        // the stream link. In the node-chain model appending data is tail-
        // linking the |next| segment (which carries its own chain) to first_'s
        // chain, so the original Append + SetNext pair is replaced by that.
        var tail = _first;
        while (tail != null && tail.NextSegmentedString != null)
            tail = tail.NextSegmentedString;
        if (tail != null && tail != next)
            tail.SetNextSegmentedString(next);
        if (_last == next)
            _last = _first;
        if (next.IsClosed)
            _first!.Close();
    }

    public int Length
    {
        get
        {
            int total = 0;
            for (var current = _first; current != null; current = current.NextSegmentedString)
                total += current.Length;
            return total;
        }
    }
}

internal class InsertionPointRecord : IDisposable
{
    private readonly HtmlInputStream _inputStream;
    private readonly SegmentedString _next;
    private readonly int _line;
    private readonly int _column;

    public InsertionPointRecord(HtmlInputStream inputStream)
    {
        _inputStream = inputStream;
        _line = inputStream.Current.CurrentLine;
        _column = inputStream.Current.CurrentColumn;
        _next = inputStream.Current;
        inputStream.SplitInto(ref _next);
        inputStream.Current.SetCurrentPosition(_line, _column, 0);
    }

    public void Dispose()
    {
        int unparsedRemainderLength = _inputStream.Current.TotalLength;
        _inputStream.MergeFrom(_next);
        _inputStream.Current.SetCurrentPosition(_line, _column, unparsedRemainderLength);
    }
}