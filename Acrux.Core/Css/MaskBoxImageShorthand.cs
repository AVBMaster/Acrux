namespace Acrux.Core.Css;

/// <summary>
/// The legacy <c>-webkit-mask-box-image</c> shorthand — CSS Masking 1's <c>mask-border</c> under
/// its old name, which the reference engine still declares while <c>mask-border</c> itself is not
/// a property there at all.
/// <para>
/// The engine has no serialisation for the shorthand: setting it writes its <b>five longhands</b>
/// into the declaration block, the shorthand itself reads back as the empty string, and the
/// longhands the value left out are written as <c>initial</c> (all measured,
/// snapshots/_b259_mask_box_image_truth.txt). So the split belongs where a declaration is
/// stored, not where one is printed, and this class is the single place that knows its grammar.
/// </para>
/// <para>
/// The shape is <c>none | &lt;image&gt; &lt;slice&gt;? / &lt;width&gt;? / &lt;outset&gt;
/// &lt;repeat&gt;?</c>: each slash section may be left out but its slash may not, and nothing may
/// follow the third. The slice written by the shorthand always carries <c>fill</c>, even when the
/// author did not type it — measured, <c>url(a.png) 3</c> gives the longhand <c>3 fill</c> — while
/// the same number written to the longhand alone stays <c>3</c>.
/// </para>
/// </summary>
public static class MaskBoxImageShorthand
{
    public const string Name = "-webkit-mask-box-image";

    /// <summary>The five longhands, in the order the reference engine writes them.</summary>
    public static readonly string[] Longhands =
    {
        "-webkit-mask-box-image-source",
        "-webkit-mask-box-image-slice",
        "-webkit-mask-box-image-width",
        "-webkit-mask-box-image-outset",
        "-webkit-mask-box-image-repeat",
    };

    private static readonly string[] RepeatKeywords = { "stretch", "round", "repeat", "space" };
    private static readonly string[] LengthUnits =
    {
        "px", "em", "rem", "ex", "ch", "cap", "ic", "lh", "rlh", "vw", "vh", "vi", "vb",
        "vmin", "vmax", "svw", "svh", "lvw", "lvh", "dvw", "dvh", "cm", "mm", "q", "in", "pt", "pc",
    };

