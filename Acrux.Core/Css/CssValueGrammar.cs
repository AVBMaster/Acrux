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
        return ValueTextIsValid(name.ToCssString(), value.CssText());
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
        return ValueTextIsValid(propertyName, value);
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
        // The properties this engine echoes without consuming are read by one table, and that table
        // is the grammar too: a value whose canonical text does not exist is not a declaration on
        // any surface (measured value by value — 'fill-rule: bogus', 'mask-type: alpha luminance',
        // 'container-name: a, b', 'animation-timeline: t' and the rest).
        if (Acrux.Core.Css.Resolver.EchoedProperties.Find(propertyName) is { } echo)
            return TextIsValid(value, text => echo.Normalize(text) != null);
        // A '-webkit-' spelling that is a property of its own has its twin's grammar: the value
        // set is the same, and only the name the declaration is stored under differs.
        var id = CssPropertyIdExtensions.BehaviourId(CssPropertyIdExtensions.FromString(propertyName));
        var grammar = id != CssPropertyId.Invalid ? GrammarFor(id) : LegacyPrefixedGrammar(propertyName);
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
        "-webkit-text-stroke-width" => static t => IsPlainLength(t.Trim()),
        // Width first, colour second, either one optional and never two of a kind (measured:
        // 'green 2px' is stored reordered as '2px green', '2px 2px' and 'red green' are dropped).
        "-webkit-text-stroke" => static t =>
        {
            var parts = SplitComponents(t).ToList();
            if (parts.Count is not (1 or 2)) return false;
            if (parts.Count == 1) return IsPlainLength(parts[0]) || IsLegacyColor(parts[0]);
            return (IsPlainLength(parts[0]) && IsLegacyColor(parts[1]))
                || (IsLegacyColor(parts[0]) && IsPlainLength(parts[1]));
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
        "cross-fade", "image-set", "element", "paint",
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
            new[] { "normal", "break-all", "keep-all", "break-word" }),
        CssPropertyId.TextWrap => IsTextWrapValue,
        // CSS Text 3 §3.4 as this engine reads it: a number or a length, and nothing else — neither
        // 'normal' nor 'auto' is a value of 'tab-size' here (all three measured).
        CssPropertyId.TabSize => static text => IsLengthList(text) && !SplitComponents(text).Any(
            static w => w.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || w.Equals("auto", StringComparison.OrdinalIgnoreCase)),
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
        CssPropertyId.CounterReset or CssPropertyId.CounterSet => IsCounterList,
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
            // The four-edge shorthands take one to four values and no more: a fifth is no
            // declaration at all (measured on 'margin:1px 1px 1px 1px 1px').
            => static text => IsLengthListUpTo(text, 4, "auto"),
        CssPropertyId.Padding or CssPropertyId.PaddingTop or CssPropertyId.PaddingRight
            or CssPropertyId.PaddingBottom or CssPropertyId.PaddingLeft
            or CssPropertyId.PaddingBlock or CssPropertyId.PaddingBlockStart or CssPropertyId.PaddingBlockEnd
            or CssPropertyId.PaddingInline or CssPropertyId.PaddingInlineStart or CssPropertyId.PaddingInlineEnd
            => static text => IsLengthListUpTo(text, 4),
        CssPropertyId.Inset or CssPropertyId.Top or CssPropertyId.Right
            or CssPropertyId.Bottom or CssPropertyId.Left
            or CssPropertyId.InsetBlock or CssPropertyId.InsetBlockStart or CssPropertyId.InsetBlockEnd
            or CssPropertyId.InsetInline or CssPropertyId.InsetInlineStart or CssPropertyId.InsetInlineEnd
            => static text => IsLengthListUpTo(text, 4, "auto"),
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
            or CssPropertyId.Background
            => static text => !NamesAFontLocal(text),
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
        CssPropertyId.Opacity => static text => IsNumberList(text, "initial", "inherit"),
        CssPropertyId.ZIndex => static text => IsOneOf(text, "auto") || IsIntegerList(text),
        CssPropertyId.LineHeight
            => static text => IsLengthList(text, "normal") || IsNumberOnly(text),
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
            or CssPropertyId.BorderBlockStart or CssPropertyId.BorderBlockEnd
            or CssPropertyId.BorderInlineStart or CssPropertyId.BorderInlineEnd
            or CssPropertyId.BorderBlock or CssPropertyId.BorderInline
            => IsBorderSideValue,
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
        CssPropertyId.ClipPath => IsClipPathValue,
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
        // CSS UI 4 §3.3: the outline's styles are the border's plus 'auto'.
        CssPropertyId.OutlineStyle => static text => IsSingleKeywordWord(text,
            new[] { "auto", "none", "hidden", "dotted", "dashed", "solid", "double", "groove",
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
        CssPropertyId.ShapeRendering
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.IsOneOfLower(
                text, "auto", "optimizeSpeed", "crispEdges", "geometricPrecision"),
        CssPropertyId.ColorRendering
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.IsOneOfLower(
                text, "auto", "optimizeSpeed", "optimizeQuality"),
        CssPropertyId.ColorInterpolation or CssPropertyId.ColorInterpolationFilters
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.IsOneOfLower(
                text, "auto", "sRGB", "linearRGB"),
        CssPropertyId.ImageOrientation
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.IsOneOfLower(
                text, "from-image", "none") || Acrux.Core.Css.Resolver.CssPropertyApplier.IsImageOrientationAngle(text),
        CssPropertyId.TextSecurity
            => static text => Acrux.Core.Css.Resolver.CssPropertyApplier.IsOneOfLower(
                text, "none", "disc", "circle", "square"),
        // The clamp takes one integer or 'none'; the two-value form a draft proposed is no value
        // here either (measured: '-webkit-line-clamp: 2 1.5em' reads back 'none').
        CssPropertyId.LineClamp or CssPropertyId.WebkitLineClamp
            => static text => text.Equals("none", StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(text.Trim(), out var lines) && lines >= 0),
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
        CssPropertyId.PageBreakBefore or CssPropertyId.PageBreakAfter => static text =>
            IsSingleKeywordWord(text, new[] { "auto", "always", "avoid", "left", "right" }),
        CssPropertyId.PageBreakInside => static text => IsSingleKeywordWord(text,
            new[] { "auto", "avoid" }),
        _ => null,
    };

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
    private static bool IsClipPathValue(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        var tokens = SplitComponents(trimmed);
        if (tokens.Count == 0) return false;
        int index = 0;
        if (tokens.Count > 1 && GeometryBoxWords.Contains(tokens[0].ToLowerInvariant())) index = 1;
        if (index >= tokens.Count) return false;
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

    private static bool IsBorderSideValue(string text)
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
        return width <= 4 && style <= 1 && colour <= 1;
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
        var pieces = SplitOutsideParentheses(text);
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
            any |= i > frac;
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

    private static bool IsIdentifierLike(string text)
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

    /// <summary><c>list-style-type</c> is the keyword family of <c>@counter-style</c> plus a quoted
    /// string used as the marker; an unknown name is a custom counter style the sheet may declare,
    /// which the engine still echoes, while a function is not a type at all (measured:
    /// <c>'symbols("*")'</c> is dropped and <c>'"–"'</c> is kept).</summary>
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
    private static bool IsCounterList(string text)
    {
        var words = SplitComponents(text);
        if (words.Count == 0) return false;
        if (words.Count == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;
        int i = 0;
        bool any = false;
        while (i < words.Count)
        {
            var name = words[i];
            if (name.Equals("none", StringComparison.OrdinalIgnoreCase)
                || name.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || !IsIdentifierLike(name)) return false;
            i++;
            if (i < words.Count)
            {
                // Only a number continues this counter; the next word starts a new one.
                if (Acrux.Core.Css.ShorthandExpander.IsNumberToken(words[i])
                    && int.TryParse(words[i], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out _)) i++;
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
            if (IsLengthOrPercentage(token)) continue;
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
