using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>SPValues3D: the neighbour search grid and its Z-order cell keys</summary>
public class SPValuesTests
{
    /// <summary>Each axis's bits interleave as x, y, z from the lowest bit up</summary>
    [TestCase(0u, 0u, 0u, 0u)]
    [TestCase(1u, 0u, 0u, 1u)]
    [TestCase(0u, 1u, 0u, 2u)]
    [TestCase(0u, 0u, 1u, 4u)]
    [TestCase(1u, 1u, 1u, 7u)]
    [TestCase(2u, 0u, 0u, 8u)]
    public void Morton_InterleavesBits(uint x, uint y, uint z, uint expected)
    {
        Assert.AreEqual(expected, SPValues3D.Morton(x, y, z));
    }

    /// <summary>The largest cell on every axis uses all 30 bits</summary>
    [Test]
    public void Morton_LargestCell_AllBits()
    {
        Assert.AreEqual((1u << 30) - 1, SPValues3D.Morton(1023, 1023, 1023));
    }

    /// <summary>Only 10 bits per axis are kept, so 1024 wraps to 0</summary>
    [Test]
    public void Morton_PastTenBits_Wraps()
    {
        Assert.AreEqual(0u, SPValues3D.Morton(1024, 0, 0));
    }

    /// <summary>Different cells get different keys</summary>
    [Test]
    public void Morton_UniqueInSmallGrid()
    {
        var seen = new System.Collections.Generic.HashSet<uint>();
        for (uint x = 0; x < 8; x++)
            for (uint y = 0; y < 8; y++)
                for (uint z = 0; z < 8; z++)
                    Assert.IsTrue(seen.Add(SPValues3D.Morton(x, y, z)));
    }

    /// <summary>A tank that's a whole number of cells wide gets exactly that many cells</summary>
    [Test]
    public void Constructor_ExactFit()
    {
        SPValues3D grid = new(new float3(-60), new float3(60), 6);
        Assert.AreEqual(20, grid.columns);
        Assert.AreEqual(20, grid.rows);
        Assert.AreEqual(20, grid.layers);
        Assert.AreEqual((int)SPValues3D.Morton(19, 19, 19) + 1, grid.NumCells);
        Assert.AreEqual(new float3(-60), grid.offset);
    }

    /// <summary>A part cell at the edge still gets a cell</summary>
    [Test]
    public void Constructor_PartCell_RoundsUp()
    {
        SPValues3D grid = new(new float3(0), new float3(61, 6, 1), 6);
        Assert.AreEqual(11, grid.columns);
        Assert.AreEqual(1, grid.rows);
        Assert.AreEqual(1, grid.layers);
    }

    /// <summary>A cell length of 0 or less logs an error</summary>
    [Test]
    public void Constructor_NegativeLength_LogsError()
    {
        LogAssert.Expect(LogType.Error, new Regex("length must be > 0"));
        new SPValues3D(new float3(-60), new float3(60), -6);
    }

    /// <summary>A flat box has no cells along its flat axis, which is warned about</summary>
    [Test]
    public void Constructor_FlatBox_ZeroCells()
    {
        LogAssert.Expect(LogType.Warning, new Regex("non-positive"));
        LogAssert.Expect(LogType.Warning, new Regex("zero cells"));
        SPValues3D grid = new(new float3(0), new float3(60, 0, 60), 6);
        Assert.AreEqual(0, grid.rows);
    }

    /// <summary>More than 1024 cells along an axis would reuse keys, which is an error</summary>
    [Test]
    public void Constructor_TooManyCells_LogsError()
    {
        LogAssert.Expect(LogType.Error, new Regex($"more than {SPValues3D.MaxCellsPerAxis}"));
        SPValues3D grid = new(new float3(0), new float3(2000, 1, 1), 1);
        Assert.AreEqual(2000, grid.columns);
    }

    /// <summary>Exactly 1024 cells is still allowed</summary>
    [Test]
    public void Constructor_MaxCells_NoError()
    {
        SPValues3D grid = new(new float3(0), new float3(SPValues3D.MaxCellsPerAxis, 1, 1), 1);
        Assert.AreEqual(SPValues3D.MaxCellsPerAxis, grid.columns);
        LogAssert.NoUnexpectedReceived();
    }
}
