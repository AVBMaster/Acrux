using Acrux.Core.Css.Properties;

namespace Acrux.Core.Css;

/// <summary>The three CSS 2.1 break names the reference engine does not keep. Each is stored under
/// the CSS Fragmentation 3 property it aliases, and the one value the two names spell differently —
/// CSS 2.1's 'always', which the modern name calls 'page' — is folded on the way in and re-spelled
/// on the way out (measured, value by value: snapshots/_probe_b290_page_break_alias.html. Writing
/// 'page-break-before: always' leaves the block carrying 'break-before: page', 'getPropertyValue'
/// and 'style.pageBreakBefore' both answer 'always', the computed style answers 'page-break-before'
/// with 'always' and 'break-before' with 'page', while a value the legacy name has no word for —
/// 'break-before: column' read as 'page-break-before' — answers the empty string).</summary>
public static class LegacyBreakAlias
{
    private sealed record Entry(
        string Legacy,
        string Modern,
        CssPropertyId LegacyId,
        string[] Values,
        string? FoldFrom,
        string? FoldTo,
        // Whether the legacy name keeps nothing of its own once the block carries the modern
        // property. 'page-break-before' reads back the word it wrote; the column spellings are
        // aliases the reference engine has no serialisation for, so a block carrying
        // 'break-before: avoid' answers 'getPropertyValue("-webkit-column-break-before")' with the
        // empty string while the computed style still answers that name with 'avoid' — and a
        // CSS-wide keyword is the one value the alias prints back (all measured).
        bool EchoesNothing = false);

    /// <summary>The value set each legacy name takes, measured one keyword at a time: 'page', 'recto'
    /// and 'avoid-column' belong to the modern name alone, and 'page-break-inside: avoid-column' is
    /// dropped while 'break-inside: avoid-column' stands. The three column spellings take a smaller
    /// set still — CSS Fragmentation 3 §3.2's 'always' is their word for what the modern name calls
    /// 'column', and the page-side keywords 'left', 'right' and 'page' belong to no column break
    /// (measured, snapshots/_probe_b316_edges.html).</summary>
    private static readonly Entry[] Entries =
    {
        new("page-break-before", "break-before", CssPropertyId.PageBreakBefore,
            new[] { "auto", "always", "avoid", "left", "right" }, "always", "page"),
        new("page-break-after", "break-after", CssPropertyId.PageBreakAfter,
            new[] { "auto", "always", "avoid", "left", "right" }, "always", "page"),
        new("page-break-inside", "break-inside", CssPropertyId.PageBreakInside,
            new[] { "auto", "avoid" }, null, null),
        new("-webkit-column-break-before", "break-before", CssPropertyId.WebkitColumnBreakBefore,
            new[] { "auto", "always", "avoid" }, "always", "column", true),
        new("-webkit-column-break-after", "break-after", CssPropertyId.WebkitColumnBreakAfter,
            new[] { "auto", "always", "avoid" }, "always", "column", true),
        new("-webkit-column-break-inside", "break-inside", CssPropertyId.WebkitColumnBreakInside,
            new[] { "auto", "avoid" }, null, null, true),
    };

    /// <summary>The modern name the block carries a legacy declaration under, or null when the name
    /// is not one of the three.</summary>
    public static string? ModernName(string name) => Find(name)?.Modern;

    /// <summary>Whether a name is one of the three, so that its value is judged by the legacy set and
    /// not by the (larger) set of the property it aliases.</summary>
    public static bool IsLegacyName(string name) => Find(name) != null;

    /// <summary>The value set a legacy id takes — its grammar, which is smaller than the modern
    /// property's own.</summary>
    public static string[]? ValuesOf(CssPropertyId id)
    {
        foreach (var entry in Entries)
            if (entry.LegacyId == id) return entry.Values;
        return null;
    }

    /// <summary>The value a legacy name is stored as: its one folded keyword in the modern spelling,
    /// any other word of its set unchanged, and null for a value the legacy name does not take.</summary>
    public static string? StoredValue(string name, string value)
    {
        var entry = Find(name);
        if (entry == null) return null;
        var word = value.Trim().ToLowerInvariant();
        if (entry.FoldFrom != null && word == entry.FoldFrom) return entry.FoldTo;
        return entry.Values.Contains(word) ? word : null;
    }

    /// <summary>The value a legacy name reads back from a block carrying the modern property: the
    /// folded keyword in its own spelling, a keyword the two names share unchanged, a CSS-wide
    /// keyword passed through, and null when the legacy name has no word for it. This is the
    /// CSSOM's specified read, and an alias the engine keeps no serialisation for answers nothing
    /// here (see <see cref="Entry.EchoesNothing"/>).</summary>
    public static string? SpecifiedValue(string name, string stored)
    {
        var entry = Find(name);
        if (entry == null) return null;
        var word = stored.Trim().ToLowerInvariant();
        if (Resolver.CssPropertyTraits.IsCssWideKeyword(word)) return word;
        return entry.EchoesNothing ? null : LegacyWord(entry, word);
    }

    /// <summary>The value a legacy name's computed style answers: the same spelling the specified
    /// read uses, and the same empty answer for a value the name has no word for — but an alias
    /// that serialises nothing still computes, because the reference engine reads
    /// '-webkit-column-break-before: avoid' back as 'avoid' (measured).</summary>
    public static string? ComputedValue(string name, string? canonical)
    {
        var entry = Find(name);
        if (entry == null) return null;
        var word = (canonical ?? "auto").Trim().ToLowerInvariant();
        return Resolver.CssPropertyTraits.IsCssWideKeyword(word)
            ? word : LegacyWord(entry, word);
    }

    /// <summary>The word a legacy name spells for a value its modern property carries, or null for
    /// one it has no word for.</summary>
    private static string? LegacyWord(Entry entry, string word)
    {
        if (entry.FoldTo != null && word == entry.FoldTo) return entry.FoldFrom;
        return word != entry.FoldFrom && entry.Values.Contains(word) ? word : null;
    }

    private static Entry? Find(string name)
    {
        foreach (var entry in Entries)
            if (name.Equals(entry.Legacy, StringComparison.OrdinalIgnoreCase)) return entry;
        return null;
    }
}
