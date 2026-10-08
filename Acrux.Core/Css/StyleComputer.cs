using Acrux.Core.Dom;
using Acrux.Core.Css.Cascade;
using Acrux.Core.Css.Resolver;
using Acrux.Core.Css.Rules;
using CascadeOrigin = Acrux.Core.Css.Resolver.CascadeOrigin;

namespace Acrux.Core.Css;

/// <summary>
/// StyleComputer - public API for computing styles on a document.
/// Delegates to the Prism <see cref="StyleResolver"/> for the actual cascade
/// resolution. Retains the submitted sheets (in both the token model and the
/// legacy string model) so <see cref="HasHoverRules"/> stays a cheap string scan.
/// </summary>
public class StyleComputer
{
    private readonly List<Stylesheet> _stylesheets = new();
    private StyleSheetContents? _uaSheet;
    private readonly List<AuthorSheet> _authorSheets = new();

    /// <summary>An author stylesheet, and — for one that came from a <style> or <link>
    /// element — that element plus the text its sheet was parsed from. The element is what
    /// lets a later recompute find the sheet again instead of adding a second copy of it
    /// (see <see cref="SyncStyleElements"/>).</summary>
    private sealed class AuthorSheet
    {
        public StyleSheetContents Contents = null!;
        public Stylesheet Legacy = null!;
        public Element? Owner;
        public string? OwnerText;
        /// <summary>The <c>media</c> attribute the sheet's own media list was last seeded from,
        /// kept so that an attribute the page has not touched does not overwrite a list the page
        /// wrote through <c>sheet.media</c>.</summary>
        public string? OwnerMedia;
        /// <summary>The value <c>Element.MediaAttributeWrites</c> had at that seeding. A sheet
        /// re-reads its medium on every write of the attribute, including one that leaves the same
        /// text in it, which the text alone cannot tell from a page that never touched it.</summary>
        public int OwnerMediaWrites;
    }

    /// <summary>The UA sheet as token rules, for read-only inspection (DevTools matched
    /// rules). Same object the cascade resolved from; callers must not mutate it.</summary>
    public StyleSheetContents? UaSheet => _uaSheet;

    /// <summary>Author sheets in insertion order — the order the cascade applied them. A sheet
    /// whose element is disabled is left out here but keeps its object and its place in the
    /// list, so 'element.sheet' still answers and re-enabling puts the same rules back in the
    /// same cascade position without a re-parse (measured in Edge).</summary>
    public IReadOnlyList<StyleSheetContents> AuthorSheets
    {
        get
        {
            var list = new List<StyleSheetContents>(_authorSheets.Count);
            foreach (var sheet in _authorSheets)
            {
                if (sheet.Owner != null && sheet.Owner.SheetDisabled) continue;
                list.Add(sheet.Contents);
            }
            return list;
        }
    }

    public void AddStylesheet(Stylesheet stylesheet, CascadeOrigin origin = CascadeOrigin.Author, Element? owner = null)
    {
        _stylesheets.Add(stylesheet);

        // Prefer the token-parsed sheet attached by CssParser.Parse so the modern
        // pipeline never re-parses the CSS text. The legacy model is retained only
        // for the string scans (HasHoverRules).
        var modern = stylesheet.ModernContents;
        if (modern == null) return;
        if (origin == CascadeOrigin.UserAgent)
            _uaSheet = modern;
        else
        {
            var sheet = new AuthorSheet
            {
                Contents = modern,
                Legacy = stylesheet,
                Owner = owner,
                OwnerText = owner?.TextContent,
            };
            SeedMedia(sheet);
            _authorSheets.Add(sheet);
            if (owner != null) owner.AssociatedStyleSheet = modern;
        }
    }

    /// <summary>Seeding a sheet's media list from its element's attribute (HTML §4.8.6): the
    /// attribute is the list's first and only outside source, and the text the cascade compares is
    /// the one the serialiser prints. The write count is remembered with it so that a later sync
    /// tells an attribute the page touched from one it did not.</summary>
    private static void SeedMedia(AuthorSheet sheet)
    {
        var element = sheet.Owner;
        if (element == null) return;
        sheet.OwnerMedia = element.GetAttribute("media");
        sheet.OwnerMediaWrites = element.MediaAttributeWrites;
        sheet.Contents.MediaText = Acrux.Core.Css.MediaFeatureValue.Canonicalize(sheet.OwnerMedia);
    }

