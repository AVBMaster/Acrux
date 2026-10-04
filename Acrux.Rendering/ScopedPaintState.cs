using SkiaSharp;

namespace Acrux.Rendering;

/// <summary>
/// Object-local paint state translated from scoped_paint_state.cc.
/// Tracks the paint offset and cull rect while balancing display-list state
/// operations in reverse order when the scope ends.
/// </summary>
internal sealed class ScopedPaintState : IDisposable
{
    private readonly DisplayList _displayList;
    private readonly List<LayerState> _states = new();
    private bool _disposed;

    public SKPoint PaintOffset { get; }
    public SKRect CullRect { get; private set; }

    public ScopedPaintState(DisplayList displayList, SKPoint paintOffset, SKRect cullRect)
    {
        _displayList = displayList;
        PaintOffset = paintOffset;
        CullRect = cullRect;
    }

    public bool LocalRectIntersectsCullRect(SKRect localRect)
    {
        localRect.Offset(PaintOffset.X, PaintOffset.Y);
        return CullRect.IntersectsWith(localRect);
    }

    public void PushLayer(float opacity, SKImageFilter? imageFilter, SKPath? clipPath, SKRect bounds, SKImage? maskImage = null, SKBlendMode blendMode = SKBlendMode.SrcOver)
    {
        var op = PaintOpPool.GetPushLayerOp();
        op.Opacity = opacity;
        op.ImageFilter = imageFilter;
        op.ClipPath = clipPath;
        op.MaskImage = maskImage;
        op.BlendMode = blendMode;
        op.Bounds = bounds;
        _displayList.Add(op);
        _states.Add(new LayerState(StateType.Layer, maskImage, bounds));
    }

    public bool PushTransform(SKMatrix matrix, SKRect bounds)
    {
        if (matrix.IsIdentity)
            return false;

        var op = PaintOpPool.GetPushTransformOp();
        op.Matrix = matrix;
        op.Bounds = bounds;
        _displayList.Add(op);
        _states.Add(new LayerState(StateType.Transform, null, SKRect.Empty));
        return true;
    }

    public bool PushClip(SKRect clipRect)
    {
        if (clipRect.Width <= 0 || clipRect.Height <= 0)
            return false;

        var op = PaintOpPool.GetPushClipOp();
        op.ClipRect = clipRect;
        op.Bounds = clipRect;
        _displayList.Add(op);
        _states.Add(new LayerState(StateType.Clip, null, clipRect));
        CullRect.Intersect(clipRect);
        return true;
    }

    public bool PushClipPath(SKPath clipPath)
    {
        var bounds = clipPath.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        var op = PaintOpPool.GetPushClipOp();
        op.ClipPath = new SKPath(clipPath);
        op.AntiAlias = true;
        op.Bounds = bounds;
        _displayList.Add(op);
        _states.Add(new LayerState(StateType.Clip, null, bounds));
        CullRect.Intersect(bounds);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        for (int i = _states.Count - 1; i >= 0; i--)
        {
            var state = _states[i];
            // The mask multiplies everything the layer accumulated so far, so it
            // goes inside the layer: emitted just before the matching pop.
            if (state.Type == StateType.Layer && state.Mask != null)
            {
                var maskOp = PaintOpPool.GetMaskApplyOp();
                maskOp.Mask = state.Mask;
                maskOp.Rect = state.Bounds;
                maskOp.Bounds = state.Bounds;
                _displayList.Add(maskOp);
            }

            PaintOp op = state.Type switch
            {
                StateType.Layer => PaintOpPool.GetPopLayerOp(),
                StateType.Clip => PaintOpPool.GetPopClipOp(),
                _ => PaintOpPool.GetPopTransformOp(),
            };
            _displayList.Add(op);
        }

        _disposed = true;
    }

    private readonly record struct LayerState(StateType Type, SKImage? Mask, SKRect Bounds);

    private enum StateType
    {
        Layer,
        Transform,
        Clip,
    }
}
