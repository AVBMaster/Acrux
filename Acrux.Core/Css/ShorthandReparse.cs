using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// The reference engine keeps a shorthand as one declaration and lets a page read it as the
/// longhands it carries, so the two shapes are the same value seen from either end. That is what
/// makes a completed set of longhands fold back: a block that ends up holding
/// <c>margin-top/-right/-bottom/-left</c> — however it came to hold them, from four writes, from an
/// authored <c>style</c> attribute, or from a part the page removed from a shorthand and then wrote
/// again — prints <c>margin: 1px 2px 3px 4px</c> (all measured, snapshots/_probe_b281_remerge*.html;
/// the same fold happens to a block authored in HTML, where the <c>style</c> attribute keeps the
/// characters the page wrote and only the CSSOM text folds).
///
/// <para>Two shapes are deliberately NOT folded here, both measured on the reference engine:
/// <c>columns</c> and <c>font</c> stay as the longhands they were written as. A shorthand whose part
/// list has not been measured (<see cref="ShorthandExpander.CssomLonghands"/>) is not folded either,
/// because folding needs the list the engine enumerates, not the list this engine expands to.</para>
/// </summary>
public static class ShorthandReparse
{
    /// <summary>The shorthands a completed set of longhands folds back into, and how each is spelled
    /// from its parts (see <see cref="MergeText"/>). Every name here is a fold the reference engine
    /// was shown to make.</summary>
    private static readonly HashSet<string> Foldable = new(StringComparer.OrdinalIgnoreCase)
    {
        "margin", "padding", "inset", "border-width", "border-style", "border-color",
        "margin-block", "margin-inline", "padding-block", "padding-inline",
        "inset-block", "inset-inline", "gap", "place-items", "place-content", "place-self",
        "overflow", "border-radius", "outline", "list-style", "flex", "background-position",
        "text-decoration",
    };

