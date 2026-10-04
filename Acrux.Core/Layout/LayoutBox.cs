using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// AuroraBox - full CSS box model layout object. This is the engine's
/// <c>LayoutBox</c> equivalent: it stores the CSS border-box rect
/// (frame_location_ / frame_size_) and exposes the nested boxes (border /
/// padding / content), overflow, scrolling and clipping helpers.
/// </summary>
/// <remarks>
/// This class is named <c>AuroraBox</c> instead of <c>LayoutBox</c> to avoid
/// a name conflict with <c>Acrux.Core.Dom.LayoutBox</c>, the simple data
/// container used for the DOM-facing layout results. See docs/layout_porting_tracker.md.
/// </remarks>
public abstract class AuroraBox : LayoutBoxModelObject
{
    private PhysicalOffset _frameLocation;
    private PhysicalSize _frameSize;
    private float _minPreferredLogicalWidth;
    private float _maxPreferredLogicalWidth;
    private bool _hasIntrinsicLogicalWidths;
    private bool _isScrollContainer;
    private OverflowModel? _overflow;
    private readonly List<LayoutResult> _layoutResults = new();

    protected AuroraBox(Node? node,
        PhysicalSize? intrinsicSize = null,
        LayoutInline? inlineBoxWrapper = null)
        : base(node)
    {
        if (intrinsicSize.HasValue)
            _intrinsicContentSize = intrinsicSize.Value;
        inlineBoxWrapper_ = inlineBoxWrapper;
    }

    private PhysicalSize _intrinsicContentSize;
    private LayoutInline? inlineBoxWrapper_;

    // ===== Fragmentation / layout results =====

    /// <summary>Number of physical fragments produced by the last layout.</summary>
    public uint PhysicalFragmentCount => (uint)_layoutResults.Count;

    public BoxFragment GetPhysicalFragment(uint index) => _layoutResults[(int)index].Fragment;

    public LayoutResult GetLayoutResult(uint sequenceNumber) => _layoutResults[(int)sequenceNumber];

    public System.Collections.Generic.IEnumerable<LayoutResult> GetLayoutResults() => _layoutResults;

    public void AppendLayoutResult(LayoutResult result) => _layoutResults.Add(result);

    /// <summary>Keep only the first |count| results. Mirrors ShrinkLayoutResults().</summary>
    public void ShrinkLayoutResults(uint count)
    {
        if (_layoutResults.Count > (int)count)
            _layoutResults.RemoveRange((int)count, _layoutResults.Count - (int)count);
    }

    public void FinalizeLayoutResults() { }

    public void InvalidateItems(LayoutResult result) { }

    // ===== Type checks =====

    public override bool IsBox => true;
    public override bool IsReplaced => false;
    public override bool IsScrollContainer => _isScrollContainer;
    public override bool CreatesNewFormattingContext => true;
    public override bool CanHaveChildren => true;
    public override string GetName() => "AuroraBox";

    public override bool IsFragmentLessBox => PhysicalFragmentCount == 0;

    // ===== Size / location =====

    public PhysicalOffset FrameLocation => _frameLocation;
    public PhysicalSize FrameSize => _frameSize;
    public PhysicalRect FrameRect => new(_frameLocation, _frameSize);

    public void SetLocation(PhysicalOffset location) => _frameLocation = location;
    public void SetSize(PhysicalSize size)
    {
        if (!Equals(size, _frameSize))
            SizeChanged();
        _frameSize = size;
    }

    public void SetRect(PhysicalRect rect)
    {
        _frameLocation = rect.Offset;
        _frameSize = rect.Size;
        SizeChanged();
    }

    public PhysicalSize Size() => _frameSize;
    public void SetLocation(float left, float top) => SetLocation(new PhysicalOffset(left, top));
    public void SetSize(float width, float height) => SetSize(new PhysicalSize(width, height));

    public AuroraBox? LocationContainer() => Parent as AuroraBox;

    public float Width => _frameSize.Width;
    public float Height => _frameSize.Height;
    public float X => _frameLocation.Left;
    public float Y => _frameLocation.Top;
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public float LogicalLeft() => IsHorizontalWritingMode ? X : Y;
    public float LogicalTop() => IsHorizontalWritingMode ? Y : X;
    public float LogicalWidth() => IsHorizontalWritingMode ? Width : Height;
    public float LogicalHeight() => IsHorizontalWritingMode ? Height : Width;
    public float LogicalRight() => LogicalLeft() + LogicalWidth();
    public float LogicalBottom() => LogicalTop() + LogicalHeight();

    public void SizeChanged()
    {
        _hasIntrinsicLogicalWidths = false;
        ClearNeedsLayout();
    }

    // ===== Sibling / parent access =====

