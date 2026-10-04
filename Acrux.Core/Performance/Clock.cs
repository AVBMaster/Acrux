using System.Diagnostics;

namespace Acrux.Core.Performance;

/// <summary>
/// High-resolution time source with monotonic guarantee.
/// All time values are in nanoseconds (long) for precision and arithmetic safety.
/// Wraps <see cref="Stopwatch"/> and exposes a stable API that can be swapped in tests.
/// </summary>
public static class Clock
{
    private static readonly double _ticksToNanos = (1_000_000_000.0) / Stopwatch.Frequency;
    private static readonly long _startNanos = NowNanos();

    /// <summary>Returns the monotonic wall clock time in nanoseconds since process start.</summary>
    public static long NowNanos()
    {
        return (long)(Stopwatch.GetTimestamp() * _ticksToNanos);
    }

    /// <summary>Returns the monotonic wall clock time in milliseconds.</summary>
    public static double NowMillis() => NowNanos() / 1_000_000.0;

    /// <summary>Returns the monotonic wall clock time in microseconds.</summary>
    public static double NowMicros() => NowNanos() / 1_000.0;

    /// <summary>Process start in nanoseconds — useful for relative-time tests.</summary>
    public static long StartNanos() => _startNanos;

    /// <summary>Convert nanoseconds to milliseconds.</summary>
    public static double NanosToMillis(long nanos) => nanos / 1_000_000.0;

    /// <summary>Convert nanoseconds to microseconds.</summary>
    public static double NanosToMicros(long nanos) => nanos / 1_000.0;
}

/// <summary>
/// Disposable timing scope: records elapsed time in nanoseconds when disposed.
/// Designed to be zero-overhead when discarded; results are written to a <see cref="TimingAccumulator"/>.
/// </summary>
public readonly struct TimingScope : IDisposable
{
    private readonly TimingAccumulator? _sink;
    private readonly long _start;

    public TimingScope(TimingAccumulator sink)
    {
        _sink = sink;
        _start = Clock.NowNanos();
    }

    public void Dispose()
    {
        if (_sink is null) return;
        var elapsed = Clock.NowNanos() - _start;
        _sink.AddSample(elapsed);
    }

    public static TimingScope Measure(TimingAccumulator sink) => new(sink);
}

/// <summary>
/// Aggregator for timing samples. Keeps running statistics without storing every sample.
/// Thread-safe via interlocked operations.
/// </summary>
public sealed class TimingAccumulator
{
    private long _count;
    private long _sumNanos;
    private long _minNanos = long.MaxValue;
    private long _maxNanos;

    public long Count => Interlocked.Read(ref _count);
    public long SumNanos => Interlocked.Read(ref _sumNanos);
    public long MinNanos => Interlocked.Read(ref _minNanos);
    public long MaxNanos => Interlocked.Read(ref _maxNanos);

    public double MeanNanos => _count == 0 ? 0 : (double)SumNanos / _count;
    public double MeanMicros => MeanNanos / 1_000.0;
    public double MeanMillis => MeanNanos / 1_000_000.0;
    public double MinMicros => MinNanos == long.MaxValue ? 0 : MinNanos / 1_000.0;
    public double MaxMicros => MaxNanos / 1_000.0;
    public double MaxMillis => MaxNanos / 1_000_000.0;
    public double SumMillis => SumNanos / 1_000_000.0;

    public void AddSample(long elapsedNanos)
    {
        if (elapsedNanos < 0) return;
        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _sumNanos, elapsedNanos);

        // Min/Max need CAS retry
        long curMin, curMax;
        do
        {
            curMin = Interlocked.Read(ref _minNanos);
            if (elapsedNanos >= curMin) break;
        } while (Interlocked.CompareExchange(ref _minNanos, elapsedNanos, curMin) != curMin);

        do
        {
            curMax = Interlocked.Read(ref _maxNanos);
            if (elapsedNanos <= curMax) break;
        } while (Interlocked.CompareExchange(ref _maxNanos, elapsedNanos, curMax) != curMax);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _sumNanos, 0);
        Interlocked.Exchange(ref _minNanos, long.MaxValue);
        Interlocked.Exchange(ref _maxNanos, 0);
    }

    public override string ToString() =>
        $"n={Count} mean={MeanMicros:F2}µs min={MinMicros:F2}µs max={MaxMicros:F2}µs total={SumMillis:F2}ms";
}

/// <summary>
/// Lightweight monotonic counter for events-per-frame metrics (tiles, images, etc).
/// </summary>
public sealed class CounterAccumulator
{
    private long _value;

    public long Value => Interlocked.Read(ref _value);

    public void AddSample(long delta) => Interlocked.Add(ref _value, delta);

    public void Set(long value) => Interlocked.Exchange(ref _value, value);

    public void Reset() => Interlocked.Exchange(ref _value, 0);
}

/// <summary>
/// Centralised timing probes for the major rendering pipeline phases.
/// Use <see cref="Start"/> / <see cref="End"/> pairs in production code; the static fields
/// here are read by diagnostics UIs.
/// </summary>
public static class PipelineTimings
{
    public static readonly TimingAccumulator Style = new();
    public static readonly TimingAccumulator Layout = new();
    public static readonly TimingAccumulator Paint = new();
    public static readonly TimingAccumulator Composite = new();
    public static readonly TimingAccumulator Script = new();
    public static readonly TimingAccumulator ImageDecode = new();
    public static readonly TimingAccumulator TileRaster = new();
    public static readonly TimingAccumulator NetworkWait = new();

    public static readonly CounterAccumulator TilesRasterized = new();
    public static readonly CounterAccumulator TilesReused = new();
    public static readonly CounterAccumulator ImagesDecoded = new();
    public static readonly CounterAccumulator ImageCacheHits = new();
    public static readonly CounterAccumulator ResourceCacheHits = new();

    public static void ResetAll()
    {
        Style.Reset();
        Layout.Reset();
        Paint.Reset();
        Composite.Reset();
        Script.Reset();
        ImageDecode.Reset();
        TileRaster.Reset();
        NetworkWait.Reset();
        TilesRasterized.Reset();
        TilesReused.Reset();
        ImagesDecoded.Reset();
        ImageCacheHits.Reset();
        ResourceCacheHits.Reset();
    }
}
