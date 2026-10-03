using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

public abstract class PaintOp
{
    public SKRect Bounds { get; set; }
    public int ZIndex { get; set; }
    /// <summary>
    /// True only for pure draw ops with accurate-enough Bounds to skip when they
    /// miss the target rect. State-stack ops (clip/layer/transform pushes & pops)
    /// stay non-cullable so culling can never unbalance the canvas state.
    /// </summary>
    public virtual bool Cullable => false;
    public abstract void Execute(SKCanvas canvas);
    public virtual void Reset() { Bounds = default; ZIndex = 0; }

    protected static (float scaleX, float scaleY) GetCanvasScale(SKCanvas canvas)
    {
        try
        {
            var m = canvas.TotalMatrix;
            // Device units per user unit along each axis = the length of the
            // transformed axis vector. The matrix diagonal alone is meaningless
            // under rotation: rotate(90deg) has ScaleX = cos 90 = ~0, which made
            // SnapToDevice collapse every rectangle to a point (invisible element).
            float sx = MathF.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY);
            float sy = MathF.Sqrt(m.SkewX * m.SkewX + m.ScaleY * m.ScaleY);
            if (float.IsNaN(sx) || sx < 0.0001f) sx = 1f;
            if (float.IsNaN(sy) || sy < 0.0001f) sy = 1f;
            return (sx, sy);
        }
        catch
        {
            return (1f, 1f);
        }
    }

    protected static float SnapToDevice(float v, float scale)
    {
        if (scale <= 0) return v;
        return MathF.Round(v * scale) / scale;
    }

    protected static SKRect AlignRectToDevice(SKRect r, SKCanvas canvas)
    {
        var m = canvas.TotalMatrix;
        // Snapping a user-space rectangle to the device grid only makes sense while
        // the CTM is axis-aligned. Under rotation or skew the corners do not map to
        // the rectangle's own edges, so rounding would move geometry instead of
        // aligning it - leave such rects untouched.
        if (MathF.Abs(m.SkewX) + MathF.Abs(m.SkewY) > 0.001f * (MathF.Abs(m.ScaleX) + MathF.Abs(m.ScaleY) + 1f))
            return r;

        var (sx, sy) = GetCanvasScale(canvas);
        var left = SnapToDevice(r.Left, sx);
        var top = SnapToDevice(r.Top, sy);
        var right = SnapToDevice(r.Right, sx);
        var bottom = SnapToDevice(r.Bottom, sy);
        // Ensure non-negative width/height
        if (right < left) right = left;
        if (bottom < top) bottom = top;
        return new SKRect(left, top, right, bottom);
    }

    public void AlignBounds(SKCanvas canvas)
    {
        Bounds = AlignRectToDevice(Bounds, canvas);
    }
}

public class DrawRectOp : PaintOp
{
    public override bool Cullable => true;
    public SKRect Rect { get; set; }
    public SKColor FillColor { get; set; }
    public float BorderTopWidth { get; set; }
    public float BorderRightWidth { get; set; }
    public float BorderBottomWidth { get; set; }
    public float BorderLeftWidth { get; set; }
    public SKColor BorderTopColor { get; set; }
    public SKColor BorderRightColor { get; set; }
    public SKColor BorderBottomColor { get; set; }
    public SKColor BorderLeftColor { get; set; }
    public BorderStyle BorderTopStyle { get; set; }
    public BorderStyle BorderRightStyle { get; set; }
    public BorderStyle BorderBottomStyle { get; set; }
    public BorderStyle BorderLeftStyle { get; set; }
    public float BorderRadius { get; set; }
    /// <summary>
    /// Optional per-corner elliptical radii [TL, TR, BR, BL] as (rx, ry) pairs.
    /// When set it overrides the scalar <see cref="BorderRadius"/> for the fill.
    /// </summary>
    public SKPoint[]? CornerRadii { get; set; }

    public override void Reset()
    {
        base.Reset();
        Rect = default;
        FillColor = default;
        BorderTopWidth = BorderRightWidth = BorderBottomWidth = BorderLeftWidth = 0;
        BorderTopColor = BorderRightColor = BorderBottomColor = BorderLeftColor = default;
        BorderTopStyle = BorderRightStyle = BorderBottomStyle = BorderLeftStyle = BorderStyle.None;
        BorderRadius = 0;
        CornerRadii = null;
    }

    public override void Execute(SKCanvas canvas)
    {
        bool hasBorder = BorderTopWidth > 0 || BorderRightWidth > 0 ||
                         BorderBottomWidth > 0 || BorderLeftWidth > 0;
        bool hasFill = FillColor.Alpha > 0;

        if ((BorderRadius > 0 || CornerRadii != null) && (hasFill || hasBorder))
        {
            ExecuteWithRoundRect(canvas, hasFill, hasBorder);
        }
        else
        {
            ExecuteWithFlatRect(canvas, hasFill, hasBorder);
        }
    }

    private void ExecuteWithRoundRect(SKCanvas canvas, bool hasFill, bool hasBorder)
    {
        var aligned = AlignRectToDevice(Rect, canvas);
        // Per-corner elliptical radii (from border-radius, including percentages
        // resolved against the box) drive the fill; the scalar radius keeps the
        // legacy uniform path for borders.
        bool useCorners = hasFill && CornerRadii != null;
        using var borderPath = new SKPath();
        if (useCorners)
        {
            var rr = new SKRoundRect();
            rr.SetRectRadii(aligned, CornerRadii!);
            borderPath.AddRoundRect(rr);
        }
        else
            borderPath.AddRoundRect(aligned, BorderRadius, BorderRadius);

        if (hasBorder)
        {
            var borderWidth = Math.Max(BorderTopWidth, Math.Max(BorderBottomWidth,
                Math.Max(BorderLeftWidth, BorderRightWidth)));

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = borderWidth,
                IsAntialias = true
            };

            float inset = borderWidth / 2;
            var innerRect = new SKRect(
                Rect.Left + inset,
                Rect.Top + inset,
                Rect.Right - inset,
                Rect.Bottom - inset);
            var pb2= new SKPathBuilder();
            pb2.AddRoundRect(innerRect, Math.Max(0, BorderRadius - inset), Math.Max(0, BorderRadius - inset));
            using var strokePath = pb2.Detach();
            borderPaint.Color = BorderTopColor;
            canvas.DrawPath(strokePath, borderPaint);
        }

        if (hasFill)
        {
            using var fillPaint = new SKPaint
            {
                Color = FillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawPath(borderPath, fillPaint);
        }
    }

    private void ExecuteWithFlatRect(SKCanvas canvas, bool hasFill, bool hasBorder)
    {
        var alignedRect = AlignRectToDevice(Rect, canvas);
        if (hasFill)
        {
            using var paint = new SKPaint
            {
                Color = FillColor,
                Style = SKPaintStyle.Fill,
                // The rect is snapped to device pixels; antialiasing an exact
                // edge-boundary fill feathers it and lets the underlying layer
                // bleed through as a hairline seam between adjacent boxes.
                IsAntialias = false
            };
            canvas.DrawRect(alignedRect, paint);
        }

        if (hasBorder)
        {
            if (BorderTopWidth > 0)
                DrawBorderSide(canvas, alignedRect.Left, alignedRect.Top, alignedRect.Right, alignedRect.Top, BorderTopWidth, BorderTopColor, BorderTopStyle);
            if (BorderBottomWidth > 0)
                DrawBorderSide(canvas, alignedRect.Left, alignedRect.Bottom, alignedRect.Right, alignedRect.Bottom, BorderBottomWidth, BorderBottomColor, BorderBottomStyle);
            if (BorderLeftWidth > 0)
                DrawBorderSide(canvas, alignedRect.Left, alignedRect.Top, alignedRect.Left, alignedRect.Bottom, BorderLeftWidth, BorderLeftColor, BorderLeftStyle);
            if (BorderRightWidth > 0)
                DrawBorderSide(canvas, alignedRect.Right, alignedRect.Top, alignedRect.Right, alignedRect.Bottom, BorderRightWidth, BorderRightColor, BorderRightStyle);
        }
    }

