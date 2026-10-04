using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Inline;

public enum FontBaselineType
{
    Alphabetic,
    Ideographic,
}

/// <summary>
/// Represents an item in a line, after line break, but still mutable and in
/// the logical coordinate system. Mirrors logical_line_item.h.
/// </summary>
public class LogicalLineItem
{
    public InlineItem? InlineItem { get; set; }
    public ShapeResult? ShapeResult { get; set; }
    public TextOffsetRange TextOffset { get; set; }
    public string TextContent { get; set; } = "";

    public LayoutResult? LayoutResult { get; set; }

    public LayoutObject? LayoutObject { get; set; }
    public StyleVariant StyleVariant { get; set; } = StyleVariant.Standard;

    public LayoutObject? OutOfFlowPositionedBox { get; set; }
    public LayoutObject? UnpositionedFloat { get; set; }
    public InlineItemTextIndex ItemIndex { get; set; } = new();

    public LogicalRect Rect { get; set; }
    public BfcOffset BfcOffset { get; set; }

    /// <summary>
    /// Inline advance of this child. Backed by <see cref="Rect"/> so that the
    /// size set by <c>AddChild</c> and the size read while placing runs are the
    /// same value, exactly as LogicalLineItem::inline_size aliases
    /// rect.size.inline_size in the source. Keeping these as two independent
    /// fields is what previously left every run at size 0.
    /// </summary>
    public float InlineSize
    {
        get => Rect.InlineSize;
        set => Rect = new LogicalRect(Rect.InlineStart, Rect.BlockStart, value, Rect.BlockSize);
    }

    /// <summary>
    /// The child's inline offset before <see cref="InlineLayoutStateStack.ComputeInlinePositions"/>
    /// shifted it into place. Saved so that inline box borders/padding can be
    /// applied relative to the leaf child later. Mirrors
    /// LogicalLineItem::margin_line_left.
    /// </summary>
    public float MarginLineLeft { get; set; }
    public int BoxDataIndex { get; set; } = -1;
    public int ChildrenCount { get; set; }
    public TextDirection BidiLevelDirection { get; set; } = TextDirection.Ltr;
    public bool HasBidiLevel { get; set; }
    public int BidiLevel { get; set; }
    public TextDirection ContainerDirection { get; set; } = TextDirection.Ltr;
    public bool HasOnlyBidiTrailingSpaces { get; set; }

    public bool IsHiddenForPaint { get; set; }
    public bool HasOverAnnotation { get; set; }
    public bool HasUnderAnnotation { get; set; }

    public LogicalLineItem() { }

    public LogicalLineItem(float blockOffset, float blockSize)
    {
        Rect = new LogicalRect(0, blockOffset, 0, blockSize);
    }

    public bool IsFloating => LayoutResult?.IsFloating ?? false;
    public bool IsInlineBox => LayoutResult?.IsInlineBox ?? false;
    public bool HasInFlowFragment() => (InlineItem != null && InlineItem.Type != InlineItem.InlineItemType.RubyLinePlaceholder) || LayoutResult != null || LayoutObject != null;
    public bool HasOutOfFlowFragment() => OutOfFlowPositionedBox != null;
    public bool HasFragment() => HasInFlowFragment() || HasOutOfFlowFragment();
    public bool HasInFlowOrFloatingFragment => HasInFlowFragment() || IsFloating;
    public bool IsControl => InlineItem?.Type == InlineItem.InlineItemType.Control;
    public bool IsRubyLinePlaceholder => InlineItem?.Type == InlineItem.InlineItemType.RubyLinePlaceholder;
    /// <summary>
    /// A fragment-less child that only carries an inline box's start or end
    /// margin/border/padding, so that the pen advances past decoration the box
    /// occupies but paints nowhere. Mirrors the inline offset that the source
    /// keeps in <c>BoxData::margin_border_padding_line_left/right</c>.
    /// </summary>
    public bool IsBoxDecorationSpacer { get; set; }
    public bool CanCreateFragmentItem => HasInFlowFragment();
    public bool IsPlaceholder => !HasFragment() && !HasBidiLevel;
    public bool IsOpaqueToBidiReordering => IsPlaceholder;
    public bool HasOnlyTrailingSpaces => HasOnlyBidiTrailingSpaces;

