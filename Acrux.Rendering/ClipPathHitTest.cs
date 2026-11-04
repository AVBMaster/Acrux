using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Hit-testing against a CSS clip-path. CSS Masking 1 §1.1 states that a clipping path
/// also clips the pointer: a point outside the shape resolves to whatever is behind the
/// element, and because the clip applies to the element's whole subtree, so do its
/// ancestors' clips. Masking deliberately has no such effect here — a fully transparent
/// mask still takes pointer events (measured against the reference engine).
/// </summary>
public static class ClipPathHitTest
{
    /// <summary>The document's own point test knows the border box and the paint order, not the
    /// shape a clip leaves visible — that geometry is built here. Masking 1 §1.1 puts the clip on
    /// the pointer, so the renderer hands the walk this test as soon as it is loaded; a document
    /// with no renderer in front of it has no clip to consult and keeps the border-box answer.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void WireIntoPointTest()
        => Document.ClippedAway = (element, x, y) => IsClippedAway(element, (float)x, (float)y);

    /// <summary>Whether (<paramref name="x"/>,<paramref name="y"/>) — document coordinates —
    /// is clipped away by <paramref name="element"/> or by any ancestor's clip-path.</summary>
    public static bool IsClippedAway(Element? element, float x, float y)
    {
        for (; element != null; element = element.ParentElement)
        {
            var style = element.ComputedStyle;
            var box = element.LayoutBox;
            if (style == null || box == null) continue;

            string? clip = style.ClipPath;
            bool hasClipPath = ClipPathClipper.HasClipPath(clip);
            // The legacy 'clip' rect takes the pointer with the region it leaves visible,
            // exactly like a clip-path on the same box (CSS Masking 1 §1.1).
            bool hasLegacyClip = LegacyClip.HasClip(style);
            if (!hasClipPath && !hasLegacyClip) continue;

            SKPath? path = hasClipPath ? ClipPathClipper.Parse(clip, box) : null;
            if (hasLegacyClip)
            {
                using var legacy = LegacyClip.Parse(style.Clip, box, style);
                if (legacy != null)
                {
                    if (path == null) path = new SKPath(legacy);
                    else
                    {
                        var shared = new SKPath();
                        if (path.Op(legacy, SKPathOp.Intersect, shared))
                        {
                            path.Dispose();
                            path = shared;
                        }
                        else shared.Dispose();
                    }
                }
            }
            if (path == null) continue;
            try
            {
                // The path is built in document space from the same border box the
                // pointer test uses, so no transform of the point is needed here.
                if (!path.Contains(x, y)) return true;
            }
            finally
            {
                path.Dispose();
            }
        }
        return false;
    }
}