    private static void DrawBorderSide(SKCanvas canvas, float x1, float y1, float x2, float y2, float width, SKColor color, BorderStyle style)
    {
        if (style == BorderStyle.Groove || style == BorderStyle.Ridge ||
            style == BorderStyle.Inset || style == BorderStyle.Outset)
        {
            DrawSpecialBorderSide(canvas, x1, y1, x2, y2, width, color, style);
            return;
        }

        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            IsAntialias = true
        };
        if (style == BorderStyle.Dashed)
            paint.PathEffect = SKPathEffect.CreateDash(new[] { width * 4, width * 4 }, 0);
        else if (style == BorderStyle.Dotted)
            paint.PathEffect = SKPathEffect.CreateDash(new[] { width, width }, 0);
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void DrawSpecialBorderSide(SKCanvas canvas, float x1, float y1, float x2, float y2, float width, SKColor color, BorderStyle style)
    {
        bool isHorizontal = MathF.Abs(y1 - y2) < 0.5f;
        SKColor light = AdjustBrightness(color, 1.4f);
        SKColor dark = AdjustBrightness(color, 0.6f);
        SKColor half = AdjustBrightness(color, 0.8f);

        float halfW = width / 2f;
        if (isHorizontal)
        {
            float left = MathF.Min(x1, x2);
            float right = MathF.Max(x1, x2);
            SKColor topColor, bottomColor;
            switch (style)
            {
                case BorderStyle.Groove:
                    topColor = dark; bottomColor = light; break;
                case BorderStyle.Ridge:
                    topColor = light; bottomColor = dark; break;
                case BorderStyle.Inset:
                    topColor = dark; bottomColor = light; break;
                default: // Outset
                    topColor = light; bottomColor = dark; break;
            }
            using var p1 = new SKPaint { Color = topColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(left, y1 - halfW, right - left, halfW, p1);
            using var p2 = new SKPaint { Color = bottomColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(left, y1, right - left, halfW, p2);
        }
        else
        {
            float top = MathF.Min(y1, y2);
            float bottom = MathF.Max(y1, y2);
            SKColor leftColor, rightColor;
            switch (style)
            {
                case BorderStyle.Groove:
                    leftColor = dark; rightColor = light; break;
                case BorderStyle.Ridge:
                    leftColor = light; rightColor = dark; break;
                case BorderStyle.Inset:
                    leftColor = dark; rightColor = light; break;
                default: // Outset
                    leftColor = light; rightColor = dark; break;
            }
            using var p1 = new SKPaint { Color = leftColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(x1 - halfW, top, halfW, bottom - top, p1);
            using var p2 = new SKPaint { Color = rightColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(x1, top, halfW, bottom - top, p2);
        }
    }

    private static SKColor AdjustBrightness(SKColor color, float factor)
    {
        byte r = (byte)MathF.Min(255, color.Red * factor);
        byte g = (byte)MathF.Min(255, color.Green * factor);
        byte b = (byte)MathF.Min(255, color.Blue * factor);
        return new SKColor(r, g, b, color.Alpha);
    }
}

public class DrawTextOp : PaintOp
{
    public override bool Cullable => true;
    public string Text { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public SKColor Color { get; set; }
    public float FontSize { get; set; } = 16;
    public string FontFamily { get; set; } = "Arial";
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
    public TextAlignType TextAlign { get; set; } = TextAlignType.Start;
    public float? MaxWidth { get; set; }
    public bool Underline { get; set; }
    public bool LineThrough { get; set; }
    public bool Overline { get; set; }
    public SKColor UnderlineColor { get; set; }
    public TextDecorationStyleType DecorationStyle { get; set; } = TextDecorationStyleType.Solid;
    /// <summary>Authored 'text-decoration-thickness' of this run's own box; NaN is 'auto'.</summary>
    public float DecorationThickness { get; set; } = float.NaN;
    public bool DecorationThicknessFromFont { get; set; }
    /// <summary>Authored 'text-underline-offset'; NaN is 'auto'.</summary>
    public float DecorationUnderlineOffset { get; set; } = float.NaN;
    public TextUnderlinePositionType DecorationUnderlinePosition { get; set; } = TextUnderlinePositionType.Auto;
    public bool DecorationSkipInk { get; set; } = true;
    /// <summary>Decorations inherited by propagation from the ancestors of the text
    /// run (CSS Text Decoration 4 §5.1), ordered outermost first. Each layer keeps the
    /// originating box's own line list, style, color and metrics.</summary>
    public List<AppliedTextDecoration>? AncestorDecorations { get; set; }
    public List<TextShadowValue>? TextShadows { get; set; }
    public float LetterSpacing { get; set; }
    public bool Italic { get; set; }
    /// <summary>Horizontal shear (tan of the angle) applied to the glyphs only.
    /// 'font-style: oblique &lt;angle&gt;' names the slant to synthesize, and the advance
    /// widths stay the upright ones (CSS Fonts 4 §3.2.2).</summary>
    public float ObliqueSkewX { get; set; }
    public string EmphasisMark { get; set; } = string.Empty;
    public bool EmphasisOver { get; set; } = true;
    public SKColor EmphasisColor { get; set; }

    /// <summary>
    /// When a text op is replayed into a TRANSPARENT layer bitmap (scroll-layer
    /// content bake), LCD/subpixel antialiasing would fringe color once composited
    /// over the container's colored background. Set during that raster so fonts use
    /// grayscale AA instead. Ambient flag — rendering is single-threaded.
    /// </summary>
    public static bool LayerBakeGrayscale;

    /// <summary>
    /// Requested AA tier, pushed by <see cref="SkiaRenderer"/> from
    /// <see cref="RenderingSettings.AntiAliasing"/>. Drives both the glyph edging
    /// and the hinting level, so the setting actually changes output instead of
    /// being read and discarded.
    /// </summary>
    public static AntiAliasMode AntiAlias = AntiAliasMode.High;

    /// <summary>
    /// Whether LCD (subpixel) glyph edging may be requested. The page pipeline
    /// always composites text through an SKPicture or a transparent tile bitmap,
    /// where Skia silently falls back to grayscale — so this is only honoured for
    /// draws that reach the opaque window surface directly (chrome, HUD).
    /// </summary>
    public static bool UseSubpixelAA;

    public override void Reset()
    {
        base.Reset();
        Text = string.Empty;
        X = Y = 0;
        Color = default;
        FontSize = 16;
        FontFamily = "Arial";
        FontWeight = FontWeight.Normal;
        TextAlign = TextAlignType.Start;
        MaxWidth = null;
        Underline = LineThrough = Overline = false;
        UnderlineColor = default;
        DecorationStyle = TextDecorationStyleType.Solid;
        DecorationThickness = float.NaN;
        DecorationThicknessFromFont = false;
        DecorationUnderlineOffset = float.NaN;
        DecorationUnderlinePosition = TextUnderlinePositionType.Auto;
        DecorationSkipInk = true;
        AncestorDecorations = null;
        TextShadows = null;
        LetterSpacing = 0;
        Italic = false;
        ObliqueSkewX = 0;
        EmphasisMark = string.Empty;
        EmphasisOver = true;
        EmphasisColor = default;
    }

    public override void Execute(SKCanvas canvas)
    {
        if (string.IsNullOrEmpty(Text)) return;

        var paint = GetTextPaint(Color);

        float x = X;

        // Measure the run width up-front: under/over decorations paint before
        // the text (so glyphs sit on top), but still need the text extent.
        bool needsAlignment = TextAlign == TextAlignType.Center || TextAlign == TextAlignType.End || TextAlign == TextAlignType.Right;

        float totalWidth;
        if (needsAlignment)
        {
            totalWidth = MeasureTextWithFallback(canvas, paint, x, Y, dryRun: true);
        }
        else
        {
            totalWidth = 0;
        }

        if (TextAlign == TextAlignType.Center)
            x -= totalWidth / 2;
        else if (TextAlign == TextAlignType.End || TextAlign == TextAlignType.Right)
            x -= totalWidth;

        // Snap coordinates to device pixels to reduce subpixel differences
        var (sx, sy) = GetCanvasScale(canvas);
        float drawX = SnapToDevice(x, sx);
        float drawY = SnapToDevice(Y, sy);
        float actualWidth = MeasureTextWithFallback(canvas, paint, drawX, drawY, dryRun: true);

        float ascent = GetFontAscent();

        // Paint order mirrors the engine's text fragment painter fast path:
        // 1. underline/overline (with shadow passes) before the text,
        // 2. text shadows + text,
        // 3. line-through (with shadow passes) after the text.
        // A propagated line belongs to an ancestor box but is drawn per text
        // fragment, outermost first, so nested underlines stack instead of
        // being replaced (CSS Text Decoration 4 §5.1).
        Func<float, float, List<SKRect>?> skipInk = (upper, stripe) => ComputeSkipInkClips(upper, stripe, drawX, drawY);
        var ownGeometry = BuildDecorationGeometry(DecorationThickness, DecorationThicknessFromFont,
            DecorationUnderlineOffset, DecorationUnderlinePosition, DecorationSkipInk, FontSize);
        PaintUnderOrOver(AncestorDecorations, canvas, drawX, actualWidth, drawY, ascent, skipInk);
        if (Underline || Overline)
        {
            TextDecorationPainter.PaintUnderOrOverLines(
                canvas, drawX, actualWidth, drawY, ascent, FontSize,
                Underline, Overline,
                DecorationStyle,
                Color,
                UnderlineColor.Alpha > 0 ? UnderlineColor : Color,
                TextShadows,
                skipInk,
                ownGeometry);
        }

        // Draw text shadows before main text. CSS Backgrounds §text-shadow paints the
        // FIRST-listed shadow on top, so iterate the list in reverse (last drawn first,
        // i.e. furthest back) to get the correct stacking.
        if (TextShadows != null && TextShadows.Count > 0)
        {
            for (int si = TextShadows.Count - 1; si >= 0; si--)
            {
                var shadow = TextShadows[si];
                using var shadowPaint = new SKPaint
                {
                    Color = shadow.Color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                    // The CSS blur length is a blur radius, i.e. half the Gaussian
                    // standard deviation that Skia's blur filter expects.
                    ImageFilter = shadow.BlurRadius > 0
                        ? SKImageFilter.CreateBlur(shadow.BlurRadius / 2f, shadow.BlurRadius / 2f)
                        : null
                };
                float shadowX = SnapToDevice(drawX + shadow.OffsetX, sx);
                float shadowY = SnapToDevice(drawY + shadow.OffsetY, sy);
                var shadowFont = CreateFont(GetTypeface());
                int shadowSlant = PushGlyphSlant(canvas, shadowX, shadowY);
                canvas.DrawText(Text, shadowX, shadowY, SKTextAlign.Left, shadowFont, shadowPaint);
                PopGlyphSlant(canvas, shadowSlant);
            }
        }

        MeasureTextWithFallback(canvas, paint, drawX, drawY, dryRun: false, snapScale: sx);

        if (LineThrough)
        {
            TextDecorationPainter.PaintLineThrough(
                canvas, drawX, actualWidth, drawY, ascent, FontSize,
                true,
                DecorationStyle,
                UnderlineColor.Alpha > 0 ? UnderlineColor : Color,
                TextShadows,
                ownGeometry);
        }
        PaintLineThrough(AncestorDecorations, canvas, drawX, actualWidth, drawY, ascent);

        if (!string.IsNullOrEmpty(EmphasisMark))
        {
            DrawEmphasisMarks(canvas, drawX, drawY, ascent);
        }
    }

    private TextDecorationPainter.DecorationGeometry BuildDecorationGeometry(
        float thickness, bool thicknessFromFont, float underlineOffset,
        TextUnderlinePositionType underlinePosition, bool skipInk, float fontSize)
    {
        var metrics = FontMetricsFor(fontSize);
        return new TextDecorationPainter.DecorationGeometry
        {
            Thickness = thickness,
            ThicknessFromFont = thicknessFromFont,
            UnderlineOffset = underlineOffset,
            UnderlinePosition = underlinePosition,
            SkipInk = skipInk,
            FontUnderlineThickness = metrics.UnderlineThickness,
            FontUnderlinePosition = MathF.Abs(metrics.UnderlinePosition),
            Descent = metrics.FloatDescent,
        };
    }

    /// <summary>Metrics of the run's own typeface at an arbitrary size — a
    /// propagated decoration keeps the metrics of the box that originated it.</summary>
    private Core.Fonts.FontMetrics FontMetricsFor(float fontSize)
    {
        try
        {
            return Core.Fonts.FontMetricsProvider.Get(GetTypeface(), fontSize);
        }
        catch
        {
            return Core.Fonts.FontMetricsProvider.Get((SKTypeface?)null, fontSize);
        }
    }

    private void PaintUnderOrOver(List<AppliedTextDecoration>? decorations, SKCanvas canvas,
        float drawX, float width, float baseline, float ascent,
        Func<float, float, List<SKRect>?> skipInk)
    {
        if (decorations == null)
            return;
        foreach (var decoration in decorations)
        {
            if (!decoration.HasUnderline && !decoration.HasOverline)
                continue;
            float originSize = decoration.OriginFontSize > 0 ? decoration.OriginFontSize : 16f;
            TextDecorationPainter.PaintUnderOrOverLines(
                canvas, drawX, width, baseline, ascent, originSize,
                decoration.HasUnderline, decoration.HasOverline,
                decoration.Style, decoration.Color, decoration.Color,
                null, skipInk, BuildGeometryFor(decoration, originSize));
        }
    }

    private void PaintLineThrough(List<AppliedTextDecoration>? decorations, SKCanvas canvas,
        float drawX, float width, float baseline, float ascent)
    {
        if (decorations == null)
            return;
        foreach (var decoration in decorations)
        {
            if (!decoration.HasLineThrough)
                continue;
            float originSize = decoration.OriginFontSize > 0 ? decoration.OriginFontSize : 16f;
            TextDecorationPainter.PaintLineThrough(
                canvas, drawX, width, baseline, ascent, originSize,
                true, decoration.Style, decoration.Color, null,
                BuildGeometryFor(decoration, originSize));
        }
    }

    private TextDecorationPainter.DecorationGeometry BuildGeometryFor(AppliedTextDecoration decoration, float originSize) =>
        BuildDecorationGeometry(decoration.Thickness, decoration.ThicknessFromFont,
            decoration.UnderlineOffsetIsAuto ? float.NaN : decoration.UnderlineOffset,
            decoration.UnderlinePosition, decoration.SkipInk, originSize);

    private void DrawEmphasisMarks(SKCanvas canvas, float x, float y, float textAscent)
    {
        var mark = EmphasisMark;
        var text = Text;
        if (string.IsNullOrEmpty(mark) || string.IsNullOrEmpty(text)) return;

        // The marks are punctuation-sized: drawn from a CJK emphasis font at half the
        // text size, which is also what the line box reserves room for (TextEmphasisMarks).
        float markSize = FontSize * UpBrowser.Core.Css.TextEmphasisMarks.FontSizeFactor;
        var markTypeface = UpBrowser.Core.Css.TextEmphasisMarks.ResolveMarkTypeface(mark[0])
            ?? GetTypefaceForChar(mark[0]);
        var markFont = CreateFont(markTypeface, markSize);
        float glyphCenterX = markFont.MeasureText(mark) / 2;
        var mm = markFont.Metrics;
        float markAscent = -mm.Ascent;
        float markDescent = Math.Max(0, mm.Descent);

        // The mark sits outside the text's own ascent/descent box, separated by a
        // small distance that grows with the font size (CSS Text 4 4.2.1). Using
        // the text metrics (not the mark's) is what keeps an 'over' mark directly
        // above the glyphs instead of landing on the previous line.
        var tm = FontMetricsFor(FontSize);
        float textDescent = Math.Max(0, tm.FloatDescent);
        float separation = UpBrowser.Core.Css.TextEmphasisMarks.Separation(FontSize);
        float offset = EmphasisOver
            ? -(textAscent + separation + markDescent)
            : textDescent + separation + markAscent;

        var markPaint = GetTextPaint(EmphasisColor.Alpha > 0 ? EmphasisColor : Color);

        float currentX = x;
        int len = text.Length;
        int runStart = 0;
        SKTypeface currentTypeface = GetTypefaceForChar(text[0]);
        for (int i = 1; i <= len; i++)
        {
            if (i < len)
            {
                char c = text[i];
                SKTypeface neededTypeface = GetTypefaceForChar(c);
                if (neededTypeface != currentTypeface)
                {
                    currentX = DrawEmphasisRun(canvas, markPaint, markFont, text[runStart..i], currentX, y, offset, glyphCenterX, currentTypeface);
                    currentTypeface = neededTypeface;
                    runStart = i;
                }
            }
            else
            {
                currentX = DrawEmphasisRun(canvas, markPaint, markFont, text[runStart..i], currentX, y, offset, glyphCenterX, currentTypeface);
            }
        }
    }

    private float DrawEmphasisRun(SKCanvas canvas, SKPaint markPaint, SKFont markFont, string run, float x, float y, float offset, float glyphCenterX, SKTypeface typeface)
    {
        float currentX = x;
        var font = CreateFont(typeface);
        for (int i = 0; i < run.Length; i++)
        {
            char c = run[i];
            float charWidth = font.MeasureText(run[i].ToString());
            if (CanReceiveTextEmphasis(c))
            {
                float centerX = currentX + charWidth / 2;
                canvas.DrawText(EmphasisMark, centerX - glyphCenterX, y + offset, SKTextAlign.Left, markFont, markPaint);
            }
            currentX += charWidth + (i < run.Length - 1 ? LetterSpacing : 0);
        }
        return currentX;
    }

    private static bool CanReceiveTextEmphasis(char c)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        if (category == UnicodeCategory.SpaceSeparator || category == UnicodeCategory.LineSeparator ||
            category == UnicodeCategory.ParagraphSeparator || category == UnicodeCategory.OtherNotAssigned ||
            category == UnicodeCategory.Control || category == UnicodeCategory.Format)
            return false;
        int cp = c;
        if (cp == 0x1361 || cp == 0x10100 || cp == 0x10101 || cp == 0x1039F || cp == 0x0F0B || cp == 0x0F0C)
            return false;
        return true;
    }

    private float GetFontAscent()
    {
        try
        {
            return Core.Fonts.FontMetricsProvider.Get(GetTypeface(), FontSize).FloatAscent;
        }
        catch
        {
            return Core.Fonts.FontMetricsProvider.Get((SKTypeface?)null, FontSize).FloatAscent;
        }
    }

    private float MeasureTextWithFallback(SKCanvas canvas, SKPaint paint, float x, float y, bool dryRun, float snapScale = 1f)
    {
        if (string.IsNullOrEmpty(Text)) return 0;

        var text = Text;
        int len = text.Length;
        float currentX = x;

        int runStart = 0;
        SKTypeface currentTypeface = GetTypefaceForChar(text[0]);

        for (int i = 1; i <= len; i++)
        {
            if (i < len)
            {
                char c = text[i];
                SKTypeface neededTypeface = GetTypefaceForChar(c);

                if (neededTypeface != currentTypeface)
                {
                    string run = text[runStart..i];
                    var font = CreateFont(currentTypeface);
                    float runWidth = MeasureRunWidth(run, currentTypeface);
                    if (!dryRun)
                        DrawRunWithSpacing(canvas, paint, font, run, SnapToDevice(currentX, snapScale), y);
                    currentX += runWidth;

                    currentTypeface = neededTypeface;
                    runStart = i;
                }
            }
            else
            {
                string run = text[runStart..i];
                var font = CreateFont(currentTypeface);
                float runWidth = MeasureRunWidth(run, currentTypeface);
                if (!dryRun)
                    DrawRunWithSpacing(canvas, paint, font, run, SnapToDevice(currentX, snapScale), y);
                currentX += runWidth;
            }
        }

        return currentX - x;
    }

    /// <summary>
    /// Draw one same-typeface run. With a non-zero letter-spacing the glyphs are
    /// placed individually (Skia has no run-level tracking on DrawText); the
    /// per-glyph advance matches MeasureRunWidth's base width + spacing model.
    /// </summary>
    private void DrawRunWithSpacing(SKCanvas canvas, SKPaint paint, SKFont font, string run, float x, float y)
    {
        int slant = PushGlyphSlant(canvas, x, y);
        if (LetterSpacing == 0 || run.Length <= 1)
        {
            canvas.DrawText(run, x, y, SKTextAlign.Left, font, paint);
            PopGlyphSlant(canvas, slant);
            return;
        }
        float cx = x;
        for (int i = 0; i < run.Length; i++)
        {
            string ch = run[i].ToString();
            canvas.DrawText(ch, cx, y, SKTextAlign.Left, font, paint);
            cx += font.MeasureText(ch) + (i < run.Length - 1 ? LetterSpacing : 0);
        }
        PopGlyphSlant(canvas, slant);
    }

    /// <summary>Shear the glyphs about their baseline. The pivot's inline position is
    /// irrelevant for a horizontal shear, so each run can use its own origin.</summary>
    private int PushGlyphSlant(SKCanvas canvas, float x, float y)
    {
        if (ObliqueSkewX == 0) return -1;
        int save = canvas.Save();
        canvas.Translate(x, y);
        canvas.Skew(ObliqueSkewX, 0f);
        canvas.Translate(-x, -y);
        return save;
    }

    private static void PopGlyphSlant(SKCanvas canvas, int save)
    {
        if (save >= 0) canvas.RestoreToCount(save);
    }

    /// <summary>
    /// Computes the skip-ink clip rects for the given decoration stripe band.
    /// Mirrors the engine's ClipDecorationsStripe: for each text run
    /// (same typeface fallback as <see cref="MeasureTextWithFallback"/>) the
    /// glyph ink intercepts of the band [upper, upper + stripe] are queried via
    /// SKTextBlob.GetIntercepts and each x-interval becomes a clip rect at
    /// (runX + begin, upper) of size (end - begin, stripe), outset vertically by
    /// 1px and horizontally by min(thickness, 13). Rectangles are in canvas
    /// coordinates, ready for SKClipOperation.Difference.
    /// </summary>
    private List<SKRect>? ComputeSkipInkClips(float upper, float stripe, float drawX, float drawY)
    {
        if (string.IsNullOrEmpty(Text))
            return null;
        if (stripe <= 0)
            return null;

        var clips = new List<SKRect>();
        // 'stripe' is the band height after the 0.5px inset on each side, so
        // stripe + 1 is the resolved thickness of the line being skipped — an
        // authored 'text-decoration-thickness' has to widen the dilation too.
        float dilation = MathF.Min(stripe + 1f, 13f);

        using var interceptPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        var text = Text;
        int len = text.Length;
        float currentX = 0;
        int runStart = 0;
        SKTypeface currentTypeface = GetTypefaceForChar(text[0]);

        for (int i = 1; i <= len; i++)
        {
            if (i < len)
            {
                char c = text[i];
                SKTypeface neededTypeface = GetTypefaceForChar(c);
                if (neededTypeface != currentTypeface)
                {
                    currentX += AddRunIntercepts(clips, text[runStart..i], currentX, upper, stripe, dilation, currentTypeface, interceptPaint, drawX, drawY);
                    currentTypeface = neededTypeface;
                    runStart = i;
                }
            }
            else
            {
                currentX += AddRunIntercepts(clips, text[runStart..i], currentX, upper, stripe, dilation, currentTypeface, interceptPaint, drawX, drawY);
            }
        }

        return clips.Count > 0 ? clips : null;
    }

    private float AddRunIntercepts(List<SKRect> clips, string run, float runX, float upper, float stripe, float dilation,
        SKTypeface typeface, SKPaint interceptPaint, float drawX, float drawY)
    {
        if (run.Length == 0)
            return 0;

        var font = CreateFont(typeface);
        float runWidth = MeasureRunWidth(run, typeface);

        using var blob = SKTextBlob.Create(run, font, new SKPoint(0, 0));
        var intervals = blob.GetIntercepts(upper, upper + stripe, interceptPaint);
        if (intervals != null)
        {
            for (int k = 0; k + 1 < intervals.Length; k += 2)
            {
                float begin = intervals[k];
                float end = intervals[k + 1];
                float x0 = drawX + runX + begin - dilation;
                float x1 = drawX + runX + end + dilation;
                float y0 = drawY + upper - 1f;
                float y1 = drawY + upper + stripe + 1f;
                clips.Add(new SKRect(x0, y0, x1, y1));
            }
        }

        return runWidth;
    }

    private SKTypeface GetTypefaceForChar(char c)
    {
        int codePoint = c;
        bool isCjk = (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) ||
                     (c >= 0x20000 && c <= 0x2A6DF) || (c >= 0x2B740 && c <= 0x2B81F) ||
                     (c >= 0x2B820 && c <= 0x2CEAF) || (c >= 0x3000 && c <= 0x303F) ||
                     (c >= 0xFF00 && c <= 0xFFEF);
        // Hangul and kana are not Han, and they are above U+2600, so the open-ended
        // emoji test below used to claim them and drew them from an emoji face that
        // has no such glyphs (tofu). They go through the same fallback chain the
        // measurer uses, so painting agrees with the advance that was laid out.
        bool isKorean = (c >= 0x1100 && c <= 0x11FF) || (c >= 0x302E && c <= 0x302F) ||
                        (c >= 0x3130 && c <= 0x318F) || (c >= 0xA960 && c <= 0xA97F) ||
                        (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xD7B0 && c <= 0xD7FF);
        bool isKana = (c >= 0x3040 && c <= 0x30FF) || (c >= 0x31F0 && c <= 0x31FF);
        // Emoji live in the astral planes, so in UTF-16 they arrive as a surrogate
        // PAIR: the individual code units are 0xD800-0xDFFF, below any astral value.
        bool isEmoji = char.IsSurrogate(c) || c >= 0x1F000;
        bool isSpecialSymbol = (c >= 0x2000 && c <= 0x206F) || (c >= 0x2100 && c <= 0x27BF) ||
                               (c >= 0x2800 && c <= 0x28FF) || c == 0x00
                               || c == 0x00A9 ||
                               c == 0x00AE || (c >= 0x2190 && c <= 0x21FF) ||
                               (c >= 0x2200 && c <= 0x22FF) || (c >= 0x2300 && c <= 0x23FF);

        // First try matching from requested font family
        if (!string.IsNullOrEmpty(FontFamily))
        {
            var familyTf = GetCachedFamilyTypeface();
            if (familyTf != null && GlyphPresentCached(familyTf, codePoint))
                return familyTf;
        }

        if (isCjk)
            return GetCachedChineseTypeface();
        if (isKorean || isKana)
        {
            var scriptTypeface = Core.Fonts.FontManager.GetFallbackTypeface(codePoint);
            if (scriptTypeface != null && GlyphPresentCached(scriptTypeface, codePoint))
                return scriptTypeface;
            return GetCachedChineseTypeface();
        }
        if (isEmoji)
            return GetCachedEmojiTypeface() ?? GetCachedChineseTypeface();
        if (isSpecialSymbol)
        {
            // Use the fallback chain to find a font that actually contains the
            // glyph — the default typeface (e.g. Arial) often lacks arrows, math
            // symbols, etc. even though the character range is recognised.
            var fallback = Core.Fonts.FontManager.GetFallbackTypeface(codePoint);
            if (fallback != null)
                return fallback;
            return GetCachedChineseTypeface();
        }

        var defaultTf = GetCachedDefaultTypeface();
        if (GlyphPresentCached(defaultTf, codePoint))
            return defaultTf;

        // Anything else the requested and default faces cannot draw goes through the
        // same chain the measurer uses (Arabic, Thai, Cyrillic-ext, …); falling back
        // to the Chinese face here produced tofu with a Han advance instead.
        var tailTypeface = Core.Fonts.FontManager.GetFallbackTypeface(codePoint);
        if (tailTypeface != null && GlyphPresentCached(tailTypeface, codePoint))
            return tailTypeface;

        return GetCachedChineseTypeface();
    }

    private static SKTypeface? _cachedChineseTypeface;
    private static bool _isChineseTypefaceDisposed = false;

    private static SKTypeface? _cachedDefaultTypeface;
    private static bool _isDefaultTypefaceDisposed = false;

    private static SKTypeface? _cachedEmojiTypeface;
    private static bool _isEmojiTypefaceDisposed = false;

    private static string[] _cachedFontFamilies = null!;
    private static readonly object _fontFamiliesLock = new();
    private static readonly Dictionary<string, SKTypeface> _globalTypefaceCache = new();
    private static readonly LinkedList<string> _typefaceCacheOrder = new();
    private const int MaxTypefaceCacheSize = 64;

    // ── Shared text resources (SKFont / SKPaint) ────────────────────────────
    // Text ops replay on every picture re-record (element scroll, layer re-bake,
    // snapshot), and each replay used to allocate a fresh native SKFont + SKPaint
    // per run. Caching them keyed by font identity keeps re-records allocation-free.
    private static readonly object _textResourceLock = new();
    private static readonly Dictionary<string, SKFont> _fontCache = new();
    private static readonly LinkedList<string> _fontCacheOrder = new();
    private const int MaxFontCacheSize = 512;
    private static readonly Dictionary<uint, SKPaint> _textPaintCache = new();
    private static readonly LinkedList<uint> _textPaintCacheOrder = new();
    private const int MaxTextPaintCacheSize = 128;

    // ── Per-character font fallback caches ──────────────────────────────────
    // GetTypefaceForChar runs once per character per op execution; the family
    // typeface lookup and the native ContainsGlyph check were redone every time.
    // Cache the resolved family typeface and per-(typeface, codepoint) glyph
    // presence so re-records hit two dictionary lookups instead of font manager
    // round-trips.
    private static readonly Dictionary<string, SKTypeface> _familyTypefaceCache = new();
    private const int MaxFamilyTypefaceCacheSize = 64;
    private static readonly Dictionary<SKTypeface, int> _typefaceIds = new(ReferenceEqualityComparer.Instance);
    private static int _nextTypefaceId = 1;
    private static readonly Dictionary<long, bool> _glyphPresenceCache = new();
    private static readonly LinkedList<long> _glyphPresenceOrder = new();
    private const int MaxGlyphPresenceCacheSize = 2048;

    // Text run widths: each DrawTextOp measures every run twice (dry-run for
    // alignment + real draw). Cache the width per (font identity, run) so the
    // second pass and later re-records skip re-shaping.
    private static readonly Dictionary<string, float> _runWidthCache = new();
    private static readonly LinkedList<string> _runWidthOrder = new();
    private const int MaxRunWidthCacheSize = 4096;

    private float MeasureRunWidth(string run, SKTypeface typeface)
    {
        if (run.Length == 0) return 0;
        if (run.Length > 256)
        {
            var f = CreateFont(typeface);
            return f.MeasureText(run) + run.Length * LetterSpacing;
        }
        string key = $"{typeface?.FamilyName}|{FontSize}|{(int)FontWeight}|{(Italic ? 1 : 0)}|{LetterSpacing}|{run}";
        lock (_runWidthCache)
        {
            if (_runWidthCache.TryGetValue(key, out var width))
            {
                _runWidthOrder.Remove(key);
                _runWidthOrder.AddFirst(key);
                return width;
            }
        }
        var font = CreateFont(typeface);
        float w = font.MeasureText(run) + run.Length * LetterSpacing;
        lock (_runWidthCache)
        {
            if (_runWidthCache.Count >= MaxRunWidthCacheSize)
            {
                var last = _runWidthOrder.Last;
                if (last != null)
                {
                    _runWidthCache.Remove(last.Value);
                    _runWidthOrder.RemoveLast();
                }
            }
            _runWidthCache[key] = w;
            _runWidthOrder.Remove(key);
            _runWidthOrder.AddFirst(key);
        }
        return w;
    }

    private SKTypeface GetCachedFamilyTypeface()
    {
        int styleIdx = FontWeight >= FontWeight.Bold ? 1 : 0;
        // Walk the FULL CSS font-family list and return the first family that is
        // installed. Generic names (sans-serif / serif / monospace / ...) map to
        // system defaults. Glyph presence is checked per-character by the caller,
        // so per-glyph fallback to CJK/emoji still works.
        foreach (var raw in FontFamily.Split(','))
        {
            string fontName = raw.Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(fontName)) continue;
            SKTypeface? tf = fontName.ToLowerInvariant() switch
            {
                "sans-serif" or "system-ui" or "cursive" or "fantasy" => GetCachedDefaultTypeface(),
                "serif" => GetCachedSerifTypeface() ?? GetCachedDefaultTypeface(),
                "monospace" => GetCachedMonoTypeface() ?? GetCachedDefaultTypeface(),
                _ => GetFamilyTypeface(fontName, styleIdx),
            };
            if (tf != null) return tf;
        }
        return GetCachedDefaultTypeface();
    }

    /// <summary>Resolve one installed family to a typeface (cached, bounded).</summary>
    private static SKTypeface GetFamilyTypeface(string fontName, int styleIdx)
    {
        string key = $"{fontName}|{styleIdx}";
        lock (_glyphPresenceCache)
        {
            if (_familyTypefaceCache.TryGetValue(key, out var cached))
                return cached;
        }
        SKTypeface? tf = null;
        var families = GetFontFamilies();
        var index = Array.IndexOf(families, fontName);
        if (index >= 0)
        {
            var styles = SKFontManager.Default.GetFontStyles(index);
            if (styleIdx >= styles.Count) styleIdx = 0;
            tf = styles.CreateTypeface(styleIdx);
        }
        if (tf != null)
        {
            lock (_glyphPresenceCache)
            {
                if (_familyTypefaceCache.Count >= MaxFamilyTypefaceCacheSize)
                {
                    var oldest = _familyTypefaceCache.Keys.First();
                    _familyTypefaceCache.Remove(oldest);
                }
                _familyTypefaceCache[key] = tf;
            }
        }
        return tf ?? SKTypeface.Default;
    }

    private static SKTypeface? _cachedSerifTypeface;
    private static bool _isSerifTypefaceDisposed;
    private static SKTypeface? _cachedMonoTypeface;
    private static bool _isMonoTypefaceDisposed;

    private static SKTypeface? GetCachedSerifTypeface()
    {
        if (_cachedSerifTypeface != null && !_isSerifTypefaceDisposed)
        {
            try { return _cachedSerifTypeface; }
            catch (ObjectDisposedException) { _isSerifTypefaceDisposed = true; _cachedSerifTypeface = null; }
        }
        var families = GetFontFamilies();
        foreach (var fontName in new[] { "Times New Roman", "Georgia", "SimSun", "Nimbus Roman" })
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedSerifTypeface = tf;
                    _isSerifTypefaceDisposed = false;
                    return tf;
                }
            }
        }
        return null;
    }

