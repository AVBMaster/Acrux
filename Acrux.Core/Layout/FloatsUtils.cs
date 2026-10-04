using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;
using System.Linq;

namespace Acrux.Core.Layout;

/// <summary>
/// Float positioning utilities. Mirrors floats_utils.cc.
/// </summary>
public static class FloatsUtils
{
    public static float ComputeMarginBoxInlineSizeForUnpositionedFloat(Element node, ConstraintSpace space)
    {
        var style = node.ComputedStyle;
        if (style == null) return 0;
        // Auto margins resolve to zero while the float's margin box is being measured.
        static float Margin(Length l, float font, ConstraintSpace sp) => l is AutoLength ? 0
            : l.ToPixels(font, sp.RootFontSize, sp.ViewportWidth, sp.ViewportHeight);
        return Margin(style.MarginLeft, style.FontSize, space) + Margin(style.MarginRight, style.FontSize, space);
    }

    public static PositionedFloat PositionFloat(float origin_inline_offset, UnpositionedFloat unpositioned_float)
    {
        return new PositionedFloat
        {
            BfcOffset = new BfcOffset(origin_inline_offset, unpositioned_float.BfcBlockOffset),
            LayoutResult = unpositioned_float.LayoutResult,
        };
    }
}

public struct UnpositionedFloat
{
    public float BfcBlockOffset;
    public float BfcLineOffset;
    public PhysicalSize Size;
    public LayoutResult? LayoutResult;
    public bool IsLeft;
}

public struct PositionedFloat
{
    public BfcOffset BfcOffset;
    public LayoutResult? LayoutResult;
}

/// <summary>Inline range a single line box may use, as reported by
/// <see cref="ExclusionSpace.LineSpaceAt"/>.</summary>
public struct FloatLineSpace
{
    public float LineStart;
    public float LineEnd;
    /// <summary>Block offset to restart the line at when no inline room is left,
    /// or NaN when the line fits.</summary>
    public float PushDownTo;
}

/// <summary>
/// Manages the exclusion space (floats + initial letter boxes) for a block
/// formatting context. Mirrors ExclusionSpace / ExclusionSpaceInternal in
/// exclusion_space.cc (simplified, no segment tree).
/// </summary>
public class ExclusionSpace
{
    private readonly List<ExclusionArea> _exclusions = new();
    private float _leftClearOffset;
    private float _rightClearOffset;
    private float _initialLetterLeftClearOffset;
    private float _initialLetterRightClearOffset;

    public IReadOnlyList<ExclusionArea> AllExclusions => _exclusions;

    /// <summary>True when anything (a float or an initial-letter box) occupies this
    /// space, i.e. when line boxes may have to step around it.</summary>
    public bool HasExclusions => _exclusions.Count > 0;

    public void Add(ExclusionArea exclusion)
    {
        _exclusions.Add(exclusion);
        if (exclusion.ExclusionKind == ExclusionArea.Kind.Float)
        {
            if (exclusion.Type == FloatType.Left)
                _leftClearOffset = Math.Max(_leftClearOffset, exclusion.Rect.BlockEndOffset);
            else
                _rightClearOffset = Math.Max(_rightClearOffset, exclusion.Rect.BlockEndOffset);
        }
        else
        {
            if (exclusion.Type == FloatType.Left)
                _initialLetterLeftClearOffset = Math.Max(_initialLetterLeftClearOffset, exclusion.Rect.BlockEndOffset);
            else
                _initialLetterRightClearOffset = Math.Max(_initialLetterRightClearOffset, exclusion.Rect.BlockEndOffset);
        }
    }

    public float ClearanceOffset(ClearType clearType)
    {
        return clearType switch
        {
            ClearType.None => float.MinValue,
            ClearType.Left => _leftClearOffset,
            ClearType.Right => _rightClearOffset,
            ClearType.Both => Math.Max(_leftClearOffset, _rightClearOffset),
            _ => float.MinValue,
        };
    }

