using Acrux.Core.Dom;
using CharacterData = Acrux.Core.Dom.CharacterData;
using CommentNode = Acrux.Core.Dom.CommentNode;
using DocumentFragment = Acrux.Core.Dom.DocumentFragment;
using DomDocument = Acrux.Core.Dom.Document;
using DomElement = Acrux.Core.Dom.Element;
using DOMException = Acrux.Core.Dom.DOMException;
using Node = Acrux.Core.Dom.Node;
using TextNode = Acrux.Core.Dom.TextNode;

namespace Acrux.Core.JavaScript;

/// <summary>
/// DOM §3.4–3.6: a Range is a pair of boundary points in the tree. The page moves them, asks
/// which comes first, and asks for the content between them as text, as a fragment, or as a
/// deletion. Offsets count over childNodes — every node, not only elements — which is what the
/// older C# range got wrong by reading the element list, putting a text boundary on the wrong
/// index.
/// </summary>
public class RangeHost : IDomPrototypeHost
{
    private Node _startContainer;
    private int _startOffset;
    private Node _endContainer;
    private int _endOffset;

    internal RangeHost(Node startContainer, int startOffset, Node endContainer, int endOffset)
    {
        _startContainer = startContainer;
        _startOffset = startOffset;
        _endContainer = endContainer;
        _endOffset = endOffset;
    }

    public string? __domType => "Range";
    public string[] __domTypeChain => new[] { "AbstractRange", "Range" };

    public object? startContainer => JsIntegrationService.WrapAny(_startContainer);
    public int startOffset => _startOffset;
    public object? endContainer => JsIntegrationService.WrapAny(_endContainer);
    public int endOffset => _endOffset;

    internal Node NativeStartContainer => _startContainer;
    internal int NativeStartOffset => _startOffset;
    internal Node NativeEndContainer => _endContainer;
    internal int NativeEndOffset => _endOffset;

    /// <summary>The two boundary points, for the selection that reports them as its anchor and
    /// focus.</summary>
    internal (Node node, int offset) StartPointOf => (_startContainer, _startOffset);
    internal (Node node, int offset) EndPointOf => (_endContainer, _endOffset);

    public bool collapsed => ReferenceEquals(_startContainer, _endContainer) && _startOffset == _endOffset;

    public object? commonAncestorContainer => JsIntegrationService.WrapAny(CommonAncestor(_startContainer, _endContainer));

    // ===== Moving the boundaries (DOM §3.4.9) =====

    public void setStart(object? node, double offset)
    {
        var n = RequireNode(node, "setStart");
        _startContainer = n;
        _startOffset = CheckOffset(n, offset, "setStart");
        // A start that lands after the end takes the end with it; the range stays ordered.
        if (PointComesFirst(_endContainer, _endOffset, _startContainer, _startOffset))
            _endContainer = _startContainer;
        if (ReferenceEquals(_endContainer, _startContainer) && _endOffset < _startOffset)
            _endOffset = _startOffset;
    }

    public void setEnd(object? node, double offset)
    {
        var n = RequireNode(node, "setEnd");
        _endContainer = n;
        _endOffset = CheckOffset(n, offset, "setEnd");
        if (PointComesFirst(_endContainer, _endOffset, _startContainer, _startOffset))
            _startContainer = _endContainer;
        if (ReferenceEquals(_startContainer, _endContainer) && _startOffset > _endOffset)
            _startOffset = _endOffset;
    }

    public void setStartBefore(object? node) => SetBoundary(node, isStart: true, before: true, "setStartBefore");
    public void setStartAfter(object? node) => SetBoundary(node, isStart: true, before: false, "setStartAfter");
    public void setEndBefore(object? node) => SetBoundary(node, isStart: false, before: true, "setEndBefore");
    public void setEndAfter(object? node) => SetBoundary(node, isStart: false, before: false, "setEndAfter");

    private void SetBoundary(object? node, bool isStart, bool before, string api)
    {
        var n = RequireNode(node, api);
        var parent = n.ParentNode;
        if (parent == null)
            throw NoParent(api);
        int index = parent.ChildNodes.IndexOfNode(n);
        if (isStart) setStart(parent, before ? index : index + 1);
        else setEnd(parent, before ? index : index + 1);
    }

    public void selectNode(object? node)
    {
        var n = RequireNode(node, "selectNode");
        var parent = n.ParentNode;
        if (parent == null)
            throw NoParent("selectNode");
        int index = parent.ChildNodes.IndexOfNode(n);
        _startContainer = parent; _startOffset = index;
        _endContainer = parent; _endOffset = index + 1;
    }

    /// <summary>§3.4.14: a range over character data is measured in characters, so the end of a
    /// selection inside a text node is that node's length, not its (always empty) child list
    /// (measured: selectNodeContents of a text node answers (text, 0)–(text, 2)).</summary>
    public void selectNodeContents(object? node)
    {
        var n = RequireNode(node, "selectNodeContents");
        _startContainer = n; _startOffset = 0;
        _endContainer = n; _endOffset = BoundaryLength(n);
    }

    public void collapse(object? toStart = null)
    {
        // The argument is an IDL boolean whose default is false, so both an absent argument and a
        // falsy one move the start to the end — and what decides it is JavaScript truthiness, not the
        // CLR type it arrives as (measured: collapse(), collapse(undefined), collapse(0) and
        // collapse(false) all collapsed to the end, while collapse(true) and collapse('x') collapsed
        // to the start).
        bool toTheStart = Truthy(toStart);
        if (toTheStart) { _endContainer = _startContainer; _endOffset = _startOffset; }
        else { _startContainer = _endContainer; _startOffset = _endOffset; }
    }

