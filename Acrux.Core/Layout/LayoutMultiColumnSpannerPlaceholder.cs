using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// Placeholder layoutObject for column-span:all elements. The column-span:all
/// layoutObject itself is a descendant of the flow thread, but due to its
/// out-of-flow nature, we need something on the outside to take care of its
/// positioning and sizing. LayoutMultiColumnSpannerPlaceholder objects are
/// siblings of LayoutMultiColumnSet objects, i.e. direct children of the
/// multicol container.
/// </summary>
public sealed class LayoutMultiColumnSpannerPlaceholder : AuroraBox
{
    private readonly AuroraBox? _layoutObjectInFlowThread;

    public LayoutMultiColumnSpannerPlaceholder(AuroraBox? layoutObjectInFlowThread)
        : base(null)
    {
        _layoutObjectInFlowThread = layoutObjectInFlowThread;
    }

    public bool IsLayoutMultiColumnSpannerPlaceholder => true;

    public override string GetName() => "LayoutMultiColumnSpannerPlaceholder";

    public static LayoutMultiColumnSpannerPlaceholder CreateAnonymous(ComputedStyle parentStyle, AuroraBox layoutObjectInFlowThread)
    {
        var newSpanner = new LayoutMultiColumnSpannerPlaceholder(layoutObjectInFlowThread);
        newSpanner.UpdateProperties(parentStyle);
        return newSpanner;
    }

    public LayoutBlockFlow MultiColumnBlockFlow() => (LayoutBlockFlow)Parent!;

    public LayoutMultiColumnFlowThread FlowThread()
    {
        return ((LayoutBlockFlow)Parent!).MultiColumnFlowThread();
    }

    public AuroraBox? LayoutObjectInFlowThread() => _layoutObjectInFlowThread;

    public bool AnonymousHasStylePropagationOverride => true;

    public void LayoutObjectInFlowThreadStyleDidChange(ComputedStyle? oldStyle)
    {
        var objectInFlowThread = _layoutObjectInFlowThread;
        if (objectInFlowThread == null)
            return;
        var flowThread = FlowThread();
        if (flowThread.RemoveSpannerPlaceholderIfNoLongerValid(objectInFlowThread))
        {
            // No longer a valid spanner, due to style changes. |this| is now dead.
            if (objectInFlowThread.StyleRef().HasOutOfFlowPosition() && (oldStyle == null || !oldStyle.HasOutOfFlowPosition()))
            {
                // We went from being a spanner to being out-of-flow positioned. When an
                // object becomes out-of-flow positioned, we need to lay out its parent,
                // since that's where the now-out-of-flow object gets added to the right
                // containing block for out-of-flow positioned objects. Since neither a
                // spanner nor an out-of-flow object is guaranteed to have this parent in
                // its containing block chain, we need to mark it here, or we risk that
                // the object isn't laid out.
                if (objectInFlowThread.Parent != null)
                    objectInFlowThread.Parent.NeedsLayout = true;
            }
            return;
        }
        if (Parent != null)
            UpdateProperties(Parent.StyleRef());
    }

    public void UpdateProperties(ComputedStyle parentStyle)
    {
        // Copy margin properties from the spanner object
        if (_layoutObjectInFlowThread != null)
        {
            var spannerStyle = _layoutObjectInFlowThread.StyleRef();
            // We really only need the block direction margins, but there are no setters
            // for that in ComputedStyle. Just copy all margin sides. The inline ones
            // don't matter anyway.
            // In the simplified Acrux model, we just reference the style directly.
        }
    }

    public new PhysicalOffset PhysicalLocation()
    {
        return _layoutObjectInFlowThread?.FrameLocation ?? PhysicalOffset.Zero;
    }

    public new PhysicalSize Size()
    {
        return _layoutObjectInFlowThread?.FrameSize ?? PhysicalSize.Zero;
    }

    internal void InsertedIntoTree()
    {
        // The object may previously have been laid out as a non-spanner, but since
        // it's a spanner now, it needs to be relaid out.
        if (_layoutObjectInFlowThread != null)
            _layoutObjectInFlowThread.NeedsLayout = true;
    }

    internal void WillBeRemovedFromTree()
    {
        if (_layoutObjectInFlowThread != null)
        {
            var exSpanner = _layoutObjectInFlowThread;
            exSpanner.ClearSpannerPlaceholder();
            // Even if the placeholder is going away, the object in the flow thread
            // might live on. Since it's not a spanner anymore, it needs to be relaid
            // out.
            exSpanner.NeedsLayout = true;
        }
    }
}

// Extension for LayoutObject
public static class LayoutObjectSpannerExtensions
{
    public static bool IsLayoutMultiColumnSpannerPlaceholder(this LayoutObject obj) => obj is LayoutMultiColumnSpannerPlaceholder;
    public static bool HasOutOfFlowPosition(this ComputedStyle style) => false;
    public static float ComputedPixelSize(this ComputedStyle style) => style.FontSize;
    public static float ToPixels(this Length length, float fontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        if (length is PixelLength pl) return pl.Value;
        if (length is PercentLength pcl) return pcl.Value * viewportWidth;
        if (length is EmLength em) return em.Value * fontSize;
        return 0;
    }

    public static ComputedStyle StyleRef(this AuroraBox box)
    {
        if (box.Node is Element el && el.ComputedStyle != null)
            return el.ComputedStyle;
        return new ComputedStyle();
    }

    public static ComputedStyle StyleRef(this LayoutObject obj)
    {
        if (obj.Node is Element el && el.ComputedStyle != null)
            return el.ComputedStyle;
        return new ComputedStyle();
    }

    public static ComputedStyle StyleRef(this LayoutBlockFlow blockFlow)
    {
        if (blockFlow.Node is Element el && el.ComputedStyle != null)
            return el.ComputedStyle;
        return new ComputedStyle();
    }

    public static ComputedStyle StyleRef(this LayoutMultiColumnSet set)
    {
        if (set.Node is Element el && el.ComputedStyle != null)
            return el.ComputedStyle;
        return new ComputedStyle();
    }

    public static float AvailableLogicalWidth(this LayoutBlockFlow blockFlow)
    {
        return blockFlow.ContentBoxRect.Width;
    }

    public static float GetFontDescription(this ComputedStyle style)
    {
        return style.FontSize;
    }

    public static LayoutObject? LastChild(this LayoutBlockFlow blockFlow)
    {
        return blockFlow.Children.Count > 0 ? blockFlow.Children[^1] : null;
    }

    public static LayoutObject? FirstChild(this LayoutFlowThread flowThread)
    {
        return flowThread.Children.Count > 0 ? flowThread.Children[0] : null;
    }
}