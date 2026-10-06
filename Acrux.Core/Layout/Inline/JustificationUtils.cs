using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Inline;

/// <summary>
/// Text-align application for laid-out logical lines. Implements the alignment
/// half of the engine's line breaker / shape-result spacing split: the line breaker records
/// expansion opportunities (breakable space runs), and this pass distributes the
/// free inline space into those opportunities (justify) or offsets the whole line
/// (end/center) once the natural content width is known.
/// </summary>
public static class JustificationUtils
{
    /// <summary>
    /// Place <paramref name="items"/> inside a content box of
    /// <paramref name="contentBoxInlineSize"/> according to the line's text-align.
    /// Must run AFTER <see cref="LogicalLineBuilder.CreateLine"/> has produced final
    /// natural positions/sizes and BEFORE run X coordinates are materialized.
    /// </summary>
    public static void ApplyTextAlignment(LineInfo info, LogicalLineItems items, float contentBoxInlineSize)
    {
        if (items == null || items.Count == 0)
            return;

        var align = ResolveEffectiveAlign(info);
        var source = info.ItemsData()?.TextContent ?? string.Empty;

        // A whitespace-only run at the end of a wrapped line hangs off the edge: it is
        // neither part of the line's content extent nor a justification opportunity
        // (CSS Text 3 4.3, verified against Chrome).
        int lastMeaningful = -1;
        var spaceRun = new bool[items.Count];
        var expansion = new bool[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            spaceRun[i] = IsSpaceRun(items[i], source);
            expansion[i] = IsExpansionRun(items[i], source);
            if (!spaceRun[i])
                lastMeaningful = i;
        }

        // Natural content extent = right edge of the right-most item.
        float naturalExtent = 0;
        for (int i = 0; i <= lastMeaningful; i++)
        {
            var it = items[i];
            if (!ParticipatesInAlignment(it))
                continue;
            naturalExtent = MathF.Max(naturalExtent, it.Rect.InlineStart + it.Rect.InlineSize);
        }

        float free = MathF.Max(0f, contentBoxInlineSize - naturalExtent);

        switch (align)
        {
            // The line breaker stacks items from the left in either direction,
            // so the edge that needs the free-space shift depends on the base
            // direction: in LTR it is 'end' (right), in RTL it is 'start'
            // (right) while 'end' stays at the left edge.
            case TextAlignType.End when IsLtr(info.BaseDirection()):
            case TextAlignType.Start when IsRtl(info.BaseDirection()):
                ShiftAll(items, free);
                break;

            case TextAlignType.Center:
                ShiftAll(items, free / 2f);
                break;

            case TextAlignType.Justify when ShouldJustifyLine(info):
                ApplyJustifyExpansion(items, contentBoxInlineSize - info.TextIndent(), naturalExtent, expansion, lastMeaningful);
                break;
        }
    }

    /// <summary>
    /// A line that ends in a forced break is the last line of its inline block, so it
    /// is not justified unless 'text-align-last' says otherwise (CSS Text 4 4.3).
    /// </summary>
    private static bool ShouldJustifyLine(LineInfo info)
    {
        if (info.LineStyle().TextAlignLast == TextAlignLastType.Justify)
            return true;
        return !info.IsLastLine() && !info.HasForcedBreak();
    }

    private static TextAlignType ResolveEffectiveAlign(LineInfo info)
    {
        var align = info.TextAlign();
        var dir = info.BaseDirection();

        // Map physical to logical for directional values (LTR base assumed for
        // Start; RTL flips Left/Right like the engine's text-align-line logic).
        return align switch
        {
            TextAlignType.Left => IsLtr(dir) ? TextAlignType.Start : TextAlignType.End,
            TextAlignType.Right => IsRtl(dir) ? TextAlignType.Start : TextAlignType.End,
            _ => align,
        };
    }

    private static void ShiftAll(LogicalLineItems items, float delta)
    {
        if (delta <= 0)
            return;
        for (int i = 0; i < items.Count; i++)
            items[i].MoveInInlineDirection(delta);
    }

    /// <summary>
    /// Distribute the free space into word-spacing expansion opportunities.
    /// Every opportunity absorbs an equal share and the run's own advance grows so
    /// following items shift cumulatively. Trailing (hanging) spaces never expand.
    /// |opportunities| is the expansion set, not the collapsible-space set.
    /// </span>
    /// </summary>
    private static void ApplyJustifyExpansion(LogicalLineItems items,
        float contentBoxInlineSize, float naturalExtent, bool[] opportunities, int lastMeaningful)
    {
        float free = contentBoxInlineSize - naturalExtent;
        if (free <= 0 || lastMeaningful < 0)
            return;

        // Every collapsible space run counts as ONE opportunity regardless of how many
        // collapsed spaces it represents; a run of no-break spaces counts one each,
        // which is what a reference browser measures (a justified line adds the same
        // amount to its NBSP as to its spaces).
        List<int> points = new();
        for (int i = 0; i < lastMeaningful; i++)
        {
            if (opportunities[i])
                points.Add(i);
        }

        if (points.Count == 0)
            return;

        float expansion = free / points.Count;

        float accumulatedShift = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (accumulatedShift != 0)
                it.MoveInInlineDirection(accumulatedShift);

            if (points.BinarySearch(i) >= 0)
            {
                // Widen the space run itself so paint/picking see the full advance.
                it.InlineSize += expansion;
                accumulatedShift += expansion;
            }
        }
    }

    private static bool IsSpaceRun(LogicalLineItem item, string source)
    {
        if (item.InlineItem is not { Type: InlineItem.InlineItemType.Text })
            return false;

        int start = item.TextOffset.Start;
        int end = item.TextOffset.End;
        if (end <= start || end > source.Length)
            return false;

        for (int i = start; i < end; i++)
        {
            if (!Character.IsBreakableSpace(source[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Whether this item receives part of the free space a justified line distributes.
    ///
    /// It is a separate predicate from <see cref="IsSpaceRun"/> on purpose: the two answer
    /// different questions. Breaking and trailing-space trimming ask "is this collapsible
    /// whitespace"; justification asks "is this an expansion opportunity", and the two
    /// differ on the no-break space — U+00A0 occupies space without ever being a break
    /// point, and a reference browser stretches it by the same share as a U+0020 while
    /// U+2003, U+2009 and U+3000 keep their natural advance (measured on b196's twelve
    /// cases). <c>LogicalLineBuilder.AddTextChildren</c> cuts a text run at every NBSP so
    /// this predicate can see one; the other space separators are left inside their word.
    /// </summary>
    private static bool IsExpansionRun(LogicalLineItem item, string source)
    {
        if (IsSpaceRun(item, source))
            return true;

        if (item.InlineItem is not { Type: InlineItem.InlineItemType.Text })
            return false;

        int start = item.TextOffset.Start;
        int end = item.TextOffset.End;
        if (end <= start || end > source.Length)
            return false;

        for (int i = start; i < end; i++)
        {
            if (source[i] != '\u00A0')
                return false;
        }
        return true;
    }

    private static bool ParticipatesInAlignment(LogicalLineItem item) =>
        item.HasInFlowFragment() && !item.IsHiddenForPaint;

    private static bool IsLtr(TextDirection d) => d != TextDirection.Rtl;
    private static bool IsRtl(TextDirection d) => d == TextDirection.Rtl;
}
