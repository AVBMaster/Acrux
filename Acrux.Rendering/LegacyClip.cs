using SkiaSharp;
using Acrux.Core.Css;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// The legacy CSS 2.1 §11.1.2 'clip' rectangle, kept alive by CSS Masking 1 §3.1 for
/// compatibility. It is a clip of its own, not a shape: the four numbers of
/// 'rect(top, right, bottom, left)' are all measured from the SAME corner — the top edge
/// gives the y of the top and bottom edges, the left edge the x of the left and right ones —
/// so 'right' is not an inset from the right edge the way 'inset()' spells it. A side written
/// 'auto' sits on the border-box edge it names, and a box whose rectangle has been turned
/// inside out (right of its left, bottom above its top) paints nothing at all.
///
/// The property only has an effect on a box taken out of the flow, which is why the answer to
/// 'does this element clip' asks about 'position' first: a static or relative box carries the
/// value and paints as though it did not (CSS 2.1 §11.1.2, 'applies to: position:absolute').
/// </summary>
internal static class LegacyClip
{
    public static bool HasClip(ComputedStyle? style) =>
        style != null
        && (style.Position == PositionType.Absolute || style.Position == PositionType.Fixed)
        // Asked of the grammar's own split, so the rect that clips the paint is exactly the rect
        // the value text prints and a script reads back. 'auto' says no clip, and so does anything
        // the property refused.
        && CssValueGrammar.LegacyClipSides(style.Clip) != null;

    /// <summary>The clip region in the box's own layout space, or null when the declaration
    /// has no region to give. A degenerate rectangle answers an EMPTY path: used as a clip
    /// that hides everything, which is what an inside-out rect means.</summary>
    public static SKPath? Parse(string? clip, LayoutBox box, ComputedStyle? style)
    {
        // The same four sides the grammar counted and the value text printed, so the region a box
        // is cut to cannot disagree with the rect a script reads back.
        var sides = CssValueGrammar.LegacyClipSides(clip);
        if (sides == null) return null;

        var border = box.BorderBox;
        float? top = Side(sides[0], style);
        float? right = Side(sides[1], style);
        float? bottom = Side(sides[2], style);
        float? left = Side(sides[3], style);
        if (sides[0] != "auto" && top == null) return null;
        if (sides[1] != "auto" && right == null) return null;
        if (sides[2] != "auto" && bottom == null) return null;
        if (sides[3] != "auto" && left == null) return null;

        float x0 = left.HasValue ? border.Left + left.Value : border.Left;
        float y0 = top.HasValue ? border.Top + top.Value : border.Top;
        float x1 = right.HasValue ? border.Left + right.Value : border.Right;
        float y1 = bottom.HasValue ? border.Top + bottom.Value : border.Bottom;

        var path = new SKPath();
        if (x1 > x0 && y1 > y0) path.AddRect(new SKRect(x0, y0, x1, y1));
        return path;
    }

    /// <summary>The offset one side names, priced in the element's own font for an 'em' and in
    /// the viewport for a 'vh'. Null means the number could not be priced at all.</summary>
    private static float? Side(string token, ComputedStyle? style)
    {
        if (token == "auto") return null;
        var length = Length.Parse(token);
        if (length is null or AutoLength or PercentLength or IntrinsicLength or FarEdgeLength) return null;
        float font = style != null && style.FontSize > 0 ? style.FontSize : 16f;
        float root = style?.RootFontSizeOrInitial ?? 16f;
        float vw = style?.ComputedViewportWidth ?? 0f;
        float vh = style?.ComputedViewportHeight ?? 0f;
        float px = length.ToPixels(font, root, vw, vh);
        return float.IsNaN(px) || float.IsInfinity(px) ? null : px;
    }
}
