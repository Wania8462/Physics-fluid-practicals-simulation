using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Tank and Spawn3DParticles: the tank's size, fluid particles and body surface samples</summary>
public class SpawnTests
{
    private GameObject host;
    private Spawn3DParticles spawn;

    [SetUp]
    public void CreateSpawner()
    {
        host = new GameObject("Spawn test");
        spawn = host.AddComponent<Spawn3DParticles>();
        UnityEngine.Random.InitState(1);
    }

    [TearDown]
    public void DestroySpawner() => Object.DestroyImmediate(host);

    #region Tank
    /// <summary>The inner size is where a particle's centre touches the wall</summary>
    [Test]
    public void Tank_HalfAndInnerSizes()
    {
        Tank tank = new(new float3(120, 60, 40), 0.5f);
        Assert.AreEqual(new float3(60, 30, 20), tank.halfSize);
        Assert.AreEqual(new float3(59.5f, 29.5f, 19.5f), tank.innerHalfSize);
    }

    /// <summary>With particles of radius 0 the inner size is the half size</summary>
    [Test]
    public void Tank_ZeroRadius_InnerIsHalf()
    {
        Tank tank = new(new float3(10), 0);
        Assert.AreEqual(tank.halfSize, tank.innerHalfSize);
    }

    /// <summary>The default spawner's tank is the particle box plus padding on each side</summary>
    [Test]
    public void GetTank_Default()
    {
        Tank tank = spawn.GetTank(0.5f);
        Assert.AreEqual(new float3(120), tank.size);
        Assert.AreEqual(new float3(59.5f), tank.innerHalfSize);
    }
    #endregion

    #region Fluid particles
    /// <summary>The default 40 box at spacing 2 holds 20³ particles, all inside the tank</summary>
    [Test]
    public void InitializePositions_Default_FillsBoxInsideTank()
    {
        float4[] positions = spawn.InitializePositions();
        Tank tank = spawn.GetTank(0.5f);

        Assert.AreEqual(8000, spawn.GetNumberOfParticles());
        Assert.AreEqual(8000, positions.Length);
        Assert.IsTrue(positions.All(p => math.all(math.abs(p.xyz) <= tank.innerHalfSize)));
        Assert.AreEqual(20, spawn.SurfaceHeight, 1e-4f);
    }

    /// <summary>SetParticleCount spawns exactly that many, also when it isn't a cube number</summary>
    [TestCase(1)]
    [TestCase(8)]
    [TestCase(1000)]
    [TestCase(1001)]
    [TestCase(100000)]
    public void SetParticleCount_SpawnsExactly(int count)
    {
        spawn.SetParticleCount(count);
        Assert.AreEqual(count, spawn.ApplyMaxParticles(spawn.InitializePositions()).Length);
    }

    /// <summary>A count of 0 spawns nothing</summary>
    [Test]
    public void SetParticleCount_Zero_SpawnsNothing()
    {
        spawn.SetParticleCount(0);
        Assert.AreEqual(0, spawn.ApplyMaxParticles(spawn.InitializePositions()).Length);
    }

    /// <summary>A negative count can't make a negative number of particles</summary>
    [Test]
    public void SetParticleCount_Negative_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => spawn.SetParticleCount(-5));
        Assert.GreaterOrEqual(spawn.ApplyMaxParticles(spawn.InitializePositions()).Length, 0);
    }

    /// <summary>With no cap every particle is kept, as the same array</summary>
    [Test]
    public void ApplyMaxParticles_NoCap_SameArray()
    {
        float4[] positions = new float4[3];
        Assert.AreSame(positions, spawn.ApplyMaxParticles(positions));
    }

    /// <summary>A cap above what fits warns and keeps all there are</summary>
    [Test]
    public void ApplyMaxParticles_CapAboveCount_WarnsAndKeepsAll()
    {
        spawn.SetParticleCount(10);
        LogAssert.Expect(LogType.Warning, new Regex("max particles fit"));
        Assert.AreEqual(3, spawn.ApplyMaxParticles(new float4[3]).Length);
    }
    #endregion

    #region Body surfaces
    /// <summary>A 20 wide box sampled every 1 has (c+1)³ - (c-1)³ points, all on its surface</summary>
    [Test]
    public void InitBoxSurfacePositions_CountAndOnSurface()
    {
        float3[] points = spawn.InitBoxSurfacePositions(new float3(10), 1);
        Assert.AreEqual(21 * 21 * 21 - 19 * 19 * 19, points.Length);
        Assert.IsTrue(points.All(p => math.abs(RigidBodyMath3D.BoxSignedDistance(p, new float3(10))) < 1e-4f));
    }

    /// <summary>A box smaller than the sample spacing still gets its 8 corners</summary>
    [Test]
    public void InitBoxSurfacePositions_TinyBox_Corners()
    {
        Assert.AreEqual(8, spawn.InitBoxSurfacePositions(new float3(0.1f), 1).Length);
    }

    /// <summary>A sphere gets enough points for its area, all at its radius</summary>
    [Test]
    public void InitSphereSurfacePositions_CountAndOnRadius()
    {
        float3[] points = spawn.InitSphereSurfacePositions(10, 2);
        Assert.AreEqual(Mathf.CeilToInt(4 * math.PI * 100 / 4), points.Length);
        Assert.IsTrue(points.All(p => math.abs(math.length(p) - 10) < 1e-3f));
    }

    /// <summary>A sphere too small for 4 points warns and still gets 4</summary>
    [Test]
    public void InitSphereSurfacePositions_TinySphere_WarnsAndUsesFour()
    {
        LogAssert.Expect(LogType.Warning, new Regex("not enough particles"));
        Assert.AreEqual(4, spawn.InitSphereSurfacePositions(0.1f, 2).Length);
    }

    /// <summary>A scale's samples are on or above its base, with none hidden under the pan</summary>
    [Test]
    public void InitScaleSurfacePositions_NoneUnderPan()
    {
        float3 halfExtents = RigidBodies3D.ScaleHalfExtents;
        float3[] points = spawn.InitScaleSurfacePositions(halfExtents, RigidBodies3D.ScalePanRadius, RigidBodies3D.ScalePanHalfHeight, 2);

        Assert.Greater(points.Length, 0);
        float top = halfExtents.y + 2 * RigidBodies3D.ScalePanHalfHeight;
        Assert.IsTrue(points.All(p => p.y >= -halfExtents.y - 1e-4f && p.y <= top + 1e-4f));
    }
    #endregion
}
