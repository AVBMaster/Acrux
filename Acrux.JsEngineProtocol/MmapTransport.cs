using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Acrux.JsEngineProtocol;

/// <summary>
/// 基于内存映射文件的跨平台 IPC 传输层。
/// Windows 使用命名共享内存节（section），其他平台使用文件映射（file-backed mmap）
/// 以在所有系统上可靠地创建/打开共享内存。
/// 两个独立的共享内存区域（主→子 + 子→主），通过内存内的同步标志协调。
/// 不依赖 EventWaitHandle，在所有平台（Windows/Linux/macOS）上均可工作。
/// </summary>
public class MmapTransport : IDisposable
{
    private const int HeaderSize = 4;
    private const int MaxMsgSize = 10 * 1024 * 1024;
    private const int PageSize = MaxMsgSize + 8;
    private const int SyncOffset = 0;
    private const int HeaderOffset = 4;
    private const int DataOffset = 8;

    // 同步标志状态
    private const int StateIdle = 0;
    private const int StateWriting = 1;
    private const int StateReady = 2;

    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly string _channelName;
    private readonly string _bootstrapPath;
    private bool _disposed;

    // 本进程写入的通道（对方读）
    private SharedMmap? _outMmap;
    private MemoryMappedViewAccessor? _outView;

    // 本进程读取的通道（对方写）
    private SharedMmap? _inMmap;
    private MemoryMappedViewAccessor? _inView;

    public MmapTransport(string channelName)
    {
        _channelName = channelName;
        var escaped = channelName.Replace("\\", "_").Replace("/", "_").Replace(":", "_");
        _bootstrapPath = Path.Combine(Path.GetTempPath(), $"acrux_mmap_{escaped}.bootstrap");
    }

