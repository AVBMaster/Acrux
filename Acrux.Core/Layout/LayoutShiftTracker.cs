namespace Acrux.Core.Layout;

/// <summary>
/// Selection state for layout objects. Mirrors selection_state.h.
/// Represents how a LayoutObject is selected for painting/invalidation.
/// </summary>
public enum SelectionState
{
    None,
    Start,
    Inside,
    End,
    StartAndEnd,
    Contain
}

/// <summary>
/// Layout shift region for CLS tracking. Mirrors layout_shift_region.cc.
/// Uses sweep line algorithm for O(n log n) area computation.
/// </summary>
public class LayoutShiftRegion
{
    private readonly List<Rect> _rects = new();

    public void AddRect(float x, float y, float width, float height)
    {
        _rects.Add(new Rect { X = x, Y = y, Width = width, Height = height });
    }

    public void Reset() => _rects.Clear();

    public double ComputeArea()
    {
        if (_rects.Count == 0) return 0;
        if (_rects.Count == 1) return _rects[0].Width * _rects[0].Height;

        // Simple union area for now (sweep line would be more efficient)
        var events = new List<(float x, bool isStart, float y1, float y2)>();
        foreach (var r in _rects)
        {
            events.Add((r.X, true, r.Y, r.Y + r.Height));
            events.Add((r.X + r.Width, false, r.Y, r.Y + r.Height));
        }
        events.Sort((a, b) => a.x.CompareTo(b.x));

        double area = 0;
        float prevX = 0;
        var active = new List<(float y1, float y2)>();

        foreach (var (x, isStart, y1, y2) in events)
        {
            if (active.Count > 0 && x > prevX)
            {
                float totalY = ComputeActiveYLength(active);
                area += (x - prevX) * totalY;
            }
            if (isStart)
                active.Add((y1, y2));
            else
                active.Remove((y1, y2));
            prevX = x;
        }
        return area;
    }

    private static float ComputeActiveYLength(List<(float y1, float y2)> active)
    {
        if (active.Count == 0) return 0;
        var sorted = active.OrderBy(a => a.y1).ToList();
        float total = 0;
        float curY1 = sorted[0].y1, curY2 = sorted[0].y2;
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].y1 <= curY2)
                curY2 = Math.Max(curY2, sorted[i].y2);
            else
            {
                total += curY2 - curY1;
                curY1 = sorted[i].y1;
                curY2 = sorted[i].y2;
            }
        }
        total += curY2 - curY1;
        return total;
    }

    private struct Rect
    {
        public float X, Y, Width, Height;
    }
}

/// <summary>
/// Layout shift tracker for CLS (Cumulative Layout Shift) measurement.
/// Mirrors layout_shift_tracker.cc.
/// </summary>
public class LayoutShiftTracker
{
    private readonly LayoutShiftRegion _region = new();
    private double _cumulativeScore;

    public double CumulativeScore => _cumulativeScore;

    public void AddShift(float x, float y, float width, float height, double shiftScore)
    {
        _region.AddRect(x, y, width, height);
        _cumulativeScore += shiftScore;
    }

    public void Reset()
    {
        _region.Reset();
        _cumulativeScore = 0;
    }
}