using Acrux.Core.Css;
using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// CSS Containment 3 §2.2: while size containment is on, the element's contents
/// contribute nothing to its intrinsic size, and <c>contain-intrinsic-*</c> supplies a
/// replacement. The replacement is the <em>content-box</em> size — measured against the
/// reference engine, <c>contain: size; padding: 10px; contain-intrinsic-width: 100px</c>
/// is a 120px box and <c>contain: size; border: 3px; contain-intrinsic-height: 30px</c>
/// is 36px, while the same box with no fallback measures exactly its border and padding.
/// Percentages are not accepted by the reference engine, so the parser never stores one.
/// </summary>
public static class ContainmentMetrics
{
    /// <summary>True when this box's own intrinsic sizes must be replaced. Kept next to the
    /// two fallback readers so no caller has to remember which styles imply containment.</summary>
    public static bool IsContained(ComputedStyle? style) => style != null && style.HasSizeContainment;

    /// <summary>The content-box inline size the contained axis measures, 0 without a fallback.</summary>
    public static float FallbackInlineSize(ComputedStyle style, ConstraintSpace space) =>
        Resolve(style.ContainIntrinsicWidth, style, space);

    /// <summary>The content-box block size the contained axis measures, 0 without a fallback.</summary>
    public static float FallbackBlockSize(ComputedStyle style, ConstraintSpace space) =>
        Resolve(style.ContainIntrinsicHeight, style, space);

    private static float Resolve(Length? length, ComputedStyle style, ConstraintSpace space)
    {
        if (length == null || length is AutoLength)
            return 0;
        // A 'contain-intrinsic-*' length can itself be font- or viewport-relative, so it
        // resolves through the same length rules every other used size goes through — the
        // font-relative basis being the element's own font size.
        return Math.Max(0, length.ToPixels(style.FontSize, space.RootFontSize,
            space.ViewportWidth, space.ViewportHeight));
    }

    /// <summary>Border-box size of a size-contained box in the inline axis: the fallback
    /// plus the box's own border and padding.</summary>
    public static float ContainedInlineBorderBox(ComputedStyle style, ConstraintSpace space,
        float borderPaddingInline) =>
        borderPaddingInline + FallbackInlineSize(style, space);

    /// <summary>Border-box size of a size-contained box in the block axis.</summary>
    public static float ContainedBlockBorderBox(ComputedStyle style, ConstraintSpace space,
        float borderPaddingBlock) =>
        borderPaddingBlock + FallbackBlockSize(style, space);
}
