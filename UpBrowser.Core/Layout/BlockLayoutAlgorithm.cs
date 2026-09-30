using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;
using UpBrowser.Core.Layout.Inline;
using System.Linq;

namespace UpBrowser.Core.Layout;

// ============================================================================
// Supporting types. These mirror block_layout_algorithm.h / layout_result.h
// enums and structs that the port needs but which have no equivalent in the
// engine's simplified world yet.
// ============================================================================

// BreakStatus is declared in ColumnLayoutAlgorithm.cs.

/// <summary>
/// Communicates to a child the position of the previous in-flow child. This is
/// used to calculate the position of the next child.
/// Mirrors PreviousInflowPosition in block_layout_algorithm.h.
/// </summary>
public struct PreviousInflowPosition
{
    public float logical_block_offset;
    public MarginStrut margin_strut;

    // > 0: Block-end annotation space of the previous line
    // < 0: Block-end annotation overflow of the previous line
    public float block_end_annotation_space;
    public bool self_collapsing_child_had_clearance;

    public PreviousInflowPosition(float logicalBlockOffset, MarginStrut marginStrut, float blockEndAnnotationSpace,
        bool selfCollapsingChildHadClearance)
    {
        logical_block_offset = logicalBlockOffset;
        margin_strut = marginStrut;
        block_end_annotation_space = blockEndAnnotationSpace;
        self_collapsing_child_had_clearance = selfCollapsingChildHadClearance;
    }
}

/// <summary>
/// Holds information for the current in-flow child. The data is not useful
/// outside of handling this single in-flow child.
/// Mirrors InflowChildData in block_layout_algorithm.h.
/// </summary>
public struct InflowChildData
{
    public BfcOffset bfc_offset_estimate;
    public MarginStrut margin_strut;
    public BoxStrut margins;
    public bool is_pushed_by_floats;

    public InflowChildData(BfcOffset bfcOffsetEstimate, MarginStrut marginStrut, BoxStrut margins, bool isPushedByFloats = false)
    {
        this.bfc_offset_estimate = bfcOffsetEstimate;
        this.margin_strut = marginStrut;
        this.margins = margins;
        this.is_pushed_by_floats = isPushedByFloats;
    }
}

/// <summary>
/// Line clamp state for a block. Mirrors BlockLineClampData in
/// block_layout_algorithm.h (the fields and relayout decisions are ported;
/// the engine style hooks are disabled, so the clamp relayouts never trigger).
/// </summary>
public class BlockLineClampData
{
    public LineClampData data;
    public int initial_lines_until_clamp;
    public MarginStrut end_margin_strut;
    public PreviousInflowPosition? previous_inflow_position_when_clamped;

    /// <summary>CSSLineClamp is not enabled in the engine; kept as a port surface.</summary>
    public const bool CssLineClampEnabled = false;

    public BlockLineClampData(LineClampData lineClampData)
    {
        data = lineClampData;
        if (data.CurrentState == LineClampData.ClampState.ClampByLines)
            initial_lines_until_clamp = data.LinesUntilClamp;
    }

    public int? LinesUntilClamp(bool showMeasuredLines = false)
    {
        if (data.CurrentState == LineClampData.ClampState.ClampByLines ||
            (showMeasuredLines && data.CurrentState == LineClampData.ClampState.MeasureLinesUntilBfcOffset))
            return data.LinesUntilClamp;
        if (data.CurrentState == LineClampData.ClampState.MeasureLinesUntilBfcOffset)
            return data.LinesUntilClamp;
        return null;
    }

    public bool IsPastClampPoint() => data.IsPastClampPoint;
    public bool ShouldHideForPaint() => data.ShouldHideForPaint;

    public bool ShouldRelayoutWithNoForcedTruncate()
    {
        if (!previous_inflow_position_when_clamped.HasValue)
            return false;
        return data.CurrentState == LineClampData.ClampState.ClampByLines && data.LinesUntilClamp == 0;
    }

    public void UpdateClampOffsetFromStyle(float clampBfcOffset, float contentEdge)
    {
        if (data.CurrentState == LineClampData.ClampState.DontTruncate)
            return;
        if (data.CurrentState == LineClampData.ClampState.MeasureLinesUntilBfcOffset)
            return;
        if (data.CurrentState == LineClampData.ClampState.Disabled)
        {
            if (float.IsNaN(clampBfcOffset))
            {
                data.CurrentState = LineClampData.ClampState.DontTruncate;
            }
            else
            {
                data.CurrentState = LineClampData.ClampState.MeasureLinesUntilBfcOffset;
                data.LinesUntilClamp = 0;
                data.ClampBfcOffset = clampBfcOffset;
            }
        }
    }

    public void UpdateLinesFromStyle(int linesUntilClamp)
    {
        if (data.CurrentState == LineClampData.ClampState.DontTruncate)
            return;
        data.CurrentState = LineClampData.ClampState.ClampByLines;
        data.LinesUntilClamp = linesUntilClamp;
    }

    /// <summary>Returns false if we need to relayout with a different clamp BFC offset.</summary>
    public bool UpdateAfterLayout(LayoutResult layoutResult, float bfcBlockOffset, PreviousInflowPosition previousInflowPosition,
        float blockEndPadding)
    {
        if (data.CurrentState == LineClampData.ClampState.ClampByLines)
        {
            if (!previous_inflow_position_when_clamped.HasValue && IsPastClampPoint())
                previous_inflow_position_when_clamped = previousInflowPosition;
        }

        if (data.CurrentState == LineClampData.ClampState.MeasureLinesUntilBfcOffset)
        {
            // We compute the margin strut we'd have after this block if we were to
            // clamp here.
            MarginStrut collapsedStrut = previousInflowPosition.margin_strut;
            collapsedStrut.positive_margin = Math.Max(collapsedStrut.positive_margin, end_margin_strut.positive_margin);
            collapsedStrut.quirky_positive_margin = Math.Max(collapsedStrut.quirky_positive_margin, end_margin_strut.quirky_positive_margin);
            collapsedStrut.negative_margin = Math.Min(collapsedStrut.negative_margin, end_margin_strut.negative_margin);

            float paddingAnnotationOverflow = 0;
            if (previousInflowPosition.block_end_annotation_space < 0)
                paddingAnnotationOverflow = Math.Max(previousInflowPosition.block_end_annotation_space, -blockEndPadding);

            float bfcOffset = bfcBlockOffset + previousInflowPosition.logical_block_offset + paddingAnnotationOverflow
                + (collapsedStrut.Sum - end_margin_strut.Sum);

            if (bfcOffset > data.ClampBfcOffset)
                return false;
        }

        return true;
    }
}

// ============================================================================
// BlockLayoutAlgorithm: a faithful port of the modern layout pipeline's
// block layout algorithm.
// ============================================================================

/// <summary>
/// A class for general block layout (e.g. a &lt;div&gt; with no special style).
/// Lays out the children in sequence, handling margin collapsing, clearance,
/// floats, out-of-flow positioning, block formatting contexts and (in
/// structure) block fragmentation and line clamp.
///
/// This is a faithful port of the modern layout pipeline's block layout
/// algorithm, adapted to this engine's simplified foundation (float
/// LayoutUnit, Physical geometry, Element-based children).
/// </summary>
public class BlockLayoutAlgorithm : LayoutAlgorithm
{
    // --- Anonymous-namespace helpers from the .cc (ported as private statics). ---

    private static bool HasLineEvenIfEmpty(Element box)
    {
        if (box.ComputedStyle == null)
            return false;
        // The engine content-editable / button-with-empty-label concept is not
        // modeled; return false so empty blocks get zero block-size.
        return false;
    }

    private static bool IsLastInflowChild(Element box)
    {
        foreach (var node in box.ParentElement?.Children ?? (IEnumerable<Node>)Array.Empty<Node>())
        {
            // Skip this node itself.
            if (ReferenceEquals(node, box))
                continue;
            if (node is Element next)
            {
                var s = next.ComputedStyle;
                if (s == null || s.Display == DisplayType.None)
                    continue;
                if (s.Float != FloatType.None || s.Position is PositionType.Absolute or PositionType.Fixed)
                    continue;
                return false;
            }
        }
        return true;
    }

    private LayoutResult LayoutBlockChild(ConstraintSpace space, BreakToken? breakToken, Element child)
    {
        var s = child.ComputedStyle!;
        if (IsReplacedElement(child))
            return new ReplacedLayoutAlgorithm(child, space).Layout();

        var disp = s.Display;
        if (disp is DisplayType.Flex or DisplayType.InlineFlex)
            return new FlexLayoutAlgorithm(child, space).Layout();
        if (disp is DisplayType.Grid or DisplayType.InlineGrid)
            return new GridLayoutAdapter(child, space).Layout();
        if (disp == DisplayType.Table)
            return new Table.TableLayoutAlgorithm(child, space, this).Layout();
        // Multicol: ColumnLayoutAlgorithm distributes the flow's lines across
        // columns (self-contained; relies on the line-breaker overflow fix for
        // correct wrapping at column width).
        if (HasMulticolStyle(child))
            return new ColumnLayoutAlgorithm(child, space).Layout();
        if (child.TagName == "FIELDSET")
            return new FieldsetLayoutAlgorithm(child, space).Layout();
        return new BlockLayoutAlgorithm(child, space).Layout();
    }

    /// <summary>
    /// Lay out an atomic inline (inline-block / inline-flex / inline-grid /
    /// replaced element) as an independent formatting context, choosing the
    /// algorithm by display type just like <see cref="LayoutBlockChild"/>. The
    /// inline layout code calls this to obtain the atomic inline's border-box
    /// size and baseline so it occupies real space on the line.
    /// </summary>
    public static LayoutResult LayoutAtomicInlineRoot(Element child, ConstraintSpace space)
    {
        var s = child.ComputedStyle;
        if (s == null)
            return LayoutResult.Abort(EStatus.Success);

        if (IsReplacedElement(child))
            return new ReplacedLayoutAlgorithm(child, space).Layout();

        var disp = s.Display;
        if (disp is DisplayType.Flex or DisplayType.InlineFlex)
            return new FlexLayoutAlgorithm(child, space).Layout();
        if (disp is DisplayType.Grid or DisplayType.InlineGrid)
            return new GridLayoutAdapter(child, space).Layout();
        if (disp is DisplayType.Table)
            return new Table.TableLayoutAlgorithm(child, space).Layout();
        if (HasMulticolStyle(child))
            return new ColumnLayoutAlgorithm(child, space).Layout();
        if (child.TagName == "FIELDSET")
            return new FieldsetLayoutAlgorithm(child, space).Layout();
        return new BlockLayoutAlgorithm(child, space).Layout();
    }

    private LayoutResult LayoutInflowChild(ConstraintSpace space, BreakToken? breakToken, Element child)
    {
        if (IsInlineLevelChild(child))
            return new InlineLayoutAlgorithm(child, space, this).Layout();
        return LayoutBlockChild(space, breakToken, child);
    }

    private static AdjoiningObjectTypes ToAdjoiningObjectTypes(ClearType clear)
    {
        return clear switch
        {
            ClearType.None => AdjoiningObjectTypes.None,
            ClearType.Left => AdjoiningObjectTypes.FloatLeft,
            ClearType.Right => AdjoiningObjectTypes.FloatRight,
            ClearType.Both => AdjoiningObjectTypes.FloatLeft | AdjoiningObjectTypes.FloatRight,
            _ => AdjoiningObjectTypes.None,
        };
    }

    /// <summary>
    /// Return true if a child is to be cleared past adjoining floats.
    /// </summary>
    private static bool HasClearancePastAdjoiningFloats(AdjoiningObjectTypes adjoiningObjectTypes, ComputedStyle childStyle,
        ComputedStyle cbStyle)
    {
        if (adjoiningObjectTypes == AdjoiningObjectTypes.None)
            return false;
        var clear = ToAdjoiningObjectTypes(childStyle.Clear);
        return ((uint)clear & (uint)adjoiningObjectTypes) != 0;
    }

    /// <summary>
    /// Adjust BFC block offset for clearance, if applicable. Return true if
    /// clearance was applied.
    /// </summary>
    private static bool ApplyClearance(ConstraintSpace constraintSpace, ref float bfcBlockOffset)
    {
        if (constraintSpace.HasClearanceOffset && bfcBlockOffset < constraintSpace.ClearanceOffset)
        {
            bfcBlockOffset = constraintSpace.ClearanceOffset;
            return true;
        }
        return false;
    }

    private static float LogicalFromBfcLineOffset(float childBfcLineOffset, float parentBfcLineOffset, float childInlineSize,
        float parentInlineSize, TextDirection direction)
    {
        float relativeLineOffset = childBfcLineOffset - parentBfcLineOffset;
        return direction == TextDirection.Ltr
            ? relativeLineOffset
            : parentInlineSize - relativeLineOffset - childInlineSize;
    }

    private static LogicalOffset LogicalFromBfcOffsets(BfcOffset childBfcOffset, BfcOffset parentBfcOffset, float childInlineSize,
        float parentInlineSize, TextDirection direction)
    {
        float inlineOffset = LogicalFromBfcLineOffset(childBfcOffset.LineOffset, parentBfcOffset.LineOffset,
            childInlineSize, parentInlineSize, direction);
        return new LogicalOffset(inlineOffset, childBfcOffset.BlockOffset - parentBfcOffset.BlockOffset);
    }

    private static float LineLeft(BoxStrut margins, TextDirection direction) => direction == TextDirection.Ltr ? margins.Left : margins.Right;
    private static float LineRight(BoxStrut margins, TextDirection direction) => direction == TextDirection.Ltr ? margins.Right : margins.Left;

    private static bool IsRtl(TextDirection direction) => direction == TextDirection.Rtl;

    private static bool IsZeroLength(Length? length) => length is PixelLength pl && pl.Value == 0;

    private static bool IsInParallelFlow(BreakToken? token)
    {
        return token switch
        {
            BlockBreakToken b => b.IsInParallelFlow,
            Inline.InlineBreakToken i => i.IsInParallelBlockFlow,
            _ => false,
        };
    }

    private static TextDirection StyleTextDirection(ComputedStyle style) =>
        string.Equals(style.Direction, "rtl", StringComparison.OrdinalIgnoreCase) ? TextDirection.Rtl : TextDirection.Ltr;

    private static BoxStrut CombineStruts(BoxStrut a, BoxStrut b) =>
        new(a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom, a.Left + b.Left);

    /// <summary>
    /// Handle auto margins. Mirrors ResolveInlineAutoMargins from
    /// margin_utils (used in HandleNewFormattingContext / CalculateMargins).
    /// Auto margins contribute zero inline size while being resolved.
    /// </summary>
    private static void ResolveInlineAutoMargins(ComputedStyle childStyle, ComputedStyle style, float availableSpace, float childInlineSize,
        ref BoxStrut margins)
    {
        bool autoL = childStyle.MarginLeft is AutoLength;
        bool autoR = childStyle.MarginRight is AutoLength;

        // Auto margins start at zero while resolving.
        float extraSpace = availableSpace - childInlineSize;

        if (autoL && autoR)
        {
            float half = Math.Max(0, extraSpace) / 2f;
            margins = new BoxStrut(margins.Top, half, margins.Bottom, half);
        }
        else if (autoL)
        {
            margins = new BoxStrut(margins.Top, margins.Right, margins.Bottom, Math.Max(0, extraSpace));
        }
        else if (autoR)
        {
            margins = new BoxStrut(margins.Top, Math.Max(0, extraSpace), margins.Bottom, margins.Left);
        }
    }

    private static float WebkitTextAlignAndJustifySelfOffset(
        ComputedStyle childStyle, ComputedStyle style, float availableSpace, BoxStrut margins,
        Func<float> childInlineSizeFunc)
    {
        // justify-self for block layout is not modeled by the engine; only plain
        // text-align -webkit-* values were supported and they layout to start.
        switch (style.TextAlign)
        {
            case TextAlignType.Center:
                {
                    float freeSpace = Math.Max(0, availableSpace - childInlineSizeFunc() - margins.HorizontalSum);
                    return freeSpace / 2f;
                }
            case TextAlignType.Right:
            case TextAlignType.End:
                {
                    float freeSpace = Math.Max(0, availableSpace - childInlineSizeFunc() - margins.HorizontalSum);
                    return freeSpace;
                }
            default:
                return 0;
        }
    }

    // --- Algorithm state (mirrors the data members of the header). ---

    private const bool CssLineClampEnabled = false;
    private const bool LayoutJustifySelfForBlocksEnabled = true;

    private readonly BoxStrut _borderPadding;
    private readonly BoxStrut _border;
    private readonly BoxStrut _padding;
    private readonly LogicalSize _childPercentageSize;
    private readonly LogicalSize _replacedChildPercentageSize;

    private readonly List<OutOfFlowChildCandidate> _oofCandidates = new();
    private ExclusionSpace _exclusionSpace = new();
    private MarginStrut _endMarginStrut = MarginStrut.Zero;
    private AdjoiningObjectTypes _adjoiningObjectTypes = AdjoiningObjectTypes.None;
    private bool _hasAdjoiningObjectDescendants;
    private bool _isPushedByFloats;

    // Current float line (CSS 2.1 §9.5.1): consecutive floats that start at the
    // bottom of the previous float join this line side by side.
    private bool _subtreeModifiedMarginStrut;

    private float? _containerBfcBlockOffset;
    private float _containerBfcLineOffset;
    private float _intrinsicBlockSize;
    private float _inlineSize;
    private float _blockSize;

    private readonly BlockLineClampData _lineClampData;
    private int _firstOverflowingLine;
    private bool _fitAllLines;
    private bool _isResuming;
    private bool _abortWhenBfcBlockOffsetUpdated;
    private bool _hasBreakOpportunityBeforeNextChild;

    private bool _shouldTextBoxTrimNodeStart;
    private bool _shouldTextBoxTrimNodeEnd;
    private bool _shouldTextBoxTrimFragmentainerStart;
    private bool _shouldTextBoxTrimFragmentainerEnd;
    private InlineNode? _lastNonEmptyInflowChild;
    private BreakToken? _lastNonEmptyBreakToken;

    private Element? _placeholderChild;

    private BreakToken? _breakToken;
    private readonly bool _hasSeenAllChildren;

    private float? _firstBaseline;
    private float? _lastBaseline;

