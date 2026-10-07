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

    public string SelectorText =>
        string.Join(", ", Selectors.Select(s => s.ToString()));

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
    public string? MediaCondition { get; set; }
    public StyleSheetContents? ImportedSheet { get; set; }

    public StyleRuleImport() : base(RuleType.Import) { }

    public override StyleRuleBase Copy()
    {
        return new StyleRuleImport
        {
            Url = Url,
            LayerNameStr = LayerNameStr,
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

    public StyleRuleProperty() : base(RuleType.Property) { }

    public override StyleRuleBase Copy() =>
        new StyleRuleProperty { Name = Name, Properties = Properties.MutableCopy(),
            LayerName = new List<string>(LayerName) };
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
    public string ScopeRoot { get; set; } = "";
    public string ScopeLimit { get; set; } = "";

    public StyleRuleScope() : base(RuleType.Scope) { }

    public override StyleRuleBase Copy()
    {
        var copy = new StyleRuleScope { ScopeRoot = ScopeRoot, ScopeLimit = ScopeLimit };
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

    public StyleRuleViewTransition() : base(RuleType.ViewTransition) { }

    public override StyleRuleBase Copy() =>
        new StyleRuleViewTransition { Properties = Properties.MutableCopy() };
}

public class StyleRulePositionTry : StyleRuleBase
{
    public string Name { get; set; } = "";
    public CssPropertyValueSet Properties { get; set; } = new();

    public StyleRulePositionTry() : base(RuleType.PositionTry) { }

    public override StyleRuleBase Copy() =>
        new StyleRulePositionTry { Name = Name, Properties = Properties.MutableCopy() };
}

/// <summary>Represents a CSS StyleSheet's parsed contents, mirroring Blink's StyleSheetContents.</summary>
public class StyleSheetContents
{
    public string? OriginalUrl { get; set; }
    public List<StyleRuleImport> ImportRules { get; } = new();
    public List<StyleRuleNamespace> NamespaceRules { get; } = new();
    public List<StyleRuleBase> ChildRules { get; } = new();
    public CssParserMode ParserMode { get; set; } = CssParserMode.HTMLStandard;

    public bool HasFontFaceRule { get; set; }
    public bool HasMediaQueries { get; set; }

    /// <summary>The CSSOM "disabled" flag of the sheet itself. A sheet an element owns keeps
    /// its state on the element (HTML puts <c>disabled</c> there); this is the one a
    /// constructed sheet carries, and the cascade leaves either of them out.</summary>
    public bool Disabled { get; set; }

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