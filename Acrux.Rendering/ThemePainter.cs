using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// Paints native-themed form controls based on -webkit-appearance.
/// Mirrors theme_painter.cc. Each Paint method returns true if it handled
/// the painting, false to fall back to CSS painting.
/// </summary>
internal sealed class ThemePainter
{
    private readonly DisplayList _displayList;

    public ThemePainter(DisplayList displayList)
    {
        _displayList = displayList;
    }

    public bool Paint(Element element, ComputedStyle style, LayoutBox box, float contentOffsetY)
    {
        // An author 'appearance: none' strips the widget; 'auto' paints the element's own
        // control, and a compat keyword ('textfield', 'button') paints whatever the page named.
        var appearance = style.PaintAppearance;
        if (string.IsNullOrEmpty(appearance) || appearance == "none" || appearance == "auto")
            return false;

        var rect = new SKRect(
            box.BorderBox.Left,
            box.BorderBox.Top + contentOffsetY,
            box.BorderBox.Right,
            box.BorderBox.Bottom + contentOffsetY);

        var lower = appearance.ToLowerInvariant();
        return lower switch
        {
            "checkbox" => PaintCheckbox(element, style, rect),
            "radio" => PaintRadio(element, style, rect),
            "push-button" or "square-button" or "button" => PaintButton(element, style, rect),
            "textfield" or "searchfield" => PaintTextField(style, rect),
            "textarea" => PaintTextArea(style, rect),
            "menulist" or "menulist-button" => PaintMenuList(element, style, rect),
            "progress-bar" => PaintProgressBar(style, rect),
            "slider-horizontal" or "slider-vertical" => PaintSliderTrack(style, rect),
            "sliderthumb-horizontal" or "sliderthumb-vertical" => PaintSliderThumb(style, rect),
            _ => false
        };
    }

    private bool PaintCheckbox(Element element, ComputedStyle style, SKRect rect)
    {
        float size = Math.Min(rect.Width, rect.Height);
        var box = new SKRect(rect.Left, rect.Top, rect.Left + size, rect.Top + size);

        var bgOp = PaintOpPool.GetDrawRectOp();
        bgOp.Rect = box;
        bgOp.FillColor = SKColors.White;
        bgOp.BorderTopWidth = 1;
        bgOp.BorderRightWidth = 1;
        bgOp.BorderBottomWidth = 1;
        bgOp.BorderLeftWidth = 1;
        bgOp.BorderTopColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderRightColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderBottomColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderLeftColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.Bounds = box;
        _displayList.Add(bgOp);

        bool isChecked = element.HasAttribute("checked");
        if (isChecked)
        {
            float inset = size * 0.2f;
            float x1 = box.Left + inset;
            float y1 = box.Top + size * 0.5f;
            float x2 = box.Left + size * 0.4f;
            float y2 = box.Bottom - inset;
            float x3 = box.Right - inset;
            float y3 = box.Top + inset;

            var line1 = PaintOpPool.GetDrawLineOp();
            line1.X1 = x1; line1.Y1 = y1; line1.X2 = x2; line1.Y2 = y2;
            line1.StrokeWidth = 2;
            line1.Color = new SKColor(0x1A, 0x73, 0xE8, 0xFF);
            line1.Bounds = box;
            _displayList.Add(line1);

            var line2 = PaintOpPool.GetDrawLineOp();
            line2.X1 = x2; line2.Y1 = y2; line2.X2 = x3; line2.Y2 = y3;
            line2.StrokeWidth = 2;
            line2.Color = new SKColor(0x1A, 0x73, 0xE8, 0xFF);
            line2.Bounds = box;
            _displayList.Add(line2);
        }
        return true;
    }

