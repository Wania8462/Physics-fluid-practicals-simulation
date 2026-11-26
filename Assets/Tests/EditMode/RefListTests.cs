using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>RefList: a growable list that hands out references to its items</summary>
public class RefListTests
{
    /// <summary>A list holding 1, 2, 3</summary>
    private static RefList<int> OneTwoThree()
    {
        RefList<int> list = new();
        list.AddRange(new[] { 1, 2, 3 });
        return list;
    }

    #region Constructor
    /// <summary>The default list is empty with room for 4</summary>
    [Test]
    public void Constructor_Default_EmptyWithCapacityFour()
    {
        RefList<int> list = new();
        Assert.AreEqual(0, list.Count);
        Assert.AreEqual(4, list.Capacity);
    }

    /// <summary>A size fills the list with that many default items</summary>
    [Test]
    public void Constructor_WithSize_FillsWithDefaults()
    {
        RefList<int> list = new(5);
        Assert.AreEqual(5, list.Count);
        Assert.AreEqual(5, list.Capacity);
        Assert.AreEqual(0, list[4]);
    }

    /// <summary>A negative size can't make an array</summary>
    [Test]
    public void Constructor_NegativeSize_Throws()
    {
        Assert.Catch(() => new RefList<int>(-1));
    }
    #endregion

