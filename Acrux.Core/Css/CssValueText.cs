using System;
using System.Globalization;
using System.Text;

namespace Acrux.Core.Css;

/// <summary>
/// Re-serialises the text of a declaration value the way a reference engine prints a specified
/// value back out: the components are separated by exactly one space, a comma is never preceded
/// by whitespace and always followed by one, and a function keeps its parentheses tight
/// (<c>rgb( 7 ,7 ,7 )</c> is printed as <c>rgb(7, 7, 7)</c>).
/// <para>
/// The engine stores a declaration as the text its parser consumed, and that text is also what
/// the CSSOM hands back for <c>rule.style.color</c>, <c>removeProperty()</c> and
/// <c>rule.cssText</c>. Authoring a sheet without a space after the commas is the common case,
/// so a page that reads back a rule it just wrote sees a different string than the one the
/// platform prints, and a script that compares the two thinks the declaration was lost.
/// </para>
/// <para>
/// Only insignificant whitespace is touched, and nothing the property's type cannot carry. A
/// quoted string is copied verbatim, because inside it the spaces and commas are data, not
/// syntax; a <c>url()</c> keeps its own characters but gains the quotes a reference engine prints
/// an address with. A value that carries a <c>var()</c> reference is left exactly
/// as authored, since its tokens are somebody else's. A unitless <c>0</c> is the one character
/// a reference engine rewrites rather than merely re-spaced: a length parser turns it into a
/// length of zero pixels and the specified value then carries the unit, so
/// <c>margin: 0 auto</c> reads back as <c>0px auto</c> while <c>line-height: 0</c>,
/// <c>opacity: 0</c> and <c>flex: 0 1 auto</c> keep the bare number. Only the properties
/// <see cref="Resolver.CssPropertyTraits.TakesLength"/> names are converted, and only the
/// components at the top level — a <c>0</c> inside <c>translate(0, 0)</c> is that function's
/// own argument.
/// </para>
/// </summary>
public static class CssValueText
{
    public static string Canonicalize(string? value, string? property = null)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (value!.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0) return value;

        // The 'animation' shorthand prints all eight of its parts in their canonical order, written
        // or not, so the reorder happens before anything else is folded and the parts it puts in are
        // folded like any other text (measured: '3s 2s LINEAR BOGUS' reads back
        // '3s linear 2s 1 normal none running BOGUS').
        if (property != null && property.Equals("animation", StringComparison.OrdinalIgnoreCase))
            value = CanonicalAnimationShorthand(value);

