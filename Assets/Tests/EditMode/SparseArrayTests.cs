using System;
using NUnit.Framework;

/// <summary>SparseArray: a fixed-size map from ids to slots where -1 means empty</summary>
public class SparseArrayTests
{
    /// <summary>A set slot reads back</summary>
    [Test]
    public void SetThenGet_ReturnsValue()
    {
        SparseArray array = new(4);
        array[2] = 7;
        Assert.AreEqual(7, array[2]);
        Assert.AreEqual(4, array.Length);
    }

    /// <summary>The first and last slots work</summary>
    [Test]
    public void FirstAndLastSlots_Work()
    {
        SparseArray array = new(3);
        array[0] = 1;
        array[2] = 3;
        Assert.AreEqual(1, array[0]);
        Assert.AreEqual(3, array[2]);
    }

    /// <summary>A slot marked empty throws when read</summary>
    [Test]
    public void EmptySlot_Throws()
    {
        SparseArray array = new(3);
        array.Fill(-1);
        Assert.Throws<InvalidOperationException>(() => _ = array[1]);
    }

    /// <summary>Indices outside the array throw</summary>
    [TestCase(-1)]
    [TestCase(3)]
    public void OutOfRange_Throws(int index)
    {
        SparseArray array = new(3);
        Assert.Throws<IndexOutOfRangeException>(() => _ = array[index]);
        Assert.Throws<IndexOutOfRangeException>(() => array[index] = 0);
    }

    /// <summary>An empty array has nothing to read</summary>
    [Test]
    public void ZeroLength_HasNoSlots()
    {
        SparseArray array = new(0);
        Assert.AreEqual(0, array.Length);
        Assert.Throws<IndexOutOfRangeException>(() => _ = array[0]);
    }

    /// <summary>A negative length can't make an array</summary>
    [Test]
    public void NegativeLength_Throws()
    {
        Assert.Catch(() => new SparseArray(-1));
    }

    /// <summary>CopyTo copies the first count slots only</summary>
    [Test]
    public void CopyTo_CopiesFirstSlots()
    {
        SparseArray source = new(3);
        source[0] = 1;
        source[1] = 2;
        source[2] = 3;
        SparseArray target = new(3);
        target.Fill(9);

        source.CopyTo(target, 2);
        Assert.AreEqual(1, target[0]);
        Assert.AreEqual(2, target[1]);
        Assert.AreEqual(9, target[2]);
    }

    /// <summary>CopyTo rejects a missing target or a count longer than either array</summary>
    [Test]
    public void CopyTo_NullOrTooLong_Throws()
    {
        SparseArray source = new(3);
        Assert.Catch(() => source.CopyTo(null, 1));
        Assert.Catch(() => source.CopyTo(new SparseArray(2), 3));
        Assert.Catch(() => source.CopyTo(new SparseArray(5), 4));
    }
}
