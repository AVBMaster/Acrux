using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>Physical side of a box, in clockwise order.</summary>
public enum BoxSide { Top, Right, Bottom, Left }

/// <summary>
/// Transliteration of BorderEdge: per-side border properties plus the style
/// resolution rules (thin double/ridge/groove degrade to solid).
/// </summary>
public struct BorderEdge
{
    public SKColor Color;
    public bool IsPresent;
    public BorderStyle Style;
    public float Width;

    public BorderEdge(float edgeWidth, SKColor edgeColor, BorderStyle edgeStyle, bool edgeIsPresent = true)
    {
        Color = edgeColor;
        IsPresent = edgeIsPresent;
        Style = EffectiveStyle(edgeStyle, edgeWidth);
        Width = edgeWidth;
    }

    public static BorderStyle EffectiveStyle(BorderStyle style, float width)
    {
        if ((style == BorderStyle.Double && width < 3) ||
            ((style == BorderStyle.Ridge || style == BorderStyle.Groove) && width <= 1))
            return BorderStyle.Solid;
        return style;
    }

    public bool HasVisibleColorAndStyle => Style != BorderStyle.None && Color.Alpha > 0;

    public bool ShouldRender => IsPresent && Width != 0 && HasVisibleColorAndStyle;

    public bool PresentButInvisible => UsedWidth != 0 && !HasVisibleColorAndStyle;

    public float UsedWidth => IsPresent ? Width : 0;

    public BorderStyle BorderStyleValue => Style;

    public float WidthValue => Width;

    public SKColor GetColor => Color;

    public enum DoubleBorderStripe { Outer, Inner }

    public float GetDoubleBorderStripeWidth(DoubleBorderStripe stripe) =>
        stripe == DoubleBorderStripe.Outer ? UsedWidth / 3.0f : UsedWidth * 2.0f / 3.0f;

    public bool SharesColorWith(BorderEdge other) => Color == other.Color;

    public void ClampWidth(float maxWidth)
    {
        if (Width > maxWidth)
        {
            Width = maxWidth;
            Style = EffectiveStyle(Style, Width);
        }
    }
}

/// <summary>
/// Transliteration of BoxBorderPainter: computes border edge geometry and emits
/// per-side strokes/fills into a <see cref="DisplayList"/>, matching the source
/// paint order, miter and opacity-group algorithms.
/// </summary>
public sealed class BoxBorderPainter
{
    private enum MiterType { NoMiter, SoftMiter, HardMiter }

    private const int TopBorderEdge = 1 << (int)BoxSide.Top;
    private const int RightBorderEdge = 1 << (int)BoxSide.Right;
    private const int BottomBorderEdge = 1 << (int)BoxSide.Bottom;
    private const int LeftBorderEdge = 1 << (int)BoxSide.Left;
    private const int AllBorderEdges = TopBorderEdge | BottomBorderEdge | LeftBorderEdge | RightBorderEdge;

    private readonly DisplayList _displayList;
    private readonly SKRect _borderRect;
    private readonly ComputedStyle _style;
    private readonly PhysicalBoxSides _sidesToInclude;

    private readonly BorderEdge[] _edges = new BorderEdge[4];
    private int _visibleEdgeCount;
    private int _firstVisibleEdge;
    private int _visibleEdgeSet;
    private bool _isUniformStyle = true;
    private bool _isUniformWidth = true;
    private bool _isUniformColor = true;
    private bool _isRounded;
    private bool _hasTransparency;

    private FloatRoundedRect _outer;
    private FloatRoundedRect _inner;

    public BoxBorderPainter(DisplayList displayList, SKRect borderRect, ComputedStyle style, PhysicalBoxSides sidesToInclude = PhysicalBoxSides.All)
    {
        _displayList = displayList;
        _borderRect = borderRect;
        _style = style;
        _sidesToInclude = sidesToInclude;

        GetBorderEdgeInfo();
        ComputeBorderProperties();

        if (_visibleEdgeSet == 0)
            return;

        _outer = RoundedBorderGeometry.PixelSnappedRoundedBorder(style, borderRect, sidesToInclude);
        _inner = RoundedBorderGeometry.PixelSnappedRoundedInnerBorder(style, borderRect, sidesToInclude);

        // Make sure the border width isn't larger than the (possibly snapped
        // smaller) border box.
        float maxWidth = _outer.Rect.Width;
        float maxHeight = _outer.Rect.Height;
        _edges[(int)BoxSide.Top].ClampWidth(maxHeight);
        _edges[(int)BoxSide.Right].ClampWidth(maxWidth);
        _edges[(int)BoxSide.Bottom].ClampWidth(maxHeight);
        _edges[(int)BoxSide.Left].ClampWidth(maxWidth);

        _isRounded = _outer.IsRounded;
    }

    // ─── edge setup ──────────────────────────────────────────────────────────

    private void GetBorderEdgeInfo()
    {
        _edges[(int)BoxSide.Top] = new BorderEdge(_style.BorderTopWidth, _style.BorderTopColor, _style.BorderTopStyle, HasSide(PhysicalBoxSides.Top));
        _edges[(int)BoxSide.Right] = new BorderEdge(_style.BorderRightWidth, _style.BorderRightColor, _style.BorderRightStyle, HasSide(PhysicalBoxSides.Right));
        _edges[(int)BoxSide.Bottom] = new BorderEdge(_style.BorderBottomWidth, _style.BorderBottomColor, _style.BorderBottomStyle, HasSide(PhysicalBoxSides.Bottom));
        _edges[(int)BoxSide.Left] = new BorderEdge(_style.BorderLeftWidth, _style.BorderLeftColor, _style.BorderLeftStyle, HasSide(PhysicalBoxSides.Left));
    }

    private bool HasSide(PhysicalBoxSides side) => (_sidesToInclude & side) != 0;

    private void ComputeBorderProperties()
    {
        for (int i = 0; i < 4; i++)
        {
            var edge = _edges[i];

            if (!edge.ShouldRender)
            {
                if (edge.PresentButInvisible)
                {
                    _isUniformWidth = false;
                    _isUniformColor = false;
                }
                continue;
            }

            _visibleEdgeCount++;
            _visibleEdgeSet |= EdgeFlagForSide((BoxSide)i);

            if (edge.Color.Alpha < 255)
                _hasTransparency = true;

            if (_visibleEdgeCount == 1)
            {
                _firstVisibleEdge = i;
                continue;
            }

            _isUniformStyle &= edge.BorderStyleValue == _edges[_firstVisibleEdge].BorderStyleValue;
            _isUniformWidth &= edge.Width == _edges[_firstVisibleEdge].Width;
            _isUniformColor &= edge.SharesColorWith(_edges[_firstVisibleEdge]);
        }
    }

    private ref BorderEdge Edge(BoxSide side) => ref _edges[(int)side];

    private BorderEdge FirstEdge()
    {
        return _edges[_firstVisibleEdge];
    }

    private static int EdgeFlagForSide(BoxSide side) => 1 << (int)side;

    private static bool IncludesEdge(int flags, BoxSide side) => (flags & EdgeFlagForSide(side)) != 0;

    private static bool IncludesAdjacentEdges(int flags)
    {
        // The set includes adjacent edges iff it contains at least one
        // horizontal and one vertical edge.
        return (flags & (TopBorderEdge | BottomBorderEdge)) != 0 && (flags & (LeftBorderEdge | RightBorderEdge)) != 0;
    }

    // ─── style predicates ─────────────────────────────────────────────────────

    private static bool StyleRequiresClipPolygon(BorderStyle style) => style == BorderStyle.Dotted || style == BorderStyle.Dashed;

    private static bool BorderStyleFillsBorderArea(BorderStyle style)
    {
        return !(style == BorderStyle.Dotted || style == BorderStyle.Dashed || style == BorderStyle.Double);
    }

    private static bool BorderStyleHasInnerDetail(BorderStyle style)
    {
        return style == BorderStyle.Groove || style == BorderStyle.Ridge || style == BorderStyle.Double;
    }

    private static bool BorderStyleIsDottedOrDashed(BorderStyle style) => style == BorderStyle.Dotted || style == BorderStyle.Dashed;

    private static bool BorderStyleHasUnmatchedColorsAtCorner(BorderStyle style, BoxSide side, BoxSide adjacentSide)
    {
        // These styles match at the top/left and bottom/right.
        if (style == BorderStyle.Inset || style == BorderStyle.Groove || style == BorderStyle.Ridge || style == BorderStyle.Outset)
        {
            int topRightFlags = EdgeFlagForSide(BoxSide.Top) | EdgeFlagForSide(BoxSide.Right);
            int bottomLeftFlags = EdgeFlagForSide(BoxSide.Bottom) | EdgeFlagForSide(BoxSide.Left);

            int flags = EdgeFlagForSide(side) | EdgeFlagForSide(adjacentSide);
            return flags == topRightFlags || flags == bottomLeftFlags;
        }
        return false;
    }

    private static bool BorderWillArcInnerEdge(SKSize firstRadius, SKSize secondRadius)
    {
        return !firstRadius.IsEmpty || !secondRadius.IsEmpty;
    }

    private static bool WillOverdraw(BoxSide side, BorderStyle style, int completedEdges)
    {
        // If we're done with this side, it will obviously not overdraw any
        // portion of the current edge.
        if (IncludesEdge(completedEdges, side))
            return false;

        // The side is still to be drawn. It overdraws the current edge iff it
        // has a solid fill style.
        return BorderStyleFillsBorderArea(style);
    }

    private static bool BorderStylesRequireMiter(BoxSide side, BoxSide adjacentSide, BorderStyle style, BorderStyle adjacentStyle)
    {
        if (style == BorderStyle.Double || adjacentStyle == BorderStyle.Double ||
            adjacentStyle == BorderStyle.Groove || adjacentStyle == BorderStyle.Ridge)
            return true;

        if (BorderStyleIsDottedOrDashed(style) != BorderStyleIsDottedOrDashed(adjacentStyle))
            return true;

        if (style != adjacentStyle)
            return true;

        return BorderStyleHasUnmatchedColorsAtCorner(style, side, adjacentSide);
    }

