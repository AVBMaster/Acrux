using UpBrowser.Core.Css;
using UpBrowser.Core.Dom;
using UpBrowser.Core.JavaScript;
using UpBrowser.PageContract;

namespace UpBrowser.PageHost.Inspection;

/// <summary>
/// In-process <see cref="IDevToolsChannel"/> over live engine objects. Single-tab mode
/// (and the shell's self-validation) serves the same DTO code path as a page-host
/// child: the same <see cref="DevToolsInspector"/> builds the batches, the same
/// preorder-id/version rules apply — only the transport disappears. Requests answer
/// synchronously on the calling (UI) thread.
/// </summary>
public sealed class InProcessDevToolsChannel : IDevToolsChannel
{
    private readonly Document? _document;
    private readonly JavaScriptEngine? _engine;
    private readonly StyleComputer? _styles;
    private readonly DevToolsTree _tree = new();

    public InProcessDevToolsChannel(Document? document, JavaScriptEngine? engine, StyleComputer? styles)
    {
        _document = document;
        _engine = engine;
        _styles = styles;
    }

    public bool Supported => _document != null;

    private void EnsureTree(long currentVersion)
    {
        if (_document != null && !_tree.IsCurrent(_document, currentVersion))
            _tree.Rebuild(_document, currentVersion);
    }

    public void RequestChildren(int nodeId, long knownVersion, int maxDepth, int limit, Action<DtNodeBatch> done)
    {
        if (_document == null)
        {
            done(DtNodeBatch.Failed(DevToolsInspector.CurrentVersion));
            return;
        }
        long current = DevToolsInspector.CurrentVersion;
        if (knownVersion >= 0 && knownVersion != current)
        {
            done(DtNodeBatch.Moved(current));
            return;
        }
        EnsureTree(current);
        done(DevToolsInspector.CollectChunk(_tree, current, nodeId, maxDepth, limit));
    }

    public void RequestStyles(int nodeId, long knownVersion, Action<DtStyleBatch?> done)
    {
        if (_document == null)
        {
            done(null);
            return;
        }
        long current = DevToolsInspector.CurrentVersion;
        if (knownVersion >= 0 && knownVersion != current)
        {
            done(new DtStyleBatch(nodeId, current, Array.Empty<DtStyleRule>(),
                Array.Empty<DtDeclaration>(), Array.Empty<DtDeclaration>(), true));
            return;
        }
        EnsureTree(current);
        done(DevToolsInspector.BuildStyles(_tree, current, nodeId, _styles));
    }

    public void Evaluate(string script, Action<EvalResult> done)
    {
        if (_engine == null)
        {
            done(new EvalResult(true, "JS engine not available"));
            return;
        }
        try
        {
            done(new EvalResult(false, DevToolsInspector.FormatEvalValue(_engine.Evaluate(script))));
        }
        catch (Exception ex)
        {
            done(new EvalResult(true, ex.Message));
        }
    }
}
