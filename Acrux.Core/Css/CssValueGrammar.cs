using Acrux.Core.Css.Properties;
using Acrux.Core.Css.Resolver;
using Acrux.Core.Css.Values;
using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// The value grammars that are checked when a declaration is <em>recorded</em>, not when it is
/// applied.
/// <para>
/// CSS Values 3 §3.1 drops a declaration whose value the property's grammar rejects. Dropping
/// it only at application time still lets the declaration win its place in the cascade, so the
/// next-lower rule never gets to speak again: 'display: garbage' on a span used to leave the
/// box at the engine's block default instead of the UA stylesheet's 'inline', because the
/// invalid author declaration had already beaten the UA rule and then applied nothing. The same
/// rule is what makes 'font-size: nonsense' a declaration that never enters the property set,
/// and therefore never reads back out of <c>rule.style.fontSize</c> either.
/// </para>
/// <para>
/// Only the properties whose grammar this engine can decide from the value text are gated; a
/// wrong rejection would lose a working declaration, so anything shaped like a function call, a
/// list or a var() reference passes untouched.
/// </para>
/// </summary>
public static class CssValueGrammar
{
    public static bool ValueIsValidFor(CssPropertyName name, CssValue value)
    {
        if (name.IsCustom) return true;
        var grammar = GrammarFor(name.Id);
        if (grammar == null) return true;

        // The grammars are decided from the value text. Identifier, string and
        // un-parsed (lazily parsed) values all carry that text verbatim; anything structured
        // — a function, a value list — has already been decoded, which is the proof that it
        // parsed, so it is left to the cascade to resolve.
        if (value is not (CssIdentifierValue or CssStringValue or Acrux.Core.Css.Tokenizer.CssUnparsedValue)) return true;
        return TextIsValid(value.CssText(), grammar);
    }

    /// <summary>The same grammar for a declaration the CSSOM is about to store, where only
    /// the property name and the value text are at hand.</summary>
    public static bool TextIsValidFor(string propertyName, string value)
    {
        if (propertyName.StartsWith("--")) return true;
        var grammar = GrammarFor(CssPropertyIdExtensions.FromString(propertyName));
        return grammar == null || TextIsValid(value, grammar);
    }

