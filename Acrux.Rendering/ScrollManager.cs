using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

public class ScrollManager
{
    public float ScrollX { get; set; }
    public float ScrollY { get; set; }
    public float MaxScrollY { get; set; }
    public float MaxScrollX { get; set; }
    public bool HasVerticalScroll { get; set; }
    public bool HasHorizontalScroll { get; set; }
    public float ViewportWidth => _viewportWidth;
    public float ViewportHeight => _viewportHeight;
    public float ContentWidth { get; private set; }
    public float ContentHeight { get; private set; }

    /// <summary>
    /// Master smooth-scrolling switch (mirrors the settings page toggle). When
    /// false every wheel / key / programmatic scroll applies its offset instantly;
    /// when true the scroll offset animates toward the requested target with an
    /// Edge-like ease-out (~0.2 s per step, continuous under rapid wheel input).
    /// </summary>
    public bool SmoothEnabled { get; set; } = true;

    public bool CanScrollY => MaxScrollY > 0;
    public bool CanScrollX => MaxScrollX > 0;
    public float ScrollableHeight => Math.Max(0, ContentHeight - ViewportHeight);
    public float ScrollableWidth => Math.Max(0, ContentWidth - ViewportWidth);

    public const float ScrollbarWidth = 12;
    public const float ScrollbarMinThumbSize = 20;

    // Physics constants
    private const float DecayLambda = 3.5f;
    private const float MinVel = 1f;
    private const float BounceK = 150f;
    private const float BounceDamp = 25f;
    // Target-follow rate for the Edge-like ease-out chase. rate=10 closes 95% of
    // the gap in ~0.3 s (a single 60px wheel notch glides visibly), while rapid
    // wheel input keeps advancing the target so the motion stays continuous.
    private const float SnapFollowRate = 10f;

    // State
    public bool IsSmoothScrollingY { get; private set; }
    public bool IsSmoothScrollingX { get; private set; }
    private float _velY, _velX;
    private bool _bounceY, _bounceX;
    private float _snapTargetY, _snapTargetX;
    private bool _snapY, _snapX;

    private Dictionary<string, ScrollContainer> _scrollContainers = new();
    private float _viewportWidth;
    private float _viewportHeight;

    public void UpdateScroll(float contentWidth, float contentHeight, float viewportWidth, float viewportHeight)
    {
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;

        MaxScrollX = Math.Max(0, contentWidth - viewportWidth);
        MaxScrollY = Math.Max(0, contentHeight - viewportHeight);

        HasHorizontalScroll = MaxScrollX > 0;
        HasVerticalScroll = MaxScrollY > 0;

        if (!IsSmoothScrollingY)
            ScrollY = Math.Max(0, Math.Min(ScrollY, MaxScrollY));
        if (!IsSmoothScrollingX)
            ScrollX = Math.Max(0, Math.Min(ScrollX, MaxScrollX));
    }

    public void UpdateScroll(float contentHeight, float viewportHeight)
    {
        UpdateScroll(ContentWidth, contentHeight, ViewportWidth, viewportHeight);
    }

