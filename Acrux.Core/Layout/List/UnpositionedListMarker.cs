using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.List;

/// <summary>
/// Represents an unpositioned list marker.
///
/// A list item can have either block children or inline children. Because
/// BlockLayoutAlgorithm handles the former while InlineLayoutAlgorithm
/// handles the latter, list marker can appear in either algorithm.
///
/// To handle these two cases consistently, when list markers appear in these
/// algorithms, they are set as "unpositioned", and are propagated to ancestors
/// through LayoutResult until they meet the corresponding list items.
/// Mirrors unpositioned_list_marker.h/.cc.
/// </summary>
public class UnpositionedListMarker
{
    private LayoutOutsideListMarker? _markerLayoutObject;

    public UnpositionedListMarker()
    {
        _markerLayoutObject = null;
    }

    public UnpositionedListMarker(LayoutOutsideListMarker? marker)
    {
        _markerLayoutObject = marker;
    }

    public UnpositionedListMarker(BlockNode node)
        : this(node.GetLayoutBox() as LayoutOutsideListMarker)
    {
    }

    public static implicit operator bool(UnpositionedListMarker m) => m._markerLayoutObject != null;

    public bool IsValid => _markerLayoutObject != null;

    /// <summary>
    /// Compute the inline offset of the marker, relative to the list item.
    /// </summary>
    public LayoutUnit InlineOffset(LayoutUnit markerInlineSize)
    {
        if (_markerLayoutObject == null) return LayoutUnit.Zero;

        var listItem = _markerLayoutObject.Marker().ListItem(_markerLayoutObject);
        if (listItem == null) return LayoutUnit.Zero;

        var margins = ListMarker.InlineMarginsForOutside(
            _markerLayoutObject.Style ?? new ComputedStyle(),
            listItem.Style ?? new ComputedStyle(),
            markerInlineSize);

        return margins.Item1;
    }

    /// <summary>
    /// Layout the marker. Returns the LayoutResult.
    /// </summary>
    public LayoutResult? Layout(ConstraintSpace parentSpace, ComputedStyle parentStyle)
    {
        if (_markerLayoutObject == null) return null;

        // Simplified: perform a block layout of the marker.
        var element = _markerLayoutObject.Node as Element;
        if (element == null) return null;

        var algorithm = new BlockLayoutAlgorithm(element, parentSpace);
        return algorithm.Layout();
    }

    /// <summary>
    /// Returns the baseline that the list-marker should place itself along.
    /// std::nullopt indicates that the child content does not have a baseline
    /// to align to.
    /// </summary>
    public LayoutUnit? ContentAlignmentBaseline(ConstraintSpace space, PhysicalFragment content)
    {
        if (content is PhysicalLineBoxFragment lineBox)
        {
            if (lineBox.IsEmptyLineBox && lineBox.BreakToken != null)
                return null;

            return new LayoutUnit(lineBox.Metrics.Ascent);
        }

        // For block content, use the first baseline.
        if (content is PhysicalBoxFragment boxFragment)
        {
            var baseline = boxFragment.FirstBaseline;
            return baseline;
        }

        return null;
    }

    /// <summary>
    /// Add a fragment for an outside list marker.
    /// </summary>
    public void AddToBox(ConstraintSpace space, PhysicalFragment content, BoxStrut borderScrollbarPadding,
        LayoutResult markerLayoutResult, LayoutUnit contentBaseline, ref LayoutUnit blockOffset,
        BoxFragmentBuilder containerBuilder)
    {
        var markerFragment = markerLayoutResult.Fragment;

        // Compute the inline offset of the marker.
        LayoutUnit markerInlineOffset = InlineOffset(new LayoutUnit(markerFragment.InlineSize));
        LogicalOffset markerOffset = new(markerInlineOffset.Value, blockOffset.Value);

        // Adjust the block offset to align baselines of the marker and the content.
        float markerAscent = MarkerBaseline(markerFragment);
        float baselineAdjust = contentBaseline.Value - markerAscent;
        if (baselineAdjust >= 0)
        {
            markerOffset = new LogicalOffset(markerOffset.InlineOffset, markerOffset.BlockOffset + baselineAdjust);
        }
        else
        {
            blockOffset = new LayoutUnit(blockOffset.Value - baselineAdjust);
        }

        markerOffset = new LogicalOffset(
            markerOffset.InlineOffset + ComputeIntrudedFloatOffset(space, containerBuilder, borderScrollbarPadding, new LayoutUnit(markerOffset.BlockOffset)).Value,
            markerOffset.BlockOffset);

        markerFragment.InlineOffset = markerOffset.InlineOffset;
        markerFragment.BlockOffset = markerOffset.BlockOffset;
        containerBuilder.AddChild(markerFragment);
    }

