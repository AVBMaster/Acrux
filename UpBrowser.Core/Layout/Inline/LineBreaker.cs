using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;
using CssLineBreakType = UpBrowser.Core.Dom.LineBreakType;

namespace UpBrowser.Core.Layout.Inline;

/// <summary>
/// The line breaking mode, mirroring LineBreakerMode.
/// </summary>
public enum LineBreakerMode
{
    Content,
    MinContent,
    MaxContent,
}

/// <summary>
/// Breaks inline items into lines. Mirrors line_breaker.h / line_breaker.cc.
///
/// The item list produced by <see cref="InlineItemsBuilder"/> is walked once;
/// text runs are shaped on demand via <see cref="HarfBuzzShaper"/> because
/// built items do not carry a pre-computed <see cref="ShapeResult"/>.
///
/// Not ported (out of scope for this port):
///  - floats positioning / exclusion spaces (floats are added as plain items),
///  - block-in-inline layout (treated as an atomic-like item forcing a break),
///  - ruby columns, SVG text, text-combine, score/bisect line breaking,
///  - hyphenation (auto hyphenation is unavailable; soft hyphens are honored).
/// </summary>
public class LineBreaker
{
    private enum LineBreakState
    {
        Done,
        Overflow,
        Trailing,
        Continue,
    }

    private enum WhitespaceState
    {
        Leading,
        None,
        Unknown,
        Collapsible,
        Collapsed,
        Preserved,
    }

    private enum BreakResult
    {
        Success,
        BreakAt,
        Overflow,
    }

    private InlineItemsData? _itemsData;
    private string _text = "";
    private LazyLineBreakIterator _breakIterator = new("");
    private HarfBuzzShaper _shaper = new("");
    private LineBreakerMode _mode = LineBreakerMode.Content;
    private bool _disallowAutoWrap;
    private bool _autoWrap = true;
    private bool _breakAnywhereIfOverflow;
    private bool _overrideBreakAnywhere;
    private bool _isForcedBreak;
    private bool _previousLineHadForcedBreak;
    private bool _isFirstFormattedLine = true;
    private bool _useFirstLineStyle;
    private int _currentItemIndex;
    private int _currentTextOffset;
    private float _position;
    private float _availableWidth;
    /// <summary>Full content inline size of the block, used as the basis for a
    /// percentage 'text-indent' (floats shrink line boxes, not the containing
    /// block; CSS Text 3 §5.2).</summary>
    private float _inlineBaseWidth;
    /// <summary>Where the current line box starts inside the content box. A left
    /// float moves the line box right (CSS 2.1 §9.5.2).</summary>
    private float _lineLeftInset;
    /// <summary>Right margin of the current line box consumed by a right float.</summary>
    private float _lineRightInset;
    /// <summary>Float machinery of the formatting context this text flows in.</summary>
    private FloatLineContext? _floatContext;
    /// <summary>Block offset (formatting-context coordinates) of the line being broken.</summary>
    private float _currentLineBlockOffset;
    /// <summary>Floats laid out and positioned while breaking lines; the owning
    /// algorithm turns them into child fragments of the block.</summary>
    public readonly List<PlacedFloat> PlacedFloats = new();
    /// <summary>Elements already positioned during this pass, so a rewind does not
    /// register the same float twice.</summary>
    private readonly List<Element> _placedFloats = new();
    private float _appliedTextIndent;
    private ComputedStyle _currentStyle = new();
    private ComputedStyle _lineStyle = new();
    /// <summary>Style of the box without ::first-line, restored on later lines.</summary>
    private ComputedStyle? _baseLineStyle;
    /// <summary>Merged ::first-line style, used while laying out the first line.</summary>
    private ComputedStyle? _firstLineStyle;

    /// <summary>Provide the merged ::first-line style (CSS Pseudo-Elements 4 §4).</summary>
    public void SetFirstLineStyle(ComputedStyle? firstLineStyle) => _firstLineStyle = firstLineStyle;
    private int? _hyphenIndex;
    private bool _hasAnyHyphens;
    private LineBreakState _state;
    private WhitespaceState _trailingWhitespace = WhitespaceState.Leading;

    // Unit-resolution context, pushed by the owning algorithm so atomic
    // inlines (inline-block/replaceables) resolve rem/vw/vh against the document.
    private float _rootFontSize = ConstraintSpace.DefaultRootFontSize;
    private float _viewportWidth;
    private float _viewportHeight;

    public void SetUnitContext(float rootFontSize, float viewportWidth, float viewportHeight)
    {
        _rootFontSize = rootFontSize > 0 ? rootFontSize : ConstraintSpace.DefaultRootFontSize;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
    }

    public LineBreaker()
    {
    }

    public LineBreakerMode Mode => _mode;

    /// <summary>
    /// Breaks all items into lines given the available inline size.
    /// Each line is a fresh <see cref="LineInfo"/> with results, line style,
    /// inline size and baseline info populated.
    /// </summary>
    /// <param name="containerLineStyle">
    /// Unit-resolution context: the block's own style, used as the line style so text-align /
    /// text-indent apply even when the item stream carries no OpenTag for the
    /// container (plain-text blocks). Falls back to sniffing the first OpenTag.
    /// </param>
    public List<LineInfo> BreakLines(InlineItemsData data, float availableInlineSize,
        ComputedStyle? containerLineStyle = null)
    {
        BeginBreaking(data, availableInlineSize, containerLineStyle);

        var lines = new List<LineInfo>();
        int guard = 0;
        while (!IsFinished() && guard++ < 1048576)
        {
            var lineInfo = BreakNextLine();
            // A trailing forced break ("text<br>") does not open an extra empty
            // line box; only breaks followed by more content do ("a<br><br>").
            if (lineInfo.IsLastLine() && lineInfo.IsEmptyLine() && _previousLineHadForcedBreak)
                break;
            lines.Add(lineInfo);
            if (lineInfo.IsLastLine()) break;
        }
        return lines;
    }

    /// <summary>
    /// Prepares the breaker for a line-by-line loop driven by the owning algorithm.
    /// Splitting the loop out of <see cref="BreakLines"/> is what lets a
    /// float-aware caller feed each line its own inline range and vertical
    /// position before that line is broken.
    /// </summary>
    public void BeginBreaking(InlineItemsData data, float availableInlineSize,
        ComputedStyle? containerLineStyle = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        _itemsData = data;
        _text = data.TextContent;
        _breakIterator = new LazyLineBreakIterator(_text);
        _shaper = new HarfBuzzShaper(_text);
        _mode = LineBreakerMode.Content;
        _disallowAutoWrap = false;
        _currentItemIndex = 0;
        _currentTextOffset = 0;
        _position = 0;
        _appliedTextIndent = 0;
        _isFirstFormattedLine = true;
        _useFirstLineStyle = false;
        _isForcedBreak = false;
        _previousLineHadForcedBreak = false;
        _overrideBreakAnywhere = false;
        _availableWidth = availableInlineSize;
        _inlineBaseWidth = availableInlineSize;
        _lineLeftInset = 0;
        _lineRightInset = 0;
        _state = LineBreakState.Continue;
        _trailingWhitespace = WhitespaceState.Leading;
        _hyphenIndex = null;
        _hasAnyHyphens = false;
        PlacedFloats.Clear();
        _placedFloats.Clear();
        _floatContext = null;
        _currentLineBlockOffset = 0;
        _lineStyle = containerLineStyle ?? ComputeInitialLineStyle(data);
        _baseLineStyle = _lineStyle;
        if (_firstLineStyle != null && _isFirstFormattedLine)
            _lineStyle = _firstLineStyle;
        SetCurrentStyleForce(_lineStyle);
    }

    /// <summary>Breaks one line. Callers that lay out line-by-line use this instead
    /// of <see cref="BreakLines"/> so they can update the line geometry between
    /// lines.</summary>
    public LineInfo BreakNextLine()
    {
        var lineInfo = new LineInfo();
        NextLine(lineInfo);
        return lineInfo;
    }

    /// <summary>
    /// Gives the line about to be broken its own inline range. |leftInset| and
    /// |availableWidth| come from the formatting context's exclusion space, and
    /// |blockOffset| is the line box's block offset in the same coordinates (used
    /// to position floats that appear on this line).
    /// </summary>
    public void SetLineSpace(float leftInset, float availableWidth, float blockOffset)
    {
        _lineLeftInset = leftInset;
        _availableWidth = availableWidth;
        _lineRightInset = Math.Max(0, _inlineBaseWidth - leftInset - availableWidth);
        _currentLineBlockOffset = blockOffset;
    }

    /// <summary>Enables float-aware line breaking for this pass (see
    /// <see cref="FloatLineContext"/>).</summary>
    public void SetFloatContext(FloatLineContext? context) => _floatContext = context;

    /// <summary>True while any exclusion (float / initial letter) can affect the
    /// line boxes of this pass.</summary>
    public bool HasFloatContext => _floatContext != null;

    /// <summary>Whether the line just broken ended in a forced break; a trailing
    /// forced break does not open an extra empty line box.</summary>
    public bool PreviousLineHadForcedBreak => _previousLineHadForcedBreak;

    /// <summary>Inline range available to a line box starting at |blockOffset|
    /// (formatting-context coordinates) and |blockSize| tall, expressed relative to
    /// the content box: left inset and available width. Also returns the block
    /// offset to restart at when the line has no inline room left.</summary>
    public (float LeftInset, float Width, float PushDownTo) LineSpaceAt(float blockOffset, float blockSize)
    {
        if (_floatContext == null)
            return (0, _inlineBaseWidth, float.NaN);
        return _floatContext.LineSpaceAt(blockOffset, blockSize);
    }

