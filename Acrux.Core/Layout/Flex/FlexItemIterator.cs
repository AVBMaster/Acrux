namespace Acrux.Core.Layout;

/// <summary>
/// A utility class for flex layout which iterates through flex items in layout
/// order across flex lines. Mirrors FlexItemIterator in flex_item_iterator.h
/// (simplified: no fragmentation / break-token resume).
/// </summary>
public class FlexItemIterator
{
    private readonly List<FlexLine> _flexLines;
    private readonly bool _isColumn;
    private int _flexLineIndex;
    private int _flexItemIndex;

    public FlexItemIterator(List<FlexLine> flexLines, bool isColumn)
    {
        _flexLines = flexLines;
        _isColumn = isColumn;
        _flexLineIndex = 0;
        _flexItemIndex = 0;
    }

    public bool HasMoreItems => _flexLineIndex < _flexLines.Count
        && (_flexItemIndex < _flexLines[_flexLineIndex].Items.Count || _flexLineIndex < _flexLines.Count - 1);

    public Entry NextItem()
    {
        if (_flexLineIndex >= _flexLines.Count)
            return new Entry(null, 0, _flexLineIndex, null);

        var line = _flexLines[_flexLineIndex];
        FlexItem? item = null;
        int itemIndex = 0;

        if (_flexItemIndex < line.Items.Count)
        {
            item = line.Items[_flexItemIndex];
            itemIndex = _flexItemIndex;
            _flexItemIndex++;
        }
        else
        {
            // Move to the next line.
            _flexLineIndex++;
            _flexItemIndex = 0;
        }

        return new Entry(item, itemIndex, _flexLineIndex, null);
    }

    public void NextLine()
    {
        if (_flexItemIndex == 0)
            return;
        _flexLineIndex++;
        _flexItemIndex = 0;
    }

    public readonly struct Entry
    {
        public FlexItem? Item { get; }
        public int ItemIndex { get; }
        public int LineIndex { get; }
        public BlockBreakToken? Token { get; }

        public Entry(FlexItem? item, int itemIndex, int lineIndex, BlockBreakToken? token)
        {
            Item = item;
            ItemIndex = itemIndex;
            LineIndex = lineIndex;
            Token = token;
        }
    }
}