namespace Acrux.Core.Dom;

using System.Runtime.CompilerServices;

/// <summary>DOM §3.5: the live ranges each document holds, which the boundary-adjustment algorithms
/// walk when the tree changes under them.</summary>
internal static class LiveRangeRegistry
{
    private static readonly ConditionalWeakTable<Document, List<WeakReference<Acrux.Core.JavaScript.RangeHost>>> _byDocument = new();

    public static void Register(Acrux.Core.JavaScript.RangeHost range)
    {
        var document = DocumentOf(range);
        if (document == null) return;
        var list = _byDocument.GetOrCreateValue(document);
        // Occasional eviction of dead entries keeps the list bounded.
        if (list.Count > 128)
            list.RemoveAll(w => !w.TryGetTarget(out _));
        list.Add(new WeakReference<Acrux.Core.JavaScript.RangeHost>(range));
    }

    public static void ForEach(Document document, Action<Acrux.Core.JavaScript.RangeHost> action)
    {
        if (!_byDocument.TryGetValue(document, out var list)) return;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].TryGetTarget(out var range))
            {
                list.RemoveAt(i);
                continue;
            }
            action(range);
        }
    }

    private static Document? DocumentOf(Acrux.Core.JavaScript.RangeHost range)
    {
        // The same parent-chain walk 'FindDocument' uses: a node from the parser may carry no
        // OwnerDocument but always carries a parent that leads to the document.
        var node = range.NativeStartContainer;
        while (node != null)
        {
            if (node is Document doc) return doc;
            node = node.ParentNode;
        }
        return null;
    }
}

/// <summary>DOM §3.5: the three boundary-adjustment algorithms that run when the tree changes under
/// a live range. Without them a range left pointing at a node that has been taken out answers a
/// boundary that no longer exists in the tree (§3.5 "removing", §3.3.10 "splitting text").</summary>
internal static class LiveRangeAdjustments
{
    /// <summary>The Range's own algorithms (deleteContents, extractContents, deleteNode) move
    /// nodes through RemoveChild/InsertBefore and then set their boundaries themselves; the
    /// adjustment would fire mid-operation and over-write a boundary the Range is about to set.
    /// §3.5 is about a change the page made, not one the Range's own algorithm is making.</summary>
    internal static bool Suppressed;

    /// <summary>§3.5 "removing": a node is leaving its parent. Every live range boundary inside the
    /// node moves to the position the node held; a boundary after it in the parent shifts back one.
    /// (measured: a range from (p1.firstChild,0) to (p2.firstChild,3) collapses its start to
    /// (box,0) when p1 is taken out, while the end stays put.)</summary>
    public static void Remove(Node node)
    {
        if (Suppressed) return;
        var parent = node.ParentNode;
        if (parent == null) return;
        var document = FindDocument(parent);
        if (document == null) return;
        int index = parent.ChildNodes.IndexOf(node);
        if (index < 0) return;

        LiveRangeRegistry.ForEach(document, range =>
        {
            var startNode = range.NativeStartContainer;
            if (startNode == node || IsInclusiveDescendant(startNode, node))
                range.SetStartBoundary(parent, index);
            else if (startNode == parent && range.NativeStartOffset > index)
                range.SetStartBoundary(parent, range.NativeStartOffset - 1);

            var endNode = range.NativeEndContainer;
            if (endNode == node || IsInclusiveDescendant(endNode, node))
                range.SetEndBoundary(parent, index);
            else if (endNode == parent && range.NativeEndOffset > index)
                range.SetEndBoundary(parent, range.NativeEndOffset - 1);
        });
    }

    /// <summary>§3.5 "inserting": a node arrives in a parent at a known position. Every live
    /// range boundary in the parent at or after the insertion index shifts forward by the length of
    /// what was inserted.</summary>
    public static void InsertAt(Node inserted, Node parent, int index)
    {
        if (Suppressed) return;
        var document = FindDocument(parent);
        if (document == null) return;
        int length = NodeLength(inserted);
        if (length == 0) return;

        LiveRangeRegistry.ForEach(document, range =>
        {
            if (range.NativeStartContainer == parent && range.NativeStartOffset >= index)
                range.SetStartBoundary(parent, range.NativeStartOffset + length);
            if (range.NativeEndContainer == parent && range.NativeEndOffset >= index)
                range.SetEndBoundary(parent, range.NativeEndOffset + length);
        });
    }

    /// <summary>§3.3.10: a text node splits at an offset, and every live range boundary past the
    /// split moves to the new node at its reduced position. The start uses a strict greater-than,
    /// the end an at-least — the boundary that was exactly at the split point belongs to the tail.
    /// (measured: a range (text,4)–(text,6) with the node split at 2 moves to (tail,2)–(tail,4).)</summary>
    public static void SplitText(TextNode original, TextNode tail, int offset)
    {
        if (Suppressed) return;
        var document = FindDocument(original);
        if (document == null) return;

        LiveRangeRegistry.ForEach(document, range =>
        {
            var startNode = range.NativeStartContainer;
            var startOffset = range.NativeStartOffset;
            if (startNode == original && startOffset > offset)
                range.SetStartBoundary(tail, startOffset - offset);

            var endNode = range.NativeEndContainer;
            var endOffset = range.NativeEndOffset;
            if (endNode == original && endOffset >= offset)
                range.SetEndBoundary(tail, endOffset - offset);
        });
    }

    public static void Register(Acrux.Core.JavaScript.RangeHost range) => LiveRangeRegistry.Register(range);

    private static Document? FindDocument(Node node)
    {
        // A node from the parser may carry no OwnerDocument at all — the parent chain is what
        // 'RangeHost.DocumentOf' walks, and so does this (measured: a text node from innerHTML has
        // no OwnerDocument but does have a parent that leads to the document).
        var current = node;
        while (current != null)
        {
            if (current is Document document) return document;
            current = current.ParentNode;
        }
        return null;
    }

    private static bool IsInclusiveDescendant(Node candidate, Node ancestor)
    {
        var current = candidate;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.ParentNode;
        }
        return false;
    }

    private static int NodeLength(Node node)
    {
        if (node is TextNode text) return text.Data?.Length ?? 0;
        if (node is CommentNode comment) return comment.Data?.Length ?? 0;
        return 1;
    }
}
