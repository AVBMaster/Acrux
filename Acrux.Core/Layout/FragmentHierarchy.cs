using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Geom = Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// PhysicalFragment - the output geometry from layout, in the physical
/// coordinate system. Mirrors physical_fragment.h.
/// </summary>
public abstract class PhysicalFragment
{
    public enum FragmentType
    {
        FragmentBox = 0,
        FragmentLineBox = 1,
    }

    public enum BoxType
    {
        NormalBox,
        InlineBox,
        ColumnBox,
        PageContainer,
        PageBorderBox,
        PageMargin,
        PageArea,
        AtomicInline,
        Floating,
        OutOfFlowPositioned,
        BlockFlowRoot,
        RenderedLegend,
    }

    public PhysicalSize Size { get; set; }
    public PhysicalOffset Offset { get; set; }
    public FragmentType Type { get; protected set; }
    public BoxType Box { get; set; } = BoxType.NormalBox;
    public LayoutObject? LayoutObject { get; set; }
    public Element? Element { get; set; }
    public BreakToken? BreakToken { get; set; }
    public List<PhysicalFragment> Children { get; } = new();
    public LayoutResult? LayoutResult { get; set; }

    public bool IsLineBox => Type == FragmentType.FragmentLineBox;
    public bool IsBox => Type == FragmentType.FragmentBox;
    public bool IsFloating => Box == BoxType.Floating;
    public bool IsOutOfFlowPositioned => Box == BoxType.OutOfFlowPositioned;
    public bool IsInlineBox => Box == BoxType.InlineBox;
    public bool IsAtomicInline => Box == BoxType.AtomicInline;

    public PhysicalRect LocalRect => new(Offset, Size);

    public bool HasOutOfFlowFragmentChild() => Children.Any(c => c.IsOutOfFlowPositioned);
    public bool HasFloatingChild() => Children.Any(c => c.IsFloating);

    public override string ToString() =>
        $"Physical{Type}({Offset.Left:F1},{Offset.Top:F1} {Size.Width:F1}x{Size.Height:F1})";

    public StyleVariant StyleVariant { get; set; } = StyleVariant.Standard;
    public bool IsBlockInInline { get; set; }
    public bool IsLineForParallelFlow { get; set; }
    public bool IsTablePart { get; set; }
    public bool HasFloatingDescendantsForPaint { get; set; }
    public bool HasAdjoiningObjectDescendants { get; set; }
    public bool DependsOnPercentageBlockSize { get; set; }
    public bool IsPaintedAtomically { get; set; }
    public bool HasCollapsedBorders { get; set; }
    public bool IsHiddenForPaint { get; set; }
    public bool IsOpaque { get; set; }
    public bool IsFieldsetContainer { get; set; }
    public bool IsFirstForNode { get; set; }
    public bool IsLastForNode { get; set; }
    public bool ChildrenValid { get; set; } = true;
    public bool MayHaveDescendantAboveBlockStart { get; set; }
    public bool IsMathMLFraction { get; set; }
    public bool IsMathMLOperator { get; set; }
    public bool IsTextControlPlaceholder { get; set; }
    public bool HasOutOfFlowFragmentChildFlag { get; set; }
    public bool HasOutOfFlowInFragmentainerSubtree { get; set; }
    public TextDirection BaseDirection { get; set; } = TextDirection.Ltr;
    public PropagatedData? PropagatedDataInternal { get; set; }
    public FragmentedOofData? FragmentedOofDataInternal { get; set; }
    public OofData? OofDataInternal { get; set; }
}

/// <summary>
/// PhysicalBoxFragment - a box fragment produced by a layout algorithm.
/// Mirrors physical_box_fragment.h.
/// </summary>
public class PhysicalBoxFragment : PhysicalFragment
{
    public PhysicalBoxFragment()
    {
        Type = FragmentType.FragmentBox;
    }

    public bool IsFormattingContextRoot { get; set; }
    public float FirstBaseline { get; set; }
    public float LastBaseline { get; set; }
    public bool HasBaseline { get; set; }
    public bool IsSelfCollapsing { get; set; }
    public bool HasBlockFragmentation { get; set; }
}

