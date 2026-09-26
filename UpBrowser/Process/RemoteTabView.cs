using SkiaSharp;

namespace UpBrowser.Process;

/// <summary>
/// Parent-side compositor state for one remote tab. Applies Full / DamageRows /
/// ScrollBlit packets straight into the SKBitmap the UI thread draws — no
/// intermediate mirror buffer, so steady-state scrolling allocates nothing and
/// copies only the bytes that actually changed.
/// </summary>
internal sealed class RemoteTabView : IDisposable
{
    private SKBitmap? _bitmap;
    private long _drawnVersion = -1;

    public RemoteTabProcess Proc { get; }

    // Last offset the parent pushed to the child.
    public float SentScrollX, SentScrollY;
    // Offset + page extents of the currently applied frame.
    public float FrameScrollX, FrameScrollY;
    public float ContentW, ContentH;
    public int DomCount, BoxCount;
    // Initial navigation deferred until the pipe connects.
    public string? PendingInitial;
    public bool InitialSent;
    // Crash-restart bookkeeping.
    public string LastUrl = "";
    public int RestartCount;

    public RemoteTabView(RemoteTabProcess proc) => Proc = proc;

    public SKBitmap? Bitmap => _bitmap;
    public bool HasFrame => _bitmap != null;
    /// <summary>Page background of the applied frame (freshly exposed scroll strips are filled with it).</summary>
    public SKColor PageBg { get; private set; } = SKColors.White;

    /// <summary>Fold the newest frame packet into the bitmap. True if applied.</summary>
    public unsafe bool UpdateFromFrame()
    {
        long version = Proc.FrameVersion;
        if (version == _drawnVersion) return false;
        if (!Proc.TryGetFrame(out var buf, out int off, out int pixLen,
                out int w, out int h, out float sx, out float sy,
                out float cw, out float ch, out int dom, out int box,
                out var mode, out int bandY, out int bandH, out long ver))
            return false;
        if (buf == null || w <= 0 || h <= 0) return false;

        int rowBytes = w * 4;
        // pixLen may be 0 for a shift-only ScrollBlit packet (content moved, nothing changed).
        if (pixLen < 0 || off + pixLen > buf.Length || (long)bandH * rowBytes != pixLen) return false;
        if (mode != FrameMode.ScrollBlit && pixLen == 0) return false;
        if (bandY < 0 || bandH < 0 || (long)(bandY + bandH) * rowBytes > (long)h * rowBytes) return false;

        bool sizeChanged = _bitmap == null || _bitmap.Width != w || _bitmap.Height != h;
        if (sizeChanged)
        {
            _bitmap?.Dispose();
            _bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            mode = FrameMode.Full; // resync at a new size
            bandY = 0; bandH = h;
        }

        byte* dst = (byte*)_bitmap.GetPixels(out _);

        if (mode == FrameMode.ScrollBlit && !sizeChanged)
        {
            // Frame row y shows document row sy+y = old row (y+dy). Shifting DOWN
            // by dy (dest[y] = src[y+dy]) when the offset grew; the band the child
            // sends is exactly the exposed strip (bottom for dy>0, top for dy<0).
            int dy = (int)Math.Round(sy - FrameScrollY);
            if (dy > 0 && dy < h)
            {
                for (int y = 0; y + dy < h; y++)
                    Buffer.MemoryCopy(dst + (long)(y + dy) * rowBytes, dst + (long)y * rowBytes, rowBytes, rowBytes);
            }
            else if (dy < 0 && -dy < h)
            {
                for (int y = h - 1; y + dy >= 0; y--)
                    Buffer.MemoryCopy(dst + (long)(y + dy) * rowBytes, dst + (long)y * rowBytes, rowBytes, rowBytes);
            }
        }

        // Overlay the payload band (Full ⇒ every row).
        fixed (byte* src = buf)
            Buffer.MemoryCopy(src + off, dst + (long)bandY * rowBytes, (long)bandH * rowBytes, pixLen);
        _bitmap.NotifyPixelsChanged();

        _drawnVersion = ver;
        FrameScrollX = sx; FrameScrollY = sy;
        ContentW = cw; ContentH = ch;
        DomCount = dom; BoxCount = box;
        PageBg = new SKColor(Proc.PageBgRgba);
        return true;
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        Proc.Dispose();
    }
}
