using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Out-of-flow positioning utilities. Mirrors absolute_utils.cc.
/// </summary>
public static class AbsoluteUtils
{
    public static (float left, float top, float right, float bottom) ComputeOutOfFlowInsets(
        ComputedStyle style, LogicalSize availableSize)
    {
        // Resolve pixel, percentage, em and calc() insets. Percentages resolve
        // against the containing block's inline (left/right) or block (top/bottom)
        // size; auto and anything else contribute 0 (the caller gates on specified).
        return (
            ResolveInsetLength(style.Left, availableSize.InlineSize, style),
            ResolveInsetLength(style.Top, availableSize.BlockSize, style),
            ResolveInsetLength(style.Right, availableSize.InlineSize, style),
            ResolveInsetLength(style.Bottom, availableSize.BlockSize, style));
    }

    private static float ResolveInsetLength(Length? length, float basis, ComputedStyle style) => length switch
    {
        PixelLength px => px.Value,
        PercentLength pct => pct.Value * basis,
        EmLength em => em.Value * style.FontSize,
        MathLength m => m.ToPixels(basis, style.FontSize, 0, 0),
        _ => 0,
    };

    /// <summary>Resolve insets from the style, returning nullopt for auto values.</summary>
    public static LogicalOofInsets ResolveOutOfFlowInsets(ComputedStyle style, LogicalSize availableSize)
    {
        return new LogicalOofInsets(
            style.Left is PixelLength pl ? pl.Value : null,
            style.Right is PixelLength pr ? pr.Value : null,
            style.Top is PixelLength pt ? pt.Value : null,
            style.Bottom is PixelLength pb ? pb.Value : null
        );
    }

    public static (float inlineSize, float blockSize) ComputeOutOfFlowSize(
        ComputedStyle style, LogicalSize availableSize, BoxStrut borderPadding)
    {
        var (left, top, right, bottom) = ComputeOutOfFlowInsets(style, availableSize);
        bool leftSpecified = style.Left is PixelLength or PercentLength;
        bool rightSpecified = style.Right is PixelLength or PercentLength;
        bool topSpecified = style.Top is PixelLength or PercentLength;
        bool bottomSpecified = style.Bottom is PixelLength or PercentLength;

        float inlineSize = availableSize.InlineSize;
        float blockSize = availableSize.BlockSize;
        bool borderBox = style.BoxSizing == BoxSizingType.BorderBox;

        if (style.Width is PixelLength pw)
        {
            inlineSize = borderBox
                ? Math.Max(borderPadding.HorizontalSum, pw.Value)
                : pw.Value + borderPadding.HorizontalSum;
        }
        else if (style.Width is PercentLength pctW)
        {
            float baseSize = pctW.Value * availableSize.InlineSize;
            inlineSize = borderBox
                ? Math.Max(borderPadding.HorizontalSum, baseSize)
                : baseSize + borderPadding.HorizontalSum;
        }
        else if (leftSpecified && rightSpecified)
            inlineSize = Math.Max(0, availableSize.InlineSize - left - right);

        if (style.Height is PixelLength ph)
        {
            blockSize = borderBox
                ? Math.Max(borderPadding.VerticalSum, ph.Value)
                : ph.Value + borderPadding.VerticalSum;
        }
        else if (style.Height is PercentLength pctH)
        {
            float baseSize = pctH.Value * availableSize.BlockSize;
            blockSize = borderBox
                ? Math.Max(borderPadding.VerticalSum, baseSize)
                : baseSize + borderPadding.VerticalSum;
        }
        else if (topSpecified && bottomSpecified)
            blockSize = Math.Max(0, availableSize.BlockSize - top - bottom);

        return (inlineSize, blockSize);
    }

