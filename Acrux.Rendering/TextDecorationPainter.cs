using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Implementation of the decoration-line painter and the geometry portions of
/// the text-decoration info / offset helpers. Computes text-decoration line
/// geometry (thickness, underline/overline/line-through offsets, double
/// offsets, wavy bezier pattern) and paints the decoration lines into a canvas.
/// </summary>
public static class TextDecorationPainter
{
    /// <summary>
    /// Authored per-decoration-line geometry: the parts of
    /// 'text-decoration-thickness', 'text-underline-offset',
    /// 'text-underline-position' and 'text-decoration-skip-ink' that change
    /// where a stripe is drawn, plus the font-supplied values those keywords
    /// fall back to. <see cref="Auto"/> keeps the historical behaviour
    /// (font-size / 10 thickness, font-derived underline offset).
    /// </summary>
    public struct DecorationGeometry
    {
        /// <summary>'text-decoration-thickness' in px; NaN means 'auto'.</summary>
        public float Thickness;

        /// <summary>True for 'text-decoration-thickness: from-font'.</summary>
        public bool ThicknessFromFont;

        /// <summary>'text-underline-offset' in px; NaN means 'auto'.</summary>
        public float UnderlineOffset;

        public TextUnderlinePositionType UnderlinePosition;

        /// <summary>False for 'text-decoration-skip-ink: none'.</summary>
        public bool SkipInk;

        /// <summary>The used font's underline thickness, for 'from-font'.</summary>
        public float FontUnderlineThickness;

        /// <summary>The used font's underline position: distance from the
        /// baseline down to the stripe, always positive.</summary>
        public float FontUnderlinePosition;

        /// <summary>The used font's descent, for 'text-underline-position: under'.</summary>
        public float Descent;

        public static DecorationGeometry Auto => new()
        {
            Thickness = float.NaN,
            UnderlineOffset = float.NaN,
            UnderlinePosition = TextUnderlinePositionType.Auto,
            SkipInk = true,
        };
    }

    /// <summary>
    /// Corresponds to ComputeDecorationThickness(): auto thickness is
    /// font_size / 10, floored at the minimum thickness (1 CSS px).
    /// </summary>
    public static float ComputeDecorationThickness(float computedFontSize, float minimumThickness = 1f)
    {
        float autoThickness = MathF.Max(minimumThickness, computedFontSize / 10f);
        return autoThickness;
    }

    /// <summary>
    /// Resolves the stripe thickness. 'auto' is font_size / 10, 'from-font' takes
    /// the font's own value, an authored length or percentage (already resolved
    /// against the font size by the cascade) is rounded to a whole pixel: a
    /// sub-pixel stripe would otherwise be snapped back up by the painter and
    /// read as a different thickness. Every branch keeps the minimum thickness.
    /// </summary>
    public static float ResolveThickness(float fontSize, in DecorationGeometry? geometry)
    {
        const float minimumThickness = 1f;
        float autoThickness = ComputeDecorationThickness(fontSize, minimumThickness);
        if (geometry is { } g)
        {
            if (g.ThicknessFromFont && g.FontUnderlineThickness > 0)
                return MathF.Max(minimumThickness, g.FontUnderlineThickness);
            if (!float.IsNaN(g.Thickness))
                return MathF.Max(minimumThickness, MathF.Round(g.Thickness));
        }
        return autoThickness;
    }

    /// <summary>
    /// Distance from the baseline down to the top of the underline stripe
    /// (ComputeUnderlineOffsetAuto / …FromFont / …ForUnder). 'auto' keeps a small
    /// gap that grows with the thickness, 'from-font' uses the font's own
    /// underline position and 'under' pushes the line below the descenders;
    /// 'alphabetic' is the horizontal-tb default. A fixed
    /// 'text-underline-offset' replaces the auto gap — the line then starts on
    /// the baseline and moves by the authored amount.
    /// </summary>
    public static float ResolveUnderlineOffset(float thickness, in DecorationGeometry? geometry)
    {
        float autoGap = Math.Max(1, (int)MathF.Ceiling(thickness / 2f));
        if (geometry is not { } g)
            return autoGap;

        bool offsetIsFixed = !float.IsNaN(g.UnderlineOffset);
        float authored = offsetIsFixed ? MathF.Round(g.UnderlineOffset) : 0f;
        float gap = g.UnderlinePosition switch
        {
            TextUnderlinePositionType.Under => MathF.Max(0f, g.Descent) + 1f,
            TextUnderlinePositionType.FromFont => g.FontUnderlinePosition > 0 ? g.FontUnderlinePosition : autoGap,
            _ => offsetIsFixed ? 0f : autoGap,
        };
        return gap + authored;
    }

