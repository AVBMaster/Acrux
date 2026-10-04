namespace Acrux.Core.Dom;

public enum CompareHow
{
    StartToStart = 0,
    StartToEnd = 1,
    EndToEnd = 2,
    EndToStart = 3
}

public class Range
{
    private Node _startContainer;
    private int _startOffset;
    private Node _endContainer;
    private int _endOffset;

    public Range()
    {
        _startContainer = null!;
        _endContainer = null!;
    }

    internal Range(Node startContainer, int startOffset, Node endContainer, int endOffset)
    {
        _startContainer = startContainer;
        _startOffset = startOffset;
        _endContainer = endContainer;
        _endOffset = endOffset;
    }

    public Node StartContainer => _startContainer;
    public int StartOffset => _startOffset;
    public Node EndContainer => _endContainer;
    public int EndOffset => _endOffset;

    public bool Collapsed => _startContainer == _endContainer && _startOffset == _endOffset;

    public Node CommonAncestorContainer
    {
        get
        {
            var start = _startContainer;
            var end = _endContainer;
            var startAncestors = new List<Node>();
            while (start != null)
            {
                startAncestors.Add(start);
                start = start.ParentNode;
            }
            while (end != null)
            {
                if (startAncestors.Contains(end))
                    return end;
                end = end.ParentNode;
            }
            return null!;
        }
    }

    public void SetStart(Node node, int offset)
    {
        _startContainer = node;
        _startOffset = offset;
    }

    public void SetEnd(Node node, int offset)
    {
        _endContainer = node;
        _endOffset = offset;
    }

    public void SetStartBefore(Node node)
    {
        var parent = node.ParentNode;
        if (parent == null) throw new DOMException("WrongDocument", "WrongDocument");
        _startContainer = parent;
        _startOffset = parent.Children.IndexOf(node);
    }

    public void SetStartAfter(Node node)
    {
        var parent = node.ParentNode;
        if (parent == null) throw new DOMException("WrongDocument", "WrongDocument");
        _startContainer = parent;
        _startOffset = parent.Children.IndexOf(node) + 1;
    }

    public void SetEndBefore(Node node)
    {
        var parent = node.ParentNode;
        if (parent == null) throw new DOMException("WrongDocument", "WrongDocument");
        _endContainer = parent;
        _endOffset = parent.Children.IndexOf(node);
    }

    public void SetEndAfter(Node node)
    {
        var parent = node.ParentNode;
        if (parent == null) throw new DOMException("WrongDocument", "WrongDocument");
        _endContainer = parent;
        _endOffset = parent.Children.IndexOf(node) + 1;
    }

    public void Collapse(bool toStart)
    {
        if (toStart)
        {
            _endContainer = _startContainer;
            _endOffset = _startOffset;
        }
        else
        {
            _startContainer = _endContainer;
            _startOffset = _endOffset;
        }
    }

    public void SelectNode(Node node)
    {
        var parent = node.ParentNode;
        if (parent == null) throw new DOMException("WrongDocument", "WrongDocument");
        _startContainer = parent;
        _startOffset = parent.Children.IndexOf(node);
        _endContainer = parent;
        _endOffset = parent.Children.IndexOf(node) + 1;
    }

    public void SelectNodeContents(Node node)
    {
        _startContainer = node;
        _startOffset = 0;
        _endContainer = node;
        _endOffset = node.ChildNodes.Count;
    }

    public short CompareBoundaryPoints(CompareHow how, Range sourceRange)
    {
        var thisNode = how == CompareHow.StartToStart || how == CompareHow.StartToEnd ? _startContainer : _endContainer;
        var thisOffset = how == CompareHow.StartToStart || how == CompareHow.StartToEnd ? _startOffset : _endOffset;
        var otherNode = how == CompareHow.StartToStart || how == CompareHow.EndToStart ? sourceRange._startContainer : sourceRange._endContainer;
        var otherOffset = how == CompareHow.StartToStart || how == CompareHow.EndToStart ? sourceRange._startOffset : sourceRange._endOffset;

        var position = thisNode.CompareDocumentPosition(otherNode);
        if (thisNode == otherNode)
        {
            if (thisOffset < otherOffset) return -1;
            if (thisOffset > otherOffset) return 1;
            return 0;
        }

        if ((position & DocumentPosition.Following) != 0)
            return -1;
        return 1;
    }

    public void DeleteContents()
    {
        if (Collapsed) return;
        var nodesToRemove = GetNodesInRange();
        foreach (var node in nodesToRemove)
        {
            node.Remove();
        }
    }

