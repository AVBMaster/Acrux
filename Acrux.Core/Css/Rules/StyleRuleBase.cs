using Acrux.Core.Css.Matcher;
using Acrux.Core.Css.Properties;
using Acrux.Core.Css.Values;
using CssSel = Acrux.Core.Css.Matcher.CssSelector;

namespace Acrux.Core.Css.Rules;

public enum RuleType
{
    Style, Import, Media, FontFace, FontPaletteValues, FontFeatureValues,
    FontFeature, Page, PageMargin, Property, Keyframes, Keyframe,
    LayerBlock, LayerStatement, NestedDeclarations, Namespace,
    Container, CounterStyle, Scope, Supports, StartingStyle,
    ViewTransition, Function, PositionTry
}

/// <summary>Base class for all CSS rules, mirroring Blink's StyleRuleBase.</summary>
public abstract class StyleRuleBase
{
    public RuleType RuleTypeValue { get; }
    public List<string> LayerName { get; set; } = new();

    protected StyleRuleBase(RuleType type)
    {
        RuleTypeValue = type;
    }

    public abstract StyleRuleBase Copy();
}

/// <summary>A regular style rule (selector + properties), mirroring Blink's StyleRule.</summary>
public class StyleRule : StyleRuleBase
{
    public List<CssSel> Selectors { get; } = new();
    public CssPropertyValueSet Properties { get; set; } = new();
    public List<StyleRuleBase> ChildRules { get; } = new();
    public bool IsLazyParsed { get; set; }
    public Func<CssPropertyValueSet>? LazyParser { get; set; }

    /// <summary>The verbatim selector text as written in the stylesheet. Kept so
    /// consumers can inspect the original (vendor-prefixed) spelling, e.g.
    /// "::-webkit-scrollbar" or ":hover".</summary>
    public string OriginalSelectorText { get; set; } = "";

    public StyleRule() : base(RuleType.Style) { }

    /// <summary>The rule's selectors as the grammar reads them, not as the page wrote them: a
    /// selector that was refused is not here at all (the rule is not either), and an any-namespace
    /// prefix disappears on the way out, so '@namespace …; *|div' reads 'div' (measured).</summary>
    public string SelectorText =>
        string.Join(", ", Selectors.Select(s => s.ToComplexText()));

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRule();
        copy.Selectors.AddRange(Selectors);
        copy.Properties = Properties.MutableCopy();
        copy.ChildRules.AddRange(ChildRules.Select(r => r.Copy()));
        copy.LayerName = new List<string>(LayerName);
        return copy;
    }
}

/// <summary>Base class for group rules (@media, @supports, @container, etc.), mirroring Blink's StyleRuleGroup.</summary>
public abstract class StyleRuleGroup : StyleRuleBase
{
    public List<StyleRuleBase> ChildRules { get; } = new();

    protected StyleRuleGroup(RuleType type) : base(type) { }
}

/// <summary>Base class for conditional rules (@media, @supports, @container).</summary>
public abstract class StyleRuleCondition : StyleRuleGroup
{
    public string ConditionText { get; set; } = "";

    protected StyleRuleCondition(RuleType type) : base(type) { }
}

public class StyleRuleMedia : StyleRuleCondition
{
    public StyleRuleMedia() : base(RuleType.Media) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleMedia();
        copy.ConditionText = ConditionText;
        copy.ChildRules.AddRange(ChildRules.Select(r => r.Copy()));
        copy.LayerName = new List<string>(LayerName);
        return copy;
    }
}

public class StyleRuleSupports : StyleRuleCondition
{
    public StyleRuleSupports() : base(RuleType.Supports) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleSupports();
        copy.ConditionText = ConditionText;
        copy.ChildRules.AddRange(ChildRules.Select(r => r.Copy()));
        copy.LayerName = new List<string>(LayerName);
        return copy;
    }
}

public class StyleRuleContainer : StyleRuleCondition
{
    public StyleRuleContainer() : base(RuleType.Container) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleContainer();
        copy.ConditionText = ConditionText;
        copy.ChildRules.AddRange(ChildRules.Select(r => r.Copy()));
        copy.LayerName = new List<string>(LayerName);
        return copy;
    }
}

