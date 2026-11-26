using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

/// <summary>SimulationAPI in real units, with the GPU side never started</summary>
public class SimulationAPITests
{
    /// <summary>The default unit scales: sim units per metre and the sim mass of 1 kg</summary>
    private const float LengthScale = 200, MassScale = 8000;
    /// <summary>Volumes in m³</summary>
    private const float OneLitre = 0.001f, TwoLitres = 0.002f;

    private SimulationRig rig;
    private SimulationAPI api;
    private SimulationManager manager;

    [SetUp]
    public void CreateRig()
    {
        rig = new SimulationRig();
        api = rig.api;
        manager = rig.manager;
    }

    [TearDown]
    public void DestroyRig() => rig.Dispose();

    private int AddCube(float mass = 1) => api.AddBody(BodyShape3D.Cube, OneLitre, mass);

    #region Units
    /// <summary>Metres and sim units convert both ways, at 200 sim units per metre</summary>
    [TestCase(1, 200)]
    [TestCase(0, 0)]
    [TestCase(-1.5f, -300)]
    [TestCase(0.001f, 0.2f)]
    public void Metres_ConvertBothWays(float metres, float sim)
    {
        Assert.AreEqual(sim, api.FromMetres(metres), 1e-4f);
        Assert.AreEqual(metres, api.ToMetres(sim), 1e-6f);
    }

    /// <summary>1 N is the sim mass of 1 kg under 1 m/s² of sim gravity</summary>
    [Test]
    public void ForceScale_IsMassTimesGravityScale()
    {
        Assert.AreEqual(MassScale * 20 / 9.81f, api.ForceScale, 0.1f);
    }

    /// <summary>Earth's gravity is 20 sim units down</summary>
    [Test]
    public void SetGravity_Earth_IsTwentySimUnitsDown()
    {
        api.SetGravity(9.81f);
        Assert.AreEqual(-20, manager.settings.gravity, 1e-4f);
        Assert.AreEqual(9.81f, api.GetGravity(), 1e-4f);
    }

    /// <summary>Gravity round trips at the UI's limits and in between</summary>
    [TestCase(0)]
    [TestCase(1.62f)]
    [TestCase(25)]
    public void Gravity_RoundTrips(float gravity)
    {
        api.SetGravity(gravity);
        Assert.AreEqual(gravity, api.GetGravity(), 1e-4f);
    }

    /// <summary>Water's density is a rest density of 1</summary>
    [Test]
    public void SetDensity_Water_IsRestDensityOne()
    {
        api.SetDensity(1000);
        Assert.AreEqual(1, manager.settings.restDensity, 1e-6f);
        Assert.AreEqual(1000, api.GetDensity(), 1e-3f);
    }

    /// <summary>Density and viscosity round trip at the UI's limits</summary>
    [TestCase(500, 0)]
    [TestCase(1500, 5)]
    [TestCase(1000, 1)]
    public void DensityAndViscosity_RoundTrip(float density, float viscosity)
    {
        api.SetDensity(density);
        api.SetViscosity(viscosity);
        Assert.AreEqual(density, api.GetDensity(), 1e-3f);
        Assert.AreEqual(viscosity, api.GetViscosity(), 1e-6f);
    }

    /// <summary>Pausing is passed to the manager, and no simulation time passes before it runs</summary>
    [Test]
    public void SetPausedAndTime()
    {
        api.SetPaused(true);
        Assert.IsTrue(manager.paused);
        api.SetPaused(false);
        Assert.IsFalse(manager.paused);
        Assert.AreEqual(0, api.GetSimulationTime());
    }
    #endregion

    #region Adding bodies
    /// <summary>A new body gets its mass in sim units and the size for its volume</summary>
    [Test]
    public void AddBody_StoresMassAndSize()
    {
        int id = api.AddBody(BodyShape3D.Cube, OneLitre, 2);
        RigidBodySettings3D body = manager.settings.bodies[id];

        Assert.IsTrue(api.HasBody(id));
        Assert.AreEqual(BodyShape3D.Cube, body.shape);
        Assert.AreEqual(10, body.bodyRadius, 1e-3f);
        Assert.AreEqual(2 * MassScale, body.boundaryBodyMass, 1e-2f);
    }

    /// <summary>A new body appears inside the tank, even with the camera outside it</summary>
    [Test]
    public void AddBody_SpawnsInsideTank()
    {
        int id = AddCube();
        RigidBodySettings3D body = manager.settings.bodies[id];
        float3 limit = manager.Tank.innerHalfSize - body.bodyRadius;
        Assert.IsTrue(math.all(math.abs(body.bodyPosition) <= limit + 1e-3f));
    }