    /// <summary>A range the engine makes is given its interface here: the host object reaches the
    /// page without going through the identity map, and 'range instanceof Range' needs the
    /// prototype the script side registered.</summary>
    internal static RangeHost Adopt(RangeHost host)
    {
        JsIntegrationService.FixProto(host);
        return host;
    }

    public RangeHost cloneRange()
        => Adopt(new RangeHost(_startContainer, _startOffset, _endContainer, _endOffset));

    /// <summary>DOM §3.4.10 with the four modes a page passes in from the 'Range' interface. The mode
    /// is answered before the trees are compared, and a range from another document is refused by its
    /// document — the reference names the document, not the tree (measured: WrongDocumentError).</summary>
    public int compareBoundaryPoints(int how, object? sourceRange)
    {
        if (sourceRange is not RangeHost other)
            throw new DOMException(
                "Failed to execute 'compareBoundaryPoints' on 'Range': parameter 2 is not of type 'Range'.",
                "TypeError");
        if (how is not (0 or 1 or 2 or 3))
            throw new DOMException("Failed to execute 'compareBoundaryPoints' on 'Range': The comparison"
                + " method provided must be one of 'START_TO_START', 'START_TO_END', 'END_TO_END',"
                + " or 'END_TO_START'.", "NotSupportedError");
        if (!ReferenceEquals(DocumentOf(_startContainer), DocumentOf(other._startContainer)))
            throw new DOMException("Failed to execute 'compareBoundaryPoints' on 'Range': The source"
                + " range is in a different document than this range.", "WrongDocumentError");

        Node node1, node2; int offset1, offset2;
        switch (how)
        {
            case 0: node1 = _startContainer; offset1 = _startOffset; node2 = other._startContainer; offset2 = other._startOffset; break;
            case 1: node1 = _startContainer; offset1 = _startOffset; node2 = other._endContainer; offset2 = other._endOffset; break;
            case 2: node1 = _endContainer; offset1 = _endOffset; node2 = other._endContainer; offset2 = other._endOffset; break;
            default: node1 = _endContainer; offset1 = _endOffset; node2 = other._startContainer; offset2 = other._startOffset; break;
        }
        return ComparePoints(node1, offset1, node2, offset2);
    }

    /// <summary>§3.4.11: -1 the point is before the range, 0 inside it, 1 after it.</summary>
    public int comparePoint(object? node, double offset)
    {
        var n = RequireNode(node, "comparePoint");
        int at = CheckOffset(n, offset, "comparePoint");
        if (!ReferenceEquals(RootOf(n), RootOf(_startContainer))) return 1;
        if (PointComesFirst(n, at, _startContainer, _startOffset)) return -1;
        if (PointComesFirst(_endContainer, _endOffset, n, at)) return 1;
        return 0;
    }

    public bool intersectsNode(object? node)
    {
        var n = RequireNode(node, "intersectsNode");
        if (!ReferenceEquals(RootOf(n), RootOf(_startContainer))) return false;
        var parent = n.ParentNode;
        if (parent == null) return true;
        int index = parent.ChildNodes.IndexOfNode(n);
        if (PointComesFirst(parent, index + 1, _startContainer, _startOffset)) return false;
        if (PointComesFirst(_endContainer, _endOffset, parent, index)) return false;
        return true;
    }

    /// <summary>The question the selection's 'containsNode' asks of its range, and which the
    /// reference does not let the page ask of a Range at all (measured: 'r.containsNode is not a
    /// function'). Blink answers it by the position of the node's two edges: a node is contained only
    /// when both edges lie strictly inside the range, so a node whose start is the range's own start
    /// is not — which is why a select-all reports its outermost element as not contained while it
    /// reports the one inside it as contained (measured both).</summary>
    internal bool ContainsWholly(Node n)
    {
        if (!ReferenceEquals(RootOf(n), RootOf(_startContainer))) return false;
        var parent = n.ParentNode;
        if (parent == null) return false;
        int index = parent.ChildNodes.IndexOfNode(n);
        return PointComesFirst(_startContainer, _startOffset, parent, index)
            && PointComesFirst(parent, index + 1, _endContainer, _endOffset);
    }

    // ===== The content between the points (DOM §3.6) =====

