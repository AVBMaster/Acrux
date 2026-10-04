using System.Diagnostics;
using System.Text.Json;
using Jint;
using Jint.Native;
using Acrux.JsEngineProtocol;

namespace Acrux.JsEngineHost;

class Program
{
    static async Task Main(string[] args)
    {
        var channelName = "";
        var engineType = "Jint";
        var tabIndex = 0;
        int parentPid = 0;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--channel=")) channelName = arg[10..];
            else if (arg.StartsWith("--engine=")) engineType = arg[9..];
            else if (arg.StartsWith("--tab=")) tabIndex = int.Parse(arg[6..]);
            else if (arg.StartsWith("--parent=")) parentPid = int.Parse(arg[9..]);
        }

        if (string.IsNullOrEmpty(channelName))
        {
            Console.Error.WriteLine("JsEngineHost: --channel required");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine($"[JsEngineHost] Starting channel={channelName} engine={engineType} tab={tabIndex} parent={parentPid}");

        using var parentWatchCts = new CancellationTokenSource();

        if (parentPid > 0)
        {
            Task.Run(() =>
            {
                while (!parentWatchCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var parent = Process.GetProcessById(parentPid);
                        if (parent.HasExited)
                        {
                            parent.Dispose();
                            Console.WriteLine("[JsEngineHost] Parent process died, exiting");
                            parentWatchCts.Cancel();
                            return;
                        }
                        parent.Dispose();
                    }
                    catch
                    {
                        // 任何异常（进程不存在、句柄不可访问等）都认为父进程已消失
                        Console.WriteLine("[JsEngineHost] Parent process check failed, exiting");
                        parentWatchCts.Cancel();
                        return;
                    }
                    Thread.Sleep(200);
                }
            });
        }

        var engine = new Engine();
        var manager = new EngineManager(engine, tabIndex);

        using var transport = new MmapTransport(channelName);
        try
        {
            await transport.StartServerAsync(parentWatchCts.Token);
            Console.WriteLine("[JsEngineHost] MMAP connected");
            Console.WriteLine("[JsEngineHost] Setup completed");
            await manager.RunAsync(transport, parentWatchCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[JsEngineHost] Fatal: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            try { File.WriteAllText("JsEngineHost_error.log", $"{ex}\n{ex.StackTrace}"); } catch { }
        }
        finally { engine.Dispose(); }
        Console.WriteLine("[JsEngineHost] Exiting");
    }
}

class EngineManager
{
    private readonly Engine _engine;
    private readonly int _tabIndex;
    private MmapTransport? _transport;
    private readonly Dictionary<long, TaskCompletionSource<IpcResponse>> _pending = new();
    private readonly object _lock = new();
    private long _nextRequestId;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _engineLock = new(1, 1);

    public EngineManager(Engine engine, int tabIndex)
    {
        _engine = engine;
        _tabIndex = tabIndex;
        DefineIpcFunction();
        ExecuteSetup();
    }

    private void DefineIpcFunction()
    {
        // 注册 __ipc 函数，JS 调用时在独立线程上异步执行，避免阻塞引擎线程
        _engine.SetValue("__ipc", (Func<string, string, string>)((method, argsJson) =>
        {
            try
            {
                return Task.Run(() => SendToMainAsync(method, argsJson)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return $"{{\"error\":\"{ex.Message}\"}}";
            }
        }));
    }

    private async Task<string> SendToMainAsync(string method, string argsJson)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _pending[requestId] = tcs;
        }

        var request = new IpcRequest
        {
            RequestId = requestId,
            Type = RequestType.DomCall,
            Payload = $"{method}|{argsJson}",
        };