    /// <summary>
    /// Baseline of the marker fragment, measured from its own block-start edge.
    /// Prefers the baseline the marker's own line box reported; otherwise derives
    /// it from the marker style's font metrics. It must not be approximated as a
    /// fraction of the marker height, or the marker will not sit on the same
    /// baseline as the list item's first line.
    /// </summary>
    private static float MarkerBaseline(BoxFragment markerFragment)
    {
        if (markerFragment.Lines.Count > 0)
        {
            var first = markerFragment.Lines[0];
            if (first.BaselineOffset > 0)
                return first.BaselineOffset;
        }

        var style = markerFragment.Element?.ComputedStyle;
        if (style != null)
            return Fonts.LineBoxMetrics.GetBaseline(style);

        return markerFragment.ContentBlockSize;
    }

    /// <summary>
    /// Add a fragment for an outside list marker when the list item has no line boxes.
    /// </summary>
    public void AddToBoxWithoutLineBoxes(ConstraintSpace space, LayoutResult markerLayoutResult,
        BoxFragmentBuilder containerBuilder, ref LayoutUnit intrinsicBlockSize)
    {
        var markerFragment = markerLayoutResult.Fragment;

        LayoutUnit markerInlineOffset = InlineOffset(new LayoutUnit(markerFragment.InlineSize));
        markerFragment.InlineOffset = markerInlineOffset.Value;
        markerFragment.BlockOffset = 0;

        containerBuilder.AddChild(markerFragment);

        // Whether the list marker should affect the block size or not is not
        // well-defined, but 3 out of 4 impls do.
        float markerBlockSize = markerFragment.BlockSize;
        if (markerBlockSize > intrinsicBlockSize.Value)
        {
            intrinsicBlockSize = new LayoutUnit(markerBlockSize);
            containerBuilder.IntrinsicBlockSize = markerBlockSize;
        }
    }

    /// <summary>
    /// Compute the intruded float offset for the marker.
    /// </summary>
    private LayoutUnit ComputeIntrudedFloatOffset(ConstraintSpace space, BoxFragmentBuilder containerBuilder,
        BoxStrut borderScrollbarPadding, LayoutUnit markerBlockOffset)
    {
        // If the BFC block-offset isn't resolved, the intruded offset isn't available either.
        if (containerBuilder.BfcBlockOffset == 0 && containerBuilder.BfcBlockOffset == 0)
            return LayoutUnit.Zero;

        float originLineOffset = containerBuilder.BfcLineOffset + borderScrollbarPadding.Left;
        float originBlockOffset = containerBuilder.BfcBlockOffset + markerBlockOffset.Value;
        float availableSize = (containerBuilder.InlineSize - borderScrollbarPadding.HorizontalSum);

        if (_markerLayoutObject == null) return LayoutUnit.Zero;

        // Simplified float intrusion: check if the marker position is within float exclusions.
        // For now, return 0 (no intrusion).
        return LayoutUnit.Zero;
    }

    public override bool Equals(object? obj)
    {
        if (obj is UnpositionedListMarker other)
            return _markerLayoutObject == other._markerLayoutObject;
        return false;
    }

    public override int GetHashCode()
    {
        return _markerLayoutObject?.GetHashCode() ?? 0;
    }

    public static bool operator ==(UnpositionedListMarker a, UnpositionedListMarker b)
    {
        return a._markerLayoutObject == b._markerLayoutObject;
    }

    public static bool operator !=(UnpositionedListMarker a, UnpositionedListMarker b)
    {
        return !(a == b);
    }
}

/// <summary>
/// Minimal physical line box fragment for list marker alignment.
/// Mirrors PhysicalLineBoxFragment.
/// </summary>
public class PhysicalLineBoxFragment : PhysicalFragment
{
    public PhysicalLineBoxFragment() : base(PhysicalFragment.FragmentType.LineBox)
    {
    }

    public bool IsEmptyLineBox { get; set; }
    public BlockBreakToken? BreakToken { get; set; }
    public FontHeightMetrics Metrics { get; set; } = new(0, 0, 0);
}

/// <summary>
/// Minimal physical box fragment for list marker alignment.
/// Mirrors PhysicalBoxFragment.
/// </summary>
public class PhysicalBoxFragment : PhysicalFragment
{
    public PhysicalBoxFragment() : base(PhysicalFragment.FragmentType.Box)
    {
    }

    public LayoutUnit? FirstBaseline { get; set; }
}

/// <summary>
/// Minimal physical fragment base class.
/// Mirrors PhysicalFragment.
/// </summary>
public class PhysicalFragment
{
    public enum FragmentType
    {
        Box,
        LineBox,
        Text,
        Fragmentainer,
        Page,
        PageBorderBox,
        Svg,
    }

    public FragmentType Type { get; }
    public LayoutObject? LayoutObject { get; set; }
    public PhysicalSize Size { get; set; }
    public PhysicalOffset Offset { get; set; }

    protected PhysicalFragment(FragmentType type)
    {
        Type = type;
    }
}

/// <summary>
/// Extension to get the layout box from a BlockNode.
/// </summary>
public static class BlockNodeExtensions
{
    public static LayoutObject? GetLayoutBox(this BlockNode node)
    {
        return node.LayoutObject;
    }
}