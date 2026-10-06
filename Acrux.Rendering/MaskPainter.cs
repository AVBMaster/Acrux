using SkiaSharp;
using Acrux.Core.Css;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Handles CSS mask-image rendering. Mirrors css_mask_painter.cc.
/// Loads the mask image and applies it as an alpha mask layer.
/// </summary>
internal sealed class MaskPainter
{
    private readonly ImageCache _imageCache;
    private readonly string? _baseUrl;
    private bool _luminance;

    public MaskPainter(ImageCache imageCache, string? baseUrl)
    {
        _imageCache = imageCache;
        _baseUrl = baseUrl;
    }

    public bool HasMask(ComputedStyle style) =>
        !string.IsNullOrEmpty(style.MaskImage) && style.MaskImage != "none" ||
        !string.IsNullOrEmpty(style.Mask) && style.Mask.Trim() != "none";

    public SKImage? TryLoadMaskImage(ComputedStyle style) => TryBuildMaskImage(style, SKRect.Empty);

    /// <summary>The three boxes a mask layer can be positioned in or clipped to, in the mask
    /// surface's own coordinates (its top-left is the border box's top-left).</summary>
    private readonly record struct Boxes(SKRect Border, SKRect Padding, SKRect Content)
    {
        public static Boxes For(ComputedStyle style, SKRect borderBox, Acrux.Core.Dom.LayoutBox? box)
        {
            // The mask surface is the border box, so every rect below is expressed from its
            // top-left — feeding the caller's document-space rect in would place the tiles
            // outside the surface and mask the element away entirely.
            var local = new SKRect(0, 0, MathF.Max(0, borderBox.Width), MathF.Max(0, borderBox.Height));
            if (local.Width <= 0 || local.Height <= 0) return new Boxes(local, local, local);
            // Without the laid-out box there is nothing to derive the inner boxes from, and a
            // mask with no authored geometry paints the whole border box either way.
            if (box == null) return new Boxes(local, local, local);
            var shift = new SKPoint(-borderBox.Left, -borderBox.Top);
            var padding = box.PaddingBox; padding.Offset(shift);
            var content = box.ContentBox; content.Offset(shift);
            return new Boxes(local, padding, content);
        }

        public SKRect Resolve(string? name, bool isClip) =>
            MaskGeometry.Box(name, Border, Padding, Content, isClip);
    }

    /// <summary>
    /// Build the mask paint source for mask-image: a decoded url() image, or a
    /// rasterized gradient sized to the element's border box. The layer blends
    /// with SrcIn, so the gradient's alpha channel is what masks (CSS Masking 1 §11).
    /// </summary>
    public SKImage? TryBuildMaskImage(ComputedStyle style, SKRect borderBox,
        Acrux.Core.Dom.LayoutBox? layoutBox = null)
    {
        // The authored 'mask' text is preferred over the normalized 'mask-image' list because
        // it still carries each layer's own position/size; the longhands fill in whatever a
        // layer did not name (they are single scalars until the lists are modelled per layer).
        var maskValue = style.Mask;
        if (string.IsNullOrEmpty(maskValue) || maskValue.Trim() == "none")
            maskValue = style.MaskImage;
        if (string.IsNullOrEmpty(maskValue) || maskValue.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        var layers = Acrux.Core.Css.MaskLayerParser.Parse(maskValue);
        if (layers.Count == 0) return null;

        int width = (int)MathF.Max(1, MathF.Round(borderBox.Width));
        int height = (int)MathF.Max(1, MathF.Round(borderBox.Height));
        var info = new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null) return null;
        surface.Canvas.Clear(SKColors.Transparent);

        var boxes = Boxes.For(style, borderBox, layoutBox);

        // Layers are listed top first, so the bottom one is painted as the backdrop and each
        // layer above it is blended with the operator declared for that layer.
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            var layer = layers[i];
            bool isBottom = i == layers.Count - 1;
            var composite = isBottom ? "add"
                : layer.Composite ?? At(style.MaskCompositeLayers, i) ?? CompositeAt(style.MaskComposite, i);

            static string? At(List<string>? list, int index)
                => list is { Count: > 0 } ? list[index % list.Count] : null;

            if (isBottom && composite == "add")
            {
                PaintLayerOnto(surface.Canvas, layer, style, boxes, width, height, i, layers.Count);
                continue;
            }

            using var scratch = SKSurface.Create(info);
            if (scratch == null) return null;
            scratch.Canvas.Clear(SKColors.Transparent);
            PaintLayerOnto(scratch.Canvas, layer, style, boxes, width, height, i, layers.Count);

            if (composite != "subtract")
            {
                using var layerImage = scratch.Snapshot();
                using var paint = new SKPaint { BlendMode = CompositeToBlendMode(composite) };
                surface.Canvas.DrawImage(layerImage, 0, 0, paint);
                continue;
            }

            // 'subtract' is max(0, layer - backdrop) and replaces the accumulated mask.
            // This Skia build has no subtracting blend mode, so the alpha channel is
            // computed directly; only a mask's alpha is ever consumed downstream.
            if (!TrySubtractAlpha(scratch, surface, width, height, out var difference)) return null;
            using (difference)
            using (var replace = new SKPaint { BlendMode = SKBlendMode.Src })
                surface.Canvas.DrawImage(difference, 0, 0, replace);
        }

