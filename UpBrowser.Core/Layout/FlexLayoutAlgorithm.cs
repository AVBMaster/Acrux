using UpBrowser.Core.Dom;
using UpBrowser.Core.Fonts;
using UpBrowser.Core.Layout.Geometry;
using UpBrowser.Core.Layout.Inline;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Lays out flex items in a flex formatting context.
/// Implements the flex box layout algorithm: main/cross axis, flex-grow/shrink,
/// justify-content, align-items/align-content, and wrapping.
/// Fragment offsets are relative to the container's CONTENT box origin.
/// </summary>
public class FlexLayoutAlgorithm : LayoutAlgorithm
{
    private readonly List<FlexItemData> _items = new();
    public List<FlexLine> Lines { get; } = new();

    public FlexLayoutAlgorithm(Element node, in ConstraintSpace space) : base(node, space) { }

    private class FlexItemData
    {
        public Element Element { get; }
        public ComputedStyle Style => Element.ComputedStyle!;
        public float FlexBaseSize { get; set; }
        public float HypotheticalMainSize { get; set; }
        public float HypotheticalCrossSize { get; set; }
        public float UsedMainSize { get; set; }
        public float UsedCrossSize { get; set; }
        public float MarginMainStart { get; set; }
        public float MarginMainEnd { get; set; }
        public float MarginCrossStart { get; set; }
        public float MarginCrossEnd { get; set; }
        public float MainOffset { get; set; }
        public float CrossOffset { get; set; }
        public bool IsFrozen { get; set; }
        public float ClampedMainSize { get; set; }
        public float TargetMainSize { get; set; }
        public float MainAxisBorderPadding { get; set; }
        public float CrossAxisBorderPadding { get; set; }
        public bool Stretched { get; set; }

        /// <summary>Outer hypothetical main size: content + border/padding + margins.</summary>
        public float OuterHypotheticalMainSize =>
            HypotheticalMainSize + MainAxisBorderPadding + MarginMainStart + MarginMainEnd;

        /// <summary>Distance from the item's border-box top to its first baseline
        /// (NaN when the item has no baseline / is not text-based).</summary>
        public float FirstBaseline = float.NaN;

        /// <summary>Main-axis margins declared `auto`; they absorb the line's free
        /// space before justify-content applies (CSS Flexbox §9.1).</summary>
        public bool MarginMainStartIsAuto;
        public bool MarginMainEndIsAuto;

        /// <summary>Cached min-content main size for the automatic-minimum-size
        /// clamp (-1 = not measured yet).</summary>
        public float MinContentMainSize = -1;

        public FlexItemData(Element element) => Element = element;
    }

    /// <summary>One resolved flex line: its items, cross size and cross-axis origin.</summary>
    private class FlexLineData
    {
        public readonly List<FlexItemData> Items = new();
        public float CrossSize;
        public float CrossStart;
    }