    /// <summary>Whether a declaration name is this shorthand, spelled with its prefix.</summary>
    public static bool IsShorthand(string? name) =>
        !string.IsNullOrEmpty(name) && name!.Equals(Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the value is one this grammar reads — the gate the property's value set
    /// asks for before a declaration is stored.</summary>
    public static bool TextIsValid(string? value) => Parse(value) != null;

    /// <summary>Parse the shorthand into its five longhand values, in <see cref="Longhands"/>
    /// order, or null when the grammar refuses it and no declaration is made.</summary>
    public static string[]? Parse(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;

        // 'none' stands alone: 'none / 4px' says nothing about a slice or a width and is refused
        // (measured), so this is the one case where the whole value is a single keyword.
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return Fill("none");

        var sections = SplitSections(text);
        if (sections == null || sections.Count is < 1 or > 3) return null;

        var first = SplitTopLevel(sections[0]);
        if (first.Count == 0 || !IsImage(first[0])) return null;
        var source = first[0];

        string? slice = null;
        if (first.Count > 1)
        {
            // The slice list is the longhand's, with the same 'fill' placement (either end, never
            // inside, never alone) — and the shorthand always writes the flag: '3' becomes
            // '3 fill', while the same number given to the longhand alone stays '3' (both measured).
            var numbers = first.GetRange(1, first.Count - 1);
            if (numbers.Count > 0
                && numbers[^1].Equals("fill", StringComparison.OrdinalIgnoreCase))
                numbers.RemoveAt(numbers.Count - 1);
            else if (numbers.Count > 1
                && numbers[0].Equals("fill", StringComparison.OrdinalIgnoreCase))
                numbers.RemoveAt(0);
            if (numbers.Count is < 1 or > 4) return null;
            if (!numbers.TrueForAll(IsSliceNumber)) return null;
            slice = string.Join(" ", numbers) + " fill";
        }
        else if (sections.Count > 1)
        {
            // A slash with no slice in front of it is a hole in the grammar: 'url(a.png) / 4px'
            // makes no declaration at all, while 'url(a.png)' on its own leaves the slice at its
            // initial value (both measured).
            return null;
        }

        string? width = null;
        if (sections.Count >= 2)
        {
            var parts = SplitTopLevel(sections[1]);
            if (parts.Count is > 4) return null;
            // An empty middle section is legal ('3 / / 2px' leaves the width initial, measured).
            if (parts.Count > 0)
            {
                if (!parts.TrueForAll(p => IsLength(p, allowPercent: true)
                        || p.Equals("auto", StringComparison.OrdinalIgnoreCase)))
                    return null;
                width = string.Join(" ", parts);
            }
        }

        string? outset = null;
        string? repeat = null;
        if (sections.Count == 3)
        {
            var parts = SplitTopLevel(sections[2]);
            int i = 0;
            var lengths = new List<string>();
            while (i < parts.Count && IsLength(parts[i], allowPercent: false))
            {
                lengths.Add(parts[i]);
                i++;
                if (lengths.Count > 4) return null;
            }
            // The outset is what the third section opens with; a section that starts with a
            // repeat keyword has no outset and is refused (measured over four-section values).
            if (lengths.Count == 0) return null;
            outset = string.Join(" ", lengths);
            var repeats = new List<string>();
            for (; i < parts.Count; i++)
            {
                if (!IsOneOf(parts[i], RepeatKeywords)) return null;
                repeats.Add(parts[i].ToLowerInvariant());
            }
            if (repeats.Count > 2) return null;
            if (repeats.Count > 0) repeat = string.Join(" ", repeats);
        }

        return Fill(source, slice, width, outset, repeat);
    }

    /// <summary>Whether a token is one piece of a slice list — the test the longhand's own grammar
    /// runs too, so the shorthand and the longhand cannot disagree about what a slice is.</summary>
    public static bool SlicePartIsValid(string token) => IsSliceNumber(token);

    /// <summary>The slice in the one order the reference engine prints it: the numbers first, the
    /// <c>fill</c> flag last, whichever way the author put them (<c>fill 3</c> reads back as
    /// <c>3 fill</c>, measured). Anything else is left exactly as written, because a slice this
    /// grammar does not read is dropped rather than repaired.</summary>
    public static string NormalizeSlice(string? value)
    {
        var parts = SplitTopLevel((value ?? string.Empty).Trim());
        if (parts.Count < 2 || !parts[0].Equals("fill", StringComparison.OrdinalIgnoreCase))
            return value ?? string.Empty;
        // Only reorder when what remains is a slice the grammar reads; a value it refuses has no
        // canonical form to take.
        var rest = parts.GetRange(1, parts.Count - 1);
        if (rest.Count is < 1 or > 4 || !rest.TrueForAll(SlicePartIsValid)) return value ?? string.Empty;
        return string.Join(" ", rest) + " fill";
    }

    /// <summary>Whether a token is one piece of a width list (a length, a percentage, a bare
    /// number or <c>auto</c>) or of an outset list (the same without percentages and without
    /// <c>auto</c>, both measured).</summary>
    public static bool WidthPartIsValid(string token) =>
        token.Equals("auto", StringComparison.OrdinalIgnoreCase) || IsLength(token, allowPercent: true);

    public static bool OutsetPartIsValid(string token) => IsLength(token, allowPercent: false);

    private static string[] Fill(string source, string? slice = null, string? width = null,
        string? outset = null, string? repeat = null) => new[]
    {
        source, slice ?? "initial", width ?? "initial", outset ?? "initial", repeat ?? "initial",
    };

    /// <summary>The value split on top-level slashes, or null when a slash has nothing on one
    /// side of it — 'a / b' with an empty tail is not three sections' worth of nothing.</summary>
    private static List<string>? SplitSections(string text)
    {
        var sections = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == '/' && depth == 0)
            {
                if (i == start) return null;               // a slash with nothing before it
                sections.Add(text[start..i]);
                start = i + 1;
            }
        }
        if (start == text.Length + 1) return null;
        sections.Add(text[start..]);
        return sections.Count > 3 ? null : sections;
    }

    /// <summary>Whitespace-separated tokens, with a function and its arguments kept in one piece.</summary>
    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (i > start) parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        if (text.Length > start) parts.Add(text[start..]);
        return parts;
    }

    /// <summary>An image the mask grammar takes: <c>url()</c>, <c>image()</c>, <c>paint()</c> or a
    /// gradient function. A colour is not an image and neither is a bare word.</summary>
    private static bool IsImage(string token)
    {
        int open = token.IndexOf('(');
        if (open <= 0 || !token.EndsWith(")", StringComparison.Ordinal)) return false;
        var name = token[..open];
        if (name.Equals("url", StringComparison.OrdinalIgnoreCase)
            || name.Equals("image", StringComparison.OrdinalIgnoreCase)
            || name.Equals("paint", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.EndsWith("-gradient", StringComparison.OrdinalIgnoreCase)
            && IsOneOf(name.ToLowerInvariant(), new[]
            {
                "linear-gradient", "repeating-linear-gradient", "radial-gradient",
                "repeating-radial-gradient", "conic-gradient", "repeating-conic-gradient",
                "cross-fade",
            });
    }

    /// <summary>A slice number: non-negative, with or without a per-cent sign, and no unit.
    /// <c>2 30%</c> is a legal slice and <c>-2</c> is not (both measured).</summary>
    private static bool IsSliceNumber(string token) => IsNumber(token, allowPercent: true);

    private static bool IsLength(string token, bool allowPercent)
    {
        if (token.Length == 0) return false;
        int cut = token.Length;
        while (cut > 0 && char.IsLetter(token[cut - 1])) cut--;
        var number = token[..cut];
        var unit = token[cut..];
        if (unit.Length == 0) return IsNumber(number, allowPercent);
        if (unit == "%") return allowPercent && IsNumber(number, allowPercent: true);
        foreach (var known in LengthUnits)
            if (unit.Equals(known, StringComparison.OrdinalIgnoreCase)) return IsNumber(number, allowPercent: false);
        // 'vh'/'vw' and their siblings are covered above; anything else ('2foo') is no length.
        return false;
    }

    private static bool IsNumber(string token, bool allowPercent)
    {
        if (token.Length == 0) return false;
        if (allowPercent && token[^1] == '%') token = token[..^1];
        if (token.Length == 0) return false;
        if (token[0] == '-') return false;
        bool seenDigit = false, seenDot = false;
        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            if (char.IsAsciiDigit(c)) { seenDigit = true; continue; }
            if (c == '.' && !seenDot) { seenDot = true; continue; }
            if ((c == 'e' || c == 'E') && i + 1 < token.Length
                && (char.IsAsciiDigit(token[i + 1]) || ((token[i + 1] == '+' || token[i + 1] == '-') && i + 2 < token.Length)))
            {
                // An exponent is a number the tokenizer folds before printing, so it reads here
                // like the value it means; the reference engine drops it for a slice.
                return false;
            }
            return false;
        }
        return seenDigit;
    }

    private static bool IsOneOf(string token, string[] words)
    {
        foreach (var word in words)
            if (token.Equals(word, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
