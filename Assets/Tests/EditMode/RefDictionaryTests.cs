using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>RefDictionary: an open addressing hash map that hands out references to its values</summary>
public class RefDictionaryTests
{
    #region Adding and reading
    /// <summary>An added value can be read back by its key</summary>
    [Test]
    public void Add_ThenIndex_ReturnsValue()
    {
        RefDictionary<string, int> dict = new();
        dict.Add("a", 1);
        dict.Add("b", 2);

        Assert.AreEqual(2, dict.Count);
        Assert.AreEqual(1, dict["a"]);
        Assert.AreEqual(2, dict["b"]);
    }

    /// <summary>The indexer returns a reference, so writing through it changes the stored value</summary>
    [Test]
    public void Indexer_ReturnsReference()
    {
        RefDictionary<int, int> dict = new();
        dict.Add(1, 10);
        dict[1] += 5;
        Assert.AreEqual(15, dict[1]);
    }

    /// <summary>A key can only be added once</summary>
    [Test]
    public void Add_DuplicateKey_Throws()
    {
        RefDictionary<int, int> dict = new();
        dict.Add(1, 1);
        Assert.Throws<ArgumentException>(() => dict.Add(1, 2));
        Assert.AreEqual(1, dict[1]);
    }

    /// <summary>Reading a missing key throws</summary>
    [Test]
    public void Indexer_MissingKey_Throws()
    {
        RefDictionary<int, int> dict = new();
        Assert.Throws<KeyNotFoundException>(() => _ = dict[42]);
    }

    /// <summary>The IDictionary setter adds a missing key and replaces a present one</summary>
    [Test]
    public void IDictionarySetter_AddsOrReplaces()
    {
        IDictionary<int, int> dict = new RefDictionary<int, int>();
        dict[1] = 1;
        dict[1] = 2;
        Assert.AreEqual(1, dict.Count);
        Assert.AreEqual(2, dict[1]);
    }

    /// <summary>TryGetValue finds present keys and gives the default for missing ones</summary>
    [Test]
    public void TryGetValue_PresentAndMissing()
    {
        RefDictionary<int, string> dict = new();
        dict.Add(1, "one");

        Assert.IsTrue(dict.TryGetValue(1, out string found));
        Assert.AreEqual("one", found);
        Assert.IsFalse(dict.TryGetValue(2, out string missing));
        Assert.IsNull(missing);
    }

    /// <summary>GetOrAdd adds a default value the first time and returns the same one after</summary>
    [Test]
    public void GetOrAdd_AddsOnceThenReturnsSame()
    {
        RefDictionary<int, int> dict = new();
        dict.GetOrAdd(7) = 3;
        dict.GetOrAdd(7) += 1;

        Assert.AreEqual(1, dict.Count);
        Assert.AreEqual(4, dict[7]);
    }

    /// <summary>A null key is stored like any other</summary>
    [Test]
    public void NullKey_RoundTrips()
    {
        RefDictionary<string, int> dict = new();
        dict.Add(null, 5);
        Assert.IsTrue(dict.ContainsKey(null));
        Assert.AreEqual(5, dict[null]);
    }
    #endregion

    #region Capacity
    /// <summary>A capacity of 0 or less still makes a working dictionary</summary>
    [TestCase(0)]
    [TestCase(-5)]
    [TestCase(1)]
    public void Constructor_TinyCapacity_StillWorks(int capacity)
    {
        RefDictionary<int, int> dict = new(capacity);
        for (int i = 0; i < 10; i++)
            dict.Add(i, i * i);

        Assert.AreEqual(10, dict.Count);
        Assert.AreEqual(81, dict[9]);
    }

    /// <summary>Growing many times keeps every entry</summary>
    [Test]
    public void Add_ThousandKeys_AllFound()
    {
        RefDictionary<int, int> dict = new();
        for (int i = 0; i < 1000; i++)
            dict.Add(i, -i);

        Assert.AreEqual(1000, dict.Count);
        for (int i = 0; i < 1000; i++)
            Assert.AreEqual(-i, dict[i]);
    }