    /// <summary>WavyControlPointDistance(): distance from the wave axis to the bezier control points.</summary>
    public static float WavyControlPointDistance(float resolvedThickness)
    {
        return 0.5f + MathF.Round(3 * MathF.Max(1f, resolvedThickness) + 0.5f);
    }

    /// <summary>WavyStep(): horizontal step between consecutive wave crests.</summary>
    public static float WavyStep(float resolvedThickness)
    {
        return 0.5f + MathF.Round(2 * MathF.Max(1f, resolvedThickness) + 0.5f);
    }

    /// <summary>
    /// PrepareWavyStrokePath(): three consecutive cubic beziers forming a wavy
    /// pattern whose midpoints sit at y = 0.5 (to reduce vertical aliasing).
    /// The pattern starts at phase_shift (negative) so it can be clipped to the
    /// line length on both ends.
    /// </summary>
    public static SKPath PrepareWavyStrokePath(float resolvedThickness)
    {
        float controlPointDistance = WavyControlPointDistance(resolvedThickness);
        float step = WavyStep(resolvedThickness);
        float phaseShift = -2f * step;

        var path = new SKPath();
        float startX = phaseShift;
        const float midY = 0.5f;
        path.MoveTo(startX, midY);

        float endX = startX + 2f * step;
        var cp1 = new SKPoint(startX + step, midY + controlPointDistance);
        var cp2 = new SKPoint(startX + step, midY - controlPointDistance);
        path.CubicTo(cp1, cp2, new SKPoint(endX, midY));

        cp1.X += 2f * step;
        cp2.X += 2f * step;
        endX += 2f * step;
        path.CubicTo(cp1, cp2, new SKPoint(endX, midY));

        cp1.X += 2f * step;
        cp2.X += 2f * step;
        endX += 2f * step;
        path.CubicTo(cp1, cp2, new SKPoint(endX, midY));

        return path;
    }

    /// <summary>
    /// Paints all text-decoration lines in one call (under/over first, then
    /// line-through), without text-shadow phases. Kept for compatibility;
    /// <see cref="PaintUnderOrOverLines"/> and <see cref="PaintLineThrough"/>
    /// are the phase-aware entry points used by DrawTextOp.
    /// </summary>
    public static void PaintDecorationLines(
        SKCanvas canvas, float startX, float width, float baselineY, float ascent, float fontSize,
        bool underline, bool overline, bool lineThrough,
        TextDecorationStyleType style, SKColor color, SKColor underlineColor)
    {
        if (width <= 0)
            return;

        float thickness = ComputeDecorationThickness(fontSize);
        PaintUnderOrOverLines(canvas, startX, width, baselineY, ascent, fontSize,
            underline, overline, style, color, underlineColor, null);
        PaintLineThrough(canvas, startX, width, baselineY, ascent, fontSize,
            lineThrough, style, color, null);
    }

