using System.Text;
using Acrux.Core.Css.Matcher;
using Acrux.Core.Css.Properties;
using Acrux.Core.Css.Rules;
using Acrux.Core.Css.Values;

namespace Acrux.Core.Css.Tokenizer;

/// <summary>
/// CSS parser implementation that uses the tokenizer to produce StyleRuleBase objects.
/// Mirrors Blink's CSSParserImpl.
/// </summary>
public class CssParserImpl
{
    private CssParserTokenStream _stream;
    private readonly List<CssPropertyValue> _parsedProperties = new();
    private readonly CssParserContext _context;
    private readonly StyleSheetContents? _styleSheet;
    private bool _inNestedStyleRule;

    public CssParserImpl(CssParserContext context, StyleSheetContents? styleSheet = null)
    {
        _context = context;
        _styleSheet = styleSheet;
        _stream = new CssParserTokenStream("");
    }

    public static StyleSheetContents ParseStyleSheet(string css, CssParserContext context)
    {
        var sheet = new StyleSheetContents { ParserMode = context.Mode };
        var parser = new CssParserImpl(context, sheet);
        parser._stream = new CssParserTokenStream(css);
        parser.ConsumeRuleList(sheet.ChildRules, AllowedRulesType.RegularRules);
        return sheet;
    }

    /// <summary>
    /// Parses a declaration block body (e.g. an inline style attribute) into a
    /// <see cref="CssPropertyValueSet"/>. Handles strings, comments, escapes and
    /// !important correctly via the tokenizer.
    /// </summary>
    public static CssPropertyValueSet ParseDeclarationBlock(string css, CssParserContext context)
    {
        var parser = new CssParserImpl(context);
        parser._stream = new CssParserTokenStream(css);
        parser._parsedProperties.Clear();
        parser.ConsumeDeclarationList();
        var set = new CssPropertyValueSet(context.Mode);
        foreach (var prop in parser._parsedProperties)
            set.SetLonghandProperty(prop);
        return set;
    }

    public static List<StyleRuleBase> ParseRuleList(string css, CssParserContext context, AllowedRulesType allowed = AllowedRulesType.RegularRules)
    {
        var parser = new CssParserImpl(context);
        parser._stream = new CssParserTokenStream(css);
        var rules = new List<StyleRuleBase>();
        parser.ConsumeRuleList(rules, allowed);
        return rules;
    }

    public enum AllowedRulesType
    {
        RegularRules, KeyframeRules, FontFeatureRules, NoRules,
        NestedGroupRules, PageMarginRules
    }

    private void ConsumeRuleList(List<StyleRuleBase> rules, AllowedRulesType allowed)
    {
        SkipWhitespaceAndComments();
        while (!_stream.Current.IsEof)
        {
            if (_stream.Current.Type == CssTokenType.RightBraceToken)
                break;

            int offsetBefore = _stream.Offset;
            var rule = ConsumeRule(allowed);
            if (rule != null)
                rules.Add(rule);

            SkipWhitespaceAndComments();

            // Safety net: if nothing was consumed (malformed input, e.g. a stray
            // '}' or a leftover block opener), force-progress to avoid an infinite
            // loop. Consume the raw block (or skip to the next ';').
            if (_stream.Offset == offsetBefore)
            {
                if (_stream.Current.Type == CssTokenType.LeftBraceToken)
                    _stream.ConsumeRawBlock();
                else
                    SkipUntilSemicolon();
            }
        }
    }

    private StyleRuleBase? ConsumeRule(AllowedRulesType allowed)
    {
        if (_stream.Current.Type == CssTokenType.AtKeywordToken)
            return ConsumeAtRule(allowed);

        if (allowed == AllowedRulesType.RegularRules ||
            allowed == AllowedRulesType.NestedGroupRules)
            return ConsumeStyleRule();

        return null;
    }

