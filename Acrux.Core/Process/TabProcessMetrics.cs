namespace Acrux.Core.Process;

public class TabProcessMetrics
{
    public int TabIndex { get; set; } = -1;
    public string Title { get; set; } = "New Tab";
    public string Url { get; set; } = "";
    public string Status { get; set; } = "New";
    public long ThreadId { get; set; }
    public long MemoryBytes { get; set; }
    public int DomNodeCount { get; set; }
    public int LayoutBoxCount { get; set; }
    public int JsHeapSizeKB { get; set; }
    public int JsTimerCount { get; set; }
    public double StyleMs { get; set; }
    public double LayoutMs { get; set; }
    public double PaintMs { get; set; }
    public double ScriptMs { get; set; }
}
