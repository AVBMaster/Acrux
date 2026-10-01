using UpBrowser.Core.Dom;
using GridImpl = UpBrowser.Core.Layout.Grid.GridLayoutAlgorithm;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Adapter exposing the existing Grid layout implementation through the LayoutAlgorithm
/// interface. Produces a BoxFragment tree from the grid container's LayoutBox tree,
/// so the container conveys its own geometry but the pane re-converts fragments
/// back into the Dom.LayoutBox model the painting pipeline consumes.
/// </summary>
public class GridLayoutAdapter : LayoutAlgorithm
{
    public GridLayoutAdapter(Element node, in ConstraintSpace space) : base(node, space)
    {
    }

    public override LayoutResult Layout()
    {
        Builder.BorderLeft = BorderLeft;
        Builder.BorderTop = BorderTop;
        Builder.BorderRight = BorderRight;
        Builder.BorderBottom = BorderBottom;
        Builder.PaddingLeft = PaddingLeft;
        Builder.PaddingTop = PaddingTop;
        Builder.PaddingRight = PaddingRight;
        Builder.PaddingBottom = PaddingBottom;
        Builder.Element = Node;

        float availableWidth = ChildAvailableInlineSize;

        // Local-geometry container: the grid algorithm positions items relative to
        // this box's content box, and the item box produced here carries the real
        // content height (row tracks + gaps) once the layout pass runs.
        // Track sizing and the auto-track stretch resolve against the grid's OWN
        // declared size: AvailableInline/BlockSize are the CONTAINING BLOCK's
        // sizes, and seeding them here stretched a definite-height grid's rows to
        // the whole viewport (873px rows spilling two screens past the box).
        // Auto width still fills the available inline size (block-level grids
        // span their containing block); auto height seeds 0 so rows size to
        // their content and nothing stretches.
        float contentWidth = ResolveOwnContentSize(Style.Width, horizontal: true, autoValue: availableWidth);
        // 'width: max-content' / 'min-content' / 'fit-content': size the tracks
        // against an indefinite inline so auto/max-content columns reach their
        // content width and fr columns keep their content base, then shrink the
        // container to the resolved track total (read back after Layout below).
        bool intrinsicWidth = Style.Width is IntrinsicLength;
        if (intrinsicWidth)
            contentWidth = float.PositiveInfinity;
        float contentHeight = ResolveOwnContentSize(Style.Height, horizontal: false, autoValue: 0f);
        var containerBox = new LayoutBox
        {
            ContentBox = new SkiaSharp.SKRect(
                BorderLeft + PaddingLeft, BorderTop + PaddingTop,
                BorderLeft + PaddingLeft + contentWidth, BorderTop + PaddingTop + contentHeight),
        };
        containerBox.PaddingBox = new SkiaSharp.SKRect(
            containerBox.ContentBox.Left - PaddingLeft, containerBox.ContentBox.Top - PaddingTop,
            containerBox.ContentBox.Right + PaddingRight, containerBox.ContentBox.Bottom + PaddingBottom);
        containerBox.BorderBox = new SkiaSharp.SKRect(
            containerBox.PaddingBox.Left - BorderLeft, containerBox.PaddingBox.Top - BorderTop,
            containerBox.PaddingBox.Right + BorderRight, containerBox.PaddingBox.Bottom + BorderBottom);
        containerBox.MarginBox = containerBox.BorderBox;

        var grid = new GridImpl(TextMeasurer.Instance, Space);
        grid.Layout(Node, containerBox, availableWidth);

        var box = new BoxFragment
        {
            InlineSize = intrinsicWidth
                ? grid.ResolvedContentInlineSize + BorderPaddingInline
                : ComputeInlineSize(),
            BlockSize = ComputeBlockSize(containerBox),
            InlineOffset = 0,
            BlockOffset = 0,
            BorderLeft = BorderLeft,
            BorderTop = BorderTop,
            BorderRight = BorderRight,
            BorderBottom = BorderBottom,
            PaddingLeft = PaddingLeft,
            PaddingTop = PaddingTop,
            PaddingRight = PaddingRight,
            PaddingBottom = PaddingBottom,
            Element = Node,
        };

        CollectChildren(containerBox, box);

        Builder.InlineSize = box.InlineSize;
        Builder.BlockSize = box.BlockSize;
        Builder.IntrinsicBlockSize = box.BlockSize;

        return LayoutResult.FromFragment(box);
    }