    /// <summary>The characters the range covers: every piece of character data it touches, with
    /// the two boundary nodes cut at the offsets (§3.6.6 "the text of a range").</summary>
    public string toString()
    {
        if (collapsed) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var node in Enumerate(RootOf(_startContainer)))
        {
            if (node is not CharacterData data) continue;
            if (!NodeIntersects(node)) continue;
            int from = ReferenceEquals(node, _startContainer) ? _startOffset : 0;
            int to = ReferenceEquals(node, _endContainer) ? _endOffset : data.Length;
            if (to > from) sb.Append(data.Data.Substring(from, to - from));
        }
        return sb.ToString();
    }

    public override string ToString() => toString();

    /// <summary>§3.6.9. A DocumentFragment does not go into the tree itself — its children do, and
    /// the range then holds exactly what was put in: start before the first of them, end after the
    /// last (measured: inserting one element into a collapsed (p, 0) left the range at (p, 0)–(p, 1),
    /// an empty fragment at (p, 0)–(p, 0), and a node put between the halves of a split text left the
    /// start on the text and moved the end to (p, 2)).</summary>
    public void insertNode(object? node)
    {
        var n = RequireNode(node, "insertNode");
        if (n is DomDocument or Acrux.Core.Dom.DocumentTypeNode)
            throw new DOMException("Failed to execute 'insertNode' on 'Range': Unable to insert a node"
                + " into a Range starting from the node itself.", "HierarchyRequestError");
        if (IsSelfOrAncestorOfStart(n))
            throw new DOMException("Failed to execute 'insertNode' on 'Range': The new child element"
                + " contains the parent.", "HierarchyRequestError");
        if (_startContainer is Acrux.Core.Dom.CommentNode or Acrux.Core.Dom.DocumentTypeNode)
            throw new DOMException($"Failed to execute 'insertNode' on 'Range': Nodes of type"
                + $" '{n.NodeName}' may not be inserted inside nodes of type '{_startContainer.NodeName}'.",
                "HierarchyRequestError");

        var children = n is Acrux.Core.Dom.DocumentFragment fragment
            ? fragment.ChildNodes.ToList()
            : new List<Node> { n };

        Node parent;
        Node? referenceNode;
        bool startStaysInText = false;
        if (_startContainer is TextNode text)
        {
            if (_startOffset > 0 && _startOffset < text.Length)
            {
                // The node goes between the two halves of the split text; the start of the range
                // stays on the first half, where it already was.
                referenceNode = text.SplitText(_startOffset);
                parent = text.ParentNode!;
                startStaysInText = true;
            }
            else
            {
                parent = text.ParentNode
                    ?? throw new DOMException("Failed to execute 'insertNode' on 'Range': the boundary"
                        + " has no parent to insert into.", "InvalidModificationError");
                referenceNode = _startOffset >= text.Length ? text.NextSibling : text;
            }
        }
        else
        {
            parent = _startContainer;
            var kids = parent.ChildNodes;
            referenceNode = _startOffset < kids.Count ? kids[_startOffset] : null;
        }

        int at = referenceNode == null
            ? parent.ChildNodes.Count
            : parent.ChildNodes.IndexOfNode(referenceNode);
        foreach (var child in children) parent.InsertBefore(child, referenceNode);

        if (!startStaysInText)
        {
            _startContainer = parent;
            _startOffset = at;
        }
        _endContainer = parent;
        _endOffset = at + children.Count;
    }

    public void deleteContents() => MoveContents(toFragment: false);

    public object? extractContents() => MoveContents(toFragment: true);

    public object? cloneContents()
    {
        var fragment = new Acrux.Core.Dom.DocumentFragment();
        if (collapsed) return WrapFragment(fragment);
        foreach (var node in CoveredNodes())
            fragment.AppendChild(node.CloneNode(true));
        return WrapFragment(fragment);
    }

    /// <summary>§3.6.11. Only an element can wrap a range, and the refusal for anything else is a
    /// HierarchyRequestError, not the InvalidStateError the partially-selected check gives (both
    /// measured: "This node type does not support this method." / "The Range has partially selected
    /// a non-Text node.").</summary>
    public void surroundContents(object? newParent)
    {
        var parent = RequireNode(newParent, "surroundContents");
        if (parent is not DomElement)
            throw new DOMException("Failed to execute 'surroundContents' on 'Range': This node type"
                + " does not support this method.", "HierarchyRequestError");

        // §3.6.11 step 1: the two boundaries have to lie on one line of descent, or there is no
        // single node to put the wrapper in.
        if (!IsAncestorOrSelf(_startContainer, _endContainer) && !IsAncestorOrSelf(_endContainer, _startContainer))
            throw new DOMException(
                "Failed to execute 'surroundContents' on 'Range': The Range has partially selected"
                + " a non-Text node.",
                "InvalidStateError");

        // Steps 2–7: only a Text node may be taken half. An element that the boundaries cut — the
        // inclusive ancestor of one boundary and not of the other — cannot be lifted whole into
        // the wrapper, so the wrap is refused.
        if (PartiallyContainedNonText(_startContainer, _endContainer) || PartiallyContainedNonText(_endContainer, _startContainer))
            throw new DOMException(
                "Failed to execute 'surroundContents' on 'Range': The Range has partially selected"
                + " a non-Text node.",
                "InvalidStateError");

        var fragment = MoveContents(toFragment: true);
        if (fragment is not Acrux.Core.Dom.DocumentFragment content) return;

        var boundary = _startContainer;
        int offset = _startOffset;
        InsertAt(boundary, offset, parent);
        foreach (var child in content.ChildNodes.ToList()) parent.AppendChild(child);
        selectNode(parent);
    }

    /// <summary>True when a node on the way down from 'from' to the root holds one boundary and
    /// not the other, and is not a text node the range is allowed to cut (DOM §3.4 partially
    /// contained).</summary>
    private static bool PartiallyContainedNonText(Node from, Node other)
    {
        for (var node = from; node != null; node = node.ParentNode)
            if (!IsAncestorOrSelf(node, other) && node is not TextNode) return true;
        return false;
    }

    public void detach() { }

    /// <summary>DOM §3.6.12: the markup is parsed as if it had been put inside the context the range
    /// starts in, which is what lets a fragment of table markup keep or lose its rows depending on
    /// where the range sits. The argument is a string however the page spells it.</summary>
    public object? createContextualFragment(string? context)
    {
        var fragment = new Acrux.Core.Dom.DocumentFragment();
        if (string.IsNullOrEmpty(context)) return WrapFragment(fragment);
        var host = _startContainer is CharacterData data ? data.ParentNode : _startContainer;
        var tag = host as DomElement ?? RootOf(host) as DomElement;
        foreach (var node in HtmlParser.ParseFragment(context, tag?.TagName ?? "body"))
            fragment.AppendChild(node);
        return WrapFragment(fragment);
    }

    /// <summary>Chromium answers this on a boundary point, not on a layout coordinate (measured:
    /// isPointInRange(p, 0) is true for a range that starts there, and the two refusals name
    /// parameter 1 as a Node) — the same question comparePoint answers, asked as a yes or no.</summary>
    public bool isPointInRange(object? node, double offset) => comparePoint(node, offset) == 0;

    /// <summary>DOM §3.6.13 (deprecated): grow the range out to the words it touches. Only the 'word'
    /// unit changes anything — measured: 'character' and a unit the reference does not know leave the
    /// range exactly as the page set it. A caret inside a word takes the whole word (measured: offset
    /// 2 of "hello world" became (0, 5), which is the word without the space that follows it).</summary>
    public void expand(string? unit)
    {
        if (!string.Equals(unit, "word", StringComparison.Ordinal)) return;
        var startText = _startContainer as TextNode;
        var endText = _endContainer as TextNode;
        bool caret = ReferenceEquals(_startContainer, _endContainer) && _startOffset == _endOffset;
        if (startText != null)
            _startOffset = StartOfWord(startText.Data, _startOffset);
        if (endText != null)
        {
            int to = EndOfWord(endText.Data, caret ? _startOffset : _endOffset);
            _endOffset = to < _startOffset && ReferenceEquals(_endContainer, _startContainer)
                ? _startOffset
                : to;
        }
    }

    internal static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Backward word movement, which is not the same question as growing a range to its word:
    /// from inside a word it goes to the start of that word, and from a gap or the start of a word it
    /// goes back over the gap and then over the word before it (measured: offset 6 of "hello world" —
    /// the caret sitting on the 'w' — moved to 0 rather than staying where it was).</summary>
    internal static int PreviousWord(string data, int offset)
    {
        int i = offset;
        if (i > 0 && IsWordChar(data[i - 1]))
        {
            while (i > 0 && IsWordChar(data[i - 1])) i--;
            return i;
        }
        while (i > 0 && !IsWordChar(data[i - 1])) i--;
        while (i > 0 && IsWordChar(data[i - 1])) i--;
        return i;
    }

    /// <summary>Where the start of a range belongs once it is grown to a word: back to the beginning
    /// of the word the caret sits inside, otherwise forward to the beginning of the next word.</summary>
    internal static int StartOfWord(string data, int offset)
    {
        if (offset > 0 && offset < data.Length && IsWordChar(data[offset - 1]) && IsWordChar(data[offset]))
        {
            int i = offset;
            while (i > 0 && IsWordChar(data[i - 1])) i--;
            return i;
        }
        int j = offset;
        while (j < data.Length && !IsWordChar(data[j])) j++;
        return j;
    }

    /// <summary>The other half of the same question: forward to the end of the word that starts at or
    /// contains the caret, back to the end of the word behind it when the caret sits on a gap.</summary>
    internal static int EndOfWord(string data, int offset)
    {
        if (offset < data.Length && IsWordChar(data[offset]))
        {
            int i = offset;
            while (i < data.Length && IsWordChar(data[i])) i++;
            return i;
        }
        int k = offset - 1;
        while (k >= 0 && !IsWordChar(data[k])) k--;
        while (k >= 0 && IsWordChar(data[k])) k--;
        return k + 1;
    }

    // CSSOM View 4: a range's rectangles are the line fragments of the text it covers, so the boxes
    // come from the elements that own the character data inside the range (measured: a range over one
    // short paragraph answers more than one rectangle and its bounding box is their union). A range
    // over content that was never attached has no boxes at all, which is the empty list the
    // reference answers for it.
    public DomRect[] getClientRects()
    {
        var rects = new List<DomRect>();
        var seen = new HashSet<DomElement>();
        foreach (var node in Enumerate(RootOf(_startContainer)))
        {
            if (!NodeIntersects(node)) continue;
            var owner = node is DomElement element && !HasTextInside(element)
                ? element
                : node is CharacterData ? node.ParentNode as DomElement : null;
            if (owner == null || !seen.Add(owner)) continue;
            rects.AddRange(new ElementHost(owner).ClientRects());
        }
        return rects.ToArray();
    }

    public DomRect getBoundingClientRect()
    {
        var rects = getClientRects();
        if (rects.Length == 0) return new DomRect(0, 0, 0, 0);
        float left = rects[0].left, top = rects[0].top, right = rects[0].right, bottom = rects[0].bottom;
        for (int i = 1; i < rects.Length; i++)
        {
            var rect = rects[i];
            if (rect.left < left) left = rect.left;
            if (rect.top < top) top = rect.top;
            if (rect.right > right) right = rect.right;
            if (rect.bottom > bottom) bottom = rect.bottom;
        }
        return new DomRect(left, top, right - left, bottom - top);
    }

    private static bool HasTextInside(Node node)
    {
        foreach (var child in node.ChildNodes)
            if (child is CharacterData || HasTextInside(child)) return true;
        return false;
    }

    // ===== The tree work behind the content methods =====

    /// <summary>Takes the covered nodes out of the tree and, when asked, into a fragment; the
    /// range then sits collapsed where its content was.</summary>
    private object? MoveContents(bool toFragment)
    {
        if (collapsed) return toFragment ? new Acrux.Core.Dom.DocumentFragment() : null;

        NormaliseBoundaries();
        var run = CoveredNodes();
        if (run.Count == 0) return toFragment ? new Acrux.Core.Dom.DocumentFragment() : null;

        var fragment = toFragment ? new Acrux.Core.Dom.DocumentFragment() : null;
        var home = run[0].ParentNode!;
        int at = home.ChildNodes.IndexOfNode(run[0]);

        foreach (var node in run)
        {
            var parent = node.ParentNode;
            if (parent == null) continue;
            parent.RemoveChild(node);
            fragment?.AppendChild(node);
        }

        _startContainer = home; _startOffset = Math.Max(0, at);
        _endContainer = home; _endOffset = _startOffset;
        return fragment;
    }

    /// <summary>Splits the character data both boundaries cut through, so everything the range
    /// covers is a whole node (DOM §3.6 first/last partially contained children).</summary>
    private void NormaliseBoundaries()
    {
        if (_startContainer is TextNode startCd)
        {
            if (_startOffset > 0 && _startOffset < startCd.Length)
            {
                var tail = startCd.SplitText(_startOffset);
                if (ReferenceEquals(_endContainer, startCd))
                {
                    _endContainer = tail;
                    _endOffset = Math.Max(0, _endOffset - _startOffset);
                }
                _startContainer = tail;
                _startOffset = 0;
            }
            else if (_startOffset >= startCd.Length)
            {
                var parent = startCd.ParentNode;
                if (parent != null) _startOffset = parent.ChildNodes.IndexOfNode(startCd) + 1;
            }
        }
        else if (_startContainer is CharacterData startWhole)
        {
            // 'Split text nodes' only cuts Text and CDATA, so a boundary inside a comment is moved
            // out to the node itself: the comment is then taken whole or not at all.
            var parent = startWhole.ParentNode;
            if (parent != null)
            {
                var index = parent.ChildNodes.IndexOfNode(startWhole);
                _startContainer = parent;
                _startOffset = _startOffset > 0 ? index + 1 : index;
            }
        }

        if (_endContainer is TextNode endCd && _endOffset > 0 && _endOffset < endCd.Length)
        {
            endCd.SplitText(_endOffset);
            _endOffset = endCd.Length;
        }
        else if (_endContainer is CharacterData endWhole)
        {
            var parent = endWhole.ParentNode;
            if (parent != null)
            {
                var index = parent.ChildNodes.IndexOfNode(endWhole);
                _endContainer = parent;
                _endOffset = _endOffset > 0 ? index + 1 : index;
            }
        }
    }

    /// <summary>The nodes the range covers, nearest the boundary first, none of them holding
    /// another one that is covered too.</summary>
    private List<Node> CoveredNodes()
    {
        var contained = new List<Node>();
        foreach (var node in Enumerate(RootOf(_startContainer)))
        {
            // A range's own boundary parent is never its content: the range (div,0)–(div,3) holds
            // the div's children, not the div. Only an element boundary is exempt — a text node the
            // boundaries cut is content, and the split above made it a whole node.
            if (IsBoundaryParent(node)) continue;
            if (NodeContained(node)) contained.Add(node);
        }

        var selected = new List<Node>();
        foreach (var node in contained)
            if (!contained.Contains(node.ParentNode!)) selected.Add(node);

        return selected;
    }

    private bool IsBoundaryParent(Node node)
        => (_startContainer is not CharacterData && ReferenceEquals(node, _startContainer))
        || (_endContainer is not CharacterData && ReferenceEquals(node, _endContainer));

    private bool NodeContained(Node node)
        => !PointComesFirst(StartPoint(node).node, StartPoint(node).offset, _startContainer, _startOffset)
        && !PointComesFirst(_endContainer, _endOffset, EndPoint(node).node, EndPoint(node).offset);

    private bool NodeIntersects(Node node)
        => !PointComesFirst(EndPoint(node).node, EndPoint(node).offset, _startContainer, _startOffset)
        && !PointComesFirst(_endContainer, _endOffset, StartPoint(node).node, StartPoint(node).offset);

    private static (Node node, int offset) StartPoint(Node node) => (node, 0);

    private static (Node node, int offset) EndPoint(Node node)
        => node is CharacterData data ? (node, data.Length) : (node, node.ChildNodes.Count);

    // ===== Ordering and tree helpers =====

    private static IEnumerable<Node> Enumerate(Node? root)
    {
        if (root == null) yield break;
        foreach (var child in root.ChildNodes)
        {
            yield return child;
            foreach (var deeper in Enumerate(child)) yield return deeper;
        }
    }

    /// <summary>How far the boundary of a container may be put: a piece of character data is cut at a
    /// character, anything else is indexed by its children. §3.4 uses this for the end of a selection
    /// over a node's contents and for the offset a refusal is measured against.</summary>
    internal static int BoundaryLength(Node node)
        => node is CharacterData data ? data.Length : node.ChildNodes.Count;

    private static bool IsAncestorOrSelf(Node ancestor, Node node)
    {
        for (var current = node; current != null; current = current.ParentNode)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private bool IsSelfOrAncestorOfStart(Node node)
    {
        for (var current = _startContainer; current != null; current = current.ParentNode)
            if (ReferenceEquals(current, node)) return true;
        return false;
    }

    internal static Node? RootOf(Node? node)
    {
        var current = node;
        while (current?.ParentNode != null) current = current.ParentNode;
        return current;
    }

    private static Node? CommonAncestor(Node a, Node b)
    {
        var ancestors = new HashSet<Node>();
        for (var current = a; current != null; current = current.ParentNode) ancestors.Add(current);
        for (var current = b; current != null; current = current.ParentNode)
            if (ancestors.Contains(current)) return current;
        return null;
    }

    private static int ComparePoints(Node node1, int offset1, Node node2, int offset2)
    {
        var key1 = BoundaryKey(node1, offset1);
        var key2 = BoundaryKey(node2, offset2);
        int shared = Math.Min(key1.Count, key2.Count);
        for (int i = 0; i < shared; i++)
        {
            if (key1[i] < key2[i]) return -1;
            if (key1[i] > key2[i]) return 1;
        }
        if (key1.Count == key2.Count) return 0;
        // The shorter chain is the boundary in the shallower container; it comes first when its
        // offset is before the deeper node, which the shared prefix already decided.
        return key1.Count < key2.Count ? -1 : 1;
    }

    /// <summary>The boundary as the chain of child indexes that runs down to its container, with
    /// the container's own offset last. Comparing those chains is the specification's locate a
    /// boundary point (DOM §3.4).</summary>
    private static List<int> BoundaryKey(Node container, int offset)
    {
        var path = new List<Node>();
        for (var current = container; current != null; current = current.ParentNode) path.Add(current);
        path.Reverse();

        var key = new List<int>();
        for (int i = 1; i < path.Count; i++)
            key.Add(path[i - 1].ChildNodes.IndexOfNode(path[i]));
        key.Add(offset);
        return key;
    }

    private static bool PointComesFirst(Node node1, int offset1, Node node2, int offset2)
        => ComparePoints(node1, offset1, node2, offset2) < 0;

    /// <summary>Which of two boundary points comes first in the tree — what a selection needs when it
    /// turns the anchor and focus the page gave it into the ordered range it reports.</summary>
    internal static bool ComesFirst(Node node1, int offset1, Node node2, int offset2)
        => PointComesFirst(node1, offset1, node2, offset2);

    private void InsertAt(Node container, int offset, Node node)
    {
        var kids = container.ChildNodes;
        var refNode = offset < kids.Count ? kids[offset] : null;
        container.InsertBefore(node, refNode);
    }

    internal static Node RequireNode(object? value, string method, string owner = "Range", int parameter = 1)
    {
        var node = ElementHost.UnwrapNode(value);
        if (node == null)
            throw new DOMException($"Failed to execute '{method}' on '{owner}': parameter {parameter} is"
                + " not of type 'Node'.", "TypeError");
        return node;
    }

    /// <summary>The offset of a boundary point is an unsigned long, so a negative spelling is refused
    /// as the wrapped number the reference prints (measured: setStart(text, -1) complained about the
    /// offset 4294967295), and a container of character data is cut at a character while anything else
    /// is indexed by its children — which is why the two refusals say different things.</summary>
    internal static int CheckOffset(Node node, double raw, string method, string owner = "Range")
    {
        double offset = WebIdL.ToUnsignedLong(raw);
        if (node is CharacterData data)
        {
            if (offset > data.Length)
                throw new DOMException($"Failed to execute '{method}' on '{owner}': The offset"
                    + $" {WebIdL.Format(offset)} is larger than the node's length ({data.Length}).",
                    "IndexSizeError");
        }
        else
        {
            int count = node.ChildNodes.Count;
            if (offset > count)
                throw new DOMException($"Failed to execute '{method}' on '{owner}': There is no child at"
                    + $" offset {WebIdL.Format(offset)}.", "IndexSizeError");
        }
        return (int)offset;
    }

    /// <summary>§3.4.15/§3.4.12: a boundary placed beside a node needs that node to have a parent, and
    /// the refusal names the node's type, not the operation (measured: InvalidNodeTypeError, "the given
    /// Node has no parent").</summary>
    private static DOMException NoParent(string method)
        => new($"Failed to execute '{method}' on 'Range': the given Node has no parent.",
            "InvalidNodeTypeError");

    /// <summary>The document a boundary's tree belongs to. A detached subtree still belongs to the
    /// document that made it, which is what lets two ranges in one detached tree be compared while a
    /// range from another document is refused (measured: WrongDocumentError).</summary>
    internal static Node? DocumentOf(Node? node)
        => node is DomDocument ? node : node?.OwnerDocument ?? RootOf(node);

    /// <summary>JavaScript truthiness, which is what an IDL boolean coerces from (measured:
    /// collapse(0) collapsed to the end while the default, no argument at all, collapses to the
    /// start).</summary>
    internal static bool Truthy(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        double number => number != 0 && !double.IsNaN(number),
        string text => text.Length > 0,
        _ => true
    };

    private static object? WrapFragment(Acrux.Core.Dom.DocumentFragment? fragment)
        => fragment == null ? null : JsIntegrationService.WrapAny(fragment);
}

