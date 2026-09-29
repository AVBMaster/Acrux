using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Intrinsic size measurements shared by the formatting contexts that need them
/// (flexbox §4.5 / grid §6.7 automatic minimum size, sizing keywords).
/// </summary>
public static class IntrinsicMeasure
{
    /// <summary>Measures an element's min-content inline size by laying it out in a
    /// 1px-wide constraint space, where every break opportunity is taken.</summary>
    public static float MinContentInlineSize(Element element)
    {
        if (element.ComputedStyle == null)
            return 0;

        var space = new ConstraintSpace(
            availableInlineSize: 1f,
            availableBlockSize: float.PositiveInfinity,
            isFixedInlineSize: true,
            isFixedBlockSize: false);
        // Same rule as the block path: 'break-word' must not break during
        // min-content measurement (CSS Text 3 §4.3).
        bool previousProbe = InlineLayoutAlgorithm.InMinContentProbe;
        InlineLayoutAlgorithm.SetMinContentProbe(true);
        LayoutResult result;
        try
        {
            result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
        }
        finally
        {
            InlineLayoutAlgorithm.SetMinContentProbe(previousProbe);
        }
        var fragment = result.Fragment;
        if (fragment == null)
            return 0;

        // The widest line is what cannot be broken any further.
        float widest = 0;
        foreach (var line in fragment.Lines)
            widest = Math.Max(widest, line.InlineSize);
        if (widest <= 0)
            widest = fragment.InlineSize;
        return widest + fragment.BorderLeft + fragment.BorderRight
            + fragment.PaddingLeft + fragment.PaddingRight;
    }

    /// <summary>Measures an element's max-content inline size by laying it out in a
    /// very wide constraint space (so nothing wraps) and taking the widest line across
    /// the whole fragment tree. Nested block/inline children are included, unlike a
    /// shallow child scan.</summary>
    public static float MaxContentInlineSize(Element element)
    {
        if (element.ComputedStyle == null)
            return 0;

        var space = new ConstraintSpace(
            availableInlineSize: 100000f,
            availableBlockSize: float.PositiveInfinity,
            isFixedInlineSize: false,
            isFixedBlockSize: false);
        LayoutResult result;
        try
        {
            result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
        }
        catch
        {
            return 0;
        }
        var fragment = result.Fragment;
        if (fragment == null)
            return 0;

        float widest = WidestLineInlineSize(fragment);
        if (widest <= 0)
            widest = fragment.InlineSize;
        return widest + fragment.BorderLeft + fragment.BorderRight
            + fragment.PaddingLeft + fragment.PaddingRight;
    }

    private static float WidestLineInlineSize(BoxFragment fragment)
    {
        float widest = 0;
        foreach (var line in fragment.Lines)
            widest = Math.Max(widest, line.InlineSize);
        foreach (var child in fragment.Children)
            widest = Math.Max(widest, WidestLineInlineSize(child));
        return widest;
    }

    /// <summary>Measures an element's content block size (height) when laid out at a
    /// wide inline size (so nothing wraps). Returns the content-box height; the caller
    /// adds border/padding. Used for a flex item's auto cross size when its text lives
    /// inside a nested element.</summary>
    public static float MaxContentBlockSize(Element element)
    {
        if (element.ComputedStyle == null)
            return 0;

        var space = new ConstraintSpace(
            availableInlineSize: 100000f,
            availableBlockSize: float.PositiveInfinity,
            isFixedInlineSize: false,
            isFixedBlockSize: false);
        LayoutResult result;
        try
        {
            result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
        }
        catch
        {
            return 0;
        }
        var fragment = result.Fragment;
        if (fragment == null)
            return 0;
        // fragment.BlockSize is the content-box height (border/padding are separate
        // fields), matching how the flex cross axis adds CrossAxisBorderPadding later.
        return fragment.BlockSize;
    }
}