    public BlockLayoutAlgorithm(Element node, in ConstraintSpace space)
        : this(node, space, null)
    {
    }

    public BlockLayoutAlgorithm(Element node, in ConstraintSpace space, BreakToken? breakToken)
        : base(node, space)
    {
        // A collapsing table splits every grid line between its two cells, so the
        // cell's box is built from half-borders that only the table algorithm can
        // resolve (CSS 2.1 §17.6.2); it passes them down through the space.
        // A collapsing table splits every grid line between its two cells, so the
        // cell's box is built from the half-borders the table algorithm resolved.
        _border = OwnBorders;
        _padding = ComputePadding();
        _borderPadding = new BoxStrut(_border.Top + _padding.Top, _border.Right + _padding.Right,
            _border.Bottom + _padding.Bottom, _border.Left + _padding.Left);

        _childPercentageSize = new LogicalSize(ChildAvailableInlineSize, ChildAvailableBlockSize);
        _replacedChildPercentageSize = new LogicalSize(ChildAvailableInlineSize, ChildAvailableBlockSize);

        // Resuming from a break token means this is a continuation fragment (a
        // later column/page). Block fragmentation uses it to skip the content
        // already placed in earlier fragmentainers.
        _breakToken = breakToken;
        _isResuming = breakToken is BlockBreakToken bt && !bt.IsBreakBefore && !bt.IsRepeated;
        _hasSeenAllChildren = true;

        _shouldTextBoxTrimNodeStart = false;
        _shouldTextBoxTrimNodeEnd = false;
        _shouldTextBoxTrimFragmentainerStart = false;
        _shouldTextBoxTrimFragmentainerEnd = false;

        // Seed the exclusion space from the constraint space (mirrors
        // container_builder_.SetExclusionSpace(params.space.GetExclusionSpace())).
        _exclusionSpace = Space.ExclusionSpace?.Copy() ?? new ExclusionSpace();
        _lineClampData = new BlockLineClampData(new LineClampData { CurrentState = LineClampData.ClampState.Disabled });
    }

    private BoxStrut ComputePadding()
    {
        float font = Style.FontSize;
        using var _scope = FontUnitContext.Use(Style);
        return new BoxStrut(
            Style.PaddingTop.ToPixels(font, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight),
            Style.PaddingRight.ToPixels(font, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight),
            Style.PaddingBottom.ToPixels(font, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight),
            Style.PaddingLeft.ToPixels(font, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight));
    }

    private ConstraintSpace ConstraintSpaceForLayout() => Space;
    private Element NodeElement() => Node;

    private void SetupRelayoutData(BlockLayoutAlgorithm previous, int relayoutType)
    {
        _isPushedByFloats = previous._isPushedByFloats;

        if (relayoutType == 0 /* kRelayoutIgnoringLineClamp */)
        {
            _lineClampData.data.CurrentState = LineClampData.ClampState.DontTruncate;
        }
        else if (relayoutType == 1 /* kRelayoutWithLineClampBlockSize */)
        {
            _lineClampData.data.CurrentState = LineClampData.ClampState.ClampByLines;
            _lineClampData.data.LinesUntilClamp = _lineClampData.initial_lines_until_clamp = previous._lineClampData.data.LinesUntilClamp;
        }
        else if (previous._lineClampData.data.CurrentState == LineClampData.ClampState.ClampByLines)
        {
            _lineClampData.data.CurrentState = LineClampData.ClampState.ClampByLines;
            _lineClampData.data.LinesUntilClamp = _lineClampData.initial_lines_until_clamp = previous._lineClampData.initial_lines_until_clamp;
        }
        else if (previous._lineClampData.data.CurrentState == LineClampData.ClampState.DontTruncate)
        {
            _lineClampData.data.CurrentState = LineClampData.ClampState.DontTruncate;
        }
    }

    public void SetBoxType(PhysicalFragment.BoxType type)
    {
        // Fragment box types aren't modeled on the engine's flat fragment; kept
        // for API parity with the layout engine's algorithm.
    }

    /// <summary>
    /// TODO(mstensho/ikilpatrick): port the full min/max contribution algorithm.
    /// The engine computes both bounds from an actual layout pass.
    /// </summary>
    public MinMaxSizesResult ComputeMinMaxSizes(MinMaxSizesFloatInput floatInput)
    {
        float minContent = 0;
        float maxContent = 0;

        foreach (var node in Node.Children)
        {
            if (node is not Element child)
                continue;
            var s = child.ComputedStyle;
            if (s == null || s.Display == DisplayType.None)
                continue;
            if (IsOutOfFlowPositionedChild(child))
                continue;

            float childInline;
            if (s.Width is PixelLength pw)
                childInline = pw.Value + BorderPaddingFor(child).HorizontalSum;
            else if (s.Width is PercentLength pct)
                childInline = pct.Value * ChildAvailableInlineSize + BorderPaddingFor(child).HorizontalSum;
            else
                childInline = ChildAvailableInlineSize;

            maxContent = Math.Max(maxContent, childInline);
            minContent = Math.Max(minContent, childInline);
        }

        var sizes = new MinMaxSizes(minContent + _borderPadding.HorizontalSum, maxContent + _borderPadding.HorizontalSum);
        return new MinMaxSizesResult(sizes, /* depends_on_block_constraints */ false);
    }

    private static BoxStrut BorderPaddingFor(Element child)
    {
        var s = child.ComputedStyle!;
        var border = LengthUtils.ComputeBorders(s);
        return new BoxStrut(border.Top, border.Right, border.Bottom, border.Left);
    }

    // ==========================================================================
    // Entry points. Mirrors Layout() / Layout(InlineChildLayoutContext).
    // ==========================================================================

    public override LayoutResult Layout()
    {
        if (Node.IsInlineFormattingContextRoot())
        {
            var result = LayoutInlineChild(Node);
            if (result.Status == EStatus.Success)
                return MaybeRelayoutForScrollbarSpace(result);
            return HandleNonsuccessfulLayoutResult(result);
        }

        var r = LayoutMain();
        r = MaybeRelayoutForScrollbarSpace(r);
        if (r.Status != EStatus.Success)
            return HandleNonsuccessfulLayoutResult(r);
        return r;
    }

    /// <summary>
    /// Classic-scrollbar placeholder (mirrors the engine's scrollbar space in
    /// ConstraintSpace + NeedsRelayoutWithNoChildScrollbarChanges): the first
    /// pass lays out without reserving bar space; if the content overflows (or
    /// overflow:scroll forces) a classic scrollbar, re-flow once with the bar
    /// thickness subtracted from the children's available inline size so
    /// content no longer flows underneath the scrollbar.
    /// </summary>
    private LayoutResult MaybeRelayoutForScrollbarSpace(LayoutResult r)
    {
        if (r.Status != EStatus.Success || Space.HasScrollbarSpaceReserved)
            return r;

        var style = Style;
        bool axisY = style.OverflowY is OverflowType.Scroll or OverflowType.Auto
            || style.Overflow is OverflowType.Scroll or OverflowType.Auto;
        bool axisX = style.OverflowX is OverflowType.Scroll or OverflowType.Auto
            || style.Overflow is OverflowType.Scroll or OverflowType.Auto;
        if (!axisY && !axisX)
            return r;

        float thickness = Dom.ScrollbarMetrics.ThicknessFor(style);
        if (thickness <= 0 || !Space.HasDefiniteInlineSize)
            return r;

        bool forceV = style.OverflowY == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
        bool forceH = style.OverflowX == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;

        // Content extent in border-box coordinates (children/lines are placed
        // relative to the border-box origin).
        var frag = r.Fragment;
        float maxRight = 0, maxBottom = 0;
        foreach (var line in frag.Lines)
        {
            maxRight = Math.Max(maxRight, line.InlineOffset + line.InlineSize);
            maxBottom = Math.Max(maxBottom, line.BlockEnd);
        }
        foreach (var c in frag.Children)
        {
            maxRight = Math.Max(maxRight, c.InlineOffset + c.InlineSize);
            maxBottom = Math.Max(maxBottom, c.BlockOffset + c.BlockSize);
        }

        // _inlineSize is only set by LayoutMain; the inline-formatting-context
        // branch skips the horizontal check rather than guess the content edge.
        float contentRightEdge = _inlineSize > 0 ? _inlineSize - _borderPadding.Right : float.MaxValue;
        float contentBottomEdge = frag.BlockSize - _borderPadding.Bottom;
        bool needV = forceV || maxBottom > contentBottomEdge + 0.5f;
        bool needH = forceH || maxRight > contentRightEdge + 0.5f;
        if (!needV && !needH)
            return r;

        // Only the vertical bar eats inline space; a horizontal bar reduces the
        // visible block extent (handled at conversion time).
        var retrySpace = Space.WithScrollbarInline(needV ? thickness : 0);
        var retry = new BlockLayoutAlgorithm(Node, retrySpace);
        return retry.Layout();
    }

    private LayoutResult HandleNonsuccessfulLayoutResult(LayoutResult result)
    {
        switch (result.Status)
        {
            case EStatus.NeedsEarlierBreak:
                return RelayoutAndBreakEarlier(result);
            case EStatus.NeedsLineClampRelayout:
                if (_lineClampData.data.CurrentState == LineClampData.ClampState.ClampByLines)
                    return RelayoutIgnoringLineClamp();
                if (Space.IsNewFormattingContext)
                    return RelayoutWithLineClampBlockSize(result.LinesUntilClamp ?? 1);
                // Propagate the error upwards until we reach the BFC root.
                return result;
            case EStatus.DisableFragmentation:
                return RelayoutWithoutFragmentation();
            case EStatus.TextBoxTrimEndDidNotApply:
                return RelayoutForTextBoxTrimEnd();
            default:
                return result;
        }
    }

    private LayoutResult RelayoutAndBreakEarlier(LayoutResult result)
    {
        // The engine has no early-break machinery; just perform the layout again.
        return LayoutMain();
    }

    private LayoutResult RelayoutWithoutFragmentation()
    {
        return LayoutMain();
    }

    private LayoutResult RelayoutIgnoringLineClamp()
    {
        // Line clamp is disabled in the engine; return the current layout.
        return LayoutMain();
    }

    private LayoutResult RelayoutWithLineClampBlockSize(int linesUntilClamp)
    {
        return LayoutMain();
    }

    private LayoutResult RelayoutForTextBoxTrimEnd()
    {
        return LayoutMain();
    }

    private LayoutResult LayoutInlineChild(Element node)
    {
        // The engine only ever uses greedy line breaking, so the "optimal inline
        // child layout context" passes are equivalent to a plain layout.
        return LayoutWithOptimalInlineChildLayoutContext(node);
    }

    private LayoutResult LayoutWithOptimalInlineChildLayoutContext(Element child)
    {
        var inlineResult = new InlineLayoutAlgorithm(Node, Space, this).Layout();
        var result = inlineResult;
        result.EndMarginStrut = _endMarginStrut;
        result.ExclusionSpaceValue = _exclusionSpace;

        // A block that establishes an inline formatting context still occupies a
        // definite position in its parent's block formatting context. The inline
        // algorithm does not run the BFC-resolution machinery, so its result comes
        // back with no BFC block-offset; the parent then cannot resolve its own
        // offset and stacks every sibling at 0 (text of consecutive blocks paints
        // on top of each other). Resolve to the offset the constraint space already
        // knows: a forced offset from a re-layout, otherwise the estimate the
        // parent placed us at. Mirrors the fact that in the source an inline
        // formatting context root is laid out through the same block algorithm and
        // resolves its BFC block-offset before line breaking.
        if (!result.BfcBlockOffsetValue.HasValue)
        {
            // Mirror the block path's NextBorderEdge: the space's estimated BFC
            // block offset does not carry the incoming margin strut, so commit it
            // here (a forced re-layout offset already accounts for it).
            result.BfcBlockOffsetValue = Space.ForcedBfcBlockOffset
                ?? Space.GetBfcOffset().BlockOffset + Space.MarginStrut.Sum;
        }

        // The parent positions the child's inline edge from its BFC line offset
        // (which carries the child's own margin). The inline algorithm never
        // records it, so an IFC root would stack at line offset 0 and lose its
        // left margin; echo the block path and report our space's line offset.
        result.BfcLineOffset = Space.GetBfcOffset().LineOffset;

        // Block fragmentation of inline content: when laying out inside a
        // fragmentainer (column/page) with a definite block-size, keep only the
        // lines that fit and emit a break token so the next fragmentainer resumes
        // with the remaining lines. This is what distributes a paragraph across
        // columns.
        if (Space.HasBlockFragmentation && Space.HasDefiniteBlockSize)
            FragmentInlineLinesForColumn(result);

        return result;
    }

    /// <summary>
    /// Slice an inline formatting context's line boxes to the current
    /// fragmentainer. Keeps the lines starting at the incoming break token's
    /// resume index whose block extent fits the fragmentainer block-size, shifts
    /// them to the fragment origin, and records a break token for the rest.
    /// </summary>
    private void FragmentInlineLinesForColumn(LayoutResult result)
    {
        var fragment = result.Fragment;
        var allLines = fragment.Lines;
        if (allLines.Count == 0)
            return;

        int startIndex = (_breakToken as BlockBreakToken)?.ResumeLineIndex ?? 0;
        if (startIndex < 0) startIndex = 0;

        float fragmentainerBlockSize = Space.AvailableBlockSize;
        if (fragmentainerBlockSize <= 0 || float.IsNaN(fragmentainerBlockSize))
            return;

        // Snapshot the full set of lines (fragment.Lines is rebuilt below).
        var snapshot = new List<BoxLine>(allLines);
        if (startIndex >= snapshot.Count)
        {
            allLines.Clear();
            fragment.BlockSize = 0;
            return;
        }

        const float epsilon = 0.5f;
        float originY = snapshot[startIndex].BlockOffset;

        int end = startIndex;
        while (end < snapshot.Count)
        {
            float relBottom = snapshot[end].BlockEnd - originY;
            // Always keep at least one line (a line taller than the fragmentainer
            // still has to go somewhere), otherwise stop before an overflowing one.
            if (end > startIndex && relBottom > fragmentainerBlockSize + epsilon)
                break;
            end++;
        }

        // Shift the kept lines up to the fragment origin and rebuild the list.
        allLines.Clear();
        float maxBottom = 0;
        for (int i = startIndex; i < end; i++)
        {
            var line = snapshot[i];
            ShiftLineBlock(line, -originY);
            allLines.Add(line);
            maxBottom = Math.Max(maxBottom, line.BlockEnd);
        }

        fragment.BlockSize = maxBottom;
        result.IntrinsicBlockSize = maxBottom;

        if (end < snapshot.Count)
        {
            fragment.BreakToken = new BlockBreakToken
            {
                ResumeLineIndex = end,
                ConsumedBlockSize = ((_breakToken as BlockBreakToken)?.ConsumedBlockSize ?? 0) + maxBottom,
                Node = Node.LayoutBox,
            };
        }
    }

    private static void ShiftLineBlock(BoxLine line, float delta)
    {
        if (delta == 0) return;
        line.BlockOffset += delta;
        line.BaselineOffset += delta;
        foreach (var run in line.Runs)
            run.BlockOffset += delta;
    }

    // ==========================================================================
    // LayoutMain 鈥?the port of Layout(InlineChildLayoutContext*).
    // ==========================================================================

