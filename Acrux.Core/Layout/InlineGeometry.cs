using Acrux.Core.Dom;
using SkiaSharp;

namespace Acrux.Core.Layout;

/// <summary>
/// The geometry of a box that has no box. An inline element is laid out as one or more fragments
/// inside the line boxes of the block that contains it, and CSSOM View §4 reports exactly those:
/// <c>getClientRects()</c> lists them in document order and <c>getBoundingClientRect()</c> is their
/// union. Each fragment is the font's content area beside the element's own padding and border
/// (CSS 2.1 §10.6.1) — the same rectangle the paint pass fills with the element's background, so a
/// page that measures a run of text and a page that shows it cannot disagree.
/// <para>
/// "Beside" is only half of it. <c>box-decoration-break</c> (CSS Fragmentation 3 §4.2) decides what
/// a broken box keeps at the break: the initial value <c>slice</c> gives the inline-start edges to
/// the first fragment, the inline-end edges to the last one, and neither to the fragments in
/// between — as if the box had been cut with scissors — while <c>clone</c> repeats the whole
/// decoration on every fragment. The block-axis edges belong to every fragment either way, because
/// the box breaks along the inline axis only. The paint pass already works this way; this is the
/// other half of the same rule, measured edge by edge (a wrapped <c>padding:0 6px</c> span gives
/// its first fragment 6+width and its last one width+6).
/// </para>
/// </summary>
public static class InlineGeometry
{
    /// <summary>One fragment before its decoration is added: the content-area rectangle beside the
    /// element that owns it.</summary>
    private readonly struct Piece
    {
        public SKRect Content { get; }
        public Element Owner { get; }

        public Piece(SKRect content, Element owner)
        {
            Content = content;
            Owner = owner;
        }
    }

