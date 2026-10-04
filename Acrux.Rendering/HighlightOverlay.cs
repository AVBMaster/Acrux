using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Geometry and color helpers for the highlight overlay paint pass. Mirrors the
/// highlight_overlay.cc concept: after normal painting, a translucent overlay is
/// repainted on top so fragments of the same selected line get a continuous
/// highlight instead of gap-ridden per-glyph boxes. This port intentionally
/// factors out only the geometry (merging/adjoining fragment rects) and the
/// selection tint resolution (invert fg/bg or an explicit ::selection override),
/// plus marker underline/overline decoration geometry. It does not reproduce the
/// full repaint-list machinery of the reference.
/// </summary>
public static class HighlightOverlay
{
    /// <summary>
    /// Horizontal adjoin tolerance in device pixels. Two rects on the same line
    /// whose gap is no larger than this are merged into one continuous overlay
    /// rect so the highlight looks seamless.
    /// </summary>
    public const float AdjoinTolerance = 1.5f;

    /// <summary>
    /// Vertical tolerance used to decide whether two rects sit on the same line
    /// (their vertical extents overlap / are within this many pixels).
    /// </summary>
    public const float SameLineTolerance = 1.5f;

    /// <summary>
    /// Computes a minimal set of overlay rects from a set of fragment rects that
    /// are part of one highlighted range. Fragments on the same line that are
    /// adjacent (within <see cref="AdjoinTolerance"/>) are merged; fragments on
    /// consecutive lines are emitted as separate rects (the reference keeps them
    /// distinct because each line may have its own leading/trailing inset).
    /// </summary>
    public static List<SKRect> ComputeOverlayRects(IEnumerable<SKRect> fragments)
    {
        var result = new List<SKRect>();
        var sorted = fragments.Where(r => r.Width > 0 && r.Height > 0).ToList();
        if (sorted.Count == 0)
            return result;

        sorted.Sort((a, b) =>
        {
            int byTop = a.Top.CompareTo(b.Top);
            if (byTop != 0) return byTop;
            return a.Left.CompareTo(b.Left);
        });

        var current = sorted[0];
        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            bool sameLine = RectsOnSameLine(current, next);
            bool adjoins = sameLine && next.Left <= current.Right + AdjoinTolerance;
            if (sameLine && adjoins)
            {
                current = SKRect.Union(current, next);
            }
            else
            {
                result.Add(current);
                current = next;
            }
        }
        result.Add(current);
        return result;
    }

    /// <summary>
    /// Returns true when two fragment rects belong to the same text line: their
    /// vertical spans overlap by more than <paramref name="tolerance"/> pixels and
    /// their tops are within the same line band.
    /// </summary>
    private static bool RectsOnSameLine(SKRect a, SKRect b, float tolerance = SameLineTolerance)
    {
        float overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return overlap > -tolerance && Math.Abs(a.Top - b.Top) <= Math.Max(a.Height, b.Height) + tolerance;
    }

    /// <summary>
    /// Resolves the tint used to paint a selection highlight for a given style.
    /// When an explicit selection background override is supplied (the
    /// ::selection background-color), it is returned with normalized alpha (the
    /// reference applies the selection overlay with a partial alpha). Otherwise
    /// the classic invert behavior is used: the background is derived by inverting
    /// the foreground color, which gives reliable contrast for an unknown text
    /// color.
    /// </summary>
    public static SKColor ResolveSelectionTint(ComputedStyle style, SKColor? selectionBackgroundOverride = null)
    {
        if (selectionBackgroundOverride.HasValue)
        {
            var c = selectionBackgroundOverride.Value;
            return new SKColor(c.Red, c.Green, c.Blue, (byte)(c.Alpha == 255 ? 0x55 : c.Alpha));
        }

        var foreground = Normalize(style.Color);
        bool hasBackground = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        if (hasBackground)
        {
            // Invert the background color so the highlighted text is readable
            // against the page regardless of the surrounding background.
            var bg = Normalize(style.BackgroundColor!.Value);
            return new SKColor(
                (byte)(255 - bg.Red),
                (byte)(255 - bg.Green),
                (byte)(255 - bg.Blue),
                0x40);
        }

        // No background: invert the foreground (dark text -> light highlight).
        return new SKColor(
            (byte)(255 - foreground.Red),
            (byte)(255 - foreground.Green),
            (byte)(255 - foreground.Blue),
            0x40);
    }

    /// <summary>
    /// Resolves the text color used to draw the highlighted glyphs. When an
    /// explicit ::selection color override is provided it wins; otherwise the
    /// foreground is inverted from the tint so the text stays visible on the
    /// translucent highlight.
    /// </summary>
    public static SKColor ResolveSelectionTextColor(SKColor tint, SKColor? selectionColorOverride = null)
    {
        if (selectionColorOverride.HasValue)
            return Normalize(selectionColorOverride.Value);

        return new SKColor(
            (byte)(255 - tint.Red),
            (byte)(255 - tint.Green),
            (byte)(255 - tint.Blue),
            255);
    }

    /// <summary>
    /// Computes the decoration geometry for a spellcheck/marker highlight run.
    /// Returns the underline/overline rectangles that should be drawn over the
    /// given run bounds, honoring the resolved color, orientation
    /// (<paramref name="under"/> vs overline) and offset/thickness provided by
    /// the decoration. When no usable thickness is available a sensible default
    /// derived from the run height is used.
    /// </summary>
    public static List<SKRect> ComputeMarkerDecorationRects(
        SKRect runBounds,
        float thickness,
        float offset,
        float fontSize,
        bool underline = true)
    {
        var result = new List<SKRect>();
        if (runBounds.Width <= 0 || runBounds.Height <= 0)
            return result;

        float resolvedThickness = thickness > 0 ? thickness : Math.Max(1f, fontSize / 12f);
        float resolvedOffset = offset;
        if (resolvedOffset == 0)
            resolvedOffset = underline ? 1.5f : -resolvedThickness * 2f;

        if (underline)
        {
            float y = runBounds.Bottom + resolvedOffset;
            result.Add(new SKRect(runBounds.Left, y, runBounds.Right, y + resolvedThickness));
        }
        else
        {
            float y = runBounds.Top - resolvedOffset - resolvedThickness;
            result.Add(new SKRect(runBounds.Left, y, runBounds.Right, y + resolvedThickness));
        }
        return result;
    }

    private static SKColor Normalize(SKColor c) =>
        new(c.Red, c.Green, c.Blue, c.Alpha);
}
