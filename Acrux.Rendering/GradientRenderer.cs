using SkiaSharp;
using Acrux.Core.Css;

namespace Acrux.Rendering;

public static class GradientRenderer
{
    /// <summary>
    /// Rasterize an image value (gradient) into a bitmap of |area|. Generated images
    /// have no intrinsic size, so the target box defines their natural dimensions.
    /// </summary>
    public static SKImage? RasterizeToImage(string value, SKRect area)
    {
        float width = MathF.Max(1, MathF.Round(area.Width));
        float height = MathF.Max(1, MathF.Round(area.Height));
        var shader = CreateGradient(value, new SKRect(0, 0, width, height));
        if (shader == null) return null;
        var info = new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null) return null;
        using var paint = new SKPaint { Shader = shader };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(0, 0, width, height, paint);
        return surface.Snapshot();
    }

    public static SKShader? CreateGradient(string gradientString, SKRect rect)
    {
        if (string.IsNullOrEmpty(gradientString)) return null;

        if (gradientString.Contains("linear-gradient", StringComparison.OrdinalIgnoreCase))
            return CreateLinearGradient(gradientString, rect);
        if (gradientString.Contains("radial-gradient", StringComparison.OrdinalIgnoreCase))
            return CreateRadialGradient(gradientString, rect);
        if (gradientString.Contains("conic-gradient", StringComparison.OrdinalIgnoreCase))
            return CreateConicGradient(gradientString, rect);

        return null;
    }

    private static SKShader? CreateLinearGradient(string input, SKRect rect)
    {
        try
        {
            var inner = ExtractGradientContent(input, "linear-gradient");
            if (inner == null) return null;
            bool repeating = input.StartsWith("repeating-", StringComparison.OrdinalIgnoreCase);

            float angle = 180f;
            var parts = SplitGradientParts(inner);

            // A failed parse must not clobber the default: TryParseAngle assigns 0 to its
            // out parameter, which would silently flip 'linear-gradient(red, blue)'.
            if (parts.Count > 0 && TryParseAngle(parts[0], out var directionAngle))
            {
                angle = directionAngle;
                parts.RemoveAt(0);
            }

            var stops = ParseColorStops(parts);
            if (stops.Count == 0) return null;

            var (startPoint, endPoint) = CalculateLinearPoints(angle, rect);

            if (repeating)
            {
                // A repeating gradient tiles the span defined by its explicit
                // lengths: the gradient line runs from the first to the last
                // explicit px stop and repeats.
                float spanPx = stops.Where(s => s.Px >= 0).DefaultIfEmpty(new ColorStop { Px = 0 }).Max(s => s.Px);
                if (spanPx <= 0) return null;
                var dir = new SKPoint(endPoint.X - startPoint.X, endPoint.Y - startPoint.Y);
                float full = MathF.Max(1f, MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
                var rcolors = stops.Select(s => s.Color).ToArray();
                var rpos = stops.Select(s => s.Px >= 0 ? s.Px / spanPx : (s.Position >= 0 ? s.Position : 1f)).ToArray();
                var shader = SKShader.CreateLinearGradient(
                    startPoint, endPoint, rcolors, rpos, SKShaderTileMode.Repeat);
                // Scale the unit gradient line down to the repeating span.
                var m = SKMatrix.CreateScale(spanPx / full, spanPx / full, startPoint.X, startPoint.Y);
                return shader.WithLocalMatrix(m);
            }

            var colors = stops.Select(s => s.Color).ToArray();
            float lineLen = MathF.Max(1f, Dist(startPoint, endPoint));
            var positions = stops.Select(s => s.Px >= 0 ? Math.Clamp(s.Px / lineLen, 0f, 1f) : s.Position).ToArray();

            return SKShader.CreateLinearGradient(
                new SKPoint(startPoint.X, startPoint.Y),
                new SKPoint(endPoint.X, endPoint.Y),
                colors, positions, SKShaderTileMode.Clamp);
        }
        catch { return null; }
    }

    private static float Dist(SKPoint a, SKPoint b) =>
        MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static SKShader? CreateRadialGradient(string input, SKRect rect)
    {
        try
        {
            var inner = ExtractGradientContent(input, "radial-gradient");
            if (inner == null) return null;

            var parts = SplitGradientParts(inner);
            if (parts.Count == 0) return null;

            float cx = rect.MidX, cy = rect.MidY;
            float radius = MathF.Max(rect.Width, rect.Height) / 2f;

            // The first part is the position/size preamble when it carries no
            // color (starts with `circle`/`ellipse` or contains `at`).
            int stopsStart = 0;
            var first = parts[0];
            bool isPreamble = (first.StartsWith("circle", StringComparison.OrdinalIgnoreCase) ||
                               first.StartsWith("ellipse", StringComparison.OrdinalIgnoreCase) ||
                               first.Contains("at ", StringComparison.OrdinalIgnoreCase)) &&
                              ParseColor(first) == null;
            if (isPreamble)
            {
                stopsStart = 1;
                int atIdx = first.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                string sizePart = atIdx >= 0 ? first[..atIdx] : first;
                if (atIdx >= 0)
                {
                    var posTokens = first[(atIdx + 4)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (posTokens.Length >= 2 &&
                        TryGradientCoord(posTokens[0], rect.Width, out var dx) &&
                        TryGradientCoord(posTokens[1], rect.Height, out var dy))
                    {
                        cx = rect.Left + dx;
                        cy = rect.Top + dy;
                    }
                }
                // Default radius: farthest-corner from the center.
                radius = FarthestCornerRadius(cx, cy, rect);
                var sizeTokens = sizePart.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => !t.Equals("circle", StringComparison.OrdinalIgnoreCase) &&
                                !t.Equals("ellipse", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (sizeTokens.Length >= 1 && !sizeTokens[0].Equals("at", StringComparison.OrdinalIgnoreCase))
                {
                    var kw = sizeTokens[0].ToLowerInvariant();
                    if (kw == "closest-side")
                        radius = Math.Min(Math.Min(cx - rect.Left, rect.Right - cx), Math.Min(cy - rect.Top, rect.Bottom - cy));
                    else if (kw == "farthest-side")
                        radius = Math.Max(Math.Max(cx - rect.Left, rect.Right - cx), Math.Max(cy - rect.Top, rect.Bottom - cy));
                    else if (kw == "closest-corner")
                        radius = ClosestCornerRadius(cx, cy, rect);
                    else if (kw == "farthest-corner")
                        radius = FarthestCornerRadius(cx, cy, rect);
                    else if (TryGradientCoord(kw, rect.Width, out var rv))
                        radius = rv;
                }
            }

            var stops = ParseColorStops(parts.Skip(stopsStart).ToList());
            if (stops.Count == 0) return null;

            float lineLen = MathF.Max(1f, radius);
            var colors = stops.Select(s => s.Color).ToArray();
            var positions = stops.Select(s => s.Px >= 0 ? Math.Clamp(s.Px / lineLen, 0f, 1f) : s.Position).ToArray();

            return SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), MathF.Max(1f, radius),
                colors, positions, SKShaderTileMode.Clamp);
        }
        catch { return null; }
    }

    private static bool TryGradientCoord(string token, float basis, out float value)
    {
        token = token.Trim();
        if (token.EndsWith("%"))
        {
            if (float.TryParse(token[..^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                value = pct / 100f * basis;
                return true;
            }
            value = 0;
            return false;
        }
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return float.TryParse(token[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        return float.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static float FarthestCornerRadius(float cx, float cy, SKRect rect)
    {
        float dx = Math.Max(cx - rect.Left, rect.Right - cx);
        float dy = Math.Max(cy - rect.Top, rect.Bottom - cy);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float ClosestCornerRadius(float cx, float cy, SKRect rect)
    {
        float best = float.MaxValue;
        foreach (var corner in new[] { new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Top),
                                       new SKPoint(rect.Left, rect.Bottom), new SKPoint(rect.Right, rect.Bottom) })
        {
            float d = Dist(new SKPoint(cx, cy), corner);
            if (d < best) best = d;
        }
        return best;
    }

    private static SKShader? CreateConicGradient(string input, SKRect rect)
    {
        try
        {
            bool repeating = input.StartsWith("repeating-", StringComparison.OrdinalIgnoreCase);
            var inner = ExtractGradientContent(input, "conic-gradient");
            if (inner == null) return null;

            var parts = SplitGradientParts(inner);

            // The preamble carries the start angle and the center:
            // 'conic-gradient(from 45deg at 30% 70%, …)'. It is only a preamble
            // when it says so — a first stop like "red 0 25%" must not be eaten.
            float fromAngle = 0f;
            var center = new SKPoint(rect.MidX, rect.MidY);
            if (parts.Count > 0 && ParseColor(parts[0]) == null &&
                (parts[0].StartsWith("from", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Contains(" at ", StringComparison.OrdinalIgnoreCase)))
            {
                var preamble = parts[0];
                int fromIdx = preamble.IndexOf("from ", StringComparison.OrdinalIgnoreCase);
                if (fromIdx >= 0)
                {
                    var angleToken = preamble[(fromIdx + 5)..];
                    int at = angleToken.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                    if (at >= 0) angleToken = angleToken[..at];
                    TryParseAngle(angleToken, out fromAngle);
                }
                int atIdx = preamble.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                if (atIdx >= 0)
                {
                    var posTokens = preamble[(atIdx + 4)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (posTokens.Length >= 2 &&
                        TryGradientCoord(posTokens[0], rect.Width, out var dx) &&
                        TryGradientCoord(posTokens[1], rect.Height, out var dy))
                        center = new SKPoint(rect.Left + dx, rect.Top + dy);
                }
                parts.RemoveAt(0);
            }

            var stops = ParseColorStops(parts);
            if (stops.Count == 0) return null;

            var colors = stops.Select(s => s.Color).ToArray();
            // A conic stop position is a fraction of the full turn; Skia also
            // requires the positions to be non-decreasing.
            var positions = stops.Select(s => Math.Clamp(
                s.Position >= 0 ? s.Position : (s.Px >= 0 ? 0f : 1f), 0f, 1f)).ToArray();
            for (int i = 1; i < positions.Length; i++)
                positions[i] = Math.Max(positions[i], positions[i - 1]);

            var sweep = SKShader.CreateSweepGradient(center, colors, positions,
                repeating ? SKShaderTileMode.Repeat : SKShaderTileMode.Clamp, 0f, 360f);

            // CSS conic angles are measured clockwise from 12 o'clock while Skia's
            // sweep starts at 3 o'clock.
            var rotation = SKMatrix.CreateRotationDegrees(-90f + fromAngle, center.X, center.Y);
            return sweep.WithLocalMatrix(rotation);
        }
        catch { return null; }
    }

    private static string? ExtractGradientContent(string input, string gradientType)
    {
        int start = input.IndexOf(gradientType, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start = input.IndexOf('(', start);
        if (start < 0) return null;
        int end = FindMatchingParen(input, start);
        if (end < 0) return null;
        return input[(start + 1)..end];
    }

    private static int FindMatchingParen(string s, int openIndex)
    {
        int depth = 1;
        for (int i = openIndex + 1; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static List<string> SplitGradientParts(string inner)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '(') depth++;
            else if (inner[i] == ')') depth--;
            else if (inner[i] == ',' && depth == 0)
            {
                parts.Add(inner[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < inner.Length)
            parts.Add(inner[start..].Trim());
        return parts;
    }

    private static bool TryParseAngle(string s, out float angle)
    {
        s = s.Trim().ToLowerInvariant();
        // CSS direction keywords
        if (s == "to top") { angle = 0; return true; }
        if (s == "to right") { angle = 90; return true; }
        if (s == "to bottom") { angle = 180; return true; }
        if (s == "to left") { angle = 270; return true; }
        if (s == "to top right" || s == "to right top") { angle = 45; return true; }
        if (s == "to top left" || s == "to left top") { angle = 315; return true; }
        if (s == "to bottom right" || s == "to right bottom") { angle = 135; return true; }
        if (s == "to bottom left" || s == "to left bottom") { angle = 225; return true; }

        if (s.EndsWith("deg"))
        {
            if (float.TryParse(s[..^3], out angle)) return true;
        }
        if (s.EndsWith("rad"))
        {
            if (float.TryParse(s[..^3], out var rad)) { angle = rad * 180f / MathF.PI; return true; }
        }
        if (float.TryParse(s, out angle)) return true;
        angle = 0;
        return false;
    }

    private static (SKPoint start, SKPoint end) CalculateLinearPoints(float angle, SKRect rect)
    {
        float rad = (angle - 90) * MathF.PI / 180f;
        float cx = rect.MidX, cy = rect.MidY;
        float halfW = rect.Width / 2f, halfH = rect.Height / 2f;

        // CSS Images 3 2.2: the gradient line has to be long enough that the two corners
        // perpendicular to it are covered by 0% and 100%, i.e. half its length is
        // |W*sin(a)|/2 + |H*cos(a)|/2 measured from the centre. Using the diagonal here
        // compresses every gradient whose box is not square.
        float theta = angle * MathF.PI / 180f;
        float length = (MathF.Abs(rect.Width * MathF.Sin(theta)) + MathF.Abs(rect.Height * MathF.Cos(theta))) / 2f;

        float dx = MathF.Cos(rad) * length;
        float dy = MathF.Sin(rad) * length;

        return (new SKPoint(cx - dx, cy - dy), new SKPoint(cx + dx, cy + dy));
    }

    private static List<ColorStop> ParseColorStops(List<string> parts)
    {
        var stops = new List<ColorStop>();
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            var spaceIdx = FindColorStopSplit(p);
            string colorPart = spaceIdx > 0 ? p[..spaceIdx].Trim() : p;
            string posPart = spaceIdx > 0 ? p[spaceIdx..].Trim() : "";

            var color = ParseColor(colorPart);
            if (!color.HasValue) continue;

            float position = -1;
            float px = -1;
            float pxEnd = -1;
            float positionEnd = -1;
            // A stop may carry two position tokens ("#000 0 10px" or
            // "red 0 25%" — a hard-stop range); the first is the stop's own
            // position, the second the end of its flat span.
            var posTokens = posPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (posTokens.Length > 0 && TryStopPosition(posTokens[0], out position, out px)) { }
            if (posTokens.Length > 1)
            {
                var endToken = posTokens[1];
                if (endToken.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(endToken[..^2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pxev))
                    pxEnd = pxev;
                else
                    TryStopPosition(endToken, out positionEnd, out _);
            }

            stops.Add(new ColorStop
            {
                Color = color.Value,
                Position = position,
                Px = px,
                PxEnd = pxEnd,
                PositionEnd = positionEnd,
            });
        }

        // Expand double-position stops: "red 0 20px" becomes a flat red span
        // from 0 to 20px followed by a hard edge (the same color repeated at
        // the end position, so the next stop starts blending from there).
        for (int i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            if (s.PxEnd > 0 && s.PxEnd > s.Px)
            {
                stops.Insert(i + 1, new ColorStop
                {
                    Color = s.Color,
                    Position = -1,
                    Px = s.PxEnd,
                    PxEnd = -1,
                });
                i++;
            }
            else if (s.PositionEnd > 0 && (s.Position < 0 || s.PositionEnd > s.Position))
            {
                stops.Insert(i + 1, new ColorStop
                {
                    Color = s.Color,
                    Position = s.PositionEnd,
                    Px = -1,
                    PxEnd = -1,
                });
                i++;
            }
        }

        if (stops.Count > 0)
        {
            bool Positioned(ColorStop s) => s.Position >= 0 || s.Px >= 0;

            // First: set first stop to 0% and last stop to 100% if unspecified
            if (!Positioned(stops[0])) stops[0] = new ColorStop { Color = stops[0].Color, Position = 0f, Px = -1 };
            if (!Positioned(stops[^1])) stops[^1] = new ColorStop { Color = stops[^1].Color, Position = 1f, Px = -1 };

            // Distribute remaining unpositioned stops evenly between known positions
            for (int i = 0; i < stops.Count; i++)
            {
                if (Positioned(stops[i])) continue;

                int start = i - 1;
                // find the next assigned position
                int end = stops.FindIndex(i + 1, s => Positioned(s) && (s.Position >= 0 || s.Px >= 0));
                if (end < 0) end = stops.Count - 1;
                if (start < 0) start = 0;

                float startPos = EffectiveFraction(stops[start], 1f);
                float endPos = EffectiveFraction(stops[end], 1f);
                int count = Math.Max(1, end - start);
                float step = (endPos - startPos) / count;
                for (int j = start + 1; j < end; j++)
                {
                    stops[j] = new ColorStop { Color = stops[j].Color, Position = startPos + step * (j - start), Px = -1 };
                }
                i = end; // skip ahead
            }
        }

        return stops;
    }

    // A stop's position as a 0..1 fraction; px values need the gradient length
    // and are resolved by the caller, so fall back to the fraction when known.
    private static float EffectiveFraction(ColorStop s, float fallback) =>
        s.Position >= 0 ? s.Position : fallback;

    private static int FindColorStopSplit(string s)
    {
        // The color comes first; the position (possibly several tokens) after
        // it. Find the first space outside parentheses — functional colors like
        // rgb(0, 0, 0) keep their inner spaces.
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (depth == 0 && s[i] == ' ')
                return i;
        }
        return -1;
    }

    private static SKColor? ParseColor(string s)
    {
        s = s.Trim();
        if (string.IsNullOrEmpty(s)) return null;

        if (s.StartsWith('#'))
        {
            if (SKColor.TryParse(s, out var c)) return c;
        }

        var namedColor = ColorParser.Parse(s);
        // ColorParser.Parse falls back to black for unknown names; only accept
        // the result when the token is a real color keyword.
        if (ColorParser.IsColorName(s))
            return namedColor;

        if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var parts = s.Replace("rgb", "").Replace("a", "").Replace("(", "").Replace(")", "").Split(',');
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

    private struct ColorStop
    {
        public SKColor Color;
        public float Position;
        /// <summary>Explicit pixel position (first position token), or -1.</summary>
        public float Px;
        /// <summary>Explicit end pixel of a double-position (hard) stop, or -1.</summary>
        public float PxEnd;
        /// <summary>Explicit end fraction of a double-position (hard) stop, or -1.</summary>
        public float PositionEnd;
    }

    /// <summary>
    /// One color-stop position token. Percentages and angles are fractions — of the
    /// gradient line for linear and radial gradients, of a full turn for conic ones —
    /// while a length stays in pixels. A unitless zero is zero in every unit, so it
    /// resolves as a fraction (CSS Values 4 §8.6).
    /// </summary>
    private static bool TryStopPosition(string token, out float fraction, out float px)
    {
        fraction = -1; px = -1;
        var t = token.Trim().ToLowerInvariant();
        if (t.Length == 0) return false;
        bool Number(string s, out float v) => float.TryParse(s,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        if (t.EndsWith('%')) { if (Number(t[..^1], out var pct)) { fraction = pct / 100f; return true; } return false; }
        if (t.EndsWith("deg")) { if (Number(t[..^3], out var deg)) { fraction = deg / 360f; return true; } return false; }
        if (t.EndsWith("grad")) { if (Number(t[..^4], out var grad)) { fraction = grad / 400f; return true; } return false; }
        if (t.EndsWith("turn")) { if (Number(t[..^4], out var turn)) { fraction = turn; return true; } return false; }
        if (t.EndsWith("rad")) { if (Number(t[..^3], out var rad)) { fraction = rad / (2f * MathF.PI); return true; } return false; }
        if (t.EndsWith("px")) { if (Number(t[..^2], out var pxv)) { px = pxv; return true; } return false; }
        if (Number(t, out var bare))
        {
            if (bare == 0) fraction = 0;
            else px = bare;
            return true;
        }
        return false;
    }
}
