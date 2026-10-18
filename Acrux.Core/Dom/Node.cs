using Acrux.Core.JavaScript;

namespace Acrux.Core.Dom;

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

    /// <summary>The empty side of a child-list note: an operation that only adds, or only takes away,
    /// still names the list it did not change.</summary>
    internal static readonly Node[] EmptyChildren = System.Array.Empty<Node>();

    /// <summary>Stamps the owning document on this node and everything below it. The
    /// insertion path copies the link from the parent, which for the root of a document is
    /// the Document itself - and a Document's own OwnerDocument is null by spec, so the
    /// link cannot come from there.</summary>
    internal void SetOwnerDocumentRecursive(Document document)
    {
        OwnerDocument = document;
        foreach (var child in _children)
            child.SetOwnerDocumentRecursive(document);
    }

    /// <summary>The document a child gains by being inserted here. A Document is its own node
    /// document, and a Document's OwnerDocument is null by spec, so copying the parent's link from
    /// a document root left the whole tree below it — the doctype, the root element, a processing
    /// instruction — without one, and 'doc.doctype.ownerDocument === doc' read false.</summary>
    internal Document? InsertedChildOwner => this as Document ?? OwnerDocument;

    public virtual string? NodeValue
    {
        get => null;
        set { }
    }

    public virtual string? TextContent
    {
        get => GetTextContent();
        set => ReplaceAllWith(value == null
            ? EmptyChildren
            : new Node[] { new TextNode(value) });
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

    /// <summary>DOM §4.2.1: the root of the tree the node sits in once the shadow boundaries are crossed.
    /// A shadow root is a root of its own, and the tree it belongs to continues at its host, so the walk
    /// hops the fragment and carries on up. This is what 'connected' is read from (and what
    /// 'getRootNode({ composed: true })' answers) — without the hop a node inside the shadow tree of a
    /// connected element would read 'isConnected' false (measured true), and so would every custom
    /// element reaction that waits for the tree to be in the document.</summary>
    public Node ShadowIncludingRoot()
    {
        var current = GetRootNode();
        while (current is ShadowRoot shadow && shadow.Host != null)
            current = shadow.Host.GetRootNode();
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
        // A clone belongs to the document the original was made in (§4.6) — the doctype and the root
        // of a document clone rather than adopt, and both carry the link.
        clone.OwnerDocument = InsertedChildOwner;
        // Cloning an element makes it the way making one does: a registered name is constructed as
        // the copy comes into being, before anything is read off it (§4.6, custom-elements §1).
        if (clone is Element cloneElement) CustomElements.Constructed(cloneElement);
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
        var previousOwner = child.OwnerDocument;
        var frame = _children.Count > 0 ? _children[^1] : null;
        child._parent = this;
        _children.Add(child);
        child.OwnerDocument = InsertedChildOwner;
        OnChildInserted(child);
        CustomElements.Inserted(child, previousOwner);
        DomMutationTracker.NoteChildList(this, new[] { child }, EmptyChildren, frame, null);
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
        var previousOwner = newChild.OwnerDocument;
        var frame = index > 0 ? _children[index - 1] : null;
        newChild._parent = this;
        _children.Insert(index, newChild);
        newChild.OwnerDocument = InsertedChildOwner;
        OnChildInserted(newChild);
        CustomElements.Inserted(newChild, previousOwner);
        DomMutationTracker.NoteChildList(this, new[] { newChild }, EmptyChildren, frame, refChild);
        DomMutationTracker.Notify();
        return newChild;
    }

    public virtual Node RemoveChild(Node child)
    {
        var frame = (child.PreviousSibling, child.NextSibling);
        if (!_children.Remove(child))
            throw new DOMException("Node is not a child", "NotFoundError");
        child._parent = null;
        OnChildRemoved(child);
        CustomElements.Removed(child);
        DomMutationTracker.NoteChildList(this, EmptyChildren, new[] { child }, frame.Item1, frame.Item2);
        DomMutationTracker.Notify();
        return child;
    }

    public virtual Node ReplaceChild(Node newChild, Node oldChild)
    {
        int index = _children.IndexOf(oldChild);
        if (index < 0)
            throw new DOMException("Node is not a child", "NotFoundError");

        newChild.Remove();
        // The slot is found again after the incoming node left the tree: when it was one of this
        // parent's children and sat before the one being replaced, the removal has shifted the
        // position the first lookup answered (DOM §4.1 'replace').
        index = _children.IndexOf(oldChild);
        if (index < 0)
            throw new DOMException("Node is not a child", "NotFoundError");
        var previousOwner = newChild.OwnerDocument;
        var frame = (oldChild.PreviousSibling, oldChild.NextSibling);
        newChild._parent = this;
        _children[index] = newChild;
        oldChild._parent = null;
        newChild.OwnerDocument = InsertedChildOwner;
        OnChildInserted(newChild);
        OnChildRemoved(oldChild);
        // The node that came away is the one that was taken out, and the replacement is put in:
        // the disconnected callback of the first and the connected callback of the second are both
        // owed, in that order (DOM §4.1 replace, HTML §1).
        CustomElements.Removed(oldChild);
        CustomElements.Inserted(newChild, previousOwner);
        DomMutationTracker.NoteChildList(this, new[] { newChild }, new[] { oldChild },
            frame.Item1, frame.Item2);
        DomMutationTracker.Notify();
        return oldChild;
    }

    public void Remove()
    {
        _parent?.RemoveChild(this);
    }

    /// <summary>DOM §4.1 'replace all': the children the node held all leave and the list that comes
    /// in takes their place, and the whole exchange is <em>one</em> mutation — the record the reference
    /// answers carries both lists, not a record per child (measured: three children out and one text
    /// node in is one record of one added and three removed). A node that arrives from another parent
    /// is still removed from it first, which is its own record, as the specification has it.</summary>
    internal void ReplaceAllWith(IReadOnlyList<Node> incoming)
    {
        var removed = _children.ToArray();
        _children.Clear();
        for (var i = 0; i < removed.Length; i++)
        {
            removed[i]._parent = null;
            OnChildRemoved(removed[i]);
            CustomElements.Removed(removed[i]);
        }
        var added = new List<Node>(incoming.Count);
        foreach (var node in incoming)
        {
            node.Remove();
            var previousOwner = node.OwnerDocument;
            node._parent = this;
            _children.Add(node);
            node.OwnerDocument = InsertedChildOwner;
            OnChildInserted(node);
            CustomElements.Inserted(node, previousOwner);
            added.Add(node);
        }
        if (removed.Length != 0 || added.Count != 0)
        {
            DomMutationTracker.NoteChildList(this, added, removed, null, null);
            DomMutationTracker.Notify();
        }
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
        // Connected is not 'has a parent': the root of the chain has to be a document (DOM §2.6), and
        // the chain of a node inside a shadow tree continues at its host. A document the page built
        // with 'document.implementation' counts too — measured, an element moved into one still reads
        // true here — so the registry, not the connection, is what keeps its reactions off those trees.
        return ShadowIncludingRoot().NodeType == NodeType.Document;
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
