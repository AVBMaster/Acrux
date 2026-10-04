using Acrux.Core.Dom;

namespace Acrux.Core.Layout;

/// <summary>
/// Iterates a table's children in layout order: top captions → sections (thead,
/// tbody, tfoot) → bottom captions. Mirrors TableChildIterator in
/// table_child_iterator.h (simplified, no fragmentation).
/// </summary>
public class TableChildIterator
{
    private readonly TableGroupedChildren _grouped;
    private int _captionIndex;
    private TableGroupedChildrenIterator? _sectionIterator;
    private bool _inBottomCaptions;
    private bool _done;

    public TableChildIterator(Element table)
    {
        _grouped = new TableGroupedChildren(table);
        // Advance past any non-top captions to find the first top caption.
        AdvanceToNextTopCaption();
    }

    public Entry NextChild()
    {
        if (_done) return new Entry(null);

        // Phase 1: top captions.
        if (_sectionIterator == null && !_inBottomCaptions)
        {
            if (_captionIndex < _grouped.Captions.Count)
            {
                var cap = _grouped.Captions[_captionIndex];
                _captionIndex++;
                AdvanceToNextTopCaption();
                // No more top captions → move to sections.
                if (_captionIndex >= _grouped.Captions.Count)
                    _sectionIterator = new TableGroupedChildrenIterator(_grouped);
                return new Entry(cap);
            }
            // No more captions at all; move to sections.
            _sectionIterator = new TableGroupedChildrenIterator(_grouped);
        }

        // Phase 2: sections.
        if (_sectionIterator != null)
        {
            var section = _sectionIterator.Current();
            if (section != null)
            {
                _sectionIterator.MoveNext();
                return new Entry(section);
            }
            // Sections exhausted; move to bottom captions.
            _sectionIterator = null;
            _inBottomCaptions = true;
            _captionIndex = 0;
            AdvanceToNextBottomCaption();
        }

        // Phase 3: bottom captions.
        if (_inBottomCaptions)
        {
            if (_captionIndex < _grouped.Captions.Count)
            {
                var cap = _grouped.Captions[_captionIndex];
                _captionIndex++;
                AdvanceToNextBottomCaption();
                return new Entry(cap);
            }
        }

        _done = true;
        return new Entry(null);
    }

    private void AdvanceToNextTopCaption()
    {
        while (_captionIndex < _grouped.Captions.Count)
        {
            if (_grouped.Captions[_captionIndex].ComputedStyle?.CaptionSide == "top")
                return;
            _captionIndex++;
        }
    }

    private void AdvanceToNextBottomCaption()
    {
        while (_captionIndex < _grouped.Captions.Count)
        {
            if (_grouped.Captions[_captionIndex].ComputedStyle?.CaptionSide == "bottom")
                return;
            _captionIndex++;
        }
    }

    public readonly struct Entry
    {
        public Element? Element { get; }

        public Entry(Element? element)
        {
            Element = element;
        }

        public static implicit operator bool(Entry e) => e.Element != null;
    }
}