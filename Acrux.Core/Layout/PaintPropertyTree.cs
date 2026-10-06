using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// Engine-standard paint property tree. Maintains separate transform, clip, effect,
/// and scroll trees that describe how an element is composited. Mirrors the
/// paint property tree builder output.
/// </summary>
public class PaintPropertyTree
{
    public TransformNode RootTransform { get; } = new();
    public ClipNode RootClip { get; } = new();
    public EffectNode RootEffect { get; } = new();
    public ScrollNode RootScroll { get; } = new();

    public void Reset()
    {
        RootTransform.Children.Clear();
        RootClip.Children.Clear();
        RootEffect.Children.Clear();
        RootScroll.Children.Clear();
        RootTransform.State = TransformNodeState.Identity;
        RootClip.State = new ClipNodeState();
        RootEffect.State = new EffectNodeState { Opacity = 1 };
    }
}

public class TransformNode
{
    public TransformNodeState State { get; set; } = TransformNodeState.Identity;
    public TransformNode? Parent { get; set; }
    public List<TransformNode> Children { get; } = new();
}

public class TransformNodeState
{
    public SKMatrix Matrix { get; set; } = SKMatrix.Identity;
    public SKPoint PaintOffset { get; set; }
    public bool IsIdentity => Matrix.IsIdentity && PaintOffset.IsEmpty;

    public static TransformNodeState Identity => new();
}

public class ClipNode
{
    public ClipNodeState State { get; set; } = new();
    public ClipNode? Parent { get; set; }
    public List<ClipNode> Children { get; } = new();
}

public class ClipNodeState
{
    public SKRect? ClipRect { get; set; }
    public bool IsIdentity => !ClipRect.HasValue;
    public SKRect? ClipRectInLocalSpace { get; set; }
}

public class EffectNode
{
    public EffectNodeState State { get; set; } = new();
    public EffectNode? Parent { get; set; }
    public List<EffectNode> Children { get; } = new();
}

public class EffectNodeState
{
    public float Opacity { get; set; } = 1;
    public string? Filter { get; set; }
    public string? BackdropFilter { get; set; }
    public string? ClipPath { get; set; }
    public bool IsIdentity => Opacity == 1 && Filter == null && BackdropFilter == null && ClipPath == null;
}

public class ScrollNode
{
    public ScrollNodeState State { get; set; } = new();
    public ScrollNode? Parent { get; set; }
    public List<ScrollNode> Children { get; } = new();
}

public class ScrollNodeState
{
    public SKRect ContainerRect { get; set; }
    public SKRect ContentsRect { get; set; }
    public float ScrollX { get; set; }
    public float ScrollY { get; set; }
}

/// <summary>
/// Builds the paint property tree for an element, mirroring the engine's fragment paint property tree builder.
/// Computes transform, clip, effect, and scroll nodes based on the element's computed style.
/// </summary>
public class PaintPropertyTreeBuilder
{
    private readonly PaintPropertyTree _tree;
    private readonly TransformNode _transformParent;
    private readonly ClipNode _clipParent;
    private readonly EffectNode _effectParent;
    private readonly ScrollNode _scrollParent;

    public PaintPropertyTreeBuilder(PaintPropertyTree tree,
        TransformNode? transformParent = null,
        ClipNode? clipParent = null,
        EffectNode? effectParent = null,
        ScrollNode? scrollParent = null)
    {
        _tree = tree;
        _transformParent = transformParent ?? tree.RootTransform;
        _clipParent = clipParent ?? tree.RootClip;
        _effectParent = effectParent ?? tree.RootEffect;
        _scrollParent = scrollParent ?? tree.RootScroll;
    }

    public (TransformNode? transform, ClipNode? clip, EffectNode? effect, ScrollNode? scroll) UpdateForSelf(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) return (null, null, null, null);

        TransformNode? transformNode = null;
        ClipNode? clipNode = null;
        EffectNode? effectNode = null;
        ScrollNode? scrollNode = null;

        // Transform
        if (NeedsTransform(style))
        {
            transformNode = new TransformNode
            {
                Parent = _transformParent,
                State = new TransformNodeState { Matrix = ParseTransform(style.Transform) }
            };
            _transformParent.Children.Add(transformNode);
        }

        // Clip
        if (NeedsClip(style))
        {
            clipNode = new ClipNode
            {
                Parent = _clipParent,
                State = new ClipNodeState
                {
                    ClipRect = ClipRectFor(element.LayoutBox, style),
                    ClipRectInLocalSpace = ClipRectFor(element.LayoutBox, style)
                }
            };
            _clipParent.Children.Add(clipNode);
        }

