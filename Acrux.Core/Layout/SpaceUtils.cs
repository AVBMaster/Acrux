using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Space utilities for layout. Mirrors space_utils.cc.
/// </summary>
public static class SpaceUtils
{
    public static bool AdjustToClearance(float clearanceOffset, ref BfcOffset offset)
    {
        if (clearanceOffset > offset.BlockOffset)
        {
            offset = new BfcOffset(offset.LineOffset, clearanceOffset);
            return true;
        }
        return false;
    }

    public static bool IsParallelWritingMode(WritingMode a, WritingMode b)
    {
        return (a == WritingMode.HorizontalTb) == (b == WritingMode.HorizontalTb);
    }

    public static bool ShouldBlockContainerChildStretchAutoInlineSize(ComputedStyle style)
    {
        return style.Width is AutoLength || style.Width == null;
    }
}

/// <summary>
/// Text utilities. Mirrors text_utils.cc.
/// </summary>
public static class TextUtils
{
    public static float ComputeTextWidth(string text, ComputedStyle style)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length * style.FontSize * 0.5f;
    }
}

/// <summary>
/// Text decoration offset computation. Mirrors text_decoration_offset.cc.
/// </summary>
public class TextDecorationOffset
{
    private readonly ComputedStyle _textStyle;

    public TextDecorationOffset(ComputedStyle textStyle)
    {
        _textStyle = textStyle;
    }

    public int ComputeUnderlineOffset(float computedFontSize, float textDecorationThickness = 1)
    {
        // CSS Text Decoration spec: underline offset is typically 1em below the
        // baseline, but for vertical text it's different.
        int gap = Math.Max(1, (int)Math.Ceiling(textDecorationThickness / 2f));
        return gap;
    }

    public int ComputeUnderlineOffsetForUnder(float computedFontSize, float textDecorationThickness = 1)
    {
        return ComputeUnderlineOffset(computedFontSize, textDecorationThickness);
    }
}