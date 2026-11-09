using System.Diagnostics;

namespace Acrux.Core;

/// <summary>
/// Stage-level latency trace for the page pipeline, off unless ACRUX_PERFTRACE=1.
/// Every line carries a process-wide monotonic millisecond clock so the shell, the
/// tab host and the engine can be lined up on one timeline. When disabled the only
/// per-call cost is one static bool read.
/// </summary>
public static class PerfTrace
{
    private static readonly long T0 = Stopwatch.GetTimestamp();
    public static bool Enabled => _enabled;
    private static readonly bool _enabled =
        Environment.GetEnvironmentVariable("ACRUX_PERFTRACE") == "1";

    public static void Trace(string what)
    {
        if (!Enabled) return;
        long ms = (Stopwatch.GetTimestamp() - T0) * 1000 / Stopwatch.Frequency;
        Console.Error.WriteLine($"[PERF {ms,7}ms pid={Environment.ProcessId} t{Environment.CurrentManagedThreadId % 100:00}] {what}");
    }

    public static void TraceMs(string what, long startTimestamp)
    {
        if (!Enabled) return;
        long us = (Stopwatch.GetTimestamp() - startTimestamp) * 1_000_000 / Stopwatch.Frequency;
        Trace($"{what} +{(us / 1000.0):F1}ms");
    }

    public static long Now() => Enabled ? Stopwatch.GetTimestamp() : 0;
}
