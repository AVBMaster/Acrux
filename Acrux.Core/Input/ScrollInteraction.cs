using Acrux.Core.Dom;

namespace Acrux.Core.Input;

/// <summary>
/// Unified scroll interaction controller for the rendering engine.
///
/// Handles all user-driven scrolling �?mouse wheel, scrollbar thumb drag,
/// track click, and hover state �?for every scroll container in the document
/// (inner overflow containers AND the root/page scroller).
///
/// The host application wires platform input events to this controller and
/// subscribes to <see cref="OnScrollChanged"/> to trigger a repaint. The
/// controller operates entirely in page coordinate space and never touches
/// window/chrome concepts.
///
/// Usage (host):
/// <code>
///   var scroll = new ScrollInteraction();
///   scroll.OnScrollChanged = () => { pendingRelayout = true; };
///
///   // On document load:
///   scroll.SetDocument(doc);
///
///   // On mouse wheel:
///   bool consumed = scroll.HandleWheel(deltaX, deltaY, pageX, pageY);
///
///   // On mouse events:
///   bool consumed = scroll.HandleMouseDown(pageX, pageY);
///   scroll.HandleMouseMove(pageX, pageY);   // drag / hover tracking
///   scroll.HandleMouseUp();
/// </code>
/// </summary>
public sealed class ScrollInteraction
{
    private Document? _document;

    /// <summary>
    /// Raised after any ScrollX/Y value changes with the scrolled container's box,
    /// so the host can request the right kind of repaint (element vs page).
    /// </summary>
    public Action<LayoutBox>? OnScrollChanged { get; set; }

    /// <summary>
    /// Master smooth-scrolling switch (mirrors the settings page toggle). When
    /// false the wheel applies its offset instantly; when true it advances a scroll
    /// TARGET that the element's spring animation chases (Edge-like ease-out).
    /// </summary>
    public bool SmoothEnabled { get; set; } = true;

    /// <summary>
    /// Wheel velocity scale for element smooth scrolling. Chosen so each 60px wheel
    /// notch injects enough velocity to travel ~one notch with a visibly easing
    /// glide (~0.3 s; the host's element decay lambda matches this scale).
    /// </summary>
    private const float ElementWheelVelScale = 10f;

    // ── Drag state ──
    private Element? _dragContainer;
    private bool _dragVertical;
    private float _dragGrabOffset;
    private float _dragTrackOrigin;
    private float _dragRange;
    private bool _dragging;

    public bool IsDragging => _dragging;

    public void SetDocument(Document? doc) => _document = doc;

