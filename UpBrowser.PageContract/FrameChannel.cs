using IO = System.IO;

namespace UpBrowser.PageContract;

/// <summary>Slot state machine. Only the page host writes pixels; only the shell marks a slot
/// as being drawn. Both sides move a slot through <see cref="Interlocked"/> comparisons.</summary>
public enum FrameSlotState : uint
{
    Free = 0,
    /// <summary>Leased by the host for rasterization.</summary>
    Writing = 1,
    /// <summary>Published: pixels and metadata are complete and readable.</summary>
    Ready = 2,
    /// <summary>Leased by the shell; the host must not overwrite it yet.</summary>
    ShellDrawing = 3,
}

/// <summary>An axis-aligned damage rectangle in device pixels.</summary>
public readonly record struct DamageRect(int X, int Y, int Width, int Height);

/// <summary>Per-frame metadata published alongside a slot's pixels.</summary>
public struct FrameMeta
{
    public FrameMode Mode;
    public float ScrollX, ScrollY;
    public float ContentW, ContentH;
    public uint PageBgRgba;
    public int DomCount, BoxCount;
    /// <summary>Device-row shift the host applied for a scroll-only frame (ScrollBlit).</summary>
    public int ScrollDy;
    /// <summary>
    /// Caret of the focused editable, in DOCUMENT space (no scroll, no chrome offset):
    /// the shell scrolls speculatively, so it transforms these with its own live scroll
    /// when it positions the IME candidate window. Zero when there is no focus.
    /// </summary>
    public float CaretX, CaretY, CaretH;
    /// <summary>True when the page's focused element accepts composed text.</summary>
    public bool HasEditableFocus;
    /// <summary>True when that element is a password field — the shell must block IME there.</summary>
    public bool IsPassword;
}

/// <summary>
/// Shared-memory frame channel between the shell and one page host.
///
/// The host creates the mapping and rasterizes straight into a slot; the shell opens it by
/// name and draws it without a copy. Only a small commit travels over the control pipe, so a
/// viewport-sized frame no longer costs a viewport-sized memcpy on each side of the boundary.
///
/// Layout, little-endian, offsets fixed at construction:
/// <code>
///   header (32B)  0 magic  4 version  8 slotCount  12 maxRects  16 stride  20 width  24 height
///   slot (80B)    0 state  4 seq  12 mode  16 scrollX  20 scrollY  24 contentW
///   descriptor    28 contentH  32 pageBgRgba  36 domCount  40 boxCount  44 scrollDy  48 rectCount
///                 52 caretX  56 caretY  60 caretH  64 imeFlags
///   then rects[maxRects] as {i32 x, i32 y, i32 w, i32 h}
///   then stride × height bytes of BGRA8888 premultiplied pixels
/// </code>
/// Slot sizes round up to a multiple of 8 so every descriptor — and the 8-byte sequence inside
/// it — stays aligned.
/// </summary>
public sealed unsafe class FrameChannel : IDisposable
{
    public const uint Magic = 0x4642_5055;   // 'UPBF'
    public const uint LayoutVersion = 2;
    public const int SlotCount = 4;

    /// <summary>imeFlags descriptor bit: the focused element accepts composed text.</summary>
    public const uint MetaFlagHasEditableFocus = 1u << 0;
    /// <summary>imeFlags descriptor bit: the focused editable is a password field.</summary>
    public const uint MetaFlagIsPassword = 1u << 1;

    private const int HeaderBytes = 32;
    private const int SlotDescriptorBytes = 80;
    private const int RectBytes = 16;

    private readonly IO.MemoryMappedFiles.MemoryMappedFile _mmf;
    private readonly IO.MemoryMappedFiles.MemoryMappedViewAccessor _view;
    private readonly byte* _base;
    private readonly int _maxRects;
    private readonly int _pixelsInSlot;
    private readonly long _slotBytes;
    private long _publishedSeq;
    private bool _disposed;

