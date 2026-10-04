using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Html;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Dispatcher for replaced-element content painting. Mirrors the role of
/// replaced_painter.cc: once the element's background/border phases have run,
/// the *content* of a replaced box is painted here 鈥?respecting the content-box
/// geometry produced by layout and the element kind.
///
/// Routing:
///   IMG     鈫?<see cref="ImagePainter"/> (existing path, unchanged)
///   VIDEO   鈫?<see cref="VideoPainter"/>        (poster / current frame / UA placeholder)
///   CANVAS  鈫?<see cref="HtmlCanvasPainter"/>   (backing store when present)
///   OBJECT/
///   EMBED   鈫?<see cref="EmbeddedObjectPainter"/>
///
/// Returns true when the tag is a handled replaced kind so the caller can skip
/// its generic inline/text painting for that box.
/// </summary>
internal sealed class ReplacedPainter
{
    private readonly DisplayList _displayList;
    private readonly ImageCache _imageCache;
    private readonly string? _baseUrl;

    public ReplacedPainter(DisplayList displayList, ImageCache imageCache, string? baseUrl)
    {
        _displayList = displayList;
        _imageCache = imageCache;
        _baseUrl = baseUrl;
    }

    public bool TryPaint(Element element, ComputedStyle style, LayoutBox box)
    {
        switch (element.TagName.ToUpperInvariant())
        {
            case "VIDEO":
                VideoPainter.Paint(element, style, box, _displayList, _imageCache, _baseUrl);
                return true;

            case "CANVAS":
                HtmlCanvasPainter.Paint(element, style, box, _displayList);
                return true;

            case "OBJECT":
            case "EMBED":
                EmbeddedObjectPainter.Paint(element, style, box, _displayList, _imageCache, _baseUrl);
                return true;
        }
        return false;
    }
}

/// <summary>
/// Paints &lt;video&gt; content: poster image 鈫?attached MediaEngine frame 鈫?/// UA-style placeholder (dark letterbox + centered play glyph). Mirrors
/// video_painter.cc's layering with the media controls appearance folded in,
/// because this renderer has no separate controls layer yet.
/// </summary>
internal static class VideoPainter
{
    public static void Paint(Element element, ComputedStyle style, LayoutBox box,
        DisplayList displayList, ImageCache imageCache, string? baseUrl)
    {
        var content = ReplacedPaintHelpers.ContentRect(box);

        // 1) Poster attribute wins until playback starts.
        var poster = element.GetAttribute("poster");
        if (!string.IsNullOrEmpty(poster))
        {
            var resolved = ReplacedPaintHelpers.ResolveUrl(poster, baseUrl);
            var image = resolved != null ? imageCache.GetImageAsync(resolved).GetAwaiter().GetResult() : null;
            if (image != null)
            {
                ReplacedPaintHelpers.AddImage(displayList, image, content, style);
                return;
            }
        }

        // 2) A live frame from an attached media pipeline (seam: no registry yet).
        if (element is HTMLVideoElement video && TryGetAttachedFrame(video, out var frame) && frame != null)
        {
            ReplacedPaintHelpers.AddImage(displayList, frame, content, style);
            return;
        }

        // 3) UA placeholder: black letterbox + translucent play glyph.
        ReplacedPaintHelpers.AddPlaceholder(displayList, content, withPlayGlyph: true);
    }

    /// <summary>
    /// Seam for future media integration: a per-element MediaEngine registry will
    /// supply decoded frames here. No engine instance exists headlessly today.
    /// </summary>
    private static bool TryGetAttachedFrame(HTMLVideoElement video, out SKImage? frame)
    {
        frame = null;
        return false;
    }
}

/// <summary>
/// Paints &lt;canvas&gt;. A canvas without a rendering context paints nothing
/// beyond its own background (spec-correct); when JS plumbing attaches a backing
/// store it is blitted 1:1 into the content box.
/// </summary>
internal static class HtmlCanvasPainter
{
    public static void Paint(Element element, ComputedStyle style, LayoutBox box,
        DisplayList displayList)
    {
        if (element is not HTMLCanvasElement canvas)
            return;

        // Seam: CanvasRenderingContext2D will publish its surface through the
        // element; draw it stretched to the content box (canvas CSS sizing).
        var snapshot = canvas.SnapshotSurface();
        if (snapshot == null)
            return;

        var content = ReplacedPaintHelpers.ContentRect(box);
        var op = PaintOpPool.GetDrawImageOp();
        op.Image = snapshot;
        op.SourceRect = new SKRect(0, 0, snapshot.Width, snapshot.Height);
        op.DestRect = content;
        op.Fit = ImageFit.Fill;
        op.ZIndex = style.ZIndex ?? 0;
        op.Bounds = content;
        displayList.Add(op);
    }
}