        // Effect (opacity, filter, clip-path)
        if (NeedsEffect(style))
        {
            effectNode = new EffectNode
            {
                Parent = _effectParent,
                State = new EffectNodeState
                {
                    Opacity = style.Opacity,
                    Filter = style.Filter,
                    BackdropFilter = style.BackdropFilter,
                    ClipPath = style.ClipPath
                }
            };
            _effectParent.Children.Add(effectNode);
        }

        // Scroll
        if (NeedsScroll(style))
        {
            scrollNode = new ScrollNode
            {
                Parent = _scrollParent,
                State = new ScrollNodeState
                {
                    ContainerRect = element.LayoutBox?.PaddingBox ?? new SKRect(),
                    ContentsRect = element.LayoutBox?.ContentBox ?? new SKRect()
                }
            };
            _scrollParent.Children.Add(scrollNode);
        }

        return (transformNode, clipNode, effectNode, scrollNode);
    }

    private static bool NeedsTransform(ComputedStyle style) =>
        style.HasAnyTransform;

    private static bool NeedsClip(ComputedStyle style)
    {
        var overflowX = style.OverflowX;
        var overflowY = style.OverflowY;
        return overflowX == OverflowType.Clip || overflowY == OverflowType.Clip ||
               overflowX == OverflowType.Hidden || overflowY == OverflowType.Hidden ||
               overflowX == OverflowType.Scroll || overflowY == OverflowType.Scroll ||
               overflowX == OverflowType.Auto || overflowY == OverflowType.Auto ||
               // CSS Containment 3 §2.4: paint containment is its own clip boundary.
               style.HasPaintContainment;
    }

    /// <summary>The box paint containment and an overflow clip fall on: containment clips
    /// the border box, an overflow clip the (smaller) padding box.</summary>
    private static SKRect? ClipRectFor(LayoutBox? box, ComputedStyle style)
    {
        if (box == null) return null;
        bool overflowClips = style.OverflowX != OverflowType.Visible || style.OverflowY != OverflowType.Visible
            || style.Overflow != OverflowType.Visible;
        return overflowClips ? box.PaddingBox : box.BorderBox;
    }

    private static bool NeedsEffect(ComputedStyle style) =>
        style.Opacity < 1.0f ||
        (!string.IsNullOrEmpty(style.Filter) && style.Filter != "none") ||
        (!string.IsNullOrEmpty(style.BackdropFilter) && style.BackdropFilter != "none") ||
        !string.IsNullOrEmpty(style.ClipPath);

    private static bool NeedsScroll(ComputedStyle style)
    {
        var overflowX = style.OverflowX;
        var overflowY = style.OverflowY;
        return overflowX == OverflowType.Scroll || overflowY == OverflowType.Scroll ||
               overflowX == OverflowType.Auto || overflowY == OverflowType.Auto;
    }

    private static SKMatrix ParseTransform(string? transform)
    {
        if (string.IsNullOrEmpty(transform) || transform == "none")
            return SKMatrix.Identity;

        try
        {
            var operations = Acrux.Core.Css.TransformParser.Parse(transform);
            if (operations == null || operations.Count == 0)
                return SKMatrix.Identity;
            return Acrux.Core.Css.TransformParser.ToMatrix(operations, 0, 0);
        }
        catch
        {
            return SKMatrix.Identity;
        }
    }
}

/// <summary>
/// Walks the layout tree and builds paint property trees, mirroring the engine's pre-paint tree walk.
/// </summary>
public class PrePaintTreeWalk
{
    private readonly PaintPropertyTree _tree = new();
    public PaintPropertyTree Tree => _tree;

    public void Walk(Element root)
    {
        _tree.Reset();
        WalkElement(root, null, null, null, null);
    }

    private void WalkElement(Element element,
        TransformNode? transformParent,
        ClipNode? clipParent,
        EffectNode? effectParent,
        ScrollNode? scrollParent)
    {
        var builder = new PaintPropertyTreeBuilder(_tree, transformParent, clipParent, effectParent, scrollParent);
        var (transform, clip, effect, scroll) = builder.UpdateForSelf(element);

        foreach (var child in element.Children)
        {
            if (child is Element childEl)
                WalkElement(childEl, transform ?? transformParent, clip ?? clipParent, effect ?? effectParent, scroll ?? scrollParent);
        }
    }
}