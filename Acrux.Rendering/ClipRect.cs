using SkiaSharp;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Rendering;

/// <summary>
/// A floating-point clip rect with radius/tightness tracking.
/// Mirrors FloatClipRect in float_clip_rect.h.
/// FloatClipRect() is infinite by default (no clip at all).
/// </summary>
public class FloatClipRect
{
    public SKRect Rect { get; private set; }
    public bool HasRadius { get; private set; }
    public bool IsTight { get; private set; }
    public bool IsInfinite { get; private set; }

    public FloatClipRect()
    {
        Rect = InfiniteSKRect();
        HasRadius = false;
        IsTight = true;
        IsInfinite = true;
    }

    public FloatClipRect(SKRect rect)
    {
        SetRect(rect);
    }

    public FloatClipRect(FloatRoundedRect rounded)
    {
        Rect = rounded.Rect;
        HasRadius = rounded.IsRounded;
        IsTight = !rounded.IsRounded;
        IsInfinite = false;
    }

    public void SetRect(SKRect rect)
    {
        Rect = rect;
        HasRadius = false;
        IsTight = true;
        IsInfinite = false;
    }

    public void Intersect(FloatClipRect other)
    {
        if (other.IsInfinite)
            return;

        if (IsInfinite)
        {
            IsInfinite = other.IsInfinite;
            Rect = other.Rect;
        }
        else
        {
            Rect = SKRect.Intersect(Rect, other.Rect);
        }

        if (other.HasRadius)
            SetHasRadius();
        else if (!other.IsTight)
            ClearIsTight();
    }

    public bool InclusiveIntersect(FloatClipRect other)
    {
        if (other.IsInfinite)
            return true;

        bool retval = true;
        if (IsInfinite)
        {
            IsInfinite = other.IsInfinite;
            Rect = other.Rect;
        }
        else
        {
            retval = Rect.IntersectsWithInclusive(other.Rect);
            if (retval)
                Rect = SKRect.Intersect(Rect, other.Rect);
        }

        if (other.HasRadius)
            SetHasRadius();
        else if (!other.IsTight)
            ClearIsTight();

        return retval;
    }

    public void SetHasRadius()
    {
        HasRadius = true;
        IsTight = false;
        IsInfinite = false;
    }

    public void ClearIsTight()
    {
        IsTight = false;
    }

    public void Move(float dx, float dy)
    {
        if (IsInfinite) return;
        Rect = new SKRect(Rect.Left + dx, Rect.Top + dy, Rect.Right + dx, Rect.Bottom + dy);
    }

    public void Map(SKMatrix matrix)
    {
        if (matrix.IsIdentity)
            return;
        if (matrix.TransX != 0 || matrix.TransY != 0)
        {
            Move(matrix.TransX, matrix.TransY);
            if (matrix.IsIdentity)
                return;
        }
        IsTight = false;
        if (IsInfinite) return;
        var points = new[] { new SKPoint(Rect.Left, Rect.Top), new SKPoint(Rect.Right, Rect.Bottom) };
        matrix.MapPoints(points);
        Rect = new SKRect(points[0].X, points[0].Y, points[1].X, points[1].Y);
    }

    public static bool operator ==(FloatClipRect a, FloatClipRect b)
    {
        if (a.IsTight != b.IsTight) return false;
        if (a.IsInfinite && b.IsInfinite) return true;
        return !a.IsInfinite && !b.IsInfinite && a.HasRadius == b.HasRadius && a.Rect == b.Rect;
    }

    public static bool operator !=(FloatClipRect a, FloatClipRect b) => !(a == b);

    public override bool Equals(object? obj) => obj is FloatClipRect f && this == f;
    public override int GetHashCode() => HashCode.Combine(Rect, HasRadius, IsTight, IsInfinite);

    public static FloatClipRect InfiniteLoose()
    {
        var rect = new FloatClipRect();
        rect.ClearIsTight();
        return rect;
    }

