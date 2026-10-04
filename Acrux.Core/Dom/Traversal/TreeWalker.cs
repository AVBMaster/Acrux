namespace Acrux.Core.Dom;

public class TreeWalker
{
    private readonly Node _root;
    private readonly int _whatToShow;
    private readonly NodeFilter? _filter;
    private Node? _currentNode;

    internal TreeWalker(Node root, int whatToShow, NodeFilter? filter)
    {
        _root = root;
        _whatToShow = whatToShow;
        _filter = filter;
        _currentNode = root;
    }

    public Node Root => _root;
    public int WhatToShow => _whatToShow;
    public NodeFilter? Filter => _filter;

    public Node CurrentNode
    {
        get => _currentNode ?? _root;
        set => _currentNode = value;
    }

    public Node? ParentNode()
    {
        var node = _currentNode;
        while (node != null && node != _root)
        {
            node = node.ParentNode;
            if (node != null && AcceptNode(node))
            {
                _currentNode = node;
                return node;
            }
        }
        return null;
    }

    public Node? FirstChild()
    {
        var node = _currentNode?.ChildNodes.FirstOrDefault();
        while (node != null)
        {
            if (AcceptNode(node))
            {
                _currentNode = node;
                return node;
            }
            node = NextSibling(node);
        }
        return null;
    }

    public Node? LastChild()
    {
        var children = _currentNode?.ChildNodes;
        if (children == null || children.Count == 0) return null;
        var node = children[^1];
        while (node != null)
        {
            if (AcceptNode(node))
            {
                _currentNode = node;
                return node;
            }
            node = PreviousSibling(node);
        }
        return null;
    }

    public Node? PreviousSibling()
    {
        var node = _currentNode;
        if (node == null || node == _root) return null;
        return PreviousSibling(node);
    }

    public Node? NextSibling()
    {
        var node = _currentNode;
        if (node == null || node == _root) return null;
        return NextSibling(node);
    }

    public Node? PreviousNode()
    {
        var node = _currentNode;
        while (node != null)
        {
            var result = PreviousSibling(node);
            if (result != null)
            {
                while (true)
                {
                    var lastChild = result.ChildNodes.LastOrDefault();
                    if (lastChild == null || !AcceptNode(lastChild)) break;
                    result = lastChild;
                }
                _currentNode = result;
                return result;
            }
            node = node.ParentNode;
            if (node == null || node == _root) return null;
            if (AcceptNode(node))
            {
                _currentNode = node;
                return node;
            }
        }
        return null;
    }

    public Node? NextNode()
    {
        var node = _currentNode;
        var children = node?.ChildNodes;
        if (children != null && children.Count > 0)
        {
            var first = children[0];
            if (AcceptNode(first))
            {
                _currentNode = first;
                return first;
            }
            node = first;
        }

        while (node != null)
        {
            var sibling = NextSibling(node);
            if (sibling != null)
            {
                if (AcceptNode(sibling))
                {
                    _currentNode = sibling;
                    return sibling;
                }
                node = sibling;
                continue;
            }
            node = node.ParentNode;
            if (node == null || node == _root) return null;
        }
        return null;
    }

    private bool AcceptNode(Node node)
    {
        if (!IsNodeShown(node)) return false;
        if (_filter != null)
        {
            var result = _filter.AcceptNode(node);
            return result == FilterResult.Accept;
        }
        return true;
    }

    private bool IsNodeShown(Node node)
    {
        return (_whatToShow & (1 << ((int)node.NodeType - 1))) != 0;
    }

    private Node? PreviousSibling(Node node)
    {
        var parent = node.ParentNode;
        if (parent == null) return null;
        var siblings = parent.Children;
        int idx = siblings.IndexOf(node);
        if (idx <= 0) return null;
        return siblings[idx - 1];
    }

    private Node? NextSibling(Node node)
    {
        var parent = node.ParentNode;
        if (parent == null) return null;
        var siblings = parent.Children;
        int idx = siblings.IndexOf(node);
        if (idx < 0 || idx >= siblings.Count - 1) return null;
        return siblings[idx + 1];
    }
}