    public float InlineOffset => Rect.InlineStart;
    public float BlockOffset => Rect.BlockStart;
    public float BlockEndOffset => Rect.BlockEnd;
    public LogicalSize Size => Rect.Size;
    public float TextHeight => Rect.BlockSize;

    public int StartOffset => TextOffset.Start;
    public int EndOffset => TextOffset.End;

    public TextDirection ResolvedDirection => HasBidiLevel ? BidiLevelDirection : TextDirection.Ltr;

    public void MoveInInlineDirection(float delta) => Rect = new LogicalRect(Rect.InlineStart + delta, Rect.BlockStart, Rect.InlineSize, Rect.BlockSize);
    public void MoveInBlockDirection(float delta) => Rect = new LogicalRect(Rect.InlineStart, Rect.BlockStart + delta, Rect.InlineSize, Rect.BlockSize);

    public override string ToString() =>
        $"'{TextContent}' [{Rect.InlineStart:F1},{Rect.BlockStart:F1} {Rect.InlineSize:F1}x{Rect.BlockSize:F1}]";
}

/// <summary>
/// A vector of LogicalLineItem children. Unlike the fragment builder, children
/// are mutable. Mirrors LogicalLineItems in logical_line_item.h.
/// </summary>
public class LogicalLineItems
{
    private readonly List<LogicalLineItem> _children = new();
    public bool WasPropagated { get; set; }

    public int Count => _children.Count;
    public int Size() => _children.Count;
    public bool IsEmpty => _children.Count == 0;

    public LogicalLineItem this[int i]
    {
        get => _children[i];
        set => _children[i] = value;
    }

    public IEnumerator<LogicalLineItem> GetEnumerator() => ((IEnumerable<LogicalLineItem>)_children).GetEnumerator();

    public void AddChild(LogicalLineItem item) => _children.Add(item);

    public void AddChild(int bidiLevel)
    {
        _children.Add(new LogicalLineItem { BidiLevel = bidiLevel, BidiLevelDirection = FromLevel(bidiLevel), HasBidiLevel = true });
    }

    /// <summary>
    /// Reserve <paramref name="inlineSize"/> of inline space for an inline box's
    /// own margin/border/padding without creating a fragment for it. The line
    /// breaker already counted this space, so placement has to as well, or the
    /// decoration would be painted over the neighbouring text.
    /// </summary>
    public void AddBoxDecorationSpacer(float inlineSize, int bidiLevel)
    {
        _children.Add(new LogicalLineItem
        {
            Rect = new LogicalRect(0, 0, inlineSize, 0),
            BidiLevel = bidiLevel,
            BidiLevelDirection = FromLevel(bidiLevel),
            IsBoxDecorationSpacer = true,
        });
    }

    public void AddChild(LayoutObject layoutObject, int bidiLevel, TextDirection direction)
    {
        _children.Add(new LogicalLineItem { LayoutObject = layoutObject, BidiLevel = bidiLevel, BidiLevelDirection = direction, HasBidiLevel = true });
    }

    public void AddChild(LayoutObject layoutObject, int bidiLevel, InlineItemTextIndex itemIndex)
    {
        _children.Add(new LogicalLineItem
        {
            LayoutObject = layoutObject,
            BidiLevel = bidiLevel,
            BidiLevelDirection = FromLevel(bidiLevel),
            HasBidiLevel = true,
            ItemIndex = itemIndex,
        });
    }

    public void AddChild(InlineItem item, TextOffsetRange textOffset, float blockOffset, float inlineSize, float textHeight, int bidiLevel, ShapeResult? shapeResult = null, string? textContent = null)
    {
        _children.Add(new LogicalLineItem
        {
            InlineItem = item,
            TextOffset = textOffset,
            ShapeResult = shapeResult,
            TextContent = textContent ?? "",
            InlineSize = inlineSize,
            Rect = new LogicalRect(0, blockOffset, inlineSize, textHeight),
            BidiLevel = bidiLevel,
            BidiLevelDirection = FromLevel(bidiLevel),
            HasBidiLevel = true,
        });
    }

