using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Represents ink overflow for layout objects. Mirrors ink_overflow.cc.
/// Tracks self overflow (shadows, outlines) and contents overflow (child overflow).
/// </summary>
public class InkOverflow
{
    public enum Type
    {
        NotSet,
        Invalidated,
        None,
        Self,
        Contents,
        SelfAndContents
    }

    public PhysicalRect SelfRect { get; private set; }
    public PhysicalRect ContentsRect { get; private set; }
    public Type CurrentType { get; private set; } = Type.NotSet;

    public void SetSelf(PhysicalRect rect, PhysicalSize size)
    {
        SelfRect = rect;
        CurrentType = (rect.Width <= 0 || rect.Height <= 0) ? Type.None : Type.Self;
    }

    public void SetContents(PhysicalRect rect, PhysicalSize size)
    {
        ContentsRect = rect;
        CurrentType = CurrentType == Type.Self ? Type.SelfAndContents : Type.Contents;
    }

    public void SetBoth(PhysicalRect self, PhysicalRect contents, PhysicalSize size)
    {
        SelfRect = self;
        ContentsRect = contents;
        CurrentType = Type.SelfAndContents;
    }

    public PhysicalRect Self(PhysicalSize size)
    {
        if (CurrentType == Type.None || CurrentType == Type.NotSet)
            return new PhysicalRect(PhysicalOffset.Zero, size);
        return SelfRect;
    }

    public PhysicalRect Contents(PhysicalSize size)
    {
        return CurrentType switch
        {
            Type.Contents or Type.SelfAndContents => ContentsRect,
            Type.None or Type.NotSet => new PhysicalRect(PhysicalOffset.Zero, size),
            _ => SelfRect
        };
    }

    public PhysicalRect SelfAndContents(PhysicalSize size)
    {
        if (CurrentType == Type.None || CurrentType == Type.NotSet)
            return new PhysicalRect(PhysicalOffset.Zero, size);
        if (CurrentType == Type.SelfAndContents || CurrentType == Type.Contents)
            return SelfRect.Union(ContentsRect);
        return SelfRect;
    }

    public void Reset() => CurrentType = Type.None;
    public void Invalidate() => CurrentType = Type.Invalidated;
}

/// <summary>
/// Calculates scrollable overflow for a box. Mirrors scrollable_overflow_calculator.cc.
/// </summary>
public static class ScrollableOverflowCalculator
{
    public static PhysicalRect ComputeScrollableOverflow(PhysicalRect contentBoxRect, PhysicalRect childOverflow)
    {
        return contentBoxRect.Union(childOverflow);
    }

    public static PhysicalRect ComputeContentsOverflow(PhysicalRect borderBoxRect, PhysicalRect scrollableOverflow)
    {
        return scrollableOverflow.Union(borderBoxRect);
    }
}

/// <summary>
/// Overflow model for a layout box. Mirrors overflow_model.h.
/// </summary>
public class OverflowModel
{
    public InkOverflow InkOverflow { get; } = new();
    public PhysicalRect ScrollableOverflow { get; set; }
    public PhysicalRect VisualOverflow { get; set; }

    public void SetScrollableOverflow(PhysicalRect overflow)
    {
        ScrollableOverflow = overflow;
    }

    public void SetVisualOverflow(PhysicalRect overflow)
    {
        VisualOverflow = overflow;
    }
}