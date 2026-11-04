using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Parses a CSS clip-path into an SKPath in the element's border-box space and
/// applies it as a display-list clip operation. Mirrors clip_path_clipper.cc.
/// </summary>
internal static class ClipPathClipper
{
    public static bool HasClipPath(string? clipPath) =>
        !string.IsNullOrEmpty(clipPath) && clipPath != "none";

    public static SKPath? Parse(string? clipPath, LayoutBox box)
    {
        if (string.IsNullOrEmpty(clipPath)) return null;

        var path = new SKPath();
        var rect = box.BorderBox;
        string inner;
        string shapeFunc;

        // Extract the shape function name and inner content, handling optional
        // geometry-box suffix (e.g. "circle(50%) border-box").
        int parenIndex = clipPath.IndexOf('(');
        if (parenIndex < 0)
        {
            // A geometry box named on its own is the whole clip — CSS Masking 1 §5.2 lets
            // 'clip-path: content-box' say the rectangle without a shape (measured: all seven boxes
            // stand, and each reads back as written), so the element is cut to that box.
            var bare = clipPath.Trim().ToLowerInvariant();
            if (Array.Exists(GeometryBoxes, b => b == bare))
            {
                path.AddRect(AdjustRectForGeometryBox(bare, box));
                return path;
            }

            path.Dispose();
            return null;
        }

        shapeFunc = clipPath[..parenIndex].Trim();
        int endParen = FindMatchingParen(clipPath, parenIndex);
        if (endParen < 0) { path.Dispose(); return null; }

        inner = clipPath[(parenIndex + 1)..endParen].Trim();

        // Check for geometry-box keyword after the closing paren
        string remainder = clipPath[(endParen + 1)..].Trim();
        if (!string.IsNullOrEmpty(remainder))
        {
            rect = AdjustRectForGeometryBox(remainder.ToLowerInvariant(), box);
        }

        // A shape function that received tokens it cannot use makes the whole
        // declaration invalid (CSS Shapes 1 4): the geometry box belongs after the
        // closing paren, never inside it.
        if (HasStrayToken(inner))
        {
            path.Dispose();
            return null;
        }

        switch (shapeFunc)
        {
            case "circle":
                ParseCircle(inner, rect, path);
                break;
            case "ellipse":
                ParseEllipse(inner, rect, path);
                break;
            case "inset":
                ParseInset(inner, rect, path);
                break;
            case "polygon":
                ParsePolygon(inner, rect, path);
                break;
            case "rect":
                ParseRect(inner, rect, path);
                break;
            case "xywh":
                ParseXywh(inner, rect, path);
                break;
            default:
                path.Dispose();
                return null;
        }

        return path;
    }

    private static readonly string[] ShapeKeywords = { "at", "round", "fill", "nonzero", "evenodd" };
    private static readonly string[] GeometryBoxes =
    {
        "border-box", "padding-box", "content-box", "margin-box", "fill-box", "stroke-box", "view-box",
    };

