using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public static class LayoutTextControl
{
    public static void StyleDidChange(Element? innerEditor, ComputedStyle? oldStyle, ComputedStyle newStyle)
    {
    }

    public static int ScrollbarThickness(Dom.LayoutBox box)
    {
        return 0;
    }

    public static float GetAvgCharWidth(ComputedStyle style)
    {
        if (style.FontSize > 0)
        {
            float width = style.FontSize * 0.5f;
            return Math.Max(width, MathF.Round(width));
        }
        const char ch = '0';
        return ComputeTextWidth(ch.ToString(), style);
    }

    public static bool HasValidAvgCharWidth(ComputedStyle style)
    {
        return style.FontSize > 0;
    }

    public static float ComputeTextWidth(string text, ComputedStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        return text.Length * style.FontSize * 0.5f;
    }

    public static void HitInnerEditorElement(
        Dom.LayoutBox box, Element innerEditor, HitTestResult result, HitTestLocation hitTestLocation, PhysicalOffset accumulatedOffset)
    {
        if (innerEditor.ComputedStyle is null)
            return;
        PhysicalOffset localPoint = hitTestLocation.Point - accumulatedOffset;
        result.OverrideNodeAndPosition(innerEditor, localPoint);
    }
}