    public float ClearanceOffsetIncludingInitialLetter(ClearType clearType)
    {
        float baseOffset = ClearanceOffset(clearType);
        float letterOffset = clearType switch
        {
            ClearType.Left => _initialLetterLeftClearOffset,
            ClearType.Right => _initialLetterRightClearOffset,
            ClearType.Both => Math.Max(_initialLetterLeftClearOffset, _initialLetterRightClearOffset),
            _ => 0,
        };
        return Math.Max(baseOffset, letterOffset);
    }

    /// <summary>
    /// The clearance offset to use when sizing an element that establishes a new
    /// formatting context, ignoring floats hidden for paint.
    /// Mirrors ExclusionSpace::NonHiddenClearanceOffsetIncludingInitialLetter().
    /// </summary>
    public float NonHiddenClearanceOffsetIncludingInitialLetter()
    {
        float maxClear = 0;
        foreach (var e in _exclusions)
        {
            if (e.IsHiddenForPaint) continue;
            maxClear = Math.Max(maxClear, e.IsForInitialLetterBox
                ? ClearanceOffsetIncludingInitialLetter(e.Type == FloatType.Left ? ClearType.Left : ClearType.Right)
                : ClearanceOffset(e.Type == FloatType.Left ? ClearType.Left : ClearType.Right));
        }
        maxClear = Math.Max(maxClear, ClearanceOffset(ClearType.Both));
        maxClear = Math.Max(maxClear, Math.Max(_initialLetterLeftClearOffset, _initialLetterRightClearOffset));
        return maxClear;
    }

    /// <summary>Create an independent copy of this exclusion space.</summary>
    public ExclusionSpace Copy()
    {
        var copy = new ExclusionSpace();
        foreach (var exclusion in _exclusions)
            copy.Add(exclusion);
        return copy;
    }

    /// <summary>
    /// Inline range available to a line box that occupies
    /// [blockOffset, blockOffset + blockSize) inside the content interval
    /// [contentLineStart, contentLineEnd], after stepping around the floats that
    /// intersect it. All offsets are in the formatting context's coordinate space.
    /// CSS 2.1 §9.5.2: only line boxes avoid floats - block boxes do not.
    /// </summary>
    public FloatLineSpace LineSpaceAt(float blockOffset, float blockSize, float contentLineStart, float contentLineEnd)
    {
        var space = new FloatLineSpace
        {
            LineStart = contentLineStart,
            LineEnd = contentLineEnd,
            PushDownTo = float.NaN,
        };

        float lineEnd = blockOffset + Math.Max(0, blockSize);
        float overlappingBottom = float.MinValue;
        foreach (var e in _exclusions)
        {
            float eStart = e.Rect.BlockStartOffset;
            float eEnd = e.Rect.BlockEndOffset;
            if (eEnd <= blockOffset || eStart >= lineEnd)
                continue;
            overlappingBottom = Math.Max(overlappingBottom, eEnd);
            if (e.Type == FloatType.Left)
                space.LineStart = Math.Max(space.LineStart, e.Rect.LineEndOffset);
            else
                space.LineEnd = Math.Min(space.LineEnd, e.Rect.LineStartOffset);
        }

        if (space.LineEnd < space.LineStart)
            space.LineEnd = space.LineStart;

        // No room left on this line: the line box has to start below the floats
        // (CSS 2.1 §9.5.2, "if a line box would be too narrow the block is pushed
        // below"). Reported so the driver can advance the vertical position.
        if (space.LineEnd - space.LineStart <= 0.5f && overlappingBottom != float.MinValue)
            space.PushDownTo = overlappingBottom;

        return space;
    }

