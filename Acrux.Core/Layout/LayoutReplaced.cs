using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// LayoutReplaced - base class for a replaced element as defined by CSS. A
/// replaced element's content is outside the scope of the CSS formatting model
/// (e.g. an image, embedded document, applet). Mirrors layout_replaced.cc.
/// http://www.w3.org/TR/CSS2/conform.html#defs
/// </summary>
public class LayoutReplaced : AuroraBox
{
    // These values are specified to be 300 and 150 pixels in the CSS 2.1 spec.
    // http://www.w3.org/TR/CSS2/visudet.html#inline-replaced-width
    public const int DefaultWidth = 300;
    public const int DefaultHeight = 150;

    public LayoutReplaced(Node? node, PhysicalSize? intrinsicSize = null) : base(node, intrinsicSize)
    {
    }

    public override string GetName() => "LayoutReplaced";
    public override bool IsLayoutReplaced => true;
    public override bool IsReplaced => true;
    public override bool CanHaveChildren => false;
    public override bool RespectsCSSOverflow() => false;

    public override bool IsAtomicInlineLevel => true;

    // ===== Intrinsic sizing =====

    /// <summary>The natural / intrinsic size of the replaced content.</summary>
    public PhysicalSize IntrinsicSize { get; set; }

    public void SetIntrinsicSize(PhysicalSize size) => IntrinsicSize = size;

    public float IntrinsicRatio => IntrinsicSize.Height > 0 ? IntrinsicSize.Width / IntrinsicSize.Height : 0;

    public bool HasObjectFit() => StyleRef().ObjectFit != ObjectFitType.Fill;

    public bool ClipsToContentBox() => true;

    public override void ComputeIntrinsicSizingInfo(IntrinsicSizingInfo? info)
    {
        if (info == null)
            return;
        var size = IntrinsicSize;
        bool hasWidth = size.Width > 0;
        bool hasHeight = size.Height > 0;
        if (hasWidth && hasHeight)
        {
            info = new IntrinsicSizingInfo(size, size, true, true);
        }
        else if (hasWidth)
        {
            info = new IntrinsicSizingInfo(size, new PhysicalSize(size.Width, 0), true, false);
        }
        else if (hasHeight)
        {
            info = new IntrinsicSizingInfo(size, new PhysicalSize(0, size.Height), false, true);
        }
        else
        {
            info = new IntrinsicSizingInfo(PhysicalSize.Zero, new PhysicalSize(DefaultWidth, DefaultHeight), false, false);
        }
    }

    public override void IntrinsicSizeChanged()
    {
        SetNeedsLayout();
        InvalidatePaint();
    }

    // ===== Replaced content placement =====

    /// <summary>Returns the local rect of the replaced content in the border-box coordinate space.</summary>
    public PhysicalRect ReplacedContentRect() => ReplacedContentRectFrom(ContentBoxRect);

    public virtual PhysicalRect ReplacedContentRectFrom(PhysicalRect baseContentRect)
    {
        return ComputeReplacedContentRect(baseContentRect);
    }

    /// <summary>Place the intrinsic content into |baseContentRect| honoring object-fit and object-position.</summary>
    public PhysicalRect ComputeReplacedContentRect(PhysicalRect baseContentRect, PhysicalSize? overriddenIntrinsicSize = null)
    {
        var intrinsicSize = overriddenIntrinsicSize ?? IntrinsicSize;
        if (intrinsicSize.Width <= 0 || intrinsicSize.Height <= 0)
            return baseContentRect;

        var objectFit = StyleRef().ObjectFit;
        float scaleX = baseContentRect.Width / intrinsicSize.Width;
        float scaleY = baseContentRect.Height / intrinsicSize.Height;
        float scale = objectFit switch
        {
            ObjectFitType.Contain => Math.Min(scaleX, scaleY),
            ObjectFitType.Cover => Math.Max(scaleX, scaleY),
            ObjectFitType.Fill => 1f,
            _ => 1f,
        };

        var contentSize = objectFit == ObjectFitType.Fill
            ? new PhysicalSize(baseContentRect.Width, baseContentRect.Height)
            : new PhysicalSize(Math.Max(1, intrinsicSize.Width * scale), Math.Max(1, intrinsicSize.Height * scale));

        if (objectFit == ObjectFitType.None)
        {
            contentSize = new PhysicalSize(Math.Min(intrinsicSize.Width, baseContentRect.Width),
                Math.Min(intrinsicSize.Height, baseContentRect.Height));
        }

        // object-position (default: 50% 50%).
        float posX = 0.5f, posY = 0.5f;
        if (StyleRef().ObjectPositionX is Length lx)
            posX = ResolvePosition(lx, baseContentRect.Width - contentSize.Width);
        if (StyleRef().ObjectPositionY is Length ly)
            posY = ResolvePosition(ly, baseContentRect.Height - contentSize.Height);

        float x = baseContentRect.X + posX * (baseContentRect.Width - contentSize.Width);
        float y = baseContentRect.Y + posY * (baseContentRect.Height - contentSize.Height);
        return new PhysicalRect(x, y, contentSize.Width, contentSize.Height);
    }

    private static float ResolvePosition(Length length, float availableDiff)
    {
        var px = length.ToPixels(16f, 16f, 0f, 0f);
        if (length is PercentLength pct)
            return pct.Value;
        return availableDiff > 0 && px != float.NaN ? px / availableDiff : 0;
    }

    /// <summary>Snap the content rect to whole pixels for elements whose size is costly to change.</summary>
    public static PhysicalRect PreSnappedRectForPersistentSizing(PhysicalRect rect) =>
        new(MathF.Round(rect.X), MathF.Round(rect.Y), MathF.Round(rect.Width), MathF.Round(rect.Height));

    public virtual bool DrawsBackgroundOntoContentLayer() => false;

    public override void PaintReplaced(PaintInfo? paintInfo, PhysicalOffset paintOffset) { }
}

/// <summary>
/// LayoutImage - image layout object. Mirrors layout_image.cc.
/// </summary>
public class LayoutImage : LayoutReplaced
{
    public LayoutImage(Node? node) : base(node)
    {
    }

    public override string GetName() => "LayoutImage";
    public override bool IsImage => true;

    public bool IsGeneratedContent { get; set; }
    public Element? ImageResource { get; set; }

    public bool IsUnsizedImage => FrameSize.Width <= 0 || FrameSize.Height <= 0;
}