using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace UpBrowser.Process;

/// <summary>
/// Parent-side handle for one tab-host child process. Launches
/// `UpBrowser --tab-host`, serves the duplex pipe, forwards commands and
/// receives rendered frames. All page state (DOM/layout/JS) lives in the
/// child; this side only owns the latest frame packet + metadata.
/// Inbound messages are parsed into a rotating double buffer — the buffer a
/// frame was exposed from is never overwritten while the compositor copies it,
/// so steady-state operation makes zero large allocations.
/// </summary>
internal sealed class RemoteTabProcess : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly System.Diagnostics.Process? _child;
    private readonly object _writeLock = new();
    private readonly Thread _reader;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _connected;

    private readonly byte[][] _rxBuf = new byte[2][];
    private int _rxIdx;
    private int _exposedIdx = -1;

    private readonly object _frameLock = new();
    private byte[]? _frameBuf;
    private int _frameOff, _framePixLen;
    private int _frameW, _frameH;
    private float _frameScrollX, _frameScrollY;
    private float _contentW, _contentH;
    private int _domCount, _boxCount;
    private byte _frameMode;
    private int _frameY, _frameRH;
    private long _frameVersion;

    public int TabIndex { get; }
    public int ChildPid => _child?.Id ?? -1;
    public string PipeName { get; }
    public bool IsConnected => _connected;
    public volatile bool IsDead;
    public volatile bool Loading;
    public volatile string Title = "";
    public volatile string Url = "";
    /// <summary>Page background (RGBA packed) carried on every frame header.</summary>
    public uint PageBgRgba { get; private set; } = 0xFFFFFFFF;

    /// <summary>Raised on the reader thread when a new frame arrives.</summary>
    public event Action? OnFrameArrived;
    /// <summary>Raised on the reader thread for title/URL/dialog updates.</summary>
    public event Action<TabMsg, string, string>? OnSignal;
    /// <summary>Child-initiated scroll (window.scrollTo / anchors) — parent syncs its scrollbar.</summary>
    public event Action<float, float>? OnScrollChanged;
    /// <summary>Reply to the last Wheel: true when an element scroller consumed it.</summary>
    public event Action<bool>? OnWheelResult;

    public RemoteTabProcess(int tabIndex, string initialUrl, float dpiScale, float resolutionScale)
    {
        TabIndex = tabIndex;
        PipeName = TabPipeNames.ForTab(tabIndex);
        _pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            IsDead = true;
            _reader = null!;
            return;
        }

        try
        {
            _child = System.Diagnostics.Process.Start(new ProcessStartInfo(exePath)
            {
                ArgumentList =
                {
                    "--tab-host",
                    $"--channel={PipeName}",
                    $"--tab={tabIndex}",
                    $"--dpi={dpiScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}",
                    $"--res={resolutionScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}",
                },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteTab {tabIndex}] launch failed: {ex.Message}");
            IsDead = true;
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
                IsDead = true;
                _connected = false;
                return;
            }
            _connected = true;

            var header = new byte[5];
            while (!_cts.IsCancellationRequested)
            {
                // Always read into the buffer that is NOT currently exposed to the compositor.
                int exposed = Volatile.Read(ref _exposedIdx);
                int target = exposed >= 0 ? 1 - exposed : _rxIdx;

                if (!ReadExact(header, 5)) break;
                int len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
                var kind = (TabMsg)header[4];
                if (len < 0 || len > 256 * 1024 * 1024) break;

                var buf = _rxBuf[target];
                if (buf == null || buf.Length < len)
                {
                    buf = new byte[Math.Max(len, 1024 * 1024)];
                    _rxBuf[target] = buf;
                }
                if (len > 0 && !ReadExact(buf, len)) break;

                Handle(kind, buf, 0, len, target);
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
                IsDead = true;
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

    private void Handle(TabMsg kind, byte[] buf, int off, int len, int bufIdx)
    {
        switch (kind)
        {
            case TabMsg.Frame:
            {
                // Header: w h sx sy cw ch dom box mode x y rw rh pixelLen
                int p = off;
                int w = ReadI(buf, ref p);
                int h = ReadI(buf, ref p);
                float sx = ReadF(buf, ref p);
                float sy = ReadF(buf, ref p);
                float cw = ReadF(buf, ref p);
                float ch = ReadF(buf, ref p);
                int dom = ReadI(buf, ref p);
                int box = ReadI(buf, ref p);
                byte mode = buf[p]; p += 1;
                uint pageBg = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    buf.AsSpan(p, 4)); p += 4; // spare field: page background RGBA
                int ry = ReadI(buf, ref p);
                p += 4; // band x (always 0; row bands span full width)
                int rh = ReadI(buf, ref p);
                int pixLen = ReadI(buf, ref p);

                lock (_frameLock)
                {
                    _frameBuf = buf;
                    _frameOff = p;
                    _framePixLen = pixLen;
                    _frameW = w; _frameH = h;
                    _frameScrollX = sx; _frameScrollY = sy;
                    _contentW = cw; _contentH = ch;
                    _domCount = dom; _boxCount = box;
                    _frameMode = mode; _frameY = ry; _frameRH = rh;
                    PageBgRgba = pageBg;
                    _frameVersion++;
                    _exposedIdx = bufIdx;
                }
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
            case TabMsg.Dialog:
            {
                int p = off;
                var text = ReadStr(buf, ref p, len);
                var type = ReadStr(buf, ref p, len);
                OnSignal?.Invoke(TabMsg.Dialog, text, type);
                break;
            }
            case TabMsg.Ready:
                Console.WriteLine($"[RemoteTab {TabIndex}] child ready");
                break;
        }
    }

    private static int ReadI(byte[] b, ref int p)
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p, 4));
        p += 4;
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
        var payload = TabFraming.BuildPayload(body);
        try
        {
            lock (_writeLock)
                TabFraming.Write(_pipe, type, payload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteTab {TabIndex}] send failed: {ex.Message}");
            IsDead = true;
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

    public long FrameVersion
    {
        get { lock (_frameLock) return _frameVersion; }
    }

    /// <summary>
    /// Grab the current frame packet. The reader never overwrites the exposed
    /// buffer, so the caller may copy straight out of it. The returned
    /// pixel bytes live at <paramref name="pixels"/> inside the buffer.
    /// </summary>
    public bool TryGetFrame(out byte[]? buf, out int pixelsOffset, out int pixelsLen,
        out int w, out int h, out float scrollX, out float scrollY,
        out float contentW, out float contentH, out int domCount, out int boxCount,
        out FrameMode mode, out int bandY, out int bandH, out long version)
    {
        lock (_frameLock)
        {
            buf = _frameBuf;
            pixelsOffset = _frameOff; pixelsLen = _framePixLen;
            w = _frameW; h = _frameH;
            scrollX = _frameScrollX; scrollY = _frameScrollY;
            contentW = _contentW; contentH = _contentH;
            domCount = _domCount; boxCount = _boxCount;
            mode = (FrameMode)_frameMode; bandY = _frameY; bandH = _frameRH;
            version = _frameVersion;
            return buf != null;
        }
    }

    public void Dispose()
    {
        try { Send(TabMsg.Close, _ => { }); } catch { }
        _cts.Cancel();
        try { _pipe.Dispose(); } catch { }
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
