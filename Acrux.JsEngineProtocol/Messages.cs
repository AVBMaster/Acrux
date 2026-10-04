using System.Text.Json.Serialization;

namespace Acrux.JsEngineProtocol;

/// <summary>IPC 请求类型</summary>
public enum RequestType : byte
{
    Execute,          // 执行 JS 代码（无返回值）
    Evaluate,         // 执行 JS 表达式（有返回值）
    CallFunction,     // 调用 JS 函数
    InvokeCallback,   // 触发回调
    RemoveCallback,   // 移除回调
    DomCall,          // 远程引擎→主进程的 DOM 调用
    DisposeEngine,    // 释放引擎
    Ping,             // 心跳
}

/// <summary>IPC 响应类型</summary>
public enum ResponseType : byte
{
    Success,
    Error,
    CallbackInvoked,
    Pong,
}

/// <summary>IPC 请求</summary>
public class IpcRequest
{
    /// <summary>JSON 线路上用于区分请求/响应的标签</summary>
    [JsonPropertyName("__kind")]
    public string Kind => "request";

    public bool IsRequest { get; set; } = true;
    public long RequestId { get; set; }
    public RequestType Type { get; set; }
    public string? Payload { get; set; }
    public int ProxyId { get; set; }
    public int CallbackId { get; set; }
    public long TimestampMs { get; set; }
}

/// <summary>IPC 响应</summary>
public class IpcResponse
{
    [JsonPropertyName("__kind")]
    public string Kind => "response";

    public bool IsRequest { get; set; } = false;
    public long RequestId { get; set; }
    public ResponseType Type { get; set; }
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public int NewProxyId { get; set; }
    public int CallbackId { get; set; }
    public int NewElementId { get; set; }
}

/// <summary>DOM 代理信息</summary>
public class ProxyInfo
{
    public int ProxyId { get; set; }
    public string? TypeName { get; set; }
    public string? TagName { get; set; }
    public string? NodeValue { get; set; }
}

/// <summary>引擎进程启动参数</summary>
public record EngineStartupArgs
{
    public string ChannelName { get; init; } = "";
    public string EngineType { get; init; } = "Jint";
    public int TabIndex { get; init; }
}
