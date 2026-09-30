namespace UpBrowser.Core.Dom.Animations;

/// <summary>
/// Which CSS properties can be animated, and which of them need layout.
///
/// CSS Animations 1 §2 defines animatability: a property animates when its
/// computed value can change over time with a <c>transition</c> or
/// <c>animation</c>. Shorthands never animate, discrete properties animate by
/// flipping at 50%, and properties that participate in box generation (sizing,
/// spacing, position) force a re-layout for every frame they move in.
/// </summary>
public static class AnimatableProperties
{
    private static readonly HashSet<string> _animatable = new(StringComparer.Ordinal)
    {
        // Visual / paint-only: the compositor can move these without layout.
        "transform", "translate", "rotate", "scale", "perspective", "transform-origin",
        "transform-style", "backface-visibility", "opacity", "filter", "backdrop-filter",
        "box-shadow", "text-shadow", "color", "background-color", "background-position",
        "background-size", "background-image", "background-repeat", "background-clip",
        "background-origin", "background-attachment", "background-blend-mode", "border-color",
        "border-top-color", "border-right-color", "border-bottom-color", "border-left-color",
        "border-image-source", "border-image-slice", "border-image-width", "border-image-outset",
        "border-image-repeat", "outline-color", "outline-style", "outline-width", "outline-offset",
        "text-decoration-color", "text-decoration-style", "text-decoration-thickness",
        "text-underline-offset", "text-underline-position", "text-decoration-skip-ink",
        "text-shadow", "caret-color", "accent-color", "column-rule-color", "column-rule-width",
        "column-rule-style", "fill", "stroke", "stroke-width", "stroke-opacity", "fill-opacity",
        "clip-path", "mask-image", "mask-size", "mask-position", "mask-clip", "mask-origin",
        "mix-blend-mode", "isolation", "box-sizing", "visibility", "pointer-events", "cursor",
        "resize", "list-style-image", "list-style-type", "list-style-position", "content",
        "quotes", "counter-reset", "counter-increment", "will-change", "image-rendering",
        "text-rendering", "-webkit-text-fill-color", "-webkit-text-stroke-color", "-webkit-text-stroke-width",
        "shape-outside", "shape-margin", "shape-image-threshold", "text-emphasis",
        "text-emphasis-color", "text-emphasis-style", "text-emphasis-position",
        "object-position", "scroll-margin-top", "scroll-margin-right", "scroll-margin-bottom",
        "scroll-margin-left", "offset", "offset-path", "offset-distance", "offset-rotate",
        "scroll-timeline", "view-timeline", "animation-timeline", "transition-behavior",
        "text-wrap", "hyphenate-character", "tab-size",

        // Layout-affecting: interpolated, but they re-run layout each frame.
        "width", "height", "min-width", "max-width", "min-height", "max-height",
        "block-size", "inline-size", "min-block-size", "min-inline-size",
        "max-block-size", "max-inline-size",
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "top", "right", "bottom", "left", "inset-block", "inset-inline",
        "flex-basis", "flex-grow", "flex-shrink",
        "row-gap", "column-gap", "gap", "grid-template-columns", "grid-template-rows",
        "grid-auto-columns", "grid-auto-rows", "grid-template", "grid",
        "line-height", "font-size", "font-weight", "letter-spacing", "word-spacing",
        "text-indent", "font-variation-settings", "border-top-width", "border-right-width",
        "border-bottom-width", "border-left-width",
        "border-top-left-radius", "border-top-right-radius",
        "border-bottom-right-radius", "border-bottom-left-radius",
        "border-spacing", "order", "z-index", "aspect-ratio", "overflow-x", "overflow-y",
        "table-layout", "caption-side", "empty-cells", "border-collapse",
        "clip-rule", "fill-rule", "stroke-dasharray", "stroke-dashoffset", "stroke-miterlimit",
    };

    /// <summary>
    /// Properties whose interpolation requires a re-layout. The host uses this to
    /// decide whether an animation tick needs a full layout pass or can reuse the
    /// previous boxes and repaint only (the compositor-only fast path).
    /// </summary>
    private static readonly HashSet<string> _layoutAffecting = new(StringComparer.Ordinal)
    {
        "width", "height", "min-width", "max-width", "min-height", "max-height",
        "block-size", "inline-size", "min-block-size", "min-inline-size",
        "max-block-size", "max-inline-size",
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "top", "right", "bottom", "left", "inset-block", "inset-inline",
        "flex-basis", "flex-grow", "flex-shrink",
        "row-gap", "column-gap", "gap", "grid", "grid-template", "grid-template-columns",
        "grid-template-rows", "grid-auto-columns", "grid-auto-rows",
        "line-height", "font-size", "font-weight", "letter-spacing", "word-spacing",
        "text-indent", "font-variation-settings",
        "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
        "border-top-left-radius", "border-top-right-radius",
        "border-bottom-right-radius", "border-bottom-left-radius",
        "border-spacing", "order", "aspect-ratio", "overflow-x", "overflow-y",
        "table-layout", "caption-side", "empty-cells", "border-collapse", "hyphenate-character",
    };

