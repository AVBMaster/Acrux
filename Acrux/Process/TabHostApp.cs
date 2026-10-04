using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using SkiaSharp;
using Acrux.PageHost;
using Acrux.Rendering;

namespace Acrux.Process;

/// <summary>
/// Child-process entry for a multi-process tab (`Acrux --tab-host`).
/// The host itself is only the process endpoint and the frame presenter: it owns the
/// command queue, the pipe threads and the raster/pixel-diff state, and pulls the page
/// out of a <see cref="PageEngine"/> that knows nothing about any of this.
/// All engine work stays on the host's main loop thread; only network fetches run on
/// worker tasks (inside the engine).
/// </summary>
internal static class TabHostApp
{
    public static int Run(string[] args)
    {
        string channel = "";
        int tab = 0;
        float dpi = 1f, res = 1f;
        bool hang = false, fault = false;
        foreach (var a in args)
        {
            if (a.StartsWith("--channel=")) channel = a[10..];
            else if (a.StartsWith("--tab=")) int.TryParse(a[6..], out tab);
            else if (a.StartsWith("--dpi=")) float.TryParse(a[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out dpi);
            else if (a.StartsWith("--res=")) float.TryParse(a[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out res);
            else if (a == "--hang") hang = true;
            else if (a == "--fault") fault = true;
        }
        if (string.IsNullOrEmpty(channel)) return 1;

        using var pipe = new NamedPipeClientStream(".", channel, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { pipe.Connect(8000); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TabHost {tab}] connect failed: {ex.Message}");
            return 1;
        }

        var host = new TabHost(tab, channel, pipe, dpi, res, hang, fault);
        TabHost.Active = host;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            TabHost.Active?.ReportCrash("unhandled", e.ExceptionObject as Exception);

        try
        {
            return host.Run();
        }
        catch (Exception ex)
        {
            // Better a reported fault the shell can classify than a silent pipe close.
            host.ReportCrash("entry", ex);
            return 2;
        }
        finally
        {
            TabHost.Active = null;
            host.Dispose();
        }
    }
}

internal sealed class TabHost : IDisposable, IPageEngineSink
{
    /// <summary>Set while the host is running so process-level handlers can report through it.</summary>
    public static TabHost? Active;

    /// <summary>Best-effort "I am dying badly" notice: the shell distinguishes a reported
    /// fault from a host that simply vanished.</summary>
    public void ReportCrash(string stage, Exception? ex)
    {
        var detail = $"{stage}: {ex?.GetType().Name}: {ex?.Message}";
        Console.Error.WriteLine($"[TabHost {_tab}] FAULT {detail}");
        // Temp, not the working directory: a real crash must not drop files wherever the
        // browser happened to be started from.
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), $"acrux_tabfault_{_tab}.log"),
                $"{DateTime.Now:O} {detail}{Environment.NewLine}{ex?.StackTrace}");
        }
        catch { }
        try { Send(TabMsg.CrashReport, w => { TabFraming.WriteString(w, stage); TabFraming.WriteString(w, detail); }); } catch { }
    }

    private readonly int _tab;
    private readonly Stream _pipe;
    private readonly object _pipeWrite = new();
    private readonly float _dpi, _res;
    private readonly ConcurrentQueue<TabMessage> _commands = new();
    private readonly Thread _readerThread;
    private readonly CancellationTokenSource _cts = new();

    // The page pipeline; the host only reads its output.
    private readonly PageEngine _engine;

    // Shared-memory frame channel: one mapping generation per viewport size, named after
    // the control pipe plus a counter so a resize can renegotiate without colliding with
    // the shell's still-open previous mapping.
    private readonly string _channelBaseName;
    private FrameChannel? _channel;
    private int _frameGen;
    // Slot holding the current raster baseline; _holdsLease is true while this process
    // owns its Writing claim (acquired but not yet published).
    private int _rasterSlot = -1;
    private bool _holdsLease;

    // Persistent raster + frame-diff state
    private bool _renderDirty = true;
    private long _lastRenderTick;
    private SKBitmap? _work;      // thin wrapper over the leased slot's pixel area
    private byte[]? _shadow;
    private bool _sentValid;
    private float _sentScrollX, _sentScrollY;
    // Device row _work's content actually stands for (host-internal band raster).
    private float _workDevY;
    private int _bandRun;
    private long _sentDlSerial;
    private readonly DamageRect[] _rectScratch = new DamageRect[MaxDamageRects];
    private const int MaxDamageRects = 64;   // must stay <= FrameChannel's maxRects
    // Frame sender mailbox (latest-wins) + double-buffered packets
    private readonly byte[][] _pktScratch = new byte[2][];
    private readonly bool[] _pktFree = { true, true };
    private byte[]? _mboxPacket;
    private int _mboxLen, _mboxSlot = -1;
    private readonly AutoResetEvent _mboxSignal = new(false);
    private readonly AutoResetEvent _cmdSignal = new(false);
    private Thread? _senderThread;
    // Idle diagnostics: set ACRUX_TAB_STATS=1 to print a 2s heartbeat of
    // which pipeline stages are firing (commands/layout/dl/raster/frames).
    private readonly bool _stats = Environment.GetEnvironmentVariable("ACRUX_TAB_STATS") == "1";
    private long _stCmds, _stRaster, _stFull, _stRows, _stBlit, _stSilent, _stBytes, _stNextLog;
    private long _stReplayMs, _stDiffMs, _stRepBand, _stNBand, _stRepFull, _stNFull;
    // Where the idle CPU goes: per-wake JS pump time, pipeline time and wake count.
    private long _stPump, _stPipe, _stWakes, _stPipeN;
    private readonly long[] _stCmdHist = new long[32];

    public TabHost(int tab, string channelName, Stream pipe, float dpi, float res, bool hang = false, bool fault = false)
    {
        _tab = tab;
        _channelBaseName = channelName;
        _pipe = pipe;
        _dpi = dpi;
        _res = res;
        _hang = hang;
        _fault = fault;
        _engine = new PageEngine(tab, dpi, res, this);
        _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = $"TabHostRead-{tab}" };
        _readerThread.Start();
        _senderThread = new Thread(SenderLoop) { IsBackground = true, Name = $"TabHostSend-{tab}" };
        _senderThread.Start();
    }

    private void SenderLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _mboxSignal.WaitOne(50);
                byte[] pkt; int len; int slot;
                lock (_mboxLock)
                {
                    if (_mboxPacket == null) continue;
                    pkt = _mboxPacket; len = _mboxLen; slot = _mboxSlot;
                    _mboxPacket = null; _mboxSlot = -1;
                }
                try
                {
                    lock (_pipeWrite) _pipe.Write(pkt, 0, len);
                }
                catch { _cts.Cancel(); return; }
                if (slot >= 0) lock (_mboxLock) _pktFree[slot] = true;
            }
        }
        catch { }
    }

    private readonly object _mboxLock = new();

    /// <summary>
    /// Hand a serialized frame-commit packet to the sender thread. NEVER drop a queued
    /// packet: the shell's FrameVersion advances exactly once per SENT commit — a lost
    /// commit would strand a published slot nobody folds, and the shell's release of it
    /// is what lets this host continue an incremental (scroll-shifted) raster chain.
    /// Callers only invoke this after AcquireFreeSlot confirmed a free slot.
    /// </summary>
    private void QueueFramePacket(byte[] packet, int len)
    {
        int slot = packet == _pktScratch[0] ? 0 : 1;
        lock (_mboxLock)
        {
            _mboxPacket = packet; _mboxLen = len; _mboxSlot = slot;
            _pktFree[slot] = false;
        }
        _mboxSignal.Set();
    }

    private int AcquireFreeSlot()
    {
        lock (_mboxLock)
        {
            for (int i = 0; i < 2; i++)
                if (_pktFree[i]) return i;
        }
        return -1;
    }

    private void ReaderLoop()
    {
        while (TabFraming.TryRead(_pipe, out var msg))
        {
            // A dialog answer must reach the blocked script thread directly: routing it
            // through the command queue would deadlock, since the queue is drained by
            // that very thread.
            if (msg.Type == TabMsg.DialogResult)
            {
                DeliverDialogResult(msg.Payload);
                continue;
            }
            _commands.Enqueue(msg);
            _cmdSignal.Set();
            if (msg.Type == TabMsg.Close) return;
        }
        _commands.Enqueue(new TabMessage(TabMsg.Close, Array.Empty<byte>()));
        _cmdSignal.Set();
    }

    // ---- dialogs: the script thread blocks here until the user answers ----

    private readonly object _dialogLock = new();
    private readonly AutoResetEvent _dialogSignal = new(false);
    private int _dialogSeq;
    private int _dialogWaitId = -1;
    private bool _dialogAnswered;
    private bool _dialogAccepted;
    private string? _dialogText;

    private void DeliverDialogResult(byte[] payload)
    {
        lock (_dialogLock)
        {
            try
            {
                using var r = new BinaryReader(new MemoryStream(payload));
                int id = r.ReadInt32();
                if (id != _dialogWaitId) return;   // stale answer for an already-closed dialog
                _dialogAccepted = r.ReadByte() != 0;
                int len = r.ReadInt32();
                _dialogText = len > 0 ? System.Text.Encoding.UTF8.GetString(r.ReadBytes(len)) : "";
            }
            catch { _dialogAccepted = false; _dialogText = null; }
            _dialogAnswered = true;
        }
        _dialogSignal.Set();
    }

    public string? RequestDialog(string message, string type)
    {
        int id = Interlocked.Increment(ref _dialogSeq);
        lock (_dialogLock)
        {
            _dialogWaitId = id;
            _dialogAnswered = false;
            _dialogAccepted = false;
            _dialogText = null;
        }
        Send(TabMsg.DialogRequest, w =>
        {
            w.Write(id);
            TabFraming.WriteString(w, message);
            TabFraming.WriteString(w, type);
        });

        // Block the script thread — alert()/confirm() are synchronous by spec — while
        // staying alert to the pipe going away, which would otherwise wedge us here.
        while (true)
        {
            lock (_dialogLock)
            {
                if (_dialogAnswered)
                {
                    _dialogWaitId = -1;
                    if (!_dialogAccepted) return type == "confirm" ? "false" : null;
                    return type == "confirm" ? "true" : (_dialogText ?? "");
                }
            }
            if (_cts.IsCancellationRequested) return null;
            // Waiting on a human is not being wedged: keep beating so the shell never
            // reports a page as unresponsive while its dialog is simply open.
            Beat();
            _dialogSignal.WaitOne(50);
        }
    }

    // Liveness: a beat at loop-stage boundaries. A wedged loop stops sending them, which
    // is the whole signal the shell's watchdog needs — no assumptions about what is slow.
    private long _loopSeq;
    private long _nextBeatTick;
    private readonly bool _hang;
    private readonly bool _fault;

    private void Beat()
    {
        _loopSeq++;
        long now = Environment.TickCount64;
        if (now < _nextBeatTick) return;
        // 1s granularity: the watchdog's thresholds are seconds apart anyway, and beating
        // faster measurably doubled an idle tab's CPU.
        _nextBeatTick = now + 1000;
        int pending = _commands.Count;
        long seq = _loopSeq;
        Send(TabMsg.Heartbeat, w => { w.Write(seq); w.Write(pending); });
    }

    public int Run()
    {
        Send(TabMsg.Ready, _ => { });
        if (_hang)
        {
            // Test-only fault injection: wedge the loop exactly like a page script that
            // never returns would, so the shell's watchdog can be observed doing its job.
            while (true) Thread.SpinWait(100000);
        }
        if (_fault)
        {
            // Test-only fault injection: die the way an unhandled engine fault dies — report
            // first, then exit nonzero — so the shell can tell it from a vanished host.
            ReportCrash("injected", new InvalidOperationException("injected fault"));
            return 2;
        }

        while (!_cts.IsCancellationRequested)
        {
            Beat();
            // Drain this pass's commands, but serve at most ONE DevTools chunk per pass:
            // a large tree dump arrives as many small requests, and re-queuing the extras
            // keeps input, JS and frames turning between chunks instead of starving them.
            int passBudget = _commands.Count;
            bool dtChunkServed = false;
            for (int i = 0; i < passBudget; i++)
            {
                if (!_commands.TryDequeue(out var msg)) break;
                bool dtChunkRequest = msg.Type is TabMsg.DtNodesRequest or TabMsg.DtStylesRequest;
                if (dtChunkRequest && dtChunkServed)
                {
                    _commands.Enqueue(msg);
                    continue;
                }
                if (_stats)
                {
                    _stCmds++;
                    _stCmdHist[(int)msg.Type % 32]++;
                }
                if (!Handle(msg)) return 0;
                if (dtChunkRequest) dtChunkServed = true;
            }

            _engine.SettleDeferredHover(_commands.Count == 0);

            long wkT = _stats ? Stopwatch.GetTimestamp() : 0;
            _engine.PumpJs();
            if (_stats) _stPump += (Stopwatch.GetTimestamp() - wkT) * 1000 / Stopwatch.Frequency;
            Beat();

            bool wantRender = _engine.PipelineWanted || _renderDirty;
            // Hidden tabs rebuild at most every 500ms (JS keeps running); the
            // active tab rebuilds at ~125fps max.
            long minInterval = _engine.IsActive ? 8 : 500;
            if (wantRender && Environment.TickCount64 - _lastRenderTick >= minInterval)
            {
                wkT = _stats ? Stopwatch.GetTimestamp() : 0;
                RenderPass();
                if (_stats)
                {
                    _stPipe += (Stopwatch.GetTimestamp() - wkT) * 1000 / Stopwatch.Frequency;
                    _stPipeN++;
                }
                Beat();
            }
            if (_stats) _stWakes++;

            if (_stats && Environment.TickCount64 >= _stNextLog)
            {
                _stNextLog = Environment.TickCount64 + 2000;
                var line = $"[TabHost {_tab} stats] cmds={_stCmds} relayout={_engine.RelayoutRequestCount} " +
                    $"style={_engine.StylePassCount} layout={_engine.LayoutPassCount} dl={_engine.DisplayListCount} " +
                    $"animRun={_engine.AnimationsRunning} parked={_engine.AnimationsParked} " +
                    $"animPark={_engine.AnimParkCount} animPaint={_engine.AnimPaintCount} " +
                    $"raster={_stRaster} " +
                    $"full={_stFull} rows={_stRows} blit={_stBlit} silent={_stSilent} KB={_stBytes / 1024} " +
                    $"wakes={_stWakes} pumpMs={_stPump} pipeMs={_stPipe} pipeN={_stPipeN} " +
                    $"sampleMs={_engine.AnimSampleMs / Math.Max(1, _engine.AnimSampleCount)} " +
                    $"rep={_stReplayMs / Math.Max(1, _stRaster)}ms bandRep={_stRepBand / Math.Max(1, _stNBand)}ms(n={_stNBand}) fullRep={_stRepFull / Math.Max(1, _stNFull)}ms(n={_stNFull}) diff={_stDiffMs / Math.Max(1, _stRaster)}ms " +
                    $"heapMB={GC.GetTotalMemory(false) / 1048576} wsMB={Environment.WorkingSet / 1048576}";
                for (int i = 0; i < _stCmdHist.Length; i++)
                    if (_stCmdHist[i] > 0) line += $" {((TabMsg)i)}:{_stCmdHist[i]}";
                Console.Error.WriteLine(line);
                try { File.AppendAllText($"acrux_tabstats_{_tab}.log", line + "\n"); } catch { }
            }

            // Event-driven idle: sleep until the next scheduled deadline instead of
            // polling — an idle tab wakes a handful of times per minute. The engine
            // folds JS timers and animation frames into one deadline: a running
            // animation has to wake at the frame boundary, not on the idle poll, or
            // it plays at whatever the poll happens to be instead of at display rate.
            int waitMs;
            if (_commands.Count > 0) waitMs = 0;
            else
            {
                int due = _engine.NextWorkDelayMs;
                // The engine folds JS timers, animation frames and the idle poll into one
                // deadline (IdleWaitMs when nothing is scheduled), so its answer is trusted
                // rather than clamped back down to the idle poll — that is what lets a
                // parked off-screen animation sleep between samples.
                if (_engine.IsActive)
                    waitMs = Math.Clamp(due, 1, 500);
                else
                    waitMs = Math.Clamp(due, 50, 500);
            }
            if (waitMs > 0) _cmdSignal.WaitOne(waitMs);
        }
        return 0;
    }

    /// <summary>Handle one command. Returns false when the host should exit.</summary>
    private bool Handle(in TabMessage msg)
    {
        switch (msg.Type)
        {
            case TabMsg.Navigate:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.Navigate(TabFraming.ReadString(r));
                break;
            }
            case TabMsg.NavigateHtml:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                var html = TabFraming.ReadString(r);
                var baseUrl = TabFraming.ReadString(r);
                _engine.LoadHtml(html, baseUrl);
                break;
            }
            case TabMsg.MouseDown:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.HandlePointerDown(r.ReadSingle(), r.ReadSingle());
                break;
            }
            case TabMsg.MouseUp:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.HandlePointerUp(r.ReadSingle(), r.ReadSingle());
                break;
            }
            case TabMsg.MouseMove:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.HandlePointerMove(r.ReadSingle(), r.ReadSingle());
                break;
            }
            case TabMsg.ScrollTo:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                float x = r.ReadSingle(), y = r.ReadSingle();
                _engine.SetScrollFromHost(x, y);
                break;
            }
            case TabMsg.Resize:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.SetViewport(r.ReadSingle(), r.ReadSingle());
                break;
            }
            case TabMsg.KeyDown:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.HandleKey(r.ReadUInt16(), r.ReadUInt16(), r.ReadByte() != 0);
                break;
            }
            case TabMsg.Char:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.HandleChar(r.ReadUInt16());
                break;
            }
            case TabMsg.ImeUpdate:
            {
                // [string text][int selStart][int selLen] — the composition caret offset
                // is what positions the pending text; selLen is carried for the protocol's
                // sake (the engine always underlines the whole pending string).
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                var text = TabFraming.ReadString(r);
                int cursor = r.ReadInt32();
                _engine.ImeCompositionUpdate(text, cursor);
                break;
            }
            case TabMsg.ImeCommit:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.ImeCompositionCommit(TabFraming.ReadString(r));
                break;
            }
            case TabMsg.ImeCancel:
                _engine.ImeCompositionCancel();
                break;
            case TabMsg.SelectAll:
                _engine.SelectAllText();
                break;
            case TabMsg.Wheel:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                _engine.HandleWheel(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                break;
            }
            case TabMsg.SelectedTextRequest:
            {
                // This runs on the main loop (the reader thread only queues commands), so
                // reading engine state here is safe — the document has one owner thread.
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                int id = r.ReadInt32();
                string text = _engine.SelectedText;
                Send(TabMsg.SelectedTextResponse, w =>
                {
                    w.Write(id);
                    TabFraming.WriteString(w, text);
                });
                break;
            }
            case TabMsg.DtNodesRequest:
            {
                // DevTools tree chunk: everything the walk touches is engine state read on
                // this thread; the budget (≤2000 nodes / ≤240 KB) is enforced in the engine.
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                var (rpcId, knownVersion, nodeId, maxDepth, limit) = DevToolsWire.ReadNodesRequest(r);
                var batch = _engine.CollectNodeTree(nodeId, knownVersion, maxDepth, limit);
                Send(TabMsg.DtNodesResponse, w => { w.Write(rpcId); DevToolsWire.Write(w, batch); });
                break;
            }
            case TabMsg.DtStylesRequest:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                var (rpcId, knownVersion, nodeId) = DevToolsWire.ReadStylesRequest(r);
                var styles = _engine.StylesForNode(nodeId, knownVersion);
                Send(TabMsg.DtStylesResponse, w => { w.Write(rpcId); DevToolsWire.Write(w, styles); });
                break;
            }
            case TabMsg.DtEvalRequest:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                var (rpcId, script) = DevToolsWire.ReadEvalRequest(r);
                // Eval is synchronous page script: a wedged script wedges this loop exactly
                // like any other long task, and the shell's eval timeout + watchdog own it.
                var result = _engine.EvaluateScript(script);
                Send(TabMsg.DtEvalResponse, w => { w.Write(rpcId); DevToolsWire.Write(w, result); });
                break;
            }
            case TabMsg.FindRequest:
            {
                using var r = new BinaryReader(new MemoryStream(msg.Payload));
                var (rpcId, options, activateIndex) = DevToolsWire.ReadFindRequest(r);
                var result = _engine.FindMatches(options.Query, options.CaseSensitive, options.Forward, activateIndex);
                Send(TabMsg.FindResponse, w => { w.Write(rpcId); DevToolsWire.Write(w, result); });
                break;
            }
            case TabMsg.SetActive:
                bool active = msg.Payload.Length > 0 && msg.Payload[0] != 0;
                if (active != _engine.IsActive)
                {
                    _engine.SetActive(active);
                    // Coming back to the foreground: push a fresh full frame
                    // built from the display lists we kept warm while hidden.
                    if (active) { _sentValid = false; _renderDirty = true; }
                }
                break;
            case TabMsg.Close:
                return false;
        }
        return true;
    }

    // ==================== engine -> host ====================

    /// <summary>Navigation asked for by the page. Queued, never executed inline: the
    /// engine loop owns the document.</summary>
    public void RequestNavigate(string url) =>
        _commands.Enqueue(new TabMessage(TabMsg.Navigate,
            TabFraming.BuildPayload(w => TabFraming.WriteString(w, url))));

    /// <summary>A fetched document is ready (worker thread). Engine/DOM work must land
    /// on the host main loop thread.</summary>
    public void RequestLoadHtml(string html, string baseUrl) =>
        _commands.Enqueue(new TabMessage(TabMsg.NavigateHtml,
            TabFraming.BuildPayload(w => { TabFraming.WriteString(w, html); TabFraming.WriteString(w, baseUrl); })));

    public void ReportTitle(string title) =>
        Send(TabMsg.Title, w => TabFraming.WriteString(w, title));

    public void ReportUrl(string url) =>
        Send(TabMsg.UrlChanged, w => TabFraming.WriteString(w, url));

    public void ReportLoading(bool loading) =>
        Send(TabMsg.Loading, w => w.Write((byte)(loading ? 1 : 0)));

    public void ReportScrollChanged(float x, float y) =>
        Send(TabMsg.ScrollChanged, wr => { wr.Write(x); wr.Write(y); });

    public void ReportWheelConsumed(bool consumed) =>
        Send(TabMsg.WheelResult, w => w.Write((byte)(consumed ? 1 : 0)));

    public void RequestFrame() => _renderDirty = true;

    public void InvalidateFrameBaseline() { _sentValid = false; _renderDirty = true; }

    public void ReportDirtyTrace(string trace)
    {
        if (!_stats) return;
        try { File.AppendAllText($"acrux_tabstats_{_tab}.log", $"[TabHost {_tab}] MarkDirty from:\n{trace}\n"); } catch { }
    }

    // ==================== render ====================

    private void RenderPass()
    {
        if (_engine.Document == null) return;
        try
        {
            _engine.UpdatePipeline();

            if (_renderDirty)
            {
                if (_stats) _stRaster++;
                RasterizeAndSend();
                _renderDirty = false;
            }
            _lastRenderTick = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TabHost {_tab}] render: {ex.Message}");
        }
    }

    private unsafe void RasterizeAndSend()
    {
        var dl = _engine.DisplayList;
        if (dl == null || _engine.Document == null) return;

        // Hidden tabs keep layout + display list warm but skip raster +
        // pixel transfer entirely; activation forces one fresh full frame.
        if (!_engine.IsActive) { _sentValid = false; return; }

        float scale = _dpi * _res;
        var bg = _engine.ViewBackgroundColor;
        long dlSerial = _engine.DisplayListSerial;
        float scrollX = _engine.ScrollX, scrollY = _engine.ScrollY;
        int wPx = Math.Max(1, (int)(_engine.ViewportWidth * scale));
        int hPx = Math.Max(1, (int)(_engine.ViewportHeight * scale));
        int rowBytes = wPx * 4;
        int total = rowBytes * hPx;

        // The slot pixel area IS the raster surface, and its size is baked into the
        // mapping layout — so a resize cannot mutate it in place: it negotiates a new
        // generation, announces it, and forces the next frame to be Full.
        if (_channel == null || _channel.Width != wPx || _channel.Height != hPx)
            RenegotiateFrameChannel(wPx, hPx);
        var ch = _channel ?? throw new InvalidOperationException("frame channel missing after renegotiation");
        if (_shadow == null || _shadow.Length != total) { _shadow = new byte[total]; _sentValid = false; }

        // Frames ride the shared mapping; only a commit crosses the pipe. Speed still
        // comes from the CHILD-INTERNAL band raster: the baseline slot is shifted by
        // whole rows on a scroll and only the exposed strip is repainted — and now the
        // shell is told so (ScrollBlit + scrollDy + strip rect) and mirrors the exact
        // same shift on its own copy. Any hypothetical shift error is confined to
        // slots we own and self-heals at the next full repaint, which is forced every
        // 64 band frames, on content changes, and on large or horizontal jumps.
        int dyPx = (int)Math.Round(scrollY - _sentScrollY);
        bool xStable = Math.Abs(scrollX - _sentScrollX) < 0.5f;
        bool scrolled = dyPx != 0 || !xStable;
        // Device-space bookkeeping: _workDevY is the device row the _work content
        // actually stands for (whole-row shifts only), so per-frame rounding
        // residuals never accumulate — the error stays under one pixel until the
        // periodic full resync instead of wobbling text.
        float scrollDevY = scrollY * scale;
        int dyW = (int)Math.Round(scrollDevY - _workDevY);
        bool bandScroll = _sentValid && dlSerial == _sentDlSerial && _bandRun < 64 &&
                          xStable && dyW != 0 && dyW > -hPx && dyW < hPx;

        // Claim the raster surface. A band shift mutates the baseline pixels: only ever
        // take it on a slot we own — still leased from an unpublished pass, or re-claimed
        // from Free, which PROVES the shell applied and released the frame it last saw
        // (that release is the backpressure keeping a shift chain from ever publishing
        // over a frame the shell skipped — the old "shift only once the send is certain"
        // rule, now enforced by the slot state machine). When the shell has not caught
        // up, any slot may be taken instead — published-and-unread included — but the
        // frame rasterized into it must then be self-contained, the old stateless policy.
        bool baselineIntact;
        if (_rasterSlot >= 0 && (_holdsLease || ch.TryClaimForWriting(_rasterSlot)))
        {
            _holdsLease = true;
            baselineIntact = true;
        }
        else
        {
            int t = ch.AcquireForWriting();
            if (t < 0) { _renderDirty = true; return; } // every slot is under the shell — retry next pass
            _rasterSlot = t;
            _holdsLease = true;
            baselineIntact = false;
            _work?.Dispose();
            _work = new SKBitmap();
            // Zero-copy wrap of the slot's pixel area: rowBytes is the mapping stride —
            // the raster surface IS shared memory now, no 3MB copy per frame.
            if (!_work.InstallPixels(new SKImageInfo(wPx, hPx, SKColorType.Bgra8888, SKAlphaType.Premul),
                    (IntPtr)ch.PixelPointer(t), ch.Stride))
                throw new InvalidOperationException("frame slot pixels could not be installed");
        }
        if (bandScroll && !baselineIntact) bandScroll = false;

        int stripY = 0, stripH = 0; // exposed strip of a shifted band frame
        var work = _work ?? throw new InvalidOperationException("raster surface missing");
        byte* cur = (byte*)work.GetPixels(out _);
        long rsT = _stats ? Stopwatch.GetTimestamp() : 0;
        // Cull in document space: the canvas maps doc→device with scale and the
        // scroll translate, so only ops near the visible viewport can paint.
        float invS = 1f / scale;
        using (var canvas = new SKCanvas(work))
        {
            if (bandScroll)
            {
                ShiftRasterRows(cur, hPx, rowBytes, dyW);
                // Repaint the exposed strip plus one guard row each side of the
                // sub-pixel residual left by the whole-row shift — clamped to the
                // surface: a published rect that reaches past the last row would be
                // rejected wholesale by the shell, starving it of new content.
                stripY = dyW > 0 ? Math.Max(0, hPx - dyW - 1) : 0;
                stripH = Math.Min(hPx - stripY, Math.Abs(dyW) + 2);
                canvas.Save();
                canvas.ClipRect(new SKRect(0, stripY, wPx, stripY + stripH));
                canvas.Scale(scale);
                canvas.Translate(-scrollX, -scrollY);
                dl.ExecuteCulled(canvas, new SKRect(
                    scrollX - 8, scrollY + stripY * invS - 8,
                    scrollX + wPx * invS + 8, scrollY + (stripY + stripH) * invS + 8));
                canvas.Restore();
                _workDevY += dyW;
                _bandRun++;
                if (_stats) { _stRepBand += (Stopwatch.GetTimestamp() - rsT) * 1000 / Stopwatch.Frequency; _stNBand++; }
            }
            else
            {
                var docWindow = new SKRect(scrollX - 8, scrollY - 512, scrollX + wPx * invS + 8, scrollY + hPx * invS + 512);
                canvas.Clear(bg);
                canvas.Scale(scale);
                canvas.Translate(-scrollX, -scrollY);
                dl.ExecuteCulled(canvas, docWindow);
                canvas.Flush();
                _workDevY = scrollDevY;
                _bandRun = 0;
                if (_stats) { _stRepFull += (Stopwatch.GetTimestamp() - rsT) * 1000 / Stopwatch.Frequency; _stNFull++; }
            }
        }
        if (_stats) _stReplayMs += (Stopwatch.GetTimestamp() - rsT) * 1000 / Stopwatch.Frequency;

        FrameMode mode;
        int rectCount = 0;
        int scrollDy = 0;
        long damageRows;
        if (bandScroll)
        {
            // Pure scroll: the shell shifts its mirror by ScrollDy and overlays the
            // exposed strip — the slot's rows outside it are content the shell already
            // has, produced by the same shift.
            mode = FrameMode.ScrollBlit;
            scrollDy = dyW;
            _rectScratch[0] = new DamageRect(0, stripY, wPx, stripH);
            rectCount = 1;
            damageRows = stripH;
        }
        else if (!_sentValid || scrolled)
        {
            // Scroll (or first frame) without the shift chain: ship the whole
            // viewport, statelessly.
            mode = FrameMode.Full;
            damageRows = hPx;
        }
        else
        {
            // Stable scroll: ship only changed rows if the change is localized.
            // Contiguous changed rows merge into one rect each, so scattered edits
            // no longer pay for the rows between them; "everything" degenerates to Full.
            long rsD = _stats ? Stopwatch.GetTimestamp() : 0;
            long changed = 0;
            int runStart = -1;
            bool overflow = false;
            var curSpan = new ReadOnlySpan<byte>(cur, total);
            for (int y = 0; y < hPx; y++)
            {
                int off = y * rowBytes;
                if (!curSpan.Slice(off, rowBytes).SequenceEqual(new ReadOnlySpan<byte>(_shadow, off, rowBytes)))
                {
                    if (runStart < 0) runStart = y;
                    changed++;
                }
                else if (runStart >= 0)
                {
                    if (rectCount < MaxDamageRects) _rectScratch[rectCount++] = new DamageRect(0, runStart, wPx, y - runStart);
                    else overflow = true;
                    runStart = -1;
                }
            }
            if (runStart >= 0)
            {
                if (rectCount < MaxDamageRects) _rectScratch[rectCount++] = new DamageRect(0, runStart, wPx, hPx - runStart);
                else overflow = true;
            }
            if (_stats) _stDiffMs += (Stopwatch.GetTimestamp() - rsD) * 1000 / Stopwatch.Frequency;
            if (changed == 0)
            {
                if (_stats) _stSilent++;
                _sentDlSerial = dlSerial;
                return; // nothing actually changed — publish nothing; the lease stays ours
            }
            if (overflow || changed > hPx * 3 / 5)
            {
                // Full is self-contained: no rect list needed (and none the shell could trust).
                mode = FrameMode.Full;
                rectCount = 0;
                damageRows = hPx;
            }
            else
            {
                mode = FrameMode.DamageRows;
                damageRows = changed;
            }
        }

        // The commit packet must be secured BEFORE publishing: every published frame
        // has its commit cross the pipe, and the diff baseline advances per published
        // frame only. Both packets in flight → retry next pass with nothing sent.
        int pktSlot = AcquireFreeSlot();
        if (pktSlot < 0) { _renderDirty = true; return; }

        _engine.GetImeCaretState(out float imeCaretX, out float imeCaretY, out float imeCaretH,
            out bool imeHasFocus, out bool imePassword);

        var meta = new FrameMeta
        {
            Mode = mode,
            ScrollX = scrollX,
            ScrollY = scrollY,
            ContentW = _engine.ContentWidth,
            ContentH = _engine.ContentHeight,
            // Page background (RGBA), so the parent can fill freshly exposed scroll
            // strips with the page's own color instead of smearing pixels (edge
            // stretch) or flashing white.
            PageBgRgba = (uint)bg,
            DomCount = _engine.DomCount,
            BoxCount = _engine.BoxCount,
            ScrollDy = scrollDy,
            // IME target state travels with every frame — the shell positions the
            // candidate window from it without a pipe round trip.
            CaretX = imeCaretX,
            CaretY = imeCaretY,
            CaretH = imeCaretH,
            HasEditableFocus = imeHasFocus,
            IsPassword = imePassword,
            SelectOpen = _engine.HasOpenSelect,
        };
        ulong seq = ch.Publish(_rasterSlot, in meta, _rectScratch.AsSpan(0, rectCount));
        _holdsLease = false;

        // Fold the diff baseline forward to the published frame. ScrollBlit shifted
        // EVERY row of the slot, so its baseline is the whole surface, not just the
        // strip; DamageRows only shipped its rects and the rest already matches.
        var src = new ReadOnlySpan<byte>(cur, total);
        if (mode != FrameMode.DamageRows)
            src.CopyTo(_shadow);
        else
            for (int i = 0; i < rectCount; i++)
            {
                var r = _rectScratch[i];
                src.Slice(r.Y * rowBytes, r.Height * rowBytes)
                    .CopyTo(new Span<byte>(_shadow, r.Y * rowBytes, r.Height * rowBytes));
            }

        const int commitPayload = 8 + 8; // [ulong seq][long damageBytes]
        const int commitPacket = 5 + commitPayload;
        if (_pktScratch[pktSlot] == null || _pktScratch[pktSlot]!.Length < commitPacket)
            _pktScratch[pktSlot] = new byte[commitPacket];
        var pkt = _pktScratch[pktSlot]!;
        var span = new Span<byte>(pkt, 0, commitPacket);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span, commitPayload);
        span[4] = (byte)TabMsg.FrameCommit;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(5, 8), seq);
        // damageBytes: what the shell will fold out of the mapping — kept on the pipe so
        // transfer-volume metrics (--proctest bandwidth invariants) still make sense.
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(span.Slice(13, 8), damageRows * rowBytes);
        QueueFramePacket(pkt, commitPacket);
        if (_stats)
        {
            _stBytes += commitPacket;
            if (mode == FrameMode.Full) _stFull++;
            else if (mode == FrameMode.ScrollBlit) _stBlit++;
            else _stRows++;
        }

        _sentValid = true;
        _sentScrollX = scrollX;
        _sentScrollY = scrollY;
        _sentDlSerial = dlSerial;
    }

    /// <summary>
    /// Start a new frame-mapping generation at the current device size. The name derives
    /// from the control pipe plus a counter so it never collides with the shell's handle
    /// on the previous generation. Hello announces name + dimensions; it is written
    /// before this thread can queue any commit of the new generation, and the pipe-write
    /// lock orders the two, so the shell always opens the mapping before its first commit.
    /// </summary>
    private void RenegotiateFrameChannel(int wPx, int hPx)
    {
        _frameGen++;
        _work?.Dispose();
        _work = null;
        _rasterSlot = -1;
        _holdsLease = false;
        _channel?.Dispose();
        var name = $"{_channelBaseName}_f{_frameGen}";
        _channel = FrameChannel.Create(name, wPx, hPx, MaxDamageRects);
        _shadow = null;
        _sentValid = false;
        _workDevY = 0f;
        _bandRun = 0;
        Send(TabMsg.Hello, w =>
        {
            w.Write(PageProtocol.Version);
            w.Write((ulong)(PageProtocol.Capabilities.ShmFrames | PageProtocol.Capabilities.BlockingDialogs |
                            PageProtocol.Capabilities.Ime | PageProtocol.Capabilities.NodeRpc |
                            PageProtocol.Capabilities.DevToolsRpc | PageProtocol.Capabilities.FindText));
            w.Write(Environment.ProcessId);
            TabFraming.WriteString(w, name);
            w.Write(wPx);
            w.Write(hPx);
        });
    }

    /// <summary>
    /// In-place whole-row shift of the raster so row y becomes old row y+d
    /// (scrolling down by d rows moves content up in the buffer). Copy order
    /// guarantees no read-after-write corruption for overlapping ranges.
    /// </summary>
    private static unsafe void ShiftRasterRows(byte* buf, int hPx, int rowBytes, int d)
    {
        if (d > 0)
        {
            for (int y = 0; y + d < hPx; y++)
                Buffer.MemoryCopy(buf + (long)(y + d) * rowBytes, buf + (long)y * rowBytes, rowBytes, rowBytes);
        }
        else if (d < 0)
        {
            for (int y = hPx - 1; y + d >= 0; y--)
                Buffer.MemoryCopy(buf + (long)(y + d) * rowBytes, buf + (long)y * rowBytes, rowBytes, rowBytes);
        }
    }

    private void Send(TabMsg type, Action<BinaryWriter> body)
    {
        var payload = TabFraming.BuildPayload(body);
        try
        {
            lock (_pipeWrite)
                TabFraming.Write(_pipe, type, payload);
        }
        catch (IOException)
        {
            _cts.Cancel(); // parent is gone
        }
        catch (ObjectDisposedException)
        {
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _engine.Dispose();
        _work?.Dispose();  // wrapper only: the pixels live in the mapping
        _work = null;
        _channel?.Dispose();
        _channel = null;
    }
}