    public void AddChild(InlineItem item, InlineItemResult itemResult, TextOffsetRange textOffset, float blockOffset, float inlineSize, float textHeight, int bidiLevel)
    {
        AddChild(item, textOffset, blockOffset, inlineSize, textHeight, bidiLevel, itemResult.ShapeResult, itemResult.TextContent);
    }

    public void AddChild(InlineItem item, ShapeResult? shapeResult, TextOffsetRange textOffset, string textContent,
        float blockOffset, float inlineSize, float textHeight, int bidiLevel)
    {
        AddChild(item, textOffset, blockOffset, inlineSize, textHeight, bidiLevel, shapeResult, textContent);
    }

    public void AddChild(LayoutResult layoutResult, LogicalOffset offset, float inlineSize, int childrenCount, int bidiLevel)
    {
        _children.Add(new LogicalLineItem
        {
            LayoutResult = layoutResult,
            InlineSize = inlineSize,
            Rect = new LogicalRect(offset.InlineOffset, offset.BlockOffset, inlineSize, layoutResult.Fragment?.BlockSize ?? 0),
            ChildrenCount = childrenCount,
            BidiLevel = bidiLevel,
            BidiLevelDirection = FromLevel(bidiLevel),
            HasBidiLevel = true,
        });
    }

    public void AddChild(LayoutResult layoutResult, BfcOffset bfcOffset, int bidiLevel)
    {
        _children.Add(new LogicalLineItem
        {
            LayoutResult = layoutResult,
            BfcOffset = bfcOffset,
            InlineSize = bfcOffset.LineOffset,
            BidiLevel = bidiLevel,
            BidiLevelDirection = FromLevel(bidiLevel),
            HasBidiLevel = true,
        });
    }

    public void AddChild(LayoutResult layoutResult, LogicalOffset offset, float inlineSize, float blockSize, int bidiLevel)
    {
        _children.Add(new LogicalLineItem
        {
            LayoutResult = layoutResult,
            InlineSize = inlineSize,
            Rect = new LogicalRect(offset.InlineOffset, offset.BlockOffset, inlineSize, blockSize),
            BidiLevel = bidiLevel,
            BidiLevelDirection = FromLevel(bidiLevel),
            HasBidiLevel = true,
        });
    }

    private static TextDirection FromLevel(int bidiLevel) => bidiLevel % 2 == 1 ? TextDirection.Rtl : TextDirection.Ltr;

    public void InsertChild(int index, LogicalLineItem item)
    {
        WillInsertChild(index);
        _children.Insert(index, item);
    }

    public void Clear() => _children.Clear();
    public void Shrink(int size) => _children.RemoveRange(size, _children.Count - size);
    public void ReserveInitialCapacity(int capacity) { }
    public void SetPropagated() => WasPropagated = true;

    public void AppendRange(int fromIndex, int toIndex, LogicalLineItems source)
    {
        for (int i = fromIndex; i < toIndex && i < source.Count; i++)
            _children.Add(source[i]);
    }

    public void MoveInInlineDirection(float delta)
    {
        foreach (var child in _children)
            child.MoveInInlineDirection(delta);
    }
    public void MoveInInlineDirection(float delta, int start, int end)
    {
        for (int i = start; i < end && i < _children.Count; i++)
            _children[i].MoveInInlineDirection(delta);
    }
    public void MoveInBlockDirection(float delta)
    {
        foreach (var child in _children)
            child.MoveInBlockDirection(delta);
    }
    public void MoveInBlockDirection(float delta, int start, int end)
    {
        for (int i = start; i < end && i < _children.Count; i++)
            _children[i].MoveInBlockDirection(delta);
    }

    public LogicalLineItem? FirstInFlowChild()
    {
        foreach (var child in _children)
            if (child.HasInFlowFragment())
                return child;
        return null;
    }
    public LogicalLineItem? LastInFlowChild()
    {
        for (int i = _children.Count - 1; i >= 0; i--)
            if (_children[i].HasInFlowFragment())
                return _children[i];
        return null;
    }