    // ─── geometry helpers ─────────────────────────────────────────────────────

    private static SKRect SetToRightSideRect(SKRect rect, float edgeWidth)
    {
        return new SKRect(rect.Right - edgeWidth, rect.Top, rect.Right, rect.Bottom);
    }

    private static SKRect SetToBottomSideRect(SKRect rect, float edgeWidth)
    {
        return new SKRect(rect.Left, rect.Bottom - edgeWidth, rect.Right, rect.Bottom);
    }

    private static SKRect CalculateSideRect(FloatRoundedRect outerBorder, BorderEdge edge, BoxSide side)
    {
        var sideRect = outerBorder.Rect;
        float width = edge.Width;

        switch (side)
        {
            case BoxSide.Top:
                sideRect = new SKRect(sideRect.Left, sideRect.Top, sideRect.Right, sideRect.Top + width);
                break;
            case BoxSide.Bottom:
                sideRect = SetToBottomSideRect(sideRect, width);
                break;
            case BoxSide.Left:
                sideRect = new SKRect(sideRect.Left, sideRect.Top, sideRect.Left + width, sideRect.Bottom);
                break;
            case BoxSide.Right:
                sideRect = SetToRightSideRect(sideRect, width);
                break;
        }
        return sideRect;
    }

    private static FloatRoundedRect CalculateAdjustedInnerBorder(FloatRoundedRect innerBorder, BoxSide side)
    {
        // Expand the inner border as necessary to make it a rounded rect (i.e.
        // radii contained within each edge).  This function relies on the fact
        // we only get radii not contained within each edge if one of the radii
        // for an edge is zero, so we can shift the arc towards the zero radius
        // corner.
        float tl = innerBorder.TopLeftRadius, tr = innerBorder.TopRightRadius;
        float bl = innerBorder.BottomLeftRadius, br = innerBorder.BottomRightRadius;
        var newRect = innerBorder.Rect;

        float overshoot;
        float maxRadii;

        switch (side)
        {
            case BoxSide.Top:
                overshoot = tl + tr - newRect.Width;
                if (overshoot > 0.1f)
                {
                    newRect = new SKRect(newRect.Left, newRect.Top, newRect.Right + overshoot, newRect.Bottom);
                    if (tl == 0)
                        newRect = new SKRect(newRect.Left - overshoot, newRect.Top, newRect.Right, newRect.Bottom);
                }
                bl = 0; br = 0;
                maxRadii = Math.Max(tl, tr);
                if (maxRadii > newRect.Height)
                    newRect = new SKRect(newRect.Left, newRect.Top, newRect.Right, newRect.Top + maxRadii);
                break;
            case BoxSide.Bottom:
                overshoot = bl + br - newRect.Width;
                if (overshoot > 0.1f)
                {
                    newRect = new SKRect(newRect.Left, newRect.Top, newRect.Right + overshoot, newRect.Bottom);
                    if (bl == 0)
                        newRect = new SKRect(newRect.Left - overshoot, newRect.Top, newRect.Right, newRect.Bottom);
                }
                tl = 0; tr = 0;
                maxRadii = Math.Max(bl, br);
                if (maxRadii > newRect.Height)
                    newRect = new SKRect(newRect.Left, newRect.Bottom - maxRadii, newRect.Right, newRect.Bottom);
                break;
            case BoxSide.Left:
                overshoot = tl + bl - newRect.Height;
                if (overshoot > 0.1f)
                {
                    newRect = new SKRect(newRect.Left, newRect.Top, newRect.Right, newRect.Bottom + overshoot);
                    if (tl == 0)
                        newRect = new SKRect(newRect.Left, newRect.Top - overshoot, newRect.Right, newRect.Bottom);
                }
                tr = 0; br = 0;
                maxRadii = Math.Max(tl, bl);
                if (maxRadii > newRect.Width)
                    newRect = new SKRect(newRect.Left, newRect.Top, newRect.Left + maxRadii, newRect.Bottom);
                break;
            case BoxSide.Right:
                overshoot = tr + br - newRect.Height;
                if (overshoot > 0.1f)
                {
                    newRect = new SKRect(newRect.Left, newRect.Top, newRect.Right, newRect.Bottom + overshoot);
                    if (tr == 0)
                        newRect = new SKRect(newRect.Left, newRect.Top - overshoot, newRect.Right, newRect.Bottom);
                }
                tl = 0; bl = 0;
                maxRadii = Math.Max(tr, br);
                if (maxRadii > newRect.Width)
                    newRect = new SKRect(newRect.Right - maxRadii, newRect.Top, newRect.Right, newRect.Bottom);
                break;
        }

        return new FloatRoundedRect
        {
            Rect = newRect,
            TopLeftRadius = tl, TopRightRadius = tr,
            BottomLeftRadius = bl, BottomRightRadius = br
        };
    }

    private static bool IsRenderable(FloatRoundedRect rr)
    {
        if (!rr.IsRounded)
            return true;
        // Each axis is bounded by the box extent in that direction: the horizontal
        // radii by the width and the vertical ones by the height.
        return rr.TopLeftRadius + rr.TopRightRadius <= rr.Rect.Width &&
               rr.BottomLeftRadius + rr.BottomRightRadius <= rr.Rect.Width &&
               rr.TopLeftRadiusY + rr.BottomLeftRadiusY <= rr.Rect.Height &&
               rr.TopRightRadiusY + rr.BottomRightRadiusY <= rr.Rect.Height;
    }

    // ─── op emission ──────────────────────────────────────────────────────────

    private void EmitFillRect(SKRect rect, SKColor color)
    {
        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = rect;
        op.FillColor = color;
        op.Bounds = rect;
        _displayList.Add(op);
    }

