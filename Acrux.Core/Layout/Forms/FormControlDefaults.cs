using Acrux.Core.Dom;
using Acrux.Core.Layout.Inline;

namespace Acrux.Core.Layout.Forms;

/// <summary>
/// Default ("widget") size of a form control, used when the author left the size
/// auto. Chrome does not size these from their contents the way it sizes a
/// <div>: a text field is a number of characters wide, a menu-list select is its
/// widest option plus the arrow, a text area is cols x rows. The HTML spec calls
/// these the control's "rendered size"; the constants below were measured against
/// Chrome at dpr 1.25 (see snapshots/css-standard-verify168-form-inline-shrink.html).
/// All returned sizes are border-box.
/// </summary>
internal static class FormControlDefaults
{
    // A text field's width is (size + 4.33) characters of half an em each: the
    // per-character allowance is exactly 0.5em (verified at 13.3333px and 40px),
    // and the remainder is the fixed part Chrome keeps for the caret and the
    // inner text container.
    private const float TextFieldCharEm = 0.5f;
    private const float TextFieldBaseEm = 2.165f;
    private const float DefaultTextFieldSize = 20;

    // The menu-list arrow allowance, measured as the whole inset a closed select keeps
    // beyond its label: Chrome's select has no box padding at all (UA 'padding: 0'), so
    // the 21px it reserves is carried here rather than split with a UA padding
    // (measured: "a" + 21 + 1.6 = 29.6 border-box).
    private const float MenuListArrowPx = 21f;

    // The menu list's vertical widget inset — the counterpart of MenuListArrowPx on the
    // block axis. The reference engine does not express it as padding (computed padding on
    // a <select> is 0), so it cannot come from the UA rule and has to live here.
    private const float MenuListVerticalInsetPx = 2f;

    // A listbox has no arrow but always keeps a 16px classic scrollbar gutter
    // (Chrome: offsetWidth - clientWidth = 16 even when the rows fit). The option's
    // own 2px inset comes from the select's UA padding, so it is not added here.
    private const float ListBoxChromePx = 16f;

    // A <textarea> keeps a classic vertical scrollbar whatever it contains, and the
    // gutter is part of the widget's default width: Chrome measures cols x "0" +
    // padding + border + this. Fitted on 5 points (cols 1/5/10/20/40 = 28/55/88/155/
    // 288 at 13.3333px monospace, i.e. 6.667px per column and a 21.67px constant).
    private const float TextAreaScrollbarPx = 15.67f;

    // checkbox / radio are drawn at a fixed 13x13 regardless of font size, and
    // their author-visible padding/border does not add to that.
    private const float TickSizePx = 13;

    public static bool TryGetDefaultInlineSize(Element el, ComputedStyle style,
        float borderPaddingInline, out float borderBoxWidth)
    {
        borderBoxWidth = 0;
        if (style.Width is not AutoLength)
            return false;

        // 'field-sizing: content' replaces the widget size with the size of the control's
        // own content (measured in the reference engine: value "ab" in Arial 16 gives 17.8px
        // of content, so 25.8 border-box with the UA 2px padding + 2px border; an empty
        // content-sized field is 9px, a placeholder "hello world" 84.48, and 'size=5' is
        // ignored). A checkbox/radio keeps its 13x13 widget whatever the sizing says.
        if (style.FieldSizing == FieldSizingType.Content)
        {
            if (el.TagName is "TEXTAREA" || el.TagName is "SELECT" || el.TagName is "INPUT")
            {
                string type = InputType(el);
                bool ticks = el.TagName is "INPUT" && type is "checkbox" or "radio";
                if (!ticks && (el.TagName != "INPUT" || IsTextLike(type)))
                {
                    borderBoxWidth = ContentInlineSize(el, style)
                        + (el.TagName == "SELECT" && !IsListBox(el) ? MenuListArrowPx : 0f)
                        + borderPaddingInline;
                    return borderBoxWidth > 0;
                }
            }
            return false;
        }

        switch (el.TagName)
        {
            case "SELECT":
            {
                borderBoxWidth = WidestOptionInline(el, style)
                    + (IsListBox(el) ? ListBoxChromePx : MenuListArrowPx) + borderPaddingInline;
                return borderBoxWidth > 0;
            }
            case "TEXTAREA":
            {
                float chars = AttributeCount(el, "cols", 20);
                borderBoxWidth = chars * TextMeasureProxy.MeasureCharacter('0', style)
                    + borderPaddingInline + TextAreaScrollbarPx;
                return borderBoxWidth > 0;
            }
            case "INPUT":
            {
                string type = InputType(el);
                if (type is "checkbox" or "radio")
                {
                    borderBoxWidth = TickSizePx;
                    return true;
                }
                if (!IsTextLike(type))
                    return false;
                float size = Math.Max(1, AttributeCount(el, "size", DefaultTextFieldSize));
                borderBoxWidth = (size * TextFieldCharEm + TextFieldBaseEm) * style.FontSize + borderPaddingInline;
                return true;
            }
            default:
                return false;
        }
    }