    public bool IsFinishedNow() => IsFinished();

    /// <summary>
    /// Breaks the next line into |lineInfo|. Mirrors LineBreaker::NextLine().
    /// </summary>
    public void NextLine(LineInfo lineInfo)
    {
        _position = 0;
        _state = LineBreakState.Continue;
        _trailingWhitespace = WhitespaceState.Leading;

        PrepareNextLine(lineInfo);

        if (IsFinished())
        {
            lineInfo.SetIsLastLine(true);
            return;
        }

        // To compute the text indent for the next line, if any, compute from the
        // applied text indent, or the style.
        _appliedTextIndent = 0;

        BreakLine(lineInfo);

        if (_hyphenIndex.HasValue)
            FinalizeHyphen(lineInfo);

        // Position is at the end of the line. Prepare for the next line.
        lineInfo.SetEndItemIndex(_currentItemIndex);
        lineInfo.SetBfcOffset(in BfcOffset.Zero);
        lineInfo.ComputeWidth();

        if (_mode == LineBreakerMode.Content)
        {
            float availableWidth = _availableWidth;
            if (!_overrideBreakAnywhere && _breakAnywhereIfOverflow && _isForcedBreak)
            {
                // Force to break the line.
                availableWidth = _position;
            }
            // Floats can be removed by the float rewind, which makes the line
            // shorter than the available width.
            if (lineInfo.HasOverflow())
                availableWidth = _position;

            lineInfo.SetWidth(availableWidth, lineInfo.ComputeWidth());
            // Unit-resolution context: update for EVERY alignment. The old guard skipped justify,
            // which left _textAlign at Start so justify/center/right never ran.
            // UpdateTextAlign also computes the hang width + justify end offset
            // that JustificationUtils relies on.
            if (lineInfo.HasLineStyle)
                lineInfo.UpdateTextAlign();
        }
        else
        {
            // Intrinsic size modes don't have text indent.
            lineInfo.SetWidth(_position, _position);
        }

        if (IsFinished() && !_isForcedBreak)
            lineInfo.SetIsLastLine(true);

        // A forced break marks the line as broken but does NOT end the block:
        // content after the <br> continues on the following line (the outer
        // loop stops via IsFinished once the last item is consumed).
        if (_isForcedBreak)
            lineInfo.SetHasForcedBreak();

        // Empty lines (e.g. only open/close tags) have no inflow content.
        if (lineInfo.Results().Count == 0)
            lineInfo.SetIsEmptyLine();

        // Carry the exact text of each text run so downstream consumers
        // (LogicalLineBuilder / FragmentItemsBuilder) can paint it.
        SetResultTextContents(lineInfo);
    }

    // ------------------------------------------------------------------
    // Line breaking loop.
    // ------------------------------------------------------------------

    private void BreakLine(LineInfo lineInfo)
    {
        while (true)
        {
            if (IsFinished())
            {
                _state = LineBreakState.Done;
                return;
            }

            var item = ItemList[_currentItemIndex];
            _itemsData!.AssertOffset(_currentItemIndex, _currentTextOffset);

            switch (item.Type)
            {
                case InlineItem.InlineItemType.Control:
                    HandleControlItem(item, lineInfo);
                    break;
                case InlineItem.InlineItemType.Floating:
                    HandleFloat(item, lineInfo);
                    break;
                case InlineItem.InlineItemType.OutOfFlowPositioned:
                    HandleOutOfFlowPositioned(item, lineInfo);
                    break;
                case InlineItem.InlineItemType.InitialLetterBox:
                case InlineItem.InlineItemType.AtomicInline:
                    HandleAtomicInline(item, lineInfo);
                    break;
                case InlineItem.InlineItemType.BlockInInline:
                    HandleBlockInInline(item, lineInfo);
                    break;
                case InlineItem.InlineItemType.OpenTag:
                    HandleOpenTag(item, lineInfo);
                    break;
                case InlineItem.InlineItemType.CloseTag:
                    HandleCloseTag(item, lineInfo);
                    break;
                case InlineItem.InlineItemType.Text:
                    HandleText(item, lineInfo);
                    break;
                case InlineItem.InlineItemType.BidiControl:
                    HandleBidiControl(item, lineInfo);
                    break;
                default:
                    HandleEmptyText(item, lineInfo);
                    break;
            }

            if (_state == LineBreakState.Continue)
                continue;

            HandleOverflowIfNeeded(lineInfo);

            if (_state == LineBreakState.Done)
                return;
        }
    }

    // ------------------------------------------------------------------
    // Item handlers.
    // ------------------------------------------------------------------

    private void HandleText(InlineItem item, LineInfo lineInfo)
    {
        HandleText(item, ShapeText(item, item.StartOffset, item.EndOffset), lineInfo);
    }

    private void HandleText(InlineItem item, ShapeResult shapeResult, LineInfo lineInfo)
    {
        // If we're trailing, only trailing spaces can be included in this line.
        if (_state == LineBreakState.Trailing)
        {
            HandleTrailingSpaces(item, shapeResult, lineInfo);
            return;
        }

        // Skip leading collapsible spaces.
        // Most cases such spaces are handled as trailing spaces of the previous
        // line, but there are some cases doing so is too complex.
        if (_trailingWhitespace == WhitespaceState.Leading)
        {
            if (WhiteSpaceStyle.ShouldCollapseWhiteSpaces(item.Style())
                && _currentTextOffset < item.EndOffset
                && _text[_currentTextOffset] == Character.kSpaceCharacter)
            {
                // Skipping one whitespace removes all collapsible spaces because
                // collapsible spaces are collapsed to a single space in the builder.
                ++_currentTextOffset;
                if (_currentTextOffset == item.EndOffset)
                {
                    HandleEmptyText(item, lineInfo);
                    return;
                }
            }
        }

        // Go to |HandleOverflow()| if the last item overflowed, and we're adding
        // text.
        if (_state == LineBreakState.Continue && !CanFitOnLine())
        {
            if (_autoWrap && _currentTextOffset < item.EndOffset
                && (Character.IsBreakableSpace(_text[_currentTextOffset]) || Character.IsOtherSpaceSeparator(_text[_currentTextOffset])))
            {
                HandleTrailingSpaces(item, shapeResult, lineInfo);
                if (_state != LineBreakState.Done)
                {
                    _state = LineBreakState.Continue;
                    return;
                }
            }
            HandleOverflow(lineInfo);
            return;
        }

        if (HasHyphen())
            _position -= RemoveHyphen(lineInfo.MutableResults());

        var itemResult = AddItem(item, lineInfo);
        itemResult.ShouldCreateLineBox = true;

        if (_autoWrap)
        {
            // Try to break inside of this text item.
            float availableWidth = RemainingAvailableWidth();
            var breakResult = BreakText(itemResult, item, shapeResult, availableWidth, availableWidth, lineInfo);
            _position += itemResult.InlineSize;
            MoveToNextOf(itemResult);

            if (breakResult == BreakResult.Success)
            {
                // If the break is at the middle of a text item, we know no trailable
                // items follow, only trailable spaces if any. This is very common,
                // so shortcut to handling trailing spaces.
                if (itemResult.EndOffset < item.EndOffset)
                {
                    HandleTrailingSpaces(item, shapeResult, lineInfo);
                    return;
                }
                // The break point found at the end of this text item. Continue
                // looking for next items, because the next item may be trailable,
                // or can prohibit breaking before.
                return;
            }

            // |kOverflow|.
            // Handle 'overflow-wrap' if it is enabled and if this text item
            // overflows.
            if (itemResult.ShapeResult == null)
            {
                HandleOverflow(lineInfo);
                return;
            }

            // Hanging trailing spaces may resolve the overflow.
            if (itemResult.HasOnlyPreWrapTrailingSpaces)
            {
                _state = LineBreakState.Trailing;
                return;
            }

            // If we're seeking for the first break opportunity, update the state.
            if (_state == LineBreakState.Overflow)
            {
                if (itemResult.CanBreakAfter)
                    _state = LineBreakState.Trailing;
                return;
            }

            // If this is all trailable spaces, this item is trailable, and next
            // item may be too. Don't go to |HandleOverflow()| yet.
            if (IsAllBreakableSpaces(_text, itemResult.StartOffset, itemResult.EndOffset))
                return;

            HandleOverflow(lineInfo);
            return;
        }

        // Add until the end of the item if !auto_wrap. In most cases, it's the
        // whole item.
        itemResult.ShapeResult = ShapeResultView.Create(shapeResult, itemResult.StartOffset, itemResult.EndOffset);
        itemResult.InlineSize = Math.Max(0, itemResult.ShapeResult.SnappedWidth());
        itemResult.MayBreakInside = false;
        itemResult.CanBreakAfter = false;
        _trailingWhitespace = WhitespaceState.Unknown;
        _position += itemResult.InlineSize;
        MoveToNextOf(item);
    }

    private void HandleEmptyText(InlineItem item, LineInfo lineInfo)
    {
        // Add an empty |InlineItemResult| for empty or fully collapsed text. They
        // aren't necessary for line breaking/layout purposes, but callsites may
        // need to see all |InlineItem| by iterating |InlineItemResult|.
        AddEmptyItem(item, lineInfo);
        MoveToNextOf(item);
    }

