using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Acrux.Core.JavaScript;

public class ObjectIdentityMap : IDisposable
{
    private readonly ConcurrentDictionary<IntPtr, WeakReference<object>> _map = new();
    private readonly ConcurrentDictionary<object, IntPtr> _reverseMap = new();
    private ConditionalWeakTable<object, object> _objectKeepAlive = new();
    private readonly object _lock = new();
    private bool _disposed;
    private long _nextHandle;

    public int Count => _map.Count;

    public IntPtr GetOrCreateHandle(object obj, Func<object> wrapperFactory)
    {
        if (_disposed) return IntPtr.Zero;

        lock (_lock)
        {
            if (_reverseMap.TryGetValue(obj, out var existing))
            {
                if (_map.TryGetValue(existing, out var wr) && wr.TryGetTarget(out _))
                    return existing;
                _map.TryRemove(existing, out _);
                _reverseMap.TryRemove(obj, out _);
            }

            var handle = (IntPtr)Interlocked.Increment(ref _nextHandle);

            var wrapper = wrapperFactory();
            _map[handle] = new WeakReference<object>(wrapper);
            _reverseMap[obj] = handle;

            _objectKeepAlive.Add(obj, wrapper);

            return handle;
        }
    }

    public object? GetWrapper(IntPtr handle)
    {
        if (_disposed || handle == IntPtr.Zero) return null;
        if (_map.TryGetValue(handle, out var wr) && wr.TryGetTarget(out var wrapper))
            return wrapper;

        _map.TryRemove(handle, out _);
        return null;
    }

    public T? GetWrapper<T>(IntPtr handle) where T : class
    {
        return GetWrapper(handle) as T;
    }

    public bool HasWrapper(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        return _map.TryGetValue(handle, out var wr) && wr.TryGetTarget(out _);
    }

    public IntPtr FindHandle(object wrapper)
    {
        foreach (var kvp in _map)
        {
            if (kvp.Value.TryGetTarget(out var target) && ReferenceEquals(target, wrapper))
                return kvp.Key;
        }
        return IntPtr.Zero;
    }

    public void Remove(IntPtr handle)
    {
        if (_disposed || handle == IntPtr.Zero) return;
        if (_map.TryRemove(handle, out var wr))
        {
            if (wr.TryGetTarget(out var wrapper))
            {
                var toRemove = _reverseMap.Where(kvp => kvp.Value == handle).ToList();
                foreach (var kvp in toRemove)
                    _reverseMap.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void RemoveByObject(object obj)
    {
        if (_disposed || obj == null) return;
        if (_reverseMap.TryRemove(obj, out var handle))
            _map.TryRemove(handle, out _);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _reverseMap.Clear();
            // ConditionalWeakTable has no Clear(); recreate it to release old entries
            _objectKeepAlive = new ConditionalWeakTable<object, object>();
        }
    }

    public void CleanupStaleEntries()
    {
        var stale = new List<IntPtr>();
        foreach (var kvp in _map)
        {
            if (!kvp.Value.TryGetTarget(out _))
                stale.Add(kvp.Key);
        }
        foreach (var handle in stale)
        {
            _map.TryRemove(handle, out _);
            var toRemove = _reverseMap.Where(kvp => kvp.Value == handle).ToList();
            foreach (var kvp in toRemove)
                _reverseMap.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _map.Clear();
        _reverseMap.Clear();
        GC.SuppressFinalize(this);
    }
}
