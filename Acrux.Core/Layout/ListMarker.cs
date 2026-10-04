using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// Generates and lays out CSS list markers (bullets / numbers).
/// Mirrors the core of list_marker.cc.
/// </summary>
public static class ListMarker
{
    /// <summary>Generate the marker text for a list item with the given ordinal.</summary>
    public static string MarkerText(ListStyleType type, int ordinal) =>
        List.ListMarkerFormatter.MarkerLabel(type, ordinal, null);

    /// <summary>Estimate the marker width in pixels based on list-style-type and font size.</summary>
    public static float MarkerWidth(ListStyleType type, ListStylePosition position, float fontSize)
    {
        if (position == ListStylePosition.Inside)
            return fontSize * 1.2f;
        switch (type)
        {
            case ListStyleType.None:
                return 0;
            case ListStyleType.Disc:
            case ListStyleType.Circle:
            case ListStyleType.Square:
                return fontSize * 0.8f;
            default:
                return fontSize * 1.5f; // numbered markers
        }
    }


}