    /// <summary>Bytes in one row of the backing surface.</summary>
    public int Stride { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>Name the shell opens; unique per mapping generation.</summary>
    public string Name { get; }

    private FrameChannel(IO.MemoryMappedFiles.MemoryMappedFile mmf,
        IO.MemoryMappedFiles.MemoryMappedViewAccessor view, byte* basePtr,
        string name, int width, int height, int stride, int maxRects)
    {
        _mmf = mmf; _view = view; _base = basePtr;
        Name = name; Width = width; Height = height; Stride = stride; _maxRects = maxRects;
        _pixelsInSlot = SlotDescriptorBytes + maxRects * RectBytes;
        _slotBytes = (_pixelsInSlot + (long)stride * height + 7) & ~7L;
    }

    public static long TotalBytes(int stride, int height, int maxRects)
    {
        long payload = SlotDescriptorBytes + (long)maxRects * RectBytes + (long)stride * height;
        return HeaderBytes + SlotCount * ((payload + 7) & ~7L);
    }

    /// <summary>Create the mapping. Ownership is procedural: the host creates, the shell opens.</summary>
    public static FrameChannel Create(string name, int widthPx, int heightPx, int maxRects = 64)
    {
        int stride = Math.Max(1, widthPx) * 4;
        int h = Math.Max(1, heightPx);
        long total = TotalBytes(stride, h, maxRects);
        var mmf = OpenMapping(name, total, createNew: true);
        var view = mmf.CreateViewAccessor(0, total, IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
        byte* p = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        if (p == null) throw new InvalidOperationException("frame mapping could not be mapped");
        var ch = new FrameChannel(mmf, view, p, name, widthPx, h, stride, maxRects);
        ch.WriteHeader();
        ch.ResetSlots();
        return ch;
    }

    /// <summary>Open a mapping the host created. Null when it is missing or does not match.</summary>
    public static FrameChannel? Open(string name, int maxRects = 64)
    {
        IO.MemoryMappedFiles.MemoryMappedFile mmf;
        try
        {
            mmf = OpenMapping(name, 0, createNew: false);
        }
        catch (IO.FileNotFoundException) { return null; }
        catch (PlatformNotSupportedException) { return null; }

        // Length 0 maps to the end of the section, so the host's size is authoritative.
        var view = mmf.CreateViewAccessor(0, 0, IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
        byte* p = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);

        FrameChannel? Reject()
        {
            if (p != null) { try { view.SafeMemoryMappedViewHandle.ReleasePointer(); } catch { } }
            view.Dispose();
            mmf.Dispose();
            return null;
        }

        if (p == null) return Reject();
        if (*(uint*)(p + 0) != Magic || *(uint*)(p + 4) != LayoutVersion ||
            *(int*)(p + 8) != SlotCount)
            return Reject();

        int rects = *(int*)(p + 12);
        int stride = (int)(*(uint*)(p + 16));
        int w = *(int*)(p + 20);
        int hgt = *(int*)(p + 24);
        if (rects > maxRects || stride <= 0 || w <= 0 || hgt <= 0)
            return Reject();

        return new FrameChannel(mmf, view, p, name, w, hgt, stride, rects);
    }

    /// <summary>
    /// Windows uses a named section. POSIX has no named shared memory in
    /// <see cref="IO.MemoryMappedFiles"/>, so the mapping is a file in the temp directory that
    /// both processes open by the same name and the host deletes on teardown.
    /// </summary>
    private static IO.MemoryMappedFiles.MemoryMappedFile OpenMapping(string name, long capacity, bool createNew)
    {
        if (OperatingSystem.IsWindows())
        {
            return createNew
                ? IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(name, capacity,
                    IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite)
                : IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(name);
        }

        string path = FilePathFor(name)!;
        if (!createNew && !File.Exists(path))
            throw new IO.FileNotFoundException("frame mapping not found", path);
        return IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(path, IO.FileMode.OpenOrCreate, null,
            createNew ? capacity : 0, IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
    }

    /// <summary>Backing file on POSIX; null on Windows, where the mapping is a named section.</summary>
    public static string? FilePathFor(string name) =>
        OperatingSystem.IsWindows() ? null : IO.Path.Combine(IO.Path.GetTempPath(), name);

    private void WriteHeader()
    {
        *(uint*)(_base + 0) = Magic;
        *(uint*)(_base + 4) = LayoutVersion;
        *(uint*)(_base + 8) = SlotCount;
        *(uint*)(_base + 12) = (uint)_maxRects;
        *(uint*)(_base + 16) = (uint)Stride;
        *(int*)(_base + 20) = Width;
        *(int*)(_base + 24) = Height;
    }

    private byte* SlotPtr(int slot) => _base + HeaderBytes + slot * _slotBytes;
    private ref uint StateRef(int slot) => ref *(uint*)SlotPtr(slot);
    private ref ulong SeqRef(int slot) => ref *(ulong*)(SlotPtr(slot) + 4);

    /// <summary>Writable pixel origin of a slot the caller has claimed.</summary>
    public byte* PixelPointer(int slot) => SlotPtr(slot) + _pixelsInSlot;

    /// <summary>Highest published sequence currently readable.</summary>
    public ulong NewestPublishedSeq
    {
        get
        {
            ulong best = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if ((FrameSlotState)Volatile.Read(ref StateRef(i)) != FrameSlotState.Ready) continue;
                ulong s = Volatile.Read(ref SeqRef(i));
                if (s > best) best = s;
            }
            return best;
        }
    }

    // ---------------- host side ----------------

    /// <summary>
    /// Claim a slot to raster into. A slot the shell is still drawing is left alone; a published
    /// slot the shell never got to is reclaimed, since newer content replaces it anyway.
    /// </summary>
    public int AcquireForWriting()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            uint expected = (uint)FrameSlotState.Free;
            if (Interlocked.CompareExchange(ref StateRef(i), (uint)FrameSlotState.Writing, expected) == expected)
                return i;
        }
        for (int i = 0; i < SlotCount; i++)
        {
            uint expected = (uint)FrameSlotState.Ready;
            if (Interlocked.CompareExchange(ref StateRef(i), (uint)FrameSlotState.Writing, expected) == expected)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Claim a specific slot, but only from Free. The host uses this to continue a raster in
    /// the slot it last published: Free proves the shell applied and released it, so an
    /// incremental (scroll-shifted) chain never publishes on top of a frame the shell has
    /// not folded. Returns false while the shell has the slot unpublished (Ready) or is
    /// drawing it (ShellDrawing).
    /// </summary>
    public bool TryClaimForWriting(int slot)
    {
        if (slot < 0 || slot >= SlotCount) return false;
        uint expected = (uint)FrameSlotState.Free;
        return Interlocked.CompareExchange(ref StateRef(slot), (uint)FrameSlotState.Writing, expected) == expected;
    }

    /// <summary>Give up a claim without publishing (nothing needed sending this pass).</summary>
    public void ReleaseForWriting(int slot)
    {
        if (slot >= 0) Volatile.Write(ref StateRef(slot), (uint)FrameSlotState.Free);
    }

    /// <summary>
    /// Publish a claimed slot. The sequence is written before the state flips to Ready, so a
    /// reader that observes a new sequence also observes complete pixels and metadata.
    /// The published sequence is returned so the host can pair it with its commit message.
    /// </summary>
    public ulong Publish(int slot, in FrameMeta meta, ReadOnlySpan<DamageRect> rects)
    {
        byte* s = SlotPtr(slot);
        *(uint*)(s + 12) = (uint)meta.Mode;
        *(float*)(s + 16) = meta.ScrollX;
        *(float*)(s + 20) = meta.ScrollY;
        *(float*)(s + 24) = meta.ContentW;
        *(float*)(s + 28) = meta.ContentH;
        *(uint*)(s + 32) = meta.PageBgRgba;
        *(int*)(s + 36) = meta.DomCount;
        *(int*)(s + 40) = meta.BoxCount;
        *(int*)(s + 44) = meta.ScrollDy;
        *(float*)(s + 52) = meta.CaretX;
        *(float*)(s + 56) = meta.CaretY;
        *(float*)(s + 60) = meta.CaretH;
        uint imeFlags = 0;
        if (meta.HasEditableFocus) imeFlags |= MetaFlagHasEditableFocus;
        if (meta.IsPassword) imeFlags |= MetaFlagIsPassword;
        *(uint*)(s + 64) = imeFlags;
        int n = Math.Min(rects.Length, _maxRects);
        *(uint*)(s + 48) = (uint)n;
        for (int i = 0; i < n; i++)
        {
            byte* r = s + SlotDescriptorBytes + i * RectBytes;
            *(int*)(r + 0) = rects[i].X;
            *(int*)(r + 4) = rects[i].Y;
            *(int*)(r + 8) = rects[i].Width;
            *(int*)(r + 12) = rects[i].Height;
        }
        ulong seq = (ulong)Interlocked.Increment(ref _publishedSeq);
        Volatile.Write(ref SeqRef(slot), seq);
        Thread.MemoryBarrier();
        Volatile.Write(ref StateRef(slot), (uint)FrameSlotState.Ready);
        return seq;
    }

    private FrameMeta ReadMeta(byte* s) => new FrameMeta
    {
        Mode = (FrameMode)(*(uint*)(s + 12)),
        ScrollX = *(float*)(s + 16),
        ScrollY = *(float*)(s + 20),
        ContentW = *(float*)(s + 24),
        ContentH = *(float*)(s + 28),
        PageBgRgba = *(uint*)(s + 32),
        DomCount = *(int*)(s + 36),
        BoxCount = *(int*)(s + 40),
        ScrollDy = *(int*)(s + 44),
        CaretX = *(float*)(s + 52),
        CaretY = *(float*)(s + 56),
        CaretH = *(float*)(s + 60),
        HasEditableFocus = (*(uint*)(s + 64) & MetaFlagHasEditableFocus) != 0,
        IsPassword = (*(uint*)(s + 64) & MetaFlagIsPassword) != 0,
    };

    private DamageRect[] ReadRects(byte* s)
    {
        int n = (int)*(uint*)(s + 48);
        var rects = new DamageRect[n];
        for (int i = 0; i < n; i++)
        {
            byte* r = s + SlotDescriptorBytes + i * RectBytes;
            rects[i] = new DamageRect(*(int*)(r + 0), *(int*)(r + 4), *(int*)(r + 8), *(int*)(r + 12));
        }
        return rects;
    }

    /// <summary>
    /// Read-only peek of a published (Ready) slot by sequence — no lease taken. The host may
    /// reclaim the slot at any time, so callers may only trust the descriptor metadata (which
    /// is what a pipe commit refers to), never the pixels. The shell uses this to attach
    /// metadata to a commit without disturbing its newest-wins lease protocol.
    /// </summary>
    public bool TryPeekPublished(ulong seq, out FrameMeta meta, out DamageRect[] rects)
    {
        meta = default; rects = Array.Empty<DamageRect>();
        for (int i = 0; i < SlotCount; i++)
        {
            if ((FrameSlotState)Volatile.Read(ref StateRef(i)) != FrameSlotState.Ready) continue;
            if (Volatile.Read(ref SeqRef(i)) != seq) continue;
            byte* s = SlotPtr(i);
            meta = ReadMeta(s);
            rects = ReadRects(s);
            return true;
        }
        return false;
    }

    // ---------------- shell side ----------------

    /// <summary>
    /// Take the newest slot published after <paramref name="lastSeq"/>. The caller must
    /// <see cref="ReleaseRead"/> once it has finished drawing; the slot's pixels stay valid
    /// until then, which is what lets the shell draw them without copying.
    /// </summary>
    public bool TryAcquireNewest(ulong lastSeq, out int slot, out ulong seq,
        out FrameMeta meta, out DamageRect[] rects)
    {
        slot = -1; seq = lastSeq; meta = default; rects = Array.Empty<DamageRect>();
        int best = -1;
        ulong bestSeq = lastSeq;
        for (int i = 0; i < SlotCount; i++)
        {
            if ((FrameSlotState)Volatile.Read(ref StateRef(i)) != FrameSlotState.Ready) continue;
            ulong s = Volatile.Read(ref SeqRef(i));
            if (s > bestSeq) { bestSeq = s; best = i; }
        }
        if (best < 0) return false;
        if (Interlocked.CompareExchange(ref StateRef(best), (uint)FrameSlotState.ShellDrawing,
                (uint)FrameSlotState.Ready) != (uint)FrameSlotState.Ready)
            return false;

        byte* s2 = SlotPtr(best);
        meta = ReadMeta(s2);
        rects = ReadRects(s2);
        slot = best;
        seq = bestSeq;
        return true;
    }

    public void ReleaseRead(int slot)
    {
        if (slot < 0) return;
        Volatile.Write(ref StateRef(slot), (uint)FrameSlotState.Free);
    }

    /// <summary>Drop every slot back to free — used when a generation is (re)started.</summary>
    public void ResetSlots()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            Volatile.Write(ref SeqRef(i), 0UL);
            Volatile.Write(ref StateRef(i), (uint)FrameSlotState.Free);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _view.SafeMemoryMappedViewHandle.ReleasePointer(); } catch { }
        _view.Dispose();
        _mmf.Dispose();
        var path = FilePathFor(Name);
        if (path != null) { try { File.Delete(path); } catch { } }
    }
}
