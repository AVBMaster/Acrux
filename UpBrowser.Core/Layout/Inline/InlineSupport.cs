using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Inline;

/// <summary>
/// A context object given to inline layout. The same instance is given to
/// children of a parent node. Mirrors inline_child_layout_context.h.
/// </summary>
public class InlineChildLayoutContext
{
    public FragmentItemsBuilder ItemsBuilder { get; } = new();
    private LogicalLineItems? _tempLogicalLineItems;
    private InlineLayoutStateStack? _boxStates;
    private readonly List<BreakToken> _parallelFlowBreakTokens = new();
    private readonly LineInfo? _lineInfo;
    private float? _balancedAvailableWidth;

    public InlineLayoutStateStack? BoxStates => _boxStates;
    public IReadOnlyList<BreakToken> ParallelFlowBreakTokens => _parallelFlowBreakTokens;
    public float? BalancedAvailableWidth => _balancedAvailableWidth;

    public InlineChildLayoutContext(LineInfo? lineInfo = null)
    {
        _lineInfo = lineInfo;
    }

    public LineInfo GetLineInfo(InlineBreakToken? breakToken, out bool isCachedOut)
    {
        isCachedOut = false;
        if (_lineInfo != null) return _lineInfo;
        return new LineInfo();
    }

    public LogicalLineItems AcquireTempLogicalLineItems()
    {
        if (_tempLogicalLineItems != null)
        {
            var result = _tempLogicalLineItems;
            _tempLogicalLineItems = null;
            result.Clear();
            return result;
        }
        return new LogicalLineItems();
    }

    public void ReleaseTempLogicalLineItems(LogicalLineItems lineItems)
    {
        lineItems.Clear();
        _tempLogicalLineItems = lineItems;
    }

    public InlineLayoutStateStack ResetBoxStates()
    {
        _boxStates = new InlineLayoutStateStack();
        return _boxStates;
    }

    public void SetItemIndex(List<InlineItem> items, int itemIndex)
    {
        ItemsBuilder.StateStack.IsEmptyLine = itemIndex == 0;
    }

    public InlineLayoutStateStack? BoxStatesIfValidForItemIndex(List<InlineItem> items, int itemIndex)
    {
        if (_boxStates != null && itemIndex == 0)
            return _boxStates;
        return null;
    }

    public void ClearParallelFlowBreakTokens() => _parallelFlowBreakTokens.Clear();
    public void PropagateParallelFlowBreakToken(BreakToken token) => _parallelFlowBreakTokens.Add(token);
    public void SetBalancedAvailableWidth(float? value) => _balancedAvailableWidth = value;
}

/// <summary>
/// Truncates lines and places ellipsis for 'text-overflow: ellipsis'.
/// Mirrors line_truncator.cc.
/// </summary>
public class LineTruncator
{
    private readonly LineInfo _lineInfo;
    private ComputedStyle? _lineStyle;
    private float _availableWidth;
    private TextDirection _lineDirection;
    private string _ellipsisText = "\u2026";
    private float _ellipsisWidth;
    private bool _useFirstLineStyle;

    public LineTruncator(LineInfo lineInfo)
    {
        _lineInfo = lineInfo;
        _lineStyle = lineInfo.LineStyle();
        _availableWidth = lineInfo.AvailableInlineSize;
        _lineDirection = TextDirection.Ltr;
        var style = _lineStyle ?? new ComputedStyle();
        float measured = TextMeasureProxy.MeasureText(_ellipsisText, style);
        _ellipsisWidth = measured > 0 ? measured : _ellipsisText.Length * Math.Max(1, style.FontSize) * 0.5f;
    }