        return surface.Snapshot();
    }

    private static string CompositeAt(string? list, int index)
    {
        var parts = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(list ?? string.Empty, ',');
        if (parts.Count == 0) return "add";
        return parts[index % parts.Count];
    }

    /// <summary>
    /// Paint one layer's tiles into a mask canvas: resolve the box the layer is positioned in
    /// ('mask-origin') and the box it is clipped to ('mask-clip'), size and anchor the tile
    /// from 'mask-size' / 'mask-position', then repeat it per 'mask-repeat'
    /// (CSS Masking 1 §7.1, §9; the geometry is the background algorithm).
    /// </summary>
    private void PaintLayerOnto(SKCanvas canvas, Acrux.Core.Css.MaskLayer layer, ComputedStyle style,
        Boxes boxes, int surfaceWidth, int surfaceHeight, int layerIndex, int layerCount)
    {
        if (layer.Image == "none") return;

        // A part the layer text did not name comes from the corresponding longhand, cycled
        // against the layer count exactly as the computed value cycles it (CSS Backgrounds 3 §2
        /// inherited by 'mask'): three images and two positions means image three uses position
        // one again. The scalar field is the first layer, so a single-layer mask is unaffected.
        string? Longhand(List<string>? layers, string? scalar) =>
            layers is { Count: > 0 } ? layers[layerIndex % layers.Count] : scalar;

        // 'mask-mode' may be named per layer or once for the element; 'match-source' means
        // alpha for a gradient and for a raster image (there is no SVG <mask> here).
        var mode = layer.Mode ?? Longhand(style.MaskModeLayers, style.MaskMode) ?? string.Empty;
        _luminance = mode.Contains("luminance", StringComparison.OrdinalIgnoreCase);

        var origin = boxes.Resolve(layer.Origin ?? Longhand(style.MaskOriginLayers, style.MaskOrigin), isClip: false);
        var clip = boxes.Resolve(layer.Clip ?? Longhand(style.MaskClipLayers, style.MaskClip), isClip: true);
        var area = new SKSize(Math.Max(0, origin.Width), Math.Max(0, origin.Height));

        var intrinsic = TryIntrinsicSize(layer.Image);
        var tile = MaskGeometry.TileSize(layer.Size ?? Longhand(style.MaskSizeLayers, style.MaskSize), area, intrinsic);
        var anchor = MaskGeometry.Position(layer.Position ?? Longhand(style.MaskPositionLayers, style.MaskPosition), area, tile);
        var repeat = MaskGeometry.Repeat(layer.Repeat ?? Longhand(style.MaskRepeatLayers, style.MaskRepeat));
        var tiles = MaskGeometry.Tiles(area, tile, anchor, repeat);
        if (tiles.Count == 0) return;

        int save = canvas.Save();
        // Everything outside the clip box masks nothing, so it stays transparent.
        canvas.ClipRect(clip);
        canvas.Translate(origin.Left, origin.Top);
        foreach (var rect in tiles)
            DrawLayerOnto(canvas, layer.Image, rect, SKBlendMode.Src);
        canvas.RestoreToCount(save);
    }

    /// <summary>A raster mask source has an intrinsic size, which 'auto', 'contain', 'cover'
    /// and the single-length 'mask-size' all need; a gradient does not, so its default object
    /// size is the positioning area.</summary>
    private SKSize? TryIntrinsicSize(string image)
    {
        if (image.Contains("gradient", StringComparison.OrdinalIgnoreCase)) return null;
        var resolved = ResolveMaskUrl(image);
        if (resolved == null) return null;
        var task = _imageCache.GetImageAsync(resolved);
        task.Wait();
        var decoded = task.Result;
        return decoded == null ? null : new SKSize(decoded.Width, decoded.Height);
    }

    /// <summary>
    /// Paint every mask layer into one alpha mask, compositing each layer onto the
    /// layers below it with its mask-composite operator (CSS Masking 1 5.1). Chrome
    /// treats the upper layer as the source, so 'subtract' keeps the upper layer.
    /// </summary>
    private SKImage? BuildCompositedMask(List<string> layers, string? compositeValue, SKRect borderBox)
    {
        int width = (int)MathF.Max(1, MathF.Round(borderBox.Width));
        int height = (int)MathF.Max(1, MathF.Round(borderBox.Height));
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null) return null;
        surface.Canvas.Clear(SKColors.Transparent);

        var composites = string.IsNullOrEmpty(compositeValue)
            ? Array.Empty<string>()
            : SplitTopLevel(compositeValue, ',').Select(s => s.Trim().ToLowerInvariant()).ToArray();

        // Layers are listed top first; the bottom one is painted as the backdrop and
        // each layer above it is blended with the operator declared for that layer.
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            var (image, rect) = ParseMaskLayer(layers[i], width, height);
            if (image == null) return null;
            bool isBottom = i == layers.Count - 1;
            var composite = !isBottom && i < composites.Length ? composites[i] : "add";

            if (isBottom)
            {
                DrawLayerOnto(surface.Canvas, image, rect, SKBlendMode.Src);
                continue;
            }

            // The operator applies to the whole mask area, and outside its own box a
            // layer is transparent, so every non-bottom layer goes through a buffer.
            using var scratch = SKSurface.Create(info);
            if (scratch == null) return null;
            scratch.Canvas.Clear(SKColors.Transparent);
            DrawLayerOnto(scratch.Canvas, image, rect, SKBlendMode.Src);

            if (composite != "subtract")
            {
                using var layerImage = scratch.Snapshot();
                using var paint = new SKPaint { BlendMode = CompositeToBlendMode(composite) };
                surface.Canvas.DrawImage(layerImage, 0, 0, paint);
                continue;
            }

            // 'subtract' is max(0, layer - backdrop) and replaces the accumulated mask.
            // This Skia build has no subtracting blend mode, so the alpha channel is
            // computed directly; only a mask's alpha is ever consumed downstream.
            if (!TrySubtractAlpha(scratch, surface, width, height, out var difference)) return null;
            using (difference)
            using (var replace = new SKPaint { BlendMode = SKBlendMode.Src })
                surface.Canvas.DrawImage(difference, 0, 0, replace);
        }

        return surface.Snapshot();
    }

    private static bool TrySubtractAlpha(SKSurface layer, SKSurface backdrop, int width, int height, out SKImage? result)
    {
        result = null;
        using var layerImage = layer.Snapshot();
        using var backdropImage = backdrop.Snapshot();
        using var layerBitmap = SKBitmap.FromImage(layerImage);
        using var backdropBitmap = SKBitmap.FromImage(backdropImage);
        if (layerBitmap.RowBytes != width * 4 || backdropBitmap.RowBytes != width * 4)
            return false;

        var layerPixels = new byte[width * height * 4];
        var backdropPixels = new byte[width * height * 4];
        System.Runtime.InteropServices.Marshal.Copy(layerBitmap.GetPixels(), layerPixels, 0, layerPixels.Length);
        System.Runtime.InteropServices.Marshal.Copy(backdropBitmap.GetPixels(), backdropPixels, 0, backdropPixels.Length);

        var output = new byte[layerPixels.Length];
        for (int i = 0; i < output.Length; i += 4)
        {
            int alpha = layerPixels[i + 3] - backdropPixels[i + 3];
            if (alpha < 0) alpha = 0;
            output[i + 3] = (byte)alpha;
        }

        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(output, 0, bitmap.GetPixels(), output.Length);
        result = SKImage.FromBitmap(bitmap);
        bitmap.Dispose();
        return result != null;
    }

    private void DrawLayerOnto(SKCanvas canvas, string image, SKRect rect, SKBlendMode blend)
    {
        if (image.Contains("gradient", StringComparison.OrdinalIgnoreCase))
        {
            var shader = GradientRenderer.CreateGradient(image, rect);
            if (shader == null) return;
            if (_luminance)
            {
                using var raster = GradientRenderer.RasterizeToImage(image, rect);
                if (raster != null)
                {
                    using var converted = ToLuminanceAlpha(raster);
                    if (converted != null)
                    {
                        using var paint = new SKPaint { BlendMode = blend };
                        canvas.DrawImage(converted, rect, paint);
                    }
                    return;
                }
            }
            using var shaderPaint = new SKPaint { Shader = shader, BlendMode = blend };
            canvas.DrawRect(rect, shaderPaint);
            return;
        }

        var resolved = ResolveMaskUrl(image);
        if (resolved == null) return;
        var task = _imageCache.GetImageAsync(resolved);
        task.Wait();
        var decoded = task.Result;
        if (decoded == null) return;
        using var imagePaint = new SKPaint { BlendMode = blend };
        if (_luminance)
        {
            using var converted = ToLuminanceAlpha(decoded);
            if (converted == null) return;
            canvas.DrawImage(converted, rect, imagePaint);
            return;
        }
        canvas.DrawImage(decoded, rect, imagePaint);
    }

    /// <summary>
    /// CSS Masking 1 11.2 'luminance': the mask value is the image's relative
    /// luminance times its alpha. Chrome uses the sRGB-weighted sum of the encoded
    /// channels (verified against its rendering of a red-to-blue gradient mask).
    /// </summary>
    private static SKImage? ToLuminanceAlpha(SKImage source)
    {
        int width = source.Width, height = source.Height;
        using var bitmap = SKBitmap.FromImage(source);
        if (bitmap.RowBytes != width * 4) return null;

        var pixels = new byte[width * height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        // SKBitmap.FromImage converts to the platform order, which is BGRA on little
        // endian, so the red and blue channels have to be picked by colour type.
        bool bgra = bitmap.ColorType == SKColorType.Bgra8888;
        int redIndex = bgra ? 2 : 0;
        int blueIndex = bgra ? 0 : 2;
        var output = new byte[pixels.Length];
        for (int i = 0; i < output.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            // Undo premultiplication so the colour of fully transparent pixels is ignored.
            float r = alpha == 0 ? 0 : pixels[i + redIndex] * 255f / alpha;
            float g = alpha == 0 ? 0 : pixels[i + 1] * 255f / alpha;
            float b = alpha == 0 ? 0 : pixels[i + blueIndex] * 255f / alpha;
            float luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            output[i + 3] = (byte)Math.Clamp(MathF.Round(luminance * alpha / 255f), 0f, 255f);
        }

        var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(output, 0, result.GetPixels(), output.Length);
        var image = SKImage.FromBitmap(result);
        result.Dispose();
        return image;
    }

    private static SKBlendMode CompositeToBlendMode(string composite) => composite switch
    {
        "intersect" => SKBlendMode.Modulate,
        "exclude" => SKBlendMode.Exclusion,
        _ => SKBlendMode.Plus,
    };

    /// <summary>
    /// Split one mask layer into its image and the destination rect from the optional
    /// `position / size` tail. Percentages follow the background-position rule.
    /// </summary>
    private static (string? Image, SKRect Rect) ParseMaskLayer(string layer, float boxWidth, float boxHeight)
    {
        var full = new SKRect(0, 0, boxWidth, boxHeight);
        layer = layer.Trim();
        int imageEnd = FindImageEnd(layer);
        if (imageEnd <= 0) return (null, full);
        var image = layer[..imageEnd].Trim();
        var tail = layer[imageEnd..].Trim();
        if (tail.Length == 0) return (image, full);

        // The geometry is `position / size`; every other keyword (repeat, origin, clip,
        // mask-mode) is parsed but not consumed, since layers are painted once.
        var parts = tail.Split('/');
        var position = SplitTokens(parts[0]);
        var size = parts.Length > 1 ? SplitTokens(parts[1]) : new List<string>();

        float w = size.Count > 0 && size[0] != "auto" ? ResolveLength(size[0], boxWidth) : boxWidth;
        float h = size.Count > 1 && size[1] != "auto" ? ResolveLength(size[1], boxHeight) : boxHeight;

        float x = position.Count > 0 ? ResolvePosition(position[0], boxWidth, w) : 0;
        float y = position.Count > 1 ? ResolvePosition(position[1], boxHeight, h) : (boxHeight - h) / 2f;

        return (image, new SKRect(x, y, x + w, y + h));
    }

    private static List<string> SplitTokens(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static float ResolveLength(string token, float extent)
    {
        if (token.Equals("auto", StringComparison.OrdinalIgnoreCase)) return extent;
        if (token.EndsWith('%') && float.TryParse(token[..^1], out var pct))
            return extent * pct / 100f;
        var length = Length.Parse(token);
        return length != null ? length.ToPixels(extent, extent, extent, extent) : extent;
    }

    private static float ResolvePosition(string token, float extent, float imageSize)
    {
        if (token.Equals("left", StringComparison.OrdinalIgnoreCase) || token == "0") return 0;
        if (token.Equals("right", StringComparison.OrdinalIgnoreCase)) return extent - imageSize;
        if (token.Equals("center", StringComparison.OrdinalIgnoreCase)) return (extent - imageSize) / 2f;
        if (token.EndsWith('%') && float.TryParse(token[..^1], out var pct))
            return (extent - imageSize) * pct / 100f;
        var length = Length.Parse(token);
        return length != null ? length.ToPixels(extent, extent, extent, extent) : 0;
    }

    /// <summary>Index just past the image function of a layer, honouring nesting.</summary>
    private static int FindImageEnd(string layer)
    {
        int depth = 0;
        for (int i = 0; i < layer.Length; i++)
        {
            char c = layer[i];
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return i + 1;
            }
            else if (depth == 0 && (c == ' ' || c == '\t'))
            {
                // A bare url-less value cannot happen for masks; stop at the first space.
                return i;
            }
        }
        return layer.Length;
    }

    private static List<string> SplitTopLevel(string value, char separator)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == separator && depth == 0)
            {
                result.Add(value[start..i]);
                start = i + 1;
            }
        }
        result.Add(value[start..]);
        return result.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private string? ResolveMaskUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[4..].Trim(' ', '"', '\'', ')');
        return UrlResolver.Resolve(trimmed, _baseUrl);
    }
}