    public LayoutResult? BlockInInlineLayoutResult()
    {
        foreach (var child in _children)
            if (child.LayoutResult?.IsBlockInInline ?? false)
                return child.LayoutResult;
        return null;
    }

    private void WillInsertChild(int index) { }
}

/// <summary>
/// Range of text offsets for an inline item. Mirrors text_offset_range.h.
/// </summary>
public readonly struct TextOffsetRange
{
    public int Start { get; }
    public int End { get; }
    public TextOffsetRange(int start, int end) { Start = start; End = end; }
    public int Length => End - Start;
    public bool IsEmpty => Start == End;
    public bool IsValid => Start >= 0 && End >= Start;
    public void AssertNotEmpty() { if (Start == End) throw new InvalidOperationException("empty TextOffsetRange"); }
    public void AssertValid() { if (!IsValid) throw new InvalidOperationException("invalid TextOffsetRange"); }
    public override string ToString() => $"{Start}-{End}";
}

/// <summary>
/// Represents the current box while InlineLayoutAlgorithm performs layout.
/// Mirrors inline_box_state.h.
/// </summary>
public class InlineBoxState
{
    public int FragmentStart { get; set; }
    public InlineItem? Item { get; set; }
    public ComputedStyle? Style { get; set; }
    public FontHeight Metrics { get; set; } = FontHeight.Empty;
    public FontHeight TextMetrics { get; set; } = FontHeight.Empty;
    public float TextTop { get; set; }
    public float TextHeight { get; set; }
    public bool HasStartEdge { get; set; }
    public bool HasEndEdge { get; set; }
    public BoxStrut Margins { get; set; } = BoxStrut.Zero;
    public BoxStrut Borders { get; set; } = BoxStrut.Zero;
    public BoxStrut Padding { get; set; } = BoxStrut.Zero;
    public List<PendingPositions> PendingDescendants { get; } = new();
    public bool IncludeUsedFonts { get; set; }
    public bool HasBoxPlaceholder { get; set; }
    public bool NeedsBoxFragment { get; set; }

    public bool HasMetrics => !Metrics.IsEmpty || PendingDescendants.Count > 0;

    public void ResetStyle(ComputedStyle style)
    {
        Style = style;
        Metrics = FontHeight.Empty;
        TextMetrics = FontHeight.Empty;
        TextTop = 0;
        TextHeight = 0;
        PendingDescendants.Clear();
    }

    public void SetStyle(ComputedStyle style, bool isLineBox = false)
    {
        ResetStyle(style);
        HasStartEdge = true;
        HasEndEdge = true;
        if (isLineBox)
        {
            IncludeUsedFonts = true;
        }
    }

    public void ComputeTextMetrics(ComputedStyle style, FontHeightMetrics font, FontBaselineType baselineType)
    {
        TextMetrics = font.Metrics;
        TextTop = TextMetrics.Ascent;
        TextHeight = TextMetrics.LineHeight;
        if (Metrics.IsEmpty)
            Metrics = TextMetrics;
    }

    public void EnsureTextMetrics(ComputedStyle style, FontHeightMetrics font, FontBaselineType baselineType)
    {
        if (TextMetrics.IsEmpty)
            ComputeTextMetrics(style, font, baselineType);
    }

    public void AccumulateUsedFonts(ShapeResult shapeResult) { }

    public void Unite(FontHeight metrics) => Metrics = Metrics.Union(metrics);

    public float TextTopOffset(TextDirection direction) => TextTop;
}

/// <summary>
/// Fragments that require the layout position/size of ancestor.
/// Mirrors PendingPositions in inline_box_state.h.
/// </summary>
public struct PendingPositions
{
    public int FragmentStart;
    public int FragmentEnd;
    public FontHeight Metrics;
    public VerticalAlign VerticalAlign;
}

public enum VerticalAlign
{
    Baseline, Sub, Super, TextTop, TextBottom, Middle, Top, Bottom, Length
}

