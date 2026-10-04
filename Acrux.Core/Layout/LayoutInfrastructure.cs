using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public enum FontBaseline
{
    Alphabetic,
    Central,
    Hanging,
    Ideographic,
    Mathematical,
    Middle,
    NoBaseline,
    Top,
    Bottom,
    TextBottom,
    TextTop,
}

public enum ItemPosition
{
    Legacy,
    Auto,
    Normal,
    Stretch,
    Baseline,
    LastBaseline,
    AnchorCenter,
    Center,
    Start,
    End,
    SelfStart,
    SelfEnd,
    FlexStart,
    FlexEnd,
    Left,
    Right,
}

public enum BaselineGroup
{
    Major,
    Minor,
}

public enum OverflowAlignment
{
    Default,
    Unsafe,
    Safe,
}

public enum ContentPosition
{
    Normal,
    Baseline,
    LastBaseline,
    Center,
    Start,
    End,
    FlexStart,
    FlexEnd,
    Left,
    Right,
}

public enum ContentDistributionType
{
    Default,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
    Stretch,
}

public class StyleContentAlignmentData
{
    public ContentPosition Position { get; set; }
    public ContentDistributionType Distribution { get; set; }
    public OverflowAlignment Overflow { get; set; }

    public StyleContentAlignmentData() { }

    public StyleContentAlignmentData(ContentPosition position, ContentDistributionType distribution, OverflowAlignment overflow = OverflowAlignment.Default)
    {
        Position = position;
        Distribution = distribution;
        Overflow = overflow;
    }

    public override bool Equals(object? obj) =>
        obj is StyleContentAlignmentData o && Position == o.Position && Distribution == o.Distribution && Overflow == o.Overflow;

    public override int GetHashCode() => HashCode.Combine(Position, Distribution, Overflow);
    public static bool operator ==(StyleContentAlignmentData a, StyleContentAlignmentData b) => a.Equals(b);
    public static bool operator !=(StyleContentAlignmentData a, StyleContentAlignmentData b) => !a.Equals(b);
}

public class StyleSelfAlignmentData
{
    public ItemPosition Position { get; set; }
    public OverflowAlignment Overflow { get; set; }
    public bool IsLegacy { get; set; }

    public StyleSelfAlignmentData() { }

    public StyleSelfAlignmentData(ItemPosition position, OverflowAlignment overflow, bool isLegacy = false)
    {
        Position = position;
        Overflow = overflow;
        IsLegacy = isLegacy;
    }

    public override bool Equals(object? obj) =>
        obj is StyleSelfAlignmentData o && Position == o.Position && Overflow == o.Overflow && IsLegacy == o.IsLegacy;

    public override int GetHashCode() => HashCode.Combine(Position, Overflow, IsLegacy);
    public static bool operator ==(StyleSelfAlignmentData a, StyleSelfAlignmentData b) => a.Equals(b);
    public static bool operator !=(StyleSelfAlignmentData a, StyleSelfAlignmentData b) => !a.Equals(b);
}

public enum EBoxAlignment
{
    Stretch,
    Start,
    Center,
    End,
    Baseline,
}

public enum EBoxPack
{
    Start,
    Center,
    End,
    Justify,
}

public enum FlexSign
{
    PositiveFlexibility,
    NegativeFlexibility,
}

public enum PhysicalDirection : byte
{
    Up = 0,
    Right,
    Down,
    Left,
}

public enum EBreakBetween
{
    Auto,
    Avoid,
    AvoidColumn,
    AvoidPage,
    Column,
    Page,
    Left,
    Right,
    Recto,
    Verso,
}

public enum EFloat
{
    None,
    Left,
    Right,
    InlineStart,
    InlineEnd,
}

public readonly struct FragmentGeometry
{
    public LogicalSize BorderBoxSize { get; }
    public BoxStrut Border { get; }
    public BoxStrut Padding { get; }
    public BoxStrut Scrollbar { get; }
    public float Margin { get; }
    public MinMaxSizes MinMax { get; }

    public FragmentGeometry(LogicalSize borderBoxSize, BoxStrut border, BoxStrut padding, BoxStrut scrollbar, float margin, MinMaxSizes minMax)
    {
        BorderBoxSize = borderBoxSize;
        Border = border;
        Padding = padding;
        Scrollbar = scrollbar;
        Margin = margin;
        MinMax = minMax;
    }
}

public enum LayoutSelectionStatus
{
    None,
    Start,
    Inside,
    End,
    StartAndEnd,
}

public class TextOffsetMap
{
}

public class BlockBreakTokenData
{
    public enum BreakTokenDataType
    {
        kBlockBreakTokenData,
        kFieldsetBreakTokenData,
        kFlexBreakTokenData,
        kGridBreakTokenData,
        kTableBreakTokenData,
        kTableRowBreakTokenData,
        kColumnBreakTokenData,
    }

    public BreakTokenDataType Type { get; }

    public LayoutUnit ConsumedBlockSize { get; set; }
    public LayoutUnit ConsumedBlockSizeLegacyAdjustment { get; set; }
    public LayoutUnit MonolithicOverflow { get; set; }
    public int SequenceNumber { get; set; }

    public BlockBreakTokenData(BreakTokenDataType type = BreakTokenDataType.kBlockBreakTokenData, BlockBreakTokenData? otherData = null)
    {
        Type = type;
        if (otherData != null)
        {
            ConsumedBlockSize = otherData.ConsumedBlockSize;
            ConsumedBlockSizeLegacyAdjustment = otherData.ConsumedBlockSizeLegacyAdjustment;
            SequenceNumber = otherData.SequenceNumber;
            MonolithicOverflow = otherData.MonolithicOverflow;
        }
    }

