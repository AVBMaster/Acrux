using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;

namespace Acrux.Rendering;

/// <summary>
/// PaintLayer - manages stacking contexts and z-ordering, mirroring the engine's PaintLayer.
/// Maintains separate z-order lists for negative, auto, and positive z-index children,
/// and provides the correct CSS paint order (background → negative-z → block bg → float →
/// inline → auto-z → positive-z).
/// </summary>
public class PaintLayer
{
    public Element Element { get; }
    public LayoutBox LayoutBox { get; }
    public ComputedStyle Style { get; }
    public PaintLayer? Parent { get; set; }

    public int ZIndex { get; set; }
    /// <summary>
    /// The used value of the CSS 'order' property. Paint order of a flex or grid
    /// container's children follows 'order' (CSS Flexbox §painting), so it has to
    /// be captured here and applied before z-index collection.
    /// </summary>
    public int Order { get; set; }
    /// <summary>
    /// True when this layer's box participates in a flex or grid container, i.e.
    /// when its 'order' is relevant for paint ordering.
    /// </summary>
    public bool ParticipatesInOrderedLayout { get; set; }
    public bool IsStackingContext { get; set; }
    public bool IsTransparent { get; set; }
    public bool HasClip { get; set; }
    public SKRect ClipRect { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public bool IsSelfPainting { get; set; }
    public bool IsPositioned { get; set; }
    public bool IsFloating { get; set; }

    /// <summary>Paint data for this layer's fragment; used by the cull-rect updater.</summary>
    public Core.Paint.FragmentData? FragmentData { get; set; }

    // Z-order lists (mirroring the engine's stacking node)
    public List<PaintLayer> NegativeZOrder { get; } = new();
    public List<PaintLayer> NormalFlow { get; } = new();
    public List<PaintLayer> PositiveZOrder { get; } = new();

    /// <summary>All direct child layers, in no particular order.</summary>
    public IEnumerable<PaintLayer> Children
    {
        get
        {
            foreach (var c in NegativeZOrder) yield return c;
            foreach (var c in NormalFlow) yield return c;
            foreach (var c in PositiveZOrder) yield return c;
        }
    }

    public PaintLayer(Element element, LayoutBox layoutBox, ComputedStyle style)
    {
        Element = element;
        LayoutBox = layoutBox;
        Style = style;
        ZIndex = style.ZIndex ?? 0;
        Order = style.Order;
        // 'order' only reorders children of a flex/grid container. An
        // out-of-flow child of a flex container paints as if order were 0
        // (CSS Display §order-modified-document-order).
        var parentDisplay = element.ParentElement?.ComputedStyle?.Display;
        ParticipatesInOrderedLayout =
            parentDisplay is DisplayType.Flex or DisplayType.InlineFlex
                or DisplayType.Grid or DisplayType.InlineGrid
            && style.Position is not (PositionType.Absolute or PositionType.Fixed);
        if (!ParticipatesInOrderedLayout)
            Order = 0;
        Opacity = style.Opacity;
        IsTransparent = Opacity < 1.0f;
        IsPositioned = style.Position != PositionType.Static;
        IsFloating = style.Float != FloatType.None;

        var overflow = style.Overflow;
        var overflowX = style.OverflowX;
        var overflowY = style.OverflowY;
        // 'clip' clips without being a scroll container (CSS Overflow 3 §3.3), so it
        // belongs here and NOT in ScrollsOverflowAxis*.
        HasClip = overflow == OverflowType.Clip ||
                  overflowX == OverflowType.Clip ||
                  overflowY == OverflowType.Clip ||
                  overflow == OverflowType.Hidden ||
                  overflowX == OverflowType.Hidden ||
                  overflowY == OverflowType.Hidden ||
                  overflowX == OverflowType.Scroll ||
                  overflowY == OverflowType.Scroll ||
                  overflowX == OverflowType.Auto ||
                  overflowY == OverflowType.Auto ||
                  // CSS Containment 3 §2.4: paint containment clips to the border box.
                  style.HasPaintContainment;

        if (HasClip)
            ClipRect = style.HasPaintContainment && !PaintLayerClipper.CreatesOverflowClip(style)
                ? layoutBox.BorderBox
                : layoutBox.PaddingBox;

        IsStackingContext = CreatesStackingContext(style);
        IsSelfPainting = IsStackingContext || IsPositioned || HasClip || IsFloating;
    }

    /// <summary>Determines whether this element creates a stacking context, per CSS spec.</summary>
    private static bool CreatesStackingContext(ComputedStyle style)
    {
        // Positioned with z-index
        if (style.Position is PositionType.Absolute or PositionType.Fixed or PositionType.Relative or PositionType.Sticky)
            if (style.ZIndex.HasValue)
                return true;

        // Fixed position always creates stacking context
        if (style.Position == PositionType.Fixed)
            return true;

        // Opacity < 1
        if (style.Opacity < 1.0f)
            return true;

        // Transform
        if (style.HasAnyTransform)
            return true;

        // Will-change
        if (style.WillChange != null && style.WillChange != "auto")
            return true;

        // Filter / backdrop-filter
        if (style.Filter != null && style.Filter != "none")
            return true;
        if (style.BackdropFilter != null && style.BackdropFilter != "none")
            return true;

        // Mix-blend-mode
        if (style.MixBlendMode != MixBlendModeType.Normal)
            return true;

        // Isolation
        if (style.Isolation == IsolationType.Isolate)
            return true;

        // Containment: layout and paint each make the box a stacking context, so its
        // subtree can no longer interleave with later siblings; 'size' and 'style' do not
        // (measured: a z-index:99 descendant of a 'contain: size' box still paints over a
        // later positioned sibling, and of a 'contain: style' box as well).
        if (style.CreatesContainmentContext)
            return true;

        // Display: contents does not create stacking context
        if (style.Display == DisplayType.Contents)
            return false;

        // Root element
        if (style.Display == DisplayType.Block && style.Position == PositionType.Static && style.ZIndex == null)
            return false;

        return false;
    }

    public void AddChild(PaintLayer child)
    {
        child.Parent = this;
        if (child.ZIndex < 0)
            NegativeZOrder.Add(child);
        else if (child.ZIndex > 0)
            PositiveZOrder.Add(child);
        else
            NormalFlow.Add(child);
    }

    /// <summary>
    /// Stable-sort a sibling list by the CSS 'order' property. Mirrors
    /// GetOrderSortedChildren()/OrderLessThan() in paint_layer_stacking_node.cc:
    /// only siblings that participate in an ordered (flex/grid) container are
    /// reordered, and the sort is stable so document order breaks ties.
    /// </summary>
    internal static IEnumerable<PaintLayer> SortByOrder(List<PaintLayer> layers)
    {
        if (layers.Count < 2)
            return layers;
        bool anyOrdered = false;
        foreach (var l in layers)
        {
            if (l.ParticipatesInOrderedLayout && l.Order != 0) { anyOrdered = true; break; }
        }
        if (!anyOrdered)
            return layers;
        // LINQ OrderBy is a stable sort, matching std::stable_sort.
        return layers.OrderBy(l => l.Order);
    }

    /// <summary>Returns children in CSS paint order (negative → auto → positive, each ascending).</summary>
    public IEnumerable<PaintLayer> ChildrenInPaintOrder =>
        SortByOrder(NegativeZOrder).OrderBy(l => l.ZIndex)
            .Concat(FlowThenPositioned(SortByOrder(NormalFlow)))
            .Concat(SortByOrder(PositiveZOrder).OrderBy(l => l.ZIndex));

    /// <summary>
    /// CSS 2.1 §E.2: inside the z:auto group the in-flow, non-positioned, non-float
    /// boxes (step 3) paint first, the floats (step 4) on top of them, and the
    /// positioned descendants (step 8) last.
    /// </summary>
    internal static IEnumerable<PaintLayer> FlowThenPositioned(IEnumerable<PaintLayer> layers)
    {
        var list = layers as IReadOnlyList<PaintLayer> ?? layers.ToList();
        for (int i = 0; i < list.Count; i++)
            if (!list[i].IsPositioned && !list[i].IsFloating)
                yield return list[i];
        for (int i = 0; i < list.Count; i++)
            if (!list[i].IsPositioned && list[i].IsFloating)
                yield return list[i];
        for (int i = 0; i < list.Count; i++)
            if (list[i].IsPositioned)
                yield return list[i];
    }
}

/// <summary>
/// Builds the paint layer tree following CSS stacking context rules.
/// Mirrors the engine's PaintLayerTreeBuilder.
/// </summary>
public class PaintLayerTree
{
    private PaintLayer? _rootLayer;
    private readonly List<PaintLayer> _allLayers = new();

