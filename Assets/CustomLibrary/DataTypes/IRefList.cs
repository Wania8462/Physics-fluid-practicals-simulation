using System.Collections.Generic;

/// <summary>A list that gives its elements by reference</summary>
public interface IRefList<T> : IEnumerable<T>, ICollection<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, IList<T>
{
    int Capacity { get; }

    new ref T this[int index] { get; }

    void Add();
    void AddRange(IEnumerable<T> collection);
    void AddRange(T[] array);

    ref T Last();

    void Fill(T value);
    void Fill(T value, int startIndex, int count);

    void RemoveLast();
    new void RemoveAt(int index);

    void EnsureCapacity(int capacity);
    new RefListEnumerator<T> GetEnumerator();
}
