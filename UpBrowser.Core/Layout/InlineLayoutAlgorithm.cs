using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;
using UpBrowser.Core.Layout.Inline;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Inline layout algorithm aligned to the modern layout pipeline. Uses ConstraintSpaceBuilder,
/// LengthUtils, BoxFragmentBuilder. Handles text runs, atomic inlines,
/// line breaking, and inline formatting context.
/// </summary>
public class InlineLayoutAlgorithm : LayoutAlgorithm
{
    /// <summary>Merged ::first-line style of this block, when declared.</summary>
    private ComputedStyle? _firstLineStyle;
    private readonly LayoutAlgorithm _parent;
    private readonly List<BoxLine> _lines = new();
    private float _currentLineInlineOffset;
    private float _currentLineBlockOffset;
    private float _lineHeight;
    private FragmentItems? _fragmentItems;

    public FragmentItems? FragmentItems => _fragmentItems;

    public InlineLayoutAlgorithm(Element node, in ConstraintSpace space, LayoutAlgorithm parent)
        : base(node, space)
    {
        _parent = parent;
        _lineHeight = Fonts.LineBoxMetrics.GetLineHeight(node.ComputedStyle);
    }

    public override LayoutResult Layout()
    {
        var border = OwnBorders;
        var padding = LengthUtils.ComputePadding(Space, Style);
        var bp = new BoxStrut(border.Top + padding.Top, border.Right + padding.Right,
            border.Bottom + padding.Bottom, border.Left + padding.Left);

        Builder.BorderLeft = border.Left; Builder.BorderTop = border.Top;
        Builder.BorderRight = border.Right; Builder.BorderBottom = border.Bottom;
        Builder.PaddingLeft = padding.Left; Builder.PaddingTop = padding.Top;
        Builder.PaddingRight = padding.Right; Builder.PaddingBottom = padding.Bottom;
        Builder.Element = Node;

        // Line boxes are recorded in this box's border-box coordinates, so the
        // content origin carries the border as well as the padding.
        _currentLineInlineOffset = border.Left + padding.Left;
        _currentLineBlockOffset = border.Top + padding.Top;
        _lines.Clear();

        float availInline = ChildAvailableInlineSize;

        // Line breaking must use THIS block's content-box width, not the parent's
        // available width. Otherwise a width-constrained block (e.g. width:129px,
        // or a multicol column) never wraps its text 鈥?it breaks only at the
        // ancestor width. When the constraint space fixes the inline size
        // (fragmentainers), that fixed size is already the content width.
        if (!Space.IsFixedInlineSize)
        {
            float ownBorderBox = LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
                t => new MinMaxSizesResult(new MinMaxSizes(availInline, availInline)));
            if (!LengthUtils.IsIndefinite(ownBorderBox))
                availInline = Math.Max(0, ownBorderBox - bp.HorizontalSum);
        }

        // width: max-content / min-content / fit-content on an inline-formatting
        // context root: measure the content's intrinsic inline size with an
        // unconstrained / fully-constrained pass, then lay out at that width.
        if (Style.Width is IntrinsicLength intrinsicWidth && !_measuringIntrinsics)
        {
            float maxContent = MeasureIntrinsicInlineSize(minContent: false, bp);
            float minContent = MeasureIntrinsicInlineSize(minContent: true, bp);
            float resolved = intrinsicWidth.Kind switch
            {
                IntrinsicSizeKind.MaxContent => maxContent,
                IntrinsicSizeKind.MinContent => minContent,
                _ => Math.Min(Math.Max(minContent, availInline + bp.HorizontalSum), maxContent),
            };
            if (resolved > 0 && !float.IsNaN(resolved))
                availInline = Math.Max(0, resolved - bp.HorizontalSum);
        }

        float curInlineSize = 0, curBlockSize = 0, curBaseline = 0, maxBlockSize = 0;

        if (TryLayoutLinesWithNgPipeline(availInline, _currentLineInlineOffset, _currentLineBlockOffset))
        {
            // Modern pipeline produced lines; skip legacy path.
        }
        else
        {
            var curLine = new BoxLine { InlineOffset = _currentLineInlineOffset, BlockOffset = _currentLineBlockOffset };

            foreach (var child in Node.Children)
            {
                if (child is TextNode tn)
                    curLine = ProcessText(tn, curLine, availInline, ref curInlineSize, ref curBlockSize, ref curBaseline, ref maxBlockSize);
                else if (child is Element el)
                    curLine = ProcessElement(el, curLine, availInline, ref curInlineSize, ref curBlockSize, ref curBaseline, ref maxBlockSize);
            }

            if (curLine.Runs.Count > 0)
            {
                curLine.InlineSize = curInlineSize; curLine.BlockSize = curBlockSize;
                curLine.BaselineOffset = curBaseline; _lines.Add(curLine);
            }
        }