    internal static SKRect InfiniteSKRect() => new(float.MinValue / 2, float.MinValue / 2, float.MaxValue / 2, float.MaxValue / 2);
}

/// <summary>
/// A layout-pixel clip rect with radius tracking.
/// Mirrors ClipRect in clip_rect.h.
/// ClipRect() is infinite by default (no clip at all).
/// </summary>
public struct ClipRect
{
    public PhysicalRect Rect { get; private set; }
    public bool HasRadius { get; set; }
    public bool IsInfinite { get; private set; }

    public ClipRect(PhysicalRect rect)
    {
        Rect = rect;
        HasRadius = false;
        IsInfinite = false;
    }

    public ClipRect(FloatClipRect rect)
    {
        HasRadius = rect.HasRadius;
        IsInfinite = rect.IsInfinite;
        Rect = new PhysicalRect(rect.Rect.Left, rect.Rect.Top, rect.Rect.Width, rect.Rect.Height);
    }

    public void SetRect(PhysicalRect rect)
    {
        Rect = rect;
        HasRadius = false;
        IsInfinite = false;
    }

    public void SetRect(FloatClipRect rect)
    {
        HasRadius = rect.HasRadius;
        IsInfinite = rect.IsInfinite;
        Rect = new PhysicalRect(rect.Rect.Left, rect.Rect.Top, rect.Rect.Width, rect.Rect.Height);
    }

    public bool IsEmpty => Rect.Width <= 0 || Rect.Height <= 0;

    public void Intersect(PhysicalRect other)
    {
        if (IsInfinite)
        {
            Rect = other;
            IsInfinite = false;
        }
        else
        {
            Rect = Rect.Intersect(other);
        }
    }

    public void Intersect(ClipRect other)
    {
        if (other.IsInfinite)
            return;
        Intersect(other.Rect);
        if (other.HasRadius)
            HasRadius = true;
    }

    public void Move(PhysicalOffset offset)
    {
        Rect = new PhysicalRect(Rect.X + offset.Left, Rect.Y + offset.Top, Rect.Width, Rect.Height);
    }

    public void Reset()
    {
        if (IsInfinite) return;
        HasRadius = true;
        IsInfinite = true;
        Rect = new PhysicalRect(float.MinValue / 2, float.MinValue / 2, float.MaxValue / 2, float.MaxValue / 2);
    }

    public static bool operator ==(ClipRect a, ClipRect b) =>
        a.Rect.X == b.Rect.X && a.Rect.Y == b.Rect.Y &&
        a.Rect.Width == b.Rect.Width && a.Rect.Height == b.Rect.Height &&
        a.HasRadius == b.HasRadius && a.IsInfinite == b.IsInfinite;

    public static bool operator !=(ClipRect a, ClipRect b) => !(a == b);

    public static bool operator ==(ClipRect a, PhysicalRect otherRect) => !(a != otherRect);

    public static bool operator !=(ClipRect a, PhysicalRect otherRect) =>
        a.Rect.X != otherRect.X || a.Rect.Y != otherRect.Y ||
        a.Rect.Width != otherRect.Width || a.Rect.Height != otherRect.Height;

    public override bool Equals(object? obj) => obj is ClipRect c && this == c;
    public override int GetHashCode() => HashCode.Combine(Rect.X, Rect.Y, Rect.Width, Rect.Height, HasRadius, IsInfinite);

    public override string ToString() =>
        $"{Rect}{(HasRadius ? " hasRadius" : "")}{(IsInfinite ? " isInfinite" : "")}";
}

public static class ClipRectExtensions
{
    public static ClipRect Intersection(ClipRect a, ClipRect b)
    {
        var c = a;
        c.Intersect(b);
        return c;
    }

    /// <summary>True if the rects intersect (inclusive of edges touching).</summary>
    public static bool IntersectsWithInclusive(this SKRect a, SKRect b) =>
        a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;
}