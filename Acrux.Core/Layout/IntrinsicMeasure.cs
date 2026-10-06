using Acrux.Core.Layout.Geometry;
using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// Intrinsic size measurements shared by the formatting contexts that need them
/// (flexbox §4.5 / grid §6.7 automatic minimum size, sizing keywords).
/// </summary>
public static class IntrinsicMeasure
{
    /// <summary>The boxes currently inside a throwaway measuring pass. A probe lays a
    /// box out on its own, and that layout would ask this class for the same box's
    /// contributions again — the recursion has to break somewhere.</summary>
    [ThreadStatic]
    private static HashSet<Element>? _probing;

    private static bool IsMeasuring(Element element)
        => _probing != null && _probing.Contains(element);

    private static void ProbeEnter(Element element) => (_probing ??= new HashSet<Element>()).Add(element);

    /// <summary>SKTypeface instances are disposed by the paint side once a run is
    /// retired, so a face kept across a probe pass must be released again.</summary>
    private static void ProbeExit(Element element)
    {
        if (_probing == null) return;
        _probing.Remove(element);
        if (_probing.Count == 0) _probing = null;
    }

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
            isFixedBlockSize: false,
            percentageResolutionInline: float.NaN);
        // CSS Containment 3 §2.2: while size containment is on, the contents contribute no
        // intrinsic size, so the probe must not lay them out at all — the axis measures the
        // contain-intrinsic fallback plus the box's own border and padding.
        if (element.ComputedStyle.HasSizeContainment)
            return ContainedBorderBoxInline(element.ComputedStyle, space);
        // Same rule as the block path: 'break-word' must not break during
        // min-content measurement (CSS Text 3 §4.3).
        bool previousProbe = InlineLayoutAlgorithm.InMinContentProbe;
        InlineLayoutAlgorithm.SetMinContentProbe(true);
        LayoutResult result;
        ProbeEnter(element);
        try
        {
            result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
        }
        finally
        {
            ProbeExit(element);
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
            isFixedBlockSize: false,
            percentageResolutionInline: float.NaN);
        if (ContainedInlineProbe(element, space) is float containedMax)
            return containedMax;
        LayoutResult result;
        ProbeEnter(element);
        try
        {
            result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
        }
        catch
        {
            return 0;
        }
        finally
        {
            ProbeExit(element);
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

    // ==========================================================================
    // Intrinsic contributions (CSS Sizing 3 §4.2-§4.4)
    // ==========================================================================

    /// <summary>Set while a measuring probe lays a box out. The probe re-runs the
    /// layout algorithm, which asks for intrinsic sizes again for every box inside;
    /// answering those with another probe nests the passes and the cost grows with the
    /// square of the subtree. Inside a probe the nested requests therefore stop at the
    /// box's own strut — the real numbers come from the probe's finished fragment, and
    /// every box still gets an accurate answer when its own layout asks.</summary>
    [ThreadStatic]
    private static bool _probingInProgress;

    /// <summary>Min- and max-content inline size of a box as its containing block sees
    /// it: a border box, plus the box's own used horizontal margins. This is what a
    /// parent folds into its own intrinsic size, and what the sizing keywords
    /// (width: max-content, min-width: min-content, ...) resolve to.</summary>
    public static (float min, float max) Contributions(Element element, ConstraintSpace space,
        bool includeMargins = true)
    {
        var style = element.ComputedStyle;
        if (style == null || style.Display == DisplayType.None)
            return (0, 0);

        // Asked from inside this box's own measuring probe: the space the probe was
        // given IS the keyword's answer, so resolving the keyword a second time from
        // the half-built geometry would collapse the pass (a 'width: max-content' box
        // probed at 100000px came back as its min-content size). Report no preferred
        // size and no constraints, which leaves the probe's width alone.
        if (IsMeasuring(element))
            return (float.NaN, float.NaN);

        var box = HorizontalBox(style, space);
        float bp = box.HorizontalSum;
        var (contentMin, contentMax) = ContentBorderBox(element, style, space, box);

        // Every size on the box then resolves through the same length rules the layout
        // path uses, so an intrinsic keyword means the same thing in both places. The
        // content contributions are handed to it as the intrinsic sizes.
        Func<SizeType, MinMaxSizesResult> intrinsic =
            _ => new MinMaxSizesResult(new MinMaxSizes(contentMin, contentMax));

        float min = contentMin, max = contentMax;
        float preferred = LengthUtils.ResolveMainInlineLength(space, style, box, intrinsic, style.Width, null);
        if (!LengthUtils.IsIndefinite(preferred))
            min = max = preferred;

        float minW = LengthUtils.ResolveMinInlineLength(space, style, box, intrinsic, style.MinWidth);
        float maxW = LengthUtils.ResolveMaxInlineLength(space, style, box, intrinsic, style.MaxWidth);
        if (maxW < minW) maxW = minW;   // CSS 2.1 §10.4: min wins over max.
        min = Math.Clamp(min, minW, maxW);
        max = Math.Clamp(max, minW, maxW);

        if (!includeMargins)
            return (Math.Max(0, min), Math.Max(0, max));

        // A child's contribution travels with its used margins (§4.4); 'auto' margins
        // take no part.
        var margin = LengthUtils.ComputeMargins(space, style);
        return (Math.Max(0, min + margin.Left + margin.Right),
                Math.Max(0, max + margin.Left + margin.Right));
    }

    /// <summary>Block size (border box) the element's content takes when it is laid
    /// out at a definite inline size. A column flex item's main size is exactly
    /// this: its height at the width the cross axis gives it.</summary>
    public static float BlockSizeAtInline(Element element, float inlineBorderBox)
    {
        if (element.ComputedStyle == null || inlineBorderBox <= 0 || float.IsNaN(inlineBorderBox))
            return 0;
        var space = new ConstraintSpace(
            availableInlineSize: inlineBorderBox,
            availableBlockSize: float.PositiveInfinity,
            isFixedInlineSize: true,
            isFixedBlockSize: false,
            percentageResolutionInline: inlineBorderBox);
        ProbeEnter(element);
        LayoutResult result;
        try
        {
            result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(element, space);
        }
        catch
        {
            return 0;
        }
        finally
        {
            ProbeExit(element);
        }
        var fragment = result.Fragment;
        if (fragment == null) return 0;
        // Content box: the caller (a column flex item's main size) adds the item's own
        // border+padding back on.
        return Math.Max(0, fragment.BlockSize
            - fragment.BorderTop - fragment.BorderBottom
            - fragment.PaddingTop - fragment.PaddingBottom);
    }

    /// <summary>The same pair as an intrinsic-sizes callback, for the call sites that
    /// resolve lengths through <see cref="LengthUtils"/>.</summary>
    public static MinMaxSizesResult SizeResult(Element element, ConstraintSpace space)
    {
        var (min, max) = Contributions(element, space, includeMargins: false);
        return new MinMaxSizesResult(new MinMaxSizes(min, max));
    }

    /// <summary>CSS Containment 3 §2.2: a size-contained box's contents contribute no
    /// intrinsic size at all, so the probes that would otherwise lay the subtree out are
    /// answered from the box's own strut plus the contain-intrinsic fallback. Returns null
    /// when the box is not contained.</summary>
    private static float? ContainedInlineProbe(Element element, ConstraintSpace space)
    {
        var style = element.ComputedStyle;
        if (style == null || !style.HasSizeContainment)
            return null;
        return ContainedBorderBoxInline(style, space);
    }

    private static float ContainedBorderBoxInline(ComputedStyle style, ConstraintSpace space)
    {
        var border = LengthUtils.ComputeBorders(style);
        var padding = LengthUtils.ComputePadding(space, style);
        return ContainmentMetrics.ContainedInlineBorderBox(style, space,
            border.HorizontalSum + padding.HorizontalSum);
    }

    /// <summary>The box's own content as a border-box min/max pair, before width,
    /// min-width and max-width have a say.</summary>
    private static (float min, float max) ContentBorderBox(
        Element element, ComputedStyle style, ConstraintSpace space, BoxStrut box)
    {
        float bp = box.HorizontalSum;

        // CSS Containment 3 §2.2: while size containment is on the contents contribute no
        // intrinsic size at all, so both keywords collapse to the contain-intrinsic fallback
        // plus the box's own border and padding (reference engine: 'contain: size' with 10px
        // padding and no fallback is a 20px-wide box, with 'contain-intrinsic-width: 100px'
        // it is 120px).
        if (style.HasSizeContainment)
        {
            float contained = ContainmentMetrics.ContainedInlineBorderBox(style, space, bp);
            return (contained, contained);
        }

        // The probe passes below lay this box out again; while one of them is running,
        // asking the same box for its contributions must not start a second probe.
        if (_probingInProgress)
            return (bp, bp);

        if (BlockLayoutAlgorithm.IsReplacedElement(element))
        {
            // A replaced box is sized by its resource, not by its content.
            float natural = MaxContentInlineSize(element);
            return (Math.Max(bp, natural), Math.Max(bp, natural));
        }

        float contentMin, contentMax;
        if (element.IsInlineFormattingContextRoot())
        {
            (contentMin, contentMax) = ProbeInlineContributions(element, bp);
        }
        else
        {
            contentMin = contentMax = 0;

            // Children's percentages resolve against this box's content box, which is
            // only definite when this box's own width is; otherwise a percentage child
            // behaves as 'auto' inside a shrink-to-fit parent.
            float childBasis = ChildPercentageBasis(style, space, bp);

            foreach (var child in element.Children)
            {
                if (child is not Element childElement) continue;
                var cs = childElement.ComputedStyle;
                if (cs == null || cs.Display == DisplayType.None) continue;
                if (cs.Position is PositionType.Absolute or PositionType.Fixed) continue;
                if (!IsBlockLevel(cs)) continue;

                // Block-level children stack: the container is as wide as the widest
                // contribution (CSS Sizing 3 §4.4).
                var childSpace = WithInlineBasis(space, childBasis);
                var (cMin, cMax) = Contributions(childElement, childSpace);
                contentMin = Math.Max(contentMin, cMin);
                contentMax = Math.Max(contentMax, cMax);
            }

            if (HasInlineLevelContent(element))
            {
                // Inline runs between block children form an anonymous block; probing
                // this box inline yields exactly that block's contribution.
                var (anonMin, anonMax) = ProbeInlineContributions(element, bp);
                contentMin = Math.Max(contentMin, anonMin);
                contentMax = Math.Max(contentMax, anonMax);
            }
        }
        return (contentMin + bp, contentMax + bp);
    }

    /// <summary>The min- and max-content sizes of a box whose content is inline, as a
    /// content-box pair (the border and padding come off again).</summary>
    private static (float min, float max) ProbeInlineContributions(Element element, float borderPaddingInline)
    {
        if (_probingInProgress)
            return (0, 0);
        bool previous = _probingInProgress;
        _probingInProgress = true;
        try
        {
            return (Math.Max(0, MinContentInlineSize(element) - borderPaddingInline),
                    Math.Max(0, MaxContentInlineSize(element) - borderPaddingInline));
        }
        finally
        {
            _probingInProgress = previous;
        }
    }

    /// <summary>Border + padding, horizontally, with percentages resolved against the
    /// space the box was given (CSS 2.1 §8.6: on every axis, the containing block's
    /// inline size).</summary>
    private static BoxStrut HorizontalBox(ComputedStyle style, ConstraintSpace space)
    {
        var border = LengthUtils.ComputeBorders(style);
        var padding = LengthUtils.ComputePadding(space, style);
        return new BoxStrut(0, border.Right + padding.Right, 0, border.Left + padding.Left);
    }

    private static float ChildPercentageBasis(ComputedStyle style, ConstraintSpace space, float borderPaddingInline)
    {
        float width = LengthUtils.ResolveMainInlineLength(space, style,
            new BoxStrut(0, borderPaddingInline, 0, borderPaddingInline),
            _ => new MinMaxSizesResult(new MinMaxSizes(0, 0)), style.Width, null);
        if (LengthUtils.IsIndefinite(width)) return float.NaN;
        float content = width - borderPaddingInline;
        return style.BoxSizing == BoxSizingType.BorderBox ? content : content;
    }

    private static ConstraintSpace WithInlineBasis(ConstraintSpace space, float basisInline)
    {
        if (float.IsNaN(basisInline))
        {
            // Indefinite: percentages have no basis. Keep the available size, drop the
            // percentage resolution so a percentage length falls back to 'auto'.
            basisInline = float.NaN;
        }
        return space.InheritBuilder(space.AvailableInlineSize, float.PositiveInfinity)
            .SetPercentageResolution(basisInline, space.PercentageResolutionBlockSize)
            .ToConstraintSpace();
    }

    private static bool IsBlockLevel(ComputedStyle style) => style.Display switch
    {
        DisplayType.Block or DisplayType.Flex or DisplayType.Grid or DisplayType.Table
        or DisplayType.ListItem => true,
        _ => false,
    };

    private static bool HasInlineLevelContent(Element element)
    {
        foreach (var child in element.Children)
        {
            if (child is TextNode text && !text.IsWhitespaceOnly) return true;
            if (child is Element el && el.ComputedStyle is { } s
                && !IsBlockLevel(s) && s.Display != DisplayType.None
                && s.Position is not PositionType.Absolute and not PositionType.Fixed)
                return true;
        }
        return false;
    }

    private static float WidestLineInlineSize(BoxFragment fragment)
    {
        float widest = 0;
        foreach (var line in fragment.Lines)
            widest = Math.Max(widest, line.InlineSize);
        foreach (var child in fragment.Children)
        {
            float childWidest = WidestLineInlineSize(child);
            // An atomic child (a replaced element such as <img>, or an empty box)
            // carries its inline size on the fragment itself, not in a text line.
            // Fold it in so a definite-width <img> widens its ancestor's max-content
            // instead of collapsing the ancestor to a sibling's text width.
            if (child.Lines.Count == 0 && child.Children.Count == 0)
                childWidest = Math.Max(childWidest, child.InlineSize
                    + child.BorderLeft + child.BorderRight
                    + child.PaddingLeft + child.PaddingRight);
            widest = Math.Max(widest, childWidest);
        }
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
            isFixedBlockSize: false,
            percentageResolutionInline: float.NaN);
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
        // The fragment's BlockSize is measured from the border edge: the line boxes
        // carry the block-start strut, and the block-end padding is folded in at the
        // end of the pass. The flex cross axis adds the item's own border+padding on
        // afterwards, so handing it the border box counted the strut twice and every
        // padded item came out 2x its vertical padding too tall (b189 §1: 34px for a
        // 26px item).
        float strut = fragment.BorderTop + fragment.BorderBottom
            + fragment.PaddingTop + fragment.PaddingBottom;
        return Math.Max(0, fragment.BlockSize - strut);
    }
}
