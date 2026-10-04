namespace Acrux.Core.Layout;

/// <summary>
/// Engine counters that describe how much re-work a layout run needed, and are the
/// only way to see it: when a block's BFC block-offset is only resolved after its
/// children have been laid out, the block throws its own fragment away and the
/// affected child is laid out again. That work is invisible in the final geometry,
/// so a convergence bug (a page that never settles, a subtree restarted over and
/// over) shows up here first.
/// </summary>
public static class LayoutDiagnostics
{
    private static long _rootPasses;
    private static long _blockPasses;
    private static long _bfcOffsetAborts;
    private static long _bfcOffsetChildRetries;

    /// <summary>Whole-tree layout runs (one per <see cref="LayoutEngine.Layout"/> call).</summary>
    public static long RootPasses => Volatile.Read(ref _rootPasses);

    /// <summary>Block-algorithm runs, including restarted ones.</summary>
    public static long BlockPasses => Volatile.Read(ref _blockPasses);

    /// <summary>Subtree passes thrown away because a BFC block-offset was resolved late.</summary>
    public static long BfcOffsetAborts => Volatile.Read(ref _bfcOffsetAborts);

    /// <summary>Children re-laid out by their parent after such an abort.</summary>
    public static long BfcOffsetChildRetries => Volatile.Read(ref _bfcOffsetChildRetries);

    public static void Reset()
    {
        _rootPasses = 0;
        _blockPasses = 0;
        _bfcOffsetAborts = 0;
        _bfcOffsetChildRetries = 0;
    }

    internal static void CountRootPass() => Interlocked.Increment(ref _rootPasses);
    internal static void CountBlockPass() => Interlocked.Increment(ref _blockPasses);
    internal static void CountBfcAbort() => Interlocked.Increment(ref _bfcOffsetAborts);
    internal static void CountBfcChildRetry() => Interlocked.Increment(ref _bfcOffsetChildRetries);
}
