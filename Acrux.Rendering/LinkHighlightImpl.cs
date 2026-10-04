using SkiaSharp;

namespace Acrux.Rendering;

/// <summary>
/// An opt-in "tap flash" highlight for focused/clicked anchors. Renders a
/// translucent rounded rect inset a few pixels OUTSIDE the target box bounds
/// (like the reference link-highlight overlay used for touch feedback), so the
/// user sees which link just received a tap. This painter is intentionally NOT
/// part of the default pipeline: the app/input layer decides when to invoke it
/// (e.g. on pointer-down on an anchor) and clears it on pointer-up/commit.
/// The highlight is painted as a display-list op so it participates in the same
/// batching, culling and (if enabled) tile caching as regular content.
/// </summary>
public sealed class LinkHighlightImpl
{
    public const float DefaultRadius = 8f;
    public const float DefaultOutset = 3f;
    public static readonly SKColor DefaultColor = new(0x2E, 0x6B, 0xE6, 0x40);

    private readonly DisplayList _displayList;

    public LinkHighlightImpl(DisplayList displayList)
    {
        _displayList = displayList;
    }

    /// <summary>
    /// Emit the highlight overlay op for <paramref name="box"/>. The rect is the
    /// box's margin box expanded by <paramref name="outset"/> on every side,
    /// clipped to nothing (the caller is responsible for visibility). Colors are
    /// translucent so the link remains readable underneath.
    /// </summary>
    public void Paint(SKRect boxBounds, float radius = DefaultRadius, float outset = DefaultOutset, SKColor? color = null)
    {
        var rect = new SKRect(
            boxBounds.Left - outset,
            boxBounds.Top - outset,
            boxBounds.Right + outset,
            boxBounds.Bottom + outset);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = rect;
        op.FillColor = color ?? DefaultColor;
        op.BorderRadius = radius;
        op.Bounds = rect;
        _displayList.Add(op);
    }
}