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
        /// <c>math-depth</c> and <c>offset-rotate</c> shape, where the engine expands a keyword.</summary>
        public Func<string, string>? ComputedText { get; init; }
    }

    /// <summary>The entry for a property name, or null when the property is not echo-only.</summary>
    public static Entry? Find(string name)
        => ByName.TryGetValue(name.Trim().ToLowerInvariant(), out var entry) ? entry : null;

    /// <summary>The canonical text of a value, or null when the value is refused.</summary>
    public static string? Normalize(string name, string value) => Find(name)?.Normalize(value);

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

            // ===== Motion Path 1 §3. The path is echoed as the engine reads it — the command
            // letter, then its numbers, each separated (measured: 'path("M0 0")' reads
            // 'path("M 0 0")') — and 'reverse' is a rotation the engine prints as the angle it
            // means (measured: 'offset-rotate: reverse' computes 'auto 180deg').
            new() { Name = "offset-path", Initial = "none", Normalize = NormalizeOffsetPath },
            new() { Name = "offset-rotate", Initial = "auto", Normalize = NormalizeOffsetRotate,
                ComputedText = OffsetRotateComputed },

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
    public static string MathDepthComputed(string canonical)
    {
        if (canonical.Equals("auto-add", StringComparison.OrdinalIgnoreCase)) return "0";
        int open = canonical.IndexOf('('), close = canonical.IndexOf(')');
        return open >= 0 && close > open ? canonical[(open + 1)..close].Trim() : canonical;
    }

    private static readonly string[] OffsetBoxes =
    {
        "content-box", "border-box", "padding-box", "margin-box", "stroke-box", "fill-box"
    };

    /// <summary>CSS Motion Path 1 §3.2: <c>none</c>, one of the geometry boxes, or a
    /// <c>path()</c>/<c>ray()</c> function. The path data is echoed the way the engine reads it —
    /// command letter, then its numbers, space separated (measured: <c>path("M0 0")</c> reads
    /// <c>path("M 0 0")</c>).</summary>
    private static string? NormalizeOffsetPath(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        foreach (var box in OffsetBoxes)
            if (trimmed.Equals(box, StringComparison.OrdinalIgnoreCase)) return box;
        if (trimmed.StartsWith("path(", StringComparison.OrdinalIgnoreCase))
        {
            int close = FindMatchingParen(trimmed, 4);
            if (close < 0 || trimmed.Length != close + 1) return null;
            var inner = trimmed[5..close].Trim();
            if (!IsQuotedString(inner)) return null;
            var data = CanonicalPathData(Unquote(inner));
            return data == null ? null : "path(\"" + data + "\")";
        }
        return trimmed.StartsWith("ray(", StringComparison.OrdinalIgnoreCase) ? trimmed.ToLowerInvariant() : null;
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

    private static int FindMatchingParen(string text, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>CSS Motion Path 1 §3.4: <c>auto</c>, optionally with an angle, <c>reverse</c>, or a
    /// bare angle. The engine prints <c>reverse</c> as the rotation it stands for (measured), and
    /// keeps an angle beside <c>auto</c> in the order it was written.</summary>
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
        if (words.Length == 2 && words[0].Equals("auto", StringComparison.OrdinalIgnoreCase)
            && IsAngle(words[1].ToLowerInvariant()))
            return "auto " + words[1].ToLowerInvariant();
        return null;
    }

    /// <summary>The computed text of <c>offset-rotate</c>: <c>reverse</c> is a rotation the engine
    /// spells out, while the specified value keeps the word the page wrote (both measured).</summary>
    public static string OffsetRotateComputed(string canonical) =>
        canonical.Equals("reverse", StringComparison.OrdinalIgnoreCase) ? "auto 180deg" : canonical;

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
