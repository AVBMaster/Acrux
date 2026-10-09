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
            or CssPropertyId.Background or CssPropertyId.Mask
            => static text => !NamesAFontLocal(text),
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
        // CSS Motion Path 1 §3: a path, a ray, an image, one of the basic shapes, or none, with the
        // reference box it resolves against written in front. A function this grammar has no reading
        // for is no path (measured: 'offset-path: bogus("M 0 0")' is not a declaration while
        // 'offset-path: path("M 0 0")' is), which is the difference between naming a shape and
        // naming anything at all.
        CssPropertyId.OffsetPath => IsOffsetPathValue,
        // CSS Values 4 §11.2 as the reference engine ships it: the two keywords, either alone or
        // together. A third spelling is nothing ('interpolate-size: numeric-keywords', measured).
        CssPropertyId.InterpolateSize
            => static text => IsOneOf(text, "numeric-only")
                || IsOneOf(text, "allow-keywords")
                || (text.Equals("numeric-only allow-keywords", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("allow-keywords numeric-only", StringComparison.OrdinalIgnoreCase)),
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

    /// <summary>The motion path's own set of shapes. The box keyword is a prefix, not the path, so
    /// it is taken off before the shape is judged and one shape has to be left behind it.</summary>
    private static bool IsOffsetPathValue(string text)
    {
        if (IsOneOf(text, "none", "normal")) return true;
        var parts = SplitComponents(text);
        if (parts.Count == 0) return false;
        if (parts.Count > 1 && IsOneOf(parts[0], "content-box", "border-box", "padding-box",
                "fill-box", "stroke-box", "view-box"))
            parts = parts.Skip(1).ToList();
        if (parts.Count != 1) return false;
        string shape = parts[0];
        foreach (var name in new[] { "path", "ray", "circle", "ellipse", "inset", "rect", "xywh", "url" })
            if (shape.StartsWith(name + "(", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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
                    if (open <= 0 || !TimingFunctions.Contains(part[..open].ToLowerInvariant())) return false;
                    continue;
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