    private LayoutResult LayoutMain()
    {
        LayoutDiagnostics.CountBlockPass();
        _containerBfcLineOffset = Space.GetBfcOffset().LineOffset;

        var adjoiningObjectTypes = Space.AdjoiningObjectTypes;
        if (adjoiningObjectTypes != AdjoiningObjectTypes.None)
        {
            // If there were preceding adjoining objects, they will be affected when
            // the BFC block-offset gets resolved or updated. We then need to roll
            // back and re-layout those objects with the new BFC block-offset, once
            // the BFC block-offset is updated.
            _abortWhenBfcBlockOffsetUpdated = true;
            _adjoiningObjectTypes |= adjoiningObjectTypes;
        }
        else if (Space.HasBlockFragmentation)
        {
            _abortWhenBfcBlockOffsetUpdated = true;
        }

        // Line-clamp state is initialized from the constraint space / style.
        // CSS line-clamp is not exposed by the engine, so only the
        // constraint-space state can ever be activated.
        //
        // This block is now gated behind CssLineClampEnabled.
        // Unconditional activation was a latent landmine: on the ROOT space the
        // block size IS the viewport, so every page whose content is taller than
        // the first screen pushed a child past data.ClampBfcOffset in
        // UpdateAfterLayout → NeedsLineClampRelayout bubbled to the root →
        // RelayoutWithLineClampBlockSize flipped ClampByLines → FinishLayout
        // aborted again → HandleNonsuccessful leaked an EMPTY Element-less
        // fragment and whole long pages rendered blank. The engine has no
        // line-clamp feature; keep the ported machinery dormant until a real
        // -webkit-line-clamp entry point exists.
        if (CssLineClampEnabled && !_lineClampData.data.IsLineClampContext && Space.HasDefiniteBlockSize)
        {
            float clampBfcOffset = ChildAvailableSize().BlockSize;
            if (float.IsNaN(clampBfcOffset) || float.IsInfinity(clampBfcOffset))
            {
                var sizes = ComputeInitialMinMaxBlockSizes(Space, Node, _borderPadding);
                if (sizes.MaxSize != float.MaxValue)
                    clampBfcOffset = Math.Max(0, sizes.MaxSize - _borderPadding.Bottom);
            }
            else
            {
                clampBfcOffset = Math.Max(0, _borderPadding.Top + clampBfcOffset);
            }
            _lineClampData.UpdateClampOffsetFromStyle(clampBfcOffset, _borderPadding.Top);
        }

        float contentEdge = _borderPadding.Top;

        var previousInflowPosition = new PreviousInflowPosition(
            /* logical_block_offset */ 0,
            /* margin_strut */ Space.MarginStrut,
            /* block_end_annotation_space */ _isResuming ? 0 : _padding.Top,
            /* self_collapsing_child_had_clearance */ false);

        // Do not collapse margins between parent and its child if:
        //
        // A: There is border/padding between them.
        // B: This is a new formatting context
        // C: We're resuming layout from a break token. Margin struts cannot pass
        //    from one fragment to another if they are generated by the same block;
        //    they must be dealt with at the first fragment.
        if (contentEdge != 0 || _isResuming || Space.IsNewFormattingContext)
        {
            bool discardSubsequentMargins = previousInflowPosition.margin_strut.discard_margins && contentEdge == 0;
            if (!ResolveBfcBlockOffset(ref previousInflowPosition))
                return AbortBfcBlockOffsetResolved();

            // Move to the content edge. This is where the first child should be
            // placed. The in-flow cursor is content-box-relative: the container's
            // own border/padding is accounted for by the parent when the
            // fragments are placed (via the content-box base used for child
            // offsets), so the first child sits at offset 0 here. If the cursor
            // started at the content edge instead, the edge would be counted
            // twice and every child of a bordered/padded block would be pushed
            // down by its border+padding.
            previousInflowPosition.logical_block_offset = 0;

            // If we resolved the BFC block offset now, the margin strut has been
            // reset. If margins are to be discarded, and this box would otherwise
            // have adjoining margins between its own margin and those subsequent
            // content, we need to make sure subsequent content discard theirs.
            if (discardSubsequentMargins)
                previousInflowPosition.margin_strut.discard_margins = true;
        }

        // If this node is a quirky container, we set our margin strut to a mode
        // where it only considers non-quirky margins.
        if (IsQuirkyContainer())
            previousInflowPosition.margin_strut.is_quirky_container_start = true;

        // Inline layout hook: the previous inline break token is always null for
        // the block-level rendering engine (no fragmentation of inline content).
        var children = WrapInlineRuns(Node);
        var childIter = new ChildIterState(Node.Children);
        int childIndexer = 0;
        bool isClosedDetails = Node.TagName == "DETAILS" && !Node.HasAttribute("open");
        bool detailsSummaryFound = false;

        _placeholderChild = null;

        BlockChildEntry entry;
        InlineBreakToken? previousInlineBreakToken = null;
        while ((entry = NextChildFrom(children, ref childIndexer, childIter, previousInlineBreakToken)).Child != null)
        {
            var child = entry.Child;

            // Closed details/summary support: only lay out up to the first summary.
            if (isClosedDetails)
            {
                if (child.TagName == "SUMMARY" && !detailsSummaryFound)
                    detailsSummaryFound = true;
                else
                    continue;
            }

            var childStyle = child.ComputedStyle;
            if (childStyle == null || childStyle.Display == DisplayType.None)
                continue;
            var childBreakToken = entry.Token;

            if (IsOutOfFlowPositionedChild(child))
            {
                // Out-of-flow fragmentation is a special step that takes place after
                // regular layout, so we should never resume anything here.
                HandleOutOfFlowPositioned(previousInflowPosition, child);
            }
            else if (IsFloatingChild(child))
            {
                HandleFloat(ref previousInflowPosition, child, childBreakToken as BlockBreakToken);
            }
            else if (IsListMarker(child))
            {
                // Ignore outside list markers; they are placed at the end of layout.
            }
            else if (IsColumnSpanAll(child) && Space.IsInColumnBfc() && Space.HasBlockFragmentation)
            {
                // Column spanners are handled by the multicol layout algorithm, but
                // the multicol engine model does not yet surface spanner children.
                continue;
            }
            else if (IsTextControlPlaceholder(child))
            {
                _placeholderChild = child;
            }
            else
            {
                EStatus status;
                if (CreatesNewFormattingContext(child))
                {
                    status = HandleNewFormattingContext(child, childBreakToken as BlockBreakToken, ref previousInflowPosition);
                    previousInlineBreakToken = null;
                }
                else
                {
                    status = HandleInflow(child, childBreakToken, ref previousInflowPosition, previousInlineBreakToken);
                }

                if (status != EStatus.Success)
                {
                    // We need to abort the layout. No fragment will be generated.
                    if (status == EStatus.BfcBlockOffsetResolved)
                        return AbortBfcBlockOffsetResolved();
                    return LayoutResult.Abort(status);
                }
                if (Space.HasBlockFragmentation && HasInflowChildBreakInside())
                    break;
            }
        }

        _hasSeenAllChildrenInternal = childIndexer >= children.Count;

        if (_placeholderChild != null)
        {
            previousInflowPosition.logical_block_offset = HandleTextControlPlaceholder(_placeholderChild, previousInflowPosition);
        }

        // The intrinsic block size is not allowed to be less than the content edge
        // offset, as that could give us a negative content box size.
        _intrinsicBlockSize = contentEdge;

        // The rest of the function is continued within |FinishLayout|; however it
        // should be read as one function.
        return FinishLayout(ref previousInflowPosition);
    }

    private struct ChildIterState
    {
        public List<ChildBreakToken>? ChildBreakTokens;
        public int TokenIdx;
        public bool HasSeenAllChildren;
        public ChildIterState(List<Node> children)
        {
            ChildBreakTokens = null;
            TokenIdx = 0;
            HasSeenAllChildren = false;
        }
    }

    private struct BlockChildEntry
    {
        public Element? Child;
        public BreakToken? Token;
        public BlockChildEntry(Element? child, BreakToken? token) { Child = child; Token = token; }
    }

    private BlockChildEntry NextChildFrom(List<Node> children, ref int index, ChildIterState iter, InlineBreakToken? previousInlineBreakToken)
    {
        BreakToken? tokenOut = null;

        // The engine has no inline contents fragmentation: if a previous inline
        // break token were provided, it would point at the node being resumed.
        if (previousInlineBreakToken != null)
        {
            var el = previousInlineBreakToken.Node?.Dimensions?.Element;
            return new BlockChildEntry(el ?? FindNextElement(children, ref index), previousInlineBreakToken);
        }

        // Consume child break tokens first, in break-token order.
        if (iter.ChildBreakTokens != null)
        {
            if (iter.TokenIdx < iter.ChildBreakTokens.Count)
            {
                var t = iter.ChildBreakTokens[iter.TokenIdx++];
                var node = t.Node?.Dimensions?.Element;
                if (node == null)
                    return NextChildFrom(children, ref index, iter, null);
                return new BlockChildEntry(node, t);
            }
            iter.ChildBreakTokens = null;
        }

        var next = FindNextElement(children, ref index);
        if (next == null)
            return new BlockChildEntry(null, null);
        return new BlockChildEntry(next, tokenOut);
    }

    private static Element? FindNextElement(List<Node> children, ref int index)
    {
        while (index < children.Count)
        {
            var node = children[index++];
            if (node is Element e)
                return e;
        }
        return null;
    }

    /// <summary>
    /// CSS 2.1 §9.2.1.1 anonymous block wrapping: when a block container has both
    /// in-flow block-level children and inline-level content (text or inline
    /// elements), each maximal run of inline content forms an anonymous block
    /// that establishes its own inline formatting context. Without this the text
    /// runs next to block children were dropped entirely. Returns the original
    /// child list unchanged for the common pure-block / pure-inline cases.
    /// </summary>
    private List<Node> WrapInlineRuns(Element node)
    {
        var src = node.Children;
        if (src == null || src.Count < 2) return src ?? new List<Node>();

        static bool IsInlineLevel(Node n)
        {
            if (n is TextNode tn) return true;
            if (n is not Element el) return false;
            var s = el.ComputedStyle;
            if (s == null || s.Display == DisplayType.None) return false;
            if (s.Position is PositionType.Absolute or PositionType.Fixed) return false;
            if (s.Float != FloatType.None) return false;
            return s.Display is DisplayType.Inline or DisplayType.InlineBlock
                or DisplayType.InlineFlex or DisplayType.InlineGrid or DisplayType.Ruby;
        }
        static bool IsBlockLevel(Node n)
        {
            if (n is not Element el) return false;
            var s = el.ComputedStyle;
            if (s == null || s.Display == DisplayType.None) return false;
            return !IsInlineLevel(n);
        }

        bool hasInline = false, hasBlock = false;
        foreach (var n in src)
        {
            if (IsInlineLevel(n)) hasInline = true;
            else if (IsBlockLevel(n)) hasBlock = true;
        }
        if (!hasInline || !hasBlock) return src;

        var result = new List<Node>();
        var run = new List<Node>();
        void FlushRun()
        {
            if (run.Count == 0) return;
            bool hasContent = run.Any(n => n is TextNode t ? !t.IsWhitespaceOnly : true);
            if (hasContent)
            {
                var anon = new HtmlElement("#anonymous-block")
                {
                    ComputedStyle = node.ComputedStyle != null ? node.ComputedStyle.Clone() : null,
                };
                if (anon.ComputedStyle != null)
                {
                    anon.ComputedStyle.Display = DisplayType.Block;
                    // Anonymous blocks are not elements: the parent's box-model
                    // offsets already wrap their content, so their own margins,
                    // padding and borders are zero (CSS 2.1 §9.2.1.1). Inheriting
                    // the parent's margin-top here made it collapse a second time
                    // through the same cursor and inflated the parent's height.
                    anon.ComputedStyle.MarginTop = new PixelLength(0);
                    anon.ComputedStyle.MarginRight = new PixelLength(0);
                    anon.ComputedStyle.MarginBottom = new PixelLength(0);
                    anon.ComputedStyle.MarginLeft = new PixelLength(0);
                    anon.ComputedStyle.PaddingTop = new PixelLength(0);
                    anon.ComputedStyle.PaddingRight = new PixelLength(0);
                    anon.ComputedStyle.PaddingBottom = new PixelLength(0);
                    anon.ComputedStyle.PaddingLeft = new PixelLength(0);
                    anon.ComputedStyle.BorderTopWidth = 0;
                    anon.ComputedStyle.BorderRightWidth = 0;
                    anon.ComputedStyle.BorderBottomWidth = 0;
                    anon.ComputedStyle.BorderLeftWidth = 0;
                    anon.ComputedStyle.Width = AutoLength.Instance;
                    anon.ComputedStyle.Height = AutoLength.Instance;
                }
                foreach (var n in run)
                    anon.AddChildReferenceForLayout(n);
                result.Add(anon);
            }
            run.Clear();
        }

        foreach (var n in src)
        {
            if (IsInlineLevel(n))
            {
                run.Add(n);
            }
            else
            {
                FlushRun();
                if (IsBlockLevel(n))
                    result.Add(n);
            }
        }
        FlushRun();
        return result;
    }

    private bool _hasSeenAllChildrenInternal = true;
    private bool HasSeenAllChildren() => _hasSeenAllChildrenInternal;

    // ==========================================================================
    // FinishLayout 鈥?the port of FinishLayout(PreviousInflowPosition*,
    // InlineChildLayoutContext*).
    // ==========================================================================

    private LayoutResult FinishLayout(ref PreviousInflowPosition previousInflowPosition)
    {
        var constraintSpace = Space;

        if (constraintSpace.IsNewFormattingContext && _lineClampData.ShouldRelayoutWithNoForcedTruncate())
        {
            // Truncation of the last line was forced, but there are no lines after
            // the truncated line. Rerun layout without forcing truncation.
            return LayoutResult.Abort(EStatus.NeedsLineClampRelayout);
        }

        if (ShouldTextBoxTrimEnd() && _lastNonEmptyInflowChild != null && !_lineClampData.previous_inflow_position_when_clamped.HasValue)
        {
            return LayoutResult.Abort(EStatus.TextBoxTrimEndDidNotApply);
        }

        // With CSSLineClamp enabled, if we line-clamped inside this box, its size
        // must be set exactly as if there were no layout boxes after the clamp
        // point. We therefore use the previous inflow position that we saved at
        // the clamp point.
        if (CssLineClampEnabled && _lineClampData.previous_inflow_position_when_clamped.HasValue)
            previousInflowPosition = _lineClampData.previous_inflow_position_when_clamped.Value;

        MarginStrut endMarginStrut = previousInflowPosition.margin_strut;

        // Add line height for empty content editable or button with empty label,
        // e.g. <div contenteditable></div>, <input type="button" value="">.
        if (HasSeenAllChildren() && HasLineEvenIfEmpty(Node))
        {
            _intrinsicBlockSize = Math.Max(_intrinsicBlockSize, _borderPadding.Top + EmptyLineBlockSize());
        }

        // Collapse annotation overflow and padding.
        if (previousInflowPosition.block_end_annotation_space < 0)
        {
            float annotationOverflow = -previousInflowPosition.block_end_annotation_space;
            previousInflowPosition.logical_block_offset -= Math.Min(_padding.Bottom, annotationOverflow);
        }

        // If line clamping occurred, and we're using the legacy behavior, the
        // intrinsic block-size comes from the intrinsic block-size at the time of
        // the clamp, without taking margins, clearance, etc. into account.
        if (!CssLineClampEnabled && _lineClampData.previous_inflow_position_when_clamped.HasValue)
        {
            _intrinsicBlockSize = _lineClampData.previous_inflow_position_when_clamped.Value.logical_block_offset + _borderPadding.Bottom;
            endMarginStrut = MarginStrut.Zero;
        }
        else if (_borderPadding.Bottom != 0 || previousInflowPosition.self_collapsing_child_had_clearance
            || constraintSpace.IsNewFormattingContext)
        {
            // The end margin strut of an in-flow fragment contributes to the size of
            // the current fragment if:
            //  - There is block-end border/scrollbar/padding.
            //  - There was a self-collapsing child affected by clearance.
            //  - We are a new formatting context.
            // Additionally this fragment produces no end margin strut.
            if (constraintSpace.IsNewFormattingContext)
            {
                float clearance = _exclusionSpace.NonHiddenClearanceOffsetIncludingInitialLetter();
                _intrinsicBlockSize = Math.Max(_intrinsicBlockSize, clearance);
            }

            if (!IsContainerBfcResolved())
            {
                // If we have collapsed through the block start and all children (if
                // any), now is the time to determine the BFC block offset.
                if (!ResolveBfcBlockOffset(ref previousInflowPosition))
                    return AbortBfcBlockOffsetResolved();
            }
            else
            {
                // If we are a quirky container, we ignore any quirky margins and just
                // consider normal margins to extend our size.
                float marginStrutSum = IsQuirkyContainer()
                    ? endMarginStrut.QuirkyContainerSum()
                    : endMarginStrut.Sum;

                // The trailing margin strut will be part of our intrinsic block size,
                // but only if there is something that separates the end margin strut
                // from the input margin strut.
                _intrinsicBlockSize = Math.Max(_intrinsicBlockSize, previousInflowPosition.logical_block_offset + marginStrutSum);
            }

            _intrinsicBlockSize += _borderPadding.Bottom;
            endMarginStrut = MarginStrut.Zero;
        }
        else
        {
            // Update our intrinsic block size to be just past the block-end border
            // edge of the last in-flow child. The pending margin is to be propagated
            // to our container, so ignore it.
            _intrinsicBlockSize = Math.Max(_intrinsicBlockSize, previousInflowPosition.logical_block_offset);
        }

        float unconstrainedIntrinsicBlockSize = _intrinsicBlockSize;
        _intrinsicBlockSize = ClampIntrinsicBlockSize(
            Space, Node, _breakToken, _borderPadding, _intrinsicBlockSize, CalculateQuirkyBodyMarginBlockSum(endMarginStrut));

        // In order to calculate the block-size for the fragment, we need to compare
        // the combined intrinsic block-size of all fragments to e.g. specified
        // block-size.
        float previouslyConsumedBlockSize = 0;
        if (_breakToken != null)
            previouslyConsumedBlockSize = (_breakToken as BlockBreakToken)?.ConsumedBlockSize ?? 0;

        // Inline size (kept on the original engine's semantics: the fragment's
        // border-box inline size).
        float inlineSize = LengthUtils.ComputeInlineSizeForFragment(Space, Style, _borderPadding,
            t => new MinMaxSizesResult(new MinMaxSizes(ChildAvailableInlineSize, ChildAvailableInlineSize)));
        if (LengthUtils.IsIndefinite(inlineSize))
            inlineSize = Space.AvailableInlineSize;

        // width: max-content / min-content / fit-content resolve from the box's
        // own intrinsic contributions (CSS Sizing 3 §4).
        if (Style.Width is IntrinsicLength intrinsicWidth)
        {
            var mmI = ComputeMinMaxSizes(new MinMaxSizesFloatInput()).Sizes;
            inlineSize = intrinsicWidth.Kind switch
            {
                IntrinsicSizeKind.MaxContent => mmI.MaxSize,
                IntrinsicSizeKind.MinContent => mmI.MinSize,
                _ => Math.Min(Math.Max(mmI.MinSize, Space.AvailableInlineSize), mmI.MaxSize),
            };
        }

        // Shrink-to-fit: an atomic inline / float / inline-block with auto inline
        // size sizes to its content, not to the full available width. The block
        // algorithm otherwise resolves auto width to the available size, which
        // makes e.g. a <button> span the whole line. Clamp to
        // min(max(min-content, available), max-content).
        if (Space.IsShrinkToFit && Style.Width is AutoLength)
        {
            var mm = ComputeMinMaxSizes(new MinMaxSizesFloatInput()).Sizes;
            float shrinkToFit = Math.Min(Math.Max(mm.MinSize, Space.AvailableInlineSize), mm.MaxSize);
            if (!LengthUtils.IsIndefinite(shrinkToFit) && shrinkToFit >= 0)
                inlineSize = shrinkToFit;
        }

        // Aspect-ratio in the opposite direction: with an auto inline size and a
        // definite block size, the ratio transfers the size to the inline axis
        // (CSS Aspect-Ratio 1 §5.2). Only an explicitly specified height counts -
        // an auto height that merely came out of the content must not widen the box.
        if (Style.AspectRatio > 0 && Style.Width is AutoLength or null
            && Style.Height is not (AutoLength or null or IntrinsicLength))
        {
            float explicitBlock = LengthUtils.ComputeBlockSizeForFragment(Space, Style, _borderPadding,
                previouslyConsumedBlockSize + _intrinsicBlockSize, _inlineSize);
            if (!LengthUtils.IsIndefinite(explicitBlock) && explicitBlock > _borderPadding.VerticalSum)
            {
                float contentBlock = explicitBlock - _borderPadding.VerticalSum;
                inlineSize = contentBlock * Style.AspectRatio + _borderPadding.HorizontalSum;
            }
        }

        var (minI, maxI) = LengthUtils.ComputeMinMaxInlineSizes(Space, Style, _borderPadding,
            t => new MinMaxSizesResult(new MinMaxSizes(ChildAvailableInlineSize, ChildAvailableInlineSize)));
        _inlineSize = Math.Clamp(inlineSize, minI, maxI);

        // Recompute the block-axis size now that we know our content size.
        float blockSize = LengthUtils.ComputeBlockSizeForFragment(Space, Style, _borderPadding,
            previouslyConsumedBlockSize + _intrinsicBlockSize, _inlineSize);

        // Aspect-ratio: when the block-axis size is auto and aspect-ratio is set,
        // derive the block size from the resolved inline size (content-box ratio).
        if (Style.AspectRatio > 0 && Style.Height is AutoLength or null
            && !float.IsNaN(_inlineSize) && _inlineSize > 0)
        {
            float contentInline = Math.Max(0, _inlineSize - _borderPadding.HorizontalSum);
            blockSize = contentInline / Style.AspectRatio + _borderPadding.VerticalSum;
        }
        if (LengthUtils.IsIndefinite(blockSize))
            blockSize = _intrinsicBlockSize;
        var (minB, maxB) = LengthUtils.ComputeMinMaxBlockSizes(Space, Style, _borderPadding, null, _ => _intrinsicBlockSize);
        _blockSize = Math.Clamp(blockSize, minB, maxB);

        // When min/max-height constrains an aspect-ratio box whose inline size is
        // auto, the used block size no longer equals the ratio-derived one, so the
        // preferred ratio would be broken. Transfer the constrained block size back
        // to the inline axis so the box keeps its ratio (CSS Aspect-Ratio 1 §5.2).
        // Only fires when a block-axis constraint actually took effect.
        if (Style.AspectRatio > 0 && Style.Width is AutoLength or null
            && Style.Height is AutoLength or null
            && Math.Abs(blockSize - _blockSize) > 0.5f)
        {
            float constrainedBlock = _blockSize - _borderPadding.VerticalSum;
            if (constrainedBlock > 0)
            {
                float transferredInline = constrainedBlock * Style.AspectRatio + _borderPadding.HorizontalSum;
                _inlineSize = Math.Clamp(transferredInline, minI, maxI);
            }
        }

        // If our BFC block-offset is still unknown, we check:
        //  - If we have a non-zero block-size (margins don't collapse through us).
        //  - If we have a break token.
        if (!IsContainerBfcResolved() && (_blockSize != 0 || _breakToken != null))
        {
            if (!ResolveBfcBlockOffset(ref previousInflowPosition))
                return AbortBfcBlockOffsetResolved();
        }

        if (IsContainerBfcResolved())
        {
            // Do not collapse margins between the last in-flow child and bottom
            // margin of its parent if:
            //  - The block-size differs from the intrinsic size.
            //  - The parent has a definite initial block-size.
            float initialBlockSize = LengthUtils.ResolveMainBlockLength(Space, Style, _borderPadding, Style.Height,
                AutoLength.Instance, float.NaN, float.NaN, null);
            if (_blockSize != _intrinsicBlockSize || !LengthUtils.IsIndefinite(initialBlockSize))
                endMarginStrut = MarginStrut.Zero;
        }

        // List markers should have been positioned if we had line boxes, or boxes
        // that have line boxes. If there were no line boxes, position without line
        // boxes.
        if (ShouldPlaceUnpositionedListMarker() && !HasInflowChildBreakInside())
        {
            if (!PositionListMarkerWithoutLineBoxes(ref previousInflowPosition))
                return LayoutResult.Abort(EStatus.BfcBlockOffsetResolved);
        }

        _endMarginStrut = endMarginStrut;

        if (IsContainerBfcResolved())
        {
            // If we know our BFC block-offset we should have correctly placed all
            // adjoining objects, and shouldn't propagate this information to
            // siblings.
            _adjoiningObjectTypes = AdjoiningObjectTypes.None;
            _hasAdjoiningObjectDescendants = false;
        }
        else
        {
            // If we don't know our BFC block-offset yet, we know that for
            // margin-collapsing purposes we are self-collapsing.
            _isSelfCollapsing = true;

            // If we've been forced at a particular BFC block-offset, (either from
            // clearance past adjoining floats, or a re-layout), we can safely set
            // our BFC block-offset now.
            if (Space.ForcedBfcBlockOffset.HasValue)
            {
                _containerBfcBlockOffset = Space.ForcedBfcBlockOffset.Value;
                if (Space.IsPushedByFloats)
                    _isPushedByFloats = true;
            }
        }

        // Block fragmentation is not performed by the engine's block algorithm; the
        // multicol algorithm owns fragmentainers. FinalizeForFragmentation() is
        // ported below for structural completeness but never runs here.

        builderReadyForFinalize();

        // At this point, perform the final block-content adjustments.
        if (Space.IsTableCell)
        {
            FinalizeTableCellLayout(_intrinsicBlockSize);
        }
        else
        {
            BlockLayoutUtils.AlignBlockContent(Style, unconstrainedIntrinsicBlockSize, Builder);
        }

        HandleOofsAndSpecialDescendants();

        // An exclusion space is confined to nodes within the same formatting
        // context.
        if (Space.IsNewFormattingContext)
            _exclusionSpace = new ExclusionSpace();

        return BuildResult();
    }

