using Acrux.Core.Css.Matcher;
using Acrux.Core.Css.Properties;
using Acrux.Core.Css.Rules;
using Acrux.Core.Css.Tokenizer;
using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// Entry point for parsing CSS text into the legacy <see cref="Stylesheet"/> model.
/// The heavy lifting is delegated to the token-based <see cref="CssParserImpl"/> so
/// parsing is robust against strings, comments, escapes and nested at-rules.
/// </summary>
public class CssParser
{
    public Stylesheet Parse(string cssText)
    {
        var stylesheet = new Stylesheet();
        if (string.IsNullOrEmpty(cssText)) return stylesheet;

        var contents = CssParserImpl.ParseStyleSheet(cssText, CssParserContext.Default());
        stylesheet.ModernContents = contents;
        ConvertRules(contents.ChildRules, stylesheet);
        return stylesheet;
    }

    /// <summary>Converts token-parsed rules into the legacy model consumed by the cascade.</summary>
    private static void ConvertRules(IReadOnlyList<StyleRuleBase> source, Stylesheet target)
    {
        foreach (var rule in source)
        {
            switch (rule)
            {
                case StyleRule styleRule:
                    ConvertStyleRule(styleRule, target.Rules);
                    break;
                case StyleRuleMedia media:
                {
                    var mediaRule = new MediaRule { Condition = media.ConditionText };
                    ConvertGroupChildren(media.ChildRules, mediaRule);
                    target.MediaRules.Add(mediaRule);
                    break;
                }
                case StyleRuleSupports supports:
                {
                    var supportsRule = new SupportsRule { Condition = supports.ConditionText };
                    ConvertGroupChildren(supports.ChildRules, supportsRule);
                    target.SupportsRules.Add(supportsRule);
                    break;
                }
                case StyleRuleLayerBlock layer:
                {
                    var layerRule = new LayerRule { Name = layer.LayerName.Count > 0 ? string.Join(".", layer.LayerName) : null };
                    ConvertGroupChildren(layer.ChildRules, layerRule);
                    target.LayerRules.Add(layerRule);
                    break;
                }
                case StyleRuleContainer container:
                {
                    var containerRule = new ContainerRule { Condition = container.ConditionText };
                    ConvertGroupChildren(container.ChildRules, containerRule);
                    target.ContainerRules.Add(containerRule);
                    break;
                }
                case StyleRuleScope scope:
                {
                    var scopeRule = new ScopeRule
                    {
                        ScopeRoot = scope.ScopeRoot,
                        ScopeLimit = scope.ScopeLimit,
                    };
                    ConvertGroupChildren(scope.ChildRules, scopeRule);
                    target.ScopeRules.Add(scopeRule);
                    break;
                }
                case StyleRuleStartingStyle starting:
                {
                    var startingRule = new StartingStyleRule();
                    ConvertGroupChildren(starting.ChildRules, startingRule);
                    target.StartingStyleRules.Add(startingRule);
                    break;
                }
                case StyleRuleFontFace fontFace:
                    target.FontFaceRules.Add(new FontFaceRule
                    {
                        Properties = ConvertPropertySet(fontFace.Properties)
                    });
                    break;
                case StyleRuleImport import:
                    target.ImportRules.Add(new ImportRule
                    {
                        Url = import.Url,
                        MediaCondition = import.MediaCondition,
                    });
                    break;
                case StyleRuleKeyframes keyframes:
                {
                    var kfRule = new KeyframesRule { Name = keyframes.Name };
                    foreach (var kf in keyframes.Keyframes)
                    {
                        kfRule.Keyframes.Add(new KeyframeBlock
                        {
                            Selector = kf.Key,
                            Properties = ConvertPropertySet(kf.Properties),
                        });
                    }
                    target.KeyframesRules.Add(kfRule);
                    break;
                }
                case StyleRuleProperty propertyRule:
                    target.PropertyRules.Add(new PropertyRule { Name = propertyRule.Name });
                    break;
                case StyleRuleCounterStyle counterStyle:
                    target.CounterStyleRules.Add(new CounterStyleRule { Name = counterStyle.Name });
                    break;
                case StyleRuleNamespace ns:
                    target.NamespaceRules.Add(new NamespaceRule { Prefix = ns.Prefix, Uri = ns.NamespaceUri });
                    break;
            }
        }
    }

