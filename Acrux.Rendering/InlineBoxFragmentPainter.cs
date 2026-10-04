using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Mirrors InlineBoxFragmentPainter / InlineBoxFragmentPainterBase.
/// Paints the box-decoration background (box-shadow, background, border) of an
/// inline box. Translated from inline_box_fragment_painter.cc.
/// Paint order: normal shadow → fill layers → inset shadow → border.
/// </summary>
public sealed class InlineBoxFragmentPainter
{
    private readonly PaintVisitor _paintVisitor;
    private readonly DisplayList _displayList;
    private readonly BoxPainterBase _boxPainter;

    public InlineBoxFragmentPainter(PaintVisitor paintVisitor)
    {
        _paintVisitor = paintVisitor;
        _displayList = paintVisitor.GetDisplayList();
        _boxPainter = paintVisitor.BoxPainter;
    }

    /// <summary>
    /// Mirror of ComputedStyle::HasBoxDecorationBackground (computed_style.h:2404):
    /// HasBackground() || HasBorderDecoration() || HasEffectiveAppearance() || BoxShadow().
    /// </summary>
    public static bool HasBoxDecorationBackground(ComputedStyle style) =>
        HasBackground(style) || HasBorderDecoration(style) || HasEffectiveAppearance(style) || style.BoxShadow != null;

    /// <summary>
    /// Mirror of ComputedStyle::HasBackground (computed_style.cc:2245): background
    /// color is not fully transparent, or a background image is present.
    /// </summary>
    public static bool HasBackground(ComputedStyle style) =>
        (style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0) ||
        (style.BackgroundImage is { Count: > 0 } && style.BackgroundImage!.Any(s => s != "none"));

    /// <summary>Mirror of HasBorderDecoration: any border edge has a visible style.</summary>
    public static bool HasBorderDecoration(ComputedStyle style) =>
        (style.BorderTopWidth > 0 && style.BorderTopStyle != BorderStyle.None) ||
        (style.BorderRightWidth > 0 && style.BorderRightStyle != BorderStyle.None) ||
        (style.BorderBottomWidth > 0 && style.BorderBottomStyle != BorderStyle.None) ||
        (style.BorderLeftWidth > 0 && style.BorderLeftStyle != BorderStyle.None);

    /// <summary>
    /// Mirror of HasEffectiveAppearance. This engine has no native appearance
    /// support, so it always reports false (matching form-free inline boxes).
    /// </summary>
    public static bool HasEffectiveAppearance(ComputedStyle style) => false;

    private static bool HasBorderRadius(ComputedStyle style) =>
        style.BorderTopLeftRadius > 0 || style.BorderTopRightRadius > 0 ||
        style.BorderBottomLeftRadius > 0 || style.BorderBottomRightRadius > 0;