    public bool IsFieldsetType => Type == BreakTokenDataType.kFieldsetBreakTokenData;
    public bool IsFlexType => Type == BreakTokenDataType.kFlexBreakTokenData;
    public bool IsGridType => Type == BreakTokenDataType.kGridBreakTokenData;
    public bool IsTableType => Type == BreakTokenDataType.kTableBreakTokenData;
    public bool IsTableRowType => Type == BreakTokenDataType.kTableRowBreakTokenData;
}

public struct NGFlexItem
{
    public LayoutUnit MainAxisFinalSize { get; set; }
    public LayoutUnit MarginBlockEnd { get; set; }
    public LayoutUnit TotalRemainingBlockSize { get; set; }
    public FlexOffset Offset { get; set; }
    public bool IsInitialBlockSizeIndefinite { get; set; }
    public bool IsUsedFlexBasisIndefinite { get; set; }
    public bool HasDescendantThatDependsOnPercentageBlockSize { get; set; }
}

public struct NGFlexLine
{
    public LayoutUnit MainAxisFreeSpace { get; set; }
    public LayoutUnit LineCrossSize { get; set; }
    public LayoutUnit CrossAxisOffset { get; set; }
    public LayoutUnit MajorBaseline { get; set; }
    public LayoutUnit MinorBaseline { get; set; }
    public LayoutUnit ItemOffsetAdjustment { get; set; }
    public bool HasSeenAllChildren { get; set; }
    public List<NGFlexItem> LineItems { get; set; }

    public NGFlexLine(int numItems)
    {
        LineItems = new List<NGFlexItem>(numItems);
        for (int i = 0; i < numItems; i++)
            LineItems.Add(new NGFlexItem());
    }

    public LayoutUnit LineCrossEnd => LineCrossSize + CrossAxisOffset + ItemOffsetAdjustment;
}

public class FlexBreakTokenDataImpl : BlockBreakTokenData
{
    public enum FlexBreakBeforeRow
    {
        kNotBreakBeforeRow,
        kAtStartOfBreakBeforeRow,
        kPastStartOfBreakBeforeRow,
    }

    public List<NGFlexLine> FlexLines { get; } = new();
    public List<EBreakBetween> RowBreakBetween { get; } = new();
    public LayoutUnit IntrinsicBlockSize { get; set; }
    public FlexBreakBeforeRow BreakBeforeRow { get; set; } = FlexBreakBeforeRow.kNotBreakBeforeRow;

    public FlexBreakTokenDataImpl(BlockBreakTokenData? breakTokenData = null)
        : base(BreakTokenDataType.kFlexBreakTokenData, breakTokenData)
    {
    }
}

public readonly struct HTMLDimension
{
    public enum TypeEnum
    {
        Relative,
        Percentage,
        Absolute,
    }

    public TypeEnum Type { get; }
    public float Value { get; }

    public HTMLDimension(TypeEnum type, float value)
    {
        Type = type;
        Value = value;
    }
}

public class FrameSetLayoutData
{
    /// <summary>Frame grid column sizes, in px.</summary>
    public List<float> ColSizes { get; } = new();
    /// <summary>Frame grid row sizes, in px.</summary>
    public List<float> RowSizes { get; } = new();
    /// <summary>Whether a vertical border may be painted after each column.</summary>
    public List<bool> ColAllowBorder { get; } = new();
    /// <summary>Whether a horizontal border may be painted after each row.</summary>
    public List<bool> RowAllowBorder { get; } = new();
    /// <summary>Border thickness, in px.</summary>
    public int BorderThickness { get; set; }
    /// <summary>True when the user specified a border color via the frameset's border-color attribute.</summary>
    public bool HasBorderColor { get; set; }
}

public readonly struct EphemeralRange
{
    public Node? StartNode { get; }
    public Node? EndNode { get; }
    public int StartOffset { get; }
    public int EndOffset { get; }

    public EphemeralRange(Node? startNode, Node? endNode, int startOffset, int endOffset)
    {
        StartNode = startNode;
        EndNode = endNode;
        StartOffset = startOffset;
        EndOffset = endOffset;
    }
}

public class LogicalRubyColumn
{
}

public static class VectorCompat
{
    public static void Add<T>(this List<T> list, T item) => list.Add(item);
    public static void EraseAt<T>(this List<T> list, int index) { if (index >= 0 && index < list.Count) list.RemoveAt(index); }
    public static void Resize<T>(this List<T> list, int size) { if (size < list.Count) list.RemoveRange(size, list.Count - size); else while (list.Count < size) list.Add(default!); }
    public static T Back<T>(this List<T> list) => list[list.Count - 1];
    public static void push_back<T>(this List<T> list, T item) => list.Add(item);
    public static T pop_back<T>(this List<T> list) { var item = list[list.Count - 1]; list.RemoveAt(list.Count - 1); return item; }
    public static bool empty<T>(this List<T> list) => list.Count == 0;
    public static int size<T>(this List<T> list) => list.Count;
    public static void clear<T>(this List<T> list) => list.Clear();
    public static void ReserveInitialCapacity<T>(this List<T> list, int capacity) { if (list.Capacity < capacity) list.Capacity = capacity; }
    public static void Shrink<T>(this List<T> list, int size) { if (size < list.Count) list.RemoveRange(size, list.Count - size); }
}