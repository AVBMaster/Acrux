using System;
using System.Collections.Generic;
using System.Linq;
using UpBrowser.Core.Css.ElementStyles;
using UpBrowser.Core.Css.Resolver;
using UpBrowser.Core.Css.Properties;

namespace UpBrowser.Core.Css;

public static class ShorthandExpander
{
    public static Dictionary<string, string> Expand(Dictionary<string, string> properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in properties)
        {
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
            case "border-width": ExpandFourSides(result, "border", value, "width"); break;
            case "border-color": ExpandFourSides(result, "border", value, "color"); break;
            case "border-style": ExpandFourSides(result, "border", value, "style"); break;
            case "border-radius": ExpandBorderRadius(result, value); break;
            case "border-top": ExpandBorderSide(result, "border-top", value); break;
            case "border-right": ExpandBorderSide(result, "border-right", value); break;
            case "border-bottom": ExpandBorderSide(result, "border-bottom", value); break;
            case "border-left": ExpandBorderSide(result, "border-left", value); break;
            case "background": ExpandBackground(result, value); break;
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
            case "text-emphasis": ExpandTextEmphasis(result, value); break;
            case "gap": ExpandGap(result, value); break;
        case "columns": ExpandColumns(result, value); break;
        case "column-rule": ExpandColumnRule(result, value); break;
            case "inset": ExpandFourSides(result, "", value); break;
            case "overflow": ExpandOverflow(result, value); break;
            case "mask": result["mask-image"] = value; break;
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
            "background" => new[] { "background-color", "background-image", "background-repeat", "background-attachment", "background-position", "background-size", "background-clip", "background-origin" },
            "margin" => new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" },
            "padding" => new[] { "padding-top", "padding-right", "padding-bottom", "padding-left" },
            "border" => new[] { "border-top-width", "border-top-style", "border-top-color", "border-right-width", "border-right-style", "border-right-color", "border-bottom-width", "border-bottom-style", "border-bottom-color", "border-left-width", "border-left-style", "border-left-color" },
            "font" => new[] { "font-style", "font-variant", "font-variant-caps", "font-weight", "font-size", "line-height", "font-family" },
            "font-variant" => new[] { "font-variant-caps" },
            "flex" => new[] { "flex-grow", "flex-shrink", "flex-basis" },
            "outline" => new[] { "outline-color", "outline-style", "outline-width" },
            "animation" => new[] { "animation-name", "animation-duration", "animation-timing-function", "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state" },
            "transition" => new[] { "transition-property", "transition-duration", "transition-timing-function", "transition-delay" },
            _ => System.Array.Empty<string>(),
        };
        return keys.Select(CssPropertyIdExtensions.FromString).Where(id => id != CssPropertyId.Invalid);
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
    }

    private static void ExpandBackground(Dictionary<string, string> result, string value)
    {
        // The background shorthand may carry multiple comma-separated layers, each
        // of which can itself contain spaces inside gradient functions. Split into
        // layers on top-level commas first, then tokenize each layer by space.
        var layers = SplitTopLevel(value, ',');
        var images = new List<string>();
        var repeats = new List<string>();

        foreach (var layer in layers)
        {
            var parts = SplitShorthand(layer);
            foreach (var raw in parts)
            {
                var p = raw.Trim().TrimEnd(',').Trim();
                if (string.IsNullOrEmpty(p)) continue;

                if (p.StartsWith("url(") || p.StartsWith("linear-gradient") || p.StartsWith("radial-gradient") || p.StartsWith("conic-gradient") || p.StartsWith("repeating-linear-gradient") || p.StartsWith("repeating-radial-gradient") || p.StartsWith("repeating-conic-gradient"))
                {
                    // An empty url() has no address and is invalid at computed-value
                    // time, so the layer contributes no image.
                    if (CssPropertyApplier.IsUrlWithoutAddress(p))
                        continue;
                    images.Add(p);
                }
                else if (ColorParser.LooksLikeColor(p))
                    result["background-color"] = p;
                else if (p == "repeat" || p == "no-repeat" || p == "repeat-x" || p == "repeat-y" || p == "round" || p == "space")
                    repeats.Add(p);
                else if (p == "scroll" || p == "fixed" || p == "local")
                    result["background-attachment"] = p;
                else if (p == "cover" || p == "contain")
                    result["background-size"] = p;
                else if (p.Contains('/') && !p.Contains("("))
                {
                    var posSize = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (posSize.Length >= 1) result["background-position"] = posSize[0].Trim();
                    if (posSize.Length >= 2) result["background-size"] = posSize[1].Trim();
                }
                else if (p.Contains('%') || p == "center" || p == "left" || p == "right" || p == "top" || p == "bottom")
                    result["background-position"] = p;
            }
        }

        if (images.Count > 0)
            result["background-image"] = string.Join(", ", images);

        if (repeats.Count > 0)
            result["background-repeat"] = string.Join(" ", repeats);

        result.TryAdd("background-repeat", "repeat");
        result.TryAdd("background-attachment", "scroll");
        result.TryAdd("background-position", "0% 0%");
        result.TryAdd("background-size", "auto");
    }

    private static void ExpandFont(Dictionary<string, string> result, string value)
    {
        // CSS Fonts 4 §5.3: [ style || variant || weight || stretch ]? size [ / line-height ]? family
        // Once the size is seen, every remaining token belongs to the font-family
        // list — unquoted family names must not be dropped or re-interpreted.
        string fontSize = "16px", lineHeight = "normal";
        bool foundSize = false;
        int normalSlot = 0;
        var family = new List<string>();

        foreach (var part in SplitFontShorthand(value))
        {
            var p = part.Trim().ToLowerInvariant();
            if (foundSize)
            {
                family.Add(p);
                continue;
            }

            if (p == "normal")
            {
                // Successive 'normal' fill the style, variant, weight, stretch slots in order.
                switch (normalSlot++)
                {
                    case 0: result["font-style"] = "normal"; break;
                    case 1: result["font-variant"] = "normal"; break;
                    case 2: result["font-weight"] = "normal"; break;
                }
            }
            else if (p == "italic" || p == "oblique")
                result["font-style"] = p;
            else if (p == "small-caps")
                result["font-variant"] = p;
            else if (p == "bold" || p == "bolder" || p == "lighter" || int.TryParse(p, out _))
                result["font-weight"] = p;
            else if (IsFontStretch(p))
            {
                // font-stretch is parsed but not applied by the engine yet.
            }
            else if (p.Contains('/'))
            {
                var sizeLine = p.Split('/');
                fontSize = sizeLine[0].Trim();
                lineHeight = sizeLine[1].Trim();
                foundSize = true;
            }
            else if (IsFontSize(p) || IsFontSizeLength(p))
            {
                fontSize = p;
                foundSize = true;
            }
            else
                family.Add(p);
        }

        result["font-size"] = fontSize;
        result["line-height"] = lineHeight;
        if (family.Count > 0)
            result["font-family"] = string.Join(" ", family);
        result.TryAdd("font-style", "normal");
        result.TryAdd("font-weight", "normal");
        result.TryAdd("font-variant", "normal");
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

    private static bool IsFontStretch(string p) => p is "ultra-condensed" or "extra-condensed" or "semi-condensed"
        or "condensed" or "semi-expanded" or "expanded" or "extra-expanded" or "ultra-expanded" or "wider" or "narrower";

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
        // Spec: grid-area: <row-start> / <column-start> / <row-end> / <column-end>.
        // A single token names all four lines (the common named-area form).
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (parts.Length)
        {
            case 1:
                result["grid-row-start"] = parts[0];
                result["grid-column-start"] = parts[0];
                result["grid-row-end"] = parts[0];
                result["grid-column-end"] = parts[0];
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
        var entries = SplitTopLevel(value, ',');
        if (entries.Count == 0) entries.Add("");

        var names = new List<string>(entries.Count);
        var durations = new List<string>(entries.Count);
        var easings = new List<string>(entries.Count);
        var delays = new List<string>(entries.Count);
        var iterations = new List<string>(entries.Count);
        var directions = new List<string>(entries.Count);
        var fills = new List<string>(entries.Count);
        var plays = new List<string>(entries.Count);

        foreach (var entry in entries)
        {
            string name = "none", duration = "0s", easing = "ease", delay = "0s";
            string iteration = "1", direction = "normal", fill = "none", play = "running";
            bool haveTime = false;
            bool nameSeen = false;

            foreach (var token in SplitShorthand(entry))
            {
                var p = token.Trim().ToLowerInvariant();
                if (p.Length == 0) continue;

                if (IsTimeToken(p))
                {
                    if (!haveTime) { duration = p; haveTime = true; }
                    else delay = p;
                }
                else if (IsEasingToken(p)) easing = p;
                else if (p == "infinite" || IsNumberToken(p)) iteration = p;
                else if (p is "normal" or "reverse" or "alternate" or "alternate-reverse") direction = p;
                else if (p is "running" or "paused") play = p;
                // 'none' is both a fill-mode and the initial animation-name, so it
                // is resolved by position: it is the name until a name is present,
                // and the fill-mode afterwards. `animation: f 1s none` therefore
                // means name=f fill=none, while `animation: none` means no
                // animation at all (CSS Animations 1 §2.11).
                else if (p == "none" && !nameSeen) { name = "none"; nameSeen = true; }
                else if (p is "none" or "forwards" or "backwards" or "both")
                {
                    fill = p;
                    nameSeen = true;
                }
                else { name = p; nameSeen = true; }
            }

            names.Add(name);
            durations.Add(duration);
            easings.Add(easing);
            delays.Add(delay);
            iterations.Add(iteration);
            directions.Add(direction);
            fills.Add(fill);
            plays.Add(play);
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
            bool haveTime = false;

            foreach (var token in SplitShorthand(entry))
            {
                var p = token.Trim().ToLowerInvariant();
                if (p.Length == 0) continue;

                if (IsTimeToken(p))
                {
                    if (!haveTime) { duration = p; haveTime = true; }
                    else delay = p;
                }
                else if (IsEasingToken(p)) easing = p;
                else property = p;
            }

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

    /// <summary>True for a &lt;time&gt; token: an optional sign, digits, and a s/ms unit.</summary>
    private static bool IsTimeToken(string token)
    {
        var t = token.Trim().ToLowerInvariant();
        if (t.Length < 2) return false;
        if (t.EndsWith("ms", StringComparison.Ordinal))
            return IsNumberToken(t[..^2]);
        if (t.EndsWith("s", StringComparison.Ordinal))
            return IsNumberToken(t[..^1]);
        return false;
    }

    private static bool IsNumberToken(string token)
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
    private static bool IsEasingToken(string token)
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

    private static void ExpandOutline(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p == "none" || p == "hidden")
                result["outline-style"] = p;
            else if (IsBorderStyle(p))
                result["outline-style"] = p;
            else if (p == "thin" || p == "medium" || p == "thick" || p.EndsWith("px") || p.EndsWith("em"))
                result["outline-width"] = p;
            else if (IsColor(p))
                result["outline-color"] = p;
        }
        result.TryAdd("outline-style", "none");
    }

    private static void ExpandTextDecoration(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        // The shorthand resets every longhand it does not carry.
        result["text-decoration-line"] = "none";
        result["text-decoration-style"] = "solid";
        result["text-decoration-color"] = "currentcolor";
        result["text-decoration-thickness"] = "auto";

        var lines = new List<string>();
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p is "underline" or "overline" or "line-through")
            {
                // 'text-decoration-line' is a list, so several keywords may appear.
                if (!lines.Contains(p)) lines.Add(p);
            }
            else if (p is "solid" or "double" or "dotted" or "dashed" or "wavy")
                result["text-decoration-style"] = p;
            else if (p is "auto" or "from-font" || IsThickness(p))
                result["text-decoration-thickness"] = p;
            else if (IsColor(p))
                result["text-decoration-color"] = p;
        }
        if (lines.Count > 0)
            result["text-decoration-line"] = string.Join(' ', lines);
    }

    /// <summary>A bare length or percentage in the 'text-decoration' shorthand is the
    /// thickness (CSS Text Decoration 4 adds it to the shorthand grammar).</summary>
    private static bool IsThickness(string token) => Dom.Length.IsLength(token);

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
        foreach (var part in SplitShorthand(value))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            if (int.TryParse(p, out _))
                result["column-count"] = p;
            else
                result["column-width"] = p;
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
            else if (value[i] == ' ' && depth == 0)
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

    private static bool IsBorderStyle(string p) => p is "none" or "hidden" or "dotted" or "dashed" or "solid" or "double" or "groove" or "ridge" or "inset" or "outset";
    private static bool IsBorderWidth(string p) => p == "thin" || p == "medium" || p == "thick" || p.EndsWith("px") || p.EndsWith("em");
    private static bool IsColor(string p) => p.StartsWith("#") || p.StartsWith("rgb") || p == "transparent" || p == "currentcolor" || IsNamedColor(p);
    private static bool IsFontSize(string p) => p is "xx-small" or "x-small" or "small" or "medium" or "large" or "x-large" or "xx-large" or "larger" or "smaller";
    private static bool IsNamedColor(string p) => KnownColors.Get(p).HasValue;
}
