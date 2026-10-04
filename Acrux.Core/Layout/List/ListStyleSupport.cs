using Acrux.Core.Dom;

namespace Acrux.Core.Layout.List;

/// <summary>
/// Pseudo-element identifiers used by the layout tree. Mirrors
/// kPseudoId* constants in pseudo_element.h.
/// </summary>
public enum PseudoId
{
    None = 0,
    Before,
    After,
    Marker,
    FirstLetter,
    FirstLine,
    Placeholder,
    Backdrop,
    FileSelectorButton,
    Selection,
    Scrollbar,
    ViewTransition,
}

public static class PseudoElementExtensions
{
    /// <summary>
    /// Return the layout object for the given pseudo-element, if any.
    /// Mirrors Element::PseudoElementLayoutObject().
    /// </summary>
    public static LayoutObject? PseudoElementLayoutObject(this Element element, PseudoId pseudoId)
    {
        if (pseudoId == PseudoId.Marker)
            return element.MarkerLayoutObject as LayoutObject;
        return null;
    }
}

/// <summary>
/// Extensions that bridge the simplified ComputedStyle to the richer behavior
/// assumptions used by the list-marker system. These mirror the corresponding
/// ComputedStyle methods in style.
/// </summary>
public static class ListStyleExtensions
{
    /// <summary>
    /// Whether the 'content' property behaves as 'normal' (i.e. the marker
    /// should generate its own text). Mirrors ContentBehavesAsNormal().
    /// </summary>
    public static bool ContentBehavesAsNormal(this ComputedStyle style)
    {
        return string.IsNullOrEmpty(style.Content) || style.Content == "normal" || style.Content == "none";
    }

    /// <summary>
    /// Whether the list item generates a marker image (list-style-image set).
    /// Mirrors GeneratesMarkerImage().
    /// </summary>
    public static bool GeneratesMarkerImage(this ComputedStyle style)
    {
        return !string.IsNullOrEmpty(style.ListStyleImage);
    }

    /// <summary>
    /// Whether the list-style-type is a counter style (i.e. not none).
    /// Mirrors GeneratesCounterStyle().
    /// </summary>
    public static bool GeneratesCounterStyle(this ComputedStyle style)
    {
        return style.ListStyleType != ListStyleType.None;
    }

    /// <summary>
    /// The text value of list-style-type: "string" markers. Mirrors
    /// ListStyleStringValue().
    /// </summary>
    public static string ListStyleStringValue(this ComputedStyle style)
    {
        return style.Content ?? "";
    }
}