    // ════════════════════════════════════════════════════════════════
    // Mouse wheel
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle a mouse-wheel event at page-space position (<paramref name="pageX"/>,
    /// <paramref name="pageY"/>). Walks from the deepest hit element up through
    /// ancestors to find the nearest scroll container with overflow.
    /// </summary>
    /// <returns>true if the event was consumed by a scroll container.</returns>
    public bool HandleWheel(double deltaX, double deltaY, float pageX, float pageY)
    {
        var el = HitTestDeepest(_document, pageX, pageY);
        while (el != null)
        {
            var box = el.LayoutBox;
            if (box != null && IsUsableScroller(box))
            {
                // #1 fix: the mouse must be within the container's VISIBLE area.
                // Descendant elements inside an overflow container have layout
                // positions extending beyond the container's visible bounds �?                // without this check, the wheel would trigger for containers
                // the mouse isn't actually over.
                var bb = box.BorderBox;
                if (pageX < bb.Left || pageX > bb.Right || pageY < bb.Top || pageY > bb.Bottom)
                {
                    el = el.ParentElement;
                    continue;
                }

                bool consumed = false;
                if (deltaY != 0)
                {
                    float dy = (float)(-deltaY / 120.0 * 60.0);
                    float maxScroll = Math.Max(0, box.ScrollContentHeight - box.ContentBox.Height);
                    if (SmoothEnabled)
                    {
                        // Edge-like: inject a fast-decaying velocity. Each 60px notch
                        // becomes ~60px of motion over ~0.25 s (the host's element
                        // decay lambda is high), continuous under rapid wheel input.
                        box.ScrollVelY += dy * ElementWheelVelScale;
                        box.IsSmoothScrollingY = true;
                        box.IsBouncingY = false;
                        box.TargetScrollY = float.NaN;
                    }
                    else
                    {
                        box.ScrollY = Math.Clamp(box.ScrollY + dy, 0, maxScroll);
                        box.IsSmoothScrollingY = false;
                        box.ScrollVelY = 0;
                    }
                    consumed = true;
                }
                if (deltaX != 0)
                {
                    // Same direction convention as the page scroller: positive
                    // deltaX scrolls right (content moves left).
                    float dx = (float)(deltaX / 120.0 * 60.0);
                    float maxScroll = Math.Max(0, box.ScrollContentWidth - box.ContentBox.Width);
                    if (SmoothEnabled)
                    {
                        box.ScrollVelX += dx * ElementWheelVelScale;
                        box.IsSmoothScrollingX = true;
                        box.IsBouncingX = false;
                        box.TargetScrollX = float.NaN;
                    }
                    else
                    {
                        box.ScrollX = Math.Clamp(box.ScrollX + dx, 0, maxScroll);
                        box.IsSmoothScrollingX = false;
                        box.ScrollVelX = 0;
                    }
                    consumed = true;
                }
                if (consumed) OnScrollChanged?.Invoke(box);
                return consumed;
            }
            el = el.ParentElement;
        }
        return false;
    }

    // ════════════════════════════════════════════════════════════════
    // Mouse down �?scrollbar thumb grab or track jump
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle mousedown at page-space position. Checks whether the point falls
    /// within any visible scrollbar area (thumb or track) of a scroll container.
    /// </summary>
    /// <returns>true if a scrollbar was hit and the event is consumed.</returns>
    public bool HandleMouseDown(float pageX, float pageY)
    {
        var el = HitTestDeepest(_document, pageX, pageY);
        while (el != null)
        {
            var box = el.LayoutBox;
            if (box != null && IsUsableScroller(box))
            {
                // #1: mouse must be within the container visible area
                var bbv = box.BorderBox;
                if (pageX < bbv.Left || pageX > bbv.Right || pageY < bbv.Top || pageY > bbv.Bottom)
                { el = el.ParentElement; continue; }

                var st = el.ComputedStyle;
                if (st == null) { el = el.ParentElement; continue; }

                float thickness = ResolveThickness(st);
                if (thickness <= 0) { el = el.ParentElement; continue; }

                bool vOvf = box.ScrollContentHeight > box.ContentBox.Height;
                bool hOvf = box.ScrollContentWidth > box.ContentBox.Width;
                bool hasV = vOvf || st.OverflowY == OverflowType.Scroll;
                bool hasH = hOvf || st.OverflowX == OverflowType.Scroll;

                var pb = box.PaddingBox;

                // Vertical scrollbar strip �?track shrinks when the horizontal
                // bar is present, mirroring ScrollableAreaPainter geometry so
                // thumb drag / track click reach the true bottom.
                if (hasV)
                {
                    float trackH = Math.Max(0, pb.Height - (hasH ? thickness : 0));
                    float sbLeft = pb.Right - thickness;
                    if (pageX >= sbLeft && pageX <= pb.Right &&
                        pageY >= pb.Top && pageY <= pb.Top + trackH)
                    {
                        return BeginVerticalDrag(box, el, pageY, trackH);
                    }
                }

                // Horizontal scrollbar strip �?track shrinks when the vertical
                // bar is present, mirroring ScrollableAreaPainter geometry.
                if (hasH)
                {
                    float trackW = Math.Max(0, pb.Width - (hasV ? thickness : 0));
                    float sbTop = pb.Bottom - thickness;
                    if (pageY >= sbTop && pageY <= pb.Bottom &&
                        pageX >= pb.Left && pageX <= pb.Left + trackW)
                    {
                        return BeginHorizontalDrag(box, el, pageX, trackW);
                    }
                }
            }
            el = el.ParentElement;
        }
        return false;
    }

    private bool BeginVerticalDrag(LayoutBox box, Element containerEl, float pageY, float trackH)
    {
        var pb = box.PaddingBox;
        float thumbRatio = box.ContentBox.Height / Math.Max(1, box.ScrollContentHeight);
        float thumbH = Math.Max(20f, trackH * Math.Min(1, thumbRatio));
        float maxScroll = Math.Max(1, box.ScrollContentHeight - box.ContentBox.Height);

        // Current thumb top relative to track origin
        float thumbTop = maxScroll > 0
            ? (trackH - thumbH) * (Math.Clamp(box.ScrollY, 0, maxScroll) / maxScroll)
            : 0;

        float localY = pageY - pb.Top;

        if (localY >= thumbTop && localY <= thumbTop + thumbH)
        {
            // Grabbed thumb: store offset from thumb top so it doesn't snap.
            _dragContainer = containerEl;
            _dragVertical = true;
            _dragGrabOffset = localY - thumbTop;
            _dragTrackOrigin = pb.Top;
            _dragRange = trackH - thumbH;
            _dragging = true;
            return true;
        }
        else
        {
            // Track click: jump so that clicked point becomes thumb center.
            float frac = Math.Clamp((localY - thumbH / 2) / Math.Max(1, trackH - thumbH), 0, 1);
            box.ScrollY = frac * maxScroll;
            OnScrollChanged?.Invoke(box);
            return true;
        }
    }

    private bool BeginHorizontalDrag(LayoutBox box, Element containerEl, float pageX, float trackW)
    {
        var pb = box.PaddingBox;
        float thumbRatio = box.ContentBox.Width / Math.Max(1, box.ScrollContentWidth);
        float thumbW = Math.Max(20f, trackW * Math.Min(1, thumbRatio));
        float maxScroll = Math.Max(1, box.ScrollContentWidth - box.ContentBox.Width);

        float thumbLeft = maxScroll > 0
            ? (trackW - thumbW) * (Math.Clamp(box.ScrollX, 0, maxScroll) / maxScroll)
            : 0;

        float localX = pageX - pb.Left;

        if (localX >= thumbLeft && localX <= thumbLeft + thumbW)
        {
            _dragContainer = containerEl;
            _dragVertical = false;
            _dragGrabOffset = localX - thumbLeft;
            _dragTrackOrigin = pb.Left;
            _dragRange = trackW - thumbW;
            _dragging = true;
            return true;
        }
        else
        {
            float frac = Math.Clamp((localX - thumbW / 2) / Math.Max(1, trackW - thumbW), 0, 1);
            box.ScrollX = frac * maxScroll;
            OnScrollChanged?.Invoke(box);
            return true;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Mouse move �?drag update + hover tracking
    // ════════════════════════════════════════════════════════════════

    /// <summary>Handle mousemove. Updates drag position if dragging.</summary>
    public void HandleMouseMove(float pageX, float pageY)
    {
        if (!_dragging || _dragContainer?.LayoutBox is not { } box) return;

        if (_dragVertical)
        {
            float localY = pageY - _dragTrackOrigin;
            float frac = _dragRange > 0
                ? Math.Clamp((localY - _dragGrabOffset) / _dragRange, 0, 1)
                : 0;
            float maxScroll = Math.Max(0, box.ScrollContentHeight - box.ContentBox.Height);
            float newScroll = frac * maxScroll;
            if (MathF.Abs(newScroll - box.ScrollY) > 0.1f)
            {
                box.ScrollY = newScroll;
                box.IsSmoothScrollingY = false;
                box.ScrollVelY = 0;
                OnScrollChanged?.Invoke(box);
            }
        }
        else
        {
            float localX = pageX - _dragTrackOrigin;
            float frac = _dragRange > 0
                ? Math.Clamp((localX - _dragGrabOffset) / _dragRange, 0, 1)
                : 0;
            float maxScroll = Math.Max(0, box.ScrollContentWidth - box.ContentBox.Width);
            float newScroll = frac * maxScroll;
            if (MathF.Abs(newScroll - box.ScrollX) > 0.1f)
            {
                box.ScrollX = newScroll;
                box.IsSmoothScrollingX = false;
                box.ScrollVelX = 0;
                OnScrollChanged?.Invoke(box);
            }
        }
    }

    /// <summary>Handle mouseup �?end drag.</summary>
    public void HandleMouseUp()
    {
        _dragging = false;
        _dragContainer = null;
    }

    // ════════════════════════════════════════════════════════════════
    // Internal helpers
    // ════════════════════════════════════════════════════════════════

    private static bool IsUsableScroller(LayoutBox box) =>
        box.IsScrollContainer &&
        box.ContentBox.Height > 0 && box.ContentBox.Width > 0 &&
        (box.ScrollContentHeight > box.ContentBox.Height ||
         box.ScrollContentWidth > box.ContentBox.Width);

    private static Element? HitTestDeepest(Document? doc, float x, float y)
    {
        Element? result = null;
        void Walk(Element e)
        {
            var b = e.LayoutBox;
            if (b != null && x >= b.BorderBox.Left && x <= b.BorderBox.Right &&
                y >= b.BorderBox.Top && y <= b.BorderBox.Bottom)
                result = e;
            foreach (var c in e.Children)
                if (c is Element ce) Walk(ce);
        }
        if (doc?.DocumentElement != null) Walk(doc.DocumentElement);
        if (result == null && doc?.Body != null) Walk(doc.Body);
        return result;
    }

    private static float ResolveThickness(ComputedStyle style) => ScrollbarMetrics.ThicknessFor(style);
}
