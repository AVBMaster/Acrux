#if USE_MULTIPLE_JS_ENGINE
using System.Diagnostics;
using System.Text.Json;
using Acrux.JsEngineProtocol;

namespace Acrux.Core.JavaScript;

/// <summary>
/// 远程 JS 引擎适配器，通过 IPC �?Acrux.JsEngineHost 进程通信�?
/// JsEngineHost 进程不进�?AOT 编译，因此可以正常使用反射�?
/// </summary>
public class RemoteJsEngineAdapter : IJavaScriptEngineAdapter, IDisposable
{
    private MmapTransport? _transport;
    private System.Diagnostics.Process? _hostProcess;
    private long _nextRequestId;
    private readonly string _channelName;
    private readonly string _engineType;
    private readonly int _tabIndex;
    private bool _disposed;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly Dictionary<long, TaskCompletionSource<IpcResponse>> _pending = new();
    private readonly object _lock = new();

    /// <summary>浏览器回调，�?BrowserApp 设置</summary>
    public Action<string>? OnAlert { get; set; }
    public Func<string, bool>? OnConfirm { get; set; }
    public Func<string, string, string?>? OnPrompt { get; set; }
    public Func<int>? OnGetInnerWidth { get; set; }
    public Func<int>? OnGetInnerHeight { get; set; }
    public Action<int, int>? OnScrollTo { get; set; }
    public Action<int, int>? OnScrollBy { get; set; }
    public string? OnFetch { get; set; } // JSON 格式�?fetch 结果
    public Func<string, string, string>? OnEngineAction { get; set; } // 引擎管理回调

    /// <summary>JS console 输出事件，参�? (method, message)</summary>
    public event Action<string, string>? OnConsoleLog;

    public DomProxyStore? DomStore { get; set; }

    public JsEngineType EngineType => _engineType switch
    {
        "V8" => JsEngineType.V8,
        "Jurassic" => JsEngineType.Jurassic,
        _ => JsEngineType.Jint
    };
    public object? InnerEngine => null;
    public bool SupportsHostObjects => true;
    public bool SupportsES6Proxy => true;

    /// <summary>
    /// JS 引擎是否已就绪（进程启动、IPC 连接成功）�?
    /// </summary>
    public bool IsReady { get; private set; }

    public RemoteJsEngineAdapter(string channelName, string engineType, int tabIndex)
    {
        _channelName = channelName;
        _engineType = engineType;
        _tabIndex = tabIndex;
    }

    /// <summary>
    /// 同步启动（供 EngineProcessManager 调用）�?
    /// </summary>
    public void Start()
    {
        StartAsync().Wait();
    }

    public async Task StartAsync()
    {
        // 启动 JsEngineHost 进程
        var hostPath = FindHostExe();
        if (hostPath == null)
            throw new InvalidOperationException("JsEngineHost.exe not found");

        var isDll = hostPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var parentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        var startInfo = new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : hostPath,
            Arguments = isDll
                ? $"\"{hostPath}\" --channel={_channelName} --engine={_engineType} --tab={_tabIndex} --parent={parentPid}"
                : $"--channel={_channelName} --engine={_engineType} --tab={_tabIndex} --parent={parentPid}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        _hostProcess = System.Diagnostics.Process.Start(startInfo);
        if (_hostProcess == null)
            throw new InvalidOperationException($"Failed to start JsEngineHost process: {hostPath}");

        _hostProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[JsEngineHost] {e.Data}");
        };
        _hostProcess.BeginErrorReadLine();