    public DocumentFragment ExtractContents()
    {
        var fragment = new DocumentFragment();
        if (Collapsed) return fragment;

        var nodes = GetNodesInRange();
        foreach (var node in nodes)
        {
            fragment.AppendChild(node);
        }
        return fragment;
    }

    public DocumentFragment CloneContents()
    {
        var fragment = new DocumentFragment();
        if (Collapsed) return fragment;

        var nodes = GetNodesInRange();
        foreach (var node in nodes)
        {
            fragment.AppendChild(node.CloneNode(true));
        }
        return fragment;
    }

    public void InsertNode(Node node)
    {
        if (_startContainer.NodeType == NodeType.Text)
        {
            var textNode = (TextNode)_startContainer;
            var text = textNode.Data;
            var before = text[.._startOffset];
            var after = text[_startOffset..];
            textNode.Data = before;
            var afterText = new TextNode(after);
            textNode.ParentNode?.InsertBefore(afterText, textNode.NextSibling);
            textNode.ParentNode?.InsertBefore(node, afterText);
        }
        else
        {
            var children = _startContainer.ChildNodes;
            var refNode = _startOffset < children.Count ? children[_startOffset] : null;
            _startContainer.InsertBefore(node, refNode);
        }
    }

    public void SurroundContents(Node newParent)
    {
        var fragment = ExtractContents();
        InsertNode(newParent);
        newParent.AppendChild(fragment);
        SelectNode(newParent);
    }

    public Range CloneRange()
    {
        return new Range(_startContainer, _startOffset, _endContainer, _endOffset);
    }

    public void Detach()
    {
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        var nodes = GetNodesInRange();
        foreach (var node in nodes)
        {
            if (node is TextNode text)
            {
                if (node == _startContainer && node == _endContainer)
                    sb.Append(text.Data[_startOffset.._endOffset]);
                else if (node == _startContainer)
                    sb.Append(text.Data[_startOffset..]);
                else if (node == _endContainer)
                    sb.Append(text.Data[.._endOffset]);
                else
                    sb.Append(text.Data);
            }
        }
        return sb.ToString();
    }

    internal bool IsPointInRange(Node node, int offset)
    {
        var position = node.CompareDocumentPosition(_startContainer);
        if (position == DocumentPosition.Same)
        {
            if (offset < _startOffset) return false;
            position = node.CompareDocumentPosition(_endContainer);
            if (position == DocumentPosition.Same && offset > _endOffset) return false;
            return true;
        }
        if ((position & DocumentPosition.Following) != 0)
        {
            position = node.CompareDocumentPosition(_endContainer);
            if ((position & DocumentPosition.Preceding) != 0 ||
                (position == DocumentPosition.Same && offset <= _endOffset))
                return true;
        }
        return false;
    }

    private List<Node> GetNodesInRange()
    {
        var nodes = new List<Node>();
        if (Collapsed) return nodes;

        var start = _startContainer;
        var end = _endContainer;

        if (start == end)
        {
            if (start.NodeType == NodeType.Text)
            {
                nodes.Add(start);
            }
            return nodes;
        }

        var ancestors = new HashSet<Node>();
        var n = end;
        while (n != null)
        {
            ancestors.Add(n);
            n = n.ParentNode;
        }

        n = start;
        while (n != null && !ancestors.Contains(n))
        {
            nodes.Add(n);
            n = n.ParentNode;
        }

        var commonAncestor = n;

        CollectNodes(start, commonAncestor, nodes, true);
        CollectNodes(end, commonAncestor, nodes, false);

        return nodes;
    }

    private static void CollectNodes(Node boundary, Node commonAncestor, List<Node> nodes, bool isStart)
    {
        var node = boundary;
        while (node != commonAncestor)
        {
            var parent = node.ParentNode;
            if (parent == null) break;
            var siblings = parent.Children;
            int idx = siblings.IndexOf(node);
            int startIdx = isStart ? idx + 1 : 0;
            int endIdx = isStart ? siblings.Count : idx;

            for (int i = startIdx; i < endIdx; i++)
            {
                AddNodeTree(siblings[i], nodes);
            }

            node = parent;
        }
    }

    private static void AddNodeTree(Node node, List<Node> nodes)
    {
        nodes.Add(node);
        foreach (var child in node.ChildNodes)
        {
            AddNodeTree(child, nodes);
        }
    }
}
