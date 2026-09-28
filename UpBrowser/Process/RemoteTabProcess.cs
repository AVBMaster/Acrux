using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using UpBrowser.PageContract;

namespace UpBrowser.Process;

/// <summary>
/// Parent-side handle for one tab-host child process. Launches
/// `UpBrowser --tab-host`, serves the duplex pipe, forwards commands and
/// receives frame commits. All page state (DOM/layout/JS) lives in the
/// child; frame pixels live in the shared-memory <see cref="FrameChannel"/>
/// the child announces over the pipe. This side only owns the mapping handle
/// plus the metadata of the most recent commit, and leases slots to the
/// compositor (<see cref="RemoteTabView"/>) so a wedge or death of the host
/// never strands the shell mid-read.
/// Inbound messages are parsed into a reusable buffer — no message body is
/// referenced after parsing returns, so nothing needs to stay stable.
/// </summary>
internal sealed class RemoteTabProcess : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly System.Diagnostics.Process? _child;
    private readonly object _writeLock = new();
    private readonly Thread _reader;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _connected;

    private byte[] _rxBuf = new byte[16 * 1024];

    // Guarded by _frameLock: the current frame mapping (+ one generation back,
    // disposed only when a newer Hello supersedes it, never while the compositor
    // could still be copying from it), and the metadata of the newest frame.
    private readonly object _frameLock = new();
    private FrameChannel? _frameChannel;
    private FrameChannel? _staleChannel;
    private int _channelVersion;
    private ulong _lastCommitSeq;
    private long _lastDamageBytes;
    private ulong _acqSeq;          // newest frame folded by the compositor
    private FrameMeta _acqMeta;
    private ulong _peekSeq;         // newest frame peeked from a commit
    private FrameMeta _peekMeta;
    private long _frameVersion;

    /// <summary>Lock protecting the frame mapping. The compositor holds it for the whole
    /// acquire → fold → release sequence; nothing expensive runs under it otherwise.</summary>
    public object FrameSync => _frameLock;

    /// <summary>Bumped on every Hello: a new frame generation resets the compositor's
    /// sequence tracking (each mapping numbers its slots from one).</summary>
    public int ChannelVersion
    {
        get { lock (_frameLock) return _channelVersion; }
    }

    public int TabIndex { get; }
    public int ChildPid => _child?.Id ?? -1;
    public string PipeName { get; }
    public bool IsConnected => _connected;
    public volatile bool IsDead;
    public volatile bool Loading;
    public volatile string Title = "";
    public volatile string Url = "";

    // Liveness bookkeeping for the shell's watchdog. The child beats at its own loop
    // stage boundaries, so the age of the last beat is the only honest liveness signal.
    public long LastBeatTick = Environment.TickCount64;
    private long _lastInboundTick;
    private long _lastOutboundTick = Environment.TickCount64;
    public long HeartbeatSeq;
    public volatile int PendingCommands;

    /// <summary>True while we have asked the page for something it has not reacted to yet.</summary>
    public bool AwaitingPage => _lastOutboundTick > Volatile.Read(ref _lastInboundTick);

    /// <summary>Milliseconds since the child last proved its loop is turning.</summary>
    public long SilentMs => Environment.TickCount64 - Volatile.Read(ref LastBeatTick);

    public PageResponsiveness Responsiveness =>
        IsDead ? PageResponsiveness.Dead
        : !_connected ? PageResponsiveness.Healthy
        : ResponsivenessEvaluator.Evaluate(SilentMs, AwaitingPage, IsDead);

    // ---- how this host died ----
    // "Who ended it" matters as much as "it ended": a watchdog kill and a page fault must not
    // be charged to the same budget, and a host that vanishes without a word is its own case.
    private int _deathDeclared;
    private volatile string? _faultDetail;
    private volatile bool _killRequested;
    private volatile bool _closeRequested;
    public CrashKind DeathKind { get; private set; } = CrashKind.CleanExit;
    public string DeathDetail { get; private set; } = "";
    /// <summary>Raised once, on whichever thread learns of the death first.</summary>
    public event Action<CrashKind, string>? OnDied;

    private CrashKind ClassifyDeath()
    {
        if (_closeRequested) return CrashKind.CleanExit;
        if (_killRequested) return CrashKind.HangKilled;
        if (!string.IsNullOrEmpty(_faultDetail)) return CrashKind.Faulted;
        int? code = null;
        try { if (_child is { HasExited: true }) code = _child.ExitCode; } catch { }
        return code switch
        {
            null => CrashKind.KilledOrCrashedNative,
            0 => CrashKind.CleanExit,
            2 => CrashKind.Faulted,          // our own entry-point catch-all exit code
            _ => CrashKind.KilledOrCrashedNative,
        };
    }

    private void DeclareDead()
    {
        IsDead = true;
        if (Interlocked.CompareExchange(ref _deathDeclared, 1, 0) != 0) return;
        var kind = ClassifyDeath();
        DeathKind = kind;
        DeathDetail = _faultDetail ?? (kind == CrashKind.KilledOrCrashedNative
            ? "host exited without reporting a fault" : kind.ToString());
        Console.WriteLine($"[RemoteTab {TabIndex}] died: {kind} — {DeathDetail}");
        OnDied?.Invoke(kind, DeathDetail);
    }

    /// <summary>End a wedged host. Deliberate, so it costs the tab nothing from its budget.</summary>
    public void KillForHang()
    {
        _killRequested = true;
        try { if (_child is { HasExited: false }) _child.Kill(entireProcessTree: true); } catch { }
    }

    private void OnChildExited(object? sender, EventArgs e)
    {
        // The pipe usually closes first and already declared this; the event is the backstop
        // for a host that dies while the pipe lingers.
        DeclareDead();
    }

    /// <summary>Page background (RGBA packed) carried in every published frame's metadata.</summary>
    public uint PageBgRgba { get; private set; } = 0xFFFFFFFF;

    /// <summary>Raised on the reader thread when a new frame arrives.</summary>
    public event Action? OnFrameArrived;
    /// <summary>Raised on the reader thread for title/URL/dialog updates.</summary>
    public event Action<TabMsg, string, string>? OnSignal;
    /// <summary>Child-initiated scroll (window.scrollTo / anchors) — parent syncs its scrollbar.</summary>
    public event Action<float, float>? OnScrollChanged;
    /// <summary>Reply to the last Wheel: true when an element scroller consumed it.</summary>
    public event Action<bool>? OnWheelResult;
    /// <summary>The page opened a modal (alert/confirm/prompt) and its script thread is
    /// blocked until <see cref="RespondDialog"/> answers <paramref name="requestId"/>.</summary>
    public event Action<int, string, string>? OnDialogRequest;

    // Selected-text RPC: correlation id -> callback. Responses for unknown or already
    // answered ids are dropped, so a stale reply can never fire (or wedge) a later request.
    private int _selectedTextSeq;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Action<string>> _selectedTextCallbacks = new();

    public RemoteTabProcess(int tabIndex, string initialUrl, float dpiScale, float resolutionScale,
        bool hang = false, bool fault = false)
    {
        TabIndex = tabIndex;
        PipeName = TabPipeNames.ForTab(tabIndex);
        _pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            DeclareDead();
            _reader = null!;
            return;
        }

        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--tab-host");
            psi.ArgumentList.Add($"--channel={PipeName}");
            psi.ArgumentList.Add($"--tab={tabIndex}");
            psi.ArgumentList.Add($"--dpi={dpiScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
            psi.ArgumentList.Add($"--res={resolutionScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
            if (hang) psi.ArgumentList.Add("--hang");
            if (fault) psi.ArgumentList.Add("--fault");
            _child = System.Diagnostics.Process.Start(psi);
            if (_child != null)
            {
                _child.EnableRaisingEvents = true;
                _child.Exited += OnChildExited;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteTab {tabIndex}] launch failed: {ex.Message}");
            DeclareDead();
        }

        _reader = new Thread(ReaderLoop)
        {
            Name = $"RemoteTab-{tabIndex}",
            IsBackground = true
        };
        _reader.Start();
    }

    private void ReaderLoop()
    {
        try
        {
            var waited = Task.Run(() => _pipe.WaitForConnectionAsync(_cts.Token)).Wait(TimeSpan.FromSeconds(10));
            if (!waited || !_pipe.IsConnected)
            {
                DeclareDead();
                _connected = false;
                return;
            }
            _connected = true;

            var header = new byte[5];
            while (!_cts.IsCancellationRequested)
            {
                if (!ReadExact(header, 5)) break;
                int len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
                var kind = (TabMsg)header[4];
                if (len < 0 || len > 256 * 1024 * 1024) break;

                // Frame pixels no longer cross the pipe, so payloads stay small;
                // Handle copies out anything it keeps, so one buffer is enough.
                if (_rxBuf.Length < Math.Max(len, 1))
                    _rxBuf = new byte[Math.Max(len, 16 * 1024)];
                if (len > 0 && !ReadExact(_rxBuf, len)) break;

                Handle(kind, _rxBuf, 0, len);
            }
        }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested)
                Console.WriteLine($"[RemoteTab {TabIndex}] reader: {ex.Message}");
        }
        finally
        {
            if (!_cts.IsCancellationRequested)
            {
                DeclareDead();
                _connected = false;
            }
        }
    }

    private bool ReadExact(byte[] buffer, int count)
    {
        int off = 0;
        try
        {
            while (off < count)
            {
                int n = _pipe.Read(buffer, off, count - off);
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    private void Handle(TabMsg kind, byte[] buf, int off, int len)
    {
        // Only a page-level reaction clears "awaiting": a handshake or a bare heartbeat says
        // the host is alive, not that it did what we asked. Counting Ready as progress made a
        // navigation issued during the handshake window invisible to the watchdog.
        if (IsPageProgress(kind)) Volatile.Write(ref _lastInboundTick, Environment.TickCount64);
        switch (kind)
        {
            case TabMsg.Hello:
            {
                // [int proto][ulong caps][int pid][string frameName][int frameW][int frameH]
                // — handshake and frame-generation announcement in one message. Re-sent by
                // the host whenever the viewport size changes; the old mapping is only
                // disposed once a NEWER Hello supersedes its replacement, so a compositor
                // mid-fold on a previous generation is never pulled out from under it.
                int p = off;
                int proto = ReadI(buf, ref p);
                ulong caps = ReadUL(buf, ref p);
                int pid = ReadI(buf, ref p);
                var frameName = ReadStr(buf, ref p, len);
                int fw = ReadI(buf, ref p);
                int fh = ReadI(buf, ref p);
                if (proto != PageProtocol.Version)
                    Console.WriteLine($"[RemoteTab {TabIndex}] host proto {proto} != {PageProtocol.Version}");
                if ((caps & (ulong)PageProtocol.Capabilities.ShmFrames) == 0 || string.IsNullOrEmpty(frameName))
                {
                    Console.WriteLine($"[RemoteTab {TabIndex}] host announced no frame mapping (caps={caps:x})");
                    break;
                }
                bool opened;
                lock (_frameLock)
                {
                    try { _staleChannel?.Dispose(); } catch { }
                    _staleChannel = _frameChannel;
                    _frameChannel = FrameChannel.Open(frameName);
                    // Fresh sequence space: the generation counter restarts at one, and
                    // so must everyone tracking "newest seen".
                    _channelVersion++;
                    _lastCommitSeq = 0;
                    _lastDamageBytes = 0;
                    _acqSeq = 0;
                    _peekSeq = 0;
                    opened = _frameChannel != null;
                }
                Console.WriteLine($"[RemoteTab {TabIndex}] frame mapping '{frameName}' {fw}x{fh} " +
                                  $"pid={pid} caps={caps:x} opened={opened}");
                break;
            }
            case TabMsg.FrameCommit:
            {
                // [ulong seq][long damageBytes] — slot `seq` is Ready in the current
                // mapping. The pixels travel shared-memory; we peek the descriptor for
                // metadata (the host only recycles a slot by overwriting it wholesale,
                // so a peeked descriptor is either this frame's or newer — and the
                // compositor leases the slot itself for the pixels it folds).
                int p = off;
                ulong seq = ReadUL(buf, ref p);
                long damage = ReadL(buf, ref p);
                bool haveChannel;
                lock (_frameLock)
                {
                    _lastCommitSeq = seq;
                    _lastDamageBytes = damage;
                    if (_frameChannel != null &&
                        _frameChannel.TryPeekPublished(seq, out var pm, out _))
                    {
                        _peekSeq = seq;
                        _peekMeta = pm;
                        PageBgRgba = pm.PageBgRgba;
                    }
                    haveChannel = _frameChannel != null;
                    _frameVersion++;
                }
                if (!haveChannel)
                    Console.WriteLine($"[RemoteTab {TabIndex}] frame commit before Hello — dropped");
                OnFrameArrived?.Invoke();
                break;
            }
            case TabMsg.ScrollChanged:
            {
                int p = off;
                float x = ReadF(buf, ref p);
                float y = ReadF(buf, ref p);
                OnScrollChanged?.Invoke(x, y);
                break;
            }
            case TabMsg.Title:
            {
                Title = ReadStr(buf, ref off, len);
                OnSignal?.Invoke(TabMsg.Title, Title, "");
                break;
            }
            case TabMsg.UrlChanged:
            {
                Url = ReadStr(buf, ref off, len);
                OnSignal?.Invoke(TabMsg.UrlChanged, Url, "");
                break;
            }
            case TabMsg.Loading:
                Loading = len > 0 && buf[off] != 0;
                break;
            case TabMsg.WheelResult:
                OnWheelResult?.Invoke(len > 0 && buf[off] != 0);
                break;
            case TabMsg.DialogRequest:
            {
                int p = off;
                int id = ReadI(buf, ref p);
                var message = ReadStr(buf, ref p, len);
                var type = ReadStr(buf, ref p, len);
                OnDialogRequest?.Invoke(id, message, type);
                break;
            }
            case TabMsg.SelectedTextResponse:
            {
                // [int requestId][string text] — deliver only to the callback that owns
                // this correlation id; anything else is stale and silently dropped.
                int p = off;
                int id = ReadI(buf, ref p);
                var text = ReadStr(buf, ref p, len);
                if (_selectedTextCallbacks.TryRemove(id, out var cb)) cb(text);
                break;
            }
            case TabMsg.Heartbeat:
            {
                int p = off;
                HeartbeatSeq = ReadL(buf, ref p);
                PendingCommands = ReadI(buf, ref p);
                Volatile.Write(ref LastBeatTick, Environment.TickCount64);
                break;
            }
            case TabMsg.CrashReport:
            {
                int p = off;
                var stage = ReadStr(buf, ref p, len);
                var detail = ReadStr(buf, ref p, len);
                // The child's detail already leads with its stage; prefixing again duplicated it.
                _faultDetail = string.IsNullOrEmpty(stage) || detail.StartsWith(stage + ":", StringComparison.Ordinal)
                    ? detail
                    : $"{stage}: {detail}";
                break;
            }
            case TabMsg.Ready:
                Console.WriteLine($"[RemoteTab {TabIndex}] child ready");
                break;
        }
    }

    private static bool IsPageProgress(TabMsg kind) => kind switch
    {
        TabMsg.FrameCommit or TabMsg.Title or TabMsg.UrlChanged or TabMsg.Loading or TabMsg.ScrollChanged
            or TabMsg.WheelResult or TabMsg.DialogRequest or TabMsg.MetricsReport or TabMsg.SelectedTextResponse => true,
        _ => false,
    };

    private static int ReadI(byte[] b, ref int p)
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p, 4));
        p += 4;
        return v;
    }

    private static long ReadL(byte[] b, ref int p)
    {
        long v = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(p, 8));
        p += 8;
        return v;
    }

    private static ulong ReadUL(byte[] b, ref int p)
    {
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(p, 8));
        p += 8;
        return v;
    }

    private static float ReadF(byte[] b, ref int p)
    {
        float v = BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(p, 4));
        p += 4;
        return v;
    }

    private static string ReadStr(byte[] b, ref int p, int len)
    {
        int slen = ReadI(b, ref p);
        if (slen <= 0 || p + slen > len) return "";
        var s = System.Text.Encoding.UTF8.GetString(b, p, slen);
        p += slen;
        return s;
    }

    private void Send(TabMsg type, Action<BinaryWriter> body)
    {
        if (!_connected || IsDead) return;
        // Commands the page must react to arm the watchdog; housekeeping does not.
        switch (type)
        {
            case TabMsg.Navigate or TabMsg.NavigateHtml or TabMsg.MouseDown or TabMsg.MouseUp
                or TabMsg.MouseMove or TabMsg.Wheel or TabMsg.Resize or TabMsg.KeyDown
                or TabMsg.Char or TabMsg.DialogResult:
                Volatile.Write(ref _lastOutboundTick, Environment.TickCount64);
                break;
        }
        var payload = TabFraming.BuildPayload(body);
        try
        {
            lock (_writeLock)
                TabFraming.Write(_pipe, type, payload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteTab {TabIndex}] send failed: {ex.Message}");
            DeclareDead();
        }
    }

    public void Navigate(string url) => Send(TabMsg.Navigate, w => TabFraming.WriteString(w, url));
    public void NavigateHtml(string html, string baseUrl) => Send(TabMsg.NavigateHtml, w =>
    {
        TabFraming.WriteString(w, html);
        TabFraming.WriteString(w, baseUrl);
    });
    public void MouseDown(float x, float y) => Send(TabMsg.MouseDown, w => { w.Write(x); w.Write(y); });
    public void MouseUp(float x, float y) => Send(TabMsg.MouseUp, w => { w.Write(x); w.Write(y); });
    public void MouseMove(float x, float y) => Send(TabMsg.MouseMove, w => { w.Write(x); w.Write(y); });
    public void Wheel(float dx, float dy, float x, float y) => Send(TabMsg.Wheel, w => { w.Write(dx); w.Write(dy); w.Write(x); w.Write(y); });
    public void Resize(float width, float height) => Send(TabMsg.Resize, w => { w.Write(width); w.Write(height); });
    public void SetActive(bool active) => Send(TabMsg.SetActive, w => w.Write((byte)(active ? 1 : 0)));
    public void ScrollTo(float x, float y) => Send(TabMsg.ScrollTo, w => { w.Write(x); w.Write(y); });
    public void KeyDown(ushort charCode, ushort key, bool repeat) => Send(TabMsg.KeyDown, w => { w.Write(charCode); w.Write(key); w.Write((byte)(repeat ? 1 : 0)); });
    public void Char(ushort charCode) => Send(TabMsg.Char, w => w.Write(charCode));
    /// <summary>Hand the user's answer back so the page's script thread resumes.</summary>
    public void RespondDialog(int requestId, bool accepted, string? text) =>
        Send(TabMsg.DialogResult, w =>
        {
            w.Write(requestId);
            w.Write((byte)(accepted ? 1 : 0));
            TabFraming.WriteString(w, text ?? "");
        });

    /// <summary>
    /// Ask the page host for its current text selection. Non-blocking: returns immediately,
    /// and <paramref name="callback"/> runs (on the pipe reader thread) when the response
    /// carrying this request's correlation id arrives. Callers that need the UI thread
    /// marshal themselves. No callback fires for a host that never answers.
    /// </summary>
    public void RequestSelectedText(Action<string> callback)
    {
        int id = Interlocked.Increment(ref _selectedTextSeq);
        _selectedTextCallbacks[id] = callback;
        Send(TabMsg.SelectedTextRequest, w => w.Write(id));
    }

    /// <summary>Test hook: send a SelectedTextRequest with an arbitrary correlation id and
    /// no registered callback, so the parent's id matching can be probed end-to-end.</summary>
    internal void ProbeSelectedTextRequest(int requestId) =>
        Send(TabMsg.SelectedTextRequest, w => w.Write(requestId));

    public long FrameVersion
    {
        get { lock (_frameLock) return _frameVersion; }
    }

    /// <summary>
    /// Metadata of the newest frame the shell knows about, without leasing a slot. Pixels
    /// live in the shared mapping now, so this reports the frame the last commit referred
    /// to — its descriptor was either peeked straight from the (still Ready) slot on
    /// arrival, or cached from the compositor's last fold after the host recycled it —
    /// plus the damage volume the commit carried. Tests and the watchdog follow frame
    /// content through this instead of a pipe packet.
    /// </summary>
    public bool TryGetFrame(out FrameMode mode, out int w, out int h,
        out float scrollX, out float scrollY, out float contentW, out float contentH,
        out int domCount, out int boxCount, out long damageBytes, out long version)
    {
        mode = FrameMode.Full; w = 0; h = 0;
        scrollX = scrollY = contentW = contentH = 0;
        domCount = boxCount = 0; damageBytes = 0; version = 0;
        lock (_frameLock)
        {
            version = _frameVersion;
            if (_frameChannel == null || _lastCommitSeq == 0) return false;
            w = _frameChannel.Width; h = _frameChannel.Height;
            var m = _peekSeq >= _acqSeq ? _peekMeta : _acqMeta;
            mode = m.Mode;
            scrollX = m.ScrollX; scrollY = m.ScrollY;
            contentW = m.ContentW; contentH = m.ContentH;
            domCount = m.DomCount; boxCount = m.BoxCount;
            damageBytes = _lastDamageBytes;
            return true;
        }
    }

    /// <summary>
    /// Lease the newest slot the compositor has not folded yet, straight out of the shared
    /// mapping — no packet, no copy, no pipe round-trip. The slot's pixels stay valid until
    /// <see cref="ReleaseFrame"/> and the host will not raster a shifted frame over one the
    /// shell has not applied, so folding newest-wins never skips chain state. The caller
    /// must hold <see cref="FrameSync"/> for the whole acquire → copy → release sequence.
    /// </summary>
    public bool TryAcquireFrame(ulong lastSeq, out FrameChannel? channel, out int slot,
        out ulong seq, out FrameMeta meta, out DamageRect[] rects)
    {
        channel = null; slot = -1; seq = lastSeq; meta = default; rects = Array.Empty<DamageRect>();
        var ch = _frameChannel;
        if (ch == null) return false;
        if (!ch.TryAcquireNewest(lastSeq, out slot, out seq, out meta, out rects))
        {
            slot = -1; seq = lastSeq; meta = default; rects = Array.Empty<DamageRect>();
            return false;
        }
        channel = ch;
        _acqSeq = seq;
        _acqMeta = meta;
        return true;
    }

    /// <summary>Hand a leased slot back to the host (its release is the host's cue that a
    /// shifted continuation frame may be published). Caller holds <see cref="FrameSync"/>.</summary>
    public void ReleaseFrame(FrameChannel channel, int slot) => channel.ReleaseRead(slot);

    /// <summary>Cached damage-byte volume of the newest commit (transfer-volume metrics).</summary>
    public long LastDamageBytes
    {
        get { lock (_frameLock) return _lastDamageBytes; }
    }

    public void Dispose()
    {
        // A teardown we asked for is not a crash, however the child chooses to leave.
        _closeRequested = true;
        try { Send(TabMsg.Close, _ => { }); } catch { }
        _cts.Cancel();
        try { _pipe.Dispose(); } catch { }
        lock (_frameLock)
        {
            try { _frameChannel?.Dispose(); } catch { }
            try { _staleChannel?.Dispose(); } catch { }
            _frameChannel = _staleChannel = null;
        }
        try
        {
            if (_child != null && !_child.HasExited)
            {
                if (!_child.WaitForExit(1000))
                    _child.Kill(entireProcessTree: true);
            }
            _child?.Dispose();
        }
        catch { }
    }
}
