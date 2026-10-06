using SkiaSharp;

namespace Acrux.Rendering;

public static class FilterRenderer
{
    // Filter strings are re-parsed on every display-list rebuild of every element
    // that declares one, and the chain is immutable — the classic cache case.
    // The inflation is the distance the chain's output can spread beyond the
    // element's box (Gaussian tails reach ~3σ); the compositor layer bounds use
    // it to stop allocating a full-window offscreen surface per filter layer.
    private static readonly Dictionary<string, (SKImageFilter? Filter, float Inflation)> _chainCache = new();
    private static readonly object _chainCacheLock = new();
    private const int MaxChainCacheSize = 64;

    public static SKImageFilter? ParseAndChain(string? filterString) =>
        ParseAndChain(filterString, out _);

    public static SKImageFilter? ParseAndChain(string? filterString, out float inflationPx)
    {
        if (string.IsNullOrWhiteSpace(filterString) || filterString == "none")
        {
            inflationPx = 0f;
            return null;
        }

        lock (_chainCacheLock)
        {
            if (_chainCache.TryGetValue(filterString, out var hit))
            {
                inflationPx = hit.Inflation;
                return hit.Filter;
            }
        }

        float inflation = 0f;
        SKImageFilter? result = null;
        bool invalid = false;

        var filters = ParseFilters(filterString);
        if (filters == null || filters.Count == 0) invalid = true;
        else
        {
            foreach (var filter in filters)
            {
                var f = CreateFilter(filter);
                if (f == null) { invalid = true; break; }

                inflation += EntryInflation(filter);

                // CreateCompose(outer, inner) evaluates inner first, so the next function in
                // the list has to become the outer one. Each stage renders through an 8-bit
                // image, which is what clamps the channels between functions.
                result = result != null ? SKImageFilter.CreateCompose(f, result) : f;
            }
        }

        if (invalid) { result = null; inflation = 0f; }

        lock (_chainCacheLock)
        {
            if (_chainCache.Count >= MaxChainCacheSize) _chainCache.Clear();
            _chainCache[filterString] = (result, inflation);
        }
        inflationPx = inflation;
        return result;
    }

