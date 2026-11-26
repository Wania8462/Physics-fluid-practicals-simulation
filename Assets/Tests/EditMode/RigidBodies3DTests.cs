using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>RigidBodies3D: building bodies, their mass, removing them and how submerged they are</summary>
public class RigidBodies3DTests
{
    private const float InteractionRadius = 6;
    private static readonly Tank BigTank = new(new float3(1000), 0.5f);

    private GameObject host;
    private Spawn3DParticles spawn;

    [SetUp]
    public void CreateSpawner()
    {
        host = new GameObject("Rigid body test");
        spawn = host.AddComponent<Spawn3DParticles>();
    }

    [TearDown]
    public void DestroySpawner() => UnityEngine.Object.DestroyImmediate(host);

    private static RigidBodySettings3D Body(BodyShape3D shape, float radius, float3 position, float mass = 0) => new()
    {
        shape = shape,
        bodyRadius = radius,
        sampleDensity = 2,
        bodyPosition = position,
        boundaryBodyMass = mass
    };

    #region Radius from volume
    /// <summary>Cubes and spheres get the radius that gives them the volume</summary>
    [Test]
    public void RadiusFromVolume_CubeAndSphere()
    {
        Assert.AreEqual(10, RigidBodies3D.RadiusFromVolume(BodyShape3D.Cube, 8000), 1e-4f);
        Assert.AreEqual(12.407f, RigidBodies3D.RadiusFromVolume(BodyShape3D.Sphere, 8000), 1e-3f);
    }

