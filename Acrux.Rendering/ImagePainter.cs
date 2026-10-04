using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Rendering;

internal sealed class ImagePainter
{
    private readonly DisplayList _displayList;
    private readonly ImageCache _imageCache;
    private readonly string? _baseUrl;
    private readonly HashSet<string> _lazyLoading = new();

    public ImagePainter(DisplayList displayList, ImageCache imageCache, string? baseUrl)
    {
        _displayList = displayList;
        _imageCache = imageCache;
        _baseUrl = baseUrl;
    }

    public void PaintImage(Element element, ComputedStyle style, LayoutBox box)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrEmpty(src))
        {
            PaintAltText(element, style, box);
            PaintMissingImagePlaceholder(element, style, box);
            return;
        }

        var loading = element.GetAttribute("loading");
        if (loading != null && loading.Equals("lazy", StringComparison.OrdinalIgnoreCase))
        {
            if (_lazyLoading.Add(src))
            {
                _ = LoadImageAsync(element, src, style, box);
                return;
            }
        }

        var resolvedSrc = ResolveImageUrl(src);
        if (resolvedSrc == null)
        {
            PaintAltText(element, style, box);
            PaintMissingImagePlaceholder(element, style, box);
            return;
        }

        var task = _imageCache.GetImageAsync(resolvedSrc);
        task.Wait();
        var image = task.Result;
        if (image == null)
        {
            PaintAltText(element, style, box);
            PaintMissingImagePlaceholder(element, style, box);
            return;
        }

        var (objectRect, objectSrc) = ComputeObjectRects(style, box.ContentBox, image.Width, image.Height);
        if (objectRect.Width <= 0 || objectRect.Height <= 0)
            return;

        var op = PaintOpPool.GetDrawImageOp();
        op.Image = image;
        op.SourceRect = objectSrc;
        op.DestRect = objectRect;
        // The concrete object size is already resolved above, so the op maps
        // source -> dest 1:1 (this also carries the object-fit clipping).
        op.Fit = ImageFit.Fill;
        op.ZIndex = style.ZIndex ?? 0;
        op.Bounds = objectRect;
        _displayList.Add(op);
    }

    private async Task LoadImageAsync(Element element, string src, ComputedStyle style, LayoutBox box)
    {
        var resolvedSrc = ResolveImageUrl(src);
        if (resolvedSrc == null) return;
        await _imageCache.GetImageAsync(resolvedSrc);
    }

    /// <summary>
    /// Resolve the concrete object size for <paramref name="intrinsicW"/>×
    /// <paramref name="intrinsicH"/> content inside |contentBox| honoring
    /// object-fit and object-position (css-images-3 §4.1..§4.3). Returns the
    /// destination rect (already clipped to the content box) together with the
    /// matching sub-rect of the source, so cover/none crop instead of overflow.
    /// </summary>
    internal static (SKRect Dest, SKRect Src) ComputeObjectRects(
        ComputedStyle style, SKRect contentBox, float intrinsicW, float intrinsicH)
    {
        var fullSrc = new SKRect(0, 0, Math.Max(1, intrinsicW), Math.Max(1, intrinsicH));
        if (intrinsicW <= 0 || intrinsicH <= 0 || contentBox.Width <= 0 || contentBox.Height <= 0)
            return (contentBox, fullSrc);

        float scaleX, scaleY;
        switch (style.ObjectFit)
        {
            case ObjectFitType.Contain:
                scaleX = scaleY = Math.Min(contentBox.Width / intrinsicW, contentBox.Height / intrinsicH);
                break;
            case ObjectFitType.Cover:
                scaleX = scaleY = Math.Max(contentBox.Width / intrinsicW, contentBox.Height / intrinsicH);
                break;
            case ObjectFitType.None:
                scaleX = scaleY = 1f;
                break;
            case ObjectFitType.ScaleDown:
                scaleX = scaleY = Math.Min(1f, Math.Min(contentBox.Width / intrinsicW, contentBox.Height / intrinsicH));
                break;
            default: // fill: stretch both axes independently
                scaleX = contentBox.Width / intrinsicW;
                scaleY = contentBox.Height / intrinsicH;
                break;
        }

        float fitW = intrinsicW * scaleX;
        float fitH = intrinsicH * scaleY;
        float offX = ObjectPositionOffset(style.ObjectPositionX, contentBox.Width, fitW, style.FontSize);
        float offY = ObjectPositionOffset(style.ObjectPositionY, contentBox.Height, fitH, style.FontSize);

        var scaled = new SKRect(contentBox.Left + offX, contentBox.Top + offY,
            contentBox.Left + offX + fitW, contentBox.Top + offY + fitH);
        var dest = SKRect.Intersect(scaled, contentBox);
        if (dest.Width <= 0 || dest.Height <= 0)
            return (SKRect.Empty, SKRect.Empty);

        var src = new SKRect(
            (dest.Left - scaled.Left) / scaleX,
            (dest.Top - scaled.Top) / scaleY,
            (dest.Right - scaled.Left) / scaleX,
            (dest.Bottom - scaled.Top) / scaleY);
        return (dest, src);
    }

    private static float ObjectPositionOffset(Length? position, float boxSize, float imageSize, float fontSize)
    {
        float free = boxSize - imageSize;
        if (position == null)
            return free * 0.5f;
        if (position is PercentLength percent)
            return free * percent.Value;
        float px = position.ToPixels(fontSize, fontSize, 0f, 0f);
        return float.IsNaN(px) ? free * 0.5f : px;
    }

    private void PaintAltText(Element element, ComputedStyle style, LayoutBox box)
    {
        var rect = box.ContentBox;
        if (rect.Width <= 2 || rect.Height <= 2) return;

        var alt = element.GetAttribute("alt");
        if (!string.IsNullOrEmpty(alt))
        {
            var op = PaintOpPool.GetDrawTextOp();
            op.Text = alt;
            op.X = rect.Left + 4;
            op.Y = rect.Top + style.FontSize;
            op.Color = style.Color;
            op.FontSize = style.FontSize;
            op.FontFamily = style.FontFamily ?? "Arial";
            op.Bounds = rect;
            _displayList.Add(op);
        }
    }

    private void PaintMissingImagePlaceholder(Element element, ComputedStyle style, LayoutBox box)
    {
        // Per css-images the alt content / broken-image indicator is laid out in
        // the content box; object-fit only scales the image itself.
        var rect = box.ContentBox;
        if (rect.Width <= 2 || rect.Height <= 2) return;

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = rect;
        op.FillColor = SKColors.Transparent;
        op.BorderTopWidth = 1;
        op.BorderRightWidth = 1;
        op.BorderBottomWidth = 1;
        op.BorderLeftWidth = 1;
        op.BorderTopColor = new SKColor(0xCC, 0xCC, 0xCC, 0xFF);
        op.BorderRightColor = new SKColor(0xCC, 0xCC, 0xCC, 0xFF);
        op.BorderBottomColor = new SKColor(0xCC, 0xCC, 0xCC, 0xFF);
        op.BorderLeftColor = new SKColor(0xCC, 0xCC, 0xCC, 0xFF);
        op.Bounds = rect;
        _displayList.Add(op);
    }

    private string? ResolveImageUrl(string url) => UrlResolver.Resolve(url, _baseUrl);
}