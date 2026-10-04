using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// Represents a generated CSS quote ("content: open-quote | close-quote | ...").
/// Mirrors layout_quote.cc. Always anonymous.
/// </summary>
public class LayoutQuote
{
    public QuoteType Type { get; }
    public string Text { get; private set; } = "";

    public LayoutQuote(QuoteType type)
    {
        Type = type;
    }

    public void UpdateText(string text)
    {
        Text = text;
    }

    /// <summary>
    /// Resolves the quote character for the given nesting depth.
    /// Mirrors the quotes data resolution in layout_quote.cc.
    /// </summary>
    public static string ResolveQuote(QuoteType type, int depth, string? quotesData)
    {
        // Default quotes pairs per CSS spec
        string[] defaults = { "\u201C", "\u201D", "\u2018", "\u2019" }; // " " ' '
        string[] reversed = { "\u201D", "\u201C", "\u2019", "\u2018" };

        bool isOpen = type == QuoteType.OpenQuote || type == QuoteType.NoOpenQuote;
        bool isNoQuote = type == QuoteType.NoOpenQuote || type == QuoteType.NoCloseQuote;
        if (isNoQuote) return "";

        var pairs = ParseQuotes(quotesData);
        if (pairs.Count == 0)
        {
            pairs.Add(("\u201C", "\u201D"));
            pairs.Add(("\u2018", "\u2019"));
        }

        int index = Math.Min(depth, pairs.Count - 1);
        var (open, close) = pairs[index];
        return isOpen ? open : close;
    }

    private static List<(string open, string close)> ParseQuotes(string? data)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrEmpty(data) || data == "none" || data == "auto")
            return result;

        var parts = data.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            result.Add((Unescape(parts[i]), Unescape(parts[i + 1])));
        }
        return result;
    }

    private static string Unescape(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1];
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
            return s[1..^1];
        return s;
    }
}

public enum QuoteType
{
    OpenQuote,
    CloseQuote,
    NoOpenQuote,
    NoCloseQuote,
}

/// <summary>
/// Manages quote nesting depth for generated quotes.
/// </summary>
public class QuoteManager
{
    private int _depth;

    public int Depth => Math.Max(0, _depth);

    public void PushQuote()
    {
        _depth++;
    }

    public void PopQuote()
    {
        _depth = Math.Max(0, _depth - 1);
    }

    public void Reset()
    {
        _depth = 0;
    }
}