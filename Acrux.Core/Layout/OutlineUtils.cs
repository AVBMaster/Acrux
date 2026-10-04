using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Collects outline rects. Mirrors outline_rect_collector.cc.
/// </summary>
public abstract class OutlineRectCollector
{
    public enum Type { Union, Vector }

    public abstract Type GetType();
    public abstract void AddRect(PhysicalRect rect);
    public abstract OutlineRectCollector ForDescendantCollector();
    public abstract void Combine(OutlineRectCollector collector, PhysicalOffset additionalOffset);
    public abstract bool IsEmpty { get; }
}

public class UnionOutlineRectCollector : OutlineRectCollector
{
    private PhysicalRect _rect;

    public PhysicalRect Rect => _rect;

    public override Type GetType() => Type.Union;

    public override void AddRect(PhysicalRect rect) => _rect = _rect.Union(rect);

    public override OutlineRectCollector ForDescendantCollector() => new UnionOutlineRectCollector();

    public override void Combine(OutlineRectCollector collector, PhysicalOffset additionalOffset)
    {
        if (collector is UnionOutlineRectCollector unionCollector)
        {
            var r = unionCollector.Rect;
            if (r.Width > 0 || r.Height > 0)
                AddRect(new PhysicalRect(r.X + additionalOffset.Left, r.Y + additionalOffset.Top, r.Width, r.Height));
        }
    }

    public override bool IsEmpty => _rect.Width <= 0 && _rect.Height <= 0;
}

/// <summary>
/// Outline utilities. Mirrors outline_utils.cc.
/// </summary>
public static class OutlineUtils
{
    public static bool ShouldPaintOutline(ComputedStyle style)
    {
        return style.OutlineWidth > 0 && style.OutlineStyle != BorderStyle.None;
    }

    public static PhysicalRect ComputeOutlineRect(PhysicalRect borderBox, float outlineWidth, float outlineOffset)
    {
        float inset = outlineOffset;
        float outset = outlineWidth + outlineOffset;
        return new PhysicalRect(
            borderBox.X - outset,
            borderBox.Y - outset,
            borderBox.Width + outset * 2,
            borderBox.Height + outset * 2);
    }
}