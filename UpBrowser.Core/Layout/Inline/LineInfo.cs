using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Inline;

/// <summary>
/// Represents a line to build. Mirrors line_info.h.
/// LineBreaker produces, and InlineLayoutAlgorithm consumes.
/// </summary>
public class LineInfo
{
    private InlineItemsData? _itemsData;
    private ComputedStyle? _lineStyle;
    private readonly List<InlineItemResult> _results = new();

    private BfcOffset _bfcOffset;
    private InlineBreakToken? _breakToken;
    private readonly List<InlineBreakToken> _parallelFlowBreakTokens = new();

    private LayoutResult? _blockInInlineLayoutResult;

    private float? _minimumSpaceShortage;

    private float _availableWidth;
    private float _width;
    private float _hangWidth;
    private float _textIndent;

    private float _annotationBlockStartAdjustment;
    private float _initialLetterBoxBlockStartAdjustment;
    private float _initialLetterBoxBlockSize;

    private InlineItemTextIndex _start = new() { ItemIndex = 0, TextOffset = 0 };
    private int _endItemIndex;
    private int _endOffsetForJustify;

    private TextAlignType _textAlign = TextAlignType.Start;
    private TextDirection _baseDirection = TextDirection.Ltr;

    private bool _isFirstFormattedLine;
    private bool _useFirstLineStyle;
    private bool _isLastLine;
    private bool _hasForcedBreak;
    private bool _isEmptyLine;
    private bool _hasLineEvenIfEmpty;
    private bool _isBlockInInline;
    private bool _hasOverflow;
    private bool _hasTrailingSpaces;
    private bool _needsAccurateEndPosition;
    private bool _isRubyBase;
    private bool _isRubyText;
    private bool _mayHaveTextCombineOrRubyItem;
    private bool _mayHaveRubyOverhang;
    private bool _allowHangForAlignment;

    // ------------------------------------------------------------------
    // Compatibility output cache used by UpBrowser's renderer model. These
    // are NOT part of the upstream LineInfo; the algorithm computes them
    // after CreateLine() so the BoxLine/BoxRun leaf model can be produced.
    // ------------------------------------------------------------------
    public float BlockSize { get; set; }
    public float BaselineOffset { get; set; }

    /// <summary>Line width (compat alias of Width(), clamped to zero).</summary>
    public float InlineSize
    {
        get => Width();
        set => _width = value;
    }

    /// <summary>Available inline size of this line (compat alias of AvailableWidth()).</summary>
    public float AvailableInlineSize
    {
        get => _availableWidth;
        set => _availableWidth = value;
    }

    /// <summary>
    /// Inline offset of this line box inside the container's content box. Normally
    /// zero; a left float pushes the line box right (CSS 2.1 §9.5.2).
    /// </summary>
    public float LeftInset { get; set; }

    /// <summary>
    /// Margin/border/padding that an inline box with
    /// <c>box-decoration-break: clone</c> reserves at the line start, on a line
    /// that continues such a box (CSS Fragmentation 3 §4.2). It is part of the
    /// line's inline size but belongs to no item of this line.
    /// </summary>
    public float BoxDecorationStartInset { get; set; }

    /// <summary>
    /// The clone counterpart of <see cref="BoxDecorationStartInset"/> at the line
    /// end: the decoration of the boxes still open when the line broke.
    /// </summary>
    public float BoxDecorationEndInset { get; set; }

    public void Reset()
    {
        _itemsData = null;
        _lineStyle = null;
        _results.Clear();

        _bfcOffset = BfcOffset.Zero;

        _breakToken = null;
        _parallelFlowBreakTokens.Clear();

        _blockInInlineLayoutResult = null;

        _availableWidth = 0;
        _width = 0;
        _hangWidth = 0;
        _textIndent = 0;
        BoxDecorationStartInset = 0;
        BoxDecorationEndInset = 0;

        _annotationBlockStartAdjustment = 0;
        _initialLetterBoxBlockStartAdjustment = 0;
        _initialLetterBoxBlockSize = 0;

        _start = new InlineItemTextIndex();
        _endItemIndex = 0;
        _endOffsetForJustify = 0;

        _textAlign = TextAlignType.Start;
        _baseDirection = TextDirection.Ltr;

        _useFirstLineStyle = false;
        _isLastLine = false;
        _hasForcedBreak = false;
        _isEmptyLine = false;
        _hasLineEvenIfEmpty = false;
        _isBlockInInline = false;
        _hasOverflow = false;
        _hasTrailingSpaces = false;
        _needsAccurateEndPosition = false;
        _isRubyBase = false;
        _isRubyText = false;
        _mayHaveTextCombineOrRubyItem = false;
        _mayHaveRubyOverhang = false;
        _allowHangForAlignment = false;
    }