    /// <summary>
    /// Truncate |lineBox| and place ellipsis. Returns the new inline-size.
    /// Mirrors LineTruncator::TruncateLine().
    /// Hides items that do not fit and performs character-level truncation on
    /// the first overflowing item so the visible prefix + ellipsis fit.
    /// </summary>
    public float TruncateLine(float lineWidth, LogicalLineItems lineBox, InlineLayoutStateStack boxStates)
    {
        if (lineBox.Count == 0) return lineWidth;

        float usedWidth = 0;
        foreach (var child in lineBox)
            usedWidth += child.MarginLineLeft + child.InlineSize;

        float ellipsis = _ellipsisWidth;
        float available = Math.Max(0, _availableWidth - ellipsis);

        // Everything already fits within the available width.
        if (usedWidth <= available) return lineWidth;

        // Find the first item that overflows the remaining space.
        float width = 0;
        int keepCount = lineBox.Count;
        for (int i = 0; i < lineBox.Count; i++)
        {
            float w = lineBox[i].MarginLineLeft + lineBox[i].InlineSize;
            if (width + w > available)
            {
                keepCount = i;
                break;
            }
            width += w;
        }

        // The item that straddles the boundary is partially kept: trim its text
        // content at character granularity so the prefix + ellipsis fit.
        InlineItem? ellipsisInlineItem = null;
        if (keepCount < lineBox.Count)
        {
            var straddle = lineBox[keepCount];
            float remaining = Math.Max(0, available - width);
            bool truncated = false;
            if (!string.IsNullOrEmpty(straddle.TextContent) && straddle.InlineSize > 0 && remaining > 0)
            {
                var style = straddle.InlineItem?.Style() ?? _lineStyle ?? new ComputedStyle();
                string str = straddle.TextContent;
                int keepChars = 0;
                float prefixWidth = 0;
                for (int k = 1; k <= str.Length; k++)
                {
                    float pw = TextMeasureProxy.MeasureText(str[..k], style);
                    if (pw > remaining) break;
                    keepChars = k;
                    prefixWidth = pw;
                }
                if (keepChars < str.Length)
                {
                    straddle.TextContent = str[..keepChars];
                    straddle.InlineSize = prefixWidth;
                    straddle.TextOffset = new TextOffsetRange(straddle.StartOffset, straddle.StartOffset + keepChars);
                    width += prefixWidth;
                    truncated = true;
                }
            }
            if (!truncated)
                straddle.IsHiddenForPaint = true;
        }

        // Hide everything after the kept prefix.
        for (int i = keepCount + 1; i < lineBox.Count; i++)
            lineBox[i].IsHiddenForPaint = true;

        // The ellipsis is synthetic: give it the nearest preceding text item so
        // the paint pipeline can resolve its TextNode (and therefore font/color)
        // instead of dropping the run for lacking a node.
        for (int i = Math.Min(keepCount, lineBox.Count - 1); i >= 0; i--)
        {
            var candidate = lineBox[i].InlineItem;
            if (candidate != null && candidate.Type == InlineItem.InlineItemType.Text)
            {
                ellipsisInlineItem = candidate;
                break;
            }
        }

        // Place ellipsis at the truncated end of the line. The items in
        // |lineBox| are already in visual order (bidi reorder ran in CreateLine),
        // so for an RTL paragraph the ellipsis goes to the visual left edge —
        // that is where the clipped overflow was (CSS Overflow 3 §4.5).
        bool rtlVisual = _lineInfo.BaseDirection() == TextDirection.Rtl;
        var ellipsisItem = new LogicalLineItem
        {
            TextContent = _ellipsisText,
            InlineSize = ellipsis,
            Rect = new LogicalRect(rtlVisual ? 0 : width, 0, ellipsis, 16),
            HasBidiLevel = true,
            InlineItem = ellipsisInlineItem,
        };
        if (rtlVisual)
        {
            for (int i = 0; i < lineBox.Count; i++)
            {
                var it = lineBox[i];
                it.Rect = new LogicalRect(it.Rect.InlineStart + ellipsis, it.Rect.BlockStart, it.Rect.InlineSize, it.Rect.BlockSize);
            }
            lineBox.InsertChild(0, ellipsisItem);
        }
        else
        {
            lineBox.AddChild(ellipsisItem);
        }

        return width + ellipsis;
    }

    /// <summary>
    /// Truncate the line in the middle, keeping the first and last parts.
    /// Mirrors TruncateLineInTheMiddle().
    /// </summary>
    public float TruncateLineInTheMiddle(float lineWidth, LogicalLineItems lineBox, InlineLayoutStateStack boxStates)
    {
        if (lineBox.Count < 2) return TruncateLine(lineWidth, lineBox, boxStates);

        float ellipsis = _ellipsisWidth;
        float half = Math.Max(0, (_availableWidth - ellipsis) / 2);

        float w = 0;
        int firstEnd = 0;
        for (int i = 0; i < lineBox.Count; i++)
        {
            float cw = lineBox[i].InlineSize;
            if (w + cw > half) break;
            w += cw;
            firstEnd = i + 1;
        }

        float w2 = 0;
        int lastStart = lineBox.Count;
        for (int i = lineBox.Count - 1; i >= firstEnd; i--)
        {
            float cw = lineBox[i].InlineSize;
            if (w2 + cw > half) break;
            w2 += cw;
            lastStart = i;
        }

        for (int i = 0; i < lineBox.Count; i++)
        {
            if (i >= firstEnd && i < lastStart)
                lineBox[i].IsHiddenForPaint = true;
        }

        var ellipsisItem = new LogicalLineItem
        {
            TextContent = _ellipsisText,
            InlineSize = ellipsis,
            Rect = new LogicalRect(w, 0, ellipsis, 16),
            HasBidiLevel = true,
            InlineItem = firstEnd > 0 ? lineBox[firstEnd - 1].InlineItem : null,
        };
        lineBox.AddChild(ellipsisItem);

        return w + ellipsis + w2;
    }
}

/// <summary>
/// Utility functions for computing caret rect in the modern layout pipeline.
/// Mirrors caret_rect.h.
/// </summary>
public static class CaretRect
{
    /// <summary>Given an inline caret position, returns the local caret rect.</summary>
    public static PhysicalRect ComputeLocalCaretRect(InlineCaretPosition position, float fontSize = 16)
    {
        float x = position.IsBefore ? position.TextOffset : position.TextOffset + 1;
        return new PhysicalRect(x * fontSize * 0.5f, 0, 1, fontSize);
    }

    /// <summary>Adjusts the returned rect to span the line box in the block direction.</summary>
    public static PhysicalRect ComputeLocalSelectionRect(InlineCaretPosition position, float fontSize = 16, float lineHeight = 16)
    {
        float x = position.IsBefore ? position.TextOffset : position.TextOffset + 1;
        return new PhysicalRect(x * fontSize * 0.5f, 0, 1, lineHeight);
    }
}

/// <summary>
/// Utility functions for line layout. Mirrors line_utils.h.
/// </summary>
public static class LineUtils
{
    public static float ComputeLineBoxInlineSize(LogicalLineItems lineBox)
    {
        float w = 0;
        foreach (var item in lineBox)
            w += item.MarginLineLeft + item.InlineSize;
        return w;
    }
}