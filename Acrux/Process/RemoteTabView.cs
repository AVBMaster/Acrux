using SkiaSharp;
using Acrux.PageContract;

namespace Acrux.Process;

/// <summary>
/// Parent-side compositor state for one remote tab. Leases published slots from the
/// shared-memory frame channel and folds Full / DamageRows / ScrollBlit deltas into the
/// SKBitmap the UI thread draws, copying only the damaged rects. The mirror bitmap is
/// deliberate: the shell must keep painting the last frame after a host is wedged or
/// killed, and a released slot belongs to the host again.
/// </summary>
internal sealed class RemoteTabView : IDisposable
{
    private SKBitmap? _bitmap;
    private long _drawnVersion = -1;
    private ulong _appliedSeq;
    private int _channelVersion = -1;

    public RemoteTabProcess Proc { get; }

    // Last offset the parent pushed to the child.
    public float SentScrollX, SentScrollY;
    // Offset + page extents of the currently applied frame.
    public float FrameScrollX, FrameScrollY;
    /// <summary>True while the page renders a select list over its content.</summary>
    public bool FrameSelectOpen;
    public float ContentW, ContentH;
    public int DomCount, BoxCount;
    // Initial navigation deferred until the pipe connects.
    public string? PendingInitial;
    public bool InitialSent;
    // Crash bookkeeping: the budget decides whether to relaunch, and a spent budget shows
    // the user a card instead of restarting the same crash forever. The policy instance is
    // carried over when a tab is relaunched — a fresh one would make the budget endless.
    public string LastUrl = "";
    public RestartPolicy Restart = new();
    public bool DeathHandled;
    public bool NeedsRelaunch;
    public long RelaunchAtTick;
    public bool Crashed;
    public string CrashDetail = "";

    public RemoteTabView(RemoteTabProcess proc) => Proc = proc;

    public SKBitmap? Bitmap => _bitmap;
    public bool HasFrame => _bitmap != null;
    /// <summary>Page background of the applied frame (freshly exposed scroll strips are filled with it).</summary>
    public SKColor PageBg { get; private set; } = SKColors.White;

    /// <summary>
    /// Fold every published frame the child has committed since the last call into the
    /// bitmap. True if anything was applied.
    /// </summary>
    public unsafe bool UpdateFromFrame()
    {
        long version = Proc.FrameVersion;
        if (version == _drawnVersion) return false;
        bool applied = false;
        lock (Proc.FrameSync)
        {
            // A new mapping generation numbers its slots from one again; start over.
            if (Proc.ChannelVersion != _channelVersion)
            {
                _channelVersion = Proc.ChannelVersion;
                _appliedSeq = 0;
            }
            while (Proc.TryAcquireFrame(_appliedSeq, out var ch, out int slot,
                       out ulong seq, out var meta, out var rects))
            {
                _appliedSeq = seq;
                try
                {
                    ApplySlot(ch!, slot, in meta, rects);
                }
                finally
                {
                    // A leaked lease is a slow stall: the host waits for a slot that the
                    // shell finished with in every other sense. Release even if the fold
                    // threw (an allocation failure on a resize, say).
                    Proc.ReleaseFrame(ch!, slot);
                }
                applied = true;
                // In practice at most one frame is ever pending (the host will not
                // publish a shifted frame until this one is released); the loop just
                // closes the commit-arrival race.
            }
            if (applied) _drawnVersion = version;
        }
        return applied;
    }

    private unsafe void ApplySlot(FrameChannel ch, int slot, in FrameMeta meta, DamageRect[] rects)
    {
        int w = ch.Width, h = ch.Height, stride = ch.Stride;
        bool sizeChanged = _bitmap == null || _bitmap.Width != w || _bitmap.Height != h;
        if (sizeChanged)
        {
            _bitmap?.Dispose();
            _bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        }

        byte* dst = (byte*)_bitmap!.GetPixels(out _);
        byte* src = ch.PixelPointer(slot);
        var mode = sizeChanged ? FrameMode.Full : meta.Mode; // resync at a new size

        if (mode == FrameMode.ScrollBlit)
        {
            // The host shifted its baseline slot by ScrollDy whole rows before
            // painting the exposed strip: frame row y shows document row
            // (sy_new + y) = old row (y + dy). Apply the identical shift here so the
            // mirror stays byte-for-byte in step with the host's surface — the same
            // copy order the host used, so overlapping ranges never corrupt.
            int dy = meta.ScrollDy;
            if (dy > 0 && dy < h)
            {
                for (int y = 0; y + dy < h; y++)
                    Buffer.MemoryCopy(dst + (long)(y + dy) * stride, dst + (long)y * stride, stride, stride);
            }
            else if (dy < 0 && -dy < h)
            {
                for (int y = h - 1; y + dy >= 0; y--)
                    Buffer.MemoryCopy(dst + (long)(y + dy) * stride, dst + (long)y * stride, stride, stride);
            }
        }

        if (mode == FrameMode.Full)
        {
            // Whole viewport; the slot row pitch is the channel stride.
            for (int y = 0; y < h; y++)
                Buffer.MemoryCopy(src + (long)y * stride, dst + (long)y * stride, stride, stride);
        }
        else
        {
            // Overlay only the damaged rects (row runs the host authored), clipped to
            // the surface — a rect hanging off the last row still has valid rows above it.
            for (int i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                int x0 = Math.Max(0, r.X), y0 = Math.Max(0, r.Y);
                int x1 = Math.Min(w, r.X + r.Width), y1 = Math.Min(h, r.Y + r.Height);
                if (x1 <= x0 || y1 <= y0) continue;
                int bytes = (x1 - x0) * 4;
                for (int y = y0; y < y1; y++)
                    Buffer.MemoryCopy(src + (long)y * stride + x0 * 4,
                        dst + (long)y * stride + x0 * 4, bytes, bytes);
            }
        }
        _bitmap.NotifyPixelsChanged();

        FrameScrollX = meta.ScrollX; FrameScrollY = meta.ScrollY;
        FrameSelectOpen = meta.SelectOpen;
        ContentW = meta.ContentW; ContentH = meta.ContentH;
        DomCount = meta.DomCount; BoxCount = meta.BoxCount;
        PageBg = new SKColor(meta.PageBgRgba);
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        Proc.Dispose();
    }
}
