using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Intrinsic size information for replaced elements (img, video, canvas).
/// Mirrors IntrinsicSizingInfo in intrinsic_sizing_info.h.
/// </summary>
public readonly struct IntrinsicSizingInfo
{
    /// <summary>Intrinsic width and height (natural dimensions).</summary>
    public PhysicalSize Size { get; }

    /// <summary>Intrinsic aspect ratio (width/height).</summary>
    public PhysicalSize AspectRatio { get; }

    /// <summary>Whether the element has an intrinsic width.</summary>
    public bool HasWidth { get; }

    /// <summary>Whether the element has an intrinsic height.</summary>
    public bool HasHeight { get; }

    public IntrinsicSizingInfo(PhysicalSize size, PhysicalSize aspectRatio, bool hasWidth, bool hasHeight)
    {
        Size = size;
        AspectRatio = aspectRatio;
        HasWidth = hasWidth;
        HasHeight = hasHeight;
    }

    public static IntrinsicSizingInfo None => new(PhysicalSize.Zero, PhysicalSize.Zero, false, false);

    public bool IsNone => !HasWidth && !HasHeight && AspectRatio.IsEmpty;
}

/// <summary>Resolve width from height + intrinsic aspect ratio.</summary>
public static class ReplacedSizeUtils
{
    public static float ResolveWidthForRatio(float height, PhysicalSize aspectRatio) =>
        aspectRatio.Height > 0 ? height * aspectRatio.Width / aspectRatio.Height : height;

    public static float ResolveHeightForRatio(float width, PhysicalSize aspectRatio) =>
        aspectRatio.Width > 0 ? width * aspectRatio.Height / aspectRatio.Width : width;

    /// <summary>
    /// Compute the concrete object size per CSS Images 3 § default-sizing.
    /// Mirrors ConcreteObjectSize().
    /// </summary>
    public static PhysicalSize ConcreteObjectSize(IntrinsicSizingInfo sizingInfo, PhysicalSize defaultObjectSize)
    {
        if (sizingInfo.HasWidth && sizingInfo.HasHeight)
            return sizingInfo.Size;

        if (sizingInfo.HasWidth)
        {
            if (sizingInfo.AspectRatio.IsEmpty)
                return new PhysicalSize(sizingInfo.Size.Width, defaultObjectSize.Height);
            return new PhysicalSize(sizingInfo.Size.Width,
                ResolveHeightForRatio(sizingInfo.Size.Width, sizingInfo.AspectRatio));
        }

        if (sizingInfo.HasHeight)
        {
            if (sizingInfo.AspectRatio.IsEmpty)
                return new PhysicalSize(defaultObjectSize.Width, sizingInfo.Size.Height);
            return new PhysicalSize(
                ResolveWidthForRatio(sizingInfo.Size.Height, sizingInfo.AspectRatio), sizingInfo.Size.Height);
        }

        // Neither width nor height known; use aspect ratio with contain constraint.
        if (!sizingInfo.AspectRatio.IsEmpty)
        {
            float w = ResolveWidthForRatio(defaultObjectSize.Height, sizingInfo.AspectRatio);
            if (w <= defaultObjectSize.Width)
                return new PhysicalSize(w, defaultObjectSize.Height);

            float h = ResolveHeightForRatio(defaultObjectSize.Width, sizingInfo.AspectRatio);
            return new PhysicalSize(defaultObjectSize.Width, h);
        }

        // Neither width nor height known and no aspect ratio: fall back to the
        // default object size — 300x150 constrained by the containing block
        // (CSS Sizing 3 §4.4). An indefinite available size must not leak through
        // as an infinite box.
        float Default(float available, float fallback) =>
            float.IsPositiveInfinity(available) || float.IsNaN(available)
                ? fallback
                : Math.Min(available, fallback);
        return new PhysicalSize(Default(defaultObjectSize.Width, 300), Default(defaultObjectSize.Height, 150));
    }
}