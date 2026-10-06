using SkiaSharp;

namespace Acrux.Core.Css;

/// <summary>
/// Rewrites the colours inside an image list ('background-image', 'mask-image', and anything
/// else that takes a &lt;image&gt;#) into the canonical rgb()/rgba() form the reference engine
/// reports. Measured: 'linear-gradient(red, rgba(0 0 0 / 50%), #00f)' computes as
/// 'linear-gradient(rgb(255, 0, 0), rgba(0, 0, 0, 0.5), rgb(0, 0, 255))', and 'transparent'
/// becomes 'rgba(0, 0, 0, 0)' rather than staying a keyword — inside a gradient the reference
/// engine always spells colours as functions, even though 'color: transparent' is reported as
/// 'rgba(0, 0, 0, 0)' too.
///
/// Only tokens that are genuinely colours are touched: a gradient argument can also hold a
/// direction ('to right'), a shape ('circle'), a position ('at 10px 20px') or a stop length
/// ('30%'), and ColorParser answers a zero-alpha colour for any word it does not know, so the
/// membership test has to come first.
/// </summary>
public static class ImageValueCanonicalizer
{
    public static string Canonicalize(string? imageList)
    {
        if (string.IsNullOrEmpty(imageList)) return imageList ?? string.Empty;
        var layers = MaskLayerParser.SplitTopLevel(imageList, ',');
        if (layers.Count == 0) return imageList;
        for (int i = 0; i < layers.Count; i++)
            if (layers[i].Contains("gradient", StringComparison.OrdinalIgnoreCase))
                layers[i] = CanonicalizeGradient(layers[i]);
        return string.Join(", ", layers);
    }

    private static string CanonicalizeGradient(string layer)
    {
        int open = layer.IndexOf('(');
        if (open < 0) return layer;
        int close = MatchingParen(layer, open);
        if (close <= open) return layer;
        var head = layer[..(open + 1)];
        var args = layer[(open + 1)..close];
        var tail = layer[close..];
        return head + CanonicalizeArguments(args) + tail;
    }

    private static string CanonicalizeArguments(string args)
    {
        var parts = MaskLayerParser.SplitTopLevel(args, ',');
        if (parts.Count == 0) return args;
        for (int i = 0; i < parts.Count; i++)
            parts[i] = CanonicalizeColorAtStart(parts[i]);
        return string.Join(", ", parts);
    }

    /// <summary>Replace the leading colour of one gradient argument, keeping whatever follows
    /// it (a stop position such as '30%', or nothing at all).</summary>
    private static string CanonicalizeColorAtStart(string argument)
    {
        var text = argument.TrimStart();
        int leadingSpace = argument.Length - text.Length;
        if (text.Length == 0) return argument;

        string colorToken;
        string rest;
        int paren = text.IndexOf('(');
        int space = IndexOutsideParens(text, ' ');
        bool functionIsFirst = paren >= 0 && (space < 0 || paren < space);

        if (functionIsFirst)
        {
            int close = MatchingParen(text, paren);
            if (close < 0) return argument;
            colorToken = text[..(close + 1)];
            rest = text[(close + 1)..];
        }
        else
        {
            colorToken = space < 0 ? text : text[..space];
            rest = space < 0 ? string.Empty : text[space..];
        }

        if (!ColorParser.IsColorToken(colorToken)) return argument;
        // 'currentcolor' depends on the element's own color, which this layer does not have;
        // leaving it spelled as the keyword is honest, resolving it here would invent a value.
        if (colorToken.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)) return argument;

        var color = ColorParser.Parse(colorToken);
        return new string(' ', leadingSpace) + Format(color) + rest;
    }

    private static string Format(SKColor c) =>
        c.Alpha == 255
            ? $"rgb({c.Red}, {c.Green}, {c.Blue})"
            : $"rgba({c.Red}, {c.Green}, {c.Blue}, {Dom.Animations.CssValueTokenizer.AlphaText(c.Alpha)})";

    /// <summary>Index of the matching ')' for the '(' at <paramref name="open"/>, or -1.</summary>
    private static int MatchingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static int IndexOutsideParens(string text, char needle)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && text[i] == needle) return i;
        }
        return -1;
    }
}
