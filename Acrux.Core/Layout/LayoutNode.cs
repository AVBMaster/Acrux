using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Represents the input to a layout algorithm for a given node. Mirrors
/// layout_input_node.h.
/// </summary>
public class LayoutInputNode
{
    public enum LayoutInputNodeType
    {
        Block,
        Inline,
    }

    public LayoutBox? Box { get; }
    public LayoutInputNodeType Type { get; }
    public LayoutObject? LayoutObject { get; }

    protected LayoutInputNode(LayoutBox? box, LayoutInputNodeType type, LayoutObject? layoutObject = null)
    {
        Box = box;
        Type = type;
        LayoutObject = layoutObject;
    }

    public bool IsInline => Type == LayoutInputNodeType.Inline;
    public bool IsBlock => Type == LayoutInputNodeType.Block;

    public LayoutInputNode? NextSibling => Box?.NextSibling != null
        ? new LayoutInputNode(Box.NextSibling, Type)
        : null;

    public ComputedStyle? Style => Box?.Dimensions?.Style;
}

/// <summary>
/// Min/max inline size calculation input for child nodes. Mirrors
/// MinMaxSizesFloatInput in layout_input_node.h.
/// </summary>
public readonly struct MinMaxSizesFloatInput
{
    public float FloatLeftInlineSize { get; }
    public float FloatRightInlineSize { get; }

    public MinMaxSizesFloatInput(float floatLeftInlineSize = 0, float floatRightInlineSize = 0)
    {
        FloatLeftInlineSize = floatLeftInlineSize;
        FloatRightInlineSize = floatRightInlineSize;
    }
}

/// <summary>
/// Represents a block node to be laid out. Mirrors block_node.h.
/// </summary>
public class BlockNode : LayoutInputNode
{
    public BlockNode(LayoutBox? box, LayoutObject? layoutObject = null)
        : base(box, LayoutInputNodeType.Block, layoutObject)
    {
    }

    public static BlockNode Null => new(null);

    public LayoutResult Layout(ConstraintSpace constraintSpace, BlockBreakToken? breakToken = null)
    {
        if (Box == null)
            return new LayoutResult();

        var algorithm = CreateBlockAlgorithm();
        return algorithm?.Layout() ?? new LayoutResult();
    }

    public MinMaxSizesResult ComputeMinMaxSizes(WritingMode containerWritingMode, ConstraintSpace space)
    {
        using (new DisableLayoutSideEffectsScope())
        {
            var algorithm = CreateBlockAlgorithm();
            if (algorithm == null)
                return new MinMaxSizesResult(new MinMaxSizes(0, float.MaxValue));
            var result = algorithm.Layout();
            float min = result.Fragment.InlineSize;
            float max = result.Fragment.InlineSize;
            return new MinMaxSizesResult(new MinMaxSizes(min, max));
        }
    }

    public LayoutInputNode? FirstChild()
    {
        if (Box?.Children.Count == 0) return null;
        var first = Box?.Children.Count > 0 ? Box.Children[0] : null;
        return first != null ? new BlockNode(first) : null;
    }

    public bool IsFloating => Box?.IsFloating ?? false;
    public bool IsOutOfFlowPositioned => false;

    private LayoutAlgorithm? CreateBlockAlgorithm()
    {
        // Bridge to the existing element-based algorithms when possible.
        var element = LayoutObject?.Node as Element;
        if (element == null && Box != null)
            return null;
        return new BlockLayoutAlgorithm(element!, new ConstraintSpace());
    }
}