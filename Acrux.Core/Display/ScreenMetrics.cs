namespace Acrux.Core.Display;

/// <summary>
/// The display the engine reports to a page: one source of truth for
/// <c>window.screen</c> and for the device media features ('device-width', 'device-height',
/// 'device-aspect-ratio'). Those two are the same measurement — Blink answers
/// <c>(device-width: &lt;screen.width&gt;px)</c> with 'true' — so reading them from
/// different places would let a page ask one question twice and get two answers.
/// A mock desktop panel: the shell does not yet feed its real monitor metrics in.
/// </summary>
public static class ScreenMetrics
{
    public const int Width = 1920;
    public const int Height = 1080;
    public const int AvailableWidth = 1920;
    public const int AvailableHeight = 1040;

    /// <summary>Bits per pixel in the display's colour buffer. Note that the CSS 'color' media
    /// feature does not report this: it counts the bits per colour component (8 here), which is
    /// why <c>(color: 8)</c> matches and <c>(color: 24)</c> does not (measured).</summary>
    public const int ColorDepth = 24;

    public const int Top = 0;
    public const int Left = 0;
}
