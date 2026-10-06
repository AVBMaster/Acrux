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
    /// <summary>Whether (<paramref name="x"/>,<paramref name="y"/>) — document coordinates —
    /// is clipped away by <paramref name="element"/> or by any ancestor's clip-path.</summary>
    public static bool IsClippedAway(Element? element, float x, float y)
    {
        for (; element != null; element = element.ParentElement)
        {
            string? clip = element.ComputedStyle?.ClipPath;
            if (!ClipPathClipper.HasClipPath(clip)) continue;
            var box = element.LayoutBox;
            if (box == null) continue;

            SKPath? path = ClipPathClipper.Parse(clip, box);
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
