using SkiaSharp;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Paint;

/// <summary>
/// Represents the data for a particular fragment of a LayoutObject.
/// Mirrors FragmentData in blink/renderer/core/paint/fragment_data.h, with the
/// property-tree-node references dropped (this engine paints directly to a
/// DisplayList). Keeps the paint offset, a lazy unique id, the fragment id,
/// sticky-position constraints and the cull rects used to limit painting.
/// </summary>
public sealed class FragmentData
{
    private static long _nextUniqueId = 1;

    private PhysicalOffset _paintOffset;
    private long _uniqueId;
    private bool _hasUniqueId;
    private int _fragmentId;
    private FragmentDataRareData? _rareData;

    /// <summary>
    /// Physical offset of this fragment's local border box's top-left position
    /// from the origin of the (virtual) transform node of the fragment's paint
    /// coordinate space.
    /// </summary>
    public PhysicalOffset PaintOffset
    {
        get => _paintOffset;
        set => _paintOffset = value;
    }

    /// <summary>An id unique for the lifetime of the WebView.</summary>
    public long UniqueId
    {
        get
        {
            EnsureId();
            return _uniqueId;
        }
    }

    public bool HasUniqueId => _hasUniqueId;

    /// <summary>The PaintLayer associated with this LayoutBoxModelObject, if any.</summary>
    public object? Layer
    {
        get => _rareData?.Layer;
        set => EnsureRareData().Layer = value;
    }

    /// <summary>Sticky-position scrolling constraints of this fragment, if any.</summary>
    public object? StickyConstraints
    {
        get => _rareData?.StickyConstraints;
        set
        {
            if (_rareData == null && value == null)
                return;
            EnsureRareData().StickyConstraints = value;
        }
    }

    /// <summary>A fragment id unique within the LayoutObject (the fragmentainer index).</summary>
    public int FragmentID
    {
        get => _rareData?.FragmentId ?? 0;
        set
        {
            if (_rareData == null && value == 0)
                return;
            EnsureRareData().FragmentId = value;
        }
    }

    /// <summary>
    /// The cull rect: visible area used to limit painting of this fragment's
    /// content. Null means infinite. Mirrors FragmentData::cull_rect_.
    /// </summary>
    public SKRect? CullRect
    {
        get => _rareData?.CullRect;
        set
        {
            if (_rareData == null && value == null)
                return;
            EnsureRareData().CullRect = value;
        }
    }

    /// <summary>
    /// The contents cull rect: visible area for descendant content painting.
    /// Mirrors FragmentData::contents_cull_rect_.
    /// </summary>
    public SKRect? ContentsCullRect
    {
        get => _rareData?.ContentsCullRect;
        set
        {
            if (_rareData == null && value == null)
                return;
            EnsureRareData().ContentsCullRect = value;
        }
    }

    public void EnsureId()
    {
        if (!_hasUniqueId)
        {
            _uniqueId = System.Threading.Interlocked.Increment(ref _nextUniqueId);
            _hasUniqueId = true;
        }
    }

    private FragmentDataRareData EnsureRareData()
    {
        return _rareData ??= new FragmentDataRareData();
    }

    private sealed class FragmentDataRareData
    {
        public object? Layer;
        public object? StickyConstraints;
        public int FragmentId;
        public SKRect? CullRect;
        public SKRect? ContentsCullRect;
    }
}