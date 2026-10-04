namespace Acrux.Core.Css.Cascade;

/// <summary>CSS cascade origin, mirroring Blink's CascadeOrigin.</summary>
public enum CascadeOrigin
{
    None,
    UserAgent,
    User,
    Author,
    Animation,
    Transition,
    JsModified
}

/// <summary>CSS cascade priority: encodes the full cascade order per CSS Cascading 4.</summary>
public readonly struct CascadePriority : IEquatable<CascadePriority>
{
    public readonly CascadeOrigin Origin;
    public readonly int LayerOrder;
    public readonly int TreeOrder;
    public readonly int Position;
    public readonly bool IsImportant;
    public readonly uint Generation;
    public readonly int SpecificityA;
    public readonly int SpecificityB;
    public readonly int SpecificityC;

    public CascadePriority(CascadeOrigin origin, int layerOrder, int treeOrder, int position, bool isImportant, uint generation = 0,
        int specificityA = 0, int specificityB = 0, int specificityC = 0)
    {
        Origin = origin;
        LayerOrder = layerOrder;
        TreeOrder = treeOrder;
        Position = position;
        IsImportant = isImportant;
        Generation = generation;
        SpecificityA = specificityA;
        SpecificityB = specificityB;
        SpecificityC = specificityC;
    }

    public CascadePriority WithGeneration(uint generation) =>
        new(Origin, LayerOrder, TreeOrder, Position, IsImportant, generation,
            SpecificityA, SpecificityB, SpecificityC);

    public CascadePriority WithImportant() =>
        new(Origin, LayerOrder, TreeOrder, Position, true, Generation,
            SpecificityA, SpecificityB, SpecificityC);

    public bool IsRelevant => Origin != CascadeOrigin.None;

    /// <summary>
    /// Compare two priorities per CSS cascade order.
    /// Returns: -1 if a wins, 1 if b wins, 0 if equal.
    /// </summary>
    public static int Compare(CascadePriority a, CascadePriority b)
    {
        if (a.IsImportant != b.IsImportant)
            return a.IsImportant ? 1 : -1;

        int originCmp = OriginRank(a.Origin).CompareTo(OriginRank(b.Origin));
        if (originCmp != 0) return originCmp;

        if (a.LayerOrder != b.LayerOrder)
            return a.LayerOrder.CompareTo(b.LayerOrder);

        int specCmp = CompareSpecificity(a, b);
        if (specCmp != 0) return specCmp;

        if (a.TreeOrder != b.TreeOrder)
            return a.TreeOrder.CompareTo(b.TreeOrder);

        if (a.Position != b.Position)
            return a.Position.CompareTo(b.Position);

        return 0;
    }

    private static int CompareSpecificity(CascadePriority a, CascadePriority b)
    {
        if (a.SpecificityA != b.SpecificityA) return a.SpecificityA.CompareTo(b.SpecificityA);
        if (a.SpecificityB != b.SpecificityB) return a.SpecificityB.CompareTo(b.SpecificityB);
        return a.SpecificityC.CompareTo(b.SpecificityC);
    }

    private static int OriginRank(CascadeOrigin origin) => origin switch
    {
        CascadeOrigin.None => -1,
        CascadeOrigin.UserAgent => 0,
        CascadeOrigin.User => 1,
        CascadeOrigin.Author => 2,
        CascadeOrigin.Animation => 3,
        CascadeOrigin.Transition => 4,
        CascadeOrigin.JsModified => 5,
        _ => 0
    };

    public bool Equals(CascadePriority other) =>
        Origin == other.Origin && LayerOrder == other.LayerOrder &&
        TreeOrder == other.TreeOrder && Position == other.Position &&
        IsImportant == other.IsImportant && Generation == other.Generation &&
        SpecificityA == other.SpecificityA && SpecificityB == other.SpecificityB &&
        SpecificityC == other.SpecificityC;

    public override bool Equals(object? obj) => obj is CascadePriority p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(Origin, LayerOrder, TreeOrder, Position, IsImportant);
    public override string ToString() => $"{Origin}{(IsImportant ? "!" : "")}({LayerOrder},{TreeOrder},{Position})";
}