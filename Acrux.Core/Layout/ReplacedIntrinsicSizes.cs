using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Seam between layout and the image decoder: replaced elements need their
/// intrinsic size during layout, but decoding lives in the rendering layer.
/// The renderer registers <see cref="Resolver"/> with a cache-backed lookup.
/// </summary>
public static class ReplacedIntrinsicSizes
{
    /// <summary>Returns the decoded size for a raw (unresolved) source string, or
    /// null when the image is unknown — layout then falls back to the default size.</summary>
    public static Func<string?, PhysicalSize?>? Resolver;

    /// <summary>
    /// The resolver the document being laid out on this thread owns, when the host said so.
    ///
    /// A process can hold more than one engine, and each one decodes images against its own
    /// base URL and its own cache. One process-wide slot makes the last page to install it
    /// answer for every document, so a second tab's <c>&lt;img&gt;</c> is measured against the
    /// first tab's images. A host therefore brackets its layout and paint stages with
    /// <see cref="Use"/>; the shared slot stays as the fallback for a host that installs from
    /// a different thread than it lays out on, which keeps single-engine processes exactly as
    /// they were.
    /// </summary>
    [ThreadStatic] private static Func<string?, PhysicalSize?>? _scoped;

    /// <summary>Install <paramref name="resolver"/> for the duration of the returned scope.</summary>
    public static IDisposable Use(Func<string?, PhysicalSize?> resolver)
    {
        var previous = _scoped;
        _scoped = resolver;
        return new Scope(() => _scoped = previous);
    }

    private sealed class Scope(Action close) : IDisposable
    {
        public void Dispose() => close();
    }

    /// <summary>Returns the decoded size for a raw (unresolved) source string, or
    /// null when the image is unknown — layout then falls back to the default size.</summary>
    public static PhysicalSize? Lookup(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return null;
        var resolver = _scoped ?? Resolver;
        if (resolver == null) return null;
        try
        {
            return resolver.Invoke(source);
        }
        catch
        {
            return null;
        }
    }
}