/// <summary>HTML §2.5.3.4: the range the page has selected, with the anchor and focus points a drag
/// made. 'document.getSelection()' answers one object for the whole document.
/// <para>
/// The selection is kept as the ordered range plus the direction the page set it with, because that is
/// exactly how the reference reports it: 'getRangeAt(0)' answers the ordered range — the same object the
/// page added, still live, so moving its boundaries moves the selection (measured: after 'addRange(r)' and
/// 'r.setStart(text, 2)' the selection answered anchor 2, focus at the end, and 'getRangeAt(0) === r') —
/// while 'anchorNode'/'focusNode' answer that range's two ends in the direction the page asked for. The
/// direction is not derivable from a Range, which is always ordered, so it is stored beside it.</para>
/// <para>
/// The methods the page reaches are on 'Selection.prototype' in the bootstrap script, not here: the
/// reference counts a method's arguments before it looks at their types ('collapse()' refuses with "1
/// argument required, but only 0 present." rather than with a type error), and a host method cannot see
/// how many arguments arrived. Those wrappers check the count and call the '__'-prefixed member below,
/// which carries the refusals and the unsigned-long offsets. The name a refusal quotes is the name the
/// page called, so 'setPosition' and 'collapse' — which otherwise do the same thing — each have their
/// own member (measured: both error sentences name the caller).</para>
/// </summary>
public class SelectionHost : IDomPrototypeHost
{
    private readonly List<RangeHost> _ranges = new();
    private readonly Node? _document;
    private bool _backward;

