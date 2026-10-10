using System.Collections;
using Acrux.Core.Css.Rules;
using Acrux.Core.Dom;
using Acrux.Core.Performance;

namespace Acrux.Core.JavaScript;

/// <summary>
/// The CSSOM view of a stylesheet (CSSOM §5.2, HTML §4.8.6). Two kinds of sheet reach this
/// object: one an element owns, whose rule list lives on that element, and one the script
/// constructed itself (<c>new CSSStyleSheet()</c>), which the document applies only once the
/// page puts it in <c>adoptedStyleSheets</c>. Either way the rules exposed here are the very
/// list the cascade reads, so an <c>insertRule</c> changes the next recompute rather than
/// just the object a script is holding.
/// </summary>
public class CssStyleSheetHost
{
    private readonly Element? _owner;
    private readonly Func<Element, ElementHost?>? _wrap;
    private readonly StyleSheetContents? _constructed;

    public CssStyleSheetHost(Element owner, Func<Element, ElementHost?> wrap)
    {
        _owner = owner;
        _wrap = wrap;
    }

    /// <summary>Wraps a constructed sheet. The contents remember this object as their view, so
    /// a page that adopts a sheet and reads it back through the document gets the same one.</summary>
    public CssStyleSheetHost(StyleSheetContents contents)
    {
        _constructed = contents;
        contents.CssomView ??= this;
    }

    /// <summary>The view of an element's sheet, remembered on the sheet itself the same way a
    /// constructed one is. The reference engine hands out one object per sheet (measured:
    /// <c>element.sheet === element.sheet</c>, and a rule pulled out of that sheet answers a
    /// <c>parentStyleSheet</c> that is the very object the page asked twice), so every path that
    /// reaches the same sheet — the element, <c>document.styleSheets</c>, a rule's parent — has
    /// to arrive at the cached host rather than build a fresh one.</summary>
    internal static CssStyleSheetHost ViewOf(Element owner, Func<Element, ElementHost?> wrap)
    {
        var contents = owner.AssociatedStyleSheet;
        if (contents?.CssomView is CssStyleSheetHost view && view._owner == owner) return view;
        var host = new CssStyleSheetHost(owner, wrap);
        if (contents != null) contents.CssomView = host;
        return host;
    }

    private StyleSheetContents? Contents => _constructed ?? _owner?.AssociatedStyleSheet;

    /// <summary>The rule list this view stands for. The document's adopted list holds the
    /// sheet itself rather than the script-facing object, so a rule a page inserts after
    /// adoption is in the list the next recompute walks.</summary>
    internal StyleSheetContents? NativeSheet => Contents;

    /// <summary>True for a sheet the script constructed itself. Only those may be adopted or
    /// replaced wholesale (measured: putting an element's sheet in adoptedStyleSheets throws
    /// NotAllowedError, because the element already owns it).</summary>
    internal bool IsConstructed => _constructed != null;

    public object? ownerNode => _owner != null && _wrap != null ? _wrap(_owner) : null;

    public object? parentStyleSheet => null;

    public object? ownerRule => null;

    /// <summary>The legacy 'type' of the sheet. A sheet that exists at all is a CSS sheet
    /// (measured: Edge answers "text/css" for a 'style' element with no type attribute), and
    /// an element whose type is anything else never reaches this object.</summary>
    public string type => "text/css";

    public string? href => _owner != null && string.Equals(_owner.TagName, "LINK", StringComparison.OrdinalIgnoreCase)
        ? _owner.GetAttribute("href")
        : null;

    public string? title => _owner?.GetAttribute("title");

    /// <summary>'disabled' is element state on a sheet an element owns — setting it hides the
    /// sheet without leaving an attribute behind (measured: 'hasAttribute("disabled")' is still
    /// false afterwards) — and the sheet's own flag on a constructed one.</summary>
    public bool disabled
    {
        get => _owner != null ? _owner.SheetDisabled : _constructed?.Disabled ?? false;
        set
        {
            if (_owner != null) _owner.SheetDisabledState = value;
            else if (_constructed != null) _constructed.Disabled = value;
            Invalidate();
        }
    }

    private CssMediaListHost? _media;

    /// <summary>The medium the sheet applies to, as its own media list (CSSOM §5.2.1; HTML
    /// §4.8.6 seeds it from the element's 'media' attribute). An empty list is no medium at all
    /// and lets the sheet apply everywhere. It reads and writes the sheet's used value rather
    /// than the attribute, so a list a page installs through here changes the cascade and leaves
    /// <c>getAttribute("media")</c> as the page wrote it (measured). The object is kept, so
    /// asking twice gives the same list and a change to it is a change to the sheet. Assigning
    /// the property itself writes the list's 'mediaText' — [PutForwards=mediaText], measured to
    /// apply on a constructed sheet as well as on one an element owns, and it still does not
    /// touch the attribute.</summary>
    public object? media
    {
        get => _media ??= new CssMediaListHost(
            () => Contents?.MediaText ?? "",
            text => WriteMedia(text));
        set => WriteMedia(CssMediaListHost.PutForward(value));
    }

    private void WriteMedia(string text)
    {
        if (Contents != null) Contents.MediaText = text;
        Invalidate();
    }

    private object[]? _ruleHosts;

    public object[] cssRules
    {
        get
        {
            var sheet = Contents;
            if (sheet == null) return Array.Empty<object>();
            // One wrapper per rule, so the rule a page pulled out of the sheet and the rule its
            // sheet lists again are the same object (CSSOM §5.3.4 identity). The cache is thrown
            // out as soon as it no longer describes the rules the sheet holds, which a rule that
            // was replaced one-for-one would otherwise slip past.
            if (!WrappersMatch(_ruleHosts, sheet.ChildRules))
                _ruleHosts = WrapRules(sheet.ChildRules, Invalidate, this);
            return _ruleHosts!;
        }
    }

    /// <summary>Wrappers for a list of rules, each remembering where it came from so it can answer
    /// 'parentStyleSheet' and 'parentRule'.</summary>
    internal static object[] WrapRules(List<StyleRuleBase> rules, Action changed, CssStyleSheetHost? sheet,
                                       CssRuleHost? parent = null)
    {
        var hosts = new object[rules.Count];
        for (int i = 0; i < rules.Count; i++) hosts[i] = new CssRuleHost(rules[i], changed, sheet, parent);
        return hosts;
    }