    public PaintLayer? RootLayer => _rootLayer;

    public void Build(Document document)
    {
        _allLayers.Clear();
        var root = document.DocumentElement ?? document.Body;
        if (root == null || root.LayoutBox == null || root.ComputedStyle == null) return;

        _rootLayer = new PaintLayer(root, root.LayoutBox, root.ComputedStyle);
        _allLayers.Add(_rootLayer);

        BuildChildren(root, _rootLayer);
    }

    private void BuildChildren(Element parent, PaintLayer parentLayer)
    {
        foreach (var child in parent.Children)
        {
            if (child is not Element childElement) continue;
            if (childElement.ComputedStyle == null || childElement.ComputedStyle.Display == DisplayType.None) continue;
            if (childElement.LayoutBox == null)
            {
                BuildChildren(childElement, parentLayer);
                continue;
            }

            var layer = new PaintLayer(childElement, childElement.LayoutBox, childElement.ComputedStyle);
            _allLayers.Add(layer);

            // A child of a non-stacking-context parent joins the parent's list
            if (!parentLayer.IsStackingContext)
            {
                // Reparent to the nearest stacking-context ancestor
                var stackingAncestor = FindNearestStackingContext(parentLayer);
                if (stackingAncestor != null)
                    stackingAncestor.AddChild(layer);
                else
                    parentLayer.AddChild(layer);
            }
            else
            {
                parentLayer.AddChild(layer);
            }

            BuildChildren(childElement, layer);
        }
    }