    internal SelectionHost(Node? document = null) => _document = document;

    public string? __domType => "Selection";
    public string[] __domTypeChain => new[] { "Selection" };

    private RangeHost? Primary => _ranges.Count > 0 ? _ranges[0] : null;

    /// <summary>The two points the page put the selection at. A backward selection holds its anchor at
    /// the end of the ordered range.</summary>
    private (Node? node, int offset) AnchorPoint => Primary is not { } r
        ? (null, 0)
        : (_backward ? r.EndPointOf : r.StartPointOf);

    private (Node? node, int offset) FocusPoint => Primary is not { } r
        ? (null, 0)
        : (_backward ? r.StartPointOf : r.EndPointOf);

    // ===== The ranges (HTML §2.5.3.4 "methods") =====

    public int rangeCount => _ranges.Count;

    public object? __getRangeAt(double index)
    {
        // The index is an unsigned long: 'getRangeAt(-1)' asks for 4294967295 and 'getRangeAt(NaN)'
        // for 0, which is the range a non-empty selection has (both measured).
        double at = WebIdL.ToUnsignedLong(index);
        if (at >= _ranges.Count)
            throw new DOMException($"Failed to execute 'getRangeAt' on 'Selection':"
                + $" {WebIdL.Format(at)} is not a valid index.", "IndexSizeError");
        return _ranges[(int)at];
    }

