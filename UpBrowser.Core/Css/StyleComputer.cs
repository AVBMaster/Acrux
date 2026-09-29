using UpBrowser.Core.Dom;
using UpBrowser.Core.Css.Cascade;
using UpBrowser.Core.Css.Resolver;
using UpBrowser.Core.Css.Rules;
using CascadeOrigin = UpBrowser.Core.Css.Resolver.CascadeOrigin;

namespace UpBrowser.Core.Css;

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
    private readonly List<StyleSheetContents> _authorSheets = new();

    public void AddStylesheet(Stylesheet stylesheet, CascadeOrigin origin = CascadeOrigin.Author)
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
            _authorSheets.Add(modern);
    }

    public void ComputeStyles(Document document, float viewportWidth = 1024f, float viewportHeight = 768f, string colorScheme = "light")
    {
        var resolver = new StyleResolver(uaSheet: _uaSheet);
        resolver.AddStyleSheets(_authorSheets);
        resolver.SetViewport(viewportWidth, viewportHeight, colorScheme);
        resolver.ResolveDocument(document);
    }

    /// <summary>
    /// Every <c>@keyframes</c> rule registered on this computer, in cascade
    /// order. The animation engine samples these; style resolution deliberately
    /// does not, so an animation always has a clean underlying value to blend
    /// from.
    /// </summary>
    public List<StyleRuleKeyframes> CollectKeyframeRules()
    {
        var rules = new List<StyleRuleKeyframes>();
        if (_uaSheet != null) CollectKeyframes(_uaSheet.ChildRules, rules);
        foreach (var sheet in _authorSheets)
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