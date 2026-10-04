using SkiaSharp;

namespace Acrux.Core.Dom;

public enum ScrollbarWidthType { Auto, Thin, None }

/// <summary>
/// Per-part custom styling collected from ::-webkit-scrollbar-* pseudo-element
/// rules (background / border / border-radius / bar thickness).
/// </summary>
public class ScrollbarPartStyle
{
    public SKColor? Background;
    public float BorderRadius;
    public float BorderWidth;
    public SKColor? BorderColor;

    /// <summary>::-webkit-scrollbar { width|height } — bar thickness in px.</summary>
    public int Thickness;
    public bool HasThickness;

    public bool HasAny => Background.HasValue || BorderWidth > 0 || HasThickness || BorderRadius > 0;
}

/// <summary>
/// Custom scrollbar styling for one element. Null on <see cref="ComputedStyle"/>
/// when no scrollbar pseudo rules matched.
/// </summary>
public sealed class ScrollbarStyles
{
    /// <summary>::-webkit-scrollbar — carries the bar thickness + base background.</summary>
    public ScrollbarPartStyle Bar = new();
    /// <summary>::-webkit-scrollbar-thumb.</summary>
    public ScrollbarPartStyle Thumb = new();
    /// <summary>::-webkit-scrollbar-track.</summary>
    public ScrollbarPartStyle Track = new();
    /// <summary>::-webkit-scrollbar-corner.</summary>
    public ScrollbarPartStyle Corner = new();

    public bool HasAny => Bar.HasAny || Thumb.HasAny || Track.HasAny || Corner.HasAny;
}

/// <summary>
/// Classic (non-overlay) scrollbar metrics shared by layout (space
/// reservation), painting and hit-testing so all three agree on the bar
/// thickness of an element.
/// </summary>
public static class ScrollbarMetrics
{
    public const float DefaultThickness = 12f;
    public const float ThinThickness = 8f;
    public const float MinThickness = 3f;
    public const float MaxThickness = 100f;
    public const float MinThumbLength = 20f;

    /// <summary>
    /// Resolved bar thickness for an element in px; 0 when scrollbars are
    /// hidden (scrollbar-width: none).
    /// </summary>
    public static float ThicknessFor(ComputedStyle style)
    {
        if (style.ScrollbarWidth == ScrollbarWidthType.None) return 0;
        var bar = style.ScrollbarCustom?.Bar;
        if (bar is { HasThickness: true })
            return Math.Clamp(bar.Thickness, MinThickness, MaxThickness);
        return style.ScrollbarWidth == ScrollbarWidthType.Thin ? ThinThickness : DefaultThickness;
    }
}
