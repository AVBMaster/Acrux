namespace Acrux.Core.Layout;

/// <summary>
/// Result of hit-testing a canvas element. Mirrors HitTestCanvasResult.
/// </summary>
public class HitTestCanvasResult
{
    public string Id { get; }
    public Dom.Element? Control { get; }

    public HitTestCanvasResult(string id, Dom.Element? control)
    {
        Id = id;
        Control = control;
    }
}