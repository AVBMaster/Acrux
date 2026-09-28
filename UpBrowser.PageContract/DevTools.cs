namespace UpBrowser.PageContract;

/// <summary>
/// Serialized DOM node. The engine owns node identity; ids are only meaningful until
/// <see cref="IDevToolsChannel.MutationVersion"/> advances, after which every cached id is void.
/// </summary>
public sealed record DtNode
{
    public int Id { get; init; }
    public int ParentId { get; init; } = -1;
    /// <summary>Child ids; empty when the engine knows the node has children but has not sent them.</summary>
    public IReadOnlyList<int> ChildIds { get; init; } = Array.Empty<int>();
    /// <summary>1 element, 3 text, 8 comment — the DOM nodeType the elements panel prints.</summary>
    public int NodeType { get; init; }
    public string NodeName { get; init; } = "";
    public string LocalName { get; init; } = "";
    public string NamespaceUri { get; init; } = "";
    public IReadOnlyList<string> Attributes { get; init; } = Array.Empty<string>();
    public string TextPreview { get; init; } = "";
    public int ChildCount { get; init; }
    public PageRect BoundRect { get; init; }
    public bool IsVisible { get; init; }
}

public sealed record DtNodeBatch(long MutationVersion, IReadOnlyList<DtNode> Nodes, bool Truncated);

/// <summary>One CSS declaration as text; the panel renders it verbatim.</summary>
public sealed record DtDeclaration(string Name, string Value, bool Important, bool Overridden);

public sealed record DtStyleRule(
    string SelectorText, string SourceSheet, int SourceLine, IReadOnlyList<DtDeclaration> Declarations);

public sealed record DtStyleBatch(
    int NodeId,
    long MutationVersion,
    IReadOnlyList<DtStyleRule> MatchedRules,
    IReadOnlyList<DtDeclaration> InlineStyle,
    IReadOnlyList<DtDeclaration> ComputedStyle);

public enum ConsoleLevel
{
    Log,
    Info,
    Warn,
    Error,
}

public sealed record DtConsoleMessage(ConsoleLevel Level, string Text, string Source, int Line);

/// <summary>
/// Read-only inspection of a page the shell may not be able to touch directly. The
/// in-process and out-of-process hosts both answer through these callbacks; nothing
/// here may block the caller, and every callback can be invoked with a failure value
/// when the page host dies mid-request.
/// </summary>
public interface IDevToolsChannel
{
    /// <summary>False when the page host cannot serve inspection at all.</summary>
    bool Supported { get; }

    /// <summary>Advances whenever the DOM mutates; consumers drop cached ids on change.</summary>
    long MutationVersion { get; }

    void RequestChildren(int nodeId, Action<DtNodeBatch> done);
    void RequestStyles(int nodeId, Action<DtStyleBatch?> done);
    void Evaluate(string script, Action<EvalResult> done);

    event Action<DtConsoleMessage>? ConsoleMessage;
    /// <summary>Raised when <see cref="MutationVersion"/> moves.</summary>
    event Action? TreeInvalidated;
}