    /// <summary>Fold <paramref name="longhand"/>'s shorthand back into one declaration when the block
    /// now carries every part of it. Returns whether a fold happened.</summary>
    public static bool TryFold(Element element, string longhand)
    {
        foreach (var shorthand in ShorthandExpander.ShorthandsFor(longhand))
        {
            if (!Foldable.Contains(shorthand)) continue;
            if (ShorthandExpander.CssomLonghands(shorthand) is not { } parts) continue;
            var values = new string[parts.Length];
            bool complete = true;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!element.Style.TryGetValue(parts[i], out var value) || string.IsNullOrEmpty(value))
                { complete = false; break; }
                values[i] = value!;
            }
            if (!complete) continue;
            var text = MergeText(shorthand, values);
            if (string.IsNullOrEmpty(text)) continue;
            ReplaceParts(element, parts, shorthand, text!);
            return true;
        }
        return false;
    }

    /// <summary>Put the one shorthand in the place the first of its parts held, and take the parts out
    /// wherever they stood (measured: a block written 'margin-left, margin-right, margin-top,
    /// margin-bottom' prints the shorthand once, and a <c>color</c> that sat among them keeps its own
    /// place after it).</summary>
    private static void ReplaceParts(Element element, string[] parts, string shorthand, string text)
    {
        var entries = element.Style.ToList();
        int insertAt = entries.FindIndex(e => Array.IndexOf(parts, e.Key) >= 0);
        element.Style.Clear();
        bool written = false;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            bool isPart = Array.IndexOf(parts, entry.Key) >= 0
                || entry.Key.Equals(shorthand, StringComparison.OrdinalIgnoreCase);
            if (isPart)
            {
                if (!written && i == insertAt)
                {
                    element.Style[shorthand] = text;
                    written = true;
                }
                continue;
            }
            element.Style[entry.Key] = entry.Value;
        }
        if (!written) element.Style[shorthand] = text;
    }

    /// <summary>The text the reference engine prints for a shorthand its parts were folded into. Each
    /// shape here is a measured print: the four-side family collapses to the shortest list that says
    /// the same four edges, the two-term shorthands collapse when both terms agree, <c>border-radius</c>
    /// keeps its <c>horizontal / vertical</c> split, and <c>text-decoration</c> is re-spelled by the
    /// printer that says the parts the page left at their initial.</summary>
    private static string? MergeText(string shorthand, string[] values) => shorthand.ToLowerInvariant() switch
    {
        "margin" or "padding" or "inset" or "border-width" or "border-style" or "border-color"
            => CollapseFourSides(values),
        "border-radius" => BorderRadiusText(values),
        "margin-block" or "margin-inline" or "padding-block" or "padding-inline"
            or "inset-block" or "inset-inline" or "gap" or "place-items" or "place-content"
            or "place-self" or "overflow"
            => values[0].Equals(values[1], StringComparison.OrdinalIgnoreCase)
                ? values[0] : values[0] + " " + values[1],
        "background-position" => ShorthandExpander.BackgroundPositionPairText(values[0], values[1]),
        "outline" or "list-style" or "flex" => string.Join(" ", values),
        "text-decoration" => CssValueText.Canonicalize(string.Join(" ", values), "text-decoration"),
        _ => null,
    };

    /// <summary>The shortest text that says the same four sides: all one value, or the
    /// top/bottom-left/right pair, or three with the left repeating the top (CSS Values §3.1).
    /// (measured: '7px 2px 1px 2px' reads 'margin: 7px 2px 1px' and four equal edges read one).</summary>
    public static string CollapseFourSides(IReadOnlyList<string> values)
    {
        bool Same(string x, string y) => x.Equals(y, StringComparison.OrdinalIgnoreCase);
        if (values.Count != 4) return string.Join(" ", values);
        if (Same(values[0], values[1]) && Same(values[0], values[2]) && Same(values[0], values[3]))
            return values[0];
        if (Same(values[0], values[2]) && Same(values[1], values[3]))
            return values[0] + " " + values[1];
        if (Same(values[1], values[3]))
            return values[0] + " " + values[1] + " " + values[2];
        return string.Join(" ", values);
    }

    /// <summary>Four corners as the one <c>border-radius</c> the engine prints: the horizontal radii
    /// collapsed, then the vertical ones after a slash only when the two differ (measured: corners
    /// '1px 2px', '3px 4px', '5px 6px', '7px 8px' read '1px 3px 5px 7px / 2px 4px 6px 8px' and corners
    /// whose halves agree read 'border-radius: 1px 2px').</summary>
    private static string? BorderRadiusText(string[] corners)
    {
        var horizontal = new string[4];
        var vertical = new string[4];
        bool split = false;
        for (int i = 0; i < corners.Length; i++)
        {
            var words = corners[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is 0 or > 2) return null;
            horizontal[i] = words[0];
            vertical[i] = words.Length == 2 ? words[1] : words[0];
            split |= !words[0].Equals(vertical[i], StringComparison.OrdinalIgnoreCase);
        }
        var h = CollapseFourSides(horizontal);
        return split ? h + " / " + CollapseFourSides(vertical) : h;
    }

    /// <summary>Replace one stored shorthand with the longhands it carries, keeping its place in
    /// the block and dropping the part the page named. The values are the ones the shorthand
    /// stands for, not the characters that were written, because from here on the block says them
    /// as separate declarations.</summary>
    public static bool DemoteShorthand(Element element, string shorthand, string value, string? drop)
    {
        var parts = ShorthandExpander.ExpandProperty(shorthand, value);
        var ordered = ShorthandExpander.CssomLonghands(shorthand);
        if (parts.Count == 0 || ordered == null) return false;
        var entries = element.Style.ToList();
        element.Style.Clear();
        foreach (var entry in entries)
        {
            if (!entry.Key.Equals(shorthand, StringComparison.OrdinalIgnoreCase))
            {
                element.Style[entry.Key] = entry.Value;
                continue;
            }
            foreach (var name in ordered)
            {
                if (drop != null && name.Equals(drop, StringComparison.OrdinalIgnoreCase)) continue;
                if (parts.TryGetValue(name, out var part)) element.Style[name] = part;
            }
        }
        return true;
    }

    /// <summary>Break apart every stored shorthand that covers <paramref name="longhand"/>, so the
    /// longhand the page is about to write can stand beside the parts its shorthand carried. Measured:
    /// a block that says 'margin: 1px 2px 3px 4px' and is then given 'margin-top: 9px' ends as
    /// 'margin: 9px 2px 3px 4px' — the write reaches through the shorthand and the fold puts it
    /// back together.</summary>
    public static void DemoteCoveringShorthands(Element element, string longhand)
    {
        foreach (var shorthand in ShorthandExpander.ShorthandsFor(longhand).ToArray())
        {
            if (element.Style.TryGetValue(shorthand, out var text) && !string.IsNullOrEmpty(text))
                DemoteShorthand(element, shorthand, text!, drop: longhand);
        }
    }
}
