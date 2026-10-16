using DomNode = Acrux.Core.Dom.Node;

namespace Acrux.Core.JavaScript;

/// <summary>DOM 4 §4.7: a TreeWalker and a NodeIterator read the same tree in the same order and
/// differ in three ways a page can measure — an iterator offers its root as the first candidate
/// while a walker starts one step after it, a walker prunes the subtree of a node the filter
/// rejects while an iterator only steps over that one node, and a walker keeps a current node the
/// page moves with parentNode()/firstChild()/lastChild()/nextSibling()/previousSibling() while an
/// iterator keeps a pointer beside its reference node. Both are live: they read the tree as it
/// stands on every step, so a node the page inserted between two calls is walked through.</summary>
public abstract class NodeTraverserHost : IDomPrototypeHost
{
    internal const int Accept = 1;
    internal const int Reject = 2;
    internal const int Skip = 3;

    private readonly DomNode _root;
    private readonly uint _whatToShow;
    private readonly int _filterId;
    private readonly JavaScriptEngine? _engine;

    protected NodeTraverserHost(DomNode root, uint whatToShow, int filterId, JavaScriptEngine? engine)
    {
        _root = root;
        _whatToShow = whatToShow;
        _filterId = filterId;
        _engine = engine;
    }

    protected DomNode Root => _root;
    protected JavaScriptEngine? TraverserEngine => _engine;
    protected bool PrunesSubtreeOnReject => this is TreeWalkerHost;

    /// <summary>Which interface the wrapper is stamped with, and therefore what the page's
    /// 'instanceof' walks.</summary>
    public string[] __domTypeChain => this is TreeWalkerHost
        ? new[] { "TreeWalker" }
        : new[] { "NodeIterator" };

    /// <summary>DOM 4 §4.7.2: the root is a node or the call is a TypeError, and that is checked
    /// before anything else — both factories start here. The message names the operation the page
    /// called, which is what the reference puts in front of it.</summary>
    internal static Acrux.Core.Dom.Node RequireRoot(object? root, string api)
    {
        var node = ElementHost.UnwrapNode(root);
        if (node == null)
            throw new Acrux.Core.Dom.DOMException(
                $"Failed to execute '{api}' on 'Document': parameter 1 is not of type 'Node'.", "TypeError");
        return node;
    }

    /// <summary>whatToShow is an unsigned long; -1 keeps the meaning it had before the constant was
    /// corrected (every bit set) instead of silently showing nothing.</summary>
    internal static uint ToMask(double whatToShow)
        => (uint)WebIdL.ToUnsignedLong(whatToShow);

    public object? root => JsIntegrationService.WrapAny(_root);

    /// <summary>'whatToShow' is an unsigned long: SHOW_ALL is 4294967295, not -1 (measured).</summary>
    public uint whatToShow => _whatToShow;

    /// <summary>The id the page's filter was stored under. 'filter' itself is answered on the
    /// interface prototype — a JavaScript object handed to a host method is converted on the way in,
    /// so only script can give the very object back (DOM 4 §4.7.2 'the filter is the object the
    /// callback was made from', measured as 'walker.filter === f'). A negative id is a filter that
    /// was given but has no callable acceptNode(), which the reference refuses only once a step
    /// actually asks it.</summary>
    public int __filterId => _filterId;

    // The step the page is in, so that the refusal of a filter that cannot answer names the
    // operation the page called rather than whichever host method happens to be walking.
    private string _step = "nextNode";

    private string InterfaceName => this is TreeWalkerHost ? "TreeWalker" : "NodeIterator";

    protected void BeginStep(string step) => _step = step;

    protected static object? Wrap(DomNode? node) => node == null ? null : JsIntegrationService.WrapAny(node);

