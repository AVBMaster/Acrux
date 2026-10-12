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

    /// <summary>The keywords the position and the size each take on their own — the edge words of
    /// one &lt;position&gt; and the three spellings of a &lt;bg-size&gt;.</summary>
    private static readonly string[] PositionOrSizeWords =
        { "left", "right", "center", "top", "bottom", "auto", "cover", "contain" };

    public static List<MaskLayer> Parse(string? value)
    {
        var layers = new List<MaskLayer>();
        foreach (var text in SplitTopLevel(value ?? string.Empty, ','))
            layers.Add(ParseLayer(text));
        return layers;
    }

    /// <summary>Whether every token of a layer list is something the mask grammar takes. A
    /// <c>&lt;color&gt;</c> is the one thing a mask layer never takes (measured: 'mask: red' and
    /// 'mask: url(a.png) red' are both refused, where the background shorthand of the same shape
    /// keeps a colour), and so is any word that is not a box, a composite, a mode, a repeat, an
    /// image, a position term or a size term. The list is also counted: two composites, two modes,
    /// three geometry boxes, or a <c>&lt;bg-size&gt;</c> whose position is missing are each no value
    /// (all measured: 'url(a.png) add exclude', 'url(a.png) luminance alpha',
    /// 'url(a.png) border-box content-box padding-box' and 'url(a.png) / cover' all read back
    /// empty). The reference engine drops the whole declaration for one such word, which is what
    /// this gate is for.</summary>
    public static bool IsMaskLayerList(string? value)
    {
        var layers = SplitTopLevel(value ?? string.Empty, ',');
        if (layers.Count == 0) return false;
        foreach (var text in layers)
        {
            var tokens = SplitTokensPreservingFunctions(text);
            if (tokens.Count == 0) return false;
            int boxes = 0, modes = 0, composites = 0, noClips = 0, images = 0;
            int positionWords = 0, sizeWords = 0, before = 0;
            bool afterSlash = false;
            foreach (var token in tokens)
            {
                var lower = token.ToLowerInvariant();
                if (lower == "/")
                {
                    // One slash, and it has to have a position in front of it: a size with no
                    // <position> before it is not the '<position> [ / <bg-size> ]?' the grammar has.
                    if (afterSlash || before == 0 || positionWords == 0) return false;
                    afterSlash = true;
                    continue;
                }
                if (GeometryBoxes.Contains(lower)) { if (++boxes > 2) return false; }
                else if (CompositeKeywords.Contains(lower)) { if (++composites > 1) return false; }
                else if (ModeKeywords.Contains(lower)) { if (++modes > 1) return false; }
                else if (lower == "no-clip") { if (++noClips > 1) return false; }
                else if (IsImageToken(lower)) { images++; }
                else if (RepeatKeywords.Contains(lower)) { /* the pair is read by the parser */ }
                else if (PositionOrSizeWords.Contains(lower) || IsLengthOrPercentageTerm(lower))
                {
                    if (afterSlash) sizeWords++; else positionWords++;
                }
                else return false;
                before++;
            }
            // Two images in one layer is the shape 'mask: url(a), url(b)' written without its
            // commas; the parser joins them and the reference engine has no reading for it.
            if (images > 1 || (afterSlash && sizeWords == 0) || sizeWords > 2 || positionWords > 4)
                return false;
        }
        return true;
    }

    /// <summary>A position or size term: a &lt;length&gt;, a &lt;percentage&gt;, or a math function
    /// carrying one. A bare number is neither (only zero stands for the length it means).</summary>
    private static bool IsLengthOrPercentageTerm(string token)
    {
        if (token.StartsWith("calc(", StringComparison.Ordinal)
            || token.StartsWith("min(", StringComparison.Ordinal)
            || token.StartsWith("max(", StringComparison.Ordinal)
            || token.StartsWith("clamp(", StringComparison.Ordinal)) return true;
        if (token.EndsWith("%", StringComparison.Ordinal))
            return double.TryParse(token[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        int i = 0;
        if (i < token.Length && token[i] is '+' or '-') i++;
        int digits = i;
        while (i < token.Length && (char.IsAsciiDigit(token[i]) || token[i] == '.')) i++;
        if (i == digits) return false;
        // A unit of its own is what separates the '10px' the grammar takes from the '10' it does
        // not — only a bare zero stands for a length, and it is spelled '0' here.
        var unit = token[i..];
        if (unit.Length == 0) return token == "0" || token == "+0" || token == "-0";
        return char.IsLetter(unit[0]) || unit[0] == '%';
    }

    /// <summary>Whether a &lt;repeat-style&gt; says nothing: the initial is 'repeat' for one axis and
    /// 'repeat repeat' for two, and a mask layer that only repeats is printed without the word at
    /// all (measured: 'mask: url(a.png) repeat repeat' reads 'url("a.png")', where the background
    /// shorthand of the same shape keeps a written 'repeat').</summary>
    public static bool RepeatIsInitial(string? repeat)
    {
        if (string.IsNullOrEmpty(repeat)) return true;
        var words = repeat!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(w => w.Equals("repeat", StringComparison.OrdinalIgnoreCase));
    }

    private static MaskLayer ParseLayer(string layerText)
    {
        var layer = new MaskLayer();
        var tokens = SplitTokensPreservingFunctions(layerText);
        var geometry = new List<string>();

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
                // The boxes are positional, exactly as 'background' reads them (CSS Masking 1
                // §5.10): the first names the origin and the last names the clip, so one box is
                // given to BOTH. A 'no-clip' before them has already spoken for the clip, and a
                // box after it can only be filling in the origin.
                if (layer.Origin == null) { layer.Origin = lower; layer.Clip ??= lower; }
                else layer.Clip = lower;
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
    /// and neither does a bare keyword. 'none' is a mask source of its own (CSS Masking 1 §5.1),
    /// which is why 'mask: none' is a declaration and not an unclassified word.</summary>
    private static bool IsImageToken(string lower) =>
        lower == "none"
        || lower.StartsWith("url(", StringComparison.Ordinal)
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
