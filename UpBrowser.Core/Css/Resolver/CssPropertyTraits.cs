using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.Resolver;

/// <summary>
/// Per-property metadata used to implement the CSS-wide keywords
/// (inherit / initial / unset / revert). The active cascade is a string switch,
/// so this table carries, for each recognized property:
///   - whether the property inherits by default
///   - a way to restore its initial value and to copy a parent's value
/// Property names are lowercase, matching the cascade map keys.
/// </summary>
public static class CssPropertyTraits
{
    private static readonly HashSet<string> Inherited = new(StringComparer.Ordinal)
    {
        "accent-color", "border-collapse", "border-spacing", "caption-side", "caret-color",
        "color", "cursor", "direction", "empty-cells", "font", "font-family", "font-feature-settings",
        "font-kerning", "font-optical-sizing", "font-size", "font-size-adjust", "font-stretch",
        "font-style", "font-synthesis", "font-variant", "font-variation-settings", "font-weight",
        "hyphens", "image-rendering", "letter-spacing", "line-break", "line-height", "list-style",
        "list-style-image", "list-style-position", "list-style-type", "orphans", "pointer-events",
        "quotes", "tab-size", "text-align", "text-align-last", "text-indent", "text-justify",
        "text-rendering", "text-shadow", "text-transform", "text-underline-offset",
        "text-underline-position", "visibility", "white-space", "widows", "word-break",
        "word-spacing", "writing-mode", "overflow-wrap", "color-scheme", "ruby-position",
        "text-emphasis", "text-emphasis-color", "text-emphasis-style", "text-emphasis-position",
    };

    /// <summary>True when the property inherits its value by default.</summary>
    public static bool IsInherited(string property) => Inherited.Contains(property);

    /// <summary>Whether the cascade recognizes this property at all.</summary>
    public static bool IsKnown(string property) => Known.Contains(property);