    public static (float x, float y) ComputeOutOfFlowPosition(
        ComputedStyle style, float inlineSize, float blockSize, LogicalSize availableSize)
    {
        var (left, top, right, bottom) = ComputeOutOfFlowInsets(style, availableSize);

        float x = left;
        float y = top;

        if (style.Right is PixelLength pr && style.Left is AutoLength)
            x = availableSize.InlineSize - inlineSize - pr.Value;
        if (style.Bottom is PixelLength pb && style.Top is AutoLength)
            y = availableSize.BlockSize - blockSize - pb.Value;

        return (x, y);
    }

    /// <summary>
    /// Compute the inset-modified containing block (IMCB) for resolving size,
    /// margins, and final position of an OOF node.
    /// Mirrors ComputeInsetModifiedContainingBlock in absolute_utils.cc.
    /// </summary>
    public static InsetModifiedContainingBlock ComputeInsetModifiedContainingBlock(
        LogicalSize availableSize, LogicalOofInsets insets, LogicalStaticPosition staticPosition)
    {
        // Auto insets extend the IMCB to the containing block edge on that side.
        float inlineStart = insets.InlineStart ?? 0;
        float inlineEnd = insets.InlineEnd ?? 0;
        float blockStart = insets.BlockStart ?? 0;
        float blockEnd = insets.BlockEnd ?? 0;

        bool hasAutoInline = !insets.InlineStart.HasValue || !insets.InlineEnd.HasValue;
        bool hasAutoBlock = !insets.BlockStart.HasValue || !insets.BlockEnd.HasValue;

        return new InsetModifiedContainingBlock(availableSize, inlineStart, inlineEnd,
            blockStart, blockEnd, hasAutoInline, hasAutoBlock);
    }

    /// <summary>
    /// Compute the inline dimensions of an OOF positioned element.
    /// Mirrors ComputeOofInlineDimensions.
    /// </summary>
    public static LogicalOofDimensions ComputeOofInlineDimensions(
        ComputedStyle style, InsetModifiedContainingBlock imcb, BoxStrut borderPadding)
    {
        float imcbInline = imcb.InlineSize();
        float size = imcbInline;

        if (style.Width is PixelLength pw)
            size = pw.Value;
        else if (style.Width is PercentLength pct)
            size = pct.Value * imcbInline;
        else if (!imcb.HasAutoInlineInset)
            size = Math.Max(0, imcbInline);

        // Apply min/max inline size.
        float min = style.MinWidth is PixelLength mn ? mn.Value : 0;
        float max = style.MaxWidth is PixelLength mx ? mx.Value : float.MaxValue;
        size = Math.Clamp(size, min, max);

        var inset = new BoxStrut(imcb.BlockStart, imcb.InlineEnd, imcb.BlockEnd, imcb.InlineStart);
        return new LogicalOofDimensions(inset, new LogicalSize(size, 0), new BoxStrut(0, 0, 0, 0));
    }

    /// <summary>
    /// Compute the block dimensions of an OOF positioned element.
    /// Mirrors ComputeOofBlockDimensions.
    /// </summary>
    public static LogicalOofDimensions ComputeOofBlockDimensions(
        ComputedStyle style, InsetModifiedContainingBlock imcb, BoxStrut borderPadding,
        float intrinsicBlockSize = 0)
    {
        float imcbBlock = imcb.BlockSize();
        float size = imcbBlock;

        if (style.Height is PixelLength ph)
            size = ph.Value;
        else if (style.Height is PercentLength pct)
            size = pct.Value * imcbBlock;
        else if (intrinsicBlockSize > 0)
            size = intrinsicBlockSize;
        else if (!imcb.HasAutoBlockInset)
            size = Math.Max(0, imcbBlock);

        // Apply min/max block size.
        float min = style.MinHeight is PixelLength mn ? mn.Value : 0;
        float max = style.MaxHeight is PixelLength mx ? mx.Value : float.MaxValue;
        size = Math.Clamp(size, min, max);

        var inset = new BoxStrut(imcb.BlockStart, imcb.InlineEnd, imcb.BlockEnd, imcb.InlineStart);
        return new LogicalOofDimensions(inset, new LogicalSize(0, size), new BoxStrut(0, 0, 0, 0));
    }
}

