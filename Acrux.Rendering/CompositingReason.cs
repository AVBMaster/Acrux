namespace Acrux.Rendering;

/// <summary>
/// Reasons an element should be promoted to a composited layer. Mirrors the
/// per-category classification from the reference painting pipeline, reduced to
/// the reasons that are computable from a <c>ComputedStyle</c> alone.
///
/// This is a plain flags enum intended to be combined with bitwise OR. It is
/// consumed only through direct enumeration (no serialization / reflection), so
/// it is AOT-safe.
/// </summary>
[Flags]
public enum CompositingReason : ulong
{
    None = 0,

    // Transform causes.
    /// <summary>A transform with a non-trivial 3D component (e.g. matrix3d, translateZ, rotateX/Y).</summary>
    Transform3D = 1UL << 0,

    // will-change hints.
    WillChangeTransform = 1UL << 1,
    WillChangeScale = 1UL << 2,
    WillChangeRotate = 1UL << 3,
    WillChangeTranslate = 1UL << 4,
    WillChangeOpacity = 1UL << 5,
    WillChangeFilter = 1UL << 6,
    WillChangeBackdropFilter = 1UL << 7,
    /// <summary>Generic "will-change" hint on a compositable property with no dedicated flag.</summary>
    WillChangeOther = 1UL << 8,

    // Position / scroll causes.
    FixedPosition = 1UL << 9,
    StickyPosition = 1UL << 10,
    /// <summary>Scrollable content on the element (overflow != visible); hint for composited scrolling.</summary>
    ScrollableContent = 1UL << 11,

    // Media / embedding element causes. These are element kinds that the
    // reference promotes unconditionally; the current style model does not carry
    // element kind, so they are currently not produced (see finder notes).
    Video = 1UL << 12,
    Iframe = 1UL << 13,

    // Effect causes.
    /// <summary>A blend mode other than normal.</summary>
    BlendMode = 1UL << 14,
    /// <summary>An (opaque) CSS filter that must be rasterised into its own surface.</summary>
    OpaqueFilter = 1UL << 15,
    BackdropFilter = 1UL << 16,
    /// <summary>A mask applied to the element.</summary>
    Mask = 1UL << 17,
    /// <summary>A non-affine clip-path that forces its own render surface.</summary>
    ClipPath = 1UL << 18,

    // Active animations.
    ActiveTransformAnimation = 1UL << 19,
    ActiveOpacityAnimation = 1UL << 20,
    ActiveFilterAnimation = 1UL << 21,
}
