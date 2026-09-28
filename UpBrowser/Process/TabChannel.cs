using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;

namespace UpBrowser.Process;

/// <summary>
/// Duplex transport for the shell &lt;-&gt; page-host control channel. Framing is
/// [int32 payloadLength][byte msgType][payload]; strings are UTF-8 with an int32
/// length prefix. Transport is a named pipe — System.IO.Pipes maps this to AF_UNIX
/// sockets on Linux/macOS and \\.\pipe on Windows, so it is cross-platform.
/// Message ids and payload layouts live in <see cref="UpBrowser.PageContract.TabMsg"/>.
/// </summary>
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