    private StyleRuleBase? ConsumeAtRule(AllowedRulesType allowed)
    {
        string name = _stream.Current.Value.ToLowerInvariant();
        _stream.Next();

        return name switch
        {
            "media" => ConsumeMediaRule(allowed),
            "supports" => ConsumeSupportsRule(allowed),
            "import" => ConsumeImportRule(),
            "font-face" => ConsumeFontFaceRule(),
            "keyframes" or "-webkit-keyframes" => ConsumeKeyframesRule(),
            "page" => ConsumePageRule(),
            "layer" => ConsumeLayerRule(allowed),
            "container" => ConsumeContainerRule(allowed),
            "scope" => ConsumeScopeRule(allowed),
            "property" => ConsumePropertyRule(),
            "counter-style" => ConsumeCounterStyleRule(),
            "starting-style" => ConsumeStartingStyleRule(allowed),
            "namespace" => ConsumeNamespaceRule(),
            "charset" => ConsumeCharsetRule(),
            "position-try" => ConsumePositionTryRule(),
            _ => ConsumeUnknownAtRule()
        };
    }

    private StyleRuleMedia? ConsumeMediaRule(AllowedRulesType allowed)
    {
        string condition = ConsumeUntilBlockStart();
        var rule = new StyleRuleMedia { ConditionText = condition.Trim() };
        if (_stream.Current.Type == CssTokenType.LeftBraceToken)
        {
            _stream.Next();
            ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleSupports? ConsumeSupportsRule(AllowedRulesType allowed)
    {
        string condition = ConsumeUntilBlockStart();
        var rule = new StyleRuleSupports { ConditionText = condition.Trim() };
        if (_stream.Current.Type == CssTokenType.LeftBraceToken)
        {
            _stream.Next();
            ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleContainer? ConsumeContainerRule(AllowedRulesType allowed)
    {
        string condition = ConsumeUntilBlockStart();
        var rule = new StyleRuleContainer { ConditionText = condition.Trim() };
        if (_stream.Current.Type == CssTokenType.LeftBraceToken)
        {
            _stream.Next();
            ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleImport? ConsumeImportRule()
    {
        var rule = new StyleRuleImport();
        SkipWhitespaceAndComments();

        if (_stream.Current.Type == CssTokenType.StringToken ||
            _stream.Current.Type == CssTokenType.UrlToken)
        {
            rule.Url = _stream.Current.Value;
            _stream.Next();
        }
        else if (_stream.Current.Type == CssTokenType.FunctionToken &&
                 _stream.Current.FunctionName.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            _stream.Next();
            if (_stream.Current.Type == CssTokenType.StringToken ||
                _stream.Current.Type == CssTokenType.UrlToken)
            {
                rule.Url = _stream.Current.Value;
                _stream.Next();
            }
            ExpectCss(CssTokenType.RightParenthesisToken);
        }

        // Parse optional layer
        SkipWhitespaceAndComments();
        if (_stream.Current.Type == CssTokenType.IdentToken &&
            _stream.Current.Value.Equals("layer", StringComparison.OrdinalIgnoreCase))
        {
            _stream.Next();
            if (_stream.Current.Type == CssTokenType.LeftParenthesisToken)
            {
                _stream.Next();
                if (_stream.Current.Type == CssTokenType.IdentToken)
                {
                    rule.LayerNameStr = _stream.Current.Value;
                    _stream.Next();
                }
                ExpectCss(CssTokenType.RightParenthesisToken);
            }
            else
            {
                rule.LayerNameStr = "";
            }
        }

        // Parse optional media query
        SkipWhitespaceAndComments();
        while (_stream.Current.Type != CssTokenType.SemicolonToken &&
               _stream.Current.Type != CssTokenType.EofToken)
        {
            rule.MediaCondition = (rule.MediaCondition ?? "") + _stream.Current.ToCssText() + " ";
            _stream.Next();
        }

        ExpectCss(CssTokenType.SemicolonToken);
        return rule;
    }

    private StyleRuleFontFace? ConsumeFontFaceRule()
    {
        var rule = new StyleRuleFontFace();
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            _parsedProperties.Clear();
            ConsumeDeclarationList();
            rule.Properties = new CssPropertyValueSet(_context.Mode);
            foreach (var prop in _parsedProperties)
                rule.Properties.SetLonghandProperty(prop);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleKeyframes? ConsumeKeyframesRule()
    {
        SkipWhitespaceAndComments();
        string name = "";
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }
        else if (_stream.Current.Type == CssTokenType.StringToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }

        var rule = new StyleRuleKeyframes { Name = name };
        SkipWhitespaceAndComments();
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            while (!_stream.Current.IsEof && _stream.Current.Type != CssTokenType.RightBraceToken)
            {
                var keyframe = ConsumeKeyframeBlock();
                if (keyframe != null)
                    rule.Keyframes.Add(keyframe);
            }
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleKeyframe? ConsumeKeyframeBlock()
    {
        SkipWhitespaceAndComments();
        string key = "";
        while (_stream.Current.Type != CssTokenType.LeftBraceToken &&
               _stream.Current.Type != CssTokenType.RightBraceToken &&
               _stream.Current.Type != CssTokenType.EofToken)
        {
            key += _stream.Current.ToCssText();
            _stream.Next();
        }

        key = key.Trim();
        if (string.IsNullOrEmpty(key)) return null;

        var rule = new StyleRuleKeyframe { Key = key };
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            _parsedProperties.Clear();
            ConsumeDeclarationList();
            rule.Properties = new CssPropertyValueSet(_context.Mode);
            foreach (var prop in _parsedProperties)
                rule.Properties.SetLonghandProperty(prop);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRulePage? ConsumePageRule()
    {
        // Parse optional page selector
        string selector = "";
        SkipWhitespaceAndComments();
        if (_stream.Current.Type != CssTokenType.LeftBraceToken &&
            _stream.Current.Type != CssTokenType.ColonToken)
        {
            while (_stream.Current.Type != CssTokenType.LeftBraceToken &&
                   _stream.Current.Type != CssTokenType.EofToken)
            {
                selector += _stream.Current.Value;
                _stream.Next();
            }
        }

        var rule = new StyleRulePage { SelectorText = selector.Trim() };
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            _parsedProperties.Clear();
            ConsumeDeclarationList();
            rule.Properties = new CssPropertyValueSet(_context.Mode);
            foreach (var prop in _parsedProperties)
                rule.Properties.SetLonghandProperty(prop);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleBase? ConsumeLayerRule(AllowedRulesType allowed)
    {
        SkipWhitespaceAndComments();
        string? name = null;

        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
            // Layer statement (no block)
            if (_stream.Current.Type == CssTokenType.SemicolonToken)
            {
                _stream.Next();
                var statement = new StyleRuleLayerStatement();
                if (name != null) statement.LayerNames.Add(name);
                return statement;
            }
            if (_stream.Current.Type == CssTokenType.CommaToken)
            {
                var statement = new StyleRuleLayerStatement();
                if (name != null) statement.LayerNames.Add(name);
                while (_stream.Current.Type == CssTokenType.CommaToken)
                {
                    _stream.Next();
                    SkipWhitespaceAndComments();
                    if (_stream.Current.Type == CssTokenType.IdentToken)
                    {
                        statement.LayerNames.Add(_stream.Current.Value);
                        _stream.Next();
                    }
                }
                ExpectCss(CssTokenType.SemicolonToken);
                return statement;
            }
        }

        // Layer block
        var rule = new StyleRuleLayerBlock();
        if (name != null) rule.LayerName.Add(name);
        SkipWhitespaceAndComments();
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleScope? ConsumeScopeRule(AllowedRulesType allowed)
    {
        string scopeText = "";
        SkipWhitespaceAndComments();
        while (_stream.Current.Type != CssTokenType.LeftBraceToken &&
               _stream.Current.Type != CssTokenType.EofToken)
        {
            scopeText += _stream.Current.ToCssText();
            _stream.Next();
        }

        var rule = new StyleRuleScope();
        // Parse scope text for root/limit: "(.root) to (.limit)" or "(.root)"
        if (scopeText.Contains("to"))
        {
            var parts = scopeText.Split("to", 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) rule.ScopeRoot = parts[0].Trim();
            if (parts.Length > 1) rule.ScopeLimit = parts[1].Trim();
        }
        else
        {
            rule.ScopeRoot = scopeText.Trim();
        }

        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleProperty? ConsumePropertyRule()
    {
        SkipWhitespaceAndComments();
        string name = "";
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }

        var rule = new StyleRuleProperty { Name = name };
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            _parsedProperties.Clear();
            ConsumeDeclarationList();
            rule.Properties = new CssPropertyValueSet(_context.Mode);
            foreach (var prop in _parsedProperties)
                rule.Properties.SetLonghandProperty(prop);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleCounterStyle? ConsumeCounterStyleRule()
    {
        SkipWhitespaceAndComments();
        string name = "";
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }

        var rule = new StyleRuleCounterStyle { Name = name };
        // The stream hands out whitespace tokens rather than swallowing them, so the
        // space between the name and the block opener has to be stepped over.
        SkipWhitespaceAndComments();
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            // The descriptors are read as raw text, not as declarations: 'system',
            // 'symbols' and 'range' have no property id, and the declaration path would
            // fold them all into one anonymous entry that overwrites itself.
            foreach (var entry in SplitDescriptorDeclarations(ConsumeBlockText()))
                rule.Descriptors[entry.Key] = entry.Value;
        }
        return rule;
    }

    /// <summary>Consume the rest of the current block, one token at a time, and return its
    /// inner text. ConsumeRawBlock cannot be used here: it starts at the tokenizer's
    /// position, which for a block whose opener was already consumed sits past the first
    /// token of the block, and it ends at the first brace that does not raise the depth -
    /// which is the block's own closer, so the caller would keep reading into the
    /// following rule.</summary>
    private string ConsumeBlockText()
    {
        var sb = new StringBuilder();
        int depth = 0;
        while (!_stream.Current.IsEof)
        {
            var token = _stream.Current;
            if (token.Type == CssTokenType.RightBraceToken && depth == 0)
            {
                _stream.Next();
                break;
            }
            if (token.Type == CssTokenType.LeftBraceToken) depth++;
            else if (token.Type == CssTokenType.RightBraceToken) depth--;

            sb.Append(token.ToCssText());
            sb.Append(' ');
            _stream.Next();
        }
        return sb.ToString();
    }

    /// <summary>Split an @counter-style block into 'descriptor: value' pairs, ignoring
    /// separators inside quoted strings and function calls.</summary>
    private static IEnumerable<KeyValuePair<string, string>> SplitDescriptorDeclarations(string block)
    {
        int depth = 0;
        char quote = '\0';
        int start = 0;
        int colon = -1;
        for (int i = 0; i <= block.Length; i++)
        {
            char c = i < block.Length ? block[i] : ';';
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < block.Length) i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ':' && depth == 0 && colon < 0)
            {
                colon = i;
            }
            else if (c == ';' && depth == 0)
            {
                if (colon > start)
                {
                    string property = block[start..colon].Trim();
                    string value = block[(colon + 1)..i].Trim();
                    if (property.Length > 0 && value.Length > 0)
                        yield return new KeyValuePair<string, string>(property.ToLowerInvariant(), value);
                }
                start = i + 1;
                colon = -1;
            }
        }
    }

    private StyleRuleStartingStyle? ConsumeStartingStyleRule(AllowedRulesType allowed)
    {
        var rule = new StyleRuleStartingStyle();
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleNamespace? ConsumeNamespaceRule()
    {
        SkipWhitespaceAndComments();
        string? prefix = null;
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            prefix = _stream.Current.Value;
            _stream.Next();
            SkipWhitespaceAndComments();
        }

        string uri = "";
        if (_stream.Current.Type == CssTokenType.StringToken ||
            _stream.Current.Type == CssTokenType.UrlToken)
        {
            uri = _stream.Current.Value;
            _stream.Next();
        }

        ExpectCss(CssTokenType.SemicolonToken);
        return new StyleRuleNamespace { Prefix = prefix, NamespaceUri = uri };
    }

    private StyleRulePositionTry? ConsumePositionTryRule()
    {
        SkipWhitespaceAndComments();
        string name = "";
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }

        var rule = new StyleRulePositionTry { Name = name };
        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            _parsedProperties.Clear();
            ConsumeDeclarationList();
            rule.Properties = new CssPropertyValueSet(_context.Mode);
            foreach (var prop in _parsedProperties)
                rule.Properties.SetLonghandProperty(prop);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleBase? ConsumeCharsetRule()
    {
        SkipUntilSemicolon();
        return null;
    }

    private StyleRuleBase? ConsumeUnknownAtRule()
    {
        // Skip unknown @ rules
        if (_stream.Current.Type == CssTokenType.LeftBraceToken)
        {
            _stream.ConsumeRawBlock();
        }
        else
        {
            SkipUntilSemicolon();
        }
        return null;
    }

    private StyleRule? ConsumeStyleRule()
    {
        string selectorText = ConsumeSelectorText();
        if (string.IsNullOrWhiteSpace(selectorText)) return null;

        var selectors = CssSelectorParser.ParseSelectorList(selectorText);
        if (selectors.Count == 0) return null;

        var rule = new StyleRule();
        rule.Selectors.AddRange(selectors);
        rule.OriginalSelectorText = selectorText;

        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            _parsedProperties.Clear();
            ConsumeDeclarationList();
            rule.Properties = new CssPropertyValueSet(_context.Mode);
            foreach (var prop in _parsedProperties)
                rule.Properties.SetLonghandProperty(prop);
            ExpectCss(CssTokenType.RightBraceToken);
        }

        return rule;
    }

    private string ConsumeSelectorText()
    {
        var result = new System.Text.StringBuilder();
        int depth = 0;

        while (!_stream.Current.IsEof)
        {
            var t = _stream.Current;

            if (t.Type == CssTokenType.LeftBraceToken && depth == 0)
                break;

            if (t.Type == CssTokenType.FunctionToken ||
                t.Type == CssTokenType.LeftParenthesisToken ||
                t.Type == CssTokenType.LeftSquareBracketToken)
                depth++;
            else if (t.Type == CssTokenType.RightParenthesisToken ||
                     t.Type == CssTokenType.RightSquareBracketToken)
                depth--;

            result.Append(t.ToCssText());
            _stream.Next();
        }

        return result.ToString().Trim();
    }

    private void ConsumeDeclarationList()
    {
        SkipWhitespaceAndComments();
        while (!_stream.Current.IsEof && _stream.Current.Type != CssTokenType.RightBraceToken)
        {
            if (_stream.Current.Type == CssTokenType.SemicolonToken)
            {
                _stream.Next();
                SkipWhitespaceAndComments();
                continue;
            }

            ConsumeDeclaration();
            SkipWhitespaceAndComments();
        }
    }

    private void ConsumeDeclaration()
    {
        if (_stream.Current.Type != CssTokenType.IdentToken)
        {
            SkipUntilSemicolon();
            return;
        }

        string propertyName = _stream.Current.Value;
        _stream.Next();
        SkipWhitespaceAndComments();

        if (_stream.Current.Type != CssTokenType.ColonToken)
        {
            SkipUntilSemicolon();
            return;
        }
        _stream.Next();
        SkipWhitespaceAndComments();

        // Parse value
        bool important = false;
        string valueText = ConsumeDeclarationValue();

        if (valueText.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
        {
            important = true;
            valueText = valueText[..^"!important".Length].Trim();
        }

        // Convert to CssValue and add to parsed properties
        if (!string.IsNullOrEmpty(propertyName) && !string.IsNullOrEmpty(valueText))
        {
            var name = CssPropertyName.FromString(propertyName);
            var value = ParseCssValue(propertyName, valueText);
            if (value != null)
            {
                _parsedProperties.Add(new CssPropertyValue(name, value, important));
            }
        }

        SkipWhitespaceAndComments();
        if (_stream.Current.Type == CssTokenType.SemicolonToken)
            _stream.Next();
    }

    private string ConsumeDeclarationValue()
    {
        var result = new System.Text.StringBuilder();
        int depth = 0;

        while (!_stream.Current.IsEof)
        {
            var t = _stream.Current;

            if (t.Type == CssTokenType.SemicolonToken && depth == 0)
                break;
            if (t.Type == CssTokenType.RightBraceToken && depth == 0)
                break;
            if (t.Type == CssTokenType.DelimiterToken && t.Value == "!" && depth == 0)
            {
                _stream.Next();
                SkipWhitespaceAndComments();
                if (_stream.Current.Type == CssTokenType.IdentToken &&
                    _stream.Current.Value.Equals("important", StringComparison.OrdinalIgnoreCase))
                {
                    result.Append(" !important");
                    _stream.Next();
                    continue;
                }
                result.Append("!");
                continue;
            }

            if (t.Type == CssTokenType.FunctionToken ||
                t.Type == CssTokenType.LeftParenthesisToken ||
                t.Type == CssTokenType.LeftSquareBracketToken ||
                t.Type == CssTokenType.LeftBraceToken)
                depth++;
            else if (t.Type == CssTokenType.RightParenthesisToken ||
                     t.Type == CssTokenType.RightSquareBracketToken ||
                     t.Type == CssTokenType.RightBraceToken)
                depth--;

            result.Append(t.ToCssText());
            _stream.Next();
        }

        return result.ToString().Trim();
    }

    private CssValue? ParseCssValue(string propertyName, string valueText)
    {
        // One spelling for every declaration. A reference engine prints the value it parsed
        // rather than the characters it was handed, whether that value is a single number or a
        // whole layer list, so the text is canonicalised first and everything below reads the
        // canonical text — otherwise 'padding: 0' takes the simple-value path and keeps the
        // bare zero, while 'margin: 0 auto' prints the '0px' the same engine prints.
        var text = Acrux.Core.Css.CssValueText.Canonicalize(valueText, propertyName.ToLowerInvariant());

        var wideKeyword = CssWideKeywordParser.Parse(text);
        if (wideKeyword != null) return wideKeyword;

        // Simple value parsing for common types
        var parsed = TryParseSimpleValue(text);
        if (parsed != null) return parsed;

        // For complex values, store as the raw text for now — in the spelling a reference
        // engine prints a specified value in, because this text is also what the CSSOM hands
        // back for the declaration ('rgb(7,7,7)' has to read out as 'rgb(7, 7, 7)').
        return new CssUnparsedValue(text);
    }

    private static CssValue? TryParseSimpleValue(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // Try CSS-wide keywords
        var keyword = CssWideKeywordParser.Parse(text);
        if (keyword != null) return keyword;

        // Try identifier
        var id = CssIdentifierValue.StringToValueId(text);
        if (id != CssValueId.Invalid)
            return CssIdentifierValue.Create(id);

        // Try number
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double num))
        {
            return CssNumericLiteralValue.Create(num, CssUnitType.Number);
        }

        // Try percentage
        if (text.EndsWith('%') && double.TryParse(text[..^1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out num))
        {
            return CssNumericLiteralValue.Create(num, CssUnitType.Percentage);
        }

        // Try length with unit
        var unit = CssPrimitiveValue.StringToUnitType(text);
        if (unit != CssUnitType.Unknown)
        {
            string numPart = text[..^CssPrimitiveValue.UnitTypeToString(unit).Length];
            if (double.TryParse(numPart, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out num))
            {
                return CssNumericLiteralValue.Create(num, unit);
            }
        }

        return null;
    }

    private string ConsumeUntilBlockStart()
    {
        var result = new System.Text.StringBuilder();
        while (!_stream.Current.IsEof && _stream.Current.Type != CssTokenType.LeftBraceToken)
        {
            result.Append(_stream.Current.ToCssText());
            _stream.Next();
        }
        SkipWhitespaceAndComments();
        return result.ToString();
    }

    private void SkipWhitespaceAndComments()
    {
        while (_stream.Current.Type == CssTokenType.WhitespaceToken ||
               _stream.Current.Type == CssTokenType.CommentToken)
            _stream.Next();
    }

    private void SkipUntilSemicolon()
    {
        // CSS Syntax: an invalid declaration consumes until ';' OR the end of
        // the declaration block. Stopping only at ';' would swallow the '}' and
        // every rule that follows until the next semicolon.
        while (!_stream.Current.IsEof &&
               _stream.Current.Type != CssTokenType.SemicolonToken &&
               _stream.Current.Type != CssTokenType.RightBraceToken)
            _stream.Next();
        if (_stream.Current.Type == CssTokenType.SemicolonToken)
            _stream.Next();
    }

    private bool ExpectCss(CssTokenType type)
    {
        if (_stream.Current.Type == type)
        {
            _stream.Next();
            return true;
        }
        return false;
    }
}

/// <summary>
/// Parser context: holds the parser mode, URL, etc. Mirrors Blink's CSSParserContext.
/// </summary>
public class CssParserContext
{
    public CssParserMode Mode { get; set; } = CssParserMode.HTMLStandard;
    public string? BaseUrl { get; set; }
    public bool IsStandardMode { get; set; } = true;

    public static CssParserContext Default() => new() { Mode = CssParserMode.HTMLStandard };
    public static CssParserContext UaMode() => new() { Mode = CssParserMode.UACSS };
}

/// <summary>Fallback unparsed value for CSS values we can't yet parse.</summary>
public class CssUnparsedValue : CssValue
{
    public string RawText { get; }

    public CssUnparsedValue(string rawText) : base(CssClassType.Unparsed)
    {
        RawText = rawText;
    }

    public override string CssText() => RawText;
}