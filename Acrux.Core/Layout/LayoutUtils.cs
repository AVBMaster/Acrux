using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Layout utility functions. Mirrors layout_utils.cc.
/// </summary>
public enum LayoutCacheStatus
{
    Hit,
    NeedsLayout,
    NeedsSimplifiedLayout,
    CanReuseLines,
}

public static class LayoutUtils
{
    public static LayoutCacheStatus CalculateSizeBasedLayoutCacheStatus(
        LayoutResult? cachedResult, ConstraintSpace newSpace)
    {
        if (cachedResult == null) return LayoutCacheStatus.NeedsLayout;

        float cachedWidth = cachedResult.Fragment?.InlineSize ?? 0;
        float cachedHeight = cachedResult.Fragment?.BlockSize ?? 0;

        bool sameInline = Math.Abs(cachedWidth - newSpace.AvailableInlineSize) < 0.01f;
        bool sameBlock = Math.Abs(cachedHeight - newSpace.AvailableBlockSize) < 0.01f;

        if (sameInline && sameBlock)
            return LayoutCacheStatus.Hit;
        if (sameInline)
            return LayoutCacheStatus.NeedsSimplifiedLayout;
        return LayoutCacheStatus.NeedsLayout;
    }

    public static bool MaySkipLayoutWithinBlockFormattingContext(
        LayoutResult? cachedResult, ConstraintSpace newSpace)
    {
        if (cachedResult == null) return false;
        return true;
    }
}

/// <summary>
/// Pagination utilities. Mirrors pagination_utils.cc.
/// </summary>
public static class PaginationUtils
{
    public static LogicalSize DesiredPageContainingBlockSize(float width, float height)
    {
        return new LogicalSize(width, height);
    }

    public static (BoxStrut margins, LogicalSize size) ResolvePageBoxGeometry(LogicalSize pageSize)
    {
        return (BoxStrut.Zero, pageSize);
    }

    public static PhysicalSize CalculateInitialContainingBlockSizeForPagination(float viewportWidth, float viewportHeight)
    {
        return new PhysicalSize(viewportWidth, viewportHeight);
    }

    public static float TargetScaleForPage(float viewportWidth, float pageWidth)
    {
        if (pageWidth <= 0 || viewportWidth <= 0) return 1;
        return Math.Min(1, pageWidth / viewportWidth);
    }
}

/// <summary>
/// Anchor positioning utilities. Mirrors anchor_evaluator_impl.cc.
/// </summary>
public static class AnchorUtils
{
    public static (float inlineOffset, float blockOffset)? ComputeAnchorCenterPosition(
        ComputedStyle style, LogicalSize availableSize)
    {
        return null;
    }
}

/// <summary>
/// Anchor scroll data. Mirrors anchor_position_scroll_data.cc.
/// </summary>
public class AnchorPositionScrollData
{
    public float ScrollX { get; set; }
    public float ScrollY { get; set; }
    public bool HasScrollData => Math.Abs(ScrollX) > 0.01f || Math.Abs(ScrollY) > 0.01f;
}

/// <summary>
/// Anchor visibility observer. Mirrors anchor_position_visibility_observer.cc.
/// </summary>
public class AnchorPositionVisibilityObserver
{
    public bool IsVisible { get; private set; } = true;
    public void UpdateVisibility(bool visible) => IsVisible = visible;
}

/// <summary>
/// Anchor query map. Mirrors anchor_query_map.cc.
/// </summary>
public class AnchorQueryMap
{
    private readonly Dictionary<string, Element?> _anchors = new();

    public void RegisterAnchor(string name, Element? element)
    {
        _anchors[name] = element;
    }

    public Element? FindAnchor(string name)
    {
        return _anchors.GetValueOrDefault(name);
    }

    public void Clear() => _anchors.Clear();
}