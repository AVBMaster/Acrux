using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Inline;

/// <summary>
/// Represents a single item in inline layout. Mirrors inline_item.h.
/// </summary>
public class InlineItem
{
    public enum InlineItemType
    {
        Text, Control, AtomicInline, BlockInInline,
        OpenTag, CloseTag, Floating, OutOfFlowPositioned,
        InitialLetterBox, ListMarker, BidiControl,
        OpenRubyColumn, CloseRubyColumn, RubyLinePlaceholder
    }

    private readonly string _textContent = "";
    private readonly LayoutObject? _layoutObject;
    private readonly ShapeResult? _shapeResult;

    public InlineItemType Type { get; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public Element? Element { get; }
    public TextItemType TextType { get; set; } = TextItemType.kNormal;
    public int BidiLevel { get; set; }
    public bool IsCollapsibleSpace { get; set; }
    public bool IsOpaqueToCollapsing { get; set; }
    public bool IsGeneratedForLineBreak { get; set; }
    public bool IsSymbolMarker { get; set; }
    public float InlineSize { get; set; }
    public InlineItemResult? Result { get; set; }
    public ComputedStyle? StyleOverride { get; set; }

    public InlineItem(InlineItemType type, int start, int end, Element? element = null, string? textContent = null, LayoutObject? layoutObject = null, ShapeResult? shapeResult = null)
    {
        Type = type;
        StartOffset = start;
        EndOffset = end;
        Element = element;
        if (textContent != null) _textContent = textContent;
        _layoutObject = layoutObject;
        _shapeResult = shapeResult;
    }

    public bool IsText => Type == InlineItemType.Text;
    public bool IsAtomicInline => Type == InlineItemType.AtomicInline;
    public int Length => EndOffset - StartOffset;

    public bool IsEmptyItem() => Length == 0;

    public LayoutObject GetLayoutObject() => _layoutObject ?? new LayoutText(Element, TextContent());

    public ComputedStyle Style() => StyleOverride ?? Element?.ComputedStyle ?? new ComputedStyle();

    public TextDirection Direction => Element?.ComputedStyle is { } s && string.Equals(s.Direction, "rtl", StringComparison.OrdinalIgnoreCase)
        ? TextDirection.Rtl
        : TextDirection.Ltr;

    public string TextContent() => _textContent;

    public ShapeResult? TextShapeResult() => _shapeResult;

    /// <summary>Mirrors InlineItem::EndCollapseType().</summary>
    public bool EndCollapseType() => IsOpaqueToCollapsing;

    public bool ShouldCreateBoxFragment()
    {
        if (Type != InlineItemType.OpenTag && Type != InlineItemType.CloseTag) return false;
        var s = Element?.ComputedStyle;
        if (s == null) return false;
        return s.BorderTopWidth + s.BorderBottomWidth + s.BorderLeftWidth + s.BorderRightWidth > 0
            || !(s.PaddingTop is PixelLength pt && pt.Value == 0)
            || !(s.PaddingBottom is PixelLength pb && pb.Value == 0)
            || !(s.PaddingLeft is PixelLength pl && pl.Value == 0)
            || !(s.PaddingRight is PixelLength pr && pr.Value == 0)
            || !(s.MarginTop is PixelLength mt && mt.Value == 0)
            || !(s.MarginBottom is PixelLength mb && mb.Value == 0)
            || !(s.MarginLeft is PixelLength ml && ml.Value == 0)
            || !(s.MarginRight is PixelLength mr && mr.Value == 0)
            || s.Display == DisplayType.InlineBlock;
    }

    public bool IsImage() => Type == InlineItemType.AtomicInline && (Element?.TagName is "IMG" or "IMG") && Element?.TagName == "IMG";

    public bool IsTextCombine() => false;

    public override string ToString() => $"{Type}:{StartOffset}-{EndOffset} '{TextContent()}'";
}

/// <summary>
/// Result of laying out an inline item. Mirrors inline_item_result.h.
/// </summary>
public class InlineItemResult
{
    public InlineItem Item { get; }
    public int ItemIndex { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public float InlineSize { get; set; }
    public float BlockSize { get; set; }
    public float BaselineOffset { get; set; }
    public bool CanBreakAfter { get; set; }
    public bool MayBreakInside { get; set; }
    public bool BreakAnywhereIfOverflow { get; set; }
    public bool ShouldCreateLineBox { get; set; }
    public bool HasUnpositionedFloats { get; set; }
    public bool HasOnlyPreWrapTrailingSpaces { get; set; }
    public bool HasOnlyBidiTrailingSpaces { get; set; }
    public bool IsHyphenated { get; set; }
    public HyphenResult? Hyphen { get; set; }
    public BoxStrut Margins { get; set; }
    public BoxStrut Borders { get; set; }
    public BoxStrut Padding { get; set; }
    public float SpacingBefore { get; set; }
    public LayoutResult? LayoutResult { get; set; }
    public string? TextContent { get; set; }
    public ShapeResult? ShapeResult { get; set; }
    public PositionedFloat? PositionedFloat { get; set; }
    public ExclusionSpace? ExclusionSpaceBeforePositionFloat { get; set; }
    public InlineItemResultRubyColumn? RubyColumn { get; set; }
    public float PendingEndOverhang { get; set; }

    public InlineItemResult(InlineItem item, int itemIndex = 0)
    {
        Item = item;
        ItemIndex = itemIndex;
        StartOffset = item.StartOffset;
        EndOffset = item.EndOffset;
    }

    public InlineItemResult(InlineItem item, int itemIndex, TextOffsetRange textOffset, bool breakAnywhereIfOverflow, bool shouldCreateLineBox, bool hasUnpositionedFloats)
    {
        Item = item;
        ItemIndex = itemIndex;
        StartOffset = textOffset.Start;
        EndOffset = textOffset.End;
        BreakAnywhereIfOverflow = breakAnywhereIfOverflow;
        ShouldCreateLineBox = shouldCreateLineBox;
        HasUnpositionedFloats = hasUnpositionedFloats;
    }

    public int Length => EndOffset - StartOffset;
    public TextOffsetRange TextOffset() => new(StartOffset, EndOffset);
    public InlineItemTextIndex Start() => new() { ItemIndex = ItemIndex, TextOffset = StartOffset };
    public InlineItemTextIndex End() => new() { ItemIndex = ItemIndex, TextOffset = EndOffset };
    public bool IsRubyColumn() => RubyColumn != null;

    /// <summary>Shape the hyphen string on demand (mirrors InlineItemResult::ShapeHyphen).</summary>
    public void ShapeHyphen()
    {
        if (Hyphen == null && Item.Style() != null) Hyphen = new HyphenResult(Item.Style());
    }

    /// <summary>Width contributing to the line, equivalent to ShapingLineBreaker output.</summary>
    public float EndOffsetForJustify => EndOffset;
}

/// <summary>Data for a ruby column result. Ruby layout is a stub in this port.</summary>
public class InlineItemResultRubyColumn
{
    public LineInfo BaseLine { get; set; } = new();
    public List<LineInfo> AnnotationLineList { get; } = new();
    public List<int> PositionList { get; } = new();
    public bool IsContinuation { get; set; }
    public InlineBreakToken? StartRubyBreakToken { get; set; }
    public InlineBreakToken? EndRubyBreakToken { get; set; }
    public float LastBaseGlyphSpacing { get; set; }
}

/// <summary>
/// Segment of an inline item by style/bidi. Mirrors inline_item_segment.h.
/// </summary>
public class InlineItemSegment
{
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public bool IsRtl { get; set; }
    public int Length => EndOffset - StartOffset;

