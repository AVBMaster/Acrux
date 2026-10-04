using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Rendering;

public enum ImeTextSpanUnderlineStyle
{
    None = 0,
    Solid,
    Dash,
    Dot,
    Squiggle,
}

public enum TextMarkerThickness { Thin, Thick, None }

/// <summary>
/// A text marker for composition (input method) and suggestion underlines.
/// Minimal mirror of StyleableMarker.
/// </summary>
public sealed class StyleableMarker
{
    public TextMarkerThickness Thickness { get; set; } = TextMarkerThickness.Thin;
    public SKColor? UnderlineColor { get; set; }
    public bool UseTextColor { get; set; }
    public ImeTextSpanUnderlineStyle UnderlineStyle { get; set; } = ImeTextSpanUnderlineStyle.Solid;
    public bool IsComposition { get; set; }

    public bool HasThicknessNone => Thickness == TextMarkerThickness.None;
    public bool HasThicknessThick => Thickness == TextMarkerThickness.Thick;
}

/// <summary>
/// Paints underlines for composition (input method) and suggestion markers.
/// Mirrors StyleableMarkerPainter.
///
/// The Skia shader-based document-marker pattern (RecordMarker) is rendered as
/// discrete wavy dash ops via the local <see cref="RecordSquigglePath"/>; solid,
/// dash and dot styles use the existing stroke helpers. Results are appended to
/// <paramref name="sink"/> as DrawRectOp / path ops.
/// </summary>
public static class StyleableMarkerPainter
{
    private const float kMarkerWidth = 4;
    private const float kMarkerHeight = 2;

    public static bool ShouldPaintUnderline(StyleableMarker marker)
    {
        if (marker.HasThicknessNone)
            return false;
        if (marker.UnderlineColor == SKColors.Transparent && !marker.UseTextColor)
            return false;
        if (marker.UnderlineStyle == ImeTextSpanUnderlineStyle.None)
            return false;
        return true;
    }

    /// <summary>
    /// Paints an underline for the given marker. <paramref name="strokeSink"/>
    /// receives solid/dash/dot line rects and <paramref name="pathSink"/> receives
    /// squiggle paths (composition markers only).
    /// </summary>
    public static void PaintUnderline(
        StyleableMarker marker, List<DrawRectOp> strokeSink, List<DrawPathOp> pathSink,
        PhysicalOffset boxOrigin, ComputedStyle style, LineRelativeRect markerRect,
        float logicalHeight, float realZoom, bool inDarkMode,
        SKColor fillColorOverride)
    {
        // Start of the line to draw, relative to boxOrigin.X().
        float start = markerRect.LineLeft + 1;
        float width = markerRect.InlineSize - 2;
        if (width <= 0)
            return;

        // 2px (before zoom) for thick marked text underlines when there is room
        // under the baseline, otherwise 1px. Line thickness scales with zoom.
        float effectiveZoom = realZoom > 0 ? realZoom : style.Zoom;
        int lineThickness = (int)(1 * effectiveZoom);
        float baseline = style.FontSize; // ascent approximation (no SimpleFontData)
        if (marker.HasThicknessThick)
        {
            int thickLineThickness = (int)(2 * effectiveZoom);
            if (logicalHeight - baseline >= thickLineThickness)
                lineThickness = thickLineThickness;
        }

        SKColor markerColor;
        if (marker.UseTextColor || inDarkMode)
            markerColor = style.Color;
        else
            markerColor = marker.UnderlineColor ?? style.TextDecorationColor;
        if (fillColorOverride.Alpha > 0)
            markerColor = fillColorOverride;

        float lineY = boxOrigin.Top + (logicalHeight - lineThickness);

        if (marker.UnderlineStyle != ImeTextSpanUnderlineStyle.Squiggle)
        {
            AppendStrokeRect(strokeSink,
                new SKRect(boxOrigin.Left + start, lineY, boxOrigin.Left + start + width, lineY + lineThickness),
                markerColor, marker.UnderlineStyle);
        }
        else if (marker.IsComposition)
        {
            var path = RecordSquigglePath(markerColor, lineY + lineThickness / 2f, width, lineThickness);
            if (path != null)
                pathSink.Add(new DrawPathOp { Path = path });
        }
    }

