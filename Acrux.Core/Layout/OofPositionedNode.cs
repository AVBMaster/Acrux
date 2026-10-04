using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Holds the containing block for an out-of-flow positioned element when the
/// containing block is a non-atomic inline (the continuation root). The
/// relative offset is applied after fragmentation.
/// Mirrors OofInlineContainer in oof_positioned_node.h.
/// </summary>
public readonly struct OofInlineContainer<T>
{
    public LayoutInline? Container { get; }
    public T RelativeOffset { get; }

    public OofInlineContainer(LayoutInline? container, T relativeOffset)
    {
        Container = container;
        RelativeOffset = relativeOffset;
    }

    public static OofInlineContainer<T> Empty => new(null, default!);
}

/// <summary>
/// A physical out-of-flow positioned node: an element with
/// "position: absolute" or "position: fixed" that has not been bubbled up to
/// its containing block yet. As soon as it reaches its containing block, it
/// gets placed and does not bubble further up the tree.
/// Mirrors PhysicalOofPositionedNode in oof_positioned_node.h.
/// </summary>
public readonly struct PhysicalOofPositionedNode
{
    public LayoutBox Box { get; }
    public PhysicalOffset StaticPositionOffset { get; }
    public LogicalStaticPosition.StaticInlinePosition HorizontalEdge { get; }
    public LogicalStaticPosition.StaticBlockPosition VerticalEdge { get; }
    public bool RequiresContentBeforeBreaking { get; }
    public bool IsHiddenForPaint { get; }
    public OofInlineContainer<PhysicalOffset> InlineContainer { get; }

    public PhysicalOofPositionedNode(LayoutBox box, LogicalStaticPosition staticPosition, bool requiresContentBeforeBreaking, bool isHiddenForPaint,
        OofInlineContainer<PhysicalOffset> inlineContainer = default)
    {
        Box = box;
        StaticPositionOffset = staticPosition.Offset.ConvertToPhysical(staticPosition.WritingDirection);
        HorizontalEdge = staticPosition.InlinePosition;
        VerticalEdge = staticPosition.BlockPosition;
        RequiresContentBeforeBreaking = requiresContentBeforeBreaking;
        IsHiddenForPaint = isHiddenForPaint;
        InlineContainer = inlineContainer;
    }
}

/// <summary>
/// The logical version of <see cref="PhysicalOofPositionedNode"/>. Used within
/// an algorithm pass; its logical coordinate system is relative to the
/// container builder's writing-mode. Only used within one pass and is not
/// stored/persisted. Mirrors LogicalOofPositionedNode in oof_positioned_node.h.
/// </summary>
public readonly struct LogicalOofPositionedNode
{
    public LayoutBox Box { get; }
    public LogicalStaticPosition StaticPosition { get; }
    public bool RequiresContentBeforeBreaking { get; }
    public bool IsHiddenForPaint { get; }
    public OofInlineContainer<LogicalOffset> InlineContainer { get; }

    public LogicalOofPositionedNode(LayoutBox box, LogicalStaticPosition staticPosition, bool requiresContentBeforeBreaking, bool isHiddenForPaint,
        OofInlineContainer<LogicalOffset> inlineContainer = default)
    {
        Box = box;
        StaticPosition = staticPosition;
        RequiresContentBeforeBreaking = requiresContentBeforeBreaking;
        IsHiddenForPaint = isHiddenForPaint;
        InlineContainer = inlineContainer;
    }
}