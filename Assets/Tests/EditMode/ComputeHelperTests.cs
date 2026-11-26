using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

/// <summary>ComputeHelper: GPU buffer helpers. Buffer tests need a graphics device with compute shaders</summary>
public class ComputeHelperTests
{
    private readonly List<ComputeBuffer> buffers = new();

    [TearDown]
    public void ReleaseBuffers()
    {
        ComputeHelper.Release(buffers);
        buffers.Clear();
    }

    private static void NeedCompute() => Assume.That(SystemInfo.supportsComputeShaders, "No compute shader support");

    /// <summary>The stride is the struct's size in bytes</summary>
    [Test]
    public void GetStride_MatchesStructSize()
    {
        Assert.AreEqual(4, ComputeHelper.GetStride<float>());
        Assert.AreEqual(12, ComputeHelper.GetStride<float3>());
        Assert.AreEqual(16, ComputeHelper.GetStride<float4>());
    }

    /// <summary>Releasing nothing is safe</summary>
    [Test]
    public void Release_Null_DoesNothing()
    {
        Assert.DoesNotThrow(() => ComputeHelper.Release((ComputeBuffer)null));
        Assert.DoesNotThrow(() => ComputeHelper.Release((GraphicsBuffer)null));
        Assert.DoesNotThrow(() => ComputeHelper.Release(new ComputeBuffer[] { null }));
    }

    /// <summary>A buffer can't be empty, so a count of 0 or less still makes one element</summary>
    [TestCase(0)]
    [TestCase(-3)]
    public void CreateBuffer_CountBelowOne_MakesOneElement(int count)
    {
        NeedCompute();
        ComputeBuffer buffer = ComputeHelper.CreateBuffer<float>(count);
        buffers.Add(buffer);
        Assert.AreEqual(1, buffer.count);
    }

    /// <summary>Empty data still makes a one element buffer</summary>
    [Test]
    public void CreateBuffer_EmptyData_MakesOneZero()
    {
        NeedCompute();
        ComputeBuffer buffer = ComputeHelper.CreateBuffer(new List<float>());
        buffers.Add(buffer);
        CollectionAssert.AreEqual(new[] { 0f }, buffer.Read<float>());
    }

    /// <summary>Null data is rejected</summary>
    [Test]
    public void CreateBuffer_NullData_Throws()
    {
        Assert.Catch(() => ComputeHelper.CreateBuffer((IReadOnlyList<float>)null));
    }

    /// <summary>Data written to a buffer reads back the same, and SetElement changes one element</summary>
    [Test]
    public void CreateBuffer_ThenRead_RoundTrips()
    {
        NeedCompute();
        ComputeBuffer buffer = ComputeHelper.CreateBuffer(new[] { new float3(1, 2, 3), new float3(4, 5, 6) });
        buffers.Add(buffer);
        buffer.SetElement(1, new float3(7, 8, 9));

        float3[] read = buffer.Read<float3>();
        Assert.AreEqual(new float3(1, 2, 3), read[0]);
        Assert.AreEqual(new float3(7, 8, 9), read[1]);
    }

    /// <summary>The same buffer twice in a list is only released once</summary>
    [Test]
    public void Release_Duplicates_ReleasedOnce()
    {
        NeedCompute();
        ComputeBuffer buffer = ComputeHelper.CreateBuffer<float>(1);
        Assert.DoesNotThrow(() => ComputeHelper.Release(new[] { buffer, buffer }));
        Assert.IsFalse(buffer.IsValid());
    }
}
