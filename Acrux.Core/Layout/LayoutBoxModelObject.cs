using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// LayoutBoxModelObject - base class for LayoutBox and LayoutInline.
/// Mirrors layout_box_model_object.cc. Exposes the common box-model concepts
/// (margins, borders, paddings) and PaintLayer management shared by the two
/// classes that own a CSS box: the full LayoutBox and LayoutInline.
/// </summary>
public abstract class LayoutBoxModelObject : LayoutObject
{
    protected LayoutBoxModelObject(Node? node) : base(node)
    {
    }

    /// <summary>Continuation pointer, used when a LayoutInline is split around block content.</summary>
    public LayoutBoxModelObject? Continuation { get; set; }

    public override bool IsBoxModelObject => true;

    // ===== Borders =====

    public virtual float BorderTop => StyleRef().BorderTopWidth;
    public virtual float BorderRight => StyleRef().BorderRightWidth;
    public virtual float BorderBottom => StyleRef().BorderBottomWidth;
    public virtual float BorderLeft => StyleRef().BorderLeftWidth;

    public float BorderLeftAndRight => BorderLeft + BorderRight;
    public float BorderTopAndBottom => BorderTop + BorderBottom;
    public float BorderWidth => BorderLeft + BorderRight;
    public float BorderHeight => BorderTop + BorderBottom;

    public float BorderBlockStart => IsHorizontalWritingMode ? BorderTop : BorderLeft;
    public float BorderBlockEnd => IsHorizontalWritingMode ? BorderBottom : BorderRight;
    public float BorderInlineStart => IsHorizontalWritingMode ? BorderLeft : BorderTop;
    public float BorderInlineEnd => IsHorizontalWritingMode ? BorderRight : BorderBottom;

    public PhysicalBoxStrut BorderOutsets() => new(BorderTop, BorderRight, BorderBottom, BorderLeft);

    // ===== Padding =====

    /// <summary>CSS computed padding (before any layout-time adjustments).</summary>
    public float ComputedCSSPaddingTop => ResolveLength(StyleRef().PaddingTop);
    public float ComputedCSSPaddingRight => ResolveLength(StyleRef().PaddingRight);
    public float ComputedCSSPaddingBottom => ResolveLength(StyleRef().PaddingBottom);
    public float ComputedCSSPaddingLeft => ResolveLength(StyleRef().PaddingLeft);

    public virtual float PaddingTop => ComputedCSSPaddingTop;
    public virtual float PaddingRight => ComputedCSSPaddingRight;
    public virtual float PaddingBottom => ComputedCSSPaddingBottom;
    public virtual float PaddingLeft => ComputedCSSPaddingLeft;

    public float PaddingLeftAndRight => PaddingLeft + PaddingRight;
    public float PaddingTopAndBottom => PaddingTop + PaddingBottom;

    public float PaddingBlockStart => IsHorizontalWritingMode ? PaddingTop : PaddingLeft;
    public float PaddingBlockEnd => IsHorizontalWritingMode ? PaddingBottom : PaddingRight;
    public float PaddingInlineEnd => IsHorizontalWritingMode ? PaddingRight : PaddingBottom;

    public PhysicalBoxStrut PaddingOutsets() => new(PaddingTop, PaddingRight, PaddingBottom, PaddingLeft);

    public float BorderAndPaddingWidth => BorderLeftAndRight + PaddingLeftAndRight;
    public float BorderAndPaddingHeight => BorderTopAndBottom + PaddingTopAndBottom;
    public float BorderAndPaddingBlockStart => BorderBlockStart + PaddingBlockStart;
    public float BorderAndPaddingBlockEnd => BorderBlockEnd + PaddingBlockEnd;
    public float BorderAndPaddingBlockSize => IsHorizontalWritingMode ? BorderAndPaddingHeight : BorderAndPaddingWidth;
    public float BorderAndPaddingInlineSize => IsHorizontalWritingMode ? BorderAndPaddingWidth : BorderAndPaddingHeight;
    public float BorderAndPaddingInlineStart => BorderInlineStart + (IsHorizontalWritingMode ? PaddingLeft : PaddingTop);

    // ===== Margins =====