    /// <summary>The grammar to gate a declaration by, or null when this engine cannot decide
    /// the property's value set from its text.</summary>
    private static Func<string, bool>? GrammarFor(CssPropertyId id) => id switch
    {
        CssPropertyId.Display => static text => CssPropertyApplier.TryParseDisplay(text, out _, out _),
        CssPropertyId.Float => static text => CssFloatKeywords.TryParseFloat(text, out _),
        CssPropertyId.Clear => static text => CssFloatKeywords.TryParseClear(text, out _),
        // CSS Fonts 4 §1.2: an absolute or relative size keyword, a system font keyword, or a
        // length/percentage. 'math' and 'auto' are the keywords text-size-adjust added.
        CssPropertyId.FontSize => static text => IsLengthList(text,
            "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large",
            "larger", "smaller", "caption", "icon", "menu", "message-box", "small-caption",
            "status-bar", "math", "auto"),
        // CSS Text 4 §5.1: a length or a percentage plus the 'normal' keyword.
        CssPropertyId.LetterSpacing or CssPropertyId.WordSpacing
            => static text => IsLengthList(text, "normal"),
        // CSS Sizing 3: 'auto' and the intrinsic sizes, and 'none' for the maximums.
        CssPropertyId.Width or CssPropertyId.Height
            or CssPropertyId.MinWidth or CssPropertyId.MinHeight
            or CssPropertyId.MaxWidth or CssPropertyId.MaxHeight
            => static text => IsLengthList(text,
                "auto", "none", "min-content", "max-content", "fit-content", "contain", "stretch",
                // The legacy spelling of 'stretch' the engine still answers, and the prefixed
                // form pages use today.
                "-webkit-fill-available", "-webkit-fit-content"),
        CssPropertyId.Margin or CssPropertyId.MarginTop or CssPropertyId.MarginRight
            or CssPropertyId.MarginBottom or CssPropertyId.MarginLeft
            or CssPropertyId.MarginBlock or CssPropertyId.MarginBlockStart or CssPropertyId.MarginBlockEnd
            or CssPropertyId.MarginInline or CssPropertyId.MarginInlineStart or CssPropertyId.MarginInlineEnd
            => static text => IsLengthList(text, "auto"),
        CssPropertyId.Padding or CssPropertyId.PaddingTop or CssPropertyId.PaddingRight
            or CssPropertyId.PaddingBottom or CssPropertyId.PaddingLeft
            or CssPropertyId.PaddingBlock or CssPropertyId.PaddingBlockStart or CssPropertyId.PaddingBlockEnd
            or CssPropertyId.PaddingInline or CssPropertyId.PaddingInlineStart or CssPropertyId.PaddingInlineEnd
            => static text => IsLengthList(text),
        CssPropertyId.Inset or CssPropertyId.Top or CssPropertyId.Right
            or CssPropertyId.Bottom or CssPropertyId.Left
            or CssPropertyId.InsetBlock or CssPropertyId.InsetBlockStart or CssPropertyId.InsetBlockEnd
            or CssPropertyId.InsetInline or CssPropertyId.InsetInlineStart or CssPropertyId.InsetInlineEnd
            => static text => IsLengthList(text, "auto"),
        CssPropertyId.BorderTopWidth or CssPropertyId.BorderRightWidth
            or CssPropertyId.BorderBottomWidth or CssPropertyId.BorderLeftWidth
            or CssPropertyId.OutlineWidth
            => static text => IsLengthList(text, "thin", "medium", "thick", "auto"),
        // CSS Multi-column 1 §6: 'auto' lets the column width be calculated.
        CssPropertyId.ColumnWidth => static text => IsLengthList(text, "auto"),
        CssPropertyId.Gap or CssPropertyId.RowGap or CssPropertyId.ColumnGap
            => static text => IsLengthList(text, "normal"),
        CssPropertyId.Color or CssPropertyId.BackgroundColor or CssPropertyId.OutlineColor
            or CssPropertyId.BorderTopColor or CssPropertyId.BorderRightColor
            or CssPropertyId.BorderBottomColor or CssPropertyId.BorderLeftColor
            or CssPropertyId.ColumnRuleColor
            => static text => IsColorList(text),
        // The background geometry longhands (CSS Backgrounds 3 §4.1, CSS Position 3 §5.2), gated
        // by the same matchers the 'background' shorthand expands through so that the two cannot
        // disagree. Gating here rather than only at application time is what keeps the rule below
        // alive: a declaration the grammar rejects is dropped from the cascade, so
        // 'background-position: top 10px' on a box the stylesheet had put at '7px 9px' reads back
        // as '7px 9px' and not as the initial '0% 0%' (measured).
        CssPropertyId.BackgroundPosition => ShorthandExpander.IsBackgroundPositionValue,
        CssPropertyId.BackgroundSize => ShorthandExpander.IsBackgroundSizeValue,
        CssPropertyId.BackgroundRepeat => ShorthandExpander.IsBackgroundRepeatValue,
        CssPropertyId.BackgroundAttachment => ShorthandExpander.IsBackgroundAttachmentValue,
        CssPropertyId.BackgroundOrigin
            => static text => ShorthandExpander.IsBackgroundBoxValue(text, allowText: false),
        CssPropertyId.BackgroundClip
            => static text => ShorthandExpander.IsBackgroundBoxValue(text, allowText: true),
        CssPropertyId.BackgroundPositionX
            => static text => ShorthandExpander.IsBackgroundPositionAxisValue(text, horizontal: true),
        CssPropertyId.BackgroundPositionY
            => static text => ShorthandExpander.IsBackgroundPositionAxisValue(text, horizontal: false),
        // CSS Paged Media 4 §3.4, as the reference engine reads it: the size of a page is one named
        // paper, one or two lengths, or 'auto' — and a direction keyword beside a named paper. Two
        // papers, a percentage, a quoted name and a direction written on 'auto' are no size
        // (measured: '@page { size: B5 JIS-B4 }' and '@page { size: 50% }' both read back empty).
        CssPropertyId.Size
            => static text => CssPageSizeValue.TryCanonicalize(text, out _),
        // CSS UI 4 §9: a cursor is a list of addresses, each with its hot spot, and one keyword to
        // fall back on. The keyword has to come last and there has to be one — 'cursor: url(a.png)
        // 4 6' names no cursor at all, and one coordinate of a hot spot is not a hot spot
        // (measured; 'url(a.png) 4 6, pointer' and 'url(a.png), pointer' both are).
        CssPropertyId.Cursor
            => static text => CssCursorValue.IsValid(text),
        // CSS Fonts 4 §11.1: a family list is a list of names, and a name is a quoted string or the
        // sequence of identifiers a reference engine joins into one. The number that is nobody's
        // name makes no declaration (measured: '#a { font-family: 3 }' reads back an empty rule,
        // while 'font-family: My Font' and 'font-family: serif, "My Font"' both read back their
        // list with the multi-word name quoted).
        CssPropertyId.FontFamily => IsFamilyNameList,
        // An <image> is an address, a gradient or one of the image functions; 'local()' names a
        // font on the user's machine and is none of them. A list that carries one is not an image
        // list and the whole declaration goes with it, in the longhand and in the shorthand alike
        // (measured: 'background-image: url(a.png), local(x)', 'mask-image: local(x)' and
        // 'background: local(x)' all read back an empty rule).
        CssPropertyId.BackgroundImage or CssPropertyId.MaskImage
            or CssPropertyId.Background or CssPropertyId.Mask
            => static text => !NamesAFontLocal(text),
        _ => null,
    };

