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

    /// <summary>'media' as written on the element. An empty list means the sheet applies to
    /// every medium; a constructed sheet has no medium to carry.</summary>
    public string media => _owner?.GetAttribute("media") ?? "";

    public object[] cssRules
    {
        get
        {
            var sheet = Contents;
            if (sheet == null) return Array.Empty<object>();
            var rules = new List<object>(sheet.ChildRules.Count);
            foreach (var rule in sheet.ChildRules)
                rules.Add(new CssRuleHost(rule, Invalidate));
            return rules.ToArray();
        }
    }

    /// <summary>Parses |rule| and puts it at |index| (default: the front, which is what the
    /// IDL default of 0 means), returning the index it landed at (CSSOM §5.2.4). Two refusals
    /// are errors rather than surprises: a text the grammar rejects is a SyntaxError, and an
    /// index past the end of the list is an IndexSizeError (measured: "larger than the maximum
    /// index", where the maximum is the number of rules — so inserting at the end appends).</summary>
    public int insertRule(string rule, int? index = null)
    {
        var sheet = WritableSheet;
        // The spec resolves the text first, so a script's typo in the rule reads as a syntax
        // problem and not as an index problem.
        var parsed = ParseRules(rule);
        var at = index ?? 0;
        if (at < 0 || at > sheet.ChildRules.Count)
            throw new InvalidOperationException(
                $"IndexSizeError: the index provided ({at}) is larger than the maximum index ({sheet.ChildRules.Count}).");
        sheet.ChildRules.InsertRange(at, parsed);
        Invalidate();
        return at;
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
        if (index < 0 || index >= sheet.ChildRules.Count)
            throw new InvalidOperationException("IndexSizeError: the rule index is outside the sheet.");
        sheet.ChildRules.RemoveAt(index);
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
            throw new InvalidOperationException(
                "NotAllowedError: Can't call replaceSync on non-constructed CSSStyleSheets.");
        _constructed.ChildRules.Clear();
        _constructed.AddRuleRange(ParseSheet(text));
        Invalidate();
    }

    private StyleSheetContents WritableSheet =>
        Contents ?? throw new InvalidOperationException(
            "InvalidStateError: the stylesheet has no rules to modify.");

    /// <summary>Parses a stylesheet fragment, keeping whatever the grammar accepted.</summary>
    private static List<StyleRuleBase> ParseSheet(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<StyleRuleBase>();
        try
        {
            return new Acrux.Core.Css.CssParser().Parse(text).ModernContents?.ChildRules
                   ?? new List<StyleRuleBase>();
        }
        catch
        {
            return new List<StyleRuleBase>();
        }
    }

    /// <summary>Parses one rule for insertRule. A text that yields no rule is refused rather
    /// than quietly adding nothing, which is what a browser does with unvalidated input.</summary>
    private static List<StyleRuleBase> ParseRules(string text)
    {
        var rules = ParseSheet(text);
        if (rules.Count == 0) throw new InvalidOperationException("SyntaxError: the rule text parsed to nothing.");
        return rules;
    }

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

    public CssRuleHost(StyleRuleBase rule, Action changed)
    {
        _rule = rule;
        _changed = changed;
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
        StyleRuleCounterStyle => 12,
        StyleRuleSupports => 13,
        _ => 0,
    };

    public string? selectorText => _rule is StyleRule style ? style.SelectorText : null;

    /// <summary>The rule's own declarations (CSSOM §5.3.1). Only a style rule has them; a
    /// group rule is read through cssRules, and an at-rule the engine models without a
    /// declaration block (a keyframes rule, say) answers null.</summary>
    public CssRuleStyleDeclaration? style =>
        _rule is StyleRule style ? new CssRuleStyleDeclaration(style, _changed) : null;

    /// <summary>The rules nested in a group rule; empty for anything that is not a group
    /// (CSSOM §5.3.8).</summary>
    public object[] cssRules
    {
        get
        {
            var children = _rule switch
            {
                StyleRule style => style.ChildRules,
                StyleRuleGroup group => group.ChildRules,
                _ => null,
            };
            if (children == null) return Array.Empty<object>();
            var rules = new List<object>(children.Count);
            foreach (var child in children) rules.Add(new CssRuleHost(child, _changed));
            return rules.ToArray();
        }
    }

    public string cssText => Serialize(_rule, "");

    /// <summary>A group serialises its children on their own lines, indented two spaces
    /// (measured: '@media screen {\n  #d3 { color: rgb(3, 3, 3); }\n}'), while a declaration
    /// block stays on one line with a semicolon after the last declaration
    /// (measured: '#q { color: rgb(8, 8, 8); }').</summary>
    private static string Serialize(StyleRuleBase rule, string indent) => rule switch
    {
        StyleRule style => style.Properties.IsEmpty
            ? $"{indent}{style.SelectorText} {{ }}"
            : $"{indent}{style.SelectorText} {{ {style.Properties.AsText()}; }}",
        StyleRuleCondition condition => Group($"@{ConditionKeyword(condition)} {condition.ConditionText}", condition.ChildRules, indent),
        StyleRuleLayerBlock layer => Group($"@layer {string.Join(".", layer.LayerName)}", layer.ChildRules, indent),
        StyleRuleScope scope => Group($"@scope ({scope.ScopeRoot})", scope.ChildRules, indent),
        StyleRuleStartingStyle starting => Group("@starting-style", starting.ChildRules, indent),
        _ => $"{indent}{rule.ToString() ?? ""}",
    };

    private static string ConditionKeyword(StyleRuleCondition condition) => condition switch
    {
        StyleRuleMedia => "media",
        StyleRuleSupports => "supports",
        StyleRuleContainer => "container",
        _ => "media",
    };

    private static string Group(string header, List<StyleRuleBase> children, string indent)
    {
        if (children.Count == 0) return $"{indent}{header} {{ }}";
        var inner = string.Join("\n", children.Select(child => Serialize(child, indent + "  ")));
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
    private readonly StyleRule _rule;
    private readonly Action _changed;

    public CssRuleStyleDeclaration(StyleRule rule, Action changed)
    {
        _rule = rule;
        _changed = changed;
    }

    public string cssText
    {
        get => _rule.Properties.AsText();
        set
        {
            var parsed = ParseBlock(value);
            if (parsed == null) return;
            _rule.Properties = parsed;
            _changed();
        }
    }

    public int length => _rule.Properties.PropertyCount;

    /// <summary>An index past the end reads back the empty string, not null (CSSOM §2.1,
    /// measured: item(9) on a two-declaration rule answers "").</summary>
    public string item(int index) =>
        index < 0 || index >= length ? "" : _rule.Properties.PropertyAt(index).Name.ToCssString();

    public string getPropertyValue(string name) => GetStyle(name) ?? "";

    public string getPropertyPriority(string name)
    {
        if (string.IsNullOrEmpty(name) || IsCustomName(name)) return "";
        var id = CssPropertyIdFromName(name);
        return id != Acrux.Core.Css.Properties.CssPropertyId.Invalid && _rule.Properties.PropertyIsImportant(id)
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
            _rule.Properties.SetLonghandProperty(property);
        _changed();
    }

    /// <summary>Removes the declaration and returns the value it carried, or the empty string
    /// when there was nothing to remove (CSSOM §2.1).</summary>
    public string removeProperty(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var old = getPropertyValue(name);
        bool removed;
        if (IsCustomName(name)) removed = _rule.Properties.RemoveProperty(name);
        else
        {
            var id = CssPropertyIdFromName(name);
            removed = id != Acrux.Core.Css.Properties.CssPropertyId.Invalid && _rule.Properties.RemoveProperty(id);
        }
        if (removed) _changed();
        return old;
    }

    protected override string? GetStyle(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        if (IsCustomName(name)) return _rule.Properties.GetPropertyCssValue(name)?.CssText() ?? "";
        var id = CssPropertyIdFromName(name);
        var direct = id == Acrux.Core.Css.Properties.CssPropertyId.Invalid
            ? ""
            : _rule.Properties.GetPropertyValue(id);
        if (!string.IsNullOrEmpty(direct)) return direct;
        // The same longhand view the inline style gives: a rule that says 'border: 2px solid
        // blue' reports 'borderTopWidth' as '2px' (CSSOM §5.3.1, measured). A block that
        // carries neither still answers the empty string, never null.
        return ReadThroughShorthand(name, shorthand =>
        {
            var shorthandId = CssPropertyIdFromName(shorthand);
            return shorthandId == Acrux.Core.Css.Properties.CssPropertyId.Invalid
                ? null
                : _rule.Properties.GetPropertyValue(shorthandId);
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
                text, Acrux.Core.Css.Tokenizer.CssParserContext.Default());
        }
        catch
        {
            return null;
        }
    }
}