    private void HandleControlItem(InlineItem item, LineInfo lineInfo)
    {
        if (item.TextType == TextItemType.kForcedLineBreak)
        {
            HandleForcedLineBreak(item, lineInfo);
            return;
        }

        if (item.StartOffset >= _text.Length || _currentTextOffset >= item.EndOffset)
        {
            HandleEmptyText(item, lineInfo);
            return;
        }

        char character = _text[item.StartOffset];
        switch (character)
        {
            case Character.kTabulationCharacter:
                {
                    if (HandleTab(item, lineInfo))
                        return;
                    var shapeResult = ShapeText(item, _currentTextOffset, item.EndOffset);
                    HandleText(item, shapeResult, lineInfo);
                    return;
                }
            case Character.kZeroWidthSpaceCharacter:
                {
                    // <wbr> tag creates break opportunities regardless of auto_wrap.
                    var itemResult = AddItem(item, lineInfo);
                    // A generated break opportunity doesn't generate fragments, but
                    // we still need to add this for rewind to find this opportunity.
                    if (!item.IsGeneratedForLineBreak)
                        itemResult.ShouldCreateLineBox = true;
                    itemResult.CanBreakAfter = true;
                    MoveToNextOf(item);
                    return;
                }
            case Character.kCarriageReturnCharacter:
            case Character.kFormFeedCharacter:
                // Ignore carriage return and form feed.
                HandleEmptyText(item, lineInfo);
                return;
            default:
                HandleEmptyText(item, lineInfo);
                return;
        }
    }

    /// <summary>
    /// Gives a tab character the advance needed to reach the next tab stop
    /// (CSS Text 3 §3.4): stops sit 'tab-size' apart from the start of the line box,
    /// and a unitless tab-size counts advances of U+0020 in the element's font.
    /// Returns false when the tab should fall back to ordinary shaping.
    /// </summary>
    private bool HandleTab(InlineItem item, LineInfo lineInfo)
    {
        float tabSize = UsedTabSize(_lineStyle);
        if (float.IsNaN(tabSize) || tabSize <= 0)
            return false;

        var itemResult = AddItem(item, lineInfo);
        float x = Math.Max(0, _position);
        itemResult.InlineSize = (float)Math.Floor(x / tabSize + 1.0) * tabSize - x;
        // A tab is pure advance: it paints no glyph, and 'pre' never breaks on one.
        itemResult.ShouldCreateLineBox = false;
        itemResult.CanBreakAfter = false;
        _position += itemResult.InlineSize;
        _trailingWhitespace = WhitespaceState.Preserved;
        MoveToNextOf(item);
        return true;
    }

    private static float UsedTabSize(ComputedStyle style)
    {
        if (style.TabSizePx is float px)
            return px;
        if (style.TabSize <= 0)
            return 0;
        float spaceWidth = TextMeasureProxy.MeasureCharacter(' ', style);
        return spaceWidth > 0 ? spaceWidth * style.TabSize : float.NaN;
    }

    private void HandleBidiControl(InlineItem item, LineInfo lineInfo)
    {
        var itemResult = AddItem(item, lineInfo);
        itemResult.ShouldCreateLineBox = true;
        MoveToNextOf(item);
    }

    private void HandleForcedLineBreak(InlineItem? item, LineInfo lineInfo)
    {
        // Check overflow, because the last item may have overflowed.
        if (HandleOverflowIfNeeded(lineInfo))
            return;

        if (item != null)
        {
            var itemResult = AddItem(item, lineInfo);
            itemResult.ShouldCreateLineBox = true;
            itemResult.HasOnlyPreWrapTrailingSpaces = true;
            itemResult.HasOnlyBidiTrailingSpaces = true;
            itemResult.CanBreakAfter = true;
            MoveToNextOf(item);

            // Include following close tags. The difference is visible when they
            // have margin/border/padding.
            while (!IsAtEnd())
            {
                var nextItem = ItemList[_currentItemIndex];
                if (nextItem.Type == InlineItem.InlineItemType.CloseTag)
                {
                    HandleCloseTag(nextItem, lineInfo);
                    continue;
                }
                if (nextItem.Type == InlineItem.InlineItemType.Text && nextItem.Length == 0)
                {
                    HandleEmptyText(nextItem, lineInfo);
                    continue;
                }
                break;
            }
        }

        if (HasHyphen())
            _position -= RemoveHyphen(lineInfo.MutableResults());
        _isForcedBreak = true;
        lineInfo.SetHasForcedBreak();
        _state = LineBreakState.Done;
    }

    private void HandleOpenTag(InlineItem item, LineInfo lineInfo)
    {
        var itemResult = AddItem(item, lineInfo);
        var style = item.Style();
        if (ComputeOpenTagResult(item, LineConstraintSpace(), false, itemResult))
        {
            // Negative margins on open tags may bring the position back. Update
            // |state_| if that happens.
            if (itemResult.InlineSize < 0 && _state == LineBreakState.Trailing)
            {
                float availableWidth = AvailableWidthToFit();
                if (_position > availableWidth && _position + itemResult.InlineSize <= availableWidth)
                    _state = LineBreakState.Continue;
            }

            _position += itemResult.InlineSize;

            // Force to create a box, because such inline boxes affect line heights.
            if (!itemResult.ShouldCreateLineBox && !item.IsEmptyItem())
                itemResult.ShouldCreateLineBox = true;
        }

        bool wasAutoWrap = _autoWrap;
        SetCurrentStyle(style);
        MoveToNextOf(item);

        var itemResults = lineInfo.Results();
        if (!wasAutoWrap && _autoWrap && itemResults.Count >= 2)
        {
            if (IsPreviousItemOfType(InlineItem.InlineItemType.Text))
            {
                var prev = itemResults[itemResults.Count - 2];
                if (!prev.CanBreakAfter)
                    ComputeCanBreakAfter(prev, _autoWrap, _breakIterator);
            }
        }
    }

    private void HandleCloseTag(InlineItem item, LineInfo lineInfo)
    {
        var itemResult = AddItem(item, lineInfo);

        var style = item.Style();
        itemResult.InlineSize = InlineLengthUtils.ComputeInlineEndSize(LineConstraintSpace(), style);
        _position += itemResult.InlineSize;

        if (!itemResult.ShouldCreateLineBox && !item.IsEmptyItem())
            itemResult.ShouldCreateLineBox = true;

        bool wasAutoWrap = _autoWrap;
        SetCurrentStyle(style);
        MoveToNextOf(item);

        // If the line can break after the previous item, prohibit it and allow
        // break after this close tag instead. Even when the close tag has
        // "nowrap", break after it is allowed if the line is breakable after the
        // previous item.
        var itemResults = lineInfo.Results();
        if (itemResults.Count >= 2)
        {
            var last = itemResults[itemResults.Count - 2];
            if (last.CanBreakAfter)
            {
                // A break opportunity before a close tag always propagates to
                // after the close tag.
                itemResult.CanBreakAfter = true;
                last.CanBreakAfter = false;
                return;
            }
            if (wasAutoWrap)
            {
                // We can break before a breakable space if we either:
                //   a) allow breaking before a white space, or
                //   b) the break point is preceded by another breakable space.
                bool precededByBreakableSpace = itemResult.EndOffset > 0 && Character.IsBreakableSpace(_text[itemResult.EndOffset - 1]);
                itemResult.CanBreakAfter = itemResult.EndOffset < _text.Length
                    && Character.IsBreakableSpace(_text[itemResult.EndOffset])
                    && (!WhiteSpaceStyle.ShouldBreakOnlyAfterWhiteSpace(style) || precededByBreakableSpace);
                return;
            }
            if (_autoWrap && itemResult.EndOffset > 0 && !Character.IsBreakableSpace(_text[itemResult.EndOffset - 1]))
                ComputeCanBreakAfter(itemResult, _autoWrap, _breakIterator);
        }
    }

    private void HandleAtomicInline(InlineItem item, LineInfo lineInfo)
    {
        var style = item.Style();

        float remainingWidth = RemainingAvailableWidth();
        if (_state == LineBreakState.Continue && remainingWidth < 0)
        {
            int itemIndex = _currentItemIndex;
            HandleOverflow(lineInfo);
            if (!lineInfo.HasOverflow() || itemIndex != _currentItemIndex)
                return;
            // Compute margins before computing overflow, because even when the
            // current position is beyond the end, negative margins can bring this
            // item back to on the current line.
            _state = LineBreakState.Continue;
        }

        var itemResult = AddItem(item, lineInfo);
        itemResult.Margins = InlineLengthUtils.ComputeLineMarginsForVisualContainer(LineConstraintSpace(), style);
        float inlineMargins = itemResult.Margins.Left + itemResult.Margins.Right;

        if (HasHyphen())
            _position -= RemoveHyphen(lineInfo.MutableResults());

        // Lay out the atomic inline as an independent formatting context so it
        // contributes its real border-box size (and later, baseline) to the line.
        // The result is stored on the item result; LogicalLineBuilder reads it to
        // place the box. Without this the box collapses to a font-size-wide sliver
        // and its background/border never paints.
        var layoutResult = LayoutAtomicInline(item, style);
        float inlineSize;
        if (layoutResult != null)
        {
            itemResult.LayoutResult = layoutResult;
            item.InlineSize = layoutResult.Fragment.InlineSize;
            inlineSize = layoutResult.Fragment.InlineSize;
        }
        else
        {
            inlineSize = item.InlineSize > 0 ? item.InlineSize : style.FontSize;
        }

        itemResult.InlineSize = inlineSize + inlineMargins;
        itemResult.ShouldCreateLineBox = true;
        itemResult.CanBreakAfter = CanBreakAfterAtomicInline(item);

        _position += itemResult.InlineSize;
        _trailingWhitespace = WhitespaceState.None;
        MoveToNextOf(item);
    }