/// <summary>
/// LogicalFragment - wraps a PhysicalFragment, exposing geometry in the logical
/// coordinate system. Mirrors logical_fragment.h.
/// </summary>
public readonly struct LogicalFragment
{
    private readonly PhysicalFragment _physical;
    private readonly WritingDirectionMode _writingDirection;

    public LogicalFragment(WritingDirectionMode writingDirection, PhysicalFragment physical)
    {
        _physical = physical;
        _writingDirection = writingDirection;
    }

    public float InlineSize => _writingDirection.IsHorizontal ? _physical.Size.Width : _physical.Size.Height;
    public float BlockSize => _writingDirection.IsHorizontal ? _physical.Size.Height : _physical.Size.Width;
    public LogicalSize Size => _writingDirection.IsHorizontal
        ? new LogicalSize(_physical.Size.Width, _physical.Size.Height)
        : new LogicalSize(_physical.Size.Height, _physical.Size.Width);
    public WritingDirectionMode WritingDirection => _writingDirection;
    public PhysicalFragment Physical => _physical;
}

/// <summary>
/// LogicalBoxFragment - logical view of a box fragment. Mirrors logical_box_fragment.h.
/// </summary>
public readonly struct LogicalBoxFragment
{
    private readonly PhysicalBoxFragment _physical;
    private readonly WritingDirectionMode _writingDirection;

    public LogicalBoxFragment(WritingDirectionMode writingDirection, PhysicalBoxFragment physical)
    {
        _physical = physical;
        _writingDirection = writingDirection;
    }

    public float InlineSize => _writingDirection.IsHorizontal ? _physical.Size.Width : _physical.Size.Height;
    public float BlockSize => _writingDirection.IsHorizontal ? _physical.Size.Height : _physical.Size.Width;
    public PhysicalBoxFragment Physical => _physical;

    public bool IsWritingModeEqual => _writingDirection.WritingMode == Geom.WritingMode.HorizontalTb;

    /// <summary>Synthesize a baseline when none is available. Mirrors SynthesizedBaseline().</summary>
    public static float SynthesizedBaseline(bool isAlphabetic, bool isFlippedLines, float blockSize)
    {
        if (isAlphabetic)
            return isFlippedLines ? 0 : blockSize;
        return blockSize / 2;
    }

    public float? FirstBaseline()
    {
        if (!IsWritingModeEqual)
            return null;
        if (_physical.HasBaseline)
            return _physical.FirstBaseline;
        return null;
    }

    public float FirstBaselineOrSynthesize(bool isAlphabeticBaseline)
    {
        var bl = FirstBaseline();
        if (bl.HasValue)
            return bl.Value;
        return SynthesizedBaseline(isAlphabeticBaseline, _writingDirection.WritingMode == Geom.WritingMode.VerticalRl, BlockSize);
    }

    public float? LastBaseline()
    {
        if (!IsWritingModeEqual)
            return null;
        if (_physical.HasBaseline)
            return _physical.LastBaseline;
        return null;
    }

    public float LastBaselineOrSynthesize(bool isAlphabeticBaseline)
    {
        var bl = LastBaseline();
        if (bl.HasValue)
            return bl.Value;
        return SynthesizedBaseline(isAlphabeticBaseline, _writingDirection.WritingMode == Geom.WritingMode.VerticalRl, BlockSize);
    }
}

/// <summary>
/// Represents a link from a fragment to a child fragment. Mirrors physical_fragment_link.h.
/// </summary>
public readonly struct PhysicalFragmentLink
{
    public PhysicalOffset Offset { get; }
    public PhysicalFragment? Fragment { get; }

    public PhysicalFragmentLink(PhysicalOffset offset, PhysicalFragment fragment)
    {
        Offset = offset;
        Fragment = fragment;
    }
}

/// <summary>
/// Logical view of a child link. Mirrors logical_fragment_link.h.
/// </summary>
public readonly struct LogicalFragmentLink
{
    public LogicalOffset Offset { get; }
    public PhysicalFragment? Fragment { get; }

    public LogicalFragmentLink(LogicalOffset offset, PhysicalFragment fragment)
    {
        Offset = offset;
        Fragment = fragment;
    }
}