    private static SKTypeface? GetCachedMonoTypeface()
    {
        if (_cachedMonoTypeface != null && !_isMonoTypefaceDisposed)
        {
            try { return _cachedMonoTypeface; }
            catch (ObjectDisposedException) { _isMonoTypefaceDisposed = true; _cachedMonoTypeface = null; }
        }
        var families = GetFontFamilies();
        foreach (var fontName in new[] { "Consolas", "Courier New", "DejaVu Sans Mono", "Liberation Mono", "Menlo", "Monaco" })
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedMonoTypeface = tf;
                    _isMonoTypefaceDisposed = false;
                    return tf;
                }
            }
        }
        return null;
    }

    private static int TypefaceId(SKTypeface typeface)
    {
        lock (_glyphPresenceCache)
        {
            if (_typefaceIds.TryGetValue(typeface, out var id)) return id;
            id = _nextTypefaceId++;
            _typefaceIds[typeface] = id;
            return id;
        }
    }

    private static bool GlyphPresentCached(SKTypeface typeface, int codepoint)
    {
        long key = ((long)TypefaceId(typeface) << 21) | (uint)codepoint;
        lock (_glyphPresenceCache)
        {
            if (_glyphPresenceCache.TryGetValue(key, out var present))
            {
                _glyphPresenceOrder.Remove(key);
                _glyphPresenceOrder.AddFirst(key);
                return present;
            }
        }
        bool result;
        using (var f = new SKFont(typeface, 12))
            result = f.ContainsGlyph(codepoint);
        lock (_glyphPresenceCache)
        {
            if (_glyphPresenceCache.Count >= MaxGlyphPresenceCacheSize)
            {
                var last = _glyphPresenceOrder.Last;
                if (last != null)
                {
                    _glyphPresenceCache.Remove(last.Value);
                    _glyphPresenceOrder.RemoveLast();
                }
            }
            _glyphPresenceCache[key] = result;
            _glyphPresenceOrder.Remove(key);
            _glyphPresenceOrder.AddFirst(key);
        }
        return result;
    }

    private static string FontIdentityKey(SKTypeface? typeface, float size, FontWeight weight, bool italic, float letterSpacing)
    {
        // AA tier and the LCD gate are part of font identity: two callers with
        // different tiers must not share a cached SKFont.
        int aa = ((int)AntiAlias << 1) | (UseSubpixelAA ? 1 : 0);
        return $"{typeface?.FamilyName ?? "?"}|{size}|{(int)weight}|{(italic ? 1 : 0)}|{letterSpacing}|{(LayerBakeGrayscale ? 1 : 0)}|{aa}";
    }

    private SKFont CreateFont(SKTypeface typeface, float? overrideSize = null)
    {
        float useSize = overrideSize ?? FontSize;
        var actualTypeface = typeface;
        if (typeface != null && (Italic || FontWeight != FontWeight.Normal))
        {
            var families = GetFontFamilies();
            var familyName = typeface.FamilyName;
            var index = Array.IndexOf(families, familyName);
            if (index >= 0)
            {
                var styles = SKFontManager.Default.GetFontStyles(index);
                SKFontStyleSlant slant = Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                SKFontStyleWeight weight = UpBrowser.Core.Fonts.FontFallbackChain.ConvertWeight(FontWeight);
                var targetStyle = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
                var tf = styles.CreateTypeface(targetStyle);
                if (tf != null) actualTypeface = tf;
            }
        }

        string key = FontIdentityKey(actualTypeface, useSize, FontWeight, Italic, LetterSpacing);
        lock (_textResourceLock)
        {
            if (_fontCache.TryGetValue(key, out var cached))
            {
                _fontCacheOrder.Remove(key);
                _fontCacheOrder.AddFirst(key);
                return cached;
            }
        }

        var font = new SKFont(actualTypeface, useSize);
        // NOTE: no #if SUPPORT_WINXP guard here. This project defines SUPPORT_WINXP,
        // so anything inside `#if !SUPPORT_WINXP` is silently excluded from the
        // build — which is how every glyph in the browser spent its whole life
        // rasterised with Skia's defaults (no hinting at all) and read as soft.
        if (UseSubpixelAA && !LayerBakeGrayscale)
        {
            // Direct draw onto the window surface: LCD-filtered edges give the
            // finest apparent resolution. Skia collapses this to grayscale
            // whenever the device matrix is scaled or the destination is not
            // opaque, so it is safe to request unconditionally.
            font.Edging = SKFontEdging.SubpixelAntialias;
            font.Subpixel = true;
            font.Hinting = SKFontHinting.Normal;
        }
        else
        {
            // Page content is composited through a picture / transparent tile, so
            // glyphs must be grayscale to survive layering without colour fringing.
            font.Edging = AntiAlias == AntiAliasMode.None
                ? SKFontEdging.Alias     // aliased: glyph edges locked to the pixel grid
                : SKFontEdging.Antialias;
            font.Subpixel = false;
            // Crispness comes from hinting: Full = Normal + stem snapping, which
            // locks stems and counters to whole device pixels. CJK faces and
            // italics stay at Normal — snapping a slanted or densely-stemmed
            // outline reads as shimmer (see FontHelper.CrispHinting).
            font.Hinting = AntiAlias switch
            {
                // A slanted outline - real italic or a synthesized oblique - must not
                // get stem snapping: it shimmers (see FontHelper.CrispHinting).
                AntiAliasMode.High => Italic || ObliqueSkewX != 0
                    ? SKFontHinting.Normal
                    : FontHelper.CrispHinting(actualTypeface),
                AntiAliasMode.None => FontHelper.CrispHinting(actualTypeface),
                _ => SKFontHinting.Normal,
            };
        }
        lock (_textResourceLock)
        {
            if (_fontCache.Count >= MaxFontCacheSize)
            {
                var last = _fontCacheOrder.Last;
                if (last != null)
                {
                    if (_fontCache.Remove(last.Value, out var evicted))
                        evicted.Dispose();
                    _fontCacheOrder.RemoveLast();
                }
            }
            _fontCache[key] = font;
            _fontCacheOrder.Remove(key);
            _fontCacheOrder.AddFirst(key);
        }
        return font;
    }

    /// <summary>Reusable fill paint for a color (avoids a native SKPaint per op).</summary>
    private static SKPaint GetTextPaint(SKColor color)
    {
        uint key = (uint)((color.Alpha << 24) | (color.Red << 16) | (color.Green << 8) | color.Blue);
        lock (_textResourceLock)
        {
            if (_textPaintCache.TryGetValue(key, out var cached))
            {
                _textPaintCacheOrder.Remove(key);
                _textPaintCacheOrder.AddFirst(key);
                return cached;
            }
        }
        var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        lock (_textResourceLock)
        {
            if (_textPaintCache.Count >= MaxTextPaintCacheSize)
            {
                var last = _textPaintCacheOrder.Last;
                if (last != null)
                {
                    if (_textPaintCache.Remove(last.Value, out var evicted))
                        evicted.Dispose();
                    _textPaintCacheOrder.RemoveLast();
                }
            }
            _textPaintCache[key] = paint;
            _textPaintCacheOrder.Remove(key);
            _textPaintCacheOrder.AddFirst(key);
        }
        return paint;
    }

    private static void CacheTypeface(string key, SKTypeface tf)
    {
        if (_globalTypefaceCache.Count >= MaxTypefaceCacheSize)
        {
            var last = _typefaceCacheOrder.Last;
            if (last != null)
            {
                _globalTypefaceCache.Remove(last.Value);
                _typefaceCacheOrder.RemoveLast();
            }
        }
        _globalTypefaceCache[key] = tf;
        _typefaceCacheOrder.Remove(key);
        _typefaceCacheOrder.AddFirst(key);
    }

    private static string[] GetFontFamilies()
    {
        if (_cachedFontFamilies == null)
        {
            lock (_fontFamiliesLock)
            {
                if (_cachedFontFamilies == null)
                    _cachedFontFamilies = SKFontManager.Default.FontFamilies.ToArray();
            }
        }
        return _cachedFontFamilies;
    }

    private static SKTypeface? GetCachedEmojiTypeface()
    {
        if (_cachedEmojiTypeface != null && !_isEmojiTypefaceDisposed)
        {
            try
            {
                return _cachedEmojiTypeface;
            }
            catch (ObjectDisposedException)
            {
                _isEmojiTypefaceDisposed = true;
                _cachedEmojiTypeface = null;
            }
        }

        var families = GetFontFamilies();
        string[] emojiFonts = { "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", "Segoe UI Symbol" };

        foreach (var fontName in emojiFonts)
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedEmojiTypeface = tf;
                    _isEmojiTypefaceDisposed = false;
                    return tf;
                }
            }
        }

        return null;
    }

    private static SKTypeface GetCachedChineseTypeface()
    {
        if (_cachedChineseTypeface != null && !_isChineseTypefaceDisposed)
        {
            try
            {
                return _cachedChineseTypeface;
            }
            catch (ObjectDisposedException)
            {
                _isChineseTypefaceDisposed = true;
                _cachedChineseTypeface = null;
            }
        }
        
        var families = GetFontFamilies();
        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "KaiTi", "FangSong", "YouYuan", "STSong", "PingFang SC", "Noto Sans SC", "Source Han Sans SC", "WenQuanYi Micro Hei", "Droid Sans Fallback" };
        
        foreach (var fontName in chineseFonts)
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedChineseTypeface = tf;
                    _isChineseTypefaceDisposed = false;
                    return tf;
                }
            }
        }
        
        _cachedChineseTypeface = SKTypeface.Default;
        _isChineseTypefaceDisposed = false;
        return _cachedChineseTypeface!;
    }
    
    private SKTypeface GetTypeface()
    {
        if (string.IsNullOrEmpty(FontFamily))
        {
            return ContainsChinese(Text) ? GetCachedChineseTypeface() : GetCachedDefaultTypeface();
        }

        var fontName = FontFamily.Split(',')[0].Trim().Trim('"', '\'');
        bool hasChinese = ContainsChinese(Text);
        var cacheKey = hasChinese ? $"tf:{fontName}:zh" : $"tf:{fontName}";

        lock (_globalTypefaceCache)
        {
            if (_globalTypefaceCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var families = GetFontFamilies();
        var index = Array.IndexOf(families, fontName);
        if (index >= 0)
        {
            var style = SKFontManager.Default.GetFontStyles(index);
            var tf = style.CreateTypeface(0);
            if (tf != null && tf.FamilyName != null)
            {
                lock (_globalTypefaceCache)
                    CacheTypeface(cacheKey, tf);
                return tf;
            }
        }

        if (hasChinese)
        {
            var chineseTf = GetCachedChineseTypeface();
            lock (_globalTypefaceCache)
                CacheTypeface(cacheKey, chineseTf);
            return chineseTf;
        }

        var defaultTf = GetCachedDefaultTypeface();
        lock (_globalTypefaceCache)
            CacheTypeface(cacheKey, defaultTf);
        return defaultTf;
    }
    
    private static SKTypeface GetCachedDefaultTypeface()
    {
        if (_cachedDefaultTypeface != null && !_isDefaultTypefaceDisposed)
        {
            try { return _cachedDefaultTypeface; }
            catch (ObjectDisposedException) { _isDefaultTypefaceDisposed = true; _cachedDefaultTypeface = null; }
        }
        
        var families = GetFontFamilies();
        string[] defaultFonts = { "Segoe UI", "Arial", "Tahoma", "Verdana", "Helvetica", "DejaVu Sans", "Liberation Sans" };
        
        foreach (var fontName in defaultFonts)
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedDefaultTypeface = tf;
                    _isDefaultTypefaceDisposed = false;
                    return tf;
                }
            }
        }
        
        _cachedDefaultTypeface = SKTypeface.Default;
        _isDefaultTypefaceDisposed = false;
        return _cachedDefaultTypeface!;
    }
    
    private static bool ContainsChinese(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
                return true;
            if (c >= 0x3400 && c <= 0x4DBF)
                return true;
            if (c >= 0x20000 && c <= 0x2A6DF)
                return true;
        }
        return false;
    }

    private static bool HasEmoji(string text)
    {
        foreach (char c in text)
        {
            if ((c >= 0x2600 && c <= 0x27BF) || c == 0x200D || c == 0xFE0F ||
                (c >= 0x1F000 && c <= 0x1FFFF) || (c >= 0x2300 && c <= 0x23FF) ||
                c == 0x2934 || c == 0x2935 || (c >= 0x2B00 && c <= 0x2BFF) ||
                c == 0x3030 || c == 0x303D || c == 0x3297 || c == 0x3299)
                return true;
        }
        return false;
    }

    private static SKTypeface? GetChineseFallbackTypeface()
    {
        if (_cachedChineseTypeface != null && !_isChineseTypefaceDisposed)
        {
            try
            {
                return _cachedChineseTypeface;
            }
            catch (ObjectDisposedException)
            {
                _isChineseTypefaceDisposed = true;
                _cachedChineseTypeface = null;
            }
        }
        
        var families = GetFontFamilies();
        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "KaiTi", "FangSong", "YouYuan", "STSong", "PingFang SC", "Noto Sans SC", "Source Han Sans SC" };
        
        foreach (var fontName in chineseFonts)
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedChineseTypeface = tf;
                    _isChineseTypefaceDisposed = false;
                    return tf;
                }
            }
        }
        
        _cachedChineseTypeface = SKTypeface.Default;
        _isChineseTypefaceDisposed = false;
        return _cachedChineseTypeface!;
    }
}