    #region Adding
    /// <summary>Adding past the capacity doubles it and keeps the items</summary>
    [Test]
    public void Add_PastCapacity_DoublesAndKeepsItems()
    {
        RefList<int> list = new();
        for (int i = 0; i < 5; i++)
            list.Add(i);

        Assert.AreEqual(5, list.Count);
        Assert.AreEqual(8, list.Capacity);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, list.ToArray());
    }

    /// <summary>Add with no item adds a default one</summary>
    [Test]
    public void Add_NoItem_AddsDefault()
    {
        RefList<string> list = new();
        list.Add();
        Assert.AreEqual(1, list.Count);
        Assert.IsNull(list[0]);
    }

    /// <summary>Adding many items keeps every one in order</summary>
    [Test]
    public void Add_TenThousandItems_AllKept()
    {
        RefList<int> list = new();
        for (int i = 0; i < 10000; i++)
            list.Add(i);

        Assert.AreEqual(10000, list.Count);
        Assert.AreEqual(9999, list[9999]);
    }

    /// <summary>AddRange takes arrays and other collections</summary>
    [Test]
    public void AddRange_ArrayAndList_AppendsInOrder()
    {
        RefList<int> list = new();
        list.AddRange(new[] { 1, 2 });
        list.AddRange(new List<int> { 3, 4 });
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, list.ToArray());
    }

    /// <summary>An empty range adds nothing</summary>
    [Test]
    public void AddRange_Empty_AddsNothing()
    {
        RefList<int> list = new();
        list.AddRange(Array.Empty<int>());
        Assert.AreEqual(0, list.Count);
    }

    /// <summary>A null range is rejected</summary>
    [Test]
    public void AddRange_Null_Throws()
    {
        RefList<int> list = new();
        Assert.Catch(() => list.AddRange((int[])null));
        Assert.Catch(() => list.AddRange((IEnumerable<int>)null));
    }

    /// <summary>A null item is a valid item for a reference type</summary>
    [Test]
    public void Add_NullItem_IsStored()
    {
        RefList<string> list = new();
        list.Add(null);
        Assert.AreEqual(1, list.Count);
        Assert.IsTrue(list.Contains(null));
        Assert.AreEqual(0, list.IndexOf(null));
    }
    #endregion

    #region Indexer
    /// <summary>The indexer returns a reference, so writing through it changes the list</summary>
    [Test]
    public void Indexer_ReturnsReference()
    {
        RefList<int> list = OneTwoThree();
        ref int second = ref list[1];
        second = 20;
        Assert.AreEqual(20, list[1]);
    }

    /// <summary>The first and last items can be read</summary>
    [Test]
    public void Indexer_FirstAndLast_Read()
    {
        RefList<int> list = OneTwoThree();
        Assert.AreEqual(1, list[0]);
        Assert.AreEqual(3, list[list.Count - 1]);
    }

    /// <summary>Just outside either end throws</summary>
    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MinValue)]
    public void Indexer_OutOfRange_Throws(int index)
    {
        RefList<int> list = OneTwoThree();
        Assert.Throws<IndexOutOfRangeException>(() => _ = list[index]);
    }

    /// <summary>Spare capacity past Count isn't readable</summary>
    [Test]
    public void Indexer_InsideCapacityPastCount_Throws()
    {
        RefList<int> list = new();
        list.Add(1);
        Assert.Throws<IndexOutOfRangeException>(() => _ = list[1]);
    }
    #endregion

    #region Last and removing
    /// <summary>Last gives the newest item</summary>
    [Test]
    public void Last_ReturnsNewest()
    {
        Assert.AreEqual(3, OneTwoThree().Last());
    }

    /// <summary>An empty list has no last item to give or remove</summary>
    [Test]
    public void LastAndRemoveLast_Empty_Throw()
    {
        RefList<int> list = new();
        Assert.Throws<InvalidOperationException>(() => list.Last());
        Assert.Throws<InvalidOperationException>(() => list.RemoveLast());
    }

    /// <summary>RemoveLast drops the newest item</summary>
    [Test]
    public void RemoveLast_DropsNewest()
    {
        RefList<int> list = OneTwoThree();
        list.RemoveLast();
        CollectionAssert.AreEqual(new[] { 1, 2 }, list.ToArray());
    }

    /// <summary>Remove swaps the last item into the gap, so order isn't kept</summary>
    [Test]
    public void Remove_Present_SwapsLastIn()
    {
        RefList<int> list = OneTwoThree();
        Assert.IsTrue(list.Remove(1));
        CollectionAssert.AreEqual(new[] { 3, 2 }, list.ToArray());
    }

    /// <summary>Removing something not in the list changes nothing</summary>
    [Test]
    public void Remove_Missing_ReturnsFalse()
    {
        RefList<int> list = OneTwoThree();
        Assert.IsFalse(list.Remove(99));
        Assert.AreEqual(3, list.Count);
    }

    /// <summary>RemoveAt swaps the last item into the gap</summary>
    [Test]
    public void RemoveAt_Middle_SwapsLastIn()
    {
        RefList<int> list = OneTwoThree();
        list.RemoveAt(0);
        CollectionAssert.AreEqual(new[] { 3, 2 }, list.ToArray());
    }

    /// <summary>RemoveAt of the only item empties the list</summary>
    [Test]
    public void RemoveAt_OnlyItem_Empties()
    {
        RefList<int> list = new();
        list.Add(7);
        list.RemoveAt(0);
        Assert.AreEqual(0, list.Count);
    }

    /// <summary>RemoveAt past the end throws and changes nothing</summary>
    [Test]
    public void RemoveAt_PastEnd_ThrowsAndKeepsCount()
    {
        RefList<int> list = OneTwoThree();
        Assert.Throws<IndexOutOfRangeException>(() => list.RemoveAt(3));
        Assert.AreEqual(3, list.Count);
    }

    /// <summary>A negative index has to be rejected before anything changes</summary>
    [Test, Category("KnownBug")]
    public void RemoveAt_Negative_ThrowsAndKeepsCount()
    {
        RefList<int> list = OneTwoThree();
        Assert.Catch(() => list.RemoveAt(-1));
        Assert.AreEqual(3, list.Count, "RemoveAt(-1) lowered Count before it threw");
    }

    /// <summary>The IList version of RemoveAt keeps the order</summary>
    [Test]
    public void IListRemoveAt_KeepsOrder()
    {
        IList<int> list = OneTwoThree();
        list.RemoveAt(0);
        CollectionAssert.AreEqual(new[] { 2, 3 }, ((RefList<int>)list).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(5));
    }

    /// <summary>The IList version of Insert shifts later items along</summary>
    [Test]
    public void IListInsert_ShiftsItems()
    {
        IList<int> list = OneTwoThree();
        list.Insert(0, 0);
        list.Insert(4, 4);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, ((RefList<int>)list).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(9, 9));
    }
    #endregion

    #region Searching and copying
    /// <summary>Contains and IndexOf find present items and miss others</summary>
    [Test]
    public void ContainsAndIndexOf()
    {
        RefList<int> list = OneTwoThree();
        Assert.IsTrue(list.Contains(2));
        Assert.IsFalse(list.Contains(4));
        Assert.AreEqual(2, list.IndexOf(3));
        Assert.AreEqual(-1, list.IndexOf(4));
    }

    /// <summary>CopyTo copies into an array at an offset</summary>
    [Test]
    public void CopyTo_AtOffset()
    {
        int[] target = new int[5];
        OneTwoThree().CopyTo(target, 2);
        CollectionAssert.AreEqual(new[] { 0, 0, 1, 2, 3 }, target);
    }

    /// <summary>CopyTo rejects a missing or too small array</summary>
    [Test]
    public void CopyTo_NullOrTooSmall_Throws()
    {
        RefList<int> list = OneTwoThree();
        Assert.Catch(() => list.CopyTo(null, 0));
        Assert.Catch(() => list.CopyTo(new int[2], 0));
    }

    /// <summary>ToArray and ToList are copies, not views</summary>
    [Test]
    public void ToArrayAndToList_AreCopies()
    {
        RefList<int> list = OneTwoThree();
        int[] array = list.ToArray();
        List<int> copy = list.ToList();
        list[0] = 100;
        Assert.AreEqual(1, array[0]);
        Assert.AreEqual(1, copy[0]);
    }
    #endregion

    #region Bulk operations
    /// <summary>ForEach runs on every item and the mapping versions return new arrays</summary>
    [Test]
    public void ForEach_VisitsEveryItem()
    {
        RefList<int> list = OneTwoThree();
        int sum = 0;
        list.ForEach(item => sum += item);
        Assert.AreEqual(6, sum);

        CollectionAssert.AreEqual(new[] { 2, 4, 6 }, list.ForEach((Func<int, int>)(item => item * 2)));
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, list.ForEach<string>(item => item.ToString()));
    }

    /// <summary>Fill sets every item</summary>
    [Test]
    public void Fill_SetsEveryItem()
    {
        RefList<int> list = OneTwoThree();
        list.Fill(9);
        CollectionAssert.AreEqual(new[] { 9, 9, 9 }, list.ToArray());
    }

    /// <summary>Clear empties the list but keeps the room it had</summary>
    [Test]
    public void Clear_KeepsCapacity()
    {
        RefList<int> list = new();
        for (int i = 0; i < 10; i++)
            list.Add(i);
        int capacity = list.Capacity;

        list.Clear();
        Assert.AreEqual(0, list.Count);
        Assert.AreEqual(capacity, list.Capacity);
    }

    /// <summary>EnsureCapacity only ever grows</summary>
    [Test]
    public void EnsureCapacity_OnlyGrows()
    {
        RefList<int> list = new();
        list.EnsureCapacity(100);
        Assert.AreEqual(100, list.Capacity);
        list.EnsureCapacity(10);
        Assert.AreEqual(100, list.Capacity);
    }

    /// <summary>AsSpan covers exactly the items</summary>
    [Test]
    public void AsSpan_CoversItems()
    {
        RefList<int> list = OneTwoThree();
        Assert.AreEqual(3, list.AsSpan().Length);
        Assert.AreEqual(2, list.AsSpan(1).Length);
        Assert.Catch(() => list.AsSpan(4));
    }

    /// <summary>Enumerating visits every item in order, and nothing for an empty list</summary>
    [Test]
    public void Enumerator_VisitsInOrder()
    {
        List<int> seen = new();
        foreach (int item in OneTwoThree())
            seen.Add(item);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, seen);

        foreach (int _ in new RefList<int>())
            Assert.Fail("An empty list has nothing to visit");
    }
    #endregion
}
