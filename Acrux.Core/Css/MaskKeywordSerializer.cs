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

    /// <summary>
    /// A mask layer list as the reference engine reports it: expanded to one entry per mask
    /// IMAGE, cycling each property's own list independently (CSS Backgrounds 3 §2, which
    /// 'mask' inherits). Measured on a three-image mask: 'mask-size: 50% 50%, 20px' →
    /// '50% 50%, 20px, 50% 50%', and a single 'mask-origin: padding-box' → 'padding-box,
    /// padding-box, padding-box'. Without this expansion a script reading a multi-layer mask
    /// cannot line the lists up with the images at all.
    /// </summary>
    public static string LayerList(System.Collections.Generic.IReadOnlyList<string>? layers,
        string? scalar, int imageCount, Func<string, string>? normalize = null)
    {
        var source = layers is { Count: > 0 }
            ? layers
            : string.IsNullOrEmpty(scalar) ? null : new[] { scalar };
        if (source == null) return string.Empty;
        if (imageCount < 1) imageCount = source.Count;

        var parts = new string[imageCount];
        for (int i = 0; i < imageCount; i++)
        {
            var entry = source[i % source.Count];
            parts[i] = normalize != null ? normalize(entry) : entry;
        }
        return string.Join(", ", parts);
    }

}