    /// <param name="resolutionDppx">The device pixel ratio the page is being rasterised at. It
    /// belongs to the media-query environment and not to the layout: 'resolution' measures the
    /// output device (CSS Media Queries 4 §7.9), so an '@media (min-resolution: …)' rule has to
    /// be filtered against the same number 'window.matchMedia' reports.</param>
    public void ComputeStyles(Document document, float viewportWidth = 1024f, float viewportHeight = 768f, string colorScheme = "light",
        float resolutionDppx = 1f)
    {
        SyncStyleElements(document);
        var resolver = new StyleResolver(uaSheet: _uaSheet);
        resolver.AddStyleSheets(EffectiveAuthorSheets(document));
        resolver.SetViewport(viewportWidth, viewportHeight, colorScheme, resolutionDppx);
        CollectCounterStyles(document);
        resolver.ResolveDocument(document);
    }

    /// <summary>The author sheets the resolver has to walk: the ones the document's elements
    /// contribute, followed by the sheets the page adopted. An adopted sheet beats an element's
    /// sheet with the same selector and a later adoption beats an earlier one (measured), which
    /// is exactly what appending in this order gives; a disabled sheet of either kind is left
    /// out — an element's on its own state, a constructed one on the sheet's flag.</summary>
    public IReadOnlyList<StyleSheetContents> EffectiveAuthorSheets(Document document)
    {
        var adopted = document.AdoptedStyleSheets;
        if (adopted.Count == 0) return AuthorSheets;
        var list = new List<StyleSheetContents>(AuthorSheets);
        foreach (var sheet in adopted)
            if (!sheet.Disabled) list.Add(sheet);
        return list;
    }

    /// <summary>
    /// Bring the document's own <style> elements into the author-sheet list.
    ///
    /// A style element contributes its sheet for as long as it is in the document and
    /// carries its text, so a script that appends one — or rewrites it — has to be visible
    /// on the next recompute. The load-time pass cannot promise that: it runs once, before
    /// any script has executed. Sheets are matched by their element, so an unchanged
    /// element is not re-parsed, an edited one is re-parsed in place (keeping its position
    /// in the list, hence its cascade order) and one that left the document takes its sheet
    /// with it. A <style> added after load may reference <c>@import</c> sheets; those are
    /// not fetched here, because fetching is an async step the document loader owns.
    /// </summary>
    private void SyncStyleElements(Document document)
    {
        var root = document.DocumentElement;
        if (root == null) return;

        var inDocument = new HashSet<Element>();
        var styleElements = new List<Element>();
        var stack = new Stack<Element>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            inDocument.Add(element);
            if (string.Equals(element.TagName, "STYLE", StringComparison.OrdinalIgnoreCase))
                styleElements.Add(element);
            var children = element.Children;
            for (int i = children.Count - 1; i >= 0; i--)
                if (children[i] is Element child)
                    stack.Push(child);
        }

        foreach (var element in styleElements)
        {
            var text = element.TextContent;
            var existing = _authorSheets.FirstOrDefault(s => ReferenceEquals(s.Owner, element));
            // A 'disabled' element keeps its sheet object — 'element.sheet' still answers and
            // re-enabling costs no re-parse — and is left out of the cascade by the projection
            // the resolver reads (see AuthorSheets).

            if (existing == null)
            {
                var parsed = ParseStyleElement(text);
                // An element owns a sheet even when the text parsed to nothing: the reference
                // engine hands out a sheet with zero rules for an empty or rejected 'style'
                // (measured), and 'document.styleSheets' counts it.
                var contents = parsed?.ModernContents ?? new StyleSheetContents();
                if (parsed != null) _stylesheets.Add(parsed);
                var added = new AuthorSheet
                {
                    Contents = contents,
                    Legacy = parsed ?? new Stylesheet(),
                    Owner = element,
                    OwnerText = text,
                };
                SeedMedia(added);
                _authorSheets.Add(added);
                element.AssociatedStyleSheet = contents;
                continue;
            }

            // A 'media' attribute the page wrote re-seeds the sheet's own list — every write of
            // it does, even one that leaves the same text, because the attribute is what the list
            // mirrors. One the page has not written does not, which is what lets a list it changed
            // through 'sheet.media' outlive the recompute that changed it (HTML §4.8.6).
            var media = element.GetAttribute("media");
            if (element.MediaAttributeWrites != existing.OwnerMediaWrites
                || !string.Equals(existing.OwnerMedia, media, StringComparison.Ordinal))
                SeedMedia(existing);

            if (string.Equals(existing.OwnerText, text, StringComparison.Ordinal)) continue;
            var reparsed = ParseStyleElement(text);
            if (reparsed == null) continue;
            _stylesheets.Remove(existing.Legacy);
            _stylesheets.Add(reparsed);
            existing.Legacy = reparsed;
            existing.OwnerText = text;
            // The cascade reads the sheet object it was handed at parse time, so a re-parse
            // has to replace it in place: the element keeps pointing at the same list the
            // resolver walks, and 'element.sheet' never becomes a stale second copy. The
            // script-facing view moves over with it for the same reason — the page holds one
            // sheet object across a rewrite of the text.
            var previousView = existing.Contents.CssomView;
            existing.Contents = reparsed.ModernContents ?? new StyleSheetContents();
            existing.Contents.CssomView ??= previousView;
            element.AssociatedStyleSheet = existing.Contents;
            // Replacing the sheet replaces its list too, which is what the reference engine does
            // with a 'text' rewrite: the attribute is read again and the used value is its own.
            SeedMedia(existing);
        }