    /// <summary>DOM 4 §4.7.1 "filtering a node": the mask bit of the node's type is tested first and
    /// a node the mask does not want is skipped — never rejected, which is what lets a SHOW_TEXT
    /// walk reach the text inside an element. Only then is the page's filter asked.</summary>
    protected int Filter(DomNode node)
    {
        int bit = 1 << ((int)node.NodeType - 1);
        if ((_whatToShow & (uint)bit) == 0) return Skip;
        if (_filterId < 0)
            throw new Acrux.Core.Dom.DOMException(
                $"Failed to execute '{_step}' on '{InterfaceName}': Failed to execute 'acceptNode'"
                + " on 'NodeFilter': The provided callback is not callable.", "TypeError");
        if (_filterId == 0) return Accept;

        var wrapped = JsIntegrationService.WrapAny(node);
        if (wrapped == null) return Accept;
        var engine = JavaScriptEngine.Current ?? _engine;
        var facade = engine?.IntegrationService?.Facade;
        if (facade == null) return Accept;
        // The page's filter is asked through the path that lets its own error escape: the reference
        // does not swallow a throwing filter, it takes the Error out of the step that asked (measured:
        // nextNode() re-raised the filter's TypeError).
        object? raw = facade.CallFunctionPropagating("__g_invoke", _filterId, wrapped);
        if (raw == null) return Accept;
        double number;
        try
        {
            number = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return Reject;
        }
        // The answer is a long before it is compared — the script side truncates it — and only the
        // two remaining spellings mean anything else: 1 accepts, 3 skips, and everything else is the
        // refusal that prunes (measured: 0, 2, 99, NaN and an object all took the subtree out).
        return number switch
        {
            Accept => Accept,
            Skip => Skip,
            _ => Reject
        };
    }

