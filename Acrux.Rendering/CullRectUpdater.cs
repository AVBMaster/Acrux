using SkiaSharp;
using Acrux.Core.Paint;

namespace Acrux.Rendering;

/// <summary>
/// Updates the cull rects of PaintLayer fragments (FragmentData.CullRect and
/// FragmentData.ContentsCullRect), the visible-area optimization that limits
/// painting to what is near the viewport. Mirrors CullRectUpdater, simplified
/// to this engine's paint model: there is no paint property tree, so cull rects are
/// computed by intersecting each layer's border box with the input cull rect
/// and expanding by the given ratio.
/// </summary>
public sealed class CullRectUpdater
{
    /// <summary>Fragments produced for the layer tree, keyed by layer.</summary>
    public Dictionary<PaintLayer, FragmentData> FragmentByLayer { get; } = new();

    private readonly SKRect _inputCullRect;
    private readonly float _expansionRatio;

    public CullRectUpdater(SKRect inputCullRect, float expansionRatio = 1.25f)
    {
        _inputCullRect = inputCullRect;
        _expansionRatio = expansionRatio;
    }

    public CullRectUpdater(SKRect inputCullRect, bool disableExpansion)
        : this(inputCullRect, disableExpansion ? 0f : 1.25f)
    {
    }

    /// <summary>
    /// Walks the layer tree in paint order and assigns cull rects. Mirrors
    /// Update()/UpdateRecursively().
    /// </summary>
    public void Update(PaintLayerTree tree)
    {
        FragmentByLayer.Clear();
        var root = tree.RootLayer;
        if (root == null)
            return;
        foreach (var layer in tree.GetPaintOrder())
        {
            var fragment = layer.FragmentData ?? (layer.FragmentData = new FragmentData());
            var cull = ComputeCullRect(GetBorderBox(layer));
            fragment.CullRect = cull;
            fragment.ContentsCullRect = cull;
            FragmentByLayer[layer] = fragment;
        }
    }

    /// <summary>Assigns a single fragment's cull rects (mirrors UpdateForSelf).</summary>
    public bool UpdateForSelf(FragmentData fragment, SKRect borderBox)
    {
        var cull = ComputeCullRect(borderBox);
        bool changed = fragment.CullRect != cull;
        fragment.CullRect = cull;
        fragment.ContentsCullRect = cull;
        return changed;
    }

    private static SKRect GetBorderBox(PaintLayer layer) => layer.LayoutBox?.BorderBox ?? layer.ClipRect;

    private SKRect ComputeCullRect(SKRect itemRect)
    {
        var cull = SKRect.Intersect(_inputCullRect, itemRect);
        if (_expansionRatio > 0 && cull.Width > 0)
        {
            float inflateW = cull.Width * _expansionRatio;
            float inflateH = cull.Height * _expansionRatio;
            var inflated = cull;
            inflated.Inflate(inflateW, inflateH);
            cull = inflated;
        }
        return cull;
    }
}