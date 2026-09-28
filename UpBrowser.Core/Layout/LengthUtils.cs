using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Length resolution utilities. Mirrors length_utils.cc.
/// Resolves CSS Length values to floats based on constraint space and style.
/// </summary>
public static class LengthUtils
{
    public const float IndefiniteSize = float.NaN;
    public static bool IsIndefinite(float v) => float.IsNaN(v);

    public static float InlineSizeFromAspectRatio(BoxStrut borderPadding, LogicalSize aspectRatio, bool isBorderBox, float blockSize)
    {
        if (aspectRatio.BlockSize <= 0) return 0;
        if (isBorderBox)
            return Math.Max(borderPadding.HorizontalSum, blockSize * aspectRatio.InlineSize / aspectRatio.BlockSize);
        blockSize -= borderPadding.VerticalSum;
        return blockSize * aspectRatio.InlineSize / aspectRatio.BlockSize + borderPadding.HorizontalSum;
    }

    public static float BlockSizeFromAspectRatio(BoxStrut borderPadding, LogicalSize aspectRatio, bool isBorderBox, float inlineSize)
    {
        if (aspectRatio.InlineSize <= 0) return 0;
        if (isBorderBox)
            return Math.Max(borderPadding.VerticalSum, inlineSize * aspectRatio.BlockSize / aspectRatio.InlineSize);
        inlineSize -= borderPadding.HorizontalSum;
        return inlineSize * aspectRatio.BlockSize / aspectRatio.InlineSize + borderPadding.VerticalSum;
    }

    public static float ResolveInlineLength(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Func<SizeType, MinMaxSizesResult> minMaxSizesFunc, Length? length, Length? autoLength,
        LengthTypeInternal lengthType = LengthTypeInternal.Main, float overrideAvailableSize = float.NaN)
    {
        if (length == null) return IndefiniteSize;
        if (length is AutoLength && autoLength != null) length = autoLength;

        var availableSize = IsIndefinite(overrideAvailableSize) ? space.AvailableInlineSize : overrideAvailableSize;
        var percentageBase = space.PercentageResolutionInlineSize;

        if (length is PixelLength pl)
        {
            float value = pl.Value;
            if (style.BoxSizing == BoxSizingType.BorderBox)
                value = Math.Max(borderPadding.HorizontalSum, value);
            else
                value += borderPadding.HorizontalSum;
            return value;
        }

        if (length is PercentLength pcl)
        {
            if (IsIndefinite(percentageBase)) return IndefiniteSize;
            float value = percentageBase * pcl.Value;
            if (style.BoxSizing == BoxSizingType.BorderBox)
                value = Math.Max(borderPadding.HorizontalSum, value);
            else
                value += borderPadding.HorizontalSum;
            return value;
        }

        // calc()/min()/max()/clamp() expressions containing '%' resolve their
        // percentages against the same inline-size base the plain PercentLength
        // path above uses (ToPixels maps the reference onto '%' conversion).
        if (length is MathLength mathLen && mathLen.Expression.Contains('%') && !IsIndefinite(percentageBase))
        {
            // The reference handed to the expression is the percentage base, so the
            // font-relative units inside it must come from the element's own style.
            using var _fontScope = FontUnitContext.Use(style);
            float mvalue = mathLen.ToPixels(percentageBase, space.RootFontSize, space.ViewportWidth, space.ViewportHeight);
            if (!float.IsNaN(mvalue) && !float.IsInfinity(mvalue))
            {
                if (style.BoxSizing == BoxSizingType.BorderBox)
                    mvalue = Math.Max(borderPadding.HorizontalSum, mvalue);
                else
                    mvalue += borderPadding.HorizontalSum;
                return mvalue;
            }
        }

        // Every remaining unit (em/rem/vw/vh/vmin/vmax/ex/ch/cq*/dv*/sv*/lv*/
        // vi/vb/re*/ric/lh/rlh/cap/rcap/math…) resolves through the shared ToPixels
        // with the real root-font-size & viewport carried by the constraint space.
        var fixedPx = TryResolveFixedLength(space, style, borderPadding.HorizontalSum, length);
        if (fixedPx.HasValue) return fixedPx.Value;

        return IndefiniteSize;
    }