    private bool PaintRadio(Element element, ComputedStyle style, SKRect rect)
    {
        float size = Math.Min(rect.Width, rect.Height);
        float cx = rect.Left + size / 2;
        float cy = rect.Top + size / 2;
        float radius = size / 2;

        var circlePath = new SKPath();
        circlePath.AddCircle(cx, cy, radius);
        var bgOp = PaintOpPool.GetDrawPathOp();
        bgOp.Path = circlePath;
        bgOp.FillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        bgOp.StrokePaint = new SKPaint { Color = new SKColor(0x76, 0x76, 0x76, 0xFF), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        bgOp.Bounds = rect;
        _displayList.Add(bgOp);

        bool isChecked = element.HasAttribute("checked");
        if (isChecked)
        {
            var dotPath = new SKPath();
            dotPath.AddCircle(cx, cy, radius * 0.35f);
            var dotOp = PaintOpPool.GetDrawPathOp();
            dotOp.Path = dotPath;
            dotOp.FillPaint = new SKPaint { Color = new SKColor(0x1A, 0x73, 0xE8, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true };
            dotOp.Bounds = rect;
            _displayList.Add(dotOp);
        }
        return true;
    }

    private bool PaintButton(Element element, ComputedStyle style, SKRect rect)
    {
        var bgOp = PaintOpPool.GetDrawRectOp();
        bgOp.Rect = rect;
        bgOp.FillColor = new SKColor(0xE0, 0xE0, 0xE0, 0xFF);
        bgOp.BorderTopWidth = 1;
        bgOp.BorderRightWidth = 1;
        bgOp.BorderBottomWidth = 1;
        bgOp.BorderLeftWidth = 1;
        bgOp.BorderTopColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderRightColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderBottomColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderLeftColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.Bounds = rect;
        _displayList.Add(bgOp);
        return true;
    }

    private bool PaintTextField(ComputedStyle style, SKRect rect)
    {
        var bgOp = PaintOpPool.GetDrawRectOp();
        bgOp.Rect = rect;
        bgOp.FillColor = SKColors.White;
        bgOp.BorderTopWidth = 1;
        bgOp.BorderRightWidth = 1;
        bgOp.BorderBottomWidth = 1;
        bgOp.BorderLeftWidth = 1;
        bgOp.BorderTopColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderRightColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderBottomColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderLeftColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.Bounds = rect;
        _displayList.Add(bgOp);
        return true;
    }

    private bool PaintTextArea(ComputedStyle style, SKRect rect) => PaintTextField(style, rect);

    private bool PaintMenuList(Element element, ComputedStyle style, SKRect rect)
    {
        var bgOp = PaintOpPool.GetDrawRectOp();
        bgOp.Rect = rect;
        bgOp.FillColor = new SKColor(0xF0, 0xF0, 0xF0, 0xFF);
        bgOp.BorderTopWidth = 1;
        bgOp.BorderRightWidth = 1;
        bgOp.BorderBottomWidth = 1;
        bgOp.BorderLeftWidth = 1;
        bgOp.BorderTopColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderRightColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderBottomColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.BorderLeftColor = new SKColor(0x76, 0x76, 0x76, 0xFF);
        bgOp.Bounds = rect;
        _displayList.Add(bgOp);

        float arrowSize = 6;
        float arrowX = rect.Right - 14;
        float arrowY = rect.MidY - arrowSize / 2;
        var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX, arrowY);
        arrowPath.LineTo(arrowX + arrowSize, arrowY);
        arrowPath.LineTo(arrowX + arrowSize / 2, arrowY + arrowSize);
        arrowPath.Close();
        var arrowOp = PaintOpPool.GetDrawPathOp();
        arrowOp.Path = arrowPath;
        arrowOp.FillPaint = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true };
        arrowOp.Bounds = rect;
        _displayList.Add(arrowOp);
        return true;
    }

    private bool PaintProgressBar(ComputedStyle style, SKRect rect)
    {
        var bgOp = PaintOpPool.GetDrawRectOp();
        bgOp.Rect = rect;
        bgOp.FillColor = new SKColor(0xE0, 0xE0, 0xE0, 0xFF);
        bgOp.Bounds = rect;
        _displayList.Add(bgOp);
        return true;
    }

    private bool PaintSliderTrack(ComputedStyle style, SKRect rect)
    {
        float cy = rect.MidY;
        float trackHeight = 4;
        var trackOp = PaintOpPool.GetDrawRectOp();
        trackOp.Rect = new SKRect(rect.Left, cy - trackHeight / 2, rect.Right, cy + trackHeight / 2);
        trackOp.FillColor = new SKColor(0xC0, 0xC0, 0xC0, 0xFF);
        trackOp.Bounds = rect;
        _displayList.Add(trackOp);
        return true;
    }

    private bool PaintSliderThumb(ComputedStyle style, SKRect rect)
    {
        float size = Math.Min(rect.Width, rect.Height);
        float cx = rect.MidX;
        float cy = rect.MidY;
        var thumbPath = new SKPath();
        thumbPath.AddCircle(cx, cy, size / 2);
        var thumbOp = PaintOpPool.GetDrawPathOp();
        thumbOp.Path = thumbPath;
        thumbOp.FillPaint = new SKPaint { Color = new SKColor(0x1A, 0x73, 0xE8, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true };
        thumbOp.Bounds = rect;
        _displayList.Add(thumbOp);
        return true;
    }
}