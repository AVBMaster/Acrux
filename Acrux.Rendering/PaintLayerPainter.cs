using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

internal sealed class PaintLayerPainter
{
    private readonly PaintVisitor _visitor;
    private readonly DisplayList _displayList;
    private readonly PaintLayer _layer;
    private readonly PaintLayerClipper _clipper;

    public PaintLayerPainter(PaintVisitor visitor, PaintLayer layer)
    {
        _visitor = visitor;
        _displayList = visitor.GetDisplayList();
        _layer = layer;
        _clipper = new PaintLayerClipper(_displayList);
    }

    public void Paint(float contentOffsetY)
    {
        var element = _layer.Element;
        var layoutBox = _layer.LayoutBox;
        var style = _layer.Style;
        if (layoutBox == null || style == null) return;

        // A layered scroll container paints its background statically and its
        // scrollable content via the cached DrawScrollLayerOp (live scroll); its
        // descendant layers are skipped by the tree walk. Everything else keeps
        // the inline paint path.
        bool layered = false;
        if (layoutBox.IsScrollContainer)
        {
            _visitor.EnsureScrollLayer(element, layoutBox, style);
            layered = ScrollLayerCache.IsLayered(layoutBox);
        }

        var pushedStates = _clipper.PushAncestorStates(element, contentOffsetY, _visitor.PhysicalScale);

        var offsetBorderBox = new SKRect(
            layoutBox.BorderBox.Left,
            layoutBox.BorderBox.Top + contentOffsetY,
            layoutBox.BorderBox.Right,
            layoutBox.BorderBox.Bottom + contentOffsetY);

        // Apply element-level effects (opacity / transform / filter / clip-path /
        // blend-mode / mask) around the layer's own background, borders, content
        // and overflow controls so they paint as one object.
        var effectState = new ScopedPaintState(_displayList,
            new SKPoint(0, contentOffsetY),
            new SKRect(float.MinValue / 2, float.MinValue / 2, float.MaxValue, float.MaxValue));
        // Effects (transform/clip/opacity) must wrap the subtree even for a
        // visibility:hidden element: its descendants may re-declare visible.
        _visitor.PushObjectEffects(style, layoutBox, offsetBorderBox, effectState);

        // empty-cells: hide skips decorations but the (empty) content still paints.
        if (!TablePainter.ShouldHideEmptyCell(element, style))
            PaintLayerBackground(element, layoutBox, style, contentOffsetY);

        if (layered)
        {
            // The scroll-layer op keeps the content visible to direct/snapshot
            // renderers; the tile compositor draws the cached layer LIVE each frame.
            _visitor.EmitScrollLayerOp(layoutBox);
            _clipper.Pop(pushedStates, layoutBox.BorderBox);
            effectState.Dispose();
            return;
        }

        // The element's own inline content must respect its overflow clip;
        // PushAncestorStates only clips descendant layers.
        bool selfClips = PaintLayerClipper.CreatesOverflowClip(style) &&
            (layoutBox.IsScrollContainer || style.Overflow == OverflowType.Hidden
             || style.OverflowX == OverflowType.Hidden || style.OverflowY == OverflowType.Hidden);
        if (selfClips)
        {
            var selfClip = layoutBox.IsScrollContainer
                ? new SKRect(layoutBox.ContentBox.Left, layoutBox.ContentBox.Top + contentOffsetY,
                    layoutBox.ContentBox.Right, layoutBox.ContentBox.Bottom + contentOffsetY)
                : new SKRect(layoutBox.PaddingBox.Left, layoutBox.PaddingBox.Top + contentOffsetY,
                    layoutBox.PaddingBox.Right, layoutBox.PaddingBox.Bottom + contentOffsetY);
            if (selfClip.Width > 0 && selfClip.Height > 0)
            {
                // overflow clips to the rounded padding box when the element has a
                // border-radius (CSS Backgrounds 3 4).
                SKPath? rounded = RoundedBorderGeometry.OverflowClipPath(
                    style, new SKRect(layoutBox.BorderBox.Left, layoutBox.BorderBox.Top + contentOffsetY,
                        layoutBox.BorderBox.Right, layoutBox.BorderBox.Bottom + contentOffsetY), selfClip);
                var clipOp = PaintOpPool.GetPushClipOp();
                if (rounded != null)
                {
                    clipOp.ClipPath = rounded;
                    clipOp.AntiAlias = true;
                    clipOp.Bounds = selfClip;
                }
                else
                {
                    clipOp.ClipRect = selfClip;
                    clipOp.Bounds = selfClip;
                }
                _displayList.Add(clipOp);
            }
            else selfClips = false;
        }

        PaintLayerContent(element, layoutBox, style, contentOffsetY);

        PaintOverflowControls(layoutBox, style, contentOffsetY);

        if (selfClips)
        {
            var popOp = PaintOpPool.GetPopClipOp();
            popOp.Bounds = layoutBox.BorderBox;
            _displayList.Add(popOp);
        }

        _clipper.Pop(pushedStates, layoutBox.BorderBox);
        effectState.Dispose();
    }

    private void PaintLayerBackground(Element element, LayoutBox box, ComputedStyle style, float contentOffsetY)
    {
        if (style.Visibility != VisibilityType.Visible) return;
        if (style.Display == DisplayType.Inline) return;
        if (TablePainter.ShouldHideEmptyCell(element, style)) return;

        var offsetBorderBox = new SKRect(
            box.BorderBox.Left,
            box.BorderBox.Top + contentOffsetY,
            box.BorderBox.Right,
            box.BorderBox.Bottom + contentOffsetY);

        bool transfersToView = _visitor.GetCurrentDocument() != null &&
            ViewPainter.BackgroundTransfersToView(element, _visitor.GetCurrentDocument()!);

        // Background fill is skipped when the background was propagated to the
        // canvas (FramePainter paints it at the bottom of the z-order). Painting
        // it again on the element's own box would cover negative-z-index content
        // such as outset box shadows.
        if (!transfersToView)
            _visitor.PaintLayerBackgroundFill(element, style, offsetBorderBox);
        _visitor.PaintLayerBorder(element, style, offsetBorderBox);
        _visitor.PaintLayerOutline(element, style, offsetBorderBox);
    }

    private void PaintLayerContent(Element element, LayoutBox box, ComputedStyle style, float contentOffsetY)
    {
        // NOT gated on visibility:hidden: DrawElementContent filters the element's
        // own content internally but still paints descendants that re-declare
        // visibility:visible.
        var offsetBorderBox = new SKRect(
            box.BorderBox.Left,
            box.BorderBox.Top + contentOffsetY,
            box.BorderBox.Right,
            box.BorderBox.Bottom + contentOffsetY);

        _visitor.PaintLayerContent(element, style, box, offsetBorderBox);
    }

    private void PaintOverflowControls(LayoutBox box, ComputedStyle style, float contentOffsetY)
    {
        if (!box.IsScrollContainer) return;

        bool overflowYScroll = style.OverflowY == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
        bool overflowXScroll = style.OverflowX == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
        bool needsScrollY = overflowYScroll || box.ScrollContentHeight > box.ContentBox.Height;
        bool needsScrollX = overflowXScroll || box.ScrollContentWidth > box.ContentBox.Width;

        if (needsScrollY || needsScrollX)
        {
            _visitor.PaintLayerScrollbar(box, style, contentOffsetY);
        }
    }
}