    public async Task StartServerAsync(CancellationToken ct = default)
    {
        var outName = MmapKey("child_to_main");
        var inName = MmapKey("main_to_child");

        _outMmap = CreateOrOpen(outName, PageSize);
        _inMmap = CreateOrOpen(inName, PageSize);

        // Reset sync state to Idle to clear any stale state left by a previously
        // killed peer process sharing the same memory-mapped section name.
        try
        {
            var resetView = _inMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
            resetView.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateIdle), 0, 4);
            resetView.Dispose();
        }
        catch { }

        File.WriteAllText(_bootstrapPath, $"{_outMmap.Key}\n{_inMmap.Key}");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (CheckChildReady()) break;
            await Task.Delay(50, ct);
        }
        if (ct.IsCancellationRequested)
            throw new OperationCanceledException("[MmapTransport] Cancelled during StartServerAsync");
        if (DateTime.UtcNow >= deadline)
            throw new TimeoutException("Mmap connect timeout — child did not become ready");

        var idle = BitConverter.GetBytes(StateIdle);
        _outView = _outMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
        _outView.WriteArray<byte>(SyncOffset, idle, 0, 4);
        _inView = _inMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
        _inView.WriteArray<byte>(SyncOffset, idle, 0, 4);
    }

    private bool CheckChildReady()
    {
        try
        {
            var view = _inMmap!.Shm.CreateViewAccessor(0, 8, MemoryMappedFileAccess.Read);
            var sync = new byte[4];
            view.ReadArray<byte>(0, sync, 0, 4);
            view.Dispose();
            return BitConverter.ToInt32(sync, 0) == StateReady;
        }
        catch { return false; }
    }

    public async Task ConnectClientAsync()
    {
        string[] readLines = null!;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(_bootstrapPath))
                {
                    readLines = File.ReadAllLines(_bootstrapPath);
                    if (readLines.Length >= 2) break;
                }
            }
            catch { }
            await Task.Delay(20);
        }
        if (!File.Exists(_bootstrapPath))
            throw new TimeoutException("Mmap connect timeout — bootstrap file not found");

        readLines = File.ReadAllLines(_bootstrapPath);
        // Line 0 = child's _outMmap (child writes, parent reads → parent's _inMmap)
        // Line 1 = child's _inMmap (child reads, parent writes → parent's _outMmap)
        var childOutKey = readLines[0];
        var childInKey = readLines[1];

        await Task.Delay(200);

        _inMmap = OpenMmapRetry(childOutKey, 10000);
        _outMmap = OpenMmapRetry(childInKey, 10000);

        // Signal the child that we are connected. The child polls its _inMmap
        // (main_to_child) for StateReady; our _outMmap is the same section.
        var readyView = _outMmap.Shm.CreateViewAccessor(0, 8, MemoryMappedFileAccess.ReadWrite);
        readyView.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateReady), 0, 4);
        readyView.Dispose();

        // Reset the child's _outMmap (child_to_main) to idle so the child's
        // first SendAsync doesn't see stale data in the handshake state.
        readyView = _inMmap.Shm.CreateViewAccessor(0, 8, MemoryMappedFileAccess.ReadWrite);
        readyView.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateIdle), 0, 4);
        readyView.Dispose();

        _inView = _inMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
        _outView = _outMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
    }

    private static SharedMmap OpenMmapRetry(string key, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try { return OpenMmap(key); }
            catch { }
            Thread.Sleep(20);
        }
        throw new TimeoutException($"Mmap connect timeout — mapped file '{key}' not found");
    }

    // ── 跨平台 CreateOrOpen ──────────────────────────────────────
    //
    // System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(name, capacity, access)
    // 在非 Windows 平台上仅接受本地路径样式的 name，不能创建真正的系统级共享节。
    // 解决方式：Windows 直接使用命名节；其他平台使用文件映射（file-backed mmap），
    // 所有进程使用同一条绝对路径作为 key 来共享同一文件。

    private string MmapKey(string suffix)
    {
        if (IsWindows)
        {
            // Local\ namespace is not available on Windows XP (Vista+)
            if (Environment.OSVersion.Version.Major < 6)
                return "Acrux_Mmap_" + _channelName + "_" + suffix;
            return "Local\\Acrux_Mmap_" + _channelName + "_" + suffix;
        }
        return Path.Combine(Path.GetTempPath(), $"acrux_mmap_{_channelName}_{suffix}.bin");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Call is guarded by runtime OS check (IsWindows) and an alternative implementation exists for non-Windows platforms.")]
    private SharedMmap CreateOrOpen(string key, int capacity)
    {
        if (IsWindows)
            return new SharedMmap(key, MemoryMappedFile.CreateOrOpen(key, capacity, MemoryMappedFileAccess.ReadWrite));
        return OpenFileMmap(key, capacity);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Call is guarded by runtime OS check (IsWindows) and an alternative implementation exists for non-Windows platforms.")]
    private static SharedMmap OpenMmap(string key)
    {
        if (IsWindows)
            return new SharedMmap(key, MemoryMappedFile.OpenExisting(key));
        return OpenFileMmap(key, 0);
    }

    private static SharedMmap OpenFileMmap(string key, int capacity)
    {
        // Unix/macOS have no named-section concept: the backing file path alone
        // identifies the shared mapping across processes, so mapName must be null
        // (a non-null name throws "Named maps are not supported" off Windows).
        var shm = MemoryMappedFile.CreateFromFile(key, FileMode.OpenOrCreate, null, (long)(capacity > 0 ? capacity : PageSize), MemoryMappedFileAccess.ReadWrite);
        return new SharedMmap(key, shm);
    }

    public async Task SendAsync(byte[] data)
    {
        await SendAsync(data, TimeSpan.FromSeconds(2));
    }

    private async Task SendAsync(byte[] data, TimeSpan timeout)
    {
        if (_disposed || _outView == null) return;
        var len = data.Length;
        if (len > MaxMsgSize)
            throw new InvalidOperationException($"Message too large: {len}");

        var deadline = DateTime.UtcNow + timeout;
        var sw = new SpinWait();
        while (DateTime.UtcNow < deadline)
        {
            var sync = new byte[4];
            _outView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            if (BitConverter.ToInt32(sync, 0) == StateIdle) break;
            sw.SpinOnce();
        }
        if (DateTime.UtcNow >= deadline)
        {
            ResetSyncToIdle(_outView);
            var sync = new byte[4];
            _outView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            var finalState = BitConverter.ToInt32(sync, 0);
            throw new TimeoutException($"SendAsync: timeout waiting for channel to become idle (final_state={finalState})");
        }

        var header = BitConverter.GetBytes(len);
        var writing = BitConverter.GetBytes(StateWriting);
        _outView.WriteArray<byte>(SyncOffset, writing, 0, 4);
        _outView.WriteArray<byte>(HeaderOffset, header, 0, HeaderSize);
        if (len > 0)
            _outView.WriteArray<byte>(DataOffset, data, 0, len);

        var ready = BitConverter.GetBytes(StateReady);
        _outView.WriteArray<byte>(SyncOffset, ready, 0, 4);
    }

    /// <summary>
    /// 异步发送消息，无需等待，不阻塞。适用于"发后即忘"场景（如 Execute 调用）。
    /// 如果通道未空闲，先强制重置，再发送。
    /// </summary>
    public async Task SendAsyncNoWait(byte[] data)
    {
        await SendAsyncNoWaitImpl(data);
    }

    private async Task SendAsyncNoWaitImpl(byte[] data)
    {
        if (_disposed || _outView == null) return;
        var len = data.Length;
        if (len > MaxMsgSize) return;

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
        var sw = new SpinWait();
        while (DateTime.UtcNow < deadline)
        {
            var sync = new byte[4];
            _outView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            if (BitConverter.ToInt32(sync, 0) == StateIdle) break;
            sw.SpinOnce();
        }

        ResetSyncToIdle(_outView);

        var header = BitConverter.GetBytes(len);
        var writing = BitConverter.GetBytes(StateWriting);
        _outView.WriteArray<byte>(SyncOffset, writing, 0, 4);
        _outView.WriteArray<byte>(HeaderOffset, header, 0, HeaderSize);
        if (len > 0)
            _outView.WriteArray<byte>(DataOffset, data, 0, len);

        var ready = BitConverter.GetBytes(StateReady);
        _outView.WriteArray<byte>(SyncOffset, ready, 0, 4);
    }

    /// <summary>
    /// 纯同步"发后即忘"发送——无 await、无 Task、无上下文切换。
    /// 内联自旋等待（最多 100ms），超时后强制重置通道。
    /// 适用于 UI 线程高频调用（如页面加载时的批量 Execute）。
    /// </summary>
    public void SendFireAndForget(byte[] data)
    {
        if (_disposed || _outView == null) return;
        var len = data.Length;
        if (len > MaxMsgSize) return;

        // 内联自旋：先快速轮询 10 次（约几微秒），再降速到 Thread.Sleep
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
        var pollCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            var sync = new byte[4];
            _outView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            if (BitConverter.ToInt32(sync, 0) == StateIdle) break;
            pollCount++;
            if (pollCount > 20)
            {
                Thread.Sleep(1);
                pollCount = 0;
            }
        }

        // 超时则强制重置通道，确保不卡死
        ResetSyncToIdle(_outView);

        var header = BitConverter.GetBytes(len);
        var writing = BitConverter.GetBytes(StateWriting);
        _outView.WriteArray<byte>(SyncOffset, writing, 0, 4);
        _outView.WriteArray<byte>(HeaderOffset, header, 0, HeaderSize);
        if (len > 0)
            _outView.WriteArray<byte>(DataOffset, data, 0, len);

        var ready = BitConverter.GetBytes(StateReady);
        _outView.WriteArray<byte>(SyncOffset, ready, 0, 4);
    }

    private void ResetSyncToIdle(MemoryMappedViewAccessor view)
    {
        try
        {
            view.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateIdle), 0, 4);
        }
        catch { }
    }

    public async Task<byte[]> ReceiveAsync(CancellationToken ct = default)
    {
        if (_disposed || _inView == null)
            return Array.Empty<byte>();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        var sw = new SpinWait();
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var sync = new byte[4];
            _inView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            if (BitConverter.ToInt32(sync, 0) == StateReady) break;
            sw.SpinOnce();
        }
    if (ct.IsCancellationRequested)
        throw new OperationCanceledException("[MmapTransport] Cancelled during ReceiveAsync");
    if (DateTime.UtcNow >= deadline)
    {
        ResetSyncToIdle(_inView);
        var sync = new byte[4];
        _inView.ReadArray<byte>(SyncOffset, sync, 0, 4);
        var finalState = BitConverter.ToInt32(sync, 0);
        throw new TimeoutException($"ReceiveAsync: timeout waiting for Ready (final_state={finalState})");
    }

        var header = new byte[HeaderSize];
        int? len = null;
        var headerSw = new SpinWait();
        for (int i = 0; i < 10; i++)
        {
            _inView.ReadArray<byte>(HeaderOffset, header, 0, HeaderSize);
            var l = BitConverter.ToInt32(header, 0);
            var h2 = new byte[HeaderSize];
            _inView.ReadArray<byte>(HeaderOffset, h2, 0, HeaderSize);
            if (BitConverter.ToInt32(h2, 0) == l) { len = l; break; }
            headerSw.SpinOnce();
        }
        if (len == null)
            throw new InvalidOperationException("Header inconsistent — connection closed");

        var finalLen = (int)len;
        if (finalLen < 0 || finalLen > MaxMsgSize)
            throw new InvalidOperationException($"Invalid message length: {finalLen}");
        if (finalLen == 0)
        {
            _inView.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateIdle), 0, 4);
            return Array.Empty<byte>();
        }

        var data = new byte[finalLen];
        _inView.ReadArray<byte>(DataOffset, data, 0, finalLen);

        var idle = BitConverter.GetBytes(StateIdle);
        _inView.WriteArray<byte>(SyncOffset, idle, 0, 4);

        return data;
    }

    /// <summary>
    /// 接收消息（用于接收循环），超时时间较长且不抛出异常。
    /// 返回 (成功, 数据)：成功=false 表示超时，可安全重试。
    /// </summary>
    public async Task<(bool Success, byte[] Data)> ReceiveAsyncNoThrow(CancellationToken ct, int timeoutSeconds = 30)
    {
        if (_disposed || _inView == null)
            return (false, Array.Empty<byte>());

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var sw = new SpinWait();
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var sync = new byte[4];
            _inView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            if (BitConverter.ToInt32(sync, 0) == StateReady) break;
            sw.SpinOnce();
        }
    if (ct.IsCancellationRequested)
        return (false, Array.Empty<byte>());
    if (DateTime.UtcNow >= deadline)
    {
        // 超时（空闲）：重置同步标志，避免后续 SendAsync 卡死
        ResetSyncToIdle(_inView);
        return (false, Array.Empty<byte>());
    }

        var header = new byte[HeaderSize];
        int? len = null;
        var headerSw = new SpinWait();
        for (int i = 0; i < 10; i++)
        {
            _inView.ReadArray<byte>(HeaderOffset, header, 0, HeaderSize);
            var l = BitConverter.ToInt32(header, 0);
            var h2 = new byte[HeaderSize];
            _inView.ReadArray<byte>(HeaderOffset, h2, 0, HeaderSize);
            if (BitConverter.ToInt32(h2, 0) == l) { len = l; break; }
            headerSw.SpinOnce();
        }
        if (len == null)
            return (false, Array.Empty<byte>());

        var finalLen = (int)len;
        if (finalLen < 0 || finalLen > MaxMsgSize)
            return (false, Array.Empty<byte>());
        if (finalLen == 0)
        {
            _inView.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateIdle), 0, 4);
            return (true, Array.Empty<byte>());
        }

        var data = new byte[finalLen];
        _inView.ReadArray<byte>(DataOffset, data, 0, finalLen);

        var idle = BitConverter.GetBytes(StateIdle);
        _inView.WriteArray<byte>(SyncOffset, idle, 0, 4);

        return (true, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _outView?.Dispose(); } catch { }
        try { _inView?.Dispose(); } catch { }
        try { _outMmap?.Dispose(); } catch { }
        try { _inMmap?.Dispose(); } catch { }
        try { File.Delete(_bootstrapPath); } catch { }
    }
}

/// <summary>跨平台共享内存封装</summary>
internal sealed class SharedMmap : IDisposable
{
    public readonly string Key;
    public readonly MemoryMappedFile Shm;
    private bool _disposed;

    public SharedMmap(string key, MemoryMappedFile shm)
    {
        Key = key;
        Shm = shm;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shm.Dispose();
    }
}