    private enum SlicePaintingType
    {
        DontPaint,
        PaintWithoutClip,
        PaintWithClip,
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainter::PaintBackgroundBorderShadow
    /// (inline_box_fragment_painter.cc:128). Paints box-shadow, background and
    /// border of the inline box into <paramref name="borderRect"/>.
    /// </summary>
    public void PaintBackgroundBorderShadow(Element element, ComputedStyle style, SKRect borderRect,
        PhysicalBoxSides sidesToInclude = PhysicalBoxSides.All, bool objectHasMultipleBoxes = false)
    {
        if (style.Visibility != VisibilityType.Visible) return;
        if (!HasBoxDecorationBackground(style)) return;

        PaintBoxDecorationBackground(element, style, borderRect, objectHasMultipleBoxes, sidesToInclude);
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainterBase::PaintBoxDecorationBackground
    /// (inline_box_fragment_painter.cc:334).
    /// </summary>
    private void PaintBoxDecorationBackground(Element element, ComputedStyle style, SKRect borderRect,
        bool objectHasMultipleBoxes, PhysicalBoxSides sidesToInclude)
    {
        // Shadow comes first and is behind the background and border.
        PaintNormalBoxShadow(style, borderRect, sidesToInclude);

        var fillLayer = BackgroundImageGeometry.FromStyle(style);
        PaintFillLayers(element, style, fillLayer, borderRect, objectHasMultipleBoxes);

        PaintInsetBoxShadow(style, borderRect, sidesToInclude);

        switch (GetBorderPaintType(style, borderRect, objectHasMultipleBoxes))
        {
            case SlicePaintingType.DontPaint:
                break;
            case SlicePaintingType.PaintWithoutClip:
            case SlicePaintingType.PaintWithClip:
                if (NinePieceImagePainter.HasBorderImage(style))
                    NinePieceImagePainter.Paint(_displayList, _paintVisitor.ImageCache, style, borderRect, _paintVisitor.BaseUrl);
                else
                    _paintVisitor.DrawElementBorder(element, null!, style, borderRect);
                break;
        }
    }

    private void PaintNormalBoxShadow(ComputedStyle style, SKRect paintRect, PhysicalBoxSides sidesToInclude)
    {
        _boxPainter.PaintNormalBoxShadow(paintRect, style, sidesToInclude);
    }

    private void PaintInsetBoxShadow(ComputedStyle style, SKRect paintRect, PhysicalBoxSides sidesToInclude)
    {
        _boxPainter.PaintInsetBoxShadowWithBorderRect(paintRect, style, sidesToInclude);
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainterBase::PaintFillLayers
    /// (inline_box_fragment_painter.cc:367). Layers are painted back-to-front:
    /// the chain is recursed into first, then the current layer is painted.
    /// </summary>
    private void PaintFillLayers(Element element, ComputedStyle style, FillLayer? layer, SKRect rect,
        bool objectHasMultipleBoxes)
    {
        if (layer == null)
            return;
        if (layer.Next != null)
            PaintFillLayers(element, style, layer.Next, rect, objectHasMultipleBoxes);
        PaintFillLayer(element, style, layer, rect, objectHasMultipleBoxes);
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainterBase::PaintFillLayer
    /// (inline_box_fragment_painter.cc:378). A single box paints the layer
    /// directly; an object spanning multiple lines paints its fill as a
    /// continuous strip clipped to this line's rect.
    /// </summary>
    private void PaintFillLayer(Element element, ComputedStyle style, FillLayer fillLayer, SKRect paintRect,
        bool objectHasMultipleBoxes)
    {
        bool hasFillImage = fillLayer.Image != null;
        if (!objectHasMultipleBoxes || (!hasFillImage && !HasBorderRadius(style)))
        {
            _paintVisitor.PaintBackgroundFill(element, style, paintRect);
            return;
        }

        // Handle fill images that clone or span multiple lines.
        bool multiLine = objectHasMultipleBoxes; // box-decoration-break: clone is not modeled.
        SKRect rect = multiLine ? PaintRectForImageStrip(paintRect, style) : paintRect;

        var clipOp = PaintOpPool.GetPushClipOp();
        clipOp.ClipRect = ToPixelSnappedRect(paintRect);
        clipOp.Bounds = paintRect;
        _displayList.Add(clipOp);

        _paintVisitor.PaintBackgroundFill(element, style, rect);

        var popOp = PaintOpPool.GetPopClipOp();
        popOp.Bounds = paintRect;
        _displayList.Add(popOp);
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainterBase::GetBorderPaintType
    /// (inline_box_fragment_painter.cc:291).
    /// </summary>
    private SlicePaintingType GetBorderPaintType(ComputedStyle style, SKRect adjustedFrameRect, bool objectHasMultipleBoxes)
    {
        if (!HasBorderDecoration(style))
            return SlicePaintingType.DontPaint;
        return GetSlicePaintType(style, adjustedFrameRect, objectHasMultipleBoxes);
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainterBase::GetSlicePaintType
    /// (inline_box_fragment_painter.cc:301). Border-image is not rendered in
    /// this engine, so there is never a nine-piece image; the border always
    /// paints without an image-strip clip.
    /// </summary>
    private SlicePaintingType GetSlicePaintType(ComputedStyle style, SKRect adjustedFrameRect, bool objectHasMultipleBoxes)
    {
        // has_nine_piece_image is always false (no border-image rendering).
        if (!objectHasMultipleBoxes)
            return SlicePaintingType.PaintWithoutClip;
        return SlicePaintingType.PaintWithClip;
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainterBase::ComputeFragmentOffsetOnLine
    /// (inline_box_fragment_painter.cc:219). This engine gives each inline
    /// element a single box, so there is exactly one fragment per layout
    /// object: before and after are both zero.
    /// </summary>
    private void ComputeFragmentOffsetOnLine(ComputedStyle style, SKRect boxRect, out float offsetOnLine, out float totalWidth)
    {
        float before = 0;
        float after = 0;
        totalWidth = before + after + boxRect.Width;
        // LTR paints from the before side; RTL would swap to `after`.
        offsetOnLine = style.Direction == "rtl" ? after : before;
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainterBase::PaintRectForImageStrip
    /// (inline_box_fragment_painter.cc:250). A fill/border image spanning
    /// multiple lines is painted as one continuous strip.
    /// </summary>
    private SKRect PaintRectForImageStrip(SKRect paintRect, ComputedStyle style)
    {
        ComputeFragmentOffsetOnLine(style, paintRect, out float offsetOnLine, out float totalWidth);
        if (style.WritingMode == WritingModeType.HorizontalTb)
            return new SKRect(paintRect.Left - offsetOnLine, paintRect.Top,
                paintRect.Left - offsetOnLine + totalWidth, paintRect.Bottom);
        return new SKRect(paintRect.Left, paintRect.Top - offsetOnLine,
            paintRect.Right, paintRect.Top - offsetOnLine + totalWidth);
    }

    /// <summary>
    /// Mirror of InlineBoxFragmentPainterBase::ClipRectForNinePieceImageStrip
    /// (inline_box_fragment_painter.cc:269). Border-image outsets are not
    /// modeled (no border-image rendering), so the outsets are zero.
    /// </summary>
    private static SKRect ClipRectForNinePieceImageStrip(ComputedStyle style, PhysicalBoxSides sidesToInclude, SKRect paintRect)
    {
        var clipRect = paintRect;
        // style.ImageOutsets(image) == 0 in this engine.
        if ((sidesToInclude & PhysicalBoxSides.Left) != 0)
        {
            clipRect.Left = paintRect.Left;
            clipRect.Right = paintRect.Right;
        }
        if ((sidesToInclude & PhysicalBoxSides.Right) != 0)
        {
            clipRect.Right = paintRect.Right;
        }
        if ((sidesToInclude & PhysicalBoxSides.Top) != 0)
        {
            clipRect.Top = paintRect.Top;
            clipRect.Bottom = paintRect.Bottom;
        }
        if ((sidesToInclude & PhysicalBoxSides.Bottom) != 0)
        {
            clipRect.Bottom = paintRect.Bottom;
        }
        return clipRect;
    }

    private static SKRect ToPixelSnappedRect(SKRect r) =>
        new((float)Math.Round(r.Left), (float)Math.Round(r.Top),
            (float)Math.Round(r.Right), (float)Math.Round(r.Bottom));
}