    private static PaintLayer? FindNearestStackingContext(PaintLayer layer)
    {
        var current = layer;
        while (current != null)
        {
            if (current.IsStackingContext)
                return current;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>Traverses layers in the correct CSS paint order.</summary>
    public List<PaintLayer> GetPaintOrder()
    {
        var order = new List<PaintLayer>();
        if (_rootLayer == null) return order;
        CollectPaintOrder(_rootLayer, order);
        return order;
    }

    private void CollectPaintOrder(PaintLayer layer, List<PaintLayer> order)
    {
        // 1. Negative z-index children
        foreach (var child in PaintLayer.SortByOrder(layer.NegativeZOrder).OrderBy(c => c.ZIndex))
            CollectPaintOrder(child, order);

        // 2. The layer itself (background, borders, content)
        order.Add(layer);

        // 3. Normal flow children (block backgrounds, floats, inline, auto-z);
        // in-flow boxes first, positioned z:auto boxes last (§E.2 step 8).
        foreach (var child in PaintLayer.FlowThenPositioned(PaintLayer.SortByOrder(layer.NormalFlow)))
            CollectPaintOrder(child, order);

        // 4. Positive z-index children
        foreach (var child in PaintLayer.SortByOrder(layer.PositiveZOrder).OrderBy(c => c.ZIndex))
            CollectPaintOrder(child, order);
    }

    public PaintLayer? FindLayerAtPoint(float x, float y)
    {
        var paintOrder = GetPaintOrder();
        for (int i = paintOrder.Count - 1; i >= 0; i--)
        {
            var layer = paintOrder[i];
            if (layer.LayoutBox.BorderBox.Contains(x, y))
                return layer;
        }
        return null;
    }

    public void Clear()
    {
        _allLayers.Clear();
        _rootLayer = null;
    }
}

/// <summary>
/// PaintPropertyTree - manages transform, clip, and effect trees for compositing.
/// Mirrors the engine's PaintPropertyTreeBuilder output.
/// </summary>
public class PaintPropertyTree
{
    private readonly List<TransformNode> _transformNodes = new();
    private readonly List<ClipNode> _clipNodes = new();
    private readonly List<EffectNode> _effectNodes = new();

    public TransformNode RootTransform { get; }
    public ClipNode RootClip { get; }
    public EffectNode RootEffect { get; }

    public PaintPropertyTree()
    {
        RootTransform = new TransformNode { Id = 0, ParentId = -1 };
        RootClip = new ClipNode { Id = 0, ParentId = -1, TransformId = 0 };
        RootEffect = new EffectNode { Id = 0, ParentId = -1, TransformId = 0, ClipId = 0 };

        _transformNodes.Add(RootTransform);
        _clipNodes.Add(RootClip);
        _effectNodes.Add(RootEffect);
    }

    public int AddTransform(SKMatrix matrix, int parentId = 0)
    {
        var node = new TransformNode { Id = _transformNodes.Count, ParentId = parentId, Matrix = matrix };
        _transformNodes.Add(node);
        return node.Id;
    }

    public int AddClip(SKRect rect, int transformId = 0, int parentId = 0)
    {
        var node = new ClipNode { Id = _clipNodes.Count, ParentId = parentId, TransformId = transformId, ClipRect = rect };
        _clipNodes.Add(node);
        return node.Id;
    }

    public int AddEffect(float opacity, int transformId = 0, int clipId = 0, int parentId = 0)
    {
        var node = new EffectNode { Id = _effectNodes.Count, ParentId = parentId, TransformId = transformId, ClipId = clipId, Opacity = opacity };
        _effectNodes.Add(node);
        return node.Id;
    }

    public TransformNode? GetTransform(int id) => _transformNodes.FirstOrDefault(n => n.Id == id);
    public ClipNode? GetClip(int id) => _clipNodes.FirstOrDefault(n => n.Id == id);
    public EffectNode? GetEffect(int id) => _effectNodes.FirstOrDefault(n => n.Id == id);

    public void Clear()
    {
        _transformNodes.Clear(); _clipNodes.Clear(); _effectNodes.Clear();
        _transformNodes.Add(RootTransform); _clipNodes.Add(RootClip); _effectNodes.Add(RootEffect);
    }
}

public class TransformNode
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public SKMatrix Matrix { get; set; } = SKMatrix.Identity;
}

public class ClipNode
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public int TransformId { get; set; }
    public SKRect ClipRect { get; set; }
}

public class EffectNode
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public int TransformId { get; set; }
    public int ClipId { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public SKColorFilter? ColorFilter { get; set; }
}