    private static void ConvertStyleRule(StyleRule styleRule, List<CssRule> target)
    {
        if (styleRule.Selectors.Count == 0) return;
        var props = ConvertPropertySet(styleRule.Properties);

        foreach (var selector in styleRule.Selectors)
        {
            selector.ComputeSpecificity();
            var rule = new CssRule
            {
                Selector = selector.ToComplexText(),
                Specificity = (selector.SpecificityA, selector.SpecificityB, selector.SpecificityC, 0),
                Properties = props,
                ImportantProperties = styleRule.Properties.PropertyCount > 0
                    ? CollectImportant(styleRule.Properties)
                    : new HashSet<string>(),
                OriginalSelectorText = styleRule.OriginalSelectorText,
            };
            target.Add(rule);
        }
    }

    private static HashSet<string> CollectImportant(CssPropertyValueSet set)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in set.Properties)
            if (prop.IsImportant)
                result.Add(prop.Name.ToCssString());
        return result;
    }

    private static Dictionary<string, string> ConvertPropertySet(CssPropertyValueSet set)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in set.Properties)
            dict[prop.Name.ToCssString()] = prop.Value.CssText();
        return dict;
    }

    private static void ConvertGroupChildren(IReadOnlyList<StyleRuleBase> children, CssAtRuleGroup target)
    {
        foreach (var child in children)
        {
            if (child is StyleRule sr)
                ConvertStyleRule(sr, target.Rules);
            else if (child is StyleRuleMedia m)
            {
                var sub = new MediaRule { Condition = m.ConditionText };
                ConvertGroupChildren(m.ChildRules, sub);
                target.SubMediaRules.Add(sub);
            }
            else if (child is StyleRuleSupports s)
            {
                var sub = new SupportsRule { Condition = s.ConditionText };
                ConvertGroupChildren(s.ChildRules, sub);
                target.SubSupportsRules.Add(sub);
            }
            else if (child is StyleRuleLayerBlock l)
            {
                var sub = new LayerRule { Name = l.LayerName.Count > 0 ? string.Join(".", l.LayerName) : null };
                ConvertGroupChildren(l.ChildRules, sub);
                target.SubLayerRules.Add(sub);
            }
            else if (child is StyleRuleContainer c)
            {
                var sub = new ContainerRule { Condition = c.ConditionText };
                ConvertGroupChildren(c.ChildRules, sub);
                target.SubContainerRules.Add(sub);
            }
            else if (child is StyleRuleScope sc)
            {
                var sub = new ScopeRule { ScopeRoot = sc.ScopeRoot, ScopeLimit = sc.ScopeLimit };
                ConvertGroupChildren(sc.ChildRules, sub);
                target.SubScopeRules.Add(sub);
            }
            else if (child is StyleRuleStartingStyle st)
            {
                var sub = new StartingStyleRule();
                ConvertGroupChildren(st.ChildRules, sub);
                target.SubStartingStyleRules.Add(sub);
            }
        }
    }

    /// <summary>
    /// Parse an inline style attribute value into a property dictionary.
    /// Uses the tokenizer so values containing ';' inside strings or functions
    /// (e.g. <c>content: "a;b"</c>) are handled correctly.
    /// </summary>
    public Dictionary<string, string> ParseInlineStyle(string styleText)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(styleText)) return result;

        var set = CssParserImpl.ParseDeclarationBlock(styleText, CssParserContext.Default());
        foreach (var prop in set.Properties)
        {
            if (prop.Value != null)
                result[prop.Name.ToCssString()] = prop.Value.CssText();
        }
        return result;
    }

    /// <summary>
    /// Parses a declaration block body into property / importance pairs.
    /// String- and function-aware so values such as <c>content: "a;b"</c> survive.
    /// </summary>
    public (Dictionary<string, string> properties, HashSet<string> importantProps) ParsePropertiesWithImportance(string body)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var important = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var set = CssParserImpl.ParseDeclarationBlock(body, CssParserContext.Default());
        foreach (var prop in set.Properties)
        {
            if (prop.Value == null) continue;
            var name = prop.Name.ToCssString();
            props[name] = prop.Value.CssText();
            if (prop.IsImportant)
                important.Add(name);
        }
        return (props, important);
    }
}

