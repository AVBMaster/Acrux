using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Paints the fieldset element border with legend cutout.
/// Mirrors fieldset_painter.cc.
/// </summary>
internal sealed class FieldsetPainter
{
    private const float BorderRadius = 0;

    public static void PaintFieldsetBorder(DisplayList displayList, Element element, ComputedStyle style, SKRect borderRect, float contentOffsetY)
    {
        float borderWidth = Math.Max(1, style.BorderTopWidth);
        var legend = FindLegend(element);
        float legendWidth = 0;
        float legendHeight = 0;
        float legendLeft = borderRect.Left + 10;

        if (legend != null)
        {
            var legendBox = legend.LayoutBox;
            if (legendBox != null)
            {
                legendWidth = legendBox.BorderBox.Width;
                legendHeight = legendBox.BorderBox.Height;
                legendLeft = legendBox.BorderBox.Left;
            }
            else
            {
                var legendStyle = legend.ComputedStyle;
                if (legendStyle != null)
                {
                    legendWidth = legendStyle.FontSize * 4;
                    legendHeight = legendStyle.FontSize * 1.2f;
                }
            }
        }

        // Draw the four border sides, with the top side having a gap for the legend
        float halfWidth = borderWidth / 2f;
        float gapLeft = legendLeft - 4;
        float gapRight = legendLeft + legendWidth + 4;

        // Top border: left segment (left edge to gap)
        if (gapLeft > borderRect.Left)
        {
            DrawLine(displayList,
                borderRect.Left, borderRect.Top + halfWidth,
                gapLeft, borderRect.Top + halfWidth,
                borderWidth, style.BorderTopColor);
        }

        // Top border: right segment (gap to right edge)
        if (gapRight < borderRect.Right)
        {
            DrawLine(displayList,
                gapRight, borderRect.Top + halfWidth,
                borderRect.Right, borderRect.Top + halfWidth,
                borderWidth, style.BorderTopColor);
        }

        // Right border
        DrawLine(displayList,
            borderRect.Right - halfWidth, borderRect.Top,
            borderRect.Right - halfWidth, borderRect.Bottom,
            borderWidth, style.BorderRightColor);

        // Bottom border
        DrawLine(displayList,
            borderRect.Left, borderRect.Bottom - halfWidth,
            borderRect.Right, borderRect.Bottom - halfWidth,
            borderWidth, style.BorderBottomColor);

        // Left border
        DrawLine(displayList,
            borderRect.Left + halfWidth, borderRect.Top,
            borderRect.Left + halfWidth, borderRect.Bottom,
            borderWidth, style.BorderLeftColor);
    }

    private static Element? FindLegend(Element fieldset)
    {
        foreach (var child in fieldset.Children)
        {
            if (child is Element el && el.TagName.Equals("LEGEND", StringComparison.OrdinalIgnoreCase))
                return el;
        }
        return null;
    }

    private static void DrawLine(DisplayList displayList, float x1, float y1, float x2, float y2, float width, SKColor color)
    {
        var op = PaintOpPool.GetDrawLineOp();
        op.X1 = x1;
        op.Y1 = y1;
        op.X2 = x2;
        op.Y2 = y2;
        op.StrokeWidth = width;
        op.Color = color;
        op.Bounds = new SKRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
        displayList.Add(op);
    }
}