public class DrawImageOp : PaintOp
{
    public override bool Cullable => true;
    public SKImage? Image { get; set; }
    public SKRect SourceRect { get; set; }
    public SKRect DestRect { get; set; }
    public ImageFit Fit { get; set; } = ImageFit.Fill;
    /// <summary>Blend used against the backdrop, e.g. 'background-blend-mode'.</summary>
    public SKBlendMode BlendMode { get; set; } = SKBlendMode.SrcOver;

    public override void Reset()
    {
        base.Reset();
        Image = null;
        SourceRect = DestRect = default;
        Fit = ImageFit.Fill;
        BlendMode = SKBlendMode.SrcOver;
    }

    public override void Execute(SKCanvas canvas)
    {
        if (Image == null) return;

        SKRect dest;
        switch (Fit)
        {
            case ImageFit.Fill:
                dest = DestRect;
                break;
            case ImageFit.Cover:
                dest = CalculateCoverRect(Image.Width, Image.Height, DestRect);
                break;
            case ImageFit.Contain:
                dest = CalculateContainRect(Image.Width, Image.Height, DestRect);
                break;
            case ImageFit.ScaleDown:
                var containDest = CalculateContainRect(Image.Width, Image.Height, DestRect);
                var noneDest = CalculateNoneRect(Image.Width, Image.Height, DestRect);
                dest = containDest.Width * containDest.Height < noneDest.Width * noneDest.Height ? containDest : noneDest;
                break;
            default:
                dest = CalculateNoneRect(Image.Width, Image.Height, DestRect);
                break;
        }

        if (BlendMode == SKBlendMode.SrcOver)
        {
            canvas.DrawImage(Image, SourceRect, dest, new SKSamplingOptions(SKFilterMode.Linear), null);
            return;
        }
        var paint = new SKPaint { BlendMode = BlendMode };
        canvas.DrawImage(Image, SourceRect, dest, new SKSamplingOptions(SKFilterMode.Linear), paint);
    }

