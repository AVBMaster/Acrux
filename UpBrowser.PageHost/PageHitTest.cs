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

        // Inline elements have no box of their own — their geometry lives in the lines of
        // the block that lays them out — so a pointer over a link resolves to that link's
        // container unless the run underneath it is consulted. Inline <a> is the common
        // shape of the web, and a click that never reaches it navigates nothing.
        return InlineOwner(result, x, y) ?? result;
    }

    /// <summary>The element owning the inline fragment under the point, searching the
    /// subtree of the block the point landed in. Deepest wins, as for boxes.</summary>
    private static Element? InlineOwner(Element? block, float x, float y)
    {
        if (block == null) return null;

        Element? found = null;
        foreach (var child in block.Children.OfType<Element>())
            found = InlineOwner(child, x, y) ?? found;

        if (found == null && block.LayoutBox is { } box)
        {
            var owner = RunOwnerAt(box, x, y);
            // A run's text node belongs to the element that holds it; an atomic inline
            // (image, replaced element) names its own element.
            var inline = owner as Element ?? owner?.ParentElement;
            if (inline != null && !ReferenceEquals(inline, block)) found = inline;
        }
        return found;
    }

    private static Node? RunOwnerAt(LayoutBox box, float x, float y)
    {
        if (box.Lines is { Count: > 0 })
        {
            foreach (var line in box.Lines)
            {
                if (y < line.Y || y > line.Y + line.Height) continue;
                float runX = box.ContentBox.Left + line.TextAlignOffsetX;
                foreach (var run in line.Runs)
                {
                    if (run.Node != null && x >= runX && x <= runX + run.Width) return run.Node;
                    runX += run.Width;
                }
            }
        }
        if (box.LineRuns is { Count: > 0 })
        {
            float runX = box.ContentBox.Left, height = 0;
            foreach (var run in box.LineRuns) height = Math.Max(height, run.Height);
            if (height <= 0) height = box.ContentBox.Height;
            if (y >= box.ContentBox.Top && y <= box.ContentBox.Top + height)
                foreach (var run in box.LineRuns)
                {
                    if (run.Node != null && x >= runX && x <= runX + run.Width) return run.Node;
                    runX += run.Width;
                }
        }
        return null;
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