    private static void AppendStrokeRect(List<DrawRectOp> sink, SKRect rect, SKColor color, ImeTextSpanUnderlineStyle style)
    {
        var op = new DrawRectOp { Rect = rect, FillColor = color };
        // Dash and dot styles are approximated by only filling the active
        // segments; the caller layers it over a matching background when desired.
        float w = rect.Width;
        float h = rect.Height;
        if (style == ImeTextSpanUnderlineStyle.Dash && w > 8)
        {
            int dashCount = (int)MathF.Floor(w / 6f);
            for (int i = 0; i < dashCount; i++)
            {
                float dStart = rect.Left + i * 6f;
                sink.Add(new DrawRectOp { Rect = new SKRect(dStart, rect.Top, MathF.Min(dStart + 3f, rect.Right), rect.Bottom), FillColor = color });
            }
            return;
        }
        if (style == ImeTextSpanUnderlineStyle.Dot && w > 6)
        {
            int dotCount = (int)MathF.Floor(w / 4f);
            for (int i = 0; i < dotCount; i++)
            {
                float dStart = rect.Left + i * 4f;
                float dotW = MathF.Min(2f, rect.Width - (dStart - rect.Left));
                if (dotW <= 0) break;
                sink.Add(new DrawRectOp { Rect = new SKRect(dStart, rect.Top, dStart + dotW, rect.Bottom), FillColor = color });
            }
            return;
        }
        sink.Add(op);
    }

    /// <summary>
    /// Records the squiggle path equivalent to the legacy document-marker
    /// pattern (three cubic beziers), scaled to fit <paramref name="width"/>.
    /// </summary>
    private static SKPath? RecordSquigglePath(SKColor color, float centerY, float width, float zoom)
    {
        if (width <= 0)
            return null;
        int patternWidth = (int)(kMarkerWidth * zoom);
        int repeats = Math.Max(1, (int)MathF.Ceiling(width / Math.Max(1, patternWidth)));
        float scale = kMarkerWidth < 1 ? 1 : 1;

        var path = new SKPath();
        for (int i = 0; i < repeats; i++)
        {
            float baseX = i * kMarkerWidth * zoom;
            // Pattern:  X o   o X
            path.MoveTo(baseX + kMarkerWidth * zoom * -3 / 8f, centerY + kMarkerHeight * zoom * 3 / 4f);
            path.CubicTo(
                baseX + kMarkerWidth * zoom * -1 / 8f, centerY + kMarkerHeight * zoom * 3 / 4f,
                baseX + kMarkerWidth * zoom * -1 / 8f, centerY + kMarkerHeight * zoom * 1 / 4f,
                baseX + kMarkerWidth * zoom * 1 / 8f, centerY + kMarkerHeight * zoom * 1 / 4f);
            path.CubicTo(
                baseX + kMarkerWidth * zoom * 3 / 8f, centerY + kMarkerHeight * zoom * 1 / 4f,
                baseX + kMarkerWidth * zoom * 3 / 8f, centerY + kMarkerHeight * zoom * 3 / 4f,
                baseX + kMarkerWidth * zoom * 5 / 8f, centerY + kMarkerHeight * zoom * 3 / 4f);
            path.CubicTo(
                baseX + kMarkerWidth * zoom * 7 / 8f, centerY + kMarkerHeight * zoom * 3 / 4f,
                baseX + kMarkerWidth * zoom * 7 / 8f, centerY + kMarkerHeight * zoom * 1 / 4f,
                baseX + kMarkerWidth * zoom * 9 / 8f, centerY + kMarkerHeight * zoom * 1 / 4f);
        }
        return path;
    }
}