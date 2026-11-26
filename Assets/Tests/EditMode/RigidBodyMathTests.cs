using NUnit.Framework;
using Unity.Mathematics;

/// <summary>RigidBodyMath3D: volumes, inertia, signed distances and submerged fractions of the body shapes</summary>
public class RigidBodyMathTests
{
    private const float Tolerance = 1e-4f;

    #region Volume and inertia
    /// <summary>A unit sphere's volume is 4/3 π</summary>
    [Test]
    public void SphereVolume_UnitSphere()
    {
        Assert.AreEqual(4f / 3 * math.PI, RigidBodyMath3D.SphereVolume(1), Tolerance);
        Assert.AreEqual(0, RigidBodyMath3D.SphereVolume(0));
    }

    /// <summary>Radius to volume and back gives the same radius</summary>
    [TestCase(0.5f)]
    [TestCase(1)]
    [TestCase(12.4f)]
    public void SphereRadiusFromVolume_RoundTrips(float radius)
    {
        Assert.AreEqual(radius, RigidBodyMath3D.SphereRadiusFromVolume(RigidBodyMath3D.SphereVolume(radius)), Tolerance);
    }

    /// <summary>A 1 L cube at 200 sim units per metre is 8000 sim units³ with a half edge of 10</summary>
    [Test]
    public void CubeHalfEdgeFromVolume_OneLitre()
    {
        Assert.AreEqual(10, RigidBodyMath3D.CubeHalfEdgeFromVolume(8000), Tolerance);
        Assert.AreEqual(0, RigidBodyMath3D.CubeHalfEdgeFromVolume(0));
    }

    /// <summary>A negative volume has no real cube root, so it isn't a number</summary>
    [Test]
    public void FromVolume_Negative_NotANumber()
    {
        Assert.IsTrue(float.IsNaN(RigidBodyMath3D.CubeHalfEdgeFromVolume(-8)));
        Assert.IsTrue(float.IsNaN(RigidBodyMath3D.SphereRadiusFromVolume(-8)));
    }

    /// <summary>The textbook moments of inertia</summary>
    [Test]
    public void Inertia_TextbookValues()
    {
        Assert.AreEqual(16, RigidBodyMath3D.SphereInertia(10, 2), Tolerance);
        Assert.AreEqual(4, RigidBodyMath3D.CubeInertia(6, 1), Tolerance);
        Assert.AreEqual(0, RigidBodyMath3D.SphereInertia(0, 2));
    }
    #endregion

    #region Signed distances
    /// <summary>Negative inside a box, zero on its surface, positive outside</summary>
    [TestCase(0, 0, 0, -1)]
    [TestCase(1, 0, 0, 0)]
    [TestCase(0, -1, 0, 0)]
    [TestCase(2, 0, 0, 1)]
    [TestCase(0.5f, 0, 0, -0.5f)]
    public void BoxSignedDistance_InsideSurfaceOutside(float x, float y, float z, float expected)
    {
        Assert.AreEqual(expected, RigidBodyMath3D.BoxSignedDistance(new float3(x, y, z), new float3(1)), Tolerance);
    }

    /// <summary>Off a box's corner the distance is to the corner</summary>
    [Test]
    public void BoxSignedDistance_PastCorner()
    {
        Assert.AreEqual(math.sqrt(2), RigidBodyMath3D.BoxSignedDistance(new float3(2, 2, 1), new float3(1)), Tolerance);
    }

    /// <summary>Negative inside a cylinder, zero on its side and caps, positive outside</summary>
    [TestCase(0, 0, 0, -1)]
    [TestCase(1, 0, 0, 0)]
    [TestCase(0, 1, 0, 0)]
    [TestCase(2, 0, 0, 1)]
    [TestCase(0, 3, 0, 2)]
    public void CylinderSignedDistance_InsideSurfaceOutside(float x, float y, float z, float expected)
    {
        Assert.AreEqual(expected, RigidBodyMath3D.CylinderSignedDistance(new float3(x, y, z), 1, 1), Tolerance);
    }
    #endregion

    #region Submerged volume
    /// <summary>A sphere is empty with the plane below it, half under at its centre and full above it</summary>
    [TestCase(-1, 0)]
    [TestCase(-0.5, 0)]
    [TestCase(0, 0.5)]
    [TestCase(0.25, 0.84375)]
    [TestCase(0.5, 1)]
    [TestCase(1, 1)]
    public void SphereFractionBelow(double depth, double expected)
    {
        Assert.AreEqual(expected, RigidBodyMath3D.SphereFractionBelow(depth), 1e-9);
    }

    /// <summary>A plane through a cube's centre cuts it in half whichever way the cube is turned</summary>
    [TestCase(0, 1, 0)]
    [TestCase(1, 1, 1)]
    [TestCase(1, 2, 3)]
    [TestCase(0, 0, -1)]
    public void CubeFractionBelow_ThroughCentre_Half(float x, float y, float z)
    {
        Assert.AreEqual(0.5, RigidBodyMath3D.CubeFractionBelow(0, math.normalize(new float3(x, y, z))), 1e-3);
    }

    /// <summary>A plane far below or above the cube leaves none or all of it under</summary>
    [Test]
    public void CubeFractionBelow_FarAway_NoneOrAll()
    {
        float3 tilted = math.normalize(new float3(1, 2, 3));
        Assert.AreEqual(0, RigidBodyMath3D.CubeFractionBelow(-2, tilted), 1e-9);
        Assert.AreEqual(1, RigidBodyMath3D.CubeFractionBelow(2, tilted), 1e-9);
    }

    /// <summary>A level plane a quarter edge above the centre leaves three quarters under</summary>
    [Test]
    public void CubeFractionBelow_Level_IsLinear()
    {
        Assert.AreEqual(0.75, RigidBodyMath3D.CubeFractionBelow(0.25, new float3(0, 1, 0)), 1e-3);
    }
    #endregion

    #region Rotated boxes
    /// <summary>An unrotated box reaches its half extents, one turned a quarter about z swaps x and y</summary>
    [Test]
    public void BoxReach_IdentityAndQuarterTurn()
    {
        float3 halfExtents = new(1, 2, 3);
        float3 reach = RigidBodyMath3D.BoxReach(float3x3.identity, halfExtents);
        Assert.AreEqual(halfExtents, reach);

        float3 turned = RigidBodyMath3D.BoxReach(float3x3.RotateZ(math.PI / 2), halfExtents);
        Assert.AreEqual(2, turned.x, Tolerance);
        Assert.AreEqual(1, turned.y, Tolerance);
        Assert.AreEqual(3, turned.z, Tolerance);
    }

    /// <summary>The box axis nearest a world axis, before and after a quarter turn about z</summary>
    [Test]
    public void MostAlignedAxis_IdentityAndQuarterTurn()
    {
        for (int axis = 0; axis < 3; axis++)
            Assert.AreEqual(axis, RigidBodyMath3D.MostAlignedAxis(float3x3.identity, axis));

        float3x3 turned = float3x3.RotateZ(math.PI / 2);
        Assert.AreEqual(1, RigidBodyMath3D.MostAlignedAxis(turned, 0));
        Assert.AreEqual(0, RigidBodyMath3D.MostAlignedAxis(turned, 1));
        Assert.AreEqual(2, RigidBodyMath3D.MostAlignedAxis(turned, 2));
    }
    #endregion
}