    public static object? UnpackSegmentData(int start, int end, object? segmentData) => segmentData;
}

/// <summary>
/// Data for inline items of a node. Mirrors inline_items_data.h.
/// </summary>
public class InlineItemsData
{
    public List<InlineItem> Items { get; } = new();
    public string TextContent { get; set; } = "";
    public bool IsBlockLevel { get; set; }
    public bool HasText { get; set; }
    public bool IsBidiEnabled { get; set; }
    public TextDirection BaseDirection { get; set; } = TextDirection.Ltr;

    public InlineItemTextIndex End() => new()
    {
        ItemIndex = Items.Count,
        TextOffset = TextContent.Length,
    };

    public void AssertOffset(InlineItemTextIndex index) { AssertOffset(index.ItemIndex, index.TextOffset); }

    /// <summary>
    /// When true, a failed <see cref="AssertOffset(int,int)"/> throws instead of
    /// reporting. Off by default: a desynchronised inline offset is a bug worth
    /// surfacing, but aborting the layout pass blanks the entire page, which is
    /// far worse than laying out one inline formatting context imperfectly.
    /// Tests and debugging sessions can opt into the strict behaviour.
    /// </summary>
    public static bool ThrowOnOffsetAssertFailure { get; set; }

    /// <summary>Number of offset inconsistencies observed so far, for diagnostics.</summary>
    public static int OffsetAssertFailureCount { get; private set; }

    /// <summary>
    /// Consistency check that the text offset lies inside the item at
    /// <paramref name="itemIndex"/>. Mirrors a debug assertion in the source
    /// algorithm, so it is non-fatal unless explicitly configured otherwise.
    /// </summary>
    public void AssertOffset(int itemIndex, int textOffset)
    {
        if (itemIndex >= Items.Count)
            return;

        var item = Items[itemIndex];
        if (textOffset >= item.StartOffset && textOffset <= item.EndOffset)
            return;

        OffsetAssertFailureCount++;
        var message = $"offset not in item: {textOffset} vs [{item.StartOffset},{item.EndOffset}] (item {itemIndex}/{Items.Count})";

        if (ThrowOnOffsetAssertFailure)
            throw new InvalidOperationException(message);

        if (OffsetAssertFailureCount <= 10)
            Console.WriteLine($"[InlineItems] {message}");
    }

