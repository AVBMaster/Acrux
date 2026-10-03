using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Converts a <see cref="BoxFragment"/> (produced by BlockLayoutAlgorithm /
/// InlineLayoutAlgorithm) into the legacy <see cref="Dom.LayoutBox"/> model that
/// the painting pipeline consumes. This bridges the modern layout pipeline into
/// real rendering.
/// </summary>
public static class AuroraFragmentConverter
{
    public static Dom.LayoutBox ToLayoutBox(BoxFragment fragment, Element? element, Dom.LayoutBox? parent = null,
        bool applyRelativeOffset = true)
    {
        var box = new Dom.LayoutBox
        {
            Parent = parent,
            Dimensions = new BoxDimensions { Style = fragment.Element?.ComputedStyle, Element = fragment.Element },
            IsFloating = fragment.IsFloating,
        };

        // Offset of the parent content box for computing absolute child positions.
        float parentContentX = parent?.ContentBox.Left ?? 0;
        float parentContentY = parent?.ContentBox.Top ?? 0;

        float x = fragment.InlineOffset;
        float y = fragment.BlockOffset;
        float w = fragment.InlineSize;
        float h = fragment.BlockSize;

        float ml = fragment.MarginLeft, mt = fragment.MarginTop;
        float mr = fragment.MarginRight, mb = fragment.MarginBottom;
        float bl = fragment.BorderLeft, bt = fragment.BorderTop;
        float br = fragment.BorderRight, bb = fragment.BorderBottom;
        float pl = fragment.PaddingLeft, pt = fragment.PaddingTop;
        float pr = fragment.PaddingRight, pb = fragment.PaddingBottom;

        // The fragment's InlineOffset/BlockOffset are relative to the parent's
        // content box. After BoxFragmentBuilder.AddChild, the offset has been
        // increased by the margin (AddChild: offset += margin). Reverse that
        // so that absX/Y represent the border-box position.
        float borderBoxTop = y - mt;
        float borderBoxLeft = x - ml;
        float absX = parentContentX + borderBoxLeft;
        float absY = parentContentY + borderBoxTop;

        // Margin box sits outside the border box by the margins.
        float marginBoxLeft = absX - ml;
        float marginBoxTop = absY - mt;

        box.MarginBox = new SKRect(marginBoxLeft, marginBoxTop, marginBoxLeft + w + ml + mr, marginBoxTop + h + mt + mb);
        box.BorderBox = new SKRect(absX, absY, absX + w, absY + h);
        box.PaddingBox = new SKRect(absX + bl, absY + bt, absX + w - br, absY + h - bb);
        box.ContentBox = new SKRect(absX + bl + pl, absY + bt + pt, absX + w - br - pr, absY + h - bb - pb);

        box.LineHeight = Fonts.LineBoxMetrics.GetLineHeight(fragment.Element?.ComputedStyle);

        // Convert lines. Line positions are anchored to the box's absolute
        // border box (derived above), because a fragment's own
        // InlineOffset/BlockOffset may not include nested-container offsets
        // (e.g. flex/grid items report zero local offsets while the parent
        // carries the placement).
        if (fragment.Lines.Count > 0)
        {
            var containerStyle = fragment.Element?.ComputedStyle;
            var lines = new List<LineBox>();
            var runs = new List<InlineRun>();
            foreach (var boxLine in fragment.Lines)
            {
                float lineLeft = box.BorderBox.Left + boxLine.InlineOffset;
                float lineTop = box.BorderBox.Top + boxLine.BlockOffset;
                float lineHeight = boxLine.BlockSize > 0
                    ? boxLine.BlockSize
                    : Fonts.LineBoxMetrics.GetLineHeight(containerStyle);

                // The baseline is the strut ascent: the font's own rounded ascent
                // plus the half-leading implied by the line box height. It must
                // never be approximated as a fraction of the font size, because
                // the real ascent varies by font from roughly 0.89em to 1.07em.
                float baselineOffset = boxLine.BaselineOffset > 0
                    ? boxLine.BaselineOffset - boxLine.BlockOffset
                    : Fonts.LineBoxMetrics.GetBaselineForLineHeight(containerStyle, lineHeight);

                var line = new LineBox
                {
                    X = lineLeft,
                    Y = lineTop,
                    Width = boxLine.InlineSize,
                    Height = lineHeight,
                    Baseline = lineTop + baselineOffset,
                };
                foreach (var run in boxLine.Runs)
                {
                    var shift = InlineRelativeShift(run, box);
                    line.Runs.Add(new InlineRun
                    {
                        Text = run.Text ?? "",
                        X = lineLeft + run.InlineOffset + shift.Left,
                        Width = run.InlineSize,
                        Height = run.BlockSize,
                        Baseline = (run.Text != null ? line.Baseline - run.BaselineShift : run.BaselineOffset) + shift.Top,
                        IsText = run.Text != null,
                        Node = run.Node ?? run.Element,
                        FontSize = run.FontSize,
                        FontFamily = run.FontFamily,
                        FontWeight = run.FontWeight,
                        Color = run.Color,
                    });

                    // An atomic inline (inline-block / replaced) carries its own
                    // fragment. Convert it into a positioned child box so the normal
                    // element paint path draws its background, border and content.
                    // Its block offset was resolved for vertical-align in
                    // AdjustLineForAtomicInlines and is relative to the container's
                    // border box. It is converted with the line's block as its parent so
                    // that a relative offset on the atomic box finds a containing block.
                    if (run.IsAtomicInline && run.AtomicInlineBox != null && run.Element != null)
                    {
                        float atomicAbsX = lineLeft + run.InlineOffset;
                        float atomicAbsY = box.BorderBox.Top + run.BlockOffset;
                        var atomicBox = ToLayoutBox(run.AtomicInlineBox, run.Element, box, applyRelativeOffset: false);
                        var atomicShift = InlineRelativeShift(run, box);
                        TranslateBox(atomicBox,
                            atomicAbsX - atomicBox.BorderBox.Left + atomicShift.Left,
                            atomicAbsY - atomicBox.BorderBox.Top + atomicShift.Top);
                        atomicBox.Parent = box;
                        box.Children.Add(atomicBox);
                    }
                }
                lines.Add(line);

                foreach (var run in boxLine.Runs)
                {
                    if (run.Text == null) continue;
                    var shift = InlineRelativeShift(run, box);
                    runs.Add(new InlineRun
                    {
                        Text = run.Text,
                        X = lineLeft + run.InlineOffset + shift.Left,
                        Width = run.InlineSize,
                        Height = run.BlockSize,
                        Baseline = line.Baseline - run.BaselineShift + shift.Top,
                        IsText = true,
                        Node = run.Node ?? run.Element,
                    });
                }
            }
            box.Lines = lines;
            box.LineRuns = runs.Count > 0 ? runs : null;
        }

        // Convert children recursively.
        foreach (var child in fragment.Children)
        {
            var childBox = ToLayoutBox(child, child.Element, box);
            box.Children.Add(childBox);
        }

        // Scroll-container state. The legacy path computed these in
        // CreateLayoutBox; the conversion here must do the same or overflow
        // containers never get scrollbars / wheel routing.
        var elStyle = fragment.Element?.ComputedStyle;
        if (elStyle != null && IsScrollableOverflow(elStyle))
        {
            float bottom = 0, right = 0;
            foreach (var l in fragment.Lines)
            {
                bottom = Math.Max(bottom, l.BlockEnd);
                right = Math.Max(right, l.InlineOffset + l.InlineSize);
            }
            foreach (var c in fragment.Children)
            {
                bottom = Math.Max(bottom, c.BlockOffset + c.BlockSize);
                right = Math.Max(right, c.InlineOffset + c.InlineSize);
            }

            float padTop = box.ContentBox.Top - box.BorderBox.Top;
            float padLeft = box.ContentBox.Left - box.BorderBox.Left;
            box.IsScrollContainer = true;
            box.ScrollContentHeight = Math.Max(box.ContentBox.Height, bottom - padTop);
            box.ScrollContentWidth = Math.Max(box.ContentBox.Width, right - padLeft);

            // Classic-scrollbar placeholder: shrink the visible content area by
            // the bar thickness so scroll ranges, clipping and hit-testing treat
            // the bar strip as outside the viewport. The vertical bar's inline
            // reservation already happened during layout
            // (BlockLayoutAlgorithm.MaybeRelayoutForScrollbarSpace); this shrink
            // defines the scrollport and adds the horizontal-bar strut.
            float barThickness = ScrollbarMetrics.ThicknessFor(elStyle);
            if (barThickness > 0)
            {
                bool forceV = elStyle.OverflowY == OverflowType.Scroll || elStyle.Overflow == OverflowType.Scroll;
                bool forceH = elStyle.OverflowX == OverflowType.Scroll || elStyle.Overflow == OverflowType.Scroll;
                bool needV = forceV || box.ScrollContentHeight > box.ContentBox.Height + 0.5f;
                bool needH = forceH || box.ScrollContentWidth > box.ContentBox.Width + 0.5f;
                if (needV)
                {
                    box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top,
                        Math.Max(box.ContentBox.Left + 1, box.ContentBox.Right - barThickness),
                        box.ContentBox.Bottom);
                }
                if (needH)
                {
                    box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top,
                        box.ContentBox.Right,
                        Math.Max(box.ContentBox.Top + 1, box.ContentBox.Bottom - barThickness));
                }
            }
        }

        // A5: export resolved multicol geometry for column-rule painting.
        if (fragment.IsMultiColumn && fragment.UsedColumnCount > 1)
        {
            box.IsMultiColumn = true;
            box.ColumnCount = fragment.UsedColumnCount;
            box.ColumnWidth = fragment.ColumnInlineSize;
            box.ColumnGapSize = fragment.ColumnProgression - fragment.ColumnInlineSize;
        }
        // Relative positioning displaces the finished box and everything anchored to it
        // from the static position the flow gave it; the flow itself is untouched, so the
        // parent's line box and the following siblings keep their geometry (CSS 2.1 §9.4).
        if (applyRelativeOffset)
            ApplyRelativeOffset(box, fragment.Element?.ComputedStyle, parent);
        return box;
    }

    private static void ApplyRelativeOffset(Dom.LayoutBox box, ComputedStyle? style, Dom.LayoutBox? parent)
    {
        if (style is null || style.Position != PositionType.Relative)
            return;

        var (inlineBasis, blockBasis, rootFontSize, viewport) = RelativeContainingBlock(parent, box);
        var offset = RelativeUtils.Offset(style, inlineBasis, blockBasis, rootFontSize, viewport.Width, viewport.Height);
        if (offset.Left != 0 || offset.Top != 0)
            TranslateBox(box, offset.Left, offset.Top);
    }

    /// <summary>The box whose padding block sizes the percentage insets of a relative
    /// offset: the nearest positioned ancestor, or the initial containing block when the
    /// chain has none (CSS 2.1 §10.5).</summary>
    private static (float InlineBasis, float BlockBasis, float RootFontSize, PhysicalSize Viewport)
        RelativeContainingBlock(Dom.LayoutBox? parent, Dom.LayoutBox? self)
    {
        Dom.LayoutBox? root = parent ?? self;
        while (root?.Parent != null)
            root = root.Parent;

        for (var node = parent; node != null; node = node.Parent)
        {
            var nodeStyle = node.Dimensions?.Style;
            if (nodeStyle is { Position: PositionType.Relative or PositionType.Absolute or PositionType.Fixed })
                return (node.PaddingBox.Width, node.PaddingBox.Height,
                    root?.Dimensions?.Style?.FontSize ?? 16,
                    new PhysicalSize(root?.BorderBox.Width ?? 0, root?.BorderBox.Height ?? 0));
        }

        // No positioned ancestor: the initial containing block, proxied by the root box.
        float width = root?.BorderBox.Width ?? self?.BorderBox.Width ?? 0;
        float height = root?.BorderBox.Height ?? self?.BorderBox.Height ?? 0;
        return (width, height, root?.Dimensions?.Style?.FontSize ?? 16, new PhysicalSize(width, height));
    }

    private static bool IsScrollableOverflow(ComputedStyle style) =>
        style.OverflowY is OverflowType.Auto or OverflowType.Scroll
        || style.OverflowX is OverflowType.Auto or OverflowType.Scroll
        || style.Overflow is OverflowType.Auto or OverflowType.Scroll;

    /// <summary>Relative displacement of an inline element's own fragments. Only the run
    /// moves: the line box and its neighbours keep their static geometry (CSS 2.1 §9.4),
    /// and a shifted run can no longer borrow the line's baseline.</summary>
    private static PhysicalOffset InlineRelativeShift(BoxRun run, Dom.LayoutBox containerBox)
    {
        var element = run.Element ?? (run.Node as Element) ?? (run.Node as TextNode)?.ParentElement;
        var style = element?.ComputedStyle;
        if (style is null || style.Position != PositionType.Relative)
            return PhysicalOffset.Zero;

        // An inline box contributes no containing block of its own, so its percentages
        // resolve against the containing block of its parent block chain.
        var (inlineBasis, blockBasis, rootFontSize, viewport) = RelativeContainingBlock(containerBox.Parent, containerBox);
        return RelativeUtils.Offset(style, inlineBasis, blockBasis, rootFontSize, viewport.Width, viewport.Height);
    }

    /// <summary>
    /// Shift a box subtree by (dx, dy). Atomic inline fragments are laid out at
    /// their own (0,0)-based origin, so after conversion they must be moved to
    /// their computed position on the line.
    /// </summary>
    private static void TranslateBox(Dom.LayoutBox box, float dx, float dy)
    {
        if (dx == 0 && dy == 0) return;

        box.MarginBox = Offset(box.MarginBox, dx, dy);
        box.BorderBox = Offset(box.BorderBox, dx, dy);
        box.PaddingBox = Offset(box.PaddingBox, dx, dy);
        box.ContentBox = Offset(box.ContentBox, dx, dy);

        if (box.Lines != null)
        {
            foreach (var line in box.Lines)
            {
                line.X += dx;
                line.Y += dy;
                line.Baseline += dy;
                foreach (var run in line.Runs)
                {
                    run.X += dx;
                    // 0 is the "use the line box's baseline" sentinel; only a real
                    // absolute baseline may be shifted.
                    if (run.Baseline != 0) run.Baseline += dy;
                }
            }
        }
        if (box.LineRuns != null)
        {
            foreach (var run in box.LineRuns)
            {
                run.X += dx;
                if (run.Baseline != 0) run.Baseline += dy;
            }
        }

        foreach (var child in box.Children)
            TranslateBox(child, dx, dy);
    }

    private static SKRect Offset(SKRect r, float dx, float dy) =>
        new(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);
}