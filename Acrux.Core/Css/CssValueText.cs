using System;
using System.Globalization;

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

        // '-webkit-mask-box-image-slice' prints its numbers ahead of its 'fill' flag, whichever end
        // of the list the author put the flag on: 'fill 3' reads back as '3 fill' (measured).
        if (property != null
            && property.Equals("-webkit-mask-box-image-slice", StringComparison.OrdinalIgnoreCase))
            value = MaskBoxImageShorthand.NormalizeSlice(value);

        // Whether a bare 0 in this value is a length. Known by the property, not by the text:
        // 'columns: 0' is a width and prints '0px', 'scale: 0' is a factor and prints '0'.
        bool lengths = property != null && Resolver.CssPropertyTraits.TakesLength(property);

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
                if (c == '+' || c == '-') i++;
                while (i < value.Length && (char.IsAsciiDigit(value[i]) || value[i] == '.')) i++;
                while (i < value.Length && (IsIdentPart(value[i]) || value[i] == '%')) i++;
                var token = value.Substring(start, i - start);
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                if (depth == 0 && !afterSlash && lengths && token == "0") token = "0px";
                // A unit is a keyword and CSS keywords are ASCII case-insensitive, so a reference
                // engine prints the one spelling of it: 'border-top: 1PX solid red' and
                // 'margin: 10Px auto' both read back with a lower-case unit (measured).
                sb.Append(FoldNumberAndUnit(token));
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
                result = FoldKeywordCase(result, property);
                if (property.Equals("font", StringComparison.OrdinalIgnoreCase))
                    result = FoldGenericFamilies(result);
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
                    result = CanonicalizeCounterList(result) ?? result;
                result = PutColorFirst(result, property);
                result = CanonicalizeTransition(result, property);
                result = OmitInitialBorderParts(result, property);
                result = CollapseBoxShorthandParts(result, property);
                result = CanonicalizeFlexFlow(result, property);
                result = CanonicalizeTextDecoration(result, property);
                result = CanonicalizeColumnRule(result, property);
                result = CanonicalizeListStyle(result, property);
                // The properties that only have to be said correctly — scroll snapping, the SVG
                // keyword family, the rest of the font-variant group — print what their one table
                // says, so a sheet and a CSSOM write cannot disagree about the order of a keyword
                // list the engine re-sorts ('font-variant-east-asian: full-width jis04').
                if (Acrux.Core.Css.Resolver.EchoedProperties.Normalize(property, result) is { } echoed)
                    result = echoed;
            }
        }
        return result;
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
                bool firstIsLength = char.IsDigit(words[0][0]) || words[0][0] is '+' or '-';
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
            // 'outline: 2px dashed rgb(1, 2, 3)' reads back in that order).
            var style = rest.FirstOrDefault(w =>
                Acrux.Core.Css.ShorthandExpander.IsBorderStyle(w));
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
        if (length == null && hanging == null && eachLine == null) return null;
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
        var sides = SplitTopLevel(trimmed, '/').ToList();
        string a, b;
        if (sides.Count == 1)
        {
            if (!double.TryParse(sides[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var single) || single <= 0)
                return null;
            a = sides[0].Trim(); b = "1";
        }
        else
        {
            if (sides.Count != 2) return null;
            a = sides[0].Trim(); b = sides[1].Trim();
            if (!PositiveNumber(a) || !PositiveNumber(b)) return null;
        }
        var ratio = a + " / " + b;
        return auto == null ? ratio : auto + " " + ratio;
    }

    private static bool PositiveNumber(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0;

    /// <summary>
    /// Print <c>counter-reset</c> and <c>counter-set</c>: every counter says its integer, whether
    /// or not the page wrote one (measured: 'c' reads 'c 0', and 'c reverse' reads
    /// 'c 0 reverse 0' because 'reverse' is a counter name and not a keyword here). A repeated
    /// name is not folded away — the engine prints the pairs in the order they were written.
    /// </summary>
    public static string? CanonicalizeCounterList(string text)
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
            string value = "0";
            if (i + 1 < words.Count && ShorthandExpander.IsNumberToken(words[i + 1]))
            {
                value = words[i + 1];
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
        foreach (var word in SplitTopLevelWords(text))
        {
            var p = word.ToLowerInvariant();
            if (p is "inside" or "outside") { position = p; continue; }
            if (p.StartsWith("url(", StringComparison.Ordinal)
                || p.StartsWith("image-set(", StringComparison.Ordinal)
                || (p.StartsWith("linear-gradient(", StringComparison.Ordinal)))
            { image = word; continue; }
            // 'none' is both a position's and an image's word and a type of its own; the first one
            // seen goes to the image, which is where the engine prints it ('outside none disc').
            if (p == "none" && image == null && position != null) { image = p; continue; }
            type ??= word;
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
    private static string FoldNumberAndUnit(string token)
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
        return CanonicalNumber(value) + unit;
    }

    /// <summary>A number printed shortest-round-trip and never in exponent notation: the form the
    /// reference engine writes into a specified value.</summary>
    private static string CanonicalNumber(double value)
    {
        if (value == 0) value = 0;   // folds the negative zero that '-0px' carries
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.Contains('E') || text.Contains('e'))
            text = value.ToString("0.##########", CultureInfo.InvariantCulture);
        return text;
    }

    /// <summary>Rewrite each colour a colour-valued property carries. A named colour prints as its
    /// own lower-case name; anything else — a hex, an <c>hsl()</c>, a modern <c>rgb()</c> written
    /// with spaces or a slash — prints as the canonical <c>rgb()</c>/<c>rgba()</c> the engine
    /// parses it to (measured over 33 spellings: '#F00' and 'hsl(0,100%,50%)' both read
    /// 'rgb(255, 0, 0)', 'rgb(1 2 3 / 0.5)' reads 'rgba(1, 2, 3, 0.5)', 'TRANSPARENT' reads
    /// 'transparent', and 'currentColor' reads 'currentcolor' because it names no colour yet).</summary>
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
                if (ColorParser.IsColorToken(word) && IsPlainColorFunction(word))
                    words[i] = CssColorText.FromColor(ColorParser.Parse(word));
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
        return lowered + "(" + inner + ")";
    }

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
                part[7].Length > 0 ? part[7] : "none",
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
        // The first component's own initial is half, and only that one goes unsaid.
        if (firstShare >= 0 && Math.Abs(firstShare - 50f) < 0.0001f) firstShare = -1;
        args[1] = first + (firstShare >= 0 ? " " + FormatShare(firstShare) : string.Empty);
        var writtenFirst = firstShare >= 0 ? firstShare : 50f;
        var second = TakePercentageOff(args[2], out float secondShare);
        // The second share is implied by the first, and a written one that says the same thing
        // adds nothing to print.
        if (secondShare >= 0 && Math.Abs(secondShare - (100f - writtenFirst)) < 0.0001f) secondShare = -1;
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
        { "url", "src", "local", "format", "domain", "regexp", "prefix", "image-set" };

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
        "timeline-scope", "scroll-timeline-name", "view-timeline-name", "container-name",
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
    private static readonly string[] BorderSideKeywords =
        { "none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset",
          "outset", "thin", "medium", "thick" };
    private static readonly string[] AutoKeyword = { "auto" };
    private static readonly string[] SpanAndAutoKeywords = { "span", "auto" };

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
            && (name.StartsWith("border", StringComparison.Ordinal) || name == "outline-style"))
            return BorderStyleKeywords;
        // A side shorthand carries the style and the width keywords beside its colour, and both
        // print in their one spelling (measured: 'border-top: 1PX SOLID RED' reads
        // '1px solid red').
        if (name == "border" || name == "outline"
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

    private static readonly string[] GenericFamilies =
        { "serif", "sans-serif", "cursive", "fantasy", "monospace", "system-ui", "ui-serif",
          "ui-sans-serif", "ui-monospace", "ui-rounded", "math", "emoji", "fangsong", "standard",
          "legacy-mac", "legacy-win", "legacy-compat" };

    /// <summary>The value a feature tag takes when nothing is written after it, so a tag written
    /// with its default is printed without it: <c>"liga" 1</c> reads back <c>"liga"</c> while
    /// <c>"liga" 0</c> and <c>"liga" 2</c> keep the number (measured, in a style rule and in an
    /// '@font-face' alike).</summary>
    private static string ElideDefaultFeatureValues(string text)
    {
        var items = SplitTopLevelCommas(text);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i].Trim();
            items[i] = item;
            if (item.Length == 0) continue;
            // The tag is the string and the number is the only thing that may follow it.
            int space = item.LastIndexOf(' ');
            if (space <= 0 || item[(space + 1)..] != "1") continue;
            var tag = item[..space].TrimEnd();
            if (tag.Length >= 2 && tag[0] is '"' or '\'' && tag[^1] == tag[0]) items[i] = tag;
        }
        return string.Join(", ", items);
    }

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

    /// <summary>Copies the string token that starts at <paramref name="start"/> (quotes and
    /// escapes included) and returns the index after its closing quote.</summary>
    private static int CopyQuoted(string text, int start, System.Text.StringBuilder into)
    {
        char quote = text[start];
        into.Append(quote);
        int i = start + 1;
        while (i < text.Length)
        {
            char c = text[i];
            into.Append(c);
            i++;
            if (c == '\\' && i < text.Length) { into.Append(text[i]); i++; continue; }
            if (c == quote) break;
        }
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

    private static bool IsIdentStart(char c) =>
        char.IsLetter(c) || c is '_' or '-' || c > '\u007F';

    private static bool IsIdentPart(char c) => IsIdentStart(c) || char.IsAsciiDigit(c);
}
