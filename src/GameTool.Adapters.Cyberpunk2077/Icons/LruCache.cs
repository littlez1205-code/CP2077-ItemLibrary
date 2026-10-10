namespace GameTool.Adapters.Cyberpunk2077.Icons;

/// <summary>线程安全的小容量 LRU：超出容量时丢掉最久未用的项。</summary>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    private readonly Lock _gate = new();

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>>(comparer);
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _map.Count;
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
                _order.Remove(existing);
            var node = new LinkedListNode<(TKey Key, TValue Value)>((key, value));
            _order.AddFirst(node);
            _map[key] = node;
            while (_map.Count > _capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }
}