    private void builderReadyForFinalize()
    {
        // Set the builder's geometry so AlignBlockContent etc. can read it.
        Builder.InlineSize = _inlineSize;
        Builder.BlockSize = _blockSize;
        Builder.IntrinsicBlockSize = _intrinsicBlockSize;
        Builder.BfcLineOffset = _containerBfcLineOffset;
        Builder.BfcBlockOffset = _containerBfcBlockOffset ?? 0;
        Builder.IsSelfCollapsing = _isSelfCollapsing;
        Builder.EndMarginStrut = _endMarginStrut.Sum;
        Builder.HasSeenAllChildren = HasSeenAllChildren();
        Builder.Baseline = _lastBaseline ?? _firstBaseline ?? 0;
        Builder.BorderLeft = _border.Left;
        Builder.BorderTop = _border.Top;
        Builder.BorderRight = _border.Right;
        Builder.BorderBottom = _border.Bottom;
        Builder.PaddingLeft = _padding.Left;
        Builder.PaddingTop = _padding.Top;
        Builder.PaddingRight = _padding.Right;
        Builder.PaddingBottom = _padding.Bottom;
        Builder.Element = Node;
    }

    private bool _isSelfCollapsing;

    private LayoutResult BuildResult()
    {
        var frag = Builder.ToBoxFragment();
        frag.Children.AddRange(Builder.Children);
        frag.Lines.AddRange(Builder.Lines);

        var result = LayoutResult.FromFragment(frag);
        result.IntrinsicBlockSize = _intrinsicBlockSize;
        result.BfcLineOffset = _containerBfcLineOffset;
        result.BfcBlockOffset = _containerBfcBlockOffset ?? 0;
        result.BfcBlockOffsetValue = _containerBfcBlockOffset;
        result.IsSelfCollapsing = _isSelfCollapsing;
        result.IsPushedByFloats = _isPushedByFloats;
        result.EndMarginStrut = _endMarginStrut;
        result.SubtreeModifiedMarginStrut = _subtreeModifiedMarginStrut;
        result.AdjoiningObjectTypes = _adjoiningObjectTypes;
        result.HasAdjoiningObjectDescendants = _hasAdjoiningObjectDescendants;
        result.ExclusionSpaceValue = _exclusionSpace;
        result.LineCount = Builder.Lines.Count;
        return result;
    }

    // ==========================================================================
    // Helper accessors from the header.
    // ==========================================================================

    /// <summary>Return the BFC block offset of this block.</summary>
    private float BfcBlockOffset()
    {
        // If we have resolved our BFC block offset, use that. Otherwise fall back
        // to the BFC block offset assigned by the parent algorithm.
        if (_containerBfcBlockOffset.HasValue)
            return _containerBfcBlockOffset.Value;
        return Space.GetBfcOffset().BlockOffset;
    }

    private bool IsContainerBfcResolved() => _containerBfcBlockOffset.HasValue;

    private BfcOffset ContainerBfcOffset() => new(_containerBfcLineOffset, BfcBlockOffset());

    /// <summary>The float exclusion space of this formatting context. The inline
    /// algorithm that breaks this block's line boxes shares it, so that line boxes
    /// step around the floats (CSS 2.1 §9.5.2) and floats positioned inside the
    /// text are visible to the following content.</summary>
    internal ExclusionSpace FloatExclusionSpace => _exclusionSpace;

    /// <summary>Formatting-context coordinates of this box's border-box origin,
    /// needed to map content-box-relative line boxes into the exclusion space.</summary>
    internal BfcOffset ContainerBfcOriginForLines() => ContainerBfcOffset();

    /// <summary>Return the BFC block offset of the next block-start border edge
    /// (for some child) we'd get if we commit pending margins.</summary>
    private float NextBorderEdge(PreviousInflowPosition previousInflowPosition)
    {
        return BfcBlockOffset() + previousInflowPosition.logical_block_offset + previousInflowPosition.margin_strut.Sum;
    }

    private float EmptyLineBlockSize() => Fonts.LineBoxMetrics.GetLineHeight(Style);

    private bool IsQuirkyContainer() => false;
    private bool IsQuirkyAndFillsViewport() => false;
    private bool IsBody() => Node.TagName == "BODY";

    private bool HasMarginBlockStartQuirk(Element child) => false;
    private bool HasMarginBlockEndQuirk(Element child) => false;

    private BoxStrut Padding() => _padding;
    private BoxStrut BorderScrollbarPadding() => _borderPadding;

    /// <summary>
    /// Available size for in-flow children. Uses the box's own resolved inline
    /// size (not the external Space.AvailableInlineSize) so fixed-width /
    /// shrink-to-fit containers — inline-block, floats, explicit width — give
    /// their children the correct content width. The scrollbar strut reserved
    /// by <see cref="MaybeRelayoutForScrollbarSpace"/> is excluded as well.
    /// </summary>
    private LogicalSize ChildAvailableSize() => new(
        Math.Max(0, OwnContentInlineSize()),
        ChildAvailableBlockSize);

    /// <summary>
    /// Block size this box's children resolve their percentages against. A
    /// percentage height needs the *containing block's* definite height; a block
    /// whose own height comes from its content has none, and its children's
    /// percentages then behave as auto (CSS 2.1 §10.5). The height we happened to be
    /// laid out in — the initial containing block, i.e. the viewport — is not such a
    /// height, so it must not be passed down.
    /// </summary>
    private float ChildPercentageBlockSize()
    {
        var height = Style.Height;
        if (height != null && height is not AutoLength)
        {
            float resolved = LengthUtils.ResolveBlockLength(Space, Style, _borderPadding, height, null);
            if (!LengthUtils.IsIndefinite(resolved) && resolved >= 0)
                return Math.Max(0, resolved - _borderPadding.VerticalSum);
        }
        // A box whose block size the parent fixed (a stretched flex or grid item, a
        // table cell, a fragmentainer) is definite as well.
        if (Space.IsFixedBlockSize && Space.HasDefiniteBlockSize)
            return Math.Max(0, Space.AvailableBlockSize - _borderPadding.VerticalSum);
        // The indefinite sentinel for percentage bases is NaN, not infinity.
        return LengthUtils.IndefiniteSize;
    }

    /// <summary>
    /// Inline size the children of this box flow in. This is the box's own resolved
    /// content width, not the outer constraint space: a fixed-width container must
    /// not let its children (or its floats) stretch past it, which is what layout
    /// opportunities for a new formatting context beside a float are measured
    /// against.
    /// </summary>
    private float ChildrenInlineSize => ChildAvailableSize().InlineSize;

    /// <summary>
    /// Content inline size of this box as understood by its children: the
    /// explicit/computed width adjusted for the CSS box model, or the outer
    /// constraint space for auto/stretch boxes. Computed from the style alone
    /// (before children are laid out), unlike <c>_inlineSize</c> which is only
    /// resolved at the end of the algorithm.
    /// </summary>
    private float OwnContentInlineSize()
    {
        float own = float.NaN;
        if (Style.Width is PixelLength px && px.Value > 0)
        {
            own = px.Value;
            if (Style.BoxSizing == BoxSizingType.ContentBox)
                own += _border.Left + _border.Right;
        }
        else if (Style.Width is PercentLength pct && Space.HasDefiniteInlineSize)
        {
            own = pct.Value * Space.AvailableInlineSize;
            if (Style.BoxSizing == BoxSizingType.ContentBox)
                own += _border.Left + _border.Right;
        }
        float maxW = Style.MaxWidth is PixelLength mw && mw.Value > 0 ? mw.Value : float.MaxValue;
        if (Style.MaxWidth is PercentLength pctMax && pctMax.Value > 0 && Space.HasDefiniteInlineSize)
            maxW = Math.Min(maxW, pctMax.Value * Space.AvailableInlineSize);
        // CSS 2.1 §10.4: when min-width exceeds max-width, max-width is ignored.
        if (Style.MinWidth is PixelLength conflictMin && conflictMin.Value > maxW)
            maxW = float.MaxValue;

        if (!float.IsNaN(own))
            return Math.Max(0, Math.Min(own, maxW) - _borderPadding.HorizontalSum - Space.ScrollbarInline);

        // Auto width: the box stretches to the container but min/max-width still
        // clamp the width its children flow in (e.g. max-width: 200px wrapping).
        float avail = ChildAvailableInlineSize;
        if (Style.MinWidth is PixelLength mnw && mnw.Value > avail)
            avail = mnw.Value;
        return Math.Max(0, Math.Min(avail, maxW) - Space.ScrollbarInline);
    }

    private float ContainerBfcBlockOffset() => _containerBfcBlockOffset ?? Space.GetBfcOffset().BlockOffset;

    // ==========================================================================
    // SetSubtreeModifiedMarginStrutIfNeeded.
    // ==========================================================================

    private void SetSubtreeModifiedMarginStrutIfNeeded(Length? margin = null)
    {
        if (_containerBfcBlockOffset.HasValue)
            return;
        if (margin != null && IsZeroLength(margin))
            return;
        _subtreeModifiedMarginStrut = true;
    }

    // ==========================================================================
    // TryReuseFragmentsFromCache 鈥?the engine has no fragment cache, so this
    // always reports failure (the .cc guards it the same way when there's no
    // previous result or when paragraph-level line breaking is in effect).
    // ==========================================================================

    private bool TryReuseFragmentsFromCache(InlineNode inlineChild, ref float logicalBlockOffset, out BreakToken? inlineBreakTokenOut)
    {
        inlineBreakTokenOut = null;
        // The engine's line breaker never caches reusable item fragments.
        return false;
    }

    // ==========================================================================
    // HandleOutOfFlowPositioned.
    // ==========================================================================

    private void HandleOutOfFlowPositioned(PreviousInflowPosition previousInflowPosition, Element child)
    {
        if (Space.HasBlockFragmentation)
        {
            // Forced breaks cannot be specified directly on out-of-flow positioned
            // elements, but if the preceding block has a forced break after, we need
            // to break before it.
            EBreakBetween breakBetween = JoinedBreakBetweenValue(EBreakBetween.Auto);
            if (IsForcedBreakValue(Space, breakBetween))
            {
                return;
            }
        }

        float staticInlineOffset = _borderPadding.InlineStartFor(Space.Direction);
        float staticBlockOffset = previousInflowPosition.logical_block_offset;

        // We only include the margin strut in the OOF static-position if we know we
        // aren't going to be a zero-block-size fragment.
        if (_containerBfcBlockOffset.HasValue)
            staticBlockOffset += previousInflowPosition.margin_strut.Sum;

        var style = child.ComputedStyle!;
        if (IsOriginalDisplayInlineType(style))
        {
            // The static-position of inline-level OOF-positioned nodes depends on
            // previous floats (if any). Due to this we need to mark this node as
            // having adjoining objects, and perform a re-layout if our position
            // shifts.
            if (!_containerBfcBlockOffset.HasValue)
            {
                _adjoiningObjectTypes |= AdjoiningObjectTypes.OutOfFlow;
                _abortWhenBfcBlockOffsetUpdated = true;
            }

            float originBfcBlockOffset = (_containerBfcBlockOffset ?? Space.ExpectedBfcBlockOffset) + staticBlockOffset;

            staticInlineOffset += CalculateOutOfFlowStaticInlineLevelOffset(
                Style, new BfcOffset(Space.GetBfcOffset().LineOffset, originBfcBlockOffset), _exclusionSpace, ChildrenInlineSize);
        }

        var candidate = new OutOfFlowChildCandidate(
            new LayoutBox { Dimensions = new BoxDimensions { Style = style, Element = child } },
            new LogicalStaticPosition(new LogicalOffset(staticInlineOffset, staticBlockOffset),
                LogicalStaticPosition.StaticInlinePosition.Left,
                LogicalStaticPosition.StaticBlockPosition.Top,
                WritingDirectionMode.HorizontalLtr))
        {
            IsAbsolute = style.Position == PositionType.Absolute,
            IsFixed = style.Position == PositionType.Fixed,
            IsHiddenForPaint = _lineClampData.ShouldHideForPaint(),
        };
        _oofCandidates.Add(candidate);
    }

    private static float CalculateOutOfFlowStaticInlineLevelOffset(ComputedStyle containerStyle, BfcOffset originBfcOffset,
        ExclusionSpace exclusionSpace, float childAvailableInlineSize)
    {
        // Simplified port of block_layout_algorithm_utils.cc: inline-level
        // OOF nodes avoid floats on the line. The engine keeps the static inline
        // offset, so this contributes nothing extra.
        return 0;
    }

    private static bool IsForcedBreakValue(ConstraintSpace space, EBreakBetween breakBetween)
    {
        return breakBetween is EBreakBetween.Column or EBreakBetween.Page or EBreakBetween.Left or EBreakBetween.Right
            or EBreakBetween.Recto or EBreakBetween.Verso;
    }

    private EBreakBetween JoinedBreakBetweenValue(EBreakBetween firstValue)
    {
        return BreakBetweenValue(firstValue);
    }

    private EBreakBetween BreakBetweenValue(EBreakBetween firstValue)
    {
        float p0 = FragmentainerBreakPrecedence(firstValue);
        float p1 = FragmentainerBreakPrecedence(Builder.PreviousBreakAfter);
        return p1 >= p0 ? Builder.PreviousBreakAfter : firstValue;
    }