    public InlineItemsData ItemsData()
    {
        if (_itemsData == null) throw new InvalidOperationException("ItemsData not set");
        return _itemsData;
    }

    /// <summary>The style to use for the line.</summary>
    public ComputedStyle LineStyle()
    {
        if (_lineStyle == null) throw new InvalidOperationException("line style not set");
        return _lineStyle;
    }

    public void SetLineStyle(InlineNode node, InlineItemsData itemsData, bool useFirstLineStyle)
    {
        _useFirstLineStyle = useFirstLineStyle;
        _itemsData = itemsData;
        _lineStyle = node.Style;
        _needsAccurateEndPosition = ComputeNeedsAccurateEndPosition();

        // Reset block start offset related members.
        _annotationBlockStartAdjustment = 0;
        _initialLetterBoxBlockStartAdjustment = 0;
        _initialLetterBoxBlockSize = 0;
    }

    public void OverrideLineStyle(ComputedStyle style) => _lineStyle = style;

    /// <summary>Guard for empty / not-yet-set line styles (compat with legacy paths).</summary>
    public bool HasLineStyle => _lineStyle != null;

    public bool IsFirstFormattedLine() => _isFirstFormattedLine;
    public void SetIsFirstFormattedLine(bool value) => _isFirstFormattedLine = value;

    public bool UseFirstLineStyle() => _useFirstLineStyle;
    public void SetUseFirstLineStyle(bool value) => _useFirstLineStyle = value;

    public bool IsLastLine() => _isLastLine;
    public void SetIsLastLine(bool isLastLine) => _isLastLine = isLastLine;

    public bool HasForcedBreak() => _hasForcedBreak;
    public void SetHasForcedBreak() => _hasForcedBreak = true;

    public bool IsEmptyLine() => _isEmptyLine;
    public void SetIsEmptyLine() => _isEmptyLine = true;

    public bool HasLineEvenIfEmpty() => _hasLineEvenIfEmpty;
    public void SetHasLineEvenIfEmpty() => _hasLineEvenIfEmpty = true;

    public bool IsBlockInInline() => _isBlockInInline;
    public void SetIsBlockInInline() => _isBlockInInline = true;

    public bool IsRubyBase() => _isRubyBase;
    public void SetIsRubyBase() => _isRubyBase = true;

    public bool IsRubyText() => _isRubyText;
    public void SetIsRubyText() => _isRubyText = true;

    public List<InlineItemResult> MutableResults() => _results;
    public IReadOnlyList<InlineItemResult> Results() => _results;

    public InlineBreakToken? GetBreakToken() => _breakToken;
    public void SetBreakToken(InlineBreakToken? breakToken) => _breakToken = breakToken;

    /// <summary>True if this line ends a paragraph; i.e. ends a block or has a forced break.</summary>
    public bool IsEndParagraph() => GetBreakToken() == null || HasForcedBreak();

    public List<InlineBreakToken> ParallelFlowBreakTokens() => _parallelFlowBreakTokens;

    public void PropagateParallelFlowBreakToken(InlineBreakToken token)
    {
        _parallelFlowBreakTokens.Add(token);
    }

    public void RemoveParallelFlowBreakToken(int itemIndex)
    {
        for (int i = 0; i < _parallelFlowBreakTokens.Count; i++)
        {
            if (_parallelFlowBreakTokens[i].StartItemIndex >= itemIndex)
            {
                if (i == 0)
                    _parallelFlowBreakTokens.Clear();
                else
                    _parallelFlowBreakTokens.RemoveRange(0, i);
                break;
            }
        }
    }

    public float? MinimumSpaceShortage() => _minimumSpaceShortage;

    public void PropagateMinimumSpaceShortage(float shortage)
    {
        if (shortage <= 0) return;
        if (_minimumSpaceShortage != null)
            _minimumSpaceShortage = Math.Min(_minimumSpaceShortage.Value, shortage);
        else
            _minimumSpaceShortage = shortage;
    }

    public void SetTextIndent(float indent) => _textIndent = indent;
    public float TextIndent() => _textIndent;

