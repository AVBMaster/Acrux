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
        var result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
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
}
