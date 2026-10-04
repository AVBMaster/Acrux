using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// A reflection, as created by -webkit-box-reflect. Mirrors BoxReflection.
/// Consists of a direction, an offset applied after flipping about the x- or
/// y-axis, and an optional mask image applied to the reflection before the
/// reflection matrix is applied.
/// </summary>
public sealed class BoxReflection
{
    public enum ReflectionDirection
    {
        /// <summary>Vertically flipped (to appear above or below).</summary>
        VerticalReflection,
        /// <summary>Horizontally flipped (to appear to the left or right).</summary>
        HorizontalReflection,
    }

    public ReflectionDirection Direction { get; }
    public float Offset { get; }
    public SKRect MaskBounds { get; }

    /// <summary>Mask paint record; null when no mask image.</summary>
    public object? Mask { get; }

    public BoxReflection(ReflectionDirection direction, float offset)
        : this(direction, offset, null, SKRect.Empty)
    {
    }

    public BoxReflection(ReflectionDirection direction, float offset, object? mask, SKRect maskBounds)
    {
        Direction = direction;
        Offset = offset;
        Mask = mask;
        MaskBounds = maskBounds;
    }

    public bool HasMask => Mask != null;

    /// <summary>
    /// Matrix mapping points between the original content and its reflection.
    /// Reflections are self-inverse, so this can be used in either direction.
    /// </summary>
    public SKMatrix ReflectionMatrix()
    {
        switch (Direction)
        {
            case ReflectionDirection.VerticalReflection:
                // Scale(1,-1) then translate by offset along y.
                var vScale = SKMatrix.CreateScaleTranslation(1, -1, 0, 0);
                return SKMatrix.Concat(SKMatrix.CreateTranslation(0, Offset), vScale);
            case ReflectionDirection.HorizontalReflection:
                // Scale(-1,1) then translate by offset along x.
                var hScale = SKMatrix.CreateScaleTranslation(-1, 1, 0, 0);
                return SKMatrix.Concat(SKMatrix.CreateTranslation(Offset, 0), hScale);
            default:
                return SKMatrix.Identity;
        }
    }

    /// <summary>
    /// Maps a source rectangle to the destination rectangle it can affect,
    /// including this reflection. Due to the symmetry of reflections this can
    /// also map from a destination rectangle back to the source rectangle that
    /// contributes to it.
    /// </summary>
    public SKRect MapRect(SKRect rect)
    {
        var reflected = ReflectionMatrix().MapRect(rect);
        return SKRect.Create(
            MathF.Min(rect.Left, reflected.Left),
            MathF.Min(rect.Top, reflected.Top),
            MathF.Max(rect.Right, reflected.Right) - MathF.Min(rect.Left, reflected.Left),
            MathF.Max(rect.Bottom, reflected.Bottom) - MathF.Min(rect.Top, reflected.Top));
    }
}

/// <summary>
/// Utilities for constructing box reflections in terms of core concepts (PaintLayer).
/// Mirrors box_reflection_utils.h/cc.
/// </summary>
public static class BoxReflectionUtils
{
    /// <summary>
    /// Builds the BoxReflection for the given paint layer, if the style has a
    /// -webkit-box-reflect. Ported from BoxReflectionForPaintLayer() in
    /// box_reflection_utils.cc. The mask image is rendered by the caller through
    /// NinePieceImagePainter; here only the mask bounding rect is threaded
    /// through so the reflection painting can apply the mask.
    /// </summary>
    public static BoxReflection ForPaintLayer(ComputedStyle style, SKSize frameSize, SKRect maskBoundingRect, object? maskRecord)
    {
        var reflectStyle = style.BoxReflect;
        if (reflectStyle == null)
            return new BoxReflection(BoxReflection.ReflectionDirection.VerticalReflection, 0);

        var direction = BoxReflection.ReflectionDirection.VerticalReflection;
        float offset = 0;
        switch (reflectStyle.Direction)
        {
            case ReflectionDirectionType.ReflectionAbove:
                direction = BoxReflection.ReflectionDirection.VerticalReflection;
                offset = -FloatValueForLength(reflectStyle.Offset, frameSize.Height);
                break;
            case ReflectionDirectionType.ReflectionBelow:
                direction = BoxReflection.ReflectionDirection.VerticalReflection;
                offset = 2 * frameSize.Height + FloatValueForLength(reflectStyle.Offset, frameSize.Height);
                break;
            case ReflectionDirectionType.ReflectionLeft:
                direction = BoxReflection.ReflectionDirection.HorizontalReflection;
                offset = -FloatValueForLength(reflectStyle.Offset, frameSize.Width);
                break;
            case ReflectionDirectionType.ReflectionRight:
                direction = BoxReflection.ReflectionDirection.HorizontalReflection;
                offset = 2 * frameSize.Width + FloatValueForLength(reflectStyle.Offset, frameSize.Width);
                break;
        }

        return new BoxReflection(direction, offset, maskRecord, maskBoundingRect);
    }

    private static float FloatValueForLength(Length? length, float referenceSize)
    {
        if (length == null) return 0;
        return length.ToPixels(referenceSize, 16, referenceSize, referenceSize);
    }
}