using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Paints overflow controls for a layout scroll container.
/// Keeps scrollbars outside the scrolling contents transform and paints the
/// scrollbar corner when both axes are present.
///
/// Consumes per-element custom styling from ::-webkit-scrollbar-*
/// pseudo rules (<see cref="ComputedStyle.ScrollbarCustom"/>) and the standard
/// scrollbar-width / scrollbar-color properties. Unstyled scrollbars keep the
/// platform-neutral defaults.
/// </summary>
internal sealed class ScrollableAreaPainter
{
    private const float AutoThickness = 12f;
    private const float ThinThickness = 8f;
    private const float MinimumThumbLength = 20f;
    private static readonly SKColor DefaultTrackColor = new(240, 240, 240);
    private static readonly SKColor DefaultThumbColor = new(180, 180, 180);

    private readonly DisplayList _displayList;
    private readonly CustomScrollbarTheme _customTheme;

    // Resolved theme for the current Paint() call.
    private float _thickness = AutoThickness;
    private SKColor _trackColor = DefaultTrackColor;
    private SKColor _thumbColor = DefaultThumbColor;
    private float _thumbRadius;
    private bool _trackHasBorder;

    public ScrollableAreaPainter(DisplayList displayList)
    {
        _displayList = displayList;
        _customTheme = new CustomScrollbarTheme(displayList);
    }

    public void Paint(LayoutBox box, ComputedStyle style, float contentOffsetY, bool canResize = false)
    {
        // scrollbar-width: none — scrolling stays functional, bars disappear.
        if (style.ScrollbarWidth == ScrollbarWidthType.None)
            return;

        // If the element carries ::-webkit-scrollbar-* pseudo styles, delegate the
        // whole bar to the custom theme; it paints only the styled parts. All other
        // cases keep the classic platform-neutral scrollbar path below.
        if (style.ScrollbarCustom is { HasAny: true })
        {
            _customTheme.Paint(box, style, contentOffsetY, canResize);
            return;
        }

        ResolveTheme(style);

        var paddingBox = box.PaddingBox;
        bool hasVerticalOverflow = box.ScrollContentHeight > box.ContentBox.Height;
        bool hasHorizontalOverflow = box.ScrollContentWidth > box.ContentBox.Width;
        bool paintVertical = hasVerticalOverflow || ForcesVerticalScrollbar(style);
        bool paintHorizontal = hasHorizontalOverflow || ForcesHorizontalScrollbar(style);

        if (paintVertical)
            PaintVertical(box, paddingBox, contentOffsetY, hasVerticalOverflow, paintHorizontal);
        if (paintHorizontal)
            PaintHorizontal(box, paddingBox, contentOffsetY, hasHorizontalOverflow, paintVertical);
        if (paintVertical && paintHorizontal)
        {
            var corner = new SKRect(
                paddingBox.Right - _thickness,
                paddingBox.Bottom - _thickness + contentOffsetY,
                paddingBox.Right,
                paddingBox.Bottom + contentOffsetY);
            var cornerBg = style.ScrollbarCustom?.Corner?.Background
                           ?? style.ScrollbarTrackColor
                           ?? _trackColor;
            AddRect(corner, cornerBg);
        }
        if (canResize && (paintVertical || paintHorizontal))
        {
            PaintResizer(box, paddingBox, contentOffsetY);
        }
    }

    /// <summary>Resolve thickness / colors for this element from A1+A2 sources.</summary>
    private void ResolveTheme(ComputedStyle style)
    {
        _thickness = ScrollbarMetrics.ThicknessFor(style);

        var bar = style.ScrollbarCustom?.Bar;
        if (bar is { HasThickness: true })
            _thickness = Math.Clamp(bar.Thickness, 3f, 100f);

        _trackColor = style.ScrollbarCustom?.Track?.Background
                      ?? style.ScrollbarTrackColor
                      ?? DefaultTrackColor;
        _thumbColor = style.ScrollbarCustom?.Thumb?.Background
                      ?? style.ScrollbarThumbColor
                      ?? DefaultThumbColor;

        // #2: unified styling — default to ROUNDED thumb (matches page-level
        // scrollbar appearance). Custom ::-webkit-scrollbar-thumb border-radius
        // still takes precedence. Set to 0 in settings for square style.
        _thumbRadius = style.ScrollbarCustom?.Thumb?.BorderRadius
                       ?? MathF.Max(0f, _thickness / 2f - 1f);
        _trackHasBorder = false;
    }

