using Acrux.Core.Performance;
using Acrux.Core.Performance.Resources;
using Acrux.Core.Performance.Scheduling;
using Acrux.Core.Performance.Memory;
using Acrux.Core.Performance.Compositor;
using Xunit;
using Xunit.Abstractions;

namespace Acrux.Core.Tests.Benchmarks;

/// <summary>
/// Hand-rolled micro-benchmarks: each test measures throughput of a hot-path data
/// structure or subsystem. They live inside the xunit project so they can run as
/// part of CI. The output is printed to the test log (using <see cref="ITestOutputHelper"/>).
/// </summary>
public class MicroBenchmarks
{
    private readonly ITestOutputHelper _output;

    public MicroBenchmarks(ITestOutputHelper output) { _output = output; }

    private static void Run(string name, int iterations, Action body)
    {
        // Warmup
        for (int i = 0; i < Math.Min(100, iterations / 10); i++) body();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) body();
        sw.Stop();
        var opsPerSec = iterations / Math.Max(0.0001, sw.Elapsed.TotalSeconds);
        Console.WriteLine($"[Bench] {name}: {iterations} ops in {sw.ElapsedMilliseconds}ms ({opsPerSec:F0} ops/s)");
    }

    [Fact]
    public void ResourceCache_PutGet_Throughput()
    {
        Run("ResourceCache Put/Get 100k", 100_000, () =>
        {
            var cache = new ResourceCache();
            cache.SetCapacity(10 * 1024 * 1024);
            for (int i = 0; i < 1000; i++)
            {
                cache.Put("k" + i, new ResourceResponse { Body = new byte[64], ContentLength = 64 });
                cache.TryGet("k" + i, out _);
            }
        });
    }

    [Fact]
    public void PriorityQueue_Throughput()
    {
        Run("PriorityResourceQueue enqueue+dequeue 100k", 100_000, () =>
        {
            var q = new PriorityResourceQueue();
            for (int i = 0; i < 1000; i++)
            {
                q.Enqueue(new ResourceRequest
                {
                    Url = "u" + i,
                    Kind = ResourceKind.Image,
                    Priority = (ResourcePriority)(i % 5),
                });
            }
            while (q.Dequeue() is not null) { }
        });
    }

    [Fact]
    public void ObjectPool_RentReturn_Throughput()
    {
        Run("ObjectPool rent/return 1M", 1_000_000, () =>
        {
            var pool = new ObjectPool<Payload>(() => new Payload(), maxIdle: 64);
            var p = pool.Rent();
            p.X = 1;
            pool.Return(p);
        });
    }

    [Fact]
    public void TimingAccumulator_ConcurrentAdd()
    {
        var acc = new TimingAccumulator();
        Run("TimingAccumulator concurrent add 1M", 1_000_000, () => acc.AddSample(123));
    }

    [Fact]
    public void CooperativeScheduler_PostAndDrain()
    {
        Run("CooperativeScheduler 100k tasks", 100_000, () =>
        {
            var s = new CooperativeScheduler();
            for (int i = 0; i < 1000; i++) s.PostTask(() => { });
            s.Drain();
        });
    }

    [Fact]
    public void TileManager_TilesForRect_Throughput()
    {
        var tm = new TileManager();
        Run("TileManager.TilesForRect 1k calls", 10_000, () =>
        {
            var rect = new SkiaSharp.SKRect(0, 0, 4096, 4096);
            var keys = tm.TilesForRect(rect, 1).ToArray();
            GC.KeepAlive(keys);
        });
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

    private sealed class Payload : IResettable
    {
        public int X;
        public void Reset() { X = 0; }
    }
}

