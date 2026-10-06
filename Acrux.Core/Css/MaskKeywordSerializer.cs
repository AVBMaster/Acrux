namespace Acrux.Core.Css;

/// <summary>
/// Serialization of the 'mask-position' / 'mask-repeat' computed values. The mask longhands are
/// kept as authored strings until the geometry side consumes them (see the mask placement gap),
/// but a computed value must still read the way the reference engine reports it: keywords are
/// resolved to the percentage of the positioning area they mean, an omitted axis takes its
/// 'center', and two identical repeat axes collapse to one keyword.
/// Measured against the reference engine: 'right bottom' → '100% 100%', 'center' → '50% 50%',
/// 'round round' → 'round', nothing authored → '0% 0%' and 'repeat'.
/// </summary>
public static class MaskKeywordSerializer
{
    private static string AxisValue(string token) => token.ToLowerInvariant() switch
    {
        "left" or "top" => "0%",
        "right" or "bottom" => "100%",
        "center" => "50%",
        var other => other,
    };

    private static bool IsVertical(string token) =>
        token.Equals("top", StringComparison.OrdinalIgnoreCase)
        || token.Equals("bottom", StringComparison.OrdinalIgnoreCase);

    private static bool IsHorizontal(string token) =>
        token.Equals("left", StringComparison.OrdinalIgnoreCase)
        || token.Equals("right", StringComparison.OrdinalIgnoreCase);

    public static string Position(string? authored)
    {
        var tokens = (authored ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return "0% 0%";
        if (tokens.Length == 1)
        {
            string only = AxisValue(tokens[0]);
            // A lone vertical keyword leaves the horizontal axis centred; anything else
            // (a horizontal keyword, a length, a percentage) leaves the vertical one centred.
            return IsVertical(tokens[0]) ? $"50% {only}" : $"{only} 50%";
        }
        // The grammar allows the two axes in either order.
        string x = AxisValue(tokens[0]), y = AxisValue(tokens[1]);
        if (IsVertical(tokens[0]) && !IsVertical(tokens[1])) (x, y) = (y, x);
        return $"{x} {y}";
    }

    public static string Repeat(string? authored)
    {
        var tokens = (authored ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return "repeat";
        if (tokens.Length == 1) return tokens[0].ToLowerInvariant();
        string x = tokens[0].ToLowerInvariant(), y = tokens[1].ToLowerInvariant();
        return x == y ? x : $"{x} {y}";
    }
}