    /// <summary>The fragments of |element|, in document order. Empty for an element that is not
    /// laid out inline (it has a box of its own) or that is not laid out at all.</summary>
    public static List<SKRect> GetFragments(Element element)
    {
        var result = new List<SKRect>();
        if (element == null) return result;
        var container = ContainingBlockOf(element);
        if (container == null) return result;

        var pieces = new List<Piece>();
        Collect(container, element, pieces);
        pieces.Sort((a, b) => a.Content.Top != b.Content.Top
            ? a.Content.Top.CompareTo(b.Content.Top)
            : a.Content.Left.CompareTo(b.Content.Left));
        for (int i = 0; i < pieces.Count; i++)
        {
            // Which fragment of its own box this one is, settled among the pieces of the same owner
            // rather than by position in the list: a nested inline contributes fragments that stand
            // between its parent's.
            bool first = true, last = true;
            for (int j = 0; j < pieces.Count; j++)
            {
                if (!ReferenceEquals(pieces[j].Owner, pieces[i].Owner)) continue;
                if (j < i) first = false;
                if (j > i) last = false;
            }
            result.Add(Inflate(pieces[i], first, last));
        }
        if (result.Count == 0 && element.ComputedStyle is { Display: not DisplayType.None } shown)
        {
            // An inline that shows nothing still answers with one rect at the content edge of the box
            // that holds it: no width, and the height of its own font's content area — measured, an
            // empty span standing in a line of text reports the same height as the text beside it.
            // An element that is not laid out at all reports none.
            var metrics = Fonts.LineBoxMetrics.GetFontMetrics(shown);
            float top = container.ContentBox.Top;
            result.Add(new SKRect(container.ContentBox.Left, top, container.ContentBox.Left,
                top + metrics.FloatAscent + metrics.FloatDescent));
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

    /// <summary>The fragment as it is reported: its content area beside whichever edges of the box
    /// this fragment carries (CSS Fragmentation 3 §4.2).</summary>
    private static SKRect Inflate(Piece piece, bool isFirst, bool isLast)
    {
        var content = piece.Content;
        var style = piece.Owner.ComputedStyle;
        if (style == null) return content;
        bool clone = style.BoxDecorationBreak == BoxDecorationBreakType.Clone;
        float start = clone || isFirst ? Edge(style.PaddingLeft, style) + style.BorderLeftWidth : 0f;
        float end = clone || isLast ? Edge(style.PaddingRight, style) + style.BorderRightWidth : 0f;
        float above = Edge(style.PaddingTop, style) + style.BorderTopWidth;
        float below = Edge(style.PaddingBottom, style) + style.BorderBottomWidth;
        return new SKRect(content.Left - start, content.Top - above,
            content.Right + end, content.Bottom + below);
    }

    private static void Collect(LayoutBox box, Element element, List<Piece> result)
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
    private static void CollectLine(LineBox line, Element element, List<Piece> result)
    {
        if (line.Runs == null) return;
        int first = result.Count;
        CollectRuns(line.Runs, element, result);
        // A forced break inside the element is a fragment of its own: zero width, standing where the
        // element's content on this line stopped, as tall as the element's content area here
        // (measured: 'a<br>b<br>c' gives five rects — the text of each line plus a zero-width one
        // after the text on the two lines the breaks close; the last line gets none). A break
        // outside the element adds nothing, which is why its owner travels with the line.
        if (line.ForcedBreakOwner == null || !IsOwnedBy(line.ForcedBreakOwner, element)) return;
        // Measured, and the one case where the break is not reported: as soon as the element paints
        // anything around its text — a background, a border, an outline, padding, a margin — the
        // reference engine merges the zero-width fragment into the one it follows and answers with
        // one rect fewer. The break is invisible either way; only the length of the list shows it.
        if (element.ComputedStyle is { } style && PaintsOwnDecoration(style)) return;
        float x = line.X;
        for (int i = first; i < result.Count; i++) x = MathF.Max(x, result[i].Content.Right);
        var rect = BreakRect(line, element, x);
        if (rect != null) result.Add(new Piece(rect.Value, element));
    }

    /// <summary>The content area a forced break stands for: the font's box around the line's
    /// baseline at the point the break sits.</summary>
    private static SKRect? BreakRect(LineBox line, Element element, float x)
    {
        var style = element.ComputedStyle;
        if (style == null) return null;
        var metrics = Fonts.LineBoxMetrics.GetFontMetrics(style);
        return new SKRect(x, line.Baseline - metrics.FloatAscent, x, line.Baseline + metrics.FloatDescent);
    }

    /// <summary>Whether the element draws anything around its text. Mirrors the paint pass's own
    /// test (<c>InlineBoxFragmentPainter.HasBoxDecorationBackground</c>) beside the outline and the
    /// edges that take part in the break rule.</summary>
    private static bool PaintsOwnDecoration(ComputedStyle style)
    {
        if (style.BackgroundColor is { } color && color.Alpha > 0) return true;
        if (style.BackgroundImage is { Count: > 0 } images)
            foreach (var image in images)
                if (!string.IsNullOrEmpty(image) && !image.Equals("none", StringComparison.OrdinalIgnoreCase))
                    return true;
        if (style.OutlineWidth > 0 && style.OutlineStyle != BorderStyle.None) return true;
        if (style.BorderLeftWidth > 0 || style.BorderRightWidth > 0
            || style.BorderTopWidth > 0 || style.BorderBottomWidth > 0) return true;
        return Edge(style.PaddingLeft, style) != 0 || Edge(style.PaddingRight, style) != 0
            || Edge(style.PaddingTop, style) != 0 || Edge(style.PaddingBottom, style) != 0
            || Edge(style.MarginLeft, style) != 0 || Edge(style.MarginRight, style) != 0;
    }

    private static void CollectRuns(List<InlineRun> runs, Element element, List<Piece> result)
    {
        Element? groupOwner = null;
        var group = new List<SKRect>();
        var groupIsSpace = new List<bool>();
        for (int i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var owner = OwnerOf(run);
            // An atomic run — an inline-block, an image — opens no inline box of its own in the list
            // its parent reports: the reference engine gives one fragment for
            // 'a<span display:inline-block></span>b' while a nested *inline* span splits its parent's
            // text into three (both measured). The run therefore joins the box that holds it and
            // keeps its own rectangle.
            var grouped = run.IsText ? owner : owner?.ParentElement ?? owner;
            if (grouped == null || !IsOwnedBy(grouped, element))
            {
                Flush(group, groupIsSpace, result, groupOwner);
                group.Clear(); groupIsSpace.Clear(); groupOwner = null;
                continue;
            }
            if (groupOwner != null && !ReferenceEquals(groupOwner, grouped))
            {
                Flush(group, groupIsSpace, result, groupOwner);
                group.Clear(); groupIsSpace.Clear();
            }
            groupOwner = grouped;
            var rect = FragmentRect(run, owner);
            if (rect == null) continue;
            group.Add(rect.Value);
            groupIsSpace.Add(run.IsText && IsAllWhitespace(run.Text));
        }
        Flush(group, groupIsSpace, result, groupOwner);
    }

    /// <summary>The white space that ends a soft-wrapped line is not part of the text the line
    /// shows, so it does not widen the fragment (measured: a wrapped run that breaks after a space
    /// reports the width of its glyphs, not of the glyphs plus the space).</summary>
    private static void Flush(List<SKRect> group, List<bool> groupIsSpace, List<Piece> result, Element? owner)
    {
        if (group.Count == 0 || owner == null) return;
        int last = group.Count - 1;
        while (last > 0 && groupIsSpace[last]) last--;
        var union = group[0];
        for (int i = 1; i <= last; i++) union = SKRect.Union(union, group[i]);
        result.Add(new Piece(union, owner));
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

    /// <summary>The rectangle of one run's content area: the font's ascent and descent around the
    /// run's baseline. Padding and border are added by <see cref="Inflate"/>, which is where the
    /// fragment's place in the box decides whether it carries them.</summary>
    private static SKRect? FragmentRect(InlineRun run, Element owner)
    {
        var style = owner.ComputedStyle;
        if (style == null) return null;
        if (!run.IsText)
        {
            var atomicBox = owner.LayoutBox;
            if (atomicBox != null) return WithoutEdges(atomicBox.BorderBox, style);
            return run.Width > 0 && run.Height > 0
                ? new SKRect(run.X, run.Baseline, run.X + run.Width, run.Baseline + run.Height)
                : null;
        }
        if (run.Width <= 0) return null;

        var metrics = Fonts.LineBoxMetrics.GetFontMetrics(style);
        return new SKRect(
            run.X,
            run.Baseline - metrics.FloatAscent,
            run.X + run.Width,
            run.Baseline + metrics.FloatDescent);
    }

    /// <summary>An atomic inline's own border box taken back to its content area, so the slice rule
    /// can give its edges to the right fragment. A replaced element carries no padding of its own
    /// and taking an empty box back would leave nothing, so its border box stands.</summary>
    private static SKRect WithoutEdges(SKRect borderBox, ComputedStyle style)
    {
        var content = new SKRect(
            borderBox.Left + Edge(style.PaddingLeft, style) + style.BorderLeftWidth,
            borderBox.Top + Edge(style.PaddingTop, style) + style.BorderTopWidth,
            borderBox.Right - Edge(style.PaddingRight, style) - style.BorderRightWidth,
            borderBox.Bottom - Edge(style.PaddingBottom, style) - style.BorderBottomWidth);
        return content.Width > 0 && content.Height > 0 ? content : borderBox;
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
