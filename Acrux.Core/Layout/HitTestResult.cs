using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Hit test result. Mirrors hit_test_result.cc.
/// Contains information about what was hit by a point or rect-based hit test.
/// </summary>
public class HitTestResult
{
    public Node? InnerNode { get; private set; }
    public Node? InnerPossiblyPseudoNode { get; private set; }
    public Element? InnerElement { get; private set; }
    public Element? UrlElement { get; private set; }
    public PhysicalOffset LocalPoint { get; private set; }
    public PhysicalOffset PointInInnerNodeFrame { get; private set; }
    public bool IsOverEmbeddedContentView { get; private set; }
    public HitTestRequest Request { get; }
    public HitTestLocation Location { get; }

    public HitTestResult()
    {
        Request = new HitTestRequest();
        Location = new HitTestLocation();
    }

    public HitTestResult(HitTestRequest request, HitTestLocation location)
    {
        Request = request;
        Location = location;
    }

    public void SetNodeAndPosition(Node? node, PhysicalOffset? point = null, PhysicalOffset? localPoint = null)
    {
        InnerNode = node;
        InnerPossiblyPseudoNode = node;
        InnerElement = node as Element;
        if (point.HasValue)
            PointInInnerNodeFrame = point.Value;
        if (localPoint.HasValue)
            LocalPoint = localPoint.Value;
    }

    public void OverrideNodeAndPosition(Node? node, PhysicalOffset? point = null, PhysicalOffset? localPoint = null)
    {
        InnerNode = node;
        InnerPossiblyPseudoNode = node;
        InnerElement = node as Element;
        if (point.HasValue)
            PointInInnerNodeFrame = point.Value;
        if (localPoint.HasValue)
            LocalPoint = localPoint.Value;
    }

    public bool IsSelected => InnerNode != null;

    public void Reset()
    {
        InnerNode = null;
        InnerPossiblyPseudoNode = null;
        InnerElement = null;
        UrlElement = null;
        LocalPoint = PhysicalOffset.Zero;
        PointInInnerNodeFrame = PhysicalOffset.Zero;
        IsOverEmbeddedContentView = false;
    }
}

/// <summary>
/// Hit test request. Mirrors hit_test_request.cc.
/// </summary>
public class HitTestRequest
{
    public HitTestAction Action { get; set; } = HitTestAction.Hit;
    public bool IsMove { get; set; }
    public bool IsRelease { get; set; }
    public bool IsActive { get; set; }
    public bool IsChildFrameHitTest { get; set; }
    public bool IgnoreClipping { get; set; }
    public bool AllowChildFrameContent { get; set; } = true;
}

/// <summary>
/// Hit test location. Mirrors hit_test_location.cc.
/// </summary>
public class HitTestLocation
{
    public PhysicalOffset Point { get; set; }
    public PhysicalRect BoundingBox { get; set; }
    public bool IsRectBased { get; set; }
    public bool IsRectilinear { get; set; } = true;

    public HitTestLocation()
    {
        Point = PhysicalOffset.Zero;
        BoundingBox = PhysicalRect.Zero;
    }

    public HitTestLocation(float x, float y)
    {
        Point = new PhysicalOffset(x, y);
        BoundingBox = new PhysicalRect(x, y, 1, 1);
    }

    public HitTestLocation(PhysicalOffset point)
    {
        Point = point;
        BoundingBox = new PhysicalRect(point, new PhysicalSize(1, 1));
    }

    public HitTestLocation(PhysicalRect rect)
    {
        Point = rect.Offset;
        BoundingBox = rect;
        IsRectBased = true;
    }

    public bool Intersects(PhysicalRect rect) => BoundingBox.Intersects(rect);
    public bool Contains(float x, float y) => BoundingBox.X <= x && x <= BoundingBox.Right && BoundingBox.Y <= y && y <= BoundingBox.Bottom;
}

/// <summary>
/// Hit test action enum.
/// </summary>
public enum HitTestAction
{
    Hit,
    HitTest,
    Scroll,
    Resize,
    Tap,
}

/// <summary>
/// Hit test phase enum. Mirrors hit_test_phase.h.
/// </summary>
public enum HitTestPhase
{
    SelfBlockBackground,
    DescendantBlockBackgrounds,
    Float,
    Foreground,
    Outline,
    ChildBlockBackground,
    ChildBlockBackgrounds,
    Last
}