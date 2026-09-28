using UpBrowser.Core.Dom;

namespace UpBrowser.PageHost;

/// <summary>
/// Document hit-testing against laid-out box geometry. Both the shell (in-process
/// tabs) and a page host answer pointer events with this, so a click resolves to the
/// same element whichever side owns the document.
/// </summary>
public static class PageHitTest
{
    /// <summary>Topmost element whose border box contains (<paramref name="x"/>,<paramref name="y"/>),
    /// document coordinates, ties broken by z-index.</summary>
    public static Element? HitTest(Document doc, float x, float y)
    {
        Element? result = null;
        float lastZ = float.MinValue;

        HitTestElement(doc.DocumentElement, x, y, ref result, ref lastZ);
        if (result == null) HitTestElement(doc.Body, x, y, ref result, ref lastZ);
        return result;
    }

    private static void HitTestElement(Element? element, float x, float y,
        ref Element? result, ref float lastZ)
    {
        if (element == null) return;

        var box = element.LayoutBox;
        if (box != null && box.BorderBox.Contains(x, y))
        {
            float z = element.ComputedStyle?.ZIndex ?? 0;
            if (result == null || z >= lastZ)
            {
                result = element;
                lastZ = z;
            }
        }

        foreach (var child in element.Children.OfType<Element>())
            HitTestElement(child, x, y, ref result, ref lastZ);
    }
}
