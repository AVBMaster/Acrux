namespace UpBrowser.PageHost;

/// <summary>
/// Engine → host notification surface: the handful of things a page engine cannot do
/// for itself because they belong to whoever owns it. A shell that runs the engine in
/// a child process marshals every one of these over its control channel; an in-process
/// host answers them directly. Navigation and page-content requests may arrive on a
/// worker thread (document fetches run off the engine loop) and must be queued, not
/// executed; everything else arrives on the thread the engine loop runs on.
/// </summary>
public interface IPageEngineSink
{
    // ---- requests the host must schedule on the engine loop ----

    /// <summary>The page asked to navigate (link click is handled inline; this is
    /// <c>location</c> writes and reload) — the host re-enters <see cref="PageEngine.Navigate"/> later.</summary>
    void RequestNavigate(string url);

    /// <summary>A fetched document is ready; the host schedules <see cref="PageEngine.LoadHtml"/>
    /// so DOM work lands back on the engine loop thread.</summary>
    void RequestLoadHtml(string html, string baseUrl);

    // ---- page state the host forwards to its own listeners ----

    void ReportTitle(string title);
    void ReportUrl(string url);
    void ReportLoading(bool loading);

    /// <summary>
    /// <c>alert</c>/<c>confirm</c>/<c>prompt</c>: called on the thread that runs page
    /// scripts, and must not return until the user answered, because script semantics
    /// are synchronous. Returning null means the answer will never arrive (the host is
    /// going away), which callers treat as a dismissed dialog.
    /// <paramref name="type"/> is "alert", "confirm" or "prompt:&lt;default&gt;".
    /// </summary>
    string? RequestDialog(string message, string type);

    /// <summary>A child-initiated scroll happened; report it so the host's scrollbar follows.</summary>
    void ReportScrollChanged(float x, float y);

    /// <summary>Answer to a wheel event: whether an element scroller consumed it.</summary>
    void ReportWheelConsumed(bool consumed);

    // ---- frame production ----

    /// <summary>Something changed downstream of the display list: a frame is wanted.</summary>
    void RequestFrame();

    /// <summary>A new document replaced the old one: whatever pixel baseline the host
    /// diffs against no longer describes this page, so the next frame must ship whole.</summary>
    void InvalidateFrameBaseline();

    // ---- diagnostics ----

    /// <summary>Stack trace of whoever marked the engine dirty (only captured when the
    /// host asked for engine statistics).</summary>
    void ReportDirtyTrace(string trace);
}
