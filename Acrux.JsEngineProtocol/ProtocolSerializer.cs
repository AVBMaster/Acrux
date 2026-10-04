using System.Text.Json;

namespace Acrux.JsEngineProtocol;

/// <summary>
/// IPC 协议序列化器。使用 JsonSerializer 读写 JSON。
/// </summary>
public static class ProtocolSerializer
{
    // 使用 System.Text.Json 源生成以支持 AOT 环境

    public static byte[] Serialize(IpcRequest value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, ProtocolJsonContext.Default.IpcRequest);
    }

    public static byte[] Serialize(IpcResponse value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, ProtocolJsonContext.Default.IpcResponse);
    }

    public static IpcRequest? DeserializeIpcRequest(byte[] data)
    {
        try { return JsonSerializer.Deserialize<IpcRequest>(data, ProtocolJsonContext.Default.IpcRequest); }
        catch { return default; }
    }

    public static IpcResponse? DeserializeIpcResponse(byte[] data)
    {
        try { return JsonSerializer.Deserialize<IpcResponse>(data, ProtocolJsonContext.Default.IpcResponse); }
        catch { return default; }
    }

    public static (bool IsRequest, IpcRequest? Request, IpcResponse? Response) DeserializeAny(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("__kind", out var kind) && kind.GetString() == "request")
                return (true, JsonSerializer.Deserialize<IpcRequest>(data, ProtocolJsonContext.Default.IpcRequest), null);
            return (false, null, JsonSerializer.Deserialize<IpcResponse>(data, ProtocolJsonContext.Default.IpcResponse));
        }
        catch { return (false, null, null); }
    }
}
