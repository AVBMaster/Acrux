using Acrux.Core.Dom;
using SkiaSharp;

namespace Acrux.Core.Layout;

/// <summary>
/// The geometry of a box that has no box. An inline element is laid out as one or more fragments
/// inside the line boxes of the block that contains it, and CSSOM View §4 reports exactly those:
/// <c>getClientRects()</c> lists them in document order and <c>getBoundingClientRect()</c> is their
/// union. Each fragment is the font's content area plus the element's own padding and border
/// (CSS 2.1 §10.6.1) — the same rectangle the paint pass fills with the element's background, so a
/// page that measures a run of text and a page that shows it cannot disagree.
/// </summary>
public static class InlineGeometry
{
    /// <summary>The fragments of |element|, in document order. Empty for an element that is not
    /// laid out inline (it has a box of its own) or that is not laid out at all.</summary>
    public static List<SKRect> GetFragments(Element element)
    {
        var result = new List<SKRect>();
        if (element == null) return result;
        var container = ContainingBlockOf(element);
        if (container == null) return result;
        Collect(container, element, result);
        result.Sort((a, b) => a.Top != b.Top
            ? a.Top.CompareTo(b.Top)
            : a.Left.CompareTo(b.Left));
        if (result.Count == 0 && element.ComputedStyle is { Display: not DisplayType.None })
        {
            // An inline that shows nothing still answers with one empty rect at the content edge of
            // the box that holds it (measured: an empty span and a space-only span each report a
            // single 0×0 rect there). An element that is not laid out at all reports none.
            result.Add(new SKRect(container.ContentBox.Left, container.ContentBox.Top,
                container.ContentBox.Left, container.ContentBox.Top));
        }
        return result;
    }

    /// <summary>The union of <see cref="GetFragments"/>, or false when the element shows no
    /// inline fragment at all.</summary>
    public static bool TryGetBoundingRect(Element element, out SKRect rect)
    {
        rect = SKRect.Empty;
        var fragments = GetFragments(element);
        if (fragments.Count == 0) return false;
        var union = fragments[0];
        for (int i = 1; i < fragments.Count; i++) union = SKRect.Union(union, fragments[i]);
        rect = union;
        return union.Width > 0 || union.Height > 0;
    }

    /// <summary>The nearest box that is laid out around the element. An inline element has no box
    /// of its own, so its fragments live in the line boxes of the closest laid-out ancestor, and
    /// whatever has scrolled that ancestor has scrolled them with it.</summary>
    public static LayoutBox? ContainingBlockOf(Element element)
    {
        for (var parent = element.ParentElement; parent != null; parent = parent.ParentElement)
            if (parent.LayoutBox != null) return parent.LayoutBox;
        return null;
    }

    private static void Collect(LayoutBox box, Element element, List<SKRect> result)
    {
        // A child box is walked when it is anonymous — the block a line of text was wrapped in is
        // where the fragments of an inline child actually live — and left alone when it belongs to
        // the element being measured or to one of its descendants: an inline-block or a replaced
        // element is one run of its own below, and its contents are its business (CSSOM View 4).
        if (box.Children != null)
        {
            foreach (var child in box.Children)
            {
                if (child == null) continue;
                var childElement = child.Dimensions?.Element;
                if (childElement != null && IsOwnedBy(childElement, element)) continue;
                Collect(child, element, result);
            }
        }
        //
        // 'LineRuns' is a flattened copy of the text runs in 'Lines', so only one of the two is
        // read — walking both would count every fragment twice.
        if (box.Lines is { Count: > 0 })
        {
            foreach (var line in box.Lines)
                CollectLine(line, element, result);
            return;
        }
        if (box.LineRuns != null)
            CollectRuns(box.LineRuns, element, result);
    }

    /// <summary>
    /// One fragment per inline box per line: the reference engine breaks a wrapped element at the
    /// line, not at every word, and breaks a line wherever a nested inline box starts or ends
    /// (measured: a span whose text wraps twice gives two rects, while a span holding 'a', a child
    /// span and 'd' on one line gives three).
    /// </summary>
    private static void CollectLine(LineBox line, Element element, List<SKRect> result)
    {
        if (line.Runs == null) return;
        CollectRuns(line.Runs, element, result);
    }