    public bool UpdateSmoothScroll(float dt)
    {
        bool moving = false;
        // Accommodate low frame rates (e.g. 15fps) without slowing the animation;
        // the exact exponential chase is stable for any dt.
        dt = Math.Min(dt, 0.1f);

        // ── Vertical ──
        if (_snapY)
        {
            _snapTargetY = Math.Clamp(_snapTargetY, 0, MaxScrollY);
            float diff = _snapTargetY - ScrollY;
            if (Math.Abs(diff) < 0.5f && Math.Abs(_velY) < 1f)
            {
                ScrollY = _snapTargetY;
                _velY = 0;
                _snapY = false;
                IsSmoothScrollingY = false;
            }
            else
            {
                // Exact exponential ease-out chase (Edge-like): closes a fixed
                // fraction of the remaining gap per unit time, so a single step
                // settles in ~0.2s. Uses 1-exp(-rate·dt) — always in (0,1), so it
                // is stable at ANY frame rate (a naive diff*rate*dt overshoots and
                // oscillates at low fps).
                float t = 1f - MathF.Exp(-SnapFollowRate * dt);
                ScrollY += diff * t;
                if (ScrollY < 0) ScrollY = 0;
                else if (ScrollY > MaxScrollY) ScrollY = MaxScrollY;
                moving = true;
            }
        }
        else if (_bounceY)
        {
            float boundary = ScrollY < 0 ? 0 : MaxScrollY;
            float diff = boundary - ScrollY;
            float force = diff * BounceK - _velY * BounceDamp;
            _velY += force * dt;
            _velY *= 0.97f;
            ScrollY += _velY * dt;
            if (Math.Abs(diff) < 0.5f && Math.Abs(_velY) < 5f)
            {
                ScrollY = boundary; _velY = 0; _bounceY = false; IsSmoothScrollingY = false;
            }
            else moving = true;
        }
        else if (Math.Abs(_velY) > MinVel)
        {
            _velY *= MathF.Exp(-DecayLambda * dt);
            ScrollY += _velY * dt;
            if (ScrollY < 0) { _bounceY = true; _velY *= 0.5f; }
            else if (ScrollY > MaxScrollY) { _bounceY = true; _velY *= 0.5f; }
            else if (Math.Abs(_velY) < MinVel) { _velY = 0; IsSmoothScrollingY = false; }
            else moving = true;
        }
        else
        {
            IsSmoothScrollingY = false;
        }

        // ── Horizontal ──
        if (_snapX)
        {
            _snapTargetX = Math.Clamp(_snapTargetX, 0, MaxScrollX);
            float diff = _snapTargetX - ScrollX;
            if (Math.Abs(diff) < 0.5f && Math.Abs(_velX) < 1f)
            {
                ScrollX = _snapTargetX;
                _velX = 0;
                _snapX = false;
                IsSmoothScrollingX = false;
            }
            else
            {
                float t = 1f - MathF.Exp(-SnapFollowRate * dt);
                ScrollX += diff * t;
                if (ScrollX < 0) ScrollX = 0;
                else if (ScrollX > MaxScrollX) ScrollX = MaxScrollX;
                moving = true;
            }
        }
        else if (_bounceX)
        {
            float boundary = ScrollX < 0 ? 0 : MaxScrollX;
            float diff = boundary - ScrollX;
            float force = diff * BounceK - _velX * BounceDamp;
            _velX += force * dt;
            _velX *= 0.97f;
            ScrollX += _velX * dt;
            if (Math.Abs(diff) < 0.5f && Math.Abs(_velX) < 5f)
            {
                ScrollX = boundary; _velX = 0; _bounceX = false; IsSmoothScrollingX = false;
            }
            else moving = true;
        }
        else if (Math.Abs(_velX) > MinVel)
        {
            _velX *= MathF.Exp(-DecayLambda * dt);
            ScrollX += _velX * dt;
            if (ScrollX < 0) { _bounceX = true; _velX *= 0.5f; }
            else if (ScrollX > MaxScrollX) { _bounceX = true; _velX *= 0.5f; }
            else if (Math.Abs(_velX) < MinVel) { _velX = 0; IsSmoothScrollingX = false; }
            else moving = true;
        }
        else
        {
            IsSmoothScrollingX = false;
        }

        return moving;
    }

    // ── Wheel / impulse scrolling ──
    // Edge-like: each wheel delta advances the scroll TARGET; the animation chases
    // it with the exponential ease-out. Rapid input keeps the target moving, so the
    // content glides continuously; the last delta leaves a brief deceleration.
    public void ScrollBy(float delta, bool smooth = true)
    {
        float scrollAmount = -delta / 120.0f * 60.0f;
        if (smooth && SmoothEnabled)
        {
            _snapTargetY += scrollAmount;
            _snapTargetY = Math.Clamp(_snapTargetY, 0, MaxScrollY);
            _velY = 0;
            _snapY = true;
            _bounceY = false;
            IsSmoothScrollingY = true;
        }
        else
        {
            ScrollY = Math.Clamp(ScrollY + scrollAmount, 0, MaxScrollY);
            _velY = 0;
            _snapY = _bounceY = false;
            IsSmoothScrollingY = false;
        }
    }

    public void ScrollBy(float deltaX, float deltaY, bool smooth = true)
    {
        if (smooth && SmoothEnabled)
        {
            _snapTargetX += deltaX;
            _snapTargetY += deltaY;
            _snapTargetX = Math.Clamp(_snapTargetX, 0, MaxScrollX);
            _snapTargetY = Math.Clamp(_snapTargetY, 0, MaxScrollY);
            _velX = _velY = 0;
            if (Math.Abs(deltaX) > 0.5f) _snapX = true;
            if (Math.Abs(deltaY) > 0.5f) _snapY = true;
            _bounceX = _bounceY = false;
            IsSmoothScrollingX = _snapX;
            IsSmoothScrollingY = _snapY;
        }
        else
        {
            ScrollX = Math.Clamp(ScrollX + deltaX, 0, MaxScrollX);
            ScrollY = Math.Clamp(ScrollY + deltaY, 0, MaxScrollY);
            _velX = _velY = 0;
            _snapX = _snapY = _bounceX = _bounceY = false;
            IsSmoothScrollingX = IsSmoothScrollingY = false;
        }
    }

    // ── Explicit target scrolling (PageUp/Down, Home/End, arrows, JS scrollTo) ──
    public void ScrollTo(float y, bool smooth = true)
    {
        float target = Math.Clamp(y, 0, MaxScrollY);
        if (smooth && SmoothEnabled && Math.Abs(target - ScrollY) > 0.5f)
        {
            _snapTargetY = target;
            _velY = 0;
            _snapY = true;
            _bounceY = false;
            IsSmoothScrollingY = true;
        }
        else
        {
            ScrollY = target;
            _velY = 0;
            _snapY = _bounceY = false;
            IsSmoothScrollingY = false;
        }
    }

