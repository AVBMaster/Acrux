namespace UpBrowser.Core.Dom;

public static class NodeExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item)) return i;
        }
        return -1;
    }

    public static int IndexOfNode(this IReadOnlyList<Node> list, Node node)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], node)) return i;
        }
        return -1;
    }
}

public abstract class Node : EventTarget
{
    private Node? _parent;
    private readonly List<Node> _children = new();

    protected NodeInternalState InternalState { get; } = new();

    // ===== Spec Properties =====

    public abstract NodeType NodeType { get; }
    public virtual string NodeName { get; protected set; } = string.Empty;
    public virtual string? BaseUri { get; set; }
    public virtual bool IsConnected => ComputeIsConnected();
    public virtual Document? OwnerDocument
    {
        get => InternalState.OwnerDocument;
        internal set => InternalState.OwnerDocument = value;
    }

    public Node? ParentNode
    {
        get => _parent;
        internal set => _parent = value;
    }

    public Element? ParentElement => _parent as Element;

    public Node? FirstChild => _children.Count > 0 ? _children[0] : null;
    public Node? LastChild => _children.Count > 0 ? _children[_children.Count - 1] : null;
    public Node? PreviousSibling
    {
        get
        {
            if (_parent == null) return null;
            int idx = _parent._children.IndexOf(this);
            return idx > 0 ? _parent._children[idx - 1] : null;
        }
    }
    public Node? NextSibling
    {
        get
        {
            if (_parent == null) return null;
            int idx = _parent._children.IndexOf(this);
            return idx >= 0 && idx < _parent._children.Count - 1 ? _parent._children[idx + 1] : null;
        }
    }

    public IReadOnlyList<Node> ChildNodes => _children.AsReadOnly();
    public List<Node> Children => _children;

    public virtual string? NodeValue
    {
        get => null;
        set { }
    }

    public virtual string? TextContent
    {
        get => GetTextContent();
        set
        {
            _children.Clear();
            if (value != null)
                AppendChild(new TextNode(value));
        }
    }

    // ===== Legacy compatibility properties =====

    public Node? Parent
    {
        get => _parent;
        set => _parent = value;
    }

    // ===== Spec Methods =====

    public Node GetRootNode()
    {
        var current = this;
        while (current._parent != null)
            current = current._parent;
        return current;
    }

    public bool HasChildNodes() => _children.Count > 0;

    public void Normalize()
    {
        var newChildren = new List<Node>();
        TextNode? lastText = null;
        foreach (var child in _children)
        {
            if (child is TextNode tn)
            {
                if (lastText != null)
                {
                    lastText.Data += tn.Data;
                }
                else
                {
                    lastText = tn;
                    newChildren.Add(tn);
                }
            }
            else
            {
                if (child is Element el)
                    el.Normalize();
                lastText = null;
                newChildren.Add(child);
            }
        }
        _children.Clear();
        _children.AddRange(newChildren);
        RebuildParentLinks();
    }

    public Node CloneNode(bool deep = false)
    {
        var clone = CloneNodeInternal(deep);
        return clone;
    }

    public bool IsEqualNode(Node? other)
    {
        if (other == null || other.NodeType != NodeType) return false;
        if (NodeName != other.NodeName || NodeValue != other.NodeValue) return false;
        if (_children.Count != other._children.Count) return false;
        for (int i = 0; i < _children.Count; i++)
        {
            if (!_children[i].IsEqualNode(other._children[i]))
                return false;
        }
        return true;
    }

    public bool IsSameNode(Node? other) => ReferenceEquals(this, other);

    public DocumentPosition CompareDocumentPosition(Node other)
    {
        if (ReferenceEquals(this, other))
            return 0;

        var thisAncestors = GetAncestors();
        var otherAncestors = other.GetAncestors();

        if (thisAncestors.Contains(other))
            return DocumentPosition.Contains | DocumentPosition.Following;
        if (otherAncestors.Contains(this))
            return DocumentPosition.ContainedBy | DocumentPosition.Preceding;

        var common = thisAncestors.Intersect(otherAncestors).FirstOrDefault();
        if (common == null)
            return DocumentPosition.Disconnected | DocumentPosition.ImplementationSpecific;

        int thisIdx = common._children.IndexOf(thisAncestors[thisAncestors.IndexOf(common) - 1]);
        int otherIdx = common._children.IndexOf(otherAncestors[otherAncestors.IndexOf(common) - 1]);

        return thisIdx < otherIdx ? DocumentPosition.Following : DocumentPosition.Preceding;
    }

    public bool Contains(Node? other)
    {
        if (other == null) return false;
        if (ReferenceEquals(this, other)) return true;
        return other.GetAncestors().Contains(this);
    }

