using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Everything the compositor needs to draw a cached scroll layer live each frame:
/// the rasterized content image (device-resolution, scroll = 0), its geometry and
/// the scrollbar appearance.
/// </summary>
public readonly struct ScrollLayerInfo
{
    public required LayoutBox Box { get; init; }
    public required SKImage Image { get; init; }
    public SKRect ContentBox { get; init; }
    public SKRect PaddingBox { get; init; }
    public float ScrollbarThickness { get; init; }
    public SKColor TrackColor { get; init; }
    public SKColor ThumbColor { get; init; }
    public float ThumbRadius { get; init; }
    public bool ShowVertical { get; init; }
    public bool ShowHorizontal { get; init; }
    /// <summary>
    /// True when the layer image already bakes the CURRENT scroll offset (content
    /// shift + sticky offsets) and must be rebuilt each scroll frame; false when it
    /// is scroll-invariant (scroll = 0) and the compositor applies the offset live.
    /// </summary>
    public bool IsBaked { get; init; }
}

/// <summary>
/// Cached raster layers for scroll containers. When a container is "layered", its
/// scrollable content is rasterized once per layout into an <see cref="SKImage"/>
/// (content-box local, scroll = 0); the compositor draws that layer at the CURRENT
/// scroll offset on every frame, so scrolling a layered container is pure
/// compositing �?no document repaint, no re-record, no tile re-raster.
///
/// Entries are keyed by the layout box instance and cleared on every full rebuild /
/// navigation. A container is only layered when it passes <see cref="IsLayerable"/>
/// and <see cref="SubtreeIsSimple"/>; everything else keeps the inline repaint path,
/// so correctness never regresses.
/// </summary>
public static class ScrollLayerCache
{
    private const int MaxDimension = 8192;
    private const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>Master switch (diagnostics): when false every container uses the
    /// inline repaint path.</summary>
    public static bool Enabled = true;

    private static readonly Dictionary<LayoutBox, LayerEntry> _entries = new(ReferenceEqualityComparer.Instance);
    private static long _bytes;
    private static readonly object _lock = new();

    private sealed class LayerEntry
    {
        public required SKImage Image;
        public required SKRect ContentBox;
        public SKRect PaddingBox;
        public float Thickness;
        public SKColor Track;
        public SKColor Thumb;
        public float ThumbRadius;
        public bool ShowVertical;
        public bool ShowHorizontal;
        public bool IsBaked;
        public bool Disposed;
        /// <summary>Scroll offsets captured when this layer was baked (raw, px).</summary>
        public float BakedScrollX;
        public float BakedScrollY;
    }

