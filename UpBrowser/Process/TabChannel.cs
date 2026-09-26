using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;

namespace UpBrowser.Process;

/// <summary>
/// Wire messages between the browser (parent) and a tab-host child process.
/// Framing: [int32 payloadLength][byte msgType][payload]. Strings are UTF-8
/// with an int32 length prefix; frame pixels ride in the payload of Frame.
/// Transport is a duplex named pipe — System.IO.Pipes maps this to AF_UNIX
/// sockets on Linux/macOS and \\.\pipe on Windows, so it is cross-platform.
/// </summary>
internal enum TabMsg : byte
{
    // Parent → child
    Navigate = 1,       // [string url]
    NavigateHtml = 2,   // [string html][string baseUrl]
    MouseDown = 3,      // [float x][float y]
    MouseUp = 4,        // [float x][float y]
    MouseMove = 5,      // [float x][float y]
    Wheel = 6,          // [float dx][float dy][float x][float y]
    Resize = 7,         // [float w][float h]
    SetActive = 8,      // [byte active]
    Close = 9,          // []
    ScrollTo = 10,      // [float x][float y]
    KeyDown = 11,       // [uint16 charCode][uint16 key][byte repeat]
    Char = 12,          // [uint16 charCode]

    // Child → parent
    Ready = 20,         // []
    Frame = 21,         // [int w][int h][float scrollX][float scrollY][float contentW][float contentH]
                        // [int domCount][int boxCount][byte mode][int x][int y][int rw][int rh][int pixelLen][pixels BGRA8888]
    Title = 22,         // [string]
    UrlChanged = 23,    // [string]
    Loading = 24,       // [byte loading]
    Dialog = 25,        // [string message][string type]
    Dead = 26,          // [string reason]
    ScrollChanged = 27, // [float x][float y] — child-initiated scroll (scrollTo/anchor/scrollIntoView)
    WheelResult = 28,   // [byte consumed] — reply to Wheel: element scroller ate it or not
}

/// <summary>Frame payload modes (byte at the mode field of TabMsg.Frame).</summary>
internal enum FrameMode : byte
{
    /// <summary>Full viewport pixels in [0..w)×[0..h).</summary>
    Full = 0,
    /// <summary>Row band [y..y+rh) × full width — pixel-diffed damage.</summary>
    DamageRows = 1,
    /// <summary>Scroll-only frame: parent shifts its cached bitmap by the delta
    /// between the new and previous frame scroll, then overlays rows [y..y+rh).</summary>
    ScrollBlit = 2,
}

internal readonly struct TabMessage
{
    public TabMsg Type { get; }
    public byte[] Payload { get; }

    public TabMessage(TabMsg type, byte[] payload)
    {
        Type = type;
        Payload = payload;
    }
}

internal static class TabFraming
{
    public static byte[] BuildPayload(Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            body(w);
        return ms.ToArray();
    }

    public static void Write(Stream pipe, TabMsg type, byte[] payload)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), payload.Length);
        header[4] = (byte)type;
        pipe.Write(header, 0, header.Length);
        if (payload.Length > 0)
            pipe.Write(payload, 0, payload.Length);
        pipe.Flush();
    }

    /// <summary>Blocking read of one message. Returns false at EOF / broken pipe.</summary>
    public static bool TryRead(Stream pipe, out TabMessage msg)
    {
        msg = default;
        var header = new byte[5];
        try
        {
            if (!ReadExact(pipe, header, 5)) return false;
            int len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
            if (len < 0 || len > 256 * 1024 * 1024) return false;
            var payload = len > 0 ? new byte[len] : Array.Empty<byte>();
            if (len > 0 && !ReadExact(pipe, payload, len)) return false;
            msg = new TabMessage((TabMsg)header[4], payload);
            return true;
        }
        catch (IOException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    private static bool ReadExact(Stream pipe, byte[] buffer, int count)
    {
        int off = 0;
        while (off < count)
        {
            int n;
            try { n = pipe.Read(buffer, off, count - off); }
            catch (IOException) { return false; }
            if (n <= 0) return false;
            off += n;
        }
        return true;
    }

    // ---- payload helpers ----

    public static void WriteString(BinaryWriter w, string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s ?? "");
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    public static string ReadString(BinaryReader r)
    {
        int len = r.ReadInt32();
        if (len <= 0) return "";
        return System.Text.Encoding.UTF8.GetString(r.ReadBytes(len));
    }
}

internal static class TabPipeNames
{
    public static string ForTab(int tabIndex) =>
        $"UpBrowser_Tab_{Environment.ProcessId:x8}_{tabIndex}_{Interlocked.Increment(ref _seq)}";

    private static int _seq;
}
