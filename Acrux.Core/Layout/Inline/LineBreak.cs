using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Inline;

/// <summary>
/// Represents a line break point. Mirrors line_break_point.h.
/// </summary>
public struct LineBreakPoint
{
    public InlineItemTextIndex Offset;
    public InlineItemTextIndex End;
    public bool IsHyphenated;

    public LineBreakPoint(InlineItemTextIndex offset, InlineItemTextIndex end, bool isHyphenated)
    {
        Offset = offset;
        End = end;
        IsHyphenated = isHyphenated;
    }

    public static bool operator ==(LineBreakPoint a, LineBreakPoint b) =>
        a.Offset.ItemIndex == b.Offset.ItemIndex && a.Offset.TextOffset == b.Offset.TextOffset &&
        a.End.ItemIndex == b.End.ItemIndex && a.End.TextOffset == b.End.TextOffset &&
        a.IsHyphenated == b.IsHyphenated;
    public static bool operator !=(LineBreakPoint a, LineBreakPoint b) => !(a == b);
    public override bool Equals(object? obj) => obj is LineBreakPoint other && this == other;
    public override int GetHashCode() => HashCode.Combine(Offset.ItemIndex, Offset.TextOffset, End.ItemIndex, End.TextOffset, IsHyphenated);
}

/// <summary>
/// Represents a break candidate (break opportunity). Mirrors line_break_candidate.h.
/// </summary>
public struct LineBreakCandidate
{
    public InlineItemTextIndex Offset;
    public InlineItemTextIndex End;
    public bool IsHyphenated;
    public float PosNoBreak { get; set; }
    public float PosIfBreak { get; set; }
    public float Penalty { get; set; }

    public LineBreakCandidate(InlineItemTextIndex offset, InlineItemTextIndex end, float posNoBreak, float posIfBreak, float penalty = 0, bool isHyphenated = false)
    {
        Offset = offset;
        End = end;
        IsHyphenated = isHyphenated;
        PosNoBreak = posNoBreak;
        PosIfBreak = posIfBreak;
        Penalty = penalty;
    }

    public LineBreakCandidate(InlineItemTextIndex offset, float position)
        : this(offset, offset, position, position) { }

    public static bool operator ==(LineBreakCandidate a, LineBreakCandidate b) =>
        a.Offset == b.Offset && a.End == b.End && a.IsHyphenated == b.IsHyphenated &&
        a.PosNoBreak == b.PosNoBreak && a.PosIfBreak == b.PosIfBreak && a.Penalty == b.Penalty;
    public static bool operator !=(LineBreakCandidate a, LineBreakCandidate b) => !(a == b);
    public override bool Equals(object? obj) => obj is LineBreakCandidate other && this == other;
    public override int GetHashCode() => HashCode.Combine(Offset, End, IsHyphenated, PosNoBreak, PosIfBreak, Penalty);
}

/// <summary>
/// Provides a context for computing LineBreakCandidate from multiple LineInfo
/// and InlineItemResult. Mirrors LineBreakCandidateContext in line_break_candidate.h.
/// </summary>
public class LineBreakCandidateContext
{
    public enum State
    {
        Break,
        MidWord
    }

    private readonly List<LineBreakCandidate> _candidates = new();
    private float _positionNoSnap;
    private State _state = State.Break;
    private InlineItem? _lastItem;
    private int _lastEndOffset;
    private float _hyphenPenalty;

    public float HyphenPenalty => _hyphenPenalty;
    public State CurrentState => _state;
    public float Position => _positionNoSnap;
    public float SnappedPosition => (float)Math.Ceiling(_positionNoSnap);
    public IReadOnlyList<LineBreakCandidate> Candidates => _candidates;
    public InlineItem? LastItem => _lastItem;
    public int LastEndOffset => _lastEndOffset;

    public void SetHyphenPenalty(float penalty) => _hyphenPenalty = penalty;
    public void SetLast(InlineItem item, int offset)
    {
        _lastItem = item;
        _lastEndOffset = offset;
    }

    public void Append(State newState, InlineItemTextIndex offset, InlineItemTextIndex end, float posNoBreak, float posIfBreak, float penalty = 0, bool isHyphenated = false)
    {
        if (newState == State.MidWord && _candidates.Count > 0 && _state == State.MidWord)
        {
            var last = _candidates[^1];
            last.PosNoBreak = posNoBreak;
            last.PosIfBreak = posIfBreak;
            last.Penalty = penalty;
            _candidates[^1] = last;
        }
        else
        {
            _candidates.Add(new LineBreakCandidate(offset, end, posNoBreak, posIfBreak, penalty, isHyphenated));
        }
        _state = newState;
        _positionNoSnap = posNoBreak;
    }

    public void Append(State newState, InlineItemTextIndex offset, float position) =>
        Append(newState, offset, offset, position, position);

    public void AppendTrailingSpaces(State newState, InlineItemTextIndex offset, float posNoBreak)
    {
        _positionNoSnap = posNoBreak;
        _state = newState;
    }