    /// <summary>
    /// Paints underline/overline decorations, mirroring the engine's
    /// paint-under/over-line-decorations path: a shadow pass
    /// first (each text shadow rendered as the decoration in the shadow color,
    /// offset and blurred), then the line in its actual color. These lines are
    /// painted before the text so glyphs sit on top of them.
    /// </summary>
    public static void PaintUnderOrOverLines(
        SKCanvas canvas, float startX, float width, float baselineY, float ascent, float fontSize,
        bool underline, bool overline,
        TextDecorationStyleType style, SKColor color, SKColor underlineColor,
        List<TextShadowValue>? shadows,
        Func<float, float, List<SKRect>?>? skipInkProvider = null,
        DecorationGeometry? geometry = null)
    {
        if (width <= 0)
            return;

        float thickness = ResolveThickness(fontSize, geometry);
        var lineColor = underlineColor.Alpha > 0 ? underlineColor : color;

        // text-decoration-skip-ink: auto — punch holes in the decoration line
        // where the glyph ink crosses it. Mirrors the engine's
        // PaintDecorationLine / ClipDecorationsStripe: the stripe
        // band is the decoration bounds inset by 0.5 (to ignore intersects
        // smaller than half a pixel); the provider returns the clip rects for a
        // given band (upper = band top relative to the baseline, stripe = band
        // height) in canvas coordinates. 'skip-ink: none' keeps the line whole.
        bool skipInk = geometry is not { SkipInk: false };
        List<SKRect>? underlineClips = null;
        List<SKRect>? overlineClips = null;
        float underlineGap = ResolveUnderlineOffset(thickness, geometry);
        if (skipInk && skipInkProvider != null)
        {
            if (underline)
            {
                float lineY = baselineY + underlineGap;
                underlineClips = skipInkProvider(lineY - baselineY + 0.5f, StripeHeight(style, thickness));
            }
            if (overline)
            {
                float lineY = baselineY - ascent - thickness;
                overlineClips = skipInkProvider(lineY - baselineY + 0.5f, StripeHeight(style, thickness));
            }
        }

        PaintWithShadowPhases(canvas, shadows, lineColor, (dx, dy, shadowColor) =>
        {
            // Underline: the offset from the baseline comes from the font, from
            // 'text-underline-position' or from both (see ResolveUnderlineOffset).
            if (underline)
            {
                float lineY = baselineY + underlineGap;
                DrawLineWithSkipInk(canvas, underlineClips, dx, dy, () =>
                {
                    PaintSingleLine(canvas, startX + dx, width, lineY + dy, thickness, style, shadowColor);
                    if (style == TextDecorationStyleType.Double)
                        PaintSingleLine(canvas, startX + dx, width, lineY + thickness + 1f + dy, thickness, TextDecorationStyleType.Solid, shadowColor);
                });
            }

            // Overline: sits just above the ascent line (TextTop position).
            if (overline)
            {
                float lineY = baselineY - ascent - thickness;
                DrawLineWithSkipInk(canvas, overlineClips, dx, dy, () =>
                {
                    PaintSingleLine(canvas, startX + dx, width, lineY + dy, thickness, style, shadowColor);
                    if (style == TextDecorationStyleType.Double)
                        PaintSingleLine(canvas, startX + dx, width, lineY - (thickness + 1f) + dy, thickness, TextDecorationStyleType.Solid, shadowColor);
                });
            }
        });
    }

    /// <summary>
    /// Height of the decoration stripe used for ink skipping, after the 0.5px
    /// inset on each side. Mirrors the engine's decoration-info Bounds() for
    /// each decoration style (double spans both stripes: DoubleOffset +
    /// thickness), with the height reduced by the inset.
    /// </summary>
    private static float StripeHeight(TextDecorationStyleType style, float thickness)
    {
        if (style == TextDecorationStyleType.Double)
            return 2 * thickness + 1 - 1f;
        return thickness - 1f;
    }

    /// <summary>
    /// Draws <paramref name="draw"/> with the skip-ink clip rects punched out
    /// (SKClipOperation.Difference), then restores. The rects are shifted by
    /// <paramref name="dx"/>/<paramref name="dy"/> so the holes follow the line
    /// for each text-shadow phase (mirroring the engine, where the clip is
    /// re-applied per phase). When there are no clips the line is drawn as-is.
    /// </summary>
    private static void DrawLineWithSkipInk(SKCanvas canvas, List<SKRect>? clips, float dx, float dy, Action draw)
    {
        if (clips == null || clips.Count == 0)
        {
            draw();
            return;
        }

        canvas.Save();
        foreach (var clip in clips)
            canvas.ClipRect(new SKRect(clip.Left + dx, clip.Top + dy, clip.Right + dx, clip.Bottom + dy), SKClipOperation.Difference);
        draw();
        canvas.Restore();
    }

    /// <summary>
    /// Paints line-through decorations, mirroring the engine's
    /// paint-line-through path: shadow pass first,
    /// then the line in its actual color. Painted after the text so it sits on
    /// top of the glyphs. No skip: ink for line-through.
    /// </summary>
    public static void PaintLineThrough(
        SKCanvas canvas, float startX, float width, float baselineY, float ascent, float fontSize,
        bool lineThrough,
        TextDecorationStyleType style, SKColor color, List<TextShadowValue>? shadows,
        DecorationGeometry? geometry = null)
    {
        if (width <= 0)
            return;

        float thickness = ResolveThickness(fontSize, geometry);

        PaintWithShadowPhases(canvas, shadows, color, (dx, dy, shadowColor) =>
        {
            if (!lineThrough)
                return;
            // Line-through: centered at 2/3 of the ascent (SetLineThroughLineData).
            float lineY = baselineY - ascent / 3f - thickness / 2f;
            PaintSingleLine(canvas, startX + dx, width, lineY + dy, thickness, style, shadowColor);
            if (style == TextDecorationStyleType.Double)
                PaintSingleLine(canvas, startX + dx, width, lineY + MathF.Floor(thickness + 1f) + dy, thickness, TextDecorationStyleType.Solid, shadowColor);
        });
    }

