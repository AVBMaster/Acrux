using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Applies the overflow clips and scroll translations inherited by a paint layer.
/// Mirrors paint_layer_clipper.cc: a layer is clipped by every clipping
/// ancestor, including ancestors that do not create a stacking context, and
/// offset by every scrolled ancestor's scroll origin.
/// </summary>
internal sealed class PaintLayerClipper
{
    private readonly DisplayList _displayList;

    public PaintLayerClipper(DisplayList displayList)
    {
        _displayList = displayList;
    }

    /// <summary>
    /// Pushes ancestor clips and scroll translations for <paramref name="element"/>
    /// and returns the stack of pushed state kinds (outermost first) that must be
    /// popped after painting the layer. The element's own clip/scroll is
    /// intentionally excluded; VisitElement owns it. <paramref name="physicalScale"/>
    /// (DPR × resolution scale) snaps the scroll translation to the device grid so
    /// sub-pixel offsets don't smear content rasterized through it.
    /// </summary>
    public List<bool> PushAncestorStates(Element element, float contentOffsetY, float physicalScale = 1f)
    {
        // Collect ancestors element → root, then replay root → element so
        // outer clips/transforms nest before inner ones.
        var ancestors = new List<Element>();
        for (var ancestor = element.ParentElement; ancestor != null; ancestor = ancestor.ParentElement)
            ancestors.Add(ancestor);
        ancestors.Reverse();

        var pushed = new List<bool>();
        // Accumulated scroll translation of ancestors already pushed; inner
        // clip rects live in that translated space and must be adjusted.
        float ax = 0, ay = 0;
        float scale = physicalScale <= 0.01f ? 1f : physicalScale;

        foreach (var ancestor in ancestors)
        {
            var style = ancestor.ComputedStyle;
            var box = ancestor.LayoutBox;
            if (style == null || box == null) continue;

            // CSS Masking 1 §1.1: a clip-path clips the element AND its descendants, and
            // the layer-tree walk paints those as separate layers — so the shape has to be
            // replayed here exactly like the overflow clip. It is pushed as a plain
            // intersecting clip (never a layer), which is why replaying it cannot
            // double-apply anything the way opacity or a filter would.
            SKPath? shapeClip = ClipPathClipper.HasClipPath(style.ClipPath)
                ? ClipPathClipper.Parse(style.ClipPath, box)
                : null;
            if (shapeClip != null)
                shapeClip.Offset(-ax, contentOffsetY - ay);

            bool clipsOverflow = CreatesClip(style);
            if (!clipsOverflow && shapeClip == null)
            {
                shapeClip?.Dispose();
                continue;
            }

            if (shapeClip != null)
            {
                var shapeOp = PaintOpPool.GetPushClipOp();
                shapeOp.ClipPath = shapeClip;
                shapeOp.AntiAlias = true;
                shapeOp.Bounds = new SKRect(box.BorderBox.Left - ax,
                    box.BorderBox.Top + contentOffsetY - ay,
                    box.BorderBox.Right - ax, box.BorderBox.Bottom + contentOffsetY - ay);
                _displayList.Add(shapeOp);
                pushed.Add(false);
            }

            if (!clipsOverflow) continue;

            var clip = ClipRectFor(box, style);
            // 'overflow-clip-margin' (CSS Overflow 3 §4.1) moves the clip edge off the
            // padding box: the keyword forms pick another box, a length inflates it.
            // The initial value is 0, so boxes that never mention it are unaffected.
            clip = ApplyOverflowClipMargin(style, box, clip);

            clip = new SKRect(
                clip.Left - ax,
                clip.Top + contentOffsetY - ay,
                clip.Right - ax,
                clip.Bottom + contentOffsetY - ay);
            if (clip.Width <= 0 || clip.Height <= 0)
                continue;

            var op = PaintOpPool.GetPushClipOp();
            // Descendant layers are clipped by the same rounded padding box the
            // ancestor clips its own content with.
            SKPath? rounded = RoundedBorderGeometry.OverflowClipPath(style,
                new SKRect(box.BorderBox.Left - ax, box.BorderBox.Top + contentOffsetY - ay,
                    box.BorderBox.Right - ax, box.BorderBox.Bottom + contentOffsetY - ay), clip);
            if (rounded != null)
            {
                op.ClipPath = rounded;
                op.AntiAlias = true;
                op.Bounds = clip;
            }
            else
            {
                op.ClipRect = clip;
                op.Bounds = clip;
            }
            _displayList.Add(op);
            pushed.Add(false);

            // A scrolled ancestor paints its contents at natural layout
            // positions; translate them back by the scroll origin so the
            // fragment lands in the visible viewport of the container. The offset
            // is snapped to the device pixel grid to keep text crisp, except while
            // the ancestor smooth-scrolls (fractional keeps motion continuous).
            float sx = box.ScrollX, sy = box.ScrollY;
            sx = box.IsSmoothScrollingX ? sx : MathF.Round(sx * scale) / scale;
            sy = box.IsSmoothScrollingY ? sy : MathF.Round(sy * scale) / scale;
            if (sx != 0 || sy != 0)
            {
                var t = PaintOpPool.GetPushTransformOp();
                t.Matrix = SKMatrix.CreateTranslation(-sx, -sy);
                t.Bounds = clip;
                _displayList.Add(t);
                pushed.Add(true);
                ax += sx;
                ay += sy;
            }
        }

        return pushed;
    }

