using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Inline;

/// <summary>
/// Represents a text run or a box in an inline formatting context. Consumes
/// less memory than a full fragment and can be stored in a flat list
/// (FragmentItems) for easier and faster traversal. Mirrors fragment_item.h.
/// </summary>
public class FragmentItem
{
    public enum ItemType
    {
        Invalid = 0,
        Text,
        GeneratedText,
        Line,
        Box,
    }

    public ItemType Type { get; set; }
    public string Text { get; set; } = "";
    public TextOffsetRange TextOffset { get; set; }
    public ShapeResult? ShapeResult { get; set; }
    public LayoutObject? LayoutObject { get; set; }
    public PhysicalOffset Offset { get; set; }
    public PhysicalSize Size { get; set; }
    public float BaselineOffset { get; set; }
    public int DescendantsCount { get; set; }
    public PhysicalLineBoxFragment? LineBoxFragment { get; set; }
    public bool IsFirstForNode { get; set; }
    public bool IsLastForNode { get; set; }
    public bool IsOpaqueToHitTesting { get; set; }

    public FragmentItem(ItemType type)
    {
        Type = type;
    }

    public static FragmentItem CreateText(LogicalLineItem lineItem, WritingDirectionMode writingDirection)
    {
        return new FragmentItem(ItemType.Text)
        {
            Text = lineItem.TextContent,
            TextOffset = lineItem.TextOffset,
            ShapeResult = lineItem.ShapeResult,
            LayoutObject = lineItem.LayoutObject ?? (lineItem.InlineItem?.Element != null
                ? new LayoutText(lineItem.InlineItem.Element, lineItem.TextContent)
                : null),
            Size = new PhysicalSize(lineItem.InlineSize, lineItem.Size.BlockSize),
            BaselineOffset = lineItem.Rect.BlockStart + lineItem.Size.BlockSize,
        };
    }

    public static FragmentItem CreateGeneratedText(ShapeResult? shapeResult, string text)
    {
        return new FragmentItem(ItemType.GeneratedText)
        {
            Text = text,
            ShapeResult = shapeResult,
            Size = new PhysicalSize(shapeResult?.InlineSize ?? 0, 16),
        };
    }

    public static FragmentItem CreateBox(BoxFragment box, Element? element)
    {
        return new FragmentItem(ItemType.Box)
        {
            LayoutObject = element != null ? new LayoutBoxModelObjectStub(element) : null,
            Offset = new PhysicalOffset(box.InlineOffset, box.BlockOffset),
            Size = new PhysicalSize(box.InlineSize, box.BlockSize),
        };
    }

    public static FragmentItem CreateLine(PhysicalLineBoxFragment lineBoxFragment, int descendantsCount)
    {
        return new FragmentItem(ItemType.Line)
        {
            LineBoxFragment = lineBoxFragment,
            DescendantsCount = descendantsCount,
        };
    }

    public PhysicalRect Rect => new(Offset, Size);
    public float InlineEnd => Offset.Left + Size.Width;
    public float BlockEnd => Offset.Top + Size.Height;

    public bool IsText => Type == ItemType.Text || Type == ItemType.GeneratedText;
    public bool IsLine => Type == ItemType.Line;
    public bool IsBox => Type == ItemType.Box;

    public override string ToString() =>
        $"{Type}: '{Text}' @({Offset.Left:F1},{Offset.Top:F1}) {Size.Width:F1}x{Size.Height:F1}";
}

/// <summary>
/// Stub box-model-object for fragment items that only carry a DOM element.
/// </summary>
internal sealed class LayoutBoxModelObjectStub : LayoutBoxModelObject
{
    public LayoutBoxModelObjectStub(Element element) : base(element) { }
    public override string GetName() => "LayoutBoxModelObjectStub";
}

/// <summary>
/// Represents the inside of an inline formatting context: a flat list of
/// FragmentItem. Mirrors fragment_items.h.
/// </summary>
public class FragmentItems
{
    private readonly List<FragmentItem> _items = new();
    public string NormalText { get; set; } = "";
    public string? FirstLineText { get; set; }
    public int SizeOfEarlierFragments { get; set; }

    public int Count => _items.Count;
    public FragmentItem this[int i] => _items[i];
    public IReadOnlyList<FragmentItem> Items => _items;

    public string Text(bool firstLine)
    {
        if (firstLine && !string.IsNullOrEmpty(FirstLineText))
            return FirstLineText;
        return NormalText;
    }

    public int EndItemIndex => SizeOfEarlierFragments + _items.Count;
    public bool HasItemIndex(int index) => index >= SizeOfEarlierFragments && index < EndItemIndex;