    /// <summary>Whether the remembered wrappers still stand for exactly these rules, in this
    /// order — the check that keeps a rule replaced one-for-one from being read through a wrapper
    /// of the rule that used to be there.</summary>
    internal static bool WrappersMatch(object[]? hosts, List<StyleRuleBase> rules)
    {
        if (hosts == null || hosts.Length != rules.Count) return false;
        for (int i = 0; i < hosts.Length; i++)
            if (hosts[i] is not CssRuleHost host || !ReferenceEquals(host.Rule, rules[i])) return false;
        return true;
    }

    /// <summary>The sentence a refusal that is not about the text carries. An '@import' put after a
    /// style rule and an '@namespace' put in a sheet that already has one are different mistakes
    /// that read almost alike, separated by the error name and by one trailing full stop.</summary>
    private const string InsertRefusal =
        "Failed to execute 'insertRule' on 'CSSStyleSheet': Failed to insert the rule";

    /// <summary>Parses |rule| and puts it at |index| (default: the front, which is what the
    /// IDL default of 0 means), returning the index it landed at (CSSOM §5.2.4). Four refusals
    /// are errors rather than surprises, and all four leave the sheet untouched (measured):
    /// a text that is not exactly one rule is a SyntaxError — two rules, an empty text, an
    /// '@charset' and even a semicolon after a complete style rule; an index outside the list is an
    /// IndexSizeError; an '@namespace' in a sheet that holds anything but imports is an
    /// InvalidStateError; and an '@import' that lands after another import or after a namespace is a
    /// HierarchyRequestError, because the statements of a sheet have to come in that order.</summary>
    public int insertRule(string rule, int? index = null)
    {
        var sheet = WritableSheet;
        // The index is the first thing the reference engine looks at, ahead of the text: an insert
        // that is both out of range and unparseable answers IndexSizeError (measured with
        // insertRule('not a rule', 99) on an empty sheet). A negative index is the interface's
        // unsigned value, which is why -1 reads back as 4294967295 (measured).
        long at = unchecked((uint)(index ?? 0));
        if (at > sheet.ChildRules.Count)
            throw new Acrux.Core.Dom.DOMException(
                "Failed to execute 'insertRule' on 'CSSStyleSheet': The index provided (" + at
                + ") is larger than the maximum index (" + sheet.ChildRules.Count + ").",
                "IndexSizeError");
        var parsed = ParseRules(rule, sheet);
        CheckStatementOrder(sheet, parsed, (int)at);
        sheet.ChildRules.InsertRange((int)at, parsed);
        // A namespace a script inserts is a declaration the rest of the sheet has to answer to,
        // including the rules inserted afterwards (measured: an inserted '@namespace url(other)'
        // makes a later inserted '#p' stop matching an HTML element).
        if (parsed[0] is StyleRuleNamespace)
            Acrux.Core.Css.Tokenizer.CssParserImpl.ApplySheetNamespaces(sheet);
        else
            Acrux.Core.Css.Tokenizer.CssParserImpl.ApplyNamespaces(parsed, sheet);
        Invalidate();
        return (int)at;
    }

    /// <summary>Whether the two statement rules may land where the script asked. The check is on
    /// what comes before the index: an '@import' is only legal while everything ahead of it is
    /// another import, and an '@namespace' only while everything ahead of it is an import or another
    /// namespace and the sheet holds nothing that is neither.</summary>
    private static void CheckStatementOrder(StyleSheetContents sheet, List<StyleRuleBase> parsed, int at)
    {
        if (parsed[0] is not StyleRuleImport && parsed[0] is not StyleRuleNamespace) return;
        bool import = parsed[0] is StyleRuleImport;
        for (int i = 0; i < at; i++)
        {
            var before = sheet.ChildRules[i];
            if (before is StyleRuleImport) continue;
            if (before is StyleRuleNamespace && !import) continue;
            // The two refusals share their sentence but not its last character: a full stop follows
            // the '@import' one and not the '@namespace' one (measured, and read back through
            // JSON.stringify so the difference is not a rendering artefact).
            throw new Acrux.Core.Dom.DOMException(
                import ? InsertRefusal + "." : InsertRefusal,
                import ? "HierarchyRequestError" : "InvalidStateError");
        }
        if (import) return;
        // A namespace after a rule that is neither an import nor a namespace is refused even when the
        // index itself points in front of everything, which is what the measured answer says the
        // check is about — the sheet, not the position (measured: 'insertRule("@namespace …")' into a
        // sheet holding one style rule answers InvalidStateError at every index).
        foreach (var existing in sheet.ChildRules)
            if (existing is not (StyleRuleImport or StyleRuleNamespace))
                throw new Acrux.Core.Dom.DOMException(InsertRefusal, "InvalidStateError");
    }

    /// <summary>Adds a rule to the end of the sheet — the alias every script expects
    /// next to insertRule, and unlike insertRule it really does append.</summary>
    public int appendRule(string rule)
    {
        var sheet = WritableSheet;
        return insertRule(rule, sheet.ChildRules.Count);
    }

    /// <summary>Removes the rule at |index|. An index outside the list is an error rather
    /// than a no-op (CSSOM §5.2.5, measured: IndexSizeError for -1 and for one past the end).</summary>
    public void deleteRule(int index)
    {
        var sheet = WritableSheet;
        // An empty sheet says so in its own sentence; a sheet that has rules reports the largest
        // index it would have accepted, and a negative index is the interface's unsigned value
        // (all three measured: 'Style sheet is empty (length 0).', '… larger than the maximum index
        // (1).', and -1 printed as 4294967295).
        if (sheet.ChildRules.Count == 0)
            throw new Acrux.Core.Dom.DOMException(
                "Failed to execute 'deleteRule' on 'CSSStyleSheet': Style sheet is empty (length 0).",
                "IndexSizeError");
        long at = unchecked((uint)index);
        if (at > sheet.ChildRules.Count - 1)
            throw new Acrux.Core.Dom.DOMException(
                "Failed to execute 'deleteRule' on 'CSSStyleSheet': The index provided (" + at
                + ") is larger than the maximum index (" + (sheet.ChildRules.Count - 1) + ").",
                "IndexSizeError");
        sheet.ChildRules.RemoveAt((int)at);
        Invalidate();
    }

