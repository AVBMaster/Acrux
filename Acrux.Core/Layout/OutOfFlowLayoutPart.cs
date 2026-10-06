using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using Acrux.Core.Layout.Inline;

namespace Acrux.Core.Layout;

/// <summary>
/// Static position of an out-of-flow positioned element. Mirrors
/// static_position.h.
/// </summary>
public readonly struct LogicalStaticPosition
{
    public enum StaticInlinePosition
    {
        Left, Center, Right,
    }

    public enum StaticBlockPosition
    {
        Top, Center, Bottom,
    }

    public LogicalOffset Offset { get; }
    public StaticInlinePosition InlinePosition { get; }
    public StaticBlockPosition BlockPosition { get; }
    public WritingDirectionMode WritingDirection { get; }

    public LogicalStaticPosition(LogicalOffset offset, StaticInlinePosition inlinePosition, StaticBlockPosition blockPosition, WritingDirectionMode writingDirection)
    {
        Offset = offset;
        InlinePosition = inlinePosition;
        BlockPosition = blockPosition;
        WritingDirection = writingDirection;
    }
}

/// <summary>
/// Containing block info for an out-of-flow node. Mirrors OofContainingBlock.
/// </summary>
public readonly struct OofContainingBlock
{
    public PhysicalOffset Offset { get; }
    public PhysicalSize Size { get; }

    public OofContainingBlock(PhysicalOffset offset, PhysicalSize size)
    {
        Offset = offset;
        Size = size;
    }
}

/// <summary>
/// A candidate for out-of-flow layout. Mirrors LogicalOofPositionedNode.
/// </summary>
public class OutOfFlowChildCandidate
{
    public LayoutBox Box { get; }
    public LogicalStaticPosition StaticPosition { get; }
    public bool IsAbsolute { get; set; }
    public bool IsFixed { get; set; }
    public bool IsHiddenForPaint { get; set; }
    public bool RequiresContentBeforeBreaking { get; set; }
    public OofInlineContainer<LogicalOffset> InlineContainer { get; set; }

    public OutOfFlowChildCandidate(LayoutBox box, LogicalStaticPosition staticPosition)
    {
        Box = box;
        StaticPosition = staticPosition;
        IsAbsolute = box.Parent != null && !box.IsFloating;
        IsHiddenForPaint = false;
        InlineContainer = OofInlineContainer<LogicalOffset>.Empty;
    }

    public LogicalOofPositionedNode ToLogicalNode() =>
        new(Box, StaticPosition, RequiresContentBeforeBreaking, IsHiddenForPaint, InlineContainer);
}

/// <summary>
/// Helper class for positioning out-of-flow blocks. It should be used together
/// with BoxFragmentBuilder. Mirrors out_of_flow_layout_part.cc.
/// </summary>
public class OutOfFlowLayoutPart
{
    private readonly BoxFragmentBuilder _containerBuilder;
    private readonly ConstraintSpace _space;
    private readonly List<OutOfFlowChildCandidate> _candidates = new();

    public OutOfFlowLayoutPart(BoxFragmentBuilder containerBuilder, in ConstraintSpace space = default)
    {
        _containerBuilder = containerBuilder;
        _space = space;
    }

    public void AddCandidate(OutOfFlowChildCandidate candidate) => _candidates.Add(candidate);