public enum RubyPosition
{
    kOver,
    kUnder,
}

public class LogicalRubyColumn
{
    public int StartIndex;
    public int Size;
    public float BaseInsetStart;
    public float BaseInsetEnd;
    public FontHeight Metrics;
    public RubyPosition RubyPosition;
    public InlineLayoutStateStack StateStack = new();
    public LogicalLineItems? AnnotationItems;
}

/// <summary>
/// Represents the inline tree structure. Mirrors InlineLayoutStateStack in
/// inline_box_state.h.
/// </summary>
public class InlineLayoutStateStack
{
    private readonly List<InlineBoxState> _stack = new();
    private readonly List<LogicalRubyColumn> _rubyColumns = new();
    public bool IsEmptyLine { get; set; }

    public InlineBoxState LineBoxState => _stack.Count > 0 ? _stack[0] : throw new InvalidOperationException("empty stack");

    public bool HasBoxFragments => false;

    public List<LogicalRubyColumn> RubyColumnList() => _rubyColumns;

    public void ClearRubyColumnList() => _rubyColumns.Clear();

    public LogicalRubyColumn CreateRubyColumn()
    {
        var column = new LogicalRubyColumn();
        _rubyColumns.Add(column);
        return column;
    }

    public LogicalRubyColumn RubyColumnAt(int index) => _rubyColumns[index];

    /// <summary>Number of boxes in the state (C++ box_data_list size).</summary>
    public int BoxCount => 0;

    /// <summary>Mirrors OnBeginPlaceItems(). Clears the stack for a new line.</summary>
    public InlineBoxState OnBeginPlaceItems(InlineNode node, ComputedStyle style, FontBaselineType baselineType, bool quirksMode, LogicalLineItems lineBox)
    {
        lineBox.Clear();
        _stack.Clear();
        lineBox.WasPropagated = false;
        var state = new InlineBoxState();
        _stack.Add(state);
        state.SetStyle(style, /* is_line_box */ true);
        return state;
    }

    public void SetIsEmptyLine(bool isEmptyLine) => IsEmptyLine = isEmptyLine;

    public InlineBoxState? OnOpenTag(ConstraintSpace space, InlineItem item, InlineItemResult itemResult, FontBaselineType baselineType, LogicalLineItems lineBox)
    {
        var state = new InlineBoxState();
        _stack.Add(state);
        state.Item = item;
        state.Style = item.Style();
        state.Margins = itemResult.Margins;
        state.Borders = itemResult.Borders;
        state.Padding = itemResult.Padding;
        state.HasStartEdge = true;
        state.HasEndEdge = true;
        state.NeedsBoxFragment = item.ShouldCreateBoxFragment();
        state.FragmentStart = lineBox.Count;
        return state;
    }

