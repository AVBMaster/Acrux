using System;

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
            if (IsIdentStart(c))
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
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < value.Length && char.IsAsciiDigit(value[i + 1])))
            {
                // Read the whole number, unit included: '0px' is one token, and printing it as
                // '0' plus 'px' would leave the decision about the unit to nobody.
                int start = i;
                while (i < value.Length && (char.IsAsciiDigit(value[i]) || value[i] == '.')) i++;
                while (i < value.Length && (IsIdentPart(value[i]) || value[i] == '%')) i++;
                var token = value.Substring(start, i - start);
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(depth == 0 && !afterSlash && lengths && token == "0" ? "0px" : token);
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
        }
        return result;
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
            // were not quoted, because it reads as two values.
            items[i] = words.Length == 1 ? words[0] : AsDoubleQuoted(string.Join(" ", words));
        }
        return string.Join(", ", items);
    }

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
