using System.Collections.Concurrent;

namespace Acrux.Process;

/// <summary>
/// A standing supply of page-host child processes that are already launched,
/// connected and have finished building their engine — the expensive part of
/// opening a tab (runtime start, JS realm bootstrap, first stylesheet parse)
/// happens while the user is looking at some other page, so a new tab adopts a
/// warm host and starts answering immediately. A spare is only enqueued once
/// the child has sent its Ready message, which the host emits after the engine
/// constructor returns; taking a host that never readied would only move the
/// stall, not hide it. The pool never lets more than one warm-up run be in
/// flight, so a burst of tab openings cannot spawn a burst of processes.
/// </summary>
internal sealed class TabHostPool : IDisposable
{
    private readonly ConcurrentQueue<RemoteTabProcess> _ready = new();
    private readonly float _dpi, _res;
    private int _spareSeq;
    private int _inFlight;      // 0 when no warm-up launch is running
    private volatile bool _closed;

    public TabHostPool(float dpiScale, float resolutionScale)
    {
        _dpi = dpiScale;
        _res = resolutionScale;
    }

    /// <summary>A host whose engine is already built, or null when none is standing by
    /// (a warm-up is started for the next tab in either case).</summary>
    public RemoteTabProcess? TryTake()
    {
        while (_ready.TryDequeue(out var p))
        {
            if (!p.IsDead && p.IsConnected)
            {
                Refill();
                return p;
            }
            try { p.Dispose(); } catch { }
        }
        Refill();
        return null;
    }

    /// <summary>Launch one spare if none is ready and none is being readied. Safe from any
    /// thread; the constructor itself only starts the child and its pipe, so the UI thread
    /// pays a process spawn, not the engine build.</summary>
    public void Refill()
    {
        if (_closed) return;
        if (!_ready.IsEmpty) return;
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
        if (_closed) { Interlocked.Exchange(ref _inFlight, 0); return; }

        RemoteTabProcess proc;
        try
        {
            // Negative indices are the pool's own namespace: pipe names carry pid+sequence
            // so they cannot collide with a real tab's, and no watchdog or DevTools channel
            // ever looks at them.
            proc = new RemoteTabProcess(int.MinValue / 4 - Interlocked.Increment(ref _spareSeq),
                "", _dpi, _res);
        }
        catch
        {
            Interlocked.Exchange(ref _inFlight, 0);
            return;
        }

        // Exactly one of Ready/Died may release the launch slot: whichever wins this gate
        // owns the release, so a death right after Ready cannot zero a newer launch's slot.
        int settled = 0;
        proc.OnSignal += (type, _, _) =>
        {
            if (type != TabMsg.Ready) return;
            if (Interlocked.CompareExchange(ref settled, 1, 0) != 0) return;
            Interlocked.Exchange(ref _inFlight, 0);
            if (_closed || proc.IsDead)
            {
                try { proc.Dispose(); } catch { }
                return;
            }
            _ready.Enqueue(proc);
        };
        proc.OnDied += (_, _) =>
        {
            // A spare that died before ever answering Ready must free the launch slot, or
            // the pool would never refill again; one that died after parking leaves it in
            // the queue, where TryTake drops it.
            if (Interlocked.CompareExchange(ref settled, 1, 0) != 0) return;
            Interlocked.Exchange(ref _inFlight, 0);
        };
    }

    public void Dispose()
    {
        _closed = true;
        while (_ready.TryDequeue(out var p))
            try { p.Dispose(); } catch { }
    }
}
