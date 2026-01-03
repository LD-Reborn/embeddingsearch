namespace Shared;


public class EnumerableLruCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _cache;
    private readonly LinkedList<TKey> _keys = new();
    private int _capacity;
    private ReaderWriterLockSlim _readerWriterLock;

    public int Capacity
    {
        get
        {
            _readerWriterLock.EnterReadLock();
            try
            {
                return _capacity;
            }
            finally
            {
                _readerWriterLock.ExitReadLock();
            }
        }
        set
        {
            _readerWriterLock.EnterWriteLock();
            try
            {
                _capacity = value;
                
                // Trim cache if new capacity is smaller than current size
                while (_keys.Count > _capacity)
                {
                    TKey last = _keys.Last!.Value;
                    _keys.RemoveLast();
                    _cache.Remove(last);
                }
            }
            finally
            {
                _readerWriterLock.ExitWriteLock();
            }
        }
    }

    public EnumerableLruCache(int capacity)
    {
        _readerWriterLock = new();
        _capacity = capacity;
        _cache = [];
    }

    public TValue this[TKey key]
    {
        get
        {
            _readerWriterLock.EnterReadLock();
            try
            {
                return _cache[key];
            }
            finally
            {
                _readerWriterLock.ExitReadLock();
            }
        }
        set
        {
            _readerWriterLock.EnterWriteLock();
            try
            {
                if (_cache.TryGetValue(key, out _))
                {
                    _keys.Remove(key);
                }
                _keys.AddFirst(key);
                _cache[key] = value;

                if (_keys.Count > _capacity)
                {
                    TKey last = _keys.Last!.Value;
                    _keys.RemoveLast();
                    _cache.Remove(last);
                }
            }
            finally
            {
                _readerWriterLock.ExitWriteLock();
            }
        }
    }

    public bool ContainsKey(TKey key)
    {
        _readerWriterLock.EnterReadLock();
        try
        {
            return _cache.ContainsKey(key);
        } finally
        {
            _readerWriterLock.ExitReadLock();
        }
    }

    public int Count()
    {
        _readerWriterLock.EnterReadLock();
        try
        {
            return _cache.Count;
        } finally
        {
            _readerWriterLock.ExitReadLock();
        }
    }

    public void Set(TKey key, TValue value)
    {
        _readerWriterLock.EnterWriteLock();
        try
        {
            if (_cache.TryGetValue(key, out _))
            {
                _keys.Remove(key);
            }
            _keys.AddFirst(key);
            _cache[key] = value;

            if (_keys.Count > _capacity)
            {
                TKey? last = _keys.Last();
                _keys.RemoveLast();
                _cache.Remove(last);
            }            
        } finally
        {
            _readerWriterLock.ExitWriteLock();
        }
    }

    public void Remove(TKey key)
    {
        _readerWriterLock.EnterWriteLock();
        try
        {
            _keys.Remove(key);
            _cache.Remove(key);            
        } finally
        {
            _readerWriterLock.ExitWriteLock();
        }
    }

    public bool TryGetValue(TKey key, out TValue? value)
    {
        _readerWriterLock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out value))
            {
                return false;
            }
            _readerWriterLock.EnterWriteLock();
            try
            {
                _keys.Remove(key);
                _keys.AddFirst(key);
            } finally
            {
                _readerWriterLock.ExitWriteLock();
            }
            return true;
        } finally
        {
            _readerWriterLock.ExitUpgradeableReadLock();
        }
    }

    public Dictionary<TKey, TValue> AsDictionary()
    {
        _readerWriterLock.EnterReadLock();
        try
        {
            return new Dictionary<TKey, TValue>(_cache);
        }
        finally
        {
            _readerWriterLock.ExitReadLock();
        }
    }

    public IEnumerable<KeyValuePair<TKey, TValue>> Items()
    {
        _readerWriterLock.EnterReadLock();
        try
        {
            foreach (var key in _keys)
            {
                if (_cache.TryGetValue(key, out var value))
                {
                    yield return new KeyValuePair<TKey, TValue>(key, value);
                }
            }
        } finally
        {
            _readerWriterLock.ExitReadLock();
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return Items().GetEnumerator();
    }
}
