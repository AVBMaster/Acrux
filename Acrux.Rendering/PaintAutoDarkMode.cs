namespace Acrux.Rendering;

/// <summary>
/// A scope to prevent pre-paint from writing to LayoutObject, PaintLayer,
/// FragmentData, etc. Mirrors PrePaintDisableSideEffectsScope.
/// </summary>
public class PrePaintDisableSideEffectsScope : IDisposable
{
    private static int _count;

    public PrePaintDisableSideEffectsScope()
    {
        _count++;
    }

    public void Dispose()
    {
        if (_count > 0)
            _count--;
    }

    public static bool IsDisabled => _count > 0;
}

/// <summary>
/// Auto-dark-mode helper for paint operations.
/// Mirrors PaintAutoDarkMode() in paint_auto_dark_mode.h.
/// </summary>
public static class PaintAutoDarkModeHelper
{
    /// <summary>Maximum ratio of image size to screen size considered an icon.</summary>
    private const float MaxIconRatio = 0.13f;
    private const int MaxImageLength = 50;
    private const int MaxImageSeparatorLength = 8;

    public enum ElementRole
    {
        Background,
        Foreground,
        Border,
        Selection,
    }

    public enum ImageType
    {
        None,
        Icon,
        Separator,
        Photo,
    }

    public readonly struct AutoDarkMode
    {
        public bool Enabled { get; }
        public ElementRole Role { get; }

        public AutoDarkMode(ElementRole role, bool enabled)
        {
            Role = role;
            Enabled = enabled;
        }

        public static AutoDarkMode Disabled() => new(ElementRole.Background, false);
    }

    public static AutoDarkMode PaintAutoDarkMode(ElementRole role, bool autoDarkModeEnabled)
    {
        return new AutoDarkMode(role, autoDarkModeEnabled);
    }

    /// <summary>
    /// Classify an image for auto-dark-mode filtering based on its size relative
    /// to the viewport.
    /// </summary>
    public static ImageType GetImageType(float destToDeviceRatio, int destW, int destH, int srcW, int srcH)
    {
        if (destToDeviceRatio <= MaxIconRatio || (destW <= MaxImageLength && destH <= MaxImageLength))
            return ImageType.Icon;
        if (srcW <= MaxImageSeparatorLength || srcH <= MaxImageSeparatorLength)
            return ImageType.Separator;
        return ImageType.Photo;
    }
}