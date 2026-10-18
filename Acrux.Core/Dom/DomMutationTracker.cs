namespace Acrux.Core.Dom;

/// <summary>
/// Global DOM mutation counter. Hosts snapshot <see cref="Version"/> around a
/// JS callback and only mark the page dirty when something actually changed —
/// a timer that merely computes must not trigger a full style/layout/paint
/// rebuild and a tile-cache wipe.
/// Every mutating DOM surface (node tree ops, attributes, inline style, text
/// data, form control state) funnels through <see cref="Notify"/>.
/// </summary>
public static class DomMutationTracker
{
    private static long _version;
    private static volatile bool _trackAttributes;
    private static volatile bool _trackChildList;
    private static volatile bool _trackCharacterData;
    private static readonly List<MutationJournalChange> _changes = new();
    private static readonly object _lock = new();

    /// <summary>The three kinds a MutationObserver can ask for are recorded separately, and only while
    /// some live registration wants them: a page that never observes pays nothing for every keystroke of
    /// a typewriter demo or every attribute of a virtual list (DOM §4.5.4).</summary>
    public static bool TrackCharacterData
    {
        get => _trackCharacterData;
        set
        {
            _trackCharacterData = value;
            if (!value) Clear();
        }
    }

    public static bool TrackAttributes
    {
        get => _trackAttributes;
        set
        {
            _trackAttributes = value;
            if (!value) Clear();
        }
    }

    public static bool TrackChildList
    {
        get => _trackChildList;
        set
        {
            _trackChildList = value;
            if (!value) Clear();
        }
    }

    /// <summary>Turns the three recordings on or off in one step, as the JS side does when its set of
    /// registrations changes. Nothing is dropped when a kind stays on — only an all-off switch empties
    /// what was queued, the same as the individual setters.</summary>
    public static void SetTracking(bool attributes, bool childList, bool characterData)
    {
        _trackAttributes = attributes;
        _trackChildList = childList;
        _trackCharacterData = characterData;
        if (!attributes && !childList && !characterData) Clear();
    }

    public static long Version => Interlocked.Read(ref _version);

    public static void Notify() => Interlocked.Increment(ref _version);

    /// <summary>DOM §4.5.7: every record carries the value the target held <em>before</em> that one
    /// mutation, and several edits between two checkpoints leave no trace in a snapshot comparison —
    /// a name written twice answers the first value only once, and an attribute added and taken away
    /// again answers nothing at all. So each mutation is written down as it happens and the observer
    /// drains the list into records, one per mutation, in order.</summary>
    public static void NoteCharacterData(CharacterData node, string oldValue, string newValue)
    {
        if (!_trackCharacterData || oldValue == newValue) return;
        Add(new MutationJournalChange
        {
            Kind = MutationJournalKind.CharacterData,
            Node = node,
            OldValue = oldValue,
            Scope = ScopeOf(node),
        });
    }

    /// <summary>The write an attribute (or a live <see cref="Attr"/>) made. The name is reported as the
    /// reference reports it: the local part with the namespace beside it when the attribute was set
    /// under one (measured: 'href' with the XLink URI, not 'xlink:href' with null).</summary>
    public static void NoteAttribute(Element node, string name, string? ns, string? oldValue, string? newValue)
    {
        if (!_trackAttributes || oldValue == newValue) return;
        var reported = string.IsNullOrEmpty(ns) ? name : Element.LocalPartOf(name);
        Add(new MutationJournalChange
        {
            Kind = MutationJournalKind.Attribute,
            Node = node,
            Name = reported,
            Namespace = ns,
            OldValue = oldValue,
            Scope = ScopeOf(node),
        });
    }

    /// <summary>The one child-list mutation a tree operation made: the nodes that came in and the ones
    /// that went out are the two lists of a single record, with the siblings that framed the change.</summary>
    public static void NoteChildList(Node parent, IReadOnlyList<Node> added, IReadOnlyList<Node> removed,
        Node? previousSibling, Node? nextSibling)
    {
        if (!_trackChildList) return;
        if (added.Count == 0 && removed.Count == 0) return;
        Add(new MutationJournalChange
        {
            Kind = MutationJournalKind.ChildList,
            Node = parent,
            Added = added.ToArray(),
            Removed = removed.ToArray(),
            PreviousSibling = previousSibling,
            NextSibling = nextSibling,
            Scope = ScopeOf(parent),
        });
    }

    /// <summary>The node and the ancestors it had <em>at the moment of the mutation</em>. A registration
    /// covers a subtree, and whether an entry belongs to it has to be answered from the tree as it was
    /// when the change happened — a page that moves a node out and then reads its records is still told
    /// about the change inside the subtree it observes (DOM §4.5.4, measured: an attribute written on a
    /// child and the child removed afterwards both reach the observer of the parent).</summary>
    private static Node[] ScopeOf(Node? node)
    {
        var chain = new List<Node>();
        for (var current = node; current != null; current = current.ParentNode)
        {
            chain.Add(current);
            if (chain.Count > 64) break;
        }
        return chain.ToArray();
    }

    /// <summary>Hands over everything recorded since the last call and empties the log.</summary>
    public static List<MutationJournalChange> TakeChanges()
    {
        lock (_lock)
        {
            if (_changes.Count == 0) return new List<MutationJournalChange>();
            var taken = new List<MutationJournalChange>(_changes);
            _changes.Clear();
            return taken;
        }
    }

    private static void Add(MutationJournalChange change)
    {
        lock (_lock)
        {
            // A page that mutates without ever reaching a checkpoint must not grow this without
            // bound (a detached document, a stalled microtask queue).
            if (_changes.Count >= 8192) return;
            _changes.Add(change);
        }
    }

    private static void Clear()
    {
        lock (_lock) _changes.Clear();
    }
}

public enum MutationJournalKind { Attribute, ChildList, CharacterData }

/// <summary>One mutation as it happened: the target, and only the fields its kind carries.</summary>
public sealed class MutationJournalChange
{
    public MutationJournalKind Kind;
    public Node Node = null!;
    public string? Name;
    public string? Namespace;
    public string? OldValue;
    public Node[]? Added;
    public Node[]? Removed;
    public Node? PreviousSibling;
    public Node? NextSibling;
    /// <summary>The target and the ancestors it had when the mutation happened (see <c>ScopeOf</c>).</summary>
    public Node[]? Scope;
}
