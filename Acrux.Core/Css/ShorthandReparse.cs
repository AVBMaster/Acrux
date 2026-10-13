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
        "text-decoration", "text-emphasis", "white-space", "text-wrap", "container",
        "contain-intrinsic-size", "scroll-margin", "scroll-padding", "grid-area", "grid-row",
        "grid-column", "grid-template", "border", "border-image", "transition", "background",
        "flex-flow",
    };

    /// <summary>The foldable shorthands, biggest first: a block that holds all seventeen parts of
    /// <c>border</c> folds into <c>border</c>, while a block that holds only the four widths folds
    /// into <c>border-width</c>. Iterating the set itself gives a hash order, and the two are
    /// different declarations.</summary>
    private static readonly string[] FoldOrder = Foldable
        .OrderByDescending(name => ShorthandExpander.CssomLonghands(name)?.Length ?? 0)
        .ThenBy(name => FoldTieBreak(name))
        .ToArray();

    /// <summary>The order between two shorthands that cover the same NUMBER of parts. The modular
    /// white-space triple is the case: 'white-space-collapse' and 'text-wrap-mode' make
    /// 'white-space' and 'text-wrap-mode' and 'text-wrap-style' make 'text-wrap', and a block that
    /// holds all three axes folds the white-space pair first, leaving the style as its own
    /// declaration (measured: 'white-space: normal; text-wrap-style: balance').</summary>
    private static int FoldTieBreak(string name) => name.ToLowerInvariant() switch
    {
        "white-space" => 0,
        "text-wrap" => 1,
        _ => 2,
    };

    /// <summary>Fold <paramref name="longhand"/>'s shorthand back into one declaration when the block
    /// now carries every part of it. Returns whether a fold happened.
    ///
    /// The walk goes over the foldable shorthands themselves and asks each one whether it covers the
    /// longhand, because the parts it covers ARE <see cref="ShorthandExpander.CssomLonghands"/> — the
    /// list the block enumerates. Reading the relation off a second list (the expansion's own
    /// controlled-longhand table) silently missed every part that table did not carry: a block whose
    /// last write was 'transition-behavior' or 'border-image-slice' folded nothing at all.</summary>
    public static bool TryFold(Element element, string longhand)
    {
        foreach (var shorthand in FoldOrder)
        {
            if (ShorthandExpander.CssomLonghands(shorthand) is not { } parts) continue;
            if (Array.FindIndex(parts, p => p.Equals(longhand, StringComparison.OrdinalIgnoreCase)) < 0)
                continue;
            // A part that already lives inside a DIFFERENT stored shorthand is not this shorthand's
            // to take: 'text-wrap-mode' belongs to the 'white-space' the block folded first, and
            // claiming it for 'text-wrap' would enumerate the axis twice and lose the word
            // (measured: the block reads 'white-space: normal; text-wrap-style: balance'). A smaller
            // shorthand the candidate swallows whole — 'background-position' inside 'background' —
            // is fine, and ReplaceParts takes it out.
            if (PartLivesInOtherShorthand(element, parts, shorthand)) continue;
            var values = new string[parts.Length];
            bool complete = true;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!TryGetPart(element, parts[i], out var value))
                { complete = false; break; }
                values[i] = value!;
            }
            if (!complete) continue;
            var text = MergeText(shorthand, parts, values);
            if (string.IsNullOrEmpty(text)) continue;
            ReplaceParts(element, parts, shorthand, text!);
            return true;
        }
        return false;
    }

    private static bool PartLivesInOtherShorthand(Element element, string[] parts, string shorthand)
    {
        foreach (var entry in element.Style)
        {
            if (entry.Key.Equals(shorthand, StringComparison.OrdinalIgnoreCase)) continue;
            if (ShorthandExpander.CssomLonghands(entry.Key) is not { } other) continue;
            bool shares = other.Any(c => Array.IndexOf(parts, c) >= 0);
            if (!shares) continue;
            if (!other.All(c => Array.IndexOf(parts, c) >= 0)) return true;
        }
        return false;
    }

    /// <summary>The value the block holds for one longhand — directly, or through a smaller shorthand
    /// that already folded and carries it. A block whose nine background parts were written one by one
    /// folds its two position axes into <c>background-position</c> on the way, and without reading
    /// through that the nine-part <c>background</c> would never see its own parts again
    /// (measured: the reference engine prints the whole layer for exactly that block).</summary>
    private static bool TryGetPart(Element element, string longhand, out string? value)
    {
        if (element.Style.TryGetValue(longhand, out value) && !string.IsNullOrEmpty(value)) return true;
        foreach (var entry in element.Style)
        {
            if (string.IsNullOrEmpty(entry.Value)) continue;
            if (ShorthandExpander.CssomLonghands(entry.Key) is not { } covered) continue;
            if (Array.FindIndex(covered, c => c.Equals(longhand, StringComparison.OrdinalIgnoreCase)) < 0)
                continue;
            var parts = ShorthandExpander.ExpandProperty(entry.Key, entry.Value);
            if (parts.TryGetValue(longhand, out var found) && !string.IsNullOrEmpty(found))
            {
                value = found;
                return true;
            }
        }
        value = null;
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
                || entry.Key.Equals(shorthand, StringComparison.OrdinalIgnoreCase)
                // A smaller shorthand the fold swallowed goes with it: the block that had
                // 'border-width' and the other three groups cannot keep them beside the one
                // 'border' they are already inside (measured: seventeen parts read back as
                // 'border: 1px solid red' and nothing else).
                || (ShorthandExpander.CssomLonghands(entry.Key) is { } covered
                    && covered.All(c => Array.IndexOf(parts, c) >= 0));
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
    private static string? MergeText(string shorthand, string[] parts, string[] values)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < parts.Length; i++) byName[parts[i]] = values[i];
        return shorthand.ToLowerInvariant() switch
        {
            "margin" or "padding" or "inset" or "border-width" or "border-style" or "border-color"
                or "scroll-margin" or "scroll-padding"
                => CollapseFourSides(values),
            "border-radius" => BorderRadiusText(values),
            "margin-block" or "margin-inline" or "padding-block" or "padding-inline"
                or "inset-block" or "inset-inline" or "gap" or "place-items" or "place-content"
                or "place-self" or "overflow" or "contain-intrinsic-size"
                => values[0].Equals(values[1], StringComparison.OrdinalIgnoreCase)
                    ? values[0] : values[0] + " " + values[1],
            "background-position" => ShorthandExpander.BackgroundPositionPairText(values[0], values[1]),
            "outline" or "flex" => string.Join(" ", values),
            // The marker list is written position, image, type — which is not the order the block
            // enumerates the three in (measured: 'list-style: inside none square').
            "list-style" => byName["list-style-position"] + " " + byName["list-style-image"]
                + " " + byName["list-style-type"],
            // The pair the two axes say, printed by the shorthand's own printer — 'row' + 'wrap' is
            // 'wrap', because 'row' is the direction the pair defaults to (measured).
            "flex-flow" => CssValueText.Canonicalize(values[0] + " " + values[1], "flex-flow"),
            // A grid line pair drops an 'auto' end, and four lines drop every trailing 'auto'
            // (measured: 'auto / 3', '1 / span 2', '1 / 2 / 3 / 4', and four autos read 'auto').
            "grid-row" or "grid-column"
                => CssValueText.Canonicalize(values[0] + " / " + values[1], "grid-row"),
            "grid-area" => GridAreaMerge(byName),
            // 'grid-template' is only folded for the one shape that was measured: three 'none's say
            // 'none'. Anything else keeps the three longhands, because the reference engine's print
            // of a real track list was not measured here and an invented one is worse than none.
            "grid-template" => values.All(v => v.Equals("none", StringComparison.OrdinalIgnoreCase))
                ? "none" : null,
            // The mark and its colour, the mode and its style, the name and its type: each pair is
            // said again by the printer that already says the authored shorthand, so the fold and a
            // written-out declaration cannot disagree about the order of the parts.
            "text-emphasis" => CssValueText.Canonicalize(string.Join(" ", values), "text-emphasis"),
            "text-wrap" => CssValueText.Canonicalize(string.Join(" ", values), "text-wrap"),
            "container" => CssValueText.Canonicalize(values[0] + " / " + values[1], "container"),
            "text-decoration" => CssValueText.Canonicalize(string.Join(" ", values), "text-decoration"),
            "white-space" => WhiteSpaceText(byName),
            "border" => BorderText(byName),
            "border-image" => CssValueText.Canonicalize(
                byName["border-image-source"] + " " + byName["border-image-slice"]
                + " / " + byName["border-image-width"] + " / " + byName["border-image-outset"]
                + " " + byName["border-image-repeat"], "border-image"),
            "transition" => LayeredText(byName, new[]
            {
                "transition-property", "transition-duration", "transition-timing-function",
                "transition-delay", "transition-behavior",
            }, "transition"),
            "background" => LayeredText(byName, new[]
            {
                "background-image", "background-position-x", "background-position-y",
                "background-size", "background-repeat", "background-attachment",
                "background-origin", "background-clip", "background-color",
            }, "background"),
            _ => null,
        };
    }

    /// <summary>The four grid lines as the one <c>grid-area</c>: a name put on all four lines is the
    /// single word it was written as, while four equal NUMBERS keep the list, and every trailing
    /// <c>auto</c> is what an omitted line means (measured: 'main' reads 'main', '1 / 1 / 1 / 1'
    /// reads as written, and four autos read 'auto'). The same two rules the computed surface uses,
    /// reached through the same helpers so the fold cannot spell an area differently.</summary>
    private static string GridAreaMerge(Dictionary<string, string> byName)
    {
        var lines = new[]
        {
            byName["grid-row-start"], byName["grid-column-start"],
            byName["grid-row-end"], byName["grid-column-end"],
        };
        if (lines[0] == lines[1] && lines[1] == lines[2] && lines[2] == lines[3]
            && ShorthandExpander.NamesAnArea(lines[0])) return lines[0];
        return CssValueText.Canonicalize(string.Join(" / ", lines), "grid-area");
    }

    /// <summary>The two white-space axes as the word they say (CSS Text 4 §1): the fold has to
    /// go the other way from the expansion, and only for the pair the shorthand can actually hold —
    /// a 'text-wrap-style' beside them stays its own declaration, because 'white-space' has no slot
    /// for it (measured: collapse + wrap reads 'normal', preserve + nowrap 'pre', preserve-breaks +
    /// wrap 'pre-line', and with a balance style the block reads
    /// 'white-space: normal; text-wrap-style: balance').</summary>
    private static string? WhiteSpaceText(Dictionary<string, string> byName)
    {
        var collapse = byName.GetValueOrDefault("white-space-collapse", "");
        var mode = byName.GetValueOrDefault("text-wrap-mode", "");
        return (collapse, mode) switch
        {
            ("collapse", "wrap") => "normal",
            ("collapse", "nowrap") => "nowrap",
            ("preserve", "nowrap") => "pre",
            ("preserve", "wrap") => "pre-wrap",
            ("preserve-breaks", "wrap") => "pre-line",
            ("break-spaces", "wrap") => "break-spaces",
            _ => null,
        };
    }

    /// <summary>The seventeen parts as the one <c>border</c> the reference engine prints them in —
    /// but only when they say a single border. Four different widths are not one value, and a
    /// <c>border-image</c> that was actually written is a second shorthand the block keeps
    /// (measured: an even 1px/solid/red with an untouched border-image reads 'border: 1px solid red',
    /// while 'border-image-slice: 50%' reads 'border-width: 1px; border-style: solid;
    /// border-color: red; border-image: none 50% / 1 / 0 stretch').</summary>
    private static string? BorderText(Dictionary<string, string> byName)
    {
        string[] groups = { "width", "style", "color" };
        var terms = new List<string>(3);
        foreach (var group in groups)
        {
            var four = new[]
            {
                byName.GetValueOrDefault("border-top-" + group, ""),
                byName.GetValueOrDefault("border-right-" + group, ""),
                byName.GetValueOrDefault("border-bottom-" + group, ""),
                byName.GetValueOrDefault("border-left-" + group, ""),
            };
            if (!four.All(v => v.Equals(four[0], StringComparison.OrdinalIgnoreCase))) return null;
            terms.Add(four[0]);
        }
        string[] imageInitial =
        {
            byName.GetValueOrDefault("border-image-source", "") == "none" ? "none" : "written",
            byName.GetValueOrDefault("border-image-slice", "") == "100%" ? "none" : "written",
            byName.GetValueOrDefault("border-image-width", "") == "1" ? "none" : "written",
            byName.GetValueOrDefault("border-image-outset", "") == "0" ? "none" : "written",
            byName.GetValueOrDefault("border-image-repeat", "") == "stretch" ? "none" : "written",
        };
        if (imageInitial.Any(v => v != "none")) return null;
        return CssValueText.Canonicalize(string.Join(" ", terms), "border");
    }

    /// <summary>A list-valued shorthand whose parts are each a comma-separated list of the same
    /// length — the background layers, the transition list. The fold walks the layers and says each
    /// one in the order the shorthand writes it, then lets that shorthand's own printer put the
    /// parts in its canonical order (measured: two background layers read
    /// 'url("a.png") 0% 0% / auto no-repeat … , url("b.png") 50% 50% / auto repeat … red', and two
    /// transitions read 'color 1s, opacity 2s linear').</summary>
    private static string? LayeredText(Dictionary<string, string> byName, string[] names, string shorthand)
    {
        var lists = new List<string[]>();
        foreach (var name in names)
        {
            var value = byName.GetValueOrDefault(name, "");
            var items = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(value, ',')
                .Select(item => item.Trim()).ToArray();
            if (items.Length == 0) items = new[] { "" };
            lists.Add(items);
        }
        int count = lists.Max(l => l.Length);
        if (count <= 0) return null;
        // Every list is either one value shared by all the layers or exactly as long as they are;
        // anything else is a value this fold does not know how to pair up, so it says nothing.
        foreach (var list in lists)
            if (list.Length != 1 && list.Length != count) return null;
        if (shorthand == "background")
        {
            // The two position axes are one component of the layer, printed as a pair.
            var x = lists[names.ToList().IndexOf("background-position-x")];
            var y = lists[names.ToList().IndexOf("background-position-y")];
            var pair = new string[count];
            for (int i = 0; i < count; i++)
                pair[i] = ShorthandExpander.BackgroundPositionPairText(
                    x.Length == 1 ? x[0] : x[i], y.Length == 1 ? y[0] : y[i]);
            lists[names.ToList().IndexOf("background-position-x")] = pair;
            // The second axis is inside the pair now; an empty entry is what the word loop below
            // skips, where a null one would be read as a value.
            lists[names.ToList().IndexOf("background-position-y")] =
                System.Linq.Enumerable.Repeat("", count).ToArray();
        }
        var layers = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var words = new List<string>();
            for (int n = 0; n < names.Length; n++)
            {
                var value = lists[n][lists[n].Length == 1 ? 0 : i];
                if (value.Length == 0) continue;
                // A transition says only what it changes: the easing, the delay and the behaviour
                // the page left at their initial are not repeated back (measured: five parts written
                // one by one read 'transition: color 1s', and a second layer keeps its own
                // 'linear' — 'color 1s, opacity 2s linear').
                if (shorthand == "transition" && value.Equals(TransitionInitial(names[n]),
                        StringComparison.OrdinalIgnoreCase)) continue;
                // The size is what the slash belongs to ('auto' on its own would read as a position).
                if (names[n] == "background-size") words.Add("/ " + value);
                // The colour is the box's, not the layer's: it is said once, on the last of them
                // (measured: two layers read 'url(a) … , url(b) … red').
                else if (names[n] == "background-color" && i != count - 1) continue;
                else words.Add(value);
            }
            layers.Add(string.Join(" ", words));
        }
        return CssValueText.Canonicalize(string.Join(", ", layers), shorthand);
    }

    /// <summary>The value of a transition part that the shorthand leaves unsaid (CSS Transitions 1 §3).</summary>
    private static string TransitionInitial(string name) => name switch
    {
        "transition-timing-function" => "ease",
        "transition-delay" => "0s",
        "transition-behavior" => "normal",
        _ => "",
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
        // The same list the fold and the enumeration read. Reading the expansion's own
        // controlled-longhand table instead takes a shorthand apart that never carried the longhand:
        // 'white-space' enumerates two axes, and writing 'text-wrap-style' after it broke the
        // 'white-space: normal' the block had already folded (measured: the reference leaves it
        // standing beside the style).
        foreach (var entry in element.Style.ToList())
        {
            var shorthand = entry.Key;
            if (ShorthandExpander.CssomLonghands(shorthand) is not { } covered) continue;
            if (Array.FindIndex(covered, c => c.Equals(longhand, StringComparison.OrdinalIgnoreCase)) < 0)
                continue;
            if (string.IsNullOrEmpty(entry.Value)) continue;
            DemoteShorthand(element, shorthand, entry.Value!, drop: longhand);
        }
    }
}
