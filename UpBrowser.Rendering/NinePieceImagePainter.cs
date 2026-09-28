using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Performance;

namespace UpBrowser.Rendering;

/// <summary>
/// Renders border-image by splitting the source image into 9 pieces and
/// mapping them onto the border area. Mirrors nine_piece_image_painter.cc
/// and nine_piece_image_grid.cc.
/// </summary>
internal static class NinePieceImagePainter
{
    public static bool HasBorderImage(ComputedStyle style) =>
        !string.IsNullOrEmpty(style.BorderImageSource) && style.BorderImageSource != "none";

    /// <summary>True for the generated image types that take the size of their box.</summary>
    private static bool IsGeneratedImage(string source) =>
        source.Contains("gradient", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Paint the border image. Returns false when the source cannot be produced, so the
    /// caller can fall back to the plain border instead of losing the decoration.
    /// </summary>
    public static bool Paint(DisplayList displayList, ImageCache imageCache, ComputedStyle style, SKRect borderRect, string? baseUrl = null)
    {
        var source = style.BorderImageSource;
        if (string.IsNullOrEmpty(source) || source == "none") return false;

        // The outset grows the border box into the border-image area; a generated image
        // (a gradient) has no intrinsic size, so that area *is* its natural size.
        var outset = ParseBoxValues(style.BorderImageOutset, 0f, 0f);
        var destRect = new SKRect(
            borderRect.Left - outset.left,
            borderRect.Top - outset.top,
            borderRect.Right + outset.right,
            borderRect.Bottom + outset.bottom);

        SKImage? image;
        if (IsGeneratedImage(source))
        {
            image = GradientRenderer.RasterizeToImage(source, destRect);
        }
        else
        {
            var url = ParseUrl(source);
            if (url == null) return false;
            url = UrlResolver.Resolve(url, baseUrl);
            if (url == null) return false;
            var task = imageCache.GetImageAsync(url);
            task.Wait();
            image = task.Result;
        }
        if (image == null) return false;

        float imgW = image.Width;
        float imgH = image.Height;
        if (imgW <= 0 || imgH <= 0) return false;

        // Parse border-image-slice (numbers are px, % of image edge-to-edge)
        var sliceSides = ParseBoxStrings(style.BorderImageSlice, "100%");
        float sliceTop = ResolveSlice(sliceSides.top, imgH);
        float sliceRight = ResolveSlice(sliceSides.right, imgW);
        float sliceBottom = ResolveSlice(sliceSides.bottom, imgH);
        float sliceLeft = ResolveSlice(sliceSides.left, imgW);

        // Clamp slices to image dimensions
        sliceTop = Math.Clamp(sliceTop, 0, imgH);
        sliceRight = Math.Clamp(sliceRight, 0, imgW);
        sliceBottom = Math.Clamp(sliceBottom, 0, imgH);
        sliceLeft = Math.Clamp(sliceLeft, 0, imgW);

        // Parse border-image-width: auto -> border width, number -> x border
        // width, % -> of the border-box extent, px -> pixels.
        var widthSides = ParseBoxStrings(style.BorderImageWidth, "auto");
        float bwTop = ResolveWidthSide(widthSides.top, style.BorderTopWidth, borderRect.Height);
        float bwRight = ResolveWidthSide(widthSides.right, style.BorderRightWidth, borderRect.Width);
        float bwBottom = ResolveWidthSide(widthSides.bottom, style.BorderBottomWidth, borderRect.Height);
        float bwLeft = ResolveWidthSide(widthSides.left, style.BorderLeftWidth, borderRect.Width);

        // Parse border-image-repeat
        var repeat = ParseRepeat(style.BorderImageRepeat);
        bool fill = style.BorderImageSlice.Contains("fill");

        // Compute source rects for the 9 pieces
        // Corners (source)
        var srcTL = new SKRect(0, 0, sliceLeft, sliceTop);
        var srcTR = new SKRect(imgW - sliceRight, 0, imgW, sliceTop);
        var srcBL = new SKRect(0, imgH - sliceBottom, sliceLeft, imgH);
        var srcBR = new SKRect(imgW - sliceRight, imgH - sliceBottom, imgW, imgH);
        // Edges (source)
        var srcTop = new SKRect(sliceLeft, 0, imgW - sliceRight, sliceTop);
        var srcBottom = new SKRect(sliceLeft, imgH - sliceBottom, imgW - sliceRight, imgH);
        var srcLeft = new SKRect(0, sliceTop, sliceLeft, imgH - sliceBottom);
        var srcRight = new SKRect(imgW - sliceRight, sliceTop, imgW, imgH - sliceBottom);
        // Middle (source)
        var srcMiddle = new SKRect(sliceLeft, sliceTop, imgW - sliceRight, imgH - sliceBottom);

        // Compute destination rects for the 9 pieces
        // Corners (destination)
        var dstTL = new SKRect(destRect.Left, destRect.Top, destRect.Left + bwLeft, destRect.Top + bwTop);
        var dstTR = new SKRect(destRect.Right - bwRight, destRect.Top, destRect.Right, destRect.Top + bwTop);
        var dstBL = new SKRect(destRect.Left, destRect.Bottom - bwBottom, destRect.Left + bwLeft, destRect.Bottom);
        var dstBR = new SKRect(destRect.Right - bwRight, destRect.Bottom - bwBottom, destRect.Right, destRect.Bottom);
        // Edges (destination)
        var dstTop = new SKRect(destRect.Left + bwLeft, destRect.Top, destRect.Right - bwRight, destRect.Top + bwTop);
        var dstBottom = new SKRect(destRect.Left + bwLeft, destRect.Bottom - bwBottom, destRect.Right - bwRight, destRect.Bottom);
        var dstLeft = new SKRect(destRect.Left, destRect.Top + bwTop, destRect.Left + bwLeft, destRect.Bottom - bwBottom);
        var dstRight = new SKRect(destRect.Right - bwRight, destRect.Top + bwTop, destRect.Right, destRect.Bottom - bwBottom);
        // Middle (destination)
        var dstMiddle = new SKRect(destRect.Left + bwLeft, destRect.Top + bwTop, destRect.Right - bwRight, destRect.Bottom - bwBottom);

        // Paint corners (always stretched, no tiling)
        PaintImagePiece(displayList, image, srcTL, dstTL, ImageFit.Fill);
        PaintImagePiece(displayList, image, srcTR, dstTR, ImageFit.Fill);
        PaintImagePiece(displayList, image, srcBL, dstBL, ImageFit.Fill);
        PaintImagePiece(displayList, image, srcBR, dstBR, ImageFit.Fill);

        // Paint edges
        PaintEdgePiece(displayList, image, srcTop, dstTop, repeat.horizontal, true);
        PaintEdgePiece(displayList, image, srcBottom, dstBottom, repeat.horizontal, true);
        PaintEdgePiece(displayList, image, srcLeft, dstLeft, repeat.vertical, false);
        PaintEdgePiece(displayList, image, srcRight, dstRight, repeat.vertical, false);

        // Paint middle (only if fill is specified)
        if (fill && srcMiddle.Width > 0 && srcMiddle.Height > 0 && dstMiddle.Width > 0 && dstMiddle.Height > 0)
        {
            PaintImagePiece(displayList, image, srcMiddle, dstMiddle, ImageFit.Fill);
        }
        return true;
    }

    /// <summary>
    /// Paint one edge piece with its repeat mode. 'stretch' scales the slice to the edge,
    /// the other modes tile it along the edge axis (CSS Backgrounds 3 4.4).
    /// </summary>
    private static void PaintEdgePiece(DisplayList displayList, SKImage image, SKRect src, SKRect dst, string mode, bool alongX)
    {
        if (src.Width <= 0 || src.Height <= 0 || dst.Width <= 0 || dst.Height <= 0) return;
        if (mode is "stretch" or "")
        {
            PaintImagePiece(displayList, image, src, dst, ImageFit.Fill);
            return;
        }

        float srcExtent = alongX ? src.Width : src.Height;
        float dstExtent = alongX ? dst.Width : dst.Height;
        if (srcExtent <= 0 || dstExtent <= 0) return;

        int count;
        float gap;
        float tileExtent;
        switch (mode)
        {
            case "round":
                // Whole tiles, each scaled by the same factor so they fill the edge exactly.
                count = Math.Max(1, (int)MathF.Round(dstExtent / srcExtent));
                gap = 0;
                tileExtent = dstExtent / count;
                break;
            case "space":
            {
                // Whole tiles at natural size, evenly spaced; a single tile is centered.
                count = Math.Max(1, (int)MathF.Floor(dstExtent / srcExtent));
                gap = (dstExtent - count * srcExtent) / (count + 1);
                tileExtent = srcExtent;
                break;
            }
            default:
                // 'repeat': tiles at natural size, the last one clipped to the edge.
                count = Math.Max(1, (int)MathF.Ceiling(dstExtent / srcExtent));
                gap = 0;
                tileExtent = srcExtent;
                break;
        }

        float position = 0;
        float remaining = dstExtent;
        for (int i = 0; i < count && remaining > 0; i++)
        {
            position += gap;
            float drawn = Math.Min(tileExtent, remaining);
            SKRect pieceSrc = src, pieceDst = dst;
            if (alongX)
            {
                float scale = drawn / tileExtent;
                pieceSrc = new SKRect(src.Left, src.Top, src.Left + src.Width * scale, src.Bottom);
                pieceDst = new SKRect(dst.Left + position, dst.Top, dst.Left + position + drawn, dst.Bottom);
            }
            else
            {
                float scale = drawn / tileExtent;
                pieceSrc = new SKRect(src.Left, src.Top, src.Right, src.Top + src.Height * scale);
                pieceDst = new SKRect(dst.Left, dst.Top + position, dst.Right, dst.Top + position + drawn);
            }
            PaintImagePiece(displayList, image, pieceSrc, pieceDst, ImageFit.Fill);
            position += drawn;
            remaining -= drawn;
        }
    }

    private static void PaintImagePiece(DisplayList displayList, SKImage image, SKRect src, SKRect dst, ImageFit fit)
    {
        if (src.Width <= 0 || src.Height <= 0 || dst.Width <= 0 || dst.Height <= 0) return;

        var op = PaintOpPool.GetDrawImageOp();
        op.Image = image;
        op.SourceRect = src;
        op.DestRect = dst;
        op.Fit = fit;
        op.Bounds = dst;
        displayList.Add(op);
    }

    private static string? ParseUrl(string source)
    {
        if (string.IsNullOrEmpty(source)) return null;
        if (source.StartsWith("url("))
            return source[4..^1].Trim('\'', '"');
        return source;
    }

    private static (float top, float right, float bottom, float left) ParseBoxValues(string value, float defaultX, float defaultY)
    {
        if (string.IsNullOrEmpty(value)) return (defaultY, defaultX, defaultY, defaultX);

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float v1 = ParseValue(parts.Length > 0 ? parts[0] : null, defaultY);
        float v2 = ParseValue(parts.Length > 1 ? parts[1] : null, defaultX);
        float v3 = ParseValue(parts.Length > 2 ? parts[2] : null, defaultY);
        float v4 = ParseValue(parts.Length > 3 ? parts[3] : null, defaultX);

        if (parts.Length == 1) return (v1, v1, v1, v1);
        if (parts.Length == 2) return (v1, v2, v1, v2);
        if (parts.Length == 3) return (v1, v2, v3, v2);
        return (v1, v2, v3, v4);
    }

    /// <summary>
    /// Expands a 1-to-4 side value list into top/right/bottom/left strings
    /// without resolving units, so each side keeps its own unit semantics.
    /// </summary>
    private static (string top, string right, string bottom, string left) ParseBoxStrings(string value, string defaultSide)
    {
        if (string.IsNullOrEmpty(value)) return (defaultSide, defaultSide, defaultSide, defaultSide);
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string p1 = parts.Length > 0 ? parts[0] : defaultSide;
        string p2 = parts.Length > 1 ? parts[1] : p1;
        string p3 = parts.Length > 2 ? parts[2] : p1;
        string p4 = parts.Length > 3 ? parts[3] : p2;
        return (p1, p2, p3, p4);
    }

    private static float ParseValue(string? s, float defaultValue)
    {
        if (string.IsNullOrEmpty(s)) return defaultValue;
        if (s.EndsWith("%") && float.TryParse(s[..^1], out var pct)) return pct;
        if (float.TryParse(s.Replace("px", ""), out var v)) return v;
        return defaultValue;
    }

    private enum MeasureKind { Auto, Number, Percent, Length }

    private static (MeasureKind Kind, float Value) ParseMeasure(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return (MeasureKind.Auto, 0);
        if (s.EndsWith("%") && float.TryParse(s[..^1], out var pct))
            return (MeasureKind.Percent, pct);
        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase) && s.Length > 2 && float.TryParse(s[..^2], out var px))
            return (MeasureKind.Length, px);
        if (float.TryParse(s, out var num))
            return (MeasureKind.Number, num);
        return (MeasureKind.Auto, 0);
    }

    /// <summary>border-image-slice: % of the image extent, bare numbers/px are pixels.</summary>
    private static float ResolveSlice(string side, float imageExtent)
    {
        var (kind, value) = ParseMeasure(side);
        return kind switch
        {
            MeasureKind.Percent => value / 100f * imageExtent,
            MeasureKind.Auto => imageExtent,
            _ => value,
        };
    }

    /// <summary>border-image-width: auto/border width, number x border width, % of border-box extent, px pixels.</summary>
    private static float ResolveWidthSide(string side, float borderWidth, float extent)
    {
        var (kind, value) = ParseMeasure(side);
        return kind switch
        {
            MeasureKind.Auto => borderWidth,
            MeasureKind.Number => value * borderWidth,
            MeasureKind.Percent => value / 100f * extent,
            _ => value,
        };
    }

    private static (string horizontal, string vertical) ParseRepeat(string repeat)
    {
        if (string.IsNullOrEmpty(repeat)) return ("stretch", "stretch");
        var parts = repeat.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var h = parts.Length > 0 ? parts[0].ToLowerInvariant() : "stretch";
        var v = parts.Length > 1 ? parts[1].ToLowerInvariant() : h;
        return (h, v);
    }
}