    public InlineBoxState? OnCloseTag(ConstraintSpace space, LogicalLineItems lineBox, InlineBoxState? boxState, FontBaselineType baselineType)
    {
        if (boxState == null || _stack.Count <= 1)
            return null;

        // Find the index of the box being closed.
        int index = _stack.Count - 1;
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stack[i], boxState))
            {
                index = i;
                break;
            }
        }
        if (index == 0) return _stack[0];

        var parent = _stack[index - 1];
        parent.Metrics = parent.Metrics.Union(boxState.Metrics);

        // Ancestors with any metric changes must be re-resolved.
        for (int i = index - 1; i > 0; i--)
            _stack[i].PendingDescendants.Add(new PendingPositions
            {
                FragmentStart = boxState.FragmentStart,
                FragmentEnd = lineBox.Count,
                Metrics = boxState.Metrics,
                VerticalAlign = VerticalAlign.Baseline,
            });
        return parent;
    }

    public void OnEndPlaceItems(ConstraintSpace space, LogicalLineItems lineBox, FontBaselineType baselineType)
    {
        if (lineBox.WasPropagated)
            return;
        // Fold pending descendant metrics into the line box.
        var lineBoxState = LineBoxState;
        foreach (var state in _stack)
        {
            if (ReferenceEquals(state, lineBoxState)) continue;
            lineBoxState.Metrics = lineBoxState.Metrics.Union(state.Metrics);
        }
        while (_stack.Count > 1)
            _stack.RemoveAt(_stack.Count - 1);
    }

    public void PrepareForReorder(LogicalLineItems lineBox) { }

    public void UpdateAfterReorder(LogicalLineItems lineBox) { }

    /// <summary>
    /// Lay out the line box children left to right, mirroring
    /// InlineLayoutStateStack::ComputeInlinePositions().
    ///
    /// At entry every child has its origin at inline offset 0. This accumulates
    /// an inline position from <paramref name="position"/>, recording each
    /// child's pre-shift offset in <see cref="LogicalLineItem.MarginLineLeft"/>
    /// and advancing the running position by the child's inline size. The
    /// advance only happens for children that actually occupy inline space (text,
    /// atomic inlines, ruby placeholders, box-decoration spacers); open/close-tag
    /// and bidi-control placeholders are shifted but do not advance the pen,
    /// exactly as the source does. Returns the line's inline size.
    /// </summary>
    public float ComputeInlinePositions(LogicalLineItems lineBox, float position, bool ignoreBoxMarginBorderPadding)
    {
        if (lineBox.IsEmpty) return position;

        for (int i = 0; i < lineBox.Count; i++)
        {
            var child = lineBox[i];
            child.MarginLineLeft = child.Rect.InlineStart;
            child.MoveInInlineDirection(position);

            // An open or close tag carries no fragment of its own; only the spacer
            // that stands in for its margin/border/padding advances the pen.
            if (!child.HasFragment() && !child.IsRubyLinePlaceholder && !child.IsBoxDecorationSpacer)
                continue;

            position += child.Rect.InlineSize;
        }

        return position;
    }

    public void CreateBoxFragments(LogicalLineItems lineBox, bool isOpaque) { }

    public void ApplyRelativePositioning(LogicalLineItems lineBox, LogicalOffset? parentOffset) { }

    public void OnBlockInInline(FontHeight metrics, LogicalLineItems lineBox)
    {
        LineBoxState.Metrics = LineBoxState.Metrics.Union(metrics);
    }

    /// <summary>
    /// Data for a box fragment. Mirrors BoxData in inline_box_state.h.
    /// </summary>
    public class BoxData
    {
        public int FragmentStart;
        public int FragmentEnd;
        public InlineItem? Item;
        public LogicalRect Rect;
        public bool HasLineLeftEdge;
        public bool HasLineRightEdge;
        public BoxStrut Borders;
        public BoxStrut Padding;
        public float MarginLineOver;
        public float MarginLineUnder;
        public float MarginLineLeft;
        public float MarginLineRight;
        public float MarginBorderPaddingLineLeft;
        public float MarginBorderPaddingLineRight;
        public int ParentBoxDataIndex;
        public int FragmentedBoxDataIndex;

        public BoxData(int start, int end, InlineItem? item, LogicalSize size)
        {
            FragmentStart = start;
            FragmentEnd = end;
            Item = item;
            Rect = new LogicalRect(LogicalOffset.Zero, size);
        }

        public void SetFragmentRange(int start, int end)
        {
            FragmentStart = start;
            FragmentEnd = end;
        }
    }
}

/// <summary>
/// Contains information necessary for copying back data to a FloatingObject.
/// Mirrors positioned_float.h.
/// </summary>
public class PositionedFloat
{
    public LayoutResult? LayoutResult { get; set; }
    public BlockBreakToken? BreakBeforeToken { get; set; }
    public BfcOffset BfcOffset { get; set; }
    public float MinimumSpaceShortage { get; set; }

    public PositionedFloat() { }
    public PositionedFloat(LayoutResult? layoutResult, BlockBreakToken? breakBeforeToken, BfcOffset bfcOffset, float minimumSpaceShortage)
    {
        LayoutResult = layoutResult;
        BreakBeforeToken = breakBeforeToken;
        BfcOffset = bfcOffset;
        MinimumSpaceShortage = minimumSpaceShortage;
    }

    public BlockBreakToken? BreakToken() => BreakBeforeToken;
}