    /// <summary>Bodies get the next free id, and a freed id is used again</summary>
    [Test]
    public void AddBody_ReusesFreedIds()
    {
        int first = AddCube();
        int second = AddCube();
        Assert.AreEqual(first + 1, second);

        api.RemoveBody(first);
        Assert.IsFalse(api.HasBody(first));
        Assert.AreEqual(first, AddCube());
    }

    /// <summary>There's room for MaxBodies bodies and no more</summary>
    [Test]
    public void AddBody_PastMax_Throws()
    {
        Assert.IsTrue(api.CanAddBody());
        for (int i = 0; i < RigidBodies3D.MaxBodies; i++)
            AddCube();

        Assert.IsFalse(api.CanAddBody());
        Assert.Throws<InvalidOperationException>(() => AddCube());
    }

    /// <summary>Removing one body from a full tank makes room again</summary>
    [Test]
    public void RemoveBody_WhenFull_MakesRoom()
    {
        for (int i = 0; i < RigidBodies3D.MaxBodies; i++)
            AddCube();

        api.RemoveBody(3);
        Assert.IsTrue(api.CanAddBody());
        Assert.AreEqual(3, AddCube());
    }

    /// <summary>A mass of 0 or less is rejected</summary>
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(float.NegativeInfinity)]
    public void AddBody_MassNotAboveZero_Throws(float mass)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => AddCube(mass));
        Assert.AreEqual("mass", error.ParamName);
        Assert.AreEqual(0, manager.settings.bodies.Length);
    }

    /// <summary>The smallest mass above 0 is accepted</summary>
    [Test]
    public void AddBody_TinyMass_Accepted()
    {
        Assert.DoesNotThrow(() => AddCube(float.Epsilon));
    }

    /// <summary>A volume of 0 or less is rejected</summary>
    [TestCase(0)]
    [TestCase(-0.001f)]
    public void AddBody_VolumeNotAboveZero_Throws(float volume)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => api.AddBody(BodyShape3D.Sphere, volume, 1));
        Assert.AreEqual("volume", error.ParamName);
    }

    /// <summary>A body bigger than the tank is rejected, one that just fits isn't</summary>
    [Test]
    public void AddBody_TooBigForTank_Throws()
    {
        // The tank's inner half size is 59.5 sim units, so a cube up to 119 / 200 m wide fits
        float fits = math.pow(118f / LengthScale, 3);
        float tooBig = math.pow(120f / LengthScale, 3);

        Assert.DoesNotThrow(() => api.AddBody(BodyShape3D.Cube, fits, 1));
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => api.AddBody(BodyShape3D.Cube, tooBig, 1));
        Assert.AreEqual("volume", error.ParamName);
    }

    /// <summary>A scale isn't a body shape with a volume</summary>
    [Test]
    public void AddBody_ScaleShape_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => api.AddBody(BodyShape3D.Scale, OneLitre, 1));
    }

    /// <summary>Not a number isn't a mass or a volume</summary>
    [Test, Category("KnownBug")]
    public void AddBody_NotANumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AddCube(float.NaN), "A NaN mass was accepted");
        Assert.Throws<ArgumentOutOfRangeException>(() => api.AddBody(BodyShape3D.Cube, float.NaN, 1), "A NaN volume was accepted");
    }
    #endregion

    #region Changing bodies
    /// <summary>SetBodyMass changes the sim mass</summary>
    [Test]
    public void SetBodyMass_Changes()
    {
        int id = AddCube();
        api.SetBodyMass(id, 80);
        Assert.AreEqual(80 * MassScale, manager.settings.bodies[id].boundaryBodyMass, 1);
    }

    /// <summary>SetBodyMass rejects a mass of 0 or less, and ids with no body</summary>
    [Test]
    public void SetBodyMass_BadInput_Throws()
    {
        int id = AddCube();
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetBodyMass(id, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetBodyMass(id, -2));
        Assert.Throws<ArgumentException>(() => api.SetBodyMass(99, 1));
        Assert.Throws<ArgumentException>(() => api.SetBodyMass(-1, 1));
    }

    /// <summary>SetBodyMass rejects not a number</summary>
    [Test, Category("KnownBug")]
    public void SetBodyMass_NotANumber_Throws()
    {
        int id = AddCube();
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetBodyMass(id, float.NaN), "A NaN mass was accepted");
    }

    /// <summary>SetBodyShape swaps the shape and resizes for the volume</summary>
    [Test]
    public void SetBodyShape_ToSphere()
    {
        int id = AddCube();
        api.SetBodyShape(id, BodyShape3D.Sphere, OneLitre);

        Assert.AreEqual(BodyShape3D.Sphere, manager.settings.bodies[id].shape);
        Assert.AreEqual(12.407f, manager.settings.bodies[id].bodyRadius, 1e-3f);
    }

    /// <summary>SetBodyShape rejects bad volumes, the scale shape and missing bodies</summary>
    [Test]
    public void SetBodyShape_BadInput_Throws()
    {
        int id = AddCube();
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetBodyShape(id, BodyShape3D.Cube, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetBodyShape(id, BodyShape3D.Scale, OneLitre));
        Assert.Throws<ArgumentException>(() => api.SetBodyShape(42, BodyShape3D.Cube, OneLitre));
    }

    /// <summary>Removing a body that isn't there is an error</summary>
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(99)]
    public void RemoveBody_Missing_Throws(int id)
    {
        Assert.Throws<ArgumentException>(() => api.RemoveBody(id));
    }

    /// <summary>HasBody is false for any id with no body</summary>
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(int.MaxValue)]
    public void HasBody_NoBody_False(int id)
    {
        Assert.IsFalse(api.HasBody(id));
    }

    /// <summary>A body dragged outside the tank is held against its walls</summary>
    [Test]
    public void MoveBody_OutsideTank_ClampedInside()
    {
        int id = api.AddBody(BodyShape3D.Sphere, OneLitre, 1);
        api.MoveBody(id, new Vector3(1000, -1000, 0));

        RigidBodySettings3D body = manager.settings.bodies[id];
        float3 walls = manager.Tank.innerHalfSize - manager.Spacing - body.bodyRadius;
        Assert.AreEqual(walls.x, body.bodyPosition.x, 1e-3f);
        Assert.AreEqual(-walls.y, body.bodyPosition.y, 1e-3f);
    }

    /// <summary>A body moved inside the tank goes exactly there</summary>
    [Test]
    public void MoveBody_InsideTank_Exact()
    {
        int id = api.AddBody(BodyShape3D.Sphere, OneLitre, 1);
        api.MoveBody(id, new Vector3(5, -3, 2));
        Assert.AreEqual(new float3(5, -3, 2), manager.settings.bodies[id].bodyPosition);
    }

    /// <summary>Moving or measuring a body that isn't there should say there's no such body</summary>
    [Test, Category("KnownBug")]
    public void MoveBodyAndBounds_MissingId_ThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => api.MoveBody(999, Vector3.zero), "MoveBody threw the wrong exception");
        Assert.Throws<ArgumentException>(() => api.GetBodyBounds(999), "GetBodyBounds threw the wrong exception");
    }

    /// <summary>Letting go of a body that isn't there is ignored, its window may have closed mid-drag</summary>
    [Test]
    public void ReleaseBody_Missing_Ignored()
    {
        Assert.DoesNotThrow(() => api.ReleaseBody(999));
    }
    #endregion

    #region Scales
    /// <summary>A placed scale is a body with the scale shape</summary>
    [Test]
    public void AddScale_IsAScaleBody()
    {
        api.AddScale(0);
        int id = api.GetScaleBodyId(0);

        Assert.IsTrue(api.HasBody(id));
        Assert.AreEqual(BodyShape3D.Scale, manager.settings.bodies[id].shape);
        Assert.AreEqual(0, api.GetScaleReading(0), "No reading has come back from the GPU yet");
    }

    /// <summary>Both scales can be out at once, with different bodies</summary>
    [Test]
    public void AddScale_Both()
    {
        api.AddScale(0);
        api.AddScale(RigidBodies3D.MaxScales - 1);
        Assert.AreNotEqual(api.GetScaleBodyId(0), api.GetScaleBodyId(1));
    }

    /// <summary>Scales are numbered 0 to MaxScales - 1</summary>
    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(int.MaxValue)]
    public void AddScale_OutOfRange_Throws(int scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => api.AddScale(scale));
    }

    /// <summary>The same scale can't be placed twice</summary>
    [Test]
    public void AddScale_Twice_Throws()
    {
        api.AddScale(0);
        Assert.Throws<InvalidOperationException>(() => api.AddScale(0));
    }

    /// <summary>A scale uses up a body slot</summary>
    [Test]
    public void AddScale_UsesBodySlot()
    {
        for (int i = 0; i < RigidBodies3D.MaxBodies - 1; i++)
            AddCube();
        api.AddScale(0);

        Assert.IsFalse(api.CanAddBody());
        Assert.Throws<InvalidOperationException>(() => api.AddScale(1));
    }

    /// <summary>A removed scale is gone, and one that isn't out can't be read or removed</summary>
    [Test]
    public void RemoveScale_ThenUnplacedThrows()
    {
        api.AddScale(0);
        api.RemoveScale(0);

        Assert.Throws<ArgumentException>(() => api.GetScaleBodyId(0));
        Assert.Throws<ArgumentException>(() => api.RemoveScale(0));
        Assert.Throws<ArgumentException>(() => api.GetScaleReading(1));
        Assert.DoesNotThrow(() => api.AddScale(0));
    }

    /// <summary>A scale dragged out of the tank stays fully inside it</summary>
    [Test]
    public void MoveBody_Scale_StaysInside()
    {
        api.AddScale(0);
        int id = api.GetScaleBodyId(0);
        api.MoveBody(id, new Vector3(1000, 1000, -1000));

        float3 position = manager.settings.bodies[id].bodyPosition;
        float3 inner = manager.Tank.innerHalfSize;
        Assert.AreEqual(inner.x - RigidBodies3D.ScaleHalfExtents.x, position.x, 1e-3f);
        Assert.AreEqual(-inner.z + RigidBodies3D.ScaleHalfExtents.z, position.z, 1e-3f);
        Assert.Less(position.y, inner.y);
    }

    /// <summary>Removing a scale's body directly has to free the scale too, so it can be placed again</summary>
    [Test, Category("KnownBug")]
    public void RemoveBody_OfScale_FreesScale()
    {
        api.AddScale(0);
        api.RemoveBody(api.GetScaleBodyId(0));
        Assert.DoesNotThrow(() => api.AddScale(0), "The scale still counts as placed after its body was removed");
    }
    #endregion

    #region Depth meters
    /// <summary>A new meter starts in the middle reading the spawned fluid's depth</summary>
    [Test]
    public void AddDepthMeter_ReadsSpawnedDepth()
    {
        Assert.IsFalse(api.HasDepthMeter(0));
        api.AddDepthMeter(0);

        Assert.IsTrue(api.HasDepthMeter(0));
        Assert.AreEqual(Vector3.zero, api.GetDepthMeterPosition(0));
        // Spawned surface at 20 plus the 60 below the tank's centre
        Assert.AreEqual(80 / LengthScale, api.GetDepthMeterReading(0), 1e-5f);
        Assert.AreEqual(0, api.GetDepthMeterHistory(0, 10).Length);
    }

    /// <summary>Meters move across the floor but keep their column inside the tank</summary>
    [Test]
    public void MoveDepthMeter_ClampedToFloor()
    {
        api.AddDepthMeter(1);
        api.MoveDepthMeter(1, 10, -5);
        Assert.AreEqual(new Vector3(10, 0, -5), api.GetDepthMeterPosition(1));

        api.MoveDepthMeter(1, 1000, -1000);
        float limit = manager.Tank.halfSize.x - 3 * manager.Spacing;
        Assert.AreEqual(new Vector3(limit, 0, -limit), api.GetDepthMeterPosition(1));
    }

    /// <summary>A meter's ray and grab box span the tank's height</summary>
    [Test]
    public void DepthMeterBounds_SpanTank()
    {
        api.AddDepthMeter(0);
        Bounds bounds = api.GetDepthMeterBounds(0, 16);
        Assert.AreEqual(manager.Tank.size.y, api.GetDepthRayLength());
        Assert.AreEqual(new Vector3(16, manager.Tank.size.y, 16), bounds.size);
    }

    /// <summary>A removed meter is gone, and meters outside 0 to MaxMeters - 1 don't exist</summary>
    [Test]
    public void DepthMeter_RemoveAndOutOfRange()
    {
        api.AddDepthMeter(DepthMeters.MaxMeters - 1);
        api.RemoveDepthMeter(DepthMeters.MaxMeters - 1);
        Assert.IsFalse(api.HasDepthMeter(DepthMeters.MaxMeters - 1));

        Assert.Catch(() => api.AddDepthMeter(DepthMeters.MaxMeters));
        Assert.Catch(() => api.HasDepthMeter(-1));
        Assert.Catch(() => api.GetDepthMeterReading(0), "Meter 0 was never added");
    }
    #endregion

    #region Wave dipper
    /// <summary>A scene without a wave dipper can't add one</summary>
    [Test]
    public void AddDipper_NoDipperInScene_Throws()
    {
        Assert.Catch(() => api.AddDipper());
    }
    #endregion
}