    /// <summary>Measured: a second range is ignored while the first is still there, and a range whose
    /// content is not in this document is ignored too — including one over a detached subtree, which
    /// has no layout to select. Nothing is removed, so the selection keeps what it had.</summary>
    public void __addRange(object? range)
    {
        var host = RequireRange(range, "addRange");
        if (_ranges.Count > 0) return;
        if (!BelongsHere(host)) return;
        _ranges.Add(host);
        _backward = false;
    }

    public void __removeRange(object? range)
    {
        var host = RequireRange(range, "removeRange");
        if (!_ranges.Remove(host))
            throw new DOMException("Failed to execute 'removeRange' on 'Selection': Range not found.",
                "NotFoundError");
        _backward = false;
    }

    public void removeAllRanges()
    {
        _ranges.Clear();
        _backward = false;
    }

    // ===== What the selection is (HTML §2.5.3.4 "attributes") =====

    public object? anchorNode => JsIntegrationService.WrapAny(AnchorPoint.node);
    public int anchorOffset => AnchorPoint.offset;
    public object? focusNode => JsIntegrationService.WrapAny(FocusPoint.node);
    public int focusOffset => FocusPoint.offset;

    /// <summary>The older names for the same two points (measured: 'baseNode === anchorNode').</summary>
    public object? baseNode => anchorNode;
    public int baseOffset => anchorOffset;
    public object? extentNode => focusNode;
    public int extentOffset => focusOffset;

