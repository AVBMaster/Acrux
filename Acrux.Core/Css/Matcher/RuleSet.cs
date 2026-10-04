using Acrux.Core.Css.Rules;
using CssSel = Acrux.Core.Css.Matcher.CssSelector;

namespace Acrux.Core.Css.Matcher;

/// <summary>
/// Stores CSS rules indexed by selector type for fast matching, mirroring Blink's RuleSet.
/// Rules are bucketed by ID, class, tag, attribute, pseudo-class, and universal.
/// </summary>
public class RuleSet
{
    public struct RuleData
    {
        public StyleRule Rule;
        public int SelectorIndex;
        public int Position;
        public uint Specificity;
        public bool IsStartingStyle;

        public RuleData(StyleRule rule, int selectorIndex, int position)
        {
            Rule = rule;
            SelectorIndex = selectorIndex;
            Position = position;
            Specificity = 0;
            IsStartingStyle = false;
        }
    }

    public Dictionary<string, List<RuleData>> IdRules { get; } = new();
    public Dictionary<string, List<RuleData>> ClassRules { get; } = new();
    public Dictionary<string, List<RuleData>> TagRules { get; } = new();
    public Dictionary<string, List<RuleData>> AttrRules { get; } = new();
    public List<RuleData> LinkPseudoClassRules { get; } = new();
    public List<RuleData> FocusPseudoClassRules { get; } = new();
    public List<RuleData> UniversalRules { get; } = new();

    public List<StyleRuleFontFace> FontFaceRules { get; } = new();
    public List<StyleRuleKeyframes> KeyframesRules { get; } = new();
    public List<StyleRuleProperty> PropertyRules { get; } = new();
    public List<StyleRuleCounterStyle> CounterStyleRules { get; } = new();
    public List<StyleRulePositionTry> PositionTryRules { get; } = new();

    private int _rulePosition;

    /// <summary>Adds rules from a StyleSheetContents into this RuleSet.</summary>
    public void AddRulesFromSheet(StyleSheetContents sheet)
    {
        foreach (var rule in sheet.ChildRules)
            AddRule(rule);
    }

    private void AddRule(StyleRuleBase rule)
    {
        if (rule is StyleRule styleRule)
        {
            AddStyleRule(styleRule);
        }
        else if (rule is StyleRuleFontFace fontFace)
        {
            FontFaceRules.Add(fontFace);
        }
        else if (rule is StyleRuleKeyframes keyframes)
        {
            KeyframesRules.Add(keyframes);
        }
        else if (rule is StyleRuleProperty property)
        {
            PropertyRules.Add(property);
        }
        else if (rule is StyleRuleCounterStyle counterStyle)
        {
            CounterStyleRules.Add(counterStyle);
        }
        else if (rule is StyleRulePositionTry positionTry)
        {
            PositionTryRules.Add(positionTry);
        }
        else if (rule is StyleRuleGroup group)
        {
            foreach (var child in group.ChildRules)
                AddRule(child);
        }
    }

    private void AddStyleRule(StyleRule rule)
    {
        for (int i = 0; i < rule.Selectors.Count; i++)
        {
            var selector = rule.Selectors[i];
            var data = new RuleData(rule, i, _rulePosition++);
            FindBestBucketAndAdd(selector, data);
        }
    }

    private void FindBestBucketAndAdd(CssSel selector, RuleData data)
    {
        // Find the most specific bucket for the rightmost (target) selector
        var rightmost = FindRightmost(selector);

        if (rightmost == null)
        {
            UniversalRules.Add(data);
            return;
        }

        // Try to find a bucket: ID > Class > Tag > Attribute > Pseudo > Universal
        var id = FindId(rightmost);
        if (id != null)
        {
            AddToDict(IdRules, id, data);
            return;
        }

        var cls = FindClass(rightmost);
        if (cls != null)
        {
            AddToDict(ClassRules, cls, data);
            return;
        }

        var tag = FindTag(rightmost);
        if (tag != null)
        {
            AddToDict(TagRules, tag, data);
            return;
        }

        if (rightmost.MatchType == CssSelectorMatchType.PseudoClass)
        {
            if (rightmost.PseudoType is CssPseudoType.Link or CssPseudoType.Visited or CssPseudoType.AnyLink)
            {
                LinkPseudoClassRules.Add(data);
                return;
            }
            if (rightmost.PseudoType is CssPseudoType.Focus or CssPseudoType.FocusVisible or CssPseudoType.FocusWithin)
            {
                FocusPseudoClassRules.Add(data);
                return;
            }
        }

        UniversalRules.Add(data);
    }

    private static string? FindId(CssSelector? selector)
    {
        while (selector != null)
        {
            if (selector.MatchType == CssSelectorMatchType.Id && selector.Value != null)
                return selector.Value;
            if (selector.Relation == CssSelectorRelation.SubSelector)
                selector = selector.Next;
            else
                break;
        }
        return null;
    }

    private static string? FindClass(CssSelector? selector)
    {
        while (selector != null)
        {
            if (selector.MatchType == CssSelectorMatchType.Class && selector.Value != null)
                return selector.Value;
            if (selector.Relation == CssSelectorRelation.SubSelector)
                selector = selector.Next;
            else
                break;
        }
        return null;
    }

    private static string? FindTag(CssSelector? selector)
    {
        while (selector != null)
        {
            if (selector.MatchType == CssSelectorMatchType.Tag &&
                selector.TagName != null && selector.TagName != "*")
                return selector.TagName;
            if (selector.Relation == CssSelectorRelation.SubSelector)
                selector = selector.Next;
            else
                break;
        }
        return null;
    }

    private static CssSelector? FindRightmost(CssSelector selector)
    {
        var current = selector;
        CssSelector? last = current;
        while (current != null)
        {
            if (current.IsLastInComplexSelector)
                return current;
            // Follow the chain
            if (current.Next != null && current.Relation == CssSelectorRelation.SubSelector)
            {
                current = current.Next;
                continue;
            }
            if (current.Next != null)
            {
                // Combinator - the next is further left, so current is rightmost
                return current;
            }
            break;
        }
        return last;
    }

    private static void AddToDict(Dictionary<string, List<RuleData>> dict, string key, RuleData data)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<RuleData>();
            dict[key] = list;
        }
        list.Add(data);
    }
}