    /// <summary>
    /// Inline range left on the line at |blockOffset| for a float that wants to sit
    /// there, plus the block offset of the next line below the floats blocking it.
    /// This is how floats end up side by side and then stacked below each other
    /// (CSS 2.1 §9.5.1).
    /// </summary>
    public (float LineStart, float LineEnd, float NextBlockOffset) FloatLineAt(
        float blockOffset, float contentLineStart, float contentLineEnd)
    {
        float lineStart = contentLineStart;
        float lineEnd = contentLineEnd;
        float next = float.MaxValue;
        foreach (var e in _exclusions)
        {
            float eStart = e.Rect.BlockStartOffset;
            float eEnd = e.Rect.BlockEndOffset;
            if (eEnd <= blockOffset || eStart > blockOffset)
                continue;
            if (e.Type == FloatType.Left)
                lineStart = Math.Max(lineStart, e.Rect.LineEndOffset);
            else
                lineEnd = Math.Min(lineEnd, e.Rect.LineStartOffset);
            next = Math.Min(next, eEnd);
        }
        if (lineEnd < lineStart)
            lineEnd = lineStart;
        return (lineStart, lineEnd, next);
    }

    public LayoutOpportunity FindLayoutOpportunity(BfcOffset offset, float availableInlineSize, float minimumInlineSize = 0)
    {
        float maxClear = Math.Max(_leftClearOffset, _rightClearOffset);
        maxClear = Math.Max(maxClear, Math.Max(_initialLetterLeftClearOffset, _initialLetterRightClearOffset));

        if (offset.BlockOffset >= maxClear)
        {
            var end = new BfcOffset(offset.LineOffset + Math.Max(0, availableInlineSize), float.MaxValue);
            return new LayoutOpportunity(new BfcRect(offset, end));
        }

        float lineStart = offset.LineOffset;
        foreach (var e in _exclusions)
        {
            float eStart = e.Rect.BlockStartOffset;
            float eEnd = e.Rect.BlockEndOffset;
            if (eEnd <= offset.BlockOffset || eStart >= offset.BlockOffset + availableInlineSize)
                continue;
            if (e.Type == FloatType.Left)
                lineStart = Math.Max(lineStart, e.Rect.LineEndOffset);
        }

        float lineEnd = offset.LineOffset + availableInlineSize;
        for (int i = _exclusions.Count - 1; i >= 0; i--)
        {
            var e = _exclusions[i];
            float eStart = e.Rect.BlockStartOffset;
            float eEnd = e.Rect.BlockEndOffset;
            if (eEnd <= offset.BlockOffset || eStart >= offset.BlockOffset + availableInlineSize)
                continue;
            if (e.Type == FloatType.Right)
                lineEnd = Math.Min(lineEnd, e.Rect.LineStartOffset);
        }

        if (lineStart >= lineEnd)
            lineStart = lineEnd;

        var startOff = new BfcOffset(lineStart, offset.BlockOffset);
        var endOff = new BfcOffset(lineEnd, float.MaxValue);
        return new LayoutOpportunity(new BfcRect(startOff, endOff));
    }

    public List<LayoutOpportunity> AllLayoutOpportunities(BfcOffset offset, float availableInlineSize)
    {
        var opportunities = new List<LayoutOpportunity>();
        var main = FindLayoutOpportunity(offset, availableInlineSize);
        opportunities.Add(main);
        float maxClear = Math.Max(_leftClearOffset, _rightClearOffset);
        maxClear = Math.Max(maxClear, Math.Max(_initialLetterLeftClearOffset, _initialLetterRightClearOffset));

        if (offset.BlockOffset < maxClear)
        {
            // If there are right floats, there may be a second opportunity on the left.
            bool hasRightFloat = _exclusions.Any(e => e.Type == FloatType.Right);
            if (hasRightFloat)
            {
                var rightOff = new BfcOffset(offset.LineOffset, offset.BlockOffset);
                var rightEnd = new BfcOffset(offset.LineOffset + availableInlineSize, float.MaxValue);
                opportunities.Add(new LayoutOpportunity(new BfcRect(rightOff, rightEnd)));
            }
        }
        return opportunities;
    }
}