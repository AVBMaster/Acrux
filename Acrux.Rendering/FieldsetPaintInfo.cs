using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Rendering;

/// <summary>
/// Fieldset block-start border offset and cut-out rectangle caused by the rendered legend.
/// Mirrors FieldsetPaintInfo in fieldset_paint_info.h.
/// </summary>
public readonly struct FieldsetPaintInfo
{
    public PhysicalBoxStrut BorderOutsets { get; }
    public PhysicalRect LegendCutoutRect { get; }

    public FieldsetPaintInfo(ComputedStyle fieldsetStyle, PhysicalSize fieldsetSize, PhysicalBoxStrut fieldsetBorders, PhysicalRect legendBorderBox)
    {
        var wm = fieldsetStyle.WritingMode;
        bool isHorizontal = wm == WritingModeType.HorizontalTb;
        bool isFlipped = wm == WritingModeType.VerticalRl;

        var outsets = new PhysicalBoxStrut(0, 0, 0, 0);

        if (isHorizontal)
        {
            float legendSize = legendBorderBox.Height;
            float borderSize = fieldsetBorders.Top;
            float legendExcess = legendSize - borderSize;
            if (legendExcess > 0)
                outsets = new PhysicalBoxStrut(legendExcess / 2f, 0, 0, 0);
            BorderOutsets = outsets;
            LegendCutoutRect = new PhysicalRect(
                legendBorderBox.X, 0,
                legendBorderBox.Width, Math.Max(legendSize, borderSize));
        }
        else
        {
            float legendSize = legendBorderBox.Width;
            float cutoutX = 0;

            if (isFlipped)
            {
                // vertical-rl
                float borderSize = fieldsetBorders.Right;
                float legendExcess = legendSize - borderSize;
                if (legendExcess > 0)
                    outsets = new PhysicalBoxStrut(0, legendExcess / 2f, 0, 0);
                cutoutX = fieldsetSize.Width - Math.Max(legendSize, borderSize);
            }
            else
            {
                // vertical-lr
                float borderSize = fieldsetBorders.Left;
                float legendExcess = legendSize - borderSize;
                if (legendExcess > 0)
                    outsets = new PhysicalBoxStrut(0, 0, 0, legendExcess / 2f);
            }

            BorderOutsets = outsets;
            float totalBlock = Math.Max(legendSize, fieldsetBorders.Left + fieldsetBorders.Right);
            LegendCutoutRect = new PhysicalRect(
                cutoutX, legendBorderBox.Y,
                totalBlock, legendBorderBox.Height);
        }
    }
}