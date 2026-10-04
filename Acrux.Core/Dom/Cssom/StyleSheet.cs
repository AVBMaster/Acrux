using System.Collections;

namespace Acrux.Core.Dom.Cssom;

public class StyleSheet
{
    public string Type { get; } = "text/css";
    public string? Href { get; }
    public Node? OwnerNode { get; }
    public StyleSheet? ParentStyleSheet { get; }
    public string? Title { get; }
    public MediaList Media { get; } = new();
    public bool Disabled { get; set; }

    public StyleSheet(string? href = null, Node? ownerNode = null, StyleSheet? parentStyleSheet = null, string? title = null)
    {
        Href = href;
        OwnerNode = ownerNode;
        ParentStyleSheet = parentStyleSheet;
        Title = title;
    }
}

public class CSSStyleSheet : StyleSheet
{
    public CSSRule? OwnerRule { get; }
    public CSSRuleList CssRules { get; } = new();
    public CSSRuleList Rules => CssRules;
    public string? DefaultNamespace { get; set; }

    public CSSStyleSheet(CSSRule? ownerRule = null, string? href = null, Node? ownerNode = null,
        StyleSheet? parentStyleSheet = null, string? title = null)
        : base(href, ownerNode, parentStyleSheet, title)
    {
        OwnerRule = ownerRule;
    }

    public ulong InsertRule(string rule, ulong index = 0)
    {
        var parsed = CSSRuleParser.Parse(rule);
        if (index > (ulong)CssRules.Length)
            throw new DOMException("Index out of bounds", "IndexSizeError");
        CssRules.Insert((int)index, parsed);
        return index;
    }

    public void DeleteRule(ulong index)
    {
        if (index >= (ulong)CssRules.Length)
            throw new DOMException("Index out of bounds", "IndexSizeError");
        CssRules.RemoveAt((int)index);
    }

    public void ReplaceSync(string text)
    {
        CssRules.Clear();
        var rules = CSSRuleParser.ParseSheet(text);
        foreach (var rule in rules)
            CssRules.Add(rule);
    }

    public async Task<CSSStyleSheet> Replace(string text)
    {
        ReplaceSync(text);
        return this;
    }
}

public class CSSRuleList : IReadOnlyList<CSSRule>
{
    private readonly List<CSSRule> _rules = new();

    public int Length => _rules.Count;
    public int Count => _rules.Count;
    public CSSRule this[int index] => index >= 0 && index < _rules.Count ? _rules[index]! : null!;

    public void Add(CSSRule rule) => _rules.Add(rule);
    public void Insert(int index, CSSRule rule) => _rules.Insert(index, rule);
    public void RemoveAt(int index) => _rules.RemoveAt(index);
    public void Clear() => _rules.Clear();

    public IEnumerator<CSSRule> GetEnumerator() => _rules.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _rules.GetEnumerator();
}

public abstract class CSSRule
{
    public virtual string CssText { get; set; } = "";
    public CSSRule? ParentRule { get; }
    public CSSStyleSheet? ParentStyleSheet { get; }
    public abstract ushort Type { get; }

    public const ushort STYLE_RULE = 1;
    public const ushort CHARSET_RULE = 2;
    public const ushort IMPORT_RULE = 3;
    public const ushort MEDIA_RULE = 4;
    public const ushort FONT_FACE_RULE = 5;
    public const ushort PAGE_RULE = 6;
    public const ushort KEYFRAMES_RULE = 7;
    public const ushort KEYFRAME_RULE = 8;
    public const ushort MARGIN_RULE = 9;
    public const ushort NAMESPACE_RULE = 10;
    public const ushort COUNTER_STYLE_RULE = 11;
    public const ushort SUPPORTS_RULE = 12;
    public const ushort LAYER_BLOCK_RULE = 16;
    public const ushort LAYER_STATEMENT_RULE = 17;
    public const ushort PROPERTY_RULE = 15;
    public const ushort SCOPE_RULE = 18;
    public const ushort CONTAINER_RULE = 19;
    public const ushort STARTING_STYLE_RULE = 20;
    public const ushort NESTING_RULE = 21;

    protected CSSRule(CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
    {
        ParentRule = parentRule;
        ParentStyleSheet = parentStyleSheet;
    }
}

public class CSSStyleRule : CSSRule
{
    public override ushort Type => STYLE_RULE;
    public string SelectorText { get; set; } = "";
    public CSSStyleDeclaration Style { get; } = new();
    public CSSRuleList CssRules { get; } = new();
    public string Name { get; set; } = "";

    public override string CssText
    {
        get => $"{SelectorText} {{ {Style.CssText} }}";
        set => base.CssText = value;
    }

    public CSSStyleRule(CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet) { }
}

public class CSSImportRule : CSSRule
{
    public override ushort Type => IMPORT_RULE;
    public string Href { get; }
    public MediaList Media { get; } = new();
    public CSSStyleSheet StyleSheet { get; }
    public string? LayerName { get; }
    public string? SupportsText { get; }

