using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Relative positioning utilities (CSS 2.1 §9.4). A relatively positioned box is
/// displaced from the position the flow gave it, and the displacement never moves the
/// flow itself. Insets resolve the way absolute insets do — percentages against the
/// containing block's size for their own axis — and an over-constrained axis keeps the
/// side the <em>flow</em> starts from: <c>top</c> always, <c>right</c> in an RTL flow and
/// <c>left</c> otherwise.
/// </summary>
public static class RelativeUtils
{
    /// <summary>Whether the flow a box sits in runs right-to-left — the direction that
    /// resolves its over-constrained insets. A box's own <c>direction</c> only governs its
    /// children, so an RTL box in an LTR flow still takes <c>left</c> (measured against the
    /// reference engine); the flow direction is the one inherited from the parent.</summary>
    public static bool FlowIsRtl(Element? node)
    {
        for (var parent = node?.ParentElement; parent != null; parent = parent.ParentElement)
            if (parent.ComputedStyle != null) return parent.ComputedStyle.IsInlineRightToLeft;
        return false;
    }

    public static LogicalOffset ComputeRelativeOffset(ComputedStyle style, WritingDirectionMode writingDirection, LogicalSize containingBlockSize)
    {
        var offset = Offset(style, writingDirection.Direction == TextDirection.Rtl,
            containingBlockSize.InlineSize, containingBlockSize.BlockSize,
            FontUnitContext.Current?.FontSize ?? 16, 0, 0);
        return new LogicalOffset(offset.Left, offset.Top);
    }

    public static PhysicalOffset ComputeRelativeOffsetForBoxFragment(Element node, ComputedStyle style, PhysicalSize containingBlockSize)
        => Offset(style, FlowIsRtl(node), containingBlockSize.Width, containingBlockSize.Height,
            FontUnitContext.Current?.FontSize ?? 16, 0, 0);

    public static PhysicalOffset ComputeRelativeOffsetForInline(Element node, ConstraintSpace space, ComputedStyle style)
        => Offset(style, FlowIsRtl(node), space.PercentageResolutionInlineSize, space.PercentageResolutionBlockSize,
            space.RootFontSize, space.ViewportWidth, space.ViewportHeight);

    /// <summary>
    /// The displacement for the box's own style. Percentages take the containing block's
    /// inline size for left/right and its block size for top/bottom. <paramref name="rtlFlow"/>
    /// is the direction of the flow the box is placed in, not the box's own.
    /// </summary>
    public static PhysicalOffset Offset(ComputedStyle style, bool rtlFlow, float containingInlineSize, float containingBlockSize,
        float rootFontSize, float viewportWidth, float viewportHeight)
    {
        if (style.Position != PositionType.Relative)
            return PhysicalOffset.Zero;

        var left = AbsoluteUtils.ResolveInset(style.Left, containingInlineSize, style, rootFontSize, viewportWidth, viewportHeight);
        var right = AbsoluteUtils.ResolveInset(style.Right, containingInlineSize, style, rootFontSize, viewportWidth, viewportHeight);
        var top = AbsoluteUtils.ResolveInset(style.Top, containingBlockSize, style, rootFontSize, viewportWidth, viewportHeight);
        var bottom = AbsoluteUtils.ResolveInset(style.Bottom, containingBlockSize, style, rootFontSize, viewportWidth, viewportHeight);

        float inlineOffset;
        if (rtlFlow && right.HasValue)
            inlineOffset = -right.Value;
        else if (left.HasValue)
            inlineOffset = left.Value;
        else if (right.HasValue)
            inlineOffset = -right.Value;
        else
            inlineOffset = 0;

        // A positive 'top' pushes the box down and a positive 'bottom' pulls it up.
        float blockOffset = top.HasValue ? top.Value : bottom.HasValue ? -bottom.Value : 0;
        return new PhysicalOffset(inlineOffset, blockOffset);
    }
}
