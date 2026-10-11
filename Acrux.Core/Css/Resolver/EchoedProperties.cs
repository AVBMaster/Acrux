using System.Globalization;

namespace Acrux.Core.Css.Resolver;

/// <summary>
/// The properties this engine reads, validates, inherits and echoes, and does not yet consume:
/// scroll snapping, container queries, the SVG keyword family that CSS borrows, and the remaining
/// <c>font-variant-*</c> flags. Each entry carries what the reference engine prints for a value and
/// what it answers when the page says nothing, so the four surfaces that must agree — the value
/// grammar (does a declaration exist at all), the canonical text (what both a sheet and the CSSOM
/// echo), the cascade (what a child inherits) and <c>getComputedStyle</c> (what a script reads) —
/// are driven from one table instead of four hand-maintained lists.
///
/// A property leaves this table the day it gains a field with a consumer. The map is the honest
/// answer for "the value is known, correct, and nothing acts on it yet"; it is not somewhere to
/// park a half-implemented feature.
///
/// Every rule below is measured against the reference engine, value by value
/// (snapshots/_b262_truth.txt); where the specification and the engine disagree — <c>auto</c> for
/// <c>print-color-adjust</c>, <c>allows-keywords</c> for <c>interpolate-size</c>,
/// <c>digits</c> for <c>text-combine-upright</c> — the engine wins, because that is what a page
/// sees.
/// </summary>
public static class EchoedProperties
{
    /// <summary>One property: what it says when nothing is written, whether it inherits, and how a
    /// written value is read. <see cref="Normalize"/> returns the canonical text, or null when the
    /// text is no value at all and the declaration must not exist.</summary>
    public sealed class Entry
    {
        public required string Name { get; init; }
        public required string Initial { get; init; }
        public required Func<string, string?> Normalize { get; init; }
        public bool Inherited { get; init; }
        /// <summary>Set when the computed value is not the text the specified value keeps — the
        /// <c>math-depth</c> and <c>offset-rotate</c> shape, where the engine expands a keyword, and
        /// the <c>offset-distance</c> shape, where it resolves a length against the element's own
        /// font. The style is handed to the hook for that alone; an entry that only re-spells a
        /// keyword ignores it.</summary>
        public Func<string, Acrux.Core.Dom.ComputedStyle, string>? ComputedText { get; init; }
    }

    /// <summary>The entry for a property name, or null when the property is not echo-only.</summary>
    public static Entry? Find(string name)
        => ByName.TryGetValue(name.Trim().ToLowerInvariant(), out var entry) ? entry : null;

    /// <summary>The canonical text of a value, or null when the value is refused.</summary>
    public static string? Normalize(string name, string value) => Find(name)?.Normalize(value);

    /// <summary>The entries that run down the tree. <see cref="CssInheritance"/> copies these and
    /// nothing else from this table, so an entry's <c>Inherited</c> flag is read in exactly one
    /// place rather than being decoration.</summary>
    public static IEnumerable<Entry> InheritedEntries { get; private set; } = Array.Empty<Entry>();

    private static readonly Dictionary<string, Entry> ByName = Build();