    public static bool TryGetDefaultBlockSize(Element el, ComputedStyle style,
        float borderPaddingBlock, out float borderBoxHeight)
    {
        borderBoxHeight = 0;
        if (style.Height is not AutoLength)
            return false;

        // A content-sized text area is as many rows tall as its content has lines (reference
        // engine: <textarea style="field-sizing: content">ab</textarea> is 26px = one line +
        // the UA 2px padding and 1px border, where the default cols/rows widget is 46px).
        if (style.FieldSizing == FieldSizingType.Content && el.TagName == "TEXTAREA")
        {
            borderBoxHeight = ContentLineCount(el) * Fonts.LineBoxMetrics.GetLineHeight(style)
                + borderPaddingBlock;
            return borderBoxHeight > 0;
        }

        switch (el.TagName)
        {
            case "SELECT":
            {
                float rows = IsListBox(el) ? Math.Max(1, AttributeCount(el, "size", el.GetAttribute("multiple") != null ? 4 : 1)) : 1;
                // A closed menu list is 'one line + 4' tall in the reference engine (19px at
                // 13.3333px, 21 at 16, 31 at 24) — but it has no box padding to carry two of
                // those pixels (UA 'padding: 0'), so the widget's own vertical inset is added
                // here. For a listbox the rows really are content and no inset applies.
                float inset = IsListBox(el) ? 0f : MenuListVerticalInsetPx;
                borderBoxHeight = rows * Fonts.LineBoxMetrics.GetLineHeight(style) + borderPaddingBlock + inset;
                return borderBoxHeight > 0;
            }
            case "TEXTAREA":
            {
                float rows = AttributeCount(el, "rows", 2);
                borderBoxHeight = rows * Fonts.LineBoxMetrics.GetLineHeight(style) + borderPaddingBlock;
                return borderBoxHeight > 0;
            }
            case "INPUT":
            {
                string type = InputType(el);
                if (type is "checkbox" or "radio")
                {
                    borderBoxHeight = TickSizePx;
                    return true;
                }
                if (!IsTextLike(type))
                    return false;
                borderBoxHeight = Fonts.LineBoxMetrics.GetLineHeight(style) + borderPaddingBlock;
                return true;
            }
            default:
                return false;
        }
    }

    private static bool IsTextLike(string type) =>
        type is "text" or "password" or "email" or "tel" or "url" or "search" or "number";

    private static bool IsListBox(Element el) =>
        el.GetAttribute("multiple") != null
        || (int.TryParse(el.GetAttribute("size"), out int rows) && rows > 1);

    private static string InputType(Element el) =>
        (el.GetAttribute("type") ?? "text").ToLowerInvariant();

    private static float AttributeCount(Element el, string name, float fallback) =>
        int.TryParse(el.GetAttribute(name), out int value) && value > 0 ? value : fallback;

    /// <summary>Inline size of the widest option label, in the select's own font.</summary>
    /// <summary>The inline size a 'field-sizing: content' control measures: the widest line of
    /// its content text. Trailing spaces are dropped (the reference engine sizes an input on
    /// its value, and a field whose value is only spaces is as narrow as an empty one).</summary>
    private static float ContentInlineSize(Element el, ComputedStyle style)
    {
        float widest = 0;
        foreach (var line in ContentLines(el))
            widest = Math.Max(widest, TextMeasureProxy.MeasureText(line.TrimEnd(), style));
        return widest;
    }

    /// <summary>The number of lines a content-sized text area shows: at least one, whatever
    /// its content holds.</summary>
    private static int ContentLineCount(Element el) => Math.Max(1, ContentLines(el).Count);

    private static List<string> ContentLines(Element el)
    {
        string text = el.TagName switch
        {
            // The value wins over the placeholder; an empty value falls back to it (measured:
            // a content-sized field with placeholder "hello world" is 84.48px wide).
            "INPUT" => el.GetAttribute("value") ?? el.GetAttribute("placeholder") ?? "",
            "TEXTAREA" => el.TextContent ?? "",
            "SELECT" => SelectedOptionText(el),
            _ => "",
        };
        return text.Split('\n').ToList();
    }

    /// <summary>A menu-list select sized to content is as wide as its <em>selected</em> option
    /// (the one shown in the button), not the widest option in the list.</summary>
    private static string SelectedOptionText(Element el)
    {
        string first = "";
        foreach (var child in el.Children)
        {
            if (child is not Element optionEl) continue;
            if (optionEl.TagName != "OPTION") continue;
            string text = optionEl.TextContent ?? optionEl.GetAttribute("label") ?? "";
            if (first.Length == 0) first = text;
            if (optionEl.GetAttribute("selected") != null)
                return text;
        }
        return first;
    }

    private static float WidestOptionInline(Element el, ComputedStyle style)
    {
        float widest = 0;
        foreach (var child in el.Children)
        {
            if (child is not Element optionEl) continue;
            string text = optionEl.TagName is "OPTION" or "OPTGROUP"
                ? (optionEl.GetAttribute("label") ?? optionEl.TextContent ?? "")
                : optionEl.TextContent ?? "";
            float width = TextMeasureProxy.MeasureText(text.Trim(), style);
            if (width > widest)
                widest = width;
        }
        return widest;
    }
}
