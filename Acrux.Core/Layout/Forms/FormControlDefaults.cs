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

    // The menu-list arrow allowance. Chrome's select has no author-visible padding
    // (it insets the label inside the widget); ours insets with real padding, so
    // the allowance is its 21px minus the 4px the UA padding already contributes
    // (measured: "a" + 17 + 4 + 1.6 = 29.3 vs Chrome's 29.6).
    private const float MenuListArrowPx = 17f;

    // A listbox has no arrow but always keeps a 16px classic scrollbar gutter
    // (Chrome: offsetWidth - clientWidth = 16 even when the rows fit). The option's
    // own 2px inset comes from the select's UA padding, so it is not added here.
    private const float ListBoxChromePx = 16f;

    // checkbox / radio are drawn at a fixed 13x13 regardless of font size, and
    // their author-visible padding/border does not add to that.
    private const float TickSizePx = 13;

    public static bool TryGetDefaultInlineSize(Element el, ComputedStyle style,
        float borderPaddingInline, out float borderBoxWidth)
    {
        borderBoxWidth = 0;
        if (style.Width is not AutoLength)
            return false;

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
                borderBoxWidth = chars * TextMeasureProxy.MeasureCharacter('0', style) + borderPaddingInline;
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

        switch (el.TagName)
        {
            case "SELECT":
            {
                float rows = IsListBox(el) ? Math.Max(1, AttributeCount(el, "size", el.GetAttribute("multiple") != null ? 4 : 1)) : 1;
                borderBoxHeight = rows * Fonts.LineBoxMetrics.GetLineHeight(style) + borderPaddingBlock;
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