    /// <summary>Scales come in one size, so they have no radius for a volume</summary>
    [Test]
    public void RadiusFromVolume_Scale_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RigidBodies3D.RadiusFromVolume(BodyShape3D.Scale, 8000));
        Assert.Throws<ArgumentOutOfRangeException>(() => RigidBodies3D.RadiusFromVolume((BodyShape3D)99, 8000));
    }
    #endregion

    #region Mass properties
    /// <summary>A sphere's inverse mass and inverse inertia come from its mass and radius</summary>
    [Test]
    public void SetMassProperties_Sphere()
    {
        RigidBodyProperties3D properties = new();
        RigidBodies3D.SetMassProperties(ref properties, Body(BodyShape3D.Sphere, 2, 0, 10));

        Assert.AreEqual(0.1f, properties.invMass, 1e-6f);
        Assert.AreEqual(1f / 16, properties.invInertia.x, 1e-6f);
    }

    /// <summary>With no mass set, a body weighs as much as its boundary particles</summary>
    [TestCase(0)]
    [TestCase(-5)]
    public void SetMassProperties_NoMass_UsesParticleCount(float mass)
    {
        RigidBodyProperties3D properties = new() { count = 4 };
        RigidBodies3D.SetMassProperties(ref properties, Body(BodyShape3D.Cube, 1, 0, mass));
        Assert.AreEqual(0.25f, properties.invMass, 1e-6f);
    }

    /// <summary>Scales and fixed bodies can't be moved by anything</summary>
    [Test]
    public void SetMassProperties_ScaleOrFixed_Immovable()
    {
        RigidBodySettings3D fixedCube = Body(BodyShape3D.Cube, 1, 0, 10);
        fixedCube.fixedInPlace = true;

        foreach (RigidBodySettings3D body in new[] { fixedCube, Body(BodyShape3D.Scale, 1, 0, 10) })
        {
            RigidBodyProperties3D properties = new() { invMass = 5 };
            RigidBodies3D.SetMassProperties(ref properties, body);
            Assert.AreEqual(0, properties.invMass);
            Assert.AreEqual(float4.zero, properties.invInertia);
        }
    }

    /// <summary>An unknown shape has no inertia</summary>
    [Test]
    public void SetMassProperties_UnknownShape_Throws()
    {
        RigidBodyProperties3D properties = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => RigidBodies3D.SetMassProperties(ref properties, Body((BodyShape3D)99, 1, 0, 1)));
    }
    #endregion

    #region Building
    /// <summary>No bodies, or a null array, builds nothing</summary>
    [Test]
    public void Build_NullOrEmpty_NoBodies()
    {
        Assert.AreEqual(0, RigidBodies3D.Build(null, spawn, InteractionRadius, BigTank).NumBodies);
        Assert.AreEqual(0, RigidBodies3D.Build(new RigidBodySettings3D[0], spawn, InteractionRadius, BigTank).NumBodies);
    }

    /// <summary>Empty slots are skipped and keep their inspector index for the bodies after them</summary>
    [Test]
    public void Build_SkipsEmptySlots()
    {
        RigidBodySettings3D[] bodies = { default, Body(BodyShape3D.Sphere, 10, new float3(-100, 0, 0)), default, Body(BodyShape3D.Cube, 10, new float3(100, 0, 0)) };
        RigidBodyData3D data = RigidBodies3D.Build(bodies, spawn, InteractionRadius, BigTank);

        Assert.AreEqual(2, data.NumBodies);
        CollectionAssert.AreEqual(new[] { 1, 3 }, data.inspectorIndices);
        Assert.AreEqual(data.NumBoundaryParticles, data.properties.Sum(p => (int)p.count));
    }

    /// <summary>A negative radius or sample spacing logs an error and the body is left out</summary>
    [Test]
    public void Build_BadBody_LogsErrorAndSkips()
    {
        RigidBodySettings3D noSpacing = Body(BodyShape3D.Sphere, 10, 0);
        noSpacing.sampleDensity = 0;

        LogAssert.Expect(LogType.Error, new Regex("needs bodyRadius and sampleDensity above 0"));
        LogAssert.Expect(LogType.Error, new Regex("needs bodyRadius and sampleDensity above 0"));
        RigidBodyData3D data = RigidBodies3D.Build(new[] { Body(BodyShape3D.Sphere, -3, 0), noSpacing }, spawn, InteractionRadius, BigTank);

        Assert.AreEqual(0, data.NumBodies);
    }

    /// <summary>Only MaxBodies bodies are built, the rest are reported and left out</summary>
    [Test]
    public void Build_TooManyBodies_StopsAtMax()
    {
        RigidBodySettings3D[] bodies = Enumerable.Range(0, RigidBodies3D.MaxBodies + 1)
            .Select(i => Body(BodyShape3D.Sphere, 5, new float3(-400 + i * 50, 0, 0)))
            .ToArray();

        LogAssert.Expect(LogType.Error, new Regex($"at most {RigidBodies3D.MaxBodies} bodies"));
        RigidBodyData3D data = RigidBodies3D.Build(bodies, spawn, InteractionRadius, BigTank);
        Assert.AreEqual(RigidBodies3D.MaxBodies, data.NumBodies);
    }
    #endregion

    #region Removing
    /// <summary>Removing a body drops its particles and moves the later bodies' particles down</summary>
    [Test]
    public void RemoveBody_FirstOfTwo_ShiftsTheOther()
    {
        RigidBodySettings3D[] bodies = { Body(BodyShape3D.Sphere, 10, new float3(-100, 0, 0)), Body(BodyShape3D.Cube, 10, new float3(100, 0, 0)) };
        RigidBodyData3D data = RigidBodies3D.Build(bodies, spawn, InteractionRadius, BigTank);
        int secondCount = (int)data.properties[1].count;

        RigidBodies3D.RemoveBody(data, 0);

        Assert.AreEqual(1, data.NumBodies);
        Assert.AreEqual(0u, data.properties[0].start);
        Assert.AreEqual(secondCount, data.NumBoundaryParticles);
        Assert.IsTrue(data.boundaryBodyIndices.All(index => index == 0));
        CollectionAssert.AreEqual(new[] { 1 }, data.inspectorIndices);
    }

    /// <summary>Removing a slot that was never built changes nothing</summary>
    [TestCase(5)]
    [TestCase(-1)]
    public void RemoveBody_UnknownSlot_NoChange(int slot)
    {
        RigidBodyData3D data = RigidBodies3D.Build(new[] { Body(BodyShape3D.Sphere, 10, 0) }, spawn, InteractionRadius, BigTank);
        int particles = data.NumBoundaryParticles;

        RigidBodies3D.RemoveBody(data, slot);
        Assert.AreEqual(1, data.NumBodies);
        Assert.AreEqual(particles, data.NumBoundaryParticles);
    }
    #endregion

    #region Submerged fraction
    /// <summary>Wet marks for a body: wet where below is true</summary>
    private static uint[] WetWhere(RigidBodyData3D data, Func<float3, bool> below)
        => data.boundaryRestPositions.Select(p => below(p.xyz) ? 1u : 0u).ToArray();

    /// <summary>A dry body is 0 under, a fully wet one is all under</summary>
    [TestCase(BodyShape3D.Sphere)]
    [TestCase(BodyShape3D.Cube)]
    public void SubmergedFraction_DryOrWet(BodyShape3D shape)
    {
        RigidBodyData3D data = RigidBodies3D.Build(new[] { Body(shape, 10, 0) }, spawn, InteractionRadius, BigTank);
        Assert.AreEqual(0, RigidBodies3D.SubmergedFraction(data, 0, new float3(0, 1, 0), WetWhere(data, _ => false)));
        Assert.AreEqual(1, RigidBodies3D.SubmergedFraction(data, 0, new float3(0, 1, 0), WetWhere(data, _ => true)));
    }

    /// <summary>Wet up to its middle, a body is about half under</summary>
    [TestCase(BodyShape3D.Sphere)]
    [TestCase(BodyShape3D.Cube)]
    public void SubmergedFraction_WetToMiddle_Half(BodyShape3D shape)
    {
        RigidBodyData3D data = RigidBodies3D.Build(new[] { Body(shape, 10, 0) }, spawn, InteractionRadius, BigTank);
        float fraction = RigidBodies3D.SubmergedFraction(data, 0, new float3(0, 1, 0), WetWhere(data, p => p.y < 0));
        Assert.AreEqual(0.5f, fraction, 0.06f);
    }

    /// <summary>A few wet particles on top, like a splash, don't outvote the real waterline</summary>
    [Test]
    public void SubmergedFraction_SplashOnTop_Ignored()
    {
        RigidBodyData3D data = RigidBodies3D.Build(new[] { Body(BodyShape3D.Cube, 10, 0) }, spawn, InteractionRadius, BigTank);
        float3[] rest = data.boundaryRestPositions.Select(p => p.xyz).ToArray();
        uint[] wet = WetWhere(data, p => p.y < 0);
        for (int i = 0, splashed = 0; i < rest.Length && splashed < 5; i++)
        {
            if (rest[i].y > 9)
            {
                wet[i] = 1;
                splashed++;
            }
        }

        Assert.AreEqual(0.5f, RigidBodies3D.SubmergedFraction(data, 0, new float3(0, 1, 0), wet), 0.06f);
    }
    #endregion

    #region Fluid inside bodies
    /// <summary>With no bodies the particles are returned as they are</summary>
    [Test]
    public void RemoveParticlesInsideBodies_NoBodies_SameArray()
    {
        float4[] positions = { new(1, 2, 3, 0) };
        Assert.AreSame(positions, RigidBodies3D.RemoveParticlesInsideBodies(positions, new List<RigidBodySettings3D>(), 1));
    }

    /// <summary>Particles inside or near a body are removed, ones far from it are kept</summary>
    [Test]
    public void RemoveParticlesInsideBodies_RemovesOnlyNearby()
    {
        float4[] positions = { new(0, 0, 0, 0), new(5.5f, 0, 0, 0), new(20, 0, 0, 0) };
        float4[] kept = RigidBodies3D.RemoveParticlesInsideBodies(positions, new List<RigidBodySettings3D> { Body(BodyShape3D.Sphere, 5, 0) }, 1);
        CollectionAssert.AreEqual(new[] { new float4(20, 0, 0, 0) }, kept);
    }

    /// <summary>Null bodies or particles are rejected</summary>
    [Test]
    public void RemoveParticlesInsideBodies_Null_Throws()
    {
        Assert.Catch(() => RigidBodies3D.RemoveParticlesInsideBodies(new float4[1], null, 1));
        Assert.Catch(() => RigidBodies3D.RemoveParticlesInsideBodies(null, new List<RigidBodySettings3D> { Body(BodyShape3D.Sphere, 5, 0) }, 1));
    }
    #endregion
}