    /// <summary>
    /// True when the baked-mode layer for <paramref name="box"/> was rasterized at
    /// a different DEVICE-quantized scroll offset than its current one, so it must
    /// be re-baked. Layers that bake the current scroll (sticky/z-index children)
    /// are otherwise re-rasterized on every frame during scrolling; skipping frames
    /// where the quantized offset is unchanged removes most of that redundant work.
    /// </summary>
    public static bool BakedOffsetChanged(LayoutBox box, float scale)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(box, out var e) || e is { Disposed: true })
                return true;
            float sx = MathF.Round(box.ScrollX * scale);
            float sy = MathF.Round(box.ScrollY * scale);
            return sx != MathF.Round(e.BakedScrollX * scale)
                || sy != MathF.Round(e.BakedScrollY * scale);
        }
    }

    public static bool IsLayered(LayoutBox box)
    {
        lock (_lock)
            return _entries.TryGetValue(box, out var e) && e is { Disposed: false };
    }

    public static bool TryGetInfo(LayoutBox box, out ScrollLayerInfo info)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(box, out var e) && e is { Disposed: false })
            {
                info = new ScrollLayerInfo
                {
                    Box = box,
                    Image = e.Image,
                    ContentBox = e.ContentBox,
                    PaddingBox = e.PaddingBox,
                    ScrollbarThickness = e.Thickness,
                    TrackColor = e.Track,
                    ThumbColor = e.Thumb,
                    ThumbRadius = e.ThumbRadius,
                    ShowVertical = e.ShowVertical,
                    ShowHorizontal = e.ShowHorizontal,
                    IsBaked = e.IsBaked,
                };
                return true;
            }
        }
        info = default;
        return false;
    }

    public static bool HasAny
    {
        get { lock (_lock) return _entries.Count > 0; }
    }

    /// <summary>
    /// Enumerate every live scroll layer; the compositor draws these at their
    /// CURRENT scroll offsets each frame.
    /// </summary>
    public static void Enumerate(Action<ScrollLayerInfo> visitor)
    {
        lock (_lock)
        {
            foreach (var kv in _entries)
            {
                var e = kv.Value;
                if (e.Disposed) continue;
                visitor(new ScrollLayerInfo
                {
                    Box = kv.Key,
                    Image = e.Image,
                    ContentBox = e.ContentBox,
                    PaddingBox = e.PaddingBox,
                    ScrollbarThickness = e.Thickness,
                    TrackColor = e.Track,
                    ThumbColor = e.Thumb,
                    ThumbRadius = e.ThumbRadius,
                    ShowVertical = e.ShowVertical,
                    ShowHorizontal = e.ShowHorizontal,
                });
            }
        }
    }

    /// <summary>Register (or replace) a freshly rasterized layer for <paramref name="box"/>.</summary>
    public static void Register(LayoutBox box, SKImage image, SKRect contentBox, int pixelW, int pixelH,
        SKRect paddingBox, float thickness, SKColor track, SKColor thumb, float thumbRadius,
        bool showVertical, bool showHorizontal, bool isBaked)
    {
        long bytes = (long)pixelW * pixelH * 4;
        lock (_lock)
        {
            if (_entries.TryGetValue(box, out var old))
            {
                old.Disposed = true;
                old.Image.Dispose();
                _bytes -= Math.Max(0, ByteCountApprox(old.ContentBox));
            }
            var entry = new LayerEntry
            {
                Image = image,
                ContentBox = contentBox,
                PaddingBox = paddingBox,
                Thickness = thickness,
                Track = track,
                Thumb = thumb,
                ThumbRadius = thumbRadius,
                ShowVertical = showVertical,
                ShowHorizontal = showHorizontal,
                IsBaked = isBaked,
                BakedScrollX = box.ScrollX,
                BakedScrollY = box.ScrollY,
            };
            _entries[box] = entry;
            _bytes += bytes;
        }
    }

    public static void ClearAll()
    {
        lock (_lock)
        {
            foreach (var kv in _entries)
            {
                if (kv.Value.Disposed) continue;
                kv.Value.Disposed = true;
                kv.Value.Image.Dispose();
            }
            _entries.Clear();
            _bytes = 0;
        }
    }

    public static bool IsLayerable(LayoutBox box)
    {
        if (box == null || !box.IsScrollContainer) return false;

        float contentW = box.ScrollContentWidth;
        float contentH = box.ScrollContentHeight;
        int w = Math.Max(1, (int)MathF.Ceiling(contentW));
        int h = Math.Max(1, (int)MathF.Ceiling(contentH));
        if (w > MaxDimension || h > MaxDimension) return false;
        if ((long)w * h * 4 > MaxBytes) return false;

        return true;
    }

    /// <summary>
    /// True when the subtree rooted at <paramref name="box"/> contains no nested
    /// scroll container, no sticky descendant and no explicit z-index (anything
    /// that could escape the atomic layer and change page-level stacking).
    /// </summary>
    public static bool SubtreeIsSimple(LayoutBox box)
    {
        var queue = new Queue<LayoutBox>();
        queue.Enqueue(box);
        while (queue.Count > 0)
        {
            var b = queue.Dequeue();
            foreach (var child in b.Children)
            {
                // Nested scrollers are the one hard break; sticky / z-index children
                // are handled by the baked rebuild-mode (IsBaked).
                if (child.IsScrollContainer) return false;
                queue.Enqueue(child);
            }
        }
        return true;
    }

    /// <summary>
    /// True when the subtree rooted at <paramref name="box"/> contains sticky or
    /// z-index children — content that cannot be encoded at scroll = 0, so the
    /// layer must bake the CURRENT scroll and be rebuilt each scroll frame.
    /// </summary>
    public static bool SubtreeNeedsBake(LayoutBox box)
    {
        var queue = new Queue<LayoutBox>();
        queue.Enqueue(box);
        while (queue.Count > 0)
        {
            var b = queue.Dequeue();
            foreach (var child in b.Children)
            {
                if (child.IsSticky || child.ZIndex is not null) return true;
                queue.Enqueue(child);
            }
        }
        return false;
    }

    private static long ByteCountApprox(SKRect contentBox)
    {
        return (long)(Math.Max(1, MathF.Ceiling(contentBox.Width))
            * Math.Max(1, MathF.Ceiling(contentBox.Height)) * 4);
    }
}