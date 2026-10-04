namespace Acrux.Core.Performance.Diagnostics;

/// <summary>
/// Stable identifier of a performance mark/measure, exposed by <see cref="PerformanceApi"/>
/// for developer code to attach custom timings. Mirrors the W3C <c>performance.mark</c>
/// / <c>performance.measure</c> API at a small scale.
/// </summary>
public sealed class PerformanceMark
{
    public required string Name { get; init; }
    public required long StartNanos { get; init; }
    public string? Detail { get; init; }
}

public sealed class PerformanceMeasure
{
    public required string Name { get; init; }
    public required long StartNanos { get; init; }
    public required long DurationNanos { get; init; }
    public string? Detail { get; init; }
    public double DurationMillis => DurationNanos / 1_000_000.0;
}

/// <summary>
/// Developer-facing performance API. Exposes marks/measures, layer tree, and
/// snapshot of the most relevant counters. Stays in lock-step with the
/// <see cref="PerformanceRegistry"/>'s internal state.
/// </summary>
public sealed class PerformanceApi
{
    private readonly Dictionary<string, PerformanceMark> _marks = new();
    private readonly List<PerformanceMeasure> _measures = new();
    private readonly object _lock = new();
    private readonly PerformanceRegistry _registry;

    public PerformanceApi(PerformanceRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<PerformanceMark> Marks
    {
        get { lock (_lock) return _marks.Values.ToArray(); }
    }

    public IReadOnlyList<PerformanceMeasure> Measures
    {
        get { lock (_lock) return _measures.ToArray(); }
    }

    public void Mark(string name, string? detail = null)
    {
        if (string.IsNullOrEmpty(name)) return;
        lock (_lock)
        {
            _marks[name] = new PerformanceMark
            {
                Name = name,
                StartNanos = Clock.NowNanos(),
                Detail = detail,
            };
        }
    }

    public PerformanceMeasure? Measure(string name, string startMark, string? endMark = null, string? detail = null)
    {
        lock (_lock)
        {
            if (!_marks.TryGetValue(startMark, out var start))
                throw new InvalidOperationException($"Unknown start mark '{startMark}'");
            long endNanos = endMark is null
                ? Clock.NowNanos()
                : (_marks.TryGetValue(endMark, out var end) ? end.StartNanos : throw new InvalidOperationException($"Unknown end mark '{endMark}'"));
            var measure = new PerformanceMeasure
            {
                Name = name,
                StartNanos = start.StartNanos,
                DurationNanos = Math.Max(0, endNanos - start.StartNanos),
                Detail = detail,
            };
            _measures.Add(measure);
            return measure;
        }
    }

    public void ClearMarks()
    {
        lock (_lock) _marks.Clear();
    }

    public void ClearMeasures()
    {
        lock (_lock) _measures.Clear();
    }

    public string Snapshot() => _registry.SnapshotJson();
}