    public static float ResolveBlockLength(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Length? length, Length? autoLength, LengthTypeInternal lengthType = LengthTypeInternal.Main,
        float overrideAvailableSize = float.NaN, float? overridePercentageResolutionSize = null,
        Func<SizeType, float>? blockSizeFunc = null)
    {
        if (length == null) return IndefiniteSize;
        if (length is AutoLength && autoLength != null) length = autoLength;

        blockSizeFunc ??= _ => IndefiniteSize;
        var percentageBase = overridePercentageResolutionSize ?? space.PercentageResolutionBlockSize;

        if (length is PixelLength pl)
        {
            float value = pl.Value;
            if (style.BoxSizing == BoxSizingType.BorderBox)
                value = Math.Max(borderPadding.VerticalSum, value);
            else
                value += borderPadding.VerticalSum;
            return value;
        }

        if (length is PercentLength pcl)
        {
            if (IsIndefinite(percentageBase))
                return lengthType == LengthTypeInternal.Main ? blockSizeFunc(SizeType.Content) : IndefiniteSize;
            float value = percentageBase * pcl.Value;
            if (style.BoxSizing == BoxSizingType.BorderBox)
                value = Math.Max(borderPadding.VerticalSum, value);
            else
                value += borderPadding.VerticalSum;
            return value;
        }

        // Every remaining unit (em/rem/vw/vh/vmin/vmax/ex/ch/cq*/dv*/sv*/lv*/
        // vi/vb/re*/ric/lh/rlh/cap/rcap/math…) resolves through the shared ToPixels
        // with the real root-font-size & viewport carried by the constraint space.
        if (length is MathLength mathLenB && mathLenB.Expression.Contains('%') && !IsIndefinite(percentageBase))
        {
            using var _fontScope = FontUnitContext.Use(style);
            float mvalue = mathLenB.ToPixels(percentageBase, space.RootFontSize, space.ViewportWidth, space.ViewportHeight);
            if (!float.IsNaN(mvalue) && !float.IsInfinity(mvalue))
            {
                if (style.BoxSizing == BoxSizingType.BorderBox)
                    mvalue = Math.Max(borderPadding.VerticalSum, mvalue);
                else
                    mvalue += borderPadding.VerticalSum;
                return mvalue;
            }
        }

        var fixedPx = TryResolveFixedLength(space, style, borderPadding.VerticalSum, length);
        if (fixedPx.HasValue) return fixedPx.Value;

        return IndefiniteSize;
    }

    /// <summary>
    /// Resolve any non-auto/non-percentage length (em/rem/vw/vh/vmin/vmax/ex/ch,
    /// container-query units, dynamic viewport units, lh/cap/math units…) through
    /// the shared <see cref="Length.ToPixels"/> implementation using the real
    /// root-font-size &amp; viewport carried by <paramref name="space"/>, then apply
    /// box-sizing correction. Returns null when the unit yields no definite value
    /// on this path (e.g. container units without a query container).
    /// </summary>
    private static float? TryResolveFixedLength(ConstraintSpace space, ComputedStyle style,
        float borderPaddingSum, Length length)
    {
        if (length is AutoLength or PercentLength)
            return null;

        float value;
        using (FontUnitContext.Use(style))
            value = length.ToPixels(style.FontSize, space.RootFontSize, space.ViewportWidth, space.ViewportHeight);
        if (float.IsNaN(value) || float.IsInfinity(value))
            return null;

        if (style.BoxSizing == BoxSizingType.BorderBox)
            value = Math.Max(borderPaddingSum, value);
        else
            value += borderPaddingSum;
        return value;
    }

    public static float ResolveMinInlineLength(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Func<SizeType, MinMaxSizesResult> minMaxSizesFunc, Length? length, Length? autoLength = null,
        float overrideAvailableSize = float.NaN)
    {
        var result = ResolveInlineLength(space, style, borderPadding, minMaxSizesFunc, length, autoLength, LengthTypeInternal.Min, overrideAvailableSize);
        return IsIndefinite(result) ? borderPadding.HorizontalSum : result;
    }

    public static float ResolveMaxInlineLength(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Func<SizeType, MinMaxSizesResult> minMaxSizesFunc, Length? length, float overrideAvailableSize = float.NaN)
    {
        var result = ResolveInlineLength(space, style, borderPadding, minMaxSizesFunc, length, null, LengthTypeInternal.Max, overrideAvailableSize);
        return IsIndefinite(result) ? float.MaxValue : result;
    }

    public static float ResolveMainInlineLength(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Func<SizeType, MinMaxSizesResult> minMaxSizesFunc, Length? length, Length? autoLength,
        float overrideAvailableSize = float.NaN)
    {
        return ResolveInlineLength(space, style, borderPadding, minMaxSizesFunc, length, autoLength, LengthTypeInternal.Main, overrideAvailableSize);
    }

    public static float ResolveMinBlockLength(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Func<SizeType, float>? blockSizeFunc, Length? length, Length? autoLength = null,
        float overrideAvailableSize = float.NaN, float? overridePercentageResolutionSize = null)
    {
        var result = ResolveBlockLength(space, style, borderPadding, length, autoLength, LengthTypeInternal.Min, overrideAvailableSize, overridePercentageResolutionSize, blockSizeFunc);
        return IsIndefinite(result) ? borderPadding.VerticalSum : result;
    }

