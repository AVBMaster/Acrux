namespace Acrux.Core.Dom;

public enum SelectionDirection
{
    Forward,
    Backward,
    Directionless
}

public class Selection
{
    private readonly Document _document;
    private Range? _range;
    private SelectionDirection _direction;

    public Selection(Document document)
    {
        _document = document;
    }

    public Node? AnchorNode => _range?.StartContainer;
    public int AnchorOffset => _range?.StartOffset ?? 0;
    public Node? FocusNode => _range?.EndContainer;
    public int FocusOffset => _range?.EndOffset ?? 0;
    public bool IsCollapsed => _range == null || _range.Collapsed;
    public int RangeCount => _range != null ? 1 : 0;
    public SelectionDirection Direction => _direction;

    public Range? GetRangeAt(int index)
    {
        if (index != 0 || _range == null) return null;
        return _range;
    }

    public void AddRange(Range range)
    {
        if (_range == null)
        {
            _range = range;
        }
    }

    public void RemoveRange(Range range)
    {
        if (_range == range)
        {
            _range = null;
        }
    }

    public void RemoveAllRanges()
    {
        _range = null;
    }

    public void Empty() => RemoveAllRanges();

    public void Collapse(Node node, int offset = 0)
    {
        if (_range != null)
        {
            _range.SetStart(node, offset);
            _range.Collapse(true);
        }
        else
        {
            _range = new Range();
            _range.SetStart(node, offset);
            _range.Collapse(true);
        }
    }

    public void SetPosition(Node node, int offset = 0) => Collapse(node, offset);

    public void CollapseToStart()
    {
        if (_range != null) _range.Collapse(true);
    }

    public void CollapseToEnd()
    {
        if (_range != null) _range.Collapse(false);
    }

    public void Extend(Node node, int offset = 0)
    {
        if (_range == null)
        {
            Collapse(node, offset);
            return;
        }

        var newRange = _document.CreateRange();
        newRange.SetStart(AnchorNode!, AnchorOffset);
        newRange.SetEnd(node, offset);
        _range = newRange;
    }

    public void SetBaseAndExtent(Node anchorNode, int anchorOffset, Node focusNode, int focusOffset)
    {
        var newRange = _document.CreateRange();
        newRange.SetStart(anchorNode, anchorOffset);
        newRange.SetEnd(focusNode, focusOffset);
        _range = newRange;
    }

    public void SelectAllChildren(Node node)
    {
        var newRange = _document.CreateRange();
        newRange.SetStartBefore(node);
        newRange.SetEndAfter(node);
        _range = newRange;
    }

    public void Modify(string alter, string direction, string granularity)
    {
    }

    public void DeleteFromDocument()
    {
        _range?.DeleteContents();
    }

    public bool ContainsNode(Node node, bool allowPartialContainment = false)
    {
        if (_range == null) return false;
        if (allowPartialContainment)
        {
            return _range.IsPointInRange(node, 0) || _range.IsPointInRange(node, node.ChildNodes.Count);
        }
        return _range.IsPointInRange(node, 0) && _range.IsPointInRange(node, node.ChildNodes.Count);
    }

    public override string ToString()
    {
        return _range?.ToString() ?? "";
    }
}
