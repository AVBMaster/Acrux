namespace UpBrowser.Core.Dom;

/// <summary>
/// Global DOM mutation counter. Hosts snapshot <see cref="Version"/> around a
/// JS callback and only mark the page dirty when something actually changed —
/// a timer that merely computes must not trigger a full style/layout/paint
/// rebuild and a tile-cache wipe.
/// Every mutating DOM surface (node tree ops, attributes, inline style, text
/// data, form control state) funnels through <see cref="Notify"/>.
/// </summary>
public static class DomMutationTracker
{
    private static long _version;

    public static long Version => Interlocked.Read(ref _version);

    public static void Notify() => Interlocked.Increment(ref _version);
}