    public static float ResolveMaxBlockLength(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Length? length, Func<SizeType, float>? blockSizeFunc = null,
        float overrideAvailableSize = float.NaN, float? overridePercentageResolutionSize = null)
    {
        var result = ResolveBlockLength(space, style, borderPadding, length, null, LengthTypeInternal.Max, overrideAvailableSize, overridePercentageResolutionSize, blockSizeFunc);
        return IsIndefinite(result) ? float.MaxValue : result;
    }

    public static float ResolveMainBlockLength(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Length? length, Length? autoLength, float intrinsicSize = float.NaN,
        float overrideAvailableSize = float.NaN, float? overridePercentageResolutionSize = null)
    {
        return ResolveBlockLength(space, style, borderPadding, length, autoLength, LengthTypeInternal.Main, overrideAvailableSize, overridePercentageResolutionSize, _ => intrinsicSize);
    }

    public static (float min, float max) ComputeMinMaxBlockSizes(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Length? autoMinLength, Func<SizeType, float> blockSizeFunc, float overrideAvailableSize = float.NaN)
    {
        float min = ResolveMinBlockLength(space, style, borderPadding, blockSizeFunc, style.MinHeight, autoMinLength, overrideAvailableSize);
        float max = ResolveMaxBlockLength(space, style, borderPadding, style.MaxHeight, blockSizeFunc, overrideAvailableSize);
        // See ComputeMinMaxInlineSizes: min-height > max-height ignores the max.
        return (min, Math.Max(min, max));
    }

    public static float ComputeBlockSizeForFragment(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        float intrinsicSize, float inlineSize, float overrideAvailableSize = float.NaN)
    {
        float extent = ResolveMainBlockLength(space, style, borderPadding, style.Height, null, intrinsicSize, overrideAvailableSize);
        if (IsIndefinite(extent)) extent = intrinsicSize;
        if (IsIndefinite(extent)) return IndefiniteSize;

        var (minSize, maxSize) = ComputeMinMaxBlockSizes(space, style, borderPadding, null, _ => intrinsicSize, overrideAvailableSize);
        return Math.Clamp(extent, minSize, maxSize);
    }

    public static float ComputeInlineSizeForFragment(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Func<SizeType, MinMaxSizesResult> minMaxSizesFunc)
    {
        float extent = ResolveMainInlineLength(space, style, borderPadding, minMaxSizesFunc, style.Width, null);
        if (IsIndefinite(extent)) extent = space.AvailableInlineSize;
        return extent;
    }

    public static BoxStrut ComputeBorders(ComputedStyle style) =>
        new(style.BorderTopWidth, style.BorderRightWidth, style.BorderBottomWidth, style.BorderLeftWidth);

    public static BoxStrut ComputePadding(ConstraintSpace space, ComputedStyle style)
    {
        float fontSize = style.FontSize;
        using var _scope = FontUnitContext.Use(style);
        return new BoxStrut(
            ResolveInsetLength(style.PaddingTop, fontSize, space),
            ResolveInsetLength(style.PaddingRight, fontSize, space),
            ResolveInsetLength(style.PaddingBottom, fontSize, space),
            ResolveInsetLength(style.PaddingLeft, fontSize, space));
    }

    public static BoxStrut ComputeMargins(ConstraintSpace space, ComputedStyle style)
    {
        float fontSize = style.FontSize;
        using var _scope = FontUnitContext.Use(style);
        return new BoxStrut(
            ResolveInsetLength(style.MarginTop, fontSize, space),
            ResolveInsetLength(style.MarginRight, fontSize, space),
            ResolveInsetLength(style.MarginBottom, fontSize, space),
            ResolveInsetLength(style.MarginLeft, fontSize, space));
    }

    /// <summary>
    /// padding/margin percentages resolve against the containing block's INLINE
    /// size on every axis (CSS box model); other units keep their normal bases.
    /// </summary>
    private static float ResolveInsetLength(Length length, float fontSize, ConstraintSpace space)
    {
        // 'auto' is not a length: it contributes zero while the side is being
        // resolved, and the leftover space is handed back later by the
        // auto-margin rules (CSS 2.1 §10.3.3).
        if (length is AutoLength)
            return 0;
        if (length is PercentLength pct)
        {
            float baseInline = float.IsNaN(space.PercentageResolutionInlineSize) ? 0 : space.PercentageResolutionInlineSize;
            return pct.Value * baseInline;
        }
        // Math expressions re-enter the evaluator here, and font-relative units
        // inside them need the element's style as the ambient context.
        if (FontUnitContext.Current is { } style)
        {
            using var _scope = FontUnitContext.Use(style);
            return length.ToPixels(fontSize, space.RootFontSize, space.ViewportWidth, space.ViewportHeight);
        }
        return length.ToPixels(fontSize, space.RootFontSize, space.ViewportWidth, space.ViewportHeight);
    }