        foreach (var stale in _authorSheets.Where(s => s.Owner != null && !inDocument.Contains(s.Owner)).ToList())
        {
            stale.Owner!.AssociatedStyleSheet = null;
            RemoveAuthorSheet(stale);
        }
    }

    private void RemoveAuthorSheet(AuthorSheet sheet)
    {
        _authorSheets.Remove(sheet);
        _stylesheets.Remove(sheet.Legacy);
    }

    private static Stylesheet? ParseStyleElement(string? cssText)
    {
        if (string.IsNullOrWhiteSpace(cssText)) return null;
        try
        {
            return new CssParser().Parse(cssText);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Rebuilds the document's @counter-style registry. The rules are global
    /// rather than selector-matched, so they are collected once per recompute instead of
    /// during the cascade; markers and counter() resolve the names later, which lets a
    /// style declared in a later sheet apply to an element resolved earlier.</summary>
    private void CollectCounterStyles(Document document)
    {
        var registry = document.CounterStyles;
        registry.Clear();

        var rules = new List<StyleRuleCounterStyle>();
        if (_uaSheet != null) CollectCounterStyles(_uaSheet.ChildRules, rules);
        foreach (var sheet in EffectiveAuthorSheets(document))
            CollectCounterStyles(sheet.ChildRules, rules);

        foreach (var rule in rules)
            registry.Register(rule);
        registry.Finish();
    }

    private static void CollectCounterStyles(List<StyleRuleBase> rules, List<StyleRuleCounterStyle> into)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case StyleRuleCounterStyle counterStyle:
                    into.Add(counterStyle);
                    break;
                case StyleRuleGroup group:
                    CollectCounterStyles(group.ChildRules, into);
                    break;
            }
        }
    }

    /// <summary>
    /// Every <c>@keyframes</c> rule registered on this computer, in cascade
    /// order. The animation engine samples these; style resolution deliberately
    /// does not, so an animation always has a clean underlying value to blend
    /// from.
    /// </summary>
    public List<StyleRuleKeyframes> CollectKeyframeRules(Document? document = null)
    {
        var rules = new List<StyleRuleKeyframes>();
        if (_uaSheet != null) CollectKeyframes(_uaSheet.ChildRules, rules);
        // With the document in hand the adopted sheets are included: a @keyframes a page
        // installed through a constructed sheet animates just like one in a <style>.
        var sheets = document != null ? EffectiveAuthorSheets(document) : AuthorSheets;
        foreach (var sheet in sheets)
            CollectKeyframes(sheet.ChildRules, rules);
        return rules;
    }

    private static void CollectKeyframes(List<StyleRuleBase> rules, List<StyleRuleKeyframes> into)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case StyleRuleKeyframes keyframes:
                    into.Add(keyframes);
                    break;
                case StyleRuleGroup group:
                    CollectKeyframes(group.ChildRules, into);
                    break;
            }
        }
    }

    /// <summary>
    /// True when any registered stylesheet contains a <c>:hover</c> selector.
    /// Cheap string scan (selector text); the host uses it to decide whether a
    /// mouse hover change can affect computed styles at all — when no :hover rule
    /// exists, hovering needs no style recompute / relayout.
    /// </summary>
    public bool HasHoverRules()
    {
        foreach (var sheet in _stylesheets)
        {
            if (SheetHasHoverRules(sheet)) return true;
        }
        return false;
    }

    private static bool SheetHasHoverRules(Stylesheet sheet)
    {
        if (RulesHaveHover(sheet.Rules)) return true;
        foreach (var m in sheet.MediaRules)
            if (RulesHaveHover(m.Rules)) return true;
        foreach (var s in sheet.SupportsRules)
            if (RulesHaveHover(s.Rules)) return true;
        foreach (var l in sheet.LayerRules)
            if (RulesHaveHover(l.Rules)) return true;
        return false;
    }

    private static bool RulesHaveHover(List<CssRule> rules)
    {
        foreach (var r in rules)
        {
            if (r.Selector.Contains(":hover", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Parse inline style string into property dictionary.
    /// </summary>
    public Dictionary<string, string> ParseInlineStyle(string styleText)
    {
        var parser = new CssParser();
        return parser.ParseInlineStyle(styleText);
    }
}