public class StyleRuleImport : StyleRuleBase
{
    public string Url { get; set; } = "";
    public string? LayerNameStr { get; set; }
    /// <summary>The 'supports()' condition of an '@import' (CSS Conditional Rules 4 §4). It is not
    /// part of the rule's media list — a page that reads 'rule.media' on
    /// <c>@import url(b.css) supports(display: flex)</c> gets an EMPTY list — so it is kept on its
    /// own and only the text after it is a medium (measured).</summary>
    public string? SupportsCondition { get; set; }
    public string? MediaCondition { get; set; }
    public StyleSheetContents? ImportedSheet { get; set; }

    public StyleRuleImport() : base(RuleType.Import) { }

    public override StyleRuleBase Copy()
    {
        return new StyleRuleImport
        {
            Url = Url,
            LayerNameStr = LayerNameStr,
            SupportsCondition = SupportsCondition,
            MediaCondition = MediaCondition,
            ImportedSheet = ImportedSheet,
            LayerName = new List<string>(LayerName)
        };
    }
}

public class StyleRuleFontFace : StyleRuleBase
{
    public CssPropertyValueSet Properties { get; set; } = new();

    public StyleRuleFontFace() : base(RuleType.FontFace) { }

    public override StyleRuleBase Copy()
    {
        return new StyleRuleFontFace
        {
            Properties = Properties.MutableCopy(),
            LayerName = new List<string>(LayerName)
        };
    }
}

public class StyleRuleKeyframes : StyleRuleBase
{
    public string Name { get; set; } = "";
    public List<StyleRuleKeyframe> Keyframes { get; } = new();

    public StyleRuleKeyframes() : base(RuleType.Keyframes) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleKeyframes { Name = Name };
        copy.Keyframes.AddRange(Keyframes.Select(k => (StyleRuleKeyframe)k.Copy()));
        copy.LayerName = new List<string>(LayerName);
        return copy;
    }
}

public class StyleRuleKeyframe : StyleRuleBase
{
    public string Key { get; set; } = ""; // e.g. "0%", "100%", "from", "to"
    public CssPropertyValueSet Properties { get; set; } = new();

    public StyleRuleKeyframe() : base(RuleType.Keyframe) { }

    public override StyleRuleBase Copy()
    {
        return new StyleRuleKeyframe
        {
            Key = Key,
            Properties = Properties.MutableCopy(),
            LayerName = new List<string>(LayerName)
        };
    }
}

public class StyleRulePage : StyleRuleBase
{
    public string SelectorText { get; set; } = "";
    public CssPropertyValueSet Properties { get; set; } = new();
    public List<StyleRulePageMargin> MarginRules { get; } = new();

    public StyleRulePage() : base(RuleType.Page) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRulePage { SelectorText = SelectorText, Properties = Properties.MutableCopy() };
        copy.MarginRules.AddRange(MarginRules.Select(m => (StyleRulePageMargin)m.Copy()));
        copy.LayerName = new List<string>(LayerName);
        return copy;
    }
}

public class StyleRulePageMargin : StyleRuleBase
{
    public string MarginId { get; set; } = "";
    public CssPropertyValueSet Properties { get; set; } = new();

    public StyleRulePageMargin() : base(RuleType.PageMargin) { }

    public override StyleRuleBase Copy() =>
        new StyleRulePageMargin { MarginId = MarginId, Properties = Properties.MutableCopy() };
}

public class StyleRuleProperty : StyleRuleBase
{
    public string Name { get; set; } = "";
    public CssPropertyValueSet Properties { get; set; } = new();

    /// <summary>The block's descriptors as written, keyed by lowercase name — the same arrangement
    /// an '@counter-style' keeps them in, and for the same reason: 'syntax', 'inherits' and
    /// 'initial-value' are not properties, so the declaration path has no id to file them under.
    /// They are what the rule reads back through the CSSOM, in the order the reference engine
    /// prints them (measured: a block written 'syntax / initial-value / inherits' serialises as
    /// 'syntax, inherits, initial-value').</summary>
    public Dictionary<string, string> Descriptors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StyleRuleProperty() : base(RuleType.Property) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleProperty { Name = Name, Properties = Properties.MutableCopy() };
        foreach (var (key, value) in Descriptors)
            copy.Descriptors[key] = value;
        return copy;
    }
}