    /// <summary>
    /// Run block layout on an atomic inline element (inline-block / inline-flex /
    /// inline-grid / replaced) to obtain its fragment. Only performed while
    /// generating real content (not during min/max-content sizing passes), and
    /// only when the item carries a DOM element.
    /// </summary>
    private LayoutResult? LayoutAtomicInline(InlineItem item, ComputedStyle style)
    {
        if (_mode != LineBreakerMode.Content)
            return null;

        var element = item.Element;
        if (element == null)
            return null;

        // An atomic inline establishes an independent (new) formatting context and
        // is sized shrink-to-fit against the line's available inline size.
        float available = _availableWidth;
        if (float.IsNaN(available) || float.IsInfinity(available) || available <= 0)
            available = Geometry.LayoutUnitUtils.NearlyMax();

        var space = ConstraintSpace.Builder(available, float.NaN)
            .SetIsNewFormattingContext(true)
            .SetShrinkToFit(true)
            .SetBfcBlockOffset(0)
            .SetForcedBfcBlockOffset(0)
            .SetDirection(style.Direction == "rtl" ? TextDirection.Rtl : TextDirection.Ltr)
            .SetRootFontSize(_rootFontSize)
            .SetViewportSize(_viewportWidth, _viewportHeight)
            .ToConstraintSpace();

        try
        {
            var result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
            return result.Status == EStatus.Success ? result : null;
        }
        catch
        {
            // A failure to lay out one atomic inline must not abort the whole line;
            // fall back to the style-based size.
            return null;
        }
    }

    private void HandleBlockInInline(InlineItem item, LineInfo lineInfo)
    {
        if (lineInfo.Results().Count > 0)
        {
            // If there were any items, force a line break before this item.
            HandleForcedLineBreak(null, lineInfo);
            return;
        }

        var itemResult = AddItem(item, lineInfo);
        itemResult.InlineSize = item.InlineSize > 0 ? item.InlineSize : _lineStyle.FontSize;
        itemResult.ShouldCreateLineBox = itemResult.InlineSize > 0;
        _position += itemResult.InlineSize;
        lineInfo.SetIsBlockInInline();
        lineInfo.SetHasForcedBreak();
        _isForcedBreak = true;
        _trailingWhitespace = WhitespaceState.None;
        MoveToNextOf(item);
        _state = LineBreakState.Done;
    }

    private void HandleFloat(InlineItem item, LineInfo lineInfo)
    {
        // When rewind occurs, an item may be handled multiple times. Since floats
        // are put into a separate list, avoid handling the same floats twice.
        var itemResult = AddItem(item, lineInfo);
        itemResult.CanBreakAfter = _autoWrap;
        PositionFloatIfNeeded(item, lineInfo);
        MoveToNextOf(item);
    }

