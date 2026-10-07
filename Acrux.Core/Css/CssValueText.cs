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
/// quoted string and a <c>url()</c> are copied verbatim, because inside them the spaces and
/// commas are data, not syntax; a value that carries a <c>var()</c> reference is left exactly
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
                if (word.Equals("url", StringComparison.OrdinalIgnoreCase)
                    && i < value.Length && value[i] == '(')
                {
                    int end = MatchingParen(value, i);
                    if (end > i)
                    {
                        if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                        sb.Append(value, start, end - start + 1);
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
        return sb.ToString().TrimEnd();
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

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static bool IsIdentStart(char c) =>
        char.IsLetter(c) || c is '_' or '-' || c > '\u007F';

    private static bool IsIdentPart(char c) => IsIdentStart(c) || char.IsAsciiDigit(c);
}
