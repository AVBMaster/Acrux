using Acrux.Core.Dom.Html;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

public class LayoutVideo : LayoutMedia
{
    private PhysicalSize _cachedImageSize;

    public LayoutVideo(HTMLVideoElement? element) : base(element)
    {
    }

    public static PhysicalSize DefaultSize() => new(300, 150);

    public PhysicalRect ReplacedContentRectFrom(PhysicalRect baseContentRect) => baseContentRect;

    public bool SupportsAcceleratedRendering() => false;

    public enum DisplayMode { Poster, Video }

    public DisplayMode GetDisplayMode() => DisplayMode.Video;

    public HTMLVideoElement? VideoElement() => Node as HTMLVideoElement;

    public override string GetName() => "LayoutVideo";

    public override void IntrinsicSizeChanged()
    {
    }

    public override OverflowClipAxes ComputeOverflowClipAxes() =>
        RespectsCSSOverflow() ? base.ComputeOverflowClipAxes() : OverflowClipAxes.BothAxis;

    public override void UpdateAfterLayout()
    {
    }

    public override void UpdateFromElement()
    {
    }

    public void InvalidateCompositing()
    {
    }

    private PhysicalSize CalculateIntrinsicSize(float scale) => DefaultSize();

    private void UpdateIntrinsicSize()
    {
    }

    public override void ImageChanged(WrappedImagePtr? image, CanDeferInvalidation defer)
    {
    }

    public override bool IsVideo => true;

    public override void PaintReplaced(PaintInfo? paintInfo, PhysicalOffset paintOffset)
    {
    }

    public override bool CanHaveAdditionalCompositingReasons => true;

    public override CompositingReasons AdditionalCompositingReasons() => CompositingReasons.None;
}