public class StyleRuleCounterStyle : StyleRuleBase
{
    public string Name { get; set; } = "";
    public CssPropertyValueSet Properties { get; set; } = new();

    /// <summary>The block's descriptors as written, keyed by lowercase name. They are kept
    /// as raw text because a counter-style descriptor is not a property: 'system', 'range'
    /// and friends have no property id, so the declaration path would collapse them all
    /// into one anonymous entry.</summary>
    public Dictionary<string, string> Descriptors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StyleRuleCounterStyle() : base(RuleType.CounterStyle) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleCounterStyle { Name = Name, Properties = Properties.MutableCopy() };
        foreach (var (key, value) in Descriptors)
            copy.Descriptors[key] = value;
        return copy;
    }
}

public class StyleRuleScope : StyleRuleGroup
{
    /// <summary>The prelude as the page wrote it, which is what the rule reads back as
    /// ('@scope (.a) to (.b)' is serialised with its own 'to', measured). The two halves below are
    /// the same text taken apart, for the code that has to match a root.</summary>
    public string ScopePrelude { get; set; } = "";
    public string ScopeRoot { get; set; } = "";
    public string ScopeLimit { get; set; } = "";

    public StyleRuleScope() : base(RuleType.Scope) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleScope
        {
            ScopeRoot = ScopeRoot, ScopeLimit = ScopeLimit, ScopePrelude = ScopePrelude
        };
        copy.ChildRules.AddRange(ChildRules.Select(r => r.Copy()));
        copy.LayerName = new List<string>(LayerName);
        return copy;
    }
}

public class StyleRuleLayerBlock : StyleRuleGroup
{
    public StyleRuleLayerBlock() : base(RuleType.LayerBlock) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleLayerBlock();
        copy.ChildRules.AddRange(ChildRules.Select(r => r.Copy()));
        copy.LayerName = new List<string>(LayerName);
        return copy;
    }
}

public class StyleRuleLayerStatement : StyleRuleBase
{
    public List<string> LayerNames { get; } = new();

    public StyleRuleLayerStatement() : base(RuleType.LayerStatement) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleLayerStatement();
        copy.LayerNames.AddRange(LayerNames);
        return copy;
    }
}

public class StyleRuleNamespace : StyleRuleBase
{
    public string? Prefix { get; set; }
    public string NamespaceUri { get; set; } = "";

    public StyleRuleNamespace() : base(RuleType.Namespace) { }

    public override StyleRuleBase Copy() =>
        new StyleRuleNamespace { Prefix = Prefix, NamespaceUri = NamespaceUri };
}

public class StyleRuleStartingStyle : StyleRuleGroup
{
    public StyleRuleStartingStyle() : base(RuleType.StartingStyle) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleStartingStyle();
        copy.ChildRules.AddRange(ChildRules.Select(r => r.Copy()));
        return copy;
    }
}

public class StyleRuleViewTransition : StyleRuleBase
{
    public CssPropertyValueSet Properties { get; set; } = new();

    /// <summary>The block's descriptors as written, held the way an '@counter-style' holds its own:
    /// 'navigation' has no property id, so the declaration path could not carry it and the rule
    /// would read back an empty block.</summary>
    public Dictionary<string, string> Descriptors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StyleRuleViewTransition() : base(RuleType.ViewTransition) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleViewTransition { Properties = Properties.MutableCopy() };
        foreach (var (key, value) in Descriptors)
            copy.Descriptors[key] = value;
        return copy;
    }
}

public class StyleRulePositionTry : StyleRuleBase
{
    public string Name { get; set; } = "";
    public CssPropertyValueSet Properties { get; set; } = new();

    public StyleRulePositionTry() : base(RuleType.PositionTry) { }

