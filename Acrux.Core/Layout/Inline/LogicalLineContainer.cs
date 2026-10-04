using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout.Inline;

/// <summary>
/// Represents a line with optional ruby annotations.
/// Used to pass CreateLine() deliverables to FragmentItemsBuilder.
/// Mirrors LogicalLineContainer in logical_line_container.h.
/// </summary>
public class LogicalLineContainer
{
    private LogicalLineItems _baseLine;
    private readonly List<AnnotationLine> _annotationLines = new();

    public LogicalLineContainer()
    {
        _baseLine = new LogicalLineItems();
    }

    public LogicalLineItems BaseLine => _baseLine;
    public IReadOnlyList<AnnotationLine> AnnotationLineList => _annotationLines;

    public void AddAnnotation(FontHeight metrics, LogicalLineItems lineItems)
    {
        _annotationLines.Add(new AnnotationLine(metrics, lineItems));
    }

    public int EstimatedFragmentItemCount()
    {
        int count = _baseLine.Count;
        foreach (var ann in _annotationLines)
            count += ann.LineItems.Count;
        return count;
    }

    public void Clear()
    {
        _baseLine.Clear();
        _annotationLines.Clear();
    }

    public void Shrink()
    {
        _baseLine.Clear();
        _annotationLines.Clear();
    }

    public void MoveInBlockDirection(float delta)
    {
        foreach (var item in _baseLine)
            item.MoveInBlockDirection(delta);
        foreach (var ann in _annotationLines)
        {
            foreach (var item in ann.LineItems)
                item.MoveInBlockDirection(delta);
        }
    }

    public readonly struct AnnotationLine
    {
        public FontHeight Metrics { get; }
        public LogicalLineItems LineItems { get; }

        public AnnotationLine(FontHeight metrics, LogicalLineItems lineItems)
        {
            Metrics = metrics;
            LineItems = lineItems;
        }
    }
}