    /// <summary>
    /// Renders the shadow pass (one draw per text shadow, offset + blur in the
    /// shadow color) followed by the fill pass in <paramref name="fillColor"/>.
    /// Mirrors TextPainter::PaintWithTextShadow / text_shadow_painter.cc.
    /// </summary>
    private static void PaintWithShadowPhases(SKCanvas canvas, List<TextShadowValue>? shadows, SKColor fillColor,
        Action<float, float, SKColor> draw)
    {
        if (shadows != null)
        {
            foreach (var shadow in shadows)
            {
                if (shadow.BlurRadius > 0)
                {
                    using var layerPaint = new SKPaint
                    {
                        ImageFilter = SKImageFilter.CreateBlur(shadow.BlurRadius, shadow.BlurRadius)
                    };
                    canvas.SaveLayer(layerPaint);
                    draw(shadow.OffsetX, shadow.OffsetY, shadow.Color);
                    canvas.Restore();
                }
                else
                {
                    draw(shadow.OffsetX, shadow.OffsetY, shadow.Color);
                }
            }
        }
        draw(0, 0, fillColor);
    }

    private static void PaintSingleLine(SKCanvas canvas, float startX, float width, float lineY, float thickness,
        TextDecorationStyleType style, SKColor color)
    {
        switch (style)
        {
            case TextDecorationStyleType.Wavy:
                PaintWavy(canvas, startX, width, lineY, thickness, color);
                break;
            case TextDecorationStyleType.Dotted:
            case TextDecorationStyleType.Dashed:
                PaintDottedOrDashed(canvas, startX, width, lineY, thickness, color, style);
                break;
            default: // Solid / Double (the second stripe is painted by the caller)
            {
                // SnapYAxis(): round to the nearest pixel and never below 1px thick.
                float snappedY = MathF.Floor(lineY + 0.5f);
                float snappedHeight = MathF.Max(MathF.Floor(thickness), 1f);
                using var paint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = false
                };
                canvas.DrawRect(new SKRect(startX, snappedY, startX + width, snappedY + snappedHeight), paint);
                break;
            }
        }
    }

    private static void PaintDottedOrDashed(SKCanvas canvas, float startX, float width, float lineY, float thickness,
        SKColor color, TextDecorationStyleType style)
    {
        // GetSnappedPointsForTextLine(): mid-point snapped to a device pixel.
        int midY = (int)MathF.Floor(lineY + MathF.Max(thickness / 2f, 0.5f));
        float strokeWidth = MathF.Max(MathF.Floor(thickness), 1f);
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            IsAntialias = true,
            StrokeCap = style == TextDecorationStyleType.Dotted ? SKStrokeCap.Round : SKStrokeCap.Butt
        };
        float dash = MathF.Max(1f, thickness);
        float gap = style == TextDecorationStyleType.Dotted ? dash * 2f : dash * 2f;
        paint.PathEffect = SKPathEffect.CreateDash(new[] { dash, gap }, 0);
        canvas.DrawLine(startX, midY, startX + width, midY, paint);
    }

    private static void PaintWavy(SKCanvas canvas, float startX, float width, float lineY, float thickness, SKColor color)
    {
        float step = WavyStep(thickness);
        float controlPointDistance = WavyControlPointDistance(thickness);

        // The pattern is authored around the axis y = 0.5. Chrome centres the wave one
        // thickness below the decoration line's top edge (measured at 24px Arial for
        // thickness auto/2/4/8), and the clip has to be expressed in the shifted space
        // as well; clipping around |lineY| after the translate hides the wave.
        float halfSpan = controlPointDistance + thickness;
        float ty = lineY - 0.5f + thickness;

        using var pattern = PrepareWavyStrokePath(thickness);

        // Tile the three-bezier pattern across the line, starting one wave before
        // startX so clipping produces identical phase at both ends.
        float left = startX - 2f * step;
        float right = startX + width + 2f * step;
        using var tiled = new SKPath();
        for (float x = left; x < right; x += 2f * step)
        {
            using var copy = new SKPath(pattern);
            copy.Transform(SKMatrix.CreateTranslation(x + 2f * step, 0));
            tiled.AddPath(copy);
        }

        canvas.Save();
        canvas.Translate(0, ty);
        canvas.ClipRect(new SKRect(startX, 0.5f - halfSpan, startX + width, 0.5f + halfSpan));
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            IsAntialias = true
        };
        canvas.DrawPath(tiled, paint);
        canvas.Restore();
    }
}