/// <summary>
/// Paints &lt;object&gt;/&lt;embed&gt;. Image payloads render like images
/// (mirroring embedded_object_painter.cc dispatching to image painting); any
/// other payload paints nothing so the element's FALLBACK CHILDREN remain
/// visible 鈥?they are regular DOM nodes painted by the normal walk.
/// </summary>
internal static class EmbeddedObjectPainter
{
    public static void Paint(Element element, ComputedStyle style, LayoutBox box,
        DisplayList displayList, ImageCache imageCache, string? baseUrl)
    {
        string? resource = element.GetAttribute("data") ?? element.GetAttribute("src");
        if (string.IsNullOrEmpty(resource))
            return; // fallback children show

        string? type = element.GetAttribute("type");
        bool looksLikeImage =
            (type != null && type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) ||
            LooksLikeImageExtension(resource);
        if (!looksLikeImage)
            return; // non-image payload: keep fallback content visible

        var resolved = ReplacedPaintHelpers.ResolveUrl(resource!, baseUrl);
        var image = resolved != null ? imageCache.GetImageAsync(resolved).GetAwaiter().GetResult() : null;
        if (image == null)
            return;

        ReplacedPaintHelpers.AddImage(displayList, image, ReplacedPaintHelpers.ContentRect(box), style);
    }

    private static bool LooksLikeImageExtension(string url)
    {
        int q = url.IndexOf('?');
        if (q >= 0) url = url[..q];
        return url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }
}

// ---------------------------------------------------------------------------
// Shared helpers for replaced-content painters.
// ---------------------------------------------------------------------------
internal static class ReplacedPaintHelpers
{
    /// <summary>Content-box rectangle in page coordinates.</summary>
    public static SKRect ContentRect(LayoutBox box) => box.ContentBox;

    public static void AddImage(DisplayList displayList, SKImage image, SKRect dest, ComputedStyle style)
    {
        var (destRect, srcRect) = ImagePainter.ComputeObjectRects(style, dest, image.Width, image.Height);
        if (destRect.Width <= 0 || destRect.Height <= 0)
            return;

        var op = PaintOpPool.GetDrawImageOp();
        op.Image = image;
        op.SourceRect = srcRect;
        op.DestRect = destRect;
        op.Fit = ImageFit.Fill;
        op.ZIndex = style.ZIndex ?? 0;
        op.Bounds = destRect;
        displayList.Add(op);
    }

    /// <summary>
    /// UA video placeholder: near-black letterbox plus a centered translucent
    /// play triangle sized relative to the box (clamped for tiny boxes).
    /// </summary>
    public static void AddPlaceholder(DisplayList displayList, SKRect rect, bool withPlayGlyph)
    {
        if (rect.Width <= 2 || rect.Height <= 2)
            return;

        var bg = PaintOpPool.GetDrawRectOp();
        bg.Rect = rect;
        bg.FillColor = new SKColor(0x10, 0x10, 0x10, 0xFF);
        bg.Bounds = rect;
        displayList.Add(bg);

        if (!withPlayGlyph)
            return;

        float size = Math.Min(rect.Width, rect.Height) * 0.34f;
        size = Math.Clamp(size, 14f, 96f);
        float cx = rect.MidX, cy = rect.MidY;

        // NOTE: ops returned by PaintOpPool own their Path/Paint (Reset disposes
        // them), so nothing here may be disposed by the caller.
        var circle = new SKPath();
        circle.AddCircle(cx, cy, size / 2f, SKPathDirection.Clockwise);
        var circleOp = PaintOpPool.GetDrawPathOp();
        circleOp.Path = circle;
        circleOp.FillPaint = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0x33), IsAntialias = true };
        circleOp.Bounds = new SKRect(cx - size / 2f, cy - size / 2f, cx + size / 2f, cy + size / 2f);
        displayList.Add(circleOp);

        float tri = size * 0.42f;
        var triPath = new SKPath();
        triPath.MoveTo(cx - tri * 0.55f, cy - tri);
        triPath.LineTo(cx - tri * 0.55f, cy + tri);
        triPath.LineTo(cx + tri * 0.85f, cy);
        triPath.Close();
        var triOp = PaintOpPool.GetDrawPathOp();
        triOp.Path = triPath;
        triOp.FillPaint = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0xE6), IsAntialias = true };
        triOp.Bounds = new SKRect(cx - tri, cy - tri, cx + tri, cy + tri);
        displayList.Add(triOp);
    }

    public static string? ResolveUrl(string url, string? baseUrl) => UrlResolver.Resolve(url, baseUrl);
}
