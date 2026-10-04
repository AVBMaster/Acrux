using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class FieldsetLayoutAlgorithm : LayoutAlgorithm
{
    private readonly WritingDirectionMode _writingDirection;
    private float _intrinsicBlockSize;
    private float _consumedBlockSize;
    private float _borderBoxInlineSize;
    private float _borderBoxBlockSize;
    private float _minimumBorderBoxBlockSize;
    private BlockBreakToken? _breakToken;

    public FieldsetLayoutAlgorithm(Element node, in ConstraintSpace space, BlockBreakToken? breakToken = null)
        : base(node, space)
    {
        _writingDirection = new WritingDirectionMode(
            (Geometry.WritingMode)space.WritingMode,
            node.ComputedStyle?.Direction == "rtl" ? TextDirection.Rtl : TextDirection.Ltr);
        _consumedBlockSize = breakToken?.ConsumedBlockSize ?? 0;
        _breakToken = breakToken;
        _borderBoxInlineSize = float.IsNaN(space.AvailableInlineSize) ? ChildAvailableInlineSize : space.AvailableInlineSize;
        _borderBoxBlockSize = float.IsNaN(space.AvailableBlockSize) ? ChildAvailableBlockSize : space.AvailableBlockSize;
    }

    public override LayoutResult Layout()
    {
        var border = LengthUtils.ComputeBorders(Style);
        var padding = LengthUtils.ComputePadding(Space, Style);
        var bp = new BoxStrut(border.Top + padding.Top, border.Right + padding.Right,
            border.Bottom + padding.Bottom, border.Left + padding.Left);

        Builder.BorderLeft = border.Left; Builder.BorderTop = border.Top;
        Builder.BorderRight = border.Right; Builder.BorderBottom = border.Bottom;
        Builder.PaddingLeft = padding.Left; Builder.PaddingTop = padding.Top;
        Builder.PaddingRight = padding.Right; Builder.PaddingBottom = padding.Bottom;
        Builder.Element = Node;

        _intrinsicBlockSize = BorderTop;

        BreakStatus breakStatus = LayoutChildren();
        if (breakStatus == BreakStatus.NeedsEarlierBreak)
            return new LayoutResult { Status = EStatus.NeedsEarlierBreak };

        _intrinsicBlockSize = Math.Max(_intrinsicBlockSize + BorderBottom, bp.VerticalSum);

        float blockSize = LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, _intrinsicBlockSize, _borderBoxInlineSize);
        if (float.IsNaN(blockSize))
            blockSize = _intrinsicBlockSize;

        if (Style.Contain is not ContainType.Size and not ContainType.Strict)
            blockSize = Math.Max(blockSize, _minimumBorderBoxBlockSize);

        float allFragmentsBlockSize = blockSize;

        Builder.IntrinsicBlockSize = _intrinsicBlockSize;
        Builder.BlockSize = allFragmentsBlockSize;
        Builder.InlineSize = _borderBoxInlineSize;

        var frag = Builder.ToBoxFragment();
        frag.Children.AddRange(Builder.Children);
        return LayoutResult.FromFragment(frag);
    }

    private BreakStatus LayoutChildren()
    {
        BlockBreakToken? contentBreakToken = null;
        bool hasSeenAllChildren = false;
        if (_breakToken is not null)
        {
            var childTokens = _breakToken.ChildBreakTokens;
            if (childTokens.Count > 0)
            {
                contentBreakToken = _breakToken;
            }
            if (_breakToken.HasSeenAllChildren)
            {
                Builder.HasSeenAllChildren = true;
                hasSeenAllChildren = true;
            }
        }

        float legendSizeContribution = 0;
        Element? renderedLegend = GetRenderedLegend();
        if (renderedLegend is not null)
        {
            if (!FragmentationUtils.IsBreakInside(_breakToken))
            {
                LayoutLegend(renderedLegend);
            }

            if (FragmentationUtils.IsBreakInside(_breakToken))
            {
                legendSizeContribution = 0;
            }
            else
            {
                legendSizeContribution = _intrinsicBlockSize - BorderTop;
            }

            float adjustedPaddingBoxBlockSize = _borderBoxBlockSize - BorderTop - BorderBottom;
            if (!float.IsNaN(adjustedPaddingBoxBlockSize) && adjustedPaddingBoxBlockSize >= 0)
            {
                float paddingBlockSum = PaddingTop + PaddingBottom;
                adjustedPaddingBoxBlockSize = Math.Max(adjustedPaddingBoxBlockSize - legendSizeContribution, paddingBlockSum);
            }

            if (Style.Contain is not ContainType.Size and not ContainType.Strict)
            {
                _minimumBorderBoxBlockSize = BorderTop + BorderBottom + PaddingTop + PaddingBottom + legendSizeContribution;
            }

            if (contentBreakToken is not null || !hasSeenAllChildren)
            {
                Element? fieldsetContent = GetFieldsetContent();
                if (fieldsetContent is not null)
                {
                    BreakStatus breakStatus = LayoutFieldsetContent(fieldsetContent, contentBreakToken, true, adjustedPaddingBoxBlockSize);
                    if (breakStatus == BreakStatus.NeedsEarlierBreak)
                        return breakStatus;
                }
            }
        }
        else if (contentBreakToken is not null || !hasSeenAllChildren)
        {
            Element? fieldsetContent = GetFieldsetContent();
            if (fieldsetContent is not null)
            {
                float adjustedPaddingBoxBlockSize = _borderBoxBlockSize - BorderTop - BorderBottom;
                BreakStatus breakStatus = LayoutFieldsetContent(fieldsetContent, contentBreakToken, false, adjustedPaddingBoxBlockSize);
                if (breakStatus == BreakStatus.NeedsEarlierBreak)
                    return breakStatus;
            }
        }

        return BreakStatus.Continue;
    }

    private void LayoutLegend(Element legend)
    {
        var legendStyle = legend.ComputedStyle ?? new ComputedStyle();
        float percentageInlineSize = Space.PercentageResolutionInlineSize;
        float childAvailableInline = ChildAvailableInlineSize;

        var legendSpace = CreateConstraintSpaceForLegend(legend, childAvailableInline, percentageInlineSize);
        LayoutResult result = LayoutChildElement(legend, legendSpace);

        float legendBorderBoxBlockSize = result.Fragment.BlockSize;
        float legendMarginTop = legendStyle.MarginTop.ToPixels(legendStyle.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
        float legendMarginBottom = legendStyle.MarginBottom.ToPixels(legendStyle.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
        float legendMarginBoxBlockSize = legendMarginTop + legendBorderBoxBlockSize + legendMarginBottom;

        float spaceLeft = BorderTop - legendBorderBoxBlockSize;
        float blockOffset = 0;
        if (spaceLeft > 0)
            blockOffset += spaceLeft / 2;

        float legendMarginEndOffset = blockOffset + legendMarginBoxBlockSize - legendMarginTop;
        if (legendMarginEndOffset > BorderTop)
            _intrinsicBlockSize = legendMarginEndOffset;

        float legendBorderBoxInlineSize = result.Fragment.InlineSize;
        float legendMarginLeft = legendStyle.MarginLeft.ToPixels(legendStyle.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
        float legendMarginRight = legendStyle.MarginRight.ToPixels(legendStyle.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);

        float legendInlineStart = BorderLeft + PaddingLeft + legendMarginLeft;

        float availableSpace = childAvailableInline - legendBorderBoxInlineSize;
        if (availableSpace > 0)
        {
            var alignment = ComputeLegendBlockAlignment(legendStyle);
            if (alignment == LegendBlockAlignment.Center)
                legendInlineStart += availableSpace / 2;
            else if (alignment == LegendBlockAlignment.End)
                legendInlineStart += availableSpace - legendMarginRight;
        }

        result.Fragment.InlineOffset = legendInlineStart;
        result.Fragment.BlockOffset = blockOffset;
        result.Fragment.MarginLeft = legendMarginLeft;
        result.Fragment.MarginTop = legendMarginTop;
        result.Fragment.MarginRight = legendMarginRight;
        result.Fragment.MarginBottom = legendMarginBottom;
        Builder.AddChild(result.Fragment);
    }

    private BreakStatus LayoutFieldsetContent(Element fieldsetContent, BlockBreakToken? contentBreakToken, bool hasLegend, float adjustedPaddingBoxBlockSize)
    {
        var contentStyle = fieldsetContent.ComputedStyle ?? new ComputedStyle();

        LayoutResult? result = null;
        bool isPastEnd = _breakToken is not null && _breakToken.IsAtBlockEnd;

        float maxContentBlockSize = float.MaxValue;
        if (float.IsNaN(adjustedPaddingBoxBlockSize))
        {
            var maxHeight = Style.MaxHeight;
            if (maxHeight is PixelLength ph)
                maxContentBlockSize = ph.Value;
        }

        if (!isPastEnd || maxContentBlockSize == float.MaxValue)
        {
            var childSpace = CreateConstraintSpaceForFieldsetContent(fieldsetContent, adjustedPaddingBoxBlockSize, _intrinsicBlockSize);
            result = LayoutChildElement(fieldsetContent, childSpace);
        }

        if (maxContentBlockSize != float.MaxValue && (result is null || result.Status == EStatus.Success))
        {
            if (maxContentBlockSize > PaddingTop + PaddingBottom)
            {
                maxContentBlockSize = Math.Max(maxContentBlockSize - (_intrinsicBlockSize + BorderBottom), PaddingTop + PaddingBottom);
            }

            if (result is not null)
            {
                float totalBlockSize = result.Fragment.BlockSize;
                if (contentBreakToken is not null)
                    totalBlockSize += contentBreakToken.ConsumedBlockSize;
                if (totalBlockSize >= maxContentBlockSize)
                    result = null;
            }

            if (result is null)
            {
                adjustedPaddingBoxBlockSize = maxContentBlockSize;
                var adjustedChildSpace = CreateConstraintSpaceForFieldsetContent(fieldsetContent, adjustedPaddingBoxBlockSize, _intrinsicBlockSize);
                result = LayoutChildElement(fieldsetContent, adjustedChildSpace);
            }
        }

        if (result is null)
            return BreakStatus.Continue;

        result.Fragment.InlineOffset = BorderLeft;
        result.Fragment.BlockOffset = _intrinsicBlockSize;
        Builder.AddChild(result.Fragment);

        if (result.Fragment.BlockSize > 0)
            Builder.Baseline = _intrinsicBlockSize + result.Fragment.BlockSize;

        _intrinsicBlockSize += result.Fragment.BlockSize;
        Builder.HasSeenAllChildren = true;

        return BreakStatus.Continue;
    }

    private ConstraintSpace CreateConstraintSpaceForLegend(Element legend, float availableSize, float percentageSize)
    {
        return Space.InheritBuilder(availableSize, ChildAvailableBlockSize)
            .SetPercentageResolution(percentageSize, Space.PercentageResolutionBlockSize)
            .SetIsNewFormattingContext(true)
            .ToConstraintSpace();
    }

    private ConstraintSpace CreateConstraintSpaceForFieldsetContent(Element fieldsetContent, float paddingBoxBlockSize, float blockOffset)
    {
        float inlineSize = _borderBoxInlineSize - BorderLeft - BorderRight;
        float blockSize = float.IsNaN(paddingBoxBlockSize) ? float.PositiveInfinity : paddingBoxBlockSize;

        var builder = Space.InheritBuilder(inlineSize, blockSize)
            .SetIsNewFormattingContext(true)
            .SetPercentageResolution(Space.PercentageResolutionInlineSize, Space.PercentageResolutionBlockSize);

        if (!float.IsNaN(paddingBoxBlockSize))
            builder.SetIsFixedBlockSize(true);

        return builder.ToConstraintSpace();
    }

    private LayoutResult LayoutChildElement(Element child, ConstraintSpace space)
    {
        var disp = child.ComputedStyle?.Display ?? DisplayType.Block;
        if (disp is DisplayType.Flex or DisplayType.InlineFlex)
            return new FlexLayoutAlgorithm(child, space).Layout();
        var algo = new BlockLayoutAlgorithm(child, space);
        return algo.Layout();
    }

    private Element? GetRenderedLegend()
    {
        foreach (var child in Node.Children)
        {
            if (child is Element el && el.TagName == "LEGEND")
                return el;
        }
        return null;
    }

    private Element? GetFieldsetContent()
    {
        foreach (var child in Node.Children)
        {
            if (child is Element el && el.TagName != "LEGEND")
                return el;
        }
        return null;
    }

    private float FragmentainerSpaceAvailable()
    {
        return Math.Max(0, FragmentainerSpaceLeftForChildren() - _intrinsicBlockSize);
    }

    private void ConsumeRemainingFragmentainerSpace()
    {
        if (Space.HasDefiniteBlockSize)
            _intrinsicBlockSize += FragmentainerSpaceAvailable();
    }

    private static float FragmentainerSpaceLeftForChildren() => float.MaxValue;

    private enum LegendBlockAlignment { Start, Center, End }

    private LegendBlockAlignment ComputeLegendBlockAlignment(ComputedStyle legendStyle)
    {
        bool startAuto = legendStyle.MarginLeft is AutoLength;
        bool endAuto = legendStyle.MarginRight is AutoLength;
        if (startAuto || endAuto)
        {
            if (startAuto)
                return endAuto ? LegendBlockAlignment.Center : LegendBlockAlignment.End;
            return LegendBlockAlignment.Start;
        }
        bool isLtr = Style.Direction == "ltr";
        return legendStyle.TextAlign switch
        {
            TextAlignType.Left => isLtr ? LegendBlockAlignment.Start : LegendBlockAlignment.End,
            TextAlignType.Right => isLtr ? LegendBlockAlignment.End : LegendBlockAlignment.Start,
            TextAlignType.Center => LegendBlockAlignment.Center,
            _ => LegendBlockAlignment.Start,
        };
    }

    public MinMaxSizesResult ComputeMinMaxSizes(MinMaxSizesFloatInput input)
    {
        var result = new MinMaxSizesResult();
        var border = LengthUtils.ComputeBorders(Style);
        var padding = LengthUtils.ComputePadding(Space, Style);

        bool hasInlineSizeContainment = Style.Contain is ContainType.Size or ContainType.Strict;
        if (hasInlineSizeContainment)
        {
            result.Sizes = new MinMaxSizes(border.HorizontalSum + padding.HorizontalSum, border.HorizontalSum + padding.HorizontalSum);
            return result;
        }

        Element? renderedLegend = GetRenderedLegend();
        if (renderedLegend is not null)
        {
            var legendStyle = renderedLegend.ComputedStyle ?? new ComputedStyle();
            var legendSpace = CreateConstraintSpaceForLegend(renderedLegend, ChildAvailableInlineSize, Space.PercentageResolutionInlineSize);
            var legendResult = LayoutChildElement(renderedLegend, legendSpace);
            float legendMin = legendResult.Fragment.InlineSize;
            float legendMax = legendResult.Fragment.InlineSize;
            float legendMarginSum = 0;
            if (legendStyle.MarginLeft is not AutoLength)
                legendMarginSum += legendStyle.MarginLeft.ToPixels(legendStyle.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
            if (legendStyle.MarginRight is not AutoLength)
                legendMarginSum += legendStyle.MarginRight.ToPixels(legendStyle.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
            result.Sizes = new MinMaxSizes(legendMin + legendMarginSum, legendMax + legendMarginSum);
        }
        else
        {
            result.Sizes = MinMaxSizes.Zero;
        }

        result.Sizes = new MinMaxSizes(
            result.Sizes.MinSize + padding.HorizontalSum,
            result.Sizes.MaxSize + padding.HorizontalSum);

        if (!hasInlineSizeContainment)
        {
            Element? content = GetFieldsetContent();
            if (content is not null)
            {
                var contentStyle = content.ComputedStyle ?? new ComputedStyle();
                var contentSpace = CreateConstraintSpaceForFieldsetContent(content, float.NaN, 0);
                var contentResult = LayoutChildElement(content, contentSpace);
                float contentMin = contentResult.Fragment.InlineSize;
                float contentMax = contentResult.Fragment.InlineSize;
                float contentMarginSum = 0;
                if (contentStyle.MarginLeft is not AutoLength)
                    contentMarginSum += contentStyle.MarginLeft.ToPixels(contentStyle.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
                if (contentStyle.MarginRight is not AutoLength)
                    contentMarginSum += contentStyle.MarginRight.ToPixels(contentStyle.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
                var contentSizes = new MinMaxSizes(contentMin + contentMarginSum, contentMax + contentMarginSum);
                result.Sizes = new MinMaxSizes(
                    Math.Max(result.Sizes.MinSize, contentSizes.MinSize),
                    Math.Max(result.Sizes.MaxSize, contentSizes.MaxSize));
            }
        }

        result.Sizes = new MinMaxSizes(
            result.Sizes.MinSize + border.HorizontalSum,
            result.Sizes.MaxSize + border.HorizontalSum);

        return result;
    }
}