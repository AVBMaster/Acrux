using System;
using System.Collections.Generic;
using System.Linq;
using Acrux.Core.Css.ElementStyles;
using Acrux.Core.Css.Resolver;
using Acrux.Core.Css.Properties;

namespace Acrux.Core.Css;

public static class ShorthandExpander
{
    public static Dictionary<string, string> Expand(Dictionary<string, string> properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in properties)
        {
            // A shorthand the property's own grammar refuses expands into nothing at all. Reading it
            // as a handful of separately-valid longhands handed a declaration the reference engine
            // drops a cascade slot it should never have held (measured: 'text-decoration: underline
            // red overline' in a <style> block paints no line, exactly as the same value written
            // through el.style.setProperty, which the CSSOM gate refuses).
            if (IsShorthandName(prop.Key) && !CssValueGrammar.TextIsValidFor(prop.Key, prop.Value)) continue;
            var expanded = ExpandProperty(prop.Key, prop.Value);
            foreach (var kv in expanded)
                result[kv.Key] = kv.Value;
        }

        return result;
    }

    public static Dictionary<string, string> ExpandProperty(string name, string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (name.ToLowerInvariant())
        {
            case "margin": ExpandFourSides(result, "margin", value); break;
            case "padding": ExpandFourSides(result, "padding", value); break;
            case "border": ExpandBorder(result, value); break;
            case "border-width": ExpandFourSides(result, "border", value, "width"); break;
            case "border-color": ExpandFourSides(result, "border", value, "color"); break;
            case "border-style": ExpandFourSides(result, "border", value, "style"); break;
            case "border-radius": ExpandBorderRadius(result, value); break;
            case "border-top": ExpandBorderSide(result, "border-top", value); break;
            case "border-right": ExpandBorderSide(result, "border-right", value); break;
            case "border-bottom": ExpandBorderSide(result, "border-bottom", value); break;
            case "border-left": ExpandBorderSide(result, "border-left", value); break;
            case "background": ExpandBackground(result, value); break;
            // CSS Backgrounds 3 §4.1.1.1 makes the position a shorthand of the two axis
            // longhands, so the cascade sees the axes and never the pair they came from.
            case "background-position": ExpandBackgroundPosition(result, value); break;
            case "font": ExpandFont(result, value); break;
            case "flex": ExpandFlex(result, value); break;
            case "flex-flow": ExpandFlexFlow(result, value); break;
            case "grid-area": ExpandGridArea(result, value); break;
            case "grid-column": ExpandGridLine(result, "grid-column", value); break;
            case "grid-row": ExpandGridLine(result, "grid-row", value); break;
            case "animation": ExpandAnimation(result, value); break;
            case "transition": ExpandTransition(result, value); break;
            case "outline": ExpandOutline(result, value); break;
            case "text-decoration": ExpandTextDecoration(result, value); break;
            case "white-space": ExpandWhiteSpace(result, value); break;
            case "contain-intrinsic-size": ExpandContainIntrinsicSize(result, value); break;
            case "text-wrap": ExpandTextWrap(result, value); break;
            case "text-emphasis": ExpandTextEmphasis(result, value); break;
            case "gap": ExpandGap(result, value); break;
        case "columns": ExpandColumns(result, value); break;
        case "column-rule": ExpandColumnRule(result, value); break;
            case "place-content": ExpandPlace(result, value, "align-content", "justify-content"); break;
            case "place-items": ExpandPlace(result, value, "align-items", "justify-items"); break;
            case "place-self": ExpandPlace(result, value, "align-self", "justify-self"); break;
            case "list-style": ExpandListStyle(result, value); break;
            case "scroll-margin": ExpandFourSides(result, "scroll-margin", value); break;
            case "scroll-padding": ExpandFourSides(result, "scroll-padding", value); break;
            case "border-block": ExpandLogicalBorder(result, "block", value); break;
            case "border-inline": ExpandLogicalBorder(result, "inline", value); break;
            case "border-block-start": ExpandBorderSide(result, "border-block-start", value); break;
            case "border-block-end": ExpandBorderSide(result, "border-block-end", value); break;
            case "border-inline-start": ExpandBorderSide(result, "border-inline-start", value); break;
            case "border-inline-end": ExpandBorderSide(result, "border-inline-end", value); break;
            case "border-block-width": ExpandLogicalBorderComponent(result, "block", "width", value); break;
            case "border-block-style": ExpandLogicalBorderComponent(result, "block", "style", value); break;
            case "border-block-color": ExpandLogicalBorderComponent(result, "block", "color", value); break;
            case "border-inline-width": ExpandLogicalBorderComponent(result, "inline", "width", value); break;
            case "border-inline-style": ExpandLogicalBorderComponent(result, "inline", "style", value); break;
            case "border-inline-color": ExpandLogicalBorderComponent(result, "inline", "color", value); break;
            case "inset": ExpandFourSides(result, "", value); break;
            case "margin-block": ExpandLogicalPair(result, "margin-block", value); break;
            case "margin-inline": ExpandLogicalPair(result, "margin-inline", value); break;
            case "padding-block": ExpandLogicalPair(result, "padding-block", value); break;
            case "padding-inline": ExpandLogicalPair(result, "padding-inline", value); break;
            // The logical inset pairs expand here as well as apply: a block that carries the two
            // edges is enumerated, folded and demoted as the pair the page wrote (CSS Logical 1 §4).
            case "inset-block": ExpandLogicalPair(result, "inset-block", value); break;
            case "inset-inline": ExpandLogicalPair(result, "inset-inline", value); break;
            // The scroll insets address the same two edges (CSS Scroll Snap 1 §6.1), and a page that
            // writes 'scroll-margin-block: 1px 2px' has written both of its longhands — which is what
            // the computed surface of the pair then reads back (measured: it answers '1px 2px').
            case "scroll-margin-block": ExpandLogicalPair(result, "scroll-margin-block", value); break;
            case "scroll-margin-inline": ExpandLogicalPair(result, "scroll-margin-inline", value); break;
            case "scroll-padding-block": ExpandLogicalPair(result, "scroll-padding-block", value); break;
            case "scroll-padding-inline": ExpandLogicalPair(result, "scroll-padding-inline", value); break;
            case "overflow": ExpandOverflow(result, value); break;
            case "mask": ExpandMask(result, value); break;
            case "offset": ExpandOffset(result, value); break;
            default: result[name] = value; break;
        }

        return result;
    }

    /// <summary>
    /// The longhand property ids a shorthand controls (for cascade reset: a
    /// shorthand must clear controlled longhands it does not itself emit).
    /// </summary>
    public static IEnumerable<CssPropertyId> GetControlledLonghands(string name)
    {
        string[] keys = name.ToLowerInvariant() switch
        {
            "background" => new[] { "background-color", "background-image", "background-repeat", "background-attachment", "background-position", "background-position-x", "background-position-y", "background-size", "background-clip", "background-origin" },
            // The position is a shorthand of its two axes (CSS Backgrounds 3 §4.1.1.1), which is
            // how a block that names both ends up applying the later of the two declarations.
            "background-position" => new[] { "background-position-x", "background-position-y" },
            "margin" => new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" },
            "padding" => new[] { "padding-top", "padding-right", "padding-bottom", "padding-left" },
            "border" => new[] { "border-top-width", "border-top-style", "border-top-color", "border-right-width", "border-right-style", "border-right-color", "border-bottom-width", "border-bottom-style", "border-bottom-color", "border-left-width", "border-left-style", "border-left-color" },
            "font" => new[] { "font-style", "font-variant", "font-variant-caps", "font-weight", "font-size", "line-height", "font-family" },
            "font-variant" => new[] { "font-variant-caps" },
            "flex" => new[] { "flex-grow", "flex-shrink", "flex-basis" },
            "outline" => new[] { "outline-color", "outline-style", "outline-width" },
            // CSS Text 4 §1/§4: both shorthands reset the whole modular triple.
            "white-space" => new[] { "white-space-collapse", "text-wrap-mode", "text-wrap-style" },
            "text-wrap" => new[] { "text-wrap-mode", "text-wrap-style" },
            "animation" => new[] { "animation-name", "animation-duration", "animation-timing-function", "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state" },
            "transition" => new[] { "transition-property", "transition-duration", "transition-timing-function", "transition-delay" },
            // Every entry below mirrors exactly what the matching Expand* method emits, so a
            // shorthand resets its parts without erasing properties it does not own
            // ('text-decoration' must not reset 'text-underline-offset', 'font' must not
            // reset 'font-kerning', …).
            "border-top" => new[] { "border-top-width", "border-top-style", "border-top-color" },
            "border-right" => new[] { "border-right-width", "border-right-style", "border-right-color" },
            "border-bottom" => new[] { "border-bottom-width", "border-bottom-style", "border-bottom-color" },
            "border-left" => new[] { "border-left-width", "border-left-style", "border-left-color" },
            "border-width" => new[] { "border-top-width", "border-right-width", "border-bottom-width", "border-left-width" },
            "border-style" => new[] { "border-top-style", "border-right-style", "border-bottom-style", "border-left-style" },
            "border-color" => new[] { "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" },
            "border-radius" => new[] { "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius" },
            "inset" => new[] { "top", "right", "bottom", "left" },
            "margin-block" => new[] { "margin-block-start", "margin-block-end" },
            "margin-inline" => new[] { "margin-inline-start", "margin-inline-end" },
            "padding-block" => new[] { "padding-block-start", "padding-block-end" },
            "padding-inline" => new[] { "padding-inline-start", "padding-inline-end" },
            "inset-block" => new[] { "inset-block-start", "inset-block-end" },
            "inset-inline" => new[] { "inset-inline-start", "inset-inline-end" },
            "scroll-margin-block" => new[] { "scroll-margin-block-start", "scroll-margin-block-end" },
            "scroll-margin-inline" => new[] { "scroll-margin-inline-start", "scroll-margin-inline-end" },
            "scroll-padding-block" => new[] { "scroll-padding-block-start", "scroll-padding-block-end" },
            "scroll-padding-inline" => new[] { "scroll-padding-inline-start", "scroll-padding-inline-end" },
            "overflow" => new[] { "overflow-x", "overflow-y" },
            "contain-intrinsic-size" => new[] { "contain-intrinsic-width", "contain-intrinsic-height" },
            "mask" => new[] { "mask-image", "mask-position", "mask-size", "mask-repeat", "mask-origin", "mask-clip", "mask-composite", "mask-mode" },
            // Motion Path 1 §3.7: the five longhands, including the anchor the shorthand's own
            // grammar never names but does reset.
            "offset" => new[] { "offset-path", "offset-distance", "offset-rotate", "offset-anchor",
                                "offset-position" },
            "gap" => new[] { "row-gap", "column-gap" },
            "columns" => new[] { "column-width", "column-count" },
            "column-rule" => new[] { "column-rule-width", "column-rule-style", "column-rule-color" },
            "flex-flow" => new[] { "flex-direction", "flex-wrap" },
            "text-decoration" => new[] { "text-decoration-line", "text-decoration-style", "text-decoration-color", "text-decoration-thickness" },
            "text-emphasis" => new[] { "text-emphasis-style", "text-emphasis-color" },
            "grid-column" => new[] { "grid-column-start", "grid-column-end" },
            "grid-row" => new[] { "grid-row-start", "grid-row-end" },
            "grid-area" => new[] { "grid-row-start", "grid-row-end", "grid-column-start", "grid-column-end" },
            // The two-axis alignment shorthands (CSS Box Alignment 3 §5–7) own exactly the pair
            // they are named after, and nothing else — 'place-self' must not reset 'align-content'.
            "place-content" => new[] { "align-content", "justify-content" },
            "place-items" => new[] { "align-items", "justify-items" },
            "place-self" => new[] { "align-self", "justify-self" },
            "list-style" => new[] { "list-style-position", "list-style-image", "list-style-type" },
            "scroll-margin" => new[] { "scroll-margin-top", "scroll-margin-right", "scroll-margin-bottom", "scroll-margin-left" },
            "scroll-padding" => new[] { "scroll-padding-top", "scroll-padding-right", "scroll-padding-bottom", "scroll-padding-left" },
            // CSS Logical Properties 1 §5.1: the axis shorthands own both of their sides, and the
            // component shorthands one component of both sides.
            "border-block" => new[] { "border-block-start-width", "border-block-start-style", "border-block-start-color", "border-block-end-width", "border-block-end-style", "border-block-end-color" },
            "border-inline" => new[] { "border-inline-start-width", "border-inline-start-style", "border-inline-start-color", "border-inline-end-width", "border-inline-end-style", "border-inline-end-color" },
            "border-block-start" => new[] { "border-block-start-width", "border-block-start-style", "border-block-start-color" },
            "border-block-end" => new[] { "border-block-end-width", "border-block-end-style", "border-block-end-color" },
            "border-inline-start" => new[] { "border-inline-start-width", "border-inline-start-style", "border-inline-start-color" },
            "border-inline-end" => new[] { "border-inline-end-width", "border-inline-end-style", "border-inline-end-color" },
            "border-block-width" => new[] { "border-block-start-width", "border-block-end-width" },
            "border-block-style" => new[] { "border-block-start-style", "border-block-end-style" },
            "border-block-color" => new[] { "border-block-start-color", "border-block-end-color" },
            "border-inline-width" => new[] { "border-inline-start-width", "border-inline-end-width" },
            "border-inline-style" => new[] { "border-inline-start-style", "border-inline-end-style" },
            "border-inline-color" => new[] { "border-inline-start-color", "border-inline-end-color" },
            _ => System.Array.Empty<string>(),
        };
        return keys.Select(CssPropertyIdExtensions.FromString).Where(id => id != CssPropertyId.Invalid);
    }

    /// <summary>The names of every shorthand this expander knows.</summary>
    private static readonly string[] ShorthandNames =
    {
        "background", "background-position", "margin", "padding", "border", "border-width", "border-color",
        "border-style", "border-radius", "border-top", "border-right", "border-bottom",
        "border-left", "font", "font-variant", "flex", "outline", "white-space", "text-wrap",
        "animation", "transition", "inset", "inset-block", "inset-inline",
        "margin-block", "margin-inline", "padding-block",
        "padding-inline", "overflow", "contain-intrinsic-size", "mask", "gap", "columns",
        "column-rule", "flex-flow", "text-decoration", "text-emphasis", "grid-column",
        "grid-row", "grid-area",
        // Every shorthand <see cref="ExpandProperty"/> knows has to be listed here as well:
        // this array is what ShorthandsFor() indexes, and a longhand whose shorthand is missing
        // from it reads back empty through the CSSOM even though the shorthand set it.
        "place-content", "place-items", "place-self", "list-style",
        "scroll-margin", "scroll-padding", "offset",
        "scroll-margin-block", "scroll-margin-inline",
        "scroll-padding-block", "scroll-padding-inline",
        "border-block", "border-inline", "border-block-start", "border-block-end",
        "border-inline-start", "border-inline-end", "border-block-width", "border-block-style",
        "border-block-color", "border-inline-width", "border-inline-style",
        "border-inline-color",
    };

    private static Dictionary<string, List<string>>? _longhandToShorthands;

    /// <summary>Whether a name is one of the shorthands this expander reads. The cascade asks this
    /// before it expands anything, and it is the same list <see cref="ShorthandsFor"/> indexes: two
    /// lists of the same property set drift apart, and a shorthand missing from one of them is
    /// written to the cascade as if it were a longhand of its own name.</summary>
    public static bool IsShorthandName(string name)
    {
        foreach (var shorthand in ShorthandNames)
            if (name.Equals(shorthand, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The shorthands that carry a given longhand — <see cref="GetControlledLonghands"/>
    /// read the other way round. The CSSOM needs it: a declaration block that was written as
    /// 'background: rgb(1,2,3)' answers 'backgroundColor' with that colour, because setting a
    /// shorthand sets its parts (measured, and the reason a script that reads a margin it set
    /// through the 'margin' shorthand is not left with an empty string).</summary>
    public static IEnumerable<string> ShorthandsFor(string longhand)
    {
        if (_longhandToShorthands == null)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var shorthand in ShorthandNames)
            {
                foreach (var id in GetControlledLonghands(shorthand))
                {
                    // The dashed name, not the enum member: an extension method never wins an
                    // instance call, so 'id.ToString()' here would index 'BackgroundColor' and
                    // every lookup for 'background-color' would miss.
                    var name = CssPropertyIdExtensions.ToString(id);
                    if (!index.TryGetValue(name, out var list)) index[name] = list = new List<string>(2);
                    list.Add(shorthand);
                }
            }
            _longhandToShorthands = index;
        }
        return _longhandToShorthands.TryGetValue(longhand, out var shorthands)
            ? shorthands : System.Array.Empty<string>();
    }

    /// <summary>'white-space' is a shorthand of the CSS Text 4 §1 triple. Expanding it
    /// here (rather than mapping it inside the applier) is what makes source order work:
    /// 'white-space-collapse: preserve; white-space: normal' must end at 'normal', and
    /// both declarations then write the same longhand keys.</summary>
    private static void ExpandWhiteSpace(Dictionary<string, string> result, string value)
    {
        string v = value.Trim().ToLowerInvariant();
        string head = v.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "normal";
        string collapse = head switch
        {
            "pre" => "preserve",
            "pre-wrap" => "preserve",
            "pre-line" => "preserve-breaks",
            "break-spaces" => "break-spaces",
            "nowrap" => "collapse",
            _ => "collapse",
        };
        string mode = head == "pre" || head == "nowrap" ? "nowrap" : "wrap";
        // The style is emitted in the spelling the property itself takes: 'balance', not the
        // 'balanced' of an older draft (measured: 'text-wrap-style: balanced' is dropped).
        string style = v.Contains("balance") ? "balance"
            : v.Contains("stable") ? "stable"
            : v.Contains("pretty") ? "pretty" : "auto";
        result["white-space-collapse"] = collapse;
        result["text-wrap-mode"] = mode;
        result["text-wrap-style"] = style;
    }

    /// <summary>CSS Containment 3 §2.2: 'contain-intrinsic-size' is the two-axis shorthand of
    /// the width/height pair, with an optional leading 'auto' that applies to both axes. An
    /// ill-formed value (a bare 'auto', a percentage, three lengths) emits nothing at all,
    /// which is what the reference engine does with the declaration — it drops it and leaves
    /// the axes as they were.</summary>
    private static void ExpandContainIntrinsicSize(Dictionary<string, string> result, string value)
    {
        var tokens = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return;
        int start = 0;
        string prefix = "";
        if (tokens[0] == "auto")
        {
            prefix = "auto ";
            start = 1;
        }
        var axes = tokens.Skip(start).ToArray();
        if (axes.Length is < 1 or > 2) return;
        if (axes.Any(t => t.Contains('%'))) return;
        string inlineAxis = prefix + axes[0];
        string blockAxis = prefix + (axes.Length == 2 ? axes[1] : axes[0]);
        result["contain-intrinsic-width"] = inlineAxis;
        result["contain-intrinsic-height"] = blockAxis;
    }

    /// <summary>'text-wrap' = &lt;text-wrap-mode&gt; || &lt;text-wrap-style&gt; (CSS Text 4 §4).
    private static void ExpandTextWrap(Dictionary<string, string> result, string value)
    {
        string v = value.Trim().ToLowerInvariant();
        result["text-wrap-mode"] = v.Contains("nowrap") ? "nowrap" : "wrap";
        result["text-wrap-style"] = v.Contains("balance") ? "balance"
            : v.Contains("stable") ? "stable"
            : v.Contains("pretty") ? "pretty" : "auto";
    }

    private static void ExpandBorderRadius(Dictionary<string, string> result, string value)
    {
        // Fast path: no '/' (elliptical radii) 鈥?expand as a plain 1-4 value list.
        if (!value.Contains('/'))
        {
            var parts = SplitShorthand(value);
            string topLeft, topRight, bottomRight, bottomLeft;
            switch (parts.Count)
            {
                case 1: topLeft = topRight = bottomRight = bottomLeft = parts[0]; break;
                case 2: topLeft = bottomRight = parts[0]; topRight = bottomLeft = parts[1]; break;
                case 3: topLeft = parts[0]; topRight = bottomLeft = parts[1]; bottomRight = parts[2]; break;
                default: topLeft = parts[0]; topRight = parts[1]; bottomRight = parts[2]; bottomLeft = parts[3]; break;
            }
            result["border-top-left-radius"] = topLeft;
            result["border-top-right-radius"] = topRight;
            result["border-bottom-right-radius"] = bottomRight;
            result["border-bottom-left-radius"] = bottomLeft;
            return;
        }

        // Elliptical radii 'border-radius: h1 h2 / v1 v2': expand each corner to
        // 'hw / vh' pairs the cascade parser understands (first value is the
        // horizontal radius, second the vertical one).
        var halves = value.Split('/');
        var h = SplitShorthand(halves[0]);
        var v = halves.Length > 1 ? SplitShorthand(halves[1]) : h;
        int maxCount = Math.Max(h.Count, v.Count);
        var hTopLeft = h[0];
        var hTopRight = h.Count > 1 ? h[1] : h[0];
        var hBottomRight = h.Count > 2 ? h[2] : h[0];
        var hBottomLeft = h.Count > 3 ? h[3] : (h.Count > 1 ? h[1] : h[0]);
        var vTopLeft = v[0];
        var vTopRight = v.Count > 1 ? v[1] : v[0];
        var vBottomRight = v.Count > 2 ? v[2] : v[0];
        var vBottomLeft = v.Count > 3 ? v[3] : (v.Count > 1 ? v[1] : v[0]);

        result["border-top-left-radius"] = $"{hTopLeft} {vTopLeft}";
        result["border-top-right-radius"] = $"{hTopRight} {vTopRight}";
        result["border-bottom-right-radius"] = $"{hBottomRight} {vBottomRight}";
        result["border-bottom-left-radius"] = $"{hBottomLeft} {vBottomLeft}";
    }

    private static void ExpandFourSides(Dictionary<string, string> result, string prefix, string value, string? suffix = null)
    {
        var parts = SplitShorthand(value);
        string top, right, bottom, left;

        switch (parts.Count)
        {
            case 1: top = right = bottom = left = parts[0]; break;
            case 2: top = bottom = parts[0]; right = left = parts[1]; break;
            case 3: top = parts[0]; right = left = parts[1]; bottom = parts[2]; break;
            default: top = parts[0]; right = parts[1]; bottom = parts[2]; left = parts[3]; break;
        }

        string suffixStr = string.IsNullOrEmpty(suffix) ? "" : $"-{suffix}";
        if (!string.IsNullOrEmpty(prefix))
        {
            result[$"{prefix}-top{suffixStr}"] = top;
            result[$"{prefix}-right{suffixStr}"] = right;
            result[$"{prefix}-bottom{suffixStr}"] = bottom;
            result[$"{prefix}-left{suffixStr}"] = left;
        }
        else
        {
            result["top"] = top;
            result["right"] = right;
            result["bottom"] = bottom;
            result["left"] = left;
        }
    }

    /// <summary>CSS Logical Properties 1 §4: the block/inline pair shorthands
    /// (<c>margin-block</c>, <c>padding-inline</c>, …) take one or two values that address
    /// the axis' start and end. They must expand to the <em>logical</em> longhands — the
    /// mapping onto physical sides happens later, against the final computed 'direction' —
    /// so this deliberately does not pick a side itself.</summary>
    private static void ExpandLogicalPair(Dictionary<string, string> result, string axis, string value)
    {
        var parts = SplitShorthand(value);
        if (parts.Count == 0) return;
        result[$"{axis}-start"] = parts[0];
        result[$"{axis}-end"] = parts.Count >= 2 ? parts[1] : parts[0];
    }

    private static void ExpandBorder(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (IsBorderStyle(p))
            {
                result["border-top-style"] = p;
                result["border-right-style"] = p;
                result["border-bottom-style"] = p;
                result["border-left-style"] = p;
            }
            else if (IsBorderWidth(p))
            {
                result["border-top-width"] = p;
                result["border-right-width"] = p;
                result["border-bottom-width"] = p;
                result["border-left-width"] = p;
            }
            else if (p == "none" || p == "hidden")
            {
                result["border-top-style"] = p;
                result["border-right-style"] = p;
                result["border-bottom-style"] = p;
                result["border-left-style"] = p;
            }
            else if (IsColor(p))
            {
                result["border-top-color"] = p;
                result["border-right-color"] = p;
                result["border-bottom-color"] = p;
                result["border-left-color"] = p;
            }
        }

        result.TryAdd("border-top-style", "none");
        result.TryAdd("border-right-style", "none");
        result.TryAdd("border-bottom-style", "none");
        result.TryAdd("border-left-style", "none");
        // The border shorthand resets border-width to its initial value (medium)
        // when no width is given, so 'border: solid' paints a 3px border.
        result.TryAdd("border-top-width", "medium");
        result.TryAdd("border-right-width", "medium");
        result.TryAdd("border-bottom-width", "medium");
        result.TryAdd("border-left-width", "medium");
    }

    private static void ExpandBorderSide(Dictionary<string, string> result, string side, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (IsBorderStyle(p))
                result[$"{side}-style"] = p;
            else if (IsBorderWidth(p))
                result[$"{side}-width"] = p;
            else if (IsColor(p))
                result[$"{side}-color"] = p;
        }

        // A side shorthand resets the components it does not carry (CSS Backgrounds 3 §4):
        // the width falls back to medium, the style to none and the colour to currentcolor,
        // which then paints the element's own text colour instead of a stale value.
        result.TryAdd($"{side}-style", "none");
        result.TryAdd($"{side}-width", "medium");
        result.TryAdd($"{side}-color", "currentcolor");
    }

    private static void ExpandBackground(Dictionary<string, string> result, string value)
    {
        // The background shorthand may carry multiple comma-separated layers, each
        // of which can itself contain spaces inside gradient functions. Split into
        // layers on top-level commas first, then tokenize each layer by space.
        var layers = SplitTopLevel(value, ',');

        // CSS Backgrounds 3 §4: the <color> may only appear in the FINAL layer. In any
        // other layer the whole declaration is invalid, so it must not produce a single
        // longhand — not even the ones that look harmless. Measured on
        // snapshots/css-standard-verify197-multilayer-background.html §9: a reference
        // browser keeps the element's earlier background-color and paints no layers,
        // while accepting it leaked the second layer's blue into the box.
        var tokens = new List<string[]>();
        foreach (var layer in layers)
            tokens.Add(SplitShorthand(layer).Select(t => t.Trim().TrimEnd(',').Trim()).Where(t => t.Length > 0).ToArray());
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            foreach (var p in tokens[i])
            {
                if (p.Equals("transparent", StringComparison.OrdinalIgnoreCase) || ColorParser.LooksLikeColor(p))
                    return;
            }
        }

        var images = new List<string>();
        // One entry per layer for every geometry longhand, filled with the property's
        // initial value where the layer stayed silent. A shorthand must produce a list
        // exactly as long as the layer count: a shorter list would CYCLE, so
        // "background: url(a) 10px 10px, url(b)" would move the second layer to
        // 10px 10px as well instead of leaving it at 0% 0%.
        var positions = new List<string>();
        var sizes = new List<string>();
        var repeats = new List<string>();
        var origins = new List<string>();
        var clips = new List<string>();
        var attachments = new List<string>();
        string? finalColor = null;

        foreach (var parts in tokens)
        {
            // `<position> / <size>`: the position is a LIST of tokens ("10px 20px",
            // "center bottom", "calc(50% - 10px) 0"), so they have to be collected and
            // joined. Matching only '%' and the keywords — as this did — silently dropped
            // every length, so "background: url(a) 10px 20px" positioned the image at
            // 10px/0 instead of 10px/20px.
            //
            // A slash written inside a token ('10px/ 30px', '10px /30px', '10px/30px') is the
            // same separator the grammar speaks of, so the spellings are put on one footing
            // first: the token becomes its position half, a '/' of its own, and its size half.
            var stream = new List<string>();
            foreach (var t in parts)
            {
                if (!t.Contains('/') || t.Contains("("))
                {
                    stream.Add(t);
                    continue;
                }
                var halves = t.Split('/', 2);
                var head = halves[0].Trim();
                var tail = halves.Length > 1 ? halves[1].Trim() : string.Empty;
                if (head.Length > 0) stream.Add(head);
                stream.Add("/");
                if (tail.Length > 0) stream.Add(tail);
            }

            var position = new List<string>();
            var sizeTokens = new List<string>();
            string repeat = "repeat";
            string origin = "padding-box";
            string clip = "border-box";
            string attachment = "scroll";
            string? layerImage = null;
            bool afterSlash = false;
            bool slashSeen = false;
            var boxKeywords = new List<string>();
            int positionLastToken = -1;

            for (int ti = 0; ti < stream.Count; ti++)
            {
                var p = stream[ti];

                // Everything up to the first token the <bg-size> production does not take is
                // the size; 'url(a) 10px / cover no-repeat' keeps both, while a slash with no
                // size term behind it ('url(a) 10px /', 'url(a) 10px / none') is a parse error.
                if (afterSlash)
                {
                    if (IsBackgroundSizeTerm(p))
                    {
                        sizeTokens.Add(KeywordText(p));
                        continue;
                    }
                    afterSlash = false;
                    if (sizeTokens.Count == 0) return;
                }

                if (IsBackgroundImageToken(p))
                {
                    // 'none' is a layer with no image, and it still has to be written into the
                    // list: a shorthand resets the longhands it does not carry, so dropping the
                    // keyword left the element's earlier image alive under 'background: none'
                    // (measured: the layer reads back as 'none'). An empty url() has no address
                    // and paints nothing, which the same 'none' layer stands for.
                    if (p.Equals("none", StringComparison.OrdinalIgnoreCase)
                        || CssPropertyApplier.IsUrlWithoutAddress(p))
                        layerImage = "none";
                    else
                        layerImage = p;
                }
                else if (ColorParser.LooksLikeColor(p))
                {
                    // <final-bg-layer> takes one <color> and no grammar says a second may replace
                    // it (measured: 'background: red blue' is a parse error and the whole
                    // declaration goes with it).
                    if (finalColor != null) return;
                    finalColor = p;
                }
                else if (IsKeyword(p, "repeat") || IsKeyword(p, "no-repeat") || IsKeyword(p, "repeat-x")
                         || IsKeyword(p, "repeat-y") || IsKeyword(p, "round") || IsKeyword(p, "space"))
                {
                    var keyword = KeywordText(p);
                    repeat = repeat == "repeat" ? keyword : repeat + " " + keyword;
                }
                else if (IsKeyword(p, "scroll") || IsKeyword(p, "fixed") || IsKeyword(p, "local"))
                    attachment = KeywordText(p);
                else if (IsKeyword(p, "padding-box") || IsKeyword(p, "border-box") || IsKeyword(p, "content-box")
                         || IsKeyword(p, "text"))
                {
                    // Collected, not assigned: which of the two properties a keyword lands on is
                    // decided once the layer has been read in full (see below).
                    boxKeywords.Add(KeywordText(p));
                }
                else if (p == "/")
                {
                    // The slash comes directly after the <bg-position>: 'url(a) / 30px' carries a
                    // size with no position and 'url(a) 10px repeat / 30px' has put a repeat
                    // between the two — both are parse errors, while 'url(a) repeat 10px / 30px'
                    // is not (measured, and the same for every layer of a list).
                    if (slashSeen) return;
                    if (position.Count == 0 || positionLastToken != ti - 1) return;
                    slashSeen = true;
                    afterSlash = true;
                }
                else if (IsKeyword(p, "left") || IsKeyword(p, "right") || IsKeyword(p, "center")
                         || IsKeyword(p, "top") || IsKeyword(p, "bottom")
                         || IsBackgroundPositionLength(p))
                {
                    position.Add(KeywordText(p));
                    positionLastToken = ti;
                }
                else
                {
                    // Nothing in the grammar of <'background'> takes this token, so the value is
                    // a parse error and the WHOLE declaration goes — not just the one part. A
                    // colour the engine does not know ('invert'), a stray 'auto' outside the
                    // size, an unknown function: all of them leave the element exactly as the
                    // rules below it had it (measured: 'background: invert', 'background: foo(2px)',
                    // 'background: url(a) auto', 'background: red blue', 'background: 10% 20% 30%').
                    return;
                }
            }

            if (afterSlash && sizeTokens.Count == 0) return;

            // <bg-position> accepts three shapes and nothing else, so a position the grammar
            // does not read takes the whole shorthand with it ('background: 10% 20% 30%').
            if (!IsBackgroundPositionList(position)) return;
            // <bg-size> is one or two terms, and 'cover'/'contain' take the box alone.
            if (!IsBackgroundSizeList(sizeTokens)) return;
            // The repeat keywords the layer accumulated are checked as the longhand reads them,
            // so that a layer which pairs an axis keyword with another mode ('url(a) repeat-x
            // no-repeat') takes the whole declaration down with it (measured).
            if (!IsBackgroundRepeatValue(repeat)) return;

            positions.Add(position.Count > 0 ? string.Join(" ", position) : "0% 0%");
            sizes.Add(sizeTokens.Count > 0 ? string.Join(" ", sizeTokens) : "auto");
            repeats.Add(repeat);
            // CSS Backgrounds 3 §4.1, measured: '<box>' may appear at most twice and 'text' at
            // most once ('url(a) text text', 'url(a) border-box content-box text' are parse
            // errors and take the whole declaration down). A single non-'text' keyword is given
            // to BOTH properties — 'background: url(a) content-box' clips to the content box as
            // well as originating there — while 'text' is a clip only, so the origin stays at its
            // initial 'padding-box'. Order does not matter: 'text content-box' and 'content-box
            // text' both originate at the content box and clip to the text.
            if (boxKeywords.Count > 2 || boxKeywords.Count(b => b == "text") > 1) return;
            if (boxKeywords.Count > 0)
            {
                var boxes = boxKeywords.Where(b => b != "text").ToArray();
                if (boxes.Length > 0) origin = boxes[0];
                clip = boxKeywords.Contains("text") ? "text" : boxes[^1];
            }
            origins.Add(origin);
            clips.Add(clip);
            attachments.Add(attachment);
            // A layer that named no image is still a layer, and it is written into the list as
            // the 'none' the reference engine reads it back as: 'background: url(a) 10px 20px, red'
            // carries two image layers — 'url(a), none' — and every geometry list is reported
            // against that count, so a silent layer is not the same as a missing one.
            images.Add(layerImage ?? "none");
        }

        if (images.Count > 0)
            result["background-image"] = string.Join(", ", images);
        if (finalColor != null)
            result["background-color"] = finalColor;

        // The position the layer stack carries goes out as its two axes, not as the pair:
        // 'background-position' is a shorthand of them (§4.1.1.1), and a block that writes
        // 'background-position-x' beside a 'background' means the axis it wrote — which only
        // holds if the two are applied as the separate longhands they are.

        // Only write a list when there actually is more than one layer; a single-entry
        // list would round-trip through the comma-splitting parsers for no reason, and
        // the scalar path already handles it identically.
        if (tokens.Count > 1)
        {
            ExpandBackgroundPosition(result, string.Join(", ", positions));
            result["background-size"] = string.Join(", ", sizes);
            result["background-repeat"] = string.Join(", ", repeats);
            result["background-origin"] = string.Join(", ", origins);
            result["background-clip"] = string.Join(", ", clips);
            result["background-attachment"] = string.Join(", ", attachments);
        }
        else if (positions[0] != "0% 0%" || sizes[0] != "auto" || repeats[0] != "repeat"
                 || origins[0] != "padding-box" || clips[0] != "border-box" || attachments[0] != "scroll")
        {
            ExpandBackgroundPosition(result, positions[0]);
            result["background-size"] = sizes[0];
            result["background-repeat"] = repeats[0];
            result["background-origin"] = origins[0];
            result["background-clip"] = clips[0];
            result["background-attachment"] = attachments[0];
        }

        // A shorthand resets the longhands it does not set (CSS 2.1 §14.3): with no
        // <color> in the list the layer stack sits on a transparent box, not on whatever
        // the cascade had before.
        result.TryAdd("background-color", "transparent");
        result.TryAdd("background-repeat", "repeat");
        result.TryAdd("background-attachment", "scroll");
        result.TryAdd("background-position-x", "0%");
        result.TryAdd("background-position-y", "0%");
        result.TryAdd("background-size", "auto");
        // The two box properties are reset like the rest: 'background-clip: content-box'
        // followed by 'background: red' computes 'border-box' again, and so does a single
        // 'background: none' after a 'background-origin' (measured, both orders).
        result.TryAdd("background-origin", "padding-box");
        result.TryAdd("background-clip", "border-box");
    }

    /// <summary>'background-position' as the shorthand CSS Backgrounds 3 §4.1.1.1 says it is:
    /// two longhands, one per axis. The cascade applies longhands, so a block that writes an
    /// axis after the pair keeps it and one that writes it before loses it to the pair
    /// (measured, both orders, and the same through the 'background' shorthand).</summary>
    private static void ExpandBackgroundPosition(Dictionary<string, string> result, string value)
    {
        if (!TrySplitBackgroundPositionAxes(value, out var xList, out var yList)) return;
        result["background-position-x"] = xList;
        result["background-position-y"] = yList;
    }

    /// <summary>The two axis lists a &lt;bg-position&gt; list stands for, each layer written in the
    /// shape its own axis grammar takes: an axis the layer never mentions is 'center' — the
    /// middle of the box, not zero — and an edge group keeps its keyword with the offset
    /// measured from it.</summary>
    public static bool TrySplitBackgroundPositionAxes(string value, out string xList, out string yList)
    {
        var xs = new List<string>();
        var ys = new List<string>();
        foreach (var layer in SplitTopLevel(value, ','))
        {
            if (!TryMatchBackgroundPosition(SplitShorthand(layer.Trim()), out var x, out var y))
            {
                xList = yList = string.Empty;
                return false;
            }
            xs.Add(PositionAxisText(x));
            ys.Add(PositionAxisText(y));
        }
        xList = string.Join(", ", xs);
        yList = string.Join(", ", ys);
        return xs.Count > 0;
    }

    private static string PositionAxisText(PositionAxis axis, bool canonicalLength = false)
        => !axis.Present ? "center"
        : axis.Edge != null
            ? axis.Offset != null
                ? KeywordText(axis.Edge) + " "
                  + (canonicalLength ? CanonicalPositionLength(axis.Offset) : KeywordText(axis.Offset))
                : KeywordText(axis.Edge)
            : canonicalLength
                ? CanonicalPositionLength(axis.Offset!)
                : KeywordText(axis.Offset!);

    /// <summary>A &lt;position&gt; offset printed the way the properties that hold one print it: a
    /// number with no unit is the zero pixels it stands for (measured: 'offset-anchor: 0 0' reads
    /// '0px 0px'). A background layer keeps the text the page wrote, where the same number is
    /// the percentage of the box it is measured against.</summary>
    private static string CanonicalPositionLength(string token)
    {
        if (token.Contains('(')) return token;
        return Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out var number, out var unit)
               && unit.Length == 0
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px"
            : token;
    }

    /// <summary>The 'background' shorthand the way the reference engine prints it: the components
    /// the page WROTE, in the order the grammar takes them — <c>&lt;bg-image&gt;</c>,
    /// <c>&lt;position&gt; [ / &lt;size&gt; ]</c>, <c>&lt;repeat-style&gt;</c>,
    /// <c>&lt;attachment&gt;</c>, the two boxes, and the <c>&lt;color&gt;</c> last (measured:
    /// 'red url(a.png)' reads 'url("a.png") red', 'red no-repeat' reads 'no-repeat red', and
    /// 'red 10px 20px' reads '10px 20px red'). Only what was written is said, so a written default
    /// keeps its word — 'url(a.png) repeat' still says 'repeat' — which is exactly why this cannot
    /// be rebuilt from the longhand map: that map carries the shorthand's resets too and cannot tell
    /// the two apart. One box sets both, so it is printed twice ('url(a.png) border-box' reads
    /// 'border-box border-box', measured), and a size forces the position to say both of its axes
    /// ('center / cover' reads 'center center / cover'). A layer this reader cannot classify is
    /// returned exactly as it came, because the grammar has already had its say.</summary>
    public static string BackgroundLayerOrderText(string text)
    {
        var layers = SplitTopLevel(text, ',');
        if (layers.Count == 0) return text;
        var printed = new List<string>(layers.Count);
        foreach (var layer in layers)
        {
            // Put a slash written inside a token on the same footing as one written alone.
            var stream = new List<string>();
            foreach (var raw in SplitShorthand(layer).Select(t => t.Trim().TrimEnd(',').Trim())
                         .Where(t => t.Length > 0))
            {
                if (!raw.Contains('/') || raw.Contains('(')) { stream.Add(raw); continue; }
                var halves = raw.Split('/', 2);
                if (halves[0].Trim().Length > 0) stream.Add(halves[0].Trim());
                stream.Add("/");
                if (halves.Length > 1 && halves[1].Trim().Length > 0) stream.Add(halves[1].Trim());
            }

            string? image = null, colour = null, repeat = null, attachment = null;
            var position = new List<string>();
            var size = new List<string>();
            var boxes = new List<string>();
            bool afterSlash = false, unknown = false;
            foreach (var token in stream)
            {
                var p = KeywordText(token);
                if (token == "/") { afterSlash = true; continue; }
                if (afterSlash)
                {
                    // The size stays open for its SECOND term: '0 0 / 30px 30px' is one size, and
                    // closing on the first would leave the other to be read as a position.
                    if (IsBackgroundSizeTerm(p)) { size.Add(p); continue; }
                    afterSlash = false;
                }
                if (IsBackgroundImageToken(p)) { image = p; continue; }
                if (ColorParser.LooksLikeColor(p)) { colour = p; continue; }
                if (IsKeyword(p, "repeat") || IsKeyword(p, "no-repeat") || IsKeyword(p, "repeat-x")
                    || IsKeyword(p, "repeat-y") || IsKeyword(p, "round") || IsKeyword(p, "space"))
                { repeat = repeat == null ? p : repeat + " " + p; continue; }
                if (IsKeyword(p, "scroll") || IsKeyword(p, "fixed") || IsKeyword(p, "local"))
                { attachment = p; continue; }
                if (IsKeyword(p, "padding-box") || IsKeyword(p, "border-box")
                    || IsKeyword(p, "content-box") || IsKeyword(p, "text"))
                { boxes.Add(p); continue; }
                if (IsKeyword(p, "left") || IsKeyword(p, "right") || IsKeyword(p, "center")
                    || IsKeyword(p, "top") || IsKeyword(p, "bottom") || IsBackgroundPositionLength(p))
                { position.Add(p); continue; }
                unknown = true;
                break;
            }
            if (unknown) return text;

            var parts = new List<string>(7);
            if (image != null) parts.Add(image);
            // The position is printed as its pair of axes whichever company it keeps: 'center'
            // reads 'center center' and '50%' reads '50% center', with or without a size behind it
            // (measured), while 'left top' and '10px 20px' are already a pair.
            var positionText = position.Count > 0
                ? (PositionPairText(string.Join(" ", position)) ?? string.Join(" ", position))
                : null;
            if (positionText != null && size.Count > 0)
                parts.Add(positionText + " / " + string.Join(" ", size));
            else if (positionText != null) parts.Add(positionText);
            else if (size.Count > 0) parts.Add(string.Join(" ", size));
            if (repeat != null) parts.Add(repeat);
            if (attachment != null) parts.Add(attachment);
            if (boxes.Count == 1 && boxes[0] != "text") { parts.Add(boxes[0]); parts.Add(boxes[0]); }
            else foreach (var box in boxes) parts.Add(box);
            if (colour != null) parts.Add(colour);
            printed.Add(parts.Count > 0 ? string.Join(" ", parts) : KeywordText(layer.Trim()));
        }
        return string.Join(", ", printed);
    }

    /// <summary>The 'mask' shorthand in the order its grammar takes the parts — image, position /
    /// size, repeat, origin, clip, composite, mode — saying only the parts the layer named
    /// (measured: 'url(a.png) exclude content-box' reads 'url("a.png") content-box exclude' and
    /// 'url(a.png) 50%' reads 'url("a.png") 50% center'). Unlike 'background', a mask layer has no
    /// colour and no attachment, and one box keyword is printed ONCE rather than as the pair the
    /// background says twice (both measured).</summary>
    public static string MaskLayerOrderText(string text)
    {
        var layers = Acrux.Core.Css.MaskLayerParser.Parse(text);
        if (layers.Count == 0) return text;
        var printed = new List<string>(layers.Count);
        foreach (var layer in layers)
        {
            var parts = new List<string>(7);
            if (layer.Image.Length > 0) parts.Add(layer.Image);
            if (layer.Position != null)
            {
                var position = PositionPairText(layer.Position) ?? layer.Position;
                parts.Add(layer.Size != null ? position + " / " + layer.Size : position);
            }
            if (layer.Repeat != null && !Acrux.Core.Css.MaskLayerParser.RepeatIsInitial(layer.Repeat))
                parts.Add(layer.Repeat);
            AddMaskBoxParts(parts, layer);
            if (layer.Composite != null) parts.Add(layer.Composite);
            if (layer.Mode != null) parts.Add(layer.Mode);
            printed.Add(parts.Count > 0 ? string.Join(" ", parts) : "none");
        }
        return string.Join(", ", printed);
    }

    /// <summary>The origin/clip pair a mask layer names, in the two-box shapes the reference engine
    /// answers (all measured): a pair that sits at its initial value says nothing at all, so
    /// <c>url(a) border-box</c> is <c>url("a")</c>; a single box is ONE word, not the pair the
    /// background says twice; unequal boxes are both said, in origin-then-clip order, even when the
    /// origin is the initial one; and 'no-clip' replaces the clip half so an initial origin beside it
    /// is silence while a named origin still speaks.</summary>
    private static void AddMaskBoxParts(List<string> parts, Acrux.Core.Css.MaskLayer layer)
    {
        if (layer.Origin == null && layer.Clip == null) return;
        if (layer.Clip == "no-clip")
        {
            if (layer.Origin != null && layer.Origin != "border-box") parts.Add(layer.Origin);
            parts.Add("no-clip");
            return;
        }
        if (layer.Origin == layer.Clip)
        {
            if (layer.Origin != "border-box") parts.Add(layer.Origin);
            return;
        }
        if (layer.Origin != null) parts.Add(layer.Origin);
        if (layer.Clip != null) parts.Add(layer.Clip);
    }

    /// <summary>The longhands a shorthand stands for when a page enumerates the declaration block,
    /// in the order the reference engine lists them — measured for every list here, and the shape
    /// is not the expansion order: <c>border</c> runs the four widths, then the four styles, then
    /// the four colours, then the five <c>border-image</c> parts (17); <c>background</c> has no
    /// <c>background-position</c> of its own because it lists the two axes; <c>mask</c> lists its
    /// position axes under their legacy <c>-webkit-</c> names; <c>place-items</c> is the two
    /// <c>align</c>/<c>justify</c> longhands it writes. Null for a shorthand that has not been
    /// measured, where the caller falls back to the parts the expansion produced.</summary>
    public static string[]? CssomLonghands(string name) => name.ToLowerInvariant() switch
    {
        "margin" => new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" },
        "padding" => new[] { "padding-top", "padding-right", "padding-bottom", "padding-left" },
        "inset" => new[] { "top", "right", "bottom", "left" },
        // The logical pairs enumerate as the two edges they wrote, start first (CSS Logical 1 §4).
        "margin-block" => new[] { "margin-block-start", "margin-block-end" },
        "margin-inline" => new[] { "margin-inline-start", "margin-inline-end" },
        "padding-block" => new[] { "padding-block-start", "padding-block-end" },
        "padding-inline" => new[] { "padding-inline-start", "padding-inline-end" },
        "inset-block" => new[] { "inset-block-start", "inset-block-end" },
        "inset-inline" => new[] { "inset-inline-start", "inset-inline-end" },
        "scroll-margin-block" => new[] { "scroll-margin-block-start", "scroll-margin-block-end" },
        "scroll-margin-inline" => new[] { "scroll-margin-inline-start", "scroll-margin-inline-end" },
        "scroll-padding-block" => new[] { "scroll-padding-block-start", "scroll-padding-block-end" },
        "scroll-padding-inline" => new[] { "scroll-padding-inline-start", "scroll-padding-inline-end" },
        "outline" => new[] { "outline-color", "outline-style", "outline-width" },
        "border-width" => new[] { "border-top-width", "border-right-width", "border-bottom-width", "border-left-width" },
        "border-style" => new[] { "border-top-style", "border-right-style", "border-bottom-style", "border-left-style" },
        "border-color" => new[] { "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" },
        "border" => new[] {
            "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
            "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
            "border-top-color", "border-right-color", "border-bottom-color", "border-left-color",
            "border-image-source", "border-image-slice", "border-image-width",
            "border-image-outset", "border-image-repeat",
        },
        "list-style" => new[] { "list-style-position", "list-style-image", "list-style-type" },
        "animation" => new[] {
            "animation-duration", "animation-timing-function", "animation-delay",
            "animation-iteration-count", "animation-direction", "animation-fill-mode",
            "animation-play-state", "animation-name", "animation-timeline",
            "animation-range-start", "animation-range-end",
        },
        "transition" => new[] {
            "transition-behavior", "transition-duration", "transition-timing-function",
            "transition-delay", "transition-property",
        },
        "columns" => new[] { "column-width", "column-count", "column-height", "column-wrap" },
        "mask" => new[] {
            "mask-image", "-webkit-mask-position-x", "-webkit-mask-position-y", "mask-size",
            "mask-repeat", "mask-origin", "mask-clip", "mask-composite", "mask-mode",
        },
        "gap" => new[] { "row-gap", "column-gap" },
        "text-decoration" => new[] {
            "text-decoration-line", "text-decoration-thickness", "text-decoration-style",
            "text-decoration-color",
        },
        "overflow" => new[] { "overflow-x", "overflow-y" },
        "border-radius" => new[] {
            "border-top-left-radius", "border-top-right-radius",
            "border-bottom-right-radius", "border-bottom-left-radius",
        },
        "place-items" => new[] { "align-items", "justify-items" },
        "place-content" => new[] { "align-content", "justify-content" },
        "place-self" => new[] { "align-self", "justify-self" },
        "background-position" => new[] { "background-position-x", "background-position-y" },
        "background" => new[] {
            "background-image", "background-position-x", "background-position-y", "background-size",
            "background-repeat", "background-attachment", "background-origin", "background-clip",
            "background-color",
        },
        "flex" => new[] { "flex-grow", "flex-shrink", "flex-basis" },
        "grid" => new[] {
            "grid-template-rows", "grid-template-columns", "grid-template-areas",
            "grid-auto-flow", "grid-auto-columns", "grid-auto-rows",
        },
        "text-emphasis" => new[] { "text-emphasis-style", "text-emphasis-color" },
        "font" => new[] {
            "font-style", "font-variant-caps", "font-variant-ligatures", "font-variant-numeric",
            "font-variant-east-asian", "font-variant-alternates", "font-size-adjust",
            "font-language-override", "font-kerning", "font-optical-sizing",
            "font-feature-settings", "font-variation-settings", "font-variant-position",
            "font-variant-emoji", "font-weight", "font-stretch", "font-size", "line-height",
            "font-family",
        },
        _ => null,
    };

    /// <summary>The two axis lists as the one &lt;bg-position&gt; list they stand for: paired a
    /// layer at a time, and a list shorter than the other cycles the way every background list
    /// does (CSS Backgrounds 3 §2 — measured: the axes '10px, 20px' and '5px' read back as
    /// '10px 5px, 20px 5px').</summary>
    public static string BackgroundPositionPairText(string xList, string yList)
    {
        var xs = SplitTopLevel(xList, ',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var ys = SplitTopLevel(yList, ',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (xs.Count == 0 || ys.Count == 0) return string.Empty;
        int count = Math.Max(xs.Count, ys.Count);
        var layers = new List<string>(count);
        for (int i = 0; i < count; i++)
            layers.Add(xs[i % xs.Count] + " " + ys[i % ys.Count]);
        return string.Join(", ", layers);
    }

    /// <summary>One &lt;position&gt; as the pair of axes it stands for, horizontal first: the
    /// properties that take a single position print it this way (measured: 'offset-anchor: top'
    /// reads 'center top' and 'bottom right' reads 'right bottom'), and a bare number is the
    /// length it means ('0 0' reads '0px 0px'). A token list the position grammar refuses has
    /// no pair at all, and the caller loses the declaration.</summary>
    public static string? PositionPairText(string value)
    {
        if (!TryMatchBackgroundPosition(SplitShorthand(value.Trim()), out var x, out var y)) return null;
        return PositionAxisText(x, canonicalLength: true) + " " + PositionAxisText(y, canonicalLength: true);
    }

    /// <summary>True for a token the &lt;image&gt; production of the background shorthand takes:
    /// the 'none' keyword, a url() or any of the gradient spellings (the engine's own set plus
    /// the legacy -webkit- ones, which a reference engine still resolves), and the image
    /// functions of CSS Images 4 — image-set(), cross-fade(), paint() and src().</summary>
    private static bool IsBackgroundImageToken(string token) =>
        token.Equals("none", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("url(", StringComparison.OrdinalIgnoreCase)
        || token.Contains("gradient(", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("image-set(", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("-webkit-image-set(", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("cross-fade(", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("paint(", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("src(", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a &lt;length&gt;, &lt;percentage&gt; or math function in the position of a
    /// background layer. A unitless number is only the zero it may stand for, and an angle or a
    /// keyword like 'auto' is not a position at all — 'background: url(a) auto' is a parse error
    /// that drops the declaration (measured).</summary>
    private static bool IsBackgroundPositionLength(string token)
    {
        foreach (var function in new[] { "calc(", "min(", "max(", "clamp(", "env(", "var(" })
            if (token.StartsWith(function, StringComparison.OrdinalIgnoreCase)) return true;
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out var number, out var unit))
            return false;
        if (unit.Length == 0) return number == 0;
        return unit.ToLowerInvariant() switch
        {
            "deg" or "grad" or "rad" or "turn" or "s" or "ms" or "hz" or "khz"
                or "dpi" or "dpcm" or "dppx" => false,
            _ => true,
        };
    }

    /// <summary>One token of an enumerated list, lower-cased so the longhand parsers — which all
    /// compare against the grammar's own spellings — see the keyword the page meant. A function
    /// keeps its case: 'var(--My-Var)' is case-sensitive even though 'FIXED' is not.</summary>
    private static string KeywordText(string token) =>
        token.Contains('(') || token.Contains(')') ? token : token.ToLowerInvariant();

    /// <summary>One axis of a matched &lt;position&gt;: the edge keyword it was written with and
    /// the offset measured from that edge. A bare &lt;length-percentage&gt; carries no keyword, and
    /// an axis the value never mentions is absent — which is not zero but the middle of the box.</summary>
    public readonly struct PositionAxis
    {
        public readonly bool Present;
        public readonly string? Edge;
        public readonly string? Offset;
        public PositionAxis(bool present, string? edge, string? offset)
        {
            Present = present;
            Edge = edge;
            Offset = offset;
        }
        public static readonly PositionAxis Absent = new(false, null, null);
    }

    /// <summary>CSS Position 3 §5.2 &lt;position&gt;, read as the grammar writes it: the tokens of
    /// one axis with the offset that belongs to it. This is the single place the three accepted
    /// shapes are described — the shorthand asks whether a layer's token list is a position at
    /// all, and the longhand parser resolves the axes the matcher returns, so the two cannot
    /// drift apart.</summary>
    public static bool TryMatchBackgroundPosition(IReadOnlyList<string> position,
        out PositionAxis x, out PositionAxis y)
    {
        x = y = PositionAxis.Absent;
        if (position.Count == 0) return true;

        // One term names one axis and leaves the other in the middle of the box; only the
        // vertical keywords 'top'/'bottom' move the term to the vertical axis. A term that is
        // neither an edge keyword nor a length is not a position at all.
        if (position.Count == 1)
        {
            var only = position[0];
            if (!IsPositionHorizontalTerm(only) && !IsPositionVerticalTerm(only)) return false;
            if (IsKeyword(only, "top") || IsKeyword(only, "bottom")) y = PositionTerm(only);
            else x = PositionTerm(only);
            return true;
        }

        if (position.Count > 4) return false;

        // [ left | center | right | <length-percentage> ] [ top | center | bottom | <length-percentage> ]
        // A length in the second slot is a vertical offset, so this shape is tried first: it is
        // what makes 'left 10px' the pair (0%, 10px) rather than a horizontal edge with an offset.
        if (position.Count == 2
            && IsPositionHorizontalTerm(position[0]) && IsPositionVerticalTerm(position[1]))
        {
            x = PositionTerm(position[0]);
            y = PositionTerm(position[1]);
            return true;
        }

        // [ center | [ left | right ] <length-percentage>? ] && [ center | [ top | bottom ] <length-percentage>? ]
        for (int split = 1; split < position.Count; split++)
        {
            if (IsPositionEdgeGroup(position, 0, split, horizontal: true)
                && IsPositionEdgeGroup(position, split, position.Count, horizontal: false))
            {
                x = PositionEdgeGroup(position, 0, split);
                y = PositionEdgeGroup(position, split, position.Count);
                return true;
            }
            if (IsPositionEdgeGroup(position, 0, split, horizontal: false)
                && IsPositionEdgeGroup(position, split, position.Count, horizontal: true))
            {
                y = PositionEdgeGroup(position, 0, split);
                x = PositionEdgeGroup(position, split, position.Count);
                return true;
            }
        }

        return false;
    }

    private static PositionAxis PositionTerm(string token) =>
        IsBackgroundPositionLength(token)
            ? new PositionAxis(true, null, token)
            : new PositionAxis(true, token, null);

    private static PositionAxis PositionEdgeGroup(IReadOnlyList<string> position, int start, int end) =>
        end - start == 1
            ? new PositionAxis(true, position[start], null)
            : new PositionAxis(true, position[start], position[start + 1]);

    /// <summary>True for a token list the &lt;position&gt; grammar reads. 'top 10px', '10px left',
    /// 'left 10px 20px' and 'center 10px 20px' match none of its three shapes and invalidate
    /// whatever carries them; 'left 10px' is a position while '10px left' is a parse error, and
    /// three lengths are not a position of any arity (measured).</summary>
    private static bool IsBackgroundPositionList(IReadOnlyList<string> position) =>
        TryMatchBackgroundPosition(position, out _, out _);

    private static bool IsPositionHorizontalTerm(string token) =>
        IsKeyword(token, "left") || IsKeyword(token, "center") || IsKeyword(token, "right")
        || IsBackgroundPositionLength(token);

    private static bool IsPositionVerticalTerm(string token) =>
        IsKeyword(token, "top") || IsKeyword(token, "center") || IsKeyword(token, "bottom")
        || IsBackgroundPositionLength(token);

    /// <summary>A group of the edge-and-offset form: one or two tokens, 'center' alone, or an edge
    /// keyword with the offset that is measured from it. Only a real edge carries an offset.</summary>
    private static bool IsPositionEdgeGroup(IReadOnlyList<string> position, int start, int end, bool horizontal)
    {
        var count = end - start;
        if (count is < 1 or > 2) return false;
        var head = position[start];
        var edge = horizontal
            ? IsKeyword(head, "left") || IsKeyword(head, "right") || IsKeyword(head, "center")
            : IsKeyword(head, "top") || IsKeyword(head, "bottom") || IsKeyword(head, "center");
        if (!edge) return false;
        if (count == 1) return true;
        return !IsKeyword(head, "center") && IsBackgroundPositionLength(position[start + 1]);
    }

    private static bool IsKeyword(string token, string keyword) =>
        token.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every layer of a &lt;bg-position&gt; list, each of which has to read as a position:
    /// one bad layer drops the whole declaration, as any other parse error does (measured:
    /// 'background-position: 10px 20px, top 10px' leaves the element at its earlier position).</summary>
    public static bool IsBackgroundPositionValue(string value) => EveryLayerIs(value, layer
        => IsBackgroundPositionList(SplitShorthand(layer)));

    /// <summary>Every layer of a &lt;bg-size&gt; list.</summary>
    public static bool IsBackgroundSizeValue(string value) => EveryLayerIs(value, layer
        => IsBackgroundSizeList(SplitShorthand(layer).Select(KeywordText).ToList()));

    /// <summary>Every layer of a &lt;repeat-style&gt; list: one keyword, or a horizontal and a
    /// vertical one — and 'repeat-x'/'repeat-y' describe a single axis, so they never pair
    /// (measured: 'background-repeat: repeat-x no-repeat' is a parse error).</summary>
    public static bool IsBackgroundRepeatValue(string value) => EveryLayerIs(value, layer =>
    {
        var terms = SplitShorthand(layer).Select(KeywordText).ToList();
        if (terms.Count == 1)
            return IsKeyword(terms[0], "repeat") || IsKeyword(terms[0], "repeat-x")
                || IsKeyword(terms[0], "repeat-y") || IsKeyword(terms[0], "no-repeat")
                || IsKeyword(terms[0], "space") || IsKeyword(terms[0], "round");
        if (terms.Count != 2) return false;
        return IsRepeatAxisTerm(terms[0]) && IsRepeatAxisTerm(terms[1]);
    });

    private static bool IsRepeatAxisTerm(string token) =>
        IsKeyword(token, "repeat") || IsKeyword(token, "no-repeat")
        || IsKeyword(token, "space") || IsKeyword(token, "round");

    /// <summary>Every layer of 'background-attachment': one of the three keywords, and never the
    /// 'auto' that CSS Backgrounds 3 does not define (measured: 'background-attachment: auto' is
    /// a parse error, so the element keeps the attachment it had).</summary>
    public static bool IsBackgroundAttachmentValue(string value) => EveryLayerIs(value, layer =>
    {
        var terms = SplitShorthand(layer).Select(KeywordText).ToList();
        if (terms.Count != 1) return false;
        var t = terms[0];
        return IsKeyword(t, "scroll") || IsKeyword(t, "fixed") || IsKeyword(t, "local");
    });

    /// <summary>Every layer of 'background-origin' — one box keyword a layer, never two — and of
    /// 'background-clip', which additionally takes 'text' (CSS Backgrounds 3 §4.1.1, §4.2;
    /// 'text' measured on the reference engine).</summary>
    public static bool IsBackgroundBoxValue(string value, bool allowText) => EveryLayerIs(value, layer =>
    {
        var terms = SplitShorthand(layer).Select(KeywordText).ToList();
        if (terms.Count != 1) return false;
        var t = terms[0];
        return IsKeyword(t, "padding-box") || IsKeyword(t, "border-box") || IsKeyword(t, "content-box")
            || (allowText && IsKeyword(t, "text"));
    });

    /// <summary>Every layer of 'background-position-x' or '-y': one term, or an edge keyword with
    /// the offset measured from it, and the keywords of the other axis are not accepted. The two
    /// productions CSS Position 3 §5.2 gives a single axis are the pair a 'background-position'
    /// would split between its axes, so 'right 10%' is 90% while the same two tokens in the
    /// shorthand are the horizontal edge and the vertical offset (measured).</summary>
    public static bool IsBackgroundPositionAxisValue(string value, bool horizontal) =>
        EveryLayerIs(value, layer => TryMatchBackgroundPositionAxis(layer, horizontal, out _));

    /// <summary>Matches one layer of 'background-position-x' / '-y' against its axis's own
    /// grammar and returns the axis it describes.</summary>
    public static bool TryMatchBackgroundPositionAxis(string layer, bool horizontal,
        out PositionAxis axis)
    {
        axis = PositionAxis.Absent;
        var terms = SplitShorthand(layer);
        if (terms.Count == 1)
        {
            var only = terms[0];
            if (IsBackgroundPositionLength(only) || IsPositionAxisKeyword(only, horizontal))
            {
                axis = PositionTerm(only);
                return true;
            }
            return false;
        }
        if (terms.Count == 2 && IsPositionEdgeGroup(terms, 0, 2, horizontal))
        {
            axis = PositionEdgeGroup(terms, 0, 2);
            return true;
        }
        return false;
    }

    private static bool IsPositionAxisKeyword(string token, bool horizontal) =>
        IsKeyword(token, "center")
        || (horizontal ? IsKeyword(token, "left") || IsKeyword(token, "right")
                       : IsKeyword(token, "top") || IsKeyword(token, "bottom"));

    private static bool IsLengthTerm(string token) => IsBackgroundPositionLength(token);

    /// <summary>True when every comma-separated layer of a longhand value passes 'one', with the
    /// tokenising the property's own grammar uses. A value with no comma is one layer.</summary>
    private static bool EveryLayerIs(string value, Func<string, bool> one)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (var layer in SplitTopLevel(value, ','))
        {
            var text = layer.Trim();
            if (text.Length == 0) return false;
            if (!one(text)) return false;
        }
        return true;
    }

    /// <summary>A term of the &lt;bg-size&gt; of the shorthand: one of the two keywords that size
    /// the image to the box, or a width/height. 'none' is not a size at all, in the shorthand or
    /// in the longhand: 'background: url(a) 10px / none' and 'background-size: none' are both
    /// dropped (measured).</summary>
    private static bool IsBackgroundSizeTerm(string token) =>
        IsKeyword(token, "auto") || IsKeyword(token, "cover") || IsKeyword(token, "contain")
        || IsBackgroundPositionLength(token);

    /// <summary>&lt;bg-size&gt; is one or two &lt;length-percentage&gt;/auto terms, or the single
    /// 'cover'/'contain' that sizes the whole image — 'url(a) 10px / 30px 40px 50px' and
    /// 'url(a) 10px / cover auto' are parse errors (measured), and so is the 'none' that is not
    /// in the production at all ('background-size: none' leaves the box at its earlier size).</summary>
    private static bool IsBackgroundSizeList(IReadOnlyList<string> size)
    {
        if (size.Count == 0) return true;
        if (size.Count > 2) return false;
        if (size.Count == 1) return IsBackgroundSizeTerm(size[0]);
        return IsBackgroundSizeAxisTerm(size[0]) && IsBackgroundSizeAxisTerm(size[1]);
    }

    /// <summary>One side of a two-term &lt;bg-size&gt;: 'cover' and 'contain' size the image to the
    /// box and therefore have no second term, so a pair is only auto and lengths (measured:
    /// 'auto cover' and 'contain auto' are both parse errors).</summary>
    private static bool IsBackgroundSizeAxisTerm(string token) =>
        IsKeyword(token, "auto") || IsBackgroundPositionLength(token);

    /// <summary>The parts a 'font' shorthand carries. Everything is in the spelling the engine
    /// prints: keywords lower-cased, the line-height left out when it says nothing.</summary>
    public sealed class FontShorthandParts
    {
        public required string Size { get; init; }
        public required string Family { get; init; }
        public string? Style { get; init; }
        public string? Variant { get; init; }
        public string? Weight { get; init; }
        public string? Stretch { get; init; }
        public string? LineHeight { get; init; }
        /// <summary>Set when the whole value was one of the six system-font keywords
        /// (CSS Fonts 4 §5.4). It stands for a size, a family and a weight the platform owns, so
        /// none of those three are carried here.</summary>
        public string? SystemFont { get; init; }
    }

    /// <summary>The system-font keywords, in the spelling the engine prints. Measured against the
    /// reference engine on this platform, all six resolve to the same UI face and size — 16px Arial —
    /// and the shorthand's own echo is the keyword as written.</summary>
    private static readonly string[] SystemFontKeywords =
        { "caption", "menu", "icon", "message-box", "small-caption", "status-bar" };

    internal static bool IsSystemFontKeyword(string token)
    {
        foreach (var keyword in SystemFontKeywords)
            if (token.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The face a system-font keyword stands for: one answer, read both by the expansion
    /// that fills the longhands and by the applier that fills the style, so the CSSOM's list of
    /// longhands and getComputedStyle cannot disagree about what 'caption' means (measured: every one
    /// of the six keywords resolves to 16px Arial on this platform, while the shorthand still echoes
    /// the keyword the page wrote).</summary>
    public static (string Size, string Family) SystemFontFace() => ("16px", "Arial");

    /// <summary>Read a 'font' shorthand (CSS Fonts 4 §5.2): <c>[ style || variant || weight ||
    /// stretch ]? size [ / line-height ]? family</c>. Three things the engine used to get wrong are
    /// decided here, once, for the grammar, the echo and the longhands alike:
    /// <list type="bullet">
    /// <item>a size and a family are both required (measured: 'bold 12px' and '12px' are dropped);</item>
    /// <item>a word before the size that is none of the four categories is no value at all
    /// (measured: 'line-height 12px serif' is dropped — it is not a family name waiting early);</item>
    /// <item>after the size everything is the family list, and a list that does not read as one is
    /// dropped ('12px serif extra', where a generic name is followed by a second word).</item>
    /// </list>
    /// 'oblique' may carry the angle it leans by, and 'normal' fills a slot without being said.</summary>
    public static FontShorthandParts? ParseFontShorthand(string value)
    {
        var single = value.Trim();
        if (!single.Contains(' ') && IsSystemFontKeyword(single))
            return new FontShorthandParts { Size = string.Empty, Family = string.Empty, SystemFont = single };
        string? style = null, variant = null, weight = null, stretch = null;
        string? size = null, lineHeight = null;
        var after = new List<string>();
        var tokens = SplitFontShorthand(value);
        for (int index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index].Trim();
            if (token.Length == 0) continue;
            var p = token.ToLowerInvariant();
            if (size != null)
            {
                after.Add(p == "/" ? "/" : token);
                continue;
            }
            if (p.Contains('/') && TrySizeThenLineHeight(p, out var glued, out var gluedLine))
            {
                size = glued; lineHeight = gluedLine;
                continue;
            }
            if (IsFontSize(p) || IsFontSizeLength(p)) { size = p; continue; }
            // 'oblique' may carry the angle it leans by (CSS Fonts 4 §3.2), and the angle is part
            // of the style rather than a word of its own (measured: 'oblique 40deg 12px serif'
            // reads back whole, where the engine used to lose it into the family list).
            if (p is "italic" or "oblique")
            {
                if (style != null) return null;
                if (p == "oblique" && index + 1 < tokens.Count && IsAngle(tokens[index + 1]))
                { style = "oblique " + tokens[++index].Trim().ToLowerInvariant(); continue; }
                style = p;
                continue;
            }
            if (p == "small-caps") { if (variant != null) return null; variant = p; continue; }
            // 'normal' fills the next slot the grammar has and is not printed at all.
            if (p == "normal") continue;
            if (p is "bold" or "bolder" or "lighter"
                || (int.TryParse(p, out var number) && number >= 1 && number <= 1000))
            {
                if (weight != null) return null;
                weight = p;
                continue;
            }
            if (IsFontStretch(p))
            {
                if (stretch != null) return null;
                stretch = p;
                continue;
            }
            return null;
        }

        if (size == null) return null;
        var familyStart = 0;
        if (after.Count > 0 && after[0] == "/")
        {
            if (after.Count < 2) return null;
            lineHeight = after[1];
            familyStart = 2;
        }
        if (familyStart >= after.Count) return null;
        var family = string.Join(" ", after.Skip(familyStart));
        if (!IsFamilyList(family)) return null;
        return new FontShorthandParts
        {
            Size = size, Family = family, Style = style, Variant = variant,
            Weight = weight, Stretch = stretch, LineHeight = lineHeight,
        };
    }

    /// <summary>'12px/1.5' as one token: a size, a slash, and whatever the line-height says.</summary>
    private static bool TrySizeThenLineHeight(string token, out string size, out string lineHeight)
    {
        size = string.Empty; lineHeight = string.Empty;
        int slash = token.IndexOf('/');
        if (slash <= 0 || slash == token.Length - 1) return false;
        var head = token[..slash];
        var tail = token[(slash + 1)..];
        if (!IsFontSize(head) && !IsFontSizeLength(head)) return false;
        size = head;
        lineHeight = tail;
        return true;
    }

    /// <summary>An angle: a number with the unit that says degrees, or a bare number for the
    /// properties that take one without.</summary>
    private static bool IsAngle(string token)
        => token.EndsWith("deg", StringComparison.OrdinalIgnoreCase)
            || token.EndsWith("grad", StringComparison.OrdinalIgnoreCase)
            || token.EndsWith("rad", StringComparison.OrdinalIgnoreCase)
            || token.EndsWith("turn", StringComparison.OrdinalIgnoreCase);

    /// <summary>A comma list of family names, each one a quoted string or a run of identifiers —
    /// except that a generic name is a family of its own (measured: 'serif extra' is dropped while
    /// 'serif, sans-serif' and 'Times New Roman' stand).</summary>
    private static bool IsFamilyList(string family)
    {
        var entries = family.Split(',');
        if (entries.Length == 0) return false;
        foreach (var entry in entries)
        {
            var text = entry.Trim();
            if (text.Length == 0) return false;
            if (text[0] is '"' or '\'')
            {
                if (text.Length < 2 || text[^1] != text[0]) return false;
                continue;
            }
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return false;
            if (Acrux.Core.Css.CssValueText.IsGenericFamilyName(words[0]))
            {
                if (words.Length != 1) return false;
                continue;
            }
            foreach (var word in words)
                if (!Acrux.Core.Css.CssValueGrammar.IsIdentifierLike(word)) return false;
        }
        return true;
    }

    private static void ExpandFont(Dictionary<string, string> result, string value)
    {
        var parts = ParseFontShorthand(value);
        if (parts == null) return;
        // CSS Fonts 4 §5.3: the shorthand resets every part it does not carry, so the longhands
        // it does not name go back to their initials rather than keeping what the page said.
        if (parts.SystemFont != null)
        {
            var system = SystemFontFace();
            result["font-style"] = "normal";
            result["font-variant"] = "normal";
            result["font-variant-caps"] = "normal";
            result["font-weight"] = "normal";
            result["font-stretch"] = "normal";
            result["font-size"] = system.Size;
            result["line-height"] = "normal";
            result["font-family"] = system.Family;
            return;
        }
        result["font-style"] = parts.Style ?? "normal";
        result["font-variant-caps"] = parts.Variant ?? "normal";
        result["font-variant"] = "normal";
        result["font-weight"] = parts.Weight ?? "normal";
        result["font-stretch"] = parts.Stretch ?? "normal";
        result["font-size"] = parts.Size;
        result["line-height"] = parts.LineHeight ?? "normal";
        result["font-family"] = JoinFamilyList(parts.Family);
    }


    /// <summary>A font-family list in the spelling the platform prints: the families separated
    /// by ', '. Authors may write the list with the commas tight against the names, or with the
    /// separators as whitespace, or both at once ('Arial,sans serif', 'Arial, serif'), and all
    /// three read back the same way. Quotes are kept, so a family whose name carries a comma
    /// stays one family.</summary>
    internal static string JoinFamilyList(string families)
    {
        var entries = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(families, ',')
            .Select(f => f.Trim())
            .Where(f => f.Length > 0);
        return string.Join(", ", entries);
    }

    /// <summary>
    /// Split a font shorthand value on whitespace outside of quoted strings, so
    /// that quoted family names like "Times New Roman" stay a single token.
    /// </summary>
    private static List<string> SplitFontShorthand(string value)
    {
        var parts = new List<string>();
        int start = 0;
        char quote = '\0';
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'')
                quote = c;
            else if (c == ' ')
            {
                if (i > start) parts.Add(value[start..i]);
                start = i + 1;
            }
        }
        if (start < value.Length) parts.Add(value[start..]);
        return parts;
    }

    private static bool IsFontStretch(string p) => IsOneOfKeyword(p, "ultra-condensed",
        "extra-condensed", "semi-condensed", "condensed", "semi-expanded", "expanded",
        "extra-expanded", "ultra-expanded", "wider", "narrower");

    private static bool IsFontSizeLength(string p)
    {
        foreach (var unit in new[] { "px", "em", "rem", "pt", "pc", "in", "cm", "mm", "ex", "ch" })
            if (p.EndsWith(unit) && float.TryParse(p[..^unit.Length], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                return true;
        return p.EndsWith("%") && float.TryParse(p[..^1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    private static void ExpandFlex(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        if (parts.Count == 0) return;

        string grow = null, shrink = null, basis = null;
        int numberCount = 0;

        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p == "none")
            {
                grow = "0"; shrink = "0"; basis = "auto";
                numberCount = 0;
            }
            else if (p == "auto")
            {
                if (basis == null) basis = "auto";
            }
            else if (p == "initial")
            {
                grow = "0"; shrink = "1"; basis = "auto";
            }
            else if (IsFlexBasisLength(p))
            {
                basis = p;
            }
            else if (float.TryParse(p, out _))
            {
                numberCount++;
                if (grow == null) grow = p;
                else if (shrink == null) shrink = p;
            }
        }

        if (grow == null) grow = numberCount > 0 ? "0" : "1";
        if (shrink == null) shrink = "1";
        if (basis == null)
        {
            basis = numberCount > 0 ? "0%" : "auto";
        }

        result["flex-grow"] = grow;
        result["flex-shrink"] = shrink;
        result["flex-basis"] = basis;
    }

    private static bool IsFlexBasisLength(string p)
    {
        if (string.IsNullOrEmpty(p)) return false;
        if (p.EndsWith("px", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.EndsWith("em", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.EndsWith("%", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.EndsWith("vh", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.EndsWith("vw", StringComparison.OrdinalIgnoreCase)) return true;
        // A bare "0" is NOT a flex-basis here: in the 'flex' shorthand the leading
        // numbers are grow/shrink, so "0" must fall through to the number branch
        // (treating it as a unitless basis would swallow grow and default it to 1).
        return false;
    }

    private static void ExpandFlexFlow(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p is "row" or "row-reverse" or "column" or "column-reverse")
                result["flex-direction"] = p;
            else if (p is "nowrap" or "wrap" or "wrap-reverse")
                result["flex-wrap"] = p;
        }
    }

    private static void ExpandGridArea(Dictionary<string, string> result, string value)
    {
        // Spec: grid-area: <grid-line> [ / <grid-line> ]{0,3} (CSS Grid 1 §8.5), so ONE value is
        // the row-start alone. One exception, measured: a single <custom-ident> — 'main', 'bogus',
        // 'none' — is the named-area form and fills all four lines, while a single integer ('2') or
        // a span ('span 2') sets only the row-start and leaves the other three 'auto'.
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (parts.Length)
        {
            case 1:
                result["grid-row-start"] = parts[0];
                if (NamesAnArea(parts[0]))
                {
                    result["grid-column-start"] = parts[0];
                    result["grid-row-end"] = parts[0];
                    result["grid-column-end"] = parts[0];
                }
                break;
            case 2:
                result["grid-row"] = parts[0];
                result["grid-column"] = parts[1];
                break;
            case 3:
                result["grid-row-start"] = parts[0];
                result["grid-column-start"] = parts[1];
                result["grid-row-end"] = parts[2];
                break;
            default:
                result["grid-row-start"] = parts[0];
                result["grid-column-start"] = parts[1];
                result["grid-row-end"] = parts[2];
                result["grid-column-end"] = parts[3];
                break;
        }
    }

    /// <summary>Whether one &lt;grid-line&gt; is a line NAME rather than a position: 'auto', an
    /// integer and 'span N' are positions, and everything else a &lt;custom-ident&gt; allows is a
    /// name the named-area form is written with (CSS Grid 1 §8.5, measured: 'main', 'bogus' and
    /// 'none' each fill all four lines while '2' and 'span 2' fill only the row-start).</summary>
    internal static bool NamesAnArea(string text)
    {
        var word = text.Trim();
        if (word.Length == 0) return false;
        if (word.Equals("auto", StringComparison.OrdinalIgnoreCase)) return false;
        if (word.StartsWith("span", StringComparison.OrdinalIgnoreCase)) return false;
        int i = word[0] is '+' or '-' ? 1 : 0;
        bool digits = false;
        while (i < word.Length && char.IsAsciiDigit(word[i])) { digits = true; i++; }
        if (digits && i == word.Length) return false;
        return true;
    }

    private static void ExpandGridLine(Dictionary<string, string> result, string prop, string value)
    {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1) result[$"{prop}-start"] = parts[0].Trim();
        if (parts.Length >= 2) result[$"{prop}-end"] = parts[1].Trim();
    }

    /// <summary>
    /// Expand the <c>animation</c> shorthand (CSS Animations 1 §2).
    ///
    /// The shorthand is a comma-separated list, and every list is expanded per
    /// entry so the longhands stay aligned. Order within one entry is free, the
    /// first &lt;time&gt; is the duration and the second is the delay, and a
    /// negative time is unambiguously a delay (a duration may not be negative).
    /// </summary>
    private static void ExpandAnimation(Dictionary<string, string> result, string value)
    {
        var entries = ClassifyAnimationEntries(value);
        var names = new List<string>(entries.Count);
        var durations = new List<string>(entries.Count);
        var easings = new List<string>(entries.Count);
        var delays = new List<string>(entries.Count);
        var iterations = new List<string>(entries.Count);
        var directions = new List<string>(entries.Count);
        var fills = new List<string>(entries.Count);
        var plays = new List<string>(entries.Count);
        foreach (var part in entries)
        {
            durations.Add(part[0].Length > 0 ? part[0] : "0s");
            easings.Add(part[1].Length > 0 ? part[1] : "ease");
            delays.Add(part[2].Length > 0 ? part[2] : "0s");
            iterations.Add(part[3].Length > 0 ? part[3] : "1");
            directions.Add(part[4].Length > 0 ? part[4] : "normal");
            fills.Add(part[5].Length > 0 ? part[5] : "none");
            plays.Add(part[6].Length > 0 ? part[6] : "running");
            names.Add(part[7].Length > 0 ? part[7] : "none");
        }

        result["animation-name"] = string.Join(", ", names);
        result["animation-duration"] = string.Join(", ", durations);
        result["animation-timing-function"] = string.Join(", ", easings);
        result["animation-delay"] = string.Join(", ", delays);
        result["animation-iteration-count"] = string.Join(", ", iterations);
        result["animation-direction"] = string.Join(", ", directions);
        result["animation-fill-mode"] = string.Join(", ", fills);
        result["animation-play-state"] = string.Join(", ", plays);
    }

    /// <summary>
    /// The eight parts of every entry of an <c>animation</c> value, in the order the shorthand
    /// prints them — duration, timing function, delay, iteration count, direction, fill mode,
    /// play state, name — with an empty string where the page wrote nothing.
    /// <para>
    /// One classifier serves both readers: <see cref="ExpandAnimation"/> fills the gaps with the
    /// longhand initials, while the specified-value printer fills them with the initials the
    /// shorthand prints, which for the duration is <c>auto</c> and not <c>0s</c> (measured:
    /// <c>animation: ease</c> reads back <c>auto ease 0s 1 normal none running none</c>). The name
    /// keeps the case it was written with, because it is a custom identifier — <c>BOGUS</c> comes
    /// back as <c>BOGUS</c> — while a keyword slot is lower-cased, which is how the engine reads it
    /// either way.
    /// </para>
    /// <para>
    /// The order inside one entry is free; the first <c>&lt;time&gt;</c> is the duration and the
    /// second the delay, and a negative time is unambiguously a delay (a duration may not be
    /// negative). <c>none</c> is both a fill mode and the initial name, so it is the name until a
    /// name is present and the fill mode afterwards: <c>f 1s none</c> means name=f fill=none while
    /// <c>none</c> alone means no animation at all (CSS Animations 1 §2.11).
    /// </para>
    /// </summary>
    internal static List<string[]> ClassifyAnimationEntries(string value)
    {
        var entries = SplitTopLevel(value, ',');
        if (entries.Count == 0) entries.Add("");
        var parts = new List<string[]>(entries.Count);

        foreach (var entry in entries)
        {
            // 0 duration, 1 easing, 2 delay, 3 iteration, 4 direction, 5 fill, 6 play, 7 name
            var slot = new string[8];
            for (int i = 0; i < slot.Length; i++) slot[i] = string.Empty;
            bool haveDuration = false, haveDelay = false;
            bool nameSeen = false;

            foreach (var token in SplitShorthand(entry))
            {
                var text = token.Trim();
                var p = text.ToLowerInvariant();
                if (p.Length == 0) continue;

                if (IsTimeToken(p))
                {
                    // A negative literal can only be the delay; a non-negative one fills the
                    // duration first, then the delay (measured, same as the transition shorthand).
                    if (IsNegativeTimeLiteral(p))
                    {
                        if (!haveDelay) { slot[2] = p; haveDelay = true; }
                    }
                    else if (!haveDuration) { slot[0] = p; haveDuration = true; }
                    else if (!haveDelay) { slot[2] = p; haveDelay = true; }
                }
                // 'auto' is a duration the page can write (CSS Animations 2 takes the initial value
                // of 'animation-duration' out of hiding), and the reference engine reads it there —
                // 'auto 1s slide' is duration auto, delay 1s, name slide (measured).
                else if (p == "auto" && !haveDuration) { slot[0] = p; haveDuration = true; }
                else if (IsEasingToken(p)) slot[1] = p;
                else if (p == "infinite" || IsNumberToken(p)) slot[3] = p;
                else if (p is "normal" or "reverse" or "alternate" or "alternate-reverse") slot[4] = p;
                else if (p is "running" or "paused") slot[6] = p;
                else if (p == "none" && !nameSeen) { slot[7] = p; nameSeen = true; }
                else if (p is "none" or "forwards" or "backwards" or "both")
                {
                    slot[5] = p;
                    nameSeen = true;
                }
                else { slot[7] = text; nameSeen = true; }
            }
            parts.Add(slot);
        }
        return parts;
    }

    /// <summary>
    /// Expand the <c>transition</c> shorthand (CSS Transitions 1 §2.2).
    /// A comma-separated list where every longhand is itself a list; the first
    /// &lt;time&gt; is the duration and the second is the delay.
    /// </summary>
    private static void ExpandTransition(Dictionary<string, string> result, string value)
    {
        var entries = SplitTopLevel(value, ',');
        if (entries.Count == 0) entries.Add("");

        var properties = new List<string>(entries.Count);
        var durations = new List<string>(entries.Count);
        var easings = new List<string>(entries.Count);
        var delays = new List<string>(entries.Count);

        foreach (var entry in entries)
        {
            string property = "all", duration = "0s", easing = "ease", delay = "0s";
            bool haveDuration = false, haveDelay = false;
            bool invalid = false;

            foreach (var token in SplitShorthand(entry))
            {
                var p = token.Trim().ToLowerInvariant();
                if (p.Length == 0) continue;

                if (IsTimeToken(p))
                {
                    // A negative literal can only be the delay; a non-negative one fills the
                    // duration first, then the delay (CSS Transitions 1 §2.2, measured). A time
                    // that fits neither slot makes the whole declaration invalid — so two
                    // negatives ('all -2s -3s') drops the shorthand entirely, as the reference does.
                    if (IsNegativeTimeLiteral(p))
                    {
                        if (haveDelay) { invalid = true; break; }
                        delay = p; haveDelay = true;
                    }
                    else if (!haveDuration) { duration = p; haveDuration = true; }
                    else if (!haveDelay) { delay = p; haveDelay = true; }
                    else { invalid = true; break; }
                }
                else if (IsEasingToken(p)) easing = p;
                else property = p;
            }

            if (invalid) return;

            properties.Add(property);
            durations.Add(duration);
            easings.Add(easing);
            delays.Add(delay);
        }

        result["transition-property"] = string.Join(", ", properties);
        result["transition-duration"] = string.Join(", ", durations);
        result["transition-timing-function"] = string.Join(", ", easings);
        result["transition-delay"] = string.Join(", ", delays);
    }

    /// <summary>True for a &lt;time&gt; token: an optional sign, digits, and a s/ms unit — or one math
    /// function that resolves to a time. CSS Transitions 1 §2.2 puts a math duration in the same slot
    /// a plain one fills: 'transition: calc(1s + 1ms) ease' is a duration and leaves the property at
    /// 'all', while 'transition: calc(1px) ease' is no transition at all (measured, both).</summary>
    internal static bool IsTimeToken(string token)
    {
        var t = token.Trim().ToLowerInvariant();
        if (t.Length < 2) return false;
        if (t.EndsWith("ms", StringComparison.Ordinal))
            return IsNumberToken(t[..^2]);
        if (t.EndsWith("s", StringComparison.Ordinal))
            return IsNumberToken(t[..^1]);
        return IsMathTimeToken(t);
    }

    /// <summary>A plain negative time literal — the one case a shorthand cannot put in the duration
    /// slot. A duration is a non-negative <c>&lt;time&gt;</c>, so 'transition: all -2s' reads the
    /// negative as the delay and leaves the duration at its initial '0s' (measured); '-0s' is zero and
    /// stays a duration, and a calc() is not a literal so it is left for the computed-value clamp.</summary>
    private static bool IsNegativeTimeLiteral(string token)
    {
        var t = token.Trim();
        if (t.Length < 2 || t[0] != '-') return false;
        int i = 1;
        bool nonZero = false;
        for (; i < t.Length && (char.IsAsciiDigit(t[i]) || t[i] == '.'); i++)
            if (char.IsAsciiDigit(t[i]) && t[i] != '0') nonZero = true;
        var unit = t[i..];
        bool timedUnit = unit.Equals("s", StringComparison.OrdinalIgnoreCase)
            || unit.Equals("ms", StringComparison.OrdinalIgnoreCase);
        return timedUnit && nonZero;
    }

    /// <summary>Whether the whole token is one call of a math function and what it comes out to is a
    /// time — or is not yet known, which a 'var()' leaves it.</summary>
    private static bool IsMathTimeToken(string token)
    {
        if (!token.EndsWith(")", StringComparison.Ordinal)) return false;
        var calls = CssMathValue.MathCalls(token);
        if (calls.Count != 1 || calls[0].Start != 0 || calls[0].Length != token.Length) return false;
        return CssMathValue.WellTyped(token, out var category) && (category == "time" || category == null);
    }

    internal static bool IsNumberToken(string token)
    {
        var t = token.Trim();
        if (t.Length == 0) return false;
        int i = t[0] == '+' || t[0] == '-' ? 1 : 0;
        if (i >= t.Length) return false;
        bool digits = false, dot = false;
        for (; i < t.Length; i++)
        {
            if (t[i] >= '0' && t[i] <= '9') { digits = true; continue; }
            if (t[i] == '.' && !dot) { dot = true; continue; }
            return false;
        }
        return digits;
    }

    /// <summary>True for a keyword easing or a <c>cubic-bezier()</c>/<c>steps()</c>/<c>linear()</c> call.</summary>
    internal static bool IsEasingToken(string token)
    {
        var p = token.Trim().ToLowerInvariant();
        if (p is "linear" or "ease" or "ease-in" or "ease-out" or "ease-in-out"
            or "step-start" or "step-end")
        {
            return true;
        }
        return p.StartsWith("cubic-bezier(", StringComparison.Ordinal)
            || p.StartsWith("steps(", StringComparison.Ordinal)
            || p.StartsWith("linear(", StringComparison.Ordinal);
    }

    /// <summary>'outline' is <'outline-color'> || <'outline-style'> || <'outline-width'>
    /// (CSS UI 4 §5.1): the three parts may come in any order, but each may come only once —
    /// a second length or a second style is not a value the grammar has, and the whole
    /// declaration is then dropped rather than the last part winning it (measured:
    /// 'outline: 2px dashed rgb(1,2,3) 4px' leaves the specified value empty and the three
    /// longhands at their initial values).</summary>
    /// <summary>'place-content', 'place-items' and 'place-self' are the two-axis shorthands of
    /// CSS Box Alignment 3 §5–7: one value addresses both axes, two give the block axis first
    /// and the inline axis second.</summary>
    private static void ExpandPlace(Dictionary<string, string> result, string value, string blockAxis, string inlineAxis)
    {
        var parts = SplitShorthand(value);
        if (parts.Count == 0) return;
        result[blockAxis] = parts[0];
        result[inlineAxis] = parts.Count >= 2 ? parts[1] : parts[0];
    }

    /// <summary>'list-style' is <'list-style-position'> || <'list-style-image'> ||
    /// <'list-style-type'> (CSS Lists 3 §3.3), in any order. 'none' is a value of both the image
    /// and the type, so it sets the two of them (measured: 'list-style: none' computes to
    /// 'outside none none'), while a keyword only the type understands becomes the type.
    /// A shorthand sets every longhand it controls, so a part the value stays silent about is
    /// written with its initial value rather than dropped: 'list-style: lower-greek' on an li
    /// inside a 'list-style-position: inside' list still computes to 'outside' (measured).</summary>
    private static void ExpandListStyle(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        string? position = null, image = null, type = null;
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p is "inside" or "outside")
            {
                if (position != null) return;
                position = p;
            }
            else if (p == "none")
            {
                image ??= "none";
                type ??= "none";
            }
            else if (p.StartsWith("url(", StringComparison.Ordinal) || p.StartsWith("image(", StringComparison.Ordinal)
                     || p.StartsWith("-image(", StringComparison.Ordinal) || p.StartsWith("-webkit-", StringComparison.Ordinal))
            {
                if (image != null) return;
                image = part;  // a url() is case-sensitive, so it keeps the authored spelling
            }
            else if (IsListStyleTypeToken(p, part))
            {
                if (type != null) return;
                type = part;
            }
            else return;  // a token none of the three parts can take
        }
        result["list-style-position"] = position ?? "outside";
        result["list-style-image"] = image ?? "none";
        result["list-style-type"] = type ?? "disc";
    }

    /// <summary>What the type part of 'list-style' may be: one of the preset keywords, a quoted
    /// string, or a counter-style name / &lt;counter()&gt;-&lt;symbols()&gt; form (CSS Lists 3 §5.3).</summary>
    private static bool IsListStyleTypeToken(string lowered, string authored) =>
        CssPropertyApplier.MatchListStyleType(lowered).HasValue
        || (authored.Length > 1 && (authored[0] == '"' || authored[0] == '\'')
            && authored[^1] == authored[0])
        || lowered.StartsWith("counter(", StringComparison.Ordinal)
        || lowered.StartsWith("symbols(", StringComparison.Ordinal);

    /// <summary>'border-block' and 'border-inline' are <'border-block-start'> ||
    /// <'border-block-end'> (CSS Logical Properties 1 §5.1): each side is its own unordered
    /// width/style/color triple, so a component written twice splits over the two sides and a
    /// component written once serves both of them.</summary>
    private static void ExpandLogicalBorder(Dictionary<string, string> result, string axis, string value)
    {
        var parts = SplitShorthand(value);
        var widths = new List<string>();
        var styles = new List<string>();
        var colors = new List<string>();
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (IsBorderStyle(p)) styles.Add(p);
            else if (IsBorderWidth(p)) widths.Add(p);
            else if (IsColor(p)) colors.Add(p);
            else return;
        }
        if (widths.Count > 2 || styles.Count > 2 || colors.Count > 2) return;

        EmitLogicalBorderSide(result, axis, "start", Share(widths, 0), Share(styles, 0), Share(colors, 0));
        EmitLogicalBorderSide(result, axis, "end", Share(widths, 1), Share(styles, 1), Share(colors, 1));
    }

    private static void EmitLogicalBorderSide(Dictionary<string, string> result, string axis, string end,
        string? width, string? style, string? color)
    {
        if (width == null && style == null && color == null) return;
        // A side the shorthand says nothing about keeps the initial triple, which is what the
        // cascade reset through GetControlledLonghands would do anyway; naming them here keeps
        // the two paths ('border-inline: 2px' for both sides, 'border-inline-start: 2px' for one)
        // from disagreeing about what the other side ends up with.
        result[$"border-{axis}-{end}-width"] = width ?? "medium";
        result[$"border-{axis}-{end}-style"] = style ?? "none";
        result[$"border-{axis}-{end}-color"] = color ?? "currentcolor";
    }

    /// <summary>The value of component number |index| of a list that may carry one or two, where
    /// a lone component applies to both sides.</summary>
    private static string? Share(List<string> values, int index) =>
        values.Count > index ? values[index] : (values.Count == 1 ? values[0] : null);

    /// <summary>'border-block-width', '-style' and '-color' address the two sides of one axis
    /// with the same one-or-two-value shape the box-side shorthands use.</summary>
    private static void ExpandLogicalBorderComponent(Dictionary<string, string> result, string axis,
        string component, string value)
    {
        var parts = SplitShorthand(value);
        if (parts.Count == 0) return;
        result[$"border-{axis}-start-{component}"] = parts[0];
        result[$"border-{axis}-end-{component}"] = parts.Count >= 2 ? parts[1] : parts[0];
    }

    /// <summary>'outline' is <'outline-width'> || <'outline-style'> || <'outline-color'>
    /// (CSS UI 4 §4.2). The legacy 'invert' colour is not part of the grammar any more, so a
    /// shorthand carrying it is dropped whole rather than losing just the colour part.</summary>
    private static void ExpandOutline(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        string? style = null, width = null, color = null;
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p == "none" || p == "hidden" || IsBorderStyle(p))
            {
                if (style != null) return;
                style = p;
            }
            else if (IsBorderWidth(p))
            {
                if (width != null) return;
                width = p;
            }
            else if (IsColor(p))
            {
                if (color != null) return;
                color = p;
            }
            else return;  // a token none of the three parts can be
        }
        // Only the parts the declaration carried are emitted; the rest are reset by the
        // cascade through GetControlledLonghands, which is how every other shorthand here
        // clears the longhands it was not given.
        if (style != null) result["outline-style"] = style;
        if (width != null) result["outline-width"] = width;
        if (color != null) result["outline-color"] = color;
    }

    private static void ExpandTextDecoration(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        // The shorthand resets every longhand it does not carry.
        result["text-decoration-line"] = "none";
        result["text-decoration-style"] = "solid";
        // The reset writes the longhand's own initial 'auto', not the colour that stands behind
        // it: the reference engine distinguishes the two (measured — after
        // 'text-decoration: blink' the longhand's specified value reads 'initial' and the
        // shorthand prints 'blink' with no colour, while the computed colour is the element's).
        result["text-decoration-color"] = "auto";
        result["text-decoration-thickness"] = "auto";

        var lines = new List<string>();
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p is "underline" or "overline" or "line-through"
                or "blink" or "spelling-error" or "grammar-error")
            {
                // 'text-decoration-line' is a list, so several keywords may appear.
                if (!lines.Contains(p)) lines.Add(p);
            }
            else if (p is "solid" or "double" or "dotted" or "dashed" or "wavy")
                result["text-decoration-style"] = p;
            else if (IsTextDecorationThickness(p))
                result["text-decoration-thickness"] = p;
            else if (IsColor(p))
                result["text-decoration-color"] = p;
        }
        if (lines.Count > 0)
            result["text-decoration-line"] = string.Join(' ', lines);
    }

    /// <summary>A bare length or percentage in the 'text-decoration' shorthand is the thickness
    /// (CSS Text Decoration 4 adds it to the shorthand grammar), next to the two keywords that name
    /// it. The printer of the same shorthand has to agree with the expander about which token that
    /// is — otherwise a width is read as the colour beside it and the value the engine keeps loses
    /// the thickness the page asked for.</summary>
    internal static bool IsTextDecorationThickness(string token) =>
        token is "auto" or "from-font" || Dom.Length.IsLength(token);

    private static void ExpandTextEmphasis(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        result["text-emphasis-style"] = "none";
        result["text-emphasis-color"] = "currentcolor";

        var markParts = new List<string>();
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (IsColor(p))
                result["text-emphasis-color"] = p;
            else
                markParts.Add(p);
        }

        if (markParts.Count > 0)
        {
            var style = string.Join(" ", markParts).Trim();
            if (style == "auto") style = "filled dot";
            result["text-emphasis-style"] = style;
        }
    }

    private static void ExpandGap(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        if (parts.Count >= 2)
        {
            result["row-gap"] = parts[0].Trim();
            result["column-gap"] = parts[1].Trim();
        }
        else if (parts.Count == 1)
        {
            result["row-gap"] = parts[0].Trim();
            result["column-gap"] = parts[0].Trim();
        }
    }

    /// <summary>
    /// A5: `column-rule: &lt;width&gt; || &lt;style&gt; || &lt;color&gt;`.
    /// </summary>
    private static void ExpandColumnRule(Dictionary<string, string> result, string value)
    {
        foreach (var part in SplitShorthand(value))
        {
            var p = part.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(p)) continue;
            if (IsBorderStyle(p))
                result["column-rule-style"] = p;
            else if (IsBorderWidth(p))
                result["column-rule-width"] = p;
            else if (IsColor(p))
                result["column-rule-color"] = p;
        }
    }

    /// <summary>
    /// `columns: &lt;'column-width'&gt; || &lt;'column-count'&gt;` — per css-multicol
    /// each of width/count may appear once in any order; an integer token is the
    /// count, everything else (including `auto`) is the width.
    /// </summary>
    private static void ExpandColumns(Dictionary<string, string> result, string value)
    {
        // columns: <column-width> || <column-count>, each at most once. A repeated
        // component makes the declaration invalid, and an invalid shorthand expands
        // to nothing (the controlled longhands then fall back to their initial
        // values instead of being fed a bogus width or count).
        bool hasCount = false;
        bool hasWidth = false;
        foreach (var part in SplitShorthand(value))
        {
            var p = part.Trim();
            if (p.Length == 0 || p.Equals("auto", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(p, out int count))
            {
                if (hasCount || count <= 0) { result.Clear(); return; }
                hasCount = true;
                result["column-count"] = p;
            }
            else
            {
                if (hasWidth) { result.Clear(); return; }
                hasWidth = true;
                result["column-width"] = p;
            }
        }
    }

    private static void ExpandOverflow(Dictionary<string, string> result, string value)
    {        var parts = SplitShorthand(value);
        if (parts.Count >= 2)
        {
            result["overflow-x"] = parts[0].Trim();
            result["overflow-y"] = parts[1].Trim();
        }
        else
        {
            result["overflow-x"] = parts[0].Trim();
            result["overflow-y"] = parts[0].Trim();
        }
    }

    internal static List<string> SplitShorthand(string value)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            // Only top-level whitespace separates tokens: 'rgb(10, 20, 30)' and
            // 'calc(100% - 10px)' must stay in one piece or the caller mis-reads
            // their fragments as extra widths and loses the real values.
            else if (char.IsWhiteSpace(value[i]) && depth == 0)
            {
                if (i > start) parts.Add(value[start..i]);
                start = i + 1;
            }
        }
        if (start < value.Length) parts.Add(value[start..]);
        return parts;
    }

    // Split on a top-level delimiter, ignoring occurrences nested inside
    // parentheses (so commas inside gradient/color functions are preserved).
    private static List<string> SplitTopLevel(string value, char delimiter)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == delimiter && depth == 0)
            {
                parts.Add(value[start..i]);
                start = i + 1;
            }
        }
        parts.Add(value[start..]);
        return parts;
    }

    /// <summary>The &lt;border-style-keyword&gt; set. CSS keywords are ASCII case-insensitive and a
    /// reference engine reads 'border-top: 1px SOLID red' as a solid border, so a test that compares
    /// the authored spelling drops a working declaration whole (measured, and the reason this one
    /// folds the case itself rather than trusting its callers).
    /// </summary>
    internal static bool IsBorderStyle(string p) => IsOneOfKeyword(p,
        "none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset");
    private static bool IsBorderWidth(string p) => CssPropertyApplier.IsBorderWidthToken(p);

    /// <summary>Whether a token is one of a set of CSS keywords, compared the way CSS compares
    /// them: ASCII-case-insensitively (CSS Syntax 3 §4).</summary>
    private static bool IsOneOfKeyword(string token, params string[] keywords)
    {
        foreach (var keyword in keywords)
            if (token.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    private static bool IsColor(string p) => ColorParser.IsColorToken(p);
    private static bool IsFontSize(string p) => IsOneOfKeyword(p, "xx-small", "x-small", "small",
        "medium", "large", "x-large", "xx-large", "larger", "smaller");
    private static bool IsNamedColor(string p) => KnownColors.Get(p).HasValue;

    /// <summary>Whether a name is the <c>offset</c> shorthand itself. The value text is decided from
    /// the parts the shorthand owns, and both the grammar gate and the CSSOM echo ask this first, so
    /// neither treats a longhand of it as a shorthand of its own.</summary>
    public static bool IsOffsetShorthandName(string name) =>
        name.Equals("offset", StringComparison.OrdinalIgnoreCase);

    /// <summary>The edge keywords a lone &lt;position&gt; is made of. 'auto' and 'normal' are not in
    /// the set: each is a value of the shorthand on its own, and 'none' is the absence of a path.</summary>
    private static readonly string[] PositionEdgeKeywords =
        { "left", "right", "center", "top", "bottom" };

    /// <summary>CSS Motion Path 1 §3.7: the shorthand that lays a box on a path. It owns all five
    /// longhands, so a part the value leaves out is reset to its initial — and every part is judged
    /// by the longhand's own table, so the shorthand and the five can never disagree about one
    /// declaration (measured: 'offset: content-box path("M 0 0") 50% auto' is the box, the shape,
    /// the distance and the auto rotation, while 'offset: url(#p) 10% reverse center top' is no
    /// declaration at all because a position is read only where the shorthand starts).</summary>
    private static void ExpandOffset(Dictionary<string, string> result, string value)
    {
        var tokens = Acrux.Core.Css.Resolver.EchoedProperties.SplitTopLevel(value.Trim());
        if (tokens.Count == 0) return;
        int index = 0;

        // Where the path starts is the one part written in front of it, and it is the keywords that
        // name an edge, or 'auto'/'normal': a length in this position is the distance along the
        // path, and an angle is the rotation at the end of it.
        string? position = null;
        if (IsOneOfKeyword(tokens[index], "auto", "normal"))
        {
            position = tokens[index];
            index = 1;
        }
        else
        {
            int run = index;
            while (run < tokens.Count && IsOneOfKeyword(tokens[run], PositionEdgeKeywords)) run++;
            if (run > index)
            {
                position = string.Join(" ", tokens.GetRange(index, run - index));
                index = run;
            }
        }

        var shape = new List<string>();
        string? distance = null, rotate = null;
        for (; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (IsOneOfKeyword(token, "none")
                || Acrux.Core.Css.Resolver.EchoedProperties.IsOffsetBoxKeyword(token)
                || Acrux.Core.Css.Resolver.EchoedProperties.IsOffsetPathShape(token))
            {
                shape.Add(token);
                continue;
            }
            // The rotation is read before the distance: 'auto' and 'reverse' are no lengths at all,
            // and an angle is a rotation rather than the distance it looks like.
            if (rotate == null && (IsOneOfKeyword(token, "auto", "reverse") || IsOffsetAngle(token)))
            {
                rotate = token;
                continue;
            }
            if (distance == null && !IsOffsetAngle(token)
                && Acrux.Core.Css.Resolver.CssPropertyApplier.IsLengthOrPercentageToken(token))
            {
                distance = token;
                continue;
            }
            return;
        }

        // Each part is echoed by the longhand that owns it, so a second shape, a second distance or
        // a rotation spelled twice is refused here exactly as it is on the longhand.
        if (Acrux.Core.Css.Resolver.EchoedProperties.Normalize("offset-path",
                shape.Count == 0 ? "none" : string.Join(" ", shape)) is not { } pathText) return;
        if (Acrux.Core.Css.Resolver.EchoedProperties.Normalize("offset-distance",
                distance ?? "0px") is not { } distanceText) return;
        if (Acrux.Core.Css.Resolver.EchoedProperties.Normalize("offset-rotate",
                rotate ?? "auto") is not { } rotateText) return;
        if (Acrux.Core.Css.Resolver.EchoedProperties.Normalize("offset-position",
                position ?? "normal") is not { } positionText) return;

        result["offset-path"] = pathText;
        result["offset-distance"] = distanceText;
        result["offset-rotate"] = rotateText;
        // The anchor is not a part of the shorthand's grammar, but the shorthand does own it, and a
        // shorthand resets every part it names (measured: 'offset: none' leaves 'auto').
        result["offset-anchor"] = "auto";
        result["offset-position"] = positionText;
    }

    /// <summary>Whether a token is an angle: the rotation at the end of the shorthand is the one
    /// part there that a length parser would otherwise claim.</summary>
    private static bool IsOffsetAngle(string token)
    {
        foreach (var unit in new[] { "deg", "grad", "rad", "turn" })
            if (token.EndsWith(unit, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(token[..^unit.Length], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _)) return true;
        return false;
    }

    /// <summary>
    /// 'mask' (CSS Masking 1 §5.1) into its longhands. The layer list is decomposed by
    /// <see cref="MaskLayerParser"/>, the same code the mask painter uses, so the cascade and
    /// the rendering can never disagree about what a layer said. Parts the value does not name
    /// fall back to their initial values — that reset is what a shorthand means.
    /// </summary>
    private static void ExpandMask(Dictionary<string, string> result, string value)
    {
        var layers = MaskLayerParser.Parse(value);
        if (layers.Count == 0) return;
        var first = layers[0];

        // The full authored value stays available too: each layer keeps its own geometry in
        // it, and the longhands below are single scalars until the painter reads layer lists.
        result["mask"] = value;
        result["mask-image"] = string.Join(", ", layers.Select(l => l.Image));
        result["mask-position"] = first.Position ?? "0% 0%";
        result["mask-size"] = first.Size ?? "auto";
        result["mask-repeat"] = first.Repeat ?? "repeat";
        result["mask-origin"] = first.Origin ?? "border-box";
        result["mask-clip"] = first.Clip ?? "border-box";
        result["mask-composite"] = first.Composite ?? "add";
        result["mask-mode"] = first.Mode ?? "match-source";
    }


}