    public CSSImportRule(string href, MediaList? media = null, CSSStyleSheet? styleSheet = null,
        string? layerName = null, string? supportsText = null,
        CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet)
    {
        Href = href;
        if (media != null) Media = media;
        StyleSheet = styleSheet ?? new CSSStyleSheet(this, href);
        LayerName = layerName;
        SupportsText = supportsText;
    }
}

public class CSSMediaRule : CSSRule
{
    public override ushort Type => MEDIA_RULE;
    public MediaList Media { get; } = new();
    public CSSRuleList CssRules { get; } = new();

    public ulong InsertRule(string rule, ulong index = 0) => 0;
    public void DeleteRule(ulong index) { }

    public CSSMediaRule(CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet) { }
}

public class CSSFontFaceRule : CSSRule
{
    public override ushort Type => FONT_FACE_RULE;
    public CSSStyleDeclaration Style { get; } = new();

    public CSSFontFaceRule(CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet) { }
}

public class CSSKeyframesRule : CSSRule
{
    public override ushort Type => KEYFRAMES_RULE;
    public string Name { get; set; } = "";
    public CSSRuleList CssRules { get; } = new();
    public int Length => CssRules.Length;

    public CSSKeyframeRule? this[int index] => CssRules[index] as CSSKeyframeRule;

    public void AppendRule(string rule)
    {
        var parsed = CSSRuleParser.ParseKeyframe(rule);
        if (parsed != null) CssRules.Add(parsed);
    }

    public void DeleteRule(string key)
    {
        for (int i = CssRules.Length - 1; i >= 0; i--)
        {
            if (CssRules[i] is CSSKeyframeRule kf && kf.KeyText == key)
            {
                CssRules.RemoveAt(i);
                break;
            }
        }
    }

    public CSSKeyframeRule? FindRule(string key)
    {
        for (int i = 0; i < CssRules.Length; i++)
        {
            if (CssRules[i] is CSSKeyframeRule kf && kf.KeyText == key)
                return kf;
        }
        return null;
    }

    public CSSKeyframesRule(CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet) { }
}

public class CSSKeyframeRule : CSSRule
{
    public override ushort Type => KEYFRAME_RULE;
    public string KeyText { get; set; } = "";
    public CSSStyleDeclaration Style { get; } = new();

    public CSSKeyframeRule(CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet) { }
}

public class CSSNamespaceRule : CSSRule
{
    public override ushort Type => NAMESPACE_RULE;
    public string NamespaceUri { get; }
    public string? Prefix { get; }

    public CSSNamespaceRule(string namespaceUri, string? prefix = null,
        CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet)
    {
        NamespaceUri = namespaceUri;
        Prefix = prefix;
    }
}

public class CSSSupportsRule : CSSRule
{
    public override ushort Type => SUPPORTS_RULE;
    public string ConditionText { get; set; } = "";
    public CSSRuleList CssRules { get; } = new();
}

public class CSSLayerBlockRule : CSSRule
{
    public override ushort Type => LAYER_BLOCK_RULE;
    public string Name { get; }
    public CSSRuleList CssRules { get; } = new();

    public CSSLayerBlockRule(string name = "",
        CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet)
    {
        Name = name;
    }
}

public class CSSLayerStatementRule : CSSRule
{
    public override ushort Type => LAYER_STATEMENT_RULE;
    public string[] NameList { get; }

    public CSSLayerStatementRule(string[] nameList,
        CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet)
    {
        NameList = nameList;
    }
}

public class CSSContainerRule : CSSRule
{
    public override ushort Type => CONTAINER_RULE;
    public string ContainerQuery { get; }
    public CSSRuleList CssRules { get; } = new();

    public CSSContainerRule(string containerQuery,
        CSSRule? parentRule = null, CSSStyleSheet? parentStyleSheet = null)
        : base(parentRule, parentStyleSheet)
    {
        ContainerQuery = containerQuery;
    }
}

public class CSSScopeRule : CSSRule
{
    public override ushort Type => SCOPE_RULE;
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public CSSRuleList CssRules { get; } = new();
}

public class CSSStartingStyleRule : CSSRule
{
    public override ushort Type => STARTING_STYLE_RULE;
    public CSSRuleList CssRules { get; } = new();
}

public class CSSNestedDeclarations : CSSRule
{
    public override ushort Type => NESTING_RULE;
    public CSSStyleDeclaration Style { get; } = new();
}

public class CSSStyleDeclaration
{
    private readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);

