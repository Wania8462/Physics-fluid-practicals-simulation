using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// List with a ref indexer. Don't hold a ref across Add/Remove.
/// Remove swaps the last element in, so order isn't kept
/// </summary>
public class RefList<T> : IRefList<T>
{
    private T[] _items;
    private int _size;

    public int Count => _size;
    public int Capacity => _items.Length;

    private const int defaultSize = 4;

    /// <summary>Starts with size default elements, not just room for them</summary>
    public RefList(int size = 0)
    {
        if (size == 0)
            _items = new T[defaultSize];

        else
            _items = new T[size];

        _size = size;
    }

    /// <summary>Element by reference, so it can be changed in place</summary>
    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_size)
                throw new IndexOutOfRangeException();

            return ref _items[index];
        }
    }

    T IReadOnlyList<T>.this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_size)
                throw new IndexOutOfRangeException();
            return _items[index];
        }
    }

    T IList<T>.this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_size)
                throw new IndexOutOfRangeException();
            return _items[index];
        }
        set
        {
            if ((uint)index >= (uint)_size)
                throw new IndexOutOfRangeException();
            _items[index] = value;
        }
    }

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Adds a default element, to fill in through the indexer or Last()</summary>
    public void Add()
    {
        if (_size == _items.Length)
            Resize(_items.Length * 2);

        _size++;
    }

    public void Add(T item)
    {
        if (_size == _items.Length)
            Resize(_items.Length * 2);

        _items[_size++] = item;
    }

    public void AddRange(IEnumerable<T> collection)
    {
        if (collection is ICollection<T> c)
        {
            EnsureCapacity(_size + c.Count);
            c.CopyTo(_items, _size);
            _size += c.Count;
            return;
        }

        foreach (T item in collection)
            Add(item);
    }

    public void AddRange(T[] array)
    {
        int count = array.Length;
        EnsureCapacity(_size + count);
        Array.Copy(array, 0, _items, _size, count);
        _size += count;
    }

    /// <summary>The last element by reference</summary>
    public ref T Last()
    {
        if (_size == 0)
            throw new InvalidOperationException();

        return ref _items[_size - 1];
    }

    public bool Contains(T item)
    {
        for (int i = 0; i < _size; i++)
            if (EqualityComparer<T>.Default.Equals(_items[i], item))
                return true;

        return false;
    }

    public int IndexOf(T item)
    {
        for (int i = 0; i < _size; i++)
            if (EqualityComparer<T>.Default.Equals(_items[i], item))
                return i;

        return -1;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        Array.Copy(_items, 0, array, arrayIndex, _size);
    }

    /// <summary>Removes the first match by moving the last element into its place</summary>
    public bool Remove(T item)
    {
        for (int i = 0; i < _size; i++)
        {
            if (EqualityComparer<T>.Default.Equals(_items[i], item))
            {
                _size--;
                _items[i] = _items[_size];
                _items[_size] = default!;
                return true;
            }
        }
        return false;
    }

    public void RemoveLast()
    {
        if (_size == 0)
            throw new InvalidOperationException();

        _size--;
        _items[_size] = default!;
    }

    /// <summary>Removes an element by moving the last element into its place</summary>
    public void RemoveAt(int index)
    {
        if (index >= _size)
            throw new IndexOutOfRangeException();

        _size--;
        _items[index] = _items[_size];
        _items[_size] = default!;
    }

    void IList<T>.Insert(int index, T item)
    {
        if ((uint)index > (uint)_size)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (_size == _items.Length)
            Resize(_items.Length * 2);

        if (index < _size)
            Array.Copy(_items, index, _items, index + 1, _size - index);

        _items[index] = item;
        _size++;
    }

    void IList<T>.RemoveAt(int index)
    {
        if ((uint)index >= (uint)_size)
            throw new ArgumentOutOfRangeException(nameof(index));

        _size--;
        if (index < _size)
            Array.Copy(_items, index + 1, _items, index, _size - index);
        _items[_size] = default!;
    }

    public void ForEach(Action<T> action)
    {
        for (int i = 0; i < _size; i++)
            action(_items[i]);
    }

    /// <summary>A new array with <paramref name="transform"/> applied to each element</summary>
    /// <remarks>Allocates every call, avoid in hot paths or per-frame loops.</remarks>
    public T[] ForEach(Func<T, T> transform)
    {
        T[] copy = new T[_size];
        for (int i = 0; i < _size; i++)
            copy[i] = transform(_items[i]);
        return copy;
    }

    /// <summary>A new array of each element passed through <paramref name="transform"/></summary>
    /// <remarks>Allocates every call, avoid in hot paths or per-frame loops.</remarks>
    public TResult[] ForEach<TResult>(Func<T, TResult> transform)
    {
        TResult[] result = new TResult[_size];
        for (int i = 0; i < _size; i++)
            result[i] = transform(_items[i]);
        return result;
    }

    /// <summary>Sets every element to value</summary>
    public void Fill(T value)
    {
        Array.Fill(_items, value, 0, _size);
    }

    /// <summary>Sets count elements from startIndex to value</summary>
    public void Fill(T value, int startIndex, int count)
    {
        Array.Fill(_items, value, startIndex, count);
    }

    public void Clear()
    {
        Array.Clear(_items, 0, _size);
        _size = 0;
    }

    public Span<T> AsSpan() => _items.AsSpan(0, _size);

    public Span<T> AsSpan(int start) => _items.AsSpan(start, _size - start);

    public Span<T> AsSpan(int start, int length) => _items.AsSpan(start, length);

    public void EnsureCapacity(int capacity)
    {
        if (capacity > _items.Length)
            Resize(capacity);
    }

    public T[] ToArray()
    {
        T[] returnArr = new T[_size];
        Array.Copy(_items, returnArr, _size);
        return returnArr;
    }

    public List<T> ToList()
    {
        List<T> list = new(_size);
        for (int i = 0; i < _size; i++)
            list.Add(_items[i]);
        return list;
    }

    /// <summary>Enumerator that doesn't allocate</summary>
    public RefListEnumerator<T> GetEnumerator() => new(_items, _size);

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        for (int i = 0; i < _size; i++)
            yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    private void Resize(int newCapacity)
    {
        T[] newArr = new T[newCapacity];
        Array.Copy(_items, newArr, _size);
        _items = newArr;
    }
}

public struct RefListEnumerator<T>
{
    private readonly T[] _items;
    private readonly int _size;
    private int _index;

    internal RefListEnumerator(T[] items, int size)
    {
        _items = items;
        _size = size;
        _index = -1;
    }

    public bool MoveNext()
    {
        _index++;
        return _index < _size;
    }

    public readonly T Current => _items[_index];
}