    /// <summary>CSSOM §5.2.4 restricts a wholesale replacement to a sheet the script
    /// constructed itself; a sheet an element owns is rewritten through the element, so the
    /// reference engine refuses the call (measured: NotAllowedError, "Can't call replaceSync
    /// on non-constructed CSSStyleSheets"). Unlike insertRule this is a stylesheet parse, so
    /// text the grammar rejects is not an error — it leaves the sheet holding nothing
    /// (measured: replaceSync('}}}bad{{') clears it and answers no exception).</summary>
    public void replaceSync(string text)
    {
        if (_constructed == null)
            throw new Acrux.Core.Dom.DOMException(
                "Failed to execute 'replaceSync' on 'CSSStyleSheet': "
                + "Can't call replaceSync on non-constructed CSSStyleSheets.", "NotAllowedError");
        _constructed.ChildRules.Clear();
        _constructed.AddRuleRange(ParseSheet(text));
        // A wholesale replacement takes the new text's declarations as the sheet's own, so a rule
        // inserted afterwards is settled against them and not against an empty map.
        Acrux.Core.Css.Tokenizer.CssParserImpl.ApplySheetNamespaces(_constructed);
        Invalidate();
    }

    private StyleSheetContents WritableSheet =>
        Contents ?? throw new InvalidOperationException(
            "InvalidStateError: the stylesheet has no rules to modify.");

    /// <summary>Parses a stylesheet fragment, keeping whatever the grammar accepted. Namespaces
    /// are settled against |into| — the sheet the rules are going into — because CSSOM §5.2.4
    /// resolves an inserted rule with that sheet's declarations and not with the fragment's own,
    /// which a text of one rule never carries. A whole-sheet parse passes no target and settles
    /// with the '@namespace' rules inside the text.</summary>
    private static List<StyleRuleBase> ParseSheet(string text, StyleSheetContents? into = null)
    {
        if (string.IsNullOrEmpty(text)) return new List<StyleRuleBase>();
        try
        {
            var fragment = Acrux.Core.Css.Tokenizer.CssParserImpl.ParseStyleSheetFragment(
                text, Acrux.Core.Css.Tokenizer.CssParserContext.Default());
            if (into != null)
                Acrux.Core.Css.Tokenizer.CssParserImpl.ApplyNamespaces(fragment.ChildRules, into);
            else
                Acrux.Core.Css.Tokenizer.CssParserImpl.ApplySheetNamespaces(fragment);
            return fragment.ChildRules;
        }
        catch
        {
            return new List<StyleRuleBase>();
        }
    }

    /// <summary>Parses exactly one rule for insertRule (CSSOM §5.2.4). A text the grammar reads as
    /// nothing, as more than one rule, or as one rule with something left over after it is refused
    /// with a SyntaxError — the reference engine does not keep the part it could read (measured:
    /// two rules, an empty text and a trailing semicolon each leave the sheet as it was). The
    /// sentence quotes the text the script handed in, exactly as it was written (measured for
    /// '', '@charset "utf-8";' and a rule whose selector the engine refuses).</summary>
    private static List<StyleRuleBase> ParseRules(string text, StyleSheetContents into)
    {
        if (string.IsNullOrEmpty(text)) throw RuleParseFailure(text);
        StyleRuleBase? rule = null;
        try
        {
            rule = Acrux.Core.Css.Tokenizer.CssParserImpl.ParseRuleForInsertion(
                text, Acrux.Core.Css.Tokenizer.CssParserContext.Default());
        }
        catch
        {
            rule = null;
        }
        if (rule == null) throw RuleParseFailure(text);
        var list = new List<StyleRuleBase> { rule };
        Acrux.Core.Css.Tokenizer.CssParserImpl.ApplyNamespaces(list, into);
        // A rule whose selector names a prefix the sheet never declares, or puts one in front of an
        // attribute, is not a rule at all — and through the CSSOM that reads as a refusal, while the
        // same text in a 'style' element only loses the rule (measured: an inserted 'zz|div' throws
        // and leaves the sheet as it was).
        if (list.Count == 0) throw RuleParseFailure(text);
        return list;
    }

    private static Acrux.Core.Dom.DOMException RuleParseFailure(string text) => new(
        "Failed to execute 'insertRule' on 'CSSStyleSheet': Failed to parse the rule '" + text + "'.",
        "SyntaxError");

    private void Invalidate()
    {
        if (_owner != null)
            DirtyState.AddSelf(_owner, DirtyFlags.Style | DirtyFlags.Layout | DirtyFlags.Paint);
        DomMutationTracker.Notify();
    }
}

/// <summary>A single rule of a <see cref="CssStyleSheetHost"/> (CSSOM §5.3). The text is
/// rebuilt from the parsed rule, so a serialisation the engine normalised (a colour, a
/// shorthand expanded into longhands) reads back normalised, and a group rule carries its
/// children both as nested cssRules and inside its own text.</summary>
public class CssRuleHost
{
    private readonly StyleRuleBase _rule;
    private readonly Action _changed;
    private readonly CssStyleSheetHost? _sheet;
    private readonly CssRuleHost? _parent;
    private CssMediaListHost? _media;
    private CssRuleStyleDeclaration? _style;
    private object[]? _childHosts;

    public CssRuleHost(StyleRuleBase rule, Action changed, CssStyleSheetHost? sheet = null, CssRuleHost? parent = null)
    {
        _rule = rule;
        _changed = changed;
        _sheet = sheet;
        _parent = parent;
    }

    /// <summary>The CSSOM rule-type constants (CSSOM §5.3, with the values Level 1 added for
    /// the at-rules it names).</summary>
    public int type => _rule switch
    {
        StyleRule => 1,
        StyleRuleImport => 3,
        StyleRuleMedia => 4,
        StyleRuleFontFace => 5,
        StyleRulePage => 6,
        StyleRuleKeyframes => 7,
        StyleRuleKeyframe => 8,
        // The legacy Level-1 constants, which are not the order the at-rules were introduced in:
        // '@counter-style' is 11, '@supports' 12 and '@namespace' 10 (measured; an '@layer' or
        // '@property' rule has no constant at all and answers 0).
        StyleRuleCounterStyle => 11,
        // '@font-feature-values' answers 14 and shows no body of its own, measured on
        // '@font-feature-values x { @styleset { a { font-variant: 1 } } }' — its feature-blocks are
        // not rules the object model exposes.
        StyleRuleFontFeatureValues => 14,
        StyleRuleSupports => 12,
        StyleRuleNamespace => 10,
        _ => 0,
    };

    /// <summary>The selectors of a style rule, and the one page selector of a '@page' rule
    /// (CSSOM §5.3.1, CSS Pages 3 §5.2) — a page written '@page :left' reads ':left' here, and the
    /// bare '@page' that carries no selector reads the empty string. Anything else has no
    /// selectors.</summary>
    public string? selectorText => _rule switch
    {
        StyleRule style => style.SelectorText,
        StyleRulePage page => page.SelectorText,
        _ => null,
    };

