using System.Collections.Concurrent;

namespace Acrux.Core.Performance.Memory;

/// <summary>
/// Generic, thread-safe object pool with an upper bound. Objects are reset via
/// <see cref="IResettable.Reset"/> on return when they implement the optional contract.
/// Designed to reduce GC pressure on hot paths (LayoutBox, InlineRun, paint records).
/// </summary>
public sealed class ObjectPool<T> where T : class
{
    private readonly ConcurrentStack<T> _free = new();
    private readonly Func<T> _factory;
    private readonly Action<T>? _onReturn;
    private readonly int _maxIdle;
    private int _totalCount;
    private long _allocations;
    private long _rentals;
    private long _returns;

    public ObjectPool(Func<T> factory, int maxIdle = 256, Action<T>? onReturn = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _maxIdle = Math.Max(0, maxIdle);
        _onReturn = onReturn;
    }

    public int FreeCount => _free.Count;
    public int TotalCount => Volatile.Read(ref _totalCount);
    public long Allocations => Interlocked.Read(ref _allocations);
    public long Rentals => Interlocked.Read(ref _rentals);
    public long Returns => Interlocked.Read(ref _returns);

    public T Rent()
    {
        Interlocked.Increment(ref _rentals);
        if (_free.TryPop(out var obj)) return obj;
        Interlocked.Increment(ref _allocations);
        Interlocked.Increment(ref _totalCount);
        return _factory();
    }

    public void Return(T obj)
    {
        if (obj is null) return;
        Interlocked.Increment(ref _returns);
        if (_onReturn is not null) _onReturn(obj);
        else if (obj is IResettable r) r.Reset();
        if (_free.Count < _maxIdle) _free.Push(obj);
        else Interlocked.Decrement(ref _totalCount);
    }

    /// <summary>Drop all idle instances. Useful at idle time to release memory.</summary>
    public void Trim()
    {
        while (_free.TryPop(out _)) { }
    }
}

/// <summary>Optional contract for resetting an object to a reusable state on pool return.</summary>
public interface IResettable
{
    void Reset();
}
