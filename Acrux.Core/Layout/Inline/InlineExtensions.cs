using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Inline;

/// <summary>Extensions bridging LayoutObject / ComputedStyle into the inline port.</summary>
public static class InlineLayoutObjectExtensions
{
    public static bool IsOriginalDisplayInlineType(this LayoutObject layoutObject)
    {
        var display = layoutObject.StyleRef().Display;
        return display is DisplayType.Inline or DisplayType.InlineBlock or DisplayType.Ruby;
    }

    public static bool IsBR(this LayoutObject layoutObject) =>
        layoutObject.StyleRef() is { } s && layoutObject.Node is Element { TagName: "BR" };

    public static bool IsText(this LayoutObject layoutObject) => layoutObject is LayoutText;

    public static bool IsLayoutListItem(this LayoutObject layoutObject) => layoutObject.StyleRef().Display == DisplayType.ListItem;

    public static ComputedStyle ParentStyle(this LayoutObject layoutObject)
    {
        return layoutObject.StyleRef();
    }

    public static bool HasBorder(this ComputedStyle style) =>
        style.BorderTopWidth + style.BorderBottomWidth + style.BorderLeftWidth + style.BorderRightWidth > 0;

    public static bool HasPadding(this ComputedStyle style) =>
        !(style.PaddingTop is PixelLength pt && pt.Value == 0)
        || !(style.PaddingBottom is PixelLength pb && pb.Value == 0)
        || !(style.PaddingLeft is PixelLength pl && pl.Value == 0)
        || !(style.PaddingRight is PixelLength pr && pr.Value == 0);

    public static bool HasMargin(this ComputedStyle style) =>
        !(style.MarginTop is PixelLength mt && mt.Value == 0)
        || !(style.MarginBottom is PixelLength mb && mb.Value == 0)
        || !(style.MarginLeft is PixelLength ml && ml.Value == 0)
        || !(style.MarginRight is PixelLength mr && mr.Value == 0);

    public static bool IsFlippedLinesWritingMode(this ComputedStyle style) => false;

    public static ContentVisibilityType ContentVisibility(this ComputedStyle style) => ContentVisibilityType.Visible;

    /// <summary>Mirrors EWrap / style.ShouldWrapLine().</summary>
    public static bool ShouldWrapLine(this ComputedStyle style) => WhiteSpaceStyle.ShouldWrapLine(style);
}

/// <summary>
/// Inline length computations mirroring length_utils.cc inline helpers.
/// </summary>
public static class InlineLengthUtils
{
    /// <summary>Mirrors ComputeLineBordersForInline().</summary>
    public static BoxStrut ComputeLineBordersForInline(ComputedStyle style)
    {
        return LengthUtils.ComputeBorders(style);
    }

    /// <summary>Mirrors ComputeLinePadding().</summary>
    public static BoxStrut ComputeLinePadding(ConstraintSpace space, ComputedStyle style)
    {
        return LengthUtils.ComputePadding(space, style);
    }

    /// <summary>Mirrors ComputeLineMarginsForSelf().</summary>
    public static BoxStrut ComputeLineMarginsForSelf(ConstraintSpace space, ComputedStyle style)
    {
        return LengthUtils.ComputeMargins(space, style);
    }

    /// <summary>Mirrors ComputeLineMarginsForVisualContainer().</summary>
    public static BoxStrut ComputeLineMarginsForVisualContainer(ConstraintSpace space, ComputedStyle style)
    {
        var margins = LengthUtils.ComputeMargins(space, style);
        return margins;
    }

    /// <summary>Mirrors the static inline-end size computation in line_breaker.cc.</summary>
    public static float ComputeInlineEndSize(ConstraintSpace space, ComputedStyle style)
    {
        var margins = ComputeLineMarginsForSelf(space, style);
        var borders = ComputeLineBordersForInline(style);
        var paddings = ComputeLinePadding(space, style);
        return margins.Right + borders.Right + paddings.Right;
    }

    public static float InlineSum(this BoxStrut strut) => strut.Left + strut.Right;
    public static float BlockSum(this BoxStrut strut) => strut.Top + strut.Bottom;
}