    public AuroraBox? FirstChildBox() => SlowFirstChild() as AuroraBox;
    public AuroraBox? LastChildBox() => SlowLastChild() as AuroraBox;
    public AuroraBox? PreviousSiblingBox() => PreviousSibling as AuroraBox;
    public AuroraBox? NextSiblingBox() => NextSibling as AuroraBox;
    public AuroraBox? ParentBox() => Parent as AuroraBox;

    // ===== Box model rectangles =====

    public PhysicalRect BorderBoxRect => new(_frameLocation, _frameSize);

    /// <summary>Physical border box rect in this box's own coordinate space.</summary>
    public PhysicalRect PhysicalBorderBoxRect() => new(PhysicalOffset.Zero, _frameSize);

    public PhysicalRect PhysicalPaddingBoxRect() =>
        new(ClientLeft, ClientTop, ClientWidth, ClientHeight);

    public PhysicalRect PaddingBoxRect => new(
        _frameLocation.Left + BorderLeft,
        _frameLocation.Top + BorderTop,
        Math.Max(0, _frameSize.Width - BorderLeft - BorderRight),
        Math.Max(0, _frameSize.Height - BorderTop - BorderBottom));

    public PhysicalRect PhysicalContentBoxRect() =>
        new(ContentLeft, ContentTop, ContentWidth, ContentHeight);

    public PhysicalRect ContentBoxRect => new(
        PaddingBoxRect.X + PaddingLeft,
        PaddingBoxRect.Y + PaddingTop,
        Math.Max(0, PaddingBoxRect.Width - PaddingLeft - PaddingRight),
        Math.Max(0, PaddingBoxRect.Height - PaddingTop - PaddingBottom));

    public PhysicalRect ComputedCSSContentBoxRect() =>
        new(BorderLeft + ComputedCSSPaddingLeft, BorderTop + ComputedCSSPaddingTop,
            Math.Max(0, ClientWidth - ComputedCSSPaddingLeft - ComputedCSSPaddingRight),
            Math.Max(0, ClientHeight - ComputedCSSPaddingTop - ComputedCSSPaddingBottom));

    public float ClientLeft => BorderLeft;
    public float ClientTop => BorderTop;
    public float ClientWidth => (int)(PaddingBoxRect.Width);
    public float ClientHeight => (int)(PaddingBoxRect.Height);

    public float ContentLeft => ContentBoxRect.X;
    public float ContentTop => ContentBoxRect.Y;
    public float ContentWidth => ContentBoxRect.Width;
    public float ContentHeight => ContentBoxRect.Height;
    public float ContentLogicalWidth => IsHorizontalWritingMode ? ContentWidth : ContentHeight;
    public float ContentLogicalHeight => IsHorizontalWritingMode ? ContentHeight : ContentWidth;
    public PhysicalSize ContentSize => new(ContentWidth, ContentHeight);

    /// <summary>Available width for children in this box.</summary>
    public float AvailableLogicalWidth => ContentLogicalWidth;

    // ===== Intrinsic sizing =====

    public float MinPreferredLogicalWidth
    {
        get { if (!_hasIntrinsicLogicalWidths) ComputeIntrinsicLogicalWidths(); return _minPreferredLogicalWidth; }
        set { _minPreferredLogicalWidth = value; _hasIntrinsicLogicalWidths = true; }
    }

    public float MaxPreferredLogicalWidth
    {
        get { if (!_hasIntrinsicLogicalWidths) ComputeIntrinsicLogicalWidths(); return _maxPreferredLogicalWidth; }
        set { _maxPreferredLogicalWidth = value; _hasIntrinsicLogicalWidths = true; }
    }

    public void SetIntrinsicLogicalWidths(float minWidth, float maxWidth)
    {
        _minPreferredLogicalWidth = minWidth;
        _maxPreferredLogicalWidth = maxWidth;
        _hasIntrinsicLogicalWidths = true;
    }

    public void ClearIntrinsicLogicalWidths()
    {
        _hasIntrinsicLogicalWidths = false;
        ClearIntrinsicLogicalWidthsDirty();
    }

    protected virtual void ComputeIntrinsicLogicalWidths() { }

    // ===== Overflow =====

    public OverflowModel? Overflow => _overflow;
    public void EnsureOverflow() => _overflow ??= new OverflowModel();

    public bool HasVisualOverflow() => _overflow != null;
    public bool HasScrollableOverflow() => _overflow != null;

    public PhysicalRect VisualOverflowRect() =>
        HasVisualOverflow() ? _overflow!.VisualOverflow : PhysicalBorderBoxRect();

    public PhysicalRect ScrollableOverflowRect() =>
        HasScrollableOverflow() ? _overflow!.ScrollableOverflow : NoOverflowRect();

    public PhysicalRect NoOverflowRect() => PhysicalPaddingBoxRect();

