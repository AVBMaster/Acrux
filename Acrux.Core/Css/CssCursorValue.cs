namespace Acrux.Core.Css;

/// <summary>
/// The value of <c>cursor</c> (CSS User Interface 4 §9): a list of addresses the platform may draw,
/// each with its own hot spot, and one keyword at the end for the case where none of them can be
/// used.
/// <para>
/// The keyword is not a decoration on the list — it is what the declaration means when the picture
/// cannot be had, so a list that ends with an address names no cursor at all and a hot spot written
/// as one number is not a point. Measured on the reference engine: <c>url(a.png) 4 6, pointer</c> and
/// <c>url(a.png), pointer</c> are cursors, while <c>url(a.png) 4 6</c>, <c>url(a.png) 4</c> and
/// <c>pointer, url(a.png)</c> make no declaration.
/// </para>
/// </summary>
public static class CssCursorValue
{
    /// <summary>The keywords a cursor may fall back on. A name outside this list is not a cursor
    /// the platform knows, and the address list in front of it then means nothing either.</summary>
    private static readonly HashSet<string> CursorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "alias", "all-scroll", "auto", "cell", "col-resize", "context-menu", "copy", "crosshair",
        "default", "e-resize", "grab", "grabbing", "help", "move", "n-resize", "ne-resize",
        "nesw-resize", "no-drop", "none", "not-allowed", "nw-resize", "nwse-resize",
        "pointer", "progress", "row-resize", "s-resize", "se-resize", "sw-resize", "text",
        "vertical-text", "wait", "w-resize", "zoom-in", "zoom-out",
    };

    public static bool IsValid(string text)
    {
        var items = SplitCommas(text);
        if (items.Count == 0) return false;

        for (int i = 0; i < items.Count; i++)
        {
            var parts = SplitTokens(items[i]);
            if (parts.Count == 0) return false;
            bool last = i == items.Count - 1;
            if (parts[0].StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                // An address carries a hot spot of two numbers or none of them, and any address
                // that is not the last item is followed by another, so only the last one needs the
                // keyword — which it does not have here.
                if (parts.Count is not (1 or 3)) return false;
                if (parts.Count == 3 && (!IsNumber(parts[1]) || !IsNumber(parts[2]))) return false;
                if (last) return false;
                continue;
            }
            if (!CursorKeywords.Contains(parts[0]) || parts.Count != 1) return false;
            // The keyword closes the list, so nothing may follow it.
            if (!last) return false;
        }
        return true;
    }

    private static bool IsNumber(string token) =>
        double.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    private static List<string> SplitCommas(string text)
    {
        var parts = new List<string>();
        int depth = 0;
        char quote = '\0';
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < text.Length) i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts.Where(p => p.Trim().Length > 0).ToList();
    }

    /// <summary>Split one item of the list at its top-level whitespace, so an address with a space
    /// in its quoted spelling stays the one token it is.</summary>
    private static List<string> SplitTokens(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;
        char quote = '\0';
        foreach (char c in text)
        {
            if (quote != '\0')
            {
                current.Append(c);
                if (c != '\\') { if (c == quote) quote = '\0'; }
                continue;
            }
            if (c is '"' or '\'') { quote = c; current.Append(c); continue; }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