    /// <summary>"none" while the two points are one — including while there is no selection at all —
    /// and otherwise which way round they are (measured: a backward 'setBaseAndExtent' answers
    /// "backward", an added range answers "forward", and a collapsed one answers "none").</summary>
    public string direction
    {
        get
        {
            var primary = Primary;
            if (primary == null || primary.collapsed) return "none";
            return _backward ? "backward" : "forward";
        }
    }

    public bool isCollapsed => Primary == null || Primary.collapsed;

    // §2.5.3.4: 'None' with no range, 'Caret' with a collapsed one, 'Range' with content selected.
    public string type => Primary == null ? "None" : (Primary.collapsed ? "Caret" : "Range");

    // ===== Moving the two points =====

    /// <summary>A nullable node with no default: 'collapse(null)' clears the selection, while a value
    /// that is not a node is the type error the reference gives (both measured). Undefined reaches the
    /// host as the same CLR null, and the bootstrap wrapper refuses the absent argument first.</summary>
    public void __collapse(object? node, double offset = 0)
    {
        if (node == null) { removeAllRanges(); return; }
        SetCaret(node, offset, "collapse");
    }

    /// <summary>The same operation under the newer name, which quotes itself in the refusal it gives
    /// for an offset outside the node (measured: "Failed to execute 'setPosition' on 'Selection': The
    /// offset 99 is larger than the node's length (11).").</summary>
    public void __setPosition(object? node, double offset = 0) => SetCaret(node, offset, "setPosition");

    private void SetCaret(object? node, double raw, string api)
    {
        var n = RangeHost.RequireNode(node, api, "Selection");
        int at = RangeHost.CheckOffset(n, raw, api, "Selection");
        SetPoints(n, at, n, at);
    }

    /// <summary>Moves the focus and leaves the anchor where it was; with no selection there is no anchor
    /// to keep, so the caret is put at the point instead (measured: 'extend(p, 5)' on an element refuses
    /// with the child-index sentence the offset check gives, naming 'extend').</summary>
    public void __extend(object? node, double offset = 0)
    {
        var n = RangeHost.RequireNode(node, "extend", "Selection");
        int at = RangeHost.CheckOffset(n, offset, "extend", "Selection");
        var anchor = AnchorPoint;
        if (anchor.node == null) SetPoints(n, at, n, at);
        else SetPoints(anchor.node, anchor.offset, n, at);
    }

