using System.Globalization;
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

        // The grammars are decided from the value text. Identifier, string and
        // un-parsed (lazily parsed) values all carry that text verbatim; anything structured
        // — a function, a value list — has already been decoded, which is the proof that it
        // parsed, so it is left to the cascade to resolve.
        if (value is not (CssIdentifierValue or CssStringValue or Acrux.Core.Css.Tokenizer.CssUnparsedValue)) return true;
        // One decision about one declaration. The property set used to ask its own question of the
        // id's grammar while the CSSOM asked the name's, and the two lists drifted: a
        // 'offset-path: content-box' written in a sheet or a style attribute was refused by the
        // property's own id grammar while the echo table — the one that also decides the cascade and
        // 'getComputedStyle' — accepted it through 'setProperty'.
        var text = value.CssText();
        // A pending value keeps the characters it was written with, comment included, and a comment is
        // no one's operator yet either: the grammar is asked of the text with each comment read as the
        // space this engine keeps for one (CSS Variables 1 §3.1; measured: 'width:
        // calc(var(--x)/*c*/+ 1px)' is a declaration that echoes the comment back).
        if (IsPendingSubstitution(text)) text = WithCommentsAsWhitespace(text);
        return ValueTextIsValid(name.ToCssString(), text);
    }

    /// <summary>Whether a value text carries an exclamation mark outside every string and every
    /// group — the shape of an important flag. The two-argument <c>CSS.supports</c> is the one
    /// surface that refuses it: 'color: red !important', 'color: red !' and even a custom property's
    /// 'red !important' are each unsupported, while the condition form reads the same declaration as
    /// well-formed (all measured). A mark inside a string — 'content: "!important"' — is a value with
    /// a mark in it and no flag at all, and is supported.</summary>
    public static bool HasImportantFlag(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < value.Length) i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (c == '!' && depth == 0) return true;
        }
        return false;
    }

    /// <summary>Whether a comment in this value is standing in for the whitespace a math operator
    /// demands. CSS Values 4 §10.3 makes that space part of the operator: 'calc(1px + 2px)' is a sum
    /// and 'calc(1px+2px)' is no expression at all, and a comment between the sign and its neighbour
    /// is still not space (measured through 'CSS.supports' on both sides: 'calc(1px/*c*/+ 2px)' and
    /// 'calc(1px +/*c*/2px)' are refused). A comment that sits beside real space is only the token
    /// boundary CSS Syntax 3 §4 says it is, so 'calc(1px /*c*/ + 2px)' stands and
    /// 'margin: 1px/*c*/2px' stays the two lengths it has always been.</summary>
    public static bool CommentSuppliesMathWhitespace(string? value)
    {
        if (string.IsNullOrEmpty(value) || value!.IndexOf("/*", StringComparison.Ordinal) < 0)
            return false;
        // A value that carries a var() is no one's expression yet: the grammar is not asked what it
        // thinks of it until the substitution has run (CSS Variables 1 §3), so the spacing a comment
        // may or may not supply is not decided here either (measured: 'width:
        // calc(var(--x)/*c*/+ 1px)' is accepted and echoes its own text).
        if (value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        // Two readings of one text: the comment as the space this engine keeps, and the comment as
        // nothing. Only a value whose operators are spaced in the first reading and unspaced in the
        // second had a comment doing a space's job.
        return MathOperatorsAreSpaced(WithCommentsAsWhitespace(value))
            && !MathOperatorsAreSpaced(Acrux.Core.Css.MediaFeatureValue.StripComments(value));
    }

    private static bool MathOperatorsAreSpaced(string text)
    {
        foreach (var call in CssMathValue.MathCalls(text))
        {
            // A sum inside a sum is still a sum: 'calc(1px + calc(2px/*c*/+ 3px))' is refused by the
            // inner operator, and a scan that stopped at the outer argument would never see it
            // (measured, through 'CSS.supports').
            if (!CssMathValue.OperatorsAreSpaced(call.Argument)) return false;
            if (!MathOperatorsAreSpaced(call.Argument)) return false;
        }
        return true;
    }

    /// <summary>Whether a value is a pending-substitution value: one that still carries a <c>var()</c>
    /// the engine has not been allowed to read. Nothing of it is this engine's expression yet, so it
    /// is held as the token stream the page wrote — comments and all — and printed back the same way
    /// (CSS Variables 1 §3.1; measured: 'width: calc(var(--x)/*c*/+ 1px)' echoes exactly that text and
    /// 'CSS.supports' takes it, while the same shape without a var() is no value at all).</summary>
    public static bool IsPendingSubstitution(string? value) =>
        !string.IsNullOrEmpty(value) && value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>A declaration value with every CSS comment replaced by the one space it stands for.
    /// A comment is a token boundary, not nothing: '1px/*c*/2px' is two lengths, and the same text with
    /// the comment cut out entirely would be one impossible one (measured: the reference engine makes
    /// four longhands out of <c>margin: 1px/*c*/2px</c> and echoes '1px 2px', stores
    /// 'rect(0px, 0px, 0px, 0px)' for a rect with a comment on either side of it, and says
    /// '12px serif' for <c>font: 12px/*x*/ serif</c>). A comment that never closes runs to the end of
    /// the value, and a '/*' inside a quoted string is a string, not a comment.</summary>
    public static string WithCommentsAsWhitespace(string? value)
    {
        if (string.IsNullOrEmpty(value) || value!.IndexOf("/*", StringComparison.Ordinal) < 0)
            return value ?? string.Empty;
        var sb = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c is '"' or '\'')
            {
                char quote = c;
                sb.Append(c);
                int j = i + 1;
                while (j < value.Length && value[j] != quote)
                    j += value[j] == '\\' ? 2 : 1;
                int length = Math.Min(j - i + 1, value.Length - i);
                sb.Append(value, i + 1, length - 1);
                i += length;
                continue;
            }
            if (c == '/' && i + 1 < value.Length && value[i + 1] == '*')
            {
                int close = value.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? value.Length : close + 1;
                sb.Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The same grammar for a declaration the CSSOM is about to store, where only
    /// the property name and the value text are at hand.</summary>
    public static bool TextIsValidFor(string propertyName, string value)
    {
        if (propertyName.StartsWith("--")) return true;
        // A name the reference engine has no property for has no value either. This engine knows a
        // handful of names only its own code paths use ('initial-letter-align', 'max-lines',
        // 'imemode' …, see CssPropertyTraits.EngineOnlyNames), and echoing a declaration under one
        // of them reads back a property the page could never have set (measured:
        // 'setProperty("initial-letter-align","superscript")' then answers the empty string).
        if (!Resolver.CssPropertyTraits.IsDeclaredProperty(propertyName)) return false;
        // A comment is the token boundary it stands for, here as much as in a stylesheet, so the
        // grammar, <c>CSS.supports</c> and the block the CSSOM stores all read one sequence
        // (measured: 'margin: 1px/*c*/2px' is four longhands and 'rect(0px/*x*/0px 0px 0px)' is a
        // rect; 'width: /*x*/' is no value at all).
        return ValueTextIsValid(propertyName, WithCommentsAsWhitespace(value));
    }

    /// <summary>Whether a value text is a value of the named property, once the name itself has been
    /// accepted as a property. Every surface that records a declaration asks this and nothing else,
    /// so a sheet, a style attribute, <c>setProperty</c> and <c>CSS.supports</c> cannot disagree.
    /// <see cref="CssValueText"/> asks this one too: a printer that re-orders words has to know when
    /// the value it was handed is not a value at all, or it repairs the declaration it should leave
    /// the cascade to drop.</summary>
    internal static bool ValueTextIsValid(string propertyName, string value)
    {
        // The background shorthand's grammar IS its expansion: a colour outside the final layer, a
        // token nothing in <bg-layer> reads and a position or size list the grammar refuses all leave
        // the declaration out of the cascade, and the reference engine refuses it in the CSSOM echo
        // too (measured: 'background: red, blue', 'background: red blue' and 'background: invert'
        // each read back empty). Deciding the echo anywhere else would let it and the cascade
        // disagree about one declaration.
        if (propertyName.Equals("background", StringComparison.OrdinalIgnoreCase)
            && !Resolver.CssPropertyTraits.IsCssWideKeyword(value.Trim()))
            return ShorthandExpander.ExpandProperty("background", value).Count > 0;
        // The 'offset' shorthand is gated the same way, because it is read as a list of parts and
        // the parts are judged by the five longhands' own table: a position written after the
        // rotation, a second distance or a rotation spelled twice leave the whole declaration out
        // (measured: 'offset: url(#p) 10% reverse center top' reads back empty).
        if (propertyName.Equals("offset", StringComparison.OrdinalIgnoreCase)
            && !Resolver.CssPropertyTraits.IsCssWideKeyword(value.Trim()))
            return ShorthandExpander.ExpandProperty("offset", value).Count > 0;
        // The 'font' shorthand is gated by the same reader the expansion and the echo use: a size
        // with no family, a word before the size that is none of the four categories, and a family
        // list that does not read as one each leave the declaration out of the cascade
        // (measured: 'font: bold 12px', 'font: line-height 12px serif' and 'font: 12px serif extra'
        // are all dropped, where the engine used to keep a half-read version of each).
        if (propertyName.Equals("font", StringComparison.OrdinalIgnoreCase)
            && !Resolver.CssPropertyTraits.IsCssWideKeyword(value.Trim())
            && ShorthandExpander.ParseFontShorthand(value) == null) return false;
        // The properties this engine echoes without consuming are read by one table, and that table
        // is the grammar too: a value whose canonical text does not exist is not a declaration on
        // any surface (measured value by value — 'fill-rule: bogus', 'mask-type: alpha luminance',
        // 'container-name: a, b', 'animation-timeline: t' and the rest).
        if (Acrux.Core.Css.Resolver.EchoedProperties.Find(propertyName) is { } echo)
            return TextIsValid(value, text => echo.Normalize(text) != null);
        // The properties whose whole value is a colour (or a list of them) have this one grammar:
        // a word the colour parser cannot read, a function with the wrong number of arguments, and
        // a '#' glued in front of a function name are all no value at all (measured: 'caret-color:
        // bogus', 'rgb(1,2)' and '#rgb(0%, 50%, 100%)' are each dropped, where the engine used to
        // store black for them).
        if (ColorValueArity.TryGetValue(propertyName, out var arity)
            && !Resolver.CssPropertyTraits.IsCssWideKeyword(value.Trim())
            && !IsColorListValue(value, arity.Max, arity.Extra)) return false;
        // A '-webkit-' spelling that is a property of its own has its twin's grammar: the value
        // set is the same, and only the name the declaration is stored under differs.
        var id = CssPropertyIdExtensions.BehaviourId(CssPropertyIdExtensions.FromString(propertyName));
        // A '-webkit-' spelling that owns a value set of its own is asked that set and not its
        // twin's: '-webkit-line-break' takes 'after-white-space' and has no 'anywhere', where
        // 'line-break' is the other way round (both measured).
        var ownSet = propertyName.StartsWith("-webkit-", StringComparison.Ordinal)
            ? LegacyStructuredGrammar(propertyName) : null;
        var grammar = ownSet ?? (id != CssPropertyId.Invalid
            ? GrammarFor(id) : LegacyPrefixedGrammar(propertyName));
        if (grammar != null && !TextIsValid(value, grammar)) return false;
        return !MathTypeRejects(propertyName, value);
    }

    /// <summary>The properties whose value is a time, spelled every way the engine knows them. A
    /// math function in one of these has to resolve to a time, which is the whole of what separates
    /// <c>transition-duration: calc(1s + 1ms)</c> from <c>calc(1s + 1px)</c> (measured, the second
    /// leaves the property at its initial '0s').</summary>
    private static readonly string[] MathTimeProperties =
    {
        "transition-duration", "transition-delay", "animation-duration", "animation-delay",
        "-webkit-transition-duration", "-webkit-transition-delay",
        "-webkit-animation-duration", "-webkit-animation-delay",
    };

    /// <summary>The properties whose value is an angle. 'rotate' takes one and nothing else, so
    /// <c>rotate: calc(1deg * 2)</c> stands while <c>calc(1deg + 1)</c> falls (measured).</summary>
    private static readonly string[] MathAngleProperties =
    { "rotate", "rotate-x", "rotate-y", "rotate-z", "offset-rotate" };

    /// <summary>The track lists, the only place a fraction is a value. A length property has no use
    /// for one: <c>width: calc(1fr)</c> is refused while <c>grid-template-columns: calc(1fr + 2fr)</c>
    /// is a track (measured for the first, and 'grid-column-gap: calc(1px + 1fr)' — a gap is not a
    /// track — for the second).</summary>
    private static readonly string[] MathTrackProperties =
    {
        "grid-template-columns", "grid-template-rows", "grid-auto-columns", "grid-auto-rows",
    };

    /// <summary>The properties whose value is a count and which take no dimension at all: a math
    /// function in one of them has to come out a number (measured for 'opacity' and 'flex-grow',
    /// whose <c>calc(0.5 + 1px)</c> and <c>calc(1 + 1px)</c> both fall).</summary>
    private static readonly string[] MathNumberProperties =
    { "opacity", "flex-grow", "flex-shrink", "order", "z-index", "aspect-ratio" };

    /// <summary>The properties that take a count, a length or a share of one, so a math function of
    /// any of the three types is a value of theirs (measured: <c>line-height</c> takes
    /// <c>calc(1 + 1)</c> and <c>calc(2%)</c> alike, and <c>tab-size</c> takes a number and a length
    /// because the property itself is spelled both ways).</summary>
    private static readonly string[] MathNumberOrLengthProperties =
    { "line-height", "tab-size", "columns" };

    /// <summary>Whether a math function in this value is a value of this property at all. The
    /// expression's own type rules come first — a number added to a length is no length whatever the
    /// property is — and then the type has to be one the property measures: <c>width: calc(1)</c> is
    /// a well-formed expression whose answer is a count, and a count is not a width
    /// (CSS Values 4 §10.3, measured through <c>supports()</c>).</summary>
    private static bool MathTypeRejects(string propertyName, string value)
    {
        if (value.IndexOf('(') < 0) return false;
        var types = MathValueTypes(propertyName);
        foreach (var call in CssMathValue.MathCalls(value))
        {
            // A sign the page did not put whitespace around is not an operator, so the expression it
            // stands in is not an expression whatever its terms are — and the printer must not be left
            // to fold 'calc(1+1)' into a number the property then reads as a value (measured: every
            // unspaced sum is dropped, through 'supports()' as well).
            if (!CssMathValue.OperatorsAreSpaced(call.Argument)) return true;
            if (!CssMathValue.WellTyped(call.Name + "(" + call.Argument + ")", out var category))
                return true;
            if (category == null || types == null) continue;
            bool matches = false;
            foreach (var type in types)
                if (type == category) { matches = true; break; }
            if (!matches) return true;
        }
        return false;
    }

    /// <summary>The value types a math function may resolve to for this property, or null when this
    /// engine cannot say — which leaves the expression's own type rules to decide and takes nothing
    /// away from a declaration it accepts today.</summary>
    private static string[]? MathValueTypes(string property)
    {
        if (Matches(MathTimeProperties, property)) return new[] { "time" };
        if (Matches(MathAngleProperties, property)) return new[] { "angle" };
        if (Matches(MathTrackProperties, property))
            return new[] { "length", "percentage", "flex" };
        if (Matches(MathNumberProperties, property)) return new[] { "number" };
        if (Matches(MathNumberOrLengthProperties, property))
            return new[] { "number", "length", "percentage" };
        // A length-valued property measures a length, and a share of one is a length as good as any
        // other (CSS Values 3 §6.5) — but a count is not.
        if (Resolver.CssPropertyTraits.TakesLength(property))
            return new[] { "length", "percentage" };
        return null;
    }

    private static bool Matches(string[] names, string property)
    {
        foreach (var name in names)
            if (name.Equals(property, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The legacy gate for the parser, which has the authored text in hand: a number this
    /// engine folds before it prints it ('1e2' as '100') would otherwise pass an integer gate it
    /// has to fail (measured: '-webkit-box-ordinal-group: 1e2' is dropped).</summary>
    public static bool LegacyTextIsValidFor(string propertyName, string authoredValue)
    {
        var grammar = LegacyPrefixedGrammar(propertyName.ToLowerInvariant());
        return grammar == null || TextIsValid(authoredValue, grammar);
    }

    /// <summary>The value sets measured for the <c>-webkit-</c> properties this engine has no id
    /// for (snapshots/out/_b257_edge_legacy_grammar.txt: every accepted and every refused value
    /// below is one the reference engine was asked about). None of them changes layout here, but
    /// each still decides whether a declaration is made at all, which is what the CSSOM prints
    /// back and what <c>CSS.supports</c> answers — and a property with no grammar in this engine
    /// takes <c>bogus</c>, which the reference engine drops. The sets come in groups: the original
    /// flex-box model, the nine-patch mask border, the text paint pair and the two interaction
    /// properties.</summary>
    private static Func<string, bool>? LegacyPrefixedGrammar(string name)
    {
        if (LegacyKeywordSets.TryGetValue(name, out var words))
        {
            // 'mask-box-image-repeat' is the one keyword property that takes a pair (one keyword
            // per axis); the rest of the sets are a single keyword.
            bool pairs = name.Equals("-webkit-mask-box-image-repeat", StringComparison.OrdinalIgnoreCase);
            return text =>
            {
                var parts = SplitComponents(text);
                return (pairs ? parts.Count is 1 or 2 : parts.Count == 1) && IsOneWordList(text, words);
            };
        }
        return LegacyStructuredGrammar(name);
    }

    /// <summary>The closed keyword set of a legacy property, or null when its value is not a
    /// single keyword of a measured set.</summary>
    private static readonly Dictionary<string, string[]> LegacyKeywordSets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["-webkit-box-align"] = new[] { "start", "center", "end", "baseline", "stretch" },
        ["-webkit-box-direction"] = new[] { "normal", "reverse" },
        ["-webkit-box-orient"] = new[] { "horizontal", "vertical", "inline-axis", "block-axis" },
        ["-webkit-box-pack"] = new[] { "start", "center", "end", "justify" },
        ["-webkit-font-smoothing"] = new[] { "auto", "none", "antialiased", "subpixel-antialiased" },
        ["-webkit-user-drag"] = new[] { "auto", "none", "element" },
        ["-webkit-user-modify"] = new[] { "read-only", "read-write", "read-write-plaintext-only" },
        ["-webkit-text-decorations-in-effect"] = new[] { "none", "underline" },
        ["-webkit-mask-box-image-repeat"] = new[] { "stretch", "round", "repeat", "space" },
    };

    /// <summary>The keywords one legacy property owns, which are the only words of its value the
    /// engine may re-spell (measured: 'END' is printed 'end' and '3 FILL' as '3 fill').</summary>
    public static bool LegacyKeywordBelongs(string property, string word)
    {
        if (LegacyKeywordSets.TryGetValue(property, out var words) && IsOneOf(word, words)) return true;
        return property switch
        {
            "-webkit-box-reflect" => IsOneOf(word, "above", "below", "left", "right"),
            "-webkit-mask-box-image-slice" => word.Equals("fill", StringComparison.OrdinalIgnoreCase),
            "-webkit-mask-box-image-source" => word.Equals("none", StringComparison.OrdinalIgnoreCase),
            "-webkit-mask-box-image-width" => word.Equals("auto", StringComparison.OrdinalIgnoreCase),
            // The colour-valued ones print their colour keywords in lower case, which is the
            // spelling the reference engine prints them in (measured 'red' stays 'red').
            "-webkit-text-fill-color" or "-webkit-text-stroke-color" or "-webkit-tap-highlight-color"
                or "-webkit-text-stroke" => ColorParser.IsColorToken(word) || word == "currentcolor",
            _ => false,
        };
    }

    /// <summary>Whether a property name is one of the legacy <c>-webkit-</c> spellings whose value
    /// set this class owns, which is also the set <see cref="Css.CssValueText"/> prints in the
    /// reference engine's own spelling.</summary>
    public static bool IsLegacyPrefixedName(string name) =>
        !string.IsNullOrEmpty(name) && (LegacyKeywordSets.ContainsKey(name) || LegacyStructuredGrammar(name) != null);

    private static Func<string, bool>? LegacyStructuredGrammar(string name) => name.ToLowerInvariant() switch
    {
        // A flex factor: any number, negative included (measured '-1' stands while 'calc(1+1)'
        // does not), and an integer group number from one up.
        "-webkit-box-flex" => static t => IsLegacyNumber(t.Trim()),
        "-webkit-box-ordinal-group" => static t => IsLegacyInteger(t.Trim()),
        // The two halves of a table's border-spacing, one plain length each (measured: '4px'
        // stands and an element that wrote nothing reports '0px').
        "-webkit-border-horizontal-spacing" or "-webkit-border-vertical-spacing"
            => static t => IsPlainLength(t.Trim()),
        // The reflection is a side, an optional distance and nothing else: a colour and a third
        // component are both refused, and 'none' is not a value of this property at all
        // (measured).
        "-webkit-box-reflect" => static t =>
        {
            var parts = SplitComponents(t).ToList();
            if (parts.Count is not (1 or 2)) return false;
            if (!IsOneOf(parts[0], "above", "below", "left", "right")) return false;
            return parts.Count == 1 || IsPlainLength(parts[1]);
        },
        "-webkit-font-smoothing" => static t =>
            IsOneWord(t, "auto", "none", "antialiased", "subpixel-antialiased"),
        // The source is one image and nothing beside it: 'none' and a gradient read, while
        // 'url(a.png) none' is refused (both measured) because the property's grammar has one
        // <image> in it, not a list.
        "-webkit-mask-box-image-source" => static t => t.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
            || IsOneImageFunction(t.Trim()),
        // The slice is one to four non-negative numbers or percentages with an optional 'fill'. The
        // flag rides either end of the list — '2 fill' and 'fill 2' both read — but never inside it
        // ('3 fill 2' is refused), never on its own, and never twice. A negative number is refused
        // while a percentage is not (all measured).
        "-webkit-mask-box-image-slice" => static t =>
        {
            var parts = SplitComponents(t).ToList();
            if (parts.Count > 0 && parts[^1].Equals("fill", StringComparison.OrdinalIgnoreCase))
                parts.RemoveAt(parts.Count - 1);
            else if (parts.Count > 1 && parts[0].Equals("fill", StringComparison.OrdinalIgnoreCase))
                parts.RemoveAt(0);
            return parts.Count is >= 1 and <= 4
                && parts.TrueForAll(MaskBoxImageShorthand.SlicePartIsValid);
        },
        // The outset is one to four lengths, and a bare number is one of them (measured '2'
        // stands); 'auto' is not.
        "-webkit-mask-box-image-outset" => static t =>
        {
            var parts = SplitComponents(t).ToList();
            return parts.Count is >= 1 and <= 4 && parts.TrueForAll(MaskBoxImageShorthand.OutsetPartIsValid);
        },
        "-webkit-mask-box-image-width" => static t =>
        {
            var parts = SplitComponents(t).ToList();
            // Negative widths are refused while percentages and bare numbers are not (measured).
            return parts.Count is >= 1 and <= 4 && parts.TrueForAll(MaskBoxImageShorthand.WidthPartIsValid);
        },
        // The shorthand has a grammar of its own because it is not a value the engine keeps: it
        // expands into five longhands and reads back as nothing, so what it accepts has to be
        // decided section by section (snapshots/_b259_mask_box_image_truth.txt).
        "-webkit-mask-box-image" => static t => MaskBoxImageShorthand.TextIsValid(t),
        "-webkit-tap-highlight-color" => static t => IsOneColor(t),
        "-webkit-text-fill-color" => static t => IsOneColor(t),
        "-webkit-text-stroke-color" => static t => IsOneColor(t),
        // The legacy line-break vocabulary: the modern set without 'anywhere' and with the word
        // this spelling brought with it (measured — 'after-white-space' stands, 'anywhere' and
        // 'loose-allows-after-ja' and 'strict-allow-empty-line' each fall).
        "-webkit-line-break" => static t => IsOneWordOnly(t, "auto", "loose", "normal", "strict",
            "after-white-space"),
        // CSS Writing Modes 3 §5.4 as the prefixed spelling reads it in this engine: the four
        // orientations below are the whole set. 'mixed' — the initial of the modern property — is
        // nobody's value here, and neither are 'vertical', 'auto', 'sideways-left', 'vertical-left'
        // or the two horizontal spellings the first draft carried
        // (all measured, in snapshots/out/_b294_ref_probeF.txt and _b294_ref_probeG.txt).
        "-webkit-text-orientation" => static t => IsOneWordOnly(t, "vertical-right",
            "sideways-right", "upright", "sideways"),
        "-webkit-text-stroke-width" => static t => IsTextStrokeWidth(t.Trim()),
        // Width first, colour second, either one optional and never two of a kind (measured:
        // 'green 2px' is stored reordered as '2px green', '2px 2px' and 'red green' are dropped).
        "-webkit-text-stroke" => static t =>
        {
            var parts = SplitComponents(t).ToList();
            if (parts.Count is not (1 or 2)) return false;
            if (parts.Count == 1) return IsTextStrokeWidth(parts[0]) || IsLegacyColor(parts[0]);
            return (IsTextStrokeWidth(parts[0]) && IsLegacyColor(parts[1]))
                || (IsLegacyColor(parts[0]) && IsTextStrokeWidth(parts[1]));
        },
        _ => null,
    };

    /// <summary>One word, or a list of words each from one set.</summary>
    private static bool IsOneWord(string text, params string[] words)
    {
        var parts = SplitComponents(text);
        return parts.Count == 1 && IsOneOf(parts[0], words);
    }

    /// <summary>One word of a closed set — a second word is no value even when it is itself a
    /// member ('word-break: normal break-word', 'user-select: none auto', both measured).</summary>
    /// <summary>One or two words of a closed set, printed once when both agree (the pair
    /// shorthands of CSS Overscroll Behavior 1 §3, measured: 'auto auto' reads 'auto').</summary>
    /// <summary>For 'object-position': a value with two side keywords and exactly one length is
    /// no position (measured). Every other shape the background grammar takes, this one takes too.</summary>
    private static bool SaysBothOrNeither(string text)
    {
        int sides = 0, lengths = 0;
        foreach (var word in SplitComponents(text))
        {
            var p = word.ToLowerInvariant();
            if (p is "left" or "right" or "top" or "bottom" or "center") sides++;
            else lengths++;
        }
        return !(sides == 2 && lengths == 1);
    }

    private static bool IsOneOrTwoKeywords(string text, params string[] allowed)
    {
        var words = SplitComponents(text);
        if (words.Count is < 1 or > 2) return false;
        return words.All(word => IsOneOf(word, allowed));
    }

    private static bool IsSingleKeywordWord(string text, string[] allowed)
    {
        var words = SplitComponents(text);
        return words.Count == 1 && IsOneOf(words[0], allowed);
    }

    /// <summary><c>text-wrap</c> is the pair of its two longhands (CSS Text 4 §4): at most one
    /// wrapping mode and at most one styling keyword, and 'reverse' — which an older draft carried
    /// — is not one of them here (measured: 'wrap reverse' is dropped).</summary>
    private static bool IsTextWrapValue(string text)
    {
        bool mode = false, style = false;
        foreach (var word in SplitComponents(text))
        {
            var p = word.ToLowerInvariant();
            if (p is "wrap" or "nowrap") { if (mode) return false; mode = true; }
            else if (p is "auto" or "stable" or "balance" or "pretty") { if (style) return false; style = true; }
            else return false;
        }
        return mode || style;
    }

    /// <summary><c>contain</c> (CSS Containment 3 §2.1): either one of the three set keywords on
    /// its own, or a comma-free list of the five kinds with none repeated. Measured value by
    /// value: 'size inline-size' is refused — 'size' already contains the axis 'inline-size'
    /// names — 'strict style', 'content, paint' and 'none style' are refused, a keyword spelled
    /// twice is refused, and 'size layout style paint' is accepted. An unknown word refuses the
    /// declaration here rather than silently computing 'none', which is what the applier's own
    /// fallback would have made of it.</summary>
    private static bool IsContainValue(string text)
    {
        var words = SplitComponents(text);
        if (words.Count == 0) return false;
        // A comma separates layers in some grammars; 'contain' takes a space-separated list, so
        // 'inline-size, layout' is no value at all (measured).
        if (text.IndexOf(',') >= 0) return false;
        if (words.Count == 1)
            return IsOneOf(words[0], "none", "strict", "content", "size", "inline-size",
                "layout", "style", "paint");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool size = false, inlineSize = false;
        foreach (var word in words)
        {
            var p = word.ToLowerInvariant();
            // The set keywords say the whole list, so they have no company.
            if (p is "none" or "strict" or "content") return false;
            if (p is not ("size" or "inline-size" or "layout" or "style" or "paint")) return false;
            if (p == "size") { if (inlineSize) return false; size = true; }
            else if (p == "inline-size") { if (size) return false; inlineSize = true; }
            if (!seen.Add(p)) return false;
        }
        return true;
    }

    /// <summary>CSS Containment 3 §3.1 for the 'contain-intrinsic' family: at most
    /// <paramref name="maxComponents"/> components — two for the shorthand, one for each axis
    /// longhand — of 'none', 'auto' or a non-negative unit-bearing length, with 'auto' never
    /// standing on its own. Measured: 'auto' and 'auto auto' are refused while 'auto none' and
    /// 'AUTO 55px' stand, '50%' and '50px 20%' and '-5px' and 'none none none' are refused.</summary>
    private static bool IsContainIntrinsicValue(string text, int maxComponents)
    {
        if (text.IndexOf(',') >= 0) return false;
        var words = SplitComponents(text);
        // One leading 'auto' is the remembered-size prefix and is not one of the sizes the property
        // counts; a second 'auto' is a size, and 'auto' alone is nothing to fall back on.
        if (words.Count > 0 && words[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Count == 1) return false;
            words.RemoveAt(0);
        }
        if (words.Count == 0 || words.Count > maxComponents) return false;
        bool saysSize = false;
        foreach (var word in words)
        {
            var p = word.ToLowerInvariant();
            if (p == "auto") continue;
            if (p == "none") { saysSize = true; continue; }
            if (p.StartsWith("-", StringComparison.Ordinal)
                || p.StartsWith("+", StringComparison.Ordinal)
                || !IsPlainLength(p)) return false;
            saysSize = true;
        }
        // 'auto' names the size to fall back on while the box is skipped; with no size beside it
        // there is nothing to fall back to, and the reference engine keeps nothing at all.
        return saysSize;
    }

    /// <summary>ONE word, from the given set. The list form below accepts any number of them,
    /// which is not the grammar of a property that takes a single keyword: 'resize: both horizontal'
    /// and 'box-decoration-break: slice clone' are two values where the reference engine keeps
    /// neither (measured).</summary>
    private static bool IsOneWordOnly(string text, params string[] words)
    {
        var parts = SplitComponents(text);
        return parts.Count == 1 && IsOneOf(parts[0], words);
    }

    /// <summary>The two tag-list properties, <c>font-variation-settings</c> and
    /// <c>font-feature-settings</c> (CSS Fonts 4 §8.6, §10.1). Each is <c>normal</c> or a comma list
    /// of a quoted tag with an optional value. The tag is <b>exactly four characters</b> — a three-
    /// or a five-character name is no tag at all — and an unquoted one is not a tag either
    /// (measured: '"abc" 1', '"abcde" 1' and 'liga 1' are each dropped while '"wght" 700' stands).
    /// A feature's value is an integer or the word <c>on</c>/<c>off</c> and may be left out; a
    /// variation's is required and may be any number (measured: '"liga" 2.5', '"liga" 0.0',
    /// '"liga" 1.0' and '"wght" 700,' are dropped while '"wght" 700.25' stands).</summary>
    private static bool IsTagSettingList(string text, bool integerValues)
    {
        var words = SplitComponents(text);
        if (words.Count == 1 && words[0].Equals("normal", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var item in SplitTagListItems(text))
        {
            var parts = SplitComponents(item);
            if (parts.Count is 0 or > 2) return false;
            if (!IsTagWord(parts[0])) return false;
            if (parts.Count == 1)
            {
                // A bare tag is a feature switched on; a variation has nothing to vary and is no
                // value at all (measured: '"smcp", "swsh" 2' stands while '"wght"' is dropped).
                if (!integerValues) return false;
                continue;
            }
            var value = parts[1];
            if (integerValues && (value.Equals("on", StringComparison.OrdinalIgnoreCase)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase))) continue;
            // A math function is a number the engine has not finished adding yet, and both slots take
            // one (measured: '"liga" calc(1 + 1)' stands, and the reference folds it to 'calc(2)' in
            // the specified value and to '2' in the computed one).
            if (IsMathFunctionCall(value, IsNumber)) continue;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || double.IsNaN(number) || double.IsInfinity(number)) return false;
            // A feature index is written as an integer, not merely valued as one: the reference
            // engine drops '"liga" 1.0' and '"liga" 0.0' while '"liga" 1' stands (measured).
            if (integerValues && !IsIntegerText(value)) return false;
        }
        return true;
    }

    /// <summary>An integer as a page writes one: an optional sign and digits, with no decimal point
    /// and no exponent. <c>1.0</c> is the same number as <c>1</c> and not the same value.</summary>
    private static bool IsIntegerText(string text)
    {
        if (text.Length == 0) return false;
        int start = text[0] == '-' || text[0] == '+' ? 1 : 0;
        if (start == text.Length) return false;
        for (int i = start; i < text.Length; i++)
            if (!char.IsAsciiDigit(text[i])) return false;
        return true;
    }

    /// <summary>The items of a tag list, split at the commas that are not inside a function. An
    /// empty item is kept, because a trailing comma is exactly what makes the reference engine drop
    /// the declaration (measured: '"wght" 700,' is no value).</summary>
    private static List<string> SplitTagListItems(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0) { parts.Add(text[start..i]); start = i + 1; }
        }
        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>A tag: four characters between a matched pair of quotes, either kind.</summary>
    private static bool IsTagWord(string word)
    {
        if (word.Length != 6) return false;
        var quote = word[0];
        return (quote == '"' || quote == '\'') && word[^1] == quote;
    }

    /// <summary>Say one of the two tag lists. A feature's <c>on</c> and its <c>1</c> are the same
    /// thing, and the reference engine leaves the number out for both; <c>off</c> is <c>0</c>, which
    /// it says, and a single-quoted tag is printed with double quotes (measured: '"liga" on' reads
    /// '"liga"', '"liga" 1' reads '"liga"', '"liga" off' reads '"liga" 0' and "'SIN' 1" reads
    /// '"SIN" 1'). |inTagOrder| is the difference between the two surfaces: the computed value puts
    /// the items in tag order and lets the last of a repeated tag win, while the specified value
    /// keeps the order the page wrote (measured: '"swsh" 2, "liga"' echoes itself and computes
    /// '"liga", "swsh" 2'; '"liga" 0, "liga" 1' computes the single '"liga"').</summary>
    public static string? CanonicalTagList(string text, bool integerValues, bool inTagOrder)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
        var items = SplitTagListItems(trimmed);
        var entries = new List<(string Tag, string? Value)>(items.Count);
        foreach (var item in items)
        {
            var parts = SplitComponents(item);
            if (parts.Count is < 1 or > 2) return null;
            if (!IsTagWord(parts[0])) return null;
            var tag = "\"" + parts[0][1..^1] + "\"";
            string? value;
            if (parts.Count == 1) value = null;
            else if (!integerValues)
            {
                // A variation keeps every number it is given, its 1 included (measured:
                // '"wght" 700, "opsz" 12, "ital" 1' computes '"ital" 1, "opsz" 12, "wght" 700').
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var variant) || double.IsNaN(variant) || double.IsInfinity(variant)) return null;
                value = CssMathValue.Number(variant);
            }
            else if (parts[1].Equals("on", StringComparison.OrdinalIgnoreCase)) value = null;
            else if (parts[1].Equals("off", StringComparison.OrdinalIgnoreCase)) value = "0";
            else
            {
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var feature) || double.IsNaN(feature) || double.IsInfinity(feature)) return null;
                value = feature == 1 ? null : CssMathValue.Number(feature);
            }
            if (inTagOrder)
            {
                int repeat = entries.FindIndex(existing => existing.Tag == tag);
                if (repeat >= 0) entries[repeat] = (tag, value);
                else entries.Add((tag, value));
            }
            else entries.Add((tag, value));
        }
        if (inTagOrder) entries.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));
        return string.Join(", ", entries.Select(entry =>
            entry.Value == null ? entry.Tag : entry.Tag + " " + entry.Value));
    }

    private static bool IsOneWordList(string text, params string[] words)
    {
        foreach (var part in SplitComponents(text))
            if (!IsOneOf(part, words)) return false;
        return true;
    }

    private static bool IsOneColor(string text)
    {
        var parts = SplitComponents(text);
        return parts.Count == 1 && (ColorParser.IsColorToken(parts[0])
            || parts[0].Equals("currentcolor", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A &lt;length&gt; with no percentage in sight: the outset and the stroke width are
    /// lengths and a percentage is refused by the reference engine (measured both). A bare number
    /// is refused too except for the unitless zero, which both properties take and print as they
    /// were handed it (measured: '-webkit-text-stroke-width: 0' stands while '2' is dropped).</summary>
    /// <summary>A <c>-webkit-text-stroke-width</c>: a plain length, or one of the three border-width
    /// keywords the stroke also takes. They are no fixed pixel counts — the reference engine prices
    /// them as a fraction of the font (measured: 'thin' on a 14px box computes '0.291667px', the font
    /// size over 48), which is why the pricing lives with the computed surface and not here.</summary>
    private static bool IsTextStrokeWidth(string token) =>
        IsPlainLength(token) || IsOneOf(token, "thin", "medium", "thick");

    private static bool IsPlainLength(string token)
    {
        if (token.Length == 0 || token.EndsWith("%", StringComparison.Ordinal)) return false;
        if (!IsLengthOrPercentage(token)) return false;
        if (HasUnit(token)) return true;
        return decimal.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var bare) && bare == 0;
    }

    private static bool HasUnit(string token)
    {
        int i = token.Length - 1;
        while (i >= 0 && (char.IsLetter(token[i]) || token[i] == '%')) i--;
        return i < token.Length - 1;
    }

    /// <summary>The group number as &lt;integer&gt; spells it — an optional sign and digits, with no
    /// fraction and no exponent, and from one up (measured: '1e2' and '1.5' and '0' are all dropped
    /// where '2' and '+2' stand).</summary>
    private static bool IsLegacyInteger(string token)
    {
        int i = 0;
        if (i < token.Length && token[i] is '+' or '-') i++;
        if (i == token.Length) return false;
        for (; i < token.Length; i++)
            if (!char.IsAsciiDigit(token[i])) return false;
        return int.TryParse(token.TrimStart('+'), out var value) && value >= 1;
    }

    /// <summary>&lt;number&gt; as the grammar spells it, exponent included: the flex factor is the
    /// one legacy property that takes it (measured: '1e3' stands and reads back '1000').</summary>
    private static bool IsLegacyNumber(string token)
    {
        return double.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    private static bool IsPercentage(string token) =>
        token.EndsWith("%", StringComparison.Ordinal) && IsLengthOrPercentage(token);

    private static bool IsLegacyColor(string token) =>
        ColorParser.IsColorToken(token) || token.Equals("currentcolor", StringComparison.OrdinalIgnoreCase);

    /// <summary>An image function, which is what a mask border source and a mask border shorthand
    /// open with.</summary>
    private static bool IsImageFunction(string token)
    {
        int open = token.IndexOf('(');
        if (open <= 0) return false;
        var name = token[..open];
        if (name.Equals("url", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var image in ImageFunctionNames)
            if (name.Equals(image, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>True when the text is one image function and nothing else — the closing parenthesis
    /// that ends it is the last character, so 'url(a.png) none' is not a single image.</summary>
    private static bool IsOneImageFunction(string text)
    {
        if (!IsImageFunction(text)) return false;
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < text.Length) i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i == text.Length - 1;
        }
        return false;
    }

    private static readonly string[] ImageFunctionNames =
    {
        "-webkit-linear-gradient", "-webkit-repeating-linear-gradient",
        "-webkit-radial-gradient", "-webkit-repeating-radial-gradient",
        "linear-gradient", "repeating-linear-gradient", "radial-gradient",
        "repeating-radial-gradient", "conic-gradient", "repeating-conic-gradient",
        // 'image-set()' with its legacy twin, and the paint-worklet function of the Houdini draft.
        "image-set", "-webkit-image-set", "paint",
        // 'cross-fade()' and 'element()' are in the spec's <image> and in no engine this round
        // measured: each of the four image properties refuses them, in the longhand and in the
        // shorthand alike (snapshots/out/_b294_ref_probeE.txt rows 39 and 40, and
        // _b294_ref_probeF.txt's 'background-image', 'mask-image', 'list-style-image' and
        // 'border-image-source' blocks).
    };

    /// <summary>The grammar to gate a declaration by, or null when this engine cannot decide
    /// the property's value set from its text.</summary>
    private static Func<string, bool>? GrammarFor(CssPropertyId id) => id switch
    {
        // ===== The closed keyword sets measured value by value in b262. Each of these properties
        // takes exactly one word, and a second word — even a word of its own vocabulary — is no
        // value at all ('word-break: normal break-word', 'user-select: none auto',
        // 'overscroll-behavior-x: nodefault').
        CssPropertyId.UserSelect => static text => IsSingleKeywordWord(text,
            new[] { "auto", "text", "none", "all" }),
        CssPropertyId.OverscrollBehaviorX or CssPropertyId.OverscrollBehaviorY
            => static text => IsSingleKeywordWord(text, new[] { "auto", "contain", "none" }),
        CssPropertyId.WordBreak => static text => IsSingleKeywordWord(text,
            new[] { "normal", "break-all", "keep-all", "break-word", "auto-phrase" }),
        // CSS Text 3 §5.1 and CSS Text 4 §4.5: 'text-justify' is one word, and the legacy
        // 'distribute' is answered as the modern 'inter-character' (measured, both surfaces).
        CssPropertyId.TextJustify => static text => IsSingleKeywordWord(text,
            new[] { "auto", "inter-word", "distribute", "inter-character", "none" }),
        // CSS Overscroll Behavior 1 §3: one behaviour per axis, and the pair shorthand takes one
        // or two of them; 'pan-contain' is a later draft this engine does not answer.
        CssPropertyId.OverscrollBehavior or CssPropertyId.OverscrollBehaviorBlock
            or CssPropertyId.OverscrollBehaviorInline
            => static text => IsOneOrTwoKeywords(text, "auto", "contain", "none"),
        // CSS Masking 1 §11.3: one composite per layer, so two keywords inside one layer are no
        // value ('intersect exclude' falls) while a comma list of them is a list of layers.
        CssPropertyId.MaskComposite => static text =>
        {
            var layers = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(text, ',');
            if (layers.Count == 0) return false;
            return layers.All(layer => IsSingleKeywordWord(layer,
                new[] { "add", "subtract", "intersect", "exclude" }));
        },
        // CSS Masking 1 §11.2: one colour space per layer, so 'bogus' is nobody's mask mode and two
        // words inside one layer are no value (measured through 'mask-mode' and 'CSS.supports').
        CssPropertyId.MaskMode => static text =>
        {
            var layers = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(text, ',');
            if (layers.Count == 0) return false;
            return layers.All(layer => IsSingleKeywordWord(layer,
                new[] { "match-source", "luminance", "alpha" }));
        },
        CssPropertyId.TextWrap => IsTextWrapValue,
        // CSS Text 4 §1: ONE of the six words. The two-value form the current draft adds
        // ('white-space: pre nowrap') and the 'stable' keyword are not values this engine's
        // reference takes (measured: 'normal nowrap', 'pre normal', 'break-spaces normal' and
        // 'stable' are each dropped, with the property left at its 'normal').
        CssPropertyId.WhiteSpace => static text => IsOneWordOnly(text,
            "normal", "pre", "nowrap", "pre-wrap", "pre-line", "break-spaces"),
        // CSS Cascades 4 §4.6: 'all' takes nothing but the keywords that stand for a whole value.
        // 'none' and 'normal' are not among them (measured: both read back empty).
        CssPropertyId.All => static text => IsOneWordOnly(text,
            "initial", "inherit", "unset", "revert", "revert-layer"),
        // CSS Text 3 §5.3: one of three words, and a second is no value
        // (measured: 'hyphens: none auto' is dropped).
        CssPropertyId.Hyphens => static text => IsOneWordOnly(text, "none", "manual", "auto"),
        // CSS Text 3 §3.4: ONE number or length, and never a negative one (measured: 'tab-size: 2 4'
        // and 'tab-size: -1' are dropped while '1.5' and '4em' stand; 'normal' and 'auto' are not
        // values of this property either, and neither is a percentage — '50%' of nothing advances
        // nothing, and the reference engine drops it).
        CssPropertyId.TabSize => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count != 1) return false;
            var word = words[0];
            if (word.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || word.Equals("auto", StringComparison.OrdinalIgnoreCase)) return false;
            if (word.StartsWith("-", StringComparison.Ordinal)) return false;
            // A number written in scientific notation is still the count of advance positions it
            // names, and the echo prints the plain spelling (measured: 'tab-size: 1e2' reads '100').
            return IsNumber(word) || IsDimensionedLength(word) || IsPlainNumber(word);
        },
        // 'symbols()' is a @counter-style descriptor, not a value of 'list-style-type' here
        // (measured: the declaration is dropped and the property stays 'disc'), while a quoted
        // string is a marker the engine keeps.
        CssPropertyId.ListStyleType => IsListStyleTypeValue,
        // CSS Text 3 §5.2 as this engine reads it: the flags come with one length, and an alias
        // word ('cap') is not one of them (measured).
        CssPropertyId.TextIndent => static text =>
            Acrux.Core.Css.CssValueText.CanonicalizeTextIndent(text, "text-indent") != null,
        CssPropertyId.AspectRatio => static text =>
            Acrux.Core.Css.CssValueText.CanonicalizeAspectRatio(text) != null,
        // The individual 'rotate' property (CSS Transforms 2 §4.2) is an angle or an axis and an
        // angle — 'none', '45deg', 'x 0.5turn', '1 0 0 90deg' — and an angle is never a bare number.
        // A math function is left to the type check below (which already prices it as an angle), so
        // only a plain value is judged here; the same reader the computed value uses decides it, which
        // is why 'rotate: 45' is refused on every surface (measured: the echo reads back empty).
        CssPropertyId.Rotate => static text =>
            text.IndexOf('(') >= 0 || Acrux.Core.Css.IndividualTransforms.CanonicalRotate(text) != null,
        // CSS Lists 3 §4.3/§4.4: a counter is a name plus an integer, and the integer is written
        // even when the page left it out. 'none' is a value of the whole property, never of one
        // counter, and 'reverse' is simply a name (both measured).
        CssPropertyId.CounterReset or CssPropertyId.CounterSet
            or CssPropertyId.CounterIncrement => IsCounterList,
        // CSS Transitions 1 §4.1 / CSS Animations 1 §3: a '<time>' is a number carrying the unit 's'
        // or 'ms', and the list is one time per item. No other unit spells a time — 'min' is a length
        // unit and the word round() takes, but it is never a duration, so the reference engine drops
        // 'transition-duration: 2min' entirely (measured: the echo, CSS.supports and the declaration
        // form all three refuse it). A duration also refuses a literal negative time ('-2s' is dropped,
        // though '-0s' is zero and reads back '0s'); a delay takes a negative happily. A calc() that
        // evaluates negative is NOT refused here — the grammar cannot price it — it is clamped to '0s'
        // at computed-value time instead (measured: 'transition-duration: calc(-1s)' echoes as written
        // and computes '0s', while the same calc on a delay computes '-1s').
        CssPropertyId.TransitionDuration or CssPropertyId.AnimationDuration
            => static text => IsTimeList(text, nonNegative: true),
        CssPropertyId.TransitionDelay or CssPropertyId.AnimationDelay
            => static text => IsTimeList(text),
        // CSS Text Decoration 4 §2: any number of the four decoration lines (each at most once), at
        // most one style, at most one thickness and at most one colour, and the three lines that
        // stand alone ('none', 'spelling-error', 'grammar-error') stand alone. Every refusal named in
        // the helper's comment is a value the reference engine drops.
        CssPropertyId.TextDecoration => IsTextDecorationShorthand,
        // CSS Flexbox 1 §4: 'flex-flow' is the pair of its two longhands — at most one direction
        // and at most one wrap, and a word that is neither is no value at all (measured:
        // 'flex-flow: wrap reverse' reads back the empty string while 'row wrap' and
        // 'column-reverse nowrap' stand).
        CssPropertyId.FlexFlow => IsFlexFlowValue,
        // CSS Flexbox 1 §7.1.1/§7.2: the shorthand is the three parts and nothing else, the two
        // factors are counts from zero up, and a basis is 'auto', 'content' or one
        // &lt;length-percentage&gt; that is not negative. Measured refusals: 'flex: none 1',
        // 'flex: 1 1 auto extra', 'flex: 1 2 3 4px', 'flex: auto auto', 'flex: 0% 0%',
        // 'flex: 1 1 -5px', 'flex-grow: none', 'flex-grow: -1', 'flex-grow: 20%',
        // 'flex-shrink: none', 'flex-basis: -5px', 'flex-basis: 1 2', 'flex-basis: auto auto' and
        // 'flex-basis: bogus' each leave no declaration.
        CssPropertyId.Flex => static text => ShorthandExpander.ParseFlexParts(text) != null,
        // CSS Text-Emphasis 1 §3.1/§3.2: the mark is at most one fill and one shape (or a string in
        // place of both, or 'none' alone), and the shorthand adds at most one colour to it. The
        // measured refusals are 'text-emphasis: normal', '… : auto dot', '… : dot auto',
        // '… : dot filled extra', '… : dot filled filled', '… : over dot', '… : dot red blue',
        // 'text-emphasis-style: normal', '… : auto dot', '… : dot dot', '… : filled open' and
        // '… : triangle red'.
        CssPropertyId.TextEmphasis => static text =>
            ShorthandExpander.ParseTextEmphasisParts(text) != null,
        CssPropertyId.TextEmphasisStyle => static text =>
            ShorthandExpander.ParseEmphasisStyle(text) != null,
        CssPropertyId.FlexGrow or CssPropertyId.FlexShrink => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count != 1) return false;
            if (IsMathFunctionCall(words[0], IsLengthOrPercentage)) return true;
            return IsNonNegativeCount(words[0]);
        },
        CssPropertyId.FlexBasis => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count != 1) return false;
            var word = words[0];
            if (word.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || word.Equals("content", StringComparison.OrdinalIgnoreCase)) return true;
            if (IsMathFunctionCall(word, IsLengthOrPercentage)) return true;
            return !IsFitContentFunction(word) && IsNonNegativeBoxLength(word);
        },
        // CSS Color Adjustment 1 §5.2: 'normal' alone, or a list of scheme keywords and custom
        // names. Measured word by word: 'light dark', 'light only' and even 'brown' stand, 'normal
        // dark' and 'only' alone fall, 'light light' stands (a duplicate is not a second grammar
        // slot), and the three words are not equally fussy about their case — 'Light' and 'DARK' are
        // the keywords, while 'ONLY' is neither the keyword nor a name.
        CssPropertyId.ColorScheme => static text =>
            // A comma would be a list of values, and this property's list is space-separated
            // (measured: 'color-scheme: light, dark' is dropped).
            !text.Contains(',', StringComparison.Ordinal) && CanonicalColorScheme(text) != null,
        // CSS Scrollbars 1 §3.1: 'scrollbar-color' is 'auto' or exactly TWO colours — the thumb and
        // the track. One colour alone is no value (measured: 'scrollbar-color: red' is dropped
        // while 'red blue' and 'currentcolor blue' both stand).
        CssPropertyId.ScrollbarColor => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count == 1 && words[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
                return true;
            return words.Count == 2 && words.All(ColorParser.IsColorToken);
        },
        // CSS UI 4 §3.11: ONE keyword. 'both horizontal' is two of them, and the reference engine
        // drops it (measured); 'block' and 'inline' are values this engine was missing.
        CssPropertyId.Resize => static text =>
            IsOneWordOnly(text, "none", "both", "horizontal", "vertical", "block", "inline",
            "auto"),
        // CSS Box Decoration Break 1 §3: one of the two, never both ('slice clone' is dropped).
        CssPropertyId.BoxDecorationBreak => static text => IsOneWordOnly(text, "slice", "clone"),
        // CSS Overflow 3 §4.1: the box it measures from, the length, or the two together — in either
        // order, and never two boxes or two lengths (measured: 'content-box border-box' and
        // '10px 10px' are both dropped while '10px content-box' stands). 'padding-box' is the word
        // the reference engine still takes, and it is the one that prints as the length.
        CssPropertyId.OverflowClipMargin => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count is < 1 or > 2) return false;
            string? box = null, length = null;
            foreach (var word in words)
            {
                var p = word.ToLowerInvariant();
                if (p is "content-box" or "border-box" or "padding-box")
                {
                    if (box != null) return false;
                    box = p;
                }
                // A bare number is no length for this property (measured: 'overflow-clip-margin: 0'
                // falls while '0px' stands), which is the one place the zero is not allowed through.
                else if (length == null && IsDimensionedLength(word)) length = p;
                else return false;
            }
            return box != null || length != null;
        },
        // CSS Text Emphasis 1 §3.3: the vertical side, optionally with the horizontal one beside it
        // — and the two may come in either order, which is what the '||' in the grammar says
        // (measured: 'left over' stands and reads back 'over left', while a lone 'left' falls).
        CssPropertyId.TextEmphasisPosition => static text =>
            CanonicalTextEmphasisPosition(text) != null,
        // CSS Fonts 3 §7.4: 'none', a ratio, or the font's own aspect value. A percentage is not a
        // ratio here (measured: 'font-size-adjust: 50%' is dropped).
        CssPropertyId.FontSizeAdjust => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count != 1) return false;
            var p = words[0].ToLowerInvariant();
            if (p is "none" or "from-font") return true;
            // A ratio written as a math function is a ratio the engine has not finished computing
            // (measured: 'font-size-adjust: calc(0.5)' stands and computes '0.5').
            if (IsMathFunctionCall(p, IsNumber)) return true;
            return !p.EndsWith("%", StringComparison.Ordinal)
                && !p.EndsWith("px", StringComparison.Ordinal)
                && double.TryParse(p, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ratio)
                && !double.IsNaN(ratio) && ratio >= 0;
        },
        // CSS Fonts 4 §8.6 and §10.1: a list of four-character tags, each with its value. The
        // reference engine refuses a three- or five-character tag, an unquoted one, a decimal
        // feature index and a 'normal' written beside a tag (measured, case by case). The
        // 'font-variant-*' sets, 'font-palette' and 'font-language-override' are read by the
        // echo-only table instead, which is their one origin.
        CssPropertyId.FontVariationSettings => static text => IsTagSettingList(text, integerValues: false),
        CssPropertyId.FontFeatureSettings => static text => IsTagSettingList(text, integerValues: true),
        // CSS Fonts 4 §8.7.1 and §8.5: one keyword each, and a second is no value
        // (measured: 'small-caps small-caps', 'petite-caps unicase' and 'sub super' are dropped).
        CssPropertyId.FontVariantCaps => static text => IsOneWordOnly(text,
            "normal", "small-caps", "all-small-caps", "petite-caps", "all-petite-caps",
            "unicase", "titling-caps"),
        CssPropertyId.FontOpticalSizing => static text => IsOneWordOnly(text, "auto", "none"),
        CssPropertyId.FontKerning => static text => IsOneWordOnly(text, "auto", "normal", "none"),
        // CSS Writing Modes 3 §3.2/§4.1: the axis keywords, and nothing else. 'vertical' and
        // 'sideways' are not values of 'writing-mode', and 'sideways-lr vertical-rl' is two of them
        // CSS Writing Modes 4 §5.2: the six modes with the one-word legacy spellings the reference
        // engine still reads and re-prices — 'tb' is a vertical mode and 'lr', 'rl' are horizontal
        // ones (measured, each: the echo keeps the word the page wrote and the computed value is
        // the mode that word says). 'vertical' and 'sideways' are not values of 'writing-mode', and
        // 'sideways-lr vertical-rl' is two of them (each measured: dropped, with the property left
        // at its initial).
        CssPropertyId.WritingMode => static text => IsOneWordOnly(text,
            "horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr",
            "lr-tb", "tb-rl", "tb-lr", "rl-tb", "lr", "rl", "tb"),
        // CSS Text 4 §2.1: the five keywords. The legacy '-isolate' and '-plaintext' spellings are
        // not values the reference engine takes any more (measured: both read back empty).
        CssPropertyId.Isolation => static text => IsOneWordOnly(text, "auto", "isolate"),
        CssPropertyId.Display => static text => CssPropertyApplier.TryParseDisplay(text, out _, out _),
        CssPropertyId.Float => static text => CssFloatKeywords.TryParseFloat(text, out _),
        CssPropertyId.Clear => static text => CssFloatKeywords.TryParseClear(text, out _),
        // CSS Fonts 4 §1.2: an absolute or relative size keyword, a system font keyword, or a
        // length/percentage. 'math' and 'auto' are the keywords text-size-adjust added.
        CssPropertyId.FontSize => static text => IsNonNegativeLengthList(text,
            "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large",
            "larger", "smaller", "caption", "icon", "menu", "message-box", "small-caption",
            "status-bar", "math", "auto"),
        // CSS Text 4 §5.1: a length or a percentage plus the 'normal' keyword — and the number has
        // to say what it is measured in, so 'letter-spacing: 1' is dropped while 'letter-spacing: 0'
        // and '50%' stand (measured, all three; the percentage is also what the computed value keeps).
        CssPropertyId.LetterSpacing or CssPropertyId.WordSpacing
            => static text => SplitComponents(text).Count >= 1 && SplitComponents(text).All(word =>
                word.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || IsDimensionedLengthPercentageOrZero(word)),
        // CSS Sizing 3, measured keyword by keyword and one component at a time: a size takes ONE
        // value, so 'width: auto 30px' and 'max-width: none none' are no declaration. 'auto' belongs
        // to the sizes and the minimums, 'none' ONLY to the maximums (the reference engine drops
        // 'width: none' and 'min-width: none', which is why a block with 'min-width: none' keeps the
        // 'auto' it had, and drops 'max-width: auto' just as surely). 'contain' is gone from every
        // one of the eight — it was in an older draft of the keyword set and the engine still
        // answered for it, but 'min-width: contain' and 'max-width: contain' are both dropped now
        // (measured). The logical pair takes the same set as its physical twin.
        CssPropertyId.Width or CssPropertyId.Height
            or CssPropertyId.InlineSize or CssPropertyId.BlockSize
            or CssPropertyId.MinWidth or CssPropertyId.MinHeight
            or CssPropertyId.MinInlineSize or CssPropertyId.MinBlockSize
            => static text => IsNonNegativeSingleLength(text,
                "auto", "min-content", "max-content", "fit-content", "stretch",
                // The legacy spelling of 'stretch' the engine still answers, and the prefixed
                // form pages use today.
                "-webkit-fill-available", "-webkit-fit-content"),
        CssPropertyId.MaxWidth or CssPropertyId.MaxHeight
            or CssPropertyId.MaxInlineSize or CssPropertyId.MaxBlockSize
            => static text => IsNonNegativeSingleLength(text,
                "none", "min-content", "max-content", "fit-content", "stretch",
                "-webkit-fill-available", "-webkit-fit-content"),
        // CSS Scroll Snap 1 §6.1, measured side by side: a margin side is a length and nothing
        // else — 'auto' and '50%' are both dropped — while a padding side is a length, a percentage
        // or the word 'auto', which is what it prints back for the zero the word means. A longhand
        // takes exactly one of them, the logical pair one or two ('scroll-margin-inline: 1px 2px 3px'
        // is no value) and the box shorthand up to four.
        // CSS Backgrounds 3 §5: each part has its own number set, and the shorthand says all five.
        CssPropertyId.BorderImageSlice => static text => CanonicalBorderImageSlice(text) != null,
        CssPropertyId.BorderImageWidth => static text => CanonicalBorderImageWidth(text) != null,
        CssPropertyId.BorderImageOutset => static text => CanonicalBorderImageOutset(text) != null,
        CssPropertyId.BorderImageRepeat => static text => CanonicalBorderImageRepeat(text) != null,
        CssPropertyId.BorderImage => static text => CanonicalBorderImageShorthand(text) != null,
        CssPropertyId.ScrollMarginTop or CssPropertyId.ScrollMarginRight
            or CssPropertyId.ScrollMarginBottom or CssPropertyId.ScrollMarginLeft
            or CssPropertyId.ScrollMarginBlockStart or CssPropertyId.ScrollMarginBlockEnd
            or CssPropertyId.ScrollMarginInlineStart or CssPropertyId.ScrollMarginInlineEnd
            => static text => IsScrollInset(text, 1, padding: false),
        CssPropertyId.ScrollPaddingTop or CssPropertyId.ScrollPaddingRight
            or CssPropertyId.ScrollPaddingBottom or CssPropertyId.ScrollPaddingLeft
            or CssPropertyId.ScrollPaddingBlockStart or CssPropertyId.ScrollPaddingBlockEnd
            or CssPropertyId.ScrollPaddingInlineStart or CssPropertyId.ScrollPaddingInlineEnd
            => static text => IsScrollInset(text, 1, padding: true),
        CssPropertyId.ScrollMarginBlock or CssPropertyId.ScrollMarginInline
            => static text => IsScrollInset(text, 2, padding: false),
        CssPropertyId.ScrollPaddingBlock or CssPropertyId.ScrollPaddingInline
            => static text => IsScrollInset(text, 2, padding: true),
        CssPropertyId.ScrollMargin => static text => IsScrollInset(text, 4, padding: false),
        CssPropertyId.ScrollPadding => static text => IsScrollInset(text, 4, padding: true),
        CssPropertyId.Margin or CssPropertyId.Padding
            or CssPropertyId.Inset
            // The four-edge shorthands take one to four values and no more: a fifth is no
            // declaration at all (measured on 'margin:1px 1px 1px 1px 1px').
            => static text => IsLengthListUpTo(text, 4, "auto"),
        CssPropertyId.MarginBlock or CssPropertyId.MarginInline
            or CssPropertyId.PaddingBlock or CssPropertyId.PaddingInline
            or CssPropertyId.InsetBlock or CssPropertyId.InsetInline
            => static text => IsLengthListUpTo(text, 2, "auto"),
        // An edge takes one value and not two: 'margin-left: 1px 2px' is no declaration, the same
        // way 'width: auto 30px' is not one (measured) — the second word belongs to the shorthand
        // that writes four edges, not to the edge itself.
        CssPropertyId.MarginTop or CssPropertyId.MarginRight
            or CssPropertyId.MarginBottom or CssPropertyId.MarginLeft
            or CssPropertyId.MarginBlockStart or CssPropertyId.MarginBlockEnd
            or CssPropertyId.MarginInlineStart or CssPropertyId.MarginInlineEnd
            or CssPropertyId.Top or CssPropertyId.Right
            or CssPropertyId.Bottom or CssPropertyId.Left
            or CssPropertyId.InsetBlockStart or CssPropertyId.InsetBlockEnd
            or CssPropertyId.InsetInlineStart or CssPropertyId.InsetInlineEnd
            => static text => IsSingleLength(text, "auto"),
        CssPropertyId.PaddingTop or CssPropertyId.PaddingRight
            or CssPropertyId.PaddingBottom or CssPropertyId.PaddingLeft
            or CssPropertyId.PaddingBlockStart or CssPropertyId.PaddingBlockEnd
            or CssPropertyId.PaddingInlineStart or CssPropertyId.PaddingInlineEnd
            => static text => IsNonNegativeSingleLength(text),
        // CSS 2.1 §17.3: a border side's width is <line-width> — a non-negative length or one of
        // the three words — and ONE of them per side. 'auto' is the word of 'border-image-width'
        // and of no border side (measured: 'border-top-width: auto' is dropped), a percentage is
        // not a width either, and neither is a negative length or a second value.
        CssPropertyId.BorderTopWidth or CssPropertyId.BorderRightWidth
            or CssPropertyId.BorderBottomWidth or CssPropertyId.BorderLeftWidth
            => static text => IsLineWidthList(text, 1),
        // The logical border widths measure the pair of edges, so one value for both or two for
        // each edge, and nothing else (measured: 'border-block-width: 1px 2px' stands).
        CssPropertyId.BorderBlockWidth or CssPropertyId.BorderInlineWidth
            => static text => IsLineWidthList(text, 2),
        CssPropertyId.BorderBlockStartWidth or CssPropertyId.BorderBlockEndWidth
            or CssPropertyId.BorderInlineStartWidth or CssPropertyId.BorderInlineEndWidth
            => static text => IsLineWidthList(text, 1),
        // 'outline-width' is the same family with a narrower set: no 'auto' (that word belongs to
        // the style here), no bare number and no percentage, and nothing negative (all measured) —
        // which is why it stands apart from the four border widths above.
        // CSS Multi-column 1 §6: 'auto' lets the column width be calculated.
        CssPropertyId.ColumnWidth => static text => IsLengthList(text, "auto"),
        // CSS Box Alignment 3 §3.7 and CSS Gap 1 §3: 'normal' or a length, one for each axis. The
        // shorthand takes at most two, so a third is no value (measured: 'gap: 10px 20px 30px' is
        // dropped while 'gap: 10px 20px' and 'gap: normal' stand), and a longhand takes exactly one.
        CssPropertyId.Gap => static text => IsGapValue(text, 2),
        CssPropertyId.RowGap or CssPropertyId.ColumnGap
            => static text => IsGapValue(text, 1),
        CssPropertyId.Color or CssPropertyId.BackgroundColor or CssPropertyId.OutlineColor
            or CssPropertyId.BorderTopColor or CssPropertyId.BorderRightColor
            or CssPropertyId.BorderBottomColor or CssPropertyId.BorderLeftColor
            => static text => IsColorList(text),
        // CSS Multi-column 1 §5.2: one colour for the rule — the two-colour shape a physical side
        // takes is not in this property's grammar (measured: 'column-rule-color: red blue' falls).
        // CSS Multi-Column 1 §3.5: the rule is a width, a line style and a colour — in any order,
        // each of them at most once. An <image> is not one of the three, which is what separates
        // this shorthand from the border shorthands that share its shape
        // (measured: 'column-rule: 1px solid red' stands while 'column-rule: url(a.png)' and
        // 'column-rule: linear-gradient(red, blue)' are each dropped).
        CssPropertyId.ColumnRule => IsColumnRuleShorthandValue,
        CssPropertyId.ColumnRuleColor => IsOneColor,
        // CSS Logical 1 §4: the logical border colour pair is the two edges' colours, and the
        // shorthand of the whole axis takes one colour for both (measured:
        // 'border-block-color: red blue' stands while 'border-block: red blue' falls).
        CssPropertyId.BorderBlockColor or CssPropertyId.BorderInlineColor
            => static text => IsTwoColors(text),
        // The background geometry longhands (CSS Backgrounds 3 §4.1, CSS Position 3 §5.2), gated
        // by the same matchers the 'background' shorthand expands through so that the two cannot
        // disagree. Gating here rather than only at application time is what keeps the rule below
        // alive: a declaration the grammar rejects is dropped from the cascade, so
        // 'background-position: top 10px' on a box the stylesheet had put at '7px 9px' reads back
        // as '7px 9px' and not as the initial '0% 0%' (measured).
        // CSS Box Alignment 3 §6/§8: the six alignment properties share the keyword family of
        // §4 and divide five of its words between them. Measured value by value
        // (snapshots/out/_b294_ref_probeF.txt), 'auto' is a word of self alignment alone
        // ('align-items: auto' and 'justify-items: auto' both fall), 'left' and 'right' belong to
        // the content distribution and to 'justify-items' ('align-content: left' and
        // 'align-items: left' fall), 'baseline' is nobody's 'justify-content' while 'last
        // baseline' is nobody's 'align-content', and the legacy family is 'justify-items'' own
        // tail. The gate is the same function the cascade uses, so the echo, supports() and the
        // stored value cannot disagree about which words are values.
        CssPropertyId.AlignContent => static text =>
            Acrux.Core.Css.Resolver.CssPropertyApplier.IsAlignmentValue(text,
                Acrux.Core.Css.Resolver.CssPropertyApplier.AlignContentWords),
        CssPropertyId.JustifyContent => static text =>
            Acrux.Core.Css.Resolver.CssPropertyApplier.IsAlignmentValue(text,
                Acrux.Core.Css.Resolver.CssPropertyApplier.JustifyContentWords),
        CssPropertyId.AlignItems => static text =>
            Acrux.Core.Css.Resolver.CssPropertyApplier.IsAlignmentValue(text,
                Acrux.Core.Css.Resolver.CssPropertyApplier.AlignItemsWords),
        CssPropertyId.JustifyItems => static text =>
            Acrux.Core.Css.Resolver.CssPropertyApplier.IsAlignmentValue(text,
                Acrux.Core.Css.Resolver.CssPropertyApplier.JustifyItemsWords),
        CssPropertyId.AlignSelf => static text =>
            Acrux.Core.Css.Resolver.CssPropertyApplier.IsAlignmentValue(text,
                Acrux.Core.Css.Resolver.CssPropertyApplier.AlignSelfWords),
        CssPropertyId.JustifySelf => static text =>
            Acrux.Core.Css.Resolver.CssPropertyApplier.IsAlignmentValue(text,
                Acrux.Core.Css.Resolver.CssPropertyApplier.JustifySelfWords),
        // CSS Box Alignment 3 §6: the 'place-*' shorthands are the pair of two of the properties
        // above, so each half is judged by the words the longhand it writes answers for — which is
        // why 'place-content: left' falls (align-content has no 'left' to take the first half)
        // while 'place-self: left' falls for the block half and 'justify-self: left' stands
        // (all measured).
        CssPropertyId.PlaceContent => static text => IsPlacePairValue(text,
            Acrux.Core.Css.Resolver.CssPropertyApplier.AlignContentWords,
            Acrux.Core.Css.Resolver.CssPropertyApplier.JustifyContentWords),
        CssPropertyId.PlaceItems => static text => IsPlacePairValue(text,
            Acrux.Core.Css.Resolver.CssPropertyApplier.AlignItemsWords,
            Acrux.Core.Css.Resolver.CssPropertyApplier.JustifyItemsWords),
        CssPropertyId.PlaceSelf => static text => IsPlacePairValue(text,
            Acrux.Core.Css.Resolver.CssPropertyApplier.AlignSelfWords,
            Acrux.Core.Css.Resolver.CssPropertyApplier.JustifySelfWords),
        CssPropertyId.BackgroundPosition => ShorthandExpander.IsBackgroundPositionValue,
        // CSS Images 3 §5.2 and CSS Position 3 §5.2: 'object-position' takes the same grammar as a
        // background position except for one case — when two side keywords are written, each of
        // them has to carry its own length (measured: 'top left 10px' is dropped here while
        // 'background-position' still takes it, and 'right 10px bottom' is dropped with it).
        CssPropertyId.ObjectPosition => static text =>
            ShorthandExpander.IsBackgroundPositionValue(text) && SaysBothOrNeither(text),
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
        // CSS Backgrounds 3 §4.4 and CSS Masking 1 §6.1: each layer of the image list is an image
        // and nothing else — a colour, a keyword or two words in one layer are no image, and
        // 'cross-fade()'/'element()' are in the spec's <image> but in no engine's table (measured).
        // 'background' keeps its own gate: there a colour IS one of the parts.
        CssPropertyId.BackgroundImage or CssPropertyId.MaskImage
            => static text => !NamesAFontLocal(text) && SaysImages(text),
        CssPropertyId.Background => static text => !NamesAFontLocal(text),
        // CSS Masking 1 §9.2: the mask's repeat is the background's repeat, one pair per layer —
        // 'repeat-x repeat-y' is two single-axis words side by side and no layer has that shape
        // (measured, and the same for the three-term spelling).
        CssPropertyId.MaskRepeat => ShorthandExpander.IsBackgroundRepeatValue,
        // CSS Lists 3 §3.3: the shorthand's three parts, at most one of each. 'auto' is a marker
        // type of its own and 'disc' is another, so the pair 'auto disc' says two types and is no
        // value; two images do the same (measured, both, and 'none none' — the image and the type —
        // is the one two-word shape that stands).
        CssPropertyId.ListStyle => IsListStyleShorthand,
        // CSS Masking 1 §5.1: a mask layer takes no <color> at all — the shorthand of the same
        // shape that the background is keeps one, and this one does not — and no word outside its
        // own list. Measured: 'mask: red' and 'mask: url(a.png) red' both read back empty.
        CssPropertyId.Mask => static text => !NamesAFontLocal(text)
            && Acrux.Core.Css.MaskLayerParser.IsMaskLayerList(text),
        // CSS Containment 3 §2.1: 'contain' is either one of the three set keywords standing
        // alone, or a duplicate-free list of the five kinds. Measured value by value:
        // 'size inline-size' is refused because 'size' already names the axis 'inline-size'
        // names, 'strict style' and 'none style' are refused because a set keyword says the
        // whole list, 'style style' is refused, and 'size layout style paint' is accepted and
        // computes 'strict' — so the refusal has to happen here, in the echo and in supports(),
        // and not only in the applier, which has no way to say the declaration was never stored.
        CssPropertyId.Contain => IsContainValue,
        // CSS Filter Effects 1 §29.1: the list is judged by the printer that would have to read it,
        // so 'grayscale' without its parentheses, a negative blur, a comma list and a colour in the
        // middle of a drop-shadow all refuse the declaration rather than computing 'none' from a
        // value the page can still read back (all measured).
        CssPropertyId.Filter or CssPropertyId.BackdropFilter
            => static text => CssFilterValue.IsFilterValueList(text),        // CSS Containment 3 §3.1: the 'contain-intrinsic' family is one or two components — the
        // shorthand takes the pair, each axis longhand exactly one — of 'none', 'auto' or a
        // non-negative unit-bearing length. Measured: 'auto' and 'auto auto' are refused while
        // 'auto none' stands, '50%' and '-5px' and 'none none none' are refused, and a bare '2'
        // is no length here.
        CssPropertyId.ContainIntrinsicSize => static text => IsContainIntrinsicValue(text, 2),
        // The four axis longhands take ONE size, with the remembered-size 'auto' in front of it —
        // 'auto 30px' is a value of 'contain-intrinsic-width' too (measured), which is why the
        // 'auto' is a prefix here and not one of the counted components.
            CssPropertyId.ContainIntrinsicWidth or CssPropertyId.ContainIntrinsicHeight
            or CssPropertyId.ContainIntrinsicInlineSize or CssPropertyId.ContainIntrinsicBlockSize
            => static text => IsContainIntrinsicValue(text, 1),
        // CSS Containment 3 §2.5: one keyword. 'match-none', the second word an older draft
        // spelled beside 'hidden', is refused here too (measured: 'content-visibility: hidden
        // match-none' reads back the empty string while 'hidden' reads back 'hidden').
        CssPropertyId.ContentVisibility => static text => IsSingleKeywordWord(text,
            new[] { "visible", "auto", "hidden" }),
        // CSS Transforms 2 §3: 'none' or a list of transform functions. A bare identifier names no
        // transform, which is the whole of the difference between 'transform: bogus' — no condition
        // at all — and 'transform: translateX(1px)' (measured through 'supports()').
        CssPropertyId.Transform => IsTransformList,
        // CSS Transitions 1 §2: a comma-separated list, each item a property name with a duration,
        // an optional delay and an optional timing function, in any order. Two bare names in one
        // item is the shape that has no reading, and it is what 'transition: bogus bogus' is
        // (measured false), while 'transition: all 1s' is one (measured true).
        CssPropertyId.Transition => IsTransitionList,
        // Numbers and integers, with the keywords the property adds: 'opacity' takes any number
        // (out of range is clamped later, not rejected now) and 'z-index' takes an integer or
        // 'auto' (measured: 'opacity: bogus' and 'z-index: bogus' are both no condition).
        CssPropertyId.Opacity => static text =>
            SplitComponents(text).Count == 1 && IsOpacityNumber(text.Trim()),
        CssPropertyId.ZIndex => static text => IsOneOf(text, "auto") || IsIntegerList(text),
        // CSS 2.1 §10.8 / CSS Line Grid: a used line height of less than nothing is out of range,
        // so a negative length and a negative number are each dropped ('-1px', '-1'), while the
        // zero that spells itself either way stands (measured: '-0px' and '0' are both values).
        CssPropertyId.LineHeight => static text =>
            (IsLengthList(text, "normal") || IsNumberOnly(text)) && !IsNegativeValue(text),
        CssPropertyId.ScrollbarGutter => IsScrollbarGutter,
        // CSS Text 3 §3.5: one keyword per axis, and a string may stand in for the ellipsis. The
        // reference engine takes 'clip ellipsis' and 'text-overflow: "…"' and refuses
        // 'text-overflow: bogus' (measured, and 'text-overflow: bogus bogus' goes for the same reason
        // as 'margin' with five values below).
        CssPropertyId.TextOverflow
            => static text => IsPairOf(text, IsTextOverflowKeyword),
        // CSS Backgrounds 3 §4.2 and CSS Borders: a width, a style and a colour in any order. The
        // width is itself a one-to-four list — 'border: 2px 8px 12px 4px solid #36c' sets the four
        // sides at once — while the style and the colour each appear at most once.
        // 'border: 1px bogus red' names no style and is no declaration (measured), and
        // 'border: 1px solid red' and 'border: solid' both are.
        CssPropertyId.Border or CssPropertyId.BorderTop or CssPropertyId.BorderRight
            or CssPropertyId.BorderBottom or CssPropertyId.BorderLeft
            => static text => IsBorderSideValue(text, maxWidth: 4),
        // CSS Logical 1 §4: the logical border shorthands measure two edges, and the shorthand of
        // the axis says ONE width, ONE style and ONE colour for both of them — a second value of
        // any one part is the longhand's own shape, not the shorthand's
        // (measured: 'border-block: 1px 2px', '… : solid solid' and '… : red blue' each fall while
        // 'border-block-width: 1px 2px' stands as the pair it names).
        CssPropertyId.BorderBlockStart or CssPropertyId.BorderBlockEnd
            or CssPropertyId.BorderInlineStart or CssPropertyId.BorderInlineEnd
            or CssPropertyId.BorderBlock or CssPropertyId.BorderInline
            => static text => IsBorderSideValue(text, maxWidth: 1),
        // 'outline' is <'outline-color'> || <'outline-style'> || <'outline-width'> (CSS UI 4 §5.1)
        // — one of each, and the width is a single value rather than the four-side list a border
        // side shorthand takes. Measured: 'outline: 2px dashed rgb(1, 2, 3)' and 'outline: red'
        // both stand, while 'outline: 3px 3px 0 #e33' and 'outline: 2px red dashed extra' fall.
        // CSS Overflow Anchoring 1 §3: exactly 'auto' or 'none'.
        CssPropertyId.OverflowAnchor => static text => IsSingleKeywordWord(text,
            new[] { "auto", "none" }),
        CssPropertyId.Outline => IsOutlineValue,
        // CSS Generated Content 3 §5.1: 'normal', 'none', or a content list with an optional
        // '/' alt string. A bare ident is no content item, so 'content: bogus' is refused.
        CssPropertyId.Content => IsContentValue,
        // CSS Masking 1 §5.2 / CSS Shapes 1 §4: 'none', a clip-source url(), or a basic shape with
        // an optional geometry box on either side of it. A shape that received a token it cannot
        // use is no value at all (measured: 'circle(50%, bogus)' reads back empty). Anything this
        // reader cannot classify is accepted, so a shape that works today is never lost here.
        CssPropertyId.ClipPath => static text => IsClipPathValue(text),
        // CSS Grid 1 §7.14/§8.3: a <grid-line> is 'auto', 'none', a line name, a line number, or
        // 'span' with its number. A bare 'span' is no line — the reference engine drops it (measured:
        // 'grid-area: span' reads back empty and computes 'auto'), and two words with no slash
        // between them are no line either ('grid-area: main head' is dropped the same way).
        CssPropertyId.GridArea => static text => IsGridLineValue(text, 4),
        CssPropertyId.GridRow or CssPropertyId.GridColumn
            => static text => IsGridLineValue(text, 2),
        CssPropertyId.GridRowStart or CssPropertyId.GridRowEnd
            or CssPropertyId.GridColumnStart or CssPropertyId.GridColumnEnd
            => static text => IsGridLineValue(text, 1),
        // CSS Grid 1 §7.4: at most one axis and at most one 'dense'. Two axes ('row column') or a
        // repeated word are no value at all (measured: 'row row' reads back empty).
        CssPropertyId.GridAutoFlow => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count is < 1 or > 2) return false;
            bool axis = false, dense = false;
            foreach (var w in words)
            {
                var p = w.ToLowerInvariant();
                if (p is "row" or "column") { if (axis) return false; axis = true; }
                else if (p == "dense") { if (dense) return false; dense = true; }
                else return false;
            }
            return true;
        },
        // CSS Text 3 §5.1.11: 'middle' is not a value of 'text-align-last' — the reference engine
        // refuses it, so the declaration never exists (measured).
        CssPropertyId.TextAlignLast => static text => IsSingleKeywordWord(text,
            new[] { "auto", "start", "end", "left", "right", "center", "justify" }),
        // CSS UI 4 §3.3: the outline's styles are the border's without 'hidden' — which is a word
        // the ring has no meaning for — and with 'auto', which is the one the border lacks
        // (measured: 'outline-style: hidden' falls and 'auto' stands).
        CssPropertyId.OutlineStyle => static text => IsSingleKeywordWord(text,
            new[] { "auto", "none", "dotted", "dashed", "solid", "double", "groove",
                "ridge", "inset", "outset" }),
        // CSS Text 3 §5.1.9: two or three lengths and an optional colour, and no 'inset' — that
        // belongs to 'box-shadow' only (measured: 'text-shadow: inset 0 0 2px red' is dropped).
        CssPropertyId.TextShadow => IsTextShadowValue,
        // CSS Backgrounds 3 §5.6: two to four lengths, an optional colour and an optional 'inset',
        // in any order (measured: 'box-shadow: inset 0 0 2px red' and 'box-shadow: 0 0 2px #F00'
        // both stand, 'box-shadow: 2px dashed rgb(1, 2, 3)' does not).
        CssPropertyId.BoxShadow => IsBoxShadowValue,
        // CSS Text Decoration 4 §2.0.1: the three keywords of the set, and nothing else. 'space',
        // 'leading' and 'trailing' are the values of the sibling property's tailoring grammar and
        // are refused here (all four measured).
        CssPropertyId.TextDecorationSkipInk
            => static text => IsOneOf(text, "auto", "none", "all"),
        // The keyword family the engine declares without acting on. Each arm is the same matcher
        // the applier canonicalises with, so a value that would be stored is a value the grammar
        // accepts and the two cannot disagree about what a page wrote.
        CssPropertyId.TouchAction
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.CanonicalTouchAction(text) != null,
        CssPropertyId.PaintOrder
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.CanonicalPaintOrder(text) != null,
        CssPropertyId.VectorEffect
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.IsOneOfLower(
                text, "none", "non-scaling-stroke"),
        // CSS Images 3 §5.1: the two words, and no angle. Measured: '0deg' and '90deg' are both
        // dropped (the rotation the draft allowed is not what this engine answers), and 'flip'
        // alone or beside another word is dropped with them.
        CssPropertyId.ImageOrientation
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.IsOneOfLower(
                text, "from-image", "none"),
        CssPropertyId.TextSecurity
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.IsOneOfLower(
                text, "none", "disc", "circle", "square"),
        // The clamp takes one integer or 'none'; the two-value form a draft proposed is no value
        // here either (measured: '-webkit-line-clamp: 2 1.5em' reads back 'none').
        CssPropertyId.LineClamp or CssPropertyId.WebkitLineClamp
            => static text => text.Equals("none", StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(text.Trim(), out var lines) && lines >= 1),
        CssPropertyId.InterpolateSize
            => static text => IsOneOf(text, "numeric-only")
                || IsOneOf(text, "allow-keywords")
                || (text.Equals("numeric-only allow-keywords", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("allow-keywords numeric-only", StringComparison.OrdinalIgnoreCase)),
        // CSS Fonts 4 §9.3: the shorthand is 'none' or a set of the three synthesis kinds, each at
        // most once. 'auto' and 'position' are later drafts this reference engine does not take, so
        // they must be refused here — the CSSOM echo drops the declaration, not just the layout.
        CssPropertyId.FontSynthesis => IsFontSynthesisValue,
        // CSS Multi-column 1 §3.3: '<column-width> || <column-count>'. A bare positive integer is a
        // count (zero is not); a length or percentage (and a lone '0', which is a length) is a width;
        // 'auto' is either. A second of either, or a count below one, drops the whole declaration.
        CssPropertyId.Columns => IsColumnsValue,
        CssPropertyId.ColumnCount => IsColumnCountValue,
        // CSS UI 4 §4.3: 'caret-shape' is exactly one keyword, and this reference engine takes only
        // 'auto', 'bar' and 'block' — 'underline' and 'none' are refused (measured via CSS.supports).
        CssPropertyId.CaretShape => static text => IsOneOf(text, "auto", "bar", "block"),
        // CSS Text Decoration 4 §3.2: a vertical choice and an optional horizontal one. The parse
        // that stores it is also the grammar — 'alphabetic', 'auto left', a repeated kind or an
        // unknown word all drop the declaration (measured against the reference engine).
        CssPropertyId.TextUnderlinePosition
            => static text => CssPropertyApplier.TryParseTextUnderlinePosition(text, out _),
        // CSS Fragmentation 3 §3.2–3.4 with the CSS 2.1 'page-break-*' aliases: one keyword of each
        // family's own measured set. The modern before/after set is the column/page/recto/verso family;
        // 'region'/'avoid-region' are refused on both, and the legacy spelling keeps only 'always'
        // where the modern set has 'page' (measured value by value).
        CssPropertyId.BreakBefore or CssPropertyId.BreakAfter => static text => IsSingleKeywordWord(text,
            new[] { "auto", "avoid", "avoid-page", "page", "left", "right", "recto", "verso",
                "avoid-column", "column" }),
        CssPropertyId.BreakInside => static text => IsSingleKeywordWord(text,
            new[] { "auto", "avoid", "avoid-page", "avoid-column" }),
        CssPropertyId.PageBreakBefore or CssPropertyId.PageBreakAfter or CssPropertyId.PageBreakInside
            => text => IsSingleKeywordWord(text, LegacyBreakAlias.ValuesOf(id)!),
        // ===== The keyword families measured value by value in b290 (251 cases,
        // snapshots/_probe_b290_unswept_keyword_sets.html). None of these properties had a grammar
        // arm at all before, so each of them took whatever word a page wrote.
        // CSS UI 4 §3.6 + the form-control spellings this engine answers: one word, and the set is
        // smaller than the draft's — 'slider', 'popover', 'toolbars', 'document', 'alpha' and
        // 'pickers' are all dropped, and a second word is never a value (measured, each).
        CssPropertyId.Appearance => static text => IsOneWordOnly(text,
            "auto", "none", "textfield", "menulist", "listbox", "textarea", "button", "checkbox",
            "radio", "menulist-button", "searchfield", "meter", "progress-bar"),
        // CSS Color 5 §…: 'zoom' is a scaling factor — a number from zero up, a percentage, or
        // 'normal'. 'reset' is a later draft's word, a length measures nothing to scale by, a
        // negative is no factor, and a calc() that mixes a percentage with a length is not a
        // number (measured: 'calc(1 + 1)' stands, 'calc(50% + 10px)' falls).
        CssPropertyId.Zoom => static text =>
        {
            var word = text.Trim();
            if (word.Equals("normal", StringComparison.OrdinalIgnoreCase)) return true;
            if (IsMathFunctionCall(word, IsLengthOrPercentage))
                return CssMathValue.TypeOf(word) == "number";
            if (word.EndsWith("%", StringComparison.Ordinal))
                return IsNumber(word[..^1]);
            return Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(word, out var factor,
                out var unit) && unit.Length == 0 && factor >= 0;
        },
        // CSS Lists 3 §4.1 and GCPM §8.1: quote pairs, so an odd number of strings says one and a
        // half a pair and is no value (measured: '"a"' and '"a" "b" "c"' both fall, two and four
        // stand), and 'none'/'auto' stand alone.
        CssPropertyId.Quotes => static text =>
        {
            var word = text.Trim();
            if (word.Equals("none", StringComparison.OrdinalIgnoreCase)
                || word.Equals("auto", StringComparison.OrdinalIgnoreCase)) return true;
            var items = SplitComponents(word);
            return items.Count >= 2 && items.Count % 2 == 0
                && items.All(item => item.Length > 1 && (item[0] == '"' || item[0] == '\''));
        },
        // CSS Text 3 §5.1: one final-line choice. 'justify-all' and 'match-parent' are the values
        // of a different draft, and the two-word form CSS Text 4 sketches is not answered here
        // either (measured: all four fall, 'center' and 'justify' stand). The four legacy '-webkit-'
        // spellings do stand — and '-webkit-justify' is the one of them that does not (measured).
        CssPropertyId.TextAlign => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count != 1) return false;
            return IsOneOf(words[0], "left", "right", "center", "justify", "start", "end",
                "-webkit-left", "-webkit-right", "-webkit-center", "-webkit-match-parent");
        },
        // CSS Text 4 §3.6 (the set this engine answers): 'trim-both' and 'trim-none' are dropped
        // (measured), and so is the second word of the draft's two-word form.
        CssPropertyId.TextSpacingTrim => static text => IsOneWordOnly(text,
            "normal", "none", "space-all", "space-first", "trim-start"),
        // CSSOM View §7.1: 'instant' is not a value of this property here (measured).
        CssPropertyId.ScrollBehavior => static text => IsOneWordOnly(text, "auto", "smooth"),
        // CSS Multi-column 1 §3.4: one keyword. The '||' with 'all'/'columns' of the same draft is
        // not answered (measured: 'balance all', 'balance columns' and 'auto all' all fall).
        CssPropertyId.ColumnFill => static text => IsOneWordOnly(text, "auto", "balance"),
        // CSS Fragmentation 3 §3.5/§3.6: an integer from one up, and no 'auto' in this engine
        // (measured: 'widows: auto', 'widows: 0' and 'widows: 1.5' all fall).
        CssPropertyId.Widows or CssPropertyId.Orphans => static text =>
            IsIntegerText(text) && int.TryParse(text.Trim(), out var count) && count >= 1,
        // ===== The property families measured value by value in b291 (268 cases,
        // snapshots/_probe_b291_unswept_families.html). Not one of these had a grammar arm before,
        // so each of them took whatever word a page wrote.
        // CSS Images 3 §5.1: one keyword of the five.
        CssPropertyId.ObjectFit => static text => IsOneWordOnly(text,
            "fill", "contain", "cover", "none", "scale-down"),
        // CSS Images 3 §5.3: three words. 'smooth', 'high-quality' and 'low-quality' are the
        // Compositing draft's vocabulary and the reference engine drops all three (measured).
        CssPropertyId.ImageRendering => static text => IsOneWordOnly(text,
            "auto", "crisp-edges", "pixelated"),
        // CSS Blend 1 §3: the sixteen, one at a time — 'plus-darker' belongs to the Compositing
        // draft and is dropped here (measured).
        CssPropertyId.MixBlendMode => static text => IsOneWordOnly(text, BlendModeWords),
        // The background's is a COMMA list, one blend per layer; the same words separated by a space
        // are no value (measured: 'multiply, screen' stands, 'multiply screen' falls).
        CssPropertyId.BackgroundBlendMode => static text =>
        {
            var layers = SplitTopLevel(text, ',');
            return layers.Count > 0 && layers.All(layer =>
            {
                var words = SplitComponents(layer);
                return words.Count == 1 && IsOneOf(words[0], BlendModeWords);
            });
        },
        // CSS Text 3 §5.3: the four. 'full-width' and 'full-size-kana' are §5.4's feature, which the
        // reference engine does not answer here (measured: both dropped).
        CssPropertyId.TextTransform => static text => IsOneWordOnly(text,
            "none", "capitalize", "uppercase", "lowercase"),
        // CSS Ruby 1 §4.1: 'over' and 'under' only. The vertical writing modes' 'left'/'right', the
        // draft's 'alternate' and the two-word form are each dropped (measured).
        CssPropertyId.RubyPosition => static text => IsOneWordOnly(text, "over", "under"),
        // CSS Scrollbars 1 §3.2: one keyword.
        CssPropertyId.ScrollbarWidth => static text => IsOneWordOnly(text, "auto", "thin", "none"),
        // CSS Adjust 1 §4.2: one keyword.
        CssPropertyId.ForcedColorAdjust => static text => IsOneWordOnly(text, "auto", "none"),
        // CSS Tables 1 §4.2: 'top' and 'bottom'. The side captions CSS 2.1 §11.2.2 spelled
        // 'left'/'right' — and the logical pair a later draft added — are dropped (measured).
        CssPropertyId.CaptionSide => static text => IsOneWordOnly(text, "top", "bottom"),
        CssPropertyId.TableLayout => static text => IsOneWordOnly(text, "auto", "fixed"),
        CssPropertyId.BorderCollapse => static text => IsOneWordOnly(text, "collapse", "separate"),
        // CSS Transforms 2 §3: the two spellings, one at a time.
        CssPropertyId.TransformStyle => static text => IsOneWordOnly(text, "flat", "preserve-3d"),
        CssPropertyId.BackfaceVisibility => static text => IsOneWordOnly(text, "visible", "hidden"),
        // CSS Display 3 §4.1: the three.
        CssPropertyId.Visibility => static text => IsOneWordOnly(text, "visible", "hidden", "collapse"),
        // CSS Transforms 2 §3: 'none' or a non-negative &lt;length&gt; that says what it is measured
        // in — 'perspective: 10' is a number and no length, and '-10px' measures backwards, so both
        // fall while '0', '0px' and '1e2px' stand (measured, each).
        CssPropertyId.Perspective => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count != 1) return false;
            if (words[0].Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            return IsNonNegativeDimensionedLength(words[0]);
        },
        // CSS Tables 1 §4.11: one or two non-negative lengths. A percentage measures nothing against
        // a table (measured: 'border-spacing: 50%' falls), three values fall, and a negative with them.
        CssPropertyId.BorderSpacing => static text =>
        {
            var words = SplitComponents(text);
            return words.Count is 1 or 2 && words.All(IsNonNegativeDimensionedLength);
        },
        // CSS Flexbox 1 §4.5: an integer. 'order: 1.5' and 'order: 1e2' are each dropped (measured),
        // and 'order: calc(1 + 1)' is the one math spelling an integer property does take.
        CssPropertyId.Order => static text => IsIntegerList(text),
        // CSS Grid 1 §7.2/§7.3: a track list of breadths, and none of the 'repeat()' notation the
        // explicit templates take (measured: 'grid-auto-rows: repeat(2, 10px)' falls while
        // 'minmax(auto, 1fr)', 'fit-content(10px)' and '1fr 2fr' stand).
        CssPropertyId.GridAutoRows or CssPropertyId.GridAutoColumns => static text =>
            !SaysSubgridOrRepeat(text) && IsTrackList(text),
        // CSS Text Decoration 4 §4.2: a length-percentage or one of the two keywords. A bare number
        // is no thickness (measured: 'text-decoration-thickness: 1' falls, '0' and '10%' stand).
        CssPropertyId.TextDecorationThickness => static text =>
            IsOneOf(text.Trim(), "auto", "from-font")
            || (SplitComponents(text).Count == 1
                && IsDimensionedLengthPercentageOrZero(text.Trim())),
        // §4.4: the same shape with 'auto' as its only keyword.
        CssPropertyId.TextUnderlineOffset => static text =>
            IsOneWordOnly(text, "auto")
            || (SplitComponents(text).Count == 1
                && IsDimensionedLengthPercentageOrZero(text.Trim())),
        // CSS Inline 3 §3.1: one keyword, and 'none' is not one of them (measured).
        CssPropertyId.BaselineSource => static text => IsOneWordOnly(text, "auto", "first", "last"),
        // CSS Transforms 2 §3: 'perspective-origin' is one &lt;position&gt; with no depth — the four
        // component spelling of CSS Position 3 §5.2 stands and a third length does not (measured:
        // 'left 10px top 20px' is a value, 'top left 10px' and '10% 20% 30%' are each dropped).
        CssPropertyId.PerspectiveOrigin => static text =>
            ShorthandExpander.IsBackgroundPositionValue(text) && SplitComponents(text).Count != 3,
        // 'transform-origin' is the mirror image: one length of depth is allowed and the four
        // component spelling is not (measured: 'left top 10px', '10px 20px 30px' and
        // 'center 10px 20px' each stand, 'right 10px bottom 20px' and 'left 10px center 20px' are
        // each dropped, leaving the initial '50% 50%'). The third component is the depth, which a
        // percentage cannot measure.
        CssPropertyId.TransformOrigin => static text =>
        {
            var parts = SplitComponents(text);
            if (parts.Count == 3)
                return ShorthandExpander.IsBackgroundPositionValue(parts[0] + " " + parts[1])
                    && IsDimensionedLength(parts[2]);
            return ShorthandExpander.IsBackgroundPositionValue(text) && parts.Count != 4;
        },
        // §3.4: the three keywords or one non-negative length. 'auto' names the style and not the
        // width, a bare number is no length, and a percentage is no width (all measured).
        CssPropertyId.OutlineWidth => static text =>
            IsOneWordOnly(text, "thin", "medium", "thick")
            || (SplitComponents(text).Count == 1 && IsNonNegativeDimensionedLength(text.Trim())),
        // §3.5: one length, the negative included — the ring moves inward as well as out. '10%', '1'
        // and 'auto' are each dropped (measured).
        CssPropertyId.OutlineOffset => static text =>
            SplitComponents(text).Count == 1 && IsDimensionedLengthOrZero(text.Trim()),
        // CSS 2.1 §10.8.1: the eight keywords, or a length-percentage. 'center' and 'auto' belong to
        // no set here, and a bare number is not a length (all measured).
        CssPropertyId.VerticalAlign => static text =>
            IsOneWordOnly(text, "baseline", "sub", "super", "top", "bottom", "middle",
                "text-top", "text-bottom")
            || (SplitComponents(text).Count == 1
                && IsDimensionedLengthPercentageOrZero(text.Trim())),
        // CSS Will Change 1 §2: 'auto' alone, or a comma-separated list of feature names. 'none'
        // says nothing will change and 'all' says everything, and neither is in the grammar —
        // CSS Will Change gives no keyword for the absence or the totality of a list of names
        // (both measured, and 'auto' with company falls with them); any other ident — 'bogus'
        // included — stands, because the property names a feature this engine may not know.
        CssPropertyId.WillChange => static text =>
        {
            var items = SplitTopLevel(text);
            if (items.Count == 0) return false;
            for (int i = 0; i < items.Count; i++)
            {
                var word = items[i].Trim();
                if (word.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    // 'auto' is the whole value or nothing in it (CSS Will Change 1 §2).
                    if (items.Count != 1) return false;
                    continue;
                }
                if (word.Equals("all", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
                if (!IsIdentifierLike(word) || char.IsAsciiDigit(word[0])) return false;
            }
            return true;
        },
        // CSS Text 3 §6.3 and §5.1: one keyword each. The two-word shapes are no value —
        // 'line-break: anywhere strict' and 'empty-cells: show hide' are each dropped (measured).
        CssPropertyId.LineBreak => static text =>
            IsOneWordOnly(text, "auto", "loose", "normal", "strict", "anywhere"),
        // The legacy spelling has a word of its own and loses one of the modern set (measured:
        // '-webkit-line-break: after-white-space' stands while 'anywhere' falls, and the modern
        // name refuses 'after-white-space' for its part).

        CssPropertyId.EmptyCells => static text => IsOneWordOnly(text, "show", "hide"),
        // CSS UI 4 §4: the two boxes, and 'padding-box' is the word no grammar here has (measured).
        CssPropertyId.BoxSizing => static text =>
            IsOneWordOnly(text, "content-box", "border-box"),
        // CSS Mobile Text Size Adjustment 1 §2: 'auto', 'none', or a percentage. A bare number is not
        // a percentage and 'fit-function' is no keyword (both measured).
        CssPropertyId.TextSizeAdjust => static text =>
            IsOneWordOnly(text, "auto", "none")
            || (SplitComponents(text).Count == 1 && !IsNegativeValue(text)
                && (IsMathFunctionCall(text.Trim(), IsLengthOrPercentage)
                    || (Acrux.Core.Dom.Animations.CssValueTokenizer
                            .TrySplitUnit(text.Trim(), out _, out var unit)
                        && unit == "%"))),
        // CSS Line Grid 1 §4.1: a positive ascent and an optional positive descent, or 'normal'.
        // 'auto' belongs to the alignment property and not here, and zero lines is no size (all
        // measured).
        CssPropertyId.InitialLetter => static text =>
        {
            var parts = SplitComponents(text);
            if (parts.Count is < 1 or > 2) return false;
            if (parts.Count == 1
                && parts[0].Equals("normal", StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; i < parts.Count; i++)
            {
                if (!IsNumber(parts[i]) || !TryPlainNumber(parts[i], out var value) || value <= 0)
                    return false;
                // The second number is how many lines the initial spans, and a half line is no
                // count (measured: '2.5 1' stands while '1.5 2.5' falls).
                if (i == 1 && !IsIntegerText(parts[i])) return false;
            }
            return true;
        },
        // §4.2: one alignment word, or the pair the grammar allows (measured: 'auto alphabetic'
        // stands and 'bogus' falls).
        CssPropertyId.InitialLetterAlign => static text =>
        {
            var parts = SplitComponents(text);
            if (parts.Count is < 1 or > 2) return false;
            foreach (var part in parts)
                if (!IsOneOf(part, "auto", "alphabetic", "hanging", "text-top")) return false;
            return true;
        },
        // CSS Ruby 1 §3.2: one keyword. The two-word shape the sketch grammar leaves room for is no
        // value (measured: 'ruby-merge: auto collapse' falls).
        CssPropertyId.RubyMerge => static text =>
            IsOneWordOnly(text, "separate", "collapse", "auto", "sequence"),
        // CSS Animations 1 §3.5–§3.9: each longhand is a comma-separated list of its own keyword
        // set, and a space between two of the words is no separator (measured:
        // 'animation-fill-mode: forwards backwards' falls while 'forwards, backwards' stands).
        CssPropertyId.AnimationFillMode => static text =>
            IsKeywordList(text, "none", "forwards", "backwards", "both"),
        CssPropertyId.AnimationDirection => static text =>
            IsKeywordList(text, "normal", "reverse", "alternate", "alternate-reverse"),
        CssPropertyId.AnimationPlayState => static text => IsKeywordList(text, "running", "paused"),
        // §3.5: 'none' or a custom ident per item — a bare number names no animation (measured),
        // while a word the engine has never heard of stands.
        CssPropertyId.AnimationName => static text =>
        {
            var items = SplitTopLevel(text);
            if (items.Count == 0) return false;
            foreach (var item in items)
            {
                var word = item.Trim();
                if (word.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                // A quoted string is the other way to say an animation's name, and the engine
                // prints it without its quotes (measured: 'animation-name: "quoted"' reads back
                // 'quoted').
                if (word.Length >= 2 && (word[0] == '"' || word[0] == '\'') && word[^1] == word[0])
                    continue;
                if (!IsIdentifierLike(word) || char.IsAsciiDigit(word[0])) return false;
            }
            return true;
        },
        // CSS Animations 1 §3: the shorthand's parts are the longhands' own values, so a word that
        // reads as a timing function has to be one — and a 'cubic-bezier()' whose x leaves [0,1] is
        // no easing at all, which takes the whole declaration with it
        // (measured: 'animation: cubic-bezier(0.1, 2, 3, 4)' leaves the property at its initial).
        CssPropertyId.Animation => IsAnimationShorthandValue,
        CssPropertyId.AnimationTimingFunction => static text => IsTimingFunctionList(text),
        CssPropertyId.TransitionTimingFunction => static text => IsTimingFunctionList(text),
        // CSS Transitions 1 §4: one of the two behaviour words per item.
        CssPropertyId.TransitionBehavior => static text =>
            IsKeywordList(text, "normal", "allow-discrete"),
        // SVG 2 §11.7/the rendering hints: one keyword from a closed set, and a word outside it is
        // no hint (measured: 'text-rendering: bogus' falls while 'optimizeLegibility' stands).
        CssPropertyId.TextRendering => static text =>
            IsOneWordOnly(text, "auto", "optimizeSpeed", "optimizeLegibility",
                "geometricPrecision"),
        CssPropertyId.ColorRendering => static text =>
            IsOneWordOnly(text, "auto", "optimizeSpeed", "optimizeQuality"),
        CssPropertyId.ColorInterpolation => static text =>
            IsOneWordOnly(text, "auto", "sRGB", "linearRGB"),
        CssPropertyId.ColorInterpolationFilters => static text =>
            IsOneWordOnly(text, "auto", "sRGB", "linearRGB"),
        CssPropertyId.ShapeRendering => static text =>
            IsOneWordOnly(text, "auto", "optimizeSpeed", "crispEdges", "geometricPrecision",
                "non-scaling-stroke", "non-scaling-size", "non-rotation", "fixed-position"),
        // CSS Writing Modes 4 §5.2 and CSS Bidi: the six modes, 'isolate-override' among them
        // (measured: it stands where 'bogus' falls).
        CssPropertyId.UnicodeBidi => static text =>
            IsOneWordOnly(text, "normal", "embed", "isolate", "bidi-override",
                "isolate-override", "plaintext"),
        // CSS Text 3 §4.5: 'auto' or one string, and a bare dash is neither (measured:
        // 'hyphenate-character: -' falls, '"--"' stands, and 'none' is not a word it takes).
        CssPropertyId.HyphenateCharacter => static text =>
        {
            var trimmed = text.Trim();
            if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)) return true;
            return IsContentString(trimmed);
        },
        // CSS Markers 1 §3: the shorthand is one marker reference or 'none' — the three-longhand
        // spelling the older draft sketched is no value (measured: two and four references fall).
        CssPropertyId.Marker => static text =>
        {
            var parts = SplitComponents(text);
            if (parts.Count != 1) return false;
            return Resolver.EchoedProperties.NormalizeMarker(parts[0]) != null;
        },
        // ============================================================ b294: the word sets, pair
        // shapes and list separators no earlier round priced. Every accept and every refusal in
        // the block below is the reference engine's own answer, read value by value from
        // snapshots/out/_b294_ref_probeE.txt, _b294_ref_probeF.txt and _b294_ref_probeG.txt.

        // CSS Position 3 §4.2 with CSS 2.1 §9.3: the five positions, and no legacy spelling of one
        // of them — '-webkit-sticky' is a name the prefixed era gave the keyword and the reference
        // engine reads no value at all from it (measured; 'STICKY' still reads back 'sticky').
        CssPropertyId.Position => static text =>
            IsOneWordOnly(text, "static", "relative", "absolute", "fixed", "sticky"),
        // CSS Writing Modes 3 §4.1: two words. 'auto' is the value an older draft gave the
        // property and a second direction beside the first is no value (both measured).
        CssPropertyId.Direction => static text => IsOneWordOnly(text, "ltr", "rtl"),
        // CSS Overflow 3 §3.3: one behaviour per axis, two for the shorthand, and 'sticky' — the
        // word a draft added to the set — is in no engine's table (measured: 'sticky' and
        // 'sticky auto' each fall while 'overlay' stands and computes 'auto').
        CssPropertyId.OverflowX or CssPropertyId.OverflowY => static text =>
            IsOneWordOnly(text, "visible", "hidden", "clip", "scroll", "auto", "overlay"),
        CssPropertyId.Overflow => static text =>
            IsOneOrTwoKeywords(text, "visible", "hidden", "clip", "scroll", "auto", "overlay"),
        // CSS Flexbox 1 §4.1/§4.3: each longhand is one word of its own set, and the names from
        // the first edition of the chapter ('flex-row', 'reverse') are not values (measured).
        CssPropertyId.FlexDirection => static text =>
            IsOneWordOnly(text, "row", "row-reverse", "column", "column-reverse"),
        CssPropertyId.FlexWrap => static text =>
            IsOneWordOnly(text, "nowrap", "wrap", "wrap-reverse"),
        // CSS Transforms 1 §3 and Transforms 2 §6: the five reference boxes. 'auto' is the value
        // the second draft added and no engine measured here takes it, and the two box words the
        // padding and margin edges own are not among the five (all measured).
        CssPropertyId.TransformBox => static text =>
            IsOneWordOnly(text, "content-box", "border-box", "fill-box", "stroke-box", "view-box"),
        // SVG 2 §12.6: the painted-region set, one word, in its camel-case spelling lower-cased on
        // the way in. The two '-box' spellings belong to other properties and the two-word SVG
        // form is not read (all measured: 'fill-box', 'stroke-box' and 'visible painted' fall).
        CssPropertyId.PointerEvents => static text =>
            IsOneWordOnly(text, "auto", "none", "visiblePainted", "visibleFill", "visibleStroke",
                "visible", "painted", "fill", "stroke", "all", "bounding-box"),
        // CSS MathML Core §5.3: 'math-style' takes its two words; 'math-shift' is the property an
        // engine answers only 'normal' for — 'superscript' and 'subscript' are its spec words and
        // are refused here exactly as they are there (measured, every one).
        CssPropertyId.MathStyle => static text => IsOneWordOnly(text, "normal", "compact"),
        CssPropertyId.MathShift => static text => IsOneWordOnly(text, "normal"),
        // CSS Multi-column 1 §7.2: 'none' or 'all'. 'auto' is the word the first edition carried
        // and a number is not a span (both measured).
        CssPropertyId.ColumnSpan => static text => IsOneWordOnly(text, "none", "all"),
        // CSS Form Control 1 §4.1: the two boxes a control's inline size may follow, one at a
        // time. 'normal' and 'auto' are the older draft's words and a pair is no value (measured;
        // the property computes 'fixed', which is what 'fixed' — not 'normal' — is its initial).
        CssPropertyId.FieldSizing => static text => IsOneWordOnly(text, "fixed", "content"),
        // CSS Text 4 §3.4.1, §4.6: the word sets the engines measured here answer. 'discard' and
        // 'preserve-spaces' are in the current draft of the collapsing grammar and in no engine's
        // table, 'normal' and 'auto' belong to the 'white-space' shorthand, and a second word is
        // never a value (all measured: each dropped, with the property left where it was).
        CssPropertyId.WhiteSpaceCollapse => static text =>
            IsOneWordOnly(text, "collapse", "preserve", "preserve-breaks", "break-spaces"),
        CssPropertyId.TextWrapMode => static text => IsOneWordOnly(text, "wrap", "nowrap"),
        CssPropertyId.TextWrapStyle => static text =>
            IsOneWordOnly(text, "auto", "balance", "stable", "pretty"),
        // CSS Text Decoration 4 §3.2: one line style, never two — the pair belongs to the
        // shorthand's decoration list, not to this property (measured: 'dotted wavy' and
        // 'solid solid' are each dropped, and 'hidden' is a border word).
        CssPropertyId.TextDecorationStyle => static text =>
            IsOneWordOnly(text, "solid", "double", "dotted", "dashed", "wavy"),
        // CSS Text Decoration 4 §3.1: the line list, where 'none' says the whole list on its own,
        // a word appears at most once, and the two error words and 'blink' — each of which already
        // says a whole decoration — appear alone (measured: 'none underline' and
        // 'underline underline' fall, 'underline overline' stands).
        CssPropertyId.TextDecorationLine => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count == 0) return false;
            if (words.All(word => IsOneOf(word, "none", "blink", "spelling-error", "grammar-error")))
                return words.Count == 1;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in words)
            {
                if (!IsOneOf(word, "underline", "overline", "line-through")) return false;
                if (!seen.Add(word.ToLowerInvariant())) return false;
            }
            return true;
        },
        // CSS Fonts 4 §4.2: 'normal', 'bold' and the two relative words, or a weight — a number
        // from 1 to 1000, and a fraction of a weight is still a weight. A zero, a negative and a
        // number past 1000 are each refused rather than clamped (measured: '0', '0.5', '-1' and
        // '1001' all leave the property at 400, while '1.5', '250' and '999' are weights the
        // computed value keeps exactly as written), and '+500' is the same weight spelled longer.
        CssPropertyId.FontWeight => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count != 1) return false;
            var word = words[0];
            if (IsOneOf(word, "normal", "bold", "bolder", "lighter")) return true;
            if (IsMathFunctionCall(word, IsNumber)) return true;
            if (!TryPlainNumber(word, out var weight)) return false;
            return weight >= 1 && weight <= 1000;
        },
        // CSS Fonts 4 §6.3: the eight absolute widths and a percentage. The two relative keywords
        // share the section and are in no engine's table (measured: 'wider' and 'narrower' fall),
        // a math function is refused because a stretch is not added to, and 'bold' is nobody's
        // width.
        CssPropertyId.FontStretch => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count != 1) return false;
            if (IsOneOf(words[0], "normal", "ultra-condensed", "extra-condensed", "condensed",
                    "semi-condensed", "semi-expanded", "expanded", "extra-expanded",
                    "ultra-expanded")) return true;
            // 'calc(100% + 1px)' is a well-formed percentage sum and still no stretch: the property
            // is a width, and a width is not computed from a length (measured).
            return IsPercentage(words[0]);
        },
        // CSS Fonts 4 §5.3: normal | italic | oblique, optionally with the angle of the slant.
        // The angle is at most a right one, whichever way it leans (measured: 'oblique 90deg' and
        // 'oblique -20deg' stand, 'oblique 91deg' and 'oblique 100deg' fall), 'left' and 'right'
        // are the vertical-text words of the current draft and are not read, and the italic that
        // follows an angle is a second style word.
        CssPropertyId.FontStyle => IsFontStyleValue,
        // CSS Fonts 4 §8.7: the variant words of the shorthand, at most one from each of its
        // families. The list this engine reads is the one the reference engine answers for, word
        // by word; 'normal' and 'none' say the whole list, and a word says a family it belongs to
        // exactly once.
        CssPropertyId.FontVariant => IsFontVariantValue,
        // CSS Fonts 4 §8.12: the synthesis longhands are two words each.
        CssPropertyId.FontSynthesisSmallCaps or CssPropertyId.FontSynthesisStyle
            or CssPropertyId.FontSynthesisWeight
            => static text => IsOneWordOnly(text, "auto", "none"),
        // CSS Text 3 §5.5: the legacy spelling of 'overflow-wrap', with the same set and the same
        // refusal of 'break-all' (measured: 'break-all' and 'auto' each fall).
        CssPropertyId.WordWrap or CssPropertyId.OverflowWrap => static text =>
            IsOneWordOnly(text, "normal", "break-word", "anywhere"),
        // CSS Animations 1 §3: each longhand is a comma-separated list — one item per animation —
        // and a space inside one item is no separator at all (measured: 'animation-duration: 1s 2s'
        // and 'animation-iteration-count: 1 2' are each dropped while '1s, 2s' and '1, 2' stand).
        CssPropertyId.AnimationIterationCount => static text =>
        {
            var items = SplitTopLevel(text);
            if (items.Count == 0) return false;
            foreach (var item in items)
            {
                var word = item.Trim();
                if (word.Equals("infinite", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsMathFunctionCall(word, IsNumber)) continue;
                // A count of nothing, and a count of less than nothing, are each refused: the
                // reference engine drops '-1' and keeps '0' (measured).
                if (!TryPlainNumber(word, out var count) || count < 0) return false;
            }
            return true;
        },
        // CSS 2.1 §11.1.2: the legacy 'clip' is 'auto' or <rect()> with exactly four lengths —
        // each one 'auto' or a unit-bearing length, never a percentage and never a bare zero —
        // and the shape functions belong to 'clip-path' (all measured).
        CssPropertyId.Clip => IsLegacyClipValue,
        // CSS Masking 1 §7.1/§8: one box per layer for each of the two properties. 'margin-box'
        // is not one of the six geometry boxes, and two boxes in one layer are the shorthand's
        // shape, not the longhand's (all measured).
        CssPropertyId.MaskClip => static text => IsMaskBoxList(text, allowNoClip: true),
        CssPropertyId.MaskOrigin => static text => IsMaskBoxList(text, allowNoClip: false),
        // CSS Masking 1 §9/§10: the same <bg-size> the background geometry takes, and the same
        // <position> the background takes — which is why 'top 1px' is no mask position either.
        CssPropertyId.MaskSize => ShorthandExpander.IsBackgroundSizeValue,
        CssPropertyId.MaskPosition => IsMaskPositionValue,
        // CSS Motion Path 1 §2/§3: the offset family's position, anchor and distance longhands are
        // echoed from their own table, which is where their value sets are written
        // (Resolver.EchoedProperties) — measured: 'offset-anchor: auto 1px' is no anchor, while
        // 'offset-position: 1px 2px' and 'offset-position: auto' both are.

        // CSS Transitions 1 §2: the property list is a comma list of names, and the two words that
        // say the whole list say it alone (measured: 'all, none' and 'all none' both fall, a quoted
        // name falls, and an unknown name stands because the property may learn it later).
        CssPropertyId.TransitionProperty => static text =>
        {
            var items = SplitTopLevel(text);
            if (items.Count == 0) return false;
            for (int i = 0; i < items.Count; i++)
            {
                var word = items[i].Trim();
                if (IsOneOf(word, "all", "none")) return items.Count == 1;
                // A feature is named, and a digit names nothing however identifier-shaped its
                // characters are (measured: 'transition-property: 1' and 'will-change: 1' fall).
                if (!IsIdentifierLike(word) || char.IsAsciiDigit(word[0])) return false;
            }
            return true;
        },
        // CSS Lists 3 §4.5 and CSS Backgrounds 3 §4.6: the marker and the border's source are one
        // image — not the list of them a layer property takes — from the same set an image is
        // drawn from, and 'cross-fade()' and 'element()' are in no engine's set (measured).
        CssPropertyId.ListStyleImage => static text =>
            !NamesAFontLocal(text) && IsOneImageReference(text),
        CssPropertyId.BorderImageSource => static text =>
            !NamesAFontLocal(text) && IsOneImageReference(text),
        // CSS 2.1 §17.3 and CSS Multi-column 1 §5.2: the style words of a side, one per side, and
        // 'wavy' — which belongs to text decoration — is not one of them.
        // CSS Multi-column 1 §5.1: one <line-width>, and 'auto' is the word of a border's image
        // and of no column rule (measured: 'column-rule-width: auto' and '… : 1px 2px' fall).
        // CSS 2.1 §8.5: four widths, each one a <line-width> — 'auto' is a style word and the
        // reference engine has no width by that name (measured: 'border-width: auto' is dropped).
        CssPropertyId.BorderWidth => static text => IsLineWidthList(text, 4),
        CssPropertyId.ColumnRuleWidth => static text => IsLineWidthList(text, 1),
        // CSS Lists 3 §3.2: two words and no more. 'auto' is the value the marker type takes, not
        // the position's, and a pair is no position (both measured).
        CssPropertyId.ListStylePosition => static text => IsOneWordOnly(text, "inside", "outside"),
        CssPropertyId.ColumnRuleStyle or CssPropertyId.BorderTopStyle
            or CssPropertyId.BorderRightStyle or CssPropertyId.BorderBottomStyle
            or CssPropertyId.BorderLeftStyle or CssPropertyId.BorderInlineStartStyle
            or CssPropertyId.BorderInlineEndStyle or CssPropertyId.BorderBlockStartStyle
            or CssPropertyId.BorderBlockEndStyle
            => static text => IsOneWordOnly(text, "none", "hidden", "dotted", "dashed", "solid",
                "double", "groove", "ridge", "inset", "outset"),
        CssPropertyId.BorderStyle => static text =>
        {
            var words = SplitComponents(text);
            if (words.Count is < 1 or > 4) return false;
            return words.All(word => IsOneOf(word, "none", "hidden", "dotted", "dashed", "solid",
                "double", "groove", "ridge", "inset", "outset"));
        },
        // The two logical border-style pairs take one or two of the same words (measured:
        // 'border-block-style: solid solid' stands and reads back 'solid', while 'solid, solid' —
        // a comma — is no pair and three words are not a pair either).
        CssPropertyId.BorderBlockStyle or CssPropertyId.BorderInlineStyle
            => static text => !text.Contains(',', StringComparison.Ordinal)
                && IsOneOrTwoKeywords(text, "none", "hidden", "dotted", "dashed",
                    "solid", "double", "groove", "ridge", "inset", "outset"),
        // CSS Sizing 4 §5.2 and CSS Grid 1 §7.2/§7.3: the track-list family, split by how many
        // lists the property holds. A single axis takes one track list; 'grid-template' is the two
        // axes with the slash between them, and a bare track list without the slash is no template
        // (measured: 'grid-template: 1px 2px' and 'grid-template: subgrid' fall while
        // 'auto auto / auto auto' and '"a" 1px' stand); 'grid' is the template with the flow and
        // the implicit tracks beside it, and it needs the slash too (measured: 'grid: 1px' and
        // 'grid: row' fall).
        CssPropertyId.GridTemplateRows or CssPropertyId.GridTemplateColumns
            => static text => IsRowOrColumnTrackList(text),
        CssPropertyId.GridTemplateAreas => IsGridTemplateAreas,
        CssPropertyId.GridTemplate => IsGridTemplateValue,
        CssPropertyId.Grid => IsGridShorthandValue,
        // CSS Grid 1 §7.2: an implicit track list is a track list that never says 'subgrid' and
        // never repeats (measured: 'grid-auto-rows: subgrid' and '… repeat(2, 1px)' both fall while
        // '1px 2px' and 'min-content' stand).
        // CSS Gap 1 §3: a gap is 'normal' or one non-negative <length-percentage>, one per axis.
        // 'auto' is the value the flexbox draft gave it and no engine measured here reads it
        // (measured: 'row-gap: auto' and 'column-gap: 1px 2px' fall, 'gap: 1px 2px' stands). The
        // two axes and the pair are gated where the family is already gated, above; the legacy
        // names take the same shape.
        CssPropertyId.GridGap => static text => IsGapValue(text, 2),
        CssPropertyId.GridColumnGap or CssPropertyId.GridRowGap
            => static text => IsGapValue(text, 1),
        // CSS Transforms 2 §3: the individual transform properties. A scale is one, two or three
        // numbers or percentages — never a length, never 'auto', never a comma list — and the
        // percentage is the number it multiplies (measured: 'scale: 50%' reads back '0.5'). A
        // translate is one, two or three lengths, and the percentage is the axis it moves along.
        CssPropertyId.Scale => IsScaleValue,
        CssPropertyId.Translate => IsTranslateValue,
        // CSS View Transitions 1 §6: the grouping is 'normal', 'contain', 'allow-discrete' or a
        // name the page invented — any single ident is an answer, and two of them are not
        // (measured: 'unknown-ident' and '--a' stand, 'normal contain' and '1px' fall). The
        // property is echoed from its own table, which is where that answer is written.
        // Scroll-driven animations 1 §3: the range pair and its two longhands are 'normal' or a
        // timeline-range name with an optional offset; two offsets in one range is no range, 'auto'
        // is not a range, and an area name the timeline does not have is no value (all measured).
        // They are echoed from their own table too, so their grammar lives there.
        // CSS 2.1 §8.5 with CSS Sizing 4: a corner radius is one or two <length-percentage>s, and
        // the physical shorthand's four-and-four form is the one place a slash divides the two
        // axes. 'top left 1px' is the corner name from a draft that never shipped (measured).
        CssPropertyId.BorderTopLeftRadius or CssPropertyId.BorderTopRightRadius
            or CssPropertyId.BorderBottomLeftRadius or CssPropertyId.BorderBottomRightRadius
            or CssPropertyId.BorderStartStartRadius or CssPropertyId.BorderStartEndRadius
            or CssPropertyId.BorderEndStartRadius or CssPropertyId.BorderEndEndRadius
            => static text => IsPairOf(text, IsRadiusCornerValue),
        // The physical shorthand is the same two lists, only up to four corners per list.
        CssPropertyId.BorderRadius => IsBorderRadiusValue,
        _ => null,
    };

    /// <summary>CSS Fonts 4 §5.3's <c>font-style</c>: one of the three words, or 'oblique' with the
    /// angle it leans by. The angle is at most a right one whichever way it leans (measured:
    /// 'oblique 90deg' and 'oblique -20deg' stand, 'oblique 91deg' and 'oblique 100deg' fall) and
    /// every unit the tokenizer prices is priced in degrees here, because
    /// <c>oblique 0.25turn</c> is the same slant written longer (measured, both).</summary>
    private static bool IsFontStyleValue(string text)
    {
        var words = SplitComponents(text);
        if (words.Count is < 1 or > 2) return false;
        if (words.Count == 1) return IsOneOf(words[0], "normal", "italic", "oblique");
        if (!words[0].Equals("oblique", StringComparison.OrdinalIgnoreCase)) return false;
        return IsObliqueAngle(words[1]);
    }

    /// <summary>The angle 'oblique' takes: a plain angle whose size is at most 90 degrees. A math
    /// function is left to the type check that runs after this one, which already prices
    /// <c>calc(45deg * 2)</c> as an angle.</summary>
    private static bool IsObliqueAngle(string token)
    {
        if (IsMathFunctionCall(token, _ => true)) return true;
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(
                token, out var number, out var unit)) return false;
        double degrees = unit.ToLowerInvariant() switch
        {
            "deg" => number,
            "grad" => number * 360.0 / 400.0,
            "rad" => number * 180.0 / System.Math.PI,
            "turn" => number * 360.0,
            _ => double.NaN,
        };
        return !double.IsNaN(degrees) && degrees >= -90.0 && degrees <= 90.0;
    }

    /// <summary>CSS Fonts 4 §8.7's <c>font-variant</c> shorthand: 'normal' or 'none' saying the
    /// whole list, or the variant words with none repeated. Which words the shorthand opens is the
    /// reference engine's answer, word by word (measured): the ligature, caps, numeric,
    /// east-asian and position families among them — while 'scientific-infractions', 'fraction'
    /// and 'capital-width', which no longhand of this family spells, each fall.</summary>
    private static bool IsFontVariantValue(string text)
    {
        var words = SplitComponents(text);
        if (words.Count == 0) return false;
        if (words.Count == 1 && IsOneOf(words[0], "normal", "none")) return true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant();
            if (!FontVariantWords.Contains(lower)) return false;
            if (!seen.Add(lower)) return false;
        }
        return true;
    }

    /// <summary>The words of the five families the <c>font-variant</c> shorthand reads.</summary>
    private static readonly HashSet<string> FontVariantWords = new(StringComparer.Ordinal)
    {
        "common-ligatures", "no-common-ligatures", "discretionary-ligatures",
        "no-discretionary-ligatures", "historical-ligatures", "no-historical-ligatures",
        "contextual", "no-contextual",
        "small-caps", "all-small-caps", "petite-caps", "all-petite-caps", "unicase",
        "titling-caps", "historical-forms",
        "lining-nums", "oldstyle-nums", "proportional-nums", "tabular-nums",
        "diagonal-fractions", "stacked-fractions", "ordinal", "slashed-zero",
        "jis78", "jis04", "simplified", "traditional", "proportional-width", "full-width",
        "half-width", "third-width", "quarter-width",
        "ruby", "isolated-ligatures", "no-isolated-ligatures",
        "sub", "super",
    };

    /// <summary>CSS 2.1 §11.1.2's legacy <c>clip</c>: 'auto', or <c>rect()</c> carrying exactly
    /// four sides — each one a length or the word 'auto'. One, two or three sides are no rect, a
    /// percentage is no length here, and the shape functions of the modern property do not belong to
    /// this one. A zero may be written without its unit, which the engine prints as '0px'
    /// (measured: 'rect(0)', 'rect(0 0)', 'rect(1px auto)', 'rect(0%, 0%, 0%, 0%)' and 'inset(0)'
    /// each fall while 'rect(0 0 0 0)', 'rect(10px 5px 20px 5px)' and
    /// 'rect(auto auto auto auto)' stand). The sides come from <see cref="LegacyClipSides"/>, which
    /// is the one place the rect is cut.</summary>
    private static bool IsLegacyClipValue(string text)
    {
        if (text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)) return true;
        var sides = LegacyClipSides(text);
        if (sides == null) return false;
        foreach (var side in sides)
        {
            if (side == "auto") continue;
            if (IsLegacyClipLength(side)) continue;
            return false;
        }
        return true;
    }

    /// <summary>The four sides of the legacy 'clip' rect, trimmed and lowercased, in the order the
    /// property names them — top, right, bottom, left — or null when the text says no rect the
    /// property reads. One function cuts the rect for the grammar, the value text, the computed
    /// surface and the paint, because a side that lands in a different place in two of them is a
    /// bug no probe would localise. A rect is refused when its sides are not four, when a comma and
    /// a space separate them in the same rect, when a comma list ends in a comma, when one side is
    /// two tokens, or when something follows the parenthesis that closes it.</summary>
    public static List<string>? LegacyClipSides(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        const string rect = "rect(";
        if (!trimmed.StartsWith(rect, StringComparison.OrdinalIgnoreCase)) return null;
        var body = trimmed[rect.Length..];

        // CSS Syntax 3 §4.3.2: a function that runs to the end of the input is still that
        // function, so the closing parenthesis here is optional (measured: 'rect(0px 20px 40px 0px'
        // stands and reads back as a whole rect). Whatever follows the parenthesis that does close
        // it is outside the value (measured: 'rect(0px 20px 40px 0px))' falls).
        int depth = 1, close = -1;
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '(') depth++;
            else if (body[i] == ')' && --depth == 0) { close = i; break; }
        }
        var inner = close < 0 ? body : body[..close];
        if (close >= 0 && body[(close + 1)..].Trim().Length > 0) return null;

        List<string> sides;
        if (inner.Contains(','))
        {
            sides = SplitTopLevel(inner, ',');
            if (sides.Any(side => side.Trim().Length == 0
                                  || SplitOutsideParentheses(side.Trim()).Count != 1)) return null;
        }
        else
            sides = SplitOutsideParentheses(inner);
        if (sides.Count != 4) return null;
        return sides.Select(side => side.Trim().ToLowerInvariant()).ToList();
    }

    /// <summary>One side of a legacy rect as a length. Three shapes the general &lt;length&gt;
    /// check waves through are refused here, because the reference engine reads none of them: a
    /// unit it has never heard of ('rect(0px 20px 40px 0x)' falls), a percentage
    /// ('rect(50% 50% 50% 50%)' falls), and a number whose decimal point has no digit after it
    /// ('rect(123456789.px 1px 1px 1px)' falls — the same shape the sizing properties refuse too,
    /// measured on 'margin-left: 123456789.px').</summary>
    private static bool IsLegacyClipLength(string token)
    {
        if (IsMathFunctionCall(token, static word =>
                IsLengthOrPercentage(word) && !word.EndsWith("%", StringComparison.Ordinal)))
            return true;

        int i = 0;
        if (i < token.Length && (token[i] == '+' || token[i] == '-')) i++;
        int before = i;
        while (i < token.Length && char.IsAsciiDigit(token[i])) i++;
        bool digitsBeforePoint = i > before;
        if (i < token.Length && token[i] == '.')
        {
            i++;
            int after = i;
            while (i < token.Length && char.IsAsciiDigit(token[i])) i++;
            if (i == after) return false;                 // '1.px' — a point with nothing after it
        }
        else if (!digitsBeforePoint) return false;        // no number at all
        if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
        {
            // 'e' opens an exponent only when digits follow it: the 'e' of '1em' is the first
            // letter of a unit, and reading it as a power would refuse the length.
            int probe = i + 1;
            if (probe < token.Length && (token[probe] == '+' || token[probe] == '-')) probe++;
            int exponent = probe;
            while (probe < token.Length && char.IsAsciiDigit(token[probe])) probe++;
            // Only digits after the 'e' make it a power; '1e' by itself is no unit the engine
            // prices either, and the unit check below answers it.
            if (probe > exponent) i = probe;
        }

        var unit = token[i..];
        // A zero is the one number this property lets drop its unit (CSS Values 3 §8.6).
        if (unit.Length == 0) return IsBareZero(token);
        return unit != "%" && PricesALength(unit);
    }

    /// <summary>Whether the engine knows what a dimension wearing this unit is worth — the same
    /// set <see cref="Acrux.Core.Dom.Length.Parse"/> prices, asked of it rather than copied here so
    /// the two can never disagree about a unit.</summary>
    private static bool PricesALength(string unit) =>
        unit.Length > 0
        && Acrux.Core.Dom.Length.Parse("1" + unit) is not Acrux.Core.Dom.AutoLength
                                                    and not Acrux.Core.Dom.IntrinsicLength
                                                    and not Acrux.Core.Dom.PercentLength;

    /// <summary>CSS Masking 1 §7.1/§8: the geometry boxes of one mask layer. The longhand takes
    /// one, the shorthand's <c>origin / clip</c> pair takes two; 'margin-box' is not one of the
    /// six boxes and 'no-clip' belongs to the clip axis alone
    /// (all measured: 'mask-clip: margin-box' and 'mask-origin: border-box padding-box' fall).</summary>
    /// <summary>CSS Masking 1 §5.3's position: the shapes the background's position takes, less
    /// the three-token form this engine's reference reads only for the background (measured:
    /// 'mask-position: right 1px bottom' is dropped where 'background-position' takes it, and
    /// 'right 1px' and the four-token 'left 10px top 20px' both stand).</summary>
    private static bool IsMaskPositionValue(string text)
    {
        foreach (var layer in SplitTopLevel(text))
            if (SplitComponents(layer).Count == 3) return false;
        return ShorthandExpander.IsBackgroundPositionValue(text);
    }

    /// <summary>Whether 'only' was written at one end of the colour-scheme list rather than in its
    /// middle — the position the reference engine reads it from.</summary>
    private static bool SaysTheFlagAtAnEnd(System.Collections.Generic.List<string> words)
    {
        int at = words.FindIndex(w => w.Equals("only", StringComparison.OrdinalIgnoreCase));
        return at == 0 || at == words.Count - 1;
    }

    private static bool IsMaskBoxList(string text, bool allowNoClip)
    {
        var items = SplitTopLevel(text);
        if (items.Count == 0) return false;
        foreach (var item in items)
        {
            var words = SplitComponents(item);
            // One box per layer: the comma is what separates the layers, so a second box written
            // inside one layer is no geometry (measured: 'mask-clip: border-box padding-box' and
            // 'mask-origin: content-box border-box' are dropped while
            // 'content-box, border-box' stands).
            if (words.Count != 1) return false;
            foreach (var word in words)
            {
                var lower = word.ToLowerInvariant();
                if (lower == "no-clip")
                {
                    // 'no-clip' extends the clip axis only, and never twice in one layer.
                    if (!allowNoClip || words.Count != 1) return false;
                    continue;
                }
                if (lower is not ("border-box" or "padding-box" or "content-box" or "fill-box"
                    or "stroke-box" or "view-box")) return false;
            }
        }
        return true;
    }

    /// <summary>CSS Grid 1 §7.2 for one explicit axis: 'none', 'subgrid' on its own, or a track
    /// list. Line names in brackets belong to the list they frame, so '[a] 1px [b]' is one track
    /// written with its two names; 'span 2' and 'auto-flow' are the spellings of other properties
    /// and are no track here (both measured).</summary>
    private static bool IsRowOrColumnTrackList(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("subgrid", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("subgrid", StringComparison.OrdinalIgnoreCase)) return false;
        return HasATrackAndOnlyTracks(trimmed);
    }

    /// <summary>The track list every grid axis shares: bracketed line names, at least one track,
    /// and nothing between them that is not one or the other.</summary>
    private static bool HasATrackAndOnlyTracks(string text)
    {
        var items = SplitGridItems(text);
        if (items.Count == 0) return false;
        bool anyTrack = false;
        foreach (var item in items)
        {
            if (item.StartsWith("[", StringComparison.Ordinal)) continue;
            if (IsTrackBreadth(item) || item.Equals("repeat", StringComparison.OrdinalIgnoreCase)
                || item.StartsWith("repeat(", StringComparison.OrdinalIgnoreCase))
            {
                anyTrack = true;
                continue;
            }
            return false;
        }
        return anyTrack;
    }

    /// <summary>The items of a track list: bracketed line-name groups are one item each, and the
    /// rest splits at the spaces that are outside every parenthesis and quotation.</summary>
    private static List<string> SplitGridItems(string text)
    {
        var items = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(' || c == '[') depth++;
            else if ((c == ')' || c == ']') && depth > 0) depth--;
            else if (c == '"' || c == '\'')
            {
                i = SkipString(text, i);
                continue;
            }
            else if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (i > start) items.Add(text[start..i]);
                start = i + 1;
            }
        }
        if (text.Length > start) items.Add(text[start..]);
        return items;
    }

    /// <summary>CSS Grid 1 §7.3: the named areas are one string per row, and a row is a list of
    /// the names in it. A name is an identifier or the '.' that means no name; 'span' is the word
    /// of the placement properties, a bare identifier is not a row, and an empty row is no row
    /// (all measured).</summary>
    private static bool IsGridTemplateAreas(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        var rows = SplitComponents(trimmed);
        if (rows.Count == 0) return false;
        foreach (var row in rows)
        {
            if (row.Length < 2 || (row[0] != '"' && row[0] != '\'') || row[^1] != row[0])
                return false;
            var cells = row[1..^1];
            if (cells.Trim().Length == 0) return false;
            foreach (var cell in SplitWords(cells))
            {
                if (cell == ".") continue;
                if (char.IsAsciiDigit(cell[0])) return false;
                if (cell.Equals("span", StringComparison.OrdinalIgnoreCase)) return false;
                foreach (var c in cell)
                    if (!char.IsLetter(c) && c != '_' && c != '-' && c <= '\u007F') return false;
            }
        }
        return true;
    }

    /// <summary>CSS Grid 1 §7.2/§7.3's <c>grid-template</c>: 'none', or the two axes with the
    /// slash between them, or a list of area strings that stand for the rows. A single track list
    /// is no template — it is the axis longhand's own shape — and 'subgrid' needs the second axis
    /// beside it (all measured: 'grid-template: 1px 2px' and 'grid-template: subgrid' fall while
    /// '"a" 1px', 'none / 1px' and 'subgrid / subgrid' stand).</summary>
    private static bool IsGridTemplateValue(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        var parts = SplitTopLevel(trimmed, '/');
        if (parts.Count == 1)
        {
            var one = parts[0].Trim();
            if (one.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            return SaysNothingButAreasAndTracks(one);
        }
        if (parts.Count != 2) return false;
        foreach (var part in parts)
        {
            var axis = part.Trim();
            if (axis.Length == 0) return false;
            if (!IsRowOrColumnTrackList(axis) && !SaysNothingButAreasAndTracks(axis)) return false;
        }
        return true;
    }

    /// <summary>Whether the components of a grid axis are area strings and, at most, one track
    /// list — the spelling that gives the rows their names.</summary>
    private static bool SaysNothingButAreasAndTracks(string text)
    {
        var items = SplitGridItems(text);
        if (items.Count == 0) return false;
        bool anyString = false;
        foreach (var item in items)
        {
            if (item[0] is '"' or '\'')
            {
                if (!IsGridTemplateAreas(item)) return false;
                anyString = true;
                continue;
            }
            if (IsTrackBreadth(item) || item.StartsWith("repeat(", StringComparison.Ordinal)
                || item.StartsWith("minmax(", StringComparison.Ordinal)
                || item.StartsWith("fit-content(", StringComparison.Ordinal)
                || item.StartsWith("calc(", StringComparison.Ordinal)
                || item.StartsWith("[", StringComparison.Ordinal)) continue;
            return false;
        }
        return anyString;
    }

    /// <summary>CSS Grid 1 §7.2.1's <c>grid</c> shorthand: a template — and the template this
    /// property takes is the one with the slash in it, or 'none', or a list of area strings — with
    /// the flow and the implicit tracks beside it. A single track list is no value here, 'subgrid'
    /// is not one either, and the flow word never stands on its own
    /// (all measured: 'grid: 1px', 'grid: row', 'grid: subgrid' and 'grid: 1px / 2px / 3px' each
    /// fall while '1px / 2px', '"a" / 1px' and 'none / auto' stand).</summary>
    private static bool IsGridShorthandValue(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        var parts = SplitTopLevel(trimmed, '/');
        if (parts.Count >= 3) return false;
        var head = parts[0].Trim();
        if (parts.Count == 1)
        {
            if (head.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            return SaysNothingButAreasAndTracks(head);
        }
        if (!IsRowOrColumnTrackList(head) && !SaysNothingButAreasAndTracks(head)) return false;
        var second = parts[1].Trim();
        return IsRowOrColumnTrackList(second)
            || SaysNothingButAreasAndTracks(second)
            || second.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>CSS Grid 1 §7.2: whether a track list says 'subgrid' or repeats. An implicit track
    /// list does neither, which is the only difference between it and an explicit one.</summary>
    private static bool SaysSubgridOrRepeat(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("subgrid", StringComparison.Ordinal)
            || lower.Contains("repeat(", StringComparison.Ordinal);
    }

    /// <summary>CSS Gap 1 §3: 'normal' or a non-negative <length-percentage>, at most
    /// <paramref name="max"/> of them. 'auto' is the value the flexbox draft gave the property and
    /// no engine measured here reads it, a comma is no separator and a negative gap is no gap at
    /// all (measured: 'row-gap: auto', 'gap: 1px, 2px' and 'column-gap: -1px' each fall).</summary>
    private static bool IsGapValue(string text, int max)
    {
        if (text.IndexOf(',') >= 0) return false;
        var words = SplitComponents(text);
        if (words.Count == 0 || words.Count > max) return false;
        if (words.Count > 1 && words.Any(word => word.Equals("normal",
                StringComparison.OrdinalIgnoreCase))) return false;
        foreach (var word in words)
        {
            if (word.Equals("normal", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsMathFunctionCall(word, IsLengthOrPercentage)) continue;
            if (!IsNonNegativeBoxLength(word)) return false;
        }
        return true;
    }

    /// <summary>CSS Transforms 2 §3: 'none', or one to three numbers or percentages. A length is
    /// no scale factor, 'auto' is not in the grammar and a comma would be a list of layers the
    /// property does not have (all measured: 'scale: 1px', 'scale: auto' and 'scale: 1, 2' each
    /// fall while 'scale: 50% 50%' stands).</summary>
    private static bool IsScaleValue(string text)
    {
        // A comma would be a list of layers, and this property has one element to scale
        // (measured: 'scale: 1, 2' is dropped where 'scale: 1 2' stands).
        if (text.Contains(',')) return false;
        var words = SplitComponents(text);
        if (words.Count is < 1 or > 3) return false;
        if (words.Count == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var word in words)
        {
            if (IsMathFunctionCall(word, IsNumberOrPercentage)) continue;
            if (IsNumber(word) || IsPercentage(word)) continue;
            return false;
        }
        return true;
    }

    /// <summary>CSS Transforms 2 §3: 'none', or one to three lengths, percentages or <c>calc()</c>s
    /// — and a fourth is a length the property has no axis for
    /// (measured: 'translate: 1px 2px 3px 4px' and 'translate: auto' fall).</summary>
    private static bool IsTranslateValue(string text)
    {
        if (text.Contains(',')) return false;
        var words = SplitComponents(text);
        if (words.Count is < 1 or > 3) return false;
        if (words.Count == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var word in words)
        {
            if (IsMathFunctionCall(word, IsLengthOrPercentage)) continue;
            if (IsBoxLength(word) && !SaysAKeywordNotALength(word)) continue;
            return false;
        }
        return true;
    }

    /// <summary>Whether a token is one of the words a length property spells and never measures —
    /// 'auto', 'none' and the intrinsic keywords. <c>IsLengthOrPercentage</c> reads a bare word as
    /// a number with no unit, which is why the shape has to be asked here.</summary>
    private static bool SaysAKeywordNotALength(string token)
    {
        if (token.Length == 0 || char.IsAsciiDigit(token[0])
            || token[0] is '+' or '-' or '.') return false;
        return true;
    }

    /// <summary>CSS Backgrounds 3 §5.3's shorthand: one or two lists of one to four corner radii,
    /// divided by the slash. A comma divides nothing in this property, a corner named in the list
    /// belongs to a draft that never shipped (measured: 'top left 1px' falls) and 'auto' is a radius
    /// in no spelling of it either.</summary>
    private static bool IsBorderRadiusValue(string text)
    {
        if (text.Contains(',')) return false;
        var halves = SplitTopLevel(text, '/');
        foreach (var half in halves)
        {
            var words = SplitComponents(half);
            if (words.Count is < 1 or > 4) return false;
            foreach (var word in words)
                if (!IsRadiusCornerValue(word)) return false;
        }
        return true;
    }

    /// <summary>CSS Backgrounds 3 §5.3 for a corner: one or two <length-percentage>s, the second
    /// the radius of the other axis. A negative radius is no radius
    /// (measured: 'border-top-left-radius: 1px 2px' and '10% 20%' both stand).</summary>
    private static bool IsRadiusCornerValue(string token)
    {
        if (IsMathFunctionCall(token, IsLengthOrPercentage)) return true;
        return IsNonNegativeBoxLength(token);
    }

    /// <summary>One <image> — the set every image property draws from, and the list of them is the
    /// layer properties' own shape. 'cross-fade()' and 'element()' are in the spec's <image> and in
    /// no engine measured here (both refused, through 'background-image', 'mask-image',
    /// 'list-style-image' and 'border-image-source' alike).</summary>
    private static bool IsOneImageReference(string text)
    {
        var words = SplitComponents(text);
        if (words.Count != 1) return false;
        var word = words[0];
        if (word.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        if (SaysAnImageNoEngineReads(word)) return false;
        return IsImageFunction(word);
    }

    /// <summary>The three slots of a rule line, each of them filled at most once.</summary>
    private static bool IsColumnRuleShorthandValue(string text)
    {
        var words = SplitComponents(text);
        if (words.Count is < 1 or > 3) return false;
        bool width = false, style = false, colour = false;
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant();
            if (!style && IsOneOf(lower, "none", "hidden", "dotted", "dashed", "solid", "double",
                "groove", "ridge", "inset", "outset")) { style = true; continue; }
            if (!width && Acrux.Core.Css.Resolver.CssPropertyApplier.IsBorderWidthToken(word))
            { width = true; continue; }
            if (!colour && IsOneColor(word)) { colour = true; continue; }
            return false;
        }
        return true;
    }

    /// <summary>The two image functions the reference engine will not read for any property.</summary>
    private static bool SaysAnImageNoEngineReads(string token)
    {
        var lower = token.ToLowerInvariant();
        return lower.StartsWith("cross-fade(", StringComparison.Ordinal)
            || lower.StartsWith("element(", StringComparison.Ordinal);
    }

    /// <summary>A number or a percentage, which is the operand type the individual transform
    /// properties price their <c>calc()</c>s against.</summary>
    private static bool IsNumberOrPercentage(string token)
        => IsNumber(token) || IsPercentage(token) || IsLengthOrPercentage(token);

    /// <summary>CSS Box Alignment 3 §6: the two halves of a 'place-*' value, each judged by the
    /// word set of the longhand it writes. A half can be two words of its own ('first baseline'),
    /// which is why the split is decided by what reads rather than by a count of spaces — the same
    /// reading the cascade gives the pair, so the shorthand is never accepted on one surface and
    /// dropped on another (measured: 'place-content: left' falls, 'place-content: baseline center'
    /// stands, 'place-self: auto' stands and 'place-items: auto' falls).</summary>
    private static bool IsPlacePairValue(string text,
        Acrux.Core.Css.Resolver.CssPropertyApplier.AlignmentWords blockWords,
        Acrux.Core.Css.Resolver.CssPropertyApplier.AlignmentWords inlineWords)
    {
        var parts = SplitComponents(text);
        if (parts.Count == 0) return false;
        string? block = Acrux.Core.Css.Resolver.CssPropertyApplier
            .CanonicalAlignmentOf(parts[0], blockWords);
        int taken = 1;
        // The first half of the pair is two words of its own ('first baseline') just as often as it
        // is one half of a two-value pair ('center start'), and the reading that comes first is the
        // one that decides: one word for the block axis, and only when that is no value, two.
        if (block == null && parts.Count >= 2)
        {
            block = Acrux.Core.Css.Resolver.CssPropertyApplier
                .CanonicalAlignmentOf(parts[0] + " " + parts[1], blockWords);
            taken = 2;
        }
        if (block == null) return false;
        if (parts.Count == taken) return true;
        return Acrux.Core.Css.Resolver.CssPropertyApplier.CanonicalAlignmentOf(
            string.Join(" ", parts.Skip(taken)), inlineWords) != null;
    }

    /// <summary>A comma-separated list of one keyword set — the animation and transition longhands
    /// each take their own list, and the separator is the comma and only the comma (measured).</summary>
    private static bool IsKeywordList(string text, params string[] words)
    {
        var items = SplitTopLevel(text);
        if (items.Count == 0) return false;
        foreach (var item in items)
            if (!IsOneOf(item.Trim(), words)) return false;
        return true;
    }

    /// <summary>A &lt;easing-function&gt;# (CSS Easing 1 §2): one of the seven keywords, or
    /// <c>cubic-bezier()</c> with four numbers, or <c>steps()</c> with a positive integer and an
    /// optional position. 'bogus' is neither (measured: 'animation-timing-function: bogus' falls).</summary>
    /// <summary>Each part of every animation layer, judged by the longhand that owns it: an easing
    /// that is no easing, an iteration count that is not a number or 'infinite', and a name that is
    /// neither an ident nor a string each leave the layer — and so the declaration — out.</summary>
    private static bool IsAnimationShorthandValue(string text)
    {
        foreach (var entry in Acrux.Core.Css.ShorthandExpander.ClassifyAnimationEntries(text))
        {
            if (entry[1].Length > 0 && !IsTimingFunction(entry[1])) return false;
            if (entry[3].Length > 0 && !entry[3].Equals("infinite", StringComparison.OrdinalIgnoreCase)
                && !Acrux.Core.Css.ShorthandExpander.IsNumberToken(entry[3])) return false;
            if (entry[7].Length > 0 && !IsAnimationNameItem(entry[7])) return false;
        }
        return true;
    }

    private static bool IsAnimationNameItem(string token)
    {
        if (token.Length >= 2 && (token[0] == '"' || token[0] == '\'')
            && token[^1] == token[0]) return true;
        return IsIdentifierLike(token);
    }

    private static bool IsTimingFunctionList(string text)
    {
        var items = SplitTopLevel(text);
        if (items.Count == 0) return false;
        foreach (var item in items)
            if (!IsTimingFunction(item.Trim())) return false;
        return true;
    }

    private static bool IsTimingFunction(string word)
    {
        if (TimingKeywords.Contains(word)) return true;
        if (!word.EndsWith(")", StringComparison.Ordinal)) return false;
        int open = word.IndexOf('(');
        if (open <= 0) return false;
        var name = word[..open].Trim().ToLowerInvariant();
        var args = SplitTopLevel(word[(open + 1)..^1]);
        if (name == "cubic-bezier")
        {
            // Four numbers, and the two that place the control points along the progress axis are
            // inside the unit interval (CSS Easing 1 §2, measured: 'cubic-bezier(0.1, 2, 3, 4)' and
            // 'cubic-bezier(2, 0, 1, 1)' are each dropped while 'cubic-bezier(0, 0, 1, 1)' stands;
            // the two that measure the value itself may be anywhere).
            if (args.Count != 4) return false;
            var numbers = new double[4];
            for (int i = 0; i < 4; i++)
                if (!TryPlainNumber(args[i].Trim(), out numbers[i])) return false;
            return numbers[0] >= 0.0 && numbers[0] <= 1.0
                && numbers[2] >= 0.0 && numbers[2] <= 1.0;
        }
        if (name == "steps")
        {
            if (args.Count is < 1 or > 2) return false;
            if (!TryPlainNumber(args[0].Trim(), out var count) || count < 1) return false;
            return args.Count == 1
                || IsOneOf(args[1].Trim(), "jump-start", "jump-end", "jump-none", "jump-both",
                    "start", "end");
        }
        // 'linear()' takes a list of outputs, each one optionally followed by its position — and the
        // position is part of the same argument, so the canonical spelling the engine prints
        // ('linear(0 0%, 1 100%)') has to read as valid too, or a value the echo itself wrote would
        // be no value at all.
        return name == "linear" && args.Count > 0 && args.All(arg =>
        {
            var words = SplitComponents(arg);
            if (words.Count is < 1 or > 2) return false;
            if (!IsNumber(words[0])) return false;
            return words.Count == 1 || IsLengthOrPercentage(words[1]);
        });
    }

    /// <summary>Whether a token reads as a number at all, the exponent spellings included — the
    /// question a property that takes a &lt;number&gt; asks before it decides how to print one.</summary>
    private static bool IsPlainNumber(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    private static bool TryPlainNumber(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);


    /// <summary>CSS Blend 1 §3's sixteen, shared by 'mix-blend-mode' and 'background-blend-mode' —
    /// one list, because the two properties would otherwise disagree about 'plus-darker'.</summary>
    private static readonly string[] BlendModeWords =
    {
        "normal", "multiply", "screen", "overlay", "darken", "lighten", "color-dodge", "color-burn",
        "hard-light", "soft-light", "difference", "exclusion", "hue", "saturation", "color",
        "luminosity",
    };

    /// <summary>CSS Anchor Positioning 1 §4.1: the inset-area vocabulary, split by the axis each
    /// word measures along. Two words name one area only when they take one slot each; the same
    /// axis twice is no area (measured: 'x-start y-end' and 'left top' stand).</summary>
    private static readonly string[] InsetAreaAlongX =
    {
        "left", "right", "x-start", "x-end", "span-left", "span-right", "span-x-start", "span-x-end",
        "inline-start", "inline-end", "span-inline-start", "span-inline-end",
    };

    private static readonly string[] InsetAreaAlongY =
    {
        "top", "bottom", "y-start", "y-end", "span-top", "span-bottom", "span-y-start", "span-y-end",
        "block-start", "block-end", "span-block-start", "span-block-end",
    };

    private static readonly string[] InsetAreaEitherAxis =
    {
        "center", "span-all", "span-x", "span-y", "self-start", "self-end", "self-center",
        "span-self-start", "span-self-end", "span-self-center",
    };

    private static bool IsInsetAreaWord(string word) =>
        System.Array.IndexOf(InsetAreaAlongX, word) >= 0
        || System.Array.IndexOf(InsetAreaAlongY, word) >= 0
        || System.Array.IndexOf(InsetAreaEitherAxis, word) >= 0;

    /// <summary>&lt;inset-area&gt;: one or two side keywords that do not both measure along
    /// the same axis. The echo table reads this one too, so a sheet, the CSSOM and supports()
    /// cannot disagree about what 'left top' names.</summary>
    internal static bool IsInsetArea(string text)
    {
        var words = SplitComponents(text);
        if (words.Count == 0 || words.Count > 2) return false;
        foreach (var word in words)
            if (!IsInsetAreaWord(word.ToLowerInvariant())) return false;
        if (words.Count == 1) return true;
        string First(string w) => w.ToLowerInvariant();
        var a = First(words[0]);
        var b = First(words[1]);
        bool AlongX(string w) => System.Array.IndexOf(InsetAreaAlongX, w) >= 0;
        bool AlongY(string w) => System.Array.IndexOf(InsetAreaAlongY, w) >= 0;
        bool Either(string w) => System.Array.IndexOf(InsetAreaEitherAxis, w) >= 0;
        // Two words fill the two slots: one from each axis, or two that resolve per axis
        // themselves. A single-axis word beside a both-axis word leaves the pair ambiguous and is
        // no area (measured: 'left span-top' and 'self-start self-end' stand while
        // 'span-block-end self-start' and 'top bottom' fall).
        return (AlongX(a) && AlongY(b)) || (AlongY(a) && AlongX(b)) || (Either(a) && Either(b));
    }

    /// <summary>A pair of values from one set — the shape of 'text-overflow', which takes one
    /// keyword per axis and so never more than two.</summary>
    private static bool IsPairOf(string text, Func<string, bool> one)
    {
        var parts = SplitComponents(text);
        if (parts.Count == 0 || parts.Count > 2) return false;
        foreach (var part in parts)
            if (!one(part)) return false;
        return true;
    }

    private static bool IsTextOverflowKeyword(string token) =>
        token.Equals("clip", StringComparison.OrdinalIgnoreCase)
        || token.Equals("ellipsis", StringComparison.OrdinalIgnoreCase)
        || (token.Length >= 2 && (token[0] == '"' || token[0] == '\'')
            && token[^1] == token[0])
        || IsOtherFunctionCall(token);

    /// <summary>The shorthand of one border side: a width, a style and a colour, each at most once
    /// and in any order, and nothing else beside them. The same matchers the applier and the
    /// shorthand expander use decide the parts, so the three cannot disagree about a declaration.</summary>
    private static bool IsOutlineValue(string text)
    {
        var parts = SplitComponents(text);
        if (parts.Count is < 1 or > 3) return false;
        int width = 0, style = 0, colour = 0;
        foreach (var part in parts)
        {
            // 'auto' is a style the outline takes and a border does not (CSS UI 4 §3.3).
            if (ShorthandExpander.IsBorderStyle(part)
                || part.Equals("auto", StringComparison.OrdinalIgnoreCase)) { style++; continue; }
            if (Acrux.Core.Css.Resolver.CssPropertyApplier.IsBorderWidthToken(part)) { width++; continue; }
            if (ColorParser.IsColorToken(part) || IsOtherFunctionCall(part)) { colour++; continue; }
            return false;
        }
        return width <= 1 && style <= 1 && colour <= 1;
    }

    /// <summary>CSS Generated Content 3 §5.1: <c>normal</c>, <c>none</c>, or one or more content
    /// items — a quoted string, an image, <c>counter()</c>, <c>counters()</c>, <c>attr()</c> and the
    /// four quote keywords — with at most one <c>/ &lt;string&gt;</c> alt text on the end. A bare
    /// ident is not an item, so 'content: bogus' and 'content: normal "a"' are no value at all, and
    /// neither is a function with the wrong arguments (all measured). This engine reads the
    /// two-argument <c>attr()</c> the reference engine refuses, deliberately and on purpose
    /// (docs/CSS-HANDOFF.md #244), so that form stays a value here.</summary>
    private static bool IsContentValue(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        var tokens = Acrux.Core.Css.Resolver.EchoedProperties.SplitTopLevel(trimmed);
        if (tokens.Count == 0) return false;
        int slash = tokens.IndexOf("/");
        if (slash >= 0)
        {
            // The alt text is exactly one string, and it is the last thing in the value.
            if (slash != tokens.Count - 2 || !IsContentString(tokens[slash + 1])) return false;
            tokens = tokens.GetRange(0, slash);
            if (tokens.Count == 0) return false;
        }
        foreach (var token in tokens)
            if (!IsContentItem(token)) return false;
        return true;
    }

    private static bool IsContentItem(string token)
    {
        if (IsContentString(token)) return true;
        if (token is "open-quote" or "close-quote" or "no-open-quote" or "no-close-quote") return true;
        int open = token.IndexOf('(');
        if (open <= 0 || !token.EndsWith(")", StringComparison.Ordinal)) return false;
        var name = token[..open].ToLowerInvariant();
        var inner = token[(open + 1)..^1].Trim();
        switch (name)
        {
            // counter(<counter-name> [, <counter-style>]) — the style is an ident or a
            // symbols()-style function, and 'counter(x, 1)' has a number where a style name
            // belongs, which is no counter (measured).
            case "counter":
                {
                    var parts = inner.Split(',');
                    if (parts.Length is < 1 or > 2) return false;
                    if (!IsContentIdent(parts[0].Trim())) return false;
                    return parts.Length == 1 || IsCounterStyle(parts[1].Trim());
                }
            // counters(<counter-name>, <string> [, <counter-style>])
            case "counters":
                {
                    var parts = SplitOnTopLevelCommas(inner);
                    if (parts.Length is < 2 or > 3) return false;
                    if (!IsContentIdent(parts[0].Trim())) return false;
                    if (!IsContentString(parts[1].Trim())) return false;
                    return parts.Length == 2 || IsCounterStyle(parts[2].Trim());
                }
            case "attr":
                {
                    // CSS Values 5 §2.2.2: 'attr( <attr-name> [ <attr-type> ]? , <declaration-value>? )'.
                    // The fallback is a declaration value, so it may be a string, a run of strings, a
                    // bare ident, a length, another function, a second comma — or nothing at all — and
                    // every one of those is a value the reference engine keeps (measured). What it
                    // refuses is a value with no name: 'attr(, "y")'. This engine additionally reads
                    // the space-separated two-argument form the reference engine refuses, deliberately
                    // and on purpose (docs/CSS-HANDOFF.md #244).
                    int comma = inner.IndexOf(',');
                    if (comma < 0)
                    {
                        var words = Acrux.Core.Css.Resolver.EchoedProperties.SplitTopLevel(inner);
                        if (words.Count is < 1 or > 2 || !IsContentIdent(words[0])) return false;
                        return words.Count == 1 || IsContentString(words[1]);
                    }
                    var head = Acrux.Core.Css.Resolver.EchoedProperties.SplitTopLevel(inner[..comma]);
                    if (head.Count is < 1 or > 2) return false;
                    if (!IsContentIdent(head[0])) return false;
                    return head.Count == 1 || IsAttrType(head[1]);
                }
            case "url": return inner.Length > 0;
            // 'target-counter()' and the Content 4 'content()' are not values this engine takes.
            default: return false;
        }
    }

    /// <summary>The optional type of an <c>attr()</c>: <c>type( … )</c> whatever its argument
    /// spells, or a bare type keyword. Both forms are measured ('attr(x type(string), "y")',
    /// 'attr(x type(&lt;length&gt;), 5px)', 'attr(x string, "y")' all stand).</summary>
    private static bool IsAttrType(string token) =>
        (token.StartsWith("type(", StringComparison.OrdinalIgnoreCase)
            && token.EndsWith(")", StringComparison.Ordinal))
        || IsContentIdent(token)
        || (token.StartsWith("<", StringComparison.Ordinal) && token.EndsWith(">", StringComparison.Ordinal));

    /// <summary>A counter style name — one of the built-in keywords or a name an @counter-style
    /// block defines. A function is not a style name here: 'counter(x, symbols("a" "b"))' is
    /// refused (measured), even though @counter-style itself takes symbols().</summary>
    private static bool IsCounterStyle(string token) =>
        IsContentIdent(token) || token.StartsWith("--", StringComparison.Ordinal);

    /// <summary>CSS Masking 1 §5.2: <c>clip-path</c> is <c>none</c>, a <c>&lt;clip-source&gt;</c>
    /// url, or a basic shape with an optional <c>&lt;geometry-box&gt;</c>. The shape's own arguments
    /// are checked (CSS Shapes 1 §4) because a stray token inside them makes the whole declaration
    /// invalid — 'circle(50%, bogus)' is refused while 'circle(50% at 0 0)' is not. A shape this
    /// reader does not recognise is accepted rather than dropped: the clip already drawn by the
    /// painter must not disappear because the grammar fell short.</summary>
    internal static bool IsClipPathValue(string text, bool bareBoxAllowed = false)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        var tokens = SplitComponents(trimmed);
        if (tokens.Count == 0) return false;
        int index = 0;
        if (GeometryBoxWords.Contains(tokens[0].ToLowerInvariant())) index = 1;
        // 'shape-outside' names its box on its own — a box with no shape is the rectangle of that
        // box (CSS Shapes 1 §11, measured: 'shape-outside: margin-box' reads back 'margin-box'),
        // where the same words alone are no clip-path value at all.
        if (index >= tokens.Count)
            return bareBoxAllowed && tokens.Count == 1;
        var shape = tokens[index];
        if (shape.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return true;
        if (index + 1 < tokens.Count
            && GeometryBoxWords.Contains(tokens[index + 1].ToLowerInvariant())) index++;
        else if (index + 1 < tokens.Count) return false;
        int open = shape.IndexOf('(');
        if (open <= 0 || !shape.EndsWith(")", StringComparison.Ordinal)) return false;
        var name = shape[..open].ToLowerInvariant();
        var inner = shape[(open + 1)..^1].Trim();
        return name switch
        {
            "circle" or "ellipse" => IsRadiusShape(name, inner),
            "inset" => IsInsetShape(inner),
            "polygon" => IsPolygonShape(inner),
            "xywh" => IsXywhShape(inner),
            "path" => inner.Length >= 2 && (inner[0] == '"' || inner[0] == '\''),
            // 'content-box' written inside a shape is a stray token; anything else is unknown.
            _ => true,
        };
    }

    private static readonly string[] GeometryBoxWords =
    {
        "border-box", "padding-box", "content-box", "margin-box",
        "fill-box", "stroke-box", "view-box",
    };

    private static string[] ShapeWords(string text) => text.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsLengthPercentage(string word)
    {
        if (word.Length == 0) return false;
        if (word.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("max(", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase)) return true;
        return Acrux.Core.Css.Resolver.CssPropertyApplier.IsLengthOrPercentageToken(word);
    }

    /// <summary>circle() and ellipse(): up to two radii (or none), then an optional 'at' position of
    /// one to three parts.</summary>
    private static bool IsRadiusShape(string name, string inner)
    {
        if (inner.Length == 0) return true;   // 'ellipse()' is the shape without radii
        var words = ShapeWords(inner);
        int at = Array.FindIndex(words, w => w.Equals("at", StringComparison.OrdinalIgnoreCase));
        var radii = at < 0 ? words : words[..at];
        var position = at < 0 ? Array.Empty<string>() : words[(at + 1)..];
        if (radii.Length > (name == "circle" ? 1 : 2)) return false;
        foreach (var radius in radii)
            if (!IsLengthPercentage(radius)
                && !radius.Equals("closest-side", StringComparison.OrdinalIgnoreCase)
                && !radius.Equals("farthest-side", StringComparison.OrdinalIgnoreCase)) return false;
        // 'at' is optional: 'circle(50%)' has no position at all, and one to three words follow it
        // when it is there.
        if (at >= 0 && position.Length is < 1 or > 3) return false;
        foreach (var part in position)
            if (!IsLengthPercentage(part) && !PositionWords.Contains(part.ToLowerInvariant())) return false;
        return true;
    }

    private static readonly string[] PositionWords =
        { "left", "right", "top", "bottom", "center" };

    /// <summary>inset(): one to four offsets, then an optional 'round' radius list.</summary>
    private static bool IsInsetShape(string inner)
    {
        var words = ShapeWords(inner);
        if (words.Length == 0) return false;
        int round = Array.FindIndex(words, w => w.Equals("round", StringComparison.OrdinalIgnoreCase));
        var offsets = round < 0 ? words : words[..round];
        var radii = round < 0 ? Array.Empty<string>() : words[(round + 1)..];
        if (offsets.Length is < 1 or > 4) return false;
        foreach (var offset in offsets) if (!IsLengthPercentage(offset)) return false;
        if (round >= 0 && (radii.Length is < 1 or > 8)) return false;
        foreach (var radius in radii)
            if (radius != "/" && !IsLengthPercentage(radius)) return false;
        return true;
    }

    /// <summary>polygon(): an optional fill rule, then the coordinate pairs. One pair is still a
    /// value — the reference engine accepts 'polygon(0 0)' and prints it back (measured).</summary>
    private static bool IsPolygonShape(string inner)
    {
        var points = SplitOnTopLevelCommas(inner);
        if (points.Length == 0) return false;
        int first = 0;
        var head = points[0].Trim().ToLowerInvariant();
        if (head is "nonzero" or "evenodd") first = 1;
        for (int i = first; i < points.Length; i++)
        {
            var words = ShapeWords(points[i]);
            if (words.Length != 2) return false;
            if (!IsLengthPercentage(words[0]) || !IsLengthPercentage(words[1])) return false;
        }
        return points.Length > first;
    }

    /// <summary>xywh(): exactly four lengths or percentages, and nothing beside them.</summary>
    private static bool IsXywhShape(string inner)
    {
        var words = ShapeWords(inner);
        if (words.Length != 4) return false;
        foreach (var word in words) if (!IsLengthPercentage(word)) return false;
        return true;
    }

    /// <summary>The comma-separated parts of a function's arguments, with a nested call or a quoted
    /// string each kept as the one part it is. Also how 'content' prints a counter's arguments.</summary>
    internal static string[] SplitCounterArguments(string inner) => SplitOnTopLevelCommas(inner);
    private static string[] SplitOnTopLevelCommas(string inner)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        bool inString = false;
        char quote = '"';
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == quote) inString = false;
                continue;
            }
            if (c is '"' or '\'') { inString = true; quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == ',' && depth == 0) { parts.Add(inner[start..i]); start = i + 1; }
        }
        parts.Add(inner[start..]);
        return parts.ToArray();
    }

    private static bool IsContentString(string token) => token.Length >= 2
        && (token[0] == '"' || token[0] == '\'') && token[^1] == token[0];

    private static bool IsContentIdent(string token)
    {
        if (token.Length == 0) return false;
        foreach (var c in token)
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '\\')) return false;
        return !char.IsDigit(token[0]);
    }

    /// <summary>One layer of a shadow list: the lengths, at most one colour, and for a box shadow
    /// at most one 'inset'. A word that is none of those — a style keyword, say — is no shadow
    /// component at all (measured: 'text-shadow: 2px dashed rgb(1, 2, 3)' is dropped).</summary>
    private static bool IsShadowLayer(string text, bool allowInset, int maxLengths)
    {
        int lengths = 0, colour = 0, inset = 0;
        foreach (var part in SplitComponents(text))
        {
            if (allowInset && part.Equals("inset", StringComparison.OrdinalIgnoreCase))
            {
                if (++inset > 1) return false;
                continue;
            }
            if (IsLengthOrPercentage(part) || IsNumber(part)) { lengths++; continue; }
            // A colour function is one component; 'rgb(1, 2, 3)' has no top-level space in it.
            if (IsLegacyColor(part) || IsOtherFunctionCall(part)) { colour++; continue; }
            return false;
        }
        return lengths >= 2 && lengths <= maxLengths && colour <= 1;
    }

    private static bool IsTextShadowValue(string text) => EveryShadowLayer(text, allowInset: false, maxLengths: 3);

    private static bool IsBoxShadowValue(string text) => EveryShadowLayer(text, allowInset: true, maxLengths: 4);

    private static bool EveryShadowLayer(string text, bool allowInset, int maxLengths)
    {
        var layers = SplitTopLevel(text, ',');
        if (layers.Count == 0) return false;
        foreach (var layer in layers)
        {
            var trimmed = layer.Trim();
            // 'none' is the initial of both shadow properties and is a value of its own.
            if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.Length == 0 || !IsShadowLayer(trimmed, allowInset, maxLengths)) return false;
        }
        return true;
    }

    /// <summary>Split on commas that are not inside a function.</summary>
    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == separator && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    private static bool IsBorderSideValue(string text, int maxWidth = 4)
    {
        var parts = SplitComponents(text);
        if (parts.Count == 0 || parts.Count > 6) return false;
        int width = 0, style = 0, colour = 0;
        foreach (var part in parts)
        {
            // The style is read first: 'none', 'hidden', 'dotted' … are border styles and, unlike a
            // width or a colour, they are words a colour parser would not claim.
            if (ShorthandExpander.IsBorderStyle(part)) { style++; continue; }
            if (Acrux.Core.Css.Resolver.CssPropertyApplier.IsBorderWidthToken(part)) { width++; continue; }
            if (ColorParser.IsColorToken(part) || IsOtherFunctionCall(part)) { colour++; continue; }
            return false;
        }
        return width <= maxWidth && style <= 1 && colour <= 1;
    }

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

    /// <summary>The components of a value: whitespace and commas separate them, but a function
    /// call is one component — its own arguments are somebody else's separators.</summary>
    private static List<string> SplitComponents(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == '"' || c == '\'')
            {
                i = SkipString(text, i);
                continue;
            }
            else if (depth == 0 && (char.IsWhiteSpace(c) || c == ','))
            {
                if (i > start) parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        if (text.Length > start)
        {
            var last = text[start..];
            if (last.Trim().Length > 0) parts.Add(last.Trim());
        }
        return parts;
    }

    private static int SkipString(string text, int quoteAt)
    {
        char quote = text[quoteAt];
        for (int i = quoteAt + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == quote) return i;
        }
        return text.Length;
    }

    /// <summary>The math functions of CSS Values 4 \u00a710.10-\u00a710.12: they take an arithmetic
    /// expression, and an expression's operands are numbers, lengths, percentages, nested calls and
    /// variables \u2014 never a bare identifier. That is what decides 'calc(bogus)' and 'calc(1px + )'.
    /// 'round()', 'mod()' and the like take a keyword or a comma-separated list on top of that.</summary>
    private static bool IsMathFunctionCall(string token, Func<string, bool> isOperand)
    {
        int open = token.IndexOf('(');
        if (open <= 0 || !token.EndsWith(")")) return false;
        string name = token[..open].ToLowerInvariant();
        if (!MathFunctions.Contains(name)) return false;
        string inner = token[(open + 1)..^1];
        if (name == "clamp")
        {
            var args = SplitTopLevel(inner);
            return args.Count == 3 && args.All(a => IsExpression(a, isOperand));
        }
        if (name is "round")
        {
            // 'round( [nearest|up|down|to-zero] , <value>, <resolution> )' — the strategy is the one
            // argument that may be left out, so the call has two or three arguments and the last two
            // are the ones that carry the numbers (measured on the b26 round family).
            var args = SplitTopLevel(inner);
            if (args.Count is not (2 or 3)) return false;
            int first = args.Count == 3 ? 1 : 0;
            if (args.Count == 3 && args[0].ToLowerInvariant()
                is not ("nearest" or "up" or "down" or "to-zero")) return false;
            return IsExpression(args[first], isOperand) && IsExpression(args[first + 1], isOperand);
        }
        if (name is "mod" or "rem")
        {
            var args = SplitTopLevel(inner);
            return args.Count == 2 && IsExpression(args[0], isOperand) && IsExpression(args[1], isOperand);
        }
        // 'min()' and 'max()' take a comma-separated list of two or more expressions, which is the
        // one place a comma belongs inside a math function; the single-argument forms take one.
        if (name is "min" or "max")
        {
            var args = SplitTopLevel(inner);
            return args.Count >= 2 && args.All(a => IsExpression(a, isOperand));
        }
        if (name is "abs" or "sign" or "sqrt" or "exp" or "log")
        {
            var args = SplitTopLevel(inner);
            return args.Count == 1 && IsExpression(args[0], isOperand);
        }
        if (name is "pow" or "hypot")
        {
            var args = SplitTopLevel(inner);
            return args.Count >= (name == "pow" ? 2 : 1) && args.All(a => IsExpression(a, isOperand));
        }
        return IsExpression(inner, isOperand);
    }

    /// <summary>An arithmetic expression: operands joined by + and -, or a single operand, with
    /// multiplication and division allowed between a number and a dimension. Every + or - has to
    /// have something on both sides of it, which is the whole of what 'calc(1px + )' fails.</summary>
    private static bool IsExpression(string text, Func<string, bool> isOperand)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == '"' || c == '\'') { i = SkipString(text, i); continue; }
            else if (depth == 0 && (c == '+' || c == '-'))
            {
                var before = text[start..i].Trim();
                // A sign belongs to the number in front of it unless there is something to take it
                // from: 'calc(-37px + 1px)' and 'calc(1px * -2)' are one operand each, while
                // 'calc(1px + )' has an operator with nothing behind it and is no value at all
                // (measured through 'supports(width: …)').
                bool operatorHere = before.Length > 0 && !before.EndsWith("+") && !before.EndsWith("-")
                    && !before.EndsWith("*") && !before.EndsWith("/");
                if (!operatorHere) continue;
                parts.Add(before);
                parts.Add(c.ToString());
                start = i + 1;
            }
        }
        var tail = text[start..].Trim();
        if (tail.Length > 0) parts.Add(tail);
        if (parts.Count == 0) return false;

        // Operators have to alternate with operands, and the list must start and end with an operand.
        for (int i = 0; i < parts.Count; i++)
        {
            bool operatorSlot = i % 2 == 1;
            if (operatorSlot)
            {
                if (parts[i] is not ("+" or "-" or "*" or "/")) return false;
                continue;
            }
            if (parts[i] is "+" or "-" or "*" or "/") return false;
            if (!IsOperandOrNested(parts[i], isOperand)) return false;
        }
        return parts.Count % 2 == 1;
    }

    private static bool IsOperandOrNested(string text, Func<string, bool> isOperand)
    {
        // An operand is a number, a dimension, a percentage, a nested function or a product of
        // them ('2 * 3px'); a leading sign belongs to the number itself.
        if (text.Length == 0) return false;
        // A group in its own parentheses is an expression of its own, operators and all, which is
        // what makes 'calc((10px + 2px))' and 'calc(2 * (1px + 2px))' values (measured, both).
        if (text[0] == '(' && MatchingClose(text, 0) == text.Length - 1)
            return IsExpression(text[1..^1], isOperand);
        if (text.StartsWith("(", StringComparison.Ordinal) && text.EndsWith(")"))
            text = text[1..^1].Trim();
        // Split on the spaces that are outside every parenthesis: a nested 'calc(100px - 50px)' is
        // one operand, and cutting it at its own spaces takes a working declaration down with it.
        // A product's operators are spaced first, because CSS Values 4 §10.2.1 puts no whitespace
        // requirement on '*' and '/' — 'calc(10px*2)' is the same value as 'calc(10px * 2)' and the
        // reference engine takes both (measured).
        var pieces = SplitOutsideParentheses(SpaceProductOperators(text));
        bool any = false;
        foreach (var piece in pieces)
        {
            if (piece is "*" or "/") continue;
            if (!IsOperandOrNestedOne(piece, isOperand)) return false;
            any = true;
        }
        return any;
    }

    /// <summary>The index of the parenthesis that closes the one at <paramref name="open"/>, or -1
    /// when nothing does.</summary>
    private static int MatchingClose(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' || c == '\'') { i = SkipString(text, i); continue; }
            if (c == '(') depth++;
            else if (c == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static List<string> SplitOutsideParentheses(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == '"' || c == '\'') { i = SkipString(text, i); continue; }
            else if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (i > start) parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        if (text.Length > start) parts.Add(text[start..]);
        return parts;
    }

    /// <summary>Put a space on each side of every <c>*</c> and <c>/</c> that stands at the top level of
    /// one math operand — outside every parenthesis and every string. CSS Values 4 §10.2.1 requires no
    /// whitespace around a product's operators, so <c>calc(10px*2)</c> is the same value as
    /// <c>calc(10px * 2)</c> and the reader of an operand list has to see three pieces in either
    /// spelling (measured: the reference engine takes both through <c>supports()</c>).</summary>
    private static string SpaceProductOperators(string text)
    {
        if (text.IndexOf('*') < 0 && text.IndexOf('/') < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + 8);
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                int next = SkipString(text, i);
                sb.Append(text, i, next - i);
                i = next - 1;
                continue;
            }
            if (c == '(') { depth++; sb.Append(c); continue; }
            if (c == ')') { depth--; sb.Append(c); continue; }
            if (depth == 0 && (c == '*' || c == '/'))
            {
                sb.Append(' ').Append(c).Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsOperandOrNestedOne(string token, Func<string, bool> isOperand)
    {
        if (isOperand(token)) return true;
        // A group inside a product is read as the operand it is.
        if (token.Length > 1 && token[0] == '(') return IsOperandOrNested(token, isOperand);
        int open = token.IndexOf('(');
        if (open > 0 && token.EndsWith(")"))
        {
            string name = token[..open].ToLowerInvariant();
            if (MathFunctions.Contains(name)) return IsMathFunctionCall(token, isOperand);
            return true;   // var(), attr(), env() \u2026: somebody else's argument, taken as written
        }
        return false;
    }

    /// <summary>A function call that is not a math function. Its arguments belong to the property's
    /// own grammar, which this helper does not have, so the call is accepted \u2014 the point is that a
    /// bare identifier is not a call at all, which is what separates 'path("M 0 0")' from 'bogus'.</summary>
    private static bool IsOtherFunctionCall(string token)
    {
        int open = token.IndexOf('(');
        return open > 0 && token.EndsWith(")") && !MathFunctions.Contains(token[..open].ToLowerInvariant());
    }

    private static readonly HashSet<string> MathFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "calc", "min", "max", "clamp", "round", "mod", "rem", "abs", "sign",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "pow", "sqrt", "hypot", "log", "exp"
    };

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(text[start..].Trim());
        return parts;
    }

    /// <summary>A &lt;transform-list&gt;: 'none' alone, or functions from the transform vocabulary, each
    /// with its own argument list. A function this engine has not heard of is still a function call,
    /// so the name is what is checked, not the arguments.</summary>
    private static bool IsTransformList(string text)
    {
        var parts = SplitComponents(text);
        if (parts.Count == 0) return false;
        if (parts.Count == 1 && parts[0].Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var part in parts)
        {
            int open = part.IndexOf('(');
            if (open <= 0 || !part.EndsWith(")")) return false;
            if (!TransformFunctions.Contains(part[..open].ToLowerInvariant())) return false;
        }
        return true;
    }

    private static readonly HashSet<string> TransformFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "matrix", "matrix3d", "translate", "translate3d", "translateX", "translateY", "translateZ",
        "scale", "scale3d", "scaleX", "scaleY", "scaleZ", "rotate", "rotate3d", "rotateX", "rotateY",
        "rotateZ", "skew", "skewX", "skewY", "perspective"
    };

    /// <summary>One comma-separated transition: a duration, an optional delay, an optional timing
    /// function and the property it animates. Only one bare name may stand for the property, and a
    /// second one has nothing left to be, so the item that carries two is no transition value.</summary>
    private static bool IsTransitionList(string text)
    {
        foreach (var item in SplitTopLevel(text))
        {
            if (item.Length == 0) return false;
            bool propertySeen = false;
            foreach (var part in SplitComponents(item))
            {
                if (part.EndsWith(")"))
                {
                    int open = part.IndexOf('(');
                    if (open <= 0) return false;
                    var name = part[..open].ToLowerInvariant();
                    if (TimingFunctions.Contains(name)) continue;
                    // A duration may be written as a math function, and the reference engine reads it
                    // when the expression comes out a time: 'transition: calc(1s + 1ms) ease' stands
                    // while 'transition: calc(1px) ease' leaves the whole shorthand out
                    // (measured, both).
                    if (MathFunctions.Contains(name)
                        && CssMathValue.WellTyped(part, out var category)
                        && (category == "time" || category == null)) continue;
                    return false;
                }
                if (IsTime(part) || IsOneOf(part, "none", "auto")) continue;
                if (TimingKeywords.Contains(part.ToLowerInvariant())) continue;
                if (propertySeen) return false;
                // A property slot is a custom name, 'all' or a single identifier — the first bare
                // word in the item is the property whatever it spells.
                if (!IsIdentifierLike(part)) return false;
                propertySeen = true;
            }
        }
        return true;
    }

    private static readonly HashSet<string> TimingFunctions = new(StringComparer.OrdinalIgnoreCase)
    { "cubic-bezier", "steps", "linear", "ease", "ease-in", "ease-out", "ease-in-out",
      "step-start", "step-end" };

    private static readonly HashSet<string> TimingKeywords = new(StringComparer.OrdinalIgnoreCase)
    { "linear", "ease", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end" };

    /// <summary>A &lt;time&gt;# — every comma-separated item a number with the unit 's' or 'ms', or one
    /// math function that resolves to a time. The unit set is closed and small: '2min' is a perfectly
    /// good CSS dimension that is not a time, and the reference engine leaves the declaration out
    /// (measured: 'transition-duration: 2min' reads back empty from every surface).</summary>
    private static bool IsTimeList(string text, bool nonNegative = false)
    {
        foreach (var item in SplitTopLevel(text))
        {
            if (item.Length == 0) return false;
            if (item.EndsWith(")"))
            {
                int open = item.IndexOf('(');
                if (open <= 0) return false;
                if (!MathFunctions.Contains(item[..open])) return false;
                if (CssMathValue.WellTyped(item, out var category) && category != "time") return false;
                continue;
            }
            if (!IsTime(item)) return false;
            // A duration has the range [0, ∞): a literal negative time is dropped, but '-0s'
            // is zero and survives. A calc() is left to the computed-value clamp.
            if (nonNegative && Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(item, out var number, out _) && number < 0)
                return false;
        }
        return true;
    }

    private static bool IsTime(string token)
    {
        if (token.Length < 2) return false;
        string unit = token[^1].ToString();
        string rest = token[..^1];
        if (!IsNumber(rest) || (unit != "s" && unit != "S")) return IsNumber(token[..^2]) &&
            token[^2..].Equals("ms", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>A &lt;number&gt; from zero up, exponent and leading plus included, as the two flex
    /// factors are written: <c>flex-grow: -1</c> is a count below nothing and <c>20%</c> is not a count
    /// at all (measured, both refused).</summary>
    private static bool IsNonNegativeCount(string word)
    {
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(word, out var number, out var unit))
            return false;
        return unit.Length == 0 && number >= 0;
    }

    /// <summary>A &lt;length-percentage&gt; that is not negative. A negative basis measures less than
    /// nothing, so <c>flex-basis: -5px</c> is no value at all — and the same negative written the other
    /// way round in the shorthand falls with it (measured, both).</summary>
    /// <summary>A &lt;length&gt; that says what it is measured in — with one exception CSS 2.1 made
    /// and the reference engine still keeps: a bare <c>0</c> is such a length, while a bare
    /// <c>1</c> is not. Measured on each of the properties that take one: 'border-spacing: 0' and
    /// 'perspective: 0' are values, 'letter-spacing: 1', 'perspective: 10',
    /// 'text-decoration-thickness: 1' and 'text-underline-offset: 1' are not. 'overflow-clip-margin'
    /// is the one that does not even take the bare zero (measured: '0' falls, '0px' stands), so it
    /// asks for <see cref="IsDimensionedLength"/> instead.</summary>
    private static bool IsDimensionedLengthOrZero(string token) =>
        IsDimensionedLength(token) || IsBareZero(token);

    /// <summary>The same &lt;length&gt; with the bare zero left out: a unit or a percentage on the
    /// number, nothing else.</summary>
    private static bool IsDimensionedLength(string token)
    {
        if (IsMathFunctionCall(token, IsLengthOrPercentage)) return true;
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out _, out var unit))
            return false;
        return unit.Length > 0 && unit != "%" && IsLengthOrPercentage(token);
    }

    /// <summary>A &lt;length-percentage&gt; that says what it is measured in, the bare zero of CSS 2.1
    /// kept and a math function left to its own type rules. This is the shape every box property
    /// takes, and a bare number is not in it: 'width: 12', 'margin-left: 12', 'font-size: 12',
    /// 'border-width: 12', 'row-gap: 12', 'flex-basis: 12', 'border-radius: 12', 'translate: 12',
    /// 'scroll-padding-left: 12' and 'inset: 12' are each dropped by the reference engine (measured,
    /// one row per property) while 'width: 0', 'width: 50%' and 'width: calc(2 * 1px)' all stand. The
    /// seats whose whole value IS a number — 'line-height', 'zoom', 'tab-size', 'border-image-width'
    /// and an SVG stroke — ask a grammar of their own and are untouched here.</summary>
    internal static bool IsBoxLength(string token) =>
        IsDimensionedLengthOrZero(token) || IsPercentage(token);

    /// <summary>A &lt;length-percentage&gt; of the box family that is not negative — the shape the
    /// paddings, the gaps, a flex basis and a corner radius are written in.</summary>
    private static bool IsNonNegativeBoxLength(string token) =>
        IsBoxLength(token) && !IsNegativeValue(token);

    private static bool IsBareZero(string token) =>
        Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out var number, out var unit)
        && unit.Length == 0 && number == 0;

    /// <summary>A &lt;length-percentage&gt; that says what it is measured in, the bare zero included:
    /// the spacing and decoration-offset family, where '50%' is a value and '1' is not (measured).
    /// </summary>
    private static bool IsDimensionedLengthPercentageOrZero(string token) =>
        IsDimensionedLengthOrZero(token)
        || (Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out _, out var unit)
            && unit == "%");

    /// <summary>The same length with nothing negative about it — the range 'border-spacing' and
    /// 'perspective' are defined on (measured: 'border-spacing: 1px -1px' and 'perspective: -10px'
    /// are each dropped, while a negative written inside a calc() is left to the type check).</summary>
    private static bool IsNonNegativeDimensionedLength(string token)
    {
        if (!IsDimensionedLengthOrZero(token)) return false;
        return !Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out var number, out _)
            || number >= 0;
    }

    /// <summary>One &lt;track-breadth&gt; of a grid track list (CSS Grid 1 §7.2): a keyword, a
    /// dimensioned length, a flex, or one of the functions that take arguments. 'repeat()' is the
    /// track-list notation the auto tracks do not take (measured: 'grid-auto-rows:
    /// repeat(auto-fill, 10px)' is dropped while 'minmax(auto, 1fr)' and 'fit-content(10px)' stand).
    /// </summary>
    private static bool IsTrackBreadth(string token)
    {
        var lower = token.ToLowerInvariant();
        if (lower is "auto" or "min-content" or "max-content") return true;
        // <line-size> is a <length-percentage>, so a track may be written as a share of the grid
        // (measured: 'grid-template-columns: 50%' and '100px 50%' both stand).
        if (lower.EndsWith("%", StringComparison.Ordinal))
            return IsLengthOrPercentage(lower)
                && (!Acrux.Core.Dom.Animations.CssValueTokenizer
                    .TrySplitUnit(lower, out var percent, out _) || percent >= 0);
        if (lower.StartsWith("minmax(", StringComparison.Ordinal)
            || lower.StartsWith("fit-content(", StringComparison.Ordinal)
            || lower.StartsWith("calc(", StringComparison.Ordinal)) return true;
        // A math function is a breadth of its own, not only 'calc()': the reference engine reads
        // 'clamp(30px, 40%, 80px)' and 'min(60px, 50%)' as the <line-size> they work out to
        // (measured, both, as a bare track and beside '1fr').
        if (IsMathFunctionCall(lower, IsLengthOrPercentage)) return true;
        if (Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out _, out var unit))
            return unit.Equals("fr", StringComparison.OrdinalIgnoreCase)
                || IsDimensionedLengthOrZero(token);
        return false;
    }

    /// <summary>A track list: the breadths separated by spaces, with commas allowed between the
    /// items (the reference engine reads 'grid-auto-rows: 1fr, 2fr' and '1fr 2fr' alike). Split at
    /// the top level so a function's own comma is not a separator.</summary>
    private static bool IsTrackList(string text)
    {
        var items = SplitTopLevel(text, ',');
        if (items.Count == 0) return false;
        foreach (var item in items)
        {
            var words = SplitTopLevel(item, ' ');
            if (words.Count == 0) return false;
            if (!words.All(IsTrackBreadth)) return false;
        }
        return true;
    }

    /// <summary>Whether the text is one number as a value may spell it: an optional sign, digits, and
    /// a fraction that — when a decimal point is written at all — has at least one digit after it.
    /// CSS Syntax 3 §4.3.12 lets a token end on the period, but no reference engine reads a value
    /// there: 'opacity: 12.', 'line-height: 12.', 'flex-grow: 12.' and 'z-index: 12.' are each refused
    /// while '12', '12.5' and '.5' all stand (measured, snapshots/_probe_b307_number.html).</summary>
    private static bool IsNumber(string text)
    {
        if (text.Length == 0) return false;
        int i = text[0] is '+' or '-' ? 1 : 0;
        int digits = i;
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        bool any = i > digits;
        if (i < text.Length && text[i] == '.')
        {
            i++;
            int frac = i;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            if (i == frac) return false;
            any = true;
        }
        return any && i == text.Length;
    }

    private static bool IsNumberList(string text, params string[] keywords)
    {
        foreach (var part in SplitComponents(text))
        {
            if (IsNumber(part) || IsMathFunctionCall(part, IsLengthOrPercentage) ||
                IsOneOf(part, keywords)) continue;
            return false;
        }
        return true;
    }

    /// <summary>One &lt;number&gt; or &lt;percentage&gt; for 'opacity', whose range is closed by the
    /// clamping that happens later: 'opacity: 1e2' is a number and prints as '100', and '100%' is the
    /// same value written the other way and prints as '1' (measured, both).</summary>
    private static bool IsOpacityNumber(string token) =>
        IsNumber(token)
        // The tokenizer reads the exponent and sign spellings a number also has: 'opacity: 1e2' is
        // a number the reference engine takes and prints as '100' (measured), while 'IsNumber' above
        // is the spelling the other grammars ask for.
        || (Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out _, out var unit)
            && (unit == "%" || unit.Length == 0))
        || IsMathFunctionCall(token, IsLengthOrPercentage);

    private static bool IsNumberOnly(string text) =>
        SplitComponents(text).Count == 1 && IsNumber(text.Trim());

    private static bool IsIntegerList(string text)
    {
        foreach (var part in SplitComponents(text))
        {
            string token = part;
            if (token.Length > 0 && (token[0] == '+' || token[0] == '-')) token = token[1..];
            if (token.Length > 0 && token.All(char.IsAsciiDigit)) continue;
            if (IsMathFunctionCall(part, IsLengthOrPercentage)) continue;
            return false;
        }
        return true;
    }

    /// <summary>One &lt;grid-line&gt;, or a slash-separated list of them (the shorthands take the same
    /// words in the same order). See the arm that calls this for what the reference engine takes.</summary>
    /// <summary>A &lt;grid-line&gt; list: 'auto', 'none', a line name, a line number, or 'span' with
    /// the name or the number it spans. A bare 'span' is no line — the reference engine drops it
    /// (measured: 'grid-area: span' reads back empty and computes 'auto'), and two words with no
    /// slash between them are no line either ('grid-area: main head' is dropped the same way).
    /// <paramref name="maxParts"/> is the number of lines the property has: two for a grid axis,
    /// four for an area, one for a single edge — and a third line on an axis is no value at all
    /// (measured: 'grid-column: a / b / c' falls).</summary>
    private static bool IsGridLineValue(string text, int maxParts = 4)
    {
        var parts = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(text, '/');
        if (parts.Count == 0 || parts.Count > maxParts) return false;
        foreach (var part in parts)
        {
            var words = SplitComponents(part);
            if (words.Count == 1)
            {
                if (IsIntegerWord(words[0]) || (IsIdentifierLike(words[0])
                    && !words[0].Equals("span", StringComparison.OrdinalIgnoreCase))) continue;
                return false;
            }
            if (words.Count == 2 && words[0].Equals("span", StringComparison.OrdinalIgnoreCase)
                && (IsIntegerWord(words[1])
                    // 'span a' spans the line named 'a' (measured: it stands where '1 span a' —
                    // a line number in front of the keyword — falls).
                    || (IsIdentifierLike(words[1])
                        && !words[1].Equals("span", StringComparison.OrdinalIgnoreCase)))) continue;
            return false;
        }
        return true;
    }

    private static bool IsIntegerWord(string text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    /// <summary>A &lt;custom-ident&gt;: the identifier characters, and it does not begin with a digit
    /// or with the '--' a custom property wears. Case is the author's own — this is the question a
    /// name asks, not the question a keyword asks.</summary>
    public static bool IsCustomIdent(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (char.IsAsciiDigit(text[0])) return false;
        if (text.StartsWith("--", StringComparison.Ordinal)) return false;
        foreach (var c in text)
            if (!char.IsLetter(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_' or '\\')) return false;
        return true;
    }

    internal static bool IsIdentifierLike(string text)
    {
        if (text.Length == 0) return false;
        foreach (var c in text)
            if (!char.IsLetter(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_' or '\\')) return false;
        return true;
    }

    /// <summary>CSS Scrollbars 1 \u00a73.4: 'auto', or 'stable' with an optional 'both-edges'. Nothing
    /// else, and a value that carries a second word which is not 'both-edges' is no value at all
    /// (measured: 'stable foo' and 'stable stable' both leave the declaration dropped and the
    /// property at 'auto').</summary>
    private static bool IsScrollbarGutter(string text)
    {
        var parts = SplitComponents(text);
        return parts.Count switch
        {
            1 => parts[0].Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Equals("stable", StringComparison.OrdinalIgnoreCase),
            2 => parts[0].Equals("stable", StringComparison.OrdinalIgnoreCase) &&
                 parts[1].Equals("both-edges", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>The one spelling of <c>color-scheme</c> the reference engine prints, or null for a
    /// value it drops (CSS Color Adjustment 1 §5.2, measured word by word). 'normal' is alone or
    /// nothing; 'light' and 'dark' are keywords in any case and print lower-cased; 'only' is a
    /// keyword ONLY in its own lower case — 'ONLY' is neither the keyword nor a name — and it moves
    /// behind the schemes it modifies ('only dark' reads 'dark only'); anything else has to be a
    /// name, and a name keeps the author's case ('Brown' reads 'Brown', 'none' reads 'none').</summary>
    internal static string? CanonicalColorScheme(string text)
    {
        var words = SplitComponents(text);
        if (words.Count == 0) return null;
        if (words.Count == 1 && words[0].Equals("normal", StringComparison.OrdinalIgnoreCase))
            return "normal";
        var kept = new System.Collections.Generic.List<string>(words.Count);
        bool saysOnly = false;
        foreach (var word in words)
        {
            if (word.Equals("normal", StringComparison.OrdinalIgnoreCase)) return null;
            if (word.Equals("light", StringComparison.OrdinalIgnoreCase)) kept.Add("light");
            else if (word.Equals("dark", StringComparison.OrdinalIgnoreCase)) kept.Add("dark");
            else if (word.Equals("only", StringComparison.Ordinal))
            {
                if (saysOnly) return null;
                saysOnly = true;
            }
            else if (word.Equals("only", StringComparison.OrdinalIgnoreCase)) return null;
            else if (!IsIdentifierLike(word)) return null;
            else kept.Add(word);
        }
        if (kept.Count == 0) return null;
        // The flag reads either in front of the whole list or behind it, and the engine prints it
        // last whichever way it was written (measured: 'only light' and 'light dark only' both say
        // '… only', while 'light only dark' — a flag dropped between two members — is refused).
        if (saysOnly && !SaysTheFlagAtAnEnd(words)) return null;
        if (saysOnly) kept.Add("only");
        return string.Join(" ", kept);
    }

    /// <summary>How many colours a colour-only property takes, and the words beside them. The
    /// border list is the four-edge one (CSS Backgrounds 3 §5.3); the rest take exactly one.</summary>
    private static readonly Dictionary<string, (int Max, string[] Extra)> ColorValueArity =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["color"] = (1, Array.Empty<string>()),
            ["background-color"] = (1, Array.Empty<string>()),
            ["caret-color"] = (1, new[] { "auto" }),
            ["accent-color"] = (1, new[] { "auto" }),
            // 'invert' is deliberately absent: CSS UI 4 §5.2 gives this property a <color>, and
            // Color 4 §14.1 keeps the keyword for forced-colours mode only (b239 measured the
            // declaration dropped, and b285 measured the same through the CSSOM).
            ["outline-color"] = (1, Array.Empty<string>()),
            ["column-rule-color"] = (1, Array.Empty<string>()),
            ["text-decoration-color"] = (1, Array.Empty<string>()),
            ["text-emphasis-color"] = (1, Array.Empty<string>()),
            ["border-color"] = (4, Array.Empty<string>()),
            ["border-top-color"] = (1, Array.Empty<string>()),
            ["border-right-color"] = (1, Array.Empty<string>()),
            ["border-bottom-color"] = (1, Array.Empty<string>()),
            ["border-left-color"] = (1, Array.Empty<string>()),
            ["border-block-color"] = (2, Array.Empty<string>()),
            ["border-block-start-color"] = (1, Array.Empty<string>()),
            ["border-block-end-color"] = (1, Array.Empty<string>()),
            ["border-inline-color"] = (2, Array.Empty<string>()),
            ["border-inline-start-color"] = (1, Array.Empty<string>()),
            ["border-inline-end-color"] = (1, Array.Empty<string>()),
            ["-webkit-text-fill-color"] = (1, Array.Empty<string>()),
            ["-webkit-text-stroke-color"] = (1, Array.Empty<string>()),
            ["-webkit-tap-highlight-color"] = (1, Array.Empty<string>()),
        };

    /// <summary>A list of one to <paramref name="max"/> colours, space divided, with the given
    /// keywords allowed in place of one. A comma is not a divider here: these properties have no
    /// layer list to write.</summary>
    internal static bool IsColorListValue(string text, int max, params string[] extra)
    {
        var words = SplitComponents(text);
        if (words.Count == 0 || words.Count > max) return false;
        foreach (var word in words)
        {
            if (extra.Any(name => name.Equals(word, StringComparison.OrdinalIgnoreCase))) continue;
            if (!IsWellFormedColor(word)) return false;
        }
        return true;
    }

    /// <summary>Whether a single word is a colour the engine can read, with the arguments its
    /// function name asks for. The arity is checked here rather than in the parser because the
    /// parser is deliberately forgiving about a page's numbers — a missing channel is not a
    /// missing number, it is a different colour than the one the page wrote.</summary>
    internal static bool IsWellFormedColor(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (token.StartsWith("#", StringComparison.Ordinal))
        {
            // A hex colour is 3, 4, 6 or 8 hex digits after the sign (CSS Color 4 §5); a '#' in
            // front of a function name is neither that nor anything else the grammar reads
            // (measured: '#rgb(0%, 50%, 100%)' is dropped).
            var digits = token[1..];
            if (digits.Length is not (3 or 4 or 6 or 8)) return false;
            return digits.All(Uri.IsHexDigit);
        }
        int open = token.IndexOf('(');
        if (open < 0) return ColorParser.IsColorToken(token);
        if (open == 0 || !token.EndsWith(")", StringComparison.Ordinal)) return false;
        var name = token[..open].TrimEnd().ToLowerInvariant();
        var inner = token[(open + 1)..^1].Trim();
        // 'light-dark()' takes two colours of its own, and a relative colour reads the base
        // colour through a 'from' clause whose channels are names, not numbers — both are the
        // parser's business, not the arity table's (measured: each stands as written).
        if (name == "light-dark")
        {
            var parts = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(inner, ',');
            return parts.Count == 2 && parts.All(part => IsWellFormedColor(part.Trim()));
        }
        if (name is "rgb" or "rgba" or "hsl" or "hsla" or "hwb"
            or "lab" or "lch" or "oklab" or "oklch")
        {
            if (inner.StartsWith("from ", StringComparison.OrdinalIgnoreCase)) return true;
            if (!TrySplitColorAlpha(inner, out var body, out var alpha)) return false;
            return HasColorChannels(body, alpha, 3);
        }
        if (name == "color")
        {
            if (inner.StartsWith("from ", StringComparison.OrdinalIgnoreCase)) return true;
            // 'color()' is a colour space and its three channels. The space is not one of the
            // channels, and a comma is not a separator this function was ever written with
            // (measured: 'color(1 2 3)', 'color(srgb, 1, 0, 0)', 'color(srgb 1 2 3 4)' and
            // 'color(srgb 1 0)' are each dropped, 'color(srgb 1 0 0 / 0.5)' stands).
            if (!TrySplitColorAlpha(inner, out var body, out var alpha)) return false;
            if (body.Contains(',', StringComparison.Ordinal)) return false;
            if (alpha != null && !IsAlphaWord(alpha)) return false;
            return SplitComponents(body).Count == 4;
        }
        if (name == "color-mix") return ColorParser.IsColorToken(token);
        return false;
    }

    /// <summary>Split a colour function's arguments at the slash that opens its alpha, refusing a
    /// second one and an alpha nobody wrote (measured: 'lab(50 20 30 / 0.5 / 0.5)' and
    /// 'rgb(1 2 3 / )' are dropped).</summary>
    private static bool TrySplitColorAlpha(string inner, out string body, out string? alpha)
    {
        body = inner; alpha = null;
        int first = IndexOfTopLevel(inner, '/');
        if (first < 0) return true;
        var tail = inner[(first + 1)..];
        if (IndexOfTopLevel(tail, '/') >= 0) return false;
        body = inner[..first].Trim();
        alpha = tail.Trim();
        return body.Length > 0 && alpha.Length > 0;
    }

    /// <summary>Whether a colour function's arguments are its three channels and at most one alpha,
    /// written in one of the two forms and never in both. The comma form carries its alpha as a
    /// fourth argument; the space form has no commas and puts the alpha behind a slash
    /// (measured: 'rgba(1 2 3)' and 'hsl(120, 50%, 50%, 0.5)' stand, while 'rgb(1, 2, 3 / 0.5)',
    /// 'rgb(1,2,3,0.5,)', 'lab(50 20 30 none)' and 'rgb(1 2 3 4)' are dropped).</summary>
    private static bool HasColorChannels(string body, string? alpha, int channels)
    {
        if (body.Contains(',', StringComparison.Ordinal))
        {
            if (alpha != null) return false;
            var parts = body.Split(',');
            if (parts.Length is < 3 or > 4) return false;
            return parts.All(part => part.Trim().Length > 0);
        }
        if (alpha != null && !IsAlphaWord(alpha)) return false;
        return SplitComponents(body).Count == channels;
    }

    /// <summary>An alpha: one word that says a number, a percentage of one, or
    /// <c>none</c> — and anything the parser has to read (a math function) is left to it.</summary>
    private static bool IsAlphaWord(string alpha)
    {
        if (SplitComponents(alpha).Count != 1) return false;
        if (alpha.Contains('(', StringComparison.Ordinal)) return true;
        if (alpha.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        var text = alpha.EndsWith("%", StringComparison.Ordinal) ? alpha[..^1] : alpha;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static int IndexOfTopLevel(string text, char marker)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == marker && depth == 0) return i;
        }
        return -1;
    }


    /// <summary>One of the border-image box-value parts (CSS Backgrounds 3 §5). The reference
    /// engine prints a part once when all of its sides agree — '50% 50% 50% 50%' reads '50%' and
    /// '1 1 1 1' reads '1' — and drops a value with a fifth side, a negative number, or a unit
    /// the part does not take. <paramref name="extra"/> names the one word this part accepts
    /// besides its numbers: 'fill' for the slice.</summary>
    private static string? CanonicalBoxPart(string text, int max, bool percentages, bool lengths,
        string? extra = null)
    {
        var words = SplitComponents(text);
        if (words.Count == 0) return null;
        var sides = new System.Collections.Generic.List<string>(words.Count);
        string? trailing = null;
        foreach (var word in words)
        {
            var p = word.ToLowerInvariant();
            if (extra != null && p == extra)
            {
                if (trailing != null) return null;
                trailing = p;
                continue;
            }
            if (p.EndsWith("%", StringComparison.Ordinal))
            {
                if (!percentages || !NonNegative(p[..^1])) return null;
                sides.Add(p); continue;
            }
            // A length has to say so with a unit: these parts take a bare number as their own
            // dimensionless unit, so '-1' is a negative number rather than a negative length.
            if (lengths && p.Length > 2 && !double.IsNaN(LengthValueOf(p)))
            {
                if (!NonNegative(p[..^2])) return null;
                sides.Add(p); continue;
            }
            if (!NonNegative(p)) return null;
            sides.Add(p);
        }
        if (sides.Count == 0 || sides.Count > max) return null;
        // The part is printed once when every side agrees (measured: '50% 50% 50% 50%' reads
        // '50%', '1 1 1 1' reads '1', and a repeat pair reads one word the same way).
        var kept = sides.All(side => side == sides[0])
            ? new System.Collections.Generic.List<string> { sides[0] } : sides;
        return trailing == null ? string.Join(" ", kept) : string.Join(" ", kept) + " " + trailing;
    }

    /// <summary>The number in front of a two-letter unit, or NaN when the word is not a length.</summary>
    private static double LengthValueOf(string word)
    {
        var head = word[..^2];
        return double.TryParse(head, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    }

    private static bool NonNegative(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
        && !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0;

    /// <summary>The slice: one to four numbers or percentages, and the word 'fill' at the end.</summary>
    internal static string? CanonicalBorderImageSlice(string text) =>
        CanonicalBoxPart(text, 4, percentages: true, lengths: false, extra: "fill");

    /// <summary>The width: one to four numbers, lengths, percentages or 'auto'.</summary>
    internal static string? CanonicalBorderImageWidth(string text)
    {
        if (text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        return CanonicalBoxPart(text, 4, percentages: true, lengths: true);
    }

    /// <summary>The outset: one to four numbers or lengths, and no percentages.</summary>
    internal static string? CanonicalBorderImageOutset(string text) =>
        CanonicalBoxPart(text, 4, percentages: false, lengths: true);

    /// <summary>The <c>border-image</c> shorthand, said the way the reference engine prints it:
    /// the source, the slice, then the three box parts divided by slashes and the repeat last,
    /// with every part the page left out named by its initial (CSS Backgrounds 3 §5.9, measured:
    /// 'url(a.png) 3' reads 'url("a.png") 3 / 1 / 0 stretch'). A value with no source in it —
    /// 'fill', 'red 3' — is no border-image at all.</summary>
    internal static string? CanonicalBorderImageShorthand(string text)
    {
        var groups = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(text, '/');
        if (groups.Count == 0 || groups.Count > 3) return null;
        string? source = null; var slice = new System.Collections.Generic.List<string>();
        var width = new System.Collections.Generic.List<string>();
        var outset = new System.Collections.Generic.List<string>();
        string? repeat = null; bool saysFill = false;
        for (int g = 0; g < groups.Count; g++)
        {
            foreach (var word in SplitComponents(groups[g]))
            {
                var p = word.ToLowerInvariant();
                if (p == "fill") { saysFill = true; continue; }
                if (p is "stretch" or "repeat" or "round" or "space")
                {
                    // The repeat is a pair of its own inside the shorthand (measured:
                    // 'url(a.png) stretch round' reads '… / 1 / 0 stretch round').
                    repeat = repeat == null ? p : repeat + " " + p;
                    continue;
                }
                if (word.StartsWith("url(", StringComparison.OrdinalIgnoreCase)
                    || IsGeneratedImageWord(word) || p == "none")
                {
                    if (source != null) return null;
                    source = word; continue;
                }
                if (g == 0) slice.Add(p);
                else if (g == 1) width.Add(p);
                else outset.Add(p);
            }
        }
        // A source is not required — 'border-image: 3' is a slice with the initial 'none' beside it
        // and reads back 'none 3 / 1 / 0 stretch' (measured) — but a value that says nothing but
        // 'fill', or says 'none' twice, is no value at all.
        if (source == null && slice.Count == 0 && width.Count == 0 && outset.Count == 0
            && repeat == null) return null;
        if (source == null && saysFill && slice.Count == 0) return null;
        source ??= "none";
        var sliceText = CanonicalBorderImageSlice(string.Join(" ", slice) + (saysFill ? " fill" : ""));
        if (slice.Count > 0 && sliceText == null) return null;
        sliceText ??= "100%";
        var widthText = width.Count == 0 ? "1" : CanonicalBorderImageWidth(string.Join(" ", width));
        var outsetText = outset.Count == 0 ? "0" : CanonicalBorderImageOutset(string.Join(" ", outset));
        if (widthText == null || outsetText == null) return null;
        var repeatText = repeat == null ? "stretch" : CanonicalBorderImageRepeat(repeat);
        if (repeatText == null) return null;
        // The shorthand says the source alone while every part beside it is still initial, and
        // then the whole five-part form once any of them has moved (measured: 'url(a.png)' reads
        // back as the url, 'url(a.png) 1 2 3 4' and 'url(a.png) stretch round' as the expansion).
        if (sliceText == "100%" && widthText == "1" && outsetText == "0" && repeatText == "stretch")
            return source;
        return $"{source} {sliceText} / {widthText} / {outsetText} {repeatText}";
    }

    /// <summary>Whether a token is one of the image functions a border-image source can name.</summary>
    private static bool IsGeneratedImageWord(string word) =>
        word.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith("radial-gradient(", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith("conic-gradient(", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith("repeating-linear-gradient(", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith("repeating-radial-gradient(", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith("repeating-conic-gradient(", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith("image(", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith("image-set(", StringComparison.OrdinalIgnoreCase);

    /// <summary>The repeat: one or two of the four words, printed once when both halves agree
    /// (measured: 'stretch stretch' reads 'stretch' while 'round stretch' keeps both).</summary>
    internal static string? CanonicalBorderImageRepeat(string text)
    {
        var words = SplitComponents(text);
        if (words.Count is < 1 or > 2) return null;
        var kept = new System.Collections.Generic.List<string>(words.Count);
        foreach (var word in words)
        {
            var p = word.ToLowerInvariant();
            if (p is not ("stretch" or "repeat" or "round" or "space")) return null;
            kept.Add(p);
        }
        return kept.Count == 2 && kept[0] == kept[1] ? kept[0] : string.Join(" ", kept);
    }

    /// <summary>The one spelling of <c>text-emphasis-position</c> (CSS Text Emphasis 1 §3.3,
    /// measured): the vertical side, then the horizontal one if the page said it, whichever order
    /// the page wrote them in. Two verticals are no position, and a lone horizontal side is either
    /// no value at all.</summary>
    internal static string? CanonicalTextEmphasisPosition(string text)
    {
        var words = SplitComponents(text);
        if (words.Count is < 1 or > 2) return null;
        string? vertical = null, horizontal = null;
        foreach (var word in words)
        {
            var p = word.ToLowerInvariant();
            if (p is "over" or "under")
            {
                if (vertical != null) return null;
                vertical = p;
            }
            else if (p is "left" or "right")
            {
                if (horizontal != null) return null;
                horizontal = p;
            }
            else return null;
        }
        if (vertical == null) return null;
        return horizontal == null ? vertical : $"{vertical} {horizontal}";
    }

    /// <summary><c>flex-flow</c> (CSS Flexbox 1 §4): at most one direction and at most one wrap,
    /// and nothing else. Measured: 'wrap reverse' is refused — 'reverse' is half of a word, not a
    /// word — while 'row wrap', 'column nowrap' and a lone 'wrap' all stand.</summary>
    private static bool IsFlexFlowValue(string text)
    {
        bool direction = false, wrap = false;
        foreach (var word in SplitComponents(text))
        {
            var p = word.ToLowerInvariant();
            if (p is "row" or "row-reverse" or "column" or "column-reverse")
            { if (direction) return false; direction = true; }
            else if (p is "nowrap" or "wrap" or "wrap-reverse")
            { if (wrap) return false; wrap = true; }
            else return false;
        }
        return direction || wrap;
    }

    /// <summary>The grammar of the <c>text-decoration</c> shorthand, measured value by value against
    /// the reference engine. The four lines that combine (<c>underline</c>, <c>overline</c>,
    /// <c>line-through</c>, <c>blink</c>) may each appear once — a repeat drops the declaration
    /// (<c>'underline underline'</c>, <c>'blink blink'</c>) — and the three that stand alone
    /// (<c>none</c>, <c>spelling-error</c>, <c>grammar-error</c>) appear with nothing else of their
    /// kind (<c>'underline none'</c>, <c>'underline spelling-error'</c> and
    /// <c>'grammar-error spelling-error'</c> are all refused) though not beside a style, a thickness
    /// or a colour (<c>'none solid'</c> and <c>'spelling-error red'</c> are both kept). One style, one
    /// thickness, one colour: <c>'underline wavy dotted'</c>, <c>'underline 3px 4px'</c>,
    /// <c>'underline auto 2px'</c> and <c>'red red'</c> each leave the property untouched, and so does
    /// a word that is none of these — <c>'underline thick'</c> is refused because <c>thick</c> is a
    /// border width, not a text-decoration thickness.</summary>
    private static bool IsTextDecorationShorthand(string text)
    {
        var lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool alone = false, nonLineSeen = false;
        int styles = 0, thicknesses = 0, colors = 0;
        foreach (var token in SplitComponents(text))
        {
            var p = token.ToLowerInvariant();
            switch (p)
            {
                case "underline" or "overline" or "line-through" or "blink":
                    // The line list is one run: a line may follow a colour that led the value
                    // ('red underline' is kept) but not one that came after the run had already
                    // started and stopped ('underline red overline' is refused) — the parser
                    // leaves a list it has begun and does not come back to it (both measured).
                    if (alone || !lines.Add(p) || (nonLineSeen && lines.Count > 1)) return false;
                    break;
                case "none" or "spelling-error" or "grammar-error":
                    if (alone || lines.Count > 0) return false;
                    alone = true;
                    break;
                case "solid" or "double" or "dotted" or "dashed" or "wavy":
                    if (++styles > 1) return false;
                    nonLineSeen = true;
                    break;
                default:
                    if (p is "auto" or "from-font"
                        || (IsLengthOrPercentage(token) && !IsUnitlessNonZero(p))
                        || IsMathFunctionCall(token, IsLengthOrPercentage))
                    {
                        if (++thicknesses > 1) return false;
                    }
                    else if (ColorParser.IsColorToken(token))
                    {
                        if (++colors > 1) return false;
                    }
                    else return false;
                    nonLineSeen = true;
                    break;
            }
        }
        // A value that named nothing the grammar recognises is refused by the branch above, so what
        // reaches here always says something — but an empty text is not a declaration either.
        return lines.Count > 0 || alone || styles > 0 || thicknesses > 0 || colors > 0;
    }

    /// <summary>A number written with no unit. Only zero means a length without one, so this is what
    /// separates the <c>'underline 0'</c> the reference engine takes (and prints as <c>0px</c>) from
    /// the <c>'underline 2'</c> it refuses.</summary>
    private static bool IsUnitlessNonZero(string token) =>
        double.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value != 0;

    /// <summary>Every comma-separated layer of the value is an image: an address, a gradient, one
    /// of the image functions, or the word 'none'. A colour is not an image, two words in one layer
    /// are not one image either, and 'local()' was refused by the arm above (measured:
    /// 'background-image: red', '… : auto' and '… : none none' are each dropped).</summary>
    private static bool SaysImages(string text)
    {
        var layers = SplitTopLevel(text);
        if (layers.Count == 0) return false;
        foreach (var layer in layers)
        {
            var words = SplitComponents(layer);
            if (words.Count != 1) return false;
            var word = words[0];
            if (word.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            if (SaysAnImageNoEngineReads(word)) return false;
            if (!IsImageFunction(word)) return false;
        }
        return true;
    }

    /// <summary>CSS Lists 3 §3.3 read as one position, one image and one marker type at most.</summary>
    private static bool IsListStyleShorthand(string text)
    {
        var words = SplitComponents(text);
        if (words.Count == 0) return false;
        // The three slots are filled in the order the words arrive, and a word goes to the first
        // slot still open that can read it. That is how the reference engine reads the two shapes a
        // strict per-slot count refuses (measured: 'list-style: none none' is the image and the type
        // and reads 'outside none none', and 'list-style: inside outside' is the position and a type
        // named 'outside' that reads back 'inside none outside').
        bool position = false, image = false, type = false;
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant();
            if (!position && lower is "inside" or "outside") { position = true; continue; }
            if (!image && IsImageFunction(word)) { image = true; continue; }
            if (!image && lower == "none") { image = true; continue; }
            if (!type && (lower == "none" || IsListStyleTypeValue(word))) { type = true; continue; }
            return false;
        }
        return true;
    }

    private static bool IsListStyleTypeValue(string text)
    {
        var words = SplitComponents(text);
        if (words.Count != 1) return false;
        var one = words[0];
        if (one.Length >= 2 && (one[0] == '"' || one[0] == '\'') && one[^1] == one[0]) return true;
        if (one.Contains('(') || one.Contains('[')) return false;
        return IsIdentifierLike(one);
    }

    /// <summary><c>counter-reset</c> and <c>counter-set</c> (CSS Lists 3 §4.3, §4.4): either the
    /// single keyword <c>none</c>, or a list of <c>&lt;counter-name&gt; &lt;integer&gt;?</c> pairs.
    /// A name may not be followed by another name's keyword — <c>'c none'</c> is refused, as is a
    /// <c>normal</c> counter — and an integer that does not fit is no declaration either (all
    /// measured; <c>'c reverse'</c> on the other hand is two counters, because 'reverse' is a name
    /// like any other).</summary>
    /// <summary>Whether a token begins with the characters of a number — the only way a length
    /// token can start, and the test that keeps a keyword out of a numeric slot.</summary>
    private static bool IsAsciiNumericStart(string word) =>
        char.IsAsciiDigit(word[0]) || word[0] is '.' or '+' or '-';

    private static bool IsCounterList(string text)
    {
        // Space separates counters; the comma separates the lists of other properties and is no
        // separator here (measured: 'counter-reset: x, y' is dropped).
        if (text.Contains(',')) return false;
        var words = SplitComponents(text);
        if (words.Count == 0) return false;
        if (words.Count == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;
        int i = 0;
        bool any = false;
        while (i < words.Count)
        {
            var name = words[i];
            // A counter is named by an identifier, and a number names nothing
            // (measured: 'counter-reset: 1' is dropped).
            if (name.Equals("none", StringComparison.OrdinalIgnoreCase)
                || name.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || char.IsAsciiDigit(name[0])
                || !IsIdentifierLike(name)) return false;
            i++;
            if (i < words.Count)
            {
                // Only a number continues this counter; the next word starts a new one. The
                // integer is read as a whole number, and one too large for the property is clamped
                // rather than refused (measured: 'counter-reset: x 2147483648' is accepted and
                // reads back 'x 2147483647', while 'x 1.5' is no counter list at all).
                var next = words[i];
                bool saysCount = Acrux.Core.Css.ShorthandExpander.IsNumberToken(next)
                    && double.TryParse(next, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var count)
                    && count == Math.Floor(count);
                // An expression that works out to a whole number is the counter's integer too, and
                // the echo keeps the expression the page wrote (measured: 'counter-increment: foo
                // calc(1)' stands and computes 'foo 1').
                if (saysCount || IsMathFunctionCall(next, IsNumberOrPercentage)) i++;
            }
            any = true;
        }
        return any;
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
    /// <summary>A &lt;length&gt; list with a ceiling on how many of them the property takes; the
    /// count is part of the grammar, and a fifth value for a four-edge property is no
    /// declaration (measured on 'margin').</summary>
    private static bool IsLengthListUpTo(string text, int max, params string[] keywords) =>
        SplitComponents(text).Count <= max && IsLengthList(text, keywords);

    private static bool IsLengthList(string text, params string[] keywords)
    {
        foreach (var token in SplitComponents(text))
        {
            if (IsBoxLength(token)) continue;
            if (IsOneOf(token, keywords)) continue;
            // A math function is the one parenthesis this grammar can decide: its arguments are
            // numbers, lengths and percentages whatever the property, so 'calc(bogus)' is no
            // length while 'calc(1px + 2px)' is (measured, both, through 'supports(width: …)').
            if (IsMathFunctionCall(token, IsLengthOrPercentage)) continue;
            if (IsOtherFunctionCall(token)) continue;
            return false;
        }
        return true;
    }

    /// <summary>One of the two scroll insets (CSS Scroll Snap 1 §6.1): up to <paramref
    /// name="max"/> sides, each a length — and for 'scroll-padding' also a percentage or the word
    /// 'auto', which the margin does not take.</summary>
    private static bool IsScrollInset(string text, int max, bool padding)
    {
        var parts = SplitComponents(text);
        if (parts.Count == 0 || parts.Count > max) return false;
        foreach (var part in parts)
        {
            if (padding && part.Equals("auto", StringComparison.OrdinalIgnoreCase)) continue;
            bool saysPercentage = part.Contains('%', StringComparison.Ordinal);
            if (saysPercentage && !padding) return false;
            if (!IsBoxLength(part)) return false;
        }
        return true;
    }

    /// <summary>A property whose grammar is ONE &lt;length-percentage&gt; or one keyword: a size, a
    /// minimum, a maximum. Two of them side by side are not a value the reference engine keeps
    /// (measured: 'width: auto 30px' and 'max-width: none none' both read back as nothing), even
    /// though the same two words written as the two axes of 'contain-intrinsic-size' are.</summary>
    private static bool IsSingleLength(string text, params string[] keywords)
    {
        var parts = SplitComponents(text);
        return parts.Count == 1 && !IsFitContentFunction(parts[0]) && IsLengthList(parts[0], keywords);
    }

    /// <summary>A length list whose terms are never negative. A size, a padding and a font size are
    /// the three properties the reference engine refuses a negative literal on — it drops the
    /// declaration instead of clamping it (measured: 'width: -10px', 'padding-left: -10px' and
    /// 'font-size: -10px' each fall while 'margin-left: -10px' and 'top: -10px' stand). A negative
    /// written inside a math function is not one of them: 'width: calc(-10px)' is a size the engine
    /// keeps and lays out as nothing wide (measured), because the sign is only known once the
    /// function is worked out.</summary>
    private static bool IsNonNegativeLengthList(string text, params string[] keywords) =>
        IsLengthList(text, keywords) && !AnyNegativeTerm(text);

    private static bool IsNonNegativeSingleLength(string text, params string[] keywords) =>
        IsSingleLength(text, keywords) && !AnyNegativeTerm(text);

    private static bool AnyNegativeTerm(string text) =>
        SplitComponents(text).Any(IsNegativeValue);

    /// <summary>CSS Sizing 4 §4: the bare <c>fit-content</c> keyword is a size, the functional
    /// <c>fit-content()</c> is not one — 'width: fit-content(200px)', 'min-height: fit-content(10px)'
    /// and 'flex-basis: fit-content(10px)' are each dropped (measured) while a grid track list keeps
    /// the very same function (CSS Grid 1 §7.2.3, measured: 'grid-auto-rows: fit-content(10px) 20px'
    /// stands). Other functions a size does take: 'attr(x px)' and 'min(10px, 20px)' both stand
    /// (measured), so this is a refusal of one name and not of parentheses in general.</summary>
    /// <summary>Whether a one-word value says a negative number — the range test 'line-height' uses
    /// (measured: '-1px' and '-1' are both refused, '-0px' is not negative and stands).</summary>
    private static bool IsNegativeValue(string text) =>
        Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(
            text.Trim(), out var number, out _) && number < 0;

    private static bool IsFitContentFunction(string token) =>
        token.StartsWith("fit-content(", StringComparison.OrdinalIgnoreCase);

    /// <summary>'font-synthesis: none | [ weight || style || small-caps ]' (CSS Fonts 4 §9.3):
    /// 'none' stands alone, otherwise each kind appears at most once. A word that is not one of the
    /// three — 'auto', 'position', a duplicate — makes the whole declaration invalid.</summary>
    private static bool IsFontSynthesisValue(string text)
    {
        var parts = SplitComponents(text);
        if (parts.Count == 0) return false;
        if (parts.Count == 1 && IsOneOf(parts[0], "none")) return true;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts)
        {
            if (!IsOneOf(p, "weight", "style", "small-caps")) return false;
            if (!seen.Add(p.ToLowerInvariant())) return false;
        }
        return true;
    }

    /// <summary>'column-count: auto | &lt;integer [1, ∞]&gt;' (CSS Multi-column 1 §3.3): a bare zero is
    /// not a count, and neither is a length.</summary>
    private static bool IsColumnCountValue(string text)
    {
        var parts = SplitComponents(text);
        if (parts.Count != 1) return false;
        if (IsOneOf(parts[0], "auto")) return true;
        return int.TryParse(parts[0], out var n) && n >= 1;
    }

    /// <summary>'columns: &lt;column-width&gt; || &lt;column-count&gt;'. A length or percentage (and a
    /// lone '0', which is a zero length) is the width; a bare positive integer is the count; 'auto'
    /// is either. A second width, a second count, or a count below one drops the declaration
    /// (measured: 'columns: 0' is a width, 'columns: 0 0' is refused).</summary>
    private static bool IsColumnsValue(string text)
    {
        bool hasWidth = false, hasCount = false;
        foreach (var token in SplitComponents(text))
        {
            if (IsOneOf(token, "auto")) continue;
            // Count first: a bare positive integer is the count, and only a value that is not one is
            // a width. A lone '0' is not a count (a count starts at one) so it falls through to the
            // width, where it is a zero length; '0 0' is then a second width and is refused.
            if (int.TryParse(token, out var n) && n >= 1)
            {
                if (hasCount) return false;
                hasCount = true;
                continue;
            }
            if (IsLengthOrPercentage(token) || IsMathFunctionCall(token, IsLengthOrPercentage))
            {
                if (hasWidth) return false;
                hasWidth = true;
                continue;
            }
            return false;
        }
        return true;
    }

    /// <summary>The colour grammar of the properties that take &lt;color&gt;#: a token the colour
    /// parser recognises, or one of the keywords that stand for a colour the element has to
    /// work out for itself. 'invert' is deliberately absent: Color 4 §14.1 defines it only for
    /// forced-colours mode, and a reference engine rejects the whole declaration outside it
    /// (measured: 'outline: 3px solid invert' leaves outline-style at its initial 'none').</summary>
    /// <summary>One colour, or the two that address the pair of edges a logical property
    /// measures (CSS Logical 1 §4, measured on 'border-block-color').</summary>
    private static bool IsTwoColors(string text)
    {
        // The pair is two colours divided by a space; a comma at the top of the value divides
        // layers, and these properties have one pair between their two edges and no list at all —
        // while the comma inside 'rgb(255, 255, 255)' is the colour's own and stays (measured:
        // 'border-block-color: red, blue' is dropped, 'border-block-color: #fff' is not).
        if (SplitTopLevel(text, ',').Count > 1) return false;
        // Split at the spaces outside every parenthesis: a component colour may be an 'rgb()' with
        // its own commas inside, and a splitter that read those would break the colour in two
        // (measured: 'border-block-color: #fff' echoes as 'rgb(255, 255, 255)' and stands).
        var words = SplitOutsideParentheses(text);
        return words.Count is 1 or 2 && words.All(word => ColorParser.IsColorToken(word));
    }

    /// <summary>One &lt;line-width&gt; — a non-negative length or the word 'thin', 'medium',
    /// 'thick' — up to <paramref name="max"/> of them. A percentage is not a width and a negative
    /// length is not one either (measured: 'border-top-width: 50%' and '… : -1px' both fall).</summary>
    private static bool IsLineWidthList(string text, int max)
    {
        if (text.IndexOf(',') >= 0) return false;
        var words = SplitComponents(text);
        if (words.Count == 0 || words.Count > max) return false;
        foreach (var word in words)
        {
            if (IsOneOf(word, "thin", "medium", "thick")) continue;
            if (IsMathFunctionCall(word, IsLengthOrPercentage)) continue;
            // A width is a length: 'auto' is a style word and no width, and a bare ident is not a
            // length either (measured: 'border-width: auto' is dropped).
            if (word.Length == 0 || !IsAsciiNumericStart(word)) return false;
            if (!IsNonNegativeBoxLength(word) || IsPercentage(word)) return false;
        }
        return true;
    }

    private static bool IsColorList(string text)
    {
        foreach (var token in SplitComponents(text))
        {
            if (ColorParser.IsColorToken(token)) continue;
            if (token.Equals("auto", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsMathFunctionCall(token, IsLengthOrPercentage) || IsOtherFunctionCall(token)) continue;
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

    private static bool IsOneOf(string token, params string[] keywords)
    {
        foreach (var keyword in keywords)
            if (token.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>A &lt;length&gt; or &lt;percentage&gt; in the sign/number/unit shape CSS Syntax
    /// gives them. The unit is not looked up in a table: a unit name this engine has not heard
    /// of yet is still a unit, and refusing it would lose a working declaration.</summary>
    /// <summary>Whether a token is a length, a percentage or the math expression that stands for
    /// one — the operand type every offset property takes (measured: 'animation-range-start' and
    /// 'offset-position: calc(1px)' both keep a calc the plain length reader would refuse).</summary>
    public static bool IsLengthPercentageOrMath(string token) =>
        IsMathFunctionCall(token, IsLengthOrPercentage) || IsLengthOrPercentage(token);

    internal static bool IsLengthOrPercentage(string token)
    {
        int i = 0;
        if (i < token.Length && (token[i] == '+' || token[i] == '-')) i++;
        int digits = i;
        // A point belongs to the number only when a digit follows it. CSS Syntax 3 §4.3.12 lets a
        // token run to the period, but no reference engine reads a value there: 'margin-left:
        // 123456789.px', 'width: 12.' and 'line-height: 12.' are each dropped while '123456789px',
        // '12px' and '12' stand (measured), and the same holds inside a unit — 'rotate: 12.deg'
        // falls with it.
        while (i < token.Length && (char.IsAsciiDigit(token[i])
                || (token[i] == '.' && i + 1 < token.Length && char.IsAsciiDigit(token[i + 1])))) i++;
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