    public void Pop(List<bool> pushed, SKRect bounds)
    {
        for (int i = pushed.Count - 1; i >= 0; i--)
        {
            PaintOp op = pushed[i]
                ? PaintOpPool.GetPopTransformOp()
                : PaintOpPool.GetPopClipOp();
            op.Bounds = bounds;
            _displayList.Add(op);
        }
    }

    private static SKRect ApplyOverflowClipMargin(ComputedStyle style, LayoutBox box, SKRect clip)
    {
        if (style.OverflowClipMarginBox != OverflowClipMarginBox.PaddingBox)
        {
            var reference = style.OverflowClipMarginBox == OverflowClipMarginBox.ContentBox
                ? box.ContentBox : box.BorderBox;
            clip = new SKRect(reference.Left, reference.Top, reference.Right, reference.Bottom);
        }
        var margin = style.OverflowClipMargin;
        if (margin == null) return clip;
        // A percentage resolves against the corresponding dimension of the padding box.
        float mx = margin.ToPixels(clip.Width, style.FontSize, 0, 0);
        float my = margin.ToPixels(clip.Height, style.FontSize, 0, 0);
        if (mx <= 0 && my <= 0) return clip;
        return new SKRect(clip.Left - mx, clip.Top - my, clip.Right + mx, clip.Bottom + my);
    }

    /// <summary>Which computed 'overflow' values actually clip the subtree. 'Clip' is
    /// here (it clips) but deliberately absent from IsScrollableOverflow (it cannot be
    /// scrolled) and from the BFC predicate (it does not establish a BFC) — the three
    /// questions are independent, and folding 'clip' into 'hidden' answers all three
    /// wrongly.</summary>
    /// <summary>CSS Containment 3 §2.4: paint containment clips the subtree as well, and it
    /// clips to the <em>border</em> box — measured against the reference engine with a 4px
    /// border and 10px padding, descendants stay hit-testable over the border band and are
    /// gone one pixel outside the border box. An element that also has an overflow clip gets
    /// the smaller padding/content clip of the two, which is what the intersection gives.</summary>
    internal static bool CreatesClip(ComputedStyle style) =>
        CreatesOverflowClip(style) || style.HasPaintContainment;

    /// <summary>The rectangle <see cref="CreatesClip"/> clips to.</summary>
    internal static SKRect ClipRectFor(LayoutBox box, ComputedStyle style) =>
        box.IsScrollContainer
            ? new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right, box.ContentBox.Bottom)
            : CreatesOverflowClip(style)
                ? new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, box.PaddingBox.Bottom)
                : new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, box.BorderBox.Bottom);

    internal static bool CreatesOverflowClip(ComputedStyle style) =>
        style.Overflow == OverflowType.Clip ||
        style.OverflowX == OverflowType.Clip ||
        style.OverflowY == OverflowType.Clip ||
        style.Overflow == OverflowType.Hidden ||
        style.Overflow == OverflowType.Scroll ||
        style.Overflow == OverflowType.Auto ||
        style.OverflowX == OverflowType.Hidden ||
        style.OverflowX == OverflowType.Scroll ||
        style.OverflowX == OverflowType.Auto ||
        style.OverflowY == OverflowType.Hidden ||
        style.OverflowY == OverflowType.Scroll ||
        style.OverflowY == OverflowType.Auto;
}
