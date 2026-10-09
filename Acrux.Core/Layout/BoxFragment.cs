using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout.Inline;

namespace Acrux.Core.Layout;

/// <summary>
/// The geometric result of laying out a box in a constraint space.
/// Mirrors the concept of the engine's physical box fragment: an immutable
/// snapshot of the box's position and size within its containing block.
/// </summary>
public class BoxFragment
{
    public float InlineOffset { get; set; }   // x
    public float BlockOffset { get; set; }    // y
    public float InlineSize { get; set; }     // width
    public float BlockSize { get; set; }      // height

    public float MarginLeft { get; set; }
    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }

    public float BorderLeft { get; set; }
    public float BorderTop { get; set; }
    public float BorderRight { get; set; }
    public float BorderBottom { get; set; }

    public float PaddingLeft { get; set; }
    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }

    public Element? Element { get; set; }
    public List<BoxFragment> Children { get; } = new();
    public List<BoxLine> Lines { get; } = new();

    /// <summary>The inline size a 'scrollbar-gutter' took off this box's content area at layout
    /// time (CSS Overflow 3 §3.5), which is what the converter turns back into the scrollport's
    /// width. Only the gutter is recorded here: the bar an overflowing scroller shows is computed
    /// from the same fragment on the converter's side, and a number covering both would be
    /// subtracted twice. Measured: a 120px box with a 15px bar and 'stable both-edges' reports
    /// clientWidth 90 whether or not a bar can ever appear in it.</summary>
    public float GutterInlineReservation { get; set; }

    // A5: resolved multicol geometry for column-rule painting (exported to
    // Dom.LayoutBox by AuroraFragmentConverter).
    public bool IsMultiColumn { get; set; }
    public int UsedColumnCount { get; set; }
    public float ColumnInlineSize { get; set; }
    public float ColumnProgression { get; set; }

    public FragmentItems? FragmentItems { get; set; }

    public bool IsFloating { get; set; }
    public bool IsInlineBox { get; set; }
    public bool IsBlockInInline { get; set; }
    public bool IsOutOfFlowPositioned { get; set; }

    // Fragmentation properties (from FragmentRepeater / fragment tree).
    public LayoutObject? LayoutObject { get; set; }
    public bool IsFirstForNode { get; set; }
    public BlockBreakToken? BreakToken { get; set; }
    public bool IsFragmentationContextRoot { get; set; }
    public bool IsFragmentainerBox { get; set; }

    public float ContentInlineSize =>
        Math.Max(0, InlineSize - BorderLeft - BorderRight - PaddingLeft - PaddingRight);
    public float ContentBlockSize =>
        Math.Max(0, BlockSize - BorderTop - BorderBottom - PaddingTop - PaddingBottom);
    public float ContentInlineOffset => InlineOffset + BorderLeft + PaddingLeft;
    public float ContentBlockOffset => BlockOffset + BorderTop + PaddingTop;

    public float BorderBoxInlineEnd => InlineOffset + BorderLeft + BorderRight + PaddingLeft + PaddingRight + ContentInlineSize;
    public float BorderBoxBlockEnd => BlockOffset + BorderTop + BorderBottom + PaddingTop + PaddingBottom + ContentBlockSize;

    public float MarginBoxInlineEnd => InlineOffset - MarginLeft + BorderLeft + BorderRight + PaddingLeft + PaddingRight + ContentInlineSize + MarginRight;
    public float MarginBoxBlockEnd => BlockOffset - MarginTop + BorderTop + BorderBottom + PaddingTop + PaddingBottom + ContentBlockSize + MarginBottom;

    public float ScrollableOverflowBlockEnd => BorderBoxBlockEnd;

    public bool IsSelfCollapsing => ContentBlockSize == 0 && BlockSize == 0;

    public override string ToString() =>
        $"BoxFragment({InlineOffset:F1},{BlockOffset:F1} {InlineSize:F1}x{BlockSize:F1})";
}

/// <summary>
/// A laid-out line of inline content, mirroring the line box concept.
/// </summary>
public class BoxLine
{
    public float InlineOffset { get; set; }
    public float BlockOffset { get; set; }
    public float InlineSize { get; set; }
    public float BlockSize { get; set; }
    public float BaselineOffset { get; set; }
    public List<BoxRun> Runs { get; } = new();

    public float InlineEnd => InlineOffset + InlineSize;
    public float BlockEnd => BlockOffset + BlockSize;

    public override string ToString() =>
        $"BoxLine({InlineOffset:F1},{BlockOffset:F1} {InlineSize:F1}x{BlockSize:F1} baseline={BaselineOffset:F1})";
}

/// <summary>A single inline run within a line box (text or atomic inline).</summary>
public class BoxRun
{
    public string? Text { get; set; }
    public Element? Element { get; set; }
    public Node? Node { get; set; }
    public float InlineOffset { get; set; }
    public float InlineSize { get; set; }
    public float BlockOffset { get; set; }
    public float BlockSize { get; set; }
    public bool IsBreakOpportunity { get; set; }
    public bool IsAtomicInline { get; set; }
    public BoxFragment? AtomicInlineBox { get; set; }
    public float BaselineOffset { get; set; }
    /// <summary>vertical-align baseline shift (positive raises the run). Applied by
    /// the converter against the line's resolved baseline so both layout paths agree.</summary>
    public float BaselineShift { get; set; }
    public bool IsLineBreak { get; set; }
    /// <summary>Resolved font of the run (per inline element, or a ::first-letter
    /// override), so painting matches what the line breaker measured.</summary>
    public float? FontSize { get; set; }
    public string? FontFamily { get; set; }
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
    public bool Italic { get; set; }
    public SKColor? Color { get; set; }
}

// LayoutResult and the EStatus enum are defined in LayoutResult.cs
// (mirrors layout_result.h).