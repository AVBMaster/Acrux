namespace Acrux.Core.Dom;

public class NodeIterator
{
    private readonly Node _root;
    private readonly int _whatToShow;
    private readonly NodeFilter? _filter;
    private readonly bool _entityReferenceExpansion;
    private Node? _referenceNode;
    private bool _pointerBeforeReferenceNode;

    internal NodeIterator(Node root, int whatToShow, NodeFilter? filter, bool entityReferenceExpansion)
    {
        _root = root;
        _whatToShow = whatToShow;
        _filter = filter;
        _entityReferenceExpansion = entityReferenceExpansion;
        _referenceNode = root;
        _pointerBeforeReferenceNode = true;
    }

    public Node Root => _root;
    public int WhatToShow => _whatToShow;
    public NodeFilter? Filter => _filter;

    public Node? NextNode()
    {
        var node = _referenceNode;
        var before = _pointerBeforeReferenceNode;

        do
        {
            if (before)
            {
                before = false;
                var children = node?.ChildNodes;
                if (children != null && children.Count > 0)
                {
                    node = children[0];
                    if (AcceptNode(node))
                    {
                        _referenceNode = node;
                        _pointerBeforeReferenceNode = false;
                        return node;
                    }
                    continue;
                }
            }

            var sibling = node?.NextSibling;
            if (sibling != null)
            {
                node = sibling;
                if (AcceptNode(node))
                {
                    _referenceNode = node;
                    _pointerBeforeReferenceNode = false;
                    return node;
                }
                continue;
            }

            var parent = node?.ParentNode;
            while (parent != null && parent != _root)
            {
                var parentSibling = parent.NextSibling;
                if (parentSibling != null)
                {
                    node = parentSibling;
                    if (AcceptNode(node))
                    {
                        _referenceNode = node;
                        _pointerBeforeReferenceNode = false;
                        return node;
                    }
                    break;
                }
                parent = parent.ParentNode;
            }

            if (parent == null || parent == _root)
            {
                _referenceNode = null;
                _pointerBeforeReferenceNode = false;
                return null;
            }
        } while (node != null);

        return null;
    }

    public Node? PreviousNode()
    {
        var node = _referenceNode;
        var before = _pointerBeforeReferenceNode;

        do
        {
            if (!before)
            {
                var children = node?.ChildNodes;
                if (children != null && children.Count > 0)
                {
                    node = children[^1];
                    if (AcceptNode(node))
                    {
                        _referenceNode = node;
                        _pointerBeforeReferenceNode = true;
                        return node;
                    }
                    continue;
                }
            }

            before = false;
            var sibling = node?.PreviousSibling;
            if (sibling != null)
            {
                node = sibling;
                if (AcceptNode(node))
                {
                    _referenceNode = node;
                    _pointerBeforeReferenceNode = true;
                    return node;
                }
                continue;
            }

            var parent = node?.ParentNode;
            if (parent != null && parent != _root)
            {
                node = parent;
                if (AcceptNode(node))
                {
                    _referenceNode = node;
                    _pointerBeforeReferenceNode = true;
                    return node;
                }
                continue;
            }

            _referenceNode = null;
            _pointerBeforeReferenceNode = true;
            return null;
        } while (node != null);

        return null;
    }

    public void Detach()
    {
    }

    private bool AcceptNode(Node? node)
    {
        if (node == null) return false;
        if (!IsNodeShown(node)) return false;
        if (_filter != null)
        {
            var result = _filter.AcceptNode(node);
            if (result == FilterResult.Reject) return false;
        }
        return true;
    }

    private bool IsNodeShown(Node node)
    {
        return (_whatToShow & (1 << ((int)node.NodeType - 1))) != 0;
    }
}
