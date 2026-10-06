using Acrux.Core.Dom;

namespace Acrux.PageHost;

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
            var inline = Origin(owner as Element ?? owner?.ParentElement);
            // The run is only a target if its own element takes pointer events: text in a
            // 'pointer-events: none' span falls through to the block that owns the line,
            // exactly as the reference engine resolves it to the containing box.
            if (inline != null && !ReferenceEquals(inline, block) &&
                IsHittable(inline, inline.LayoutBox ?? box))
                found = inline;
        }
        return found;
    }

    /// <summary>Which run of a line owns the point. The run's own <c>X</c> is the recorded
    /// fact — it already carries the line's justification spread, the text-align offset and
    /// any relative shift — so re-adding widths from the content edge (as this method used to)
    /// put every run after the first one at the wrong place on a justified line.</summary>
    private static Node? RunOwnerAt(LayoutBox box, float x, float y)
    {
        if (box.Lines is { Count: > 0 })
        {
            foreach (var line in box.Lines)
            {
                if (y < line.Y || y > line.Y + line.Height) continue;
                foreach (var run in line.Runs)
                {
                    if (run.Node != null && x >= run.X && x <= run.X + run.Width) return run.Node;
                }
            }
        }
        if (box.LineRuns is { Count: > 0 })
        {
            float height = 0;
            foreach (var run in box.LineRuns) height = Math.Max(height, run.Height);
            if (height <= 0) height = box.ContentBox.Height;
            if (y >= box.ContentBox.Top && y <= box.ContentBox.Top + height)
                foreach (var run in box.LineRuns)
                {
                    if (run.Node != null && x >= run.X && x <= run.X + run.Width) return run.Node;
                }
        }
        return null;
    }


    private static void HitTestElement(Element? element, float x, float y,
        ref Element? result, ref float lastZ)
    {
        if (element == null) return;

        var box = element.LayoutBox;
        // CSS UI 4 §11: 'pointer-events: none' takes this box out of the hit set, but the
        // walk still descends — a descendant may set the property back to 'auto' and become
        // the target of the very pointer that passes through its parent.
        if (box != null && IsHittable(element, box) && box.BorderBox.Contains(x, y))
        {
            // A generated box (::before / ::after / a floated ::first-letter) has no node to
            // receive the event, so the pointer is attributed to the element that declared it.
            var target = Origin(element) ?? element;
            if (ReferenceEquals(target, element) || IsHittable(target, box))
            {
                float z = target.ComputedStyle?.ZIndex ?? 0;
                if (result == null || z >= lastZ)
                {
                    result = target;
                    lastZ = z;
                }
            }
        }

        foreach (var child in element.Children.OfType<Element>())
            HitTestElement(child, x, y, ref result, ref lastZ);
    }

    /// <summary>The element a box belongs to: itself, or the originating element of a
    /// generated box (walking out through nested pseudo-elements).</summary>
    private static Element? Origin(Element? element)
    {
        while (element is { IsGeneratedPseudoElement: true })
            element = element.ParentElement;
        return element;
    }

    /// <summary>Whether a point over this box can land on it. Three independent reasons
    /// remove a box, all measured against the reference engine (b223 §A/§C/§G):
    /// <c>pointer-events: none</c> on the element itself (the SVG keyword family behaves
    /// like <c>auto</c> on HTML, and <c>all</c> does not rescue anything), an inherited or
    /// authored <c>visibility</c> other than <c>visible</c>, and contents skipped by an
    /// ancestor's <c>content-visibility: hidden</c> — which renders the box's own
    /// decoration but nothing below it. Opacity is deliberately absent: a fully transparent
    /// layer still takes pointer events. The walk keeps descending either way, so a
    /// descendant that restores <c>visibility</c> or sets <c>pointer-events: auto</c> is
    /// reachable again.</summary>
    private static bool IsHittable(Element element, LayoutBox box)
    {
        if (box.InHiddenSubtree) return false;
        if (string.Equals(element.ComputedStyle?.PointerEvents?.Trim(), "none",
                StringComparison.OrdinalIgnoreCase)) return false;
        return element.ComputedStyle?.Visibility == VisibilityType.Visible;
    }
}