    private static float FragmentainerBreakPrecedence(EBreakBetween breakValue)
    {
        switch (breakValue)
        {
            default:
            case EBreakBetween.Auto:
                return 0;
            case EBreakBetween.AvoidColumn:
                return 1;
            case EBreakBetween.AvoidPage:
                return 2;
            case EBreakBetween.Avoid:
                return 4;
            case EBreakBetween.Column:
                return 5;
            case EBreakBetween.Page:
                return 6;
            case EBreakBetween.Left:
            case EBreakBetween.Recto:
                return 7;
            case EBreakBetween.Right:
            case EBreakBetween.Verso:
                return 8;
        }
    }

    private static bool IsOriginalDisplayInlineType(ComputedStyle style)
    {
        return style.Display is DisplayType.Inline or DisplayType.InlineBlock or DisplayType.InlineFlex or DisplayType.InlineGrid;
    }

    // ==========================================================================
    // HandleFloat.
    // ==========================================================================

    private void HandleFloat(ref PreviousInflowPosition previousInflowPosition, Element child, BlockBreakToken? childBreakToken)
    {
        var constraintSpace = Space;
        var style = child.ComputedStyle!;

        if (constraintSpace.HasBlockFragmentation)
        {
            EBreakBetween breakBetween = JoinedBreakBetweenValue(EBreakBetween.Auto);
            if (IsForcedBreakValue(constraintSpace, breakBetween))
            {
                AddBreakBeforeChild(child, BreakAppeal.Perfect, /* is_forced_break */ true);
                return;
            }
        }

        // Floats are positioned against the block's own exclusion space, so the
        // container's BFC block-offset has to be resolved at the first float.
        if (Space.AdjoiningObjectTypes != AdjoiningObjectTypes.None && !_containerBfcBlockOffset.HasValue)
        {
            _adjoiningObjectTypes |= Space.AdjoiningObjectTypes;
        }

        if (!_containerBfcBlockOffset.HasValue)
        {
            // Resolve the container's BFC block-offset at the first float, so that
            // the floats and the in-flow content share one vertical origin.
            bool ok = ResolveBfcBlockOffset(ref previousInflowPosition);
            if (!ok)
                return;
        }

        // Layout the float.
        var childSpace = CreateFloatConstraintSpace(child);
        var layoutResult = LayoutBlockChild(childSpace, childBreakToken, child);
        var fragment = layoutResult.Fragment;

        float childInlineSize = LogicalInlineSize(fragment);
        float childBlockSize = LogicalBlockSize(fragment);

        bool isLeft = style.Float == FloatType.Left;

        // A float's auto margins are treated as zero (CSS 2.1 §10.3.3). Negative
        // margins are legal and shift the float / shrink the line box it reserves,
        // so they must not be clamped to zero here.
        using var _marginFontScope = FontUnitContext.Use(style);
        float MarginPx(Length l) => l is AutoLength ? 0
            : l.ToPixels(style.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
        float BlockMarginPx(Length l) => l is AutoLength ? 0 :
            l.ToPixels(style.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
        float marginTopBlock = BlockMarginPx(style.MarginTop);
        float marginBottomBlock = BlockMarginPx(style.MarginBottom);
        float marginLeftBlock = MarginPx(style.MarginLeft);
        float marginRightBlock = MarginPx(style.MarginRight);
        // A float reserves its margin box on the line it starts on.
        float marginInlineSize = childInlineSize + marginLeftBlock + marginRightBlock;

        // CSS 2.1 §9.5.1: a float's block-start edge may not be higher than the
        // border edges of the blocks that came before it, and it has to clear the
        // floats that already occupy that line. Start at the natural offset and step
        // down line by line until the margin box fits; the inline position is the
        // remaining edge of that line (start for a left float, end for a right one).
        float contentInline = OwnContentInlineSize();
        float contentLineStart = ContainerBfcOffset().LineOffset + _border.Left + _padding.Left;
        float contentBlockStart = ContainerBfcOffset().BlockOffset + _border.Top + _padding.Top;
        // Border-box top of the float, relative to the container's content box.
        float floatBlockOffset = previousInflowPosition.logical_block_offset + marginTopBlock;
        float floatInlineOffset;
        for (int guard = 0; ; guard++)
        {
            var (lineStart, lineEnd, nextBlockOffset) = _exclusionSpace.FloatLineAt(
                contentBlockStart + floatBlockOffset, contentLineStart, contentLineStart + contentInline);
            bool fitsOnLine = lineEnd - lineStart >= marginInlineSize;
            if (fitsOnLine || guard >= 64 || nextBlockOffset <= contentBlockStart + floatBlockOffset
                || nextBlockOffset == float.MaxValue)
            {
                floatInlineOffset = isLeft
                    ? lineStart + marginLeftBlock - contentLineStart
                    : lineEnd - marginRightBlock - childInlineSize - contentLineStart;
                break;
            }
            floatBlockOffset = nextBlockOffset - contentBlockStart;
        }
        float inlineOffset = floatInlineOffset;

        fragment.BlockOffset = floatBlockOffset;
        fragment.InlineOffset = inlineOffset;
        fragment.MarginTop = marginTopBlock;
        fragment.MarginBottom = marginBottomBlock;
        fragment.MarginLeft = marginLeftBlock;
        fragment.MarginRight = marginRightBlock;
        fragment.IsFloating = true;
        Builder.AddChild(fragment);

        // Record the float in the exclusion space so that clearance, layout
        // opportunities and the line boxes of this block's text avoid it. The
        // reserved area is the float's margin box.
        //
        // The float does NOT advance the in-flow position: block boxes overlap floats
        // and only line boxes step around them (CSS 2.1 §9.5.2).
        float bfcLineStart = contentLineStart + inlineOffset - marginLeftBlock;
        float bfcLineEnd = bfcLineStart + marginInlineSize;
        float bfcBlockStart = contentBlockStart + floatBlockOffset - marginTopBlock;
        float bfcBlockEnd = bfcBlockStart + marginTopBlock + childBlockSize + marginBottomBlock;
        _exclusionSpace.Add(ExclusionArea.Create(
            new BfcRect(new BfcOffset(bfcLineStart, bfcBlockStart), new BfcOffset(bfcLineEnd, bfcBlockEnd)),
            style.Float, /* is_hidden_for_paint */ false));

        // Later in-flow children with 'clear' only get clearance when they know an
        // adjoining float exists; without this registration the clear property is
        // silently ignored (CSS 2.1 §9.5.2).
        _adjoiningObjectTypes |= isLeft ? AdjoiningObjectTypes.FloatLeft : AdjoiningObjectTypes.FloatRight;
    }

    private ConstraintSpace CreateFloatConstraintSpace(Element child)
    {
        var b = Space.InheritBuilder(ChildrenInlineSize, float.PositiveInfinity);
        b.SetIsNewFormattingContext(true);
        b.SetPercentageResolution(ChildrenInlineSize, ChildAvailableBlockSize);
        b.SetDirection(Space.Direction);
        // A float with an auto width is shrink-to-fit (CSS 2.1 §10.3.5): it takes its
        // content's preferred width, capped by the available line, so it neither fills
        // the container nor blocks a sibling float from sitting beside it. The
        // shrink-to-fit path only fires when width is auto, so explicit-width floats
        // are unaffected.
        b.SetShrinkToFit(true);
        return b.ToConstraintSpace();
    }

    // ==========================================================================
    // HandleNewFormattingContext / LayoutNewFormattingContext.
    // ==========================================================================

    private EStatus HandleNewFormattingContext(Element child, BlockBreakToken? childBreakToken,
        ref PreviousInflowPosition previousInflowPosition)
    {
        var constraintSpace = Space;
        var childStyle = child.ComputedStyle!;
        var direction = constraintSpace.Direction;

        InflowChildData childData = ComputeChildData(previousInflowPosition, child, childBreakToken, /* is_new_fc */ true);

        float childOriginLineOffset = constraintSpace.GetBfcOffset().LineOffset;

        // If the child has a block-start margin, and the BFC block offset is still
        // unresolved, and we have preceding adjoining floats, things get
        // complicated here. See the .cc for the full discussion.
        MarginStrut adjoiningMarginStrut = previousInflowPosition.margin_strut;
        adjoiningMarginStrut.Append(childData.margins.Top, HasMarginBlockStartQuirk(child));
        float adjoiningBfcOffsetEstimate = childData.bfc_offset_estimate.BlockOffset + adjoiningMarginStrut.Sum;
        float nonAdjoiningBfcOffsetEstimate = childData.bfc_offset_estimate.BlockOffset + previousInflowPosition.margin_strut.Sum;
        float childBfcOffsetEstimate = adjoiningBfcOffsetEstimate;
        bool bfcOffsetAlreadyResolved = false;
        bool childDeterminedBfcOffset = false;
        bool childMarginGotSeparated = false;
        bool hasAdjoiningFloats = false;

        if (!_containerBfcBlockOffset.HasValue)
        {
            hasAdjoiningFloats = (_adjoiningObjectTypes & (AdjoiningObjectTypes.FloatLeft | AdjoiningObjectTypes.FloatRight)) != 0;

            // If this node, or an arbitrary ancestor had clearance past adjoining
            // floats, we consider the margin "separated".
            bool hasClearancePastAdjoiningFloats = Space.AncestorHasClearancePastAdjoiningFloats
                || HasClearancePastAdjoiningFloats(_adjoiningObjectTypes, childStyle, Style);

            if (hasClearancePastAdjoiningFloats)
            {
                childBfcOffsetEstimate = NextBorderEdge(previousInflowPosition);
                childMarginGotSeparated = true;
            }
            else if (Space.ForcedBfcBlockOffset.HasValue)
            {
                // This is not the first time we're here. We already have a suggested
                // BFC block offset.
                bfcOffsetAlreadyResolved = true;
                childBfcOffsetEstimate = Space.ForcedBfcBlockOffset.Value;
                childMarginGotSeparated = childBfcOffsetEstimate != adjoiningBfcOffsetEstimate;
            }

            // The BFC block offset of this container gets resolved because of this
            // child.
            childDeterminedBfcOffset = true;

            // The block-start margin of the child will only affect the parent's
            // position if it is adjoining.
            if (!childMarginGotSeparated)
                SetSubtreeModifiedMarginStrutIfNeeded(childStyle.MarginTop);

            if (!ResolveBfcBlockOffset(ref previousInflowPosition, childBfcOffsetEstimate))
            {
                // If we need to abort here, it means that we had preceding
                // unpositioned floats.
                if (!bfcOffsetAlreadyResolved)
                    return AbortBfcBlockOffsetResolvedStatus();
            }

            // We reset the block offset here as it may have been affected by
            // clearance.
            childBfcOffsetEstimate = ContainerBfcBlockOffset();
        }

        // If the child has a non-zero block-start margin, our initial estimate will
        // be that any pending floats will be flush (block-start-wise) with this
        // child. In this case, the child's margin no longer collapses with the
        // previous margin strut, so we'll need another layout pass if this happens.
        bool abortIfCleared = childData.margins.Top != 0 && !childMarginGotSeparated && childDeterminedBfcOffset;
        BfcOffset childBfcOffset;
        BoxStrut resolvedMargins;
        LayoutResult? layoutResult = LayoutNewFormattingContext(child, childBreakToken, childData,
            new BfcOffset(childOriginLineOffset, childBfcOffsetEstimate), abortIfCleared, out childBfcOffset, out resolvedMargins);

        if (layoutResult == null)
        {
            // Layout got aborted, because the child got pushed down by floats, and
            // we may have had pending floats that we tentatively positioned
            // incorrectly. Try again without the child's margin.
            if (childDeterminedBfcOffset)
            {
                // The BFC block offset was calculated when we got to this child, with
                // the child's margin adjoining. Since that turned out to be wrong,
                // re-resolve the BFC block offset without the child's margin.
                float oldOffset = ContainerBfcBlockOffset();
                _containerBfcBlockOffset = null;

                ResolveBfcBlockOffset(ref previousInflowPosition, nonAdjoiningBfcOffsetEstimate, null);

                if ((bfcOffsetAlreadyResolved || hasAdjoiningFloats) && oldOffset != ContainerBfcBlockOffset())
                {
                    // The first BFC block offset resolution turned out to be wrong, and
                    // we positioned preceding adjacent floats based on that. Now we have
                    // to roll back and position them at the correct offset.
                    return AbortBfcBlockOffsetResolvedStatus();
                }
            }

            childBfcOffsetEstimate = nonAdjoiningBfcOffsetEstimate;
            childMarginGotSeparated = true;

            // We can re-layout the child right away. This re-layout *must* produce a
            // fragment which fits within the exclusion space.
            layoutResult = LayoutNewFormattingContext(child, childBreakToken, childData,
                new BfcOffset(childOriginLineOffset, childBfcOffsetEstimate), /* abort_if_cleared */ false,
                out childBfcOffset, out resolvedMargins);
        }

        // Block fragmentation is not applied by the engine block algorithm.

        var fragment = layoutResult!.Fragment;
        float fragmentInline = LogicalInlineSize(fragment);

        LogicalOffset logicalOffset = LogicalFromBfcOffsets(childBfcOffset, ContainerBfcOffset(), fragmentInline,
            _inlineSize, direction);

        if (!PositionOrPropagateListMarker(layoutResult, ref logicalOffset, ref previousInflowPosition))
            return AbortBfcBlockOffsetResolvedStatus();

        PropagateBaselineFromBlockChild(fragment, resolvedMargins, logicalOffset.BlockOffset);

        fragment.BlockOffset = logicalOffset.BlockOffset;
        fragment.InlineOffset = logicalOffset.InlineOffset;
        fragment.MarginLeft = resolvedMargins.Left;
        fragment.MarginTop = resolvedMargins.Top;
        fragment.MarginRight = resolvedMargins.Right;
        fragment.MarginBottom = resolvedMargins.Bottom;
        Builder.AddChild(fragment);

        if (childBreakToken == null || !IsInParallelFlow(childBreakToken))
        {
            previousInflowPosition = ComputeInflowPosition(previousInflowPosition, child, childData, childBfcOffset.BlockOffset,
                logicalOffset, layoutResult, new LogicalSize(fragmentInline, LogicalBlockSize(fragment)),
                /* self_collapsing_child_had_clearance */ false);
        }

        // Update line-clamp data, and abort if needed.
        if (!_lineClampData.UpdateAfterLayout(layoutResult, ContainerBfcBlockOffset(), previousInflowPosition, Padding().Bottom))
        {
            Builder.NotifyNeedsLineClampRelayout();
            return EStatus.NeedsLineClampRelayout;
        }

        return EStatus.Success;
    }

    private LayoutResult? LayoutNewFormattingContext(Element child, BlockBreakToken? childBreakToken, InflowChildData childData,
        BfcOffset originOffset, bool abortIfCleared, out BfcOffset outChildBfcOffset, out BoxStrut outResolvedMargins)
    {
        outChildBfcOffset = BfcOffset.Zero;
        outResolvedMargins = childData.margins;

        var childStyle = child.ComputedStyle!;
        var direction = Space.Direction;

        if (!IsBreakInside(childBreakToken))
        {
            // The origin offset is where we should start looking for layout
            // opportunities. It needs to be adjusted by the child's clearance.
            AdjustToClearance(_exclusionSpace.ClearanceOffsetIncludingInitialLetter(childStyle.Clear), ref originOffset);
        }

        var opportunities = _exclusionSpace.AllLayoutOpportunities(originOffset, ChildrenInlineSize);

        // We should always have at least one opportunity.
        if (opportunities.Count == 0)
            opportunities.Add(new LayoutOpportunity(new BfcRect(originOffset,
                new BfcOffset(originOffset.LineOffset + ChildrenInlineSize, float.MaxValue))));

        // Now we lay out. This will give us a child fragment and thus its size,
        // which means that we can find out if it's actually going to fit.
        foreach (var opportunity in opportunities)
        {
            if (abortIfCleared && originOffset.BlockOffset < opportunity.Rect.BlockStartOffset)
            {
                // Abort if we got pushed downwards. We need to adjust
                // origin_offset.block_offset, reposition any floats affected by that,
                // and try again.
                return null;
            }

            // Determine which sides of the opportunity have floats we should avoid.
            bool hasFloatsOnLineLeft = opportunity.Rect.LineStartOffset != originOffset.LineOffset;
            bool hasFloatsOnLineRight = opportunity.Rect.LineEndOffset != originOffset.LineOffset + ChildrenInlineSize;
            bool canExpandOutsideOpportunity = !hasFloatsOnLineLeft && !hasFloatsOnLineRight;

            float lineLeftMargin = LineLeft(childData.margins, direction);
            float lineRightMargin = LineRight(childData.margins, direction);

            // Find the available inline-size which should be given to the child.
            float lineLeftOffset = opportunity.Rect.LineStartOffset;
            float lineRightOffset = opportunity.Rect.LineEndOffset;

            if (canExpandOutsideOpportunity)
            {
                // No floats have affected the available inline-size, adjust the
                // available inline-size by the margins.
                lineLeftOffset += lineLeftMargin;
                lineRightOffset -= lineRightMargin;
            }
            else
            {
                // Margins are applied from the content-box, not the layout opportunity
                // area.
                lineLeftOffset = Math.Max(lineLeftOffset, originOffset.LineOffset + Math.Max(0, lineLeftMargin));
                lineRightOffset = Math.Min(lineRightOffset,
                    originOffset.LineOffset + ChildrenInlineSize - Math.Max(0, lineRightMargin));
            }
            float opportunitySize = Math.Max(0, lineRightOffset - lineLeftOffset);

            // The available inline size in the child constraint space needs to include
            // inline margins.
            float childAvailableInlineSize = Math.Max(0, opportunitySize + childData.margins.HorizontalSum);

            ConstraintSpace childSpace = CreateConstraintSpaceForChild(child, childBreakToken, childData,
                new LogicalSize(childAvailableInlineSize, ChildAvailableSize().BlockSize),
                /* is_new_fc */ true, opportunity.Rect.BlockStartOffset, /* has_clearance_past_adjoining_floats */ false,
                /* block_start_annotation_space */ 0);

            var layoutResult = LayoutBlockChild(childSpace, childBreakToken, child);

            if (layoutResult.Status != EStatus.Success)
                continue;

            // Check if we can fit in the opportunity block direction.
            float fragmentBlockSize = LogicalBlockSize(layoutResult.Fragment);
            if (fragmentBlockSize > opportunity.Rect.BlockSize && opportunity.Rect.BlockSize != float.MaxValue)
                continue;

            // Now find the fragment's (final) position calculating the auto margins.
            BoxStrut autoMargins = childData.margins;
            float textAlignOffset = 0;
            bool hasAutoMargins = false;
            float fragmentInlineSize = LogicalInlineSize(layoutResult.Fragment);

            if (childStyle.MarginLeft is AutoLength || childStyle.MarginRight is AutoLength)
            {
                hasAutoMargins = true;
                ResolveInlineAutoMargins(childStyle, Style, childAvailableInlineSize, fragmentInlineSize, ref autoMargins);
            }
            else
            {
                // Handle -webkit- values for text-align.
                textAlignOffset = WebkitTextAlignAndJustifySelfOffset(childStyle, Style, opportunity.Rect.InlineSize, childData.margins,
                    () => fragmentInlineSize);
            }

            // Determine our final BFC offset.
            var childBfcOffset = new BfcOffset(0, opportunity.Rect.BlockStartOffset);
            if (direction == TextDirection.Ltr)
            {
                float autoMarginLineLeft = LineLeft(autoMargins, direction) - lineLeftMargin;
                childBfcOffset = new BfcOffset(lineLeftOffset + autoMarginLineLeft + textAlignOffset, opportunity.Rect.BlockStartOffset);
            }
            else
            {
                float autoMarginLineRight = LineRight(autoMargins, direction) - lineRightMargin;
                childBfcOffset = new BfcOffset(lineRightOffset - textAlignOffset - autoMarginLineRight - fragmentInlineSize,
                    opportunity.Rect.BlockStartOffset);
            }

            // Check if we'll intersect any floats on our line-left/line-right.
            if (hasFloatsOnLineLeft && childBfcOffset.LineOffset < opportunity.Rect.LineStartOffset)
                continue;
            if (hasFloatsOnLineRight && childBfcOffset.LineOffset + fragmentInlineSize > opportunity.Rect.LineEndOffset)
                continue;
            if (!canExpandOutsideOpportunity && fragmentInlineSize > opportunity.Rect.InlineSize)
                continue;

            // auto-margins are "fun". To ensure round tripping from
            // getComputedStyle the used values are relative to the content-box
            // edge, rather than the opportunity edge.
            BoxStrut resolvedMargins = childData.margins;
            if (hasAutoMargins)
            {
                float inlineOffsetFromContent = LogicalFromBfcLineOffset(childBfcOffset.LineOffset, _containerBfcLineOffset,
                    fragmentInlineSize, _inlineSize, direction) - _borderPadding.InlineStartFor(direction);
                if (childStyle.MarginLeft is AutoLength)
                    resolvedMargins = new BoxStrut(resolvedMargins.Top, resolvedMargins.Right, resolvedMargins.Bottom, inlineOffsetFromContent);
                if (childStyle.MarginRight is AutoLength)
                    resolvedMargins = new BoxStrut(resolvedMargins.Top, ChildrenInlineSize - inlineOffsetFromContent - fragmentInlineSize,
                        resolvedMargins.Bottom, resolvedMargins.Left);
            }

            outChildBfcOffset = childBfcOffset;
            outResolvedMargins = resolvedMargins;
            return layoutResult;
        }

        // NOTREACHED in the reference algorithm. Fall back to the origin for robustness.
        outChildBfcOffset = new BfcOffset(originOffset.LineOffset, originOffset.BlockOffset);
        var fallbackSpace = CreateConstraintSpaceForChild(child, childBreakToken, childData,
            new LogicalSize(ChildrenInlineSize, ChildAvailableSize().BlockSize),
            /* is_new_fc */ true, originOffset.BlockOffset, false, 0);
        return LayoutBlockChild(fallbackSpace, childBreakToken, child);
    }

    private static void AdjustToClearance(float clearance, ref BfcOffset origin)
    {
        if (origin.BlockOffset < clearance)
            origin = new BfcOffset(origin.LineOffset, clearance);
    }

    // ==========================================================================
    // HandleInflow / FinishInflow.
    // ==========================================================================

    private EStatus HandleInflow(Element child, BreakToken? childBreakToken, ref PreviousInflowPosition previousInflowPosition,
        InlineBreakToken? previousInlineBreakToken)
    {
        var childStyle = child.ComputedStyle!;

        // In this port a float resolves the container BFC block-offset at placement
        // time (see HandleFloat), so unlike the reference algorithm we must apply
        // clearance even after that offset is resolved.
        bool hasClearancePastAdjoiningFloats = IsBlockChild(child)
            && HasClearancePastAdjoiningFloats(_adjoiningObjectTypes, childStyle, Style);

        float? forcedBfcBlockOffset = null;
        bool isPushedByFloats = false;

        // If we can separate the previous margin strut from what is to follow, do
        // that. Then we're able to resolve *our* BFC block offset and position any
        // pending floats. There are two situations where this is necessary:
        //  1. If the child is to be cleared by adjoining floats.
        //  2. If the child is a non-empty inline.
        if (hasClearancePastAdjoiningFloats)
        {
            if (!ResolveBfcBlockOffset(ref previousInflowPosition))
                return AbortBfcBlockOffsetResolvedStatus();

            // If we had clearance past any adjoining floats, we already know where
            // the child is going to be (the child's margins won't have any effect).
            forcedBfcBlockOffset = _exclusionSpace.ClearanceOffset(childStyle.Clear);
            isPushedByFloats = true;
        }

        // Perform layout on the child.
        InflowChildData childData = ComputeChildData(previousInflowPosition, child, childBreakToken, /* is_new_fc */ false);
        childData.is_pushed_by_floats = isPushedByFloats;
        ConstraintSpace childSpace = CreateConstraintSpaceForChild(child, childBreakToken, childData, ChildAvailableSize(),
            /* is_new_fc */ false, forcedBfcBlockOffset, hasClearancePastAdjoiningFloats, previousInflowPosition.block_end_annotation_space);
        var layoutResult = LayoutInflowChild(childSpace, childBreakToken, child);

        // The rest of this function is continued within |FinishInflow|; however it
        // should be read as one function.
        return FinishInflow(child, childBreakToken, childSpace, hasClearancePastAdjoiningFloats, layoutResult, ref childData,
            ref previousInflowPosition);
    }

    private EStatus FinishInflow(Element child, BreakToken? childBreakToken, ConstraintSpace childSpace,
        bool hasClearancePastAdjoiningFloats, LayoutResult layoutResult, ref InflowChildData childData,
        ref PreviousInflowPosition previousInflowPosition)
    {
        // If a kNeedsLineClampRelayout layout result was not handled in
        // HandleNonsuccessfulLayoutResult, it needs to be propagated upwards until
        // the BFC root.
        if (layoutResult.Status == EStatus.NeedsLineClampRelayout)
        {
            Builder.NotifyNeedsLineClampRelayout();
            return EStatus.NeedsLineClampRelayout;
        }

        float? childBfcBlockOffset = layoutResult.BfcBlockOffsetValue;

        bool isSelfCollapsing = layoutResult.IsSelfCollapsing;

        // "Normal child" here means non-self-collapsing. Even self-collapsing
        // children may be cleared by floats, if they have a forced BFC block-offset.
        bool normalChildHadClearance = layoutResult.IsPushedByFloats && !isSelfCollapsing;

        // A child may have aborted its layout if it resolved its BFC block-offset.
        // If we don't have a BFC block-offset yet, we need to propagate the abort
        // signal up to our parent.
        if (layoutResult.Status == EStatus.BfcBlockOffsetResolved && !IsContainerBfcResolved())
        {
            _abortWhenBfcBlockOffsetUpdated = true;

            float bfcBlockOffset = childBfcBlockOffset ?? 0;

            if (normalChildHadClearance)
            {
                // If the child has the same clearance-offset as ourselves it means that
                // we should *also* resolve ourselves at that offset.
                if (Space.ClearanceOffset == childSpace.ClearanceOffset)
                {
                    _isPushedByFloats = true;
                }
                else
                {
                    bfcBlockOffset = NextBorderEdge(previousInflowPosition);
                }
            }

            if (!ResolveBfcBlockOffset(ref previousInflowPosition, bfcBlockOffset, /* forced_bfc_block_offset */ null))
                return AbortBfcBlockOffsetResolvedStatus();
        }

        // We have special behavior for a self-collapsing child which gets pushed
        // down due to clearance, see comment inside |ComputeInflowPosition|.
        bool selfCollapsingChildHadClearance = isSelfCollapsing && hasClearancePastAdjoiningFloats;

        // We try and position the child within the block formatting-context. This
        // may cause our BFC block-offset to be resolved, in which case we should
        // abort our layout if needed.
        if (!childBfcBlockOffset.HasValue)
        {
            if (childSpace.HasClearanceOffset && child.ComputedStyle!.Clear != ClearType.None)
            {
                // This is a self-collapsing child that we collapsed through, so we have
                // to detect clearance manually. See if the child's hypothetical border
                // edge is past the relevant floats. If it's not, we need to apply
                // clearance before it.
                float childBlockOffsetEstimate = BfcBlockOffset() + layoutResult.EndMarginStrut.Sum;
                if (childBlockOffsetEstimate < childSpace.ClearanceOffset)
                    selfCollapsingChildHadClearance = true;
            }
        }

        bool childHadClearance = selfCollapsingChildHadClearance || normalChildHadClearance;
        if (childHadClearance)
        {
            // The child has clearance. Clearance inhibits margin collapsing and acts
            // as spacing before the block-start margin of the child.
            if (!ResolveBfcBlockOffset(ref previousInflowPosition))
                return AbortBfcBlockOffsetResolvedStatus();
        }
        else if (layoutResult.SubtreeModifiedMarginStrut)
        {
            // The child doesn't have clearance, and modified its incoming
            // margin-strut. Propagate this information up to our parent if needed.
            SetSubtreeModifiedMarginStrutIfNeeded();
        }

        bool selfCollapsingChildNeedsRelayout = false;
        if (!childBfcBlockOffset.HasValue)
        {
            // Layout wasn't able to determine the BFC block-offset of the child.
            // This has to mean that the child is self-collapsing.
            if (IsContainerBfcResolved() && layoutResult.Status == EStatus.Success)
            {
                // Since we know our own BFC block-offset, though, we can calculate that
                // of the child as well.
                childBfcBlockOffset = PositionSelfCollapsingChildWithParentBfc(child, childSpace, childData, layoutResult);

                // We may need to relayout this child if it had any (adjoining) objects
                // which were positioned in the incorrect place.
                if (layoutResult.HasAdjoiningObjectDescendants && childBfcBlockOffset.Value != childSpace.ExpectedBfcBlockOffset)
                    selfCollapsingChildNeedsRelayout = true;
            }
        }
        else if (!childHadClearance && !isSelfCollapsing)
        {
            // Only non self-collapsing children are allowed resolve their parent's
            // BFC block-offset.
            //
            // The child's BFC block-offset is known, and since there's no clearance,
            // this container will get the same offset, unless it has already been
            // resolved.
            if (!ResolveBfcBlockOffset(ref previousInflowPosition, childBfcBlockOffset.Value))
                return AbortBfcBlockOffsetResolvedStatus();
        }

        // We need to re-layout a self-collapsing child if it was affected by
        // clearance in order to produce a new margin strut.
        if (selfCollapsingChildHadClearance)
        {
            MarginStrut marginStrut = new();
            marginStrut.Append(childData.margins.Top, HasMarginBlockStartQuirk(child));

            // We only need to relayout if the new margin strut is different to the
            // previous one.
            if (!childData.margin_strut.Equals(marginStrut))
            {
                childData.margin_strut = marginStrut;
                selfCollapsingChildNeedsRelayout = true;
            }
        }

        // We need to layout a child if we know its BFC block offset and:
        //  - It aborted its layout as it resolved its BFC block offset.
        //  - It has some unpositioned floats.
        //  - It was affected by clearance.
        if ((layoutResult.Status == EStatus.BfcBlockOffsetResolved || selfCollapsingChildNeedsRelayout) && childBfcBlockOffset.HasValue)
        {
            LayoutDiagnostics.CountBfcChildRetry();
            // If the child got pushed down by floats (normally because of clearance),
            // we need to carry over this state to the next layout pass.
            childData.is_pushed_by_floats = layoutResult.IsPushedByFloats;

            ConstraintSpace newChildSpace = CreateConstraintSpaceForChild(child, childBreakToken, childData, ChildAvailableSize(),
                /* is_new_fc */ false, childBfcBlockOffset.Value, /* has_clearance_past_adjoining_floats */ false,
                /* block_start_annotation_space */ 0);
            layoutResult = LayoutInflowChild(newChildSpace, childBreakToken, child);

            if (layoutResult.Status == EStatus.BfcBlockOffsetResolved)
            {
                // Even a second layout pass may abort, if the BFC block offset initially
                // calculated turned out to be wrong.
                childBfcBlockOffset = layoutResult.BfcBlockOffsetValue;

                newChildSpace = CreateConstraintSpaceForChild(child, childBreakToken, childData, ChildAvailableSize(),
                    /* is_new_fc */ false, childBfcBlockOffset, /* has_clearance_past_adjoining_floats */ false,
                    /* block_start_annotation_space */ 0);
                layoutResult = LayoutInflowChild(newChildSpace, childBreakToken, child);
            }

            if (layoutResult.Status != EStatus.Success)
                return layoutResult.Status;
        }

        float? lineBoxBfcBlockOffset = layoutResult.LineBoxBfcBlockOffset;

        // Block fragmentation is not applied here (see HandleNewFormattingContext).

        // It is now safe to update our version of the exclusion space, and any
        // propagated adjoining floats.
        if (layoutResult.ExclusionSpaceValue is { } es && es != _exclusionSpace)
            _exclusionSpace = es;
        _adjoiningObjectTypes = layoutResult.AdjoiningObjectTypes;
        _hasAdjoiningObjectDescendants = layoutResult.HasAdjoiningObjectDescendants;

        // If we don't know our BFC block-offset yet, and the child stumbled into
        // something that needs it (unable to position floats yet), we need to abort
        // layout, and trigger a re-layout once we manage to resolve it.
        if (!IsContainerBfcResolved())
        {
            if (layoutResult.AdjoiningObjectTypes != AdjoiningObjectTypes.None)
                _abortWhenBfcBlockOffsetUpdated = true;
            // If our BFC block offset is unknown, and the child got pushed down by
            // floats, so will we.
            if (layoutResult.IsPushedByFloats)
                _isPushedByFloats = true;
        }

        var fragment = layoutResult.Fragment;
        float fragmentInline = LogicalInlineSize(fragment);

        if (lineBoxBfcBlockOffset.HasValue)
            childBfcBlockOffset = lineBoxBfcBlockOffset;

        LogicalOffset logicalOffset = CalculateLogicalOffset(fragment, layoutResult.BfcLineOffset, childBfcBlockOffset);

        if (!PositionOrPropagateListMarker(layoutResult, ref logicalOffset, ref previousInflowPosition))
            return AbortBfcBlockOffsetResolvedStatus();

        if (fragment.Lines.Count > 0 || IsInlineLevelChild(child))
        {
            PropagateBaselineFromLineBox(fragment, logicalOffset.BlockOffset);
        }
        else
        {
            PropagateBaselineFromBlockChild(fragment, childData.margins, logicalOffset.BlockOffset);
        }

        fragment.BlockOffset = logicalOffset.BlockOffset;
        fragment.InlineOffset = logicalOffset.InlineOffset;
        fragment.MarginLeft = childData.margins.Left;
        fragment.MarginTop = childData.margins.Top;
        fragment.MarginRight = childData.margins.Right;
        fragment.MarginBottom = childData.margins.Bottom;
        Builder.AddChild(fragment);

        if (childBreakToken == null || !IsInParallelFlow(childBreakToken))
        {
            previousInflowPosition = ComputeInflowPosition(previousInflowPosition, child, childData, childBfcBlockOffset, logicalOffset,
                layoutResult, new LogicalSize(fragmentInline, LogicalBlockSize(fragment)), selfCollapsingChildHadClearance);
        }

        // Update |line_clamp_data_| from the LayoutResult, and abort if needed.
        if (IsContainerBfcResolved())
        {
            if (!_lineClampData.UpdateAfterLayout(layoutResult, ContainerBfcBlockOffset(), previousInflowPosition, Padding().Bottom))
            {
                Builder.NotifyNeedsLineClampRelayout();
                return EStatus.NeedsLineClampRelayout;
            }
        }

        return EStatus.Success;
    }

    // ==========================================================================
    // ComputeChildData.
    // ==========================================================================

    private InflowChildData ComputeChildData(PreviousInflowPosition previousInflowPosition, Element child,
        BreakToken? childBreakToken, bool isNewFc)
    {
        // Calculate margins in parent's writing mode.
        float additionalLineOffset = 0;
        BoxStrut margins = CalculateMargins(child, isNewFc, out additionalLineOffset);

        // Append the current margin strut with child's block start margin.
        MarginStrut marginStrut = previousInflowPosition.margin_strut;

        float logicalBlockOffset = previousInflowPosition.logical_block_offset;

        var childBlockBreakToken = childBreakToken as BlockBreakToken;
        if (childBlockBreakToken != null)
        {
            AdjustMarginsForFragmentation(childBlockBreakToken, ref margins);
            if (childBlockBreakToken.IsForcedBreak)
            {
                // After a forced fragmentainer break we need to reset the margin strut.
                marginStrut = MarginStrut.Zero;
            }
        }

        // The margins are recomputed below (the earlier calculation was only a
        // first pass used for the break-token adjustments above).
        BoxStrut fullMargins = CalculateMargins(child, isNewFc, out additionalLineOffset);
        marginStrut.Append(fullMargins.Top /* = Top */, HasMarginBlockStartQuirk(child));
        if (IsBlockChild(child))
            SetSubtreeModifiedMarginStrutIfNeeded(child.ComputedStyle!.MarginTop);

        var direction = Space.Direction;
        BfcOffset childBfcOffset = new(
            Space.GetBfcOffset().LineOffset + additionalLineOffset + LineLeft(fullMargins, direction),
            BfcBlockOffset() + logicalBlockOffset);

        return new InflowChildData(childBfcOffset, marginStrut, fullMargins);
    }

    // ==========================================================================
    // ComputeInflowPosition.

    private PreviousInflowPosition ComputeInflowPosition(PreviousInflowPosition previousInflowPosition, Element child,
        InflowChildData childData, float? childBfcBlockOffset, LogicalOffset logicalOffset, LayoutResult layoutResult,
        LogicalSize fragmentSize, bool selfCollapsingChildHadClearance)
    {
        // Determine the child's end logical offset, for the next child to use.
        float logicalBlockOffset;
        float? clearanceAfterLine = layoutResult.ClearanceAfterLine;
        float? trimBlockEndBy = layoutResult.TrimBlockEndBy;

        bool isSelfCollapsing = layoutResult.IsSelfCollapsing;
        if (isSelfCollapsing)
        {
            // The default behavior for self-collapsing children is they just pass
            // through the previous inflow position.
            logicalBlockOffset = previousInflowPosition.logical_block_offset;

            if (selfCollapsingChildHadClearance)
            {
                // If there's clearance, we must have applied that by now and thus
                // resolved our BFC block-offset.
                if (childBfcBlockOffset.HasValue)
                {
                    // First move past the margin that is to precede the clearance. It will
                    // not participate in any subsequent margin collapsing.
                    float marginBeforeClearance = previousInflowPosition.margin_strut.Sum;
                    logicalBlockOffset += marginBeforeClearance;

                    // Calculate and apply actual clearance.
                    float clearance = childBfcBlockOffset.Value - layoutResult.EndMarginStrut.Sum - NextBorderEdge(previousInflowPosition);
                    logicalBlockOffset += clearance;
                }
            }
            if (!IsContainerBfcResolved())
                logicalBlockOffset = 0;
        }
        else
        {
            logicalBlockOffset = logicalOffset.BlockOffset + fragmentSize.BlockSize;

            clearanceAfterLine = layoutResult.ClearanceAfterLine;
            trimBlockEndBy = layoutResult.TrimBlockEndBy;
            if (trimBlockEndBy.HasValue)
            {
                logicalBlockOffset -= trimBlockEndBy.Value;

                if (clearanceAfterLine.HasValue)
                    logicalBlockOffset += clearanceAfterLine.Value;
            }
            else
            {
                logicalBlockOffset += Math.Max(layoutResult.AnnotationOverflow, clearanceAfterLine ?? 0);
            }
        }

        MarginStrut marginStrut = layoutResult.EndMarginStrut;

        // Self collapsing child's end margin can "inherit" quirkiness from its
        // start margin.
        bool isQuirky = (isSelfCollapsing && HasMarginBlockStartQuirk(child)) || HasMarginBlockEndQuirk(child);
        marginStrut.Append(childData.margins.Bottom, isQuirky);
        if (IsBlockChild(child))
            SetSubtreeModifiedMarginStrutIfNeeded(child.ComputedStyle!.MarginBottom);

        // This flag is subtle, but in order to determine our size correctly we need
        // to check if our last child is self-collapsing, and it was affected by
        // clearance *or* an adjoining self-collapsing sibling was affected by
        // clearance.
        bool selfOrSiblingSelfCollapsingChildHadClearance
            = selfCollapsingChildHadClearance || (previousInflowPosition.self_collapsing_child_had_clearance && isSelfCollapsing);

        float annotationSpace = 0;
        if (!isSelfCollapsing && !trimBlockEndBy.HasValue)
        {
            annotationSpace = layoutResult.BlockEndAnnotationSpace;
            if (layoutResult.AnnotationOverflow > 0)
            {
                // Allow the portion of the annotation overflow that isn't also part of
                // clearance to overlap with certain types of subsequent content.
                annotationSpace = -Math.Max(0, layoutResult.AnnotationOverflow - (clearanceAfterLine ?? 0));
            }
        }

        return new PreviousInflowPosition(logicalBlockOffset, marginStrut, annotationSpace,
            selfOrSiblingSelfCollapsingChildHadClearance);

    }

    // ==========================================================================
    // PositionSelfCollapsingChildWithParentBfc.
    // ==========================================================================

    private float PositionSelfCollapsingChildWithParentBfc(Element child, ConstraintSpace childSpace, InflowChildData childData,
        LayoutResult layoutResult)
    {
        // The child must be an in-flow zero-block-size fragment, use its end
        // margin strut for positioning.
        float childBfcBlockOffset = childData.bfc_offset_estimate.BlockOffset + layoutResult.EndMarginStrut.Sum;

        ApplyClearance(childSpace, ref childBfcBlockOffset);

        return childBfcBlockOffset;
    }

    // ==========================================================================
    // CalculateLogicalOffset.
    // ==========================================================================

    private LogicalOffset CalculateLogicalOffset(BoxFragment fragment, float childBfcLineOffset, float? childBfcBlockOffset)
    {
        float containerInlineSize = _inlineSize;
        TextDirection direction = Space.Direction;

        float fragmentInlineSize = LogicalInlineSize(fragment);

        if (childBfcBlockOffset.HasValue && IsContainerBfcResolved())
        {
            var lo = LogicalFromBfcOffsets(new BfcOffset(childBfcLineOffset, childBfcBlockOffset.Value), ContainerBfcOffset(),
                fragmentInlineSize, containerInlineSize, direction);
            return lo;
        }

        float inlineOffset = LogicalFromBfcLineOffset(childBfcLineOffset, _containerBfcLineOffset, fragmentInlineSize,
            containerInlineSize, direction);

        // If we've reached here, either the parent, or the child don't have a BFC
        // block-offset yet. Children in this situation are always placed at a
        // logical block-offset of zero.
        return new LogicalOffset(inlineOffset, 0);
    }

    // ==========================================================================
    // ConsumeRemainingFragmentainerSpace.
    // ==========================================================================

    private void ConsumeRemainingFragmentainerSpace(ref PreviousInflowPosition previousInflowPosition)
    {
        if (Space.HasKnownFragmentainerBlockSize())
        {
            // The remaining part of the fragmentainer (the unusable space for child
            // content, due to the break) should still be occupied by this container.
            previousInflowPosition.logical_block_offset = Math.Max(previousInflowPosition.logical_block_offset,
                FragmentainerSpaceLeftForChildren());
        }
    }

    // ==========================================================================
    // FinalizeForFragmentation.
    // ==========================================================================

    private BreakStatus FinalizeForFragmentation()
    {
        // The engine's block algorithm does not fragment; multicol does. The
        // structure of the .cc method is retained for parity:
        //   - line-box widows/orphans early break handling
        //   - FinishFragmentationForFragmentainer / FinishFragmentation
        return BreakStatus.Continue;
    }

    // ==========================================================================
    // BreakBeforeChildIfNeeded.
    // ==========================================================================

    private BreakStatus BreakBeforeChildIfNeeded(Element child, LayoutResult layoutResult,
        ref PreviousInflowPosition previousInflowPosition, float bfcBlockOffset, bool hasContainerSeparation)
    {
        // Not reached by the engine (no block fragmentation in this algorithm).
        return BreakStatus.Continue;
    }

    // ==========================================================================
    // UpdateEarlyBreakBetweenLines.
    // ==========================================================================

    private void UpdateEarlyBreakBetweenLines()
    {
        // Not reached by the engine (no block fragmentation in this algorithm).
    }

    // ==========================================================================
    // Baseline propagation.
    // ==========================================================================

    private void PropagateBaselineFromLineBox(BoxFragment lineBox, float blockOffset)
    {
        // Line box baselines: use the first/last line's baseline offset relative
        // to the parent's content box.
        if (lineBox.Lines.Count == 0)
            return;
        float baseline = blockOffset + lineBox.Lines[^1].BaselineOffset;
        _firstBaseline ??= baseline;
        _lastBaseline = baseline;
    }

    private void PropagateBaselineFromBlockChild(BoxFragment child, BoxStrut margins, float blockOffset)
    {
        var childStyle = child.Element?.ComputedStyle;
        float baseline = blockOffset + Fonts.LineBoxMetrics.GetBaseline(childStyle);
        _firstBaseline ??= baseline;
        _lastBaseline = baseline;
    }

    // ==========================================================================
    // ResolveBfcBlockOffset.
    // ==========================================================================

    /// <summary>
    /// Abort this block's layout: its BFC block-offset has just been resolved, but
    /// content was already placed against another offset, so the fragment cannot be
    /// trusted. The aborted result carries no fragment, yet it must still report the
    /// offset that was resolved, because that is what lets the parent re-lay-out
    /// *this child* on its own (see <see cref="FinishInflow"/>) instead of
    /// propagating the abort to its own parent. Mirrors the reference engine's
    /// layout result falling back to the node's container BFC block offset when
    /// there is no fragment.
    /// </summary>
    private LayoutResult AbortBfcBlockOffsetResolved()
    {
        LayoutDiagnostics.CountBfcAbort();
        var result = LayoutResult.Abort(EStatus.BfcBlockOffsetResolved);
        result.BfcBlockOffsetValue = _containerBfcBlockOffset;
        result.BfcBlockOffset = BfcBlockOffset();
        result.BfcLineOffset = _containerBfcLineOffset;
        // A block that has resolved its BFC block-offset is by definition not
        // self-collapsing; clearance state carries over the same way as it does in
        // the successful result.
        result.IsSelfCollapsing = !_containerBfcBlockOffset.HasValue;
        result.IsPushedByFloats = _isPushedByFloats;
        return result;
    }

    private static EStatus AbortBfcBlockOffsetResolvedStatus()
    {
        LayoutDiagnostics.CountBfcAbort();
        return EStatus.BfcBlockOffsetResolved;
    }

    private bool NeedsAbortOnBfcBlockOffsetChange()
    {
        if (!_abortWhenBfcBlockOffsetUpdated)
            return false;
        return _containerBfcBlockOffset != Space.ExpectedBfcBlockOffset;
    }

    /// <summary>Resolve the container's BFC block offset, with the optional
    /// forced offset. Mirrors ResolveBfcBlockOffset().</summary>
    private bool ResolveBfcBlockOffset(ref PreviousInflowPosition previousInflowPosition, float bfcBlockOffset,
        float? forcedBfcBlockOffset)
    {
        // Clearance may have been resolved (along with BFC block-offset) in a
        // previous layout pass, so check the constraint space for pre-applied
        // clearance.
        if (Space.IsPushedByFloats)
            _isPushedByFloats = true;

        if (_containerBfcBlockOffset.HasValue)
            return true;

        bfcBlockOffset = forcedBfcBlockOffset ?? bfcBlockOffset;

        if (ApplyClearance(Space, ref bfcBlockOffset))
            _isPushedByFloats = true;

        _containerBfcBlockOffset = bfcBlockOffset;

        if (NeedsAbortOnBfcBlockOffsetChange())
        {
            // A formatting context root should always be able to resolve its
            // whereabouts before layout, so there should never be any incorrect
            // estimates that we need to go back and fix.
            return false;
        }

        // Set the offset to our block-start border edge. We'll now end up at the
        // block-start border edge.
        previousInflowPosition.logical_block_offset = 0;

        // Resolving the BFC offset normally means that we have finished collapsing
        // adjoining margins, so that we can reset the margin strut. One exception
        // here is if we're resuming after a break.
        if (!_isResuming)
            previousInflowPosition.margin_strut = MarginStrut.Zero;

        return true;
    }

    private bool ResolveBfcBlockOffset(ref PreviousInflowPosition previousInflowPosition, float bfcBlockOffset)
    {
        return ResolveBfcBlockOffset(ref previousInflowPosition, bfcBlockOffset, Space.ForcedBfcBlockOffset);
    }

    private bool ResolveBfcBlockOffset(ref PreviousInflowPosition previousInflowPosition)
    {
        return ResolveBfcBlockOffset(ref previousInflowPosition, NextBorderEdge(previousInflowPosition));
    }

    // ==========================================================================
    // CalculateQuirkyBodyMarginBlockSum.
    // ==========================================================================

    private float? CalculateQuirkyBodyMarginBlockSum(MarginStrut endMarginStrut)
    {
        if (!IsQuirkyAndFillsViewport())
            return null;
        if (Style.Height is not AutoLength)
            return null;
        if (Space.IsNewFormattingContext)
            return null;

        using var _scope = FontUnitContext.Use(Style);
        float blockEndMargin = Style.MarginBottom.ToPixels(Style.FontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
        // The |endMarginStrut| is the block-start margin if the body doesn't have
        // a resolved BFC block-offset.
        if (!_containerBfcBlockOffset.HasValue)
            return endMarginStrut.Sum + blockEndMargin;

        MarginStrut bodyStrut = endMarginStrut;
        bodyStrut.Append(blockEndMargin, HasMarginBlockEndQuirk(Node));
        return _containerBfcBlockOffset.Value - Space.GetBfcOffset().BlockOffset + bodyStrut.Sum;
    }

    // ==========================================================================
    // List markers.
    // ==========================================================================

    private bool ShouldPlaceUnpositionedListMarker()
    {
        if (Style.Display != DisplayType.ListItem)
            return false;
        if (Space.IsAnonymous() || Style.ListStyleType == ListStyleType.None)
            return false;
        return true;
    }

    private bool PositionOrPropagateListMarker(LayoutResult layoutResult, ref LogicalOffset contentOffset,
        ref PreviousInflowPosition previousInflowPosition)
    {
        // If there are no line boxes in this list item yet, the marker is kept for
        // the end of layout (see PositionListMarkerWithoutLineBoxes).
        return true;
    }

    /// <summary>
    /// Position the list marker without line boxes. The engine adds the marker as
    /// a line box at the block-start of the list item.
    /// </summary>
    private bool PositionListMarkerWithoutLineBoxes(ref PreviousInflowPosition previousInflowPosition)
    {
        if (!ShouldPlaceUnpositionedListMarker())
            return true;

        // If the marker was already positioned by a line box, don't add a new one.
        if (Builder.Lines.Any(l => l.Runs.Any(r => r.Text != null && IsMarkerRun(r))))
            return true;

        var markerText = ListMarker.MarkerText(Style.ListStyleType, ListItemIndex(Node));
        if (string.IsNullOrEmpty(markerText))
            return true;

        float fontSize = Style.FontSize;
        float markerWidth = ListMarker.MarkerWidth(Style.ListStyleType, Style.ListStylePosition, fontSize);

        // The marker shares the list item's first line box, so it uses the same
        // strut: line height from the resolved 'line-height', baseline from the
        // font's own ascent plus half-leading.
        var markerStrut = Fonts.LineBoxMetrics.GetStrut(Style);

        var markerLine = new BoxLine
        {
            InlineOffset = 0,
            BlockOffset = 0,
            InlineSize = markerWidth,
            BlockSize = markerStrut.LineHeight,
            BaselineOffset = markerStrut.Ascent,
        };
        markerLine.Runs.Add(new BoxRun
        {
            Text = markerText,
            InlineOffset = 0,
            InlineSize = markerWidth,
            BlockOffset = 0,
            BlockSize = markerStrut.LineHeight,
            BaselineOffset = markerStrut.Ascent,
        });
        Builder.Lines.Add(markerLine);
        return true;
    }

    private bool IsMarkerRun(BoxRun run) => run.Element == null && run.IsLineBreak == false && run.AtomicInlineBox == null;

    private static int ListItemIndex(Element element)
    {
        int index = 1;
        var parent = element.ParentElement;
        if (parent == null) return index;
        foreach (var sibling in parent.Children)
        {
            if (ReferenceEquals(sibling, element))
                return index;
            if (sibling is Element el && el.ComputedStyle?.Display == DisplayType.ListItem)
                index++;
        }
        return index;
    }

    // ==========================================================================
    // HandleTextControlPlaceholder.
    // ==========================================================================

    private float HandleTextControlPlaceholder(Element placeholder, PreviousInflowPosition previousInflowPosition)
    {
        if (!IsTextControl(Node))
            return previousInflowPosition.logical_block_offset;

        LogicalSize availableSize = ChildAvailableSize();
        bool applyFixedSize = false;

        InflowChildData childData = ComputeChildData(previousInflowPosition, placeholder, /* child_break_token */ null,
            /* is_new_fc */ false);
        ConstraintSpace space = CreateConstraintSpaceForChild(placeholder, null, childData, availableSize, false);
        var result = LayoutInflowChild(space, null, placeholder);
        LogicalOffset offset = BorderScrollbarPadding().StartOffset();
        return FinishTextControlPlaceholder(result, offset, applyFixedSize, previousInflowPosition);
    }

    private float FinishTextControlPlaceholder(LayoutResult result, LogicalOffset offset, bool applyFixedSize,
        PreviousInflowPosition previousInflowPosition)
    {
        var fragment = result.Fragment;
        fragment.BlockOffset = offset.BlockOffset;
        fragment.InlineOffset = offset.InlineOffset;
        Builder.AddChild(fragment);

        float blockOffset = previousInflowPosition.logical_block_offset;
        if (applyFixedSize)
            return blockOffset;
        return Math.Max(blockOffset, offset.BlockOffset + LogicalBlockSize(fragment));
    }

    // ==========================================================================
    // AdjustSliderThumbInlineOffset.
    // ==========================================================================

    private LogicalOffset AdjustSliderThumbInlineOffset(BoxFragment fragment, LogicalOffset logicalOffset)
    {
        // The engine has no slider-thumb ratio plumbing; the thumb keeps its
        // logical offset.
        return logicalOffset;
    }

    // ==========================================================================
    // CalculateMargins.
    // ==========================================================================

    private BoxStrut CalculateMargins(Element child, bool isNewFc, out float additionalLineOffset)
    {
        additionalLineOffset = 0;
        var childStyle = child.ComputedStyle!;

        if (IsInlineLevelChild(child))
            return new BoxStrut(0, 0, 0, 0);

        BoxStrut margins = ComputeMarginsFor(childStyle);

        if (isNewFc)
            return margins;

        float childInlineSize = ComputeChildInlineSize(child, childStyle);

        var style = Style;
        bool isRtl = IsRtl(StyleTextDirection(style));
        // Auto margins center against this box's own content width (which honors
        // an explicit width), not the outer constraint space.
        float availableSpace = Math.Max(0, OwnContentInlineSize());

        if (childStyle.MarginLeft is AutoLength || childStyle.MarginRight is AutoLength)
        {
            // Resolve auto-margins.
            ResolveInlineAutoMargins(childStyle, style, availableSpace, childInlineSize, ref margins);
        }
        else
        {
            additionalLineOffset = WebkitTextAlignAndJustifySelfOffset(childStyle, style, availableSpace, margins,
                () => childInlineSize);
        }

        if (isRtl)
            additionalLineOffset = ChildAvailableInlineSize - additionalLineOffset - childInlineSize - margins.HorizontalSum;

        return margins;
    }

    private BoxStrut ComputeMarginsFor(ComputedStyle childStyle)
    {
        float font = childStyle.FontSize;
        using var _scope = FontUnitContext.Use(childStyle);
        // Percentage margins resolve against the containing block's inline size.
        // For a child laid out here, that containing block is THIS box, so the
        // base is our own content inline size (ChildrenInlineSize) — the same
        // value we hand the child as its percentage base — NOT the base this box
        // was itself resolved against (Space.PercentageResolutionInlineSize),
        // which is one level too high and breaks nested narrow containers.
        float pctBase = LengthUtils.IsIndefinite(ChildrenInlineSize) ? 0 : ChildrenInlineSize;
        static float M(Length l, float font, float pctBase, ConstraintSpace sp) =>
            l is AutoLength ? 0
            : l is PercentLength p ? p.Value * pctBase : l.ToPixels(font, sp.RootFontSize, sp.ViewportWidth, sp.ViewportHeight);
        return new BoxStrut(
            M(childStyle.MarginTop, font, pctBase, Space),
            M(childStyle.MarginRight, font, pctBase, Space),
            M(childStyle.MarginBottom, font, pctBase, Space),
            M(childStyle.MarginLeft, font, pctBase, Space));
    }

    private float ComputeChildInlineSize(Element child, ComputedStyle childStyle)
    {
        var childBp = CombineStruts(BorderPaddingFor(child), PaddingFor(child));
        // Keep in sync with CreateConstraintSpaceForChild: an auto-width block's
        // margin box fills the containing block (CSS 2.1 §10.3.3), so the margins
        // are subtracted from the available inline size here as well.
        float availableInline = ChildAvailableInlineSize;
        if (childStyle.Width is AutoLength && IsBlockChild(child))
            availableInline = Math.Max(0, availableInline - ComputeMarginsFor(childStyle).HorizontalSum);
        var childSpace = Space.InheritBuilder(availableInline, float.PositiveInfinity).ToConstraintSpace();
        float inlineSize = LengthUtils.ComputeInlineSizeForFragment(childSpace, childStyle, childBp,
            t => new MinMaxSizesResult(new MinMaxSizes(availableInline, availableInline)));
        if (LengthUtils.IsIndefinite(inlineSize))
            return availableInline;
        return inlineSize;
    }

    private BoxStrut PaddingFor(Element child)
    {
        var s = child.ComputedStyle!;
        float font = s.FontSize;
        using var _scope = FontUnitContext.Use(s);
        // Child percentage padding resolves against its containing block inline
        // size = this box's content inline size (see ComputeMarginsFor).
        float pctBase = LengthUtils.IsIndefinite(ChildrenInlineSize) ? 0 : ChildrenInlineSize;
        static float P(Length l, float font, float pctBase, ConstraintSpace sp) =>
            l is PercentLength p ? p.Value * pctBase : l.ToPixels(font, sp.RootFontSize, sp.ViewportWidth, sp.ViewportHeight);
        return new BoxStrut(
            P(s.PaddingTop, font, pctBase, Space),
            P(s.PaddingRight, font, pctBase, Space),
            P(s.PaddingBottom, font, pctBase, Space),
            P(s.PaddingLeft, font, pctBase, Space));
    }

    // ==========================================================================
    // CreateConstraintSpaceForChild.
    // ==========================================================================

    private ConstraintSpace CreateConstraintSpaceForChild(Element child, BreakToken? childBreakToken, InflowChildData childData,
        LogicalSize childAvailableSize, bool isNewFc, float? childBfcBlockOffset = null, bool hasClearancePastAdjoiningFloats = false,
        float blockStartAnnotationSpace = 0)
    {
        var childStyle = child.ComputedStyle!;

        // An auto-width block child clamped by max-width must flow its own inline
        // content at the clamped width, not at the parent's full content width.
        float childAvailInline = childAvailableSize.InlineSize;
        if (IsBlockChild(child) && childStyle.Width is AutoLength)
        {
            if (childStyle.MaxWidth is PixelLength cmw && cmw.Value > 0)
            {
                var cbp = LengthUtils.ComputeBorders(childStyle);
                float limit = cmw.Value - (cbp.HorizontalSum + LengthUtils.ComputePadding(Space, childStyle).HorizontalSum);
                childAvailInline = Math.Min(childAvailInline, Math.Max(0, limit));
            }
            // CSS 2.1 §10.3.3: an in-flow block with 'width: auto' gets a margin
            // box exactly as wide as the containing block — the horizontal margins
            // come out of the auto width instead of overflowing beside it. Floats
            // and BFC roots (new formatting contexts) resolve their width from the
            // layout opportunity instead, so they're excluded here.
            if (!isNewFc)
                childAvailInline = Math.Max(0, childAvailInline - (childData.margins.Left + childData.margins.Right));
        }

        var builder = Space.InheritBuilder(childAvailInline, childAvailableSize.BlockSize);
        builder.SetIsNewFormattingContext(isNewFc);
        builder.SetAvailableSize(childAvailInline, childAvailableSize.BlockSize);
        // Percentages resolve against the containing block's content width, not the
        // margin-reduced auto width.
        builder.SetPercentageResolution(childAvailableSize.InlineSize, ChildPercentageBlockSize());
        builder.SetDirection(Space.Direction);

        bool hasBfcBlockOffset = _containerBfcBlockOffset.HasValue;

        // Propagate the |ConstraintSpace::ForcedBfcBlockOffset| down to our
        // children.
        if (!hasBfcBlockOffset && Space.ForcedBfcBlockOffset.HasValue)
            builder.SetForcedBfcBlockOffset(Space.ForcedBfcBlockOffset.Value);
        if (childBfcBlockOffset.HasValue && !isNewFc)
            builder.SetForcedBfcBlockOffset(childBfcBlockOffset.Value);

        // Propagate the |ConstraintSpace::AncestorHasClearancePastAdjoiningFloats|
        // flag down to our children.
        if (!hasBfcBlockOffset && Space.AncestorHasClearancePastAdjoiningFloats)
            builder.SetAncestorHasClearancePastAdjoiningFloats();
        if (hasClearancePastAdjoiningFloats)
            builder.SetAncestorHasClearancePastAdjoiningFloats();

        float clearanceOffset = float.MinValue;
        if (!IsBreakInside(childBreakToken as BlockBreakToken))
        {
            if (!Space.IsNewFormattingContext)
                clearanceOffset = Space.ClearanceOffset;
            if (IsBlockChild(child))
                clearanceOffset = Math.Max(clearanceOffset, _exclusionSpace.ClearanceOffset(childStyle.Clear));
        }
        builder.SetClearanceOffset(clearanceOffset);

        if (childData.is_pushed_by_floats)
        {
            // Clearance has been applied, but it won't be automatically detected
            // when laying out the child, since the BFC block-offset has already been
            // updated to be past the relevant floats.
            builder.SetIsPushedByFloats(true);
        }

        if (!isNewFc)
        {
            builder.SetMarginStrut(childData.margin_strut);
            builder.SetBfcLineOffset(childData.bfc_offset_estimate.LineOffset);
            builder.SetBfcBlockOffset(childData.bfc_offset_estimate.BlockOffset);
            builder.SetExpectedBfcBlockOffset(childData.bfc_offset_estimate.BlockOffset);
            builder.SetExclusionSpace(_exclusionSpace);
            if (!hasBfcBlockOffset)
                builder.SetAdjoiningObjectTypes(_adjoiningObjectTypes);
        }

        return builder.ToConstraintSpace();
    }

    // ==========================================================================
    // Interpolation helpers for table cells / alignment / fragmentation.
    // ==========================================================================

    private void FinalizeTableCellLayout(float unconstrainedIntrinsicBlockSize)
    {
        // Table-cell block-size adjustments live in the table layout algorithm.
    }

    // ==========================================================================
    // Fragmentation primitives (engine stubs, mirroring the simplified multicol
    // algorithm's infrastructure).
    // ==========================================================================

    private static bool IsBreakInside(BlockBreakToken? token)
    {
        return token != null && !token.IsBreakBefore && !token.IsRepeated;
    }

    private bool HasInflowChildBreakInside()
    {
        return false;
    }

    private bool HasKnownFragmentainerBlockSize()
    {
        return Space.HasDefiniteBlockSize;
    }

    private float FragmentainerSpaceLeftForChildren() => float.MaxValue;
    private float FragmentainerCapacityForChildren() => float.MaxValue;
    private float FragmentainerOffsetForChildren() => 0;
    private float FragmentainerOffsetAtBfc() => 0;

    private void AddBreakBeforeChild(Element child, BreakAppeal appeal, bool isForcedBreak) { }
    private bool MovePastBreakpoint(Element child, LayoutResult layoutResult, float fragmentainerBlockOffset, BreakAppeal appeal) => true;
    private void PropagateSpaceShortage(LayoutResult layoutResult, float bfcBlockOffset) { }

    private void HandleOofsAndSpecialDescendants()
    {
        if (_oofCandidates.Count == 0)
            return;
        var oofSpace = Space;
        var oofPart = new OutOfFlowLayoutPart(Builder, oofSpace);
        foreach (var candidate in _oofCandidates)
            oofPart.AddCandidate(candidate);
        oofPart.Run();
    }

    private static void AdjustMarginsForFragmentation(BlockBreakToken? breakToken, ref BoxStrut margins)
    {
        if (breakToken != null && breakToken.IsBreakBefore)
            margins = new BoxStrut(0, margins.Right, margins.Bottom, margins.Left);
    }

    private float ClampIntrinsicBlockSize(ConstraintSpace space, Element node, BreakToken? breakToken, BoxStrut borderPadding,
        float intrinsicSize, float? quirkyBodyMarginBlockSum)
    {
        var (minSize, maxSize) = LengthUtils.ComputeMinMaxBlockSizes(space, Style, borderPadding, null, _ => intrinsicSize);
        return Math.Clamp(intrinsicSize, minSize, maxSize);
    }

    private static MinMaxSizes ComputeInitialMinMaxBlockSizes(ConstraintSpace space, Element node, BoxStrut borderPadding)
    {
        float maxSize = float.MaxValue;
        if (node.ComputedStyle?.Height is PixelLength ph)
            maxSize = ph.Value + borderPadding.VerticalSum;
        return new MinMaxSizes(0, maxSize);
    }

    // ==========================================================================
    // Child classification helpers.
    // ==========================================================================

    private static ComputedStyle ChildStyle(Element child) => child.ComputedStyle!;

    private static bool IsOutOfFlowPositionedChild(Element child)
    {
        var s = child.ComputedStyle;
        return s != null && s.Position is PositionType.Absolute or PositionType.Fixed;
    }

    private static bool IsFloatingChild(Element child)
    {
        var s = child.ComputedStyle;
        return s != null && s.Float != FloatType.None;
    }

    private static bool IsListMarker(Element child) => false;

    private static bool IsColumnSpanAll(Element child) =>
        child.ComputedStyle?.ColumnSpanAll ?? false;

    private static bool IsTextControlPlaceholder(Element child) => false;

    private bool IsTextControl(Element node) => node.TagName is "INPUT" or "TEXTAREA";

    private static bool IsBlockChild(Element child)
    {
        var s = child.ComputedStyle;
        if (s == null) return true;
        return !IsInlineLevelChild(child);
    }

    private static bool IsInlineLevelChild(Element child)
    {
        var s = child.ComputedStyle;
        if (s == null) return false;
        if (s.Display is DisplayType.InlineBlock or DisplayType.InlineFlex or DisplayType.InlineGrid)
            return true;
        if (s.Display == DisplayType.Inline)
            return s.Position is not (PositionType.Absolute or PositionType.Fixed);
        return false;
    }

    private static bool CreatesNewFormattingContext(Element child)
    {
        var s = child.ComputedStyle;
        if (s == null) return false;
        if (s.Float != FloatType.None)
            return true;
        if (s.Position is PositionType.Absolute or PositionType.Fixed)
            return true;
        // CSS 2.1 §9.4.1: a box with a computed 'overflow' other than 'visible'
        // establishes a new block formatting context. That is what lets it sit beside
        // a float instead of overlapping it, and what makes it contain its own floats.
        if (s.Overflow != OverflowType.Visible || s.OverflowX != OverflowType.Visible
            || s.OverflowY != OverflowType.Visible)
            return true;
        // CSS Containment 3 §2.5: 'contain: layout' / 'paint' (and the shorthands
        // that include them) also create a new formatting context.
        if (s.Contain is ContainType.Strict or ContainType.Content or ContainType.Layout or ContainType.Paint)
            return true;
        return s.Display is DisplayType.Flex or DisplayType.InlineFlex or DisplayType.Grid or DisplayType.InlineGrid
            or DisplayType.Table or DisplayType.InlineBlock or DisplayType.TableCell or DisplayType.TableRow
            or DisplayType.TableRowGroup or DisplayType.TableHeaderGroup or DisplayType.TableFooterGroup
            or DisplayType.TableCaption or DisplayType.TableColumn or DisplayType.TableColumnGroup;
    }

    private static bool IsReplacedElement(Element element)
    {
        var tag = element.TagName;
        return tag == "IMG" || tag == "VIDEO" || tag == "CANVAS" || tag == "INPUT" || tag == "SVG";
    }

    private static bool HasMulticolStyle(Element element)
    {
        var s = element.ComputedStyle;
        if (s == null) return false;
        return s.ColumnCount > 0 || (s.ColumnWidth != null && s.ColumnWidth is not AutoLength);
    }

    private float LogicalInlineSize(BoxFragment fragment)
    {
        var wd = Space.GetWritingDirection();
        return wd.IsHorizontal ? fragment.InlineSize : fragment.BlockSize;
    }

    private float LogicalBlockSize(BoxFragment fragment)
    {
        var wd = Space.GetWritingDirection();
        return wd.IsHorizontal ? fragment.BlockSize : fragment.InlineSize;
    }

    private static bool ShouldIncludeBlockEndBorderPaddingInternal() => true;

    private bool ShouldTextBoxTrimStart() => _shouldTextBoxTrimNodeStart || _shouldTextBoxTrimFragmentainerStart;
    private bool ShouldTextBoxTrimEnd() => _shouldTextBoxTrimNodeEnd || _shouldTextBoxTrimFragmentainerEnd;
    private bool ShouldTextBoxTrim() => ShouldTextBoxTrimStart() || ShouldTextBoxTrimEnd();

    private void ClearShouldTextBoxTrimEnd()
    {
        _shouldTextBoxTrimNodeEnd = false;
        _shouldTextBoxTrimFragmentainerEnd = false;
    }
}

// ============================================================================
// Extension helpers used by the algorithm.
// ============================================================================

internal static class BlockLayoutAlgorithmExtensions
{
    public static float InlineStartFor(this BoxStrut strut, TextDirection direction)
    {
        return direction == TextDirection.Ltr ? strut.Left : strut.Right;
    }

    /// <summary>The offset of the content-box start edge in the logical
    /// coordinate system.</summary>
    public static LogicalOffset StartOffset(this BoxStrut strut) => new(strut.Left, strut.Top);

    public static bool IsInColumnBfc(this ConstraintSpace space) => false;

    public static void NotifyNeedsLineClampRelayout(this BoxFragmentBuilder builder)
    {
        // Informational hook; line clamp relayout is disabled for the engine.
    }
}

/// <summary>Implementation of the engine's IFC-root detection hook.</summary>
internal static class BlockLayoutAlgorithmNodeExtensions
{
    /// <summary>
    /// An element is an inline formatting context root when it contains inline
    /// text (or inline-level elements) and no in-flow block-level descendants.
    /// </summary>
    public static bool IsInlineFormattingContextRoot(this Element node)
    {
        bool hasText = false;
        bool hasInlineChild = false;
        bool hasInflowBlockChild = false;
        if (node.Children == null)
            return false;
        // A box that establishes a new formatting context must contain its floats
        // (CSS 2.1 §9.4.1 / §9.5.1); only the block path folds the float bottoms
        // into the box's block size. Treat a floating child as block-level here so
        // such a box is routed through BlockLayoutAlgorithm instead of the inline
        // path, which sizes only to its line boxes and lets the float overflow.
        // For a block-flow box the BFC triggers are a non-visible overflow or
        // layout/paint containment (float/abspos/flex/grid are not IFC roots).
        bool selfIsBfc = false;
        {
            var ns = node.ComputedStyle;
            if (ns != null)
                selfIsBfc = ns.Overflow != OverflowType.Visible || ns.OverflowX != OverflowType.Visible
                    || ns.OverflowY != OverflowType.Visible
                    || ns.Contain is ContainType.Strict or ContainType.Content or ContainType.Layout or ContainType.Paint;
        }
        foreach (var child in node.Children)
        {
            if (child is TextNode textNode)
            {
                // Collapsible whitespace-only text between block-level or
                // out-of-flow content does not establish an inline formatting
                // context. Counting it here misclassified a block whose only
                // real children are absolutely-positioned (with newlines between
                // them) as an inline root, which routed it to the inline path and
                // dropped its out-of-flow children entirely.
                if (!textNode.IsWhitespaceOnly)
                    hasText = true;
                continue;
            }
            if (child is not Element el)
                continue;
            var s = el.ComputedStyle;
            if (s == null || s.Display == DisplayType.None)
                continue;
            if (s.Position is PositionType.Absolute or PositionType.Fixed)
                continue;
            if (s.Float != FloatType.None)
            {
                if (selfIsBfc)
                    hasInflowBlockChild = true;
                continue;
            }
            if (s.Display is DisplayType.Inline or DisplayType.InlineBlock or DisplayType.InlineFlex or DisplayType.InlineGrid or DisplayType.Ruby || s.Display == DisplayType.Inline)
            {
                hasInlineChild = true;
                continue;
            }
            hasInflowBlockChild = true;
        }
        return (hasText || hasInlineChild) && !hasInflowBlockChild;
    }
}