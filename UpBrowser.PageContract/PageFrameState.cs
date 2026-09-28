namespace UpBrowser.PageContract;

/// <summary>
/// Metadata the shell caches from the most recent committed frame. Everything the
/// chrome needs to draw a scrollbar, a title or a status hint without ever asking
/// the engine — so no field here may require a round trip to read.
/// </summary>
public sealed record PageFrameState
{
    public bool HasFrame { get; init; }
    /// <summary>Rasterized size in device pixels.</summary>
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>Offset baked into the frame, CSS pixels.</summary>
    public float ScrollX { get; init; }
    public float ScrollY { get; init; }
    /// <summary>Document extents, CSS pixels; drives scrollbar geometry.</summary>
    public float ContentWidth { get; init; }
    public float ContentHeight { get; init; }
    /// <summary>Page background, packed RGBA — used to fill freshly exposed strips.</summary>
    public uint PageBgRgba { get; init; } = 0xFFFFFFFF;
    public int DomCount { get; init; }
    public int BoxCount { get; init; }
    /// <summary>Monotonic commit counter; a shell frame that already drew this value skips the fold.</summary>
    public long FrameSeq { get; init; }
    /// <summary>True once the first navigation has produced a usable frame.</summary>
    public bool Navigated { get; init; }
}

public enum TabSignalKind
{
    Title,
    UrlChanged,
    Loading,
    ScrollChanged,
    WheelResult,
    Responsiveness,
    LongTask,
    Crash,
    Metrics,
}

/// <summary>One engine-originated notification. Unused fields carry their default.</summary>
public sealed record TabSignal(TabSignalKind Kind)
{
    public string Text { get; init; } = "";
    public bool Flag { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public int Phase { get; init; }
    public long DurationMs { get; init; }
    public PageMetrics? Metrics { get; init; }
}

/// <summary>Engine-side cost counters, reported on a slow cadence for the task manager.</summary>
public sealed record PageMetrics
{
    public int DomCount { get; init; }
    public int BoxCount { get; init; }
    public long JsHeapKB { get; init; }
    public int TimerCount { get; init; }
    public long WorkingSetKB { get; init; }
    public long LastLayoutMs { get; init; }
    public long LastDisplayListMs { get; init; }
    public long LastRasterMs { get; init; }
    public long LastLongTaskMs { get; init; }
    public int PendingCommands { get; init; }
    public long BytesSent { get; init; }
}

/// <summary>
/// Liveness of one page host as seen by the shell. A host that holds the pipe open
/// while its loop is wedged still degrades Healthy -> Suspect -> Unresponsive.
/// </summary>
public enum PageResponsiveness
{
    Healthy,
    Suspect,
    Unresponsive,
    Dead,
}
