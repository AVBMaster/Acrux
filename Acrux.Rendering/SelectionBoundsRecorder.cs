using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Rendering;

/// <summary>
/// Records painted selection (highlight) bounds for the current paint pass.
/// Mirrors SelectionBoundsRecorder, mapping the PaintController callback to a
/// simple sink: the recorded bounds are appended
/// to <see cref="SelectionBoundsSink.Rects"/> at dispose time (i.e. after the
/// enclosing painting completes), matching the reference's destructor behavior.
/// </summary>
public sealed class SelectionBoundsRecorder : IDisposable
{
    /// <summary>Sink collecting recorded selection bounds for a paint pass.</summary>
    public sealed class SelectionBoundsSink
    {
        public List<(SelectionState State, SKRect Rect)> Rects { get; } = new();
    }

    private readonly SelectionBoundsSink _sink;
    private readonly SelectionState _state;
    private readonly SKRect _selectionRect;

    public SelectionBoundsRecorder(SelectionState state, PhysicalRect selectionRect, SelectionBoundsSink sink,
        TextDirection textDirection = TextDirection.Ltr, WritingMode writingMode = WritingMode.HorizontalTb)
    {
        _state = state;
        _selectionRect = selectionRect.ToSKRect();
        _sink = sink;
    }

    /// <summary>Only Start/End/StartAndEnd/Contain states produce composited bounds.</summary>
    public static bool ShouldRecordSelection(SelectionState state) =>
        state is SelectionState.Start or SelectionState.End or SelectionState.StartAndEnd or SelectionState.Contain;

    public void Dispose()
    {
        _sink.Rects.Add((_state, _selectionRect));
    }
}