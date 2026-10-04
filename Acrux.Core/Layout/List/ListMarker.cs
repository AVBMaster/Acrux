using System.Text;
using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.List;

/// <summary>
/// Hold code shared among all classes for list markers, for both legacy layout
/// and the modern layout pipeline. Mirrors list_marker.h/.cc.
/// </summary>
public class ListMarker
{
    // --- Marker text type constants ---
    private enum MarkerTextFormat
    {
        WithPrefixSuffix,
        WithoutPrefixSuffix,
        AlternativeText,
    }

    private enum MarkerTextType
    {
        NotText,
        Unresolved,
        OrdinalValue,
        Static,
        SymbolValue,
    }

    // --- List style category ---
    public enum ListStyleCategory
    {
        None,
        Symbol,
        Language,
        StaticString,
    }

    private MarkerTextType _markerTextType = MarkerTextType.NotText;

    // --- UA constants ---
    private const int CMarkerPaddingPx = 7;
    private const int CUAMarkerMarginEm = 1;
    private const float CClosureMarkerMarginEm = 0.4f;

    public ListMarker()
    {
        _markerTextType = MarkerTextType.NotText;
    }

    // ============ Static helpers ============

    public static ListMarker? Get(LayoutObject? marker)
    {
        if (marker is LayoutOutsideListMarker outsideMarker)
            return outsideMarker.Marker();
        if (marker is LayoutInsideListMarker insideMarker)
            return insideMarker.Marker();
        return null;
    }

    public static LayoutObject? MarkerFromListItem(LayoutObject? listItem)
    {
        if (listItem is LayoutListItem li)
            return li.Marker();
        if (listItem is LayoutInlineListItem inlineLi)
            return inlineLi.Marker();
        return null;
    }

    /// <summary>Generate the marker text for a list item with the given ordinal.</summary>
    public static string MarkerText(ListStyleType type, int ordinal) =>
        ListMarkerFormatter.MarkerLabel(type, ordinal, null);

    /// <summary>The marker text of a list item, including the counter style the item's
    /// style selects - predefined, quoted string, or a document @counter-style rule.</summary>
    public static string MarkerText(ComputedStyle style, int ordinal, Document? document) =>
        ListMarkerFormatter.MarkerLabel(style, ordinal, document);

    /// <summary>
    /// The marker's inline box: the label with its separator, plus the
    /// 'normal space' CSS Counter Styles puts after every suffix except the
    /// ideographic ones (U+3001 already is the whole suffix). The space belongs
    /// to the box, which is what makes an outside marker's glyph end one space
    /// before the item's content edge instead of sitting right on it.
    /// A custom '::marker { content }' replaces the label verbatim and never
    /// gets that space - the author's string carries its own spacing.
    /// </summary>
    public static string MarkerBoxText(ComputedStyle style, int ordinal, string? markerContent,
        Document? document)
    {
        string? custom = ResolveMarkerContent(markerContent, ordinal);
        if (custom is not null) return custom;

        string label = ListMarkerFormatter.MarkerLabel(style, ordinal, document);
        // A quoted <string> type IS the whole marker, same as custom content.
        if (style.ListStyleType == ListStyleType.String) return label;
        if (label.Length == 0) return label;
        char last = label[label.Length - 1];
        if (char.IsWhiteSpace(last) || last == '、') return label;
        return label + " ";
    }

    public static string MarkerBoxText(ListStyleType type, int ordinal, string? typeString) =>
        MarkerBoxText(StyleFor(type, typeString), ordinal, null, null);

    /// <summary>The few call sites that only know the marker type get a stand-in style,
    /// which carries the type and the quoted string the same way a computed one does.</summary>
    private static ComputedStyle StyleFor(ListStyleType type, string? typeString) => new()
    {
        ListStyleType = type,
        ListStyleTypeString = typeString,
    };