    public TextAlignType TextAlign()
    {
        // `text-align-last` overrides the last line's alignment (CSS Text 3
        // §5.11.1). It is applied here rather than in UpdateTextAlign() because
        // the line is not known to be the last one until after that update.
        if (_isLastLine && _lineStyle != null && _lineStyle.TextAlignLast != TextAlignLastType.Auto)
        {
            return _lineStyle.TextAlignLast switch
            {
                TextAlignLastType.Start => TextAlignType.Start,
                TextAlignLastType.End => TextAlignType.End,
                TextAlignLastType.Left => TextAlignType.Left,
                TextAlignLastType.Right => TextAlignType.Right,
                TextAlignLastType.Center => TextAlignType.Center,
                TextAlignLastType.Justify => TextAlignType.Justify,
                _ => _textAlign,
            };
        }
        return _textAlign;
    }

    /// <summary>Update |TextAlign()| and related fields.</summary>
    public void UpdateTextAlign()
    {
        _textAlign = GetTextAlign(IsLastLine());

        if (HasTrailingSpaces() && WhiteSpaceStyle.ShouldWrapLine(LineStyle()))
        {
            if (ShouldHangTrailingSpaces())
            {
                _hangWidth = ComputeTrailingSpaceWidth(out _endOffsetForJustify);
                _allowHangForAlignment = true;
                return;
            }
            _hangWidth = ComputeTrailingSpaceWidth();
        }
        else
        {
            _hangWidth = 0;
            _allowHangForAlignment = false;
        }

        if (_textAlign == TextAlignType.Justify)
            _endOffsetForJustify = InflowEndOffset();
    }

    public BfcOffset GetBfcOffset() => _bfcOffset;
    public void SetBfcOffset(in BfcOffset bfcOffset) => _bfcOffset = bfcOffset;

    public float AvailableWidth() => _availableWidth;

    /// <summary>Width of this line, including hanging width from trailing spaces.</summary>
    public float Width() => Math.Max(0, _width);

    /// <summary>Same as |Width()| but negative values as-is.</summary>
    public float WidthForAlignment() => _width - HangWidthForAlignment();

    public float HangWidth() => _hangWidth;
    public float HangWidthForAlignment() => _allowHangForAlignment ? _hangWidth : 0;

    public float ComputeWidth() => _textIndent + SumResults();

    private float SumResults()
    {
        float inlineSize = 0;
        foreach (var itemResult in _results)
            inlineSize += itemResult.InlineSize;
        return inlineSize;
    }

    public bool HasTrailingSpaces() => _hasTrailingSpaces;
    public void SetHasTrailingSpaces() => _hasTrailingSpaces = true;

    public bool ShouldHangTrailingSpaces()
    {
        if (!HasTrailingSpaces()) return false;
        if (!WhiteSpaceStyle.ShouldWrapLine(LineStyle())) return false;
        switch (_textAlign)
        {
            case TextAlignType.Start:
            case TextAlignType.Justify:
                return true;
            case TextAlignType.End:
            case TextAlignType.Center:
                return false;
            case TextAlignType.Left:
                return IsLtr(BaseDirection());
            case TextAlignType.Right:
                return IsRtl(BaseDirection());
        }
        return false;
    }

    public bool HasOverflow() => _hasOverflow;
    public void SetHasOverflow(bool value = true) => _hasOverflow = value;

    public bool IsHyphenated()
    {
        for (int i = _results.Count - 1; i >= 0; i--)
        {
            if (_results[i].Length > 0)
                return _results[i].IsHyphenated;
        }
        return false;
    }

    public void SetWidth(float availableWidth, float width)
    {
        _availableWidth = availableWidth;
        _width = width;
    }

    public InlineItemTextIndex Start() => _start;
    public int StartOffset() => _start.TextOffset;
    public void SetStart(in InlineItemTextIndex index)
    {
        _start = new InlineItemTextIndex { ItemIndex = index.ItemIndex, TextOffset = index.TextOffset };
    }

    /// <summary>Start text offset excluding OOF objects and zero-length items.</summary>
    public int InflowStartOffset()
    {
        foreach (var itemResult in _results)
        {
            var item = itemResult.Item;
            if ((item.Type == InlineItem.InlineItemType.Text || item.Type == InlineItem.InlineItemType.Control
                || item.Type == InlineItem.InlineItemType.AtomicInline) && item.Length > 0)
            {
                return itemResult.StartOffset;
            }
        }
        return EndTextOffset();
    }

    public InlineItemTextIndex End()
    {
        if (GetBreakToken() != null)
            return GetBreakToken()!.Start;
        if (_endItemIndex > 0 && _endItemIndex < ItemsData().Items.Count)
            return new InlineItemTextIndex { ItemIndex = _endItemIndex, TextOffset = ItemsData().Items[_endItemIndex].StartOffset };
        return ItemsData().End();
    }