    public virtual float MarginTop => ResolveLength(StyleRef().MarginTop);
    public virtual float MarginRight => ResolveLength(StyleRef().MarginRight);
    public virtual float MarginBottom => ResolveLength(StyleRef().MarginBottom);
    public virtual float MarginLeft => ResolveLength(StyleRef().MarginLeft);

    public float MarginWidth => MarginLeft + MarginRight;
    public float MarginHeight => MarginTop + MarginBottom;

    public float MarginBlockStart => IsHorizontalWritingMode ? MarginTop : MarginLeft;
    public float MarginBlockEnd => IsHorizontalWritingMode ? MarginBottom : MarginRight;
    public float MarginInlineStart => IsHorizontalWritingMode ? MarginLeft : MarginTop;
    public float MarginInlineEnd => IsHorizontalWritingMode ? MarginRight : MarginBottom;

    public MarginStrut MarginLogicalHeight => new(MarginBlockStart + MarginBlockEnd, 0);

    public PhysicalBoxStrut MarginOutsets() => new(MarginTop, MarginRight, MarginBottom, MarginLeft);

    /// <summary>Resolve a CSS length to a pixel value (percent is resolved against font size as a fallback).</summary>
    protected static float ResolveLength(Length? length)
    {
        if (length == null || length is AutoLength)
            return 0;
        try
        {
            var px = length.ToPixels(16f, 16f, 0f, 0f);
            return float.IsNaN(px) ? 0 : Math.Max(0, px);
        }
        catch
        {
            return 0;
        }
    }

    // ===== PaintLayer management =====

    /// <summary>The type of PaintLayer to instantiate for this object. See PaintLayerType.</summary>
    public override PaintLayerType LayerTypeRequired() => PaintLayerType.NoPaintLayer;

    /// <summary>True if this object has a layer that is self-painting.</summary>
    public bool HasSelfPaintingLayer() => LayerTypeRequired() is PaintLayerType.ForcedPaintLayer or PaintLayerType.NormalPaintLayer;

    public void DestroyLayer() => SetHasLayer(false);

    // ===== IE extensions (offsetWidth/Height etc.) =====

    public virtual float OffsetLeft(Element? element) => 0;
    public virtual float OffsetTop(Element? element) => 0;
    public virtual float OffsetWidth() => 0;
    public virtual float OffsetHeight() => 0;

    public virtual float FirstLineHeight() => 0;

    /// <summary>Returns the visual overflow rect of this object in physical coordinates.</summary>
    public virtual PhysicalRect VisualOverflowRect() => PhysicalRect.Zero;

    // ===== Tree manipulation =====

    /// <summary>Move |child| to another box model object, maintaining sibling links.</summary>
    public void MoveChildTo(LayoutBoxModelObject? toBoxModelObject, LayoutObject? child, LayoutObject? beforeChild = null, bool fullRemoveInsert = false)
    {
        if (child == null || toBoxModelObject == null)
            return;
        if (Parent != null && ReferenceEquals(Parent, this))
            RemoveChild(child);
        if (toBoxModelObject is LayoutBlock targetBlock)
            targetBlock.AddChild(child, beforeChild);
        else
            toBoxModelObject.AddChild(child, beforeChild);
    }

    /// <summary>Move all children to another box model object.</summary>
    public void MoveAllChildrenTo(LayoutBoxModelObject? toBoxModelObject, LayoutObject? beforeChild = null, bool fullRemoveInsert = false)
    {
        if (toBoxModelObject == null)
            return;
        var children = new List<LayoutObject>(Children);
        foreach (var child in children)
            MoveChildTo(toBoxModelObject, child, beforeChild, fullRemoveInsert);
    }

    /// <summary>Move children from |startChild| up to (but excluding) |endChild|.</summary>
    public void MoveChildrenTo(LayoutBoxModelObject? toBoxModelObject, LayoutObject? startChild, LayoutObject? endChild,
        LayoutObject? beforeChild = null, bool fullRemoveInsert = false)
    {
        if (toBoxModelObject == null)
            return;
        var children = new List<LayoutObject>();
        bool inRange = startChild == null;
        foreach (var child in Children)
        {
            if (child == startChild)
            {
                inRange = true;
                if (startChild == null) continue;
            }
            if (inRange)
            {
                if (child == endChild)
                    break;
                children.Add(child);
            }
        }
        foreach (var child in children)
            MoveChildTo(toBoxModelObject, child, beforeChild, fullRemoveInsert);
    }
}