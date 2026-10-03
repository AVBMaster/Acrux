using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout.List;

/// <summary>
/// HTML list numbering (HTML §the ol element, CSS Lists 3 §4.2).
///
/// 'start' and 'reversed' only apply to 'ol'; a 'ul' always counts up from one.
/// A pinned item (one whose 'value' attribute parses as an integer) reports that
/// value and leaves the sequential counter untouched, so the items after it keep
/// numbering as though the pinned item had not been there.
/// </summary>
public static class ListItemNumbering
{
    public static int Ordinal(Element item)
    {
        var parent = item.ParentElement;
        if (parent == null) return 1;

        var items = new List<Element>();
        foreach (var child in parent.Children)
            if (child is Element el && el.ComputedStyle?.Display == DisplayType.ListItem)
                items.Add(el);

        int selfIndex = items.IndexOf(item);
        if (selfIndex < 0) return 1;

        bool ordered = parent.TagName.Equals("OL", StringComparison.OrdinalIgnoreCase);
        bool reversed = ordered && parent.HasAttribute("reversed");
        int step = reversed ? -1 : 1;
        int running = (ordered ? ParseInt(parent.GetAttribute("start")) : null)
                      ?? (reversed ? items.Count : 1);

        for (int i = 0; i < items.Count; i++)
        {
            int? pinned = ParseInt(items[i].GetAttribute("value"));
            if (pinned.HasValue)
            {
                if (i == selfIndex) return pinned.Value;
                // HTML §the-ol-element: a pinned value also re-anchors the counter, so
                // the FOLLOWING item is one step away from it, not from wherever the
                // sequence had been ('value=9' then two plain items counts 9, 10, 11).
                running = pinned.Value + step;
                continue;
            }
            if (i == selfIndex) return running;
            running += step;
        }
        return 1;
    }

    /// <summary>'value'/'start' are integers; anything else behaves as absent.
    /// Leading and trailing space is allowed, thousands separators are not.</summary>
    private static int? ParseInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