/// <summary>Base class for CSS rules parsed from a stylesheet.</summary>
public abstract class CssAtRule
{
    public string? MediaCondition { get; set; }
}

/// <summary>Base for group rules that own a nested rule list.</summary>
public abstract class CssAtRuleGroup : CssAtRule
{
    public List<CssRule> Rules { get; } = new();
    public List<MediaRule> SubMediaRules { get; } = new();
    public List<SupportsRule> SubSupportsRules { get; } = new();
    public List<LayerRule> SubLayerRules { get; } = new();
    public List<ContainerRule> SubContainerRules { get; } = new();
    public List<ScopeRule> SubScopeRules { get; } = new();
    public List<StartingStyleRule> SubStartingStyleRules { get; } = new();
}

public class MediaRule : CssAtRuleGroup
{
    public string Condition { get; set; } = "";
}

public class ContainerRule : CssAtRuleGroup
{
    public string Condition { get; set; } = "";
}

public class SupportsRule : CssAtRuleGroup
{
    public string Condition { get; set; } = "";
}

public class LayerRule : CssAtRuleGroup
{
    public string? Name { get; set; }
}

public class ScopeRule : CssAtRuleGroup
{
    public string ScopeRoot { get; set; } = "";
    public string ScopeLimit { get; set; } = "";
}

public class StartingStyleRule : CssAtRuleGroup
{
}

public class FontFaceRule : CssAtRule
{
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ImportRule : CssAtRule
{
    public string Url { get; set; } = "";
}

public class KeyframesRule : CssAtRule
{
    public string Name { get; set; } = "";
    public List<KeyframeBlock> Keyframes { get; set; } = new();
}

public class KeyframeBlock
{
    public string Selector { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class PropertyRule : CssAtRule
{
    public string Name { get; set; } = "";
}

public class CounterStyleRule : CssAtRule
{
    public string Name { get; set; } = "";
}

public class NamespaceRule : CssAtRule
{
    public string? Prefix { get; set; }
    public string Uri { get; set; } = "";
}

public class Stylesheet
{
    /// <summary>
    /// The token-parsed model backing this legacy sheet. Set by
    /// <see cref="CssParser.Parse"/> so the modern pipeline can reuse the same
    /// parse result instead of re-tokenizing the CSS text.
    /// </summary>
    public StyleSheetContents? ModernContents { get; set; }

    public List<CssRule> Rules { get; } = new();
    public List<MediaRule> MediaRules { get; } = new();
    public List<ContainerRule> ContainerRules { get; } = new();
    public List<FontFaceRule> FontFaceRules { get; } = new();
    public List<ImportRule> ImportRules { get; } = new();
    public List<KeyframesRule> KeyframesRules { get; } = new();
    public List<SupportsRule> SupportsRules { get; } = new();
    public List<LayerRule> LayerRules { get; } = new();
    public List<ScopeRule> ScopeRules { get; } = new();
    public List<StartingStyleRule> StartingStyleRules { get; } = new();
    public List<PropertyRule> PropertyRules { get; } = new();
    public List<CounterStyleRule> CounterStyleRules { get; } = new();
    public List<NamespaceRule> NamespaceRules { get; } = new();

    public void AddRule(CssRule rule) => Rules.Add(rule);
}

public class CssRule
{
    public string Selector { get; set; } = string.Empty;
    public (int a, int b, int c, int d) Specificity { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ImportantProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string OriginalSelectorText { get; set; } = "";

    public bool IsPropertyImportant(string prop) => ImportantProperties.Contains(prop);
}
