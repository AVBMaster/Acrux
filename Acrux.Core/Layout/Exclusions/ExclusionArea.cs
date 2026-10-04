using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Optional shape data for an exclusion (shape-outside).
/// Mirrors ExclusionShapeData in exclusion_area.h.
/// </summary>
public class ExclusionShapeData
{
    public LayoutBox? LayoutBox { get; }
    public BoxStrut Margins { get; }
    public BoxStrut ShapeInsets { get; }

    public ExclusionShapeData(LayoutBox? layoutBox, BoxStrut margins, BoxStrut shapeInsets)
    {
        LayoutBox = layoutBox;
        Margins = margins;
        ShapeInsets = shapeInsets;
    }
}

/// <summary>
/// A rectangular exclusion area for floats and initial letter boxes.
/// Mirrors ExclusionArea in exclusion_area.h.
/// </summary>
public class ExclusionArea
{
    public enum Kind
    {
        Float,
        InitialLetterBox,
    }

    public BfcRect Rect { get; }
    public FloatType Type { get; }
    public Kind ExclusionKind { get; }
    public bool IsHiddenForPaint { get; }
    public ExclusionShapeData? ShapeData { get; }
    public bool IsPastOtherExclusions { get; set; }

    public ExclusionArea(BfcRect rect, FloatType type, Kind kind, bool isHiddenForPaint, ExclusionShapeData? shapeData = null)
    {
        Rect = rect;
        Type = type;
        ExclusionKind = kind;
        IsHiddenForPaint = isHiddenForPaint;
        ShapeData = shapeData;
    }

    public bool IsForInitialLetterBox => ExclusionKind == Kind.InitialLetterBox;

    public static ExclusionArea Create(BfcRect rect, FloatType type, bool isHiddenForPaint, ExclusionShapeData? shapeData = null)
        => new(rect, type, Kind.Float, isHiddenForPaint, shapeData);

    public static ExclusionArea CreateForInitialLetterBox(BfcRect rect, FloatType type, bool isHiddenForPaint)
        => new(rect, type, Kind.InitialLetterBox, isHiddenForPaint);

    public ExclusionArea CopyWithOffset(BfcDelta delta)
    {
        if (delta.LineOffsetDelta == 0 && delta.BlockOffsetDelta == 0)
            return this;
        return new ExclusionArea(Rect + delta, Type, ExclusionKind, IsHiddenForPaint, ShapeData);
    }
}