/// <summary>
/// Resolved insets for an OOF positioned element. Mirrors LogicalOofInsets.
/// </summary>
public readonly struct LogicalOofInsets
{
    public float? InlineStart { get; }
    public float? InlineEnd { get; }
    public float? BlockStart { get; }
    public float? BlockEnd { get; }

    public LogicalOofInsets(float? inlineStart, float? inlineEnd, float? blockStart, float? blockEnd)
    {
        InlineStart = inlineStart; InlineEnd = inlineEnd;
        BlockStart = blockStart; BlockEnd = blockEnd;
    }
}

/// <summary>
/// The inset-modified containing block (IMCB) for resolving size, margins, and
/// final position of an OOF node. Mirrors InsetModifiedContainingBlock.
/// </summary>
public readonly struct InsetModifiedContainingBlock
{
    public LogicalSize AvailableSize { get; }
    public float InlineStart { get; }
    public float InlineEnd { get; }
    public float BlockStart { get; }
    public float BlockEnd { get; }
    public bool HasAutoInlineInset { get; }
    public bool HasAutoBlockInset { get; }

    public enum InsetBias { kStart, kEnd, kEqual }

    public InsetBias InlineInsetBias { get; }
    public InsetBias BlockInsetBias { get; }
    public InsetBias? InlineSafeInsetBias { get; }
    public InsetBias? BlockSafeInsetBias { get; }
    public InsetBias? InlineDefaultInsetBias { get; }
    public InsetBias? BlockDefaultInsetBias { get; }

    public InsetModifiedContainingBlock(LogicalSize availableSize, float inlineStart, float inlineEnd,
        float blockStart, float blockEnd, bool hasAutoInlineInset, bool hasAutoBlockInset,
        InsetBias inlineBias = InsetBias.kStart, InsetBias blockBias = InsetBias.kStart,
        InsetBias? inlineSafeBias = null, InsetBias? blockSafeBias = null,
        InsetBias? inlineDefaultBias = null, InsetBias? blockDefaultBias = null)
    {
        AvailableSize = availableSize; InlineStart = inlineStart; InlineEnd = inlineEnd;
        BlockStart = blockStart; BlockEnd = blockEnd;
        HasAutoInlineInset = hasAutoInlineInset; HasAutoBlockInset = hasAutoBlockInset;
        InlineInsetBias = inlineBias; BlockInsetBias = blockBias;
        InlineSafeInsetBias = inlineSafeBias; BlockSafeInsetBias = blockSafeBias;
        InlineDefaultInsetBias = inlineDefaultBias; BlockDefaultInsetBias = blockDefaultBias;
    }

    public float InlineEndOffset => AvailableSize.InlineSize - InlineEnd;
    public float BlockEndOffset => AvailableSize.BlockSize - BlockEnd;
    public float InlineSize() => Math.Max(0, AvailableSize.InlineSize - InlineStart - InlineEnd);
    public float BlockSize() => Math.Max(0, AvailableSize.BlockSize - BlockStart - BlockEnd);
    public LogicalSize Size() => new(InlineSize(), BlockSize());
}

/// <summary>
/// Dimensions of an OOF positioned element. Mirrors LogicalOofDimensions.
/// </summary>
public readonly struct LogicalOofDimensions
{
    public BoxStrut Inset { get; }
    public LogicalSize Size { get; }
    public BoxStrut Margins { get; }

    public LogicalOofDimensions(BoxStrut inset, LogicalSize size, BoxStrut margins)
    {
        Inset = inset; Size = size; Margins = margins;
    }

    public float MarginBoxInlineStart => Inset.Left - Margins.Left;
    public float MarginBoxBlockStart => Inset.Top - Margins.Top;
    public float MarginBoxInlineEnd => Inset.Left + Size.InlineSize + Margins.Right;
    public float MarginBoxBlockEnd => Inset.Top + Size.BlockSize + Margins.Bottom;
}

