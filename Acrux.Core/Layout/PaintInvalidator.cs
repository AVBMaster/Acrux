using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Performance;

namespace Acrux.Core.Layout;

/// <summary>Reason why an element's painting needs invalidation, mirroring the engine's PaintInvalidationReason.</summary>
public enum PaintInvalidationReason
{
    None,
    Incremental,
    Selection,
    HitTest,
    Layout,
    Style,
    Geometry,
    Scroll,
    Background,
    Border,
    Outline,
    Clip,
    Transform,
    Opacity,
    Filter,
    Mask,
    Image,
    Subtree,
    Ancestor,
    Compulsory,
    JustCreated,
    Full
}

/// <summary>
/// Determines why an element's paint needs to be invalidated.
/// Mirrors the engine's ObjectPaintInvalidator and PaintInvalidatorContext.
/// </summary>
public class PaintInvalidator
{
    public PaintInvalidationReason ComputeReason(Element element, bool subtreeChanged, bool applyToAll)
    {
        if (subtreeChanged) return PaintInvalidationReason.Subtree;
        if (applyToAll) return PaintInvalidationReason.Full;

        var flags = DirtyState.GetSelf(element);
        if ((flags & DirtyFlags.Paint) != 0) return PaintInvalidationReason.Layout;
        if ((flags & DirtyFlags.Style) != 0) return PaintInvalidationReason.Style;
        return PaintInvalidationReason.None;
    }

    public SKRect GetPaintInvalidationRect(Element element)
    {
        var box = element.LayoutBox;
        if (box == null) return new SKRect();
        return box.MarginBox;
    }

    public void InvalidatePaint(Element element)
    {
        DirtyState.AddSelf(element, DirtyFlags.Paint);
    }
}