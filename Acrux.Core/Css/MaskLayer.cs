namespace Acrux.Core.Css;

/// <summary>One layer of a 'mask' / 'mask-image' value, split into the parts the grammar
/// allows in any order (CSS Masking 1 §5.1).</summary>
public sealed class MaskLayer
{
    /// <summary>The mask source: url(), a gradient function or 'none'. Joined when a layer
    /// names several images, which the grammar does not allow but the engine has seen.</summary>
    public string Image = "none";
    /// <summary>Null means the part was not named in this layer, so the corresponding
    /// longhand's own value applies.</summary>
    public string? Position;
    public string? Size;
    public string? Repeat;
    public string? Origin;
    public string? Clip;
    public string? Composite;
    public string? Mode;
}

/// <summary>
/// The single place a 'mask' layer list is decomposed. Both the cascade (which turns the
/// shorthand into longhands) and the painter (which needs each layer's own geometry) call
/// this, so the two can never disagree about what a layer said — the failure shape this
/// codebase has hit repeatedly with hand-maintained parallel lists.
/// </summary>
public static class MaskLayerParser
{
    private static readonly string[] GeometryBoxes =
        { "border-box", "padding-box", "content-box", "fill-box", "stroke-box", "view-box" };

    private static readonly string[] RepeatKeywords =
        { "repeat", "repeat-x", "repeat-y", "no-repeat", "space", "round" };

    private static readonly string[] CompositeKeywords =
        { "add", "subtract", "intersect", "exclude" };

    private static readonly string[] ModeKeywords =
        { "match-source", "alpha", "luminance" };

    public static List<MaskLayer> Parse(string? value)
    {
        var layers = new List<MaskLayer>();
        foreach (var text in SplitTopLevel(value ?? string.Empty, ','))
            layers.Add(ParseLayer(text));
        return layers;
    }

    private static MaskLayer ParseLayer(string layerText)
    {
        var layer = new MaskLayer();
        var tokens = SplitTokensPreservingFunctions(layerText);
        var geometry = new List<string>();
        bool seenBox = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var lower = token.ToLowerInvariant();

            if (IsImageToken(lower))
            {
                layer.Image = layer.Image == "none" || layer.Image.Length == 0 ? token : layer.Image + " " + token;
                continue;
            }
            if (lower == "no-clip") { layer.Clip = "no-clip"; continue; }
            if (GeometryBoxes.Contains(lower))
            {
                // One box names both origin and clip; a second one fills whichever is still
                // unset (CSS Masking 1 §5.1 — measured, and asymmetric with 'mask-clip' alone).
                if (!seenBox) { layer.Origin = lower; layer.Clip = lower; seenBox = true; }
                else if (layer.Clip == null || layer.Clip == "no-clip") layer.Clip = lower;
                else layer.Origin = lower;
                continue;
            }
            if (CompositeKeywords.Contains(lower)) { layer.Composite ??= lower; continue; }
            if (ModeKeywords.Contains(lower)) { layer.Mode ??= lower; continue; }
            if (RepeatKeywords.Contains(lower))
            {
                // 'repeat-x'/'repeat-y' stand alone; otherwise a second keyword is the other axis.
                if (lower is "repeat-x" or "repeat-y") { layer.Repeat ??= lower; continue; }
                string x = lower;
                if (i + 1 < tokens.Count && RepeatKeywords.Contains(tokens[i + 1].ToLowerInvariant()))
                { x += " " + tokens[i + 1].ToLowerInvariant(); i++; }
                layer.Repeat ??= x;
                continue;
            }
            if (lower == "/") { geometry.Add("/"); continue; }
            geometry.Add(token);
        }

        AssignPositionAndSize(layer, geometry);
        if (layer.Image.Length == 0) layer.Image = "none";
        return layer;
    }

    /// <summary>Whatever is left is the `<position> [ / <bg-size> ]?` tail: tokens before the
    /// slash are the position, tokens after it are the size.</summary>
    private static void AssignPositionAndSize(MaskLayer layer, List<string> geometry)
    {
        int slash = geometry.IndexOf("/");
        var position = slash >= 0 ? geometry.Take(slash).ToList() : geometry;
        var size = slash >= 0 ? geometry.Skip(slash + 1).ToList() : Enumerable.Empty<string>();

        if (position.Count > 0) layer.Position = string.Join(" ", position);
        if (size.Count() > 0) layer.Size = string.Join(" ", size);
    }

    /// <summary>'url(...)' and any gradient function name the mask source; 'src(...)' does not,
    /// and neither does a bare keyword.</summary>
    private static bool IsImageToken(string lower) =>
        lower.StartsWith("url(", StringComparison.Ordinal)
        || (lower.Contains("gradient(") && !lower.StartsWith("src(", StringComparison.Ordinal));

    /// <summary>Split on whitespace at function depth zero, so url(a b.png) stays one token.</summary>
    public static List<string> SplitTokensPreservingFunctions(string text)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        foreach (var c in text)
        {
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            if (depth == 0 && c == '/')
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                tokens.Add("/");
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>Split on a separator that is not inside (...) or "...".</summary>
    public static List<string> SplitTopLevel(string value, char separator)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        char quote = '\0';
        foreach (var c in value)
        {
            if (quote != '\0') { if (c == quote) quote = '\0'; sb.Append(c); continue; }
            if (c == '"' || c == '\'') { quote = c; sb.Append(c); continue; }
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            if (c == separator && depth == 0) { parts.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString().Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }
}
