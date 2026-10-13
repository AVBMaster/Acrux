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
        string? FoldTo);

    /// <summary>The value set each legacy name takes, measured one keyword at a time: 'page', 'recto'
    /// and 'avoid-column' belong to the modern name alone, and 'page-break-inside: avoid-column' is
    /// dropped while 'break-inside: avoid-column' stands.</summary>
    private static readonly Entry[] Entries =
    {
        new("page-break-before", "break-before", CssPropertyId.PageBreakBefore,
            new[] { "auto", "always", "avoid", "left", "right" }, "always", "page"),
        new("page-break-after", "break-after", CssPropertyId.PageBreakAfter,
            new[] { "auto", "always", "avoid", "left", "right" }, "always", "page"),
        new("page-break-inside", "break-inside", CssPropertyId.PageBreakInside,
            new[] { "auto", "avoid" }, null, null),
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
    /// keyword passed through, and null when the legacy name has no word for it.</summary>
    public static string? SpecifiedValue(string name, string stored)
    {
        var entry = Find(name);
        if (entry == null) return null;
        var word = stored.Trim().ToLowerInvariant();
        if (Resolver.CssPropertyTraits.IsCssWideKeyword(word)) return word;
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
