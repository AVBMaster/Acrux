using SkiaSharp;

namespace Acrux.Rendering;

/// <summary>
/// Placement of one mask layer inside its element (CSS Masking 1 §5.1, §7.1 and §9 — the
/// geometry is the background algorithm applied to a mask instead of a paint source, so the
/// same rules for sizing, positioning, tiling and clipping apply).
///
/// This lives apart from <see cref="MaskPainter"/> because the answer is consumed twice —
/// once to size the tiles and once to clip them — and because the same question has to be
/// answered for a layer written inline in the 'mask' shorthand and for one set through the
/// longhands.
/// </summary>
internal static class MaskGeometry
{
    /// <summary>One axis of 'mask-repeat' / 'background-repeat'.</summary>
    public enum AxisRepeat { NoRepeat, Repeat, Space, Round }

    /// <summary>Both axes of a repeat value. 'repeat-x' and 'repeat-y' are the two-axis
    /// shorthands they look like; a single keyword applies to both axes; the two-token form
    /// names the horizontal axis first (CSS Backgrounds 3 §2).</summary>
    public static (AxisRepeat X, AxisRepeat Y) Repeat(string? text)
    {
        var tokens = Tokens(text);
        if (tokens.Count == 0) return (AxisRepeat.Repeat, AxisRepeat.Repeat);
        if (tokens[0] == "repeat-x") return (AxisRepeat.Repeat, AxisRepeat.NoRepeat);
        if (tokens[0] == "repeat-y") return (AxisRepeat.NoRepeat, AxisRepeat.Repeat);
        var first = One(tokens[0]);
        if (tokens.Count == 1) return (first, first);
        return (first, One(tokens[1]));
    }

    private static AxisRepeat One(string token) => token switch
    {
        "no-repeat" => AxisRepeat.NoRepeat,
        "space" => AxisRepeat.Space,
        "round" => AxisRepeat.Round,
        _ => AxisRepeat.Repeat,
    };