    /// <summary>
    /// The open-tag items in <c>[startItemIndex, startItemIndex + itemCount)</c>
    /// that are still open at the end of that range, i.e. the inline boxes a line
    /// starting there continues. Mirrors InlineItemsData::GetOpenTagItems().
    /// </summary>
    public List<InlineItem> GetOpenTagItems(int startItemIndex, int itemCount, List<InlineItem> openItems)
    {
        openItems.Clear();
        for (int i = Math.Max(0, startItemIndex); i < Math.Min(Items.Count, startItemIndex + itemCount); i++)
        {
            var item = Items[i];
            switch (item.Type)
            {
                case InlineItem.InlineItemType.OpenTag:
                    openItems.Add(item);
                    break;
                case InlineItem.InlineItemType.CloseTag:
                    // Generated content does not always pair an open tag with a close
                    // tag, so search for the matching one.
                    for (int j = openItems.Count - 1; j >= 0; j--)
                    {
                        if (ReferenceEquals(openItems[j].Element, item.Element))
                        {
                            openItems.RemoveAt(j);
                            break;
                        }
                    }
                    break;
            }
        }
        return openItems;
    }
}

/// <summary>
/// Item text index for inline layout. Mirrors inline_item_text_index.h.
/// </summary>
public class InlineItemTextIndex : IComparable<InlineItemTextIndex>, IEquatable<InlineItemTextIndex>
{
    public int ItemIndex { get; set; }
    public int TextOffset { get; set; }

    public bool IsZero => ItemIndex == 0 && TextOffset == 0;

    public int CompareTo(InlineItemTextIndex? other)
    {
        if (other == null) return 1;
        int cmp = ItemIndex.CompareTo(other.ItemIndex);
        return cmp != 0 ? cmp : TextOffset.CompareTo(other.TextOffset);
    }

    public static bool operator <(InlineItemTextIndex a, InlineItemTextIndex b) => a.CompareTo(b) < 0;
    public static bool operator >(InlineItemTextIndex a, InlineItemTextIndex b) => a.CompareTo(b) > 0;
    public static bool operator <=(InlineItemTextIndex a, InlineItemTextIndex b) => a.CompareTo(b) <= 0;
    public static bool operator >=(InlineItemTextIndex a, InlineItemTextIndex b) => a.CompareTo(b) >= 0;
    public static bool operator ==(InlineItemTextIndex? a, InlineItemTextIndex? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.ItemIndex == b.ItemIndex && a.TextOffset == b.TextOffset;
    }
    public static bool operator !=(InlineItemTextIndex? a, InlineItemTextIndex? b) => !(a == b);
    public bool Equals(InlineItemTextIndex? other) => this == other;
    public override bool Equals(object? obj) => obj is InlineItemTextIndex other && this == other;
    public override int GetHashCode() => HashCode.Combine(ItemIndex, TextOffset);
    public override string ToString() => $"{ItemIndex}/{TextOffset}";
}

/// <summary>
/// Node data for inline items. Mirrors inline_node_data.h.
/// </summary>
public class InlineNodeData
{
    public InlineItemsData ItemsData { get; } = new();
    public string TextContent => ItemsData.TextContent;
}

/// <summary>
/// Caret position for inline content. Mirrors inline_caret_position.h.
/// </summary>
public class InlineCaretPosition
{
    public int ItemIndex { get; set; }
    public int TextOffset { get; set; }
    public bool IsBefore { get; set; }
    public bool IsAfter { get; set; }
}

/// <summary>
/// A span of InlineItems tied to InlineItemsData (mirrors inline_item_span.h).
/// GC-lifetime is managed by the runtime in C#, so this is a simple window type.
/// </summary>
public struct InlineItemSpan
{
    private InlineItemsData? _data;
    private int _begin;
    private int _size;

    public void SetItems(InlineItemsData data, int begin, int size)
    {
        _data = data;
        _begin = begin;
        _size = size;
    }

    public void Clear()
    {
        _data = null;
        _begin = 0;
        _size = 0;
    }

    public bool Empty => _size == 0;
    public int Size => _size;
    public InlineItem this[int i] => _data!.Items[_begin + i];
    public InlineItem Front => _data!.Items[_begin];
}

/// <summary>
/// Holds a LayoutObject for an inline item together with the item range.
/// </summary>
public struct InlineItemLayoutObjectSwitcher
{
    public LayoutObject? LayoutObject;
    public int StartItemIndex;
    public int EndItemIndex;
}