    public void __setBaseAndExtent(object? anchorNode, double anchorOffset, object? focusNode, double focusOffset)
    {
        // Every argument is converted before any of them is checked, so a value that is not a node in
        // parameter 3 is refused even when parameter 2 held an offset too large to be true (measured:
        // "parameter 1 is not of type 'Node'." for the anchor, "There is no child at offset 9." once
        // both nodes were nodes).
        var anchor = RangeHost.RequireNode(anchorNode, "setBaseAndExtent", "Selection");
        var focus = RangeHost.RequireNode(focusNode, "setBaseAndExtent", "Selection", parameter: 3);
        int at = RangeHost.CheckOffset(anchor, anchorOffset, "setBaseAndExtent", "Selection");
        int to = RangeHost.CheckOffset(focus, focusOffset, "setBaseAndExtent", "Selection");
        SetPoints(anchor, at, focus, to);
    }

    /// <summary>§2.5.3.4: the whole content of the node, measured in children or in characters, which
    /// is what 'selectNodeContents' of a range already answers.</summary>
    public void __selectAllChildren(object? node)
    {
        var n = RangeHost.RequireNode(node, "selectAllChildren", "Selection");
        SetPoints(n, 0, n, RangeHost.BoundaryLength(n));
    }

    private void SetPoints(Node anchor, int anchorOffset, Node focus, int focusOffset)
    {
        _backward = RangeHost.ComesFirst(focus, focusOffset, anchor, anchorOffset);
        var range = _backward
            ? new RangeHost(focus, focusOffset, anchor, anchorOffset)
            : new RangeHost(anchor, anchorOffset, focus, focusOffset);
        _ranges.Clear();
        _ranges.Add(RangeHost.Adopt(range));
    }

    // ===== What the selection holds =====

    public void collapseToStart()
    {
        var primary = Primary ?? throw NoSelection("collapseToStart");
        primary.collapse(true);
        _backward = false;
    }

    public void collapseToEnd()
    {
        var primary = Primary ?? throw NoSelection("collapseToEnd");
        primary.collapse(false);
        _backward = false;
    }

    /// <summary>DOM 4 "containsNode", which the reference answers by the position of the node's edges:
    /// a piece of character data is contained as soon as the selection touches it, while an element
    /// needs both its edges strictly inside the selection — so a select-all answers false for the
    /// elements at its own ends and true for the one between them (all measured).</summary>
    public bool __containsNode(object? node, object? allowPartial = null)
    {
        var n = RangeHost.RequireNode(node, "containsNode", "Selection");
        var primary = Primary;
        if (primary == null) return false;
        if (RangeHost.Truthy(allowPartial) || n is CharacterData) return primary.intersectsNode(n);
        return primary.ContainsWholly(n);
    }

    public void deleteFromDocument() => Primary?.deleteContents();

    public string toString() => Primary?.toString() ?? string.Empty;

    public override string ToString() => toString();

    /// <summary>The selection's ranges for the shadow-DOM-aware callers, as fresh objects each time
    /// (measured: 'getComposedRanges()[0] !== getRangeAt(0)'). The bootstrap script builds the Array
    /// from these clones, because a CLR array would reach the page as an array-like.</summary>
    public object? __composedRange(double index)
    {
        double at = WebIdL.ToUnsignedLong(index);
        if (at >= _ranges.Count) return null;
        return _ranges[(int)at].cloneRange();
    }

    /// <summary>The non-standard selection editor the pages of one browser vendor use. Blink walks its
    /// layout tree to answer this; the engine's selection only has the data tree, so the two granularities
    /// that live on text are carried out there and everything else leaves the selection where it was.
    /// Nothing is validated (measured: 'modify()' and 'modify(null, null, null)' both return undefined,
    /// and an unknown granularity changes nothing) and 'move' collapses the caret where it lands
    /// (measured: moving backward a word from offset 6 of "hello world" answered anchor and focus 0).</summary>
    public void modify(string? alter = null, string? direction = null, string? granularity = null)
    {
        var primary = Primary;
        if (primary == null) return;
        bool extend = Spelled(alter, "extend");
        if (!extend && !Spelled(alter, "move")) return;
        bool forward = Spelled(direction, "forward");
        bool byWord = Spelled(granularity, "word");
        if (!byWord && !Spelled(granularity, "character")) return;

        var focus = FocusPoint;
        if (focus.node is not TextNode text) return;
        int to = (forward, byWord) switch
        {
            (true, true) => RangeHost.EndOfWord(text.Data, focus.offset),
            (true, false) => System.Math.Min(text.Length, focus.offset + 1),
            (false, true) => RangeHost.PreviousWord(text.Data, focus.offset),
            (false, false) => System.Math.Max(0, focus.offset - 1)
        };
        if (extend)
        {
            var anchor = AnchorPoint;
            if (anchor.node == null) return;
            SetPoints(anchor.node, anchor.offset, text, to);
        }
        else SetPoints(text, to, text, to);
    }

    private static bool Spelled(string? left, string right)
        => string.Equals(left, right, StringComparison.Ordinal);

    private static DOMException NoSelection(string api)
        => new($"Failed to execute '{api}' on 'Selection': there is no selection.", "InvalidStateError");

    private static RangeHost RequireRange(object? value, string method)
    {
        if (value is not RangeHost host)
            throw new DOMException($"Failed to execute '{method}' on 'Selection': parameter 1 is not"
                + " of type 'Range'.", "TypeError");
        return host;
    }

    /// <summary>Whether the range's content is part of the document this selection belongs to. A range
    /// over another document, or over a subtree that was never attached, is ignored (measured both).</summary>
    private bool BelongsHere(RangeHost range)
    {
        if (_document == null) return true;
        return ReferenceEquals(RangeHost.RootOf(range.NativeStartContainer), _document)
            && ReferenceEquals(RangeHost.DocumentOf(range.NativeEndContainer),
                RangeHost.DocumentOf(range.NativeStartContainer));
    }
}