    public static (float min, float max) ComputeMinMaxInlineSizes(ConstraintSpace space, ComputedStyle style, BoxStrut borderPadding,
        Func<SizeType, MinMaxSizesResult> minMaxSizesFunc, Length? autoMinLength = null, float overrideAvailableSize = float.NaN)
    {
        float min = ResolveMinInlineLength(space, style, borderPadding, minMaxSizesFunc, style.MinWidth, autoMinLength, overrideAvailableSize);
        float max = ResolveMaxInlineLength(space, style, borderPadding, minMaxSizesFunc, style.MaxWidth, overrideAvailableSize);
        // CSS 2.1 §10.4: when min-width > max-width the max is ignored, so the
        // pair must stay ordered — otherwise downstream clamps (Math.Clamp with
        // min > max) throw and the box silently disappears.
        return (min, Math.Max(min, max));
    }

    /// <summary>Compute the concrete size for a replaced element given its intrinsic info and the available space.</summary>
    public static PhysicalSize ComputeReplacedSize(IntrinsicSizingInfo intrinsic, ConstraintSpace space, ComputedStyle style,
        BoxStrut borderPadding, float availableInline, float availableBlock)
    {
        float availInline = Math.Max(0, availableInline - borderPadding.HorizontalSum);
        float availBlock = Math.Max(0, availableBlock - borderPadding.VerticalSum);
        var defaultSize = new PhysicalSize(availInline, availBlock);

        // Use explicit width/height from style if provided; otherwise fall back
        // to ConcreteObjectSize (intrinsic sizing).
        float w = style.Width is not AutoLength ? style.Width.ToPixels(style.FontSize, space.RootFontSize, space.ViewportWidth, space.ViewportHeight) : float.NaN;
        float h = style.Height is not AutoLength ? style.Height.ToPixels(style.FontSize, space.RootFontSize, space.ViewportWidth, space.ViewportHeight) : float.NaN;

        if (float.IsNaN(w) || float.IsNaN(h))
        {
            var concrete = ReplacedSizeUtils.ConcreteObjectSize(intrinsic, defaultSize);
            if (float.IsNaN(w)) w = concrete.Width;
            if (float.IsNaN(h)) h = concrete.Height;
        }

        var minMax = new MinMaxSizesResult(new MinMaxSizes(w, w));

        // Unit-resolution context: percentage min/max on replaced elements resolve against the
        // CONTAINING BLOCK inline size handed to us (spec), not against whatever
        // percentage base the incoming space happens to carry — anonymous/atomic
        // child spaces can have stale bases (was producing max-width:100% → 8px).
        float minW = style.MinWidth is PercentLength minPct
            ? availableInline * minPct.Value - borderPadding.HorizontalSum
            : ResolveMinInlineLength(space, style, borderPadding, _ => minMax, style.MinWidth);
        float maxW = style.MaxWidth is PercentLength maxPct
            ? Math.Max(0, availableInline * maxPct.Value - borderPadding.HorizontalSum)
            : ResolveMaxInlineLength(space, style, borderPadding, _ => minMax, style.MaxWidth);
        float minH = ResolveMinBlockLength(space, style, borderPadding, _ => h, style.MinHeight);
        float maxH = style.MaxHeight is PercentLength maxPctH
            ? Math.Max(0, availableBlock * maxPctH.Value - borderPadding.VerticalSum)
            : ResolveMaxBlockLength(space, style, borderPadding, style.MaxHeight, _ => h);

        // CSS 2.1 §10.4/§10.6.5: min wins when it exceeds max; keep the pair
        // ordered so Math.Clamp below cannot throw.
        maxW = Math.Max(maxW, minW);
        maxH = Math.Max(maxH, minH);
        w = Math.Clamp(w, minW, maxW);
        h = Math.Clamp(h, minH, maxH);
        return new PhysicalSize(w, h);
    }
}

public enum SizeType { Content, Intrinsic }
public enum LengthTypeInternal { Min, Main, Max }

public struct MinMaxSizesResult
{
    public MinMaxSizes Sizes { get; set; }
    public bool DependsOnBlockConstraints { get; set; }
    public MinMaxSizesResult(MinMaxSizes sizes, bool dependsOnBlockConstraints = false) { Sizes = sizes; DependsOnBlockConstraints = dependsOnBlockConstraints; }
}