    private SKRect CalculateNoneRect(float srcW, float srcH, SKRect dest)
    {
        float w = Math.Min(srcW, dest.Width);
        float h = Math.Min(srcH, dest.Height);
        float x = dest.Left + (dest.Width - w) / 2;
        float y = dest.Top + (dest.Height - h) / 2;
        return new SKRect(x, y, x + w, y + h);
    }

    private SKRect CalculateCoverRect(float srcW, float srcH, SKRect dest)
    {
        float srcRatio = srcW / srcH;
        float destRatio = dest.Width / dest.Height;

        if (srcRatio > destRatio)
        {
            float newWidth = srcH * destRatio;
            float offset = (srcW - newWidth) / 2;
            return new SKRect(offset, 0, offset + newWidth, srcH);
        }
        else
        {
            float newHeight = srcW / destRatio;
            float offset = (srcH - newHeight) / 2;
            return new SKRect(0, offset, srcW, offset + newHeight);
        }
    }

    private SKRect CalculateContainRect(float srcW, float srcH, SKRect dest)
    {
        float srcRatio = srcW / srcH;
        float destRatio = dest.Width / dest.Height;

        if (srcRatio > destRatio)
        {
            float height = dest.Width / srcRatio;
            float y = dest.MidY - height / 2;
            return new SKRect(dest.Left, y, dest.Right, y + height);
        }
        else
        {
            float width = dest.Height * srcRatio;
            float x = dest.MidX - width / 2;
            return new SKRect(x, dest.Top, x + width, dest.Bottom);
        }
    }
}