    public void ScrollTo(float x, float y, bool smooth = true)
    {
        _snapTargetX = Math.Clamp(x, 0, MaxScrollX);
        _snapTargetY = Math.Clamp(y, 0, MaxScrollY);
        if (smooth && SmoothEnabled && (Math.Abs(_snapTargetX - ScrollX) > 0.5f || Math.Abs(_snapTargetY - ScrollY) > 0.5f))
        {
            if (Math.Abs(_snapTargetX - ScrollX) > 0.5f) { _snapX = true; _velX = 0; }
            if (Math.Abs(_snapTargetY - ScrollY) > 0.5f) { _snapY = true; _velY = 0; }
            _bounceX = _bounceY = false;
            IsSmoothScrollingX = _snapX;
            IsSmoothScrollingY = _snapY;
        }
        else
        {
            ScrollX = _snapTargetX; ScrollY = _snapTargetY;
            _velX = _velY = 0;
            _snapX = _snapY = _bounceX = _bounceY = false;
            IsSmoothScrollingX = IsSmoothScrollingY = false;
        }
    }

    public void ScrollToInstant(float y)
    {
        ScrollY = Math.Clamp(y, 0, MaxScrollY);
        _velY = 0; _snapY = _bounceY = false; IsSmoothScrollingY = false;
    }

    public void ScrollToInstant(float x, float y)
    {
        ScrollX = Math.Clamp(x, 0, MaxScrollX);
        ScrollY = Math.Clamp(y, 0, MaxScrollY);
        _velX = _velY = 0;
        _snapX = _snapY = _bounceX = _bounceY = false;
        IsSmoothScrollingX = IsSmoothScrollingY = false;
    }

    public void PageUp(bool smooth = true) => ScrollTo(ScrollY - _viewportHeight * 0.85f, smooth);
    public void PageDown(bool smooth = true) => ScrollTo(ScrollY + _viewportHeight * 0.85f, smooth);
    public void PageLeft(bool smooth = true) => ScrollTo(ScrollX - _viewportWidth * 0.85f, ScrollY, smooth);
    public void PageRight(bool smooth = true) => ScrollTo(ScrollX + _viewportWidth * 0.85f, ScrollY, smooth);
    public void ScrollHome(bool smooth = true) => ScrollTo(0f, smooth);
    public void ScrollEnd(bool smooth = true) => ScrollTo(MaxScrollY, smooth);
    public void ScrollToTop(bool smooth = true) => ScrollHome(smooth);
    public void ScrollToBottom(bool smooth = true) => ScrollEnd(smooth);

    public bool HitTestVerticalScrollbar(float x, float y, float containerHeight, float containerWidth)
    {
        if (!HasVerticalScroll) return false;
        float scrollbarLeft = containerWidth - ScrollbarWidth;
        return x >= scrollbarLeft && x <= containerWidth && y >= 0 && y <= containerHeight;
    }

    public bool HitTestHorizontalScrollbar(float x, float y, float containerWidth, float containerHeight)
    {
        if (!HasHorizontalScroll) return false;
        float scrollbarTop = containerHeight - ScrollbarWidth;
        float rightEdge = HasVerticalScroll ? containerWidth - ScrollbarWidth : containerWidth;
        return y >= scrollbarTop && y <= containerHeight && x >= 0 && x <= rightEdge;
    }
}

public class ScrollContainer
{
    public string ElementId { get; set; } = "";
    public float ScrollX { get; set; }
    public float ScrollY { get; set; }
    public float ContentWidth { get; set; }
    public float ContentHeight { get; set; }
    public float ViewportWidth { get; set; }
    public float ViewportHeight { get; set; }
    public OverflowType OverflowX { get; set; }
    public OverflowType OverflowY { get; set; }

    public float MaxScrollX => Math.Max(0, ContentWidth - ViewportWidth);
    public float MaxScrollY => Math.Max(0, ContentHeight - ViewportHeight);
    public bool CanScrollX => OverflowX is OverflowType.Scroll or OverflowType.Auto && MaxScrollX > 0;
    public bool CanScrollY => OverflowY is OverflowType.Scroll or OverflowType.Auto && MaxScrollY > 0;

    public bool SetScroll(float x, float y)
    {
        float newX = Math.Clamp(x, 0, MaxScrollX);
        float newY = Math.Clamp(y, 0, MaxScrollY);
        if (Math.Abs(newX - ScrollX) < 0.5f && Math.Abs(newY - ScrollY) < 0.5f)
            return false;
        ScrollX = newX;
        ScrollY = newY;
        return true;
    }

    public void ScrollBy(float deltaX, float deltaY) => SetScroll(ScrollX + deltaX, ScrollY + deltaY);
}
