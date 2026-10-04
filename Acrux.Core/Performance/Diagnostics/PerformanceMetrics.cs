using System.Collections.Concurrent;
using Acrux.Core.Performance.Memory;
using Acrux.Core.Performance.Scheduling;

namespace Acrux.Core.Performance.Diagnostics;

/// <summary>
/// The five user-perceived performance signals defined by the W3C Web Vitals spec
/// and Acrux's extensions. We expose a single API for both the runtime and the
/// devtools surface.
/// </summary>
public sealed class PerformanceMetrics
{
    public long FirstPaintNanos { get; private set; }
    public long FirstContentfulPaintNanos { get; private set; }
    public long LargestContentfulPaintNanos { get; private set; }
    public long TimeToInteractiveNanos { get; private set; }
    public long TotalBlockingTimeNanos { get; private set; }
    public long CumulativeLayoutShiftMicros { get; private set; }
    public long FirstInputDelayNanos { get; private set; }

    public DateTime? FirstPaintAt { get; private set; }
    public DateTime? FirstContentfulPaintAt { get; private set; }
    public DateTime? LargestContentfulPaintAt { get; private set; }
    public DateTime? TimeToInteractiveAt { get; private set; }

    public bool HasFirstPaint => FirstPaintNanos > 0;
    public bool HasFirstContentfulPaint => FirstContentfulPaintNanos > 0;
    public bool HasLargestContentfulPaint => LargestContentfulPaintNanos > 0;
    public bool HasTimeToInteractive => TimeToInteractiveNanos > 0;

    public double FirstPaintMillis => FirstPaintNanos / 1_000_000.0;
    public double FirstContentfulPaintMillis => FirstContentfulPaintNanos / 1_000_000.0;
    public double LargestContentfulPaintMillis => LargestContentfulPaintNanos / 1_000_000.0;
    public double TimeToInteractiveMillis => TimeToInteractiveNanos / 1_000_000.0;
    public double TotalBlockingTimeMillis => TotalBlockingTimeNanos / 1_000_000.0;
    public double CumulativeLayoutShiftMillis => CumulativeLayoutShiftMicros / 1_000.0;
    public double FirstInputDelayMillis => FirstInputDelayNanos / 1_000_000.0;

    public void RecordFirstPaint()
    {
        if (FirstPaintNanos == 0) FirstPaintNanos = Clock.NowNanos();
        FirstPaintAt ??= DateTime.UtcNow;
    }

    public void RecordFirstContentfulPaint()
    {
        if (FirstContentfulPaintNanos == 0) FirstContentfulPaintNanos = Clock.NowNanos();
        FirstContentfulPaintAt ??= DateTime.UtcNow;
    }

    public void RecordLargestContentfulPaint()
    {
        var now = Clock.NowNanos();
        if (LargestContentfulPaintNanos == 0 || now > LargestContentfulPaintNanos)
        {
            LargestContentfulPaintNanos = now;
            LargestContentfulPaintAt ??= DateTime.UtcNow;
        }
    }

    public void RecordTimeToInteractive()
    {
        if (TimeToInteractiveNanos == 0) TimeToInteractiveNanos = Clock.NowNanos();
        TimeToInteractiveAt ??= DateTime.UtcNow;
    }

    public void RecordFirstInputDelay(long delayNanos)
    {
        if (delayNanos < 0) return;
        if (delayNanos > FirstInputDelayNanos) FirstInputDelayNanos = delayNanos;
    }

    public void AddBlockingTime(long blockNanos)
    {
        if (blockNanos > 0) TotalBlockingTimeNanos += blockNanos;
    }

    public void AddLayoutShift(long shiftMicros)
    {
        if (shiftMicros > 0) CumulativeLayoutShiftMicros += shiftMicros;
    }

    public void Reset()
    {
        FirstPaintNanos = FirstContentfulPaintNanos = LargestContentfulPaintNanos = 0;
        TimeToInteractiveNanos = TotalBlockingTimeNanos = 0;
        CumulativeLayoutShiftMicros = FirstInputDelayNanos = 0;
        FirstPaintAt = FirstContentfulPaintAt = LargestContentfulPaintAt = null;
        TimeToInteractiveAt = null;
    }
}

/// <summary>
/// Central performance registry. Wires together timing accumulators, long task
/// observer, memory pressure monitor and metrics. This is the single object that
/// diagnostics UIs query to render performance panels.
/// </summary>
public sealed class PerformanceRegistry
{
    public PerformanceMetrics Metrics { get; } = new();
    public LongTaskObserver LongTasks { get; }
    public MemoryPressureMonitor MemoryPressure { get; } = new();
    public DiagnosticsFeed Feed { get; } = new();
    public DateTime SessionStartedAt { get; } = DateTime.UtcNow;
    public TimeSpan SessionUptime => DateTime.UtcNow - SessionStartedAt;