    /// <summary>
    /// Properties whose painted pixels leave the box by an amount the box geometry
    /// cannot express: a shadow spread, an outline offset, a blur. A host culling by
    /// geometry cannot locate their damage at all.
    /// </summary>
    private static readonly HashSet<string> _bleedsOutsideBox = new(StringComparer.Ordinal)
    {
        "box-shadow", "text-shadow", "outline", "outline-width", "outline-offset",
        "outline-color", "filter", "backdrop-filter", "drop-shadow",
    };

    /// <summary>Properties that only change in discrete steps at the 50% mark.</summary>
    private static readonly HashSet<string> _discrete = new(StringComparer.Ordinal)
    {
        "visibility", "pointer-events", "cursor", "resize", "display", "position", "float",
        "clear", "box-sizing", "text-decoration-style", "text-decoration-line",
        "border-style", "border-top-style", "border-right-style", "border-bottom-style",
        "border-left-style", "outline-style", "column-rule-style", "overflow", "overflow-x",
        "overflow-y", "list-style-type", "list-style-position", "table-layout", "caption-side",
        "empty-cells", "border-collapse", "background-repeat", "background-attachment",
        "background-clip", "background-origin", "background-image", "border-image-repeat",
        "object-fit", "mix-blend-mode", "isolation", "clip-rule", "fill-rule",
        "backface-visibility", "transform-style", "image-rendering", "text-rendering",
        "writing-mode", "direction", "font-style", "font-variant-caps", "text-transform",
        "text-wrap", "white-space", "word-break", "overflow-wrap", "hyphens", "unicode-bidi",
        "mask-repeat", "mask-clip", "mask-mode", "mask-composite", "z-index", "order",
    };

    public static bool IsAnimatable(string property)
    {
        if (string.IsNullOrEmpty(property)) return false;
        // Custom properties are not interpolable by the engine, and shorthands
        // never animate (CSS Animations 1 §2).
        if (property.StartsWith("--", StringComparison.Ordinal)) return false;
        if (IsShorthand(property)) return false;
        return _animatable.Contains(property);
    }

    public static bool IsLayoutAffecting(string property) => _layoutAffecting.Contains(property);

    /// <summary>True when the animated value can paint pixels outside the box entirely.</summary>
    public static bool BleedsOutsideBox(string property) => _bleedsOutsideBox.Contains(property);

    /// <summary>True when the property is animated as a discrete 50% flip.</summary>
    public static bool IsDiscrete(string property) => _discrete.Contains(property);

    /// <summary>
    /// Shorthands are not animatable; a declaration naming one inside
    /// <c>@keyframes</c> is dropped, and <c>transition-property: all</c> skips it.
    /// </summary>
    public static bool IsShorthand(string property) => property switch
    {
        "all" or "animation" or "background" or "border" or "border-color" or "border-style"
            or "border-width" or "border-radius" or "border-top" or "border-right"
            or "border-bottom" or "border-left" or "border-image"
            or "column-rule" or "columns" or "content" or "flex" or "flex-flow" or "font"
            or "gap" or "grid" or "grid-area" or "grid-column" or "grid-row" or "grid-template"
            or "inset" or "list-style" or "margin" or "mask" or "offset" or "outline"
            or "overflow" or "padding" or "place-content" or "place-items" or "place-self"
            or "text-decoration" or "text-emphasis" or "text-wrap" or "transition"
            or "inset-block" or "inset-inline" or "inset-block-start" or "inset-block-end"
            or "inset-inline-start" or "inset-inline-end"
            or "border-image-outset" or "border-image-width"
            or "border-block" or "border-inline" or "margin-block" or "margin-inline"
            or "padding-block" or "padding-inline" or "scroll-margin" or "scroll-padding"
            or "page-break-after" or "page-break-before" or "page-break-inside" => true,
        _ => false,
    };

    /// <summary>
    /// The properties <c>transition-property: all</c> covers, in a stable order.
    /// The duration/delay/easing lists cycle over this order, so it has to be
    /// deterministic across runs — a <see cref="HashSet{T}"/> enumeration order is
    /// not stable and would make <c>all</c> transitions non-reproducible.
    /// Only layout-independent properties are listed: a size change goes through
    /// the normal path anyway.
    /// </summary>
    public static IReadOnlyList<string> AllTransitionable { get; } = BuildAllTransitionable();

    private static IReadOnlyList<string> BuildAllTransitionable()
    {
        var list = new List<string>(_animatable.Count);
        foreach (var name in _animatable)
        {
            if (IsShorthand(name)) continue;
            if (name is "display" or "position" or "float" or "clear" or "content" or "quotes"
                or "will-change" or "order" or "z-index") continue;
            list.Add(name);
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }
}
