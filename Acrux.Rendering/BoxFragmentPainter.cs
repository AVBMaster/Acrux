using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.Core.Performance;

namespace Acrux.Rendering;

/// <summary>
/// PaintPhase enum — controls the paint order of elements.
/// Ported from paint_phase.h.
/// </summary>
public enum PaintPhase
{
    // Background phase — backgrounds and borders of all blocks
    BlockBackground,
    // Paint background of the current object only
    SelfBlockBackgroundOnly,
    // Paint backgrounds of non-self-painting descendants only
    DescendantBlockBackgroundsOnly,
    // Forced colors mode backplate (readability)
    ForcedColorsModeBackplate,
    // Float phase — floating objects
    Float,
    // Foreground phase — all inlines, atomic inline elements
    Foreground,
    // Outline phase — outlines over foreground
    Outline,
    // Paint outline for the current object only
    SelfOutlineOnly,
    // Paint outlines of non-self-painting descendants only
    DescendantOutlinesOnly,
    // Overlay overflow controls (scrollbars)
    OverlayOverflowControls,
    // Selection drag image
    SelectionDragImage,
    // Text clip
    TextClip,
    // Mask
    Mask
}

/// <summary>PaintPhase helpers.</summary>
public static class PaintPhaseHelper
{
    public static bool ShouldPaintSelfBlockBackground(PaintPhase phase) =>
        phase == PaintPhase.BlockBackground || phase == PaintPhase.SelfBlockBackgroundOnly;

    public static bool ShouldPaintSelfOutline(PaintPhase phase) =>
        phase == PaintPhase.Outline || phase == PaintPhase.SelfOutlineOnly;

    public static bool ShouldPaintDescendantBlockBackgrounds(PaintPhase phase) =>
        phase == PaintPhase.BlockBackground || phase == PaintPhase.DescendantBlockBackgroundsOnly;

    public static bool ShouldPaintDescendantOutlines(PaintPhase phase) =>
        phase == PaintPhase.Outline || phase == PaintPhase.DescendantOutlinesOnly;
}

/// <summary>
/// BoxFragmentPainter — paints elements in the correct CSS paint phase order.
/// Translated from box_fragment_painter.cc.
/// Paint order: Background → ForcedColorsBackplate → Float → Foreground → Outline
/// </summary>
public class BoxFragmentPainter
{
    private readonly PaintVisitor _paintVisitor;
    private int _contentOffsetY;

    public BoxFragmentPainter(int contentOffsetY = 0,
        Dictionary<string, SKTypeface>? typefaceCache = null,
        ImageCache? imageCache = null,
        string[]? fontFamilies = null,
        string? baseUrl = null)
    {
        _contentOffsetY = contentOffsetY;
        _paintVisitor = new PaintVisitor(
            contentOffsetY, typefaceCache, imageCache, fontFamilies, baseUrl);
    }

    public DisplayList GetDisplayList() => _paintVisitor.GetDisplayList();

    public void SetFocusedElement(Element? e) => _paintVisitor.SetFocusedElement(e);
    public void SetPressedElement(Element? e) => _paintVisitor.SetPressedButton(e);
    public void SetMouseState(float x, float y, bool down, string? control = null)
        => _paintVisitor.SetMouseState(x, y, down, control);
    public void SetInputState(int cursor, int selStart, bool showCursor, bool ime, string imeText, int imeC)
        => _paintVisitor.SetInputState(cursor, selStart, showCursor, ime, imeText, imeC);
    public void SetSelectionRange(TextNode? anchor, int aOff, TextNode? focus, int fOff)
        => _paintVisitor.SetSelectionRange(anchor, aOff, focus, fOff);
    public void SetInputScroll(float offset) => _paintVisitor.SetInputScrollOffset(offset);
    public void SetTextAreaScroll(float sy, bool userScroll)
    {
        _paintVisitor.SetTextAreaScrollY(sy);
        _paintVisitor.SetTextAreaUserScroll(userScroll);
    }
    public void SetPasswordRevealed(bool r) => _paintVisitor.SetPasswordRevealed(r);
    public void SetSkipInputTextOverlay(bool skip) => _paintVisitor.SetSkipInputTextOverlay(skip);

    // ─── BoxFragmentPainter core ─────────────────────────────────────

    /// <summary>
    /// Paint the document using the CSS paint phase order.
    /// Mirrors BoxFragmentPainter::Paint() + PaintAllPhasesAtomically().
    /// VisitElement paints the full element in correct internal order
    /// (background → border → foreground → outline), so only the Foreground
    /// phase is needed.  Phase helpers are preserved for compatibility.
    /// </summary>
    public void PaintDocument(Document document)
    {
        var sw = Clock.NowNanos();
        var root = document.DocumentElement ?? document.Body;
        if (root == null) return;

        PaintElement(root, PaintPhase.Foreground);

        var displayList = _paintVisitor.GetDisplayList();
        PipelineTimings.Paint.AddSample(Clock.NowNanos() - sw);
    }

    /// <summary>
    /// Paint an element in a specific phase, recursing into children.
    /// Mirrors BoxFragmentPainter::PaintInternal() + PaintObject().
    /// </summary>
    private void PaintElement(Element element, PaintPhase phase)
    {
        var box = element.LayoutBox;
        var style = element.ComputedStyle;
        if (box == null || style == null) return;
        if (style.Display == DisplayType.None) return;

        var offsetBorderBox = new SKRect(
            box.BorderBox.Left,
            box.BorderBox.Top + _contentOffsetY,
            box.BorderBox.Right,
            box.BorderBox.Bottom + _contentOffsetY);

        // VisitElement paints the full element (background, border, content,
        // outline) in the correct internal order.  The phase system is preserved
        // for the recursion structure but only the Foreground phase issues the
        // actual paint call — this avoids 5x redundant painting per element.
        if (phase == PaintPhase.Foreground && style.Visibility != VisibilityType.Hidden)
        {
            _paintVisitor.VisitElement(element);
        }

        // Paint children (unless self-only phases)
        if (phase != PaintPhase.SelfBlockBackgroundOnly &&
            phase != PaintPhase.SelfOutlineOnly &&
            phase != PaintPhase.OverlayOverflowControls)
        {
            foreach (var child in element.Children)
            {
                if (child is Element childEl)
                {
                    PaintPhase childPhase = phase;

                    // Convert descendant phases to base phases for children
                    if (phase == PaintPhase.DescendantBlockBackgroundsOnly)
                        childPhase = PaintPhase.BlockBackground;
                    else if (phase == PaintPhase.DescendantOutlinesOnly)
                        childPhase = PaintPhase.Outline;

                    PaintElement(childEl, childPhase);
                }
            }
        }
    }

    /// <summary>
    /// Paint an element in all phases atomically — used for atomic inlines
    /// (inline-block, replaced elements). Mirrors PaintAllPhasesAtomically().
    /// </summary>
    private void PaintAllPhasesAtomically(Element element, PaintPhase currentPhase)
    {
        // If current phase is kSelectionDragImage or kTextClip, just paint normally.
        if (currentPhase == PaintPhase.SelectionDragImage || currentPhase == PaintPhase.TextClip)
        {
            PaintElement(element, currentPhase);
            return;
        }

        // If current phase is not kForeground, skip.
        if (currentPhase != PaintPhase.Foreground)
            return;

        PaintElement(element, PaintPhase.Foreground);
    }
}