    /// <summary>The address an '@namespace' rule declares (CSS Namespaces 1 §3, CSSOM §5.3.10).
    /// A prefixless declaration reads the empty string, not a missing prefix (measured). On a
    /// '@counter-style' the same accessor name carries that rule's own 'prefix' descriptor.</summary>
    public string? namespaceURI => _rule is StyleRuleNamespace ns ? ns.NamespaceUri : null;

    public string? prefix => _rule switch
    {
        StyleRuleNamespace ns => ns.Prefix ?? "",
        StyleRuleCounterStyle counter => counter.Descriptors.GetValueOrDefault("prefix") ?? "",
        _ => null,
    };

    /// <summary>The name a '@keyframes', '@counter-style' or '@property' rule is filed under. The
    /// animation name and the counter style are written without quotes and read back that way, so
    /// <c>@keyframes "q" { … }</c> reads 'q' (measured).</summary>
    public string? name => _rule switch
    {
        StyleRuleKeyframes keyframes => keyframes.Name,
        StyleRuleCounterStyle counter => counter.Name,
        StyleRuleProperty property => property.Name,
        _ => null,
    };

    /// <summary>The keyframes an animation runs through, as the number its rule reports. '@length'
    /// and '@cssRules' both count them (measured), and the list is the written order — a rule the
    /// page repeated for the same offset is there twice.</summary>
    public int? length => _rule is StyleRuleKeyframes keyframes ? keyframes.Keyframes.Count : null;

    /// <summary>The '@import' rule's or '@media' rule's own keyframe selector (CSS Animations 1
    /// §7.3): the offsets the rule was written with, with 'from' and 'to' already folded to '0%'
    /// and '100%' — the same text the rule serialises its header from.</summary>
    public string? keyText => _rule is StyleRuleKeyframe keyframe ? keyframe.Key : null;

    /// <summary>The '@property' descriptors (CSS Property Values 5 §5.2). They are the block as the
    /// page wrote it — with 'syntax' read one step further, as the syntax string inside the quotes
    /// the page put around it (measured: a block that says 'syntax: "&lt;length&gt;"' reads
    /// '&lt;length&gt;' here while its cssText keeps the quotes) — and 'inherits' is the boolean the
    /// grammar makes of the word.</summary>
    public string? syntax
    {
        get
        {
            var quoted = Descriptor("syntax");
            if (quoted == null || quoted.Length < 2) return quoted;
            char quote = quoted[0];
            return quote != '"' && quote != '\'' || quoted[^1] != quote
                ? quoted
                : quoted[1..^1];
        }
    }

    public string? initialValue => Descriptor("initial-value");

