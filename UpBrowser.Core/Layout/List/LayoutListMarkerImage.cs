using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.List;

/// <summary>
/// A LayoutObject subclass for list marker images.
/// Mirrors layout_list_marker_image.h/.cc.
/// </summary>
public class LayoutListMarkerImage : LayoutImage
{
    public LayoutListMarkerImage(Element? element) : base(element)
    {
    }

    public override string GetName() => "LayoutListMarkerImage";
    public override bool IsListMarkerImage => true;
    public override bool IsLayoutNGObject => true;

    public static LayoutListMarkerImage CreateAnonymous()
    {
        return new LayoutListMarkerImage(null);
    }

    public PhysicalSize DefaultSize()
    {
        var style = Style;
        if (style == null) return new PhysicalSize(16, 16);

        float ascent = Fonts.LineBoxMetrics.GetFontMetrics(style).LayoutAscent;
        float bulletWidth = ascent / 2f;
        return new PhysicalSize(bulletWidth, bulletWidth);
    }

    public void ComputeIntrinsicSizingInfoByDefaultSize(IntrinsicSizingInfo info)
    {
        var concreteSize = ConcreteObjectSize(ImageResource, Style?.Zoom ?? 1f, DefaultSize());
        var pixelRatio = 1f;

        concreteSize = new PhysicalSize(concreteSize.Width * pixelRatio, concreteSize.Height * pixelRatio);

        info.Size = concreteSize;
        info.HasWidth = true;
        info.HasHeight = true;
    }

    public void ComputeIntrinsicSizingInfo(IntrinsicSizingInfo info)
    {
        if (info.Size.Width <= 0 || info.Size.Height <= 0)
        {
            ComputeIntrinsicSizingInfoByDefaultSize(info);
        }
    }

    private static PhysicalSize ConcreteObjectSize(Element? imageResource, float zoom, PhysicalSize defaultSize)
    {
        if (imageResource == null)
            return defaultSize;

        float w = imageResource.IntrinsicWidth > 0 ? imageResource.IntrinsicWidth : defaultSize.Width;
        float h = imageResource.IntrinsicHeight > 0 ? imageResource.IntrinsicHeight : defaultSize.Height;
        return new PhysicalSize(w * zoom, h * zoom);
    }
}

/// <summary>
/// Intrinsic sizing info for replaced elements.
/// Mirrors IntrinsicSizingInfo in intrinsic_sizing_info.h.
/// </summary>
public class IntrinsicSizingInfo
{
    public PhysicalSize Size { get; set; } = PhysicalSize.Zero;
    public bool HasWidth { get; set; }
    public bool HasHeight { get; set; }
    public float AspectRatio => Size.Height > 0 ? Size.Width / Size.Height : 0;
}