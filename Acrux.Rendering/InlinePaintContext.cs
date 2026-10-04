using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.Core.Layout.Geometry;
using Acrux.Core.Layout.Inline;

namespace Acrux.Rendering;

/// <summary>
/// Represents a [decorating box]:
/// https://drafts.csswg.org/css-text-decor-3/#decorating-box
/// Mirrors DecoratingBox.
/// </summary>
public readonly struct DecoratingBox
{
    public PhysicalOffset ContentOffsetInContainer { get; }
    public ComputedStyle Style { get; }
    public System.Collections.Generic.List<AppliedTextDecoration> AppliedTextDecorations { get; }

    public DecoratingBox(PhysicalOffset contentOffsetInContainer, ComputedStyle style,
        System.Collections.Generic.List<AppliedTextDecoration>? decorations)
    {
        ContentOffsetInContainer = contentOffsetInContainer;
        Style = style;
        AppliedTextDecorations = decorations ?? style.AppliedTextDecorations();
    }

    public DecoratingBox(FragmentItem item, ComputedStyle style,
        System.Collections.Generic.List<AppliedTextDecoration>? decorations)
        : this(item.Offset, style, decorations)
    {
    }

    public override string ToString() => $"DecoratingBox @({ContentOffsetInContainer.Left:F1},{ContentOffsetInContainer.Top:F1}) {AppliedTextDecorations.Count} decoration(s)";
}

/// <summary>
/// Carries contextual information shared across multiple inline fragments within
/// an inline formatting context. Mirrors InlinePaintContext.
/// </summary>
public sealed class InlinePaintContext
{
    private readonly System.Collections.Generic.List<DecoratingBox> _decoratingBoxes = new();
    private System.Collections.Generic.List<AppliedTextDecoration>? _lastDecorations;
    private System.Collections.Generic.List<AppliedTextDecoration>? _lineDecorations;
    private PhysicalOffset _paintOffset;

    public System.Collections.Generic.IReadOnlyList<DecoratingBox> DecoratingBoxes => _decoratingBoxes;

    public PhysicalOffset PaintOffset => _paintOffset;
    public void SetPaintOffset(PhysicalOffset paintOffset) => _paintOffset = paintOffset;

    public void PushDecoratingBox(DecoratingBox box) => _decoratingBoxes.Add(box);
    public void PushDecoratingBox(PhysicalOffset contentOffset, ComputedStyle style,
        System.Collections.Generic.List<AppliedTextDecoration>? decorations) =>
        _decoratingBoxes.Add(new DecoratingBox(contentOffset, style, decorations));

    public void PushDecoratingBoxes(System.Collections.Generic.IReadOnlyList<DecoratingBox> boxes)
    {
        _decoratingBoxes.AddRange(boxes);
    }

    public void PopDecoratingBox(int size)
    {
        int count = Math.Min(size, _decoratingBoxes.Count);
        _decoratingBoxes.RemoveRange(_decoratingBoxes.Count - count, count);
    }

    public void ClearDecoratingBoxes(System.Collections.Generic.List<DecoratingBox>? savedDecoratingBoxes = null)
    {
        if (savedDecoratingBoxes != null)
        {
            savedDecoratingBoxes.AddRange(_decoratingBoxes);
            _decoratingBoxes.Clear();
        }
        else
        {
            _decoratingBoxes.Clear();
        }
    }

    /// <summary>
    /// Synchronize _decoratingBoxes with the AppliedTextDecorations of the given
    /// fragment item, walking the layout-object ancestors to find decorating
    /// boxes (elements with strictly more decorations than their parent).
    /// Mirrors SyncDecoratingBox / DecorationBoxSynchronizer in inline_paint_context.cc,
    /// reduced to the propagation semantics this engine models (each style keeps a
    /// single list of its own decorations; identity means "same shared list").
    /// </summary>
    public int SyncDecoratingBox(FragmentItem item, System.Collections.Generic.List<DecoratingBox>? savedDecoratingBoxes = null)
    {
        var layoutObject = item.LayoutObject;
        var style = layoutObject?.Style;
        if (style == null)
            return 0;
        return SyncDecoratingBoxFromStyle(item, layoutObject, style, savedDecoratingBoxes, stopAt: _lastDecorations);
    }