    public int EndTextOffset()
    {
        if (GetBreakToken() != null)
            return GetBreakToken()!.StartTextOffset;
        if (_endItemIndex > 0 && _endItemIndex < ItemsData().Items.Count)
            return ItemsData().Items[_endItemIndex].StartOffset;
        return ItemsData().TextContent.Length;
    }

    public int InflowEndOffset() => InflowEndOffsetInternal(false);
    public int InflowEndOffsetWithoutForcedBreak() => InflowEndOffsetInternal(true);

    public int EndOffsetForJustify() => _endOffsetForJustify;

    public int EndItemIndex() => _endItemIndex;
    public void SetEndItemIndex(int index) => _endItemIndex = index;

    /// <summary>Compatibility with the legacy simplified LineInfo API.</summary>
    public int StartItemIndex => _start.ItemIndex;

    public bool GlyphCountIsGreaterThan(int limit)
    {
        int count = 0;
        foreach (var itemResult in _results)
        {
            count += GlyphCount(itemResult);
            if (count > limit) return true;
        }
        return false;
    }

    private static int GlyphCount(InlineItemResult itemResult)
    {
        if (itemResult.ShapeResult != null)
            return itemResult.ShapeResult.NumGlyphs;
        if (itemResult.LayoutResult != null)
            return 1;
        return 0;
    }

    public TextDirection BaseDirection() => _baseDirection;
    public void SetBaseDirection(TextDirection direction) => _baseDirection = direction;

    public bool NeedsAccurateEndPosition() => _needsAccurateEndPosition;

    public LayoutResult? BlockInInlineLayoutResult() => _blockInInlineLayoutResult;
    public void SetBlockInInlineLayoutResult(LayoutResult? layoutResult) => _blockInInlineLayoutResult = layoutResult;

    public bool MayHaveTextCombineOrRubyItem() => _mayHaveTextCombineOrRubyItem;
    public void SetHaveTextCombineOrRubyItem() => _mayHaveTextCombineOrRubyItem = true;

    public bool MayHaveRubyOverhang() => _mayHaveRubyOverhang;
    public void SetMayHaveRubyOverhang() => _mayHaveRubyOverhang = true;

    public float ComputeAnnotationBlockOffsetAdjustment()
    {
        if (_annotationBlockStartAdjustment < 0)
            return _annotationBlockStartAdjustment + _initialLetterBoxBlockStartAdjustment;
        return Math.Max(_annotationBlockStartAdjustment - _initialLetterBoxBlockStartAdjustment, 0);
    }

    public float ComputeBlockStartAdjustment()
    {
        if (_annotationBlockStartAdjustment < 0)
            return _annotationBlockStartAdjustment + _initialLetterBoxBlockStartAdjustment;
        return Math.Max(_annotationBlockStartAdjustment, _initialLetterBoxBlockStartAdjustment);
    }

    public float ComputeInitialLetterBoxBlockStartAdjustment()
    {
        if (_annotationBlockStartAdjustment == 0) return 0;
        if (_annotationBlockStartAdjustment < 0)
            return Math.Min(_initialLetterBoxBlockStartAdjustment + _annotationBlockStartAdjustment, 0);
        return Math.Max(_annotationBlockStartAdjustment - _initialLetterBoxBlockStartAdjustment, 0);
    }

    public float ComputeTotalBlockSize(float lineHeight, float annotationOverflowBlockEnd)
    {
        float lineHeightWithAnnotation = lineHeight + _annotationBlockStartAdjustment + annotationOverflowBlockEnd;
        return Math.Max(_initialLetterBoxBlockSize, lineHeightWithAnnotation);
    }

    public void SetAnnotationBlockStartAdjustment(float amount)
    {
        if (IsEmptyLine()) return;
        _annotationBlockStartAdjustment = amount;
    }

    public void SetInitialLetterBlockStartAdjustment(float amount)
    {
        if (amount < 0 || IsEmptyLine()) return;
        _initialLetterBoxBlockStartAdjustment = amount;
    }

    public void SetInitialLetterBoxBlockSize(float blockSize)
    {
        if (blockSize < 0) return;
        _initialLetterBoxBlockSize = blockSize;
    }

    public float AnnotationBlockStartAdjustmentInternal => _annotationBlockStartAdjustment;

