namespace Acrux.Core.Css;

/// <summary>
/// The value of the <c>size</c> a page box is printed on (CSS Paged Media 4 §3.4), read as the
/// reference engine reads it: one named paper, one or two lengths, or <c>auto</c> — and a direction
/// keyword that says which way the named paper lies.
/// <para>
/// The engine has no page box to lay out, so this is the value as the CSSOM records and prints it,
/// which is where the difference between the grammar and a bare identifier list shows: a page size
/// is a keyword the engine folds to its own spelling (<c>size: A4</c> reads back <c>a4</c>, measured)
/// and the pair of named papers a stylesheet might write is not one of them.
/// </para>
/// </summary>
public static class CssPageSizeValue
{
    private static readonly string[] PaperSizes =
    {
        "a3", "a4", "a5", "a6", "b4", "b5", "jis-b4", "jis-b5",
        "letter", "legal", "ledger",
    };

    /// <summary>Whether the text is a page size, and the spelling the reference engine prints it
    /// in: lower case, and without the <c>portrait</c> a named paper already carries (measured,
    /// both <c>A4 portrait</c> and <c>portrait A4</c> read back as <c>a4</c> while
    /// <c>a4 landscape</c> keeps the turn). Two named papers, a percentage, a quoted name and a
    /// direction written on <c>auto</c> are no size at all.</summary>
    public static bool TryCanonicalize(string text, out string canonical)
    {
        canonical = "";
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        string? direction = null;
        var sizes = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            var lower = token.ToLowerInvariant();
            if (lower is "portrait" or "landscape")
            {
                if (direction != null) return false;
                direction = lower;
                continue;
            }
            sizes.Add(lower);
        }

        // 'auto' is a size of its own and turns no way.
        if (sizes.Contains("auto"))
        {
            if (sizes.Count != 1 || direction != null) return false;
            canonical = "auto";
            return true;
        }

        if (sizes.Count == 1 && PaperSizes.Contains(sizes[0]))
        {
            // The named papers of the grammar all lie portrait, so the keyword that says so again
            // is not part of what the engine prints.
            canonical = direction == "landscape" ? sizes[0] + " landscape" : sizes[0];
            return true;
        }

        // A length pair carries no direction at all here: measured, '20cm 30cm' is a size and
        // '20cm 30cm landscape' is not, whatever the draft's grammar may allow.
        if (sizes.Count <= 2 && direction == null && sizes.TrueForAll(IsLength))
        {
            canonical = string.Join(" ", sizes);
            return true;
        }

        // One direction keyword on its own is the size a page asks for without naming a paper.
        if (sizes.Count == 0 && direction != null)
        {
            canonical = direction!;
            return true;
        }

        return false;
    }

    private static bool IsLength(string token)
    {
        int i = 0;
        if (i < token.Length && (token[i] == '+' || token[i] == '-')) i++;
        int digits = i;
        while (i < token.Length && (char.IsAsciiDigit(token[i]) || token[i] == '.')) i++;
        if (i == digits) return false;
        // A length is a number and a unit name; the bare zero a stylesheet may write and the
        // percentage a page box has no size in are both refused here, and so is a function —
        // 'size: calc(20cm + 1in)' is not a size this engine records.
        var unit = token[i..];
        if (unit.Length == 0) return false;
        foreach (char c in unit)
            if (!char.IsLetter(c) && c != '-' && c != '_' && c <= '\u007F') return false;
        return char.IsLetter(unit[0]);
    }
}
