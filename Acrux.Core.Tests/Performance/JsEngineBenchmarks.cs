#if USE_MULTIPLE_JS_ENGINE
using System.Diagnostics;
using System.Text.Json;
using Acrux.Core.JavaScript;
using Acrux.Core.Performance;
using Acrux.JsEngineProtocol;
using Xunit;
using Xunit.Abstractions;

namespace Acrux.Core.Tests.Benchmarks;

/// <summary>
/// JS engine & DOM IPC micro-benchmarks: measure per-operation IPC overhead,
/// serialization cost, and batch-vs-unbatch throughput.
/// </summary>
public class JsEngineBenchmarks
{
    private readonly ITestOutputHelper _output;

    public JsEngineBenchmarks(ITestOutputHelper output) { _output = output; }

    private static void Run(string name, int iterations, Action body)
    {
        for (int i = 0; i < Math.Min(100, iterations / 10); i++) body();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) body();
        sw.Stop();
        var opsPerSec = iterations / Math.Max(0.0001, sw.Elapsed.TotalSeconds);
        Console.WriteLine($"[Bench] {name}: {iterations} ops in {sw.ElapsedMilliseconds}ms ({opsPerSec:F0} ops/s)");
    }

    private static void RunWithMs(string name, int iterations, Action body)
    {
        for (int i = 0; i < Math.Min(100, iterations / 10); i++) body();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) body();
        sw.Stop();
        var avgMs = sw.Elapsed.TotalMilliseconds / Math.Max(1, iterations);
        Console.WriteLine($"[Bench] {name}: {sw.ElapsedMilliseconds}ms total, avg {avgMs:F3}ms/operation");
    }

    [Fact]
    public void DirtyFlags_RecordAndCheck()
    {
        var el = new Acrux.Core.Dom.HtmlElement("div");
        Run("DirtyFlags add/check 1M", 1_000_000, () =>
        {
            DirtyState.AddSelf(el, DirtyFlags.Layout);
            var _ = DirtyState.IsClean(el);
            DirtyState.ClearAll(el);
        });
    }

    [Fact]
    public void IpcRequest_SerializeOverhead()
    {
        // Simulates the cost of creating and serializing an IpcRequest per DOM call
        Run("IpcRequest serialize 100k", 100_000, () =>
        {
            var request = new IpcRequest
            {
                RequestId = 12345,
                Type = RequestType.DomCall,
                Payload = "dom_getProperty|[\"42\",\"offsetWidth\"]"
            };
            var data = ProtocolSerializer.Serialize(request);
            GC.KeepAlive(data);
        });
    }

    [Fact]
    public void IpcResponse_SerializeOverhead()
    {
        Run("IpcResponse serialize 100k", 100_000, () =>
        {
            var response = new IpcResponse
            {
                RequestId = 12345,
                Success = true,
                Result = "100"
            };
            var data = ProtocolSerializer.Serialize(response);
            GC.KeepAlive(data);
        });
    }

    [Fact]
    public void DomProxyStore_RegisterElement_Throughput()
    {
        var store = new DomProxyStore();
        var host = new ElementHost(new Acrux.Core.Dom.HtmlElement("div"));
        Run("DomProxyStore RegisterElement 100k", 100_000, () =>
        {
            var el = new ElementHost(new Acrux.Core.Dom.HtmlElement("span"));
            store.RegisterElement(el);
        });
    }

    [Fact]
    public void DomProxyStore_RegisterElements_BatchThroughput()
    {
        var store = new DomProxyStore();
        var batch = new List<ElementHost>(50);
        for (int i = 0; i < 50; i++)
            batch.Add(new ElementHost(new Acrux.Core.Dom.HtmlElement("span")));

        Run("DomProxyStore RegisterElements[50] x2000", 2_000, () =>
        {
            var fresh = new List<ElementHost>(50);
            for (int i = 0; i < 50; i++)
                fresh.Add(new ElementHost(new Acrux.Core.Dom.HtmlElement("span")));
            store.RegisterElements(fresh);
        });
    }

    [Fact]
    public void Json_GetStringArg_DeserializeOverhead()
    {
        var argsJson = "[\"42\",\"offsetWidth\"]";
        Run("Json deserialize string[2] 1M", 1_000_000, () =>
        {
            var arr = JsonSerializer.Deserialize<string[]>(argsJson);
            GC.KeepAlive(arr);
        });
    }

    [Fact]
    public void Json_PropBatch_DeserializeOverhead()
    {
        Run("Json deserialize batch props (6) 100k", 100_000, () =>
        {
            var outer = JsonSerializer.Deserialize<string[]>(JSONBatchRequest());
            if (outer != null && outer.Length >= 2)
            {
                var props = JsonSerializer.Deserialize<string[]>(outer[1]!);
                GC.KeepAlive(props);
            }
        });
    }

    private static string JSONBatchRequest()
    {
        return "[\"42\",\"" + propNamesJson + "\"]";
    }

    private static string propNamesJson =>
        "[\"offsetWidth\",\"offsetHeight\",\"clientWidth\",\"clientHeight\",\"offsetTop\",\"offsetLeft\"]";

    private sealed class JsPayload
    {
        public float Width;
        public float Height;
    }
}
#endif