    public PerformanceRegistry(LongTaskObserver? longTasks = null)
    {
        LongTasks = longTasks ?? new LongTaskObserver();
    }

    public string SnapshotJson()
    {
        var sb = new System.Text.StringBuilder(512);
        sb.Append("{");
        sb.AppendFormat("\"sessionStartedAt\":\"{0:o}\",", SessionStartedAt);
        sb.AppendFormat("\"sessionUptimeSeconds\":{0:F2},", SessionUptime.TotalSeconds);

        sb.Append("\"metrics\":{");
        sb.AppendFormat("\"firstPaintMs\":{0:F2},", Metrics.FirstPaintMillis);
        sb.AppendFormat("\"firstContentfulPaintMs\":{0:F2},", Metrics.FirstContentfulPaintMillis);
        sb.AppendFormat("\"largestContentfulPaintMs\":{0:F2},", Metrics.LargestContentfulPaintMillis);
        sb.AppendFormat("\"timeToInteractiveMs\":{0:F2},", Metrics.TimeToInteractiveMillis);
        sb.AppendFormat("\"totalBlockingTimeMs\":{0:F2},", Metrics.TotalBlockingTimeMillis);
        sb.AppendFormat("\"cumulativeLayoutShift\":{0:F3},", Metrics.CumulativeLayoutShiftMillis);
        sb.AppendFormat("\"firstInputDelayMs\":{0:F2}", Metrics.FirstInputDelayMillis);
        sb.Append("},");

        sb.Append("\"longTasks\":{");
        sb.AppendFormat("\"observed\":{0},", LongTasks.Observed);
        sb.AppendFormat("\"thresholdMicros\":{0},", LongTasks.ThresholdMicros);
        sb.Append("\"recent\":[");
        bool first = true;
        foreach (var e in LongTasks.Snapshot())
        {
            if (!first) sb.Append(',');
            first = false;
            sb.AppendFormat("{{\"name\":\"{0}\",\"durationMs\":{1:F2},\"priority\":{2}}}",
                Escape(e.Name), e.DurationNanos / 1_000_000.0, (int)e.Priority);
        }
        sb.Append("]},");

        sb.Append("\"pipeline\":{");
        sb.AppendFormat("\"style\":[{0}],", FormatTiming(PipelineTimings.Style));
        sb.AppendFormat("\"layout\":[{0}],", FormatTiming(PipelineTimings.Layout));
        sb.AppendFormat("\"paint\":[{0}],", FormatTiming(PipelineTimings.Paint));
        sb.AppendFormat("\"composite\":[{0}],", FormatTiming(PipelineTimings.Composite));
        sb.AppendFormat("\"script\":[{0}],", FormatTiming(PipelineTimings.Script));
        sb.AppendFormat("\"imageDecode\":[{0}],", FormatTiming(PipelineTimings.ImageDecode));
        sb.AppendFormat("\"tileRaster\":[{0}],", FormatTiming(PipelineTimings.TileRaster));
        sb.AppendFormat("\"networkWait\":[{0}]", FormatTiming(PipelineTimings.NetworkWait));
        sb.Append("},");

        sb.Append("\"memory\":{");
        sb.AppendFormat("\"level\":{0},", (int)MemoryPressure.Level);
        sb.AppendFormat("\"currentBytes\":{0},", MemoryPressure.CurrentBytes);
        sb.AppendFormat("\"shrinks\":{0},", MemoryPressure.Shrinks);
        sb.AppendFormat("\"expansions\":{0}", MemoryPressure.Expansions);
        sb.Append("}");

        sb.Append("}");
        return sb.ToString();
    }

    private static string FormatTiming(Acrux.Core.Performance.TimingAccumulator t) =>
        $"\"n\":{t.Count},\"meanMs\":{t.MeanMillis:F3},\"maxMs\":{t.MaxMillis:F3},\"totalMs\":{t.SumMillis:F3}";

    private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>
/// Ring buffer of diagnostic events. Used by the devtools timeline view.
/// </summary>
public sealed class DiagnosticsFeed
{
    private readonly ConcurrentQueue<Entry> _entries = new();
    private readonly int _capacity;
    private long _dropped;

    public DiagnosticsFeed(int capacity = 512)
    {
        _capacity = Math.Max(16, capacity);
    }

    public long Dropped => Interlocked.Read(ref _dropped);

    public void Append(string category, string message)
    {
        _entries.Enqueue(new Entry { Category = category, Message = message, At = DateTime.UtcNow });
        while (_entries.Count > _capacity && _entries.TryDequeue(out _)) Interlocked.Increment(ref _dropped);
    }

    public IReadOnlyList<Entry> Snapshot() => _entries.ToArray();

    public sealed class Entry
    {
        public required string Category { get; init; }
        public required string Message { get; init; }
        public required DateTime At { get; init; }
    }
}