    public override StyleRuleBase Copy() =>
        new StyleRulePositionTry { Name = Name, Properties = Properties.MutableCopy() };
}

/// <summary>An '@font-feature-values' block (CSS Fonts 5 §9.3): it names one family and holds the
/// feature-blocks that give that family's named features their values.</summary>
public class StyleRuleFontFeatureValues : StyleRuleBase
{
    /// <summary>The family the features are defined for, as the page wrote it.</summary>
    public string FamilyName { get; set; } = "";

    public StyleRuleFontFeatureValues() : base(RuleType.FontFeatureValues) { }

    public override StyleRuleBase Copy() =>
        new StyleRuleFontFeatureValues { FamilyName = FamilyName };
}

/// <summary>Represents a CSS StyleSheet's parsed contents, mirroring Blink's StyleSheetContents.</summary>
public class StyleSheetContents
{
    public string? OriginalUrl { get; set; }
    public List<StyleRuleImport> ImportRules { get; } = new();
    public List<StyleRuleNamespace> NamespaceRules { get; } = new();

    /// <summary>The address the sheet's own prefixless '@namespace' gives, and the addresses its
    /// prefixed ones give, first declaration of each winning. A rule that reaches the sheet later —
    /// through <c>insertRule</c>, whose text carries no declarations of its own — takes these: an id
    /// selector inserted into a sheet whose default namespace is a foreign one applies to nothing
    /// (measured, and the same rule in a sheet declaring the document's own namespace applies).</summary>
    public string? DefaultNamespaceUri { get; set; }
    public Dictionary<string, string> NamespacePrefixes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<StyleRuleBase> ChildRules { get; } = new();
    public CssParserMode ParserMode { get; set; } = CssParserMode.HTMLStandard;

    public bool HasFontFaceRule { get; set; }
    public bool HasMediaQueries { get; set; }

    /// <summary>The CSSOM "disabled" flag of the sheet itself. A sheet an element owns keeps
    /// its state on the element (HTML puts <c>disabled</c> there); this is the one a
    /// constructed sheet carries, and the cascade leaves either of them out.</summary>
    public bool Disabled { get; set; }

    /// <summary>The medium this sheet applies to, as its own media list (CSSOM §5.2.1, and HTML
    /// §4.8.6 for the <c>media</c> attribute behind it). An element's attribute seeds it, and a
    /// later change to that attribute re-seeds it; a write through <c>sheet.media</c> does not
    /// touch the attribute, because the used value and the written one are different things
    /// (measured). Empty is no medium at all, which the cascade reads as every medium.</summary>
    public string MediaText { get; set; } = "";

    /// <summary>The script-facing view of this sheet, remembered so that the object a page
    /// holds and the one <c>document.adoptedStyleSheets</c> hands back are the same object
    /// (CSSOM identity), and so that mutating either reaches the rules the cascade walks.
    /// Typed <c>object</c> to keep the CSS layer free of JS-host types.</summary>
    public object? CssomView { get; set; }

    public void AddRule(StyleRuleBase rule)
    {
        ChildRules.Add(rule);
        if (rule is StyleRuleFontFace) HasFontFaceRule = true;
        if (rule is StyleRuleMedia) HasMediaQueries = true;
    }

    public void AddRuleRange(IEnumerable<StyleRuleBase> rules)
    {
        foreach (var rule in rules)
            AddRule(rule);
    }

    public List<StyleRule> GetStyleRules()
    {
        var result = new List<StyleRule>();
        foreach (var rule in ChildRules)
            CollectStyleRules(rule, result);
        return result;
    }

    private static void CollectStyleRules(StyleRuleBase rule, List<StyleRule> result)
    {
        if (rule is StyleRule styleRule)
            result.Add(styleRule);
        if (rule is StyleRuleGroup group)
        {
            foreach (var child in group.ChildRules)
                CollectStyleRules(child, result);
        }
    }

    private static void CollectStyleRules(StyleSheetContents sheet, List<StyleRule> result)
    {
        foreach (var rule in sheet.ChildRules)
            CollectStyleRules(rule, result);
    }
}