/// <summary>
/// Fragmentation utilities. Mirrors fragmentation_utils.cc.
/// Handles column/page break behavior.
/// </summary>
public static class FragmentationUtils
{
    public static bool IsForcedBreakValue(ConstraintSpace space, string breakValue)
    {
        return breakValue is "page" or "column" or "left" or "right" or "recto" or "verso";
    }

    public static bool IsAvoidBreakValue(ConstraintSpace space, string breakValue)
    {
        return breakValue == "avoid" || breakValue == "avoid-page" || breakValue == "avoid-column" || breakValue == "avoid-region";
    }

    public static bool IsBreakInside(BlockBreakToken? token)
    {
        return token != null && !token.IsBreakBefore && !token.IsRepeated;
    }

    public static bool InvolvedInBlockFragmentation(ConstraintSpace space, BlockBreakToken? previousBreakToken)
    {
        return space.HasDefiniteBlockSize || IsBreakInside(previousBreakToken);
    }

    /// <summary>Break-between values in order of increasing precedence.</summary>
    public static int FragmentainerBreakPrecedence(string breakValue)
    {
        switch (breakValue)
        {
            default:
            case "auto":
                return 0;
            case "avoid-column":
                return 1;
            case "avoid-page":
                return 2;
            case "avoid":
                return 4;
            case "column":
                return 5;
            case "page":
                return 6;
            case "left":
            case "recto":
                return 7;
            case "right":
            case "verso":
                return 8;
        }
    }

    /// <summary>Join two break values, keeping the one with higher precedence. Mirrors JoinFragmentainerBreakValues().</summary>
    public static string JoinFragmentainerBreakValues(string firstValue, string secondValue) =>
        FragmentainerBreakPrecedence(secondValue) >= FragmentainerBreakPrecedence(firstValue) ? secondValue : firstValue;
}

/// <summary>
/// Block break token. Mirrors block_break_token.cc.
/// </summary>
public class BlockBreakToken : BreakToken
{
    public bool IsForcedBreak { get; set; }
    public bool IsInParallelFlow { get; set; }
    public bool IsBreakBefore { get; set; }
    public bool IsRepeated { get; set; }
    public bool IsAtBlockEnd { get; set; }
    public bool HasSeenAllChildren { get; set; }
    public int SequenceNumber { get; set; }
    public float ConsumedBlockSize { get; set; }
    /// <summary>
    /// Index of the first line box to lay out when resuming an inline formatting
    /// context in a later fragmentainer (column/page). Line-level block
    /// fragmentation records where the previous fragmentainer stopped.
    /// </summary>
    public int ResumeLineIndex { get; set; }
    public Dom.LayoutBox? Node { get; set; }
    public List<ChildBreakToken> ChildBreakTokens { get; } = new();

    public ComputedStyle? StyleForInline => Node?.Dimensions?.Style;

    public static BlockBreakToken CreateRepeated(int sequenceNumber)
    {
        return new BlockBreakToken { IsRepeated = true, SequenceNumber = sequenceNumber };
    }

    public static BlockBreakToken CreateForBreakInRepeatedFragment(int sequenceNumber, float consumedBlockSize, bool isAtBlockEnd)
    {
        return new BlockBreakToken { IsRepeated = true, SequenceNumber = sequenceNumber, ConsumedBlockSize = consumedBlockSize, IsAtBlockEnd = isAtBlockEnd };
    }
}

/// <summary>
/// A break token for a child of a fragmented box.
/// </summary>
public class ChildBreakToken : BreakToken
{
    public bool IsAtEnd { get; set; }
}

/// <summary>
/// Break token base class. Mirrors break_token.cc.
/// </summary>
public class BreakToken
{
    public bool IsBreakBefore { get; set; }
    public bool IsRepeated { get; set; }
    public Dom.LayoutBox? Node { get; set; }
}

/// <summary>
/// Inline break token. Mirrors inline_break_token.cc.
/// </summary>
public class InlineBreakToken : BreakToken
{
    public int ItemIndex { get; set; }
    public int TextOffset { get; set; }
}