        float intrinsicBlock = 0;
        foreach (var l in _lines) intrinsicBlock = Math.Max(intrinsicBlock, l.BlockEnd + bp.Bottom);
        Builder.IntrinsicBlockSize = intrinsicBlock;

        float blockSize = LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, intrinsicBlock, availInline);
        if (LengthUtils.IsIndefinite(blockSize)) blockSize = intrinsicBlock;
        var (minB, maxB) = LengthUtils.ComputeMinMaxBlockSizes(Space, Style, bp, null, _ => intrinsicBlock);
        Builder.BlockSize = Math.Clamp(blockSize, minB, maxB);

        float inlineSize = LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
            t => new MinMaxSizesResult(new MinMaxSizes(availInline, availInline)));
        if (LengthUtils.IsIndefinite(inlineSize)) inlineSize = availInline;

        // Intrinsic width keywords: the line breaking above already ran at the
        // measured content width, so the box takes that width (plus the box
        // model struts) rather than stretching to the container.
        if (Style.Width is IntrinsicLength)
        {
            float widestLine = 0;
            foreach (var l in _lines)
                widestLine = Math.Max(widestLine, l.InlineSize);
            if (widestLine > 0)
                inlineSize = widestLine + bp.HorizontalSum;
        }

        // Shrink-to-fit auto width (atomic inline / inline-block / float): the box
        // sizes to its content (the widest line), not to the full available
        // width. Without this an inline-block such as a <button> stretches across
        // the whole line and forces surrounding content to wrap.
        if (Space.IsShrinkToFit && Style.Width is null or AutoLength)
        {
            float maxLineInline = 0;
            foreach (var l in _lines)
                maxLineInline = Math.Max(maxLineInline, l.InlineSize);
            float contentBorderBox = maxLineInline + bp.HorizontalSum;
            // An empty atomic inline shrinks to zero; keeping the available width
            // here would push it onto its own line and break the surrounding text.
            inlineSize = Math.Min(inlineSize, contentBorderBox);
        }

        var (minI, maxI) = LengthUtils.ComputeMinMaxInlineSizes(Space, Style, bp,
            t => new MinMaxSizesResult(new MinMaxSizes(availInline, availInline)));
        Builder.InlineSize = Math.Clamp(inlineSize, minI, maxI);

        // Aspect-ratio with auto height: derive the block size from the resolved
        // inline size. Mirrors the block path; runs here because an inline
        // formatting-context root is sized by this algorithm, not LayoutMain.
        if (Style.AspectRatio > 0 && Style.Height is AutoLength or null
            && !float.IsNaN(Builder.InlineSize) && Builder.InlineSize > 0)
        {
            float contentInline = Math.Max(0, Builder.InlineSize - bp.HorizontalSum);
            float arBlock = contentInline / Style.AspectRatio + bp.VerticalSum;
            Builder.BlockSize = Math.Clamp(arBlock, minB, maxB);
            Builder.IntrinsicBlockSize = arBlock;
        }

        // Absolutely/fixed-positioned children of an inline formatting context
        // root are not inline items: position them against this container after
        // the in-flow pass, mirroring the block path's OutOfFlowLayoutPart.
        RunOutOfFlowChildren(bp);

        var frag = Builder.ToBoxFragment();
        frag.Children.AddRange(Builder.Children);
        frag.Lines.AddRange(_lines);
        frag.FragmentItems = _fragmentItems;
        return LayoutResult.FromFragment(frag);
    }

    private void RunOutOfFlowChildren(BoxStrut bp)
    {
        List<Element>? oof = null;
        foreach (var child in Node.Children)
        {
            if (child is Element el &&
                el.ComputedStyle?.Position is PositionType.Absolute or PositionType.Fixed &&
                el.ComputedStyle.Display != DisplayType.None)
            {
                oof ??= new List<Element>();
                oof.Add(el);
            }
        }
        if (oof == null) return;

        var oofPart = new OutOfFlowLayoutPart(Builder, Space);
        foreach (var el in oof)
        {
            // Static position: the content-box origin (inline offset recorded
            // border-box relative, block offset content relative).
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

    /// <summary>
    /// Modern inline layout path: collect items via InlineNode, break lines
    /// via LineBreaker, build logical line items via LogicalLineBuilder, then
    /// convert to the existing BoxLine/BoxRun output model. Returns false when
    /// there is nothing to lay out (so callers can fall back).
    /// </summary>
    private bool TryLayoutLinesWithNgPipeline(float availInline, float contentInlineOrigin, float contentBlockOrigin)
    {
        var inlineNode = new InlineNode(Node, Style);
        inlineNode.CollectInlineItems();
        inlineNode.ComputeBidiFlags();
        var data = inlineNode.ItemsData;
        if (data.Items.Count == 0) return false;

        var lineBreaker = new LineBreaker();
        lineBreaker.SetUnitContext(Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight, Space.DpiScale);
        lineBreaker.SetIntrinsicMinContent(_sMinContentProbe);
        // ::first-line changes measurement (font-size/weight/letter-spacing), so the
        // merged style has to reach the line breaker, not just painting.
        _firstLineStyle = UpBrowser.Core.Css.PseudoStyleMerger.Merge(Style, Node.FirstLineStyles);
        lineBreaker.SetFirstLineStyle(_firstLineStyle);

        // Only line boxes avoid floats, block boxes do not (CSS 2.1 §9.5.2), so the
        // inline range of each line has to be resolved against the formatting
        // context's exclusion space. That requires breaking the lines one at a time,
        // because a line's vertical position is only known once the previous lines
        // have been laid out.
        var floatContext = CreateFloatLineContext(contentInlineOrigin, contentBlockOrigin, availInline, data);
        List<LineInfo> lines;
        if (floatContext == null)
        {
            lines = lineBreaker.BreakLines(data, availInline, Style);
        }
        else
        {
            lines = new List<LineInfo>();
            lineBreaker.BeginBreaking(data, availInline, Style);
            lineBreaker.SetFloatContext(floatContext);
        }
        float contentTopBfc = floatContext?.ContentTop ?? 0;
        float strutHeight = Fonts.LineBoxMetrics.GetLineHeight(Style);

        var stateStack = new InlineLayoutStateStack();
        var builder = new LogicalLineBuilder(inlineNode, Space, null, stateStack);

        // text-overflow: ellipsis support via LineTruncator. Per spec it only
        // applies when the block clips overflow (overflow != visible).
        bool useEllipsis = Style.TextOverflow is TextOverflowType.Ellipsis
            && Style.Overflow is not OverflowType.Visible;
        var itemsBuilder = new FragmentItemsBuilder();

        int nextLineIndex = 0;
        while (true)
        {
            LineInfo info;
            if (floatContext != null)
            {
                if (lineBreaker.IsFinishedNow())
                    break;
                // Where this line box starts, vertically, relative to the content box.
                float lineTop = _currentLineBlockOffset - contentBlockOrigin;
                var (inset, width, pushDownTo) = floatContext.LineSpaceAt(contentTopBfc + lineTop, strutHeight);
                if (!float.IsNaN(pushDownTo) && pushDownTo > lineTop)
                {
                    // No inline room left on this line: the line box begins below the
                    // floats instead.
                    lineTop = pushDownTo;
                    _currentLineBlockOffset = lineTop + contentBlockOrigin;
                    (inset, width, _) = floatContext.LineSpaceAt(contentTopBfc + lineTop, strutHeight);
                }
                lineBreaker.SetLineSpace(inset, width, contentTopBfc + lineTop);
                info = lineBreaker.BreakNextLine();
                // Mirror BreakLines(): a trailing forced break does not open an extra
                // empty line box.
                if (info.IsLastLine() && info.IsEmptyLine() && lineBreaker.PreviousLineHadForcedBreak)
                    break;
            }
            else
            {
                if (nextLineIndex >= lines.Count)
                    break;
                info = lines[nextLineIndex++];
            }

            float lineWidth = floatContext != null && info.AvailableInlineSize > 0
                ? info.AvailableInlineSize
                : availInline;
            info.AvailableInlineSize = lineWidth;
            var logicalLineItems = new LogicalLineItems();
            // Continuation-line box-state rebuild happens inside CreateLine.
            builder.CreateLine(info, logicalLineItems, this);

            // Truncate overflowing lines and place ellipsis. 'nowrap' content simply
            // does not break, so the width comparison - not the breaker's overflow
            // flag - is what detects it (CSS Overflow 3 §4.5).
            bool lineOverflows = info.HasOverflow() || info.InlineSize > lineWidth + 0.5f;
            if (useEllipsis && lineOverflows)
            {
                var truncator = new LineTruncator(info);
                truncator.TruncateLine(info.InlineSize, logicalLineItems, stateStack);
            }

            // Apply text-align (end/center/justify). Justify distributes the
            // free inline space into word-spacing expansion opportunities on every
            // line except the last. LTR 'start' needs no work, but RTL 'start' is
            // the right edge, so it goes through the same shift path.
            if (info.TextAlign() is not TextAlignType.Start || info.BaseDirection() == TextDirection.Rtl)
                JustificationUtils.ApplyTextAlignment(info, logicalLineItems, lineWidth);

            // The line box height is the united strut of the inline boxes on the
            // line; fall back to this container's own strut when the line breaker
            // did not report one.
            float lineBlockSize = info.BlockSize > 0
                ? info.BlockSize
                : Fonts.LineBoxMetrics.GetLineHeight(Style);

            var boxLine = new BoxLine
            {
                // The line breaker already gates 'text-indent' (including the
                // hanging variant) to the lines it applies to. A float on the line's
                // left moves the whole line box right (info.LeftInset).
                InlineOffset = contentInlineOrigin + info.LeftInset + info.TextIndent()
                    + (info.IsFirstFormattedLine() ? List.ListMarker.InsideMarkerIndent(Style) : 0),
                BlockOffset = _currentLineBlockOffset,
                InlineSize = info.InlineSize,
                BlockSize = lineBlockSize,
                BaselineOffset = _currentLineBlockOffset +
                    Fonts.LineBoxMetrics.GetBaselineForLineHeight(Style, lineBlockSize)
            };

            // Add a line item + content items to the FragmentItemsBuilder.
            var lineBoxFragment = new PhysicalLineBoxFragment
            {
                Size = new PhysicalSize(Math.Min(info.InlineSize, lineWidth), boxLine.BlockSize),
                BaselineOffset = boxLine.BaselineOffset,
            };
            itemsBuilder.Add(FragmentItem.CreateLine(lineBoxFragment, logicalLineItems.Count));

            for (int i = 0; i < logicalLineItems.Count; i++)
            {
                var item = logicalLineItems[i];
                if (item.IsHiddenForPaint) continue;
                if (!item.HasInFlowOrFloatingFragment && item.InlineItem == null && string.IsNullOrEmpty(item.TextContent))
                    continue;

                // LogicalLineBuilder has already laid the children out left to
                // right via ComputeInlinePositions, so the child's placed inline
                // offset is its Rect.InlineStart (relative to the line content
                // start). Add the container padding to get the container-relative
                // paint offset. Re-accumulating here was what stacked every run at
                // the same x.
                // LogicalLineBuilder has already laid the children out left to
                // right via ComputeInlinePositions, so the child's placed inline
                // offset is its Rect.InlineStart - relative to the LINE BOX, which
                // is the frame BoxRun.InlineOffset uses (the converter adds the line
                // box origin, which already carries padding, indent and float
                // insets). Re-accumulating here was what stacked every run at the
                // same x; adding the padding again was what double-indented it.
                float runInline = item.Rect.InlineStart;
                float itemInlineSize = item.Rect.InlineSize;

                if (item.InlineItem?.IsAtomicInline == true || item.LayoutResult != null)
                {
                    var atomicFragment = item.LayoutResult?.Fragment;
                    // For atomic inlines placed via PlaceLayoutResult the
                    // LogicalLineItem carries no InlineItem, so recover the element
                    // from the laid-out fragment; without it the box cannot be
                    // matched back to its DOM element and never paints.
                    var el = item.InlineItem?.Element ?? atomicFragment?.Element;
                    float atomicBlockSize = atomicFragment != null && atomicFragment.BlockSize > 0
                        ? atomicFragment.BlockSize
                        : (item.Size.BlockSize > 0 ? item.Size.BlockSize : Style.FontSize);
                    boxLine.Runs.Add(new BoxRun
                    {
                        Element = el,
                        InlineOffset = runInline,
                        InlineSize = itemInlineSize,
                        BlockOffset = _currentLineBlockOffset,
                        BlockSize = atomicBlockSize,
                        IsAtomicInline = true,
                        // Carry the atomic inline's own fragment so the converter
                        // can turn it into a paintable box (background/border/text).
                        AtomicInlineBox = atomicFragment,
                    });
                }
                else
                {
                    var text = item.TextContent ?? item.InlineItem?.TextContent() ?? "";
                    if (text.Length > 0
                        && item.InlineItem != null
                        && item.InlineItem.Type != InlineItem.InlineItemType.Text
                        && text.IndexOf(Character.kObjectReplacementCharacter) >= 0)
                    {
                        // The U+FFFC here is the internal object-replacement
                        // placeholder of an inline item (atomic inline / float /
                        // out-of-flow positioned), laid out as a replaced or
                        // control unit. It is layout bookkeeping, not user text,
                        // and must never be painted as a glyph.
                        text = "";
                    }
                    if (text.Length > 0)
                    {
                        // The painted text run must carry its TextNode so the
                        // renderer (DrawInlineRuns) can paint it and resolve the
                        // per-text-node style/selection state. BaselineOffset 0
                        // keeps the run on the line box's absolute baseline.
                        float vaShift = ComputeVerticalAlignShift(
                            item.InlineItem?.GetLayoutObject()?.Node, item.Size.BlockSize);
                        // Paint resolves per-run fonts, so the ::first-line style has
                        // to be folded in here for the first line's runs.
                        var runStyle = item.InlineItem?.StyleOverride ?? Style;
                        if (_firstLineStyle != null && info.IsFirstFormattedLine())
                            runStyle = UpBrowser.Core.Css.PseudoStyleMerger.Merge(runStyle, Node.FirstLineStyles) ?? runStyle;
                        boxLine.Runs.Add(new BoxRun
                        {
                            Text = text,
                            Node = item.InlineItem?.GetLayoutObject()?.Node,
                            InlineOffset = runInline,
                            InlineSize = itemInlineSize,
                            BlockOffset = _currentLineBlockOffset,
                            BlockSize = Math.Max(Style.FontSize, item.Size.BlockSize),
                            BaselineOffset = 0,
                            BaselineShift = vaShift,
                            // Carry the item's resolved style (per-run font, and the
                            // ::first-letter override) so painting matches measuring.
                            FontSize = runStyle.FontSize,
                            FontFamily = runStyle.FontFamily,
                            FontWeight = runStyle.FontWeight,
                            Italic = runStyle.FontStyle == FontStyleType.Italic || runStyle.FontStyle == FontStyleType.Oblique,
                            Color = runStyle.Color,
                        });
                        itemsBuilder.Add(new FragmentItem(FragmentItem.ItemType.Text)
                        {
                            Text = text,
                            Offset = new PhysicalOffset(item.Rect.InlineStart, 0),
                            Size = new PhysicalSize(itemInlineSize, boxLine.BlockSize),
                        });
                    }
                }
            }

            _lines.Add(boxLine);

            // Place every box on the line by its 'vertical-align' and grow the line
            // box to contain them (CSS 2.1 §10.6.3, §10.8.1).
            AlignLineBoxes(boxLine, lineBlockSize);

            // Also feed via LogicalLineContainer.
            var lineContainer = new LogicalLineContainer();
            foreach (var li in logicalLineItems)
                lineContainer.BaseLine.AddChild(li);
            itemsBuilder.AddLogicalLineContainer(lineContainer, WritingDirectionMode.HorizontalLtr, null);
            _currentLineBlockOffset += boxLine.BlockSize;
        }

        // Floats that were laid out and positioned while breaking the lines become
        // child boxes of this block; they took no horizontal space on their line.
        foreach (var placed in lineBreaker.PlacedFloats)
            Builder.AddChild(placed.Result.Fragment);

        // Attach the flat fragment items list for hit-testing / painting.
        _fragmentItems = itemsBuilder.ToFragmentItems(data.TextContent);
        return true;
    }

    /// <summary>
    /// Builds the float machinery for this block's line boxes, or null when nothing
    /// can shorten a line box: no float of our own and none in the enclosing
    /// formatting context. Keeping the plain path for that case avoids touching the
    /// overwhelmingly common float-free layout.
    /// </summary>
    private FloatLineContext? CreateFloatLineContext(float contentInlineOrigin, float contentBlockOrigin,
        float availInline, Inline.InlineItemsData data)
    {
        bool hasOwnFloat = false;
        foreach (var item in data.Items)
        {
            if (item.Type == InlineItem.InlineItemType.Floating)
            {
                hasOwnFloat = true;
                break;
            }
        }

        var owner = _parent as BlockLayoutAlgorithm;
        var exclusionSpace = owner?.FloatExclusionSpace ?? Space.ExclusionSpace;
        if (!hasOwnFloat && (exclusionSpace == null || !exclusionSpace.HasExclusions))
            return null;

        var origin = owner != null ? owner.ContainerBfcOriginForLines() : Space.GetBfcOffset();
        if (exclusionSpace == null)
            exclusionSpace = new ExclusionSpace();

        return new FloatLineContext
        {
            Space = exclusionSpace,
            // The caller's origin already carries this box's border and padding.
            ContentLineStart = origin.LineOffset + contentInlineOrigin,
            ContentTop = origin.BlockOffset + contentBlockOrigin,
            ContentWidth = availInline,
            LineBlockSize = Fonts.LineBoxMetrics.GetLineHeight(Style),
        };
    }

    /// <summary>
    /// Place the boxes on a line by their 'vertical-align' and grow the line box to
    /// contain them (CSS 2.1 §10.6.3 and §10.8.1). The baseline stays where the
    /// parent's strut puts it; each box contributes how far it reaches above and
    /// below that baseline, and the line box becomes the union. A run is raised by
    /// writing its own baseline offset into BoxRun.BaselineShift, which the painter
    /// already honours for glyphs, backgrounds and borders.
    /// </summary>
    private void AlignLineBoxes(BoxLine boxLine, float strutHeight)
    {
        float strutAscent = Fonts.LineBoxMetrics.GetBaselineForLineHeight(Style, strutHeight);
        float strutDescent = Math.Max(0, strutHeight - strutAscent);
        var parentMetrics = Fonts.FontMetricsProvider.Get(Style.FontFamily, Style.FontSize,
            Style.FontWeight, Style.FontStyle);
        float parentAscent = parentMetrics.FloatAscent;
        float parentDescent = parentMetrics.FloatDescent;
        float xHeight = parentMetrics.XHeight > 0
            ? parentMetrics.XHeight
            : parentAscent * Fonts.FontMetricsProvider.SynthesizedXHeightRatio;

        float maxAscent = strutAscent;
        float maxDescent = strutDescent;
        var reach = new System.Collections.Generic.Dictionary<BoxRun, (float Top, float Bottom)>();
        // 'top' and 'bottom' align against the final extents of the line box, which
        // only exist once every other box has been placed, so they are applied in a
        // second pass.
        var edgeAligned = new System.Collections.Generic.List<(BoxRun Run, float BoxHeight, float BaselineFromTop, bool ToTop)>();

        foreach (var run in boxLine.Runs)
        {
            var runStyle = RunStyle(run);

            // A text box's own height is its line-height; an atomic inline uses its
            // border box (CSS 2.1 §10.6.3).
            float boxHeight = run.IsAtomicInline && run.BlockSize > 0
                ? run.BlockSize
                : Fonts.LineBoxMetrics.GetLineHeight(runStyle);
            // Half the leading sits above the baseline, exactly like the strut does,
            // so a box that needs no shift contributes its own height and never
            // grows the line box.
            float baselineFromTop = run.IsAtomicInline
                ? boxHeight
                : Fonts.LineBoxMetrics.GetBaselineForLineHeight(runStyle, boxHeight);

            // The box's top, measured above the line's baseline.
            float top = baselineFromTop;
            switch (runStyle.VerticalAlign)
            {
                case VerticalAlignType.Middle:
                    top = boxHeight / 2f + xHeight / 2f;
                    break;
                case VerticalAlignType.TextTop:
                    top = parentAscent;
                    break;
                case VerticalAlignType.TextBottom:
                    top = boxHeight - parentDescent;
                    break;
                case VerticalAlignType.Sub:
                    top = baselineFromTop - (Style.FontSize / 5f + 1f);
                    break;
                case VerticalAlignType.Super:
                    top = baselineFromTop + (Style.FontSize / 3f + 1f);
                    break;
                case VerticalAlignType.Percentage:
                case VerticalAlignType.Length:
                    {
                        // Percentages resolve against the box's own line-height; a
                        // positive value raises the box (CSS 2.1 §10.8.1).
                        float offset = runStyle.VerticalAlign == VerticalAlignType.Percentage
                            ? (runStyle.VerticalAlignOffsetPx ?? 0) * boxHeight
                            : runStyle.VerticalAlignOffsetPx ?? 0;
                        top = baselineFromTop + offset;
                        break;
                    }
                case VerticalAlignType.Top:
                case VerticalAlignType.Bottom:
                    // Not part of the "aligned subtree" whose extents they align to,
                    // so they take no share in computing it.
                    edgeAligned.Add((run, boxHeight, baselineFromTop,
                        runStyle.VerticalAlign == VerticalAlignType.Top));
                    continue;
            }

            float bottom = Math.Max(0, boxHeight - top);
            ApplyRunReach(run, reach, top, bottom, baselineFromTop);
            maxAscent = Math.Max(maxAscent, top);
            maxDescent = Math.Max(maxDescent, bottom);
        }

        foreach (var (run, boxHeight, baselineFromTop, toTop) in edgeAligned)
        {
            float top = toTop ? maxAscent : boxHeight - maxDescent;
            float bottom = Math.Max(0, boxHeight - top);
            ApplyRunReach(run, reach, top, bottom, baselineFromTop);
            maxAscent = Math.Max(maxAscent, top);
            maxDescent = Math.Max(maxDescent, bottom);
        }

        float lineHeight = maxAscent + maxDescent;
        if (lineHeight <= boxLine.BlockSize + 0.01f)
            return;

        boxLine.BlockSize = lineHeight;
        boxLine.BaselineOffset = boxLine.BlockOffset + maxAscent;
        foreach (var run in boxLine.Runs)
        {
            if (!run.IsAtomicInline || !reach.TryGetValue(run, out var r))
                continue;
            run.BlockOffset = boxLine.BlockOffset + maxAscent - r.Top;
        }
    }

    /// <summary>Record where one box ended up on its line: how far its top and
    /// bottom reach from the line baseline, and the resulting baseline shift (the
    /// painter raises the glyphs, background and border by that amount).</summary>
    private static void ApplyRunReach(BoxRun run,
        System.Collections.Generic.Dictionary<BoxRun, (float Top, float Bottom)> reach,
        float top, float bottom, float baselineFromTop)
    {
        run.BaselineShift = top - baselineFromTop;
        reach[run] = (top, bottom);
    }

    /// <summary>The style a run was laid out with: its own element's, falling back
    /// to the container's (the per-run font fields already carry the pseudo overrides).</summary>
    private ComputedStyle RunStyle(BoxRun run)
    {
        var element = run.Element ?? (run.Node as Element) ?? (run.Node as TextNode)?.ParentElement;
        var style = element?.ComputedStyle;
        if (style == null)
            return Style;
        // The ::first-line / ::first-letter merge is not stored on the element, so
        // rebuild it from the fields the run already carries.
        if (run.FontSize != null && Math.Abs(style.FontSize - run.FontSize.Value) > 0.01f)
        {
            var merged = style.Clone();
            merged.FontSize = run.FontSize.Value;
            return merged;
        }
        return style;
    }

    /// <summary>
    /// Enlarge a line box so atomic inlines that are taller than the text strut
    /// fit, and place each atomic run according to its 'vertical-align'. Only the
    /// common cases (baseline, top, middle, bottom) are handled; anything else
    /// falls back to baseline.
    /// </summary>
    private void AdjustLineForAtomicInlines(BoxLine boxLine, float strutHeight)
    {
        float maxAtomicHeight = 0;
        bool hasAtomic = false;
        foreach (var run in boxLine.Runs)
        {
            if (run.IsAtomicInline)
            {
                hasAtomic = true;
                if (run.BlockSize > maxAtomicHeight) maxAtomicHeight = run.BlockSize;
            }
        }
        if (!hasAtomic) return;

        float strutAscent = Fonts.LineBoxMetrics.GetBaselineForLineHeight(Style, strutHeight);
        float strutDescent = strutHeight - strutAscent;

        // A baseline-aligned box needs `height` above the baseline. The line's
        // ascent is the greater of the text ascent and the tallest such box.
        float lineAscent = Math.Max(strutAscent, maxAtomicHeight);
        float lineHeight = lineAscent + strutDescent;

        float lineTopToBaseline = lineAscent;
        boxLine.BlockSize = lineHeight;
        boxLine.BaselineOffset = boxLine.BlockOffset + lineTopToBaseline;

        // Reposition every run relative to the (possibly moved) baseline.
        foreach (var run in boxLine.Runs)
        {
            if (!run.IsAtomicInline)
                continue;

            var valign = run.Element?.ComputedStyle?.VerticalAlign ?? VerticalAlignType.Baseline;
            float top = valign switch
            {
                VerticalAlignType.Top => 0f,
                VerticalAlignType.Bottom => lineHeight - run.BlockSize,
                VerticalAlignType.Middle => (lineHeight - run.BlockSize) / 2f,
                // baseline / text-top / text-bottom / sub / super fall back to
                // sitting the box bottom on the baseline.
                _ => lineTopToBaseline - run.BlockSize,
            };
            run.BlockOffset = boxLine.BlockOffset + top;
        }
    }

    private static bool _measuringIntrinsics;
    [ThreadStatic] private static bool _sMinContentProbe;

    /// <summary>True while a min-content intrinsic measurement is in flight.</summary>
    public static bool InMinContentProbe => _sMinContentProbe;

    /// <summary>Enters/leaves the min-content probe state for measurement passes
    /// that do not go through <see cref="MeasureIntrinsicInlineSize"/>.</summary>
    public static void SetMinContentProbe(bool value) => _sMinContentProbe = value;

    /// <summary>Runs a throwaway inline pass to measure the content's intrinsic
    /// inline size: unconstrained (max-content) or squeezed to 1px (min-content,
    /// where every break opportunity is taken).</summary>
    private float MeasureIntrinsicInlineSize(bool minContent, BoxStrut bp)
    {
        if (_measuringIntrinsics) return float.NaN;
        float probe = minContent ? 1f : 100000f;
        var space = Space.InheritBuilder(probe, float.PositiveInfinity).ToConstraintSpace();
        _measuringIntrinsics = true;
        bool previousProbe = _sMinContentProbe;
        _sMinContentProbe = minContent;
        try
        {
            var measure = new InlineLayoutAlgorithm(Node, space, _parent);
            measure.Layout();
            float widest = 0;
            foreach (var l in measure._lines)
                widest = Math.Max(widest, l.InlineSize);
            return widest + bp.HorizontalSum;
        }
        finally
        {
            _sMinContentProbe = previousProbe;
            _measuringIntrinsics = false;
        }
    }

    /// <summary>
    /// Baseline shift (positive = raised) for a text run whose element declares a
    /// non-baseline vertical-align. Font-metric approximations per CSS 2.1 §10.8.1:
    /// middle raises by half the parent x-height, text-top/bottom align the em-box
    /// edges, sub/super use the conventional 1/5 / 1/3 parent-font offsets.
    /// </summary>
    private float ComputeVerticalAlignShift(Node? node, float runBlockSize)
    {
        var elStyle = (node as Element)?.ComputedStyle ?? (node as TextNode)?.ParentElement?.ComputedStyle;
        if (elStyle == null) return 0;
        float parentFs = Style.FontSize;
        float runFs = elStyle.FontSize > 0 ? elStyle.FontSize : parentFs;
        return elStyle.VerticalAlign switch
        {
            VerticalAlignType.Middle => parentFs * 0.25f,
            VerticalAlignType.TextTop => 0.8f * (parentFs - runFs),
            VerticalAlignType.TextBottom => 0.2f * (runFs - parentFs),
            VerticalAlignType.Sub => -parentFs / 5f,
            VerticalAlignType.Super => parentFs / 3f,
            _ => 0f,
        };
    }

    private BoxLine ProcessText(TextNode textNode, BoxLine currentLine, float availInline,
        ref float curInlineSize, ref float curBlockSize, ref float curBaseline, ref float maxBlockSize)
    {
        var text = textNode.Data ?? "";
        if (string.IsNullOrEmpty(text)) return currentLine;

        var style = textNode.ParentElement?.ComputedStyle;
        float fontSize = style?.FontSize ?? 16;
        float charWidth = fontSize * 0.5f;
        var textStrut = Fonts.LineBoxMetrics.GetStrut(style);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n' || (c == ' ' && DoesLineFit(text, i, charWidth, curInlineSize, availInline)))
            {
                if (curInlineSize > 0)
                {
                    currentLine.InlineSize = curInlineSize; currentLine.BlockSize = curBlockSize;
                    currentLine.BaselineOffset = curBaseline; _lines.Add(currentLine);
                }
                curInlineSize = 0; curBlockSize = 0; curBaseline = 0;
                _currentLineBlockOffset += Math.Max(fontSize, maxBlockSize);
                currentLine = new BoxLine { InlineOffset = _currentLineInlineOffset, BlockOffset = _currentLineBlockOffset };
                maxBlockSize = 0;
                if (c == '\n') continue;
            }

            float w = c == ' ' ? charWidth * 0.5f : charWidth;
            if (curInlineSize + w > availInline && curInlineSize > 0)
            {
                currentLine.InlineSize = curInlineSize; currentLine.BlockSize = curBlockSize;
                currentLine.BaselineOffset = curBaseline; _lines.Add(currentLine);
                curInlineSize = 0; curBlockSize = 0; curBaseline = 0;
                _currentLineBlockOffset += Math.Max(fontSize, maxBlockSize);
                currentLine = new BoxLine { InlineOffset = _currentLineInlineOffset, BlockOffset = _currentLineBlockOffset };
                maxBlockSize = 0;
            }

            curInlineSize += w;
            curBlockSize = Math.Max(curBlockSize, textStrut.LineHeight);
            curBaseline = textStrut.Ascent;
            maxBlockSize = Math.Max(maxBlockSize, textStrut.LineHeight);

            currentLine.Runs.Add(new BoxRun
            {
                Text = c.ToString(),
                InlineOffset = curInlineSize - w,
                InlineSize = w,
                BlockOffset = _currentLineBlockOffset,
                BlockSize = textStrut.LineHeight,
                BaselineOffset = textStrut.Ascent,
                IsBreakOpportunity = c == ' '
            });
        }
        return currentLine;
    }

    private BoxLine ProcessElement(Element child, BoxLine currentLine, float availInline,
        ref float curInlineSize, ref float curBlockSize, ref float curBaseline, ref float maxBlockSize)
    {
        var style = child.ComputedStyle;
        if (style == null || style.Display == DisplayType.None) return currentLine;

        float fs = style.FontSize;
        float w = style.Width is PixelLength pl ? pl.Value : 20;
        float h = style.Height is PixelLength ph ? ph.Value : fs;

        if (curInlineSize + w > availInline && curInlineSize > 0)
        {
            currentLine.InlineSize = curInlineSize; currentLine.BlockSize = curBlockSize;
            currentLine.BaselineOffset = curBaseline; _lines.Add(currentLine);
            curInlineSize = 0; curBlockSize = 0; curBaseline = 0;
            _currentLineBlockOffset += Math.Max(fs, maxBlockSize);
            currentLine = new BoxLine { InlineOffset = _currentLineInlineOffset, BlockOffset = _currentLineBlockOffset };
            maxBlockSize = 0;
        }

        curInlineSize += w; curBlockSize = Math.Max(curBlockSize, h);
        // An atomic inline sits on the line with its bottom margin edge on the
        // baseline (CSS 2.1 10.8.1), so its baseline offset is its own height.
        curBaseline = Math.Max(curBaseline, h); maxBlockSize = Math.Max(maxBlockSize, h);

        currentLine.Runs.Add(new BoxRun
        {
            Element = child, InlineOffset = curInlineSize - w, InlineSize = w,
            BlockOffset = _currentLineBlockOffset, BlockSize = h, IsAtomicInline = true
        });
        return currentLine;
    }

    private static bool DoesLineFit(string text, int pos, float charWidth, float curInline, float avail) =>
        pos > 0 && text[pos - 1] != ' ' && curInline + charWidth > avail;

    /// <summary>
    /// Places a block-level box nested inside an inline formatting context.
    /// Mirrors InlineLayoutAlgorithm::PlaceBlockInInline().
    /// </summary>
    public void PlaceBlockInInline(InlineItem item, InlineItemResult itemResult, LogicalLineItems lineBox)
    {
        lineBox.AddChild(item.GetLayoutObject(), item.BidiLevel, itemResult.Start());
    }

    private float fontSize => Style.FontSize;
}