    private void EmitFillPath(SKPath path, SKColor color, bool antialias = true)
    {
        var op = PaintOpPool.GetDrawPathOp();
        op.Path.Dispose();
        op.Path = new SKPath(path);
        op.FillPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = antialias };
        op.Bounds = path.Bounds;
        _displayList.Add(op);
    }

    private void EmitStrokePath(SKPath path, float thickness, SKColor color, BorderStyle style)
    {
        var op = PaintOpPool.GetDrawPathOp();
        op.Path.Dispose();
        op.Path = new SKPath(path);
        op.StrokePaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            IsAntialias = true
        };
        if (style == BorderStyle.Dashed)
            op.StrokePaint.PathEffect = SKPathEffect.CreateDash(new[] { thickness * 2, thickness }, 0);
        else if (style == BorderStyle.Dotted)
            op.StrokePaint.PathEffect = SKPathEffect.CreateDash(new[] { thickness, thickness }, 0);
        op.Bounds = path.Bounds;
        _displayList.Add(op);
    }

    private void EmitFillDRRect(FloatRoundedRect outer, FloatRoundedRect inner, SKColor color)
    {
        var path = BuildRoundRectPath(outer, false);
        var innerPath = BuildRoundRectPath(inner, true);
        path.AddPath(innerPath);
        innerPath.Dispose();
        EmitFillPath(path, color);
        path.Dispose();
    }

    private static SKPath BuildRoundRectPath(FloatRoundedRect rr, bool counterClockwise)
    {
        var path = new SKPath();
        path.FillType = SKPathFillType.Winding;
        var direction = counterClockwise ? SKPathDirection.CounterClockwise : SKPathDirection.Clockwise;
        if (rr.IsRounded)
        {
            rr.ConstrainRadii();
            var rrect = new SKRoundRect();
            rrect.SetRectRadii(rr.Rect,
                new[] { rr.CornerRadius(0), rr.CornerRadius(1), rr.CornerRadius(2), rr.CornerRadius(3) });
            path.AddRoundRect(rrect, direction);
        }
        else
        {
            path.AddRect(rr.Rect, direction);
        }
        return path;
    }

    private void EmitPushClipPath(SKPath path, bool antialias)
    {
        var op = PaintOpPool.GetPushClipOp();
        op.ClipPath?.Dispose();
        op.ClipPath = new SKPath(path);
        op.AntiAlias = antialias;
        op.Bounds = path.Bounds;
        _displayList.Add(op);
    }

    private void EmitPushClipOut(FloatRoundedRect inner)
    {
        var path = BuildRoundRectPath(_outer, false);
        var innerPath = BuildRoundRectPath(inner, true);
        path.AddPath(innerPath);
        innerPath.Dispose();
        EmitPushClipPath(path, true);
        path.Dispose();
    }

    private void EmitPopClip()
    {
        var op = PaintOpPool.GetPopClipOp();
        op.Bounds = _borderRect;
        _displayList.Add(op);
    }

    private void EmitPushLayer(float opacity)
    {
        var op = PaintOpPool.GetPushLayerOp();
        op.Opacity = opacity;
        op.Bounds = _borderRect;
        _displayList.Add(op);
    }

    private void EmitPopLayer()
    {
        var op = PaintOpPool.GetPopLayerOp();
        op.Bounds = _borderRect;
        _displayList.Add(op);
    }

    // ─── side drawing ─────────────────────────────────────────────────────────

    private void DrawSolidBorderRect(SKRect borderRect, float borderWidth, SKColor color)
    {
        var strokeRect = borderRect;
        strokeRect = new SKRect(
            strokeRect.Left + borderWidth / 2f, strokeRect.Top + borderWidth / 2f,
            strokeRect.Right - borderWidth / 2f, strokeRect.Bottom - borderWidth / 2f);

        var path = new SKPath();
        path.AddRect(strokeRect);
        EmitStrokePath(path, borderWidth, color, BorderStyle.Solid);
        path.Dispose();
    }

    private static SKColor ColorDark(SKColor color)
    {
        if (color.Red == 255 && color.Green == 255 && color.Blue == 255)
            return new SKColor(0xCC, 0xCC, 0xCC, color.Alpha);

        const float scaleFactor = 255.99998f;
        float r = color.Red / 255f, g = color.Green / 255f, b = color.Blue / 255f;
        float v = Math.Max(r, Math.Max(g, b));
        float multiplier = v == 0 ? 0 : Math.Max(0, (v - 0.33f) / v);
        return new SKColor(
            ClampByte((int)(multiplier * r * scaleFactor)),
            ClampByte((int)(multiplier * g * scaleFactor)),
            ClampByte((int)(multiplier * b * scaleFactor)),
            color.Alpha);
    }

    private static SKColor ColorLight(SKColor color)
    {
        if (color.Red == 0 && color.Green == 0 && color.Blue == 0)
            return new SKColor(0x53, 0x53, 0x53, color.Alpha);

        const float scaleFactor = 255.99998f;
        float r = color.Red / 255f, g = color.Green / 255f, b = color.Blue / 255f;
        float v = Math.Max(r, Math.Max(g, b));
        if (v == 0)
            return new SKColor(0x53, 0x53, 0x53, color.Alpha);
        float multiplier = Math.Min(1f, v + 0.33f) / v;
        return new SKColor(
            ClampByte((int)(multiplier * r * scaleFactor)),
            ClampByte((int)(multiplier * g * scaleFactor)),
            ClampByte((int)(multiplier * b * scaleFactor)),
            color.Alpha);
    }

    private static byte ClampByte(int v) => (byte)Math.Clamp(v, 0, 255);

    private static float RelativeLuminance(SKColor c)
    {
        float Linearize(float channel)
        {
            float s = channel / 255f;
            return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
        }
        float r = Linearize(c.Red), g = Linearize(c.Green), b = Linearize(c.Blue);
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    private static float GetContrastRatio(SKColor a, SKColor b)
    {
        float la = RelativeLuminance(a), lb = RelativeLuminance(b);
        float lighter = Math.Max(la, lb), darker = Math.Min(la, lb);
        return (lighter + 0.05f) / (darker + 0.05f);
    }

    private static SKColor CalculateBorderStyleColor(BorderStyle style, BoxSide side, SKColor color)
    {
        bool isDarken = (side == BoxSide.Top || side == BoxSide.Left) == (style == BorderStyle.Inset);

        var darkColor = ColorDark(color);
        if (isDarken)
            return darkColor;

        // The following condition skips the contrast-ratio test when the result
        // is known to be false (values from a brute-force search of r,g,b).
        if (color.Red >= 150 || color.Green >= 92)
            return color;
        return GetContrastRatio(color, darkColor) < 1.75f ? ColorLight(color) : color;
    }

    private void DrawDashedOrDottedBoxSide(int x1, int y1, int x2, int y2, BoxSide side, SKColor color, int thickness, BorderStyle style)
    {
        if (thickness <= 0)
            return;

        float x, y;
        switch (side)
        {
            case BoxSide.Bottom:
            case BoxSide.Top:
                y = y1 + thickness / 2f;
                EmitStrokeLine(x1, y, x2, y, thickness, color, style);
                break;
            case BoxSide.Right:
            case BoxSide.Left:
                x = x1 + thickness / 2f;
                EmitStrokeLine(x, y1, x, y2, thickness, color, style);
                break;
        }
    }

    private void EmitStrokeLine(float x1, float y1, float x2, float y2, float thickness, SKColor color, BorderStyle style)
    {
        var path = new SKPath();
        path.MoveTo(x1, y1);
        path.LineTo(x2, y2);
        EmitStrokePath(path, thickness, color, style);
        path.Dispose();
    }

    private void DrawDoubleBoxSide(int x1, int y1, int x2, int y2, int length, BoxSide side, SKColor color, int thickness, int adjacentWidth1, int adjacentWidth2)
    {
        int thirdOfThickness = (thickness + 1) / 3;
        if (thirdOfThickness <= 0)
            return;

        if (adjacentWidth1 == 0 && adjacentWidth2 == 0)
        {
            switch (side)
            {
                case BoxSide.Top:
                case BoxSide.Bottom:
                    EmitFillRect(new SKRect(x1, y1, x1 + length, y1 + thirdOfThickness), color);
                    EmitFillRect(new SKRect(x1, y2 - thirdOfThickness, x1 + length, y2), color);
                    break;
                case BoxSide.Left:
                case BoxSide.Right:
                    EmitFillRect(new SKRect(x1, y1, x1 + thirdOfThickness, y1 + length), color);
                    EmitFillRect(new SKRect(x2 - thirdOfThickness, y1, x2, y1 + length), color);
                    break;
            }
            return;
        }

        int adjacent1BigThird = ((adjacentWidth1 > 0) ? adjacentWidth1 + 1 : adjacentWidth1 - 1) / 3;
        int adjacent2BigThird = ((adjacentWidth2 > 0) ? adjacentWidth2 + 1 : adjacentWidth2 - 1) / 3;

        switch (side)
        {
            case BoxSide.Top:
                DrawLineForBoxSide(x1 + Math.Max((-adjacentWidth1 * 2 + 1) / 3, 0), y1,
                    x2 - Math.Max((-adjacentWidth2 * 2 + 1) / 3, 0), y1 + thirdOfThickness,
                    side, color, BorderStyle.Solid, adjacent1BigThird, adjacent2BigThird);
                DrawLineForBoxSide(x1 + Math.Max((adjacentWidth1 * 2 + 1) / 3, 0), y2 - thirdOfThickness,
                    x2 - Math.Max((adjacentWidth2 * 2 + 1) / 3, 0), y2,
                    side, color, BorderStyle.Solid, adjacent1BigThird, adjacent2BigThird);
                break;
            case BoxSide.Left:
                DrawLineForBoxSide(x1, y1 + Math.Max((-adjacentWidth1 * 2 + 1) / 3, 0),
                    x1 + thirdOfThickness, y2 - Math.Max((-adjacentWidth2 * 2 + 1) / 3, 0),
                    side, color, BorderStyle.Solid, adjacent1BigThird, adjacent2BigThird);
                DrawLineForBoxSide(x2 - thirdOfThickness, y1 + Math.Max((adjacentWidth1 * 2 + 1) / 3, 0),
                    x2, y2 - Math.Max((adjacentWidth2 * 2 + 1) / 3, 0),
                    side, color, BorderStyle.Solid, adjacent1BigThird, adjacent2BigThird);
                break;
            case BoxSide.Bottom:
                DrawLineForBoxSide(x1 + Math.Max((adjacentWidth1 * 2 + 1) / 3, 0), y1,
                    x2 - Math.Max((adjacentWidth2 * 2 + 1) / 3, 0), y1 + thirdOfThickness,
                    side, color, BorderStyle.Solid, adjacent1BigThird, adjacent2BigThird);
                DrawLineForBoxSide(x1 + Math.Max((-adjacentWidth1 * 2 + 1) / 3, 0), y2 - thirdOfThickness,
                    x2 - Math.Max((-adjacentWidth2 * 2 + 1) / 3, 0), y2,
                    side, color, BorderStyle.Solid, adjacent1BigThird, adjacent2BigThird);
                break;
            case BoxSide.Right:
                DrawLineForBoxSide(x1, y1 + Math.Max((adjacentWidth1 * 2 + 1) / 3, 0),
                    x1 + thirdOfThickness, y2 - Math.Max((adjacentWidth2 * 2 + 1) / 3, 0),
                    side, color, BorderStyle.Solid, adjacent1BigThird, adjacent2BigThird);
                DrawLineForBoxSide(x2 - thirdOfThickness, y1 + Math.Max((-adjacentWidth1 * 2 + 1) / 3, 0),
                    x2, y2 - Math.Max((-adjacentWidth2 * 2 + 1) / 3, 0),
                    side, color, BorderStyle.Solid, adjacent1BigThird, adjacent2BigThird);
                break;
        }
    }

    private void DrawRidgeOrGrooveBoxSide(int x1, int y1, int x2, int y2, BoxSide side, SKColor color, BorderStyle style, int adjacentWidth1, int adjacentWidth2)
    {
        BorderStyle s1, s2;
        if (style == BorderStyle.Groove)
        {
            s1 = BorderStyle.Inset;
            s2 = BorderStyle.Outset;
        }
        else
        {
            s1 = BorderStyle.Outset;
            s2 = BorderStyle.Inset;
        }

        int adjacent1BigHalf = ((adjacentWidth1 > 0) ? adjacentWidth1 + 1 : adjacentWidth1 - 1) / 2;
        int adjacent2BigHalf = ((adjacentWidth2 > 0) ? adjacentWidth2 + 1 : adjacentWidth2 - 1) / 2;

        switch (side)
        {
            case BoxSide.Top:
                DrawLineForBoxSide(x1 + Math.Max(-adjacentWidth1, 0) / 2, y1,
                    x2 - Math.Max(-adjacentWidth2, 0) / 2, (y1 + y2 + 1) / 2,
                    side, color, s1, adjacent1BigHalf, adjacent2BigHalf);
                DrawLineForBoxSide(x1 + Math.Max(adjacentWidth1 + 1, 0) / 2, (y1 + y2 + 1) / 2,
                    x2 - Math.Max(adjacentWidth2 + 1, 0) / 2, y2,
                    side, color, s2, adjacentWidth1 / 2, adjacentWidth2 / 2);
                break;
            case BoxSide.Left:
                DrawLineForBoxSide(x1, y1 + Math.Max(-adjacentWidth1, 0) / 2,
                    (x1 + x2 + 1) / 2, y2 - Math.Max(-adjacentWidth2, 0) / 2,
                    side, color, s1, adjacent1BigHalf, adjacent2BigHalf);
                DrawLineForBoxSide((x1 + x2 + 1) / 2, y1 + Math.Max(adjacentWidth1 + 1, 0) / 2,
                    x2, y2 - Math.Max(adjacentWidth2 + 1, 0) / 2,
                    side, color, s2, adjacentWidth1 / 2, adjacentWidth2 / 2);
                break;
            case BoxSide.Bottom:
                DrawLineForBoxSide(x1 + Math.Max(adjacentWidth1, 0) / 2, y1,
                    x2 - Math.Max(adjacentWidth2, 0) / 2, (y1 + y2 + 1) / 2,
                    side, color, s2, adjacent1BigHalf, adjacent2BigHalf);
                DrawLineForBoxSide(x1 + Math.Max(-adjacentWidth1 + 1, 0) / 2, (y1 + y2 + 1) / 2,
                    x2 - Math.Max(-adjacentWidth2 + 1, 0) / 2, y2,
                    side, color, s1, adjacentWidth1 / 2, adjacentWidth2 / 2);
                break;
            case BoxSide.Right:
                DrawLineForBoxSide(x1, y1 + Math.Max(adjacentWidth1, 0) / 2,
                    (x1 + x2 + 1) / 2, y2 - Math.Max(adjacentWidth2, 0) / 2,
                    side, color, s2, adjacent1BigHalf, adjacent2BigHalf);
                DrawLineForBoxSide((x1 + x2 + 1) / 2, y1 + Math.Max(-adjacentWidth1 + 1, 0) / 2,
                    x2, y2 - Math.Max(-adjacentWidth2 + 1, 0) / 2,
                    side, color, s1, adjacentWidth1 / 2, adjacentWidth2 / 2);
                break;
        }
    }

    private void FillQuad(SKPoint p1
        , SKPoint p2, SKPoint p3, SKPoint p4, SKColor color, bool antialias)
    {
        var path = new SKPath();
        path.MoveTo(p1);
        path.LineTo(p2);
        path.LineTo(p3);
        path.LineTo(p4);
        path.Close();
        EmitFillPath(path, color, antialias);
        path.Dispose();
    }

    private void DrawSolidBoxSide(int x1, int y1, int x2, int y2, BoxSide side, SKColor color, int adjacentWidth1, int adjacentWidth2)
    {
        if (x2 < x1 || y2 < y1)
            return;

        if (adjacentWidth1 == 0 && adjacentWidth2 == 0)
        {
            EmitFillRect(new SKRect(x1, y1, x2, y2), color);
            return;
        }

        SKPoint p1, p2, p3, p4;
        switch (side)
        {
            case BoxSide.Top:
                p1 = new SKPoint(x1 + Math.Max(-adjacentWidth1, 0), y1);
                p2 = new SKPoint(x1 + Math.Max(adjacentWidth1, 0), y2);
                p3 = new SKPoint(x2 - Math.Max(adjacentWidth2, 0), y2);
                p4 = new SKPoint(x2 - Math.Max(-adjacentWidth2, 0), y1);
                break;
            case BoxSide.Bottom:
                p1 = new SKPoint(x1 + Math.Max(adjacentWidth1, 0), y1);
                p2 = new SKPoint(x1 + Math.Max(-adjacentWidth1, 0), y2);
                p3 = new SKPoint(x2 - Math.Max(-adjacentWidth2, 0), y2);
                p4 = new SKPoint(x2 - Math.Max(adjacentWidth2, 0), y1);
                break;
            case BoxSide.Left:
                p1 = new SKPoint(x1, y1 + Math.Max(-adjacentWidth1, 0));
                p2 = new SKPoint(x1, y2 - Math.Max(-adjacentWidth2, 0));
                p3 = new SKPoint(x2, y2 - Math.Max(adjacentWidth2, 0));
                p4 = new SKPoint(x2, y1 + Math.Max(adjacentWidth1, 0));
                break;
            default: // Right
                p1 = new SKPoint(x1, y1 + Math.Max(adjacentWidth1, 0));
                p2 = new SKPoint(x1, y2 - Math.Max(adjacentWidth2, 0));
                p3 = new SKPoint(x2, y2 - Math.Max(-adjacentWidth2, 0));
                p4 = new SKPoint(x2, y1 + Math.Max(-adjacentWidth1, 0));
                break;
        }

        FillQuad(p1, p2, p3, p4, color, true);
    }

    private void DrawLineForBoxSide(int x1, int y1, int x2, int y2, BoxSide side, SKColor color, BorderStyle style, int adjacentWidth1, int adjacentWidth2)
    {
        int thickness;
        int length;
        if (side == BoxSide.Top || side == BoxSide.Bottom)
        {
            thickness = y2 - y1;
            length = x2 - x1;
        }
        else
        {
            thickness = x2 - x1;
            length = y2 - y1;
        }

        // Recursive calls can produce empty borders; guard against that.
        if (length <= 0 || thickness <= 0)
            return;

        style = BorderEdge.EffectiveStyle(style, thickness);

        switch (style)
        {
            case BorderStyle.None:
                return;
            case BorderStyle.Dotted:
            case BorderStyle.Dashed:
                DrawDashedOrDottedBoxSide(x1, y1, x2, y2, side, color, thickness, style);
                break;
            case BorderStyle.Double:
                DrawDoubleBoxSide(x1, y1, x2, y2, length, side, color, thickness, adjacentWidth1, adjacentWidth2);
                break;
            case BorderStyle.Ridge:
            case BorderStyle.Groove:
                DrawRidgeOrGrooveBoxSide(x1, y1, x2, y2, side, color, style, adjacentWidth1, adjacentWidth2);
                break;
            case BorderStyle.Inset:
            case BorderStyle.Outset:
                color = CalculateBorderStyleColor(style, side, color);
                DrawSolidBoxSide(x1, y1, x2, y2, side, color, adjacentWidth1, adjacentWidth2);
                break;
            default:
                DrawSolidBoxSide(x1, y1, x2, y2, side, color, adjacentWidth1, adjacentWidth2);
                break;
        }
    }

    private static void FindIntersection(SKPoint p1, SKPoint p2, SKPoint d1, SKPoint d2, ref SKPoint intersection)
    {
        float pxLength = p2.X - p1.X;
        float pyLength = p2.Y - p1.Y;

        float dxLength = d2.X - d1.X;
        float dyLength = d2.Y - d1.Y;

        float denom = pxLength * dyLength - pyLength * dxLength;
        if (denom == 0)
            return;

        float param = ((d1.X - p1.X) * dyLength - (d1.Y - p1.Y) * dxLength) / denom;

        intersection = new SKPoint(p1.X + param * pxLength, p1.Y + param * pyLength);
    }

    // ─── opacity grouping ─────────────────────────────────────────────────────

    private sealed class OpacityGroup
    {
        public List<BoxSide> Sides = new();
        public int EdgeFlags;
        public float Alpha;

        public OpacityGroup(float alpha)
        {
            Alpha = alpha;
        }
    }

    private sealed class ComplexBorderInfo
    {
        public List<OpacityGroup> OpacityGroups = new();
        public SKPath? RoundedBorderPath;

        public ComplexBorderInfo(BoxBorderPainter borderPainter)
        {
            var sortedSides = new List<BoxSide>();

            for (int i = borderPainter._firstVisibleEdge; i < 4; i++)
            {
                var side = (BoxSide)i;
                if (IncludesEdge(borderPainter._visibleEdgeSet, side))
                    sortedSides.Add(side);
            }

            sortedSides.Sort((a, b) =>
            {
                var edgeA = borderPainter._edges[(int)a];
                var edgeB = borderPainter._edges[(int)b];

                float alphaA = edgeA.GetColor.Alpha;
                float alphaB = edgeB.GetColor.Alpha;
                if (alphaA != alphaB)
                    return alphaA.CompareTo(alphaB);

                int stylePriorityA = StylePriority(edgeA.BorderStyleValue);
                int stylePriorityB = StylePriority(edgeB.BorderStyleValue);
                if (stylePriorityA != stylePriorityB)
                    return stylePriorityA.CompareTo(stylePriorityB);

                return SidePriority(a).CompareTo(SidePriority(b));
            });

            BuildOpacityGroups(borderPainter, sortedSides);

            if (borderPainter._isRounded)
                RoundedBorderPath = borderPainter._outer.ToPath(true);
        }

        private static int StylePriority(BorderStyle style) => style switch
        {
            BorderStyle.Inset or BorderStyle.Groove or BorderStyle.Outset or BorderStyle.Ridge => 2,
            BorderStyle.Dotted or BorderStyle.Dashed or BorderStyle.Double => 1,
            BorderStyle.Solid => 3,
            _ => 0
        };

        private static int SidePriority(BoxSide side) => side switch
        {
            BoxSide.Top => 0,
            BoxSide.Bottom => 1,
            BoxSide.Right => 2,
            _ => 3
        };

        private void BuildOpacityGroups(BoxBorderPainter borderPainter, List<BoxSide> sortedSides)
        {
            float currentAlpha = 0;
            foreach (var side in sortedSides)
            {
                var edge = borderPainter._edges[(int)side];
                float edgeAlpha = edge.GetColor.Alpha;
                if (edgeAlpha != currentAlpha)
                {
                    OpacityGroups.Add(new OpacityGroup(edgeAlpha));
                    currentAlpha = edgeAlpha;
                }
                var currentGroup = OpacityGroups[OpacityGroups.Count - 1];
                currentGroup.Sides.Add(side);
                currentGroup.EdgeFlags |= EdgeFlagForSide(side);
            }
        }
    }

    // ─── main paint ───────────────────────────────────────────────────────────

    public void Paint()
    {
        if (_visibleEdgeCount == 0 || _outer.Rect.Width <= 0 || _outer.Rect.Height <= 0)
            return;

        if (PaintBorderFastPath())
            return;

        if (PaintRoundedSolidBorders())
            return;

        // Solid, rectangular, possibly-partial borders (single side or several
        // sides with independent colours) are handled directly; the complex
        // mitre path below is reserved for rounded corners and non-solid styles.
        if (PaintPerSideSolidBorders())
            return;

        bool clipToOuterBorder = _outer.IsRounded;
        if (clipToOuterBorder)
        {
            using (var outerClipPath = _outer.ToPath(true))
                EmitPushClipPath(outerClipPath, true);
            if (IsRenderable(_inner) && !_inner.IsEmpty)
                EmitPushClipOut(_inner);
        }

        var borderInfo = new ComplexBorderInfo(this);
        PaintOpacityGroup(borderInfo, 0, 1);

        if (clipToOuterBorder)
        {
            EmitPopClip();
            if (IsRenderable(_inner) && !_inner.IsEmpty)
                EmitPopClip();
        }
    }

    private bool PaintBorderFastPath()
    {
        if (!_isUniformColor || !_isUniformStyle || !IsRenderable(_inner))
            return false;

        var firstStyle = FirstEdge().BorderStyleValue;
        if (firstStyle != BorderStyle.Solid && firstStyle != BorderStyle.Double)
            return false;

        if (_visibleEdgeSet == AllBorderEdges)
        {
            if (firstStyle == BorderStyle.Solid)
            {
                if (_isUniformWidth && !_outer.IsRounded)
                {
                    DrawSolidBorderRect(_outer.Rect, FirstEdge().Width, FirstEdge().GetColor);
                }
                else
                {
                                EmitFillDRRect(_outer, _inner, FirstEdge().GetColor);
                }
            }
            else
            {
                DrawDoubleBorder();
            }
            return true;
        }

        // This is faster than the complex path only if it avoids transparency
        // layers (when the border is translucent).
        if (firstStyle == BorderStyle.Solid && !_outer.IsRounded && _hasTransparency)
        {
            var path = new SKPath();
            path.FillType = SKPathFillType.Winding;
            foreach (BoxSide side in new[] { BoxSide.Top, BoxSide.Right, BoxSide.Bottom, BoxSide.Left })
            {
                var currEdge = Edge(side);
                if (currEdge.ShouldRender)
                    path.AddRect(CalculateSideRect(_outer, currEdge, side));
            }
            EmitFillPath(path, FirstEdge().GetColor);
            path.Dispose();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Fast path for rectangular borders whose visible edges are all 'solid' but
    /// may differ in colour or width (e.g. a lone 'border-bottom', or
    /// 'border-left' + 'border-top'). Each visible side is filled with its own
    /// colour. The general complex path is only needed for mitred joins between
    /// adjacent edges of differing style, or for rounded corners; without this
    /// path a single-side opaque border fell through to the complex path and
    /// painted incorrectly (a faint hairline instead of the requested colour).
    /// </summary>
    private bool PaintPerSideSolidBorders()
    {
        if (_outer.IsRounded)
            return false;

        // Every visible edge must be solid; other styles (dashed/dotted/double/
        // groove/…) need their dedicated drawing.
        for (int i = 0; i < 4; i++)
        {
            ref BorderEdge edge = ref _edges[i];
            if (!edge.ShouldRender)
                continue;
            if (edge.BorderStyleValue != BorderStyle.Solid)
                return false;
        }

        foreach (BoxSide side in new[] { BoxSide.Top, BoxSide.Right, BoxSide.Bottom, BoxSide.Left })
        {
            var edge = Edge(side);
            if (!edge.ShouldRender)
                continue;
            var rect = CalculateSideRect(_outer, edge, side);
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;
            using var path = new SKPath();
            path.AddRect(rect);
            EmitFillPath(path, edge.GetColor);
        }
        return true;
    }

    /// <summary>
    /// Paint a rounded border whose four solid edges carry different colours.
    /// Each side owns the wedge of the ring cut out by the 45 degree mitre rays that
    /// leave the border-box corners, which is what a browser seams adjacent coloured
    /// arcs with. Filling the ring per side keeps the arcs covered, which the
    /// axis-aligned side rectangles of the general path cannot do.
    /// </summary>
    private bool PaintRoundedSolidBorders()
    {
        if (!_outer.IsRounded || _visibleEdgeSet != AllBorderEdges)
            return false;

        for (int i = 0; i < 4; i++)
        {
            ref BorderEdge edge = ref _edges[i];
            if (!edge.ShouldRender || edge.BorderStyleValue != BorderStyle.Solid)
                return false;
        }

        using var ring = BuildRoundRectPath(_outer, false);
        if (IsRenderable(_inner) && !_inner.IsEmpty)
            ring.AddPath(BuildRoundRectPath(_inner, true));

        foreach (BoxSide side in new[] { BoxSide.Top, BoxSide.Right, BoxSide.Bottom, BoxSide.Left })
        {
            using var region = BuildSideMiterPath(side);
            EmitPushClipPath(ring, true);
            EmitPushClipPath(region, false);
            EmitFillRect(_outer.Rect, Edge(side).GetColor);
            EmitPopClip();
            EmitPopClip();
        }
        return true;
    }

    /// <summary>The 45 degree miter wedge of <paramref name="side"/> over the border box.</summary>
    private SKPath BuildSideMiterPath(BoxSide side)
    {
        var rect = _outer.Rect;
        float l = rect.Left, t = rect.Top, r = rect.Right, b = rect.Bottom;
        float ext = Math.Max(rect.Width, rect.Height) + 1f;
        var polygon = new List<SKPoint>
        {
            new(l - ext, t - ext), new(r + ext, t - ext), new(r + ext, b + ext), new(l - ext, b + ext)
        };

        // A side is bounded by the two rays through its corners: keep the half-plane
        // that faces that side (see CSS 2.1 8.5.3 mitre joins).
        switch (side)
        {
            case BoxSide.Top:
                ClipHalfPlane(polygon, 1, -1, l - t, keepGreater: true);
                ClipHalfPlane(polygon, 1, 1, r + t, keepGreater: false);
                break;
            case BoxSide.Right:
                ClipHalfPlane(polygon, 1, 1, r + t, keepGreater: true);
                ClipHalfPlane(polygon, 1, -1, r - b, keepGreater: true);
                break;
            case BoxSide.Bottom:
                ClipHalfPlane(polygon, 1, -1, r - b, keepGreater: false);
                ClipHalfPlane(polygon, 1, 1, l + b, keepGreater: true);
                break;
            default:
                ClipHalfPlane(polygon, 1, 1, l + b, keepGreater: false);
                ClipHalfPlane(polygon, 1, -1, l - t, keepGreater: false);
                break;
        }

        var path = new SKPath();
        if (polygon.Count > 2)
        {
            path.MoveTo(polygon[0]);
            for (int i = 1; i < polygon.Count; i++)
                path.LineTo(polygon[i]);
            path.Close();
        }
        return path;
    }

    private static void ClipHalfPlane(List<SKPoint> polygon, float a, float b, float c, bool keepGreater)
    {
        if (polygon.Count == 0)
            return;

        float Signed(SKPoint p) => a * p.X + b * p.Y - c;
        bool Inside(SKPoint p) => keepGreater ? Signed(p) >= 0 : Signed(p) <= 0;

        var clipped = new List<SKPoint>(polygon.Count + 2);
        for (int i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            bool currentInside = Inside(current);
            bool nextInside = Inside(next);
            if (currentInside)
                clipped.Add(current);
            if (currentInside != nextInside)
            {
                float fc = Signed(current), fn = Signed(next);
                float scale = fc / (fc - fn);
                clipped.Add(new SKPoint(current.X + (next.X - current.X) * scale,
                                        current.Y + (next.Y - current.Y) * scale));
            }
        }
        polygon.Clear();
        polygon.AddRange(clipped);
    }

    private void DrawDoubleBorder()    {
        var color = FirstEdge().GetColor;
        bool forceRectangular = !_outer.IsRounded && !_inner.IsRounded;

        // outer stripe
        var outerThirdOutsets = DoubleStripeOutsets(BorderEdge.DoubleBorderStripe.Outer);
        var outerThirdRect = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(_style, _borderRect, outerThirdOutsets, _sidesToInclude);
        if (forceRectangular)
            ClearRadii(ref outerThirdRect);
        EmitFillDRRect(_outer, outerThirdRect, color);

        // inner stripe
        var innerThirdOutsets = DoubleStripeOutsets(BorderEdge.DoubleBorderStripe.Inner);
        var innerThirdRect = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(_style, _borderRect, innerThirdOutsets, _sidesToInclude);
        if (forceRectangular)
            ClearRadii(ref innerThirdRect);
        EmitFillDRRect(innerThirdRect, _inner, color);
    }

    private static void ClearRadii(ref FloatRoundedRect rr)
    {
        rr.TopLeftRadius = rr.TopRightRadius = rr.BottomLeftRadius = rr.BottomRightRadius = 0;
    }

    private PhysicalBoxStrut DoubleStripeOutsets(BorderEdge.DoubleBorderStripe stripe)
    {
        return new PhysicalBoxStrut(
            Edge(BoxSide.Top).GetDoubleBorderStripeWidth(stripe),
            Edge(BoxSide.Right).GetDoubleBorderStripeWidth(stripe),
            Edge(BoxSide.Bottom).GetDoubleBorderStripeWidth(stripe),
            Edge(BoxSide.Left).GetDoubleBorderStripeWidth(stripe));
    }

    private PhysicalBoxStrut CenterOutsets()
    {
        return new PhysicalBoxStrut(
            (int)(Edge(BoxSide.Top).UsedWidth * 0.5),
            (int)(Edge(BoxSide.Right).UsedWidth * 0.5),
            (int)(Edge(BoxSide.Bottom).UsedWidth * 0.5),
            (int)(Edge(BoxSide.Left).UsedWidth * 0.5));
    }

    private int PaintOpacityGroup(ComplexBorderInfo borderInfo, int index, float effectiveOpacity)
    {
        var opacityGroups = borderInfo.OpacityGroups;

        // For overdraw logic purposes, treat missing/transparent edges as completed.
        if (index >= opacityGroups.Count)
            return ~_visibleEdgeSet;

        // Groups are sorted in increasing opacity order, but layers are created
        // in decreasing opacity order - hence the reverse iteration.
        var group = opacityGroups[opacityGroups.Count - index - 1];

        // Adjust this group's paint opacity to account for ancestor layers.
        float paintAlpha = group.Alpha / effectiveOpacity;

        // For the last (bottom) group, skip the layer iff it contains no
        // adjacent edges (no in-group overdraw possibility).
        bool needsLayer = group.Alpha != 1.0f &&
            (IncludesAdjacentEdges(group.EdgeFlags) || (index + 1 < opacityGroups.Count));

        if (needsLayer)
        {
            EmitPushLayer(group.Alpha / effectiveOpacity);
            effectiveOpacity = group.Alpha;
            paintAlpha = 1.0f;
        }

        int completedEdges = PaintOpacityGroup(borderInfo, index + 1, effectiveOpacity);

        foreach (var side in group.Sides)
        {
            PaintSide(borderInfo, side, paintAlpha, completedEdges);
            completedEdges |= EdgeFlagForSide(side);
        }

        if (needsLayer)
            EmitPopLayer();

        return completedEdges;
    }

    private void PaintSide(ComplexBorderInfo borderInfo, BoxSide side, float alpha, int completedEdges)
    {
        var edge = Edge(side);
        var color = edge.GetColor.WithAlpha((byte)(alpha * 255));

        var sideRect = _outer.Rect;
        SKPath? path = null;

        switch (side)
        {
            case BoxSide.Top:
            {
                bool usePath = _isRounded &&
                    (BorderStyleHasInnerDetail(edge.BorderStyleValue) ||
                     BorderWillArcInnerEdge(CornerRadius(_inner, 0), CornerRadius(_inner, 1)));
                if (usePath)
                    path = borderInfo.RoundedBorderPath;
                else
                    sideRect = new SKRect(sideRect.Left, sideRect.Top, sideRect.Right, sideRect.Top + edge.Width);
                PaintOneBorderSide(sideRect, BoxSide.Top, BoxSide.Left, BoxSide.Right, path, color, completedEdges);
                break;
            }
            case BoxSide.Bottom:
            {
                bool usePath = _isRounded &&
                    (BorderStyleHasInnerDetail(edge.BorderStyleValue) ||
                     BorderWillArcInnerEdge(CornerRadius(_inner, 2), CornerRadius(_inner, 3)));
                if (usePath)
                    path = borderInfo.RoundedBorderPath;
                else
                    sideRect = SetToBottomSideRect(sideRect, edge.Width);
                PaintOneBorderSide(sideRect, BoxSide.Bottom, BoxSide.Left, BoxSide.Right, path, color, completedEdges);
                break;
            }
            case BoxSide.Left:
            {
                bool usePath = _isRounded &&
                    (BorderStyleHasInnerDetail(edge.BorderStyleValue) ||
                     BorderWillArcInnerEdge(CornerRadius(_inner, 3), CornerRadius(_inner, 0)));
                if (usePath)
                    path = borderInfo.RoundedBorderPath;
                else
                    sideRect = new SKRect(sideRect.Left, sideRect.Top, sideRect.Left + edge.Width, sideRect.Bottom);
                PaintOneBorderSide(sideRect, BoxSide.Left, BoxSide.Top, BoxSide.Bottom, path, color, completedEdges);
                break;
            }
            case BoxSide.Right:
            {
                bool usePath = _isRounded &&
                    (BorderStyleHasInnerDetail(edge.BorderStyleValue) ||
                     BorderWillArcInnerEdge(CornerRadius(_inner, 2), CornerRadius(_inner, 1)));
                if (usePath)
                    path = borderInfo.RoundedBorderPath;
                else
                    sideRect = SetToRightSideRect(sideRect, edge.Width);
                PaintOneBorderSide(sideRect, BoxSide.Right, BoxSide.Top, BoxSide.Bottom, path, color, completedEdges);
                break;
            }
        }
    }

    /// <summary>Corner radius as an SKSize for one corner of a rounded rect.</summary>
    private static SKSize CornerRadius(FloatRoundedRect rr, int corner)
    {
        var radius = rr.CornerRadius(corner);
        return new SKSize(radius.X, radius.Y);
    }

    private MiterType ComputeMiter(BoxSide side, BoxSide adjacentSide, int completedEdges)
    {
        var adjacentEdge = Edge(adjacentSide);

        // No miters for missing edges.
        if (adjacentEdge.UsedWidth == 0)
            return MiterType.NoMiter;

        // The adjacent edge will overdraw this corner, resulting in a correct miter.
        if (WillOverdraw(adjacentSide, adjacentEdge.BorderStyleValue, completedEdges))
            return MiterType.NoMiter;

        // Color transitions require miters.
        if (!ColorsMatchAtCorner(side, adjacentSide))
            return MiterType.SoftMiter;

        // Non-anti-aliased miters ensure correct same-color seaming when
        // required by style.
        if (BorderStylesRequireMiter(side, adjacentSide, Edge(side).BorderStyleValue, adjacentEdge.BorderStyleValue))
            return MiterType.HardMiter;

        // Overdraw the adjacent edge when the colors match and we have no style
        // restrictions.
        return MiterType.NoMiter;
    }

    private static bool MitersRequireClipping(MiterType miter1, MiterType miter2, BorderStyle style)
    {
        bool shouldClip = miter1 == MiterType.HardMiter || miter2 == MiterType.HardMiter;
        shouldClip = shouldClip || ((miter1 != MiterType.NoMiter || miter2 != MiterType.NoMiter) && StyleRequiresClipPolygon(style));
        return shouldClip;
    }

    private void PaintOneBorderSide(SKRect sideRect, BoxSide side, BoxSide adjacentSide1, BoxSide adjacentSide2, SKPath? path, SKColor color, int completedEdges)
    {
        var edgeToRender = Edge(side);
        var adjacentEdge1 = Edge(adjacentSide1);
        var adjacentEdge2 = Edge(adjacentSide2);

        if (path != null)
        {
            MiterType miter1 = ColorsMatchAtCorner(side, adjacentSide1) ? MiterType.HardMiter : MiterType.SoftMiter;
            MiterType miter2 = ColorsMatchAtCorner(side, adjacentSide2) ? MiterType.HardMiter : MiterType.SoftMiter;

            bool pushedPolygon = ClipBorderSidePolygon(side, miter1, miter2);
            bool pushedOut = false;
            if (!IsRenderable(_inner))
            {
                var adjustedInnerRect = CalculateAdjustedInnerBorder(_inner, side);
                if (!adjustedInnerRect.IsEmpty)
                {
                    EmitPushClipOut(adjustedInnerRect);
                    pushedOut = true;
                }
            }

            // The path-based sides still work on an integer grid, so a fractional
            // (device-quantised) width is rounded rather than truncated.
            int edgeThickness = (int)MathF.Round(edgeToRender.Width);
            int strokeThickness = (int)MathF.Round(Math.Max(Math.Max(edgeToRender.Width, adjacentEdge1.Width), adjacentEdge2.Width));
            DrawBoxSideFromPath(path, edgeThickness, strokeThickness, side, color, edgeToRender.BorderStyleValue);

            if (pushedOut) EmitPopClip();
            if (pushedPolygon) EmitPopClip();
        }
        else
        {
            MiterType miter1 = ComputeMiter(side, adjacentSide1, completedEdges);
            MiterType miter2 = ComputeMiter(side, adjacentSide2, completedEdges);
            bool shouldClip = MitersRequireClipping(miter1, miter2, edgeToRender.BorderStyleValue);

            bool pushedPolygon = false;
            if (shouldClip)
            {
                pushedPolygon = ClipBorderSidePolygon(side, miter1, miter2);
                miter1 = miter2 = MiterType.NoMiter;
            }

            DrawLineForBoxSide((int)sideRect.Left, (int)sideRect.Top, (int)sideRect.Right, (int)sideRect.Bottom, side, color,
                edgeToRender.BorderStyleValue, miter1 != MiterType.NoMiter ? (int)MathF.Round(adjacentEdge1.Width) : 0,
                miter2 != MiterType.NoMiter ? (int)MathF.Round(adjacentEdge2.Width) : 0);

            if (pushedPolygon) EmitPopClip();
        }
    }

    private void DrawBoxSideFromPath(SKPath borderPath, int borderThickness, int strokeThickness, BoxSide side, SKColor color, BorderStyle borderStyle)
    {
        if (borderThickness <= 0)
            return;

        switch (borderStyle)
        {
            case BorderStyle.None:
                return;
            case BorderStyle.Dotted:
            case BorderStyle.Dashed:
                DrawDashedDottedBoxSideFromPath(borderThickness, strokeThickness, color, borderStyle);
                return;
            case BorderStyle.Double:
                DrawDoubleBoxSideFromPath(borderPath, borderThickness, strokeThickness, side, color);
                return;
            case BorderStyle.Ridge:
            case BorderStyle.Groove:
                DrawRidgeGrooveBoxSideFromPath(borderPath, borderThickness, strokeThickness, side, color, borderStyle);
                return;
            case BorderStyle.Inset:
            case BorderStyle.Outset:
                color = CalculateBorderStyleColor(borderStyle, side, color);
                break;
        }

        EmitFillRect(_outer.Rect, color);
    }

    private void DrawDashedDottedBoxSideFromPath(int borderThickness, int strokeThickness, SKColor color, BorderStyle borderStyle)
    {
        // Convert the path to be down the middle of the dots or dashes.
        var centerlinePath = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(_style, _borderRect, CenterOutsets(), _sidesToInclude).ToPath(true);

        // The stroke is doubled here because the provided path is the outside
        // edge of the border so half the stroke is clipped off, with the extra
        // multiplier so the clipping mask can antialias the edges.
        float thicknessMultiplier = 2 * 1.1f;
        EmitStrokePath(centerlinePath, strokeThickness * thicknessMultiplier, color, borderStyle);
        centerlinePath.Dispose();
    }

    private void DrawDoubleBoxSideFromPath(SKPath borderPath, int borderThickness, int strokeThickness, BoxSide side, SKColor color)
    {
        // Draw inner border line
        var innerOutsets = DoubleStripeOutsets(BorderEdge.DoubleBorderStripe.Inner);
        var innerClip = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(_style, _borderRect, innerOutsets, _sidesToInclude);
        using (var innerClipPath = innerClip.ToPath(true))
            EmitPushClipPath(innerClipPath, true);
        DrawBoxSideFromPath(borderPath, borderThickness, strokeThickness, side, color, BorderStyle.Solid);
        EmitPopClip();

        // Draw outer border line
        var outerOutsets = DoubleStripeOutsets(BorderEdge.DoubleBorderStripe.Outer);
        var outerClip = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(_style, _borderRect, outerOutsets, _sidesToInclude);
        EmitPushClipOut(outerClip);
        DrawBoxSideFromPath(borderPath, borderThickness, strokeThickness, side, color, BorderStyle.Solid);
        EmitPopClip();
    }

    private void DrawRidgeGrooveBoxSideFromPath(SKPath borderPath, int borderThickness, int strokeThickness, BoxSide side, SKColor color, BorderStyle borderStyle)
    {
        BorderStyle s1, s2;
        if (borderStyle == BorderStyle.Groove)
        {
            s1 = BorderStyle.Inset;
            s2 = BorderStyle.Outset;
        }
        else
        {
            s1 = BorderStyle.Outset;
            s2 = BorderStyle.Inset;
        }

        // Paint full border
        DrawBoxSideFromPath(borderPath, borderThickness, strokeThickness, side, color, s1);

        // Paint inner only
        var clipRect = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(_style, _borderRect, CenterOutsets(), _sidesToInclude);
        using (var clipRectPath = clipRect.ToPath(true))
            EmitPushClipPath(clipRectPath, true);
        DrawBoxSideFromPath(borderPath, borderThickness, strokeThickness, side, color, s2);
        EmitPopClip();
    }

    // ─── clip polygon ─────────────────────────────────────────────────────────

    private bool ClipBorderSidePolygon(BoxSide side, MiterType firstMiter, MiterType secondMiter)
    {
        if (firstMiter == MiterType.NoMiter && secondMiter == MiterType.NoMiter)
            return false;

        // The boundary of the edge for fill.
        var edgeQuad = new SKPoint[4];
        var edgePentagon = new SKPoint[5];

        SKPoint boundQuad1, boundQuad2;

        var innerPoints = new[]
        {
            new SKPoint(_inner.Rect.Left, _inner.Rect.Top),
            new SKPoint(_inner.Rect.Right, _inner.Rect.Top),
            new SKPoint(_inner.Rect.Right, _inner.Rect.Bottom),
            new SKPoint(_inner.Rect.Left, _inner.Rect.Bottom)
        };
        var outerPoints = new[]
        {
            new SKPoint(_outer.Rect.Left, _outer.Rect.Top),
            new SKPoint(_outer.Rect.Right, _outer.Rect.Top),
            new SKPoint(_outer.Rect.Right, _outer.Rect.Bottom),
            new SKPoint(_outer.Rect.Left, _outer.Rect.Bottom)
        };

        // Offset size and direction to expand clipping quad.
        const float kExtensionLength = 1e-1f;
        SKPoint extensionOffset;
        bool hasPentagon = false;

        switch (side)
        {
            case BoxSide.Top:
                edgeQuad[0] = outerPoints[0]; edgeQuad[1] = innerPoints[0];
                edgeQuad[2] = innerPoints[1]; edgeQuad[3] = outerPoints[1];

                boundQuad1 = new SKPoint(edgeQuad[0].X, edgeQuad[1].Y);
                boundQuad2 = new SKPoint(edgeQuad[3].X, edgeQuad[2].Y);

                extensionOffset = new SKPoint(-kExtensionLength, 0);

                if (_inner.TopLeftRadius > 0)
                {
                    var r = new SKSize(_inner.TopLeftRadius, _inner.TopLeftRadius);
                    FindIntersection(edgeQuad[0], edgeQuad[1],
                        new SKPoint(edgeQuad[1].X + r.Width, edgeQuad[1].Y),
                        new SKPoint(edgeQuad[1].X, edgeQuad[1].Y + r.Height), ref edgeQuad[1]);
                    boundQuad1.Y = edgeQuad[1].Y;
                    boundQuad2.Y = edgeQuad[1].Y;

                    if (edgeQuad[1].Y > innerPoints[2].Y)
                        FindIntersection(edgeQuad[0], edgeQuad[1], innerPoints[3], innerPoints[2], ref edgeQuad[1]);
                    if (edgeQuad[1].X > innerPoints[2].X)
                        FindIntersection(edgeQuad[0], edgeQuad[1], innerPoints[1], innerPoints[2], ref edgeQuad[1]);
                    if (edgeQuad[2].Y < edgeQuad[1].Y && edgeQuad[2].X > edgeQuad[1].X)
                    {
                        edgePentagon = new[] { edgeQuad[0], edgeQuad[1], new SKPoint(edgeQuad[2].X, edgeQuad[1].Y), edgeQuad[2], edgeQuad[3] };
                        hasPentagon = true;
                    }
                }

                if (_inner.TopRightRadius > 0)
                {
                    var r = new SKSize(_inner.TopRightRadius, _inner.TopRightRadius);
                    FindIntersection(edgeQuad[3], edgeQuad[2],
                        new SKPoint(edgeQuad[2].X - r.Width, edgeQuad[2].Y),
                        new SKPoint(edgeQuad[2].X, edgeQuad[2].Y + r.Height), ref edgeQuad[2]);
                    if (boundQuad1.Y < edgeQuad[2].Y)
                    {
                        boundQuad1.Y = edgeQuad[2].Y;
                        boundQuad2.Y = edgeQuad[2].Y;
                    }

                    if (edgeQuad[2].Y > innerPoints[3].Y)
                        FindIntersection(edgeQuad[3], edgeQuad[2], innerPoints[3], innerPoints[2], ref edgeQuad[2]);
                    if (edgeQuad[2].X < innerPoints[3].X)
                        FindIntersection(edgeQuad[3], edgeQuad[2], innerPoints[0], innerPoints[3], ref edgeQuad[2]);
                    if (edgeQuad[2].Y > edgeQuad[1].Y && edgeQuad[2].X > edgeQuad[1].X)
                    {
                        edgePentagon = new[] { edgeQuad[0], edgeQuad[1], new SKPoint(edgeQuad[1].X, edgeQuad[2].Y), edgeQuad[2], edgeQuad[3] };
                        hasPentagon = true;
                    }
                }
                break;

            case BoxSide.Left:
                (firstMiter, secondMiter) = (secondMiter, firstMiter);
                edgeQuad[0] = outerPoints[3]; edgeQuad[1] = innerPoints[3];
                edgeQuad[2] = innerPoints[0]; edgeQuad[3] = outerPoints[0];

                boundQuad1 = new SKPoint(edgeQuad[1].X, edgeQuad[0].Y);
                boundQuad2 = new SKPoint(edgeQuad[2].X, edgeQuad[3].Y);

                extensionOffset = new SKPoint(0, kExtensionLength);

                if (_inner.TopLeftRadius > 0)
                {
                    var r = new SKSize(_inner.TopLeftRadius, _inner.TopLeftRadius);
                    FindIntersection(edgeQuad[3], edgeQuad[2],
                        new SKPoint(edgeQuad[2].X + r.Width, edgeQuad[2].Y),
                        new SKPoint(edgeQuad[2].X, edgeQuad[2].Y + r.Height), ref edgeQuad[2]);
                    boundQuad1.X = edgeQuad[2].X;
                    boundQuad2.X = edgeQuad[2].X;

                    if (edgeQuad[2].Y > innerPoints[2].Y)
                        FindIntersection(edgeQuad[3], edgeQuad[2], innerPoints[3], innerPoints[2], ref edgeQuad[2]);
                    if (edgeQuad[2].X > innerPoints[2].X)
                        FindIntersection(edgeQuad[3], edgeQuad[2], innerPoints[1], innerPoints[2], ref edgeQuad[2]);
                    if (edgeQuad[2].Y < edgeQuad[1].Y && edgeQuad[2].X > edgeQuad[1].X)
                    {
                        edgePentagon = new[] { edgeQuad[0], edgeQuad[1], new SKPoint(edgeQuad[2].X, edgeQuad[1].Y), edgeQuad[2], edgeQuad[3] };
                        hasPentagon = true;
                    }
                }

                if (_inner.BottomLeftRadius > 0)
                {
                    var r = new SKSize(_inner.BottomLeftRadius, _inner.BottomLeftRadius);
                    FindIntersection(edgeQuad[0], edgeQuad[1],
                        new SKPoint(edgeQuad[1].X + r.Width, edgeQuad[1].Y),
                        new SKPoint(edgeQuad[1].X, edgeQuad[1].Y - r.Height), ref edgeQuad[1]);
                    if (boundQuad1.X < edgeQuad[1].X)
                    {
                        boundQuad1.X = edgeQuad[1].X;
                        boundQuad2.X = edgeQuad[1].X;
                    }

                    if (edgeQuad[1].Y < innerPoints[1].Y)
                        FindIntersection(edgeQuad[0], edgeQuad[1], innerPoints[0], innerPoints[1], ref edgeQuad[1]);
                    if (edgeQuad[1].X > innerPoints[1].X)
                        FindIntersection(edgeQuad[0], edgeQuad[1], innerPoints[1], innerPoints[2], ref edgeQuad[1]);
                    if (edgeQuad[2].Y < edgeQuad[1].Y && edgeQuad[2].X < edgeQuad[1].X)
                    {
                        edgePentagon = new[] { edgeQuad[0], edgeQuad[1], new SKPoint(edgeQuad[1].X, edgeQuad[2].Y), edgeQuad[2], edgeQuad[3] };
                        hasPentagon = true;
                    }
                }
                break;

            case BoxSide.Bottom:
                (firstMiter, secondMiter) = (secondMiter, firstMiter);
                edgeQuad[0] = outerPoints[2]; edgeQuad[1] = innerPoints[2];
                edgeQuad[2] = innerPoints[3]; edgeQuad[3] = outerPoints[3];

                boundQuad1 = new SKPoint(edgeQuad[0].X, edgeQuad[1].Y);
                boundQuad2 = new SKPoint(edgeQuad[3].X, edgeQuad[2].Y);

                extensionOffset = new SKPoint(kExtensionLength, 0);

                if (_inner.BottomLeftRadius > 0)
                {
                    var r = new SKSize(_inner.BottomLeftRadius, _inner.BottomLeftRadius);
                    FindIntersection(edgeQuad[3], edgeQuad[2],
                        new SKPoint(edgeQuad[2].X + r.Width, edgeQuad[2].Y),
                        new SKPoint(edgeQuad[2].X, edgeQuad[2].Y - r.Height), ref edgeQuad[2]);
                    boundQuad1.Y = edgeQuad[2].Y;
                    boundQuad2.Y = edgeQuad[2].Y;

                    if (edgeQuad[2].Y < innerPoints[1].Y)
                        FindIntersection(edgeQuad[3], edgeQuad[2], innerPoints[0], innerPoints[1], ref edgeQuad[2]);
                    if (edgeQuad[2].X > innerPoints[1].X)
                        FindIntersection(edgeQuad[3], edgeQuad[2], innerPoints[1], innerPoints[2], ref edgeQuad[2]);
                    if (edgeQuad[2].Y < edgeQuad[1].Y && edgeQuad[2].X < edgeQuad[1].X)
                    {
                        edgePentagon = new[] { edgeQuad[0], edgeQuad[1], new SKPoint(edgeQuad[1].X, edgeQuad[2].Y), edgeQuad[2], edgeQuad[3] };
                        hasPentagon = true;
                    }
                }

                if (_inner.BottomRightRadius > 0)
                {
                    var r = new SKSize(_inner.BottomRightRadius, _inner.BottomRightRadius);
                    FindIntersection(edgeQuad[0], edgeQuad[1],
                        new SKPoint(edgeQuad[1].X - r.Width, edgeQuad[1].Y),
                        new SKPoint(edgeQuad[1].X, edgeQuad[1].Y - r.Height), ref edgeQuad[1]);
                    if (boundQuad1.Y > edgeQuad[1].Y)
                    {
                        boundQuad1.Y = edgeQuad[1].Y;
                        boundQuad2.Y = edgeQuad[1].Y;
                    }

                    if (edgeQuad[1].Y < innerPoints[0].Y)
                        FindIntersection(edgeQuad[0], edgeQuad[1], innerPoints[0], innerPoints[1], ref edgeQuad[1]);
                    if (edgeQuad[1].X < innerPoints[0].X)
                        FindIntersection(edgeQuad[0], edgeQuad[1], innerPoints[0], innerPoints[3], ref edgeQuad[1]);
                    if (edgeQuad[2].X < edgeQuad[1].X && edgeQuad[2].Y > edgeQuad[1].Y)
                    {
                        edgePentagon = new[] { edgeQuad[0], edgeQuad[1], new SKPoint(edgeQuad[2].X, edgeQuad[1].Y), edgeQuad[2], edgeQuad[3] };
                        hasPentagon = true;
                    }
                }
                break;

            default: // Right
                edgeQuad[0] = outerPoints[1]; edgeQuad[1] = innerPoints[1];
                edgeQuad[2] = innerPoints[2]; edgeQuad[3] = outerPoints[2];

                boundQuad1 = new SKPoint(edgeQuad[1].X, edgeQuad[0].Y);
                boundQuad2 = new SKPoint(edgeQuad[2].X, edgeQuad[3].Y);

                extensionOffset = new SKPoint(0, -kExtensionLength);

                if (_inner.TopRightRadius > 0)
                {
                    var r = new SKSize(_inner.TopRightRadius, _inner.TopRightRadius);
                    FindIntersection(edgeQuad[0], edgeQuad[1],
                        new SKPoint(edgeQuad[1].X - r.Width, edgeQuad[1].Y),
                        new SKPoint(edgeQuad[1].X, edgeQuad[1].Y + r.Height), ref edgeQuad[1]);
                    boundQuad1.X = edgeQuad[1].X;
                    boundQuad2.X = edgeQuad[1].X;

                    if (edgeQuad[1].Y > innerPoints[3].Y)
                        FindIntersection(edgeQuad[0], edgeQuad[1], innerPoints[3], innerPoints[2], ref edgeQuad[1]);
                    if (edgeQuad[1].X < innerPoints[3].X)
                        FindIntersection(edgeQuad[0], edgeQuad[1], innerPoints[0], innerPoints[3], ref edgeQuad[1]);
                    if (edgeQuad[2].Y > edgeQuad[1].Y && edgeQuad[2].X > edgeQuad[1].X)
                    {
                        edgePentagon = new[] { edgeQuad[0], edgeQuad[1], new SKPoint(edgeQuad[1].X, edgeQuad[2].Y), edgeQuad[2], edgeQuad[3] };
                        hasPentagon = true;
                    }
                }

                if (_inner.BottomRightRadius > 0)
                {
                    var r = new SKSize(_inner.BottomRightRadius, _inner.BottomRightRadius);
                    FindIntersection(edgeQuad[3], edgeQuad[2],
                        new SKPoint(edgeQuad[2].X - r.Width, edgeQuad[2].Y),
                        new SKPoint(edgeQuad[2].X, edgeQuad[2].Y - r.Height), ref edgeQuad[2]);
                    if (boundQuad1.X > edgeQuad[2].X)
                    {
                        boundQuad1.X = edgeQuad[2].X;
                        boundQuad2.X = edgeQuad[2].X;
                    }

                    if (edgeQuad[2].Y < innerPoints[0].Y)
                        FindIntersection(edgeQuad[3], edgeQuad[2], innerPoints[0], innerPoints[1], ref edgeQuad[2]);
                    if (edgeQuad[2].X < innerPoints[0].X)
                        FindIntersection(edgeQuad[3], edgeQuad[2], innerPoints[0], innerPoints[3], ref edgeQuad[2]);
                    if (edgeQuad[2].X < edgeQuad[1].X && edgeQuad[2].Y > edgeQuad[1].Y)
                    {
                        edgePentagon = new[] { edgeQuad[0], edgeQuad[1], new SKPoint(edgeQuad[2].X, edgeQuad[1].Y), edgeQuad[2], edgeQuad[3] };
                        hasPentagon = true;
                    }
                }
                break;
        }

        // Build the intersection of all required clip polygons into a single
        // clip region, then apply it once.
        bool antialias = firstMiter == MiterType.SoftMiter || secondMiter == MiterType.SoftMiter;
        var regions = new List<SKPoint[]>();

        if (firstMiter == secondMiter)
        {
            if (hasPentagon && !IsRenderable(_inner))
            {
                regions.Add(edgePentagon);
            }
            else
            {
                regions.Add(edgeQuad);
            }
        }
        else
        {
            if (firstMiter != MiterType.NoMiter)
            {
                var clippingQuad = new SKPoint[4];
                clippingQuad[0] = new SKPoint(edgeQuad[0].X + extensionOffset.X, edgeQuad[0].Y + extensionOffset.Y);
                var intersection = new SKPoint();
                FindIntersection(edgeQuad[0], edgeQuad[1], boundQuad1, boundQuad2, ref intersection);
                clippingQuad[1] = new SKPoint(intersection.X + extensionOffset.X, intersection.Y + extensionOffset.Y);
                clippingQuad[2] = boundQuad2;
                clippingQuad[3] = edgeQuad[3];
                regions.Add(clippingQuad);
            }

            if (secondMiter != MiterType.NoMiter)
            {
                var clippingQuad = new SKPoint[4];
                clippingQuad[0] = edgeQuad[0];
                clippingQuad[1] = boundQuad1;
                var intersection = new SKPoint();
                FindIntersection(edgeQuad[2], edgeQuad[3], boundQuad1, boundQuad2, ref intersection);
                clippingQuad[2] = new SKPoint(intersection.X - extensionOffset.X, intersection.Y - extensionOffset.Y);
                clippingQuad[3] = new SKPoint(edgeQuad[3].X - extensionOffset.X, edgeQuad[3].Y - extensionOffset.Y);
                regions.Add(clippingQuad);
            }
        }

        var clipPath = new SKPath();
        bool first = true;
        foreach (var region in regions)
        {
            var poly = new SKPath();
            poly.AddPoly(region, true);
            if (first)
            {
                clipPath.AddPath(poly);
                first = false;
            }
            else
            {
                var result = new SKPath();
                clipPath.Op(poly, SKPathOp.Intersect, result);
                clipPath.Dispose();
                clipPath = result;
            }
            poly.Dispose();
        }

        bool pushed = false;
        if (clipPath.Points.Length > 0 || !first)
        {
            EmitPushClipPath(clipPath, antialias);
            pushed = true;
        }
        clipPath.Dispose();
        return pushed;
    }

    private bool ColorsMatchAtCorner(BoxSide side, BoxSide adjacentSide)
    {
        if (!Edge(adjacentSide).ShouldRender)
            return false;

        if (!Edge(side).SharesColorWith(Edge(adjacentSide)))
            return false;

        return !BorderStyleHasUnmatchedColorsAtCorner(Edge(side).BorderStyleValue, side, adjacentSide);
    }

    public static void DrawBoxSide(DisplayList displayList, SKRect snappedEdgeRect, BoxSide side, SKColor color, BorderStyle style)
    {
        var painter = new BoxBorderPainter(displayList, snappedEdgeRect, DefaultStyleFor(color, style));
        painter.DrawLineForBoxSide((int)snappedEdgeRect.Left, (int)snappedEdgeRect.Top,
            (int)snappedEdgeRect.Right, (int)snappedEdgeRect.Bottom, side, color, style, 0, 0);
    }

    private static ComputedStyle DefaultStyleFor(SKColor color, BorderStyle style)
    {
        var s = new ComputedStyle();
        s.BorderTopWidth = s.BorderRightWidth = s.BorderBottomWidth = s.BorderLeftWidth = 1;
        s.BorderTopColor = s.BorderRightColor = s.BorderBottomColor = s.BorderLeftColor = color;
        s.BorderTopStyle = s.BorderRightStyle = s.BorderBottomStyle = s.BorderLeftStyle = style;
        return s;
    }
}