    /// <summary>A family list: one name per comma, each name a quoted string or the identifiers
    /// that stand for one family. A component that is a number, a dimension or an empty slot is not
    /// a name, and the declaration that carries one names no font.</summary>
    private static bool IsFamilyNameList(string text)
    {
        if (ContainsAnyStructural(text)) return true;
        foreach (var item in text.Split(','))
        {
            var trimmed = item.Trim();
            if (trimmed.Length == 0) return false;
            if (trimmed[0] is '"' or '\'') continue;   // a string's characters are data, not syntax
            foreach (var word in SplitWords(trimmed))
                if (!IsFamilyIdentifier(word)) return false;
        }
        return true;
    }

    /// <summary>One family name written as identifiers: it does not begin with a digit and every
    /// character of it is a name character.</summary>
    private static bool IsFamilyIdentifier(string text)
    {
        if (text.Length == 0 || char.IsAsciiDigit(text[0])) return false;
        foreach (var c in text)
            if (!char.IsLetter(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_') && c <= '\u007F')
                return false;
        return true;
    }

    /// <summary>Whether the value calls 'local()' — a font on the user's machine, which no
    /// &lt;image&gt; grammar has a place for. The search skips the characters inside a quoted
    /// string, where they are an address rather than a function call.</summary>
    private static bool NamesAFontLocal(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) { i += 2; continue; }
                    if (text[i] == c) break;
                    i++;
                }
                continue;
            }
            if ((c is 'L' or 'l') && i + 6 <= text.Length &&
                string.Compare(text, i, "local(", 0, 6, StringComparison.OrdinalIgnoreCase) == 0)
                return true;
        }
        return false;
    }

    private static bool TextIsValid(string text, Func<string, bool> grammar)
    {
        text = text.Trim();
        if (text.Length == 0) return true;
        if (CssPropertyTraits.IsCssWideKeyword(text)) return true;
        if (text.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return grammar(text);
    }

    /// <summary>A &lt;length-percentage&gt; list, which is how a length-valued property is
    /// written whatever the arity of its grammar is: the keyword set is the one the property
    /// adds to the numeric values, and a value this helper cannot see through is accepted.</summary>
    private static bool IsLengthList(string text, params string[] keywords)
    {
        if (ContainsAnyStructural(text)) return true;
        foreach (var token in SplitWords(text))
        {
            if (IsLengthOrPercentage(token)) continue;
            if (IsOneOf(token, keywords)) continue;
            return false;
        }
        return true;
    }

    /// <summary>The colour grammar of the properties that take &lt;color&gt;#: a token the colour
    /// parser recognises, or one of the keywords that stand for a colour the element has to
    /// work out for itself. 'invert' is deliberately absent: Color 4 §14.1 defines it only for
    /// forced-colours mode, and a reference engine rejects the whole declaration outside it
    /// (measured: 'outline: 3px solid invert' leaves outline-style at its initial 'none').</summary>
    private static bool IsColorList(string text)
    {
        if (ContainsAnyStructural(text)) return true;
        foreach (var token in SplitWords(text))
        {
            if (ColorParser.IsColorToken(token)) continue;
            if (token.Equals("auto", StringComparison.OrdinalIgnoreCase)) continue;
            return false;
        }
        return true;
    }

    /// <summary>A value with a parenthesis, a bracket or a slash is a function call, a grid
    /// track list or a shorthand component list: deciding those from text alone would mean
    /// duplicating the parser, so they are accepted here and resolved by the applier.</summary>
    private static bool ContainsAnyStructural(string text) =>
        text.IndexOfAny(StructuralChars) >= 0;

    private static readonly char[] StructuralChars = { '(', '[', '/' };

    private static IEnumerable<string> SplitWords(string text) =>
        text.Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsOneOf(string token, string[] keywords)
    {
        foreach (var keyword in keywords)
            if (token.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>A &lt;length&gt; or &lt;percentage&gt; in the sign/number/unit shape CSS Syntax
    /// gives them. The unit is not looked up in a table: a unit name this engine has not heard
    /// of yet is still a unit, and refusing it would lose a working declaration.</summary>
    private static bool IsLengthOrPercentage(string token)
    {
        int i = 0;
        if (i < token.Length && (token[i] == '+' || token[i] == '-')) i++;
        int digits = i;
        while (i < token.Length && (char.IsAsciiDigit(token[i]) || token[i] == '.')) i++;
        if (i == digits) return false;
        if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
        {
            int probe = i + 1;
            if (probe < token.Length && (token[probe] == '+' || token[probe] == '-')) probe++;
            int exponent = probe;
            while (probe < token.Length && char.IsAsciiDigit(token[probe])) probe++;
            if (probe > exponent) i = probe;
        }
        var unit = token[i..];
        if (unit.Length == 0) return true;                 // a bare number, as '0' and as history
        if (unit == "%") return true;
        // Everything after the number has to be an identifier: '10px!', '10 px' and '10em2%'
        // are not lengths.
        foreach (var c in unit)
            if (!char.IsLetter(c) && c != '_' && c != '-' && c <= '\u007F') return false;
        return true;
    }
}