    public bool AppendLine(LineInfo lineInfo)
    {
        if (lineInfo.StartItemIndex == 0 && lineInfo.EndItemIndex() == 0)
            return false;
        _positionNoSnap = lineInfo.InlineSize;
        return true;
    }

    public void EnsureFirstSentinel(LineInfo firstLineInfo)
    {
        if (_candidates.Count == 0 || _candidates[0].Offset.ItemIndex != 0 || _candidates[0].Offset.TextOffset != 0)
        {
            _candidates.Insert(0, new LineBreakCandidate(new InlineItemTextIndex(), 0));
        }
    }

    public void EnsureLastSentinel(LineInfo lastLineInfo)
    {
        var lastOffset = new InlineItemTextIndex
        {
            ItemIndex = lastLineInfo.EndItemIndex(),
            TextOffset = lastLineInfo.EndItemIndex()
        };
        _candidates.Add(new LineBreakCandidate(lastOffset, _positionNoSnap));
    }
}

/// <summary>
/// Represents a break token for an inline node. Mirrors inline_break_token.h.
/// </summary>
public class InlineBreakToken : BreakToken
{
    public enum InlineBreakTokenFlags
    {
        Default = 0,
        IsForcedBreak = 1 << 0,
        HasRareData = 1 << 1,
        UseFirstLineStyle = 1 << 2,
        HasClonedBoxDecorations = 1 << 3,
        IsInParallelBlockFlow = 1 << 4,
        IsPastFirstFormattedLine = 1 << 5,
    }

    public ComputedStyle? Style { get; set; }
    public InlineItemTextIndex Start { get; set; }
    public int Flags { get; set; }

    public BlockBreakToken? SubBreakToken { get; set; }

    public int StartItemIndex => Start.ItemIndex;
    public int StartTextOffset => Start.TextOffset;

    public bool UseFirstLineStyle => (Flags & (int)InlineBreakTokenFlags.UseFirstLineStyle) != 0;
    public bool IsForcedBreak => (Flags & (int)InlineBreakTokenFlags.IsForcedBreak) != 0;
    public bool HasClonedBoxDecorations => (Flags & (int)InlineBreakTokenFlags.HasClonedBoxDecorations) != 0;
    public bool IsInParallelBlockFlow => (Flags & (int)InlineBreakTokenFlags.IsInParallelBlockFlow) != 0;
    public bool IsPastFirstFormattedLine => (Flags & (int)InlineBreakTokenFlags.IsPastFirstFormattedLine) != 0;

    public static InlineBreakToken Create(InlineNode node, ComputedStyle? style, InlineItemTextIndex start, int flags, BlockBreakToken? subBreakToken = null)
    {
        return new InlineBreakToken
        {
            Style = style,
            Start = start,
            Flags = flags,
            SubBreakToken = subBreakToken,
        };
    }

    public static InlineBreakToken Create(InlineNode node, InlineItemTextIndex start, InlineItemTextIndex end,
        BlockBreakToken? subBreakToken = null, bool isLastLine = false)
    {
        int flags = 0;
        if (isLastLine) flags |= (int)InlineBreakTokenFlags.IsPastFirstFormattedLine;
        return Create(node, null, start, flags, subBreakToken);
    }

    /// <summary>Mirrors InlineBreakToken::CreateForParallelBlockFlow().</summary>
    public static InlineBreakToken CreateForParallelBlockFlow(InlineNode node, InlineItemTextIndex start, BlockBreakToken subBreakToken)
    {
        return new InlineBreakToken
        {
            Style = subBreakToken.StyleForInline,
            Start = start,
            Flags = (int)InlineBreakTokenFlags.IsInParallelBlockFlow,
            SubBreakToken = subBreakToken,
        };
    }

    public static InlineBreakToken CreateForParallelBlockFlow(InlineNode node, int itemIndex, BlockBreakToken subBreakToken)
    {
        return new InlineBreakToken
        {
            Start = new InlineItemTextIndex { ItemIndex = itemIndex, TextOffset = 0 },
            Flags = (int)InlineBreakTokenFlags.IsInParallelBlockFlow,
            SubBreakToken = subBreakToken,
        };
    }

    public BlockBreakToken? GetBlockBreakToken() => SubBreakToken as BlockBreakToken;

    public override string ToString() => $"InlineBreakToken({Start.ItemIndex}/{Start.TextOffset})";

    public static bool IsStartEqual(InlineBreakToken? lhs, InlineBreakToken? rhs)
    {
        if (lhs == null) return rhs == null;
        return rhs != null && lhs.Start.ItemIndex == rhs.Start.ItemIndex && lhs.Start.TextOffset == rhs.Start.TextOffset;
    }
}

/// <summary>
/// Represents already handled leading floats within an inline formatting
/// context. Mirrors leading_floats.h.
/// </summary>
public class LeadingFloats
{
    public int HandledIndex { get; set; }
    public List<PositionedFloat> Floats { get; } = new();
}