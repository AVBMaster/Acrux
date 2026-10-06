using SkiaSharp;
using System.Globalization;
using System.Text.RegularExpressions;
using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// Cross-platform color parser supporting all CSS color formats.
/// </summary>
public static class ColorParser
{
    /// <summary>CSS 'transparent' is rgba(0, 0, 0, 0). SkiaSharp's
    /// <see cref="SKColors.Transparent"/> is WHITE with zero alpha, and that leaked
    /// (255,255,255,0) into every computed value and into any interpolation that
    /// reads the channels rather than just the alpha.</summary>
    public static readonly SKColor CssTransparent = new SKColor(0, 0, 0, 0);
    private static readonly Regex RgbFuncRegex = new(@"^\s*rgba?\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HslFuncRegex = new(@"^\s*hsla?\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HwbRegex = new(@"^\s*hwb\s*\(\s*([\d.]+)(?:deg|turn|rad|grad)?\s*[,\s]\s*([\d.]+)%\s*[,\s]\s*([\d.]+)%\s*(?:[/,]\s*([\d.]+%?))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LabRegex = new(@"^\s*lab\s*\(\s*([\d.]+)%?\s*([+-]?\s*[\d.]+)\s*([+-]?\s*[\d.]+)\s*(?:\s*/\s*([\d.]+%?))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LchRegex = new(@"^\s*lch\s*\(\s*([\d.]+)%?\s*([\d.]+)\s*([\d.]+)\s*(?:\s*/\s*([\d.]+))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OklabRegex = new(@"^\s*oklab\s*\(\s*([\d.]+%?)\s*([+-]?\s*[\d.]+)\s*([+-]?\s*[\d.]+)\s*(?:\s*/\s*([\d.]+))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OklchRegex = new(@"^\s*oklch\s*\(\s*([\d.]+%?)\s*([\d.]+)\s*([\d.]+)\s*(?:\s*/\s*([\d.]+))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Hex8Regex = new(@"^#([0-9a-f]{8})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Hex6Regex = new(@"^#([0-9a-f]{6})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Hex4Regex = new(@"^#([0-9a-f]{4})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Hex3Regex = new(@"^#([0-9a-f]{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="context">The style the color is being resolved for; needed by
    /// light-dark(), which depends on the element's used color scheme.</param>
    /// <summary>Whether a token is a colour at all — needed because Parse is forgiving by
    /// design (an unknown word comes back as a zero-alpha colour rather than failing), so a
    /// caller that rewrites colour tokens inside a gradient cannot use Parse's success as a
    /// signal. Recognises the CSS named colours, any #rgb/#rrggbb/#rrggbbaa form, and the
    /// colour functions of Color 3/4 plus 'color-mix'.</summary>
    public static bool IsColorToken(string token)
    {
        token = (token ?? string.Empty).Trim();
        if (token.Length == 0) return false;
        if (token[0] == '#')
            return token.Length is 4 or 5 or 7 or 9;
        if (token.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            || token.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var prefix in new[] { "rgb(", "rgba(", "hsl(", "hsla(", "hwb(", "lab(", "lch(",
                                       "oklab(", "oklch(", "color(", "color-mix(" })
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return IsColorName(token);
    }

    public static SKColor Parse(string value, ComputedStyle? context = null)
    {
        if (string.IsNullOrEmpty(value) || value == "inherit")
            return SKColors.Transparent;
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return CssTransparent;

        value = value.Trim();

        var hex = ParseHex(value);
        if (hex.HasValue) return hex.Value;

        // Before the plain function parsers: 'rgb(from …)' would otherwise be read as
        // an rgb() call with a garbage channel list.
        var relative = ParseRelativeColor(value, context);
        if (relative.HasValue) return relative.Value;

        var rgb = ParseRgb(value);
        if (rgb.HasValue) return rgb.Value;

        var hsl = ParseHsl(value);
        if (hsl.HasValue) return hsl.Value;

        var hwb = ParseHwb(value);
        if (hwb.HasValue) return hwb.Value;

        var lab = ParseLab(value);
        if (lab.HasValue) return lab.Value;

        var lch = ParseLch(value);
        if (lch.HasValue) return lch.Value;

        var oklab = ParseOklab(value);
        if (oklab.HasValue) return oklab.Value;

        var oklch = ParseOklch(value);
        if (oklch.HasValue) return oklch.Value;

        var lightDark = ParseLightDark(value, context);
        if (lightDark.HasValue) return lightDark.Value;

        var colorMix = ParseColorMix(value, context);
        if (colorMix.HasValue) return colorMix.Value;

        var colorFn = ParseColorFunction(value);
        if (colorFn.HasValue) return colorFn.Value;

        var current = ParseCurrentColor(value);
        if (current.HasValue) return current.Value;

        return GetNamedColor(value);
    }

    private static SKColor? ParseHex(string value)
    {
        if (!value.StartsWith("#")) return null;

        var match8 = Hex8Regex.Match(value);
        if (match8.Success)
        {
            var hex = match8.Groups[1].Value;
            return new SKColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
        }

        var match6 = Hex6Regex.Match(value);
        if (match6.Success)
        {
            var hex = match6.Groups[1].Value;
            return new SKColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }

        var match4 = Hex4Regex.Match(value);
        if (match4.Success)
        {
            var hex = match4.Groups[1].Value;
            return new SKColor(
                Convert.ToByte($"{hex[0]}{hex[0]}", 16),
                Convert.ToByte($"{hex[1]}{hex[1]}", 16),
                Convert.ToByte($"{hex[2]}{hex[2]}", 16),
                Convert.ToByte($"{hex[3]}{hex[3]}", 16));
        }

        var match3 = Hex3Regex.Match(value);
        if (match3.Success)
        {
            var hex = match3.Groups[1].Value;
            return new SKColor(
                Convert.ToByte($"{hex[0]}{hex[0]}", 16),
                Convert.ToByte($"{hex[1]}{hex[1]}", 16),
                Convert.ToByte($"{hex[2]}{hex[2]}", 16));
        }

        return null;
    }

    private static SKColor? ParseRgb(string value)
    {
        var match = RgbFuncRegex.Match(value);
        if (!match.Success) return null;

        var inner = ExtractFunctionInner(value, match.Index);
        if (inner == null) return null;

        var parts = ParseColorFunctionArgs(inner);
        if (parts.Count < 3) return null;

        byte r = ParseColorChannel(parts[0], 255);
        byte g = ParseColorChannel(parts[1], 255);
        byte b = ParseColorChannel(parts[2], 255);
        byte alpha = 255;

        if (parts.Count >= 4)
            alpha = (byte)Math.Clamp(MathF.Round(ParseAlpha(parts[3]) * 255), 0, 255);

        return new SKColor(r, g, b, alpha);
    }

    private static SKColor? ParseHsl(string value)
    {
        var match = HslFuncRegex.Match(value);
        if (!match.Success) return null;

        var inner = ExtractFunctionInner(value, match.Index);
        if (inner == null) return null;

        var parts = ParseColorFunctionArgs(inner);
        if (parts.Count < 3) return null;

        float h = float.Parse(parts[0]);
        float s = ParsePercent(parts[1]);
        float l = ParsePercent(parts[2]);
        float alpha = 1.0f;

        if (parts.Count >= 4)
            alpha = ParseAlpha(parts[3]);

        return HslToRgb(h, s, l, alpha);
    }

    private static string? ExtractFunctionInner(string value, int funcStart)
    {
        int parenStart = value.IndexOf('(', funcStart);
        if (parenStart < 0) return null;
        int depth = 1;
        int i = parenStart + 1;
        while (i < value.Length && depth > 0)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            if (depth > 0) i++;
        }
        return depth == 0 ? value[(parenStart + 1)..i] : null;
    }

    private static List<string> ParseColorFunctionArgs(string inner)
    {
        var result = new List<string>();

        // CSS Color 4 allows whitespace-separated channels with the alpha introduced
        // by a top-level '/': "rgb(255 128 0 / 50%)". Legacy comma syntax keeps working.
        string main = inner;
        string? alphaPart = null;
        int depth = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == '/' && depth == 0)
            {
                main = inner[..i];
                alphaPart = inner[(i + 1)..];
                break;
            }
        }

        foreach (var part in main.Split(',', StringSplitOptions.RemoveEmptyEntries))
            foreach (var tok in part.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(tok.Trim());

        if (alphaPart != null)
            foreach (var tok in alphaPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(tok.Trim());

        return result;
    }

    private static byte ParseColorChannel(string value, byte max)
    {
        value = value.Trim();
        if (value.EndsWith("%"))
        {
            float pct = float.Parse(value[..^1]);
            return (byte)Math.Clamp(MathF.Round(pct / 100f * max), 0, max);
        }
        return byte.Parse(value);
    }

    private static float ParseAlpha(string value)
    {
        value = value.Trim();
        if (value.EndsWith("%"))
            return float.Parse(value[..^1]) / 100f;
        return float.Parse(value);
    }

    private static float ParsePercent(string value)
    {
        value = value.Trim();
        if (value.EndsWith("%"))
            return float.Parse(value[..^1]) / 100f;
        return float.Parse(value);
    }

    private static SKColor HslToRgb(float h, float s, float l, float a)
    {
        h = ((h % 360) + 360) % 360;
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        float m = l - c / 2;

        float r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new SKColor(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255),
            (byte)(a * 255));
    }

    private static SKColor? ParseHwb(string value)
    {
        var match = HwbRegex.Match(value);
        if (!match.Success) return null;

        float h = float.Parse(match.Groups[1].Value);
        float w = float.Parse(match.Groups[2].Value) / 100f;
        float b = float.Parse(match.Groups[3].Value) / 100f;
        float alpha = 1f;
        if (match.Groups[4].Success)
        {
            var aVal = match.Groups[4].Value;
            alpha = aVal.EndsWith("%") ? float.Parse(aVal[..^1]) / 100f : float.Parse(aVal);
        }

        h = ((h % 360) + 360) % 360;
        w = MathF.Min(w, 1f);
        b = MathF.Min(b, 1f);
        float sum = w + b;
        if (sum > 1f) { w /= sum; b /= sum; }

        // HWB to RGB: compute hue with full saturation, then apply whiteness and blackness
        float hue = h;
        float c = 1f;
        float x = c * (1 - Math.Abs((hue / 60f) % 2 - 1));
        float r = 0, g = 0, bb = 0;
        if (hue < 60) { r = c; g = x; }
        else if (hue < 120) { r = x; g = c; }
        else if (hue < 180) { r = 0; g = c; bb = x; }
        else if (hue < 240) { r = 0; g = x; bb = c; }
        else if (hue < 300) { r = x; g = 0; bb = c; }
        else { r = c; g = 0; bb = x; }

        // Apply whiteness and blackness
        r = r * (1f - w - b) + w;
        g = g * (1f - w - b) + w;
        bb = bb * (1f - w - b) + w;

        return new SKColor(
            (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(bb * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
    }

    private static SKColor? ParseLab(string value)
    {
        var match = LabRegex.Match(value);
        if (!match.Success) return null;

        float l = float.Parse(match.Groups[1].Value);
        float a = float.Parse(match.Groups[2].Value);
        float bb = float.Parse(match.Groups[3].Value);
        float alpha = 1f;
        if (match.Groups[4].Success)
            alpha = float.Parse(match.Groups[4].Value);

        // Approximate Lab -> sRGB via simple conversion
        l = Math.Clamp(l, 0, 100);
        float fy = (l + 16) / 116;
        float fx = a / 500 + fy;
        float fz = fy - bb / 200;

        float x = LabLinearComponent(fx) * 0.96422f;
        float y = LabLinearComponent(fy) * 1f;
        float z = LabLinearComponent(fz) * 0.82521f;

        return XyzToSrgb(x, y, z, alpha);
    }

    private static SKColor? ParseLch(string value)
    {
        var match = LchRegex.Match(value);
        if (!match.Success) return null;

        float l = float.Parse(match.Groups[1].Value);
        float c = float.Parse(match.Groups[2].Value);
        float h = float.Parse(match.Groups[3].Value);
        float alpha = 1f;
        if (match.Groups[4].Success)
            alpha = float.Parse(match.Groups[4].Value);

        float a = c * MathF.Cos(h * MathF.PI / 180f);
        float bb = c * MathF.Sin(h * MathF.PI / 180f);

        float fy = (l + 16) / 116;
        float fx = a / 500 + fy;
        float fz = fy - bb / 200;

        float x = LabLinearComponent(fx) * 0.96422f;
        float y = LabLinearComponent(fy) * 1f;
        float z = LabLinearComponent(fz) * 0.82521f;

        return XyzToSrgb(x, y, z, alpha);
    }

    private static SKColor? ParseOklab(string value)
    {
        var match = OklabRegex.Match(value);
        if (!match.Success) return null;

        float l = float.Parse(match.Groups[1].Value);
        float a = float.Parse(match.Groups[2].Value);
        float bb = float.Parse(match.Groups[3].Value);
        float alpha = 1f;
        if (match.Groups[4].Success)
            alpha = float.Parse(match.Groups[4].Value);

        // OKLab -> linear sRGB conversion
        l = Math.Clamp(l, 0, 1);
        float l_ = l + 0.3963377774f * a + 0.2158037573f * bb;
        float m_ = l - 0.1055613458f * a - 0.0638541728f * bb;
        float s_ = l - 0.0894841775f * a - 1.2914855480f * bb;

        float l3 = l_ * l_ * l_;
        float m3 = m_ * m_ * m_;
        float s3 = s_ * s_ * s_;

        float r = 4.0767416621f * l3 - 3.3077115913f * m3 + 0.2309699292f * s3;
        float g = -1.2684380046f * l3 + 2.6097574011f * m3 - 0.3413193965f * s3;
        float b = -0.0041960863f * l3 - 0.7034186147f * m3 + 1.7076147010f * s3;

        r = LinearToSrgbChannel(r);
        g = LinearToSrgbChannel(g);
        b = LinearToSrgbChannel(b);

        return new SKColor(
            (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(b * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
    }

    private static SKColor? ParseOklch(string value)
    {
        var match = OklchRegex.Match(value);
        if (!match.Success) return null;

        float l = float.Parse(match.Groups[1].Value);
        float c = float.Parse(match.Groups[2].Value);
        float h = float.Parse(match.Groups[3].Value);
        float alpha = 1f;
        if (match.Groups[4].Success)
            alpha = float.Parse(match.Groups[4].Value);

        float a = c * MathF.Cos(h * MathF.PI / 180f);
        float bb = c * MathF.Sin(h * MathF.PI / 180f);

        // OKLab -> linear sRGB (reuse oklab logic)
        l = Math.Clamp(l, 0, 1);
        float l_ = l + 0.3963377774f * a + 0.2158037573f * bb;
        float m_ = l - 0.1055613458f * a - 0.0638541728f * bb;
        float s_ = l - 0.0894841775f * a - 1.2914855480f * bb;

        float l3 = l_ * l_ * l_;
        float m3 = m_ * m_ * m_;
        float s3 = s_ * s_ * s_;

        float r = 4.0767416621f * l3 - 3.3077115913f * m3 + 0.2309699292f * s3;
        float g = -1.2684380046f * l3 + 2.6097574011f * m3 - 0.3413193965f * s3;
        float b = -0.0041960863f * l3 - 0.7034186147f * m3 + 1.7076147010f * s3;

        r = LinearToSrgbChannel(r);
        g = LinearToSrgbChannel(g);
        b = LinearToSrgbChannel(b);

        return new SKColor(
            (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(b * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
    }

    /// <summary>
    /// light-dark(&lt;light&gt;, &lt;dark&gt;) resolves against the element's used color scheme.
    /// The engine presents a light UI, so 'light dark' (both available) stays light
    /// and only a dark-only scheme picks the second value (CSS Color 5 §6).
    /// </summary>
    private static SKColor? ParseLightDark(string value, ComputedStyle? context)
    {
        var match = Regex.Match(value, @"^\s*light-dark\s*\(\s*([^,]+)\s*,\s*(.+)\s*\)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        string scheme = context?.ColorScheme ?? "normal";
        bool dark = scheme.Contains("dark", StringComparison.OrdinalIgnoreCase) &&
                    !scheme.Contains("light", StringComparison.OrdinalIgnoreCase);
        return Parse((dark ? match.Groups[2] : match.Groups[1]).Value.Trim(), context);
    }

    /// <summary>
    /// color-mix([in &lt;space&gt; [&lt;hue-method&gt;],]? &lt;color&gt; [&lt;pct&gt;], &lt;color&gt; [&lt;pct&gt;]).
    /// Interpolation happens in the named space with premultiplied alpha, and the
    /// default space is oklab (CSS Color 5 §3).
    /// </summary>
    private static SKColor? ParseColorMix(string value, ComputedStyle? context)
    {
        var match = Regex.Match(value, @"^\s*color-mix\s*\(", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var inner = ExtractFunctionInner(value, match.Index);
        if (inner == null) return null;

        string space = "oklab";
        string hueMethod = "shorter";

        var tokens = inner.Trim();
        if (tokens.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
        {
            int comma = IndexTopLevel(tokens, ',');
            string spec = (comma > 0 ? tokens[..comma] : tokens).Trim();
            tokens = comma > 0 ? tokens[(comma + 1)..].Trim() : "";

            var words = spec[3..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0) space = ColorInterpolation.Normalize(words[0]);
            if (words.Length > 1) hueMethod = ColorInterpolation.Normalize(words[1]);
        }

        int split = IndexTopLevel(tokens, ',');
        if (split < 0) return null;
        var (c1, p1) = ParseColorMixArg(tokens[..split], context);
        var (c2, p2) = ParseColorMixArg(tokens[(split + 1)..], context);

        // A missing percentage is whatever makes the pair sum to 100%.
        if (p1 < 0 && p2 < 0) { p1 = p2 = 0.5f; }
        else if (p1 < 0) p1 = 1f - p2;
        else if (p2 < 0) p2 = 1f - p1;

        var x = ColorInterpolation.FromSrgb(c1, space);
        var y = ColorInterpolation.FromSrgb(c2, space);
        return ColorInterpolation.ToSrgb(ColorInterpolation.Mix(x, p1, y, p2, hueMethod));
    }

    private static int IndexTopLevel(string text, char target)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == target && depth == 0) return i;
        }
        return -1;
    }

    private static (SKColor color, float pct) ParseColorMixArg(string arg, ComputedStyle? context)
    {
        arg = arg.Trim();
        // The weight is the last whitespace-separated token, and only when it is a
        // percentage — the color itself may contain spaces inside its own function.
        int split = LastTopLevelSpace(arg);
        if (split > 0)
        {
            string tail = arg[(split + 1)..].Trim();
            if (tail.EndsWith('%') && float.TryParse(tail[..^1], NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
                return (Parse(arg[..split].Trim(), context), Math.Clamp(pct / 100f, 0f, 1f));
        }
        return (Parse(arg, context), -1f);
    }

    private static int LastTopLevelSpace(string text)
    {
        int depth = 0;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c == ')') depth++;
            else if (c == '(') depth--;
            else if (char.IsWhiteSpace(c) && depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Relative color syntax: &lt;func&gt;(from &lt;color&gt; c0 c1 c2 [/ alpha]) where each
    /// channel is a number, a percentage, 'none', a channel keyword of the target
    /// space, or a calc() over those (CSS Color 5 §5). The origin color is converted
    /// into the target space before its channels are substituted.
    /// </summary>
    private static SKColor? ParseRelativeColor(string value, ComputedStyle? context)
    {
        var match = Regex.Match(value,
            @"^\s*(rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch|color)\s*\(\s*from\s+",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var inner = ExtractFunctionInner(value, match.Index);
        if (inner == null) return null;

        int fromIdx = inner.IndexOf("from", StringComparison.OrdinalIgnoreCase);
        if (fromIdx < 0) return null;
        string body = inner[(fromIdx + 4)..].Trim();

        int originEnd = EndOfColorToken(body);
        if (originEnd <= 0) return null;
        SKColor origin = Parse(body[..originEnd], context);
        string rest = body[originEnd..].Trim();

        string func = match.Groups[1].Value.ToLowerInvariant();
        string space = func switch
        {
            "hsl" or "hsla" => "hsl",
            "hwb" => "hwb",
            "lab" => "lab",
            "lch" => "lch",
            "oklab" => "oklab",
            "oklch" => "oklch",
            _ => "srgb",
        };

        var ch = ColorInterpolation.FromSrgb(origin, space);

        string mainPart = rest;
        string? alphaPart = null;
        int slash = IndexTopLevel(rest, '/');
        if (slash >= 0)
        {
            mainPart = rest[..slash];
            alphaPart = rest[(slash + 1)..];
        }

        // Whitespace only separates channels outside of nested functions, so a
        // 'calc(l + 20)' slot stays one token.
        var toks = SplitTopLevelWhitespace(mainPart);
        if (toks.Count < 3) return null;

        float c0 = ResolveRelativeChannel(toks[0], space, 0, ch);
        float c1 = ResolveRelativeChannel(toks[1], space, 1, ch);
        float c2 = ResolveRelativeChannel(toks[2], space, 2, ch);
        float alpha = alphaPart is null ? ch.Alpha : ResolveRelativeAlpha(alphaPart.Trim(), ch);

        return ColorInterpolation.ToSrgb(new ColorChannels(space, c0, c1, c2, alpha));
    }

    private static List<string> SplitTopLevelWhitespace(string text)
    {
        var result = new List<string>();
        int depth = 0;
        int start = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (start >= 0) { result.Add(text[start..i]); start = -1; }
                continue;
            }
            if (start < 0 && !char.IsWhiteSpace(c)) start = i;
        }
        if (start >= 0) result.Add(text[start..]);
        return result;
    }

    /// <summary>Length of the leading color token: a balanced function call, or
    /// everything up to the first whitespace.</summary>
    private static int EndOfColorToken(string text)
    {
        int paren = text.IndexOf('(');
        int space = text.IndexOf(' ');
        if (paren > 0 && (space < 0 || paren < space))
        {
            int depth = 0;
            for (int i = paren; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }
            }
            return -1;
        }
        return space < 0 ? text.Length : space;
    }

    private static float ResolveRelativeChannel(string token, string space, int index, in ColorChannels ch)
    {
        token = token.Trim();
        if (token.Equals("none", StringComparison.OrdinalIgnoreCase)) return float.NaN;

        float divisor = SpecifiedDivisor(space, index);
        float specified = EvaluateChannelExpr(token, space, index, ch);
        return specified / divisor;
    }

    /// <summary>Evaluate a channel slot in that channel's specified units, so that
    /// 'calc(l + 20)' adds twenty the way the author wrote it.</summary>
    private static float EvaluateChannelExpr(string token, string space, int index, in ColorChannels ch)
    {
        string text = token.Trim();
        var calc = Regex.Match(text, @"^calc\s*\((.*)\)$", RegexOptions.IgnoreCase);
        if (calc.Success) text = calc.Groups[1].Value;

        float total = 0;
        int sign = 1;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool boundary = i == text.Length || text[i] == '+' || text[i] == '-';
            if (!boundary) continue;
            if (i > start)
            {
                string term = text[start..i].Trim();
                if (term.Length > 0) total += sign * ParseChannelTerm(term, space, index, ch);
            }
            if (i < text.Length)
            {
                sign = text[i] == '-' ? -1 : 1;
                start = i + 1;
            }
        }
        return total;
    }

    private static float ParseChannelTerm(string term, string space, int index, in ColorChannels ch)
    {
        float keyword = KeywordChannel(space, index, term, ch);
        if (!float.IsNaN(keyword)) return keyword * SpecifiedDivisor(space, index);

        if (term.EndsWith("%"))
        {
            if (float.TryParse(term[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                return pct / 100f * PercentMultiplier(space, index);
            return 0;
        }
        return float.TryParse(term, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : 0;
    }

    /// <summary>The target space's keyword for this channel slot, in internal units,
    /// or NaN when the token is not that keyword.</summary>
    private static float KeywordChannel(string space, int index, string token, in ColorChannels ch)
    {
        token = token.Trim().ToLowerInvariant();
        string expected = (space, index) switch
        {
            ("srgb", 0) => "r", ("srgb", 1) => "g", ("srgb", 2) => "b",
            ("hsl", 0) => "h", ("hsl", 1) => "s", ("hsl", 2) => "l",
            ("hwb", 0) => "h", ("hwb", 1) => "w", ("hwb", 2) => "b",
            ("lab", 0) => "l", ("lab", 1) => "a", ("lab", 2) => "b",
            ("lch", 0) => "l", ("lch", 1) => "c", ("lch", 2) => "h",
            ("oklab", 0) => "l", ("oklab", 1) => "a", ("oklab", 2) => "b",
            ("oklch", 0) => "l", ("oklch", 1) => "c", ("oklch", 2) => "h",
            _ => "",
        };
        bool isHueAlias = token == "hue" && index == HueIndexInSpace(space);
        if (token != expected && !isHueAlias) return float.NaN;
        return index switch { 0 => ch.C0, 1 => ch.C1, _ => ch.C2 };
    }

    private static int HueIndexInSpace(string space) => space switch
    {
        "hsl" or "hwb" => 0,
        "lch" or "oklch" => 2,
        _ => -1,
    };

    /// <summary>Divisor from a channel's specified units to the internal units
    /// ColorInterpolation works in (sRGB 0..1, HSL saturation 0..1, Lab L 0..100, …).</summary>
    private static float SpecifiedDivisor(string space, int index) => (space, index) switch
    {
        ("srgb", _) => 255f,
        ("hsl", 1) or ("hsl", 2) or ("hwb", 1) or ("hwb", 2) => 100f,
        ("lab", 0) or ("lch", 0) => 100f,
        _ => 1f,
    };

    /// <summary>What 100% means for this channel (CSS Color 5 §5.2).</summary>
    private static float PercentMultiplier(string space, int index) => (space, index) switch
    {
        ("srgb", _) => 255f,
        ("hsl", 0) or ("hwb", 0) or ("lch", 2) or ("oklch", 2) => 3.6f,
        ("hsl", 1) or ("hsl", 2) or ("hwb", 1) or ("hwb", 2) => 100f,
        ("lab", 0) or ("lch", 0) => 100f,
        ("lab", 1) or ("lab", 2) or ("lch", 1) => 125f,
        ("oklab", 1) or ("oklab", 2) or ("oklch", 1) => 0.4f,
        _ => 1f,
    };

    private static float ResolveRelativeAlpha(string token, in ColorChannels ch)
    {
        token = token.Trim();
        if (token.Equals("none", StringComparison.OrdinalIgnoreCase)) return float.NaN;
        if (token.Equals("a", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("alpha", StringComparison.OrdinalIgnoreCase)) return ch.Alpha;
        if (token.EndsWith("%") &&
            float.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            return Math.Clamp(pct / 100f, 0f, 1f);
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            ? Math.Clamp(a, 0f, 1f) : 1f;
    }

    /// <summary>
    /// color(&lt;space&gt; c1 c2 c3 [/ alpha]) for the named coordinate spaces of
    /// CSS Color 4 §11 (srgb, srgb-linear, display-p3, a98-rgb, prophoto-rgb,
    /// rec2020, xyz, xyz-d50, xyz-d65).
    /// </summary>
    private static SKColor? ParseColorFunction(string value)
    {
        var match = Regex.Match(value, @"^\s*color\s*\(\s*([\w-]+)(.*)\)\s*$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        string space = match.Groups[1].Value.ToLowerInvariant();
        var parts = ParseColorFunctionArgs(match.Groups[2].Value);
        if (parts.Count < 3) return null;

        // 'none' stands for the space's neutral/zero value.
        float Channel(string token, float noneValue) => token.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? noneValue
            : ParsePercent(token);

        float c0 = Channel(parts[0], 0f);
        float c1 = Channel(parts[1], 0f);
        float c2 = Channel(parts[2], 0f);
        float alpha = parts.Count > 3 ? ParseAlpha(parts[3]) : 1f;

        return ColorInterpolation.FromColorSpace(space, c0, c1, c2, alpha);
    }

    private static float LabLinearComponent(float t)
    {
        const float delta = 6f / 29f;
        if (t > delta)
            return t * t * t;
        return 3 * delta * delta * (t - 4f / 29f);
    }

    private static SKColor XyzToSrgb(float x, float y, float z, float alpha)
    {
        float r = 3.2404542f * x - 1.5371385f * y - 0.4985314f * z;
        float g = -0.9692660f * x + 1.8760108f * y + 0.0415560f * z;
        float b = 0.0556434f * x - 0.2040259f * y + 1.0572252f * z;

        r = LinearToSrgbChannel(r);
        g = LinearToSrgbChannel(g);
        b = LinearToSrgbChannel(b);

        return new SKColor(
            (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(b * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
    }

    private static (byte r, byte g, byte b) DisplayP3ToSrgb(float r, float g, float b)
    {
        // Approximate Display P3 to sRGB
        float sr = 1.0f * r + 0.0f * g + 0.0f * b;
        float sg = 0.0f * r + 1.0f * g + 0.0f * b;
        float sb = 0.0f * r + 0.0f * g + 1.0f * b;
        return (
            (byte)Math.Clamp(MathF.Round(LinearToSrgbChannel(sr) * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(LinearToSrgbChannel(sg) * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(LinearToSrgbChannel(sb) * 255), 0, 255));
    }

    private static float LinearToSrgbChannel(float c)
    {
        if (c <= 0.0031308f)
            return 12.92f * c;
        return 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    private static SKColor? ParseCurrentColor(string value)
    {
        return value.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)
            ? SKColors.Black
            : null;
    }

    private static SKColor GetNamedColor(string name)
    {
        return KnownColors.Get(name) ?? SystemColors.Get(name) ?? SKColors.Black;
    }

    public static bool IsColorName(string name) =>
        KnownColors.Get(name).HasValue || SystemColors.Get(name).HasValue;

    /// <summary>True when a shorthand token is (or starts) a color value: a hex
    /// literal, a named color, or one of the functional color notations
    /// (rgb/hsl/hwb/lab/lch/oklab/oklch/color()/color-mix()).</summary>
    public static bool LooksLikeColor(string value)
    {
        var v = value.TrimStart().TrimEnd(',').Trim();
        if (v.Length == 0) return false;
        if (v[0] == '#') return true;
        if (IsColorName(v)) return true;
        foreach (var fn in FunctionalColorPrefixes)
            if (v.StartsWith(fn, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>True when the value is itself a functional color notation. The math
    /// evaluator must not fold calc() inside such a value: a relative color syntax
    /// slot like 'calc(l + 20)' only means something to this parser.</summary>
    public static bool IsFunctionalColor(string value)
    {
        var v = value.TrimStart();
        foreach (var fn in FunctionalColorPrefixes)
            if (v.StartsWith(fn, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static readonly string[] FunctionalColorPrefixes =
    {
        "rgb(", "rgba(", "hsl(", "hsla(", "hwb(", "lab(", "lch(",
        "oklab(", "oklch(", "color(", "color-mix(", "light-dark(",
    };
}

public static class KnownColors
{
    private static readonly Dictionary<string, SKColor> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "black", SKColors.Black }, { "white", SKColors.White },
        { "red", SKColor.Parse("#FF0000") }, { "green", SKColor.Parse("#008000") },
        { "blue", SKColor.Parse("#0000FF") }, { "yellow", SKColors.Yellow },
        { "cyan", SKColors.Cyan }, { "magenta", SKColors.Magenta },
        { "gray", SKColors.Gray }, { "silver", SKColor.Parse("#C0C0C0") },
        { "maroon", SKColor.Parse("#800000") }, { "olive", SKColor.Parse("#808000") },
        { "lime", SKColor.Parse("#00FF00") }, { "aqua", SKColor.Parse("#00FFFF") },
        { "teal", SKColor.Parse("#008080") }, { "navy", SKColor.Parse("#000080") },
        { "fuchsia", SKColor.Parse("#FF00FF") }, { "purple", SKColors.Purple },
        { "orange", SKColor.Parse("#FFA500") }, { "pink", SKColor.Parse("#FFC0CB") },
        { "coral", SKColor.Parse("#FF7F50") }, { "salmon", SKColor.Parse("#FA8072") },
        { "gold", SKColor.Parse("#FFD700") }, { "khaki", SKColor.Parse("#F0E68C") },
        { "plum", SKColor.Parse("#DDA0DD") }, { "violet", SKColor.Parse("#EE82EE") },
        { "tan", SKColor.Parse("#D2B48C") }, { "chocolate", SKColor.Parse("#D2691E") },
        { "transparent", ColorParser.CssTransparent },
        { "aliceblue", SKColor.Parse("#F0F8FF") }, { "antiquewhite", SKColor.Parse("#FAEBD7") },
        { "aquamarine", SKColor.Parse("#7FFFD4") }, { "azure", SKColor.Parse("#F0FFFF") },
        { "beige", SKColor.Parse("#F5F5DC") }, { "bisque", SKColor.Parse("#FFE4C4") },
        { "blanchedalmond", SKColor.Parse("#FFEBCD") }, { "blueviolet", SKColor.Parse("#8A2BE2") },
        { "brown", SKColor.Parse("#A52A2A") }, { "burlywood", SKColor.Parse("#DEB887") },
        { "cadetblue", SKColor.Parse("#5F9EA0") }, { "chartreuse", SKColor.Parse("#7FFF00") },
        { "cornflowerblue", SKColor.Parse("#6495ED") }, { "cornsilk", SKColor.Parse("#FFF8DC") },
        { "crimson", SKColor.Parse("#DC143C") }, { "darkblue", SKColor.Parse("#00008B") },
        { "darkcyan", SKColor.Parse("#008B8B") }, { "darkgoldenrod", SKColor.Parse("#B8860B") },
        { "darkgray", SKColor.Parse("#A9A9A9") }, { "darkgreen", SKColor.Parse("#006400") },
        { "darkkhaki", SKColor.Parse("#BDB76B") }, { "darkmagenta", SKColor.Parse("#8B008B") },
        { "darkolivegreen", SKColor.Parse("#556B2F") }, { "darkorange", SKColor.Parse("#FF8C00") },
        { "darkorchid", SKColor.Parse("#9932CC") }, { "darkred", SKColor.Parse("#8B0000") },
        { "darksalmon", SKColor.Parse("#E9967A") }, { "darkseagreen", SKColor.Parse("#8FBC8F") },
        { "darkslateblue", SKColor.Parse("#483D8B") }, { "darkslategray", SKColor.Parse("#2F4F4F") },
        { "darkturquoise", SKColor.Parse("#00CED1") }, { "darkviolet", SKColor.Parse("#9400D3") },
        { "deeppink", SKColor.Parse("#FF1493") }, { "deepskyblue", SKColor.Parse("#00BFFF") },
        { "dimgray", SKColor.Parse("#696969") }, { "dodgerblue", SKColor.Parse("#1E90FF") },
        { "firebrick", SKColor.Parse("#B22222") }, { "floralwhite", SKColor.Parse("#FFFAF0") },
        { "forestgreen", SKColor.Parse("#228B22") }, { "gainsboro", SKColor.Parse("#DCDCDC") },
        { "ghostwhite", SKColor.Parse("#F8F8FF") }, { "goldenrod", SKColor.Parse("#DAA520") },
        { "greenyellow", SKColor.Parse("#ADFF2F") }, { "honeydew", SKColor.Parse("#F0FFF0") },
        { "hotpink", SKColor.Parse("#FF69B4") }, { "indianred", SKColor.Parse("#CD5C5C") },
        { "indigo", SKColor.Parse("#4B0082") }, { "ivory", SKColor.Parse("#FFFFF0") },
        { "lavender", SKColor.Parse("#E6E6FA") }, { "lavenderblush", SKColor.Parse("#FFF0F5") },
        { "lawngreen", SKColor.Parse("#7CFC00") }, { "lemonchiffon", SKColor.Parse("#FFFACD") },
        { "lightblue", SKColor.Parse("#ADD8E6") }, { "lightcoral", SKColor.Parse("#F08080") },
        { "lightcyan", SKColor.Parse("#E0FFFF") }, { "lightgoldenrodyellow", SKColor.Parse("#FAFAD2") },
        { "lightgray", SKColor.Parse("#D3D3D3") }, { "lightgreen", SKColor.Parse("#90EE90") },
        { "lightpink", SKColor.Parse("#FFB6C1") }, { "lightsalmon", SKColor.Parse("#FFA07A") },
        { "lightseagreen", SKColor.Parse("#20B2AA") }, { "lightskyblue", SKColor.Parse("#87CEFA") },
        { "lightslategray", SKColor.Parse("#778899") }, { "lightsteelblue", SKColor.Parse("#B0C4DE") },
        { "lightyellow", SKColor.Parse("#FFFFE0") }, { "limegreen", SKColor.Parse("#32CD32") },
        { "linen", SKColor.Parse("#FAF0E6") }, { "mediumaquamarine", SKColor.Parse("#66CDAA") },
        { "mediumblue", SKColor.Parse("#0000CD") }, { "mediumorchid", SKColor.Parse("#BA55D3") },
        { "mediumpurple", SKColor.Parse("#9370DB") }, { "mediumseagreen", SKColor.Parse("#3CB371") },
        { "mediumslateblue", SKColor.Parse("#7B68EE") }, { "mediumspringgreen", SKColor.Parse("#00FA9A") },
        { "mediumturquoise", SKColor.Parse("#48D1CC") }, { "mediumvioletred", SKColor.Parse("#C71585") },
        { "midnightblue", SKColor.Parse("#191970") }, { "mintcream", SKColor.Parse("#F5FFFA") },
        { "mistyrose", SKColor.Parse("#FFE4E1") }, { "moccasin", SKColor.Parse("#FFE4B5") },
        { "navajowhite", SKColor.Parse("#FFDEAD") }, { "oldlace", SKColor.Parse("#FDF5E6") },
        { "olivedrab", SKColor.Parse("#6B8E23") }, { "orangered", SKColor.Parse("#FF4500") },
        { "orchid", SKColor.Parse("#DA70D6") }, { "palegoldenrod", SKColor.Parse("#EEE8AA") },
        { "palegreen", SKColor.Parse("#98FB98") }, { "paleturquoise", SKColor.Parse("#AFEEEE") },
        { "palevioletred", SKColor.Parse("#DB7093") }, { "papayawhip", SKColor.Parse("#FFEFD5") },
        { "peachpuff", SKColor.Parse("#FFDAB9") }, { "peru", SKColor.Parse("#CD853F") },
        { "powderblue", SKColor.Parse("#B0E0E6") }, { "rosybrown", SKColor.Parse("#BC8F8F") },
        { "royalblue", SKColor.Parse("#4169E1") }, { "saddlebrown", SKColor.Parse("#8B4513") },
        { "sandybrown", SKColor.Parse("#F4A460") }, { "seagreen", SKColor.Parse("#2E8B57") },
        { "seashell", SKColor.Parse("#FFF5EE") }, { "sienna", SKColor.Parse("#A0522D") },
        { "skyblue", SKColor.Parse("#87CEEB") }, { "slateblue", SKColor.Parse("#6A5ACD") },
        { "slategray", SKColor.Parse("#708090") }, { "snow", SKColor.Parse("#FFFAFA") },
        { "springgreen", SKColor.Parse("#00FF7F") }, { "steelblue", SKColor.Parse("#4682B4") },
        { "thistle", SKColor.Parse("#D8BFD8") }, { "tomato", SKColor.Parse("#FF6347") },
        { "turquoise", SKColor.Parse("#40E0D0") }, { "wheat", SKColor.Parse("#F5DEB3") },
        { "whitesmoke", SKColor.Parse("#F5F5F5") }, { "yellowgreen", SKColor.Parse("#9ACD32") },
        { "rebeccapurple", SKColor.Parse("#663399") },
        { "grey", SKColor.Parse("#808080") },
        { "darkgrey", SKColor.Parse("#A9A9A9") },
        { "darkslategrey", SKColor.Parse("#2F4F4F") },
        { "dimgrey", SKColor.Parse("#696969") },
        { "lightgrey", SKColor.Parse("#D3D3D3") },
        { "lightslategrey", SKColor.Parse("#778899") },
        { "slategrey", SKColor.Parse("#708090") }
    };

    public static SKColor? Get(string name) => Colors.TryGetValue(name, out var color) ? color : null;
}

/// <summary>
/// CSS system colors (CSS Color 3 "crisp" set) plus the -webkit- alias the UA
/// sheet leans on. These are not decorative names: the UA rules for buttons,
/// fields and fieldsets are written with them, and an unresolved keyword falls
/// back to black - which is how a <button> used to paint as a solid black block.
/// Values are Chrome's used colors on a Windows light theme, read back with
/// getComputedStyle (see snapshots/css-standard-verify171-system-colors.html).
/// </summary>
public static class SystemColors
{
    public static readonly SKColor FieldFrame = new SKColor(0x76, 0x76, 0x76);

    private static readonly Dictionary<string, SKColor> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Widget faces and their text.
        { "buttonface", new SKColor(240, 240, 240) },
        { "buttontext", SKColors.Black },
        { "buttonborder", SKColors.Black },
        { "buttonshadow", new SKColor(240, 240, 240) },
        { "field", SKColors.White },
        { "fieldtext", SKColors.Black },
        { "canvas", SKColors.White },
        { "canvastext", SKColors.Black },
        // Selection and highlighting.
        { "highlight", new SKColor(0, 120, 215) },
        { "highlighttext", SKColors.White },
        { "mark", new SKColor(255, 255, 0) },
        { "marktext", SKColors.Black },
        { "graytext", new SKColor(109, 109, 109) },
        // Links.
        { "linktext", new SKColor(0, 102, 204) },
        { "visitedtext", new SKColor(0, 102, 204) },
        { "activetext", new SKColor(0, 102, 204) },
        // Window chrome.
        { "window", SKColors.White },
        { "windowframe", SKColors.Black },
        { "windowtext", SKColors.Black },
        { "scrollbar", SKColors.White },
        { "activeborder", SKColors.Black },
        { "activecaption", SKColors.White },
        { "captiontext", SKColors.Black },
        { "inactiveborder", SKColors.Black },
        { "inactivecaption", SKColors.White },
        { "inactivecaptiontext", new SKColor(128, 128, 128) },
        { "infobackground", SKColors.White },
        { "infotext", SKColors.Black },
        { "menu", SKColors.White },
        // The 3D bevel set. Chrome reports these as black on Windows; the legacy
        // threedface alias is the button face, not black.
        { "threedface", new SKColor(240, 240, 240) },
        { "threedshadow", SKColors.Black },
        { "threeddarkshadow", SKColors.Black },
        { "threedhighlight", SKColors.Black },
        { "threedlightshadow", SKColors.Black },
        // Chrome's focus ring / field border. The keyword itself is internal, but
        // every UA border that names it renders as #767676.
        { "-webkit-focus-ring-color", FieldFrame },
    };

    public static SKColor? Get(string name) => Colors.TryGetValue(name, out var color) ? color : null;
}