    public bool? inherits => _rule is StyleRuleProperty property
        ? property.Descriptors.GetValueOrDefault("inherits")?.ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        }
        : null;

    /// <summary>The '@counter-style' descriptors, which the reference engine hands out one per
    /// accessor and empty for the ones the block left out (measured: a rule that says only
    /// 'system: cyclic' reads 'suffix', 'symbols' and friends as the empty string). 'speaks-as' is
    /// spelled 'speak-as' here, which is the name the IDL gives it.</summary>
    public string? system => CounterDescriptor("system");
    public string? symbols => CounterDescriptor("symbols");
    public string? additiveSymbols => CounterDescriptor("additive-symbols");
    public string? range => CounterDescriptor("range");
    public string? pad => CounterDescriptor("pad");
    public string? suffix => CounterDescriptor("suffix");
    public string? negative => CounterDescriptor("negative");
    /// <summary>The style a value outside 'range' falls back to. A block that wrote none reads the
    /// empty string (measured), and so does one that wrote a value the grammar refuses — 'none',
    /// a CSS-wide keyword, a number or a quoted string.</summary>
    public string? fallback => CounterDescriptor("fallback");
    public string? speakAs => CounterDescriptor("speak-as");

    private string? Descriptor(string name) =>
        _rule is StyleRuleProperty property ? property.Descriptors.GetValueOrDefault(name) : null;

    private string? CounterDescriptor(string name) =>
        _rule is StyleRuleCounterStyle counter
            ? counter.Descriptors.GetValueOrDefault(name) ?? ""
            : null;

    /// <summary>The address an '@import' rule names (CSSOM §5.3.11). The reference engine answers
    /// the text the rule was written with, not one it resolved against the document: measured on a
    /// file page, <c>@import url(nothere.css)</c> reads back 'nothere.css' and
    /// <c>@import url(https://example.com/x.css)</c> reads back its own absolute address. A rule
    /// that is not an '@import' has no address at all.</summary>
    public string? href => _rule is StyleRuleImport import ? import.Url : null;

    /// <summary>The layer an '@import' asks for (CSS Cascade 5 §6.1). An unnamed <c>layer</c> is the
    /// empty string and a rule with no layer at all is null, which is what the reference engine
    /// answers too (measured: <c>@import url(c.css) layer</c> reads <c>""</c> and
    /// <c>@import url(b.css) supports(display: flex)</c> reads null). The dotted name a page writes
    /// is kept whole: <c>layer(a.b)</c> reads 'a.b', because the layer name is one token here rather
    /// than a list.</summary>
    public string? layerName => _rule is StyleRuleImport import ? import.LayerNameStr : null;

    /// <summary>The <c>supports()</c> condition of an '@import' (CSS Conditional Rules 4 §4), read
    /// back exactly as the page wrote it — leading spaces gone, the rest untouched (measured:
    /// <c>supports( display :   flex )</c> reads 'display :   flex ' and
    /// <c>supports(selector(:hover))</c> reads 'selector(:hover)'). A rule without one answers null,
    /// and the condition is NOT the rule's medium: an '@import' that carries one still reads an empty
    /// media list unless a medium follows it as well.</summary>
    public string? supportsText => _rule is StyleRuleImport import ? import.SupportsCondition : null;

    /// <summary>The condition of a conditional rule (CSS Conditional Rules 3 §3.4, §5.1). A
    /// '@media' reads it back through the same serialiser a MediaQueryList's 'media' uses, so the
    /// math in it is folded and its spelling is canonical (measured: a rule written
    /// '@media (min-width: calc(446px - 2px)) and (max-width: 900px)' reads as
    /// '(min-width: calc(444px)) and (max-width: 900px)'). A '@supports' or '@container' condition
    /// is a different grammar and is returned as the engine parsed it.</summary>
    public string? conditionText => _rule switch
    {
        StyleRuleMedia media => Acrux.Core.Css.MediaFeatureValue.Canonicalize(media.ConditionText),
        StyleRuleCondition condition => condition.ConditionText,
        _ => null,
    };

    /// <summary>The media list of a conditional rule (CSS Conditional Rules 3 §4.1, §5.2): the same
    /// text 'conditionText' reads, looked at one comma-separated query at a time, and written back
    /// into the rule the cascade walks. The object is kept, so a script that asks twice gets the
    /// same list and a change to one is a change to the rule (CSSOM §5.3.10 identity). A '@media'
    /// and an '@import' carry one; an '@supports' or '@container' has no media to hand out.
    /// Assigning the property itself — <c>rule.media = 'print'</c> — writes the list's 'mediaText',
    /// which is what the IDL's [PutForwards=mediaText] means (measured, and a number written that
    /// way lands as 'not all' because '123' is not a query).</summary>
    public object? media
    {
        get => _rule switch
        {
            StyleRuleMedia media => _media ??= new CssMediaListHost(() => media.ConditionText,
                                          text => { media.ConditionText = text; _changed(); }),
            StyleRuleImport import => _media ??= new CssMediaListHost(() => import.MediaCondition ?? "",
                                          text => { import.MediaCondition = text; _changed(); }),
            _ => null,
        };
        set
        {
            var text = CssMediaListHost.PutForward(value);
            switch (_rule)
            {
                case StyleRuleMedia media: media.ConditionText = text; _changed(); break;
                case StyleRuleImport import: import.MediaCondition = text; _changed(); break;
            }
        }
    }

    /// <summary>The rule's own declarations (CSSOM §5.3.1). A style rule, an '@font-face' and a
    /// '@page' each carry a block a script can read and write (measured); a group rule is read
    /// through cssRules, and an at-rule the engine models without a declaration block — a keyframes
    /// rule, an '@import', an '@counter-style', which has its own accessors instead — answers null.
    /// Like the media list, the object is the same one every read gives.</summary>
    public CssRuleStyleDeclaration? style => _rule switch
    {
        StyleRule style => _style ??= new CssRuleStyleDeclaration(
            () => style.Properties, v => style.Properties = v, _changed),
        StyleRulePage page => _style ??= new CssRuleStyleDeclaration(
            () => page.Properties, v => page.Properties = v, _changed,
            Acrux.Core.Css.Tokenizer.CssParserImpl.PageDescriptorNames),
        StyleRuleFontFace face => _style ??= new CssRuleStyleDeclaration(
            () => face.Properties, v => face.Properties = v, _changed,
            Acrux.Core.Css.Tokenizer.CssParserImpl.FontFaceDescriptorNames, exclusive: true),
        // A keyframe of an animation and a position-area each carry a block a script can write into
        // (measured: 'keyframes.cssRules[0].style.opacity = 0' reaches the rule's own text).
        StyleRuleKeyframe keyframe => _style ??= new CssRuleStyleDeclaration(
            () => keyframe.Properties, v => keyframe.Properties = v, _changed),
        StyleRulePositionTry area => _style ??= new CssRuleStyleDeclaration(
            () => area.Properties, v => area.Properties = v, _changed),
        _ => null,
    };

    /// <summary>The sheet the rule belongs to, and the group rule that holds it — null for a rule
    /// the sheet itself lists (CSSOM §5.3.6, §5.3.7).</summary>
    public object? parentStyleSheet => _sheet;

    public object? parentRule => _parent;

    /// <summary>The rules nested in a group rule, or the keyframes of an animation; empty for
    /// anything that is not one of those (CSSOM §5.3.8, CSS Animations 1 §7.2).</summary>
    public object[] cssRules
    {
        get
        {
            var children = _rule switch
            {
                StyleRule style => style.ChildRules,
                StyleRuleGroup group => group.ChildRules,
                StyleRuleKeyframes keyframes => keyframes.Keyframes.Select<StyleRuleKeyframe, StyleRuleBase>(keyframe => keyframe).ToList(),
                _ => null,
            };
            if (children == null) return Array.Empty<object>();
            if (!CssStyleSheetHost.WrappersMatch(_childHosts, children))
                _childHosts = CssStyleSheetHost.WrapRules(children, _changed, _sheet, this);
            return _childHosts!;
        }
    }

    /// <summary>The rule this wrapper stands for, for the sheet that has to tell whether the
    /// wrappers it kept still describe the rules it holds.</summary>
    internal StyleRuleBase Rule => _rule;

    public string cssText => Serialize(_rule, "");

    /// <summary>A group serialises its children on their own lines, indented two spaces
    /// (measured: '@media screen {\n  #d3 { color: rgb(3, 3, 3); }\n}'), while a declaration
    /// block stays on one line with a semicolon after the last declaration
    /// (measured: '#q { color: rgb(8, 8, 8); }'). A '@media' header is the canonical spelling of
    /// its condition — the same text 'conditionText' and 'media.mediaText' read — because the
    /// reference engine serialises the rule and its list from one place.
    /// <para>
    /// An animation is the one block that breaks the one-line rule: its keyframes go on their own
    /// lines, and they keep the indentation of the rule itself rather than the group it sits in, so
    /// a '@keyframes' nested in an '@media' lines up with the media rather than with its siblings
    /// and closes at column zero (measured, both). An empty one still opens its block and puts the
    /// closer on the next line ('@keyframes k { \n}'), which is not the '{ }' an empty style rule
    /// or descriptor block gives.
    /// </para></summary>
    private static string Serialize(StyleRuleBase rule, string indent) => rule switch
    {
        StyleRule style => Block(style.SelectorText, style.Properties, indent),
        StyleRuleMedia media => Group(ConditionHeader("media", Acrux.Core.Css.MediaFeatureValue.Canonicalize(media.ConditionText)), media.ChildRules, indent),
        StyleRuleCondition condition => Group(ConditionHeader(ConditionKeyword(condition), condition.ConditionText), condition.ChildRules, indent),
        StyleRuleLayerBlock layer => Group($"@layer {string.Join(".", layer.LayerName)}", layer.ChildRules, indent),
        StyleRuleLayerStatement statement => $"{indent}@layer {string.Join(", ", statement.LayerNames)};",
        StyleRuleScope scope => Group(scope.ScopePrelude.Length > 0
            ? $"@scope {scope.ScopePrelude}" : "@scope", scope.ChildRules, indent),
        StyleRuleStartingStyle starting => Group("@starting-style", starting.ChildRules, indent),
        StyleRuleImport import => $"{indent}{ImportText(import)}",
        StyleRuleNamespace ns => $"{indent}@namespace{(ns.Prefix is { Length: > 0 } prefix ? $" {prefix}" : "")} url(\"{ns.NamespaceUri}\");",
        StyleRuleFontFace face => Block("@font-face", face.Properties, indent),
        StyleRulePage page => PageText(page, indent),
        StyleRuleKeyframes keyframes => KeyframesText(keyframes, indent),
        StyleRuleKeyframe keyframe => Block(keyframe.Key, keyframe.Properties, indent),
        StyleRuleProperty property => DescriptorBlock($"@property {property.Name}", Ordered(property.Descriptors, PropertyOrder), indent),
        StyleRuleCounterStyle counter => DescriptorBlock($"@counter-style {counter.Name}",
            Ordered(counter.Descriptors, CounterStyleOrder), indent),
        StyleRuleViewTransition view => DescriptorBlock("@view-transition", view.Descriptors, indent),
        StyleRulePositionTry area => Block($"@position-try {area.Name}", area.Properties, indent),
        StyleRuleFontFeatureValues features => $"{indent}@font-feature-values {features.FamilyName} {{ }}",
        // Every rule the engine models is listed above; anything else has no text to print, and it
        // is better to read an empty line than a .NET class name.
        _ => indent,
    };

    /// <summary>A header and its declaration block on one line: '{ }' when the block is empty and
    /// a semicolon after the last declaration when it is not (measured on '@font-face', '@page',
    /// a keyframe and '@position-try' alike).</summary>
    private static string Block(string header, Acrux.Core.Css.Properties.CssPropertyValueSet properties, string indent) =>
        properties.IsEmpty
            ? $"{indent}{header} {{ }}"
            : $"{indent}{header} {{ {properties.AsText()}; }}";

    /// <summary>A descriptor block, printed in the order the reference engine keeps its descriptors
    /// in rather than the order the page wrote them (measured: '@property' written
    /// 'syntax / initial-value / inherits' reads back 'syntax, inherits, initial-value', and
    /// '@counter-style' prints system, symbols, additive-symbols, negative, prefix, suffix, pad,
    /// range, speak-as whichever way the block was ordered).</summary>
    private static string DescriptorBlock(string header, IEnumerable<KeyValuePair<string, string>> entries,
                                        string indent)
    {
        var list = entries.Select(entry => $"{entry.Key}: {entry.Value}").ToList();
        return list.Count == 0
            ? $"{indent}{header} {{ }}"
            : $"{indent}{header} {{ {string.Join("; ", list)}; }}";
    }

    private static IEnumerable<KeyValuePair<string, string>> Ordered(
        Dictionary<string, string> descriptors, string[] order) =>
        order.Where(name => descriptors.ContainsKey(name))
             .Select(name => new KeyValuePair<string, string>(name, descriptors[name]));

    private static readonly string[] PropertyOrder = { "syntax", "inherits", "initial-value" };

    /// <summary>The order the reference engine prints an '@counter-style' block's descriptors in,
    /// whatever order the page wrote them: 'fallback' sits between 'range' and 'speak-as' (measured).</summary>
    private static readonly string[] CounterStyleOrder =
    {
        "system", "symbols", "additive-symbols", "negative", "prefix", "suffix", "pad", "range",
        "fallback", "speak-as"
    };

    /// <summary>A '@page': its own declarations first, then the page-margin rules nested in it,
    /// each printed as a block of its own and separated by a space (measured:
    /// '@page { margin: 1cm; @top-left { content: "x"; } }' — the semicolon belongs to the last
    /// declaration, and the margin rules take the order the page wrote rather than the order the
    /// declarations came in).</summary>
    private static string PageText(StyleRulePage page, string indent)
    {
        var header = "@page" + (page.SelectorText.Length > 0 ? " " + page.SelectorText : "");
        string declarations = page.Properties.IsEmpty ? "" : page.Properties.AsText() + ";";
        var margins = page.MarginRules
            .Select(margin => Block("@" + margin.MarginId, margin.Properties, "").TrimStart())
            .ToList();
        string body = margins.Count == 0
            ? declarations
            : declarations.Length > 0
                ? declarations + " " + string.Join(" ", margins)
                : string.Join(" ", margins);
        return body.Length == 0
            ? $"{indent}{header} {{ }}"
            : $"{indent}{header} {{ {body} }}";
    }

    private static string KeyframesText(StyleRuleKeyframes keyframes, string indent)
    {
        if (keyframes.Keyframes.Count == 0) return $"{indent}@keyframes {keyframes.Name} {{ \n}}";
        var inner = string.Join("\n", keyframes.Keyframes.Select(keyframe => Serialize(keyframe, indent + "  ")));
        return $"{indent}@keyframes {keyframes.Name} {{ \n{inner}\n}}";
    }

    /// <summary>An '@import' on one line (CSSOM §5.3.11). The address is always printed in the
    /// <c>url("")</c> form even when the page wrote a bare string (measured:
    /// <c>@import "str.css" print</c> reads back <c>@import url("str.css") print;</c>), the layer
    /// keeps its parentheses only when it has a name (a plain <c>layer</c> prints as one word), and
    /// the medium is the same canonical text the rule's media list reads — so
    /// <c>@import url(d.css) PRINT, Print</c> prints 'print, print' and a medium that is not a query
    /// at all prints as the query the grammar makes of it, 'not all' (measured).</summary>
    private static string ImportText(StyleRuleImport import)
    {
        var text = $"@import url(\"{import.Url}\")";
        if (import.LayerNameStr != null)
            text += import.LayerNameStr.Length == 0 ? " layer" : $" layer({import.LayerNameStr})";
        if (!string.IsNullOrEmpty(import.SupportsCondition))
            text += $" supports({import.SupportsCondition})";
        var medium = Acrux.Core.Css.MediaFeatureValue.Canonicalize(import.MediaCondition);
        if (medium.Length > 0) text += " " + medium;
        return text + ";";
    }

    private static string ConditionKeyword(StyleRuleCondition condition) => condition switch
    {
        StyleRuleMedia => "media",
        StyleRuleSupports => "supports",
        StyleRuleContainer => "container",
        _ => "media",
    };

    /// <summary>The at-rule keyword and its condition, with the space between them even when the
    /// condition is empty — a '@media' whose list was emptied still prints one (measured: a rule
    /// reduced to no media at all serialises as '@media  {' with the gap the block's own brace
    /// adds to it).</summary>
    private static string ConditionHeader(string keyword, string condition) => $"@{keyword} {condition}";

    /// <summary>A group's children, measured three levels deep (g02/g15/g22 of the reference run):
    /// each one goes on a line of its own with two spaces in front of it, and those spaces belong to
    /// the line rather than to the child — a '@media' inside an '@media' still prints its own
    /// children two spaces in and its own closer at column zero, so the second level lines up with
    /// the first instead of running after it. An empty group opens its block and puts the closer on
    /// the next line ('@media screen {\n}'), which like '@keyframes' is not the '{ }' an empty style
    /// rule gives.</summary>
    private static string Group(string header, List<StyleRuleBase> children, string indent)
    {
        if (children.Count == 0) return $"{indent}{header} {{\n{indent}}}";
        var inner = string.Join("\n", children.Select(child => "  " + Serialize(child, "")));
        return $"{indent}{header} {{\n{inner}\n{indent}}}";
    }
}

