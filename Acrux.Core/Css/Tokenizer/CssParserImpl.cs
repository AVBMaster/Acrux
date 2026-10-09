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

    /// <summary>Whether the sheet has moved past the rules an '@import' may follow, and past the
    /// rules an '@namespace' may follow. The two lead a stylesheet, and they do not lead it equally:
    /// an '@import' may come after '@charset', after another '@import' and after an '@layer'
    /// statement, and an '@namespace' may come after all of those AND after an '@import', but the
    /// first rule of any other kind closes each out (measured: <c>@layer a; @import url(b.css); #x{}</c>
    /// keeps all three and <c>@import url(a.css); @namespace url(n);</c> keeps both, while
    /// <c>@media screen{} @import url(c.css);</c>, <c>@font-face{…} @import url(b.css);</c>,
    /// <c>@namespace url(n); @import url(b.css);</c> and <c>#x{} @namespace url(n);</c> each lose the
    /// statement they follow). Inside a group rule there is no statement phase at all, so both kinds
    /// are dropped there too.</summary>
    private bool _importsClosed;
    private bool _namespacesClosed;

    /// <summary>The descriptors of the at-rule block being parsed, or null while an ordinary
    /// declaration block is. An at-rule speaks its own vocabulary: a '@font-face' has a 'src' and
    /// a 'size-adjust' that are not properties at all, and it has no place for the properties a
    /// style rule carries. The list is what tells the two apart, and a name missing from it makes
    /// no declaration (measured: '@font-face { --foo: bar; font-size-adjust: 90%; src: url(a) }'
    /// answers 'src' and nothing else). A descriptor takes a value of its own shape and no
    /// CSS-wide keyword: 'font-family: initial' is not a family.</summary>
    private HashSet<string>? _descriptors;

    /// <summary>Whether the block being parsed carries nothing but <see cref="_descriptors"/>. An
    /// '@font-face' is such a block — a property a style rule may carry makes no declaration there,
    /// 'font-size-adjust' among them. An '@page' is not: it takes any property that can apply to a
    /// page box and adds its own 'size' to them.</summary>
    private bool _descriptorsExclusive;

    public CssParserImpl(CssParserContext context, StyleSheetContents? styleSheet = null)
    {
        _context = context;
        _styleSheet = styleSheet;
        _stream = new CssParserTokenStream("");
    }

    public static StyleSheetContents ParseStyleSheet(string css, CssParserContext context)
    {
        var sheet = ParseStyleSheetFragment(css, context);
        ApplySheetNamespaces(sheet);
        return sheet;
    }

    /// <summary>Parses a rule list and leaves its selectors unsettled. A fragment a script hands
    /// to <c>insertRule</c> has to be settled against the sheet it lands in and not against
    /// itself (CSSOM §5.2.4 resolves an inserted rule with the target's namespace map), so a
    /// prefix written only in the inserted text still means something when that sheet declares
    /// it (measured: an inserted 'p|div' rule in a sheet holding '@namespace p url(…)' paints).</summary>
    public static StyleSheetContents ParseStyleSheetFragment(string css, CssParserContext context)
    {
        var sheet = new StyleSheetContents { ParserMode = context.Mode };
        var parser = new CssParserImpl(context, sheet);
        parser._stream = new CssParserTokenStream(css);
        parser.ConsumeRuleList(sheet.ChildRules, AllowedRulesType.RegularRules);
        return sheet;
    }

    /// <summary>Parses the text a script hands to <c>insertRule</c> (CSSOM §5.2.4): the whole of it
    /// has to be exactly one rule. A text that holds two, or that ends in a semicolon after a
    /// complete style rule, is refused rather than read as far as it works, and an '@charset' is a
    /// rule the sheet decoder owns and no script may insert (measured: each of those three answers
    /// a SyntaxError and leaves the sheet holding what it held before). The rule comes back with its
    /// selectors unsettled, because they belong to the sheet the rule lands in.</summary>
    public static StyleRuleBase? ParseRuleForInsertion(string css, CssParserContext context)
    {
        var parser = new CssParserImpl(context);
        parser._stream = new CssParserTokenStream(css);
        parser.SkipWhitespaceAndComments();
        var rule = parser.ConsumeRule(AllowedRulesType.RegularRules);
        if (rule == null) return null;
        parser.SkipWhitespaceAndComments();
        return parser._stream.Current.IsEof ? rule : null;
    }

    /// <summary>Settle the whole sheet's namespaces from the '@namespace' rules it holds, then hand
    /// each rule its own answer. The declarations are read from the rules the phase flags already
    /// let through: the first bare one is the default, the first one for a prefix is that prefix's,
    /// and a later duplicate of either is a rule the sheet keeps but nothing else honours (measured:
    /// two '@namespace url(…)' lines stay in the sheet and the first decides). A selector that names
    /// a prefix no declaration gives, or a prefix in front of an attribute, takes its whole rule
    /// down with it — the rest of the selector list goes too, which is how a single bad comma-
    /// separated selector empties a sheet (measured: 'zz|div, #x { … }' leaves no rule at all).</summary>
    internal static void ApplySheetNamespaces(StyleSheetContents sheet)
    {
        CollectNamespaces(sheet);
        ApplyNamespaces(sheet.ChildRules, sheet);
    }

    /// <summary>Settle a list of rules against the namespaces the sheet already knows.</summary>
    internal static void ApplyNamespaces(List<StyleRuleBase> rules, StyleSheetContents sheet) =>
        ApplyNamespacesTo(rules, (sheet.DefaultNamespaceUri,
            sheet.NamespacePrefixes.Count > 0 ? sheet.NamespacePrefixes : null));

    /// <summary>Read the sheet's own '@namespace' rules into it: the first prefixless one is the
    /// default, the first one for a prefix is that prefix's, and a later duplicate of either stays
    /// in the sheet as a rule without deciding anything (measured: two '@namespace url(…)' lines
    /// are both kept and the first one is the address the selectors are held to).</summary>
    private static void CollectNamespaces(StyleSheetContents sheet)
    {
        sheet.NamespaceRules.Clear();
        sheet.DefaultNamespaceUri = null;
        sheet.NamespacePrefixes.Clear();
        foreach (var rule in sheet.ChildRules)
        {
            if (rule is not StyleRuleNamespace ns) continue;
            sheet.NamespaceRules.Add(ns);
            if (string.IsNullOrEmpty(ns.Prefix))
            {
                sheet.DefaultNamespaceUri ??= ns.NamespaceUri;
                continue;
            }
            if (!sheet.NamespacePrefixes.ContainsKey(ns.Prefix))
                sheet.NamespacePrefixes[ns.Prefix] = ns.NamespaceUri;
        }
    }

    /// <summary>The same decision, made rule by rule, so a group's children take the sheet's
    /// declarations just as the rules beside it does (measured: an id selector inside an '@media' in
    /// a sheet with a foreign default matches nothing).</summary>
    private static void ApplyNamespacesTo(List<StyleRuleBase> rules,
        (string? Default, Dictionary<string, string>? Prefixes) namespaces)
    {
        for (int i = rules.Count - 1; i >= 0; i--)
        {
            switch (rules[i])
            {
                case StyleRule style:
                    if (CssSelectorParser.ApplyNamespaces(style.Selectors, namespaces.Default,
                            namespaces.Prefixes))
                        rules.RemoveAt(i);
                    else if (style.Selectors.Count == 0)
                        rules.RemoveAt(i);
                    ApplyNamespacesTo(style.ChildRules, namespaces);
                    break;
                case StyleRuleGroup group:
                    ApplyNamespacesTo(group.ChildRules, namespaces);
                    break;
            }
        }
    }

    /// <summary>
    /// Parses a declaration block body (e.g. an inline style attribute) into a
    /// <see cref="CssPropertyValueSet"/>. Handles strings, comments, escapes and
    /// !important correctly via the tokenizer.
    /// <para>
    /// The descriptor vocabulary is the one thing a block written through the CSSOM has to say more
    /// about than an inline style does: <c>fontFaceRule.style.src = 'url(a.woff)'</c> names a
    /// descriptor of an '@font-face' and is stored, while the same name in a style rule is nobody's
    /// property and is dropped (measured). A block that carries none is parsed as a style rule is.
    /// </para>
    /// </summary>
    public static CssPropertyValueSet ParseDeclarationBlock(string css, CssParserContext context,
        HashSet<string>? descriptors = null, bool descriptorsOnly = false)
    {
        var parser = new CssParserImpl(context);
        parser._stream = new CssParserTokenStream(css);
        parser._descriptors = descriptors;
        parser._descriptorsExclusive = descriptorsOnly;
        parser._stream.SetUnicodeRangesAllowed(descriptorsOnly);
        parser._parsedProperties.Clear();
        parser.ConsumeDeclarationList();
        var set = new CssPropertyValueSet(context.Mode);
        foreach (var prop in parser._parsedProperties)
            set.SetLonghandProperty(prop);
        return set;
    }

    /// <summary>The descriptor vocabulary of an '@font-face', for a block the CSSOM is writing.</summary>
    internal static HashSet<string> FontFaceDescriptorNames => FontFaceDescriptors;

    /// <summary>The descriptor vocabulary of an '@page' — the properties of a page box plus the one
    /// name that is a descriptor and not a property anywhere else.</summary>
    internal static HashSet<string> PageDescriptorNames => PageDescriptors;

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
            {
                rules.Add(rule);
                // Each statement rule closes out the phase of the statements that may not follow it.
                // An '@namespace' closes the imports; an '@import' closes nothing.
                if (allowed == AllowedRulesType.RegularRules)
                {
                    if (rule is not (StyleRuleImport or StyleRuleLayerStatement)) _importsClosed = true;
                    if (rule is not (StyleRuleImport or StyleRuleNamespace or StyleRuleLayerStatement))
                        _namespacesClosed = true;
                }
            }

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
            "import" => ConsumeImportRule(allowed),
            "font-face" => ConsumeFontFaceRule(),
            "keyframes" or "-webkit-keyframes" => ConsumeKeyframesRule(),
            "page" => ConsumePageRule(),
            "layer" => ConsumeLayerRule(allowed),
            "container" => ConsumeContainerRule(allowed),
            "scope" => ConsumeScopeRule(allowed),
            "property" => ConsumePropertyRule(),
            "counter-style" => ConsumeCounterStyleRule(),
            "starting-style" => ConsumeStartingStyleRule(allowed),
            "namespace" => ConsumeNamespaceRule(allowed),
            "charset" => ConsumeCharsetRule(),
            "position-try" => ConsumePositionTryRule(),
            "font-feature-values" => ConsumeFontFeatureValuesRule(),
            "view-transition" => ConsumeViewTransitionRule(),
            _ => ConsumeUnknownAtRule()
        };
    }

    /// <summary>An '@font-feature-values' block. The rule is kept and named, and everything written
    /// inside it is dropped: the feature-blocks are not rules the object model exposes, so
    /// '@font-feature-values x { @styleset { a { font-variant: 1 } } }' reads back as the block with
    /// nothing in it (measured) — and a prelude that names no family makes no rule at all.</summary>
    private StyleRuleFontFeatureValues? ConsumeFontFeatureValuesRule()
    {
        string family = ConsumeAtRulePrelude(out bool unterminated).Trim();
        if (unterminated || !AtRuleBlockFollows() || family.Length == 0)
        {
            if (_stream.Current.Type == CssTokenType.LeftBraceToken) _stream.ConsumeRawBlock();
            return null;
        }
        _stream.Next();                       // the block's opener
        _stream.ConsumeRawBlock();
        return new StyleRuleFontFeatureValues { FamilyName = family };
    }

    private StyleRuleMedia? ConsumeMediaRule(AllowedRulesType allowed)
    {
        string condition = ConsumeAtRulePrelude(out bool unterminated);
        if (unterminated || !AtRuleBlockFollows()) return null;
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
        string condition = ConsumeAtRulePrelude(out bool unterminated);
        if (unterminated || !AtRuleBlockFollows()) return null;
        // The rule reads structure, not the declaration grammar: a group keeps the rule whatever is
        // inside it, and the condition then answers no when the cascade reaches it. Measured on
        // '@supports (color)', '@supports (bogus: 1)', '@supports (width: calc(bogus))' and
        // '@supports ()', each of which leaves the block in the sheet with nothing inside it
        // applying, while '@supports display: flex' — a declaration with nothing round it — and
        // '@supports (display: flex) junk', which the read cannot finish, make no rule at all.
        if (!SupportsConditionParsesAsRule(condition))
        {
            SkipStatementBlock();
            return null;
        }
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
        string condition = ConsumeAtRulePrelude(out bool unterminated);
        if (unterminated || !AtRuleBlockFollows()) return null;
        var rule = new StyleRuleContainer { ConditionText = condition.Trim() };
        if (_stream.Current.Type == CssTokenType.LeftBraceToken)
        {
            _stream.Next();
            ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    private StyleRuleBase? ConsumeImportRule(AllowedRulesType allowed)
    {
        var rule = new StyleRuleImport();
        SkipWhitespaceAndComments();

        bool hasUrl = TryConsumeUrl(out string url);
        rule.Url = url;

        // Parse optional layer. 'layer' with a name is one function token to the tokenizer, not an
        // ident followed by a parenthesis, so reading only the ident spelling leaves
        // '@import url(a.css) layer(one) print' with its layer name swept up into the medium.
        SkipWhitespaceAndComments();
        if (IsIdentNamed(_stream.Current, "layer"))
        {
            _stream.Next();
            rule.LayerNameStr = "";
        }
        else if (IsFunctionNamed(_stream.Current, "layer"))
        {
            _stream.Next();
            SkipWhitespaceAndComments();
            // A layer name is a dotted sequence of idents (CSS Cascade 5 §6.3), not one ident:
            // '@import url(a.css) layer(a.b)' names the layer 'a.b' and reads back that way, and
            // stopping after the first component leaves '.b' behind to be swept up by the medium.
            var name = new StringBuilder();
            while (_stream.Current.Type == CssTokenType.IdentToken)
            {
                name.Append(_stream.Current.Value);
                _stream.Next();
                if (_stream.Current.Type != CssTokenType.DelimiterToken || _stream.Current.Value != ".") break;
                if (_stream.LookAhead().Type != CssTokenType.IdentToken) break;
                name.Append('.');
                _stream.Next();
            }
            // The name has to be the whole argument: 'layer(a . b)' and 'layer(a.)' are not names and
            // 'layer(1)' never was one, and what the grammar cannot read as a layer is not thrown away
            // but left where it stands, to be read as the beginning of the medium (measured:
            // 'layer(1)' reads back its own text as a media item and leaves 'layerName' null, and
            // 'layer(a . b)' does the same with its spaces gone).
            if (name.Length > 0 && _stream.Current.Type == CssTokenType.RightParenthesisToken)
            {
                rule.LayerNameStr = name.ToString();
                _stream.Next();
            }
            else
            {
                // What the read reached is kept with the group: 'layer(a . b)' reads back with its
                // first name component and both of its spaces, and 'layer(a.)' with its dot (measured
                // both) — so the ident or idents the name loop took are part of the text, not gone.
                rule.LayerNameStr = null;
                rule.MediaCondition = (rule.MediaCondition ?? "") + "layer(" + name + ConsumeGroupTail();
            }
        }

        // 'supports(...)' sits between the layer and the medium (CSS Conditional Rules 4 §4) and is
        // NOT part of the medium: a rule that carries one still answers an empty media list, so the
        // condition has to be taken out here rather than swept up with what follows it. The text it
        // reads back is the page's own, taken from between the parentheses rather than re-spelled
        // from the tokens (measured: 'supports( display :   flex )' reads back with its spaces and
        // its ' :' untouched, and 'supports(selector(:hover))' keeps its own inner function).
        SkipWhitespaceAndComments();
        bool supportsInvalid = false;
        if (IsFunctionNamed(_stream.Current, "supports"))
        {
            int start = _stream.Offset;      // just past 'supports('
            _stream.Next();
            int depth = 1;
            while (!_stream.Current.IsEof)
            {
                var token = _stream.Current;
                // A ';' written inside the function's own group belongs to the group: the statement
                // has no end there, and the reference engine reads on to the end of the sheet and
                // takes everything after the condition with it (measured: '@import url(b.css)
                // supports((display: flex); @import url(c.css);' leaves one rule, not two).
                if (token.Type == CssTokenType.FunctionToken || token.Type == CssTokenType.LeftParenthesisToken) depth++;
                else if (token.Type == CssTokenType.RightParenthesisToken && --depth == 0) break;
                _stream.Next();
            }
            rule.SupportsCondition = (_stream.Current.IsEof
                ? _stream.RawRange(start, _stream.Offset)
                : _stream.RawRange(start, _stream.TokenStart)).TrimStart();
            if (_stream.Current.Type == CssTokenType.RightParenthesisToken) _stream.Next();
            // A 'supports()' whose condition the grammar cannot read is not a condition, and an
            // '@import' with no condition to test is not an '@import' (measured: 'supports()',
            // 'supports(display: )' and 'supports(bogus: 1)' each take the whole rule down, while
            // 'supports(selector(a))' and 'supports(NOT (display: flex))' are kept as written). What
            // the read does not reach is dropped from the text it keeps rather than left beside it:
            // 'supports(selector(a)|b)' reads back 'selector(a)' (measured).
            if (!SupportsConditionParsesForImport(rule.SupportsCondition, out int reached)) supportsInvalid = true;
            else if (reached >= 0 && reached < (rule.SupportsCondition?.Length ?? 0))
                rule.SupportsCondition = rule.SupportsCondition![..reached];
        }

        // The medium runs to the ';' that ends the statement — or, if the page opened a block
        // before ending it, to that block, which is what makes the whole import invalid. Measured
        // on '@import url(x.css) #i { color: red } #j { color: green }': the import and the '#i'
        // block it opened are both gone and '#j' survives as the rule it is.
        SkipWhitespaceAndComments();
        bool blockFollows = false;
        while (_stream.Current.Type != CssTokenType.SemicolonToken &&
               _stream.Current.Type != CssTokenType.EofToken)
        {
            if (_stream.Current.Type == CssTokenType.LeftBraceToken) { blockFollows = true; break; }
            if (_stream.Current.Type == CssTokenType.RightBraceToken) break;  // the parent's closer
            rule.MediaCondition = (rule.MediaCondition ?? "") + _stream.Current.ToCssText() + " ";
            _stream.Next();
        }

        ExpectCss(CssTokenType.SemicolonToken);
        if (blockFollows) SkipStatementBlock();

        // A statement with no address to name is not an '@import' at all ('@import;' goes, while
        // '@import url();' stays with an empty one), one whose 'supports()' names no condition makes
        // no rule either, and one that is out of the statement phase makes none (measured all three).
        if (!hasUrl || blockFollows || supportsInvalid || !ImportsAllowed(allowed)) return null;
        return rule;
    }

    private StyleRuleBase? ConsumeFontFaceRule()
    {
        // An '@font-face' is a declaration block and nothing else: written with a ';' in place of
        // one it makes no rule at all (measured), which is why the block is required rather than
        // skipped over.
        SkipWhitespaceAndComments();
        if (!ExpectCss(CssTokenType.LeftBraceToken)) return null;
        _descriptors = FontFaceDescriptors;
        _descriptorsExclusive = true;
        // An '@font-face' is the one block whose stream reads a codepoint range as a token of its
        // own rather than an ident and a number (CSS Syntax 3 §4.3.14).
        _stream.SetUnicodeRangesAllowed(true);
        var rule = new StyleRuleFontFace { Properties = ParseDeclarationSet() };
        _descriptors = null;
        _descriptorsExclusive = false;
        _stream.SetUnicodeRangesAllowed(false);
        ExpectCss(CssTokenType.RightBraceToken);
        return rule;
    }

    private StyleRuleBase? ConsumeKeyframesRule()
    {
        SkipWhitespaceAndComments();
        // The animation name is required, and an ident or a string — '@keyframes { … }' names
        // nothing and so is no rule at all (measured).
        string name = "";
        if (_stream.Current.Type == CssTokenType.IdentToken ||
            _stream.Current.Type == CssTokenType.StringToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }
        else
        {
            SkipStatementTail();
            return null;
        }

        SkipWhitespaceAndComments();
        if (!ExpectCss(CssTokenType.LeftBraceToken)) return null;

        var rule = new StyleRuleKeyframes { Name = name };
        while (!_stream.Current.IsEof && _stream.Current.Type != CssTokenType.RightBraceToken)
        {
            var keyframe = ConsumeKeyframeBlock();
            if (keyframe != null)
                rule.Keyframes.Add(keyframe);
        }
        ExpectCss(CssTokenType.RightBraceToken);
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
        // A keyframe selector is a comma-separated list of offsets, and the list has to be valid
        // all the way through: '0.5%' of a rule that also names '120%' or 'from to' takes the whole
        // rule with it (measured), while one offset out of range leaves the others of its own list
        // behind in the same way.
        if (!IsValidKeyframeSelector(key, out string normalised))
        {
            if (_stream.Current.Type == CssTokenType.LeftBraceToken) SkipStatementBlock();
            else SkipStatementTail();
            return null;
        }

        var rule = new StyleRuleKeyframe { Key = normalised };
        if (!ExpectCss(CssTokenType.LeftBraceToken)) return rule;
        rule.Properties = ParseDeclarationSet();
        ExpectCss(CssTokenType.RightBraceToken);
        return rule;
    }

    /// <summary>Whether a keyframe rule's selectors are all offsets the animation can use, and the
    /// text they read back as. 'from' and 'to' are folded to '0%' and '100%' (measured: a rule
    /// written 'from' reads its keyText as '0%'), an offset outside 0..100 is no offset, and a
    /// number without a '%' is not one either — which is what drops '0.5' and 'from to', the second
    /// of them because a keyframe list is separated by commas, not by spaces.</summary>
    private static bool IsValidKeyframeSelector(string key, out string normalised)
    {
        normalised = "";
        if (key.Length == 0) return false;
        var parts = key.Split(',');
        var keys = new List<string>(parts.Length);
        foreach (var raw in parts)
        {
            string part = raw.Trim();
            if (part.Equals("from", StringComparison.OrdinalIgnoreCase)) { keys.Add("0%"); continue; }
            if (part.Equals("to", StringComparison.OrdinalIgnoreCase)) { keys.Add("100%"); continue; }
            if (!part.EndsWith("%", StringComparison.Ordinal)) return false;
            var number = part[..^1].Trim();
            if (number.Length == 0 || !TryParseCssNumber(number, out double value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 100)
                return false;
            keys.Add(part);
        }
        normalised = string.Join(", ", keys);
        return true;
    }

    private static bool TryParseCssNumber(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value);

    private StyleRuleBase? ConsumePageRule()
    {
        // The page selector is one compound: a page name and at most one page-context pseudo-class
        // of the ones the grammar knows. A list of compounds, a pseudo-class outside that set, and
        // a second pseudo-class each leave no rule behind (measured: '@page', '@page :left',
        // '@page a:left' and '@page :first' stay while '@page :left, :right', '@page :nth(2)',
        // '@page :blank' and '@page toc :first' go), and the pseudo-class is read without regard to
        // case and written back in lower case.
        string selector = ConsumeAtRulePrelude(out bool unterminated);
        if (unterminated || !AtRuleBlockFollows() ||
            !IsValidPageSelector(selector.Trim(), out string pageSelector))
        {
            if (!unterminated && _stream.Current.Type == CssTokenType.LeftBraceToken)
                SkipStatementBlock();
            return null;
        }

        var rule = new StyleRulePage { SelectorText = pageSelector };
        _stream.Next();   // the block opener the check above found
        ParsePageBody(rule);
        return rule;
    }

    /// <summary>Read a '@page' block: its own declarations and the page-margin at-rules nested in
    /// it. The margin rules belong to the page rather than to the sheet — the reference engine
    /// prints them inside their page, after its declarations, whatever order the block wrote them
    /// in (measured) — and one whose name is not a page margin makes no rule at all.</summary>
    private void ParsePageBody(StyleRulePage rule)
    {
        int mark = _parsedProperties.Count;
        while (true)
        {
            SkipWhitespaceAndComments();
            if (_stream.Current.IsEof) break;
            if (_stream.Current.Type == CssTokenType.RightBraceToken)
            {
                _stream.Next();
                break;
            }

            if (_stream.Current.Type != CssTokenType.AtKeywordToken)
            {
                // A '@page' carries the properties a page box can have plus the one descriptor that
                // is not a property anywhere else but is a keyword list here: 'size'.
                _descriptors = PageDescriptors;
                ConsumeDeclaration();
                _descriptors = null;
                continue;
            }

            string margin = _stream.Current.Value.ToLowerInvariant();
            _stream.Next();
            SkipWhitespaceAndComments();
            if (!PageMarginNames.Contains(margin) ||
                _stream.Current.Type != CssTokenType.LeftBraceToken)
            {
                SkipStatementTail();
                continue;
            }

            _stream.Next();
            int inner = _parsedProperties.Count;
            ConsumeDeclarationList();
            ExpectCss(CssTokenType.RightBraceToken);
            rule.MarginRules.Add(new StyleRulePageMargin
            {
                MarginId = margin,
                Properties = TakeParsedProperties(inner)
            });
        }
        rule.Properties = TakeParsedProperties(mark);
    }

    private static readonly HashSet<string> PageMarginNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "top-left-corner", "top-left", "top-center", "top-right", "top-right-corner",
        "bottom-left-corner", "bottom-left", "bottom-center", "bottom-right", "bottom-right-corner",
        "left-top", "left-middle", "left-bottom", "right-top", "right-middle", "right-bottom"
    };

    /// <summary>Validate a page selector and give back the text the rule reads as.</summary>
    private static bool IsValidPageSelector(string selector, out string normalised)
    {
        normalised = "";
        if (selector.Length == 0) return true;
        // The compound is written without a gap: '@page toc :first' is not a page selector, and the
        // reference engine drops it (measured).
        if (selector.IndexOf(',') >= 0 ||
            selector.Any(c => c is ' ' or '\t' or '\n' or '\r' or '\f')) return false;

        int colon = selector.IndexOf(':');
        string name = (colon < 0 ? selector : selector[..colon]).Trim();
        string pseudo = colon < 0 ? "" : selector[(colon + 1)..].Trim();
        if (name.Length > 0 && !IsCssIdent(name)) return false;
        if (colon >= 0 && selector.IndexOf(':', colon + 1) >= 0) return false;
        if (pseudo.Length == 0)
        {
            normalised = name;
            return true;
        }
        if (!pseudo.Equals("left", StringComparison.OrdinalIgnoreCase) &&
            !pseudo.Equals("right", StringComparison.OrdinalIgnoreCase) &&
            !pseudo.Equals("first", StringComparison.OrdinalIgnoreCase))
            return false;
        normalised = (name.Length > 0 ? name + ":" : ":") + pseudo.ToLowerInvariant();
        return true;
    }

    private static bool IsCssIdent(string text)
    {
        if (text.Length == 0) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or '-' ||
                      (i > 0 && c is >= '0' and <= '9');
            if (!ok) return false;
        }
        return text[0] != '-' || text.Length > 1;
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
        string prelude = ConsumeAtRulePrelude(out bool unterminated);
        if (unterminated || !AtRuleBlockFollows()) return null;

        var rule = new StyleRuleScope { ScopePrelude = prelude.Trim() };
        if (!TrySplitScopePrelude(prelude, out string root, out string limit))
        {
            SkipStatementBlock();
            return null;
        }
        rule.ScopeRoot = root;
        rule.ScopeLimit = limit;

        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
            ExpectCss(CssTokenType.RightBraceToken);
        }
        return rule;
    }

    /// <summary>Read the prelude of an '@scope': one parenthesised selector, and after the word 'to'
    /// a second one. A prelude that is none of those is no scope — a bare 'div' in front of the block
    /// is not a parenthesised selector, and a string is not a selector at all (measured: each leaves
    /// no rule behind) — while no prelude at all is legal, the root being left to the block.</summary>
    private static bool TrySplitScopePrelude(string prelude, out string root, out string limit)
    {
        root = limit = "";
        var tokens = SignificantTokens(prelude);
        int i = 0;
        if (i >= tokens.Count) return true;
        if (!ReadScopeGroup(tokens, ref i, out root)) return false;
        if (i == tokens.Count) return true;
        if (!(tokens[i].Type == CssTokenType.IdentToken &&
              tokens[i].Value.Equals("to", StringComparison.OrdinalIgnoreCase))) return false;
        i++;
        if (!ReadScopeGroup(tokens, ref i, out limit)) return false;
        return i == tokens.Count;
    }

    private static bool ReadScopeGroup(List<CssParserToken> tokens, ref int i, out string group)
    {
        group = "";
        if (i >= tokens.Count || tokens[i].Type != CssTokenType.LeftParenthesisToken) return false;
        int depth = 1;
        i++;
        var text = new System.Text.StringBuilder();
        for (; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type is CssTokenType.LeftParenthesisToken or CssTokenType.FunctionToken) depth++;
            else if (token.Type == CssTokenType.RightParenthesisToken && --depth == 0) break;
            text.Append(token.ToCssText());
        }
        if (depth != 0) return false;
        i++;
        var body = text.ToString().Trim();
        // The selector inside has to start with something a selector can start with. A string, a
        // number or a parenthesis is not one, and the group is then no root.
        if (!(body.Length > 0 &&
              (char.IsLetter(body[0]) || body[0] is '-' or '_' or '*' or '|' or '.' or '#' or '[' or ':')))
            return false;
        group = body;
        return true;
    }

    /// <summary>The tokens of a piece of text with the whitespace and comments left out.</summary>
    private static List<CssParserToken> SignificantTokens(string text)
    {
        var list = new List<CssParserToken>();
        var tokenizer = new CssTokenizer(text);
        while (true)
        {
            var token = tokenizer.TokenizeSingle();
            if (token.Type == CssTokenType.EofToken) break;
            if (token.Type is CssTokenType.WhitespaceToken or CssTokenType.CommentToken) continue;
            list.Add(token);
        }
        return list;
    }

    private StyleRuleBase? ConsumePropertyRule()
    {
        SkipWhitespaceAndComments();
        string name = "";
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }
        SkipWhitespaceAndComments();

        // The name is a custom property name and the block is what carries the descriptors, so a
        // bare '@property --p;' or a '@property p { … }' makes no rule (measured).
        if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length == 2 ||
            !ExpectCss(CssTokenType.LeftBraceToken))
        {
            if (_stream.Current.Type == CssTokenType.LeftBraceToken) SkipStatementBlock();
            return null;
        }

        var rule = new StyleRuleProperty { Name = name };
        foreach (var entry in SplitDescriptorDeclarations(ConsumeBlockText()))
            rule.Descriptors[entry.Key] = entry.Value;

        // 'syntax' and 'inherits' are both required and 'initial-value' is required of any syntax
        // that does not accept everything (measured: '@property --p { syntax: "*" }' and
        // '@property --p { syntax: "x"; inherits: false }' both make no rule, while
        // '@property --r { syntax: "*"; inherits: false }' is a rule with two descriptors).
        string syntax = rule.Descriptors.GetValueOrDefault("syntax") ?? "";
        string inherits = rule.Descriptors.GetValueOrDefault("inherits") ?? "";
        bool hasInitial = rule.Descriptors.TryGetValue("initial-value", out string? initial) &&
                          !string.IsNullOrWhiteSpace(initial);
        if (!IsValidSyntaxString(syntax)) return null;
        if (!inherits.Equals("true", StringComparison.OrdinalIgnoreCase) &&
            !inherits.Equals("false", StringComparison.OrdinalIgnoreCase)) return null;
        if (syntax.Trim('"', '\'') != "*" && !hasInitial) return null;
        return rule;
    }

    /// <summary>Whether the text is a quoted CSS syntax string: '<c>type</c>' tokens and plain
    /// literals, separated by '|', with nothing left empty (CSS Property Values 5 §9.1). The value
    /// is kept as the page wrote it, so only the shape is checked here.</summary>
    private static bool IsValidSyntaxString(string quoted)
    {
        if (quoted.Length < 2) return false;
        char quote = quoted[0];
        if (quote != '"' && quote != '\'') return false;
        if (quoted[^1] != quote) return false;
        string inner = quoted[1..^1].Trim();
        if (inner.Length == 0) return false;
        if (inner == "*") return true;
        foreach (var raw in inner.Split('|'))
        {
            string token = raw.Trim();
            if (token.Length == 0) return false;
            // A syntax component is either one '<…>' type or a run of literals; a literal is a
            // plain ident or keyword, and anything else ('x', '1', ')') is not a syntax at all.
            foreach (var part in token.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part[0] == '<')
                {
                    if (part[^1] != '>' || part.Length < 3) return false;
                }
                else if (!IsCssIdent(part)) return false;
            }
        }
        return true;
    }

    private StyleRuleBase? ConsumeCounterStyleRule()
    {
        SkipWhitespaceAndComments();
        string name = "";
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }
        // The counter style is named or it is nothing, and its descriptors only live in a block
        // (measured: '@counter-style { system: cyclic }' makes no rule).
        if (name.Length == 0)
        {
            SkipStatementTail();
            return null;
        }

        var rule = new StyleRuleCounterStyle { Name = name };
        // The stream hands out whitespace tokens rather than swallowing them, so the
        // space between the name and the block opener has to be stepped over.
        SkipWhitespaceAndComments();
        if (!ExpectCss(CssTokenType.LeftBraceToken)) return null;

        // The descriptors are read as raw text, not as declarations: 'system',
        // 'symbols' and 'range' have no property id, and the declaration path would
        // fold them all into one anonymous entry that overwrites itself.
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SplitDescriptorDeclarations(ConsumeBlockText()))
            if (CounterStyleDescriptors.Contains(entry.Key)) written[entry.Key] = entry.Value;

        // 'system' is read first: it decides how many symbols a cyclic or numeric style may carry,
        // and it is where an additive list can be written inline, which the engine keeps and prints
        // as the 'additive-symbols' descriptor of its own (measured).
        string system = NormaliseCounterDescriptor("system",
            written.GetValueOrDefault("system") ?? "", "", out string inlineAdditive);
        if (system.Length > 0) rule.Descriptors["system"] = system;
        if (inlineAdditive.Length > 0) rule.Descriptors["additive-symbols"] = inlineAdditive;
        foreach (string descriptor in PrintableCounterStyleDescriptors)
        {
            if (!written.TryGetValue(descriptor, out string? raw)) continue;
            string value = NormaliseCounterDescriptor(descriptor, raw, system, out _);
            if (value.Length > 0) rule.Descriptors[descriptor] = value;
        }
        return rule;
    }

    private static readonly string[] CounterStyleDescriptors =
    {
        "system", "symbols", "additive-symbols", "negative", "prefix", "suffix", "pad", "range",
        "speak-as", "speaks-as", "fallback"
    };

    /// <summary>The order the rule prints its descriptors in. The reference engine puts
    /// 'fallback' between 'range' and 'speak-as' whichever end of the block the page wrote it at
    /// (measured), and drops it when the value is not a plain name: 'fallback: none', 'initial',
    /// a number and a quoted string each leave no descriptor behind.</summary>
    private static readonly string[] PrintableCounterStyleDescriptors =
    {
        "symbols", "additive-symbols", "negative", "prefix", "suffix", "pad", "range", "fallback",
        "speak-as"
    };

    /// <summary>Check an '@counter-style' descriptor against the grammar of CSS Counter Styles 1 §3
    /// and give back the text the rule reads as, or the empty string when the value is not one. The
    /// values are held as written — the reference engine prints the page's own spelling — with three
    /// exceptions it makes itself: a 'fixed' system with no count is 'fixed 1', an automatic
    /// 'system: auto' says nothing and is not printed, and an additive list written on the system
    /// comes back in <paramref name="inlineAdditive"/> (all measured).</summary>
    private static string NormaliseCounterDescriptor(string name, string value, string system,
                                                     out string inlineAdditive)
    {
        inlineAdditive = "";
        var parts = SplitTopLevel(value);
        switch (name)
        {
            case "system":
            {
                string keyword = parts.Count > 0 ? parts[0].ToLowerInvariant() : "";
                if (keyword is "" or "auto") return "";
                if (keyword is "cyclic" or "numeric" or "alphabetic" or "symbolic")
                    return parts.Count == 1 ? keyword : "";
                if (keyword == "fixed")
                {
                    if (parts.Count == 1) return "fixed 1";
                    return parts.Count == 2 && IsInteger(parts[1]) ? "fixed " + parts[1] : "";
                }
                if (keyword == "extends")
                    return parts.Count == 2 && IsCssIdent(parts[1]) ? "extends " + parts[1] : "";
                if (keyword == "additive")
                {
                    if (parts.Count == 1) return "additive";
                    // 'system: additive 1000 "M", 100 "C"' — the pairs belong to the additive list.
                    var pairs = new List<string>();
                    foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var items = SplitTopLevel(pair.Trim());
                        int from = items.Count > 0 && items[0].ToLowerInvariant() == "additive" ? 1 : 0;
                        if (items.Count - from is < 2 or > 3 || !IsInteger(items[from])) return "";
                        for (int i = from + 1; i < items.Count; i++)
                            if (!IsSymbol(items[i])) return "";
                        pairs.Add(string.Join(" ", items.Skip(from)));
                    }
                    inlineAdditive = string.Join(", ", pairs);
                    return "additive";
                }
                return "";
            }
            case "symbols":
            {
                if (parts.Count == 0) return "";
                // Every symbol the page wrote is taken, whatever the system: a numeric style with
                // eleven symbols is a rule the reference engine keeps and prints with all of them
                // (measured: 'numeric' with 11, with 3 and with 1 each survive as written), so no
                // count is a reason to throw the descriptor — and with it the whole style — away.
                return parts.All(IsSymbol) ? string.Join(" ", parts) : "";
            }
            case "additive-symbols":
            {
                var groups = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (groups.Length == 0) return "";
                var groups2 = new List<string>();
                foreach (var group in groups)
                {
                    var items = SplitTopLevel(group);
                    if (items.Count is < 2 or > 3 || !IsInteger(items[0])) return "";
                    for (int i = 1; i < items.Count; i++)
                        if (!IsSymbol(items[i])) return "";
                    groups2.Add(string.Join(" ", items));
                }
                return string.Join(", ", groups2);
            }
            case "negative":
                return parts.Count is 1 or 2 && parts.All(IsString) ? string.Join(" ", parts) : "";
            case "prefix" or "suffix":
                return parts.Count == 1 && IsString(parts[0]) ? parts[0] : "";
            case "pad":
            {
                if (parts.Count < 2 || parts.Count > 3 || !IsInteger(parts[0])) return "";
                return parts.Skip(1).All(IsCssIdent) ? string.Join(" ", parts) : "";
            }
            case "range":
            {
                if (parts.Count == 1 && (parts[0] == "auto" || parts[0] == "infinite")) return parts[0];
                // A lone integer is not a range: 'range: 5' makes no descriptor at all, while
                // 'range: 1 10' and 'range: a 5' are both one (measured, both).
                if (parts.Count == 2 && (IsInteger(parts[0]) || IsCssIdent(parts[0])) && IsInteger(parts[1]))
                    return string.Join(" ", parts);
                return "";
            }
            case "fallback":
                // One counter-style name and nothing else. The value is somebody else's style, so
                // the name is taken exactly as the page spelled it — a style name no descriptor of
                // its own this engine validates, which is why 'none' and the CSS-wide keywords are
                // not accepted here either (measured: 'fallback: none' reads back no descriptor).
                return parts.Count == 1 && IsCssIdent(parts[0]) &&
                    !Acrux.Core.Css.Resolver.CssPropertyTraits.IsCssWideKeyword(parts[0]) &&
                    !parts[0].Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? parts[0]
                    : "";
            case "speak-as":
                return parts.Count == 1 &&
                       parts[0].ToLowerInvariant() is "auto" or "bullets" or "numbers" or "words"
                    ? parts[0].ToLowerInvariant()
                    : "";
            default:
                return "";
        }
    }

    private static bool IsInteger(string text) =>
        text.Length > 0 && text.All(char.IsDigit);

    private static bool IsString(string text) =>
        text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0];

    private static bool IsSymbol(string text) => IsString(text) || text.Length == 1;

    /// <summary>Split descriptor text at the top level: on whitespace, keeping quoted strings and
    /// function calls whole, the way the value of a counter-style descriptor is read one token at a
    /// time.</summary>
    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (c == '\\' && i + 1 < text.Length) { current.Append(text[++i]); }
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; current.Append(c); continue; }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            if (depth == 0 && (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f'))
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    private StyleRuleBase? ConsumeViewTransitionRule()
    {
        // '@view-transition' is a descriptor block on its own (CSS View Transitions 1 §5.3). The
        // reference engine keeps it as a rule — one that reads type 0 and hands out no 'style' or
        // 'cssRules' (measured) — so the descriptors are held as written, the way an
        // '@counter-style' holds them, because 'navigation' is not a property the declaration path
        // has an id for.
        SkipWhitespaceAndComments();
        if (!ExpectCss(CssTokenType.LeftBraceToken)) return null;
        var rule = new StyleRuleViewTransition();
        foreach (var entry in SplitDescriptorDeclarations(ConsumeBlockText()))
            rule.Descriptors[entry.Key] = entry.Value;
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
        // Nothing may stand between the keyword and its block, and a word written there makes no
        // rule — the block the page wrote for it goes with it (measured: '@starting-style bogus
        // { #x { color: red } }' leaves the sheet empty). The space a page puts in front of the
        // brace is not a prelude, so it has to be stepped over before the brace can be seen.
        SkipWhitespaceAndComments();
        if (_stream.Current.Type != CssTokenType.LeftBraceToken)
        {
            SkipStatementTail();
            return null;
        }

        var rule = new StyleRuleStartingStyle();
        ExpectCss(CssTokenType.LeftBraceToken);
        ConsumeRuleList(rule.ChildRules, AllowedRulesType.NestedGroupRules);
        ExpectCss(CssTokenType.RightBraceToken);
        return rule;
    }

    private StyleRuleBase? ConsumeNamespaceRule(AllowedRulesType allowed)
    {
        SkipWhitespaceAndComments();
        string? prefix = null;
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            prefix = _stream.Current.Value;
            _stream.Next();
            SkipWhitespaceAndComments();
        }

        bool hasUrl = TryConsumeUrl(out string uri);

        // The statement ends at its ';'. A block written in its place belongs to nothing — the
        // namespace is dropped and the block goes with it (measured: '@namespace url(y) #k { … }
        // #l {}' leaves only '#l') — and a prefix with no address after it is no namespace either
        // ('@namespace p;' goes, '@namespace url();' stays with an empty one).
        bool blockFollows = false;
        bool junkFollows = false;
        while (_stream.Current.Type != CssTokenType.SemicolonToken &&
               _stream.Current.Type != CssTokenType.EofToken)
        {
            if (_stream.Current.Type == CssTokenType.LeftBraceToken) { blockFollows = true; break; }
            if (_stream.Current.Type == CssTokenType.RightBraceToken) break;  // the parent's closer
            if (_stream.Current.Type != CssTokenType.WhitespaceToken &&
                _stream.Current.Type != CssTokenType.CommentToken) junkFollows = true;
            _stream.Next();
        }
        ExpectCss(CssTokenType.SemicolonToken);
        if (blockFollows) SkipStatementBlock();

        // Nothing but the address stands between the name and the ';': 'url(n) extra' and
        // 'url(n) screen' are not namespaces either (measured: each leaves no rule behind).
        if (!hasUrl || blockFollows || junkFollows || !NamespacesAllowed(allowed)) return null;
        return new StyleRuleNamespace { Prefix = prefix, NamespaceUri = uri };
    }

    private StyleRuleBase? ConsumePositionTryRule()
    {
        SkipWhitespaceAndComments();
        string name = "";
        if (_stream.Current.Type == CssTokenType.IdentToken)
        {
            name = _stream.Current.Value;
            _stream.Next();
        }
        // A position-area name is a custom ident, and the block is the rule (measured: neither
        // '@position-try { left: 0 }' nor '@position-try p { left: 0 }' makes a rule).
        if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length == 2)
        {
            SkipStatementTail();
            return null;
        }

        SkipWhitespaceAndComments();
        if (!ExpectCss(CssTokenType.LeftBraceToken)) return null;
        var rule = new StyleRulePositionTry { Name = name, Properties = ParseDeclarationSet() };
        ExpectCss(CssTokenType.RightBraceToken);
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

        // The block is read before the rule is judged, because a refused rule still has a block to
        // consume: leaving its '{} in the stream makes the parser take that brace for the beginning
        // of the next rule, and everything after it disappears from the sheet with it.
        var rule = new StyleRule();
        if (!string.IsNullOrWhiteSpace(selectorText))
        {
            rule.Selectors.AddRange(CssSelectorParser.ParseSelectorList(selectorText));
            rule.OriginalSelectorText = selectorText;
        }

        if (ExpectCss(CssTokenType.LeftBraceToken))
        {
            _parsedProperties.Clear();
            ConsumeDeclarationList();
            rule.Properties = new CssPropertyValueSet(_context.Mode);
            foreach (var prop in _parsedProperties)
                rule.Properties.SetLonghandProperty(prop);
            ExpectCss(CssTokenType.RightBraceToken);
        }

        // A selector list the grammar refuses is not a selector, and a rule with nothing to apply to
        // is not a rule (measured: ':nth-child(abc)' and '#p, bogus!!!' each leave the reference
        // engine's sheet with one rule fewer rather than one weaker rule).
        return rule.Selectors.Count > 0 ? rule : null;
    }

    private string ConsumeSelectorText()
    {
        // The prelude of a style rule is cut out of the source and kept as the page wrote it, rather
        // than rebuilt from the tokens it passes over. A rebuilt prelude loses what a token cannot
        // hold: ':nth-child(2n+0)' arrives as the dimension '2n' followed by a *signed number*, and a
        // number serialized from its value is '0' — '2n0' is no pattern at all. That text is not
        // only what the CSSOM prints, it is what a re-match re-parses, so the loss reached matching
        // as well as the echo (measured against the reference engine through insertRule).
        int start = _stream.TokenStart;
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

            _stream.Next();
        }
        return _stream.RawRange(start, _stream.TokenStart).Trim();
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
        // A name the reference engine does not declare makes no declaration anywhere at all — not
        // in the cascade and not in the CSSOM text it would have been stored in.
        if (Acrux.Core.Css.Resolver.CssPropertyTraits.IsEngineOnlyName(propertyName))
        {
            SkipUntilSemicolon();
            return;
        }
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
        string? valueText = ConsumeDeclarationValue();
        if (valueText == null)
        {
            // The value carried a bad-url or a bad-string token, so it is the declaration that
            // goes and the block that stays.
            SkipUntilSemicolon();
            return;
        }

        if (valueText.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
        {
            important = true;
            valueText = valueText[..^"!important".Length].Trim();
        }

        // Convert to CssValue and add to parsed properties
        if (!string.IsNullOrEmpty(propertyName) && !string.IsNullOrEmpty(valueText))
        {
            var name = DeclarationName(propertyName);
            if (name != null)
            {
                // Only a block that carries nothing but descriptors validates its values as
                // descriptors: an '@page' has one ('size', decided by its own grammar above) and
                // the properties of a page box beside it.
                var value = ParseCssValue(propertyName, valueText, _descriptorsExclusive);
                if (value != null)
                {
                    _parsedProperties.Add(new CssPropertyValue(name.Value, value, important));
                }
            }
        }

        SkipWhitespaceAndComments();
        if (_stream.Current.Type == CssTokenType.SemicolonToken)
            _stream.Next();
    }

    /// <summary>The descriptors an '@page' block carries besides the properties it shares with a
    /// style rule. 'size' has a property id in this engine's table, so a style rule that writes it
    /// keeps it too — measured, '#a { size: A4 }' reads back 'size: a4'; what no style rule keeps
    /// is 'marks' and 'bleed', which have no id and so make no declaration for it.</summary>
    private static readonly HashSet<string> PageDescriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        "size",
    };

    /// <summary>The names an '@font-face' block carries. Those with a property id are the ones a
    /// style rule may carry too and keep the engine's own spelling of them; the rest — an address
    /// list, a codepoint range, a metric override — are descriptors and nothing else, so they are
    /// stored under the literal name and print under it (measured: <c>src</c> and <c>size-adjust</c>
    /// read back by name out of <c>rule.style</c>, while a stylesheet rule that writes them answers
    /// an empty one).</summary>
    private static readonly HashSet<string> FontFaceDescriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        "font-family", "src", "unicode-range", "font-variant", "font-feature-settings",
        "font-variation-settings", "font-stretch", "font-weight", "font-style", "font-display",
        "ascent-override", "descent-override", "line-gap-override", "size-adjust",
        // The three Fonts 5 additions the reference engine takes in an '@font-face' block, each
        // measured with its own grammar: 'font-size: 10px', 'font-optical-sizing: auto' and
        // 'font-variant-emoji: text' are kept there. 'font-size-adjust', 'font-named-instance' and
        // 'font-language-override' are not descriptors there and are dropped as one.
        "font-size", "font-optical-sizing", "font-variant-emoji",
    };

    /// <summary>The descriptors of an at-rule block whose value this engine reads itself: the shape
    /// a name of its own decides, rather than the grammar of a property it shares with a style rule.
    /// A name the block carries but this list does not is validated as a property and printed as
    /// written.</summary>
    private static bool TryCanonicalDescriptor(string name, string value, out string canonical)
    {
        string? result = name.ToLowerInvariant() switch
        {
            "src" => CanonicalSourceList(value),
            "unicode-range" => CanonicalUnicodeRange(value),
            // A family name that is one name. The descriptor of an '@font-face' takes a single
            // family, so the list a style rule may write — 'a, b' — names nothing here, and neither
            // does the number or the dimension that is not a name at all (measured three).
            "font-family" => IsSingleFamilyName(value) ? value : null,
            // A metric override is a percentage or the 'normal' keyword — and a math function whose
            // result is a percentage is one too, printed as written ('ascent-override: calc(50%)'
            // reads back 'calc(50%)'), while a function that can only be resolved against a length
            // is not a percentage and makes no declaration ('size-adjust: calc(100% + 1px)' reads
            // back an empty block; measured both).
            "ascent-override" or "descent-override" or "line-gap-override"
                => IsPercentageOrNormalOrMath(value, allowNormal: true) ? value : null,
            "size-adjust" => IsPercentageOrNormalOrMath(value, allowNormal: false) ? value : null,
            "font-display" => IsOneOf(value, "auto", "block", "swap", "fallback", "optional") ? value : null,
            _ => value,
        };
        canonical = result ?? "";
        return result != null;
    }

    /// <summary>Whether the value is one identifier out of the list.</summary>
    private static bool IsOneOf(string value, params string[] keywords)
    {
        for (int i = 0; i < keywords.Length; i++)
            if (string.Equals(value, keywords[i], StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsPercentage(string value) =>
        value.Length > 1 && value.EndsWith("%", StringComparison.Ordinal) &&
        double.TryParse(value[..^1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    /// <summary>A metric override takes a percentage or the 'normal' keyword — and a bare number
    /// is neither, which is why 'descent-override: 0.2' makes no declaration while
    /// 'descent-override: normal' does (measured).</summary>
    private static bool IsPercentageOrNormal(string value) =>
        IsPercentage(value) || value.Equals("normal", StringComparison.OrdinalIgnoreCase);

    /// <summary>The same, with a math function whose terms are all percentages or plain numbers
    /// allowed in a percentage's place — and 'normal' only where the descriptor names it.</summary>
    private static bool IsPercentageOrNormalOrMath(string text, bool allowNormal)
    {
        if (IsPercentage(text)) return true;
        if (text.Equals("normal", StringComparison.OrdinalIgnoreCase)) return allowNormal;
        return IsPercentageMathFunction(text);
    }

    /// <summary>A math function that can only have a percentage for a result: every term of it is a
    /// percentage or a number. 'calc(100% + 1px)' is not one, because the length has nothing to be
    /// added to until the box it will be used in is known, and a metric override is not a length
    /// the engine is going to resolve (measured: the function with a length in it makes no
    /// declaration, the one without it reads back exactly as written).</summary>
    private static bool IsPercentageMathFunction(string text)
    {
        int open = text.IndexOf('(');
        if (open <= 0 || !text.EndsWith(")", StringComparison.Ordinal)) return false;
        if (text[..open].ToLowerInvariant() is not ("calc" or "min" or "max" or "clamp")) return false;
        var terms = text[(open + 1)..^1].Split(
            new[] { ' ', '\t', '\n', ',', '+', '-', '*', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return false;
        foreach (var term in terms)
        {
            if (IsPercentage(term)) continue;
            if (double.TryParse(term, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _)) continue;
            return false;
        }
        return true;
    }

    /// <summary>One family name: a quoted string, or the sequence of identifiers a reference engine
    /// joins and prints as one string. A comma, a number or a dimension is not a name, and the
    /// descriptor that carries one is not a font (measured: 'font-family: a, b' and
    /// 'font-family: 3' both read back an empty '@font-face').</summary>
    private static bool IsSingleFamilyName(string text)
    {
        if (text.Length == 0) return false;
        if (SplitTopLevelCommas(text).Count != 1) return false;
        if (IsQuotedString(text)) return true;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!IsFamilyIdentifier(word)) return false;
        return true;
    }

    private static bool IsQuotedString(string text) =>
        text.Length >= 2 && (text[0] is '"' or '\'') && text[^1] == text[0];

    /// <summary>Whether the token is one identifier and nothing else — the shape a family name has
    /// when it is not written as a string.</summary>
    private static bool IsFamilyIdentifier(string text)
    {
        if (text.Length == 0) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool namePart = char.IsLetter(c) || char.IsDigit(c) || c is '-' or '_' or '\\' || c > '\u007F';
            if (!namePart) return false;
            if (i == 0 && char.IsDigit(c)) return false;
        }
        return true;
    }

    /// <summary>An address list: comma-separated sources, each one a 'url()' or a 'local()' with at
    /// most one 'format()' after it. A function the grammar does not name — a 'tech()', a second
    /// format, two sources with no comma between them — is not one source of a list but a list that
    /// never was, and the whole descriptor goes with it (measured all three).</summary>
    private static string? CanonicalSourceList(string value)
    {
        var items = SplitTopLevelCommas(value);
        if (items.Count == 0) return null;
        var printed = new StringBuilder(value.Length + 4);
        foreach (var item in items)
        {
            var parts = SplitTopLevel(item);
            if (parts.Count == 0 || parts.Count > 2 || !IsAddressFunction(parts[0])) return null;
            // An address has to be somewhere and a local name has to be somebody: 'url()' is a
            // source that fetches nothing the engine will name, but 'local()' names no family at
            // all and takes the descriptor with it (measured).
            if (parts[0].StartsWith("local(", StringComparison.OrdinalIgnoreCase) &&
                !NamesAFamily(parts[0])) return null;
            if (parts.Count == 2 &&
                (!parts[1].StartsWith("format(", StringComparison.OrdinalIgnoreCase) ||
                 !HasStringArgument(parts[1]))) return null;
            if (printed.Length > 0) printed.Append(", ");
            printed.Append(string.Join(" ", parts));
        }
        return printed.ToString();
    }

    private static bool IsAddressFunction(string token) =>
        token.StartsWith("url(", StringComparison.OrdinalIgnoreCase) ||
        token.StartsWith("local(", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a 'format()' names a format at all — a string or a bare word both spell the
    /// same thing, and the printed one is the string the value canonicaliser puts around it.</summary>
    private static bool HasStringArgument(string token)
    {
        int open = token.IndexOf('(');
        return open >= 0 && token.EndsWith(")", StringComparison.Ordinal) &&
               token[(open + 1)..^1].Trim().Length > 0;
    }

    /// <summary>Whether 'local()' names a family at all. The quotes an empty argument is printed
    /// with are the absence of a name rather than a name of its own, and a source that names
    /// neither a file nor a family is no source: 'src: local()' makes no descriptor (measured).</summary>
    private static bool NamesAFamily(string token)
    {
        int open = token.IndexOf('(');
        if (open < 0 || !token.EndsWith(")", StringComparison.Ordinal)) return false;
        var body = token[(open + 1)..^1].Trim();
        if (IsQuotedString(body)) body = body[1..^1];
        return body.Length > 0;
    }

    /// <summary>A codepoint range: 'U+' and hexadecimal digits, or two such numbers with a '-'
    /// between them. The letters print in upper case and the 'U' keeps its '+', so 'u+0-7f' reads
    /// back 'U+0-7F' while 'auto', a bare number, a trailing dash and a wildcard range are no range
    /// at all (measured four). The second side of a pair carries no 'U+' — the token the syntax
    /// hands over is the whole range, and this engine prints the numbers it was written with.</summary>
    private static string? CanonicalUnicodeRange(string value)
    {
        var items = SplitTopLevelCommas(value);
        if (items.Count == 0) return null;
        var printed = new StringBuilder(value.Length + 2);
        foreach (var item in items)
        {
            var text = item.Trim();
            int dash = text.IndexOf('-');
            var start = dash < 0 ? text : text[..dash];
            var end = dash < 0 ? "" : text[(dash + 1)..];
            if (!IsCodepoint(start)) return null;
            if (dash >= 0)
            {
                if (end.Length == 0 || !IsHexDigits(end)) return null;
                // 'U+41-U+42' is two ranges with a dash between them, not a range.
                if (end[0] is 'U' or 'u' && end.Length > 1 && end[1] == '+') return null;
            }
            if (printed.Length > 0) printed.Append(", ");
            printed.Append(start.ToUpperInvariant());
            if (dash >= 0) printed.Append('-').Append(end.ToUpperInvariant());
        }
        return printed.ToString();
    }

    private static bool IsCodepoint(string text)
    {
        if (text.Length < 3) return false;
        if (text[0] is not ('U' or 'u') || text[1] != '+') return false;
        return IsHexDigits(text[2..]);
    }

    private static bool IsHexDigits(string text)
    {
        if (text.Length == 0) return false;
        for (int i = 0; i < text.Length; i++)
            if (!Uri.IsHexDigit(text[i])) return false;
        return true;
    }

    /// <summary>Split a list at its top-level commas: a comma inside a function or a quoted string
    /// belongs to that component rather than to the list around it.</summary>
    private static List<string> SplitTopLevelCommas(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (c == '\\' && i + 1 < text.Length) current.Append(text[++i]);
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; current.Append(c); continue; }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            if (c == ',' && depth == 0)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        parts.Add(current.ToString());
        return parts;
    }

    /// <summary>Whether the token begins a component of its own that is glued to the one before it.
    /// A space, a comma, a parenthesis or the end of the block separates two components; anything
    /// else ran on.</summary>
    private static bool IsAttachedComponent(CssParserToken next) => next.Type is CssTokenType.IdentToken
        or CssTokenType.AtKeywordToken or CssTokenType.HashToken or CssTokenType.StringToken
        or CssTokenType.NumberToken or CssTokenType.PercentageToken or CssTokenType.DimensionToken
        or CssTokenType.FunctionToken or CssTokenType.UnicodeRangeToken;

    private string? ConsumeDeclarationValue()
    {
        var result = new System.Text.StringBuilder();
        int depth = 0;
        bool invalid = false;

        while (!_stream.Current.IsEof)
        {
            var t = _stream.Current;

            if (t.Type == CssTokenType.SemicolonToken && depth == 0)
                break;
            if (t.Type == CssTokenType.RightBraceToken && depth == 0)
                break;
            // CSS Syntax 3 §4: a bad-url or a bad-string token makes the declaration it stands in
            // invalid. The tokenizer has already swept the rest of the address, so the text that
            // would have been collected describes nothing.
            if (t.Type is CssTokenType.BadUrlToken or CssTokenType.BadStringToken)
            {
                invalid = true;
                _stream.Next();
                continue;
            }
            // A codepoint range is a component of its own. Whatever runs straight onto it is not
            // part of the range — and a descriptor whose list has a loose component in it names
            // nothing at all, so 'unicode-range: U+1??1' makes no declaration (measured: the whole
            // '@font-face' block reads back empty).
            if (t.Type == CssTokenType.UnicodeRangeToken && IsAttachedComponent(_stream.LookAhead()))
            {
                invalid = true;
                _stream.Next();
                continue;
            }
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

        return invalid ? null : result.ToString().Trim();
    }

    /// <summary>The name a declaration in the block being parsed is stored under, or null when the
    /// block does not carry that name. An at-rule block speaks its own vocabulary: a '@font-face'
    /// has a 'src' and a 'size-adjust' that are no property at all and are kept under their own
    /// spelling, and it has no place for the properties a style rule carries — not even a custom
    /// one (measured: '@font-face { --foo: bar; font-size-adjust: 90%; src: url(a) }' answers 'src'
    /// and nothing else). A style rule runs the same rule the other way: a name with no property id
    /// is somebody else's property and makes no declaration, which is why '#a { foo: bar }' and
    /// '#a { src: url(a) }' both read back as an empty rule.</summary>
    private CssPropertyName? DeclarationName(string propertyName)
    {
        var id = CssPropertyIdExtensions.FromString(propertyName);
        bool inTable = _descriptors != null && _descriptors.Contains(propertyName);
        if (_descriptors != null && _descriptorsExclusive && !inTable) return null;
        // A custom property is somebody else's property: it has no id in this engine's table and
        // must not be judged by that. It is stored under the name as written, because custom names
        // are case-sensitive — and dropping one loses every 'var()' that reads it, which is how a
        // sheet's 'padding: var(--spacing)' silently became no padding at all.
        if (propertyName.StartsWith("--", StringComparison.Ordinal))
            return new CssPropertyName(propertyName);
        // A descriptor this engine has no id for is stored under the literal name, exactly as a
        // custom property is, so the CSSOM reads 'rule.style.src' back and the rule prints it.
        if (id == CssPropertyId.Invalid)
        {
            if (inTable) return new CssPropertyName(propertyName.ToLowerInvariant());
            // The same is true of a property the reference engine declares that this engine has no
            // behaviour for. Measured over the 105 '-webkit-' names it accepts
            // (snapshots/out/_b257_edge_names.txt): 33 of them are stored under their own spelling
            // — '-webkit-box-flex: 1' prints '-webkit-box-flex: 1;', never 'box-flex' and never
            // nothing — and a declaration the block simply loses is a declaration the reference
            // engine has. Such a name has no id, so the cascade has nothing to run for it, which is
            // what the reference engine does too: the value is stored and read back, and no layout
            // changes.
            if (Acrux.Core.Css.Resolver.CssPropertyTraits.IsDeclaredProperty(propertyName))
                return new CssPropertyName(propertyName.ToLowerInvariant());
            return null;
        }
        return new CssPropertyName(id);
    }

    private CssValue? ParseCssValue(string propertyName, string valueText, bool descriptorsOnly = false)
    {
        // One spelling for every declaration. A reference engine prints the value it parsed
        // rather than the characters it was handed, whether that value is a single number or a
        // whole layer list, so the text is canonicalised first and everything below reads the
        // canonical text — otherwise 'padding: 0' takes the simple-value path and keeps the
        // bare zero, while 'margin: 0 auto' prints the '0px' the same engine prints.
        var text = Acrux.Core.Css.CssValueText.Canonicalize(valueText, propertyName.ToLowerInvariant());

        // A keyword the property owns is printed in the property's own spelling rather than the
        // one the author typed: a page size of 'A4' is 'a4', and the 'portrait' every named paper
        // already lies in is not printed at all (measured). The grammar gate in the property set
        // reads a declaration's text and lets a value that parsed into a number alone, so the page
        // size is decided here as well: 'size: 50%' says nothing about a page and makes no
        // declaration, in an '@page' and in a style rule alike (measured: '@page { size: 50% }' and
        // '#a { size: 50% }' both read back empty). A CSS-wide keyword is not a size but a place
        // for somebody else's value, and a page box takes it (measured: '@page { size: inherit }'
        // and '@page { size: initial }' both read back as written).
        if (propertyName.Equals("size", StringComparison.OrdinalIgnoreCase))
        {
            if (CssPageSizeValue.TryCanonicalize(text, out string pageSize)) text = pageSize;
            else if (!Acrux.Core.Css.Resolver.CssPropertyTraits.IsCssWideKeyword(text)) return null;
        }

        if (descriptorsOnly)
        {
            // A descriptor names a value, not a place for one to be inherited from: the CSS-wide
            // keywords of an '@font-face' make no declaration even though every one of them is a
            // legal value of the same-spelled property in a style rule (measured: '@font-face {
            // font-family: initial }' reads back an empty block). An '@page' is not so strict —
            // its one descriptor is a property elsewhere and keeps the wide keywords.
            if (CssWideKeywordParser.Parse(text) != null) return null;
            if (!TryCanonicalDescriptor(propertyName, text, out string canonical)) return null;
            text = canonical;
        }
        else
        {
            var wideKeyword = CssWideKeywordParser.Parse(text);
            if (wideKeyword != null) return wideKeyword;

            // A legacy '-webkit-' name has no id, so the grammar gate at the bottom of this
            // method — which reads a declaration through the property table — would never reach it,
            // and a value of more than one word would be stored unparsed without ever being
            // measured. Its value set is decided here instead, in the same shape the reference
            // engine refuses it in (snapshots/out/_b257_edge_legacy_grammar.txt).
            if (Acrux.Core.Css.CssValueGrammar.IsLegacyPrefixedName(propertyName.ToLowerInvariant()))
                return Acrux.Core.Css.CssValueGrammar.LegacyTextIsValidFor(propertyName, valueText.Trim())
                    ? new CssUnparsedValue(text)
                    : null;
        }

        // Simple value parsing for common types
        var parsed = TryParseSimpleValue(text);
        if (parsed != null)
        {
            // The property set runs the same grammar over what it records, but it can only read a
            // declaration's text while that text is still an identifier or a string — a value that
            // parsed into a number walks past it. So the grammar is consulted here too, where the
            // text is in hand either way (measured: '#a { font-family: 3 }' reads back an empty
            // rule, and so does '@page { size: 50% }').
            return Acrux.Core.Css.CssValueGrammar.TextIsValidFor(propertyName, text) ? parsed : null;
        }

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

    /// <summary>CSS Syntax 3 §5.2.1 "consume the prelude of an at-rule": the components run to the
    /// first '{' that is not already inside a function or a bracket. A parenthesis the page never
    /// closed therefore does not stop at the block it was meant to open — that block is part of the
    /// prelude, everything written after it goes with it, and the at-rule is dropped at the end of
    /// the sheet, taking the rules that followed it down too (measured: a sheet whose
    /// '@media (min-width: 100px' left its parenthesis open has no '@media' rule in it at all, and
    /// none of the rules after it either). 'unterminated' is that reading and the caller drops the
    /// rule on it. A media LIST read from the same text is the other case: it closes what the page
    /// left open and keeps the query, which is why 'matchMedia' answers about
    /// '(min-width: 100px)' while the cascade never sees a rule for it.
    /// <para>
    /// A ';' or a '}' at the top level ends the prelude as well, and neither of them is a block:
    /// '@media screen; #a { … }' has no media rule in it, only the style rule that followed (CSS
    /// Conditional 3 §3.1 makes '@media' a group rule, which needs a block to be a rule at all),
    /// and inside a style rule it is the parent's own '}' that ends the attempt.
    /// </para>
    /// </summary>
    private string ConsumeAtRulePrelude(out bool unterminated)
    {
        var result = new System.Text.StringBuilder();
        int depth = 0;
        while (!_stream.Current.IsEof)
        {
            var token = _stream.Current;
            if (depth == 0 && token.Type is CssTokenType.LeftBraceToken or CssTokenType.SemicolonToken
                                              or CssTokenType.RightBraceToken) break;
            switch (token.Type)
            {
                case CssTokenType.FunctionToken:
                case CssTokenType.LeftParenthesisToken:
                case CssTokenType.LeftSquareBracketToken:
                case CssTokenType.LeftBraceToken:
                    depth++;
                    break;
                case CssTokenType.RightParenthesisToken:
                case CssTokenType.RightSquareBracketToken:
                case CssTokenType.RightBraceToken:
                    if (depth > 0) depth--;
                    break;
            }
            result.Append(token.ToCssText());
            _stream.Next();
        }
        unterminated = depth > 0;
        SkipWhitespaceAndComments();
        return result.ToString();
    }

    /// <summary>Whether the prelude just read is followed by the block the at-rule needs. A ';' the
    /// page wrote in place of that block is consumed here — it belongs to the rule being dropped —
    /// while a '}' is the parent's and stays where it is for the rule list to end on.</summary>
    private bool AtRuleBlockFollows()
    {
        if (_stream.Current.Type == CssTokenType.SemicolonToken) _stream.Next();
        return _stream.Current.Type == CssTokenType.LeftBraceToken;
    }

    /// <summary>Whether an '@import' or a '@namespace' may still make a rule here: only at the top
    /// level of a sheet, and only while nothing the statement may not follow has come before it.</summary>
    private bool ImportsAllowed(AllowedRulesType allowed) =>
        allowed == AllowedRulesType.RegularRules && !_importsClosed;
    private bool NamespacesAllowed(AllowedRulesType allowed) =>
        allowed == AllowedRulesType.RegularRules && !_namespacesClosed;

    /// <summary>Throw away a statement at-rule the grammar will not take, along with the block the
    /// page wrote for it: '@import url(x.css) #i { color: red } #j { … }' loses the '#i' block with
    /// the import and still keeps '#j' (measured).</summary>
    private void SkipStatementBlock()
    {
        int depth = 0;
        while (!_stream.Current.IsEof)
        {
            var token = _stream.Current;
            if (token.Type == CssTokenType.LeftBraceToken) depth++;
            else if (token.Type == CssTokenType.RightBraceToken)
            {
                depth--;
                _stream.Next();
                if (depth <= 0) return;
                continue;
            }
            _stream.Next();
        }
    }

    /// <summary>The rest of a parenthesised group whose opening token the caller has already read,
    /// its own closer included: the tokens spelt back, which is the page's text with its comments
    /// gone and a run of spaces become one. What the grammar cannot read as a layer name is not
    /// thrown away but read on as the beginning of the medium, and it reads back as it stood —
    /// 'layer(a . b)' keeps both of its spaces while 'layer(a.)' keeps its none, and 'layer(1)' keeps
    /// its number (measured three). A group the page never closed is left open here and closed by the
    /// media serialiser, which is where every other unclosed parenthesis in a query is closed.</summary>
    private string ConsumeGroupTail()
    {
        var text = new StringBuilder();
        int depth = 1;
        while (!_stream.Current.IsEof)
        {
            var token = _stream.Current;
            bool closesGroup = false;
            if (token.Type is CssTokenType.FunctionToken or CssTokenType.LeftParenthesisToken) depth++;
            else if (token.Type == CssTokenType.RightParenthesisToken && --depth == 0) closesGroup = true;
            text.Append(closesGroup ? ")" : token.ToCssText());
            _stream.Next();
            if (closesGroup) return text.ToString();
        }
        return text.ToString();
    }

    /// <summary>Whether the text between the parentheses of a 'supports()' is a condition the grammar
    /// can read, and how far of it the read reaches. CSS Conditional Rules 4 §4 gives three shapes,
    /// and only one of them has to be understood: a group in parentheses holds whatever it holds
    /// ('(bogus)', '((color))' and even '()' are conditions, measured), a function group of its own
    /// is the same kind of thing ('selector(bogus syntax !!!)' stays a condition), and a bare
    /// declaration outside any group has to name a property this engine knows and carry a value that
    /// parses for it — which is what takes 'supports()' and 'supports(display: )' and
    /// 'supports(bogus: 1)' and 'supports(display: bogus)' down with the '@import' that carries them
    /// (measured four). Combinators join groups at one level and all of them have to be the same
    /// word; where the read stops the reference engine keeps what it has read and drops the rest, so
    /// the reached length is returned too: 'selector(a)|b' reads back 'selector(a)' and
    /// '(display: flex) (color)' reads back '(display: flex) ' with the space that stood in front of
    /// the group it stopped before (measured both), while a combinator with no group after it leaves
    /// no condition at all ('(display: flex) and' is dropped, measured).</summary>
    /// <summary>Whether a <c>@supports</c> condition is one the grammar reads: the same decision an
    /// '@supports' rule makes while a sheet is parsed, an '@import' makes for its <c>supports()</c>
    /// condition, and <c>CSS.supports()</c> makes for a script, so the three cannot answer
    /// differently (measured on 36 declarations, each through all three channels).</summary>
    public static bool SupportsConditionIsWellFormed(string? condition) =>
        SupportsConditionShapes(condition, ShapeBareDeclaration | ShapeValidateDeclaration, out int reached)
        && string.IsNullOrWhiteSpace(TailOf(condition, reached));

    /// <summary>Whether an '@supports' prelude is a condition the sheet parser keeps. This channel
    /// reads structure only: a group holds whatever the page wrote in it, and a group whose
    /// declaration the grammar would refuse still makes a rule — one that then answers no. Measured:
    /// '@supports (color)', '@supports (bogus: 1)', '@supports (width: calc(bogus))' and even
    /// '@supports ()' each keep their block in the sheet while nothing inside it applies, and
    /// '@supports display: flex' — a declaration with nothing round it — is no condition at all,
    /// nor is one the read has to leave words beside it ('@supports (display: flex) junk').</summary>
    public static bool SupportsConditionParsesAsRule(string? condition) =>
        SupportsConditionShapes(condition, ShapeAtom, out int reached)
        && string.IsNullOrWhiteSpace(TailOf(condition, reached));

    /// <summary>Whether an '@import' prelude's <c>supports( … )</c> is a condition the import keeps.
    /// The import reads structure like the rule but is the forgiving reader of the three: a bare
    /// declaration is a condition, what the read does not reach is dropped from the text instead of
    /// refusing the rule, and — measured — <c>supports((color) (display:flex))</c> and
    /// <c>supports((color:red)extra)</c> both survive while <c>supports(bogus: 1)</c> and
    /// <c>supports(--x)</c> take the import down with them.</summary>
    public static bool SupportsConditionParsesForImport(string? condition, out int reached) =>
        SupportsConditionShapes(condition,
            ShapeAtom | ShapeBareDeclaration | ShapeValidateDeclaration | ShapeTruncates, out reached);

    private const int ShapeAtom = 1;
    private const int ShapeBareDeclaration = 2;
    private const int ShapeValidateDeclaration = 4;
    private const int ShapeTruncates = 8;

    private static string TailOf(string? condition, int reached) =>
        string.IsNullOrEmpty(condition) ? "" : condition![Math.Min(reached, condition.Length)..];

    private static bool SupportsConditionShapes(string? condition, int shapes, out int reached)
    {
        reached = 0;
        if (string.IsNullOrWhiteSpace(condition)) return false;
        var tokens = ConditionTokens(condition!, out var starts, out var ends);
        if (tokens.Count == 0) return false;
        return SupportsConditionText(condition!, tokens, starts, ends, 0, tokens.Count,
            topLevel: true, shapes, out reached);
    }

    /// <summary>Whether a condition an '@supports' rule kept is one the engine answers yes to.
    /// The rule is read as structure and the condition as grammar, which is why a group the grammar
    /// refuses can sit in the sheet with nothing applying inside it: <c>@supports (color)</c>,
    /// <c>@supports (bogus: 1)</c>, <c>@supports ()</c> and <c>@supports ((color))</c> each keep
    /// their block and each answer no, and a negation of such a no is a yes
    /// (measured: <c>@supports not (color)</c> and <c>@supports (not (color))</c> both apply what
    /// is inside them). A chain is joined as it is written, and a read that cannot finish — junk
    /// beside the groups, a combinator with nothing after it — matches nothing.</summary>
    public static bool SupportsConditionMatches(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return false;
        var tokens = ConditionTokens(condition!, out var starts, out var ends);
        if (tokens.Count == 0) return false;
        return MatchesCondition(condition!, tokens, starts, ends, 0, tokens.Count);
    }

    private static bool MatchesCondition(string text, List<CssParserToken> tokens, List<int> starts,
        List<int> ends, int lo, int hi)
    {
        if (lo >= hi) return false;
        int i = lo;
        if (IsConditionWord(tokens, i, "not"))
        {
            if (++i >= hi || !IsConditionGroupStart(tokens[i])) return false;
            bool inner = MatchesGroup(text, tokens, starts, ends, i, hi);
            return SkipConditionGroup(tokens, i, hi) >= hi && !inner;
        }

        if (i < hi && IsConditionGroupStart(tokens[i]))
        {
            string? joiner = null;
            bool accumulator = false, started = false;
            while (true)
            {
                bool value = MatchesGroup(text, tokens, starts, ends, i, hi);
                accumulator = !started ? value
                    : joiner == "and" ? accumulator && value : accumulator || value;
                started = true;
                i = SkipConditionGroup(tokens, i, hi);
                if (i >= hi) return accumulator;
                bool and = IsConditionWord(tokens, i, "and");
                bool or = !and && IsConditionWord(tokens, i, "or");
                if (!and && !or) return false;      // a read that does not finish matches nothing
                joiner = and ? "and" : "or";
                if (++i >= hi || !IsConditionGroupStart(tokens[i])) return false;
            }
        }

        return MatchesDeclaration(text, tokens, starts, ends, i, hi);
    }

    private static bool MatchesGroup(string text, List<CssParserToken> tokens, List<int> starts,
        List<int> ends, int i, int hi)
    {
        int close = GroupCloseIndex(tokens, i, hi);
        int from = i + 1, to = close;
        if (tokens[i].Type == CssTokenType.FunctionToken)
        {
            // The selector atomic is the one function form with a reading here: it is yes when the
            // engine can parse the selector it carries.
            if ((tokens[i].FunctionName ?? "").Equals("selector", StringComparison.OrdinalIgnoreCase))
                return from < to && MatchesSelector(text[starts[from]..Math.Min(ends[to - 1], text.Length)]);
            return false;
        }
        if (from >= to) return false;                      // '()' is a condition that answers no
        if (tokens[from].Type == CssTokenType.IdentToken && from + 1 < to
            && tokens[from + 1].Type == CssTokenType.ColonToken)
            return MatchesDeclaration(text, tokens, starts, ends, from, to);
        if (IsConditionGroupStart(tokens[from]) || IsConditionWord(tokens, from, "not"))
            return MatchesCondition(text, tokens, starts, ends, from, to);
        return false;                                      // an atom the grammar left out answers no
    }

    /// <summary>The grammar's own answer for one declaration of a matching condition — the same
    /// predicate <c>CSS.supports()</c> is built from, so a condition the IDL calls true is a
    /// condition that matches and nothing else is.</summary>
    private static bool MatchesDeclaration(string text, List<CssParserToken> tokens,
        List<int> starts, List<int> ends, int from, int to)
    {
        if (from >= to || tokens[from].Type != CssTokenType.IdentToken) return false;
        var inner = text[starts[from]..ends[to - 1]];
        return SupportsConditionShapes(inner,
            ShapeBareDeclaration | ShapeValidateDeclaration, out int reached)
            && string.IsNullOrWhiteSpace(TailOf(inner, reached));
    }

    /// <summary>Whether the engine can read a <c>selector()</c> argument as a selector list. The
    /// reference engine answers for the selector grammar alone, and so does this: an argument it can
    /// parse is a condition that holds, whether or not any element on the page matches it.</summary>
    private static bool MatchesSelector(string argument)
    {
        try
        {
            var selectors = new List<Acrux.Core.Css.Matcher.CssSelector>();
            return Acrux.Core.Css.Matcher.CssSelectorParser.ReadSelectorList(
                new Acrux.Core.Css.Tokenizer.CssParserTokenStream(argument), selectors)
                && selectors.Count > 0;
        }
        catch { return false; }
    }

    /// <summary>Whether the tokens <c>[lo, hi)</c> of <paramref name="text"/> read as a supports
    /// condition (CSS Conditional 5 §4). The reference engine's answers split the shapes as
    /// follows, all measured (snapshots/out/_b256_edge_supports.txt): a leading <c>not</c> takes one
    /// group and never looks inside it — <c>not (bogus:1)</c> and <c>not (color:red)</c> both stand
    /// while <c>not bogus</c> and <c>not</c> fall; otherwise the text is a chain of groups joined by
    /// one repeated word, and every group is judged on its own; and what is left after the chain has
    /// been read is dropped rather than refused, unless another group stands directly behind the
    /// first with no space between them, which is refused outright
    /// (<c>(color:red)(display:flex)</c> falls, <c>(color:red)extra</c> and
    /// <c>(display: flex) (color)</c> stand). Outside any group a bare declaration is judged the same
    /// way a group judges one.</summary>
    private static bool SupportsConditionText(string text, List<CssParserToken> tokens,
        List<int> starts, List<int> ends, int lo, int hi, bool topLevel, int shapes, out int reached)
    {
        // A general-enclosed atom ('(color)', '(bogus 1)') is a condition on its own, and the
        // reference engine leaves it out of a nested condition that carries nothing else:
        // '((bogus))' falls while '((bogus) and (color))' stands (measured).
        reached = hi >= tokens.Count ? text.Length : starts[hi];
        int i = lo;
        // 'not' is a condition word only at the head of the condition: as the contents of a group
        // it is nothing the grammar has a place for (measured: 'not (color:red)' is a condition and
        // '(not (color:red))' is not).
        if ((topLevel || (shapes & ShapeAtom) != 0) && IsConditionWord(tokens, i, "not"))
        {
            if (++i >= hi || !IsConditionGroupStart(tokens[i]))
            {
                reached = lo < tokens.Count ? starts[lo] : text.Length;
                return false;
            }
            i = SkipConditionGroup(tokens, i, hi);
            reached = i >= hi ? text.Length : starts[i];
            return true;
        }

        if (i < hi && IsConditionGroupStart(tokens[i]))
        {
            string? joiner = null;
            while (true)
            {
                // Only a group that is the whole condition may be read as the general-enclosed
                // atom, which is what makes '(bogus)' stand and '((bogus))' fall (measured).
                if (!SupportsConditionGroupIsValid(text, tokens, starts, ends, i, hi, shapes))
                    return false;
                int after = SkipConditionGroup(tokens, i, hi);
                if (after >= hi) break;
                bool and = IsConditionWord(tokens, after, "and");
                bool or = !and && IsConditionWord(tokens, after, "or");
                if (!and && !or)
                {
                    // Two groups with nothing between them are not a condition; anything else
                    // ends the read and the rest of the text is dropped (both measured).
                    bool touching = starts[after] == ends[i] && IsConditionGroupStart(tokens[after]);
                    if (touching || (shapes & ShapeTruncates) == 0) return false;
                    reached = starts[after];
                    return true;
                }
                var word = and ? "and" : "or";
                if (joiner != null && joiner != word)
                {
                    if ((shapes & ShapeTruncates) == 0) return false;
                    reached = starts[after];
                    return true;
                }
                joiner = word;
                if (++after >= hi || !IsConditionGroupStart(tokens[after])) return false;
                i = after;
            }
            reached = text.Length;
            return true;
        }

        // A declaration with nothing round it is a condition for the IDL and for an '@import', and
        // for nobody else (measured: '@supports display: flex' is no rule).
        if ((shapes & ShapeBareDeclaration) == 0) return false;
        return SupportsGroupDeclarationIsValid(text, tokens, starts, ends, i, hi, shapes, out _);
    }

    /// <summary>One group of a condition: a <c>( … )</c> or a function group of its own. A function
    /// group is the atomic form, and the reference engine refuses it when its own text carries a
    /// top-level property name and colon — which takes <c>supports(color:red)</c> and
    /// <c>not(color:red)</c> down and leaves <c>selector(a)</c> and <c>selector(:hover) and
    /// (color:red)</c> standing, all measured; the <c>selector()</c> form is the selector atomic and
    /// its text is never read here. A plain group is a declaration when its contents open with an
    /// ident straight before a colon, a nested condition when they open with a parenthesis, and
    /// otherwise the general-enclosed atom, which only the whole condition may be.</summary>
    private static bool SupportsConditionGroupIsValid(string text, List<CssParserToken> tokens,
        List<int> starts, List<int> ends, int i, int hi, int shapes)
    {
        int close = GroupCloseIndex(tokens, i, hi);        // index of the group's own ')'
        int from = i + 1, to = close;                      // its contents, 'from' <= 'to'
        if (tokens[i].Type == CssTokenType.FunctionToken)
        {
            // 'selector( … )' is the selector atomic and its text is a selector, not a declaration.
            // 'supports( … )' and 'not( … )' are read as the words they spell rather than as atoms,
            // and this engine has no conditional form to put them in (measured both down).
            string fn = (tokens[i].FunctionName ?? "").ToLowerInvariant();
            if (fn is "selector" or "url") return true;
            if ((shapes & ShapeAtom) != 0) return true;
            if (fn is "supports" or "not") return false;
            return !HasTopLevelDeclaration(tokens, from, to);
        }
        // '()' and '( )' are nothing to the IDL — its reader reports a SyntaxError for them, measured
        // — and a rule the sheet keeps to the parser, which reads structure only ('@supports ()'
        // stays in the sheet with nothing applying inside it, measured).
        if (from >= to) return (shapes & ShapeAtom) != 0;

        if (tokens[from].Type == CssTokenType.IdentToken && from + 1 < to
            && tokens[from + 1].Type == CssTokenType.ColonToken)
            return SupportsGroupDeclarationIsValid(text, tokens, starts, ends, from, to, shapes, out _);

        bool operatorWord = tokens[from].Type == CssTokenType.IdentToken &&
            (IsConditionWord(tokens, from, "not") || IsConditionWord(tokens, from, "and") ||
             IsConditionWord(tokens, from, "or"));
        if (operatorWord || IsConditionGroupStart(tokens[from]))
        {
            // A nested condition has to be read whole: '((color:red) bogus)' falls (measured).
            if (!SupportsConditionText(text, tokens, starts, ends, from, to, false, shapes, out int read))
                return false;
            // The nested read has to have taken the group's last token; what it stopped before is
            // text the group's own grammar has no place for.
            return read >= ends[to - 1];
        }

        // The general-enclosed atom — '(color)', '(bogus 1)', '(-x)', '(url(a.png))', '()'. CSS
        // Conditional 5 took the shape out of the grammar the IDL reads, measured on all of them
        // as no condition; the sheet parser still keeps a rule for it, because the prelude is read
        // as structure and the condition then answers no ('@supports (color)' and
        // '@supports ((color))' both stay in the sheet, both inert).
        return (shapes & ShapeAtom) != 0;
    }

    /// <summary>Whether a group's contents are a declaration the grammar reads: the name is an
    /// ident straight before a colon that names a property the reference engine declares
    /// (snapshots/out/_b256_edge_props.txt is what decides the names, and it is what says no to
    /// <c>(bogus:1)</c>, <c>(-webkit-color:red)</c>, <c>(text-security:disc)</c> and
    /// <c>(line-clamp:2)</c> while saying yes to their <c>-webkit-</c> spellings), and the value is
    /// everything from that colon to the end of the group, which has to satisfy that property's own
    /// grammar. '!' 'important' belongs to the declaration rather than to its value: a value that
    /// carries it reads as the part in front of it stands, a '!' the word does not follow falls, and
    /// a declaration whose value would be left empty by it falls (measured four). A custom property
    /// takes any value at all. Where this engine has no grammar to decide a value from its text the
    /// value is taken as read, so a property whose spelling it cannot judge is kept.</summary>
    private static bool SupportsGroupDeclarationIsValid(string text, List<CssParserToken> tokens,
        List<int> starts, List<int> ends, int from, int to, int shapes, out int stop)
    {
        stop = to;
        // The rule channel stops here: a group that opens 'ident :' has the shape of a declaration,
        // and what is inside it is somebody else's business — the condition answers no when the
        // cascade reads it with the grammar the other two channels use.
        if ((shapes & ShapeValidateDeclaration) == 0)
            return tokens[from].Type == CssTokenType.IdentToken;
        if (tokens[from].Type != CssTokenType.IdentToken) return false;
        int colon = -1;
        for (int k = from + 1; k < to; k++)
            if (tokens[k].Type == CssTokenType.ColonToken) { colon = k; break; }
        if (colon < 0 || colon + 1 >= to) return false;

        var name = tokens[from].Value.ToLowerInvariant();
        bool custom = name.StartsWith("--", StringComparison.Ordinal);
        if (!custom && !Acrux.Core.Css.Resolver.CssPropertyTraits.IsDeclaredProperty(name))
            return false;

        var value = Acrux.Core.Css.MediaFeatureValue.StripComments(
            text[ends[colon]..ends[to - 1]]).Trim();
        // A ';' in the value is a second declaration, and a group holds one declaration
        // (measured: '(color: red; color: blue)' and '(background:red; color:blue)' are no
        // condition, while a comma belongs to the value and '(transition:all 1s, color 2s)' is one).
        if (value.IndexOf(';') >= 0) return false;
        // A value that opens with a colon is a second name and no value: '(x: :)' is no condition
        // either way, since the grammar has already lost the plot by then (measured).
        if (value.StartsWith(":", StringComparison.Ordinal)) return false;
        int bang = value.LastIndexOf('!');
        if (bang >= 0)
        {
            var after = value[(bang + 1)..].TrimStart();
            if (!after.Equals("important", StringComparison.OrdinalIgnoreCase)) return false;
            value = value[..bang].Trim();
        }
        if (value.Length == 0) return false;
        return custom || (shapes & ShapeValidateDeclaration) == 0
               || Acrux.Core.Css.CssValueGrammar.TextIsValidFor(name, value);
    }

    /// <summary>Whether the tokens hold a property name straight before a colon at this group's own
    /// depth — the shape that makes a function group a declaration rather than an atom.</summary>
    private static bool HasTopLevelDeclaration(List<CssParserToken> tokens, int from, int to)
    {
        int depth = 0;
        for (int k = from; k < to; k++)
        {
            var type = tokens[k].Type;
            if (type is CssTokenType.FunctionToken or CssTokenType.LeftParenthesisToken) depth++;
            else if (type == CssTokenType.RightParenthesisToken) depth--;
            else if (depth == 0 && type == CssTokenType.IdentToken && k + 1 < to
                     && tokens[k + 1].Type == CssTokenType.ColonToken)
                return true;
        }
        return false;
    }

    /// <summary>The index of the ')' that closes the group opening at <paramref name="i"/>, or
    /// <paramref name="hi"/> when the text never closes it.</summary>
    private static int GroupCloseIndex(List<CssParserToken> tokens, int i, int hi)
    {
        int depth = 0;
        for (; i < hi; i++)
        {
            if (tokens[i].Type is CssTokenType.FunctionToken or CssTokenType.LeftParenthesisToken) depth++;
            else if (tokens[i].Type == CssTokenType.RightParenthesisToken && --depth == 0) return i;
        }
        return hi;
    }


    private static bool IsConditionGroupStart(CssParserToken token) =>
        token.Type is CssTokenType.FunctionToken or CssTokenType.LeftParenthesisToken;

    /// <summary>The index just past the group that begins at <paramref name="i"/>, whose own contents
    /// are not examined — a group is read as far as its closer and kept as it stands.</summary>
    private static int SkipConditionGroup(List<CssParserToken> tokens, int i, int? limit = null)
    {
        int hi = limit ?? tokens.Count;
        int depth = 0;
        for (; i < hi; i++)
        {
            if (tokens[i].Type is CssTokenType.FunctionToken or CssTokenType.LeftParenthesisToken) depth++;
            else if (tokens[i].Type == CssTokenType.RightParenthesisToken && --depth == 0) return i + 1;
        }
        return hi;
    }

    private static bool IsConditionWord(List<CssParserToken> tokens, int i, string word) =>
        i < tokens.Count && tokens[i].Type == CssTokenType.IdentToken &&
        tokens[i].Value.Equals(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>The tokens of a condition with the whitespace and comments between them left out, and
    /// where each one begins and ends in the text. The beginning is the token's own first character,
    /// not the run of spaces in front of it, because the text a stopped read keeps ends before that
    /// run — and a token this engine spells differently from the page ('COLOR', '3.0') still has to be
    /// cut where the page wrote it.</summary>
    private static List<CssParserToken> ConditionTokens(string condition, out List<int> starts,
        out List<int> ends)
    {
        var tokens = new List<CssParserToken>();
        starts = new List<int>();
        ends = new List<int>();
        var tokenizer = new CssTokenizer(condition);
        while (true)
        {
            var token = tokenizer.TokenizeSingle();
            if (token.Type == CssTokenType.EofToken) break;
            if (token.Type is CssTokenType.WhitespaceToken or CssTokenType.CommentToken) continue;
            // The offset the tokenizer keeps is where the token's run began, and that run carries the
            // spaces and comments in front of the token with it, so the token's own first character
            // has to be walked out to — the text a stopped read keeps ends right before it.
            int own = tokenizer.PreviousOffset;
            while (own < condition.Length)
            {
                if (char.IsWhiteSpace(condition[own])) { own++; continue; }
                if (condition[own] == '/' && own + 1 < condition.Length && condition[own + 1] == '*')
                {
                    int close = condition.IndexOf("*/", own + 2, StringComparison.Ordinal);
                    own = close < 0 ? condition.Length : close + 2;
                    continue;
                }
                break;
            }
            tokens.Add(token);
            starts.Add(own);
            ends.Add(tokenizer.Offset);
        }
        return tokens;
    }

    /// <summary>Throw away the rest of a malformed at-rule: to its ';' (which goes with it), into
    /// its block if it has opened one, and no further than the '}' that closes whatever the rule is
    /// nested in.</summary>
    private void SkipStatementTail()
    {
        while (!_stream.Current.IsEof)
        {
            var token = _stream.Current;
            if (token.Type == CssTokenType.SemicolonToken)
            {
                _stream.Next();
                return;
            }
            if (token.Type == CssTokenType.RightBraceToken) return;   // the parent's closer
            if (token.Type == CssTokenType.LeftBraceToken)
            {
                SkipStatementBlock();
                return;
            }
            _stream.Next();
        }
    }

    /// <summary>Read a '@import' or '@namespace' address: a string, a url token, or a 'url()'
    /// function — which is taken whether or not it names anything, since '@import url();' is a rule
    /// with an empty address (measured). The bool is whether one of those spellings was there at
    /// all.</summary>
    private bool TryConsumeUrl(out string url)
    {
        url = "";
        if (_stream.Current.Type == CssTokenType.StringToken ||
            _stream.Current.Type == CssTokenType.UrlToken)
        {
            url = _stream.Current.Value;
            _stream.Next();
            return true;
        }
        if (!IsFunctionNamed(_stream.Current, "url")) return false;

        _stream.Next();
        SkipWhitespaceAndComments();
        if (_stream.Current.Type == CssTokenType.StringToken ||
            _stream.Current.Type == CssTokenType.UrlToken)
        {
            url = _stream.Current.Value;
            _stream.Next();
        }
        ExpectCss(CssTokenType.RightParenthesisToken);
        return true;
    }

    /// <summary>Parse the declaration block the stream is sitting on into its own property set.</summary>
    private CssPropertyValueSet ParseDeclarationSet()
    {
        _parsedProperties.Clear();
        ConsumeDeclarationList();
        var set = new CssPropertyValueSet(_context.Mode);
        foreach (var prop in _parsedProperties)
            set.SetLonghandProperty(prop);
        _parsedProperties.Clear();
        return set;
    }

    /// <summary>Take the declarations parsed since the given mark into their own set and drop them
    /// from the shared list, so a rule that parses several blocks in a row files each one where it
    /// belongs.</summary>
    private CssPropertyValueSet TakeParsedProperties(int from)
    {
        var set = new CssPropertyValueSet(_context.Mode);
        for (int i = from; i < _parsedProperties.Count; i++)
            set.SetLonghandProperty(_parsedProperties[i]);
        _parsedProperties.RemoveRange(from, _parsedProperties.Count - from);
        return set;
    }

    /// <summary>Whether the token is the given name on its own ('layer'), as an ident.</summary>
    private static bool IsIdentNamed(CssParserToken token, string name) =>
        token.Type == CssTokenType.IdentToken &&
        string.Equals(token.Value, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the token opens a function of the given name ('layer(', 'supports('). The
    /// tokenizer hands a function call's name and its '(' back as one token, so an at-rule component
    /// that has both spellings has to be read twice.</summary>
    private static bool IsFunctionNamed(CssParserToken token, string name) =>
        token.Type == CssTokenType.FunctionToken &&
        string.Equals(token.FunctionName, name, StringComparison.OrdinalIgnoreCase);

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