using Acrux.PageContract;
using Acrux.Rendering.DevTools;

namespace Acrux.Process;

/// <summary>
/// Shell-side <see cref="IDevToolsChannel"/> for a page living in a tab-host child.
/// Same interface and same DTO semantics as the in-process channel — only the pipe is
/// new. Callbacks arrive from the pipe reader thread; this class marshals every one of
/// them through the shell's event loop, so the DevTools panel only ever mutates on the
/// UI thread.
/// </summary>
internal sealed class RemoteDevToolsChannel : IDevToolsChannel
{
    private readonly RemoteTabProcess _proc;
    private readonly Action<Action> _marshal;

    public RemoteDevToolsChannel(RemoteTabProcess proc, Action<Action> marshal)
    {
        _proc = proc;
        _marshal = marshal;
    }

    public bool Supported => _proc.IsConnected && !_proc.IsDead;

    public void RequestChildren(int nodeId, long knownVersion, int maxDepth, int limit, Action<DtNodeBatch> done)
    {
        if (!Supported)
        {
            done(DtNodeBatch.Failed(0));
            return;
        }
        _proc.RequestNodeChunk(knownVersion, nodeId, maxDepth, limit,
            batch => _marshal(() => done(batch)));
    }

    public void RequestStyles(int nodeId, long knownVersion, Action<DtStyleBatch?> done)
    {
        if (!Supported)
        {
            done(null);
            return;
        }
        _proc.RequestNodeStyles(knownVersion, nodeId, batch => _marshal(() => done(batch)));
    }

    public void Evaluate(string script, Action<EvalResult> done)
    {
        if (!Supported)
        {
            done(new EvalResult(true, "JS engine not available"));
            return;
        }
        _proc.RequestEval(script, result => _marshal(() => done(result)));
    }
}