/// <summary>The declaration block of a style rule (CSSOM §5.3.1) — the path scripts mutate a
/// stylesheet through most: <c>sheet.cssRules[0].style.color = 'red'</c>. It writes into the
/// very property set the cascade reads, so the change is applied on the next recompute instead
/// of living only in the object the script holds. A name is parsed as a one-declaration block,
/// which is also what expands a shorthand into its longhands and drops a value whose grammar
/// rejects it.</summary>
public class CssRuleStyleDeclaration : CssStyleDeclarationBase
{
    private readonly Func<Acrux.Core.Css.Properties.CssPropertyValueSet> _get;
    private readonly Action<Acrux.Core.Css.Properties.CssPropertyValueSet> _set;
    private readonly Action _changed;
    private readonly HashSet<string>? _descriptors;
    private readonly bool _descriptorsOnly;

    /// <summary>Wraps the declaration block of whatever rule carries one — a style rule, an
    /// '@font-face' or a '@page' all have a 'style' and the reference engine lets a script write
    /// through each of them (measured) — so the block is read and replaced through the owner rather
    /// than being typed to one rule class. The at-rule's descriptor vocabulary comes along with it:
    /// a name that is a descriptor of this block is a property name nowhere else.</summary>
    public CssRuleStyleDeclaration(Func<Acrux.Core.Css.Properties.CssPropertyValueSet> get, Action<Acrux.Core.Css.Properties.CssPropertyValueSet> set, Action changed,
        HashSet<string>? descriptors = null, bool exclusive = false)
    {
        _get = get;
        _set = set;
        _changed = changed;
        _descriptors = descriptors;
        _descriptorsOnly = exclusive;
    }