    private int SyncDecoratingBoxFromStyle(
        FragmentItem? item, LayoutObject? layoutObject, ComputedStyle style,
        System.Collections.Generic.List<DecoratingBox>? savedDecoratingBoxes,
        System.Collections.Generic.List<AppliedTextDecoration>? stopAt)
    {
        if (layoutObject == null)
            return 0;

        var decorations = style.AppliedTextDecorations();
        if (stopAt != null && ReferenceEquals(decorations, stopAt))
            return 0;

        var parent = layoutObject.Parent;
        var parentStyle = parent?.Style;
        if (parentStyle == null || parent == null)
        {
            // No parent: this style is the only source. If it has decorations,
            // treat it as a decorating box.
            if (decorations.Count > 0)
            {
                _decoratingBoxes.Add(new DecoratingBox(item?.Offset ?? _paintOffset, style, decorations));
                return 1;
            }
            return 0;
        }

        var parentDecorations = parentStyle.AppliedTextDecorations();
        if (!ReferenceEquals(decorations, parentDecorations))
        {
            // It's a decorating box if it has more decorations than its parent.
            if (decorations.Count > parentDecorations.Count)
            {
                int numPushes = 0;
                if (!ReferenceEquals(parentDecorations, stopAt))
                {
                    numPushes = SyncDecoratingBoxFromStyle(item: null, parent, parentStyle,
                        savedDecoratingBoxes, stopAt);
                }
                numPushes += PushDecoratingBoxesUntilParent(item, layoutObject, style, decorations, parentDecorations);
                return numPushes;
            }

            if (decorations.Count == parentDecorations.Count
                && (style.TextDecorationLine == TextDecorationLineType.None || layoutObject.IsText))
            {
                if (ReferenceEquals(parentDecorations, stopAt))
                    return 0;
                return SyncDecoratingBoxFromStyle(item: null, parent, parentStyle, savedDecoratingBoxes, stopAt);
            }

            // The node stopped propagation: reset and keep only its own decorations.
            if (decorations.Count <= 1)
            {
                ClearDecoratingBoxes(savedDecoratingBoxes);
                if (decorations.Count == 0)
                    return 0;
                _decoratingBoxes.Add(new DecoratingBox(item?.Offset ?? _paintOffset, style, decorations));
                return 1;
            }
        }

        if (!parent.IsLayoutInline)
            return 0;

        return SyncDecoratingBoxFromStyle(item: null, parent, parentStyle, savedDecoratingBoxes, stopAt);
    }

    private int PushDecoratingBoxesUntilParent(
        FragmentItem? item, LayoutObject layoutObject, ComputedStyle style,
        System.Collections.Generic.List<AppliedTextDecoration> decorations,
        System.Collections.Generic.List<AppliedTextDecoration> parentDecorations)
    {
        var baseDecorations = style.BaseAppliedTextDecorations();
        if (ReferenceEquals(baseDecorations, parentDecorations))
        {
            _decoratingBoxes.Add(new DecoratingBox(item?.Offset ?? _paintOffset, style, decorations));
            return 1;
        }
        // This engine does not model ::first-line derived chains: push the single
        // decorating box carrying this style's decorations.
        _decoratingBoxes.Add(new DecoratingBox(item?.Offset ?? _paintOffset, style, decorations));
        return 1;
    }

    public void PushDecoratingBoxAncestors(FragmentItem? inlineBox)
    {
        if (inlineBox == null || inlineBox.LayoutObject == null)
            return;
        // Simplified: push decorating boxes for each ancestor, innermost first.
        var chain = new System.Collections.Generic.List<LayoutObject>();
        var lo = inlineBox.LayoutObject;
        while (lo != null)
        {
            chain.Add(lo);
            lo = lo.Parent;
        }
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            SyncDecoratingBoxFromStyle(item: null, chain[i], chain[i].Style!,
                savedDecoratingBoxes: null, stopAt: _lastDecorations);
        }
    }

    public void ClearLineBox()
    {
        _lastDecorations = null;
        _lineDecorations = null;
        _decoratingBoxes.Clear();
    }

    /// <summary>Pushes decorating boxes for the given line's style if any.</summary>
    public void SetLineDecorations(System.Collections.Generic.List<AppliedTextDecoration>? decorations)
    {
        _lineDecorations = _lastDecorations = decorations;
    }

    /// <summary>
    /// RAII: pushes a decorating box for an inline item while in scope.
    /// Mirrors ScopedInlineItem.
    /// </summary>
    public sealed class ScopedInlineItem : IDisposable
    {
        private readonly InlinePaintContext _context;
        private readonly System.Collections.Generic.List<AppliedTextDecoration>? _lastDecorations;
        private readonly System.Collections.Generic.List<DecoratingBox> _saved;
        private readonly int _pushCount;

        public ScopedInlineItem(FragmentItem item, InlinePaintContext context)
        {
            _context = context;
            _lastDecorations = context._lastDecorations;
            _saved = new System.Collections.Generic.List<DecoratingBox>();
            _pushCount = context.SyncDecoratingBox(item, _saved);
        }

        public void Dispose()
        {
            _context._lastDecorations = _lastDecorations;
            if (_saved.Count > 0)
            {
                // Restore the saved list: the push performed a clear-and-set.
                _context._decoratingBoxes.Clear();
                _context._decoratingBoxes.AddRange(_saved);
                return;
            }
            if (_pushCount > 0)
                _context.PopDecoratingBox(_pushCount);
        }
    }

    /// <summary>RAII: cleans the line box state while in scope. Mirrors ScopedInlineBoxAncestors / ScopedLineBox.</summary>
    public sealed class ScopedLineBox : IDisposable
    {
        private readonly InlinePaintContext _context;

        public ScopedLineBox(InlinePaintContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.ClearLineBox();
    }

    /// <summary>RAII: sets the paint offset while in scope. Mirrors ScopedPaintOffset.</summary>
    public sealed class ScopedPaintOffset : IDisposable
    {
        private readonly InlinePaintContext _context;
        private readonly PhysicalOffset _previous;

        public ScopedPaintOffset(PhysicalOffset paintOffset, InlinePaintContext context)
        {
            _context = context;
            _previous = context._paintOffset;
            context._paintOffset = paintOffset;
        }

        public void Dispose() => _context._paintOffset = _previous;
    }
}