public class DrawLineOp : PaintOp
{
    public override bool Cullable => true;
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public float StrokeWidth { get; set; } = 1;
    public SKColor Color { get; set; }

    public override void Reset()
    {
        base.Reset();
        X1 = Y1 = X2 = Y2 = 0;
        StrokeWidth = 1;
        Color = default;
    }

    public override void Execute(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokeWidth,
            IsAntialias = true
        };
        canvas.DrawLine(X1, Y1, X2, Y2, paint);
    }
}

public class DrawPathOp : PaintOp
{
    public override bool Cullable => true;
    public SKPath Path { get; set; } = new();
    public SKPaint? FillPaint { get; set; }
    public SKPaint? StrokePaint { get; set; }

    public override void Reset()
    {
        base.Reset();
        Path.Dispose();
        Path = new();
        FillPaint?.Dispose();
        FillPaint = null;
        StrokePaint?.Dispose();
        StrokePaint = null;
    }

    public override void Execute(SKCanvas canvas)
    {
        if (FillPaint != null)
            canvas.DrawPath(Path, FillPaint);
        if (StrokePaint != null)
            canvas.DrawPath(Path, StrokePaint);
    }
}

public class PushLayerOp : PaintOp
{
    public float Opacity { get; set; } = 1.0f;
    public SKRect ClipRect { get; set; }
    public bool HasClipRect { get; set; }
    public SKPath? ClipPath { get; set; }
    public SKImageFilter? ImageFilter { get; set; }
    public SKImage? MaskImage { get; set; }
    public SKBlendMode BlendMode { get; set; } = SKBlendMode.SrcOver;

    public override void Reset()
    {
        base.Reset();
        Opacity = 1.0f;
        ClipRect = default;
        HasClipRect = false;
        ClipPath?.Dispose();
        ClipPath = null;
        ImageFilter = null;
        MaskImage = null;
        BlendMode = SKBlendMode.SrcOver;
    }

    public override void Execute(SKCanvas canvas)
    {
        var paint = new SKPaint();
        paint.Color = SKColors.Black;
        if (Opacity < 1.0f)
            // Round rather than truncate: a layer alpha of 0.5 is exactly 128/255,
            // and truncating to 127 makes the composited result one level lighter
            // than every other engine's.
            paint.Color = paint.Color.WithAlpha((byte)Math.Clamp((int)(Opacity * 255f + 0.5f), 0, 255));
        if (ImageFilter != null)
            paint.ImageFilter = ImageFilter;
        if (BlendMode != SKBlendMode.SrcOver)
            paint.BlendMode = BlendMode;

        // Masking is applied inside the layer by MaskApplyOp (DstIn), because a
        // SaveLayer paint cannot itself act as a mask source.

        canvas.SaveLayer(paint);

        if (ClipPath != null)
            canvas.ClipPath(ClipPath, SKClipOperation.Intersect, true);
        else if (HasClipRect && ClipRect.Width > 0 && ClipRect.Height > 0)
            canvas.ClipRect(ClipRect, SKClipOperation.Intersect, true);
    }
}

/// <summary>
/// Multiplies the enclosing layer by a mask image's alpha (CSS Masking 1 §11):
/// emitted inside a push/pop layer pair, after the element's own content.
/// </summary>
public class MaskApplyOp : PaintOp
{
    public SKImage? Mask { get; set; }
    public SKRect Rect { get; set; }

    public override void Reset()
    {
        base.Reset();
        Mask = null;
        Rect = default;
    }

    public override void Execute(SKCanvas canvas)
    {
        if (Mask == null || Rect.Width <= 0 || Rect.Height <= 0)
            return;

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            BlendMode = SKBlendMode.DstIn,
        };
        paint.Shader = SKShader.CreateImage(Mask, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
            SKMatrix.CreateTranslation(Rect.Left, Rect.Top));
        canvas.DrawRect(Rect, paint);
    }
}

public class PopLayerOp : PaintOp
{
    public override void Reset() { base.Reset(); }
    public override void Execute(SKCanvas canvas) { canvas.Restore(); }
}

public class PushClipOp : PaintOp
{
    public SKRect ClipRect { get; set; }
    public SKPath? ClipPath { get; set; }
    public bool AntiAlias { get; set; } = true;

    public override void Reset()
    {
        base.Reset();
        ClipRect = default;
        ClipPath?.Dispose();
        ClipPath = null;
        AntiAlias = true;
    }

    public override void Execute(SKCanvas canvas)
    {
        canvas.Save();
        if (ClipPath != null)
            canvas.ClipPath(ClipPath, SKClipOperation.Intersect, AntiAlias);
        else
        {
            // Snap the clip to device pixels and clip without antialiasing:
            // an AA clip at a fractional edge feathers coverage below 100%,
            // letting the backdrop bleed through as a hairline seam between
            // adjacent boxes (e.g. grid cells).
            canvas.ClipRect(AlignRectToDevice(ClipRect, canvas), SKClipOperation.Intersect, false);
        }
    }
}

public class PopClipOp : PaintOp
{
    public override void Reset() { base.Reset(); }
    public override void Execute(SKCanvas canvas)
    {
        canvas.Restore();
    }
}

public class PushTransformOp : PaintOp
{
    public SKMatrix Matrix { get; set; } = SKMatrix.Identity;

    public override void Reset()
    {
        base.Reset();
        Matrix = SKMatrix.Identity;
    }

    public override void Execute(SKCanvas canvas)
    {
        canvas.Save();
        var m = Matrix;
        canvas.Concat(ref m);
    }
}

public class PopTransformOp : PaintOp
{
    public override void Reset() { base.Reset(); }
    public override void Execute(SKCanvas canvas)
    {
        canvas.Restore();
    }
}