    /// <summary>Inclusive pre-order traversal: the first node the traverser accepts in the subtree,
    /// descending only when the node itself was skipped (or rejected by an iterator, which does not
    /// prune).</summary>
    protected DomNode? FirstAcceptedIn(DomNode node)
    {
        var result = Filter(node);
        if (result == Accept) return node;
        if (result == Reject && PrunesSubtreeOnReject) return null;
        foreach (var child in node.Children)
        {
            var found = FirstAcceptedIn(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>The last node the traverser accepts in the subtree, for the reads that step
    /// backwards through siblings. A node the filter accepts is answered itself and its children
    /// are looked at only when the node is skipped — measured on the reference, whose
    /// previousSibling of a paragraph answered the section before it rather than that section's
    /// last text node, which is what the specification's post-order reading would give.</summary>
    protected DomNode? LastAcceptedInSubtree(DomNode node)
    {
        var result = Filter(node);
        if (result == Accept) return node;
        if (result == Reject && PrunesSubtreeOnReject) return null;
        var children = node.Children;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var found = LastAcceptedInSubtree(children[i]);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Inclusive post-order traversal, for the node that comes just before a subtree in
    /// document order: the deepest last accepted node of that subtree, and only the node itself
    /// when nothing below it is accepted.</summary>
    protected DomNode? LastAcceptedBeforeSubtree(DomNode node)
    {
        var result = Filter(node);
        if (result == Reject && PrunesSubtreeOnReject) return null;
        var children = node.Children;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var found = LastAcceptedBeforeSubtree(children[i]);
            if (found != null) return found;
        }
        return result == Accept ? node : null;
    }

    /// <summary>The node after 'node' in the traversal: its subtree first (unless a walker pruned
    /// it), then the next sibling of 'node' or of the nearest ancestor that still has one, stopping
    /// at the root.</summary>
    protected DomNode? NextAfter(DomNode node)
    {
        var result = Filter(node);
        if (!(result == Reject && PrunesSubtreeOnReject))
        {
            foreach (var child in node.Children)
            {
                var found = FirstAcceptedIn(child);
                if (found != null) return found;
            }
        }
        var cursor = node;
        while (!ReferenceEquals(cursor, _root))
        {
            var sibling = cursor.NextSibling;
            while (sibling != null)
            {
                var found = FirstAcceptedIn(sibling);
                if (found != null) return found;
                sibling = sibling.NextSibling;
            }
            cursor = cursor.ParentNode!;
            if (cursor == null) return null;
        }
        return null;
    }

    /// <summary>The node before 'node': the deepest accepted node of a previous sibling's subtree,
    /// else the nearest accepted ancestor.</summary>
    protected DomNode? PreviousBefore(DomNode node)
    {
        var cursor = node;
        while (!ReferenceEquals(cursor, _root))
        {
            var sibling = cursor.PreviousSibling;
            while (sibling != null)
            {
                var found = LastAcceptedBeforeSubtree(sibling);
                if (found != null) return found;
                sibling = sibling.PreviousSibling;
            }
            var parent = cursor.ParentNode;
            if (parent == null) return null;
            if (Filter(parent) == Accept) return parent;
            cursor = parent;
        }
        return null;
    }
}

/// <summary>DOM 4 §4.7.7: the walker's current node is where its next read starts, and the
/// navigation methods move it as they answer.</summary>
public sealed class TreeWalkerHost : NodeTraverserHost
{
    private DomNode _current;

    public TreeWalkerHost(DomNode root, uint whatToShow, int filterId, JavaScriptEngine? engine)
        : base(root, whatToShow, filterId, engine)
    {
        _current = root;
    }

    public object? currentNode
    {
        get => Wrap(_current);
        set
        {
            var node = ElementHost.UnwrapNode(value);
            if (node == null)
                throw new Acrux.Core.Dom.DOMException("parameter 1 is not of type 'Node'.", "TypeError");
            // Measured: the reference does not refuse a node outside the walked tree — the setter
            // only moves the pointer.
            _current = node;
        }
    }

    public object? nextNode()
    {
        BeginStep("nextNode");
        var found = NextAfter(_current);
        if (found != null) _current = found;
        return Wrap(found);
    }

    public object? previousNode()
    {
        BeginStep("previousNode");
        var found = PreviousBefore(_current);
        if (found != null) _current = found;
        return Wrap(found);
    }

    public object? parentNode()
    {
        BeginStep("parentNode");
        var node = _current;
        while (node != null && !ReferenceEquals(node, Root))
        {
            node = node.ParentNode;
            if (node == null) return null;
            if (Filter(node) == Accept)
            {
                _current = node;
                return Wrap(node);
            }
        }
        return null;
    }

    public object? firstChild()
    {
        BeginStep("firstChild");
        var child = _current.FirstChild;
        while (child != null)
        {
            var found = FirstAcceptedIn(child);
            if (found != null)
            {
                _current = found;
                return Wrap(found);
            }
            child = child.NextSibling;
        }
        return null;
    }

    public object? lastChild()
    {
        BeginStep("lastChild");
        var child = _current.LastChild;
        while (child != null)
        {
            var found = LastAcceptedInSubtree(child);
            if (found != null)
            {
                _current = found;
                return Wrap(found);
            }
            child = child.PreviousSibling;
        }
        return null;
    }

    public object? nextSibling()
    {
        BeginStep("nextSibling");
        if (ReferenceEquals(_current, Root)) return null;
        var sibling = _current.NextSibling;
        while (sibling != null)
        {
            var found = FirstAcceptedIn(sibling);
            if (found != null)
            {
                _current = found;
                return Wrap(found);
            }
            sibling = sibling.NextSibling;
        }
        return null;
    }

    public object? previousSibling()
    {
        BeginStep("previousSibling");
        if (ReferenceEquals(_current, Root)) return null;
        var sibling = _current.PreviousSibling;
        while (sibling != null)
        {
            var found = LastAcceptedInSubtree(sibling);
            if (found != null)
            {
                _current = found;
                return Wrap(found);
            }
            sibling = sibling.PreviousSibling;
        }
        return null;
    }
}

/// <summary>DOM 4 §4.7.3: the iterator's pointer sits before or after its reference node, and that
/// is what makes 'nextNode()' after a 'previousNode()' answer the same node again — the state, not
/// a cursor between two nodes (measured: next, next, next, prev answers the node it had just
/// handed back, and the step after it answers the same one once more).</summary>
public sealed class NodeIteratorHost : NodeTraverserHost
{
    private DomNode _reference;
    private bool _pointerBefore = true;

    public NodeIteratorHost(DomNode root, uint whatToShow, int filterId, JavaScriptEngine? engine)
        : base(root, whatToShow, filterId, engine)
    {
        _reference = root;
    }

    public object? referenceNode => Wrap(_reference);
    public bool pointerBeforeReferenceNode => _pointerBefore;

    public object? nextNode()
    {
        BeginStep("nextNode");
        var found = _pointerBefore ? FirstAcceptedIn(_reference) : NextAfter(_reference);
        if (found != null)
        {
            _reference = found;
            _pointerBefore = false;
        }
        return Wrap(found);
    }

    public object? previousNode()
    {
        BeginStep("previousNode");
        if (!_pointerBefore)
        {
            _pointerBefore = true;
            return Wrap(_reference);
        }
        var found = PreviousBefore(_reference);
        if (found != null)
        {
            _reference = found;
            _pointerBefore = true;
        }
        return Wrap(found);
    }

    /// <summary>DOM 4 §4.7.6: kept for the pages that still call it — detaching changed nothing
    /// once the pointer stopped being pinned.</summary>
    public void detach() { }
}
