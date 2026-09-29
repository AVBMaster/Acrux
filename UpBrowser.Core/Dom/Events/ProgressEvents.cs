namespace UpBrowser.Core.Dom;

public class ProgressEvent : Event
{
    public bool LengthComputable { get; }
    public ulong Loaded { get; }
    public ulong Total { get; }

    public ProgressEvent(string type, ProgressEventInit? init = null) : base(type, init)
    {
        LengthComputable = init?.LengthComputable ?? false;
        Loaded = init?.Loaded ?? 0;
        Total = init?.Total ?? 0;
    }
}

public class ProgressEventInit : EventInit
{
    public bool LengthComputable { get; set; }
    public ulong Loaded { get; set; }
    public ulong Total { get; set; }
}
