using System.Collections.Generic;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Represents a node in the stacked element tree (aka paint tree) for a
/// stacking context. It caches the z-order lists used for painting and
/// hit-testing. Mirrors PaintLayerStackingNode.
///
/// Only layers that are stacked (positioned, or stacking contexts themselves,
/// or inline/flex/grid items with a non-auto z-index) are collected; the walk
/// stops at child stacking contexts, whose own children belong to their own
/// stacking node. Lists are order-sorted (CSS 'order') then z-index-sorted.
/// </summary>
public sealed class PaintLayerStackingNode
{
    public PaintLayer Layer { get; }

    private readonly List<PaintLayer> _posZOrderList = new();
    private readonly List<PaintLayer> _negZOrderList = new();
    private bool _zOrderListsDirty = true;

    public PaintLayerStackingNode(PaintLayer layer)
    {
        Layer = layer;
    }

    /// <summary>Layers with z-index >= 0, ascending by z-index (stable).</summary>
    public IReadOnlyList<PaintLayer> PosZOrderList => _posZOrderList;

    /// <summary>Layers with z-index &lt; 0, ascending by z-index (stable).</summary>
    public IReadOnlyList<PaintLayer> NegZOrderList => _negZOrderList;

    public bool ZOrderListsDirty => _zOrderListsDirty;

    public void DirtyZOrderLists()
    {
        _posZOrderList.Clear();
        _negZOrderList.Clear();
        _zOrderListsDirty = true;
    }

    public void UpdateZOrderLists()
    {
        if (_zOrderListsDirty)
            RebuildZOrderLists();
    }

    /// <summary>
    /// Rebuilds the positive/negative z-order lists by walking the paint-layer
    /// tree below this stacking context. Mirrors RebuildZOrderLists().
    /// </summary>
    private void RebuildZOrderLists()
    {
        var orderSortedChildren = new List<PaintLayer>();
        foreach (var child in Layer.Children)
            orderSortedChildren.Add(child);
        foreach (var child in PaintLayer.SortByOrder(orderSortedChildren))
            CollectLayers(child, Layer);
        _posZOrderList.Sort(ZIndexLessThan);
        _negZOrderList.Sort(ZIndexLessThan);

        _zOrderListsDirty = false;
    }

    private static int ZIndexLessThan(PaintLayer a, PaintLayer b) => a.ZIndex.CompareTo(b.ZIndex);

    /// <summary>
    /// Collects stacked layers reachable from <paramref name="paintLayer"/> into
    /// this stacking node's lists. Stops when a stacking context is reached (its
    /// subtree belongs to its own stacking node). Mirrors CollectLayers().
    /// Overlay-overflow-control reparenting (scrollbar painting) does not apply
    /// in this engine and is omitted.
    /// </summary>
    private void CollectLayers(PaintLayer paintLayer, PaintLayer root)
    {
        if (object.ReferenceEquals(paintLayer, root))
            return;

        var style = paintLayer.Style;

        if (IsStacked(paintLayer))
        {
            if (paintLayer.ZIndex >= 0)
                _posZOrderList.Add(paintLayer);
            else
                _negZOrderList.Add(paintLayer);
        }

        if (paintLayer.IsStackingContext)
            return;

        var children = new List<PaintLayer>();
        foreach (var child in paintLayer.Children)
            children.Add(child);
        var orderSortedChildren = PaintLayer.SortByOrder(children);
        foreach (var child in orderSortedChildren)
            CollectLayers(child, root);
    }

    /// <summary>
    /// A stacked element is a positioned element, a stacking context, or a
    /// flex/grid/inline item with a non-auto z-index. Mirrors
    /// LayoutObject::IsStacked().
    /// </summary>
    private static bool IsStacked(PaintLayer layer)
    {
        if (layer.IsStackingContext || layer.IsPositioned)
            return true;
        var parentDisplay = layer.Element.ParentElement?.ComputedStyle?.Display;
        bool isFlexOrGridChild = parentDisplay is
            Acrux.Core.Dom.DisplayType.Flex or
            Acrux.Core.Dom.DisplayType.InlineFlex or
            Acrux.Core.Dom.DisplayType.Grid or
            Acrux.Core.Dom.DisplayType.InlineGrid;
        return isFlexOrGridChild && layer.Style.ZIndex.HasValue;
    }

    /// <summary>Invoked at element level from style recalculation; returns true when the stacking structure changed.</summary>
    public static bool StyleDidChange(PaintLayer layer, ComputedStyle? oldStyle)
    {
        bool wasStackingContext = oldStyle != null && CreatesStackingContext(layer, oldStyle);
        bool wasStacked = oldStyle != null && IsStackedStyle(layer, oldStyle);
        int oldZIndex = oldStyle?.ZIndex ?? 0;
        int oldOrder = oldStyle?.Order ?? 0;

        var newStyle = layer.Style;
        bool shouldBeStackingContext = layer.IsStackingContext;
        bool shouldBeStacked = IsStacked(layer);

        if (shouldBeStackingContext == wasStackingContext &&
            wasStacked == shouldBeStacked &&
            oldZIndex == (newStyle.ZIndex ?? 0) &&
            oldOrder == newStyle.Order)
        {
            return false;
        }
        return true;
    }

    private static bool CreatesStackingContext(PaintLayer layer, ComputedStyle style)
    {
        if (style.Position is Acrux.Core.Dom.PositionType.Absolute
                or Acrux.Core.Dom.PositionType.Fixed
                or Acrux.Core.Dom.PositionType.Relative
                or Acrux.Core.Dom.PositionType.Sticky
            && style.ZIndex.HasValue)
            return true;
        if (style.Position == Acrux.Core.Dom.PositionType.Fixed) return true;
        if (style.Opacity < 1.0f) return true;
        return false;
    }

    private static bool IsStackedStyle(PaintLayer layer, ComputedStyle style)
    {
        if (CreatesStackingContext(layer, style)) return true;
        if (style.Position != Acrux.Core.Dom.PositionType.Static) return true;
        var parentDisplay = layer.Element.ParentElement?.ComputedStyle?.Display;
        bool isFlexOrGridChild = parentDisplay is
            Acrux.Core.Dom.DisplayType.Flex or
            Acrux.Core.Dom.DisplayType.InlineFlex or
            Acrux.Core.Dom.DisplayType.Grid or
            Acrux.Core.Dom.DisplayType.InlineGrid;
        return isFlexOrGridChild && style.ZIndex.HasValue;
    }
}