    /// <summary>Keys whose hashes collide are all kept apart</summary>
    [Test]
    public void CollidingKeys_AllFound()
    {
        // Multiples of the table size land in the same slot
        RefDictionary<int, int> dict = new(16);
        int[] keys = { 0, 16, 32, 48, 64 };
        foreach (int key in keys)
            dict.Add(key, key + 1);

        foreach (int key in keys)
            Assert.AreEqual(key + 1, dict[key]);
    }
    #endregion

    #region Removing
    /// <summary>A removed key is gone and the others stay</summary>
    [Test]
    public void Remove_Present_RemovesOnlyThatKey()
    {
        RefDictionary<int, int> dict = new();
        dict.Add(1, 1);
        dict.Add(2, 2);
        dict.Remove(1);

        Assert.AreEqual(1, dict.Count);
        Assert.IsFalse(dict.ContainsKey(1));
        Assert.AreEqual(2, dict[2]);
    }

    /// <summary>Remove throws for a missing key, TryRemove returns false</summary>
    [Test]
    public void Remove_Missing_ThrowsButTryRemoveDoesNot()
    {
        RefDictionary<int, int> dict = new();
        Assert.Throws<KeyNotFoundException>(() => dict.Remove(1));
        Assert.IsFalse(dict.TryRemove(1));
    }

    /// <summary>A key past a removed one in the same probe chain is still found</summary>
    [Test]
    public void Remove_InProbeChain_LaterKeysStillFound()
    {
        RefDictionary<int, int> dict = new(16);
        dict.Add(0, 0);
        dict.Add(16, 16);
        dict.Add(32, 32);
        dict.Remove(16);

        Assert.AreEqual(32, dict[32]);
        Assert.IsFalse(dict.ContainsKey(16));
    }

    /// <summary>A key can be added again after being removed</summary>
    [Test]
    public void Remove_ThenAddAgain_Works()
    {
        RefDictionary<int, int> dict = new();
        dict.Add(1, 1);
        dict.Remove(1);
        dict.Add(1, 2);
        Assert.AreEqual(2, dict[1]);
        Assert.AreEqual(1, dict.Count);
    }

    /// <summary>Clear empties the dictionary</summary>
    [Test]
    public void Clear_Empties()
    {
        RefDictionary<int, int> dict = new();
        dict.Add(1, 1);
        dict.Clear();
        Assert.AreEqual(0, dict.Count);
        Assert.IsFalse(dict.ContainsKey(1));
    }

    /// <summary>Adding and removing over and over must not wear out the table</summary>
    [Test, Category("KnownBug"), Timeout(10000)]
    public void AddRemoveChurn_DoesNotHang()
    {
        RefDictionary<int, int> dict = new(4);

        // Each slot of the 4 is used once and left as a removed marker, which never makes the table grow
        Task churn = Task.Run(() =>
        {
            for (int key = 1; key <= 4; key++)
            {
                dict.Add(key, key);
                dict.Remove(key);
            }
            dict.ContainsKey(5);
        });

        // ponytail: a hung lookup can't be stopped, so its thread spins until Unity reloads scripts
        Assert.IsTrue(churn.Wait(1000), "ContainsKey never returned once every slot had been removed from");
    }
    #endregion

    #region Views
    /// <summary>Keys, Values and enumeration see every entry</summary>
    [Test]
    public void KeysValuesAndEnumerator_SeeEveryEntry()
    {
        RefDictionary<int, string> dict = new();
        dict.Add(1, "a");
        dict.Add(2, "b");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, dict.Keys.ToArray());
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, dict.Values.ToArray());
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, dict.Select(pair => pair.Key).ToArray());
    }

    /// <summary>Keys and Values can't be changed directly</summary>
    [Test]
    public void KeysAndValues_AreReadOnly()
    {
        RefDictionary<int, int> dict = new();
        Assert.IsTrue(dict.Keys.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dict.Keys.Add(1));
        Assert.Throws<NotSupportedException>(() => dict.Values.Clear());
    }

    /// <summary>An empty dictionary enumerates nothing</summary>
    [Test]
    public void Empty_EnumeratesNothing()
    {
        CollectionAssert.IsEmpty(new RefDictionary<int, int>().ToArray());
    }
    #endregion
}