    /// <summary>
    /// Resolve the grid container's own declared width/height to a content-box
    /// extent. Only pixel lengths are definite here (matching
    /// <c>_containerHeightAuto</c>, which treats percent height as auto);
    /// everything else falls back to <paramref name="autoValue"/> — the
    /// available inline size for width (block-level fill), 0 for height
    /// (auto rows size to content).
    /// </summary>
    private float ResolveOwnContentSize(Length? declared, bool horizontal, float autoValue)
    {
        if (declared is not PixelLength px) return autoValue;
        float value = px.Value;
        if (Style.BoxSizing == BoxSizingType.BorderBox)
        {
            float borderPadding = horizontal
                ? BorderLeft + PaddingLeft + BorderRight + PaddingRight
                : BorderTop + PaddingTop + BorderBottom + PaddingBottom;
            value = Math.Max(0, value - borderPadding);
        }
        return Math.Max(0, value);
    }

    private static void CollectChildren(LayoutBox parent, BoxFragment target)
    {
        foreach (var child in parent.Children)
        {
            // Reconstruct a fragment whose re-conversion reproduces the child box
            // exactly: offsets are relative to the parent's content box (the
            // converter adds the parent content origin and subtracts the margins),
            // and the border/padding insets are carried so an item's own
            // border-box/padding doesn't get lost.
            float marginLeft = child.BorderBox.Left - child.MarginBox.Left;
            float marginTop = child.BorderBox.Top - child.MarginBox.Top;

            var element = child.Dimensions?.Element;
            var childFragment = new BoxFragment
            {
                InlineOffset = (child.BorderBox.Left - parent.ContentBox.Left) + marginLeft,
                BlockOffset = (child.BorderBox.Top - parent.ContentBox.Top) + marginTop,
                InlineSize = child.BorderBox.Width,
                BlockSize = child.BorderBox.Height,
                MarginLeft = marginLeft,
                MarginTop = marginTop,
                MarginRight = child.MarginBox.Right - child.BorderBox.Right,
                MarginBottom = child.MarginBox.Bottom - child.BorderBox.Bottom,
                BorderLeft = child.PaddingBox.Left - child.BorderBox.Left,
                BorderTop = child.PaddingBox.Top - child.BorderBox.Top,
                BorderRight = child.BorderBox.Right - child.PaddingBox.Right,
                BorderBottom = child.BorderBox.Bottom - child.PaddingBox.Bottom,
                PaddingLeft = child.ContentBox.Left - child.PaddingBox.Left,
                PaddingTop = child.ContentBox.Top - child.PaddingBox.Top,
                PaddingRight = child.PaddingBox.Right - child.ContentBox.Right,
                PaddingBottom = child.PaddingBox.Bottom - child.ContentBox.Bottom,
                IsFloating = child.IsFloating,
                Element = element,
            };

            if (child.IsMultiColumn)
            {
                childFragment.IsMultiColumn = true;
                childFragment.UsedColumnCount = child.ColumnCount;
                childFragment.ColumnInlineSize = child.ColumnWidth;
                childFragment.ColumnProgression = child.ColumnWidth + child.ColumnGapSize;
            }

            RebuildLines(childFragment, child);

            target.Children.Add(childFragment);
            if (child.Children.Count > 0)
                CollectChildren(child, childFragment);
        }
    }

    /// <summary>
    /// Re-derive the fragment's cache of inline lines/runs from the already
    /// converted lines, so text laid out inside a grid/flex item survives the
    /// fragment round-trip (the painter consumes the reconverted Dom.LineBoxes).
    /// </summary>
    private static void RebuildLines(BoxFragment childFragment, LayoutBox child)
    {
        if (child.Lines == null || child.Lines.Count == 0) return;

        foreach (var line in child.Lines)
        {
            var boxLine = new BoxLine
            {
                InlineOffset = line.X - child.BorderBox.Left,
                BlockOffset = line.Y - child.BorderBox.Top,
                InlineSize = line.Width,
                BlockSize = line.Height,
                BaselineOffset = line.Baseline - child.BorderBox.Top,
            };
            foreach (var run in line.Runs)
                boxLine.Runs.Add(new BoxRun
                {
                    Text = run.IsText ? run.Text : null,
                    Node = run.Node,
                    Element = run.Node as Element,
                    InlineOffset = run.X - line.X,
                    InlineSize = run.Width,
                    BlockSize = run.Height,
                    BaselineOffset = run.Baseline,
                });
            childFragment.Lines.Add(boxLine);
        }
    }

    private float ComputeInlineSize()
    {
        var style = Style;
        if (style.Width is PixelLength px) return px.Value + BorderPaddingInline;
        return Space.HasDefiniteInlineSize ? Space.AvailableInlineSize : ChildAvailableInlineSize;
    }

    private float ComputeBlockSize(LayoutBox containerBox)
    {
        var style = Style;
        if (style.Height is PixelLength px) return px.Value + BorderPaddingBlock;
        return containerBox.ContentBox.Height + BorderPaddingBlock;
    }
}