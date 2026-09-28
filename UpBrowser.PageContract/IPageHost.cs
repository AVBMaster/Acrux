namespace UpBrowser.PageContract;

/// <summary>
/// The whole surface a browser shell is allowed to see of a page. One implementation
/// drives the engine in-process, another marshals over IPC to a page-host process, so
/// the shell holds no document, layout box or script engine and cannot be frozen by
/// any of them.
///
/// Contract that both implementations honour:
///  - Every method is non-blocking. Commands are queued; requests answer on the
///    <see cref="Signal"/>/callback they specify, never on the calling stack.
///  - Callbacks may arrive on any thread except the shell's paint pass, and may never
///    arrive at all if the page host dies — callers must tolerate a missing answer.
///  - The last committed frame stays valid forever, so a wedged page keeps painting
///    what it last showed instead of going blank.
/// </summary>
public interface IPageHost : IDisposable
{
    // ---- lifecycle ----

    void Navigate(string url);
    void LoadHtml(string html, string? baseUrl);
    /// <summary>Viewport size in CSS pixels. <paramref name="inSizeMove"/> marks a live resize drag,
    /// which the host answers with a cheaper partial relayout instead of deferring.</summary>
    void SetViewport(float width, float height, float dpiScale, float resolutionScale, bool inSizeMove);
    void SetActive(bool active);

    // ---- input ----

    void SendPointer(PointerPacket pointer);
    void SendWheel(WheelPacket wheel);
    void SendKey(KeyPacket key);
    void SendChar(char ch);
    void SendIme(ImePacket ime);
    /// <summary>Window-level scroll owned by the shell (its scrollbar), pushed to the host.</summary>
    void ScrollRootTo(float x, float y);

    // ---- requests ----

    void RequestNodeInfo(float x, float y, Action<NodeHitInfo> done);
    void RequestSelectedText(Action<string> done);
    void RequestCaretRect(Action<CaretRect> done);
    void RequestFind(FindOptions options, Action<FindResult> done);

    // ---- state ----

    /// <summary>Metadata of the newest committed frame. Never null.</summary>
    PageFrameState Current { get; }
    PageResponsiveness Responsiveness { get; }
    string Url { get; }
    string PageTitle { get; }
    bool IsLoading { get; }
    bool IsConnected { get; }
    int ProcessId { get; }

    /// <summary>Raised when a new frame is folded in, on the thread that received it.</summary>
    event Action? FrameArrived;
    event Action<TabSignal>? Signal;
    /// <summary>Answered on the host thread; the shell replies through <see cref="DialogRequest.Complete"/>.</summary>
    event Action<DialogRequest>? DialogRequested;
    IDevToolsChannel DevTools { get; }
}
