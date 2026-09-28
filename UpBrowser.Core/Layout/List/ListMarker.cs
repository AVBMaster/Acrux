using System.Text;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.List;

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

    /// <summary>Generate the marker text for a list item at the given index (1-based).</summary>
    public static string MarkerText(ListStyleType type, int itemIndex)
    {
        switch (type)
        {
            case ListStyleType.None:
                return "";
            case ListStyleType.Decimal:
                return itemIndex.ToString() + ".";
            case ListStyleType.DecimalLeadingZero:
                return itemIndex.ToString("D2") + ".";
            case ListStyleType.LowerRoman:
                return ToRoman(itemIndex) + ".";
            case ListStyleType.UpperRoman:
                return ToRoman(itemIndex).ToUpperInvariant() + ".";
            case ListStyleType.LowerAlpha:
                return ToAlpha(itemIndex) + ".";
            case ListStyleType.UpperAlpha:
                return ToAlpha(itemIndex).ToUpperInvariant() + ".";
            case ListStyleType.Disc:
            case ListStyleType.Circle:
            case ListStyleType.Square:
                return "\u2022";
            default:
                return "\u2022";
        }
    }

    /// <summary>Space between the marker glyph and the item's content.</summary>
    public const float MarkerGap = 8f;

    /// <summary>
    /// list-style-position: inside puts the marker in the first line box, so the
    /// text starts after it (CSS 2.1 §10.7.1).
    /// </summary>
    public static bool GeneratesInsideMarker(ComputedStyle style) =>
        style.Display == DisplayType.ListItem &&
        style.ListStylePosition == ListStylePosition.Inside &&
        style.ListStyleType != ListStyleType.None;

    public static float InsideMarkerIndent(ComputedStyle style) =>
        GeneratesInsideMarker(style)
            ? MarkerWidth(style.ListStyleType, ListStylePosition.Inside, style.FontSize) + MarkerGap
            : 0;

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
        string symbol = type switch
        {
            ListStyleType.Disc => "\u2022",
            ListStyleType.Circle => "\u25E6",
            ListStyleType.Square => "\u25AA",
            _ => "\u2022",
        };

        return format switch
        {
            MarkerTextFormat.WithPrefixSuffix => symbol + " ",
            MarkerTextFormat.WithoutPrefixSuffix => symbol,
            MarkerTextFormat.AlternativeText => symbol,
            _ => symbol,
        };
    }

    private static string GenerateLanguageRepresentation(int value, ListStyleType type, MarkerTextFormat format)
    {
        string text = type switch
        {
            ListStyleType.Decimal => value.ToString(),
            ListStyleType.DecimalLeadingZero => value.ToString("D2"),
            ListStyleType.LowerRoman => ToRoman(value),
            ListStyleType.UpperRoman => ToRoman(value).ToUpperInvariant(),
            ListStyleType.LowerAlpha => ToAlpha(value),
            ListStyleType.UpperAlpha => ToAlpha(value).ToUpperInvariant(),
            _ => value.ToString(),
        };

        return format switch
        {
            MarkerTextFormat.WithPrefixSuffix => text + ".",
            MarkerTextFormat.WithoutPrefixSuffix => text,
            MarkerTextFormat.AlternativeText => text + ".",
            _ => text,
        };
    }

    // ============ Roman/AIpha helpers ============

    public static string ToAlpha(int value)
    {
        if (value <= 0) return "";
        var sb = new StringBuilder();
        while (value > 0)
        {
            value--;
            sb.Insert(0, (char)('a' + (value % 26)));
            value /= 26;
        }
        return sb.ToString();
    }

    public static string ToRoman(int value)
    {
        if (value <= 0 || value >= 4000) return value.ToString();
        var sb = new StringBuilder();
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var numerals = new[] { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
        for (int i = 0; i < values.Length; i++)
        {
            while (value >= values[i])
            {
                sb.Append(numerals[i]);
                value -= values[i];
            }
        }
        return sb.ToString();
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