    /// <summary>
    /// Position and append all OOF children to the fragment builder. Mirrors
    /// OutOfFlowLayoutPart::Run().
    /// </summary>
    public void Run()
    {
        if (_candidates.Count == 0) return;

        float containerInline = _containerBuilder.InlineSize;
        float containerBlock = _containerBuilder.BlockSize;

        // The containing block of an absolutely-positioned child is the PADDING
        // box of the positioned ancestor; fragment offsets are relative to the
        // content box origin (the converter adds the parent's content origin),
        // hence the "- padding" shift applied to every resolved inset below.
        float contentInline = Math.Max(0, containerInline - _containerBuilder.BorderLeft - _containerBuilder.BorderRight
            - _containerBuilder.PaddingLeft - _containerBuilder.PaddingRight);
        float contentBlock = Math.Max(0, containerBlock - _containerBuilder.BorderTop - _containerBuilder.BorderBottom
            - _containerBuilder.PaddingTop - _containerBuilder.PaddingBottom);
        float padBoxInline = contentInline + _containerBuilder.PaddingLeft + _containerBuilder.PaddingRight;
        float padBoxBlock = contentBlock + _containerBuilder.PaddingTop + _containerBuilder.PaddingBottom;
        var paddingBoxSize = new LogicalSize(padBoxInline, padBoxBlock);

        foreach (var candidate in _candidates)
        {
            if (candidate.IsHiddenForPaint) continue;
            var style = GetStyleOf(candidate.Box);
            if (style == null) continue;

            // Border/padding of the OOF box itself: the resolved width/height are
            // content-box values (content-box is the CSS default), so the border
            // box extent is width/height + borders + padding.
            var border = LengthUtils.ComputeBorders(style);
            var padding = LengthUtils.ComputePadding(_space, style);
            var bp = new BoxStrut(
                border.Top + padding.Top, border.Right + padding.Right,
                border.Bottom + padding.Bottom, border.Left + padding.Left);

            // Compute the border box size of the OOF box.
            var (inlineSize, blockSize) = AbsoluteUtils.ComputeOutOfFlowSize(style, paddingBoxSize, bp);

            // An auto size on an absolutely positioned box is shrink-to-fit
            // (CSS 2.1 §10.3.7), not the containing block's extent; only a box
            // with both insets on the axis stretches instead.
            bool autoInline = style.Width is AutoLength &&
                !(style.Left is not AutoLength && style.Right is not AutoLength);
            bool autoBlock = style.Height is AutoLength &&
                !(style.Top is not AutoLength && style.Bottom is not AutoLength);
            bool hasDefiniteBlock = !autoBlock;

            // Create the fragment for this OOF box. The element is laid out with
            // its real algorithm (block/flex/grid/replaced) so children render,
            // then sized to the computed border box above.
            var element = GetElementOf(candidate.Box);
            BoxFragment fragment;
            if (element != null)
            {
                float contentInline2 = Math.Max(0, inlineSize - bp.HorizontalSum);
                float contentBlock2 = Math.Max(0, blockSize - bp.VerticalSum);
                var childSpace = new ConstraintSpace(
                    availableInlineSize: contentInline2,
                    availableBlockSize: hasDefiniteBlock ? contentBlock2 : float.PositiveInfinity,
                    isFixedInlineSize: !autoInline,
                    isFixedBlockSize: hasDefiniteBlock,
                    isShrinkToFit: autoInline
                );
                fragment = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, childSpace).Fragment;

                if (autoInline)
                    inlineSize = Math.Min(paddingBoxSize.InlineSize, fragment.InlineSize);
                if (autoBlock)
                    blockSize = fragment.BlockSize;

                fragment.InlineSize = inlineSize;
                fragment.BlockSize = blockSize;
            }
            else
            {
                fragment = new BoxFragment
                {
                    InlineSize = inlineSize,
                    BlockSize = blockSize,
                    Element = element,
                };
            }

            // Resolve the box origin. The returned offsets are relative to the
            // container's CONTENT box (the convention fragment offsets use).
            var (x, y) = ComputePosition(style, inlineSize, blockSize, paddingBoxSize, candidate, _containerBuilder);
            fragment.InlineOffset = x;
            fragment.BlockOffset = y;
            fragment.IsOutOfFlowPositioned = true;

            _containerBuilder.Children.Add(fragment);
        }
    }

    /// <summary>
    /// Resolve the OOF box's border-box origin relative to the container's
    /// CONTENT box. This is CSS 2.1 §10.3.7 / §10.3.8 solved on each axis:
    /// auto margins are 0 unless the axis is over-constrained, an auto inset is
    /// whatever the equation leaves, a fully over-constrained axis drops 'right'
    /// in ltr and 'left' in rtl, and an axis with both insets auto falls back to
    /// the static position. Margins always count.
    /// </summary>
    private static (float x, float y) ComputePosition(ComputedStyle style, float inlineSize, float blockSize,
        LogicalSize paddingBoxSize, OutOfFlowChildCandidate candidate, BoxFragmentBuilder builder)
    {
        float padL = builder.PaddingLeft, padT = builder.PaddingTop;
        // The static position and the over-constrained tie-break follow the CONTAINING
        // block's 'direction', not the box's own: an 'ltr' abspos child of an 'rtl'
        // container still starts at the container's inline-start (right) edge.
        bool rtl = candidate.StaticPosition.WritingDirection.Direction == TextDirection.Rtl;
        float cbInline = paddingBoxSize.InlineSize;
        float cbBlock = paddingBoxSize.BlockSize;
        float fontSize = style.FontSize;

        float x = ResolveInlineAxis(style, rtl, inlineSize, cbInline, fontSize, padL, candidate, builder);
        float y = ResolveBlockAxis(style, blockSize, cbBlock, fontSize, padT, candidate);
        return (x, y);
    }

    private static float ResolveInlineAxis(ComputedStyle style, bool rtl, float inlineSize,
        float cbInline, float fontSize, float padL, OutOfFlowChildCandidate candidate,
        BoxFragmentBuilder builder)
    {
        bool leftAuto = style.Left is AutoLength, rightAuto = style.Right is AutoLength;
        bool widthAuto = style.Width is AutoLength;
        bool mlAuto = style.MarginLeft is AutoLength, mrAuto = style.MarginRight is AutoLength;
        float ml = mlAuto ? 0f : ResolveInset(style.MarginLeft, cbInline, fontSize);
        float mr = mrAuto ? 0f : ResolveInset(style.MarginRight, cbInline, fontSize);
        float left = leftAuto ? 0f : ResolveInset(style.Left, cbInline, fontSize);
        float right = rightAuto ? 0f : ResolveInset(style.Right, cbInline, fontSize);

        if (leftAuto && rightAuto)
        {
            // Static position: the recorded offset is the container's inline-start edge
            // as a left-based coordinate, so in rtl the box hangs to its left.
            float start = candidate.StaticPosition.Offset.InlineOffset - builder.BorderLeft - padL;
            return rtl ? start - inlineSize - mr : start + ml;
        }

        if (!leftAuto && !rightAuto && !widthAuto)
        {
            // Over-constrained: auto margins absorb what the equation leaves over,
            // then one inset is dropped depending on the direction.
            float surplus = cbInline - left - right - inlineSize - ml - mr;
            if (surplus > 0f)
            {
                if (mlAuto && mrAuto) { ml += surplus / 2f; mr += surplus / 2f; }
                else if (mlAuto) ml += surplus;
                else if (mrAuto) mr += surplus;
            }
            return rtl
                ? cbInline - right - mr - inlineSize - padL
                : left + ml - padL;
        }

        // Exactly one inset is auto: it absorbs the remaining space, so the box is
        // pinned by the definite side (plus that side's margin).
        return leftAuto
            ? cbInline - right - mr - inlineSize - padL
            : left + ml - padL;
    }

    private static float ResolveBlockAxis(ComputedStyle style, float blockSize,
        float cbBlock, float fontSize, float padT, OutOfFlowChildCandidate candidate)
    {
        bool topAuto = style.Top is AutoLength, bottomAuto = style.Bottom is AutoLength;
        bool heightAuto = style.Height is AutoLength;
        bool mtAuto = style.MarginTop is AutoLength, mbAuto = style.MarginBottom is AutoLength;
        float mt = mtAuto ? 0f : ResolveInset(style.MarginTop, cbBlock, fontSize);
        float mb = mbAuto ? 0f : ResolveInset(style.MarginBottom, cbBlock, fontSize);
        float top = topAuto ? 0f : ResolveInset(style.Top, cbBlock, fontSize);
        float bottom = bottomAuto ? 0f : ResolveInset(style.Bottom, cbBlock, fontSize);

        if (topAuto && bottomAuto)
            return candidate.StaticPosition.Offset.BlockOffset + mt;

        if (!topAuto && !bottomAuto && !heightAuto)
        {
            float surplus = cbBlock - top - bottom - blockSize - mt - mb;
            if (surplus > 0f)
            {
                if (mtAuto && mbAuto) { mt += surplus / 2f; mb += surplus / 2f; }
                else if (mtAuto) mt += surplus;
                else if (mbAuto) mb += surplus;
            }
            // §10.3.8: a vertical over-constrained equation drops 'bottom'.
            return top + mt - padT;
        }

        return topAuto ? cbBlock - bottom - mb - blockSize - padT : top + mt - padT;
    }

    // For a calc() inset, MathLength.ToPixels treats its first argument as the
    // percentage base, so it must be the containing-block size — not the font
    // size, which would resolve 'calc(50% - 10px)' against the glyph metrics.
    // Font-relative units inside the calc come from the ambient FontUnitContext.
    private static float ResolveInset(Length length, float basis, float fontSize) =>
        length is PercentLength pct
            ? pct.Value * basis
            : length.ToPixels(basis, fontSize, basis, basis);

    private static ComputedStyle? GetStyleOf(LayoutBox box) => box.Dimensions?.Style ?? null;
    private static Element? GetElementOf(LayoutBox box) => box.Dimensions?.Element ?? null;
}