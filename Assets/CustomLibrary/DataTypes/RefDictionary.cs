using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>Hash map with a ref indexer. Don't hold a ref across Add/Remove</summary>
public class RefDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDictionary<TKey, TValue>
{
    private struct Entry
    {
        public TKey Key;
        public TValue Value;
        public int Hash;
    }

    private const int Empty = 0;
    private const int Tombstone = int.MinValue;
    private const float LoadFactor = 0.72f;

    private Entry[] _entries;
    private int _count;

    public int Count => _count;

    public RefDictionary(int capacity = 16)
    {
        _entries = new Entry[NextPow2(Math.Max(capacity, 4))];
    }

    /// <summary>Value by reference, so it can be changed in place. Throws if the key is missing</summary>
    public ref TValue this[TKey key]
    {
        get
        {
            int index = FindIndex(key);
            if (index < 0)
                throw new KeyNotFoundException($"Key not found: {key}");
            return ref _entries[index].Value;
        }
    }

    TValue IDictionary<TKey, TValue>.this[TKey key]
    {
        get
        {
            int index = FindIndex(key);
            if (index < 0)
                throw new KeyNotFoundException($"Key not found: {key}");
            return _entries[index].Value;
        }
        set
        {
            int index = FindIndex(key);
            if (index >= 0)
            {
                _entries[index].Value = value;
                return;
            }
            Add(key, value);
        }
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

    public ICollection<TKey> Keys => new KeyCollection(this);
    public ICollection<TValue> Values => new ValueCollection(this);

    public void Add(TKey key, TValue value)
    {
        if (_count >= _entries.Length * LoadFactor)
            Resize();

        int hash = GetHash(key);
        int mask = _entries.Length - 1;
        int index = hash & mask;
        int firstTombstone = -1;

        while (true)
        {
            ref var entry = ref _entries[index];

            if (entry.Hash == Empty)
            {
                int insertAt = firstTombstone >= 0 ? firstTombstone : index;
                ref var slot = ref _entries[insertAt];
                slot.Key = key;
                slot.Value = value;
                slot.Hash = hash;
                _count++;
                return;
            }

            if (entry.Hash == Tombstone)
            {
                if (firstTombstone < 0) firstTombstone = index;
            }
            else if (entry.Hash == hash && EqualityComparer<TKey>.Default.Equals(entry.Key, key))
            {
                throw new ArgumentException($"Key already exists: {key}");
            }

            index = (index + 1) & mask;
        }
    }

    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    /// <summary>Removes the key. Throws if it is missing</summary>
    public void Remove(TKey key)
    {
        int index = FindIndex(key);
        if (index < 0)
            throw new KeyNotFoundException();

        ref var entry = ref _entries[index];
        entry.Hash = Tombstone;
        entry.Key = default!;
        entry.Value = default!;
        _count--;
    }

    bool IDictionary<TKey, TValue>.Remove(TKey key) => TryRemove(key);

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
    {
        int index = FindIndex(item.Key);
        if (index < 0 || !EqualityComparer<TValue>.Default.Equals(_entries[index].Value, item.Value))
            return false;

        ref var entry = ref _entries[index];
        entry.Hash = Tombstone;
        entry.Key = default!;
        entry.Value = default!;
        _count--;
        return true;
    }

    /// <summary>Removes the key if it is there</summary>
    public bool TryRemove(TKey key)
    {
        int index = FindIndex(key);
        if (index < 0) return false;

        ref var entry = ref _entries[index];
        entry.Hash = Tombstone;
        entry.Key = default!;
        entry.Value = default!;
        _count--;
        return true;
    }

    public bool ContainsKey(TKey key) => FindIndex(key) >= 0;

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
    {
        int index = FindIndex(item.Key);
        return index >= 0 && EqualityComparer<TValue>.Default.Equals(_entries[index].Value, item.Value);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        int index = FindIndex(key);
        if (index < 0)
        {
            value = default!;
            return false;
        }
        value = _entries[index].Value;
        return true;
    }

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        int i = arrayIndex;
        foreach (var entry in _entries)
            if (entry.Hash != Empty && entry.Hash != Tombstone)
                array[i++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
    }

    /// <summary>Value by reference, adding a default one if the key is missing</summary>
    public ref TValue GetOrAdd(TKey key)
    {
        if (_count >= _entries.Length * LoadFactor)
            Resize();

        int hash = GetHash(key);
        int mask = _entries.Length - 1;
        int index = hash & mask;
        int firstTombstone = -1;

        while (true)
        {
            ref var entry = ref _entries[index];

            if (entry.Hash == Empty)
            {
                int insertAt = firstTombstone >= 0 ? firstTombstone : index;
                ref var slot = ref _entries[insertAt];
                slot.Key = key;
                slot.Value = default!;
                slot.Hash = hash;
                _count++;
                return ref slot.Value;
            }

            if (entry.Hash == Tombstone)
            {
                if (firstTombstone < 0) firstTombstone = index;
            }
            else if (entry.Hash == hash && EqualityComparer<TKey>.Default.Equals(entry.Key, key))
            {
                return ref entry.Value;
            }

            index = (index + 1) & mask;
        }
    }

    public void Clear()
    {
        Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var entry in _entries)
            if (entry.Hash != Empty && entry.Hash != Tombstone)
                yield return new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int FindIndex(TKey key)
    {
        int hash = GetHash(key);
        int mask = _entries.Length - 1;
        int index = hash & mask;

        while (true)
        {
            ref var entry = ref _entries[index];
            if (entry.Hash == Empty) return -1;
            if (entry.Hash == hash && entry.Hash != Tombstone &&
                EqualityComparer<TKey>.Default.Equals(entry.Key, key))
                return index;
            index = (index + 1) & mask;
        }
    }

    private int GetHash(TKey key)
    {
        int h = EqualityComparer<TKey>.Default.GetHashCode(key!);
        // Mixes the hash and keeps it off the reserved Empty and Tombstone values
        h ^= h >> 16;
        if (h == Empty) h = 1;
        if (h == Tombstone) h = Tombstone + 1;
        return h;
    }

    private void Resize()
    {
        var old = _entries;
        _entries = new Entry[old.Length * 2];
        _count = 0;
        foreach (var entry in old)
            if (entry.Hash != Empty && entry.Hash != Tombstone)
                Add(entry.Key, entry.Value);
    }

    private static int NextPow2(int n)
    {
        n--;
        n |= n >> 1; n |= n >> 2; n |= n >> 4; n |= n >> 8; n |= n >> 16;
        return n + 1;
    }

    private sealed class KeyCollection : ICollection<TKey>
    {
        private readonly RefDictionary<TKey, TValue> _dict;
        public KeyCollection(RefDictionary<TKey, TValue> dict) => _dict = dict;

        public int Count => _dict._count;
        public bool IsReadOnly => true;
        public bool Contains(TKey item) => _dict.ContainsKey(item);

        public void CopyTo(TKey[] array, int arrayIndex)
        {
            int i = arrayIndex;
            foreach (var entry in _dict._entries)
                if (entry.Hash != Empty && entry.Hash != Tombstone)
                    array[i++] = entry.Key;
        }

        public IEnumerator<TKey> GetEnumerator()
        {
            foreach (var entry in _dict._entries)
                if (entry.Hash != Empty && entry.Hash != Tombstone)
                    yield return entry.Key;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(TKey item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Remove(TKey item) => throw new NotSupportedException();
    }

    private sealed class ValueCollection : ICollection<TValue>
    {
        private readonly RefDictionary<TKey, TValue> _dict;
        public ValueCollection(RefDictionary<TKey, TValue> dict) => _dict = dict;

        public int Count => _dict._count;
        public bool IsReadOnly => true;

        public bool Contains(TValue item)
        {
            foreach (var entry in _dict._entries)
                if (entry.Hash != Empty && entry.Hash != Tombstone &&
                    EqualityComparer<TValue>.Default.Equals(entry.Value, item))
                    return true;
            return false;
        }

        public void CopyTo(TValue[] array, int arrayIndex)
        {
            int i = arrayIndex;
            foreach (var entry in _dict._entries)
                if (entry.Hash != Empty && entry.Hash != Tombstone)
                    array[i++] = entry.Value;
        }

        public IEnumerator<TValue> GetEnumerator()
        {
            foreach (var entry in _dict._entries)
                if (entry.Hash != Empty && entry.Hash != Tombstone)
                    yield return entry.Value;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(TValue item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Remove(TValue item) => throw new NotSupportedException();
    }
}