    // ------------------------------------------------------------------
    // Helpers used by InlineLayoutAlgorithm / LogicalLineBuilder.
    // ------------------------------------------------------------------

    public void SetLineStyleDirect(ComputedStyle? style) => _lineStyle = style;

    public void SetItemsData(InlineItemsData data) => _itemsData = data;

    public void AddParallelResumeBreakToken(InlineBreakToken token) => _parallelFlowBreakTokens.Add(token);

    // ------------------------------------------------------------------
    // Private helpers ported from line_info.cc.
    // ------------------------------------------------------------------

    private TextAlignType GetTextAlign(bool isLastLine = false)
    {
        var style = LineStyle();
        return style.TextAlign;
    }

    private bool ComputeNeedsAccurateEndPosition()
    {
        var style = LineStyle();
        switch (GetTextAlign())
        {
            case TextAlignType.End:
            case TextAlignType.Center:
            case TextAlignType.Justify:
                return true;
            case TextAlignType.Left:
                return IsRtl(BaseDirection());
            case TextAlignType.Right:
                return IsLtr(BaseDirection());
        }
        return false;
    }

    private float ComputeTrailingSpaceWidth(out int endOffsetOut)
    {
        endOffsetOut = InflowEndOffset();
        if (!_hasTrailingSpaces) return 0;

        float trailingSpacesWidth = 0;
        for (int i = _results.Count - 1; i >= 0; i--)
        {
            var itemResult = _results[i];
            var item = itemResult.Item;

            if (item.EndCollapseType())
                continue;

            float trailingItemWidth = 0;
            bool willContinue = false;
            int endOffset = itemResult.EndOffset;

            if (item.Type == InlineItem.InlineItemType.Control || itemResult.HasOnlyPreWrapTrailingSpaces)
            {
                trailingItemWidth = itemResult.InlineSize;
                willContinue = true;
            }
            else if (item.Type == InlineItem.InlineItemType.Text)
            {
                if (itemResult.Length == 0)
                {
                    continue;
                }
                var text = ItemsData().TextContent;
                if (endOffset > 0 && IsHangingSpace(text[endOffset - 1]))
                {
                    do
                    {
                        --endOffset;
                    } while (endOffset > itemResult.StartOffset && IsHangingSpace(text[endOffset - 1]));

                    if (endOffset == itemResult.StartOffset)
                    {
                        trailingItemWidth = itemResult.InlineSize;
                        willContinue = true;
                    }
                    else
                    {
                        if (itemResult.ShapeResult != null)
                        {
                            float endPosition = itemResult.ShapeResult.PositionForOffset(endOffset);
                            trailingItemWidth = IsRtl(BaseDirection())
                                ? endPosition
                                : itemResult.ShapeResult.InlineSize - endPosition;
                        }
                    }
                }
            }

            if (trailingItemWidth != 0)
            {
                trailingSpacesWidth += trailingItemWidth;
            }

            if (!willContinue)
            {
                endOffsetOut = endOffset;
                return trailingSpacesWidth;
            }
        }

        endOffsetOut = StartOffset();
        return trailingSpacesWidth;
    }

    private float ComputeTrailingSpaceWidth()
    {
        return ComputeTrailingSpaceWidth(out _);
    }

    private static bool IsHangingSpace(char c) => c == ' ' || Character.IsOtherSpaceSeparator(c);

    private int InflowEndOffsetInternal(bool skipForcedBreak)
    {
        for (int i = _results.Count - 1; i >= 0; i--)
        {
            var itemResult = _results[i];
            var item = itemResult.Item;
            if (skipForcedBreak)
            {
                if (item.Type == InlineItem.InlineItemType.Control && ItemsData().TextContent[item.StartOffset] == '\n')
                    continue;
                if (item.Type == InlineItem.InlineItemType.Text && item.Length == 0)
                    continue;
            }
            if (item.Type == InlineItem.InlineItemType.Text || item.Type == InlineItem.InlineItemType.Control
                || item.Type == InlineItem.InlineItemType.AtomicInline)
            {
                return itemResult.EndOffset;
            }
        }
        return StartOffset();
    }

    private static bool IsLtr(TextDirection direction) => direction == TextDirection.Ltr;
    private static bool IsRtl(TextDirection direction) => direction == TextDirection.Rtl;
}

/// <summary>Directional predicate helpers shared by the inline port.</summary>
public static class TextDirectionUtils
{
    public static bool IsLtr(TextDirection direction) => direction == TextDirection.Ltr;
    public static bool IsRtl(TextDirection direction) => direction == TextDirection.Rtl;
}