    /// <summary>
    /// Resolve a '::marker { content }' declaration to the marker's text.
    /// Returns null when the declaration leaves the preset counter label alone
    /// ('normal', absent, or a construct we cannot evaluate here); returns an
    /// empty string for 'content: none' and 'content: ""', which suppress the box.
    /// </summary>
    public static string? ResolveMarkerContent(string? raw, int ordinal)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string value = raw.Trim();
        if (value.Equals("normal", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return string.Empty;

        var text = new StringBuilder();
        int i = 0;
        while (i < value.Length)
        {
            char c = value[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c is '"' or '\'')
            {
                i++;
                while (i < value.Length && value[i] != c)
                {
                    if (value[i] == '\\' && i + 1 < value.Length)
                    {
                        char escaped = value[++i];
                        text.Append(escaped switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            _ => escaped,
                        });
                        i++;
                        continue;
                    }
                    text.Append(value[i]);
                    i++;
                }
                i++;
                continue;
            }

            if (!System.MemoryExtensions.StartsWith(value.AsSpan(i), "counter(".AsSpan(),
                    StringComparison.OrdinalIgnoreCase))
                return null;

            int close = value.IndexOf(')', i);
            if (close < 0) return null;
            string name = value[(i + "counter(".Length)..close].Trim();
            // Inside a marker, 'list-item' is the only counter with a guaranteed
            // value; anything else would need the document's counter scope.
            if (!name.Equals("list-item", StringComparison.OrdinalIgnoreCase)) return null;
            text.Append(ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            i = close + 1;
        }
        return text.ToString();
    }

    /// <summary>The raw '::marker { content }' declaration of an item, if any.</summary>
    public static string? MarkerContentOf(Element? item) =>
        item?.MarkerStyles is { } styles && styles.TryGetValue("content", out string? value) ? value : null;

    /// <summary>
    /// A symbolic marker (disc/circle/square) reserves a box of 1.4 times the
    /// MARKER font size rather than the width of its glyph: measured, all three
    /// share one width, and changing '::marker { font-size }' moves it while the
    /// item's font size does not.
    /// </summary>
    public const float SymbolicMarkerBoxFontSizeFactor = 1.4f;

    public static bool IsSymbolicMarker(ListStyleType type) =>
        type is ListStyleType.Disc or ListStyleType.Circle or ListStyleType.Square;

    /// <summary>The font size '::marker' asks the marker to use, else the item's.</summary>
    public static float MarkerFontSize(ComputedStyle style, Element? item)
    {
        if (item?.MarkerStyles is { } styles && styles.TryGetValue("font-size", out string? text))
        {
            float resolved = Length.Parse(text.Trim()).ToPixels(style.FontSize, style.FontSize, 0, 0);
            if (!float.IsNaN(resolved) && resolved > 0) return resolved;
        }
        return style.FontSize;
    }

    /// <summary>Measured inline box width of a marker (see <see cref="MarkerBoxText"/>).
    /// Falls back to the type-based estimate only when shaping yields nothing
    /// (no measurer installed), because an estimate that ignores the label makes
    /// long markers overlap the item text. An empty box reserves nothing.</summary>
    public static float MarkerBoxWidth(ComputedStyle style, int ordinal, string? markerContent, Element? item)
    {
        string text = MarkerBoxText(style, ordinal, markerContent, item?.OwnerDocument);
        if (text.Length == 0) return 0;
        if (style.ListStyleType == ListStyleType.None) return 0;
        // Only the preset symbols get the wide box; custom content is verbatim.
        if (IsSymbolicMarker(style.ListStyleType) && ResolveMarkerContent(markerContent, ordinal) is null)
            return SymbolicMarkerBoxFontSizeFactor * MarkerFontSize(style, item);
        float measured = Inline.TextMeasureProxy.MeasureText(text, style);
        return measured > 0 ? measured : MarkerWidth(style.ListStyleType, style.ListStylePosition, style.FontSize);
    }

    public static float MarkerBoxWidth(ComputedStyle style, int ordinal) =>
        MarkerBoxWidth(style, ordinal, null, null);

    /// <summary>
    /// list-style-position: inside puts the marker in the first line box, so the
    /// text starts after it (CSS 2.1 §10.7.1).
    /// </summary>
    public static bool GeneratesInsideMarker(ComputedStyle style) =>
        style.Display == DisplayType.ListItem &&
        style.ListStylePosition == ListStylePosition.Inside &&
        style.ListStyleType != ListStyleType.None;

    public static float InsideMarkerIndent(ComputedStyle style, int ordinal, string? markerContent, Element? item) =>
        GeneratesInsideMarker(style) ? MarkerBoxWidth(style, ordinal, markerContent, item) : 0;

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
                return fontSize * 1.5f;
        }
    }

    public static LayoutUnit WidthOfSymbol(ComputedStyle style, string listStyle)
    {
        float fontSize = style.FontSize;
        if (fontSize <= 0) return LayoutUnit.Zero;
        if (listStyle is "disclosure-open" or "disclosure-closed")
            return new LayoutUnit(fontSize * style.Zoom * 0.66f);
        float ascent = Fonts.LineBoxMetrics.GetFontMetrics(style).LayoutAscent;
        return new LayoutUnit((ascent * 2 / 3 + 1) / 2 + 2);
    }

    public static PhysicalRect RelativeSymbolMarkerRect(ComputedStyle style, string listStyle, LayoutUnit width)
    {
        float fontSize = style.FontSize;
        float ascent = Fonts.LineBoxMetrics.GetFontMetrics(style).LayoutAscent;

        if (listStyle is "disclosure-open" or "disclosure-closed")
        {
            float markerSize = fontSize * style.Zoom * 0.66f;
            return new PhysicalRect(0, ascent - markerSize, markerSize, markerSize);
        }

        float bulletWidth = (ascent * 2 / 3 + 1) / 2;
        return new PhysicalRect(1, 3 * (ascent - ascent * 2 / 3) / 2, bulletWidth, bulletWidth);
    }

    public static ListStyleCategory GetListStyleCategory(ComputedStyle style)
    {
        var listStyleType = style.ListStyleType;
        if (listStyleType == ListStyleType.None)
            return ListStyleCategory.None;

        if (!string.IsNullOrEmpty(style.Content) && style.Content != "normal" && style.Content != "none")
            return ListStyleCategory.StaticString;

        return IsPredefinedSymbolMarker(listStyleType)
            ? ListStyleCategory.Symbol
            : ListStyleCategory.Language;
    }

    public static bool IsPredefinedSymbolMarker(ListStyleType type)
    {
        return type switch
        {
            ListStyleType.Disc => true,
            ListStyleType.Circle => true,
            ListStyleType.Square => true,
            _ => false,
        };
    }

    public static (LayoutUnit, LayoutUnit) InlineMarginsForInside(ComputedStyle markerStyle, ComputedStyle listItemStyle)
    {
        if (!listItemStyle.ContentBehavesAsNormal())
            return (LayoutUnit.Zero, LayoutUnit.Zero);

        if (listItemStyle.GeneratesMarkerImage())
            return (LayoutUnit.Zero, new LayoutUnit(CMarkerPaddingPx));

        var category = GetListStyleCategory(listItemStyle);
        if (category == ListStyleCategory.Symbol)
        {
            string name = ListStyleName(listItemStyle.ListStyleType);
            if (name is "disclosure-open" or "disclosure-closed")
                return (LayoutUnit.Zero, new LayoutUnit(CClosureMarkerMarginEm * markerStyle.FontSize));
            return (new LayoutUnit(-1), new LayoutUnit(CUAMarkerMarginEm * markerStyle.FontSize));
        }

        return (LayoutUnit.Zero, LayoutUnit.Zero);
    }

    public static (LayoutUnit, LayoutUnit) InlineMarginsForOutside(ComputedStyle markerStyle, ComputedStyle listItemStyle, LayoutUnit markerInlineSize)
    {
        LayoutUnit marginStart = LayoutUnit.Zero;
        LayoutUnit marginEnd = LayoutUnit.Zero;

        if (!markerStyle.ContentBehavesAsNormal())
        {
            marginStart = -markerInlineSize;
        }
        else if (listItemStyle.GeneratesMarkerImage())
        {
            marginStart = -markerInlineSize - CMarkerPaddingPx;
            marginEnd = new LayoutUnit(CMarkerPaddingPx);
        }
        else
        {
            var category = GetListStyleCategory(listItemStyle);
            switch (category)
            {
                case ListStyleCategory.None:
                    break;
                case ListStyleCategory.Symbol:
                {
                    float ascent = Fonts.LineBoxMetrics.GetFontMetrics(markerStyle).LayoutAscent;
                    string name = ListStyleName(listItemStyle.ListStyleType);
                    LayoutUnit offset = (name is "disclosure-open" or "disclosure-closed")
                        ? new LayoutUnit(markerStyle.FontSize * markerStyle.Zoom * 0.66f)
                        : new LayoutUnit(ascent * 2 / 3);
                    marginStart = -offset - CMarkerPaddingPx - 1;
                    marginEnd = offset + CMarkerPaddingPx + 1 - markerInlineSize;
                    break;
                }
                default:
                    marginStart = -markerInlineSize;
                    break;
            }
        }

        return (marginStart, marginEnd);
    }

    private static string ListStyleName(ListStyleType type) => type switch
    {
        ListStyleType.Disc => "disc",
        ListStyleType.Circle => "circle",
        ListStyleType.Square => "square",
        ListStyleType.Decimal => "decimal",
        ListStyleType.DecimalLeadingZero => "decimal-leading-zero",
        ListStyleType.LowerRoman => "lower-roman",
        ListStyleType.UpperRoman => "upper-roman",
        ListStyleType.LowerAlpha => "lower-alpha",
        ListStyleType.UpperAlpha => "upper-alpha",
        ListStyleType.None => "none",
        _ => "disc",
    };

    // ============ Instance methods ============

    public LayoutObject? ListItem(LayoutObject marker)
    {
        // Walk up the parent chain to find the list item layout object.
        var parent = marker.Parent;
        while (parent != null)
        {
            if (parent.IsLayoutListItem || parent.IsInlineListItem)
                return parent;
            parent = parent.Parent;
        }
        return null;
    }

    private int ListItemValue(LayoutObject listItem)
    {
        if (listItem is LayoutListItem li)
            return li.Value();
        if (listItem is LayoutInlineListItem inlineLi)
            return inlineLi.Value();
        return 1;
    }

    public void ListStyleTypeChanged(LayoutObject marker)
    {
        if (_markerTextType == MarkerTextType.NotText || _markerTextType == MarkerTextType.Unresolved)
            return;
        _markerTextType = MarkerTextType.Unresolved;
        marker.NeedsLayout = true;
    }

    public void CounterStyleChanged(LayoutObject marker)
    {
        if (_markerTextType == MarkerTextType.NotText || _markerTextType == MarkerTextType.Unresolved)
            return;
        _markerTextType = MarkerTextType.Unresolved;
        marker.NeedsLayout = true;
    }

    public void OrdinalValueChanged(LayoutObject marker)
    {
        if (_markerTextType == MarkerTextType.OrdinalValue)
        {
            _markerTextType = MarkerTextType.Unresolved;
            marker.NeedsLayout = true;
        }
    }

    public LayoutObject? GetContentChild(LayoutObject marker)
    {
        return marker.SlowFirstChild();
    }

    public LayoutText? GetTextChild(LayoutObject marker)
    {
        return GetContentChild(marker) as LayoutText;
    }

    public void UpdateMarkerTextIfNeeded(LayoutObject marker)
    {
        if (_markerTextType == MarkerTextType.Unresolved)
            UpdateMarkerText(marker);
    }

    public void UpdateMarkerContentIfNeeded(LayoutObject marker)
    {
        var style = marker.Style;
        if (style == null || !style.ContentBehavesAsNormal())
        {
            _markerTextType = MarkerTextType.NotText;
            return;
        }

        LayoutObject? child = GetContentChild(marker);
        var listItem = ListItem(marker);
        var listItemStyle = listItem?.Style;

        if (listItemStyle != null && listItemStyle.GeneratesMarkerImage())
        {
            if (child is LayoutListMarkerImage)
            {
                _markerTextType = MarkerTextType.NotText;
                return;
            }
            _markerTextType = MarkerTextType.NotText;
            return;
        }

        if (listItemStyle == null || listItemStyle.ListStyleType == ListStyleType.None)
        {
            _markerTextType = MarkerTextType.NotText;
            return;
        }

        if (child is LayoutText)
        {
            _markerTextType = MarkerTextType.Unresolved;
            return;
        }

        _markerTextType = MarkerTextType.Unresolved;
    }

    public LayoutObject? SymbolMarkerLayoutText(LayoutObject marker)
    {
        if (_markerTextType != MarkerTextType.SymbolValue)
            return null;
        return GetContentChild(marker);
    }

    public bool IsMarkerImage(LayoutObject marker)
    {
        var style = marker.Style;
        if (style == null || !style.ContentBehavesAsNormal())
            return false;
        var listItem = ListItem(marker);
        return listItem?.Style?.GeneratesMarkerImage() == true;
    }

    public string MarkerTextWithSuffix(LayoutObject marker)
    {
        var sb = new StringBuilder();
        MarkerTextInternal(marker, sb, MarkerTextFormat.WithPrefixSuffix);
        return sb.ToString();
    }

    public string MarkerTextWithoutSuffix(LayoutObject marker)
    {
        var sb = new StringBuilder();
        MarkerTextInternal(marker, sb, MarkerTextFormat.WithoutPrefixSuffix);
        return sb.ToString();
    }

    public string TextAlternative(LayoutObject marker)
    {
        if (_markerTextType == MarkerTextType.NotText)
        {
            string text = MarkerTextWithSuffix(marker);
            if (!string.IsNullOrEmpty(text))
                return text;

            var textChild = GetContentChild(marker);
            if (textChild is LayoutText layoutText)
                return layoutText.Text;

            return text;
        }

        if (_markerTextType == MarkerTextType.Unresolved)
            return MarkerTextWithSuffix(marker);

        var textChild2 = GetTextChild(marker);
        return textChild2?.Text ?? "";
    }

    private void UpdateMarkerText(LayoutObject marker)
    {
        var text = GetTextChild(marker);
        if (text == null) return;
        var sb = new StringBuilder();
        _markerTextType = MarkerTextInternal(marker, sb, MarkerTextFormat.WithPrefixSuffix);
        text.SetText(sb.ToString());
    }

    private MarkerTextType MarkerTextInternal(LayoutObject marker, StringBuilder text, MarkerTextFormat format)
    {
        var style = marker.Style;
        if (style == null || !style.ContentBehavesAsNormal())
            return MarkerTextType.NotText;

        if (IsMarkerImage(marker))
        {
            if (format == MarkerTextFormat.WithPrefixSuffix)
                text.Append(' ');
            return MarkerTextType.NotText;
        }

        var listItem = ListItem(marker);
        if (listItem == null) return MarkerTextType.NotText;

        var listItemStyle = listItem.Style;
        if (listItemStyle == null) return MarkerTextType.NotText;

        switch (GetListStyleCategory(listItemStyle))
        {
            case ListStyleCategory.None:
                return MarkerTextType.NotText;

            case ListStyleCategory.StaticString:
                text.Append(listItemStyle.ListStyleStringValue());
                return MarkerTextType.Static;

            case ListStyleCategory.Symbol:
            {
                string symbol = GenerateSymbolRepresentation(listItemStyle.ListStyleType, format);
                text.Append(symbol);
                return MarkerTextType.SymbolValue;
            }

            case ListStyleCategory.Language:
            {
                int value = ListItemValue(listItem);
                string representation = GenerateLanguageRepresentation(value, listItemStyle.ListStyleType, format);
                text.Append(representation);
                return MarkerTextType.OrdinalValue;
            }
        }

        return MarkerTextType.NotText;
    }

    private static string GenerateSymbolRepresentation(ListStyleType type, MarkerTextFormat format)
    {
        string symbol = ListMarkerFormatter.MarkerLabel(type, 1, null);

        return format switch
        {
            MarkerTextFormat.WithPrefixSuffix => symbol + " ",
            _ => symbol,
        };
    }

    private static string GenerateLanguageRepresentation(int value, ListStyleType type, MarkerTextFormat format)
    {
        string text = ListMarkerFormatter.MarkerLabel(type, value, null);

        return format switch
        {
            MarkerTextFormat.WithoutPrefixSuffix => text,
            _ => text + " ",
        };
    }

    private static float DisclosureSymbolSize(ComputedStyle style)
    {
        return style.FontSize * style.Zoom * 0.66f;
    }
}

/// <summary>
/// Lightweight unit type for layout calculations. Mirrors LayoutUnit.
/// </summary>
public readonly struct LayoutUnit
{
    public float Value { get; }
    public LayoutUnit(float value) { Value = value; }
    public static readonly LayoutUnit Zero = new(0);
    public static readonly LayoutUnit Max = new(float.MaxValue);

    public static LayoutUnit operator +(LayoutUnit a, LayoutUnit b) => new(a.Value + b.Value);
    public static LayoutUnit operator -(LayoutUnit a, LayoutUnit b) => new(a.Value - b.Value);
    public static LayoutUnit operator -(LayoutUnit a) => new(-a.Value);
    public static LayoutUnit operator *(LayoutUnit a, float b) => new(a.Value * b);
    public static LayoutUnit operator /(LayoutUnit a, float b) => new(a.Value / b);
    public static implicit operator LayoutUnit(float v) => new(v);
    public static implicit operator float(LayoutUnit u) => u.Value;
    public override string ToString() => $"{Value:F1}";
}