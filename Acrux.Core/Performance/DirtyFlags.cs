using Acrux.Core.Dom;

namespace Acrux.Core.Performance;

/// <summary>
/// Bitfield describing which parts of an element are stale and need re-work in the
/// rendering pipeline. Stored on each <see cref="Element"/> via extension methods to
/// avoid touching the hot DOM class with extra fields. Mirrors Blink's <c>needsLayout</c>,
/// <c>needsPaint</c> etc. flags but kept intentionally simple.
/// </summary>
[Flags]
public enum DirtyFlags : ushort
{
    None            = 0,
    Style           = 1 << 0,   // ComputedStyle is stale
    Layout          = 1 << 1,   // Box geometry is stale
    Position        = 1 << 2,   // Position changed but size did not
    Paint           = 1 << 3,   // Visual representation is stale
    Layer           = 1 << 4,   // Stacking-context / compositing layer membership
    TextShape       = 1 << 5,   // Text wrapping / run layout is stale
    Image           = 1 << 6,   // Decoded image resource is stale
    ChildrenStyle   = 1 << 7,   // Inherited style may have changed for descendants
    ChildrenLayout  = 1 << 8,   // Layout children may have changed
    Overflow        = 1 << 9,   // Overflow region may have changed (scroll container)
    Resource        = 1 << 10,  // External resource (img, iframe) availability changed

    // Convenience aggregates
    AllStyle        = Style | ChildrenStyle,
    AllLayout       = Layout | Position | ChildrenLayout | TextShape | Overflow,
    AllPaint        = Paint,
    All             = Style | Layout | Position | Paint | Layer | TextShape |
                     Image | ChildrenStyle | ChildrenLayout | Overflow | Resource,
}

/// <summary>
/// Strongly typed set of dirty flags with structural sharing semantics. Used to
/// accumulate propagation across a tree without mutating each node's underlying
/// state until it is processed.
/// </summary>
public readonly struct DirtySet
{
    public DirtyFlags Flags { get; }
    public DirtySet(DirtyFlags flags) { Flags = flags; }
    public static readonly DirtySet Empty = new(DirtyFlags.None);

    public bool IsEmpty => Flags == DirtyFlags.None;
    public bool Has(DirtyFlags f) => (Flags & f) == f;
    public bool HasAny(DirtyFlags f) => (Flags & f) != 0;

    public static DirtySet Of(DirtyFlags f) => new(f);

    public DirtySet Union(DirtySet other) => new(Flags | other.Flags);
    public DirtySet Union(DirtyFlags f) => new(Flags | f);

    public static DirtySet operator |(DirtySet a, DirtySet b) => a.Union(b);
    public static DirtySet operator |(DirtySet a, DirtyFlags b) => new(a.Flags | b);

    public override string ToString() => Flags.ToString();
}

/// <summary>
/// Extension methods that attach dirty-state to <see cref="Element"/> without modifying
/// the type itself. We use a ConditionalWeakTable-like sidecar: a global dictionary keyed
/// by the element, with weak references. The sidecar is allocated lazily.
/// </summary>
public static class DirtyState
{
    public sealed class NodeState
    {
        public DirtyFlags Self = DirtyFlags.None;
        public DirtyFlags Children = DirtyFlags.None;
        public int SubtreeElementCount;
        public long LayoutVersion;
        public long StyleVersion;
    }

    // Element is the key, and we keep the entry's value alive while the element is alive.
    // System.Runtime.CompilerServices.ConditionalWeakTable is the perfect fit.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Element, NodeState> _table = new();

    public static NodeState Get(Element element)
    {
        if (element == null) return new NodeState();
        if (_table.TryGetValue(element, out var state)) return state;
        state = new NodeState();
        _table.Add(element, state);
        return state;
    }

    public static bool TryGet(Element element, out NodeState? state)
    {
        if (_table.TryGetValue(element, out var s))
        {
            state = s;
            return true;
        }
        state = null;
        return false;
    }

    public static DirtyFlags GetSelf(Element element) =>
        TryGet(element, out var s) ? s!.Self : DirtyFlags.None;

    public static DirtyFlags GetChildren(Element element) =>
        TryGet(element, out var s) ? s!.Children : DirtyFlags.None;

    public static DirtySet GetAll(Element element)
    {
        if (!TryGet(element, out var s) || s is null) return DirtySet.Empty;
        return new DirtySet(s.Self | s.Children);
    }

    public static bool IsClean(Element element)
    {
        if (!TryGet(element, out var s) || s is null) return true;
        return s.Self == DirtyFlags.None && s.Children == DirtyFlags.None;
    }

    public static void SetSelf(Element element, DirtyFlags flags)
    {
        Get(element).Self = flags;
    }

    public static void SetChildren(Element element, DirtyFlags flags)
    {
        Get(element).Children = flags;
    }

    public static void AddSelf(Element element, DirtyFlags flags)
    {
        var s = Get(element);
        s.Self |= flags;
    }

    public static void AddChildren(Element element, DirtyFlags flags)
    {
        var s = Get(element);
        s.Children |= flags;
    }

    public static void ClearAll(Element element)
    {
        if (TryGet(element, out var s) && s is not null)
        {
            s.Self = DirtyFlags.None;
            s.Children = DirtyFlags.None;
        }
    }

    public static void ClearSelf(Element element)
    {
        if (TryGet(element, out var s) && s is not null)
        {
            s.Self = DirtyFlags.None;
        }
    }

    public static void ClearChildren(Element element)
    {
        if (TryGet(element, out var s) && s is not null)
        {
            s.Children = DirtyFlags.None;
        }
    }

    public static long BumpLayoutVersion(Element element) =>
        Get(element).LayoutVersion = unchecked(Get(element).LayoutVersion + 1);

    public static long BumpStyleVersion(Element element) =>
        Get(element).StyleVersion = unchecked(Get(element).StyleVersion + 1);

    public static long GetLayoutVersion(Element element) =>
        TryGet(element, out var s) && s is not null ? s.LayoutVersion : 0;

    public static long GetStyleVersion(Element element) =>
        TryGet(element, out var s) && s is not null ? s.StyleVersion : 0;

    public static void SetSubtreeElementCount(Element element, int count)
    {
        Get(element).SubtreeElementCount = count;
    }

    public static int GetSubtreeElementCount(Element element) =>
        TryGet(element, out var s) && s is not null ? s.SubtreeElementCount : 0;
}