    /// <summary>
    /// True when the shape's argument list holds a token no shape can consume: a
    /// geometry-box keyword (which is only legal after the closing paren) or a bare
    /// word that is neither a grammar keyword nor a number with a known unit.
    /// </summary>
    private static bool HasStrayToken(string inner)
    {
        foreach (var raw in inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim();
            if (token.Length == 0) continue;
            if (Array.Exists(ShapeKeywords, k => token.Equals(k, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (token.Contains("calc", StringComparison.OrdinalIgnoreCase)) continue;
            if (Array.Exists(GeometryBoxes, b => token.Equals(b, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (!LooksLikeLength(token))
                return true;
        }
        return false;
    }

    private static bool LooksLikeLength(string token)
    {
        int end = 0;
        while (end < token.Length && (char.IsDigit(token[end]) || token[end] is '.' or '-' or '+'))
            end++;
        if (end == 0) return false;
        string unit = token[end..].ToLowerInvariant();
        if (unit.Length == 0 || unit == "%")
            return float.TryParse(token[..end], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        return unit is "px" or "em" or "rem" or "vw" or "vh" or "vmin" or "vmax" or "pt" or "pc" or "in"
            or "cm" or "mm" or "q" or "ex" or "ch" or "deg" or "rad" or "grad" or "turn";
    }

    private static int FindMatchingParen(string s, int openIndex)    {
        int depth = 0;
        for (int i = openIndex; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static SKRect AdjustRectForGeometryBox(string geometryBox, LayoutBox box)
    {
        switch (geometryBox)
        {
            case "padding-box":
                return new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, box.PaddingBox.Bottom);
            case "content-box":
                return new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right, box.ContentBox.Bottom);
            case "margin-box":
                return new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right, box.MarginBox.Bottom);
            default: // border-box
                return new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, box.BorderBox.Bottom);
        }
    }

    private static void ParseCircle(string inner, SKRect rect, SKPath path)
    {
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float radius = Math.Min(rect.Width, rect.Height) / 2f;
        float cx = rect.MidX, cy = rect.MidY;
        if (parts.Length >= 1)
        {
            var rStr = parts[0].Trim();
            if (rStr.EndsWith("%"))
            {
                if (float.TryParse(rStr[..^1].Trim(), out var pct))
                    radius = Math.Min(rect.Width, rect.Height) * pct / 100f;
            }
            else
            {
                float.TryParse(rStr.Replace("px", "").Trim(), out radius);
            }
        }
        int atIdx = Array.FindIndex(parts, p => p.Equals("at", StringComparison.OrdinalIgnoreCase));
        if (atIdx >= 0 && parts.Length > atIdx + 2)
        {
            ParseCenter(parts[atIdx + 1], parts[atIdx + 2], rect, ref cx, ref cy);
        }
        path.AddCircle(cx, cy, Math.Max(0, radius));
    }

    private static void ParseEllipse(string inner, SKRect rect, SKPath path)
    {
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float rx = rect.Width / 2f, ry = rect.Height / 2f;
        float cx = rect.MidX, cy = rect.MidY;
        if (parts.Length >= 2)
        {
            var rxStr = parts[0];
            if (rxStr.EndsWith("%"))
            {
                if (float.TryParse(rxStr[..^1].Trim(), out var pct))
                    rx = rect.Width * pct / 100f;
            }
            else
            {
                float.TryParse(rxStr.Replace("px", "").Trim(), out rx);
            }
            var ryStr = parts[1];
            if (ryStr.EndsWith("%"))
            {
                if (float.TryParse(ryStr[..^1].Trim(), out var pct))
                    ry = rect.Height * pct / 100f;
            }
            else
            {
                float.TryParse(ryStr.Replace("px", "").Trim(), out ry);
            }
        }
        int atIdx = Array.FindIndex(parts, p => p.Equals("at", StringComparison.OrdinalIgnoreCase));
        if (atIdx >= 0 && parts.Length > atIdx + 2)
        {
            ParseCenter(parts[atIdx + 1], parts[atIdx + 2], rect, ref cx, ref cy);
        }
        path.AddOval(new SKRect(cx - Math.Max(0, rx), cy - Math.Max(0, ry), cx + Math.Max(0, rx), cy + Math.Max(0, ry)));
    }

    private static void ParseInset(string inner, SKRect rect, SKPath path)
    {
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        float top = 0, right = 0, bottom = 0, left = 0;
        int count = 0;
        var rawValues = new List<string>();
        foreach (var p in parts)
        {
            if (p.Equals("round", StringComparison.OrdinalIgnoreCase)) break;
            rawValues.Add(p);
            float val = 0;
            var trimmed = p.Replace("px", "").Trim();
            if (trimmed.EndsWith("%"))
            {
                if (float.TryParse(trimmed[..^1].Trim(), out var pct))
                    val = pct / 100f;
            }
            else
            {
                float.TryParse(trimmed, out val);
            }
            if (count == 0) top = val;
            else if (count == 1) right = val;
            else if (count == 2) bottom = val;
            else if (count == 3) left = val;
            count++;
        }
        if (count == 1) right = bottom = left = top;
        else if (count == 2) { bottom = top; left = right; }
        else if (count == 3) { left = right; }

        if (top < 1 && top >= 0 && rawValues[0].EndsWith("%")) top = rect.Height * top;
        if (right < 1 && right >= 0 && rawValues[Math.Min(1, rawValues.Count - 1)].EndsWith("%")) right = rect.Width * right;
        if (bottom < 1 && bottom >= 0 && rawValues[Math.Min(2, rawValues.Count - 1)].EndsWith("%")) bottom = rect.Height * bottom;
        if (left < 1 && left >= 0 && rawValues[Math.Min(3, rawValues.Count - 1)].EndsWith("%")) left = rect.Width * left;

        var insetRect = new SKRect(rect.Left + left, rect.Top + top, rect.Right - right, rect.Bottom - bottom);
        path.AddRect(insetRect);
    }

    private static void ParsePolygon(string inner, SKRect rect, SKPath path)
    {
        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
        int firstPt = 0;
        if (parts.Length > 0)
        {
            var trimmed = parts[0].Trim();
            if (trimmed.Equals("fill", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("nonzero", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("evenodd", StringComparison.OrdinalIgnoreCase))
                firstPt = 1;
        }
        bool first = true;
        for (int pi = firstPt; pi < parts.Length; pi++)
        {
            var coords = parts[pi].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (coords.Length < 2) continue;
            float x = 0, y = 0;
            if (coords[0].EndsWith("%"))
            {
                if (float.TryParse(coords[0][..^1].Trim(), out var pct))
                    x = rect.Left + rect.Width * pct / 100f;
            }
            else
            {
                float.TryParse(coords[0].Replace("px", "").Trim(), out x);
                x += rect.Left;
            }
            if (coords[1].EndsWith("%"))
            {
                if (float.TryParse(coords[1][..^1].Trim(), out var pct))
                    y = rect.Top + rect.Height * pct / 100f;
            }
            else
            {
                float.TryParse(coords[1].Replace("px", "").Trim(), out y);
                y += rect.Top;
            }
            if (first) { path.MoveTo(x, y); first = false; }
            else path.LineTo(x, y);
        }
        path.Close();
    }

    private static void ParseRect(string inner, SKRect rect, SKPath path)
    {
        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
        float top = 0, right = rect.Width, bottom = rect.Height, left = 0;
        if (parts.Length >= 1) float.TryParse(parts[0].Replace("px", "").Trim(), out top);
        if (parts.Length >= 2) float.TryParse(parts[1].Replace("px", "").Trim(), out right);
        if (parts.Length >= 3) float.TryParse(parts[2].Replace("px", "").Trim(), out bottom);
        if (parts.Length >= 4) float.TryParse(parts[3].Replace("px", "").Trim(), out left);
        var r = new SKRect(rect.Left + left, rect.Top + top, rect.Left + right, rect.Top + bottom);
        path.AddRect(r);
    }

    /// <summary>'xywh(x y width height)' is the box at that offset inside the reference rectangle
    /// (CSS Shapes 1 §4.6): the position and the size each measure along their own axis, so a
    /// percentage of the x or the width is a share of the box's width and a percentage of the y or
    /// the height a share of its height.</summary>
    private static void ParseXywh(string inner, SKRect rect, SKPath path)
    {
        var parts = inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            // Nothing was said about a rectangle: the box keeps all of itself rather than vanish.
            path.AddRect(rect);
            return;
        }
        float x = ShapeOffset(parts[0], rect.Width);
        float y = ShapeOffset(parts[1], rect.Height);
        float width = ShapeOffset(parts[2], rect.Width);
        float height = ShapeOffset(parts[3], rect.Height);
        path.AddRect(new SKRect(rect.Left + x, rect.Top + y, rect.Left + x + width,
            rect.Top + y + height));
    }

    /// <summary>One argument of a shape measured along an axis of the reference box: a percentage of
    /// that axis, or the length the page wrote.</summary>
    private static float ShapeOffset(string token, float axis)
    {
        token = token.Trim();
        if (token.EndsWith("%", StringComparison.Ordinal))
            return float.TryParse(token[..^1].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var percent)
                ? percent * axis / 100f : 0f;
        return float.TryParse(token.Replace("px", "").Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var length) ? length : 0f;
    }

    private static void ParseCenter(string xStr, string yStr, SKRect rect, ref float cx, ref float cy)
    {
        if (xStr.EndsWith("%"))
        {
            if (float.TryParse(xStr[..^1].Trim(), out var pct))
                cx = rect.Left + rect.Width * pct / 100f;
        }
        else
        {
            if (float.TryParse(xStr.Replace("px", "").Trim(), out var v))
                cx = rect.Left + v;
        }
        if (yStr.EndsWith("%"))
        {
            if (float.TryParse(yStr[..^1].Trim(), out var pct))
                cy = rect.Top + rect.Height * pct / 100f;
        }
        else
        {
            if (float.TryParse(yStr.Replace("px", "").Trim(), out var v))
                cy = rect.Top + v;
        }
    }
}