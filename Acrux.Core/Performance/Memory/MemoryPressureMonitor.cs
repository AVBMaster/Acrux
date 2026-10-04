using Acrux.Core.Performance.Resources;

namespace Acrux.Core.Performance.Memory;

/// <summary>
/// Levels of memory pressure, in increasing order. The current level triggers a set of
/// shrink actions on registered resources.
/// </summary>
public enum MemoryPressureLevel : byte
{
    Nominal = 0,
    Moderate = 1,
    High = 2,
    Critical = 3,
}

public sealed class MemoryBudget
{
    public long TotalCapacityBytes { get; }
    public long ReservedForWorkingSet { get; init; } = 32L * 1024 * 1024; // 32 MB

    public long ScriptHeapSoftLimit => (long)(TotalCapacityBytes * 0.30);
    public long ImagePoolSoftLimit => (long)(TotalCapacityBytes * 0.40);
    public long ResourceCacheSoftLimit => (long)(TotalCapacityBytes * 0.20);
    public long TileMemorySoftLimit => (long)(TotalCapacityBytes * 0.20);

    public MemoryBudget(long totalCapacityBytes)
    {
        TotalCapacityBytes = Math.Max(0, totalCapacityBytes);
    }
}

/// <summary>
/// Centralised memory pressure monitor. Components register shrink/expand callbacks
/// and the monitor calls them in priority order when the pressure level changes.
/// </summary>
public sealed class MemoryPressureMonitor
{
    private readonly List<Registered> _responders = new();
    private readonly object _lock = new();
    private MemoryPressureLevel _level = MemoryPressureLevel.Nominal;
    private long _currentBytes;
    private long _criticalEvents;
    private long _moderateEvents;
    private long _shrinks;
    private long _expansions;

    public MemoryPressureLevel Level => _level;
    public long CurrentBytes => Interlocked.Read(ref _currentBytes);
    public long CriticalEvents => Interlocked.Read(ref _criticalEvents);
    public long ModerateEvents => Interlocked.Read(ref _moderateEvents);
    public long Shrinks => Interlocked.Read(ref _shrinks);
    public long Expansions => Interlocked.Read(ref _expansions);
    public int ResponderCount { get { lock (_lock) return _responders.Count; } }

    public event Action<MemoryPressureLevel, MemoryPressureLevel>? OnPressureChanged;

    public void Register(MemoryResponder responder, int priority = 0)
    {
        if (responder is null) return;
        lock (_lock)
        {
            _responders.Add(new Registered(responder, priority));
            _responders.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }

    public void Unregister(MemoryResponder responder)
    {
        lock (_lock)
        {
            _responders.RemoveAll(r => r.Source == responder);
        }
    }

    public void ReportUsage(long currentBytes)
    {
        Interlocked.Exchange(ref _currentBytes, currentBytes);
        var newLevel = Classify(currentBytes);
        if (newLevel != _level)
        {
            var old = _level;
            _level = newLevel;
            if (newLevel >= MemoryPressureLevel.High) Interlocked.Increment(ref _criticalEvents);
            else if (newLevel >= MemoryPressureLevel.Moderate) Interlocked.Increment(ref _moderateEvents);
            ApplyPressure(newLevel);
            OnPressureChanged?.Invoke(old, newLevel);
        }
    }

    public int ForceShrink()
    {
        ApplyPressure(MemoryPressureLevel.Critical);
        return (int)Interlocked.Read(ref _shrinks);
    }

    private MemoryPressureLevel Classify(long bytes) => bytes switch
    {
        var b when b > 5L * 1024 * 1024 * 1024 => MemoryPressureLevel.Critical, // > 5 GB
        var b when b > 2L * 1024 * 1024 * 1024 => MemoryPressureLevel.High,
        var b when b > 512L * 1024 * 1024 => MemoryPressureLevel.Moderate,
        _ => MemoryPressureLevel.Nominal,
    };

    private void ApplyPressure(MemoryPressureLevel level)
    {
        Registered[] snapshot;
        lock (_lock) snapshot = _responders.ToArray();
        bool isShrink = level > MemoryPressureLevel.Nominal;
        foreach (var r in snapshot)
        {
            try
            {
                if (isShrink) r.Source.OnMemoryPressure(level);
                else r.Source.OnMemoryRelease(level);
                if (isShrink) Interlocked.Increment(ref _shrinks);
                else Interlocked.Increment(ref _expansions);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MemoryPressure] responder threw: {ex.Message}");
            }
        }
    }

    private sealed record Registered(MemoryResponder Source, int Priority);
}

/// <summary>
/// Base class for memory-aware components. Override the relevant shrink/expand hooks
/// to drop or restore caches when pressure changes.
/// </summary>
public abstract class MemoryResponder
{
    public abstract string Name { get; }
    public virtual void OnMemoryPressure(MemoryPressureLevel level) { }
    public virtual void OnMemoryRelease(MemoryPressureLevel level) { }
}

/// <summary>
/// Aggregate pressure responder that coordinates the major caches (resource cache,
/// decoded image pool, tile manager) in a single place.
/// </summary>
public sealed class AggregateMemoryResponder : MemoryResponder
{
    public override string Name => "aggregate";
    public ResourceCache? Resources { get; set; }
    public DecodedImagePool? Images { get; set; }
    public Compositor.TileManager? Tiles { get; set; }

    public override void OnMemoryPressure(MemoryPressureLevel level)
    {
        if (Resources is null && Images is null && Tiles is null) return;
        long factor = level switch
        {
            MemoryPressureLevel.Critical => 4,
            MemoryPressureLevel.High => 2,
            MemoryPressureLevel.Moderate => 1,
            _ => 0,
        };
        if (factor == 0) return;

        if (Resources is not null)
        {
            long newCap = Resources.CapacityBytes / factor;
            if (newCap < 1024 * 1024) newCap = 1024 * 1024; // 1 MB floor
            Resources.SetCapacity(newCap);
        }
        if (Images is not null)
        {
            long newCap = Images.CapacityBytes / factor;
            if (newCap < 1024 * 1024) newCap = 1024 * 1024;
            Images.SetCapacity(newCap);
        }
        if (Tiles is not null)
        {
            int newCap = Math.Max(32, Tiles.Settings.MaxTilesInMemory / (int)factor);
            // Tile manager max is not directly settable; evict to enforce.
            while (Tiles.ActiveCount > newCap) Tiles.EnforceMemoryBudget();
        }
    }

    public override void OnMemoryRelease(MemoryPressureLevel level)
    {
        // Could expand capacities; default no-op.
    }
}
