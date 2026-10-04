namespace Acrux.PageContract;

/// <summary>
/// Serialized DOM node. The engine owns node identity: ids are document preorder
/// indexes, valid only while <see cref="DtNodeBatch.MutationVersion"/> holds. When the
/// DOM mutation version moves, every cached id is void and the client's batches carry
/// <see cref="DtNodeBatch.VersionMoved"/> instead of wrong data.
/// </summary>
public sealed record DtNode
{
    public int Id { get; init; }
    public int ParentId { get; init; } = -1;
    /// <summary>Preorder depth: document root is 0, its children 1, and so on.</summary>
    public int Depth { get; init; }
    /// <summary>1 element, 3 text, 8 comment, 9 document, 10 doctype — the DOM nodeType.</summary>
    public int NodeType { get; init; }
    public string NodeName { get; init; } = "";
    public string LocalName { get; init; } = "";
    /// <summary>Attribute names and values interleaved: [name0, value0, name1, value1, ...].</summary>
    public IReadOnlyList<string> Attributes { get; init; } = Array.Empty<string>();
    /// <summary>Trimmed text for text nodes (and data for comments), preview-capped; empty for others.</summary>
    public string TextPreview { get; init; } = "";
    public int ChildCount { get; init; }
    /// <summary>True when the node has at least one Element child — the shapes the
    /// Elements panel draws with (expansion marker, whether text children render).</summary>
    public bool HasElementChildren { get; init; }
}

/// <summary>One streamed chunk of the preorder node tree. When <see cref="Truncated"/>
/// is set, the client continues from the last received id + 1 under the same version.</summary>
public sealed record DtNodeBatch(long MutationVersion, IReadOnlyList<DtNode> Nodes, bool Truncated, bool VersionMoved)
{
    /// <summary>Failure batch telling the client its cached ids belong to a DOM version
    /// that no longer exists; <see cref="DtNodeBatch.MutationVersion"/> is the live one.</summary>
    public static DtNodeBatch Moved(long currentVersion) =>
        new(currentVersion, Array.Empty<DtNode>(), false, true);

    /// <summary>Nothing to serve (dead host / unsupported page).</summary>
    public static DtNodeBatch Failed(long currentVersion) =>
        new(currentVersion, Array.Empty<DtNode>(), false, true);
}

/// <summary>One CSS declaration as text; the panel renders it verbatim.</summary>
public sealed record DtDeclaration(string Name, string Value, bool Important, bool Overridden);

public sealed record DtStyleRule(
    string SelectorText, string SourceSheet, int SourceLine, IReadOnlyList<DtDeclaration> Declarations);

/// <summary>Styles for one node: matched rules in cascade order (UA first), the inline
/// style attribute, and the computed-style subset the panel draws. A null batch means
/// the node id is unknown; <see cref="VersionMoved"/> means the id is stale because the
/// DOM version changed.</summary>
public sealed record DtStyleBatch(
    int NodeId,
    long MutationVersion,
    IReadOnlyList<DtStyleRule> MatchedRules,
    IReadOnlyList<DtDeclaration> InlineStyle,
    IReadOnlyList<DtDeclaration> ComputedStyle,
    bool VersionMoved);

/// <summary>
/// Read-only inspection of a page the shell may not be able to touch directly. The
/// in-process and out-of-process hosts both answer through these callbacks; nothing
/// here may block the caller. Every callback can be invoked with a failure value when
/// the page host dies mid-request. Callbacks may arrive on a transport thread — the
/// shell marshals to its UI thread.
///
/// <paramref name="knownVersion"/> is the mutation version the client's cached ids
/// were minted under (-1 = no cached state). If the engine's version moved, the batch
/// reports <see cref="DtNodeBatch.VersionMoved"/> rather than serving ids that now
/// address different nodes.
/// </summary>
public interface IDevToolsChannel
{
    /// <summary>False when the page host cannot serve inspection at all.</summary>
    bool Supported { get; }

    /// <summary>Stream preorder nodes starting at <paramref name="nodeId"/> (0 = document
    /// root). <paramref name="maxDepth"/> counts down from that node's depth (0/negative =
    /// unbounded); the engine answers at most <paramref name="limit"/> nodes and one
    /// message's worth of bytes.</summary>
    void RequestChildren(int nodeId, long knownVersion, int maxDepth, int limit, Action<DtNodeBatch> done);

    void RequestStyles(int nodeId, long knownVersion, Action<DtStyleBatch?> done);

    /// <summary>Evaluate <paramref name="script"/> in the page. The callback may be a
    /// timeout error when the page's script loop is wedged — callers never block.</summary>
    void Evaluate(string script, Action<EvalResult> done);
}
