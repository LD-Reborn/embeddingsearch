namespace Shared;

public sealed class EnumerableLruCache<TKey, TValue> where TKey : notnull
{
    private sealed record CacheItem(TKey Key, TValue Value);

    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _map;
    private readonly LinkedList<CacheItem> _lruList;
    private readonly ReaderWriterLockSlim _lock = new();

    private int _capacity;

    public EnumerableLruCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
        _lruList = new LinkedList<CacheItem>();
    }

    public int Capacity
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _capacity;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            _lock.EnterWriteLock();
            try
            {
                _capacity = value;
                TrimIfNeeded();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _map.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public TValue this[TKey key]
    {
        get
        {
            if (!TryGetValue(key, out var value))
                throw new KeyNotFoundException();

            return value!;
        }
        set => Set(key, value);
    }

    public bool TryGetValue(TKey key, out TValue? value)
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (!_map.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }

            value = node.Value.Value;

            // LRU aktualisieren
            _lock.EnterWriteLock();
            try
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            return true;
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    public void Set(TKey key, TValue value)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_map.TryGetValue(key, out var existing))
            {
                // Update + nach vorne
                existing.Value = existing.Value with { Value = value };
                _lruList.Remove(existing);
                _lruList.AddFirst(existing);
                return;
            }

            var item = new CacheItem(key, value);
            var node = new LinkedListNode<CacheItem>(item);

            _lruList.AddFirst(node);
            _map[key] = node;

            TrimIfNeeded();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool Remove(TKey key)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_map.TryGetValue(key, out var node))
                return false;

            _lruList.Remove(node);
            _map.Remove(key);
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool ContainsKey(TKey key)
    {
        _lock.EnterReadLock();
        try
        {
            return _map.ContainsKey(key);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Dictionary<TKey, TValue> AsDictionary()
    {
        _lock.EnterReadLock();
        try
        {
            return _map.Values.ToDictionary(
                n => n.Value.Key,
                n => n.Value.Value
            );
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IEnumerable<KeyValuePair<TKey, TValue>> Items()
    {
        _lock.EnterReadLock();
        try
        {
            foreach (var item in _lruList)
            {
                yield return new KeyValuePair<TKey, TValue>(item.Key, item.Value);
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        List<KeyValuePair<TKey, TValue>> snapshot;

        _lock.EnterReadLock();
        try
        {
            snapshot = new List<KeyValuePair<TKey, TValue>>(_map.Count);

            foreach (var item in _lruList)
            {
                snapshot.Add(new KeyValuePair<TKey, TValue>(
                    item.Key,
                    item.Value
                ));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return snapshot.GetEnumerator();
    }

    private void TrimIfNeeded()
    {
        while (_map.Count > _capacity)
        {
            var lruNode = _lruList.Last!;
            _lruList.RemoveLast();
            _map.Remove(lruNode.Value.Key);
        }
    }
}
