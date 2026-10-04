using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Style difference flags - mirrors the engine's StyleDifference.
/// </summary>
[Flags]
public enum StyleDifference
{
    None = 0,
    Layout = 1 << 0,
    Paint = 1 << 1,
    PropertyChange = 1 << 2,
    RecomputeOverflow = 1 << 3,
    Transform = 1 << 4,
    Composite = 1 << 5,
}

/// <summary>
/// Paint info - minimal paint context mirroring the engine's PaintInfo.
/// </summary>
public sealed class PaintInfo
{
    public PaintInfo(PhysicalOffset paintOffset)
    {
        PaintOffset = paintOffset;
    }

    public PhysicalOffset PaintOffset { get; }
}

/// <summary>
/// Paint invalidator context - mirror of the engine's PaintInvalidatorContext.
/// Carries subtree flags and enclosing state down the invalidation walk.
/// </summary>
public sealed class PaintInvalidatorContext
{
    public PaintInvalidatorContext? Parent { get; set; }
    public PaintInvalidationSubtreeFlags SubtreeFlags { get; set; }

    public bool NeedsSubtreeWalk => (SubtreeFlags & (PaintInvalidationSubtreeFlags.SubtreeInvalidationChecking
        | PaintInvalidationSubtreeFlags.SubtreeFullInvalidation
        | PaintInvalidationSubtreeFlags.SubtreeFullInvalidationForStackedContents)) != 0;

    /// <summary>Indicates the walk has entered a region where no invalidation may be issued.</summary>
    public bool SubtreeNoInvalidation => (SubtreeFlags & PaintInvalidationSubtreeFlags.SubtreeNoInvalidation) != 0;

    /// <summary>The enclosing paint layer, if any.</summary>
    public object? PaintingLayer { get; set; }

    /// <summary>Per-fragment paint data blob attached via the invalidator.</summary>
    public object? FragmentData { get; set; }
}

/// <summary>
/// Subtree-flags carried down the paint-invalidation walk. Mirrors the
/// PaintInvalidatorContext::SubtreeFlag enum in paint_invalidator.h.
/// </summary>
[Flags]
public enum PaintInvalidationSubtreeFlags
{
    None = 0,
    SubtreeInvalidationChecking = 1 << 0,
    SubtreeFullInvalidation = 1 << 1,
    SubtreeFullInvalidationForStackedContents = 1 << 2,
    SubtreeNoInvalidation = 1 << 6,
}

/// <summary>
/// Cursor directive - mirror of the engine's CursorDirective.
/// </summary>
public enum CursorDirective
{
    SetCursorBasedOnStyle,
    SetCursor,
    DoNotSetCursor,
}

/// <summary>
/// System cursor placeholder - mirror of ui::Cursor.
/// </summary>
public sealed class Cursor
{
}

/// <summary>
/// Affine transform - minimal 2D affine transform placeholder.
/// </summary>
public sealed class AffineTransform
{
    public AffineTransform()
    {
    }

    public AffineTransform(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
    }

    public double A { get; set; }
    public double B { get; set; }
    public double C { get; set; }
    public double D { get; set; }
    public double E { get; set; }
    public double F { get; set; }
}

/// <summary>
/// Paint layer type - mirror of the engine's PaintLayerType.
/// </summary>
public enum PaintLayerType
{
    NoPaintLayer,
    ForcedPaintLayer,
    NormalPaintLayer,
}

/// <summary>
/// Compositing reasons - mirror of the engine's CompositingReasons.
/// </summary>
[Flags]
public enum CompositingReasons
{
    None = 0,
    Video = 1 << 0,
    Canvas = 1 << 1,
    Plugin = 1 << 2,
    Transform3D = 1 << 3,
    WillChange = 1 << 4,
    BackdropFilter = 1 << 5,
}

/// <summary>
/// Wrapped image pointer - mirror of the engine's WrappedImagePtr.
/// </summary>
public sealed class WrappedImagePtr
{
}

/// <summary>
/// CanDeferInvalidation - mirror of the engine's CanDeferInvalidation.
/// </summary>
public enum CanDeferInvalidation
{
    No,
    Yes,
}

/// <summary>
/// Position - editing position placeholder mirroring the engine's Position.
/// </summary>
public sealed class Position
{
    public Position()
    {
    }
}

/// <summary>
/// PositionWithAffinity - position plus upstream affinity.
/// </summary>
public sealed class PositionWithAffinity
{
    public PositionWithAffinity(Position? position)
    {
        Position = position;
    }

    public Position? Position { get; }
}

/// <summary>
/// Embedded content view - mirror of the engine's EmbeddedContentView base.
/// </summary>
public class EmbeddedContentView
{
}

/// <summary>
/// Web plugin container - mirror of the engine's WebPluginContainerImpl.
/// </summary>
public sealed class WebPluginContainerImpl
{
}

/// <summary>
/// Font placeholder - mirror of the engine's Font.
/// </summary>
public sealed class Font
{
}

/// <summary>
/// Inline cursor - mirror of the engine's InlineCursor.
/// </summary>
public sealed class InlineCursor
{
}

/// <summary>
/// Line-relative rect used in painting - mirror of the engine's LineRelativeRect.
/// </summary>
public readonly struct LineRelativeRect
{
}

/// <summary>
/// First letter pseudo element placeholder.
/// </summary>
public sealed class FirstLetterPseudoElement
{
    public LayoutObject? LayoutObject { get; set; }
}