        // Wait for child process to create bootstrap file (signals it's ready)
        var escaped = _channelName.Replace("\\", "_").Replace("/", "_").Replace(":", "_");
        var bootstrapPath = Path.Combine(Path.GetTempPath(), $"acrux_mmap_{escaped}.bootstrap");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(bootstrapPath)) break;
            if (_hostProcess.HasExited)
            {
                var exitCode = _hostProcess.ExitCode;
                _hostProcess.Dispose();
                _hostProcess = null;
                throw new InvalidOperationException($"JsEngineHost exited immediately (code {exitCode}). " +
                    "Check that all dependencies are available in the output directory.");
            }
            await Task.Delay(20);
        }
        if (_hostProcess.HasExited)
        {
            var exitCode = _hostProcess.ExitCode;
            _hostProcess.Dispose();
            _hostProcess = null;
            throw new InvalidOperationException($"JsEngineHost failed to start (exit code {exitCode}).");
        }

        _hostProcess.EnableRaisingEvents = true;
        _hostProcess.Exited += (_, _) =>
        {
            Console.WriteLine($"[JsEngineHost] Process {_hostProcess?.Id} exited");
            _hostProcess?.Dispose();
            _hostProcess = null;
        };

        // 连接内存映射 IPC �?不注�?AppDomain 全局退出事件，避免跨标签页误触�?
        _transport = new MmapTransport(_channelName);
        await _transport.ConnectClientAsync();

        // 标记引擎已就�?
        IsReady = true;
        Console.WriteLine($"[RemoteJsEngine] tab={_tabIndex} IPC connected, engine ready");

        // 启动后台线程接收远程引擎�?DomCall 请求
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token));
    }

    private static string? FindHostExe()
    {
        // 1. 当前目录（发布后�?
        var exe = Path.Combine(AppContext.BaseDirectory, "Acrux.JsEngineHost.exe");
        if (File.Exists(exe)) return exe;
        var dll = Path.Combine(AppContext.BaseDirectory, "Acrux.JsEngineHost.dll");
        if (File.Exists(dll)) return dll;

        // 2. 开发环境：从项目输出目录查�?
        var baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;

            // DLL（无 RuntimeIdentifier �?build 输出�?
            var devDll = Path.Combine(baseDir, "Acrux.JsEngineHost", "bin", "Debug", "net10.0", "Acrux.JsEngineHost.dll");
            if (File.Exists(devDll)) return devDll;
            var releaseDll = Path.Combine(baseDir, "Acrux.JsEngineHost", "bin", "Release", "net10.0", "Acrux.JsEngineHost.dll");
            if (File.Exists(releaseDll)) return releaseDll;

            // EXE（带 RuntimeIdentifier �?build 输出�?
            var ridExe = Path.Combine(baseDir, "Acrux.JsEngineHost", "bin", "Debug", "net10.0", "win-x64", "Acrux.JsEngineHost.exe");
            if (File.Exists(ridExe)) return ridExe;
            var ridReleaseExe = Path.Combine(baseDir, "Acrux.JsEngineHost", "bin", "Release", "net10.0", "win-x64", "Acrux.JsEngineHost.exe");
            if (File.Exists(ridReleaseExe)) return ridReleaseExe;
        }

        return null;
    }

    private void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;

        _receiveCts?.Cancel();

        // 保存局部引用，避免 Exited 事件处理程序在异步线程中将其置为 null
        var process = _hostProcess;
        if (process != null && !process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch { }
            try
            {
                process.WaitForExit(3000);
            }
            catch { }
            try
            {
                process.Dispose();
            }
            catch { }
            _hostProcess = null;
        }

        // 如果 Exited 事件处理程序已经 Dispose 并置�?null，这里确保清�?
        if (_hostProcess != null)
        {
            try { _hostProcess.Dispose(); } catch { }
            _hostProcess = null;
        }

        _transport?.Dispose();
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _transport != null)
        {
            try
            {
                var (success, bytes) = await _transport.ReceiveAsyncNoThrow(ct);
                if (!success) continue;
                if (bytes.Length == 0) continue;

                var msg = ProtocolSerializer.DeserializeAny(bytes);
            if (msg.Request != null)
            {
                if (msg.Request.Type == RequestType.DomCall)
                {
                    var reqCopy = msg.Request;
                    try
                    {
                        var result = HandleDomCall(reqCopy);
                        var responseData = ProtocolSerializer.Serialize(result);
                        if (_transport != null)
                            _transport.SendAsync(responseData).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RemoteJsEngine] DomCall ERROR reqId={reqCopy.RequestId}: {ex.Message}");
                        var errorResponse = new IpcResponse
                        {
                            RequestId = reqCopy.RequestId,
                            Success = false,
                            Error = ex.Message
                        };
                        var errorData = ProtocolSerializer.Serialize(errorResponse);
                        if (_transport != null)
                            _transport.SendAsync(errorData).GetAwaiter().GetResult();
                    }
                }
                continue;
            }

            if (msg.Response != null)
            {
                TaskCompletionSource<IpcResponse>? tcs;
                lock (_lock)
                {
                    if (_pending.TryGetValue(msg.Response.RequestId, out tcs))
                        _pending.Remove(msg.Response.RequestId);
                }
                if (tcs != null)
                {
                    tcs.TrySetResult(msg.Response);
                }
            }
        }
        catch (OperationCanceledException) { break; }
        catch (EndOfStreamException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteJsEngine] Receive error: {ex.Message}");
        }
    }
    }

    private IpcResponse HandleDomCall(IpcRequest request)
    {
        try
        {
            var payload = request.Payload ?? "";
            var sep = payload.IndexOf('|');
            if (sep < 0) return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid payload" };

            var method = payload[..sep];
            var argsJson = payload[(sep + 1)..];

            return HandleDomCallInner(method, argsJson, request);
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private static string? Truncate(string? s, int max)
    {
        if (s == null) return "null";
        return s.Length <= max ? s : s[..max] + "...";
    }

    private IpcResponse HandleDomCallInner(string method, string argsJson, IpcRequest request)
    {
        return method switch
        {
                "console.log" or "console.error" or "console.warn"
                    or "console.info" or "console.debug" => HandleConsoleLog(method, argsJson),
                "setTimeout" => HandleSetTimeout(request, argsJson),
                "setInterval" => HandleSetInterval(request, argsJson),
                "clearTimeout" or "clearInterval" => RespondOk(request),
                "innerWidth" => JsonResult(request, (OnGetInnerWidth?.Invoke() ?? 1024).ToString()),
                "innerHeight" => JsonResult(request, (OnGetInnerHeight?.Invoke() ?? 768).ToString()),
                "scrollTo" => HandleAction(request, argsJson, a => OnScrollTo?.Invoke(a[0], a[1])),
                "scrollBy" => HandleAction(request, argsJson, a => OnScrollBy?.Invoke(a[0], a[1])),
                "alert" => HandleAlert(request, argsJson),
                "confirm" => JsonResult(request, OnConfirm?.Invoke(GetStringArg(argsJson, 0) ?? "") == true ? "true" : "false"),
                "prompt" => JsonResult(request, OnPrompt?.Invoke(GetStringArg(argsJson, 0) ?? "", GetStringArg(argsJson, 1) ?? "") ?? ""),
                "atob" => JsonResult(request, DecodeBase64(GetStringArg(argsJson, 0) ?? "")),
                "btoa" => JsonResult(request, EncodeBase64(GetStringArg(argsJson, 0) ?? "")),
                "fetch" => HandleFetch(request, argsJson),
                "dom_getPropertyValue" => HandleDomGetPropertyValue(request, argsJson),
                "dom_getProperty" => HandleDomGetProperty(request, argsJson),
                "dom_setProperty" => HandleDomSetProperty(request, argsJson),
                "dom_setStyleProperty" => HandleDomSetStyleProperty(request, argsJson),
                "dom_getStyleProperty" => HandleDomGetStyleProperty(request, argsJson),
                "dom_setCssText" => HandleDomSetCssText(request, argsJson),
                "dom_getCssText" => HandleDomGetCssText(request, argsJson),
                "dom_createElement" => HandleDomCreateElement(request, argsJson),
                "dom_createElementBatch" => HandleDomCreateElementBatch(request, argsJson),
                "dom_appendChild" => HandleDomAppendChild(request, argsJson),
                "dom_insertBefore" => HandleDomInsertBefore(request, argsJson),
                "dom_removeChild" => HandleDomRemoveChild(request, argsJson),
                "dom_remove" => HandleDomRemove(request, argsJson),
                "dom_setAttribute" => HandleDomSetAttribute(request, argsJson),
                "dom_getAttribute" => HandleDomGetAttribute(request, argsJson),
                "dom_removeAttribute" => HandleDomRemoveAttribute(request, argsJson),
                "dom_setClassName" => HandleDomSetClassName(request, argsJson),
                "dom_setId" => HandleDomSetId(request, argsJson),
                "dom_setValue" => HandleDomSetValue(request, argsJson),
                "dom_setTextContent" => HandleDomSetTextContent(request, argsJson),
                "dom_setInnerHTML" => HandleDomSetInnerHTML(request, argsJson),
                "dom_dispatchEvent" => HandleDomDispatchEvent(request, argsJson),
                "dom_getBoundingClientRect" => HandleDomGetBoundingClientRect(request, argsJson),
                "dom_click" => HandleDomClick(request, argsJson),
                "dom_getChildren" => HandleDomGetChildren(request, argsJson),
                "dom_getParent" => HandleDomGetParent(request, argsJson),
                "dom_nextSibling" => HandleDomNextSibling(request, argsJson),
                "dom_previousSibling" => HandleDomPreviousSibling(request, argsJson),
                "dom_focus" => HandleDomFocus(request, argsJson),
                "dom_blur" => HandleDomBlur(request, argsJson),
                "dom_scrollIntoView" => HandleDomScrollIntoView(request, argsJson),
                "dom_insertAdjacentHTML" => HandleDomInsertAdjacentHTML(request, argsJson),
                "dom_replaceWith" => HandleDomReplaceWith(request, argsJson),
                "dom_before" => HandleDomBefore(request, argsJson),
                "dom_after" => HandleDomAfter(request, argsJson),
                "dom_cloneNode" => HandleDomCloneNode(request, argsJson),
                "dom_toggleAttribute" => HandleDomToggleAttribute(request, argsJson),
                "dom_getTag" => HandleDomGetTag(request, argsJson),
                "dom_getClassName" => HandleDomGetClassName(request, argsJson),
                "dom_getId" => HandleDomGetId(request, argsJson),
                "dom_getValue" => HandleDomGetValue(request, argsJson),
                "dom_getTextContent" => HandleDomGetTextContent(request, argsJson),
                "dom_getInnerHTML" => HandleDomGetInnerHTML(request, argsJson),
                "dom_hasAttribute" => HandleDomHasAttribute(request, argsJson),
                "dom_getNodeType" => HandleDomGetNodeType(request, argsJson),
                "dom_contains" => HandleDomContains(request, argsJson),
                "dom_getOffsetWidth" => HandleDomGetOffsetWidth(request, argsJson),
                "dom_getOffsetHeight" => HandleDomGetOffsetHeight(request, argsJson),
                "dom_getClientWidth" => HandleDomGetClientWidth(request, argsJson),
                "dom_getClientHeight" => HandleDomGetClientHeight(request, argsJson),
                "dom_getScrollTop" => HandleDomGetScrollTop(request, argsJson),
                "dom_getScrollLeft" => HandleDomGetScrollLeft(request, argsJson),
                "dom_getScrollWidth" => HandleDomGetScrollWidth(request, argsJson),
                "dom_getScrollHeight" => HandleDomGetScrollHeight(request, argsJson),
                "dom_setScrollTop" => HandleDomSetScrollTop(request, argsJson),
                "dom_setScrollLeft" => HandleDomSetScrollLeft(request, argsJson),
                "dom_getOffsetTop" => HandleDomGetOffsetTop(request, argsJson),
                "dom_getOffsetLeft" => HandleDomGetOffsetLeft(request, argsJson),
                "dom_getComputedStyle" => HandleDomGetComputedStyle(request, argsJson),
                "dom_addEventListener" => HandleDomAddEventListener(request, argsJson),
                "dom_removeEventListener" => HandleDomRemoveEventListener(request, argsJson),
                "dom_matches" => HandleDomMatches(request, argsJson),
                "dom_closest" => HandleDomClosest(request, argsJson),
                "dom_normalize" => HandleDomNormalize(request, argsJson),
                "dom_getClassList" => HandleDomGetClassList(request, argsJson),
                "dom_getChildNodes" => HandleDomGetChildNodes(request, argsJson),
                "dom_getFirstElementChild" => HandleDomGetFirstElementChild(request, argsJson),
                "dom_getLastElementChild" => HandleDomGetLastElementChild(request, argsJson),
                "dom_getChildElementCount" => HandleDomGetChildElementCount(request, argsJson),
                "dom_querySelector" => HandleDomQuerySelector(request, argsJson),
                "dom_querySelectorAll" => HandleDomQuerySelectorAll(request, argsJson),
                "dom_getElementsByTagName" => HandleDomGetElementsByTagName(request, argsJson),
                "dom_getElementsByClassName" => HandleDomGetElementsByClassName(request, argsJson),
                "dom_getElementsByName" => HandleDomGetElementsByName(request, argsJson),
                "dom_getElementById" => HandleDomGetElementById(request, argsJson),
                "dom_getPropertyBatch" => HandleDomGetPropertyBatch(request, argsJson),
                "dom_setPropertyBatch" => HandleDomSetPropertyBatch(request, argsJson),
                "dom_setTitle" => HandleDomSetTitle(request, argsJson),
                "dom_getUrl" => HandleDomGetUrl(request, argsJson),
                "dom_getReadyState" => JsonResult(request, "complete"),
                "dom_getDocumentElement" => HandleDomGetDocumentElement(request, argsJson),
                "dom_getBody" => HandleDomGetBody(request, argsJson),
                "dom_getHead" => HandleDomGetHead(request, argsJson),
                "dom_getActiveElement" => HandleDomGetActiveElement(request, argsJson),
                "dom_write" => HandleDomWrite(request, argsJson),
                "dom_getForms" => HandleDomGetForms(request, argsJson),
                "dom_getImages" => HandleDomGetImages(request, argsJson),
                "dom_getLinks" => HandleDomGetLinks(request, argsJson),
                "dom_getScripts" => HandleDomGetScripts(request, argsJson),
                "dom_getAnchors" => HandleDomGetAnchors(request, argsJson),
                "dom_setWindowLocation" => HandleDomSetWindowLocation(request, argsJson),
                "dom_getWindowInnerWidth" => JsonResult(request, (OnGetInnerWidth?.Invoke() ?? 1024).ToString()),
                "dom_getWindowInnerHeight" => JsonResult(request, (OnGetInnerHeight?.Invoke() ?? 768).ToString()),
                "dom_localStorage_get" => HandleDomLocalStorageGet(request, argsJson),
                "dom_localStorage_set" => HandleDomLocalStorageSet(request, argsJson),
                "dom_localStorage_remove" => HandleDomLocalStorageRemove(request, argsJson),
                "dom_localStorage_clear" => HandleDomLocalStorageClear(request, argsJson),
                "dom_sessionStorage_get" => HandleDomSessionStorageGet(request, argsJson),
                "dom_sessionStorage_set" => HandleDomSessionStorageSet(request, argsJson),
                "dom_sessionStorage_remove" => HandleDomSessionStorageRemove(request, argsJson),
                "dom_sessionStorage_clear" => HandleDomSessionStorageClear(request, argsJson),
                "dom_history_pushState" => HandleDomHistoryPushState(request, argsJson),
                "dom_history_replaceState" => HandleDomHistoryReplaceState(request, argsJson),
                "dom_history_back" => HandleDomHistoryBack(request),
                "dom_history_forward" => HandleDomHistoryForward(request),
                "dom_history_go" => HandleDomHistoryGo(request, argsJson),
                "dom_history_getState" => HandleDomHistoryGetState(request),
                "dom_history_getLength" => HandleDomHistoryGetLength(request),
                "dom_getFirstChild" => HandleDomGetFirstChild(request, argsJson),
                "dom_getLastChild" => HandleDomGetLastChild(request, argsJson),
                "engineGetStatus" => HandleEngineGetStatus(request),
                "engineDownload" => HandleEngineDownload(request, argsJson),
                "engineBrowse" => HandleEngineBrowse(request, argsJson),
                "engineApply" => HandleEngineApply(request, argsJson),
                _ => new IpcResponse { RequestId = request.RequestId, Success = false, Error = $"Unknown: {method}" }
        };
    }

    private IpcResponse RespondOk(IpcRequest request) =>
        new() { RequestId = request.RequestId, Success = true };

    private IpcResponse JsonResult(IpcRequest request, string value) =>
        new() { RequestId = request.RequestId, Success = true, Result = value };

    private static string[]? DeserializeArgs(string argsJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
        var root = doc.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        var arr = new string[root.GetArrayLength()];
        int i = 0;
        foreach (var el in root.EnumerateArray())
        {
            arr[i++] = el.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => el.GetString() ?? "",
                System.Text.Json.JsonValueKind.Number => el.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                System.Text.Json.JsonValueKind.Null => "",
                _ => el.GetRawText()
            };
        }
        return arr;
    }

    private static string? GetStringArg(string argsJson, int index)
    {
        try
        {
            var arr = DeserializeArgs(argsJson);
            return arr != null && index < arr.Length ? arr[index] : null;
        }
        catch { return null; }
    }

    private static string DecodeBase64(string s)
    {
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch { return s; }
    }

    private static string EncodeBase64(string s)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s ?? ""));
    }

    private IpcResponse HandleConsoleLog(string method, string argsJson)
    {
        Console.WriteLine($"[{method}] {argsJson}");
        OnConsoleLog?.Invoke(method, argsJson);
        return RespondOk(new IpcRequest { RequestId = 0 });
    }

    private IpcResponse HandleAction(IpcRequest request, string argsJson, Action<int[]> action)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize(argsJson, AcruxJsonContext.Default.Int32Array);
            if (args != null) action(args);
            return RespondOk(request);
        }
        catch { return RespondOk(request); }
    }

    private IpcResponse HandleAlert(IpcRequest request, string argsJson)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize(argsJson, AcruxJsonContext.Default.StringArray);
            if (args != null && args.Length > 0) OnAlert?.Invoke(args[0] ?? "");
            return RespondOk(request);
        }
        catch { return RespondOk(request); }
    }

    private IpcResponse HandleSetTimeout(IpcRequest request, string argsJson)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize(argsJson, AcruxJsonContext.Default.Int32Array);
            if (args == null || args.Length < 2)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid args" };

            var callbackId = args[0];
            var delayMs = Math.Max(1, args[1]);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs);
                    InvokeCallback(callbackId);
                }
                catch { }
            });

            return RespondOk(request);
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleSetInterval(IpcRequest request, string argsJson)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize(argsJson, AcruxJsonContext.Default.Int32Array);
            if (args == null || args.Length < 2)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid args" };

            var callbackId = args[0];
            var intervalMs = Math.Max(16, args[1]);

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_disposed)
                    {
                        await Task.Delay(intervalMs);
                        InvokeCallback(callbackId);
                    }
                }
                catch { }
            });

            return RespondOk(request);
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleFetch(IpcRequest request, string argsJson)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize(argsJson, AcruxJsonContext.Default.ObjectArray);
            if (args == null || args.Length < 4)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid args" };

            var url = args[0]?.ToString() ?? "";
            var optsJson = args[1]?.ToString() ?? "{}";
            var resolveId = Convert.ToInt32(args[2]);
            var rejectId = Convert.ToInt32(args[3]);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    var options = string.IsNullOrEmpty(optsJson) ? null :
                        System.Text.Json.JsonSerializer.Deserialize(optsJson, AcruxJsonContext.Default.FetchOptions);
                    var method = options?.Method ?? "GET";
                    var req = new HttpRequestMessage(new HttpMethod(method), url);

                    if (options?.Body != null && (method == "POST" || method == "PUT" || method == "PATCH"))
                        req.Content = new StringContent(options.Body, System.Text.Encoding.UTF8, "application/json");

                    var response = await http.SendAsync(req);
                    var text = await response.Content.ReadAsStringAsync();

                    var result = new FetchResult
                    {
                        Success = true,
                        Data = text,
                        Status = (int)response.StatusCode,
                        StatusText = response.ReasonPhrase ?? ""
                    };

                    var resultJson = System.Text.Json.JsonSerializer.Serialize(result, AcruxJsonContext.Default.FetchResult);
                    InvokeCallbackWith(resolveId, resultJson);
                }
                catch (Exception ex)
                {
                    var error = new FetchResult { Success = false, Error = ex.Message };
                    var errorJson = System.Text.Json.JsonSerializer.Serialize(error, AcruxJsonContext.Default.FetchResult);
                    InvokeCallbackWith(rejectId, errorJson);
                }
            });

            return RespondOk(request);
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleDomGetPropertyBatch(IpcRequest request, string argsJson)
    {
        try
        {
            var parsed = DeserializeArgs(argsJson);
            if (parsed == null || parsed.Length < 2)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid args" };

            var obj = GetDomObject(argsJson);
            var doc = obj as DocumentHost;
            var el = obj as ElementHost;
            if (doc == null && el == null)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Element not found" };

            var propsJson = parsed[1];
            var propNames = System.Text.Json.JsonSerializer.Deserialize(propsJson, AcruxJsonContext.Default.StringArray);
            if (propNames == null || propNames.Length == 0)
                return JsonResult(request, "{}");

            var results = new Dictionary<string, string?>();
            foreach (var p in propNames)
            {
                string? r = doc != null
                    ? p switch
                    {
                        "title" => doc.title, "scrollWidth" => doc.scrollWidth.ToString(),
                        "scrollHeight" => doc.scrollHeight.ToString(), "scrollTop" => doc.scrollTop.ToString(),
                        "scrollLeft" => doc.scrollLeft.ToString(), "URL" => doc.URL,
                        _ => "null"
                    }
                    : el != null
                    ? p switch
                    {
                        "tagName" => el.tagName, "nodeName" => el.nodeName, "localName" => el.localName,
                        "nodeType" => el.nodeType.ToString(), "className" => el.className, "id" => el.id,
                        "textContent" => el.textContent, "innerHTML" => el.innerHTML, "outerHTML" => el.outerHTML,
                        "value" => el.value, "hidden" => el.hidden ? "true" : "false",
                        "draggable" => el.draggable ? "true" : "false", "disabled" => el.disabled ? "true" : "false",
                        "readOnly" => el.readOnly ? "true" : "false", "required" => el.required ? "true" : "false",
                        "checked" => el.@checked ? "true" : "false", "isConnected" => el.isConnected ? "true" : "false",
                        "offsetWidth" => el.offsetWidth.ToString(), "offsetHeight" => el.offsetHeight.ToString(),
                        "clientWidth" => el.clientWidth.ToString(), "clientHeight" => el.clientHeight.ToString(),
                        "scrollTop" => el.scrollTop.ToString(), "scrollLeft" => el.scrollLeft.ToString(),
                        "offsetTop" => el.offsetTop.ToString(), "offsetLeft" => el.offsetLeft.ToString(),
                        "type" => el.type, "placeholder" => el.placeholder, "href" => el.href,
                        "rel" => el.rel, "target" => el.target, "src" => el.src, "lang" => el.lang,
                        "dir" => el.dir, "title" => el.title, "tabIndex" => el.tabIndex.ToString(),
                        "nodeValue" => el.nodeValue, "hasAttributes" => el.hasAttributes() ? "true" : "false",
                        "childElementCount" => el.childElementCount.ToString(),
                        _ => "null"
                    }
                    : "null";
                results[p] = r;
            }

            return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(results, AcruxJsonContext.Default.DictionaryStringString));
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetPropertyBatch(IpcRequest request, string argsJson)
    {
        try
        {
            var parsed = DeserializeArgs(argsJson);
            if (parsed == null || parsed.Length < 2)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid args" };

            var obj = GetDomObject(argsJson);
            var doc = obj as DocumentHost;
            var el = obj as ElementHost;
            if (doc == null && el == null)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Element not found" };

            var pairsJson = parsed[1];
            var pairs = System.Text.Json.JsonSerializer.Deserialize(pairsJson, AcruxJsonContext.Default.DictionaryStringString);
            if (pairs == null || pairs.Count == 0)
                return RespondOk(request);

            foreach (var (p, v) in pairs)
            {
                if (doc != null)
                {
                    if (p == "title") doc.title = v ?? "";
                }
                else if (el != null)
                {
                    SetElProp(el, p, v ?? "");
                }
            }

            // Single batched MarkDirty instead of per-property
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    #region DOM Helpers

    private static int GetIntArg(string argsJson, int idx)
    {
        try
        {
            var arr = DeserializeArgs(argsJson);
            return arr != null && idx < arr.Length ? int.Parse(arr[idx]) : 0;
        }
        catch { return 0; }
    }

    private object? GetDomObject(string argsJson)
    {
        var id = GetIntArg(argsJson, 0);
        if (id == -1) return DomStore?.Document;
        return DomStore?.GetElement(id);
    }

    private IpcResponse OkElement(IpcRequest request, ElementHost? el)
    {
        if (el == null) return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null", NewElementId = 0 };
        var id = DomStore?.RegisterElement(el) ?? 0;
        return new IpcResponse { RequestId = request.RequestId, Success = true, Result = id.ToString(), NewElementId = id };
    }

    private static IpcResponse OkBool(IpcRequest request, bool v) =>
        new() { RequestId = request.RequestId, Success = true, Result = v ? "true" : "false" };

    private void MarkDirty()
    {
        if (DomStore?.Document?.Engine != null)
            DomStore.Document.Engine?.MarkDirty();
    }

    #endregion

    #region DOM Element Property Handlers

    private IpcResponse HandleDomGetProperty(IpcRequest request, string argsJson)
    {
        try
        {
            var obj = GetDomObject(argsJson);
            var doc = obj as DocumentHost;
            var el = obj as ElementHost;
            if (doc == null && el == null) return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Element not found" };
            var p = GetStringArg(argsJson, 1) ?? "";
            string? r = doc != null
                ? p switch
                {
                    "title" => doc.title, "scrollWidth" => doc.scrollWidth.ToString(),
                    "scrollHeight" => doc.scrollHeight.ToString(), "scrollTop" => "0",
                    "scrollLeft" => "0", "URL" => doc.URL,
                    "href" => doc.URL, "documentURI" => doc.documentURI,
                    "baseURI" => doc.baseURI ?? "",
                    _ => "null"
                }
                : p switch
                {
                    "tagName" => el!.tagName, "nodeName" => el.nodeName, "localName" => el.localName,
                    "nodeType" => el.nodeType.ToString(), "className" => el.className, "id" => el.id,
                    "textContent" => el.textContent, "innerHTML" => el.innerHTML, "outerHTML" => el.outerHTML,
                    "value" => el.value, "hidden" => el.hidden ? "true" : "false",
                    "draggable" => el.draggable ? "true" : "false", "disabled" => el.disabled ? "true" : "false",
                    "readOnly" => el.readOnly ? "true" : "false", "required" => el.required ? "true" : "false",
                    "checked" => el.@checked ? "true" : "false", "isConnected" => el.isConnected ? "true" : "false",
                    "offsetWidth" => el.offsetWidth.ToString(), "offsetHeight" => el.offsetHeight.ToString(),
                    "clientWidth" => el.clientWidth.ToString(), "clientHeight" => el.clientHeight.ToString(),
                    "scrollTop" => el.scrollTop.ToString(), "scrollLeft" => el.scrollLeft.ToString(),
                    "offsetTop" => el.offsetTop.ToString(), "offsetLeft" => el.offsetLeft.ToString(),
                    "type" => el.type, "placeholder" => el.placeholder, "href" => el.href,
                    "rel" => el.rel, "target" => el.target, "src" => el.src, "lang" => el.lang,
                    "dir" => el.dir, "title" => el.title, "tabIndex" => el.tabIndex.ToString(),
                    "nodeValue" => el.nodeValue, "hasAttributes" => el.hasAttributes() ? "true" : "false",
                "childElementCount" => el.childElementCount.ToString(),
                _ => null
            };
            return r != null ? JsonResult(request, r) : new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetPropertyValue(IpcRequest request, string argsJson)
    {
        return HandleDomGetProperty(request, argsJson);
    }

    private IpcResponse HandleDomSetProperty(IpcRequest request, string argsJson)
    {
        try
        {
            var obj = GetDomObject(argsJson);
            var el = obj as ElementHost;
            var p = GetStringArg(argsJson, 1) ?? "";
            var v = GetStringArg(argsJson, 2) ?? "";
            if (el != null)
            {
                SetElProp(el, p, v);
                MarkDirty();
                return RespondOk(request);
            }
            var doc = obj as DocumentHost;
            if (doc != null)
            {
                switch (p)
                {
                    case "body":
                        var bodyId = GetIntArg(argsJson, 2);
                        if (bodyId >= 0)
                        {
                            var bodyEl = DomStore?.GetElement(bodyId);
                            if (bodyEl != null)
                                doc.body = bodyEl;
                        }
                        break;
                    case "title":
                        doc.title = v;
                        break;
                }
                MarkDirty();
                return RespondOk(request);
            }
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Object not found" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetStyleProperty(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el == null) return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Element not found" };
            var name = ToKebabCase(GetStringArg(argsJson, 1) ?? "");
            var v = GetStringArg(argsJson, 2);
            if (string.IsNullOrEmpty(v))
                el.style.removeProperty(name);
            else
                el.style.setProperty(name, v);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetStyleProperty(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el == null) return JsonResult(request, "");
            var name = ToKebabCase(GetStringArg(argsJson, 1) ?? "");
            return JsonResult(request, el.style.getPropertyValue(name) ?? "");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetCssText(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el == null) return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Element not found" };
            el.style.cssText = GetStringArg(argsJson, 1) ?? "";
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetCssText(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el == null) return JsonResult(request, "");
            return JsonResult(request, el.style.cssText ?? "");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    /// <summary>
    /// JS style objects use camelCase CSS names (<c>backgroundColor</c>) while the
    /// inline-style dictionary is kebab-case (<c>background-color</c>); names that
    /// are already kebab-case pass through untouched.
    /// </summary>
    private static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('-')) return name;
        var sb = new System.Text.StringBuilder(name.Length + 4);
        foreach (var ch in name)
        {
            if (char.IsUpper(ch)) { sb.Append('-'); sb.Append(char.ToLowerInvariant(ch)); }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private static void SetElProp(ElementHost el, string p, string v)
    {
        switch (p)
        {
            case "className": el.className = v; break;
            case "id": el.id = v; break;
            case "textContent": el.textContent = v; break;
            case "innerHTML": el.innerHTML = v; break;
            case "value": el.value = v; break;
            case "scrollTop": el.scrollTop = double.Parse(v ?? "0"); break;
            case "scrollLeft": el.scrollLeft = double.Parse(v ?? "0"); break;
            case "hidden": el.hidden = v == "true"; break;
            case "draggable": el.draggable = v == "true"; break;
            case "disabled": el.disabled = v == "true"; break;
            case "readOnly": el.readOnly = v == "true"; break;
            case "required": el.required = v == "true"; break;
            case "checked": el.@checked = v == "true"; break;
            case "type": el.type = v; break;
            case "placeholder": el.placeholder = v; break;
            case "href": el.href = v; break;
            case "rel": el.rel = v; break;
            case "target": el.target = v; break;
            case "src": el.src = v; break;
            case "lang": el.lang = v; break;
            case "dir": el.dir = v; break;
            case "title": el.title = v; break;
            case "tabIndex": el.tabIndex = int.Parse(v ?? "0"); break;
        }
    }

    #endregion

    #region DOM Element Creation & Manipulation

    private IpcResponse HandleDomCreateElementBatch(IpcRequest request, string argsJson)
    {
        try
        {
            var tags = System.Text.Json.JsonSerializer.Deserialize(argsJson, AcruxJsonContext.Default.StringArray);
            var doc = DomStore?.Document;
            if (doc == null || tags == null)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "No document" };

            var ids = new List<string>();
            var elements = new ElementHost[tags.Length];
            for (int i = 0; i < tags.Length; i++)
            {
                var el = doc.createElement(tags[i] ?? "div");
                elements[i] = el;
                ids.Add((DomStore?.RegisterElement(el) ?? 0).ToString());
            }

            // Single batched MarkDirty for all creations
            MarkDirty();
            return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids.ToArray(), AcruxJsonContext.Default.StringArray));
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomCreateElement(IpcRequest request, string argsJson)
    {
        try
        {
            var tag = GetStringArg(argsJson, 0) ?? "div";
            var doc = DomStore?.Document;
            if (doc != null)
            {
                var el = doc.createElement(tag);
                return OkElement(request, el);
            }
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "No document" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomAppendChild(IpcRequest request, string argsJson)
    {
        try
        {
            var parent = GetDomObject(argsJson) as ElementHost;
            var childId = GetIntArg(argsJson, 1);
            var child = DomStore?.GetElement(childId);
            if (parent != null && child != null) parent.appendChild(child);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomInsertBefore(IpcRequest request, string argsJson)
    {
        try
        {
            var parent = GetDomObject(argsJson) as ElementHost;
            var childId = GetIntArg(argsJson, 1);
            var refId = GetIntArg(argsJson, 2);
            var child = DomStore?.GetElement(childId);
            var refEl = refId > 0 ? DomStore?.GetElement(refId) : null;
            if (parent != null && child != null) parent.insertBefore(child, refEl);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomRemoveChild(IpcRequest request, string argsJson)
    {
        try
        {
            var parent = GetDomObject(argsJson) as ElementHost;
            var childId = GetIntArg(argsJson, 1);
            var child = DomStore?.GetElement(childId);
            if (parent != null && child != null) parent.removeChild(child);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomRemove(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.remove();
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomInsertAdjacentHTML(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.insertAdjacentHTML(GetStringArg(argsJson, 1) ?? "beforeend", GetStringArg(argsJson, 2) ?? "");
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomReplaceWith(IpcRequest request, string argsJson)
    {
        try
        {
            var oldEl = DomStore?.GetElement(GetIntArg(argsJson, 0));
            var newEl = DomStore?.GetElement(GetIntArg(argsJson, 1));
            if (oldEl != null && newEl != null) oldEl.replaceWith(newEl);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomBefore(IpcRequest request, string argsJson)
    {
        try
        {
            var el = DomStore?.GetElement(GetIntArg(argsJson, 0));
            var newEl = DomStore?.GetElement(GetIntArg(argsJson, 1));
            if (el != null && newEl != null) el.before(newEl);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomAfter(IpcRequest request, string argsJson)
    {
        try
        {
            var el = DomStore?.GetElement(GetIntArg(argsJson, 0));
            var newEl = DomStore?.GetElement(GetIntArg(argsJson, 1));
            if (el != null && newEl != null) el.after(newEl);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomCloneNode(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null)
            {
                var clone = el.cloneNode(GetStringArg(argsJson, 1) == "true");
                var cloneEl = clone as ElementHost;
                if (cloneEl != null) DomStore?.RegisterElement(cloneEl);
            }
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    #endregion

    #region DOM Attributes

    private IpcResponse HandleDomSetAttribute(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.setAttribute(GetStringArg(argsJson, 1) ?? "", GetStringArg(argsJson, 2) ?? "");
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetAttribute(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null)
            {
                var v = el.getAttribute(GetStringArg(argsJson, 1) ?? "");
                return v != null ? JsonResult(request, v) : new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
            }
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Element not found" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomRemoveAttribute(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.removeAttribute(GetStringArg(argsJson, 1) ?? "");
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomToggleAttribute(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.toggleAttribute(GetStringArg(argsJson, 1) ?? "");
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomHasAttribute(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkBool(request, el.hasAttribute(GetStringArg(argsJson, 1) ?? ""));
            return OkBool(request, false);
        }
        catch { return OkBool(request, false); }
    }

    #endregion

    #region DOM Element Setters

    private IpcResponse HandleDomSetClassName(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.className = GetStringArg(argsJson, 1) ?? "";
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetId(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.id = GetStringArg(argsJson, 1) ?? "";
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetValue(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.value = GetStringArg(argsJson, 1);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetTextContent(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.textContent = GetStringArg(argsJson, 1);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetInnerHTML(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.innerHTML = GetStringArg(argsJson, 1);
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetScrollTop(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.scrollTop = double.Parse(GetStringArg(argsJson, 1) ?? "0");
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSetScrollLeft(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.scrollLeft = double.Parse(GetStringArg(argsJson, 1) ?? "0");
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    #endregion

    #region DOM Element Layout

    private IpcResponse HandleDomGetBoundingClientRect(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null)
            {
                var rect = el.getBoundingClientRect();
                string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                return JsonResult(request, $"{{\"x\":{F(rect.x)},\"y\":{F(rect.y)},\"width\":{F(rect.width)},\"height\":{F(rect.height)},\"top\":{F(rect.top)},\"right\":{F(rect.right)},\"bottom\":{F(rect.bottom)},\"left\":{F(rect.left)}}}");
            }
            return JsonResult(request, "{\"x\":0,\"y\":0,\"width\":0,\"height\":0,\"top\":0,\"right\":0,\"bottom\":0,\"left\":0}");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetComputedStyle(IpcRequest request, string argsJson)
    {
        try
        {
            return JsonResult(request, "{}");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    #endregion

    #region DOM Element Traversal

    private IpcResponse HandleDomGetChildren(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null)
            {
                var ids = DomStore?.RegisterElements(el.GetChildHosts()).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetChildNodes(IpcRequest request, string argsJson)
    {
        return HandleDomGetChildren(request, argsJson);
    }

    private IpcResponse HandleDomGetParent(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkElement(request, el.parentElement);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomNextSibling(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkElement(request, el.nextElementSibling as ElementHost);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomPreviousSibling(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkElement(request, el.previousElementSibling as ElementHost);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetFirstChild(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkElement(request, el.firstChild as ElementHost);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetLastChild(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkElement(request, el.lastChild as ElementHost);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetFirstElementChild(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkElement(request, el.firstElementChild);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetLastElementChild(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkElement(request, el.lastElementChild);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetChildElementCount(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return JsonResult(request, el.childElementCount.ToString());
            return JsonResult(request, "0");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    #endregion

    #region DOM Element Selection

    private IpcResponse HandleDomQuerySelector(IpcRequest request, string argsJson)
    {
        try
        {
            var obj = GetDomObject(argsJson);
            var selector = GetStringArg(argsJson, 1) ?? "";
            if (obj is ElementHost el)
                return OkElement(request, el.querySelector(selector) as ElementHost);
            if (obj is DocumentHost doc)
                return OkElement(request, doc.querySelector(selector) as ElementHost);
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Element not found" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomQuerySelectorAll(IpcRequest request, string argsJson)
    {
        try
        {
            var obj = GetDomObject(argsJson);
            var selector = GetStringArg(argsJson, 1) ?? "";
            List<ElementHost>? hosts = null;
            if (obj is ElementHost el)
                hosts = el.querySelectorAll(selector).OfType<ElementHost>().ToList();
            else if (obj is DocumentHost doc)
                hosts = ((System.Collections.IEnumerable)doc.querySelectorAll(selector)).OfType<ElementHost>().ToList();
            if (hosts != null)
            {
                var ids = DomStore?.RegisterElements(hosts).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetElementById(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null)
            {
                var result = doc.getElementById(GetStringArg(argsJson, 0) ?? "");
                return OkElement(request, result);
            }
            return JsonResult(request, "null");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetElementsByTagName(IpcRequest request, string argsJson)
    {
        try
        {
            var obj = GetDomObject(argsJson);
            System.Collections.IEnumerable? coll = null;
            var name = GetStringArg(argsJson, 1) ?? "";
            if (obj is ElementHost el) coll = el.getElementsByTagName(name);
            else if (obj is DocumentHost doc) coll = doc.getElementsByTagName(name) as System.Collections.IEnumerable;
            if (coll != null)
            {
                var hosts = coll.OfType<ElementHost>().ToList();
                var ids = DomStore?.RegisterElements(hosts).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetElementsByClassName(IpcRequest request, string argsJson)
    {
        try
        {
            var obj = GetDomObject(argsJson);
            System.Collections.IEnumerable? coll = null;
            var name = GetStringArg(argsJson, 1) ?? "";
            if (obj is ElementHost el) coll = el.getElementsByClassName(name);
            else if (obj is DocumentHost doc) coll = doc.getElementsByClassName(name) as System.Collections.IEnumerable;
            if (coll != null)
            {
                var hosts = coll.OfType<ElementHost>().ToList();
                var ids = DomStore?.RegisterElements(hosts).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetElementsByName(IpcRequest request, string argsJson)
    {
        return JsonResult(request, "[]");
    }

    private IpcResponse HandleDomMatches(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) return OkBool(request, el.matches(GetStringArg(argsJson, 1) ?? ""));
            return OkBool(request, false);
        }
        catch { return OkBool(request, false); }
    }

    private IpcResponse HandleDomClosest(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null)
            {
                var result = el.closest(GetStringArg(argsJson, 1) ?? "");
                return OkElement(request, result as ElementHost);
            }
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomContains(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null)
            {
                var other = DomStore?.GetElement(GetIntArg(argsJson, 1));
                return OkBool(request, other != null && el.contains(other));
            }
            return OkBool(request, false);
        }
        catch { return OkBool(request, false); }
    }

    #endregion

    #region DOM Events

    private IpcResponse HandleDomClick(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.click();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomFocus(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.focus();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomBlur(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.blur();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomScrollIntoView(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.scrollIntoView(GetStringArg(argsJson, 1) == "true");
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomDispatchEvent(IpcRequest request, string argsJson)
    {
        try
        {
            var obj = GetDomObject(argsJson);
            var el = obj as ElementHost;
            var doc = obj as DocumentHost;
            var evt = new ScriptEvent(GetStringArg(argsJson, 1) ?? "", el);
            if (el != null)
            {
                el.dispatchEvent(evt);
            }
            else if (doc != null)
            {
                doc.dispatchEvent(evt);
            }
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomAddEventListener(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomRemoveEventListener(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    #endregion

    #region DOM Document Methods

    private IpcResponse HandleDomSetTitle(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null) doc.title = GetStringArg(argsJson, 0) ?? "";
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetUrl(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            return doc != null ? JsonResult(request, doc.URL) : JsonResult(request, "");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetDocumentElement(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null) return OkElement(request, doc.documentElement);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetBody(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null) return OkElement(request, doc.body);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetHead(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null) return OkElement(request, doc.head);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetActiveElement(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null) return OkElement(request, doc.activeElement);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = "null" };
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomWrite(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null) doc.write(GetStringArg(argsJson, 0) ?? "");
            MarkDirty();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetForms(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null)
            {
                var hosts = doc.forms.OfType<ElementHost>().ToList();
                var ids = DomStore?.RegisterElements(hosts).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetImages(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null)
            {
                var hosts = doc.images.OfType<ElementHost>().ToList();
                var ids = DomStore?.RegisterElements(hosts).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetLinks(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null)
            {
                var hosts = doc.links.OfType<ElementHost>().ToList();
                var ids = DomStore?.RegisterElements(hosts).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetScripts(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null)
            {
                var hosts = doc.scripts.OfType<ElementHost>().ToList();
                var ids = DomStore?.RegisterElements(hosts).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetAnchors(IpcRequest request, string argsJson)
    {
        try
        {
            var doc = DomStore?.Document;
            if (doc != null)
            {
                var hosts = doc.anchors.OfType<ElementHost>().ToList();
                var ids = DomStore?.RegisterElements(hosts).Select(i => i.ToString()).ToArray() ?? Array.Empty<string>();
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(ids, AcruxJsonContext.Default.StringArray));
            }
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    #endregion

    #region DOM Element Generic Getters

    private IpcResponse HandleDomGetTag(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.tagName) : JsonResult(request, ""); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetClassName(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.className) : JsonResult(request, ""); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetId(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.id) : JsonResult(request, ""); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetValue(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.value ?? "") : JsonResult(request, "null"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetTextContent(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.textContent ?? "") : JsonResult(request, "null"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetInnerHTML(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.innerHTML ?? "") : JsonResult(request, "null"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetNodeType(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.nodeType.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetOffsetWidth(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.offsetWidth.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetOffsetHeight(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.offsetHeight.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetClientWidth(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.clientWidth.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetClientHeight(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.clientHeight.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetScrollTop(IpcRequest request, string argsJson)
    {
        try { var obj = GetDomObject(argsJson); var doc = obj as DocumentHost; var el = obj as ElementHost; return doc != null ? JsonResult(request, doc.scrollTop.ToString()) : el != null ? JsonResult(request, el.scrollTop.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetScrollLeft(IpcRequest request, string argsJson)
    {
        try { var obj = GetDomObject(argsJson); var doc = obj as DocumentHost; var el = obj as ElementHost; return doc != null ? JsonResult(request, doc.scrollLeft.ToString()) : el != null ? JsonResult(request, el.scrollLeft.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetScrollWidth(IpcRequest request, string argsJson)
    {
        try { var obj = GetDomObject(argsJson); var doc = obj as DocumentHost; var el = obj as ElementHost; return doc != null ? JsonResult(request, doc.scrollWidth.ToString()) : el != null ? JsonResult(request, el.scrollWidth.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetScrollHeight(IpcRequest request, string argsJson)
    {
        try { var obj = GetDomObject(argsJson); var doc = obj as DocumentHost; var el = obj as ElementHost; return doc != null ? JsonResult(request, doc.scrollHeight.ToString()) : el != null ? JsonResult(request, el.scrollHeight.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetOffsetTop(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.offsetTop.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetOffsetLeft(IpcRequest request, string argsJson)
    {
        try { var el = GetDomObject(argsJson) as ElementHost; return el != null ? JsonResult(request, el.offsetLeft.ToString()) : JsonResult(request, "0"); }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomGetClassList(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null)
                return JsonResult(request, System.Text.Json.JsonSerializer.Serialize(el.classListValues, AcruxJsonContext.Default.ObjectArray));
            return JsonResult(request, "[]");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomNormalize(IpcRequest request, string argsJson)
    {
        try
        {
            var el = GetDomObject(argsJson) as ElementHost;
            if (el != null) el.normalize();
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    #endregion

    #region DOM Storage/History/Location

    private IpcResponse HandleDomSetWindowLocation(IpcRequest request, string argsJson)
    {
        try
        {
            var url = GetStringArg(argsJson, 0) ?? "";
            var doc = DomStore?.Document;
            if (doc != null)
            {
                doc.NativeDocument.Url = url;
            }
            return RespondOk(request);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomLocalStorageGet(IpcRequest request, string argsJson)
    {
        try
        {
            var key = GetStringArg(argsJson, 0) ?? "";
            var store = DomStore?.Document?.Engine;
            return JsonResult(request, "");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomLocalStorageSet(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomLocalStorageRemove(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomLocalStorageClear(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomSessionStorageGet(IpcRequest request, string argsJson)
    {
        try
        {
            return JsonResult(request, "");
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleDomSessionStorageSet(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomSessionStorageRemove(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomSessionStorageClear(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomHistoryPushState(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomHistoryReplaceState(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomHistoryBack(IpcRequest request)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomHistoryForward(IpcRequest request)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomHistoryGo(IpcRequest request, string argsJson)
    {
        return RespondOk(request);
    }

    private IpcResponse HandleDomHistoryGetState(IpcRequest request)
    {
        return JsonResult(request, "null");
    }

    private IpcResponse HandleDomHistoryGetLength(IpcRequest request)
    {
        return JsonResult(request, "1");
    }

    #endregion

    #region Engine Management

    private IpcResponse HandleEngineGetStatus(IpcRequest request)
    {
        try
        {
            return JsonResult(request, new AcruxBuiltins(null!).engineGetStatus());
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleEngineDownload(IpcRequest request, string argsJson)
    {
        try
        {
            var name = GetStringArg(argsJson, 0) ?? "";
            var result = OnEngineAction?.Invoke("download", name)
                ?? AcruxBuiltins.GlobalEngineAction?.Invoke("download", name)
                ?? "{\"success\":false,\"error\":\"no handler\"}";
            return JsonResult(request, result);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleEngineBrowse(IpcRequest request, string argsJson)
    {
        try
        {
            var name = GetStringArg(argsJson, 0) ?? "";
            var result = OnEngineAction?.Invoke("browse", name)
                ?? AcruxBuiltins.GlobalEngineAction?.Invoke("browse", name)
                ?? "{\"success\":false,\"error\":\"no handler\"}";
            return JsonResult(request, result);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    private IpcResponse HandleEngineApply(IpcRequest request, string argsJson)
    {
        try
        {
            var name = GetStringArg(argsJson, 0) ?? "";
            var result = OnEngineAction?.Invoke("apply", name)
                ?? AcruxBuiltins.GlobalEngineAction?.Invoke("apply", name)
                ?? "{\"success\":false,\"error\":\"no handler\"}";
            return JsonResult(request, result);
        }
        catch (Exception ex) { return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message }; }
    }

    #endregion

    private class FetchOptions
    {
        public string? Method { get; set; }
        public string? Body { get; set; }
    }

    private class FetchResult
    {
        public bool Success { get; set; }
        public string? Data { get; set; }
        public int Status { get; set; }
        public string? StatusText { get; set; }
        public string? Error { get; set; }
    }

    public void Execute(string code)
    {
        if (_disposed || _transport == null || string.IsNullOrEmpty(code)) return;
        try
        {
            var request = new IpcRequest
            {
                RequestId = Interlocked.Increment(ref _nextRequestId),
                Type = RequestType.Execute,
                Payload = code,
            };
            SendRequest(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteJsEngine] Execute failed (blocking): {ex.Message}");
        }
    }

    public object? Evaluate(string expression)
    {
        try
        {
            var request = new IpcRequest
            {
                RequestId = Interlocked.Increment(ref _nextRequestId),
                Type = RequestType.Evaluate,
                Payload = expression,
            };
            var response = SendRequest(request);
            if (!response.Success)
                Console.WriteLine($"[RemoteJsEngine] Evaluate failed: {response.Error}");
            return response.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteJsEngine] Evaluate exception: {ex.Message}");
            return null;
        }
    }

    public object? CallFunction(string functionName, params object?[] args)
    {
        try
        {
            var argStrings = new List<string> { functionName };
            foreach (var arg in args)
                argStrings.Add(arg?.ToString() ?? "");
            var payload = System.Text.Json.JsonSerializer.Serialize(argStrings.ToArray(), AcruxJsonContext.Default.StringArray);

            var request = new IpcRequest
            {
                RequestId = Interlocked.Increment(ref _nextRequestId),
                Type = RequestType.CallFunction,
                Payload = payload,
            };
            var response = SendRequest(request);
            if (!response.Success)
                Console.WriteLine($"[RemoteJsEngine] CallFunction failed: {response.Error}");
            return response.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteJsEngine] CallFunction exception: {ex.Message}");
            return null;
        }
    }

    public void EmbedHostObject(string name, object? value) => SetGlobal(name, value);

    public void SetGlobal(string name, object? value)
    {
        // 远程引擎的初始化脚本已经定义了所有全局对象（console, __acrux 等）
        // 不需要再发�?.NET 对象到远程引�?
        // 只有简单值（字符串、数字）才需要设�?
        if (value is string strValue)
        {
            var escaped = strValue.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n");
            Execute($"var {name} = '{escaped}';");
        }
        else if (value is int || value is long || value is float || value is double)
        {
            Execute($"var {name} = {value};");
        }
        else if (value is bool b)
        {
            Execute($"var {name} = {(b ? "true" : "false")};");
        }
        // 复杂对象（ConsoleHost, DocumentHost 等）已在远程引擎的初始化脚本中定�?
    }

    public int StoreCallback(object callback)
    {
        // 回调在远程引擎中注册，返�?ID
        return 0;
    }

    public void InvokeCallback(int id)
    {
        try
        {
            var request = new IpcRequest
            {
                RequestId = Interlocked.Increment(ref _nextRequestId),
                Type = RequestType.InvokeCallback,
                CallbackId = id,
            };
            SendRequest(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteJsEngine] InvokeCallback exception: {ex.Message}");
        }
    }

    public void InvokeCallbackWith(int id, object? arg)
    {
        try
        {
            var request = new IpcRequest
            {
                RequestId = Interlocked.Increment(ref _nextRequestId),
                Type = RequestType.InvokeCallback,
                CallbackId = id,
                Payload = arg?.ToString(),
            };
            SendRequest(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteJsEngine] InvokeCallbackWith exception: {ex.Message}");
        }
    }

    public void RemoveCallback(int id)
    {
        try
        {
            var request = new IpcRequest
            {
                RequestId = Interlocked.Increment(ref _nextRequestId),
                Type = RequestType.RemoveCallback,
                CallbackId = id,
            };
            SendRequest(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteJsEngine] RemoveCallback exception: {ex.Message}");
        }
    }

    public void ClearCallbacks()
    {
        // 远程引擎不支持批量清除，逐个清除由主进程管理
    }

    public T? GetGlobal<T>(string name) where T : class
    {
        return null;
    }

    public void SetGlobal(string name, object? value, bool rewriteExisting)
    {
        SetGlobal(name, value);
    }

    private IpcResponse SendRequest(IpcRequest request)
    {
        if (_transport == null)
            throw new InvalidOperationException("IPC transport not initialized");

        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pending[request.RequestId] = tcs;
        }

        try
        {
            var data = ProtocolSerializer.Serialize(request);
            // 同步发送，�?await、无上下文切�?
            _transport.SendFireAndForget(data);

            // 100ms 超时：快速失败，避免 UI 线程长时间等�?
            if (!tcs.Task.Wait(100))
            {
                lock (_lock) _pending.Remove(request.RequestId);
                return new IpcResponse
                {
                    RequestId = request.RequestId,
                    Success = false,
                    Error = $"IPC request {request.RequestId} timed out"
                };
            }

            return tcs.Task.Result;
        }
        catch (Exception ex)
        {
            lock (_lock) _pending.Remove(request.RequestId);
            throw;
        }
    }

    public void Dispose()
    {
        Cleanup();
    }

    // IJavaScriptEngineAdapter 必需的其他方�?
    public int CaptureFunction(string globalName)
    {
        // 远程引擎不支持直接捕获函数，返回 0
        return 0;
    }

    public void Reset()
    {
        // 远程引擎不支持重置，由主进程管理
    }

    }

/// <summary>
/// 存储远程引擎中的 DOM 代理对象（Document, Element 等）
/// </summary>
public class DomProxyStore
{
    private readonly object _lock = new();
    private int _nextElementId = 1;
    private Dictionary<int, ElementHost> _elements = new();
    private DocumentHost? _document;

    public DocumentHost? Document => _document;

    public long Version { get; private set; }

    public void SetDocument(DocumentHost doc)
    {
        lock (_lock) { _document = doc; Version++; }
    }

    public ElementHost? GetElement(int id)
    {
        lock (_lock) return _elements.TryGetValue(id, out var el) ? el : null;
    }

    public int RegisterElement(ElementHost el)
    {
        lock (_lock)
        {
            var id = _nextElementId++;
            _elements[id] = el;
            return id;
        }
    }

    public int[] RegisterElements(IEnumerable<ElementHost> els)
    {
        lock (_lock)
        {
            var ids = new List<int>();
            foreach (var el in els)
            {
                var id = _nextElementId++;
                _elements[id] = el;
                ids.Add(id);
            }
            return ids.ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _elements.Clear();
            _nextElementId = 1;
            _document = null;
            Version++;
        }
    }
}
#endif