    public override LayoutResult Layout()
    {
        var border = LengthUtils.ComputeBorders(Style);
        var padding = LengthUtils.ComputePadding(Space, Style);
        var bp = new BoxStrut(border.Top + padding.Top, border.Right + padding.Right,
            border.Bottom + padding.Bottom, border.Left + padding.Left);

        Builder.BorderLeft = border.Left; Builder.BorderTop = border.Top;
        Builder.BorderRight = border.Right; Builder.BorderBottom = border.Bottom;
        Builder.PaddingLeft = padding.Left; Builder.PaddingTop = padding.Top;
        Builder.PaddingRight = padding.Right; Builder.PaddingBottom = padding.Bottom;
        Builder.Element = Node;

        var style = Style;
        bool isRow = style.FlexDirection == FlexDirectionType.Row || style.FlexDirection == FlexDirectionType.RowReverse;
        bool isReverse = style.FlexDirection == FlexDirectionType.RowReverse || style.FlexDirection == FlexDirectionType.ColumnReverse;
        bool isMultiline = style.FlexWrap != FlexWrapType.NoWrap;
        bool wrapReverse = style.FlexWrap == FlexWrapType.WrapReverse;

        // Definite cross/main size from the container's own width/height. When the
        // flex container has a definite main size (its own width/height rather than
        // the parent's), items and justify-content are resolved against it --
        // otherwise a centered box with its own width would size/justify against
        // the (much wider) containing block. Pixel AND percentage lengths are
        // definite here: a width:50% container resolves against the space's
        // percentage base (the containing block), matching how the container-size
        // computation below (Compute*ForFragment) resolves the same property, so
        // the gate and the resolution can never disagree.
        float definiteMain;
        float definiteCross;
        if (isRow)
        {
            definiteMain = ResolveOwnContentSize(style.Width, isInlineAxis: true, bp);
            definiteCross = ResolveOwnContentSize(style.Height, isInlineAxis: false, bp);
        }
        else
        {
            definiteMain = ResolveOwnContentSize(style.Height, isInlineAxis: false, bp);
            definiteCross = ResolveOwnContentSize(style.Width, isInlineAxis: true, bp);
        }

        // Main axis falls back to the available extent along that axis: inline for
        // row flow, block for column flow (infinite unless the parent fixed it --
        // column wrap without a definite height therefore never breaks).
        float availableMain = !float.IsNaN(definiteMain)
            ? definiteMain
            : (isRow ? ChildAvailableInlineSize : ChildAvailableBlockSize);
        float availableCross = !float.IsNaN(definiteCross)
            ? definiteCross
            : (isRow ? ChildAvailableBlockSize : ChildAvailableInlineSize);

        // Collect items in DOM order. Absolutely/fixed-positioned children are NOT
        // flex items; they are collected as out-of-flow candidates and positioned
        // against this container (its padding box) after the in-flow pass.
        // Per CSS flexbox spec, contiguous in-flow text becomes an anonymous block
        // flex item, so a flex container can center bare text (e.g. display:flex
        // with only a text child). Element children keep their 'order'-based
        // sorting; anonymous items have order 0.
        _items.Clear();
        var oofCandidates = new List<Element>();
        {
            var slots = new List<(int DomIndex, int Order, FlexItemData Item)>();
            var pendingText = new List<TextNode>();
            int domIndex = 0;

            foreach (var child in Node.Children)
            {
                if (child is TextNode tn)
                {
                    pendingText.Add(tn);
                }
                else if (child is Element el)
                {
                    if (HasMeaningfulText(pendingText))
                        slots.Add((domIndex++, 0, CreateAnonymousFlexItem(style, pendingText)));
                    pendingText.Clear();
                    if (el.ComputedStyle?.Display == DisplayType.None)
                    {
                        domIndex++;
                        continue;
                    }
                    if (el.ComputedStyle?.Position is PositionType.Absolute or PositionType.Fixed)
                    {
                        oofCandidates.Add(el);
                    }
                    else
                    {
                        int order = el.ComputedStyle?.Order ?? 0;
                        slots.Add((domIndex, order, new FlexItemData(el)));
                    }
                    domIndex++;
                }
            }

            if (HasMeaningfulText(pendingText))
                slots.Add((domIndex++, 0, CreateAnonymousFlexItem(style, pendingText)));

            foreach (var slot in slots.OrderBy(s => s.Order).ThenBy(s => s.DomIndex))
                _items.Add(slot.Item);
        }

        // Item border/padding and margins are needed before line breaking (the
        // line-break decision uses the outer hypothetical main size).
        foreach (var item in _items)
        {
            var itemBorder = LengthUtils.ComputeBorders(item.Style);
            var itemPadding = LengthUtils.ComputePadding(Space, item.Style);
            item.MainAxisBorderPadding = isRow
                ? itemBorder.HorizontalSum + itemPadding.HorizontalSum
                : itemBorder.VerticalSum + itemPadding.VerticalSum;
            item.CrossAxisBorderPadding = isRow
                ? itemBorder.VerticalSum + itemPadding.VerticalSum
                : itemBorder.HorizontalSum + itemPadding.HorizontalSum;

            item.MarginMainStart = ResolveMargin(isRow ? item.Style.MarginLeft : item.Style.MarginTop, item.Style.FontSize);
            item.MarginMainEnd = ResolveMargin(isRow ? item.Style.MarginRight : item.Style.MarginBottom, item.Style.FontSize);
            item.MarginCrossStart = ResolveMargin(isRow ? item.Style.MarginTop : item.Style.MarginLeft, item.Style.FontSize);
            item.MarginCrossEnd = ResolveMargin(isRow ? item.Style.MarginBottom : item.Style.MarginRight, item.Style.FontSize);
            var mainMarginStartLen = isRow ? item.Style.MarginLeft : item.Style.MarginTop;
            var mainMarginEndLen = isRow ? item.Style.MarginRight : item.Style.MarginBottom;
            item.MarginMainStartIsAuto = mainMarginStartLen is AutoLength;
            item.MarginMainEndIsAuto = mainMarginEndLen is AutoLength;
        }

        // Compute flex base size and hypothetical sizes
        foreach (var item in _items)
        {
            ComputeFlexBaseSize(item, availableMain, isRow);
            item.HypotheticalMainSize = ClampMainSize(item, item.FlexBaseSize, isRow);
            item.HypotheticalCrossSize = ClampCrossSize(item, ComputeCrossSize(item, availableCross, isRow), isRow);
        }

        // ---- Line breaking (flex-wrap) ----
        float mainGap = ResolveGap(isRow ? style.ColumnGap : style.RowGap, style.FontSize);
        float crossGap = ResolveGap(isRow ? style.RowGap : style.ColumnGap, style.FontSize);

        var lines = new List<FlexLineData>();
        bool canBreak = isMultiline && !float.IsNaN(availableMain) && !float.IsInfinity(availableMain);
        {
            var current = new FlexLineData();
            float cursor = 0;
            foreach (var item in _items)
            {
                float outer = item.OuterHypotheticalMainSize;
                float needed = outer + (current.Items.Count > 0 ? mainGap : 0);
                if (canBreak && current.Items.Count > 0 && cursor + needed > availableMain + 0.5f)
                {
                    lines.Add(current);
                    current = new FlexLineData();
                    cursor = 0;
                    needed = outer;
                }
                current.Items.Add(item);
                cursor += needed;
            }
            if (current.Items.Count > 0 || lines.Count == 0)
                lines.Add(current);
        }
        if (wrapReverse)
            lines.Reverse();

        // ---- Per-line resolution ----
        foreach (var line in lines)
        {
            if (line.Items.Count == 0) continue;

            // Resolve flexible lengths (grow/shrink) against this line's items.
            ResolveFlexibleLengths(line.Items, availableMain, mainGap);

            // Line cross size: the largest outer hypothetical cross size. A single
            // line with a definite container cross size spans the full inner cross
            // size so align-items/justify can center content within the container.
            float natural = 0;
            foreach (var item in line.Items)
                natural = Math.Max(natural, item.HypotheticalCrossSize + item.CrossAxisBorderPadding
                    + item.MarginCrossStart + item.MarginCrossEnd);
            if (lines.Count == 1 && !float.IsNaN(definiteCross))
                natural = Math.Max(natural, availableCross);
            line.CrossSize = natural;

            // Resolve each item's used cross size. 'align-self: stretch' (the
            // initial value via 'align-items') makes an auto-sized item fill the
            // line's cross size; an item with a definite cross size keeps it.
            foreach (var item in line.Items)
            {
                var alignSelf = ResolveAlignSelf(item.Style, style);
                bool hasDefiniteCross = isRow
                    ? item.Style.Height is PixelLength or PercentLength
                    : item.Style.Width is PixelLength or PercentLength;

                item.Stretched = alignSelf == Dom.AlignSelfType.Stretch && !hasDefiniteCross;
                item.UsedCrossSize = item.Stretched
                    ? Math.Max(0, line.CrossSize - item.CrossAxisBorderPadding - item.MarginCrossStart - item.MarginCrossEnd)
                    : item.HypotheticalCrossSize;

                item.CrossOffset = ComputeCrossOffset(item, line.CrossSize, style);
            }

            // Position items along the main axis, content-box origin (0).
            float mainOffset = 0;
            foreach (var item in line.Items)
            {
                item.MainOffset = mainOffset + item.MarginMainStart;
                mainOffset += item.UsedMainSize + item.MainAxisBorderPadding
                    + item.MarginMainStart + item.MarginMainEnd + mainGap;
            }

            if (isReverse && !float.IsInfinity(availableMain) && !float.IsNaN(availableMain))
            {
                foreach (var item in line.Items)
                    item.MainOffset = availableMain - (item.MainOffset + item.UsedMainSize + item.MainAxisBorderPadding + item.MarginMainEnd);
            }

            // Apply justify-content per line.
            ApplyJustifyContent(line.Items, availableMain, style, mainGap);
        }

        // ---- Build fragments (pass 1: lay out items so baselines are measurable) ----
        var laidOut = new List<(FlexLineData Line, FlexItemData Item, BoxFragment Fragment)>();
        foreach (var line in lines)
        {
            foreach (var item in line.Items)
            {
                var childSpace = new ConstraintSpace(
                    availableInlineSize: item.UsedMainSize,
                    availableBlockSize: float.PositiveInfinity,
                    isFixedInlineSize: true,
                    isFixedBlockSize: false
                );
                // Pick the child's layout algorithm by its own display type (e.g. a
                // nested flex/grid item lays out with its flex/grid algorithm rather
                // than as an opaque block).
                var result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(item.Element, childSpace);
                var fragment = result.Fragment;
                laidOut.Add((line, item, fragment));

                if (isRow && fragment.Lines.Count > 0)
                {
                    var firstLine = fragment.Lines[0];
                    var itemBorder0 = LengthUtils.ComputeBorders(item.Style);
                    var itemPadding0 = LengthUtils.ComputePadding(Space, item.Style);
                    item.FirstBaseline = firstLine.BlockOffset + firstLine.BaselineOffset
                        + itemBorder0.Top + itemPadding0.Top;
                }
            }
        }

        // ---- Baseline alignment (flexbox §8.7): shift items so their first
        // baselines coincide, growing the line cross size when needed. ----
        if (isRow)
        {
            foreach (var line in lines)
            {
                float maxAscent = float.NaN;
                foreach (var item in line.Items)
                {
                    if (ResolveAlignSelf(item.Style, style) != Dom.AlignSelfType.Baseline) continue;
                    if (float.IsNaN(item.FirstBaseline)) continue;
                    float ascent = item.MarginCrossStart + item.FirstBaseline;
                    maxAscent = float.IsNaN(maxAscent) ? ascent : Math.Max(maxAscent, ascent);
                }
                if (float.IsNaN(maxAscent)) continue;

                foreach (var item in line.Items)
                {
                    if (ResolveAlignSelf(item.Style, style) != Dom.AlignSelfType.Baseline) continue;
                    if (float.IsNaN(item.FirstBaseline)) continue;
                    item.CrossOffset = Math.Max(0, maxAscent - (item.MarginCrossStart + item.FirstBaseline));
                    line.CrossSize = Math.Max(line.CrossSize,
                        item.CrossOffset + item.UsedCrossSize + item.CrossAxisBorderPadding
                        + item.MarginCrossStart + item.MarginCrossEnd);
                }
            }
        }

        // ---- Cross-axis line packing (align-content) ----
        // Only a container with a DEFINITE cross size has leftover space to
        // distribute; an auto-sized container grows to fit its lines.
        float crossTotal = 0;
        {
            float cursor = 0;
            foreach (var line in lines)
            {
                line.CrossStart = cursor;
                cursor += line.CrossSize + crossGap;
            }
            crossTotal = lines.Count > 0 ? cursor - crossGap : 0;
        }
        if (!float.IsNaN(definiteCross))
            PackLines(lines, crossTotal, availableCross, style, crossGap);

        // ---- Build fragments (pass 2: position) ----
        float maxMainSize = 0;
        foreach (var (line, item, fragment) in laidOut)
        {
            {
                float lineCrossStart = line.CrossStart;
                float mainPos = isRow ? item.MainOffset : lineCrossStart + item.CrossOffset;
                float crossPos = isRow ? lineCrossStart + item.CrossOffset : item.MainOffset;

                fragment.InlineOffset = mainPos;
                fragment.BlockOffset = crossPos;
                // Fragment outer size = content (used flex size) + border/padding.
                // Stretched items keep their border box equal to the line cross
                // size (the content box shrinks inside the border/padding). Map the
                // logical main/cross extents onto inline/block per flow direction.
                float crossBorderBox = item.Stretched
                    ? Math.Max(0, line.CrossSize - item.MarginCrossStart - item.MarginCrossEnd)
                    : item.UsedCrossSize + item.CrossAxisBorderPadding;
                float mainBorderBox = item.UsedMainSize + item.MainAxisBorderPadding;
                fragment.InlineSize = isRow ? mainBorderBox : crossBorderBox;
                fragment.BlockSize = isRow ? crossBorderBox : mainBorderBox;
                fragment.MarginLeft = isRow ? item.MarginMainStart : item.MarginCrossStart;
                fragment.MarginTop = isRow ? item.MarginCrossStart : item.MarginMainStart;
                fragment.MarginRight = isRow ? item.MarginMainEnd : item.MarginCrossEnd;
                fragment.MarginBottom = isRow ? item.MarginCrossEnd : item.MarginMainEnd;

                Builder.AddChild(fragment);
                maxMainSize = Math.Max(maxMainSize, item.MainOffset + item.UsedMainSize + item.MainAxisBorderPadding
                    + item.MarginMainEnd);
            }
        }

        // Compute container size. When the container has a definite main/cross size
        // of its own (e.g. width:120px / height:80px on the flex box) it wins
        // over the content-derived size. The declared width/height is the CONTENT
        // size (content-box semantics, like block layout); the border box adds the
        // container's own border+padding, so e.g. width:200px + 10px borders give
        // a 220px-wide box.
        float containerMain = _items.Count > 0 ? maxMainSize + bp.HorizontalSum : 0;
        float containerCross = _items.Count > 0 ? crossTotal + bp.VerticalSum : 0;

        // A block-level flex container (display:flex, not inline-flex) with an
        // auto width stretches to its containing block, exactly like a block
        // box. For a row flex that is the main axis; for a column flex the
        // inline (cross) axis. Indefinite sizes keep the content-derived size.
        bool fillsAvailableInline = style.Width is AutoLength && style.Display == DisplayType.Flex;
        if (fillsAvailableInline)
        {
            if (isRow && float.IsNaN(definiteMain))
                containerMain = ChildAvailableInlineSize;
            else if (!isRow && float.IsNaN(definiteCross))
                containerCross = ChildAvailableInlineSize;
        }
        if (!float.IsNaN(definiteMain))
            containerMain = isRow
                ? LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
                    t => new MinMaxSizesResult(new MinMaxSizes(definiteMain, definiteMain)))
                : LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, containerMain, definiteMain);
        if (!float.IsNaN(definiteCross))
            containerCross = isRow
                ? LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, containerCross, definiteCross)
                : LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
                    t => new MinMaxSizesResult(new MinMaxSizes(definiteCross, definiteCross)));

        Builder.InlineSize = isRow ? containerMain : containerCross;
        Builder.BlockSize = isRow ? containerCross : containerMain;
        Builder.IntrinsicBlockSize = Builder.BlockSize;

        // Out-of-flow children position against this container's padding box.
        if (oofCandidates.Count > 0)
        {
            var oofPart = new OutOfFlowLayoutPart(Builder, Space);
            foreach (var el in oofCandidates)
            {
                var candidate = new OutOfFlowChildCandidate(
                    new LayoutBox { Dimensions = new BoxDimensions { Style = el.ComputedStyle, Element = el } },
                    new LogicalStaticPosition(new LogicalOffset(bp.Left, 0),
                        LogicalStaticPosition.StaticInlinePosition.Left,
                        LogicalStaticPosition.StaticBlockPosition.Top,
                        WritingDirectionMode.HorizontalLtr))
                {
                    IsAbsolute = el.ComputedStyle!.Position == PositionType.Absolute,
                    IsFixed = el.ComputedStyle!.Position == PositionType.Fixed,
                };
                oofPart.AddCandidate(candidate);
            }
            oofPart.Run();
        }

        var box = Builder.ToBoxFragment();
        box.Children.AddRange(Builder.Children);

        // Build the flex line output (FlexData), consumable by
        // FlexItemIterator.
        Lines.Clear();
        foreach (var line in lines)
        {
            var lineOut = new FlexLine(line.Items.Count);
            foreach (var item in line.Items)
            {
                var childBox = new Dom.LayoutBox { Dimensions = new BoxDimensions { Style = item.Style, Element = item.Element } };
                lineOut.Items.Add(new FlexItem(new BlockNode(childBox))
                {
                    MainAxisFinalSize = item.UsedMainSize,
                    Offset = new FlexOffset(item.MainOffset, item.CrossOffset),
                });
            }
            lineOut.MainAxisFreeSpace = 0;
            lineOut.LineCrossSize = line.CrossSize;
            lineOut.CrossAxisOffset = line.CrossStart;
            Lines.Add(lineOut);
        }

        return LayoutResult.FromFragment(box);
    }

    /// <summary>
    /// Resolve the flex container's own declared size on one axis to a
    /// content-box extent, or NaN when the property is auto/indefinite.
    /// Pixel lengths keep the historical convention (declared size minus the
    /// container's own border+padding); percentages resolve against the space's
    /// percentage base (the containing block), the same base
    /// LengthUtils.ResolveInline/BlockLength uses when the fragment size is
    /// computed. Returns NaN for auto and for percentages against an
    /// indeterminate base, so callers fall back to content-derived sizing.
    /// </summary>
    private float ResolveOwnContentSize(Length? length, bool isInlineAxis, BoxStrut bp)
    {
        float borderPadding = isInlineAxis ? bp.HorizontalSum : bp.VerticalSum;
        if (length is PixelLength px)
            return Math.Max(0, px.Value - borderPadding);
        if (length is PercentLength pct)
        {
            float basis = isInlineAxis
                ? Space.PercentageResolutionInlineSize
                : Space.PercentageResolutionBlockSize;
            if (float.IsNaN(basis) || float.IsInfinity(basis))
                return float.NaN;
            return Math.Max(0, basis * pct.Value - borderPadding);
        }
        return float.NaN;
    }

    private void ComputeFlexBaseSize(FlexItemData item, float availableMain, bool isRow)
    {
        var style = item.Style;
        if (style.FlexBasis is PixelLength px)
        {
            item.FlexBaseSize = px.Value;
        }
        else if (style.FlexBasis is PercentLength pb)
        {
            item.FlexBaseSize = ResolvePercent(pb, isRow);
        }
        else if (isRow && style.Width is PixelLength widthPx)
        {
            item.FlexBaseSize = widthPx.Value;
        }
        else if (isRow && style.Width is PercentLength widthPct)
        {
            item.FlexBaseSize = ResolvePercent(widthPct, isRow);
        }
        else if (!isRow && style.Height is PixelLength heightPx)
        {
            item.FlexBaseSize = heightPx.Value;
        }
        else if (!isRow && style.Height is PercentLength heightPct)
        {
            item.FlexBaseSize = ResolvePercent(heightPct, isRow);
        }
        else
        {
            // Auto: use content size
            item.FlexBaseSize = EstimateContentSize(item.Element, availableMain);
        }

        item.ClampedMainSize = item.FlexBaseSize;
    }

    private float ResolvePercent(PercentLength pct, bool isRow)
    {
        float basis = isRow
            ? Space.PercentageResolutionInlineSize
            : Space.PercentageResolutionBlockSize;
        if (float.IsNaN(basis) || float.IsInfinity(basis))
            basis = isRow ? ChildAvailableInlineSize : 0;
        return pct.Value * basis;
    }

    /// <summary>Clamp a content-box main size by the item's min/max on the main axis.</summary>
    private float ClampMainSize(FlexItemData item, float size, bool isRow)
    {
        float min = ResolveMinMax(item.Style.MinWidth, isRow);
        float max = ResolveMinMax(item.Style.MaxWidth, isRow);
        if (float.IsNaN(min)) min = 0;
        if (float.IsNaN(max)) max = float.MaxValue;

        // Automatic minimum size (CSS Flexbox §4.5): a row-flow item whose
        // min-width is `auto` cannot shrink below its min-content size, otherwise
        // text would be squeezed to nothing.
        // min-width's initial value is `auto`, which the style model stores as null.
        if (isRow && item.Style.MinWidth is AutoLength or null)
        {
            if (item.MinContentMainSize < 0)
                item.MinContentMainSize = ContentMinInlineSize(item.Element);
            min = Math.Max(min, Math.Min(item.MinContentMainSize, max));
        }

        return Math.Clamp(size, Math.Max(0, min), Math.Max(min, max));
    }

    /// <summary>Measures an item's min-content inline size by laying it out in a
    /// 1px-wide constraint space (every break opportunity is then taken).</summary>
    private static float ContentMinInlineSize(Element element)
        => IntrinsicMeasure.MinContentInlineSize(element);

    private float ClampCrossSize(FlexItemData item, float size, bool isRow)
    {
        float min = ResolveMinMax(isRow ? item.Style.MinHeight : item.Style.MinWidth, !isRow);
        float max = ResolveMinMax(isRow ? item.Style.MaxHeight : item.Style.MaxWidth, !isRow);
        if (float.IsNaN(min)) min = 0;
        if (float.IsNaN(max)) max = float.MaxValue;
        return Math.Clamp(size, Math.Max(0, min), Math.Max(min, max));
    }

    private float ResolveMinMax(Length? length, bool inlineAxis)
    {
        switch (length)
        {
            case null:
            case AutoLength:
                return float.NaN;
            case PixelLength px:
                return px.Value;
            case PercentLength pct:
            {
                float basis = inlineAxis
                    ? Space.PercentageResolutionInlineSize
                    : Space.PercentageResolutionBlockSize;
                if (float.IsNaN(basis) || float.IsInfinity(basis)) return float.NaN;
                return pct.Value * basis;
            }
            default:
                return float.NaN;
        }
    }

    private static float EstimateContentSize(Element element, float availableMain)
    {
        // A flex item with an auto main size is content-sized. Measure its real
        // max-content inline size (a full layout pass that recurses into nested
        // block/inline children); the earlier shallow child scan returned 0 for an
        // item whose text lived inside a nested element, collapsing the item.
        float max = IntrinsicMeasure.MaxContentInlineSize(element);
        if (max > 0)
            return max;

        // Fallback: shallow scan for text/children with explicit pixel widths.
        float size = 0;
        var style = element.ComputedStyle;
        foreach (var child in element.Children)
        {
            if (child is TextNode t)
            {
                string data = t.Data ?? "";
                if (data.Length == 0) continue;
                size += style != null ? TextMeasureProxy.MeasureText(data, style) : data.Length * 8;
            }
            else if (child is Element e && e.ComputedStyle?.Width is PixelLength w)
                size += w.Value;
        }
        return size;
    }

    private float ComputeCrossSize(FlexItemData item, float availableCross, bool isRow)
    {
        var style = item.Style;
        if (isRow)
        {
            if (style.Height is PixelLength h) return h.Value;
            if (style.Height is PercentLength hp)
            {
                float basis = Space.PercentageResolutionBlockSize;
                if (!float.IsNaN(basis) && !float.IsInfinity(basis)) return hp.Value * basis;
            }
        }
        else
        {
            if (style.Width is PixelLength w) return w.Value;
            if (style.Width is PercentLength wp)
            {
                float basis = Space.PercentageResolutionInlineSize;
                if (!float.IsNaN(basis) && !float.IsInfinity(basis)) return wp.Value * basis;
            }
        }
        return EstimateContentCrossSize(item.Element);
    }

    private static float EstimateContentCrossSize(Element element)
    {
        // Content-sized cross axis: measure the element's real stacked height so a
        // nested block/inline child (no explicit height) is counted. The shallow scan
        // below only handled direct text and explicitly-sized children.
        float block = IntrinsicMeasure.MaxContentBlockSize(element);
        if (block > 0)
            return block;

        float size = 0;
        var style = element.ComputedStyle;
        if (style != null)
        {
            foreach (var child in element.Children)
            {
                if (child is TextNode t && !string.IsNullOrWhiteSpace(t.Data))
                    size = Math.Max(size, LineBoxMetrics.GetLineHeight(style));
            }
        }

        foreach (var child in element.Children)
        {
            if (child is Element e && e.ComputedStyle?.Height is PixelLength h)
                size = Math.Max(size, h.Value);
        }
        return size;
    }

    /// <summary>
    /// True when the pending run contains at least one text node with non-space
    /// content. Whitespace-only runs between element children must not become
    /// anonymous flex items (they are collapsed by block/inline layout).
    /// </summary>
    private static bool HasMeaningfulText(List<TextNode> pendingText)
    {
        foreach (var textNode in pendingText)
        {
            var data = textNode.Data;
            if (!string.IsNullOrWhiteSpace(data))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Builds an anonymous block flex item wrapping a contiguous run of text
    /// nodes (CSS: in-flow text directly inside a flex container becomes an
    /// anonymous flex item). The synthetic element keeps <see cref="FlexItemData"/>
    /// uniform so block layout produces the text runs; the original text nodes
    /// are referenced without re-parenting, so painting and hit-testing still
    /// resolve style and events through their real parent element.
    /// </summary>
    private static FlexItemData CreateAnonymousFlexItem(ComputedStyle containerStyle, IReadOnlyList<TextNode> textNodes)
    {
        var wrapper = new HtmlElement("anonymous-flex-item");
        var style = containerStyle.Clone();
        style.Display = DisplayType.Block;
        style.Width = AutoLength.Instance;
        style.Height = AutoLength.Instance;
        style.MarginTop = style.MarginRight = style.MarginBottom = style.MarginLeft = new PixelLength(0);
        style.PaddingTop = style.PaddingRight = style.PaddingBottom = style.PaddingLeft = new PixelLength(0);
        style.BorderTopWidth = style.BorderBottomWidth = style.BorderLeftWidth = style.BorderRightWidth = 0;
        style.MinWidth = null;
        style.MaxWidth = null;
        style.MinHeight = null;
        style.MaxHeight = null;
        style.Order = 0;
        style.FlexGrow = 0;
        style.FlexShrink = 1;
        style.FlexBasis = AutoLength.Instance;
        style.BackgroundColor = null;
        style.BackgroundImage = null;
        wrapper.ComputedStyle = style;
        foreach (var textNode in textNodes)
            wrapper.Children.Add(textNode);
        return new FlexItemData(wrapper);
    }

    private void ResolveFlexibleLengths(List<FlexItemData> items, float availableMain, float mainGap)
    {
        bool isRowAxis = Style.FlexDirection is FlexDirectionType.Row or FlexDirectionType.RowReverse;
        foreach (var item in items)
        {
            item.UsedMainSize = item.FlexBaseSize;
            item.IsFrozen = false;
        }

        bool finite = !float.IsNaN(availableMain) && !float.IsInfinity(availableMain);
        float marginSum = 0;
        foreach (var item in items)
            marginSum += item.MarginMainStart + item.MarginMainEnd;
        availableMain -= marginSum;
        if (items.Count > 1)
            availableMain -= mainGap * (items.Count - 1);
        if (!finite)
        {
            foreach (var item in items)
                item.UsedMainSize = ClampMainSize(item, item.UsedMainSize, isRowAxis);
            return;
        }

        // CSS Flexbox §9.7 iterative resolution with freeze steps: clamping a
        // flexible track to its min/max freezes the item and removes its
        // contribution from the remaining free space, so shrinkable/growable
        // siblings absorb the difference instead of overflowing.
        float Unclamped(FlexItemData item) => item.FlexBaseSize + item.TargetMainSize;
        float FrozenUsed(FlexItemData item) => ClampMainSize(item, Unclamped(item), isRowAxis);

        foreach (var item in items)
        {
            float clamped = ClampMainSize(item, item.FlexBaseSize, isRowAxis);
            item.TargetMainSize = clamped - item.FlexBaseSize;
        }

        for (int pass = 0; pass < items.Count + 2; pass++)
        {
            // Remaining free space = available minus frozen used sizes minus
            // unfrozen items' current unclamped hypothetical sizes.
            float used = 0;
            foreach (var item in items)
                used += item.IsFrozen ? FrozenUsed(item) : Unclamped(item);
            float totalRemaining = availableMain - used;
            if (Math.Abs(totalRemaining) < 0.01f) break;

            if (totalRemaining > 0)
            {
                float totalGrow = 0;
                foreach (var item in items)
                    if (!item.IsFrozen) totalGrow += item.Style.FlexGrow;
                if (totalGrow == 0) break;

                int maxViolation = -1;
                float maxViol = 0;
                foreach (var item in items)
                {
                    if (item.IsFrozen || item.Style.FlexGrow == 0) continue;
                    float grow = item.Style.FlexGrow / totalGrow;
                    float scaled = totalRemaining * grow;
                    float factor = scaled < 1 ? scaled : 1;
                    item.TargetMainSize += scaled * factor;
                    float unclamped = Unclamped(item);
                    if (unclamped > ClampMainSize(item, unclamped, isRowAxis) && unclamped - ClampMainSize(item, unclamped, isRowAxis) > maxViol)
                    {
                        maxViol = unclamped - ClampMainSize(item, unclamped, isRowAxis);
                        maxViolation = items.IndexOf(item);
                    }
                }
                if (maxViolation < 0) break;
                var viol = items[maxViolation];
                viol.TargetMainSize = ClampMainSize(viol, Unclamped(viol), isRowAxis) - viol.FlexBaseSize;
                viol.IsFrozen = true;
            }
            else
            {
                float weighted = 0;
                foreach (var item in items)
                    if (!item.IsFrozen) weighted += item.Style.FlexShrink * item.FlexBaseSize;
                if (weighted == 0) break;

                int maxViolation = -1;
                float maxRatio = -1;
                foreach (var item in items)
                {
                    if (item.IsFrozen || item.Style.FlexShrink == 0) continue;
                    float scaled = -totalRemaining * (item.Style.FlexShrink * item.FlexBaseSize / weighted);
                    float factor = scaled < 1 ? scaled : 1;
                    item.TargetMainSize -= scaled * factor;
                    float unclamped = Unclamped(item);
                    float clamped = ClampMainSize(item, unclamped, isRowAxis);
                    float ratio = unclamped == 0 ? float.MaxValue : (clamped - unclamped) / (item.Style.FlexShrink * item.FlexBaseSize);
                    if (unclamped < clamped && ratio > maxRatio)
                    {
                        maxRatio = ratio;
                        maxViolation = items.IndexOf(item);
                    }
                }
                if (maxViolation < 0) break;
                var viol = items[maxViolation];
                viol.TargetMainSize = ClampMainSize(viol, Unclamped(viol), isRowAxis) - viol.FlexBaseSize;
                viol.IsFrozen = true;
            }
        }

        foreach (var item in items)
        {
            item.UsedMainSize = Math.Max(0, ClampMainSize(item, Unclamped(item), isRowAxis));
        }
    }

    /// <summary>
    /// Resolve 'align-self: auto' against the container's 'align-items', giving
    /// the item's effective cross-axis alignment.
    /// </summary>
    private static Dom.AlignSelfType ResolveAlignSelf(ComputedStyle itemStyle, ComputedStyle containerStyle)
    {
        var alignSelf = itemStyle.AlignSelf;
        if (alignSelf != Dom.AlignSelfType.Auto)
            return alignSelf;

        return containerStyle.AlignItems switch
        {
            Dom.AlignItemsType.FlexStart => Dom.AlignSelfType.FlexStart,
            Dom.AlignItemsType.FlexEnd => Dom.AlignSelfType.FlexEnd,
            Dom.AlignItemsType.Center => Dom.AlignSelfType.Center,
            Dom.AlignItemsType.Baseline => Dom.AlignSelfType.Baseline,
            _ => Dom.AlignSelfType.Stretch
        };
    }

    private static float ComputeCrossOffset(FlexItemData item, float lineCrossSize, ComputedStyle style)
    {
        var alignSelf = ResolveAlignSelf(item.Style, style);
        float outerCross = item.UsedCrossSize + item.CrossAxisBorderPadding + item.MarginCrossStart + item.MarginCrossEnd;

        return alignSelf switch
        {
            Dom.AlignSelfType.FlexEnd => lineCrossSize - outerCross + item.MarginCrossStart,
            Dom.AlignSelfType.Center => item.MarginCrossStart + (lineCrossSize - outerCross) / 2,
            // stretch / start / baseline: at the line cross-start (baseline
            // alignment is not implemented and falls back to start).
            _ => item.MarginCrossStart,
        };
    }

    /// <summary>
    /// Distribute leftover container cross space between/around flex lines per
    /// 'align-content'. Only meaningful with a definite container cross size.
    /// Lines arrive with their sequential (stretch-start) positions in
    /// CrossStart; this shifts and/or grows them.
    /// </summary>
    private static void PackLines(List<FlexLineData> lines, float crossTotal, float availableCross,
        ComputedStyle style, float crossGap)
    {
        if (lines.Count == 0) return;

        float extra = availableCross - crossTotal;
        if (extra <= 0)
            return;

        string mode = (style.AlignContent ?? "stretch").Trim().ToLowerInvariant();

        if (lines.Count == 1)
        {
            if (mode == "stretch")
                lines[0].CrossSize = availableCross;
            else
                lines[0].CrossStart += mode switch
                {
                    "center" => extra / 2,
                    "end" or "flex-end" => extra,
                    _ => 0,
                };
            return;
        }

        switch (mode)
        {
            case "center":
                ShiftLines(lines, extra / 2);
                break;
            case "end":
            case "flex-end":
                ShiftLines(lines, extra);
                break;
            case "space-between":
            {
                float gapExtra = extra / (lines.Count - 1);
                for (int i = 0; i < lines.Count; i++)
                    lines[i].CrossStart += gapExtra * i;
                break;
            }
            case "space-around":
            {
                float gapExtra = extra / lines.Count;
                for (int i = 0; i < lines.Count; i++)
                    lines[i].CrossStart += gapExtra * (i + 0.5f);
                break;
            }
            case "space-evenly":
            {
                float gapExtra = extra / (lines.Count + 1);
                for (int i = 0; i < lines.Count; i++)
                    lines[i].CrossStart += gapExtra * (i + 1);
                break;
            }
            case "stretch":
            default:
            {
                // Stretch grows every line equally; subsequent lines shift by the
                // accumulated growth.
                float grow = extra / lines.Count;
                float shift = 0;
                foreach (var line in lines)
                {
                    line.CrossStart += shift;
                    line.CrossSize += grow;
                    shift += grow;
                }
                break;
            }
        }
    }

    private static void ShiftLines(List<FlexLineData> lines, float delta)
    {
        foreach (var line in lines)
            line.CrossStart += delta;
    }

    private void ApplyJustifyContent(List<FlexItemData> items, float availableMain, ComputedStyle style, float mainGap)
    {
        if (items.Count == 0) return;
        if (float.IsNaN(availableMain) || float.IsInfinity(availableMain))
            return;

        float totalMain = 0;
        foreach (var item in items)
            totalMain += item.UsedMainSize + item.MainAxisBorderPadding + item.MarginMainStart + item.MarginMainEnd;
        totalMain += mainGap * (items.Count - 1);
        float freeSpace = availableMain - totalMain;
        if (freeSpace <= 0)
            return;

        // 'auto' margins on the main axis absorb the free space first; when any
        // exists, justify-content only distributes what is left (CSS Flexbox §9.1).
        int autoMargins = 0;
        foreach (var item in items)
        {
            if (item.MarginMainStartIsAuto) autoMargins++;
            if (item.MarginMainEndIsAuto) autoMargins++;
        }
        if (autoMargins > 0)
        {
            float share = freeSpace / autoMargins;
            float shift = 0;
            foreach (var item in items)
            {
                if (item.MarginMainStartIsAuto) shift += share;
                item.MainOffset += shift;
                if (item.MarginMainEndIsAuto) shift += share;
            }
            return;
        }

        switch (style.JustifyContent)
        {
            case Dom.JustifyContentType.Center:
                Shift(items, freeSpace / 2);
                break;
            case Dom.JustifyContentType.FlexEnd:
                Shift(items, freeSpace);
                break;
            case Dom.JustifyContentType.SpaceBetween:
                Distribute(items, freeSpace, true, items.Count);
                break;
            case Dom.JustifyContentType.SpaceAround:
                Distribute(items, freeSpace, false, items.Count);
                break;
            case Dom.JustifyContentType.SpaceEvenly:
                DistributeEvenly(items, freeSpace, items.Count);
                break;
        }
    }

    private static void Shift(List<FlexItemData> items, float amount)
    {
        foreach (var item in items)
            item.MainOffset += amount;
    }

    private static void Distribute(List<FlexItemData> items, float freeSpace, bool spaceBetween, int count)
    {
        int gaps = count - 1;
        if (gaps <= 0) return;
        float gap = spaceBetween ? freeSpace / gaps : freeSpace / (count * 2f);
        float offset = spaceBetween ? 0 : gap;
        foreach (var item in items)
        {
            item.MainOffset += offset;
            offset += spaceBetween ? gap : gap * 2;
        }
    }

    private static void DistributeEvenly(List<FlexItemData> items, float freeSpace, int count)
    {
        float gap = freeSpace / (count + 1);
        float offset = gap;
        foreach (var item in items)
        {
            item.MainOffset += offset;
            offset += gap;
        }
    }

    private float ResolveMargin(Length length, float fontSize)
    {
        if (length is AutoLength || length == null) return 0;
        return length.ToPixels(fontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
    }

    private float ResolveGap(Length gap, float fontSize) =>
        gap is AutoLength ? 0 : gap.ToPixels(fontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
}
