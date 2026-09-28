using UpBrowser.Core.Layout;

namespace UpBrowser.PageHost;

/// <summary>
/// Per-process engine prerequisites. Every process that lays out or paints a page
/// must call <see cref="Initialize"/> once before it does — the layout engine reads
/// <see cref="TextMeasurer.Instance"/> through a static seam, so a host that skips
/// this silently measures text with the fallback instead of Skia.
/// </summary>
public static class PageEnvironment
{
    static PageEnvironment()
    {
        // SKFontManager enumeration is expensive; the system font list is static
        // for the process lifetime.
        FontFamilies = SkiaSharp.SKFontManager.Default.FontFamilies.ToArray();
    }

    /// <summary>Installed font family names, enumerated once for the process.</summary>
    public static string[] FontFamilies { get; }

    public static void Initialize()
    {
        if (TextMeasurer.Instance is SkiaTextMeasurer) return;
        TextMeasurer.Instance = new SkiaTextMeasurer();
    }
}
