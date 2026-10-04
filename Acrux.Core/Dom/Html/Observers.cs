namespace Acrux.Core.Dom.Html;

public class IntersectionObserver
{
    private Element? _root;
    private string _rootMargin;
    private double[] _thresholds;

    public IntersectionObserver(IntersectionObserverCallback callback, IntersectionObserverInit? options = null)
    {
        Callback = callback;
        _root = options?.Root;
        _rootMargin = options?.RootMargin ?? "0px";
        _thresholds = options?.Threshold ?? new[] { 0d };
    }

    public delegate void IntersectionObserverCallback(IntersectionObserverEntry[] entries, IntersectionObserver observer);
    public event IntersectionObserverCallback? Callback;

    public Element? Root => _root;
    public string RootMargin => _rootMargin;
    public double[] Thresholds => _thresholds;

    public void Observe(Element target) { }
    public void Unobserve(Element target) { }
    public void Disconnect() { }
    public IntersectionObserverEntry[] TakeRecords() => Array.Empty<IntersectionObserverEntry>();
}

public class IntersectionObserverInit
{
    public Element? Root { get; set; }
    public string RootMargin { get; set; } = "0px";
    public double[]? Threshold { get; set; }
}

public class IntersectionObserverEntry
{
    public double Time { get; }
    public DOMRectReadOnly? BoundingClientRect { get; }
    public DOMRectReadOnly? IntersectionRect { get; }
    public DOMRectReadOnly? RootBounds { get; }
    public Element? Target { get; }
    public double IntersectionRatio { get; }
    public bool IsIntersecting { get; }
    public bool IsVisible { get; }
}

public class ResizeObserver
{
    private ResizeObserverCallback? _callback;

    public delegate void ResizeObserverCallback(ResizeObserverEntry[] entries, ResizeObserver observer);

    public ResizeObserver(ResizeObserverCallback callback)
    {
        _callback = callback;
    }

    public void Observe(Element target, ResizeObserverOptions? options = null) { }
    public void Unobserve(Element target) { }
    public void Disconnect() { }
}

public class ResizeObserverOptions
{
    public string Box { get; set; } = "content-box";
}

public class ResizeObserverEntry
{
    public Element? Target { get; }
    public DOMRectReadOnly? ContentRect { get; }
    public ResizeObserverSize[] ContentBoxSize { get; } = Array.Empty<ResizeObserverSize>();
    public ResizeObserverSize[] BorderBoxSize { get; } = Array.Empty<ResizeObserverSize>();
    public ResizeObserverSize[] DevicePixelContentBoxSize { get; } = Array.Empty<ResizeObserverSize>();
}

public class ResizeObserverSize
{
    public double InlineSize { get; }
    public double BlockSize { get; }
}