    public string? LookupPrefix(string? ns)
    {
        if (ns == null) return null;
        if (this is Element el && el.NamespaceUri == ns && el.Prefix != null)
            return el.Prefix;
        var parent = ParentElement;
        return parent?.LookupPrefix(ns);
    }

    public string? LookupNamespaceUri(string? prefix)
    {
        if (prefix == null) return null;
        if (this is Element el && el.Prefix == prefix)
            return el.NamespaceUri;
        var parent = ParentElement;
        return parent?.LookupNamespaceUri(prefix);
    }

    public bool IsDefaultNamespace(string? ns)
    {
        return LookupNamespaceUri(null) == ns;
    }

    // ===== Tree Mutation Methods =====

    public virtual Node AppendChild(Node child)
    {
        if (child == this)
            throw new DOMException("Cannot append a node to itself", "HierarchyRequestError");
        if (child.NodeType == NodeType.Document && this.NodeType != NodeType.Document)
            throw new DOMException("Cannot append a Document node", "HierarchyRequestError");

        child.Remove();
        child._parent = this;
        _children.Add(child);
        child.OwnerDocument = OwnerDocument;
        OnChildInserted(child);
        DomMutationTracker.Notify();
        return child;
    }

    /// <summary>
    /// Adds a child reference for transient layout-only trees (anonymous blocks
    /// created during block sizing) without mutating the DOM: the child keeps
    /// its real parent and stays in the original parent's child list.
    /// </summary>
    internal void AddChildReferenceForLayout(Node child)
    {
        _children.Add(child);
    }

    public virtual Node InsertBefore(Node newChild, Node? refChild)
    {
        if (newChild == this)
            throw new DOMException("Cannot insert a node before itself", "HierarchyRequestError");

        if (refChild == null)
            return AppendChild(newChild);

        int index = _children.IndexOf(refChild);
        if (index < 0)
            throw new DOMException("Reference node is not a child", "NotFoundError");

        newChild.Remove();
        newChild._parent = this;
        _children.Insert(index, newChild);
        newChild.OwnerDocument = OwnerDocument;
        OnChildInserted(newChild);
        DomMutationTracker.Notify();
        return newChild;
    }

    public virtual Node RemoveChild(Node child)
    {
        if (!_children.Remove(child))
            throw new DOMException("Node is not a child", "NotFoundError");
        child._parent = null;
        OnChildRemoved(child);
        DomMutationTracker.Notify();
        return child;
    }

    public virtual Node ReplaceChild(Node newChild, Node oldChild)
    {
        int index = _children.IndexOf(oldChild);
        if (index < 0)
            throw new DOMException("Node is not a child", "NotFoundError");

        newChild.Remove();
        newChild._parent = this;
        _children[index] = newChild;
        oldChild._parent = null;
        newChild.OwnerDocument = OwnerDocument;
        OnChildInserted(newChild);
        OnChildRemoved(oldChild);
        DomMutationTracker.Notify();
        return oldChild;
    }

    public void Remove()
    {
        _parent?.RemoveChild(this);
    }

    // ===== Utility Methods =====

    private string GetTextContent()
    {
        var sb = new System.Text.StringBuilder();
        CollectTextContent(this, sb);
        return sb.ToString();
    }

    private static void CollectTextContent(Node node, System.Text.StringBuilder sb)
    {
        if (node is TextNode tn)
            sb.Append(tn.Data);
        else if (node is Element)
        {
            foreach (var child in node._children)
                CollectTextContent(child, sb);
        }
    }

    private List<Node> GetAncestors()
    {
        var ancestors = new List<Node>();
        var current = _parent;
        while (current != null)
        {
            ancestors.Add(current);
            current = current._parent;
        }
        return ancestors;
    }

    private bool ComputeIsConnected()
    {
        var root = GetRootNode();
        return root.NodeType == NodeType.Document;
    }

    private void RebuildParentLinks()
    {
        foreach (var child in _children)
            child._parent = this;
    }

    protected virtual Node CloneNodeInternal(bool deep)
    {
        throw new NotImplementedException();
    }

    protected virtual void OnChildInserted(Node child) { }
    protected virtual void OnChildRemoved(Node child) { }
    protected virtual void OnAttributeChanged(string name, string? oldValue, string? newValue) { }

    // ===== Legacy methods for backward compat =====

    public T? FirstChildOfType<T>() where T : Node => _children.FirstOrDefault() as T;
    public IEnumerable<T> ChildrenOfType<T>() where T : Node => _children.OfType<T>();

    public bool IsElement => this is Element;
    public bool IsTextNode => this is TextNode;
}
