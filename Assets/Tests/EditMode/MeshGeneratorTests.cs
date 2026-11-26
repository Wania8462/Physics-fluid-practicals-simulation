using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>MeshGenerator: simple meshes built in code</summary>
public class MeshGeneratorTests
{
    /// <summary>A rectangle has 4 corners, 2 triangles and the asked for size</summary>
    [Test]
    public void Rectangle_HasFourCornersAndSize()
    {
        Mesh mesh = MeshGenerator.Rectangle(4, 2);
        Assert.AreEqual(4, mesh.vertexCount);
        Assert.AreEqual(6, mesh.triangles.Length);
        Assert.AreEqual(new Vector3(4, 2, 0), mesh.bounds.size);
        Assert.AreEqual(Vector3.zero, mesh.bounds.center);
    }

    /// <summary>A rectangle of size 0 is flat but still valid</summary>
    [Test]
    public void Rectangle_ZeroSize_IsDegenerate()
    {
        Mesh mesh = MeshGenerator.Rectangle(0, 0);
        Assert.AreEqual(Vector3.zero, mesh.bounds.size);
    }

    /// <summary>A line is a strip as wide as asked, from start to end</summary>
    [Test]
    public void Line_HasWidthAndLength()
    {
        Mesh mesh = MeshGenerator.Line(new float2(0, 0), new float2(10, 0), 2);
        Assert.AreEqual(4, mesh.vertexCount);
        Assert.AreEqual(10, mesh.bounds.size.x, 1e-5f);
        Assert.AreEqual(2, mesh.bounds.size.y, 1e-5f);
    }

    /// <summary>A line with no length can't have a direction, so there's no mesh</summary>
    [Test]
    public void Line_SamePoint_ReturnsNull()
    {
        Assert.IsNull(MeshGenerator.Line(new float2(3, 3), new float2(3, 3), 1));
    }

    /// <summary>A wire box has 8 corners joined by 12 lines</summary>
    [Test]
    public void WireBox_HasTwelveEdges()
    {
        Mesh mesh = MeshGenerator.WireBox(new float3(2, 4, 6));
        Assert.AreEqual(8, mesh.vertexCount);
        Assert.AreEqual(MeshTopology.Lines, mesh.GetTopology(0));
        Assert.AreEqual(24, mesh.GetIndices(0).Length);
        Assert.AreEqual(new Vector3(2, 4, 6), mesh.bounds.size);
    }

    /// <summary>A circle has a centre plus 4 points per resolution step, all at the radius</summary>
    [TestCase(1)]
    [TestCase(8)]
    public void Circle_PointsOnRadius(int resolution)
    {
        Mesh mesh = MeshGenerator.Circle(5, resolution);
        Assert.AreEqual(4 * resolution + 1, mesh.vertexCount);
        Assert.AreEqual(12 * resolution, mesh.triangles.Length);
        Assert.IsTrue(mesh.vertices.Skip(1).All(v => Mathf.Abs(v.magnitude - 5) < 1e-4f));
        Assert.IsTrue(mesh.triangles.All(i => i >= 0 && i < mesh.vertexCount));
    }

    /// <summary>A radius or resolution of 0 or less logs an error and gives no mesh</summary>
    [TestCase(0, 8)]
    [TestCase(-1, 8)]
    [TestCase(5, 0)]
    [TestCase(5, -2)]
    public void Circle_BadInput_LogsErrorAndReturnsNull(float radius, int resolution)
    {
        LogAssert.Expect(LogType.Error, new Regex("Mesh generator"));
        Assert.IsNull(MeshGenerator.Circle(radius, resolution));
    }

    /// <summary>There's a mesh for each of the 16 marching squares cases: none filled has no triangles</summary>
    [Test]
    public void MarchingSquareVariations_SixteenCases()
    {
        Mesh[] meshes = MeshGenerator.MarchingSquareVariations();
        Assert.AreEqual(16, meshes.Length);
        Assert.AreEqual(0, meshes[0].triangles.Length);
        Assert.Greater(meshes[15].triangles.Length, 0);
    }
}