    /// <summary>
    /// Lays out a float that appears in the inline stream, positions it at the
    /// current line box (CSS 2.1 §9.5.1), and registers it with the formatting
    /// context so this and the following line boxes avoid it.
    /// </summary>
    private void PositionFloatIfNeeded(InlineItem item, LineInfo lineInfo)
    {
        var context = _floatContext;
        var element = item.Element;
        if (context == null || element == null || _mode != LineBreakerMode.Content)
            return;
        // Positioning a float twice would double-count it in the exclusion space.
        if (_placedFloats.Contains(element))
            return;

        var style = item.Style();
        // A float is sized shrink-to-fit against the containing block's content
        // width, not against the shortened line box (CSS 2.1 §10.3.5).
        var space = ConstraintSpace.Builder(context.ContentWidth, float.NaN)
            .SetIsNewFormattingContext(true)
            .SetShrinkToFit(true)
            .SetBfcBlockOffset(0)
            .SetForcedBfcBlockOffset(0)
            .SetDirection(item.Direction == TextDirection.Rtl ? TextDirection.Rtl : TextDirection.Ltr)
            .SetRootFontSize(_rootFontSize)
            .SetViewportSize(_viewportWidth, _viewportHeight)
            .ToConstraintSpace();

        LayoutResult result;
        try
        {
            result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
        }
        catch
        {
            return;
        }
        if (result.Status != EStatus.Success)
            return;

        var fragment = result.Fragment;
        float fontSize = style.FontSize;
        float ml = style.MarginLeft.ToPixels(fontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float mr = style.MarginRight.ToPixels(fontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float mt = style.MarginTop.ToPixels(fontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float mb = style.MarginBottom.ToPixels(fontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        // Negative margins cannot pull a float outside the line box it starts on.
        ml = Math.Max(0, ml);
        mr = Math.Max(0, mr);
        mt = Math.Max(0, mt);

        bool isLeft = style.Float != FloatType.Right;
        float lineStart = context.ContentLineStart + _lineLeftInset;
        float lineEnd = context.ContentLineStart + _inlineBaseWidth - _lineRightInset;
        float marginInlineStart = isLeft
            ? lineStart
            : lineEnd - mr - fragment.InlineSize;
        float marginBlockStart = _currentLineBlockOffset;

        // Offsets recorded on the fragment are border-box positions relative to the
        // container's content box (BoxFragmentBuilder.AddChild adds the margins).
        fragment.InlineOffset = marginInlineStart + ml - context.ContentLineStart;
        fragment.BlockOffset = marginBlockStart + mt - context.ContentTop;
        fragment.MarginLeft = ml;
        fragment.MarginRight = mr;
        fragment.MarginTop = mt;
        fragment.MarginBottom = mb;
        fragment.IsFloating = true;

        PlacedFloats.Add(new PlacedFloat(element, result));
        _placedFloats.Add(element);

        context.Space.Add(ExclusionArea.Create(
            new BfcRect(
                new BfcOffset(marginInlineStart, marginBlockStart),
                new BfcOffset(marginInlineStart + ml + fragment.InlineSize + mr,
                    marginBlockStart + mt + fragment.BlockSize + mb)),
            isLeft ? FloatType.Left : FloatType.Right, /* is_hidden_for_paint */ false));

        // The float takes part of this very line: re-read the inline range so the
        // items after it wrap early instead of running underneath it.
        RefreshCurrentLineSpace(context.LineBlockSize);
        lineInfo.LeftInset = _lineLeftInset;
        lineInfo.AvailableInlineSize = _availableWidth;
    }

    /// <summary>Re-reads the current line box's inline range from the exclusion
    /// space (used right after a float is positioned on this line).</summary>
    private void RefreshCurrentLineSpace(float blockSize)
    {
        var (inset, width, _) = LineSpaceAt(_currentLineBlockOffset, blockSize);
        _lineLeftInset = inset;
        _availableWidth = width;
        _lineRightInset = Math.Max(0, _inlineBaseWidth - inset - width);
    }

    private void HandleOutOfFlowPositioned(InlineItem item, LineInfo lineInfo)
    {
        var itemResult = AddItem(item, lineInfo);

        // Break opportunity after OOF is not well-defined nor interoperable.
        if (itemResult.ShouldCreateLineBox)
            ComputeCanBreakAfter(itemResult, _autoWrap, _breakIterator);

        MoveToNextOf(item);
    }

    /// <summary>
    /// Computes the border/padding/margin of an open tag. Returns true if the
    /// result contributes inline size (i.e. the box has visible inline extent).
    /// Mirrors LineBreaker::ComputeOpenTagResult().
    /// </summary>
    public static bool ComputeOpenTagResult(InlineItem item, ConstraintSpace constraintSpace, bool isInSvgText, InlineItemResult itemResult)
    {
        if (item.Type != InlineItem.InlineItemType.OpenTag)
            return false;
        var style = item.Style();
        if (!isInSvgText && item.ShouldCreateBoxFragment() && (style.HasBorder() || style.HasPadding() || style.HasMargin()))
        {
            itemResult.Borders = InlineLengthUtils.ComputeLineBordersForInline(style);
            itemResult.Padding = InlineLengthUtils.ComputeLinePadding(constraintSpace, style);
            itemResult.Margins = InlineLengthUtils.ComputeLineMarginsForSelf(constraintSpace, style);
            itemResult.InlineSize = itemResult.Margins.Left + itemResult.Borders.Left + itemResult.Padding.Left;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Text breaking.
    // ------------------------------------------------------------------

    private BreakResult BreakText(InlineItemResult itemResult, InlineItem item, ShapeResult itemShapeResult,
        float availableWidth, float availableWidthWithHyphens, LineInfo lineInfo)
    {
        var breaker = new ShapingLineBreaker(itemShapeResult, _breakIterator, item.Style());
        breaker.SetLineStart(lineInfo.StartOffset());
        breaker.SetIsAfterForcedBreak(_previousLineHadForcedBreak);
        breaker.SetDontReshapeEndIfAtSpace();
        if (_breakAnywhereIfOverflow && !_overrideBreakAnywhere)
            breaker.SetNoResultIfOverflow();

        var result = new ShapingLineBreaker.Result();
        var shapeResult = breaker.ShapeLine(itemResult.StartOffset, Math.Max(0, availableWidth), out result);

        if (shapeResult == null)
        {
            itemResult.InlineSize = availableWidthWithHyphens + 1;
            itemResult.EndOffset = item.EndOffset;
            return BreakResult.Overflow;
        }

        float inlineSize = Math.Max(0, shapeResult.SnappedWidth());
        itemResult.InlineSize = inlineSize;
        if (result.IsHyphenated)
        {
            var itemResults = lineInfo.MutableResults();
            float hyphenInlineSize = AddHyphen(itemResults, itemResult);
            if (!result.IsOverflow && inlineSize <= availableWidth)
            {
                float spaceForHyphen = availableWidthWithHyphens - inlineSize;
                if (spaceForHyphen >= 0 && hyphenInlineSize > spaceForHyphen)
                {
                    availableWidth -= hyphenInlineSize;
                    RemoveHyphen(itemResults);
                    shapeResult = breaker.ShapeLine(itemResult.StartOffset, Math.Max(0, availableWidth), out result);
                    if (shapeResult == null)
                    {
                        itemResult.InlineSize = availableWidthWithHyphens + 1;
                        itemResult.EndOffset = item.EndOffset;
                        return BreakResult.Overflow;
                    }
                    inlineSize = Math.Max(0, shapeResult.SnappedWidth());
                    itemResult.InlineSize = inlineSize;
                    if (result.IsHyphenated)
                        hyphenInlineSize = AddHyphen(itemResults, itemResult);
                }
            }
            inlineSize = itemResult.InlineSize;
        }

        itemResult.EndOffset = result.BreakOffset;
        itemResult.HasOnlyPreWrapTrailingSpaces = result.HasTrailingSpaces;
        itemResult.HasOnlyBidiTrailingSpaces = result.HasTrailingSpaces;
        itemResult.ShapeResult = shapeResult;

        if (itemResult.EndOffset < item.EndOffset)
        {
            // The break point is found inside of |item|.
            itemResult.CanBreakAfter = true;
            _trailingWhitespace = WhitespaceState.None;
        }
        else
        {
            // |itemResult| is at the end of |item|. No need to split.
            itemResult.CanBreakAfter = CanBreakAfter(item);
            _trailingWhitespace = WhitespaceState.Unknown;
        }

        itemResult.MayBreakInside = !result.IsOverflow;
        return inlineSize <= availableWidthWithHyphens ? BreakResult.Success : BreakResult.Overflow;
    }

    private bool BreakTextAtPreviousBreakOpportunity(InlineItemResult itemResult)
    {
        if (!itemResult.MayBreakInside) return false;
        var item = itemResult.Item;
        if (item.Type != InlineItem.InlineItemType.Text) return false;
        if (!WhiteSpaceStyle.ShouldWrapLine(item.Style())) return false;

        int breakOpportunity = _breakIterator.PreviousBreakOpportunity(itemResult.EndOffset - 1, itemResult.StartOffset);
        if (breakOpportunity <= itemResult.StartOffset) return false;
        itemResult.EndOffset = breakOpportunity;
        itemResult.ShapeResult = ShapeText(item, itemResult.StartOffset, itemResult.EndOffset);
        itemResult.InlineSize = itemResult.ShapeResult.SnappedWidth();
        itemResult.CanBreakAfter = true;
        return true;
    }

    private void HandleTrailingSpaces(InlineItem item, LineInfo lineInfo)
    {
        HandleTrailingSpaces(item, ShapeText(item, item.StartOffset, item.EndOffset), lineInfo);
    }

    private void HandleTrailingSpaces(InlineItem item, ShapeResult shapeResult, LineInfo lineInfo)
    {
        int textLength = _text.Length;
        var style = item.Style();

        if (!_autoWrap)
        {
            _state = LineBreakState.Done;
            return;
        }

        if (_currentTextOffset >= textLength)
        {
            _state = LineBreakState.Done;
            return;
        }

        if (WhiteSpaceStyle.ShouldCollapseWhiteSpaces(style) && !Character.IsOtherSpaceSeparator(_text[_currentTextOffset]))
        {
            if (_text[_currentTextOffset] != Character.kSpaceCharacter)
            {
                if (_currentTextOffset > 0 && Character.IsBreakableSpace(_text[_currentTextOffset - 1]))
                    _trailingWhitespace = WhitespaceState.Collapsible;
                _state = LineBreakState.Done;
                return;
            }

            _currentTextOffset++;
            _trailingWhitespace = WhitespaceState.Collapsed;
            var itemResults = lineInfo.MutableResults();
            if (itemResults.Count > 0)
                itemResults[itemResults.Count - 1].CanBreakAfter = true;
        }
        else if (!WhiteSpaceStyle.ShouldBreakSpaces(style))
        {
            int end = _currentTextOffset;
            while (end < item.EndOffset && (Character.IsBreakableSpace(_text[end]) || Character.IsOtherSpaceSeparator(_text[end])))
                end++;
            if (end == _currentTextOffset)
            {
                if (_currentTextOffset > 0
                    && (Character.IsBreakableSpace(_text[_currentTextOffset - 1]) || Character.IsOtherSpaceSeparator(_text[_currentTextOffset - 1])))
                    _trailingWhitespace = WhitespaceState.Preserved;
                _state = LineBreakState.Done;
                return;
            }

            var itemResult = AddItem(item, end, lineInfo);
            itemResult.ShouldCreateLineBox = true;
            itemResult.HasOnlyPreWrapTrailingSpaces = true;
            itemResult.HasOnlyBidiTrailingSpaces = true;
            itemResult.ShapeResult = ShapeResultView.Create(shapeResult);
            if (itemResult.StartOffset == item.StartOffset && itemResult.EndOffset == item.EndOffset)
                itemResult.InlineSize = itemResult.ShapeResult != null && _mode != LineBreakerMode.MinContent ? itemResult.ShapeResult.SnappedWidth() : 0;
            else
            {
                UpdateShapeResult(lineInfo, itemResult);
                if (_mode == LineBreakerMode.MinContent) itemResult.InlineSize = 0;
            }
            _position += itemResult.InlineSize;
            itemResult.CanBreakAfter = end < textLength && !(Character.IsBreakableSpace(_text[end]) || Character.IsOtherSpaceSeparator(_text[end]));
            _currentTextOffset = end;
            _trailingWhitespace = WhitespaceState.Preserved;
        }

        if (_currentTextOffset < item.EndOffset)
        {
            _state = LineBreakState.Done;
            return;
        }
        var results = lineInfo.Results();
        if (results.Count == 0 || results[results.Count - 1].Item != item)
        {
            AddEmptyItem(item, lineInfo);
        }
        _currentItemIndex++;
        _state = LineBreakState.Trailing;
    }

    private void UpdateShapeResult(LineInfo lineInfo, InlineItemResult itemResult)
    {
        if (itemResult.ShapeResult == null) return;
        itemResult.ShapeResult = ShapeResultView.Create(itemResult.ShapeResult, itemResult.StartOffset, itemResult.EndOffset);
        itemResult.InlineSize = itemResult.ShapeResult.SnappedWidth();
    }

    private ShapeResult ShapeText(InlineItem item, int start, int end)
    {
        return _shaper.Shape(item.Style(), item.Direction, start, end);
    }

    // ------------------------------------------------------------------
    // Overflow handling.
    // ------------------------------------------------------------------

    private void HandleOverflow(LineInfo lineInfo)
    {
        float availableWidth = AvailableWidthToFit();
        var itemResults = lineInfo.MutableResults();

        int? hyphenIndexBefore = _hyphenIndex;
        if (HasHyphen())
            _position -= RemoveHyphen(itemResults);

        // Compute the width needing to rewind. When |width_to_rewind| goes
        // negative, items can fit within the line.
        float widthToRewind = _position - availableWidth;

        // Keep track of the shortest break opportunity.
        int breakBefore = 0;

        // True if there is at least one item that has 'break-word'.
        bool hasBreakAnywhereIfOverflow = _breakAnywhereIfOverflow;

        // Search for a break opportunity that can fit.
        for (int i = itemResults.Count; i > 0;)
        {
            var itemResult = itemResults[--i];
            hasBreakAnywhereIfOverflow |= itemResult.BreakAnywhereIfOverflow;

            // Try to break after this item.
            if (i < itemResults.Count - 1 && itemResult.CanBreakAfter)
            {
                if (widthToRewind <= 0)
                {
                    _position = availableWidth + widthToRewind;
                    RewindOverflow(i + 1, lineInfo);
                    return;
                }
                breakBefore = i + 1;
            }

            // Compute the position after this item was removed entirely.
            widthToRewind -= itemResult.InlineSize;

            // Try next if still does not fit.
            if (widthToRewind > 0)
                continue;

            var item = itemResult.Item;
            if (item.Type == InlineItem.InlineItemType.Text)
            {
                if (itemResult.Length == 0)
                {
                    // Empty text items are trailable, see |HandleEmptyText|.
                    continue;
                }
                // If space is available, and if this text is breakable, part of
                // the text may fit. Try to break this item.
                if (widthToRewind < 0 && itemResult.MayBreakInside)
                {
                    float itemAvailableWidth = -widthToRewind;
                    // Make sure the available width is smaller than the current
                    // width. The break point must not be at the end when e.g., the
                    // text fits but its right margin does not or following items
                    // do not.
                    float minAvailableWidth = itemResult.InlineSize - 1;
                    // If |inline_size| is zero (e.g., 'font-size: 0'), |BreakText|
                    // cannot make it shorter. Take the previous break opportunity.
                    if (minAvailableWidth <= 0)
                    {
                        if (BreakTextAtPreviousBreakOpportunity(itemResult))
                        {
                            RewindOverflow(i + 1, lineInfo);
                            return;
                        }
                        continue;
                    }
                    var wasCurrentStyle = _currentStyle;
                    SetCurrentStyle(item.Style());
                    var itemResultBefore = Snapshot(itemResult);
                    BreakText(itemResult, item, ShapeText(item, item.StartOffset, item.EndOffset),
                        Math.Min(itemAvailableWidth, minAvailableWidth), itemAvailableWidth, lineInfo);

                    // If BreakText() changed this item small enough to fit, break
                    // here.
                    if (itemResult.CanBreakAfter && itemResult.InlineSize <= itemAvailableWidth
                        && itemResult.EndOffset < itemResultBefore.EndOffset)
                    {
                        // If this is the last item, adjust it to accommodate the
                        // change.
                        int newEnd = i + 1;
                        if (newEnd == itemResults.Count)
                        {
                            _position = availableWidth + widthToRewind + itemResult.InlineSize;
                            _currentTextOffset = itemResult.EndOffset;
                            _currentItemIndex = itemResult.ItemIndex;
                            HandleTrailingSpaces(item, lineInfo);
                            return;
                        }

                        _state = LineBreakState.Trailing;
                        Rewind(newEnd, lineInfo);
                        return;
                    }

                    // Failed to break to fit. Restore to the original state.
                    if (HasHyphen())
                        RemoveHyphen(itemResults);
                    Restore(itemResult, itemResultBefore);
                    SetCurrentStyle(wasCurrentStyle);
                }
            }
        }

        // Reaching here means that the rewind point was not found.

        if (!_overrideBreakAnywhere && hasBreakAnywhereIfOverflow)
        {
            // Overflow occurred but 'overflow-wrap' is set. Change the break type
            // and retry the line breaking.
            _overrideBreakAnywhere = true;
            RetryAfterOverflow(lineInfo, itemResults);
            return;
        }

        // Let this line overflow.
        lineInfo.SetHasOverflow();

        // Restore the hyphenation states to before the loop if needed.
        if (hyphenIndexBefore.HasValue)
            _position += AddHyphen(itemResults, itemResults[hyphenIndexBefore.Value]);

        // If there was a break opportunity, the overflow should stop there.
        if (breakBefore > 0)
        {
            RewindOverflow(breakBefore, lineInfo);
            return;
        }

        if (CanBreakAfterLast(itemResults))
        {
            _state = LineBreakState.Trailing;
            return;
        }

        // No break opportunities. Break at the earliest break opportunity.
        _state = LineBreakState.Overflow;
    }

    private void RetryAfterOverflow(LineInfo lineInfo, List<InlineItemResult> itemResults)
    {
        _state = LineBreakState.Continue;

        // Rewind all items.
        // Also |SetCurrentStyle| forcibly, because the retry uses different
        // conditions such as |override_break_anywhere_|.
        if (itemResults.Count > 0)
        {
            SetCurrentStyleForce(ComputeCurrentStyle(0, lineInfo));
            Rewind(0, lineInfo);
        }
        else
        {
            SetCurrentStyleForce(_currentStyle);
        }
    }

    // Rewind to |new_end| on overflow. If trailable items follow at |new_end|,
    // they are included (not rewound).
    private void RewindOverflow(int newEnd, LineInfo lineInfo)
    {
        var itemResults = lineInfo.Results();
        if (newEnd >= itemResults.Count)
        {
            // All items are trailable. Done without rewinding.
            _trailingWhitespace = WhitespaceState.Unknown;
            _position = lineInfo.ComputeWidth();
            _state = LineBreakState.Done;
            if (IsAtEnd())
                lineInfo.SetIsLastLine(true);
            return;
        }

        int openTagCount = 0;
        for (int index = newEnd; index < itemResults.Count; index++)
        {
            var itemResult = itemResults[index];
            var item = itemResult.Item;
            if (item.Type == InlineItem.InlineItemType.Text)
            {
                // Text items are trailable if they start with trailable spaces.
                if (itemResult.Length == 0)
                {
                    // Empty text items are trailable, see |HandleEmptyText|.
                    continue;
                }
                if (itemResult.ShapeResult != null || (_breakAnywhereIfOverflow && !_overrideBreakAnywhere))
                {
                    var style = item.Style();
                    if (WhiteSpaceStyle.ShouldWrapLine(style) && !WhiteSpaceStyle.ShouldBreakSpaces(style)
                        && itemResult.StartOffset < _text.Length && Character.IsBreakableSpace(_text[itemResult.StartOffset]))
                    {
                        // If all characters are trailable spaces, check the next
                        // item.
                        if (itemResult.ShapeResult != null && IsAllBreakableSpaces(_text, itemResult.StartOffset + 1, itemResult.EndOffset))
                            continue;
                        // If this item starts with spaces followed by non-space
                        // characters, the line should break after the spaces.
                        // Rewind to before this item.
                        _state = LineBreakState.Trailing;
                        Rewind(index, lineInfo);
                        return;
                    }
                }
            }
            else if (item.Type == InlineItem.InlineItemType.Control)
            {
                // All control characters except newline are trailable if auto_wrap.
                var style = item.Style();
                if (WhiteSpaceStyle.ShouldWrapLine(style) && !WhiteSpaceStyle.ShouldBreakSpaces(style))
                    continue;
            }
            else if (item.Type == InlineItem.InlineItemType.OpenTag)
            {
                // Open tags are ambiguous. Count the nest-level and mark where the
                // nest-level was 0.
                if (openTagCount == 0)
                    newEnd = index;
                openTagCount++;
                continue;
            }
            else if (item.Type == InlineItem.InlineItemType.CloseTag)
            {
                if (openTagCount > 0)
                    openTagCount--;
                continue;
            }
            else if (IsTrailableItemType(item.Type))
            {
                continue;
            }

            // Found a non-trailable item. Rewind to before the item, or to before
            // the open tag if the nest-level is not zero.
            if (openTagCount > 0)
                index = newEnd;
            _state = LineBreakState.Done;
            Rewind(index, lineInfo);
            return;
        }

        // The open tag turned out to be non-trailable if the nest-level is not
        // zero. Rewind to before the open tag.
        if (openTagCount > 0)
        {
            _state = LineBreakState.Done;
            Rewind(newEnd, lineInfo);
            return;
        }

        // All items are trailable. Done without rewinding.
        _trailingWhitespace = WhitespaceState.Unknown;
        _position = lineInfo.ComputeWidth();
        _state = LineBreakState.Done;
        if (IsAtEnd())
            lineInfo.SetIsLastLine(true);
    }

    private void Rewind(int newEnd, LineInfo lineInfo)
    {
        var itemResults = lineInfo.MutableResults();

        // Avoid rewinding floats if possible. They will be added back anyway while
        // processing trailing items even when zero available width.
        while (newEnd < itemResults.Count && itemResults[newEnd].Item.Type == InlineItem.InlineItemType.Floating)
        {
            // We assume floats can break after, or this may cause an infinite loop.
            ++newEnd;
            if (newEnd == itemResults.Count)
            {
                _position = lineInfo.ComputeWidth();
                return;
            }
        }

        if (newEnd > 0)
        {
            // Use |results[newEnd - 1].end_offset| because it may have been
            // truncated and may not be equal to |results[newEnd].start_offset|.
            MoveToNextOf(itemResults[newEnd - 1]);
            _trailingWhitespace = WhitespaceState.Unknown;
            // When a space item is followed by empty text, we will break the line
            // at the empty text.
            var items = ItemList;
            while (!IsAtEnd() && items[_currentItemIndex].Type == InlineItem.InlineItemType.Text && items[_currentItemIndex].Length == 0)
            {
                HandleEmptyText(items[_currentItemIndex], lineInfo);
            }
        }
        else
        {
            // Rewinding all items.
            _currentItemIndex = lineInfo.Start().ItemIndex;
            _currentTextOffset = lineInfo.Start().TextOffset;
            _trailingWhitespace = WhitespaceState.Leading;
        }

        int styleIndex = Math.Min(newEnd, Math.Max(0, itemResults.Count - 1));
        SetCurrentStyle(ComputeCurrentStyle(styleIndex, lineInfo));

        itemResults.RemoveRange(newEnd, itemResults.Count - newEnd);

        _hyphenIndex = null;
        if (!_hyphenIndex.HasValue && _hasAnyHyphens)
            RestoreLastHyphen(itemResults);

        _position = lineInfo.ComputeWidth();
    }

    private bool HandleOverflowIfNeeded(LineInfo lineInfo)
    {
        if (_state == LineBreakState.Continue && !CanFitOnLine())
        {
            HandleOverflow(lineInfo);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Line state / helpers.
    // ------------------------------------------------------------------

    private void PrepareNextLine(LineInfo lineInfo)
    {
        lineInfo.Reset();

        if (!(IsFirstPosition()))
        {
            // We're past the first line.
            _previousLineHadForcedBreak = _isForcedBreak;
            _isForcedBreak = false;
            _isFirstFormattedLine = false;
            _useFirstLineStyle = false;
        }

        // The first formatted line carries the ::first-line style for measurement;
        // every later line falls back to the box style (CSS Pseudo-Elements 4 §4).
        if (_firstLineStyle != null)
            _lineStyle = _isFirstFormattedLine ? _firstLineStyle : _baseLineStyle ?? _lineStyle;

        lineInfo.SetStart(new InlineItemTextIndex { ItemIndex = _currentItemIndex, TextOffset = _currentTextOffset });
        lineInfo.SetIsFirstFormattedLine(_isFirstFormattedLine);
        lineInfo.SetLineStyleDirect(_lineStyle);
        lineInfo.SetItemsData(_itemsData);
        lineInfo.SetUseFirstLineStyle(_useFirstLineStyle);
        lineInfo.LeftInset = _lineLeftInset;
        lineInfo.AvailableInlineSize = _availableWidth;

        if (IsFinished())
            return;

        _overrideBreakAnywhere = false;
        _hyphenIndex = null;
        _hasAnyHyphens = false;
        SetCurrentStyle(_lineStyle);

        // Use 'text-indent' as the initial position. This lets tab positions to
        // align regardless of 'text-indent'.
        // A percentage 'text-indent' resolves against the containing block's
        // inline size, which is only known here (CSS Text 3 §5.2). Floats shorten
        // individual line boxes but not the containing block, hence the base width.
        float indentLength = _lineStyle.TextIndent + _lineStyle.TextIndentPercent * _inlineBaseWidth;
        float textIndent = _lineStyle.TextIndentHanging
            ? (_isFirstFormattedLine ? 0 : indentLength)
            : (_isFirstFormattedLine ? indentLength : 0);
        _appliedTextIndent = textIndent;
        lineInfo.SetTextIndent(textIndent);
        _position += textIndent;

        lineInfo.SetBaseDirection(_itemsData.BaseDirection);
    }

    private InlineItemResult AddItem(InlineItem item, LineInfo lineInfo)
    {
        return AddItem(item, item.EndOffset, lineInfo);
    }

    private InlineItemResult AddItem(InlineItem item, int endOffset, LineInfo lineInfo)
    {
        var itemResult = new InlineItemResult(item, _currentItemIndex)
        {
            StartOffset = _currentTextOffset,
            EndOffset = endOffset,
            BreakAnywhereIfOverflow = _breakAnywhereIfOverflow,
            ShouldCreateLineBox = ShouldCreateLineBox(item, lineInfo),
            HasUnpositionedFloats = false,
        };
        lineInfo.MutableResults().Add(itemResult);
        return itemResult;
    }

    private InlineItemResult AddEmptyItem(InlineItem item, LineInfo lineInfo)
    {
        var itemResult = AddItem(item, _currentTextOffset, lineInfo);

        // Prevent breaking before an empty item, but allow to break after if the
        // previous item had |can_break_after|.
        var itemResults = lineInfo.Results();
        if (itemResults.Count >= 2)
        {
            var lastItemResult = itemResults[itemResults.Count - 2];
            if (lastItemResult.CanBreakAfter)
            {
                lastItemResult.CanBreakAfter = false;
                itemResult.CanBreakAfter = true;
            }
        }
        return itemResult;
    }

    private void RemoveLastItem(LineInfo lineInfo)
    {
        var itemResults = lineInfo.MutableResults();
        if (itemResults.Count == 0) return;
        _position -= itemResults[itemResults.Count - 1].InlineSize;
        itemResults.RemoveAt(itemResults.Count - 1);
    }

    private bool ShouldCreateLineBox(InlineItem item, LineInfo lineInfo)
    {
        if (item.Type == InlineItem.InlineItemType.Control && _currentTextOffset < _text.Length
            && (_text[_currentTextOffset] == Character.kTabulationCharacter || _text[_currentTextOffset] == Character.kCarriageReturnCharacter))
            return true;
        if (item.Type == InlineItem.InlineItemType.BlockInInline)
            return false;
        if (!lineInfo.IsEmptyLine())
            return true;
        if (item.Type == InlineItem.InlineItemType.Text)
        {
            var style = item.Style();
            if (WhiteSpaceStyle.ShouldCollapseWhiteSpaces(style) && _currentTextOffset < _text.Length && _text[_currentTextOffset] == Character.kSpaceCharacter)
                return false;
            return true;
        }
        return lineInfo.IsFirstFormattedLine() || item.Type == InlineItem.InlineItemType.ListMarker;
    }

    private void MoveToNextOf(InlineItem item)
    {
        _currentTextOffset = item.EndOffset;
        _currentItemIndex++;
    }

    private void MoveToNextOf(InlineItemResult itemResult)
    {
        // Mirror LineBreaker::MoveToNextOf(const InlineItemResult&): reset the
        // position to this result's end (BOTH item index and text offset), then
        // step past the item if the offset reached its end. Previously this only
        // incremented the *current* index and, worse, reset _state to Continue —
        // which clobbered the kDone state that RewindOverflow sets before calling
        // Rewind. The line then never terminated: the index crept forward while
        // the text offset stayed frozen, dropping every word after the first
        // wrapped line.
        _currentTextOffset = itemResult.EndOffset;
        _currentItemIndex = itemResult.ItemIndex;
        if (_currentTextOffset == itemResult.Item.EndOffset)
            _currentItemIndex++;
    }

    private bool IsPreviousItemOfType(InlineItem.InlineItemType type)
    {
        return _currentItemIndex > 0 && ItemList[_currentItemIndex - 1].Type == type;
    }

    private static bool IsTrailableItemType(InlineItem.InlineItemType type)
    {
        switch (type)
        {
            case InlineItem.InlineItemType.Text:
            case InlineItem.InlineItemType.Control:
            case InlineItem.InlineItemType.Floating:
            case InlineItem.InlineItemType.OutOfFlowPositioned:
                return true;
            default:
                return false;
        }
    }

    private static bool IsAllBreakableSpaces(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!(Character.IsBreakableSpace(text[i]) || Character.IsOtherSpaceSeparator(text[i])))
                return false;
        }
        return true;
    }

    private bool CanBreakAfterLast(List<InlineItemResult> itemResults)
    {
        for (int i = itemResults.Count; i > 0;)
        {
            var itemResult = itemResults[--i];
            if (itemResult.CanBreakAfter)
                return true;
            var item = itemResult.Item;
            if (item.Type == InlineItem.InlineItemType.Text)
            {
                return itemResult.MayBreakInside && !itemResult.HasOnlyBidiTrailingSpaces;
            }
            if (item.Type == InlineItem.InlineItemType.AtomicInline)
                return false;
        }
        return false;
    }

    private static void ComputeCanBreakAfter(InlineItemResult itemResult, bool autoWrap, LazyLineBreakIterator breakIterator)
    {
        if (!autoWrap) return;
        if (itemResult.Item.Type == InlineItem.InlineItemType.AtomicInline)
        {
            itemResult.CanBreakAfter = true;
            return;
        }
        itemResult.CanBreakAfter = breakIterator.IsBreakable(itemResult.EndOffset);
    }

    private bool CanBreakAfter(InlineItem item)
    {
        if (item.Type == InlineItem.InlineItemType.AtomicInline)
            return CanBreakAfterAtomicInline(item);
        if (!_autoWrap) return false;
        bool canBreakAfter = _breakIterator.IsBreakable(item.EndOffset);
        if (item.Type != InlineItem.InlineItemType.Text)
            return canBreakAfter;
        var atomicInlineItem = TryGetAtomicInlineItemAfter(item);
        if (atomicInlineItem == null)
            return canBreakAfter;
        return true;
    }

    private bool CanBreakAfterAtomicInline(InlineItem item)
    {
        if (!_autoWrap) return false;
        if (item.EndOffset >= _text.Length) return true;
        return true;
    }

    private InlineItem? TryGetAtomicInlineItemAfter(InlineItem item)
    {
        if (!_autoWrap) return null;
        if (item.EndOffset >= _text.Length) return null;
        if (_text[item.EndOffset] != Character.kObjectReplacementCharacter) return null;
        var items = ItemList;
        for (int i = _currentItemIndex + 1; i < items.Count; i++)
        {
            var next = items[i];
            if (next.Type == InlineItem.InlineItemType.AtomicInline)
                return next;
            if (next.EndOffset > item.EndOffset)
                return null;
        }
        return null;
    }

    private ComputedStyle ComputeCurrentStyle(int itemResultIndex, LineInfo lineInfo)
    {
        var itemResults = lineInfo.Results();
        if (itemResults.Count == 0) return _lineStyle;
        itemResultIndex = Math.Clamp(itemResultIndex, 0, itemResults.Count - 1);

        var item = itemResults[itemResultIndex].Item;
        if (item.Type == InlineItem.InlineItemType.Text || item.Type == InlineItem.InlineItemType.CloseTag)
            return item.Style();

        while (itemResultIndex > 0)
        {
            item = itemResults[--itemResultIndex].Item;
            if (item.Type == InlineItem.InlineItemType.Text || item.Type == InlineItem.InlineItemType.OpenTag)
                return item.Style();
            if (item.Type == InlineItem.InlineItemType.CloseTag)
                return item.Style();
        }
        return _lineStyle;
    }

    private void SetCurrentStyle(ComputedStyle style)
    {
        if (ReferenceEquals(style, _currentStyle))
            return;
        SetCurrentStyleForce(style);
    }

    private void SetCurrentStyleForce(ComputedStyle style)
    {
        _currentStyle = style;
        _autoWrap = ShouldAutoWrap(style);
        if (!_autoWrap)
            return;

        var cssLineBreak = style.LineBreak;
        if (cssLineBreak == CssLineBreakType.Anywhere)
        {
            _breakIterator.SetStrictness(LineBreakStrictness.kDefault);
            _breakIterator.SetBreakType(LineBreakType.kBreakCharacter);
            _breakAnywhereIfOverflow = false;
        }
        else
        {
            _breakIterator.SetStrictness(StrictnessFromLineBreak(cssLineBreak));
            var lineBreakType = LineBreakType.kNormal;
            switch (style.WordBreak)
            {
                case WordBreakMode.Normal:
                    lineBreakType = LineBreakType.kNormal;
                    _breakAnywhereIfOverflow = false;
                    break;
                case WordBreakMode.BreakAll:
                    lineBreakType = LineBreakType.kBreakAll;
                    _breakAnywhereIfOverflow = false;
                    break;
                case WordBreakMode.BreakWord:
                    lineBreakType = LineBreakType.kNormal;
                    _breakAnywhereIfOverflow = true;
                    break;
            }
            if (!_breakAnywhereIfOverflow)
            {
                // 'overflow-wrap: anywhere' affects both layout and min-content,
                // while 'break-word' affects layout but not min-content.
                var overflowWrap = style.OverflowWrap;
                _breakAnywhereIfOverflow = overflowWrap == OverflowWrapMode.Anywhere
                    || (overflowWrap == OverflowWrapMode.BreakWord && _mode == LineBreakerMode.Content);
            }
            if (_breakAnywhereIfOverflow)
            {
                if (_overrideBreakAnywhere)
                    lineBreakType = LineBreakType.kBreakCharacter;
                else if (_mode == LineBreakerMode.MinContent)
                {
                    _overrideBreakAnywhere = true;
                    lineBreakType = LineBreakType.kBreakCharacter;
                }
            }
            _breakIterator.SetBreakType(lineBreakType);
        }

        var hyphens = style.Hyphens;
        if (hyphens == HyphensType.None)
        {
            _breakIterator.EnableSoftHyphen(false);
            _hyphenation = null;
        }
        else
        {
            _breakIterator.EnableSoftHyphen(true);
            _hyphenation = Hyphenation.None;
        }

        if (WhiteSpaceStyle.ShouldBreakSpaces(style))
            _breakIterator.SetBreakSpace(BreakSpaceType.kAfterEverySpace);
        else
            _breakIterator.SetBreakSpace(BreakSpaceType.kAfterSpaceRun);
    }

    private static LineBreakStrictness StrictnessFromLineBreak(CssLineBreakType lineBreak)
    {
        switch (lineBreak)
        {
            case CssLineBreakType.Normal:
                return LineBreakStrictness.kNormal;
            case CssLineBreakType.Strict:
                return LineBreakStrictness.kStrict;
            default:
                // Auto / Loose.
                return LineBreakStrictness.kDefault;
        }
    }

    private bool ShouldAutoWrap(ComputedStyle style)
    {
        return !_disallowAutoWrap && WhiteSpaceStyle.ShouldWrapLine(style);
    }

    private bool IsFirstPosition()
    {
        return _currentItemIndex == 0 && _currentTextOffset == 0;
    }

    private bool IsAtEnd()
    {
        return _currentItemIndex >= ItemList.Count;
    }

    private bool IsFinished()
    {
        return IsAtEnd();
    }

    private bool IsEmptyInline()
    {
        return _position == 0;
    }

    private float AvailableWidthToFit()
    {
        return _availableWidth + 0.0001f;
    }

    private float RemainingAvailableWidth()
    {
        return AvailableWidthToFit() - _position;
    }

    private bool CanFitOnLine()
    {
        return _position <= AvailableWidthToFit();
    }

    private ConstraintSpace LineConstraintSpace()
    {
        return new ConstraintSpace(availableInlineSize: _availableWidth);
    }

    private InlineItemsData ItemsData() => _itemsData!;
    private List<InlineItem> ItemList => _itemsData!.Items;

    private static ComputedStyle ComputeInitialLineStyle(InlineItemsData data)
    {
        foreach (var item in data.Items)
        {
            if (item.Type == InlineItem.InlineItemType.OpenTag)
            {
                var style = item.StyleOverride ?? item.Element?.ComputedStyle;
                if (style != null) return style;
            }
        }
        return new ComputedStyle();
    }

    // ------------------------------------------------------------------
    // Hyphenation helpers.
    // ------------------------------------------------------------------

    private Hyphenation? _hyphenation;

    private bool HasHyphen() => _hyphenIndex.HasValue;

    private float AddHyphen(List<InlineItemResult> itemResults, InlineItemResult itemResult)
    {
        int index = itemResults.IndexOf(itemResult);
        if (index < 0 || index >= itemResults.Count)
            return 0;
        if (_hyphenIndex.HasValue)
            return 0;
        _hyphenIndex = index;
        if (itemResult.Hyphen == null)
        {
            itemResult.ShapeHyphen();
            _hasAnyHyphens = true;
        }
        if (itemResult.Hyphen == null)
            return 0;
        float hyphenInlineSize = itemResult.Hyphen.InlineSize();
        itemResult.InlineSize += hyphenInlineSize;
        return hyphenInlineSize;
    }

    private float RemoveHyphen(List<InlineItemResult> itemResults)
    {
        if (!_hyphenIndex.HasValue) return 0;
        var itemResult = itemResults[_hyphenIndex.Value];
        if (itemResult.Hyphen == null)
        {
            _hyphenIndex = null;
            return 0;
        }
        float hyphenInlineSize = itemResult.Hyphen.InlineSize();
        itemResult.InlineSize -= hyphenInlineSize;
        _hyphenIndex = null;
        return hyphenInlineSize;
    }

    private void RestoreLastHyphen(List<InlineItemResult> itemResults)
    {
        if (_hyphenIndex.HasValue || !_hasAnyHyphens) return;
        for (int i = itemResults.Count - 1; i >= 0; i--)
        {
            var itemResult = itemResults[i];
            if (itemResult.Hyphen != null)
            {
                AddHyphen(itemResults, itemResult);
                return;
            }
            var item = itemResult.Item;
            if (item.Type == InlineItem.InlineItemType.Text || item.Type == InlineItem.InlineItemType.AtomicInline)
                return;
        }
    }

    private void FinalizeHyphen(LineInfo lineInfo)
    {
        if (!_hyphenIndex.HasValue) return;
        lineInfo.MutableResults()[_hyphenIndex.Value].IsHyphenated = true;
    }

    // ------------------------------------------------------------------
    // Misc.
    // ------------------------------------------------------------------

    private static InlineItemResult Snapshot(InlineItemResult source)
    {
        var copy = new InlineItemResult(source.Item, source.ItemIndex)
        {
            StartOffset = source.StartOffset,
            EndOffset = source.EndOffset,
            InlineSize = source.InlineSize,
            BlockSize = source.BlockSize,
            BaselineOffset = source.BaselineOffset,
            CanBreakAfter = source.CanBreakAfter,
            MayBreakInside = source.MayBreakInside,
            BreakAnywhereIfOverflow = source.BreakAnywhereIfOverflow,
            ShouldCreateLineBox = source.ShouldCreateLineBox,
            HasUnpositionedFloats = source.HasUnpositionedFloats,
            HasOnlyPreWrapTrailingSpaces = source.HasOnlyPreWrapTrailingSpaces,
            HasOnlyBidiTrailingSpaces = source.HasOnlyBidiTrailingSpaces,
            IsHyphenated = source.IsHyphenated,
            Hyphen = source.Hyphen,
            Margins = source.Margins,
            Borders = source.Borders,
            Padding = source.Padding,
            SpacingBefore = source.SpacingBefore,
            LayoutResult = source.LayoutResult,
            TextContent = source.TextContent,
            ShapeResult = source.ShapeResult,
            PositionedFloat = source.PositionedFloat,
        };
        return copy;
    }

    private static void Restore(InlineItemResult target, InlineItemResult source)
    {
        target.StartOffset = source.StartOffset;
        target.EndOffset = source.EndOffset;
        target.InlineSize = source.InlineSize;
        target.BlockSize = source.BlockSize;
        target.BaselineOffset = source.BaselineOffset;
        target.CanBreakAfter = source.CanBreakAfter;
        target.MayBreakInside = source.MayBreakInside;
        target.BreakAnywhereIfOverflow = source.BreakAnywhereIfOverflow;
        target.ShouldCreateLineBox = source.ShouldCreateLineBox;
        target.HasUnpositionedFloats = source.HasUnpositionedFloats;
        target.HasOnlyPreWrapTrailingSpaces = source.HasOnlyPreWrapTrailingSpaces;
        target.HasOnlyBidiTrailingSpaces = source.HasOnlyBidiTrailingSpaces;
        target.IsHyphenated = source.IsHyphenated;
        target.Hyphen = source.Hyphen;
        target.Margins = source.Margins;
        target.Borders = source.Borders;
        target.Padding = source.Padding;
        target.SpacingBefore = source.SpacingBefore;
        target.LayoutResult = source.LayoutResult;
        target.TextContent = source.TextContent;
        target.ShapeResult = source.ShapeResult;
        target.PositionedFloat = source.PositionedFloat;
    }

    private void SetResultTextContents(LineInfo lineInfo)
    {
        var results = lineInfo.MutableResults();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (r.Item.Type != InlineItem.InlineItemType.Text) continue;
            if (r.Length <= 0) continue;
            r.TextContent = _text.Substring(r.StartOffset, r.Length);
        }
    }
}

/// <summary>
/// The float machinery of the formatting context a run of inline content flows
/// in. Line boxes are the only things that avoid floats (CSS 2.1 §9.5.2), so the
/// line breaker needs the exclusion space plus the geometry that maps a line's
/// vertical position into that space's coordinates.
/// </summary>
public sealed class FloatLineContext
{
    /// <summary>Floats of the enclosing formatting context (shared, mutable).</summary>
    public required ExclusionSpace Space;

    /// <summary>Line offset of the content box start, in formatting-context coordinates.</summary>
    public required float ContentLineStart;

    /// <summary>Block offset of the content box top, in formatting-context coordinates.</summary>
    public required float ContentTop;

    /// <summary>Content-box inline size of the block whose text is being broken.</summary>
    public required float ContentWidth;

    /// <summary>Height assumed for a line box when testing it against the floats.</summary>
    public float LineBlockSize;

    /// <summary>Inline range available to a line box starting at |blockOffset|
    /// (formatting-context coordinates), as content-box-relative inset and width,
    /// plus the content-relative block offset to restart at when no room is left.</summary>
    public (float LeftInset, float Width, float PushDownTo) LineSpaceAt(float blockOffset, float blockSize)
    {
        var range = Space.LineSpaceAt(blockOffset, blockSize, ContentLineStart, ContentLineStart + ContentWidth);
        float pushDown = float.IsNaN(range.PushDownTo) ? float.NaN : range.PushDownTo - ContentTop;
        return (range.LineStart - ContentLineStart, range.LineEnd - range.LineStart, pushDown);
    }
}

/// <summary>A float positioned while breaking lines. The owning algorithm adopts
/// the layout result as a child fragment of the block.</summary>
public readonly record struct PlacedFloat(Element Element, LayoutResult Result);