    public string CssText
    {
        get => string.Join("; ", _properties.Select(kv => $"{kv.Key}: {kv.Value}"));
        set
        {
            _properties.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = part.IndexOf(':');
                if (colon > 0)
                {
                    var prop = part[..colon].Trim();
                    var val = part[(colon + 1)..].Trim();
                    if (!string.IsNullOrEmpty(prop))
                        _properties[prop] = val;
                }
            }
        }
    }

    public int Length => _properties.Count;
    public CSSRule? ParentRule { get; set; }
    public string CssFloat { get => GetPropertyValue("float"); set => SetProperty("float", value); }

    public string GetPropertyValue(string property) =>
        _properties.TryGetValue(property, out var val) ? val : "";

    public string GetPropertyPriority(string property) =>
        _properties.TryGetValue(property, out var val) && val.EndsWith("!important") ? "important" : "";

    public void SetProperty(string property, string value, string priority = "")
    {
        if (priority == "important")
            _properties[property] = value + " !important";
        else
            _properties[property] = value;
    }

    public string RemoveProperty(string property)
    {
        _properties.Remove(property, out var old);
        return old ?? "";
    }

    public string this[int index] => index >= 0 && index < _properties.Count ? _properties.ElementAt(index).Key : null!;

    public string? this[string property]
    {
        get => GetPropertyValue(property);
        set
        {
            if (value != null) SetProperty(property, value);
            else RemoveProperty(property);
        }
    }

    public IReadOnlyDictionary<string, string> Properties => _properties;
}

public class MediaList
{
    private readonly List<string> _media = new();

    public string MediaText
    {
        get => string.Join(", ", _media);
        set
        {
            _media.Clear();
            if (!string.IsNullOrEmpty(value))
                _media.AddRange(value.Split(',').Select(m => m.Trim()));
        }
    }

    public int Length => _media.Count;
    public string this[int index] => index >= 0 && index < _media.Count ? _media[index] : null!;

    public void AppendMedium(string medium)
    {
        if (!_media.Contains(medium))
            _media.Add(medium);
    }

    public void DeleteMedium(string medium) => _media.Remove(medium);
}

public static class CSSRuleParser
{
    public static CSSRule Parse(string cssText)
    {
        cssText = cssText.Trim();

        if (cssText.StartsWith("@import"))
        {
            var href = ExtractUrl(cssText);
            return new CSSImportRule(href ?? "");
        }

        if (cssText.StartsWith("@media"))
        {
            return new CSSMediaRule();
        }

        if (cssText.StartsWith("@keyframes") || cssText.StartsWith("@-webkit-keyframes"))
        {
            return new CSSKeyframesRule { Name = ExtractKeyframesName(cssText) };
        }

        if (cssText.StartsWith("@font-face"))
        {
            return new CSSFontFaceRule();
        }

        if (cssText.StartsWith("@namespace"))
        {
            return new CSSNamespaceRule("");
        }

        if (cssText.StartsWith("@supports"))
        {
            return new CSSSupportsRule();
        }

        if (cssText.StartsWith("@layer"))
        {
            return new CSSLayerBlockRule();
        }

        if (cssText.StartsWith("@container"))
        {
            return new CSSContainerRule("");
        }

        if (cssText.StartsWith("@scope"))
        {
            return new CSSScopeRule();
        }

        if (cssText.StartsWith("@starting-style"))
        {
            return new CSSStartingStyleRule();
        }

        // Default: style rule
        var braceIdx = cssText.IndexOf('{');
        if (braceIdx > 0)
        {
            var selector = cssText[..braceIdx].Trim();
            var styleText = cssText[braceIdx..].Trim('{', '}', ' ');
            var rule = new CSSStyleRule { SelectorText = selector };
            rule.Style.CssText = styleText;
            return rule;
        }

        return new CSSStyleRule();
    }

    public static CSSKeyframeRule? ParseKeyframe(string cssText)
    {
        cssText = cssText.Trim();
        var braceIdx = cssText.IndexOf('{');
        if (braceIdx <= 0) return null;

        var keyText = cssText[..braceIdx].Trim();
        var styleText = cssText[braceIdx..].Trim('{', '}', ' ');
        var rule = new CSSKeyframeRule { KeyText = keyText };
        rule.Style.CssText = styleText;
        return rule;
    }

    public static List<CSSRule> ParseSheet(string cssText)
    {
        var rules = new List<CSSRule>();
        if (string.IsNullOrWhiteSpace(cssText)) return rules;
        return rules;
    }

    private static string? ExtractUrl(string importText)
    {
        var urlMatch = importText.IndexOf("url(", StringComparison.Ordinal);
        if (urlMatch >= 0)
        {
            var start = urlMatch + 4;
            var end = importText.IndexOf(')', start);
            if (end > start) return importText[start..end].Trim('\'', '"', ' ');
        }
        var quoteMatch = importText.IndexOf('\'');
        if (quoteMatch < 0) quoteMatch = importText.IndexOf('"');
        if (quoteMatch >= 0)
        {
            var quote = importText[quoteMatch];
            var start = quoteMatch + 1;
            var end = importText.IndexOf(quote, start);
            if (end > start) return importText[start..end];
        }
        return null;
    }

    private static string ExtractKeyframesName(string cssText)
    {
        var parts = cssText.Split('{')[0].Trim().Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] is "@keyframes" or "@-webkit-keyframes" && i + 1 < parts.Length)
                return parts[i + 1];
        }
        return "";
    }
}