    private static Dictionary<string, Entry> Build()
    {
        var entries = new Entry[]
        {
            // ===== Scroll Snap 1 §5. The axis and the strictness are one value, and the strictness
            // is printed only when it says 'mandatory' — 'proximity' is what the pair means without
            // it (measured: 'both proximity' reads 'both' while 'x mandatory' reads as written).
            new() { Name = "scroll-snap-type", Initial = "none", Normalize = NormalizeScrollSnapType },
            new() { Name = "scroll-snap-align", Initial = "none", Normalize = NormalizeScrollSnapAlign },
            new() { Name = "scroll-snap-stop", Initial = "normal", Normalize = Single("normal", "always") },

            // ===== Writing Modes 4 §3.4 and Ruby 1 §3.3. 'sideways-left' is not a value here, and
            // 'vertical-rl' belongs to 'writing-mode' alone (both measured).
            new() { Name = "text-orientation", Initial = "mixed",
                Normalize = Single("mixed", "upright", "sideways-right", "sideways") },
            new() { Name = "ruby-align", Initial = "space-around",
                Normalize = Single("start", "center", "space-between", "space-around") },
            new() { Name = "text-combine-upright", Initial = "none",
                Normalize = Single("none", "all") },

            // ===== Fonts 4 §9. A tag is a quoted string, and the engine prints it with double
            // quotes whichever way the page wrote it (measured: "'SIN'" reads '"SIN"').
            new() { Name = "font-language-override", Initial = "normal", Inherited = true,
                Normalize = NormalizeFontLanguageOverride },
            new() { Name = "font-variant-position", Initial = "normal", Inherited = true,
                Normalize = Single("normal", "sub", "super") },
            new() { Name = "font-variant-east-asian", Initial = "normal", Inherited = true,
                Normalize = NormalizeEastAsian },
            new() { Name = "font-variant-numeric", Initial = "normal", Inherited = true,
                Normalize = NormalizeFontVariantNumeric },
            new() { Name = "font-variant-ligatures", Initial = "normal", Inherited = true,
                Normalize = NormalizeLigatures },

            // ===== Text 4 and Color Adjust 1. 'text-decoration-skip-ink' is NOT in this table even
            // though it looks like the rest: the line painter reads it, so it keeps its own field and
            // its own computed case.
            new() { Name = "print-color-adjust", Initial = "economy", Inherited = true,
                Normalize = Single("economy", "exact") },
            new() { Name = "-webkit-print-color-adjust", Initial = "economy", Inherited = true,
                Normalize = Single("economy", "exact") },
            new() { Name = "hyphenate-limit-chars", Initial = "auto", Inherited = true,
                Normalize = NormalizeHyphenateLimitChars },

            // ===== Masks 1 §3.3 and MathML 4 §3.3.2.
            new() { Name = "mask-type", Initial = "luminance",
                Normalize = Single("luminance", "alpha") },
            new() { Name = "math-depth", Initial = "0", Normalize = NormalizeMathDepth,
                ComputedText = MathDepthComputed },

            // ===== Motion Path 1 §3. Nothing here lays a box on a path yet, but every one of these
            // values is read, inherited and echoed correctly (snapshots/_b265_*): the path is echoed
            // the way the engine reads it — command letter, then its numbers, each separated
            // (measured: 'path("M0 0")' reads 'path("M 0 0")') — a geometry box stands beside the
            // shape but never beside another box, and 'reverse' is a rotation the engine spells out
            // in the computed value while the specified one keeps the word the page wrote.
            new() { Name = "offset-path", Initial = "none", Normalize = NormalizeOffsetPath,
                ComputedText = OffsetPathComputed },
            new() { Name = "offset-distance", Initial = "0px", Normalize = NormalizeOffsetDistance,
                ComputedText = LengthPercentageComputed },
            new() { Name = "offset-rotate", Initial = "auto", Normalize = NormalizeOffsetRotate,
                ComputedText = OffsetRotateComputed },
            new() { Name = "offset-anchor", Initial = "auto", Normalize = NormalizeOffsetAnchor,
                ComputedText = PositionComputed },
            new() { Name = "offset-position", Initial = "normal", Normalize = NormalizeOffsetPosition,
                ComputedText = PositionComputed },

            // ===== SVG 2 properties CSS inherits: each is one keyword of a closed set.
            new() { Name = "text-anchor", Initial = "start", Inherited = true,
                Normalize = Single("start", "middle", "end") },
            new() { Name = "dominant-baseline", Initial = "auto", Inherited = true,
                Normalize = Single("auto", "alphabetic", "hanging", "ideographic", "middle",
                    "mathematical", "central") },
            new() { Name = "alignment-baseline", Initial = "auto", Inherited = true,
                Normalize = Single("auto", "baseline", "before-edge", "text-before-edge", "middle",
                    "central", "after-edge", "text-after-edge", "ideographic", "alphabetic",
                    "hanging", "mathematical") },
            new() { Name = "stroke-linecap", Initial = "butt", Inherited = true,
                Normalize = Single("butt", "round", "square") },
            new() { Name = "stroke-linejoin", Initial = "miter", Inherited = true,
                Normalize = Single("miter", "round", "bevel") },
            new() { Name = "fill-rule", Initial = "nonzero", Inherited = true,
                Normalize = Single("nonzero", "evenodd") },
            new() { Name = "clip-rule", Initial = "nonzero", Inherited = true,
                Normalize = Single("nonzero", "evenodd") },

            // ===== CSS Fill and Stroke 3 / SVG 2 §11. The paint family is inherited and, in this
            // engine, read by nothing but the CSSOM — every one of them computed the empty string
            // until here. A <paint> is a colour, 'none' or 'currentColor', and the computed value
            // resolves the keyword to the element's own colour (measured: 'fill: currentColor' reads
            // 'rgb(0, 0, 0)'); a bare number of 'stroke-width'/'stroke-dashoffset' is the pixel
            // length it stands for once computed but keeps its unitless spelling as specified, while
            // a percentage is never priced; and a dash list is printed comma-separated, each part
            // with its unit (measured: '5 3' specifies as '5, 3' and computes '5px, 3px').
            new() { Name = "fill", Initial = "rgb(0, 0, 0)", Inherited = true,
                Normalize = NormalizePaint, ComputedText = PaintComputed },
            new() { Name = "stroke", Initial = "none", Inherited = true,
                Normalize = NormalizePaint, ComputedText = PaintComputed },
            new() { Name = "stop-color", Initial = "rgb(0, 0, 0)", Inherited = true,
                Normalize = NormalizePaint, ComputedText = PaintComputed },
            new() { Name = "fill-opacity", Initial = "1", Inherited = true,
                Normalize = NormalizeOpacityValue },
            new() { Name = "stroke-opacity", Initial = "1", Inherited = true,
                Normalize = NormalizeOpacityValue },
            new() { Name = "stop-opacity", Initial = "1", Inherited = true,
                Normalize = NormalizeOpacityValue },
            new() { Name = "stroke-width", Initial = "1px", Inherited = true,
                Normalize = NormalizeSvgLength, ComputedText = SvgLengthComputed },
            new() { Name = "stroke-dashoffset", Initial = "0px", Inherited = true,
                Normalize = NormalizeSvgLength, ComputedText = SvgLengthComputed },
            new() { Name = "stroke-miterlimit", Initial = "4", Inherited = true,
                Normalize = NormalizePositiveNumber },
            new() { Name = "stroke-dasharray", Initial = "none", Inherited = true,
                Normalize = NormalizeDashArray, ComputedText = DashArrayComputed },

            // ===== SVG 2 §11.3. The three marker properties name one image or nothing at all. The
            // specified value keeps the address the page wrote in the url() spelling the CSSOM
            // prints; the computed value makes it absolute, except for a bare fragment, which is
            // already a reference into the document (all measured).
            new() { Name = "marker-start", Initial = "none", Inherited = true,
                Normalize = NormalizeMarker, ComputedText = MarkerComputed },
            new() { Name = "marker-mid", Initial = "none", Inherited = true,
                Normalize = NormalizeMarker, ComputedText = MarkerComputed },
            new() { Name = "marker-end", Initial = "none", Inherited = true,
                Normalize = NormalizeMarker, ComputedText = MarkerComputed },

            // ===== Scroll-driven Animations 1, View Transitions 1 and Anchor Position 1. A name is
            // a dashed ident here, so a bare 't' is refused while '--t' is kept; 'view-transition-name'
            // reserves 'auto' (measured: it is refused).
            new() { Name = "animation-timeline", Initial = "auto", Normalize = NormalizeTimeline },
            new() { Name = "view-transition-name", Initial = "none", Normalize = NormalizeViewTransitionName },
            new() { Name = "position-anchor", Initial = "auto", Normalize = NormalizePositionAnchor },

            // ===== Containment 3 §3.4 and the interpolation control of Values 4 §1.2. A container
            // name list is space-separated — a comma is no value (measured).
            new() { Name = "container-type", Initial = "normal",
                Normalize = Single("normal", "size", "inline-size") },
            new() { Name = "container-name", Initial = "none", Normalize = NormalizeContainerName },
            new() { Name = "interpolate-size", Initial = "numeric-only", Inherited = true,
                Normalize = Single("numeric-only") },
        };

        var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var e in entries) map[e.Name] = e;
        InheritedEntries = entries.Where(e => e.Inherited).ToArray();
        return map;
    }

    // ===== the readers =====

    /// <summary>One keyword of a closed set, in the engine's own lower case.</summary>
    private static Func<string, string?> Single(params string[] allowed) => text =>
    {
        var words = Words(text);
        if (words.Length != 1) return null;
        foreach (var a in allowed)
            if (words[0].Equals(a, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    };

    private static string[] Words(string text) => text.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NormalizeScrollSnapType(string text)
    {
        var words = Words(text);
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        if (words.Length is not (1 or 2)) return null;
        var axis = words[0].ToLowerInvariant();
        if (axis is not ("x" or "y" or "block" or "inline" or "both")) return null;
        if (words.Length == 1) return axis;
        var strictness = words[1].ToLowerInvariant();
        // 'proximity' is what the axis means on its own, so it is said and then dropped; a value
        // that names both strictnesses is no value (measured).
        if (strictness == "proximity") return axis;
        return strictness == "mandatory" ? axis + " mandatory" : null;
    }

    private static string? NormalizeScrollSnapAlign(string text)
    {
        var words = Words(text);
        if (words.Length == 0 || words.Length > 2) return null;
        foreach (var w in words)
            if (w.ToLowerInvariant() is not ("start" or "end" or "center" or "none")) return null;
        var first = words[0].ToLowerInvariant();
        if (words.Length == 1) return first;
        var second = words[1].ToLowerInvariant();
        // 'none' is a value of its own and does not pair; two equal axes print once (measured:
        // 'start start' reads 'start', while 'end center' keeps both).
        if (first == "none" || second == "none") return null;
        return first == second ? first : first + " " + second;
    }

    private static string? NormalizeFontLanguageOverride(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
        return IsQuotedString(trimmed) ? "\"" + Unquote(trimmed) + "\"" : null;
    }

    /// <summary>CSS Fonts 4 §9.3: one value from each of three groups, and <c>normal</c> says it
    /// alone. The engine prints the groups in its own order — the width variant, then the spacing,
    /// then <c>ruby</c> — whichever order the page wrote them in (measured: 'full-width jis04'
    /// reads 'jis04 full-width' and 'ruby full-width' reads 'full-width ruby').</summary>
    private static string? NormalizeEastAsian(string text)
    {
        var words = Words(text);
        if (words.Length == 0) return null;
        string? variant = null, width = null;
        bool ruby = false;
        foreach (var raw in words)
        {
            var w = raw.ToLowerInvariant();
            if (w == "normal") return words.Length == 1 ? "normal" : null;
            if (w is "jis04" or "jis78" or "jis83" or "simplified" or "traditional")
            {
                if (variant != null) return null;
                variant = w;
            }
            else if (w is "full-width" or "proportional-width")
            {
                if (width != null) return null;
                width = w;
            }
            else if (w == "ruby")
            {
                if (ruby) return null;
                ruby = true;
            }
            else return null;
        }
        var parts = new List<string>();
        if (variant != null) parts.Add(variant);
        if (width != null) parts.Add(width);
        if (ruby) parts.Add("ruby");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>CSS Fonts 4 §9.2: the numeric keywords come in three either/or groups, and the
    /// engine prints what the page wrote in the order it wrote it (measured: 'tabular-nums ordinal
    /// slashed-zero' reads back unchanged, while 'diagonal-fractions stacked-fractions' is refused
    /// and 'normal ordinal' never combines with the rest).</summary>
    private static string? NormalizeFontVariantNumeric(string text)
    {
        var words = Words(text);
        if (words.Length == 0) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outWords = new List<string>();
        foreach (var raw in words)
        {
            var w = raw.ToLowerInvariant();
            if (w == "normal") return words.Length == 1 ? "normal" : null;
            if (w is not ("ordinal" or "slashed-zero" or "oldstyle-nums" or "lining-nums"
                or "proportional-nums" or "tabular-nums" or "diagonal-fractions"
                or "stacked-fractions"))
            {
                // A numeric factor ('1/2', '3/4') is the one other thing the grammar takes; it is
                // echoed as written.
                if (!IsFactor(w)) return null;
                w = "factor";
            }
            if (!seen.Add(Group(w))) return null;
            outWords.Add(raw.ToLowerInvariant());
        }
        return string.Join(" ", outWords);
    }

    private static string Group(string w) => w switch
    {
        "oldstyle-nums" or "lining-nums" => "figuring",
        "proportional-nums" or "tabular-nums" => "spacing",
        "diagonal-fractions" or "stacked-fractions" => "fractions",
        "factor" => "factor",
        _ => w,
    };

    private static bool IsFactor(string w)
    {
        int slash = w.IndexOf('/');
        if (slash <= 0 || slash == w.Length - 1) return false;
        return int.TryParse(w[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && int.TryParse(w[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>CSS Fonts 4 §9.1: the ligature keywords are three either/or groups plus
    /// <c>contextual</c>, and <c>normal</c> stands alone.</summary>
    private static string? NormalizeLigatures(string text)
    {
        var words = Words(text);
        if (words.Length == 0) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outWords = new List<string>();
        foreach (var raw in words)
        {
            var w = raw.ToLowerInvariant();
            if (w == "normal") return words.Length == 1 ? "normal" : null;
            if (w is not ("common-ligatures" or "no-common-ligatures" or "discretionary-ligatures"
                or "no-discretionary-ligatures" or "historical-ligatures"
                or "no-historical-ligatures" or "contextual" or "no-contextual")) return null;
            string group = w.StartsWith("no-", StringComparison.Ordinal) ? w[3..] : w;
            if (!seen.Add(group)) return null;
            outWords.Add(w);
        }
        return string.Join(" ", outWords);
    }

    /// <summary>CSS Text 4 §5.6.3: <c>auto</c>, or one to three integers where the first is the
    /// whole-word minimum. Zero is not a minimum, so '0' is no value (measured).</summary>
    private static string? NormalizeHyphenateLimitChars(string text)
    {
        var words = Words(text);
        if (words.Length == 1 && words[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
            return "auto";
        if (words.Length is < 1 or > 3) return null;
        var parts = new List<string>();
        foreach (var w in words)
        {
            if (!int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1)
                return null;
            parts.Add(n.ToString(CultureInfo.InvariantCulture));
        }
        return string.Join(" ", parts);
    }

    /// <summary>MathML 4 §3.3.2: the specified value is <c>auto-add</c>, <c>add(</c>integer<c>)</c>
    /// or a bare integer, and the computed value is the integer it adds — <c>auto-add</c> adds
    /// nothing, so it reads <c>0</c> (all three measured).</summary>
    private static string? NormalizeMathDepth(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("auto-add", StringComparison.OrdinalIgnoreCase)) return "auto-add";
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bare))
            return bare.ToString(CultureInfo.InvariantCulture);
        if (!trimmed.StartsWith("add(", StringComparison.OrdinalIgnoreCase)) return null;
        int close = trimmed.IndexOf(')');
        if (close < 0) return null;
        var inner = trimmed[4..close].Trim();
        return int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? "add(" + n.ToString(CultureInfo.InvariantCulture) + ")" : null;
    }

    /// <summary>The computed text of <c>math-depth</c>: the integer the specified value adds.</summary>
    public static string MathDepthComputed(string canonical, Acrux.Core.Dom.ComputedStyle _)
    {
        if (canonical.Equals("auto-add", StringComparison.OrdinalIgnoreCase)) return "0";
        int open = canonical.IndexOf('('), close = canonical.IndexOf(')');
        return open >= 0 && close > open ? canonical[(open + 1)..close].Trim() : canonical;
    }

    /// <summary>The geometry boxes <c>offset-path</c> reads (CSS Motion Path 1 §3.2). The
    /// reference engine's set is the one a shape can be measured against: <c>margin-box</c> is not
    /// in it (measured: refused), while <c>view-box</c> is.</summary>
    private static readonly string[] OffsetBoxes =
    {
        "content-box", "border-box", "padding-box", "stroke-box", "fill-box", "view-box"
    };

    /// <summary>CSS Motion Path 1 §3.2: <c>none</c>, one of the geometry boxes, a box beside a
    /// shape, or a shape — which the engine prints FIRST, whichever order the page wrote them in
    /// (measured: <c>content-box path("M 0 0")</c> reads <c>path("M 0 0") content-box</c>, and two
    /// boxes are no value at all). The path data itself is echoed command letter, then its numbers,
    /// space separated and with their case kept, because an absolute <c>L</c> and a relative <c>l</c>
    /// are different commands (measured: <c>path("M0 0")</c> reads <c>path("M 0 0")</c> and
    /// <c>path("M 1 2 L 3 4")</c> keeps its <c>L</c>).</summary>
    private static string? NormalizeOffsetPath(string text)
    {
        var tokens = SplitTopLevel(text);
        if (tokens.Count == 0) return null;
        if (tokens.Count == 1 && tokens[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        string? box = null, shape = null;
        foreach (var token in tokens)
        {
            if (box == null && IsOneOf(token, OffsetBoxes)) box = token.ToLowerInvariant();
            else if (shape == null && IsOffsetShape(token, out var canonical)) shape = canonical;
            else return null;
        }
        if (shape == null) return box;
        return box == null ? shape : shape + " " + box;
    }

    /// <summary>Whether one token is a shape <c>offset-path</c> takes, and the text it is
    /// echoed as. <c>path()</c> has its own reprint; <c>url()</c> the quoted spelling the CSSOM
    /// prints; <c>ray()</c> is the page's own data; and the basic shapes have the grammar of
    /// CSS Shapes 1 §4 as the reference engine reads it: a radius that is the shape's own default
    /// is not said, an ellipse takes two radii or none, the offsets of an inset take one to four
    /// lengths and collapse the way a four-sided list does, the box of an xywh takes exactly four
    /// lengths and nothing beside them, and an <c>at</c> clause is a position, which the engine
    /// prints as its two axes (all measured, value by value — see snapshots/_probe_b265_h.html).</summary>
    private static bool IsOffsetShape(string token, out string? canonical)
    {
        canonical = null;
        int open = token.IndexOf('(');
        if (open <= 0 || !token.EndsWith(")", StringComparison.Ordinal)) return false;
        var name = token[..open].ToLowerInvariant();
        var inner = token[(open + 1)..^1].Trim();
        switch (name)
        {
            case "path":
            {
                if (!IsQuotedString(inner)) return false;
                var data = CanonicalPathData(Unquote(inner));
                if (data == null) return false;
                canonical = "path(\"" + data + "\")";
                return true;
            }
            case "ray":
            {
                // The shape position of a ray is not a value this engine reads (measured:
                // 'ray(center 45deg)' is refused while 'ray(45deg contain)' is kept whole).
                var words = inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0
                    || words[0].ToLowerInvariant() is "left" or "center" or "right"
                        or "top" or "bottom") return false;
                canonical = token.ToLowerInvariant();
                return true;
            }
            case "url":
            {
                var address = IsQuotedString(inner) ? Unquote(inner) : inner;
                if (address.Length == 0) return false;
                canonical = "url(\"" + address + "\")";
                return true;
            }
            case "circle":
            case "ellipse":
            {
                if (!TrySplitShapeClause(inner, "at", out var sizes, out var position)) return false;
                // An ellipse is two radii or no radii at all; a circle has one (measured:
                // 'ellipse(10px)' is refused while 'ellipse()' is kept).
                if (sizes.Count > (name == "circle" ? 1 : 2)) return false;
                if (name == "ellipse" && sizes.Count == 1) return false;
                var printed = new List<string>();
                foreach (var size in sizes)
                {
                    if (size.ToLowerInvariant() is "closest-side" or "farthest-side")
                        printed.Add(size.ToLowerInvariant());
                    else if (ShapeLength(size) is { } length) printed.Add(length);
                    else return false;
                }
                // A radius the shape would assume anyway is not said twice: the default of both
                // circle() and ellipse() is 'closest-side', and the engine prints the shape with
                // that radius as the shape without one (measured: 'circle(closest-side at left)'
                // reads 'circle(at left center)', but 'ellipse(closest-side 10px)' keeps both).
                if (printed.Count > 0 && printed.TrueForAll(r => r == "closest-side"))
                    printed.Clear();
                var head = string.Join(" ", printed);
                string? positionText = null;
                if (position != null)
                {
                    positionText = ShorthandExpander.PositionPairText(string.Join(" ", position));
                    if (positionText == null) return false;
                }
                canonical = name + "(" + head
                    + (positionText == null ? "" : (head.Length > 0 ? " at " : "at ") + positionText)
                    + ")";
                return true;
            }
            case "inset":
            {
                if (!TrySplitShapeClause(inner, "round", out var offsets, out var radii))
                    return false;
                if (offsets.Count == 0 || offsets.Count > 4) return false;
                var sides = new List<string>();
                foreach (var offset in offsets)
                {
                    if (ShapeLength(offset) is { } length) sides.Add(length);
                    else return false;
                }
                var text = "inset(" + string.Join(" ", CollapseBox(sides));
                if (radii != null)
                {
                    // The round corners are border radii: one to four of them, each possibly a
                    // horizontal and a vertical radius separated by '/'.
                    var corners = new List<string>();
                    foreach (var corner in radii)
                    {
                        if (corner == "/") { corners.Add("/"); continue; }
                        if (ShapeLength(corner) is { } radius) corners.Add(radius);
                        else return false;
                    }
                    int slash = corners.IndexOf("/");
                    if (slash < 0)
                    {
                        if (corners.Count == 0 || corners.Count > 4) return false;
                        text += " round " + string.Join(" ", CollapseBox(corners));
                    }
                    else
                    {
                        var horizontal = corners[..slash];
                        var vertical = corners[(slash + 1)..];
                        if (horizontal.Count == 0 || vertical.Count == 0
                            || horizontal.Count > 4 || vertical.Count > 4) return false;
                        var first = CollapseBox(horizontal);
                        var second = CollapseBox(vertical);
                        var pair = first.SequenceEqual(second)
                            ? string.Join(" ", first)
                            : string.Join(" ", first) + " / " + string.Join(" ", second);
                        text += " round " + pair;
                    }
                }
                canonical = text + ")";
                return true;
            }
            case "xywh":
            {
                // Four lengths and nothing else: the 'extend' and 'contain' flags of the shapes
                // grammar are no value here (measured: refused).
                var args = SplitTopLevel(inner);
                if (args.Count != 4) return false;
                var printed = new List<string>();
                foreach (var arg in args)
                {
                    if (ShapeLength(arg) is { } length) printed.Add(length);
                    else return false;
                }
                canonical = "xywh(" + string.Join(" ", printed) + ")";
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>The tokens of a shape's arguments, split at its one top-level keyword: what comes
    /// before it, and what comes after it — null when the keyword is absent, an empty list when the
    /// page wrote the keyword and nothing after it, which is no shape at all.</summary>
    private static bool TrySplitShapeClause(string inner, string keyword, out List<string> head,
        out List<string>? tail)
    {
        head = new List<string>();
        tail = null;
        var tokens = SplitTopLevel(inner);
        int at = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Equals(keyword, StringComparison.OrdinalIgnoreCase)) { at = i; break; }
        }
        if (at < 0)
        {
            head = tokens;
            return true;
        }
        head = tokens[..at].ToList();
        tail = tokens[(at + 1)..].ToList();
        return tail.Count > 0;
    }

    /// <summary>One &lt;length-percentage&gt; of a shape, printed the way the engine prints a
    /// length: a number the page wrote without a unit is the zero pixels it stands for (measured:
    /// <c>xywh(0 0 10px 10px)</c> reads <c>xywh(0px 0px 10px 10px)</c>), and a math function is
    /// kept whole because folding its constant parts is the same gap every other length has
    /// (#277). Anything else — a keyword, an angle, a name — is not a length.</summary>
    private static string? ShapeLength(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 0) return null;
        if (IsCalc(trimmed)) return trimmed;
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(trimmed, out var number,
                out var unit)) return null;
        var canonical = number.ToString("0.################", CultureInfo.InvariantCulture)
                        + (unit.Length == 0 ? "px" : unit.ToLowerInvariant());
        return CssPropertyApplier.IsLengthOrPercentageToken(canonical) ? canonical : null;
    }

    /// <summary>The four-value collapse of a one-to-four-item list (CSS 2.1 §5.10.1, and the same
    /// rule the reference engine applies to an inset's offsets and its round corners): the list is
    /// read as the four sides it means, and said again as the shortest sequence that means those
    /// four (measured: <c>inset(10px 20px 10px)</c> reads <c>inset(10px 20px)</c> and
    /// <c>round 5px 10px 5px 10px</c> reads <c>round 5px 10px</c>).</summary>
    private static List<string> CollapseBox(List<string> values)
    {
        string top = values[0];
        string right = values.Count > 1 ? values[1] : values[0];
        string bottom = values.Count > 2 ? values[2] : values[0];
        string left = values.Count > 3 ? values[3] : right;
        if (bottom == top && left == right) return top == right ? new List<string> { top }
            : new List<string> { top, right };
        if (left == right) return new List<string> { top, right, bottom };
        return new List<string> { top, right, bottom, left };
    }

    /// <summary>The computed text of <c>offset-path</c>: the shape the page wrote, measured in the
    /// box it will be drawn in. A length is resolved against the element's own font, a position
    /// inside the shape becomes the pair of edges and offsets it stands for, and a box given as
    /// <c>xywh()</c> is said as the insets it means (all three measured: <c>circle(2em at left 50%)</c>
    /// computes <c>circle(32px at 0% 50%)</c> and <c>xywh(10px 10px 10px 10px)</c> computes
    /// <c>inset(10px calc(100% - 20px) calc(100% - 20px) 10px)</c>). A path, a ray, a url and a
    /// geometry box are the text they already are.</summary>
    public static string OffsetPathComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (style == null) return canonical;
        var parts = new List<string>();
        foreach (var token in SplitTopLevel(canonical))
        {
            parts.Add(TryShapeFunction(token, out var name, out var inner)
                ? ShapeComputed(name, inner, style) : token);
        }
        return string.Join(" ", parts);
    }

    /// <summary>Whether the token is one of the four basic shapes whose arguments the computed
    /// value has to resolve. A <c>path()</c>, a <c>ray()</c> and a <c>url()</c> are not: their data
    /// is measured by nothing at computed-value time.</summary>
    private static bool TryShapeFunction(string token, out string name, out string inner)
    {
        int open = token.IndexOf('(');
        name = open <= 0 ? string.Empty : token[..open].ToLowerInvariant();
        inner = open <= 0 ? string.Empty : token[(open + 1)..^1].Trim();
        return name is "circle" or "ellipse" or "inset" or "xywh"
            && token.EndsWith(")", StringComparison.Ordinal);
    }

    private static string ShapeComputed(string name, string inner,
        Acrux.Core.Dom.ComputedStyle style)
    {
        switch (name)
        {
            case "circle":
            case "ellipse":
            {
                if (!TrySplitShapeClause(inner, "at", out var sizes, out var position))
                    return name + "(" + inner + ")";
                var parts = new List<string>();
                foreach (var size in sizes) parts.Add(ResolveShapeLength(size, style));
                var text = string.Join(" ", parts);
                if (position != null)
                    text += (text.Length > 0 ? " at " : "at ")
                            + PositionComputed(string.Join(" ", position), style);
                return name + "(" + text + ")";
            }
            case "inset":
            {
                if (!TrySplitShapeClause(inner, "round", out var offsets, out var radii))
                    return "inset(" + inner + ")";
                var sides = new List<string>();
                foreach (var offset in offsets) sides.Add(ResolveShapeLength(offset, style));
                var text = "inset(" + string.Join(" ", CollapseBox(sides));
                if (radii != null)
                {
                    var corners = new List<string>();
                    foreach (var radius in radii)
                        corners.Add(radius == "/" ? "/" : ResolveShapeLength(radius, style));
                    text += " round " + string.Join(" ", corners);
                }
                return text + ")";
            }
            case "xywh":
            {
                var args = SplitTopLevel(inner);
                if (args.Count != 4) return "xywh(" + inner + ")";
                var resolved = new List<string>();
                foreach (var arg in args) resolved.Add(ResolveShapeLength(arg, style));
                if (resolved.Count(r => IsCalc(r)) > 0) return "xywh(" + string.Join(" ", resolved) + ")";
                if (resolved.Count(r => TryEdgeValue(r, out _, out _)) != 4)
                    return "xywh(" + string.Join(" ", resolved) + ")";
                TryEdgeValue(resolved[0], out var xPx, out var xPct);
                TryEdgeValue(resolved[1], out var yPx, out var yPct);
                TryEdgeValue(resolved[2], out var wPx, out var wPct);
                TryEdgeValue(resolved[3], out var hPx, out var hPct);
                // The box is the four insets it leaves: the top and the left are the origin the
                // page wrote, and the right and the bottom are what is left of the reference box
                // once the origin and the size are taken off it.
                return "inset(" + resolved[1] + " " + RemainingInset(xPx, xPct, wPx, wPct) + " "
                     + RemainingInset(yPx, yPct, hPx, hPct) + " " + resolved[0] + ")";
            }
            default:
                return name + "(" + inner + ")";
        }
    }

    /// <summary>One length of a shape, resolved the way the same length is resolved anywhere else
    /// in the computed value: a font unit against this element's font, a percentage kept as the
    /// percentage it is, a math function kept whole.</summary>
    private static string ResolveShapeLength(string token, Acrux.Core.Dom.ComputedStyle style) =>
        ShapeLength(token) is { } length ? LengthPercentageComputed(length, style) : token;

    /// <summary>The number and the unit of a resolved length: <c>32px</c> is 32 pixels and no
    /// percentage, <c>50%</c> is 50 percent and no pixels.</summary>
    private static bool TryEdgeValue(string text, out double px, out double percent)
    {
        px = percent = 0;
        if (text.EndsWith("%", StringComparison.Ordinal))
            return double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out percent);
        return Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(text, out px, out var unit)
            && (unit.Length == 0 || unit.Equals("px", StringComparison.Ordinal));
    }

    /// <summary>What is left of a 100%-wide reference box once one origin and one size are taken
    /// off it. A pure percentage folds to that percentage; a mix of percentages and pixels is the
    /// arithmetic the engine cannot do yet, so it is said as the calculation it is (measured:
    /// <c>xywh(10% 0px 30% 40px)</c> computes <c>inset(0px 60% calc(100% - 40px) 10%)</c>).</summary>
    private static string RemainingInset(double originPx, double originPercent, double sizePx,
        double sizePercent)
    {
        double percent = 100 - originPercent - sizePercent;
        double px = -(originPx + sizePx);
        if (px == 0) return Number(percent) + "%";
        if (percent == 0) return Number(px) + "px";
        return "calc(" + Number(percent) + "% " + (px < 0 ? "- " : "+ ") + Number(Math.Abs(px))
             + "px)";
    }

    private static string Number(double value) =>
        value.ToString("0.################", CultureInfo.InvariantCulture);


    /// <summary>Motion Path 1 §3.3: one <c>&lt;length-percentage&gt;</c>. A bare number is no
    /// length — except zero, which the engine prints with the unit it means, and a number written
    /// in another spelling is echoed in the digits it stands for (measured: <c>offset-distance: 0</c>
    /// reads <c>0px</c> and <c>1e2px</c> reads <c>100px</c>), while <c>auto</c> and <c>normal</c> are
    /// not values of this property at all. A math function is kept whole: folding its constant
    /// parts is the same gap every other length has (#277).</summary>
    private static string? NormalizeOffsetDistance(string text)
    {
        var trimmed = text.Trim();
        // A math function is one value with spaces inside it, so it is read before the length
        // token test, which has no way to see past the first of them.
        if (IsCalc(trimmed)) return SplitTopLevel(trimmed).Count == 1 ? trimmed : null;
        return trimmed.Contains(' ') ? null : ShapeLength(trimmed);
    }

    /// <summary>The computed text of a length the page wrote: resolved against the element's own
    /// font and viewport, with a percentage kept as the percentage it is and a math function kept
    /// whole because part of it cannot be resolved yet (all three measured: <c>2em</c> on a 16px box
    /// is <c>32px</c>, <c>50%</c> stays <c>50%</c> and <c>calc(50% + 10px)</c> stays as written).</summary>
    public static string LengthPercentageComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (style == null || IsCalc(canonical)) return canonical;
        if (!CssPropertyApplier.IsLengthOrPercentageToken(canonical)) return canonical;
        var length = Acrux.Core.Dom.Length.Parse(canonical.ToLowerInvariant());
        return length is Acrux.Core.Dom.AutoLength ? canonical : style.ComputedLengthCss(length);
    }

    private static bool IsCalc(string text) =>
        text.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("max(", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase);

    /// <summary>Motion Path 1 §3.5: where the box is placed on the path. <c>auto</c> stands alone —
    /// two of them are no value (measured) — and anything else is a position, which the engine
    /// prints as its two axes with the horizontal one first, the axis the page never named filled
    /// with the middle of the box (measured: <c>bottom right</c> reads <c>right bottom</c> and
    /// <c>top</c> reads <c>center top</c>).</summary>
    private static string? NormalizeOffsetAnchor(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        return ShorthandExpander.PositionPairText(trimmed);
    }

    /// <summary>Motion Path 1 §3.6: where an element with no path starts. <c>normal</c> and
    /// <c>auto</c> stand alone, and a position is printed as the pair of axes it names (measured:
    /// <c>offset-position: none</c> is refused, so the initial stays <c>normal</c>).</summary>
    private static string? NormalizeOffsetPosition(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        return ShorthandExpander.PositionPairText(trimmed);
    }

    /// <summary>The computed text of a position: each axis resolved to the place in the box it
    /// stands for — the keywords are the edges and the middle, a length is itself (measured:
    /// <c>right 50%</c> computes <c>100% 50%</c> and <c>top</c> computes <c>50% 0%</c>).</summary>
    public static string PositionComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical is "auto" or "normal" || style == null) return canonical;
        var words = SplitTopLevel(canonical);
        if (words.Count == 0) return canonical;
        if (!ShorthandExpander.TryMatchBackgroundPosition(words, out var x, out var y))
            return canonical;
        return AxisComputed(x, true, style) + " " + AxisComputed(y, false, style);
    }

    private static string AxisComputed(ShorthandExpander.PositionAxis axis, bool horizontal,
        Acrux.Core.Dom.ComputedStyle style)
    {
        // A math function on an axis is printed as written: its percentage half has no box to be
        // resolved against yet (measured: 'calc(50% + 10px) top' computes 'calc(50% + 10px) 0%').
        if (axis.Present && axis.Edge == null && IsCalc(axis.Offset ?? "")) return axis.Offset!;
        var length = CssPropertyApplier.ResolvePositionAxis(axis, horizontal);
        return length == null ? "center" : style.ComputedLengthCss(length);
    }

    /// <summary>The items of a 'content' value. Whitespace separates them, but two strings written
    /// side by side are still two items — the reference engine reads 'content: "a""b"' as the pair
    /// it prints, '"a" "b"' (measured).</summary>
    public static List<string> SplitContentItems(string text)
    {
        var items = new List<string>();
        foreach (var token in SplitTopLevel(text))
        {
            var strings = RunOfQuotedStrings(token);
            if (strings != null) items.AddRange(strings);
            else items.Add(token);
        }
        return items;
    }

    /// <summary>The quoted strings a token is made of, or null when it is anything else — so
    /// '"a""b"' is two items while '"a" attr(x)' and 'url("a")' stay exactly as they are.</summary>
    private static List<string>? RunOfQuotedStrings(string token)
    {
        var found = new List<string>();
        int i = 0;
        while (i < token.Length)
        {
            char quote = token[i];
            if (quote != '"' && quote != '\'') return null;
            int j = i + 1;
            while (j < token.Length)
            {
                if (token[j] == '\\') { j += 2; continue; }
                if (token[j] == quote) break;
                j++;
            }
            if (j >= token.Length) return null;   // the string never closes
            found.Add(token.Substring(i, j - i + 1));
            i = j + 1;
        }
        return found.Count > 0 ? found : null;
    }

    /// <summary>Whether one token is a shape <c>offset-path</c> takes. The <c>'offset'</c> shorthand
    /// reads its parts one token at a time, and this is the line between the shape and the distance
    /// written beside it.</summary>
    public static bool IsOffsetPathShape(string token) => IsOffsetShape(token, out _);

    /// <summary>Whether one token names a geometry box a path can be measured against.</summary>
    public static bool IsOffsetBoxKeyword(string token) => IsOneOf(token, OffsetBoxes);

    /// <summary>The whitespace-separated tokens of a value, with a function call and a quoted string
    /// each kept as the one token they are.</summary>
    public static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = -1;
        bool inString = false;
        char quote = '"';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < text.Length) i++;
                else if (c == quote) inString = false;
                continue;
            }
            if (c is '"' or '\'') { inString = true; quote = c; if (start < 0) start = i; continue; }
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (start >= 0 && i > start) parts.Add(text[start..i]);
                start = -1;
                continue;
            }
            if (start < 0) start = i;
        }
        if (start >= 0 && start < text.Length) parts.Add(text[start..]);
        return parts;
    }

    private static bool IsOneOf(string token, string[] allowed)
    {
        foreach (var a in allowed)
            if (token.Equals(a, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Re-print SVG path data: each command letter on its own, its numbers after it.
    /// A letter the grammar does not know, or a number it cannot read, is no path at all.</summary>
    private static string? CanonicalPathData(string data)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < data.Length)
        {
            char c = data[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(c) < 0) return null;
            parts.Add(c.ToString());
            i++;
            while (i < data.Length)
            {
                int start = i;
                if (data[i] is '+' or '-' or '.' || char.IsAsciiDigit(data[i]))
                {
                    i++;
                    while (i < data.Length && (char.IsAsciiDigit(data[i]) || data[i] is '.' or 'e' or 'E'
                        || ((data[i] == '+' || data[i] == '-') && (data[i - 1] == 'e' || data[i - 1] == 'E'))))
                        i++;
                    if (!double.TryParse(data[start..i], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var value)) return null;
                    parts.Add(value.ToString("0.################", CultureInfo.InvariantCulture));
                }
                else if (char.IsWhiteSpace(data[i])) { i++; }
                else break;
            }
        }
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>CSS Motion Path 1 §3.4: <c>auto</c>, optionally with an angle, <c>reverse</c>,
    /// optionally with an angle, or a bare angle. The engine prints <c>reverse</c> as the rotation
    /// it stands for (measured), and keeps an angle beside <c>auto</c> or <c>reverse</c> in the
    /// order it was written.</summary>
    private static string? NormalizeOffsetRotate(string text)
    {
        var words = Words(text);
        if (words.Length == 0) return null;
        if (words.Length == 1)
        {
            var one = words[0].ToLowerInvariant();
            if (one == "auto") return "auto";
            if (one == "reverse") return "reverse";
            return IsAngle(one) ? one : null;
        }
        if (words.Length != 2) return null;
        var first = words[0].ToLowerInvariant();
        if (first != "auto" && first != "reverse") return null;
        var angle = words[1].ToLowerInvariant();
        return IsAngle(angle) ? first + " " + angle : null;
    }

    /// <summary>The computed text of <c>offset-rotate</c>: the engine always spells out the rotation
    /// it stands for, so <c>auto</c> becomes <c>auto 0deg</c> and <c>reverse</c> the half turn it
    /// means — with an angle beside it, the turn it adds (all three measured: <c>reverse</c> is
    /// <c>auto 180deg</c> and <c>reverse -45deg</c> is <c>auto 135deg</c>), while an angle the page
    /// wrote beside <c>auto</c> is kept as it is.</summary>
    public static string OffsetRotateComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        var words = SplitTopLevel(canonical);
        if (words.Count == 1)
        {
            if (words[0].Equals("reverse", StringComparison.OrdinalIgnoreCase)) return "auto 180deg";
            if (words[0].Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto 0deg";
            return canonical;
        }
        if (words.Count == 2 && words[0].Equals("reverse", StringComparison.OrdinalIgnoreCase)
            && TryAngleInDegrees(words[1], out var degrees))
            return "auto " + Number(degrees + 180) + "deg";
        return canonical;
    }

    /// <summary>The degrees an angle stands for; a unit the engine does not know is no angle.</summary>
    private static bool TryAngleInDegrees(string token, out double degrees)
    {
        degrees = 0;
        foreach (var (suffix, factor) in new[]
                 {
                     ("deg", 1.0), ("grad", 0.9), ("rad", 180.0 / System.Math.PI), ("turn", 360.0)
                 })
        {
            if (token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(token[..^suffix.Length], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value))
            {
                degrees = value * factor;
                return true;
            }
        }
        return false;
    }

    private static bool IsAngle(string w)
    {
        foreach (var suffix in new[] { "deg", "grad", "rad", "turn" })
            if (w.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(w[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out _)) return true;
        return false;
    }

    /// <summary>Scroll-driven Animations 1 §3: <c>auto</c>, <c>none</c>, a dashed ident naming a
    /// scroll timeline, or one of the timeline functions with its arguments. A bare ident is not a
    /// name — the dashed form is (measured: 't' is refused, '--t' is kept, and 'scroll()' is kept).</summary>
    private static string? NormalizeTimeline(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        foreach (var layer in trimmed.Split(','))
        {
            var one = layer.Trim();
            if (one.Length == 0) return null;
            if (one.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || one.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsDashedIdent(one)) continue;
            int open = one.IndexOf('(');
            if (open <= 0 || !one.EndsWith(")", StringComparison.Ordinal)) return null;
            var function = one[..open].TrimEnd();
            if (!function.Equals("scroll", StringComparison.OrdinalIgnoreCase)
                && !function.Equals("view", StringComparison.OrdinalIgnoreCase)) return null;
        }
        return trimmed.ToLowerInvariant();
    }

    private static string? NormalizeViewTransitionName(string text)
    {
        var words = Words(text);
        if (words.Length != 1) return null;
        var w = words[0];
        if (w.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        // 'auto' is reserved for the engine, and a keyword of another property is no custom name
        // (measured: 'view-transition-name: auto' is refused).
        return IsCustomIdent(w) && !w.Equals("auto", StringComparison.OrdinalIgnoreCase) ? w : null;
    }

    private static string? NormalizePositionAnchor(string text)
    {
        var words = Words(text);
        if (words.Length != 1) return null;
        var w = words[0];
        if (w.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        return IsDashedIdent(w) || IsCustomIdent(w) ? w : null;
    }

    private static string? NormalizeContainerName(string text)
    {
        var words = Words(text);
        if (words.Length == 0 || text.Contains(',')) return null;
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        var parts = new List<string>();
        foreach (var w in words)
        {
            if (!IsCustomIdent(w) || w.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
            parts.Add(w);
        }
        return string.Join(" ", parts);
    }

    /// <summary>CSS Fill and Stroke 3 §2: a <c>&lt;paint&gt;</c> is <c>none</c>, <c>currentColor</c>
    /// or a colour. A colour written as a name keeps it, and any other spelling becomes the rgb()
    /// form the engine prints (measured: 'stroke: blue' reads 'blue' while 'fill: #00ff00' reads
    /// 'rgb(0, 255, 0)') — the same split every other colour property in this engine already makes.</summary>
    private static string? NormalizePaint(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (trimmed.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)) return "currentcolor";
        if (!Acrux.Core.Css.ColorParser.IsColorToken(trimmed)) return null;
        bool isName = char.IsAsciiLetter(trimmed[0])
            && !trimmed.Contains('(') && !trimmed.Contains('#');
        if (isName) return trimmed.ToLowerInvariant();
        var color = Acrux.Core.Css.ColorParser.Parse(trimmed);
        return Acrux.Core.Dom.Animations.ComputedValueSerializer.ColorText(color);
    }

    /// <summary>The computed <c>&lt;paint&gt;</c>: 'currentColor' becomes the element's own colour,
    /// which is the one place the keyword never survives, and a named colour becomes the rgb() triple
    /// it stands for (measured: 'fill: red' specifies 'red' and computes 'rgb(255, 0, 0)').</summary>
    public static string PaintComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
            return style == null ? "rgb(0, 0, 0)"
                : Acrux.Core.Dom.Animations.ComputedValueSerializer.ColorText(style.Color);
        if (canonical.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (!canonical.Contains('('))
            return Acrux.Core.Dom.Animations.ComputedValueSerializer
                .ColorText(Acrux.Core.Css.ColorParser.Parse(canonical));
        return canonical;
    }

    /// <summary>A <c>&lt;number&gt;</c> or <c>&lt;percentage&gt;</c>, echoed in the digits it stands
    /// for. The opacity properties take one; a negative or a non-number is no value.</summary>
    private static string? NormalizeOpacityValue(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            return double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out var percent) && percent >= 0
                ? Number(percent) + "%" : null;
        }
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number >= 0 ? Number(number) : null;
    }

    /// <summary>A positive <c>&lt;number&gt;</c>, which is what 'stroke-miterlimit' measures.</summary>
    private static string? NormalizePositiveNumber(string text)
    {
        var trimmed = text.Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number > 0 ? Number(number) : null;
    }

    /// <summary>CSS Fill and Stroke 3 §4.5: a <c>&lt;length&gt;</c>, a <c>&lt;percentage&gt;</c> or a
    /// bare number, which is a number of pixels. The specified value keeps the spelling the page
    /// wrote (measured: 'stroke-width: 2' reads '2'), so only the digits are normalised.</summary>
    private static string? NormalizeSvgLength(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
            return double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out var percent) ? Number(percent) + "%" : null;
        if (Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(trimmed, out var number,
                out var unit))
        {
            return unit.Length == 0 ? Number(number)
                : Number(number) + unit.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>The computed length: a bare number is the pixel count it means, a length is priced
    /// against the element's own font, and a percentage is left alone (measured: '2' computes '2px'
    /// and '50%' computes '50%').</summary>
    public static string SvgLengthComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical.EndsWith("%", StringComparison.Ordinal) || style == null) return canonical;
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(canonical, out var number,
                out var unit)) return canonical;
        if (unit.Length == 0) return Number(number) + "px";
        var length = Acrux.Core.Dom.Length.Parse(canonical.ToLowerInvariant());
        return length is Acrux.Core.Dom.AutoLength ? canonical : style.ComputedLengthCss(length);
    }

    /// <summary>CSS Fill and Stroke 3 §5.5: 'none' or a list of lengths, numbers and percentages.
    /// The page may separate the parts with commas, with spaces or with both, and the reference
    /// engine always prints them comma-separated (measured: '5 3' and '5,3' both read '5, 3').</summary>
    private static string? NormalizeDashArray(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        var parts = trimmed.Split(new[] { ',', ' ', '\t' },
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var printed = new List<string>();
        foreach (var part in parts)
        {
            if (NormalizeSvgLength(part) is not { } one) return null;
            printed.Add(one);
        }
        return string.Join(", ", printed);
    }

    /// <summary>The computed dash list: every part priced the way 'stroke-width' is.</summary>
    public static string DashArrayComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        var parts = canonical.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.TrimEntries
            | StringSplitOptions.RemoveEmptyEntries);
        var printed = new List<string>();
        foreach (var part in parts) printed.Add(SvgLengthComputed(part, style));
        return string.Join(", ", printed);
    }

    /// <summary>SVG 2 §11.3: a marker is <c>none</c> or one <c>url()</c>. A bare ident names no
    /// image, so 'marker-start: bogus' is refused (measured).</summary>
    private static string? NormalizeMarker(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith(")", StringComparison.Ordinal)) return null;
        var inner = trimmed[4..^1].Trim();
        var address = IsQuotedString(inner) ? Unquote(inner) : inner;
        return address.Length == 0 ? null : "url(\"" + address + "\")";
    }

    /// <summary>The computed marker: a relative address becomes the absolute one the image is
    /// looked up by, and a fragment reference is already complete.</summary>
    public static string MarkerComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical.Equals("none", StringComparison.OrdinalIgnoreCase)) return canonical;
        var printed = Acrux.Core.Css.ImageValueCanonicalizer.Canonicalize(canonical);
        return string.IsNullOrWhiteSpace(printed) ? canonical : printed;
    }

    // ===== the small token tests every entry above leans on =====
    private static bool IsQuotedString(string text) => text.Length >= 2
        && (text[0] == '"' || text[0] == '\'') && text[text.Length - 1] == text[0];

    private static string Unquote(string text) => text[1..^1];

    private static bool IsDashedIdent(string w) => w.Length > 2 && w[0] == '-' && w[1] == '-'
        && IsIdentChars(w[2..]);

    private static bool IsCustomIdent(string w) => w.Length > 0 && w[0] != '-' && IsIdentChars(w);

    private static bool IsIdentChars(string w)
    {
        foreach (var c in w)
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '\\')) return false;
        return w.Length > 0;
    }
}
