using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Core.Paint;

/// <summary>
/// One contiguous URL range within an anchor's concatenated text content.
/// Mirrors the reference "URLElementInfo" shape: the resolved URL plus the
/// [start, end) offsets into the anchor text and the painted rect.
/// </summary>
public readonly record struct UrlMetadataRange(SKRect Rect, int Start, int End, string Url);

/// <summary>
/// URL metadata for a single anchor: the anchor element, its resolved href and
/// the set of ranges (per painted text run) that belong to it.
/// </summary>
public sealed class UrlMetadata
{
    public required Element Anchor { get; init; }
    public required string Url { get; init; }
    public string Text { get; set; } = string.Empty;
    public List<UrlMetadataRange> Ranges { get; } = new();
}

/// <summary>
/// Collects URL metadata ranges from a laid-out box tree. The reference walker
/// (AddURLRectsForInlineChildrenRecursively) visits inline children recursively
/// and records the URL of every object that carries a link; this port does the
/// same over the engine's <see cref="LayoutBox"/> tree, grouping text runs by
/// their enclosing anchor so consumers (a11y, find-in-page) get per-anchor
/// concatenated text plus the sub-rects hit by that text.
/// </summary>
public static class UrlMetadataUtils
{
    /// <summary>
    /// Walk <paramref name="root"/> (inclusive) and collect every anchor's URL
    /// metadata. Runs belonging to the same anchor are appended to a per-anchor
    /// concatenated text buffer so offsets are stable across lines.
    /// </summary>
    public static List<UrlMetadata> Collect(LayoutBox root)
    {
        var result = new List<UrlMetadata>();
        var anchors = new Dictionary<Element, UrlMetadata>();
        Visit(root, anchors, result);
        return result;
    }

    private static void Visit(LayoutBox box, Dictionary<Element, UrlMetadata> anchors, List<UrlMetadata> order)
    {
        var el = box.Dimensions?.Element;
        UrlMetadata? activeAnchor = ResolveAnchor(box, el, anchors, order);

        if (el == null || IsSameOrDescendantOf(el, activeAnchor))
        {
            foreach (var line in box.Lines ?? Enumerable.Empty<LineBox>())
            {
                foreach (var run in line.Runs)
                    VisitRun(box, run, activeAnchor);
            }
            foreach (var run in box.LineRuns ?? Enumerable.Empty<InlineRun>())
                VisitRun(box, run, activeAnchor);
        }

        foreach (var child in box.Children)
            Visit(child, anchors, order);
    }

    private static void VisitRun(LayoutBox box, InlineRun run, UrlMetadata? anchor)
    {
        if (anchor == null) return;
        if (!run.IsText || string.IsNullOrEmpty(run.Text)) return;

        int start = anchor.Text.Length;
        anchor.Text += run.Text;
        anchor.Ranges.Add(new UrlMetadataRange(
            new SKRect(run.X, box.ContentBox.Top, run.X + run.Width, box.ContentBox.Top + run.Height),
            start, anchor.Text.Length, anchor.Url));
    }

    private static UrlMetadata? ResolveAnchor(
        LayoutBox box,
        Element? el,
        Dictionary<Element, UrlMetadata> anchors,
        List<UrlMetadata> order)
    {
        if (el == null || el.TagName != "A" || !el.HasAttribute("href"))
            return null;

        if (!anchors.TryGetValue(el, out var existing))
        {
            existing = new UrlMetadata
            {
                Anchor = el,
                Url = el.GetAttribute("href") ?? string.Empty,
                Text = string.Empty,
            };
            anchors[el] = existing;
            order.Add(existing);
        }
        return existing;
    }

    private static bool IsSameOrDescendantOf(Element? el, UrlMetadata? anchor)
        => anchor != null && el != null && IsSameOrDescendantOf(el, anchor.Anchor);

    private static bool IsSameOrDescendantOf(Element el, Element ancestor)
    {
        for (var n = el; n != null; n = n.ParentElement)
        {
            if (ReferenceEquals(n, ancestor)) return true;
        }
        return false;
    }
}