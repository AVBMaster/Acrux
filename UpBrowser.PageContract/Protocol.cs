namespace UpBrowser.PageContract;

/// <summary>Wire protocol identity shared by the shell and every page-side process.</summary>
public static class PageProtocol
{
    /// <summary>Bumped when the message set or frame layout changes incompatibly.</summary>
    public const int Version = 2;

    /// <summary>Optional feature groups, exchanged in the Hello/Capabilities handshake.</summary>
    [Flags]
    public enum Capabilities : ulong
    {
        None = 0,
        /// <summary>Frames travel through a shared-memory segment instead of the control pipe.</summary>
        ShmFrames = 1UL << 0,
        /// <summary>Windowed dialogs round-trip their result back to the page.</summary>
        BlockingDialogs = 1UL << 1,
        /// <summary>Hit-test / selection / caret queries answer over the control channel.</summary>
        NodeRpc = 1UL << 2,
        /// <summary>DevTools state is served as chunked DTOs.</summary>
        DevToolsRpc = 1UL << 3,
        /// <summary>Text search runs engine-side and returns match rectangles.</summary>
        FindText = 1UL << 4,
        /// <summary>Composition text and caret placement round-trip for CJK input.</summary>
        Ime = 1UL << 5,
        /// <summary>The page host reports its own loop heartbeat; the shell may watchdog it.</summary>
        Heartbeat = 1UL << 6,
    }
}

/// <summary>
/// Control-channel message ids between the shell (parent) and one page host (child).
/// Framing: [int32 payloadLength][byte msgType][payload] — unchanged from v1, so the
/// legacy pipe reader keeps working. RPC pairs carry their correlation id as the
/// first int32 of the payload instead of in the frame header.
/// Values 1-28 are the v1 set and must not be renumbered.
/// </summary>
public enum TabMsg : byte
{
    // ---- shell -> page host ----
    Navigate = 1,         // [string url]
    NavigateHtml = 2,     // [string html][string baseUrl]
    MouseDown = 3,        // [float x][float y] (+v2 [int buttons][int modifiers][int clickCount])
    MouseUp = 4,          // [float x][float y] (+v2 [int buttons][int modifiers][int clickCount])
    MouseMove = 5,        // [float x][float y] (+v2 [int buttons][int modifiers][int clickCount])
    Wheel = 6,            // [float dx][float dy][float x][float y]
    Resize = 7,           // [float w][float h] (+v2 [float dpi][float resolutionScale][byte inSizeMove])
    SetActive = 8,        // [byte active]
    Close = 9,            // []
    ScrollTo = 10,        // [float x][float y]
    KeyDown = 11,         // [uint16 charCode][uint16 key][byte repeat] (+v2 [int modifiers])
    Char = 12,            // [uint16 charCode]

    // ---- page host -> shell ----
    Ready = 20,           // []
    Frame = 21,           // v1 inline-pixel frame — retired: pixels now ride the FrameChannel mapping (see FrameCommit)
    Title = 22,           // [string]
    UrlChanged = 23,      // [string]
    Loading = 24,         // [byte loading]
    Dialog = 25,          // [string message][string type] — v1 fire-and-forget, superseded by DialogRequest
    Dead = 26,            // [string reason]
    ScrollChanged = 27,   // [float x][float y] — engine-initiated scroll
    WheelResult = 28,     // [byte consumed] — reply to Wheel

    // ---- v2 handshake / liveness ----
    /// <summary>C->S [int proto][ulong caps][int pid][string frameName][int frameW][int frameH].
    /// frameName is the current shared-memory frame mapping (a FrameChannel generation, named
    /// from the pipe name + a counter); the child re-sends Hello with a fresh name whenever the
    /// viewport size changes, so Hello doubles as the frame-generation announcement.</summary>
    Hello = 40,
    /// <summary>C->S [ulong seq][long damageBytes] — the FrameChannel slot with sequence seq is
    /// Ready in the announced mapping. damageBytes = sum of damage-rect areas × 4 (the full
    /// viewport for a Full frame); it rides the pipe only so the shell can report
    /// transfer-volume metrics — no pixel bytes cross the pipe anymore.</summary>
    FrameCommit = 43,
    /// <summary>Proof of life: emitted at loop-stage boundaries, so a host whose loop is
    /// wedged simply stops sending it. [long loopSeq][int pendingCommands]</summary>
    Heartbeat = 44,       // C->S
    LongTask = 45,        // C->S [int phase][long durMs][int pendingCommands]
    CrashReport = 46,     // C->S [string kind][string detail]
    MetricsReport = 47,   // C->S [see PageMetrics]

    // ---- v2 request / response RPC ----
    DialogRequest = 48,   // C->S [int kind][string message][string defaultValue]
    DialogResult = 49,    // S->C [byte accepted][string text]
    NodeInfoRequest = 50, // S->C [float x][float y]
    NodeInfoResponse = 51,// C->S [see NodeHitInfo]
    SelectedTextRequest = 52,  // S->C []
    SelectedTextResponse = 53, // C->S [string text]
    CaretRectRequest = 54,     // S->C []
    CaretRectResponse = 55,    // C->S [float x][float y][float h][byte visible]
    ImeUpdate = 56,       // S->C [string text][int selStart][int selLen]
    ImeCommit = 57,       // S->C [string text]
    ImeCancel = 58,       // S->C []
    FindRequest = 59,     // S->C [string query][byte caseSensitive][byte forward][int activeIndex]
    FindResponse = 60,    // C->S [int total][int active][int rectCount]{rect}
    DtNodesRequest = 61,  // S->C [int nodeId][int depth][int limit]
    DtNodesResponse = 62, // C->S [long mutationVersion][int nodeCount]{node}
    DtStylesRequest = 63, // S->C [int nodeId]
    DtStylesResponse = 64,// C->S [see DtStyleBatch]
    DtEvalRequest = 65,   // S->C [string script]
    DtEvalResponse = 66,  // C->S [byte isError][string text]
    SelectAll = 67,       // S->C - select every run of the page, as Ctrl+A does
}

/// <summary>How a published frame describes its pixel delta.</summary>
public enum FrameMode : byte
{
    /// <summary>Full viewport pixels in [0..w)x[0..h).</summary>
    Full = 0,
    /// <summary>Row band [y..y+rh) x full width — pixel-diffed damage.</summary>
    DamageRows = 1,
    /// <summary>Scroll-only frame: the consumer shifts its cached bitmap by the delta
    /// between the new and previous frame scroll, then overlays rows [y..y+rh).</summary>
    ScrollBlit = 2,
}
