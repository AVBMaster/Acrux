using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

/// <summary>
/// Transliteration of view_painter.cc plus the background-propagation rules from
/// StyleResolver::PropagateStyleToViewport (style_resolver.cc) and
/// LayoutBoxModelObject::BackgroundTransfersToView (layout_box_model_object.cc).
///
/// The engine paints the "canvas" background (the rectangle behind the root
/// element) itself rather than letting the html/body elements paint it on their
/// own boxes:
///   1. The base background color (LocalFrameView::BaseBackgroundColor, default
///      white) fills the viewport. In Acrux this is the SkiaRenderer's
///      canvas.Clear(...).
///   2. The root element's background propagates to the canvas: when the html
///      element has no background, the first body element's background is used
///      instead. Propagated layers always use border-box as the clip box and
///      scroll attachment behaves as local.
///   3. When a background transfers to the view, the element itself must not
///      paint its own background layers.
/// </summary>
public static class ViewPainter
{
    /// <summary>
    /// Color::Blend (platform/graphics/color.cc) — alpha-composites <paramref name="source"/>
    /// over <paramref name="baseColor"/>. Used to blend the base background color
    /// with the propagated root element background color.
    /// </summary>
    public static SKColor Blend(SKColor baseColor, SKColor source)
    {
        if (baseColor.Alpha == 0 || source.Alpha == 255)
            return source;
        if (source.Alpha == 0)
            return baseColor;

        int sourceAlpha = source.Alpha;
        int alpha = baseColor.Alpha;

        int d = 255 * (alpha + sourceAlpha) - alpha * sourceAlpha;
        int a = d / 255;
        int r = (baseColor.Red * alpha * (255 - sourceAlpha) + 255 * sourceAlpha * source.Red) / d;
        int g = (baseColor.Green * alpha * (255 - sourceAlpha) + 255 * sourceAlpha * source.Green) / d;
        int b = (baseColor.Blue * alpha * (255 - sourceAlpha) + 255 * sourceAlpha * source.Blue) / d;
        return new SKColor((byte)r, (byte)g, (byte)b, (byte)a);
    }

    /// <summary>
    /// ComputedStyle::HasBackground() — true when there is any background color or
    /// background image layer.
    /// </summary>
    public static bool HasBackground(ComputedStyle style)
    {
        bool hasColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasImage = style.BackgroundImage is { Count: > 0 } && style.BackgroundImage!.Any(s => s != "none");
        return hasColor || hasImage;
    }

    /// <summary>
    /// LayoutBoxModelObject::BackgroundTransfersToView() (layout_box_model_object.cc:869).
    /// When true, the element's background is painted by the ViewPainter (canvas)
    /// instead of by the element's own box.
    /// </summary>
    public static bool BackgroundTransfersToView(Element element, Document? document)
    {
        if (document == null)
            return false;

        // The document element's background always paints on the view.
        if (element == document.DocumentElement)
            return true;

        // Only the first body element can transfer, and only when the document
        // element is <html> and has no background of its own.
        if (element.TagName.Equals("BODY", StringComparison.OrdinalIgnoreCase))
        {
            var documentElement = document.DocumentElement;
            if (documentElement == null || !documentElement.TagName.Equals("HTML", StringComparison.OrdinalIgnoreCase))
                return false;
            var documentElementStyle = documentElement.ComputedStyle;
            if (documentElementStyle == null || HasBackground(documentElementStyle))
                return false;
            if (element != document.Body)
                return false;
            // Containment (ShouldApplyAnyContainment) is not modeled in Acrux.
            return true;
        }

        return false;
    }

    /// <summary>
    /// StyleResolver::PropagateStyleToViewport (style_resolver.cc:2797) background
    /// half. Returns the style whose background should paint the canvas: the html
    /// element's, or the first body element's when html has no background.
    /// </summary>
    public static ComputedStyle? BackgroundStyleForCanvas(Document document)
    {
        var documentElement = document.DocumentElement;
        if (documentElement == null)
            return null;

        var documentElementStyle = documentElement.ComputedStyle;
        if (documentElementStyle == null)
            return null;

        if (HasBackground(documentElementStyle))
            return documentElementStyle;

        var body = document.Body;
        if (body != null && elementTransfersToCanvas(body, document))
            return body.ComputedStyle;

        return documentElementStyle;
    }

    private static bool elementTransfersToCanvas(Element element, Document document)
    {
        var documentElement = document.DocumentElement;
        if (documentElement == null)
            return false;
        var documentElementStyle = documentElement.ComputedStyle;
        if (documentElementStyle == null)
            return false;
        if (HasBackground(documentElementStyle))
            return false;
        if (element != document.Body)
            return false;
        return true;
    }
}