    private Acrux.Core.Css.Properties.CssPropertyValueSet Props => _get();

    public string cssText
    {
        // Every declaration carries its own terminator, the last one included, so the text ends with
        // a ';' whenever it holds anything at all and reads back empty when it holds nothing
        // (measured on an inline attribute, a style rule, an '@page' and an '@font-face' alike).
        get => Props.IsEmpty ? "" : Props.AsText() + ";";
        set
        {
            var parsed = ParseBlock(value);
            if (parsed == null) return;
            _set(parsed);
            _changed();
        }
    }

    public int length => Props.PropertyCount;

    /// <summary>An index past the end reads back the empty string, not null (CSSOM §2.1,
    /// measured: item(9) on a two-declaration rule answers "").</summary>
    public string item(int index) =>
        index < 0 || index >= length ? "" : Props.PropertyAt(index).Name.ToCssString();

    public string getPropertyValue(string name) => GetStyle(name) ?? "";

    public string getPropertyPriority(string name)
    {
        if (string.IsNullOrEmpty(name) || IsCustomName(name)) return "";
        var id = CssPropertyIdFromName(name);
        return id != Acrux.Core.Css.Properties.CssPropertyId.Invalid && Props.PropertyIsImportant(id)
            ? "important"
            : "";
    }

    public void setProperty(string name, string value, string? priority = null)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (string.IsNullOrEmpty(value)) { removeProperty(name); return; }
        var important = string.Equals(priority, "important", StringComparison.OrdinalIgnoreCase) ? " !important" : "";
        var parsed = ParseBlock($"{name}: {value}{important}");
        if (parsed == null || parsed.IsEmpty) return;
        foreach (var property in parsed.Properties)
            Props.SetLonghandProperty(property);
        _changed();
    }

    /// <summary>Removes the declaration and returns the value it carried, or the empty string
    /// when there was nothing to remove (CSSOM §2.1).</summary>
    public string removeProperty(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var old = getPropertyValue(name);
        bool removed;
        if (IsCustomName(name)) removed = Props.RemoveProperty(name);
        else
        {
            var id = CssPropertyIdFromName(name);
            // The same two spellings the read has: an at-rule descriptor is stored under its own
            // name rather than under a property id, and removing it has to find that.
            removed = id != Acrux.Core.Css.Properties.CssPropertyId.Invalid
                ? Props.RemoveProperty(id)
                : Props.RemoveProperty(name);
        }
        if (removed) _changed();
        return old;
    }

    protected override string? GetStyle(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        if (IsCustomName(name)) return Props.GetPropertyCssValue(name)?.CssText() ?? "";
        var id = CssPropertyIdFromName(name);
        // A name with no property id is either nobody's property or a descriptor this block stores
        // under its own spelling — an '@font-face' 'src' is one — so it is looked up as the name it
        // is written by (measured: 'fontFaceRule.style.src' and 'getPropertyValue("src")' both
        // answer the address list).
        var direct = id == Acrux.Core.Css.Properties.CssPropertyId.Invalid
            ? Props.GetPropertyCssValue(name)?.CssText() ?? ""
            : Props.GetPropertyValue(id);
        if (!string.IsNullOrEmpty(direct)) return direct;
        // The same longhand view the inline style gives: a rule that says 'border: 2px solid
        // blue' reports 'borderTopWidth' as '2px' (CSSOM §5.3.1, measured). A block that
        // carries neither still answers the empty string, never null.
        return ReadThroughShorthand(name, shorthand =>
        {
            var shorthandId = CssPropertyIdFromName(shorthand);
            return shorthandId == Acrux.Core.Css.Properties.CssPropertyId.Invalid
                ? null
                : Props.GetPropertyValue(shorthandId);
        }) ?? "";
    }

    protected override void SetStyle(string name, string? value) => setProperty(name, value ?? "");

    private static Acrux.Core.Css.Properties.CssPropertyId CssPropertyIdFromName(string name) =>
        Acrux.Core.Css.Properties.CssPropertyIdExtensions.FromString(name);

    private static bool IsCustomName(string name) =>
        name.Length > 2 && name[0] == '-' && name[1] == '-';

    private Acrux.Core.Css.Properties.CssPropertyValueSet? ParseBlock(string text)
    {
        try
        {
            return Acrux.Core.Css.Tokenizer.CssParserImpl.ParseDeclarationBlock(
                text, Acrux.Core.Css.Tokenizer.CssParserContext.Default(), _descriptors, _descriptorsOnly);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>The media list of a conditional rule (CSS Conditional Rules 3 §4.1, exposed as a
/// '@media' rule's 'media' and as a style sheet's own). It is a live view of the text its owner
/// carries: the items are that text split at the top level on commas and re-printed by the
/// serialiser 'conditionText' uses, and every write — 'mediaText = …', 'appendMedium',
/// 'deleteMedium' — puts the new text back on the object the cascade walks, so the change is
/// applied on the next recompute instead of living only in the object the script holds. An empty
/// list is not a syntax error but a rule that applies to everything.</summary>
public class CssMediaListHost : IList
{
    private readonly Func<string> _read;
    private readonly Action<string> _write;

    public CssMediaListHost(Func<string> read, Action<string> write)
    {
        _read = read;
        _write = write;
    }

    private string Text => Acrux.Core.Css.MediaFeatureValue.Canonicalize(_read());

    /// <summary>The queries as the serialiser prints them. A stray comma is not dropped here: an
    /// empty query is the query the grammar makes of an absent medium, so it reads as 'not all' and
    /// is counted (measured: 'mediaText = ",,screen,,"' leaves five items).</summary>
    private List<string> Items => Acrux.Core.Css.MediaQueryEvaluator.SplitQueryList(Text);

    public string mediaText
    {
        get => Text;
        set
        {
            // Stored as it is printed, so the rule, its 'conditionText' and the cascade read one
            // spelling of the condition. Duplicates survive the write: the list is a list and not a
            // set (measured: 'mediaText = "screen, print, print"' leaves three items).
            _write(Acrux.Core.Css.MediaFeatureValue.Canonicalize(value));
        }
    }

    public int length => Items.Count;

    /// <summary>The query at <paramref name="index"/>, or null past the end of the list — which is
    /// what the legacy getter answers, while a plain 'list[i]' there reads undefined.</summary>
    public string? item(int index) => index >= 0 && index < Items.Count ? Items[index] : null;

    /// <summary>Appends one query. A medium that is empty, or that parses to more than one query, is
    /// not added at all, and one the list already holds is not added twice (measured: appending
    /// 'aural, embossed' to 'screen, print' leaves the list as it was, and so does appending 'PRINT'
    /// to a list holding 'print', while appending 'null' adds an item called 'null' — the argument is
    /// a DOMString, so the null a script passes is the word rather than the absence).</summary>
    public void appendMedium(string? medium)
    {
        var one = Acrux.Core.Css.MediaFeatureValue.Canonicalize(medium ?? "null");
        if (one.Length == 0) return;
        if (Acrux.Core.Css.MediaQueryEvaluator.SplitQueryList(one).Count != 1) return;
        var items = Items;
        if (items.Contains(one, StringComparer.Ordinal)) return;
        items.Add(one);
        _write(string.Join(", ", items));
    }

    /// <summary>Removes every item the query matches — a list holding 'print' twice loses both — and
    /// refuses when nothing matches (measured: the delete then throws a NotFoundError, while deleting
    /// an empty medium is nothing to do and stays silent). Two queries are the same when the text
    /// they print as is, which is why '050px' and '50PX' delete a stored '50px' and why a folded
    /// 'calc(444px)' deletes a stored 'calc(446px - 2px)' but a plain '444px' does not.</summary>
    public void deleteMedium(string? medium)
    {
        var one = Acrux.Core.Css.MediaFeatureValue.Canonicalize(medium ?? "null");
        var items = Items;
        var kept = items.Where(item => !string.Equals(item, one, StringComparison.Ordinal)).ToList();
        if (kept.Count == items.Count)
        {
            if (one.Length == 0) return;
            throw new DOMException($"Failed to delete '{medium}'.", "NotFoundError");
        }
        _write(string.Join(", ", kept));
    }

    /// <summary>The list coerced to a string is its text (measured: a MediaList in a template
    /// literal prints 'screen, print', not a class name). The script engine reaches that text two
    /// ways — through the 'toString' member for a concatenation, through the target's own
    /// ToString for 'String(list)' — so both answer it.</summary>
    public string toString() => Text;

    public override string ToString() => Text;

    /// <summary>The text one write through a <c>[PutForwards=mediaText]</c> assignment stands
    /// for. The attribute the IDL forwards to is declared
    /// <c>[LegacyNullToEmptyString] DOMString mediaText</c>, so a null or undefined written to the
    /// list is the EMPTY medium and not the word 'null' (measured: <c>rule.media = null</c> leaves
    /// an empty list of no items), while any other value is the string a script would print for
    /// it. Whether that string is a query the grammar accepts is the setter's problem, and an
    /// unparsable one becomes 'not all' (measured: <c>sheet.media = 123</c>).</summary>
    internal static string PutForward(object? value) => value switch
    {
        null => "",
        string s => s,
        CssMediaListHost list => list.mediaText,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>The DOMString a plain IDL attribute of that type holds for the value a script
    /// wrote. Web IDL converts a DOMString argument by printing it, so <c>null</c> is the word
    /// 'null' rather than the absence of a value — which is exactly what a reflected content
    /// attribute then carries (measured: <c>style.media = null</c> leaves
    /// <c>getAttribute('media') === 'null'</c>, while a list written through
    /// <c>[PutForwards=mediaText]</c> takes the same null as the EMPTY medium because that
    /// attribute is <c>[LegacyNullToEmptyString]</c>; see <see cref="PutForward"/>).</summary>
    internal static string DomString(object? value) => value switch
    {
        null => "null",
        string s => s,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    // The list is read through the index as well as through 'item()', so it is presented as an
    // IList — which is what makes a script engine give it numeric property access. The mutating
    // members of that interface stay refused: a MediaList is changed through its own three methods.
    public object? this[int index] { get => item(index); set => throw new NotSupportedException(); }
    public int Count => Items.Count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public IEnumerator GetEnumerator() => Items.GetEnumerator();
    public void CopyTo(Array array, int index) => Items.ToArray().CopyTo(array, index);
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => value is string s && Items.Contains(s, StringComparer.Ordinal);
    public int IndexOf(object? value) => value is string s ? Items.FindIndex(m => m.Equals(s, StringComparison.Ordinal)) : -1;
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