    public void SetVisualOverflow(PhysicalRect self, PhysicalRect contents)
    {
        EnsureOverflow();
        _overflow!.InkOverflow.SetBoth(self, contents, _frameSize);
        _overflow.SetVisualOverflow(self.Union(contents));
    }

    public void AddSelfVisualOverflow(PhysicalRect r)
    {
        EnsureOverflow();
        _overflow.InkOverflow.SetSelf(_overflow.VisualOverflow.Union(r), _frameSize);
        _overflow.SetVisualOverflow(_overflow.VisualOverflow.Union(r));
    }

    public void AddContentsVisualOverflow(PhysicalRect r)
    {
        EnsureOverflow();
        _overflow.InkOverflow.SetContents(_overflow.VisualOverflow.Union(r), _frameSize);
        _overflow.SetVisualOverflow(_overflow.VisualOverflow.Union(r));
    }

    public void ClearVisualOverflow() => _overflow?.ResetIfOnlyInk();

    public void SetScrollableOverflow(PhysicalRect overflow)
    {
        EnsureOverflow();
        _overflow.SetScrollableOverflow(overflow);
    }

    public override void RecalcScrollableOverflow()
    {
        base.RecalcScrollableOverflow();
        ClearNeedsOverflowRecalc();
    }

    // ===== Scrolling =====

    public void SetIsScrollContainer(bool v) => _isScrollContainer = v;

    public virtual float ScrollWidth() => HasScrollableOverflow() ? Math.Max(ClientWidth, _overflow!.ScrollableOverflow.Right) : ClientWidth;
    public virtual float ScrollHeight() => HasScrollableOverflow() ? Math.Max(ClientHeight, _overflow!.ScrollableOverflow.Bottom) : ClientHeight;

    public bool HasScrollableOverflowX() => ScrollsOverflowX() && ScrollWidth() != ClientWidth;
    public bool HasScrollableOverflowY() => ScrollsOverflowY() && ScrollHeight() != ClientHeight;
    public bool ScrollsOverflowX() => HasNonVisibleOverflow && ScrollsOverflowAxisX();
    public bool ScrollsOverflowY() => HasNonVisibleOverflow && ScrollsOverflowAxisY();

    private bool ScrollsOverflowAxisX() =>
        StyleRef().OverflowX is OverflowType.Scroll or OverflowType.Auto or OverflowType.Hidden;
    private bool ScrollsOverflowAxisY() =>
        StyleRef().OverflowY is OverflowType.Scroll or OverflowType.Auto or OverflowType.Hidden;

    // ===== Clipping =====

    /// <summary>The intersection of all overflow clips which apply to this box.</summary>
    public PhysicalRect OverflowClipRect(PhysicalOffset location)
    {
        var rect = new PhysicalRect(location.Left + BorderLeft, location.Top + BorderTop,
            Math.Max(0, ClientWidth), Math.Max(0, ClientHeight));
        return rect;
    }

    public PhysicalRect ClipRect(PhysicalOffset location) => OverflowClipRect(location);

    /// <summary>The combination of overflow clip, contain: paint clip and CSS clip.</summary>
    public PhysicalRect ClippingRect(PhysicalOffset location) => OverflowClipRect(location);

    /// <summary>Whether the box clips overflow along either/both axes.</summary>
    public bool HasControlClip() => ShouldClipOverflowAlongBothAxis();

    // ===== Formatting contexts =====

    /// <summary>True if this box establishes a fragmentation context root (e.g. a multicol container).</summary>
    public virtual bool IsFragmentationContextRoot() => false;

    /// <summary>True if this box is monolithic (unbreakable) in fragmentation contexts.</summary>
    public override bool IsMonolithic => false;

    public override bool RespectsCSSOverflow() => true;

    // ===== Coordinate helpers =====

    public PhysicalOffset PhysicalLocation()
    {
        var container = LocationContainer();
        if (container == null || !container.HasFlippedBlocksWritingMode())
            return _frameLocation;
        return new PhysicalOffset(container.Width - Width - X, Y);
    }

    public override PhysicalOffset OffsetFromContainer(LayoutObject? container, int mode = 0)
    {
        if (container == null)
            return PhysicalOffset.Zero;
        return _frameLocation;
    }

    public bool HasFlippedBlocksWritingMode() => false;

    public override void UpdateAfterLayout() { }

    /// <summary>Compatibility alias kept for the legacy callers.</summary>
    public new void ClearNeedsLayout() => NeedsLayout = false;
}

/// <summary>Helper extensions kept for parity with the multi-column code.</summary>
public static class AuroraBoxExtensions
{
    public static void ResetIfOnlyInk(this OverflowModel overflow)
    {
        overflow.InkOverflow.Reset();
        overflow.SetVisualOverflow(PhysicalRect.Zero);
        overflow.SetScrollableOverflow(PhysicalRect.Zero);
    }
}