    private void PaintVertical(LayoutBox box, SKRect paddingBox, float contentOffsetY,
        bool hasOverflow, bool hasHorizontalScrollbar)
    {
        float trackHeight = Math.Max(0, paddingBox.Height -
            (hasHorizontalScrollbar ? _thickness : 0));
        float trackX = paddingBox.Right - _thickness;
        float trackY = paddingBox.Top + contentOffsetY;
        AddRect(new SKRect(trackX, trackY, paddingBox.Right, trackY + trackHeight), _trackColor);

        if (!hasOverflow || trackHeight <= 0)
            return;

        float ratio = box.ContentBox.Height / Math.Max(1, box.ScrollContentHeight);
        float thumbHeight = Math.Min(trackHeight,
            Math.Max(MinimumThumbLength, trackHeight * Math.Min(1, ratio)));
        float scrollRange = Math.Max(1, box.ScrollContentHeight - box.ContentBox.Height);
        float scrollPosition = Math.Clamp(box.ScrollY, 0, scrollRange);
        float thumbY = trackY + (trackHeight - thumbHeight) * (scrollPosition / scrollRange);
        AddRect(new SKRect(trackX + 2, thumbY + 1,
            paddingBox.Right - 2, thumbY + thumbHeight - 1), _thumbColor, _thumbRadius);
    }

    private void PaintHorizontal(LayoutBox box, SKRect paddingBox, float contentOffsetY,
        bool hasOverflow, bool hasVerticalScrollbar)
    {
        float trackWidth = Math.Max(0, paddingBox.Width -
            (hasVerticalScrollbar ? _thickness : 0));
        float trackX = paddingBox.Left;
        float trackY = paddingBox.Bottom - _thickness + contentOffsetY;
        AddRect(new SKRect(trackX, trackY, trackX + trackWidth,
            paddingBox.Bottom + contentOffsetY), _trackColor);

        if (!hasOverflow || trackWidth <= 0)
            return;

        float ratio = box.ContentBox.Width / Math.Max(1, box.ScrollContentWidth);
        float thumbWidth = Math.Min(trackWidth,
            Math.Max(MinimumThumbLength, trackWidth * Math.Min(1, ratio)));
        float scrollRange = Math.Max(1, box.ScrollContentWidth - box.ContentBox.Width);
        float scrollPosition = Math.Clamp(box.ScrollX, 0, scrollRange);
        float thumbX = trackX + (trackWidth - thumbWidth) * (scrollPosition / scrollRange);
        AddRect(new SKRect(thumbX + 1, trackY + 2,
            thumbX + thumbWidth - 1, paddingBox.Bottom - 2 + contentOffsetY), _thumbColor, _thumbRadius);
    }

    private void AddRect(SKRect rect, SKColor color, float borderRadius = 0)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = rect;
        op.FillColor = color;
        op.BorderRadius = borderRadius;
        op.Bounds = rect;
        _displayList.Add(op);
    }

    private void PaintResizer(LayoutBox box, SKRect paddingBox, float contentOffsetY)
    {
        // Draw the resize grip in the scroll corner (bottom-right). Mirrors
        // DrawPlatformResizerImage: two dark diagonal lines with light
        // counterparts offset by one pixel below.
        float cornerSize = _thickness;
        float cornerX = paddingBox.Right - cornerSize;
        float cornerY = paddingBox.Bottom - cornerSize;

        float edge = 3;
        float midX = cornerX + cornerSize / 2;
        float midY = cornerY + cornerSize / 2;
        float bottomRightX = cornerX + cornerSize - edge;
        float bottomRightY = cornerY + cornerSize - edge;

        // First grip line (mid-top to right-mid).
        AddLine(midX, cornerY + edge, bottomRightX, midY, new SKColor(0x00, 0x00, 0x00, 0x99));
        // Second grip line (bottom-mid to mid-right).
        AddLine(cornerX + edge, bottomRightY, bottomRightX, cornerY + cornerSize - edge, new SKColor(0x00, 0x00, 0x00, 0x99));
    }

    private void AddLine(float x1, float y1, float x2, float y2, SKColor color)
    {
        var op = PaintOpPool.GetDrawLineOp();
        op.X1 = x1;
        op.Y1 = y1;
        op.X2 = x2;
        op.Y2 = y2;
        op.StrokeWidth = 1;
        op.Color = color;
        op.Bounds = new SKRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(x1, x2));
        _displayList.Add(op);
    }

    private static bool ForcesVerticalScrollbar(ComputedStyle style) =>
        style.OverflowY == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;

    private static bool ForcesHorizontalScrollbar(ComputedStyle style) =>
        style.OverflowX == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
}