    private static void CollectRuns(List<InlineRun> runs, Element element, List<SKRect> result)
    {
        Element? groupOwner = null;
        var group = new List<SKRect>();
        var groupIsSpace = new List<bool>();
        for (int i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var owner = OwnerOf(run);
            if (owner == null || !IsOwnedBy(owner, element))
            {
                Flush(group, groupIsSpace, result);
                group.Clear(); groupIsSpace.Clear(); groupOwner = null;
                continue;
            }
            if (groupOwner != null && !ReferenceEquals(groupOwner, owner))
            {
                Flush(group, groupIsSpace, result);
                group.Clear(); groupIsSpace.Clear();
            }
            groupOwner = owner;
            var rect = FragmentRect(run, owner);
            if (rect == null) continue;
            group.Add(rect.Value);
            groupIsSpace.Add(run.IsText && IsAllWhitespace(run.Text));
        }
        Flush(group, groupIsSpace, result);
    }

    /// <summary>The white space that ends a soft-wrapped line is not part of the text the line
    /// shows, so it does not widen the fragment (measured: a wrapped run that breaks after a space
    /// reports the width of its glyphs, not of the glyphs plus the space).</summary>
    private static void Flush(List<SKRect> group, List<bool> groupIsSpace, List<SKRect> result)
    {
        if (group.Count == 0) return;
        int last = group.Count - 1;
        while (last > 0 && groupIsSpace[last]) last--;
        var union = group[0];
        for (int i = 1; i <= last; i++) union = SKRect.Union(union, group[i]);
        result.Add(union);
        group.Clear(); groupIsSpace.Clear();
    }

    private static bool IsAllWhitespace(string? text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        foreach (var c in text)
            if (!char.IsWhiteSpace(c)) return false;
        return true;
    }

    /// <summary>The element a run paints for: a text run belongs to the element holding the text
    /// node, an atomic run to the element it stands for.</summary>
    private static Element? OwnerOf(InlineRun run)
    {
        if (run == null) return null;
        return run.IsText
            ? (run.Node as TextNode)?.ParentElement ?? run.Node as Element
            : run.Node as Element;
    }

    /// <summary>The rectangle of one run: the font's content area around the run's baseline, plus
    /// the owner's own padding and border (CSS 2.1 §10.6.1) — the box its background paints into.</summary>
    private static SKRect? FragmentRect(InlineRun run, Element owner)
    {
        var style = owner.ComputedStyle;
        if (style == null) return null;
        if (!run.IsText)
        {
            var atomicBox = owner.LayoutBox;
            if (atomicBox != null) return atomicBox.BorderBox;
            return run.Width > 0 && run.Height > 0
                ? new SKRect(run.X, run.Baseline, run.X + run.Width, run.Baseline + run.Height)
                : null;
        }
        if (run.Width <= 0) return null;

        var metrics = Fonts.LineBoxMetrics.GetFontMetrics(style);
        float padL = Edge(style.PaddingLeft, style), padR = Edge(style.PaddingRight, style);
        float padT = Edge(style.PaddingTop, style), padB = Edge(style.PaddingBottom, style);
        return new SKRect(
            run.X - padL - style.BorderLeftWidth,
            run.Baseline - metrics.FloatAscent - padT - style.BorderTopWidth,
            run.X + run.Width + padR + style.BorderRightWidth,
            run.Baseline + metrics.FloatDescent + padB + style.BorderBottomWidth);
    }

    /// <summary>Whether |owner| is the element whose fragments are being collected, or one of its
    /// descendants — a nested inline contributes to its ancestor's rectangle.</summary>
    private static bool IsOwnedBy(Node owner, Element element)
    {
        for (var node = owner; node != null; node = node.Parent)
            if (ReferenceEquals(node, element)) return true;
        return false;
    }

    /// <summary>An edge of an inline box resolved against its own font. A percentage of an inline
    /// box resolves against nothing this engine can measure, and a negative edge is no edge.</summary>
    private static float Edge(Length? length, ComputedStyle style)
    {
        if (length == null) return 0;
        float px = length.ToPixels(style.FontSize, style.FontSize, 0, 0);
        return float.IsNaN(px) || px < 0 ? 0 : px;
    }
}