public class DrawShadowOp : PaintOp
{
    public override bool Cullable => true;
    public SKPath Path { get; set; } = new();
    public SKColor Color { get; set; }
    public float BlurRadius { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public bool Inset { get; set; }

    public override void Reset()
    {
        base.Reset();
        Path.Dispose();
        Path = new();
        Color = default;
        BlurRadius = OffsetX = OffsetY = 0;
        Inset = false;
    }

    private const int MaxBlurCacheSize = 32;
    private static readonly Dictionary<float, SKImageFilter> _blurCache = new();
    private static readonly List<float> _blurCacheOrder = new();

    private static SKImageFilter GetOrCreateBlur(float radius)
    {
        if (radius <= 0) return null!;
        lock (_blurCache)
        {
            if (!_blurCache.TryGetValue(radius, out var filter))
            {
                filter = SKImageFilter.CreateBlur(radius, radius);
                // Evict oldest entry if cache is full
                if (_blurCache.Count >= MaxBlurCacheSize)
                {
                    float oldest = _blurCacheOrder[0];
                    _blurCacheOrder.RemoveAt(0);
                    if (_blurCache.Remove(oldest, out var oldFilter))
                        oldFilter?.Dispose();
                }
                _blurCache[radius] = filter;
                _blurCacheOrder.Add(radius);
            }
            return filter;
        }
    }

    internal static void ClearBlurCache()
    {
        lock (_blurCache)
        {
            foreach (var f in _blurCache.Values)
                f?.Dispose();
            _blurCache.Clear();
            _blurCacheOrder.Clear();
        }
    }

    public override void Execute(SKCanvas canvas)
    {
        // Fresh blur filter per draw (matches the standalone verification, avoids
        // any shared-cache/image-filter lifecycle issue).
        SKImageFilter blurFilter = BlurRadius > 0 ? SKImageFilter.CreateBlur(BlurRadius, BlurRadius) : null;
        using var paint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            ImageFilter = blurFilter
        };

        if (Inset)
        {
            canvas.Save();
            canvas.Translate(OffsetX, OffsetY);
            canvas.DrawPath(Path, paint);
            canvas.Restore();
            return;
        }

        canvas.Save();
        canvas.Translate(OffsetX, OffsetY);
        canvas.DrawPath(Path, paint);
        canvas.Restore();
    }
}

public enum ImageFit { Fill, Contain, Cover, None, ScaleDown }

/// <summary>
/// Renders a cached scroll-container layer plus its live scrollbar in one draw.
/// The layer image holds the container's full scrollable content at scroll = 0
/// (content-box local coordinates); Execute re-bakes the CURRENT scroll at record
/// time, so the compositor re-records the (cull-filtered) picture whenever a
/// scroll offset changes instead of re-painting the whole page.
/// </summary>
public sealed class DrawScrollLayerOp : PaintOp
{
    public override bool Cullable => true;
    public SKImage? Image;
    public UpBrowser.Core.Dom.LayoutBox Box = null!;
    public SKRect ContentBox;      // page coords
    public SKRect PaddingBox;      // page coords (scrollbar strip lives here)
    public float ScrollbarThickness = 12f;
    public SKColor TrackColor = new(240, 240, 240);
    public SKColor ThumbColor = new(180, 180, 180);
    public float ThumbRadius = 5f;
    public bool ShowVertical;
    public bool ShowHorizontal;
    /// <summary>True when the layer image already bakes the current scroll (sticky/
    /// z-index containers): no live translate is applied.</summary>
    public bool IsBaked;
    /// <summary>Device pixel ratio (DPR × resolution scale) the canvas maps
    /// logical→device at. Used to snap the scroll translate and draw rect to the
    /// device grid so the cached bitmap is never sampled at sub-pixel offsets.</summary>
    public float PhysicalScale = 1f;

    public override void Reset()
    {
        base.Reset();
        // The layer image is owned by ScrollLayerCache, never by this op.
        Image = null;
        Box = null!;
        PhysicalScale = 1f;
    }

    public override void Execute(SKCanvas canvas)
    {
        if (Image == null) return;
        float scale = PhysicalScale <= 0.01f ? 1f : PhysicalScale;
        float sx = IsBaked ? 0f : Box.ScrollX;
        float sy = IsBaked ? 0f : Box.ScrollY;

        canvas.Save();
        canvas.ClipRect(ContentBox);
        // Snap the live scroll translate to the device grid only when the container
        // is not smooth-scrolling (smooth scroll keeps a fractional offset so the
        // layer moves continuously instead of 1px juddering).
        float tx = IsBaked || Box.IsSmoothScrollingX ? sx : MathF.Round(sx * scale) / scale;
        float ty = IsBaked || Box.IsSmoothScrollingY ? sy : MathF.Round(sy * scale) / scale;
        if (tx != 0 || ty != 0)
            canvas.Translate(-tx, -ty);
        // The layer is a DEVICE-resolution bitmap (content × scale); draw it 1:1
        // aligned to the device grid so no sub-pixel sampling smears the content.
        // The layer was baked with its translate SNAPPED to the device grid, so
        // image texel u holds content at round(ContentBox.Left*scale)/scale + u/scale;
        // draw texel 0 at that same snapped logical position so every baked pixel
        // maps to a whole device pixel (crisp, aligned with the page ops).
        float left = SnapToDevice(ContentBox.Left, scale);
        float top = SnapToDevice(ContentBox.Top, scale);
        canvas.DrawImage(Image,
            new SKRect(left, top,
                left + Image.Width / scale,
                top + Image.Height / scale),
            new SKSamplingOptions(SKFilterMode.Linear), null);
        canvas.Restore();

        DrawScrollbar(canvas);
    }

    private void DrawScrollbar(SKCanvas canvas)
    {
        if (!ShowVertical && !ShowHorizontal) return;
        using var trackPaint = new SKPaint { Color = TrackColor, Style = SKPaintStyle.Fill };
        using var thumbPaint = new SKPaint { Color = ThumbColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        float thickness = ScrollbarThickness;

        float vRange = Math.Max(1f, Box.ScrollContentHeight - Box.ContentBox.Height);
        float hRange = Math.Max(1f, Box.ScrollContentWidth - Box.ContentBox.Width);

        if (ShowVertical)
        {
            float trackX = PaddingBox.Right - thickness;
            float trackY = PaddingBox.Top;
            float trackH = Math.Max(0f, PaddingBox.Height - (ShowHorizontal ? thickness : 0f));
            canvas.DrawRect(new SKRect(trackX, trackY, PaddingBox.Right, trackY + trackH), trackPaint);
            if (trackH > 0)
            {
                float ratio = Box.ContentBox.Height / Math.Max(1f, Box.ScrollContentHeight);
                float thumbH = Math.Min(trackH, Math.Max(20f, trackH * Math.Min(1f, ratio)));
                float pos = Math.Clamp(Box.ScrollY, 0f, vRange);
                float thumbY = trackY + (trackH - thumbH) * (pos / vRange);
                canvas.DrawRoundRect(new SKRect(trackX + 2, thumbY + 1, PaddingBox.Right - 2, thumbY + thumbH - 1), ThumbRadius, ThumbRadius, thumbPaint);
            }
        }

        if (ShowHorizontal)
        {
            float trackX = PaddingBox.Left;
            float trackY = PaddingBox.Bottom - thickness;
            float trackW = Math.Max(0f, PaddingBox.Width - (ShowVertical ? thickness : 0f));
            canvas.DrawRect(new SKRect(trackX, trackY, trackX + trackW, PaddingBox.Bottom), trackPaint);
            if (trackW > 0)
            {
                float ratio = Box.ContentBox.Width / Math.Max(1f, Box.ScrollContentWidth);
                float thumbW = Math.Min(trackW, Math.Max(20f, trackW * Math.Min(1f, ratio)));
                float pos = Math.Clamp(Box.ScrollX, 0f, hRange);
                float thumbX = trackX + (trackW - thumbW) * (pos / hRange);
                canvas.DrawRoundRect(new SKRect(thumbX + 1, trackY + 2, thumbX + thumbW - 1, PaddingBox.Bottom - 2), ThumbRadius, ThumbRadius, thumbPaint);
            }
        }

        if (ShowVertical && ShowHorizontal)
        {
            // Scrollbar corner where the two tracks meet (matches ScrollableAreaPainter).
            canvas.DrawRect(new SKRect(
                PaddingBox.Right - thickness,
                PaddingBox.Bottom - thickness,
                PaddingBox.Right,
                PaddingBox.Bottom), trackPaint);
        }
    }
}

public static class PaintOpPool
{
    private const int MaxPoolSize = 500;
    private static readonly ConcurrentStack<DrawRectOp> _rectOps = new();
    private static readonly ConcurrentStack<DrawTextOp> _textOps = new();
    private static readonly ConcurrentStack<DrawImageOp> _imageOps = new();
    private static readonly ConcurrentStack<DrawLineOp> _lineOps = new();
    private static readonly ConcurrentStack<DrawPathOp> _pathOps = new();
    private static readonly ConcurrentStack<PushClipOp> _clipOps = new();
    private static readonly ConcurrentStack<PopClipOp> _popClipOps = new();
    private static readonly ConcurrentStack<PushTransformOp> _transformOps = new();
    private static readonly ConcurrentStack<PopTransformOp> _popTransformOps = new();
    private static readonly ConcurrentStack<DrawShadowOp> _shadowOps = new();
    private static readonly ConcurrentStack<PushLayerOp> _layerOps = new();
    private static readonly ConcurrentStack<PopLayerOp> _popLayerOps = new();
    private static readonly ConcurrentStack<MaskApplyOp> _maskApplyOps = new();

    public static DrawRectOp GetDrawRectOp() => _rectOps.TryPop(out var op) ? op : new DrawRectOp();
    public static DrawTextOp GetDrawTextOp() => _textOps.TryPop(out var op) ? op : new DrawTextOp();
    public static DrawImageOp GetDrawImageOp() => _imageOps.TryPop(out var op) ? op : new DrawImageOp();
    public static DrawLineOp GetDrawLineOp() => _lineOps.TryPop(out var op) ? op : new DrawLineOp();
    public static DrawPathOp GetDrawPathOp() => _pathOps.TryPop(out var op) ? op : new DrawPathOp();
    public static PushClipOp GetPushClipOp() => _clipOps.TryPop(out var op) ? op : new PushClipOp();
    public static PopClipOp GetPopClipOp() => _popClipOps.TryPop(out var op) ? op : new PopClipOp();
    public static PushTransformOp GetPushTransformOp() => _transformOps.TryPop(out var op) ? op : new PushTransformOp();
    public static PopTransformOp GetPopTransformOp() => _popTransformOps.TryPop(out var op) ? op : new PopTransformOp();
    public static DrawShadowOp GetDrawShadowOp() => _shadowOps.TryPop(out var op) ? op : new DrawShadowOp();
    public static PushLayerOp GetPushLayerOp() => _layerOps.TryPop(out var op) ? op : new PushLayerOp();
    public static PopLayerOp GetPopLayerOp() => _popLayerOps.TryPop(out var op) ? op : new PopLayerOp();

    public static MaskApplyOp GetMaskApplyOp() => _maskApplyOps.TryPop(out var op) ? op : new MaskApplyOp();

