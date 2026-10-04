namespace Acrux.Core.Dom;

public class ScrollIntoViewOptions
{
    public ScrollLogicalPosition Block { get; set; } = ScrollLogicalPosition.Start;
    public ScrollLogicalPosition Inline { get; set; } = ScrollLogicalPosition.Nearest;
    public ScrollBehavior Behavior { get; set; } = ScrollBehavior.Auto;
}

public enum ScrollLogicalPosition
{
    Start,
    Center,
    End,
    Nearest
}

public enum ScrollBehavior
{
    Auto,
    Smooth,
    Instant
}

public class ScrollToOptions
{
    public double Left { get; set; }
    public double Top { get; set; }
    public ScrollBehavior Behavior { get; set; } = ScrollBehavior.Auto;
}

public class CheckVisibilityOptions
{
    public bool CheckOpacity { get; set; } = true;
    public bool CheckVisibilityCSS { get; set; } = true;
    public bool ContentVisibilityAuto { get; set; }
    public bool OpacityProperty { get; set; }
    public bool VisibilityProperty { get; set; }
}
