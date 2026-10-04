using Acrux.Core.Performance.Memory;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class ObjectPoolTests
{
    private sealed class Poolable : IResettable
    {
        public int Value;
        public bool ResetCalled;
        public void Reset() { ResetCalled = true; Value = 0; }
    }

    [Fact]
    public void Rent_Allocates_OnFirstCall()
    {
        var pool = new ObjectPool<Poolable>(() => new Poolable());
        var obj = pool.Rent();
        Assert.NotNull(obj);
        Assert.Equal(1, pool.Allocations);
    }

    [Fact]
    public void Return_ThenRent_ReusesInstance()
    {
        var pool = new ObjectPool<Poolable>(() => new Poolable());
        var a = pool.Rent();
        a.Value = 42;
        pool.Return(a);
        var b = pool.Rent();
        Assert.Same(a, b);
        Assert.True(b.ResetCalled);
    }

    [Fact]
    public void Returns_ExceedingMaxIdle_AreDropped()
    {
        var pool = new ObjectPool<Poolable>(() => new Poolable(), maxIdle: 2);
        var a = pool.Rent(); var b = pool.Rent(); var c = pool.Rent();
        pool.Return(a); pool.Return(b); pool.Return(c);
        Assert.Equal(2, pool.FreeCount);
    }

    [Fact]
    public void Trim_DropsAllIdleInstances()
    {
        var pool = new ObjectPool<Poolable>(() => new Poolable());
        pool.Return(pool.Rent());
        pool.Return(pool.Rent());
        pool.Trim();
        Assert.Equal(0, pool.FreeCount);
    }

    [Fact]
    public void PoolIsThreadSafe()
    {
        var pool = new ObjectPool<Poolable>(() => new Poolable(), maxIdle: 1024);
        Parallel.For(0, 1000, _ =>
        {
            var obj = pool.Rent();
            obj.Value = 1;
            pool.Return(obj);
        });
        Assert.Equal(1000, pool.Rentals);
        Assert.Equal(1000, pool.Returns);
    }
}