    public void Append(FragmentItem item) => _items.Add(item);
    public void Clear() => _items.Clear();

    public FragmentItem? FirstOf(LayoutObject layoutObject)
    {
        foreach (var item in _items)
            if (item.LayoutObject == layoutObject)
                return item;
        return null;
    }

    public bool IsContainerForCulledInline(LayoutInline layoutInline, out bool isFirstContainer, out bool isLastContainer, out bool childHasAnyChildItems)
    {
        isFirstContainer = false;
        isLastContainer = false;
        childHasAnyChildItems = false;
        foreach (var item in _items)
        {
            if (item.LayoutObject == layoutInline)
            {
                childHasAnyChildItems = true;
                isFirstContainer |= item.IsFirstForNode;
                isLastContainer |= item.IsLastForNode;
            }
        }
        return childHasAnyChildItems;
    }
}

/// <summary>
/// Builds a flat list of FragmentItem while laying out a line box.
/// Mirrors fragment_items_builder.h.
/// </summary>
public class FragmentItemsBuilder
{
    private readonly List<FragmentItem> _items = new();
    public InlineLayoutStateStack StateStack { get; } = new();
    public List<LogicalLineItems> Lines { get; } = new();

    public int Count => _items.Count;

    public void Add(FragmentItem item) => _items.Add(item);

    public void AddLogicalLineItems(LogicalLineItems lineItems, WritingDirectionMode writingDirection, LayoutObject? container)
    {
        Lines.Add(lineItems);
        foreach (var lineItem in lineItems)
        {
            if (!lineItem.CanCreateFragmentItem) continue;
            if (lineItem.InlineItem?.IsAtomicInline == true || lineItem.LayoutResult != null)
            {
                var box = lineItem.LayoutResult?.Fragment ?? new BoxFragment
                {
                    InlineSize = lineItem.InlineSize,
                    BlockSize = lineItem.Size.BlockSize > 0 ? lineItem.Size.BlockSize : 16
                };
                var item = FragmentItem.CreateBox(box, lineItem.InlineItem?.Element);
                item.Offset = new PhysicalOffset(lineItem.Rect.InlineStart, lineItem.Rect.BlockStart);
                _items.Add(item);
            }
            else
            {
                var text = lineItem.TextContent;
                if (text.Length == 0) continue;
                if (lineItem.InlineItem != null
                    && lineItem.InlineItem.Type != InlineItem.InlineItemType.Text
                    && text.IndexOf(Character.kObjectReplacementCharacter) >= 0)
                {
                    // Internal object-replacement placeholder of a replaced /
                    // control inline item: kept out of the text fragment list.
                    continue;
                }
                var item = new FragmentItem(FragmentItem.ItemType.Text)
                {
                    Text = text,
                    TextOffset = lineItem.TextOffset,
                    ShapeResult = lineItem.ShapeResult,
                    Offset = new PhysicalOffset(lineItem.Rect.InlineStart, lineItem.Rect.BlockStart),
                    Size = new PhysicalSize(lineItem.InlineSize, lineItem.Size.BlockSize > 0 ? lineItem.Size.BlockSize : 16),
                };
                _items.Add(item);
            }
        }
    }

    /// <summary>
    /// Add a LogicalLineContainer (base line + optional annotation lines).
    /// Mirrors FragmentItemsBuilder::AddLine in fragment_items_builder.cc.
    /// </summary>
    public void AddLogicalLineContainer(LogicalLineContainer container, WritingDirectionMode writingDirection, LayoutObject? containerLayoutObject)
    {
        AddLogicalLineItems(container.BaseLine, writingDirection, containerLayoutObject);
        foreach (var annotation in container.AnnotationLineList)
            AddLogicalLineItems(annotation.LineItems, writingDirection, containerLayoutObject);
    }

    public FragmentItems ToFragmentItems(string normalText)
    {
        var result = new FragmentItems { NormalText = normalText };
        foreach (var item in _items)
            result.Append(item);
        return result;
    }
}

/// <summary>
/// Physical line box fragment. Mirrors physical_line_box_fragment.h.
/// </summary>
public class PhysicalLineBoxFragment : PhysicalFragment
{
    public PhysicalLineBoxFragment()
    {
        Type = FragmentType.FragmentLineBox;
        Box = BoxType.InlineBox;
    }

    public float BaselineOffset { get; set; }
    public bool IsEmptyLineBox { get; set; }
    public bool HasLineBounds { get; set; }
    public bool IsFirstFormattedLine { get; set; }
}