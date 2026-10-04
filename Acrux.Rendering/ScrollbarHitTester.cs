using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Hit-testing and interaction model for custom scrollbars.
/// Given a page-space mouse position and a scroll-container LayoutBox,
/// determines whether the cursor is over the track / thumb / corner and
/// provides the mapping between thumb position and ScrollY/X.
/// </summary>
public static class ScrollbarHitTester
{
    public enum Part { None, VerticalThumb, VerticalTrack, HorizontalThumb, HorizontalTrack, Corner }

    public readonly struct HitResult
    {
        public Part Part { get; init; }
        public Element Container { get; init; }
        public LayoutBox Box { get; init; }
        /// <summary>Vertical track rect (page space) when Part is Vertical*.</summary>
        public SKRect TrackRect { get; init; }
        /// <summary>ScrollY (or X) corresponding to the clicked point on the track.</summary>
        public float TrackScroll { get; init; }
    }

    // ── Geometry helpers (mirror ScrollableAreaPainter layout math) ──

    public static float ResolveThickness(ComputedStyle style)
    {
        if (style.ScrollbarWidth == ScrollbarWidthType.None) return 0;
        var bar = style.ScrollbarCustom?.Bar;
        if (bar is { HasThickness: true })
            return Math.Clamp(bar.Thickness, 3f, 100f);
        return style.ScrollbarWidth == ScrollbarWidthType.Thin ? 8f : 12f;
    }

    public static SKRect GetVerticalTrackRect(LayoutBox box, ComputedStyle style, bool hasHorizontal)
    {
        float t = ResolveThickness(style);
        float x = box.PaddingBox.Right - t;
        float trackH = Math.Max(0, box.PaddingBox.Height - (hasHorizontal ? t : 0));
        return new SKRect(x, box.PaddingBox.Top, box.PaddingBox.Right, box.PaddingBox.Top + trackH);
    }

    public static SKRect GetHorizontalTrackRect(LayoutBox box, ComputedStyle style, bool hasVertical)
    {
        float t = ResolveThickness(style);
        float y = box.PaddingBox.Bottom - t;
        float trackW = Math.Max(0, box.PaddingBox.Width - (hasVertical ? t : 0));
        return new SKRect(box.PaddingBox.Left, y, box.PaddingBox.Left + trackW, box.PaddingBox.Bottom);
    }

    /// <summary>Vertical thumb rect from current ScrollY.</summary>
    public static SKRect GetVerticalThumbRect(LayoutBox box, SKRect trackRect)
    {
        float trackH = trackRect.Height;
        float ratio = box.ContentBox.Height / Math.Max(1, box.ScrollContentHeight);
        float thumbH = Math.Min(trackH, Math.Max(20f, trackH * Math.Min(1, ratio)));
        float scrollRange = Math.Max(1, box.ScrollContentHeight - box.ContentBox.Height);
        float pos = Math.Clamp(box.ScrollY, 0, scrollRange);
        float thumbY = trackRect.Top + (trackH - thumbH) * (pos / scrollRange);
        return new SKRect(trackRect.Left + 2, thumbY + 1, trackRect.Right - 2, thumbY + thumbH - 1);
    }

    /// <summary>Convert Y within vertical track to a ScrollY value.</summary>
    public static float TrackPositionToScrollY(LayoutBox box, SKRect trackRect, float mouseY)
    {
        float ratio = box.ContentBox.Height / Math.Max(1, box.ScrollContentHeight);
        float thumbH = Math.Min(trackRect.Height, Math.Max(20f, trackRect.Height * Math.Min(1, ratio)));
        float usable = trackRect.Height - thumbH;
        if (usable <= 0) return 0;
        float frac = Math.Clamp((mouseY - trackRect.Top - thumbH / 2) / usable, 0, 1);
        return frac * Math.Max(0, box.ScrollContentHeight - box.ContentBox.Height);
    }

    /// <summary>
    /// Hit-test a page-space point against all visible scrollbar parts of the
    /// given element tree. Returns the innermost hit.
    /// </summary>
    public static HitResult? HitTest(Element root, float pageX, float pageY)
    {
        HitResult? best = null;
        int bestDepth = -1;

        void Walk(Element el, int depth)
        {
            var box = el.LayoutBox;
            var st = el.ComputedStyle;
            if (box == null || st == null || !box.IsScrollContainer) { }
            else
            {
                float t = ResolveThickness(st);
                if (t <= 0) { }

                bool vOvf = box.ScrollContentHeight > box.ContentBox.Height;
                bool hOvf = box.ScrollContentWidth > box.ContentBox.Width;
                bool forceV = st.OverflowY == OverflowType.Scroll || st.Overflow == OverflowType.Scroll;
                bool forceH = st.OverflowX == OverflowType.Scroll || st.Overflow == OverflowType.Scroll;

                if (t > 0 && (vOvf || hOvf || forceV || forceH))
                {
                    // Check vertical scrollbar area
                    var vtRect = GetVerticalTrackRect(box, st, hOvf || forceH);
                    if (pageX >= vtRect.Left && pageX <= vtRect.Right &&
                        pageY >= vtRect.Top && pageY <= vtRect.Bottom)
                    {
                        var thRect = GetVerticalThumbRect(box, vtRect);
                        bool onThumb = pageY >= thRect.Top && pageY <= thRect.Bottom;
                        var part = onThumb ? Part.VerticalThumb : Part.VerticalTrack;
                        float scrollAt = TrackPositionToScrollY(box, vtRect, pageY);

                        if (depth > bestDepth)
                        {
                            best = new HitResult { Part = part, Container = el, Box = box, TrackRect = vtRect, TrackScroll = scrollAt };
                            bestDepth = depth;
                        }
                        return; // don't check horizontal if we hit vertical
                    }

                    // Check horizontal scrollbar area
                    var htRect = GetHorizontalTrackRect(box, st, vOvf || forceV);
                    if (pageX >= htRect.Left && pageX <= htRect.Right &&
                        pageY >= htRect.Top && pageY <= htRect.Bottom)
                    {
                        if (depth > bestDepth)
                        {
                            best = new HitResult { Part = Part.HorizontalTrack, Container = el, Box = box, TrackRect = htRect };
                            bestDepth = depth;
                        }
                        return;
                    }
                }
            }

            foreach (var c in el.Children)
                if (c is Element ce)
                    Walk(ce, depth + 1);
        }

        Walk(root, 0);
        return best;
    }
}