    public static void Return(DrawRectOp op) { op.Reset(); if (_rectOps.Count < MaxPoolSize) _rectOps.Push(op); }
    public static void Return(DrawTextOp op) { op.Reset(); if (_textOps.Count < MaxPoolSize) _textOps.Push(op); }
    public static void Return(DrawImageOp op) { op.Reset(); if (_imageOps.Count < MaxPoolSize) _imageOps.Push(op); }
    public static void Return(DrawLineOp op) { op.Reset(); if (_lineOps.Count < MaxPoolSize) _lineOps.Push(op); }
    public static void Return(DrawPathOp op) { op.Reset(); if (_pathOps.Count < MaxPoolSize) _pathOps.Push(op); }
    public static void Return(PushClipOp op) { op.Reset(); if (_clipOps.Count < MaxPoolSize) _clipOps.Push(op); }
    public static void Return(PopClipOp op) { op.Reset(); if (_popClipOps.Count < MaxPoolSize) _popClipOps.Push(op); }
    public static void Return(PushTransformOp op) { op.Reset(); if (_transformOps.Count < MaxPoolSize) _transformOps.Push(op); }
    public static void Return(PopTransformOp op) { op.Reset(); if (_popTransformOps.Count < MaxPoolSize) _popTransformOps.Push(op); }
    public static void Return(DrawShadowOp op) { op.Reset(); if (_shadowOps.Count < MaxPoolSize) _shadowOps.Push(op); }
    public static void Return(PushLayerOp op) { op.Reset(); if (_layerOps.Count < MaxPoolSize) _layerOps.Push(op); }
    public static void Return(PopLayerOp op) { op.Reset(); if (_popLayerOps.Count < MaxPoolSize) _popLayerOps.Push(op); }
    public static void Return(MaskApplyOp op) { op.Reset(); if (_maskApplyOps.Count < MaxPoolSize) _maskApplyOps.Push(op); }

    public static void ReturnOp(PaintOp op)
    {
        switch (op)
        {
            case DrawRectOp o: Return(o); break;
            case DrawTextOp o: Return(o); break;
            case DrawImageOp o: Return(o); break;
            case DrawLineOp o: Return(o); break;
            case DrawPathOp o: Return(o); break;
            case PushClipOp o: Return(o); break;
            case PopClipOp o: Return(o); break;
            case PushTransformOp o: Return(o); break;
            case PopTransformOp o: Return(o); break;
            case DrawShadowOp o: Return(o); break;
            case PushLayerOp o: Return(o); break;
            case PopLayerOp o: Return(o); break;
            case MaskApplyOp o: Return(o); break;
        }
    }

    public static void Clear()
    {
        _rectOps.Clear();
        _textOps.Clear();
        _imageOps.Clear();
        _lineOps.Clear();
        _pathOps.Clear();
        _clipOps.Clear();
        _popClipOps.Clear();
        _transformOps.Clear();
        _popTransformOps.Clear();
        _shadowOps.Clear();
        _layerOps.Clear();
        _popLayerOps.Clear();
        _maskApplyOps.Clear();
        DrawShadowOp.ClearBlurCache();
    }
}

/// <summary>
/// Spatial grid index for fast region-based paint op lookups.
/// </summary>
public class SpatialGrid
{
    private readonly float _cellSize;
    private readonly Dictionary<(int, int), List<PaintOp>> _grid = new();
    private int _opCount;

    public SpatialGrid(float cellSize = 100)
    {
        _cellSize = cellSize;
    }

    public void Build(IReadOnlyList<PaintOp> ops)
    {
        _grid.Clear();
        _opCount = ops.Count;

        foreach (var op in ops)
        {
            var cells = GetCellsForRect(op.Bounds);
            foreach (var cell in cells)
            {
                if (!_grid.TryGetValue(cell, out var list))
                {
                    list = new List<PaintOp>();
                    _grid[cell] = list;
                }
                list.Add(op);
            }
        }
    }

    public IEnumerable<PaintOp> GetOpsInRect(SKRect rect)
    {
        if (_opCount == 0) yield break;

        var seen = new HashSet<PaintOp>();
        var cells = GetCellsForRect(rect);
        foreach (var cell in cells)
        {
            if (_grid.TryGetValue(cell, out var ops))
            {
                foreach (var op in ops)
                {
                    if (seen.Add(op) && op.Bounds.IntersectsWith(rect))
                        yield return op;
                }
            }
        }
    }

    public void Clear()
    {
        _grid.Clear();
        _opCount = 0;
    }

    private List<(int, int)> GetCellsForRect(SKRect rect)
    {
        var cells = new List<(int, int)>(4);
        int minX = (int)(rect.Left / _cellSize);
        int maxX = (int)(rect.Right / _cellSize);
        int minY = (int)(rect.Top / _cellSize);
        int maxY = (int)(rect.Bottom / _cellSize);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                cells.Add((x, y));
        return cells;
    }
}

public class DisplayList
{
    private readonly object _lock = new();
    private List<PaintOp> _ops = new();
    private bool _isSorted;
    private SpatialGrid? _spatialGrid;

    public SpatialGrid? SpatialGrid => _spatialGrid;

    public void Add(PaintOp op) { lock (_lock) _ops.Add(op); }

    /// <summary>Inspect the op list (ordered as stored).</summary>
    public IEnumerable<PaintOp> EnumerateOps()
    {
        List<PaintOp> snapshot;
        lock (_lock) snapshot = new List<PaintOp>(_ops);
        return snapshot;
    }

    /// <summary>
    /// Execute the list under one lock, skipping cullable draw ops whose Bounds
    /// miss <paramref name="docWindow"/> (document-space rect of what the canvas
    /// will actually show, padding included). Zero per-op allocation — used by
    /// the multi-process rasterizer where a full op walk every scroll tick was
    /// the frame-time bottleneck.
    /// </summary>
    public void ExecuteCulled(SKCanvas canvas, SKRect docWindow)
    {
        lock (_lock)
        {
            for (int i = 0; i < _ops.Count; i++)
            {
                var op = _ops[i];
                if (op.Cullable && !op.Bounds.IsEmpty && !docWindow.IntersectsWith(op.Bounds))
                    continue;
                op.Execute(canvas);
            }
        }
    }

    /// <summary>
    /// Non-copying ordered walk (snapshot copies of a 20k-op list at frame rate
    /// were pure GC churn).
    /// </summary>
    public void ForEachOp(Action<PaintOp> visit)
    {
        lock (_lock)
        {
            for (int i = 0; i < _ops.Count; i++) visit(_ops[i]);
        }
    }

    public void AddRange(IEnumerable<PaintOp> ops) { lock (_lock) _ops.AddRange(ops); }

    public void Clear()
    {
        List<PaintOp> oldOps;
        lock (_lock)
        {
            oldOps = _ops;
            _ops = new List<PaintOp>();
            _isSorted = false;
        }
        for (int i = 0; i < oldOps.Count; i++)
        {
            PaintOpPool.ReturnOp(oldOps[i]);
        }
        _spatialGrid?.Clear();
    }

    public void SortByZIndex()
    {
        lock (_lock)
        {
            if (_isSorted) return;
            // Use stable sort (OrderBy) so equal-ZIndex ops keep their original order.
            // List<T>.Sort is unstable and can reorder background/foreground ops.
            _ops = _ops.OrderBy(op => op.ZIndex).ToList();
            _isSorted = true;
        }
    }

    public void Execute(SKCanvas canvas)
    {
        List<PaintOp> snapshot;
        lock (_lock) snapshot = new List<PaintOp>(_ops);
        for (int i = 0; i < snapshot.Count; i++)
        {
            try
            {
                snapshot[i].AlignBounds(canvas);
            }
            catch
            {
                // ignore alignment errors
            }
            snapshot[i].Execute(canvas);
        }
    }

    /// <summary>
    /// Execute only the ops whose bounds intersect <paramref name="cull"/>.
    /// Op <see cref="PaintOp.Execute"/> is not free (font/text shaping, image
    /// alignment, paint setup), so skipping clipped-out ops is what keeps a
    /// per-frame re-record (element scroll) O(changed region) instead of O(page).
    /// </summary>
    public void Execute(SKCanvas canvas, SKRect cull, bool skipScrollLayerOps = false)
    {
        if (cull.IsEmpty)
        {
            Execute(canvas);
            return;
        }
        List<PaintOp> snapshot;
        lock (_lock) snapshot = new List<PaintOp>(_ops);
        for (int i = 0; i < snapshot.Count; i++)
        {
            var op = snapshot[i];
            // Layered scroll containers are composited LIVE by the tile compositor
            // (DrawLiveScrollLayers), so the recorded picture leaves a hole where
            // they are; replaying the op here would double-draw the layer.
            if (skipScrollLayerOps && op is DrawScrollLayerOp)
                continue;
            if (!op.Bounds.IntersectsWith(cull))
                continue;
            try
            {
                op.AlignBounds(canvas);
            }
            catch
            {
                // ignore alignment errors
            }
            op.Execute(canvas);
        }
    }

    /// <summary>
    /// Remove every op whose bounds are fully contained in <paramref name="region"/>
    /// (returned to the pool). Used by the element-scroll fast path to drop a scrolled
    /// container's stale ops before its subtree is re-painted at the new offset.
    /// Structural state ops (clips / transforms / layers) are KEPT: removing a clip
    /// while its governed content stays in the list would rasterize that content
    /// unclipped — the "content floats out of the scroller" artifact.
    /// </summary>
    public void RemoveOpsContainedIn(SKRect region)
    {
        lock (_lock)
        {
            List<PaintOp> kept = new(_ops.Count);
            for (int i = 0; i < _ops.Count; i++)
            {
                var op = _ops[i];
                if (op is PushClipOp or PopClipOp or PushTransformOp or PopTransformOp
                    or PushLayerOp or PopLayerOp)
                {
                    kept.Add(op);
                    continue;
                }
                if (!op.Bounds.IsEmpty
                    && op.Bounds.Left >= region.Left && op.Bounds.Right <= region.Right
                    && op.Bounds.Top >= region.Top && op.Bounds.Bottom <= region.Bottom)
                {
                    PaintOpPool.ReturnOp(op);
                    continue;
                }
                kept.Add(op);
            }
            if (kept.Count != _ops.Count)
            {
                _ops = kept;
                _isSorted = false;
                _spatialGrid?.Clear();
            }
        }
    }

    public IEnumerable<PaintOp> GetOpsInRect(SKRect rect)
    {
        if (_spatialGrid != null && _ops.Count > 100)
        {
            foreach (var op in _spatialGrid.GetOpsInRect(rect))
                yield return op;
            yield break;
        }
        List<PaintOp> snapshot;
        lock (_lock) snapshot = new List<PaintOp>(_ops);
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i].Bounds.IntersectsWith(rect))
                yield return snapshot[i];
        }
    }

    public void BuildSpatialGrid()
    {
        List<PaintOp> snapshot;
        lock (_lock) snapshot = new List<PaintOp>(_ops);
        if (snapshot.Count <= 100) return;
        _spatialGrid ??= new SpatialGrid();
        _spatialGrid.Build(snapshot);
    }

    public int Count { get { lock (_lock) return _ops.Count; } }

    public PaintOp? this[int index] { get { lock (_lock) return index >= 0 && index < _ops.Count ? _ops[index] : null; } }
}