        try
        {
            var data = ProtocolSerializer.Serialize(request);
            await _transport!.SendAsync(data).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000, cts.Token));
            if (completed != tcs.Task)
            {
                lock (_lock) _pending.Remove(requestId);
                return "{\"error\":\"timeout\"}";
            }

            var response = await tcs.Task;
            return response.Result ?? "null";
        }
        catch (Exception ex)
        {
            lock (_lock) _pending.Remove(requestId);
            Console.WriteLine($"[JsEngineHost] SendToMain error: {ex.Message}");
            return $"{{\"error\":\"{ex.Message}\"}}";
        }
    }

    public async Task RunAsync(MmapTransport transport, CancellationToken ct = default)
    {
        _transport = transport;
        _cts = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        _ = Task.Run(() => ReceiveLoop(linked.Token));
        try { await Task.Delay(-1, linked.Token); }
        catch (OperationCanceledException) { }
        finally { _cts.Cancel(); }
    }

    public void ExecuteSetup()
    {
        var setup = @"
(function() {
    var g = typeof globalThis !== 'undefined' ? globalThis : this;
    g.__g_cbid = 0; g.__g_cbs = {};
    g.__g_store = function(fn) {
        if (typeof fn !== 'function') return -1;
        var id = ++g.__g_cbid;
        g.__g_cbs[id] = fn;
        return id;
    };
    g.__g_invoke = function(id, arg) {
        var fn = g.__g_cbs[id];
        if (fn) { return arg !== undefined ? fn(arg) : fn(); }
    };
    g.__g_remove = function(id) { delete g.__g_cbs[id]; };

    // Capture native built-ins BEFORE shadowing them, to avoid self-recursion (stack overflow).
    g.___nativeInt = parseInt;
    g.___nativeFloat = parseFloat;
    g.___nativeNaN = isNaN;
    g.___nativeFinite = isFinite;
    g.___nativeDecodeURI = decodeURI;
    g.___nativeEncodeURI = encodeURI;
    g.___nativeDecodeURIComponent = decodeURIComponent;
    g.___nativeEncodeURIComponent = encodeURIComponent;

    g.console = {
        log: function() { __ipc('console.log', JSON.stringify(Array.prototype.slice.call(arguments))); },
        error: function() { __ipc('console.error', JSON.stringify(Array.prototype.slice.call(arguments))); },
        warn: function() { __ipc('console.warn', JSON.stringify(Array.prototype.slice.call(arguments))); },
        info: function() { __ipc('console.info', JSON.stringify(Array.prototype.slice.call(arguments))); },
        debug: function() { __ipc('console.debug', JSON.stringify(Array.prototype.slice.call(arguments))); }
    };

    g.setTimeout = function(fn, ms) { return __ipc('setTimeout', JSON.stringify([__g_store(fn), ms||0])); };
    g.setInterval = function(fn, ms) { return __ipc('setInterval', JSON.stringify([__g_store(fn), ms||0])); };
    g.clearTimeout = function(id) { try { __ipc('clearTimeout', JSON.stringify([id])); } catch(e) {} };
    g.clearInterval = function(id) { try { __ipc('clearInterval', JSON.stringify([id])); } catch(e) {} };

    g.__acrux = {
        setTimeout: g.setTimeout, setInterval: g.setInterval,
        clearTimeout: g.clearTimeout, clearInterval: g.clearInterval,
        innerWidth: function() { return g.___nativeInt(__ipc('innerWidth', '[]')); },
        innerHeight: function() { return g.___nativeInt(__ipc('innerHeight', '[]')); },
        scrollTo: function(x,y) { __ipc('scrollTo', JSON.stringify([x||0,y||0])); },
        scrollBy: function(x,y) { __ipc('scrollBy', JSON.stringify([x||0,y||0])); },
        alert: function(msg) { __ipc('alert', JSON.stringify([msg||''])); },
        confirm: function(msg) { return __ipc('confirm', JSON.stringify([msg||''])) === 'true'; },
        prompt: function(msg,def) { return __ipc('prompt', JSON.stringify([msg||'',def||''])); },
        decodeURI: function(s) { return g.___nativeDecodeURI(s); },
        encodeURI: function(s) { return g.___nativeEncodeURI(s); },
        decodeURIComponent: function(s) { return g.___nativeDecodeURIComponent(s); },
        encodeURIComponent: function(s) { return g.___nativeEncodeURIComponent(s); },
        parseInt: function(s,r) { return g.___nativeInt(s, r||10); },
        parseFloat: function(s) { return g.___nativeFloat(s); },
        isNaN: function(v) { return g.___nativeNaN(v); },
        isFinite: function(v) { return g.___nativeFinite(v); },
        atob: function(s) { return __ipc('atob', JSON.stringify([s||''])); },
        btoa: function(s) { return __ipc('btoa', JSON.stringify([s||''])); },
        _fetch: function(url, opts, resolveId, rejectId) {
            __ipc('fetch', JSON.stringify([url, opts||'', resolveId, rejectId]));
        },
        createXMLHttpRequest: function() { return __ipc('createXHR', '[]'); },
        createURL: function(url,base) { return __ipc('createURL', JSON.stringify([url||'',base||''])); },
        createURLSearchParams: function(q) { return __ipc('createURLSearchParams', JSON.stringify([q||''])); },
        engineGetStatus: function() { return __ipc('engineGetStatus', '[]'); },
        engineDownload: function(name) { return __ipc('engineDownload', JSON.stringify([name||''])); },
        engineBrowse: function(name) { return __ipc('engineBrowse', JSON.stringify([name||''])); },
        engineApply: function(name) { return __ipc('engineApply', JSON.stringify([name||''])); }
    };

    g.window = g;
    g.alert = function(m) { __ipc('alert', JSON.stringify([m||''])); };
    g.confirm = function(m) { return __ipc('confirm', JSON.stringify([m||''])) === 'true'; };
    g.prompt = function(m,d) { return __ipc('prompt', JSON.stringify([m||'',d||''])); };
    g.fetch = function(url, opts) {
        return new Promise(function(resolve, reject) {
            var rid = __g_store(resolve);
            var rjid = __g_store(reject);
            __ipc('fetch', JSON.stringify([url, JSON.stringify(opts||{}), rid, rjid]));
        });
    };
    g.requestAnimationFrame = function(fn) { return g.setTimeout(fn, 16); };
    g.cancelAnimationFrame = function(id) { g.clearTimeout(id); };
    g.decodeURI = function(s) { try { return g.___nativeDecodeURI(s); } catch(e) { return s; } };
    g.encodeURI = function(s) { try { return g.___nativeEncodeURI(s); } catch(e) { return s; } };
    g.decodeURIComponent = function(s) { try { return g.___nativeDecodeURIComponent(s); } catch(e) { return s; } };
    g.encodeURIComponent = function(s) { try { return g.___nativeEncodeURIComponent(s); } catch(e) { return s; } };
    g.parseInt = function(s,r) { return g.___nativeInt(s, r||10); };
    g.parseFloat = function(s) { return g.___nativeFloat(s); };
    g.isNaN = function(v) { return g.___nativeNaN(v); };
    g.isFinite = function(v) { return g.___nativeFinite(v); };
    g.atob = function(s) { return __ipc('atob', JSON.stringify([s||''])); };
    g.btoa = function(s) { return __ipc('btoa', JSON.stringify([s||''])); };
    try { g.Object.defineProperty(g, 'innerWidth', { configurable: true, get: function() { return g.___nativeInt(__ipc('innerWidth', '[]')); } }); } catch(e) {}
    try { g.Object.defineProperty(g, 'innerHeight', { configurable: true, get: function() { return g.___nativeInt(__ipc('innerHeight', '[]')); } }); } catch(e) {}
})();
";
        _engine.Execute(setup);
        // Load DOM setup from embedded resource (preferred) or external JS file (fallback)
        try
        {
            var asm = typeof(Program).Assembly;
            var resourceNames = asm.GetManifestResourceNames();
            Console.WriteLine("[JsEngineHost] Embedded resources: " + string.Join(", ", resourceNames));
            string? domSetup = null;
            // Prefer any resource that ends with DomSetup.js
            var resourceName = resourceNames.FirstOrDefault(n => n.EndsWith(".DomSetup.js", StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var rs = asm.GetManifestResourceStream(resourceName);
                if (rs != null)
                {
                    using var sr = new StreamReader(rs);
                    domSetup = sr.ReadToEnd();
                    try
                    {
                        _engine.Execute(domSetup);
                        Console.WriteLine($"[JsEngineHost] DOM setup loaded from embedded resource '{resourceName}'");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[JsEngineHost] Failed to execute embedded DOM setup: {ex.Message}");
                        domSetup = null;
                    }
                }
            }

            if (domSetup == null)
            {
                var domSetupPath = Path.Combine(AppContext.BaseDirectory, "DomSetup.js");
                if (File.Exists(domSetupPath))
                {
                    try
                    {
                        domSetup = File.ReadAllText(domSetupPath);
                        _engine.Execute(domSetup);
                        Console.WriteLine("[JsEngineHost] DOM setup loaded from file");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[JsEngineHost] Failed to load DOM setup from file: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[JsEngineHost] DomSetup.js not found (embedded resource missing and file not present)");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JsEngineHost] Failed to load DOM setup: {ex.Message}");
        }
    }

    private string SendToMain(string method, string argsJson)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _pending[requestId] = tcs;
        }

        var request = new IpcRequest
        {
            RequestId = requestId,
            Type = RequestType.DomCall,
            Payload = $"{method}|{argsJson}",
        };

        try
        {
            var data = ProtocolSerializer.Serialize(request);
            _transport!.SendAsync(data).Wait();

            // 1 秒超时：避免 JS 引擎阻塞等待主进程响应
            if (!tcs.Task.Wait(1000))
            {
                lock (_lock) _pending.Remove(requestId);
                return "{\"error\":\"timeout\"}";
            }

            var response = tcs.Task.Result;
            return response.Result ?? "null";
        }
        catch (Exception ex)
        {
            lock (_lock) _pending.Remove(requestId);
            Console.WriteLine($"[JsEngineHost] SendToMain error: {ex.Message}");
            return $"{{\"error\":\"{ex.Message}\"}}";
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _transport != null)
        {
            try
            {
                var (success, data) = await _transport.ReceiveAsyncNoThrow(ct);
                if (!success) continue;
                if (data.Length == 0) continue;

                var msg = ProtocolSerializer.DeserializeAny(data);
            if (msg.Request != null)
            {
                var reqCopy = msg.Request;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await ProcessRequest(reqCopy);
                        var responseData = ProtocolSerializer.Serialize(result);
                        await _transport!.SendAsync(responseData);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[JsEngineHost] Request processing error: {ex.Message}");
                        var errorResponse = new IpcResponse
                        {
                            RequestId = reqCopy.RequestId,
                            Success = false,
                            Error = ex.Message
                        };
                        var errorData = ProtocolSerializer.Serialize(errorResponse);
                        await _transport!.SendAsync(errorData);
                    }
                });
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
        catch (EndOfStreamException) { break; }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[JsEngineHost] Error: {ex.Message}");
        }
    }
        _cts?.Cancel();
    }

    private async Task<IpcResponse> ProcessRequest(IpcRequest request)
    {
        try
        {
            return request.Type switch
            {
                RequestType.Execute => HandleExecute(request),
                RequestType.Evaluate => HandleEvaluate(request),
                RequestType.CallFunction => HandleCallFunction(request),
                RequestType.InvokeCallback => HandleInvokeCallback(request),
                RequestType.DisposeEngine => HandleDispose(),
                RequestType.Ping => new IpcResponse { RequestId = request.RequestId, Success = true, Type = ResponseType.Pong },
                RequestType.DomCall => HandleDomCall(request),
                _ => new IpcResponse { RequestId = request.RequestId, Success = false, Error = $"Unknown: {request.Type}" }
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleDomCall(IpcRequest request)
    {
        try
        {
            var payload = request.Payload ?? "";
            var sep = payload.IndexOf('|');
            if (sep < 0)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid payload" };

            var method = payload[..sep];
            var argsJson = payload[(sep + 1)..];

            var result = SendToMain(method, argsJson);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Result = result };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JsEngineHost] HandleDomCall ERROR: {ex.Message}");
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleExecute(IpcRequest request)
    {
        _engineLock.Wait();
        try
        {
            var code = request.Payload ?? "";
            try
            {
                _engine.Execute(code);
                return new IpcResponse { RequestId = request.RequestId, Success = true };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[JsEngineHost] Execute error: {ex.Message}");
                Console.Error.WriteLine($"[JsEngineHost] Code: {code[..Math.Min(500, code.Length)]}");
                throw;
            }
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private IpcResponse HandleEvaluate(IpcRequest request)
    {
        _engineLock.Wait();
        try
        {
            var code = request.Payload ?? "";
            try
            {
                var result = _engine.Evaluate(code);
                return new IpcResponse
                {
                    RequestId = request.RequestId,
                    Success = true,
                    Result = result?.ToString()
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[JsEngineHost] Evaluate error: {ex.Message}");
                Console.Error.WriteLine($"[JsEngineHost] Code: {code[..Math.Min(500, code.Length)]}");
                throw;
            }
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private IpcResponse HandleCallFunction(IpcRequest request)
    {
        _engineLock.Wait();
        try
        {
            if (request.Payload != null)
            {
                var parts = ParseJsonStringArray(request.Payload);
                if (parts != null && parts.Length >= 1)
                {
                    var globalObj = _engine.GetValue("globalThis");
                    var fn = _engine.GetValue(parts[0]);
                    if (parts.Length > 1)
                    {
                        var jsArgs = parts.Skip(1).Select<string, JsValue>(a => (JsValue)a).ToArray();
                        var result = fn.Call(globalObj, jsArgs);
                        return new IpcResponse { RequestId = request.RequestId, Success = true, Result = result?.ToString() };
                    }
                    else
                    {
                        var result = fn.Call(globalObj);
                        return new IpcResponse { RequestId = request.RequestId, Success = true, Result = result?.ToString() };
                    }
                }
            }
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid call" };
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private static string[]? ParseJsonStringArray(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array) return null;
        var arr = new string[root.GetArrayLength()];
        var i = 0;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
                arr[i++] = el.GetString() ?? "";
            else
                arr[i++] = el.GetRawText();
        }
        return arr;
    }

    private IpcResponse HandleInvokeCallback(IpcRequest request)
    {
        _engineLock.Wait();
        try
        {
            var payload = request.Payload ?? "";
            var payloadJson = JsonEncodedText.Encode(payload).ToString();
            var code = $"__g_invoke({request.CallbackId}, {payloadJson})";
            _engine.Evaluate(code);
            return new IpcResponse { RequestId = request.RequestId, Success = true, Type = ResponseType.CallbackInvoked };
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private IpcResponse HandleDispose()
    {
        _cts?.Cancel();
        return new IpcResponse { RequestId = 0, Success = true };
    }
}