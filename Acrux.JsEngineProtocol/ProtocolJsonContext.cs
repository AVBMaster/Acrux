using System.Text.Json.Serialization;

namespace Acrux.JsEngineProtocol;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
[JsonSerializable(typeof(ProxyInfo))]
[JsonSerializable(typeof(EngineStartupArgs))]
internal partial class ProtocolJsonContext : JsonSerializerContext
{
}
