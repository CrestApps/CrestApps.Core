using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class BoundedClientCacheTests
{
    [Fact]
    public void GetOrAdd_SameKey_ReturnsSameInstance()
    {
        var cache = new BoundedClientCache<TestClient>(capacity: 4);

        var first = cache.GetOrAdd("key1", _ => new TestClient("key1"));
        var second = cache.GetOrAdd("key1", _ => new TestClient("key1-other"));

        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void GetOrAdd_AtCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new BoundedClientCache<TestClient>(capacity: 2);

        var a = cache.GetOrAdd("a", k => new TestClient(k));
        var b = cache.GetOrAdd("b", k => new TestClient(k));

        // Touch "a" so "b" becomes least recently used.
        var aAgain = cache.GetOrAdd("a", _ => throw new InvalidOperationException("Should not recreate."));

        Assert.Same(a, aAgain);

        // Adding a third entry should evict "b".
        var c = cache.GetOrAdd("c", k => new TestClient(k));

        Assert.Equal(2, cache.Count);

        // "a" should still be present (recently used).
        var aStill = cache.GetOrAdd("a", _ => throw new InvalidOperationException("Should not recreate."));
        Assert.Same(a, aStill);

        // "b" should have been evicted; a new instance is created.
        var bRecreated = cache.GetOrAdd("b", k => new TestClient(k));
        Assert.NotSame(b, bRecreated);
        Assert.True(b.Disposed, "Evicted client should be disposed.");

        Assert.NotNull(c);
    }

    [Fact]
    public void Clear_RemovesAllEntries_AndDisposesClients()
    {
        var cache = new BoundedClientCache<TestClient>(capacity: 4);

        var a = cache.GetOrAdd("a", k => new TestClient(k));
        var b = cache.GetOrAdd("b", k => new TestClient(k));

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.True(a.Disposed);
        Assert.True(b.Disposed);

        // After clear, the same key produces a fresh instance.
        var aNew = cache.GetOrAdd("a", k => new TestClient(k));
        Assert.NotSame(a, aNew);
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedClientCache<TestClient>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedClientCache<TestClient>(-1));
    }

    [Fact]
    public void GetOrAdd_NullArguments_Throw()
    {
        var cache = new BoundedClientCache<TestClient>(capacity: 2);

        Assert.Throws<ArgumentNullException>(() => cache.GetOrAdd(null, _ => new TestClient("x")));
        Assert.Throws<ArgumentNullException>(() => cache.GetOrAdd("k", null));
    }

    [Fact]
    public void GetOrAdd_ConcurrentSameKey_ReturnsNonNullAndStaysConsistent()
    {
        var cache = new BoundedClientCache<TestClient>(capacity: 8);
        var results = new TestClient[16];

        Parallel.For(0, results.Length, i =>
        {
            results[i] = cache.GetOrAdd("shared", k => new TestClient(k));
        });

        Assert.All(results, r => Assert.NotNull(r));

        // After contention, the cache must still contain exactly one entry for the key
        // and all subsequent reads must return the surviving instance.
        Assert.Equal(1, cache.Count);

        var canonical = cache.GetOrAdd("shared", _ => throw new InvalidOperationException("Should not recreate."));

        Assert.All(results, r => Assert.Same(canonical, r));
    }

    private sealed class TestClient : IDisposable
    {
        public TestClient(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
