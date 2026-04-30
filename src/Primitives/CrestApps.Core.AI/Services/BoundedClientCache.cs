namespace CrestApps.Core.AI.Services;

/// <summary>
/// A thread-safe, bounded least-recently-used (LRU) cache used by AI provider
/// client factories to avoid unbounded growth of long-lived SDK client instances
/// keyed by connection fingerprint.
/// </summary>
/// <typeparam name="TClient">The cached client type.</typeparam>
internal sealed class BoundedClientCache<TClient> where TClient : class
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _order = new();
    private readonly Lock _syncLock = new();

    public BoundedClientCache(int capacity = 64)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be greater than zero.");
        }

        _capacity = capacity;
        _map = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
    }

    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                return _map.Count;
            }
        }
    }

    public TClient GetOrAdd(string key, Func<string, TClient> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_syncLock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _order.AddLast(existing);

                return existing.Value.Client;
            }
        }

        var created = factory(key);

        lock (_syncLock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _order.AddLast(existing);
                DisposeIfNeeded(created);

                return existing.Value.Client;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, created));
            _map[key] = node;
            _order.AddLast(node);

            while (_map.Count > _capacity)
            {
                var first = _order.First;
                if (first is null)
                {
                    break;
                }

                _order.RemoveFirst();
                _map.Remove(first.Value.Key);
                DisposeIfNeeded(first.Value.Client);
            }

            return created;
        }
    }

    public void Clear()
    {
        TClient[] toDispose;

        lock (_syncLock)
        {
            toDispose = new TClient[_map.Count];
            var i = 0;
            foreach (var node in _map.Values)
            {
                toDispose[i++] = node.Value.Client;
            }

            _map.Clear();
            _order.Clear();
        }

        foreach (var client in toDispose)
        {
            DisposeIfNeeded(client);
        }
    }

    private static void DisposeIfNeeded(TClient client)
    {
        if (client is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Best-effort disposal; swallow to avoid disrupting cache eviction.
            }
        }
    }

    private readonly struct Entry
    {
        public Entry(string key, TClient client)
        {
            Key = key;
            Client = client;
        }

        public string Key { get; }

        public TClient Client { get; }
    }
}
