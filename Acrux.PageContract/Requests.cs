namespace Acrux.PageContract;

/// <summary>
/// What the engine says lives at a viewport point — enough for the shell to build a
/// context menu without ever walking the DOM itself.
/// </summary>
public sealed record NodeHitInfo
{
    public bool Hit { get; init; }
    public string TagName { get; init; } = "";
    public string Href { get; init; } = "";
    public string Src { get; init; } = "";
    public string InputType { get; init; } = "";
    public bool IsEditable { get; init; }
    public bool IsLink { get; init; }
    public bool HasSelection { get; init; }
    public string SelectionText { get; init; } = "";
    /// <summary>Caret offset inside <see cref="SelectionText"/> for a collapsed selection.</summary>
    public int TextCaret { get; init; }
}

/// <summary>Screen position of the text caret, so the shell can park an IME window there.</summary>
public sealed record CaretRect(float X, float Y, float Height, bool Visible);

public sealed record FindOptions(string Query)
{
    public bool CaseSensitive { get; init; }
    public bool Forward { get; init; } = true;
}

public sealed record FindResult
{
    public int TotalCount { get; init; }
    /// <summary>Index within <see cref="Rects"/> of the match the engine scrolled into view.</summary>
    public int ActiveIndex { get; init; } = -1;
    public IReadOnlyList<PageRect> Rects { get; init; } = Array.Empty<PageRect>();
}

public enum DialogKind
{
    Alert,
    Confirm,
    Prompt,
}

/// <summary>
/// A page-initiated modal. The engine blocks its script thread until
/// <see cref="Complete"/> runs, so the shell must answer from its frame loop rather
/// than assume the request can wait.
/// </summary>
public sealed class DialogRequest
{
    private readonly Action<DialogResult> _resume;

    internal DialogRequest(int requestId, DialogKind kind, string message, string defaultValue, Action<DialogResult> resume)
    {
        RequestId = requestId;
        Kind = kind;
        Message = message;
        DefaultValue = defaultValue;
        _resume = resume;
    }

    public int RequestId { get; }
    public DialogKind Kind { get; }
    public string Message { get; }
    /// <summary>Prompt pre-fill text.</summary>
    public string DefaultValue { get; }

    public void Complete(bool accepted, string? text = null) => _resume(new DialogResult(accepted, text));
}

/// <summary>Result handed back to a blocked script. <see cref="Text"/> is null for a dismissed prompt.</summary>
public readonly record struct DialogResult(bool Accepted, string? Text);

/// <summary>Outcome of a DevTools console evaluation.</summary>
public sealed record EvalResult(bool IsError, string Text);
