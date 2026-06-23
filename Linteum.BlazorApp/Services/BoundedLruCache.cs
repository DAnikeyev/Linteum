namespace Linteum.BlazorApp.Services;

/// <summary>
/// A simple bounded LRU cache with a uniform per-entry TTL. NOT thread-safe — callers must
/// synchronize access (MyApiClient holds every read/write under a single lock). Expired entries
/// are evicted lazily on access (reported as a miss); once <see cref="Capacity"/> is exceeded the
/// least-recently-used entry is evicted. This bounds client memory for the pixel/history caches
/// that previously grew without limit (P-PERF-06) while keeping the cache effective (TTL + LRU).
/// </summary>
internal sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly LinkedList<Node> _order = new();
    private readonly Dictionary<TKey, LinkedListNode<Node>> _lookup = new();

    private readonly struct Node
    {
        public Node(TKey key, TValue value, DateTime expiry)
        {
            Key = key;
            Value = value;
            Expiry = expiry;
        }

        public TKey Key { get; }
        public TValue Value { get; }
        public DateTime Expiry { get; }
    }

    public BoundedLruCache(int capacity, TimeSpan ttl)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        _capacity = capacity;
        _ttl = ttl;
    }

    public int Capacity => _capacity;

    public int Count => _lookup.Count;

    /// <summary>Snapshot of the current keys (copy before mutating during iteration).</summary>
    public IEnumerable<TKey> Keys => _lookup.Keys;

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_lookup.TryGetValue(key, out var node))
        {
            var entry = node.Value;
            if (entry.Expiry > DateTime.UtcNow)
            {
                Touch(node);
                value = entry.Value;
                return true;
            }

            // Expired — evict lazily so stale entries do not accumulate.
            RemoveNode(node);
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        var expiry = DateTime.UtcNow.Add(_ttl);
        if (_lookup.TryGetValue(key, out var node))
        {
            node.Value = new Node(key, value, expiry);
            Touch(node);
            return;
        }

        var newNode = _order.AddFirst(new Node(key, value, expiry));
        _lookup[key] = newNode;

        while (_lookup.Count > _capacity)
        {
            RemoveNode(_order.Last!);
        }
    }

    public bool Remove(TKey key)
    {
        if (_lookup.TryGetValue(key, out var node))
        {
            RemoveNode(node);
            return true;
        }

        return false;
    }

    public void Clear()
    {
        _order.Clear();
        _lookup.Clear();
    }

    private void Touch(LinkedListNode<Node> node)
    {
        if (_order.First == node)
        {
            return;
        }

        _order.Remove(node);
        _order.AddFirst(node);
    }

    private void RemoveNode(LinkedListNode<Node> node)
    {
        _order.Remove(node);
        _lookup.Remove(node.Value.Key);
    }
}