    private static float EntryInflation(FilterEntry entry)
    {
        switch (entry.Name)
        {
            case "blur":
            {
                float radius = 0;
                if (entry.Args.Length >= 1) TryFilterLength(entry.Args[0], out radius);
                return Math.Max(0f, radius) * 3f;
            }
            case "drop-shadow":
            {
                // Re-parse the tokens the same way CreateDropShadow does: dx dy [blur] color.
                var tokens = new List<string>();
                foreach (var a in entry.Args)
                    tokens.AddRange(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                float dx = 0, dy = 0, blur = 0;
                int idx = 0;
                if (tokens.Count > idx) { float.TryParse(tokens[idx].Replace("px", "").Trim(), out dx); idx++; }
                if (tokens.Count > idx) { float.TryParse(tokens[idx].Replace("px", "").Trim(), out dy); idx++; }
                if (tokens.Count > idx && float.TryParse(tokens[idx].Replace("px", "").Trim(), out var b)) { blur = b; }
                return blur * 3f + Math.Max(Math.Abs(dx), Math.Abs(dy));
            }
            default:
                return 0f;
        }
    }

    /// <summary>Split the filter list; returns null when the value is invalid.</summary>
    private static List<FilterEntry>? ParseFilters(string input)
    {
        var filters = new List<FilterEntry>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '(') depth++;
            else if (input[i] == ')') depth--;
            else if (input[i] == ' ' && depth == 0)
            {
                var part = input[start..i].Trim();
                if (!string.IsNullOrEmpty(part))
                {
                    var entry = ParseFilterEntry(part);
                    if (entry == null) return null;
                    filters.Add(entry);
                }
                start = i + 1;
            }
        }
        var last = input[start..].Trim();
        if (!string.IsNullOrEmpty(last))
        {
            var entry = ParseFilterEntry(last);
            if (entry == null) return null;
            filters.Add(entry);
        }
        return filters;
    }

    private static FilterEntry? ParseFilterEntry(string s)
    {
        var parenIdx = s.IndexOf('(');
        // Every filter function is written name(...) : a bare keyword such as
        // `grayscale` is not a valid <filter-function> and voids the declaration.
        if (parenIdx <= 0 || !s.EndsWith(')'))
            return null;
        var name = s[..parenIdx].Trim().ToLowerInvariant();
        var argStr = s[(parenIdx + 1)..^1].Trim();
        // Split by commas at depth 0 (respect nested parens)
        var args = SplitFilterArgs(argStr);
        return new FilterEntry { Name = name, Args = args };
    }

    private static string[] SplitFilterArgs(string argStr)
    {
        var args = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < argStr.Length; i++)
        {
            if (argStr[i] == '(') depth++;
            else if (argStr[i] == ')') depth--;
            else if (argStr[i] == ',' && depth == 0)
            {
                args.Add(argStr[start..i].Trim());
                start = i + 1;
            }
        }
        args.Add(argStr[start..].Trim());
        return args.Where(s => !string.IsNullOrEmpty(s)).ToArray();
    }

    private static SKImageFilter? CreateFilter(FilterEntry entry)
    {
        return entry.Name switch
        {
            "blur" => CreateBlur(entry.Args),
            "brightness" => CreateColorMatrix(entry.Args, BrightnessMatrix),
            "contrast" => CreateColorMatrix(entry.Args, ContrastMatrix),
            "saturate" => CreateColorMatrix(entry.Args, SaturateMatrix),
            "grayscale" => CreateGrayscale(entry.Args),
            "sepia" => CreateSepia(entry.Args),
            "invert" => CreateInvert(entry.Args),
            "hue-rotate" => CreateHueRotate(entry.Args),
            "opacity" => CreateOpacity(entry.Args),
            "drop-shadow" => CreateDropShadow(entry.Args),
            _ => null
        };
    }

    /// <summary>CSS Filters 1: effect amounts accept a &lt;number&gt; (0..1) or a
    /// &lt;percentage&gt;; the unitless form is the fraction itself, not a percent.</summary>
    internal static bool TryFilterAmount(string? value, out float amount)
    {
        amount = 0;
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return false;
        bool isPercent = text.EndsWith("%", StringComparison.OrdinalIgnoreCase);
        if (isPercent)
            text = text[..^1].Trim();
        if (!float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out amount))
            return false;
        if (isPercent)
            amount /= 100f;
        return true;
    }

    private static SKImageFilter? CreateBlur(string[] args)
    {
        // blur() with no argument is a zero-radius blur, not an invalid function.
        float radius = 0;
        if (args.Length >= 1 && !TryFilterLength(args[0], out radius))
            return null;
        radius = Math.Max(0, radius);
        return SKImageFilter.CreateBlur(radius, radius);
    }

    private static bool TryFilterLength(string text, out float value)
    {
        text = text.Trim();
        foreach (var unit in new[] { "px", "em", "rem", "%" })
        {
            if (text.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^unit.Length];
                break;
            }
        }
        return float.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static SKImageFilter? CreateColorMatrix(string[] args, Func<float, float[]> matrixFunc, float defaultAmount = 100f)
    {
        float amount = defaultAmount;
        if (args.Length >= 1)
        {
            var valStr = args[0].Trim();
            bool isPercent = valStr.EndsWith("%");
            valStr = valStr.Replace("%", "").Trim();
            if (!float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out amount))
                return null;

            // CSS: brightness(2) = 200% brightness, brightness(50%) = 50% brightness
            // If no % sign, treat as multiplier (e.g., 2 → 200%)
            if (!isPercent)
                amount *= 100f;
        }

        var matrix = matrixFunc(amount);
        var colorMatrix = SKColorFilter.CreateColorMatrix(matrix);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static float[] BrightnessMatrix(float amount)
    {
        amount /= 100f;
        return new float[]
        {
            amount, 0, 0, 0, 0,
            0, amount, 0, 0, 0,
            0, 0, amount, 0, 0,
            0, 0, 0, 1, 0
        };
    }

    private static float[] ContrastMatrix(float amount)
    {
        amount /= 100f;
        float t = (1f - amount) / 2f;
        return new float[]
        {
            amount, 0, 0, 0, t,
            0, amount, 0, 0, t,
            0, 0, amount, 0, t,
            0, 0, 0, 1, 0
        };
    }

    private static float[] SaturateMatrix(float amount)
    {
        amount /= 100f;
        float[] m = new float[20];
        m[0] = 0.2126f + 0.7874f * amount;
        m[1] = 0.7152f - 0.7152f * amount;
        m[2] = 0.0722f - 0.0722f * amount;
        m[5] = 0.2126f - 0.2126f * amount;
        m[6] = 0.7152f + 0.2848f * amount;
        m[7] = 0.0722f - 0.0722f * amount;
        m[10] = 0.2126f - 0.2126f * amount;
        m[11] = 0.7152f - 0.7152f * amount;
        m[12] = 0.0722f + 0.9278f * amount;
        m[15] = 1; m[16] = 1; m[17] = 1; m[18] = 1;
        return m;
    }

    private static SKImageFilter? CreateGrayscale(string[] args)
    {
        float amount = 1f;
        if (args.Length >= 1 && !TryFilterAmount(args[0], out amount))
            return null;
        // grayscale(a) == saturate(1 - a). The round trip through CreateColorMatrix's
        // string arguments lost the unit, so build the matrix from the fraction.
        float satPercent = Math.Clamp(1f - amount, 0f, 1f) * 100f;
        var filter = SKColorFilter.CreateColorMatrix(SaturateMatrix(satPercent));
        return SKImageFilter.CreateColorFilter(filter);
    }

    private static SKImageFilter? CreateSepia(string[] args)
    {
        float amount = 1f;
        if (args.Length >= 1 && !TryFilterAmount(args[0], out amount))
            return null;
        amount = Math.Clamp(amount, 0f, 1f);
        // sepia(a) is the linear blend between the identity matrix and the full
        // sepia matrix (CSS Filter Effects 1 §2.3).
        float[] full =
        {
            0.393f, 0.769f, 0.189f, 0, 0,
            0.349f, 0.686f, 0.168f, 0, 0,
            0.272f, 0.534f, 0.131f, 0, 0,
            0f, 0f, 0f, 1f, 0f
        };
        var m = new float[20];
        for (int row = 0; row < 3; row++)
        {
            int baseIdx = row * 5;
            for (int col = 0; col < 5; col++)
            {
                float identity = col == row ? 1f : 0f;
                m[baseIdx + col] = identity + (full[baseIdx + col] - identity) * amount;
            }
        }
        m[15] = 1f; m[16] = 1f; m[17] = 1f; m[18] = 1f; m[19] = 0f;
        var colorMatrix = SKColorFilter.CreateColorMatrix(m);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static SKImageFilter? CreateInvert(string[] args)
    {
        float amount = 1f;
        if (args.Length >= 1 && !TryFilterAmount(args[0], out amount))
            return null;
        amount = Math.Clamp(amount, 0f, 1f);
        // invert(a) maps c to c*(1-2a) + a, i.e. a blend with the negative matrix.
        float scale = 1f - 2f * amount;
        // Row-major 4x5: the diagonal is R,G,B at 0, 6 and 12 (not 5/10, which are
        // the R coefficients of the next row).
        var m = new float[20];
        m[0] = scale; m[6] = scale; m[12] = scale;
        m[4] = amount; m[9] = amount; m[14] = amount;
        m[18] = 1f;
        var colorMatrix = SKColorFilter.CreateColorMatrix(m);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static SKImageFilter? CreateHueRotate(string[] args)
    {
        var valStr = args.Length >= 1 ? args[0].Trim() : "0";
        float unitScale = 1f;
        if (valStr.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
        {
            unitScale = 180f / MathF.PI;
            valStr = valStr[..^3];
        }
        else if (valStr.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
        {
            unitScale = 0.9f;
            valStr = valStr[..^4];
        }
        else if (valStr.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            unitScale = 360f;
            valStr = valStr[..^4];
        }
        else if (valStr.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            valStr = valStr[..^3];
        }
        if (!float.TryParse(valStr.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var degrees)) return null;
        degrees *= unitScale;

        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        float[] m = new float[]
        {
            0.213f + cos * 0.787f - sin * 0.213f,
            0.715f - cos * 0.715f - sin * 0.715f,
            0.072f - cos * 0.072f + sin * 0.928f, 0, 0,
            0.213f - cos * 0.213f + sin * 0.143f,
            0.715f + cos * 0.285f + sin * 0.140f,
            0.072f - cos * 0.072f - sin * 0.283f, 0, 0,
            0.213f - cos * 0.213f - sin * 0.787f,
            0.715f - cos * 0.715f + sin * 0.715f,
            0.072f + cos * 0.928f + sin * 0.072f, 0, 0,
            0, 0, 0, 1, 0
        };
        var colorMatrix = SKColorFilter.CreateColorMatrix(m);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static SKImageFilter? CreateOpacity(string[] args)
    {
        // opacity(0.5) is 50%; the previous code divided every value by 100, which
        // made the element almost fully transparent. Bare opacity() is fully opaque.
        float amount = 1f;
        if (args.Length >= 1 && !TryFilterAmount(args[0], out amount)) return null;
        amount = Math.Clamp(amount, 0f, 1f);

        float[] m = new float[]
        {
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, amount, 0
        };
        var colorMatrix = SKColorFilter.CreateColorMatrix(m);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static SKImageFilter? CreateDropShadow(string[] args)
    {
        float offsetX = 0, offsetY = 0, blur = 0;
        SKColor color = SKColors.Black;

        // Split all args by spaces (drop-shadow uses space-separated values)
        var tokens = new List<string>();
        foreach (var a in args)
        {
            var parts = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            tokens.AddRange(parts);
        }

        // drop-shadow() requires at least the two offsets.
        if (tokens.Count < 2) return null;
        int idx = 0;
        if (tokens.Count > idx) float.TryParse(tokens[idx].Replace("px", "").Trim(), out offsetX); idx++;
        if (tokens.Count > idx) float.TryParse(tokens[idx].Replace("px", "").Trim(), out offsetY); idx++;
        if (tokens.Count > idx)
        {
            var blurStr = tokens[idx].Replace("px", "").Trim();
            if (float.TryParse(blurStr, out var b)) { blur = b; idx++; }
        }
        if (tokens.Count > idx)
        {
            var col = tokens[idx].Trim();
            // If it looks like a color function, join remaining tokens
            if (col.StartsWith("rgb") || col.StartsWith('#'))
            {
                color = ParseFilterColor(col) ?? SKColors.Black;
            }
        }

        // drop-shadow() takes a blur radius; the Gaussian standard deviation is half of it.
        return SKImageFilter.CreateDropShadow(offsetX, offsetY, blur / 2f, blur / 2f, color);
    }

    private static SKColor? ParseFilterColor(string s)
    {
        s = s.Trim();
        if (s.StartsWith("#") && SKColor.TryParse(s, out var c)) return c;
        if (s.StartsWith("rgba") || s.StartsWith("rgb"))
        {
            try
            {
                var inner = s[s.IndexOf('(')..].Trim('(', ')');
                var parts = inner.Split(',');
                if (parts.Length >= 3)
                {
                    byte r = byte.Parse(parts[0].Trim());
                    byte g = byte.Parse(parts[1].Trim());
                    byte b = byte.Parse(parts[2].Trim());
                    byte a = parts.Length > 3 ? (byte)(float.Parse(parts[3].Trim()) * 255) : (byte)255;
                    return new SKColor(r, g, b, a);
                }
            }
            catch { }
        }
        return null;
    }

    private class FilterEntry
    {
        public string Name { get; set; } = "";
        public string[] Args { get; set; } = Array.Empty<string>();
    }
}