    /// <summary>Properties the cascade can resolve initial values for.</summary>
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "width", "height", "min-width", "min-height", "max-width", "max-height",
        "display", "position", "float", "clear", "margin", "margin-top", "margin-right",
        "margin-bottom", "margin-left", "padding", "padding-top", "padding-right",
        "padding-bottom", "padding-left", "color", "background", "background-color",
        "font-family", "font-size", "font-weight", "font-style", "line-height",
        "text-align", "text-decoration", "vertical-align", "white-space", "visibility",
        "overflow", "z-index", "opacity", "border", "border-top", "border-right",
        "border-bottom", "border-left", "border-width", "border-style", "border-color",
        "border-radius", "box-sizing", "outline", "top", "right", "bottom", "left",
        "cursor", "flex", "flex-direction", "flex-wrap", "flex-grow", "flex-shrink",
        "flex-basis", "justify-content", "align-items", "align-self", "order", "gap",
        "transform", "transform-origin", "transition", "animation", "filter",
        "content", "word-break", "overflow-wrap", "letter-spacing", "word-spacing",
        "text-indent", "text-transform", "direction", "writing-mode", "list-style",
        "list-style-type", "list-style-position", "list-style-image",
    };

    /// <summary>Handles `all: <keyword>` — every property the cascade knows about
    /// except direction/unicode-bidi (CSS Properties 4 §all).</summary>
    private static readonly string[] AllResetProperties =
        Known.Union(Inherited)
            .Where(p => p != "direction" && p != "unicode-bidi")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Copies a property value from a source style into a target style.</summary>
    public static void Copy(ComputedStyle to, ComputedStyle from, string property)
    {
        switch (property)
        {
            case "width": to.Width = from.Width; break;
            case "height": to.Height = from.Height; break;
            case "min-width": to.MinWidth = from.MinWidth; break;
            case "min-height": to.MinHeight = from.MinHeight; break;
            case "max-width": to.MaxWidth = from.MaxWidth; break;
            case "max-height": to.MaxHeight = from.MaxHeight; break;
            case "display": to.Display = from.Display; break;
            case "position": to.Position = from.Position; break;
            case "float": to.Float = from.Float; break;
            case "clear": to.Clear = from.Clear; break;
            case "margin": case "margin-top": to.MarginTop = from.MarginTop; break;
            case "margin-right": to.MarginRight = from.MarginRight; break;
            case "margin-bottom": to.MarginBottom = from.MarginBottom; break;
            case "margin-left": to.MarginLeft = from.MarginLeft; break;
            case "padding": case "padding-top": to.PaddingTop = from.PaddingTop; break;
            case "padding-right": to.PaddingRight = from.PaddingRight; break;
            case "padding-bottom": to.PaddingBottom = from.PaddingBottom; break;
            case "padding-left": to.PaddingLeft = from.PaddingLeft; break;
            case "color": to.Color = from.Color; break;
            case "background": case "background-color": to.BackgroundColor = from.BackgroundColor; break;
            case "background-image": to.BackgroundImage = from.BackgroundImage; break;
            case "font-family": to.FontFamily = from.FontFamily; break;
            case "font-size": to.FontSize = from.FontSize; break;
            case "font-weight": to.FontWeight = from.FontWeight; break;
            case "font-style": to.FontStyle = from.FontStyle; break;
            case "line-height":
                to.LineHeight = from.LineHeight;
                to.LineHeightIsNormal = from.LineHeightIsNormal;
                to.LineHeightPx = from.LineHeightPx;
                break;
            case "text-align": to.TextAlign = from.TextAlign; break;
            case "text-decoration": to.TextDecoration = from.TextDecoration; break;
            case "text-decoration-line": to.TextDecorationLine = from.TextDecorationLine; break;
            case "vertical-align": to.VerticalAlign = from.VerticalAlign; break;
            case "white-space": to.WhiteSpace = from.WhiteSpace; break;
            case "tab-size": to.TabSize = from.TabSize; to.TabSizePx = from.TabSizePx; break;
            case "visibility": to.Visibility = from.Visibility; break;
            case "overflow": to.Overflow = to.OverflowX = to.OverflowY = from.Overflow; break;
            case "overflow-x": to.OverflowX = from.OverflowX; break;
            case "overflow-y": to.OverflowY = from.OverflowY; break;
            case "z-index": to.ZIndex = from.ZIndex; break;
            case "opacity": to.Opacity = from.Opacity; break;
            case "border-width": case "border-top-width": to.BorderTopWidth = from.BorderTopWidth; break;
            case "border-right-width": to.BorderRightWidth = from.BorderRightWidth; break;
            case "border-bottom-width": to.BorderBottomWidth = from.BorderBottomWidth; break;
            case "border-left-width": to.BorderLeftWidth = from.BorderLeftWidth; break;
            case "border-style": case "border-top-style": to.BorderTopStyle = from.BorderTopStyle; break;
            case "border-right-style": to.BorderRightStyle = from.BorderRightStyle; break;
            case "border-bottom-style": to.BorderBottomStyle = from.BorderBottomStyle; break;
            case "border-left-style": to.BorderLeftStyle = from.BorderLeftStyle; break;
            case "border-color": case "border-top-color": to.BorderTopColor = from.BorderTopColor; break;
            case "border-right-color": to.BorderRightColor = from.BorderRightColor; break;
            case "border-bottom-color": to.BorderBottomColor = from.BorderBottomColor; break;
            case "border-left-color": to.BorderLeftColor = from.BorderLeftColor; break;
            case "border-radius": case "border-top-left-radius": to.BorderTopLeftRadius = from.BorderTopLeftRadius; break;
            case "border-top-right-radius": to.BorderTopRightRadius = from.BorderTopRightRadius; break;
            case "border-bottom-right-radius": to.BorderBottomRightRadius = from.BorderBottomRightRadius; break;
            case "border-bottom-left-radius": to.BorderBottomLeftRadius = from.BorderBottomLeftRadius; break;
            case "box-sizing": to.BoxSizing = from.BoxSizing; break;
            case "top": to.Top = from.Top; break;
            case "right": to.Right = from.Right; break;
            case "bottom": to.Bottom = from.Bottom; break;
            case "left": to.Left = from.Left; break;
            case "cursor": to.Cursor = from.Cursor; break;
            case "flex-direction": to.FlexDirection = from.FlexDirection; break;
            case "flex-wrap": to.FlexWrap = from.FlexWrap; break;
            case "flex-grow": to.FlexGrow = from.FlexGrow; break;
            case "flex-shrink": to.FlexShrink = from.FlexShrink; break;
            case "flex-basis": to.FlexBasis = from.FlexBasis; break;
            case "justify-content": to.JustifyContent = from.JustifyContent; break;
            case "align-items": to.AlignItems = from.AlignItems; break;
            case "align-self": to.AlignSelf = from.AlignSelf; break;
            case "order": to.Order = from.Order; break;
            case "gap": case "row-gap": to.RowGap = from.RowGap; break;
            case "column-gap": to.ColumnGap = from.ColumnGap; break;
            case "transform": to.Transform = from.Transform; break;
            case "transform-origin": to.TransformOrigin = from.TransformOrigin; break;
            case "transition": to.Transition = from.Transition; break;
            case "animation": to.Animation = from.Animation; break;
            case "filter": to.Filter = from.Filter; break;
            case "content": to.Content = from.Content; break;
            case "word-break": to.WordBreak = from.WordBreak; break;
            case "overflow-wrap": to.OverflowWrap = from.OverflowWrap; break;
            case "letter-spacing": to.LetterSpacing = from.LetterSpacing; break;
            case "word-spacing": to.WordSpacing = from.WordSpacing; break;
            case "text-indent": to.TextIndent = from.TextIndent; to.TextIndentHanging = from.TextIndentHanging;
                to.TextIndentPercent = from.TextIndentPercent; break;
            case "text-transform": to.TextTransform = from.TextTransform; break;
            case "direction": to.Direction = from.Direction; break;
            case "writing-mode": to.WritingMode = from.WritingMode; break;
            case "list-style": case "list-style-type": to.ListStyleType = from.ListStyleType;
                to.ListStyleTypeString = from.ListStyleTypeString; break;
            case "list-style-position": to.ListStylePosition = from.ListStylePosition; break;
            case "list-style-image": to.ListStyleImage = from.ListStyleImage; break;
        }
    }

    /// <summary>Restores a property to its initial (default) value.</summary>
    public static void SetInitial(ComputedStyle to, string property)
    {
        // 'display' has the initial value 'inline' (CSS 2.1 §9.7), while a fresh
        // ComputedStyle models the engine's default box as block.
        if (property == "display")
        {
            to.Display = DisplayType.Inline;
            return;
        }
        Copy(to, new ComputedStyle(), property);
    }

    /// <summary>
    /// Handles the CSS-wide keywords inherit/initial/unset/revert/revert-layer.
    /// Returns true when the value is a global keyword (already applied), false
    /// when the value is a normal declaration that the caller must apply.
    /// </summary>
    public static bool TryApplyCssWideKeyword(ComputedStyle style, string name, string value, ComputedStyle? parentStyle)
    {
        if (name == "all")
            return TryApplyAll(style, value.Trim(), parentStyle);

        switch (value.Trim())
        {
            case "inherit":
                if (IsKnown(name))
                    Copy(style, parentStyle ?? new ComputedStyle(), name);
                return true;
            case "initial":
                if (IsKnown(name))
                    SetInitial(style, name);
                return true;
            case "unset":
                if (IsInherited(name))
                    Copy(style, parentStyle ?? new ComputedStyle(), name);
                else if (IsKnown(name))
                    SetInitial(style, name);
                return true;
            case "revert":
            case "revert-layer":
                // Best effort: treat as unset (author layer history is not tracked).
                if (IsInherited(name))
                    Copy(style, parentStyle ?? new ComputedStyle(), name);
                else if (IsKnown(name))
                    SetInitial(style, name);
                return true;
            default:
                return false;
        }
    }

    private static bool TryApplyAll(ComputedStyle style, string value, ComputedStyle? parentStyle)
    {
        var source = parentStyle ?? new ComputedStyle();
        switch (value)
        {
            case "inherit":
                foreach (var property in AllResetProperties)
                    Copy(style, source, property);
                return true;
            case "initial":
                foreach (var property in AllResetProperties)
                    SetInitial(style, property);
                return true;
            case "unset":
            case "revert":
            case "revert-layer":
                foreach (var property in AllResetProperties)
                {
                    if (IsInherited(property))
                        Copy(style, source, property);
                    else
                        SetInitial(style, property);
                }
                return true;
            default:
                return false;
        }
    }
}