        // CSS Images 4 §4.2: '-webkit-image-set()' is the same function under its legacy name, and
        // every surface prints the modern one (measured: 'mask-image: -webkit-image-set(url(a.png)
        // 1x)' reads back 'image-set(url("a.png") 1x)').
        if (value.IndexOf("-webkit-image-set(", StringComparison.OrdinalIgnoreCase) >= 0)
            value = System.Text.RegularExpressions.Regex.Replace(value,
                "-webkit-image-set\\(", "image-set(",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // '-webkit-mask-box-image-slice' prints its numbers ahead of its 'fill' flag, whichever end
        // of the list the author put the flag on: 'fill 3' reads back as '3 fill' (measured).
        if (property != null
            && property.Equals("-webkit-mask-box-image-slice", StringComparison.OrdinalIgnoreCase))
            value = MaskBoxImageShorthand.NormalizeSlice(value);

        // Whether a bare 0 in this value is a length. Known by the property, not by the text:
        // 'columns: 0' is a width and prints '0px', 'scale: 0' is a factor and prints '0'.
        bool lengths = property != null && Resolver.CssPropertyTraits.TakesLength(property);
        // The counter properties hold <integer>s, and an integer keeps the digits it was written
        // with: the six-significant-digit spelling below is a float's rule, and applied to
        // 'counter-reset: x 2147483648' it left '2.14748e+09' — a number whose own clamp could no
        // longer recover what the page meant (measured: the reference engine reads back
        // 'x 2147483647', the largest integer the property can hold).
        bool counters = property is "counter-reset" or "counter-increment" or "counter-set";
        // The box's own numbers are cut at seven decimals before the six-significant-digit print,
        // and only for that family: 'margin-left: 0.000123456789px' echoes as '0.0001234px' while
        // 'line-height: 0.000123456789px' echoes as '0.000123457px' (measured, both).
        bool cut = Acrux.Core.Dom.Animations.CssValueTokenizer.CutsAtSevenDecimals(property);

        var sb = new System.Text.StringBuilder(value.Length + 8);
        bool pendingSpace = false;
        bool afterSlash = false;
        int depth = 0;
        int i = 0;
        while (i < value.Length)
        {
            char c = value[i];
            if (IsWhitespace(c))
            {
                if (sb.Length > 0) pendingSpace = true;
                i++;
                continue;
            }
            if (c == ',')
            {
                sb.Append(", ");
                pendingSpace = false;
                afterSlash = false;
                i++;
                // The space the comma owns is already printed, so swallow whatever the author
                // put after it instead of adding a second one.
                while (i < value.Length && IsWhitespace(value[i])) i++;
                continue;
            }
            if (c == '/' && depth == 0)
            {
                // A top-level slash separates two components ('50%/25%', '12px/1.5', '1/3') and
                // a reference engine prints it with a space on each side, exactly as a comma is
                // printed with one. Inside a function the author's slash is left alone, because
                // there it can be part of a syntax the value parser reads as written.
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('/');
                pendingSpace = true;
                afterSlash = true;
                i++;
                while (i < value.Length && IsWhitespace(value[i])) i++;
                continue;
            }
            if (c is '(' or ')')
            {
                // A parenthesis belongs to the token before it, and its first argument to it.
                sb.Append(c);
                // But unlike '(' the closing one ends a component, so whatever follows needs a
                // separator of its own: 'translate(4px,5px)rotate(30deg)' is two functions, and
                // printing them glued leaves one token for the shorthand splitter to fail on.
                pendingSpace = c == ')';
                i++;
                if (c == '(') depth++;
                else if (depth > 0) depth--;
                continue;
            }
            if (c is '"' or '\'')
            {
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                i = CopyQuoted(value, i, sb);
                afterSlash = false;
                continue;
            }
            if (c == '#')
            {
                // A hex colour is one token: its digits are not a number and its letters are not a
                // unit, so letting the number branch read it would truncate '#00F' to '#0f'. The
                // colour fold below re-spells it as an rgb() when the property prints colours at all.
                int start = i;
                i++;
                while (i < value.Length && char.IsAsciiHexDigit(value[i])) i++;
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(value, start, i - start);
                afterSlash = false;
                continue;
            }
            if (IsIdentStart(c) && !(c is '-' or '+' && StartsNumber(value, i + 1)))
            {
                int start = i;
                while (i < value.Length && IsIdentPart(value[i])) i++;
                var word = value.Substring(start, i - start);
                if (word.Equals("attr", StringComparison.OrdinalIgnoreCase) && i < value.Length
                    && value[i] == '(' && property != null
                    && property.Equals("content", StringComparison.OrdinalIgnoreCase))
                {
                    // 'content' echoes an attr() exactly as the page wrote it — the name's own case
                    // and the spacing round the comma included — because its fallback is an
                    // arbitrary token run rather than a parsed argument list (measured:
                    // 'ATTR(data-x , "y")' reads back 'ATTR(data-x , "y")' while 'COUNTER(x)' reads
                    // 'counter(x)').
                    int attrEnd = MatchingParen(value, i);
                    if (attrEnd > i)
                    {
                        if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                        sb.Append(value, start, attrEnd + 1 - start);
                        pendingSpace = true;
                        afterSlash = false;
                        i = attrEnd + 1;
                        continue;
                    }
                }
                if (IsQuotedArgument(word) && i < value.Length && value[i] == '(')
                {
                    int end = MatchingParen(value, i);
                    if (end > i)
                    {
                        if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                        // The name of the function is a keyword, so it prints in the one spelling
                        // however the author cased it: 'URL( a.png )' is 'url("a.png")'.
                        sb.Append(word.ToLowerInvariant()).Append('(')
                          .Append(QuotedArgument(value, i + 1, end)).Append(')');
                        // The address is a component, so a component written straight after its
                        // closing parenthesis is separated from it in the printed form.
                        pendingSpace = true;
                        afterSlash = false;
                        i = end + 1;
                        continue;
                    }
                }
                // The space between the name and its '(' is the parenthesis branch's to drop;
                // the space in front of the name belongs to the previous component and has to
                // stay, or 'solid rgb(1, 2, 3)' glues into 'solidrgb(1, 2, 3)' and the shorthand
                // expander can no longer see the colour at all.
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(word);
                afterSlash = false;
                continue;
            }
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < value.Length && char.IsAsciiDigit(value[i + 1]))
                || ((c == '+' || c == '-') && StartsNumber(value, i + 1)))
            {
                // Read the whole number, unit included: '0px' is one token, and printing it as
                // '0' plus 'px' would leave the decision about the unit to nobody.
                int start = i;
                bool signed = c == '+' || c == '-';
                if (signed) i++;
                while (i < value.Length && (char.IsAsciiDigit(value[i]) || value[i] == '.')) i++;
                while (i < value.Length && (IsIdentPart(value[i]) || value[i] == '%')) i++;
                var token = value.Substring(start, i - start);
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                if (counters && IsIntegerDigits(token))
                {
                    sb.Append(token);
                    afterSlash = false;
                    continue;
                }
                // A sign with no space in front of it is not a number's: written straight after a
                // value it belongs to the sum the page meant to write, and re-spelling '+1' as '1'
                // here glued the two operands into 'calc(11)' — a value the property's grammar has no
                // reason to refuse. A declaration the page wrote without spaces around its operator
                // is no declaration (CSS Values 4 §10.2.1), so its characters stay as they came and
                // the grammar is left to drop it (measured: 'flex-grow: calc(1+1)' reads back empty).
                if (signed && sb.Length > 0 && !IsComponentSeparator(sb[^1]))
                {
                    sb.Append(c).Append(FoldNumberAndUnit(token[1..], cut));
                    afterSlash = false;
                    continue;
                }
                if (depth == 0 && !afterSlash && lengths && token == "0") token = "0px";
                // A unit is a keyword and CSS keywords are ASCII case-insensitive, so a reference
                // engine prints the one spelling of it: 'border-top: 1PX solid red' and
                // 'margin: 10Px auto' both read back with a lower-case unit (measured).
                sb.Append(FoldNumberAndUnit(token, cut));
                afterSlash = false;
                continue;
            }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
            i++;
            afterSlash = false;
        }
        var result = sb.ToString().TrimEnd();

        // Two properties print their value as something other than the components they were
        // written with, and both do it the same way in a style rule and in the descriptor of an
        // at-rule (measured for each).
        if (property != null)
        {
            if (property.Equals("font-family", StringComparison.OrdinalIgnoreCase))
                result = FoldFamilyNames(result);
            else if (property.Equals("list-style-type", StringComparison.OrdinalIgnoreCase))
                // A string marker is printed with double quotes whichever way the page wrote it
                // (measured: "'x'" reads '"x"'), and a predefined counter style is a keyword that
                // prints in its one spelling — 'DISC' reads 'disc' while a name that is not one of
                // them keeps the author's case ('HEX' reads 'HEX', both measured). Only one string
                // ever reaches here — two of them are no value, which the grammar has refused.
                result = MapLayers(FoldQuotedString(result),
                    layer => FoldKeywordsInLayer(layer, ListStyleTypeKeywords));
            else if (property.Equals("font-feature-settings", StringComparison.OrdinalIgnoreCase))
                result = ElideDefaultFeatureValues(result);
            else if (Acrux.Core.Css.CssValueGrammar.IsLegacyPrefixedName(property))
                result = LegacyCanonical(result, property.ToLowerInvariant());
            else
            {
                // A colour-valued property prints its colour in the form the reference engine
                // prints it, and a property whose grammar owns keywords prints those in their one
                // spelling while leaving a custom name alone. Both decisions are made by the
                // property and never by the shape of the word: 'animation-name: BOGUS' and
                // 'font-family: FOO' keep the author's case (measured) because no grammar of
                // theirs owns those words.
                result = FoldColorWords(result, property);
                result = CanonicalizeOpacityNumber(result, property);
                result = CanonicalizeBorderSpacing(result, property);
                result = FoldKeywordCase(result, property);
                if (property.Equals("font", StringComparison.OrdinalIgnoreCase))
                {
                    result = CanonicalizeFontShorthand(result);
                    result = FoldGenericFamilies(result);
                }
                // Three properties print a value the page did not literally write: the indent flags
                // move behind the length, a ratio always says both sides, and a counter always says
                // its integer. All three are decided here so a sheet, the CSSOM echo and
                // getComputedStyle read the same text.
                var indent = CanonicalizeTextIndent(result, property);
                if (indent != null) result = indent;
                else if (property.Equals("aspect-ratio", StringComparison.OrdinalIgnoreCase))
                    result = CanonicalizeAspectRatio(result) ?? result;
                else if (property.Equals("counter-reset", StringComparison.OrdinalIgnoreCase)
                    || property.Equals("counter-set", StringComparison.OrdinalIgnoreCase))
                    result = CanonicalizeCounterList(result, "0") ?? result;
                else if (property.Equals("counter-increment", StringComparison.OrdinalIgnoreCase))
                    result = CanonicalizeCounterList(result, "1") ?? result;
                // A printer that re-arranges a value has to know when the value is not a value at
                // all. 'text-decoration: underline red overline' is a line list broken by a colour,
                // which the reference engine drops; sorting it into 'underline overline red' made
                // the sheet's own grammar see a declaration it takes, and the element painted two
                // lines (measured, both). The same holds for every pass below: each one says a
                // value the property accepted, and none of them is allowed to earn a refused value
                // the right to be applied.
                if (!Acrux.Core.Css.CssValueGrammar.ValueTextIsValid(property, result))
                {
                    // Nothing re-spelled: the text goes to the cascade as the page wrote it, where
                    // the property's grammar is asked and the declaration is dropped.
                }
                else
                {
                    result = PutColorFirst(result, property);
                    result = CanonicalizeTransition(result, property);
                    result = OmitInitialBorderParts(result, property);
                    result = CollapseBoxShorthandParts(result, property);
                    result = CanonicalizeFlexFlow(result, property);
                    result = CanonicalizeFlexShorthand(result, property);
                    result = CanonicalizeTextWrap(result, property);
                    result = CanonicalizeSchemeList(result, property);
                    result = CanonicalizeEmphasisPosition(result, property);
                    result = CanonicalizeEmphasisStyle(result, property);
                    result = CanonicalizeClipMargin(result, property);
                    result = CanonicalizeScrollInset(result, property);
                    result = CanonicalizeBorderImage(result, property);
                    result = CanonicalizeB284(result, property);
                    result = CanonicalizeB294(result, property);
                    result = CanonicalizeTextDecoration(result, property);
                    result = CanonicalizeColumnRule(result, property);
                    result = CanonicalizeListStyle(result, property);
                    result = CanonicalizeOffsetShorthand(result, property);
                    result = CanonicalizeGridArea(result, property);
                    result = CanonicalizeFontSynthesis(result, property);
                    result = CanonicalizeColumns(result, property);
                    result = CanonicalizeTextUnderlinePosition(result, property);
                    result = CanonicalizeContent(result, property);
                    result = CanonicalizeGridAutoFlow(result, property);
                    result = CanonicalizeMaskPosition(result, property);
                    result = CanonicalizePositionPair(result, property);
                    result = CanonicalizeContain(result, property);
                    result = CanonicalizeContainIntrinsic(result, property);
                    result = CanonicalizeContainerShorthand(result, property);
                    result = CanonicalizeFilterList(result, property);
                    result = CanonicalizeBackgroundShorthand(result, property);
                    result = CanonicalizePaintOrder(result, property);
                    result = CanonicalizeClipPath(result, property);
                    // The properties that only have to be said correctly — scroll snapping, the SVG
                    // keyword family, the rest of the font-variant group — print what their one table
                    // says, so a sheet and a CSSOM write cannot disagree about the order of a keyword
                    // list the engine re-sorts ('font-variant-east-asian: full-width jis04').
                    if (Acrux.Core.Css.Resolver.EchoedProperties.Normalize(property, result) is { } echoed)
                        result = echoed;
                }
            }
        }
        // Last of all, because it reads the components the passes above have just put in their
        // places and prints each math function among them as the single expression the engine
        // evaluates: 'inset(calc(10px + 2px))' and 'calc(10px + 2px) auto' both come out folded.
        return CssMathValue.Fold(result);
    }

    /// <summary>The value as the reference engine prints one of its legacy <c>-webkit-</c>
    /// properties: the keyword in its own lower-case spelling, the width of a stroke ahead of its
    /// colour, the offset of a reflection written even when the page left it out, and the leading
    /// plus of a group number dropped (all measured, snapshots/out/_b257_edge_legacy_grammar.txt).
    /// The words that are not keywords of that property keep their spelling, because a custom
    /// name or a string inside a url() is not the engine's to re-spell.</summary>
    private static string LegacyCanonical(string text, string property)
    {
        var words = System.Text.RegularExpressions.Regex.Split(text.Trim(), "\\s+");
        switch (property)
        {
            case "-webkit-box-ordinal-group":
                return words.Length == 1 && words[0].StartsWith("+", StringComparison.Ordinal)
                    ? words[0][1..] : text;
            case "-webkit-box-reflect" when words.Length is 1 or 2
                && System.Array.BinarySearch(SideKeywords, words[0].ToLowerInvariant(),
                    StringComparer.Ordinal) >= 0:
                return words.Length == 1 ? words[0].ToLowerInvariant() + " 0px"
                    : words[0].ToLowerInvariant() + " " + words[1];
            case "-webkit-text-stroke" when words.Length == 2:
            {
                // The width is printed first whichever order the page wrote (measured: 'green 2px'
                // reads back '2px green'), and a value that is not a two-word stroke never reaches
                // here because the grammar has already refused it.
                bool firstIsLength = char.IsDigit(words[0][0]) || words[0][0] is '+' or '-'
                    // 'thin', 'medium' and 'thick' are widths here too, and the pair prints with
                    // the width first whichever order the page wrote them in (measured:
                    // '-webkit-text-stroke: thin red' reads back as it was written).
                    || System.Array.BinarySearch(StrokeWidthOrder, words[0].ToLowerInvariant(),
                        StringComparer.Ordinal) >= 0;
                return firstIsLength ? words[0] + " " + words[1] : words[1] + " " + words[0];
            }
        }
        // One word, or a number with one keyword beside it: every keyword of these properties is
        // printed in lower case (measured 'END' and '3 FILL').
        var lowered = new string[words.Length];
        for (int i = 0; i < words.Length; i++)
            lowered[i] = IsLegacyKeywordWord(property, words[i])
                ? words[i].ToLowerInvariant() : words[i];
        return string.Join(" ", lowered);
    }

    private static readonly string[] SideKeywords = { "above", "below", "left", "right" };

    /// <summary>The three border-width keywords a text stroke takes in place of a length, sorted for
    /// a binary search like <see cref="SideKeywords"/>.</summary>
    private static readonly string[] StrokeWidthOrder = { "medium", "thick", "thin" };

    /// <summary>Whether a word of a legacy value is one of that property's own keywords, which is
    /// the only kind of word the engine may re-spell.</summary>
    private static bool IsLegacyKeywordWord(string property, string word)
    {
        if (word.Length == 0 || !(char.IsLetter(word[0]) || word[0] == '-')) return false;
        if (word.IndexOf('(') >= 0) return false;   // a function keeps its own spelling
        return Acrux.Core.Css.CssValueGrammar.LegacyKeywordBelongs(property, word.ToLowerInvariant());
    }

    /// <summary>
    /// Print a shadow or an outline with its colour in front, which is the order a reference
    /// engine serialises those shorthands in however the page wrote them (measured:
    /// <c>outline: 2px dashed rgb(1, 2, 3)</c> reads back <c>rgb(1, 2, 3) dashed 2px</c>, and
    /// <c>box-shadow: 0 0 2px #F00</c> reads <c>rgb(255, 0, 0) 0px 0px 2px</c>). A border side is
    /// not one of them — the reference engine keeps <c>1px solid red</c> in the order it was
    /// written (measured) — and a per-layer list re-orders each layer on its own.
    /// </summary>
    private static string PutColorFirst(string text, string property)
    {
        var name = property.ToLowerInvariant();
        if (name != "outline" && name != "box-shadow" && name != "text-shadow") return text;
        return MapLayers(text, layer => PutColorFirstInLayer(layer, name));
    }

    private static string PutColorFirstInLayer(string layer, string name)
    {
        var words = SplitTopLevelWords(layer);
        // 'currentcolor' names no colour yet, so it is not a colour the fold re-spells — but it IS
        // the colour component, and the reference engine moves it to the front with the rest
        // (measured: 'outline: medium solid currentcolor' reads 'currentcolor solid medium').
        int colour = words.FindIndex(w => IsColorWord(w)
            || w.Equals("currentcolor", StringComparison.OrdinalIgnoreCase));
        var head = colour >= 0 ? new List<string> { words[colour] } : new List<string>();
        var rest = new List<string>();
        for (int i = 0; i < words.Count; i++)
            if (i != colour) rest.Add(words[i]);
        if (name == "outline")
        {
            // The reference engine prints an outline as colour, style, width — the style is a
            // keyword of its own set and the width is whatever is left (measured:
            // 'outline: 2px dashed rgb(1, 2, 3)' reads back in that order). 'auto' is one of the
            // outline's style words and not a width, so it moves with them (measured:
            // 'outline: 2px auto' reads 'auto 2px').
            var style = rest.FirstOrDefault(w =>
                Acrux.Core.Css.ShorthandExpander.IsBorderStyle(w)
                || w.Equals("auto", StringComparison.OrdinalIgnoreCase));
            if (style != null)
            {
                rest.Remove(style);
                head.Add(style);
            }
        }
        else
        {
            // A shadow prints its 'inset' flag last, whatever order the page wrote it in
            // (measured: 'box-shadow: inset 0 0 2px red' reads 'red 0px 0px 2px inset').
            var flags = rest.Where(w => w.Equals("inset", StringComparison.OrdinalIgnoreCase)).ToList();
            if (flags.Count > 0) rest.RemoveAll(w => w.Equals("inset", StringComparison.OrdinalIgnoreCase));
            head.AddRange(rest);
            head.AddRange(flags);
            return string.Join(" ", head);
        }
        head.AddRange(rest);
        return string.Join(" ", head);
    }

    /// <summary>Whether a word is a colour this layer may move to the front: a hex literal, a
    /// plain colour function, or one of the colour names.</summary>
    private static bool IsColorWord(string word) =>
        ColorParser.IsColorToken(word)
        && (word.Length > 0 && word[0] == '#'
            || IsPlainColorFunction(word)
            || ColorParser.IsColorName(word)
            || word.Equals("transparent", StringComparison.OrdinalIgnoreCase));

    /// <summary>Drop the components of a border shorthand that repeat a longhand's initial value.
    /// The engine stores the longhands, and CSSOM 5.5.2 prints a shorthand without a component that
    /// says nothing (measured: 'border-top: medium solid currentcolor' reads 'solid',
    /// '1px none currentcolor' reads '1px', and a shorthand whose every component is initial reads
    /// as the empty string). Only the physical sides and 'border-inline' were measured this way;
    /// 'border-block-start' still prints what the page wrote, so it stays out of this list rather
    /// than being folded by analogy. Dropping an initial component cannot change what the expansion
    /// means, because that is what an initial value is.</summary>
    private static string OmitInitialBorderParts(string text, string property)
    {
        var name = property.ToLowerInvariant();
        if (name is not ("border" or "border-top" or "border-right" or "border-bottom" or "border-left"
            or "border-inline"))
            return text;
        var kept = new List<string>();
        foreach (var word in SplitTopLevelWords(text))
        {
            if (IsInitialBorderPart(word)) continue;
            kept.Add(word);
        }
        // When every component is initial there is nothing left to say, and the reference engine
        // answers the empty string — but it then writes the shorthand's longhands into cssText
        // rather than a declaration with no value. This engine has no longhand text to fall back
        // on at that point, and 'border: ;' would not survive being parsed again, so the whole
        // value stays written (measured, and left as a documented gap).
        return kept.Count == 0 ? text : string.Join(" ", kept);
    }

    /// <summary>One component of a border shorthand that repeats its longhand's initial value:
    /// 'medium' for the width, 'none' for the style, 'currentcolor' for the colour. The printer that
    /// drops initial components and the one that asks whether anything is left both read this, so
    /// the two cannot disagree about what an initial value is.</summary>
    private static bool IsInitialBorderPart(string word) =>
        word.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)
        || word.Equals("medium", StringComparison.OrdinalIgnoreCase)
        || word.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static bool IsBorderSideShorthand(string name) =>
        name is "border" or "border-top" or "border-right" or "border-bottom" or "border-left"
            or "border-inline" or "border-inline-start" or "border-inline-end"
            or "border-block" or "border-block-start" or "border-block-end";

    /// <summary>
    /// Whether a shorthand's value says nothing beyond the initial values of its longhands. The
    /// reference engine then prints the longhands in its place and the shorthand reads back as the
    /// empty string, while the style attribute keeps what the page wrote (measured:
    /// 'border: medium none currentcolor' gives 'border-width: medium; border-style: none;
    /// border-color: currentcolor; border-image: none;', and 'border-inline: medium none
    /// currentcolor' gives the two logical sides with their values unwritten).
    /// </summary>
    public static bool ShorthandPrintsEmpty(string property, string? value)
    {
        if (value == null || value.Length == 0) return false;
        var name = property.ToLowerInvariant();
        if (!IsBorderSideShorthand(name)) return false;
        var words = SplitTopLevelWords(value);
        return words.Count > 0 && words.TrueForAll(IsInitialBorderPart);
    }

    /// <summary>
    /// The declarations a shorthand prints when it has nothing left to say — its parts, each with
    /// the value it holds. Measured for the border family: 'border' gives its four sub-shorthands
    /// with their initials and a 'border-image: none' beside them, 'border-top' gives its three
    /// longhands, and 'border-inline' gives the two logical sides with the value the page wrote
    /// (a logical shorthand does not decompose any further on the way out). A shorthand this table
    /// does not know prints as itself, which is what it did before.
    /// </summary>
    public static List<KeyValuePair<string, string>>? EmptyShorthandParts(string property, string value)
    {
        var name = property.ToLowerInvariant();
        if (!IsBorderSideShorthand(name)) return null;
        if (name == "border")
            return new List<KeyValuePair<string, string>>
            {
                new("border-width", "medium"),
                new("border-style", "none"),
                new("border-color", "currentcolor"),
                new("border-image", "none"),
            };
        if (name is "border-inline" or "border-block")
            return new List<KeyValuePair<string, string>>
            {
                new(name + "-start", value),
                new(name + "-end", value),
            };
        return new List<KeyValuePair<string, string>>
        {
            new(name + "-width", "medium"),
            new(name + "-style", "none"),
            new(name + "-color", "currentcolor"),
        };
    }

    /// <summary>Collapse a four-value box shorthand to the shortest form that says the same thing,
    /// the way the reference engine prints the sides it repeats (measured: 'padding: 0px 0px' reads
    /// '0px'; the same rule gives '1px 2px' for a value with two distinct pairs and '1px 2px 3px'
    /// when only the left repeats the top). Two-value logical shorthands have nothing to repeat, so
    /// they are left alone.</summary>
    private static string CollapseBoxShorthandParts(string text, string property)
    {
        var name = property.ToLowerInvariant();
        if (name is not ("margin" or "padding" or "inset" or "border-width" or "border-style"
            or "border-color" or "scroll-margin" or "scroll-padding"))
            return text;
        var w = SplitTopLevelWords(text);
        bool Same(string x, string y) => x.Equals(y, StringComparison.OrdinalIgnoreCase);
        return w.Count switch
        {
            2 when Same(w[0], w[1]) => w[0],
            3 when Same(w[0], w[2]) && Same(w[0], w[1]) => w[0],
            4 when Same(w[0], w[1]) && Same(w[0], w[2]) && Same(w[0], w[3]) => w[0],
            4 when Same(w[0], w[2]) && Same(w[1], w[3]) && !Same(w[0], w[1]) => w[0] + " " + w[1],
            4 when Same(w[1], w[3]) && !Same(w[0], w[1]) && !Same(w[1], w[2])
                => w[0] + " " + w[1] + " " + w[2],
            _ => text,
        };
    }

    /// <summary>
    /// Print a <c>transition</c> the way the reference engine does: property, duration, timing
    /// function, delay, with the components that say nothing dropped — the initial property
    /// <c>all</c>, the initial easing <c>ease</c> and a zero delay are all left out (measured:
    /// <c>transition: all 1s</c> and <c>transition: 1s all</c> both read <c>1s</c>, and
    /// <c>transition: color 1s ease 2s</c> reads <c>color 1s 2s</c>). A layer that names no
    /// duration keeps its <c>all</c>, because then it is the only thing the declaration says
    /// (measured: <c>transition: ALL</c> reads <c>all</c>).
    /// </summary>
    private static string CanonicalizeTransition(string text, string property)
    {
        if (!property.Equals("transition", StringComparison.OrdinalIgnoreCase)) return text;
        var layers = new List<string>();
        foreach (var layer in SplitTopLevel(text, ','))
        {
            var words = SplitTopLevelWords(layer.Trim());
            if (words.Count == 0) { layers.Add(""); continue; }
            string? name = null, duration = null, delay = null, easing = null;
            foreach (var word in words)
            {
                if (Acrux.Core.Css.ShorthandExpander.IsTimeToken(word))
                {
                    if (duration == null) duration = word;
                    else if (delay == null) delay = word;
                    continue;
                }
                if (Acrux.Core.Css.ShorthandExpander.IsEasingToken(word))
                {
                    easing = word;
                    continue;
                }
                name ??= word;
            }
            var parts = new List<string>();
            if (name != null && !name.Equals("all", StringComparison.OrdinalIgnoreCase))
                parts.Add(name);
            // A duration of zero is the initial value and is left out like the rest — measured,
            // 'transition: none 0s linear 0s' reads back 'none linear' and 'transition: 0s' 'all'.
            if (duration != null && !IsZeroTime(duration)) parts.Add(duration);
            if (easing != null && !easing.Equals("ease", StringComparison.OrdinalIgnoreCase))
                parts.Add(easing);
            if (delay != null && !delay.Equals("0s", StringComparison.OrdinalIgnoreCase)
                && !delay.Equals("0ms", StringComparison.OrdinalIgnoreCase))
                parts.Add(delay);
            if (parts.Count == 0) parts.Add(name != null ? name.ToLowerInvariant() : "all");
            layers.Add(string.Join(" ", parts));
        }
        return string.Join(", ", layers);
    }

    private static bool IsZeroTime(string word) =>
        word.Equals("0s", StringComparison.OrdinalIgnoreCase)
        || word.Equals("0ms", StringComparison.OrdinalIgnoreCase)
        || word.Equals("0", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Print <c>flex-flow</c> as the reference engine does: the direction, then the wrap, with a
    /// part dropped when it says <c>row</c> or <c>nowrap</c> — the pair the <c>flex</c> shorthand
    /// carries by default, which is not the longhands' own initial pair (a plain <c>wrap</c> is
    /// printed although it is <c>flex-wrap</c>'s initial value, and a plain <c>nowrap</c> is
    /// dropped). When both parts are dropped the direction still stands alone, because the shorthand
    /// has nothing else to say (all twelve cases measured: 'row nowrap' and 'nowrap' read 'row',
    /// 'row wrap' and 'wrap' read 'wrap', 'column nowrap' reads 'column', 'column wrap' and
    /// 'row-reverse wrap-reverse' read as written).
    /// </summary>
    private static string CanonicalizeFlexFlow(string text, string property)
    {
        if (!property.Equals("flex-flow", StringComparison.OrdinalIgnoreCase)) return text;
        string direction = "row", wrap = null;
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            if (p is "row" or "row-reverse" or "column" or "column-reverse") direction = p;
            else if (p is "nowrap" or "wrap" or "wrap-reverse") wrap = p;
        }
        var parts = new List<string>();
        if (direction != "row") parts.Add(direction);
        if (wrap != null && wrap != "nowrap") parts.Add(wrap);
        return parts.Count == 0 ? "row" : string.Join(" ", parts);
    }

    /// <summary>Print <c>flex</c> as the three longhands it stands for, in the order the property
    /// lists them: the words of the shorthand are not part of its answer, so <c>flex: none</c> reads
    /// <c>0 0 auto</c> and <c>flex: 1</c> reads <c>1 1 0%</c> while <c>flex: 1 1 0</c> reads the
    /// <c>0px</c> its basis means (measured, and the same three parts the computed surface answers).
    /// A CSS-wide keyword is the value itself and keeps its spelling, and a value the reader cannot
    /// take apart is left exactly as the page wrote it — the grammar drops it, and a printer that
    /// invented parts for a refused shorthand would hand the cascade a declaration it never had.</summary>
    private static string CanonicalizeFlexShorthand(string text, string property)
    {
        if (!property.Equals("flex", StringComparison.OrdinalIgnoreCase)) return text;
        if (Resolver.CssPropertyTraits.IsCssWideKeyword(text.Trim())) return text;
        return ShorthandExpander.ParseFlexParts(text) is { } parts ? parts.Text : text;
    }

    /// <summary>Print <c>text-wrap</c> as the two longhands it says, with the initial ones left out
    /// (CSS Text Wrap 1 §3/§4, measured): 'wrap pretty' reads 'pretty', 'wrap stable' and
    /// 'stable wrap' read 'stable', 'wrap balance' reads 'balance', and a value that says nothing the
    /// page did not already have — 'auto', 'wrap', 'wrap auto' — still has to say its mode, so it
    /// reads 'wrap'. 'auto' is the other spelling of the initial mode, never a word of its own.</summary>
    private static string CanonicalizeTextWrap(string text, string property)
    {
        if (!property.Equals("text-wrap", StringComparison.OrdinalIgnoreCase)) return text;
        string mode = null, style = null;
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            if (p is "wrap" or "nowrap") { if (mode != null) return text; mode = p; }
            else if (p is "stable" or "balance" or "pretty") { if (style != null) return text; style = p; }
            else if (p != "auto") return text;
        }
        var parts = new List<string>();
        if (mode != null && mode != "wrap") parts.Add(mode);
        if (style != null) parts.Add(style);
        return parts.Count == 0 ? "wrap" : string.Join(" ", parts);
    }

    /// <summary>Say <c>color-scheme</c> in the one order and casing the reference engine prints, and
    /// say nothing for a value it would drop. The grammar owns the decision (both surfaces and the
    /// cascade read the same answer), so a sheet, a CSSOM write and getComputedStyle cannot disagree
    /// about where 'only' goes.</summary>
    private static string CanonicalizeSchemeList(string text, string property)
    {
        if (!property.Equals("color-scheme", StringComparison.OrdinalIgnoreCase)) return text;
        return Acrux.Core.Css.CssValueGrammar.CanonicalColorScheme(text) ?? text;
    }

    /// <summary>Say one of the scroll insets' shorthands the way the reference engine prints a
    /// box or pair whose sides all match (CSS Scroll Snap 1 §6.1, measured): 'scroll-margin-block:
    /// 2px 2px' reads '2px' and 'scroll-padding-inline: auto auto' reads 'auto', while a pair that
    /// differs keeps both halves in the order the page wrote them.</summary>
    private static string CanonicalizeScrollInset(string text, string property)
    {
        var lower = property.ToLowerInvariant();
        if (lower is not ("scroll-margin" or "scroll-padding" or "scroll-margin-block"
            or "scroll-margin-inline" or "scroll-padding-block" or "scroll-padding-inline"))
            return text;
        var parts = SplitTopLevelWords(text);
        if (parts.Count < 2) return text;
        return parts.All(part => part.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
            ? parts[0] : string.Join(" ", parts);
    }

    /// <summary>Say the properties this batch tightened in the one spelling the reference engine
    /// prints: a timing function with its default step term left out and its linear positions
    /// filled in, the legacy 'distribute' as the modern 'inter-character', and a pair of identical
    /// overscroll behaviours as the one word it is (all measured). The cascade and the computed
    /// surface read the same helpers, so a sheet, a style attribute and getComputedStyle agree.</summary>
    private static string CanonicalizeB284(string text, string property)
    {
        var lower = property.ToLowerInvariant();
        if (lower is "animation-timing-function" or "transition-timing-function"
            or "-webkit-animation-timing-function" or "-webkit-transition-timing-function")
            return Acrux.Core.Css.Resolver.CssPropertyApplier.CanonicalTimingFunction(text);
        if (lower == "text-justify"
            && text.Trim().Equals("distribute", StringComparison.OrdinalIgnoreCase))
            return "inter-character";
        if (lower is "overscroll-behavior" or "overscroll-behavior-block"
            or "overscroll-behavior-inline" or "overscroll-behavior-x" or "overscroll-behavior-y")
        {
            var parts = SplitTopLevelWords(text);
            if (parts.Count == 2 && parts[0].Equals(parts[1], StringComparison.OrdinalIgnoreCase))
                return parts[0].ToLowerInvariant();
        }
        if (lower is "align-items" or "align-content" or "justify-self" or "align-self")
        {
            var trimmed = string.Join(" ", SplitTopLevelWords(text));
            if (trimmed.Equals("first baseline", StringComparison.OrdinalIgnoreCase))
                return "baseline";
        }
        return text;
    }

    /// <summary>Say one of the border-image parts, and the shorthand, the way the reference engine
    /// prints them: a box part collapses to one value when all its sides agree, and the shorthand
    /// names every part the page left out (CSS Backgrounds 3 §5, measured). One canonicaliser
    /// serves the echo, the grammar and the cascade, so the three cannot disagree about a spelling.</summary>
    private static string CanonicalizeBorderImage(string text, string property)
    {
        var lower = property.ToLowerInvariant();
        var canonical = lower switch
        {
            "border-image-slice" => Acrux.Core.Css.CssValueGrammar.CanonicalBorderImageSlice(text),
            "border-image-width" => Acrux.Core.Css.CssValueGrammar.CanonicalBorderImageWidth(text),
            "border-image-outset" => Acrux.Core.Css.CssValueGrammar.CanonicalBorderImageOutset(text),
            "border-image-repeat" => Acrux.Core.Css.CssValueGrammar.CanonicalBorderImageRepeat(text),
            "border-image" => Acrux.Core.Css.CssValueGrammar.CanonicalBorderImageShorthand(text),
            _ => null,
        };
        return canonical ?? text;
    }

    /// <summary>Say <c>overflow-clip-margin</c> as one value: the box it measures from in front of
    /// the length, each part said only when it left its initial (CSS Overflow 3 §4.1, measured:
    /// '10px content-box' and 'content-box 10px' both read 'content-box 10px', a plain 'border-box'
    /// reads 'border-box', and 'padding-box' — the initial box, which the reference engine still
    /// takes — reads the length it means, '0px'). The computed surface prints the same shape from
    /// the same two parts, so the two surfaces cannot drift.</summary>
    private static string CanonicalizeClipMargin(string text, string property)
    {
        if (!property.Equals("overflow-clip-margin", StringComparison.OrdinalIgnoreCase)) return text;
        string box = null, length = null;
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            if (p is "content-box" or "border-box" or "padding-box") { if (box != null) return text; box = p; }
            else { if (length != null) return text; length = p; }
        }
        var parts = new List<string>();
        if (box != null && box != "padding-box") parts.Add(box);
        if (length != null && length != "0px") parts.Add(length);
        return parts.Count == 0 ? "0px" : string.Join(" ", parts);
    }

    /// <summary>Say <c>text-emphasis-position</c> with the vertical side first, whichever order the
    /// page used (measured: 'left over' reads 'over left').</summary>
    private static string CanonicalizeEmphasisPosition(string text, string property)
    {
        if (!property.Equals("text-emphasis-position", StringComparison.OrdinalIgnoreCase)) return text;
        return Acrux.Core.Css.CssValueGrammar.CanonicalTextEmphasisPosition(text) ?? text;
    }

    /// <summary>Say an emphasis mark with its fill in front of its shape and its colour behind both,
    /// whichever order the page wrote them: 'dot filled' reads 'filled dot', 'red dot' reads 'dot red'
    /// and 'red filled dot' reads 'filled dot red' (measured). The words the page left unspoken stay
    /// unspoken on this surface — 'filled' reads 'filled' while the computed value answers the
    /// 'dot' it means — and a mark the reader cannot take apart is left exactly as it was written.</summary>
    private static string CanonicalizeEmphasisStyle(string text, string property)
    {
        if (property.Equals("text-emphasis-style", StringComparison.OrdinalIgnoreCase))
            return ShorthandExpander.ParseEmphasisStyle(text) ?? text;
        if (property.Equals("text-emphasis", StringComparison.OrdinalIgnoreCase))
        {
            if (Resolver.CssPropertyTraits.IsCssWideKeyword(text.Trim())) return text;
            if (ShorthandExpander.ParseTextEmphasisParts(text) is not { } parts) return text;
            if (parts.Color == null) return parts.Style;
            // 'none' is what the style slot says when the page named only a colour, and the echo
            // does not print a word nobody wrote (measured: 'text-emphasis: red' reads 'red', while
            // the computed value of the same declaration says both parts).
            bool saysNone = ShorthandExpander.SplitShorthand(text)
                .Any(word => word.Equals("none", StringComparison.OrdinalIgnoreCase));
            return parts.Style.Equals("none", StringComparison.OrdinalIgnoreCase) && !saysNone
                ? parts.Color
                : parts.Style + " " + parts.Color;
        }
        return text;
    }

    /// <summary>
    /// Print <c>text-decoration</c>: the lines, then a thickness that is not the initial <c>auto</c>,
    /// then a style that is not the initial <c>solid</c>, then a colour that is not
    /// <c>currentcolor</c>; when every part is initial the shorthand says <c>none</c>, which is its
    /// line's initial (all measured: 'underline solid red' reads 'underline red', 'dashed' reads
    /// 'dashed', 'solid' reads 'none', 'overline 3px solid blue' reads 'overline 3px blue',
    /// 'line-through wavy green 4px' reads 'line-through 4px wavy green' — the thickness has its own
    /// place in the printed order, so it is neither dropped with the style nor read as the colour).
    /// </summary>
    private static string CanonicalizeTextDecoration(string text, string property)
    {
        if (!property.Equals("text-decoration", StringComparison.OrdinalIgnoreCase)) return text;
        var lines = new List<string>();
        string? style = null, color = null, thickness = null;
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            switch (p)
            {
                case "solid" or "double" or "dotted" or "dashed" or "wavy": style = p; break;
                case "underline" or "overline" or "line-through" or "blink" or "none"
                    or "spelling-error" or "grammar-error":
                    if (!lines.Contains(p)) lines.Add(p);
                    break;
                default:
                    // The width is a length, a percentage or one of two keywords; anything else that
                    // is not a line or a style is the colour.
                    if (thickness == null && ShorthandExpander.IsTextDecorationThickness(p))
                        thickness = p;
                    else color = word;
                    break;
            }
        }
        // The lines print in the engine's own order, not the order they were written in
        // (measured: 'text-decoration: blink underline' reads back 'underline blink').
        // A thickness written without a unit is zero — a non-zero number is no length and the
        // grammar has already refused the declaration — and the engine prints the unit it means
        // (measured: 'underline 0' and 'underline 0.0' both read 'underline 0px').
        if (thickness != null && IsBareZero(thickness)) thickness = "0px";
        var ordered = lines
            .Where(l => l != "none")
            .OrderBy(l => LineOrder(l))
            .ToList();
        var parts = new List<string>(ordered);
        if (thickness != null && thickness != "auto") parts.Add(thickness);
        if (style != null && style != "solid") parts.Add(style);
        if (color != null && !color.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
            parts.Add(color);
        if (parts.Count == 0) parts.Add("none");
        return string.Join(" ", parts);
    }

    /// <summary>The 'grid-area' shorthand as the engine prints it: the four lines joined by
    /// <c>" / "</c> with the trailing <c>auto</c>s dropped, and the <c>span</c> keyword in its one
    /// spelling (measured: '1 / auto' reads '1', 'auto / auto / 2' keeps its two leading autos,
    /// 'SPAN 2 / 3' reads 'span 2 / 3'). A line NAME keeps the author's case — it is a
    /// &lt;custom-ident&gt;, not a keyword.</summary>
    private static string CanonicalizeGridArea(string text, string property)
    {
        bool area = property.Equals("grid-area", StringComparison.OrdinalIgnoreCase);
        bool line = property.Equals("grid-row", StringComparison.OrdinalIgnoreCase)
            || property.Equals("grid-column", StringComparison.OrdinalIgnoreCase);
        if (!area && !line) return text;
        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > (area ? 4 : 2)) return text;
        for (int i = 0; i < parts.Length; i++)
        {
            var words = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return text;
            if (words[0].Equals("span", StringComparison.OrdinalIgnoreCase))
                words[0] = "span";
            else if (words[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
                words[0] = "auto";
            parts[i] = string.Join(" ", words);
        }
        int last = parts.Length - 1;
        while (last > 0 && parts[last].Equals("auto", StringComparison.Ordinal)) last--;
        return string.Join(" / ", parts, 0, last + 1);
    }

    /// <summary>'font-synthesis' prints its kinds in the engine's own order — weight, style,
    /// small-caps — whichever order the page listed them (measured: 'style weight' reads
    /// 'weight style', 'small-caps weight' reads 'weight small-caps'); 'none' stands alone.</summary>
    private static string CanonicalizeFontSynthesis(string text, string property)
    {
        if (!property.Equals("font-synthesis", StringComparison.OrdinalIgnoreCase)) return text;
        var words = SplitTopLevelWords(text);
        if (words.Count == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        bool weight = false, style = false, smallCaps = false;
        foreach (var word in words)
            switch (word.ToLowerInvariant())
            {
                case "weight": weight = true; break;
                case "style": style = true; break;
                case "small-caps": smallCaps = true; break;
                // A word that is not one of the three makes the declaration invalid; leave it for the
                // grammar/applier to refuse rather than reorder the value into a valid one.
                default: return text;
            }
        var parts = new List<string>(3);
        if (weight) parts.Add("weight");
        if (style) parts.Add("style");
        if (smallCaps) parts.Add("small-caps");
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    /// <summary>'contain' prints its keyword list in the order the reference engine uses and in
    /// its one spelling: 'layout inline-size' reads back 'inline-size layout', 'STYLE' reads
    /// 'style' (both measured). It does NOT collapse a complete set into 'content' or 'strict' —
    /// that shortcut belongs to the computed value alone (measured: 'style layout paint' reads
    /// back all three words and computes 'content'). A word that is nobody's containment keyword
    /// ('inherit', a function) leaves the text as written, and so does a list the grammar
    /// refuses: folding 'strict style' into a word of its own would say what the page never wrote.
    /// Idempotent, because the text it produces parses back to the same set.</summary>
    private static string CanonicalizeContain(string text, string property)
    {
        if (!property.Equals("contain", StringComparison.OrdinalIgnoreCase)) return text;
        if (text.IndexOf(',') >= 0) return text;
        var words = SplitTopLevelWords(text);
        if (words.Count == 0) return text;
        foreach (var word in words)
            if (System.Array.IndexOf(ContainWords, word.ToLowerInvariant()) < 0) return text;
        if (!Acrux.Core.Css.CssValueGrammar.TextIsValidFor("contain", text)) return text;
        if (words.Count == 1) return words[0].ToLowerInvariant();

        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var word in words) seen.Add(word.ToLowerInvariant());
        var parts = new System.Collections.Generic.List<string>(4);
        foreach (var name in ContainWords)
            if (name is not ("none" or "strict" or "content") && seen.Contains(name)) parts.Add(name);
        return parts.Count == 0 ? text : string.Join(' ', parts);
    }

    /// <summary>The 'contain-intrinsic' family prints its 'auto' flag in the one keyword
    /// spelling and drops an <c>auto</c> that no axis is waiting for: the flag belongs in front
    /// of the size it modifies, so '55px auto' keeps the size and loses the flag (measured: it
    /// reads back '55px' and computes '55px'), while 'AUTO 55px' reads 'auto 55px'. A text the
    /// property's grammar refuses is left exactly as written.</summary>
    private static string CanonicalizeContainIntrinsic(string text, string property)
    {
        if (!property.StartsWith("contain-intrinsic-", StringComparison.OrdinalIgnoreCase)) return text;
        if (!Acrux.Core.Css.CssValueGrammar.TextIsValidFor(property, text)) return text;
        var words = SplitTopLevelWords(text);
        if (words.Count == 0) return text;
        for (int i = 0; i < words.Count; i++)
            if (words[i].Equals("auto", StringComparison.OrdinalIgnoreCase)) words[i] = "auto";
        // 'auto' with nothing after it says nothing, and an 'auto' after the last size has no
        // size to introduce — the reference engine simply leaves it out.
        if (words.Count > 1 && words[^1] == "auto") words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    /// <summary>The 'container' shorthand keeps the page's own spelling — the words are names —
    /// except for the single word 'none', which is a keyword and so prints in its one spelling
    /// (measured: 'container: INLINE-SIZE Cards' reads back with its capitals while
    /// 'container: NONE' reads 'none').</summary>
    private static string CanonicalizeContainerShorthand(string text, string property)
    {
        if (!property.Equals("container", StringComparison.OrdinalIgnoreCase)) return text;
        var words = SplitTopLevelWords(text);
        return words.Count == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase)
            ? "none" : text;
    }

    /// <summary>'filter' and 'backdrop-filter' print a <c>drop-shadow()</c> with its colour in
    /// front of the offsets, whichever end of the list the page wrote it on (CSS Filter Effects 1
    /// §29.1, measured); the rest of the list is what it was.</summary>
    /// <summary>The value texts b294 measured one property at a time
    /// (snapshots/out/_b294_ref_probeE.txt and _b294_ref_probeF.txt). Each of these is a fact the
    /// reference engine states twice — in the CSSOM echo and in the text a sheet reads back — so
    /// all of them live in this one printer rather than in the applier that stores the value.</summary>
    private static string CanonicalizeB294(string text, string property)
    {
        var p = property.ToLowerInvariant();
        switch (p)
        {
            // CSS Masking 1 §9.2: 'mask-repeat' prints the two-word legacy pair as the one word it
            // means, and a pair of equal words as the single word (measured: 'repeat no-repeat'
            // reads 'repeat-x', 'no-repeat repeat' reads 'repeat-y', 'repeat repeat' and
            // 'no-repeat no-repeat' each read one word, while 'repeat space' — no legacy spelling
            // of anything — keeps both).
            case "mask-repeat":
                return MaskRepeatPair(text);

            // CSS Masking 1 §9.1: a mask size of two terms keeps the second only when it says
            // something the first did not (measured: '1px auto' reads '1px' and 'auto auto' reads
            // 'auto'), which is the same rule the background size prints by.
            case "mask-size":
                return MaskSizePair(text);

            // CSS Overflow 3 §3.1 with CSS Box Alignment 3 §6: the two axes of 'overflow' and of a
            // 'place-*' shorthand print as one word when they agree (measured: 'auto auto' and
            // 'place-self: center center' each read back a single word).
            case "overflow":
                return CollapseEqualPair(text);
            case "place-content":
                return Acrux.Core.Css.Resolver.CssPropertyApplier.PlacePairCanonical(text,
                    Acrux.Core.Css.Resolver.CssPropertyApplier.AlignContentWords,
                    Acrux.Core.Css.Resolver.CssPropertyApplier.JustifyContentWords);
            case "place-items":
                return Acrux.Core.Css.Resolver.CssPropertyApplier.PlacePairCanonical(text,
                    Acrux.Core.Css.Resolver.CssPropertyApplier.AlignItemsWords,
                    Acrux.Core.Css.Resolver.CssPropertyApplier.JustifyItemsWords);
            case "place-self":
                return Acrux.Core.Css.Resolver.CssPropertyApplier.PlacePairCanonical(text,
                    Acrux.Core.Css.Resolver.CssPropertyApplier.AlignSelfWords,
                    Acrux.Core.Css.Resolver.CssPropertyApplier.JustifySelfWords);

            // CSS Logical 1 §4 with CSS 2.1 §17.2: the logical border pair prints one word when
            // both edges wear the same style (measured: 'border-block-style: solid solid' reads
            // 'solid').
            case "border-block-style":
            case "border-inline-style":
            case "border-block-width":
            case "border-inline-width":
            case "border-block-color":
            case "border-inline-color":
                return CollapseEqualPair(text);

            // CSS 2.1 §11.1.2: the legacy rect() prints its four sides with commas, whichever
            // separator the page used, and every side carries its unit
            // (measured: 'rect(0px 1px 2px 3px)' reads back 'rect(0px, 1px, 2px, 3px)').
            case "clip":
                return LegacyClipText(text);

            // CSS Transforms 2 §3: 'scale' has no percentage — the number a percentage multiplies
            // is the factor the property stores (measured: 'scale: 50%' reads '0.5' and
            // 'scale: 1 50%' reads '1 0.5'), and a pair of equal factors reads one.
            case "scale":
                return ScaleText(text);

            // CSS Transforms 2 §4.2: the axis of a one-axis rotation is the same rotation spelled
            // with a vector, so the vector that names x, y or z prints as that letter and the
            // vector that names z prints as nothing at all (measured: 'rotate: 1 0 0 45deg' reads
            // 'x 45deg' and 'rotate: z 45deg' reads '45deg').
            case "rotate":
                return RotateAxisText(text);

            // CSS Fonts 4 §8.7: the variant families print in the order the shorthand's grammar
            // takes them, not the order the page wrote them (measured: 'small-caps
            // common-ligatures' reads back 'common-ligatures small-caps').
            case "font-variant":
                return OrderWords(text, FontVariantFamilyOrder);

            // CSS Backgrounds 3 §5.3: 'border-radius' is the four-corner list twice over, the two
            // axes divided by a slash, and each half collapses on its own
            // (measured: '1px 1px 1px 1px' reads '1px', '0 0 0 0' reads '0px', and
            // '1px 1px 1px 1px / 2px 2px 2px 2px' reads '1px / 2px').
            case "border-radius":
                return CollapseRadiusHalves(text);

            // CSS Grid 1 §7.2: the template with no areas and no tracks on either axis is the one
            // word 'none', not the pair the page wrote (measured: 'grid: none / none' and
            // 'grid-template: none / none' both read back 'none').
            case "grid":
            case "grid-template":
                return CollapseGridNone(text);

            // CSS Animations 1 §3.6: a quoted name is the keyframe set the ident of the same
            // spelling names, and the engine prints the ident (measured: 'animation-name: "quoted"'
            // reads back 'quoted').
            case "animation-name":
                return UnquoteNameList(text);

            default:
                return text;
        }
    }

    /// <summary>Whether a template says nothing on both of its axes — the pair the grammar allows
    /// and the value that has one word of its own.</summary>
    private static string CollapseGridNone(string text)
    {
        var halves = SplitRatioSides(text);
        if (halves.Count != 2) return text;
        return halves[0].Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
            && halves[1].Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
            ? "none" : text;
    }

    /// <summary>A comma list of custom idents, each with its quotes taken off — the spelling a
    /// quoted keyframe name, an animation name or a timeline name reads back as.</summary>
    public static string UnquoteNameList(string text)
    {
        var layers = SplitTopLevelCommas(text);
        var printed = new List<string>(layers.Count);
        foreach (var layer in layers)
        {
            var words = SplitTerms(layer);
            printed.Add(words.Count == 1 ? StripQuotes(words[0]) : string.Join(" ", words));
        }
        return string.Join(", ", printed);
    }

    private static string StripQuotes(string token)
        => token.Length >= 2 && ((token[0] == '"' && token[^1] == '"')
            || (token[0] == '\'' && token[^1] == '\''))
            ? token[1..^1] : token;

    /// <summary>Each side of a 'border-radius' value, folded to the shortest list of corners that
    /// still says all four. A percentage and a length are compared as the texts the page wrote,
    /// which is what the engine prints back.</summary>
    private static string CollapseRadiusHalves(string text)
    {
        var halves = text.Split('/', 2);
        var horizontal = CollapseFourCornerList(halves[0].Trim());
        if (halves.Length == 1) return horizontal;
        return horizontal + " / " + CollapseFourCornerList(halves[1].Trim());
    }

    private static string CollapseFourCornerList(string text)
    {
        var words = SplitTerms(text);
        if (words.Count != 4) return text;
        if (words[0] == words[1] && words[1] == words[2] && words[2] == words[3]) return words[0];
        if (words[0] == words[2] && words[1] == words[3]) return words[0] + " " + words[1];
        if (words[1] == words[3]) return words[0] + " " + words[1] + " " + words[2];
        return string.Join(" ", words);
    }

    /// <summary>The order the 'font-variant' shorthand prints its families in: ligatures, then
    /// numeric forms, then east-asian, then caps, then alternates (measured across the pairs that
    /// were asked about; every other list keeps its words because each family appears once).</summary>
    private static readonly string[] FontVariantFamilyOrder =
    {
        "common-ligatures", "no-common-ligatures", "discretionary-ligatures",
        "no-discretionary-ligatures", "historical-ligatures", "no-historical-ligatures",
        "contextual", "no-contextual",
        // The caps family comes before the numeric one, which is where the shorthand's grammar
        // puts it (measured: 'font-variant: small-caps oldstyle-nums' reads back in that order).
        "small-caps", "all-small-caps", "petite-caps", "all-petite-caps", "unicase",
        "titling-caps", "historical-forms",
        "lining-nums", "oldstyle-nums", "proportional-nums", "tabular-nums",
        "diagonal-fractions", "stacked-fractions", "ordinal", "slashed-zero",
        "jis78", "jis04", "simplified", "traditional", "proportional-width", "full-width",
        "half-width", "third-width", "quarter-width", "ruby", "isolated-ligatures",
        "no-isolated-ligatures",
        "sub", "super",
    };

    /// <summary>Words sorted into a named order, with the ones the list does not name keeping
    /// their place at the end.</summary>
    private static string OrderWords(string text, string[] order)
    {
        var words = System.Text.RegularExpressions.Regex
            .Split(text.Trim(), "\\s+").Where(w => w.Length > 0).ToList();
        if (words.Count < 2) return text;
        int Rank(string word)
        {
            int index = System.Array.IndexOf(order, word.ToLowerInvariant());
            return index < 0 ? order.Length : index;
        }
        // A stable sort keeps two words of the same family in the order the page wrote them.
        var numbered = words.Select((word, index) => (word, index)).ToList();
        numbered.Sort((a, b) =>
        {
            int byRank = Rank(a.word).CompareTo(Rank(b.word));
            return byRank != 0 ? byRank : a.index.CompareTo(b.index);
        });
        return string.Join(" ", numbered.Select(t => t.word));
    }

    /// <summary>The two-axis mask repeat as the one word it means (see
    /// <see cref="CanonicalizeB294"/>). A comma separates layers, and each layer is judged on
    /// its own terms.</summary>
    private static string MaskRepeatPair(string text)
    {
        var layers = SplitTopLevelCommas(text);
        var printed = new List<string>(layers.Count);
        foreach (var layer in layers)
        {
            var words = SplitTerms(layer);
            if (words.Count == 2)
            {
                var pair = words[0] + " " + words[1];
                if (pair.Equals("repeat no-repeat", StringComparison.OrdinalIgnoreCase))
                { printed.Add("repeat-x"); continue; }
                if (pair.Equals("no-repeat repeat", StringComparison.OrdinalIgnoreCase))
                { printed.Add("repeat-y"); continue; }
                if (words[0].Equals(words[1], StringComparison.OrdinalIgnoreCase))
                { printed.Add(words[0].ToLowerInvariant()); continue; }
            }
            printed.Add(string.Join(" ", SplitTerms(layer).Select(w => w.ToLowerInvariant())));
        }
        return string.Join(", ", printed);
    }

    /// <summary>The mask size with its second term left out when it repeats what the first says
    /// (measured: '1px auto' is '1px', 'auto auto' is 'auto', '1px 2px' is both).</summary>
    private static string MaskSizePair(string text)
    {
        var layers = SplitTopLevelCommas(text);
        var printed = new List<string>(layers.Count);
        foreach (var layer in layers)
        {
            var words = SplitTerms(layer);
            if (words.Count == 2 && words[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
                printed.Add(words[0]);
            else if (words.Count == 2 && words[0].Equals("auto", StringComparison.OrdinalIgnoreCase)
                && words[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
                printed.Add("auto");
            else printed.Add(string.Join(" ", words.Select(WithZeroUnit)));
        }
        return string.Join(", ", printed);
    }

    /// <summary>A zero written without its unit is printed with one (measured: 'mask-size: 0' reads
    /// back '0px'), and every other term is left exactly as it was said.</summary>
    private static string WithZeroUnit(string word)
        => word is "0" or "-0" or "+0" or "00" ? "0px" : word;

    /// <summary>The legacy rect() printed with the commas the reference engine prints, each side
    /// carrying the unit it was measured in (measured: 'rect(0 0 0 0)' is nobody's rect and never
    /// reaches here; 'rect(10px 5px 20px 5px)' reads back 'rect(10px, 5px, 20px, 5px)').</summary>
    private static string LegacyClipText(string text)
    {
        // The same split the grammar answers with, so a rect that reaches the value text cannot be
        // cut into a different number of sides than the one the declaration was accepted for.
        var sides = CssValueGrammar.LegacyClipSides(text);
        if (sides == null) return text.Trim().ToLowerInvariant();
        var printed = new List<string>(4);
        foreach (var side in sides)
        {
            // A zero is a zero whatever shape it was written in, and this property prints its
            // unit even when the page left it out.
            if (side != "auto" && double.TryParse(side, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number) && number == 0)
                printed.Add("0px");
            else printed.Add(side);
        }
        return "rect(" + string.Join(", ", printed) + ")";
    }

    /// <summary>'scale' as the engine stores it: every percentage folded to the factor it says,
    /// and a pair of equal factors read as one (measured: 'scale: 50% 50%' is '0.5' and
    /// 'scale: 2 50% 2' is '2 0.5 2').</summary>
    private static string ScaleText(string text)
    {
        var layers = SplitTopLevelCommas(text);
        var printed = new List<string>(layers.Count);
        foreach (var layer in layers)
        {
            var words = SplitTerms(layer).Select(FactorTerm).ToList();
            if (words.Count == 2 && words[0] == words[1]) words.RemoveAt(1);
            printed.Add(string.Join(" ", words));
        }
        return string.Join(", ", printed);
    }

    /// <summary>One term of a scale: a percentage is the hundredth it names, written as the number
    /// the property holds.</summary>
    private static string FactorTerm(string word)
    {
        word = word.ToLowerInvariant();
        if (!word.EndsWith("%", StringComparison.Ordinal)) return word;
        if (!double.TryParse(word[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out var share)) return word;
        // '50%' is the factor '0.5' and '10%' is '0.1'; the trim is the same one every number in a
        // value text gets, so '2 50% 2' does not come out as '2 0.50 2'.
        return (share / 100.0).ToString("0.#############################", CultureInfo.InvariantCulture);
    }

    /// <summary>'rotate' with its axis written the way the engine writes it: the unit vector that
    /// names one axis becomes that axis's letter, and the letter 'z' is the rotation the property
    /// means with no letter at all (measured, all three spellings).</summary>
    private static string RotateAxisText(string text)
    {
        var words = SplitTerms(text.Trim());
        if (words.Count == 4)
        {
            // Only the three vectors that name one axis fold; '1 1 0 45deg' is a real vector and
            // prints as written (measured, both).
            var vector = (words[0], words[1], words[2]);
            var angle = words[3];
            return vector switch
            {
                ("1", "0", "0") => "x " + angle,
                ("0", "1", "0") => "y " + angle,
                ("0", "0", "1") => angle,
                _ => string.Join(" ", words),
            };
        }
        // 'z 45deg' is the rotation the property means with no axis named (measured).
        if (words.Count == 2 && words[0].Equals("z", StringComparison.OrdinalIgnoreCase))
            return words[1];
        return text;
    }

    /// <summary>A pair of comma-free terms printed once when both say the same thing
    /// (measured for 'overflow' and for the logical border pairs: 'auto auto' is 'auto').</summary>
    private static string CollapseEqualPair(string text)
    {
        var layers = SplitTopLevelCommas(text);
        var printed = new List<string>(layers.Count);
        foreach (var layer in layers)
        {
            var words = SplitTerms(layer);
            printed.Add(words.Count == 2
                && words[0].Equals(words[1], StringComparison.OrdinalIgnoreCase)
                ? words[0].ToLowerInvariant() : string.Join(" ", words));
        }
        return string.Join(", ", printed);
    }

    /// <summary>The whitespace-separated terms of one layer. A parenthesis or a quotation holds its
    /// own spaces in, so a gradient is one term however many spaces it carries.</summary>
    private static List<string> SplitTerms(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c is '"' or '\'')
            {
                char quote = c;
                for (i++; i < text.Length; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i++;
                    else if (text[i] == quote) break;
                }
                continue;
            }
            else if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (i > start) parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        if (text.Length > start) parts.Add(text[start..]);
        return parts;
    }

    private static string CanonicalizeFilterList(string text, string property)
        => property.Equals("filter", StringComparison.OrdinalIgnoreCase)
            || property.Equals("backdrop-filter", StringComparison.OrdinalIgnoreCase)
            ? Acrux.Core.Css.CssFilterValue.CanonicalizeSpecified(text) : text;

    /// <summary>'background' says its components in the order the grammar takes them rather than
    /// the order the page wrote them (CSS Backgrounds 3 §4, measured); see
    /// <see cref="Acrux.Core.Css.ShorthandExpander.BackgroundLayerOrderText"/>. 'mask' has the same
    /// shape with a part list of its own (CSS Masking 1 §5.1).</summary>
    private static string CanonicalizeBackgroundShorthand(string text, string property)
    {
        if (property.Equals("background", StringComparison.OrdinalIgnoreCase))
            return Acrux.Core.Css.ShorthandExpander.BackgroundLayerOrderText(text);
        if (property.Equals("mask", StringComparison.OrdinalIgnoreCase)
            && Acrux.Core.Css.CssValueGrammar.TextIsValidFor("mask", text))
            return Acrux.Core.Css.ShorthandExpander.MaskLayerOrderText(text);
        return text;
    }

    private static readonly string[] ContainWords =
        { "none", "strict", "content", "size", "inline-size", "layout", "style", "paint" };

    /// <summary>'grid-auto-flow' says its axis only when it says 'column': 'row' is what the
    /// property means without it, so 'row dense' is the single word 'dense' (measured), while a
    /// plain 'row' still names itself. Two axes or two 'dense's are refused by the grammar and are
    /// left exactly as written here.</summary>
    private static string CanonicalizeGridAutoFlow(string text, string property)
    {
        if (!property.Equals("grid-auto-flow", StringComparison.OrdinalIgnoreCase)) return text;
        bool column = false, dense = false, row = false;
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            switch (p)
            {
                case "row": if (row || column) return text; row = true; break;
                case "column": if (row || column) return text; column = true; break;
                case "dense": if (dense) return text; dense = true; break;
                default: return text;
            }
        }
        if (!row && !column && !dense) return text;
        var parts = new List<string>();
        if (column) parts.Add("column");
        else if (!dense) parts.Add("row");
        if (dense) parts.Add("dense");
        return string.Join(" ", parts);
    }

    /// <summary>'mask-position' is a position, and a position is printed as its two axes with the
    /// horizontal one first and the axis the page left out named explicitly (measured: 'center'
    /// reads 'center center' and 'top' reads 'center top'). A comma-separated multi-layer value is
    /// left alone — the layers are the applier's to keep in order.</summary>
    private static string CanonicalizeMaskPosition(string text, string property)
    {
        if (!property.Equals("mask-position", StringComparison.OrdinalIgnoreCase)) return text;
        if (text.Contains(',')) return text;
        return ShorthandExpander.PositionPairText(text.Trim()) ?? text;
    }

    /// <summary>'paint-order' is printed as the shortest list that still says the whole order: the
    /// item the page left out is implied by the ones it named, so 'stroke fill' is just 'stroke'
    /// (measured). The applier already computes that prefix, and the echo says the same thing.</summary>
    private static string CanonicalizePaintOrder(string text, string property)
    {
        if (!property.Equals("paint-order", StringComparison.OrdinalIgnoreCase)) return text;
        return Css.Resolver.CssPropertyApplier.CanonicalPaintOrder(text) ?? text;
    }

    /// <summary>The geometry boxes a <c>&lt;geometry-box&gt;</c> names (CSS Shapes 1 §4, CSS
    /// Masking 1 §5.2). 'no-clip' belongs to 'clip', not to 'clip-path'.</summary>
    private static readonly string[] GeometryBoxes =
    {
        "border-box", "padding-box", "content-box", "margin-box",
        "fill-box", "stroke-box", "view-box",
    };

    /// <summary>'clip-path' is a shape with an optional geometry box, and the box goes AFTER the
    /// shape whichever way the page wrote it — so 'border-box circle(50%)' becomes
    /// 'circle(50%) border-box' (measured, and the reference engine drops the initial box entirely,
    /// which is what the echo says). This is not only a spelling: the clip parser reads the shape
    /// name as the text before the first '(', so a box written in front made the whole declaration
    /// name a shape that does not exist and the element drew unclipped.</summary>
    private static string CanonicalizeClipPath(string text, string property)
    {
        // One shape grammar, two default boxes: a clip starts at the border box (CSS Masking 1 §5.1)
        // and a float exclusion at the margin box (CSS Shapes 1 §11). The box that is not the default
        // is printed BEHIND the shape whichever end the page wrote it at.
        bool isClip = property.Equals("clip-path", StringComparison.OrdinalIgnoreCase);
        if (!isClip && !property.Equals("shape-outside", StringComparison.OrdinalIgnoreCase)) return text;
        var initialBox = isClip ? "border-box" : "margin-box";
        var tokens = Css.Resolver.EchoedProperties.SplitTopLevel(text.Trim());
        if (tokens.Count == 0) return text;
        string? shape = null, box = null;
        foreach (var token in tokens)
        {
            var lower = token.ToLowerInvariant();
            if (GeometryBoxes.Contains(lower))
            {
                // Two boxes is no shape value at all; leave it for the grammar to refuse.
                if (box != null) return text;
                box = lower;
                continue;
            }
            // 'none' and a clip-source url() carry no box and need no reordering.
            if (lower == "none" || lower.StartsWith("url(", StringComparison.Ordinal))
            {
                if (shape != null || box != null) return text;
                return lower == "none" ? "none" : text;
            }
            if (shape != null) return text;
            shape = FoldShapeLengths(token);
        }
        if (shape == null || box == initialBox) return shape ?? text;
        return box == null ? shape : shape + " " + box;
    }

    /// <summary>Inside a basic shape every argument is a length, a percentage or a keyword, and a
    /// number written without a unit is the zero pixels it stands for — which is what the reference
    /// engine prints (measured: 'inset(0)' reads 'inset(0px)', and the same fold shows in
    /// 'xywh(0 0 10px 10px)' → 'xywh(0px 0px 10px 10px)'). A nested call is left alone, because its
    /// own arguments are not lengths this fold may touch.</summary>
    private static string FoldShapeLengths(string token)
    {
        int open = token.IndexOf('(');
        if (open <= 0 || !token.EndsWith(")", StringComparison.Ordinal)) return token;
        var name = token[..open].ToLowerInvariant();
        var inner = token[(open + 1)..^1];
        // A quoted argument is opaque apart from its spelling: path()'s SVG data is re-printed the
        // way the engine reads it, in the double quotes the CSSOM always uses (measured:
        // "path('M0 0 L10 10')" reads 'path("M 0 0 L 10 10")').
        if (inner.Length > 0 && (inner[0] == '"' || inner[0] == '\''))
        {
            if (name != "path" || inner.Length < 2) return token;
            var data = Css.Resolver.EchoedProperties.CanonicalPathData(
                inner.Substring(1, inner.Length - 2));
            return data == null ? token : "path(\"" + data + "\")";
        }
        // A nested call — 'calc(1px + 2px)' inside an inset — is not a list of lengths to fold.
        if (inner.Contains('(')) return token;
        if (name == "inset") return "inset(" + FoldInset(inner) + ")";
        // The comma separates the points of a polygon, so it is the outer split and the space the
        // inner one: folding both into one list would turn 'polygon(0 0, 10px 10px)' into a list of
        // four coordinates instead of two points.
        var points = inner.Split(',');
        for (int i = 0; i < points.Length; i++)
        {
            var words = points[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int w = 0; w < words.Length; w++) words[w] = FoldOneLength(words[w]);
            points[i] = string.Join(" ", words);
        }
        return token[..open] + "(" + string.Join(inner.Contains(',') ? ", " : " ", points) + ")";
    }

    /// <summary>'inset' is one to four offsets and an optional 'round' radius list, and each list
    /// collapses to the shortest form that means the same four sides (measured: 'inset(50% 0% 50%
    /// 0%)' reads 'inset(50% 0%)').</summary>
    private static string FoldInset(string inner)
    {
        var words = inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int round = Array.FindIndex(words, w => w.Equals("round", StringComparison.OrdinalIgnoreCase));
        var offsetWords = round < 0 ? words : words[..round];
        var offsets = new List<string>();
        foreach (var word in offsetWords) offsets.Add(FoldOneLength(word));
        var text = string.Join(" ", CollapseSides(offsets));
        if (round >= 0)
        {
            var corners = new List<string>();
            foreach (var word in words[(round + 1)..]) corners.Add(word == "/" ? "/" : FoldOneLength(word));
            int slash = corners.IndexOf("/");
            if (slash < 0) text += " round " + string.Join(" ", CollapseSides(corners));
            else text += " round " + string.Join(" ", CollapseSides(corners[..slash])) + " / "
                       + string.Join(" ", CollapseSides(corners[(slash + 1)..]));
        }
        return text;
    }

    /// <summary>The shortest list that means the same four sides as the one given.</summary>
    private static List<string> CollapseSides(List<string> values)
    {
        if (values.Count is 0 or > 4) return values;
        string top = values[0];
        string right = values.Count > 1 ? values[1] : values[0];
        string bottom = values.Count > 2 ? values[2] : values[0];
        string left = values.Count > 3 ? values[3] : right;
        if (bottom == top && left == right)
            return top == right ? new List<string> { top } : new List<string> { top, right };
        if (left == right) return new List<string> { top, right, bottom };
        return new List<string> { top, right, bottom, left };
    }

    /// <summary>A bare number in a length position is the pixels it stands for.</summary>
    private static string FoldOneLength(string word)
    {
        if (word.Length == 0) return word;
        if (word.EndsWith("%", StringComparison.Ordinal)
            || word.EndsWith("px", StringComparison.OrdinalIgnoreCase)) return word;
        return double.TryParse(word, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _) ? word + "px" : word;
    }

    /// <summary>'object-position' and 'background-position' are the same <c>&lt;position&gt;</c>
    /// grammar, printed the same way. Their appliers read the axes by keyword, so the echo has to
    /// say both of them (measured: 'background-position: top' and 'mask-position: top' both read
    /// 'center top').</summary>
    private static string CanonicalizePositionPair(string text, string property)
    {
        // The same &lt;position&gt; printed the same way: a one-sided value gains the center of the
        // other axis (measured for each of the four — 'background-position: top', 'object-position:
        // top', 'perspective-origin: left' and 'transform-origin: 10px' read back 'center top',
        // 'center top', 'left center' and '10px center').
        if (!property.Equals("object-position", StringComparison.OrdinalIgnoreCase)
            && !property.Equals("background-position", StringComparison.OrdinalIgnoreCase)
            && !property.Equals("perspective-origin", StringComparison.OrdinalIgnoreCase)
            && !property.Equals("transform-origin", StringComparison.OrdinalIgnoreCase)) return text;
        if (text.Contains(',')) return text;
        return ShorthandExpander.PositionPairText(text.Trim()) ?? text;
    }

    /// <summary>'content' is echoed as a space-separated list of its items, which is what the
    /// reference engine prints even when the page ran two strings together (measured:
    /// 'content: "a""b"' reads back '"a" "b"'). A value the grammar will refuse is left exactly as
    /// written — re-spelling it would be the same mistake a shorthand canonicaliser makes when it
    /// tidies an invalid value into a valid one.</summary>
    private static string CanonicalizeContent(string text, string property)
    {
        if (!property.Equals("content", StringComparison.OrdinalIgnoreCase)) return text;
        if (!CssValueGrammar.TextIsValidFor("content", text)) return text;
        var tokens = Acrux.Core.Css.Resolver.EchoedProperties.SplitContentItems(text.Trim());
        for (int i = 0; i < tokens.Count; i++) tokens[i] = CanonicalCounterCall(tokens[i]);
        return tokens.Count == 0 ? text : string.Join(" ", tokens);
    }

    /// <summary>A counter call is printed with its arguments separated by one space after the comma,
    /// and without a style that is the default anyway (measured: 'counter(x, decimal)' reads back
    /// 'counter(x)' while 'counter(x, upper-roman)' keeps its style).</summary>
    private static string CanonicalCounterCall(string token)
    {
        int open = token.IndexOf('(');
        if (open <= 0 || !token.EndsWith(")", StringComparison.Ordinal)) return token;
        var name = token[..open].ToLowerInvariant();
        if (name != "counter" && name != "counters") return token;
        var args = new List<string>(CssValueGrammar.SplitCounterArguments(token[(open + 1)..^1]));
        // 'counter(name, decimal)' and 'counters(name, sep, decimal)' say the style the property
        // would use anyway.
        int styleIndex = name == "counter" ? 1 : 2;
        if (args.Count == styleIndex + 1
            && args[styleIndex].Trim().Equals("decimal", StringComparison.OrdinalIgnoreCase))
            args.RemoveAt(styleIndex);
        for (int i = 0; i < args.Count; i++) args[i] = args[i].Trim();
        return name + "(" + string.Join(", ", args) + ")";
    }

    /// <summary>'columns' prints the width before the count, and omits whichever half is 'auto'
    /// (measured: '2 100px' reads '100px 2', 'auto 100px' reads '100px', '2 auto' reads '2'). A bare
    /// positive integer is the count; everything else that survives is the width, already folded to
    /// its unit by the generic pass ('0' has become '0px' by now). A second width or count is not the
    /// engine's to reorder — '92px 8px' is an invalid columns value, so it is left exactly as written
    /// for the grammar/applier to refuse rather than silently trimmed to a valid '92px'.</summary>
    private static string CanonicalizeColumns(string text, string property)
    {
        if (!property.Equals("columns", StringComparison.OrdinalIgnoreCase)) return text;
        string? width = null, count = null;
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            if (p == "auto") continue;
            // A bare positive integer is the count; a lone '0' (folded to '0px' by now) and any
            // unit-bearing value are the width. A second of either is not the engine's to reorder.
            if (int.TryParse(p, out var n) && n >= 1)
            {
                if (count != null) return text;
                count = p;
            }
            else
            {
                if (width != null) return text;
                width = word;
            }
        }
        var parts = new List<string>(2);
        if (width != null) parts.Add(width);
        if (count != null) parts.Add(count);
        return parts.Count == 0 ? "auto" : string.Join(" ", parts);
    }

    /// <summary>'text-underline-position' prints its vertical choice before its horizontal one
    /// (measured: 'left under' reads back 'under left'). A value the parser refuses is left exactly
    /// as written, so the grammar/applier still drops it rather than the printer repairing it.</summary>
    private static string CanonicalizeTextUnderlinePosition(string text, string property)
    {
        if (!property.Equals("text-underline-position", StringComparison.OrdinalIgnoreCase)) return text;
        if (!Resolver.CssPropertyApplier.TryParseTextUnderlinePosition(text, out var p)) return text;
        if (p == Acrux.Core.Dom.TextUnderlinePositionType.Auto) return "auto";
        var parts = new List<string>(2);
        if ((p & Acrux.Core.Dom.TextUnderlinePositionType.Under) != 0) parts.Add("under");
        if ((p & Acrux.Core.Dom.TextUnderlinePositionType.FromFont) != 0) parts.Add("from-font");
        if ((p & Acrux.Core.Dom.TextUnderlinePositionType.Left) != 0) parts.Add("left");
        if ((p & Acrux.Core.Dom.TextUnderlinePositionType.Right) != 0) parts.Add("right");
        return parts.Count == 0 ? "auto" : string.Join(" ", parts);
    }

    /// <summary>
    /// Print <c>text-indent</c> the way the reference engine does: the length first, then
    /// <c>hanging</c>, then <c>each-line</c>, whichever order the page wrote them in (measured:
    /// 'each-line 20px' reads '20px each-line' and '10px each-line hanging' reads
    /// '10px hanging each-line'). Each flag says itself once, and a word that is neither flag is
    /// no alias this engine has — 'cap 2' is refused rather than printed.
    /// Null means the text is not a value at all, which is also how the grammar reads it.
    /// </summary>
    public static string? CanonicalizeTextIndent(string text, string property)
    {
        if (!property.Equals("text-indent", StringComparison.OrdinalIgnoreCase)) return null;
        bool? hanging = null, eachLine = null;
        string? length = null;
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            if (p == "hanging") { if (hanging == true) return null; hanging = true; continue; }
            if (p == "each-line") { if (eachLine == true) return null; eachLine = true; continue; }
            if (length != null) return null;
            // A length or percentage, with the one exception every length property shares: a bare
            // number is a length only when it is zero, and then it is printed with its unit.
            if (Dom.Length.IsLength(word)) length = word;
            else if (IsBareZero(p)) length = "0px";
            else return null;
        }
        // The flags are additions to an indent, not an indent of their own: 'text-indent: hanging'
        // and 'text-indent: each-line' are each dropped (measured), while '10px hanging each-line'
        // stands.
        if (length == null) return null;
        var parts = new List<string>();
        if (length != null) parts.Add(length);
        if (hanging == true) parts.Add("hanging");
        if (eachLine == true) parts.Add("each-line");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Print <c>aspect-ratio</c>: the engine always says both sides of the ratio, so a lone number
    /// gains its denominator (measured: '0.5' reads '0.5 / 1'), and an <c>auto</c> written beside a
    /// ratio is kept in both the specified and the computed value (measured: 'auto 1 / 2' reads
    /// 'auto 1 / 2' — the box's own ratio does not replace it).
    /// </summary>
    public static string? CanonicalizeAspectRatio(string text)
    {
        var trimmed = text.Trim();
        string? auto = null;
        if (trimmed.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed[4..].Trim();
            if (rest.Length == 0) return "auto";
            auto = "auto";
            trimmed = rest;
        }
        var sides = SplitRatioSides(trimmed);
        string a, b;
        if (sides.Count == 1)
        {
            if (!PositiveRatioSide(sides[0].Trim())) return null;
            a = sides[0].Trim(); b = "1";
        }
        else
        {
            if (sides.Count != 2) return null;
            a = sides[0].Trim(); b = sides[1].Trim();
            if (!PositiveRatioSide(a) || !PositiveRatioSide(b)) return null;
        }
        var ratio = a + " / " + b;
        return auto == null ? ratio : auto + " " + ratio;
    }

    /// <summary>The sides of a ratio, split at the slash that stands outside every function — the one
    /// inside 'calc(1 / 2)' belongs to the expression and does not make two sides of it.</summary>
    internal static List<string> SplitRatioSides(string text) => SplitTopLevel(text, '/').ToList();

    private static bool PositiveNumber(string text) =>        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0;

    /// <summary>Whether one side of a ratio is a number the ratio can carry: a positive plain number,
    /// or a math expression that resolves to a count. The reference engine reads
    /// 'aspect-ratio: calc(1 + 1)' as the ratio it prints 'calc(2) / 1' and leaves
    /// 'aspect-ratio: calc(1px)' out of the cascade altogether (measured, both) — a length is no
    /// count whatever number it is written with.</summary>
    /// <summary>A side of a ratio is a number that is not negative: 'aspect-ratio: 0' is the value
    /// '0 / 1', '1 / 0' stands as written, and '-1' is dropped (measured, all three) — the reference
    /// engine asks for [0,∞) on both sides and does not divide by the second one at computed time.</summary>
    private static bool PositiveRatioSide(string text)
    {
        if (text.IndexOf('(') < 0)
            return Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(
                text, out var number, out var unit) && unit.Length == 0 && number >= 0;
        return CssMathValue.WellTyped(text, out var category)
            && (category == "number" || category == null);
    }

    /// <summary>
    /// Print <c>counter-reset</c>, <c>counter-set</c> and <c>counter-increment</c>: every counter
    /// says its integer, whether or not the page wrote one (measured: 'c' reads 'c 0' for reset but
    /// 'c 1' for increment, and 'c reverse' reads 'c 0 reverse 0' because 'reverse' is a counter name
    /// and not a keyword here). A repeated name is not folded away — the engine prints the pairs in
    /// the order they were written.
    /// </summary>
    public static string? CanonicalizeCounterList(string text, string defaultValue = "0")
    {
        var words = SplitTopLevelWords(text);
        if (words.Count == 0) return null;
        if (words.Count == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        var parts = new List<string>();
        for (int i = 0; i < words.Count; i++)
        {
            var name = words[i];
            if (name.Equals("none", StringComparison.OrdinalIgnoreCase)
                || name.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || !IsCounterNameToken(name)) return null;
            string value = defaultValue;
            if (i + 1 < words.Count
                && double.TryParse(words[i + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                // An <integer> that does not fit is read as the closest one that does and printed
                // as a whole number (measured: 'counter-reset: x 2147483648' reads 'x 2147483647').
                value = double.TryParse(words[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? ((long)Math.Clamp(Math.Round(number), int.MinValue, int.MaxValue))
                        .ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : words[i + 1];
                i++;
            }
            parts.Add(name + " " + value);
        }
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static bool IsCounterNameToken(string word)
    {
        foreach (var c in word)
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '\\')) return false;
        return word.Length > 0;
    }

    private static int LineOrder(string line) => line switch
    {
        "underline" => 0,
        "overline" => 1,
        "line-through" => 2,
        _ => 3,
    };

    /// <summary>A number written with no unit and worth zero — the one length a page may write
    /// without saying what it is measured in.</summary>
    /// <summary>A single quoted string, re-printed with double quotes; anything else is left as
    /// written.</summary>
    private static string FoldQuotedString(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '\'' || trimmed[^1] != '\'') return text;
        return "\"" + trimmed[1..^1] + "\"";
    }

    private static bool IsBareZero(string token) =>
        double.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value == 0;

    /// <summary>
    /// Print <c>column-rule</c> the way the border shorthands are printed: a component that repeats
    /// its longhand's initial is dropped, and a shorthand with nothing left says the width's initial
    /// (<c>medium</c>) alone (measured: 'medium none currentcolor' reads 'medium' while
    /// '2px solid red' reads as written).
    /// </summary>
    private static string CanonicalizeColumnRule(string text, string property)
    {
        if (!property.Equals("column-rule", StringComparison.OrdinalIgnoreCase)) return text;
        var parts = new List<string>();
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            if (p is "medium" or "none" or "currentcolor") continue;
            parts.Add(word);
        }
        if (parts.Count == 0) parts.Add("medium");
        return string.Join(" ", parts);
    }

    /// <summary>The five parts of the <c>offset</c> shorthand, each said by the longhand that owns
    /// it and each dropped when it still says that longhand's initial — until nothing is left, which
    /// the shorthand says as its own initial <c>normal</c> (CSSOM §5.3.1; measured:
    /// <c>offset: content-box path("M 0 0") 50% auto</c> reads
    /// <c>path("M 0 0") content-box 50%</c> and a value that sets nothing reads <c>normal</c>).</summary>
    private static string CanonicalizeOffsetShorthand(string text, string property)
    {
        if (!ShorthandExpander.IsOffsetShorthandName(property)) return text;
        var expanded = ShorthandExpander.ExpandProperty(property, text);
        if (expanded.Count == 0) return text;
        var parts = new List<string>();
        // The position is a position, and a position prints as its two axes with the horizontal
        // one first (measured: 'offset: 1px' reads '1px center' and 'offset: top' reads
        // 'center top' — the same pair 'background-position' writes).
        if (expanded.TryGetValue("offset-position", out var position) && position != "normal")
            parts.Add(ShorthandExpander.PositionPairText(position) ?? position);
        if (expanded.TryGetValue("offset-path", out var path) && path != "none")
            parts.Add(path);
        if (expanded.TryGetValue("offset-distance", out var distance) && distance != "0px")
            parts.Add(distance);
        if (expanded.TryGetValue("offset-rotate", out var rotate) && rotate != "auto")
            parts.Add(rotate);
        return parts.Count == 0 ? "normal" : string.Join(" ", parts);
    }

    /// <summary>
    /// Put <c>list-style</c>'s three parts in the order the engine prints them — position, image,
    /// type — keeping each one the page wrote (measured: 'url(a.png) inside' reads back
    /// 'inside url("a.png")', and 'disc outside none' keeps all three as 'outside none disc',
    /// because this shorthand prints what was written rather than only what differs).
    /// </summary>
    private static string CanonicalizeListStyle(string text, string property)
    {
        if (!property.Equals("list-style", StringComparison.OrdinalIgnoreCase)) return text;
        string? position = null, image = null, type = null;
        // The three slots are filled in the order the words arrive, each word going to the first
        // open slot that can read it — which is how the engine reads the shapes a strict per-slot
        // count refuses (measured: 'none none' prints the image and the type, and 'inside outside'
        // prints the position and a type whose name happens to be a position word).
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            if (position == null && p is "inside" or "outside") { position = p; continue; }
            if (image == null && (p.Contains("(") && !p.StartsWith("calc(", StringComparison.Ordinal)))
            { image = word; continue; }
            if (image == null && p == "none") { image = p; continue; }
            if (type == null) { type = word; continue; }
            return text;
        }
        var parts = new List<string>();
        if (position != null) parts.Add(position);
        if (image != null) parts.Add(image);
        if (type != null) parts.Add(type);
        return parts.Count == 0 ? text : string.Join(" ", parts);
    }

    /// <summary>Apply a fold to each comma-separated layer of a value and rejoin them. A list
    /// property carries several shadows or gradients in one value, and a fold that walks the whole
    /// text would read a layer's trailing comma as part of its last token — which is how
    /// <c>'text-shadow: 2px 2px 0 #e33, 4px 4px 0 #33e'</c> came to lose a layer and gain a
    /// black colour (a five-character '<c>#e33,</c>' is a valid hex alpha form to the parser).</summary>
    private static string MapLayers(string text, Func<string, string> fold)
    {
        var layers = new List<string>();
        foreach (var layer in SplitTopLevel(text, ','))
            layers.Add(fold(layer.Trim()));
        return string.Join(", ", layers);
    }

    /// <summary>Split on commas that are not inside a function.</summary>
    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == separator && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        yield return text[start..];
    }

    /// <summary>Whether the text at |i| begins a number, which is what tells a leading sign
    /// whether it belongs to one or is an operator between two.</summary>
    private static bool StartsNumber(string value, int i) =>
        i < value.Length && (char.IsAsciiDigit(value[i])
            || (value[i] == '.' && i + 1 < value.Length && char.IsAsciiDigit(value[i + 1])));

    /// <summary>The number token as the reference engine prints it: the value re-written shortest
    /// and the unit in lower case. The engine stores what the number means, so '1E2PX' is
    /// '100px', '1.50px' is '1.5px', '.5em' is '0.5em', '+2px' is '2px' and '-0px' is '0px'
    /// (measured), and a unit is a keyword, so it has the one spelling. Only a token that begins
    /// with a number is touched, so a hex colour, an identifier and a unicode range keep their
    /// characters.</summary>
    /// <summary>A token that is nothing but an integer's digits, with the one sign a page may put
    /// in front of them — the spelling an &lt;integer&gt; keeps as it was written.</summary>
    private static bool IsIntegerDigits(string token)
    {
        int i = token.Length > 0 && (token[0] == '+' || token[0] == '-') ? 1 : 0;
        if (i >= token.Length) return false;
        for (; i < token.Length; i++)
            if (!char.IsAsciiDigit(token[i])) return false;
        return true;
    }

    private static string FoldNumberAndUnit(string token, bool cut = false)
    {
        if (token.Length == 0
            || !(char.IsAsciiDigit(token[0]) || token[0] == '.' || token[0] == '+' || token[0] == '-'))
            return token;
        // A leading sign belongs to the number, so the scan starts after it — otherwise the
        // number part reads as empty and '-0px' survives instead of becoming '0px'.
        int i = token[0] is '+' or '-' ? 1 : 0;
        while (i < token.Length && (char.IsAsciiDigit(token[i]) || token[i] == '.')) i++;
        // An exponent belongs to the number, so '1e3px' splits after the last digit, not at the 'e'.
        if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
        {
            int probe = i + 1;
            if (probe < token.Length && (token[probe] == '+' || token[probe] == '-')) probe++;
            int digits = probe;
            while (probe < token.Length && char.IsAsciiDigit(token[probe])) probe++;
            if (probe > digits) i = probe;
        }
        var number = token[..i];
        int unitStart = i;
        while (i < token.Length && (char.IsLetter(token[i]) || token[i] == '%')) i++;
        if (i != token.Length) return token;
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.IsNaN(value) || double.IsInfinity(value))
            return token;
        var unit = token[unitStart..].ToLowerInvariant();
        // A six-digit print, and for the box family the number first cut at seven decimals. The cut
        // is taken on the digits the page wrote, which are still in hand here: they are not always
        // how the parser's own widening would be written again.
        return (cut ? Acrux.Core.Dom.Animations.CssValueTokenizer.NumCutText(number)
                    : Acrux.Core.Dom.Animations.CssValueTokenizer.Num(value)) + unit;
    }

    /// <summary>Rewrite each colour a colour-valued property carries. A named colour prints as its
    /// own lower-case name; anything else — a hex, an <c>hsl()</c>, a modern <c>rgb()</c> written
    /// with spaces or a slash — prints as the canonical <c>rgb()</c>/<c>rgba()</c> the engine
    /// parses it to (measured over 33 spellings: '#F00' and 'hsl(0,100%,50%)' both read
    /// 'rgb(255, 0, 0)', 'rgb(1 2 3 / 0.5)' reads 'rgba(1, 2, 3, 0.5)', 'TRANSPARENT' reads
    /// 'transparent', and 'currentColor' reads 'currentcolor' because it names no colour yet).</summary>
    /// <summary>'opacity' is a &lt;number&gt;, and a percentage is the same number written the other
    /// way: the reference engine folds it on the way in, so 'opacity: 50%' is stored, echoed and
    /// computed as '0.5' and 'opacity: 101%' as '1.01' (measured, both — the clamp to the property's
    /// range happens later, on the computed value only).</summary>
    private static string CanonicalizeOpacityNumber(string text, string property)
    {
        if (!property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) return text;
        var token = text.Trim();
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(token, out var number, out var unit)
            || unit != "%") return text;
        return Acrux.Core.Dom.Animations.CssValueTokenizer.Num(number / 100f);
    }

    /// <summary>'border-spacing' is the pair of table axes and prints the shortest spelling that
    /// says the same thing: '0 0' is '0px' and '1px 1px' is '1px', while two different axes keep both
    /// words (measured). The same rule the four-side shorthands follow, on two sides.</summary>
    private static string CanonicalizeBorderSpacing(string text, string property)
    {
        if (!property.Equals("border-spacing", StringComparison.OrdinalIgnoreCase)) return text;
        var words = SplitTopLevelWords(text);
        if (words.Count != 2) return text;
        return words[0].Equals(words[1], StringComparison.OrdinalIgnoreCase) ? words[0] : text;
    }

    private static string FoldColorWords(string text, string property)
    {
        if (!Resolver.CssPropertyTraits.TakesColorValue(property)) return text;
        return MapLayers(text, layer => FoldColorWordsInLayer(layer, property));
    }

    private static string FoldColorWordsInLayer(string text, string property)
    {
        var words = SplitTopLevelWords(text);
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.Contains('(', StringComparison.Ordinal))
            {
                // A colour written as a function of numbers is the form the engine folds into
                // rgb()/rgba(). Every other function it keeps as written — evaluating a
                // 'color-mix()' here would replace what the page wrote with a number no page asked
                // for — but it does parse the arguments, so those print canonically:
                // 'linear-gradient(45DEG, RED 10%, BLUE)' is 'linear-gradient(45deg, red 10%, blue)'
                // and 'color-mix(in hsl, hsl(0 100% 50%), …)' has its stops folded to rgb() the way
                // the reference engine prints them (measured; and re-read: the expansion was checked
                // against the background, border-image and color-mix parsers alike).
                if (ColorParser.IsColorToken(word) && IsPlainColorFunction(word) && !IsRelativeColorSyntax(word))
                    words[i] = CssColorText.FromColor(ColorParser.Parse(word));
                // A lightness, a chroma and an angle are not colours to fold but arguments to say
                // correctly, and the reference engine re-spells them: 'lab(50% 20 30 / 50%)' is
                // 'lab(50 20 30 / 0.5)', 'lch(50% 30 50grad)' is 'lch(50 30 45)',
                // 'oklab(50% …)' is 'oklab(0.5 …)', and 'color(xyz …)' names its space in full.
                else if (CssColorFunctions.Canonicalize(word) is { } colourFunction)
                    words[i] = colourFunction;
                else
                    words[i] = InsideFunction(word,
                        inner => MapLayers(inner, layer => FoldColorWordsInLayer(layer, property)));
                continue;
            }
            if (!ColorParser.IsColorToken(word)) continue;
            if (word[0] == '#')
            {
                words[i] = CssColorText.FromColor(ColorParser.Parse(word));
                continue;
            }
            if (ColorParser.IsColorName(word)
                || word.Equals("transparent", StringComparison.OrdinalIgnoreCase)
                || word.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
                words[i] = word.ToLowerInvariant();
        }
        return string.Join(" ", words);
    }

    /// <summary>Apply |fold| to the arguments of a function word and rebuild it with a lower-case
    /// name. A <c>url()</c> and its siblings name a file or a string the page owns rather than a
    /// value the engine parses, so those are left exactly as written.</summary>
    private static string InsideFunction(string word, Func<string, string> fold)
    {
        int open = word.IndexOf('(');
        if (open <= 0 || !word.EndsWith(")", StringComparison.Ordinal)) return word;
        var name = word[..open];
        if (!IsBareIdentifier(name)) return word;
        foreach (var blocked in OpaqueArgumentFunctions)
            if (name.Equals(blocked, StringComparison.OrdinalIgnoreCase)) return word;
        var lowered = name.ToLowerInvariant();
        var inner = fold(word[(open + 1)..^1]);
        if (lowered == "color-mix") inner = CanonicalColorMix(inner);
        // A function name is ASCII case-insensitive on the way in, but the CSSOM prints the few
        // that carry a capital in their canonical spelling — the transform family above all, so
        // 'TRANSLATEZ(10px)' reads back 'translateZ(10px)' and not 'translatez(10px)' (measured).
        if (CanonicalFunctionNames.TryGetValue(lowered, out var canonical)) lowered = canonical;
        return lowered + "(" + inner + ")";
    }

    /// <summary>Function names whose printed form is not simply the lowercase one.</summary>
    private static readonly Dictionary<string, string> CanonicalFunctionNames =
        new(StringComparer.Ordinal)
    {
        ["rotatex"] = "rotateX", ["rotatey"] = "rotateY", ["rotatez"] = "rotateZ",
        ["rotatedx"] = "rotateX", ["rotatedy"] = "rotateY", ["rotatedz"] = "rotateZ",
        ["scalex"] = "scaleX", ["scaley"] = "scaleY", ["scalez"] = "scaleZ",
        ["translatex"] = "translateX", ["translatey"] = "translateY", ["translatez"] = "translateZ",
        ["rotate3d"] = "rotate3d", ["scale3d"] = "scale3d", ["translate3d"] = "translate3d",
        ["matrix3d"] = "matrix3d",
    };

    /// <summary>
    /// An <c>animation</c> value in the order the engine prints it: duration, timing function,
    /// delay, iteration count, direction, fill mode, play state, name — every part present, the ones
    /// the page left out standing in with the initial the shorthand prints (a duration of
    /// <c>auto</c>, which is not what the longhand starts at). A value that is one of the CSS-wide
    /// keywords is left exactly as it is, because there is no list of parts in it to order.
    /// </summary>
    private static string CanonicalAnimationShorthand(string value)
    {
        var text = value.Trim();
        if (text.Length == 0
            || text.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            || text.Equals("initial", StringComparison.OrdinalIgnoreCase)
            || text.Equals("unset", StringComparison.OrdinalIgnoreCase)
            || text.Equals("revert", StringComparison.OrdinalIgnoreCase)
            || text.Equals("revert-layer", StringComparison.OrdinalIgnoreCase))
            return value;

        var layers = new List<string>();
        foreach (var part in ShorthandExpander.ClassifyAnimationEntries(text))
        {
            layers.Add(string.Join(" ", new[]
            {
                part[0].Length > 0 ? part[0] : "auto",
                part[1].Length > 0 ? part[1] : "ease",
                part[2].Length > 0 ? part[2] : "0s",
                part[3].Length > 0 ? part[3] : "1",
                part[4].Length > 0 ? part[4] : "normal",
                part[5].Length > 0 ? part[5] : "none",
                part[6].Length > 0 ? part[6] : "running",
                // The keyframe set's name prints without the quotes a string spelling carried
                // (measured: 'animation: "slide"' names 'slide').
                part[7].Length > 0 ? StripQuotes(part[7]) : "none",
            }));
        }
        return string.Join(", ", layers);
    }

    /// <summary>
    /// Print a <c>color-mix()</c> with only what differs from the default (CSS Color 5 §5). The
    /// interpolation space disappears when it is the initial <c>oklab</c>, and a component's
    /// percentage disappears when it is the value the other component already implies — measured:
    /// <c>in oklab, RED 50%, blue 50%</c> comes back as <c>red, blue</c>,
    /// <c>in oklab, red 40%, blue 60%</c> as <c>red 40%, blue</c>, and
    /// <c>in srgb, red 25%, blue 75%</c> as <c>in srgb, red 25%, blue</c>.
    /// </summary>
    private static string CanonicalColorMix(string inner)
    {
        var args = SplitTopLevelCommas(inner);
        if (args.Count != 3) return inner;
        var space = args[0].Trim();
        if (space.Equals("in oklab", StringComparison.OrdinalIgnoreCase)) args[0] = string.Empty;

        var first = TakePercentageOff(args[1], out float firstShare);
        bool firstWritten = firstShare >= 0;
        float writtenFirst = firstWritten ? firstShare : 50f;
        // The first component's own initial is half, and only that one goes unsaid.
        if (firstWritten && Math.Abs(firstShare - 50f) < 0.0001f) firstShare = -1;
        var second = TakePercentageOff(args[2], out float secondShare);
        // A share written on the SECOND component is the first one's complement, and the engine says
        // the FIRST, which is the one that has no initial to hide behind (measured: 'red, blue 40%'
        // reads 'red 60%, blue' and 'red, blue 100%' reads 'red 0%, blue').
        if (!firstWritten && secondShare >= 0 && Math.Abs(secondShare - 50f) >= 0.0001f)
        {
            firstShare = 100f - secondShare;
            secondShare = -1;
        }
        // The second share is implied by the first, and a written one that says the same thing
        // adds nothing to print.
        if (secondShare >= 0 && Math.Abs(secondShare - (100f - writtenFirst)) < 0.0001f) secondShare = -1;
        args[1] = first + (firstShare >= 0 ? " " + FormatShare(firstShare) : string.Empty);
        args[2] = second + (secondShare >= 0 ? " " + FormatShare(secondShare) : string.Empty);

        var kept = args.Where(a => a.Trim().Length > 0).Select(a => a.Trim()).ToList();
        return string.Join(", ", kept);
    }

    /// <summary>The component with its trailing percentage removed, and the share it carried (-1
    /// when there was none, or when it was not a plain percentage the printer can judge).</summary>
    private static string TakePercentageOff(string component, out float share)
    {
        share = -1f;
        var text = component.Trim();
        if (text.Length == 0 || text[^1] != '%') return component;
        var words = SplitTopLevelWords(text);
        if (words.Count == 0) return component;
        var last = words[^1];
        if (last.Length < 2 || last[^1] != '%' ||
            !float.TryParse(last[..^1], NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            return component;
        share = value;
        words.RemoveAt(words.Count - 1);
        return string.Join(" ", words);
    }

    private static string FormatShare(float value) =>
        Math.Abs(value - MathF.Round(value)) < 0.0001f
            ? ((int)MathF.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"
            : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static readonly string[] OpaqueArgumentFunctions =
        { "url", "src", "local", "format", "domain", "regexp", "prefix", "image-set", "path" };

    /// <summary>Print the words a property's grammar owns in their canonical spelling. The engine
    /// stores the value it parsed rather than the text it read, so a keyword comes back in lower
    /// case whatever the page wrote — <c>visibility: COLLAPSE</c> is 'collapse',
    /// <c>text-rendering: OPTIMIZELEGIBILITY</c> even loses the camel case the author meant
    /// (measured over sixty properties, and every one of them folded). Two things are left alone:
    /// a property whose value can name something the page invented, and a word that is not a bare
    /// identifier — a string, a <c>url()</c>, a <c>var()</c> and a function's arguments are the
    /// page's own data, and only the folds above descend into those.</summary>
    private static string FoldKeywordCase(string text, string property)
    {
        var keywords = SpecifiedKeywords(property);
        if (keywords != null) return MapLayers(text, layer => FoldKeywordsInLayer(layer, keywords));
        if (CustomIdentProperties.Contains(property)) return text;
        return MapLayers(text, FoldBareIdentsInLayer);
    }

    private static string FoldBareIdentsInLayer(string layer)
    {
        var words = SplitTopLevelWords(layer);
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.Contains('(', StringComparison.Ordinal))
            {
                words[i] = InsideFunction(word, inner => MapLayers(inner, FoldBareIdentsInLayer));
                continue;
            }
            // A trailing comma belongs to the separator, not to the word.
            var bare = word.EndsWith(",", StringComparison.Ordinal) ? word[..^1] : word;
            if (IsBareIdentifier(bare)) words[i] = bare.ToLowerInvariant() + (word.Length - bare.Length == 1 ? "," : "");
        }
        return string.Join(" ", words);
    }

    /// <summary>The properties whose value space holds a name the page invented — a family, a
    /// counter, a named grid line, an ident standing for an element, a timeline or a keyframe set.
    /// No grammar of ours owns those words, so the engine prints them as written (measured:
    /// <c>font-family: ARIA1</c> and <c>animation-name: BOGUS</c> both keep their case).</summary>
    private static readonly HashSet<string> CustomIdentProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "font-family", "src", "format", "unicode-range", "animation-name", "list-style-type",
        "counter-reset", "counter-increment", "counter-set", "string-set", "bookmark-label",
        "bookmark-level", "grid-template", "grid-template-areas", "grid-template-columns",
        "grid-template-rows", "grid-area", "grid-row", "grid-column", "grid-row-start",
        "grid-row-end", "grid-column-start", "grid-column-end", "anchor-name", "anchor-scope",
        "position-anchor", "position-try-anchor", "view-transition-name", "view-transition-class",
        // A scheme list is keywords and names mixed, and the names keep the author's case
        // (measured: 'color-scheme: Brown' reads 'Brown', while 'Light' is the keyword 'light').
        "color-scheme",
        "timeline-scope", "scroll-timeline-name", "view-timeline-name", "container-name",
        // The 'container' shorthand is a list of names on this engine's reading too: the reference
        // engine echoes it back exactly as authored, capitals and all (measured: 'INLINE-SIZE
        // Cards' reads 'INLINE-SIZE Cards'), so no keyword folding of ours may re-spell it.
        "container",
        "font-palette-values", "page", "export", "part",
    };

    private static string FoldKeywordsInLayer(string text, string[] keywords)
    {
        var words = SplitTopLevelWords(text);
        for (int i = 0; i < words.Count; i++)
            foreach (var keyword in keywords)
                if (words[i].Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    words[i] = keyword;
                    break;
                }
        return string.Join(" ", words);
    }

    private static readonly string[] BorderStyleKeywords =
        { "none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset" };
    private static readonly string[] FontSizeKeywords =
        { "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large",
          "larger", "smaller" };
    private static readonly string[] BorderWidthKeywords = { "thin", "medium", "thick" };
    // CSS UI 4 §3.3: the outline's own style words — the border's without 'hidden' and with the
    // 'auto' the ring brings (measured, and 'AUTO' folds to 'auto' like any other keyword).
    private static readonly string[] OutlineStyleKeywords =
        { "auto", "none", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset",
          "outset" };
    private static readonly string[] OutlineSideKeywords =
        OutlineStyleKeywords.Concat(BorderWidthKeywords).ToArray();
    private static readonly string[] BorderSideKeywords =
        { "none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset",
          "outset", "thin", "medium", "thick" };
    private static readonly string[] AutoKeyword = { "auto" };
    private static readonly string[] SpanAndAutoKeywords = { "span", "auto" };

    /// <summary>The predefined counter styles of CSS Lists 3 §4.3, spelled the one way the engine
    /// spells every enum keyword (see <see cref="Acrux.Core.Dom.CssEnumFormatter"/>), so the list is
    /// derived from the enum and cannot drift from it. 'Symbol', 'String' and 'Custom' are storage
    /// cases of this engine rather than values a page may write, so they are not in it.</summary>
    private static readonly string[] ListStyleTypeKeywords = System.Array.FindAll(
        System.Enum.GetNames<Acrux.Core.Dom.ListStyleType>(),
        static n => n is not ("Symbol" or "String" or "Custom"))
        .Select(Acrux.Core.Dom.CssEnumFormatter.CssKeywordFromEnum)
        .Concat(new[] { "circle", "disc" })
        .ToArray();

    /// <summary>The closed keyword sets of the animation grammar. Everything else an
    /// <c>animation</c> shorthand holds is a keyframe-set name, and that one is case-sensitive:
    /// measured, <c>animation: 3s 2s LINEAR BOGUS</c> reads back with 'linear' folded and 'BOGUS'
    /// untouched, while <c>animation-name: BOGUS</c> keeps both spellings.</summary>
    private static readonly string[] AnimationKeywords =
        { "standard", "ease", "ease-in", "ease-out", "ease-in-out", "linear", "step-start",
          "step-end", "reverse", "alternate", "alternate-reverse", "normal", "none", "forwards",
          "backwards", "both", "running", "paused", "infinite" };

    /// <summary>The keywords a property owns, or null when this layer has no measured set for it.</summary>
    private static string[]? SpecifiedKeywords(string property)
    {
        var name = property.ToLowerInvariant();
        if (name.EndsWith("-style", StringComparison.Ordinal)
            && name.StartsWith("border", StringComparison.Ordinal))
            return BorderStyleKeywords;
        if (name == "outline-style") return OutlineStyleKeywords;
        // A side shorthand carries the style and the width keywords beside its colour, and both
        // print in their one spelling (measured: 'border-top: 1PX SOLID RED' reads
        // '1px solid red').
        if (name == "outline") return OutlineSideKeywords;
        if (name == "border"
            || (name.StartsWith("border-", StringComparison.Ordinal)
                && !name.EndsWith("-color", StringComparison.Ordinal)
                && !name.EndsWith("-width", StringComparison.Ordinal)
                && !name.EndsWith("-style", StringComparison.Ordinal)))
            return BorderSideKeywords;
        if (name == "border-width" || name.EndsWith("-width", StringComparison.Ordinal)
            && name.StartsWith("border", StringComparison.Ordinal))
            return BorderWidthKeywords;
        // 'transition' and 'transition-property' are not listed: every word either of them takes
        // is a keyword or a property name, and both print lower-cased (measured: 'WIDTH 1S' reads
        // back 'width 1s'), which is the bare-ident fold rather than a set of its own.
        if (name is "animation" or "animation-direction" or "animation-fill-mode"
            or "animation-play-state" or "animation-timing-function")
            return AnimationKeywords;
        if (name == "font-size" || name == "font") return FontSizeKeywords;
        // CSS Lists 3 §4.3: a predefined counter style is a keyword and prints in its one spelling,
        // while a name that is not one of them is a custom ident and keeps the author's case
        // (measured: 'DISC' reads 'disc' and 'HEX' reads 'HEX'). The words that name no counter
        // style are left out of the list on purpose — 'Symbol'/'String'/'Custom' are this engine's
        // own storage cases, not values a page may write.
        if (name == "list-style-type") return ListStyleTypeKeywords;
        if (name is "grid-area" or "grid-row" or "grid-column"
            or "grid-row-start" or "grid-row-end" or "grid-column-start" or "grid-column-end")
            return SpanAndAutoKeywords;
        if (name is "margin" or "margin-top" or "margin-right" or "margin-bottom" or "margin-left"
            or "margin-block" or "margin-block-start" or "margin-block-end"
            or "margin-inline" or "margin-inline-start" or "margin-inline-end"
            or "inset" or "inset-block" or "inset-inline" or "inset-block-start" or "inset-block-end"
            or "inset-inline-start" or "inset-inline-end"
            or "top" or "right" or "bottom" or "left"
            or "scroll-margin" or "place-content" or "place-items" or "place-self")
            return AutoKeyword;
        return null;
    }

    /// <summary>A colour written as a function of numbers only — <c>rgb()</c>, <c>hsl()</c>,
    /// <c>hwb()</c> and their legacy aliases. Those are the forms the reference engine folds into
    /// <c>rgb()</c>/<c>rgba()</c>; the newer ones keep their own spelling.</summary>
    private static bool IsPlainColorFunction(string word)
    {
        int open = word.IndexOf('(');
        if (open <= 0) return false;
        var name = word[..open];
        return name.Equals("rgb", StringComparison.OrdinalIgnoreCase)
            || name.Equals("rgba", StringComparison.OrdinalIgnoreCase)
            || name.Equals("hsl", StringComparison.OrdinalIgnoreCase)
            || name.Equals("hsla", StringComparison.OrdinalIgnoreCase)
            || name.Equals("hwb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a colour function derives itself from another colour — <c>hsl(from red
    /// h s l)</c> (CSS Color 4 §13.2.3). The engine has no slot for the origin it would resolve
    /// against, so the reference engine prints the authored form as the specified value and only
    /// resolves it on the computed surface (measured: 'hsl(from red h s l)' reads itself there).</summary>
    private static bool IsRelativeColorSyntax(string word)
    {
        int open = word.IndexOf('(');
        if (open <= 0 || !word.EndsWith(")", StringComparison.Ordinal)) return false;
        var first = SplitTopLevelWords(word[(open + 1)..^1]).FirstOrDefault();
        return first is not null && first.Equals("from", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Split a value into its top-level components: whitespace-separated words, with a
    /// function and its arguments kept in one piece.</summary>
    private static List<string> SplitTopLevelWords(string text)
    {
        var words = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (i > start) words.Add(text[start..i]);
                start = i + 1;
            }
        }
        if (text.Length > start) words.Add(text[start..]);
        return words;
    }

    /// <summary>One family name as the reference engine prints it. A name that is a sequence of
    /// identifiers is one string and is quoted — <c>font-family: My Font</c> reads back
    /// <c>"My Font"</c> — and a name that is written as a string and could have been written as one
    /// identifier loses its quotes: <c>font-family: "FFF"</c> reads back <c>FFF</c> (measured both,
    /// in a style rule and in an '@font-face').</summary>
    private static string FoldFamilyNames(string text)
    {
        var items = SplitTopLevelCommas(text);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i].Trim();
            items[i] = item;
            if (item.Length == 0) continue;
            if (item.Length >= 2 && item[0] is '"' or '\'' && item[^1] == item[0])
            {
                var name = DecodeStringBody(item[1..^1]);
                items[i] = IsBareIdentifier(name) ? name : AsDoubleQuoted(name);
                continue;
            }
            var words = item.Split(new[] { ' ', '\t', '\n', '\r', '\f' },
                StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            bool everyWordIsName = true;
            foreach (var word in words)
                if (!IsBareIdentifier(word)) { everyWordIsName = false; break; }
            if (!everyWordIsName) continue;
            // One identifier is the name itself; several are a name that no parser would find if it
            // were not quoted, because it reads as two values. A generic family is a keyword rather
            // than a name the page invented, so it prints in its one spelling however the author
            // cased it (measured: 'font: 16px SERIF' reads '16px serif' beside a 'font-family: ARIA1'
            // that keeps its case).
            items[i] = words.Length == 1 ? CanonicalGenericFamily(words[0])
                : AsDoubleQuoted(string.Join(" ", words));
        }
        return string.Join(", ", items);
    }

    private static string CanonicalGenericFamily(string name)
    {
        foreach (var generic in GenericFamilies)
            if (name.Equals(generic, StringComparison.OrdinalIgnoreCase)) return generic;
        return name;
    }

    /// <summary>Whether a word is one of the generic family names. The list has one home because
    /// two readers need it: the printer, which says a generic in its one spelling, and the
    /// <c>font</c> shorthand's grammar, which refuses a generic followed by another name
    /// (measured: 'font: 12px serif extra' is dropped while 'font: 12px serif, sans-serif' stands).</summary>
    internal static bool IsGenericFamilyName(string name)
    {
        foreach (var generic in GenericFamilies)
            if (name.Equals(generic, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Lower-case only the generic family keywords of a <c>font</c> shorthand. The whole
    /// value cannot go through the family fold, because its other components — 'bold 16px serif' —
    /// are also bare identifiers and would be quoted into one family name. A generic family is a
    /// keyword, so it prints in its one spelling (measured: 'font: 16px SERIF' reads '16px serif'),
    /// while a name the page invented keeps its case.</summary>
    private static string FoldGenericFamilies(string text)
    {
        var words = SplitTopLevelWords(text);
        for (int i = 0; i < words.Count; i++) words[i] = CanonicalGenericFamily(words[i]);
        return string.Join(" ", words);
    }

    /// <summary>Say the <c>font</c> shorthand in the order its grammar lists its parts, with a
    /// line-height that says nothing left out and the family list printed the way
    /// <c>font-family</c> prints it (CSS Fonts 4 §5.2, measured: 'small-caps bold italic condensed
    /// 12px/1.4 serif' reads 'italic small-caps bold condensed 12px / 1.4 serif', 'normal 12px serif'
    /// reads '12px serif', '12px/normal serif' reads '12px serif', and '12px "Foo", serif' reads
    /// '12px Foo, serif'). A text the shorthand reader refuses is returned untouched, so the
    /// declaration keeps the shape the cascade is about to drop.</summary>
    private static string CanonicalizeFontShorthand(string text)
    {
        var parts = Acrux.Core.Css.ShorthandExpander.ParseFontShorthand(text);
        if (parts == null) return text;
        if (parts.SystemFont != null) return parts.SystemFont;
        var printed = new List<string>(7);
        if (parts.Style != null) printed.Add(parts.Style);
        if (parts.Variant != null) printed.Add(parts.Variant);
        if (parts.Weight != null) printed.Add(parts.Weight);
        if (parts.Stretch != null) printed.Add(parts.Stretch);
        var size = parts.Size;
        if (parts.LineHeight != null
            && !parts.LineHeight.Equals("normal", StringComparison.OrdinalIgnoreCase))
            size += " / " + parts.LineHeight;
        printed.Add(size);
        printed.Add(FoldFamilyNames(parts.Family));
        return string.Join(" ", printed);
    }

    private static readonly string[] GenericFamilies =
        { "serif", "sans-serif", "cursive", "fantasy", "monospace", "system-ui", "ui-serif",
          "ui-sans-serif", "ui-monospace", "ui-rounded", "math", "emoji", "fangsong", "standard",
          "legacy-mac", "legacy-win", "legacy-compat" };

    /// <summary>The value a feature tag takes when nothing is written after it, so a tag written
    /// with its default is printed without it: <c>"liga" 1</c> reads back <c>"liga"</c> while
    /// <c>"liga" 0</c> and <c>"liga" 2</c> keep the number (measured, in a style rule and in an
    /// '@font-face' alike).</summary>
    /// <summary>Say <c>font-feature-settings</c> the way the reference engine prints the value the
    /// page wrote: a feature's default value is not said at all, so 'on' and '1' both leave the
    /// number out and 'off' becomes the '0' it means (measured: '"liga" on' and '"liga" 1' both read
    /// '"liga"', '"liga" off' reads '"liga" 0'). The items keep the order the page used; the computed
    /// surface reads the same helper with its own ordering, so the two cannot disagree.</summary>
    private static string ElideDefaultFeatureValues(string text)
        => Acrux.Core.Css.CssValueGrammar.CanonicalTagList(text,
            integerValues: true, inTagOrder: false) ?? text;

    /// <summary>Split a list at its commas, ignoring the ones inside a quoted string or a
    /// parenthesis.</summary>
    private static List<string> SplitTopLevelCommas(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                int end = i;
                while (end < text.Length)
                {
                    char q = text[end];
                    end++;
                    if (q == '\\' && end < text.Length) end++;
                    else if (q == c) break;
                }
                current.Append(text, i, end - i);
                i = end - 1;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
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

    /// <summary>Whether the text is one identifier: what a family name may be printed as without
    /// quotes, and what a string that begins with a digit or carries a space can never be.</summary>
    private static bool IsBareIdentifier(string text)
    {
        if (text.Length == 0 || char.IsAsciiDigit(text[0])) return false;
        foreach (char c in text)
            if (!IsIdentPart(c)) return false;
        return true;
    }

    /// <summary>The characters of a string token with its escapes undone.</summary>
    private static string DecodeStringBody(string body)
    {
        var raw = new System.Text.StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c != '\\' || i + 1 >= body.Length)
            {
                raw.Append(c);
                continue;
            }
            char next = body[++i];
            if (next is '\\' or '"' or '\'') raw.Append(next);
            else { raw.Append(c); raw.Append(next); }
        }
        return raw.ToString();
    }

    /// <summary>The text as a double-quoted string token.</summary>
    private static string AsDoubleQuoted(string raw)
    {
        var printed = new System.Text.StringBuilder(raw.Length + 2).Append('"');
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c is '"' or '\\') printed.Append('\\');
            printed.Append(c);
        }
        return printed.Append('"').ToString();
    }

    /// <summary>Copies the string token that starts at <paramref name="start"/>, and returns the
    /// index after its closing quote.
    ///
    /// The characters a page escaped are printed as the characters they mean, and the quote the
    /// string was written with is the one the engine writes it back with (measured:
    /// <c>hyphenate-character: "\2014"</c> reads <c>"—"</c>, <c>'x'</c> reads <c>"x"</c> and
    /// <c>quotes: "\201C" "\201D"</c> reads <c>"“" "”"</c> — the same answer the computed surface
    /// gives, because it is the same string). A character that cannot be written bare — a backslash,
    /// the quote itself, or a control character — keeps an escape of its own.</summary>
    private static int CopyQuoted(string text, int start, System.Text.StringBuilder into)
    {
        char quote = text[start];
        int i = start + 1;
        var raw = new StringBuilder();
        while (i < text.Length)
        {
            char c = text[i];
            i++;
            if (c == '\\' && i < text.Length)
            {
                raw.Append('\\');
                raw.Append(text[i]);
                i++;
                continue;
            }
            if (c == quote) break;
            raw.Append(c);
        }
        into.Append('"');
        foreach (var c in Acrux.Core.Css.Resolver.CssPropertyApplier.UnescapeCssString(raw.ToString()))
        {
            if (c == '\\' || c == '"') { into.Append('\\').Append(c); continue; }
            if (c < 0x20 || c == 0x7f)
            {
                // A control character is written as the hex escape the reference engine writes:
                // its digits end with a space, which is part of the escape and not of the text.
                into.Append('\\').Append(((int) c).ToString("x")).Append(' ');
                continue;
            }
            into.Append(c);
        }
        into.Append('"');
        return i;
    }

    /// <summary>Index of the ')' matching the '(' at <paramref name="open"/>, or -1. Quotes are
    /// skipped so a quoted parenthesis cannot end a url early.</summary>
    private static int MatchingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                int end = i;
                while (end < text.Length)
                {
                    char q = text[end];
                    end++;
                    if (q == '\\' && end < text.Length) end++;
                    else if (q == c) break;
                }
                i = end - 1;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>The functions whose one argument is a string and is printed as one: an address
    /// written <c>url(a.png)</c>, <c>URL( a.png )</c> or <c>url('a.png')</c> reads back
    /// <c>url("a.png")</c> from the reference engine — the name in lower case, the argument in
    /// double quotes, and the whitespace an unquoted address may stand around itself gone
    /// (measured). The font source spells its family name and its format the same way, and an
    /// empty address still prints its quotes: <c>url()</c> is a call that names nothing rather
    /// than no call at all.</summary>
    private static bool IsQuotedArgument(string word) =>
        word.Equals("url", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("local", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("format", StringComparison.OrdinalIgnoreCase);

    /// <summary>The text between <paramref name="from"/> and <paramref name="to"/> re-written as a
    /// double-quoted string. Its escapes are decoded first and re-encoded after, so a single-quoted
    /// argument loses the escapes only its own quote needed and a bare one gains the escapes a
    /// string requires.</summary>
    private static string QuotedArgument(string text, int from, int to)
    {
        var inner = text.Substring(from, to - from).Trim();
        bool quoted = inner.Length >= 2 && (inner[0] is '"' or '\'') && inner[^1] == inner[0];
        var body = quoted ? inner[1..^1] : inner;

        var raw = new System.Text.StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c != '\\' || i + 1 >= body.Length)
            {
                raw.Append(c);
                continue;
            }
            char next = body[++i];
            if (next is '\\' or '"' or '\'') raw.Append(next);
            else { raw.Append(c); raw.Append(next); }
        }

        var printed = new System.Text.StringBuilder(raw.Length + 2).Append('"');
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c is '"' or '\\') printed.Append('\\');
            printed.Append(c);
        }
        return printed.Append('"').ToString();
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    /// <summary>Whether a character at the end of the printed text already separates the component
    /// that follows it, so a sign written next is that component's own and not a sum's operator.</summary>
    private static bool IsComponentSeparator(char c) =>
        IsWhitespace(c) || c is '(' or ',' or '*' or '/';

    private static bool IsIdentStart(char c) =>
        char.IsLetter(c) || c is '_' or '-' || c > '\u007F';

    private static bool IsIdentPart(char c) => IsIdentStart(c) || char.IsAsciiDigit(c);
}