    /// <summary>Which box a layer is positioned in ('mask-origin') or clipped to
    /// ('mask-clip'). The SVG boxes ('fill-box', 'stroke-box', 'view-box') name a box an
    /// HTML element does not have, so they resolve to the border box; 'no-clip' exists only
    /// for 'mask-clip' and lets the mask cover the whole canvas.</summary>
    public static SKRect Box(string? boxName, SKRect borderBox, SKRect paddingBox, SKRect contentBox,
        bool isClip)
    {
        switch ((boxName ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "content-box": return contentBox;
            case "padding-box": return paddingBox;
            case "no-clip" when isClip:
                // Unbounded: wide enough that nothing a layer can paint is cut away.
                return new SKRect(-1e6f, -1e6f, 1e6f, 1e6f);
            default: return borderBox;
        }
    }

    /// <summary>Tile size for one layer. A percentage resolves against the axis it belongs
    /// to; 'auto' keeps the source's own size, and with no intrinsic size (a gradient) it
    /// takes the whole positioning area — the default-object-size rule backgrounds use.
    /// 'contain'/'cover' need a ratio, so they only differ from 'auto' when the source has
    /// one; with none they behave like 'auto auto'.</summary>
    public static SKSize TileSize(string? sizeText, SKSize area, SKSize? intrinsic)
    {
        float areaW = Math.Max(0, area.Width), areaH = Math.Max(0, area.Height);
        var tokens = Tokens(sizeText);
        if (tokens.Count == 0) return intrinsic ?? new SKSize(areaW, areaH);

        if (tokens[0] is "contain" or "cover")
        {
            if (intrinsic is not { } src || src.Width <= 0 || src.Height <= 0)
                return new SKSize(areaW, areaH);
            float scale = tokens[0] == "contain"
                ? Math.Min(areaW / src.Width, areaH / src.Height)
                : Math.Max(areaW / src.Width, areaH / src.Height);
            return new SKSize(src.Width * scale, src.Height * scale);
        }

        float? w = Length(tokens[0], areaW);
        float? h = tokens.Count > 1 ? Length(tokens[1], areaH) : null;

        if (w is null && h is null) return intrinsic ?? new SKSize(areaW, areaH);
        if (intrinsic is { } s && s.Width > 0 && s.Height > 0)
        {
            // One axis given, the other 'auto' ⇒ preserve the intrinsic ratio.
            if (w is { } ww && h is null) return new SKSize(ww, ww * s.Height / s.Width);
            if (h is { } hh && w is null) return new SKSize(hh * s.Width / s.Height, hh);
        }
        return new SKSize(w ?? areaW, h ?? areaH);
    }

    /// <summary>Top-left of the tile grid's anchor. A percentage positions the tile so that
    /// the given point of the tile meets the same point of the area, which is why it scales
    /// by (area − tile) and not by the area (CSS Backgrounds 4 §4.1).</summary>
    public static SKPoint Position(string? positionText, SKSize area, SKSize tile)
    {
        var tokens = Tokens(positionText);
        string xToken = "center", yToken = "center";
        if (tokens.Count == 1)
        {
            // A lone 'top'/'bottom' names the vertical axis and leaves the horizontal one at
            // its initial 'center'; any other single token does the opposite.
            if (tokens[0] is "top" or "bottom") yToken = tokens[0];
            else xToken = tokens[0];
        }
        else if (tokens.Count >= 2)
        {
            if (tokens[0] is "top" or "bottom" && tokens[1] is not ("top" or "bottom"))
            { yToken = tokens[0]; xToken = tokens[1]; }
            else { xToken = tokens[0]; yToken = tokens[1]; }
        }
        return new SKPoint(Axis(xToken, area.Width, tile.Width), Axis(yToken, area.Height, tile.Height));
    }

    private static float Axis(string token, float areaExtent, float tileExtent)
    {
        float free = Math.Max(0, areaExtent - tileExtent);
        switch (token)
        {
            case "left" or "top": return 0;
            case "right" or "bottom": return free;
            case "center": return free / 2f;
        }
        if (token.EndsWith('%') && TryNum(token[..^1], out var pct)) return free * pct / 100f;
        if (TryLength(token, out var len)) return len;
        return 0;
    }

    /// <summary>Every rect at which a tile is painted, in coordinates relative to the
    /// positioning area's top-left. The grid is ANCHORED at the computed position and repeats
    /// in both directions, so tiles also cover the band between the area's origin and the
    /// anchor — that is what makes 'position: 50%' with 'repeat' look like a centred strip
    /// rather than a strip pushed right.</summary>
    public static List<SKRect> Tiles(SKSize area, SKSize tile, SKPoint anchor,
        (AxisRepeat X, AxisRepeat Y) repeat)
    {
        var rects = new List<SKRect>();
        if (area.Width <= 0 || area.Height <= 0 || tile.Width <= 0 || tile.Height <= 0)
            return rects;

        var (originX, stepX, countX, extentX) = AxisPlan(area.Width, tile.Width, anchor.X, repeat.X);
        var (originY, stepY, countY, extentY) = AxisPlan(area.Height, tile.Height, anchor.Y, repeat.Y);

        for (int j = 0; j < countY; j++)
            for (int i = 0; i < countX; i++)
            {
                float x = originX + i * stepX, y = originY + j * stepY;
                rects.Add(new SKRect(x, y, x + extentX, y + extentY));
            }
        return rects;
    }

    /// <summary>(first-tile origin, distance between origins, tile count, tile extent) for one
    /// axis. 'space' changes the step and keeps the extent; 'round' changes both.</summary>
    private static (float Origin, float Step, int Count, float Extent) AxisPlan(float area, float tile,
        float anchor, AxisRepeat repeat)
    {
        switch (repeat)
        {
            case AxisRepeat.NoRepeat:
                return (anchor, tile, 1, tile);

            case AxisRepeat.Round:
            {
                // Whole tiles only, scaled so they exactly fill the area.
                int n = Math.Max(1, (int)Math.Round(area / tile, MidpointRounding.AwayFromZero));
                float scaled = area / n;
                return (0, scaled, n, scaled);
            }

            case AxisRepeat.Space:
            {
                if (tile >= area) return (0, tile, 1, tile);
                // Largest whole-tile count that still leaves room for equal gaps.
                int n = Math.Max(1, (int)Math.Floor((area + tile) / (2 * tile)));
                float space = n > 1 ? (area - n * tile) / (n - 1) : 0;
                return (0, tile + space, n, tile);
            }

            default:
            {
                // 'repeat': the grid extends backwards from the anchor as well, so coverage
                // starts at the last origin that is still at or before the area's edge.
                int back = anchor > 0 ? (int)Math.Ceiling(anchor / tile) : 0;
                float origin = anchor - back * tile;
                int count = (int)Math.Ceiling((area - origin) / tile);
                if (count < 1) count = 1;
                return (origin, tile, count, tile);
            }
        }
    }

    /// <summary>A length token in the mask's own coordinate space. Percentages are handled by
    /// the caller (they need the axis extent); here only the numeric part of a unit-bearing
    /// length is taken, and font- or viewport-relative units are read as their number — they
    /// are resolved against metrics this layer does not have (see the mask geometry note in
    /// docs/CSS-HANDOFF.md §4).</summary>
    private static bool TryLength(string token, out float value)
    {
        value = 0;
        for (int i = 0; i < token.Length; i++)
        {
            if (!char.IsAsciiDigit(token[i]) && token[i] != '.' && token[i] != '-' && token[i] != '+')
            {
                var number = token[..i];
                return i > 0 && TryNum(number, out value);
            }
        }
        return LooksNumeric(token) && TryNum(token, out value);
    }

    private static float? Length(string token, float areaExtent)
    {
        if (token == "auto") return null;
        if (token.EndsWith('%') && TryNum(token[..^1], out var pct)) return pct / 100f * areaExtent;
        if (TryLength(token, out var px)) return px;
        return null;
    }

    private static bool LooksNumeric(string token) =>
        token.Length > 0 && (char.IsAsciiDigit(token[0]) || token[0] is '.' or '-' or '+');

    private static bool TryNum(string text, out float value) =>
        float.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private static List<string> Tokens(string? text) =>
        string.IsNullOrEmpty(text)
            ? new List<string>()
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                  .Select(t => t.Trim().ToLowerInvariant()).ToList();
}
